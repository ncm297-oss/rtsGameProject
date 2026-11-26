using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA of D37's BUG-1760 fix to <see cref="UnitHitBonusText"/>: the first-shot words joined by an en dash, an em dash, the
/// non-breaking hyphen, other Unicode dashes, two spaces or a line break, in capitals and in the plural, are read as first-shot
/// words (a false first-shot bonus on a unit without the field fails "data none"); "first" and "shot" far apart in one
/// sentence are not (that percentage is not read as the first shot's).
/// </summary>
public class D37Bug1760QaTests
{
    private const string Archer = "teblor_javelin_thrower";
    private const string Warrior = "teblor_warrior";

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

    [Theory]
    [InlineData("It deals +50% first–shot damage.")] // en dash
    [InlineData("It deals +50% first—shot damage.")] // em dash
    [InlineData("It deals +50% first‑shot damage.")] // non-breaking hyphen
    [InlineData("It deals +50% first‐shot damage.")] // hyphen (U+2010)
    [InlineData("It deals +50% first‒shot damage.")] // figure dash
    [InlineData("It deals +50% first  shot damage.")] // two spaces
    [InlineData("It deals +50% first shot damage.")] // no-break space
    [InlineData("It deals +50% first\nshot damage.")] // line break
    [InlineData("Its First-Shot bonus is +50%.")] // capitals
    [InlineData("Its FIRST-SHOT bonus is +50%.")]
    [InlineData("Its first-shots deal +50% damage.")] // plural
    [InlineData("Its first-hits deal +50% damage.")]
    [InlineData("It strikes from–hiding for +50%.")]
    [InlineData("It strikes From-Stealth for +50%.")]
    public void QA_DashedOrSpacedFirstShot_OnAUnitWithoutTheField_FailsDataNone(string text)
    {
        List<string> p = Read(TestSim.Data, Warrior, text);
        Assert.True(p.Exists(x => x.StartsWith($"{Warrior} description {UnitHitBonusText.StealthField}: '", StringComparison.Ordinal) && x.EndsWith("data none", StringComparison.Ordinal)),
            $"'{text}': no data-none problem in: {string.Join(" | ", p)}");
    }

    /// <summary>"first" and "shot" far apart in one sentence are not first-shot words: the percentage is not the first shot's.</summary>
    [Theory]
    [InlineData("Its first volley is slow, but each later shot deals +50% damage.")]
    [InlineData("First among warriors, it can take a shot for +50% more damage.")]
    [InlineData("The first of the clan to charge; a shot at it deals +50%.")]
    [InlineData("First, it closes in. Shot at, it gains +50% armour.")]
    public void QA_FirstAndShotFarApart_OnAUnitWithoutTheField_IsNotRead(string text)
    {
        List<string> p = Read(TestSim.Data, Warrior, text);
        Assert.True(p.Count == 0, $"'{text}': " + string.Join(" | ", p));
    }

    /// <summary>On the forged unit, a far-apart sentence's other number is not read as the first shot's; the true first-shot sentence states the field.</summary>
    [Fact]
    public void QA_FarApart_OnTheForgedUnit_TheOtherNumberIsNotTheFirstShots()
    {
        List<string> p = Read(Forged.Value, Archer, "Its first volley is slow, but each later shot deals +40% damage. Its first—shot from hiding deals +50% damage.");
        Assert.True(p.Count == 0, string.Join(" | ", p));
    }

    /// <summary>On the forged unit, a wrong number with an en-dashed or capitalised first shot is read as the first shot's (not merely unstated).</summary>
    [Theory]
    [InlineData("While hidden it deals +50% damage. Its First–Shot bonus is +40%.")]
    [InlineData("While hidden it deals +50% damage. Its first  shots deal +40%.")]
    public void QA_WrongDashedNumber_IsReadAsTheFirstShot(string text)
    {
        List<string> p = Read(Forged.Value, Archer, text);
        Assert.Contains(p, x => x.StartsWith($"{Archer} description {UnitHitBonusText.StealthField}: '+40%' reads as the first shot from stealth, data +50%", StringComparison.Ordinal));
    }
}
