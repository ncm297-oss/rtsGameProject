using System;
using System.Collections.Immutable;
using Rts.Sim.Data;
using Rts.Sim.Vision;

namespace Rts.Sim.ViewApi;

/// <summary>
/// The resource nodes as the local player last saw them (M4-VH2, BUG-0281 item 1; docs/02 "Fog of war": explored ground
/// shows last-seen state, not live changes). A per-slot copy of the resource store's <c>Alive</c>, <c>TypeId</c> and
/// <c>Cell</c> that follows the store only where the player sees the node's footprint: a tree felled in explored, not
/// visible ground stays in the copy (drawn darkened) until a cell of its footprint is visible again. The props and the
/// minimap's resource layer draw this copy and relist on <see cref="Version"/>, which moves only when the copy changes.
/// </summary>
/// <remarks>
/// <see cref="Update"/> compares every slot with the store only when the caller's store key moves (the grid's version:
/// every spawn and fell bumps it), keeps the slots that differ in a pending list, and each call checks only those against
/// the fog. Without fog (<c>--no-fog</c>) every difference is taken at once. The first call copies the whole store
/// (unexplored ground draws nothing anyway). Reads the spans it is handed only; allocation-free after construction.
/// M6-V8 (the resource click): <see cref="UpdateAmounts"/> also keeps each node's amount left as last seen
/// (<see cref="Amount"/>), so the selection panel can show it for a node in explored, not visible ground.
/// </remarks>
public sealed class SeenResources
{
    private readonly bool[] _alive, _isPending;
    private readonly int[] _type, _cell, _pending, _amount;
    private readonly int[] _width, _height;
    private int _pendingCount;
    private int _storeKey;
    private bool _filled, _amountsFilled;

    /// <summary>Sizes the copy for a store of <paramref name="capacity"/> slots of the given resource types (only footprints are read).</summary>
    public SeenResources(ImmutableArray<ResourceDef> types, int capacity)
    {
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _alive = new bool[capacity];
        _isPending = new bool[capacity];
        _type = new int[capacity];
        _cell = new int[capacity];
        _pending = new int[capacity];
        _amount = new int[capacity];
        _width = new int[types.Length];
        _height = new int[types.Length];
        for (int t = 0; t < types.Length; t++)
        {
            _width[t] = types[t].FootprintWidth;
            _height[t] = types[t].FootprintHeight;
        }
    }

    /// <summary>Moves each time the copy changes (0 after the first fill, -1 before).</summary>
    public int Version { get; private set; } = -1;

    /// <summary>Slots whose store state differs from the copy and wait to be seen.</summary>
    public int PendingCount => _pendingCount;

    /// <summary>Per slot, whether the player last saw a live node there.</summary>
    public ReadOnlySpan<bool> Alive => _alive;

    /// <summary>Per slot, the last-seen node's resource type.</summary>
    public ReadOnlySpan<int> TypeId => _type;

    /// <summary>Per slot, the last-seen node's footprint anchor cell.</summary>
    public ReadOnlySpan<int> Cell => _cell;

    /// <summary>Per slot, the amount left the player last saw in the last-seen node (M6-V8; see <see cref="UpdateAmounts"/>).</summary>
    public ReadOnlySpan<int> Amount => _amount;

    /// <summary>
    /// Brings <see cref="Amount"/> up to what player <paramref name="player"/> sees now (M6-V8): each last-seen live node
    /// that is still the store's node in that slot (same type and anchor) and has a footprint cell in sight takes the store's
    /// amount; every other slot keeps the amount last seen. The first call takes every matching slot's amount whether seen
    /// or not: a match starts with full nodes, and a node can only be clicked once its ground is explored. Call it after
    /// <see cref="Update"/>, once per tick. Allocation-free.
    /// </summary>
    public void UpdateAmounts(ReadOnlySpan<bool> alive, ReadOnlySpan<int> typeId, ReadOnlySpan<int> cell, ReadOnlySpan<int> remaining,
        FogStore fog, int player, bool fogEnabled, int gridWidth)
    {
        int n = Math.Min(_alive.Length, Math.Min(Math.Min(alive.Length, remaining.Length), Math.Min(typeId.Length, cell.Length)));
        bool all = !_amountsFilled || !fogEnabled;
        _amountsFilled = true;
        for (int s = 0; s < n; s++)
        {
            if (!_alive[s] || !alive[s] || typeId[s] != _type[s] || cell[s] != _cell[s]) continue;
            if (all || Sees(fog, player, gridWidth, _type[s], _cell[s])) _amount[s] = remaining[s];
        }
    }

    /// <summary>True when the last-seen node in <paramref name="slot"/> has a footprint cell player <paramref name="player"/> sees now (always without fog); false for an empty or out-of-range slot.</summary>
    public bool InSight(int slot, FogStore fog, int player, bool fogEnabled, int gridWidth) =>
        (uint)slot < (uint)_alive.Length && _alive[slot] && (!fogEnabled || Sees(fog, player, gridWidth, _type[slot], _cell[slot]));

    /// <summary>
    /// Brings the copy up to what player <paramref name="player"/> sees now; true when it changed (<see cref="Version"/> moved).
    /// </summary>
    /// <param name="storeKey">A key that moves whenever the store's node set may have changed (<c>NavGrid.Version</c>).</param>
    /// <param name="alive">The resource store's <c>Alive</c>.</param>
    /// <param name="typeId">The resource store's <c>TypeId</c>.</param>
    /// <param name="cell">The resource store's <c>Cell</c>.</param>
    /// <param name="fog">The sim's fog (only read); ignored when <paramref name="fogEnabled"/> is false.</param>
    /// <param name="player">The local player.</param>
    /// <param name="fogEnabled">False for <c>--no-fog</c>: the copy follows the store at once.</param>
    /// <param name="gridWidth">Map width in cells, to walk a footprint.</param>
    public bool Update(int storeKey, ReadOnlySpan<bool> alive, ReadOnlySpan<int> typeId, ReadOnlySpan<int> cell,
        FogStore fog, int player, bool fogEnabled, int gridWidth)
    {
        int n = Math.Min(_alive.Length, Math.Min(alive.Length, Math.Min(typeId.Length, cell.Length)));
        if (!_filled)
        {
            for (int s = 0; s < n; s++) Copy(s, alive, typeId, cell);
            _filled = true;
            _storeKey = storeKey;
            Version = 0;
            return true;
        }
        if (storeKey != _storeKey)
        {
            _storeKey = storeKey;
            for (int s = 0; s < n; s++)
            {
                if (_isPending[s] || !Differs(s, alive, typeId, cell)) continue;
                _isPending[s] = true;
                _pending[_pendingCount++] = s;
            }
        }
        bool changed = false;
        for (int k = _pendingCount - 1; k >= 0; k--)
        {
            int s = _pending[k];
            if (Differs(s, alive, typeId, cell))
            {
                // The remembered node goes when its footprint is in sight; the store's new node comes only when its own
                // footprint is (BUG-0400 item 3: a slot reused elsewhere must not show a node the player has not seen).
                bool seeOld = _alive[s] && Sees(fog, player, gridWidth, _type[s], _cell[s]);
                bool seeNew = alive[s] && Sees(fog, player, gridWidth, typeId[s], cell[s]);
                if (fogEnabled && !seeOld && !seeNew) continue;
                if (fogEnabled && alive[s] && !seeNew)
                {
                    // Only the old footprint is seen: take the removal; the slot waits for its new node to be seen.
                    _alive[s] = false;
                    changed = true;
                    continue;
                }
                Copy(s, alive, typeId, cell);
                changed = true;
            }
            _isPending[s] = false;
            _pending[k] = _pending[--_pendingCount];
        }
        if (changed) Version++;
        return changed;
    }

    private bool Differs(int s, ReadOnlySpan<bool> alive, ReadOnlySpan<int> typeId, ReadOnlySpan<int> cell) =>
        alive[s] != _alive[s] || alive[s] && (typeId[s] != _type[s] || cell[s] != _cell[s]);

    private void Copy(int s, ReadOnlySpan<bool> alive, ReadOnlySpan<int> typeId, ReadOnlySpan<int> cell)
    {
        _alive[s] = alive[s];
        _type[s] = typeId[s];
        _cell[s] = cell[s];
    }

    private bool Sees(FogStore fog, int player, int gridWidth, int type, int anchor)
    {
        if ((uint)type >= (uint)_width.Length || anchor < 0 || gridWidth <= 0) return false;
        int ax = anchor % gridWidth, ay = anchor / gridWidth;
        for (int y = ay; y < ay + _height[type]; y++)
            for (int x = ax; x < ax + _width[type]; x++)
                if (x < gridWidth && fog.IsVisible(player, y * gridWidth + x)) return true;
        return false;
    }
}
