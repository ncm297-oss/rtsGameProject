using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA of D37 (BUG-1612, rule 75: before agreeing with the Normal result, up to three full Normal rows of QA's own on lever
/// values the developer's 39 orders did not put together). Each is M1RS (the order handed to D38) with one more lever moved:
/// QR1 the War-dog step through the reserve as well (Rider + War-dog + Shaman, a switch set none of the 39 carries), QR2 the
/// Shaman train step due at 10:00 instead of 9:00 (the Shaman step's own time was never varied), QR3 the Anointing step moved
/// just ahead of the Breaker step (that position was tried only on RSC, as RSCAB). Same row as the developer's (<see cref="D34TeblorStepOrderReportTests.Play"/>), judged against
/// <see cref="DH10TeblorSwitchReportTests.Bar"/>. A measurement: only preconditions asserted.
/// <c>QA_D37_VARIANTS</c>, <c>QA_D37_SEEDS</c> (default 1-8: the full 64 Normal seats), <c>QA_D37_THREADS</c> (cap 6).
/// </summary>
[Trait("Category", "Stress")]
public class D37TeblorNormalOrderQaStressTests(ITestOutputHelper output)
{
    private static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

    private static JsonObject Step(JsonObject o, (string What, string? Id) s) =>
        o["buildOrder"]!.AsArray().Select(n => n!.AsObject()).Single(n => (string)n["do"]! == s.What && (string?)n["id"] == s.Id);

    private static JsonObject Edit(JsonObject o, Action<JsonObject> edit)
    {
        JsonObject c = JsonNode.Parse(o.ToJsonString())!.AsObject();
        edit(c);
        return c;
    }

    internal static (string Name, string What, JsonObject Opening)[] AllVariants()
    {
        JsonObject m1rs = D37TeblorNormalOrderReportTests.AllVariants().Single(v => v.Name == "M1RS").Opening;
        return new[]
        {
            ("QR1", "M1RS with throughReserve on the War-dog step too (Rider + War-dog + Shaman)", Edit(m1rs, c => Step(c, DH10TeblorSwitchReportTests.Dog)["throughReserve"] = true)),
            ("QR2", "M1RS with the Shaman train step due at 10:00", Edit(m1rs, c => Step(c, DH10TeblorSwitchReportTests.Shamans)["at"] = new JsonObject { ["time"] = 600 })),
            ("QR3", "M1RS with the Anointing step moved just ahead of the Breaker step", Edit(m1rs, c =>
            {
                JsonArray order = c["buildOrder"]!.AsArray();
                JsonObject anoint = Step(c, DH10TeblorSwitchReportTests.Anointing);
                order.Remove(anoint);
                order.Insert(order.IndexOf(Step(c, DH10TeblorSwitchReportTests.Breaker)), anoint);
            })),
        };
    }

    /// <summary>QA's orders load as the Teblor standard, keep M1RS's steps as a multiset, never switch the expand step.</summary>
    [Fact]
    public void QA_EveryOrder_Loads_AndKeepsM1RSsSteps()
    {
        JsonObject m1rs = D37TeblorNormalOrderReportTests.AllVariants().Single(v => v.Name == "M1RS").Opening;
        static string Steps(JsonObject o) => string.Join(", ", o["buildOrder"]!.AsArray().Select(s => $"{(string)s!["do"]!} {(string?)s["id"]}").OrderBy(k => k, StringComparer.Ordinal));
        foreach (var v in AllVariants())
        {
            GameData d = TeblorOpeningVariantsReportTests.DataWith(v.Opening);
            Assert.Equal(m1rs["buildOrder"]!.AsArray().Count, d.Ai[d.FindFaction("teblor")].Openings[0].BuildOrder.Length);
            Assert.Equal(Steps(m1rs), Steps(v.Opening));
            Assert.DoesNotContain(v.Opening["buildOrder"]!.AsArray(), s => s!["noReserve"] != null);
        }
    }

    [Fact]
    public void QA_Normal_Row_Report()
    {
        string? only = Env("QA_D37_VARIANTS");
        var variants = AllVariants().Where(v => only == null || only.Split(',').Contains(v.Name)).ToArray();
        ulong[] seeds = (Env("QA_D37_SEEDS") ?? "1,2,3,4,5,6,7,8").Split(',').Select(ulong.Parse).ToArray();
        int threads = Math.Clamp(int.TryParse(Env("QA_D37_THREADS"), out int n) ? n : 6, 1, 6);
        string[] maps = { "generated", "raraku", "pale_hills", "vathar_crossing" };
        string[] opponents = { "malazan", "whirlwind" };
        var jobs = new List<(string V, GameData D, string M, string O, ulong S)>();
        foreach (var v in variants)
        {
            GameData d = TeblorOpeningVariantsReportTests.DataWith(v.Opening);
            output.WriteLine($"- {v.Name} ({d.ContentHash():X16}): {v.What}");
            foreach (ulong seed in seeds)
                foreach (string mp in maps)
                    foreach (string o in opponents) jobs.Add((v.Name, d, mp, o, seed));
        }
        var rows = new D34TeblorStepOrderReportTests.Seat[jobs.Count];
        var errors = new ConcurrentQueue<string>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = threads }, i =>
        {
            var j = jobs[i];
            try { rows[i] = D34TeblorStepOrderReportTests.Play(j.V, j.D, j.M, j.O, j.S, AiDifficulty.Normal); }
            catch (Exception e) { errors.Enqueue($"{j.V} {j.S} {j.M} {j.O}: {e}"); }
        });
        Assert.True(errors.IsEmpty, string.Join("\n", errors));
        output.WriteLine($"QA D37: {jobs.Count} matches in {clock.Elapsed.TotalSeconds:0} s on {threads} threads, seeds {string.Join(",", seeds)}");
        var bar = DH10TeblorSwitchReportTests.Bar(AiDifficulty.Normal);
        output.WriteLine("| order | defeated | 2nd Longhouse | Shaman | Breaker | Rider | War-dog | Anointing | misses | short |");
        foreach (var v in variants)
        {
            var g = rows.Where(r => r.Variant == v.Name).ToList();
            int misses = 0, shortBy = 0;
            string Cell(int value, int least, bool most = false)
            {
                bool miss = most ? value > least : value < least;
                if (miss) { misses++; shortBy += most ? value - least : least - value; }
                return $"{value}{(miss ? "!" : "")}";
            }
            output.WriteLine($"| {v.Name} | {Cell(g.Count(r => r.DefeatedAt >= 0), bar.MaxDefeated, true)} | {Cell(g.Count(r => r.HallAt >= 0), bar.Hall)} | "
                + $"{Cell(g.Count(r => r.Fielded[1]), bar.Shaman)} | {Cell(g.Count(r => r.Fielded[2]), bar.Breaker)} | {Cell(g.Count(r => r.Fielded[3]), bar.Rider)} | "
                + $"{Cell(g.Count(r => r.Fielded[4]), bar.Dog)} | {Cell(g.Count(r => r.AnointedAt >= 0), bar.Anointing)} | {misses} | {shortBy} | (of {g.Count})");
        }
    }
}
