using System.Diagnostics;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// BUG-0217: what a whole tick costs in a brawl on a generated 3-level map, where combat's target scan asks the vision
/// rule (<c>VisionSystem.UnitSeesUnit</c>) about candidates the scanner's own one-level sight can't settle. Debug, like
/// every tick budget in docs/03; 10 warm-up ticks, then <see cref="MeasuredTicks"/> timed.
/// </summary>
[Collection(SerialCollection.Name)]
public class VisionGatePerfTests
{
    private const int MeasuredTicks = 300;

    /// <summary>docs/03's tick budget for 500 units.</summary>
    private const double TickBudgetMs = 4.0;

    private readonly ITestOutputHelper _out;

    public VisionGatePerfTests(ITestOutputHelper output) => _out = output;

    private static Simulation ThreeLevelBrawl(int perSide)
    {
        Simulation sim = CombatScenes.MapBrawl(3, perSide);
        int levels = 0;
        foreach (byte l in sim.World.Heightmap.Levels) levels |= 1 << l;
        Assert.Equal(7, levels); // seed 3's map has levels 0, 1 and 2
        Assert.True(sim.World.Fog.MultiLevel);
        return sim;
    }

    private double AverageTickMs(Simulation sim, out double worstMs)
    {
        for (int t = 0; t < 10; t++) sim.Tick();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long freq = Stopwatch.Frequency, total = 0, worst = 0;
        for (int t = 0; t < MeasuredTicks; t++)
        {
            long start = Stopwatch.GetTimestamp();
            sim.Tick();
            long took = Stopwatch.GetTimestamp() - start;
            total += took;
            worst = Math.Max(worst, took);
        }
        worstMs = worst * 1000.0 / freq;
        return total * 1000.0 / freq / MeasuredTicks;
    }

    /// <summary>250 v 250 (500 units) on seed 3's 3-level map stays inside docs/03's 4 ms tick budget.</summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void Brawl250v250_OnAThreeLevelMap_AvgTickUnder4Ms()
    {
        Simulation sim = ThreeLevelBrawl(250);
        double avg = AverageTickMs(sim, out double worst);
        _out.WriteLine($"250 v 250 on seed 3's 3-level map: avg {avg:F2} ms, worst {worst:F2} ms over {MeasuredTicks} ticks (target avg < {TickBudgetMs} ms)");
        Assert.True(avg < TickBudgetMs, $"avg {avg:F2} ms (docs/03: < {TickBudgetMs} ms for 500 units)");
    }

    /// <summary>1,000 v 1,000 on seed 3's 3-level map: reported (4x docs/03's 500 units, no budget of its own), QA's worst case for the gate.</summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void Brawl1000v1000_OnAThreeLevelMap_Report()
    {
        Simulation sim = ThreeLevelBrawl(1000);
        double avg = AverageTickMs(sim, out double worst);
        _out.WriteLine($"1,000 v 1,000 on seed 3's 3-level map: avg {avg:F2} ms, worst {worst:F2} ms over {MeasuredTicks} ticks");
        Assert.True(avg > 0);
    }
}
