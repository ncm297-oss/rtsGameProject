using System;
using System.Numerics;
using Rts.Sim.Map;

namespace Rts.Sim.ViewApi;

/// <summary>Debug start positions until real start locations exist: one block of units each side of the map centre.</summary>
/// <remarks>
/// Deterministic from the nav grid alone (no RNG). A block fills the passable cell centres nearest
/// to the middle of its inner edge, a column <see cref="HalfGapCells"/> cells from the map's centre
/// line, scoring cells by <c>dx² + dy²</c>: a half-disc facing the other block, compact enough that
/// both default blocks fit the start camera. Ties go to the lower cell index. Used once at match
/// start, so it allocates.
/// </remarks>
public static class StartLayout
{
    /// <summary>Cells between the map's centre line and each block's inner edge.</summary>
    public const int HalfGapCells = 3;

    /// <summary>Up to <paramref name="count"/> unit positions (meters) for the west or east block; fewer if that half of the map runs out of room.</summary>
    /// <param name="grid">Passability of the match map.</param>
    /// <param name="count">Units wanted.</param>
    /// <param name="west">True for the block west of the centre (smaller x), false for east.</param>
    /// <param name="maxRadius">Largest unit radius in the block; positions are at least twice this apart.</param>
    public static Vector2[] Block(NavGrid grid, int count, bool west, float maxRadius)
    {
        if (count <= 0) return Array.Empty<Vector2>();
        int w = grid.Width, h = grid.Height;
        // Cell centres are a whole number of cells apart, so a stride of ceil(2r / cell) keeps bodies apart.
        int stride = Math.Max(1, (int)MathF.Ceiling(2f * maxRadius / MapConstants.CellSize - 1e-4f));
        int mid = w / 2, cy = h / 2;
        int inner = west ? mid - HalfGapCells - 1 : mid + HalfGapCells;
        int dir = west ? -1 : 1;
        long cells = (long)w * h;

        var keys = new long[w * h];
        int n = 0;
        for (int y = 0; y < h; y++)
        {
            int dy = y - cy;
            if (dy % stride != 0) continue;
            for (int x = 0; x < w; x++)
            {
                int dx = (x - inner) * dir;
                if (dx < 0 || dx % stride != 0 || !grid.IsPassable(x, y)) continue;
                long score = (long)dx * dx + (long)dy * dy;
                keys[n++] = score * cells + (y * (long)w + x);
            }
        }
        Array.Sort(keys, 0, n);

        int take = Math.Min(count, n);
        var result = new Vector2[take];
        for (int i = 0; i < take; i++)
        {
            int cell = (int)(keys[i] % cells);
            result[i] = grid.CellCenter(cell % w, cell / w);
        }
        return result;
    }
}
