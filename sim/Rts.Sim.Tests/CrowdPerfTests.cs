using System.Diagnostics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// The 2,500-unit crowd perf rows of docs/03 "Local movement" (BUG-0044, BUG-0047 item 1): the
/// one-player tight blob, enforced at the M1-4d-3 target, and the rows where two players' units
/// press into each other all the time (a contested point, four points of alternating owners),
/// which cost the plug test the most.
/// </summary>
/// <remarks>
/// Debug wall clock, like every tick budget in docs/03; the class runs alone (<see cref="SerialCollection"/>).
/// A failing row is re-run once alone before it counts: other test processes on the machine slow it.
/// </remarks>
[Collection(SerialCollection.Name)]
public class CrowdPerfTests
{
    private const int Units = 2500;
    private const int WarmUpTicks = 5;
    private const int MeasuredTicks = 300;

    /// <summary>
    /// The one-player 2,500 tight blob's budget: the M1-4d-3 target of 4.5 ms, widened to 4.6 ms in M4-1 (BUG-0140,
    /// approved by the Producer in advance): M4-1's per-unit combat checks (a chaser's arrival distance, the Attacking
    /// test in the hard-wall and shove rules, phase 7's per-unit loop) cost about 0.08 ms here, which left the row 0.05 ms
    /// under 4.5 and failing 1 run in 5-6. See docs/03 "Implementation (M4-1)", Cost.
    /// </summary>
    private const double TightBlobBudgetMs = 4.6;

    /// <summary>
    /// Guard for the two-player rows: what they cost before the plug test was cached (BUG-0044: contested
    /// blob 10.5 ms, 4 points 3.7 ms). Not a design budget; it fails a return of that regression.
    /// </summary>
    private const double ContestedBlobBeforeMs = 10.5;
    private const double FourPointsBeforeMs = 3.7;

    private readonly ITestOutputHelper _out;

    public CrowdPerfTests(ITestOutputHelper output) => _out = output;

    /// <summary>All units spawned within 12 path cells of the central cell and ordered to its center (seed 99, the stress suite's tight blob).</summary>
    private static Simulation TightBlob(int players, bool combat = true)
    {
        Simulation sim = MoveScenario.Spawn(seed: 99, units: Units, maxCost: 12f, out int goalCell, players: players, combat: combat);
        MoveScenario.MoveAll(sim, MoveScenario.Center(sim.World.NavGrid, goalCell));
        sim.Tick();
        return sim;
    }

    /// <summary>Average and worst of <see cref="MeasuredTicks"/> timed ticks after <see cref="WarmUpTicks"/> warm-up ticks and a full GC.</summary>
    private static (double Avg, double Worst) Measure(Simulation sim, int ticks = MeasuredTicks)
    {
        for (int t = 0; t < WarmUpTicks; t++) sim.Tick(); // JIT, field build
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var ms = new double[ticks];
        long freq = Stopwatch.Frequency;
        for (int t = 0; t < ticks; t++)
        {
            long start = Stopwatch.GetTimestamp();
            sim.Tick();
            ms[t] = (Stopwatch.GetTimestamp() - start) * 1000.0 / freq;
        }
        return (ms.Average(), ms.Max());
    }

    private static int CountMoving(UnitStore u)
    {
        int n = 0;
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.State[i] == UnitState.Moving) n++;
        return n;
    }

    /// <summary>BUG-0047 item 1: the 2,500-unit one-player tight blob averages at most 4.6 ms per tick (Debug; 4.5 until M4-1, BUG-0140).</summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void TightBlob2500_OnePlayer_AverageTickAtMost4_6Ms()
    {
        Simulation sim = TightBlob(players: 1);
        (double avg, double worst) = Measure(sim);
        _out.WriteLine($"{Units} units, one player, tight blob: avg {avg:F2} ms, worst {worst:F2} ms, {CountMoving(sim.World.Units)} still moving after {MeasuredTicks} ticks (target <= {TightBlobBudgetMs} ms)");
        Assert.True(avg <= TightBlobBudgetMs, $"avg {avg:F2} ms (target <= {TightBlobBudgetMs} ms)");
    }

    /// <summary>
    /// BUG-0044: the same blob with owners alternating, so both players contest one point and their
    /// units overlap every tick (the plug test's worst case). Reported, and guarded against the
    /// uncached cost.
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void ContestedBlob2500_TwoPlayers_Report()
    {
        Simulation sim = TightBlob(players: 2, combat: false); // a movement cost row: the two owners contest, never fight (BUG-0135)
        (double avg, double worst) = Measure(sim);
        _out.WriteLine($"{Units} units, two players contesting one point: avg {avg:F2} ms, worst {worst:F2} ms, {CountMoving(sim.World.Units)} still moving after {MeasuredTicks} ticks (uncached plug test: {ContestedBlobBeforeMs} ms)");
        Assert.True(avg < ContestedBlobBeforeMs, $"avg {avg:F2} ms: back at the uncached plug test's {ContestedBlobBeforeMs} ms (BUG-0044)");
    }

    /// <summary>BUG-0044: 2,500 units to 4 points 6 m apart, one player per point (neighbors the enemy's), seed 1, 600 ticks. Reported, and guarded like the contested blob.</summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void FourPoints2500_OnePlayerPerPoint_Report()
    {
        Simulation sim = MoveScenario.Spawn(1, Units, 70f, out int goalCell, combat: false); // a movement cost row (BUG-0135)
        var goals = CrowdRows.FourPoints(MoveScenario.Center(sim.World.NavGrid, goalCell));
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
            sim.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), goals[CrowdRows.PointOf(u, i, onePlayerPerPoint: true)]));
        sim.Tick();
        (double avg, double worst) = Measure(sim, 600);
        _out.WriteLine($"{Units} units to 4 points, one player per point: avg {avg:F2} ms, worst {worst:F2} ms over 600 ticks, {CountMoving(u)} still moving (uncached plug test: {FourPointsBeforeMs} ms)");
        Assert.True(avg < FourPointsBeforeMs, $"avg {avg:F2} ms: back at the uncached plug test's {FourPointsBeforeMs} ms (BUG-0044)");
    }
}
