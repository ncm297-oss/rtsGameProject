namespace Rts.Sim.Data;

/// <summary>One entry of the effect vocabulary (docs/02 "Ability system"); spelled as <see cref="DataLimits.AbilityEffectKindIds"/>. Supported since M4-4a: damage, applyStatus; since M4-4b-2: createZone.</summary>
public enum AbilityEffectKind
{
    /// <summary>One hit of <see cref="AbilityEffect.Amount"/> of <see cref="AbilityEffect.DamageType"/>, through <c>DamageCalc</c>.</summary>
    Damage = 0,

    /// <summary>Puts <see cref="AbilityEffect.Status"/> on the unit with <see cref="AbilityEffect.Magnitude"/> for <see cref="AbilityEffect.DurationTicks"/>.</summary>
    ApplyStatus = 1,

    /// <summary>
    /// Leaves a zone at the cast point (M4-4b-2): a circle of the ability's radius, owned by the caster's owner, that lasts
    /// the ability's duration, applies <see cref="AbilityEffect.ZoneStatuses"/> to the units inside every tick and, with
    /// <see cref="AbilityEffect.BlocksVision"/>, hides its cells from the other players' units outside it.
    /// </summary>
    CreateZone = 2,
}
