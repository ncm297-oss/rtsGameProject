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
        int cx = ClampCell(x / MapConstants.CellSize, map.Width);
        int cy = ClampCell(y / MapConstants.CellSize, map.Height);
        return InCell(map, cx, cy, x, y);
    }

    // Unit offsets of the eight rim samples of MaxUnder (every 45 degrees; constants, no trig).
    private const float Diag = 0.70710678f;

    // The steepest slope a drawn ramp cell can have (rise over run): one level across one cell.
    private const float SteepestRamp = MapConstants.LevelHeight / MapConstants.CellSize;

    /// <summary>
    /// The highest surface height (m) among the centre and eight points round the rim of the disc of
    /// <paramref name="radius"/> m at (x, y), so a flat disc placed there is not half buried on a ramp (BUG-0190). Only
    /// ground joined to the centre by a slope counts: a rim point on another plateau cell when the centre is on one too,
    /// or one higher than any ramp could rise over the radius, is across a cliff and is ignored, so a disc beside a cliff
    /// stays on its own ground (BUG-0223). A non-positive or NaN radius samples the centre only.
    /// </summary>
    public static float MaxUnder(Heightmap map, float x, float y, float radius)
    {
        int cx = ClampCell(x / MapConstants.CellSize, map.Width);
        int cy = ClampCell(y / MapConstants.CellSize, map.Height);
        float centre = InCell(map, cx, cy, x, y);
        if (!(radius > 0f)) return centre;
        bool centreRamp = map.IsRamp(cx, cy);
        // A ramp can lift a rim point at most radius x slope; the small slack absorbs float rounding.
        float maxRise = radius * SteepestRamp * 1.001f + 1e-4f;
        float d = radius * Diag;
        float best = centre;
        best = Rim(map, x + radius, y, centre, centreRamp, maxRise, best);
        best = Rim(map, x - radius, y, centre, centreRamp, maxRise, best);
        best = Rim(map, x, y + radius, centre, centreRamp, maxRise, best);
        best = Rim(map, x, y - radius, centre, centreRamp, maxRise, best);
        best = Rim(map, x + d, y + d, centre, centreRamp, maxRise, best);
        best = Rim(map, x + d, y - d, centre, centreRamp, maxRise, best);
        best = Rim(map, x - d, y + d, centre, centreRamp, maxRise, best);
        best = Rim(map, x - d, y - d, centre, centreRamp, maxRise, best);
        return best;
    }

    // One rim sample of MaxUnder: counts only if a slope joins it to the centre (BUG-0223).
    private static float Rim(Heightmap map, float x, float y, float centre, bool centreRamp, float maxRise, float best)
    {
        int cx = ClampCell(x / MapConstants.CellSize, map.Width);
        int cy = ClampCell(y / MapConstants.CellSize, map.Height);
        // Plateau to plateau is either the same height (no lift) or a cliff.
        if (!centreRamp && !map.IsRamp(cx, cy)) return best;
        float h = InCell(map, cx, cy, x, y);
        return h - centre <= maxRise ? MathF.Max(best, h) : best;
    }

    // Clamps in float before the cast: an out-of-range float-to-int cast is int.MinValue on x64,
    // so 1e10 or +Infinity would otherwise land on cell -2^31 and throw (BUG-0052). NaN fails
    // `f >= 0` and lands on cell 0.
    private static int ClampCell(float f, int size) => f >= 0f ? (int)MathF.Min(f, size - 1) : 0;

    /// <summary>Height of cell (cx, cy)'s top surface plane at ground point (x, y), clamped to the cell's footprint.</summary>
    public static float InCell(Heightmap map, int cx, int cy, float x, float y)
    {
        if (!map.IsRamp(cx, cy)) return map.ElevationAt(cx, cy);
        Span<float> c = stackalloc float[4];
        CellCorners(map, cx, cy, c);
        float u = Unit(x / MapConstants.CellSize - cx);
        float v = Unit(y / MapConstants.CellSize - cy);
        // Lerp written as a*(1-t) + b*t so the corners come back exactly.
        float top = c[0] * (1f - u) + c[1] * u;
        float bottom = c[2] * (1f - u) + c[3] * u;
        return top * (1f - v) + bottom * v;
    }

    // Clamp to [0, 1] with NaN going to 0 (Math.Clamp passes NaN through, which would make the height NaN).
    private static float Unit(float t) => t > 0f ? MathF.Min(t, 1f) : 0f;

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
