using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Tests.Save;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M5-4g, session 2026-10-18-1814): the build order's reserve attacked from the sides the developer's
/// <c>Ai/ReserveTests</c> doesn't take. Starvation the other way (no known wood; a step that can never be placed: a full
/// building store, no site in the search radius): production must keep training. A save taken while a reserve is held
/// (one tick after the think that named it, one tick before the next, on the think tick itself): the loaded twin sends
/// the same AI commands every tick and names the same reserve at every think. A save / load of a reserve-holding seat is
/// field-equal (nothing stored). A 4-seat 256 map where every step of every seat is due and unaffordable: a think
/// allocates nothing and averages under 1 ms.
/// </summary>
public class ReserveQaTests
{
    private readonly ITestOutputHelper _out;

    public ReserveQaTests(ITestOutputHelper output) => _out = output;

    /// <summary>Shipped data, every Malazan opening given <paramref name="order"/> and the composition <paramref name="unit"/> alone; the scout home; optional rules.json ai edits.</summary>
    private static GameData Data(JsonArray order, string unit, Action<JsonNode>? rulesAi = null) => AiScenes.DataWith(dir =>
    {
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            foreach (JsonNode? o in root["openings"]!.AsArray())
            {
                o!["buildOrder"] = order.DeepClone();
                o["compositions"] = JsonNode.Parse($$"""[{ "age": 1, "units": { "{{unit}}": 1 } }, { "age": 2, "units": { "{{unit}}": 1 } }]""");
            }
        });
        dir.EditJson("common/rules.json", r =>
        {
            r["ai"]!["scoutAfter"] = 3600;
            r["ai"]!["reserveArmyShare"] = 0; // M5-4g fix round 1 (BUG-1021): no army in these scenes, the reserve named from the start
            rulesAi?.Invoke(r["ai"]!);
        });
    });

    /// <summary>The flat AI scene (Hard Malazan seat 0 v a human seat) plus a finished Barracks and Crossbow Range for player 0.</summary>
    private static Simulation Scene(GameData data, int workers = 5)
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Human), data, workers: workers);
        Assert.True(sim.World.Buildings.Spawn(0, data.FindBuilding("malazan_barracks"), 44 * AiScenes.FlatSize + 8, out _));
        Assert.True(sim.World.Buildings.Spawn(0, data.FindBuilding("malazan_crossbow_range"), 44 * AiScenes.FlatSize + 16, out _));
        return sim;
    }

    private static readonly Lazy<GameData> s_ageTwo = new(() => Data(AiScenes.BuildOrder(("time", 0, "research", "age_ii", 0)), "malazan_heavy_infantry"));

    // ---------- starvation the other way ----------

    /// <summary>
    /// The no-node guard's wood twin (the developer's row drops gold): every tree marked dropped, Age II (400 / 200) due, the
    /// purse 500 / 190 (short of wood only). Since M5-4g fix round 1 (BUG-1023 item 2) a step short of a kind no gatherer can
    /// reach is not reserved at all (it would bank its gold for nothing), so a Heavy Infantry (54 / 20) trains; with the
    /// trees known, Age II holds 400 / 200 and 170 &lt; 200 wood blocks it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NoKnownWood_WoodDropsOutOfTheReserve_ProductionTrainsAboveTheGoldPart(bool woodGone)
    {
        GameData d = s_ageTwo.Value;
        Simulation sim = Scene(d);
        AiPlayer ai = sim.AiOf(0)!;
        ResourceStore r = sim.World.Resources;
        int trees = 0;
        for (int s = 0; s < r.Capacity && woodGone; s++)
            if (r.Alive[s] && d.Resources[r.TypeId[s]].Resource == ResourceKind.Wood)
            {
                ai.Board.NodeDropped[s] = r.Generation[s];
                trees++;
            }
        Assert.True(!woodGone || trees > 0);
        List<Command> log = AiScenes.Log(sim, 0);
        (int step, int gold, int wood, bool shortGold, bool shortWood, float share) seen = default;
        ai.ThoughtForTests = a => seen = (a.ReserveStep, a.ReserveGold, a.ReserveWood, a.ReserveShortGold, a.ReserveShortWood, a.WantedGoldShare());
        while (sim.TickNumber % ai.ThinkTicks != 0) sim.Tick();
        BuildMaps.SetTotals(sim, 0, 500, 190);
        sim.Tick();
        Assert.Equal(woodGone ? -1 : 0, seen.step);
        Assert.Equal(woodGone ? (0, 0) : (400, 200), (seen.gold, seen.wood));
        Assert.Equal((false, !woodGone), (seen.shortGold, seen.shortWood));
        Assert.Equal(woodGone ? d.AiRules.GoldShare : d.AiRules.GoldShare - d.AiRules.ReserveShift, seen.share, 5); // a guarded kind is never "short"
        Assert.Equal(woodGone, log.Any(c => c.Kind == CommandKind.Train && c.TypeId == d.FindUnit("malazan_heavy_infantry")));
    }

    /// <summary>A flat scene like <see cref="Scene"/> with the building store <paramref name="capacity"/> slots (full at 10: player 0's hall, Barracks, Range, six Billets, player 1's hall).</summary>
    private static Simulation SceneWithStore(GameData data, int capacity, int workers = 5)
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Human);
        var config = new SimConfig(3, 2, 256, 512) { Data = data, Seats = seats.ToImmutableArray(), Combat = false, BuildingCapacity = capacity };
        var sim = new Simulation(config, LocalMovementTests.Flat(AiScenes.FlatSize));
        World w = sim.World;
        int mine = data.FindResource("gold_mine"), tree = data.FindResource("tree");
        for (int p = 0; p < 2; p++)
        {
            (int hx, int hy) = AiScenes.HallAt(p);
            Assert.True(w.Buildings.Spawn(p, StartBase.BuildingOfSlot(data, seats[p].Faction, BuildingSlot.TownHall), hy * AiScenes.FlatSize + hx, out _));
            int toward = p % 2 == 0 ? 1 : -1;
            for (int m = 0; m < 2; m++) Assert.True(w.Resources.Spawn(mine, (hy - 6) * AiScenes.FlatSize + hx + toward * 2 + 3 * m * toward, data.Rules.StartMineGold, out _));
            for (int t = 0; t < 12; t++) Assert.True(w.Resources.Spawn(tree, (hy + 9 + t / 4) * AiScenes.FlatSize + hx + t % 4, data.Rules.TreeWood, out _));
            int worker = StartBase.UnitOfSlot(data, seats[p].Faction, UnitSlot.Worker);
            for (int k = 0; k < (p == 0 ? workers : 1); k++) CombatScenes.Place(sim, p, worker, w.NavGrid.CellCenter(hx + k % 4, hy + 5 + k / 4));
        }
        Assert.True(w.Buildings.Spawn(0, data.FindBuilding("malazan_barracks"), 44 * AiScenes.FlatSize + 8, out _));
        Assert.True(w.Buildings.Spawn(0, data.FindBuilding("malazan_crossbow_range"), 44 * AiScenes.FlatSize + 16, out _));
        AddBillets(sim, data);
        return TestSim.Explored(sim);
    }

    /// <summary>Six finished Billets for player 0 along the south edge, so the population cap is not what stops production in the starvation rows.</summary>
    private static void AddBillets(Simulation sim, GameData data)
    {
        for (int k = 0; k < 6; k++) Assert.True(sim.World.Buildings.Spawn(0, data.FindBuilding("malazan_billet"), 58 * AiScenes.FlatSize + 4 + 5 * k, out _));
    }

    /// <summary>Player 0's Train commands over <paramref name="ticks"/> ticks of the natural economy (the start purse, the workers gathering).</summary>
    private static (int Trains, int Builds) Play(Simulation sim, int ticks)
    {
        List<Command> log = AiScenes.Log(sim, 0);
        for (int t = 0; t < ticks; t++) sim.Tick();
        return (log.Count(c => c.Kind == CommandKind.Train), log.Count(c => c.Kind == CommandKind.Build));
    }

    /// <summary>
    /// A step that can never be placed because the building store is full (the cost is checked before the store, so it is
    /// reserved while unpayable): over 8 minutes of the natural economy production still trains, no fewer units than the
    /// twin whose Armory step is never due, less a margin of 2 (the reserve banks one Armory's cost at most). Printed: both counts.
    /// </summary>
    [Fact]
    public void AFullBuildingStore_NeverBuildableStep_ProductionDoesNotFreeze()
    {
        GameData due = Data(AiScenes.BuildOrder(("time", 0, "build", "malazan_armory", 0)), "malazan_heavy_infantry");
        GameData never = Data(AiScenes.BuildOrder(("time", 3600, "build", "malazan_armory", 0)), "malazan_heavy_infantry");
        Simulation a = SceneWithStore(due, 10), b = SceneWithStore(never, 10);
        Assert.Equal(0, a.World.Buildings.FreeCount);
        int stepIndex = -1, held = 0, thinks = 0;
        a.AiOf(0)!.ThoughtForTests = x => { thinks++; if (x.ReserveStep >= 0) { held++; stepIndex = x.ReserveStep; } };
        (int trainsDue, int buildsDue) = Play(a, 8 * 60 * SimConstants.TicksPerSecond);
        (int trainsNever, _) = Play(b, 8 * 60 * SimConstants.TicksPerSecond);
        _out.WriteLine($"store full: trains with the Armory step due {trainsDue} (builds sent {buildsDue}, reserve held {held} / {thinks} thinks, step {stepIndex}); never due {trainsNever}; pop {a.World.HalfPop[0]} / {a.World.HalfPopCap[0]} v {b.World.HalfPop[0]} / {b.World.HalfPopCap[0]}");
        Assert.True(trainsDue > 0, "production froze behind a step the full store never lets be placed");
        Assert.True(trainsDue >= trainsNever - 2, $"production starved: {trainsDue} trains v {trainsNever} without the step");
    }

    /// <summary>
    /// A step that never finds a site (the AI's site search cut to 4 m round the hall, inside its footprint): the step is
    /// reserved while unpayable, searches once payable, waits its no-site retry, and so on. Over 8 minutes production
    /// still trains, no fewer units than the never-due twin less 2. Printed: both counts and the reserve's share of thinks.
    /// </summary>
    [Fact]
    public void ANoSiteStep_ProductionDoesNotFreeze()
    {
        Action<JsonNode> cramped = ai => ai["placementSearchRadius"] = 4;
        GameData due = Data(AiScenes.BuildOrder(("time", 0, "build", "malazan_armory", 0)), "malazan_heavy_infantry", cramped);
        GameData never = Data(AiScenes.BuildOrder(("time", 3600, "build", "malazan_armory", 0)), "malazan_heavy_infantry", cramped);
        Simulation a = Scene(due), b = Scene(never);
        AddBillets(a, due);
        AddBillets(b, never);
        int held = 0, thinks = 0;
        a.AiOf(0)!.ThoughtForTests = x => { thinks++; if (x.ReserveStep >= 0) held++; };
        (int trainsDue, int buildsDue) = Play(a, 8 * 60 * SimConstants.TicksPerSecond);
        (int trainsNever, _) = Play(b, 8 * 60 * SimConstants.TicksPerSecond);
        _out.WriteLine($"no site: trains with the Armory step due {trainsDue} (builds sent {buildsDue}, reserve held {held} / {thinks} thinks); never due {trainsNever}; pop {a.World.HalfPop[0]} / {a.World.HalfPopCap[0]} v {b.World.HalfPop[0]} / {b.World.HalfPopCap[0]}; armories {a.World.Buildings.Count}");
        Assert.True(trainsDue > 0, "production froze behind a step that never finds a site");
        Assert.True(trainsDue >= trainsNever - 2, $"production starved: {trainsDue} trains v {trainsNever} without the step");
    }

    // ---------- save / load while a reserve is held ----------

    private static string Fmt(in Command c) =>
        $"{c.Kind} p{c.Player} t{c.Tick} s{c.Sequence} ty{c.TypeId} x{BitConverter.SingleToInt32Bits(c.Position.X):X8} " +
        $"y{BitConverter.SingleToInt32Bits(c.Position.Y):X8} u{c.Unit.Index}/{c.Unit.Generation} f{c.Flags} tg{c.Target.Index}/{c.Target.Generation} b{c.TargetIsBuilding}";

    /// <summary>Hooks every AI seat: commands into <paramref name="cmds"/>, each think's reserve into <paramref name="reserves"/>.</summary>
    private static void Hook(Simulation sim, List<string> cmds, List<string> reserves)
    {
        for (int p = 0; p < sim.World.Config.PlayerCount; p++)
            if (sim.AiOf(p) is { } ai)
            {
                ai.SentForTests = c => cmds.Add(Fmt(c));
                ai.ThoughtForTests = x => reserves.Add($"p{x.Player} t{x.Now} step {x.ReserveStep} {x.ReserveGold}/{x.ReserveWood} short {x.ReserveShortGold}/{x.ReserveShortWood} share {x.WantedGoldShare():R}");
            }
    }

    /// <summary>
    /// The menu map, Normal v Normal, a pairing's smallest seed: the reference runs 14,000 ticks (9,000 before M5-4g fix round
    /// 1: since BUG-1021 the reserve waits for a wave-sized army or the first wave, the first named at 6:00-6:10 on these seeds); at the first three thinks
    /// that named a reserve (at least 600 ticks apart) it is saved at the think tick (before the think), one tick after it
    /// and one tick before the next think. Each load is ticked beside the reference for 600 ticks: the AI commands
    /// (every field), the reserve each seat names at every think, and the hash, every tick; the seats field for field at the load and at the end.
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData(0, 0)]
    [InlineData(2, 1)]
    [InlineData(1, 2)]
    public void SavedWhileAReserveIsHeld_TheLoadedTwinSendsTheSameCommandsAndNamesTheSameReserve(int malazan, int whirlwind)
    {
        ulong seed = Rts.Sim.Tests.Content.AiOpeningSeeds.SmallestPair(malazan, whirlwind);
        GameData data = TestSim.Data;
        Simulation reference = Rts.Sim.Tests.Content.AiSpendingReportTests.MenuMap(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai), seed);
        var refCmds = new List<string>();
        var refRes = new List<string>();
        Hook(reference, refCmds, refRes);
        int think = reference.AiOf(0)!.ThinkTicks;
        var namedAt = new List<int>();
        var live = new List<(Simulation Sim, List<string> Cmds, List<string> Res, int SavedAt)>();
        int loads = 0, compared = 0, reservesCompared = 0;
        var pendingSaves = new SortedSet<int>();
        while (reference.TickNumber < 14000 || live.Count > 0)
        {
            int t = reference.TickNumber;
            if (pendingSaves.Remove(t))
            {
                byte[] bytes = SaveTestRun.Save(reference);
                Simulation loaded = SaveTestRun.Load(data, bytes);
                for (int p = 0; p < 2; p++) SaveTestRun.AssertSameAi(reference.AiOf(p), loaded.AiOf(p), $"seed {seed} saved at {t}: seat {p}");
                var c = new List<string>();
                var r = new List<string>();
                Hook(loaded, c, r);
                live.Add((loaded, c, r, t));
                loads++;
            }
            refCmds.Clear();
            refRes.Clear();
            reference.Tick();
            bool named = refRes.Any(x => !x.Contains("step -1", StringComparison.Ordinal));
            if (named && t < 13000 && (namedAt.Count == 0 || t - namedAt[^1] >= 600) && namedAt.Count < 3)
            {
                namedAt.Add(t);
                pendingSaves.Add(t + 1);
                pendingSaves.Add(t + think - 1);
                pendingSaves.Add(t + think);
            }
            ulong hash = reference.StateHash();
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var l = live[i];
                l.Sim.Tick();
                string at = $"seed {seed} saved at {l.SavedAt}, tick {reference.TickNumber}";
                Assert.True(l.Cmds.SequenceEqual(refCmds), $"{at}: AI commands differ\n  unsaved: {string.Join(" | ", refCmds)}\n  loaded:  {string.Join(" | ", l.Cmds)}");
                Assert.True(l.Res.SequenceEqual(refRes), $"{at}: reserves differ\n  unsaved: {string.Join(" | ", refRes)}\n  loaded:  {string.Join(" | ", l.Res)}");
                compared += refCmds.Count;
                reservesCompared += refRes.Count;
                l.Cmds.Clear();
                l.Res.Clear();
                Assert.True(l.Sim.StateHash() == hash, $"{at}: hash differs");
                if (reference.TickNumber == l.SavedAt + 600)
                {
                    for (int p = 0; p < 2; p++) SaveTestRun.AssertSameAi(reference.AiOf(p), l.Sim.AiOf(p), $"{at}: seat {p}");
                    SaveTestRun.AssertSameState(reference, l.Sim, at);
                    live.RemoveAt(i);
                }
            }
        }
        _out.WriteLine($"seed {seed}: reserves named at {string.Join(", ", namedAt)}; {loads} loads, {compared:N0} AI commands and {reservesCompared:N0} think reserves compared");
        Assert.Equal(3, namedAt.Count);
        Assert.Equal(9, loads);
        Assert.True(reservesCompared > 0);
    }

    // ---------- the 4-seat 256 think with every step due and unaffordable ----------

    /// <summary>
    /// Shipped data with every opening of both factions given one long build order, all due at 0 with no slack (the second expand at 1 s): a research of
    /// every faction tech, a build of every faction building, two expands, a train of every faction unit; expandAfter 0.
    /// </summary>
    private static readonly Lazy<GameData> s_everyStep = new(() =>
    {
        GameData shipped = TestSim.Data;
        return AiScenes.DataWith(dir =>
        {
            foreach (string f in new[] { "malazan", "whirlwind" })
            {
                int fi = shipped.FindFaction(f);
                var steps = new List<(string, double, string, string?, int)>();
                // The requirement-blocked steps first, so the reserve's checks run on every one of them before one qualifies.
                foreach (TechDef t in shipped.Techs) if (t.Faction == fi && (t.Requires.Length > 0 || t.RequiresAnyOf.Length > 0)) steps.Add(("time", 0, "research", t.Key, 0));
                foreach (BuildingDef b in shipped.Buildings) if (b.Faction == fi && (b.RequiresTechs.Length > 0 || b.RequiresBuildings.Length > 0)) steps.Add(("time", 0, "build", b.Key, 0));
                foreach (TechDef t in shipped.Techs) if (t.Faction == fi && t.Requires.Length == 0 && t.RequiresAnyOf.Length == 0) steps.Add(("time", 0, "research", t.Key, 0));
                foreach (BuildingDef b in shipped.Buildings) if (b.Faction == fi && b.RequiresTechs.Length == 0 && b.RequiresBuildings.Length == 0) steps.Add(("time", 0, "build", b.Key, 0));
                steps.Add(("time", 0, "expand", null, 0));
                steps.Add(("time", 1, "expand", null, 0)); // a second expand (an identical step is refused by the loader)
                foreach (UnitDef u in shipped.Units) if (u.Faction == fi) steps.Add(("time", 0, "train", u.Key, 3));
                JsonArray order = AiScenes.BuildOrder(steps.ToArray());
                dir.EditJson($"factions/{f}/ai.json", root =>
                {
                    foreach (JsonNode? o in root["openings"]!.AsArray()) o!["buildOrder"] = order.DeepClone();
                    foreach (string d in new[] { "easy", "normal", "hard" })
                    {
                        root["difficulties"]![d]!["expandAfter"] = 0;
                        root["difficulties"]![d]!["buildOrderSlack"] = 0;
                    }
                });
            }
            // M5-4g fix round 1 (BUG-1021): every think names a reserve whatever the army (the reserve's whole path runs).
            dir.EditJson("common/rules.json", r => r["ai"]!["reserveArmyShare"] = 0);
        });
    });

    /// <summary>The allocation-measuring and timing rows: alone in <see cref="SerialCollection"/>.</summary>
    [Collection(SerialCollection.Name)]
    public class Serial
    {
        private readonly ITestOutputHelper _out;

        public Serial(ITestOutputHelper output) => _out = output;

        /// <summary>Four human seats on a generated 256 map (24 forests, 16 mines), each base placed with an army, one Hard AiPlayer a player built here so each think can be timed.</summary>
        private static (Simulation Sim, AiPlayer[] Ais) Scene()
        {
            GameData d = s_everyStep.Value;
            var seats = Enumerable.Range(0, 4).Select(p => new PlayerSeat(p % 2, SeatController.Human, AiDifficulty.Hard)).ToImmutableArray();
            var map = MapGenParams.Default with { Width = 256, Height = 256, Forests = 24, GoldMines = 16, StartLocations = 4 };
            var sim = new Simulation(new SimConfig(7, 4, 2000, 4096) { Data = d, Map = map, Seats = seats });
            SaveScenes.PlaceBases(sim, army: true);
            for (int t = 0; t < 8; t++) sim.Tick();
            TestSim.Explored(sim);
            var ais = new AiPlayer[4];
            for (int p = 0; p < 4; p++)
                ais[p] = new AiPlayer(new PlayerView(sim.World, p), d, d.Ai[sim.World.FactionOf(p)], AiDifficulty.Hard, new SimRng(7, (ulong)RngStream.Ai(p)), sim);
            return (sim, ais);
        }

        /// <summary>
        /// Every step due and unaffordable (each seat's purse set to 0 / 0 before each think, the Requires / cost / no-node
        /// checks all walked): a think's average under 1 ms (worst printed) over 60 thinks a seat after 5, and a think
        /// allocates nothing. Printed: how many of the thinks named a reserve, and which step.
        /// </summary>
        [Fact]
        [Trait("Category", "Perf")]
        public void FourSeats256_EveryStepDueAndUnaffordable_AThinkUnder1Ms_AndAllocatesNothing()
        {
            (Simulation sim, AiPlayer[] ais) = Scene();
            int think = ais[0].ThinkTicks;
            var held = new int[4];
            var step = new int[4];
            for (int p = 0; p < 4; p++)
            {
                int q = p;
                ais[p].ThoughtForTests = x => { if (x.ReserveStep >= 0) { held[q]++; step[q] = x.ReserveStep; } };
            }
            var sw = new Stopwatch();
            var times = new List<double>();
            for (int t = 0; t < 65 * think; t++)
            {
                if (sim.TickNumber % think == 0)
                    for (int p = 0; p < 4; p++)
                    {
                        BuildMaps.SetTotals(sim, p, 0, 0);
                        sw.Restart();
                        ais[p].Think();
                        sw.Stop();
                        if (t >= 5 * think) times.Add(sw.Elapsed.TotalMilliseconds);
                    }
                sim.Tick();
            }
            double avg = times.Average(), worst = times.Max();
            _out.WriteLine($"{times.Count} thinks: avg {avg:F3} ms, worst {worst:F3} ms; reserve held {string.Join(", ", held)} thinks (steps {string.Join(", ", step)} of {ais[0].Opening.BuildOrder.Length} / {ais[1].Opening.BuildOrder.Length}); commands sent {ais.Sum(a => a.CommandsSent)}");
            Assert.True(held.All(h => h > 0), "a seat never named a reserve: the scene doesn't reach the reserve's path");
            for (int p = 0; p < 4; p++) ais[p].ThoughtForTests = null;
            while (sim.TickNumber % think != 0) sim.Tick();
            AiPlayer a0 = ais[0];
            AllocationProbe.AssertZero(() => a0.Think(), _out, () => BuildMaps.SetTotals(sim, 0, 0, 0));
            Assert.True(avg < 1.0, $"avg {avg:F3} ms over the 1 ms budget");
        }
    }
}
