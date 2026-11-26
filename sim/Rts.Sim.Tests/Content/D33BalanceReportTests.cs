using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D33 (the start of the balance pass), a measurement (rule 48: only preconditions asserted). Two reports:
/// <list type="bullet">
/// <item>BUG-1553: Whirlwind <c>holy_war</c>'s wave threshold (<c>attack.minArmyPop</c>, and <c>minArmyCost</c> the floor of
/// its Age I mix at that pop, D21's rule) tried at several values on <see cref="HolyWarFirstWaveReportTests"/>' nine runs
/// (each Malazan opening, the pairing's three smallest seeds, Normal v Normal on the menu's map, 20 minutes).</item>
/// <item>BUG-1420: variants of the Teblor <c>standard</c> opening on the 64-seat row of
/// <c>QA/Content/D32OpeningQaStressTests</c>' shape (the Teblor on seat 0 against the Malazan and on seat 1 against the
/// Whirlwind, four maps, seeds 1-8, to 15:00, the same difficulty on both seats), the Age II kinds fielded per match.</item>
/// </list>
/// <c>D33_VARIANTS</c> (comma-separated names), <c>D33_DIFFS</c> (Easy, Normal, Hard) and <c>D33_THREADS</c> narrow a run.
/// </summary>
[Trait("Category", "Stress")]
public class D33BalanceReportTests(ITestOutputHelper output)
{
    private const int Tps = SimConstants.TicksPerSecond;
    private const int Ten = 12_000;
    private const int Fifteen = 18_000;
    private const int Twenty = 24_000;
    private static readonly string[] Maps = { "generated", "raraku", "pale_hills", "vathar_crossing" };
    private static readonly string[] Opponents = { "malazan", "whirlwind" };
    private static readonly ulong[] Seeds = { 1, 2, 3, 4, 5, 6, 7, 8 };

    /// <summary>The Age II kinds the Fielded column counts, in its order.</summary>
    internal static readonly string[] AgeTwoUnits = { "teblor_javelin_thrower", "teblor_shaman", "teblor_breaker", "teblor_destrier_rider", "teblor_war_dog" };

    private static int Threads() =>
        int.TryParse(Environment.GetEnvironmentVariable("D33_THREADS"), out int n) && n > 0 ? n : Math.Max(1, Environment.ProcessorCount / 2);

    private static bool Wanted(string name)
    {
        string? only = Environment.GetEnvironmentVariable("D33_VARIANTS");
        return string.IsNullOrEmpty(only) || only.Split(',').Contains(name);
    }

    private static string T(int tick) => tick < 0 ? "never" : $"{tick / Tps / 60}:{tick / Tps % 60:00}";

    // ---------- BUG-1553: holy_war's wave ----------

    /// <summary>
    /// Puts D33's two wave changes back on a data copy: the Malazan <c>cadre_rush</c> at 18 army pop / 1,332 and the Whirlwind
    /// <c>holy_war</c> at 20 / 1,312, each with the description that quoted it, as before D33 (a fixture written on an
    /// earlier hash rebuilds that hash with it; the Teblor opening is put back by its own fixture).
    /// </summary>
    internal static void PutBackTheWaves(TestDataDir dir)
    {
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            JsonNode o = root["openings"]!.AsArray().Single(x => (string)x!["id"]! == "cadre_rush")!;
            o["attack"] = new JsonObject { ["minArmyPop"] = 18, ["minArmyCost"] = 1332 };
            o["description"] = ((string)o["description"]!).Replace("waits for 16 army pop.", "waits for 18 army pop.", StringComparison.Ordinal);
        });
        dir.EditJson("factions/whirlwind/ai.json", root =>
        {
            JsonNode o = root["openings"]!.AsArray().Single(x => (string)x!["id"]! == "holy_war")!;
            o["attack"] = new JsonObject { ["minArmyPop"] = 20, ["minArmyCost"] = 1312 };
            o["description"] = ((string)o["description"]!).Replace("the first wave goes at 14 army pop.", "the first wave waits for 20 army pop.", StringComparison.Ordinal);
        });
    }

    /// <summary>The floor of <paramref name="opening"/>'s Age I mix cost (gold + wood) at <paramref name="pop"/> army pop (D21's rule for <c>minArmyCost</c>).</summary>
    internal static int AgeOneCost(GameData data, AiOpening opening, int pop)
    {
        double perPop = 0, weight = 0;
        AiComposition one = opening.Compositions[0];
        for (int k = 0; k < one.Units.Length; k++)
        {
            UnitDef u = data.Units[one.Units[k]];
            weight += one.Weights[k];
            perPop += one.Weights[k] * (u.CostGold + u.CostWood) / (u.HalfPop / 2.0);
        }
        return (int)Math.Floor(perPop / weight * pop);
    }

    /// <summary>
    /// <c>holy_war</c> at each wave threshold (<c>D33_POPS</c>, default 20, 18, 16, 15, 14) on the nine runs: per run the
    /// Malazan first wave, when <c>holy_war</c>'s wave was due, its first wave and waves, its defeat, its peak army pop, and
    /// the Malazan defeat; per threshold the runs with no wave, standing without a wave and defeated.
    /// </summary>
    [Fact]
    public void HolyWar_WaveThreshold_NineRuns_Report()
    {
        int wf = TestSim.Data.FindFaction("whirlwind");
        int holyWar = Array.FindIndex(TestSim.Data.Ai[wf].Openings.ToArray(), o => o.Id == "holy_war");
        Assert.True(holyWar >= 0);
        AiOpening shipped = TestSim.Data.Ai[wf].Openings[holyWar];
        int[] pops = (Environment.GetEnvironmentVariable("D33_POPS") is { Length: > 0 } p ? p : "20,18,16,15,14").Split(',').Select(int.Parse).ToArray();
        int malazans = TestSim.Data.Ai[TestSim.Data.FindFaction("malazan")].Openings.Length;
        var jobs = new List<(int Pop, int Cost, GameData Data, int M, ulong Seed)>();
        foreach (int pop in pops)
        {
            int cost = AgeOneCost(TestSim.Data, shipped, pop);
            GameData data = AiScenes.DataWithAi("whirlwind", root =>
                AiScenes.Opening(root, holyWar)["attack"] = new JsonObject { ["minArmyPop"] = pop, ["minArmyCost"] = cost });
            for (int m = 0; m < malazans; m++)
                foreach (ulong seed in HolyWarFirstWaveReportTests.PairSeeds(m, holyWar)) jobs.Add((pop, cost, data, m, seed));
        }
        var rows = new (int MalWave, int Due, int Wave, int Waves, int Fell, int Peak, int MalFell)[jobs.Count];
        var errors = new ConcurrentQueue<string>();
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Threads() }, i =>
        {
            var j = jobs[i];
            try
            {
                Simulation sim = AiSpendingReportTests.MenuMap(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai), j.Seed, j.Data);
                Assert.Equal((j.M, holyWar), (sim.AiOpening(0), sim.AiOpening(1)));
                AiPlayer mal = sim.AiOf(0)!, hw = sim.AiOf(1)!;
                int malWave = -1, due = -1, wave = -1, fell = -1, peak = 0, malFell = -1;
                for (int t = 0; t < Twenty; t++)
                {
                    sim.Tick();
                    if (malWave < 0 && mal.Board.WavesStarted > 0) malWave = sim.TickNumber;
                    if (wave < 0 && hw.Board.WavesStarted > 0) wave = sim.TickNumber;
                    if (fell < 0 && sim.World.IsDefeated(1)) fell = sim.TickNumber;
                    if (malFell < 0 && sim.World.IsDefeated(0)) malFell = sim.TickNumber;
                    if (fell < 0 && due < 0 && sim.TickNumber % hw.ThinkTicks == 0 && AiOpeningPairsReportTests.WaveDue(sim, hw)) due = sim.TickNumber;
                    if (sim.TickNumber % Tps == 0) peak = Math.Max(peak, ArmyHalfPop(sim.World, 1));
                }
                rows[i] = (malWave, due, wave, hw.Board.WavesStarted, fell, peak, malFell);
            }
            catch (Exception e) { errors.Enqueue($"{j.Pop} {j.M} {j.Seed}: {e}"); }
        });
        Assert.True(errors.IsEmpty, string.Join("\n", errors));
        var names = TestSim.Data.Ai[TestSim.Data.FindFaction("malazan")].Openings;
        output.WriteLine($"BUG-1553: holy_war's wave threshold, Normal v Normal, menu map, {Twenty} ticks; shipped {shipped.AttackMinHalfPop / 2.0} pop / {shipped.AttackMinCost}");
        output.WriteLine("| minArmyPop / minArmyCost | Malazan opening | seed | Malazan first wave | `holy_war` wave due | `holy_war` first wave (waves) | `holy_war` defeated at | `holy_war` peak army pop | Malazan defeated at |");
        output.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        for (int i = 0; i < jobs.Count; i++)
        {
            var j = jobs[i];
            var r = rows[i];
            output.WriteLine($"| {j.Pop} / {j.Cost:#,0} | `{names[j.M].Id}` | {j.Seed} | {T(r.MalWave)} | {T(r.Due)} | {T(r.Wave)} ({r.Waves}) | {T(r.Fell)} | {r.Peak / 2.0} | {T(r.MalFell)} |");
        }
        output.WriteLine("");
        output.WriteLine("| minArmyPop | runs with a wave | stands without a wave | defeated | Malazan defeated | first wave (median) |");
        output.WriteLine("| --- | --- | --- | --- | --- | --- |");
        foreach (int pop in pops)
        {
            var g = Enumerable.Range(0, jobs.Count).Where(i => jobs[i].Pop == pop).Select(i => rows[i]).ToList();
            var firsts = g.Where(r => r.Wave >= 0).Select(r => r.Wave).OrderBy(x => x).ToList();
            output.WriteLine($"| {pop} | {g.Count(r => r.Waves > 0)} | {g.Count(r => r.Waves == 0 && r.Fell < 0)} | {g.Count(r => r.Fell >= 0)} | {g.Count(r => r.MalFell >= 0)} | {(firsts.Count == 0 ? "-" : T(firsts[firsts.Count / 2]))} |");
        }
    }

    // ---------- BUG-1420: the Teblor's Age II kinds ----------

    private static JsonObject Copy(JsonObject o) => JsonNode.Parse(o.ToJsonString())!.AsObject();

    private static JsonArray Order(JsonObject o) => o["buildOrder"]!.AsArray();

    private static int IndexOf(JsonObject o, string what, string? id)
    {
        JsonArray order = Order(o);
        for (int i = 0; i < order.Count; i++)
            if ((string)order[i]!["do"]! == what && (string?)order[i]!["id"] == id) return i;
        throw new InvalidOperationException($"no {what} {id} step");
    }

    private static void MoveBefore(JsonObject o, string what, string? id, string beforeWhat, string? beforeId)
    {
        JsonArray order = Order(o);
        JsonNode step = order[IndexOf(o, what, id)]!;
        order.Remove(step);
        order.Insert(IndexOf(o, beforeWhat, beforeId), step);
    }

    private static void MoveAfter(JsonObject o, string what, string? id, string afterWhat, string? afterId)
    {
        JsonArray order = Order(o);
        JsonNode step = order[IndexOf(o, what, id)]!;
        order.Remove(step);
        order.Insert(IndexOf(o, afterWhat, afterId) + 1, step);
    }

    private static void At(JsonObject o, string what, string? id, int time) => Order(o)[IndexOf(o, what, id)]!["at"] = new JsonObject { ["time"] = time };

    /// <summary>The variant D33 ships (<c>Fixtures/teblor_standard_d33.json</c>, pinned to <c>ai.json</c> by <see cref="TeblorOpeningFixtureTests"/>).</summary>
    internal const string ShippedVariant = "F3";

    /// <summary>
    /// The description D33 ships with <see cref="ShippedVariant"/>: D32's sentence with the Breakers, Destrier Riders and War-dogs
    /// named again (each has its own train step now and is fielded in 12 / 22 / 39 of the 64 Normal matches). The variants
    /// keep D32's words: words do not play.
    /// </summary>
    internal const string Description = "War Lodge and Javelin Ground first, then Age II; four Warriors raid at 8 army pop, then Javelin Throwers, Shamans, Breakers, Destrier Riders and War-dogs join them and a second Longhouse goes up.";

    /// <summary>The variant whose opening the shipped file holds, by name (the D33 fixture's measured family).</summary>
    internal static JsonObject Variant(string name) => AllTeblorVariants().Single(v => v.Name == name).Opening;

    /// <summary>The variants <c>D33_VARIANTS</c> names (comma-separated), or all of <see cref="AllTeblorVariants"/>.</summary>
    internal static (string Name, string What, JsonObject Opening)[] TeblorVariants() => AllTeblorVariants().Where(v => Wanted(v.Name)).ToArray();

    /// <summary>
    /// Every variant D33 measured, each named by its edit to the opening D32 shipped (S0, <c>Fixtures/teblor_standard_d32.json</c>,
    /// since the shipped file holds D33's) or an earlier variant.
    /// </summary>
    internal static (string Name, string What, JsonObject Opening)[] AllTeblorVariants()
    {
        JsonObject s0 = D32TeblorOpeningReportTests.D32();
        var list = new List<(string, string, JsonObject)> { ("S0", "the opening D32 shipped (D32's X7)", s0) };

        JsonObject b1 = Copy(s0);
        MoveBefore(b1, "build", "teblor_breakers_hall", "build", "teblor_bloodwood_carver");
        MoveBefore(b1, "build", "teblor_stables", "build", "teblor_bloodwood_carver");
        list.Add(("B1", "S0 with the Breakers Hall and the Stables first of the 10:00 steps (before the Carver)", b1));

        JsonObject b2 = Copy(b1);
        At(b2, "build", "teblor_bloodwood_carver", 660);
        list.Add(("B2", "B1 with the Carver at 11:00", b2));

        JsonObject b3 = Copy(s0);
        MoveAfter(b3, "build", "teblor_stables", "train", "teblor_shaman");
        MoveAfter(b3, "build", "teblor_breakers_hall", "build", "teblor_stables");
        At(b3, "build", "teblor_stables", 540);
        At(b3, "build", "teblor_breakers_hall", 540);
        list.Add(("B3", "S0 with the Stables and the Breakers Hall at 9:00, after the Shaman step", b3));

        JsonObject b4 = Copy(s0);
        MoveAfter(b4, "build", "teblor_stables", "research", "age_ii");
        At(b4, "build", "teblor_stables", 420);
        MoveBefore(b4, "build", "teblor_breakers_hall", "build", "teblor_bloodwood_carver");
        list.Add(("B4", "S0 with the Stables at 7:00 before the expansion and the Breakers Hall first of the 10:00 steps", b4));

        JsonObject b5 = Copy(b1);
        MoveAfter(b5, "train", "teblor_gatherer", "research", "armor_1");
        list.Add(("B5", "B1 with the eight Gatherers last in the file", b5));

        JsonObject b6 = Copy(b3);
        At(b6, "build", "teblor_bloodwood_carver", 660);
        list.Add(("B6", "B3 with the Carver at 11:00", b6));

        // Batch 2: a train step for each of the three kinds after its hall's step (the Shaman step's pattern, D32).
        JsonObject c1 = Copy(b4);
        AddTrains(c1);
        list.Add(("C1", "B4 plus train steps: 1 Breaker after the Breakers Hall, 1 Destrier Rider and 1 War-dog pack after the Stables", c1));

        JsonObject c2 = Copy(b3);
        AddTrains(c2);
        list.Add(("C2", "B3 plus C1's train steps", c2));

        JsonObject c3 = Copy(s0);
        AddTrains(c3);
        list.Add(("C3", "S0 plus C1's train steps", c3));

        JsonObject c4 = Copy(b1);
        AddTrains(c4);
        list.Add(("C4", "B1 plus C1's train steps", c4));

        JsonObject c5 = Copy(s0);
        MoveAfter(c5, "build", "teblor_stables", "expand", null);
        At(c5, "build", "teblor_stables", 480);
        MoveBefore(c5, "build", "teblor_breakers_hall", "build", "teblor_bloodwood_carver");
        AddTrains(c5);
        list.Add(("C5", "S0 with the Stables at 8:00 just after the expansion, the Breakers Hall first of the 10:00 steps, and C1's train steps", c5));

        JsonObject c6 = Copy(b6);
        AddTrains(c6);
        list.Add(("C6", "B6 plus C1's train steps", c6));

        // Batch 3 (C4 came closest on Normal: Breakers 6 of 64, the Breakers Hall finished in 32 at a median 13:30).
        JsonObject d1 = Copy(c4);
        At(d1, "build", "teblor_bloodwood_carver", 660);
        list.Add(("D1", "C4 with the Carver at 11:00", d1));

        JsonObject d2 = Copy(s0);
        MoveBefore(d2, "build", "teblor_breakers_hall", "build", "teblor_shamans_circle");
        At(d2, "build", "teblor_breakers_hall", 540);
        MoveBefore(d2, "build", "teblor_stables", "build", "teblor_bloodwood_carver");
        AddTrains(d2);
        list.Add(("D2", "S0 with the Breakers Hall at 9:00 before the Circle, the Stables first of the 10:00 steps, and C1's train steps", d2));

        JsonObject d3 = Copy(s0);
        MoveAfter(d3, "build", "teblor_breakers_hall", "train", "teblor_shaman");
        At(d3, "build", "teblor_breakers_hall", 540);
        MoveBefore(d3, "build", "teblor_stables", "build", "teblor_bloodwood_carver");
        AddTrains(d3);
        list.Add(("D3", "S0 with the Breakers Hall at 9:00 after the Shaman step, the Stables first of the 10:00 steps, and C1's train steps", d3));

        JsonObject d4 = Copy(d3);
        At(d4, "build", "teblor_bloodwood_carver", 660);
        list.Add(("D4", "D3 with the Carver at 11:00", d4));

        JsonObject d5 = Copy(c4);
        Order(d5)[IndexOf(d5, "train", "teblor_breaker")]!["count"] = 2;
        list.Add(("D5", "C4 with 2 Breakers in the train step", d5));

        JsonObject d6 = Copy(c4);
        JsonObject two = d6["compositions"]![1]!["units"]!.AsObject();
        var weights = two.ToDictionary(kv => kv.Key, kv => (int)kv.Value!);
        two.Clear();
        foreach (string k in new[] { "teblor_warrior", "teblor_javelin_thrower", "teblor_breaker", "teblor_destrier_rider", "teblor_shaman", "teblor_war_dog" }) two[k] = weights[k];
        list.Add(("D6", "C4 with the Breaker listed before the Destrier Rider and the Shaman in the Age II mix (the tie order)", d6));

        JsonObject d7 = Copy(d3);
        At(d7, "build", "teblor_stables", 660);
        list.Add(("D7", "D3 with the Stables at 11:00", d7));

        // Batch 4: the halls stand in 30-50 matches but the Breaker is not trained: the train steps sit after the expand step
        // in the file, so while the expansion waits (36 of 64 never finish it) its 275 / 275 reserve holds them back. Here
        // the train steps go before the expand step (still due at their hall's time; a train step waits for its hall).
        JsonObject e1 = Copy(c4);
        TrainsBeforeExpand(e1, "teblor_breaker", "teblor_destrier_rider", "teblor_war_dog");
        list.Add(("E1", "C4 with its three train steps moved before the expand step", e1));

        JsonObject e2 = Copy(c4);
        TrainsBeforeExpand(e2, "teblor_breaker");
        list.Add(("E2", "C4 with the Breaker train step moved before the expand step", e2));

        JsonObject e3 = Copy(c3);
        TrainsBeforeExpand(e3, "teblor_breaker", "teblor_destrier_rider", "teblor_war_dog");
        list.Add(("E3", "C3 (S0 plus the train steps) with the three train steps before the expand step", e3));

        JsonObject e4 = Copy(d2);
        TrainsBeforeExpand(e4, "teblor_breaker", "teblor_destrier_rider", "teblor_war_dog");
        list.Add(("E4", "D2 with the three train steps before the expand step", e4));

        JsonObject e5 = Copy(e1);
        TrainsBeforeExpand(e5, "teblor_shaman");
        list.Add(("E5", "E1 with the Shaman step before the expand step too", e5));

        JsonObject e6 = Copy(e3);
        TrainsBeforeExpand(e6, "teblor_shaman");
        list.Add(("E6", "E3 with the Shaman step before the expand step too", e6));

        // Batch 5: E1 fields Breakers in 7 of 64 on Normal (the brief asks 8); its Breakers Hall stands in 25, at a median
        // 12:29. The hall sooner, or the two researches later.
        JsonObject f1 = Copy(e1);
        MoveBefore(f1, "build", "teblor_breakers_hall", "expand", null);
        list.Add(("F1", "E1 with the Breakers Hall step moved before the expand step (still due at 10:00)", f1));

        JsonObject f2 = Copy(e1);
        At(f2, "build", "teblor_breakers_hall", 540);
        list.Add(("F2", "E1 with the Breakers Hall due at 9:00", f2));

        JsonObject f3 = Copy(f1);
        At(f3, "build", "teblor_breakers_hall", 540);
        list.Add(("F3", "F1 with the Breakers Hall due at 9:00", f3));

        JsonObject f4 = Copy(e1);
        At(f4, "research", "teblor_blood_oil_anointing", 780);
        At(f4, "research", "armor_1", 810);
        list.Add(("F4", "E1 with the Anointing at 13:00 and Armor at 13:30", f4));

        JsonObject f5 = Copy(e1);
        At(f5, "build", "teblor_bloodwood_carver", 660);
        At(f5, "research", "teblor_blood_oil_anointing", 750);
        At(f5, "research", "armor_1", 780);
        list.Add(("F5", "E1 with the Carver at 11:00, the Anointing at 12:30 and Armor at 13:00", f5));

        JsonObject f6 = Copy(e4);
        At(f6, "research", "teblor_blood_oil_anointing", 780);
        At(f6, "research", "armor_1", 810);
        list.Add(("F6", "E4 with the Anointing at 13:00 and Armor at 13:30", f6));

        return list.ToArray();
    }

    /// <summary>Moves each named unit's train step to just before the expand step, in the order given.</summary>
    private static void TrainsBeforeExpand(JsonObject o, params string[] units)
    {
        foreach (string u in units) MoveBefore(o, "train", u, "expand", null);
    }

    private static JsonObject Train(string id, int count, int time) =>
        new() { ["at"] = new JsonObject { ["time"] = time }, ["do"] = "train", ["id"] = id, ["count"] = count };

    private static int TimeOf(JsonObject o, string what, string id) => (int)Order(o)[IndexOf(o, what, id)]!["at"]!["time"]!;

    /// <summary>One Breaker after the Breakers Hall step, one Destrier Rider and one War-dog pack after the Stables step, each due with its hall.</summary>
    private static void AddTrains(JsonObject o)
    {
        Order(o).Insert(IndexOf(o, "build", "teblor_breakers_hall") + 1, Train("teblor_breaker", 1, TimeOf(o, "build", "teblor_breakers_hall")));
        int st = TimeOf(o, "build", "teblor_stables");
        Order(o).Insert(IndexOf(o, "build", "teblor_stables") + 1, Train("teblor_war_dog", 1, st));
        Order(o).Insert(IndexOf(o, "build", "teblor_stables") + 1, Train("teblor_destrier_rider", 1, st));
    }

    /// <summary>Every variant loads as the Teblor's <c>standard</c> on a shipped copy.</summary>
    [Fact]
    public void EveryTeblorVariant_Loads()
    {
        foreach (var v in TeblorVariants())
        {
            GameData d = TeblorOpeningVariantsReportTests.DataWith(v.Opening);
            Assert.Equal("standard", d.Ai[d.FindFaction("teblor")].Openings[0].Id);
            Assert.Equal(v.Opening["buildOrder"]!.AsArray().Count, d.Ai[d.FindFaction("teblor")].Openings[0].BuildOrder.Length);
        }
    }

    internal sealed record Seat(string Variant, AiDifficulty Diff, ulong Seed, string Map, string Opponent, int AgeTwoAt, int Waves10, int DefeatedAt,
        int HallAt, bool EveryKind, int AnointedAt, bool[] Fielded, int StablesAt, int BreakersHallAt, int CircleAt);

    /// <summary>One match to 15:00 as <c>D32OpeningQaStressTests</c> plays it: the Teblor on seat 0 against the Malazan, on seat 1 against the Whirlwind.</summary>
    internal static Seat Play(string variant, GameData data, string map, string opponent, ulong seed, AiDifficulty diff)
    {
        int teblor = data.FindFaction("teblor");
        PlayerSeat t = new(teblor, SeatController.Ai, diff);
        PlayerSeat o = new(data.FindFaction(opponent), SeatController.Ai, diff);
        bool first = opponent != "whirlwind";
        PlayerSeat[] seats = first ? new[] { t, o } : new[] { o, t };
        int p = first ? 0 : 1;
        Simulation sim = map == "generated"
            ? AiScenes.Generated(seats, seed, false, out _, data: data)
            : AiScenes.Generated(seats, seed, false, out _, data: data,
                configure: c => c with { MapId = map, Map = c.Map with { Width = data.Maps[data.FindMap(map)].Size, Height = data.Maps[data.FindMap(map)].Size } });
        World w = sim.World;
        Assert.Equal(teblor, w.FactionOf(p));
        AiPlayer ai = sim.AiOf(p)!;
        Assert.Equal("standard", ai.Opening.Id);
        var kinds = new SortedSet<int>();
        foreach (AiStep s in ai.Steps) if (s.Action == AiStepAction.Build) kinds.Add(s.Target);
        var built = new bool[data.Buildings.Length];
        int hall = data.FindBuilding("teblor_longhouse");
        int[] ageTwo = AgeTwoUnits.Select(data.FindUnit).ToArray();
        Assert.DoesNotContain(-1, ageTwo);
        int anoint = data.FindTech("teblor_blood_oil_anointing");
        var fielded = new bool[ageTwo.Length];
        int ageAt = -1, waves10 = 0, fell = -1, hallAt = -1;
        int stables = data.FindBuilding("teblor_stables"), breakers = data.FindBuilding("teblor_breakers_hall"), circle = data.FindBuilding("teblor_shamans_circle");
        int stablesAt = -1, breakersAt = -1, circleAt = -1;
        while (sim.TickNumber < Fifteen)
        {
            sim.Tick();
            int now = sim.TickNumber;
            if (ageAt < 0 && w.Age(p) >= 2) ageAt = now;
            if (now == Ten) waves10 = ai.Board.WavesStarted;
            if (fell < 0 && w.IsDefeated(p)) fell = now;
            if (now % Tps != 0) continue;
            var u = w.Units;
            for (int s = 0; s < u.Capacity; s++)
            {
                if (!u.Alive[s] || u.Owner[s] != p) continue;
                int kind = Array.IndexOf(ageTwo, u.TypeId[s]);
                if (kind >= 0) fielded[kind] = true;
            }
            var bs = w.Buildings;
            int halls = 0;
            for (int s = 0; s < bs.Capacity; s++)
            {
                if (!bs.Alive[s] || bs.Owner[s] != p || bs.UnderConstruction[s]) continue;
                built[bs.TypeId[s]] = true;
                if (bs.TypeId[s] == hall) halls++;
                if (bs.TypeId[s] == stables && stablesAt < 0) stablesAt = now;
                if (bs.TypeId[s] == breakers && breakersAt < 0) breakersAt = now;
                if (bs.TypeId[s] == circle && circleAt < 0) circleAt = now;
            }
            if (hallAt < 0 && halls >= 2) hallAt = now;
        }
        return new Seat(variant, diff, seed, map, opponent, ageAt, waves10, fell, hallAt, kinds.All(k => built[k]), w.TechResearchedTick(p, anoint), fielded,
            stablesAt, breakersAt, circleAt);
    }

    /// <summary>
    /// Each variant at each difficulty (<c>D33_DIFFS</c>, default Normal and Hard) on the 64-seat row: defeats by 15:00,
    /// a second Longhouse by 15:00, Age II and a wave by 10:00, every building kind, the Anointing, and the matches in which
    /// each Age II kind was alive at some second before 15:00.
    /// </summary>
    [Fact]
    public void TeblorVariants_64SeatRow_Report()
    {
        var variants = TeblorVariants();
        AiDifficulty[] diffs = (Environment.GetEnvironmentVariable("D33_DIFFS") is { Length: > 0 } s ? s : "Normal,Hard").Split(',').Select(Enum.Parse<AiDifficulty>).ToArray();
        var jobs = new List<(string V, GameData D, string M, string O, ulong S, AiDifficulty X)>();
        foreach (var v in variants)
        {
            GameData d = TeblorOpeningVariantsReportTests.DataWith(v.Opening);
            foreach (AiDifficulty x in diffs)
                foreach (ulong seed in Seeds)
                    foreach (string m in Maps)
                        foreach (string o in Opponents) jobs.Add((v.Name, d, m, o, seed, x));
        }
        var rows = new Seat[jobs.Count];
        var errors = new ConcurrentQueue<string>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Threads() }, i =>
        {
            var j = jobs[i];
            try { rows[i] = Play(j.V, j.D, j.M, j.O, j.S, j.X); }
            catch (Exception e) { errors.Enqueue($"{j.V} {j.X} {j.S} {j.M} {j.O}: {e}"); }
        });
        Assert.True(errors.IsEmpty, string.Join("\n", errors));
        output.WriteLine($"D33 BUG-1420: {jobs.Count} matches to 15:00 in {clock.Elapsed.TotalSeconds:0} s (seeds 1-8, four maps, the Teblor v the Malazan on seat 0 and v the Whirlwind on seat 1)");
        foreach (var v in variants) output.WriteLine($"- {v.Name}: {v.What}");
        output.WriteLine("");
        output.WriteLine("| variant | difficulty | defeated by 15:00 | 2nd Longhouse by 15:00 | Age II by 10:00 | wave by 10:00 | every kind | Anointing | Circle / Breakers Hall / Stables finished (median first) | fielded: Javelin Throwers / Shamans / Breakers / Destrier Riders / War-dogs |");
        output.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var v in variants)
            foreach (AiDifficulty x in diffs)
            {
                var g = rows.Where(r => r.Variant == v.Name && r.Diff == x).ToList();
                output.WriteLine($"| {v.Name} | {x} | {g.Count(r => r.DefeatedAt >= 0)} | {g.Count(r => r.HallAt >= 0)} | {g.Count(r => r.AgeTwoAt >= 0 && r.AgeTwoAt <= Ten)} | {g.Count(r => r.Waves10 > 0)} | "
                    + $"{g.Count(r => r.EveryKind)} | {g.Count(r => r.AnointedAt >= 0)} | {g.Count(r => r.CircleAt >= 0)} / {g.Count(r => r.BreakersHallAt >= 0)} ({Median(g.Select(r => r.BreakersHallAt))}) / {g.Count(r => r.StablesAt >= 0)} ({Median(g.Select(r => r.StablesAt))}) | {string.Join(" / ", Enumerable.Range(0, AgeTwoUnits.Length).Select(k => g.Count(r => r.Fielded[k])))} | (of {g.Count})");
            }
        output.WriteLine("");
        output.WriteLine("Per map (defeated / 2nd Longhouse / Breakers / Riders / War-dogs):");
        foreach (var v in variants)
            foreach (AiDifficulty x in diffs)
                output.WriteLine($"| {v.Name} | {x} | " + string.Join(" | ", Maps.Select(m =>
                {
                    var g = rows.Where(r => r.Variant == v.Name && r.Diff == x && r.Map == m).ToList();
                    return $"{m} {g.Count(r => r.DefeatedAt >= 0)}/{g.Count(r => r.HallAt >= 0)}/{g.Count(r => r.Fielded[2])}/{g.Count(r => r.Fielded[3])}/{g.Count(r => r.Fielded[4])}";
                })) + " |");
    }

    /// <summary>
    /// Rule 45 (a pinned criterion row that stops ending moves to the nearest seed that ends): the M5-5 rows of
    /// <c>D33_ROWS</c> ("map:seed:players", players negative for the 2v2; default the four-seat FFA's Vathar Crossing seeds
    /// 1, 3, 4 and 5) played as <c>Stress/MatchRowsM6S10Report</c> plays them, every check on, to 72,000 ticks, in parallel.
    /// </summary>
    [Fact]
    public void CriterionRows_Report()
    {
        string spec = Environment.GetEnvironmentVariable("D33_ROWS") is { Length: > 0 } r ? r : "vathar_crossing:1:4,vathar_crossing:3:4,vathar_crossing:4:4,vathar_crossing:5:4";
        var rows = spec.Split(',').Select(x => x.Split(':')).Select(x => (Map: x[0], Seed: ulong.Parse(x[1]), Players: int.Parse(x[2]))).ToArray();
        var lines = new string[rows.Length];
        var errors = new ConcurrentQueue<string>();
        Parallel.For(0, rows.Length, new ParallelOptions { MaxDegreeOfParallelism = Threads() }, i =>
        {
            var (map, seed, players) = rows[i];
            try
            {
                int[]? teams = players < 0 ? new[] { 1, 2, 1, 2 } : null;
                int seats = Math.Abs(players);
                Simulation sim = CriterionMatch.Build(map, seed, seats, teams);
                CriterionMatch.Result res = CriterionMatch.Play(sim, 72_000, checks: true);
                lines[i] = $"ROW {map} seed {seed}, {seats} seats{(teams == null ? "" : " 2v2")}: over {res.Over} at {res.EndTick} ({CriterionMatch.Clock(res.EndTick)}); winner {res.Winner}; "
                    + $"defeated at {string.Join(" / ", res.DefeatedAt)}; waves {res.Waves}, hunts {res.Hunts}; refused: {res.RefusalText}; longest refusal run {res.LongestStreak}; "
                    + $"error {(res.Error ?? "none")}; fold {res.Fold:X16}, end hash {res.EndHash:X16}";
            }
            catch (Exception e) { errors.Enqueue($"{map} {seed} {players}: {e}"); }
        });
        Assert.True(errors.IsEmpty, string.Join("\n", errors));
        foreach (string line in lines) output.WriteLine(line);
    }

    /// <summary>
    /// For <c>Stress/M6Sh2DeterminismStressTests</c>' re-stage (its seed must be a Raraku Normal v Normal match in which a seat
    /// loses every worker and sends a worker Train): Raraku seeds 1-8 (<c>D33_SEEDS</c>), each to 45,000 ticks or its end, the
    /// first such retrain's tick and seat, and the end.
    /// </summary>
    [Fact]
    public void RarakuRetrainSeeds_Report()
    {
        ulong[] seeds = (Environment.GetEnvironmentVariable("D33_SEEDS") is { Length: > 0 } x ? x : "1,2,4,5,6,7,8").Split(',').Select(ulong.Parse).ToArray();
        var lines = new string[seeds.Length];
        var errors = new ConcurrentQueue<string>();
        Parallel.For(0, seeds.Length, new ParallelOptions { MaxDegreeOfParallelism = Threads() }, i =>
        {
            try
            {
                Simulation sim = MapFileTests.MapMatch("raraku", seeds[i], false, out _);
                int retrainAt = -1, seat = -1;
                for (int p = 0; p < 2; p++)
                {
                    AiPlayer ai = sim.AiOf(p)!;
                    int player = p;
                    ai.SentForTests = c =>
                    {
                        if (retrainAt < 0 && c.Kind == Rts.Sim.Commands.CommandKind.Train && ai.WorkerCount == 0 && ai.IsWorkerType[c.TypeId]) (retrainAt, seat) = (c.Tick, player);
                    };
                }
                while (sim.TickNumber < 45_000 && !sim.MatchOver) sim.Tick();
                lines[i] = $"raraku seed {seeds[i]}: first retrain {(retrainAt < 0 ? "none" : $"seat {seat} for tick {retrainAt}")}; ran to {sim.TickNumber}, over {sim.MatchOver}";
            }
            catch (Exception e) { errors.Enqueue($"{seeds[i]}: {e}"); }
        });
        Assert.True(errors.IsEmpty, string.Join("\n", errors));
        foreach (string l in lines) output.WriteLine(l);
    }

    private static string Median(IEnumerable<int> ticks)
    {
        var l = ticks.Where(t => t >= 0).OrderBy(t => t).ToList();
        return l.Count == 0 ? "-" : T(l[l.Count / 2]);
    }

    private static int ArmyHalfPop(World w, int player)
    {
        var u = w.Units;
        int army = 0;
        for (int s = 0; s < u.Capacity; s++)
            if (u.Alive[s] && u.Owner[s] == player && w.Data.Units[u.TypeId[s]].Slot != UnitSlot.Worker) army += w.Data.Units[u.TypeId[s]].HalfPop;
        return army;
    }
}
