namespace Rts.Sim.Economy;

/// <summary>Why a unit can't be queued at a building (<see cref="World.CanTrain"/>, M3-4), in the order the rules are checked.</summary>
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

    /// <summary>The unit type has a <c>requires</c> list (an Age II unique); requirements are resolved with techs (M3-5 / M3-6).</summary>
    LockedByRequirement,

    /// <summary>The queue already holds <see cref="EconomyConstants.ProductionQueueCapacity"/> items.</summary>
    QueueFull,

    /// <summary>The player has less gold or wood than the unit costs.</summary>
    CannotAfford,
}
