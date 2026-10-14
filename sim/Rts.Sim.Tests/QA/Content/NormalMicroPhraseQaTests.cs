using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA (D17 inspection): the K pin on docs/02's Normal bullet after the rewording. The pre-D17 bullet (only "fair play at a
/// relaxed pace") fails naming both Normal fields; a rewrap that splits the phrase across two lines still passes (the bullet
/// reader joins continuation lines); cutting only "pull back wounded" fails both rows (the phrase is one pin, not two);
/// Easy's "doesn't micro" and Hard's phrases are untouched by the Normal edit.
/// </summary>
public class NormalMicroPhraseQaTests
{
    private const string Phrase = "doesn't focus fire or pull back wounded";

    private static string[] Docs02() => PageTables.Lines(Path.Combine("docs", "02-game-design.md"));

    private static (string[] Lines, int Normal) WithNormal()
    {
        string[] lines = Docs02();
        int at = Array.FindIndex(lines, l => l.TrimStart().StartsWith("- **Normal:**", StringComparison.Ordinal));
        Assert.True(at >= 0, "docs/02 has no '- **Normal:**' bullet line");
        return (lines, at);
    }

    [Fact]
    public void ThePreD17NormalBullet_FailsNamingBothFields()
    {
        (string[] lines, int at) = WithNormal();
        var edited = new List<string>(lines);
        // Drop the bullet's continuation lines, then put back the exact pre-D17 text.
        while (at + 1 < edited.Count && edited[at + 1].StartsWith("    ", StringComparison.Ordinal)
               && !edited[at + 1].TrimStart().StartsWith("- ", StringComparison.Ordinal))
            edited.RemoveAt(at + 1);
        edited[at] = "  - **Normal:** fair play at a relaxed pace, follows the build order on time, uses abilities.";
        List<string> problems = AiContentTests.Docs02Problems(edited.ToArray());
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.StartsWith("docs/02 'Normal'", StringComparison.Ordinal) && p.Contains("(focusFire false)"));
        Assert.Contains(problems, p => p.StartsWith("docs/02 'Normal'", StringComparison.Ordinal) && p.Contains("(pullBackWounded false)"));
    }

    [Fact]
    public void ThePhraseRewrappedAcrossTwoLines_StillPasses()
    {
        string[] lines = Docs02();
        int at = Array.FindIndex(lines, l => l.Contains(Phrase, StringComparison.Ordinal));
        Assert.True(at >= 0);
        var edited = new List<string>(lines);
        string indent = new string(' ', lines[at].Length - lines[at].TrimStart().Length);
        int cut = lines[at].IndexOf("pull back", StringComparison.Ordinal);
        edited[at] = lines[at][..cut].TrimEnd();
        edited.Insert(at + 1, indent + lines[at][cut..]);
        Assert.Empty(AiContentTests.Docs02Problems(edited.ToArray()));
    }

    [Fact]
    public void CuttingOnlyPullBackWounded_FailsBothNormalRows()
    {
        string[] lines = Docs02();
        int at = Array.FindIndex(lines, l => l.Contains(Phrase, StringComparison.Ordinal));
        string[] edited = (string[])lines.Clone();
        edited[at] = edited[at].Replace(Phrase, "doesn't focus fire", StringComparison.Ordinal);
        List<string> problems = AiContentTests.Docs02Problems(edited);
        Assert.Equal(2, problems.Count);
        Assert.All(problems, p => Assert.StartsWith("docs/02 'Normal'", p));
    }

    [Fact]
    public void TheShippedDoc_KeepsEasysDoesntMicro_AndNoRowLeansOnRelaxedPace()
    {
        (string[] lines, int at) = WithNormal();
        // Easy's pin row is still there and still holds on the shipped doc (its bullet wraps "doesn't / micro").
        Assert.Contains(AiContentTests.DesignStates, s => s.Phrase == "doesn't micro" && s.Difficulty == Rts.Sim.Data.AiDifficulty.Easy);
        Assert.DoesNotContain(AiContentTests.DesignStates, s => s.Phrase.Contains("relaxed pace", StringComparison.Ordinal));
        Assert.Contains("fair play at a relaxed pace", lines[at]);
        Assert.Empty(AiContentTests.Docs02Problems(lines));
    }
}
