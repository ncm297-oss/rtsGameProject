using System.Collections.Immutable;

namespace Rts.Sim.Data;

/// <summary>
/// One faction's AI data from <c>factions/&lt;id&gt;/ai.json</c> (M5-1, docs/03 "AI architecture"): per difficulty knobs, a
/// shared build order, an army composition per age, and the attack and retreat thresholds. Not in
/// <see cref="GameData.ContentHash"/> yet: nothing reads it to change sim state before the AI sends commands (M5-2 adds it).
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

    /// <summary>The build order, in file order (at least one step).</summary>
    public required ImmutableArray<AiStep> BuildOrder { get; init; }

    /// <summary>The army mix per age, indexed by age - 1 (one per age: Age I and Age II).</summary>
    public required ImmutableArray<AiComposition> Compositions { get; init; }

    /// <summary>The army population, in half-pop, it gathers before sending a wave (<c>attack.minArmyPop</c>).</summary>
    public int AttackMinHalfPop { get; init; }

    /// <summary>The army's total cost (gold + wood) it gathers before sending a wave (<c>attack.minArmyCost</c>).</summary>
    public int AttackMinCost { get; init; }

    /// <summary>The hp fraction below which a unit counts as wounded (<c>retreat.hpFraction</c>, 0-1).</summary>
    public float RetreatHpFraction { get; init; }

    /// <summary>The fraction of a wave lost at which it retreats (<c>retreat.lossFraction</c>, 0-1).</summary>
    public float RetreatLossFraction { get; init; }

    /// <summary>The knobs of difficulty <paramref name="difficulty"/>.</summary>
    public AiDifficultyDef Difficulty(AiDifficulty difficulty) => Difficulties[(int)difficulty];
}
