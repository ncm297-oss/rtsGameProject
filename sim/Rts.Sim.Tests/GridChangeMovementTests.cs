using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;
using Rts.Sim.Pathfinding;
using Xunit.Abstractions;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests;

/// <summary>
/// M3-2b (BUG-0073, BUG-0077): an opening grid change (a tree felled) leaves cached fields usable, so walkers
/// keep walking while the build pass refreshes them; a closing one (a building dropped) resets every walker's
/// progress mark, so the detour isn't read as "no progress".
/// </summary>
[Collection(SerialCollection.Name)]
public class GridChangeMovementTests
{
    private readonly ITestOutputHelper _out;

    public GridChangeMovementTests(ITestOutputHelper output) => _out = output;

    /// <summary>32 walkers, one per goal, from the west edge to the east one across a 10 x 10 grid of trees (the QA BUG-0073 scene).</summary>
    internal static (Simulation Sim, EntityHandle[] Trees) ThirtyTwoGroups()
    {
        Simulation sim = NewSim(Flat(120, 72), units: 32);
        NavGrid g = sim.World.NavGrid;
        var trees = new EntityHandle[100];
        for (int k = 0; k < 100; k++) trees[k] = Spawn(sim.World, Tree, 40 + k % 10 * 3, 6 + k / 10 * 6, TreeWood);
        for (int i = 0; i < 32; i++) sim.Enqueue(Command.SpawnUnit(0, 0, g.CellCenter(4 + i % 2 * 2, 4 + i * 2)));
        sim.Tick();
        for (int i = 0; i < 32; i++) sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, i), g.CellCenter(110 + i % 2 * 3, 4 + i * 2)));
        // 32 goals at 2 builds a tick: every field is cached within 17 ticks.
        for (int t = 0; t < 20; t++) sim.Tick();
        for (int i = 0; i < 32; i++) Assert.True(sim.World.FlowFields.Contains(sim.World.Units.GoalCell[i]));
        return (sim, trees);
    }

    /// <summary>True if Moving unit <paramref name="i"/> stands waiting for a field: outside its goal cell with no usable field, or a stale one with no direction on its cell.</summary>
    private static bool WaitingForField(World w, int i)
    {
        UnitStore u = w.Units;
        if (u.State[i] != UnitState.Moving || u.Velocity[i] != Vector2.Zero) return false;
        if (!w.NavGrid.WorldToCell(u.Position[i], out int x, out int y)) return false;
        int cell = y * w.NavGrid.Width + x;
        if (cell == u.GoalCell[i]) return false;
        FlowField? f = w.FlowFields.PeekCached(u.GoalCell[i]);
        return f == null || (f.Version != w.NavGrid.Version && f.DirectionAt(cell) == FlowField.NoDirection);
    }

    [Fact]
    public void OneTreeFallsEveryTickFor100Ticks_32Groups_NobodyWaitsMoreThan2Ticks_NobodyGivesUp_AllArrive()
    {
        (Simulation sim, EntityHandle[] trees) = ThirtyTwoGroups();
        World w = sim.World;
        UnitStore u = w.Units;
        var wait = new int[32];
        int longest = 0, maxBuilds = 0, ticks = 0, moving;
        do
        {
            if (ticks < 100) Assert.Equal(TreeWood, w.Resources.Take(trees[ticks], TreeWood));
            int builds = w.FlowFields.BuildCount;
            sim.Tick();
            ticks++;
            maxBuilds = Math.Max(maxBuilds, w.FlowFields.BuildCount - builds);
            moving = 0;
            for (int i = 0; i < 32; i++)
            {
                if (u.State[i] == UnitState.Moving) moving++;
                wait[i] = WaitingForField(w, i) ? wait[i] + 1 : 0;
                longest = Math.Max(longest, wait[i]);
            }
            Assert.Equal(-1, MoveScenario.FirstUnitOnBlockedGround(w));
        } while ((moving > 0 || ticks < 100) && ticks < 3000);
        bool[] arrived = MoveScenario.Arrived(w);
        int gaveUp = Enumerable.Range(0, 32).Count(i => !arrived[i] && u.GoalCell[i] == -1);
        _out.WriteLine($"longest field wait {longest} ticks, max builds/tick {maxBuilds}, arrived {arrived.Count(a => a)}/32, gave up {gaveUp}, settled after {ticks} ticks");
        Assert.True(longest <= 2, $"longest wait {longest}");
        Assert.True(maxBuilds <= MovementConstants.MaxFieldBuildsPerTick, $"{maxBuilds} builds in one tick");
        Assert.Equal(0, gaveUp);
        Assert.Equal(32, arrived.Count(a => a));
    }

    /// <summary>
    /// The same scene's average tick while a tree falls every tick: under the brief's 0.5 ms, an absolute bound
    /// (BUG-0082). The cap still spends its 2 builds a tick on refreshes while felling goes on, so the tick is
    /// about 2 builds of a 120 x 72 field plus one step-mask pass. Debug, quiet machine: 1.76 ms a tick before
    /// M3-2b, 0.81 ms after its first round (0.33 ms a build, 0.12 ms a step pass), 0.40 ms after BUG-0082's
    /// fix (0.15 ms a build, 0.045 ms a step pass). The printed breakdown says which part grew when it fails.
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void OneTreeFallsEveryTick_AverageTick_Perf()
    {
        (Simulation sim, EntityHandle[] trees) = ThirtyTwoGroups();
        (Simulation calm, _) = ThirtyTwoGroups();
        for (int t = 90; t < 100; t++) // warm-up: JIT the refresh path
        {
            sim.World.Resources.Take(trees[t], TreeWood);
            sim.Tick();
            calm.Tick();
        }
        double felling = 0, calmMs = 0;
        for (int t = 0; t < 90; t++)
        {
            sim.World.Resources.Take(trees[t], TreeWood);
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            sim.Tick();
            felling += System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            calm.Tick();
            calmMs += System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        }
        NavGrid g = sim.World.NavGrid;
        var steps = new byte[g.Width * g.Height];
        var queue = new CellQueue(steps.Length);
        var field = new FlowField(g.Width, g.Height);
        long b0 = System.Diagnostics.Stopwatch.GetTimestamp();
        for (int k = 0; k < 30; k++)
        {
            FlowField.ComputeSteps(g, steps);
            field.Build(g, sim.World.Units.GoalCell[k], queue, steps);
            field.Build(g, sim.World.Units.GoalCell[k + 1], queue, steps);
        }
        double budget = System.Diagnostics.Stopwatch.GetElapsedTime(b0).TotalMilliseconds / 30;
        b0 = System.Diagnostics.Stopwatch.GetTimestamp();
        for (int k = 0; k < 30; k++) FlowField.ComputeSteps(g, steps);
        double stepPass = System.Diagnostics.Stopwatch.GetElapsedTime(b0).TotalMilliseconds / 30;
        _out.WriteLine($"avg tick with a tree felled every tick: {felling / 90:F3} ms; calm {calmMs / 90:F3} ms; 2 builds + 1 step pass alone {budget:F3} ms (step pass {stepPass:F3} ms)");
        Assert.True(felling / 90 < 0.5, $"avg tick {felling / 90:F3} ms (M3-2b criterion 3: < 0.5 ms)");
    }

    /// <summary>
    /// A unit standing on a cell opened after its goal's field was built (no direction there) waits, still
    /// Moving, while older goals take the build cap; then its goal is rebuilt as a miss and it walks. It never
    /// stands on blocked ground.
    /// </summary>
    [Fact]
    public void AUnitOnACellOpenedAfterTheBuild_WaitsForTheRebuild_ThenWalks()
    {
        Simulation sim = GatherMaps.NewSim(Flat(40, 24), units: 16);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        UnitStore u = w.Units;
        EntityHandle tree = Spawn(w, Tree, 12, 12, TreeWood);
        // A walks to G and arrives; G's field stays cached (nobody Moving needs it, so nobody refreshes it).
        EntityHandle a = GatherMaps.Unit(sim, g.CellCenter(4, 4), type: GatherMaps.Infantry);
        Vector2 goal = g.CellCenter(30, 12);
        int goalCell = 12 * g.Width + 30;
        sim.Enqueue(Command.Move(0, a, goal));
        GatherMaps.Run(sim, 2); // a command applies in the tick after the one running when it was queued
        for (int t = 0; t < 400 && u.State[a.Index] == UnitState.Moving; t++) sim.Tick();
        Assert.Equal(UnitState.Idle, u.State[a.Index]);
        FlowField field = w.FlowFields.PeekCached(goalCell)!;
        Assert.NotNull(field);

        // The tree falls: G's field is usable but stale, with no direction on the opened cell; B stands there.
        Assert.Equal(TreeWood, w.Resources.Take(tree, TreeWood));
        int opened = 12 * g.Width + 12;
        Assert.Same(field, w.FlowFields.PeekCached(goalCell));
        Assert.Equal(FlowField.NoDirection, field.DirectionAt(opened));
        EntityHandle b = GatherMaps.Unit(sim, g.CellCenter(12, 12), type: GatherMaps.Infantry);
        var others = new EntityHandle[4];
        for (int k = 0; k < 4; k++) others[k] = GatherMaps.Unit(sim, g.CellCenter(3 + k, 20), type: GatherMaps.Infantry);
        Assert.NotEqual(field.Version, g.Version);

        // Four new goals ordered one tick before B's: two get built that tick, the other two (older than B's
        // order) take the next tick's cap, so B waits exactly then.
        for (int k = 0; k < 4; k++) sim.Enqueue(Command.Move(0, others[k], g.CellCenter(20 + 3 * k, 3)));
        sim.Tick();
        sim.Enqueue(Command.Move(0, b, goal));
        int builds = w.FlowFields.BuildCount;
        sim.Tick(); // the four orders apply
        Assert.Equal(2, w.FlowFields.BuildCount - builds);
        Vector2 start = u.Position[b.Index];
        builds = w.FlowFields.BuildCount;
        sim.Tick(); // B's order applies
        Assert.Equal(2, w.FlowFields.BuildCount - builds); // the two older goals
        Assert.Equal(UnitState.Moving, u.State[b.Index]);
        Assert.Equal(Vector2.Zero, u.Velocity[b.Index]);
        Assert.Equal(start, u.Position[b.Index]);
        Assert.Equal(0, u.StuckTicks[b.Index]);
        Assert.Equal(float.PositiveInfinity, u.BestRemaining[b.Index]);
        Assert.Same(field, w.FlowFields.PeekCached(goalCell));
        Assert.NotEqual(g.Version, field.Version); // not refreshed: the misses took the cap

        builds = w.FlowFields.BuildCount;
        sim.Tick();
        Assert.Equal(1, w.FlowFields.BuildCount - builds); // B's goal, as a miss
        Assert.Equal(g.Version, field.Version);
        Assert.NotEqual(Vector2.Zero, u.Velocity[b.Index]);
        for (int t = 0; t < 600 && u.State[b.Index] == UnitState.Moving; t++)
        {
            sim.Tick();
            Assert.Equal(-1, MoveScenario.FirstUnitOnBlockedGround(w));
        }
        Assert.True(Vector2.Distance(u.Position[b.Index], goal) < 3f, $"B ended at {u.Position[b.Index]}");
    }

    /// <summary>
    /// BUG-0077: a building dropped across a marching column's path lengthens every walker's route. Its progress
    /// mark is reset before the next plan, so after that tick each mark is the estimate on the new field, above
    /// the old best (kept as the minimum, the old mark would have stayed at or below it and the detour read as
    /// "no progress"), and no walker counts a stuck tick for the change.
    /// </summary>
    [Fact]
    public void ABuildingDroppedAcrossAColumnsPath_ResetsEveryWalkersProgressMark()
    {
        Simulation sim = GatherMaps.NewSim(Flat(48, 24), units: 24);
        UnitStore u = sim.World.Units;
        var units = new List<EntityHandle>();
        for (int k = 0; k < 16; k++) units.Add(GatherMaps.Unit(sim, GatherMaps.At(sim, 3 + k % 4, 8 + k / 4), type: GatherMaps.Infantry));
        foreach (EntityHandle h in units) sim.Enqueue(Command.Move(0, h, GatherMaps.At(sim, 42, 10)));
        GatherMaps.Run(sim, 30);
        sim.Enqueue(Command.SpawnBuilding(0, GatherMaps.Keep, GatherMaps.At(sim, 20, 8)));
        sim.Tick(); // stamped for the next tick
        Assert.Equal(0, sim.World.Buildings.Count);
        var best = units.Select(h => u.BestRemaining[h.Index]).ToArray();
        var stuck = units.Select(h => u.StuckTicks[h.Index]).ToArray();
        Assert.All(units, h => Assert.Equal(UnitState.Moving, u.State[h.Index]));
        Assert.All(best, b => Assert.True(float.IsFinite(b)));
        sim.Tick(); // the building lands, then movement
        Assert.Equal(1, sim.World.Buildings.Count);
        for (int k = 0; k < units.Count; k++)
        {
            int i = units[k].Index;
            Assert.Equal(UnitState.Moving, u.State[i]);
            Assert.True(u.BestRemaining[i] > best[k], $"slot {i}: mark {u.BestRemaining[i]} not above the old best {best[k]}");
            Assert.True(u.StuckTicks[i] <= Math.Max(stuck[k], 1), $"slot {i}: stuck {stuck[k]} -> {u.StuckTicks[i]}");
        }
        Assert.Equal(sim.World.NavGrid.BlockVersion, sim.World.SeenBlockVersion);
    }

    /// <summary>
    /// An opening change (a tree felled behind three walkers pressing into enemies across a 1-cell corridor, so
    /// their stuck counts climb) leaves every progress mark, stuck count and position exactly as in an untouched
    /// twin: no reset, and the refreshed field changes no estimate on their route.
    /// </summary>
    [Fact]
    public void AnOpeningChange_LeavesEveryWalkersProgressMarkAlone()
    {
        (Simulation sim, EntityHandle[] walkers, EntityHandle tree) = Corridor();
        (Simulation twin, _, EntityHandle twinTree) = Corridor();
        UnitStore u = sim.World.Units, tu = twin.World.Units;
        // Until the front walker has been stuck for 4 ticks (the others are still closing up behind it).
        for (int t = 0; t < 200 && walkers.All(h => u.StuckTicks[h.Index] < 4); t++)
        {
            sim.Tick();
            twin.Tick();
        }
        Assert.All(walkers, h => Assert.Equal(UnitState.Moving, u.State[h.Index]));
        Assert.All(walkers, h => Assert.True(float.IsFinite(u.BestRemaining[h.Index])));
        Assert.Contains(walkers, h => u.StuckTicks[h.Index] >= 4);
        Assert.Equal(sim.StateHash(), twin.StateHash());

        int seen = sim.World.SeenBlockVersion;
        Assert.Equal(TreeWood, sim.World.Resources.Take(tree, TreeWood));
        Assert.True(twin.World.Resources.IsAlive(twinTree));
        for (int t = 0; t < 3; t++)
        {
            sim.Tick();
            twin.Tick();
            foreach (EntityHandle h in walkers)
            {
                int i = h.Index;
                Assert.Equal(tu.State[i], u.State[i]);
                Assert.Equal(tu.BestRemaining[i], u.BestRemaining[i]);
                Assert.Equal(tu.StuckTicks[i], u.StuckTicks[i]);
                Assert.Equal(tu.Position[i], u.Position[i]);
            }
        }
        Assert.Contains(walkers, h => u.StuckTicks[h.Index] >= 5);
        Assert.Equal(seen, sim.World.SeenBlockVersion);
    }

    /// <summary>A 1-cell corridor (row 2, between cliffs) with two enemies standing across it at x 14, three walkers behind it heading east, and a tree behind them, off the route.</summary>
    private static (Simulation Sim, EntityHandle[] Walkers, EntityHandle Tree) Corridor()
    {
        Simulation sim = GatherMaps.NewSim(FromRows(
            "00000000000000000000000000",
            "11111111111111111111111111",
            "00000000000000000000000000",
            "11111111111111111111111111",
            "00000000000000000000000000"), units: 8, players: 2);
        NavGrid g = sim.World.NavGrid;
        Assert.Equal(24, g.PassableCount);
        EntityHandle tree = Spawn(sim.World, Tree, 3, 2, TreeWood); // behind the walkers, off every route
        // Two enemies side by side fill the 2 m corridor (a walker's center may go anywhere in the cell).
        GatherMaps.Unit(sim, g.CellCenter(14, 2) - new Vector2(0f, 0.5f), player: 1, type: GatherMaps.Infantry);
        GatherMaps.Unit(sim, g.CellCenter(14, 2) + new Vector2(0f, 0.5f), player: 1, type: GatherMaps.Infantry);
        var walkers = new EntityHandle[3];
        for (int k = 0; k < 3; k++) walkers[k] = GatherMaps.Unit(sim, g.CellCenter(8 + k, 2), type: GatherMaps.Infantry);
        foreach (EntityHandle h in walkers) sim.Enqueue(Command.Move(0, h, g.CellCenter(22, 2)));
        return (sim, walkers, tree);
    }
}
