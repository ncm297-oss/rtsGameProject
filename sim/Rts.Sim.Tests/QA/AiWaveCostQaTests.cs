using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// QA (M5-3, session 2026-10-12-1930): the think cost with every seat's wave in progress. As <c>AiPerfTests</c> (eight Hard
/// seats, 2,500 units in eight bands of a generated 128 map, four in five army units), plus a holding spotter of each player
/// beside the next player's Town Hall, so every seat knows an enemy building from the first think: each seat starts a wave of
/// 200+ members (200+ AttackMoves in one think), then runs the wave pass (members, retreat check, target known) every think.
/// Gated as the budget row: a think's average under 1 ms (Debug); no allocation in a think with the waves in progress.
/// </summary>
[Collection(SerialCollection.Name)]
public class AiWaveCostQaTests
{
    private const int Players = 8, Units = 2500;
    private const double BudgetMs = 1.0;
    private readonly ITestOutputHelper _out;

    public AiWaveCostQaTests(ITestOutputHelper output) => _out = output;

    private static (Simulation Sim, AiPlayer[] Ais) Scene()
    {
        GameData d = TestSim.Data;
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
        // A spotter of each player beside the next player's hall, on the nearest open cell round it.
        for (int p = 0; p < Players; p++)
        {
            int hall = halls[(p + 1) % Players], cell = sim.World.Buildings.Cell[hall];
            int hx = cell % g.Width, hy = cell / g.Width;
            bool done = false;
            for (int r = 3; r < 12 && !done; r++)
                for (int dy = -r; dy <= r && !done; dy++)
                    for (int dx = -r; dx <= r && !done; dx++)
                    {
                        int x = hx + dx, y = hy + dy;
                        if (!g.IsPassable(x, y) || sim.World.Buildings.SlotAt(x, y) >= 0) continue;
                        CombatScenes.Spotter(sim, p, g.CellCenter(x, y));
                        done = true;
                    }
            Assert.True(done, $"no spot for player {p}'s spotter");
        }
        for (int t = 0; t < 8; t++) sim.Tick();
        var ais = new AiPlayer[Players];
        for (int p = 0; p < Players; p++)
            ais[p] = new AiPlayer(new PlayerView(sim.World, p), d, d.Ai[sim.World.FactionOf(p)], AiDifficulty.Hard,
                new SimRng(3, (ulong)RngStream.Ai(p)), sim);
        return (sim, ais);
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void EightSeats_EachWithA200UnitWaveInProgress_AThinkUnder1Ms()
    {
        (Simulation sim, AiPlayer[] ais) = Scene();
        var sw = new Stopwatch();
        var times = new List<double>();
        double firstWorst = 0;
        int startMembers = int.MaxValue;
        int think = ais[0].ThinkTicks;
        // Warm up ten thinks with the waves held back (the cooldown), then let them start: the start think is timed alone.
        foreach (AiPlayer ai in ais) ai.Board.WaveCooldownUntil = 10 * think;
        for (int t = 0; t < 70 * think; t++)
        {
            if (sim.TickNumber % think == 0)
                foreach (AiPlayer ai in ais)
                {
                    bool starting = !ai.Board.WaveActive;
                    sw.Restart();
                    Assert.True(ai.Think());
                    sw.Stop();
                    if (starting && ai.Board.WaveActive)
                    {
                        firstWorst = Math.Max(firstWorst, sw.Elapsed.TotalMilliseconds);
                        startMembers = Math.Min(startMembers, ai.ArmySlots.Take(ai.ArmyCount).Count(s => ai.Board.WaveGeneration[s] != 0));
                    }
                    else if (t >= 20 * think) times.Add(sw.Elapsed.TotalMilliseconds);
                }
            sim.Tick();
        }
        int members = ais.Min(a => a.ArmySlots.Take(a.ArmyCount).Count(s => a.Board.WaveGeneration[s] != 0));
        double avg = times.Average(), worst = times.Max(), p95 = times.OrderBy(x => x).ElementAt((int)(times.Count * 0.95));
        _out.WriteLine($"waves {string.Join(", ", ais.Select(a => a.Board.WavesStarted))}; fewest members at the start {startMembers}, at the end {members}; wave-start think worst {firstWorst:F3} ms; " +
            $"{times.Count} wave thinks: avg {avg:F3} ms, p95 {p95:F3} ms, worst {worst:F3} ms; {ais.Sum(a => a.CommandsSent)} commands");
        Assert.All(ais, a => Assert.True(a.Board.WavesStarted >= 1, $"player {a.Player} never waved: the scene proves little"));
        // Re-check round 1: the scene's size is gated at the wave start. Since BUG-0591's fix a combat-off member Idle by the
        // seen target gets one Attack and then goes home out of the wave, so the end count drains (147 of 200+ measured); the
        // timed thinks still carry the 200+ waves while the members walk.
        Assert.True(startMembers >= 200, $"a wave start of only {startMembers}");
        Assert.True(avg < BudgetMs, $"avg {avg:F3} ms over the {BudgetMs} ms budget");
        Assert.True(firstWorst < 5 * BudgetMs, $"a wave start (200+ AttackMoves) took {firstWorst:F3} ms"); // reported; a one-think spike, gated loosely
    }

    [Fact]
    public void AThinkWithWavesInProgress_AllocatesNothing()
    {
        (Simulation sim, AiPlayer[] ais) = Scene();
        int think = ais[0].ThinkTicks;
        for (int t = 0; t < 10 * think; t++)
        {
            if (sim.TickNumber % think == 0) foreach (AiPlayer ai in ais) ai.Think();
            sim.Tick();
        }
        while (sim.TickNumber % think != 0) sim.Tick();
        Assert.All(ais, a => Assert.True(a.Board.WaveActive, $"player {a.Player}: no wave in progress"));
        AllocationProbe.AssertZero(() => { foreach (AiPlayer ai in ais) ai.Think(); }, _out);
    }
}
