using System;
using System.Numerics;
using Rts.Sim.Entities;
using Rts.Sim.Map;

namespace Rts.Sim.Spatial;

/// <summary>Uniform grid of unit slots for neighbour queries (docs/03 "Spatial hash"), rebuilt from the unit store every tick.</summary>
/// <remarks>
/// Buckets are <see cref="BucketCells"/> x <see cref="BucketCells"/> nav cells. <see cref="Rebuild"/>
/// counting-sorts live slots into flat arrays sized once in the constructor, and copies each slot's
/// position and side (M6-S4: its owner's, <see cref="SideOf"/>), so queries see the units as they were at the last rebuild. Units outside
/// the map clamp into the nearest edge bucket, so they are still found. Nothing allocates after
/// construction. Every query lists slots in ascending slot index, the same in every run.
/// <para>
/// Truncation: a query writes at most <c>results.Length</c> slots (the lowest matching slot
/// indices) and returns the total number of matches. A return value larger than
/// <c>results.Length</c> means the buffer was too small.
/// </para>
/// Derived state: rebuilt from <see cref="UnitStore"/>, so it does not feed the state hash.
/// </remarks>
public sealed partial class SpatialHash
{
    /// <summary>Bucket edge in nav cells.</summary>
    public const int BucketCells = 2;

    /// <summary>Bucket edge in meters (2 cells of <see cref="MapConstants.CellSize"/>).</summary>
    public const float BucketSize = BucketCells * MapConstants.CellSize;

    private readonly int[] _bucketStart; // entries of bucket b are [_bucketStart[b], _bucketStart[b + 1])
    // Per bucket: the fill position during the rebuild; after it (M4-1), the bucket's owner code for QueryEnemies: the one
    // side (M6-S4: an owner and its allies) of all its units, EmptyBucket or MixedBucket. The fill positions are spent by
    // then, so this costs no memory.
    private readonly int[] _cursor;
    private const int EmptyBucket = int.MinValue, MixedBucket = int.MinValue + 1; // far from any player index
    private readonly int[] _slotBucket;  // bucket of each slot at the last rebuild, -1 if dead
    private readonly int[] _entrySlot;   // slot of each entry, sorted by bucket then slot
    private readonly Vector2[] _entryPos;
    private readonly int[] _entrySide;   // the side of each entry's owner (SideOf): the owner itself in a free-for-all
    private readonly int[] _scratch;     // matches of the running query, before sorting
    private readonly int[] _sideCount = new int[OwnerSlots]; // live units per side (sides past the last slot share it)
    private const int OwnerSlots = 64;
    // M6-S4: per player, its side: the lowest player it is allied with (itself on team 0). Derived once from the world's
    // alliance table (taken by reference at construction, never written); empty for a hash built without one, where every
    // owner is its own side.
    private readonly int[] _sideOf;
    // Whether _cursor holds owner codes: not when one side has every unit (a one-player crowd), where the counts answer
    // every query and the pass is skipped.
    private bool _ownerCodes;

    /// <summary>Creates a hash for up to <paramref name="unitCapacity"/> slots over a map of the given size in cells, every owner a side of its own.</summary>
    public SpatialHash(int unitCapacity, int mapWidthCells, int mapHeightCells) : this(unitCapacity, mapWidthCells, mapHeightCells, null, 0)
    {
    }

    /// <summary>
    /// M6-S4: a hash whose enemy queries skip a player's allies too: <paramref name="allied"/> is the world's flat alliance
    /// table of <paramref name="players"/> players (<c>allied[a * players + b]</c>, <see cref="World.AreAllied"/>), or null
    /// for none (every owner its own side).
    /// </summary>
    public SpatialHash(int unitCapacity, int mapWidthCells, int mapHeightCells, bool[]? allied, int players)
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
        _entrySide = new int[unitCapacity];
        _scratch = new int[unitCapacity];
        Array.Fill(_slotBucket, -1);
        if (allied == null || players < 1)
        {
            _sideOf = Array.Empty<int>();
            return;
        }
        if (allied.Length != players * players) throw new ArgumentException($"an alliance table of {allied.Length} entries for {players} players", nameof(allied));
        _sideOf = new int[players];
        for (int a = 0; a < players; a++)
        {
            int side = a;
            for (int b = 0; b < a && side == a; b++)
                if (allied[a * players + b]) side = b;
            _sideOf[a] = side;
        }
    }

    /// <summary>
    /// M6-S4: <paramref name="player"/>'s side, what the enemy queries compare: the lowest player allied with it (itself on
    /// no team, so in a free-for-all every owner is its own side); an owner outside the table is its own side.
    /// </summary>
    public int SideOf(int player) => (uint)player < (uint)_sideOf.Length ? _sideOf[player] : player;

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
        Array.Clear(_sideCount);
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
            int o = SideOf(owner[s]);
            _entrySide[e] = o;
            _sideCount[(uint)o < OwnerSlots - 1 ? o : OwnerSlots - 1]++; // sides past 62 share the last count
        }
        Count = _bucketStart[_cursor.Length];
        // The fill positions are spent: _cursor now holds each bucket's side code (M4-1), unless one side has them all.
        _ownerCodes = Count > 0 && OthersThanSide(_entrySide[0]) > 0;
        if (!_ownerCodes) return;
        Array.Fill(_cursor, EmptyBucket);
        for (int e = 0; e < Count; e++)
        {
            int b = _slotBucket[_entrySlot[e]], o = _entrySide[e], was = _cursor[b];
            _cursor[b] = was == EmptyBucket || was == o ? o : MixedBucket;
        }
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

    /// <summary>The closest unit within <paramref name="radius"/> of an enemy of <paramref name="player"/> (not its own; M6-S4: nor an ally's); ties go to the lowest slot. False if there is none.</summary>
    /// <remarks>Uses the <see cref="QueryRadius"/> match rule and compares squared float distances.</remarks>
    public bool NearestEnemy(Vector2 center, float radius, int player, out int slot)
    {
        slot = -1;
        if (!(radius >= 0f) || !float.IsFinite(center.X) || !float.IsFinite(center.Y)) return false;
        int side = SideOf(player);
        float best = radius * radius;
        GetRadiusRange(center, radius, out int x0, out int y0, out int x1, out int y1);
        for (int by = y0; by <= y1; by++)
        {
            for (int bx = x0; bx <= x1; bx++)
            {
                int b = by * BucketsX + bx;
                for (int e = _bucketStart[b]; e < _bucketStart[b + 1]; e++)
                {
                    if (_entrySide[e] == side) continue;
                    float dx = _entryPos[e].X - center.X, dy = _entryPos[e].Y - center.Y;
                    float d2 = dx * dx + dy * dy;
                    // Written so a NaN distance is skipped, as QueryRadius's "d2 <= r2" skips it (BUG-0016).
                    if (!(d2 <= best)) continue;
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

    /// <summary>
    /// Slots of <paramref name="player"/>'s enemies (not its own; M6-S4: nor its allies') whose point lies within
    /// <paramref name="radius"/> meters of <paramref name="center"/> (the <see cref="QueryRadius"/> match rule), in bucket
    /// order, <b>not</b> sorted by slot; returns the match count (truncation as in <see cref="QueryRadius"/>). Buckets
    /// holding only the player's side's units are skipped on one compare (M4-1), so a scan with no enemy near costs a few
    /// bucket reads, and one with no enemy unit anywhere costs one compare.
    /// </summary>
    public int QueryEnemies(Vector2 center, float radius, int player, Span<int> results)
    {
        if (!(radius >= 0f) || !float.IsFinite(center.X) || !float.IsFinite(center.Y)) return 0;
        // No enemy on the map: nothing to look at (a one-player crowd's scans cost this test).
        int side = SideOf(player);
        if (OthersThanSide(side) == 0) return 0;
        float r2 = radius * radius;
        GetRadiusRange(center, radius, out int x0, out int y0, out int x1, out int y1);
        int n = 0;
        for (int by = y0; by <= y1; by++)
        {
            for (int bx = x0; bx <= x1; bx++)
            {
                int b = by * BucketsX + bx;
                if (_ownerCodes)
                {
                    int o = _cursor[b];
                    if (o == EmptyBucket || o == side) continue; // empty, or only the player's side's units
                }
                for (int e = _bucketStart[b]; e < _bucketStart[b + 1]; e++)
                {
                    if (_entrySide[e] == side) continue;
                    float dx = _entryPos[e].X - center.X, dy = _entryPos[e].Y - center.Y;
                    if (!(dx * dx + dy * dy <= r2)) continue;
                    if (n < results.Length) results[n] = _entrySlot[e];
                    n++;
                }
            }
        }
        return n;
    }

    /// <summary>Live units at the last rebuild of <paramref name="player"/>'s enemies (M4-1: not its own; M6-S4: nor its allies'); for sides past 62 an upper bound.</summary>
    public int OthersThan(int player) => OthersThanSide(SideOf(player));

    private int OthersThanSide(int side) => (uint)side < OwnerSlots - 1 ? Count - _sideCount[side] : Count;

    /// <summary>Test seam (M6-S4): whether <see cref="QueryEnemies"/> skips bucket <paramref name="bucket"/> for <paramref name="player"/> on its code alone (empty, or holding only the player's side's units).</summary>
    internal bool SkipsBucketFor(int bucket, int player)
    {
        if (!_ownerCodes) return OthersThan(player) == 0 || _bucketStart[bucket] == _bucketStart[bucket + 1];
        int o = _cursor[bucket];
        return o == EmptyBucket || o == SideOf(player);
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
