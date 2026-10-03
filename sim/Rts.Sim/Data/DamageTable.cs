using System.Collections.Immutable;

namespace Rts.Sim.Data;

/// <summary>Damage type × armor class multipliers from <c>common/damage_table.json</c> (docs/02).</summary>
/// <remarks>Armor class and damage type ids are dense ints in ordinal order of their string ids.</remarks>
public sealed class DamageTable
{
    internal DamageTable()
    {
    }

    /// <summary>Armor class string ids, indexed by armor class id.</summary>
    public required ImmutableArray<string> ArmorClassKeys { get; init; }
    /// <summary>Armor class display names, indexed by armor class id.</summary>
    public required ImmutableArray<string> ArmorClassNames { get; init; }
    /// <summary>Damage type string ids, indexed by damage type id.</summary>
    public required ImmutableArray<string> DamageTypeKeys { get; init; }
    /// <summary>Damage type display names, indexed by damage type id.</summary>
    public required ImmutableArray<string> DamageTypeNames { get; init; }
    /// <summary>Per damage type: true when armor is not subtracted (Magic in docs/02).</summary>
    public required ImmutableArray<bool> IgnoresArmor { get; init; }
    /// <summary>Flat table: entry <c>damageType * ArmorClassCount + armorClass</c>.</summary>
    public required ImmutableArray<float> Multipliers { get; init; }

    /// <summary>Number of armor classes.</summary>
    public int ArmorClassCount => ArmorClassKeys.Length;

    /// <summary>Multiplier for a damage type hitting an armor class.</summary>
    public float Multiplier(int damageType, int armorClass) => Multipliers[damageType * ArmorClassCount + armorClass];
}
