using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA (D24, 2026-11-06-0326): the id-based map-text pin against real loaded data, not hand-built tuples. A copied data dir
/// gains maps the pin doesn't name (one sorting before <c>raraku</c>, as the sim's <c>pale_hills</c> will, one after, as
/// <c>vathar_crossing</c> will): with clean text the pin and the checker both pass and Raraku is still found by id; with
/// a "placeholder", a ratio word or a faction name in the new map's text the checker (which runs over every map) fails.
/// </summary>
public class D24MapPinQaTests
{
    private const string Clean = "Pale ridges ring a shallow basin; open ground between the camps and timber along the rims.";

    private static GameData LoadWithExtraMaps(string firstDescription)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        string raraku = File.ReadAllText(dir.FullPath("maps/raraku.json"));
        AddMap(dir, raraku, "aa_first", "Aa First", firstDescription);
        AddMap(dir, raraku, "zz_last", "Zz Last", Clean);
        DataLoadResult result = DataLoader.LoadAll(dir.Path);
        Assert.True(result.Ok, string.Join(Environment.NewLine, result.Errors));
        return result.Data!;
    }

    private static void AddMap(TestDataDir dir, string rarakuJson, string id, string name, string description)
    {
        File.WriteAllText(dir.FullPath($"maps/{id}.json"), rarakuJson);
        dir.EditJson($"maps/{id}.json", root =>
        {
            root["id"] = id;
            root["displayName"] = name;
            root["description"] = description;
        });
    }

    private static List<string> CheckAll(GameData d)
    {
        var problems = new List<string>();
        foreach (MapDef m in d.Maps) problems.AddRange(MapTextContentTests.Problems(d, m.Key, m.DisplayName, m.Description));
        return problems;
    }

    [Fact]
    public void ThreeMaps_WithCleanText_PassThePinAndTheChecker_AndRarakuIsFoundById()
    {
        GameData d = LoadWithExtraMaps(Clean);
        Assert.Equal(3, d.Maps.Length);
        Assert.NotEqual("raraku", d.Maps[0].Key); // the index moved: an index-based pin would now read the wrong map
        Assert.Equal("raraku", MapTextContentTests.Raraku(d).Key);
        List<string> pin = MapTextContentTests.PinProblems(MapTextContentTests.Shipped(d), MapTextContentTests.Pinned);
        Assert.True(pin.Count == 0, string.Join("\n", pin));
        List<string> problems = CheckAll(d);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Theory]
    [InlineData("Placeholder: a basin among pale ridges.", "'placeholder'")]
    [InlineData("A basin with twice the timber of the desert.", "ratio word 'twice'")]
    [InlineData("A basin with HALF the open ground.", "ratio word 'half'")]
    [InlineData("Pale ridges and a basin: Whirlwind country.", "names 'Whirlwind'")]
    public void AnUnpinnedMap_WithBadText_FailsTheChecker(string description, string says)
    {
        GameData d = LoadWithExtraMaps(description);
        List<string> pin = MapTextContentTests.PinProblems(MapTextContentTests.Shipped(d), MapTextContentTests.Pinned);
        Assert.Empty(pin); // the pin only covers raraku; the checker is what catches the new map
        List<string> problems = CheckAll(d);
        Assert.Contains(problems, p => p.StartsWith("aa_first:", StringComparison.Ordinal) && p.Contains(says, StringComparison.Ordinal));
    }

    /// <summary>The sim brief's provisional M6-S8 sentences (studio/handoff.md, 2026-11-06-0326), verbatim: they must pass
    /// this branch's checker (now with ratio words) or the conductor's sim → data merge goes red.</summary>
    [Theory]
    [InlineData("Four camps in dense hill woods, joined by narrow paths between the trees. Timber is everywhere, gold is scarce beyond the camps, and every path is a place for an ambush.")]
    [InlineData("A sunken river of slopes cuts the steppe in two, crossed only at its fords. Hold a ford and the far bank is yours; lose it and the enemy pours through.")]
    public void TheSimsProvisionalSentences_PassTheChecker(string description)
    {
        GameData d = LoadWithExtraMaps(description);
        List<string> problems = CheckAll(d);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void ShippedData_HasNoCentreSpelling()
    {
        foreach (string f in Directory.GetFiles(TestDataDir.Shipped, "*.json", SearchOption.AllDirectories))
            Assert.DoesNotContain("centre", File.ReadAllText(f), StringComparison.OrdinalIgnoreCase);
    }
}
