using Rts.Sim.Save;

namespace Rts.Sim.Abilities;

// M6-S2: the live zones in a save, whole: the hashed fields, the fog's per-player records of each blocking zone, the
// derived radius and vision flag, the counts, the loop bound and the blocker version the fog's near-blocker mask follows.
public sealed partial class ZoneStore
{
    /// <summary>Writes every slot, the fog records and the counters (M6-S2).</summary>
    internal void WriteTo(SaveWriter w)
    {
        w.Tag(SaveFormat.TagZones);
        w.Write(Capacity);
        w.WriteArray(_alive);
        w.WriteArray(_owner);
        w.WriteArray(_center);
        w.WriteArray(_abilityId);
        w.WriteArray(_ticksRemaining);
        w.WriteArray(_radius);
        w.WriteArray(_blocksVision);
        w.WriteArray(_seenInside);
        w.WriteArray(_covered);
        w.Write(Count);
        w.Write(BlockerCount);
        w.Write(BlockerVersion);
        w.Write(_end);
    }

    /// <summary>Reads what <see cref="WriteTo"/> wrote into this store of the same capacity and players; checks the counts and every live slot's ability and owner.</summary>
    internal void ReadFrom(SaveReader r)
    {
        if (!r.Expect(SaveFormat.TagZones) || !r.Check(r.ReadInt32() == Capacity)) return;
        r.ReadBools(_alive);
        r.ReadArray(_owner);
        r.ReadArray(_center);
        r.ReadArray(_abilityId);
        r.ReadArray(_ticksRemaining);
        r.ReadArray(_radius);
        r.ReadBools(_blocksVision);
        r.ReadArray(_seenInside);
        r.ReadArray(_covered);
        Count = r.ReadInt32In(0, Capacity);
        BlockerCount = r.ReadInt32In(0, Capacity);
        BlockerVersion = r.ReadInt32();
        _end = r.ReadInt32In(0, Capacity);
        if (r.Failed) return;
        int live = 0;
        for (int k = 0; k < Capacity; k++)
        {
            if (!_alive[k]) continue;
            live++;
            if (!r.Check(k < _end && (uint)_abilityId[k] < (uint)_data.Abilities.Length && (uint)_owner[k] < (uint)_players)) return;
        }
        r.Check(live == Count);
    }
}
