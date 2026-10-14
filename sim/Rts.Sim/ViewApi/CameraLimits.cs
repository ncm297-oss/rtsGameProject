using System;
using System.Numerics;
using Rts.Sim.Map;

namespace Rts.Sim.ViewApi;

/// <summary>RTS camera limits from docs/02 "Controls and camera", kept pure so they can be unit-tested.</summary>
/// <remarks>Zoom is the camera's height in meters above its ground focus point.</remarks>
public static class CameraLimits
{
    /// <summary>Fixed camera pitch below the horizon in degrees.</summary>
    public const float PitchDegrees = 55f;

    /// <summary>Closest zoom in meters.</summary>
    public const float MinZoom = 20f;

    /// <summary>Farthest zoom in meters.</summary>
    public const float MaxZoom = 60f;

    /// <summary>Zoom at match start in meters.</summary>
    public const float DefaultZoom = 40f;

    /// <summary>Zoom change per mouse-wheel notch in meters.</summary>
    public const float ZoomStep = 4f;

    /// <summary>Width of the screen-edge band that pans the camera, in pixels.</summary>
    public const float EdgePanPixels = 8f;

    /// <summary>Pan speed in meters per second per meter of zoom, so a pan covers the same share of the screen at any zoom.</summary>
    public const float PanSpeedPerZoom = 1f;

    /// <summary>Clamps a zoom to [<see cref="MinZoom"/>, <see cref="MaxZoom"/>]; NaN becomes <see cref="DefaultZoom"/>.</summary>
    public static float ClampZoom(float zoom) => float.IsNaN(zoom) ? DefaultZoom : Math.Clamp(zoom, MinZoom, MaxZoom);

    /// <summary>Clamps a ground focus point (sim x, y in meters) to the map, <c>[0, width x CellSize] x [0, height x CellSize]</c>; NaN goes to the map center.</summary>
    public static Vector2 ClampFocus(Vector2 focus, int widthCells, int heightCells)
    {
        float w = widthCells * MapConstants.CellSize;
        float h = heightCells * MapConstants.CellSize;
        float x = float.IsNaN(focus.X) ? w / 2 : Math.Clamp(focus.X, 0f, w);
        float y = float.IsNaN(focus.Y) ? h / 2 : Math.Clamp(focus.Y, 0f, h);
        return new Vector2(x, y);
    }

    /// <summary>Pan speed in meters per second at a zoom.</summary>
    public static float PanSpeed(float zoom) => ClampZoom(zoom) * PanSpeedPerZoom;

    /// <summary>
    /// The edge pan's direction for a cursor at <paramref name="mouse"/> in a viewport of <paramref name="size"/> pixels: -1 / +1
    /// per axis inside a band of <paramref name="band"/> pixels at that edge, else 0; zero outside the viewport (the cursor is
    /// on another window or monitor) and for NaN. M6-V7: the band is <c>[0, band)</c> and <c>[size - band, size]</c>, so the last
    /// pixel column / row (<c>size - 1</c>, where a confined cursor stops) and the border itself pan.
    /// </summary>
    public static Vector2 EdgeDirection(Vector2 mouse, Vector2 size, float band)
    {
        if (!(mouse.X >= 0f && mouse.Y >= 0f && mouse.X <= size.X && mouse.Y <= size.Y)) return Vector2.Zero;
        var d = Vector2.Zero;
        if (mouse.X < band) d.X = -1f;
        else if (mouse.X >= size.X - band) d.X = 1f;
        if (mouse.Y < band) d.Y = -1f;
        else if (mouse.Y >= size.Y - band) d.Y = 1f;
        return d;
    }
}
