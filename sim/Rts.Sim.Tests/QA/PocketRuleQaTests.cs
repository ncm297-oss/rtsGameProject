using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Tests.Stress;
using Xunit.Abstractions;
using static Rts.Sim.Tests.BuildMaps;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M3-H1, session 2026-10-07-0800): targeted attacks on the BUG-0093 pocket rule. Pockets inside pockets (a 5 x 5
/// block of Houses hollowed from the inside), pockets against the border and in a corner, pockets either side of a
/// cliff (two levels), a Cancel and a fell landing in the same tick, and a building freed in the tick a Build pushes
/// workers out. Every step is checked by the independent oracle of <see cref="PocketRuleFuzzStressTests.Check"/>.
/// </summary>
[Collection(SerialCollection.Name)]
public class PocketRuleQaTests
{
    private readonly ITestOutputHelper _out;

    public PocketRuleQaTests(ITestOutputHelper output) => _out = output;

    private static void AssertOracle(Simulation sim, string where)
    {
        string? bad = PocketRuleFuzzStressTests.Check(sim.World.NavGrid, new NavGrid(sim.World.Heightmap));
        Assert.True(bad == null, $"{where}: {bad}");
    }

    private static void AssertFootprint(NavGrid g, int x0, int y0, NavFlags expected, string where)
    {
        for (int y = y0; y < y0 + 2; y++)
            for (int x = x0; x < x0 + 2; x++)
                Assert.True(g.FlagsAt(x, y) == expected, $"{where}: ({x}, {y}) is {g.FlagsAt(x, y)}, expected {expected}");
    }

    private static readonly NavFlags PocketFlags = NavFlags.Blocked | NavFlags.Pocket;

    // ------------------------------------------------------------------ pockets inside pockets

    /// <summary>
    /// A 5 x 5 block of Houses (cells 6-15). Hollowing it from the inside: the centre becomes a pocket, a diagonal
    /// neighbour a second, separate pocket, the House between them merges both into one, a third diagonal one stays
    /// apart. Then an outer House falls: exactly the merged union reopens (one bump), the separate pocket stays, and the
    /// House joining it to the reopened ground reopens it too.
    /// </summary>
    [Fact]
    public void FiveByFiveHouseBlock_HollowedFromInside_PocketsMergeAndReopenExactlyByUnion()
    {
        Simulation sim = BuildMaps.NewSim(Flat(24, 24));
        NavGrid g = sim.World.NavGrid;
        BuildingStore b = sim.World.Buildings;
        var h = new EntityHandle[5, 5];
        for (int j = 0; j < 5; j++)
            for (int i = 0; i < 5; i++)
                h[i, j] = Building(sim, 6 + 2 * i, 6 + 2 * j, type: House);
        AssertOracle(sim, "block placed");
        int v = g.Version, bv = g.BlockVersion, passable = g.PassableCount;

        b.Damage(h[2, 2], 1_000_000); // centre
        b.Damage(h[1, 1], 1_000_000); // diagonal: a second pocket
        AssertFootprint(g, 10, 10, PocketFlags, "centre");
        AssertFootprint(g, 8, 8, PocketFlags, "(1, 1)");
        b.Damage(h[2, 1], 1_000_000); // touches both: one 12-cell pocket
        b.Damage(h[3, 3], 1_000_000); // diagonal to the centre only: stays apart
        AssertFootprint(g, 10, 8, PocketFlags, "(2, 1)");
        AssertFootprint(g, 12, 12, PocketFlags, "(3, 3)");
        Assert.Equal((v, bv, passable), (g.Version, g.BlockVersion, g.PassableCount));
        AssertOracle(sim, "four interior Houses down");

        b.Damage(h[1, 0], 1_000_000); // outer edge, beside (1, 1): reopens itself + the 12-cell union
        Assert.Equal((v + 1, bv, passable + 16), (g.Version, g.BlockVersion, g.PassableCount));
        foreach ((int x, int y) in new[] { (8, 6), (8, 8), (10, 8), (10, 10) }) AssertFootprint(g, x, y, NavFlags.None, $"reopened ({x}, {y})");
        AssertFootprint(g, 12, 12, PocketFlags, "(3, 3) only touches the union at a corner");
        AssertOracle(sim, "outer House down");

        b.Damage(h[3, 2], 1_000_000); // beside the reopened centre and the (3, 3) pocket
        Assert.Equal((v + 2, bv, passable + 24), (g.Version, g.BlockVersion, g.PassableCount));
        AssertFootprint(g, 12, 12, NavFlags.None, "(3, 3) joined");
        AssertOracle(sim, "joining House down");
    }

    // ------------------------------------------------------------------ the border

    /// <summary>A House in the top-left corner (the border ring on two sides) walled in by two Houses, freed, then opened by one of its walls; the same along the left edge.</summary>
    [Fact]
    public void PocketsAgainstTheBorderAndInACorner_StayBlocked_ThenReopenThroughAWall()
    {
        Simulation sim = BuildMaps.NewSim(Flat(20, 16));
        NavGrid g = sim.World.NavGrid;
        BuildingStore b = sim.World.Buildings;
        EntityHandle corner = Building(sim, 1, 1, type: House);
        EntityHandle right = Building(sim, 3, 1, type: House);
        Building(sim, 1, 3, type: House);
        EntityHandle edge = Building(sim, 1, 7, type: House);
        Building(sim, 1, 5, type: House);
        Building(sim, 1, 9, type: House);
        EntityHandle edgeWall = Building(sim, 3, 7, type: House);
        int v = g.Version, passable = g.PassableCount;

        b.Damage(corner, 1_000_000);
        b.Damage(edge, 1_000_000);
        AssertFootprint(g, 1, 1, PocketFlags, "corner");
        AssertFootprint(g, 1, 7, PocketFlags, "left edge");
        Assert.Equal((v, passable), (g.Version, g.PassableCount));
        Assert.False(sim.World.CanPlace(0, House, Cell(sim, 1, 1), out PlacementError why));
        Assert.Equal(PlacementError.Blocked, why);
        AssertOracle(sim, "both pockets");

        b.Damage(right, 1_000_000);
        AssertFootprint(g, 1, 1, NavFlags.None, "corner reopened");
        b.Damage(edgeWall, 1_000_000);
        AssertFootprint(g, 1, 7, NavFlags.None, "edge reopened");
        Assert.Equal((v + 2, passable + 16), (g.Version, g.PassableCount));
        AssertOracle(sim, "both reopened");
    }

    // ------------------------------------------------------------------ two levels

    /// <summary>West half level 0, a 3-wide ramp at column 20 (rows 13-15), level 1 from column 21 (a cliff column).</summary>
    private static Heightmap TwoLevels()
    {
        var rows = new string[24];
        for (int y = 0; y < 24; y++)
            rows[y] = new string('0', 20) + (y is >= 13 and <= 15 ? "r" : "0") + new string('1', 19);
        return FromRows(rows);
    }

    /// <summary>
    /// The pocket rule's adjacency (4-neighbour passable) is the movement graph only if no two 4-adjacent passable
    /// cells sit on different levels without a ramp between: true on the hand-made map and on generated maps.
    /// </summary>
    [Fact]
    public void NoTwoAdjacentPassableCellsDifferInLevel_WithoutARamp()
    {
        var grids = new List<NavGrid> { new(TwoLevels()) };
        for (ulong seed = 1; seed <= 6; seed++)
            grids.Add(new Simulation(TestSim.Config(Seed: seed, PlayerCount: 1, UnitCapacity: 4, CommandCapacity: 4) with { Map = MapGenParams.Default with { Forests = 10, GoldMines = 4 } }).World.NavGrid);
        foreach (NavGrid g in grids)
        {
            for (int y = 0; y < g.Height; y++)
                for (int x = 0; x + 1 < g.Width; x++)
                    foreach ((int nx, int ny) in new[] { (x + 1, y), (x, y + 1) })
                    {
                        if (!g.IsPassable(x, y) || !g.IsPassable(nx, ny) || g.LevelAt(x, y) == g.LevelAt(nx, ny)) continue;
                        bool ramp = ((g.FlagsAt(x, y) | g.FlagsAt(nx, ny)) & NavFlags.Ramp) != 0;
                        Assert.True(ramp, $"({x}, {y}) level {g.LevelAt(x, y)} beside ({nx}, {ny}) level {g.LevelAt(nx, ny)}, no ramp");
                    }
        }
    }

    /// <summary>
    /// A pocket on level 0 at the cliff foot (its east side is the level-1 cliff column) and one on level 1 at the
    /// cliff top, two cells apart across the cliff: both stay pockets (the passable ground beyond a cliff is not
    /// beside them); opening the level-0 one's west wall reopens only that one; the level-1 one reopens through its own wall.
    /// </summary>
    [Fact]
    public void PocketsEitherSideOfACliff_StaySeparate_AndEachReopensOnlyThroughItsOwnLevel()
    {
        Simulation sim = BuildMaps.NewSim(TwoLevels());
        NavGrid g = sim.World.NavGrid;
        BuildingStore b = sim.World.Buildings;
        Assert.True((g.FlagsAt(21, 4) & NavFlags.Cliff) != 0 && g.IsPassable(20, 4) && g.IsPassable(22, 4) && g.LevelAt(22, 4) == 1);
        EntityHandle low = Building(sim, 19, 4, type: House);  // cells 19-20: east ring is the cliff column
        EntityHandle high = Building(sim, 22, 4, type: House); // cells 22-23: west ring is the cliff column
        Building(sim, 19, 2, type: House);
        Building(sim, 19, 6, type: House);
        EntityHandle lowWall = Building(sim, 17, 4, type: House);
        Building(sim, 22, 2, type: House);
        Building(sim, 22, 6, type: House);
        EntityHandle highWall = Building(sim, 24, 4, type: House);
        AssertOracle(sim, "placed");
        int v = g.Version, passable = g.PassableCount;

        b.Damage(low, 1_000_000);
        b.Damage(high, 1_000_000);
        AssertFootprint(g, 19, 4, PocketFlags, "level 0");
        AssertFootprint(g, 22, 4, PocketFlags, "level 1");
        Assert.Equal((v, passable), (g.Version, g.PassableCount));
        AssertOracle(sim, "both pockets");

        b.Damage(lowWall, 1_000_000);
        AssertFootprint(g, 19, 4, NavFlags.None, "level 0 reopened");
        AssertFootprint(g, 22, 4, PocketFlags, "level 1 must not reopen across the cliff");
        Assert.Equal((v + 1, passable + 8), (g.Version, g.PassableCount));
        AssertOracle(sim, "level 0 reopened");

        b.Damage(highWall, 1_000_000);
        AssertFootprint(g, 22, 4, NavFlags.None, "level 1 reopened");
        Assert.Equal((v + 2, passable + 16), (g.Version, g.PassableCount));
        AssertOracle(sim, "both reopened");
    }

    // ------------------------------------------------------------------ same-tick interactions

    /// <summary>
    /// Site S at (11, 8) walled in by Houses above, below and east, and by trees T (10, 8) and T2 (10, 9) on the west;
    /// T is exposed west and holds 1 wood with a worker on it. A twin finds the tick T falls; the other run cancels S
    /// so the Cancel applies in that same tick (Cancel in the command phase, the fell in phase 4). S must end open,
    /// joined through T's cell, with one bump only (the Cancel publishes nothing, the fell one opening change).
    /// </summary>
    [Fact]
    public void CancelAndFellInTheSameTick_TheCancelledPocketReopensWithTheTree()
    {
        (Simulation probe, EntityHandle probeTree) = CancelFellScene();
        int fellTick = -1;
        for (int t = 0; t < 2000 && fellTick < 0; t++)
        {
            probe.Tick();
            if (!probe.World.Resources.IsAlive(probeTree)) fellTick = (int)probe.TickNumber;
        }
        Assert.True(fellTick > 0, "the tree never fell");

        (Simulation sim, EntityHandle tree) = CancelFellScene();
        NavGrid g = sim.World.NavGrid;
        while (sim.TickNumber < fellTick - 2) sim.Tick();
        sim.Enqueue(Command.Cancel(0, At(sim, 11, 8))); // applies in the tick numbered fellTick
        sim.Tick();
        Assert.True(sim.World.Resources.IsAlive(tree));
        Assert.True(SiteAt(sim, 11, 8) >= 0);
        int v = g.Version, bv = g.BlockVersion, passable = g.PassableCount;
        sim.Tick();
        Assert.Equal(fellTick, (int)sim.TickNumber);
        Assert.False(sim.World.Resources.IsAlive(tree), "the twin diverged: the tree did not fall in the Cancel's tick");
        Assert.True(SiteAt(sim, 11, 8) < 0, "the Cancel did not apply");
        AssertFootprint(g, 11, 8, NavFlags.None, "site cells");
        Assert.Equal(NavFlags.None, g.FlagsAt(10, 8));
        _out.WriteLine($"tick {fellTick}: Version {v} -> {g.Version}, passable {passable} -> {g.PassableCount}");
        Assert.Equal((v + 1, bv, passable + 5), (g.Version, g.BlockVersion, g.PassableCount));
        AssertOracle(sim, "after the shared tick");
    }

    private static (Simulation Sim, EntityHandle Tree) CancelFellScene()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20));
        Give(sim, 0, 10_000, 10_000);
        EntityHandle builder = Unit(sim, At(sim, 16, 15));
        sim.Enqueue(Command.Build(0, builder, House, At(sim, 11, 8)));
        Run(sim, 2);
        Assert.True(SiteAt(sim, 11, 8) >= 0);
        sim.Enqueue(Command.Move(0, builder, At(sim, 20, 15))); // off the site: it stays at 0 work
        Building(sim, 11, 6, type: House);
        Building(sim, 11, 10, type: House);
        Building(sim, 13, 8, type: House);
        EntityHandle tree = Spawn(sim.World, Tree, 10, 8, TreeWood);
        Spawn(sim.World, Tree, 10, 9, TreeWood);
        sim.World.Resources.Take(tree, TreeWood - 1);
        EntityHandle w = Unit(sim, At(sim, 7, 8));
        sim.Enqueue(Command.Gather(0, w, At(sim, 10, 8)));
        sim.Tick();
        AssertOracle(sim, "scene");
        return (sim, tree);
    }

    /// <summary>
    /// In one tick's command phase: a Cancel frees the enclosure's east wall (a site), a Build lands a House on 12 of the
    /// player's units (push-out on the flow-field scratch the pocket flood also borrows) and a Cancel removes that new
    /// site again at 0 % (full refund, its cells reopen). Then D, beside C's pocket and the reopened east side, falls:
    /// D and C reopen together. No unit may end on a blocked cell, no two pushed units share a cell, the totals change by
    /// exactly the east site's refund, and the oracle holds.
    /// </summary>
    [Fact]
    public void FreedWhileWorkersArePushedOut_SameTick_NoUnitOnBlockedGround_FullRefundAtZeroPercent()
    {
        (Simulation sim, EntityHandle c, EntityHandle d, EntityHandle rightSite, _) = PocketRuleTests.Enclosure();
        NavGrid g = sim.World.NavGrid;
        UnitStore u = sim.World.Units;
        BuildingStore b = sim.World.Buildings;
        b.Damage(c, 1_000_000); // a pocket beside D
        AssertFootprint(g, 10, 8, PocketFlags, "C");
        EntityHandle worker = Unit(sim, At(sim, 4, 15));
        var stacked = new List<int>();
        for (int k = 0; k < 12; k++) stacked.Add(Unit(sim, At(sim, 4 + k, 17)).Index);
        int needed = b.WorkNeeded(House), work = b.Work[rightSite.Index];
        int eastRefund = (int)((long)sim.World.Data.Buildings[House].CostWood * (needed - work) / needed);
        int gold = sim.World.Gold[0], wood = sim.World.Wood[0];

        sim.Enqueue(Command.Cancel(0, At(sim, 14, 8)));
        sim.Enqueue(Command.Build(0, worker, House, At(sim, 20, 14)));
        sim.Enqueue(Command.Cancel(0, At(sim, 20, 14)));
        sim.Tick(); // the commands apply in the next tick
        Assert.True(b.IsAlive(rightSite));
        for (int k = 0; k < 12; k++) // stacked in the footprint (a position set, as a test seam: avoidance would spread them)
            u.Position[stacked[k]] = u.PrevPosition[stacked[k]] = new Vector2(40.2f + 0.3f * (k % 4), 28.2f + 0.6f * (k / 4));
        work = b.Work[rightSite.Index];
        eastRefund = (int)((long)sim.World.Data.Buildings[House].CostWood * (needed - work) / needed);
        gold = sim.World.Gold[0];
        wood = sim.World.Wood[0];
        sim.Tick();
        Assert.False(b.IsAlive(rightSite), "the east Cancel did not apply");
        Assert.True(SiteAt(sim, 20, 14) < 0, "the push-out site was not cancelled in its own tick");
        AssertFootprint(g, 14, 8, NavFlags.None, "the east site opened onto open ground");
        AssertFootprint(g, 20, 14, NavFlags.None, "the cancelled push-out site");
        Assert.Equal(gold, sim.World.Gold[0]);
        Assert.Equal(wood + eastRefund, sim.World.Wood[0]); // the push-out site: spent and refunded in full at 0 %
        var cells = new HashSet<int>();
        foreach (int i in stacked)
        {
            Assert.True(g.WorldToCell(u.Position[i], out int cx, out int cy));
            Assert.False(cx >= 20 && cx < 22 && cy >= 14 && cy < 16, $"unit {i} was not pushed out");
            Assert.True(cells.Add(cy * g.Width + cx), $"pushed unit {i} shares cell ({cx}, {cy})");
            Assert.Equal(u.Position[i], u.PrevPosition[i]);
        }
        AssertOracle(sim, "the shared tick");

        int v = g.Version, passable = g.PassableCount;
        b.Damage(d, 1_000_000);
        AssertFootprint(g, 10, 8, NavFlags.None, "C's pocket reopened through D");
        AssertFootprint(g, 12, 8, NavFlags.None, "D");
        Assert.Equal((v + 1, passable + 8), (g.Version, g.PassableCount));
        for (int t = 0; t < 40; t++)
        {
            sim.Tick();
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i] || !g.WorldToCell(u.Position[i], out int cx, out int cy)) continue;
                Assert.True(g.IsPassable(cx, cy), $"tick {t}: unit {i} on {g.FlagsAt(cx, cy)} at ({cx}, {cy})");
            }
        }
        AssertOracle(sim, "end");
    }
}
