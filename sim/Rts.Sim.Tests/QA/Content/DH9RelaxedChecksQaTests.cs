using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA D-H9 (session 2026-11-15-0506): each check D-H9 relaxed is still a check. The own-names blanking in
/// <c>UnitContentTests</c> E / <c>BuildingContentTests</c> F, the spawn-only summon's Radius "—" in <c>AbilityContentTests</c>,
/// the parenthesised damage rate (BUG-1360) and the unread-passive readiness problem (BUG-1450), each attacked with QA's own
/// mutants of the page and of the data, both ways.
/// </summary>
public class DH9RelaxedChecksQaTests
{
    private static GameData Data => TestSim.Data;

    private static List<string> OwnOf(string faction) => OwnNames.Of(Data, Data.FindFaction(faction));

    // ---------- own names ----------

    /// <summary>
    /// The Shadow shape (shadow.md's names as the own names): a foreign name standing next to, before, after or between own
    /// names still fails; overlapping own names ("Edur Warrior Lodge") and a plural of an own name pass.
    /// </summary>
    [Theory]
    [InlineData("Edur Warrior Lodge veterans.", null)]
    [InlineData("Edur Warriors drill at the Warrior Lodge.", null)]
    [InlineData("A Warrior, then an Edur Warrior.", "Warrior")]
    [InlineData("An Edur Warrior Warrior.", "Warrior")]
    [InlineData("The Warrior Lodge Warrior.", "Warrior")]
    [InlineData("An Edur Warrior who hunts the Teblor.", "Teblor")]
    [InlineData("An Edur Warrior with a War-dog.", "War-dog")]
    public void ShadowShape_AForeignNameOutsideEveryOwnName_StillFails(string text, string? foreign)
    {
        List<string> p = UnitContentTests.ForeignNameProblems(Data, "qa", text, -1, UnitContentTests.ShadowPageNames());
        if (foreign == null) Assert.True(p.Count == 0, string.Join("\n", p));
        else Assert.Equal(new[] { $"qa names {foreign}" }, p);
    }

    /// <summary>
    /// The data side: a shipped faction's own names come from the data only, so a page-only name ("Warrior Lodge") does not
    /// protect another faction's "Warrior" in a Malazan text; the Teblor may say "Warrior" but not "Heavy Infantry".
    /// </summary>
    [Theory]
    [InlineData("malazan", "Trains at a Warrior Lodge.", "Warrior")]
    [InlineData("malazan", "Heavy Infantry hold the line.", null)]
    [InlineData("teblor", "A Warrior who outfights Heavy Infantry.", "Heavy Infantry")]
    [InlineData("teblor", "A Warrior, trained at the Longhouse.", null)]
    [InlineData("whirlwind", "Rides with a Raider beside the Destrier Rider.", "Destrier Rider")]
    public void ShippedFaction_OwnNamesFromData_AForeignNameStillFails(string faction, string text, string? foreign)
    {
        List<string> p = UnitContentTests.ForeignNameProblems(Data, "qa", text, Data.FindFaction(faction), OwnOf(faction));
        if (foreign == null) Assert.True(p.Count == 0, string.Join("\n", p));
        else Assert.Contains($"qa names {foreign}", p);
        // F (buildings) reads the same blanking for units.
        List<string> f = BuildingContentTests.ForeignUnitProblems(Data, "qa", text, Data.FindFaction(faction), OwnOf(faction));
        if (foreign == null) Assert.True(f.Count == 0, string.Join("\n", f));
        else Assert.Contains($"qa names {foreign}", f);
    }

    // ---------- Radius "—" ----------

    /// <summary>
    /// "—" for a Radius the data has fails on every shipped non-summon kind: Telas Fire is the developer's target-ground row;
    /// here the Teblor's self / aura Blood-oil Frenzy (8 m in the data) on a page copy.
    /// </summary>
    [Fact]
    public void ADashRadius_OnASelfAura_WithARadius_Fails()
    {
        string[] lines = PageTables.FactionLines("teblor");
        int i = Array.FindIndex(lines, l => l.StartsWith("| Blood-oil Frenzy | Shaman | Self / aura | — | 8 m |", StringComparison.Ordinal));
        Assert.True(i >= 0, "teblor.md's Blood-oil Frenzy row moved");
        AbilityContentTests.Result clean = AbilityContentTests.Compare(Data, Data.Abilities, "teblor", lines);
        Assert.DoesNotContain(clean.Problems, p => p.Contains("'Blood-oil Frenzy' Radius:", StringComparison.Ordinal));
        string[] edited = (string[])lines.Clone();
        edited[i] = lines[i].Replace("| — | 8 m |", "| — | — |", StringComparison.Ordinal);
        AbilityContentTests.Result r = AbilityContentTests.Compare(Data, Data.Abilities, "teblor", edited);
        Assert.Contains(r.Problems, p => p.Contains("'Blood-oil Frenzy' Radius:", StringComparison.Ordinal) && p.Contains("'—'", StringComparison.Ordinal));
    }

    // ---------- the parenthesised rate (BUG-1360) ----------

    private static GameData Burning(double magnitude, double duration, string tail) => TeblorLanding.DataWith(dir =>
    {
        dir.EditJson("common/statuses.json", root =>
        {
            JsonObject s = root["statuses"]!.AsArray().Single(x => (string)x!["id"]! == TeblorLanding.Status)!.AsObject();
            s["onEnd"] = new JsonObject { ["status"] = "burning", ["magnitude"] = magnitude, ["duration"] = duration };
            s["description"] = "Attacks faster and deals 20% more damage. " + tail;
        });
        dir.EditJson("factions/teblor/abilities.json", root => root["abilities"]!.AsArray().Single(a => (string)a!["id"]! == "blood_oil_frenzy")!["description"] =
            $"Drives nearby warriors into a frenzy: own units within 8 m attack 40% faster and deal 20% more damage for 10 seconds, then are Burning for {duration} seconds.");
    });

    private static List<string> StatusProblems(GameData d) =>
        AbilityContentTests.DescriptionProblems(d, d.Abilities, d.Statuses).Where(p => p.StartsWith(TeblorLanding.Status + " ", StringComparison.Ordinal)).ToList();

    /// <summary>
    /// A parenthesis must not hide a wrong number: a duration inside the parenthesis, the data's seconds or rate moved under
    /// a page that kept the old ones, and the "dmg/s" form all still fail; other rate wordings pass.
    /// </summary>
    [Theory]
    [InlineData(5, 4, "When it ends, the unit is Burning (5 fire damage per second) for 4 seconds.", null)]
    [InlineData(5, 4, "When it ends, the unit is Burning (5 dmg/s) for 4 seconds.", null)]
    [InlineData(5, 4, "When it ends, the unit is Burning (5 dmg/s) for 3 seconds.", "'3 seconds' reads as a duration")]
    [InlineData(5, 4, "When it ends, the unit is Burning (5 damage a second for 3 seconds) for 4 seconds.", "'3 seconds'")]
    [InlineData(5, 4, "When it ends, the unit is Burning (lasts 3 seconds) for 4 seconds.", "'3 seconds'")]
    [InlineData(5, 3, "When it ends, the unit is Burning (5 damage a second) for 4 seconds.", "'4 seconds' reads as a duration")]
    [InlineData(6, 4, "When it ends, the unit is Burning (5 damage a second) for 4 seconds.", "'5' reads as a damage per second")]
    public void AParenthesis_DoesNotHideAWrongNumber(double magnitude, double duration, string tail, string? want)
    {
        List<string> p = StatusProblems(Burning(magnitude, duration, tail));
        if (want == null) Assert.True(p.Count == 0, string.Join("\n", p));
        else Assert.True(p.Any(x => x.Contains(want, StringComparison.Ordinal)), $"no problem containing '{want}':\n" + string.Join("\n", p));
    }

    // ---------- unread passives (BUG-1450) ----------

    /// <summary>Upper-case "PASSIVE" and a second lower-case passive in the same bullet are each one readiness problem.</summary>
    [Theory]
    [InlineData("Passive **Shadowed**:", "PASSIVE **Shadowed**:")]
    [InlineData("forests). The first shot", "forests). passive **Ambush**: The first shot")]
    public void AnUnreadPassive_IsOneReadinessProblem(string find, string replace)
    {
        string[] lines = PageTables.FactionLines("shadow");
        int i = Array.FindIndex(lines, l => l.Contains(find, StringComparison.Ordinal));
        Assert.True(i >= 0, $"shadow.md has no line containing '{find}'");
        string[] edited = (string[])lines.Clone();
        edited[i] = lines[i].Replace(find, replace, StringComparison.Ordinal);
        List<string> p = PagePinReadinessTests.Problems("shadow", edited);
        Assert.True(p.Count == 1 && Regex.IsMatch(p[0], @"^shadow\.md Unit notes line \d+: "), string.Join("\n", p));
    }
}
