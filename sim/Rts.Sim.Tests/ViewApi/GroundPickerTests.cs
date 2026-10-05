using System.Numerics;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>Right-click ground picking against a brute-force ray march on hand-made and generated maps.</summary>
public class GroundPickerTests
{
    private const float Cs = MapConstants.CellSize;
    private const float MaxError = 0.1f;
    private readonly ITestOutputHelper _out;

    public GroundPickerTests(ITestOutputHelper output) => _out = output;

    // Where RtsCamera sits for a focus point and zoom: zoom m up, zoom / tan 55° behind on +Z.
    private static Vector3 CameraFor(float fx, float fz, float zoom) =>
        new(fx, zoom, fz + zoom / (float)Math.Tan(CameraLimits.PitchDegrees * Math.PI / 180));

    // Independent oracle: march the ray in 5 mm steps until it is inside the map at or below
    // TerrainHeight.At, then bisect the last step.
    private static bool Oracle(Heightmap map, Vector3 o, Vector3 d, out Vector3 hit)
    {
        d = Vector3.Normalize(d);
        const float step = 0.005f;
        bool Below(float t)
        {
            Vector3 p = o + d * t;
            return p.X >= 0 && p.Z >= 0 && p.X <= map.Width * Cs && p.Z <= map.Height * Cs
                && p.Y <= TerrainHeight.At(map, p.X, p.Z);
        }
        for (float t = 0; t < 600f; t += step)
        {
            if (!Below(t)) continue;
            float lo = MathF.Max(0, t - step), hi = t;
            for (int i = 0; i < 30; i++)
            {
                float mid = (lo + hi) / 2;
                if (Below(mid)) hi = mid; else lo = mid;
            }
            hit = o + d * hi;
            return true;
        }
        hit = default;
        return false;
    }

    private static void AssertMatchesOracle(Heightmap map, Vector3 origin, Vector3 dir, string what, ref float worst)
    {
        bool expected = Oracle(map, origin, dir, out Vector3 want);
        bool got = GroundPicker.TryPick(map, origin, dir, out Vector3 hit);
        Assert.True(expected == got, $"{what}: oracle hit {expected}, picker hit {got}");
        if (!got) return;
        float err = Vector3.Distance(want, hit);
        // A ray that exactly grazes a lip (the camera geometry makes some rays pass through a ramp's
        // top edge at y = 4.000) touches the surface there; float noise decides which side either
        // method lands. Accept the picker's earlier hit when it really is on the surface (1 cm, the
        // 1 mm cell inset times the steepest slope plus noise).
        bool touch = Vector3.Distance(origin, hit) < Vector3.Distance(origin, want)
            && MathF.Abs(hit.Y - TerrainHeight.At(map, hit.X, hit.Z)) <= 0.01f;
        if (touch) return;
        worst = MathF.Max(worst, err);
        Assert.True(err <= MaxError, $"{what}: picked {hit}, true intersection {want} ({err:F3} m off)");
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    [InlineData(5UL)]
    public void GeneratedMap_RampsPlateausAndHighGround_AtBothZoomLimits_MatchTheRayMarch(ulong seed)
    {
        Heightmap map = TerrainHeightTests.GeneratedMap(seed);
        int ramps = 0, high = 0, low = 0;
        float worst = 0f;
        for (int cy = 1; cy < map.Height - 1; cy++)
        {
            for (int cx = 1; cx < map.Width - 1; cx++)
            {
                bool ramp = map.IsRamp(cx, cy);
                int level = map.LevelAt(cx, cy);
                // Every 3rd ramp cell, a 3-cell lattice on high ground, a sparse 9-cell lattice elsewhere.
                int every = ramp || level == MapConstants.MaxLevel ? 3 : 9;
                if (ramp ? (cx + cy) % 3 != 0 : (cx % every != 0 || cy % every != 0)) continue;
                if (ramp) ramps++; else if (level == MapConstants.MaxLevel) high++; else low++;
                float px = (cx + 0.3f) * Cs, pz = (cy + 0.6f) * Cs;
                var target = new Vector3(px, TerrainHeight.At(map, px, pz), pz);
                foreach (float zoom in new[] { CameraLimits.MinZoom, CameraLimits.MaxZoom })
                {
                    // Aimed through the focus, and from a camera focused 12 m to the side (off-centre pixel).
                    foreach (float off in new[] { 0f, 12f })
                    {
                        Vector3 cam = CameraFor(px + off, pz - off, zoom);
                        AssertMatchesOracle(map, cam, target - cam, $"seed {seed} cell ({cx}, {cy}) zoom {zoom} offset {off}", ref worst);
                    }
                }
            }
        }
        _out.WriteLine($"seed {seed}: {ramps} ramp, {high} high-ground, {low} other targets; worst error {worst * 1000:F2} mm");
        Assert.True(ramps >= 10 && high >= 5 && low >= 20, $"too few targets: {ramps} ramp, {high} high, {low} low");
    }

    [Fact]
    public void GeneratedMap_GrazingRays_MatchTheRayMarch()
    {
        Heightmap map = TerrainHeightTests.GeneratedMap(7);
        float worst = 0f;
        for (int i = 0; i < 40; i++)
        {
            // Nearly horizontal rays (1-4 degrees down) from just above the top level, in many headings.
            float heading = i * 0.157f, down = 0.02f + 0.0015f * i;
            var origin = new Vector3(128f + 20f * MathF.Cos(heading * 3), 9.5f, 128f + 20f * MathF.Sin(heading * 3));
            var dir = new Vector3(MathF.Cos(heading), -down, MathF.Sin(heading));
            AssertMatchesOracle(map, origin, dir, $"grazing ray {i}", ref worst);
        }
        _out.WriteLine($"grazing: worst error {worst * 1000:F2} mm");
    }

    // 8 x 8: rows 0-3 a level-1 plateau, rows 4-7 level-0 ground; the cliff faces +Z (toward the camera).
    private static Heightmap CliffMap()
    {
        var levels = new byte[64];
        var elev = new float[64];
        for (int i = 0; i < 32; i++)
        {
            levels[i] = 1;
            elev[i] = MapConstants.LevelHeight;
        }
        return new Heightmap(8, 8, levels, elev);
    }

    [Fact]
    public void RayThroughACliffWall_HitsTheUpperPlateauCell_NotTheLowGroundBehind()
    {
        Heightmap map = CliffMap();
        // Level ray 2 m up, flying north (-Z) over the low ground into the cliff face at z = 8.
        Assert.True(GroundPicker.TryPick(map, new Vector3(5f, 2f, 40f), new Vector3(0f, 0f, -1f), out Vector3 hit));
        Assert.Equal(8f, hit.Z, 2);
        Assert.Equal(2f, hit.Y, 3);
        Assert.Equal(3, (int)(hit.Z / Cs)); // plateau row, not the low row 4
        float worst = 0f;
        AssertMatchesOracle(map, new Vector3(5f, 2f, 40f), new Vector3(0f, 0f, -1f), "level ray into the cliff", ref worst);

        // The RtsCamera view at max zoom, aimed just below the lip: lands on the wall or the plateau, never in front.
        Vector3 cam = CameraFor(5f, 9f, CameraLimits.MaxZoom);
        Assert.True(GroundPicker.TryPick(map, cam, new Vector3(5f, 3.9f, 8.05f) - cam, out hit));
        Assert.True(hit.Z < 8f, $"picked {hit}: in front of the cliff");
    }

    [Fact]
    public void HandMap_RampAndPlateau_MatchTheRayMarch()
    {
        Heightmap map = TerrainHeightTests.HandMap();
        float worst = 0f;
        for (float x = 0.25f; x < 8f; x += 0.5f)
        {
            for (float z = 0.25f; z < 8f; z += 0.5f)
            {
                foreach (float zoom in new[] { CameraLimits.MinZoom, CameraLimits.MaxZoom })
                {
                    Vector3 cam = CameraFor(4f, 4f, zoom);
                    var target = new Vector3(x, TerrainHeight.At(map, x, z), z);
                    AssertMatchesOracle(map, cam, target - cam, $"hand map ({x}, {z}) zoom {zoom}", ref worst);
                }
            }
        }
    }

    [Fact]
    public void RaysThatMissTheMap_ReturnFalse()
    {
        Heightmap map = TerrainHeightTests.GeneratedMap(1);
        float size = map.Width * Cs;
        Vector3 cam = CameraFor(0f, 0f, CameraLimits.MaxZoom);
        Assert.False(GroundPicker.TryPick(map, cam, new Vector3(0f, 1f, -1f), out _));            // into the sky
        Assert.False(GroundPicker.TryPick(map, cam, new Vector3(-1f, -1f, -1f), out _));          // past the north-west corner
        Assert.False(GroundPicker.TryPick(map, new Vector3(-50f, 20f, 50f), new Vector3(-1f, -0.2f, 0f), out _)); // away from the map
        Assert.False(GroundPicker.TryPick(map, new Vector3(size + 5f, 20f, 50f), new Vector3(0f, -1f, 0f), out _)); // straight down beside it
        Assert.False(GroundPicker.TryPick(map, cam, Vector3.Zero, out _));
        Assert.False(GroundPicker.TryPick(map, cam, new Vector3(float.NaN, -1f, 0f), out _));
        Assert.False(GroundPicker.TryPick(map, new Vector3(float.PositiveInfinity, 20f, 50f), new Vector3(0f, -1f, 0f), out _));
    }

    [Fact]
    public void StraightDown_HitsTheSurfaceUnderneath()
    {
        Heightmap map = TerrainHeightTests.HandMap();
        Assert.True(GroundPicker.TryPick(map, new Vector3(5.5f, 30f, 3.1f), new Vector3(0f, -1f, 0f), out Vector3 hit));
        Assert.Equal(TerrainHeight.At(map, 5.5f, 3.1f), hit.Y, 4);
        Assert.Equal(5.5f, hit.X, 4);
        Assert.Equal(3.1f, hit.Z, 4);
    }
}
