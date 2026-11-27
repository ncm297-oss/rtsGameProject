using System.Globalization;
using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA for D39 (session 2026-11-26-1847, tier light): the unit-limit description check (<see cref="UnitLimitText"/>) on
/// forged Hound stand-ins (the Teblor War-dog) with packs, and the BUG-1840 per-list oracles of
/// <see cref="AiContentQaTests"/> on a forged entry. Every forge strips the shipped <c>maxAlive</c> first (rule 58).
/// </summary>
public class D39UnitLimitQaTests
{
    private const string Dog = "teblor_war_dog";
    private const string PageSentence = "A horse-sized hound: each other Hound within 8 m adds 10% damage, up to 30%. No more than 7 can live at once.";

    private static UnitDef Forge(int spawnCount, int? maxAlive)
    {
        GameData d = AiScenes.DataWith(dir =>
        {
            MaxAliveLoaderTests.Strip(dir);
            dir.SetUnitField("teblor", Dog, "spawnCount", spawnCount.ToString(CultureInfo.InvariantCulture));
            if (maxAlive is int m) dir.SetUnitField("teblor", Dog, "maxAlive", m.ToString(CultureInfo.InvariantCulture));
        });
        return d.Units[d.FindUnit(Dog)];
    }

    /// <summary>The page's own sentence is read from shadow.md, so the QA rows follow the page, not a copy.</summary>
    [Fact]
    public void ThePageSentenceUsedHere_IsShadowMdsHoundDescription()
    {
        Assert.Equal(PageSentence, UnitLimitPagePin.OwnerReviewUnitDescriptions(PageTables.FactionLines("shadow"))["shadow_hound"]);
    }

    /// <summary>Packs above 1 at a limit of 7 (2, 3, the boundary 7 = spawnCount): the page's sentence passes; at a limit of 6 it fails naming 7 and 6.</summary>
    [Theory]
    [InlineData(2, 7, true)]
    [InlineData(3, 7, true)]
    [InlineData(7, 7, true)]
    [InlineData(3, 6, false)]
    [InlineData(6, 6, false)]
    public void ThePagesSentence_OnAPack_IsJudgedByMaxAliveOnly(int spawnCount, int maxAlive, bool passes)
    {
        UnitDef u = Forge(spawnCount, maxAlive);
        Assert.Equal((spawnCount, maxAlive), (u.SpawnCount, u.MaxAlive));
        List<string> problems = UnitLimitText.Problems(u, PageSentence);
        if (passes) Assert.True(problems.Count == 0, string.Join("\n", problems));
        else Assert.Contains($"{Dog} description maxAlive: 'No more than 7' reads as the unit limit, data {maxAlive}", problems);
    }

    /// <summary>A pack with no limit: the page's sentence is a limit no field holds.</summary>
    [Fact]
    public void ThePagesSentence_OnAPackWithoutTheField_Fails()
    {
        List<string> problems = UnitLimitText.Problems(Forge(3, null), PageSentence);
        Assert.Contains($"{Dog} description maxAlive: 'No more than 7' reads as the unit limit, data none", problems);
    }

    /// <summary>A pack's spawn size stated beside the limit ("3 at a time") is not read as a limit; two limits, one wrong, fail on the wrong one.</summary>
    [Fact]
    public void APacksSizeBesideTheLimit_IsNotALimit_ButASecondWrongLimitIs()
    {
        UnitDef u = Forge(3, 7);
        Assert.Empty(UnitLimitText.Problems(u, "Trained 3 at a time. No more than 7 can live at once."));
        List<string> two = UnitLimitText.Problems(u, "No more than 7 can live at once; at most 6 alive on the field.");
        Assert.Single(two);
        Assert.StartsWith($"{Dog} description maxAlive: 'at most 6'", two[0], StringComparison.Ordinal);
    }

    /// <summary>A count of targets or projectiles "at once" on a unit with no limit is not a unit limit.</summary>
    [Theory(Skip = "BUG-1880: UnitLimitText reads any 'up to / at most N ... at once' as a unit limit")]
    [InlineData("Strikes up to 3 foes at once.")]
    [InlineData("Throws at most 2 spears at once.")]
    public void ATargetCountAtOnce_IsNotAUnitLimit(string text)
    {
        List<string> problems = UnitLimitText.Problems(Forge(1, null), text);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>
    /// BUG-1840 per list: a forged <c>hard</c> entry that never researches Age II. The loader takes it (no order rule); the
    /// oracle should name the entry, and must not throw.
    /// </summary>
    [Fact(Skip = "BUG-1880: CompositionProblems throws 'Sequence contains no elements' on a list without the Age II research")]
    public void AHardEntryWithoutTheAgeResearch_LoadsAndTheCompositionOracleDoesNotThrow()
    {
        string age2 = DataLimits.AgeTechIds[0];
        GameData d = AiScenes.DataWithAi("malazan", root =>
        {
            JsonObject o = AiScenes.Opening(root.AsObject(), 0).AsObject();
            JsonArray steps = JsonNode.Parse(o["buildOrder"]!.ToJsonString())!.AsArray();
            foreach (JsonNode? s in steps.Where(s => (string)s!["do"]! == "research" && (string?)s["id"] == age2).ToArray()) steps.Remove(s);
            o["buildOrders"] = new JsonObject { ["hard"] = steps };
        });
        Exception? thrown = Record.Exception(() => AiContentQaTests.CompositionProblems(d, "malazan", 0, "buildOrders.hard"));
        Assert.Null(thrown);
    }
}
