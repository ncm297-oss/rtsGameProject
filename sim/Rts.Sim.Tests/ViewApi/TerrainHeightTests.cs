using System.Numerics;
using Rts.Sim.Determinism;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>Unit views stand on <see cref="TerrainHeight"/>; it must match the drawn mesh exactly.</summary>
public class TerrainHeightTests
{
    private const float Tolerance = 1e-5f;

    internal static Heightmap GeneratedMap(ulong seed)
    {
        var rng = new SimRng(seed, RngStream.MapGen);
        return MapGenerator.Generate(MapGenParams.Default, ref rng);
    }

    // 4 x 4: columns 0-1 a level-1 plateau, 2-3 level 0, cell (2, 1) a ramp rising west (as in TerrainMeshBuilderTests).
    internal static Heightmap HandMap()
    {
        const float lo = 0f, hi = MapConstants.LevelHeight;
        byte[] levels = { 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0 };
        float[] elev = { hi, hi, lo, lo, hi, hi, hi / 2, lo, hi, hi, lo, lo, hi, hi, lo, lo };
        return new Heightmap(4, 4, levels, elev);
    }

    public static IEnumerable<object[]> Maps()
    {
        yield return new object[] { "hand 4x4", 0UL };
        for (ulong seed = 1; seed <= 20; seed++) yield return new object[] { "generated", seed };
    }

    [Theory]
    [MemberData(nameof(Maps))]
    public void At_EqualsTheMeshTopSurface_AtEveryCellCornerAndCentre(string kind, ulong seed)
    {
        Heightmap map = kind == "generated" ? GeneratedMap(seed) : HandMap();
        TerrainMesh mesh = TerrainMeshBuilder.Build(map);
        const float cs = MapConstants.CellSize;
        int tops = 0, ramps = 0;
        // Every quad is four consecutive vertices; tops have an upward normal, walls a horizontal one.
        for (int q = 0; q < mesh.Positions.Length; q += 4)
        {
            if (mesh.Normals[q].Y <= 1e-3f) continue;
            tops++;
            Vector3 p0 = mesh.Positions[q], p1 = mesh.Positions[q + 1], p2 = mesh.Positions[q + 2], p3 = mesh.Positions[q + 3];
            int cx = (int)(p0.X / cs), cy = (int)(p0.Z / cs);
            if (map.IsRamp(cx, cy)) ramps++;
            foreach (Vector3 p in new[] { p0, p1, p2, p3 })
                Near(p.Y, TerrainHeight.InCell(map, cx, cy, p.X, p.Z), $"cell ({cx}, {cy}) corner {p}");
            // The cell's own min corner is where At's floor rule picks this cell.
            Near(p0.Y, TerrainHeight.At(map, p0.X, p0.Z), $"cell ({cx}, {cy}) min corner via At");

            // The centre lies on both triangles' shared diagonal; a planar quad gives one height.
            float diagA = (p0.Y + p2.Y) / 2, diagB = (p1.Y + p3.Y) / 2;
            Near(diagA, diagB, $"cell ({cx}, {cy}) top is not planar");
            Near(diagA, TerrainHeight.At(map, (cx + 0.5f) * cs, (cy + 0.5f) * cs), $"cell ({cx}, {cy}) centre");
        }
        Assert.Equal(map.Width * map.Height, tops);
        Assert.True(ramps > 0, "map has no ramp cells to check");
    }

    [Fact]
    public void HandMap_Ramp_RunsFromThePlateauLipToTheLowGround()
    {
        Heightmap map = HandMap();
        const float cs = MapConstants.CellSize, hi = MapConstants.LevelHeight;
        Assert.Equal(hi, TerrainHeight.At(map, 2 * cs, 1.5f * cs), 5);
        Assert.Equal(hi / 2, TerrainHeight.At(map, 2.5f * cs, 1.5f * cs), 5);
        Assert.Equal(hi / 4, TerrainHeight.At(map, 2.75f * cs, 1.2f * cs), 5);
        Assert.Equal(hi, TerrainHeight.At(map, 1.5f * cs, 0.5f * cs), 5);
        Assert.Equal(0f, TerrainHeight.At(map, 3.5f * cs, 3.5f * cs), 5);
    }

    [Theory]
    [InlineData(-10f, -10f, MapConstants.LevelHeight)] // clamps onto cell (0, 0)
    [InlineData(100f, 100f, 0f)]                      // clamps onto cell (3, 3)
    [InlineData(float.NaN, float.NaN, MapConstants.LevelHeight)]
    [InlineData(8f, 8f, 0f)]                          // the far map corner belongs to the last cell
    public void At_OffTheMap_ClampsOntoTheEdgeCells(float x, float y, float expected)
    {
        Assert.Equal(expected, TerrainHeight.At(HandMap(), x, y));
    }

    private static void Near(float expected, float actual, string what) =>
        Assert.True(MathF.Abs(expected - actual) <= Tolerance, $"{what}: expected {expected}, got {actual}");
}
