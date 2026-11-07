using System.Reflection;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA (D-R33, 2026-11-07-0236): BUG-1104's new pin rule against real loaded data with a sim-added decoy map (shipped, not
/// pinned): Raraku stays first, the pin reports nothing, and the per-map checker still judges the decoy's own text. Also
/// guards that both stale <c>KnownBeatenAlive</c> allowances (BUG-1110) stay empty, so a regression back to "beaten but
/// alive" fails its row instead of being waved through.
/// </summary>
public class DR33QaTests
{
    [Fact]
    public void AShippedButUnpinnedDecoy_PassesThePin_AndThePerMapCheckerStillJudgesIt()
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

        // The new ThePin_NamesEveryShippedMap_RarakuFirst rule, applied to this data: Raraku first, no pin problem.
        Assert.Equal("raraku", MapTextContentTests.Pinned[0].Id);
        Assert.Equal("raraku", MapTextContentTests.Raraku(d).Key);
        Assert.Empty(MapTextContentTests.PinProblems(MapTextContentTests.Shipped(d), MapTextContentTests.Pinned));

        // The decoy's text is still checked, by id, even though no pin names it.
        Assert.Empty(MapTextContentTests.Problems(d, "aa_decoy", "Decoy", "Open ground between low ridges, with timber along the rims."));
        Assert.Contains(MapTextContentTests.Problems(d, "aa_decoy", "Decoy", "Placeholder: 2 ridges for the Malazan side."),
            p => p.Contains("a digit", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(typeof(D21OpeningsQaTests))]
    [InlineData(typeof(DH6ThresholdQaTests))]
    public void KnownBeatenAlive_StaysEmpty(Type owner)
    {
        FieldInfo? f = owner.GetField("KnownBeatenAlive", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(f);
        var list = (Array)f!.GetValue(null)!;
        Assert.True(list.Length == 0,
            $"{owner.Name}.KnownBeatenAlive has {list.Length} entr(y/ies) again: re-measure, and file a bug rather than re-allow (BUG-1110)");
    }
}
