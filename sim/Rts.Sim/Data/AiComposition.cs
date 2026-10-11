using System.Collections.Immutable;

namespace Rts.Sim.Data;

/// <summary>A faction's target army mix for one age (M5-1, an entry of <c>ai.json</c> <c>compositions</c>).</summary>
public readonly record struct AiComposition
{
    /// <summary>The age it applies in (1 = Age I).</summary>
    public int Age { get; init; }

    /// <summary>Unit type ids of the faction, ascending.</summary>
    public ImmutableArray<int> Units { get; init; }

    /// <summary>Each unit's relative weight (above 0), in <see cref="Units"/> order.</summary>
    public ImmutableArray<float> Weights { get; init; }
}
