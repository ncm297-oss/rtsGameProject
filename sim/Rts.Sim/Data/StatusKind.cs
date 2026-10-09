namespace Rts.Sim.Data;

/// <summary>What a status does (<c>common/statuses.json</c> <c>kind</c>, M4-4a); spelled as <see cref="DataLimits.StatusKindIds"/>.</summary>
public enum StatusKind
{
    /// <summary>Damage over time: the applying effect's magnitude is damage per second of the status's damage type (Burning).</summary>
    DamageOverTime = 0,

    /// <summary>Movement speed x (1 - magnitude) (Slowed); the strongest slow on a unit wins.</summary>
    Slow = 1,
}
