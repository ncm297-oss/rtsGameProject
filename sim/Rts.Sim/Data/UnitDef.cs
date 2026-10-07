using System.Collections.Immutable;

namespace Rts.Sim.Data;

/// <summary>One unit type from <c>factions/&lt;id&gt;/units.json</c>, converted to sim units (ticks, half-pop).</summary>
public sealed class UnitDef
{
    internal UnitDef()
    {
    }

    /// <summary>Dense unit id (index into <see cref="GameData.Units"/>).</summary>
    public int Id { get; init; }
    /// <summary>String id, e.g. <c>malazan_crossbowman</c>.</summary>
    public required string Key { get; init; }
    /// <summary>Owning faction id.</summary>
    public int Faction { get; init; }
    /// <summary>Template slot.</summary>
    public UnitSlot Slot { get; init; }
    /// <summary>Player-facing name.</summary>
    public required string DisplayName { get; init; }
    /// <summary>Player-facing tooltip text.</summary>
    public required string Description { get; init; }
    /// <summary>Model path under assets, unresolved until M2/M6.</summary>
    public required string Model { get; init; }
    /// <summary>Maximum hit points.</summary>
    public int Hp { get; init; }
    /// <summary>Flat armor.</summary>
    public int Armor { get; init; }
    /// <summary>Armor class id into <see cref="DamageTable"/>.</summary>
    public int ArmorClass { get; init; }
    /// <summary>The unit's attack.</summary>
    public required AttackDef Attack { get; init; }
    /// <summary>Movement speed in meters per tick.</summary>
    public float SpeedPerTick { get; init; }
    /// <summary>Sight radius in meters.</summary>
    public float Sight { get; init; }
    /// <summary>Collision radius in meters.</summary>
    public float Radius { get; init; }
    /// <summary>Gold cost.</summary>
    public int CostGold { get; init; }
    /// <summary>Wood cost.</summary>
    public int CostWood { get; init; }
    /// <summary>Population cost in half-pop units (pop 1 = 2, pop 1.5 = 3).</summary>
    public int HalfPop { get; init; }
    /// <summary>Ticks to train.</summary>
    public int TrainTicks { get; init; }
    /// <summary>String id of the building type that trains it (<c>trainedAt</c>).</summary>
    public required string TrainedAt { get; init; }
    /// <summary>Building type id (index into <see cref="GameData.Buildings"/>) of <see cref="TrainedAt"/>, resolved at load (M3-4): an own-faction building; -1 in hand-built data.</summary>
    /// <remarks>Set once by the loader after the buildings are read; never written afterwards.</remarks>
    public int TrainedAtTypeId { get; internal set; } = -1;
    /// <summary>Building / tech ids required to train it: each names one (checked at load, M3-5); whether they are met is M3-6.</summary>
    public required ImmutableArray<string> Requires { get; init; }
    /// <summary>Free-form tags used by bonuses and targeting (e.g. <c>infantry</c>).</summary>
    public required ImmutableArray<string> Tags { get; init; }
}
