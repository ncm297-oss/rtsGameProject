namespace Rts.Sim;

/// <summary>One player defeated in the last tick run (M5-4b): read through <see cref="World.DefeatEvents"/>.</summary>
/// <param name="Player">The player who lost their last building.</param>
/// <param name="Tick">The number of the tick it happened in (one less than <see cref="World.TickNumber"/> when read between ticks).</param>
public readonly record struct DefeatEvent(int Player, int Tick);
