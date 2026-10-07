using System;
using System.Numerics;
using Rts.Sim.Map;

namespace Rts.Sim.ViewApi;

/// <summary>Debug start positions until real start locations exist: one block of units each side of the map centre.</summary>
/// <remarks>
/// Deterministic from the nav grid alone (no RNG). A block fills the open cell centres nearest
/// to the middle of its inner edge, a column <see cref="HalfGapCells"/> cells from the map's centre
/// line, scoring cells by <c>dx² + dy²</c>: a half-disc facing the other block, compact enough that
/// both default blocks fit the start camera. Ties go to the lower cell index. A cell is open when it
/// is passable, not a ramp, and none of its 8 neighbours is <see cref="NavFlags.Blocked"/> (so no
/// spot touches a tree, a mine, a cliff edge or the border ring; docs/02 "Map": start locations are
/// open bases, BUG-0085). A block also stays in one clearing: the open cells joined to its first
/// (best-scoring) cell through open cells on that cell's level, so an army never starts split by a
/// cliff or a forest. The first cell is the best-scoring one whose clearing holds the whole block,
/// else the one with the roomiest clearing. Used once at match start, so it allocates.
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
                if (dx < 0 || dx % stride != 0 || !IsOpen(grid, x, y)) continue;
                long score = (long)dx * dx + (long)dy * dy;
                keys[n++] = score * cells + (y * (long)w + x);
            }
        }
        Array.Sort(keys, 0, n);

        // The block takes one clearing: the open cells joined to its first cell through open cells on
        // the same level. The first cell is the best-scoring one whose clearing holds the whole block
        // (else the one with the roomiest clearing), so a small terrace by the centre line can't split
        // or shrink the army.
        int[] region = Regions(grid, out int regions);
        var room = new int[regions];
        var keyRegion = new int[n];
        for (int i = 0; i < n; i++) room[keyRegion[i] = region[(int)(keys[i] % cells)]]++;
        int want = Math.Min(count, n), chosen = -1;
        for (int i = 0; i < n && chosen < 0; i++)
            if (room[keyRegion[i]] >= want) chosen = keyRegion[i];
        if (chosen < 0) // nowhere holds them all: the roomiest clearing, first by score on ties
            for (int i = 0; i < n; i++)
                if (chosen < 0 || room[keyRegion[i]] > room[chosen]) chosen = keyRegion[i];

        int take = chosen < 0 ? 0 : Math.Min(want, room[chosen]);
        var spots = new Vector2[take];
        int found = 0;
        for (int i = 0; i < n && found < take; i++)
        {
            if (keyRegion[i] != chosen) continue;
            int cell = (int)(keys[i] % cells);
            spots[found++] = grid.CellCenter(cell % w, cell / w);
        }
        return spots;
    }

    // Labels every open cell with its clearing (4-connected open cells on one level), -1 elsewhere.
    private static int[] Regions(NavGrid grid, out int count)
    {
        int w = grid.Width, h = grid.Height;
        var region = new int[w * h];
        Array.Fill(region, -1);
        var stack = new int[w * h];
        count = 0;
        for (int start = 0; start < region.Length; start++)
        {
            if (region[start] >= 0 || !IsOpen(grid, start % w, start / w)) continue;
            int level = grid.LevelAt(start % w, start / w), top = 0;
            region[start] = count;
            stack[top++] = start;
            while (top > 0)
            {
                int c = stack[--top], x = c % w, y = c / w;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    if (!grid.InBounds(nx, ny)) continue;
                    int nc = ny * w + nx;
                    if (region[nc] >= 0 || grid.LevelAt(nx, ny) != level || !IsOpen(grid, nx, ny)) continue;
                    region[nc] = count;
                    stack[top++] = nc;
                }
            }
            count++;
        }
        return region;
    }

    /// <summary>True if (x, y) may hold a start spot: passable, not a ramp, and no <see cref="NavFlags.Blocked"/> cell (off-map counts as blocked) among its 8 neighbours.</summary>
    public static bool IsOpen(NavGrid grid, int x, int y)
    {
        if (!grid.IsPassable(x, y) || (grid.FlagsAt(x, y) & NavFlags.Ramp) != 0) return false;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if ((grid.FlagsAt(x + dx, y + dy) & (NavFlags.Blocked | NavFlags.Resource)) != 0) return false;
        return true;
    }
}
