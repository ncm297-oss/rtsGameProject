namespace Rts.Sim.Data;

/// <summary>What a status does (<c>common/statuses.json</c> <c>kind</c>, M4-4a); spelled as <see cref="DataLimits.StatusKindIds"/>.</summary>
public enum StatusKind
{
    /// <summary>Damage over time: the applying effect's magnitude is damage per second of the status's damage type (Burning).</summary>
    DamageOverTime = 0,

    /// <summary>Movement speed x (1 - magnitude) (Slowed); the strongest slow on a unit wins.</summary>
    Slow = 1,

    /// <summary>
    /// Blinded (M4-4b-2): the unit's fog circle shrinks to the status's <see cref="StatusDef.Sight"/> and it neither takes
    /// nor strikes a target farther than <see cref="StatusDef.Reach"/>; the strongest (smallest sight) blind on a unit wins.
    /// </summary>
    Blind = 2,
}
