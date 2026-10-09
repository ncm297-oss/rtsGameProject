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

    /// <summary>
    /// Largest sight radius in meters, for units and buildings (M4-3a): the fog stamps a precomputed circle per distinct
    /// radius, so a radius is a memory and stamping cost. Far above any designed sight (docs/02: 10-24 m).
    /// </summary>
    public const double MaxSight = 64;

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

    /// <summary>JSON spelling of each <see cref="AttackTargets"/>, indexed by the enum value (<c>attack.targets</c>, M4-2a).</summary>
    public static readonly ImmutableArray<string> AttackTargetIds = ImmutableArray.Create("all", "units", "buildings");

    /// <summary>JSON spelling of each <see cref="ProjectileKind"/>, indexed by the enum value (<c>common/projectiles.json</c>, M4-2b).</summary>
    public static readonly ImmutableArray<string> ProjectileKindIds = ImmutableArray.Create("aimed", "lob");

    /// <summary>
    /// An aimed projectile's <c>hitTolerance</c> in meters when the file gives none (docs/02 "Projectiles": collision
    /// radius + 0.3 m). The schema's default, like <c>attack.targets</c>' <c>all</c>; the shipped file writes it out.
    /// </summary>
    public const double DefaultHitTolerance = 0.3;

    /// <summary>Largest <c>hitTolerance</c> in meters: a cell, so an "aimed" shot can't silently become a sure hit at any miss distance.</summary>
    public const double MaxHitTolerance = 2.0;

    /// <summary>
    /// Slowest projectile <c>speed</c> in m/s (BUG-0182): far below any real shot (docs/02: 12-25 m/s) but a step a float
    /// holds, so a typo like <c>1e-50</c> is an error rather than a shot that hovers for an hour.
    /// </summary>
    public const double MinProjectileSpeed = 1.0;

    /// <summary>JSON spelling of each <see cref="TechStat"/>, indexed by the enum value (docs/03 "Data format", M3-5).</summary>
    public static readonly ImmutableArray<string> TechStatIds = ImmutableArray.Create("attack", "armor", "range", "hp", "abilityCooldown");

    /// <summary>
    /// The common techs that advance a player's age, in order: researching entry k puts the player in Age k + 2 (docs/02
    /// "Ages": Age I at start, Age II researched). Each must exist in <c>common/techs.json</c>. A rule of the format.
    /// </summary>
    public static readonly ImmutableArray<string> AgeTechIds = ImmutableArray.Create("age_ii");

    /// <summary>Most abilities one unit type may list (M4-4a): the command card's ability row (docs/02 "Controls and camera").</summary>
    public const int MaxUnitAbilities = 4;

    /// <summary>
    /// Largest ability <c>radius</c> in meters (M4-4a): the resolve queries the spatial hash over the circle, so a radius is a
    /// per-cast cost. Far above any designed area (docs/factions: 3-3.5 m); a Producer default.
    /// </summary>
    public const double MaxAbilityRadius = 16;

    /// <summary>JSON spelling of each <see cref="StatusKind"/>, indexed by the enum value (<c>common/statuses.json</c>, M4-4a).</summary>
    public static readonly ImmutableArray<string> StatusKindIds = ImmutableArray.Create("damageOverTime", "slow");

    /// <summary>JSON spelling of each supported <see cref="AbilityKind"/>, indexed by the enum value (M4-4a).</summary>
    public static readonly ImmutableArray<string> AbilityKindIds = ImmutableArray.Create("targetGround");

    /// <summary>Ability kinds of docs/02 the format knows but the sim doesn't run yet: refused as "not supported yet" (slice 2).</summary>
    public static readonly ImmutableArray<string> PlannedAbilityKindIds = ImmutableArray.Create("targetUnit", "selfAura", "summon");

    /// <summary>JSON spelling of each supported <see cref="AbilityEffectKind"/>, indexed by the enum value (M4-4a).</summary>
    public static readonly ImmutableArray<string> AbilityEffectKindIds = ImmutableArray.Create("damage", "applyStatus");

    /// <summary>Effect kinds of docs/02 the format knows but the sim doesn't run yet: refused as "not supported yet" (slice 2).</summary>
    public static readonly ImmutableArray<string> PlannedAbilityEffectKindIds = ImmutableArray.Create("createZone", "teleport", "spawn");

    /// <summary>JSON spelling of each <see cref="AbilityAffects"/>, indexed by the enum value (M4-4a).</summary>
    public static readonly ImmutableArray<string> AbilityAffectsIds = ImmutableArray.Create("enemy_units", "own_units", "all_units");
}
