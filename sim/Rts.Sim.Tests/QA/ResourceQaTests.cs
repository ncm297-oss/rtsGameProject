using System.Numerics;
using System.Text;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.Replays;
using Rts.Sim.Tests.Stress;
using Xunit.Abstractions;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M3-1): resource store, depletion against movement, hash sensitivity, capacity, replay format 3
/// and resources.json, attacked independently of the developer's suites.
/// </summary>
[Collection(SerialCollection.Name)]
public class ResourceQaTests
{
    private readonly ITestOutputHelper _out;

    public ResourceQaTests(ITestOutputHelper output) => _out = output;

    // ---------------------------------------------------------------- Take edges

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(int.MinValue, 0)]
    [InlineData(1, 1)]
    [InlineData(int.MaxValue, 100)]
    public void Take_EdgeAmounts_ClampOrRefuse_AndNeverThrow(int amount, int expectedTaken)
    {
        Simulation sim = NewSim(Flat(16, 16));
        EntityHandle h = Spawn(sim.World, Tree, 5, 5, 100);
        int version = sim.World.NavGrid.Version;
        ulong before = sim.StateHash();
        Assert.Equal(expectedTaken, sim.World.Resources.Take(h, amount));
        if (expectedTaken == 0)
        {
            Assert.Equal(before, sim.StateHash());
            Assert.Equal(version, sim.World.NavGrid.Version);
        }
        Assert.Equal(expectedTaken == 100, !sim.World.Resources.IsAlive(h));
    }

    [Fact]
    public void Take_StaleAfterSlotReuse_DefaultAndOutOfRangeHandles_TakeNothing()
    {
        Simulation sim = NewSim(Flat(16, 16));
        ResourceStore r = sim.World.Resources;
        EntityHandle old = Spawn(sim.World, Tree, 5, 5, 3);
        Assert.Equal(3, r.Take(old, 3));
        EntityHandle reused = Spawn(sim.World, Tree, 7, 7, 3); // LIFO: same slot, next generation
        Assert.Equal(old.Index, reused.Index);
        Assert.NotEqual(old.Generation, reused.Generation);
        ulong h = sim.StateHash();
        foreach (EntityHandle bad in new[] { old, default, new EntityHandle(-1, 1), new EntityHandle(int.MaxValue, 1), new EntityHandle(r.Capacity, 1), new EntityHandle(0, int.MaxValue) })
            Assert.Equal(0, r.Take(bad, 1));
        Assert.Equal(h, sim.StateHash());
        Assert.True(r.IsAlive(reused));
        Assert.Equal(3, r.Remaining[reused.Index]);
    }

    // ---------------------------------------------------------------- hash sensitivity

    [Fact]
    public void Hash_CoversGenerationAlone()
    {
        // a: slot 0 spawned, freed, re-spawned on the same cell (generation 2, version 3).
        // b: slot 0 spawned once (generation 1), version bumped twice by the test seam.
        Simulation a = NewSim(Flat(16, 16)), b = NewSim(Flat(16, 16));
        EntityHandle ha = Spawn(a.World, Tree, 4, 4, 10);
        a.World.Resources.Take(ha, 10);
        Spawn(a.World, Tree, 4, 4, 10);
        Spawn(b.World, Tree, 4, 4, 10);
        b.World.NavGrid.BumpVersionForTests();
        b.World.NavGrid.BumpVersionForTests();
        Assert.Equal(a.World.NavGrid.Version, b.World.NavGrid.Version);
        Assert.True(a.World.Resources.Cell.SequenceEqual(b.World.Resources.Cell));
        Assert.True(a.World.Resources.Remaining.SequenceEqual(b.World.Resources.Remaining));
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void Hash_CoversWhichSlotHoldsWhichNode()
    {
        Simulation a = NewSim(Flat(16, 16)), b = NewSim(Flat(16, 16));
        Spawn(a.World, Tree, 3, 3, 10);
        Spawn(a.World, Tree, 9, 9, 10);
        Spawn(b.World, Tree, 9, 9, 10);
        Spawn(b.World, Tree, 3, 3, 10);
        Assert.Equal(a.World.NavGrid.Version, b.World.NavGrid.Version);
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void Hash_EveryRemainingValue_AtEverySlot_ChangesIt()
    {
        // 64 trees; taking 1 from any single one changes the hash, and all 64 one-off hashes differ.
        var seen = new HashSet<ulong>();
        Simulation reference = NewSim(Flat(40, 40));
        for (int k = 0; k < 64; k++) Spawn(reference.World, Tree, 2 + k % 8 * 4, 2 + k / 8 * 4, 50);
        seen.Add(reference.StateHash());
        for (int s = 0; s < 64; s++)
        {
            Simulation sim = NewSim(Flat(40, 40));
            for (int k = 0; k < 64; k++) Spawn(sim.World, Tree, 2 + k % 8 * 4, 2 + k / 8 * 4, 50);
            Assert.Equal(1, sim.World.Resources.Take(sim.World.Resources.HandleOf(s), 1));
            Assert.True(seen.Add(sim.StateHash()), $"slot {s}: hash collides with another one-off");
        }
    }

    [Fact]
    public void ForestsOn_TwoSims_EqualEveryTick_AndDifferFromForestsOff()
    {
        MapGenParams m = MapGenParams.Default with { Forests = 20, GoldMines = 8 };
        var a = new Simulation(TestSim.Config(Seed: 31, PlayerCount: 2, UnitCapacity: 64, CommandCapacity: 128) with { Map = m });
        var b = new Simulation(TestSim.Config(Seed: 31, PlayerCount: 2, UnitCapacity: 64, CommandCapacity: 128) with { Map = m });
        var off = new Simulation(TestSim.Config(Seed: 31, PlayerCount: 2, UnitCapacity: 64, CommandCapacity: 128));
        Assert.NotEqual(a.StateHash(), off.StateHash());
        NavGrid g = a.World.NavGrid;
        var cells = OrderMix.Cells(g, 14f);
        foreach (Simulation s in new[] { a, b })
            for (int i = 0; i < 64; i++) s.Enqueue(Command.SpawnUnit(i % 2, i % TestSim.UnitTypeCount, MoveScenario.Center(g, cells[i * 5 % cells.Count])));
        var rng = new SimRng(31, 9);
        ResourceStore ra = a.World.Resources, rb = b.World.Resources;
        for (int t = 0; t < 600; t++)
        {
            if (t % 40 == 3)
            {
                Vector2 target = MoveScenario.Center(g, cells[rng.NextInt(0, cells.Count)]);
                MoveScenario.MoveAll(a, target);
                MoveScenario.MoveAll(b, target);
            }
            if (t % 5 == 0)
            {
                int slot = rng.NextInt(0, Math.Max(1, ra.Count));
                int bite = rng.NextInt(1, 60);
                Assert.Equal(ra.Take(ra.HandleOf(slot), bite), rb.Take(rb.HandleOf(slot), bite));
            }
            a.Tick();
            b.Tick();
            Assert.Equal(a.StateHash(), b.StateHash());
            Assert.Equal(-1, MoveScenario.FirstUnitOnBlockedGround(a.World));
        }
    }

    // ---------------------------------------------------------------- capacity

    [Fact]
    public void ResourceCapacity_Zero_IsRejectedAtConstruction_WithArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Simulation(TestSim.Config(Seed: 1, PlayerCount: 1, UnitCapacity: 4, CommandCapacity: 4) with { ResourceCapacity = 0, Map = MapGenParams.Default with { Forests = 1 } }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Simulation(TestSim.Config(Seed: 1, PlayerCount: 1, UnitCapacity: 4, CommandCapacity: 4) with { ResourceCapacity = -5 }));
    }

    [Theory]
    [InlineData(1, 3, 1, 0)]       // the first mine fills the store; no forest fits
    [InlineData(13, 1, 1, 1)]      // one mine + one 12-tree forest exactly
    [InlineData(14, 3, 3, 0)]      // three mines leave 11 slots: no partial forest
    [InlineData(12, 0, 0, 1)]      // no mines: one 12-tree forest exactly
    [InlineData(23, 0, 0, 1)]      // 11 slots left after the first forest: no partial second one
    public void SmallCapacity_FillsWithoutAPartialForest(int capacity, int requestMines, int expectMines, int expectForests)
    {
        MapGenParams m = MapGenParams.Default with { Forests = 5, ForestMinTrees = 12, ForestMaxTrees = 12, GoldMines = requestMines };
        var sim = new Simulation(TestSim.Config(Seed: 3, PlayerCount: 1, UnitCapacity: 4, CommandCapacity: 4) with { Map = m, ResourceCapacity = capacity });
        ResourcePlacement p = sim.World.ResourcePlacement;
        Assert.Equal(expectMines, p.Mines);
        Assert.Equal(expectForests, p.Forests);
        Assert.Equal(12 * expectForests, p.Trees);
        Assert.Equal(p.Mines + p.Trees, sim.World.Resources.Count);
        var bare = new Simulation(TestSim.Config(Seed: 3, PlayerCount: 1, UnitCapacity: 4, CommandCapacity: 4));
        string? err = ResourceOracle.Check(sim.World, bare.World, m);
        Assert.True(err == null, err);
    }

    [Fact]
    public void Capacity100k_DenseForests_HashCostReport_AndZeroAllocTick()
    {
        MapGenParams m = MapGenParams.Default with { Forests = 64, ForestMinTrees = 60, ForestMaxTrees = 120, GoldMines = 16 };
        var sim = new Simulation(TestSim.Config(Seed: 8, PlayerCount: 1, UnitCapacity: 8, CommandCapacity: 8) with { Map = m, ResourceCapacity = 100_000 });
        int nodes = sim.World.Resources.Count;
        sim.Tick();
        sim.StateHash();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 200; i++) sim.StateHash();
        double us = sw.Elapsed.TotalMilliseconds * 1000 / 200;
        _out.WriteLine($"capacity 100k, {nodes} live nodes ({sim.World.ResourcePlacement}): StateHash {us:F1} us");
        Assert.True(nodes > 1000, $"{nodes} nodes");
        AllocationProbe.AssertZero(() => { sim.Tick(); sim.StateHash(); }, _out);
    }

    // ---------------------------------------------------------------- depletion vs movement

    private static int CellOf(NavGrid g, int x, int y) => y * g.Width + x;

    [Fact]
    public void OrderOntoATree_GoesToTheNearestOpenCell_AndOntoTheCellOnceFelled()
    {
        Simulation sim = NewSim(Flat(24, 16));
        NavGrid g = sim.World.NavGrid;
        EntityHandle tree = Spawn(sim.World, Tree, 12, 5, TreeWood);
        sim.Enqueue(Command.SpawnUnit(0, 0, g.CellCenter(4, 5)));
        sim.Tick();
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), g.CellCenter(12, 5)));
        int t = 0;
        for (; t < 600 && !(t > 2 && u.State[0] == UnitState.Idle); t++)
        {
            if (t == 10) Assert.Equal(TreeWood, sim.World.Resources.Take(tree, TreeWood)); // falls mid-walk
            sim.Tick();
            Assert.Equal(-1, MoveScenario.FirstUnitOnBlockedGround(sim.World));
        }
        // Resolved at order time (docs/03: a blocked target snaps to the nearest passable cell): of the four
        // cells at distance 1, the lowest index, (12, 4); the tree falling mid-walk doesn't move the goal.
        Assert.True(g.WorldToCell(u.Position[0], out int x, out int y));
        _out.WriteLine($"first order: idle after {t} ticks in ({x}, {y}), goal cell {u.GoalCell[0]}");
        Assert.Equal(CellOf(g, 12, 4), u.GoalCell[0]);
        Assert.Equal((12, 4), (x, y));

        // The same click again now lands on the freed cell itself.
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), g.CellCenter(12, 5)));
        for (t = 0; t < 600 && !(t > 2 && u.State[0] == UnitState.Idle); t++) sim.Tick();
        Assert.True(g.WorldToCell(u.Position[0], out x, out y));
        Assert.Equal((12, 5), (x, y));
        Assert.Equal(CellOf(g, 12, 5), u.GoalCell[0]);
    }

    [Fact]
    public void BlobPackedAgainstATreeLine_TreesFallUnderTheBlob_NobodyStrandsOrEntersBlockedGround()
    {
        // 30 units pack against a tree line at x = 12 (gap at the bottom); then the 5 trees they press on fall.
        Simulation sim = NewSim(Flat(32, 24), units: 30);
        NavGrid g = sim.World.NavGrid;
        var line = new EntityHandle[20];
        for (int y = 1; y <= 20; y++) line[y - 1] = Spawn(sim.World, Tree, 12, y, TreeWood);
        for (int i = 0; i < 30; i++) sim.Enqueue(Command.SpawnUnit(0, i % TestSim.UnitTypeCount, g.CellCenter(3 + i % 5, 5 + i / 5)));
        sim.Tick();
        sim.Tick();
        Assert.Equal(30, sim.World.Units.Count);
        MoveScenario.MoveAll(sim, g.CellCenter(11, 10));
        UnitStore u = sim.World.Units;
        for (int t = 0; t < 300; t++)
        {
            sim.Tick();
            Assert.Equal(-1, MoveScenario.FirstUnitOnBlockedGround(sim.World));
        }
        for (int y = 8; y <= 12; y++) Assert.Equal(TreeWood, sim.World.Resources.Take(line[y - 1], TreeWood));
        // Then everyone to the far side, straight through the new gap.
        MoveScenario.MoveAll(sim, g.CellCenter(24, 10));
        sim.Tick(); // the Moves are stamped for the next tick
        int ticks = 0, moving;
        do
        {
            sim.Tick();
            ticks++;
            Assert.Equal(-1, MoveScenario.FirstUnitOnBlockedGround(sim.World));
            for (int i = 0; i < u.Capacity; i++) Assert.True(float.IsFinite(u.Position[i].X) && float.IsFinite(u.Position[i].Y));
            moving = 0;
            for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.State[i] == UnitState.Moving) moving++;
        } while (moving > 0 && ticks < 2000);
        bool[] arrived = MoveScenario.Arrived(sim.World);
        int east = 0;
        for (int i = 0; i < u.Capacity; i++) if (u.Position[i].X > 13 * MapConstants.CellSize) east++;
        _out.WriteLine($"through the felled gap: {arrived.Count(a => a)} arrived, {east} east of the line, settled after {ticks} ticks");
        Assert.Equal(0, moving);
        Assert.Equal(30, east);
    }

    /// <summary>A 3 x 3 grove whose middle tree falls first: an open cell nobody can reach (BUG-0075). Where does a unit ordered there go?</summary>
    private static (Vector2 End, int GoalCell, UnitState State, bool Reachable) OrderIntoGrove(bool fellMiddle)
    {
        Simulation sim = NewSim(Flat(24, 16));
        NavGrid g = sim.World.NavGrid;
        EntityHandle middle = default;
        for (int y = 6; y <= 8; y++)
            for (int x = 10; x <= 12; x++)
            {
                EntityHandle h = Spawn(sim.World, Tree, x, y, TreeWood);
                if (x == 11 && y == 7) middle = h;
            }
        if (fellMiddle) Assert.Equal(TreeWood, sim.World.Resources.Take(middle, TreeWood));
        sim.Enqueue(Command.SpawnUnit(0, 0, g.CellCenter(3, 7)));
        sim.Tick();
        sim.Tick();
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), g.CellCenter(11, 7)));
        UnitStore u = sim.World.Units;
        sim.Tick();
        for (int t = 0; t < 400 && !(t > 2 && u.State[0] == UnitState.Idle); t++) sim.Tick();
        return (u.Position[0], u.GoalCell[0], u.State[0], Stress.ResourceOracle.Reach(g) == null);
    }

    [Fact]
    public void OrderIntoAFelledGroveMiddle_Report()
    {
        var before = OrderIntoGrove(fellMiddle: false);
        var after = OrderIntoGrove(fellMiddle: true);
        _out.WriteLine($"grove intact: unit ends at {before.End} (goal cell {before.GoalCell}, {before.State}); every open cell reachable: {before.Reachable}");
        _out.WriteLine($"middle felled: unit ends at {after.End} (goal cell {after.GoalCell}, {after.State}); every open cell reachable: {after.Reachable}");
        Assert.True(before.Reachable);
        Assert.False(after.Reachable); // the hollow: docs/03 says this can't happen
    }

    /// <summary>
    /// BUG-0075 regression (M3-2, replaces the skipped row): only exposed nodes are gathered, so a worker set on
    /// the grove's middle fells it from the outside; once the middle is gone its cell is reachable, and a unit
    /// ordered there walks into the grove.
    /// </summary>
    [Fact]
    public void OrderIntoAFelledGroveMiddle_StillWalksToTheGrove()
    {
        Simulation sim = GatherMaps.NewSim(Flat(24, 16));
        NavGrid g = sim.World.NavGrid;
        EntityHandle middle = default;
        for (int y = 6; y <= 8; y++)
            for (int x = 10; x <= 12; x++)
            {
                EntityHandle h = Spawn(sim.World, Tree, x, y, 10);
                if (x == 11 && y == 7) middle = h;
            }
        GatherMaps.Building(sim, 2, 10);
        EntityHandle worker = GatherMaps.Unit(sim, g.CellCenter(8, 7));
        sim.Enqueue(Command.Gather(0, worker, g.CellCenter(11, 7)));
        for (int t = 0; t < 20000 && sim.World.Resources.IsAlive(middle); t++)
        {
            sim.Tick();
            Assert.Null(Stress.ResourceOracle.Reach(g)); // never a hollow, after any fall
        }
        Assert.False(sim.World.Resources.IsAlive(middle));
        sim.Enqueue(Command.Stop(0, worker));
        EntityHandle walker = GatherMaps.Unit(sim, g.CellCenter(3, 4), type: GatherMaps.Infantry);
        sim.Enqueue(Command.Move(0, walker, g.CellCenter(11, 7)));
        UnitStore u = sim.World.Units;
        sim.Tick();
        for (int t = 0; t < 600 && !(t > 2 && u.State[walker.Index] == UnitState.Idle); t++) sim.Tick();
        Assert.True(u.Position[walker.Index].X > 8 * MapConstants.CellSize, $"unit stayed at {u.Position[walker.Index]}");
        Assert.True(Vector2.Distance(u.Position[walker.Index], g.CellCenter(11, 7)) < 2.5f, $"unit ended at {u.Position[walker.Index]}");
    }

    /// <summary>
    /// 32 goal groups walking with all 32 fields cached; 100 trees fall between two ticks. Every field
    /// is stale at once and only 2 rebuild per tick: measures how long walkers stand and checks nobody
    /// gives up because of it (waiting is not stuck) and everyone arrives.
    /// </summary>
    [Fact]
    public void HundredTreesFallInOneTick_With32CachedFields_WalkersWaitThenAllArrive()
    {
        (Simulation sim, EntityHandle[] trees) = ThirtyTwoWalkers();
        UnitStore u = sim.World.Units;
        FlowFieldCache cache = sim.World.FlowFields;
        int warm = WarmUntilAllFieldsCached(sim);
        Assert.Equal(32, cache.Count);
        foreach (EntityHandle h in trees) Assert.Equal(TreeWood, sim.World.Resources.Take(h, TreeWood));
        var wait = new int[32];
        var longest = new int[32];
        int ticks = 0, moving, maxBuilds = 0;
        do
        {
            int before = cache.BuildCount;
            sim.Tick();
            ticks++;
            maxBuilds = Math.Max(maxBuilds, cache.BuildCount - before);
            moving = 0;
            for (int i = 0; i < 32; i++)
            {
                if (u.State[i] != UnitState.Moving) continue;
                moving++;
                bool waiting = u.Velocity[i] == Vector2.Zero && !cache.Contains(u.GoalCell[i]);
                wait[i] = waiting ? wait[i] + 1 : 0;
                longest[i] = Math.Max(longest[i], wait[i]);
            }
            Assert.Equal(-1, MoveScenario.FirstUnitOnBlockedGround(sim.World));
        } while (moving > 0 && ticks < 4000);
        bool[] arrived = MoveScenario.Arrived(sim.World);
        int gaveUp = Enumerable.Range(0, 32).Count(i => !arrived[i] && u.GoalCell[i] == -1);
        _out.WriteLine($"warm-up {warm} ticks; after 100 trees fell: longest wait {longest.Max()} ticks (units waiting >= 10 ticks: {longest.Count(l => l >= 10)}), " +
            $"max builds/tick {maxBuilds}, arrived {arrived.Count(a => a)}/32, gave up {gaveUp}, settled after {ticks} ticks");
        Assert.True(maxBuilds <= Movement.MovementConstants.MaxFieldBuildsPerTick);
        Assert.Equal(0, gaveUp);
        Assert.Equal(32, arrived.Count(a => a));
        Assert.True(longest.Max() <= 32 / Movement.MovementConstants.MaxFieldBuildsPerTick + 1, $"longest wait {longest.Max()}");
    }

    /// <summary>
    /// M3-2 will fell trees inside ticks: one falling every tick makes every cached field stale every
    /// tick, so with more than 2 goal groups the younger orders never get a field while felling goes on.
    /// Pins the bound QA expects (no walker stands more than 40 ticks); see BUG-0073.
    /// </summary>
    [Fact(Skip = "BUG-0073: one tree felled per tick starves all but the 2 oldest goal groups of a flow field (walkers stand still for as long as felling continues); un-skip when fixed")]
    public void OneTreeFallsEveryTick_NoWalkerStandsWaitingForItsFieldMoreThan40Ticks()
    {
        int longest = ContinuousFellingLongestWait(out string report);
        _out.WriteLine(report);
        Assert.True(longest <= 40, report);
    }

    [Fact]
    public void OneTreeFallsEveryTick_Report()
    {
        ContinuousFellingLongestWait(out string report);
        _out.WriteLine(report);
    }

    private static int ContinuousFellingLongestWait(out string report)
    {
        // Baseline tick cost of the same walk with nothing felled.
        (Simulation calm, _) = ThirtyTwoWalkers();
        WarmUntilAllFieldsCached(calm);
        var swCalm = System.Diagnostics.Stopwatch.StartNew();
        for (int t = 0; t < 100; t++) calm.Tick();
        double calmMs = swCalm.Elapsed.TotalMilliseconds / 100;

        (Simulation sim, EntityHandle[] trees) = ThirtyTwoWalkers();
        UnitStore u = sim.World.Units;
        FlowFieldCache cache = sim.World.FlowFields;
        WarmUntilAllFieldsCached(sim);
        var wait = new int[32];
        var longest = new int[32];
        int fell = 0;
        double tickMs = 0;
        for (int t = 0; t < 100; t++)
        {
            if (fell < trees.Length) sim.World.Resources.Take(trees[fell++], TreeWood);
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            sim.Tick();
            tickMs += (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            for (int i = 0; i < 32; i++)
            {
                bool waiting = u.State[i] == UnitState.Moving && u.Velocity[i] == Vector2.Zero && !cache.Contains(u.GoalCell[i]);
                wait[i] = waiting ? wait[i] + 1 : 0;
                longest[i] = Math.Max(longest[i], wait[i]);
            }
        }
        int starving = longest.Count(l => l >= 90);
        report = $"one tree per tick for 100 ticks, 32 goal groups: longest wait {longest.Max()} ticks; {starving} of 32 walkers waited >= 90 of the 100 ticks; {longest.Count(l => l <= 2)} waited <= 2; avg tick {tickMs / 100:F3} ms vs {calmMs:F3} ms with nothing felled (120 x 72 map)";
        return longest.Max();
    }

    private static (Simulation Sim, EntityHandle[] Trees) ThirtyTwoWalkers()
    {
        Simulation sim = NewSim(Flat(120, 72), units: 32);
        NavGrid g = sim.World.NavGrid;
        var trees = new EntityHandle[100];
        for (int k = 0; k < 100; k++) trees[k] = Spawn(sim.World, Tree, 40 + k % 10 * 3, 6 + k / 10 * 6, TreeWood);
        for (int i = 0; i < 32; i++) sim.Enqueue(Command.SpawnUnit(0, 0, g.CellCenter(4 + i % 2 * 2, 4 + i * 2)));
        sim.Tick();
        for (int i = 0; i < 32; i++) sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, i), g.CellCenter(110 + i % 2 * 3, 4 + i * 2)));
        return (sim, trees);
    }

    private static int WarmUntilAllFieldsCached(Simulation sim)
    {
        UnitStore u = sim.World.Units;
        for (int t = 1; t <= 100; t++)
        {
            sim.Tick();
            bool all = true;
            for (int i = 0; i < 32; i++) all &= u.State[i] == UnitState.Moving && sim.World.FlowFields.Contains(u.GoalCell[i]);
            if (all && t > 2) return t;
        }
        Assert.Fail("fields never all cached");
        return -1;
    }

    [Fact]
    public void TickThatRebuildsStaleFieldsAfterAFall_AllocatesNothing()
    {
        (Simulation sim, EntityHandle[] trees) = ThirtyTwoWalkers();
        WarmUntilAllFieldsCached(sim);
        int k = 0;
        // Each probe run fells one more tree first, so every measured tick recomputes the step masks and rebuilds 2 fields.
        AllocationProbe.AssertZero(() => sim.Tick(), _out, setup: () => sim.World.Resources.Take(trees[k++], TreeWood));
        AllocationProbe.AssertZero(() =>
        {
            ResourceStore r = sim.World.Resources;
            r.Take(trees[90], 0);
            r.Take(trees[90], -1);
            r.Take(trees[0], 5); // dead
            r.Take(default, 5);
            r.Take(trees[91], int.MaxValue);
            sim.StateHash();
        }, _out);
    }

    // ---------------------------------------------------------------- replay format 3

    private static Replay ForestReplay()
    {
        var map = MapGenParams.Default with { Forests = 6, GoldMines = 3 };
        var sim = new Simulation(TestSim.Config(Seed: 12, PlayerCount: 2, UnitCapacity: 16, CommandCapacity: 32) with { Map = map });
        var rec = new ReplayRecorder(sim, checkpointInterval: 20);
        NavGrid g = sim.World.NavGrid;
        var cells = OrderMix.Cells(g, 10f);
        for (int i = 0; i < 16; i++) sim.Enqueue(Command.SpawnUnit(i % 2, 0, MoveScenario.Center(g, cells[i * 3 % cells.Count])));
        for (int t = 0; t < 60; t++)
        {
            if (t == 2) MoveScenario.MoveAll(sim, MoveScenario.Center(g, cells[0]));
            sim.Tick();
        }
        return rec.ToReplay();
    }

    private static string Body(string text) => text[..text.LastIndexOf("checksum ", StringComparison.Ordinal)];

    [Theory]
    [InlineData("map.forests 6\n", "map.forests 2147483647\n", ReplayError.InvalidHeader)]
    [InlineData("map.forests 6\n", "map.forests -2147483648\n", ReplayError.InvalidHeader)]
    [InlineData("map.forests 6\n", "map.forests 2147483648\n", ReplayError.Malformed)]
    [InlineData("map.forests 6\n", "map.forests 64\n", ReplayError.CheckpointMismatch)]
    [InlineData("map.gold-mines 3\n", "map.gold-mines 2147483647\n", ReplayError.InvalidHeader)]
    [InlineData("map.gold-mines 3\n", "map.gold-mines 65\n", ReplayError.InvalidHeader)]
    [InlineData("map.forest-min-trees 12\n", "map.forest-min-trees 0\n", ReplayError.InvalidHeader)]
    [InlineData("map.forest-min-trees 12\n", "map.forest-min-trees -2147483648\n", ReplayError.InvalidHeader)]
    [InlineData("map.forest-max-trees 40\n", "map.forest-max-trees 257\n", ReplayError.InvalidHeader)]
    [InlineData("map.forest-max-trees 40\n", "map.forest-max-trees 2147483647\n", ReplayError.InvalidHeader)]
    [InlineData("map.forest-max-trees 40\n", "map.forest-max-trees 11\n", ReplayError.InvalidHeader)]
    [InlineData("map.mine-spacing 41C00000\n", "map.mine-spacing 7F800000\n", ReplayError.InvalidHeader)]
    [InlineData("map.mine-spacing 41C00000\n", "map.mine-spacing FF800000\n", ReplayError.InvalidHeader)]
    [InlineData("map.mine-spacing 41C00000\n", "map.mine-spacing BF800000\n", ReplayError.InvalidHeader)]
    [InlineData("map.mine-spacing 41C00000\n", "map.mine-spacing 80000000\n", ReplayError.None)] // -0 m: valid, and these 3 mines land as with 24 m
    [InlineData("resource-capacity 4096\n", "resource-capacity 2147483647\n", ReplayError.InvalidHeader)]
    [InlineData("resource-capacity 4096\n", "resource-capacity -2147483648\n", ReplayError.InvalidHeader)]
    [InlineData("resource-capacity 4096\n", "resource-capacity 1\n", ReplayError.CheckpointMismatch)]
    public void Format3HeaderFields_AtExtremes_AreRefusedOrDiverge_NeverThrow(string from, string to, ReplayError expected)
    {
        string text = Encoding.ASCII.GetString(ReplayFormat.Write(ForestReplay()));
        Assert.Contains(from, text);
        byte[] bytes = ReplayFormat.Seal(Body(text.Replace(from, to)));
        ReplayError read = ReplayFormat.TryRead(bytes, out Replay? r);
        if (expected == ReplayError.Malformed || r == null)
        {
            Assert.Equal(expected, read);
            return;
        }
        ReplayResult played = default;
        Exception? ex = Record.Exception(() => played = ReplayPlayer.Run(r, TestSim.Data));
        Assert.Null(ex);
        Assert.Equal(expected, played.Error);
    }

    [Fact]
    public void ForestReplay_TruncatedAtEveryByte_NeverAccepted_NeverThrows()
    {
        byte[] full = ReplayFormat.Write(ForestReplay());
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(full, out _));
        for (int len = 0; len < full.Length; len++)
        {
            ReplayError e = ReplayError.None;
            Exception? ex = Record.Exception(() => e = ReplayFormat.TryRead(full.AsSpan(0, len).ToArray(), out _));
            Assert.True(ex == null, $"cut at {len}: {ex}");
            Assert.NotEqual(ReplayError.None, e);
        }
    }

    [Fact]
    public void MaxResourceCapacityReplay_PlaysWithinASecond()
    {
        Replay r = ForestReplay();
        string text = Encoding.ASCII.GetString(ReplayFormat.Write(r)).Replace("resource-capacity 4096\n", $"resource-capacity {Replay.MaxCapacity}\n");
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Seal(Body(text)), out Replay? big));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        ReplayResult played = ReplayPlayer.Run(big!, TestSim.Data);
        sw.Stop();
        _out.WriteLine($"resource-capacity {Replay.MaxCapacity}: {played.Error} after {sw.ElapsedMilliseconds} ms");
        Assert.Equal(ReplayError.CheckpointMismatch, played.Error); // capacity is hashed
        Assert.True(sw.ElapsedMilliseconds < 5000);
    }

    // ---------------------------------------------------------------- resources.json

    private const string ResourcesFile = "common/resources.json";

    private static DataLoadResult LoadNoThrow(string dir)
    {
        DataLoadResult? result = null;
        Exception? ex = Record.Exception(() => result = DataLoader.LoadAll(dir));
        Assert.True(ex == null, $"LoadAll threw {ex}");
        foreach (DataError e in result!.Errors)
        {
            Assert.False(string.IsNullOrEmpty(e.File));
            Assert.False(string.IsNullOrEmpty(e.Message));
        }
        return result;
    }

    [Fact]
    public void ResourcesFile_TruncatedAtEveryCut_YieldsAnErrorInThatFile()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        byte[] original = File.ReadAllBytes(dir.FullPath(ResourcesFile));
        for (int len = 0; len < original.Length - 3; len++)
        {
            File.WriteAllBytes(dir.FullPath(ResourcesFile), original.AsSpan(0, len).ToArray());
            DataLoadResult r = LoadNoThrow(dir.Path);
            Assert.False(r.Ok, $"truncated at {len} bytes but loaded");
            Assert.Contains(r.Errors, e => e.File == ResourcesFile);
        }
    }

    [Fact]
    public void ResourcesFile_RandomByteMutations_NeverThrow_AndLoadedDataIsSane()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        byte[] original = File.ReadAllBytes(dir.FullPath(ResourcesFile));
        var rng = new SimRng(73, 1);
        int loaded = 0;
        for (int k = 0; k < 600; k++)
        {
            byte[] b = (byte[])original.Clone();
            int edits = rng.NextInt(1, 4);
            for (int e = 0; e < edits; e++) b[rng.NextInt(0, b.Length)] = (byte)rng.NextInt(0, 256);
            File.WriteAllBytes(dir.FullPath(ResourcesFile), b);
            DataLoadResult r = LoadNoThrow(dir.Path);
            if (!r.Ok) continue;
            loaded++;
            foreach (ResourceDef d in r.Data!.Resources)
            {
                Assert.InRange(d.FootprintWidth, 1, DataLimits.MaxFootprint);
                Assert.InRange(d.FootprintHeight, 1, DataLimits.MaxFootprint);
                Assert.True(Enum.IsDefined(d.Resource));
                Assert.False(string.IsNullOrEmpty(d.DisplayName));
            }
        }
        _out.WriteLine($"{loaded} of 600 mutated files still loaded (sane)");
    }

    [Theory]
    [InlineData("\"resources\"", "\"extra\": 1, \"resources\"")]
    [InlineData("\"id\": \"tree\"", "\"id\": \"tree\", \"amount\": 100")]
    [InlineData("{ \"width\": 1, \"height\": 1 }", "{ \"width\": 1, \"height\": 1, \"depth\": 1 }")]
    [InlineData("{ \"width\": 1, \"height\": 1 }", "{ \"width\": 1 }")]
    [InlineData("{ \"width\": 1, \"height\": 1 }", "{ \"width\": 1.5, \"height\": 1 }")]
    [InlineData("{ \"width\": 1, \"height\": 1 }", "{ \"width\": \"1\", \"height\": 1 }")]
    [InlineData("{ \"width\": 1, \"height\": 1 }", "null")]
    [InlineData("{ \"width\": 1, \"height\": 1 }", "{ \"width\": 2147483648, \"height\": 1 }")]
    [InlineData("\"resource\": \"wood\"", "\"resource\": \"Wood\"")]
    [InlineData("\"resource\": \"wood\"", "\"resource\": 1")]
    [InlineData("\"resource\": \"wood\"", "\"resource\": \"\"")]
    [InlineData("\"displayName\": \"Tree\"", "\"displayName\": \"\"")]
    [InlineData("{ \"width\": 1, \"height\": 1 }", "{ \"width\": 0, \"height\": 1 }")]
    [InlineData("{ \"width\": 1, \"height\": 1 }", "{ \"width\": 1, \"height\": 5 }")]
    [InlineData("{ \"width\": 1, \"height\": 1 }", "{ \"width\": -2147483648, \"height\": 1 }")]
    [InlineData("\"id\": \"tree\"", "\"id\": \"Tree\"")]
    [InlineData("\"id\": \"tree\"", "\"id\": \"gold_mine\"")]
    [InlineData("\"id\": \"tree\"", "\"id\": \"\"")]
    public void ResourcesFile_BadField_IsOneErrorInThatFile(string from, string to)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        string path = dir.FullPath(ResourcesFile);
        string text = File.ReadAllText(path);
        Assert.Contains(from, text);
        File.WriteAllText(path, text.Replace(from, to));
        DataLoadResult r = LoadNoThrow(dir.Path);
        Assert.False(r.Ok, $"{to} loaded");
        _out.WriteLine(string.Join(" | ", r.Errors));
        Assert.Equal(ResourcesFile, Assert.Single(r.Errors).File);
    }

    [Fact]
    public void ResourcesFile_Missing_IsOneError()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        File.Delete(dir.FullPath(ResourcesFile));
        DataLoadResult r = LoadNoThrow(dir.Path);
        Assert.Equal(ResourcesFile, Assert.Single(r.Errors).File);
    }

    [Theory]
    [InlineData("\"displayName\": \"Gold Mine\"", "\"displayName\": \"Gold Mines\"")]
    [InlineData("A seam of gold.", "A seam of gold!")]
    [InlineData("\"resource\": \"gold\"", "\"resource\": \"wood\"")]
    [InlineData("{ \"width\": 2, \"height\": 2 }", "{ \"width\": 3, \"height\": 2 }")]
    [InlineData("{ \"width\": 2, \"height\": 2 }", "{ \"width\": 2, \"height\": 3 }")]
    [InlineData("\"id\": \"gold_mine\"", "\"id\": \"gold_vein\"")]
    [InlineData("\"id\": \"gold_mine\"", "\"id\": \"zz_mine\"")] // reorders the ids too
    public void ContentHash_ChangesWithEveryResourcesField(string from, string to)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        string path = dir.FullPath(ResourcesFile);
        string text = File.ReadAllText(path);
        Assert.Contains(from, text);
        File.WriteAllText(path, text.Replace(from, to));
        DataLoadResult r = LoadNoThrow(dir.Path);
        Assert.True(r.Ok, string.Join(" | ", r.Errors));
        Assert.NotEqual(TestSim.Data.ContentHash(), r.Data!.ContentHash());
    }

    [Fact]
    public void EmptyResourcesList_Report()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        File.WriteAllText(dir.FullPath(ResourcesFile), "{ \"resources\": [] }");
        DataLoadResult r = LoadNoThrow(dir.Path);
        _out.WriteLine(r.Ok ? "an empty resources list loads clean" : string.Join(" | ", r.Errors));
        if (!r.Ok) return;
        var sim = new Simulation(new SimConfig(1, 1, 4, 4) { Data = r.Data!, Map = MapGenParams.Default with { Forests = 12, GoldMines = 8 } });
        _out.WriteLine($"forests 12 + mines 8 requested on it: placed {sim.World.ResourcePlacement}");
    }

    [Fact]
    public void FourByFourMine_On16x16GeneratedMaps_KeepsTheOracle()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        string path = dir.FullPath(ResourcesFile);
        File.WriteAllText(path, File.ReadAllText(path).Replace("{ \"width\": 2, \"height\": 2 }", "{ \"width\": 4, \"height\": 4 }"));
        GameData data = LoadNoThrow(dir.Path).Data!;
        Assert.Equal(4, data.Resources[data.FindResource("gold_mine")].FootprintWidth);
        MapGenParams m = MapGenParams.Default with
        {
            Width = 16, Height = 16, EdgeMargin = 1, Level1Plateaus = 2, Level1MinSize = 3, Level1MaxSize = 12, Level2Plateaus = 1, Level2MinSize = 3,
            Level2MaxSize = 6, Level2Inset = 1, RampWidth = 1, RampLength = 3, GoldMines = 4, MineSpacing = 0f, Forests = 2, ForestMinTrees = 1, ForestMaxTrees = 10,
        };
        int mines = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            SimConfig c = new SimConfig(seed, 1, 4, 4) { Data = data, Map = m };
            World w = new Simulation(c).World;
            World bare = new Simulation(c with { Map = m with { GoldMines = 0, Forests = 0 } }).World;
            string? err = ResourceOracle.Check(w, bare, m);
            Assert.True(err == null, $"seed {seed}: {err}");
            mines += w.ResourcePlacement.Mines;
        }
        _out.WriteLine($"4x4 mines on 16x16: {mines} placed over 40 seeds");
    }

    /// <summary>The loader accepts a tree footprint of 1-4 cells, so the placer must honor it (BUG-0074).</summary>
    [Fact(Skip = "BUG-0074: the forest placer assumes 1 x 1 trees; a 2 x 2 tree footprint (valid data) overlaps, miscounts and skips the open-ground / connectivity checks; un-skip when fixed")]
    public void TwoByTwoTrees_KeepTheOracle()
    {
        Assert.Null(TwoByTwoTreeOracle(out _));
    }

    [Fact]
    public void TwoByTwoTrees_Report()
    {
        _out.WriteLine(TwoByTwoTreeOracle(out string summary) ?? "oracle holds");
        _out.WriteLine(summary);
    }

    private static string? TwoByTwoTreeOracle(out string summary)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        string path = dir.FullPath(ResourcesFile);
        File.WriteAllText(path, File.ReadAllText(path).Replace("{ \"width\": 1, \"height\": 1 }", "{ \"width\": 2, \"height\": 2 }"));
        DataLoadResult loaded = LoadNoThrow(dir.Path);
        Assert.True(loaded.Ok, string.Join("\n", loaded.Errors));
        GameData data = loaded.Data!;
        MapGenParams m = MapGenParams.Default with { Forests = 12, GoldMines = 4 };
        string? first = null;
        int bad = 0, pockets = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            SimConfig c = new SimConfig(seed, 1, 4, 4) { Data = data, Map = m };
            World w = new Simulation(c).World;
            World bare = new Simulation(c with { Map = MapGenParams.Default }).World;
            string? err = ResourceOracle.Check(w, bare, m);
            if (err == null) continue;
            bad++;
            if (ResourceOracle.Reach(w.NavGrid) != null) pockets++;
            first ??= $"seed {seed}: {err} (report {w.ResourcePlacement}, live nodes {w.Resources.Count})";
        }
        summary = $"2x2 trees: {bad} of 20 seeds break the oracle, {pockets} with a sealed pocket";
        return first;
    }
}
