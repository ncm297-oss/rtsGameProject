using System.Text.RegularExpressions;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// Content (D23): the hand-made maps' player-facing text (the setup's Map list, docs/02 "Map and terrain") is pinned here,
/// so an edit of the sim-owned map file can't change it unnoticed. A description stays short, names no faction (a map is
/// for any pairing, even the one docs/02 says it favours), holds no digit (a layout number goes stale on a retune) and no
/// ratio word ("twice the forests": it goes stale when either map changes, BUG-1102), and never ships the "placeholder"
/// text of the first draft. The pin is by id (D24): a shipped map the pin doesn't name yet still runs the checker, so the
/// sim can add a map with provisional text and the data task pins it later.
/// </summary>
public class MapTextContentTests
{
    /// <summary>
    /// The setup list's text by map id: id, display name, description (D23, 2026-11-05; Raraku's "center" D24). Raraku stays
    /// first (the pin's own rule test reads it as <c>Pinned[0]</c>). Pale Hills and Vathar Crossing joined in D-H7
    /// (2026-11-06) with the text M6-S8 shipped: a new sentence there also needs the sim track's map source
    /// (tools/Rts.Cli/maps/&lt;id&gt;.quad.json, which <c>mirror-map</c> rebuilds the file from byte for byte) and
    /// <c>Map/MapFileTests.TheNewMaps_Load_WithTheirFields</c>, so it lands with them.
    /// </summary>
    public static readonly (string Id, string DisplayName, string Description)[] Pinned =
    {
        ("raraku", "Raraku",
            "Rival camps face each other across open desert of sand, scrub and sparse palms. Forests are few, twin mesas flank a wide open center, and long sightlines leave little room to hide."),
        ("pale_hills", "Pale Hills",
            "Four camps in dense hill woods, joined by narrow paths between the trees. Timber is everywhere, gold is scarce beyond the camps, and every path is a place for an ambush."),
        ("vathar_crossing", "Vathar Crossing",
            "A sunken river of slopes cuts the steppe in two, crossed only at its fords. Hold a ford and the far bank is yours; lose it and the enemy pours through."),
    };

    /// <summary>The longest description the setup shows (the presets' limit, <see cref="StartingResourcesContentTests.MaxDescriptionLength"/>).</summary>
    public const int MaxDescriptionLength = StartingResourcesContentTests.MaxDescriptionLength;

    private static readonly Regex Digit = new(@"\d", RegexOptions.CultureInvariant);

    private static GameData Data => TestSim.Data;

    [Fact]
    public void TheMapsText_IsThePinnedText_ById()
    {
        List<string> problems = PinProblems(Shipped(Data), Pinned);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>The pin's rules themselves: a shipped map the pin doesn't name passes; a pinned id the data lacks, or a changed text, fails.</summary>
    [Fact]
    public void ThePin_AllowsAnUnpinnedShippedMap_ButNotAMissingOrChangedPinnedOne()
    {
        var rarakuOnly = new[] { Pinned[0] };
        var shipped = new List<(string, string, string)>
        {
            ("frost_pass", "Frost Pass", "Provisional text."), // sorts first, not pinned
            ("raraku", "Raraku", Pinned[0].Description),
        };
        Assert.Empty(PinProblems(shipped, rarakuOnly));

        var missing = new[] { Pinned[0], ("vathar_crossing", "Vathar Crossing", "A river ford.") };
        Assert.Contains(PinProblems(shipped, missing), p => p.Contains("vathar_crossing: pinned but not shipped", StringComparison.Ordinal));

        var renamed = new List<(string, string, string)> { ("raraku", "Raraku Waste", Pinned[0].Description) };
        Assert.Contains(PinProblems(renamed, rarakuOnly), p => p.Contains("raraku: display name", StringComparison.Ordinal));
        var reworded = new List<(string, string, string)> { ("raraku", "Raraku", Pinned[0].Description + " More.") };
        Assert.Contains(PinProblems(reworded, rarakuOnly), p => p.Contains("raraku: description", StringComparison.Ordinal));
    }

    private readonly Xunit.Abstractions.ITestOutputHelper _out;

    public MapTextContentTests(Xunit.Abstractions.ITestOutputHelper output) => _out = output;

    /// <summary>
    /// Raraku is pinned first (the rule test above reads it there). The pin does not have to name every shipped map (D24:
    /// the sim may ship a map with provisional text and the data task pins it later, BUG-1104), so an unpinned shipped map
    /// is printed, not failed; <see cref="EveryShippedMapText_HasNoProblem"/> still checks its text.
    /// </summary>
    [Fact]
    public void ThePin_NamesEveryShippedMap_RarakuFirst()
    {
        Assert.Equal("raraku", Pinned[0].Id);
        foreach (MapDef m in Data.Maps)
            if (Array.FindIndex(Pinned, p => p.Id == m.Key) < 0)
                _out.WriteLine($"not pinned yet: {m.Key} ('{m.DisplayName}'), the data track pins its text");
    }

    [Fact]
    public void EveryShippedMapText_HasNoProblem()
    {
        var problems = new List<string>();
        foreach (MapDef m in Data.Maps)
            problems.AddRange(Problems(Data, m.Key, m.DisplayName, m.Description));
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>The checker catches what it is for: each mutant of the shipped text fails, naming why.</summary>
    [Theory]
    [InlineData("Rival camps face each other across open desert with 2 mesas and few forests.", "a digit")]
    [InlineData("Open desert, few forests and long sightlines: Whirlwind country.", "names 'Whirlwind'")]
    [InlineData("Placeholder: open desert with long sightlines, two mesas and little timber.", "'placeholder'")]
    [InlineData(null, "201 characters")]
    [InlineData("Twice the forests of the other desert, and long sightlines.", "ratio word 'twice'")]
    [InlineData("Open desert with double the usual mesas.", "ratio word 'double'")]
    [InlineData("Sand, scrub and triple the sightlines.", "ratio word 'triple'")]
    [InlineData("Open desert with HALF the timber of a forest map.", "ratio word 'half'")]
    public void AMutantDescription_IsCaught(string? text, string says)
    {
        MapDef raraku = Raraku(Data);
        text ??= (raraku.Description + new string('.', 201)).Substring(0, 201); // just over the limit
        List<string> problems = Problems(Data, raraku.Key, raraku.DisplayName, text);
        Assert.True(problems.Any(x => x.Contains(says, StringComparison.Ordinal)), $"'{text}': " + string.Join("; ", problems));
    }

    /// <summary>Counting in words is allowed (the brief: "two mesas"); only comparing is not, and only as a whole word.</summary>
    [Theory]
    [InlineData("Open desert where two mesas flank a wide center.")]
    [InlineData("Rival camps halfway across the sand, doubled back by the mesas.")] // "halfway" / "doubled" are not the whole words
    public void ACountOrAPartWord_Passes(string text)
    {
        MapDef raraku = Raraku(Data);
        List<string> problems = Problems(Data, raraku.Key, raraku.DisplayName, text);
        Assert.True(problems.Count == 0, string.Join("; ", problems));
    }

    /// <summary>The shipped map with id <c>raraku</c> (the data sorts maps by id, so its index moves as maps are added).</summary>
    public static MapDef Raraku(GameData d)
    {
        foreach (MapDef m in d.Maps)
            if (m.Key == "raraku") return m;
        throw new InvalidOperationException("no shipped map with id 'raraku'");
    }

    /// <summary>Every shipped map as (id, display name, description), in data order.</summary>
    public static List<(string Id, string DisplayName, string Description)> Shipped(GameData d)
    {
        var list = new List<(string, string, string)>();
        foreach (MapDef m in d.Maps) list.Add((m.Key, m.DisplayName, m.Description));
        return list;
    }

    /// <summary>
    /// Every way <paramref name="shipped"/> disagrees with <paramref name="pinned"/>: each pinned id must be shipped once
    /// with that display name and description. A shipped id the pin doesn't name is not a problem (its checker still runs).
    /// </summary>
    public static List<string> PinProblems(
        IReadOnlyList<(string Id, string DisplayName, string Description)> shipped,
        IReadOnlyList<(string Id, string DisplayName, string Description)> pinned)
    {
        var problems = new List<string>();
        foreach (var p in pinned)
        {
            int found = 0;
            foreach (var s in shipped)
            {
                if (s.Id != p.Id) continue;
                found++;
                if (s.DisplayName != p.DisplayName) problems.Add($"{p.Id}: display name '{s.DisplayName}', pinned '{p.DisplayName}'");
                if (s.Description != p.Description) problems.Add($"{p.Id}: description '{s.Description}', pinned '{p.Description}'");
            }
            if (found == 0) problems.Add($"{p.Id}: pinned but not shipped");
            else if (found > 1) problems.Add($"{p.Id}: shipped {found} times");
        }
        return problems;
    }

    /// <summary>Every way a map's <paramref name="displayName"/> and <paramref name="description"/> break the rules; empty when fine.</summary>
    public static List<string> Problems(GameData d, string id, string displayName, string description)
    {
        var problems = new List<string>();
        if (displayName.Length == 0) problems.Add($"{id}: no display name");
        if (description.Length == 0) problems.Add($"{id}: no description");
        if (description.Length > MaxDescriptionLength)
            problems.Add($"{id}: description is {description.Length} characters, over {MaxDescriptionLength}");
        foreach (string field in new[] { displayName, description })
        {
            if (Digit.IsMatch(field)) problems.Add($"{id}: '{field}' has a digit (a layout number goes stale; describe, don't count)");
            if (field.Contains("placeholder", StringComparison.OrdinalIgnoreCase)) problems.Add($"{id}: '{field}' still says 'placeholder'");
            foreach (string w in StartingResourcesContentTests.RatioWords)
                if (Regex.IsMatch(field, $@"\b{w}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    problems.Add($"{id}: '{field}' has the ratio word '{w}' (a comparison goes stale when either side changes)");
            foreach (string w in FactionWords.In(d, field))
                problems.Add($"{id}: '{field}' names '{w}' (a map is for any faction)");
        }
        return problems;
    }
}
