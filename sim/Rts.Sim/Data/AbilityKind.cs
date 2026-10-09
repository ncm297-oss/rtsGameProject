namespace Rts.Sim.Data;

/// <summary>How an ability is aimed (docs/02 "Ability system"); spelled as <see cref="DataLimits.AbilityKindIds"/>. Only target ground is supported (M4-4a).</summary>
public enum AbilityKind
{
    /// <summary>A point in range; the effects land on what is within the radius of it.</summary>
    TargetGround = 0,
}
