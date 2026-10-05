using System;
using System.Numerics;
using Rts.Sim.Map;

namespace Rts.Sim.ViewApi;

/// <summary>Intersects a camera ray with the drawn terrain, without physics (docs/03 "Picking without physics").</summary>
/// <remarks>
/// Coordinates are the view's Y-up meters: X = sim x, Y = elevation, Z = sim y. The ray is clipped
/// to the map's box, then walked cell by cell (grid DDA) from the camera. In each cell the top
/// surface is a plane (<see cref="TerrainHeight.InCell"/>), so the ray's height above it is linear
/// and the crossing is solved exactly. A ray that enters a cell already below that cell's surface
/// has hit the cliff (or ramp side) wall on the boundary, which belongs to the higher cell. The
/// first hit wins. Pure, allocation-free, and it only reads the heightmap.
/// </remarks>
public static class GroundPicker
{
    /// <summary>How far a hit is kept inside the cell it belongs to, in meters, so the cell lookup can't round onto a neighbour.</summary>
    public const float CellInset = 1e-3f;

    /// <summary>Finds where the ray first meets the terrain; false when it misses the map.</summary>
    /// <param name="map">Terrain to pick on.</param>
    /// <param name="origin">Ray start (the camera), view coordinates.</param>
    /// <param name="direction">Ray direction; need not be normalized.</param>
    /// <param name="hit">The point on the ray where it meets the surface or a wall; X/Z lie inside the hit cell.</param>
    public static bool TryPick(Heightmap map, Vector3 origin, Vector3 direction, out Vector3 hit)
    {
        hit = default;
        const float cs = MapConstants.CellSize;
        if (!IsFinite(origin) || !IsFinite(direction) || direction == Vector3.Zero) return false;

        // Nothing stands above the top level, so the box's lid is just over it.
        float top = MapConstants.MaxLevel * MapConstants.LevelHeight + 1f;
        float t0 = 0f, t1 = float.PositiveInfinity;
        if (!Slab(origin.X, direction.X, 0f, map.Width * cs, ref t0, ref t1)) return false;
        if (!Slab(origin.Y, direction.Y, -1f, top, ref t0, ref t1)) return false;
        if (!Slab(origin.Z, direction.Z, 0f, map.Height * cs, ref t0, ref t1)) return false;

        int cx = Math.Clamp((int)MathF.Floor((origin.X + direction.X * t0) / cs), 0, map.Width - 1);
        int cz = Math.Clamp((int)MathF.Floor((origin.Z + direction.Z * t0) / cs), 0, map.Height - 1);
        int stepX = Math.Sign(direction.X), stepZ = Math.Sign(direction.Z);
        float nextX = NextBoundary(origin.X, direction.X, cx, stepX);
        float nextZ = NextBoundary(origin.Z, direction.Z, cz, stepZ);
        float deltaX = stepX != 0 ? cs / MathF.Abs(direction.X) : float.PositiveInfinity;
        float deltaZ = stepZ != 0 ? cs / MathF.Abs(direction.Z) : float.PositiveInfinity;

        float enter = t0;
        while (true)
        {
            float exit = MathF.Max(enter, MathF.Min(MathF.Min(nextX, nextZ), t1));
            float above0 = Above(map, cx, cz, origin, direction, enter);
            float above1 = Above(map, cx, cz, origin, direction, exit);
            float t = float.NaN;
            if (above0 <= 0f) t = enter; // entered below the surface: the wall on this boundary
            else if (above1 <= 0f) t = enter + (exit - enter) * (above0 / (above0 - above1));
            if (!float.IsNaN(t))
            {
                Vector3 p = origin + direction * t;
                float x = Math.Clamp(p.X, cx * cs + CellInset, (cx + 1) * cs - CellInset);
                float z = Math.Clamp(p.Z, cz * cs + CellInset, (cz + 1) * cs - CellInset);
                hit = new Vector3(x, p.Y, z);
                return true;
            }
            if (exit >= t1) return false;
            if (nextX < nextZ)
            {
                cx += stepX;
                enter = nextX;
                nextX += deltaX;
            }
            else
            {
                cz += stepZ;
                enter = nextZ;
                nextZ += deltaZ;
            }
            if ((uint)cx >= (uint)map.Width || (uint)cz >= (uint)map.Height) return false;
        }
    }

    // Height of the ray above cell (cx, cz)'s surface plane at parameter t.
    private static float Above(Heightmap map, int cx, int cz, Vector3 o, Vector3 d, float t) =>
        o.Y + d.Y * t - TerrainHeight.InCell(map, cx, cz, o.X + d.X * t, o.Z + d.Z * t);

    // Ray parameter of the next cell boundary along one axis.
    private static float NextBoundary(float o, float d, int cell, int step)
    {
        if (step == 0) return float.PositiveInfinity;
        float edge = (step > 0 ? cell + 1 : cell) * MapConstants.CellSize;
        return (edge - o) / d;
    }

    // Narrows [t0, t1] to where the ray lies between lo and hi on one axis; false if that is empty.
    private static bool Slab(float o, float d, float lo, float hi, ref float t0, ref float t1)
    {
        if (d == 0f) return o >= lo && o <= hi;
        float a = (lo - o) / d, b = (hi - o) / d;
        if (a > b) (a, b) = (b, a);
        t0 = MathF.Max(t0, a);
        t1 = MathF.Min(t1, b);
        return t0 <= t1;
    }

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
