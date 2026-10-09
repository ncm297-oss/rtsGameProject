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

    // Cell-edge steps (m) along the line from a disc's centre to a rim sample (BUG-0224, BUG-0226). Drawn surfaces that
    // join meet exactly (CellCorners; generated maps have no seams, MaxUnderEdgeWalkQaTests), so a step is a wall: a cliff
    // or a ramp's side wall, which grows from 0 m at the ramp's foot. A disc lies over a step of up to Straddle(radius)
    // (StraddleStep plus a few mm) as if it were flat ground. Over a step of up to WallStep (the foot of a side wall) the
    // rim sample counts, but at most FootRise above the step's top: the disc may sit on the step's edge, not climb the
    // ramp beyond it (with one flat 0.15 m slack it climbed about 0.5 m up). A bigger step is a wall: the sample is dropped.
    private const float StraddleStep = 0.05f, WallStep = 0.15f, FootRise = 0.08f;

    /// <summary>
    /// The highest surface height (m) among the centre and eight points round the rim of the disc of
    /// <paramref name="radius"/> m at (x, y), so a flat disc placed there is not half buried on a ramp (BUG-0190). Only
    /// ground joined to the centre by the drawn surface counts: a rim point whose straight line from the centre crosses a
    /// cell edge where the two cells' surfaces don't meet (a cliff, or a ramp's side wall) is ignored, so a disc beside a
    /// cliff or a ramp's side stays on its own ground (BUG-0223, BUG-0224); beyond the low foot of a side wall it counts at
    /// most a little above the step (BUG-0226). A non-positive or NaN radius samples the centre only.
    /// </summary>
    public static float MaxUnder(Heightmap map, float x, float y, float radius)
    {
        int cx = ClampCell(x / MapConstants.CellSize, map.Width);
        int cy = ClampCell(y / MapConstants.CellSize, map.Height);
        float centre = InCell(map, cx, cy, x, y);
        if (!(radius > 0f)) return centre;
        float d = radius * Diag;
        float best = centre;
        float straddle = Straddle(radius);
        best = Rim(map, x, y, x + radius, y, straddle, best);
        best = Rim(map, x, y, x - radius, y, straddle, best);
        best = Rim(map, x, y, x, y + radius, straddle, best);
        best = Rim(map, x, y, x, y - radius, straddle, best);
        best = Rim(map, x, y, x + d, y + d, straddle, best);
        best = Rim(map, x, y, x + d, y - d, straddle, best);
        best = Rim(map, x, y, x - d, y + d, straddle, best);
        best = Rim(map, x, y, x - d, y - d, straddle, best);
        return best;
    }

    // One rim sample (x1, y1) of MaxUnder: counts only if the surface is joined along the line from the centre (x0, y0),
    // and no higher than the cap a side wall's foot on the way puts on it.
    private static float Rim(Heightmap map, float x0, float y0, float x1, float y1, float straddle, float best)
    {
        if (!Joined(map, x0, y0, x1, y1, straddle, out float cap)) return best;
        int cx = ClampCell(x1 / MapConstants.CellSize, map.Width);
        int cy = ClampCell(y1 / MapConstants.CellSize, map.Height);
        return MathF.Max(best, MathF.Min(InCell(map, cx, cy, x1, y1), cap));
    }

    // The largest step a disc of `radius` lies over as flat ground: StraddleStep plus twice the steepest slope's rise (one
    // level per cell) over 1/512 of the radius, the wall test of the 512-step wall-aware walk QA measures MaxUnder against
    // (MaxUnderEdgeWalkQaTests), so the two agree on which side-wall feet are walls. At most 0.06 m for the widest disc.
    private static float Straddle(float radius) =>
        StraddleStep + MathF.Min(radius, 2f) / 512f * (MapConstants.LevelHeight / MapConstants.CellSize) * 2f;

    // Walks the cells the segment (x0, y0) -> (x1, y1) passes through (a grid walk, edge by edge) and compares the two
    // cells' surfaces at each crossing point: a step bigger than WallStep is a wall (false). A step between `straddle`
    // and WallStep caps the sample at the step's top plus FootRise (the lowest such cap; +infinity when none). Within a
    // cell the surface is one plane, so the crossings are the only places a wall can be.
    private static bool Joined(Heightmap map, float x0, float y0, float x1, float y1, float straddle, out float cap)
    {
        const float cs = MapConstants.CellSize;
        cap = float.PositiveInfinity;
        int ix = ClampCell(x0 / cs, map.Width), iy = ClampCell(y0 / cs, map.Height);
        int tx = ClampCell(x1 / cs, map.Width), ty = ClampCell(y1 / cs, map.Height);
        float dx = x1 - x0, dy = y1 - y0;
        int sx = tx > ix ? 1 : tx < ix ? -1 : 0, sy = ty > iy ? 1 : ty < iy ? -1 : 0;
        // Each step moves at least one cell toward the target, so this bound is never reached on a finite segment.
        for (int guard = Math.Abs(tx - ix) + Math.Abs(ty - iy); guard > 0 && (ix != tx || iy != ty); guard--)
        {
            // Segment parameter of the next vertical and horizontal cell edge (beyond 1 or none: infinity).
            float px = ix != tx && dx != 0f ? ((sx > 0 ? ix + 1 : ix) * cs - x0) / dx : float.PositiveInfinity;
            float py = iy != ty && dy != 0f ? ((sy > 0 ? iy + 1 : iy) * cs - y0) / dy : float.PositiveInfinity;
            if (float.IsPositiveInfinity(px) && float.IsPositiveInfinity(py)) return true; // clamped off the map
            float t = MathF.Min(px, py);
            int nx = ix != tx && px <= t ? ix + sx : ix, ny = iy != ty && py <= t ? iy + sy : iy;
            float ex = x0 + dx * t, ey = y0 + dy * t;
            float here = InCell(map, ix, iy, ex, ey), there = InCell(map, nx, ny, ex, ey);
            float step = MathF.Abs(here - there);
            if (step > WallStep) return false;
            if (step > straddle) cap = MathF.Min(cap, MathF.Max(here, there) + FootRise);
            ix = nx;
            iy = ny;
        }
        return true;
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
