namespace Rts.Sim.Commands;

/// <summary>
/// M5-5: why phase 1 dropped an accepted command when it applied (docs/03 "Implementation (M5-5)"). Reported only through the
/// test seam <c>Simulation.RefusedForTests</c>; the sim never stores or reads it, so it is no state and never hashed.
/// </summary>
public enum CommandRefusal
{
    /// <summary>Applied (nothing dropped).</summary>
    None = 0,

    /// <summary>A bad payload <see cref="Command.IsValid"/> refuses (a NaN position).</summary>
    Invalid,

    /// <summary>The player is defeated (M5-4b): every kind is dropped from the tick after the defeat.</summary>
    PlayerDefeated,

    /// <summary>A unit order for a dead or recycled handle (the unit died after the order was sent).</summary>
    UnitGone,

    /// <summary>A unit order for another player's unit.</summary>
    NotOwner,

    /// <summary>A positional order whose point is off the map.</summary>
    OffMap,

    /// <summary>A Gather, Build or Repair for a unit that is no worker.</summary>
    NotAWorker,

    /// <summary>An Attack on something the unit may not fight (dead, unseen, an ally, its own).</summary>
    AttackTargetInvalid,

    /// <summary>A UseAbility the unit's type lacks, on cooldown, or out of its rules.</summary>
    AbilityUnavailable,

    /// <summary>A queued (shift) order for a unit whose order queue is full.</summary>
    UnitQueueFull,

    /// <summary>A Build that neither places a site nor joins one; the detail is the <see cref="Economy.PlacementError"/>.</summary>
    BuildRefused,

    /// <summary>A Repair with no own finished, hurt building at the point.</summary>
    NothingToRepair,

    /// <summary>A Gather with no node to work at the point.</summary>
    NoResourceNode,

    /// <summary>A Move or AttackMove whose point resolves to no passable cell.</summary>
    NoPassableCell,

    /// <summary>A Train the building refuses; the detail is the <see cref="Economy.TrainError"/>.</summary>
    TrainRefused,

    /// <summary>A Research the building refuses; the detail is the <see cref="Economy.ResearchError"/>.</summary>
    ResearchRefused,

    /// <summary>A Cancel with no own site at the point.</summary>
    NoSiteToCancel,

    /// <summary>A CancelTrain with no own finished building there or no such queue item.</summary>
    NoItemToCancel,

    /// <summary>A SetRally or ClearRally with no own finished building there (or a SetRally point off the map).</summary>
    NoRallyBuilding,

    /// <summary>A SpawnUnit or SpawnBuilding (dev and tests) that does not fit: an unknown type, a full store, a blocked footprint.</summary>
    SpawnRefused,
}
