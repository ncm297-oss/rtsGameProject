using System.Diagnostics;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using System.Numerics;
using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-2 criterion 5: what an AI think costs, and that it allocates nothing at steady state. Eight AI seats share 2,500 units
/// (a fifth of them workers) in eight bands of a generated 128 map with forests and mines; each player has a Town Hall, a
/// barracks, a crossbow range and a house, and gold and wood to spend. Its population is far over its cap, so the house rule
/// searches for a site at every think it has no house under construction, the heaviest path. M5-4b: every seat is under
/// attack (its buildings lose hp before each think), so the defense pass sends orders in every timed think. M5-4c: each seat
/// has <see cref="Casters"/> casters beside the next seat's units, ready at every think. Gated: a think's average under
/// 1 ms in the Debug build; the worst is reported.
/// </summary>
[Collection(SerialCollection.Name)]
public class AiPerfTests
{
    private const int Players = 8, Units = 2500, Casters = 16;
    private const double BudgetMs = 1.0;
    private readonly ITestOutputHelper _out;

    public AiPerfTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// M5-4c: the shipped data with a Sandstorm that doesn't block vision. Each Priest's storm would otherwise make every
    /// view's unit cursor test each unit against each storm (the fog's zone rule, M4-4b-2), a cost of the fog, not
    /// of a think, which with dozens of storms swamps what this row measures. What the AI does is the same either way.
    /// </summary>
    private static readonly Lazy<GameData> s_data = new(() => AiScenes.DataWith(dir => dir.EditJson("factions/whirlwind/abilities.json", root =>
    {
        foreach (JsonNode? a in root["abilities"]!.AsArray())
            foreach (JsonNode? fx in a!["effects"]!.AsArray())
                if ((string?)fx!["kind"] == "createZone") fx["blocksVision"] = false;
    })));

    /// <summary>The scene, every seat human, and one <see cref="AiPlayer"/> a player built here on the sim's own command door, so the test can time each think.</summary>
    private static (Simulation Sim, AiPlayer[] Ais) Scene()
    {
        GameData d = s_data.Value;
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
        // M5-4c: each seat's casters (its faction's unit types with abilities but the Cusser's one-hit kills, Casters in all)
        // beside the next seat's units, so every seat has casters with enemies in range at every think (beyond the 2,500).
        for (int p = 0; p < Players; p++)
        {
            int[] casterTypes = d.Factions[sim.World.FactionOf(p)].Units
                .Where(u => d.Units[u].Abilities.Length > 0 && !d.Abilities[d.Units[u].Abilities[0]].HitsBuildings).ToArray();
            int next = (p + 1) % Players, k = 0;
            for (int i = 0; i < sim.World.Units.Capacity && k < Casters; i++)
            {
                if (!sim.World.Units.Alive[i] || sim.World.Units.Owner[i] != next || i % 7 != 0) continue;
                Vector2 at = sim.World.Units.Position[i] + new Vector2(2.5f, 0f);
                if (sim.World.Units.TrySpawn(p, casterTypes[k % casterTypes.Length], d.Units[casterTypes[k % casterTypes.Length]], at, out _)) k++;
            }
            Assert.Equal(Casters, k);
        }
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
            if (sim.TickNumber % think == 0) UnderAttack(sim);
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
        double avg = times.Average(), worst = times.Max(), p95 = times.OrderBy(x => x).ElementAt((int)(times.Count * 0.95));
        _out.WriteLine($"{Players} AI seats, {sim.World.Units.Count} units: {times.Count} thinks, avg {avg:F3} ms, p95 {p95:F3} ms, worst {worst:F3} ms; {sent} commands sent");
        // M5-3: every seat thinks with an army of 200 or more (four in five of its units), so the military manager's rally and
        // wave passes are in the timed thinks.
        Assert.All(ais, a => Assert.True(a.ArmyCount >= 200, $"player {a.Player}: an army of {a.ArmyCount}"));
        // M5-4b: every seat is under attack at every think (its buildings lose hp), so the defense pass sends in the timed thinks.
        Assert.All(ais, a => Assert.True(a.Board.Defending && a.Board.DefenseVersion > 0, $"player {a.Player}: not defending"));
        // M5-4c: every seat casts in the timed thinks (its casters' cooldowns are cleared before each think).
        Assert.All(ais, a => Assert.True(a.Board.CastsSent > 0, $"player {a.Player}: no cast"));
        _out.WriteLine($"casts {string.Join(", ", ais.Select(a => a.Board.CastsSent))}");
        _out.WriteLine($"armies {string.Join(", ", ais.Select(a => a.ArmyCount))}; waves {string.Join(", ", ais.Select(a => a.Board.WavesStarted))}");
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
            if (sim.TickNumber % think == 0) UnderAttack(sim);
            if (sim.TickNumber % think == 0) foreach (AiPlayer ai in ais) ai.Think();
            sim.Tick();
        }
        while (sim.TickNumber % think != 0) sim.Tick();
        UnderAttack(sim);
        Action thinkAll = () => { foreach (AiPlayer ai in ais) ai.Think(); };
        AllocationProbe.AssertZero(thinkAll, _out);
        Assert.All(ais, a => Assert.True(a.Board.Defending, $"player {a.Player}: not defending")); // M5-4b: the defense pass ran
    }

    /// <summary>
    /// M5-4b: every live building loses 1 hp (a test seam), so each seat's defense triggers at its next think. M5-4c: and every
    /// cooldown is over and every unit at full hp (test seams), so every caster not casting is judged and searched for a target
    /// at every think, and the burns kill nobody (the armies stay over 200).
    /// </summary>
    private static void UnderAttack(Simulation sim)
    {
        Rts.Sim.Entities.UnitStore u = sim.World.Units;
        Array.Clear(u.AbilityReadyTick);
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i]) u.Hp[i] = sim.World.Data.Units[u.TypeId[i]].Hp;
        Rts.Sim.Entities.BuildingStore b = sim.World.Buildings;
        for (int k = 0; k < b.Capacity; k++)
            if (b.Alive[k] && b.Hp[k] > 1) b.SetHp(k, b.Hp[k] - 1);
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
