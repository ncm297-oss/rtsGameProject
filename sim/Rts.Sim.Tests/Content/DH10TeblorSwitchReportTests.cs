using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D-H10 (BUG-1612's open half), a measurement (rule 48: only preconditions asserted). D34's Teblor <c>standard</c>
/// (<c>Fixtures/teblor_standard_d34.json</c>, "M", the shipped file at the task's base) against variants that add the M7-8
/// reserve switches (<c>throughReserve</c> on a train step, <c>noReserve</c> on a build / research step) and, for a few,
/// move or re-time the Carver and the Anointing. The row is D34's (<see cref="D34TeblorStepOrderReportTests.Play"/>: the
/// Teblor on seat 0 against the Malazan and on seat 1 against the Whirlwind, the same difficulty on both seats, four maps,
/// to 15:00). Each figure is printed beside the bar of docs/01 2026-11-24 ACCEPT (3)(c), a miss marked "!".
/// <c>DH10_VARIANTS</c> (comma-separated names), <c>DH10_DIFFS</c> (default Easy, Normal, Hard), <c>DH10_SEEDS</c> (default
/// 1, 2: the reduced screen; 1-8 is the full 64) and <c>DH10_THREADS</c> narrow a run. No variant puts <c>noReserve</c> on
/// the expand step (BUG-1663: that means "never expand"). Outcome (2026-11-25, teblor.md "The unit mix (BUG-1612,
/// D-H10)"): M's full rows give D34's 21 figures exactly; none of the eleven candidates meets the bar on the full rows, so
/// nothing ships (<see cref="ShippedVariant"/> is null); the nearest is A, six figures short.
/// </summary>
[Trait("Category", "Stress")]
public class DH10TeblorSwitchReportTests(ITestOutputHelper output)
{
    /// <summary>The variant D-H10 ships: none met the bar (teblor.md "The unit mix (BUG-1612, D-H10)").</summary>
    internal const string? ShippedVariant = null;

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

    private static JsonObject Copy(JsonObject o) => JsonNode.Parse(o.ToJsonString())!.AsObject();

    private static JsonArray Order(JsonObject o) => o["buildOrder"]!.AsArray();

    private static int IndexOf(JsonObject o, string what, string? id)
    {
        JsonArray order = Order(o);
        for (int i = 0; i < order.Count; i++)
            if ((string)order[i]!["do"]! == what && (string?)order[i]!["id"] == id) return i;
        throw new InvalidOperationException($"no {what} {id} step");
    }

    /// <summary>The step that does <paramref name="what"/> to <paramref name="id"/>; the first, or the last when <paramref name="last"/>.</summary>
    private static JsonObject Step(JsonObject o, string what, string? id, bool last = false)
    {
        JsonArray order = Order(o);
        for (int k = 0; k < order.Count; k++)
        {
            int i = last ? order.Count - 1 - k : k;
            if ((string)order[i]!["do"]! == what && (string?)order[i]!["id"] == id) return order[i]!.AsObject();
        }
        throw new InvalidOperationException($"no {what} {id} step");
    }

    /// <summary>Sets <paramref name="field"/> true on each named step (the first step of that action and id).</summary>
    private static JsonObject With(JsonObject o, string field, params (string What, string? Id)[] steps)
    {
        JsonObject c = Copy(o);
        foreach (var (what, id) in steps) Step(c, what, id)[field] = true;
        return c;
    }

    /// <summary>Moves the named steps, in the order given, to just before the step <paramref name="beforeWhat"/> <paramref name="beforeId"/>.</summary>
    private static void MoveBefore(JsonObject o, string beforeWhat, string? beforeId, params (string What, string? Id)[] steps)
    {
        JsonArray order = Order(o);
        foreach (var (what, id) in steps)
        {
            JsonNode step = order[IndexOf(o, what, id)]!;
            order.Remove(step);
            order.Insert(IndexOf(o, beforeWhat, beforeId), step);
        }
    }

    private static void At(JsonObject o, string what, string? id, int time) => Step(o, what, id)["at"] = new JsonObject { ["time"] = time };

    internal static readonly (string, string?) Rider = ("train", "teblor_destrier_rider");
    internal static readonly (string, string?) Dog = ("train", "teblor_war_dog");
    internal static readonly (string, string?) Shamans = ("train", "teblor_shaman");
    internal static readonly (string, string?) Breaker = ("train", "teblor_breaker");
    internal static readonly (string, string?) Carver = ("build", "teblor_bloodwood_carver");
    internal static readonly (string, string?) Anointing = ("research", "teblor_blood_oil_anointing");
    internal static readonly (string, string?) Armor = ("research", "armor_1");
    internal static readonly (string, string?) Stables = ("build", "teblor_stables");
    internal static readonly (string, string?) BreakersHall = ("build", "teblor_breakers_hall");
    internal static readonly (string, string?) Circle = ("build", "teblor_shamans_circle");

    /// <summary>
    /// Every variant D-H10 measured, each named by its edit to D34's opening ("M", the fixture). The brief's A / S / RS / AR /
    /// AN first, then the developer's own. Steps are never added, removed or re-counted; no description changes here.
    /// </summary>
    internal static (string Name, string What, JsonObject Opening)[] AllVariants()
    {
        JsonObject m = TeblorOpeningFixtureTests.D34Fixture();
        const string tr = "throughReserve", nr = "noReserve";
        var list = new List<(string, string, JsonObject)> { ("M", "D34's opening (the shipped file at the base)", m) };

        JsonObject a = With(m, tr, Rider, Dog, Shamans);
        list.Add(("A", "M with throughReserve on the Rider, War-dog and Shaman steps", a));
        list.Add(("S", "M with throughReserve on the Shaman step only", With(m, tr, Shamans)));
        list.Add(("RS", "M with throughReserve on the Rider and Shaman steps", With(m, tr, Rider, Shamans)));

        JsonObject ar = Copy(a);
        MoveBefore(ar, "train", "teblor_breaker", Carver, Anointing);
        list.Add(("AR", "A with the Carver and the Anointing moved ahead of the Breaker, Rider and War-dog train steps", ar));

        list.Add(("AN", "A with noReserve on the armor_1 research step", With(a, nr, Armor)));

        // The developer's own (up to six).
        JsonObject ar2 = Copy(a);
        At(ar2, "build", "teblor_bloodwood_carver", 540);
        At(ar2, "research", "teblor_blood_oil_anointing", 540);
        list.Add(("AT", "A with the Carver and the Anointing due at 9:00 (were 10:00), file order kept", ar2));

        list.Add(("AB", "A with throughReserve on the Breaker step too (every Age II train step but the Gatherers)", With(a, tr, Breaker)));

        list.Add(("ANS", "AN with noReserve on the Stables, the Breakers' Hall and the Shaman's Circle build steps too",
            With(With(a, nr, Armor), nr, Stables, BreakersHall, Circle)));

        JsonObject dn = With(m, tr, Dog, Shamans);
        list.Add(("DS", "M with throughReserve on the War-dog and Shaman steps (the Rider left to the reserve)", dn));

        // After the first full rows: A's Shamans are paid out of the money the 10:00 steps (the Anointing, the Breaker)
        // waited for, so pay them two minutes sooner; and Hard's Riders and War-dogs wait on a Stables due at 10:00.
        JsonObject ase = Copy(a);
        At(ase, "build", "teblor_shamans_circle", 480);
        At(ase, "train", "teblor_shaman", 480);
        list.Add(("ASE", "A with the Shamans' Circle and the Shaman step due at 8:00 (were 9:00), file order kept", ase));

        JsonObject ases = Copy(ase);
        At(ases, "build", "teblor_stables", 540);
        MoveBefore(ases, "expand", null, Stables);
        list.Add(("ASES", "ASE with the Stables due at 9:00 (was 10:00) and moved ahead of the expand step", ases));

        return list.ToArray();
    }

    internal static (string Name, string What, JsonObject Opening)[] Variants()
    {
        string? only = Env("DH10_VARIANTS");
        return AllVariants().Where(v => only == null || only.Split(',').Contains(v.Name)).ToArray();
    }

    /// <summary>
    /// The bar (docs/01 2026-11-24 ACCEPT (3)(c)), per difficulty: the least seats of 64 for Anointing / Rider / War-dog /
    /// Shaman / Breaker / 2nd Longhouse and the most defeated. -1 = not part of that difficulty's bar.
    /// </summary>
    internal static (int Anointing, int Rider, int Dog, int Shaman, int Breaker, int Hall, int MaxDefeated) Bar(AiDifficulty d) => d switch
    {
        AiDifficulty.Easy => (20, 20, 40, 25, 20, -1, 2),
        AiDifficulty.Normal => (17, 22, 37, 20, 12, 18, 10),
        AiDifficulty.Hard => (20, 40, 46, 22, 35, 18, 12),
        _ => throw new ArgumentOutOfRangeException(nameof(d)),
    };

    /// <summary>Every variant loads as the Teblor <c>standard</c> on a shipped copy, keeps M's steps (as a multiset) and no expand step carries <c>noReserve</c>.</summary>
    [Fact]
    public void EveryVariant_Loads_KeepsMsSteps_AndNeverSwitchesTheExpand()
    {
        JsonObject m = TeblorOpeningFixtureTests.D34Fixture();
        static string Steps(JsonObject o) => string.Join(", ", Order(o).Select(s => $"{(string)s!["do"]!} {(string?)s["id"]} x{(int?)s["count"] ?? 1}").OrderBy(k => k, StringComparer.Ordinal));
        foreach (var v in AllVariants())
        {
            GameData d = TeblorOpeningVariantsReportTests.DataWith(v.Opening);
            Assert.Equal(Order(m).Count, d.Ai[d.FindFaction("teblor")].Openings[0].BuildOrder.Length);
            Assert.Equal(Steps(m), Steps(v.Opening));
            foreach (string field in new[] { "id", "displayName", "description", "compositions", "attack" })
                Assert.True(JsonNode.DeepEquals(m[field], v.Opening[field]), $"{v.Name}: {field} differs from M");
            Assert.DoesNotContain(Order(v.Opening), s => (string)s!["do"]! == "expand" && s["noReserve"] != null);
            Assert.DoesNotContain(Order(v.Opening), s => s!["throughReserve"] is JsonValue f && !(bool)f);
        }
    }

    /// <summary>
    /// Each variant at each difficulty on the row: defeated / 2nd Longhouse / Shaman / Breaker / Rider / War-dog / Anointing
    /// (seats of the row), each beside its bar on a full row; a miss is marked "!".
    /// </summary>
    [Fact]
    public void Variants_Row_Report()
    {
        var variants = Variants();
        AiDifficulty[] diffs = (Env("DH10_DIFFS") ?? "Easy,Normal,Hard").Split(',').Select(Enum.Parse<AiDifficulty>).ToArray();
        ulong[] seeds = (Env("DH10_SEEDS") ?? "1,2").Split(',').Select(ulong.Parse).ToArray();
        int threads = int.TryParse(Env("DH10_THREADS"), out int n) && n > 0 ? n : Math.Max(1, Environment.ProcessorCount / 2);
        string[] maps = { "generated", "raraku", "pale_hills", "vathar_crossing" };
        string[] opponents = { "malazan", "whirlwind" };
        var jobs = new List<(string V, GameData D, string M, string O, ulong S, AiDifficulty X)>();
        var hashes = new Dictionary<string, ulong>();
        foreach (var v in variants)
        {
            GameData d = TeblorOpeningVariantsReportTests.DataWith(v.Opening);
            hashes[v.Name] = d.ContentHash();
            foreach (AiDifficulty x in diffs)
                foreach (ulong seed in seeds)
                    foreach (string m in maps)
                        foreach (string o in opponents) jobs.Add((v.Name, d, m, o, seed, x));
        }
        var rows = new D34TeblorStepOrderReportTests.Seat[jobs.Count];
        var errors = new ConcurrentQueue<string>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = threads }, i =>
        {
            var j = jobs[i];
            try { rows[i] = D34TeblorStepOrderReportTests.Play(j.V, j.D, j.M, j.O, j.S, j.X); }
            catch (Exception e) { errors.Enqueue($"{j.V} {j.X} {j.S} {j.M} {j.O}: {e}"); }
        });
        Assert.True(errors.IsEmpty, string.Join("\n", errors));
        bool full = seeds.Length == 8;
        output.WriteLine($"D-H10 BUG-1612: {jobs.Count} matches to 15:00 in {clock.Elapsed.TotalSeconds:0} s (seeds {string.Join(",", seeds)}, four maps, two opponents: "
            + $"{seeds.Length * 8} seats a row; {(full ? "full rows" : "REDUCED rows: the bar is not judged")})");
        foreach (var v in variants) output.WriteLine($"- {v.Name} ({hashes[v.Name]:X16}): {v.What}");
        output.WriteLine("");
        output.WriteLine("| variant | difficulty | defeated | 2nd Longhouse | Shaman | Breaker | Rider | War-dog | Anointing | misses |");
        output.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (AiDifficulty x in diffs)
            foreach (var v in variants)
            {
                var g = rows.Where(r => r.Variant == v.Name && r.Diff == x).ToList();
                var bar = Bar(x);
                int misses = 0;
                string Cell(int value, int least, bool most = false)
                {
                    if (least < 0) return $"{value}";
                    bool miss = most ? value > least : value < least;
                    if (miss) misses++;
                    return $"{value}{(miss ? "!" : "")} ({(most ? "<=" : ">=")}{least})";
                }
                string line = $"| {v.Name} | {x} | {Cell(g.Count(r => r.DefeatedAt >= 0), bar.MaxDefeated, most: true)} | {Cell(g.Count(r => r.HallAt >= 0), bar.Hall)} | "
                    + $"{Cell(g.Count(r => r.Fielded[1]), bar.Shaman)} | {Cell(g.Count(r => r.Fielded[2]), bar.Breaker)} | {Cell(g.Count(r => r.Fielded[3]), bar.Rider)} | "
                    + $"{Cell(g.Count(r => r.Fielded[4]), bar.Dog)} | {Cell(g.Count(r => r.AnointedAt >= 0), bar.Anointing)} | {(full ? $"{misses}" : $"({misses}, reduced)")} | (of {g.Count})";
                output.WriteLine(line);
            }
        output.WriteLine("");
        output.WriteLine("Per map (defeated / Shaman / Breaker / Rider / War-dog / Anointing):");
        foreach (AiDifficulty x in diffs)
            foreach (var v in variants)
                output.WriteLine($"| {v.Name} | {x} | " + string.Join(" | ", maps.Select(m =>
                {
                    var g = rows.Where(r => r.Variant == v.Name && r.Diff == x && r.Map == m).ToList();
                    return $"{m} {g.Count(r => r.DefeatedAt >= 0)}/{g.Count(r => r.Fielded[1])}/{g.Count(r => r.Fielded[2])}/{g.Count(r => r.Fielded[3])}/{g.Count(r => r.Fielded[4])}/{g.Count(r => r.AnointedAt >= 0)}";
                })) + " |");
    }
}
