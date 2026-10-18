using System.Collections.Immutable;

namespace Rts.Sim.Data;

/// <summary>
/// One faction's AI data from <c>factions/&lt;id&gt;/ai.json</c> (M5-1, docs/03 "AI architecture"): per difficulty knobs, the
/// named openings (M5-8a: each a build order, an army composition per age and the attack thresholds), and the shared retreat
/// thresholds. In <see cref="GameData.ContentHash"/> since M5-3, every field.
/// </summary>
public sealed class AiDef
{
    internal AiDef()
    {
    }

    /// <summary>The faction id (index into <see cref="GameData.Factions"/>).</summary>
    public int Faction { get; init; }

    /// <summary>Per difficulty, indexed by <see cref="AiDifficulty"/> (all three present).</summary>
    public required ImmutableArray<AiDifficultyDef> Difficulties { get; init; }

    /// <summary>M5-8a: the named openings, in file order (1 to <see cref="DataLimits.MaxAiOpenings"/>); Easy always plays the first.</summary>
    public required ImmutableArray<AiOpening> Openings { get; init; }

    /// <summary>The hp fraction below which a unit counts as wounded (<c>retreat.hpFraction</c>, 0-1).</summary>
    public float RetreatHpFraction { get; init; }

    /// <summary>The fraction of a wave lost at which it retreats (<c>retreat.lossFraction</c>, 0-1).</summary>
    public float RetreatLossFraction { get; init; }

    /// <summary>The knobs of difficulty <paramref name="difficulty"/>.</summary>
    public AiDifficultyDef Difficulty(AiDifficulty difficulty) => Difficulties[(int)difficulty];

    /// <summary>The index of opening <paramref name="id"/> in <see cref="Openings"/>, or -1.</summary>
    public int FindOpening(string id)
    {
        for (int i = 0; i < Openings.Length; i++)
            if (Openings[i].Id == id) return i;
        return -1;
    }
}
