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
    /// <summary>True for the worker slot (<see cref="UnitSlot.Worker"/>): the one "is a worker" rule (gathering, building, combat's scan rule, the AI, the M6-S1 army value).</summary>
    public bool IsWorker => Slot == UnitSlot.Worker;
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
    /// <summary>Building / tech ids required to train it, as written: each names one (checked at load, M3-5). Kept for tools; the sim reads <see cref="RequiresTechs"/> / <see cref="RequiresBuildings"/>.</summary>
    public required ImmutableArray<string> Requires { get; init; }
    /// <summary><see cref="Requires"/>' tech ids, resolved at load (M3-6), ascending: each must be researched to queue the unit.</summary>
    /// <remarks>Set once by the loader; never written afterwards. Empty in hand-built data.</remarks>
    public ImmutableArray<int> RequiresTechs { get; internal set; } = ImmutableArray<int>.Empty;
    /// <summary><see cref="Requires"/>' building type ids, resolved at load (M3-6), ascending: the player needs an own finished building of each type.</summary>
    /// <remarks>Set once by the loader; never written afterwards. Empty in hand-built data.</remarks>
    public ImmutableArray<int> RequiresBuildings { get; internal set; } = ImmutableArray<int>.Empty;
    /// <summary>Free-form tags used by bonuses and targeting (e.g. <c>infantry</c>).</summary>
    public required ImmutableArray<string> Tags { get; init; }
    /// <summary>
    /// Ability ids (index into <see cref="GameData.Abilities"/>) of the unit's <c>abilities</c> list, in file order (M4-4a):
    /// own-faction abilities, at most <see cref="DataLimits.MaxUnitAbilities"/>, no repeats. A <c>UseAbility</c> names one by
    /// its index in this list.
    /// </summary>
    /// <remarks>Set once by the loader; never written afterwards. Empty in hand-built data and for a unit without abilities.</remarks>
    public ImmutableArray<int> Abilities { get; internal set; } = ImmutableArray<int>.Empty;
    /// <summary>
    /// The unit's <c>passives</c> in file order (M4-4b-3a): at most <see cref="DataLimits.MaxUnitPassives"/>, one per trigger.
    /// Data, so never hashed as state; <see cref="GameData.ContentHash"/> covers it.
    /// </summary>
    /// <remarks>Set once by the loader, with <see cref="BelowHpPassive"/> and <see cref="DeathPassive"/>; never written afterwards. Empty in hand-built data.</remarks>
    public ImmutableArray<UnitPassive> Passives { get; internal set; } = ImmutableArray<UnitPassive>.Empty;
    /// <summary>The index in <see cref="Passives"/> of its <see cref="PassiveTrigger.WhenBelowHp"/> passive, or -1 (the per-type flag phase 5 reads, M4-4b-3a).</summary>
    public int BelowHpPassive { get; internal set; } = -1;
    /// <summary>The index in <see cref="Passives"/> of its <see cref="PassiveTrigger.OnDeath"/> passive, or -1 (the per-type flag the death path reads, M4-4b-3a).</summary>
    public int DeathPassive { get; internal set; } = -1;
    /// <summary>
    /// Detection radius in meters (M4-5: <c>detector</c>, optional, docs/02 "Stealth and detection": casters 10 m); above 0
    /// and at most <see cref="DataLimits.MaxSight"/>; 0 for none.
    /// </summary>
    public float Detector { get; init; }
}
