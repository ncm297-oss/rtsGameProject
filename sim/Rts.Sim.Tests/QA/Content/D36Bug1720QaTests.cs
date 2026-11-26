using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA of D36's BUG-1720 change to <see cref="UnitHitBonusText"/> (ruling docs/01 2026-11-25 ACCEPT (5)), adversarial
/// sentences for both halves: an on-death sentence with "other" and "within" in different clauses, "dies" / "death"
/// inside another word, a pack sentence that mentions a death, "hidden" with a first-shot bonus on a unit without the
/// field, and the first-shot words written with a hyphen (BUG-1760).
/// </summary>
public class D36Bug1720QaTests
{
    private const string Archer = "teblor_javelin_thrower";
    private const string Warrior = "teblor_warrior";
    private const string Zealot = "whirlwind_zealot";

    private static readonly Lazy<GameData> Forged = new(() =>
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/teblor/units.json", root =>
        {
            JsonArray units = root["units"]!.AsArray();
            units.Select(n => n!.AsObject()).Single(u => (string)u["id"]! == Archer)["attack"]!.AsObject()["fromStealthBonus"] = 0.5;
        });
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        return r.Data!;
    });

    private static List<string> Read(GameData d, string unit, string text) => UnitHitBonusText.Problems(d.Units[d.FindUnit(unit)], text);

    private static void Passes(List<string> p) => Assert.True(p.Count == 0, string.Join("\n", p));

    private static void Fails(List<string> p, string unit, string field) =>
        Assert.True(p.Exists(x => x.StartsWith($"{unit} description {field}: ", StringComparison.Ordinal)), $"no {field} problem in: {string.Join(" | ", p)}");

    /// <summary>(a) On-death sentences, "other" and "within" in different clauses (one sentence), are not the pack on a unit without it.</summary>
    [Theory]
    [InlineData(Zealot, "When it dies, other Zealots go wild, those within 6 m gaining +50% attack speed.")]
    [InlineData(Zealot, "Other Zealots, on its death, frenzy if they stand within 6 m.")]
    [InlineData(Zealot, "Within 6 m of a dying Zealot, the other Zealots gain +50% attack speed.")]
    public void QA_OnDeath_OtherAndWithinInDifferentClauses_IsNotThePack(string unit, string text) => Passes(Read(TestSim.Data, unit, text));

    /// <summary>(a) "dies" / "death" inside another word does not make an on-death sentence: the pack words still fail on a unit without the pack.</summary>
    [Theory]
    [InlineData(Warrior, "Bodies of other Warriors within 6 m shield it.", UnitHitBonusText.RadiusField)]
    [InlineData(Warrior, "Deathgrip: other Warriors within 6 m add 10% damage each.", UnitHitBonusText.PerMateField)]
    [InlineData(Warrior, "Its deathly howl lets other Warriors within 6 m add 10% damage each.", UnitHitBonusText.PerMateField)]
    [InlineData(Warrior, "Other Warriors within 6 m add 10% damage each; it studies their deaths.", UnitHitBonusText.PerMateField)]
    public void QA_DeathInsideAnotherWord_StillReadsThePack(string unit, string text, string field) => Fails(Read(TestSim.Data, unit, text), unit, field);

    /// <summary>(a) A pack sentence that mentions a death, written with "each other", still reads as the pack (fails on a unit without it).</summary>
    [Fact]
    public void QA_EachOtherSentence_WithADeath_StillReadsThePack() =>
        Fails(Read(TestSim.Data, Warrior, "Each other Warrior within 6 m adds 10% damage, lost when it dies."), Warrior, UnitHitBonusText.PerMateField);

    /// <summary>(b) "hidden" with a first-shot bonus on a unit without the field fails "data none" (first-shot words present).</summary>
    [Theory]
    [InlineData(Warrior, "While hidden, its first shot deals +50% damage.")]
    [InlineData(Warrior, "Hidden in the trees, its first attack deals 25% more damage.")]
    [InlineData(Zealot, "Stealthed, it strikes from hiding for +30% damage.")]
    public void QA_HiddenFirstShot_OnAUnitWithoutTheField_Fails(string unit, string text) => Fails(Read(TestSim.Data, unit, text), unit, UnitHitBonusText.StealthField);

    /// <summary>
    /// BUG-1760: the first-shot words written with a hyphen ("first-shot") are not read, so a first-shot bonus a unit does
    /// not have passes. On the base ("first" was a stealth word) these failed as they should.
    /// </summary>
    [Theory(Skip = "BUG-1760 (open): 'first-shot' (hyphen) is not a first-shot word, so a false first-shot bonus passes")]
    [InlineData(Warrior, "Hidden, its first-shot bonus is +50%.")]
    [InlineData(Warrior, "It deals +50% first-shot damage while stealthed.")]
    public void QA_HyphenatedFirstShot_OnAUnitWithoutTheField_Fails(string unit, string text) => Fails(Read(TestSim.Data, unit, text), unit, UnitHitBonusText.StealthField);

    /// <summary>(b) On the forged unit, a wrong number with the hyphenated words is at least caught as the field unstated (no other sentence states it).</summary>
    [Fact]
    public void QA_HyphenatedFirstShot_WrongNumber_OnTheForgedUnit_IsCaughtAsUnstated() =>
        Fails(Read(Forged.Value, Archer, "Hidden, its first-shot bonus is +40%."), Archer, UnitHitBonusText.StealthField);

    /// <summary>(b) Every shipped description still passes (the same reading as the developer's row, re-run independently).</summary>
    [Fact]
    public void QA_EveryShippedDescription_StillPasses() => Passes(UnitHitBonusText.Problems(TestSim.Data.Units));
}
