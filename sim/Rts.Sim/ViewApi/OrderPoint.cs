using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;

namespace Rts.Sim.ViewApi;

/// <summary>
/// One point of a unit's order path (M6-V22, <see cref="OrderPath.Fill"/>): where its current order is taking it, or one
/// shift-queued entry. Plain data read from the sim; holds no state of its own.
/// </summary>
public struct OrderPoint
{
    /// <summary>The order's kind (Move, AttackMove, Attack, Gather, Build, Repair, UseAbility, Stop, HoldPosition).</summary>
    public CommandKind Kind;

    /// <summary>
    /// The place in meters (sim x, y): the order's point; a Build's footprint centre; an Attack's target where it stands now
    /// (only while <see cref="TargetLive"/>, else zero). Meaningless when <see cref="HasPlace"/> is false.
    /// </summary>
    public Vector2 Point;

    /// <summary>False for an entry with no place of its own (Stop, HoldPosition, a no-target ability): a drawing puts it at the previous point.</summary>
    public bool HasPlace;

    /// <summary>An Attack's target (a building handle when <see cref="TargetIsBuilding"/>); default for every other kind.</summary>
    public EntityHandle Target;

    /// <summary>True when <see cref="Target"/> is a building handle.</summary>
    public bool TargetIsBuilding;

    /// <summary>An Attack whose target handle still resolves to a live unit or building. A drawing still applies its own fog rule.</summary>
    public bool TargetLive;

    /// <summary>True for the order the unit is carrying out now (not a queue entry).</summary>
    public bool IsCurrent;
}
