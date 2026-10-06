using System.Numerics;
using Rts.Sim.Determinism;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>Minimap pixel/meter mapping: round trips, letterboxing, rejection of outside and non-finite pixels, view-corner rays.</summary>
public class MinimapTransformTests
{
    private static readonly Vector2 Control = new(220, 220);

    [Theory]
    [InlineData(256f, 256f)]
    [InlineData(320f, 96f)]
    [InlineData(96f, 320f)]
    public void PixelToMapToPixel_RoundTripsWithinHalfAPixel(float mapW, float mapH)
    {
        var t = new MinimapTransform(Control, new Vector2(mapW, mapH));
        var points = new List<Vector2> { new(0, 0), new(mapW, 0), new(mapW, mapH), new(0, mapH), new(mapW / 2, mapH / 2) };
        var rng = new SimRng(99, RngStream.Combat);
        for (int i = 0; i < 100; i++) points.Add(new Vector2(rng.NextFloat() * mapW, rng.NextFloat() * mapH));
        foreach (Vector2 m in points)
        {
            Vector2 px = t.ToPixel(m);
            Assert.True(t.TryToMap(px, out Vector2 back), $"{m} -> {px} rejected");
            Assert.True(Vector2.Distance(t.ToPixel(back), px) <= 0.5f, $"{m}: pixel {px} -> {back}");
            Assert.True(Vector2.Distance(back, m) * t.Scale <= 0.5f, $"{m} came back as {back}");
        }
    }

    [Fact]
    public void WideMap_IsLetterboxedTopAndBottom()
    {
        // 160 x 48 cells = 320 x 96 m: full width, 66 px tall, centred vertically.
        var t = new MinimapTransform(Control, new Vector2(320, 96));
        Assert.Equal(220f / 320f, t.Scale, 5);
        (Vector2 pos, Vector2 size) = t.MapRect;
        Near(new Vector2(0, 77), pos);
        Near(new Vector2(220, 66), size);
        Assert.False(t.TryToMap(new Vector2(110, 76), out _), "above the map is letterbox");
        Assert.False(t.TryToMap(new Vector2(110, 144), out _), "below the map is letterbox");
        Assert.True(t.TryToMap(new Vector2(110, 110), out Vector2 c));
        Near(new Vector2(160, 48), c);
    }

    [Fact]
    public void TallMap_IsLetterboxedLeftAndRight()
    {
        var t = new MinimapTransform(Control, new Vector2(96, 320));
        (Vector2 pos, Vector2 size) = t.MapRect;
        Near(new Vector2(77, 0), pos);
        Near(new Vector2(66, 220), size);
        Assert.False(t.TryToMap(new Vector2(76, 110), out _));
        Assert.False(t.TryToMap(new Vector2(144, 110), out _));
        Assert.True(t.TryToMap(new Vector2(77, 0), out Vector2 corner));
        Near(Vector2.Zero, corner);
    }

    [Theory]
    [InlineData(-1f, 10f)]
    [InlineData(10f, -0.01f)]
    [InlineData(220.5f, 10f)]
    [InlineData(10f, 221f)]
    [InlineData(float.NaN, 10f)]
    [InlineData(10f, float.NaN)]
    [InlineData(float.PositiveInfinity, 10f)]
    [InlineData(10f, float.NegativeInfinity)]
    public void OutsideOrNonFinitePixels_AreRejected(float x, float y)
    {
        var t = new MinimapTransform(Control, new Vector2(256, 256));
        Assert.False(t.TryToMap(new Vector2(x, y), out _));
    }

    [Fact]
    public void ZeroSizedControl_RejectsEverything()
    {
        var t = new MinimapTransform(Vector2.Zero, new Vector2(256, 256));
        Assert.False(t.TryToMap(new Vector2(0, 0), out _));
        Assert.False(t.TryToMap(new Vector2(5, 5), out _));
    }

    [Fact]
    public void ClampToMap_PinsPointsOntoTheMap()
    {
        var t = new MinimapTransform(Control, new Vector2(256, 128));
        Near(new Vector2(0, 128), t.ClampToMap(new Vector2(-40, 900)));
        Near(new Vector2(10, 20), t.ClampToMap(new Vector2(10, 20)));
    }

    [Fact]
    public void RayToGround_HitsThePlane_OrStopsAt200Meters()
    {
        // 45 degrees down from 10 m above the plane at y = 2: lands 8 m ahead.
        Vector2 hit = MinimapTransform.RayToGround(new Vector3(5, 10, 50), new Vector3(0, -1, -1), 2f);
        Near(new Vector2(5, 42), hit);
        // Pointing up: the point 200 m along the ray.
        Vector2 up = MinimapTransform.RayToGround(new Vector3(0, 10, 0), new Vector3(1, 1, 0), 0f);
        Near(new Vector2(MinimapTransform.MaxRayMeters / MathF.Sqrt(2), 0), up);
        // A grazing ray that lands 1 km away is cut at 200 m too.
        Vector2 far = MinimapTransform.RayToGround(new Vector3(0, 10, 0), new Vector3(0, -0.01f, -1), 0f);
        Assert.True(far.Length() <= MinimapTransform.MaxRayMeters + 1e-3f, $"{far}");
    }

    private static void Near(Vector2 want, Vector2 got) =>
        Assert.True(Vector2.Distance(want, got) < 1e-3f, $"want {want}, got {got}");
}
