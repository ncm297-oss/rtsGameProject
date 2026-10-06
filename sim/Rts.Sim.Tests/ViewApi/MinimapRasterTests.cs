using System.Numerics;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>The minimap's pixels: terrain matches the 3D mesh's tints, dots land on the unit's cell in its owner's colour.</summary>
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
            AssertDot(raster, x, y, Colors[1]);
            Assert.Equal(1, CountDots(raster)); // the previous dot was cleared
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
