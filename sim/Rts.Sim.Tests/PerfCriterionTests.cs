using System.Diagnostics;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// The M1 exit criterion "Perf test: 500 moving units, average tick &lt; 4 ms on the dev machine"
/// (docs/05-roadmap.md, docs/03 "Testing strategy"), enforced.
/// </summary>
[Collection(SerialCollection.Name)]
public class PerfCriterionTests
{
    private const int Units = 500;
    private const int WarmUpTicks = 5;
    private const int MeasuredTicks = 200;
    private const double BudgetMs = 4.0;
    private const double MinMovingFraction = 0.95;

    private readonly ITestOutputHelper _out;

    public PerfCriterionTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// 500 units of every shipped type, two players, on the default 128 x 128 map, spawned within 12
    /// path cells of the center and ordered to the passable cell farthest from it, so the walk is far
    /// longer than the measurement: at least 95% are Moving after every measured tick (asserted, so
    /// the measurement can't quietly turn into idle ticks). 5 warm-up ticks (they apply the orders and
    /// build the field), then 200 timed one by one; average under 4 ms.
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void FiveHundredMovingUnits_AverageTickUnder4Ms()
    {
        Simulation sim = MoveScenario.Spawn(seed: 7, units: Units, maxCost: 12f, out int center);
        NavGrid g = sim.World.NavGrid;
        Assert.Equal(MapGenParams.Default.Width, g.Width);
        Assert.Equal(MapGenParams.Default.Height, g.Height);
        FlowField fromCenter = FlowField.Build(g, center);
        int far = center;
        for (int c = 0; c < g.Width * g.Height; c++)
            if (float.IsFinite(fromCenter.CostAt(c)) && fromCenter.CostAt(c) > fromCenter.CostAt(far)) far = c;
        // The fastest unit covers at most this many cells during the run; the target is farther from every spawn.
        float maxCells = sim.World.MaxUnitSpeed * (WarmUpTicks + MeasuredTicks) / MapConstants.CellSize;
        Assert.True(fromCenter.CostAt(far) - 12f > maxCells, $"target only {fromCenter.CostAt(far)} cells out; units could arrive within {maxCells}");

        MoveScenario.MoveAll(sim, MoveScenario.Center(g, far));
        for (int t = 0; t < WarmUpTicks; t++) sim.Tick();

        UnitStore u = sim.World.Units;
        var ms = new double[MeasuredTicks];
        int fewestMoving = Units, fewestWalking = Units;
        long freq = Stopwatch.Frequency;
        for (int t = 0; t < MeasuredTicks; t++)
        {
            long start = Stopwatch.GetTimestamp();
            sim.Tick();
            ms[t] = (Stopwatch.GetTimestamp() - start) * 1000.0 / freq;
            int moving = 0, walking = 0;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i] || u.State[i] != UnitState.Moving) continue;
                moving++;
                if (u.Velocity[i] != System.Numerics.Vector2.Zero) walking++; // stepped this tick (not waiting or blocked)
            }
            fewestMoving = Math.Min(fewestMoving, moving);
            fewestWalking = Math.Min(fewestWalking, walking);
            Assert.True(moving >= MinMovingFraction * Units, $"tick {sim.TickNumber}: only {moving} of {Units} units Moving; the measurement is not representative");
        }

        double avg = ms.Average();
        double[] sorted = (double[])ms.Clone();
        Array.Sort(sorted);
        double p99 = sorted[(int)Math.Ceiling(0.99 * MeasuredTicks) - 1];
        double worst = sorted[^1];
        _out.WriteLine($"{Units} moving units (fewest on a measured tick: {fewestMoving} Moving, {fewestWalking} stepping), {MeasuredTicks} ticks: avg {avg:F3} ms, p99 {p99:F3} ms, worst {worst:F3} ms (budget: avg < {BudgetMs} ms)");
        Assert.True(avg < BudgetMs, $"average tick {avg:F3} ms (p99 {p99:F3}, worst {worst:F3}) is over the {BudgetMs} ms budget");
    }
}
