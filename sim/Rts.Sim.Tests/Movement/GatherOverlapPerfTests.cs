using System.Diagnostics;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-S9 criterion 8: <see cref="GatherPerfTests"/>' two tick-cost rows with the gather overlap on (0.5, the default to ship),
/// held to the same thresholds (4 ms and 1 ms, Debug). The same scenes as there, on the test copy.
/// </summary>
[Collection(SerialCollection.Name)]
public class GatherOverlapPerfTests
{
    private readonly ITestOutputHelper _out;

    public GatherOverlapPerfTests(ITestOutputHelper output) => _out = output;

    private static readonly MapGenParams ResourceMap = new() { Forests = 12, GoldMines = 8 };

    /// <summary><see cref="GatherPerfTests"/>' scene on <paramref name="data"/>: <paramref name="marchers"/> units ordered across the map (seed 7, 12 forests / 8 mines) plus <paramref name="workers"/> gathering workers over two mines' Keeps.</summary>
    internal static Simulation Mixed(GameData data, int marchers, int workers, ulong seed = 7)
    {
        Simulation sim = MoveScenario.Spawn(seed, units: marchers, maxCost: 12f, out int center, capacity: marchers + workers, players: 1, map: ResourceMap, data: data);
        NavGrid g = sim.World.NavGrid;
        if (marchers > 0)
        {
            FlowField fromCenter = FlowField.Build(g, center);
            int far = center;
            for (int c = 0; c < g.Width * g.Height; c++)
                if (float.IsFinite(fromCenter.CostAt(c)) && fromCenter.CostAt(c) > fromCenter.CostAt(far)) far = c;
            MoveScenario.MoveAll(sim, MoveScenario.Center(g, far));
        }
        EconomyScenario.Setup(sim, workers / 2, mineIndex: 0);
        EconomyScenario.Setup(sim, workers - workers / 2, mineIndex: 1);
        return sim;
    }

    private static double AverageTickMs(Simulation sim, int warmUp, int measured)
    {
        for (int t = 0; t < warmUp; t++) sim.Tick();
        long start = Stopwatch.GetTimestamp();
        for (int t = 0; t < measured; t++) sim.Tick();
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency / measured;
    }

    private static int OnLoop(Simulation sim)
    {
        int n = 0;
        for (int i = 0; i < sim.World.Units.Capacity; i++)
            if (sim.World.Units.Alive[i] && EconomySystem.OnLoop(sim.World.Units, i)) n++;
        return n;
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void OverlapOn_FiveHundredMovingUnitsAndFiftyGatheringWorkers_AverageTickUnder4Ms()
    {
        Simulation sim = Mixed(GatherOverlapScenes.DataAt(0.5), 500, 50);
        double ms = AverageTickMs(sim, warmUp: 5, measured: 200);
        _out.WriteLine($"overlap 0.5, 500 marching + 50 gathering: avg {ms:F3} ms per tick; on the loop at the end {OnLoop(sim)}; gold {sim.World.Gold[0]} wood {sim.World.Wood[0]}");
        Assert.Equal(50, OnLoop(sim));
        Assert.True(ms < 4.0, $"average tick {ms:F3} ms");
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void OverlapOn_TwoHundredGatheringWorkersAlone_AverageTickUnder1Ms()
    {
        Simulation sim = Mixed(GatherOverlapScenes.DataAt(0.5), 0, 200);
        double ms = AverageTickMs(sim, warmUp: 400, measured: 400);
        _out.WriteLine($"overlap 0.5, 200 gathering workers: avg {ms:F3} ms per tick; gold {sim.World.Gold[0]} wood {sim.World.Wood[0]}");
        Assert.True(sim.World.Gold[0] > 200 && sim.World.Wood[0] > 200);
        Assert.True(ms < 1.0, $"average tick {ms:F3} ms");
    }
}
