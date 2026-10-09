namespace Rts.Sim.Tests.Content;

/// <summary>
/// The low-level markdown readers every content pin shares (D-H1): a doc's lines, a heading's line, a section's tables
/// and a table row's cells. <see cref="FactionPage"/>, <c>CounterTriangleMarginsTests</c> and <c>TechContentTests</c>
/// read the pages through these, so a page-format quirk is handled in one place.
/// </summary>
internal static class PageTables
{
    /// <summary>The lines of a markdown file under the repo root (<c>docs/02-game-design.md</c>).</summary>
    public static string[] Lines(string relativePath) => File.ReadAllLines(Path.Combine(TestDataDir.RepoRoot(), relativePath));

    /// <summary>The lines of <c>docs/factions/&lt;faction&gt;.md</c>.</summary>
    public static string[] FactionLines(string faction) => Lines(FactionPath(faction));

    /// <summary><c>docs/factions/&lt;faction&gt;.md</c>, relative to the repo root.</summary>
    public static string FactionPath(string faction) => Path.Combine("docs", "factions", faction + ".md");

    /// <summary>
    /// The index of the line that is exactly <paramref name="heading"/> (with its <c>#</c>s); fails naming
    /// <paramref name="where"/> when there is none.
    /// </summary>
    public static int Heading(string[] lines, string heading, string where)
    {
        int i = Array.IndexOf(lines, heading);
        Assert.True(i >= 0, $"{where} has no '{heading}' heading");
        return i;
    }

    /// <summary>The index of the first line starting with <paramref name="prefix"/> (a heading with a dated suffix), or -1.</summary>
    public static int HeadingStartingWith(string[] lines, string prefix) =>
        Array.FindIndex(lines, l => l.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>
    /// Every table between line <paramref name="heading"/> and the next <c>## </c> heading, each as its trimmed lines
    /// (header and separator included), in page order.
    /// </summary>
    public static List<List<string>> TablesInSection(string[] lines, int heading)
    {
        var tables = new List<List<string>>();
        for (int i = heading + 1; i < lines.Length && !lines[i].StartsWith("## ", StringComparison.Ordinal); i++)
        {
            if (!lines[i].StartsWith('|')) continue;
            if (i == 0 || !lines[i - 1].StartsWith('|')) tables.Add(new List<string>());
            tables[^1].Add(lines[i].Trim());
        }
        return tables;
    }

    /// <summary>
    /// The body rows (header and separator dropped) of the first table at or after line <paramref name="from"/>, as cells.
    /// </summary>
    public static List<string[]> FirstTableRows(string[] lines, int from, bool stripBackticks)
    {
        int i = from;
        while (i < lines.Length && !lines[i].StartsWith('|')) i++;
        var rows = new List<string[]>();
        for (i += 2; i < lines.Length && lines[i].StartsWith('|'); i++)
            rows.Add(Cells(lines[i], stripBackticks));
        return rows;
    }

    /// <summary>
    /// The header cells and body rows of the first table under <c>## <paramref name="section"/></c> (a faction page's
    /// "Abilities"), so a pin can find its columns by name; fails naming <paramref name="where"/> when the heading is missing.
    /// </summary>
    public static (string[] Header, List<string[]> Rows) NamedTable(string[] lines, string section, string where)
    {
        int i = Heading(lines, "## " + section, where);
        while (i < lines.Length && !lines[i].StartsWith('|')) i++;
        Assert.True(i < lines.Length, $"{where} has no table under '## {section}'");
        return (Cells(lines[i]), FirstTableRows(lines, i, stripBackticks: true));
    }

    /// <summary>"| a | b |" -> ["a", "b"]: one row's cells, trimmed; with <paramref name="stripBackticks"/>, <c>`id`</c> reads as <c>id</c>.</summary>
    public static string[] Cells(string markdownRow, bool stripBackticks = false)
    {
        string[] cells = markdownRow.Trim().Trim('|').Split('|');
        for (int c = 0; c < cells.Length; c++)
            cells[c] = stripBackticks ? cells[c].Trim().Trim('`') : cells[c].Trim();
        return cells;
    }
}
