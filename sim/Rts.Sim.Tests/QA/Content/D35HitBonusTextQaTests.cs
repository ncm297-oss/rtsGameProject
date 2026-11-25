using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA (D35): <see cref="UnitHitBonusText"/> fed sentences of QA's own on a temp copy of the shipped data whose Javelin
/// Thrower carries <c>attack.fromStealthBonus</c> 0.5 and whose War-dog carries shadow.md's <c>nearbyOwn</c> row (the
/// developer's stand-ins, built independently here). Also pins how the reading treats sentences it was not written for.
/// </summary>
public class D35HitBonusTextQaTests
{
    private const string Archer = "teblor_javelin_thrower";
    private const string Hound = "teblor_war_dog";

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

    private static UnitDef U(string id) => Forged.Value.Units[Forged.Value.FindUnit(id)];

    private static List<string> Read(string unit, string text) => UnitHitBonusText.Problems(U(unit), text);

    /// <summary>QA focus (1): a wrong cap fails at maxStacks, naming the unit.</summary>
    [Theory]
    [InlineData("Each other War-dog within 8 m adds +10% damage, max +20%.")]
    [InlineData("Each other War-dog within 8 m adds +10% damage, at most 40%.")]
    [InlineData("Each other War-dog within 8 m adds 10% damage, capped at 3%.")]
    public void QA_AWrongCap_FailsAtMaxStacks(string text)
    {
        List<string> p = Read(Hound, text);
        Assert.Contains(p, x => x.StartsWith($"{Hound} description {UnitHitBonusText.CapField}: ", StringComparison.Ordinal));
    }

    /// <summary>QA focus (2): a missing radius fails at radius, naming the unit.</summary>
    [Theory]
    [InlineData("Each other War-dog adds +10% damage, up to +30%.")]
    [InlineData("Each other War-dog close by adds 10% damage, up to 30%.")]
    public void QA_AMissingRadius_FailsAtRadius(string text)
    {
        List<string> p = Read(Hound, text);
        Assert.Contains(p, x => x.StartsWith($"{Hound} description {UnitHitBonusText.RadiusField}: ", StringComparison.Ordinal));
    }

    /// <summary>
    /// QA focus (3): "+50 %" with a space is read as 50 and passes, as the passive pin (" ?%") and the docstring
    /// ("+50 %") intend. Pinned so a later change is a decision, not an accident.
    /// </summary>
    [Theory]
    [InlineData("Its first shot from hiding deals +50 % damage.")]
    [InlineData("Its first shot from hiding deals 50 per cent more damage.")]
    public void QA_ASpacedPercent_ReadsAsTheNumber(string text) => Assert.Empty(Read(Archer, text));

    /// <summary>A spaced wrong number still fails.</summary>
    [Fact]
    public void QA_ASpacedWrongPercent_Fails() =>
        Assert.Contains(Read(Archer, "Its first shot from hiding deals +40 % damage."),
            x => x.StartsWith($"{Archer} description {UnitHitBonusText.StealthField}: ", StringComparison.Ordinal));

    /// <summary>Both bonuses' fields absent on a unit that has neither: a plain description has no problems.</summary>
    [Fact]
    public void QA_APlainUnit_HasNoProblems() => Assert.Empty(Read("teblor_warrior", "A giant with a sword."));

    /// <summary>The page's own Hound wording in the Unit notes shape (cap in brackets) also passes the description reading.</summary>
    [Fact]
    public void QA_TheNotesWording_PassesTheDescriptionReading() =>
        Assert.Empty(Read(Hound, "+10% damage for each other War-dog within 8 m (max +30%)."));

    /// <summary>
    /// BUG-1690: a description stating the shipped War-dog's own Hamstring slow (an onHit passive's Slowed magnitude, a
    /// real unit number) is refused as a wrong pack bonus, so the reading blocks a true sentence. Expected: no problem
    /// (the reading does not own that number). Runs on the shipped data, where the War-dog has no pack passive.
    /// </summary>
    /// <summary>
    /// BUG-1691: shadow.md's Archer note "Passive **Shadowed**: becomes Stealthed after 3 s ..." is G1's <c>stillStealth</c>,
    /// not a data passive. Landed as written (on the Javelin Thrower stand-in, with <c>stillStealth</c> in its data), the
    /// passive pin refuses it, so the roster cannot land as the page says, though the readiness check reports 0 problems.
    /// </summary>
    [Fact(Skip = "BUG-1691 (open): the Shadowed sentence (stillStealth, G1) is read as a passive with no trigger; the Shadow roster would land red")]
    public void QA_TheShadowedSentence_LandsGreenOnItsStillStealthRow()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/teblor/units.json", root =>
            root["units"]!.AsArray().Select(n => n!.AsObject()).Single(u => (string)u["id"]! == Archer)["stillStealth"] =
                JsonNode.Parse("{ \"status\": \"stealthed\", \"delay\": 3 }"));
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));

        string[] shadow = PageTables.FactionLines("shadow");
        PassiveContentTests.PagePassive shadowed = Assert.Single(PassiveContentTests.PagePassives(shadow, "shadow.md"), p => p.Name == "Shadowed");
        string[] lines = PageTables.FactionLines("teblor");
        int thrower = Array.FindIndex(lines, l => l.StartsWith("- **Javelin Thrower:**", StringComparison.Ordinal));
        Assert.True(thrower >= 0);
        string firstSentence = shadowed.Text[..(shadowed.Text.IndexOf(". ", StringComparison.Ordinal) + 1)];
        lines[thrower] += $" Passive **Shadowed**: {firstSentence}";

        List<string> problems = PassiveContentTests.Compare(r.Data!, "teblor", lines).Problems;
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact(Skip = "BUG-1690 (open): UnitHitBonusText reads every unit percentage as a hit bonus; a true Hamstring 'slows by 20%' is refused as passives[nearbyOwn].damageBonus")]
    public void QA_TheWarDogsHamstringSlow_IsNotReadAsAPackBonus()
    {
        GameData d = TestSim.Data;
        UnitDef dog = d.Units[d.FindUnit(Hound)];
        UnitPassive hamstring = Assert.Single(dog.Passives, p => p.Trigger == PassiveTrigger.OnHit);
        int pct = (int)Math.Round(hamstring.Magnitude * 100);
        List<string> p = UnitHitBonusText.Problems(dog, $"Big shaggy dogs. Each bite slows its victim by {pct}% so the giants can catch it.");
        Assert.True(p.Count == 0, string.Join("\n", p));
    }
}
