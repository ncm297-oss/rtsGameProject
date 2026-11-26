using System.Collections.Immutable;

namespace Rts.Sim.Data;

/// <summary>
/// M5-8a: one named opening of a faction's <c>ai.json</c> <c>openings</c> (docs/03 "AI architecture"): a build order, an army
/// mix per age and the attack thresholds. The AI picks one at match start (Easy the first; Normal and Hard a seeded draw).
/// In <see cref="GameData.ContentHash"/>, every field.
/// </summary>
public sealed class AiOpening
{
    internal AiOpening()
    {
    }

    /// <summary>The opening's snake_case id, unique within its file.</summary>
    public required string Id { get; init; }

    /// <summary>Player-facing name (1-40 characters).</summary>
    public required string DisplayName { get; init; }

    /// <summary>Player-facing description (optional in the file: "" when absent; at most 200 characters).</summary>
    public required string Description { get; init; }

    /// <summary>The build order, in file order (at least one step): the one every difficulty without its own entry in <see cref="BuildOrders"/> plays.</summary>
    public required ImmutableArray<AiStep> BuildOrder { get; init; }

    /// <summary>
    /// M7-9 (Request 59): the opening's own build order per difficulty (<c>buildOrders</c>), indexed by <see cref="AiDifficulty"/>
    /// (one entry per difficulty); an empty entry where the file has none (a written entry has at least one step), and the
    /// whole array empty when the file leaves the field out. Read it through <see cref="BuildOrderFor"/>.
    /// </summary>
    public ImmutableArray<ImmutableArray<AiStep>> BuildOrders { get; init; } = ImmutableArray<ImmutableArray<AiStep>>.Empty;

    /// <summary>M7-9: the build order a seat of <paramref name="difficulty"/> plays: its own entry in <see cref="BuildOrders"/>, else <see cref="BuildOrder"/>.</summary>
    public ImmutableArray<AiStep> BuildOrderFor(AiDifficulty difficulty)
    {
        int k = (int)difficulty;
        return HasEntry(BuildOrders, k) ? BuildOrders[k] : BuildOrder;
    }

    /// <summary>M7-9: whether <paramref name="lists"/> (an opening's <see cref="BuildOrders"/>) holds an entry, a list with steps, for difficulty index <paramref name="k"/>.</summary>
    internal static bool HasEntry(ImmutableArray<ImmutableArray<AiStep>> lists, int k)
    {
        return !lists.IsDefault && (uint)k < (uint)lists.Length && !lists[k].IsDefaultOrEmpty;
    }

    /// <summary>The army mix per age, indexed by age - 1 (one per age: Age I and Age II).</summary>
    public required ImmutableArray<AiComposition> Compositions { get; init; }

    /// <summary>The army population, in half-pop, it gathers before sending a wave (<c>attack.minArmyPop</c>).</summary>
    public int AttackMinHalfPop { get; init; }

    /// <summary>The army's total cost (gold + wood) it gathers before sending a wave (<c>attack.minArmyCost</c>).</summary>
    public int AttackMinCost { get; init; }

    /// <summary>
    /// M7-6 (Request 52): this opening's own share of its wave the army must reach before the build order may save for a
    /// step (<c>attack.reserveArmyShare</c>, 0-1); null when the file leaves it out, and <see cref="AiRulesDef.ReserveArmyShare"/> applies.
    /// </summary>
    public float? ReserveArmyShare { get; init; }
}
