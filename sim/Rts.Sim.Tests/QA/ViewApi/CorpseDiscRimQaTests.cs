using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>
/// QA (M4-V3 fix round 1, BUG-0223 against BUG-0190 item 1): <see cref="TerrainHeight.MaxUnder"/>'s rim rule checked
/// against a ground-truth reference on generated maps. The reference walks the straight segment from the disc centre to
/// each rim sample in small steps and counts the rim sample only if the walk never crosses a wall (a height jump no
/// slope could make in one step). The disc must sit at or above every rim point reachable without a wall (it does not sink
/// into a slope: BUG-0190), and above that only by what a wall the rule cannot see can add (BUG-0223).
/// </summary>
public class CorpseDiscRimQaTests
{
    private readonly ITestOutputHelper _out;

    public CorpseDiscRimQaTests(ITestOutputHelper output) => _out = output;

    private const float Diag = 0.70710678f;
    private const int WalkSteps = 64;

    // Rim radii: the smallest, middle and largest unit radius (0.4, 0.7, 0.9 m in data) x CombatViews.CorpseRimScale 1.2.
    private static readonly float[] Radii = { 0.48f, 0.84f, 1.08f };

    // The highest ground among the centre and the eight rim samples MaxUnder uses, counting a rim sample only if the walk
    // to it never jumps more than a slope could rise in one step (plus slack): i.e. no cliff wall in between.
    internal static float Reference(Heightmap map, float x, float y, float r)
    {
        float centre = TerrainHeight.At(map, x, y);
        float best = centre;
        for (int k = 0; k < 8; k++)
        {
            float dx = k switch { 0 => 1f, 1 => -1f, 2 => 0f, 3 => 0f, 4 => Diag, 5 => Diag, 6 => -Diag, _ => -Diag } * r;
            float dy = k switch { 0 => 0f, 1 => 0f, 2 => 1f, 3 => -1f, 4 => Diag, 5 => -Diag, 6 => Diag, _ => -Diag } * r;
            float prev = centre;
            bool wall = false;
            float stepLen = r / WalkSteps;
            float maxStep = stepLen * (MapConstants.LevelHeight / MapConstants.CellSize) * 2f + 0.05f;
            for (int s = 1; s <= WalkSteps; s++)
            {
                float h = TerrainHeight.At(map, x + dx * s / WalkSteps, y + dy * s / WalkSteps);
                if (MathF.Abs(h - prev) > maxStep) { wall = true; break; }
                prev = h;
            }
            if (!wall) best = MathF.Max(best, prev);
        }
        return best;
    }

    public static IEnumerable<object[]> Seeds()
    {
        foreach (ulong s in new ulong[] { 1, 5, 17, 23, 42 }) yield return new object[] { s };
    }

    private sealed class Stats
    {
        public int Points, Sunk, OverOne, RampCentres, RampBesideHigher;
        public float WorstSink, WorstHang, WorstHangRamp, WorstHangPlateau, WorstReachableLift;
        public string WorstHangAt = "";
    }

    // Every cell next to a ramp or a cliff, a 6 x 6 grid of points per cell, three rim radii.
    private Stats Probe(ulong seed)
    {
        Heightmap map = Rts.Sim.Tests.ViewApi.TerrainHeightTests.GeneratedMap(seed);
        const float cs = MapConstants.CellSize;
        var st = new Stats();
        for (int cy = 0; cy < map.Height; cy++)
        for (int cx = 0; cx < map.Width; cx++)
        {
            if (!NearAnEdge(map, cx, cy)) continue;
            bool ramp = map.IsRamp(cx, cy);
            bool besideHigher = ramp && HasHigherPlateauNeighbour(map, cx, cy);
            for (int j = 0; j < 6; j++)
            for (int i = 0; i < 6; i++)
            {
                float x = (cx + (i + 0.5f) / 6f) * cs, y = (cy + (j + 0.5f) / 6f) * cs;
                foreach (float r in Radii)
                {
                    float got = TerrainHeight.MaxUnder(map, x, y, r);
                    float at = TerrainHeight.At(map, x, y);
                    float want = Reference(map, x, y, r);
                    Assert.True(float.IsFinite(got), $"NaN/Inf at ({x}, {y})");
                    Assert.True(got >= at - 1e-4f, $"below its own centre at ({x}, {y})");
                    st.Points++;
                    if (ramp) st.RampCentres++;
                    if (besideHigher) st.RampBesideHigher++;
                    float sink = want - got;
                    if (sink > 1e-3f) { st.Sunk++; st.WorstSink = MathF.Max(st.WorstSink, sink); }
                    float hang = got - want;
                    if (hang > 1f) st.OverOne++;
                    if (hang > st.WorstHang)
                    {
                        st.WorstHang = hang;
                        st.WorstHangAt = $"({x:R}, {y:R}) r {r} cell ({cx},{cy}) {(ramp ? "ramp" : "plateau")} e{map.ElevationAt(cx, cy):F1}";
                    }
                    if (ramp) st.WorstHangRamp = MathF.Max(st.WorstHangRamp, hang);
                    else
                    {
                        st.WorstHangPlateau = MathF.Max(st.WorstHangPlateau, hang);
                        st.WorstReachableLift = MathF.Max(st.WorstReachableLift, want - at);
                    }
                }
            }
        }
        _out.WriteLine($"seed {seed}: {st.Points} points ({st.RampCentres} on ramps, {st.RampBesideHigher} on ramps beside a higher plateau)");
        _out.WriteLine($"  sunk below the wall-free rim: {st.Sunk} (worst {st.WorstSink:F3} m)");
        _out.WriteLine($"  hang over the wall-free reference: worst {st.WorstHang:F3} m at {st.WorstHangAt}; ramp centres {st.WorstHangRamp:F3}, plateau centres {st.WorstHangPlateau:F3}; {st.OverOne} over 1 m");
        _out.WriteLine($"  plateau centres (ramp feet): worst wall-free lift {st.WorstReachableLift:F3} m");
        return st;
    }

    // BUG-0190 item 1 kept: a disc never sinks into slope reachable from its centre; on a ramp (also beside a plateau one
    // level up) it never sits above the wall-free ground either.
    [Theory]
    [MemberData(nameof(Seeds))]
    public void MaxUnder_NeverSinksIntoReachableSlope_AndOnARampNeverHangs_OnGeneratedMaps(ulong seed)
    {
        Stats st = Probe(seed);
        Assert.True(st.RampCentres > 0 && st.RampBesideHigher > 0, "no ramp cells (beside a higher plateau) sampled");
        Assert.Equal(0, st.Sunk);
        Assert.True(st.WorstHangRamp < 1e-3f, $"a disc on a ramp hangs {st.WorstHangRamp:F3} m over the wall-free ground");
    }

    // BUG-0224: on low ground beside a ramp's side wall, a rim sample up on the ramp (within radius x one level per cell
    // of the centre) is counted, so the disc hangs up to 2.16 m (a 0.9 m unit) in the air beside the wall.
    [Theory(Skip = "BUG-0224: a corpse disc beside a ramp's side wall hangs up to 2.16 m above its own ground")]
    [MemberData(nameof(Seeds))]
    public void MaxUnder_BesideARampsSideWall_NeverHangsAboveTheWallFreeGround_OnGeneratedMaps(ulong seed)
    {
        Stats st = Probe(seed);
        Assert.True(st.WorstHang < 0.25f, $"disc hangs {st.WorstHang:F2} m above the wall-free ground at {st.WorstHangAt}");
    }

    // A unit on a ramp's low end beside a plateau one level up (the cliff the ramp is cut into): the plateau is never under the disc.
    [Fact]
    public void MaxUnder_OnARampsLowEnd_BesideAPlateauOneLevelUp_StaysOnTheRamp()
    {
        // 3 x 3: row 0 level-1 plateau (4 m); row 1: (0,1) level 0, (1,1) ramp rising east... built so the ramp's side
        // neighbour (1,0) is the plateau one level up, its west neighbour level 0 and its east neighbour the plateau.
        const float hi = MapConstants.LevelHeight, cs = MapConstants.CellSize;
        byte[] levels = { 1, 1, 1, 0, 0, 1, 0, 0, 0 };
        float[] elev = { hi, hi, hi, 0f, hi / 2, hi, 0f, 0f, 0f };
        var map = new Heightmap(3, 3, levels, elev);
        Assert.True(map.IsRamp(1, 1));
        // Low end of the ramp (west edge, ~0.2 m up), 0.3 m from the plateau cell (1, 0) to the north.
        float x = 1 * cs + 0.1f, y = 1 * cs + 0.3f;
        float at = TerrainHeight.At(map, x, y);
        Assert.True(at < 0.5f, $"ramp low end at {at}");
        foreach (float r in Radii)
        {
            float got = TerrainHeight.MaxUnder(map, x, y, r);
            float want = Reference(map, x, y, r);
            _out.WriteLine($"r {r}: centre {at:F3}, MaxUnder {got:F3}, wall-free reference {want:F3}");
            Assert.True(got < hi - 1f, $"r {r}: disc at {got} m, on the plateau one level up");
            Assert.True(got >= want - 1e-3f, $"r {r}: sinks into the ramp ({got} < {want})");
        }
        // High end of the same ramp: the plateau lip ahead is joined by the slope and counts.
        float xh = 2 * cs - 0.2f, yh = 1.5f * cs;
        Assert.True(TerrainHeight.MaxUnder(map, xh, yh, 0.84f) >= TerrainHeight.At(map, xh, yh) - 1e-4f);
        Assert.Equal(hi, TerrainHeight.MaxUnder(map, xh, yh, 0.84f), 3);
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

    private static bool HasHigherPlateauNeighbour(Heightmap map, int cx, int cy)
    {
        float e = map.ElevationAt(cx, cy);
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int nx = cx + dx, ny = cy + dy;
            if ((uint)nx >= (uint)map.Width || (uint)ny >= (uint)map.Height) continue;
            if (!map.IsRamp(nx, ny) && map.ElevationAt(nx, ny) > e) return true;
        }
        return false;
    }
}
