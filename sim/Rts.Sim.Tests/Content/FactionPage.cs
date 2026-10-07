using System.Globalization;

namespace Rts.Sim.Tests.Content;

/// <summary>Reads the markdown tables of <c>docs/factions/&lt;id&gt;.md</c> so content tests can pin a page to the data.</summary>
internal static class FactionPage
{
    /// <summary>
    /// The first table after the <c>## <paramref name="section"/></c> heading, as trimmed cells per row (header and
    /// separator dropped). Backticks around ids are stripped.
    /// </summary>
    public static string[][] Table(string faction, string section) => DocTable(Path.Combine("docs", "factions", faction + ".md"), section);

    /// <summary>As <see cref="Table"/>, for any markdown file under the repo root (e.g. <c>docs/02-game-design.md</c>).</summary>
    public static string[][] DocTable(string relativePath, string section)
    {
        string[] lines = File.ReadAllLines(Path.Combine(TestDataDir.RepoRoot(), relativePath));
        int i = Array.IndexOf(lines, "## " + section);
        Assert.True(i >= 0, $"{relativePath} has no '## {section}' heading");
        while (i < lines.Length && !lines[i].StartsWith('|')) i++;
        var rows = new List<string[]>();
        for (i += 2; i < lines.Length && lines[i].StartsWith('|'); i++)
            rows.Add(lines[i].Trim().Trim('|').Split('|').Select(c => c.Trim().Trim('`')).ToArray());
        Assert.NotEmpty(rows);
        return rows.ToArray();
    }

    /// <summary>
    /// The paragraph under the heading <c>## <paramref name="heading"/></c>, its lines joined with spaces and the
    /// <c>**</c> bold markers removed.
    /// </summary>
    public static string Paragraph(string faction, string heading)
    {
        string[] lines = File.ReadAllLines(Path.Combine(TestDataDir.RepoRoot(), "docs", "factions", faction + ".md"));
        int i = Array.IndexOf(lines, "## " + heading);
        Assert.True(i >= 0, $"{faction}.md has no '## {heading}' heading");
        for (i++; i < lines.Length && lines[i].Trim().Length == 0; i++) { }
        var text = new List<string>();
        for (; i < lines.Length && lines[i].Trim().Length > 0; i++) text.Add(lines[i].Trim());
        Assert.NotEmpty(text);
        return string.Join(" ", text).Replace("**", "");
    }

    /// <summary>"Town Hall" -> <c>TownHall</c>: page slot names are the enum names with spaces.</summary>
    public static T Slot<T>(string cell) where T : struct, Enum => Enum.Parse<T>(cell.Replace(" ", ""));

    /// <summary>"50 / 0" -> (50, 0).</summary>
    public static (int Gold, int Wood) Cost(string cell)
    {
        string[] p = cell.Split('/');
        Assert.Equal(2, p.Length);
        return (int.Parse(p[0].Trim(), CultureInfo.InvariantCulture), int.Parse(p[1].Trim(), CultureInfo.InvariantCulture));
    }

    /// <summary>Invariant-culture number.</summary>
    public static double Num(string cell) => double.Parse(cell, CultureInfo.InvariantCulture);

    /// <summary>Invariant-culture text for a number, as the pages write it ("7.5", never "7,5").</summary>
    public static string Text(double value) => value.ToString(CultureInfo.InvariantCulture);
}
