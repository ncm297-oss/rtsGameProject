using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D37 (BUG-1612), a measurement (rule 48: only preconditions asserted). With an order per difficulty coming (docs/01
/// 2026-11-26 (3), the sim's Request 59), Normal's order is searched alone against Normal's figures of the bar (docs/01
/// 2026-11-24 ACCEPT (3)(c), <see cref="DH10TeblorSwitchReportTests.Bar"/>), and Easy's (K9KC10S) and Hard's (QRD1) are
/// re-measured on their own difficulty. The row is D34's (<see cref="D34TeblorStepOrderReportTests.Play"/>: the Teblor on
/// seat 0 against the Malazan and on seat 1 against the Whirlwind, the same difficulty on both seats, four maps, to 15:00).
/// The levers (rule 78): the Shaman step's count and position, the Shamans' Circle's time, the Anointing's time and
/// position, the Carver's time, which train steps carry <c>throughReserve</c>; never <c>noReserve</c> on the expand step,
/// and not on <c>armor_1</c> (docs/01 2026-11-26 (4)). <c>D37_VARIANTS</c>, <c>D37_DIFFS</c> (default Normal),
/// <c>D37_SEEDS</c> (default 1, 2: reduced; 1-8 is the full 64) and <c>D37_THREADS</c> (default and cap 6) narrow a run;
/// <c>D37_SEATS</c>=1 prints one line a seat. Outcome (2026-11-26, teblor.md "The unit mix, per difficulty (D37)"): 39
/// orders screened on reduced Normal rows, six measured on the full Normal row (<see cref="FullNormalRows"/>; M and RSC equal
/// D36's); M1SC, M1RS and RSCR meet every Normal figure, and <see cref="NormalOrder"/> is M1RS. K9KC10S on Easy and QRD1
/// on Hard equal D36's rows to the cell.
/// </summary>
[Trait("Category", "Stress")]
public class D37TeblorNormalOrderReportTests(ITestOutputHelper output)
{
    /// <summary>The orders measured on the full 64 Normal seats (M and RSC re-measured first; six of the eight allowed).</summary>
    internal static readonly string[] FullNormalRows = { "M", "RSC", "M1S", "M1SC", "M1RS", "RSCR" };

    /// <summary>
    /// Normal's order (teblor.md, D37): M1RS, one of the three that meet every Normal figure (M1SC, M1RS, RSCR), the only one
    /// with a seat of margin on every figure and the Shaman step still after its Circle.
    /// </summary>
    internal const string NormalOrder = "M1RS";

    /// <summary>Easy's and Hard's orders (D36's), re-measured on their own difficulty.</summary>
    internal const string EasyOrder = "K9KC10S", HardOrder = "QRD1";

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

    private static void Through(JsonObject o, params (string, string?)[] steps)
    {
        foreach (var s in steps) Step(o, s)["throughReserve"] = true;
    }

    private static void NotThrough(JsonObject o, params (string, string?)[] steps)
    {
        foreach (var s in steps) Step(o, s).Remove("throughReserve");
    }

    private static void At(JsonObject o, (string, string?) s, int time) => Step(o, s)["at"] = new JsonObject { ["time"] = time };

    private static void Count(JsonObject o, int n) => Step(o, Shamans)["count"] = n;

    /// <summary>Moves the named steps, in the order given, to just before the step <paramref name="before"/>.</summary>
    private static void MoveBefore(JsonObject o, (string, string?) before, params (string, string?)[] steps)
    {
        JsonArray order = Order(o);
        foreach (var s in steps)
        {
            JsonNode step = order[IndexOf(o, s)]!;
            order.Remove(step);
            order.Insert(IndexOf(o, before), step);
        }
    }

    private static readonly (string, string?) Rider = DH10TeblorSwitchReportTests.Rider, Dog = DH10TeblorSwitchReportTests.Dog,
        Shamans = DH10TeblorSwitchReportTests.Shamans, Breaker = DH10TeblorSwitchReportTests.Breaker, Carver = DH10TeblorSwitchReportTests.Carver,
        Anointing = DH10TeblorSwitchReportTests.Anointing, Circle = DH10TeblorSwitchReportTests.Circle,
        BreakersHall = DH10TeblorSwitchReportTests.BreakersHall, Stables = DH10TeblorSwitchReportTests.Stables;
    private static readonly (string, string?) Expand = ("expand", null);

    /// <summary>
    /// Every order D37 measured, each named by its edit to M (the shipped order), RSC (D36's) or QSH (QA's). M, QRD1, RSC and
    /// K9KC10S are D36's own definitions and QSH and QSR QA's (<c>QA/Content/D36TeblorOrderQaStressTests</c>). Steps are
    /// never added or removed; only the Shaman step's count, the switches, the times and the file order change.
    /// </summary>
    internal static (string Name, string What, JsonObject Opening)[] AllVariants()
    {
        var d36 = D36TeblorOrderSearchReportTests.AllVariants();
        var qa = QA.Content.D36TeblorOrderQaStressTests.AllVariants();
        JsonObject D36(string n) => d36.Single(v => v.Name == n).Opening;
        JsonObject m = D36("M"), rsc = D36("RSC"), qsh = qa.Single(v => v.Name == "QSH").Opening;
        var list = new List<(string, string, JsonObject)>
        {
            ("M", "the shipped order (D34's Q2; D36's definition)", m),
            ("RSC", "D36's: QRD1 with the Shaman step moved just ahead of the Carver", rsc),
            ("QRD1", "QA's (D36's definition): M with throughReserve on the Rider and War-dog steps, the Shaman step's count 1", D36("QRD1")),
            ("K9KC10S", "D36's: QRD1 with the Carver at 9:00, the Circle at 10:00, the Circle and Shaman steps just ahead of the Rider step, throughReserve on the Shaman step", D36("K9KC10S")),
            ("QSH", "QA's: QRD1 with the Shaman step alone moved just ahead of the Breakers' Hall", qsh),
            ("QSR", "QA's: QRD1 with the Shaman step alone moved just ahead of the Rider step", qa.Single(v => v.Name == "QSR").Opening),
        };

        // --- Screen 1, around M (no switch; only the Shaman short): the Shaman step's count, position and switch.
        list.Add(("M1", "M with the Shaman step's count 1", Edit(m, c => Count(c, 1))));
        list.Add(("M1S", "M1 with throughReserve on the Shaman step", Edit(m, c => { Count(c, 1); Through(c, Shamans); })));
        list.Add(("M1SC", "M1 with the Shaman step moved just ahead of the Carver (RSC without its Rider and War-dog switches)", Edit(m, c => { Count(c, 1); MoveBefore(c, Carver, Shamans); })));
        list.Add(("M1SR", "M1 with the Shaman step moved just ahead of the Rider step", Edit(m, c => { Count(c, 1); MoveBefore(c, Rider, Shamans); })));
        list.Add(("M1SH", "M1 with the Shaman step moved just ahead of the Breakers' Hall (QSH without its switches)", Edit(m, c => { Count(c, 1); MoveBefore(c, BreakersHall, Shamans); })));
        list.Add(("M1SX", "M1 with the Shaman step moved just ahead of the expand step", Edit(m, c => { Count(c, 1); MoveBefore(c, Expand, Shamans); })));
        list.Add(("M1SHS", "M1SH with throughReserve on the Shaman step", Edit(m, c => { Count(c, 1); MoveBefore(c, BreakersHall, Shamans); Through(c, Shamans); })));
        list.Add(("M2SC", "M with the Shaman step (count 2) moved just ahead of the Carver", Edit(m, c => MoveBefore(c, Carver, Shamans))));
        list.Add(("M2SH", "M with the Shaman step (count 2) moved just ahead of the Breakers' Hall", Edit(m, c => MoveBefore(c, BreakersHall, Shamans))));
        // Which train steps carry the switch, from M: the Rider only, the War-dog only, the Rider and Shaman (D-H10's RS, count 1).
        list.Add(("M1R", "M1 with throughReserve on the Rider step", Edit(m, c => { Count(c, 1); Through(c, Rider); })));
        list.Add(("M1D", "M1 with throughReserve on the War-dog step", Edit(m, c => { Count(c, 1); Through(c, Dog); })));
        list.Add(("M1RS", "M1 with throughReserve on the Rider and Shaman steps (D-H10's RS with one Shaman)", Edit(m, c => { Count(c, 1); Through(c, Rider, Shamans); })));
        list.Add(("M1DS", "M1 with throughReserve on the War-dog and Shaman steps", Edit(m, c => { Count(c, 1); Through(c, Dog, Shamans); })));
        // The Circle's time and the Carver's time, from M1.
        list.Add(("M1C830", "M1 with the Shamans' Circle due at 8:30", Edit(m, c => { Count(c, 1); At(c, Circle, 510); })));
        list.Add(("M1K930", "M1 with the Carver due at 9:30", Edit(m, c => { Count(c, 1); At(c, Carver, 570); })));

        // --- Screen 1, around RSC (only the Anointing short): which train steps carry the switch, the Circle's, Carver's and
        // Anointing's time, the Anointing's position, the Shaman step's count.
        list.Add(("RSCR", "RSC without throughReserve on the War-dog step (the Rider step only)", Edit(rsc, c => NotThrough(c, Dog))));
        list.Add(("RSCD", "RSC without throughReserve on the Rider step (the War-dog step only)", Edit(rsc, c => NotThrough(c, Rider))));
        list.Add(("RSC2", "RSC with the Shaman step's count 2", Edit(rsc, c => Count(c, 2))));
        list.Add(("RSCC930", "RSC with the Shamans' Circle due at 9:30", Edit(rsc, c => At(c, Circle, 570))));
        list.Add(("RSCC10", "RSC with the Shamans' Circle due at 10:00", Edit(rsc, c => At(c, Circle, 600))));
        list.Add(("RSCC830", "RSC with the Shamans' Circle due at 8:30", Edit(rsc, c => At(c, Circle, 510))));
        list.Add(("RSCK930", "RSC with the Carver due at 9:30 (QA's QSCK)", Edit(rsc, c => At(c, Carver, 570))));
        list.Add(("RSCA930", "RSC with the Anointing due at 9:30", Edit(rsc, c => At(c, Anointing, 570))));
        list.Add(("RSCA", "RSC with the Anointing step moved just ahead of the Shaman step (D36's RSCA)", Edit(rsc, c => MoveBefore(c, Shamans, Anointing))));
        list.Add(("RSCAB", "RSC with the Anointing step moved just ahead of the Breaker step", Edit(rsc, c => MoveBefore(c, Breaker, Anointing))));
        list.Add(("RSCS", "RSC with throughReserve on the Shaman step", Edit(rsc, c => Through(c, Shamans))));

        // --- Screen 1, around QSH (QA's nearest; the Anointing short): the switches.
        list.Add(("QSHR", "QSH without throughReserve on the War-dog step", Edit(qsh, c => NotThrough(c, Dog))));
        list.Add(("QSHD", "QSH without throughReserve on the Rider step", Edit(qsh, c => NotThrough(c, Rider))));
        list.Add(("QSHC10", "QSH with the Shamans' Circle due at 10:00", Edit(qsh, c => At(c, Circle, 600))));

        // --- Screen 2 (after screen 1: throughReserve on the Rider step alone, with the Shaman step paid early or through,
        // keeps the Anointing and the Riders and loses a little of the Shamans; on the War-dog alone the reverse): the
        // Shaman step through as well, and D-H10's RS (two Shamans; its full Normal row is known) as the screen's calibrator.
        list.Add(("RS", "D-H10's RS: M with throughReserve on the Rider and Shaman steps (two Shamans)", Edit(m, c => Through(c, Rider, Shamans))));
        list.Add(("RSCRS", "RSCR with throughReserve on the Shaman step", Edit(rsc, c => { NotThrough(c, Dog); Through(c, Shamans); })));
        list.Add(("QSHRS", "QSHR with throughReserve on the Shaman step", Edit(qsh, c => { NotThrough(c, Dog); Through(c, Shamans); })));
        list.Add(("M1RSX", "M1RS with the Shaman step moved just ahead of the expand step", Edit(m, c => { Count(c, 1); Through(c, Rider, Shamans); MoveBefore(c, Expand, Shamans); })));

        return list.ToArray();
    }

    /// <summary>The variants <c>D37_VARIANTS</c> names (all when empty), in the class's order.</summary>
    private static (string Name, string What, JsonObject Opening)[] Variants()
    {
        string? only = Env("D37_VARIANTS");
        return AllVariants().Where(v => only == null || only.Split(',').Contains(v.Name)).ToArray();
    }

    /// <summary>
    /// Every order loads as the Teblor <c>standard</c> on a shipped copy with M's steps (as a multiset of action and id), M's
    /// other fields, no <c>noReserve</c> anywhere (expand and <c>armor_1</c> included), no switch written <c>false</c>, no
    /// <c>buildOrders</c> field; the borrowed orders are D36's and QA's; at most eight orders on the full Normal row.
    /// </summary>
    [Fact]
    public void EveryOrder_Loads_KeepsMsSteps_AndNeverSwitchesTheExpand()
    {
        JsonObject m = TeblorOpeningFixtureTests.D34Fixture();
        // D38 (BUG-1612): the shipped opening is D37's three orders now, so M (D34's order, the base's) is the shipped
        // opening with buildOrder put back to D34's and buildOrders taken out (was: M equals the shipped opening).
        JsonObject asBase = TeblorOpeningVariantsReportTests.Shipped();
        asBase.Remove("buildOrders");
        asBase["buildOrder"] = m["buildOrder"]!.DeepClone();
        Assert.True(JsonNode.DeepEquals(m, asBase), "M is not the shipped opening with D34's order");
        Assert.False(JsonNode.DeepEquals(m, TeblorOpeningVariantsReportTests.Shipped()), "M is still the shipped opening");
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
            Assert.Null(v.Opening["buildOrders"]);
            Assert.DoesNotContain(Order(v.Opening), s => s!["noReserve"] != null);
            Assert.DoesNotContain(Order(v.Opening), s => s!["throughReserve"] is JsonValue f && !(bool)f);
        }
        Assert.InRange(FullNormalRows.Length, 2, 8);
        Assert.Equal(new[] { "M", "RSC" }, FullNormalRows.Take(2));
        foreach (string name in FullNormalRows.Append(NormalOrder).Append(EasyOrder).Append(HardOrder)) Assert.Contains(name, names);
        var d36 = D36TeblorOrderSearchReportTests.AllVariants();
        foreach (string name in new[] { "M", "RSC", "QRD1", "K9KC10S" })
            Assert.True(JsonNode.DeepEquals(d36.Single(v => v.Name == name).Opening, AllVariants().Single(v => v.Name == name).Opening), $"{name} is not D36's");
        var qa = QA.Content.D36TeblorOrderQaStressTests.AllVariants();
        foreach (string name in new[] { "QSH", "QSR" })
            Assert.True(JsonNode.DeepEquals(qa.Single(v => v.Name == name).Opening, AllVariants().Single(v => v.Name == name).Opening), $"{name} is not QA's");
    }

    /// <summary>
    /// Each order at each difficulty of <c>D37_DIFFS</c> (default Normal) on the row: defeated / 2nd Longhouse / Shaman /
    /// Breaker / Rider / War-dog / Anointing, a miss of the bar marked "!", with the figures missed and the seats short.
    /// </summary>
    [Fact]
    public void Orders_Row_Report() =>
        Report(Variants(), (Env("D37_DIFFS") ?? "Normal").Split(',').Select(Enum.Parse<AiDifficulty>).ToArray(),
            (Env("D37_SEEDS") ?? "1,2").Split(',').Select(ulong.Parse).ToArray());

    /// <summary>Easy's order on Easy's full 64 seats and Hard's on Hard's (criterion (2): they must equal D36's rows).</summary>
    [Fact]
    public void EasyAndHard_Remeasured_Report()
    {
        ulong[] seeds = (Env("D37_SEEDS") ?? "1,2,3,4,5,6,7,8").Split(',').Select(ulong.Parse).ToArray();
        var all = AllVariants();
        Report(new[] { all.Single(v => v.Name == EasyOrder) }, new[] { AiDifficulty.Easy }, seeds);
        Report(new[] { all.Single(v => v.Name == HardOrder) }, new[] { AiDifficulty.Hard }, seeds);
    }

    private void Report((string Name, string What, JsonObject Opening)[] variants, AiDifficulty[] diffs, ulong[] seeds)
    {
        int threads = Math.Clamp(int.TryParse(Env("D37_THREADS"), out int n) ? n : 6, 1, 6);
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
        output.WriteLine($"D37: {jobs.Count} matches to 15:00 in {clock.Elapsed.TotalSeconds:0} s on {threads} threads, seeds {string.Join(",", seeds)} "
            + $"({seeds.Length * 8} seats a row; {(full ? "full rows" : "REDUCED rows: not judged against the bar")})");
        output.WriteLine("| order | difficulty | defeated | 2nd Longhouse | Shaman | Breaker | Rider | War-dog | Anointing | misses | short |");
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
                output.WriteLine($"| {v.Name} | {x} | {Cell(g.Count(r => r.DefeatedAt >= 0), bar.MaxDefeated, true)} | {Cell(g.Count(r => r.HallAt >= 0), bar.Hall)} | "
                    + $"{Cell(g.Count(r => r.Fielded[1]), bar.Shaman)} | {Cell(g.Count(r => r.Fielded[2]), bar.Breaker)} | {Cell(g.Count(r => r.Fielded[3]), bar.Rider)} | "
                    + $"{Cell(g.Count(r => r.Fielded[4]), bar.Dog)} | {Cell(g.Count(r => r.AnointedAt >= 0), bar.Anointing)} | {misses} | {shortBy} | (of {g.Count})");
            }
        output.WriteLine("Per map (defeated / Shaman / Breaker / Rider / War-dog / Anointing):");
        foreach (AiDifficulty x in diffs)
            foreach (var v in variants)
                output.WriteLine($"| {v.Name} | {x} | " + string.Join(" | ", maps.Select(mp =>
                {
                    var g = rows.Where(r => r.Variant == v.Name && r.Diff == x && r.Map == mp).ToList();
                    return $"{mp} {g.Count(r => r.DefeatedAt >= 0)}/{g.Count(r => r.Fielded[1])}/{g.Count(r => r.Fielded[2])}/{g.Count(r => r.Fielded[3])}/{g.Count(r => r.Fielded[4])}/{g.Count(r => r.AnointedAt >= 0)}";
                })) + " |");
        if (Env("D37_SEATS") != "1") return;
        output.WriteLine("Seats (order diff seed map opponent: defeatedAt hallAt anointedAt carverAt circleAt fielded[jav,sha,bre,rid,dog]):");
        foreach (var r in rows.OrderBy(r => r.Variant, StringComparer.Ordinal).ThenBy(r => r.Diff).ThenBy(r => r.Seed).ThenBy(r => r.Map, StringComparer.Ordinal).ThenBy(r => r.Opponent, StringComparer.Ordinal))
            output.WriteLine($"{r.Variant} {r.Diff} {r.Seed} {r.Map} {r.Opponent}: {r.DefeatedAt} {r.HallAt} {r.AnointedAt} {r.CarverAt} {r.CircleAt} {string.Join("", r.Fielded.Select(f => f ? '1' : '0'))}");
    }
}
