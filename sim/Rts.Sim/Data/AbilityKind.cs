namespace Rts.Sim.Data;

/// <summary>How an ability is aimed (docs/02 "Ability system"); spelled as <see cref="DataLimits.AbilityKindIds"/>. Target ground (M4-4a) and self / aura (M4-4b-3a) are supported.</summary>
public enum AbilityKind
{
    /// <summary>A point in range; the effects land on what is within the radius of it.</summary>
    TargetGround = 0,

    /// <summary>
    /// Self / aura (M4-4b-3a): no target; the command's point is ignored and the effects land within the radius of the
    /// caster's position at the resolve. The ability has no range.
    /// </summary>
    SelfAura = 1,
}
