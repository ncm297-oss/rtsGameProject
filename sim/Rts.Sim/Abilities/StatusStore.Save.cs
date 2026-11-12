using Rts.Sim.Save;

namespace Rts.Sim.Abilities;

// M6-S2: the statuses in a save, whole: the hashed entries and the derived per-slot blind and stealth counts with their
// totals (kept incrementally, so stored rather than recounted).
public sealed partial class StatusStore
{
    /// <summary>Writes every entry and every derived count (M6-S2).</summary>
    internal void WriteTo(SaveWriter w)
    {
        w.WriteArray(Count);
        w.WriteArray(StatusId);
        w.WriteArray(Magnitude);
        w.WriteArray(TicksRemaining);
        w.WriteArray(PulseTicks);
        w.WriteArray(SourcePlayer);
        w.WriteArray(_blind);
        w.WriteArray(_stealth);
        w.Write(UnitsWithStatuses);
        w.Write(BlindedUnits);
        w.Write(StealthedUnits);
    }

    /// <summary>M7-1 (format 10): the derived regenerating counts, in the unit store's <c>REGN</c> part (<see cref="Entities.UnitStore"/>).</summary>
    internal void WriteRegenTo(SaveWriter w)
    {
        w.WriteArray(_regen);
        w.Write(RegeneratingUnits);
    }

    /// <summary>
    /// Reads what <see cref="WriteRegenTo"/> wrote. BUG-1165: the counts are derived, so each slot's must equal its entries of
    /// a <see cref="Data.StatusKind.Regenerating"/> status (by <paramref name="statuses"/>) and the total the slots that count
    /// one; a forged count is refused, never trusted (it made <c>RegenSystem</c>'s skip fire, or a byte underflow on expiry).
    /// Call after <see cref="ReadFrom"/> has checked every entry's status id.
    /// </summary>
    internal void ReadRegenFrom(SaveReader r, System.Collections.Immutable.ImmutableArray<Data.StatusDef> statuses)
    {
        r.ReadArray(_regen);
        RegeneratingUnits = r.ReadInt32In(0, Count.Length);
        if (r.Failed) return;
        int total = 0;
        for (int u = 0; u < Count.Length; u++)
        {
            int entries = 0;
            for (int k = u * PerUnit; k < u * PerUnit + Count[u]; k++)
                if (statuses[StatusId[k]].Kind == Data.StatusKind.Regenerating) entries++;
            if (!r.Check(_regen[u] == entries)) return;
            if (entries > 0) total++;
        }
        r.Check(RegeneratingUnits == total);
    }

    /// <summary>Reads what <see cref="WriteTo"/> wrote into a store of the same capacity; each slot's count must be at most <see cref="PerUnit"/> and each entry's status below <paramref name="statuses"/>.</summary>
    internal void ReadFrom(SaveReader r, int statuses)
    {
        r.ReadArray(Count);
        r.ReadArray(StatusId);
        r.ReadArray(Magnitude);
        r.ReadArray(TicksRemaining);
        r.ReadArray(PulseTicks);
        r.ReadArray(SourcePlayer);
        r.ReadArray(_blind);
        r.ReadArray(_stealth);
        UnitsWithStatuses = r.ReadInt32In(0, Count.Length);
        BlindedUnits = r.ReadInt32In(0, Count.Length);
        StealthedUnits = r.ReadInt32In(0, Count.Length);
        if (r.Failed) return;
        for (int u = 0; u < Count.Length; u++)
        {
            if (!r.Check(Count[u] >= 0 && Count[u] <= PerUnit)) return;
            for (int k = u * PerUnit; k < u * PerUnit + Count[u]; k++)
                if (!r.Check((uint)StatusId[k] < (uint)statuses)) return;
        }
    }
}
