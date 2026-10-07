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

    /// <summary>
    /// docs/02 "Buildings": n workers build in <c>t x 3 / (n + 2)</c> ticks (Age of Empires' formula). A site needs
    /// this many times its one-worker build ticks of work, and n builders in reach add n + <see cref="BuildWorkBase"/>
    /// a tick, so integer work gives the formula exactly. A rule of the formula, not a stat.
    /// </summary>
    public const int BuildWorkScale = 3;

    /// <summary>The "+ 2" of docs/02's <c>t x 3 / (n + 2)</c>: work a site gains per tick on top of one per builder in reach.</summary>
    public const int BuildWorkBase = 2;

    /// <summary>Rings of cells round a new site searched for a free cell to push a unit standing in its footprint to; past them the nearest passable cell is used.</summary>
    public const int PushRings = 8;

    /// <summary>Fixed-point scale (2^16) for the repair factors and accumulators, so repair is integer math.</summary>
    public const int RepairFixedOne = 65536;
}
