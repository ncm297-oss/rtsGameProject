namespace Rts.Sim.Commands;

/// <summary>What a command asks the sim to do.</summary>
public enum CommandKind
{
    /// <summary>Does nothing; useful to test ordering and sequence numbers.</summary>
    Noop = 0,

    /// <summary>Spawns a unit of <see cref="Command.TypeId"/> at <see cref="Command.Position"/> (tests and dev console).</summary>
    SpawnUnit = 1,

    /// <summary>Orders <see cref="Command.Unit"/> to walk to <see cref="Command.Position"/>. A group move is one command per unit.</summary>
    Move = 2,

    /// <summary>Orders <see cref="Command.Unit"/> to stop: drop its orders and stand, goal-less and shovable.</summary>
    Stop = 3,

    /// <summary>As <see cref="Stop"/>, then hold the ground: never shoved and never walking back, until the next unqueued order.</summary>
    HoldPosition = 4,

    /// <summary>Orders <see cref="Command.Unit"/> to attack-move to <see cref="Command.Position"/>; walks like <see cref="Move"/> until combat (M4).</summary>
    AttackMove = 5,

    /// <summary>Places a building of <see cref="Command.TypeId"/> for the player with its anchor (lowest x, y) cell at <see cref="Command.Position"/> (dev and tests only until construction, M3-3).</summary>
    SpawnBuilding = 6,

    /// <summary>Orders worker <see cref="Command.Unit"/> to gather the resource node at <see cref="Command.Position"/> (any point of its footprint), carrying loads to drop-offs until told otherwise (M3-2).</summary>
    Gather = 7,

    /// <summary>Orders worker <see cref="Command.Unit"/> to build a <see cref="Command.TypeId"/> with its anchor cell at <see cref="Command.Position"/>: places the site (paying its cost) or joins the own site of that type already anchored there (M3-3).</summary>
    Build = 8,

    /// <summary>Cancels the player's own construction site covering <see cref="Command.Position"/>, refunding the unbuilt fraction of its cost (M3-3; not a unit order).</summary>
    Cancel = 9,

    /// <summary>Orders worker <see cref="Command.Unit"/> to repair the player's own damaged building covering <see cref="Command.Position"/> (M3-3).</summary>
    Repair = 10,
}
