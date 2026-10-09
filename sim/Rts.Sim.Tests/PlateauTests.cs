using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;
using static Rts.Sim.Tests.BuildMaps;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests;

/// <summary>
/// M3-H2 (BUG-0097 / 0095 / 0096): plateau ids (connected same-level ground), the spawn and push-out search bounded by
/// the footprint's plateau, push-out leftovers spread instead of stacked, and the never-seal answer memo.
/// </summary>
public class PlateauTests
{
    private readonly ITestOutputHelper _out;

    public PlateauTests(ITestOutputHelper output) => _out = output;

    /// <summary>A size x size level-0 map with a 6 x 6 level-1 block at (x0..x0+5, y0..y0+5) and a ramp down its west side.</summary>
    public static Heightmap SmallPlateau(int size, int x0, int y0)
    {
        var rows = new string[size];
        for (int y = 0; y < size; y++)
        {
            var row = new char[size];
            for (int x = 0; x < size; x++) row[x] = x >= x0 && x <= x0 + 5 && y >= y0 && y <= y0 + 5 ? '1' : '0';
            if (y == y0 + 2) row[x0 - 1] = 'r';
            rows[y] = new string(row);
        }
        return FromRows(rows);
    }

    /// <summary>Two 8 x 8 level-1 plateaus, A at (10-17, 10-17) and B at (40-47, 40-47), on a 64 x 64 map, a ramp each.</summary>
    public static Heightmap TwoPlateaus()
    {
        var rows = new string[64];
        for (int y = 0; y < 64; y++)
        {
            var row = new char[64];
            for (int x = 0; x < 64; x++)
                row[x] = (x >= 10 && x <= 17 && y >= 10 && y <= 17) || (x >= 40 && x <= 47 && y >= 40 && y <= 47) ? '1' : '0';
            if (y == 13) row[9] = 'r';
            if (y == 43) row[39] = 'r';
            rows[y] = new string(row);
        }
        return FromRows(rows);
    }

    [Fact]
    public void TwoPlateausOfOneLevel_AreTwoPlateaus_EachWithItsOwnBox_AndARampBelongsToTheGroundBelow()
    {
        Simulation sim = BuildMaps.NewSim(TwoPlateaus(), units: 8);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        int a = w.Plateaus.At(Cell(sim, 13, 13)), b = w.Plateaus.At(Cell(sim, 43, 43)), low = w.Plateaus.At(Cell(sim, 30, 30));
        Assert.True(a >= 0 && b >= 0 && low >= 0);
        Assert.NotEqual(a, b);
        Assert.NotEqual(a, low);
        Assert.Equal(3, w.Plateaus.Count);
        Assert.Equal(low, w.Plateaus.At(Cell(sim, 9, 13)));      // the ramp: level 0, the ground at its foot
        Assert.Equal(-1, w.Plateaus.At(Cell(sim, 10, 10)));      // a cliff edge is in no plateau
        Assert.Equal(-1, w.Plateaus.At(Cell(sim, 0, 5)));        // nor the border
        Assert.True(w.Plateaus.Bounds(a, out int minX, out int minY, out int maxX, out int maxY));
        Assert.True(minX >= 10 && maxX <= 17 && minY >= 10 && maxY <= 17, $"A's box ({minX}, {minY})-({maxX}, {maxY})");
        Assert.True(w.Plateaus.Bounds(b, out minX, out minY, out maxX, out maxY));
        Assert.True(minX >= 40 && maxX <= 47 && minY >= 40 && maxY <= 47, $"B's box ({minX}, {minY})-({maxX}, {maxY})");
        Assert.False(w.Plateaus.Bounds(-1, out _, out _, out _, out _));
        Assert.False(w.Plateaus.Bounds(3, out _, out _, out _, out _));
        Assert.Equal(-1, w.Plateaus.At(-1));
        Assert.Equal(-1, w.Plateaus.At(g.Width * g.Height));
    }

    /// <summary>On generated maps, against an independent flood: every passable cell is in a plateau, 4-adjacent passable cells of one level share it, each plateau is one level and one connected piece, and its box is its cells' extent.</summary>
    [Theory]
    [InlineData(1UL)] [InlineData(2UL)] [InlineData(3UL)] [InlineData(4UL)]
    public void PlateauIds_AreTheConnectedSameLevelPiecesOfPassableGround_OnGeneratedMaps(ulong seed)
    {
        // No resources, so the grid is the terrain the ids were computed from.
        var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 1, UnitCapacity: 8, CommandCapacity: 16) with { Map = MapGenParams.Default with { Forests = 0, GoldMines = 0 } });
        World w = sim.World;
        NavGrid g = w.NavGrid;
        int n = g.Width * g.Height, count = w.Plateaus.Count;
        var cells = new int[count];
        var box = new int[4 * count];
        for (int p = 0; p < count; p++) { box[4 * p] = box[4 * p + 1] = int.MaxValue; box[4 * p + 2] = box[4 * p + 3] = int.MinValue; }
        var level = Enumerable.Repeat(-1, count).ToArray();
        for (int c = 0; c < n; c++)
        {
            int x = c % g.Width, y = c / g.Width, p = w.Plateaus.At(c);
            Assert.Equal(g.IsPassable(x, y), p >= 0);
            if (p < 0) continue;
            cells[p]++;
            if (level[p] < 0) level[p] = g.LevelAt(x, y);
            Assert.Equal(level[p], g.LevelAt(x, y));
            box[4 * p] = Math.Min(box[4 * p], x); box[4 * p + 1] = Math.Min(box[4 * p + 1], y);
            box[4 * p + 2] = Math.Max(box[4 * p + 2], x); box[4 * p + 3] = Math.Max(box[4 * p + 3], y);
            if (g.IsPassable(x + 1, y) && g.LevelAt(x + 1, y) == g.LevelAt(x, y)) Assert.Equal(p, w.Plateaus.At(c + 1));
            if (g.IsPassable(x, y + 1) && g.LevelAt(x, y + 1) == g.LevelAt(x, y)) Assert.Equal(p, w.Plateaus.At(c + g.Width));
        }
        var seen = new bool[n];
        for (int p = 0; p < count; p++)
        {
            Assert.True(w.Plateaus.Bounds(p, out int minX, out int minY, out int maxX, out int maxY));
            Assert.Equal(new[] { box[4 * p], box[4 * p + 1], box[4 * p + 2], box[4 * p + 3] }, new[] { minX, minY, maxX, maxY });
            // One connected piece: a flood from its first cell through its own cells reaches all of them.
            int start = Enumerable.Range(0, n).First(c => w.Plateaus.At(c) == p), reached = 0;
            var queue = new Queue<int>();
            queue.Enqueue(start);
            seen[start] = true;
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                reached++;
                foreach (int d in new[] { c - 1, c + 1, c - g.Width, c + g.Width })
                    if (!seen[d] && w.Plateaus.At(d) == p) { seen[d] = true; queue.Enqueue(d); }
            }
            Assert.Equal(cells[p], reached);
        }
        _out.WriteLine($"seed {seed}: {count} plateaus");
        Assert.True(count >= 2);
    }

    [Fact]
    public void PlateauIds_AreTerrainOnly_NodesAndBuildingsDontChangeThem()
    {
        Simulation sim = BuildMaps.NewSim(TwoPlateaus(), units: 8);
        World w = sim.World;
        int before = w.Plateaus.At(Cell(sim, 30, 30));
        Spawn(w, Tree, 30, 30, TreeWood);
        Building(sim, 20, 20);
        Assert.Equal(before, w.Plateaus.At(Cell(sim, 30, 30)));
        Assert.Equal(before, w.Plateaus.At(Cell(sim, 21, 21)));
        Assert.Equal(3, w.Plateaus.Count);
    }

    /// <summary>A sim on <see cref="SmallPlateau"/>(64, 28, 28) with 30 own Laborers standing inside a House footprint at (30, 30) and one worker far off.</summary>
    public static (Simulation Sim, int Worker) ThirtyOnASmallPlateau()
    {
        // M4-3b: explored, so the Build far from the far-off worker is about the plateau, not the explored rule.
        var sim = TestSim.Explored(new Simulation(TestSim.Config(Seed: 5, PlayerCount: 1, UnitCapacity: 64, CommandCapacity: 128), SmallPlateau(64, 28, 28)));
        NavGrid g = sim.World.NavGrid;
        for (int k = 0; k < 31; k++) sim.Enqueue(Command.SpawnUnit(0, Laborer, g.CellCenter(4 + k, 4)));
        Run(sim, 2);
        PackIntoTheHouse(sim);
        SetTotals(sim, 0, 1000, 1000);
        return (sim, 30);
    }

    /// <summary>Sets units 0-29 inside the House footprint at (30, 30), each on its own point (a test seam).</summary>
    internal static void PackIntoTheHouse(Simulation sim)
    {
        UnitStore u = sim.World.Units;
        for (int i = 0; i < 30; i++) u.Position[i] = u.PrevPosition[i] = new Vector2(60.2f + 0.25f * (i % 6), 60.2f + 0.6f * (i / 6));
    }

    [Fact]
    public void APushOutOnAFullPlateau_SpreadsTheLeftovers_OnePerCellFirst_NoTwoOnOnePoint_NeverOffThePlateau()
    {
        (Simulation sim, int worker) = ThirtyOnASmallPlateau();
        World w = sim.World;
        NavGrid g = w.NavGrid;
        Assert.True(ConstructionSystem.StartBuild(w, worker, House, At(sim, 30, 30), replaceQueue: true));
        int plateau = w.Plateaus.At(Cell(sim, 30, 30));
        int free = 0;
        for (int c = 0; c < g.Width * g.Height; c++) if (w.Plateaus.At(c) == plateau && g.IsPassable(c % g.Width, c / g.Width)) free++;
        var perCell = new Dictionary<int, int>();
        var points = new HashSet<Vector2>();
        for (int i = 0; i < 30; i++)
        {
            Assert.True(g.WorldToCell(w.Units.Position[i], out int x, out int y));
            int c = y * g.Width + x;
            Assert.True(g.IsPassable(x, y) && w.Plateaus.At(c) == plateau, $"unit {i} at ({x}, {y}) is off the plateau");
            perCell[c] = perCell.GetValueOrDefault(c) + 1;
            Assert.True(points.Add(w.Units.Position[i]), $"unit {i} shares a point");
            Assert.Equal(w.Units.Position[i], w.Units.PrevPosition[i]);
        }
        _out.WriteLine($"{free} free cells on the plateau; {perCell.Count} used; most on one {perCell.Values.Max()}");
        Assert.Equal(free, perCell.Count);
        // 30 over `free` cells: the first `free` take the cell centers, then the leftovers one per cell per pass.
        Assert.True(perCell.Values.Max() - perCell.Values.Min() <= 1, "leftovers spread unevenly");
    }

    [Fact]
    public void APushOutLeavingCellsFree_IsUnchanged_EachUnitOnItsOwnCellCenter()
    {
        // Ten units, plenty of room: no leftover pass at all.
        (Simulation sim, int worker) = ThirtyOnASmallPlateau();
        World w = sim.World;
        for (int i = 10; i < 30; i++) w.Units.Free(new EntityHandle(i, w.Units.Generation[i]));
        Assert.True(ConstructionSystem.StartBuild(w, worker, House, At(sim, 30, 30), replaceQueue: true));
        var cells = new HashSet<int>();
        for (int i = 0; i < 10; i++)
        {
            Assert.True(w.NavGrid.WorldToCell(w.Units.Position[i], out int x, out int y));
            Assert.True(cells.Add(y * w.NavGrid.Width + x));
            Assert.Equal(w.NavGrid.CellCenter(x, y), w.Units.Position[i]);
        }
    }

    // ---------- the never-seal answer memo (BUG-0096) ----------

    /// <summary>A 64 x 64 flat map with a tree wall at x = 32 from y = 3 to the bottom border: a House in the top gap seals the map.</summary>
    private static Simulation Wall()
    {
        // M4-3b: explored, so the refusals are the seal rule's, not the explored rule's.
        var sim = TestSim.Explored(new Simulation(TestSim.Config(Seed: 5, PlayerCount: 1, UnitCapacity: 128, CommandCapacity: 256) with { ResourceCapacity = 128 }, Flat(64, 64)));
        for (int y = 3; y < 63; y++) Spawn(sim.World, Tree, 32, y, TreeWood);
        return sim;
    }

    [Fact]
    public void HundredBuildsRefusedForSealsGround_InOneTick_FloodOnce()
    {
        Simulation sim = Wall();
        Give(sim, 0, 1_000_000, 1_000_000);
        EntityHandle[] workers = Enumerable.Range(0, 100).Select(k => Unit(sim, At(sim, 4 + k % 25, 20 + k / 25))).ToArray();
        Run(sim, 1);
        Assert.False(sim.World.CanPlace(0, House, Cell(sim, 32, 1), out PlacementError why));
        Assert.Equal(PlacementError.SealsGround, why);
        sim.World.Seal.ForgetForTests();
        long floods = sim.World.Seal.Floods;
        foreach (EntityHandle wk in workers) sim.Enqueue(Command.Build(0, wk, House, At(sim, 32, 1)));
        Run(sim, 2); // they apply in the second tick
        Assert.Equal(0, sim.World.Buildings.Count);
        Assert.Equal(1, sim.World.Seal.Floods - floods);
    }

    [Fact]
    public void TheMemo_FollowsTheGridVersion_AnOpeningChangeAtTheSameSpotChangesTheAnswer()
    {
        Simulation sim = Wall();
        World w = sim.World;
        Give(sim, 0, 1_000_000, 1_000_000);
        Assert.False(w.CanPlace(0, House, Cell(sim, 32, 1), out PlacementError why));
        Assert.Equal(PlacementError.SealsGround, why);
        int blockVersion = w.NavGrid.BlockVersion;
        // Fell a tree in the wall: an opening change (Version moves, BlockVersion doesn't), and the House no longer seals.
        w.NavGrid.ClearResource(32, 40, 1, 1);
        Assert.Equal(blockVersion, w.NavGrid.BlockVersion);
        Assert.True(w.CanPlace(0, House, Cell(sim, 32, 1), out why), why.ToString());
        // A closing change back: sealing again.
        w.NavGrid.SetResource(32, 40, 1, 1);
        Assert.False(w.CanPlace(0, House, Cell(sim, 32, 1), out why));
        Assert.Equal(PlacementError.SealsGround, why);
    }

    [Fact]
    public void TheMemo_IsPerFootprint_AnotherSizeOrAnchorFloodsAgain()
    {
        Simulation sim = Wall();
        World w = sim.World;
        Give(sim, 0, 1_000_000, 1_000_000);
        w.CanPlace(0, House, Cell(sim, 32, 1), out PlacementError why);
        Assert.Equal(PlacementError.SealsGround, why);
        long floods = w.Seal.Floods;
        w.CanPlace(0, House, Cell(sim, 32, 1), out _);
        Assert.Equal(floods, w.Seal.Floods);                    // the same question: kept
        w.CanPlace(0, House, Cell(sim, 31, 1), out why);        // another anchor at the gap: asked again
        Assert.Equal(floods + 1, w.Seal.Floods);
        Assert.Equal(PlacementError.SealsGround, why);
        w.CanPlace(0, House, Cell(sim, 32, 1), out _);          // back to the first: the memo holds one answer
        Assert.Equal(floods + 2, w.Seal.Floods);
    }
}
