using System;
using Rts.Sim.Map;

namespace Rts.Sim.Pathfinding;

/// <summary>Shortest-path cost and next-step direction from every cell of a <see cref="NavGrid"/> to one target cell (docs/03 "Flow fields").</summary>
/// <remarks>
/// Built by 8-connected Dijkstra from the target. A straight step costs 1 and a diagonal step
/// <see cref="DiagonalCost"/>; a diagonal step is allowed only when both cells it passes between
/// are passable (no corner cutting), so a unit heading at the next cell's center never clips a
/// blocked corner. Each reached cell points at the neighbor that starts its shortest path (ties go
/// to the lowest direction number). A blocked target cell is replaced by the nearest passable cell.
/// Cells are indexed <c>y * Width + x</c>, like the nav grid.
/// </remarks>
public sealed class FlowField
{
    /// <summary>Direction of the target cell, of blocked cells, and of cells the target can't reach.</summary>
    public const byte NoDirection = 255;

    /// <summary>Number of directions; odd numbers are diagonals.</summary>
    public const int DirectionCount = 8;

    /// <summary>Cost of a diagonal step (sqrt 2); a straight step costs 1.</summary>
    public const float DiagonalCost = 1.41421f;

    // Direction d steps by (DirX[d], DirY[d]): east first, then clockwise in +y-down grid order.
    private static readonly int[] DirX = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private static readonly int[] DirY = { 0, 1, 1, 1, 0, -1, -1, -1 };

    private readonly float[] _cost;
    private readonly byte[] _direction;
    private readonly int[] _offset; // cell index step per direction for this width

    internal FlowField(int width, int height)
    {
        Width = width;
        Height = height;
        _cost = new float[width * height];
        _direction = new byte[width * height];
        _offset = new int[DirectionCount];
        for (int d = 0; d < DirectionCount; d++)
            _offset[d] = DirY[d] * width + DirX[d];
    }

    /// <summary>Width in cells (same as the grid it was built on).</summary>
    public int Width { get; }

    /// <summary>Height in cells.</summary>
    public int Height { get; }

    /// <summary>The cell the field was asked to lead to (its cache key); -1 before the first build.</summary>
    public int RequestedCell { get; private set; } = -1;

    /// <summary>The cell the field actually leads to: <see cref="RequestedCell"/>, or the nearest passable cell if that is blocked; -1 if the grid has no passable cell.</summary>
    public int TargetCell { get; private set; } = -1;

    /// <summary><see cref="NavGrid.Version"/> at build time; a mismatch means the field is stale.</summary>
    public int Version { get; private set; }

    /// <summary>Path cost from the cell to <see cref="TargetCell"/>; +infinity for blocked or unreachable cells.</summary>
    public float CostAt(int cell) => _cost[cell];

    /// <summary>Direction (0-7) of the cell's next step, or <see cref="NoDirection"/>.</summary>
    public byte DirectionAt(int cell) => _direction[cell];

    /// <summary>X offset in cells of direction <paramref name="direction"/> (0-7).</summary>
    public static int OffsetX(int direction) => DirX[direction];

    /// <summary>Y offset in cells of direction <paramref name="direction"/> (0-7).</summary>
    public static int OffsetY(int direction) => DirY[direction];

    /// <summary>Builds a standalone field (allocates; tick code uses <see cref="FlowFieldCache"/> instead).</summary>
    public static FlowField Build(NavGrid grid, int targetCell)
    {
        var field = new FlowField(grid.Width, grid.Height);
        var steps = new byte[grid.Width * grid.Height];
        ComputeSteps(grid, steps);
        field.Build(grid, targetCell, new CellQueue(steps.Length), steps);
        return field;
    }

    /// <summary>The passable cell nearest to <paramref name="cell"/> by squared cell distance (ties: lowest y, then x); -1 if none.</summary>
    /// <remarks>A full scan, O(cells); only blocked targets pay for it.</remarks>
    public static int NearestPassable(NavGrid grid, int cell)
    {
        int w = grid.Width;
        int tx = cell % w, ty = cell / w;
        int best = -1;
        long bestD2 = long.MaxValue;
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (!grid.IsPassable(x, y)) continue;
                long dx = x - tx, dy = y - ty;
                long d2 = dx * dx + dy * dy;
                // Strict < keeps the first cell in (y, x) scan order on a tie.
                if (d2 < bestD2)
                {
                    bestD2 = d2;
                    best = y * w + x;
                }
            }
        }
        return best;
    }

    /// <summary>
    /// Fills one bit per direction for every cell: bit d is set when a unit may step from the cell in
    /// direction d (both cells passable and, for a diagonal, both side cells too). Depends only on the
    /// grid, so the cache computes it once per <see cref="NavGrid.Version"/> and shares it by all fields.
    /// </summary>
    internal static void ComputeSteps(NavGrid grid, byte[] steps)
    {
        int w = grid.Width;
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int mask = 0;
                if (grid.IsPassable(x, y))
                {
                    for (int d = 0; d < DirectionCount; d++)
                    {
                        int dx = DirX[d], dy = DirY[d];
                        if (!grid.IsPassable(x + dx, y + dy)) continue;
                        if ((d & 1) == 1 && !(grid.IsPassable(x + dx, y) && grid.IsPassable(x, y + dy))) continue;
                        mask |= 1 << d;
                    }
                }
                steps[y * w + x] = (byte)mask;
            }
        }
    }

    /// <summary>Rebuilds this field in place for a target using step masks from <see cref="ComputeSteps"/>; allocation-free.</summary>
    internal void Build(NavGrid grid, int requestedCell, CellQueue queue, byte[] steps)
    {
        if (grid.Width != Width || grid.Height != Height)
            throw new ArgumentException("grid size differs from the field's", nameof(grid));
        if ((uint)requestedCell >= (uint)_cost.Length)
            throw new ArgumentOutOfRangeException(nameof(requestedCell));
        Array.Fill(_cost, float.PositiveInfinity);
        Array.Fill(_direction, NoDirection);
        RequestedCell = requestedCell;
        Version = grid.Version;
        int w = Width;
        TargetCell = grid.IsPassable(requestedCell % w, requestedCell / w)
            ? requestedCell
            : NearestPassable(grid, requestedCell);
        if (TargetCell < 0) return;

        // Dijkstra from the target. Steps are symmetric (the same side cells gate a diagonal both
        // ways), so cost from the target equals cost to it. Each cell picks its direction when it
        // is settled: every neighbor that could start its shortest path is cheaper, so already
        // settled, and any unsettled neighbor costs at least as much as the cell itself, so it
        // can't win. That is the same pick a separate pass over the finished field would make.
        int[] offset = _offset;
        float[] cost = _cost;
        byte[] direction = _direction;
        cost[TargetCell] = 0f;
        queue.Reset(cost);
        queue.PushOrDecrease(TargetCell);
        int c;
        while ((c = queue.Pop()) >= 0)
        {
            int mask = steps[c];
            float cc = cost[c];
            float best = float.PositiveInfinity;
            int bestDir = NoDirection;
            for (int d = 0; mask != 0; d++, mask >>= 1)
            {
                if ((mask & 1) == 0) continue;
                int n = c + offset[d];
                float step = (d & 1) == 0 ? 1f : DiagonalCost;
                float cn = cost[n];
                if (cn + step < best)
                {
                    best = cn + step;
                    bestDir = d;
                }
                if (cc + step < cn)
                {
                    cost[n] = cc + step;
                    queue.PushOrDecrease(n);
                }
            }
            if (c != TargetCell) direction[c] = (byte)bestDir;
        }
    }
}
