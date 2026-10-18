using Rts.Sim.Save;

namespace Rts.Sim.Economy;

// M6-S2: the ledger in a save, whole: the hashed totals, kills, losses and defeat flags, and the incrementally kept
// population, cap, live and finished counts (stored, not recounted from the stores), then the match stats.
internal sealed partial class PlayerLedger
{
    /// <summary>Writes every per-player array and the finished-building counts, then <see cref="Stats"/> (M6-S2).</summary>
    internal void WriteTo(SaveWriter w)
    {
        w.Tag(SaveFormat.TagLedger);
        w.Write(Gold.Length);
        w.WriteArray(Gold);
        w.WriteArray(Wood);
        w.WriteArray(HalfPop);
        w.WriteArray(HalfPopProvided);
        w.WriteArray(HalfPopCap);
        w.WriteArray(Kills);
        w.WriteArray(Losses);
        w.WriteArray(LiveBuildings);
        w.WriteArray(LiveUnits);
        w.WriteArray(ProductionBuildings);
        w.WriteArray(EverBuilt);
        w.WriteArray(Defeated);
        w.WriteArray(_finishedOfType);
        w.WriteArray(_finishedInSlot);
        Stats.WriteTo(w);
    }

    /// <summary>Reads what <see cref="WriteTo"/> wrote into a ledger of the same players and data.</summary>
    internal void ReadFrom(SaveReader r)
    {
        if (!r.Expect(SaveFormat.TagLedger) || !r.Check(r.ReadInt32() == Gold.Length)) return;
        r.ReadArray(Gold);
        r.ReadArray(Wood);
        r.ReadArray(HalfPop);
        r.ReadArray(HalfPopProvided);
        r.ReadArray(HalfPopCap);
        r.ReadArray(Kills);
        r.ReadArray(Losses);
        r.ReadArray(LiveBuildings);
        r.ReadArray(LiveUnits);
        r.ReadArray(ProductionBuildings);
        r.ReadBools(EverBuilt);
        r.ReadBools(Defeated);
        r.ReadArray(_finishedOfType);
        r.ReadArray(_finishedInSlot);
        Stats.ReadFrom(r);
    }
}
