using System;
using System.Numerics;
using Rts.Sim.Entities;

namespace Rts.Sim.ViewApi;

/// <summary>The player's nine control groups (keys 1-9, index 0-8) as handle sets; view state, never sim state.</summary>
/// <remarks>
/// Each group is a fixed-capacity <see cref="SelectionSet"/>, so nothing allocates after
/// construction. Liveness is checked against the unit store's arrays passed in, so this class holds
/// no reference to the sim: a dead or recycled handle is pruned, never recalled.
/// </remarks>
public sealed class ControlGroups
{
    /// <summary>Number of groups.</summary>
    public const int Count = 9;

    /// <summary>Longest gap between two recalls of the same group that counts as a double-tap, in whole milliseconds of wall clock (the unit the gap is compared in); the one source for the rule (BUG-0070).</summary>
    public const double DoubleTapMs = 300;

    /// <summary><see cref="DoubleTapMs"/> in seconds.</summary>
    public const double DoubleTapSeconds = DoubleTapMs / 1000;

    private readonly SelectionSet[] _groups = new SelectionSet[Count];
    private int _lastTap = -1;
    private double _lastTapMs = double.NegativeInfinity;

    /// <summary>Creates nine empty groups for a unit store of <paramref name="slotCapacity"/> slots.</summary>
    public ControlGroups(int slotCapacity)
    {
        for (int g = 0; g < Count; g++) _groups[g] = new SelectionSet(slotCapacity);
    }

    /// <summary>The handles in group <paramref name="group"/> (0-8), oldest first; may include dead ones until the next <see cref="Prune"/>.</summary>
    public ReadOnlySpan<EntityHandle> Items(int group) => Group(group).Items;

    /// <summary>Replaces group <paramref name="group"/> with <paramref name="units"/> (Ctrl + digit).</summary>
    public void Assign(int group, ReadOnlySpan<EntityHandle> units)
    {
        SelectionSet set = Group(group);
        set.Clear();
        foreach (EntityHandle h in units) set.Add(h);
    }

    /// <summary>Adds <paramref name="units"/> to group <paramref name="group"/>, keeping what it has (Shift + digit).</summary>
    public void Add(int group, ReadOnlySpan<EntityHandle> units)
    {
        SelectionSet set = Group(group);
        foreach (EntityHandle h in units) set.Add(h);
    }

    /// <summary>Prunes group <paramref name="group"/>, then replaces <paramref name="into"/> with its live units; returns how many (an empty group leaves <paramref name="into"/> as it was).</summary>
    public int Recall(int group, SelectionSet into, ReadOnlySpan<bool> alive, ReadOnlySpan<int> generation)
    {
        SelectionSet set = Group(group);
        set.Prune(alive, generation);
        if (set.Count == 0) return 0;
        into.Clear();
        foreach (EntityHandle h in set.Items) into.Add(h);
        return set.Count;
    }

    /// <summary>Drops dead or recycled handles from every group; returns how many were dropped.</summary>
    public int Prune(ReadOnlySpan<bool> alive, ReadOnlySpan<int> generation)
    {
        int dropped = 0;
        foreach (SelectionSet set in _groups) dropped += set.Prune(alive, generation);
        return dropped;
    }

    /// <summary>Mean position of group <paramref name="group"/>'s live units (the camera target of a double-tap); false if it has none.</summary>
    public bool TryMean(int group, ReadOnlySpan<Vector2> positions, ReadOnlySpan<bool> alive, ReadOnlySpan<int> generation, out Vector2 mean)
    {
        Vector2 sum = Vector2.Zero;
        int n = 0;
        foreach (EntityHandle h in Group(group).Items)
        {
            if ((uint)h.Index >= (uint)alive.Length || !alive[h.Index] || generation[h.Index] != h.Generation) continue;
            sum += positions[h.Index];
            n++;
        }
        mean = n > 0 ? sum / n : Vector2.Zero;
        return n > 0;
    }

    /// <summary>Records a recall of <paramref name="group"/> at <paramref name="nowSeconds"/>; true if it is the second tap of the same group within <see cref="DoubleTapSeconds"/>.</summary>
    /// <remarks>
    /// A double-tap is consumed: a third quick tap starts a new pair. Times are rounded to whole
    /// milliseconds first (the clock the view reads), so a gap of exactly 300 ms gets the same answer
    /// at any clock value instead of depending on how <c>t / 1000.0</c> rounds (BUG-0067).
    /// </remarks>
    public bool Tap(int group, double nowSeconds)
    {
        Group(group);
        // Integer-valued doubles subtract exactly up to 2^53 ms; NaN never pairs.
        double nowMs = Math.Round(nowSeconds * 1000.0);
        bool doubled = group == _lastTap && nowMs - _lastTapMs <= DoubleTapMs && nowMs >= _lastTapMs;
        _lastTap = doubled ? -1 : group;
        _lastTapMs = nowMs;
        return doubled;
    }

    private SelectionSet Group(int group)
    {
        if ((uint)group >= Count) throw new ArgumentOutOfRangeException(nameof(group));
        return _groups[group];
    }
}
