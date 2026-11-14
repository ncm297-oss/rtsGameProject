using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Xunit.Abstractions;
using Row = Rts.Sim.Tests.Content.TeblorOpeningHorizonReportTests.Row;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D32 (the Teblor AI's second pass): the variants of the opening D31 shipped (D29's V24 with its own reserve share 0.25,
/// <see cref="D31"/>) tried for Shamans fielded and a second Longhouse by 15:00, each on the 64-match row of
/// <see cref="TeblorLandedShareReportTests"/> (D-H8's harness, <see cref="TeblorOpeningHorizonReportTests.Play"/>: Normal
/// against Normal to 15:00, the Teblor on seat 0 against the Malazan and on seat 1 against the Whirlwind, four maps, seeds
/// 1-8), and the expansion diagnosed think by think on the D31 opening (why the <c>expand</c> step is not sent). A
/// measurement (rule 48): only preconditions are asserted; the brief's four figures are read off the printed rows.
/// <c>D32_VARIANTS</c> (comma-separated names) runs a subset.
/// </summary>
[Trait("Category", "Stress")]
public class D32TeblorOpeningReportTests(ITestOutputHelper output)
{
    private const int Tps = SimConstants.TicksPerSecond;
    private const int Fifteen = 18_000;

    private static readonly string[] Maps = { "generated", "raraku", "pale_hills", "vathar_crossing" };
    private static readonly string[] Opponents = { "malazan", "whirlwind" };
    private static readonly ulong[] Seeds = { 1, 2, 3, 4, 5, 6, 7, 8 };

    /// <summary>
    /// The opening D31 shipped, word for word (the hand-over V24 with its own share 0.25 and V24's description), kept as
    /// <c>Fixtures/teblor_standard_d31.json</c> since the shipped file now holds D32's.
    /// </summary>
    internal static JsonObject D31() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.RepoRoot(), "sim", "Rts.Sim.Tests", "Content", "Fixtures", "teblor_standard_d31.json")))!.AsObject();

    private static JsonObject Copy(JsonObject o) => JsonNode.Parse(o.ToJsonString())!.AsObject();

    private static JsonArray Order(JsonObject o) => o["buildOrder"]!.AsArray();

    private static int IndexOf(JsonObject o, string what, string? id)
    {
        JsonArray order = Order(o);
        for (int i = 0; i < order.Count; i++)
            if ((string)order[i]!["do"]! == what && (string?)order[i]!["id"] == id) return i;
        throw new InvalidOperationException($"no {what} {id} step");
    }

    /// <summary>Moves the step that does <paramref name="what"/> to <paramref name="id"/> to sit just before the step <paramref name="beforeWhat"/> <paramref name="beforeId"/>.</summary>
    private static void MoveBefore(JsonObject o, string what, string? id, string beforeWhat, string? beforeId)
    {
        JsonArray order = Order(o);
        JsonNode step = order[IndexOf(o, what, id)]!;
        order.Remove(step);
        order.Insert(IndexOf(o, beforeWhat, beforeId), step);
    }

    /// <summary>The index of the <paramref name="nth"/> (0-based) train step of <paramref name="id"/>.</summary>
    private static int NthTrain(JsonObject o, string id, int nth)
    {
        JsonArray order = Order(o);
        for (int i = 0; i < order.Count; i++)
            if ((string)order[i]!["do"]! == "train" && (string?)order[i]!["id"] == id && nth-- == 0) return i;
        throw new InvalidOperationException($"no train {id} step {nth}");
    }

    private static void At(JsonObject o, string what, string? id, int time) => Order(o)[IndexOf(o, what, id)]!["at"] = new JsonObject { ["time"] = time };

    private static void InsertAfter(JsonObject o, string afterWhat, string? afterId, JsonObject step) => Order(o).Insert(IndexOf(o, afterWhat, afterId) + 1, step);

    private static JsonObject Train(string id, int count, int time) =>
        new() { ["at"] = new JsonObject { ["time"] = time }, ["do"] = "train", ["id"] = id, ["count"] = count };

    /// <summary>The variant D32 ships (<c>Fixtures/teblor_standard_d32.json</c>, <see cref="D32"/>).</summary>
    internal const string ShippedVariant = "X7";

    /// <summary>
    /// BUG-1390's fix, the words D32 ships for the opening: the brief's sentence narrowed to what the opening fields by 15:00
    /// on the 64-match row (Javelin Throwers and Shamans; no Breaker, Destrier Rider or War-dog in any match, D31's opening
    /// neither) plus the expansion. The variants keep D31's words: words do not play.
    /// </summary>
    internal const string Description = "War Lodge and Javelin Ground first, then Age II; four Warriors raid at 8 army pop, then Javelin Throwers and Shamans join them and a second Longhouse goes up.";

    /// <summary>The opening D32 ships, read from <c>Fixtures/teblor_standard_d32.json</c> (pinned to <c>ai.json</c> by <see cref="TeblorOpeningFixtureTests"/>).</summary>
    internal static JsonObject D32() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.RepoRoot(), "sim", "Rts.Sim.Tests", "Content", "Fixtures", "teblor_standard_d32.json")))!.AsObject();

    /// <summary>The variants <c>D32_VARIANTS</c> names (comma-separated), or all of <see cref="AllVariants"/>.</summary>
    internal static (string Name, string What, JsonObject Opening)[] Variants()
    {
        var list = AllVariants();
        string? only = Environment.GetEnvironmentVariable("D32_VARIANTS");
        if (string.IsNullOrEmpty(only)) return list;
        var keep = new HashSet<string>(only.Split(','));
        return list.Where(v => keep.Contains(v.Name)).ToArray();
    }

    /// <summary>Every variant D32 measured, each named by its edit to an earlier one (D31 the shipped opening at the diff base).</summary>
    internal static (string Name, string What, JsonObject Opening)[] AllVariants()
    {
        JsonObject d31 = D31();
        var list = new List<(string, string, JsonObject)> { ("D31", "the opening D31 shipped (V24, own share 0.25)", d31) };

        // The expansion: the expand step (8:00) ahead of the Age II buildings in file order, so it is the first to hold the reserve.
        JsonObject e1 = Copy(d31);
        MoveBefore(e1, "expand", null, "build", "teblor_bloodwood_carver");
        list.Add(("E1", "D31, the expand step moved before the Bloodwood Carver (file order)", e1));

        JsonObject e2 = Copy(e1);
        e2["attack"]!["reserveArmyShare"] = 0.0;
        list.Add(("E2", "E1, own reserve share 0", e2));

        // Shamans: the Circle first of the 8:00 buildings, and / or a train step for Shamans after it.
        JsonObject s1 = Copy(d31);
        MoveBefore(s1, "build", "teblor_shamans_circle", "build", "teblor_bloodwood_carver");
        list.Add(("S1", "D31, the Shamans' Circle moved before the Bloodwood Carver", s1));

        JsonObject s2 = Copy(d31);
        InsertAfter(s2, "build", "teblor_shamans_circle", Train("teblor_shaman", 2, 540));
        list.Add(("S2", "D31, a train step for 2 Shamans at 9:00 after the Circle", s2));

        JsonObject s3 = Copy(s1);
        InsertAfter(s3, "build", "teblor_shamans_circle", Train("teblor_shaman", 2, 540));
        list.Add(("S3", "S1 plus S2's train step", s3));

        JsonObject s4 = Copy(s3);
        Order(s4)[IndexOf(s4, "train", "teblor_shaman")]!["count"] = 1;
        list.Add(("S4", "S3 with 1 Shaman", s4));

        // Both.
        JsonObject c1 = Copy(s3);
        MoveBefore(c1, "expand", null, "build", "teblor_shamans_circle");
        list.Add(("C1", "S3 with the expand step before the Circle", c1));

        JsonObject c2 = Copy(s3);
        MoveBefore(c2, "expand", null, "build", "teblor_bloodwood_carver");
        list.Add(("C2", "S3 with the expand step after the Shaman step, before the Carver", c2));

        JsonObject c3 = Copy(c1);
        c3["attack"]!["reserveArmyShare"] = 0.0;
        list.Add(("C3", "C1, own reserve share 0", c3));

        JsonObject c4 = Copy(c2);
        MoveBefore(c4, "build", "teblor_breakers_hall", "build", "teblor_stables");
        At(c4, "build", "teblor_breakers_hall", 600);
        list.Add(("C4", "C2 with the Breakers Hall at 10:00, after the gatherers", c4));

        // Batch 2 (after the think-by-think diagnostic: the bank never holds a Longhouse's cost). Nothing else due beside the
        // expansion at 8:00: the Age II buildings at 10:00, the two researches after them.
        JsonObject x1 = Copy(e1);
        foreach (string b in new[] { "teblor_bloodwood_carver", "teblor_shamans_circle", "teblor_breakers_hall" }) At(x1, "build", b, 600);
        At(x1, "research", "teblor_blood_oil_anointing", 690);
        At(x1, "research", "armor_1", 720);
        list.Add(("X1", "E1 with the Carver, Circle and Breakers Hall at 10:00, the Anointing at 11:30 and Armor at 12:00", x1));

        JsonObject x2 = Copy(x1);
        InsertAfter(x2, "build", "teblor_shamans_circle", Train("teblor_shaman", 2, 600));
        list.Add(("X2", "X1 plus a train step for 2 Shamans at 10:00 after the Circle", x2));

        // More income for it: the eight Gatherers due at 8:00 instead of 10:00.
        JsonObject x3 = Copy(e1);
        Order(x3)[NthTrain(x3, "teblor_gatherer", 1)]!["at"] = new JsonObject { ["time"] = 480 };
        list.Add(("X3", "E1 with the eight more Gatherers due at 8:00", x3));

        JsonObject x4 = Copy(s2);
        MoveBefore(x4, "expand", null, "build", "teblor_bloodwood_carver");
        Order(x4)[NthTrain(x4, "teblor_gatherer", 1)]!["at"] = new JsonObject { ["time"] = 480 };
        list.Add(("X4", "S2 with the expand step before the Carver and the eight Gatherers at 8:00", x4));

        JsonObject x5 = Copy(x2);
        x5["attack"]!["reserveArmyShare"] = 0.0;
        list.Add(("X5", "X2, own reserve share 0", x5));

        JsonObject x6 = Copy(x2);
        Order(x6)[NthTrain(x6, "teblor_gatherer", 1)]!["at"] = new JsonObject { ["time"] = 480 };
        list.Add(("X6", "X2 with the eight Gatherers at 8:00", x6));

        // Batch 3: X2 (all four figures met) with the Shamans sooner.
        JsonObject x7 = Copy(x2);
        At(x7, "build", "teblor_shamans_circle", 540);
        At(x7, "train", "teblor_shaman", 540);
        list.Add(("X7", "X2 with the Circle and the Shaman step at 9:00", x7));

        JsonObject x8 = Copy(x2);
        MoveBefore(x8, "build", "teblor_shamans_circle", "build", "teblor_bloodwood_carver");
        MoveBefore(x8, "train", "teblor_shaman", "build", "teblor_bloodwood_carver");
        list.Add(("X8", "X2 with the Circle and the Shaman step first of the 10:00 steps (before the Carver)", x8));

        JsonObject x9 = Copy(x8);
        At(x9, "build", "teblor_bloodwood_carver", 660);
        At(x9, "build", "teblor_breakers_hall", 660);
        list.Add(("X9", "X8 with the Carver and Breakers Hall at 11:00", x9));

        return list.ToArray();
    }

    /// <summary>The variants' opening ids, steps and wave are valid: each loads as the Teblor's <c>standard</c> on a shipped copy.</summary>
    [Fact]
    public void EveryVariant_LoadsOnAShippedCopy()
    {
        foreach (var v in Variants())
        {
            GameData d = TeblorOpeningVariantsReportTests.DataWith(v.Opening);
            Assert.Equal("standard", d.Ai[d.FindFaction("teblor")].Openings[0].Id);
            Assert.Equal((float)(double)v.Opening["attack"]!["reserveArmyShare"]!, d.Ai[d.FindFaction("teblor")].Openings[0].ReserveArmyShare);
            Assert.Equal(v.Opening["buildOrder"]!.AsArray().Count, d.Ai[d.FindFaction("teblor")].Openings[0].BuildOrder.Length);
        }
    }

    private static bool Met(IEnumerable<Row> seed) => seed.Count(r => r.Age10) >= 6 && seed.Count(r => r.Wave10) >= 6;

    /// <summary>The variants on the 64-match row: the brief's four figures, then D30's columns and the per-seed cells.</summary>
    [Fact]
    public void Variants_64MatchRow_Report()
    {
        var variants = Variants();
        var jobs = new List<(string, GameData, string, string, ulong, bool)>();
        foreach (var v in variants)
        {
            GameData d = TeblorOpeningVariantsReportTests.DataWith(v.Opening);
            foreach (ulong seed in Seeds)
                foreach (string m in Maps)
                    foreach (string o in Opponents) jobs.Add((v.Name, d, m, o, seed, false));
        }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        List<Row> rows = TeblorOpeningHorizonReportTests.PlayAll(jobs);
        Assert.Equal(variants.Length * 64, rows.Count);
        output.WriteLine($"D32: {jobs.Count} matches to 15:00 in {clock.Elapsed.TotalSeconds:0} s (seeds 1-8; four maps; Normal v Normal)");
        output.WriteLine("");
        output.WriteLine("| variant | change | seeds met (of 8) | defeated by 15:00 (of 64) | matches with a Shaman | matches with a 2nd Longhouse by 15:00 | Age II by 10:00 | wave by 10:00 | every kind | Anointing | Shamans' Circle | army pop at 15:00 (a match, mean) | casts landed (total) | matches with a cast | Javelin Throwers / Shamans / Breakers / Destrier Riders / War-dogs fielded (matches) |");
        output.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        int shaman = 1; // TeblorOpeningHorizonReportTests.AgeTwoUnits: javelin, shaman, breaker, rider, dog
        foreach (var v in variants)
        {
            List<Row> g = rows.Where(r => r.Variant == v.Name).ToList();
            output.WriteLine($"| {v.Name} | {v.What} | {g.GroupBy(r => r.Seed).Count(Met)} | {g.Count(r => r.Lost)} | {g.Count(r => r.Fielded[shaman])} | {g.Count(r => r.ExpandedAt >= 0)} | "
                + $"{g.Count(r => r.Age10)} | {g.Count(r => r.Wave10)} | {g.Count(r => r.NeverBuilt == "-")} | {g.Count(r => r.AnointedAt >= 0)} | {g.Count(r => r.Shamans)} | "
                + $"{g.Average(r => r.Army15) / 2.0:0.0} | {g.Sum(r => r.CastsLanded)} | {g.Count(r => r.CastsLanded > 0)} | {string.Join(" / ", Enumerable.Range(0, 5).Select(k => g.Count(r => r.Fielded[k])))} |");
        }
        output.WriteLine("");
        output.WriteLine("Per seed (8 pairings each): Age II 10:00 / wave 10:00 / defeated 15:00.");
        output.WriteLine("| variant | " + string.Join(" | ", Seeds.Select(s => $"seed {s}")) + " |");
        output.WriteLine("| --- " + string.Concat(Seeds.Select(_ => "| --- ")) + "|");
        foreach (var v in variants)
        {
            var cells = Seeds.Select(s =>
            {
                List<Row> g = rows.Where(r => r.Variant == v.Name && r.Seed == s).ToList();
                return $"{g.Count(r => r.Age10)} / {g.Count(r => r.Wave10)} / {g.Count(r => r.Lost)}";
            });
            output.WriteLine($"| {v.Name} | " + string.Join(" | ", cells) + " |");
        }
        output.WriteLine("");
        foreach (var v in variants)
        {
            List<Row> g = rows.Where(r => r.Variant == v.Name).ToList();
            string lost = string.Join(", ", g.Where(r => r.Lost).OrderBy(r => r.DefeatedAt).Select(r => $"s{r.Seed} {r.Map} v {r.Opponent} {TeblorOpeningHorizonReportTests.T(r.DefeatedAt)}"));
            string halls = string.Join(", ", g.Where(r => r.ExpandedAt >= 0).OrderBy(r => r.ExpandedAt).Select(r => $"s{r.Seed} {r.Map} v {r.Opponent} {TeblorOpeningHorizonReportTests.T(r.ExpandedAt)}"));
            output.WriteLine($"- {v.Name}: defeated [{lost}]; 2nd Longhouse [{halls}]");
        }
    }

    // ---------- the expansion, think by think ----------

    /// <summary>One match's expansion figures for the Teblor seat, counted at each think from <c>expandAfter</c> while the <c>expand</c> step is not done.</summary>
    internal sealed class Diag
    {
        public string Variant = "", Map = "", Opponent = "";
        public ulong Seed;
        public int DueThinks, MineKnown, MayReserve, HeldByExpand, HeldByOther, NoReserve, Affordable, Defending, DoneAt = -1, HallAt = -1, DefeatedAt = -1;
        /// <summary>The first second a second Longhouse (a site too) stands; sites lost unfinished; a site standing unfinished at 15:00 and its build progress.</summary>
        public int SiteAt = -1, SitesLost, SiteProgress = -1;
        public readonly SortedDictionary<string, int> Holders = new(StringComparer.Ordinal);
    }

    private static Diag Diagnose(string variant, GameData data, string map, string opponent, ulong seed)
    {
        int teblor = data.FindFaction("teblor");
        PlayerSeat t = new(teblor, SeatController.Ai, AiDifficulty.Normal);
        PlayerSeat o = new(data.FindFaction(opponent), SeatController.Ai, AiDifficulty.Normal);
        bool first = opponent == "malazan";
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
        AiPlayer ai = sim.AiOf(p)!;
        var steps = ai.Opening.BuildOrder;
        int e = -1;
        for (int i = 0; i < steps.Length && e < 0; i++) if (steps[i].Action == AiStepAction.Expand) e = i;
        Assert.True(e >= 0);
        int hall = data.FindBuilding("teblor_longhouse");
        BuildingDef hd = data.Buildings[hall];
        var d = new Diag { Variant = variant, Map = map, Opponent = opponent, Seed = seed };
        ai.ThoughtForTests = a =>
        {
            if (a.Now < a.Knobs.ExpandAfterTicks) return;
            if (a.Board.StepDone[e])
            {
                if (d.DoneAt < 0) d.DoneAt = a.Now;
                return;
            }
            d.DueThinks++;
            if (BuildOrderExecutor.ExpansionMine(a) >= 0) d.MineKnown++;
            if (a.Board.Defending) d.Defending++;
            if (a.MayReserve()) d.MayReserve++;
            if (a.View.Gold >= hd.CostGold && a.View.Wood >= hd.CostWood) d.Affordable++;
            if (a.ReserveStep == e) d.HeldByExpand++;
            else if (a.ReserveStep >= 0)
            {
                d.HeldByOther++;
                AiStep s = steps[a.ReserveStep];
                string key = $"{a.ReserveStep + 1} {s.Action.ToString().ToLowerInvariant()} {(s.Action == AiStepAction.Research ? data.Techs[s.Target].Key : data.Buildings[s.Target].Key)}";
                d.Holders[key] = d.Holders.TryGetValue(key, out int n) ? n + 1 : 1;
            }
            else d.NoReserve++;
        };
        int sitesWas = 0, hallsWas = 0;
        while (sim.TickNumber < Fifteen)
        {
            sim.Tick();
            int now = sim.TickNumber;
            if (d.DefeatedAt < 0 && w.IsDefeated(p)) d.DefeatedAt = now;
            if (now % Tps != 0) continue;
            var bs = w.Buildings;
            int halls = 0, sites = 0, progress = -1;
            for (int s = 0; s < bs.Capacity; s++)
            {
                if (!bs.Alive[s] || bs.Owner[s] != p || bs.TypeId[s] != hall) continue;
                if (!bs.UnderConstruction[s]) halls++;
                else
                {
                    sites++;
                    progress = Math.Max(progress, bs.Work[s]);
                }
            }
            if (d.HallAt < 0 && halls >= 2) d.HallAt = now;
            if (d.SiteAt < 0 && halls + sites >= 2) d.SiteAt = now;
            if (sites < sitesWas && halls <= hallsWas) d.SitesLost += sitesWas - sites;
            (sitesWas, hallsWas) = (sites, halls);
            d.SiteProgress = progress;
        }
        return d;
    }

    /// <summary>
    /// Why the <c>expand</c> step waits: at each Teblor think from Normal's <c>expandAfter</c> until the step is done, whether a
    /// free gold mine is known (<see cref="BuildOrderExecutor.ExpansionMine"/>), whether the AI may reserve, whether the bank
    /// holds a Longhouse's cost, and which step holds the think's reserve. <c>D32_DIAG</c> names the variants (default D31).
    /// </summary>
    [Fact]
    public void Expansion_ThinkByThink_Report()
    {
        string names = Environment.GetEnvironmentVariable("D32_DIAG") is { Length: > 0 } s ? s : "D31";
        var keep = new HashSet<string>(names.Split(','));
        var all = AllVariants().Where(v => keep.Contains(v.Name)).ToArray();
        var jobs = new List<(string V, GameData D, string M, string O, ulong S)>();
        foreach (var v in all)
        {
            GameData data = TeblorOpeningVariantsReportTests.DataWith(v.Opening);
            foreach (ulong seed in Seeds)
                foreach (string m in Maps)
                    foreach (string o in Opponents) jobs.Add((v.Name, data, m, o, seed));
        }
        var rows = new Diag[jobs.Count];
        var errors = new ConcurrentQueue<string>();
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, i =>
        {
            var j = jobs[i];
            try { rows[i] = Diagnose(j.V, j.D, j.M, j.O, j.S); }
            catch (Exception ex) { errors.Enqueue($"{j.V} {j.S} {j.M} {j.O}: {ex}"); }
        });
        Assert.True(errors.IsEmpty, string.Join("\n", errors));
        output.WriteLine("Thinks from expandAfter while the expand step is not done (summed over the matches):");
        output.WriteLine("| variant | matches | step sent | 2nd hall finished | due thinks | free mine known | may reserve | defending | bank >= a Longhouse | reserve: the expand | reserve: another step | no reserve |");
        output.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var v in all)
        {
            var g = rows.Where(r => r.Variant == v.Name).ToList();
            output.WriteLine($"| {v.Name} | {g.Count} | {g.Count(r => r.DoneAt >= 0)} | {g.Count(r => r.HallAt >= 0)} | {g.Sum(r => r.DueThinks)} | {g.Sum(r => r.MineKnown)} | {g.Sum(r => r.MayReserve)} | "
                + $"{g.Sum(r => r.Defending)} | {g.Sum(r => r.Affordable)} | {g.Sum(r => r.HeldByExpand)} | {g.Sum(r => r.HeldByOther)} | {g.Sum(r => r.NoReserve)} |");
            var holders = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var r in g)
                foreach (var kv in r.Holders) holders[kv.Key] = holders.TryGetValue(kv.Key, out int n) ? n + kv.Value : kv.Value;
            output.WriteLine($"  reserve holders: {string.Join("; ", holders.Select(kv => $"step {kv.Key}: {kv.Value}"))}");
        }
        output.WriteLine("");
        output.WriteLine("| variant | seed | map | opp | due thinks | mine known | may reserve | bank >= hall | held by expand | held by other | sent | site placed | sites lost | site at 15:00 (progress) | 2nd hall | defeated |");
        output.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (Diag r in rows)
            output.WriteLine($"| {r.Variant} | {r.Seed} | {r.Map} | {r.Opponent} | {r.DueThinks} | {r.MineKnown} | {r.MayReserve} | {r.Affordable} | {r.HeldByExpand} | {r.HeldByOther} | "
                + $"{TeblorOpeningHorizonReportTests.T(r.DoneAt)} | {TeblorOpeningHorizonReportTests.T(r.SiteAt)} | {r.SitesLost} | {r.SiteProgress} | {TeblorOpeningHorizonReportTests.T(r.HallAt)} | {TeblorOpeningHorizonReportTests.T(r.DefeatedAt)} |");
    }
}
