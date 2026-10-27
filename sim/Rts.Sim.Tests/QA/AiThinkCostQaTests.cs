using System.Collections.Immutable;
using System.Diagnostics;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// QA (M5-2, session 2026-10-11-0634): what an AI think costs on the paths the developer's gate scene doesn't reach. The gate
/// (<c>Ai/AiPerfTests</c>) times the house rule finding a site near the hall at once; here every candidate site fails late:
/// the building store is full, so <see cref="PlayerView.CanPlace"/> walks the footprint, the ghosts and every remembered node
/// for each of the 33 x 33 candidates before saying <see cref="Economy.PlacementError.StoreFull"/>, at every think. Same
/// budget as the gate: a think's average under 1 ms in the Debug build.
/// </summary>
[Collection(SerialCollection.Name)]
public class AiThinkCostQaTests
{
    private const int Players = 8, Units = 2500;
    private const double BudgetMs = 1.0;
    private readonly ITestOutputHelper _out;

    public AiThinkCostQaTests(ITestOutputHelper output) => _out = output;

    /// <summary>The gate's scene (eight bands of a generated 128 map, a hall, barracks, range and house each, 2,500 units, pop over the cap) with the building store exactly full.</summary>
    private static (Simulation Sim, AiPlayer[] Ais) FullStoreScene(int forests, bool teams = false)
    {
        GameData d = TestSim.Data;
        var map = MapGenParams.Default with { Forests = forests, GoldMines = 4 };
        var config = new SimConfig(3, Players, Units + 400, 4096) { Data = d, Map = map, Combat = false, BuildingCapacity = Players * 4 };
        // M6-S4 QA: four teams of two (the default seats, each with team 1 + p mod 4), so every seat has an ally in sight.
        if (teams) config = config with { Seats = Enumerable.Range(0, Players).Select(p => config.SeatOf(p) with { Team = 1 + p % 4 }).ToImmutableArray() };
        var sim = new Simulation(config);
        NavGrid g = sim.World.NavGrid;
        var open = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
            if (g.IsPassable(c % g.Width, c / g.Width)) open.Add(c);
        int stride = Math.Max(1, open.Count / Units);
        int band = Units / Players;
        for (int p = 0; p < Players; p++)
        {
            int f = sim.World.FactionOf(p);
            int from = (p * band + band / 2) * stride % open.Count;
            foreach (BuildingSlot slot in new[] { BuildingSlot.TownHall, BuildingSlot.InfantryHall, BuildingSlot.RangedHall, BuildingSlot.House })
            {
                int type = Rts.Sim.ViewApi.StartBase.BuildingOfSlot(d, f, slot);
                for (int k = from; k < open.Count; k += 7)
                    if (sim.World.Buildings.Spawn(p, type, open[k], out _)) { from = k + 40; break; }
            }
            BuildMaps.SetTotals(sim, p, 100_000, 100_000);
        }
        Assert.Equal(0, sim.World.Buildings.FreeCount);
        int placed = 0;
        for (int k = 0; placed < Units && k < 4 * open.Count; k++)
        {
            int c = open[k * stride % open.Count];
            if (sim.World.Buildings.SlotAt(c % g.Width, c / g.Width) >= 0) continue;
            int owner = Math.Min(placed / band, Players - 1);
            FactionDef fd = d.Factions[sim.World.FactionOf(owner)];
            int type = placed % 5 == 0 ? Rts.Sim.ViewApi.StartBase.UnitOfSlot(d, fd.Id, UnitSlot.Worker) : fd.Units[placed % 3];
            CombatScenes.Place(sim, owner, type, g.CellCenter(c % g.Width, c / g.Width));
            placed++;
        }
        for (int t = 0; t < 8; t++) sim.Tick();
        var ais = new AiPlayer[Players];
        for (int p = 0; p < Players; p++)
            ais[p] = new AiPlayer(new PlayerView(sim.World, p), d, d.Ai[sim.World.FactionOf(p)], AiDifficulty.Hard,
                new SimRng(3, (ulong)RngStream.Ai(p)), sim);
        return (sim, ais);
    }

    [Theory]
    [Trait("Category", "Perf")]
    [InlineData(6, false)]
    [InlineData(12, false)]
    [InlineData(12, true)] // M6-S4 QA: the same with teams (the ally checks in the census and the ability manager)
    public void EightAiSeats_FullBuildingStore_AThinkUnder1Ms(int forests, bool teams)
    {
        (Simulation sim, AiPlayer[] ais) = FullStoreScene(forests, teams);
        var sw = new Stopwatch();
        var times = new List<double>();
        int think = ais[0].ThinkTicks;
        for (int t = 0; t < 40 * think; t++)
        {
            if (sim.TickNumber % think == 0)
                foreach (AiPlayer ai in ais)
                {
                    sw.Restart();
                    ai.Think();
                    sw.Stop();
                    if (t >= 5 * think) times.Add(sw.Elapsed.TotalMilliseconds);
                }
            sim.Tick();
        }
        double avg = times.Average(), worst = times.Max();
        _out.WriteLine($"forests {forests} teams {teams}: {sim.World.Resources.Count} nodes; {times.Count} thinks, avg {avg:F3} ms, worst {worst:F3} ms; {ais.Sum(a => a.CommandsSent)} commands sent");
        Assert.True(avg < BudgetMs, $"avg {avg:F3} ms over the {BudgetMs} ms budget (full building store)");
    }
}
