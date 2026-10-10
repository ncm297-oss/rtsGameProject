namespace Rts.Sim.Data;

/// <summary>How an ability is aimed (docs/02 "Ability system"); spelled as <see cref="DataLimits.AbilityKindIds"/>. Target ground (M4-4a), self / aura (M4-4b-3a) and summon (M4-4b-3b) are supported.</summary>
public enum AbilityKind
{
    /// <summary>A point in range; the effects land on what is within the radius of it.</summary>
    TargetGround = 0,

    /// <summary>
    /// Self / aura (M4-4b-3a): no target; the command's point is ignored and the effects land within the radius of the
    /// caster's position at the resolve. The ability has no range.
    /// </summary>
    SelfAura = 1,

    /// <summary>
    /// Summon (M4-4b-3b): no target and no range, cast like <see cref="SelfAura"/>; at the resolve its <c>spawn</c> effects put
    /// temporary units of the caster's owner on free cells round the caster (<see cref="AbilityEffectKind.Spawn"/>).
    /// </summary>
    Summon = 2,
}
