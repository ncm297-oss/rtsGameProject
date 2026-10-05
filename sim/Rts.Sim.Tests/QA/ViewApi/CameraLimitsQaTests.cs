using Rts.Sim.Determinism;
using System.Numerics;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>QA (M2-1): camera clamps at all four map edges, both zoom limits, and junk input.</summary>
public class CameraLimitsQaTests
{
    private const int W = 128, H = 128;
    private const float MaxX = W * MapConstants.CellSize, MaxY = H * MapConstants.CellSize;

    [Fact]
    public void Constants_MatchBrief()
    {
        Assert.Equal(55f, CameraLimits.PitchDegrees);
        Assert.Equal(20f, CameraLimits.MinZoom);
        Assert.Equal(60f, CameraLimits.MaxZoom);
        Assert.Equal(40f, CameraLimits.DefaultZoom);
        Assert.Equal(8f, CameraLimits.EdgePanPixels);
        Assert.InRange(CameraLimits.DefaultZoom, CameraLimits.MinZoom, CameraLimits.MaxZoom);
    }

    [Theory]
    [InlineData(-1000f, 100f, 0f, 100f)]      // west
    [InlineData(1000f, 100f, MaxX, 100f)]     // east
    [InlineData(100f, -1000f, 100f, 0f)]      // north
    [InlineData(100f, 1000f, 100f, MaxY)]     // south
    [InlineData(-1f, -1f, 0f, 0f)]            // corners
    [InlineData(1e9f, 1e9f, MaxX, MaxY)]
    [InlineData(-1e9f, 1e9f, 0f, MaxY)]
    [InlineData(1e9f, -1e9f, MaxX, 0f)]
    [InlineData(0f, 0f, 0f, 0f)]              // exactly on the limits stays put
    [InlineData(MaxX, MaxY, MaxX, MaxY)]
    [InlineData(128.5f, 3f, 128.5f, 3f)]      // inside unchanged
    public void ClampFocus_AllEdgesAndCorners(float x, float y, float ex, float ey)
    {
        Vector2 r = CameraLimits.ClampFocus(new Vector2(x, y), W, H);
        Assert.Equal(new Vector2(ex, ey), r);
    }

    [Fact]
    public void ClampFocus_NonSquareMap_UsesEachAxisSize()
    {
        Vector2 r = CameraLimits.ClampFocus(new Vector2(1e6f, 1e6f), 64, 32);
        Assert.Equal(new Vector2(128f, 64f), r);
    }

    [Fact]
    public void ClampFocus_NonFinite_StaysOnTheMap()
    {
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Vector2 r = CameraLimits.ClampFocus(new Vector2(bad, bad), W, H);
            Assert.True(float.IsFinite(r.X) && float.IsFinite(r.Y), $"{bad} -> {r}");
            Assert.InRange(r.X, 0f, MaxX);
            Assert.InRange(r.Y, 0f, MaxY);
        }
        Assert.Equal(new Vector2(MaxX / 2, MaxY / 2), CameraLimits.ClampFocus(new Vector2(float.NaN, float.NaN), W, H));
    }

    [Theory]
    [InlineData(0f, 20f)]
    [InlineData(-100f, 20f)]
    [InlineData(19.999f, 20f)]
    [InlineData(20f, 20f)]
    [InlineData(40f, 40f)]
    [InlineData(60f, 60f)]
    [InlineData(60.001f, 60f)]
    [InlineData(1e30f, 60f)]
    [InlineData(float.PositiveInfinity, 60f)]
    [InlineData(float.NegativeInfinity, 20f)]
    [InlineData(float.NaN, 40f)]
    public void ClampZoom_BothLimits(float zoom, float expected)
    {
        Assert.Equal(expected, CameraLimits.ClampZoom(zoom));
    }

    [Fact]
    public void WheelNotches_FromDefault_ReachLimitsAndStop()
    {
        float z = CameraLimits.DefaultZoom;
        for (int i = 0; i < 50; i++) z = CameraLimits.ClampZoom(z - CameraLimits.ZoomStep);
        Assert.Equal(CameraLimits.MinZoom, z);
        for (int i = 0; i < 50; i++) z = CameraLimits.ClampZoom(z + CameraLimits.ZoomStep);
        Assert.Equal(CameraLimits.MaxZoom, z);
    }

    [Fact]
    public void PanSpeed_ScalesWithZoom_AndIsClamped()
    {
        Assert.True(CameraLimits.PanSpeed(60f) > CameraLimits.PanSpeed(20f));
        Assert.Equal(CameraLimits.PanSpeed(20f), CameraLimits.PanSpeed(-5f));
        Assert.Equal(CameraLimits.PanSpeed(60f), CameraLimits.PanSpeed(1e9f));
        Assert.True(float.IsFinite(CameraLimits.PanSpeed(float.NaN)) && CameraLimits.PanSpeed(float.NaN) > 0f);
    }

    [Fact]
    public void Fuzz_RandomPans_NeverLeaveTheMap()
    {
        var rng = new SimRng(9, 1);
        var focus = new Vector2(MaxX / 2, MaxY / 2);
        float zoom = CameraLimits.DefaultZoom;
        for (int i = 0; i < 100_000; i++)
        {
            zoom = CameraLimits.ClampZoom(zoom + (rng.NextFloat() - 0.5f) * 20f);
            float step = CameraLimits.PanSpeed(zoom) * rng.NextFloat() * 0.5f;
            var d = new Vector2(rng.NextFloat() * 2 - 1, rng.NextFloat() * 2 - 1) * step * (rng.NextInt(0, 50) == 0 ? 1000f : 1f);
            focus = CameraLimits.ClampFocus(focus + d, W, H);
            Assert.InRange(focus.X, 0f, MaxX);
            Assert.InRange(focus.Y, 0f, MaxY);
            Assert.InRange(zoom, CameraLimits.MinZoom, CameraLimits.MaxZoom);
        }
    }
}
