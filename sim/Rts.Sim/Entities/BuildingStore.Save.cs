using Rts.Sim.Economy;
using Rts.Sim.Save;

namespace Rts.Sim.Entities;

// M6-S2: the building store in a save, every array whole: construction work, the production queues (techs flagged), rally
// points, repair accumulators, tower state, the free list and the high-water mark. Its footprints are already in the saved
// nav grid, so nothing is re-blocked.
public sealed partial class BuildingStore
{
    /// <summary>Writes every slot's every field, the free list, the free count and the high-water mark (M6-S2).</summary>
    internal void WriteTo(SaveWriter w)
    {
        w.Tag(SaveFormat.TagBuildings);
        w.Write(Capacity);
        w.WriteArray(_alive);
        w.WriteArray(_generation);
        w.WriteArray(_owner);
        w.WriteArray(_typeId);
        w.WriteArray(_cell);
        w.WriteArray(_hp);
        w.WriteArray(_underConstruction);
        w.WriteArray(_work);
        w.WriteArray(_repairProgress);
        w.WriteArray(_repairGold);
        w.WriteArray(_repairWood);
        w.WriteArray(_queueCount);
        w.WriteArray(_queueTypeId);
        w.WriteArray(_queueIsTech);
        w.WriteArray(_progress);
        w.WriteArray(_hasRally);
        w.WriteArray(_rallyPosition);
        w.WriteArray(_towerTarget);
        w.WriteArray(_towerCooldown);
        w.WriteArray(_towerWindup);
        w.WriteArray(_freeList);
        w.Write(_freeCount);
        w.Write(_highWater);
    }

    /// <summary>
    /// Reads what <see cref="WriteTo"/> wrote into this store of the same capacity (M6-S2). Checks the counts, every live
    /// slot's owner below <paramref name="players"/>, type, anchor cell (below <paramref name="cells"/>) and queue (each
    /// item a known unit or tech), and the free list's slots.
    /// </summary>
    internal void ReadFrom(SaveReader r, int players, int cells)
    {
        if (!r.Expect(SaveFormat.TagBuildings) || !r.Check(r.ReadInt32() == Capacity)) return;
        r.ReadBools(_alive);
        r.ReadArray(_generation);
        r.ReadArray(_owner);
        r.ReadArray(_typeId);
        r.ReadArray(_cell);
        r.ReadArray(_hp);
        r.ReadBools(_underConstruction);
        r.ReadArray(_work);
        r.ReadArray(_repairProgress);
        r.ReadArray(_repairGold);
        r.ReadArray(_repairWood);
        r.ReadArray(_queueCount);
        r.ReadArray(_queueTypeId);
        r.ReadBools(_queueIsTech);
        r.ReadArray(_progress);
        r.ReadBools(_hasRally);
        r.ReadArray(_rallyPosition);
        r.ReadArray(_towerTarget);
        r.ReadArray(_towerCooldown);
        r.ReadArray(_towerWindup);
        r.ReadArray(_freeList);
        _freeCount = r.ReadInt32In(0, Capacity);
        _highWater = r.ReadInt32In(0, Capacity);
        if (r.Failed) return;

        int live = 0;
        for (int i = 0; i < Capacity; i++)
        {
            if (!_alive[i]) continue;
            live++;
            if (!r.Check(i < _highWater && (uint)_owner[i] < (uint)players && (uint)_typeId[i] < (uint)_defs.Length && (uint)_cell[i] < (uint)cells)) return;
            int n = _queueCount[i];
            if (!r.Check(n >= 0 && n <= EconomyConstants.ProductionQueueCapacity)) return;
            for (int q = 0; q < n; q++)
            {
                int e = i * EconomyConstants.ProductionQueueCapacity + q;
                if (!r.Check((uint)_queueTypeId[e] < (uint)(_queueIsTech[e] ? _techs.Length : _units.Length))) return;
            }
        }
        if (!r.Check(live == Capacity - _freeCount)) return;
        for (int k = 0; k < _freeCount; k++)
            if (!r.Check((uint)_freeList[k] < (uint)Capacity && !_alive[_freeList[k]])) return;
    }
}
