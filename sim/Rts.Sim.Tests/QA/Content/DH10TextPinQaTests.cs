using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA (D-H10): BUG-1690's and BUG-1691's fixes fed sentences of QA's own. <see cref="UnitHitBonusText"/> on the shipped
/// data and on a copy whose Javelin Thrower carries <c>attack.fromStealthBonus</c> 0.5 and whose War-dog carries shadow.md's
/// <c>nearbyOwn</c> row; <see cref="PassiveContentTests.Compare"/> on <see cref="StillScenes.Still"/> (the Javelin Thrower carries
/// <c>stillStealth</c>, stealthed, 3 s) with a Shadowed sentence on its teblor.md bullet.
/// </summary>
public class DH10TextPinQaTests
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

    private static UnitDef U(GameData d, string id) => d.Units[d.FindUnit(id)];

    private static List<string> Shipped(string unit, string text) => UnitHitBonusText.Problems(U(TestSim.Data, unit), text);

    private static List<string> OnForged(string unit, string text) => UnitHitBonusText.Problems(U(Forged.Value, unit), text);

    // ---------- BUG-1690: true sentences the reading must leave alone ----------

    /// <summary>True status / on-death sentences with no pack and no first-shot words: not read (these pass).</summary>
    [Theory]
    [InlineData("whirlwind_zealot", "When a Zealot dies, Zealots within 6 m gain Frenzied (+50% attack speed) for 5 s.")]
    [InlineData("whirlwind_zealot", "Below 50% HP it gains Frenzied, +50% attack speed.")]
    [InlineData(Hound, "Each bite leaves its target Slowed 20% for 2 s.")]
    [InlineData(Hound, "Hamstring: each bite on a target that survives it leaves it Slowed 20% for 2 s, so the pack pins kiting units.")]
    [InlineData("teblor_breaker", "Each blow per target slows it by 20 per cent.")]
    public void QA_ATrueStatusSentence_IsNotRead(string unit, string text)
    {
        List<string> p = Shipped(unit, text);
        Assert.True(p.Count == 0, string.Join("\n", p));
    }

    /// <summary>
    /// BUG-1720 (a): an on-death sentence with "other ... within" (the natural English for the Zealot's own Martyrdom) is
    /// read as the pack's reach on a unit with no pack, so a true description is refused (the ruling's "other ... within"
    /// pack words do not tell a pack from an on-death area).
    /// </summary>
    [Theory(Skip = "BUG-1720 (open): 'other <Unit>s within N m' in an on-death sentence reads as passives[nearbyOwn].radius")]
    [InlineData("whirlwind_zealot", "When a Zealot dies, other Zealots within 6 m gain Frenzied (+50% attack speed) for 5 s.")]
    [InlineData("whirlwind_zealot", "Its death drives the other Zealots within 6 m wild.")]
    public void QA_AnOnDeathSentenceWithOther_IsNotReadAsThePack(string unit, string text)
    {
        List<string> p = Shipped(unit, text);
        Assert.True(p.Count == 0, string.Join("\n", p));
    }

    /// <summary>
    /// BUG-1720 (b): a status percentage in a sentence that names stealth ("hidden", "stealthed") but not a first shot is
    /// read as the first-shot bonus: on a unit without the field it fails "data none", on the Archer it fails as a wrong
    /// first-shot bonus beside a correct first-shot sentence.
    /// </summary>
    [Theory(Skip = "BUG-1720 (open): a percentage beside 'hidden' / 'stealthed' reads as attack.fromStealthBonus without first-shot words")]
    [InlineData(false, "teblor_warrior", "Hidden enemies it strikes are Slowed 20% for 2 s.")]
    [InlineData(true, Archer, "Its first shot from hiding deals +50% damage. While stealthed it moves 20% slower.")]
    public void QA_AStealthWordStatusPercent_IsNotReadAsTheFirstShot(bool forged, string unit, string text)
    {
        List<string> p = forged ? OnForged(unit, text) : Shipped(unit, text);
        Assert.True(p.Count == 0, string.Join("\n", p));
    }

    // ---------- BUG-1690: wrong hit bonuses must still fail ----------

    /// <summary>A wrong pack percentage still fails at the per-mate field, beside a true Hamstring sentence.</summary>
    [Theory]
    [InlineData("Each other War-dog within 8 m adds +15% damage, up to +30%. Its bite leaves the target Slowed 20% for 2 s.", UnitHitBonusText.PerMateField)]
    [InlineData("+10% damage for every other War-dog within 6 m (max +30%).", UnitHitBonusText.RadiusField)]
    [InlineData("For every other War-dog within 8 m it deals 10 per cent more, at most +40%.", UnitHitBonusText.CapField)]
    public void QA_AWrongPackNumber_StillFails(string text, string field)
    {
        List<string> p = OnForged(Hound, text);
        Assert.Contains(p, x => x.StartsWith($"{Hound} description {field}: ", StringComparison.Ordinal));
    }

    /// <summary>The forged Hound's true pack sentence plus a true Hamstring sentence (no "each" in the second) passes.</summary>
    [Fact]
    public void QA_ATruePackAndHamstring_Pass()
    {
        List<string> p = OnForged(Hound, "Each other War-dog within 8 m adds +10% damage, up to +30%. Its bite leaves the target Slowed 20% for 2 s.");
        Assert.True(p.Count == 0, string.Join("\n", p));
    }

    /// <summary>A pack sentence on a unit without the pack still fails (the bonus the data lacks), and a wrong first shot too.</summary>
    [Theory]
    [InlineData("teblor_warrior", "For every other Warrior within 6 m it hits 5% harder.", UnitHitBonusText.PerMateField)]
    [InlineData("teblor_warrior", "Per other Warrior it deals 5% more.", UnitHitBonusText.PerMateField)]
    [InlineData(Archer, "Its first shot from hiding deals +45% damage.", UnitHitBonusText.StealthField)]
    public void QA_AWrongBonus_OnTheWrongUnit_Fails(string unit, string text, string field)
    {
        List<string> p = OnForged(unit, text);
        Assert.Contains(p, x => x.StartsWith($"{unit} description {field}: ", StringComparison.Ordinal));
    }

    // ---------- BUG-1691: the stand-still sentence ----------

    private static List<string> Shadowed(string sentence)
    {
        string[] lines = PageTables.FactionLines("teblor");
        int thrower = Array.FindIndex(lines, l => l.StartsWith("- **Javelin Thrower:**", StringComparison.Ordinal));
        Assert.True(thrower >= 0);
        lines[thrower] += $" Passive **Shadowed**: {sentence}";
        return PassiveContentTests.Compare(StillScenes.Still, "teblor", lines).Problems;
    }

    [Theory]
    [InlineData("becomes Stealthed after 3 s without moving.")]
    [InlineData("becomes Stealthed after 3.0 s without moving or attacking, anywhere.")]
    [InlineData("Stealthed after 3 s without moving.")]
    public void QA_ATrueStandStillSentence_Passes(string sentence)
    {
        List<string> p = Shadowed(sentence);
        Assert.True(p.Count == 0, string.Join("\n", p));
    }

    /// <summary>Wrong delays (and wordings the reading cannot place) fail; none passes silently.</summary>
    [Theory]
    [InlineData("becomes Stealthed after 30 s without moving.", "stillStealth.delay")]
    [InlineData("becomes Stealthed after 0.3 s without moving.", "stillStealth.delay")]
    [InlineData("becomes Stealthed after 3.05 s without moving.", "stillStealth.delay")]
    [InlineData("becomes Hidden after 3 s without moving.", "stillStealth.status")]
    [InlineData("becomes Stealthed after 4 seconds without moving.", "'Shadowed'")]
    [InlineData("becomes stealthed after 3 s without moving.", "'Shadowed'")]
    [InlineData("is Stealthed when it has not moved for 4 s.", "'Shadowed'")]
    public void QA_AWrongStandStillSentence_Fails(string sentence, string want)
    {
        List<string> p = Shadowed(sentence);
        Assert.Contains(p, x => x.Contains(want, StringComparison.Ordinal));
    }
}
