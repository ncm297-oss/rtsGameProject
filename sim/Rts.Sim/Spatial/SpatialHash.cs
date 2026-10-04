using System;
using System.Numerics;
using Rts.Sim.Entities;
using Rts.Sim.Map;

namespace Rts.Sim.Spatial;

/// <summary>Uniform grid of unit slots for neighbour queries (docs/03 "Spatial hash"), rebuilt from the unit store every tick.</summary>
/// <remarks>
/// Buckets are <see cref="BucketCells"/> x <see cref="BucketCells"/> nav cells. <see cref="Rebuild"/>
/// counting-sorts live slots into flat arrays sized once in the constructor, and copies each slot's
/// position and owner, so queries see the units as they were at the last rebuild. Units outside
/// the map clamp into the nearest edge bucket, so they are still found. Nothing allocates after
/// construction. Every query lists slots in ascending slot index, the same in every run.
/// <para>
/// Truncation: a query writes at most <c>results.Length</c> slots (the lowest matching slot
/// indices) and returns the total number of matches. A return value larger than
/// <c>results.Length</c> means the buffer was too small.
/// </para>
/// Derived state: rebuilt from <see cref="UnitStore"/>, so it does not feed the state hash.
/// </remarks>
public sealed class SpatialHash
{
    /// <summary>Bucket edge in nav cells.</summary>
    public const int BucketCells = 2;

    /// <summary>Bucket edge in meters (2 cells of <see cref="MapConstants.CellSize"/>).</summary>
    public const float BucketSize = BucketCells * MapConstants.CellSize;

    private readonly int[] _bucketStart; // entries of bucket b are [_bucketStart[b], _bucketStart[b + 1])
    private readonly int[] _cursor;      // fill position per bucket during the rebuild
    private readonly int[] _slotBucket;  // bucket of each slot at the last rebuild, -1 if dead
    private readonly int[] _entrySlot;   // slot of each entry, sorted by bucket then slot
    private readonly Vector2[] _entryPos;
    private readonly int[] _entryOwner;
    private readonly int[] _scratch;     // matches of the running query, before sorting

    /// <summary>Creates a hash for up to <paramref name="unitCapacity"/> slots over a map of the given size in cells.</summary>
    public SpatialHash(int unitCapacity, int mapWidthCells, int mapHeightCells)
    {
        if (unitCapacity < 1) throw new ArgumentOutOfRangeException(nameof(unitCapacity));
        if (mapWidthCells < 1) throw new ArgumentOutOfRangeException(nameof(mapWidthCells));
        if (mapHeightCells < 1) throw new ArgumentOutOfRangeException(nameof(mapHeightCells));
        BucketsX = (mapWidthCells + BucketCells - 1) / BucketCells;
        BucketsY = (mapHeightCells + BucketCells - 1) / BucketCells;
        int buckets = BucketsX * BucketsY;
        _bucketStart = new int[buckets + 1];
        _cursor = new int[buckets];
        _slotBucket = new int[unitCapacity];
        _entrySlot = new int[unitCapacity];
        _entryPos = new Vector2[unitCapacity];
        _entryOwner = new int[unitCapacity];
        _scratch = new int[unitCapacity];
        Array.Fill(_slotBucket, -1);
    }

    /// <summary>Buckets along x.</summary>
    public int BucketsX { get; }

    /// <summary>Buckets along y (world z).</summary>
    public int BucketsY { get; }

    /// <summary>Number of slots the hash can hold (the unit store capacity it was sized for).</summary>
    public int Capacity => _slotBucket.Length;

    /// <summary>Live units indexed by the last rebuild.</summary>
    public int Count { get; private set; }

    /// <summary>Re-indexes every live unit of <paramref name="units"/> (counting sort, slot order kept within a bucket).</summary>
    public void Rebuild(UnitStore units)
    {
        if (units.Capacity > Capacity)
            throw new ArgumentException($"store capacity {units.Capacity} exceeds hash capacity {Capacity}", nameof(units));
        Array.Clear(_bucketStart);
        bool[] alive = units.Alive;
        Vector2[] position = units.Position;
        for (int s = 0; s < units.Capacity; s++)
        {
            if (!alive[s])
            {
                _slotBucket[s] = -1;
                continue;
            }
            int b = BucketY(position[s].Y) * BucketsX + BucketX(position[s].X);
            _slotBucket[s] = b;
            _bucketStart[b + 1]++;
        }
        for (int b = 0; b < _cursor.Length; b++)
        {
            _bucketStart[b + 1] += _bucketStart[b];
            _cursor[b] = _bucketStart[b];
        }
        int[] owner = units.Owner;
        for (int s = 0; s < units.Capacity; s++)
        {
            int b = _slotBucket[s];
            if (b < 0) continue;
            int e = _cursor[b]++;
            _entrySlot[e] = s;
            _entryPos[e] = position[s];
            _entryOwner[e] = owner[s];
        }
        Count = _bucketStart[_cursor.Length];
    }

    /// <summary>Slots whose point lies within <paramref name="radius"/> meters of <paramref name="center"/>; returns the match count (see remarks on truncation).</summary>
    /// <remarks>
    /// Match rule, in float: <c>dx * dx + dy * dy &lt;= radius * radius</c> with <c>dx = p.X - center.X</c>.
    /// A negative or NaN radius, or a non-finite center, matches nothing.
    /// </remarks>
    public int QueryRadius(Vector2 center, float radius, Span<int> results)
    {
        if (!(radius >= 0f) || !float.IsFinite(center.X) || !float.IsFinite(center.Y)) return 0;
        float r2 = radius * radius;
        GetRadiusRange(center, radius, out int x0, out int y0, out int x1, out int y1);
        int n = 0;
        for (int by = y0; by <= y1; by++)
        {
            for (int bx = x0; bx <= x1; bx++)
            {
                int b = by * BucketsX + bx;
                for (int e = _bucketStart[b]; e < _bucketStart[b + 1]; e++)
                {
                    float dx = _entryPos[e].X - center.X, dy = _entryPos[e].Y - center.Y;
                    if (dx * dx + dy * dy <= r2) _scratch[n++] = _entrySlot[e];
                }
            }
        }
        return Emit(n, results);
    }

    /// <summary>Slots whose point lies in the rectangle spanned by two corners, edges included; returns the match count (see remarks on truncation).</summary>
    /// <remarks>Corners may come in any order. A NaN corner matches nothing.</remarks>
    public int QueryRect(Vector2 min, Vector2 max, Span<int> results)
    {
        if (float.IsNaN(min.X) || float.IsNaN(min.Y) || float.IsNaN(max.X) || float.IsNaN(max.Y)) return 0;
        float lx = Math.Min(min.X, max.X), hx = Math.Max(min.X, max.X);
        float ly = Math.Min(min.Y, max.Y), hy = Math.Max(min.Y, max.Y);
        // Division and flooring are monotonic, so these buckets hold every point inside the rectangle.
        int x0 = BucketX(lx), x1 = BucketX(hx), y0 = BucketY(ly), y1 = BucketY(hy);
        int n = 0;
        for (int by = y0; by <= y1; by++)
        {
            for (int bx = x0; bx <= x1; bx++)
            {
                int b = by * BucketsX + bx;
                for (int e = _bucketStart[b]; e < _bucketStart[b + 1]; e++)
                {
                    Vector2 p = _entryPos[e];
                    if (p.X >= lx && p.X <= hx && p.Y >= ly && p.Y <= hy) _scratch[n++] = _entrySlot[e];
                }
            }
        }
        return Emit(n, results);
    }

    /// <summary>The closest unit within <paramref name="radius"/> not owned by <paramref name="player"/>; ties go to the lowest slot. False if there is none.</summary>
    /// <remarks>Uses the <see cref="QueryRadius"/> match rule and compares squared float distances. Any other owner counts as an enemy until teams exist.</remarks>
    public bool NearestEnemy(Vector2 center, float radius, int player, out int slot)
    {
        slot = -1;
        if (!(radius >= 0f) || !float.IsFinite(center.X) || !float.IsFinite(center.Y)) return false;
        float best = radius * radius;
        GetRadiusRange(center, radius, out int x0, out int y0, out int x1, out int y1);
        for (int by = y0; by <= y1; by++)
        {
            for (int bx = x0; bx <= x1; bx++)
            {
                int b = by * BucketsX + bx;
                for (int e = _bucketStart[b]; e < _bucketStart[b + 1]; e++)
                {
                    if (_entryOwner[e] == player) continue;
                    float dx = _entryPos[e].X - center.X, dy = _entryPos[e].Y - center.Y;
                    float d2 = dx * dx + dy * dy;
                    if (d2 > best) continue;
                    // Buckets are not visited in slot order, so ties need the explicit slot check.
                    if (slot < 0 || d2 < best || _entrySlot[e] < slot)
                    {
                        best = d2;
                        slot = _entrySlot[e];
                    }
                }
            }
        }
        return slot >= 0;
    }

    /// <summary>Bucket column of a world x in meters; outside the map (or NaN) clamps to the nearest edge column.</summary>
    public int BucketX(float x) => Clamp(x / BucketSize, BucketsX);

    /// <summary>Bucket row of a world y (z) in meters; outside the map (or NaN) clamps to the nearest edge row.</summary>
    public int BucketY(float y) => Clamp(y / BucketSize, BucketsY);

    private static int Clamp(float f, int count)
    {
        // Written so NaN lands in bucket 0; non-negative values cast by flooring.
        if (!(f >= 0f)) return 0;
        return f < count ? (int)f : count - 1;
    }

    // Buckets touching the circle's bounding square, padded so rounding in the float match rule
    // (which can accept a point a few ulps past the radius) never misses a bucket.
    private void GetRadiusRange(Vector2 center, float radius, out int x0, out int y0, out int x1, out int y1)
    {
        float padX = radius + (Math.Abs(center.X) + radius) * 1e-5f + 1e-3f;
        float padY = radius + (Math.Abs(center.Y) + radius) * 1e-5f + 1e-3f;
        x0 = BucketX(center.X - padX);
        x1 = BucketX(center.X + padX);
        y0 = BucketY(center.Y - padY);
        y1 = BucketY(center.Y + padY);
    }

    // Matches arrive bucket by bucket; sorting gives the documented ascending slot order.
    private int Emit(int n, Span<int> results)
    {
        Span<int> found = _scratch.AsSpan(0, n);
        found.Sort();
        int write = Math.Min(n, results.Length);
        found.Slice(0, write).CopyTo(results);
        return n;
    }
}
