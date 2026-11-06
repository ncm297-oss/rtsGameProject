using System.Text.RegularExpressions;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// Content (D23): the hand-made maps' player-facing text (the setup's Map list, docs/02 "Map and terrain") is pinned here,
/// so an edit of the sim-owned map file can't change it unnoticed. A description stays short, names no faction (a map is
/// for any pairing, even the one docs/02 says it favours), holds no digit (a layout number goes stale on a retune), and
/// never ships the "placeholder" text of the first draft.
/// </summary>
public class MapTextContentTests
{
    /// <summary>The setup list's text, in <see cref="GameData.Maps"/> order: id, display name, description (D23, 2026-11-05).</summary>
    public static readonly (string Id, string DisplayName, string Description)[] Pinned =
    {
        ("raraku", "Raraku",
            "Rival camps face each other across open desert of sand, scrub and sparse palms. Forests are few, twin mesas flank a wide open centre, and long sightlines leave little room to hide."),
    };

    /// <summary>The longest description the setup shows (the presets' limit, <see cref="StartingResourcesContentTests.MaxDescriptionLength"/>).</summary>
    public const int MaxDescriptionLength = StartingResourcesContentTests.MaxDescriptionLength;

    private static readonly Regex Digit = new(@"\d", RegexOptions.CultureInvariant);

    private static GameData Data => TestSim.Data;

    [Fact]
    public void TheMapsText_IsThePinnedText_InOrder()
    {
        Assert.Equal(Pinned.Length, Data.Maps.Length);
        for (int i = 0; i < Pinned.Length; i++)
        {
            Assert.Equal(Pinned[i].Id, Data.Maps[i].Key);
            Assert.Equal(Pinned[i].DisplayName, Data.Maps[i].DisplayName);
            Assert.Equal(Pinned[i].Description, Data.Maps[i].Description);
        }
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
    public void AMutantDescription_IsCaught(string? text, string says)
    {
        MapDef raraku = Data.Maps[0];
        text ??= (raraku.Description + new string('.', 201)).Substring(0, 201); // just over the limit
        Assert.Equal("raraku", raraku.Key);
        List<string> problems = Problems(Data, raraku.Key, raraku.DisplayName, text);
        Assert.True(problems.Any(x => x.Contains(says, StringComparison.Ordinal)), $"'{text}': " + string.Join("; ", problems));
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
            foreach (string w in FactionWords.In(d, field))
                problems.Add($"{id}: '{field}' names '{w}' (a map is for any faction)");
        }
        return problems;
    }
}
