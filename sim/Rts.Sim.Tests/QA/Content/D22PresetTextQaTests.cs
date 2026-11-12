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
    /// The opening the High line names: the first <c>`id`</c> after "Standard has no threshold" (the measured opening,
    /// malazan.md's <c>cadre_rush</c>; teblor.md's <c>standard</c>), or null when the line names none there.
    /// </summary>
    internal static string? HighLineOpening(string line)
    {
        int at = line.IndexOf("Standard has no threshold", StringComparison.Ordinal);
        if (at < 0) return null;
        Match m = Regex.Match(line[at..], @"`([a-z][a-z0-9_]*)`", RegexOptions.CultureInvariant);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>
    /// Each page's High line (AI build order (Normal)) says Standard has no spend-down threshold and High's is
    /// <c>aiSpendAbove</c>: the shipped presets must agree, and the line must stay marked as unpinned measurement.
    /// D28 (BUG-1210): one row per data faction (<see cref="ContentFactions.Keys"/>), the opening id read from the line itself
    /// and required to be one of the faction's shipped openings, so a third faction's line is checked against its own file.
    /// </summary>
    [Theory]
    [MemberData(nameof(ContentFactions.Theory), MemberType = typeof(ContentFactions))]
    public void PageHighLine_MatchesTheShippedPresets(string faction)
    {
        string line = FactionPage.Bullets(faction, AiContentTests.Section, 2)
            .Single(b => b.StartsWith("High starting resources ", StringComparison.Ordinal));
        string? problem = HighLineOpeningProblem(Data, faction, line);
        Assert.True(problem == null, problem);
        HighLineWording(line, HighLineOpening(line)!);

        StartingResourcesDef standard = Data.Rules.StartingResources[Data.Rules.FindStartingResources("standard")];
        StartingResourcesDef high = Data.Rules.StartingResources[Data.Rules.FindStartingResources("high")];
        Assert.Equal(0, standard.AiSpendAbove);
        Assert.True(high.AiSpendAbove > 0, "high has a spend-down threshold");
        Assert.True(high.AiSpendAbove < high.Gold + high.Wood, "high's start is above its own threshold");
    }

    /// <summary>Why the High line's opening is wrong for <paramref name="faction"/>'s shipped <c>ai.json</c>, or null when it names one of its openings.</summary>
    internal static string? HighLineOpeningProblem(GameData d, string faction, string line)
    {
        string? opening = HighLineOpening(line);
        if (opening == null) return $"{faction}.md High starting resources: names no opening by `id` after 'Standard has no threshold'";
        return d.Ai[d.FindFaction(faction)].Openings.Any(o => o.Id == opening) ? null
            : $"{faction}.md High starting resources names `{opening}`, not an opening of factions/{faction}/ai.json";
    }

    /// <summary>D28 (BUG-1210): the opening is read from the line, so a page naming an opening its file lacks reads red.</summary>
    [Theory]
    [InlineData("malazan", "`cadre_rush` held", "`cadre_push` held", "malazan.md High starting resources names `cadre_push`")]
    [InlineData("teblor", "`standard` at High", "`war_band` at High", "teblor.md High starting resources names `war_band`")]
    public void PageHighLine_AnOpeningTheFileLacks_IsCaught(string faction, string from, string to, string want)
    {
        string line = FactionPage.Bullets(faction, AiContentTests.Section, 2)
            .Single(b => b.StartsWith("High starting resources ", StringComparison.Ordinal));
        Assert.Contains(from, line);
        Assert.Null(HighLineOpeningProblem(Data, faction, line));
        Assert.StartsWith(want, HighLineOpeningProblem(Data, faction, line.Replace(from, to, StringComparison.Ordinal)) ?? "no failure");
    }

    /// <summary>The High line's wording.</summary>
    internal static void HighLineWording(string line, string opening)
    {
        Assert.Contains("`aiSpendAbove`", line);
        Assert.Contains("Standard has no threshold", line);
        Assert.Contains("not pinned", line);
        Assert.Contains($"`{opening}`", line);
    }
}
