using Xunit.Abstractions;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// BUG-1331 item 3, in memory (the default loop): the landing's text edits keep each file's bytes outside the landing's
/// lines and its line endings, give the files <see cref="TeblorLanding.EditRoot"/> gives, and change nothing applied twice.
/// </summary>
public class TeblorLandingTextEditTests(ITestOutputHelper output)
{
    private static bool Landed => TestSim.Data.FindStatus(TeblorLanding.Status) >= 0;

    private static string Shipped(string rel) => File.ReadAllText(Path.Combine(TestDataDir.Shipped, rel));

    /// <summary>Lines only in <paramref name="a"/> and only in <paramref name="b"/> (a longest-common-subsequence line diff, as git's).</summary>
    internal static (List<string> Removed, List<string> Added) LineDiff(string a, string b)
    {
        string[] x = a.Split('\n'), y = b.Split('\n');
        var l = new int[x.Length + 1, y.Length + 1];
        for (int i = x.Length - 1; i >= 0; i--)
            for (int j = y.Length - 1; j >= 0; j--)
                l[i, j] = x[i] == y[j] ? l[i + 1, j + 1] + 1 : Math.Max(l[i + 1, j], l[i, j + 1]);
        var removed = new List<string>();
        var added = new List<string>();
        int p = 0, q = 0;
        while (p < x.Length && q < y.Length)
        {
            if (x[p] == y[q]) { p++; q++; }
            else if (l[p + 1, q] >= l[p, q + 1]) removed.Add(x[p++]);
            else added.Add(y[q++]);
        }
        removed.AddRange(x[p..]);
        added.AddRange(y[q..]);
        return (removed, added);
    }

    /// <summary>
    /// The lines each data file's edit removes and adds on the shipped files before the landing: ten one-field edits in
    /// buildings.json are ten added lines; the Frenzy's effect and description two replaced lines; the Anointing's
    /// description and a comma replaced plus its new effect; the status row appended (one comma, the row's eight lines).
    /// </summary>
    [Theory]
    [InlineData("factions/teblor/buildings.json", 0, 10)]
    [InlineData("factions/teblor/abilities.json", 2, 2)]
    [InlineData("factions/teblor/techs.json", 2, 3)]
    [InlineData("common/statuses.json", 0, 8)]
    public void TheTextEdit_ChangesOnlyTheLandingsLines(string rel, int removed, int added)
    {
        string before = Shipped(rel);
        string after = TeblorLanding.EditText(rel, before);
        if (Landed)
        {
            Assert.Equal(before, after); // D31 shipped the helper's own text
            return;
        }
        (List<string> r, List<string> a) = LineDiff(before, after);
        foreach (string line in r) output.WriteLine("- " + line);
        foreach (string line in a) output.WriteLine("+ " + line);
        Assert.Equal((removed, added), (r.Count, a.Count));
        if (rel.EndsWith("buildings.json", StringComparison.Ordinal)) Assert.All(a, line => Assert.Equal("      \"scale\": 1.25,", line));
        Assert.True(JsonTextEdit.Same(System.Text.Json.Nodes.JsonNode.Parse(TeblorLanding.FileTexts()[rel]), System.Text.Json.Nodes.JsonNode.Parse(after)),
            $"{rel}: the text edit and the copy's edit differ");
    }

    [Theory]
    [MemberData(nameof(DataFiles))]
    public void TheTextEdit_AppliedTwice_ChangesNothingMore(string rel)
    {
        string once = TeblorLanding.EditText(rel, Shipped(rel));
        Assert.Equal(once, TeblorLanding.EditText(rel, once));
    }

    public static TheoryData<string> DataFiles()
    {
        var d = new TheoryData<string>();
        foreach (string rel in TeblorLanding.Files) d.Add(rel);
        return d;
    }

    /// <summary>The file's line endings are kept: an LF file gets no CR, the same file in CRLF gets CRLF on every line, new ones included.</summary>
    [Theory]
    [MemberData(nameof(DataFiles))]
    public void TheTextEdit_KeepsTheLineEndings(string rel)
    {
        string lf = Shipped(rel).Replace("\r\n", "\n", StringComparison.Ordinal);
        string fromLf = TeblorLanding.EditText(rel, lf);
        Assert.DoesNotContain('\r', fromLf);
        string crlf = lf.Replace("\n", "\r\n", StringComparison.Ordinal);
        string fromCrlf = TeblorLanding.EditText(rel, crlf);
        Assert.Equal(fromLf.Replace("\n", "\r\n", StringComparison.Ordinal), fromCrlf);
    }

    /// <summary>The docs: the file's own line ending and final newline; the lines are the landing's; written twice the same.</summary>
    [Fact]
    public void TheDocTexts_KeepTheirLineEndings_AndAreTheLandingsLines()
    {
        foreach (string rel in TeblorLanding.Docs)
        {
            string file = File.ReadAllText(Path.Combine(TestDataDir.RepoRoot(), rel));
            string text = TeblorLanding.DocText(rel);
            Assert.Equal(file.Contains('\r'), text.Contains('\r'));
            Assert.Equal(file.EndsWith('\n'), text.EndsWith('\n'));
            string[] lines = rel.EndsWith("teblor.md", StringComparison.Ordinal) ? TeblorLanding.PageLines() : TeblorLanding.Doc02Lines();
            Assert.Equal(string.Join("\n", lines), text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n'));
            (List<string> r, List<string> a) = LineDiff(file.Replace("\r\n", "\n", StringComparison.Ordinal), text.Replace("\r\n", "\n", StringComparison.Ordinal));
            output.WriteLine($"{rel}: {r.Count} lines removed, {a.Count} added");
            if (!Landed) Assert.True(r.Count > 0 && r.Count < 40 && a.Count < 40, $"{rel}: {r.Count} removed / {a.Count} added: the landing's few lines, not the whole file");
        }
    }
}
