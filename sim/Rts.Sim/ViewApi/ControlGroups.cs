using System;
using System.Collections.Immutable;
using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;

namespace Rts.Sim.ViewApi;

/// <summary>The player's nine control groups (keys 1-9, index 0-8): each holds units or one building (M6-V7); view state, never sim state.</summary>
/// <remarks>
/// Each group is a fixed-capacity <see cref="SelectionSet"/> of units plus at most one building handle, so nothing allocates
/// after construction. A group is units or one building, never both (the selection model holds one building alone, M3-V2):
/// assigning either replaces the other. Liveness is checked against the stores' arrays passed in, so this class holds no
/// reference to the sim: a dead or recycled handle is pruned, never recalled.
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
    // The building a group holds; Generation 0 (never a live handle's) = none.
    private readonly EntityHandle[] _buildings = new EntityHandle[Count];
    private int _lastTap = -1;
    private double _lastTapMs = double.NegativeInfinity;

    /// <summary>Creates nine empty groups for a unit store of <paramref name="slotCapacity"/> slots.</summary>
    public ControlGroups(int slotCapacity)
    {
        for (int g = 0; g < Count; g++) _groups[g] = new SelectionSet(slotCapacity);
    }

    /// <summary>The unit handles in group <paramref name="group"/> (0-8), oldest first; may include dead ones until the next <see cref="Prune"/>. Empty while it holds a building.</summary>
    public ReadOnlySpan<EntityHandle> Items(int group) => Group(group).Items;

    /// <summary>The building group <paramref name="group"/> holds (Ctrl / Shift + digit with a building selected), or default (Generation 0) for none; may be dead until the next <see cref="PruneBuildings"/>.</summary>
    public EntityHandle Building(int group)
    {
        Group(group);
        return _buildings[group];
    }

    /// <summary>Replaces group <paramref name="group"/> with <paramref name="units"/> (Ctrl + digit); a building it held goes.</summary>
    public void Assign(int group, ReadOnlySpan<EntityHandle> units)
    {
        SelectionSet set = Group(group);
        set.Clear();
        _buildings[group] = default;
        foreach (EntityHandle h in units) set.Add(h);
    }

    /// <summary>Adds <paramref name="units"/> to group <paramref name="group"/>, keeping its units (Shift + digit); a building it held goes when any unit comes in (a group is units or one building).</summary>
    public void Add(int group, ReadOnlySpan<EntityHandle> units)
    {
        SelectionSet set = Group(group);
        if (units.Length > 0) _buildings[group] = default;
        foreach (EntityHandle h in units) set.Add(h);
    }

    /// <summary>Makes group <paramref name="group"/> hold <paramref name="building"/> alone (Ctrl or Shift + digit with a building selected); its units go.</summary>
    public void AssignBuilding(int group, EntityHandle building)
    {
        Group(group).Clear();
        _buildings[group] = building;
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

    /// <summary>The live building group <paramref name="group"/> holds, as a slot, or -1 (none, or it died or its slot was reused: then it is dropped). Takes the building store's arrays.</summary>
    public int RecallBuilding(int group, ReadOnlySpan<bool> alive, ReadOnlySpan<int> generation)
    {
        Group(group);
        EntityHandle b = _buildings[group];
        if (b.Generation == 0) return -1;
        if (Live(b, alive, generation)) return b.Index;
        _buildings[group] = default;
        return -1;
    }

    /// <summary>Drops dead or recycled unit handles from every group; returns how many were dropped.</summary>
    public int Prune(ReadOnlySpan<bool> alive, ReadOnlySpan<int> generation)
    {
        int dropped = 0;
        foreach (SelectionSet set in _groups) dropped += set.Prune(alive, generation);
        return dropped;
    }

    /// <summary>Drops a razed, cancelled or recycled building from every group (the building store's arrays); returns how many were dropped.</summary>
    public int PruneBuildings(ReadOnlySpan<bool> alive, ReadOnlySpan<int> generation)
    {
        int dropped = 0;
        for (int g = 0; g < Count; g++)
        {
            if (_buildings[g].Generation == 0 || Live(_buildings[g], alive, generation)) continue;
            _buildings[g] = default;
            dropped++;
        }
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

    /// <summary>
    /// The camera target of a double-tap on a group that holds a building: the live building's footprint centre (the building
    /// store, its defs and the grid it stands on); false when group <paramref name="group"/> holds no live building (then a
    /// double-tap centres on its units, <see cref="TryMean"/>).
    /// </summary>
    public bool TryBuildingCentre(int group, BuildingStore buildings, ImmutableArray<BuildingDef> defs, NavGrid grid, out Vector2 centre)
    {
        int slot = RecallBuilding(group, buildings.Alive, buildings.Generation);
        centre = slot >= 0 ? StartBase.FootprintCenter(grid, defs[buildings.TypeId[slot]], buildings.Cell[slot]) : Vector2.Zero;
        return slot >= 0;
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

    private static bool Live(EntityHandle h, ReadOnlySpan<bool> alive, ReadOnlySpan<int> generation) =>
        (uint)h.Index < (uint)alive.Length && alive[h.Index] && generation[h.Index] == h.Generation;

    private SelectionSet Group(int group)
    {
        if ((uint)group >= Count) throw new ArgumentOutOfRangeException(nameof(group));
        return _groups[group];
    }
}
