using System.Globalization;
using System.Text.RegularExpressions;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// Content (D22): the starting-resources presets' player-facing text in <c>rules.json</c> (the skirmish setup's list,
/// docs/02 "Game flow" 2) is pinned here, so an edit of the sim-owned file can't change it unnoticed. Each description
/// stays short, names no faction, unit, building, tech or ability (a preset is generic), and names no number but its own
/// preset's gold or wood, so a retune of the amounts can't leave the text saying the old ones.
/// </summary>
public class StartingResourcesContentTests
{
    /// <summary>The setup list's text, in file order: id, display name, description (D22, 2026-11-05).</summary>
    public static readonly (string Id, string DisplayName, string Description)[] Pinned =
    {
        ("standard", "Standard",
            "Every player starts with a modest stock of gold and wood: enough for the first workers and buildings, then the economy pays for the rest. The usual start."),
        ("high", "High",
            "Every player starts with a large stockpile of gold and wood, enough to build and train at once from the first minute. AI players spend theirs rather than sit on it."),
    };

    /// <summary>The longest description the setup shows (the brief's limit).</summary>
    public const int MaxDescriptionLength = 200;

    /// <summary>Words that state an amount without digits ("five times the usual").</summary>
    private static readonly string[] NumberWords =
    {
        "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "twenty",
        "fifty", "hundred", "hundreds", "thousand", "thousands", "twice", "double", "triple", "half",
    };

    private static readonly Regex Digits = new(@"\d[\d,]*(?:\.\d+)?", RegexOptions.CultureInvariant);

    private static GameData Data => TestSim.Data;

    [Fact]
    public void ThePresetsText_IsThePinnedText_InFileOrder()
    {
        var presets = Data.Rules.StartingResources;
        Assert.Equal(Pinned.Length, presets.Length);
        for (int i = 0; i < Pinned.Length; i++)
        {
            Assert.Equal(Pinned[i].Id, presets[i].Key);
            Assert.Equal(Pinned[i].DisplayName, presets[i].DisplayName);
            Assert.Equal(Pinned[i].Description, presets[i].Description);
        }
    }

    [Fact]
    public void EveryShippedDescription_HasNoProblem()
    {
        var problems = new List<string>();
        foreach (StartingResourcesDef p in Data.Rules.StartingResources)
            problems.AddRange(Problems(Data, p.Key, p.Description, p.Gold, p.Wood));
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>The checker catches what it is for: each mutant of a shipped text fails, naming why.</summary>
    [Theory]
    [InlineData("standard", "Every player starts with five times the usual gold and wood.", "names the number 'five'")]
    [InlineData("high", "Every player starts with twice the usual gold and wood.", "names the number 'twice'")]
    [InlineData("standard", "Every player starts with 1,000 gold and wood.", "names the number '1,000'")]
    [InlineData("high", "Every player starts with 200 gold and wood.", "names the number '200'")]
    [InlineData("high", "Every player starts with a large stockpile, like a Malazan Empire treasury.", "names 'Malazan Empire'")]
    [InlineData("standard", "Every player starts with enough for a Garrison Keep.", "names 'Garrison Keep'")]
    [InlineData("high", "Every player starts like a Malazan quartermaster.", "names 'Malazan'")] // BUG-1081: a bare faction word
    [InlineData("standard", "Every player starts with a Whirlwind war chest.", "names 'Whirlwind'")]
    public void AMutantDescription_IsCaught(string id, string text, string says)
    {
        StartingResourcesDef p = Data.Rules.StartingResources[Data.Rules.FindStartingResources(id)];
        List<string> problems = Problems(Data, id, text, p.Gold, p.Wood);
        Assert.True(problems.Any(x => x.Contains(says, StringComparison.Ordinal)), $"'{text}': " + string.Join("; ", problems));
    }

    /// <summary>BUG-1081 item 1: the proper-name list holds each faction's id and its bare name words, not only whole names.</summary>
    [Fact]
    public void ProperNames_HoldTheBareFactionWords()
    {
        List<string> names = ProperNames(Data);
        foreach (string w in new[] { "Malazan", "Empire", "Imperial", "Whirlwind", "Apocalypse", "Malazan Empire", "The Whirlwind" })
            Assert.Contains(w, names);
        foreach (FactionDef f in Data.Factions)
            Assert.Contains(names, n => string.Equals(n, f.Key, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("Army", names); // a generic word ("Army of the Apocalypse" is still whole)
    }

    /// <summary>A preset's own amounts may be named ("1,000 gold" on High): the rule is "no number but its own".</summary>
    [Fact]
    public void ThePresetsOwnAmount_IsAllowed()
    {
        StartingResourcesDef high = Data.Rules.StartingResources[Data.Rules.FindStartingResources("high")];
        string text = $"Every player starts with {high.Gold.ToString("N0", CultureInfo.InvariantCulture)} gold and {high.Wood} wood.";
        Assert.Empty(Problems(Data, "high", text, high.Gold, high.Wood));
    }

    /// <summary>Every way <paramref name="text"/> breaks the preset text's rules; empty when it is fine.</summary>
    public static List<string> Problems(GameData d, string id, string text, int gold, int wood)
    {
        var problems = new List<string>();
        if (text.Length == 0) problems.Add($"{id}: no description");
        if (text.Length > MaxDescriptionLength) problems.Add($"{id}: description is {text.Length} characters, over {MaxDescriptionLength}");

        foreach (Match m in Digits.Matches(text))
        {
            string raw = m.Value.TrimEnd(',');
            bool own = int.TryParse(raw.Replace(",", ""), NumberStyles.None, CultureInfo.InvariantCulture, out int n) && (n == gold || n == wood);
            if (!own) problems.Add($"{id}: description names the number '{raw}', not its own gold ({gold}) or wood ({wood})");
        }
        foreach (string w in NumberWords)
            if (Regex.IsMatch(text, $@"\b{w}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                problems.Add($"{id}: description names the number '{w}' (a preset's amount may change; say 'a large stockpile', not a ratio)");

        foreach (string name in ProperNames(d))
            if (Regex.IsMatch(text, $@"\b{Regex.Escape(name)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                problems.Add($"{id}: description names '{name}' (a preset is generic: no faction, unit, building, tech or ability)");
        return problems;
    }

    /// <summary>
    /// The data's faction, unit, building, tech, ability and status display names, plus each faction's id and bare name
    /// words (<see cref="FactionWords"/>, BUG-1081), longest first, no duplicates.
    /// </summary>
    public static List<string> ProperNames(GameData d)
    {
        var names = new List<string>(FactionWords.All(d));
        foreach (FactionDef f in d.Factions) { names.Add(f.DisplayName); names.Add(f.BonusDisplayName); }
        foreach (UnitDef u in d.Units) names.Add(u.DisplayName);
        foreach (BuildingDef b in d.Buildings) names.Add(b.DisplayName);
        foreach (TechDef t in d.Techs) names.Add(t.DisplayName);
        foreach (AbilityDef a in d.Abilities) names.Add(a.DisplayName);
        foreach (StatusDef s in d.Statuses) names.Add(s.DisplayName);
        return names.Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(n => n.Length).ThenBy(n => n, StringComparer.Ordinal).ToList();
    }
}
