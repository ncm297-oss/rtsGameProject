using System.Text.RegularExpressions;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA, D22 (2026-11-05): the starting-resources presets' text and the faction pages' "High starting resources" line,
/// for what <see cref="StartingResourcesContentTests"/> doesn't check: a bare faction name (its proper-name list holds
/// whole display names, so "Malazan" or "Whirlwind" alone pass it, BUG-1081), plain one-sentence-style punctuation, and
/// that each page's High line agrees with the shipped presets (Standard has no threshold, High has one).
/// </summary>
public class D22PresetTextQaTests
{
    private static GameData Data => TestSim.Data;

    /// <summary>Each faction's id and the capitalised words of its display names ("Malazan", "Empire", "Whirlwind", ...).</summary>
    private static List<string> FactionWords()
    {
        var words = new List<string>();
        foreach (FactionDef f in Data.Factions)
        {
            words.Add(f.Key);
            foreach (string name in new[] { f.DisplayName, f.BonusDisplayName })
                foreach (string w in name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    if (w.Length >= 4 && char.IsUpper(w[0]) && w != "Army") words.Add(w);
        }
        return words;
    }

    [Fact]
    public void ShippedDescriptions_NameNoFactionWord()
    {
        var problems = new List<string>();
        foreach (StartingResourcesDef p in Data.Rules.StartingResources)
            foreach (string w in FactionWords())
                if (Regex.IsMatch(p.Description, $@"\b{Regex.Escape(w)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    problems.Add($"{p.Key}: names '{w}'");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void ShippedText_IsPlainAscii_SentenceCased_AndEndsWithAFullStop()
    {
        foreach (StartingResourcesDef p in Data.Rules.StartingResources)
        {
            Assert.All(p.DisplayName + p.Description, c => Assert.InRange((int)c, 0x20, 0x7E));
            Assert.True(char.IsUpper(p.DisplayName[0]), p.Key);
            Assert.True(char.IsUpper(p.Description[0]), p.Key);
            Assert.EndsWith(".", p.Description);
            Assert.DoesNotContain("  ", p.Description);
            Assert.Equal(p.Description.Trim(), p.Description);
        }
    }

    [Fact]
    public void ThePresetsDisplayNames_AreDistinct()
    {
        var names = Data.Rules.StartingResources.Select(p => p.DisplayName).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>
    /// Each page's High line (AI build order (Normal)) says Standard has no spend-down threshold and High's is
    /// <c>aiSpendAbove</c>: the shipped presets must agree, and the line must stay marked as unpinned measurement.
    /// </summary>
    [Theory]
    [InlineData("malazan", "cadre_rush")]
    [InlineData("whirlwind", "holy_war")]
    public void PageHighLine_MatchesTheShippedPresets(string faction, string opening)
    {
        string line = FactionPage.Bullets(faction, AiContentTests.Section, 2)
            .Single(b => b.StartsWith("High starting resources ", StringComparison.Ordinal));
        HighLineWording(line, opening);

        StartingResourcesDef standard = Data.Rules.StartingResources[Data.Rules.FindStartingResources("standard")];
        StartingResourcesDef high = Data.Rules.StartingResources[Data.Rules.FindStartingResources("high")];
        Assert.Equal(0, standard.AiSpendAbove);
        Assert.True(high.AiSpendAbove > 0, "high has a spend-down threshold");
        Assert.True(high.AiSpendAbove < high.Gold + high.Wood, "high's start is above its own threshold");

        // The opening the line names exists in this faction's ai.json.
        Assert.Contains(Data.Ai[Data.FindFaction(faction)].Openings, o => o.Id == opening);
    }

    /// <summary>The High line's wording (shared with <see cref="TeblorPageParseQaTests"/>, which reads a page ahead of its data).</summary>
    internal static void HighLineWording(string line, string opening)
    {
        Assert.Contains("`aiSpendAbove`", line);
        Assert.Contains("Standard has no threshold", line);
        Assert.Contains("not pinned", line);
        Assert.Contains($"`{opening}`", line);
    }
}
