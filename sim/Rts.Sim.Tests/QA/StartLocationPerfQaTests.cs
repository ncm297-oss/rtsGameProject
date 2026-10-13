using System.Diagnostics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Xunit.Abstractions;
using static Rts.Sim.Tests.ProductionMaps;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M5-4a): is the production perf scene's tick doubling on the map with start locations (0.53 to 1.2 ms, the dev's
/// report) a cost of start locations or a different scene? The same scene (<see cref="ProductionPerfTests"/>: 500 marchers to
/// the farthest cell, 50 gatherers on mines 0 and 1, 20 halls training) on both maps over several seeds, measured, plus the
/// marchers' path length (the far cell's flow cost) that sets most of the movement work. Gated only at the docs/03 design
/// budget (4 ms for 500 units), not at the scene's own 1.3 ms gate, which stays on the map it was measured on.
/// Measured 2026-10-13 (2,000 ticks, Debug): seed 7 0.54 → 1.19 ms, but seeds 1 / 3 cheaper on the new map (1.12 → 1.03,
/// 0.91 → 0.83) and 2 / 4 / 5 / 6 within 6%; the old map alone spans 0.41-1.19 ms over these seeds: scene variance, not a
/// start-location cost (no per-tick code reads them).
/// </summary>
[Collection(SerialCollection.Name)]
public class StartLocationPerfQaTests
{
    private readonly ITestOutputHelper _out;

    public StartLocationPerfQaTests(ITestOutputHelper output) => _out = output;

    private static (Simulation Sim, List<int> Halls, float FarCost) Scene(ulong seed, int startLocations)
    {
        var map = new MapGenParams { Forests = 12, GoldMines = 8, StartLocations = startLocations };
        Simulation sim = MoveScenario.Spawn(seed, units: 500, maxCost: 12f, out int center, capacity: 650, players: 1, map: map);
        TestSim.Explored(sim);
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
        foreach (int a in anchors) sim.Enqueue(Command.SpawnBuilding(1, HolyCamp, g.CellCenter(a % g.Width, a / g.Width)));
        sim.Tick();
        sim.Tick();
        var halls = new List<int>();
        BuildingStore b = sim.World.Buildings;
        for (int k = 0; k < b.Capacity; k++)
            if (b.Alive[k] && b.Owner[k] == 1) halls.Add(k);
        foreach (int k in halls) sim.Enqueue(Command.SetRally(1, b.Cell[k], g.CellCenter(b.Cell[k] % g.Width + 2, b.Cell[k] / g.Width + 7)));
        return (sim, halls, fromCenter.CostAt(far));
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void ProductionScene_OnBothMaps_Seeds1To6And7_Measured()
    {
        double worstNew = 0;
        foreach (ulong seed in new ulong[] { 7, 1, 2, 3, 4, 5, 6 })
        {
            var line = new System.Text.StringBuilder($"seed {seed}:");
            foreach (int starts in new[] { 0, 2 })
            {
                (Simulation sim, List<int> halls, float farCost) = Scene(seed, starts);
                for (int t = 0; t < 20; t++) { ProductionPerfTests.TopUp(sim, halls); sim.Tick(); }
                long ticks = 0;
                for (int t = 0; t < 2000; t++)
                {
                    ProductionPerfTests.TopUp(sim, halls);
                    long start = Stopwatch.GetTimestamp();
                    sim.Tick();
                    ticks += Stopwatch.GetTimestamp() - start;
                }
                double ms = ticks * 1000.0 / Stopwatch.Frequency / 2000;
                if (starts == 2) worstNew = Math.Max(worstNew, ms);
                line.Append($"  starts {starts}: {ms:F3} ms/tick, far cost {farCost:F0}, halls {halls.Count}");
            }
            _out.WriteLine(line.ToString());
        }
        Assert.True(worstNew < 4.0, $"worst {worstNew:F3} ms against the 4 ms design budget for 500 units");
    }
}
