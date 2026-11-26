using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D36 (BUG-1612, BUG-1721), a measurement (rule 48: only preconditions asserted). The second, bounded search for a Teblor
/// <c>standard</c> order that meets the bar of docs/01 2026-11-24 ACCEPT (3)(c) on Easy, Normal and Hard, around QA's
/// one-Shaman orders QA1 and QRD1 (<c>QA/Content/DH10TeblorOrderQaStressTests</c>). The row is D34's
/// (<see cref="D34TeblorStepOrderReportTests.Play"/>: the Teblor on seat 0 against the Malazan and on seat 1 against the
/// Whirlwind, the same difficulty on both seats, four maps, to 15:00), judged against <see cref="DH10TeblorSwitchReportTests.Bar"/>.
/// The levers (rule 78) are the brief's: the Shaman step's count and position, which of the Breaker, Destrier Rider and
/// War-dog train steps carry <c>throughReserve</c>, the Shamans' Circle's time, the Anointing's and the Carver's time or
/// position, and <c>noReserve</c> on the <c>armor_1</c> research step; never <c>noReserve</c> on the expand step
/// (docs/01 2026-11-24 ACCEPT (1)(e)). <c>D36_VARIANTS</c> (comma-separated names), <c>D36_DIFFS</c> (default Easy, Normal,
/// Hard), <c>D36_SEEDS</c> (default 1, 2: reduced; 1-8 is the full 64) and <c>D36_THREADS</c> (default and cap 6: the sim
/// track's suites run beside this one) narrow a run; <c>D36_SEATS</c>=1 prints one line a seat. Outcome (2026-11-25,
/// teblor.md "The unit mix, second search (BUG-1612, BUG-1721, D36)"): the full rows of <see cref="FullRowOrders"/> (M
/// equal to D-H10's, QRD1 equal to QA's); none meets the bar, so nothing ships and there is no fixture
/// (<see cref="ShippedVariant"/> is null); the nearest is QRD1 (4 figures missed, 16 seats short).
/// </summary>
[Trait("Category", "Stress")]
public class D36TeblorOrderSearchReportTests(ITestOutputHelper output)
{
    /// <summary>The order D36 hands to D37: none met the bar (teblor.md "The unit mix, second search").</summary>
    internal const string? ShippedVariant = null;

    /// <summary>The orders measured on full rows (seeds 1-8), the shipped order first; the rest were reduced screens only.</summary>
    internal static readonly string[] FullRowOrders = { "M", "QRD1", "RSX", "RSC", "K9KC930", "K9KC10S" };

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

    /// <summary>The first step that does <paramref name="what"/> to <paramref name="id"/>.</summary>
    private static JsonObject Step(JsonObject o, (string What, string? Id) s) => Order(o)[IndexOf(o, s.What, s.Id)]!.AsObject();

    private static JsonObject Edit(JsonObject o, Action<JsonObject> edit)
    {
        JsonObject c = Copy(o);
        edit(c);
        return c;
    }

    private static void Through(JsonObject o, params (string, string?)[] steps)
    {
        foreach (var s in steps) Step(o, s)["throughReserve"] = true;
    }

    private static void NotThrough(JsonObject o, params (string, string?)[] steps)
    {
        foreach (var s in steps) Step(o, s).Remove("throughReserve");
    }

    private static void At(JsonObject o, (string, string?) s, int time) => Step(o, s)["at"] = new JsonObject { ["time"] = time };

    /// <summary>Moves the named steps, in the order given, to just before the step <paramref name="before"/>.</summary>
    private static void MoveBefore(JsonObject o, (string What, string? Id) before, params (string What, string? Id)[] steps)
    {
        JsonArray order = Order(o);
        foreach (var s in steps)
        {
            JsonNode step = order[IndexOf(o, s.What, s.Id)]!;
            order.Remove(step);
            order.Insert(IndexOf(o, before.What, before.Id), step);
        }
    }

    private static readonly (string, string?) Rider = DH10TeblorSwitchReportTests.Rider, Dog = DH10TeblorSwitchReportTests.Dog,
        Shamans = DH10TeblorSwitchReportTests.Shamans, Breaker = DH10TeblorSwitchReportTests.Breaker, Carver = DH10TeblorSwitchReportTests.Carver,
        Anointing = DH10TeblorSwitchReportTests.Anointing, Armor = DH10TeblorSwitchReportTests.Armor, Circle = DH10TeblorSwitchReportTests.Circle,
        BreakersHall = DH10TeblorSwitchReportTests.BreakersHall, Stables = DH10TeblorSwitchReportTests.Stables;
    private static readonly (string, string?) Expand = ("expand", null);

    /// <summary>QA's QRD1 (BUG-1721): M with <c>throughReserve</c> on the Rider and War-dog steps and the Shaman step's count 2 -> 1.</summary>
    internal static JsonObject Qrd1() => Edit(TeblorOpeningFixtureTests.D34Fixture(), c =>
    {
        Through(c, Rider, Dog);
        Step(c, Shamans)["count"] = 1;
    });

    /// <summary>QA's QA1 (BUG-1721): M with <c>throughReserve</c> on the Rider, War-dog and Shaman steps and the Shaman step's count 2 -> 1.</summary>
    internal static JsonObject Qa1() => Edit(TeblorOpeningFixtureTests.D34Fixture(), c =>
    {
        Through(c, Rider, Dog, Shamans);
        Step(c, Shamans)["count"] = 1;
    });

    /// <summary>
    /// Every order D36 measured, each named by its edit to M (the shipped order, D34's Q2), QRD1 or QA1. Steps are never added
    /// or removed; only the Shaman step's count, the switches, the times and the file order change. No description changes.
    /// </summary>
    internal static (string Name, string What, JsonObject Opening)[] AllVariants()
    {
        JsonObject m = TeblorOpeningFixtureTests.D34Fixture(), qrd1 = Qrd1(), qa1 = Qa1();
        var list = new List<(string, string, JsonObject)>
        {
            ("M", "the shipped order (D34's Q2)", m),
            ("QRD1", "QA's: M with throughReserve on the Rider and War-dog steps, the Shaman step's count 1", qrd1),
            ("QA1", "QA's: M with throughReserve on the Rider, War-dog and Shaman steps, the Shaman step's count 1", qa1),
        };

        // --- Screen 1: one lever at a time around QRD1 / QA1.
        // The Shaman step's count.
        list.Add(("R2", "QRD1 with the Shaman step's count 2 (QA's QRD)", Edit(qrd1, c => Step(c, Shamans)["count"] = 2)));
        // Which train steps carry throughReserve (Breaker added; War-dog only; Rider only; War-dog and Shaman).
        list.Add(("RB", "QRD1 with throughReserve on the Breaker step too", Edit(qrd1, c => Through(c, Breaker))));
        list.Add(("RD", "QRD1 without throughReserve on the Rider step (the War-dog step only)", Edit(qrd1, c => NotThrough(c, Rider))));
        list.Add(("RR", "QRD1 without throughReserve on the War-dog step (the Rider step only)", Edit(qrd1, c => NotThrough(c, Dog))));
        // The Shaman step's position (no switch): ahead of the expand step, ahead of the Carver (after the Breaker).
        list.Add(("RSX", "QRD1 with the Shaman step moved just ahead of the expand step", Edit(qrd1, c => MoveBefore(c, Expand, Shamans))));
        list.Add(("RSC", "QRD1 with the Shaman step moved just ahead of the Carver (after the Breaker step)", Edit(qrd1, c => MoveBefore(c, Carver, Shamans))));
        list.Add(("RSE", "QRD1 with the Shaman step moved to the end of the file", Edit(qrd1, c =>
        {
            JsonNode s = Step(c, Shamans);
            Order(c).Remove(s);
            Order(c).Add(s);
        })));
        // The Circle's time.
        list.Add(("RC930", "QRD1 with the Shamans' Circle due at 9:30 (was 9:00)", Edit(qrd1, c => At(c, Circle, 570))));
        list.Add(("RC830", "QRD1 with the Shamans' Circle due at 8:30 (was 9:00)", Edit(qrd1, c => At(c, Circle, 510))));
        // The Anointing's time and position, the Carver's time.
        list.Add(("RA930", "QRD1 with the Anointing due at 9:30 (was 10:00), the Carver kept at 10:00", Edit(qrd1, c => At(c, Anointing, 570))));
        list.Add(("RK930", "QRD1 with the Carver due at 9:30 (was 10:00), the Anointing kept at 10:00", Edit(qrd1, c => At(c, Carver, 570))));
        list.Add(("RK900", "QRD1 with the Carver due at 9:00 (was 10:00), the Anointing kept at 10:00", Edit(qrd1, c => At(c, Carver, 540))));
        list.Add(("RAB", "QRD1 with the Carver and the Anointing moved ahead of the Breaker step (times kept)", Edit(qrd1, c => MoveBefore(c, Breaker, Carver, Anointing))));
        // noReserve on armor_1.
        list.Add(("RN", "QRD1 with noReserve on the armor_1 research step", Edit(qrd1, c => Step(c, Armor)["noReserve"] = true)));
        // The same levers on QA1 (the Shaman step through).
        list.Add(("AK930", "QA1 with the Carver due at 9:30 (was 10:00)", Edit(qa1, c => At(c, Carver, 570))));
        list.Add(("ASX", "QA1 with the Shaman step moved just ahead of the expand step", Edit(qa1, c => MoveBefore(c, Expand, Shamans))));
        list.Add(("AC930", "QA1 with the Shamans' Circle and the Shaman step due at 9:30 (were 9:00)", Edit(qa1, c =>
        {
            At(c, Circle, 570);
            At(c, Shamans, 570);
        })));
        list.Add(("AD", "QA1 without throughReserve on the Rider step (War-dog and Shaman through)", Edit(qa1, c => NotThrough(c, Rider))));
        // The Circle and the Shaman step together, between QRD1 (after the expand) and QA's QRD1F (ahead of the Breaker).
        list.Add(("RCH", "QRD1 with the Shamans' Circle and the Shaman step moved just ahead of the Breakers' Hall (behind the 10:00 steps)", Edit(qrd1, c => MoveBefore(c, BreakersHall, Circle, Shamans))));
        list.Add(("RCR", "QRD1 with the Shamans' Circle and the Shaman step moved just ahead of the Rider step (after the Breaker, Carver and Anointing)", Edit(qrd1, c => MoveBefore(c, Rider, Circle, Shamans))));
        list.Add(("RCK", "QRD1 with the Shamans' Circle and the Shaman step moved just ahead of the Carver (after the Breaker step)", Edit(qrd1, c => MoveBefore(c, Carver, Circle, Shamans))));

        // --- Screen 2 (after screen 1: on Easy the Anointing lands in the last seconds before 15:00, and the Carver at 9:00
        // (RK900) brings it to most seats but takes the Shamans): the Carver at 9:00 with the levers that pay the Shamans.
        JsonObject k9 = Edit(qrd1, c => At(c, Carver, 540));
        list.Add(("K9N", "RK900 with noReserve on the armor_1 research step", Edit(k9, c => Step(c, Armor)["noReserve"] = true)));
        list.Add(("K9S", "RK900 with throughReserve on the Shaman step (QA1 with the Carver at 9:00)", Edit(k9, c => Through(c, Shamans))));
        list.Add(("K9SN", "K9S with noReserve on the armor_1 research step", Edit(k9, c =>
        {
            Through(c, Shamans);
            Step(c, Armor)["noReserve"] = true;
        })));
        list.Add(("K9A9", "RK900 with the Anointing due at 9:00 too", Edit(k9, c => At(c, Anointing, 540))));
        list.Add(("K9A9S", "K9A9 with throughReserve on the Shaman step", Edit(k9, c =>
        {
            At(c, Anointing, 540);
            Through(c, Shamans);
        })));
        list.Add(("K9A9SN", "K9A9S with noReserve on the armor_1 research step", Edit(k9, c =>
        {
            At(c, Anointing, 540);
            Through(c, Shamans);
            Step(c, Armor)["noReserve"] = true;
        })));
        list.Add(("K9SX", "RK900 with the Shaman step moved just ahead of the expand step", Edit(k9, c => MoveBefore(c, Expand, Shamans))));
        list.Add(("K9SXN", "K9SX with noReserve on the armor_1 research step", Edit(k9, c =>
        {
            MoveBefore(c, Expand, Shamans);
            Step(c, Armor)["noReserve"] = true;
        })));
        list.Add(("K9C830", "RK900 with the Shamans' Circle due at 8:30", Edit(k9, c => At(c, Circle, 510))));
        list.Add(("K9C830S", "K9C830 with throughReserve on the Shaman step", Edit(k9, c =>
        {
            At(c, Circle, 510);
            Through(c, Shamans);
        })));
        list.Add(("K9C830SN", "K9C830S with noReserve on the armor_1 research step", Edit(k9, c =>
        {
            At(c, Circle, 510);
            Through(c, Shamans);
            Step(c, Armor)["noReserve"] = true;
        })));
        list.Add(("K830", "QRD1 with the Carver due at 8:30", Edit(qrd1, c => At(c, Carver, 510))));
        list.Add(("K830S", "K830 with throughReserve on the Shaman step", Edit(qrd1, c =>
        {
            At(c, Carver, 510);
            Through(c, Shamans);
        })));
        list.Add(("K830SN", "K830S with noReserve on the armor_1 research step", Edit(qrd1, c =>
        {
            At(c, Carver, 510);
            Through(c, Shamans);
            Step(c, Armor)["noReserve"] = true;
        })));
        list.Add(("K9KC", "RK900 with the Shamans' Circle and the Shaman step moved just ahead of the Rider step (after the Carver and the Anointing)", Edit(k9, c => MoveBefore(c, Rider, Circle, Shamans))));
        // --- Screen 3 (after screen 2: K9KC gives Easy its Shamans and Anointing but its Breakers' Hall waits behind the
        // Circle): K9KC with the Circle due later, so the Hall (due 9:00) is paid first.
        JsonObject k9kc = Edit(k9, c => MoveBefore(c, Rider, Circle, Shamans));
        list.Add(("K9KC930", "K9KC with the Shamans' Circle due at 9:30", Edit(k9kc, c => At(c, Circle, 570))));
        list.Add(("K9KC10", "K9KC with the Shamans' Circle due at 10:00", Edit(k9kc, c => At(c, Circle, 600))));
        list.Add(("K9KC1030", "K9KC with the Shamans' Circle due at 10:30", Edit(k9kc, c => At(c, Circle, 630))));
        list.Add(("K9KCS", "K9KC with throughReserve on the Shaman step", Edit(k9kc, c => Through(c, Shamans))));
        list.Add(("K9KC10S", "K9KC10 with throughReserve on the Shaman step", Edit(k9kc, c =>
        {
            At(c, Circle, 600);
            Through(c, Shamans);
        })));
        list.Add(("K9KC10A930", "K9KC10 with the Anointing due at 9:30", Edit(k9kc, c =>
        {
            At(c, Circle, 600);
            At(c, Anointing, 570);
        })));
        list.Add(("K9KC2", "K9KC with the Shaman step's count 2", Edit(k9kc, c => Step(c, Shamans)["count"] = 2)));
        list.Add(("K930KC10", "QRD1 with the Carver due at 9:30, the Circle at 10:00 and the Circle and Shaman steps moved just ahead of the Rider step", Edit(qrd1, c =>
        {
            At(c, Carver, 570);
            At(c, Circle, 600);
            MoveBefore(c, Rider, Circle, Shamans);
        })));
        list.Add(("K9C930S", "RK900 with the Shamans' Circle due at 9:30 (file order kept) and throughReserve on the Shaman step", Edit(k9, c =>
        {
            At(c, Circle, 570);
            Through(c, Shamans);
        })));

        // --- Screen 4 (after screen 3: the Carver at 9:00 costs Normal and Hard their Breakers, Riders and War-dogs): the
        // Anointing's position on QRD1 (Normal's Anointing is QRD1's largest miss).
        list.Add(("RA1", "QRD1 with the Anointing step moved just ahead of the Breaker step (the Carver kept)", Edit(qrd1, c => MoveBefore(c, Breaker, Anointing))));
        list.Add(("RA1B", "RA1 with throughReserve on the Breaker step", Edit(qrd1, c =>
        {
            MoveBefore(c, Breaker, Anointing);
            Through(c, Breaker);
        })));
        list.Add(("RA1BX", "RA1B with the Shaman step moved just ahead of the expand step", Edit(qrd1, c =>
        {
            MoveBefore(c, Breaker, Anointing);
            Through(c, Breaker);
            MoveBefore(c, Expand, Shamans);
        })));

        // --- Screen 5 (after the first full rows: K9KC10S meets every Easy figure and Normal's Anointing, but the Carver at
        // 9:00 costs Hard its Breakers, Riders and War-dogs; RSC meets every Normal figure but the Anointing).
        list.Add(("K9C10S", "RK900 with the Shamans' Circle due at 10:00 (file order kept) and throughReserve on the Shaman step", Edit(k9, c =>
        {
            At(c, Circle, 600);
            Through(c, Shamans);
        })));
        list.Add(("AA1B", "QA1 with the Anointing step moved just ahead of the Breaker step and throughReserve on the Breaker step", Edit(qa1, c =>
        {
            MoveBefore(c, Breaker, Anointing);
            Through(c, Breaker);
        })));
        list.Add(("RSCA", "RSC with the Anointing step moved just ahead of the Shaman step", Edit(qrd1, c =>
        {
            MoveBefore(c, Carver, Shamans);
            MoveBefore(c, Shamans, Anointing);
        })));
        list.Add(("RSX2", "RSX with the Shaman step's count 2", Edit(qrd1, c =>
        {
            MoveBefore(c, Expand, Shamans);
            Step(c, Shamans)["count"] = 2;
        })));
        list.Add(("K930KC10S", "K930KC10 with throughReserve on the Shaman step", Edit(qrd1, c =>
        {
            At(c, Carver, 570);
            At(c, Circle, 600);
            MoveBefore(c, Rider, Circle, Shamans);
            Through(c, Shamans);
        })));

        list.Add(("C830S", "QRD1 with the Shamans' Circle due at 8:30 and throughReserve on the Shaman step", Edit(qrd1, c =>
        {
            At(c, Circle, 510);
            Through(c, Shamans);
        })));
        list.Add(("C830SN", "C830S with noReserve on the armor_1 research step", Edit(qrd1, c =>
        {
            At(c, Circle, 510);
            Through(c, Shamans);
            Step(c, Armor)["noReserve"] = true;
        })));

        return list.ToArray();
    }

    /// <summary>The variants <c>D36_VARIANTS</c> names (all when empty), in the class's order.</summary>
    internal static (string Name, string What, JsonObject Opening)[] Variants()
    {
        string? only = Env("D36_VARIANTS");
        return AllVariants().Where(v => only == null || only.Split(',').Contains(v.Name)).ToArray();
    }

    /// <summary>
    /// Every order loads as the Teblor <c>standard</c> on a shipped copy with M's steps (as a multiset of action and id), M's
    /// other fields, no <c>noReserve</c> on the expand step, no switch written <c>false</c>; QRD1 and QA1 are QA's orders.
    /// </summary>
    [Fact]
    public void EveryOrder_Loads_KeepsMsSteps_AndNeverSwitchesTheExpand()
    {
        JsonObject m = TeblorOpeningFixtureTests.D34Fixture();
        static string Steps(JsonObject o) => string.Join(", ", Order(o).Select(s => $"{(string)s!["do"]!} {(string?)s["id"]}").OrderBy(k => k, StringComparer.Ordinal));
        var names = new HashSet<string>();
        foreach (var v in AllVariants())
        {
            Assert.True(names.Add(v.Name), $"{v.Name} twice");
            GameData d = TeblorOpeningVariantsReportTests.DataWith(v.Opening);
            Assert.Equal(Order(m).Count, d.Ai[d.FindFaction("teblor")].Openings[0].BuildOrder.Length);
            Assert.Equal(Steps(m), Steps(v.Opening));
            foreach (string field in new[] { "id", "displayName", "description", "compositions", "attack" })
                Assert.True(JsonNode.DeepEquals(m[field], v.Opening[field]), $"{v.Name}: {field} differs from M");
            Assert.DoesNotContain(Order(v.Opening), s => (string)s!["do"]! == "expand" && s["noReserve"] != null);
            Assert.DoesNotContain(Order(v.Opening), s => s!["throughReserve"] is JsonValue f && !(bool)f);
        }
        Assert.InRange(FullRowOrders.Length, 1, 12);
        Assert.Equal("M", FullRowOrders[0]);
        foreach (string name in FullRowOrders) Assert.Contains(name, names);
        var qa = QA.Content.DH10TeblorOrderQaStressTests.AllVariants();
        foreach (string name in new[] { "QRD1", "QA1" })
            Assert.True(JsonNode.DeepEquals(qa.Single(v => v.Name == name).Opening, AllVariants().Single(v => v.Name == name).Opening), $"{name} is not QA's");
    }

    /// <summary>
    /// Each order at each difficulty on the row: defeated / 2nd Longhouse / Shaman / Breaker / Rider / War-dog / Anointing
    /// (seats of the row), a miss of the bar marked "!", with the figures missed and the seats short on a full row.
    /// </summary>
    [Fact]
    public void Orders_Row_Report()
    {
        var variants = Variants();
        AiDifficulty[] diffs = (Env("D36_DIFFS") ?? "Easy,Normal,Hard").Split(',').Select(Enum.Parse<AiDifficulty>).ToArray();
        ulong[] seeds = (Env("D36_SEEDS") ?? "1,2").Split(',').Select(ulong.Parse).ToArray();
        int threads = Math.Clamp(int.TryParse(Env("D36_THREADS"), out int n) ? n : 6, 1, 6);
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
        output.WriteLine($"D36 BUG-1612: {jobs.Count} matches to 15:00 in {clock.Elapsed.TotalSeconds:0} s on {threads} threads, seeds {string.Join(",", seeds)} "
            + $"({seeds.Length * 8} seats a row; {(full ? "full rows" : "REDUCED rows: not judged against the bar")})");
        output.WriteLine("| order | difficulty | defeated | 2nd Longhouse | Shaman | Breaker | Rider | War-dog | Anointing | misses | short |");
        var totals = new Dictionary<string, (int Misses, int Short)>();
        foreach (AiDifficulty x in diffs)
            foreach (var v in variants)
            {
                var g = rows.Where(r => r.Variant == v.Name && r.Diff == x).ToList();
                var bar = DH10TeblorSwitchReportTests.Bar(x);
                int misses = 0, shortBy = 0;
                // On a reduced row the bar is scaled to its seats, only to rank candidates (not evidence).
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
                string line = $"| {v.Name} | {x} | {Cell(g.Count(r => r.DefeatedAt >= 0), bar.MaxDefeated, true)} | {Cell(g.Count(r => r.HallAt >= 0), bar.Hall)} | "
                    + $"{Cell(g.Count(r => r.Fielded[1]), bar.Shaman)} | {Cell(g.Count(r => r.Fielded[2]), bar.Breaker)} | {Cell(g.Count(r => r.Fielded[3]), bar.Rider)} | "
                    + $"{Cell(g.Count(r => r.Fielded[4]), bar.Dog)} | {Cell(g.Count(r => r.AnointedAt >= 0), bar.Anointing)} | {misses} | {shortBy} | (of {g.Count})";
                output.WriteLine(line);
                totals[v.Name] = totals.TryGetValue(v.Name, out var t) ? (t.Misses + misses, t.Short + shortBy) : (misses, shortBy);
            }
        output.WriteLine("Totals (figures missed, seats short" + (full ? "" : "; reduced: against the bar scaled to the seats") + "):");
        foreach (var v in variants) output.WriteLine($"| {v.Name} | {totals[v.Name].Misses} | {totals[v.Name].Short} |");
        output.WriteLine("Per map (defeated / Shaman / Breaker / Rider / War-dog / Anointing):");
        foreach (AiDifficulty x in diffs)
            foreach (var v in variants)
                output.WriteLine($"| {v.Name} | {x} | " + string.Join(" | ", maps.Select(mp =>
                {
                    var g = rows.Where(r => r.Variant == v.Name && r.Diff == x && r.Map == mp).ToList();
                    return $"{mp} {g.Count(r => r.DefeatedAt >= 0)}/{g.Count(r => r.Fielded[1])}/{g.Count(r => r.Fielded[2])}/{g.Count(r => r.Fielded[3])}/{g.Count(r => r.Fielded[4])}/{g.Count(r => r.AnointedAt >= 0)}";
                })) + " |");
        if (Env("D36_SEATS") != "1") return;
        output.WriteLine("Seats (order diff seed map opponent: defeatedAt hallAt anointedAt carverAt circleAt fielded[jav,sha,bre,rid,dog]):");
        foreach (var r in rows.OrderBy(r => r.Variant, StringComparer.Ordinal).ThenBy(r => r.Diff).ThenBy(r => r.Seed).ThenBy(r => r.Map, StringComparer.Ordinal).ThenBy(r => r.Opponent, StringComparer.Ordinal))
            output.WriteLine($"{r.Variant} {r.Diff} {r.Seed} {r.Map} {r.Opponent}: {r.DefeatedAt} {r.HallAt} {r.AnointedAt} {r.CarverAt} {r.CircleAt} {string.Join("", r.Fielded.Select(f => f ? '1' : '0'))}");
    }
}
