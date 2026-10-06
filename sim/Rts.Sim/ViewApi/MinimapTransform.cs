using System;
using System.Numerics;

namespace Rts.Sim.ViewApi;

/// <summary>Maps ground meters (sim x, y) to minimap pixels and back for an aspect-preserving fit; a non-square map is letterboxed.</summary>
/// <remarks>
/// Pixel (0, 0) is the control's top-left corner; map y runs down the image, so screen-up is map
/// -y, as in the 3D camera. The map fills the control's shorter side and is centred on the other.
/// </remarks>
public readonly struct MinimapTransform
{
    /// <summary>How far along a camera ray a view corner is taken when the ray misses the ground plane or meets it farther away, in meters.</summary>
    public const float MaxRayMeters = 200f;

    /// <summary>Pixels per meter.</summary>
    public readonly float Scale;

    /// <summary>Pixel position of the map's (0, 0) corner.</summary>
    public readonly Vector2 Offset;

    /// <summary>Map size in meters.</summary>
    public readonly Vector2 MapSize;

    /// <summary>Fits a map of <paramref name="mapMeters"/> into a control of <paramref name="controlPixels"/>.</summary>
    public MinimapTransform(Vector2 controlPixels, Vector2 mapMeters)
    {
        MapSize = mapMeters;
        Scale = MathF.Min(controlPixels.X / mapMeters.X, controlPixels.Y / mapMeters.Y);
        Offset = (controlPixels - mapMeters * Scale) / 2;
    }

    /// <summary>Pixel rectangle the map occupies: top-left corner and size.</summary>
    public (Vector2 Position, Vector2 Size) MapRect => (Offset, MapSize * Scale);

    /// <summary>Minimap pixel of a ground point in meters.</summary>
    public Vector2 ToPixel(Vector2 map) => Offset + map * Scale;

    /// <summary>Ground point under a minimap pixel; false for pixels outside the map area or non-finite input.</summary>
    public bool TryToMap(Vector2 pixel, out Vector2 map)
    {
        map = (pixel - Offset) / Scale;
        return float.IsFinite(map.X) && float.IsFinite(map.Y)
            && map.X >= 0 && map.Y >= 0 && map.X <= MapSize.X && map.Y <= MapSize.Y;
    }

    /// <summary>Clamps a ground point onto the map rectangle.</summary>
    public Vector2 ClampToMap(Vector2 map) => Vector2.Clamp(map, Vector2.Zero, MapSize);

    /// <summary>Ground (x, z) where a view ray meets the plane at height <paramref name="planeY"/>, or the point <see cref="MaxRayMeters"/> along it if it misses or lands farther.</summary>
    /// <remarks>View coordinates: X = sim x, Y = elevation, Z = sim y.</remarks>
    public static Vector2 RayToGround(Vector3 origin, Vector3 direction, float planeY)
    {
        Vector3 d = Vector3.Normalize(direction);
        float t = d.Y < 0f ? (planeY - origin.Y) / d.Y : float.PositiveInfinity;
        if (!(t >= 0f && t <= MaxRayMeters)) t = MaxRayMeters;
        Vector3 p = origin + d * t;
        return new Vector2(p.X, p.Z);
    }
}
