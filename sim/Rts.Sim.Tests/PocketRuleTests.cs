using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using static Rts.Sim.Tests.BuildMaps;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests;

/// <summary>
/// M3-H1 (BUG-0093): the pocket rule. A freed building's or node's cells reopen only when they (and the pocket cells
/// joined to them) touch open ground; otherwise they stay blocked as <see cref="NavFlags.Pocket"/> cells and nothing is
/// published. So every passable cell always reaches every other.
/// </summary>
[Collection(SerialCollection.Name)]
public class PocketRuleTests
{
    // House C at (10, 8) and D at (12, 8), side by side, walled in by Houses above, below, left of C and right of D.
    private const int Cx = 10, Dx = 12, Y = 8;

    /// <summary>C and D walled in by finished Houses above and below, a House (or, with <paramref name="treesLeft"/>, two trees) on the left and a construction site on the right.</summary>
    internal static (Simulation Sim, EntityHandle C, EntityHandle D, EntityHandle RightSite, EntityHandle[] LeftTrees) Enclosure(bool treesLeft = false)
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20));
        Give(sim, 0, 10_000, 10_000);
        EntityHandle c = Building(sim, Cx, Y, type: House);
        EntityHandle d = Building(sim, Dx, Y, type: House);
        foreach (int x in new[] { Cx, Dx })
        {
            Building(sim, x, Y - 2, type: House);
            Building(sim, x, Y + 2, type: House);
        }
        EntityHandle[] trees = Array.Empty<EntityHandle>();
        if (treesLeft) trees = new[] { Spawn(sim.World, Tree, Cx - 1, Y, TreeWood), Spawn(sim.World, Tree, Cx - 1, Y + 1, TreeWood) };
        else Building(sim, Cx - 2, Y, type: House);
        // The right wall as a site, so a Cancel can open it.
        EntityHandle worker = Unit(sim, At(sim, 20, 15));
        sim.Enqueue(Command.Build(0, worker, House, At(sim, Dx + 2, Y)));
        Run(sim, 2);
        int site = SiteAt(sim, Dx + 2, Y);
        Assert.True(site >= 0);
        Assert.Null(Stress.ResourceOracle.Reach(sim.World.NavGrid));
        return (sim, c, d, sim.World.Buildings.HandleOf(site), trees);
    }

    private static void AssertCells(NavGrid g, int x0, int fw, NavFlags expected)
    {
        for (int y = Y; y < Y + 2; y++)
            for (int x = x0; x < x0 + fw; x++)
                Assert.True(g.FlagsAt(x, y) == expected, $"({x}, {y}): {g.FlagsAt(x, y)}, expected {expected}");
    }

    [Fact]
    public void AnEnclosedBuildingFreed_StaysBlockedAsAPocket_AndPublishesNothing()
    {
        (Simulation sim, EntityHandle c, _, _, _) = Enclosure();
        NavGrid g = sim.World.NavGrid;
        int v = g.Version, bv = g.BlockVersion, passable = g.PassableCount;
        sim.World.Buildings.Damage(c, 10_000); // destroyed: the same Free path as a Cancel
        Assert.False(sim.World.Buildings.IsAlive(c));
        AssertCells(g, Cx, 2, NavFlags.Blocked | NavFlags.Pocket);
        Assert.Equal((v, bv, passable), (g.Version, g.BlockVersion, g.PassableCount));
        Assert.Equal(MapConstants.CostBlocked, g.CostAt(Cx, Y));
        Assert.False(sim.World.CanPlace(0, House, Cell(sim, Cx, Y), out PlacementError why));
        Assert.Equal(PlacementError.Blocked, why);
        Assert.Null(Stress.ResourceOracle.Reach(g));
    }

    [Fact]
    public void ACellOpenedIntoAPocketAlone_BecomesPocketToo_ThenAChainReopensTransitively_OnACancel()
    {
        (Simulation sim, EntityHandle c, EntityHandle d, EntityHandle right, _) = Enclosure();
        NavGrid g = sim.World.NavGrid;
        BuildingStore b = sim.World.Buildings;
        int v = g.Version, bv = g.BlockVersion, passable = g.PassableCount;
        b.Damage(c, 10_000);
        b.Damage(d, 10_000); // D touches only C's pocket and walls: it joins the pocket
        AssertCells(g, Cx, 4, NavFlags.Blocked | NavFlags.Pocket);
        Assert.Equal((v, bv, passable), (g.Version, g.BlockVersion, g.PassableCount));

        // Cancelling the right wall (a site) opens it onto open ground: D's cells, and through them C's, reopen with it,
        // in the tick the Cancel applies, as one opening change.
        sim.Enqueue(Command.Cancel(0, At(sim, Dx + 2, Y)));
        sim.Tick();
        Assert.True(b.IsAlive(right), "the Cancel applies in the next tick");
        sim.Tick();
        Assert.False(b.IsAlive(right));
        AssertCells(g, Cx, 6, NavFlags.None);
        Assert.Equal((v + 1, bv, passable + 12), (g.Version, g.BlockVersion, g.PassableCount));
        Assert.Equal(MapConstants.CostPassable, g.CostAt(Cx, Y));
        Assert.Null(Stress.ResourceOracle.Reach(g));
    }

    [Fact]
    public void FellingATreeBesideAPocket_ReopensItWithTheTree()
    {
        (Simulation sim, EntityHandle c, _, _, EntityHandle[] trees) = Enclosure(treesLeft: true);
        NavGrid g = sim.World.NavGrid;
        sim.World.Buildings.Damage(c, 10_000);
        AssertCells(g, Cx, 2, NavFlags.Blocked | NavFlags.Pocket);
        int v = g.Version, passable = g.PassableCount;
        Assert.Equal(TreeWood, sim.World.Resources.Take(trees[0], TreeWood));
        Assert.Equal(NavFlags.None, g.FlagsAt(Cx - 1, Y));
        AssertCells(g, Cx, 2, NavFlags.None);
        Assert.Equal((v + 1, passable + 5), (g.Version, g.PassableCount));
        Assert.Null(Stress.ResourceOracle.Reach(g));
    }

    [Fact]
    public void AnInteriorTreeFelled_StaysAPocket_UntilItsOuterNeighbourFalls()
    {
        Simulation sim = BuildMaps.NewSim(Flat(20, 20));
        NavGrid g = sim.World.NavGrid;
        var grove = new EntityHandle[3, 3];
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
                grove[x, y] = Spawn(sim.World, Tree, 8 + x, 8 + y, TreeWood);
        int v = g.Version, passable = g.PassableCount;
        sim.World.Resources.Take(grove[1, 1], TreeWood); // the middle: no open side
        Assert.Equal(NavFlags.Blocked | NavFlags.Pocket, g.FlagsAt(9, 9));
        Assert.Equal((v, passable), (g.Version, g.PassableCount));
        sim.World.Resources.Take(grove[1, 0], TreeWood); // its north neighbour, on the edge: both open, one change
        Assert.True(g.IsPassable(9, 8) && g.IsPassable(9, 9));
        Assert.Equal((v + 1, passable + 2), (g.Version, g.PassableCount));
        Assert.Null(Stress.ResourceOracle.Reach(g));
    }

    /// <summary>
    /// Criterion 3: 300 random Build / Cancel / Damage / fell / SpawnBuilding actions crowded into a small forested patch,
    /// on 4 seeds: after every action every passable cell reaches every other and every pocket cell is blocked; a twin
    /// fed the same actions hashes identically every tick.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    public void Fuzz_MixedActions_KeepEveryPassableCellReachable_TwinsMatch(ulong seed)
    {
        (Simulation a, EntityHandle[] workersA) = FuzzScene(seed);
        (Simulation b, EntityHandle[] workersB) = FuzzScene(seed);
        var rngA = new SimRng(seed, 93);
        var rngB = new SimRng(seed, 93);
        int pocketsSeen = 0, reopened = 0;
        for (int k = 0; k < 300; k++)
        {
            int before = Count(a.World.NavGrid, NavFlags.Pocket);
            FuzzAction(a, workersA, ref rngA);
            FuzzAction(b, workersB, ref rngB);
            for (int t = 0; t < 2; t++)
            {
                a.Tick();
                b.Tick();
                Assert.True(a.StateHash() == b.StateHash(), $"seed {seed} action {k}: twins differ");
            }
            NavGrid g = a.World.NavGrid;
            string? bad = Stress.ResourceOracle.Reach(g);
            Assert.True(bad == null, $"seed {seed} action {k}: {bad}");
            for (int c = 0; c < g.Width * g.Height; c++)
            {
                NavFlags f = g.FlagsAt(c % g.Width, c / g.Width);
                Assert.True((f & NavFlags.Pocket) == 0 || (f & NavFlags.Blocked) != 0, $"seed {seed} action {k}: an open pocket cell");
            }
            int after = Count(g, NavFlags.Pocket);
            if (after > before) pocketsSeen++;
            if (after < before) reopened++;
        }
        Assert.True(pocketsSeen > 0 && reopened > 0, $"seed {seed}: the fuzz made {pocketsSeen} pockets and reopened {reopened}: too gentle");
    }

    private static int Count(NavGrid g, NavFlags flag)
    {
        int n = 0;
        for (int c = 0; c < g.Width * g.Height; c++)
            if ((g.FlagsAt(c % g.Width, c / g.Width) & flag) != 0) n++;
        return n;
    }

    // A 32 x 24 flat map, a grove in its 16 x 12 middle patch; six workers and plenty to spend.
    private static (Simulation Sim, EntityHandle[] Workers) FuzzScene(ulong seed)
    {
        var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 1, UnitCapacity: 16, CommandCapacity: 256) with { BuildingCapacity = 64 }, Flat(32, 24));
        // A solid grove (12 x 8) with a 2-cell margin of the patch round it: interior fells and buildings against it.
        var rng = new SimRng(seed, 17);
        for (int y = 8; y < 16; y++)
            for (int x = 10; x < 22; x++)
                if (rng.NextInt(0, 3) != 0 || (x > 10 && x < 21 && y > 8 && y < 15)) Spawn(sim.World, Tree, x, y, TreeWood); // gaps on its edge only
        Give(sim, 0, 1_000_000, 1_000_000);
        EntityHandle[] workers = Enumerable.Range(0, 6).Select(i => Unit(sim, At(sim, 3 + 4 * i, 2))).ToArray();
        return (sim, workers);
    }

    private static void FuzzAction(Simulation sim, EntityHandle[] workers, ref SimRng rng)
    {
        World w = sim.World;
        BuildingStore b = w.Buildings;
        Vector2 p = At(sim, 8 + rng.NextInt(0, 16), 6 + rng.NextInt(0, 12));
        int roll = rng.NextInt(0, 100);
        if (roll < 40)
        {
            int type = rng.NextInt(0, 4) == 0 ? Keep : House;
            sim.Enqueue(Command.Build(0, workers[rng.NextInt(0, workers.Length)], type, p));
        }
        else if (roll < 50)
        {
            sim.Enqueue(Command.SpawnBuilding(0, House, p));
        }
        else if (roll < 65)
        {
            int k = Pick(b.Capacity, i => b.Alive[i] && b.UnderConstruction[i], ref rng);
            if (k >= 0) sim.Enqueue(Command.Cancel(0, w.NavGrid.CellCenter(b.Cell[k] % w.NavGrid.Width, b.Cell[k] / w.NavGrid.Width)));
        }
        else if (roll < 80)
        {
            int k = Pick(b.Capacity, i => b.Alive[i], ref rng);
            if (k >= 0) b.Damage(b.HandleOf(k), 100_000);
        }
        else
        {
            // Any tree, interior ones too: the grid's rule, not the gather rule, must keep the ground connected.
            ResourceStore r = w.Resources;
            int k = Pick(r.Capacity, i => r.Alive[i], ref rng);
            if (k >= 0) r.Take(r.HandleOf(k), r.Remaining[k]);
        }
    }

    private static int Pick(int capacity, Func<int, bool> ok, ref SimRng rng)
    {
        var live = new List<int>();
        for (int i = 0; i < capacity; i++) if (ok(i)) live.Add(i);
        return live.Count == 0 ? -1 : live[rng.NextInt(0, live.Count)];
    }
}
