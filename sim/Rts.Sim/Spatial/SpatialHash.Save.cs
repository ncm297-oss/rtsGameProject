using Rts.Sim.Save;

namespace Rts.Sim.Spatial;

// M6-S2: the spatial hash in a save, whole. It is rebuilt every tick right after the commands apply, but the commands of
// that phase (and every read between ticks) see the last tick's index, bucket order and owner codes included, so a load
// restores it as it was instead of rebuilding it from the units' current positions.
public sealed partial class SpatialHash
{
    /// <summary>Writes every index array, the side counts and codes (M6-S4: the owners' sides, the owners themselves in a free-for-all), and the count (M6-S2).</summary>
    internal void WriteTo(SaveWriter w)
    {
        w.Tag(SaveFormat.TagSpatial);
        w.Write(Capacity);
        w.WriteArray(_bucketStart);
        w.WriteArray(_cursor);
        w.WriteArray(_slotBucket);
        w.WriteArray(_entrySlot);
        w.WriteArray(_entryPos);
        w.WriteArray(_entrySide);
        w.WriteArray(_sideCount);
        w.Write(_ownerCodes);
        w.Write(Count);
    }

    /// <summary>Reads what <see cref="WriteTo"/> wrote into a hash of the same capacity and map; checks the bucket bounds and entry slots.</summary>
    internal void ReadFrom(SaveReader r)
    {
        if (!r.Expect(SaveFormat.TagSpatial) || !r.Check(r.ReadInt32() == Capacity)) return;
        r.ReadArray(_bucketStart);
        r.ReadArray(_cursor);
        r.ReadArray(_slotBucket);
        r.ReadArray(_entrySlot);
        r.ReadArray(_entryPos);
        r.ReadArray(_entrySide);
        r.ReadArray(_sideCount);
        _ownerCodes = r.ReadBool();
        Count = r.ReadInt32In(0, Capacity);
        if (r.Failed) return;
        // Queries walk [_bucketStart[b], _bucketStart[b + 1]) and read the entries there: the bounds must climb within Count.
        if (!r.Check(_bucketStart[0] == 0 && _bucketStart[^1] == Count)) return;
        for (int b = 0; b + 1 < _bucketStart.Length; b++)
            if (!r.Check(_bucketStart[b] <= _bucketStart[b + 1])) return;
        for (int e = 0; e < Count; e++)
            if (!r.Check((uint)_entrySlot[e] < (uint)Capacity)) return;
        int buckets = _cursor.Length;
        for (int s = 0; s < Capacity; s++)
            if (!r.Check(_slotBucket[s] >= -1 && _slotBucket[s] < buckets)) return;
    }
}
