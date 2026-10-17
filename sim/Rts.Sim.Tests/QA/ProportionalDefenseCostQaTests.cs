using System.Diagnostics;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M5-4f (session 2026-10-16-2036): the cost path the gate rows don't reach. <c>Ai/AiPerfTests</c> times every seat
/// attacked with no wave out and <c>AiWaveCostQaTests</c> times 200-unit waves with the defense quiet (defendRadius 8);
/// here every seat has a 200+ unit wave in progress <b>and</b> an enemy line unit parked by its hall (spotted, holding,
/// combat off), so every think is a trigger: the sizing runs the recall test (the members heapsorted by distance to the
/// hall) and the picks (members pulled while the rest keep the threshold). Same budget: a think's average under 1 ms.
/// </summary>
[Collection(SerialCollection.Name)]
public class ProportionalDefenseCostQaTests
{
    private const int Players = 8, Units = 2500;
    private const double BudgetMs = 1.0;
    private readonly ITestOutputHelper _out;

    public ProportionalDefenseCostQaTests(ITestOutputHelper output) => _out = output;

    private static readonly Lazy<GameData> s_data = new(() => AiScenes.DataWith(dir => dir.EditJson("common/rules.json", r => r["ai"]!["defendRadius"] = 8)));

    private static (Simulation Sim, AiPlayer[] Ais, int[] Halls) Scene()
    {
        GameData d = s_data.Value;
        var map = MapGenParams.Default with { Forests = 6, GoldMines = 4 };
        var sim = new Simulation(new SimConfig(3, Players, Units + 400, 8192) { Data = d, Map = map, Combat = false });
        NavGrid g = sim.World.NavGrid;
        var open = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
            if (g.IsPassable(c % g.Width, c / g.Width)) open.Add(c);
        int stride = Math.Max(1, open.Count / Units);
        int band = Units / Players;
        var halls = new int[Players];
        for (int p = 0; p < Players; p++)
        {
            int f = sim.World.FactionOf(p);
            int from = (p * band + band / 2) * stride % open.Count;
            foreach (BuildingSlot slot in new[] { BuildingSlot.TownHall, BuildingSlot.InfantryHall, BuildingSlot.RangedHall, BuildingSlot.House })
            {
                int type = Rts.Sim.ViewApi.StartBase.BuildingOfSlot(d, f, slot);
                for (int k = from; k < open.Count; k += 7)
                    if (sim.World.Buildings.Spawn(p, type, open[k], out EntityHandle h))
                    {
                        if (slot == BuildingSlot.TownHall) halls[p] = h.Index;
                        from = k + 40;
                        break;
                    }
            }
            BuildMaps.SetTotals(sim, p, 100_000, 100_000);
        }
        int placed = 0;
        for (int k = 0; placed < Units && k < 4 * open.Count; k++)
        {
            int c = open[k * stride % open.Count];
            if (sim.World.Buildings.SlotAt(c % g.Width, c / g.Width) >= 0) continue;
            int owner = Math.Min(placed / band, Players - 1);
            FactionDef fd = d.Factions[sim.World.FactionOf(owner)];
            int[] army = fd.Units.Where(u => d.Units[u].Slot != UnitSlot.Worker).Take(3).ToArray();
            int type = placed % 5 == 0 ? Rts.Sim.ViewApi.StartBase.UnitOfSlot(d, fd.Id, UnitSlot.Worker) : army[placed % 3];
            CombatScenes.Place(sim, owner, type, g.CellCenter(c % g.Width, c / g.Width));
            placed++;
        }
        for (int p = 0; p < Players; p++)
        {
            int hall = halls[(p + 1) % Players], cell = sim.World.Buildings.Cell[hall];
            Assert.True(Near(sim, cell, out int x, out int y));
            CombatScenes.Spotter(sim, p, g.CellCenter(x, y));
        }
        for (int t = 0; t < 8; t++) sim.Tick();
        var ais = new AiPlayer[Players];
        for (int p = 0; p < Players; p++)
            ais[p] = new AiPlayer(new PlayerView(sim.World, p), d, d.Ai[sim.World.FactionOf(p)], AiDifficulty.Hard, new SimRng(3, (ulong)RngStream.Ai(p)), sim);
        return (sim, ais, halls);
    }

    /// <summary>The nearest open cell 3-11 cells round <paramref name="cell"/>.</summary>
    private static bool Near(Simulation sim, int cell, out int x, out int y)
    {
        NavGrid g = sim.World.NavGrid;
        int hx = cell % g.Width, hy = cell / g.Width;
        for (int r = 3; r < 12; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    x = hx + dx;
                    y = hy + dy;
                    if (g.IsPassable(x, y) && sim.World.Buildings.SlotAt(x, y) < 0) return true;
                }
        x = y = -1;
        return false;
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void EightSeats_A200UnitWaveOut_AndRaidedEveryThink_AThinkUnder1Ms()
    {
        (Simulation sim, AiPlayer[] ais, int[] halls) = Scene();
        World w = sim.World;
        int think = ais[0].ThinkTicks;
        foreach (AiPlayer ai in ais) ai.Board.WaveCooldownUntil = 2 * think;
        // The waves start and march off for a few thinks.
        for (int t = 0; t < 6 * think; t++)
        {
            if (sim.TickNumber % think == 0) foreach (AiPlayer ai in ais) ai.Think();
            sim.Tick();
        }
        Assert.All(ais, a => Assert.True(a.Board.WaveActive, $"player {a.Player} has no wave out"));
        int startMembers = ais.Min(a => a.ArmySlots.Take(a.ArmyCount).Count(s => a.Board.WaveGeneration[s] != 0));
        // A raider by every hall: an enemy line unit within defendRadius (8 m) of the hall's centre, seen, holding.
        for (int p = 0; p < Players; p++)
        {
            int cell = w.Buildings.Cell[halls[p]];
            BuildingDef hd = w.Data.Buildings[w.Buildings.TypeId[halls[p]]];
            var centre = new System.Numerics.Vector2((cell % w.NavGrid.Width + hd.FootprintWidth * 0.5f) * MapConstants.CellSize, (cell / w.NavGrid.Width + hd.FootprintHeight * 0.5f) * MapConstants.CellSize);
            int enemy = (p + 1) % Players;
            int line = Rts.Sim.ViewApi.StartBase.UnitOfSlot(w.Data, w.FactionOf(enemy), UnitSlot.Line);
            EntityHandle h = CombatScenes.Place(sim, enemy, line, centre + new System.Numerics.Vector2(0f, hd.FootprintHeight * MapConstants.CellSize * 0.5f + 1.5f));
            w.Units.Hold[h.Index] = true;
            CombatScenes.Spot(sim, p, h);
        }
        var sw = new Stopwatch();
        var times = new List<double>();
        var at = new List<(int Think, int Player)>();
        int triggered = 0, n = 0;
        for (int t = 0; t < 40 * think; t++)
        {
            if (sim.TickNumber % think == 0)
                foreach (AiPlayer ai in ais)
                {
                    sw.Restart();
                    ai.Think();
                    sw.Stop();
                    times.Add(sw.Elapsed.TotalMilliseconds);
                    at.Add((n, ai.Player));
                    if (ai.IntruderCount > 0 && ai.Board.Defending) triggered++;
                }
            if (sim.TickNumber % think == 0) n++;
            sim.Tick();
        }
        double avg = times.Average(), worst = times.Max();
        int wi = times.IndexOf(worst);
        double rest = times.Where((_, i) => at[i].Think > 0).Average();
        int pulls = ais.Sum(a => a.Board.WavePulls), recalls = ais.Sum(a => a.Board.WaveRecalls), active = ais.Count(a => a.Board.WaveActive);
        _out.WriteLine($"fewest members at the raid {startMembers}; {times.Count} thinks ({triggered} triggers): avg {avg:F3} ms, worst {worst:F3} ms (think {at[wi].Think}, player {at[wi].Player}), avg without the raid's first think {rest:F3} ms; pulls {pulls}, recalls {recalls}, waves still out {active}/{Players}");
        Assert.True(startMembers >= 200, $"a wave of only {startMembers}: the scene proves little");
        Assert.True(triggered >= times.Count / 2, $"only {triggered} of {times.Count} thinks triggered: the scene proves little");
        Assert.True(avg < BudgetMs, $"avg {avg:F3} ms over the {BudgetMs} ms budget");
    }
}
