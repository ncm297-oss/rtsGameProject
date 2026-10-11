using Rts.Sim.Commands;

namespace Rts.Sim.Ai;

/// <summary>
/// Where an <see cref="AiPlayer"/> sends its commands (M5-2): the sim's command door, handed to the AI as this small
/// interface so no AI file names the simulation (docs/03 "AI architecture", the source-scan rule).
/// </summary>
internal interface ICommandSink
{
    /// <summary>
    /// Queues <paramref name="command"/> through the door every input uses (<c>Simulation.Enqueue</c>: stamped for the next
    /// tick, recorded by a replay recorder); false, with nothing queued, when the queue is full or the command is malformed.
    /// </summary>
    bool TryEnqueue(in Command command);
}
