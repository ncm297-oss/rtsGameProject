namespace Rts.Sim.Data;

/// <summary>Which units an ability's effects land on (<c>affects</c>, M4-4a); spelled as <see cref="DataLimits.AbilityAffectsIds"/>. Buildings are never affected yet.</summary>
public enum AbilityAffects
{
    /// <summary>Units of any other player (Telas Fire).</summary>
    EnemyUnits = 0,

    /// <summary>The caster owner's units, the caster included.</summary>
    OwnUnits = 1,

    /// <summary>Every unit.</summary>
    AllUnits = 2,
}
