using Rts.Sim.Save;

namespace Rts.Sim.Economy;

// M6-S2: the researched techs in a save: the hashed flags and the derived bonus sums, stored rather than re-summed so the
// floats are the very ones the unsaved run holds (the effect tables follow from the data).
internal sealed partial class TechState
{
    /// <summary>Writes the flags and both bonus sums (M6-S2).</summary>
    internal void WriteTo(SaveWriter w)
    {
        w.Tag(SaveFormat.TagTechs);
        w.WriteArray(_flags);
        w.WriteArray(_bonus);
        w.WriteArray(_buildingBonus);
    }

    /// <summary>Reads what <see cref="WriteTo"/> wrote into the techs of the same players and data.</summary>
    internal void ReadFrom(SaveReader r)
    {
        if (!r.Expect(SaveFormat.TagTechs)) return;
        r.ReadArray(_flags);
        r.ReadArray(_bonus);
        r.ReadArray(_buildingBonus);
    }
}
