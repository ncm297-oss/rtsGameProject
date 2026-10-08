namespace Rts.Sim.Data;

/// <summary>How a projectile resolves on arrival (docs/02 "Projectiles", M4-2b).</summary>
public enum ProjectileKind
{
    /// <summary>Hits its target if the target is still within its radius + the projectile's hit tolerance of the impact point; otherwise it lands harmlessly.</summary>
    Aimed = 0,

    /// <summary>A ground-targeted lob: always explodes at the impact point (the attack's splash).</summary>
    Lob = 1,
}
