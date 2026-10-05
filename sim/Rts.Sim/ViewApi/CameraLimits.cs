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
}
