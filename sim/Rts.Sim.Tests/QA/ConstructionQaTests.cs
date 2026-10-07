using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;
using static Rts.Sim.Tests.BuildMaps;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M3-3, session 2026-10-06-2114): placement, construction, cancel and repair attacked independently of the
/// developer's suites: the placement rule against a whole-map flood oracle at every anchor of hand-made sealing
/// geometries (forest rings, ramp mouths, corridors that only two placements close, mines, map edges and corners),
/// refund / repair conservation under exploit loops, Build floods, builders freed mid-build, push-out crowds.
/// </summary>
[Collection(SerialCollection.Name)]
public class ConstructionQaTests
{
    private readonly ITestOutputHelper _out;

    public ConstructionQaTests(ITestOutputHelper output) => _out = output;

    private static int Barracks => TestSim.Data.FindBuilding("malazan_barracks");
    private static BuildingDef Def(int type) => TestSim.Data.Buildings[type];

    // ------------------------------------------------------------------ maps

    /// <summary>32 x 32 flat: a one-tree-thick square ring of trees (8..20) with a 2-cell gap at the top (x 13-14).</summary>
    private static Simulation ForestRing(int gap = 2)
    {
        Simulation sim = BuildMaps.NewSim(Flat(32, 32), players: 2);
        for (int k = 8; k <= 20; k++)
        {
            if (!(k >= 13 && k < 13 + gap)) Spawn(sim.World, Tree, k, 8, TreeWood);
            Spawn(sim.World, Tree, k, 20, TreeWood);
            if (k > 8 && k < 20)
            {
                Spawn(sim.World, Tree, 8, k, TreeWood);
                Spawn(sim.World, Tree, 20, k, TreeWood);
            }
        }
        return sim;
    }

    /// <summary>30 x 20: level 0 west, a 3-wide ramp at x = 12 (y 8-10), a level-1 plateau from x = 13.</summary>
    private static Simulation RampMouth()
    {
        var rows = new string[20];
        for (int y = 0; y < 20; y++)
            rows[y] = new string('0', 12) + (y is >= 8 and <= 10 ? "r" : "0") + new string('1', 17);
        return BuildMaps.NewSim(FromRows(rows), players: 2);
    }

    /// <summary>40 x 20 flat: a tree wall at x = 10 with a 4-tall gap (y 5-8) and one at x = 25 with a 1-cell gap (y 12).</summary>
    private static Simulation Corridors()
    {
        Simulation sim = BuildMaps.NewSim(Flat(40, 20), players: 2);
        for (int y = 1; y < 19; y++)
        {
            if (y is < 5 or > 8) Spawn(sim.World, Tree, 10, y, TreeWood);
            if (y != 12) Spawn(sim.World, Tree, 25, y, TreeWood);
        }
        return sim;
    }

    /// <summary>30 x 30 flat: a wall of touching 2 x 2 gold mines across y 14-15 with one 2-cell gap (x 13-14).</summary>
    private static Simulation MineRow()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 30), players: 2);
        foreach (int x in new[] { 1, 3, 5, 7, 9, 11, 15, 17, 19, 21, 23, 25, 27 })
            Spawn(sim.World, Mine, x, 14, 1000);
        return sim;
    }

    // ------------------------------------------------------------------ oracle sweep

    /// <summary>
    /// Every anchor of every footprint size: Blocked exactly when a footprint cell is impassable, a ramp, or on another
    /// level than the anchor; otherwise SealsGround exactly when the flood oracle says the footprint splits connected
    /// cells. Returns how many anchors were sealing.
    /// </summary>
    private int SweepAgainstOracle(Simulation sim, string name)
    {
        NavGrid g = sim.World.NavGrid;
        Give(sim, 0, 100_000, 100_000);
        int seals = 0, open = 0;
        foreach (int type in new[] { House, Barracks, Keep })
        {
            BuildingDef d = Def(type);
            for (int c = 0; c < g.Width * g.Height; c++)
            {
                int x0 = c % g.Width, y0 = c / g.Width;
                sim.World.CanPlace(0, type, c, out PlacementError why);
                if (x0 + d.FootprintWidth > g.Width || y0 + d.FootprintHeight > g.Height)
                {
                    Assert.Equal(PlacementError.OffMap, why);
                    continue;
                }
                bool badCell = false;
                for (int y = y0; y < y0 + d.FootprintHeight; y++)
                    for (int x = x0; x < x0 + d.FootprintWidth; x++)
                        badCell |= !g.IsPassable(x, y) || (g.FlagsAt(x, y) & NavFlags.Ramp) != 0 || g.LevelAt(x, y) != g.LevelAt(x0, y0);
                Assert.True(badCell == (why == PlacementError.Blocked), $"{name}: type {d.Id} at ({x0}, {y0}): {why}, bad cell {badCell}");
                if (badCell) continue;
                bool oracle = SealOracle.Seals(g, x0, y0, d.FootprintWidth, d.FootprintHeight);
                Assert.True(oracle == (why == PlacementError.SealsGround), $"{name}: type {d.Id} at ({x0}, {y0}): CanPlace {why}, oracle seals {oracle}");
                if (oracle) seals++; else open++;
            }
        }
        _out.WriteLine($"{name}: {seals} sealing anchors, {open} open anchors, all agree with the flood oracle");
        return seals;
    }

    [Fact]
    public void EveryAnchor_OnSealingGeometries_AgreesWithTheFloodOracle()
    {
        Assert.True(SweepAgainstOracle(ForestRing(), "forest ring, 2-gap") > 0);
        Assert.True(SweepAgainstOracle(ForestRing(gap: 1), "forest ring, 1-gap") > 0);
        Assert.True(SweepAgainstOracle(RampMouth(), "ramp mouth") > 0);
        Assert.True(SweepAgainstOracle(Corridors(), "corridors") > 0);
        Assert.True(SweepAgainstOracle(MineRow(), "mine row") > 0);
        SweepAgainstOracle(BuildMaps.NewSim(Flat(24, 24)), "flat (edges and corners)");
    }

    [Fact]
    public void MapCorners_AndEdges_ArePlaceable_OnFlatGround()
    {
        Simulation sim = BuildMaps.NewSim(Flat(32, 24));
        Give(sim, 0, 10_000, 10_000);
        foreach (int type in new[] { House, Barracks, Keep })
        {
            int fw = Def(type).FootprintWidth, fh = Def(type).FootprintHeight;
            foreach ((int x, int y) in new[] { (1, 1), (31 - fw, 1), (1, 23 - fh), (31 - fw, 23 - fh), (10, 1), (1, 10), (31 - fw, 10), (10, 23 - fh) })
            {
                Assert.True(sim.World.CanPlace(0, type, Cell(sim, x, y), out PlacementError why), $"type {Def(type).Id} at ({x}, {y}): {why}");
            }
            // One cell further is the border ring (blocked) or off the map.
            Assert.False(sim.World.CanPlace(0, type, Cell(sim, 0, 1), out _));
            Assert.False(sim.World.CanPlace(0, type, Cell(sim, 32 - fw, 1), out _));
            Assert.False(sim.World.CanPlace(0, type, Cell(sim, 1, 24 - fh), out _));
        }
    }

    [Fact]
    public void AHouseClosingTheLastGapOfAForestRing_IsRefused_ByEveryRoute_AndNobodyPays()
    {
        Simulation sim = ForestRing();
        Give(sim, 0, 1000, 1000);
        EntityHandle w = Unit(sim, At(sim, 4, 4));
        int gold = sim.World.Gold[0], wood = sim.World.Wood[0];
        foreach ((int x, int y) in new[] { (13, 7), (13, 8), (12, 7), (14, 7), (12, 8), (14, 8) })
        {
            Assert.False(sim.World.CanPlace(0, House, Cell(sim, x, y), out PlacementError why));
            Assert.True(why is PlacementError.SealsGround or PlacementError.Blocked, $"({x}, {y}): {why}");
        }
        Assert.False(sim.World.CanPlace(0, House, Cell(sim, 13, 7), out PlacementError mouth));
        Assert.Equal(PlacementError.SealsGround, mouth);
        sim.Enqueue(Command.Build(0, w, House, At(sim, 13, 7)));
        sim.Enqueue(Command.Build(0, w, House, At(sim, 13, 8), queued: true));
        sim.Enqueue(Command.SpawnBuilding(0, House, At(sim, 13, 7)));
        Run(sim, 60);
        Assert.Equal(0, sim.World.Buildings.Count);
        Assert.Equal((gold, wood), (sim.World.Gold[0], sim.World.Wood[0]));
        // Inside the ring a House is fine (the interior stays one region round it).
        Assert.True(sim.World.CanPlace(0, House, Cell(sim, 13, 13), out _));
    }

    [Fact]
    public void AKeepAcrossTheRampMouth_IsRefused()
    {
        Simulation sim = RampMouth();
        Give(sim, 0, 1000, 1000);
        NavGrid g = sim.World.NavGrid;
        // Keeps on the plateau right above the ramp (x = 13 is cliff except the mouth, y 8-10).
        var sealing = new List<(int X, int Y)>();
        for (int y0 = 4; y0 <= 10; y0++)
            for (int x0 = 13; x0 <= 15; x0++)
            {
                sim.World.CanPlace(0, Keep, Cell(sim, x0, y0), out PlacementError why);
                _out.WriteLine($"Keep at ({x0}, {y0}) by the ramp mouth: {why}");
                if (why == PlacementError.SealsGround) sealing.Add((x0, y0));
            }
        Assert.NotEmpty(sealing);
        EntityHandle w = Unit(sim, At(sim, 4, 9));
        (int sx, int sy) = sealing[0];
        sim.Enqueue(Command.Build(0, w, Keep, At(sim, sx, sy)));
        Run(sim, 2);
        Assert.Equal(0, sim.World.Buildings.Count);
        Assert.Equal((1200, 1200), (sim.World.Gold[0], sim.World.Wood[0]));
    }

    [Fact]
    public void TwoPlacementsThatSealOnlyTogether_TheSecondIsRefused()
    {
        Simulation sim = Corridors();
        Give(sim, 0, 1000, 1000);
        EntityHandle w = Unit(sim, At(sim, 5, 10));
        // The 4-tall gap at x = 10, y 5-8: a House at (10, 5) leaves y 7-8 open; a House at (10, 7) would close it.
        Assert.True(sim.World.CanPlace(0, House, Cell(sim, 10, 5), out _));
        Assert.True(sim.World.CanPlace(0, House, Cell(sim, 10, 7), out _));
        sim.Enqueue(Command.Build(0, w, House, At(sim, 10, 5)));
        Run(sim, 2);
        Assert.Equal(1, sim.World.Buildings.Count);
        Assert.False(sim.World.CanPlace(0, House, Cell(sim, 10, 7), out PlacementError why));
        Assert.Equal(PlacementError.SealsGround, why);
        Assert.False(sim.World.CanPlace(0, House, Cell(sim, 9, 7), out why));
        Assert.Equal(PlacementError.SealsGround, why);
        // Same tick, two workers, both halves: the first places, the second is refused.
        Simulation twin = Corridors();
        Give(twin, 0, 1000, 1000);
        EntityHandle a = Unit(twin, At(twin, 5, 10)), b = Unit(twin, At(twin, 5, 12));
        twin.Enqueue(Command.Build(0, a, House, At(twin, 10, 5)));
        twin.Enqueue(Command.Build(0, b, House, At(twin, 10, 7)));
        Run(twin, 2);
        Assert.Equal(1, twin.World.Buildings.Count);
        Assert.Equal(-1, SealOracle.LostConnection(SealOracle.Labels(Corridors().World.NavGrid), SealOracle.Labels(twin.World.NavGrid)));
    }

    [Fact]
    public void ACancelledSiteEnclosedByOwnBuildings_LeavesAPocket_ThatLaterPlacementsKeep()
    {
        // Documented exception (docs/03): a cancelled or destroyed enclosed building leaves a pocket. Check the rule after it.
        Simulation sim = BuildMaps.NewSim(Flat(30, 30));
        Give(sim, 0, 10_000, 10_000);
        EntityHandle w = Unit(sim, At(sim, 3, 3));
        // A House site at (12, 12), then four Barracks round it (each leaves the outside connected: the site is a wall).
        sim.Enqueue(Command.Build(0, w, House, At(sim, 12, 12)));
        Run(sim, 2);
        foreach ((int x, int y) in new[] { (9, 9), (12, 9), (9, 12), (14, 11) })
        {
            sim.Enqueue(Command.SpawnBuilding(0, Barracks, At(sim, x, y)));
            Run(sim, 2);
        }
        int count = sim.World.Buildings.Count;
        _out.WriteLine($"{count} buildings round the site");
        sim.Enqueue(Command.Cancel(0, At(sim, 12, 12)));
        Run(sim, 2);
        NavGrid g = sim.World.NavGrid;
        int[] labels = SealOracle.Labels(g);
        int regions = labels.Where(l => l >= 0).Distinct().Count();
        _out.WriteLine($"after the cancel: {regions} passable regions");
        // Whatever the regions are now, the rule must still agree with the oracle everywhere.
        SweepAgainstOracle(sim, "after an enclosed cancel");
    }

    // ------------------------------------------------------------------ formula and destruction

    [Theory]
    [InlineData(1, 400)]
    [InlineData(2, 300)]
    [InlineData(4, 200)]
    [InlineData(8, 120)]
    [InlineData(6, 150)]
    public void AHouseWithNBuilders_TakesTTimes3OverNPlus2_Ticks(int n, int expected)
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 24));
        SetTotals(sim, 0, 1000, 1000);
        EntityHandle[] ws = WorkersRound(sim, 10, 10, 2, 2, n);
        foreach (EntityHandle w in ws) sim.Enqueue(Command.Build(0, w, House, At(sim, 10, 10)));
        sim.Tick(); // the Builds apply in the next tick, which already counts as one tick of work
        BuildingStore b = sim.World.Buildings;
        int ticks = 0;
        do { sim.Tick(); ticks++; } while (b.Count == 1 && b.UnderConstruction[SiteAt(sim, 10, 10)] && ticks < 1000);
        _out.WriteLine($"House, {n} builders: {ticks} ticks (docs/02: 20 s x 3 / ({n} + 2) = {expected})");
        Assert.Equal(expected, ticks);
        Assert.Equal(500, b.Hp[SiteAt(sim, 10, 10)]);
    }

    [Fact]
    public void DestroyingABuilding_IsAnOpeningChange_PlacingASiteIsAClosingOne()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 24));
        SetTotals(sim, 0, 1000, 1000);
        NavGrid g = sim.World.NavGrid;
        EntityHandle w = Unit(sim, At(sim, 4, 4));
        int v = g.Version, bv = g.BlockVersion;
        sim.Enqueue(Command.Build(0, w, House, At(sim, 10, 10)));
        Run(sim, 2);
        Assert.Equal((v + 1, bv + 1), (g.Version, g.BlockVersion));
        BuildingStore b = sim.World.Buildings;
        EntityHandle site = b.HandleOf(SiteAt(sim, 10, 10));
        b.Damage(site, 1); // a site at 1 hp: destroyed
        Assert.False(b.IsAlive(site));
        Assert.Equal((v + 2, bv + 1), (g.Version, g.BlockVersion));
        Assert.True(g.IsPassable(10, 10) && g.IsPassable(11, 11));
        b.Damage(site, 5); // stale handle: nothing
        Assert.Equal(v + 2, g.Version);
        Run(sim, 2);
        Assert.Equal(UnitState.Idle, sim.World.Units.State[w.Index]);
    }

    /// <summary>
    /// BUG-0078 through player commands only: a House site beside a tree whose other sides are forest, walled in by
    /// three own Houses (each legal: the site is already a wall), then cancelled. Its cells reopen as a pocket, the tree
    /// is "exposed" to it again, and a worker sent to that tree is in BUG-0078's situation. Reports what the worker does.
    /// </summary>
    [Fact(Skip = "BUG-0093: a cancelled walled-in site leaves a pocket; a worker sent to a tree exposed only to it gathers nothing for ever (BUG-0078 via player commands); un-skip when fixed")]
    public void ACancelledSiteBesideATree_LeavesThePocketBug0078Described_Report()
    {
        Simulation sim = BuildMaps.NewSim(Flat(40, 30));
        SetTotals(sim, 0, 1000, 1000);
        EntityHandle tree = Spawn(sim.World, Tree, 20, 15, TreeWood);
        Spawn(sim.World, Tree, 19, 15, TreeWood);
        Spawn(sim.World, Tree, 20, 14, TreeWood);
        Spawn(sim.World, Tree, 20, 16, TreeWood);
        Building(sim, 30, 4); // a drop-off
        EntityHandle w = Unit(sim, At(sim, 26, 22));
        foreach ((int x, int y) in new[] { (21, 15), (21, 13), (21, 17), (23, 15) })
        {
            Assert.True(sim.World.CanPlace(0, House, Cell(sim, x, y), out PlacementError why), $"({x}, {y}): {why}");
            sim.Enqueue(Command.Build(0, w, House, At(sim, x, y)));
            Run(sim, 2);
        }
        Assert.Equal(5, sim.World.Buildings.Count);
        sim.Enqueue(Command.Cancel(0, At(sim, 21, 15)));
        Run(sim, 2);
        NavGrid g = sim.World.NavGrid;
        int[] labels = SealOracle.Labels(g);
        bool pocket = labels[Cell(sim, 21, 15)] != labels[Cell(sim, 26, 22)];
        sim.Enqueue(Command.Gather(0, w, At(sim, 20, 15)));
        Run(sim, 1200);
        UnitStore u = sim.World.Units;
        _out.WriteLine($"pocket after the cancel: {pocket}; after 60 s on the tree: state {u.State[w.Index]}, node {u.GatherNode[w.Index].Index} (tree {tree.Index}), cargo {u.Cargo[w.Index]}, wood {sim.World.Wood[0]}, at {u.Position[w.Index]}");
        Assert.True(pocket, "expected the cancel to leave a pocket");
        Assert.False(u.GatherNode[w.Index] == tree && u.Cargo[w.Index] == 0, "BUG-0078: the worker retries a tree it can only reach through a pocket, for ever");
    }

    // ------------------------------------------------------------------ cancel / refund exploits

    [Fact]
    public void CancelRebuildLoop_ConservesResourcesExactly()
    {
        Simulation sim = BuildMaps.NewSim(Flat(64, 24));
        SetTotals(sim, 0, 5000, 5000);
        EntityHandle[] ws = Enumerable.Range(0, 3).Select(i => Unit(sim, At(sim, 4 + i, 4))).ToArray();
        BuildingStore b = sim.World.Buildings;
        var rng = new Determinism.SimRng(42, 3);
        long spentGold = 0, spentWood = 0, refundGold = 0, refundWood = 0;
        int loops = 0, completed = 0;
        for (int k = 0; k < 120; k++)
        {
            int type = k % 3 == 0 ? Keep : House;
            BuildingDef d = Def(type);
            int x = 4 + (k % 10) * 5, y = 8 + (k / 10 % 2) * 6;
            if (SiteAt(sim, x, y) >= 0 || !sim.World.CanPlace(0, type, Cell(sim, x, y), out _)) continue;
            foreach (EntityHandle w in ws.Take(1 + k % 3)) sim.Enqueue(Command.Build(0, w, type, At(sim, x, y)));
            int gold = sim.World.Gold[0], wood = sim.World.Wood[0];
            Run(sim, 2);
            int s = SiteAt(sim, x, y);
            Assert.True(s >= 0);
            Assert.Equal(gold - d.CostGold, sim.World.Gold[0]);
            Assert.Equal(wood - d.CostWood, sim.World.Wood[0]);
            spentGold += d.CostGold;
            spentWood += d.CostWood;
            Run(sim, rng.NextInt(0, k % 7 == 0 ? 1500 : 200));
            sim.Enqueue(Command.Cancel(0, At(sim, x + 1, y)));
            sim.Tick();
            bool site = b.Alive[s] && b.UnderConstruction[s];
            long work = b.Work[s], needed = b.WorkNeeded(type);
            gold = sim.World.Gold[0];
            wood = sim.World.Wood[0];
            sim.Tick(); // the Cancel applies first thing in this tick
            if (site)
            {
                long rg = d.CostGold * (needed - work) / needed, rw = d.CostWood * (needed - work) / needed;
                Assert.False(b.Alive[s]);
                Assert.Equal(gold + rg, sim.World.Gold[0]);
                Assert.Equal(wood + rw, sim.World.Wood[0]);
                refundGold += rg;
                refundWood += rw;
                loops++;
            }
            else
            {
                Assert.True(b.Alive[s]); // finished: the Cancel is dropped
                Assert.Equal((gold, wood), (sim.World.Gold[0], sim.World.Wood[0]));
                completed++;
            }
            Assert.True(sim.World.Gold[0] >= 0 && sim.World.Wood[0] >= 0);
        }
        _out.WriteLine($"{loops} cancels, {completed} completions; spent {spentGold}/{spentWood}, refunded {refundGold}/{refundWood}");
        Assert.Equal(5000 - spentGold + refundGold, sim.World.Gold[0]);
        Assert.Equal(5000 - spentWood + refundWood, sim.World.Wood[0]);
        Assert.True(loops > 20 && completed > 0, $"{loops} cancels, {completed} completions");
        Assert.True(refundGold <= spentGold && refundWood <= spentWood);
    }

    [Fact]
    public void BuildThenCancelInOneTick_RefundsAll_CancelBeforeBuild_IsDropped()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20));
        SetTotals(sim, 0, 1000, 1000);
        EntityHandle w = Unit(sim, At(sim, 4, 4));
        sim.Enqueue(Command.Build(0, w, Keep, At(sim, 10, 10)));
        sim.Enqueue(Command.Cancel(0, At(sim, 11, 11)));
        Run(sim, 2);
        Assert.Equal(0, sim.World.Buildings.Count);
        Assert.Equal((1000, 1000), (sim.World.Gold[0], sim.World.Wood[0]));
        Assert.Equal(UnitState.Idle, sim.World.Units.State[w.Index]);
        Assert.Equal(default, sim.World.Units.BuildTarget[w.Index]);

        sim.Enqueue(Command.Cancel(0, At(sim, 11, 11)));
        sim.Enqueue(Command.Build(0, w, Keep, At(sim, 10, 10)));
        Run(sim, 2);
        Assert.Equal(1, sim.World.Buildings.Count);
        Assert.Equal((725, 725), (sim.World.Gold[0], sim.World.Wood[0]));
    }

    [Fact]
    public void CancelEdges_AtZero_OneBeforeDone_Finished_Enemy_AndNothing()
    {
        Simulation sim = BuildMaps.NewSim(Flat(40, 24), players: 2);
        SetTotals(sim, 0, 1000, 1000);
        SetTotals(sim, 1, 1000, 1000);
        BuildingStore b = sim.World.Buildings;
        EntityHandle w0 = Unit(sim, At(sim, 9, 9));
        EntityHandle w1 = Unit(sim, At(sim, 30, 4), player: 1);
        sim.Enqueue(Command.Build(0, w0, House, At(sim, 10, 10)));
        sim.Enqueue(Command.Build(1, w1, WhirlwindHouse, At(sim, 30, 10)));
        Run(sim, 2);
        int s0 = SiteAt(sim, 10, 10), s1 = SiteAt(sim, 30, 10);
        Assert.True(s0 >= 0 && s1 >= 0);
        // Enemy cancels are dropped, and so are cancels on open ground.
        sim.Enqueue(Command.Cancel(1, At(sim, 10, 10)));
        sim.Enqueue(Command.Cancel(0, At(sim, 30, 10)));
        sim.Enqueue(Command.Cancel(0, At(sim, 20, 20)));
        sim.Enqueue(Command.Cancel(0, new Vector2(-5f, 3f)));
        Run(sim, 2);
        Assert.True(b.Alive[s0] && b.Alive[s1]);
        Assert.Equal((1000, 950), (sim.World.Gold[0], sim.World.Wood[0]));
        Assert.Equal((1000, 950), (sim.World.Gold[1], sim.World.Wood[1]));
        // One work unit before done: refund floor(50 x 1 / 1200) = 0.
        sim.Enqueue(Command.Stop(0, w0)); // no more work
        Run(sim, 2);
        b.SetWork(s0, b.WorkNeeded(House) - 1);
        Assert.True(b.UnderConstruction[s0]);
        sim.Enqueue(Command.Cancel(0, At(sim, 11, 11)));
        Run(sim, 2);
        Assert.False(b.Alive[s0]);
        Assert.Equal((1000, 950), (sim.World.Gold[0], sim.World.Wood[0]));
        // A finished building can't be cancelled.
        b.SetWork(s1, b.WorkNeeded(WhirlwindHouse));
        Assert.False(b.UnderConstruction[s1]);
        sim.Enqueue(Command.Cancel(1, At(sim, 30, 10)));
        Run(sim, 2);
        Assert.True(b.Alive[s1]);
        Assert.Equal((1000, 950), (sim.World.Gold[1], sim.World.Wood[1]));
    }

    [Fact]
    public void CancelWhileBuildersWalk_IdlesThemWhereTheyStand_FullRefund()
    {
        Simulation sim = BuildMaps.NewSim(Flat(64, 20));
        SetTotals(sim, 0, 1000, 1000);
        EntityHandle[] ws = Enumerable.Range(0, 4).Select(i => Unit(sim, At(sim, 3, 3 + 2 * i))).ToArray();
        foreach (EntityHandle w in ws) sim.Enqueue(Command.Build(0, w, Keep, At(sim, 50, 8)));
        Run(sim, 12);
        UnitStore u = sim.World.Units;
        Assert.All(ws, w => Assert.Equal(UnitState.Moving, u.State[w.Index]));
        Assert.Equal(0, sim.World.Buildings.Work[SiteAt(sim, 50, 8)]);
        sim.Enqueue(Command.Cancel(0, At(sim, 52, 10)));
        Run(sim, 2);
        Assert.Equal(0, sim.World.Buildings.Count);
        Assert.Equal((1000, 1000), (sim.World.Gold[0], sim.World.Wood[0]));
        Vector2[] at = ws.Select(w => u.Position[w.Index]).ToArray();
        Run(sim, 40);
        for (int k = 0; k < ws.Length; k++)
        {
            Assert.Equal(UnitState.Idle, u.State[ws[k].Index]);
            Assert.Equal(default, u.BuildTarget[ws[k].Index]);
            Assert.True(Vector2.Distance(at[k], u.Position[ws[k].Index]) < 1.5f, $"worker {k} kept walking");
        }
    }

    // ------------------------------------------------------------------ Build floods and races

    [Fact]
    public void SameAnchorFromBothPlayersInOneTick_ExactlyOnePays()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20), players: 2);
        SetTotals(sim, 0, 1000, 1000);
        SetTotals(sim, 1, 1000, 1000);
        EntityHandle a = Unit(sim, At(sim, 4, 4)), b = Unit(sim, At(sim, 25, 4), player: 1);
        // Player 1's command first in time: application order is (player, sequence), so player 0 still wins.
        sim.Enqueue(Command.Build(1, b, WhirlwindHouse, At(sim, 12, 10)));
        sim.Enqueue(Command.Build(0, a, House, At(sim, 12, 10)));
        Run(sim, 2);
        Assert.Equal(1, sim.World.Buildings.Count);
        Assert.Equal(0, sim.World.Buildings.Owner[SiteAt(sim, 12, 10)]);
        Assert.Equal((1000, 950), (sim.World.Gold[0], sim.World.Wood[0]));
        Assert.Equal((1000, 1000), (sim.World.Gold[1], sim.World.Wood[1]));
        Assert.Equal(default, sim.World.Units.BuildTarget[b.Index]);
        // An overlapping (not identical) anchor of the same type from the same player: dropped, not joined, not paid.
        sim.Enqueue(Command.Build(0, a, House, At(sim, 13, 11)));
        Run(sim, 2);
        Assert.Equal(1, sim.World.Buildings.Count);
        Assert.Equal((1000, 950), (sim.World.Gold[0], sim.World.Wood[0]));
    }

    [Fact]
    public void FourThousandBuildsAtOneAnchor_OneSite_OnePayment_EveryWorkerJoins()
    {
        var sim = new Simulation(TestSim.Config(Seed: 5, PlayerCount: 1, UnitCapacity: 64, CommandCapacity: 4200), Flat(48, 48));
        SetTotals(sim, 0, 1000, 1000);
        for (int i = 0; i < 64; i++) sim.Enqueue(Command.SpawnUnit(0, Laborer, At(sim, 4 + i % 16 * 2, 4 + i / 16 * 2)));
        Run(sim, 2);
        UnitStore u = sim.World.Units;
        for (int k = 0; k < 4096; k++)
            sim.Enqueue(Command.Build(0, new EntityHandle(k % 64, u.Generation[k % 64]), Keep, At(sim, 20, 30)));
        long t0 = Stopwatch.GetTimestamp();
        Run(sim, 2);
        double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
        _out.WriteLine($"4,096 Builds at one anchor: {ms:F1} ms for the two ticks");
        Assert.Equal(1, sim.World.Buildings.Count);
        Assert.Equal((725, 725), (sim.World.Gold[0], sim.World.Wood[0]));
        EntityHandle site = sim.World.Buildings.HandleOf(SiteAt(sim, 20, 30));
        for (int i = 0; i < 64; i++) Assert.Equal(site, u.BuildTarget[i]);
    }

    [Fact]
    public void FourThousandBuildsAtRandomAnchors_PayExactlyForWhatWasPlaced_AndSealNothing()
    {
        var config = TestSim.Config(Seed: 3, PlayerCount: 2, UnitCapacity: 64, CommandCapacity: 4200) with
        {
            Map = MapGenParams.Default with { Forests = 12, GoldMines = 8 },
            BuildingCapacity = 1024,
        };
        var sim = new Simulation(config);
        NavGrid g = sim.World.NavGrid;
        int[] before = SealOracle.Labels(g);
        var rng = new Determinism.SimRng(9, 1);
        List<int> open = FlowFieldOracle.PassableCells(g);
        for (int i = 0; i < 64; i++) sim.Enqueue(Command.SpawnUnit(i % 2, Laborer, MoveScenario.Center(g, open[rng.NextInt(0, open.Count)])));
        Run(sim, 2);
        SetTotals(sim, 0, 20_000, 20_000);
        SetTotals(sim, 1, 20_000, 20_000);
        UnitStore u = sim.World.Units;
        for (int k = 0; k < 4096; k++)
        {
            int slot = rng.NextInt(0, 64), p = u.Owner[slot];
            int type = TestSim.Data.Buildings.Where(d => d.Faction == p).ElementAt(rng.NextInt(0, 10)).Id;
            sim.Enqueue(Command.Build(p, new EntityHandle(slot, u.Generation[slot]), type, MoveScenario.Center(g, open[rng.NextInt(0, open.Count)])));
        }
        sim.Tick();
        long t0 = Stopwatch.GetTimestamp();
        sim.Tick();
        double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
        BuildingStore b = sim.World.Buildings;
        var spent = new long[2, 2];
        for (int k = 0; k < b.Capacity; k++)
        {
            if (!b.Alive[k]) continue;
            spent[b.Owner[k], 0] += Def(b.TypeId[k]).CostGold;
            spent[b.Owner[k], 1] += Def(b.TypeId[k]).CostWood;
        }
        _out.WriteLine($"4,096 random Builds: {b.Count} sites placed in one tick of {ms:F1} ms; p0 {sim.World.Gold[0]}/{sim.World.Wood[0]}, p1 {sim.World.Gold[1]}/{sim.World.Wood[1]}");
        for (int p = 0; p < 2; p++)
        {
            Assert.Equal(20_000 - spent[p, 0], sim.World.Gold[p]);
            Assert.Equal(20_000 - spent[p, 1], sim.World.Wood[p]);
        }
        Assert.Equal(-1, SealOracle.LostConnection(before, SealOracle.Labels(g)));
        for (int i = 0; i < 64; i++)
        {
            Vector2 p = u.Position[i];
            Assert.True(g.WorldToCell(p, out int x, out int y) && (g.FlagsAt(x, y) & NavFlags.Building) == 0, $"unit {i} inside a building at {p}");
        }
    }

    /// <summary>A Build that fails after the seal check (here: can't afford) still pays for a flood. On a long-detour anchor of the design map, measure what a selection's worth (and a flood) of such commands costs one tick.</summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void UnaffordableBuildsAtALongDetourAnchor_TickCost_Report()
    {
        foreach (int count in new[] { 1, 20, 100, 4096 })
        {
            (Simulation sim, int anchorX) = LongWall(128, count + 8);
            SetTotals(sim, 0, 0, 0);
            EntityHandle w = Unit(sim, At(sim, 20, 20));
            for (int k = 0; k < count; k++) sim.Enqueue(Command.Build(0, w, House, At(sim, anchorX, 1)));
            sim.Tick();
            long t0 = Stopwatch.GetTimestamp();
            sim.Tick();
            double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            _out.WriteLine($"128 map, {count} unaffordable Builds at the long-detour anchor: tick {ms:F2} ms");
            Assert.Equal(0, sim.World.Buildings.Count);
        }
    }

    /// <summary>A <paramref name="size"/> map with a one-tree wall down x = size / 2 from y = 3 to size - 6: a House in the top gap has sides that meet only round the bottom.</summary>
    internal static (Simulation Sim, int AnchorX) LongWall(int size, int commands = 64)
    {
        var sim = new Simulation(TestSim.Config(Seed: 5, PlayerCount: 1, UnitCapacity: 8, CommandCapacity: commands) with { ResourceCapacity = 2 * size }, Flat(size, size));
        int x = size / 2;
        for (int y = 3; y < size - 6; y++) Spawn(sim.World, Tree, x, y, TreeWood);
        return (sim, x);
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void CanPlace_LongDetourYes_ByMapSize_Report()
    {
        foreach (int size in new[] { 128, 256, 512, 1024 })
        {
            (Simulation sim, int x) = LongWall(size);
            Give(sim, 0, 1000, 1000);
            int cell = Cell(sim, x, 1);
            Assert.True(sim.World.CanPlace(0, House, cell, out PlacementError why), why.ToString());
            int reps = size >= 512 ? 5 : 50;
            long t0 = Stopwatch.GetTimestamp();
            for (int k = 0; k < reps; k++) sim.World.CanPlace(0, House, cell, out _);
            double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency / reps;
            _out.WriteLine($"{size} map: one CanPlace whose sides meet only round a {size - 9}-cell wall: {ms:F3} ms");
            if (size == 128) Assert.True(ms < 4.0, $"{ms:F3} ms on the design map, the whole tick budget");
        }
    }

    // ------------------------------------------------------------------ builders freed, joining, holding

    [Fact]
    public void BuildersFreedMidBuild_SiteKeepsItsWork_ASlotReusedDoesNotInheritTheOrder()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 24));
        SetTotals(sim, 0, 1000, 1000);
        EntityHandle[] ws = WorkersRound(sim, 10, 10, 2, 2, 3);
        foreach (EntityHandle w in ws) sim.Enqueue(Command.Build(0, w, House, At(sim, 10, 10)));
        Run(sim, 12);
        BuildingStore b = sim.World.Buildings;
        int s = SiteAt(sim, 10, 10);
        int work = b.Work[s];
        Assert.True(work > 0);
        UnitStore u = sim.World.Units;
        foreach (EntityHandle w in ws) u.Free(w);
        Run(sim, 2); // the freed builders' last counted tick, if any
        int idle = b.Work[s];
        Run(sim, 40);
        Assert.Equal(idle, b.Work[s]);
        Assert.True(b.UnderConstruction[s]);
        // The freed slots come back as fresh workers: no inherited build target, no work.
        EntityHandle fresh = Unit(sim, At(sim, 9, 10));
        Assert.Equal(default, u.BuildTarget[fresh.Index]);
        Run(sim, 20);
        Assert.Equal(idle, b.Work[s]);
        // A new worker joins and finishes it at the one-builder rate.
        sim.Enqueue(Command.Build(0, fresh, House, At(sim, 10, 10)));
        Run(sim, 2);
        int left = b.WorkNeeded(House) - b.Work[s];
        Run(sim, (left + 2) / 3 + 2);
        Assert.False(b.UnderConstruction[s]);
        Assert.Equal(Def(House).Hp, b.Hp[s]);
        Assert.Equal(UnitState.Idle, u.State[fresh.Index]);
    }

    [Fact]
    public void ABuilderFreedInTheTickItsSiteCompletes_NothingBreaks()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 24));
        SetTotals(sim, 0, 1000, 1000);
        EntityHandle[] ws = WorkersRound(sim, 10, 10, 2, 2, 2);
        foreach (EntityHandle w in ws) sim.Enqueue(Command.Build(0, w, House, At(sim, 10, 10)));
        Run(sim, 4);
        BuildingStore b = sim.World.Buildings;
        int s = SiteAt(sim, 10, 10);
        b.SetWork(s, b.WorkNeeded(House) - 4); // 2 builders add 4: completes next tick
        sim.World.Units.Free(ws[0]);
        Run(sim, 2);
        Assert.False(b.UnderConstruction[s]);
        Assert.Equal(UnitState.Idle, sim.World.Units.State[ws[1].Index]);
    }

    [Fact]
    public void AWorkerOnHoldOrderedToBuildOnItsOwnSpot_Report()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 24));
        SetTotals(sim, 0, 1000, 1000);
        EntityHandle w = Unit(sim, At(sim, 11, 11));
        sim.Enqueue(Command.HoldPosition(0, w));
        Run(sim, 2);
        sim.World.CanPlace(0, House, Cell(sim, 10, 10), out PlacementError why);
        sim.Enqueue(Command.Build(0, w, House, At(sim, 10, 10)));
        Run(sim, 2);
        _out.WriteLine($"holding worker inside the footprint: CanPlace {why}; after its own Build: {sim.World.Buildings.Count} sites, hold {sim.World.Units.Hold[w.Index]}");
        Assert.Equal(PlacementError.UnitInTheWay, why);
    }

    [Fact]
    public void QueuedBuilds_PayWhenTheyStart_AndAnUnaffordableOneIsDropped()
    {
        Simulation sim = BuildMaps.NewSim(Flat(40, 24));
        SetTotals(sim, 0, 1000, 100);
        EntityHandle w = Unit(sim, At(sim, 4, 4));
        sim.Enqueue(Command.Move(0, w, At(sim, 30, 4)));
        sim.Enqueue(Command.Build(0, w, House, At(sim, 20, 10), queued: true));
        sim.Enqueue(Command.Build(0, w, House, At(sim, 24, 10), queued: true));
        sim.Enqueue(Command.Build(0, w, House, At(sim, 28, 10), queued: true)); // can't afford by then: 3 x 50 > 100
        Run(sim, 2);
        Assert.Equal(0, sim.World.Buildings.Count);
        Assert.Equal(100, sim.World.Wood[0]);
        Run(sim, 4000);
        BuildingStore b = sim.World.Buildings;
        _out.WriteLine($"queued builds: {b.Count} buildings, wood {sim.World.Wood[0]}, worker {sim.World.Units.State[w.Index]}");
        Assert.Equal(2, b.Count);
        Assert.Equal(0, sim.World.Wood[0]);
        Assert.False(b.UnderConstruction[SiteAt(sim, 20, 10)]);
        Assert.False(b.UnderConstruction[SiteAt(sim, 24, 10)]);
        Assert.Equal(-1, SiteAt(sim, 28, 10));
    }

    // ------------------------------------------------------------------ repair

    private (Simulation Sim, EntityHandle Keep, EntityHandle[] Workers) DamagedKeep(int workers, int damage)
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 24));
        EntityHandle keep = Building(sim, 10, 10);
        EntityHandle[] ws = WorkersRound(sim, 10, 10, 4, 4, workers);
        sim.World.Buildings.Damage(keep, damage);
        return (sim, keep, ws);
    }

    [Fact]
    public void RepairWithNothing_StopsAtOnce_NoHpNoDebt()
    {
        (Simulation sim, EntityHandle keep, EntityHandle[] ws) = DamagedKeep(2, 1000);
        SetTotals(sim, 0, 0, 0);
        foreach (EntityHandle w in ws) sim.Enqueue(Command.Repair(0, w, At(sim, 11, 11)));
        Run(sim, 60);
        Assert.Equal(1400, sim.World.Buildings.Hp[keep.Index]);
        Assert.Equal((0, 0), (sim.World.Gold[0], sim.World.Wood[0]));
        Assert.All(ws, w => Assert.Equal(UnitState.Idle, sim.World.Units.State[w.Index]));
        // Gold but no wood (the Keep costs both): also stops.
        SetTotals(sim, 0, 500, 0);
        foreach (EntityHandle w in ws) sim.Enqueue(Command.Repair(0, w, At(sim, 11, 11)));
        Run(sim, 60);
        Assert.Equal(1400, sim.World.Buildings.Hp[keep.Index]);
        Assert.Equal((500, 0), (sim.World.Gold[0], sim.World.Wood[0]));
    }

    [Fact]
    public void RepairOnAShoestring_StopsBeforeDebt_AndPaysAtMostTheFormula()
    {
        (Simulation sim, EntityHandle keep, EntityHandle[] ws) = DamagedKeep(4, 2000);
        SetTotals(sim, 0, 3, 3);
        foreach (EntityHandle w in ws) sim.Enqueue(Command.Repair(0, w, At(sim, 11, 11)));
        int minGold = 3;
        for (int t = 0; t < 2000; t++)
        {
            sim.Tick();
            minGold = Math.Min(minGold, Math.Min(sim.World.Gold[0], sim.World.Wood[0]));
        }
        int hp = sim.World.Buildings.Hp[keep.Index], restored = hp - 400;
        double owed = 0.25 * 275 * restored / 2400;
        _out.WriteLine($"3 gold / 3 wood: restored {restored} hp (owes {owed:F2} of each), left {sim.World.Gold[0]} / {sim.World.Wood[0]}");
        Assert.True(minGold >= 0);
        Assert.True(restored > 0);
        Assert.True(3 - sim.World.Gold[0] <= Math.Ceiling(owed) && 3 - sim.World.Gold[0] >= Math.Floor(owed), $"paid {3 - sim.World.Gold[0]} for {owed:F2}");
        Assert.True(hp < 2400);
        Assert.All(ws, w => Assert.Equal(UnitState.Idle, sim.World.Units.State[w.Index]));
    }

    [Fact]
    public void RepairDamagedAgainMidway_PaysForEveryHpRestored_AndNoMore()
    {
        (Simulation sim, EntityHandle keep, EntityHandle[] ws) = DamagedKeep(1, 1200);
        SetTotals(sim, 0, 1000, 1000);
        sim.Enqueue(Command.Repair(0, ws[0], At(sim, 11, 11)));
        Run(sim, 600);
        BuildingStore b = sim.World.Buildings;
        int mid = b.Hp[keep.Index];
        b.Damage(keep, 500);
        Run(sim, 4000);
        Assert.Equal(2400, b.Hp[keep.Index]);
        int restored = 1200 + 500;
        int expected = (int)Math.Floor(0.25 * 275 * restored / 2400);
        _out.WriteLine($"hp at the second hit {mid}; restored {restored}; paid {1000 - sim.World.Gold[0]} gold / {1000 - sim.World.Wood[0]} wood (formula floor {expected})");
        Assert.InRange(1000 - sim.World.Gold[0], expected - 1, expected + 1);
        Assert.InRange(1000 - sim.World.Wood[0], expected - 1, expected + 1);
        Assert.Equal(UnitState.Idle, sim.World.Units.State[ws[0].Index]);
    }

    [Fact]
    public void RepairCost_IsTheSameWithOneOrTwelveWorkers_AndRateScales()
    {
        int Cost(int n, out int ticks)
        {
            (Simulation sim, EntityHandle keep, EntityHandle[] ws) = DamagedKeep(n, 1200);
            SetTotals(sim, 0, 1000, 1000);
            foreach (EntityHandle w in ws) sim.Enqueue(Command.Repair(0, w, At(sim, 11, 11)));
            Run(sim, 2);
            ticks = 0;
            while (sim.World.Buildings.Hp[keep.Index] < 2400 && ticks < 5000) { sim.Tick(); ticks++; }
            return 2000 - sim.World.Gold[0] - sim.World.Wood[0];
        }
        int one = Cost(1, out int t1), twelve = Cost(12, out int t12);
        _out.WriteLine($"repair 1200 hp: 1 worker {t1} ticks for {one}; 12 workers {t12} ticks for {twelve}");
        Assert.Equal(one, twelve);
        Assert.InRange(t1, 1795, 1801);
        Assert.InRange(t12, 148, 152);
    }

    [Fact]
    public void RepairTargets_FullSiteEnemyAndNothing_AreDropped_AndDestroyedMidRepairIdles()
    {
        Simulation sim = BuildMaps.NewSim(Flat(40, 24), players: 2);
        SetTotals(sim, 0, 1000, 1000);
        EntityHandle full = Building(sim, 4, 4);
        EntityHandle enemy = Building(sim, 30, 4, player: 1, type: TestSim.Data.FindBuilding("whirlwind_holy_camp"));
        sim.World.Buildings.Damage(enemy, 500);
        EntityHandle w = Unit(sim, At(sim, 20, 12));
        sim.Enqueue(Command.Build(0, w, House, At(sim, 20, 16)));
        Run(sim, 2);
        foreach (Vector2 target in new[] { At(sim, 5, 5), At(sim, 31, 5), At(sim, 20, 16), At(sim, 15, 15) })
        {
            sim.Enqueue(Command.Repair(0, w, target));
            Run(sim, 2);
            Assert.NotEqual(full, sim.World.Units.BuildTarget[w.Index]);
            Assert.NotEqual(enemy, sim.World.Units.BuildTarget[w.Index]);
        }
        // Its Build order survived the dropped Repairs.
        Assert.Equal(SiteAt(sim, 20, 16), sim.World.Units.BuildTarget[w.Index].Index);
        sim.World.Buildings.Damage(full, 600);
        sim.Enqueue(Command.Repair(0, w, At(sim, 5, 5)));
        Run(sim, 30);
        Assert.Equal(full, sim.World.Units.BuildTarget[w.Index]);
        sim.World.Buildings.Damage(full, 5000); // destroyed while the worker walks to it
        Run(sim, 2);
        Assert.Equal(default, sim.World.Units.BuildTarget[w.Index]);
        Assert.Equal(UnitState.Idle, sim.World.Units.State[w.Index]);
        Assert.True(sim.World.NavGrid.IsPassable(5, 5));
    }

    // ------------------------------------------------------------------ push-out

    private static bool InFootprint(Vector2 p, int x0, int y0, int fw, int fh) =>
        p.X >= x0 * 2f && p.X < (x0 + fw) * 2f && p.Y >= y0 * 2f && p.Y < (y0 + fh) * 2f;

    [Fact]
    public void TwentyUnitsInsideAKeep_AllPushedOntoDistinctFreeCells_WithinTwoRings()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 30), units: 40);
        SetTotals(sim, 0, 1000, 1000);
        NavGrid g = sim.World.NavGrid;
        var rng = new Determinism.SimRng(1, 1);
        for (int k = 0; k < 20; k++)
            sim.Enqueue(Command.SpawnUnit(0, Laborer, new Vector2(20f + rng.NextFloat() * 8f, 20f + rng.NextFloat() * 8f)));
        Run(sim, 2);
        UnitStore u = sim.World.Units;
        EntityHandle builder = new(0, u.Generation[0]);
        sim.Enqueue(Command.Build(0, builder, Keep, At(sim, 10, 10)));
        sim.Tick();
        bool[] wasInside = Enumerable.Range(0, 20).Select(i => InFootprint(u.Position[i], 10, 10, 4, 4)).ToArray();
        sim.Tick();
        Assert.Equal(1, sim.World.Buildings.Count);
        var cells = new HashSet<int>();
        int pushed = 0;
        for (int i = 0; i < 20; i++)
        {
            Assert.True(u.Alive[i]);
            Vector2 p = u.Position[i];
            Assert.False(InFootprint(p, 10, 10, 4, 4), $"unit {i} still inside at {p}");
            Assert.True(g.WorldToCell(p, out int x, out int y) && g.IsPassable(x, y));
            if (!wasInside[i]) continue;
            pushed++;
            Assert.True(cells.Add(y * 30 + x), $"two pushed units set down on cell ({x}, {y})");
            Assert.True(x >= 8 && x <= 15 && y >= 8 && y <= 15, $"unit {i} pushed past ring 2 to ({x}, {y})");
        }
        _out.WriteLine($"{pushed} of 20 units were inside the Keep when it was placed; all set down on distinct free cells");
        Assert.True(pushed >= 12, $"only {pushed} inside");
        Run(sim, 200);
        for (int i = 0; i < 20; i++) Assert.False(InFootprint(u.Position[i], 10, 10, 4, 4));
    }

    [Fact]
    public void PushOut_FootprintEdges_HalfOpen()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 30), units: 16);
        SetTotals(sim, 0, 1000, 1000);
        // House at (10, 10): x in [20, 24), y in [20, 24) meters.
        Vector2[] spots = { new(20f, 21f), new(23.999f, 23f), new(24f, 21f), new(22f, 24f), new(19.999f, 23f), new(22f, 20f) };
        bool[] inside = { true, true, false, false, false, true };
        foreach (Vector2 p in spots) sim.Enqueue(Command.SpawnUnit(0, Laborer, p));
        Run(sim, 2); // spaced 2 m apart: nothing shoves them before the Build
        for (int i = 0; i < spots.Length; i++) Assert.Equal(spots[i], sim.World.Units.Position[i]);
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.Build(0, new EntityHandle(0, u.Generation[0]), House, At(sim, 10, 10)));
        sim.Tick();
        Run(sim, 1);
        Assert.Equal(1, sim.World.Buildings.Count);
        for (int i = 0; i < spots.Length; i++)
        {
            Vector2 p = u.Position[i];
            Assert.False(InFootprint(p, 10, 10, 2, 2), $"spot {i} ({spots[i]}) left inside at {p}");
            if (!inside[i]) Assert.True(Vector2.Distance(p, spots[i]) < 0.5f, $"spot {i} ({spots[i]}) outside the footprint but moved to {p}");
        }
    }

    [Fact]
    public void PushOut_RingsOneToThreeFull_LandsOnRingFour_AllEightFull_Report()
    {
        foreach (int full in new[] { 3, 8 })
        {
            var sim = new Simulation(TestSim.Config(Seed: 5, PlayerCount: 2, UnitCapacity: 600, CommandCapacity: 2048), Flat(40, 40));
            SetTotals(sim, 0, 1000, 1000);
            const int x0 = 18, y0 = 18;
            int n = 0;
            // Enemy units (player 1) on every cell of rings 1..full; they're outside the footprint, so they don't block it.
            for (int y = y0 - full; y < y0 + 2 + full; y++)
                for (int x = x0 - full; x < x0 + 2 + full; x++)
                    if (!(x >= x0 && x < x0 + 2 && y >= y0 && y < y0 + 2)) { sim.Enqueue(Command.SpawnUnit(1, Infantry, At(sim, x, y))); n++; }
            sim.Enqueue(Command.SpawnUnit(0, Laborer, At(sim, x0, y0)));
            Run(sim, 2);
            UnitStore u = sim.World.Units;
            int me = Enumerable.Range(0, u.Capacity).Single(i => u.Alive[i] && u.Owner[i] == 0);
            Assert.Equal(n + 1, Enumerable.Range(0, u.Capacity).Count(i => u.Alive[i]));
            EntityHandle builder = new(me, u.Generation[me]);
            for (int i = 0; i < u.Capacity; i++)
                if (u.Alive[i] && u.Owner[i] == 1) sim.Enqueue(Command.HoldPosition(1, new EntityHandle(i, u.Generation[i])));
            sim.Enqueue(Command.Build(0, builder, House, At(sim, x0, y0)));
            sim.Tick();
            sim.Tick();
            Vector2 p = u.Position[me];
            sim.World.NavGrid.WorldToCell(p, out int cx, out int cy);
            int ring = Math.Max(Math.Max(x0 - cx, cx - (x0 + 1)), Math.Max(y0 - cy, cy - (y0 + 1)));
            int sharing = Enumerable.Range(0, u.Capacity).Count(i => i != me && u.Alive[i] && sim.World.NavGrid.WorldToCell(u.Position[i], out int ox, out int oy) && ox == cx && oy == cy);
            _out.WriteLine($"rings 1-{full} full: {sim.World.Buildings.Count} site; builder set down on ({cx}, {cy}), ring {ring}, sharing its cell with {sharing} unit(s)");
            Assert.Equal(1, sim.World.Buildings.Count);
            Assert.False(InFootprint(p, x0, y0, 2, 2));
            if (full == 3) { Assert.Equal(4, ring); Assert.Equal(0, sharing); }
        }
    }

    [Fact]
    public void PushOut_NeverOntoAnotherLevel_ByTheRingSearch()
    {
        // Plateau edge: footprint on level 1 against the cliff; own units in it. Every pushed unit stays on level 1.
        Simulation sim = RampMouth();
        SetTotals(sim, 0, 1000, 1000);
        NavGrid g = sim.World.NavGrid;
        int x0 = -1, y0 = 2;
        for (int x = 13; x < 25 && x0 < 0; x++)
            if (sim.World.CanPlace(0, Keep, Cell(sim, x, y0), out _)) x0 = x;
        Assert.True(x0 > 0);
        for (int k = 0; k < 16; k++) sim.Enqueue(Command.SpawnUnit(0, Laborer, At(sim, x0 + k % 4, y0 + k / 4)));
        Run(sim, 2);
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.Build(0, new EntityHandle(0, u.Generation[0]), Keep, At(sim, x0, y0)));
        sim.Tick();
        sim.Tick();
        Assert.Equal(1, sim.World.Buildings.Count);
        for (int i = 0; i < 16; i++)
        {
            g.WorldToCell(u.Position[i], out int x, out int y);
            Assert.Equal(1, g.LevelAt(x, y));
            Assert.True(g.IsPassable(x, y));
        }
    }
}
