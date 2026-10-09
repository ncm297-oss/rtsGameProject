using System.Diagnostics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>M3-3 criterion 12: construction costs little per tick, and one placement query is cheap enough for a ghost preview every frame.</summary>
[Collection(SerialCollection.Name)]
public class ConstructionPerfTests
{
    private readonly ITestOutputHelper _out;

    public ConstructionPerfTests(ITestOutputHelper output) => _out = output;

    private static readonly MapGenParams ResourceMap = new() { Forests = 12, GoldMines = 8 };

    [Fact]
    [Trait("Category", "Perf")]
    public void FiveHundredMarchersAndFiftyWorkersOnTenSites_AverageTickUnder1Point2Ms()
    {
        Simulation sim = MoveScenario.Spawn(7, units: 500, maxCost: 12f, out int center, capacity: 550, players: 1, map: ResourceMap);
        TestSim.Explored(sim); // M4-3b: the sites are picked map-wide
        NavGrid g = sim.World.NavGrid;
        FlowField fromCenter = FlowField.Build(g, center);
        int far = center;
        for (int c = 0; c < g.Width * g.Height; c++)
            if (float.IsFinite(fromCenter.CostAt(c)) && fromCenter.CostAt(c) > fromCenter.CostAt(far)) far = c;
        MoveScenario.MoveAll(sim, MoveScenario.Center(g, far));
        BuildMaps.Give(sim, 0, 100_000, 100_000);
        // Ten Keep sites (5,400 work: none completes while measured), five workers each, spawned beside them.
        var anchors = new List<int>();
        for (int c = 0; c < g.Width * g.Height && anchors.Count < 10; c += 97)
            if (sim.World.CanPlace(0, GatherMaps.Keep, c, out _) && anchors.All(a => Math.Abs(a % g.Width - c % g.Width) > 6 || Math.Abs(a / g.Width - c / g.Width) > 6)) anchors.Add(c);
        Assert.Equal(10, anchors.Count);
        foreach (int a in anchors)
            for (int k = 0; k < 5; k++)
                sim.Enqueue(Command.SpawnUnit(0, GatherMaps.Laborer, g.CellCenter(a % g.Width + k % 4, a / g.Width - 1)));
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        int slot = 500;
        foreach (int a in anchors)
            for (int k = 0; k < 5; k++, slot++)
                sim.Enqueue(Command.Build(0, MoveScenario.Handle(sim, slot), GatherMaps.Keep, g.CellCenter(a % g.Width, a / g.Width)));
        for (int t = 0; t < 60; t++) sim.Tick(); // walk in
        long start = Stopwatch.GetTimestamp();
        for (int t = 0; t < 200; t++) sim.Tick();
        double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency / 200;
        int building = Enumerable.Range(500, 50).Count(i => u.State[i] == UnitState.Building);
        _out.WriteLine($"500 marching + 50 building 10 sites: avg {ms:F3} ms per tick; {sim.World.Buildings.Count} sites, {building} workers building");
        Assert.Equal(10, sim.World.Buildings.Count);
        Assert.True(building >= 40, $"{building} building");
        Assert.True(ms < 1.2, $"average tick {ms:F3} ms");
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void OneCanPlace_IncludingTheFlood_Under0Point3Ms()
    {
        // M4-3b: explored, so every anchor runs the whole rule chain (the flood, the explored cells, the units) as a
        // placement in sight would.
        var sim = TestSim.Explored(new Simulation(TestSim.Config(Seed: 7, PlayerCount: 1, UnitCapacity: 512, CommandCapacity: 16) with { Map = ResourceMap }));
        NavGrid g = sim.World.NavGrid;
        BuildMaps.Give(sim, 0, 100_000, 100_000);
        // A warm-up sweep, a timed sweep, then the 20 slowest anchors (footprints beside a cliff, forest or border, whose
        // flood goes furthest) timed again, 20 calls each: the slowest of those is the bound.
        int cells = g.Width * g.Height;
        var times = new long[cells];
        for (int c = 0; c < cells; c++) sim.World.CanPlace(0, GatherMaps.Keep, c, out _);
        for (int c = 0; c < cells; c++)
        {
            long t0 = Stopwatch.GetTimestamp();
            sim.World.CanPlace(0, GatherMaps.Keep, c, out _);
            times[c] = Stopwatch.GetTimestamp() - t0;
        }
        double mean = times.Sum() * 1000.0 / Stopwatch.Frequency / cells, ms = 0;
        int worst = -1;
        foreach (int c in Enumerable.Range(0, cells).OrderByDescending(c => times[c]).Take(20))
        {
            long start = Stopwatch.GetTimestamp();
            for (int k = 0; k < 20; k++) sim.World.CanPlace(0, GatherMaps.Keep, c, out _);
            double each = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency / 20;
            if (each > ms) (ms, worst) = (each, c);
        }
        sim.World.CanPlace(0, GatherMaps.Keep, worst, out var why);
        _out.WriteLine($"CanPlace: mean {mean:F4} ms over {cells} anchors; slowest anchor {worst} ({why}) {ms:F4} ms");
        Assert.True(ms < 0.3, $"{ms:F4} ms");
    }
}
