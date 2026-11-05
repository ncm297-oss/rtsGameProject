using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA, M6-S5 at 5x the 500-unit design number: 2,500 units (625 a seat) brawling round the centre of seed 1's 4-start 256 map,
/// side A (players 0, 1) west, side B (2, 3) east, as a free-for-all, a 2v2 both ways and three allies against one. Reports the
/// average tick and the fog update's share; guards the 2v2 at 2,500 against a blow-up (shared vision's merge is a word-wise OR
/// per team: it must not grow with the units). Run each row in its own process for a clean reading (one filter per row).
/// </summary>
[Collection(SerialCollection.Name)]
[Trait("Category", "Perf")]
public class SharedVisionScaleQaPerfTests
{
    private const int PerSeat = 625, MeasuredTicks = 200;

    private readonly ITestOutputHelper _out;

    public SharedVisionScaleQaPerfTests(ITestOutputHelper output) => _out = output;

    private static Simulation Brawl(int[] teams)
    {
        ImmutableArray<PlayerSeat> seats = teams.Select((t, p) => new PlayerSeat(p % 2, SeatController.Human, AiDifficulty.Normal, t)).ToImmutableArray();
        var map = MapGenParams.Default with { Width = 256, Height = 256, Forests = 24, GoldMines = 16, StartLocations = 4 };
        var sim = new Simulation(new SimConfig(1, 4, 4 * PerSeat + 64, 8 * PerSeat + 256) { Data = TestSim.Data, Map = map, Seats = seats });
        SaveScenes.PlaceBases(sim, army: false);
        NavGrid g = sim.World.NavGrid;
        int center = MoveScenario.CentralCell(g);
        int cx = center % g.Width;
        Pathfinding.FlowField field = Pathfinding.FlowField.Build(g, center);
        var halves = new[] { new List<int>(), new List<int>() };
        for (int c = 0; c < g.Width * g.Height; c++)
        {
            if (!(field.CostAt(c) <= 30f)) continue;
            if (c % g.Width < cx - 1) halves[0].Add(c);
            else if (c % g.Width > cx + 1) halves[1].Add(c);
        }
        var rng = new Determinism.SimRng(1, 73);
        for (int k = 0; k < 2 * PerSeat; k++)
            for (int side = 0; side < 2; side++)
            {
                int owner = 2 * side + (k & 1);
                int cell = halves[side][rng.NextInt(0, halves[side].Count)];
                Vector2 corner = MoveScenario.Center(g, cell) - new Vector2(MapConstants.CellSize / 2);
                int type = side == 0 ? CombatScenes.HeavyInfantry : CombatScenes.Raider;
                sim.Enqueue(Command.SpawnUnit(owner, type, corner + new Vector2(0.05f + rng.NextFloat() * 1.9f, 0.05f + rng.NextFloat() * 1.9f)));
            }
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        Vector2 c0 = MoveScenario.Center(g, center);
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && (u.TypeId[i] == CombatScenes.HeavyInfantry || u.TypeId[i] == CombatScenes.Raider))
                sim.Enqueue(Command.AttackMove(u.Owner[i], new EntityHandle(i, u.Generation[i]), c0 + new Vector2(u.Owner[i] < 2 ? 12f : -12f, 0f)));
        return sim;
    }

    [Theory]
    [InlineData("0000")]
    [InlineData("1122")]
    [InlineData("1212")]
    [InlineData("1110")]
    public void Brawl2500_On256_Report(string layout)
    {
        int[] teams = layout.Select(c => c - '0').ToArray();
        Simulation sim = Brawl(teams);
        Assert.True(sim.World.Units.Count >= 4 * PerSeat, $"setup: {sim.World.Units.Count} units");
        for (int t = 0; t < 10; t++) sim.Tick();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long start = Stopwatch.GetTimestamp();
        for (int t = 0; t < MeasuredTicks; t++) sim.Tick();
        double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency / MeasuredTicks;
        long fogStart = Stopwatch.GetTimestamp();
        for (int k = 0; k < 50; k++) sim.World.Fog.Update();
        double fog = (Stopwatch.GetTimestamp() - fogStart) * 1000.0 / Stopwatch.Frequency / 50;
        _out.WriteLine(FormattableString.Invariant($"QA-SCALE {layout}: {sim.World.Units.Count} units, average tick {ms:F2} ms, fog update {fog:F3} ms, losses {sim.World.Losses.ToArray().Sum()}"));
        Assert.True(ms < 25.0, $"2,500 units average {ms:F2} ms a tick");
    }
}
