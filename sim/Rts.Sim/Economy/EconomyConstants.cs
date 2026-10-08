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
    /// (within ArrivalDistance of that point) is always in reach. Since M4-2a (BUG-0146) a footprint walker walks on to
    /// within about 0.3 m of the point while it can, so the slack behind it is left for the next worker in the queue.
    /// </summary>
    public const float GoalInset = (Reach - MovementConstants.ArrivalDistance) / 2f;

    /// <summary>
    /// Stand points a footprint walk may aim at per passable cell beside the footprint (BUG-0146): the shared edge's
    /// middle and one to either side, so several workers stand at a 1 x 1 tree open on one side instead of a column of
    /// which only the front one is in reach.
    /// </summary>
    public const int StandPointsPerCell = 3;

    /// <summary>
    /// Gap between neighbouring stand points along an edge, in walker radii: 1.5, more than twice
    /// <see cref="MovementConstants.ArrivalSpacing"/> (1.2), so workers on neighbouring points are not too crowded to
    /// stop, and less than 2, so three points fit on a 2 m cell edge for a 0.4 m worker.
    /// </summary>
    public const float StandPointSpacing = 1.5f;

    /// <summary>
    /// docs/02 "Buildings": n workers build in <c>t x 3 / (n + 2)</c> ticks (Age of Empires' formula). A site needs
    /// this many times its one-worker build ticks of work, and n builders in reach add n + <see cref="BuildWorkBase"/>
    /// a tick, so integer work gives the formula exactly. A rule of the formula, not a stat.
    /// </summary>
    public const int BuildWorkScale = 3;

    /// <summary>The "+ 2" of docs/02's <c>t x 3 / (n + 2)</c>: work a site gains per tick on top of one per builder in reach.</summary>
    public const int BuildWorkBase = 2;

    /// <summary>Fixed-point scale (2^16) for the repair factors and accumulators, so repair is integer math.</summary>
    public const int RepairFixedOne = 65536;

    /// <summary>docs/02 / docs/03 "Economy implementation": a building's production queue holds 5 items (the head one training). A rule of the queue, not a stat.</summary>
    public const int ProductionQueueCapacity = 5;
}
