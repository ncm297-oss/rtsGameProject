namespace Rts.Sim.Data;

/// <summary>One status a zone applies (M4-4b-2, an entry of a <c>createZone</c> effect's <c>statuses</c>), converted to sim units.</summary>
public readonly record struct ZoneStatus
{
    /// <summary>The status id (index into <see cref="GameData.Statuses"/>).</summary>
    public int Status { get; init; }
    /// <summary>As <see cref="AbilityEffect.Magnitude"/>: damage per second, a slow's fraction; 0 for a blind.</summary>
    public float Magnitude { get; init; }
    /// <summary>Ticks the status lasts from each application: the zone reapplies it every tick, so this is how long it lingers after a unit leaves.</summary>
    public int DurationTicks { get; init; }
}
