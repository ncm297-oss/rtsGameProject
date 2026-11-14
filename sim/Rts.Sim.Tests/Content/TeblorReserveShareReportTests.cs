using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Xunit.Abstractions;
using Row = Rts.Sim.Tests.Content.TeblorOpeningHorizonReportTests.Row;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D-H8: V24 (D30's provisional opening for D31) re-measured with the reserve share on its own opening
/// (<c>attack.reserveArmyShare</c>, M7-6), the opponent left at <c>rules.json</c>'s shared 1, at 0.25, 0.5 and 0.75, beside
/// D30's two V24 rows re-run in the same harness: the shared share 1 (shipped) and the global 0.5 (D30's stand-in, which
/// moved the opponent's gate too). Every row with <see cref="TeblorLanding"/>'s Frenzy rows (D30: on or off gives V24 the
/// same row). D30's harness (<see cref="TeblorOpeningHorizonReportTests.Play"/>, Normal against Normal to 15:00, the Teblor
/// on seat 0 against the Malazan and seat 1 against the Whirlwind, four maps), seeds 1-8, 64 matches a row. A measurement
/// (rule 48): only preconditions are asserted, no bound; the Producer picks at D31. Runs on the sim's rules as they are on
/// this tree (no Frenzy cast rule beyond <c>main</c>'s).
/// </summary>
[Trait("Category", "Stress")]
public class TeblorReserveShareReportTests(ITestOutputHelper output)
{
    private const int Ten = 12_000;

    private static readonly string[] Maps = { "generated", "raraku", "pale_hills", "vathar_crossing" };
    private static readonly string[] Opponents = { "malazan", "whirlwind" };
    private static readonly ulong[] Seeds = { 1, 2, 3, 4, 5, 6, 7, 8 };

    /// <summary>The opening's own shares measured.</summary>
    public static readonly double[] OwnShares = { 0.25, 0.5, 0.75 };

    private static JsonObject V24()
    {
        var v = TeblorOpeningHorizonReportTests.Variants().Where(x => x.Name == "V24").ToArray();
        Assert.True(v.Length == 1, "no variant V24 (is D29_VARIANTS set without it?)");
        return JsonNode.Parse(v[0].Opening.ToJsonString())!.AsObject();
    }

    /// <summary>
    /// The shipped data with the landing's rows and V24 as the Teblor's opening: <paramref name="own"/> on the opening's
    /// <c>attack</c> when given, <paramref name="global"/> as <c>rules.json</c>'s <c>ai.reserveArmyShare</c> when given.
    /// </summary>
    internal static GameData Make(double? own, double? global) => AiScenes.DataWith(dir =>
    {
        TeblorLanding.Edit(dir);
        JsonObject opening = V24();
        if (own is double s) opening["attack"]!["reserveArmyShare"] = s;
        dir.EditJson("factions/teblor/ai.json", root => root["openings"]![0] = opening);
        if (global is double g) dir.EditJson("common/rules.json", root => root["ai"]!["reserveArmyShare"] = g);
    });

    /// <summary>The configurations: D30's two rows, then the three own shares.</summary>
    internal static (string Name, GameData Data)[] Configs()
    {
        var list = new List<(string, GameData)> { ("V24, shared share 1 (D30)", Make(null, null)), ("V24, global share 0.5 (D30)", Make(null, 0.5)) };
        foreach (double s in OwnShares) list.Add(($"V24, own share {s}", Make(s, null)));
        return list.ToArray();
    }

    /// <summary>
    /// The preconditions: every row has the landing's rows and V24; the own-share rows carry the share on the Teblor
    /// opening only, the shared value 1 for everyone else (no other opening of any faction carries one).
    /// </summary>
    [Fact]
    public void TheConfigurations_PutTheShareWhereTheyClaim()
    {
        float shipped = TestSim.Data.AiRules.ReserveArmyShare;
        Assert.Equal(1f, shipped);
        foreach ((string name, GameData d) in Configs())
        {
            Assert.True(d.FindStatus(TeblorLanding.Status) >= 0, name);
            int teblor = d.FindFaction("teblor");
            AiOpening t = d.Ai[teblor].Openings[0];
            Assert.Equal("standard", t.Id);
            for (int f = 0; f < d.Factions.Length; f++)
                for (int i = 0; i < d.Ai[f].Openings.Length; i++)
                    if (f != teblor || i != 0) Assert.Null(d.Ai[f].Openings[i].ReserveArmyShare);
            if (name.Contains("own share", StringComparison.Ordinal))
            {
                Assert.Equal(1f, d.AiRules.ReserveArmyShare);
                Assert.Contains((double)t.ReserveArmyShare!.Value, OwnShares.Select(s => (double)(float)s));
            }
            else
            {
                Assert.Null(t.ReserveArmyShare);
                Assert.Equal(name.Contains("0.5", StringComparison.Ordinal) ? 0.5f : 1f, d.AiRules.ReserveArmyShare);
            }
        }
    }

    private static bool Met(IEnumerable<Row> seed) => seed.Count(r => r.Age10) >= 6 && seed.Count(r => r.Wave10) >= 6;

    /// <summary>The five rows, 320 matches: D30's columns, then the per-seed figures.</summary>
    [Fact]
    public void V24_OwnReserveShare_Seeds1To8_Report()
    {
        (string Name, GameData Data)[] configs = Configs();
        var jobs = new List<(string, GameData, string, string, ulong, bool)>();
        foreach (var c in configs)
            foreach (ulong seed in Seeds)
                foreach (string m in Maps)
                    foreach (string o in Opponents) jobs.Add((c.Name, c.Data, m, o, seed, false));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        List<Row> rows = TeblorOpeningHorizonReportTests.PlayAll(jobs);
        Assert.Equal(configs.Length * 64, rows.Count);
        output.WriteLine($"D-H8, V24's reserve share: {jobs.Count} matches to 15:00 in {clock.Elapsed.TotalSeconds:0} s (seeds 1-8; four maps; Normal v Normal; the landing's rows in every row)");
        output.WriteLine("");
        output.WriteLine("D30's columns: seeds met · Age II by 10:00 · wave by 10:00 · expanded · every building kind · Anointing · defeated by 15:00 · army pop at 15:00 · Frenzy casts landed a match");
        output.WriteLine("");
        output.WriteLine("| configuration | D30's columns | casts sent / match | of the defeats, after 10:00 | army pop 10:00 |");
        output.WriteLine("| --- | --- | --- | --- | --- |");
        foreach ((string name, _) in configs)
        {
            List<Row> g = rows.Where(r => r.Variant == name).ToList();
            int met = g.GroupBy(r => r.Seed).Count(Met);
            output.WriteLine($"| {name} | {met}/8 · {g.Count(r => r.Age10)} · {g.Count(r => r.Wave10)} · {g.Count(r => r.ExpandedAt >= 0)} · {g.Count(r => r.NeverBuilt == "-")} · "
                + $"{g.Count(r => r.AnointedAt >= 0)} · {g.Count(r => r.Lost)} · {g.Sum(r => r.Army15) / 2.0} · {g.Average(r => r.CastsLanded):0.00} | {g.Average(r => r.CastsSent):0.00} | "
                + $"{g.Count(r => r.DefeatedAt > Ten)} | {g.Sum(r => r.Army10) / 2.0} |");
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
        output.WriteLine("Waves started by 15:00 and the defeats per row (seed, map, opponent, time):");
        foreach ((string name, _) in configs)
        {
            List<Row> g = rows.Where(r => r.Variant == name).ToList();
            string lost = string.Join(", ", g.Where(r => r.Lost).OrderBy(r => r.DefeatedAt).Select(r => $"s{r.Seed} {r.Map} v {r.Opponent} {TeblorOpeningHorizonReportTests.T(r.DefeatedAt)}"));
            output.WriteLine($"- {name}: waves by 15:00 {g.Sum(r => r.Waves15)}; defeated [{lost}]");
        }
    }
}
