using System;
using Rts.Sim.Map;

namespace Rts.Sim.ViewApi;

/// <summary>Height of the drawn terrain surface (the <see cref="TerrainMeshBuilder"/> top faces) at a ground point.</summary>
/// <remarks>
/// Pure and allocation-free, so unit views can call it every frame. Plateau cells are flat; a ramp
/// cell is the tilted plane the mesh draws. The mesh and this class share <see cref="CellCorners"/>,
/// so they cannot drift apart.
/// </remarks>
public static class TerrainHeight
{
    /// <summary>Surface height in meters at sim ground point (x, y); points off the map are clamped onto it.</summary>
    /// <remarks>On a cell boundary the cell with the larger index wins (floor), matching <see cref="NavGrid.WorldToCell"/>.</remarks>
    public static float At(Heightmap map, float x, float y)
    {
        float fx = x / MapConstants.CellSize, fy = y / MapConstants.CellSize;
        // Written so NaN lands on cell 0 instead of throwing.
        int cx = fx >= 0f ? Math.Min((int)fx, map.Width - 1) : 0;
        int cy = fy >= 0f ? Math.Min((int)fy, map.Height - 1) : 0;
        return InCell(map, cx, cy, x, y);
    }

    /// <summary>Height of cell (cx, cy)'s top surface plane at ground point (x, y), clamped to the cell's footprint.</summary>
    public static float InCell(Heightmap map, int cx, int cy, float x, float y)
    {
        if (!map.IsRamp(cx, cy)) return map.ElevationAt(cx, cy);
        Span<float> c = stackalloc float[4];
        CellCorners(map, cx, cy, c);
        float u = Math.Clamp(x / MapConstants.CellSize - cx, 0f, 1f);
        float v = Math.Clamp(y / MapConstants.CellSize - cy, 0f, 1f);
        // Lerp written as a*(1-t) + b*t so the corners come back exactly.
        float top = c[0] * (1f - u) + c[1] * u;
        float bottom = c[2] * (1f - u) + c[3] * u;
        return top * (1f - v) + bottom * v;
    }

    /// <summary>Heights of a cell's four top corners: [0] (x0, y0), [1] (x1, y0), [2] (x0, y1), [3] (x1, y1).</summary>
    /// <remarks>
    /// Plateau cells are flat at their elevation. A ramp cell is a plane tilted along the axis whose
    /// two neighbours straddle its height: each edge on that axis meets a plateau neighbour at the
    /// plateau's height and a ramp neighbour halfway between the two, so the slope is continuous
    /// from the low ground to the plateau lip.
    /// </remarks>
    public static void CellCorners(Heightmap map, int x, int y, Span<float> c)
    {
        float e = map.ElevationAt(x, y);
        c[0] = c[1] = c[2] = c[3] = e;
        if (!map.IsRamp(x, y)) return;

        if (Straddles(map, e, x - 1, y, x + 1, y))
        {
            float left = EdgeHeight(map, e, x - 1, y), right = EdgeHeight(map, e, x + 1, y);
            c[0] = c[2] = left;
            c[1] = c[3] = right;
        }
        else if (Straddles(map, e, x, y - 1, x, y + 1))
        {
            float top = EdgeHeight(map, e, x, y - 1), bottom = EdgeHeight(map, e, x, y + 1);
            c[0] = c[1] = top;
            c[2] = c[3] = bottom;
        }
    }

    private static bool Straddles(Heightmap map, float e, int ax, int ay, int bx, int by)
    {
        if (!InBounds(map, ax, ay) || !InBounds(map, bx, by)) return false;
        float ea = map.ElevationAt(ax, ay), eb = map.ElevationAt(bx, by);
        return (ea < e && eb > e) || (ea > e && eb < e);
    }

    private static float EdgeHeight(Heightmap map, float e, int nx, int ny)
    {
        float en = map.ElevationAt(nx, ny);
        return map.IsRamp(nx, ny) ? (e + en) / 2 : en;
    }

    private static bool InBounds(Heightmap map, int x, int y) => (uint)x < (uint)map.Width && (uint)y < (uint)map.Height;
}
