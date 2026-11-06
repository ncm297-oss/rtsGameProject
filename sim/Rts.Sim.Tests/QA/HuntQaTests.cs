using System.Diagnostics;
using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA, M5-4d (session 2026-11-06-0926): the hunt's cost and its stalls. Four seats on a generated 256 map with no building
/// anywhere (so no enemy building is ever known and every seat hunts the whole time), 60 army units each, combat off: a
/// hunting think stays under the 1 ms gate and allocates nothing (the shipped perf rows never hunt). An enemy unit sealed in
/// a pocket of blocked cells, seen but out of reach: the hunt neither spams orders nor freezes the AI's offence for good.
/// A last unit at the map's edge or corner must be found by the sweep (BUG-1107: three rows red until it is fixed).
/// </summary>
[Collection(SerialCollection.Name)]
public class HuntQaTests
{
    private const int Players = 4, ArmyEach = 60;
    private readonly ITestOutputHelper _out;

    public HuntQaTests(ITestOutputHelper output) => _out = output;

    private static (Simulation Sim, AiPlayer[] Ais) HuntScene()
    {
        GameData d = TestSim.Data;
        var map = MapGenParams.Default with { Width = 256, Height = 256, Forests = 8, GoldMines = 6 };
        var sim = new Simulation(new SimConfig(5, Players, 1024, 4096) { Data = d, Map = map, Combat = false });
        NavGrid g = sim.World.NavGrid;
        for (int p = 0; p < Players; p++)
        {
            int f = sim.World.FactionOf(p);
            int line = StartBase.UnitOfSlot(d, f, UnitSlot.Line);
            int cx = p % 2 == 0 ? g.Width / 4 : 3 * g.Width / 4, cy = p < 2 ? g.Height / 4 : 3 * g.Height / 4;
            int placed = 0;
            for (int r = 0; r < 60 && placed < ArmyEach; r++)
                for (int y = cy - r; y <= cy + r && placed < ArmyEach; y++)
                    for (int x = cx - r; x <= cx + r && placed < ArmyEach; x++)
                    {
                        if (Math.Max(Math.Abs(x - cx), Math.Abs(y - cy)) != r || !g.IsPassable(x, y)) continue;
                        CombatScenes.Place(sim, p, line, g.CellCenter(x, y));
                        placed++;
                    }
            Assert.Equal(ArmyEach, placed);
        }
        for (int t = 0; t < 8; t++) sim.Tick();
        var ais = new AiPlayer[Players];
        for (int p = 0; p < Players; p++)
        {
            ais[p] = new AiPlayer(new PlayerView(sim.World, p), d, d.Ai[sim.World.FactionOf(p)], AiDifficulty.Normal,
                new SimRng(5, (ulong)RngStream.Ai(p)), sim);
            ais[p].Board.WavesStarted = 1; // the hunt's precondition: a wave was sent before
        }
        return (sim, ais);
    }

    /// <summary>
    /// The hunting think's cost at four seats on 256: every timed think is a hunting one (a building never exists), the
    /// sweep re-targets and sightings re-target along the way; the average stays under 1 ms (Debug).
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void FourHuntingSeats_On256_AThinkUnder1Ms()
    {
        (Simulation sim, AiPlayer[] ais) = HuntScene();
        var sw = new Stopwatch();
        var times = new List<double>();
        int think = ais[0].ThinkTicks, hunting = 0, retargets = 0;
        var lastTarget = new Vector2[Players];
        for (int t = 0; t < 300 * think; t++)
        {
            if (sim.TickNumber % think == 0)
                for (int p = 0; p < Players; p++)
                {
                    AiPlayer ai = ais[p];
                    sw.Restart();
                    Assert.True(ai.Think());
                    sw.Stop();
                    if (!ai.Board.Hunting) continue;
                    hunting++;
                    if (ai.Board.WaveTargetPoint != lastTarget[p]) retargets++;
                    lastTarget[p] = ai.Board.WaveTargetPoint;
                    if (t >= 10 * think) times.Add(sw.Elapsed.TotalMilliseconds);
                }
            sim.Tick();
        }
        double avg = times.Average(), worst = times.Max(), p95 = times.OrderBy(x => x).ElementAt((int)(times.Count * 0.95));
        int hunts = ais.Sum(a => a.Board.HuntsStarted);
        _out.WriteLine($"{Players} hunting seats, {sim.World.Units.Count} units, 256 map: {times.Count} hunting thinks timed, avg {avg:F3} ms, p95 {p95:F3} ms, worst {worst:F3} ms; hunts {hunts}, re-targets {retargets}, sweep marks {ais.Sum(a => a.Board.HuntVisited.Count(v => v))}");
        Assert.True(hunting > 200, $"only {hunting} hunting thinks");
        Assert.True(retargets > 20, $"only {retargets} re-targets: the sweep barely moved");
        Assert.True(avg < 1.0, $"avg {avg:F3} ms over the 1 ms budget");
    }

    /// <summary>A hunting think (a sweep moving on, sightings followed) allocates nothing.</summary>
    [Fact]
    public void AHuntingThink_AllocatesNothing()
    {
        (Simulation sim, AiPlayer[] ais) = HuntScene();
        int think = ais[0].ThinkTicks;
        for (int t = 0; t < 60 * think; t++)
        {
            if (sim.TickNumber % think == 0) foreach (AiPlayer ai in ais) ai.Think();
            sim.Tick();
        }
        Assert.All(ais, a => Assert.True(a.Board.Hunting, $"player {a.Player}: not hunting"));
        // Many thinks in the probe so a re-target (NextSweep, HuntTarget) and a sighting land inside it.
        Action block = () =>
        {
            for (int t = 0; t < 40 * think; t++)
            {
                if (sim.TickNumber % think == 0) foreach (AiPlayer ai in ais) ai.Think();
                sim.Tick();
            }
        };
        int before = ais.Sum(a => a.Board.HuntVisited.Count(v => v));
        AllocationProbe.AssertZero(block, _out);
        int after = ais.Sum(a => a.Board.HuntVisited.Count(v => v));
        _out.WriteLine($"sweep marks {before} -> {after}");
    }

    // ---------- a sighting seen but out of reach ----------

    private static readonly Lazy<GameData> s_quiet = new(() => AiScenes.DataWith(dir =>
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            foreach (JsonNode? o in root["openings"]!.AsArray())
            {
                o!["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1));
                o["attack"] = new JsonObject { ["minArmyPop"] = 200, ["minArmyCost"] = 0 };
            }
        })));

    /// <summary>
    /// Player 1 reduced to one Raider (its hall and workers removed), sealed inside a ring of blocked cells (a pocket 4 cells out: 8+ m, past <c>ai.rallyReach</c>, inside
    /// sight once beside it), combat off, player 0 hunting with four Heavy Infantry: over 24,000 ticks the hunt must not spam
    /// orders (bounded commands per member per minute) and must not freeze: the members keep being ordered somewhere,
    /// and a hunt is (re)started after any end.
    /// </summary>
    [Fact]
    public void AnUnreachableSighting_NeitherSpamsNorFreezesTheHunt()
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), s_quiet.Value);
        World w = sim.World;
        int heavy = TestSim.Data.FindUnit("malazan_heavy_infantry"), raider = TestSim.Data.FindUnit("whirlwind_raider");
        const int px = 30, py = 48, ring = 4;
        for (int y = py - ring; y <= py + ring; y++)
            for (int x = px - ring; x <= px + ring; x++)
                if (Math.Max(Math.Abs(x - px), Math.Abs(y - py)) == ring) w.NavGrid.SetResource(x, y, 1, 1);
        sim.Tick();
        // Player 1 keeps nothing but the sealed Raider: no building to find, so the AI can only hunt.
        for (int i = 0; i < w.Buildings.Capacity; i++)
            if (w.Buildings.Alive[i] && w.Buildings.Owner[i] == 1) Assert.True(w.Buildings.Free(new EntityHandle(i, w.Buildings.Generation[i])));
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 1) w.Units.Free(new EntityHandle(i, w.Units.Generation[i]));
        EntityHandle enemy = CombatScenes.Place(sim, 1, raider, w.NavGrid.CellCenter(px, py));
        var army = new List<EntityHandle>();
        for (int k = 0; k < 4; k++) army.Add(CombatScenes.Place(sim, 0, heavy, w.NavGrid.CellCenter(18 + k, 34)));
        AiPlayer ai = sim.AiOf(0)!;
        ai.Board.WavesStarted = 1;
        List<Command> log = AiScenes.Log(sim, 0);
        var perMinute = new List<int>();
        int lastCount = 0, nearestTicks = 0;
        float nearest = float.MaxValue;
        for (int t = 0; t < 24_000; t++)
        {
            sim.Tick();
            foreach (EntityHandle h in army)
            {
                float d = Vector2.Distance(w.Units.Position[h.Index], w.Units.Position[enemy.Index]);
                if (d < nearest) nearest = d;
                if (d < 14f) nearestTicks++;
            }
            if (sim.TickNumber % 1200 == 0)
            {
                int n = log.Count(c => army.Contains(c.Unit));
                perMinute.Add(n - lastCount);
                lastCount = n;
            }
        }
        _out.WriteLine($"orders to the 4 members per minute: {string.Join(", ", perMinute)}; hunts {ai.Board.HuntsStarted}; nearest approach {nearest:F1} m, member-ticks within sight {nearestTicks}; " +
            $"hunting at the end {ai.Board.Hunting}, wave {ai.Board.WaveActive}, known buildings {ai.Board.KnownEnemyBuildings}, sweep {ai.Board.HuntSweep}, memory tick {ai.Board.EnemyUnitTick} at {ai.Board.EnemyUnitPoint}, cooldown until {ai.Board.WaveCooldownUntil}; members at " +
            string.Join(" ", army.Select(h => $"{w.Units.Position[h.Index]}:{w.Units.State[h.Index]}")) + $"; last orders: " + string.Join("; ", log.Where(c => army.Contains(c.Unit)).TakeLast(6).Select(c => $"{c.Tick} {c.Kind} {c.Position}")));
        Assert.True(nearest > ai.Rules.RallyReach, "the pocket was reachable: the scene is wrong");
        Assert.True(perMinute.Max() <= 4 * 12, $"order spam: {perMinute.Max()} orders to 4 members in a minute");
        // Not frozen: in the last 10 minutes the members were ordered at least once more.
        Assert.True(perMinute.Skip(perMinute.Count - 10).Sum() > 0, "the hunt froze: no order in the last 10 minutes");
    }
    /// <summary>
    /// BUG-1107 (S2): the hunt's sweep must find a last enemy unit standing at the map's edge or corner. Player 1 is reduced
    /// to one worker (hall and other workers removed) standing in cell (<paramref name="x"/>, <paramref name="y"/>); player 0
    /// hunts with four Heavy Infantry (combat off: being seen is what counts). Within two full sweeps (20,000 ticks on the
    /// 64-cell flat map, 13 sweep points) the AI must have seen it (a sighting remembered) at least once.
    /// </summary>
    [Theory]
    [InlineData(1, 1)]   // a corner
    [InlineData(62, 62)] // the opposite corner
    [InlineData(1, 32)]  // the west edge's middle
    [InlineData(32, 1)]  // the north edge's middle
    [InlineData(32, 32)] // control: the map's centre
    public void AHuntFindsALastUnitAtTheMapEdge(int x, int y)
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), s_quiet.Value);
        World w = sim.World;
        int heavy = TestSim.Data.FindUnit("malazan_heavy_infantry"), worker = TestSim.Data.FindUnit("whirlwind_camp_follower");
        sim.Tick();
        for (int i = 0; i < w.Buildings.Capacity; i++)
            if (w.Buildings.Alive[i] && w.Buildings.Owner[i] == 1) Assert.True(w.Buildings.Free(new EntityHandle(i, w.Buildings.Generation[i])));
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 1) w.Units.Free(new EntityHandle(i, w.Units.Generation[i]));
        Assert.True(w.NavGrid.IsPassable(x, y));
        EntityHandle enemy = CombatScenes.Place(sim, 1, worker, w.NavGrid.CellCenter(x, y));
        for (int k = 0; k < 4; k++) CombatScenes.Place(sim, 0, heavy, w.NavGrid.CellCenter(18 + k, 34));
        AiPlayer ai = sim.AiOf(0)!;
        ai.Board.WavesStarted = 1;
        int seenAt = -1, sweeps = 0, lastSweep = -2;
        while (sim.TickNumber < 20_000 && seenAt < 0)
        {
            sim.Tick();
            if (ai.Board.EnemyUnitTick >= 0) seenAt = sim.TickNumber;
            if (ai.Board.HuntSweep != lastSweep) { sweeps++; lastSweep = ai.Board.HuntSweep; }
        }
        _out.WriteLine($"unit at cell ({x}, {y}) = {w.Units.Position[enemy.Index]}: seen at tick {seenAt}; hunting {ai.Board.Hunting}, {sweeps} sweep targets, visited {ai.Board.HuntVisited.Count(v => v)} / {ai.Board.HuntVisited.Length}");
        Assert.True(ai.Board.HuntsStarted > 0, "no hunt started");
        Assert.True(seenAt >= 0, $"a last unit at cell ({x}, {y}) was never seen by a hunt in 20,000 ticks ({sweeps} sweep targets)");
    }
}
