using Rts.Sim.Map;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA reference (M3-2b re-check, BUG-0082): the flow-field build exactly as it was at a80c143, before the
/// lazy-deletion ring and the unrolled relax loop. Copied verbatim (the linked-list bucket queue, the
/// settle-time direction pick, the bounds-checked step masks) so the rewrite can be compared bit for bit.
/// Never "fix" this file: its whole value is that it is the old code.
/// </summary>
internal static class LegacyFlowFieldA80c143
{
    private const byte NoDirection = 255;
    private const float DiagonalCost = 1.41421f;
    private static readonly int[] DirX = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private static readonly int[] DirY = { 0, 1, 1, 1, 0, -1, -1, -1 };

    /// <summary>a80c143's ComputeSteps: every cell through bounds-checked IsPassable calls.</summary>
    public static void ComputeSteps(NavGrid grid, byte[] steps)
    {
        int w = grid.Width;
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int mask = 0;
                if (grid.IsPassable(x, y))
                {
                    for (int d = 0; d < 8; d++)
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

    /// <summary>a80c143's Build: returns (cost, direction, resolved target). <paramref name="queue"/> may be reused across calls.</summary>
    public static (float[] Cost, byte[] Dir, int Target) Build(NavGrid grid, int requestedCell, Queue queue, byte[] steps)
    {
        int w = grid.Width;
        var cost = new float[w * grid.Height];
        var direction = new byte[w * grid.Height];
        Array.Fill(cost, float.PositiveInfinity);
        Array.Fill(direction, NoDirection);
        int target = grid.IsPassable(requestedCell % w, requestedCell / w)
            ? requestedCell
            : Rts.Sim.Pathfinding.FlowField.NearestPassable(grid, requestedCell);
        if (target < 0) return (cost, direction, target);
        var offset = new int[8];
        for (int d = 0; d < 8; d++) offset[d] = DirY[d] * w + DirX[d];
        cost[target] = 0f;
        queue.Reset(cost);
        queue.PushOrDecrease(target);
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
            if (c != target) direction[c] = (byte)bestDir;
        }
        return (cost, direction, target);
    }

    /// <summary>a80c143's CellQueue, verbatim.</summary>
    public sealed class Queue
    {
        private const int BucketCount = 4;
        private readonly int[] _next;
        private readonly int[] _prev;
        private readonly int[] _bucketOf;
        private readonly int[] _head = new int[BucketCount];
        private float[] _keys = Array.Empty<float>();
        private int _count;
        private int _current;

        public Queue(int cells)
        {
            _next = new int[cells];
            _prev = new int[cells];
            _bucketOf = new int[cells];
            Array.Fill(_bucketOf, -1);
            Array.Fill(_head, -1);
        }

        public void Reset(float[] keys)
        {
            for (int b = 0; b < BucketCount; b++)
            {
                for (int c = _head[b]; c >= 0; c = _next[c])
                    _bucketOf[c] = -1;
                _head[b] = -1;
            }
            _count = 0;
            _current = 0;
            _keys = keys;
        }

        public void PushOrDecrease(int cell)
        {
            int b = (int)_keys[cell] & (BucketCount - 1);
            int old = _bucketOf[cell];
            if (old == b) return;
            if (old >= 0) Unlink(cell, old);
            else _count++;
            int head = _head[b];
            _next[cell] = head;
            _prev[cell] = -1;
            if (head >= 0) _prev[head] = cell;
            _head[b] = cell;
            _bucketOf[cell] = b;
        }

        public int Pop()
        {
            if (_count == 0) return -1;
            int b = _current & (BucketCount - 1);
            while (_head[b] < 0)
            {
                _current++;
                b = _current & (BucketCount - 1);
            }
            int cell = _head[b];
            Unlink(cell, b);
            _bucketOf[cell] = -1;
            _count--;
            return cell;
        }

        private void Unlink(int cell, int bucket)
        {
            int next = _next[cell], prev = _prev[cell];
            if (prev >= 0) _next[prev] = next;
            else _head[bucket] = next;
            if (next >= 0) _prev[next] = prev;
        }
    }
}
