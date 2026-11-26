using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// BUG-1720 (ruling docs/01 2026-11-25 ACCEPT (5)): <see cref="UnitHitBonusText"/>'s two leftover shapes, each half in both
/// directions. (a) "other ... within" is the pack's words only in a sentence that is not an on-death sentence ("dies",
/// "death", "dying"); "each other" / "per other" / "for every other" still is. (b) A percentage beside "hidden" /
/// "stealthed" is the first-shot bonus only with first-shot words ("first shot", "first hit", "from hiding") or on a unit
/// with <c>attack.fromStealthBonus</c> (where it counts as stating the field when it is the field's number). Read on the
/// shipped data and on a copy whose Javelin Thrower carries <c>fromStealthBonus</c> 0.5 and whose War-dog carries
/// shadow.md's <c>nearbyOwn</c> row (looked up, then replaced: rule 58).
/// </summary>
public class UnitHitBonusTextBug1720Tests
{
    private const string Archer = "teblor_javelin_thrower";
    private const string Hound = "teblor_war_dog";
    private const string Warrior = "teblor_warrior";
    private const string Zealot = "whirlwind_zealot";

    private static readonly Lazy<GameData> Forged = new(() =>
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/teblor/units.json", root =>
        {
            JsonArray units = root["units"]!.AsArray();
            JsonObject Unit(string id) => units.Select(n => n!.AsObject()).Single(u => (string)u["id"]! == id);
            Unit(Archer)["attack"]!.AsObject()["fromStealthBonus"] = 0.5;
            JsonObject dog = Unit(Hound);
            if (dog["passives"] is not JsonArray ps) dog["passives"] = ps = new JsonArray();
            foreach (JsonNode? p in ps.Where(p => (string?)p!["trigger"] == "nearbyOwn").ToArray()) ps.Remove(p);
            ps.Add(JsonNode.Parse("{ \"trigger\": \"nearbyOwn\", \"units\": [\"teblor_war_dog\"], \"radius\": 8, \"damageBonus\": 0.1, \"maxStacks\": 3 }"));
        });
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        return r.Data!;
    });

    private static List<string> Read(GameData d, string unit, string text) => UnitHitBonusText.Problems(d.Units[d.FindUnit(unit)], text);

    private static List<string> Shipped(string unit, string text) => Read(TestSim.Data, unit, text);

    private static List<string> OnForged(string unit, string text) => Read(Forged.Value, unit, text);

    private static void Fails(List<string> p, string unit, string field) =>
        Assert.True(p.Exists(x => x.StartsWith($"{unit} description {field}: ", StringComparison.Ordinal)), $"no {field} problem in: {string.Join(" | ", p)}");

    // ---------- (a) "other ... within" in an on-death sentence ----------

    /// <summary>An on-death sentence with "other ... within" is not the pack's reach (nor its percentage the pack's), on a unit without the pack.</summary>
    [Theory]
    [InlineData(Zealot, "When a Zealot dies, other Zealots within 6 m gain Frenzied (+50% attack speed) for 5 s.")]
    [InlineData(Zealot, "Its death drives the other Zealots within 6 m wild.")]
    [InlineData(Zealot, "Dying, it sends the other Zealots within 6 m into a Frenzy, +50% attack speed.")]
    [InlineData(Warrior, "When a Warrior dies, the other Warriors gain 20% attack speed while within 6 m of it.")]
    [InlineData(Warrior, "Each Warrior's death frenzies the other Warriors within 6 m for 5 s.")]
    public void AnOnDeathOtherWithinSentence_IsNotThePack(string unit, string text)
    {
        List<string> p = Shipped(unit, text);
        Assert.True(p.Count == 0, string.Join("\n", p));
    }

    /// <summary>
    /// The same words in a sentence that is not about a death still read as the pack's: on a unit without the pack they fail
    /// (the bonus the data lacks), "death" inside another word does not hide them, and neither does a death beside "each other".
    /// </summary>
    [Theory]
    [InlineData(Warrior, "Other Warriors within 6 m add 10% damage each.", UnitHitBonusText.PerMateField)]
    [InlineData(Warrior, "Other Warriors within 6 m make it stronger.", UnitHitBonusText.RadiusField)]
    [InlineData(Warrior, "Deathless, other Warriors within 6 m add 10% damage each.", UnitHitBonusText.PerMateField)]
    [InlineData(Warrior, "Each other Warrior within 6 m adds 10% damage until its death.", UnitHitBonusText.PerMateField)]
    [InlineData(Warrior, "For every other Warrior near it deals 5% more, even when one dies.", UnitHitBonusText.PerMateField)]
    public void APackSentence_StillReadsAsThePack(string unit, string text, string field) => Fails(Shipped(unit, text), unit, field);

    /// <summary>On the forged Hound a true pack sentence that mentions a death passes, and a wrong number in it still fails.</summary>
    [Fact]
    public void APackSentenceThatMentionsADeath_IsStillCheckedOnThePackUnit()
    {
        List<string> right = OnForged(Hound, "Each other War-dog within 8 m adds +10% damage, up to +30%, until its death.");
        Assert.True(right.Count == 0, string.Join("\n", right));
        Fails(OnForged(Hound, "Each other War-dog within 8 m adds +20% damage, up to +30%, until its death."), Hound, UnitHitBonusText.PerMateField);
        Fails(OnForged(Hound, "Each other War-dog within 6 m adds +10% damage, up to +30%, until its death."), Hound, UnitHitBonusText.RadiusField);
    }

    // ---------- (b) a percentage beside "hidden" / "stealthed" ----------

    /// <summary>Without first-shot words, on a unit without the field, a percentage beside a stealth word is not the first-shot bonus.</summary>
    [Theory]
    [InlineData(Warrior, "Hidden enemies it strikes are Slowed 20% for 2 s.")]
    [InlineData(Warrior, "While stealthed it moves 20% slower.")]
    [InlineData(Warrior, "It hides in the trees; hidden, it takes 10% less damage.")]
    [InlineData(Archer, "Hidden in the trees it stays 20% harder to hit.")]
    public void AStealthWordPercent_WithoutFirstShotWords_IsNotTheFirstShot(string unit, string text)
    {
        List<string> p = Shipped(unit, text);
        Assert.True(p.Count == 0, string.Join("\n", p));
    }

    /// <summary>With first-shot words the percentage is the first-shot bonus, so on a unit without the field it fails ("data none").</summary>
    [Theory]
    [InlineData(Warrior, "Its first shot from hiding deals +50% damage.")]
    [InlineData(Warrior, "Its first hit on a hidden enemy deals 20% more damage.")]
    [InlineData(Warrior, "From hiding it strikes 30% harder.")]
    [InlineData(Archer, "Hidden, its first shot deals +50% damage.")]
    public void AFirstShotPercent_OnAUnitWithoutTheField_Fails(string unit, string text) => Fails(Shipped(unit, text), unit, UnitHitBonusText.StealthField);

    /// <summary>
    /// On a unit with the field: a status percentage beside "stealthed" next to a true first-shot sentence passes; a stealth
    /// sentence carrying the field's number states it (with no first-shot words); a wrong number in a first-shot sentence
    /// still fails, and a stealth sentence with another number does not count as stating the field.
    /// </summary>
    [Fact]
    public void OnAUnitWithTheField_TheStealthSentenceIsTheFirstShotOnlyWithItsNumberOrTheWords()
    {
        foreach (string ok in new[]
        {
            "Its first shot from hiding deals +50% damage. While stealthed it moves 20% slower.",
            "While hidden it deals +50% damage.",
            "Stealthed, it strikes 50% harder; hidden enemies it hits are Slowed 20% for 2 s.",
        })
        {
            List<string> p = OnForged(Archer, ok);
            Assert.True(p.Count == 0, $"'{ok}': {string.Join("\n", p)}");
        }
        Fails(OnForged(Archer, "Its first shot from hiding deals +40% damage. While stealthed it moves 20% slower."), Archer, UnitHitBonusText.StealthField);
        List<string> unstated = OnForged(Archer, "While hidden it deals +40% damage.");
        Assert.Contains(unstated, x => x.StartsWith($"{Archer} description {UnitHitBonusText.StealthField}: data +50%, which the text does not state", StringComparison.Ordinal));
    }

    /// <summary>Every shipped unit description of the three factions still passes the reading (BUG-1720 refuses nothing new).</summary>
    [Fact]
    public void EveryShippedDescription_StillPasses()
    {
        List<string> p = UnitHitBonusText.Problems(TestSim.Data.Units);
        Assert.True(p.Count == 0, string.Join("\n", p));
    }
}
