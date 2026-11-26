using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// BUG-1760 (D37): <see cref="UnitHitBonusText"/>'s first-shot words written with a hyphen ("first-shot", the usual English
/// adjective), any other dash (en dash, em dash, the non-breaking hyphen) or several spaces between the two words are the
/// same words as "first shot" (ruling docs/01 2026-11-25 ACCEPT (5)). Both directions: on a unit without
/// <c>attack.fromStealthBonus</c> such a sentence fails ("data none"); on a copy whose Javelin Thrower carries 0.5 a true
/// one passes and a wrong number in it is read as the first shot's (not only as the field unstated).
/// </summary>
public class UnitHitBonusTextBug1760Tests
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

    /// <summary>A first-shot bonus written with a hyphen, a dash or several spaces, on a unit without the field, fails "data none".</summary>
    [Theory]
    [InlineData(Warrior, "Hidden, its first-shot bonus is +50%.")]
    [InlineData(Warrior, "It deals +50% first-shot damage while stealthed.")]
    [InlineData(Warrior, "Its first-hit damage is 20% higher.")]
    [InlineData(Warrior, "It deals +50% first–shot damage.")] // en dash
    [InlineData(Warrior, "It deals +50% first—shot damage.")] // em dash
    [InlineData(Warrior, "It deals +50% first‑shot damage.")] // non-breaking hyphen
    [InlineData(Warrior, "It deals +50% first - shot damage.")]
    [InlineData(Warrior, "It deals +50% first   shot damage.")]
    [InlineData(Zealot, "It strikes from-hiding for +30% damage.")]
    public void AHyphenatedFirstShotBonus_OnAUnitWithoutTheField_Fails(string unit, string text)
    {
        List<string> p = Read(TestSim.Data, unit, text);
        Assert.True(p.Exists(x => x.StartsWith($"{unit} description {UnitHitBonusText.StealthField}: '", StringComparison.Ordinal) && x.EndsWith("data none", StringComparison.Ordinal)),
            $"no '{UnitHitBonusText.StealthField} ... data none' problem in: {string.Join(" | ", p)}");
    }

    /// <summary>On the unit with the field a true hyphenated (or dashed) first-shot sentence passes.</summary>
    [Theory]
    [InlineData("Its first-shot bonus is +50%.")]
    [InlineData("It deals +50% first-shot damage.")]
    [InlineData("Hidden, its first–shot damage is 50% higher.")]
    public void ATrueHyphenatedFirstShotSentence_OnTheUnitWithTheField_Passes(string text)
    {
        List<string> p = Read(Forged.Value, Archer, text);
        Assert.True(p.Count == 0, string.Join("\n", p));
    }

    /// <summary>
    /// A wrong number in a hyphenated first-shot sentence is read as the first shot's even when another sentence states the
    /// field (before the fix it was not read at all, so this description passed).
    /// </summary>
    [Fact]
    public void AWrongHyphenatedNumber_IsReadAsTheFirstShot_EvenWithTheFieldStatedElsewhere()
    {
        List<string> p = Read(Forged.Value, Archer, "While hidden it deals +50% damage. Its first-shot bonus is +40%.");
        Assert.Contains(p, x => x.StartsWith($"{Archer} description {UnitHitBonusText.StealthField}: '+40%' reads as the first shot from stealth, data +50%", StringComparison.Ordinal));
    }

    /// <summary>"first" with a hyphen before another word is not a first-shot word ("first-rate", "first-born").</summary>
    [Theory]
    [InlineData(Warrior, "A first-rate fighter, 20% faster while hidden.")]
    [InlineData(Warrior, "The first-born of the clan, 10% tougher while stealthed.")]
    public void FirstBeforeAnotherWord_IsNotAFirstShotWord(string unit, string text)
    {
        List<string> p = Read(TestSim.Data, unit, text);
        Assert.True(p.Count == 0, string.Join("\n", p));
    }

    /// <summary>Every shipped unit description of the three factions still passes (the fix refuses nothing shipped).</summary>
    [Fact]
    public void EveryShippedDescription_StillPasses()
    {
        List<string> p = UnitHitBonusText.Problems(TestSim.Data.Units);
        Assert.True(p.Count == 0, string.Join("\n", p));
    }
}
