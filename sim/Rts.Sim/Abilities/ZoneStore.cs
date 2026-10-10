using System;
using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Determinism;

namespace Rts.Sim.Abilities;

/// <summary>
/// The live zones (M4-4b-2, docs/03 "Implementation (M4-4b-2)"): flat structure-of-arrays storage sized once from
/// <see cref="SimConfig.ZoneCapacity"/>. A zone is a circle on the ground left by an ability's <c>createZone</c> effect:
/// owner, centre, ability and ticks remaining are its state (hashed); its radius, statuses and vision flag come from the
/// ability's data. No handles: a zone takes the lowest free slot and every loop runs in slot order. The read-only spans
/// and the per-slot reads are the view's (M4-V6c); only the sim writes.
/// </summary>
public sealed class ZoneStore
{
    /// <summary>Default <see cref="SimConfig.ZoneCapacity"/>: far above what a match casts at once (a 12 s zone on a 45 s cooldown is about a quarter of the casters).</summary>
    public const int DefaultCapacity = 64;

    private readonly GameData _data;
    private readonly bool[] _alive;
    private readonly int[] _owner;
    private readonly Vector2[] _center;
    private readonly int[] _abilityId;
    private readonly int[] _ticksRemaining;

    // Derived from the ability id (not hashed): the radius and whether the zone blocks vision.
    private readonly float[] _radius;
    private readonly bool[] _blocksVision;

    // BUG-0360: per (slot, player), the fog update's record of a blocking zone's box (the cells whose centres may lie within
    // its radius, FogStore.ZoneBox): which cells a unit of that player standing inside this zone saw, and which cells that
    // player's circles covered, zones aside. Written by the fog update (FogStore), cleared when a zone is made or freed;
    // hashed with the zone (they follow from the positions at the last update, as the fog's visible bits do).
    private readonly int _players;
    private readonly int _boxSide;
    private readonly int _boxWords;
    private readonly ulong[] _seenInside;
    private readonly ulong[] _covered;

    /// <summary>Every live slot is below this (a loop bound); reset when the store empties. Not state.</summary>
    private int _end;

    /// <summary>Creates an empty store of <paramref name="capacity"/> slots for <paramref name="players"/> players' zones of <paramref name="data"/>'s abilities.</summary>
    internal ZoneStore(int capacity, int players, GameData data)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _data = data;
        _alive = new bool[capacity];
        _owner = new int[capacity];
        _center = new Vector2[capacity];
        _abilityId = new int[capacity];
        _ticksRemaining = new int[capacity];
        _radius = new float[capacity];
        _blocksVision = new bool[capacity];
        // The widest box any zone in the data can have: a radius r spans at most floor(2r / cell) + 2 cells a side.
        float maxRadius = 0f;
        foreach (AbilityDef a in data.Abilities)
            if (a.ZoneEffect >= 0 && a.Radius > maxRadius) maxRadius = a.Radius;
        _players = players;
        _boxSide = maxRadius > 0f ? (int)(2f * maxRadius / Map.MapConstants.CellSize) + 2 : 0;
        _boxWords = (_boxSide * _boxSide + 63) / 64;
        _seenInside = new ulong[capacity * players * _boxWords];
        _covered = new ulong[capacity * players * _boxWords];
    }

    /// <summary>Cells along a side of the fog record of a zone's box (BUG-0360): a cell (x, y) of the box is bit <c>(y - y0) * BoxSide + (x - x0)</c>.</summary>
    internal int BoxSide => _boxSide;

    /// <summary>Whether bit <paramref name="bit"/> of slot <paramref name="slot"/>'s record for <paramref name="player"/> is set: <paramref name="covered"/> picks the covered cells, else the cells seen from inside.</summary>
    internal bool FogBit(bool covered, int slot, int player, int bit)
    {
        int at = (slot * _players + player) * _boxWords + (bit >> 6);
        return ((covered ? _covered : _seenInside)[at] & (1UL << (bit & 63))) != 0;
    }

    /// <summary>Sets bit <paramref name="bit"/> of slot <paramref name="slot"/>'s record for <paramref name="player"/> (the fog update's).</summary>
    internal void SetFogBit(bool covered, int slot, int player, int bit)
    {
        int at = (slot * _players + player) * _boxWords + (bit >> 6);
        (covered ? _covered : _seenInside)[at] |= 1UL << (bit & 63);
    }

    /// <summary>Clears slot <paramref name="slot"/>'s records for <paramref name="player"/> (the fog update's, before it writes them).</summary>
    internal void ClearFogBits(int slot, int player)
    {
        int at = (slot * _players + player) * _boxWords;
        Array.Clear(_seenInside, at, _boxWords);
        Array.Clear(_covered, at, _boxWords);
    }

    /// <summary>Number of slots.</summary>
    public int Capacity => _alive.Length;

    /// <summary>Live zones.</summary>
    public int Count { get; private set; }

    /// <summary>Live zones that block vision (derived from <see cref="Count"/>'s zones and their abilities; not hashed).</summary>
    public int BlockerCount { get; private set; }

    /// <summary>Every live slot is below this: a loop over the zones may stop here.</summary>
    internal int End => _end;

    /// <summary>Per slot: a live zone. Read the other fields only where this is true.</summary>
    public ReadOnlySpan<bool> Alive => _alive;

    /// <summary>Per slot: the player whose unit cast it (its statuses spare or take that player's units by the ability's <c>affects</c>; it never hides its cells from that player).</summary>
    public ReadOnlySpan<int> Owner => _owner;

    /// <summary>Per slot: the centre (x, z) in meters, the cast point.</summary>
    public ReadOnlySpan<Vector2> Center => _center;

    /// <summary>Per slot: the ability that left it (index into <see cref="GameData.Abilities"/>; its display name, statuses and radius).</summary>
    public ReadOnlySpan<int> AbilityId => _abilityId;

    /// <summary>Per slot: ticks it has left; it is freed in phase 5 of the tick this would reach 0 (1 between ticks: its last tick is the next one).</summary>
    public ReadOnlySpan<int> TicksRemaining => _ticksRemaining;

    /// <summary>Slot <paramref name="slot"/>'s radius in meters (its ability's <see cref="AbilityDef.Radius"/>).</summary>
    public float Radius(int slot) => _radius[slot];

    /// <summary>Whether slot <paramref name="slot"/>'s zone hides its cells from the other players' units outside it (its effect's <see cref="AbilityEffect.BlocksVision"/>).</summary>
    public bool BlocksVision(int slot) => _blocksVision[slot];

    /// <summary>
    /// A zone of <paramref name="ability"/> (which has a <c>createZone</c> effect) for <paramref name="owner"/> at
    /// <paramref name="center"/>, lasting the ability's duration, in the lowest free slot. Returns the slot, or -1 when the
    /// store is full (the zone is not made: docs/03 "Implementation (M4-4b-2)").
    /// </summary>
    internal int Add(int owner, AbilityDef ability, Vector2 center)
    {
        int k = Array.IndexOf(_alive, false);
        if (k < 0) return -1;
        _alive[k] = true;
        _owner[k] = owner;
        _center[k] = center;
        _abilityId[k] = ability.Id;
        _ticksRemaining[k] = Math.Max(1, ability.DurationTicks);
        _radius[k] = ability.Radius;
        _blocksVision[k] = ability.Effects[ability.ZoneEffect].BlocksVision;
        ClearFogRecord(k); // no fog update has seen it yet: nothing inside it is seen until one has
        Count++;
        if (_blocksVision[k]) BlockerCount++;
        if (k >= _end) _end = k + 1;
        return k;
    }

    /// <summary>Counts slot <paramref name="slot"/>'s life down one tick; true when it has ticks left, false when it just ended (the caller frees it).</summary>
    internal bool CountDown(int slot) => --_ticksRemaining[slot] > 0;

    /// <summary>Frees live slot <paramref name="slot"/> (its time is up).</summary>
    internal void Free(int slot)
    {
        if (_blocksVision[slot]) BlockerCount--;
        _alive[slot] = false;
        _owner[slot] = 0;
        _center[slot] = default;
        _abilityId[slot] = 0;
        _ticksRemaining[slot] = 0;
        _radius[slot] = 0f;
        _blocksVision[slot] = false;
        ClearFogRecord(slot);
        if (--Count == 0) _end = 0;
    }

    /// <summary>Clears slot <paramref name="slot"/>'s fog records for every player.</summary>
    private void ClearFogRecord(int slot)
    {
        int n = _players * _boxWords;
        Array.Clear(_seenInside, slot * n, n);
        Array.Clear(_covered, slot * n, n);
    }

    /// <summary>
    /// Whether <paramref name="p"/> (m) is inside live slot <paramref name="slot"/>'s circle: within its radius of the centre
    /// (the edge counts). The rule for a unit's center (its statuses, whether it views from inside).
    /// </summary>
    internal bool Contains(int slot, Vector2 p) => Vector2.DistanceSquared(p, _center[slot]) <= _radius[slot] * _radius[slot];

    /// <summary>
    /// Mixes the zones into a state hash, only when any is live (so a match without one hashes as before M4-4b-2): the
    /// count, then every live slot in slot order with its owner, centre, ability and ticks remaining, and (BUG-0360) the fog
    /// update's records of it, every player's.
    /// </summary>
    internal void AddToHash(ref StateHasher h)
    {
        if (Count == 0) return;
        h.Add(Count);
        int n = _players * _boxWords;
        for (int k = 0; k < _end; k++)
        {
            if (!_alive[k]) continue;
            h.Add(k);
            h.Add(_owner[k]);
            h.Add(_center[k]);
            h.Add(_abilityId[k]);
            h.Add(_ticksRemaining[k]);
            for (int w = k * n, end = w + n; w < end; w++)
            {
                h.AddWord(_seenInside[w]);
                h.AddWord(_covered[w]);
            }
        }
    }
}
