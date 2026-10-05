namespace Rts.Sim.Replays;

/// <summary>The sim's <see cref="Simulation.StateHash"/> right after the tick that made <c>TickNumber</c> equal <see cref="Tick"/>.</summary>
public readonly record struct ReplayCheckpoint(int Tick, ulong Hash);
