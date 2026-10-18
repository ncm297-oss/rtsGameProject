using Rts.Sim.Save;

namespace Rts.Sim.Pathfinding;

// M6-S2: the flow-field cache in a save, whole (Producer decision 2026-10-07, BUG-0081): the clock, count, build count, the
// LRU stamps, the cell-to-slot index, the shared step masks with the grid version they were computed at, and every used
// slot's field (FlowField.Save). Slots past the count were never built and keep their fresh state.
public sealed partial class FlowFieldCache
{
    /// <summary>Writes the cache's bookkeeping and every used slot's field (M6-S2).</summary>
    internal void WriteTo(SaveWriter w)
    {
        w.Tag(SaveFormat.TagFlowFields);
        w.Write(Capacity);
        w.Write(_count);
        w.Write(_clock);
        w.Write(_stepsVersion);
        w.Write(BuildCount);
        w.WriteArray(_lastUse);
        w.WriteArray(_slotOfCell);
        w.WriteArray(_steps);
        for (int i = 0; i < _count; i++) _fields[i].WriteTo(w);
    }

    /// <summary>Reads what <see cref="WriteTo"/> wrote into a cache of the same capacity on the same grid; checks the count and the index's slots.</summary>
    internal void ReadFrom(SaveReader r)
    {
        if (!r.Expect(SaveFormat.TagFlowFields) || !r.Check(r.ReadInt32() == Capacity)) return;
        _count = r.ReadInt32In(0, Capacity);
        _clock = r.ReadInt64();
        _stepsVersion = r.ReadInt32();
        BuildCount = r.ReadInt32();
        r.ReadArray(_lastUse);
        r.ReadArray(_slotOfCell);
        r.ReadArray(_steps);
        if (r.Failed) return;
        foreach (int slot in _slotOfCell)
            if (!r.Check(slot >= -1 && slot < _count)) return;
        // The load's scratch for the cost rebuild (a load is not a tick: it may allocate).
        int cells = _slotOfCell.Length;
        var stack = new int[cells];
        var state = new byte[cells];
        for (int i = 0; i < _count && !r.Failed; i++) _fields[i].ReadFrom(r, stack, state);
    }

    /// <summary>Test seam (M6-S2's direct compares): the cache's use clock.</summary>
    internal long Clock => _clock;

    /// <summary>Test seam: the field in slot <paramref name="slot"/> (below <see cref="Count"/>).</summary>
    internal FlowField SlotField(int slot) => _fields[slot];

    /// <summary>Test seam: slot <paramref name="slot"/>'s LRU stamp.</summary>
    internal long SlotLastUse(int slot) => _lastUse[slot];
}
