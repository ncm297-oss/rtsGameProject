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
    /// <summary>
    /// M7-1: the number a unit's entry of this status adds to the state hash in place of its id: the statuses of the M4 kinds
    /// first (<see cref="StatusKind.Revealed"/> and below) in id order, then the newer kinds in id order. A status of a new
    /// kind sorts among the old ids (<c>regenerating</c> before <c>revealed</c>) and so renumbers them, but its hash number
    /// doesn't renumber theirs: a match without it hashes as before it existed. -1 in hand-built data (the id is hashed).
    /// </summary>
    /// <remarks>Set once by the loader; never written afterwards. In <see cref="GameData.ContentHash"/> (derived from the ids and kinds, which it covers too).</remarks>
    public int StateHashId { get; internal set; } = -1;
}
