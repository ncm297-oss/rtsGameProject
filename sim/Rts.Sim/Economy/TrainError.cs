namespace Rts.Sim.Economy;

/// <summary>
/// Why a unit can't be queued at a building (<see cref="World.CanTrain"/>, M3-4), in the order the rules are checked, except
/// <see cref="LimitReached"/> (M8-4), appended last but checked between <see cref="LockedByRequirement"/> and <see cref="QueueFull"/>.
/// </summary>
public enum TrainError
{
    /// <summary>It can be queued.</summary>
    None = 0,

    /// <summary>No live building in that slot, another player's, or a construction site (sites have no queue).</summary>
    NoBuilding,

    /// <summary>The unit type id is not in the data.</summary>
    UnknownType,

    /// <summary>The unit type belongs to another faction than the player's.</summary>
    WrongFaction,

    /// <summary>The unit type's <c>trainedAt</c> names another building type.</summary>
    NotTrainedHere,

    /// <summary>The player hasn't met the unit type's <c>requires</c> (M3-6): a tech not researched (the uniques need Age II), or no own finished building of a required type.</summary>
    LockedByRequirement,

    /// <summary>The queue already holds <see cref="EconomyConstants.ProductionQueueCapacity"/> items.</summary>
    QueueFull,

    /// <summary>The player has less gold or wood than the unit costs.</summary>
    CannotAfford,

    /// <summary>
    /// M8-4 (Request 56 G4): the unit type's <c>maxAlive</c> would be passed: the player's live units of the type plus those its
    /// queued items will spawn, plus this item's <c>spawnCount</c>, is above the limit. Appended last so no value moves, but
    /// checked after <see cref="LockedByRequirement"/> and before <see cref="QueueFull"/> and <see cref="CannotAfford"/>.
    /// </summary>
    LimitReached,
}
