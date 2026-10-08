using System;
using System.Numerics;
using Rts.Sim.Entities;
using Rts.Sim.Map;

namespace Rts.Sim.Economy;

/// <summary>
/// The ring search shared by the construction push-out (M3-3) and production spawns (M3-4): the nearest free cell
/// outside a footprint, on the footprint's plateau. Allocation-free.
/// </summary>
/// <remarks>
/// Ring r is the cells at Chebyshev distance r from the footprint (ring 1: the cells 8-adjacent to it). Rings are
/// searched nearest first; within a ring the cell nearest the given point wins, ties to the lower cell index. A cell
/// is free when it is passable, on the footprint's plateau (<see cref="Plateaus"/>: connected same-level ground, M3-H2,
/// BUG-0097; before it, any cell of the same level, so a full plateau spilled onto another one of that level), and no
/// live unit's center lies in it. The walk stops at the first ring with a free cell, and at the latest once a ring
/// encloses the plateau's bounding box: past that no cell is on the plateau, so a full small plateau costs its own
/// area, not the map's (BUG-0095).
/// </remarks>
internal static class FreeCellSearch
{
    /// <summary>
    /// The nearest free cell (index <c>y * Width + x</c>) to <paramref name="from"/> in the nearest ring round the
    /// footprint at (<paramref name="x0"/>, <paramref name="y0"/>) of <paramref name="fw"/> x <paramref name="fh"/> cells,
    /// or -1 if its plateau has none. <paramref name="taken"/> caches each cell's answer (0 not asked yet, 1 free, 2 or
    /// more a unit's center in it); the caller clears it whenever units may have moved, and marks a cell 2 when it puts
    /// a unit there. Occupants come from the spatial hash, which must hold the units as they stand, except those so marked.
    /// </summary>
    public static int Nearest(World world, int x0, int y0, int fw, int fh, Vector2 from, int[] taken) =>
        Walk(world, x0, y0, fw, fh, from, taken, 0);

    /// <summary>
    /// For a push-out on a plateau with no free cell left (M3-H2, BUG-0095): the nearest passable cell of the plateau,
    /// by the same ring order, holding fewer than <paramref name="pass"/> (1 or more) of the push-out's leftovers, where
    /// <paramref name="taken"/> counts them above 2 (a cell holding k leftovers reads <c>2 + k</c>). So leftovers spread
    /// one per cell before any cell takes a second. -1 when every passable cell of the plateau holds that many already,
    /// or the plateau has none outside the footprint.
    /// </summary>
    public static int NearestForLeftover(World world, int x0, int y0, int fw, int fh, Vector2 from, int[] taken, int pass) =>
        Walk(world, x0, y0, fw, fh, from, taken, pass);

    /// <summary>The ring walk; <paramref name="pass"/> 0 asks for a free cell, more for a leftover's cell (see <see cref="NearestForLeftover"/>).</summary>
    private static int Walk(World world, int x0, int y0, int fw, int fh, Vector2 from, int[] taken, int pass)
    {
        NavGrid g = world.NavGrid;
        int plateau = world.Plateaus.At(y0 * g.Width + x0);
        if (!world.Plateaus.Bounds(plateau, out int minX, out int minY, out int maxX, out int maxY)) return -1;
        for (int r = 1; ; r++)
        {
            int left = x0 - r, right = x0 + fw - 1 + r, top = y0 - r, bottom = y0 + fh - 1 + r;
            // The ring encloses the plateau's box: it and every ring after it lie outside the plateau.
            if (left < minX && right > maxX && top < minY && bottom > maxY) return -1;
            int best = -1;
            float bestD2 = float.PositiveInfinity;
            // The ring's cells in index order (rows top to bottom, x ascending), clipped to the box, so a tie keeps the lower cell.
            int yFrom = Math.Max(top, minY), yTo = Math.Min(bottom, maxY);
            for (int y = yFrom; y <= yTo; y++)
            {
                if (y == top || y == bottom)
                {
                    int xFrom = Math.Max(left, minX), xTo = Math.Min(right, maxX);
                    for (int x = xFrom; x <= xTo; x++) Consider(world, taken, x, y, plateau, from, pass, ref best, ref bestD2);
                }
                else
                {
                    if (left >= minX) Consider(world, taken, left, y, plateau, from, pass, ref best, ref bestD2);
                    if (right <= maxX) Consider(world, taken, right, y, plateau, from, pass, ref best, ref bestD2);
                }
            }
            if (best >= 0) return best;
        }
    }

    private static void Consider(World world, int[] taken, int x, int y, int plateau, Vector2 from, int pass, ref int best, ref float bestD2)
    {
        NavGrid g = world.NavGrid;
        int c = y * g.Width + x;
        if (!g.IsPassable(x, y) || world.Plateaus.At(c) != plateau) return;
        float d2 = Vector2.DistanceSquared(from, g.CellCenter(x, y));
        if (d2 >= bestD2) return;
        // A leftover's pass: any passable plateau cell with fewer than `pass` leftovers (an unasked or free cell has none).
        if (pass > 0 ? taken[c] - 2 < pass : !Occupied(world, taken, x, y))
        {
            best = c;
            bestD2 = d2;
        }
    }

    /// <summary>
    /// True if a live unit has its center in cell (x, y): asked of the spatial hash the first time, then read from
    /// <paramref name="taken"/>, where the callers also mark the cells they set units down on.
    /// </summary>
    private static bool Occupied(World world, int[] taken, int x, int y)
    {
        int c = y * world.NavGrid.Width + x;
        if (taken[c] != 0) return taken[c] >= 2;
        const float cs = MapConstants.CellSize;
        UnitStore u = world.Units;
        int[] near = world.Neighbors;
        bool occupied = false;
        int n = world.Spatial.QueryRect(new Vector2(x * cs, y * cs), new Vector2((x + 1) * cs, (y + 1) * cs), near);
        for (int m = 0; m < n && !occupied; m++)
        {
            Vector2 p = u.Position[near[m]];
            occupied = u.Alive[near[m]] && p.X >= x * cs && p.X < (x + 1) * cs && p.Y >= y * cs && p.Y < (y + 1) * cs;
        }
        taken[c] = occupied ? 2 : 1;
        return occupied;
    }
}
