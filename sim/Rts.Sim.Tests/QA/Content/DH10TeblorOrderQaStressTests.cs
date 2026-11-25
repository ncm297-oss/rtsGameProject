using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA of D-H10 (BUG-1612, rule 75: "try at least two orders of your own before agreeing that none meets the bar"). QA's own
/// Teblor <c>standard</c> orders beside the developer's M and A, on D34's row
/// (<see cref="D34TeblorStepOrderReportTests.Play"/>: the Teblor on seat 0 against the Malazan and on seat 1 against the
/// Whirlwind, the same difficulty on both seats, four maps, to 15:00), judged against
/// <see cref="DH10TeblorSwitchReportTests.Bar"/>. A measurement: only preconditions asserted. Prints the per-map figures
/// in the developer's format (so M and A can be compared map for map with the developer's log) and one line a seat.
/// <c>QA_DH10_VARIANTS</c>, <c>QA_DH10_DIFFS</c>, <c>QA_DH10_SEEDS</c> (default 1, 2: reduced), <c>QA_DH10_THREADS</c>.
/// </summary>
[Trait("Category", "Stress")]
public class DH10TeblorOrderQaStressTests(ITestOutputHelper output)
{
    private static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

    private static JsonObject Copy(JsonObject o) => JsonNode.Parse(o.ToJsonString())!.AsObject();

    private static JsonObject Step(JsonObject o, string what, string? id)
    {
        foreach (JsonNode? s in o["buildOrder"]!.AsArray())
            if ((string)s!["do"]! == what && (string?)s["id"] == id) return s.AsObject();
        throw new InvalidOperationException($"no {what} {id} step");
    }

    private static JsonObject Edit(JsonObject o, Action<JsonObject> edit)
    {
        JsonObject c = Copy(o);
        edit(c);
        return c;
    }

    /// <summary>M, A (the developer's, by their definitions) and QA's own orders.</summary>
    internal static (string Name, string What, JsonObject Opening)[] AllVariants()
    {
        var dev = DH10TeblorSwitchReportTests.AllVariants();
        JsonObject m = dev.Single(v => v.Name == "M").Opening, a = dev.Single(v => v.Name == "A").Opening;
        const string tr = "throughReserve";
        var list = new List<(string, string, JsonObject)>
        {
            ("M", "the shipped order", m),
            ("A", "the developer's nearest", a),
            ("QL11", "A with the Rider, War-dog and Shaman train steps due at 11:00 (were 10:00 / 10:00 / 9:00)", Edit(a, c =>
            {
                foreach (string id in new[] { "teblor_destrier_rider", "teblor_war_dog", "teblor_shaman" })
                    Step(c, "train", id)["at"] = new JsonObject { ["time"] = 660 };
            })),
            ("QL12", "A with the Rider, War-dog and Shaman train steps due at 12:00", Edit(a, c =>
            {
                foreach (string id in new[] { "teblor_destrier_rider", "teblor_war_dog", "teblor_shaman" })
                    Step(c, "train", id)["at"] = new JsonObject { ["time"] = 720 };
            })),
            ("QA1", "A with the Shaman step's count 2 -> 1", Edit(a, c => Step(c, "train", "teblor_shaman")["count"] = 1)),
            ("QRD", "M with throughReserve on the Rider and War-dog steps (not the Shaman)", Edit(m, c =>
            {
                Step(c, "train", "teblor_destrier_rider")[tr] = true;
                Step(c, "train", "teblor_war_dog")[tr] = true;
            })),
            ("QS1", "RS with the Shaman step's count 2 -> 1", Edit(m, c =>
            {
                Step(c, "train", "teblor_destrier_rider")[tr] = true;
                Step(c, "train", "teblor_shaman")[tr] = true;
                Step(c, "train", "teblor_shaman")["count"] = 1;
            })),
        };
        // After QA1's full rows (four misses, A's six): QA1's neighbours.
        JsonObject qa1 = list.Single(v => v.Item1 == "QA1").Item3;
        list.Add(("QA1T", "QA1 with the Carver and the Anointing due at 9:30 (were 10:00), file order kept", Edit(qa1, c =>
        {
            Step(c, "build", "teblor_bloodwood_carver")["at"] = new JsonObject { ["time"] = 570 };
            Step(c, "research", "teblor_blood_oil_anointing")["at"] = new JsonObject { ["time"] = 570 };
        })));
        list.Add(("QA1L", "QA1 with the Shaman step due at 10:00 (was 9:00)", Edit(qa1, c => Step(c, "train", "teblor_shaman")["at"] = new JsonObject { ["time"] = 600 })));
        list.Add(("QRD1", "QRD with the Shaman step's count 2 -> 1 (the Shaman left to the reserve)", Edit(list.Single(v => v.Item1 == "QRD").Item3,
            c => Step(c, "train", "teblor_shaman")["count"] = 1)));
        // After QRD1's full rows (four misses, all on Easy / Normal Shamans and Anointing; Hard met): QRD1's neighbours.
        JsonObject qrd1 = list.Single(v => v.Item1 == "QRD1").Item3;
        list.Add(("QRD1T", "QRD1 with the Carver and the Anointing due at 9:30 (were 10:00), file order kept", Edit(qrd1, c =>
        {
            Step(c, "build", "teblor_bloodwood_carver")["at"] = new JsonObject { ["time"] = 570 };
            Step(c, "research", "teblor_blood_oil_anointing")["at"] = new JsonObject { ["time"] = 570 };
        })));
        list.Add(("QRD1E", "QRD1 with the Shamans' Circle and the Shaman step due at 8:00 (were 9:00)", Edit(qrd1, c =>
        {
            Step(c, "build", "teblor_shamans_circle")["at"] = new JsonObject { ["time"] = 480 };
            Step(c, "train", "teblor_shaman")["at"] = new JsonObject { ["time"] = 480 };
        })));
        list.Add(("QRD1F", "QRD1 with the Shamans' Circle and the Shaman steps moved (in that order) just ahead of the Breaker train step", Edit(qrd1, c =>
        {
            JsonArray order = c["buildOrder"]!.AsArray();
            foreach (var (what, id) in new[] { ("build", "teblor_shamans_circle"), ("train", "teblor_shaman") })
            {
                JsonObject s = Step(c, what, id);
                order.Remove(s);
                order.Insert(order.IndexOf(Step(c, "train", "teblor_breaker")), s);
            }
        })));
        return list.ToArray();
    }

    /// <summary>Every QA order loads as the Teblor standard and none puts noReserve on the expand step (BUG-1663).</summary>
    [Fact]
    public void QA_EveryOrder_Loads_AndNeverSwitchesTheExpand()
    {
        foreach (var v in AllVariants())
        {
            GameData d = TeblorOpeningVariantsReportTests.DataWith(v.Opening);
            Assert.Equal(v.Opening["buildOrder"]!.AsArray().Count, d.Ai[d.FindFaction("teblor")].Openings[0].BuildOrder.Length);
            Assert.DoesNotContain(v.Opening["buildOrder"]!.AsArray(), s => (string)s!["do"]! == "expand" && s["noReserve"] != null);
        }
    }

    [Fact]
    public void QA_Orders_Row_Report()
    {
        string? only = Env("QA_DH10_VARIANTS");
        var variants = AllVariants().Where(v => only == null || only.Split(',').Contains(v.Name)).ToArray();
        AiDifficulty[] diffs = (Env("QA_DH10_DIFFS") ?? "Easy,Normal,Hard").Split(',').Select(Enum.Parse<AiDifficulty>).ToArray();
        ulong[] seeds = (Env("QA_DH10_SEEDS") ?? "1,2").Split(',').Select(ulong.Parse).ToArray();
        int threads = int.TryParse(Env("QA_DH10_THREADS"), out int n) && n > 0 ? n : Math.Max(1, Environment.ProcessorCount / 2);
        string[] maps = { "generated", "raraku", "pale_hills", "vathar_crossing" };
        string[] opponents = { "malazan", "whirlwind" };
        var jobs = new List<(string V, GameData D, string M, string O, ulong S, AiDifficulty X)>();
        foreach (var v in variants)
        {
            GameData d = TeblorOpeningVariantsReportTests.DataWith(v.Opening);
            output.WriteLine($"- {v.Name} ({d.ContentHash():X16}): {v.What}");
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
        output.WriteLine($"QA D-H10: {jobs.Count} matches in {clock.Elapsed.TotalSeconds:0} s, seeds {string.Join(",", seeds)} ({(full ? "full rows" : "REDUCED")})");
        output.WriteLine("| variant | difficulty | defeated | 2nd Longhouse | Shaman | Breaker | Rider | War-dog | Anointing | misses |");
        foreach (AiDifficulty x in diffs)
            foreach (var v in variants)
            {
                var g = rows.Where(r => r.Variant == v.Name && r.Diff == x).ToList();
                var bar = DH10TeblorSwitchReportTests.Bar(x);
                int misses = 0;
                string Cell(int value, int least, bool most = false)
                {
                    if (least < 0) return $"{value}";
                    bool miss = most ? value > least : value < least;
                    if (miss) misses++;
                    return $"{value}{(miss ? "!" : "")}";
                }
                string line = $"| {v.Name} | {x} | {Cell(g.Count(r => r.DefeatedAt >= 0), bar.MaxDefeated, true)} | {Cell(g.Count(r => r.HallAt >= 0), bar.Hall)} | "
                    + $"{Cell(g.Count(r => r.Fielded[1]), bar.Shaman)} | {Cell(g.Count(r => r.Fielded[2]), bar.Breaker)} | {Cell(g.Count(r => r.Fielded[3]), bar.Rider)} | "
                    + $"{Cell(g.Count(r => r.Fielded[4]), bar.Dog)} | {Cell(g.Count(r => r.AnointedAt >= 0), bar.Anointing)} | {misses}{(full ? "" : " (reduced)")} |";
                output.WriteLine(line);
            }
        output.WriteLine("Per map (defeated / Shaman / Breaker / Rider / War-dog / Anointing):");
        foreach (AiDifficulty x in diffs)
            foreach (var v in variants)
                output.WriteLine($"| {v.Name} | {x} | " + string.Join(" | ", maps.Select(m =>
                {
                    var g = rows.Where(r => r.Variant == v.Name && r.Diff == x && r.Map == m).ToList();
                    return $"{m} {g.Count(r => r.DefeatedAt >= 0)}/{g.Count(r => r.Fielded[1])}/{g.Count(r => r.Fielded[2])}/{g.Count(r => r.Fielded[3])}/{g.Count(r => r.Fielded[4])}/{g.Count(r => r.AnointedAt >= 0)}";
                })) + " |");
        output.WriteLine("Seats (variant diff seed map opponent: defeatedAt hallAt anointedAt fielded[jav,sha,bre,rid,dog]):");
        foreach (var r in rows.OrderBy(r => r.Variant, StringComparer.Ordinal).ThenBy(r => r.Diff).ThenBy(r => r.Seed).ThenBy(r => r.Map, StringComparer.Ordinal).ThenBy(r => r.Opponent, StringComparer.Ordinal))
            output.WriteLine($"{r.Variant} {r.Diff} {r.Seed} {r.Map} {r.Opponent}: {r.DefeatedAt} {r.HallAt} {r.AnointedAt} {string.Join("", r.Fielded.Select(f => f ? '1' : '0'))}");
    }
}
