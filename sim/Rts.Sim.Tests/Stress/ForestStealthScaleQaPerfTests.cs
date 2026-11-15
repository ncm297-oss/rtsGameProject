using System.Diagnostics;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA (M8-1, session 2026-11-14-1246): 2,000 forest-hidden units (4x the 500-unit budget's whole army, all hidden at once).
/// A 160-cell flat map with a tree in every 3x3 block and a forest unit holding on each of the 8 cells round it; one enemy
/// far off. Reports the tick cost against the same scene on the copy without the block, the forest pass alone, and the
/// allocation over 100 ticks. Bounds: nothing allocates; the forest pass alone stays under 1 ms (Debug).
/// </summary>
[Trait("Category", "Perf")]
[Collection(SerialCollection.Name)]
public class ForestStealthScaleQaPerfTests
{
    private readonly ITestOutputHelper _out;

    public ForestStealthScaleQaPerfTests(ITestOutputHelper output) => _out = output;

    private static readonly Lazy<GameData> s_plain = new(() => AiScenes.DataWith(StealthTests.AddTo));

    private const int Units = 2000;

    private static Simulation Scene(GameData data)
    {
        var sim = new Simulation(new SimConfig(1, 2, Units + 64, 8192) { Data = data, ResourceCapacity = 4096 }, LocalMovementTests.Flat(160));
        World w = sim.World;
        int infantry = data.FindUnit("malazan_heavy_infantry");
        int placed = 0;
        for (int by = 1; by + 2 < 158 && placed < Units; by += 3)
            for (int bx = 1; bx + 2 < 158 && placed < Units; bx += 3)
            {
                ForestScenes.Tree(sim, bx + 1, by + 1);
                for (int dy = 0; dy < 3 && placed < Units; dy++)
                    for (int dx = 0; dx < 3 && placed < Units; dx++)
                    {
                        if (dx == 1 && dy == 1) continue;
                        EntityHandle h = CombatScenes.Place(sim, 0, infantry, w.NavGrid.CellCenter(bx + dx, by + dy));
                        sim.Enqueue(Command.HoldPosition(0, h));
                        placed++;
                    }
            }
        Assert.Equal(Units, placed);
        CombatScenes.Place(sim, 1, data.FindUnit("whirlwind_raider"), w.NavGrid.CellCenter(158, 158));
        return sim;
    }

    private static double MsPerTick(Simulation sim, int ticks)
    {
        var sw = Stopwatch.StartNew();
        for (int t = 0; t < ticks; t++) sim.Tick();
        return sw.Elapsed.TotalMilliseconds / ticks;
    }

    [Fact]
    public void TwoThousandHiddenUnits_TickCost_ThePassAlone_AndNoAllocation()
    {
        Simulation forest = Scene(ForestScenes.Data3), plain = Scene(s_plain.Value);
        for (int t = 0; t < 30; t++)
        {
            forest.Tick();
            plain.Tick();
        }
        World w = forest.World;
        int held = 0;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Forest!.Held[i] != 0) held++;
        Assert.Equal(Units, held);
        Assert.Equal(Units, w.Units.Statuses.StealthedUnits);

        var rounds = new List<(double Forest, double Plain)>();
        for (int r = 0; r < 3; r++) rounds.Add((MsPerTick(forest, 100), MsPerTick(plain, 100)));

        // The forest pass alone (every unit already held: the steady state's work), 1,000 calls.
        var sw = Stopwatch.StartNew();
        for (int k = 0; k < 1000; k++) ForestStealthSystem.Run(w);
        double pass = sw.Elapsed.TotalMilliseconds / 1000;
        sw.Restart();
        for (int k = 0; k < 200; k++) StealthSystem.Run(w);
        double detect = sw.Elapsed.TotalMilliseconds / 200;

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int t = 0; t < 100; t++) forest.Tick();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        _out.WriteLine($"2,000 hidden: ms a tick (forest / no block) {string.Join("; ", rounds.Select(x => $"{x.Forest:F3} / {x.Plain:F3}"))}; the forest pass alone {pass * 1000:F1} us; the detection pass over 2,000 stealthed {detect:F3} ms; {bytes} bytes over 100 ticks");
        Assert.Equal(0, bytes);
        Assert.True(pass < 1.0, $"the forest pass takes {pass:F3} ms over 2,000 units");
    }
}
