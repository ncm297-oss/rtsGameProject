namespace Rts.Sim;

/// <summary>Fixed timing constants of the simulation (docs/03 "Tick model").</summary>
public static class SimConstants
{
    /// <summary>Length of one sim tick in milliseconds.</summary>
    public const int TickMs = 50;

    /// <summary>Number of sim ticks per second of game time.</summary>
    public const int TicksPerSecond = 1000 / TickMs;
}
