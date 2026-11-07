using System.Diagnostics;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA, M5-4d slice 2 (session 2026-11-07-0236), criterion 8 under the rebuild paths: eight Hard AI seats (each with a hall,
/// barracks, range and house, 300 units, a full bank) on a generated 128 map, and before every think either (a) every
/// seat's Town Hall (finished or a site) is razed, so the hall-first rule searches and sends at every think it has none in
/// flight, or (b) every build-order building the seat holds is razed with every <c>build</c> step marked done, so the rebuild
/// scan reopens steps and their site searches run at every think. Gated as <c>AiPerfTests</c>: a think's average under
/// 1 ms (Debug); and a think on either path allocates nothing.
/// </summary>
[Collection(SerialCollection.Name)]
public class RebuildThinkCostQaTests
{
    private const int Players = 8, UnitsPerSeat = 300;
    private readonly ITestOutputHelper _out;

    public RebuildThinkCostQaTests(ITestOutputHelper output) => _out = output;

    private static (Simulation Sim, AiPlayer[] Ais) Scene()
    {
        GameData d = TestSim.Data;
        var map = MapGenParams.Default with { Forests = 6, GoldMines = 4 };
        var sim = new Simulation(new SimConfig(3, Players, Players * UnitsPerSeat + 400, 4096) { Data = d, Map = map, Combat = false });
        NavGrid g = sim.World.NavGrid;
        var open = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
            if (g.IsPassable(c % g.Width, c / g.Width)) open.Add(c);
        int band = open.Count / Players;
        for (int p = 0; p < Players; p++)
        {
            int f = sim.World.FactionOf(p);
            int from = p * band + band / 3;
            foreach (BuildingSlot slot in new[] { BuildingSlot.TownHall, BuildingSlot.InfantryHall, BuildingSlot.RangedHall, BuildingSlot.House })
            {
                int type = Rts.Sim.ViewApi.StartBase.BuildingOfSlot(d, f, slot);
                for (int k = from; k < open.Count; k += 7)
                    if (sim.World.Buildings.Spawn(p, type, open[k], out _)) { from = k + 40; break; }
            }
            BuildMaps.SetTotals(sim, p, 100_000, 100_000);
            FactionDef fd = d.Factions[f];
            int[] army = fd.Units.Where(u => d.Units[u].Slot != UnitSlot.Worker).Take(3).ToArray();
            int worker = Rts.Sim.ViewApi.StartBase.UnitOfSlot(d, fd.Id, UnitSlot.Worker);
            int placed = 0;
            for (int k = p * band + band / 2; placed < UnitsPerSeat && k < (p + 1) * band; k += 2)
            {
                int c = open[k];
                if (sim.World.Buildings.SlotAt(c % g.Width, c / g.Width) >= 0) continue;
                CombatScenes.Place(sim, p, placed % 5 == 0 ? worker : army[placed % 3], g.CellCenter(c % g.Width, c / g.Width));
                placed++;
            }
        }
        for (int t = 0; t < 8; t++) sim.Tick();
        var ais = new AiPlayer[Players];
        for (int p = 0; p < Players; p++)
            ais[p] = new AiPlayer(new PlayerView(sim.World, p), d, d.Ai[sim.World.FactionOf(p)], AiDifficulty.Hard, new SimRng(3, (ulong)RngStream.Ai(p)), sim);
        return (sim, ais);
    }

    /// <summary>Razes before a think: (a) <paramref name="halls"/>: every hall; (b) else every building a build step names, with every build step marked done.</summary>
    private static void Raze(Simulation sim, AiPlayer[] ais, bool halls)
    {
        BuildingStore b = sim.World.Buildings;
        foreach (AiPlayer ai in ais)
        {
            var steps = ai.Opening.BuildOrder;
            for (int k = 0; k < b.Capacity; k++)
            {
                if (!b.Alive[k] || b.Owner[k] != ai.Player) continue;
                int type = b.TypeId[k];
                bool named = false;
                for (int i = 0; i < steps.Length; i++) named |= steps[i].Action == AiStepAction.Build && steps[i].Target == type;
                if (halls ? type == ai.TownHallType : named && type != ai.TownHallType) b.Free(new EntityHandle(k, b.Generation[k]));
            }
            if (!halls)
                for (int i = 0; i < steps.Length; i++)
                    if (steps[i].Action == AiStepAction.Build) ai.Board.StepDone[i] = true;
            BuildMaps.SetTotals(sim, ai.Player, 100_000, 100_000);
        }
    }

    [Theory]
    [Trait("Category", "Perf")]
    [InlineData(true)]
    [InlineData(false)]
    public void EightSeats_RebuildingAtEveryThink_AThinkUnder1Ms(bool halls)
    {
        (Simulation sim, AiPlayer[] ais) = Scene();
        var sw = new Stopwatch();
        var times = new List<double>();
        int think = ais[0].ThinkTicks;
        int sentBefore = 0, builds = 0;
        bool timed = false;
        foreach (AiPlayer ai in ais)
        {
            AiPlayer seat = ai;
            seat.SentForTests = c =>
            {
                if (timed && c.Kind == Rts.Sim.Commands.CommandKind.Build && (halls ? c.TypeId == seat.TownHallType : c.TypeId != seat.TownHallType)) builds++;
            };
        }
        for (int t = 0; t < 60 * think; t++)
        {
            if (sim.TickNumber % think == 0)
            {
                Raze(sim, ais, halls);
                if (!timed && t >= 10 * think) { sentBefore = ais.Sum(a => a.CommandsSent); timed = true; }
                foreach (AiPlayer ai in ais)
                {
                    sw.Restart();
                    Assert.True(ai.Think());
                    sw.Stop();
                    if (t >= 10 * think) times.Add(sw.Elapsed.TotalMilliseconds);
                }
            }
            sim.Tick();
        }
        double avg = times.Average(), worst = times.Max(), p95 = times.OrderBy(x => x).ElementAt((int)(times.Count * 0.95));
        int sent = ais.Sum(a => a.CommandsSent) - sentBefore;
        _out.WriteLine($"{(halls ? "halls razed" : "build-order buildings razed")}: {times.Count} thinks, avg {avg:F3} ms, p95 {p95:F3} ms, worst {worst:F3} ms; {sent} commands sent in the timed thinks, {builds} of them rebuild Builds");
        Assert.True(builds > 0, "nothing was rebuilt: the row measures nothing");
        Assert.True(avg < 1.0, $"avg {avg:F3} ms over the 1 ms budget");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARebuildingThink_AllocatesNothing(bool halls)
    {
        (Simulation sim, AiPlayer[] ais) = Scene();
        int think = ais[0].ThinkTicks;
        for (int t = 0; t < 20 * think; t++)
        {
            if (sim.TickNumber % think == 0)
            {
                Raze(sim, ais, halls);
                foreach (AiPlayer ai in ais) ai.Think();
            }
            sim.Tick();
        }
        while (sim.TickNumber % think != 0) sim.Tick();
        Action thinkAll = () => { foreach (AiPlayer ai in ais) ai.Think(); };
        AllocationProbe.AssertZero(thinkAll, _out, setup: () => Raze(sim, ais, halls));
        if (halls) Assert.All(ais, a => Assert.True(a.TownHallCell < 0, $"player {a.Player} has a hall"));
    }
}
