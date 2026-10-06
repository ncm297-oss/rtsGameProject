using System.Diagnostics;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>M3-2 criteria 7-8: gathering costs little per tick, allocates nothing, and is deterministic.</summary>
[Collection(SerialCollection.Name)]
public class GatherPerfTests
{
    private readonly ITestOutputHelper _out;

    public GatherPerfTests(ITestOutputHelper output) => _out = output;

    private static readonly MapGenParams ResourceMap = new() { Forests = 12, GoldMines = 8 };

    /// <summary><paramref name="marchers"/> units ordered across the map (seed 7, 12 forests / 8 mines) plus <paramref name="workers"/> gathering workers, split over two mines' Keeps.</summary>
    private static Simulation Mixed(int marchers, int workers, ulong seed = 7)
    {
        Simulation sim = MoveScenario.Spawn(seed, units: marchers, maxCost: 12f, out int center, capacity: marchers + workers, players: 1, map: ResourceMap);
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

    private double AverageTickMs(Simulation sim, int warmUp, int measured)
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
    public void FiveHundredMovingUnitsAndFiftyGatheringWorkers_AverageTickUnder4Ms()
    {
        Simulation sim = Mixed(500, 50);
        double ms = AverageTickMs(sim, warmUp: 5, measured: 200);
        _out.WriteLine($"500 marching + 50 gathering: avg {ms:F3} ms per tick; on the loop at the end {OnLoop(sim)}; gold {sim.World.Gold[0]} wood {sim.World.Wood[0]}");
        Assert.Equal(50, OnLoop(sim));
        Assert.True(ms < 4.0, $"average tick {ms:F3} ms");
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void TwoHundredGatheringWorkersAlone_AverageTickUnder1Ms()
    {
        Simulation sim = Mixed(0, 200);
        double ms = AverageTickMs(sim, warmUp: 400, measured: 400); // steady state: trips under way
        _out.WriteLine($"200 gathering workers: avg {ms:F3} ms per tick; gold {sim.World.Gold[0]} wood {sim.World.Wood[0]}");
        Assert.True(sim.World.Gold[0] > 200 && sim.World.Wood[0] > 200);
        Assert.True(ms < 1.0, $"average tick {ms:F3} ms");
    }

    [Fact]
    public void ATickWithFiftyGatheringWorkersAnd200Marchers_AllocatesNothing()
    {
        Simulation sim = Mixed(200, 50, seed: 9);
        for (int t = 0; t < 300; t++) sim.Tick(); // into the loop: walking, working, depositing, re-walking
        Action ticks = () =>
        {
            for (int t = 0; t < 100; t++) sim.Tick();
        };
        AllocationProbe.AssertZero(ticks, _out);
        Assert.Equal(50, OnLoop(sim));
    }

    [Fact]
    public void TwinRuns_50WorkersAnd200Marchers_3000Ticks_HashIdenticalEveryTick()
    {
        Simulation a = Mixed(200, 50, seed: 11), b = Mixed(200, 50, seed: 11);
        for (int t = 0; t < 3000; t++)
        {
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"twins differ after tick {a.TickNumber}");
        }
        _out.WriteLine($"3,000 ticks: gold {a.World.Gold[0]} wood {a.World.Wood[0]}, {OnLoop(a)} on the loop");
        Assert.True(a.World.Gold[0] > 400 && a.World.Wood[0] > 400);
    }
}
