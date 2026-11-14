using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Xunit.Abstractions;
using Row = Rts.Sim.Tests.Content.TeblorOpeningHorizonReportTests.Row;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D31 (rule 41): the reserve share re-measured on the tree that ships: the landing's rows, V24 as the Teblor opening and
/// the Gatherer's <c>carry</c> 15 all in the shipped files. Three rows in D-H8's harness
/// (<see cref="TeblorOpeningHorizonReportTests.Play"/>, Normal against Normal to 15:00, the Teblor on seat 0 against the
/// Malazan and seat 1 against the Whirlwind, four maps, seeds 1-8, 64 matches a row): the shipped data as it is (V24, own
/// share 0.25); V24 with its own share 0.5 (<see cref="TeblorReserveShareReportTests.Make"/> on the shipped data); and the
/// shipped data with one line changed, the Age II composition's <c>teblor_shaman</c> weight (scope item 4), with the two
/// Shaman columns (matches that field a Shaman before 15:00, casts landed). D-H8's own-share rows (without the carry) are
/// printed beside them as literals. A measurement (rule 48): only preconditions are asserted; the share's rule (6 of 8
/// seeds) is the brief's, read off the printed rows. <c>D31_ROWS</c> (comma-separated: <c>shipped</c>, <c>own05</c>,
/// <c>shamans</c>) runs a subset.
/// </summary>
[Trait("Category", "Stress")]
public class TeblorLandedShareReportTests(ITestOutputHelper output)
{
    private const int Ten = 12_000;

    private static readonly string[] Maps = { "generated", "raraku", "pale_hills", "vathar_crossing" };
    private static readonly string[] Opponents = { "malazan", "whirlwind" };
    private static readonly ulong[] Seeds = { 1, 2, 3, 4, 5, 6, 7, 8 };

    /// <summary>The Shaman weight the third row tries in the Age II composition (shipped: 10).</summary>
    internal const int ShamanWeight = 25;

    /// <summary>D-H8's two own-share rows (2026-11-13 session log, without the carry), D30's columns.</summary>
    private static readonly (string Name, string Row)[] DH8 =
    {
        ("D-H8: V24, own share 0.25, no carry", "6/8 · 46 · 55 · 5 · 8 · 11 · 17 · 56 · 0.03"),
        ("D-H8: V24, own share 0.5, no carry", "6/8 · 44 · 55 · 5 · 0 · 11 · 16 · 36 · 0.09"),
    };

    /// <summary>The shipped data with the Age II composition's Shaman weight set to <see cref="ShamanWeight"/>.</summary>
    internal static GameData MoreShamans() => AiScenes.DataWithAi("teblor", root =>
    {
        JsonObject two = root["openings"]![0]!["compositions"]!.AsArray().Single(c => (int)c!["age"]! == 2)!.AsObject();
        two["units"]!["teblor_shaman"] = ShamanWeight;
    });

    private static (string Key, string Name, Func<GameData> Data)[] Configs()
    {
        var all = new (string, string, Func<GameData>)[]
        {
            ("shipped", "shipped: V24, own share 0.25, carry 15", () => AiScenes.DataWith(_ => { })),
            ("own05", "V24, own share 0.5, carry 15", () => TeblorReserveShareReportTests.Make(0.5, null)),
            ("shamans", $"shipped, Age II Shaman weight {ShamanWeight}", MoreShamans),
        };
        string? only = Environment.GetEnvironmentVariable("D31_ROWS");
        if (string.IsNullOrEmpty(only)) return all;
        var keep = new HashSet<string>(only.Split(','));
        return all.Where(c => keep.Contains(c.Item1)).ToArray();
    }

    /// <summary>The preconditions: the shipped data is the landing (status row, carry 15 on the Gatherer, V24 at 0.25), and each row differs from it only where it says.</summary>
    [Fact]
    public void TheRows_AreTheTreeThatShips_PlusTheirOneChange()
    {
        GameData shipped = TestSim.Data;
        Assert.True(shipped.FindStatus(TeblorLanding.Status) >= 0);
        int teblor = shipped.FindFaction("teblor");
        Assert.Equal(15, shipped.CarryOf(shipped.FindUnit("teblor_gatherer")));
        Assert.Equal(0.25f, shipped.Ai[teblor].Openings[0].ReserveArmyShare);

        GameData half = TeblorReserveShareReportTests.Make(0.5, null);
        Assert.Equal(0.5f, half.Ai[teblor].Openings[0].ReserveArmyShare);
        Assert.Equal(15, half.CarryOf(half.FindUnit("teblor_gatherer")));

        GameData more = MoreShamans();
        Assert.Equal(0.25f, more.Ai[teblor].Openings[0].ReserveArmyShare);
        Assert.NotEqual(shipped.ContentHash(), more.ContentHash());
    }

    private static bool Met(IEnumerable<Row> seed) => seed.Count(r => r.Age10) >= 6 && seed.Count(r => r.Wave10) >= 6;

    /// <summary>The three rows (192 matches): D30's columns, the Shaman columns, then the per-seed figures.</summary>
    [Fact]
    public void LandedTree_ReserveShareAndShamans_Seeds1To8_Report()
    {
        var configs = Configs().Select(c => (c.Name, Data: c.Data())).ToArray();
        int shaman = Array.IndexOf(new[] { "teblor_javelin_thrower", "teblor_shaman", "teblor_breaker", "teblor_destrier_rider", "teblor_war_dog" }, "teblor_shaman");
        var jobs = new List<(string, GameData, string, string, ulong, bool)>();
        foreach (var c in configs)
            foreach (ulong seed in Seeds)
                foreach (string m in Maps)
                    foreach (string o in Opponents) jobs.Add((c.Name, c.Data, m, o, seed, false));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        List<Row> rows = TeblorOpeningHorizonReportTests.PlayAll(jobs);
        Assert.Equal(configs.Length * 64, rows.Count);
        output.WriteLine($"D31, the landed tree: {jobs.Count} matches to 15:00 in {clock.Elapsed.TotalSeconds:0} s (seeds 1-8; four maps; Normal v Normal)");
        output.WriteLine("");
        output.WriteLine("D30's columns: seeds met · Age II by 10:00 · wave by 10:00 · expanded · every building kind · Anointing · defeated by 15:00 · army pop at 15:00 · Frenzy casts landed a match");
        output.WriteLine("");
        output.WriteLine("| configuration | D30's columns | matches fielding a Shaman by 15:00 | casts landed (total) |");
        output.WriteLine("| --- | --- | --- | --- |");
        foreach ((string name, string row) in DH8) output.WriteLine($"| {name} | {row} | - | - |");
        foreach ((string name, _) in configs)
        {
            List<Row> g = rows.Where(r => r.Variant == name).ToList();
            int met = g.GroupBy(r => r.Seed).Count(Met);
            output.WriteLine($"| {name} | {met}/8 · {g.Count(r => r.Age10)} · {g.Count(r => r.Wave10)} · {g.Count(r => r.ExpandedAt >= 0)} · {g.Count(r => r.NeverBuilt == "-")} · "
                + $"{g.Count(r => r.AnointedAt >= 0)} · {g.Count(r => r.Lost)} · {g.Sum(r => r.Army15) / 2.0} · {g.Average(r => r.CastsLanded):0.00} | "
                + $"{g.Count(r => r.Fielded[shaman])} | {g.Sum(r => r.CastsLanded)} |");
        }
        output.WriteLine("");
        output.WriteLine("Per seed (8 pairings each): Age II 10:00 / wave 10:00 / defeated 15:00 / army pop 15:00.");
        output.WriteLine("| configuration | " + string.Join(" | ", Seeds.Select(s => $"seed {s}")) + " |");
        output.WriteLine("| --- " + string.Concat(Seeds.Select(_ => "| --- ")) + "|");
        foreach ((string name, _) in configs)
        {
            var cells = Seeds.Select(s =>
            {
                List<Row> g = rows.Where(r => r.Variant == name && r.Seed == s).ToList();
                return $"{g.Count(r => r.Age10)} / {g.Count(r => r.Wave10)} / {g.Count(r => r.Lost)} / {g.Sum(r => r.Army15) / 2.0}";
            });
            output.WriteLine($"| {name} | " + string.Join(" | ", cells) + " |");
        }
        output.WriteLine("");
        output.WriteLine("Waves started by 15:00, the defeats after 10:00, and the defeats per row (seed, map, opponent, time):");
        foreach ((string name, _) in configs)
        {
            List<Row> g = rows.Where(r => r.Variant == name).ToList();
            string lost = string.Join(", ", g.Where(r => r.Lost).OrderBy(r => r.DefeatedAt).Select(r => $"s{r.Seed} {r.Map} v {r.Opponent} {TeblorOpeningHorizonReportTests.T(r.DefeatedAt)}"));
            output.WriteLine($"- {name}: waves by 15:00 {g.Sum(r => r.Waves15)}; after 10:00 {g.Count(r => r.DefeatedAt > Ten)}; defeated [{lost}]");
        }
    }
}
