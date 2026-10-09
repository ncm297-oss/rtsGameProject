using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>
/// QA (M4-VH1, BUG-0226): <see cref="TerrainHeight.MaxUnder"/>'s three step bands (straddle up to ~5 cm, capped up to
/// 0.15 m, wall above) on seeds the fix was not measured on (the developer's rows use 1, 5, 17, 23, 42, 77, 1234). Points
/// only on ramp cells and their eight neighbours (ramp feet, side walls, lips; generated maps have three levels), random
/// in the cell, with the three real corpse rim radii (0.4 / 0.7 / 0.9 m unit radius x 1.2) and a random one. Against the
/// 512-step wall-aware reference (the oracle the fix borrowed its straddle from): never sinks, hangs under 0.25 m. Also
/// measures, without asserting, how high a disc whose centre is on flat ground rises over its own ground when a rim
/// line crosses a side wall under the straddle (the oracle counts those too, so the oracle row can't see them).
/// QA 2026-10-08-2144 ran 16 seeds once (also 2, 3, 64, 202, 303, 999, 4096, 65537: worst hang 0.204 m); the eight kept
/// are the worst (0.196-0.225 m). The worst strict lift (0.46 m, seed 404) is a centre 3 mm from a ramp's foot line, its
/// rim over the ramp: joined through the foot, the designed float of BUG-0190, not a climb.
/// </summary>
public class MaxUnderBandsQaTests
{
    private readonly ITestOutputHelper _out;

    public MaxUnderBandsQaTests(ITestOutputHelper output) => _out = output;

    private const float Diag = 0.70710678f;

    public static IEnumerable<object[]> Seeds()
    {
        foreach (ulong s in new ulong[] { 9, 31, 99, 101, 404, 555, 777, 2024 }) yield return new object[] { s };
    }

    // The MaxUnderEdgeWalkQaTests reference: a jump bigger than twice the steepest slope's rise over one step plus `flat`
    // is a wall. flat = 0.05 is that row's oracle; flat = 1e-3 calls every real step a wall (strict "own ground").
    private static float Reference(Heightmap map, float x, float y, float r, int steps, float flat)
    {
        float centre = TerrainHeight.At(map, x, y);
        float best = centre;
        for (int k = 0; k < 8; k++)
        {
            float dx = k switch { 0 => 1f, 1 => -1f, 2 => 0f, 3 => 0f, 4 => Diag, 5 => Diag, 6 => -Diag, _ => -Diag } * r;
            float dy = k switch { 0 => 0f, 1 => 0f, 2 => 1f, 3 => -1f, 4 => Diag, 5 => -Diag, 6 => Diag, _ => -Diag } * r;
            float prev = centre, stepLen = r / steps;
            float maxStep = stepLen * (MapConstants.LevelHeight / MapConstants.CellSize) * 2f + flat;
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

    private static bool NearARamp(Heightmap map, int cx, int cy)
    {
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int nx = cx + dx, ny = cy + dy;
            if ((uint)nx < (uint)map.Width && (uint)ny < (uint)map.Height && map.IsRamp(nx, ny)) return true;
        }
        return false;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void MaxUnder_NearRamps_OnUnseenSeeds_NeverSinks_HangsUnderAQuarterMetre(ulong seed)
    {
        Heightmap map = Rts.Sim.Tests.ViewApi.TerrainHeightTests.GeneratedMap(seed);
        var rnd = new Random((int)(seed % int.MaxValue) * 31 + 7);
        const float cs = MapConstants.CellSize;
        float[] radii = { 0.48f, 0.84f, 1.08f, 0f };
        int points = 0, sunk = 0, levels = 0, flatCentres = 0, strictOverTenth = 0;
        float worstSink = 0f, worstHang = 0f, worstStrictLift = 0f;
        string hangAt = "", sinkAt = "", strictAt = "";
        var seen = new bool[8];
        for (int cy = 0; cy < map.Height; cy++)
        for (int cx = 0; cx < map.Width; cx++)
        {
            int lv = (int)MathF.Round(map.ElevationAt(cx, cy) / MapConstants.LevelHeight);
            if ((uint)lv < 8 && !seen[lv]) { seen[lv] = true; levels++; }
            if (!NearARamp(map, cx, cy)) continue;
            for (int k = 0; k < 6; k++)
            {
                float x = (cx + (float)rnd.NextDouble()) * cs, y = (cy + (float)rnd.NextDouble()) * cs;
                float r = radii[k % 4];
                if (r == 0f) r = 0.48f + 0.6f * (float)rnd.NextDouble();
                float got = TerrainHeight.MaxUnder(map, x, y, r);
                float want = Reference(map, x, y, r, 512, 0.05f);
                points++;
                Assert.True(float.IsFinite(got), $"({x}, {y}) r {r}: {got}");
                if (want - got > 1e-3f)
                {
                    sunk++;
                    if (want - got > worstSink) { worstSink = want - got; sinkAt = $"({x:R}, {y:R}) r {r:R} cell ({cx},{cy}) ramp {map.IsRamp(cx, cy)}"; }
                }
                if (got - want > worstHang) { worstHang = got - want; hangAt = $"({x:R}, {y:R}) r {r:R} cell ({cx},{cy}) ramp {map.IsRamp(cx, cy)} e{map.ElevationAt(cx, cy):F1}"; }
                if (!map.IsRamp(cx, cy))
                {
                    // A flat centre: how far over the strict "every step is a wall" ground the disc is drawn.
                    flatCentres++;
                    float strict = Reference(map, x, y, r, 512, 1e-3f);
                    float lift = got - strict;
                    if (lift > 0.1f) strictOverTenth++;
                    if (lift > worstStrictLift) { worstStrictLift = lift; strictAt = $"({x:R}, {y:R}) r {r:R} cell ({cx},{cy}) e{map.ElevationAt(cx, cy):F1}"; }
                }
            }
        }
        _out.WriteLine($"seed {seed}: {levels} levels, {points} points near ramps; sunk {sunk} (worst {worstSink:F3} m {sinkAt}); worst hang over the 0.05 m oracle {worstHang:F3} m at {hangAt}");
        _out.WriteLine($"  flat centres {flatCentres}: worst lift over the strict own-ground walk {worstStrictLift:F3} m at {strictAt}; {strictOverTenth} over 0.1 m");
        Assert.True(levels >= 3, $"seed {seed}: only {levels} levels");
        Assert.True(sunk == 0, $"seed {seed}: {sunk} points sink below the wall-free ground, worst {worstSink:F3} m at {sinkAt}");
        Assert.True(worstHang < 0.25f, $"seed {seed}: hang {worstHang:F3} m at {hangAt}");
    }
}
