using System.Collections.Concurrent;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D38 (BUG-1612, rule 75): the three Teblor orders measured on the shipped file itself (the data as loaded, each seat
/// playing the list of its difficulty), on D34's row as D37 measured them (<see cref="D34TeblorStepOrderReportTests.Play"/>:
/// the Teblor against the Malazan on seat 0 and the Whirlwind on seat 1, the same difficulty on both seats, four maps,
/// seeds 1-8, to 15:00; 64 seats a difficulty). Each difficulty's seven figures (defeated / 2nd Longhouse / Shaman /
/// Breaker / Destrier Rider / War-dog / Anointing) must equal D37's row for that difficulty's order, read from teblor.md's
/// D37 table (never typed here): K9KC10S on Easy, M1RS on Normal, QRD1 on Hard. A sim change that moves Teblor play moves
/// these rows; the data task after it re-measures them (rule 41). <c>D38_DIFFS</c> (default all three), <c>D38_THREADS</c>
/// (default and cap 6) narrow a run; a run on fewer seeds is not possible (the comparison is the full row).
/// </summary>
[Trait("Category", "Stress")]
public class D38ShippedOrdersReportTests(ITestOutputHelper output)
{
    /// <summary>The order D37 named for each difficulty, as teblor.md's D37 table writes it.</summary>
    internal static string OrderOf(AiDifficulty x) => x switch
    {
        AiDifficulty.Easy => D37TeblorNormalOrderReportTests.EasyOrder,
        AiDifficulty.Normal => D37TeblorNormalOrderReportTests.NormalOrder,
        _ => D37TeblorNormalOrderReportTests.HardOrder,
    };

    /// <summary>D37's row of <paramref name="x"/>'s order on teblor.md ("The unit mix, per difficulty (BUG-1612, D37, ...)"), its seven figures.</summary>
    internal static string D37Row(AiDifficulty x)
    {
        string[] lines = PageTables.FactionLines("teblor");
        int h = PageTables.HeadingStartingWith(lines, "## The unit mix, per difficulty (BUG-1612, D37");
        Assert.True(h >= 0, "teblor.md has no D37 unit-mix section");
        foreach (List<string> table in PageTables.TablesInSection(lines, h))
            foreach (string row in table.Skip(2))
            {
                string[] c = PageTables.Cells(row);
                string name = c[0].Replace("**", "").Split(' ')[0];
                if (name == OrderOf(x) && c[1] == x.ToString()) return c[2];
            }
        Assert.Fail($"teblor.md's D37 table has no {OrderOf(x)} {x} row");
        return "";
    }

    [Fact(Skip = "BUG-1853: re-measured by the data track after M6-S14")]
    public void ShippedFile_SixtyFourSeats_EqualD37sRows_Report()
    {
        string? only = Environment.GetEnvironmentVariable("D38_DIFFS");
        AiDifficulty[] diffs = only is { Length: > 0 } ? only.Split(',').Select(Enum.Parse<AiDifficulty>).ToArray() : Enum.GetValues<AiDifficulty>();
        int threads = Math.Clamp(int.TryParse(Environment.GetEnvironmentVariable("D38_THREADS"), out int n) ? n : 6, 1, 6);
        GameData data = TestSim.Data;
        string[] maps = { "generated", "raraku", "pale_hills", "vathar_crossing" };
        string[] opponents = { "malazan", "whirlwind" };
        var jobs = new List<(string M, string O, ulong S, AiDifficulty X)>();
        foreach (AiDifficulty x in diffs)
            for (ulong seed = 1; seed <= 8; seed++)
                foreach (string mp in maps)
                    foreach (string o in opponents) jobs.Add((mp, o, seed, x));
        var rows = new D34TeblorStepOrderReportTests.Seat[jobs.Count];
        var errors = new ConcurrentQueue<string>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = threads }, i =>
        {
            var j = jobs[i];
            try { rows[i] = D34TeblorStepOrderReportTests.Play("shipped", data, j.M, j.O, j.S, j.X); }
            catch (Exception e) { errors.Enqueue($"{j.X} {j.S} {j.M} {j.O}: {e}"); }
        });
        Assert.True(errors.IsEmpty, string.Join("\n", errors));
        output.WriteLine($"D38: the shipped file ({data.ContentHash():X16}), {jobs.Count} matches to 15:00 in {clock.Elapsed.TotalSeconds:0} s on {threads} threads, seeds 1-8 (64 seats a difficulty)");
        output.WriteLine("| difficulty | order | defeated / 2nd Longhouse / Shaman / Breaker / Rider / War-dog / Anointing | D37's row | equal |");
        var differ = new List<string>();
        foreach (AiDifficulty x in diffs)
        {
            var g = rows.Where(r => r.Diff == x).ToList();
            string row = $"{g.Count(r => r.DefeatedAt >= 0)} / {g.Count(r => r.HallAt >= 0)} / {g.Count(r => r.Fielded[1])} / {g.Count(r => r.Fielded[2])} / "
                + $"{g.Count(r => r.Fielded[3])} / {g.Count(r => r.Fielded[4])} / {g.Count(r => r.AnointedAt >= 0)}";
            string d37 = D37Row(x);
            output.WriteLine($"| {x} | {OrderOf(x)} | {row} | {d37} | {(row == d37 ? "yes" : "NO")} |");
            if (row != d37) differ.Add($"{x} ({OrderOf(x)}): shipped {row}, D37 {d37}");
        }
        output.WriteLine("Per map (defeated / Shaman / Breaker / Rider / War-dog / Anointing, 16 seats a map):");
        foreach (AiDifficulty x in diffs)
            output.WriteLine($"| {x} | " + string.Join(" | ", maps.Select(mp =>
            {
                var g = rows.Where(r => r.Diff == x && r.Map == mp).ToList();
                return $"{mp} {g.Count(r => r.DefeatedAt >= 0)}/{g.Count(r => r.Fielded[1])}/{g.Count(r => r.Fielded[2])}/{g.Count(r => r.Fielded[3])}/{g.Count(r => r.Fielded[4])}/{g.Count(r => r.AnointedAt >= 0)}";
            })) + " |");
        Assert.True(differ.Count == 0, "the shipped file's rows differ from D37's: " + string.Join("; ", differ));
    }
}
