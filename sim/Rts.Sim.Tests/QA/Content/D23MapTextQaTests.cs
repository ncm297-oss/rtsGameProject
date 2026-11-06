using System.Text.RegularExpressions;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA (D23, 2026-11-05-2148): attacks on the map-text checker and Raraku's shipped text beyond the dev's pins. The rule
/// is "no digit", not "no number word" (the brief: describe, don't count; "two mesas" is fine in words), but a ratio word
/// ("twice", "half") is out (D24, BUG-1102: a comparison goes stale when either side changes), faction words
/// match whole words in any case, and the text reads as one tooltip (printable ASCII, sentences, no double space).
/// </summary>
public class D23MapTextQaTests
{
    private static GameData Data => TestSim.Data;

    private static List<string> Check(string text)
    {
        MapDef m = MapTextContentTests.Raraku(Data);
        return MapTextContentTests.Problems(Data, m.Key, m.DisplayName, text);
    }

    [Theory]
    [InlineData("Open desert where two mesas flank a wide center.")]
    [InlineData("A dozen dunes, a few forests and long sightlines.")]
    [InlineData("Malazans beware nothing here: open sand.")] // "Malazans" is not the whole word "Malazan"
    [InlineData("A drill-flat plain of sand and scrub.")] // hyphen splits words: "drill" IS caught, see below
    public void NumberWords_AreAllowed_ButFactionWholeWordsStillCount(string text)
    {
        List<string> problems = Check(text);
        bool hasDrill = Regex.IsMatch(text, @"\bdrill\b", RegexOptions.IgnoreCase);
        if (hasDrill) Assert.Contains(problems, p => p.Contains("names 'Drill'", StringComparison.Ordinal));
        else Assert.True(problems.Count == 0, string.Join("; ", problems));
    }

    /// <summary>Re-cut in D24 (BUG-1102): number words stay allowed, ratio words do not, in any case.</summary>
    [Theory]
    [InlineData("A dozen dunes, a few forests and twice the sightlines.", "twice")]
    [InlineData("Twice the forests of Pale Hills.", "twice")]
    [InlineData("Two mesas and DOUBLE the open sand.", "double")]
    [InlineData("Half the timber of a forest map.", "half")]
    public void RatioWords_AreRejected_EvenBesideANumberWord(string text, string word)
    {
        List<string> problems = Check(text);
        Assert.Contains(problems, p => p.Contains($"ratio word '{word}'", StringComparison.Ordinal));
        Assert.Single(problems);
    }

    [Theory]
    [InlineData("Open desert, home of the malazan legions.", "names 'Malazan'")] // lower case
    [InlineData("WHIRLWIND country: open desert.", "names 'Whirlwind'")] // upper case
    [InlineData("An old Empire road crosses the sand.", "names 'Empire'")]
    [InlineData("Open desert, the PLACEHOLDER layout.", "'placeholder'")]
    [InlineData("Open desert with ٢ mesas.", "a digit")] // a non-ASCII digit is still a digit to \d
    public void TheChecker_CatchesCaseAndScriptVariants(string text, string says)
    {
        List<string> problems = Check(text);
        Assert.True(problems.Any(p => p.Contains(says, StringComparison.Ordinal)), $"'{text}': " + string.Join("; ", problems));
    }

    [Fact]
    public void TheChecker_RejectsAnEmptyOrDigitDisplayName()
    {
        MapDef m = MapTextContentTests.Raraku(Data);
        Assert.Contains(MapTextContentTests.Problems(Data, m.Key, "", m.Description), p => p.Contains("no display name"));
        Assert.Contains(MapTextContentTests.Problems(Data, m.Key, "Raraku 2", m.Description), p => p.Contains("a digit"));
        Assert.Empty(MapTextContentTests.Problems(Data, m.Key, m.DisplayName, m.Description));
    }

    [Fact]
    public void ExactlyTwoHundredCharacters_Passes()
    {
        string text = ("Open desert of sand and scrub. " + new string('a', 300)).Substring(0, 200);
        Assert.Empty(Check(text));
    }

    /// <summary>Every shipped map description reads as a tooltip: printable ASCII, capitalised, ends on a full stop, no double space.</summary>
    [Fact]
    public void ShippedMapDescriptions_ReadAsATooltip()
    {
        foreach (MapDef m in Data.Maps)
        {
            string d = m.Description;
            Assert.True(d.All(c => c >= ' ' && c <= '~'), $"{m.Key}: non-printable or non-ASCII character");
            Assert.True(char.IsUpper(d[0]), $"{m.Key}: starts lower case");
            Assert.EndsWith(".", d);
            Assert.DoesNotContain("  ", d);
            Assert.Equal(d.Trim(), d);
        }
    }

    /// <summary>Raraku's description names what docs/02's Shipped maps row says: desert, open center, few forests, long sightlines.</summary>
    [Fact]
    public void Raraku_NamesTheDocsTraits()
    {
        MapDef r = Data.Maps.First(m => m.Key == "raraku");
        Assert.Equal("Raraku", r.DisplayName);
        foreach (string w in new[] { "desert", "open cent", "forests are few", "long sightlines" })
            Assert.Contains(w, r.Description, StringComparison.OrdinalIgnoreCase);
    }
}
