using System.Diagnostics;
using Rts.Sim.Commands;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Xunit.Abstractions;
using static Rts.Sim.Tests.ProductionMaps;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA M3-4 (2026-10-07-0925): the criterion-10 scene (marchers + gathering workers + Town Halls with full queues training
/// non-stop, trained units removed as deaths would) at 1x, 2x and 5x, to look for super-linear production cost and for
/// a cost per spawn that grows with the number of halls. Reports; asserts only that 2x stays within 2.5 x the 1x tick.
/// </summary>
[Collection(SerialCollection.Name)]
public class ProductionScaleStressTests
{
    private readonly ITestOutputHelper _out;

    public ProductionScaleStressTests(ITestOutputHelper output) => _out = output;

    private static readonly MapGenParams ResourceMap = new() { Forests = 12, GoldMines = 8 };

    private static (Simulation Sim, List<int> Halls) Scene(int marchers, int hallCount)
    {
        Simulation sim = MoveScenario.Spawn(7, units: marchers, maxCost: 12f, out int center, capacity: marchers + 150, players: 2, map: ResourceMap);
        NavGrid g = sim.World.NavGrid;
        FlowField fromCenter = FlowField.Build(g, center);
        int far = center;
        for (int c = 0; c < g.Width * g.Height; c++)
            if (float.IsFinite(fromCenter.CostAt(c)) && fromCenter.CostAt(c) > fromCenter.CostAt(far)) far = c;
        MoveScenario.MoveAll(sim, MoveScenario.Center(g, far));
        EconomyScenario.Setup(sim, 25, mineIndex: 0);
        EconomyScenario.Setup(sim, 25, mineIndex: 1);
        BuildMaps.Give(sim, 1, 100_000_000, 100_000_000);
        var anchors = new List<int>();
        for (int c = 0; c < g.Width * g.Height && anchors.Count < hallCount; c += 37)
            if (sim.World.CanPlace(1, HolyCamp, c, out _) && anchors.All(a => Math.Abs(a % g.Width - c % g.Width) > 5 || Math.Abs(a / g.Width - c / g.Width) > 5)) anchors.Add(c);
        Assert.Equal(hallCount, anchors.Count);
        foreach (int a in anchors) sim.Enqueue(Command.SpawnBuilding(1, HolyCamp, g.CellCenter(a % g.Width, a / g.Width)));
        sim.Tick();
        sim.Tick();
        var halls = new List<int>();
        BuildingStore b = sim.World.Buildings;
        for (int k = 0; k < b.Capacity; k++)
            if (b.Alive[k] && b.Owner[k] == 1) halls.Add(k);
        Assert.Equal(hallCount, halls.Count);
        foreach (int k in halls) sim.Enqueue(Command.SetRally(1, b.Cell[k], g.CellCenter(b.Cell[k] % g.Width + 2, b.Cell[k] / g.Width + 7)));
        return (sim, halls);
    }

    private static void TopUp(Simulation sim, List<int> halls)
    {
        BuildingStore b = sim.World.Buildings;
        foreach (int k in halls)
            for (int n = b.QueueCount[k]; n < 5; n++) sim.Enqueue(Command.Train(1, In(sim, k), CampFollower));
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == 1) u.Free(new EntityHandle(i, u.Generation[i]));
    }

    private (double TickMs, double ProductionMs, int Spawned) Measure(int marchers, int hallCount)
    {
        (Simulation sim, List<int> halls) = Scene(marchers, hallCount);
        for (int t = 0; t < 20; t++) { TopUp(sim, halls); sim.Tick(); }
        long ticks = 0;
        int spawned = 0;
        for (int t = 0; t < 1000; t++)
        {
            TopUp(sim, halls);
            int before = sim.World.Units.Count;
            long start = Stopwatch.GetTimestamp();
            sim.Tick();
            ticks += Stopwatch.GetTimestamp() - start;
            spawned += sim.World.Units.Count - before;
        }
        // Production alone: heads mid-training (cheap) on the same scene.
        long p0 = Stopwatch.GetTimestamp();
        for (int n = 0; n < 200; n++) ProductionSystem.Run(sim.World);
        double prod = (Stopwatch.GetTimestamp() - p0) * 1000.0 / Stopwatch.Frequency / 200;
        return (ticks * 1000.0 / Stopwatch.Frequency / 1000, prod, spawned);
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void ProductionScene_At1x2x5x_Report()
    {
        Measure(500, 20); // warm-up
        var rows = new (int M, int H)[] { (500, 20), (1000, 40), (2500, 100) };
        var results = new List<double>();
        foreach ((int m, int h) in rows)
        {
            (double tick, double prod, int spawned) = Measure(m, h);
            results.Add(tick);
            _out.WriteLine($"{m} marchers + 50 gatherers + {h} halls: avg tick {tick:F3} ms over 1,000; ProductionSystem.Run alone {prod:F4} ms; {spawned} trained");
        }
        Assert.True(results[1] < 2.5 * results[0], $"2x tick {results[1]:F3} ms vs 1x {results[0]:F3} ms");
    }
}
