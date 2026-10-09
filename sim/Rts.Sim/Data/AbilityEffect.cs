namespace Rts.Sim.Data;

/// <summary>One entry of an ability's <c>effects</c> (M4-4a), converted to sim units; the fields a kind doesn't use are -1 / 0.</summary>
public readonly record struct AbilityEffect
{
    /// <summary>What it does.</summary>
    public AbilityEffectKind Kind { get; init; }
    /// <summary>Damage: the damage type id; -1 otherwise.</summary>
    public int DamageType { get; init; }
    /// <summary>Damage: the attack value of the hit (at least 1); 0 otherwise.</summary>
    public int Amount { get; init; }
    /// <summary>ApplyStatus: the status id; -1 otherwise.</summary>
    public int Status { get; init; }
    /// <summary>ApplyStatus: damage per second (a whole number, at least 1) for damage over time, the fraction 0-1 (exclusive) for a slow; 0 otherwise.</summary>
    public float Magnitude { get; init; }
    /// <summary>ApplyStatus: how long the status lasts, in ticks (at least 1); 0 otherwise.</summary>
    public int DurationTicks { get; init; }
}
