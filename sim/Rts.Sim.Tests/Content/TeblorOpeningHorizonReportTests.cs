using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D29 fix round 1 (BUG-1300, BUG-1301): the Teblor opening variants judged on two horizons. QA's eight pairings (the
/// generated map, Raraku, Pale Hills, Vathar Crossing; the Teblor on seat 0 against a Malazan and on seat 1 against a
/// Whirlwind, or the reverse with the seats swapped; Normal against Normal) on a copy of the shipped data whose Teblor
/// <c>standard</c> opening is replaced, played to 18,000 ticks (15:00). At 10:00: Age II, a wave (BUG-1241's target: both in
/// at least 6 of 8 per seed). At 15:00: Age II, expanded (a second finished Longhouse), every building kind of the opening
/// finished at least once, the Breakers Hall, Shamans' Circle and Stables finished, the Anointing researched, each Age II unit
/// kind ever fielded, the Teblor defeated, Gatherers and army pop alive. The hand-over
/// (<c>Fixtures/teblor_standard_d29.json</c>) is measured beside the shipped opening on both seat orders, and with the
/// global <c>ai.reserveArmyShare</c> lowered on a data copy (BUG-1301's proxy for a per-opening share: it moves the
/// opponent's gate too). A measurement (rule 48): only preconditions are asserted.
/// </summary>
[Trait("Category", "Stress")]
public class TeblorOpeningHorizonReportTests(ITestOutputHelper output)
{
    private const int Tps = SimConstants.TicksPerSecond;
    private const int Ten = 12_000;
    private const int Fifteen = 18_000;

    private static readonly string[] Maps = { "generated", "raraku", "pale_hills", "vathar_crossing" };
    private static readonly string[] Opponents = { "malazan", "whirlwind" };
    private static readonly ulong[] Seeds = { 1, 2, 3, 4, 5 };

    /// <summary>The Age II unit kinds the opening's description names, in its order (each counted when ever fielded by 15:00).</summary>
    private static readonly string[] AgeTwoUnits = { "teblor_javelin_thrower", "teblor_shaman", "teblor_breaker", "teblor_destrier_rider", "teblor_war_dog" };

    /// <summary>The hand-over (BUG-1300's fix): the opening D29 proposes to land, the variant <see cref="HandOverVariant"/>.</summary>
    internal static string HandOverPath => Path.Combine(TestDataDir.RepoRoot(), "sim", "Rts.Sim.Tests", "Content", "Fixtures", "teblor_standard_d29.json");

    /// <summary>The variant the hand-over fixture is (asserted equal by <see cref="TeblorOpeningFixtureTests"/>).</summary>
    internal const string HandOverVariant = "V24";

    /// <summary>The hand-over's opening.</summary>
    internal static JsonObject HandOver() => JsonNode.Parse(File.ReadAllText(HandOverPath))!.AsObject();

    /// <summary>One match's figures for the Teblor seat (ticks; -1 never).</summary>
    internal sealed record Row(string Variant, ulong Seed, string Map, string Opponent, int Seat, int AgeTwoAt, int Waves10, int Waves15,
        int ExpandedAt, string NeverBuilt, bool Breakers, bool Shamans, bool Stables, int AnointedAt, bool[] Fielded, int DefeatedAt, int OtherDefeatedAt,
        int Gatherers10, int Gatherers15, int Army10, int Army15)
    {
        public bool Age10 => AgeTwoAt >= 0 && AgeTwoAt <= Ten;
        public bool Age15 => AgeTwoAt >= 0;
        public bool Wave10 => Waves10 > 0;
        public bool Lost => DefeatedAt >= 0;
    }

    // ---------- variants ----------

    private static JsonObject Copy(JsonObject o) => JsonNode.Parse(o.ToJsonString())!.AsObject();

    private static JsonObject Step(JsonObject o, string what, string? id, int nth = 0)
    {
        foreach (JsonNode? s in o["buildOrder"]!.AsArray())
            if ((string)s!["do"]! == what && (string?)s["id"] == id && nth-- == 0) return s.AsObject();
        throw new InvalidOperationException($"no {what} {id} step");
    }

    private static void At(JsonObject step, int time) => step["at"] = new JsonObject { ["time"] = time };

    private static void Wave(JsonObject o, double pop, int cost) => o["attack"] = new JsonObject { ["minArmyPop"] = pop, ["minArmyCost"] = cost };

    /// <summary>
    /// Every variant measured in fix round 1, each named by its edit to an earlier one (V0 shipped; V4 D29's first hand-over,
    /// <see cref="TeblorOpeningVariantsReportTests.Variants"/>). <c>D29_VARIANTS</c> (comma-separated names) runs a subset.
    /// </summary>
    internal static (string Name, JsonObject Opening)[] Variants()
    {
        JsonObject v0 = TeblorOpeningVariantsReportTests.Shipped();
        JsonObject v4 = TeblorOpeningVariantsReportTests.Fixture(); // the first hand-over (BUG-1300)
        var list = new List<(string, JsonObject)> { ("V0", v0), ("V4", v4) };

        // V5: V4 with the eight Gatherers due at 7:00 (Age II's research is due at 5:00 and takes 60 s).
        JsonObject v5 = Copy(v4);
        At(Step(v5, "train", "teblor_gatherer", 1), 420);
        list.Add(("V5", v5));

        // V6: V5 with 11 Gatherers at the start (the shipped count).
        JsonObject v6 = Copy(v5);
        Step(v6, "train", "teblor_gatherer")["count"] = 11;
        list.Add(("V6", v6));

        // V7: V5 with the expansion at 8:00 and the Stables at 8:15 (the shipped times).
        JsonObject v7 = Copy(v5);
        At(Step(v7, "expand", null), 480);
        At(Step(v7, "build", "teblor_stables"), 495);
        list.Add(("V7", v7));

        // V8: V7 with 11 Gatherers at the start.
        JsonObject v8 = Copy(v7);
        Step(v8, "train", "teblor_gatherer")["count"] = 11;
        list.Add(("V8", v8));

        // V9: V5 with a 6 pop / 450 wave.
        JsonObject v9 = Copy(v5);
        Wave(v9, 6, 450);
        list.Add(("V9", v9));

        // V10: V8 with a 6 pop / 450 wave.
        JsonObject v10 = Copy(v8);
        Wave(v10, 6, 450);
        list.Add(("V10", v10));

        // V11: V0 with only the wave changed to 4 pop / 300.
        JsonObject v11 = Copy(v0);
        Wave(v11, 4, 300);
        list.Add(("V11", v11));

        // V12: V4 with the shipped 8 pop / 600 wave.
        JsonObject v12 = Copy(v4);
        Wave(v12, 8, 600);
        list.Add(("V12", v12));

        // V13: V4 with 11 Gatherers at the start (the eight more still at 10:00).
        JsonObject v13 = Copy(v4);
        Step(v13, "train", "teblor_gatherer")["count"] = 11;
        list.Add(("V13", v13));

        // V14: V4 with Age II due at 6:00.
        JsonObject v14 = Copy(v4);
        At(Step(v14, "research", "age_ii"), 360);
        list.Add(("V14", v14));

        // V15: V4 with a 6 pop / 450 wave.
        JsonObject v15 = Copy(v4);
        Wave(v15, 6, 450);
        list.Add(("V15", v15));

        // V16: V0 with the Javelin Ground at pop 9 as Age II's second kind, the Carver and Armor I at 8:00 (after Age II).
        JsonObject v16 = Copy(v0);
        Step(v16, "build", "teblor_javelin_ground")["at"] = new JsonObject { ["pop"] = 9 };
        At(Step(v16, "build", "teblor_bloodwood_carver"), 480);
        At(Step(v16, "research", "armor_1"), 600);
        list.Add(("V16", v16));

        // V17: V14 with the shipped 8 pop / 600 wave.
        JsonObject v17 = Copy(v14);
        Wave(v17, 8, 600);
        list.Add(("V17", v17));

        // V18: V14 with the eight Gatherers due at 8:00.
        JsonObject v18 = Copy(v14);
        At(Step(v18, "train", "teblor_gatherer", 1), 480);
        list.Add(("V18", v18));

        // V19: V4 with Age II due at 7:00 (the shipped time).
        JsonObject v19 = Copy(v4);
        At(Step(v19, "research", "age_ii"), 420);
        list.Add(("V19", v19));

        // V20: V12 with Age II due at 4:00.
        JsonObject v20 = Copy(v12);
        At(Step(v20, "research", "age_ii"), 240);
        list.Add(("V20", v20));

        // V21: V14 with the Stables at 8:15 (the shipped time).
        JsonObject v21 = Copy(v14);
        At(Step(v21, "build", "teblor_stables"), 495);
        list.Add(("V21", v21));

        // V22: V14 with a 5 pop / 375 wave.
        JsonObject v22 = Copy(v14);
        Wave(v22, 5, 375);
        list.Add(("V22", v22));

        // V23: V17 with the Breakers Hall at 8:00 and the Carver at 9:00 (the Age II halls before the upgrade building).
        JsonObject v23 = Copy(v17);
        At(Step(v23, "build", "teblor_breakers_hall"), 480);
        At(Step(v23, "build", "teblor_bloodwood_carver"), 540);
        list.Add(("V23", v23));

        // V24: V17 with the expansion at 8:00 (the shipped time).
        JsonObject v24 = Copy(v17);
        At(Step(v24, "expand", null), 480);
        list.Add(("V24", v24));

        // V25: V17 with a 7 pop / 525 wave.
        JsonObject v25 = Copy(v17);
        Wave(v25, 7, 525);
        list.Add(("V25", v25));

        // V26: V17 with the eight Gatherers due at 9:00.
        JsonObject v26 = Copy(v17);
        At(Step(v26, "train", "teblor_gatherer", 1), 540);
        list.Add(("V26", v26));

        string? only = Environment.GetEnvironmentVariable("D29_VARIANTS");
        if (string.IsNullOrEmpty(only)) return list.ToArray();
        var keep = new HashSet<string>(only.Split(','));
        return list.Where(v => keep.Contains(v.Item1)).ToArray();
    }

    // ---------- play ----------

    /// <summary>Plays one pairing to 15:00 (the Teblor on seat 0 against the Malazan, seat 1 against the Whirlwind; the reverse when <paramref name="swapped"/>).</summary>
    internal static Row Play(string variant, GameData data, string map, string opponent, ulong seed, bool swapped = false)
    {
        int teblor = data.FindFaction("teblor");
        PlayerSeat t = new(teblor, SeatController.Ai, AiDifficulty.Normal);
        PlayerSeat o = new(data.FindFaction(opponent), SeatController.Ai, AiDifficulty.Normal);
        bool first = (opponent == "malazan") != swapped;
        PlayerSeat[] seats = first ? new[] { t, o } : new[] { o, t };
        int p = first ? 0 : 1;
        Simulation sim;
        if (map == "generated") sim = AiScenes.Generated(seats, seed, false, out _, data: data);
        else
        {
            int size = data.Maps[data.FindMap(map)].Size;
            sim = AiScenes.Generated(seats, seed, false, out _, data: data,
                configure: c => c with { MapId = map, Map = c.Map with { Width = size, Height = size } });
        }
        World w = sim.World;
        Assert.Equal(teblor, w.FactionOf(p));
        AiPlayer ai = sim.AiOf(p)!;
        Assert.Equal("standard", ai.Opening.Id);

        var kinds = new SortedSet<int>();
        foreach (AiStep s in ai.Opening.BuildOrder) if (s.Action == AiStepAction.Build) kinds.Add(s.Target);
        var built = new bool[data.Buildings.Length];
        int hall = data.FindBuilding("teblor_longhouse");
        int breakers = data.FindBuilding("teblor_breakers_hall"), stables = data.FindBuilding("teblor_stables"), shamans = data.FindBuilding("teblor_shamans_circle");
        int[] ageTwoUnits = AgeTwoUnits.Select(data.FindUnit).ToArray();
        var fielded = new bool[ageTwoUnits.Length];
        int gatherer = data.FindUnit("teblor_gatherer");
        int anoint = data.FindTech("teblor_blood_oil_anointing");
        int ageAt = -1, waves10 = 0, expanded = -1, fell = -1, otherFell = -1, g10 = 0, a10 = 0;
        while (sim.TickNumber < Fifteen)
        {
            sim.Tick();
            int now = sim.TickNumber;
            if (ageAt < 0 && w.Age(p) >= 2) ageAt = now;
            if (fell < 0 && w.IsDefeated(p)) fell = now;
            if (otherFell < 0 && w.IsDefeated(1 - p)) otherFell = now;
            if (now == Ten) (waves10, g10, a10) = (ai.Board.WavesStarted, Count(w, p, gatherer), Army(w, p));
            if (now % Tps != 0) continue;
            var bs = w.Buildings;
            int halls = 0;
            for (int s = 0; s < bs.Capacity; s++)
            {
                if (!bs.Alive[s] || bs.Owner[s] != p || bs.UnderConstruction[s]) continue;
                built[bs.TypeId[s]] = true;
                if (bs.TypeId[s] == hall) halls++;
            }
            if (expanded < 0 && halls >= 2) expanded = now;
            for (int k = 0; k < ageTwoUnits.Length; k++) if (!fielded[k] && Count(w, p, ageTwoUnits[k]) > 0) fielded[k] = true;
        }
        Assert.Equal(Fifteen, sim.TickNumber);
        string never = string.Join(",", kinds.Where(k => !built[k]).Select(k => data.Buildings[k].Key.Replace("teblor_", "")));
        return new Row(variant, seed, map, opponent, p, ageAt, waves10, ai.Board.WavesStarted, expanded, never == "" ? "-" : never,
            built[breakers], built[shamans], built[stables], w.TechResearchedTick(p, anoint), fielded, fell, otherFell, g10, Count(w, p, gatherer), a10, Army(w, p));
    }

    private static int Count(World w, int player, int type)
    {
        var u = w.Units;
        int n = 0;
        for (int s = 0; s < u.Capacity; s++) if (u.Alive[s] && u.Owner[s] == player && u.TypeId[s] == type) n++;
        return n;
    }

    private static int Army(World w, int player)
    {
        var u = w.Units;
        int half = 0;
        for (int s = 0; s < u.Capacity; s++)
            if (u.Alive[s] && u.Owner[s] == player && w.Data.Units[u.TypeId[s]].Slot != UnitSlot.Worker) half += w.Data.Units[u.TypeId[s]].HalfPop;
        return half;
    }

    private static List<Row> PlayAll(List<(string V, GameData D, string M, string O, ulong S, bool X)> jobs)
    {
        var rows = new Row[jobs.Count];
        var errors = new ConcurrentQueue<string>();
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, i =>
        {
            var j = jobs[i];
            try { rows[i] = Play(j.V, j.D, j.M, j.O, j.S, j.X); }
            catch (Exception e) { errors.Enqueue($"{j.V} {j.S} {j.M} {j.O}: {e}"); }
        });
        Assert.True(errors.IsEmpty, string.Join("\n", errors));
        return rows.ToList();
    }

    private static string T(int tick) => tick < 0 ? "never" : $"{tick / Tps / 60}:{tick / Tps % 60:00}";

    // ---------- print ----------

    private static string Kinds(Row r) => string.Join(",", AgeTwoUnits.Where((_, k) => r.Fielded[k]).Select(u => u.Replace("teblor_", "")));

    private void PrintRows(List<Row> rows)
    {
        output.WriteLine("| variant | seed | map | opp | seat | Age II | waves 10:00 / 15:00 | expanded | never built | Anointing | Age II kinds fielded | teblor defeated | opponent defeated | Gatherers 10:00 / 15:00 | army pop 10:00 / 15:00 |");
        output.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (Row r in rows)
            output.WriteLine($"| {r.Variant} | {r.Seed} | {r.Map} | {r.Opponent} | {r.Seat} | {T(r.AgeTwoAt)} | {r.Waves10} / {r.Waves15} | {T(r.ExpandedAt)} | {r.NeverBuilt} | "
                + $"{T(r.AnointedAt)} | {Kinds(r)} | {T(r.DefeatedAt)} | {T(r.OtherDefeatedAt)} | {r.Gatherers10} / {r.Gatherers15} | {r.Army10 / 2.0} / {r.Army15 / 2.0} |");
        output.WriteLine("");
    }

    private const string TallyHead = "| Age II 10:00 | wave 10:00 | target | Age II 15:00 | expanded | every kind | Breakers Hall | Shamans' Circle | Stables | Anointing | "
        + "Javelin Throwers / Shamans / Breakers / Destrier Riders / War-dogs fielded | defeated | Gatherers 15:00 (sum) | army pop 15:00 (sum) |";

    private static readonly string TallyRule = "| --- | --- " + string.Concat(Enumerable.Repeat("| --- ", 14)) + "|";

    private static string Tally(List<Row> g, string target)
    {
        string fielded = string.Join(" / ", AgeTwoUnits.Select((_, k) => g.Count(r => r.Fielded[k])));
        return $"| {g.Count(r => r.Age10)} | {g.Count(r => r.Wave10)} | {target} | {g.Count(r => r.Age15)} | {g.Count(r => r.ExpandedAt >= 0)} | "
            + $"{g.Count(r => r.NeverBuilt == "-")} | {g.Count(r => r.Breakers)} | {g.Count(r => r.Shamans)} | {g.Count(r => r.Stables)} | {g.Count(r => r.AnointedAt >= 0)} | "
            + $"{fielded} | {g.Count(r => r.Lost)} | {g.Sum(r => r.Gatherers15)} | {g.Sum(r => r.Army15) / 2.0} |";
    }

    private void PrintTally(List<Row> rows, string[] order)
    {
        output.WriteLine("Per variant and seed (8 pairings each). Target: Age II and a wave by 10:00 in at least 6 of 8.");
        output.WriteLine("| variant | seed " + TallyHead);
        output.WriteLine(TallyRule);
        foreach (string v in order)
            foreach (var g in rows.Where(r => r.Variant == v).GroupBy(r => r.Seed).OrderBy(g => g.Key))
            {
                List<Row> l = g.ToList();
                bool met = l.Count(r => r.Age10) >= 6 && l.Count(r => r.Wave10) >= 6;
                output.WriteLine($"| {v} | {g.Key} " + Tally(l, met ? "met" : "not met"));
            }
        output.WriteLine("");
        output.WriteLine("Per variant, every seed together (target: the seeds meeting it).");
        output.WriteLine("| variant | matches " + TallyHead);
        output.WriteLine(TallyRule);
        foreach (string v in order)
        {
            List<Row> g = rows.Where(r => r.Variant == v).ToList();
            int seeds = g.Select(r => r.Seed).Distinct().Count();
            int met = g.GroupBy(r => r.Seed).Count(s => s.Count(r => r.Age10) >= 6 && s.Count(r => r.Wave10) >= 6);
            output.WriteLine($"| {v} | {g.Count} " + Tally(g, $"{met} of {seeds} seeds"));
        }
        output.WriteLine("");
    }

    /// <summary>Against <paramref name="baseline"/>, pairing by pairing: those <paramref name="v"/> loses (Age II by 10:00, a wave by 10:00, not defeated by 15:00) and the reverse.</summary>
    private void PrintDiff(List<Row> rows, string baseline, string v)
    {
        var lostAge = new List<string>();
        var lostWave = new List<string>();
        var worse = new List<string>();
        var better = new List<string>();
        foreach (Row a in rows.Where(r => r.Variant == baseline))
        {
            Row? b = rows.SingleOrDefault(r => r.Variant == v && r.Seed == a.Seed && r.Map == a.Map && r.Opponent == a.Opponent && r.Seat == a.Seat);
            if (b == null) continue;
            string at = $"seed {a.Seed} {a.Map} v {a.Opponent}";
            if (a.Age10 && !b.Age10) lostAge.Add(at);
            if (a.Wave10 && !b.Wave10) lostWave.Add(at);
            if (!a.Lost && b.Lost) worse.Add($"{at} ({T(b.DefeatedAt)})");
            if (a.Lost && !b.Lost) better.Add($"{at} ({baseline} {T(a.DefeatedAt)})");
        }
        output.WriteLine($"- {v} against {baseline}: Age II by 10:00 on {baseline} only {lostAge.Count} [{string.Join("; ", lostAge)}]; a wave by 10:00 on {baseline} only {lostWave.Count} [{string.Join("; ", lostWave)}]; "
            + $"defeated by 15:00 on {v} only {worse.Count} [{string.Join("; ", worse)}]; on {baseline} only {better.Count} [{string.Join("; ", better)}]");
    }

    private static List<(string, GameData, string, string, ulong, bool)> Jobs(IEnumerable<(string Name, GameData Data)> configs, bool swapped)
    {
        var jobs = new List<(string, GameData, string, string, ulong, bool)>();
        foreach (var c in configs)
            foreach (ulong seed in Seeds)
                foreach (string m in Maps)
                    foreach (string o in Opponents) jobs.Add((c.Name, c.Data, m, o, seed, swapped));
        return jobs;
    }

    // ---------- reports ----------

    /// <summary>
    /// Every fix-round-1 variant (V0, V4-V26) on seeds 1-5, eight pairings each, to 15:00: the per seed and per variant
    /// tallies on both horizons, each variant's pairing differences from V0, then the rows. Asserted: preconditions only.
    /// </summary>
    [Fact]
    public void Variants_Seeds1To5_To15Minutes_Report()
    {
        var variants = Variants();
        var jobs = Jobs(variants.Select(v => (v.Name, TeblorOpeningVariantsReportTests.DataWith(v.Opening))), false);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        List<Row> rows = PlayAll(jobs);
        output.WriteLine($"D29 fix round 1: {jobs.Count} matches to {Fifteen} ticks in {clock.Elapsed.TotalSeconds:0} s (teblor seat 0 v malazan, seat 1 v whirlwind)");
        string[] order = variants.Select(v => v.Name).ToArray();
        PrintTally(rows, order);
        foreach (string v in order) if (v != "V0") PrintDiff(rows, "V0", v);
        output.WriteLine("");
        PrintRows(rows);
        Assert.Equal(variants.Length * Seeds.Length * 8, rows.Count);
        Assert.All(rows, r => Assert.Equal(r.Opponent == "malazan" ? 0 : 1, r.Seat));
    }

    /// <summary>
    /// The hand-over beside the shipped opening on both seat orders (seeds 1-5, 80 matches a seat order): tallies, pairing
    /// differences and rows. The hand-over is read from its fixture file, so this is what would land.
    /// </summary>
    [Fact]
    public void HandOverAndShipped_BothSeatOrders_To15Minutes_Report()
    {
        var configs = new (string, GameData)[]
        {
            ("V0", TeblorOpeningVariantsReportTests.DataWith(TeblorOpeningVariantsReportTests.Shipped())),
            ("hand-over", TeblorOpeningVariantsReportTests.DataWith(HandOver())),
        };
        foreach (bool swapped in new[] { false, true })
        {
            List<Row> rows = PlayAll(Jobs(configs, swapped));
            output.WriteLine(swapped ? "Seats swapped (teblor seat 1 v malazan, seat 0 v whirlwind):" : "QA's seats (teblor seat 0 v malazan, seat 1 v whirlwind):");
            PrintTally(rows, new[] { "V0", "hand-over" });
            PrintDiff(rows, "V0", "hand-over");
            output.WriteLine("");
            PrintRows(rows);
            Assert.Equal(80, rows.Count);
            Assert.All(rows, r => Assert.Equal((r.Opponent == "malazan") != swapped ? 0 : 1, r.Seat));
        }
    }

    /// <summary>
    /// BUG-1301: the requested per-opening reserve share, approximated by the global <c>rules.json</c> <c>ai.reserveArmyShare</c>
    /// (shipped 1) on a data copy. A proxy and an upper bound: it lowers every seat's gate, the opponent's too. The shipped
    /// opening and the hand-over at shares 1 (control), 0.75 and 0.5; seeds 1-5, QA's seats, to 15:00.
    /// </summary>
    [Fact]
    public void ReserveShareProxy_ShippedAndHandOver_To15Minutes_Report()
    {
        static GameData Make(JsonObject opening, double share) => AiScenes.DataWith(dir =>
        {
            dir.EditJson("factions/teblor/ai.json", root => root["openings"]![0] = JsonNode.Parse(opening.ToJsonString()));
            dir.EditJson("common/rules.json", root => root["ai"]!["reserveArmyShare"] = share);
        });
        var configs = new List<(string, GameData)>();
        foreach (double share in new[] { 1.0, 0.75, 0.5 })
        {
            configs.Add(($"V0 share {share}", Make(TeblorOpeningVariantsReportTests.Shipped(), share)));
            configs.Add(($"hand-over share {share}", Make(HandOver(), share)));
        }
        List<Row> rows = PlayAll(Jobs(configs, false));
        output.WriteLine("BUG-1301 proxy: rules.json ai.reserveArmyShare on a data copy (every seat's gate moves, the opponent's too).");
        string[] order = configs.Select(c => c.Item1).ToArray();
        PrintTally(rows, order);
        foreach (string v in order) if (v != "V0 share 1") PrintDiff(rows, "V0 share 1", v);
        PrintDiff(rows, "hand-over share 1", "hand-over share 0.5");
        output.WriteLine("");
        PrintRows(rows);
        Assert.Equal(configs.Count * 40, rows.Count);
    }
}
