namespace Rts.Sim.Commands;

/// <summary>What a command asks the sim to do.</summary>
public enum CommandKind
{
    /// <summary>Does nothing; useful to test ordering and sequence numbers.</summary>
    Noop = 0,

    /// <summary>Spawns a unit of <see cref="Command.TypeId"/> at <see cref="Command.Position"/> (tests and dev console).</summary>
    SpawnUnit = 1,
}
