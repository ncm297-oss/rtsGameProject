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

    internal FlowField(int width, int height)
    {
        Width = width;
        Height = height;
        _cost = new float[width * height];
        _direction = new byte[width * height];
    }

    /// <summary>Width in cells (same as the grid it was built on).</summary>
    public int Width { get; }

    /// <summary>Height in cells.</summary>
    public int Height { get; }

    /// <summary>The cell the field was asked to lead to (its cache key); -1 before the first build.</summary>
    public int RequestedCell { get; private set; } = -1;

    /// <summary>The cell the field actually leads to: <see cref="RequestedCell"/>, or the nearest passable cell if that is blocked; -1 if the grid has no passable cell.</summary>
    public int TargetCell { get; private set; } = -1;

    /// <summary><see cref="NavGrid.Version"/> at build time; a mismatch means the field is stale (it may miss a shorter way through newly opened cells, and cells opened since have no direction).</summary>
    public int Version { get; private set; }

    /// <summary><see cref="NavGrid.BlockVersion"/> at build time; while it matches, no cell the field reaches has been blocked since, so units may still follow it (M3-2b).</summary>
    public int BlockVersion { get; private set; }

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
    /// <remarks>
    /// Searches square rings outward and stops once a ring's nearest possible cell (r squared away)
    /// is farther than the best found, so the cost grows with the distance to passable ground, not
    /// with the map size.
    /// </remarks>
    public static int NearestPassable(NavGrid grid, int cell)
    {
        int w = grid.Width, h = grid.Height;
        int tx = cell % w, ty = cell / w;
        int maxR = Math.Max(Math.Max(tx, w - 1 - tx), Math.Max(ty, h - 1 - ty));
        int best = -1;
        long bestD2 = long.MaxValue;
        for (int r = 0; r <= maxR && (long)r * r <= bestD2; r++)
        {
            for (int y = Math.Max(ty - r, 0); y <= Math.Min(ty + r, h - 1); y++)
            {
                if (y == ty - r || y == ty + r)
                {
                    for (int x = Math.Max(tx - r, 0); x <= Math.Min(tx + r, w - 1); x++)
                        Consider(grid, x, y, tx, ty, ref best, ref bestD2);
                }
                else
                {
                    Consider(grid, tx - r, y, tx, ty, ref best, ref bestD2);
                    Consider(grid, tx + r, y, tx, ty, ref best, ref bestD2);
                }
            }
        }
        return best;
    }

    private static void Consider(NavGrid grid, int x, int y, int tx, int ty, ref int best, ref long bestD2)
    {
        // IsPassable is false off the grid, so ring sides past an edge drop out here.
        if (!grid.IsPassable(x, y)) return;
        long dx = x - tx, dy = y - ty;
        long d2 = dx * dx + dy * dy;
        int index = y * grid.Width + x;
        // Rings aren't visited in (y, x) order, so a tie compares the cell index explicitly.
        if (d2 < bestD2 || (d2 == bestD2 && index < best))
        {
            bestD2 = d2;
            best = index;
        }
    }

    /// <summary>
    /// Fills one bit per direction for every cell: bit d is set when a unit may step from the cell in
    /// direction d (both cells passable and, for a diagonal, both side cells too). Depends only on the
    /// grid, so the cache computes it once per <see cref="NavGrid.Version"/> and shares it by all fields.
    /// </summary>
    /// <remarks>
    /// Runs after every grid change (with felling, every tick), so it is written for Debug builds, where
    /// the JIT inlines nothing: inner cells slide a 3 x 3 window of open bits along each row, reading only
    /// the three new cells on the right from the flags array, instead of 25 bounds-checked
    /// <see cref="NavGrid.IsPassable"/> calls each (M3-2b: 1.5 ms a pass on 120 x 72 in Debug at first,
    /// 0.12 ms with direct reads, BUG-0082). The outer ring takes the general path, which bounds-checks.
    /// </remarks>
    internal static void ComputeSteps(NavGrid grid, byte[] steps)
    {
        int w = grid.Width, h = grid.Height;
        NavFlags[] flags = grid.Flags;
        for (int y = 0; y < h; y++)
        {
            if (y == 0 || y == h - 1 || w < 3)
            {
                for (int x = 0; x < w; x++) steps[y * w + x] = StepMask(grid, x, y);
                continue;
            }
            int row = y * w;
            steps[row] = StepMask(grid, 0, y);
            steps[row + w - 1] = StepMask(grid, w - 1, y);
            // Open bits of the window round cell (x, y): column x - 1 (nw, west, sw), x (n, c, s), x + 1 (ne, e, se).
            int up = row - w, down = row + w;
            bool nw = (flags[up] & NavFlags.Blocked) == 0, west = (flags[row] & NavFlags.Blocked) == 0, sw = (flags[down] & NavFlags.Blocked) == 0;
            bool n = (flags[up + 1] & NavFlags.Blocked) == 0, c = (flags[row + 1] & NavFlags.Blocked) == 0, s = (flags[down + 1] & NavFlags.Blocked) == 0;
            for (int x = 1; x < w - 1; x++)
            {
                bool ne = (flags[up + x + 1] & NavFlags.Blocked) == 0;
                bool e = (flags[row + x + 1] & NavFlags.Blocked) == 0;
                bool se = (flags[down + x + 1] & NavFlags.Blocked) == 0;
                // Same bits as StepMask: d is open if its cell is, a diagonal only if both side cells are too.
                int mask = 0;
                if (c)
                {
                    if (e) mask |= 1;
                    if (e && s && se) mask |= 2;
                    if (s) mask |= 4;
                    if (west && s && sw) mask |= 8;
                    if (west) mask |= 16;
                    if (west && n && nw) mask |= 32;
                    if (n) mask |= 64;
                    if (e && n && ne) mask |= 128;
                }
                steps[row + x] = (byte)mask;
                nw = n; west = c; sw = s;
                n = ne; c = e; s = se;
            }
        }
    }

    /// <summary>The step mask of one cell, read through bounds-checked grid queries (the definition <see cref="ComputeSteps"/> follows).</summary>
    internal static byte StepMask(NavGrid grid, int x, int y)
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
        return (byte)mask;
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
        BlockVersion = grid.BlockVersion;
        int w = Width;
        TargetCell = grid.IsPassable(requestedCell % w, requestedCell / w)
            ? requestedCell
            : NearestPassable(grid, requestedCell);
        if (TargetCell < 0) return;

        // Dijkstra from the target. Steps are symmetric (the same side cells gate a diagonal both
        // ways), so cost from the target equals cost to it. A cell's direction points back along the
        // step that gave it its final cost, the lowest direction number on a tie. Every neighbor that
        // can give that cost is cheaper by at least 1, so it is settled, and has relaxed the cell,
        // before the cell is: the same pick a pass over the finished field would make. Neither costs
        // nor directions depend on the order cells are settled in, so the order inside a bucket is free.
        float[] cost = _cost;
        byte[] direction = _direction;
        int[] entries = queue.Entries;
        int cells = queue.Cells;
        // The ring of three buckets: whole-number costs k (draining), k + 1 and k + 2, each a run of
        // entries starting at its base. Lengths stay in locals: the hot loop touches no queue field.
        int baseK = 0, baseK1 = cells, baseK2 = 2 * cells;
        int lengthK = 1, lengthK1 = 0, lengthK2 = 0;
        cost[TargetCell] = 0f;
        entries[0] = TargetCell;
        for (int k = 0; lengthK + lengthK1 + lengthK2 > 0; k++)
        {
            // Settling bucket k's cells pushes only into k + 1 and k + 2, so its length is fixed while it drains.
            for (int e = baseK, end = baseK + lengthK; e < end; e++)
            {
                int c = entries[e];
                float cc = cost[c];
                if ((int)cc != k) continue; // stale: the cell's cost fell into an earlier bucket and it was settled there
                // The eight directions are written out, one block each (east, then clockwise; offset,
                // step cost and the neighbor's direction back to c differ), rather than looped over a
                // table: tests run Debug builds, where the loop's bookkeeping was a third of a build
                // (BUG-0082). In every block: a cheaper way to n lowers its cost, points it back at c and
                // queues it unless it is already queued in that whole number; an equal one only wins the
                // tie for a lower direction number.
                int mask = steps[c];
                if ((mask & 1) != 0)
                {
                    int n = c + 1;
                    float through = cc + 1f;
                    float cn = cost[n];
                    if (through < cn)
                    {
                        cost[n] = through;
                        direction[n] = 4;
                        int kn = (int)through;
                        if (cn == float.PositiveInfinity || (int)cn != kn)
                        {
                            if (kn == k + 1) entries[baseK1 + lengthK1++] = n;
                            else entries[baseK2 + lengthK2++] = n;
                        }
                    }
                    else if (through == cn && 4 < direction[n]) direction[n] = 4;
                }
                if ((mask & 2) != 0)
                {
                    int n = c + w + 1;
                    float through = cc + DiagonalCost;
                    float cn = cost[n];
                    if (through < cn)
                    {
                        cost[n] = through;
                        direction[n] = 5;
                        int kn = (int)through;
                        if (cn == float.PositiveInfinity || (int)cn != kn)
                        {
                            if (kn == k + 1) entries[baseK1 + lengthK1++] = n;
                            else entries[baseK2 + lengthK2++] = n;
                        }
                    }
                    else if (through == cn && 5 < direction[n]) direction[n] = 5;
                }
                if ((mask & 4) != 0)
                {
                    int n = c + w;
                    float through = cc + 1f;
                    float cn = cost[n];
                    if (through < cn)
                    {
                        cost[n] = through;
                        direction[n] = 6;
                        int kn = (int)through;
                        if (cn == float.PositiveInfinity || (int)cn != kn)
                        {
                            if (kn == k + 1) entries[baseK1 + lengthK1++] = n;
                            else entries[baseK2 + lengthK2++] = n;
                        }
                    }
                    else if (through == cn && 6 < direction[n]) direction[n] = 6;
                }
                if ((mask & 8) != 0)
                {
                    int n = c + w - 1;
                    float through = cc + DiagonalCost;
                    float cn = cost[n];
                    if (through < cn)
                    {
                        cost[n] = through;
                        direction[n] = 7;
                        int kn = (int)through;
                        if (cn == float.PositiveInfinity || (int)cn != kn)
                        {
                            if (kn == k + 1) entries[baseK1 + lengthK1++] = n;
                            else entries[baseK2 + lengthK2++] = n;
                        }
                    }
                    else if (through == cn && 7 < direction[n]) direction[n] = 7;
                }
                if ((mask & 16) != 0)
                {
                    int n = c + -1;
                    float through = cc + 1f;
                    float cn = cost[n];
                    if (through < cn)
                    {
                        cost[n] = through;
                        direction[n] = 0;
                        int kn = (int)through;
                        if (cn == float.PositiveInfinity || (int)cn != kn)
                        {
                            if (kn == k + 1) entries[baseK1 + lengthK1++] = n;
                            else entries[baseK2 + lengthK2++] = n;
                        }
                    }
                    else if (through == cn && 0 < direction[n]) direction[n] = 0;
                }
                if ((mask & 32) != 0)
                {
                    int n = c + -w - 1;
                    float through = cc + DiagonalCost;
                    float cn = cost[n];
                    if (through < cn)
                    {
                        cost[n] = through;
                        direction[n] = 1;
                        int kn = (int)through;
                        if (cn == float.PositiveInfinity || (int)cn != kn)
                        {
                            if (kn == k + 1) entries[baseK1 + lengthK1++] = n;
                            else entries[baseK2 + lengthK2++] = n;
                        }
                    }
                    else if (through == cn && 1 < direction[n]) direction[n] = 1;
                }
                if ((mask & 64) != 0)
                {
                    int n = c + -w;
                    float through = cc + 1f;
                    float cn = cost[n];
                    if (through < cn)
                    {
                        cost[n] = through;
                        direction[n] = 2;
                        int kn = (int)through;
                        if (cn == float.PositiveInfinity || (int)cn != kn)
                        {
                            if (kn == k + 1) entries[baseK1 + lengthK1++] = n;
                            else entries[baseK2 + lengthK2++] = n;
                        }
                    }
                    else if (through == cn && 2 < direction[n]) direction[n] = 2;
                }
                if ((mask & 128) != 0)
                {
                    int n = c + -w + 1;
                    float through = cc + DiagonalCost;
                    float cn = cost[n];
                    if (through < cn)
                    {
                        cost[n] = through;
                        direction[n] = 3;
                        int kn = (int)through;
                        if (cn == float.PositiveInfinity || (int)cn != kn)
                        {
                            if (kn == k + 1) entries[baseK1 + lengthK1++] = n;
                            else entries[baseK2 + lengthK2++] = n;
                        }
                    }
                    else if (through == cn && 3 < direction[n]) direction[n] = 3;
                }
            }
            // Bucket k is empty: it becomes k + 3.
            int drained = baseK;
            baseK = baseK1;
            baseK1 = baseK2;
            baseK2 = drained;
            lengthK = lengthK1;
            lengthK1 = lengthK2;
            lengthK2 = 0;
        }
    }
}
