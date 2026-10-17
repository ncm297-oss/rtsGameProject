using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA, D20 (2026-10-17): the faction pages' re-worded "Defense" lines against docs/03 "Implementation (M5-4f)", case by
/// case, for the clauses <see cref="D20PinQaTests"/> doesn't pin (it pins the numbers, not the rule's wording).
/// </summary>
public class D20DefenseWordingQaTests
{
    private static string DefenseLine(string faction) =>
        FactionPage.Bullets(faction, AiContentTests.Section, 2).Single(b => b.StartsWith("Defense ", StringComparison.Ordinal));

    /// <summary>
    /// docs/03 (M5-4f, "Who answers" (b)): a wave member is pulled only if the members left keep the wave's <b>start
    /// threshold</b> (<c>attack.minArmyPop</c> / <c>minArmyCost</c> x <c>waveSizeMultiplier</c>, kept at the wave's start), so a
    /// wave sent above its threshold gives its surplus. "only while the wave keeps the size it was sent at" says no member
    /// can ever be pulled (any pull shrinks the wave below the size it was sent at).
    /// </summary>
    [Theory]
    [InlineData("malazan")]
    [InlineData("whirlwind")]
    public void DefenseLine_WavePull_IsBoundByTheStartThreshold(string faction)
    {
        string line = DefenseLine(faction);
        Assert.DoesNotContain("the size it was sent at", line);
        Assert.Contains("threshold", line);
    }

    /// <summary>
    /// docs/03 (M5-4f, "Who answers"): the two candidate orders differ, the home army nearest the defense point first, the
    /// wave's members nearest the hall first (BUG-0870's note: "nearest home first" read as one order for both).
    /// </summary>
    [Theory]
    [InlineData("malazan")]
    [InlineData("whirlwind")]
    public void DefenseLine_CandidateOrders_MatchDocs03(string faction)
    {
        string line = DefenseLine(faction);
        Assert.DoesNotContain("nearest home first", line);
        Assert.Contains("the army at home first (nearest the defense point first)", line);
        Assert.Matches(@"then from the members of a wave out \(nearest the [A-Z][A-Za-z ]+ first\)", line);
    }

    /// <summary>
    /// D20's spending paragraph title quotes the gather rates the report ran at; pin them to <c>rules.json</c>
    /// <c>gatherRate</c> so a rate change turns the title red directly, not only through the report numbers.
    /// </summary>
    [Theory]
    [InlineData("malazan")]
    [InlineData("whirlwind")]
    public void SpendingTitle_GatherRates_MatchRulesJson(string faction)
    {
        System.Text.Json.Nodes.JsonNode rules = System.Text.Json.Nodes.JsonNode.Parse(
            File.ReadAllText(Path.Combine(TestDataDir.Shipped, "common", "rules.json")))!;
        double gold = rules["gatherRate"]!["gold"]!.GetValue<double>();
        double wood = rules["gatherRate"]!["wood"]!.GetValue<double>();
        string title = PageTables.FactionLines(faction).Single(l => l.StartsWith("**Spending on the menu's map (D20", StringComparison.Ordinal));
        Assert.Contains($"gathering gold {FactionPage.Text(gold)} / wood {FactionPage.Text(wood)} a second", title);
    }

    /// <summary>docs/03 (M5-4f): the whole-wave recall (c1) compares the threat's worth, not the 1.5x answer, and needs a wave out; the hall rule (c2) needs the hall hit below <c>recallHallHp</c>.</summary>
    [Theory]
    [InlineData("malazan")]
    [InlineData("whirlwind")]
    public void DefenseLine_RecallClauses_MatchDocs03(string faction)
    {
        string line = DefenseLine(faction);
        Assert.Contains("A raid worth more than the home army and the wave's spare units together calls the whole wave home", line);
        Assert.Contains("of its max hp does the same, however small the raid", line);
        // The second quote of defendMinValue, which D20PinQaTests' `ai.x` N pattern doesn't reach.
        Assert.Contains($"(a building hit with nobody seen gets {TestSim.Data.AiRules.DefendMinValue.ToString(System.Globalization.CultureInfo.InvariantCulture)})", line);
        // M5-4b's unconditional "a wave out ends like a retreat" on every threat is gone (M5-4f sized it).
        Assert.DoesNotContain("within it (`rules.json` `ai.defendRadius` 48 m; the threat holds `ai.defendFor` 20 s after the last sign); a wave out ends like a retreat", line);
    }
}
