namespace Rts.Sim.Economy;

/// <summary>Why a tech can't be queued at a building (<see cref="World.CanResearch"/>, M3-5), in the order the rules are checked.</summary>
public enum ResearchError
{
    /// <summary>It can be queued.</summary>
    None = 0,

    /// <summary>No live building in that slot, another player's, or a construction site (sites have no queue).</summary>
    NoBuilding,

    /// <summary>The tech id is not in the data.</summary>
    UnknownTech,

    /// <summary>The tech is another faction's upgrade.</summary>
    WrongFaction,

    /// <summary>The building's type doesn't research it (another slot, or another faction's building).</summary>
    NotResearchedHere,

    /// <summary>
    /// The player hasn't met the tech's <c>requires</c> or <c>requiresAnyOf</c> (M3-6): a tech not researched, no own
    /// finished building of a required type, or too few of the listed slots filled (Age II: any two of the four halls).
    /// </summary>
    Requires,

    /// <summary>The player has already researched it.</summary>
    AlreadyResearched,

    /// <summary>The player already has it queued at one of its buildings (a tech is queued once at a time).</summary>
    AlreadyQueued,

    /// <summary>The queue already holds <see cref="EconomyConstants.ProductionQueueCapacity"/> items.</summary>
    QueueFull,

    /// <summary>The player has less gold or wood than the tech costs.</summary>
    CannotAfford,
}
