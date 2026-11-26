using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA of D36 (BUG-1612, rule 75: before agreeing with "none meets the bar", try up to four full-row orders of QA's own and
/// re-run the developer's best row and the shipped row). QA's orders sit on a lever value the developer's 57 did not
/// screen: the Shaman train step moved <b>alone</b> (the Circle kept behind the expand step) to a place between the
/// 10:00 steps (ahead of the Rider, ahead of the War-dog, ahead of the Breakers' Hall), and RSC with the Carver at 9:30.
/// Same row as the developer's (<see cref="D34TeblorStepOrderReportTests.Play"/>), judged against
/// <see cref="DH10TeblorSwitchReportTests.Bar"/>. A measurement: only preconditions asserted.
/// <c>QA_D36_VARIANTS</c>, <c>QA_D36_DIFFS</c>, <c>QA_D36_SEEDS</c> (default 1, 2: reduced), <c>QA_D36_THREADS</c> (cap 6).
/// </summary>
[Trait("Category", "Stress")]
public class D36TeblorOrderQaStressTests(ITestOutputHelper output)
{
    private static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

    private static JsonObject Copy(JsonObject o) => JsonNode.Parse(o.ToJsonString())!.AsObject();

    private static JsonArray Order(JsonObject o) => o["buildOrder"]!.AsArray();

    private static int IndexOf(JsonObject o, (string What, string? Id) s)
    {
        JsonArray order = Order(o);
        for (int i = 0; i < order.Count; i++)
            if ((string)order[i]!["do"]! == s.What && (string?)order[i]!["id"] == s.Id) return i;
        throw new InvalidOperationException($"no {s.What} {s.Id} step");
    }

    private static JsonObject Step(JsonObject o, (string, string?) s) => Order(o)[IndexOf(o, s)]!.AsObject();

    private static JsonObject Edit(JsonObject o, Action<JsonObject> edit)
    {
        JsonObject c = Copy(o);
        edit(c);
        return c;
    }

    private static void MoveBefore(JsonObject o, (string, string?) before, (string, string?) step)
    {
        JsonArray order = Order(o);
        JsonNode n = order[IndexOf(o, step)]!;
        order.Remove(n);
        order.Insert(IndexOf(o, before), n);
    }

    private static readonly (string, string?) Rider = DH10TeblorSwitchReportTests.Rider, Dog = DH10TeblorSwitchReportTests.Dog,
        Shamans = DH10TeblorSwitchReportTests.Shamans, Carver = DH10TeblorSwitchReportTests.Carver,
        BreakersHall = DH10TeblorSwitchReportTests.BreakersHall;

    /// <summary>The developer's M and QRD1 (their own definitions) and QA's orders.</summary>
    internal static (string Name, string What, JsonObject Opening)[] AllVariants()
    {
        var dev = D36TeblorOrderSearchReportTests.AllVariants();
        JsonObject m = dev.Single(v => v.Name == "M").Opening, qrd1 = dev.Single(v => v.Name == "QRD1").Opening;
        return new[]
        {
            ("M", "the shipped order (developer's)", m),
            ("QRD1", "QA's QRD1 (developer's copy)", qrd1),
            ("QSR", "QRD1 with the Shaman step alone moved just ahead of the Rider step (Circle kept)", Edit(qrd1, c => MoveBefore(c, Rider, Shamans))),
            ("QSD", "QRD1 with the Shaman step alone moved just ahead of the War-dog step (Circle kept)", Edit(qrd1, c => MoveBefore(c, Dog, Shamans))),
            ("QSH", "QRD1 with the Shaman step alone moved just ahead of the Breakers' Hall (Circle kept)", Edit(qrd1, c => MoveBefore(c, BreakersHall, Shamans))),
            ("QSRS", "QSR with throughReserve on the Shaman step", Edit(qrd1, c =>
            {
                MoveBefore(c, Rider, Shamans);
                Step(c, Shamans)["throughReserve"] = true;
            })),
            ("QSCK", "RSC (Shaman just ahead of the Carver) with the Carver due at 9:30", Edit(qrd1, c =>
            {
                MoveBefore(c, Carver, Shamans);
                Step(c, Carver)["at"] = new JsonObject { ["time"] = 570 };
            })),
            ("QSDK", "QSD with the Carver due at 9:30", Edit(qrd1, c =>
            {
                MoveBefore(c, Dog, Shamans);
                Step(c, Carver)["at"] = new JsonObject { ["time"] = 570 };
            })),
        };
    }

    [Fact]
    public void QA_EveryOrder_Loads_KeepsTheSteps_AndNeverSwitchesTheExpand()
    {
        JsonObject m = TeblorOpeningFixtureTests.D34Fixture();
        Assert.True(JsonNode.DeepEquals(m, TeblorOpeningVariantsReportTests.Shipped()), "M is not the shipped opening");
        static string Steps(JsonObject o) => string.Join(", ", Order(o).Select(s => $"{(string)s!["do"]!} {(string?)s["id"]}").OrderBy(k => k, StringComparer.Ordinal));
        foreach (var v in AllVariants())
        {
            GameData d = TeblorOpeningVariantsReportTests.DataWith(v.Opening);
            Assert.Equal(Order(m).Count, d.Ai[d.FindFaction("teblor")].Openings[0].BuildOrder.Length);
            Assert.Equal(Steps(m), Steps(v.Opening));
            Assert.DoesNotContain(Order(v.Opening), s => (string)s!["do"]! == "expand" && s["noReserve"] != null);
        }
    }

    [Fact]
    public void QA_Orders_Row_Report()
    {
        string? only = Env("QA_D36_VARIANTS");
        var variants = AllVariants().Where(v => only == null || only.Split(',').Contains(v.Name)).ToArray();
        AiDifficulty[] diffs = (Env("QA_D36_DIFFS") ?? "Easy,Normal,Hard").Split(',').Select(Enum.Parse<AiDifficulty>).ToArray();
        ulong[] seeds = (Env("QA_D36_SEEDS") ?? "1,2").Split(',').Select(ulong.Parse).ToArray();
        int threads = Math.Clamp(int.TryParse(Env("QA_D36_THREADS"), out int n) ? n : 6, 1, 6);
        string[] maps = { "generated", "raraku", "pale_hills", "vathar_crossing" };
        string[] opponents = { "malazan", "whirlwind" };
        var jobs = new List<(string V, GameData D, string M, string O, ulong S, AiDifficulty X)>();
        foreach (var v in variants)
        {
            GameData d = TeblorOpeningVariantsReportTests.DataWith(v.Opening);
            output.WriteLine($"- {v.Name} ({d.ContentHash():X16}): {v.What}");
            foreach (AiDifficulty x in diffs)
                foreach (ulong seed in seeds)
                    foreach (string mp in maps)
                        foreach (string o in opponents) jobs.Add((v.Name, d, mp, o, seed, x));
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
        bool full = seeds.Length == 8 && seeds.Distinct().Count() == 8;
        output.WriteLine($"QA D36: {jobs.Count} matches in {clock.Elapsed.TotalSeconds:0} s on {threads} threads, seeds {string.Join(",", seeds)} ({(full ? "full rows" : "REDUCED")})");
        output.WriteLine("| order | difficulty | defeated | 2nd Longhouse | Shaman | Breaker | Rider | War-dog | Anointing | misses | short |");
        var totals = new Dictionary<string, (int Misses, int Short)>();
        foreach (var v in variants)
            foreach (AiDifficulty x in diffs)
            {
                var g = rows.Where(r => r.Variant == v.Name && r.Diff == x).ToList();
                var bar = DH10TeblorSwitchReportTests.Bar(x);
                int misses = 0, shortBy = 0;
                double scale = g.Count / 64.0;
                string Cell(int value, int least, bool most = false)
                {
                    if (least < 0) return $"{value}";
                    double limit = least * scale;
                    bool miss = most ? value > limit : value < limit;
                    if (miss)
                    {
                        misses++;
                        shortBy += (int)Math.Ceiling(most ? value - limit : limit - value);
                    }
                    return $"{value}{(miss ? "!" : "")}";
                }
                output.WriteLine($"| {v.Name} | {x} | {Cell(g.Count(r => r.DefeatedAt >= 0), bar.MaxDefeated, true)} | {Cell(g.Count(r => r.HallAt >= 0), bar.Hall)} | "
                    + $"{Cell(g.Count(r => r.Fielded[1]), bar.Shaman)} | {Cell(g.Count(r => r.Fielded[2]), bar.Breaker)} | {Cell(g.Count(r => r.Fielded[3]), bar.Rider)} | "
                    + $"{Cell(g.Count(r => r.Fielded[4]), bar.Dog)} | {Cell(g.Count(r => r.AnointedAt >= 0), bar.Anointing)} | {misses} | {shortBy} | (of {g.Count})");
                totals[v.Name] = totals.TryGetValue(v.Name, out var t) ? (t.Misses + misses, t.Short + shortBy) : (misses, shortBy);
            }
        output.WriteLine("Totals (figures missed, seats short):");
        foreach (var v in variants) output.WriteLine($"| {v.Name} | {totals[v.Name].Misses} | {totals[v.Name].Short} |");
    }
}
