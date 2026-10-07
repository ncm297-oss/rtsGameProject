namespace Rts.Sim.Pathfinding;

/// <summary>Storage for Dijkstra's priority queue of grid cells: a bucket queue on the whole-number part of each cell's cost. Sized once; never allocates after construction.</summary>
/// <remarks>
/// Every step costs at least 1, so a cell whose cost has integer part k can only push a neighbor
/// to k + 1 or more: cells sharing a bucket can't improve each other and may be settled in any
/// order (Dial's algorithm with bucket width = cheapest step), and Dijkstra still gives exact
/// shortest costs. A cell settled from bucket k pushes neighbors only into buckets k + 1 and k + 2
/// (a step costs less than 2), so live buckets never span more than three whole numbers and three
/// buckets reused in a ring are enough: 12 bytes a cell, what the earlier linked-list queue took.
/// <para>
/// Deletion is lazy: a cell whose cost drops into a lower whole number is pushed again and its old
/// entry is skipped when popped (its cost no longer matches the bucket). A cell's cost only falls, so it
/// enters each bucket at most once per lap and a bucket never holds more entries than there are cells.
/// <see cref="FlowField"/> reads and writes the entries inline in its build loop, keeping the bucket
/// lengths in locals, instead of calling methods: tests run Debug builds, where the JIT inlines nothing,
/// and the push / pop calls of the earlier linked-list queue were a quarter of a field's build time
/// (M3-2b, BUG-0082).
/// </para>
/// </remarks>
internal sealed class CellQueue
{
    /// <summary>Number of buckets in the ring: the span of live whole-number costs (k to k + 2).</summary>
    public const int BucketCount = 3;

    /// <summary>Creates storage for a grid of <paramref name="cells"/> cells.</summary>
    public CellQueue(int cells)
    {
        Cells = cells;
        Entries = new int[BucketCount * cells];
    }

    /// <summary>Cells in the grid: the room each bucket has.</summary>
    public int Cells { get; }

    /// <summary>Bucket b's entries start at <c>Entries[b * Cells]</c>; the build loop tracks how many each holds.</summary>
    public int[] Entries { get; }
}
