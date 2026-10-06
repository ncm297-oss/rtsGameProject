using System.Numerics;
using Rts.Sim.Determinism;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>QA (M2-2): units must not float or sink: <see cref="TerrainHeight.At"/> against the drawn triangles at random interior points, and robustness off the map.</summary>
/// <remarks>
/// The developer's test checks corners and centres. This samples random points inside every top
/// quad and interpolates the height from the triangle of the quad the point falls in (the GPU's
/// view of the surface), so a non-planar or mis-split quad would show up away from the corners.
/// </remarks>
public class TerrainHeightQaTests
{
    private const float Cs = MapConstants.CellSize;
    private readonly ITestOutputHelper _out;

    public TerrainHeightQaTests(ITestOutputHelper output) => _out = output;

    // Height of triangle (a, b, c) at ground point (x, z), or NaN if (x, z) is outside it.
    private static float TriangleHeight(Vector3 a, Vector3 b, Vector3 c, float x, float z)
    {
        float det = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
        if (MathF.Abs(det) < 1e-9f) return float.NaN;
        float l1 = ((b.Z - c.Z) * (x - c.X) + (c.X - b.X) * (z - c.Z)) / det;
        float l2 = ((c.Z - a.Z) * (x - c.X) + (a.X - c.X) * (z - c.Z)) / det;
        float l3 = 1 - l1 - l2;
        const float eps = -1e-5f;
        if (l1 < eps || l2 < eps || l3 < eps) return float.NaN;
        return l1 * a.Y + l2 * b.Y + l3 * c.Y;
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(4UL)]
    [InlineData(13UL)]
    [InlineData(29UL)]
    public void At_MatchesTheDrawnTriangles_AtRandomInteriorPoints(ulong seed)
    {
        var mapRng = new SimRng(seed, RngStream.MapGen);
        Heightmap map = MapGenerator.Generate(MapGenParams.Default, ref mapRng);
        TerrainMesh mesh = TerrainMeshBuilder.Build(map);
        var rng = new SimRng(seed + 1000, RngStream.Combat);
        float worstRamp = 0f, worstFlat = 0f;
        int rampSamples = 0;
        for (int q = 0; q < mesh.Positions.Length; q += 4)
        {
            if (mesh.Normals[q].Y <= 1e-3f) continue; // walls
            int i0 = q / 4 * 6;
            Vector3 p0 = mesh.Positions[q];
            int cx = (int)(p0.X / Cs), cz = (int)(p0.Z / Cs);
            bool ramp = map.IsRamp(cx, cz);
            int samples = ramp ? 40 : 2;
            for (int s = 0; s < samples; s++)
            {
                float x = (cx + rng.NextFloat()) * Cs, z = (cz + rng.NextFloat()) * Cs;
                float drawn = float.NaN;
                for (int t = 0; t < 2 && float.IsNaN(drawn); t++)
                {
                    int k = i0 + 3 * t;
                    drawn = TriangleHeight(mesh.Positions[mesh.Indices[k]], mesh.Positions[mesh.Indices[k + 1]], mesh.Positions[mesh.Indices[k + 2]], x, z);
                }
                Assert.False(float.IsNaN(drawn), $"({x}, {z}) is in neither triangle of cell ({cx}, {cz})");
                float err = MathF.Abs(drawn - TerrainHeight.At(map, x, z));
                if (ramp) { worstRamp = MathF.Max(worstRamp, err); rampSamples++; }
                else worstFlat = MathF.Max(worstFlat, err);
            }
        }
        _out.WriteLine($"seed {seed}: {rampSamples} ramp samples, worst ramp error {worstRamp * 1000:F4} mm, worst flat error {worstFlat * 1000:F4} mm");
        Assert.True(rampSamples > 0, "no ramps");
        Assert.True(worstRamp <= 1e-3f && worstFlat <= 1e-3f, $"surface error ramp {worstRamp} m, flat {worstFlat} m (limit 1 mm)");
    }

    [Theory]
    [InlineData(1e10f, 5f)]
    [InlineData(-1e10f, 5f)]
    [InlineData(5f, 1e10f)]
    [InlineData(float.PositiveInfinity, 5f)]
    [InlineData(float.NegativeInfinity, 5f)]
    [InlineData(5f, float.PositiveInfinity)]
    [InlineData(float.MaxValue, float.MaxValue)]
    public void At_FarOffTheMapOrInfinite_ClampsOntoTheMapInsteadOfThrowing(float x, float y)
    {
        // TerrainHeight.At's contract: "points off the map are clamped onto it".
        var rng = new SimRng(5, RngStream.MapGen);
        Heightmap map = MapGenerator.Generate(MapGenParams.Default, ref rng);
        float h = TerrainHeight.At(map, x, y);
        Assert.True(float.IsFinite(h) && h >= 0f && h <= MapConstants.MaxLevel * MapConstants.LevelHeight, $"At({x}, {y}) = {h}");
    }
}
