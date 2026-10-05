using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA re-check of the M1-4d-1 round-1 fixes: back-off counts toward giving up and keeps the goal
/// (BUG-0027), and a Move to the unit's current goal cell is "the same order" (BUG-0029).
/// Attacks: anchors that are not linked to their point, units that can't be re-ordered, same-cell
/// spam termination and determinism, allocation under spam.
/// </summary>
public class LocalMovementRecheckQaTests
{
    private readonly ITestOutputHelper _out;

    public LocalMovementRecheckQaTests(ITestOutputHelper output) => _out = output;

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

    /// <summary>Call right after queuing Moves: one tick applies them (Moves apply at the start of the tick after the one they were queued in), then ticks until nothing is Moving or the limit passes; returns ticks used.</summary>
    private static int Settle(Simulation sim, int limit)
    {
        int t = 0;
        sim.Tick();
        do { sim.Tick(); t++; } while (CountMoving(sim.World.Units) > 0 && t < limit);
        return t;
    }

    private static int SlowestType()
    {
        int best = 0;
        for (int t = 1; t < TestSim.UnitTypeCount; t++)
            if (TestSim.Data.Units[t].SpeedPerTick < TestSim.Data.Units[best].SpeedPerTick) best = t;
        return best;
    }

    /// <summary>
    /// Idle units that still hold a goal cell but are not linked to their point (MoveScenario.Arrived):
    /// stray anchors that later units would pack against, away from the goal.
    /// </summary>
    private static int StrayAnchors(World w, out string? first)
    {
        bool[] arrived = MoveScenario.Arrived(w);
        UnitStore u = w.Units;
        int n = 0;
        first = null;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.State[i] != UnitState.Idle || u.GoalCell[i] < 0 || arrived[i]) continue;
            n++;
            first ??= $"unit {i} at {u.Position[i]}, {Vector2.Distance(u.Position[i], u.Goal[i]):F2} m from its point";
        }
        return n;
    }

    // ---------- BUG-0029 rule: "same goal cell = same order" ----------

    /// <summary>
    /// A lone unit that arrived near one corner of a 2 m cell, ordered to the opposite corner of that
    /// same cell (about 2.5 m away). A player's short repositioning order must move it.
    /// </summary>
    [Fact(Skip = "BUG-0030: a Move to another point of an arrived unit's goal cell is ignored; un-skip when fixed")]
    public void ArrivedLoneUnit_OrderedToTheOppositeCornerOfItsGoalCell_MovesThere()
    {
        Simulation sim = LocalMovementTests.SimOn(LocalMovementTests.Flat(32), 1);
        int type = LocalMovementTests.TypeWithRadius(0.4f);
        SpawnAll(sim, (type, new Vector2(21f, 31f)));
        UnitStore u = sim.World.Units;
        // Cell (15, 15) spans 30..32 on both axes.
        Vector2 a = new(30.1f, 30.1f), b = new(31.9f, 31.9f);
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), a));
        Settle(sim, 600);
        Assert.Equal(UnitState.Idle, u.State[0]);
        Assert.True(Vector2.Distance(u.Position[0], a) <= MovementConstants.ArrivalDistance);
        Vector2 before = u.Position[0];
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), b));
        int ticks = Settle(sim, 600);
        _out.WriteLine($"after the second order: {u.State[0]} at {u.Position[0]} (was {before}), {Vector2.Distance(u.Position[0], b):F2} m from b, {ticks} ticks");
        Assert.True(Vector2.Distance(u.Position[0], b) <= MovementConstants.ArrivalDistance,
            $"stayed {Vector2.Distance(u.Position[0], b):F2} m from the new point (moved {Vector2.Distance(u.Position[0], before):F2} m)");
    }

    /// <summary>
    /// The same order given while the unit is still walking (it is in the goal cell, walking at corner a):
    /// it takes the new point and must still arrive there, for every unit type (slowest included),
    /// despite keeping the old order's progress estimate.
    /// </summary>
    [Fact]
    public void MovingUnit_RetargetedInsideItsGoalCell_ToTheOppositeCorner_Arrives_EveryType()
    {
        for (int type = 0; type < TestSim.UnitTypeCount; type++)
        {
            Simulation sim = LocalMovementTests.SimOn(LocalMovementTests.Flat(32), 1);
            SpawnAll(sim, (type, new Vector2(30.1f, 41f)));
            UnitStore u = sim.World.Units;
            Vector2 a = new(30.1f, 30.1f), b = new(31.9f, 30.1f);
            sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), a));
            sim.Tick(); // queued
            sim.Tick(); // applies
            // Walk south until inside the goal cell (y < 32), about 1.9 m from a, not yet arrived.
            int t = 0;
            while (u.Position[0].Y > 31.98f && u.State[0] == UnitState.Moving && t < 600) { sim.Tick(); t++; }
            Assert.Equal(UnitState.Moving, u.State[0]);
            sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), b));
            int ticks = Settle(sim, 600);
            float d = Vector2.Distance(u.Position[0], b);
            _out.WriteLine($"type {type} (speed {u.Speed[0]:F3}/tick): {u.State[0]} after {ticks} ticks, {d:F2} m from b, goal cell {u.GoalCell[0]}");
            Assert.True(d <= MovementConstants.ArrivalDistance, $"type {type}: stopped {d:F2} m from b (goal cell {u.GoalCell[0]})");
        }
    }

    /// <summary>
    /// A unit stopped by the back-off limit keeps its goal cell, so the same order is now a no-op;
    /// a Move to any other cell must still restart it.
    /// </summary>
    [Fact]
    public void UnitStoppedAtBackOffLimit_KeepsItsGoal_AndAMoveToAnotherCellStillRestartsIt()
    {
        var rows = new string[16];
        for (int y = 0; y < 16; y++) rows[y] = y >= 1 && y <= 14 ? "0000100000000000" : new string('0', 16);
        Simulation sim = LocalMovementTests.SimOn(LocalMovementTests.Rows(rows), 2);
        int type = LocalMovementTests.TypeWithRadius(0.4f);
        SpawnAll(sim, (type, new Vector2(10.05f, 11f)), (type, new Vector2(10.15f, 11f)));
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), new Vector2(10.3f, 11f)));
        Settle(sim, 200);
        Assert.Equal(UnitState.Idle, u.State[0]);
        int kept = u.GoalCell[0];
        Assert.NotEqual(-1, kept);
        // Same order again: stays put, no restart.
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), new Vector2(10.3f, 11f)));
        sim.Tick();
        sim.Tick();
        Assert.Equal(UnitState.Idle, u.State[0]);
        // The stranger leaves first (while it overlaps, unit 0 is pinned to the cliff: BUG-0031).
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 1), new Vector2(25f, 25f)));
        Settle(sim, 600);
        Assert.True(Vector2.Distance(u.Position[1], u.Position[0]) > 2f);
        Assert.Equal(UnitState.Idle, u.State[0]);
        // Another cell: it walks there and arrives.
        Vector2 away = new(21f, 21f);
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), away));
        int ticks = Settle(sim, 600);
        Assert.True(Vector2.Distance(u.Position[0], away) <= MovementConstants.ArrivalDistance, $"after {ticks} ticks at {u.Position[0]}");
        Assert.NotEqual(kept, u.GoalCell[0]);
    }

    /// <summary>
    /// A unit against a cliff, overlapped by a standing unit (no goal) 0.1 m east of it, is ordered
    /// away along the wall (north or south) or east. BUG-0031: Constrain asked every candidate step to
    /// clear the whole overlap with the standing unit, which points into the cliff, so every step was
    /// refused and the unit gave up whatever the order (fixed in M1-4d-2).
    /// </summary>
    [Theory]
    [InlineData(10.5f, 25f)]
    [InlineData(10.5f, 3f)]
    [InlineData(25f, 11f)]
    public void UnitOverlappingAStandingUnit_WithACliffBehind_CanStillWalkAway(float tx, float ty)
    {
        var rows = new string[16];
        for (int y = 0; y < 16; y++) rows[y] = y >= 1 && y <= 14 ? "0000100000000000" : new string('0', 16);
        Simulation sim = LocalMovementTests.SimOn(LocalMovementTests.Rows(rows), 2);
        int type = LocalMovementTests.TypeWithRadius(0.4f);
        SpawnAll(sim, (type, new Vector2(10.05f, 11f)), (type, new Vector2(10.15f, 11f)));
        UnitStore u = sim.World.Units;
        Vector2 target = new(tx, ty);
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), target));
        int ticks = Settle(sim, 2000);
        float moved = Vector2.Distance(u.Position[0], new Vector2(10.05f, 11f));
        _out.WriteLine($"to {target}: {u.State[0]} after {ticks} ticks at {u.Position[0]}, moved {moved:F2} m, goal cell {u.GoalCell[0]}");
        Assert.True(Vector2.Distance(u.Position[0], target) <= MovementConstants.ArrivalDistance,
            $"stopped {Vector2.Distance(u.Position[0], target):F2} m from {target} after moving {moved:F2} m (goal cell {u.GoalCell[0]})");
    }

    // ---------- BUG-0027 fix: units that keep their goal at the back-off limit ----------

    /// <summary>A packed square formation (centers 2r apart, touching) of <paramref name="side"/>² units.</summary>
    private static Simulation Formation(int side, float radius, Vector2 origin, int mapSize)
    {
        Simulation sim = LocalMovementTests.SimOn(LocalMovementTests.Flat(mapSize), side * side);
        int type = LocalMovementTests.TypeWithRadius(radius);
        var units = new (int, Vector2)[side * side];
        for (int k = 0; k < units.Length; k++)
            units[k] = (type, origin + new Vector2(k % side, k / side) * (2f * radius));
        SpawnAll(sim, units);
        return sim;
    }

    /// <summary>
    /// After every crowd scenario settles, every Idle unit that kept a goal cell must be linked to its
    /// point (a chain of touching groupmates to one within ArrivalDistance). A unit keeping its goal
    /// at the back-off limit takes that tick's back-off step first, which could leave it detached:
    /// a stray anchor that later units would pack against, away from the goal. Also reports the
    /// arrived / gave-up split per scenario.
    /// </summary>
    [Theory]
    [InlineData("coincident50")]
    [InlineData("formation10x10_toOwnCenter")]
    [InlineData("formation20x20_toOwnCenter")]
    [InlineData("crowd300_toCliffCorner")]
    [InlineData("blob200_nudgedOneCell")]
    [InlineData("crowd500_onePoint")]
    [InlineData("funnel150_justPastAOneCellGap")]
    public void AfterCrowdsSettle_EveryIdleUnitHoldingAGoal_IsLinkedToItsPoint(string scenario)
    {
        Simulation sim;
        Vector2 goal;
        switch (scenario)
        {
            case "coincident50":
            {
                sim = LocalMovementTests.SimOn(LocalMovementTests.Flat(32), 50);
                goal = new Vector2(31f, 31f);
                var units = new (int, Vector2)[50];
                for (int i = 0; i < 50; i++) units[i] = (i % TestSim.UnitTypeCount, goal);
                SpawnAll(sim, units);
                break;
            }
            case "formation10x10_toOwnCenter":
                sim = Formation(10, 0.4f, new Vector2(27.4f, 27.4f), 48);
                goal = new Vector2(31f, 31f);
                break;
            case "formation20x20_toOwnCenter":
                sim = Formation(20, 0.4f, new Vector2(23.4f, 23.4f), 48);
                goal = new Vector2(31f, 31f);
                break;
            case "crowd300_toCliffCorner":
            {
                // A cliff block occupying cells x 20..27, y 20..27; the goal hugs its south-west corner outside.
                var rows = new string[48];
                for (int y = 0; y < 48; y++)
                {
                    char[] r = new string('0', 48).ToCharArray();
                    if (y >= 20 && y <= 27) for (int x = 20; x <= 27; x++) r[x] = '1';
                    rows[y] = new string(r);
                }
                sim = new Simulation(TestSim.Config(Seed: 5, PlayerCount: 1, UnitCapacity: 300, CommandCapacity: 1300), LocalMovementTests.Rows(rows));
                goal = new Vector2(19 * 2f + 1.9f, 19 * 2f + 1.9f); // cell (19, 19), its corner touching the block
                var rng = new SimRng(5, 9);
                var units = new (int, Vector2)[300];
                for (int i = 0; i < 300; i++)
                    units[i] = (rng.NextInt(0, TestSim.UnitTypeCount), new Vector2(4f + rng.NextFloat() * 30f, 4f + rng.NextFloat() * 30f));
                SpawnAll(sim, units);
                break;
            }
            case "blob200_nudgedOneCell":
            {
                sim = MoveScenario.Spawn(seed: 41, units: 200, maxCost: 25f, out int gc);
                Vector2 c = MoveScenario.Center(sim.World.NavGrid, gc);
                MoveScenario.MoveAll(sim, c);
                Assert.True(Settle(sim, 3000) < 3000);
                goal = c + new Vector2(MapConstants.CellSize, 0f);
                break;
            }
            case "crowd500_onePoint":
            {
                sim = MoveScenario.Spawn(seed: 77, units: 500, maxCost: 40f, out int gc);
                goal = MoveScenario.Center(sim.World.NavGrid, gc) + new Vector2(0.6f, -0.3f);
                break;
            }
            case "funnel150_justPastAOneCellGap":
            {
                // A cliff wall at x = 20 with a 1-cell gap at row 12; the goal is the cell just east of the gap.
                var rows = new string[24];
                for (int y = 0; y < 24; y++)
                {
                    char[] r = new string('0', 40).ToCharArray();
                    if (y != 12) r[20] = '1';
                    rows[y] = new string(r);
                }
                sim = new Simulation(TestSim.Config(Seed: 8, PlayerCount: 1, UnitCapacity: 150, CommandCapacity: 700), LocalMovementTests.Rows(rows));
                goal = new Vector2(21 * 2f + 0.5f, 12 * 2f + 1f);
                var rng = new SimRng(8, 2);
                var units = new (int, Vector2)[150];
                for (int i = 0; i < 150; i++)
                    units[i] = (rng.NextInt(0, TestSim.UnitTypeCount), new Vector2(2.5f + rng.NextFloat() * 34f, 2.5f + rng.NextFloat() * 42f));
                SpawnAll(sim, units);
                break;
            }
            default:
                throw new ArgumentException(scenario);
        }
        Assert.True(sim.World.NavGrid.WorldToCell(goal, out int gx, out int gy) && sim.World.NavGrid.IsPassable(gx, gy), "goal on passable ground");
        MoveScenario.MoveAll(sim, goal);
        int ticks = Settle(sim, 4000);
        World w = sim.World;
        UnitStore u = w.Units;
        bool[] arrived = MoveScenario.Arrived(w);
        int arrivedCount = 0, gaveUp = 0;
        for (int i = 0; i < u.Capacity; i++) { if (arrived[i]) arrivedCount++; else if (u.GoalCell[i] == -1) gaveUp++; }
        int stray = StrayAnchors(w, out string? firstStray);
        _out.WriteLine($"{scenario}: {CountMoving(u)} moving after {ticks} ticks; arrived {arrivedCount}, gave up {gaveUp}, stray anchors {stray}; pack: {MoveScenario.FirstPackViolation(w) ?? "ok"}");
        Assert.Equal(0, CountMoving(u));
        Assert.Equal(-1, MoveScenario.FirstUnitOnBlockedGround(w));
        Assert.True(stray == 0, $"{stray} Idle units keep a goal cell but are not linked to their point; first: {firstStray}");
    }

    // ---------- same-cell spam: termination, determinism, allocation ----------

    /// <summary>
    /// 60 units walk to one cell while every Moving unit gets a Move to a random point of that cell
    /// every tick (an AI refreshing with jitter). Every unit must stop in bounded time, nobody may
    /// stand on blocked ground, and two runs must hash equal every tick.
    /// </summary>
    [Fact]
    public void RandomPointOfTheSameGoalCell_SpammedEveryTick_AllTerminate_Deterministic()
    {
        Simulation Make() => MoveScenario.Spawn(seed: 64, units: 60, maxCost: 20f, out _);
        Simulation s1 = Make(), s2 = Make();
        int goalCell = MoveScenario.CentralCell(s1.World.NavGrid);
        Vector2 corner = MoveScenario.Center(s1.World.NavGrid, goalCell) - new Vector2(MapConstants.CellSize / 2f);
        var r1 = new SimRng(64, 3);
        var r2 = new SimRng(64, 3);
        void Spam(Simulation s, SimRng r)
        {
            UnitStore u = s.World.Units;
            for (int i = 0; i < u.Capacity; i++)
                if (u.Alive[i] && (s.World.TickNumber < 3 || u.State[i] == UnitState.Moving))
                    s.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(s, i), corner + new Vector2(0.02f + r.NextFloat() * 1.96f, 0.02f + r.NextFloat() * 1.96f)));
        }
        int t = 0;
        do
        {
            Spam(s1, r1);
            Spam(s2, r2);
            s1.Tick();
            s2.Tick();
            t++;
            Assert.Equal(s1.StateHash(), s2.StateHash());
            Assert.Equal(-1, MoveScenario.FirstUnitOnBlockedGround(s1.World));
        } while ((t < 3 || CountMoving(s1.World.Units) > 0) && t < 2000);
        UnitStore u1 = s1.World.Units;
        int gaveUp = 0;
        for (int i = 0; i < u1.Capacity; i++) if (u1.GoalCell[i] == -1) gaveUp++;
        _out.WriteLine($"same-cell jitter spam: all Idle after {t} ticks, {gaveUp} of 60 gave up, stray anchors {StrayAnchors(s1.World, out _)}");
        Assert.Equal(0, CountMoving(u1));
    }

    /// <summary>Allocation: a settled 500-unit blob re-ordered to the same point every tick (the no-op path).</summary>
    [Collection(SerialCollection.Name)]
    public class Serial
    {
        private readonly ITestOutputHelper _out;

        public Serial(ITestOutputHelper output) => _out = output;

        [Fact]
        public void SettledBlobOf500_SameMoveEveryTick_AllocatesNothing()
        {
            Simulation sim = MoveScenario.Spawn(seed: 12, units: 500, maxCost: 40f, out int goalCell);
            Vector2 goal = MoveScenario.Center(sim.World.NavGrid, goalCell);
            MoveScenario.MoveAll(sim, goal);
            int t = 0;
            sim.Tick();
            do { sim.Tick(); t++; } while (CountMoving(sim.World.Units) > 0 && t < 3000);
            Assert.Equal(0, CountMoving(sim.World.Units));
            UnitStore u = sim.World.Units;
            Action enqueue = () =>
            {
                for (int i = 0; i < u.Capacity; i++) sim.Enqueue(Command.Move(u.Owner[i], new EntityHandle(i, u.Generation[i]), goal));
            };
            Action tick = () => sim.Tick();
            for (int k = 0; k < 5; k++) { enqueue(); sim.Tick(); }
            AllocationProbe.AssertZero(tick, _out, enqueue);
            Assert.Equal(0, CountMoving(u));
        }
    }
}
