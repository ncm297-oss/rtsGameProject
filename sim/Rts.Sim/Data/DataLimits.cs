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

    /// <summary>Largest side of a resource node or building footprint in cells (docs/02: the largest footprint, a Town Hall, is 4 x 4).</summary>
    public const int MaxFootprint = 4;

    /// <summary>JSON spelling of each <see cref="ResourceKind"/>, indexed by the enum value (docs/02 "Economy").</summary>
    public static readonly ImmutableArray<string> ResourceKindIds = ImmutableArray.Create("gold", "wood");

    /// <summary>JSON spelling of each <see cref="UnitSlot"/>, indexed by the enum value (docs/02 "Faction template").</summary>
    public static readonly ImmutableArray<string> SlotIds =
        ImmutableArray.Create("worker", "line", "ranged", "shock", "caster", "siege", "unique");

    /// <summary>JSON spelling of each <see cref="BuildingSlot"/>, indexed by the enum value (docs/02 "Buildings").</summary>
    public static readonly ImmutableArray<string> BuildingSlotIds = ImmutableArray.Create(
        "town_hall", "house", "camp", "infantry_hall", "ranged_hall", "shock_hall", "forge", "caster_hall", "siege_works", "watch_tower");

    /// <summary>JSON spelling of each <see cref="TechStat"/>, indexed by the enum value (docs/03 "Data format", M3-5).</summary>
    public static readonly ImmutableArray<string> TechStatIds = ImmutableArray.Create("attack", "armor", "range", "hp", "abilityCooldown");

    /// <summary>
    /// The common techs that advance a player's age, in order: researching entry k puts the player in Age k + 2 (docs/02
    /// "Ages": Age I at start, Age II researched). Each must exist in <c>common/techs.json</c>. A rule of the format.
    /// </summary>
    public static readonly ImmutableArray<string> AgeTechIds = ImmutableArray.Create("age_ii");
}
