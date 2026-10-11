using System;
using Rts.Sim.Map;

namespace Rts.Sim.ViewApi;

/// <summary>The terrain cells a ground disc or ring covers, each as its own drawn top surface, so a disc drawn on them lies on the ground on both sides of a cliff (BUG-0430).</summary>
/// <remarks>
/// Every cell's drawn top is one plane (flat, or a ramp's tilted plane; <see cref="TerrainHeight.CellCorners"/>, which the
/// terrain mesh shares), so a quad per cell at the cell's corner heights is the terrain surface exactly. A disc drawn as
/// these quads (the circle cut out per pixel) hugs the plateau and the low ground alike; cells don't share vertices, so a
/// cliff between two cells leaves a step in the patch rather than a slanted sheet across it. Pure and allocation-free; reads
/// only the immutable <see cref="Heightmap"/>.
/// </remarks>
public static class GroundPatch
{
    /// <summary>Most cells a disc of <paramref name="outer"/> m can touch: the output length that always fits <see cref="Collect"/>.</summary>
    public static int MaxCells(float outer)
    {
        if (!(outer > 0f)) return 1;
        int side = (int)MathF.Ceiling(MathF.Min(outer, 4096f) * 2f / MapConstants.CellSize) + 1;
        return side * side;
    }

    /// <summary>
    /// Writes the map cells that overlap the ring between <paramref name="inner"/> and <paramref name="outer"/> m round (x, y)
    /// (inner 0: the whole disc) into <paramref name="quads"/>, row by row, and returns how many (at most its length). A
    /// cell overlaps when its nearest point is closer than <paramref name="outer"/> and its farthest corner farther than
    /// <paramref name="inner"/>. Cells off the map are left out; a non-positive or NaN radius gives none.
    /// </summary>
    public static int Collect(Heightmap map, float x, float y, float outer, float inner, Span<GroundQuad> quads)
    {
        if (!(outer > 0f) || !float.IsFinite(x) || !float.IsFinite(y)) return 0;
        const float cs = MapConstants.CellSize;
        outer = MathF.Min(outer, 4096f);
        int x0 = Math.Max(0, (int)MathF.Floor((x - outer) / cs)), x1 = Math.Min(map.Width - 1, (int)MathF.Floor((x + outer) / cs));
        int y0 = Math.Max(0, (int)MathF.Floor((y - outer) / cs)), y1 = Math.Min(map.Height - 1, (int)MathF.Floor((y + outer) / cs));
        float outer2 = outer * outer, inner2 = inner > 0f ? inner * inner : -1f;
        Span<float> c = stackalloc float[4];
        int n = 0;
        for (int cy = y0; cy <= y1; cy++)
            for (int cx = x0; cx <= x1; cx++)
            {
                float minX = cx * cs, minY = cy * cs, maxX = minX + cs, maxY = minY + cs;
                float nx = Math.Clamp(x, minX, maxX) - x, ny = Math.Clamp(y, minY, maxY) - y;
                if (nx * nx + ny * ny >= outer2) continue;
                float fx = MathF.Max(MathF.Abs(minX - x), MathF.Abs(maxX - x)), fy = MathF.Max(MathF.Abs(minY - y), MathF.Abs(maxY - y));
                if (fx * fx + fy * fy <= inner2) continue; // wholly inside a ring's hole
                if (n >= quads.Length) return n;
                TerrainHeight.CellCorners(map, cx, cy, c);
                quads[n++] = new GroundQuad(cx, cy, c[0], c[1], c[2], c[3]);
            }
        return n;
    }

    /// <summary>The height of quad <paramref name="q"/>'s surface at ground point (x, y), clamped to its cell (the bilinear blend of its corners; exact, as the cell is a plane).</summary>
    public static float HeightIn(in GroundQuad q, float x, float y)
    {
        float u = Unit(x / MapConstants.CellSize - q.X), v = Unit(y / MapConstants.CellSize - q.Y);
        float top = q.H00 * (1f - u) + q.H10 * u, bottom = q.H01 * (1f - u) + q.H11 * u;
        return top * (1f - v) + bottom * v;
    }

    private static float Unit(float t) => t > 0f ? MathF.Min(t, 1f) : 0f;
}
