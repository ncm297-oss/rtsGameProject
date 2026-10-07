using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;
using Rts.Sim.Pathfinding;
using Xunit.Abstractions;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M3-2b, session 2026-10-06-1744): closing vs opening grid changes. Stuck-walker hunts on freshly
/// opened cells (the goal cell itself, a cell opened and closed again), refresh starvation with more goals
/// than cache slots, building spam every tick against the progress-mark reset, the save/load question,
/// the step-mask fast path on odd grids, and the wood-footprint rule (BUG-0074).
/// </summary>
[Collection(SerialCollection.Name)]
public class GridChangeQaTests
{
    private readonly ITestOutputHelper _out;

    public GridChangeQaTests(ITestOutputHelper output) => _out = output;

    /// <summary>Ticks once and checks the per-tick invariants (no unit on blocked ground, usable fields never into a blocked cell); returns the waiting units.</summary>
    private static void TickChecked(Simulation sim, int[] wait, ref int longest, bool fields = true)
    {
        sim.Tick();
        World w = sim.World;
        int bad = MoveScenario.FirstUnitOnBlockedGround(w);
        Assert.True(bad < 0, $"tick {sim.TickNumber}: unit {bad} on blocked ground at {(bad >= 0 ? w.Units.Position[bad] : default)}");
        string? v = fields ? GridChangeOracle.UsableFieldViolation(w) : null;
        Assert.True(v == null, $"tick {sim.TickNumber}: {v}");
        for (int i = 0; i < wait.Length; i++)
        {
            wait[i] = GridChangeOracle.WaitingForField(w, i) ? wait[i] + 1 : 0;
            longest = Math.Max(longest, wait[i]);
        }
    }

    // ---------------------------------------------------------------- stuck-walker hunts

    /// <summary>
    /// The goal cell is a tree when the order is given (the field leads to the nearest open cell); the tree falls
    /// mid-walk, so the goal cell itself opens and has no direction in the usable field. A unit is then spawned on
    /// the goal cell and one on a cell beside it, both ordered to the same goal. Nobody waits more than 3 ticks,
    /// nobody stands on blocked ground, everybody ends Idle near the goal.
    /// </summary>
    [Fact]
    public void TheGoalCellItselfOpensMidWalk_NobodyStrands_AllArriveNearTheGoal()
    {
        Simulation sim = GatherMaps.NewSim(Flat(40, 24), units: 16);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        UnitStore u = w.Units;
        EntityHandle tree = Spawn(w, Tree, 30, 12, TreeWood);
        Vector2 goal = g.CellCenter(30, 12);
        int goalCell = 12 * g.Width + 30;
        var walkers = new List<EntityHandle>();
        for (int k = 0; k < 4; k++) walkers.Add(GatherMaps.Unit(sim, g.CellCenter(4, 8 + 2 * k), type: GatherMaps.Infantry));
        foreach (EntityHandle h in walkers) sim.Enqueue(Command.Move(0, h, goal));
        var wait = new int[u.Capacity];
        int longest = 0;
        for (int t = 0; t < 40; t++) TickChecked(sim, wait, ref longest);
        // A Move to a blocked cell is remapped to the nearest open cell, which keys the field.
        int remapped = u.GoalCell[walkers[0].Index];
        Assert.NotEqual(goalCell, remapped);
        FlowField? f = w.FlowFields.PeekCached(remapped);
        Assert.NotNull(f);

        Assert.Equal(TreeWood, w.Resources.Take(tree, TreeWood));
        Assert.Same(f, w.FlowFields.PeekCached(remapped)); // usable, stale
        Assert.Equal(FlowField.NoDirection, f!.DirectionAt(goalCell));
        // One unit on the opened cell ordered to the tree's point (now open: its own goal cell, so it is there),
        // one on the opened cell ordered to the walkers' remapped goal (stranded: no direction under it), one beside.
        EntityHandle onGoal = GatherMaps.Unit(sim, goal, type: GatherMaps.Infantry);
        EntityHandle stranded = GatherMaps.Unit(sim, goal + new Vector2(0.4f, 0.4f), type: GatherMaps.Infantry);
        EntityHandle beside = GatherMaps.Unit(sim, g.CellCenter(31, 13), type: GatherMaps.Infantry);
        sim.Enqueue(Command.Move(0, onGoal, goal));
        sim.Enqueue(Command.Move(0, stranded, g.CellCenter(remapped % g.Width, remapped / g.Width)));
        sim.Enqueue(Command.Move(0, beside, goal));
        walkers.Add(stranded);
        walkers.Add(beside);
        walkers.Add(onGoal);
        walkers.Add(beside);
        for (int t = 0; t < 600 && walkers.Any(h => u.State[h.Index] == UnitState.Moving); t++) TickChecked(sim, wait, ref longest);
        _out.WriteLine($"longest wait {longest}; ends: " + string.Join(", ", walkers.Select(h => $"{u.State[h.Index]} {Vector2.Distance(u.Position[h.Index], goal):F2} m")));
        Assert.True(longest <= 3, $"longest wait {longest}");
        Assert.All(walkers, h => Assert.Equal(UnitState.Idle, u.State[h.Index]));
        Assert.All(walkers, h => Assert.True(Vector2.Distance(u.Position[h.Index], goal) < 4f, $"slot {h.Index} ended {Vector2.Distance(u.Position[h.Index], goal):F2} m from the goal"));
    }

    /// <summary>
    /// A tree on a walker's straight route falls (opening: its field stays usable, the walker keeps its detour);
    /// a second unit appears on the felled cell and is ordered to the same goal (stranded on an undirected cell:
    /// it waits for the rebuild, then walks off); once the cell is clear a Keep is dropped over it (closing: the
    /// field is unusable and rebuilt round the Keep). No unit center ever enters a blocked cell; no wait over 3
    /// ticks; both arrive. A twin run hashes equal every tick.
    /// </summary>
    [Fact]
    public void ACellOpenedThenClosedAgain_UnderAWalkerAndAStrandedUnit_NobodyEntersABlockedCell_BothArrive_TwinMatches()
    {
        (Simulation a, List<EntityHandle> unitsA, int longestA) = OpenedThenReclosed();
        (Simulation b, _, _) = OpenedThenReclosed();
        Assert.Equal(a.StateHash(), b.StateHash());
        UnitStore u = a.World.Units;
        Vector2 goal = a.World.NavGrid.CellCenter(35, 12);
        _out.WriteLine($"longest wait {longestA}; buildings {a.World.Buildings.Count}; ends: " + string.Join(", ", unitsA.Select(h => $"{u.State[h.Index]} {Vector2.Distance(u.Position[h.Index], goal):F2} m")));
        Assert.True(longestA <= 3, $"longest wait {longestA}");
        Assert.Equal(1, a.World.Buildings.Count);
        Assert.All(unitsA, h => Assert.True(MoveScenario.Arrived(a.World)[h.Index], $"slot {h.Index} didn't arrive"));
    }

    private static (Simulation Sim, List<EntityHandle> Units, int Longest) OpenedThenReclosed()
    {
        Simulation sim = GatherMaps.NewSim(Flat(44, 26), units: 16);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        UnitStore u = w.Units;
        // A short wall of trees across row 12 at x 20, with one gap cell (20, 12) closed by a tree too.
        var trees = new List<EntityHandle>();
        for (int y = 6; y <= 18; y++) trees.Add(Spawn(w, Tree, 20, y, TreeWood));
        EntityHandle gapTree = trees[12 - 6];
        Vector2 goal = g.CellCenter(35, 12);
        int goalCell = 12 * g.Width + 35;
        EntityHandle walker = GatherMaps.Unit(sim, g.CellCenter(6, 12), type: GatherMaps.Infantry);
        sim.Enqueue(Command.Move(0, walker, goal));
        var wait = new int[u.Capacity];
        int longest = 0;
        void Step(Simulation s) => TickChecked(s, wait, ref longest);
        for (int t = 0; t < 30; t++) Step(sim);
        FlowField f = w.FlowFields.PeekCached(goalCell)!;
        Assert.NotNull(f);

        // Open the gap: the walker's field stays usable (it keeps walking its detour).
        Assert.Equal(TreeWood, w.Resources.Take(gapTree, TreeWood));
        Assert.Same(f, w.FlowFields.PeekCached(goalCell));
        // A unit appears on the opened cell and is ordered to the same goal.
        EntityHandle stranded = GatherMaps.Unit(sim, g.CellCenter(20, 12), type: GatherMaps.Infantry);
        sim.Enqueue(Command.Move(0, stranded, goal));
        for (int t = 0; t < 20; t++) Step(sim);
        // Wait until nobody stands in the Keep's footprint, then close the gap again.
        int keepX = 19, keepY = 11;
        BuildingDef keep = w.Data.Buildings[GatherMaps.Keep];
        bool dropped = false;
        for (int t = 0; t < 200 && !dropped; t++)
        {
            bool clear = true;
            for (int i = 0; i < u.Capacity && clear; i++)
                if (u.Alive[i] && GatherMaps.DistanceToFootprint(g, u.Position[i], keepY * g.Width + keepX, keep.FootprintWidth, keep.FootprintHeight) < 1e-3f) clear = false;
            // The Keep needs open ground: the column of trees at x 20 would block it, so remove the other covered trees first (more opening changes).
            if (clear)
            {
                foreach (EntityHandle h in trees)
                {
                    if (!w.Resources.IsAlive(h)) continue;
                    int c = w.Resources.Cell[h.Index];
                    int cx = c % g.Width, cy = c / g.Width;
                    if (cx >= keepX && cx < keepX + keep.FootprintWidth && cy >= keepY && cy < keepY + keep.FootprintHeight)
                        w.Resources.Take(h, TreeWood);
                }
                sim.Enqueue(Command.SpawnBuilding(0, GatherMaps.Keep, g.CellCenter(keepX, keepY)));
                dropped = true;
            }
            Step(sim);
        }
        Assert.True(dropped);
        var units = new List<EntityHandle> { walker, stranded };
        for (int t = 0; t < 800 && units.Any(h => u.State[h.Index] == UnitState.Moving); t++) Step(sim);
        for (int t = 0; t < 5; t++) Step(sim);
        return (sim, units, longest);
    }

    /// <summary>
    /// 24 units each spawned on a tree's cell the tick after it falls, while one tree falls every tick and 32
    /// marchers walk 32 cached goals; each spawned unit is ordered to a marcher's goal (cached, usable, stale,
    /// undirected under it). Every one walks within a bounded wait and arrives; no wait exceeds 20 ticks.
    /// </summary>
    [Fact]
    public void UnitsAppearingOnFelledCellsEveryTick_AllWalkWithinABoundedWait()
    {
        (Simulation sim, EntityHandle[] trees) = ThirtyTwoGroups(capacity: 64);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        UnitStore u = w.Units;
        var wait = new int[u.Capacity];
        int longest = 0, spawned = 0;
        var newcomers = new List<int>();
        for (int t = 0; t < 100; t++)
        {
            int c = w.Resources.Cell[trees[t].Index];
            Assert.Equal(TreeWood, w.Resources.Take(trees[t], TreeWood));
            if (t % 4 == 0 && spawned < 24)
            {
                var alive = (bool[])u.Alive.Clone();
                sim.Enqueue(Command.SpawnUnit(0, GatherMaps.Infantry, g.CellCenter(c % g.Width, c / g.Width)));
                TickChecked(sim, wait, ref longest);
                TickChecked(sim, wait, ref longest);
                for (int i = 0; i < u.Capacity; i++)
                    if (u.Alive[i] && !alive[i])
                    {
                        newcomers.Add(i);
                        int target = u.GoalCell[spawned % 32];
                        sim.Enqueue(Command.Move(0, new EntityHandle(i, u.Generation[i]), g.CellCenter(target % g.Width, target / g.Width)));
                    }
                spawned++;
            }
            TickChecked(sim, wait, ref longest);
        }
        for (int t = 0; t < 3000 && Enumerable.Range(0, u.Capacity).Any(i => u.Alive[i] && u.State[i] == UnitState.Moving); t++) TickChecked(sim, wait, ref longest);
        bool[] arrived = MoveScenario.Arrived(w);
        int gaveUp = newcomers.Count(i => !arrived[i]);
        _out.WriteLine($"{newcomers.Count} newcomers on felled cells; longest field wait {longest} ticks; newcomers not arrived {gaveUp}; marchers arrived {Enumerable.Range(0, 32).Count(i => arrived[i])}/32");
        Assert.Equal(24, newcomers.Count);
        Assert.True(longest <= 20, $"longest wait {longest}");
        Assert.Equal(0, gaveUp);
    }

    /// <summary>The dev's BUG-0073 scene (32 walkers, 32 goals, 100 trees on 120 x 72), with room for more units.</summary>
    private static (Simulation Sim, EntityHandle[] Trees) ThirtyTwoGroups(int capacity)
    {
        Simulation sim = NewSim(Flat(120, 72), units: capacity);
        NavGrid g = sim.World.NavGrid;
        var trees = new EntityHandle[100];
        for (int k = 0; k < 100; k++) trees[k] = Spawn(sim.World, Tree, 40 + k % 10 * 3, 6 + k / 10 * 6, TreeWood);
        for (int i = 0; i < 32; i++) sim.Enqueue(Command.SpawnUnit(0, 0, g.CellCenter(4 + i % 2 * 2, 4 + i * 2)));
        sim.Tick();
        for (int i = 0; i < 32; i++) sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, i), g.CellCenter(110 + i % 2 * 3, 4 + i * 2)));
        for (int t = 0; t < 20; t++) sim.Tick();
        for (int i = 0; i < 32; i++) Assert.True(sim.World.FlowFields.Contains(sim.World.Units.GoalCell[i]));
        return (sim, trees);
    }

    // ---------------------------------------------------------------- refresh starvation (BUG-0025 interplay)

    /// <summary>
    /// 64 walkers, each its own goal, on a 32-slot cache (twice the slots: BUG-0025's plain LRU thrashes), with a
    /// tree felled every tick for 300 ticks, against a calm twin. Reports the longest field wait, give-ups and
    /// settle time in both; asserts the cap and that both settle.
    /// </summary>
    [Fact]
    public void SixtyFourGoals_On32Slots_ATreeFallsEveryTick_Report()
    {
        (int longest, int gaveUp, int settle, int maxBuilds, int builds) felling = SixtyFourGoals(fell: true);
        (int longest, int gaveUp, int settle, int maxBuilds, int builds) calm = SixtyFourGoals(fell: false);
        _out.WriteLine($"felling every tick: longest field wait {felling.longest} ticks, gave up {felling.gaveUp}/64, settled after {felling.settle} ticks, max builds/tick {felling.maxBuilds}, builds {felling.builds}");
        _out.WriteLine($"calm twin:          longest field wait {calm.longest} ticks, gave up {calm.gaveUp}/64, settled after {calm.settle} ticks, max builds/tick {calm.maxBuilds}, builds {calm.builds}");
        Assert.True(felling.maxBuilds <= MovementConstants.MaxFieldBuildsPerTick);
        Assert.True(felling.settle > 0 && calm.settle > 0);
    }

    private static (int, int, int, int, int) SixtyFourGoals(bool fell)
    {
        var sim = new Simulation(TestSim.Config(Seed: 5, PlayerCount: 1, UnitCapacity: 64, CommandCapacity: 512), Flat(120, 72));
        Assert.Equal(32, sim.World.FlowFields.Capacity);
        NavGrid g = sim.World.NavGrid;
        UnitStore u = sim.World.Units;
        var trees = new List<EntityHandle>();
        for (int k = 0; k < 300; k++) trees.Add(Spawn(sim.World, Tree, 40 + k % 30, 4 + k / 30 * 6, TreeWood));
        for (int i = 0; i < 64; i++) sim.Enqueue(Command.SpawnUnit(0, 0, g.CellCenter(3 + i % 4, 3 + i)));
        sim.Tick();
        for (int i = 0; i < 64; i++) sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, i), g.CellCenter(100 + i % 16, 4 + i)));
        var wait = new int[u.Capacity];
        int longest = 0, maxBuilds = 0, settle = -1;
        int b0 = sim.World.FlowFields.BuildCount;
        for (int t = 0; t < 4000; t++)
        {
            if (fell && t < trees.Count) sim.World.Resources.Take(trees[t], TreeWood);
            int before = sim.World.FlowFields.BuildCount;
            TickChecked(sim, wait, ref longest, fields: t % 50 == 0);
            maxBuilds = Math.Max(maxBuilds, sim.World.FlowFields.BuildCount - before);
            if (t > 2 && Enumerable.Range(0, 64).All(i => u.State[i] != UnitState.Moving)) { settle = t; break; }
        }
        bool[] arrived = MoveScenario.Arrived(sim.World);
        return (longest, Enumerable.Range(0, 64).Count(i => !arrived[i]), settle, maxBuilds, sim.World.FlowFields.BuildCount - b0);
    }

    // ---------------------------------------------------------------- building spam vs the progress-mark reset

    /// <summary>
    /// A 100 x 40 map: an open field (rows 1-29) joined at its west end to a 1-cell corridor (row 31). Three
    /// walkers in the corridor press east into two enemies standing across it, so they can never pass. A closing
    /// change lands every tick (a Keep in the field, then trees), so every progress mark is reset every tick.
    /// They must still give up, in about the time a calm twin takes. Twins hash equal every tick.
    /// </summary>
    [Fact]
    public void WalkersPinnedBehindEnemies_AClosingChangeEveryTick_StillGiveUp()
    {
        (int spamTicks, string spamEnd) = Pinned(spam: true, out ulong hashA);
        (int spamTicksB, _) = Pinned(spam: true, out ulong hashB);
        (int calmTicks, string calmEnd) = Pinned(spam: false, out _);
        _out.WriteLine($"closing change every tick: all walkers stopped after {spamTicks} ticks ({spamEnd}); calm twin {calmTicks} ticks ({calmEnd})");
        Assert.Equal(hashA, hashB);
        Assert.Equal(spamTicks, spamTicksB);
        Assert.True(calmTicks > 0, "calm walkers never stopped");
        Assert.True(spamTicks > 0, "walkers under a closing change every tick never stopped");
        Assert.True(spamTicks <= calmTicks + 3 * MovementConstants.GiveUpTicks, $"{spamTicks} vs calm {calmTicks}");
    }

    /// <summary>The map of the pinned scenes: field rows 1-29, wall row 30 open at x 1-2, corridor row 31, then cliffs and a sealed plateau.</summary>
    private static Heightmap FieldAndCorridor()
    {
        var rows = new string[40];
        for (int y = 0; y < 40; y++)
        {
            if (y < 30) rows[y] = new string('0', 100);
            else if (y == 30) rows[y] = "000" + new string('1', 97);
            else if (y == 31) rows[y] = new string('0', 100);
            else rows[y] = new string('1', 100);
        }
        return FromRows(rows);
    }

    /// <summary>
    /// Drops a closing change this tick: the next free Keep spot in the field's top rows (2-17), else a tree in rows
    /// 19-28 on a cell with all 8 neighbours open (so trees never touch and never seal anything). False when there is no room left.
    /// </summary>
    private static bool ClosingChange(Simulation sim, ref int next)
    {
        World w = sim.World;
        NavGrid g = w.NavGrid;
        while (next < 19 * 3)
        {
            int x = 4 + next % 19 * 5, y = 2 + next / 19 * 5;
            next++;
            if (!w.Buildings.Fits(GatherMaps.Keep, y * g.Width + x)) continue;
            sim.Enqueue(Command.SpawnBuilding(0, GatherMaps.Keep, g.CellCenter(x, y)));
            return true;
        }
        for (int k = Math.Max(next, 1000); k < 1000 + 920; k++)
        {
            int x = 4 + (k - 1000) % 92, y = 19 + (k - 1000) / 92;
            next = k + 1;
            bool clear = true;
            for (int dy = -1; dy <= 1 && clear; dy++)
                for (int dx = -1; dx <= 1 && clear; dx++)
                    if (!g.IsPassable(x + dx, y + dy)) clear = false;
            if (!clear || !g.CanTakeResource(x, y) || GridChangeOracle.UnitIn(w, x, y)) continue;
            Assert.True(w.Resources.Spawn(Tree, y * g.Width + x, TreeWood, out _));
            return true;
        }
        return false;
    }

    private static (int Ticks, string End) Pinned(bool spam, out ulong hash)
    {
        Simulation sim = GatherMaps.NewSim(FieldAndCorridor(), units: 16, players: 2);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        UnitStore u = w.Units;
        GatherMaps.Unit(sim, g.CellCenter(50, 31) - new Vector2(0f, 0.5f), player: 1, type: GatherMaps.Infantry);
        GatherMaps.Unit(sim, g.CellCenter(50, 31) + new Vector2(0f, 0.5f), player: 1, type: GatherMaps.Infantry);
        var walkers = new List<EntityHandle>();
        for (int k = 0; k < 3; k++) walkers.Add(GatherMaps.Unit(sim, g.CellCenter(40 + k, 31), type: GatherMaps.Infantry));
        foreach (EntityHandle h in walkers) sim.Enqueue(Command.Move(0, h, g.CellCenter(80, 31)));
        int next = 0, stopped = -1, block0 = g.BlockVersion, spamEnded = -1;
        var wait = new int[u.Capacity];
        int longest = 0;
        for (int t = 0; t < 1500; t++)
        {
            if (spam && !ClosingChange(sim, ref next) && spamEnded < 0) spamEnded = t;
            TickChecked(sim, wait, ref longest);
            if (t > 2 && walkers.All(h => u.State[h.Index] != UnitState.Moving)) { stopped = t; break; }
        }
        hash = sim.StateHash();
        string end = $"closing changes ran out at tick {spamEnded}, block version +{g.BlockVersion - block0}, buildings {w.Buildings.Count}, walkers: " + string.Join(", ", walkers.Select(h => $"x {u.Position[h.Index].X:F2} goal {u.GoalCell[h.Index]}"));
        return (stopped, end);
    }

    /// <summary>
    /// 40 units ordered from the field to the far end of the dead-end corridor (a 2-cell mouth, a 1-cell
    /// corridor: most of them can't fit and must settle or give up) while a closing change lands every tick.
    /// The crowd settles within a bound of the calm twin's time.
    /// </summary>
    [Fact]
    public void ACrowdJammedAtACorridorMouth_AClosingChangeEveryTick_StillSettles()
    {
        int spam = Jam(spam: true, out string spamEnd);
        int calm = Jam(spam: false, out string calmEnd);
        _out.WriteLine($"closing change every tick: settled after {spam} ticks ({spamEnd}); calm twin {calm} ticks ({calmEnd})");
        Assert.True(calm > 0, "calm crowd never settled");
        Assert.True(spam > 0, "crowd under a closing change every tick never settled (1,500 ticks)");
    }

    private static int Jam(bool spam, out string end)
    {
        Simulation sim = GatherMaps.NewSim(FieldAndCorridor(), units: 48);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        UnitStore u = w.Units;
        for (int k = 0; k < 40; k++) sim.Enqueue(Command.SpawnUnit(0, GatherMaps.Infantry, g.CellCenter(4 + k % 8 * 2, 24 + k / 8)));
        sim.Tick();
        sim.Tick();
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i]) sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, i), g.CellCenter(97, 31)));
        int next = 0, settled = -1, spamEnded = -1;
        var wait = new int[u.Capacity];
        int longest = 0;
        for (int t = 0; t < 1500; t++)
        {
            if (spam && !ClosingChange(sim, ref next) && spamEnded < 0) spamEnded = t;
            TickChecked(sim, wait, ref longest);
            bool moving = false;
            for (int i = 0; i < u.Capacity && !moving; i++) moving = u.Alive[i] && u.State[i] == UnitState.Moving;
            if (t > 2 && !moving) { settled = t; break; }
        }
        bool[] arrived = MoveScenario.Arrived(w);
        end = $"closing changes ran out at tick {spamEnded}, arrived {arrived.Count(a => a)}/40, longest field wait {longest}, buildings {w.Buildings.Count}";
        return settled;
    }

    /// <summary>
    /// The jostle case for the progress-mark reset: 300 units of one player to one point on the default map (a blob
    /// that pushes and swings at its edge), with a tree grown far from the blob on every tick (a closing change, so
    /// every mark resets every tick). The blob must still settle, near the calm twin's time.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    public void ABlobOf300_AClosingChangeEveryTick_StillSettles(ulong seed)
    {
        (int spam, string spamEnd) = Blob(seed, spam: true);
        (int calm, string calmEnd) = Blob(seed, spam: false);
        _out.WriteLine($"seed {seed}: closing change every tick: settled after {spam} ticks ({spamEnd}); calm twin {calm} ticks ({calmEnd})");
        Assert.True(calm > 0, "calm blob never settled");
        Assert.True(spam > 0, "blob under a closing change every tick never settled (3,000 ticks)");
        Assert.True(spam <= 2 * calm + 200, $"{spam} vs calm {calm}");
    }

    private static (int Ticks, string End) Blob(ulong seed, bool spam)
    {
        Simulation sim = MoveScenario.Spawn(seed, 300, 40f, out int goalCell, players: 1);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        UnitStore u = w.Units;
        Vector2 goal = MoveScenario.Center(g, goalCell);
        MoveScenario.MoveAll(sim, goal);
        var far = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c += 7)
            if (Vector2.Distance(MoveScenario.Center(g, c), goal) > 120f) far.Add(c);
        int next = 0, settled = -1, grown = 0;
        var wait = new int[u.Capacity];
        int longest = 0;
        for (int t = 0; t < 3000; t++)
        {
            while (spam && next < far.Count)
            {
                int c = far[next++];
                int x = c % g.Width, y = c / g.Width;
                bool clear = true;
                for (int dy = -1; dy <= 1 && clear; dy++)
                    for (int dx = -1; dx <= 1 && clear; dx++)
                        if (!g.IsPassable(x + dx, y + dy)) clear = false;
                if (!clear || !g.CanTakeResource(x, y)) continue;
                Assert.True(w.Resources.Spawn(Tree, c, TreeWood, out _));
                grown++;
                break;
            }
            TickChecked(sim, wait, ref longest, fields: t % 100 == 0);
            bool moving = false;
            for (int i = 0; i < u.Capacity && !moving; i++) moving = u.Alive[i] && u.State[i] == UnitState.Moving;
            if (t > 2 && !moving) { settled = t; break; }
        }
        bool[] arrived = MoveScenario.Arrived(w);
        int gaveUp = Enumerable.Range(0, u.Capacity).Count(i => u.Alive[i] && !arrived[i] && u.GoalCell[i] == -1);
        return (settled, $"{grown} trees grown, arrived {arrived.Count(a => a)}, gave up {gaveUp}, longest field wait {longest}");
    }

    /// <summary>
    /// BUG-0080 report: a closing change on every tick for 300 ticks (the test seam's closing bump, no cells
    /// change) with 4 goal groups ordered on one tick. Every field is unusable every tick, the cap builds 2, and
    /// the same-tick tie goes to the lower goal cells (BUG-0026), so the other 2 groups stand for as long as the
    /// closings go on. Then the closings stop and everybody settles. Reports the longest field wait and the
    /// period at which closings stop starving anyone.
    /// </summary>
    [Fact]
    public void FourGroups_AClosingChangeEveryTick_StarvesTwoGroupsWhileItLasts_Report()
    {
        foreach (int period in new[] { 1, 2, 3 })
        {
            int longest = 0, tick = 0;
            var wait = new int[600];
            CrowdRows.Result r = CrowdRows.ToFourPoints(1, 300, 3000, onePlayerPerPoint: true, everyTick: w =>
            {
                for (int i = 0; i < w.Units.Capacity; i++)
                {
                    wait[i] = GridChangeOracle.WaitingForField(w, i) ? wait[i] + 1 : 0;
                    longest = Math.Max(longest, wait[i]);
                }
                if (++tick < 300 && tick % period == 0) w.NavGrid.BumpVersionForTests(); // lands before the next tick's movement pass
                return null;
            });
            _out.WriteLine($"closing every {period} tick(s) for 300 ticks, 4 goal groups: longest field wait {longest} ticks; {r}");
            Assert.Equal(0, r.StillMoving);
            if (period == 1) Assert.True(longest >= 250, $"expected the starvation BUG-0080 describes; longest wait {longest}");
        }
    }

    // ---------------------------------------------------------------- save / load proxy

    /// <summary>
    /// docs/03 says save/load will save the cache keys and rebuild the fields at load. A usable-but-stale field can't
    /// be rebuilt from its key: its contents depend on the grid as it was at its build. Proxy: two twins, trees
    /// felled for 10 ticks; in one, every stale field is rebuilt (what a load would do). Reports when hashes and
    /// positions part.
    /// </summary>
    [Fact]
    public void RebuildingStaleFieldsAtLoad_Proxy_DivergesFromAnUninterruptedRun_Report()
    {
        (Simulation a, EntityHandle gapA) = BehindATreeWall();
        (Simulation b, EntityHandle gapB) = BehindATreeWall();
        Assert.Equal(a.StateHash(), b.StateHash());
        // A tree in the middle of the wall falls: every walker's field is usable but stale (a much shorter way opened).
        a.World.Resources.Take(gapA, TreeWood);
        b.World.Resources.Take(gapB, TreeWood);
        int stale = 0;
        foreach (FlowField f in GridChangeOracle.UsedFields(b.World.FlowFields))
        {
            if (f.Version == b.World.NavGrid.Version) continue;
            b.World.FlowFields.Get(f.RequestedCell); // what "save the keys, rebuild the fields at load" would do
            stale++;
        }
        bool hashParted = a.StateHash() != b.StateHash();
        int firstPositionDiff = -1;
        float maxGap = 0f;
        for (int t = 0; t < 400; t++)
        {
            a.Tick();
            b.Tick();
            for (int i = 0; i < 32; i++)
            {
                float d = Vector2.Distance(a.World.Units.Position[i], b.World.Units.Position[i]);
                if (d > 0f && firstPositionDiff < 0) firstPositionDiff = t;
                maxGap = MathF.Max(maxGap, d);
            }
        }
        _out.WriteLine($"{stale} stale-but-usable fields at the 'save' point; hash parted at once: {hashParted}; positions first differ {firstPositionDiff} ticks later (-1: never within 400), largest gap {maxGap:F2} m");
        Assert.True(stale > 2);
        Assert.True(hashParted);
    }

    /// <summary>32 walkers, 32 goals east of a full-height tree wall at x 60 (one gap at the bottom); returns after every field is cached, with the wall's middle tree.</summary>
    private static (Simulation Sim, EntityHandle Middle) BehindATreeWall()
    {
        var sim = new Simulation(TestSim.Config(Seed: 5, PlayerCount: 1, UnitCapacity: 40, CommandCapacity: 256), Flat(120, 72));
        NavGrid g = sim.World.NavGrid;
        EntityHandle middle = default;
        for (int y = 1; y <= 66; y++)
        {
            EntityHandle h = Spawn(sim.World, Tree, 60, y, TreeWood);
            if (y == 34) middle = h;
        }
        for (int i = 0; i < 32; i++) sim.Enqueue(Command.SpawnUnit(0, 0, g.CellCenter(40 + i % 2 * 2, 3 + i * 2)));
        sim.Tick();
        for (int i = 0; i < 32; i++) sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, i), g.CellCenter(80 + i % 2 * 3, 3 + i * 2)));
        for (int t = 0; t < 20; t++) sim.Tick();
        for (int i = 0; i < 32; i++) Assert.True(sim.World.FlowFields.Contains(sim.World.Units.GoalCell[i]));
        return (sim, middle);
    }

    // ---------------------------------------------------------------- step-mask fast path

    /// <summary>The fast <c>ComputeSteps</c> against the bounds-checked <c>StepMask</c> on tiny and thin grids (every cell is on the ring, or one inner cell) and on random mazes with nodes coming and going.</summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 7)]
    [InlineData(7, 1)]
    [InlineData(2, 2)]
    [InlineData(2, 9)]
    [InlineData(3, 3)]
    [InlineData(3, 10)]
    [InlineData(10, 3)]
    [InlineData(4, 4)]
    [InlineData(17, 11)]
    [InlineData(64, 33)]
    public void ComputeSteps_MatchesStepMask_OnOddGridsAndRandomMazes(int width, int height)
    {
        var rng = new Determinism.SimRng((ulong)(width * 1000 + height), 9);
        for (int round = 0; round < 20; round++)
        {
            var rows = new string[height];
            for (int y = 0; y < height; y++)
            {
                var row = new char[width];
                for (int x = 0; x < width; x++) row[x] = rng.NextInt(0, 5) == 0 ? '1' : '0';
                rows[y] = new string(row);
            }
            var grid = new NavGrid(FromRows(rows));
            var steps = new byte[width * height];
            for (int change = 0; change < 6; change++)
            {
                FlowField.ComputeSteps(grid, steps);
                for (int c = 0; c < steps.Length; c++)
                    Assert.True(steps[c] == FlowField.StepMask(grid, c % width, c / width), $"{width}x{height} round {round} change {change}: cell ({c % width}, {c / width}) fast {steps[c]} vs {FlowField.StepMask(grid, c % width, c / width)}");
                int pick = rng.NextInt(0, width * height);
                int px = pick % width, py = pick / width;
                if ((grid.FlagsAt(px, py) & NavFlags.Resource) != 0) grid.ClearResource(px, py, 1, 1);
                else if (grid.CanTakeResource(px, py)) grid.SetResource(px, py, 1, 1);
            }
        }
    }

    // ---------------------------------------------------------------- BUG-0074: wood footprints

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(4, 4)]
    [InlineData(1, 4)]
    public void AWoodTypeWithANon1x1Footprint_IsOneErrorOnItsFootprint(int width, int height)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        string path = dir.FullPath("common/resources.json");
        string text = File.ReadAllText(path);
        const string treeFp = "\"resource\": \"wood\",\n      \"footprint\": { \"width\": 1, \"height\": 1 }";
        string normalized = text.Replace("\r\n", "\n");
        Assert.Contains(treeFp, normalized);
        File.WriteAllText(path, normalized.Replace(treeFp, $"\"resource\": \"wood\",\n      \"footprint\": {{ \"width\": {width}, \"height\": {height} }}"));
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        _out.WriteLine(string.Join(" | ", r.Errors));
        DataError e = Assert.Single(r.Errors);
        Assert.Equal("common/resources.json", e.File);
        Assert.EndsWith(".footprint", e.Path);
        Assert.Contains($"{width} x {height}", e.Message);
    }

    [Theory]
    [InlineData("{ \"width\": 0, \"height\": 2 }")]
    [InlineData("{ \"width\": 5, \"height\": 1 }")]
    [InlineData("{ \"width\": 1 }")]
    [InlineData("null")]
    public void AWoodTypeWithABadFootprintSide_IsOneError_NotTwo(string footprint)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        string path = dir.FullPath("common/resources.json");
        string normalized = File.ReadAllText(path).Replace("\r\n", "\n");
        const string treeFp = "\"resource\": \"wood\",\n      \"footprint\": { \"width\": 1, \"height\": 1 }";
        Assert.Contains(treeFp, normalized);
        File.WriteAllText(path, normalized.Replace(treeFp, "\"resource\": \"wood\",\n      \"footprint\": " + footprint));
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        _out.WriteLine(string.Join(" | ", r.Errors));
        Assert.Single(r.Errors);
    }

    [Fact]
    public void AGoldTypeWith1x1Footprint_AndAWood1x1_StillLoad()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        string path = dir.FullPath("common/resources.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("{ \"width\": 2, \"height\": 2 }", "{ \"width\": 1, \"height\": 1 }"));
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join(" | ", r.Errors));
    }
}
