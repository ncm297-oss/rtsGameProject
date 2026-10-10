namespace Rts.Sim.Data;

/// <summary>One status type from <c>common/statuses.json</c> (M4-4a): what a status id does; the magnitude and duration come from the effect applying it.</summary>
public sealed class StatusDef
{
    internal StatusDef()
    {
    }

    /// <summary>Dense status id (index into <see cref="GameData.Statuses"/>).</summary>
    public int Id { get; init; }
    /// <summary>String id, e.g. <c>burning</c>.</summary>
    public required string Key { get; init; }
    /// <summary>Player-facing name.</summary>
    public required string DisplayName { get; init; }
    /// <summary>Player-facing tooltip text.</summary>
    public required string Description { get; init; }
    /// <summary>What it does.</summary>
    public StatusKind Kind { get; init; }
    /// <summary>Damage type id of a <see cref="StatusKind.DamageOverTime"/> status; -1 for any other kind.</summary>
    public int DamageType { get; init; } = -1;
    /// <summary>Blind (M4-4b-2): the sight in meters of a unit with this status (its fog circle); 0 for any other kind.</summary>
    public float Sight { get; init; }
    /// <summary>Blind (M4-4b-2): the farthest in meters a unit with this status takes or strikes a target (center to center; to a footprint for a building); 0 for any other kind.</summary>
    public float Reach { get; init; }
}
