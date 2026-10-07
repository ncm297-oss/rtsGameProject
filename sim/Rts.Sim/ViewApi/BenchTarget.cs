using System;
using System.Numerics;
using Rts.Sim.Map;

namespace Rts.Sim.ViewApi;

/// <summary>Where the <c>--bench</c> "order across the map" step sends the army (M2-7, BUG-0101): a far point on the other side of the map.</summary>
/// <remarks>
/// Pure and read-only: it looks at the nav grid and allocates nothing. An army west of the map's
/// centre line goes to the passable cell nearest (<see cref="FarFraction"/> x width, half the height),
/// an army east of it to the mirror point; if no passable cell lies within <see cref="SearchCells"/>
/// of that point (a cliff top, a forest), it goes to the passable cell nearest the map corner opposite
/// the army. Ties go to the lower cell index.
/// </remarks>
public static class BenchTarget
{
    /// <summary>How far across the map the target sits, as a fraction of the width measured from the army's own side.</summary>
    public const float FarFraction = 0.85f;

    /// <summary>Largest distance in cells (per axis) from the far point that still counts as "near" it before the corner fallback.</summary>
    public const int SearchCells = 8;

    /// <summary>The far target (meters, a passable cell's centre) for an army centred at <paramref name="armyCentre"/>; false if the map has no passable cell at all.</summary>
    public static bool TryAcross(NavGrid grid, Vector2 armyCentre, out Vector2 target)
    {
        float w = grid.Width * MapConstants.CellSize, h = grid.Height * MapConstants.CellSize;
        bool east = armyCentre.X > w / 2;
        var far = new Vector2(east ? (1f - FarFraction) * w : FarFraction * w, h / 2);
        if (TryNearest(grid, far, SearchCells, out target)) return true;
        var corner = new Vector2(east ? 0f : w, armyCentre.Y > h / 2 ? 0f : h);
        return TryNearest(grid, corner, Math.Max(grid.Width, grid.Height), out target);
    }

    // The passable cell centre nearest `point` among cells at most `radius` cells from its cell on each axis.
    private static bool TryNearest(NavGrid grid, Vector2 point, int radius, out Vector2 best)
    {
        best = default;
        int px = (int)Math.Clamp(MathF.Floor(point.X / MapConstants.CellSize), 0f, grid.Width - 1);
        int py = (int)Math.Clamp(MathF.Floor(point.Y / MapConstants.CellSize), 0f, grid.Height - 1);
        float bestD = float.PositiveInfinity;
        for (int y = Math.Max(0, py - radius); y <= Math.Min(grid.Height - 1, py + radius); y++)
        {
            for (int x = Math.Max(0, px - radius); x <= Math.Min(grid.Width - 1, px + radius); x++)
            {
                if (!grid.IsPassable(x, y)) continue;
                Vector2 c = grid.CellCenter(x, y);
                float d = Vector2.DistanceSquared(c, point);
                if (d < bestD) // row-major scan, strict compare: ties keep the lower index
                {
                    bestD = d;
                    best = c;
                }
            }
        }
        return bestD < float.PositiveInfinity;
    }
}
