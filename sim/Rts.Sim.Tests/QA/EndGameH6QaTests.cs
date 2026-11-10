using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA, M5-H6 (session 2026-11-09-1336, full tier): the end-of-M5 hardening's shipped levers attacked on scenes.
/// <list type="bullet">
/// <item>The hunt's free starts first (lever (i)): a twin fog oracle (two runs that differ only in where an unseen remnant
/// stands must send the hunt to the same targets on the same ticks until one of them sees it) and hide-and-seek rows on the
/// developer's 512 m four-start flat map (a lone worker at a far free start, at its own start, sealed in a corner pocket,
/// walking between the two free starts).</item>
/// <item>The house rule (lever (iii)) under siege: a beaten Whirlwind seat with 3 workers and 10 tents loses tent after
/// tent and rebuilds none; a seat at its cap does; the margin's boundary.</item>
/// <item>BUG-1158's leg-death cap at its boundary: exactly <c>ai.scoutReplacements</c> deaths (the next worker still walks);
/// a death on the tick a start is marked looked and on the tick before; earlier non-leg losses (the shipped rule reads the
/// match's losses: the first leg death then ends the leg); a save taken after a leg death loaded in a child process.</item>
/// <item>No allocation in the new sweep code.</item>
/// </list>
/// </summary>
[Collection(SerialCollection.Name)]
public class EndGameH6QaTests
{
    private readonly ITestOutputHelper _out;

    public EndGameH6QaTests(ITestOutputHelper output) => _out = output;

    private static int Heavy => TestSim.Data.FindUnit("malazan_heavy_infantry");

    // ---------------------------------------------------------------- the hunt's free starts

    /// <summary>The developer's 512 m flat map's four starts (cells): 0 the hunter's, 1 the remnant's own, 2 and 3 free.</summary>
    private static readonly StartLocation[] Starts512 = { new(7 * 256 + 136, 0), new(240 * 256 + 128, 0), new(200 * 256 + 200, 0), new(200 * 256 + 40, 0) };

    private static readonly Lazy<GameData> s_quiet = new(() => AiScenes.DataWith(dir =>
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            foreach (JsonNode? o in root["openings"]!.AsArray())
            {
                o!["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1));
                o["attack"] = new JsonObject { ["minArmyPop"] = 2, ["minArmyCost"] = 0 };
            }
        })));

    private static void NextThink(Simulation sim)
    {
        int think = sim.AiOf(0)!.ThinkTicks;
        do sim.Tick(); while ((sim.TickNumber - 1) % think != 0);
    }

    private sealed class Hide
    {
        public int SeenAfter = -1, Start;
        public readonly List<(int Tick, Vector2 Target)> Targets = new();
        public Simulation Sim = null!;
        public EntityHandle Worker;
    }

    /// <summary>
    /// The 512 m scene: player 1 (a human seat) keeps one camp follower at <paramref name="hide"/>; player 0 hunts with four
    /// Heavy Infantry from round (40, 60) m, with <paramref name="huntsBefore"/> hunts already started (2+ = lever (i) on).
    /// <paramref name="block"/> may seal cells first; <paramref name="perTick"/> runs after each tick. Runs to the first
    /// sighting or <paramref name="bound"/> ticks after the hunt began.
    /// </summary>
    private static Hide RunHide(Vector2 hide, int huntsBefore, int bound, Action<World>? block = null, Action<Hide>? perTick = null, bool stopAtSight = true)
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), s_quiet.Value,
            map: LocalMovementTests.Flat(256).WithStartLocations(Starts512));
        World w = sim.World;
        block?.Invoke(w);
        AiPlayer ai = sim.AiOf(0)!;
        ai.Board.WaveCooldownUntil = int.MaxValue;
        sim.Tick();
        for (int i = 0; i < w.Buildings.Capacity; i++)
            if (w.Buildings.Alive[i] && w.Buildings.Owner[i] == 1) Assert.True(w.Buildings.Free(new EntityHandle(i, w.Buildings.Generation[i])));
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 1) w.Units.Free(new EntityHandle(i, w.Units.Generation[i]));
        var h = new Hide { Sim = sim, Worker = CombatScenes.Place(sim, 1, TestSim.Data.FindUnit("whirlwind_camp_follower"), hide) };
        for (int k = 0; k < 4; k++) CombatScenes.Place(sim, 0, Heavy, w.NavGrid.CellCenter(18 + k, 28));
        NextThink(sim);
        ai.Board.WavesStarted = 1;
        ai.Board.HuntsStarted = huntsBefore;
        ai.Board.WaveCooldownUntil = 0;
        h.Start = sim.TickNumber;
        while (sim.TickNumber < h.Start + bound)
        {
            sim.Tick();
            if (ai.Board.Hunting && (h.Targets.Count == 0 || h.Targets[^1].Target != ai.Board.WaveTargetPoint)) h.Targets.Add((sim.TickNumber, ai.Board.WaveTargetPoint));
            if (h.SeenAfter < 0 && w.Fog.CanSeeUnit(0, h.Worker.Index))
            {
                h.SeenAfter = sim.TickNumber - h.Start;
                if (stopAtSight) break;
            }
            perTick?.Invoke(h);
        }
        return h;
    }

    private static Vector2 StartCentre(int k) => new((Starts512[k].Cell % 256 + 0.5f) * MapConstants.CellSize, (Starts512[k].Cell / 256 + 0.5f) * MapConstants.CellSize);

    private string Show(Hide h) => $"seen {(h.SeenAfter < 0 ? "never" : $"after {h.SeenAfter}")}; pass {h.Sim.AiOf(0)!.Board.HuntPass}, hunts {h.Sim.AiOf(0)!.Board.HuntsStarted}, {h.Targets.Count} targets: {string.Join(" ", h.Targets.Take(6).Select(t => $"{t.Tick - h.Start}:{t.Target}"))}";

    /// <summary>
    /// The fog oracle for lever (i): the hunt may read the start list (map data) and who is defeated (public), never where an
    /// unseen enemy stands. Two runs of the second hunt differ only in the remnant's place: at free start 2 (a far depot's
    /// worker) and at the far corner, or at free start 3 and at its own start 1. Until either run's hunter first sees its
    /// remnant, both runs send the hunt to the same targets on the same ticks.
    /// </summary>
    [Theory]
    [InlineData(2, -1)]
    [InlineData(3, 1)]
    [InlineData(2, 3)]
    public void TheHuntsStarts_DoNotDependOnWhereAnUnseenRemnantStands(int startA, int startB)
    {
        Vector2 a = StartCentre(startA), b = startB < 0 ? new Vector2(500f, 500f) : StartCentre(startB);
        Hide ra = RunHide(a, 1, 20_000, stopAtSight: true), rb = RunHide(b, 1, 20_000, stopAtSight: true);
        _out.WriteLine($"A at {a}: {Show(ra)}");
        _out.WriteLine($"B at {b}: {Show(rb)}");
        int until = Math.Min(ra.SeenAfter < 0 ? int.MaxValue : ra.SeenAfter, rb.SeenAfter < 0 ? int.MaxValue : rb.SeenAfter);
        var ta = ra.Targets.Where(t => t.Tick - ra.Start < until).ToList();
        var tb = rb.Targets.Where(t => t.Tick - rb.Start < until).ToList();
        Assert.True(ta.Count >= 1, "no hunt target before the first sighting");
        Assert.Equal(ta.Select(t => (t.Tick - ra.Start, t.Target)), tb.Select(t => (t.Tick - rb.Start, t.Target)));
    }

    /// <summary>
    /// Hide-and-seek on the second hunt (lever (i) on), each row against the first hunt (lever off) as the reference: a lone
    /// worker at free start 3's centre (the nearer free start), at its own start 1 (not a free start: the lever walks away
    /// from it first), in the corner diagonal from the army, and at a quarter-cell interior hide. Asserts it is seen within
    /// 72,000 ticks (the hide rows' bound) with the lever on, and prints both times (the lever's cost or gain). Measured at
    /// M5-H6 (second / first hunt): free start 3 60,945 / never (BUG-1161 item 2), own start 1 never / 39,797 (BUG-1162), far
    /// corner 17,921 / 7,441, interior quarter 51,545 / 41,289.
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData("free start 3", 82f, 402f)]
    [InlineData("own start 1", 258f, 482f, Skip = "BUG-1162 (open, S3): with the free starts first, a lone worker at its own start is not seen on the second hunt in 72,000 ticks (the first hunt sees it at 39,797)")]
    [InlineData("far corner", 500f, 500f)]
    [InlineData("interior quarter", 166.5f, 344.7f)]
    public void ALoneWorker_OnTheSecondHunt_IsFound(string where, float x, float y)
    {
        Hide on = RunHide(new Vector2(x, y), 1, 72_000), off = RunHide(new Vector2(x, y), 0, 72_000);
        _out.WriteLine($"{where}: second hunt {Show(on)}");
        _out.WriteLine($"{where}: first hunt  {Show(off)}");
        Assert.True(on.SeenAfter >= 0, $"{where}: never seen on the second hunt in 72,000 ticks");
    }

    /// <summary>
    /// Lever (i)'s promise: when the hunt "looks at" a free start, a remnant standing at that start's centre is seen. The
    /// start proxy is the interior sweep point nearest the centre, which on the even pass can lie up to half a scout cell
    /// (25.6 m) off it, past a Heavy Infantry's 14 m sight: on the developer's own 512 m scene start 3's proxy (76.8, 384)
    /// is 17.6 m from its centre (81, 401). Asserts the worker at start 3's centre is seen before the hunt's second target
    /// (the army stood at start 3's proxy first).
    /// </summary>
    [Fact(Skip = "BUG-1161 item 2 (open, S4): the hunt's start proxy can lie past the army's sight from the start's centre; a remnant at free start 3's centre is walked past (shipped map files: 1.4 / 7.6 m, inside sight)")]
    public void AWorkerAtAFreeStartsCentre_IsSeenWhenTheHuntLooksAtThatStart()
    {
        Hide h = RunHide(StartCentre(3), 1, 12_000, stopAtSight: true);
        _out.WriteLine($"start 3 centre {StartCentre(3)}: {Show(h)}");
        Assert.True(h.Targets.Count >= 1);
        int secondTarget = h.Targets.Count >= 2 ? h.Targets[1].Tick - h.Start : int.MaxValue;
        Assert.True(h.SeenAfter >= 0 && h.SeenAfter < secondTarget, $"seen {(h.SeenAfter < 0 ? "never" : $"after {h.SeenAfter}")}, the hunt left start 3's proxy {h.Targets[0].Target} at {secondTarget}");
    }

    /// <summary>
    /// The shipped maps' start proxies: for every start location of every map file and generated seeds 1-8 (the sizes the
    /// tally and the criterion rows use), the distance from the start's centre to its even-pass proxy, against the shortest
    /// sight of the hunt's usual members (14 m). Prints the table; asserts only for maps with more starts than a 1v1 uses
    /// (a 2-start map never has a free start while the match lasts): Pale Hills 1.4 m (the developer's figure), Vathar 7.6 m.
    /// </summary>
    [Fact]
    public void TheShippedMapsStartProxies_LieWithinSightOfTheirStarts()
    {
        GameData data = TestSim.Data;
        float sight = 14f; // Heavy Infantry / Line: the hunt's usual members
        var bad = new List<string>();
        foreach (string map in new[] { "pale_hills", "vathar_crossing", "raraku" })
        {
            int size = data.Maps[data.FindMap(map)].Size;
            PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Ai, AiDifficulty.Normal) };
            Simulation sim = map == "raraku"
                ? AiScenes.Generated(seats, 1, false, out _, configure: c => c with { MapId = map })
                : AiScenes.Generated(seats, 1, false, out _, configure: c => c with { MapId = map, Map = c.Map with { Width = size, Height = size } });
            sim.Tick();
            AiPlayer ai = sim.AiOf(0)!;
            int cols = ScoutManager.GridLines(ai.View.MapWidth, ai.Rules.ScoutGrid), rows = ScoutManager.GridLines(ai.View.MapHeight, ai.Rules.ScoutGrid);
            for (int k = 0; k < ai.View.StartCount; k++)
            {
                Vector2 c = ai.CellCentre(ai.View.StartCell(k)), p = MilitaryManager.HuntPoint(ai, cols, rows, MilitaryManager.StartProxy(ai, cols, rows, c));
                float d = Vector2.Distance(c, p);
                _out.WriteLine($"{map} ({ai.View.MapWidth} cells) start {k} at {c}: proxy {p}, {d:F1} m");
                if (d > sight && ai.View.StartCount > 2) bad.Add($"{map} start {k}: {d:F1} m");
            }
        }
        Assert.True(bad.Count == 0, "proxies past 14 m sight: " + string.Join("; ", bad));
    }

    /// <summary>
    /// A lone worker sealed in a corner pocket (a ring of blocked cells 3 out, so no unit reaches it but sight does) on the
    /// second hunt: the sweep's ring leg must still bring it into sight within 20,000 ticks.
    /// </summary>
    [Fact]
    public void ALoneWorker_SealedInACornerPocket_IsSeenOnTheSecondHunt()
    {
        const int px = 251, py = 251, ring = 3;
        Hide h = RunHide(new Vector2((px + 0.5f) * MapConstants.CellSize, (py + 0.5f) * MapConstants.CellSize), 1, 20_000, block: w =>
        {
            for (int y = py - ring; y <= py + ring; y++)
                for (int x = px - ring; x <= px + ring; x++)
                    if (Math.Max(Math.Abs(x - px), Math.Abs(y - py)) == ring) w.NavGrid.SetResource(x, y, 1, 1);
        });
        _out.WriteLine($"sealed corner: {Show(h)}");
        Assert.True(h.SeenAfter >= 0, "the sealed worker was never seen on the second hunt in 20,000 ticks");
    }

    /// <summary>
    /// A lone worker walking back and forth between the two free starts (a Move to the other each time it stops) on the second
    /// hunt, whose first two targets are exactly those starts: it must be seen within 20,000 ticks (it must not slip between
    /// the two proxies for ever).
    /// </summary>
    [Fact]
    public void ALoneWorker_WalkingBetweenTheFreeStarts_IsSeenOnTheSecondHunt()
    {
        Vector2 s2 = StartCentre(2), s3 = StartCentre(3);
        bool toward3 = true;
        Hide h = RunHide(s2, 1, 20_000, perTick: x =>
        {
            World w = x.Sim.World;
            if (!w.Units.IsAlive(x.Worker) || w.Units.State[x.Worker.Index] != UnitState.Idle) return;
            x.Sim.Enqueue(Command.Move(1, x.Worker, toward3 ? s3 : s2));
            toward3 = !toward3;
        });
        _out.WriteLine($"walking between starts 2 and 3: {Show(h)}");
        Assert.True(h.SeenAfter >= 0, "the walking worker was never seen on the second hunt in 20,000 ticks");
    }

    /// <summary>Constraint "no per-think allocation": the new sweep code (the starts first, their proxies) allocates nothing.</summary>
    [Fact]
    public void TheHuntsStartCode_AllocatesNothing()
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), s_quiet.Value,
            map: LocalMovementTests.Flat(256).WithStartLocations(Starts512));
        AiPlayer ai = sim.AiOf(0)!;
        sim.Tick();
        ai.Board.HuntsStarted = 2;
        int cols = ScoutManager.GridLines(256, ai.Rules.ScoutGrid);
        Vector2 from = new(40f, 60f);
        AllocationProbe.AssertZero(() => MilitaryManager.NextHuntStart(ai, from, -1), _out);
        AllocationProbe.AssertZero(() => MilitaryManager.StartProxy(ai, cols, cols, StartCentre(2)), _out);
        AllocationProbe.AssertZero(() => MilitaryManager.NextSweep(ai, from, -1), _out);
    }

    /// <summary>
    /// QA focus "any new per-think work under 1 ms": a sweep re-target with the starts first (<see cref="MilitaryManager.NextSweep"/>
    /// with <c>HuntsStarted</c> 2, both free starts' proxies unvisited, so <see cref="MilitaryManager.NextHuntStart"/> walks
    /// every start's <see cref="MilitaryManager.StartProxy"/> pass) on the 512 m four-start map. A seat re-targets at most once
    /// a think, so eight seats doing it at once cost 8 calls: each must stay under 50 us (0.4 ms for eight, inside the 1 ms
    /// think budget with the rest). Rule 42: alone, after 30 s idle.
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void ASweepRetargetWithTheStartsFirst_CostsUnder50Us()
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), s_quiet.Value,
            map: LocalMovementTests.Flat(256).WithStartLocations(Starts512));
        AiPlayer ai = sim.AiOf(0)!;
        sim.Tick();
        ai.Board.HuntsStarted = 2;
        Vector2 from = new(40f, 60f);
        int sink = 0;
        for (int k = 0; k < 2000; k++) sink += MilitaryManager.NextSweep(ai, from, -1); // warm-up
        const int calls = 20_000;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int k = 0; k < calls; k++) sink += MilitaryManager.NextSweep(ai, from, -1);
        sw.Stop();
        double us = sw.Elapsed.TotalMilliseconds * 1000.0 / calls;
        _out.WriteLine($"NextSweep with the starts first: {us:F2} us a call ({ai.Board.HuntVisited.Length} points, {ai.View.StartCount} starts; sink {sink})");
        Assert.True(us < 50.0, $"a sweep re-target costs {us:F1} us");
    }

    // ---------------------------------------------------------------- the house rule under siege

    /// <summary>The Whirlwind seat's order: <c>tents</c> tents at times 0..n-1 (every opening), nothing else.</summary>
    private static GameData TentData(int tents) => AiScenes.DataWithAi("whirlwind", root =>
    {
        var steps = new (string, double, string, string?, int)[tents];
        for (int k = 0; k < tents; k++) steps[k] = ("time", k, "build", "whirlwind_tent", 0);
        foreach (JsonNode? o in root["openings"]!.AsArray())
        {
            o!["buildOrder"] = AiScenes.BuildOrder(steps);
            o["attack"] = new JsonObject { ["minArmyPop"] = 200, ["minArmyCost"] = 0 };
        }
    });

    private static readonly Lazy<GameData> s_tents10 = new(() => TentData(10));
    private static readonly Lazy<GameData> s_tents2 = new(() => TentData(2));

    /// <summary>Player 0: the Whirlwind AI (faction 1); player 1: a Malazan human seat. Ticks until every tent step is done and every tent stands finished.</summary>
    private static (Simulation Sim, int Tent) TentScene(GameData data, int tents)
    {
        PlayerSeat[] seats = { new(1, SeatController.Ai, AiDifficulty.Normal), new(0, SeatController.Human, AiDifficulty.Normal) };
        Simulation sim = AiScenes.Flat(seats, data);
        BuildMaps.Give(sim, 0, 0, 5000); // wood for tents, no gold: no worker trained, the population stays put
        AiPlayer ai = sim.AiOf(0)!;
        int tent = data.FindBuilding("whirlwind_tent");
        for (int t = 0; t < 20_000 && !(AllDone(ai, tents) && Finished(sim.World, tent) == tents); t++) sim.Tick();
        Assert.True(AllDone(ai, tents), "the tent steps never all finished");
        Assert.Equal(tents, Finished(sim.World, tent));
        return (sim, tent);
    }

    private static bool AllDone(AiPlayer ai, int n)
    {
        for (int i = 0; i < n; i++) if (!ai.Board.StepDone[i]) return false;
        return true;
    }

    private static int Finished(World w, int type)
    {
        int n = 0;
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 0 && w.Buildings.TypeId[k] == type && !w.Buildings.UnderConstruction[k]) n++;
        return n;
    }

    private static EntityHandle OneOf(World w, int type)
    {
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 0 && w.Buildings.TypeId[k] == type) return new EntityHandle(k, w.Buildings.Generation[k]);
        return default;
    }

    private static void KillWorkersDownTo(Simulation sim, int keep)
    {
        World w = sim.World;
        int n = 0;
        for (int i = 0; i < w.Units.Capacity; i++)
        {
            if (!w.Units.Alive[i] || w.Units.Owner[i] != 0 || !w.Data.Units[w.Units.TypeId[i]].IsWorker) continue;
            if (++n > keep) CombatSystem.HitUnit(w, new PendingHit(default, 1, new EntityHandle(i, w.Units.Generation[i]), false, w.Units.Hp[i] + 1));
        }
    }

    /// <summary>
    /// QA focus "the house rule under siege": a beaten Whirlwind seat with 3 workers and 10 tents (population 3 of 90) loses a
    /// tent every think until one is left. No tent step reopens and no tent Build is sent: it never needs one.
    /// </summary>
    [Fact]
    public void ABeatenSeat_ThreeWorkersTenTents_RebuildsNoTentUnderSiege()
    {
        (Simulation sim, int tent) = TentScene(s_tents10.Value, 10);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        KillWorkersDownTo(sim, 3);
        List<Command> log = AiScenes.Log(sim, 0);
        NextThink(sim);
        int razedFrom = sim.TickNumber;
        for (int k = 0; k < 9; k++)
        {
            Assert.True(w.Buildings.Free(OneOf(w, tent)));
            NextThink(sim);
            Assert.True(AllDone(ai, 10), $"a tent step reopened after raze {k + 1} (half-pop {ai.View.HalfPop} of {ai.View.HalfPopCap})");
        }
        for (int k = 0; k < 5; k++) NextThink(sim);
        _out.WriteLine($"tents left {Finished(w, tent)}, half-pop {ai.View.HalfPop} of {ai.View.HalfPopCap}, tent Builds since the first raze {log.Count(c => c.Tick >= razedFrom && c.Kind == CommandKind.Build && c.TypeId == tent)}");
        Assert.DoesNotContain(log, c => c.Tick >= razedFrom && c.Kind == CommandKind.Build && c.TypeId == tent);
    }

    /// <summary>
    /// The siege's control and the margin's boundary: 2 tents (cap 26 pop, 52 half-pop), workers placed (no tick between, so
    /// the economy's own house rule can't build first) until the population is exactly <paramref name="belowCapAfterRaze"/>
    /// half-pop under the cap one raze leaves (36), then one tent razed. Over the new cap (-16: the seat was full), at it (0)
    /// and at the margin (<c>ai.houseWhenPopWithin</c> 2 pop = 4 half-pop) a tent goes up again within 5 thinks; one step
    /// past the margin (6) none does.
    /// </summary>
    [Theory]
    [InlineData(-16, true)]
    [InlineData(0, true)]
    [InlineData(4, true)]
    [InlineData(6, false)]
    public void ASeatAtOrNearItsCap_RebuildsTheRazedTent(int belowCapAfterRaze, bool rebuilds)
    {
        (Simulation sim, int tent) = TentScene(s_tents2.Value, 2);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        Assert.Equal(4, ai.Rules.HouseHalfPopMargin);
        int worker = TestSim.Data.FindUnit("whirlwind_camp_follower"), placed = 0;
        int capAfter = w.HalfPopCap[0] - TestSim.Data.Buildings[tent].HalfPopProvided, halfPop = ai.View.HalfPop;
        int perWorker = TestSim.Data.Units[worker].HalfPop;
        while (halfPop + placed * perWorker < capAfter - belowCapAfterRaze)
        {
            CombatScenes.Place(sim, 0, worker, w.NavGrid.CellCenter(4 + placed % 8, 44 + placed / 8));
            placed++;
        }
        List<Command> log = AiScenes.Log(sim, 0);
        Assert.True(w.Buildings.Free(OneOf(w, tent)));
        int razed = sim.TickNumber;
        NextThink(sim);
        bool reopened = !AllDone(ai, 2);
        Assert.Equal(capAfter - belowCapAfterRaze, ai.View.HalfPop);
        Assert.Equal(capAfter, ai.View.HalfPopCap);
        for (int k = 0; k < 5; k++) NextThink(sim);
        int tents = 0;
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 0 && w.Buildings.TypeId[k] == tent) tents++;
        _out.WriteLine($"below the new cap by {belowCapAfterRaze}: half-pop {ai.View.HalfPop} of {ai.View.HalfPopCap}; step reopened at the first think {reopened}; tents (sites too) after 6 thinks {tents}, tent Builds {log.Count(c => c.Tick > razed && c.Kind == CommandKind.Build && c.TypeId == tent)}");
        Assert.Equal(rebuilds, reopened);
        if (rebuilds) Assert.True(tents >= 2, $"no tent rebuilt ({tents})");
        else Assert.Equal(1, tents);
    }

    /// <summary>
    /// Since the house rule, a House step is reopened exactly when the economy's own house rule (<c>EconomyManager.House</c>,
    /// the same test) builds a house too, and both Builds go out at the same think: one razed tent at the cap comes back as
    /// two. Asserts one tent is rebuilt for one lost.
    /// </summary>
    [Fact(Skip = "BUG-1161 item 3 (open, S4): a reopened House step and the economy's house rule both build at the same think: two tents for one lost")]
    public void ARazedTent_AtTheCap_IsRebuiltOnce()
    {
        (Simulation sim, int tent) = TentScene(s_tents2.Value, 2);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        int worker = TestSim.Data.FindUnit("whirlwind_camp_follower"), placed = 0;
        int capAfter = w.HalfPopCap[0] - TestSim.Data.Buildings[tent].HalfPopProvided, halfPop = ai.View.HalfPop;
        while (halfPop + placed * TestSim.Data.Units[worker].HalfPop < capAfter)
            CombatScenes.Place(sim, 0, worker, w.NavGrid.CellCenter(4 + placed % 8, 44 + placed++ / 8));
        Assert.True(w.Buildings.Free(OneOf(w, tent)));
        for (int k = 0; k < 6; k++) NextThink(sim);
        int tents = 0;
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 0 && w.Buildings.TypeId[k] == tent) tents++;
        Assert.Equal(2, tents);
    }

    // ---------------------------------------------------------------- BUG-1158: the leg-death cap

    private static readonly (int X, int Y)[] Starts64 = { (10, 32), (54, 32), (32, 56), (32, 8) };

    private static readonly Lazy<GameData> s_far = new(() => AiScenes.DataWith(dir =>
    {
        foreach (string f in new[] { "malazan", "whirlwind" })
            dir.EditJson($"factions/{f}/ai.json", root =>
            {
                foreach (JsonNode? o in root["openings"]!.AsArray())
                    o!["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "build", f == "malazan" ? "malazan_billet" : "whirlwind_tent", 0));
            });
        dir.EditJson("common/rules.json", r => r["ai"]!["starvedWaveAfter"] = 0);
    }));

    /// <summary>The developer's far scene (64 flat, four starts, every gold mine emptied, no far mine): the seat arms and walks its leg.</summary>
    private static Simulation FarScene()
    {
        GameData data = s_far.Value;
        PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Human, AiDifficulty.Normal) };
        var starts = new StartLocation[Starts64.Length];
        for (int k = 0; k < starts.Length; k++) starts[k] = new StartLocation(Starts64[k].Y * AiScenes.FlatSize + Starts64[k].X, 0);
        Simulation sim = AiScenes.Flat(seats, data, map: LocalMovementTests.Flat(AiScenes.FlatSize).WithStartLocations(starts), explored: false);
        World w = sim.World;
        for (int i = 0; i < w.Resources.Capacity; i++)
            if (w.Resources.Alive[i] && data.Resources[w.Resources.TypeId[i]].Resource == ResourceKind.Gold)
                w.Resources.Take(w.Resources.HandleOf(i), int.MaxValue);
        w.Ledger.Gold[0] = 0;
        w.Ledger.Wood[0] = 200;
        w.Ledger.Gold[1] = 0;
        w.Ledger.Wood[1] = 200;
        sim.AiOf(0)!.Board.ScoutPhase = 2;
        return sim;
    }

    private static int Workers(Simulation sim)
    {
        int n = 0;
        for (int i = 0; i < sim.World.Units.Capacity; i++)
            if (sim.World.Units.Alive[i] && sim.World.Units.Owner[i] == 0 && sim.World.Data.Units[sim.World.Units.TypeId[i]].IsWorker) n++;
        return n;
    }

    private static void Kill(World w, EntityHandle h) => CombatSystem.HitUnit(w, new PendingHit(default, 1, h, false, w.Units.Hp[h.Index] + 1));

    private static EntityHandle Leg(AiPlayer ai) => new(ai.Board.FarScoutSlot, ai.Board.FarScoutGeneration);

    private static int FreeStarts => (1 << 2) | (1 << 3);

    /// <summary>
    /// The cap's boundary: exactly <c>ai.scoutReplacements</c> (2) leg workers killed, each as soon as it holds the leg; the
    /// next one is still sent and walks the leg to its end (both free starts marked looked), alive.
    /// </summary>
    [Fact]
    public void ExactlyScoutReplacementsLegDeaths_TheNextWorkerStillWalksTheLeg()
    {
        Simulation sim = FarScene();
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        int cap = ai.Rules.ScoutReplacements, killed = 0, workers = Workers(sim);
        EntityHandle last = default;
        while (sim.TickNumber < 8000 && ai.Board.FarLooked != FreeStarts)
        {
            sim.Tick();
            if (ai.Board.FarScoutSlot < 0 || !w.Units.IsAlive(Leg(ai))) continue;
            last = Leg(ai);
            if (killed < cap) { Kill(w, last); killed++; }
        }
        _out.WriteLine($"killed {killed} (cap {cap}), losses {ai.View.OwnUnitLosses}, looked {ai.Board.FarLooked}, start {ai.Board.FarStart}, phase {ai.Board.FarPhase}, workers {Workers(sim)} of {workers}, at {sim.TickNumber}");
        Assert.Equal(cap, killed);
        Assert.Equal(FreeStarts, ai.Board.FarLooked);
        Assert.True(w.Units.IsAlive(last), "the third leg worker died");
        Assert.Equal(workers - cap, Workers(sim));
    }

    /// <summary>
    /// The shipped rule reads the match's own unit losses, not leg deaths (the developer's call, docs/03 "Implementation
    /// (M5-H6)"): a seat that has lost 3 units elsewhere before it arms gives up the leg at its first leg death (every start
    /// marked looked, no replacement), though the brief's cap ("replaced at most <c>scoutReplacements</c> times in all") would
    /// send two more. Pins the shipped behaviour so a change to it is seen.
    /// </summary>
    [Fact]
    public void AfterThreeOtherLosses_TheFirstLegDeath_EndsTheLegForTheMatch()
    {
        Simulation sim = FarScene();
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        for (int k = 0; k < 3; k++) Kill(w, CombatScenes.Place(sim, 0, Heavy, w.NavGrid.CellCenter(4 + k, 40)));
        sim.Tick();
        Assert.Equal(3, ai.View.OwnUnitLosses);
        int killed = 0, legs = 0, workers = Workers(sim);
        EntityHandle seen = default;
        while (sim.TickNumber < 6000)
        {
            sim.Tick();
            if (ai.Board.FarScoutSlot < 0 || !w.Units.IsAlive(Leg(ai))) continue;
            if (Leg(ai) != seen) { legs++; seen = Leg(ai); }
            if (killed == 0) { Kill(w, seen); killed++; }
        }
        _out.WriteLine($"leg workers {legs}, killed {killed}, losses {ai.View.OwnUnitLosses}, looked {ai.Board.FarLooked}, start {ai.Board.FarStart}, phase {ai.Board.FarPhase}, workers {Workers(sim)} of {workers}");
        Assert.Equal(1, legs);
        Assert.Equal((1 << 4) - 1, ai.Board.FarLooked);
        Assert.Equal(-1, ai.Board.FarStart);
        Assert.Equal(FarExpansion.Armed, ai.Board.FarPhase);
        // Every start marked by the cap (the live players' too) is a valid save: it loads and follows.
        Simulation load = SaveTestRun.Load(s_far.Value, SaveTestRun.Save(sim));
        SaveTestRun.AssertSameState(sim, load, "after the cap");
        for (int t = 0; t < 400; t++) { sim.Tick(); load.Tick(); }
        Assert.Equal(sim.StateHash(), load.StateHash());
    }

    /// <summary>
    /// QA focus "a death on the tick the start is marked looked": the leg worker killed right after the think that marks its
    /// first start (<paramref name="afterTheMark"/>), or on the tick it comes within <c>ai.scoutReach</c> of the start's last
    /// look point, before the think that would mark it. Either way one worker is lost, its replacement walks on, and the leg
    /// ends with both free starts looked at; nothing throws.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ALegDeath_OnTheTickTheStartIsMarked_TheLegStillFinishes(bool afterTheMark)
    {
        Simulation sim = FarScene();
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        int killed = 0, killedAt = -1, workers = Workers(sim), lookedBefore = 0;
        float reach = ai.Rules.ScoutReach;
        while (sim.TickNumber < 10_000 && ai.Board.FarLooked != FreeStarts)
        {
            sim.Tick();
            if (killed > 0 || ai.Board.FarScoutSlot < 0 || !w.Units.IsAlive(Leg(ai))) { lookedBefore = ai.Board.FarLooked; continue; }
            bool hit = afterTheMark
                ? ai.Board.FarLooked != lookedBefore
                : ai.Board.FarStart >= 0 && ai.Board.FarLook == FarExpansion.LookPoints - 1 && ai.Board.FarMoved
                  && Vector2.Distance(w.Units.Position[ai.Board.FarScoutSlot], FarExpansion.LookPoint(ai, ai.Board.FarStart, ai.Board.FarLook)) <= reach;
            lookedBefore = ai.Board.FarLooked;
            if (!hit) continue;
            Kill(w, Leg(ai));
            killed++;
            killedAt = sim.TickNumber;
        }
        _out.WriteLine($"after the mark {afterTheMark}: killed at {killedAt}, looked {ai.Board.FarLooked} at {sim.TickNumber}, losses {ai.View.OwnUnitLosses}, workers {Workers(sim)} of {workers}");
        Assert.Equal(1, killed);
        Assert.Equal(FreeStarts, ai.Board.FarLooked);
        Assert.Equal(workers - 1, Workers(sim));
    }

    private const string LegChild = "RTS_QA_M5H6_LEG_CHILD", LegMarker = "M5H6-LEG ";

    private static string FollowLeg(Simulation sim)
    {
        AiPlayer ai = sim.AiOf(0)!;
        var checkpoints = new List<string>();
        while (sim.TickNumber < 9000)
        {
            sim.Tick();
            if (sim.TickNumber % 1000 == 0) checkpoints.Add($"{sim.TickNumber}:{sim.StateHash():X16}");
        }
        return $"looked {ai.Board.FarLooked} start {ai.Board.FarStart} look {ai.Board.FarLook} slot {ai.Board.FarScoutSlot} phase {ai.Board.FarPhase} losses {ai.View.OwnUnitLosses} | {string.Join(" ", checkpoints)}";
    }

    /// <summary>
    /// QA focus "a mid-leg save after a death in a child process": the first leg worker killed; a save taken while its
    /// replacement walks (between thinks), loaded in a second process, follows the uninterrupted run (state hash every 1,000
    /// ticks to 9,000 and the far fields at the end).
    /// </summary>
    [Fact]
    public void ASaveMidLeg_AfterALegDeath_LoadedInAChildProcess_FollowsTheRun()
    {
        string? path = Environment.GetEnvironmentVariable(LegChild);
        if (path != null)
        {
            _out.WriteLine(LegMarker + FollowLeg(SaveTestRun.Load(s_far.Value, File.ReadAllBytes(path))));
            return;
        }
        Simulation sim = FarScene();
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        EntityHandle first = default;
        while (sim.TickNumber < 6000)
        {
            sim.Tick();
            if (ai.Board.FarScoutSlot < 0 || !w.Units.IsAlive(Leg(ai))) continue;
            if (first == default) { first = Leg(ai); Kill(w, first); continue; }
            if (Leg(ai) != first && ai.Board.FarMoved && sim.TickNumber % ai.ThinkTicks == 5) break;
        }
        Assert.Equal(1, ai.View.OwnUnitLosses);
        Assert.True(ai.Board.FarScoutSlot >= 0 && ai.Board.FarMoved, "no replacement on its way");
        byte[] bytes = SaveTestRun.Save(sim);
        Simulation local = SaveTestRun.Load(s_far.Value, bytes);
        SaveTestRun.AssertSameState(sim, local, "loaded mid-leg");
        string file = Path.Combine(Path.GetTempPath(), $"rts-qa-m5h6-leg-{Environment.ProcessId}.sav");
        File.WriteAllBytes(file, bytes);
        try
        {
            string mine = FollowLeg(sim);
            _out.WriteLine($"saved at {bytes.Length} bytes; this process: {mine}");
            string child = MapFileTests.RunChild(typeof(EndGameH6QaTests), nameof(ASaveMidLeg_AfterALegDeath_LoadedInAChildProcess_FollowsTheRun),
                LegChild, LegMarker, seconds: 300, value: file);
            _out.WriteLine($"the child: {child}");
            Assert.Equal(mine, child);
        }
        finally
        {
            File.Delete(file);
        }
    }

    // ---------------------------------------------------------------- BUG-1159 item 1: the reach filter's edge

    /// <summary>
    /// The reach filter compares the start's <b>centre</b> with <c>ai.farExpandReach</c>, but <see cref="FarExpansion.Candidate"/>
    /// compares the <b>mine's</b> centre, and a start's mines lie up to <c>StartMineDistance</c> (18 m) from its centre. A free
    /// start 2 whose centre is just past the reach while its mine (6 cells toward the hall) is inside it: the mine is a valid
    /// candidate once seen, so the leg should walk there (before M5-H6 it did) and the seat expand.
    /// </summary>
    [Fact(Skip = "BUG-1161 item 1 (open, S4): NextStart filters on the start's centre, so a start whose mine lies inside farExpandReach but whose centre lies outside is never walked to")]
    public void AStartJustPastTheReach_WithItsMineInside_IsStillWalkedTo()
    {
        Simulation probe = FarScene();
        AiPlayer pa = probe.AiOf(0)!;
        probe.Tick();
        // The mine: 6 cells north of start 2's centre (32, 56), toward the hall.
        const int mx = 31, my = 49;
        Vector2 hall = pa.HallPoint, s2 = pa.CellCentre(pa.View.StartCell(2)), s3 = pa.CellCentre(pa.View.StartCell(3));
        int mine = s_far.Value.FindResource("gold_mine");
        ResourceDef def = s_far.Value.Resources[mine];
        Vector2 mc = pa.NodeCentre(mine, my * AiScenes.FlatSize + mx);
        float reach = (Vector2.Distance(hall, mc) + Vector2.Distance(hall, s2)) * 0.5f;
        _out.WriteLine($"hall {hall}; start 2 at {Vector2.Distance(hall, s2):F1} m, its mine at {Vector2.Distance(hall, mc):F1} m (footprint {def.FootprintWidth}x{def.FootprintHeight}), start 3 at {Vector2.Distance(hall, s3):F1} m; reach {reach:F1}");
        Assert.True(Vector2.Distance(hall, s3) > reach, "the scene: start 3 must be outside the reach too");
        GameData data = AiScenes.DataWith(dir =>
        {
            foreach (string f in new[] { "malazan", "whirlwind" })
                dir.EditJson($"factions/{f}/ai.json", root =>
                {
                    foreach (JsonNode? o in root["openings"]!.AsArray())
                        o!["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "build", f == "malazan" ? "malazan_billet" : "whirlwind_tent", 0));
                });
            dir.EditJson("common/rules.json", r => { r["ai"]!["starvedWaveAfter"] = 0; r["ai"]!["farExpandReach"] = Math.Round(reach, 1); });
        });
        PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Human, AiDifficulty.Normal) };
        var starts = new StartLocation[Starts64.Length];
        for (int k = 0; k < starts.Length; k++) starts[k] = new StartLocation(Starts64[k].Y * AiScenes.FlatSize + Starts64[k].X, 0);
        Simulation sim = AiScenes.Flat(seats, data, map: LocalMovementTests.Flat(AiScenes.FlatSize).WithStartLocations(starts), explored: false);
        World w = sim.World;
        for (int i = 0; i < w.Resources.Capacity; i++)
            if (w.Resources.Alive[i] && data.Resources[w.Resources.TypeId[i]].Resource == ResourceKind.Gold)
                w.Resources.Take(w.Resources.HandleOf(i), int.MaxValue);
        w.Ledger.Gold[0] = 0;
        w.Ledger.Wood[0] = 200;
        Assert.True(w.Resources.Spawn(mine, my * AiScenes.FlatSize + mx, data.Rules.ExpansionMineGold, out _));
        AiPlayer ai = sim.AiOf(0)!;
        ai.Board.ScoutPhase = 2;
        int legAt = -1;
        while (sim.TickNumber < 6000 && ai.Board.FarExpansions == 0)
        {
            sim.Tick();
            if (legAt < 0 && ai.Board.FarStart >= 0) legAt = sim.TickNumber;
        }
        _out.WriteLine($"armed {ai.Board.FarPhase}, first leg at {legAt}, expansions {ai.Board.FarExpansions}, next start {FarExpansion.NextStart(ai)}");
        Assert.True(ai.Board.FarExpansions >= 1, "the seat never walked to the start whose mine lies inside the reach, so never expanded");
    }
}
