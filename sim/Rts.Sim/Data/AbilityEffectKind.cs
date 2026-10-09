namespace Rts.Sim.Data;

/// <summary>One entry of the effect vocabulary (docs/02 "Ability system"); spelled as <see cref="DataLimits.AbilityEffectKindIds"/>. Supported since M4-4a: damage, applyStatus.</summary>
public enum AbilityEffectKind
{
    /// <summary>One hit of <see cref="AbilityEffect.Amount"/> of <see cref="AbilityEffect.DamageType"/>, through <c>DamageCalc</c>.</summary>
    Damage = 0,

    /// <summary>Puts <see cref="AbilityEffect.Status"/> on the unit with <see cref="AbilityEffect.Magnitude"/> for <see cref="AbilityEffect.DurationTicks"/>.</summary>
    ApplyStatus = 1,
}
