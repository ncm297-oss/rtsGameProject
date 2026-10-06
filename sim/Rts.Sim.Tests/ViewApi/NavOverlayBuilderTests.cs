using System.Numerics;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M2-5 nav-grid overlay: one inset quad per cell on the terrain surface, coloured by flags, refilled once per grid version.</summary>
[Collection(SerialCollection.Name)]
public class NavOverlayBuilderTests
{
    private readonly ITestOutputHelper _out;

    public NavOverlayBuilderTests(ITestOutputHelper output) => _out = output;

    public static IEnumerable<object[]> Maps()
    {
        yield return new object[] { "hand 4x4", 0UL };
        foreach (ulong seed in new[] { 1UL, 2UL, 7UL }) yield return new object[] { "generated", seed };
    }

    private static Heightmap MapOf(string kind, ulong seed) =>
        kind == "generated" ? TerrainHeightTests.GeneratedMap(seed) : TerrainHeightTests.HandMap();

    [Fact]
    public void ColorFor_BlockedWinsOverEverything_CliffDark_RampOrange_ElseFaint()
    {
        Assert.Equal(NavOverlayBuilder.PassableColor, NavOverlayBuilder.ColorFor(NavFlags.None));
        Assert.Equal(NavOverlayBuilder.BlockedColor, NavOverlayBuilder.ColorFor(NavFlags.Blocked));
        Assert.Equal(NavOverlayBuilder.CliffColor, NavOverlayBuilder.ColorFor(NavFlags.Blocked | NavFlags.Cliff));
        Assert.Equal(NavOverlayBuilder.RampColor, NavOverlayBuilder.ColorFor(NavFlags.Ramp));
        // A blocked ramp (border ring, sealed pocket) is blocked.
        Assert.Equal(NavOverlayBuilder.BlockedColor, NavOverlayBuilder.ColorFor(NavFlags.Blocked | NavFlags.Ramp));
        Assert.Equal(NavOverlayBuilder.CliffColor, NavOverlayBuilder.ColorFor(NavFlags.Blocked | NavFlags.Cliff | NavFlags.Ramp));
        // Bits this build doesn't know (the sim track's Resource bit, anything later) never change the colour.
        for (int unknown = 8; unknown < 256; unknown += 8)
        {
            for (int known = 0; known < 8; known++)
            {
                var flags = (NavFlags)(known | unknown);
                Assert.Equal(NavOverlayBuilder.ColorFor((NavFlags)known), NavOverlayBuilder.ColorFor(flags));
            }
        }
        // Distinct, visible colours.
        var all = new[] { NavOverlayBuilder.PassableColor, NavOverlayBuilder.BlockedColor, NavOverlayBuilder.CliffColor, NavOverlayBuilder.RampColor };
        Assert.Equal(4, all.Distinct().Count());
        Assert.All(all, c => Assert.InRange(c.W, 0.05f, 0.9f));
    }

    [Theory]
    [MemberData(nameof(Maps))]
    public void EveryCell_HasOneQuad_ColouredByItsFlags(string kind, ulong seed)
    {
        Heightmap map = MapOf(kind, seed);
        var grid = new NavGrid(map);
        var b = new NavOverlayBuilder(map);
        Assert.True(b.Refresh(grid));
        int cells = map.Width * map.Height;
        Assert.Equal(cells * 4, b.Positions.Length);
        Assert.Equal(cells * 4, b.Colors.Length);
        Assert.Equal(cells * 6, b.Indices.Length);
        var seen = new HashSet<Vector4>();
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                Vector4 want = NavOverlayBuilder.ColorFor(grid.FlagsAt(x, y));
                int v = 4 * (y * map.Width + x);
                for (int k = 0; k < 4; k++) Assert.Equal(want, b.Colors[v + k]);
                seen.Add(want);
                // Edge rows and columns are the blocked border ring.
                if (x == 0 || y == 0 || x == map.Width - 1 || y == map.Height - 1)
                    Assert.True(want == NavOverlayBuilder.BlockedColor || want == NavOverlayBuilder.CliffColor, $"edge cell ({x}, {y}) is {want}");
            }
        }
        if (kind == "generated")
        {
            // A generated map has open ground, the border, cliffs and ramps.
            Assert.Contains(NavOverlayBuilder.PassableColor, seen);
            Assert.Contains(NavOverlayBuilder.BlockedColor, seen);
            Assert.Contains(NavOverlayBuilder.CliffColor, seen);
            Assert.Contains(NavOverlayBuilder.RampColor, seen);
        }
    }

    [Fact]
    public void HandMap_KnownCells()
    {
        Heightmap map = TerrainHeightTests.HandMap();
        var grid = new NavGrid(map);
        var b = new NavOverlayBuilder(map);
        b.Refresh(grid);
        Vector4 At(int x, int y) => b.Colors[4 * (y * 4 + x)];
        // Corners and the whole first and last row: border ring.
        for (int x = 0; x < 4; x++)
        {
            Assert.NotEqual(NavOverlayBuilder.PassableColor, At(x, 0));
            Assert.NotEqual(NavOverlayBuilder.PassableColor, At(x, 3));
        }
        // Inner cells follow the grid's own verdict, including the ramp (2, 1).
        Assert.True((grid.FlagsAt(2, 1) & NavFlags.Ramp) != 0);
        Assert.Equal(NavOverlayBuilder.ColorFor(grid.FlagsAt(2, 1)), At(2, 1));
        Assert.Equal(NavOverlayBuilder.ColorFor(grid.FlagsAt(1, 1)), At(1, 1));
    }

    [Theory]
    [MemberData(nameof(Maps))]
    public void Quads_SitInsetInTheirCell_LiftedAboveTheDrawnSurface_FacingUp(string kind, ulong seed)
    {
        Heightmap map = MapOf(kind, seed);
        var b = new NavOverlayBuilder(map);
        const float cs = MapConstants.CellSize;
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                int i = y * map.Width + x;
                for (int k = 0; k < 4; k++)
                {
                    Vector3 p = b.Positions[4 * i + k];
                    float wantX = (k & 1) == 0 ? x * cs + NavOverlayBuilder.Inset : (x + 1) * cs - NavOverlayBuilder.Inset;
                    float wantZ = k < 2 ? y * cs + NavOverlayBuilder.Inset : (y + 1) * cs - NavOverlayBuilder.Inset;
                    Assert.Equal(wantX, p.X, 4);
                    Assert.Equal(wantZ, p.Z, 4);
                    float surface = TerrainHeight.InCell(map, x, y, p.X, p.Z);
                    Assert.True(MathF.Abs(p.Y - surface - NavOverlayBuilder.Lift) < 1e-4f, $"cell ({x}, {y}) corner {k}: y {p.Y}, surface {surface}");
                }
                for (int t = 0; t < 2; t++)
                {
                    int a = b.Indices[6 * i + 3 * t], c1 = b.Indices[6 * i + 3 * t + 1], c2 = b.Indices[6 * i + 3 * t + 2];
                    Assert.All(new[] { a, c1, c2 }, v => Assert.InRange(v, 4 * i, 4 * i + 3));
                    // Clockwise seen from above, like the terrain's top faces: the cross product points down.
                    Vector3 n = Vector3.Cross(b.Positions[c1] - b.Positions[a], b.Positions[c2] - b.Positions[a]);
                    Assert.True(n.Y < 0f, $"cell ({x}, {y}) triangle {t} winds the wrong way");
                }
            }
        }
    }

    [Fact]
    public void Refresh_RefillsExactlyOncePerGridVersion()
    {
        Heightmap map = TerrainHeightTests.GeneratedMap(1);
        var grid = new NavGrid(map);
        var b = new NavOverlayBuilder(map);
        Assert.Equal(-1, b.BuiltVersion);
        Assert.True(b.Colors.All(c => c == Vector4.Zero));
        Assert.True(b.Refresh(grid));
        Assert.Equal(1, b.Builds);
        for (int i = 0; i < 10; i++) Assert.False(b.Refresh(grid));
        Assert.Equal(1, b.Builds);
        grid.BumpVersionForTests();
        Assert.True(b.Refresh(grid));
        Assert.Equal(grid.Version, b.BuiltVersion);
        for (int i = 0; i < 10; i++) Assert.False(b.Refresh(grid));
        Assert.Equal(2, b.Builds);
        grid.BumpVersionForTests();
        grid.BumpVersionForTests();
        Assert.True(b.Refresh(grid));
        Assert.Equal(3, b.Builds);
    }

    [Fact]
    public void Refresh_OnAGridOfAnotherSize_Throws()
    {
        var b = new NavOverlayBuilder(TerrainHeightTests.HandMap());
        Assert.Throws<ArgumentException>(() => b.Refresh(new NavGrid(TerrainHeightTests.GeneratedMap(1))));
    }

    [Fact]
    public void Refresh_AfterAVersionChange_AllocatesZeroBytes()
    {
        Heightmap map = TerrainHeightTests.GeneratedMap(2);
        var grid = new NavGrid(map);
        var b = new NavOverlayBuilder(map);
        b.Refresh(grid);
        bool rebuilt = false;
        Action setup = grid.BumpVersionForTests;
        Action block = () => rebuilt = b.Refresh(grid) && b.Refresh(grid) == false;
        int runs = AllocationProbe.AssertZero(block, _out, setup);
        Assert.True(rebuilt);
        _out.WriteLine($"nav overlay refill ({map.Width} x {map.Height}): 0 bytes (runs {runs})");
    }
}
