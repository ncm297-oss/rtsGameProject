using Rts.Sim.Data;
using Rts.Sim.Tests.Scenario;
using Xunit.Abstractions;
using static Rts.Sim.Tests.Scenario.CounterTriangleScene;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D6 balance report (M4-2b): how wide each counter-triangle win is at equal cost. The six group pairs of
/// <c>Scenario/CounterTriangleTests</c> run in both seats and its two siege rows run as there, all on the sim's shared
/// <see cref="CounterTriangleScene"/> (D8, BUG-0230 item 2 / BUG-0240: no copy of the scene here, so a scene change
/// reaches this report). The test prints the two markdown tables of the "Balance baseline" section of
/// docs/factions/malazan.md (every row) and whirlwind.md (the rows a Whirlwind unit wins), plus the scenario's own 14
/// lines for a byte diff against <c>Scenario/CounterTriangleTests</c>.
/// <para>
/// Two kinds of pin. The rule's: docs/02 "Faction template"'s winner wins (the margins are the owner's call, so no margin
/// is asserted). The page's (D8, BUG-0243): every printed row equals its page row cell for cell, so when the sim moves a
/// number the test fails naming the page, pair, seat and column, and the page is re-printed from this test's output.
/// </para>
/// </summary>
public class CounterTriangleMarginsTests
{
    private readonly ITestOutputHelper _out;

    public CounterTriangleMarginsTests(ITestOutputHelper output) => _out = output;

    /// <summary>The docs/02 counter pairs: the page's rule name, the scenario's, the doc's winner, its loser (as in <c>CounterTriangleTests</c>).</summary>
    private static readonly (string Rule, string ScenarioRule, string Winner, string Loser)[] Pairs =
    {
        ("Line beats Shock", "Line beats Shock", "malazan_heavy_infantry", "whirlwind_horse_raider"),
        ("Line beats Shock", "Line beats Shock", "whirlwind_raider", "malazan_wickan_lancer"),
        ("Shock beats Ranged", "Shock beats Ranged", "malazan_wickan_lancer", "whirlwind_desert_archer"),
        ("Shock beats Ranged", "Shock beats Ranged", "whirlwind_horse_raider", "malazan_crossbowman"),
        ("Ranged beats casters", "Ranged beats Light (casters)", "malazan_crossbowman", "whirlwind_priest"),
        ("Ranged beats casters", "Ranged beats Light (casters)", "whirlwind_desert_archer", "malazan_cadre_mage"),
    };

    /// <summary>docs/02 "Siege beats buildings": siege unit, the same cost of its faction's line infantry, the enemy building.</summary>
    private static readonly (string Siege, string Line, string Building)[] SiegeRows =
    {
        ("malazan_catapult", "malazan_heavy_infantry", "whirlwind_tent"),
        ("whirlwind_battering_ram", "whirlwind_raider", "malazan_billet"),
    };

    private const string GroupHeader = "| Rule | Winner v loser | Winner seat | Fielded (winner v loser) | Winner left | Winner hp left | Winner keeps (cost) | Time to last death |";
    private const string SiegeHeader = "| Siege unit | Same cost of line infantry | Building | Siege time | Line time | Siege / line |";

    /// <summary>One printed table row: the faction whose unit wins it (the page that carries it besides Malazan's), its key for messages, its markdown.</summary>
    internal readonly record struct Row(string WinnerFaction, string Key, string Markdown);

    /// <summary>Everything the harness gives, computed once for both facts.</summary>
    private sealed record Report(Row[] Group, Row[] Siege, string[] ScenarioLines, string[] WinnerFailures);

    private static readonly Lazy<Report> Measured = new(Measure);

    private static string Name(string key) => TestSim.Data.Units[TestSim.Data.FindUnit(key)].DisplayName;

    private static string Faction(string unitKey) => TestSim.Data.Factions[TestSim.Data.Units[TestSim.Data.FindUnit(unitKey)].Faction].Key;

    private static string Seconds(int ticks) => ticks == int.MaxValue ? "not done" : FormattableString.Invariant($"{ticks / 20f:F1} s");

    private static string Describe(Side s) => $"{s.Key} {s.Left}/{s.Fielded} left ({s.Hp} hp, cost {s.CostLeft} of {s.FieldedCost})";

    private static Report Measure()
    {
        GameData data = TestSim.Data;
        var group = new List<Row>();
        var siege = new List<Row>();
        var scenario = new List<string>();
        var failures = new List<string>();
        foreach ((string rule, string scenarioRule, string winner, string loser) in Pairs)
            for (int seat = 0; seat < 2; seat++)
            {
                (Side w, Side l, int ticks) = Fight(winner, loser, seat);
                int keeps = (int)Math.Round(100.0 * w.CostLeft / w.FieldedCost, MidpointRounding.AwayFromZero);
                group.Add(new Row(Faction(winner), $"{Name(winner)} v {Name(loser)} seat {seat}", FormattableString.Invariant(
                    $"| {rule} | {Name(winner)} v {Name(loser)} | {seat} | {w.Fielded} ({w.FieldedCost}) v {l.Fielded} ({l.FieldedCost}) | {w.Left} / {w.Fielded} | {w.Hp} | {w.CostLeft} / {w.FieldedCost} ({keeps} %) | {Seconds(ticks)} |")));
                // Scenario/CounterTriangleTests' line, character for character.
                scenario.Add($"{scenarioRule}, {winner} as player {seat}: {Describe(w)} v {Describe(l)} after {ticks} ticks ({ticks / 20f:F1} s)");
                if (!(w.Left > 0 && l.Left == 0))
                    failures.Add($"{rule}: {winner} as player {seat} left {w.Left}, {loser} left {l.Left} after {ticks} ticks");
            }
        foreach ((string siegeKey, string line, string building) in SiegeRows)
        {
            int n = SameCostCount(data, siegeKey, line);
            int s = TimeToKill(siegeKey, 1, building), l = TimeToKill(line, n, building);
            string ratio = s == int.MaxValue || l == int.MaxValue ? "-" : FormattableString.Invariant($"{100.0 * s / l:F0} %");
            siege.Add(new Row(Faction(siegeKey), $"1 {Name(siegeKey)} on a {data.Buildings[data.FindBuilding(building)].DisplayName}",
                $"| 1 {Name(siegeKey)} | {n} {Name(line)} | {data.Buildings[data.FindBuilding(building)].DisplayName} | {Seconds(s)} | {Seconds(l)} | {ratio} |"));
            string Show(int t) => t == int.MaxValue ? $"not in {SiegeMaxTicks} ticks" : $"{t} ticks ({t / 20f:F1} s)";
            scenario.Add($"{building}: 1 {siegeKey} {Show(s)}; {n} {line} {Show(l)}");
            if (!(s < l)) failures.Add($"Siege beats buildings: 1 {siegeKey} {Seconds(s)} is not faster than {n} {line} {Seconds(l)} on a {building}");
        }
        return new Report(group.ToArray(), siege.ToArray(), scenario.ToArray(), failures.ToArray());
    }

    [Fact]
    public void AllEightPairs_BothSeats_PrintTheMarginTable_AndTheDocWinnerWins()
    {
        Report r = Measured.Value;
        _out.WriteLine(GroupHeader);
        _out.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (Row row in r.Group) _out.WriteLine(row.Markdown);
        _out.WriteLine("");
        _out.WriteLine(SiegeHeader);
        _out.WriteLine("| --- | --- | --- | --- | --- | --- |");
        foreach (Row row in r.Siege) _out.WriteLine(row.Markdown);
        _out.WriteLine("");
        _out.WriteLine("Scenario/CounterTriangleTests' lines on the same harness:");
        foreach (string line in r.ScenarioLines) _out.WriteLine(line);
        Assert.True(r.WinnerFailures.Length == 0, "The doc's winner does not win:\n" + string.Join("\n", r.WinnerFailures));
    }

    [Theory]
    [InlineData("malazan")]
    [InlineData("whirlwind")]
    public void EveryPrintedRow_EqualsItsPageRow(string faction)
    {
        Report r = Measured.Value;
        string[] lines = PageTables.FactionLines(faction);
        string[] problems = PageProblems(faction, lines, r.Group, r.Siege);
        Assert.True(problems.Length == 0, $"docs/factions/{faction}.md \"Balance baseline\" differs from the harness (re-print it from " +
            "AllEightPairs_BothSeats_PrintTheMarginTable_AndTheDocWinnerWins' output):\n" + string.Join("\n", problems));
    }

    /// <summary>
    /// The pin can fail (D8 criterion 2): one page cell changed in memory (the Malazan Lancer seat-1 row's time or hp, the
    /// Whirlwind ram's line time) gives exactly one problem, naming the page, the pair, the seat and the column.
    /// </summary>
    [Theory]
    [InlineData("malazan", "| Wickan Lancer v Desert Archer | 1 |", "| 22.0 s |", "| 21.5 s |", "Wickan Lancer v Desert Archer seat 1", "Time to last death")]
    [InlineData("malazan", "| Wickan Lancer v Desert Archer | 1 |", "| 888 |", "| 912 |", "Wickan Lancer v Desert Archer seat 1", "Winner hp left")]
    [InlineData("whirlwind", "| Raider v Wickan Lancer | 0 |", "| 19 / 19 |", "| 18 / 19 |", "Raider v Wickan Lancer seat 0", "Winner left")]
    [InlineData("whirlwind", "| 1 Battering Ram |", "| 254.5 s |", "| 250.0 s |", "1 Battering Ram on a Billet", "Line time")]
    public void AMutatedPageCell_FailsNamingPairSeatAndColumn(string faction, string rowMark, string cell, string mutant, string key, string column)
    {
        Report r = Measured.Value;
        string[] lines = PageTables.FactionLines(faction);
        int i = Array.FindIndex(lines, l => l.Contains(rowMark, StringComparison.Ordinal));
        Assert.True(i >= 0 && lines[i].Contains(cell, StringComparison.Ordinal), $"{faction}.md: no row '{rowMark}' with '{cell}'");
        lines[i] = lines[i].Replace(cell, mutant);
        string[] problems = PageProblems(faction, lines, r.Group, r.Siege);
        Assert.True(problems.Length == 1, $"expected one problem, got {problems.Length}:\n" + string.Join("\n", problems));
        Assert.Contains(key, problems[0]);
        Assert.Contains($"column '{column}'", problems[0]);
        Assert.Contains(faction + ".md", problems[0]);
    }

    /// <summary>
    /// The Malazan page in memory with one group-table column dropped and, optionally, one Lancer seat-1 cell put back.
    /// Dropping column 5 ("Winner hp left") and putting 21.5 s back gives the group table as it stood before D8 (1a9925b):
    /// every other cell is the current page's, which is what that page had.
    /// </summary>
    private static string[] PreD8MalazanPage(int dropColumn, string? staleCell, string? stale)
    {
        string[] lines = PageTables.FactionLines("malazan");
        int i = PageTables.HeadingStartingWith(lines, "## Balance baseline");
        while (!lines[i].StartsWith('|')) i++;
        Assert.True(lines[i] == GroupHeader, $"malazan.md group header is '{lines[i]}', expected the printed one");
        for (; i < lines.Length && lines[i].StartsWith('|'); i++)
        {
            List<string> cells = PageTables.Cells(lines[i]).ToList();
            cells.RemoveAt(dropColumn);
            lines[i] = "| " + string.Join(" | ", cells) + " |";
            if (staleCell != null && lines[i].Contains("| Wickan Lancer v Desert Archer | 1 |", StringComparison.Ordinal))
            {
                Assert.Contains(staleCell, lines[i]);
                lines[i] = lines[i].Replace(staleCell, stale!);
            }
        }
        return lines;
    }

    /// <summary>
    /// BUG-0290: a header change no longer hides the rows. The pre-D8 page reports its header (naming the missing column)
    /// and, in the same run, the stale 21.5 s cell by pair, seat and column.
    /// </summary>
    [Fact]
    public void BUG0290_AHeaderMismatch_StillReportsTheStaleCells()
    {
        Report r = Measured.Value;
        string[] problems = PageProblems("malazan", PreD8MalazanPage(5, "| 22.0 s |", "| 21.5 s |"), r.Group, r.Siege);
        Assert.True(problems.Length == 2, $"expected the header and one cell, got {problems.Length}:\n" + string.Join("\n", problems));
        Assert.StartsWith("malazan.md group table: header '| Rule | Winner v loser | Winner seat | Fielded (winner v loser) | Winner left | Winner keeps (cost) | Time to last death |'", problems[0]);
        Assert.Contains("page lacks 'Winner hp left'", problems[0]);
        Assert.Contains("rows compared on the shared columns", problems[0]);
        Assert.Equal("malazan.md group table: Wickan Lancer v Desert Archer seat 1, column 'Time to last death': page '21.5 s' vs sim '22.0 s'", problems[1]);
    }

    /// <summary>BUG-0290: when the page lacks a column that names a row, the header problem says the rows were not checked.</summary>
    [Fact]
    public void BUG0290_AHeaderWithoutARowKey_SaysTheRowsAreUnchecked()
    {
        Report r = Measured.Value;
        string[] problems = PageProblems("malazan", PreD8MalazanPage(2, null, null), r.Group, r.Siege);
        Assert.True(problems.Length == 1, $"expected one problem, got {problems.Length}:\n" + string.Join("\n", problems));
        Assert.Contains("page lacks 'Winner seat'", problems[0]);
        Assert.Contains("rows not checked (the page lacks the row key 'Winner seat')", problems[0]);
    }

    /// <summary>
    /// Compares the "## Balance baseline" section of a page with the printed rows: Malazan's page carries every row,
    /// another faction's page the rows its own unit wins. Each expected row must be on the page once with every cell
    /// equal, both headers must be the printed ones, and the page may have no row the harness does not print.
    /// </summary>
    internal static string[] PageProblems(string faction, string[] lines, Row[] group, Row[] siege)
    {
        var problems = new List<string>();
        int i = PageTables.HeadingStartingWith(lines, "## Balance baseline");
        if (i < 0) return new[] { $"{faction}.md has no '## Balance baseline' heading" };
        List<List<string>> tables = PageTables.TablesInSection(lines, i);
        if (tables.Count != 2) return new[] { $"{faction}.md \"Balance baseline\": {tables.Count} tables, expected 2 (groups, siege)" };
        Compare(faction, "group", GroupHeader, tables[0], group, problems);
        Compare(faction, "siege", SiegeHeader, tables[1], siege, problems);
        return problems.ToArray();
    }

    /// <summary>
    /// One table against its printed rows. A header that differs is reported with the columns it lacks or adds, and the
    /// rows are still compared on the columns both share, matched by name (BUG-0290: a re-print then fixes the header and
    /// every stale cell in one round). Only when the page lacks a column that names a row are the rows left unchecked,
    /// and the message says so.
    /// </summary>
    private static void Compare(string faction, string table, string header, List<string> page, Row[] printed, List<string> problems)
    {
        string where = $"{faction}.md {table} table";
        string[] columns = PageTables.Cells(header);
        string[] pageColumns = PageTables.Cells(page[0]);
        // For each printed column, where the page has it (-1: not on the page).
        int[] at = columns.Select(c => Array.IndexOf(pageColumns, c)).ToArray();
        // Group rows are keyed by "Winner v loser" + seat, siege rows by the siege unit: the columns that name the row.
        int[] keyColumns = table == "group" ? new[] { 1, 2 } : new[] { 0 };
        if (page[0] != header)
        {
            string missing = string.Join(", ", columns.Where(c => !pageColumns.Contains(c)).Select(c => $"'{c}'"));
            string extra = string.Join(", ", pageColumns.Where(c => !columns.Contains(c)).Select(c => $"'{c}'"));
            string[] unkeyed = keyColumns.Where(k => at[k] < 0).Select(k => $"'{columns[k]}'").ToArray();
            problems.Add($"{where}: header '{page[0]}' vs printed '{header}'" +
                (missing.Length > 0 ? $"; page lacks {missing}" : "") + (extra.Length > 0 ? $"; page adds {extra}" : "") +
                (unkeyed.Length > 0 ? $"; rows not checked (the page lacks the row key {string.Join(", ", unkeyed)})" : "; rows compared on the shared columns"));
            if (unkeyed.Length > 0) return;
        }
        string[][] pageRows = page.Skip(2).Select(r => PageTables.Cells(r)).ToArray();
        string PrintedKey(string[] c) => table == "group" ? $"{c[1]} seat {c[2]}" : c[0];
        string PageKey(string[] c) => table == "group" ? $"{c[at[1]]} seat {c[at[2]]}" : c[at[0]];
        var expected = printed.Where(r => faction == "malazan" || r.WinnerFaction == faction).ToArray();
        foreach (Row row in expected)
        {
            string[] want = PageTables.Cells(row.Markdown);
            string k = PrintedKey(want);
            string[][] found = pageRows.Where(c => c.Length == pageColumns.Length && PageKey(c) == k).ToArray();
            if (found.Length != 1) { problems.Add($"{where}: {row.Key}: {found.Length} page rows, expected 1"); continue; }
            for (int c = 0; c < want.Length; c++)
                if (at[c] >= 0 && found[0][at[c]] != want[c])
                    problems.Add($"{where}: {row.Key}, column '{columns[c]}': page '{found[0][at[c]]}' vs sim '{want[c]}'");
        }
        var expectedKeys = expected.Select(r => PrintedKey(PageTables.Cells(r.Markdown))).ToHashSet(StringComparer.Ordinal);
        foreach (string[] c in pageRows)
            if (c.Length != pageColumns.Length || !expectedKeys.Contains(PageKey(c)))
                problems.Add($"{where}: page row '| {string.Join(" | ", c)} |' is not a row the harness prints for this page");
    }
}
