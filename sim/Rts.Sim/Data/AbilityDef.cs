using System.Collections.Immutable;

namespace Rts.Sim.Data;

/// <summary>One ability from <c>factions/&lt;id&gt;/abilities.json</c> (M4-4a), converted to sim units (ticks, meters).</summary>
public sealed class AbilityDef
{
    internal AbilityDef()
    {
    }

    /// <summary>Dense ability id (index into <see cref="GameData.Abilities"/>), ordinal order of the string ids across factions.</summary>
    public int Id { get; init; }
    /// <summary>String id, e.g. <c>telas_fire</c>.</summary>
    public required string Key { get; init; }
    /// <summary>Owning faction id: only its units may list it.</summary>
    public int Faction { get; init; }
    /// <summary>Player-facing name.</summary>
    public required string DisplayName { get; init; }
    /// <summary>Player-facing tooltip text.</summary>
    public required string Description { get; init; }
    /// <summary>How it is aimed.</summary>
    public AbilityKind Kind { get; init; }
    /// <summary>Farthest the target point may be from the caster's center, in meters.</summary>
    public float Range { get; init; }
    /// <summary>The effects land on units whose center is within this many meters of the point.</summary>
    public float Radius { get; init; }
    /// <summary>Ticks the caster stands before the effects land (0: on the tick the cast starts).</summary>
    public int CastTicks { get; init; }
    /// <summary>Ticks from the resolve until the caster may use it again (at least 1).</summary>
    public int CooldownTicks { get; init; }
    /// <summary>The ability's own <c>duration</c> in ticks (0 when absent); unused until zones (slice 2).</summary>
    public int DurationTicks { get; init; }
    /// <summary>Which units the effects land on.</summary>
    public AbilityAffects Affects { get; init; }
    /// <summary>The effects, in file order; applied in this order to each affected unit.</summary>
    public required ImmutableArray<AbilityEffect> Effects { get; init; }
    /// <summary>Whether any damage effect reaches buildings (M4-4b-1, <see cref="AbilityEffect.Buildings"/>): the resolve looks at buildings only then.</summary>
    public bool HitsBuildings { get; init; }
}
