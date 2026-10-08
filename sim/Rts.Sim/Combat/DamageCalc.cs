using System;
using Rts.Sim.Data;

namespace Rts.Sim.Combat;

/// <summary>The damage formula (docs/02 "Damage formula"), in one place.</summary>
public static class DamageCalc
{
    /// <summary>
    /// Damage of one hit: <c>raw = attack x table[damageType][armorClass] x bonusVs</c>, then <c>max(1, round(raw) - armor)</c>,
    /// or <c>max(1, round(raw))</c> for a damage type that ignores armor (Magic). <paramref name="attack"/> and
    /// <paramref name="armor"/> already include tech bonuses.
    /// </summary>
    /// <remarks>
    /// <c>round</c> is half up, <c>floor(raw + 0.5)</c> in float, so 6.5 gives 7 (banker's rounding would give 6) and
    /// the docs' worked example <c>9 x 0.6 x 1.3 = 7.02</c> gives 7. All of it is float multiply and floor, so the same
    /// inputs give the same damage in every run.
    /// </remarks>
    public static int Compute(DamageTable table, int damageType, int armorClass, int attack, float bonusVs, int armor)
    {
        float raw = attack * table.Multiplier(damageType, armorClass) * bonusVs;
        int rounded = (int)MathF.Floor(raw + 0.5f);
        int damage = table.IgnoresArmor[damageType] ? rounded : rounded - armor;
        return Math.Max(1, damage);
    }

    /// <summary>
    /// One hit of <paramref name="attacker"/>'s attack on a target of <paramref name="armorClass"/> with
    /// <paramref name="armor"/> (already including the target owner's tech bonus): the attack's <c>bonusVs</c> for the
    /// class (1 where the data gives none) and the attacker owner's attack bonus <paramref name="attackBonus"/> applied.
    /// </summary>
    public static int Compute(DamageTable table, AttackDef attacker, int attackBonus, int armorClass, int armor)
    {
        float bonusVs = (uint)armorClass < (uint)attacker.BonusVs.Length ? attacker.BonusVs[armorClass] : 1f;
        return Compute(table, attacker.DamageType, armorClass, attacker.Value + attackBonus, bonusVs, armor);
    }

    /// <summary>A tech bonus sum (whole points by the data's rule) as an integer, rounded half up like the damage.</summary>
    public static int Points(float bonus) => (int)MathF.Floor(bonus + 0.5f);
}
