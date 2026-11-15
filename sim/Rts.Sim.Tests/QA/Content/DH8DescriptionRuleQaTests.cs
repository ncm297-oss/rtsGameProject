using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA D-H8 (session 2026-11-13-2306): BUG-1330's stricter description-number rule (a status row's <c>damageBonus</c> and
/// <c>onEnd</c> read from their own phrases, each required to be stated) attacked with QA's own mutants, beyond the
/// developer's: the right number in the wrong words, both row numbers moved to the same new value, an <c>onEnd</c> that is
/// not a slow, a <c>damageBonus</c> with no <c>onEnd</c>, a damage bonus promised on a row without one; and the shipped
/// rows (Malazan, Whirlwind, Teblor, the shared statuses) still clean under it.
/// </summary>
public class DH8DescriptionRuleQaTests
{
    private const string Ability = "blood_oil_frenzy";

    private static void AssertNone(IEnumerable<string> problems) => Assert.True(!problems.Any(), string.Join("\n", problems));

    private static void AssertNamed(IEnumerable<string> problems, string want) =>
        Assert.True(problems.Any(p => p.Contains(want, StringComparison.Ordinal)), $"no problem containing '{want}':\n" + string.Join("\n", problems));

    private static List<string> Problems(GameData d) => AbilityContentTests.DescriptionProblems(d, d.Abilities, d.Statuses);

    private static List<string> Problems((System.Collections.Immutable.ImmutableArray<AbilityDef> A, System.Collections.Immutable.ImmutableArray<StatusDef> S) m, GameData d) =>
        AbilityContentTests.DescriptionProblems(d, m.A, m.S);

    /// <summary>The landing with the Frenzy status row edited in place (and, optionally, the ability's description replaced).</summary>
    private static GameData Status(Action<JsonObject> edit, string? abilityText = null) => TeblorLanding.DataWith(dir =>
    {
        dir.EditJson("common/statuses.json", root => edit(root["statuses"]!.AsArray().Single(s => (string)s!["id"]! == TeblorLanding.Status)!.AsObject()));
        if (abilityText != null)
            dir.EditJson("factions/teblor/abilities.json", root => root["abilities"]!.AsArray().Single(a => (string)a!["id"]! == Ability)!["description"] = abilityText);
    });

    // ---------- shipped rows ----------

    /// <summary>Every shipped ability and status (every faction) is clean under the stricter rule, with nothing merely reported.</summary>
    [Fact]
    public void ShippedData_EveryRow_IsCleanUnderTheStricterRule()
    {
        var reports = new List<string>();
        AssertNone(AbilityContentTests.DescriptionProblems(TestSim.Data, TestSim.Data.Abilities, TestSim.Data.Statuses, reports));
        AssertNone(reports);
        // Not vacuous: abilities of at least the three shipped factions were read.
        foreach (string f in new[] { "malazan", "whirlwind", "teblor" })
            Assert.Contains(TestSim.Data.Abilities, a => a.Faction == TestSim.Data.FindFaction(f));
    }

    // ---------- the right number, the wrong words ----------

    [Theory]
    // The status tooltip: the bonus's 20 moved into speed words; the aftermath's 20 under another status's name.
    [InlineData(TeblorLanding.Status, "deals 20% more damage", "attacks 20% faster", "teblor_frenzied description: data percentage (damage bonus")]
    [InlineData(TeblorLanding.Status, "Slowed 20% for 4 seconds", "Blinded 20% for 4 seconds", "teblor_frenzied description: data percentage (the aftermath's magnitude")]
    [InlineData(TeblorLanding.Status, "Slowed 20% for 4 seconds", "Slowed 20%, attacks for 4 seconds", "teblor_frenzied description: data duration (the aftermath's")]
    // The ability tooltip: the bonus in "less damage taken" words; the aftermath under another name; the 40% in damage words.
    [InlineData(Ability, "deal 20% more damage", "take 20% less damage", "blood_oil_frenzy description: data percentage (damage bonus")]
    [InlineData(Ability, "then are Slowed 20%", "then are Blinded 20%", "blood_oil_frenzy description: data percentage (the aftermath's magnitude")]
    [InlineData(Ability, "attack 40% faster and deal 20% more damage", "attack 20% faster and deal 40% more damage", "blood_oil_frenzy description: '40%' reads as a percentage (damage bonus")]
    public void TheRightNumber_InTheWrongWords_IsNamed(string key, string text, string mutant, string want)
    {
        GameData d = TeblorLanding.Data;
        AssertNone(Problems(d));
        AssertNamed(Problems(AbilityContentTests.WithDescription(key, text, mutant, d), d), want);
    }

    // ---------- both row numbers moved together ----------

    /// <summary>damageBonus and onEnd.magnitude both 0.3 (so they still agree with each other): both tooltips name both.</summary>
    [Fact]
    public void BothTwenties_MovedToTheSameNewValue_EachIsNamedOnBothTooltips()
    {
        GameData wrong = Status(s => { s["damageBonus"] = 0.3; s["onEnd"]!["magnitude"] = 0.3; });
        List<string> p = Problems(wrong);
        foreach (string key in new[] { TeblorLanding.Status, Ability })
        {
            AssertNamed(p, $"{key} description: '20%' reads as a percentage (damage bonus");
            AssertNamed(p, $"{key} description: '20%' reads as a percentage (the aftermath's magnitude");
            AssertNamed(p, $"{key} description: data percentage (damage bonus x 100: 'N% more damage') 30");
            AssertNamed(p, $"{key} description: data percentage (the aftermath's magnitude x 100: '<onEnd status> N%') 30");
        }
    }

    /// <summary>The text updated for one of the two moved numbers only: the other is still named.</summary>
    [Fact]
    public void BothMoved_TextUpdatedForTheBonusOnly_TheAftermathIsNamed()
    {
        GameData wrong = Status(s =>
        {
            s["damageBonus"] = 0.3;
            s["onEnd"]!["magnitude"] = 0.3;
            s["description"] = "Attacks faster and deals 30% more damage. When it ends, the unit is Slowed 20% for 4 seconds.";
        });
        List<string> p = Problems(wrong).Where(x => x.StartsWith(TeblorLanding.Status + " ", StringComparison.Ordinal)).ToList();
        AssertNamed(p, "teblor_frenzied description: '20%' reads as a percentage (the aftermath's magnitude");
        Assert.DoesNotContain(p, x => x.Contains("(damage bonus", StringComparison.Ordinal));
    }

    // ---------- an onEnd that is not a slow ----------

    /// <summary>
    /// An aftermath of Burning (a damage over time: its magnitude is a rate, not a percentage): the tooltip naming Burning
    /// for the data's seconds is clean; the seconds moved, or the aftermath left out, are named.
    /// </summary>
    [Theory]
    [InlineData("When it ends, the unit is Burning for 4 seconds.", null)]
    [InlineData("When it ends, the unit is Burning for 5 seconds.", "teblor_frenzied description: '5 seconds' reads as a duration (the aftermath's")]
    [InlineData("When it ends, the unit catches fire.", "teblor_frenzied description: data duration (the aftermath's")]
    [InlineData("When it ends, the unit is Slowed 20% for 4 seconds.", "teblor_frenzied description: '20%' reads as a percentage")]
    // BUG-1360 (D-H9): the rate in parentheses between the name and "for" keeps the link; a wrong rate or wrong seconds still fail.
    [InlineData("When it ends, the unit is Burning (5 damage a second) for 4 seconds.", null)]
    [InlineData("When it ends, the unit is Burning (6 damage a second) for 4 seconds.", "teblor_frenzied description: '6' reads as a damage per second")]
    [InlineData("When it ends, the unit is Burning (5 damage a second) for 3 seconds.", "teblor_frenzied description: '3 seconds' reads as a duration (the aftermath's")]
    public void AnAftermathOfBurning_IsReadAsItsOwnKind(string tail, string? want)
    {
        GameData d = Status(s =>
        {
            s["onEnd"] = new JsonObject { ["status"] = "burning", ["magnitude"] = 5, ["duration"] = 4 };
            s["description"] = "Attacks faster and deals 20% more damage. " + tail;
        }, abilityText: "Drives nearby warriors into a frenzy: own units within 8 m attack 40% faster and deal 20% more damage for 10 seconds, then are Burning for 4 seconds.");
        List<string> p = Problems(d);
        Assert.DoesNotContain(p, x => x.StartsWith(Ability + " ", StringComparison.Ordinal));
        if (want == null) AssertNone(p);
        else AssertNamed(p, want);
    }

    // ---------- a damage bonus with no onEnd ----------

    /// <summary>
    /// A row with damageBonus and no onEnd: its own "N% more damage" is held; the ability text that still promises "Slowed
    /// 20% for 4 seconds" (an aftermath the data no longer has) is named.
    /// </summary>
    [Theory]
    [InlineData("Attacks faster and deals 20% more damage.", null)]
    [InlineData("Attacks faster and deals 25% more damage.", "teblor_frenzied description: '25%' reads as a percentage (damage bonus")]
    [InlineData("Attacks faster and hits harder.", "teblor_frenzied description: data percentage (damage bonus")]
    public void ADamageBonus_WithNoOnEnd_IsHeld(string text, string? want)
    {
        GameData d = Status(s => { s.Remove("onEnd"); s["description"] = text; });
        List<string> p = Problems(d);
        AssertNamed(p, "blood_oil_frenzy description: '20%' reads as a percentage");
        List<string> own = p.Where(x => x.StartsWith(TeblorLanding.Status + " ", StringComparison.Ordinal)).ToList();
        if (want == null) AssertNone(own);
        else AssertNamed(own, want);
    }

    /// <summary>A damage bonus promised on a row with none (the shared Frenzied, the Zealot's) is named: the phrase role is read in every entry.</summary>
    [Fact]
    public void ADamageBonus_PromisedOnARowWithout_IsNamed()
    {
        GameData d = TestSim.Data;
        var m = AbilityContentTests.WithDescription("frenzied", "Attacks faster:", "Attacks faster and deals 10% more damage:", d);
        AssertNamed(Problems(m, d), "frenzied description: '10%' reads as a percentage (damage bonus x 100: 'N% more damage'), but its data percentage (damage bonus x 100: 'N% more damage') is none");
    }

    // ---------- accepted phrasings (the rule must not over-reach) ----------

    [Theory]
    [InlineData(TeblorLanding.Status, "Slowed 20% for 4 seconds", "Slowed by 20% for 4 s")]
    [InlineData(TeblorLanding.Status, "deals 20% more damage", "deals +20% damage")]
    [InlineData(TeblorLanding.Status, "Slowed 20% for 4 seconds", "slowed 20% for 4 seconds")]
    [InlineData(Ability, "then are Slowed 20% for 4 seconds", "then are Slowed by 20% for 4 s")]
    public void EquivalentPhrasings_AreAccepted(string key, string text, string mutant)
    {
        GameData d = TeblorLanding.Data;
        AssertNone(Problems(AbilityContentTests.WithDescription(key, text, mutant, d), d));
    }
}
