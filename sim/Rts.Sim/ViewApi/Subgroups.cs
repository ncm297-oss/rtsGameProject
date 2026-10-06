using System;
using Rts.Sim.Entities;

namespace Rts.Sim.ViewApi;

/// <summary>Tab subgroups of a selection: its distinct unit types in ascending type id, one of them active; view state, never sim state.</summary>
/// <remarks>
/// Fixed capacity (one entry per unit type), so nothing allocates after construction. The type
/// list is rebuilt from the selection by <see cref="Update"/>; the active subgroup survives a
/// rebuild if its type is still there, unless the caller asks for a reset (a new selection).
/// </remarks>
public sealed class Subgroups
{
    private readonly bool[] _present;
    private readonly int[] _types;

    /// <summary>Creates an empty subgroup list for type ids 0 to <paramref name="typeCount"/> - 1.</summary>
    public Subgroups(int typeCount)
    {
        if (typeCount < 1) throw new ArgumentOutOfRangeException(nameof(typeCount));
        _present = new bool[typeCount];
        _types = new int[typeCount];
    }

    /// <summary>Number of subgroups (distinct types in the selection).</summary>
    public int Count { get; private set; }

    /// <summary>Index of the active subgroup in <see cref="Types"/>; 0 when there are none.</summary>
    public int Index { get; private set; }

    /// <summary>The active subgroup's type id, or -1 with an empty selection.</summary>
    public int ActiveType => Count == 0 ? -1 : _types[Index];

    /// <summary>The selection's distinct type ids, ascending.</summary>
    public ReadOnlySpan<int> Types => _types.AsSpan(0, Count);

    /// <summary>Rebuilds the type list from <paramref name="selection"/>; <paramref name="reset"/> makes the first subgroup active, otherwise the active type stays active while it is still selected.</summary>
    /// <param name="typeIds">The unit store's <c>TypeId</c> array; out-of-range types are ignored.</param>
    public void Update(ReadOnlySpan<EntityHandle> selection, ReadOnlySpan<int> typeIds, bool reset)
    {
        int active = ActiveType;
        for (int i = 0; i < Count; i++) _present[_types[i]] = false;
        foreach (EntityHandle h in selection)
        {
            if ((uint)h.Index >= (uint)typeIds.Length) continue;
            int t = typeIds[h.Index];
            if ((uint)t < (uint)_present.Length) _present[t] = true;
        }
        Count = 0;
        Index = 0;
        for (int t = 0; t < _present.Length; t++)
        {
            if (!_present[t]) continue;
            if (!reset && t == active) Index = Count;
            _types[Count++] = t;
        }
    }

    /// <summary>Makes the next subgroup active, wrapping to the first (Tab); does nothing with no subgroups.</summary>
    public void Next()
    {
        if (Count > 0) Index = (Index + 1) % Count;
    }
}
