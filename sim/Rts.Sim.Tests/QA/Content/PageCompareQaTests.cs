using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA D-H1 (BUG-0290 fix): <c>CounterTriangleMarginsTests.PageProblems</c> matches columns by name. These feed it the
/// Malazan page's own rows as the "printed" rows (no fight harness needed: <c>EveryPrintedRow_EqualsItsPageRow</c> pins
/// them equal to the harness), then edit the page in memory: reordered columns must not invent cell problems, a stale
/// cell in a reordered table is still named, and a renamed row-key column says the rows were not checked.
/// </summary>
public class PageCompareQaTests
{
    private static (string[] Lines, CounterTriangleMarginsTests.Row[] Group, CounterTriangleMarginsTests.Row[] Siege) Page()
    {
        string[] lines = PageTables.FactionLines("malazan");
        int h = PageTables.HeadingStartingWith(lines, "## Balance baseline");
        Assert.True(h >= 0, "malazan.md has no '## Balance baseline' heading");
        List<List<string>> tables = PageTables.TablesInSection(lines, h);
        Assert.Equal(2, tables.Count);
        CounterTriangleMarginsTests.Row[] group = tables[0].Skip(2).Select(r =>
        {
            string[] c = PageTables.Cells(r);
            return new CounterTriangleMarginsTests.Row("malazan", $"{c[1]} seat {c[2]}", r);
        }).ToArray();
        CounterTriangleMarginsTests.Row[] siege = tables[1].Skip(2)
            .Select(r => new CounterTriangleMarginsTests.Row("malazan", PageTables.Cells(r)[0], r)).ToArray();
        return (lines, group, siege);
    }

    /// <summary>Rewrites every row of the first table after the balance heading through <paramref name="edit"/>.</summary>
    private static string[] EditGroupTable(string[] lines, Func<string, List<string>, List<string>> edit)
    {
        string[] copy = (string[])lines.Clone();
        int i = PageTables.HeadingStartingWith(copy, "## Balance baseline");
        while (!copy[i].StartsWith('|')) i++;
        for (; i < copy.Length && copy[i].StartsWith('|'); i++)
            copy[i] = "| " + string.Join(" | ", edit(copy[i], PageTables.Cells(copy[i]).ToList())) + " |";
        return copy;
    }

    private static List<string> Swap45(string line, List<string> c)
    {
        (c[4], c[5]) = (c[5], c[4]);
        return c;
    }

    [Fact]
    public void UnchangedPage_HasNoProblems()
    {
        var (lines, group, siege) = Page();
        Assert.Empty(CounterTriangleMarginsTests.PageProblems("malazan", lines, group, siege));
    }

    [Fact]
    public void ReorderedColumns_ReportOnlyTheHeader_NoCellProblems()
    {
        var (lines, group, siege) = Page();
        string[] problems = CounterTriangleMarginsTests.PageProblems("malazan", EditGroupTable(lines, Swap45), group, siege);
        Assert.True(problems.Length == 1, string.Join("\n", problems));
        Assert.EndsWith("rows compared on the shared columns", problems[0]);
        Assert.DoesNotContain("lacks", problems[0]);
        Assert.DoesNotContain("adds", problems[0]);
    }

    [Fact]
    public void ReorderedColumns_WithAStaleCell_NameTheCellByColumnName()
    {
        var (lines, group, siege) = Page();
        string[] edited = EditGroupTable(lines, (line, c) =>
        {
            c = Swap45(line, c);
            if (c[1] == "Wickan Lancer v Desert Archer" && c[2] == "1") c[7] = "99.9 s";
            return c;
        });
        string[] problems = CounterTriangleMarginsTests.PageProblems("malazan", edited, group, siege);
        Assert.True(problems.Length == 2, string.Join("\n", problems));
        Assert.StartsWith("malazan.md group table: Wickan Lancer v Desert Archer seat 1, column 'Time to last death': page '99.9 s' vs sim '", problems[1]);
    }

    [Fact]
    public void AnExtraPageColumn_IsReported_AndRowsStillCompared()
    {
        var (lines, group, siege) = Page();
        string[] edited = EditGroupTable(lines, (line, c) =>
        {
            c.Add(c[0] == "Rule" ? "Notes" : c[0] == "---" ? "---" : "x");
            if (c[1] == "Heavy Infantry v Horse Raider" && c[2] == "0") c[4] = "1 / 16";
            return c;
        });
        string[] problems = CounterTriangleMarginsTests.PageProblems("malazan", edited, group, siege);
        Assert.True(problems.Length == 2, string.Join("\n", problems));
        Assert.Contains("page adds 'Notes'; rows compared on the shared columns", problems[0]);
        Assert.Contains("Heavy Infantry v Horse Raider seat 0, column 'Winner left': page '1 / 16'", problems[1]);
    }

    [Fact]
    public void ARenamedSiegeKeyColumn_SaysRowsNotChecked()
    {
        var (lines, group, siege) = Page();
        string[] edited = (string[])lines.Clone();
        int i = Array.FindIndex(edited, l => l.StartsWith("| Siege unit |", StringComparison.Ordinal));
        Assert.True(i >= 0, "malazan.md has no siege header");
        edited[i] = edited[i].Replace("| Siege unit |", "| Engine |");
        string[] problems = CounterTriangleMarginsTests.PageProblems("malazan", edited, group, siege);
        Assert.True(problems.Length == 1, string.Join("\n", problems));
        Assert.EndsWith("page lacks 'Siege unit'; page adds 'Engine'; rows not checked (the page lacks the row key 'Siege unit')", problems[0]);
    }
}
