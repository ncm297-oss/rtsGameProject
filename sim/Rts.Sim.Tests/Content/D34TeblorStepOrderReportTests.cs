using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D34 (BUG-1612), a measurement (rule 48: only preconditions asserted). D33's Teblor <c>standard</c>
/// (<c>Fixtures/teblor_standard_d33.json</c>, "M") against variants that change only the order and the times of its steps,
/// on the 64-seat row of <c>QA/Content/D33TeblorRowQaStressTests</c>' shape (the Teblor on seat 0 against the Malazan and on
/// seat 1 against the Whirlwind, the same difficulty on both seats, four maps, to 15:00). Rows: defeated / 2nd Longhouse /
/// Shaman / Breaker / Rider / War-dog / Anointing, plus the Bloodwood Carver and the Shaman's Circle finished.
/// <c>D34_VARIANTS</c> (comma-separated names), <c>D34_DIFFS</c> (default Easy, Normal), <c>D34_SEEDS</c> (default 1-4, the
/// reduced screen; 1-8 is the full 64) and <c>D34_THREADS</c> narrow a run. Outcome (2026-11-18, teblor.md "Easy's
/// Anointing (BUG-1612, D34)"): G5 on the full rows brings Easy's Anointing to 32 of 64 but Normal's Breakers to 7 (the
/// bar 8), so none ships; M's full rows equal QA's D33 rows to the match.
/// </summary>
[Trait("Category", "Stress")]
public class D34TeblorStepOrderReportTests(ITestOutputHelper output)
{
    private const int Tps = SimConstants.TicksPerSecond;
    private const int Fifteen = 18_000;
    private static readonly string[] Maps = { "generated", "raraku", "pale_hills", "vathar_crossing" };
    private static readonly string[] Opponents = { "malazan", "whirlwind" };

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

    private static void At(JsonObject o, string what, string? id, int time) => Order(o)[IndexOf(o, what, id)]!["at"] = new JsonObject { ["time"] = time };

    private static readonly (string, string?) Circle = ("build", "teblor_shamans_circle");
    private static readonly (string, string?) Shamans = ("train", "teblor_shaman");
    private static readonly (string, string?) Carver = ("build", "teblor_bloodwood_carver");
    private static readonly (string, string?) Anointing = ("research", "teblor_blood_oil_anointing");

    /// <summary>The variant D34 ships, if any (<c>null</c>: none reached the brief's criterion 2, the shipped file is D33's).</summary>
    internal const string? ShippedVariant = null;

    /// <summary>
    /// Every variant D34 measured, each named by its edit to D33's opening ("M", the fixture). Only the order and the
    /// <c>at.time</c> of steps change; no step is added or removed and no count changes.
    /// </summary>
    internal static (string Name, string What, JsonObject Opening)[] AllVariants()
    {
        JsonObject m = TeblorOpeningFixtureTests.D33Fixture();
        var list = new List<(string, string, JsonObject)> { ("M", "D33's opening (the shipped file, `main`)", m) };

        JsonObject g1 = Copy(m);
        MoveBefore(g1, "train", "teblor_breaker", Circle, Shamans, Anointing);
        list.Add(("G1", "M with the Shaman's Circle, the Shaman step and the Anointing moved ahead of the three train steps (times kept)", g1));

        JsonObject g2 = Copy(g1);
        At(g2, "research", "teblor_blood_oil_anointing", 600);
        list.Add(("G2", "G1 with the Anointing due at 10:00 (was 11:30)", g2));

        JsonObject g3 = Copy(m);
        MoveBefore(g3, "train", "teblor_breaker", Circle, Shamans, Carver, Anointing);
        At(g3, "research", "teblor_blood_oil_anointing", 600);
        list.Add(("G3", "M with the Circle, the Shaman step, the Bloodwood Carver and the Anointing (at 10:00) ahead of the three train steps", g3));

        JsonObject g4 = Copy(g3);
        At(g4, "build", "teblor_bloodwood_carver", 540);
        At(g4, "research", "teblor_blood_oil_anointing", 570);
        list.Add(("G4", "G3 with the Carver at 9:00 and the Anointing at 9:30", g4));

        JsonObject g5 = Copy(m);
        MoveBefore(g5, "train", "teblor_breaker", Carver, Anointing);
        At(g5, "research", "teblor_blood_oil_anointing", 600);
        list.Add(("G5", "M with only the Carver and the Anointing (at 10:00) ahead of the three train steps; the Circle and the Shamans where D33 has them", g5));

        return list.ToArray();
    }

    internal static (string Name, string What, JsonObject Opening)[] Variants()
    {
        string? only = Env("D34_VARIANTS");
        return AllVariants().Where(v => only == null || only.Split(',').Contains(v.Name)).ToArray();
    }

    /// <summary>Every variant loads as the Teblor <c>standard</c> on a shipped copy, keeps M's steps (as a multiset) and changes only order and times.</summary>
    [Fact]
    public void EveryVariant_Loads_AndKeepsMsSteps()
    {
        JsonObject m = TeblorOpeningFixtureTests.D33Fixture();
        static string Steps(JsonObject o) => string.Join(", ", Order(o).Select(s => $"{(string)s!["do"]!} {(string?)s["id"]} x{(int?)s["count"] ?? 1}").OrderBy(k => k, StringComparer.Ordinal));
        foreach (var v in AllVariants())
        {
            GameData d = TeblorOpeningVariantsReportTests.DataWith(v.Opening);
            Assert.Equal(Order(m).Count, d.Ai[d.FindFaction("teblor")].Openings[0].BuildOrder.Length);
            Assert.Equal(Steps(m), Steps(v.Opening));
            foreach (string field in new[] { "id", "displayName", "description", "compositions", "attack" })
                Assert.True(JsonNode.DeepEquals(m[field], v.Opening[field]), $"{v.Name}: {field} differs from M");
        }
    }

    internal sealed record Seat(string Variant, AiDifficulty Diff, ulong Seed, string Map, string Opponent, int DefeatedAt, int HallAt,
        int AnointedAt, bool[] Fielded, int CarverAt, int CircleAt);

    /// <summary>One match to 15:00 as <c>D33TeblorRowQaStressTests</c> plays it.</summary>
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
        int hall = data.FindBuilding("teblor_longhouse"), carver = data.FindBuilding("teblor_bloodwood_carver"), circle = data.FindBuilding("teblor_shamans_circle");
        int[] ageTwo = D33BalanceReportTests.AgeTwoUnits.Select(data.FindUnit).ToArray();
        Assert.DoesNotContain(-1, ageTwo);
        var fielded = new bool[ageTwo.Length];
        int fell = -1, hallAt = -1, carverAt = -1, circleAt = -1;
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
            {
                if (!bs.Alive[s] || bs.Owner[s] != p || bs.UnderConstruction[s]) continue;
                if (bs.TypeId[s] == hall) halls++;
                if (bs.TypeId[s] == carver && carverAt < 0) carverAt = now;
                if (bs.TypeId[s] == circle && circleAt < 0) circleAt = now;
            }
            if (hallAt < 0 && halls >= 2) hallAt = now;
        }
        return new Seat(variant, diff, seed, map, opponent, fell, hallAt, w.TechResearchedTick(p, data.FindTech("teblor_blood_oil_anointing")),
            fielded, carverAt, circleAt);
    }

    /// <summary>
    /// Each variant at each difficulty on the row: defeated / 2nd Longhouse / Shaman / Breaker / Rider / War-dog / Anointing
    /// (matches of the row), with the Carver and the Circle finished and the Javelin Throwers fielded beside them.
    /// </summary>
    [Fact]
    public void Variants_Row_Report()
    {
        var variants = Variants();
        AiDifficulty[] diffs = (Env("D34_DIFFS") ?? "Easy,Normal").Split(',').Select(Enum.Parse<AiDifficulty>).ToArray();
        ulong[] seeds = (Env("D34_SEEDS") ?? "1,2,3,4").Split(',').Select(ulong.Parse).ToArray();
        int threads = int.TryParse(Env("D34_THREADS"), out int n) && n > 0 ? n : Math.Max(1, Environment.ProcessorCount / 2);
        var jobs = new List<(string V, GameData D, string M, string O, ulong S, AiDifficulty X)>();
        foreach (var v in variants)
        {
            GameData d = TeblorOpeningVariantsReportTests.DataWith(v.Opening);
            foreach (AiDifficulty x in diffs)
                foreach (ulong seed in seeds)
                    foreach (string m in Maps)
                        foreach (string o in Opponents) jobs.Add((v.Name, d, m, o, seed, x));
        }
        var rows = new Seat[jobs.Count];
        var errors = new ConcurrentQueue<string>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = threads }, i =>
        {
            var j = jobs[i];
            try { rows[i] = Play(j.V, j.D, j.M, j.O, j.S, j.X); }
            catch (Exception e) { errors.Enqueue($"{j.V} {j.X} {j.S} {j.M} {j.O}: {e}"); }
        });
        Assert.True(errors.IsEmpty, string.Join("\n", errors));
        output.WriteLine($"D34 BUG-1612: {jobs.Count} matches to 15:00 in {clock.Elapsed.TotalSeconds:0} s (seeds {string.Join(",", seeds)}, four maps, two opponents: {seeds.Length * 8} seats a row)");
        foreach (var v in variants) output.WriteLine($"- {v.Name}: {v.What}");
        output.WriteLine("");
        output.WriteLine("| variant | difficulty | defeated | 2nd Longhouse | Shaman | Breaker | Rider | War-dog | Anointing | Carver / Circle finished | Javelin |");
        output.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (AiDifficulty x in diffs)
            foreach (var v in variants)
            {
                var g = rows.Where(r => r.Variant == v.Name && r.Diff == x).ToList();
                output.WriteLine($"| {v.Name} | {x} | {g.Count(r => r.DefeatedAt >= 0)} | {g.Count(r => r.HallAt >= 0)} | {g.Count(r => r.Fielded[1])} | {g.Count(r => r.Fielded[2])} | "
                    + $"{g.Count(r => r.Fielded[3])} | {g.Count(r => r.Fielded[4])} | {g.Count(r => r.AnointedAt >= 0)} | {g.Count(r => r.CarverAt >= 0)} / {g.Count(r => r.CircleAt >= 0)} | {g.Count(r => r.Fielded[0])} | (of {g.Count})");
            }
        output.WriteLine("");
        output.WriteLine("Per map (defeated / Shaman / Breaker / Rider / War-dog / Anointing):");
        foreach (AiDifficulty x in diffs)
            foreach (var v in variants)
                output.WriteLine($"| {v.Name} | {x} | " + string.Join(" | ", Maps.Select(m =>
                {
                    var g = rows.Where(r => r.Variant == v.Name && r.Diff == x && r.Map == m).ToList();
                    return $"{m} {g.Count(r => r.DefeatedAt >= 0)}/{g.Count(r => r.Fielded[1])}/{g.Count(r => r.Fielded[2])}/{g.Count(r => r.Fielded[3])}/{g.Count(r => r.Fielded[4])}/{g.Count(r => r.AnointedAt >= 0)}";
                })) + " |");
    }
}
