using Rts.Sim.Save;

namespace Rts.Sim.Entities;

// M6-S2: the resource store in a save, whole (a felled tree's freed slot and moved-on generation included). Its footprints
// are in the saved nav grid, so nothing is re-blocked and the placer never runs on a load.
public sealed partial class ResourceStore
{
    /// <summary>Writes every slot, the free list, the free count and the high-water mark (M6-S2).</summary>
    internal void WriteTo(SaveWriter w)
    {
        w.Tag(SaveFormat.TagResources);
        w.Write(Capacity);
        w.WriteArray(_alive);
        w.WriteArray(_generation);
        w.WriteArray(_typeId);
        w.WriteArray(_cell);
        w.WriteArray(_remaining);
        w.WriteArray(_freeList);
        w.Write(_freeCount);
        w.Write(_highWater);
    }

    /// <summary>Reads what <see cref="WriteTo"/> wrote into this store of the same capacity; checks the counts and every live slot's type and anchor cell (below <paramref name="cells"/>).</summary>
    internal void ReadFrom(SaveReader r, int cells)
    {
        if (!r.Expect(SaveFormat.TagResources) || !r.Check(r.ReadInt32() == Capacity)) return;
        r.ReadBools(_alive);
        r.ReadArray(_generation);
        r.ReadArray(_typeId);
        r.ReadArray(_cell);
        r.ReadArray(_remaining);
        r.ReadArray(_freeList);
        _freeCount = r.ReadInt32In(0, Capacity);
        _highWater = r.ReadInt32In(0, Capacity);
        if (r.Failed) return;
        int live = 0;
        for (int i = 0; i < Capacity; i++)
        {
            if (!_alive[i]) continue;
            live++;
            if (!r.Check(i < _highWater && (uint)_typeId[i] < (uint)_defs.Length && (uint)_cell[i] < (uint)cells && _remaining[i] >= 1)) return;
        }
        if (!r.Check(live == Capacity - _freeCount)) return;
        for (int k = 0; k < _freeCount; k++)
            if (!r.Check((uint)_freeList[k] < (uint)Capacity && !_alive[_freeList[k]])) return;
    }
}
