using Rts.Sim.Movement;

namespace Rts.Sim.Economy;

/// <summary>Fixed gather-loop geometry and timing (docs/03 "Economy implementation", M3-2). Engine constants like <c>MapConstants.CellSize</c>, not gameplay stats: rates, carry and search radius come from <c>rules.json</c>.</summary>
public static class EconomyConstants
{
    /// <summary>A worker gathers or deposits when its center is at most this far (meters) from the node's or building's footprint rectangle.</summary>
    public const float Reach = 1.25f;

    /// <summary>A worker standing out of reach on its gather loop (it arrived behind others, or gave up) walks again after this many ticks: the queue at a mine's edge.</summary>
    public const int RetryTicks = 20;

    /// <summary>
    /// How far (meters) outside the footprint edge a gather walk aims: half the slack between
    /// <see cref="Reach"/> and <see cref="MovementConstants.ArrivalDistance"/>, so a walker that arrives
    /// (within ArrivalDistance of that point) is always in reach.
    /// </summary>
    public const float GoalInset = (Reach - MovementConstants.ArrivalDistance) / 2f;
}
