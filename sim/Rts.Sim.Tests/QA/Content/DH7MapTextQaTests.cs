using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA (D-H7, 2026-11-06-0926): the map-text checker over the three shipped descriptions and the developer's two proposed
/// final sentences (not shipped: the text is mirrored in sim-owned files), each with a mutant per rule (a digit, "twice",
/// "Malazan", "centre"); and the id pin against real loaded data with a shipped-but-unpinned decoy and a pinned-but-missing
/// map. <see cref="MapTextContentTests.Problems"/> has no spelling rule: "centre" is caught only by the whole-data scan
/// <c>D24MapPinQaTests.ShippedData_HasNoCentreSpelling</c>, so the centre mutant here goes through that scan's rule.
/// </summary>
public class DH7MapTextQaTests
{
    private const string ShippedPale =
        "Four camps in dense hill woods, joined by narrow paths between the trees. Timber is everywhere, gold is scarce beyond the camps, and every path is a place for an ambush.";
    private const string ShippedVathar =
        "A sunken river of slopes cuts the steppe in two, crossed only at its fords. Hold a ford and the far bank is yours; lose it and the enemy pours through.";
    private const string ProposedPale =
        "Camps in hill woods, linked by paths through rows of trees. A hill stands between each camp and the open center. Timber is everywhere, gold scarce beyond the camps; every path invites an ambush.";
    private const string ProposedVathar =
        "A sunken riverbed cuts the steppe in two, its steep banks crossed only at a few fords. Gold waits on the banks beside them, and troops down in the bed cannot see up onto either side.";

    public static TheoryData<string, string> Sentences() => new()
    {
        { "pale_hills", ShippedPale },
        { "vathar_crossing", ShippedVathar },
        { "pale_hills", ProposedPale },
        { "vathar_crossing", ProposedVathar },
    };

    private static MapDef Map(GameData d, string id)
    {
        foreach (MapDef m in d.Maps)
            if (m.Key == id) return m;
        throw new InvalidOperationException($"no shipped map '{id}'");
    }

    [Theory]
    [MemberData(nameof(Sentences))]
    public void ShippedAndProposedSentences_PassTheChecker_AndTheSpellingScan(string id, string text)
    {
        MapDef m = Map(TestSim.Data, id);
        List<string> problems = MapTextContentTests.Problems(TestSim.Data, m.Key, m.DisplayName, text);
        Assert.True(problems.Count == 0, string.Join("; ", problems));
        Assert.True(text.Length <= MapTextContentTests.MaxDescriptionLength, $"{text.Length} chars");
        Assert.DoesNotContain("centre", text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(Sentences))]
    public void EachSentence_WithAMutant_IsCaught(string id, string text)
    {
        MapDef m = Map(TestSim.Data, id);
        // Keep the mutant under the length limit so only the injected rule fires.
        string Mutate(string insert) => (insert + " " + text).Length <= MapTextContentTests.MaxDescriptionLength
            ? insert + " " + text
            : insert + " " + text.Substring(0, MapTextContentTests.MaxDescriptionLength - insert.Length - 1);

        Assert.Contains(MapTextContentTests.Problems(TestSim.Data, id, m.DisplayName, Mutate("Has 2 fords.")), p => p.Contains("a digit", StringComparison.Ordinal));
        Assert.Contains(MapTextContentTests.Problems(TestSim.Data, id, m.DisplayName, Mutate("Twice the trees.")), p => p.Contains("ratio word 'twice'", StringComparison.Ordinal));
        Assert.Contains(MapTextContentTests.Problems(TestSim.Data, id, m.DisplayName, Mutate("Malazan land.")), p => p.Contains("names 'Malazan'", StringComparison.Ordinal));

        // "centre": the per-map checker does not look at spelling; the shipped-data scan does. Ship the mutant in a copy
        // of the data and run that scan's rule over the copy.
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson($"maps/{id}.json", root => root["description"] = text.Contains("center", StringComparison.Ordinal) ? text.Replace("center", "centre") : "An open centre. " + text);
        bool found = false;
        foreach (string f in Directory.GetFiles(dir.Path, "*.json", SearchOption.AllDirectories))
            found |= File.ReadAllText(f).Contains("centre", StringComparison.OrdinalIgnoreCase);
        Assert.True(found, "the centre mutant should be visible to the spelling scan");
    }

    /// <summary>A shipped map the pin doesn't name passes the pin (D24's design); the pinned and shipped id sets differ, which D-H7's every-map row failed on until D-R33 (BUG-1104) made it print instead (see <c>DR33QaTests</c>).</summary>
    [Fact]
    public void AShippedButUnpinnedDecoy_PassesThePin_ButNotTheEveryMapRule()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        File.Copy(dir.FullPath("maps/raraku.json"), dir.FullPath("maps/aa_decoy.json"));
        dir.EditJson("maps/aa_decoy.json", root =>
        {
            root["id"] = "aa_decoy";
            root["displayName"] = "Decoy";
            root["description"] = "Open ground between low ridges, with timber along the rims.";
        });
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        GameData d = r.Data!;
        Assert.Empty(MapTextContentTests.PinProblems(MapTextContentTests.Shipped(d), MapTextContentTests.Pinned));
        string[] pinned = MapTextContentTests.Pinned.Select(p => p.Id).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        string[] shipped = d.Maps.Select(m => m.Key).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.NotEqual(shipped, pinned); // since D-R33 ThePin_NamesEveryShippedMap_RarakuFirst prints this as "not pinned yet", not a failure
        Assert.Equal("raraku", MapTextContentTests.Raraku(d).Key);
    }

    [Fact]
    public void APinnedButMissingMap_IsReported()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        File.Delete(dir.FullPath("maps/vathar_crossing.json"));
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        List<string> pin = MapTextContentTests.PinProblems(MapTextContentTests.Shipped(r.Data!), MapTextContentTests.Pinned);
        Assert.Single(pin);
        Assert.Contains("vathar_crossing: pinned but not shipped", pin[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AChangedPinnedDescription_IsReported_ById()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("maps/pale_hills.json", root => root["description"] = ProposedPale);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        List<string> pin = MapTextContentTests.PinProblems(MapTextContentTests.Shipped(r.Data!), MapTextContentTests.Pinned);
        Assert.Single(pin);
        Assert.StartsWith("pale_hills: description", pin[0], StringComparison.Ordinal);
    }
}
