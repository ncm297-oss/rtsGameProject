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
    [Theory(Skip = "BUG-0870: the Defense lines say a wave gives members only while it keeps 'the size it was sent at'; docs/03 says its start threshold")]
    [InlineData("malazan")]
    [InlineData("whirlwind")]
    public void DefenseLine_WavePull_IsBoundByTheStartThreshold(string faction)
    {
        string line = DefenseLine(faction);
        Assert.DoesNotContain("the size it was sent at", line);
        Assert.Contains("threshold", line);
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
