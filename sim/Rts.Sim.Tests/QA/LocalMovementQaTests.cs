using System.Numerics;
using System.Reflection;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;
using Rts.Sim.Pathfinding;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>QA attacks on M1-4d-1 local movement: give-up abuse, back-off livelock, lone-walker give-ups, hash audit.</summary>
public class LocalMovementQaTests
{
    private readonly ITestOutputHelper _out;

    public LocalMovementQaTests(ITestOutputHelper output) => _out = output;

    private static int CountMoving(UnitStore u)
    {
        int n = 0;
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.State[i] == UnitState.Moving) n++;
        return n;
    }

    private static void SpawnAll(Simulation sim, params (int Type, Vector2 At)[] units)
    {
        foreach ((int type, Vector2 at) in units) sim.Enqueue(Command.SpawnUnit(0, type, at));
        sim.Tick();
        sim.Tick();
    }

    /// <summary>
    /// Call after the tick that queued the Moves: one tick applies them (and takes the first step),
    /// then ticks until no unit is Moving or <paramref name="limit"/> ticks pass; returns ticks used.
    /// </summary>
    private static int Settle(Simulation sim, int limit)
    {
        sim.Tick();
        int t = 1;
        while (CountMoving(sim.World.Units) > 0 && t < limit)
        {
            sim.Tick();
            t++;
        }
        return t;
    }

    /// <summary>A 16 x 16 flat map whose column x = 4 is a cliff (blocked) between rows 1 and 14.</summary>
    private static Heightmap WestWall()
    {
        var rows = new string[16];
        for (int y = 0; y < 16; y++) rows[y] = y >= 1 && y <= 14 ? "0000100000000000" : new string('0', 16);
        return LocalMovementTests.Rows(rows);
    }

    // ---------- BUG-0027: back-off never terminates ----------

    /// <summary>
    /// A unit at its goal against a wall, overlapped by an idle unit with no goal (spawned there, or
    /// one that gave up there). It is "crowded" so it may not stop; the back-off step pushes it into
    /// the wall and is refused, and back-off ticks never count toward giving up. It must still
    /// terminate within GiveUpTicks + slack (QA focus "give-up abuse": all terminate, never loop).
    /// </summary>
    [Fact]
    public void UnitAtGoal_OverlappedByIdleStranger_AgainstAWall_StillGoesIdle()
    {
        Simulation sim = LocalMovementTests.SimOn(WestWall(), 2);
        NavGrid g = sim.World.NavGrid;
        Assert.False(g.IsPassable(4, 5));
        Assert.True(g.IsPassable(5, 5));
        int type = LocalMovementTests.TypeWithRadius(0.4f);
        // Cell (5, 5) spans x 10..12. The walker hugs the cliff, the stranger is 0.1 m east of it.
        Vector2 walker = new(10.05f, 11f), stranger = new(10.15f, 11f);
        SpawnAll(sim, (type, walker), (type, stranger));
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), new Vector2(10.3f, 11f)));
        sim.Tick(); // queued
        UnitStore u = sim.World.Units;
        int ticks = Settle(sim, 10 * MovementConstants.GiveUpTicks);
        _out.WriteLine($"state {u.State[0]} after {ticks} ticks at {u.Position[0]}, stuck {u.StuckTicks[0]}");
        Assert.True(u.State[0] == UnitState.Idle,
            $"unit still Moving after {ticks} ticks at {u.Position[0]} (stuck counter {u.StuckTicks[0]}): back-off loops forever");
    }

    /// <summary>The same livelock without any wall: 50 units spawned on one point and ordered to that point.</summary>
    [Fact]
    public void FiftyUnitsSpawnedOnOnePoint_OrderedToThatPoint_AllTerminate()
    {
        Simulation sim = LocalMovementTests.SimOn(LocalMovementTests.Flat(32), 50);
        Vector2 p = new(31f, 31f);
        var units = new (int, Vector2)[50];
        for (int i = 0; i < units.Length; i++) units[i] = (i % TestSim.UnitTypeCount, p);
        SpawnAll(sim, units);
        MoveScenario.MoveAll(sim, p);
        sim.Tick();
        int ticks = Settle(sim, 600);
        UnitStore u = sim.World.Units;
        int gaveUp = 0;
        for (int i = 0; i < 50; i++) if (u.GoalCell[i] == -1) gaveUp++;
        _out.WriteLine($"50 coincident units to their own point: {CountMoving(u)} still Moving after {ticks} ticks, {gaveUp} gave up");
        for (int i = 0; i < 50; i++)
            if (u.State[i] == UnitState.Moving)
            {
                Vector2 before = u.Position[i];
                sim.Tick();
                _out.WriteLine($"  unit {i} r {u.Radius[i]} at {before} -> {u.Position[i]}, stuck {u.StuckTicks[i]}, {Vector2.Distance(before, p):F2} m from the goal");
            }
        Assert.Equal(0, CountMoving(u));
        Assert.Equal(-1, MoveScenario.FirstUnitOnBlockedGround(sim.World));
    }

    [Fact]
    public void FiftyUnitsSpawnedOnOnePoint_OrderedAway_AllTerminate_WithinTravelPlusGiveUp()
    {
        Simulation sim = LocalMovementTests.SimOn(LocalMovementTests.Flat(32), 50);
        Vector2 p = new(15f, 15f), goal = new(45f, 45f);
        var units = new (int, Vector2)[50];
        for (int i = 0; i < units.Length; i++) units[i] = (i % TestSim.UnitTypeCount, p);
        SpawnAll(sim, units);
        MoveScenario.MoveAll(sim, goal);
        sim.Tick();
        int ticks = Settle(sim, 2000);
        UnitStore u = sim.World.Units;
        int gaveUp = 0;
        for (int i = 0; i < 50; i++) if (u.GoalCell[i] == -1) gaveUp++;
        _out.WriteLine($"50 coincident units to a far point: idle after {ticks} ticks, {gaveUp} gave up");
        Assert.Equal(0, CountMoving(u));
        _out.WriteLine($"tightest pair: {MoveScenario.FirstPackViolation(sim.World, 0.5f) ?? "none under 0.5 x radii"}");
    }

    /// <summary>A goal cell packed with arrived units of another group: newcomers must stop (arrive or give up), never loop.</summary>
    [Fact]
    public void GoalCellFullOfAnotherGroupsArrivedUnits_NewcomersTerminate()
    {
        Simulation sim = LocalMovementTests.SimOn(LocalMovementTests.Flat(32), 40);
        int type = LocalMovementTests.TypeWithRadius(0.4f);
        Vector2 point = new(31f, 31f); // center of cell (15, 15)
        var first = new List<(int, Vector2)>();
        for (int i = 0; i < 20; i++) first.Add((type, new Vector2(20f + (i % 5) * 1.2f, 20f + (i / 5) * 1.2f)));
        SpawnAll(sim, first.ToArray());
        MoveScenario.MoveAll(sim, point);
        sim.Tick();
        Assert.True(Settle(sim, 600) < 600);
        // Second group, ordered to the same cell but a different point (another "goal").
        for (int i = 0; i < 20; i++) sim.Enqueue(Command.SpawnUnit(0, type, new Vector2(44f + (i % 5) * 1.2f, 44f + (i / 5) * 1.2f)));
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        for (int i = 20; i < 40; i++) sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, i), point + new Vector2(0.6f, 0.6f)));
        sim.Tick();
        int ticks = Settle(sim, 600);
        int arrived = 0, gaveUp = 0;
        bool[] a = MoveScenario.Arrived(sim.World);
        for (int i = 20; i < 40; i++) { if (a[i]) arrived++; else if (u.GoalCell[i] == -1) gaveUp++; }
        _out.WriteLine($"second group: idle after {ticks} ticks, arrived {arrived}, gave up {gaveUp}");
        Assert.Equal(0, CountMoving(u));
    }

    /// <summary>A unit boxed in by idle units that arrived at another goal (they keep GoalCell): gives up on time.</summary>
    [Fact]
    public void UnitBoxedInByArrivedUnitsOfAnotherGoal_GivesUpWithinGiveUpTicksPlusSlack()
    {
        int type = LocalMovementTests.TypeWithRadius(0.4f);
        Vector2 center = new(31f, 31f);
        var units = new List<(int, Vector2)> { (type, center) };
        for (int k = 0; k < 8; k++)
        {
            float a = k * MathF.PI / 4f;
            units.Add((type, center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * 0.8f));
        }
        Simulation sim = LocalMovementTests.SimOn(LocalMovementTests.Flat(32), units.Count);
        SpawnAll(sim, units.ToArray());
        UnitStore u = sim.World.Units;
        // The ring "arrives" at its own positions, so each keeps a real GoalCell.
        for (int i = 1; i < units.Count; i++) sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, i), u.Position[i]));
        sim.Tick();
        sim.Tick();
        for (int i = 1; i < units.Count; i++) Assert.Equal(UnitState.Idle, u.State[i]);
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), center + new Vector2(20f, 0f)));
        sim.Tick();
        int ticks = Settle(sim, 200);
        _out.WriteLine($"boxed unit idle after {ticks} ticks, goal cell {u.GoalCell[0]}");
        Assert.True(ticks <= MovementConstants.GiveUpTicks + 3, $"{ticks} ticks");
        Assert.Equal(-1, u.GoalCell[0]);
    }

    /// <summary>
    /// A settled blob re-ordered to the same point every tick (click spam, or an AI re-issuing its
    /// order): report how much it churns. Before M1-4d-1 re-ordered units settled again within a tick.
    /// </summary>
    [Fact]
    public void SettledBlob_ReorderedToSamePointEveryTick_Churn_Report()
    {
        Simulation sim = MoveScenario.Spawn(seed: 3, units: 20, maxCost: 40f, out int goalCell);
        Vector2 goal = MoveScenario.Center(sim.World.NavGrid, goalCell);
        MoveScenario.MoveAll(sim, goal);
        sim.Tick();
        Assert.True(Settle(sim, 1200) < 1200);
        UnitStore u = sim.World.Units;
        float travelled = 0f;
        int maxMoving = 0, movingAtEnd = 0;
        for (int t = 0; t < 200; t++)
        {
            MoveScenario.MoveAll(sim, goal);
            sim.Tick();
            if (t < 100) continue; // let the first re-orders play out
            int moving = CountMoving(u);
            maxMoving = Math.Max(maxMoving, moving);
            movingAtEnd = moving;
            for (int i = 0; i < u.Capacity; i++) travelled += Vector2.Distance(u.Position[i], u.PrevPosition[i]);
        }
        _out.WriteLine($"spam ticks 100-200: up to {maxMoving}/20 Moving (at the end {movingAtEnd}), {travelled:F1} m walked in total by a blob that had already arrived");
    }

    // ---------- lone walkers must never give up ----------

    /// <summary>
    /// One unit, no other unit on the map, random reachable goals on generated maps: it must always
    /// arrive, never give up (a give-up with no units around is a false positive of the stuck measure).
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void LoneUnit_RandomReachableGoals_AlwaysArrives_NeverGivesUp(int typeIndex)
    {
        int type = typeIndex % TestSim.UnitTypeCount;
        int falseGiveUps = 0, runs = 0, worstStuck = 0;
        string? firstFail = null;
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var sim = new Simulation(TestSim.Config(seed, 1, UnitCapacity: 1, CommandCapacity: 8));
            NavGrid g = sim.World.NavGrid;
            List<int> passable = FlowFieldOracle.PassableCells(g);
            var rng = new Determinism.SimRng(seed, 4242);
            sim.Enqueue(Command.SpawnUnit(0, type, MoveScenario.Center(g, passable[rng.NextInt(0, passable.Count)])));
            sim.Tick();
            sim.Tick();
            UnitStore u = sim.World.Units;
            for (int k = 0; k < 15; k++)
            {
                g.WorldToCell(u.Position[0], out int sx, out int sy);
                int goalCell = passable[rng.NextInt(0, passable.Count)];
                float cost = FlowField.Build(g, goalCell).CostAt(sy * g.Width + sx);
                if (!float.IsFinite(cost)) continue; // unreachable: not this test's subject
                Vector2 goal = MoveScenario.Center(g, goalCell) + new Vector2(rng.NextFloat() - 0.5f, rng.NextFloat() - 0.5f);
                sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), goal));
                sim.Tick();
                int limit = (int)(cost * 2f * MapConstants.CellSize / u.Speed[0]) + 200;
                int t = 0;
                while (t < 3 || (u.State[0] == UnitState.Moving && t < limit))
                {
                    sim.Tick();
                    worstStuck = Math.Max(worstStuck, u.StuckTicks[0]);
                    t++;
                }
                runs++;
                if (u.State[0] != UnitState.Idle || u.GoalCell[0] == -1)
                {
                    falseGiveUps++;
                    firstFail ??= $"seed {seed} order {k}: {u.State[0]}, goal cell {u.GoalCell[0]}, at {u.Position[0]}, goal {goal}, {Vector2.Distance(u.Position[0], goal):F2} m short";
                }
            }
        }
        _out.WriteLine($"type {type}: {runs} lone orders, {falseGiveUps} failed, worst stuck counter {worstStuck}; first: {firstFail}");
        Assert.True(falseGiveUps == 0, $"{falseGiveUps}/{runs} lone orders gave up or never stopped; first: {firstFail}");
    }

    // ---------- hash audit ----------

    /// <summary>
    /// Every per-unit array on UnitStore is either in StateHash or one of the documented derived
    /// arrays (Speed, Radius follow from TypeId). Mutating one element of a live unit must change the hash.
    /// </summary>
    [Fact]
    public void EveryUnitStoreArray_IsHashed_OrDocumentedDerived()
    {
        var derived = new HashSet<string> { "Speed", "Radius" };
        Simulation sim = MoveScenario.Spawn(5, units: 2, maxCost: 10f, out _);
        UnitStore u = sim.World.Units;
        ulong h0 = sim.StateHash();
        var unhashed = new List<string>();
        int checkedArrays = 0;
        foreach (FieldInfo f in typeof(UnitStore).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (!f.FieldType.IsArray || f.Name.StartsWith("_")) continue;
            if (derived.Contains(f.Name)) continue;
            var arr = (Array)f.GetValue(u)!;
            if (arr.Length != u.Capacity) continue;
            object? old = arr.GetValue(1);
            object changed = old switch
            {
                int x => x + 1,
                float x => float.IsFinite(x) ? x + 1.5f : 2.5f,
                bool x => !x,
                Vector2 x => x + new Vector2(0.25f, 0f),
                UnitState x => x == UnitState.Idle ? UnitState.Moving : UnitState.Idle,
                _ => throw new InvalidOperationException($"{f.Name}: element type {f.FieldType} not covered by the audit"),
            };
            arr.SetValue(changed, 1);
            if (sim.StateHash() == h0) unhashed.Add(f.Name);
            arr.SetValue(old, 1);
            Assert.Equal(h0, sim.StateHash());
            checkedArrays++;
        }
        _out.WriteLine($"{checkedArrays} UnitStore arrays audited");
        Assert.Contains("StuckTicks", typeof(UnitStore).GetFields().Select(x => x.Name));
        Assert.Contains("BestRemaining", typeof(UnitStore).GetFields().Select(x => x.Name));
        Assert.True(unhashed.Count == 0, "not in StateHash: " + string.Join(", ", unhashed));
    }

    /// <summary>
    /// A unit retargeted mid-walk to a farther goal must not inherit the old order's best-remaining
    /// estimate (mutation target: drop the BestRemaining reset in ApplyMove and this must fail).
    /// </summary>
    [Fact]
    public void Retarget_MidWalk_ToAFartherGoal_DoesNotInheritProgressState()
    {
        Simulation sim = LocalMovementTests.SimOn(LocalMovementTests.Flat(32), 1);
        int type = LocalMovementTests.TypeWithRadius(0.9f);
        SpawnAll(sim, (type, new Vector2(31f, 31f)));
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), new Vector2(41f, 31f)));
        sim.Tick();
        for (int t = 0; t < 15; t++) sim.Tick();
        Assert.Equal(UnitState.Moving, u.State[0]);
        Vector2 far = new(5f, 57f);
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), far));
        sim.Tick();
        int ticks = Settle(sim, 3000);
        _out.WriteLine($"retargeted unit idle after {ticks} ticks at {u.Position[0]}");
        Assert.True(Vector2.Distance(u.Position[0], far) <= MovementConstants.ArrivalDistance,
            $"stopped {Vector2.Distance(u.Position[0], far):F2} m short (goal cell {u.GoalCell[0]})");
        Assert.NotEqual(-1, u.GoalCell[0]);
    }
}
