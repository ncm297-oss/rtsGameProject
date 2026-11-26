using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA D34 (session 2026-11-18-0926), a measurement (rule 48: only preconditions asserted). BUG-1612: QA's own step orders
/// of the Teblor <c>standard</c>, played in QA's own loop (the counting of <see cref="D33TeblorRowQaStressTests"/>, not
/// the developer's <c>Content/D34TeblorStepOrderReportTests.Play</c>), to check that the "none ships" fallback was not
/// taken where an order would have done. Each variant edits the shipped opening (the D33 fixture) by order and
/// <c>at.time</c> only:
/// <list type="bullet">
/// <item>Q1: the order D34 names but did not try: the Bloodwood Carver and the Anointing (due 10:00) just after the three
/// train steps, ahead of the Breakers Hall and the expansion.</item>
/// <item>Q2: G5 with the Breaker's train step kept ahead of the Carver and the Anointing (the step G5 starves), so
/// the Breaker spends before the Anointing holds the reserve; the Rider and War-dog steps follow.</item>
/// <item>Q3: Q1 with the Anointing kept at its shipped 11:30.</item>
/// </list>
/// <c>D34QA_VARIANTS</c>, <c>D34QA_DIFFS</c> (default Easy, Normal, Hard) and <c>D34QA_SEEDS</c> (default 1-8, the full 64)
/// narrow a run. Rows: defeated / 2nd Longhouse / Shaman / Breaker / Rider / War-dog / Anointing.
/// Outcome (2026-11-18, 768 matches, 1 h 23 m shared): M equals the developer's and QA D33's rows to the match; Q2 meets
/// the brief's criterion 2 bar (Easy Anointing 28; Normal 9 / 23 / 15 / 14 / 22 / 37; Hard Breaker / Rider / War-dog
/// 39 / 33 / 46), Q1 and Q3 miss on Easy's Anointing (18, 17). BUG-1641.
/// D34 fix round 1: Q2 ships (<c>Content/Fixtures/teblor_standard_d34.json</c>), so M is no longer the shipped file; it is
/// played as D33's opening on a shipped copy and still hashes to <see cref="D33QaTests.D33Hash"/>.
/// </summary>
[Trait("Category", "Stress")]
public class D34OrderQaStressTests(ITestOutputHelper output)
{
    private const int Tps = SimConstants.TicksPerSecond;
    private const int Fifteen = 18_000;
    private static readonly string[] Maps = { "generated", "raraku", "pale_hills", "vathar_crossing" };
    private static readonly string[] AgeTwoUnits = { "teblor_javelin_thrower", "teblor_shaman", "teblor_breaker", "teblor_destrier_rider", "teblor_war_dog" };

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

    private static JsonArray Steps(JsonObject o) => o["buildOrder"]!.AsArray();

    private static int Find(JsonObject o, string what, string id)
    {
        JsonArray s = Steps(o);
        for (int i = 0; i < s.Count; i++)
            if ((string)s[i]!["do"]! == what && (string?)s[i]!["id"] == id) return i;
        throw new InvalidOperationException($"no {what} {id}");
    }

    /// <summary>Takes the named step out and puts it back at <paramref name="index"/> of the list without it.</summary>
    private static void Put(JsonObject o, string what, string id, int index)
    {
        JsonArray s = Steps(o);
        JsonNode step = s[Find(o, what, id)]!;
        s.Remove(step);
        s.Insert(index, step);
    }

    private static void Time(JsonObject o, string what, string id, int seconds) => Steps(o)[Find(o, what, id)]!["at"] = new JsonObject { ["time"] = seconds };

    internal static (string Name, JsonObject Opening)[] AllVariants()
    {
        JsonObject m = TeblorOpeningFixtureTests.D33Fixture();
        JsonObject Copy() => JsonNode.Parse(m.ToJsonString())!.AsObject();

        // Q1: Carver then Anointing right after the War-dog train step (the last of the three).
        JsonObject q1 = Copy();
        Put(q1, "build", "teblor_bloodwood_carver", Find(q1, "train", "teblor_war_dog") + 1);
        Put(q1, "research", "teblor_blood_oil_anointing", Find(q1, "build", "teblor_bloodwood_carver") + 1);
        Time(q1, "research", "teblor_blood_oil_anointing", 600);

        // Q2: Breaker train, Carver, Anointing (10:00), Rider train, War-dog train.
        JsonObject q2 = Copy();
        Put(q2, "build", "teblor_bloodwood_carver", Find(q2, "train", "teblor_breaker") + 1);
        Put(q2, "research", "teblor_blood_oil_anointing", Find(q2, "build", "teblor_bloodwood_carver") + 1);
        Time(q2, "research", "teblor_blood_oil_anointing", 600);

        JsonObject q3 = JsonNode.Parse(q1.ToJsonString())!.AsObject();
        Time(q3, "research", "teblor_blood_oil_anointing", 690);

        return new[] { ("M", m), ("Q1", q1), ("Q2", q2), ("Q3", q3) };
    }

    /// <summary>Each variant keeps M's steps as a multiset and every field but the build order; only order and times move.</summary>
    [Fact]
    public void QaVariants_KeepMsSteps_AndLoad()
    {
        JsonObject m = TeblorOpeningFixtureTests.D33Fixture();
        static string Key(JsonNode s) => $"{(string)s["do"]!} {(string?)s["id"]} x{(int?)s["count"] ?? 1}";
        string ms = string.Join(", ", Steps(m).Select(s => Key(s!)).OrderBy(k => k, StringComparer.Ordinal));
        foreach (var (name, o) in AllVariants())
        {
            Assert.Equal(ms, string.Join(", ", Steps(o).Select(s => Key(s!)).OrderBy(k => k, StringComparer.Ordinal)));
            foreach (string field in new[] { "id", "displayName", "description", "compositions", "attack" })
                Assert.True(JsonNode.DeepEquals(m[field], o[field]), $"{name}: {field}");
            GameData d = TeblorOpeningVariantsReportTests.DataWith(o);
            Assert.Equal(Steps(m).Count, d.Ai[d.FindFaction("teblor")].Openings[0].BuildOrder.Length);
            output.WriteLine($"{name}: {string.Join(" | ", Steps(o).Skip(16).Select(s => $"{Key(s!)}@{s!["at"]!.ToJsonString()}"))}");
        }
    }

    private sealed record Row(string V, AiDifficulty X, ulong Seed, string Map, int DefeatedAt, int HallAt, int AnointedAt, bool[] Fielded);

    private static Row Play(string v, GameData data, string map, string opponent, ulong seed, AiDifficulty diff)
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
        Assert.Equal("standard", sim.AiOf(p)!.Opening.Id);
        int hall = data.FindBuilding("teblor_longhouse");
        int[] ageTwo = AgeTwoUnits.Select(data.FindUnit).ToArray();
        var fielded = new bool[ageTwo.Length];
        int fell = -1, hallAt = -1;
        while (sim.TickNumber < Fifteen)
        {
            sim.Tick();
            int now = sim.TickNumber;
            if (fell < 0 && w.IsDefeated(p)) fell = now;
            if (now % Tps != 0) continue;
            var u = w.Units;
            for (int s = 0; s < u.Capacity; s++)
            {
                if (!u.Alive[s] || u.Owner[s] != p) continue;
                int k = Array.IndexOf(ageTwo, u.TypeId[s]);
                if (k >= 0) fielded[k] = true;
            }
            var bs = w.Buildings;
            int halls = 0;
            for (int s = 0; s < bs.Capacity; s++)
                if (bs.Alive[s] && bs.Owner[s] == p && !bs.UnderConstruction[s] && bs.TypeId[s] == hall) halls++;
            if (hallAt < 0 && halls >= 2) hallAt = now;
        }
        return new Row(v, diff, seed, map, fell, hallAt, w.TechResearchedTick(p, data.FindTech("teblor_blood_oil_anointing")), fielded);
    }

    [Fact]
    public void QaOrders_Row_Report()
    {
        string? only = Env("D34QA_VARIANTS");
        // QA re-check of D34 fix round 1: "S" is the shipped data exactly as loaded (TestSim.Data, not a rebuilt copy), so the
        // shipped state's rows are measured on the files themselves; asked for by name only.
        var variants = AllVariants().Append(("S", TeblorOpeningVariantsReportTests.Shipped()))
            .Where(v => only == null ? v.Item1 != "S" : only.Split(',').Contains(v.Item1)).ToArray();
        AiDifficulty[] diffs = (Env("D34QA_DIFFS") ?? "Easy,Normal,Hard").Split(',').Select(Enum.Parse<AiDifficulty>).ToArray();
        ulong[] seeds = (Env("D34QA_SEEDS") ?? "1,2,3,4,5,6,7,8").Split(',').Select(ulong.Parse).ToArray();
        var jobs = new List<(string V, GameData D, string M, string O, ulong S, AiDifficulty X)>();
        foreach (var (name, o) in variants)
        {
            // D34 fix round 1 (BUG-1641): M is D33's opening on a shipped copy (was TestSim.Data, which is D34's Q2 now).
            // M6-S13 (sim track, the hash move): S and M are D34's and D33's records, so both on the shipped files with the draw
            // block out (S was TestSim.Data, which carries the block now).
            // D38 (data track, the hash move): the shipped Teblor opening is D37's three orders now, so S, D34's record, is the
            // D34 fixture on that copy (was DrawScenes.Off, the shipped files with the block out).
            GameData d = name == "S" ? Draw.DrawScenes.TeblorOpeningBeforeDraw(TeblorOpeningFixtureTests.D34Fixture()) : name == "M" ? Draw.DrawScenes.TeblorOpeningBeforeDraw(o) : TeblorOpeningVariantsReportTests.DataWith(o);
            if (name == "M") Assert.Equal(D33QaTests.D33Hash, d.ContentHash());
            if (name == "S") Assert.Equal(D34QaTests.D34Hash, d.ContentHash());
            foreach (AiDifficulty x in diffs)
                foreach (ulong s in seeds)
                    foreach (string m in Maps)
                        foreach (string opp in new[] { "malazan", "whirlwind" }) jobs.Add((name, d, m, opp, s, x));
        }
        var rows = new Row[jobs.Count];
        var errors = new ConcurrentQueue<string>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, i =>
        {
            var j = jobs[i];
            try { rows[i] = Play(j.V, j.D, j.M, j.O, j.S, j.X); }
            catch (Exception e) { errors.Enqueue($"{j}: {e}"); }
        });
        Assert.True(errors.IsEmpty, string.Join("\n", errors));
        output.WriteLine($"QA D34: {jobs.Count} matches in {clock.Elapsed.TotalSeconds:0} s, seeds {string.Join(",", seeds)} ({seeds.Length * 8} a row)");
        output.WriteLine("| variant | difficulty | defeated | 2nd Longhouse | Shaman | Breaker | Rider | War-dog | Anointing | Javelin |");
        foreach (AiDifficulty x in diffs)
            foreach (var (name, _) in variants)
            {
                var g = rows.Where(r => r.V == name && r.X == x).ToList();
                output.WriteLine($"| {name} | {x} | {g.Count(r => r.DefeatedAt >= 0)} | {g.Count(r => r.HallAt >= 0)} | {g.Count(r => r.Fielded[1])} | {g.Count(r => r.Fielded[2])} | "
                    + $"{g.Count(r => r.Fielded[3])} | {g.Count(r => r.Fielded[4])} | {g.Count(r => r.AnointedAt >= 0)} | {g.Count(r => r.Fielded[0])} |");
            }
    }
}
