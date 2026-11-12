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
    /// M7-4, a <see cref="StatusKind.Frenzied"/> status only (<c>damageBonus</c>, optional): its bearer's own attacks deal
    /// <c>attack x (1 + bonus)</c>, the largest bonus among its frenzied-kind entries; 0 (none) by default and on any other kind.
    /// </summary>
    public float DamageBonus { get; init; }
    /// <summary>
    /// M7-4 (<c>onEnd</c>, optional, any kind): the status applied to the bearer when an entry of this status runs out of
    /// time in phase 5 (never on death, a refresh or an early removal); -1 for none. Never a status with its own
    /// <c>onEnd</c>, never this status itself.
    /// </summary>
    /// <remarks>Set once by the loader (it names another row, resolved after every row is read); never written afterwards.</remarks>
    public int OnEndStatus { get; internal set; } = -1;
    /// <summary>M7-4: the <see cref="OnEndStatus"/> application's magnitude (by that status's kind, as an <c>applyStatus</c>'s); 0 with none.</summary>
    public float OnEndMagnitude { get; internal set; }
    /// <summary>M7-4: the <see cref="OnEndStatus"/> application's duration in ticks (at least 1); 0 with none.</summary>
    public int OnEndTicks { get; internal set; }
    /// <summary>
    /// M7-1: the number a unit's entry of this status adds to the state hash in place of its id: the statuses of the M4 kinds
    /// first (<see cref="StatusKind.Revealed"/> and below) in id order, then the newer kinds in id order. A status of a new
    /// kind sorts among the old ids (<c>regenerating</c> before <c>revealed</c>) and so renumbers them, but its hash number
    /// doesn't renumber theirs: a match without it hashes as before it existed. -1 in hand-built data (the id is hashed).
    /// </summary>
    /// <remarks>Set once by the loader; never written afterwards. In <see cref="GameData.ContentHash"/> (derived from the ids and kinds, which it covers too).</remarks>
    public int StateHashId { get; internal set; } = -1;
}
