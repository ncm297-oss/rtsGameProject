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
}
