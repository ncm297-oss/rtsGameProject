namespace Rts.Sim.Tests.Content;

/// <summary>Reads the markdown tables of <c>docs/factions/&lt;id&gt;.md</c> so content tests can pin a page to the data.</summary>
internal static class FactionPage
{
    /// <summary>
    /// The first table after the <c>## <paramref name="section"/></c> heading, as trimmed cells per row (header and
    /// separator dropped). Backticks around ids are stripped.
    /// </summary>
    public static string[][] Table(string faction, string section)
    {
        string path = Path.Combine(TestDataDir.RepoRoot(), "docs", "factions", faction + ".md");
        string[] lines = File.ReadAllLines(path);
        int i = Array.IndexOf(lines, "## " + section);
        Assert.True(i >= 0, $"{faction}.md has no '## {section}' heading");
        while (i < lines.Length && !lines[i].StartsWith('|')) i++;
        var rows = new List<string[]>();
        for (i += 2; i < lines.Length && lines[i].StartsWith('|'); i++)
            rows.Add(lines[i].Trim().Trim('|').Split('|').Select(c => c.Trim().Trim('`')).ToArray());
        Assert.NotEmpty(rows);
        return rows.ToArray();
    }

    /// <summary>"Town Hall" -> <c>TownHall</c>: page slot names are the enum names with spaces.</summary>
    public static T Slot<T>(string cell) where T : struct, Enum => Enum.Parse<T>(cell.Replace(" ", ""));

    /// <summary>"50 / 0" -> (50, 0).</summary>
    public static (int Gold, int Wood) Cost(string cell)
    {
        string[] p = cell.Split('/');
        Assert.Equal(2, p.Length);
        return (int.Parse(p[0].Trim()), int.Parse(p[1].Trim()));
    }

    /// <summary>Invariant-culture number.</summary>
    public static double Num(string cell) => double.Parse(cell, System.Globalization.CultureInfo.InvariantCulture);
}
