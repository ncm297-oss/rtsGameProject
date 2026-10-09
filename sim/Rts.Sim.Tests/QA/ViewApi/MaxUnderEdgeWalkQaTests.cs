using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>
/// QA (M4-V3 fix round 2, BUG-0224): <see cref="TerrainHeight.MaxUnder"/>'s cell-edge walk with its 0.15 m seam slack.
/// Random (not grid) points and radii near every edge of generated maps against a fine 512-step wall-aware reference;
/// 0 bytes per call; hostile inputs (NaN, infinities, off-map, huge radii) stay finite and terminate; the size of the
/// steps generated maps actually have at cell edges, so the slack is known to sit between seams and walls.
/// </summary>
public class MaxUnderEdgeWalkQaTests
{
    private readonly ITestOutputHelper _out;

    public MaxUnderEdgeWalkQaTests(ITestOutputHelper output) => _out = output;

    private const float Diag = 0.70710678f;

    public static IEnumerable<object[]> Seeds()
    {
        foreach (ulong s in new ulong[] { 1, 5, 17, 23, 42, 77, 1234 }) yield return new object[] { s };
    }

    // The CorpseDiscRimQaTests reference at a finer step count (512): a step bigger than a slope could make (x2) plus 0.05 m is a wall.
    private static float FineReference(Heightmap map, float x, float y, float r, int steps)
    {
        float centre = TerrainHeight.At(map, x, y);
        float best = centre;
        for (int k = 0; k < 8; k++)
        {
            float dx = k switch { 0 => 1f, 1 => -1f, 2 => 0f, 3 => 0f, 4 => Diag, 5 => Diag, 6 => -Diag, _ => -Diag } * r;
            float dy = k switch { 0 => 0f, 1 => 0f, 2 => 1f, 3 => -1f, 4 => Diag, 5 => -Diag, 6 => Diag, _ => -Diag } * r;
            float prev = centre, stepLen = r / steps;
            float maxStep = stepLen * (MapConstants.LevelHeight / MapConstants.CellSize) * 2f + 0.05f;
            bool wall = false;
            for (int s = 1; s <= steps; s++)
            {
                float h = TerrainHeight.At(map, x + dx * s / steps, y + dy * s / steps);
                if (MathF.Abs(h - prev) > maxStep) { wall = true; break; }
                prev = h;
            }
            if (!wall) best = MathF.Max(best, prev);
        }
        return best;
    }

    private static bool NearAnEdge(Heightmap map, int cx, int cy)
    {
        if (map.IsRamp(cx, cy)) return true;
        float e = map.ElevationAt(cx, cy);
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int nx = cx + dx, ny = cy + dy;
            if ((uint)nx >= (uint)map.Width || (uint)ny >= (uint)map.Height) continue;
            if (map.IsRamp(nx, ny) || map.ElevationAt(nx, ny) != e) return true;
        }
        return false;
    }

    // Random points (8 per edge cell) and random radii in [0.48, 1.08] m against the 512-step reference.
    private (int points, int sunk, float worstSink, float worstHang, int overTenth, string at) RandomProbe(ulong seed)
    {
        Heightmap map = Rts.Sim.Tests.ViewApi.TerrainHeightTests.GeneratedMap(seed);
        var rnd = new Random((int)seed * 7919 + 3);
        const float cs = MapConstants.CellSize;
        int points = 0, sunk = 0, overTenth = 0;
        float worstSink = 0f, worstHang = 0f;
        string at = "";
        for (int cy = 0; cy < map.Height; cy++)
        for (int cx = 0; cx < map.Width; cx++)
        {
            if (!NearAnEdge(map, cx, cy)) continue;
            for (int k = 0; k < 8; k++)
            {
                float x = (cx + (float)rnd.NextDouble()) * cs, y = (cy + (float)rnd.NextDouble()) * cs;
                float r = 0.48f + 0.6f * (float)rnd.NextDouble();
                float got = TerrainHeight.MaxUnder(map, x, y, r);
                float want = FineReference(map, x, y, r, 512);
                points++;
                Assert.True(float.IsFinite(got) && got >= TerrainHeight.At(map, x, y) - 1e-4f, $"({x}, {y}) r {r}: {got}");
                if (want - got > 1e-3f) { sunk++; worstSink = MathF.Max(worstSink, want - got); }
                float hang = got - want;
                if (hang > 0.1f) overTenth++;
                if (hang > worstHang) { worstHang = hang; at = $"({x:R}, {y:R}) r {r:R} cell ({cx},{cy}) ramp {map.IsRamp(cx, cy)} e{map.ElevationAt(cx, cy):F1}"; }
            }
        }
        _out.WriteLine($"seed {seed}: {points} random points; sunk {sunk} (worst {worstSink:F3} m); worst hang {worstHang:F3} m at {at}; {overTenth} over 0.1 m");
        return (points, sunk, worstSink, worstHang, overTenth, at);
    }

    // BUG-0190 item 1 kept at random points: never sinks below the wall-free ground. The hang guard (0.75 m) pins the
    // residual of BUG-0226 (a side wall's foot under the 0.15 m slack lets the disc up the ramp): ~0.27-0.52 m today.
    [Theory]
    [MemberData(nameof(Seeds))]
    public void MaxUnder_RandomPointsAndRadii_NeverSinks_AndHangsUnderThreeQuartersOfAMetre(ulong seed)
    {
        var p = RandomProbe(seed);
        Assert.Equal(0, p.sunk);
        Assert.True(p.worstHang < 0.75f, $"seed {seed}: hang {p.worstHang:F3} m at {p.at}");
    }

    // BUG-0226: the BUG-0224 row's own bound (0.25 m) at random points instead of its 6 x 6 grid.
    [Theory(Skip = "BUG-0226: beside a ramp's side wall near its foot the disc still hangs up to ~0.5 m over the wall-free ground")]
    [MemberData(nameof(Seeds))]
    public void MaxUnder_RandomPointsAndRadii_HangsUnderAQuarterMetre(ulong seed)
    {
        var p = RandomProbe(seed);
        Assert.True(p.worstHang < 0.25f, $"seed {seed}: hang {p.worstHang:F3} m at {p.at}");
    }

    // The steps generated maps have where two edge-adjacent cells' drawn surfaces meet (17 points along every edge that
    // touches a ramp). TerrainHeight's SeamSlack comment says slope edges have "a few generated seams about 0.1 m high";
    // QA finds none (BUG-0226): ramp-to-ramp edges meet exactly, and no edge is offset along its whole length. The only
    // sub-slack steps are the feet of ramp side walls (a wall growing from 0 m), which is what the slack lets through.
    [Theory]
    [MemberData(nameof(Seeds))]
    public void EdgeSteps_OnGeneratedMaps_RampToRampEdgesMeetExactly_SmallStepsAreOnlySideWallFeet(ulong seed)
    {
        Heightmap map = Rts.Sim.Tests.ViewApi.TerrainHeightTests.GeneratedMap(seed);
        const float cs = MapConstants.CellSize;
        int rampRamp = 0, sideFeet = 0, offsetEdges = 0;
        float worstRampRamp = 0f;
        for (int cy = 0; cy < map.Height; cy++)
        for (int cx = 0; cx < map.Width; cx++)
        for (int dir = 0; dir < 2; dir++)
        {
            int nx = cx + (dir == 0 ? 1 : 0), ny = cy + (dir == 1 ? 1 : 0);
            if (nx >= map.Width || ny >= map.Height) continue;
            bool ra = map.IsRamp(cx, cy), rb = map.IsRamp(nx, ny);
            if (!ra && !rb) continue;
            float minStep = float.MaxValue;
            for (int s = 0; s <= 16; s++)
            {
                float t = s / 16f;
                float ex = dir == 0 ? nx * cs : (cx + t) * cs, ey = dir == 1 ? ny * cs : (cy + t) * cs;
                float step = MathF.Abs(TerrainHeight.InCell(map, cx, cy, ex, ey) - TerrainHeight.InCell(map, nx, ny, ex, ey));
                minStep = MathF.Min(minStep, step);
                if (ra && rb && step > 1e-4f) { rampRamp++; worstRampRamp = MathF.Max(worstRampRamp, step); }
                if (!(ra && rb) && step > 1e-4f && step <= 0.15f) sideFeet++;
            }
            if (minStep > 1e-4f && minStep <= 0.15f) offsetEdges++;
        }
        _out.WriteLine($"seed {seed}: ramp-ramp edge steps {rampRamp} (worst {worstRampRamp:F3} m); edges offset along their whole length by <= 0.15 m: {offsetEdges}; side-wall-foot samples <= 0.15 m: {sideFeet}");
        Assert.Equal(0, rampRamp);
        Assert.Equal(0, offsetEdges);
    }

    // NaN, infinities, far off the map, a radius larger than the map, a denormal radius: finite, at least the centre, returns.
    [Fact]
    public void MaxUnder_HostileInputs_StayFiniteAndTerminate()
    {
        Heightmap map = Rts.Sim.Tests.ViewApi.TerrainHeightTests.GeneratedMap(5);
        float w = map.Width * MapConstants.CellSize, h = map.Height * MapConstants.CellSize;
        float[] coords = { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1e9f, -0.001f, 0f, w, w + 0.001f, 1e9f, w / 2, h / 3, float.Epsilon };
        float[] radii = { float.NaN, -1f, 0f, float.Epsilon, 1e-6f, 0.48f, 1.08f, 50f, 1e6f, float.PositiveInfinity, float.MaxValue };
        foreach (float x in coords)
        foreach (float y in coords)
        foreach (float r in radii)
        {
            float got = TerrainHeight.MaxUnder(map, x, y, r);
            Assert.True(float.IsFinite(got), $"MaxUnder({x}, {y}, {r}) = {got}");
            Assert.True(got >= TerrainHeight.At(map, x, y) - 1e-4f, $"MaxUnder({x}, {y}, {r}) = {got} below the centre {TerrainHeight.At(map, x, y)}");
            Assert.True(got <= MapConstants.LevelHeight * 4f, $"MaxUnder({x}, {y}, {r}) = {got} above any level");
        }
    }

    // The slack on a one-cell ramp (the hand maps', one level over 2 m): low ground beside the ramp's side wall, right at
    // the ramp's foot corner, where the wall is under 0.15 m high. Reports how high a disc there is lifted (BUG-0226).
    [Fact]
    public void Slack_AtASteepRampsFootCorner_NeverLiftsTheDiscHalfALevel()
    {
        // 3 x 2: row 0 = level 0 | ramp rising east | level 1; row 1 = level 0 throughout. Ramp (1,0)'s south edge is a side
        // wall from 0 m (west) to 4 m (east) over level-0 cell (1,1).
        const float hi = MapConstants.LevelHeight, cs = MapConstants.CellSize;
        byte[] levels = { 0, 0, 1, 0, 0, 0 };
        float[] elev = { 0f, hi / 2, hi, 0f, 0f, 0f };
        var map = new Heightmap(3, 2, levels, elev);
        Assert.True(map.IsRamp(1, 0));
        float worst = 0f, worstRef = 0f;
        string at = "";
        for (int i = 0; i <= 40; i++)
        for (int j = 0; j <= 10; j++)
        {
            // Just south of the side wall, from the ramp's west foot corner eastwards.
            float x = cs + i * 0.01f, y = cs + j * 0.01f;
            foreach (float r in new[] { 0.48f, 0.84f, 1.08f })
            {
                float lift = TerrainHeight.MaxUnder(map, x, y, r) - TerrainHeight.At(map, x, y);
                float refLift = FineReference(map, x, y, r, 512) - TerrainHeight.At(map, x, y);
                if (lift > worst) { worst = lift; worstRef = refLift; at = $"({x:F2}, {y:F2}) r {r}"; }
            }
        }
        _out.WriteLine($"steep ramp foot corner: worst lift {worst:F3} m at {at} (fine wall-aware reference there {worstRef:F3} m)");
        // One-cell ramps exist only on hand-made test maps (generated ramps rise a level over 4 cells); today 1.67 m
        // (BUG-0226 note). Guard: never half a level.
        Assert.True(worst < MapConstants.LevelHeight / 2f, $"lift {worst:F3} m at {at}, reference {worstRef:F3}");
    }

    /// <summary>Runs alone (allocation measurement).</summary>
    [Collection(SerialCollection.Name)]
    public class Serial
    {
        // 0 bytes per call (a corpse disc per dead unit is placed every frame).
        [Fact]
        public void MaxUnder_AllocatesNothing_OnEdgeCells()
        {
            Heightmap map = Rts.Sim.Tests.ViewApi.TerrainHeightTests.GeneratedMap(17);
            const float cs = MapConstants.CellSize;
            float sink = 0f;
            for (int i = 0; i < 1000; i++) sink += TerrainHeight.MaxUnder(map, (i % 120 + 0.37f) * cs, (i / 120 * 13 % 120 + 0.61f) * cs, 1.08f);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int cy = 0; cy < map.Height; cy++)
            for (int cx = 0; cx < map.Width; cx++)
            {
                if (!NearAnEdge(map, cx, cy)) continue;
                sink += TerrainHeight.MaxUnder(map, (cx + 0.13f) * cs, (cy + 0.87f) * cs, 1.08f);
                sink += TerrainHeight.MaxUnder(map, (cx + 0.5f) * cs, (cy + 0.5f) * cs, 0.48f);
                sink += TerrainHeight.MaxUnder(map, (cx + 0.99f) * cs, (cy + 0.01f) * cs, 25f);
            }
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(float.IsFinite(sink));
            Assert.Equal(0L, bytes);
        }
    }
}
