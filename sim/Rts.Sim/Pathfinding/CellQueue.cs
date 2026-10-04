using System;

namespace Rts.Sim.Pathfinding;

/// <summary>Dijkstra's priority queue of grid cells: a bucket queue on the whole-number part of each cell's cost. Sized once; never allocates after construction.</summary>
/// <remarks>
/// Every step costs at least 1, so a cell whose cost has integer part k can only push a neighbor
/// to k + 1 or more: cells sharing a bucket can't improve each other and may be settled in any
/// order (Dial's algorithm with bucket width = cheapest step). The queue pops buckets in cost
/// order and cells within one bucket last-in first-out, and Dijkstra still gives exact shortest
/// costs. Queued costs never span more than three whole numbers (settled cost + at most one
/// diagonal), so four buckets reused in a ring are enough. Each bucket is a doubly linked list
/// threaded through per-cell arrays, so lowering a queued cell's cost is O(1). Chosen over a
/// binary heap because tests run Debug builds, where heap sifting made a 128 x 128 field cost 2 ms.
/// </remarks>
internal sealed class CellQueue
{
    private const int BucketCount = 4; // must exceed the span of live costs (< 3); a power of two for the mask

    private readonly int[] _next;
    private readonly int[] _prev;
    private readonly int[] _bucketOf; // bucket a cell is queued in, -1 when not queued
    private readonly int[] _head = new int[BucketCount];
    private float[] _keys = Array.Empty<float>();
    private int _count;
    private int _current; // whole-number cost of the bucket being drained

    public CellQueue(int cells)
    {
        _next = new int[cells];
        _prev = new int[cells];
        _bucketOf = new int[cells];
        Array.Fill(_bucketOf, -1);
        Array.Fill(_head, -1);
    }

    /// <summary>Empties the queue and makes it order cells by <paramref name="keys"/>.</summary>
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

    /// <summary>Queues a cell, or moves it after its key dropped. A key must be finite and no lower than the bucket being drained.</summary>
    public void PushOrDecrease(int cell)
    {
        int b = (int)_keys[cell] & (BucketCount - 1);
        int old = _bucketOf[cell];
        if (old == b) return; // same bucket: order inside a bucket doesn't matter
        if (old >= 0) Unlink(cell, old);
        else _count++;
        int head = _head[b];
        _next[cell] = head;
        _prev[cell] = -1;
        if (head >= 0) _prev[head] = cell;
        _head[b] = cell;
        _bucketOf[cell] = b;
    }

    /// <summary>Removes and returns a cell of the cheapest non-empty bucket; -1 when empty.</summary>
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
