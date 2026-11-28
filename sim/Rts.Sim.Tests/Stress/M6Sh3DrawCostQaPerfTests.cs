using System.Diagnostics;
using Rts.Sim.Tests.Draw;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA, M6-SH3 (session 2026-11-27-0817, tier full, QA focus 7): the tick cost of the exact draw check with 8 seats and 2,000
/// units on its worst path (every seat with an item waiting at the cap and nothing it can pay for), and no allocation. In the
/// serial collection (a wall-clock and allocation measure).
/// </summary>
[Collection(SerialCollection.Name)]
public class M6Sh3DrawCostQaPerfTests
{
    private readonly ITestOutputHelper _out;

    public M6Sh3DrawCostQaPerfTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// Eight seats (four Malazan, four Whirlwind) on a 160-cell map, 2,000 soldiers, every seat at its population cap with a paid
    /// item waiting and no bank (the check's worst path: (a)'s per-type room loop, then (b)'s whole unit list, for every seat):
    /// one check's least-of-five time is printed and held under 20 microseconds (the 50 ms tick's 0.04 %), and 10,000 checks allocate nothing.
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void EightSeats_TwoThousandUnits_TheWorstPathCheck_IsCheap_AndAllocatesNothing()
    {
        const int seats = 8;
        Simulation sim = DrawScenes.Empty(DrawScenes.OutOfReach, seats, size: 160, units: 2304);
        World w = sim.World;
        var halls = new int[seats];
        for (int p = 0; p < seats; p++)
        {
            int x = 6 + (p % 4) * 38, y = p < 4 ? 6 : 140;
            halls[p] = DrawScenes.Building(sim, p, x, y).Index;
        }
        int placed = 0;
        for (int y = 20; y < 136 && placed < 2000; y++)
            for (int x = 4; x < 156 && placed < 2000; x++, placed++)
                DrawScenes.Unit(sim, placed % seats, x, y);
        DrawScenes.Broke(sim);
        int queuedSeats = 0;
        for (int p = 0; p < seats; p++)
        {
            int type = w.Data.UnitsTrainedAt(w.Buildings.TypeId[halls[p]])[0];
            if (w.HalfPopCap[p] - w.HalfPop[p] < w.Data.Units[type].ItemHalfPop)
            {
                w.Buildings.Enqueue(halls[p], type);
                queuedSeats++;
            }
        }
        Assert.Equal(seats, queuedSeats);
        Assert.Equal(2000, placed);
        Assert.True(w.MatchSpent);
        for (int k = 0; k < 2000; k++) w.UpdateDraw();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int k = 0; k < 10_000; k++) w.UpdateDraw();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        double best = double.MaxValue;
        for (int run = 0; run < 5; run++)
        {
            var sw = Stopwatch.StartNew();
            for (int k = 0; k < 20_000; k++) w.UpdateDraw();
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds / 20_000);
        }
        Assert.True(w.DrawClock > 0 && !w.MatchDrawn);
        // And one full tick of this world, for scale.
        var tick = Stopwatch.StartNew();
        for (int k = 0; k < 20; k++) sim.Tick();
        double perTick = tick.Elapsed.TotalMilliseconds / 20;
        _out.WriteLine($"8 seats, 2,000 units, worst path: one draw check {best * 1000:F3} us; allocated {bytes} B over 10,000 checks; a whole tick of this world {perTick:F2} ms");
        Assert.Equal(0, bytes);
        Assert.True(best < 0.020, $"one draw check took {best * 1000:F3} us");
    }
}
