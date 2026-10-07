using System.Numerics;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>The minimap's pixels: terrain matches the 3D mesh's tints, dots are a 2 x 2 owner-coloured centre holding the unit's cell inside a contrasting rim.</summary>
public class MinimapRasterTests
{
    private const float Cs = MapConstants.CellSize;
    private static readonly uint[] Colors = { 0x3366CC, 0xE08020 };

    public static IEnumerable<object[]> Maps()
    {
        yield return new object[] { "hand 4x4", 0UL };
        foreach (ulong seed in new ulong[] { 1, 7, 42 }) yield return new object[] { "generated", seed };
    }

    private static Heightmap MapOf(string kind, ulong seed) =>
        kind == "generated" ? TerrainHeightTests.GeneratedMap(seed) : TerrainHeightTests.HandMap();

    [Theory]
    [MemberData(nameof(Maps))]
    public void EveryCell_HasTheMeshTopTint_CliffTint_OrADarkenedTintWhenImpassable(string kind, ulong seed)
    {
        Heightmap map = MapOf(kind, seed);
        var grid = new NavGrid(map);
        var raster = new MinimapRaster(map, grid, Colors, 1);
        TerrainMesh mesh = TerrainMeshBuilder.Build(map);
        int tops = 0, cliffs = 0, ramps = 0, shaded = 0;
        for (int q = 0; q < mesh.Positions.Length; q += 4)
        {
            if (mesh.Normals[q].Y <= 1e-3f) continue; // walls
            tops++;
            int cx = (int)(mesh.Positions[q].X / Cs), cy = (int)(mesh.Positions[q].Z / Cs);
            Vector4 meshTint = mesh.Colors[q];
            NavFlags flags = grid.FlagsAt(cx, cy);
            Vector4 expected;
            if (map.IsRamp(cx, cy))
            {
                Assert.Equal(TerrainMeshBuilder.RampColor, meshTint);
                expected = meshTint;
                ramps++;
            }
            else if ((flags & NavFlags.Cliff) != 0)
            {
                expected = TerrainMeshBuilder.CliffColor;
                cliffs++;
            }
            else
            {
                Assert.Equal(TerrainMeshBuilder.LevelColor(map.LevelAt(cx, cy)), meshTint);
                float k = (flags & NavFlags.Blocked) != 0 ? MinimapRaster.ImpassableShade : 1f;
                if (k != 1f) shaded++;
                expected = new Vector4(meshTint.X * k, meshTint.Y * k, meshTint.Z * k, meshTint.W);
            }
            int i = (cy * map.Width + cx) * 4;
            byte[] want = { Byte(expected.X), Byte(expected.Y), Byte(expected.Z), Byte(expected.W) };
            Assert.True(want.AsSpan().SequenceEqual(raster.Terrain.AsSpan(i, 4)),
                $"cell ({cx}, {cy}): raster {raster.Terrain[i]},{raster.Terrain[i + 1]},{raster.Terrain[i + 2]} want {want[0]},{want[1]},{want[2]}");
        }
        Assert.Equal(map.Width * map.Height, tops);
        Assert.True(ramps > 0 && cliffs > 0 && shaded > 0, $"{ramps} ramps, {cliffs} cliffs, {shaded} shaded: every kind must be exercised");
    }

    private static byte Byte(float c) => (byte)MathF.Round(c * 255f);

    [Fact]
    public void Constructor_DoesNotChangeTheSim()
    {
        var sim = new Simulation(TestSim.Config(1, 2, 2000, 4096));
        ulong hash = sim.StateHash();
        _ = new MinimapRaster(sim.World.Heightmap, sim.World.NavGrid, Colors, 2000);
        Assert.Equal(hash, sim.StateHash());
    }

    [Fact]
    public void Dots_LandOnTheUnitsCell_AtCornersCentresAndTheMapsLastFloat()
    {
        Heightmap map = TerrainHeightTests.GeneratedMap(1);
        var raster = new MinimapRaster(map, new NavGrid(map), Colors, 16);
        float right = map.Width * Cs, bottom = map.Height * Cs;
        (Vector2 Pos, int X, int Y)[] cases =
        {
            (new(0f, 0f), 0, 0),
            (new(3 * Cs, 5 * Cs), 3, 5), // exact corner belongs to the higher cell (floor)
            (new(10.5f * Cs, 20.5f * Cs), 10, 20), // centre
            (new(MathF.BitDecrement(4 * Cs), 7 * Cs), 3, 7), // just below a boundary
            (new(MathF.BitDecrement(right), MathF.BitDecrement(bottom)), map.Width - 1, map.Height - 1),
            (new(right, bottom), map.Width - 1, map.Height - 1), // on the far edge: clamped
            (new(-5f, -5f), 0, 0),
        };
        foreach ((Vector2 pos, int x, int y) in cases)
        {
            int drawn = raster.DrawDots(new[] { true }, new[] { pos }, new[] { 1 });
            Assert.Equal(1, drawn);
            AssertOnlyDotAt(raster, pos, x, y, Colors[1]); // the previous dot and rim were cleared
        }
    }

    [Fact]
    public void DeadUnits_UnknownOwners_AndNonFinitePositions_DrawNothing()
    {
        Heightmap map = TerrainHeightTests.HandMap();
        var raster = new MinimapRaster(map, new NavGrid(map), Colors, 8);
        bool[] alive = { false, true, true, true, true };
        Vector2[] pos = { new(1, 1), new(3, 3), new(float.NaN, 1), new(float.PositiveInfinity, 1), new(5, 5) };
        int[] owners = { 0, 2, 0, 1, -1 };
        Assert.Equal(0, raster.DrawDots(alive, pos, owners));
        Assert.Equal(0, CountDots(raster));

        raster.DrawDots(new[] { true, true }, new[] { new Vector2(1, 1), new Vector2(7, 7) }, new[] { 0, 1 });
        AssertDot(raster, 0, 0, Colors[0]);
        AssertDot(raster, 3, 3, Colors[1]);
        raster.DrawDots(new[] { false, false }, new[] { new Vector2(1, 1), new Vector2(7, 7) }, new[] { 0, 1 });
        Assert.Equal(0, CountDots(raster));
    }

    [Fact]
    public void MoreUnitsThanMaxDots_AreIgnored_NotAnError()
    {
        Heightmap map = TerrainHeightTests.HandMap();
        var raster = new MinimapRaster(map, new NavGrid(map), Colors, 2);
        Assert.Equal(2, raster.DrawDots(new[] { true, true, true }, new[] { new Vector2(1, 1), new Vector2(3, 1), new Vector2(5, 1) }, new[] { 0, 0, 0 }));
    }

    [Fact]
    public void ALoneDot_IsA2x2CentreInTheOwnerColour_InsideAOneCellRim()
    {
        // BUG-0069: a one-cell centre in a 3 x 3 rim read as its rim colour at 220 px.
        Heightmap map = TerrainHeightTests.GeneratedMap(1);
        var raster = new MinimapRaster(map, new NavGrid(map), Colors, 4);
        // In cell (20, 30), nearer its north-west corner: the centre is cells 19-20 x 29-30.
        Vector2 at = new(20.3f * Cs, 30.2f * Cs);
        raster.DrawDots(new[] { true }, new[] { at }, new[] { 0 });
        Assert.Equal(MinimapRaster.DotCells, CountDots(raster));
        Assert.Equal(16, MinimapRaster.DotCells);
        for (int y = 28; y <= 31; y++)
            for (int x = 18; x <= 21; x++)
            {
                bool centre = x is 19 or 20 && y is 29 or 30;
                AssertDot(raster, x, y, centre ? Colors[0] : MinimapRaster.RimFor(Colors[0]));
            }
        AssertOnlyDotAt(raster, at, 20, 30, Colors[0]);
    }

    [Theory]
    [InlineData(0.5f, 0.5f, 0, 0)]   // a cell's exact centre: the corner to its south-east
    [InlineData(0.49f, 0.51f, -1, 0)]
    [InlineData(0.99f, 0.01f, 0, -1)]
    [InlineData(0.01f, 0.99f, -1, 0)]
    public void TheCentre_IsTheFourCellsAroundTheNearestCorner_AndHoldsTheUnitsCell(float fx, float fy, int dx, int dy)
    {
        Heightmap map = TerrainHeightTests.GeneratedMap(1);
        var raster = new MinimapRaster(map, new NavGrid(map), Colors, 1);
        Vector2 at = new((40 + fx) * Cs, (50 + fy) * Cs);
        Assert.Equal((50 + dy) * raster.Width + 40 + dx, raster.CentreOf(at));
        raster.DrawDots(new[] { true }, new[] { at }, new[] { 1 });
        AssertOnlyDotAt(raster, at, 40, 50, Colors[1]);
    }

    [Fact]
    public void ALoneDotsCentre_CoversAtLeastThreeScreenPixelsPerAxis_At220PxFor128Cells()
    {
        // The shipped minimap: 220 px for 128 cells, nearest filtering (pixel p shows texel floor((p + 0.5) * 128 / 220)).
        const int px = 220, cells = 128;
        int fewest = int.MaxValue;
        for (int k = 1; k < cells; k++) // every possible centre: cells k-1 and k
        {
            int covered = 0;
            for (int p = 0; p < px; p++)
            {
                int texel = (int)((p + 0.5) * cells / px);
                if (texel == k - 1 || texel == k) covered++;
            }
            fewest = Math.Min(fewest, covered);
        }
        Assert.True(fewest >= 3, $"a dot centre covers only {fewest} screen pixels on some axis");
    }

    [Theory]
    [InlineData(0, 0, 9)]
    [InlineData(0, 30, 12)]
    [InlineData(-1, -1, 9)] // far corner (filled in below)
    public void ADotOnTheMapEdge_ClipsItsRim(int x, int y, int cells)
    {
        Heightmap map = TerrainHeightTests.GeneratedMap(1);
        if (x < 0) { x = map.Width - 1; y = map.Height - 1; }
        var raster = new MinimapRaster(map, new NavGrid(map), Colors, 4);
        Vector2 at = new((x + 0.5f) * Cs, (y + 0.5f) * Cs);
        raster.DrawDots(new[] { true }, new[] { at }, new[] { 1 });
        AssertOnlyDotAt(raster, at, x, y, Colors[1]);
        Assert.Equal(cells, CountDots(raster));
    }

    [Fact]
    public void NeighbouringDots_NeverHideEachOthersCell_InEitherSlotOrder()
    {
        Heightmap map = TerrainHeightTests.GeneratedMap(1);
        var raster = new MinimapRaster(map, new NavGrid(map), Colors, 4);
        Vector2 a = new(10.5f * Cs, 10.5f * Cs), b = new(11.5f * Cs, 10.5f * Cs);
        foreach ((Vector2 first, Vector2 second, int o1, int o2) in new[] { (a, b, 0, 1), (b, a, 1, 0) })
        {
            Assert.Equal(2, raster.DrawDots(new[] { true, true }, new[] { first, second }, new[] { o1, o2 }));
            AssertDot(raster, 10, 10, Colors[0]); // a is always owner 0, b owner 1: each unit's own cell shows its colour
            AssertDot(raster, 11, 10, Colors[1]);
            Assert.Equal(20, CountDots(raster)); // two overlapping 4 x 4 blocks: 5 x 4 cells
            for (int y = 10; y <= 11; y++)
                for (int x = 10; x <= 12; x++)
                {
                    int i = (y * raster.Width + x) * 4;
                    uint rgb = (uint)(raster.Dots[i] << 16 | raster.Dots[i + 1] << 8 | raster.Dots[i + 2]);
                    Assert.True(rgb == Colors[0] || rgb == Colors[1], $"centre cell ({x}, {y}) is {rgb:X6}, a rim colour");
                }
        }
    }

    [Fact]
    public void EveryShippedFactionColour_GetsARimThatContrastsWithIt()
    {
        // BUG-0064: Malazan's dark grey vanished on cliffs, Whirlwind's orange looked like a ramp tick.
        foreach (Rts.Sim.Data.FactionDef f in TestSim.Data.Factions)
        {
            uint rim = MinimapRaster.RimFor(f.PrimaryColor);
            float gap = MathF.Abs(Luma(rim) - Luma(f.PrimaryColor));
            Assert.True(gap >= 0.35f, $"{f.Id}: dot {f.PrimaryColor:X6} rim {rim:X6}, luma gap {gap:F2}");
        }
        Assert.Equal(MinimapRaster.LightRim, MinimapRaster.RimFor(0x000000));
        Assert.Equal(MinimapRaster.DarkRim, MinimapRaster.RimFor(0xFFFFFF));
    }

    private static float Luma(uint rgb) => (0.299f * (byte)(rgb >> 16) + 0.587f * (byte)(rgb >> 8) + 0.114f * (byte)rgb) / 255f;

    // Every opaque pixel belongs to the one dot of a unit at `pos`, whose cell is (x, y): its 2 x 2 centre (the four cells
    // around the cell corner nearest the unit, clamped onto the map; it holds (x, y)) in the owner colour, and the rest
    // of its 4 x 4 block in the rim colour.
    private static void AssertOnlyDotAt(MinimapRaster r, Vector2 pos, int x, int y, uint rgb)
    {
        int kx = (int)Math.Clamp(Math.Floor(pos.X / Cs + 0.5), 1, r.Width - 1), ky = (int)Math.Clamp(Math.Floor(pos.Y / Cs + 0.5), 1, r.Height - 1);
        int cx = kx - 1, cy = ky - 1;
        Assert.True(x >= cx && x <= cx + 1 && y >= cy && y <= cy + 1, $"the centre at ({cx}, {cy}) does not hold the unit's cell ({x}, {y})");
        AssertDot(r, x, y, rgb);
        for (int py = 0; py < r.Height; py++)
            for (int px = 0; px < r.Width; px++)
            {
                int i = (py * r.Width + px) * 4;
                bool inCentre = px >= cx && px <= cx + 1 && py >= cy && py <= cy + 1;
                bool inBlock = px >= cx - 1 && px <= cx + 2 && py >= cy - 1 && py <= cy + 2;
                if (r.Dots[i + 3] == 0)
                {
                    Assert.False(inBlock, $"({px}, {py}) inside the dot block at ({cx}, {cy}) is transparent");
                    continue;
                }
                Assert.True(inBlock, $"stray dot pixel at ({px}, {py}), dot centre at ({cx}, {cy})");
                AssertDot(r, px, py, inCentre ? rgb : MinimapRaster.RimFor(rgb));
            }
    }

    private static bool IsRgb(MinimapRaster r, int x, int y, uint rgb)
    {
        int i = (y * r.Width + x) * 4;
        return r.Dots[i + 3] == 255 && r.Dots[i] == (byte)(rgb >> 16) && r.Dots[i + 1] == (byte)(rgb >> 8) && r.Dots[i + 2] == (byte)rgb;
    }

    private static void AssertDot(MinimapRaster r, int x, int y, uint rgb)
    {
        int i = (y * r.Width + x) * 4;
        Assert.Equal(((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, (byte)255), (r.Dots[i], r.Dots[i + 1], r.Dots[i + 2], r.Dots[i + 3]));
    }

    private static int CountDots(MinimapRaster r)
    {
        int n = 0;
        for (int i = 3; i < r.Dots.Length; i += 4) if (r.Dots[i] != 0) n++;
        return n;
    }
}
