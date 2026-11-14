using System.Text.Json.Nodes;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA D-H8 (session 2026-11-13-2306): BUG-1331 item 3's text edits (<see cref="TeblorLanding.EditText"/>) on file shapes the
/// developer's tests don't cover: a data file with its final newline missing (LF and CRLF), a CRLF file checked line by
/// line (no bare LF introduced anywhere), and the edit applied twice on those shapes.
/// </summary>
public class DH8WriteHelperQaTests
{
    private static string Shipped(string rel) => File.ReadAllText(Path.Combine(TestDataDir.Shipped, rel)).Replace("\r\n", "\n", StringComparison.Ordinal);

    public static TheoryData<string, bool> Shapes()
    {
        var d = new TheoryData<string, bool>();
        foreach (string rel in TeblorLanding.Files)
        {
            d.Add(rel, false);
            d.Add(rel, true);
        }
        return d;
    }

    /// <summary>
    /// The final newline dropped: the edit keeps it dropped, changes the same lines as on the file with it, parses to the
    /// landing's edit, and applied twice changes nothing more.
    /// </summary>
    [Theory]
    [MemberData(nameof(Shapes))]
    public void NoFinalNewline_IsKept_AndTheEditIsTheSame(string rel, bool crlf)
    {
        string with = Shipped(rel);
        Assert.EndsWith("\n", with);
        string without = with.TrimEnd('\n');
        if (crlf)
        {
            with = with.Replace("\n", "\r\n", StringComparison.Ordinal);
            without = without.Replace("\n", "\r\n", StringComparison.Ordinal);
        }
        string a = TeblorLanding.EditText(rel, with), b = TeblorLanding.EditText(rel, without);
        Assert.False(b.EndsWith('\n'), $"{rel}: the edit added a final newline");
        Assert.Equal(a, b + (crlf ? "\r\n" : "\n"));
        Assert.Equal(b, TeblorLanding.EditText(rel, b));
        Assert.True(JsonTextEdit.Same(JsonNode.Parse(TeblorLanding.FileTexts()[rel]), JsonNode.Parse(b)), rel);
    }

    /// <summary>A CRLF file: every line of the result ends CRLF (no bare LF from a new line), and no stray CR either.</summary>
    [Theory]
    [MemberData(nameof(DataFiles))]
    public void CrlfFile_EveryLineStaysCrlf(string rel)
    {
        string crlf = Shipped(rel).Replace("\n", "\r\n", StringComparison.Ordinal);
        string after = TeblorLanding.EditText(rel, crlf);
        for (int i = 0; i < after.Length; i++)
        {
            if (after[i] == '\n') Assert.True(i > 0 && after[i - 1] == '\r', $"{rel}: bare LF at {i}");
            if (after[i] == '\r') Assert.True(i + 1 < after.Length && after[i + 1] == '\n', $"{rel}: stray CR at {i}");
        }
        if (TestSim.Data.FindStatus(TeblorLanding.Status) < 0) Assert.NotEqual(crlf, after); // until D31 the edit changes something
    }

    public static TheoryData<string> DataFiles()
    {
        var d = new TheoryData<string>();
        foreach (string rel in TeblorLanding.Files) d.Add(rel);
        return d;
    }
}
