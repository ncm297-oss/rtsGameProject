using System.Diagnostics;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Draw;

/// <summary>
/// M6-S13 criterion 12: the per-tick draw check allocates nothing, on every path (a match that can train, a spent match whose
/// clock runs, a drawn match, the rule off), and it reads counters, not stores: its time does not grow with the units.
/// </summary>
[Collection(SerialCollection.Name)]
public class DrawCostTests
{
    private readonly ITestOutputHelper _out;

    public DrawCostTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void TheDrawCheck_AllocatesNothing_OnEveryPath()
    {
        // The spent world waits a game day, so 10,000 checks keep taking the counting path with the clock running.
        Simulation spent = DrawScenes.Spent(DrawScenes.OutOfReach, players: 4, soldiers: 4);
        Simulation rich = DrawScenes.Spent(DrawScenes.Short, players: 4, soldiers: 4);
        for (int p = 0; p < 4; p++) rich.World.Ledger.Gold[p] = 1000;
        Simulation off = DrawScenes.Spent(DrawScenes.Off, players: 4, soldiers: 4);
        Simulation drawn = DrawScenes.Spent(DrawScenes.Short);
        DrawScenes.RunToEnd(drawn, 2 * DrawScenes.ShortTicks);
        Assert.True(drawn.World.MatchDrawn);
        // M6-SH3: the exact rule's paths: an item waiting at the cap, a worker carrying wood, a drop-off site, gold on the map
        // (every clause read, none holding), and the same with the site finished (the reach read, (b) holding).
        Simulation counts = DrawReachPropertyTests.NewCountsScene();
        Simulation reach = DrawReachPropertyTests.NewCountsScene();
        for (int k = 0; k < reach.World.Buildings.Capacity; k++)
            if (reach.World.Buildings.Alive[k] && reach.World.Buildings.UnderConstruction[k]) reach.World.Buildings.SetWork(k, reach.World.Buildings.WorkNeeded(reach.World.Buildings.TypeId[k]));
        Assert.True(reach.World.CanStillTrain(0));
        foreach (Simulation s in new[] { spent, rich, off, drawn, counts, reach }) { s.World.UpdateDraw(); _ = s.World.MatchSpent; }
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool any = false;
        for (int k = 0; k < 10_000; k++)
        {
            spent.World.UpdateDraw();
            rich.World.UpdateDraw();
            off.World.UpdateDraw();
            drawn.World.UpdateDraw();
            counts.World.UpdateDraw();
            reach.World.UpdateDraw();
            any |= spent.World.MatchSpent | rich.World.CanStillTrain(1) | off.World.MatchSpent | counts.World.MatchSpent | reach.World.CanStillTrain(0);
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(any);
        Assert.Equal(0, bytes);
    }

    /// <summary>The check's time on 8 units and on 2,000 (four seats): the same order (it reads per-seat counters, no store), printed.</summary>
    [Fact]
    public void TheDrawCheck_DoesNotScanTheUnits()
    {
        double small = Time(DrawScenes.Spent(DrawScenes.OutOfReach, players: 4, soldiers: 2));
        Simulation big = DrawScenes.Empty(DrawScenes.OutOfReach, 4, size: 128, units: 2048);
        for (int p = 0; p < 4; p++)
        {
            (int x, int y) = DrawScenes.Corner(big, p);
            DrawScenes.Building(big, p, x, y);
        }
        int placed = 0;
        for (int y = 30; y < 100 && placed < 2000; y++)
            for (int x = 20; x < 108 && placed < 2000; x++, placed++)
                DrawScenes.Unit(big, placed % 4, x, y);
        DrawScenes.Broke(big);
        Assert.True(big.World.MatchSpent);
        double large = Time(big);
        _out.WriteLine($"one draw check: {small * 1000:F3} us with 8 units, {large * 1000:F3} us with 2,000 (four seats)");
        Assert.True(large < 20 * small + 0.002, $"the check grew with the units: {small * 1000:F3} -> {large * 1000:F3} us");
    }

    /// <summary>Milliseconds per draw check on a spent world whose wait is out of reach (the clock runs, the full check every call).</summary>
    private static double Time(Simulation sim)
    {
        for (int k = 0; k < 2000; k++) sim.World.UpdateDraw();
        double best = double.MaxValue;
        for (int run = 0; run < 5; run++) // the least of five, so a busy machine's pause in one run is not read as cost
        {
            var sw = Stopwatch.StartNew();
            for (int k = 0; k < 20_000; k++) sim.World.UpdateDraw();
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds / 20_000);
        }
        Assert.True(sim.World.DrawClock > 0 && !sim.World.MatchDrawn);
        return best;
    }
}
