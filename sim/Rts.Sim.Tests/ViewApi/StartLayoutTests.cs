using System.Numerics;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>The debug start blocks the view spawns both armies into.</summary>
public class StartLayoutTests
{
    public static IEnumerable<object[]> Seeds()
    {
        for (ulong seed = 1; seed <= 20; seed++) yield return new object[] { seed };
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Blocks_ArePassable_Unique_InsideTheMap_OnTheirSide_AndDoNotOverlap(ulong seed)
    {
        var grid = new NavGrid(TerrainHeightTests.GeneratedMap(seed));
        float midX = grid.Width / 2 * MapConstants.CellSize;
        foreach (int count in new[] { 1, 100, 1000 })
        {
            foreach (float radius in new[] { 0.9f, 1.5f })
            {
                Vector2[] west = StartLayout.Block(grid, count, west: true, radius);
                Vector2[] east = StartLayout.Block(grid, count, west: false, radius);
                Assert.Equal(count, west.Length);
                Assert.Equal(count, east.Length);
                foreach (Vector2 p in west) Assert.True(p.X < midX - StartLayout.HalfGapCells * MapConstants.CellSize, $"west unit at {p}");
                foreach (Vector2 p in east) Assert.True(p.X > midX + StartLayout.HalfGapCells * MapConstants.CellSize, $"east unit at {p}");

                var all = west.Concat(east).ToArray();
                foreach (Vector2 p in all)
                {
                    Assert.True(grid.WorldToCell(p, out int x, out int y), $"{p} is off the map");
                    Assert.True(grid.IsPassable(x, y), $"{p} is on blocked ground");
                }
                AssertSpread(all, 2 * radius, $"seed {seed}, {count} units, radius {radius}");
            }
        }
    }

    private static void AssertSpread(Vector2[] points, float minDistance, string what)
    {
        // Sorted by x, so each point only checks the run of points within minDistance in x.
        Vector2[] sorted = points.OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
        for (int i = 0; i < sorted.Length; i++)
        {
            for (int j = i + 1; j < sorted.Length && sorted[j].X - sorted[i].X < minDistance; j++)
            {
                float d = Vector2.Distance(sorted[i], sorted[j]);
                Assert.True(d >= minDistance - 1e-4f, $"{what}: {sorted[i]} and {sorted[j]} are {d} m apart (duplicate or overlapping)");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Blocks_OnTheMatchMap_NoSpotTouchesABlockedOrResourceCell_AndEachBlockIsOnOneLevel(ulong seed)
    {
        // BUG-0085: seed 1's west army started threaded through a forest. Match map: 12 forests, 8 mines.
        NavGrid grid = PropLayoutTests.MatchSim(seed).World.NavGrid;
        float maxR = TestSim.Data.Units.Max(u => u.Radius);
        for (int p = 0; p < 2; p++)
        {
            Vector2[] spots = StartLayout.Block(grid, 100, west: p == 0, maxR);
            Assert.Equal(100, spots.Length);
            int level = -1;
            foreach (Vector2 s in spots)
            {
                Assert.True(grid.WorldToCell(s, out int x, out int y), $"seed {seed} p{p}: {s} off the map");
                Assert.True((grid.FlagsAt(x, y) & NavFlags.Ramp) == 0, $"seed {seed} p{p}: ({x}, {y}) is a ramp");
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        Assert.True((grid.FlagsAt(x + dx, y + dy) & (NavFlags.Blocked | NavFlags.Resource)) == 0,
                            $"seed {seed} p{p}: spot ({x}, {y}) touches ({x + dx}, {y + dy}) flags {grid.FlagsAt(x + dx, y + dy)}");
                if (level < 0) level = grid.LevelAt(x, y);
                Assert.True(grid.LevelAt(x, y) == level, $"seed {seed} p{p}: ({x}, {y}) on level {grid.LevelAt(x, y)}, block on {level}");
            }
        }
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(7UL)]
    [InlineData(11UL)]
    public void Blocks_OnTheMatchMap_StillFit1000PerSide(ulong seed)
    {
        // `--units 1000` (the dots shot and the perf rows) must still fill both blocks.
        NavGrid grid = PropLayoutTests.MatchSim(seed).World.NavGrid;
        float maxR = TestSim.Data.Units.Max(u => u.Radius);
        Assert.Equal(1000, StartLayout.Block(grid, 1000, west: true, maxR).Length);
        Assert.Equal(1000, StartLayout.Block(grid, 1000, west: false, maxR).Length);
    }

    [Fact]
    public void Block_IsDeterministic_AndCentredOnTheMapsMiddleRow()
    {
        var grid = new NavGrid(TerrainHeightTests.GeneratedMap(4));
        Vector2[] a = StartLayout.Block(grid, 100, true, 0.9f);
        Vector2[] b = StartLayout.Block(grid, 100, true, 0.9f);
        Assert.Equal(a, b);
        // A half-disc on the middle row; holes in it (cliffs) only stretch it.
        float meanY = a.Average(p => p.Y);
        Assert.InRange(meanY, grid.Height / 2 * MapConstants.CellSize - 20f, grid.Height / 2 * MapConstants.CellSize + 20f);
        // 100 units fill a half-disc of radius ~8 cells; allow for holes but not a strip across the map.
        Assert.True(a.Max(p => p.Y) - a.Min(p => p.Y) < 30 * MapConstants.CellSize, "block too tall");
        Assert.True(a.Max(p => p.X) - a.Min(p => p.X) < 16 * MapConstants.CellSize, "block too wide");
    }

    [Fact]
    public void Block_ZeroOrNegativeCount_IsEmpty_AndAFullHalfReturnsWhatFits()
    {
        var grid = new NavGrid(TerrainHeightTests.HandMap());
        Assert.Empty(StartLayout.Block(grid, 0, true, 0.4f));
        Assert.Empty(StartLayout.Block(grid, -5, false, 0.4f));
        // The 4 x 4 hand map's passable cells all lie in the blocked ring or near the centre line: few or none fit.
        Assert.True(StartLayout.Block(grid, 1000, true, 0.4f).Length < 16);
    }
}
