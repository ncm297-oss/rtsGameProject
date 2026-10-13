using System.Diagnostics;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-2 criterion 5: what an AI think costs, and that it allocates nothing at steady state. Eight AI seats share 2,500 units
/// (a fifth of them workers) in eight bands of a generated 128 map with forests and mines; each player has a Town Hall, a
/// barracks, a crossbow range and a house, and gold and wood to spend. Its population is far over its cap, so the house rule
/// searches for a site at every think it has no house under construction, the heaviest path. Gated: a think's average under
/// 1 ms in the Debug build; the worst is reported.
/// </summary>
[Collection(SerialCollection.Name)]
public class AiPerfTests
{
    private const int Players = 8, Units = 2500;
    private const double BudgetMs = 1.0;
    private readonly ITestOutputHelper _out;

    public AiPerfTests(ITestOutputHelper output) => _out = output;

    /// <summary>The scene, every seat human, and one <see cref="AiPlayer"/> a player built here on the sim's own command door, so the test can time each think.</summary>
    private static (Simulation Sim, AiPlayer[] Ais) Scene()
    {
        GameData d = TestSim.Data;
        var map = MapGenParams.Default with { Forests = 6, GoldMines = 4 };
        var sim = new Simulation(new SimConfig(3, Players, Units + 400, 4096) { Data = d, Map = map, Combat = false });
        NavGrid g = sim.World.NavGrid;
        var open = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
            if (g.IsPassable(c % g.Width, c / g.Width)) open.Add(c);
        int stride = Math.Max(1, open.Count / Units);
        int band = Units / Players;
        for (int p = 0; p < Players; p++)
        {
            // The buildings first, in the band's middle, on the first open spots that fit.
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
        // The units over the bands, in open-cell order, skipping the buildings' cells, until all 2,500 stand.
        int placed = 0;
        for (int k = 0; placed < Units && k < 4 * open.Count; k++)
        {
            int c = open[k * stride % open.Count];
            if (sim.World.Buildings.SlotAt(c % g.Width, c / g.Width) >= 0) continue;
            int owner = Math.Min(placed / band, Players - 1);
            FactionDef fd = d.Factions[sim.World.FactionOf(owner)];
            // M5-3: four in five are army units (the faction's first three non-worker types): an army of over 200 a seat.
            int[] army = fd.Units.Where(u => d.Units[u].Slot != UnitSlot.Worker).Take(3).ToArray();
            int type = placed % 5 == 0 ? Rts.Sim.ViewApi.StartBase.UnitOfSlot(d, fd.Id, UnitSlot.Worker) : army[placed % 3];
            CombatScenes.Place(sim, owner, type, g.CellCenter(c % g.Width, c / g.Width));
            placed++;
        }
        Assert.Equal(Units, sim.World.Units.Count);
        for (int t = 0; t < 8; t++) sim.Tick(); // two fog updates
        var ais = new AiPlayer[Players];
        for (int p = 0; p < Players; p++)
            ais[p] = new AiPlayer(new PlayerView(sim.World, p), d, d.Ai[sim.World.FactionOf(p)], AiDifficulty.Hard,
                new SimRng(3, (ulong)RngStream.Ai(p)), sim);
        return (sim, ais);
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void EightAiSeats_Over2500Units_AThinkUnder1Ms()
    {
        (Simulation sim, AiPlayer[] ais) = Scene();
        var sw = new Stopwatch();
        var times = new List<double>();
        int think = ais[0].ThinkTicks;
        for (int t = 0; t < 60 * think; t++)
        {
            if (sim.TickNumber % think == 0)
                foreach (AiPlayer ai in ais)
                {
                    sw.Restart();
                    Assert.True(ai.Think());
                    sw.Stop();
                    if (t >= 10 * think) times.Add(sw.Elapsed.TotalMilliseconds); // past the first thinks' one-time costs
                }
            sim.Tick();
        }
        int sent = ais.Sum(a => a.CommandsSent);
        // M5-3: every seat thinks with an army of 200 or more (four in five of its units), so the military manager's rally and
        // wave passes are in the timed thinks.
        Assert.All(ais, a => Assert.True(a.ArmyCount >= 200, $"player {a.Player}: an army of {a.ArmyCount}"));
        _out.WriteLine($"armies {string.Join(", ", ais.Select(a => a.ArmyCount))}; waves {string.Join(", ", ais.Select(a => a.Board.WavesStarted))}");
        double avg = times.Average(), worst = times.Max(), p95 = times.OrderBy(x => x).ElementAt((int)(times.Count * 0.95));
        _out.WriteLine($"{Players} AI seats, {sim.World.Units.Count} units: {times.Count} thinks, avg {avg:F3} ms, p95 {p95:F3} ms, worst {worst:F3} ms; {sent} commands sent");
        Assert.True(sent > 0, "the AIs sent nothing");
        Assert.True(avg < BudgetMs, $"avg {avg:F3} ms over the {BudgetMs} ms budget");
    }

    /// <summary>A steady-state think (the house search included) allocates nothing; nor does a tick that runs the sim's own AI seats.</summary>
    [Fact]
    public void ASteadyStateThink_AllocatesNothing()
    {
        (Simulation sim, AiPlayer[] ais) = Scene();
        int think = ais[0].ThinkTicks;
        for (int t = 0; t < 20 * think; t++)
        {
            if (sim.TickNumber % think == 0) foreach (AiPlayer ai in ais) ai.Think();
            sim.Tick();
        }
        while (sim.TickNumber % think != 0) sim.Tick();
        Action thinkAll = () => { foreach (AiPlayer ai in ais) ai.Think(); };
        AllocationProbe.AssertZero(thinkAll, _out);
    }

    [Fact]
    public void ATickWithAiSeats_AllocatesNothing()
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Ai);
        Simulation sim = AiScenes.Generated(seats, 5, record: false, out _);
        for (int t = 0; t < 600; t++) sim.Tick();
        int sentBefore = sim.AiOf(0)!.CommandsSent + sim.AiOf(1)!.CommandsSent;
        Action ticks = () => { for (int t = 0; t < 40; t++) sim.Tick(); };
        AllocationProbe.AssertZero(ticks, _out);
        Assert.True(sim.AiOf(0)!.Thinks >= 60);
        _out.WriteLine($"commands sent before the probe {sentBefore}, after {sim.AiOf(0)!.CommandsSent + sim.AiOf(1)!.CommandsSent}");
    }
}
