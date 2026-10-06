using System.Numerics;
using Rts.Sim.Determinism;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>QA (M2-2): <see cref="GroundPicker"/> against an oracle that intersects the ray with the drawn mesh's own triangles (not <see cref="TerrainHeight"/>).</summary>
/// <remarks>
/// The developer's oracle marches against <c>TerrainHeight.At</c>, the same plane maths the picker
/// uses, so a shared mistake would pass both. This one reads only <see cref="TerrainMeshBuilder"/>
/// output: two-sided Moller-Trumbore in double precision over every top and wall triangle, nearest
/// hit wins. Cameras are placed exactly as <c>RtsCamera</c> does (pitch 55, vertical FOV 50, 16:9),
/// over random focus points at both zoom limits, through random pixels including the top row
/// (the grazing rays). Extra attacks: cliff faces seen from the low side must give a point in the
/// upper cell, rays that miss, degenerate and non-finite rays, and shallow rays skimming walls.
/// </remarks>
public class GroundPickerQaTests
{
    private const float Cs = MapConstants.CellSize;
    private const float FovDegrees = 50f;
    private const float Aspect = 16f / 9f;
    private readonly ITestOutputHelper _out;

    public GroundPickerQaTests(ITestOutputHelper output) => _out = output;

    private static Heightmap Generated(ulong seed)
    {
        var rng = new SimRng(seed, RngStream.MapGen);
        return MapGenerator.Generate(MapGenParams.Default, ref rng);
    }

    /// <summary>All mesh triangles as double-precision vertex triples.</summary>
    private sealed class MeshOracle
    {
        private readonly (Vector3 A, Vector3 B, Vector3 C)[] _tris;

        public MeshOracle(Heightmap map)
        {
            TerrainMesh m = TerrainMeshBuilder.Build(map);
            _tris = new (Vector3, Vector3, Vector3)[m.Indices.Length / 3];
            for (int i = 0; i < _tris.Length; i++)
                _tris[i] = (m.Positions[m.Indices[3 * i]], m.Positions[m.Indices[3 * i + 1]], m.Positions[m.Indices[3 * i + 2]]);
        }

        public bool Hit(Vector3 o, Vector3 d, out Vector3 hit)
        {
            double ox = o.X, oy = o.Y, oz = o.Z, dx = d.X, dy = d.Y, dz = d.Z;
            double best = double.PositiveInfinity;
            foreach ((Vector3 a, Vector3 b, Vector3 c) in _tris)
            {
                double e1x = b.X - a.X, e1y = b.Y - a.Y, e1z = b.Z - a.Z;
                double e2x = c.X - a.X, e2y = c.Y - a.Y, e2z = c.Z - a.Z;
                double px = dy * e2z - dz * e2y, py = dz * e2x - dx * e2z, pz = dx * e2y - dy * e2x;
                double det = e1x * px + e1y * py + e1z * pz;
                if (Math.Abs(det) < 1e-12) continue;
                double inv = 1.0 / det;
                double tx = ox - a.X, ty = oy - a.Y, tz = oz - a.Z;
                double u = (tx * px + ty * py + tz * pz) * inv;
                if (u < -1e-9 || u > 1 + 1e-9) continue;
                double qx = ty * e1z - tz * e1y, qy = tz * e1x - tx * e1z, qz = tx * e1y - ty * e1x;
                double v = (dx * qx + dy * qy + dz * qz) * inv;
                if (v < -1e-9 || u + v > 1 + 1e-9) continue;
                double t = (e2x * qx + e2y * qy + e2z * qz) * inv;
                if (t >= 0 && t < best) best = t;
            }
            hit = double.IsInfinity(best) ? default : new Vector3((float)(ox + dx * best), (float)(oy + dy * best), (float)(oz + dz * best));
            return !double.IsInfinity(best);
        }
    }

    // RtsCamera.ApplyTransform: (focus.x, zoom, focus.y + zoom / tan 55), pitched 55 down, looking -Z.
    private static (Vector3 Origin, Vector3 Dir) CameraRay(float fx, float fz, float zoom, float ndcX, float ndcY)
    {
        float pitch = CameraLimits.PitchDegrees * MathF.PI / 180f;
        var origin = new Vector3(fx, zoom, fz + zoom / MathF.Tan(pitch));
        var forward = new Vector3(0, -MathF.Sin(pitch), -MathF.Cos(pitch));
        var up = new Vector3(0, MathF.Cos(pitch), -MathF.Sin(pitch));
        var right = Vector3.UnitX;
        float th = MathF.Tan(FovDegrees * MathF.PI / 360f);
        Vector3 dir = forward + right * (ndcX * th * Aspect) + up * (ndcY * th);
        return (origin, Vector3.Normalize(dir));
    }

    private static bool OnMapEdge(Heightmap map, Vector3 p) =>
        p.X <= GroundPicker.CellInset * 2 || p.Z <= GroundPicker.CellInset * 2
        || p.X >= map.Width * Cs - GroundPicker.CellInset * 2 || p.Z >= map.Height * Cs - GroundPicker.CellInset * 2;

    [Theory]
    [InlineData(1UL)]
    [InlineData(7UL)]
    [InlineData(42UL)]
    public void CameraRays_BothZoomLimits_MatchTheDrawnMesh_Within10cm(ulong seed)
    {
        Heightmap map = Generated(seed);
        var oracle = new MeshOracle(map);
        var rng = new SimRng(seed * 31 + 5, RngStream.MapGen);
        float worst = 0f;
        int hits = 0, misses = 0, walls = 0, edge = 0, grazing = 0, highGround = 0, ramps = 0;
        float w = map.Width * Cs, h = map.Height * Cs;
        foreach (float zoom in new[] { CameraLimits.MinZoom, CameraLimits.MaxZoom })
        {
            for (int k = 0; k < 220; k++)
            {
                float fx = rng.NextFloat() * w, fz = rng.NextFloat() * h;
                // A third of the rays go through the top pixel row (shallowest), the rest anywhere.
                float ndcY = k % 3 == 0 ? 1f : rng.NextFloat() * 2f - 1f;
                float ndcX = rng.NextFloat() * 2f - 1f;
                if (k % 3 == 0) grazing++;
                (Vector3 o, Vector3 d) = CameraRay(fx, fz, zoom, ndcX, ndcY);
                bool want = oracle.Hit(o, d, out Vector3 truth);
                bool got = GroundPicker.TryPick(map, o, d, out Vector3 hit);
                if (got && OnMapEdge(map, hit) && (!want || Vector3.Distance(o, truth) > Vector3.Distance(o, hit) + 0.05f))
                {
                    edge++; // entered through the map's side below an edge cell's top; the mesh draws no skirt there
                    continue;
                }
                Assert.True(want == got, $"zoom {zoom} focus ({fx:F2}, {fz:F2}) ndc ({ndcX:F3}, {ndcY:F3}): mesh hit {want} {truth}, picker hit {got} {hit}");
                if (!got) { misses++; continue; }
                hits++;
                float err = Vector3.Distance(truth, hit);
                worst = MathF.Max(worst, err);
                Assert.True(err <= 0.1f, $"zoom {zoom} focus ({fx:F2}, {fz:F2}) ndc ({ndcX:F3}, {ndcY:F3}): picker {hit}, mesh {truth}, {err:F4} m");
                // The order target's cell must be the one whose surface the ray reached: never the cell behind a wall.
                float surface = TerrainHeight.At(map, hit.X, hit.Z);
                Assert.True(hit.Y <= surface + 0.01f, $"picked {hit} floats {hit.Y - surface:F3} m above its cell's surface");
                if (surface - hit.Y > 0.01f) walls++;
                int cx = (int)(hit.X / Cs), cz = (int)(hit.Z / Cs);
                if (map.LevelAt(cx, cz) == MapConstants.MaxLevel) highGround++;
                if (map.IsRamp(cx, cz)) ramps++;
            }
        }
        _out.WriteLine($"seed {seed}: {hits} hits ({walls} on cliff faces, {highGround} high ground, {ramps} ramps), {misses} misses, {edge} map-side, {grazing} top-row rays; worst {worst * 1000:F3} mm");
        Assert.True(hits > 300, "too few hits to mean anything");
    }

    [Theory]
    [InlineData(2UL)]
    [InlineData(9UL)]
    public void CameraRaysAimedAtRampAndHighGroundCells_BothZoomLimits_MatchTheDrawnMesh(ulong seed)
    {
        Heightmap map = Generated(seed);
        var oracle = new MeshOracle(map);
        var rng = new SimRng(seed * 17 + 3, RngStream.MapGen);
        int ramps = 0, high = 0, occluded = 0;
        float worst = 0f;
        for (int cz = 0; cz < map.Height; cz++)
            for (int cx = 0; cx < map.Width; cx++)
            {
                bool ramp = map.IsRamp(cx, cz);
                bool top = map.LevelAt(cx, cz) == MapConstants.MaxLevel && !ramp && (cx * 7 + cz * 3) % 7 == 0;
                if (!ramp && !top) continue;
                foreach (float zoom in new[] { CameraLimits.MinZoom, CameraLimits.MaxZoom })
                {
                    float x = (cx + 0.1f + 0.8f * rng.NextFloat()) * Cs, z = (cz + 0.1f + 0.8f * rng.NextFloat()) * Cs;
                    var p = new Vector3(x, TerrainHeight.At(map, x, z), z);
                    // Same orientation as the game camera, any pixel; back the origin up the ray to the zoom height.
                    (_, Vector3 d) = CameraRay(0, 0, zoom, rng.NextFloat() * 2 - 1, rng.NextFloat() * 2 - 1);
                    Vector3 o = p - d * ((zoom - p.Y) / -d.Y);
                    Assert.True(oracle.Hit(o, d, out Vector3 truth), $"oracle missed cell ({cx}, {cz})");
                    Assert.True(GroundPicker.TryPick(map, o, d, out Vector3 hit), $"picker missed cell ({cx}, {cz}) from zoom {zoom}");
                    float err = Vector3.Distance(truth, hit);
                    worst = MathF.Max(worst, err);
                    Assert.True(err <= 0.1f, $"cell ({cx}, {cz}) zoom {zoom}: picker {hit}, mesh {truth}, {err:F4} m");
                    if (Vector3.Distance(truth, p) > 0.01f) occluded++;
                    if (ramp) ramps++; else high++;
                }
            }
        _out.WriteLine($"seed {seed}: {ramps} ramp rays, {high} high-ground rays ({occluded} occluded by nearer terrain); worst {worst * 1000:F3} mm");
        Assert.True(ramps > 20 && high > 20, "map has too few ramp or high-ground cells");
    }

    // 8 x 8: rows 0-3 a level-2 plateau (8 m), rows 4-7 level 0. The camera looks north (-Z), so the cliff face at z = 8 faces it.
    private static Heightmap CliffNorth()
    {
        var levels = new byte[64];
        var elev = new float[64];
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                byte l = (byte)(y < 4 ? 2 : 0);
                levels[y * 8 + x] = l;
                elev[y * 8 + x] = l * MapConstants.LevelHeight;
            }
        return new Heightmap(8, 8, levels, elev);
    }

    [Theory]
    [InlineData(20f)]
    [InlineData(40f)]
    [InlineData(60f)]
    public void RayIntoACliffFace_PicksTheUpperPlateau_NotTheLowGroundBehindOrBelow(float zoom)
    {
        Heightmap map = CliffNorth();
        // Aim straight at the middle of the face: (8, 4, 8). Camera placed as RtsCamera at that zoom, focus shifted so the ray passes through.
        var target = new Vector3(8f, 4f, 8f);
        float pitch = CameraLimits.PitchDegrees * MathF.PI / 180f;
        var origin = new Vector3(8f, zoom, 30f);
        Vector3 dir = Vector3.Normalize(target - origin);
        Assert.True(GroundPicker.TryPick(map, origin, dir, out Vector3 hit), "ray into the cliff face missed");
        int cz = (int)(hit.Z / Cs);
        Assert.True(cz == 3, $"cliff hit {hit} is in row {cz}, expected the plateau's edge row 3");
        Assert.True(MathF.Abs(hit.Y - 4f) < 0.05f, $"cliff hit {hit} not on the face's middle");
        Assert.True(TerrainHeight.At(map, hit.X, hit.Z) == 2 * MapConstants.LevelHeight, "target cell is not the plateau");
        _ = pitch;
    }

    [Fact]
    public void RayJustUnderTheLip_HitsTheFace_RayJustOverIt_HitsThePlateauTop()
    {
        Heightmap map = CliffNorth();
        var origin = new Vector3(5f, 30f, 40f);
        Vector3 under = Vector3.Normalize(new Vector3(5f, 7.99f, 8f) - origin);
        Vector3 over = Vector3.Normalize(new Vector3(5f, 8.01f, 8f) - origin);
        Assert.True(GroundPicker.TryPick(map, origin, under, out Vector3 a));
        Assert.True(GroundPicker.TryPick(map, origin, over, out Vector3 b));
        Assert.True((int)(a.Z / Cs) == 3 && a.Y < 8f, $"under the lip: {a}");
        Assert.True((int)(b.Z / Cs) <= 3 && MathF.Abs(b.Y - 8f) < 1e-3f, $"over the lip: {b}");
    }

    [Fact]
    public void ShallowRaysSkimmingTheLowGround_StopAtTheFirstWall()
    {
        Heightmap map = CliffNorth();
        // Nearly horizontal rays 1 m above the low ground heading north must stop on the face at z = 8.
        for (int i = 0; i < 50; i++)
        {
            float x = 0.5f + i * 0.3f;
            var origin = new Vector3(x, 1f, 15.9f);
            var dir = new Vector3(0.01f * (i % 5 - 2), -0.0001f * i, -1f);
            Assert.True(GroundPicker.TryPick(map, origin, dir, out Vector3 hit), $"ray {i} missed");
            Assert.True((int)(hit.Z / Cs) == 3, $"ray {i}: hit {hit} not in the plateau's edge row");
        }
    }

    [Fact]
    public void Misses_DegenerateAndNonFiniteRays_ReturnFalseWithoutThrowing()
    {
        Heightmap map = Generated(3);
        float w = map.Width * Cs, h = map.Height * Cs;
        Vector3[] origins = { new(w / 2, 60, h / 2), new(-50, 60, -50), new(w + 10, 5, h / 2), new(w / 2, 60, h + 500) };
        Vector3[] dirs =
        {
            Vector3.UnitY, // straight up
            new(0, 0, 0),
            new(float.NaN, -1, 0),
            new(0, float.PositiveInfinity, 0),
            new(-1, 0, 0), // horizontal, away
            new(0, 0.01f, -1), // rising
        };
        foreach (Vector3 o in origins)
            foreach (Vector3 d in dirs)
            {
                bool got = GroundPicker.TryPick(map, o, d, out Vector3 hit);
                Assert.False(got && (d == Vector3.Zero || !float.IsFinite(d.X) || !float.IsFinite(d.Y)), $"degenerate ray {d} from {o} hit {hit}");
                if (d == Vector3.UnitY || d == new Vector3(0, 0.01f, -1)) Assert.False(got, $"rising ray {d} from {o} hit {hit}");
            }
        // Rays aimed past the map's corner from the camera height miss.
        Assert.False(GroundPicker.TryPick(map, new Vector3(-10, 60, -10), Vector3.Normalize(new Vector3(-1, -1, -1)), out _));
        Assert.False(GroundPicker.TryPick(map, new Vector3(w + 10, 60, h + 10), Vector3.Normalize(new Vector3(1, -1, 1)), out _));
        // Non-finite origins.
        Assert.False(GroundPicker.TryPick(map, new Vector3(float.NaN, 60, 0), -Vector3.UnitY, out _));
        Assert.False(GroundPicker.TryPick(map, new Vector3(float.PositiveInfinity, 60, 0), -Vector3.UnitY, out _));
    }

    [Fact]
    public void StraightDownRays_OnEveryCellOfAGeneratedMap_ReturnTheSurfaceHeight()
    {
        Heightmap map = Generated(11);
        float worst = 0f;
        for (int cz = 0; cz < map.Height; cz++)
            for (int cx = 0; cx < map.Width; cx++)
            {
                float x = (cx + 0.37f) * Cs, z = (cz + 0.61f) * Cs;
                Assert.True(GroundPicker.TryPick(map, new Vector3(x, 60, z), -Vector3.UnitY, out Vector3 hit), $"cell ({cx}, {cz}) missed straight down");
                float err = MathF.Abs(hit.Y - TerrainHeight.At(map, x, z)) + MathF.Abs(hit.X - x) + MathF.Abs(hit.Z - z);
                worst = MathF.Max(worst, err);
            }
        _out.WriteLine($"straight-down worst error {worst * 1000:F4} mm");
        Assert.True(worst < 1e-3f, $"straight-down worst error {worst} m");
    }
}
