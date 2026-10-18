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

    /// <summary>The build order, in file order (at least one step).</summary>
    public required ImmutableArray<AiStep> BuildOrder { get; init; }

    /// <summary>The army mix per age, indexed by age - 1 (one per age: Age I and Age II).</summary>
    public required ImmutableArray<AiComposition> Compositions { get; init; }

    /// <summary>The army population, in half-pop, it gathers before sending a wave (<c>attack.minArmyPop</c>).</summary>
    public int AttackMinHalfPop { get; init; }

    /// <summary>The army's total cost (gold + wood) it gathers before sending a wave (<c>attack.minArmyCost</c>).</summary>
    public int AttackMinCost { get; init; }
}
