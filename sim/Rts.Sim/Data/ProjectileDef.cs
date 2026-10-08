namespace Rts.Sim.Data;

/// <summary>One projectile type from <c>common/projectiles.json</c> (M4-2b), converted to sim units.</summary>
public sealed class ProjectileDef
{
    internal ProjectileDef()
    {
    }

    /// <summary>Dense projectile type id (index into <see cref="GameData.Projectiles"/>).</summary>
    public int Id { get; init; }
    /// <summary>String id, e.g. <c>bolt</c>; what a unit's <c>attack.projectile</c> names.</summary>
    public required string Key { get; init; }
    /// <summary>Aimed (can miss) or lob (always explodes).</summary>
    public ProjectileKind Kind { get; init; }
    /// <summary>Flight speed in meters per tick (the data's m/s / 20).</summary>
    public float SpeedPerTick { get; init; }
    /// <summary>Meters past the target's collision radius it may stand off the impact point and still be hit; aimed only, 0 for a lob.</summary>
    public float HitTolerance { get; init; }
    /// <summary>
    /// Fastest target, in meters per tick, the shooter leads (BUG-0183): an aimed shot at a unit walking no faster flies to
    /// where that unit will be at its current step when the shot lands; a faster one is shot where it is and can dodge.
    /// 0 (the default, and always for a lob): never leads.
    /// </summary>
    public float LeadSpeedPerTick { get; init; }
}
