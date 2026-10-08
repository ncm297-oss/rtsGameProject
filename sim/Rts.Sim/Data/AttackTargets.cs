namespace Rts.Sim.Data;

/// <summary>What a unit's attack may target (<c>attack.targets</c>, M4-2a): the scan, retaliation, holding and the explicit Attack order all respect it.</summary>
/// <remarks>Order matches <see cref="DataLimits.AttackTargetIds"/>, which holds the JSON spelling.</remarks>
public enum AttackTargets : byte
{
    /// <summary>Units and buildings (the default when the field is absent).</summary>
    All = 0,
    /// <summary>Units only.</summary>
    Units = 1,
    /// <summary>Buildings only (the Battering Ram).</summary>
    Buildings = 2,
}
