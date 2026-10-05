using System;
using Rts.Sim.Entities;

namespace Rts.Sim.ViewApi;

/// <summary>The player's selected units as handles, in the order they were selected; a view concern, never sim state.</summary>
/// <remarks>
/// Fixed capacity (one entry per unit slot), so nothing allocates after construction. At most one
/// handle per slot. Liveness is checked against the unit store's arrays passed to <see cref="Prune"/>,
/// so this class holds no reference to the sim.
/// </remarks>
public sealed class SelectionSet
{
    private readonly EntityHandle[] _items;
    private readonly int[] _position; // per slot: index into _items, or -1

    /// <summary>Creates an empty selection for a unit store of <paramref name="slotCapacity"/> slots.</summary>
    public SelectionSet(int slotCapacity)
    {
        if (slotCapacity < 1) throw new ArgumentOutOfRangeException(nameof(slotCapacity));
        _items = new EntityHandle[slotCapacity];
        _position = new int[slotCapacity];
        Array.Fill(_position, -1);
    }

    /// <summary>Number of selected units.</summary>
    public int Count { get; private set; }

    /// <summary>The selected handles, oldest selection first. Valid until the next change.</summary>
    public ReadOnlySpan<EntityHandle> Items => _items.AsSpan(0, Count);

    /// <summary>True if exactly this handle (slot and generation) is selected.</summary>
    public bool Contains(EntityHandle h) =>
        (uint)h.Index < (uint)_position.Length && _position[h.Index] >= 0 && _items[_position[h.Index]] == h;

    /// <summary>Deselects everything.</summary>
    public void Clear()
    {
        for (int i = 0; i < Count; i++) _position[_items[i].Index] = -1;
        Count = 0;
    }

    /// <summary>Selects a handle; a stale handle to the same slot is replaced. Returns false if it was already selected or is out of range.</summary>
    public bool Add(EntityHandle h)
    {
        if ((uint)h.Index >= (uint)_position.Length) return false;
        int at = _position[h.Index];
        if (at >= 0)
        {
            if (_items[at] == h) return false;
            _items[at] = h;
            return true;
        }
        _position[h.Index] = Count;
        _items[Count++] = h;
        return true;
    }

    /// <summary>Deselects a handle; returns false if it wasn't selected. Keeps the others' order.</summary>
    public bool Remove(EntityHandle h)
    {
        if (!Contains(h)) return false;
        int at = _position[h.Index];
        _position[h.Index] = -1;
        for (int i = at + 1; i < Count; i++)
        {
            _items[i - 1] = _items[i];
            _position[_items[i - 1].Index] = i - 1;
        }
        Count--;
        return true;
    }

    /// <summary>Selects the handle if it isn't selected, otherwise deselects it (Shift + click).</summary>
    public void Toggle(EntityHandle h)
    {
        if (!Remove(h)) Add(h);
    }

    /// <summary>Drops handles whose slot is dead or has a newer generation; returns how many were dropped.</summary>
    /// <param name="alive">The unit store's <c>Alive</c> array.</param>
    /// <param name="generation">The unit store's <c>Generation</c> array.</param>
    public int Prune(ReadOnlySpan<bool> alive, ReadOnlySpan<int> generation)
    {
        int kept = 0;
        for (int i = 0; i < Count; i++)
        {
            EntityHandle h = _items[i];
            bool live = (uint)h.Index < (uint)alive.Length && alive[h.Index] && generation[h.Index] == h.Generation;
            if (!live)
            {
                _position[h.Index] = -1;
                continue;
            }
            _items[kept] = h;
            _position[h.Index] = kept;
            kept++;
        }
        int dropped = Count - kept;
        Count = kept;
        return dropped;
    }
}
