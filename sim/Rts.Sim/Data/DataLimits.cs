using System.Collections.Immutable;

namespace Rts.Sim.Data;

/// <summary>Schema limits the data loader enforces. These are rules of the format, not gameplay stats.</summary>
public static class DataLimits
{
    /// <summary>Smallest unit collision radius in meters (docs/02 "Template baseline stats").</summary>
    public const double MinUnitRadius = 0.4;

    /// <summary>Largest unit collision radius in meters: half a 2 m cell, one pathfinding size class (docs/02).</summary>
    public const double MaxUnitRadius = 1.0;

    /// <summary>Largest value a decimal field (meters, rates, multipliers) may hold; checked before narrowing to float (BUG-0007).</summary>
    public const double MaxDecimal = 1_000_000;

    /// <summary>Largest value an integer field may hold, so derived values such as half-pop (2 x popCap) stay inside int (BUG-0007).</summary>
    public const int MaxInteger = 1_000_000;

    /// <summary>Longest duration in seconds (one hour), so the conversion to ticks can't overflow int (BUG-0007).</summary>
    public const double MaxSeconds = 3600;

    /// <summary>JSON spelling of each <see cref="UnitSlot"/>, indexed by the enum value (docs/02 "Faction template").</summary>
    public static readonly ImmutableArray<string> SlotIds =
        ImmutableArray.Create("worker", "line", "ranged", "shock", "caster", "siege", "unique");
}
