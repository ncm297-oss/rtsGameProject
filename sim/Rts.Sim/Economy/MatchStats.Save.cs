using Rts.Sim.Save;

namespace Rts.Sim.Economy;

// M6-S2: the post-match stats in a save, whole (not hashed, so only a direct compare can catch a save that drops them):
// every counter, each tech's researched tick, the army-value samples with their count, and the sample pass's sums.
internal sealed partial class MatchStats
{
    /// <summary>Writes every counter, the tech ticks, the samples and the sample count (M6-S2).</summary>
    internal void WriteTo(SaveWriter w)
    {
        w.Tag(SaveFormat.TagStats);
        w.Write(_players);
        w.Write(_techs);
        w.Write(SampleCapacity);
        w.WriteArray(GatheredGold);
        w.WriteArray(GatheredWood);
        w.WriteArray(UnitsTrained);
        w.WriteArray(UnitKills);
        w.WriteArray(BuildingKills);
        w.WriteArray(UnitLosses);
        w.WriteArray(BuildingLosses);
        w.WriteArray(BuildingsBuilt);
        w.WriteArray(_techTick);
        w.WriteArray(_samples);
        w.WriteArray(_sampleSums);
        w.Write(SampleCount);
    }

    /// <summary>Reads what <see cref="WriteTo"/> wrote into stats of the same players, techs and sample capacity.</summary>
    internal void ReadFrom(SaveReader r)
    {
        if (!r.Expect(SaveFormat.TagStats)) return;
        if (!r.Check(r.ReadInt32() == _players) || !r.Check(r.ReadInt32() == _techs) || !r.Check(r.ReadInt32() == SampleCapacity)) return;
        r.ReadArray(GatheredGold);
        r.ReadArray(GatheredWood);
        r.ReadArray(UnitsTrained);
        r.ReadArray(UnitKills);
        r.ReadArray(BuildingKills);
        r.ReadArray(UnitLosses);
        r.ReadArray(BuildingLosses);
        r.ReadArray(BuildingsBuilt);
        r.ReadArray(_techTick);
        r.ReadArray(_samples);
        r.ReadArray(_sampleSums);
        SampleCount = r.ReadInt32In(0, SampleCapacity);
    }
}
