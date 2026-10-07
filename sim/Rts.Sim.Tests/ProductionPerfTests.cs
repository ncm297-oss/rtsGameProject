using System.Diagnostics;
using Rts.Sim.Commands;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Xunit.Abstractions;
using static Rts.Sim.Tests.ProductionMaps;

namespace Rts.Sim.Tests;

/// <summary>M3-4 criteria 4 and 10: production costs little per tick, and a spawn that finds no cell doesn't scan the map.</summary>
[Collection(SerialCollection.Name)]
public class ProductionPerfTests
{
    private readonly ITestOutputHelper _out;

    public ProductionPerfTests(ITestOutputHelper output) => _out = output;

    private static readonly MapGenParams ResourceMap = new() { Forests = 12, GoldMines = 8 };

    /// <summary>
    /// 500 marchers and 50 gathering workers (player 0, as in <c>GatherPerfTests</c>) plus 20 of player 1's Town Halls
    /// training workers non-stop, rallied, queues topped up to 5 and the trained units removed between ticks (as deaths
    /// would) so the cap never stops them; returns the sim and the halls.
    /// </summary>
    internal static (Simulation Sim, List<int> Halls) Scene()
    {
        Simulation sim = MoveScenario.Spawn(7, units: 500, maxCost: 12f, out int center, capacity: 650, players: 1, map: ResourceMap);
        NavGrid g = sim.World.NavGrid;
        FlowField fromCenter = FlowField.Build(g, center);
        int far = center;
        for (int c = 0; c < g.Width * g.Height; c++)
            if (float.IsFinite(fromCenter.CostAt(c)) && fromCenter.CostAt(c) > fromCenter.CostAt(far)) far = c;
        MoveScenario.MoveAll(sim, MoveScenario.Center(g, far));
        EconomyScenario.Setup(sim, 25, mineIndex: 0);
        EconomyScenario.Setup(sim, 25, mineIndex: 1);
        BuildMaps.Give(sim, 1, 10_000_000, 10_000_000);
        var anchors = new List<int>();
        for (int c = 0; c < g.Width * g.Height && anchors.Count < 20; c += 89)
            if (sim.World.CanPlace(1, HolyCamp, c, out _) && anchors.All(a => Math.Abs(a % g.Width - c % g.Width) > 6 || Math.Abs(a / g.Width - c / g.Width) > 6)) anchors.Add(c);
        Assert.Equal(20, anchors.Count);
        foreach (int a in anchors) sim.Enqueue(Command.SpawnBuilding(1, HolyCamp, g.CellCenter(a % g.Width, a / g.Width)));
        sim.Tick();
        sim.Tick();
        var halls = new List<int>();
        BuildingStore b = sim.World.Buildings;
        for (int k = 0; k < b.Capacity; k++)
            if (b.Alive[k] && b.Owner[k] == 1) halls.Add(k);
        Assert.Equal(20, halls.Count);
        foreach (int k in halls) sim.Enqueue(Command.SetRally(1, b.Cell[k], g.CellCenter(b.Cell[k] % g.Width + 2, b.Cell[k] / g.Width + 7)));
        return (sim, halls);
    }

    /// <summary>Between ticks: Trains to bring every hall's queue back to 5 (the surplus of the two ticks before they apply is dropped as full), and player 1's trained units removed.</summary>
    internal static void TopUp(Simulation sim, List<int> halls)
    {
        BuildingStore b = sim.World.Buildings;
        foreach (int k in halls)
            for (int n = b.QueueCount[k]; n < 5; n++) sim.Enqueue(Command.Train(1, In(sim, k), CampFollower));
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == 1) u.Free(new EntityHandle(i, u.Generation[i]));
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void FiveHundredMarchers_TwentyHallsProducing_FiftyGatherers_AverageTickUnder1Point3Ms()
    {
        (Simulation sim, List<int> halls) = Scene();
        for (int t = 0; t < 20; t++)
        {
            TopUp(sim, halls);
            sim.Tick();
        }
        long ticks = 0;
        int spawned = 0;
        for (int t = 0; t < 2000; t++)
        {
            TopUp(sim, halls);
            int before = sim.World.Units.Count;
            long start = Stopwatch.GetTimestamp();
            sim.Tick();
            ticks += Stopwatch.GetTimestamp() - start;
            spawned += sim.World.Units.Count - before;
        }
        double ms = ticks * 1000.0 / Stopwatch.Frequency / 2000;
        int onLoop = 0;
        for (int i = 0; i < sim.World.Units.Capacity; i++)
            if (sim.World.Units.Alive[i] && EconomySystem.OnLoop(sim.World.Units, i)) onLoop++;
        _out.WriteLine($"500 marching + 50 gathering + 20 halls training: avg {ms:F3} ms per tick over 2,000; {spawned} units trained; {onLoop} on the gather loop");
        // 20 halls x 2,000 / 240 ticks a worker: about 166, all spawned (the cap never binds: they're removed).
        Assert.True(spawned >= 150, $"{spawned} trained");
        Assert.Equal(50, onLoop);
        Assert.True(ms < 1.3, $"average tick {ms:F3} ms");
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void ASpawnWithNoFreeCellOnASmallPlateau_OfAHugeMap_IsCappedByTheLevelsBox_Under0Point2Ms()
    {
        // A 420-square map: without the cap the ring walk would go out about 400 rings before giving up.
        (Simulation sim, int keep, List<(int X, int Y)> free) = ProductionTests.PlateauKeep(420, 200);
        World w = sim.World;
        foreach ((int x, int y) in free) sim.Enqueue(Command.SpawnUnit(1, Raider, GatherMaps.At(sim, x, y)));
        GatherMaps.Run(sim, 2);
        BuildMaps.SetTotals(sim, 0, 1000, 1000);
        ProductionMaps.Apply(sim, Command.Train(0, In(sim, keep), GatherMaps.Laborer));
        GatherMaps.Run(sim, w.Data.Units[GatherMaps.Laborer].TrainTicks);
        Assert.Equal(w.Buildings.TrainTicks(GatherMaps.Laborer), w.Buildings.Progress[keep]); // complete, waiting
        for (int n = 0; n < 50; n++) ProductionSystem.Run(w); // warm-up
        long start = Stopwatch.GetTimestamp();
        const int runs = 500;
        for (int n = 0; n < runs; n++) ProductionSystem.Run(w);
        double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency / runs;
        _out.WriteLine($"420 x 420, full 8 x 8 plateau: one waiting spawn attempt {ms:F4} ms");
        Assert.Equal(1, w.Buildings.QueueCount[keep]);
        Assert.Equal(free.Count, w.Units.Count);
        Assert.True(ms < 0.2, $"{ms:F4} ms per spawn attempt");
    }
}
