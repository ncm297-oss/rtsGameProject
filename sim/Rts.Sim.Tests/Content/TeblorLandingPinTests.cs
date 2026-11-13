using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D30: the content pins taught M7-4's fields before any shipped row uses them, proven on <see cref="TeblorLanding"/>'s copy
/// (docs/03 "For the data track (M7-4)"'s four rows, with teblor.md and docs/02 as the landing leaves them). Criterion 1:
/// the pins that read the new fields pass on the copy, run through their own bodies (<see cref="ContentScope"/>).
/// Criterion 2: each new arm fails on a wrong copy, by name: the Anointing at 5 s against the page's "+4 s", a Frenzy
/// description saying 25% against <c>damageBonus</c> 0.2, an <c>onEnd</c> of 5 s against "for 4 s", a building at 1.5
/// against the page's 1.25; and the page's "lands with D31" notes read both ways.
/// </summary>
public class TeblorLandingPinTests
{
    private const string Teblor = "teblor";

    private static GameData Copy => TeblorLanding.Data;

    private static void AssertNone(IEnumerable<string> problems) => Assert.True(!problems.Any(), string.Join("\n", problems));

    private static void AssertNamed(IEnumerable<string> problems, string want) =>
        Assert.True(problems.Any(p => p.Contains(want, StringComparison.Ordinal)), $"no problem containing '{want}':\n" + string.Join("\n", problems));

    private static string[][] StatusRows() => FactionPage.DocTable("docs/02-game-design.md", "Status effects", level: 3);

    // ---------- the copy is what docs/03 asks for ----------

    [Fact]
    public void TheCopy_CarriesDocs03sFourRows_AndKeepsTheM4StatusHashNumbers()
    {
        StatusDef s = Copy.Statuses[Copy.FindStatus(TeblorLanding.Status)];
        Assert.Equal((StatusKind.Frenzied, 0.2f, "slowed", 0.2f, 4 * SimConstants.TicksPerSecond),
            (s.Kind, s.DamageBonus, Copy.Statuses[s.OnEndStatus].Key, s.OnEndMagnitude, s.OnEndTicks));
        AbilityEffect e = Assert.Single(Copy.Abilities[Copy.FindAbility("blood_oil_frenzy")].Effects);
        Assert.Equal((s.Id, 0.4f, 10 * SimConstants.TicksPerSecond), (e.Status, e.Magnitude, e.DurationTicks));
        TechEffect d = Copy.Techs[Copy.FindTech("teblor_blood_oil_anointing")].Effects[^1];
        Assert.Equal((TechStat.StatusDuration, 4f * SimConstants.TicksPerSecond, s.Id), (d.Stat, d.Amount, Assert.Single(d.Statuses)));
        int f = Copy.FindFaction(Teblor);
        Assert.All(Copy.Buildings.Where(b => b.Faction == f), b => Assert.Equal(1.25f, b.Scale));
        Assert.All(Copy.Buildings.Where(b => b.Faction != f), b => Assert.Equal(1f, b.Scale));
        // docs/03's id rule: after `stealthed`, so every shipped status of the M4 kinds keeps its state-hash number.
        foreach (StatusDef before in TeblorLanding.BeforeData.Statuses.Where(x => x.Kind <= StatusKind.Revealed))
            Assert.Equal(before.StateHashId, Copy.Statuses[Copy.FindStatus(before.Key)].StateHashId);
        // The edit replaces rather than appends: applied twice it gives the same files (BUG-1280's hazard).
        Assert.Equal(TeblorLanding.FileTexts(), TwiceEdited());
    }

    private static Dictionary<string, string> TwiceEdited()
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string rel, string text) in TeblorLanding.FileTexts())
        {
            JsonObject root = JsonNode.Parse(text)!.AsObject();
            TeblorLanding.EditRoot(rel, root);
            texts[rel] = root.ToJsonString();
        }
        return texts;
    }

    // ---------- criterion 1: the pins pass on the copy ----------

    [Fact]
    public void OnTheCopy_TheTechPins_AtoD_Pass_WithTheStatusDurationArm()
    {
        using IDisposable _ = ContentScope.Use(TeblorLanding.Scope());
        var t = new TechContentTests();
        t.A_EachFaction_ShipsExactlyItsFactionUpgrade_AsTheDocsSay();
        t.B_ThePagesFactionUpgradeLine_MatchesTheData();
        t.C_ThePagesTechsTable_MatchesTheData();
        t.D_Descriptions_StateEveryEffectWithItsNumber_AndTheirRequirements();
        t.I_NoShippedDescription_SaysARequirementOtherThanWithNeeds();
    }

    [Fact]
    public void OnTheCopy_TheBuildingPins_Pass_WithTheScaleColumn()
    {
        using IDisposable _ = ContentScope.Use(TeblorLanding.Scope());
        var b = new BuildingContentTests();
        b.A_EachFaction_HasTenBuildings_OnePerSlot_InSlotOrder_TownHallFirst();
        b.B_IdsAndNames_MatchTheFactionPages();
        b.C_EveryNumber_MatchesDocs02();
        b.G_ThePagesBuildingTables_MatchTheRosterAndTemplate();
        b.J_PageSight_IsBuildingDefSight_ForAll20Buildings_BothWays();
        b.L_ThePagesScaleColumn_IsBuildingDefScale_BothWays();
        AssertNone(PagePinReadinessTests.Problems(Teblor, TeblorLanding.PageLines()));
    }

    [Fact]
    public void OnTheCopy_TheAbilityRows_Descriptions_AndDocs02StatusRows_Pass()
    {
        using IDisposable _ = ContentScope.Use(TeblorLanding.Scope());
        foreach (string faction in ContentFactions.Keys)
            AssertNone(AbilityContentTests.Compare(Copy, Copy.Abilities, faction, PageTables.FactionLines(faction)).Problems);
        AssertNone(AbilityContentTests.DescriptionProblems(Copy, Copy.Abilities, Copy.Statuses));
        AssertNone(AbilityContentTests.CompareStatuses(Copy, Copy.Statuses, Copy.Abilities, StatusRows()).Problems);
        // Not vacuous: the row and its docs/02 row carry the new numbers.
        Assert.Contains("+20% damage) for 10 s, then are Slowed 20% for 4 s", string.Join("\n", TeblorLanding.PageLines()));
        Assert.Contains(StatusRows(), r => r[0] == TeblorLanding.StatusName && r[1].EndsWith("; its own attacks deal +20% damage; when it ends: Slowed 20% for 4 s", StringComparison.Ordinal));
    }

    [Fact]
    public void OnTheCopy_TheOwnerReview_TheRuleRowClaims_AndTheUnitsTable_Pass()
    {
        string[] page = TeblorLanding.PageLines();
        AssertNone(TeblorRosterPinTests.OwnerReviewProblems(Copy, page));
        AssertNone(RuleRowClaims.Problems(Copy, Teblor, page));
        AssertNone(BuildingContentTests.ScaleProblems(Copy, Teblor, page));
        UnitContentTests.PinUnitsTable(Teblor, page);
        foreach (string faction in ContentFactions.Keys.Where(k => k != Teblor))
            AssertNone(RuleRowClaims.Problems(Copy, faction, PageTables.FactionLines(faction)));
    }

    // ---------- the "lands with D31" notes, both ways ----------

    /// <summary>
    /// Until D31, "before the landing" is the shipped tree itself: the reverse copy's data hashes as the shipped data's and
    /// its page is today's page, so the "before" tests below test what ships. (After D31 this has nothing to compare.)
    /// </summary>
    [Fact]
    public void BeforeTheLanding_IsTheShippedTree_UntilTheRowsLand()
    {
        if (TestSim.Data.FindStatus(TeblorLanding.Status) >= 0) return; // landed: the shipped tree is the landing's side
        Assert.Equal(TestSim.Data.ContentHash(), TeblorLanding.BeforeData.ContentHash());
        Assert.Equal(PageTables.FactionLines(Teblor), TeblorLanding.BeforePageLines());
    }

    /// <summary>The landing's page on the data before it: every claim it no longer notes fails (the page may not run ahead of the files).</summary>
    [Fact]
    public void TheLandingsPage_OnTheDataBeforeIt_Fails()
    {
        string[] page = TeblorLanding.PageLines();
        GameData before = TeblorLanding.BeforeData;
        List<string> problems = RuleRowClaims.Problems(before, Teblor, page);
        AssertNamed(problems, "damage bonus: page '+20% damage'");
        AssertNamed(problems, "aftermath (Slowed): page 'Slowed 20% for 4 s'");
        AssertNamed(problems, "duration: page 'duration +4 s'");
        AssertNamed(problems, "Buildings scale: page '1.25× scale'");
        AssertNamed(BuildingContentTests.ScaleProblems(before, Teblor, page), "teblor.md Buildings row Hut scale: page 1.25 vs data 1");
        AssertNamed(AbilityContentTests.Compare(before, before.Abilities, Teblor, page).Problems, "'Blood-oil Frenzy' Effect:");
    }

    /// <summary>The page before the landing on the landing's data: every note still saying "lands with D31" fails, and the missing Scale column.</summary>
    [Fact]
    public void ThePageBeforeTheLanding_OnTheLandingsData_Fails()
    {
        string[] page = TeblorLanding.BeforePageLines();
        List<string> problems = RuleRowClaims.Problems(Copy, Teblor, page);
        AssertNamed(problems, "damage bonus: '+20% damage' is noted as landing later");
        AssertNamed(problems, "aftermath (Slowed): 'Slowed 20% for 4 s' is noted as landing later");
        AssertNamed(problems, "duration: 'duration +4 s' is noted as landing later");
        AssertNamed(problems, "Buildings scale: '1.25× scale' is noted as landing later");
        AssertNamed(BuildingContentTests.ScaleProblems(Copy, Teblor, page), "teblor.md Buildings row Longhouse scale: the page has no Scale column, data 1.25");
        AssertNamed(AbilityContentTests.Compare(Copy, Copy.Abilities, Teblor, page).Problems, "'Blood-oil Frenzy' Effect:");
    }

    // ---------- criterion 2: each new arm fails on a wrong copy ----------

    private static void EditRow(TestDataDir dir, string rel, string list, string id, Action<JsonObject> edit) =>
        dir.EditJson(rel, root => edit(root[list]!.AsArray().Single(n => (string)n!["id"]! == id)!.AsObject()));

    /// <summary>The Anointing's effect at 5 s against the page's "+4 s": the tech pins B and C and the claim pin name it.</summary>
    [Fact]
    public void TheAnointingAt5s_AgainstThePages4s_Fails()
    {
        GameData wrong = TeblorLanding.DataWith(dir => EditRow(dir, "factions/teblor/techs.json", "techs", "teblor_blood_oil_anointing",
            t => t["effects"]![2]!["amount"] = 5));
        Assert.Equal(5f * SimConstants.TicksPerSecond, wrong.Techs[wrong.FindTech("teblor_blood_oil_anointing")].Effects[2].Amount);
        var t = new TechContentTests();
        using (ContentScope.Use(TeblorLanding.Scope(wrong)))
        {
            Exception? b = Record.Exception(t.B_ThePagesFactionUpgradeLine_MatchesTheData);
            Assert.True(b != null && b.Message.Contains("vs data 'duration +5 s'", StringComparison.Ordinal), b?.Message ?? "B passed");
            Exception? c = Record.Exception(t.C_ThePagesTechsTable_MatchesTheData);
            Assert.True(c != null && c.Message.Contains("Blood-oil Frenzy duration +5 s", StringComparison.Ordinal), c?.Message ?? "C passed");
            Exception? d = Record.Exception(t.D_Descriptions_StateEveryEffectWithItsNumber_AndTheirRequirements);
            Assert.True(d != null && d.Message.Contains("should say '5 s longer'", StringComparison.Ordinal), d?.Message ?? "D passed");
        }
        AssertNamed(RuleRowClaims.Problems(wrong, Teblor, TeblorLanding.PageLines()), "faction upgrade duration: page 'duration +4 s' vs data [5]");
    }

    /// <summary>A Frenzy description saying 25% against <c>damageBonus</c> 0.2: the description-number rule names it.</summary>
    [Fact]
    public void AFrenzyDescriptionSaying25_AgainstDamageBonus02_Fails()
    {
        (ImmutableArray<AbilityDef> abilities, ImmutableArray<StatusDef> statuses) =
            AbilityContentTests.WithDescription(TeblorLanding.Status, "deals 20% more damage", "deals 25% more damage", Copy);
        AssertNamed(AbilityContentTests.DescriptionProblems(Copy, abilities, statuses), "teblor_frenzied description: '25%' reads as a percentage");
        // ... and an ability text stating the status's numbers is read against them too.
        (abilities, statuses) = AbilityContentTests.WithDescription("blood_oil_frenzy", "for 10 seconds.", "for 10 seconds and deal 25% more damage.", Copy);
        AssertNamed(AbilityContentTests.DescriptionProblems(Copy, abilities, statuses), "blood_oil_frenzy description: '25%' reads as a percentage");
        (abilities, statuses) = AbilityContentTests.WithDescription("blood_oil_frenzy", "for 10 seconds.", "for 10 seconds and deal 20% more damage.", Copy);
        AssertNone(AbilityContentTests.DescriptionProblems(Copy, abilities, statuses));
    }

    /// <summary>The bonus itself at 0.25 against the page's "+20% damage": the Abilities row, docs/02 and the claim pin name it.</summary>
    [Fact]
    public void ADamageBonusOf025_AgainstThePages20_Fails()
    {
        GameData wrong = TeblorLanding.DataWith(dir => EditRow(dir, "common/statuses.json", "statuses", TeblorLanding.Status, s => s["damageBonus"] = 0.25));
        string[] page = TeblorLanding.PageLines();
        AssertNamed(AbilityContentTests.Compare(wrong, wrong.Abilities, Teblor, page).Problems, "'Blood-oil Frenzy' Effect: '");
        AssertNamed(RuleRowClaims.Problems(wrong, Teblor, page), "Abilities damage bonus: page '+20% damage' vs data [25]");
        // Not the description rule: its "20% more damage" equals the aftermath's 20% of the same row, which a role-based
        // reading accepts; the page's claims above are what hold the bonus.
        using (ContentScope.Use(TeblorLanding.Scope(wrong)))
            AssertNamed(AbilityContentTests.CompareStatuses(wrong, wrong.Statuses, wrong.Abilities, StatusRows()).Problems, "status 'teblor_frenzied' Effect:");
    }

    /// <summary>An <c>onEnd</c> of 5 s against "for 4 s": the Abilities row's onEnd arm, the claim pin and the descriptions name it.</summary>
    [Fact]
    public void AnOnEndOf5s_AgainstFor4s_Fails()
    {
        GameData wrong = TeblorLanding.DataWith(dir => EditRow(dir, "common/statuses.json", "statuses", TeblorLanding.Status, s => s["onEnd"]!["duration"] = 5));
        Assert.Equal(5 * SimConstants.TicksPerSecond, wrong.Statuses[wrong.FindStatus(TeblorLanding.Status)].OnEndTicks);
        string[] page = TeblorLanding.PageLines();
        AssertNamed(AbilityContentTests.Compare(wrong, wrong.Abilities, Teblor, page).Problems, "'Blood-oil Frenzy' Effect onEnd: '");
        AssertNamed(RuleRowClaims.Problems(wrong, Teblor, page), "Abilities aftermath (Slowed): page 'Slowed 20% for 4 s' vs data [20% for 5 s]");
        AssertNamed(AbilityContentTests.DescriptionProblems(wrong, wrong.Abilities, wrong.Statuses), "teblor_frenzied description: '4 seconds' reads as a duration");
        using (ContentScope.Use(TeblorLanding.Scope(wrong)))
            AssertNamed(AbilityContentTests.CompareStatuses(wrong, wrong.Statuses, wrong.Abilities, StatusRows()).Problems, "Slowed 20% for 5 s");
    }

    /// <summary>A Teblor building at 1.5 against the page's 1.25: the Scale column pin and the prose claim name it.</summary>
    [Fact]
    public void ABuildingAt15_AgainstThePages125_Fails()
    {
        GameData wrong = TeblorLanding.DataWith(dir => EditRow(dir, "factions/teblor/buildings.json", "buildings", "teblor_hut", b => b["scale"] = 1.5));
        string[] page = TeblorLanding.PageLines();
        Assert.Equal(new[] { "teblor.md Buildings row Hut scale: page 1.25 vs data 1.5" }, BuildingContentTests.ScaleProblems(wrong, Teblor, page));
        AssertNamed(RuleRowClaims.Problems(wrong, Teblor, page), "Buildings scale: data 1.5, which the page does not state");
        // The page side: one Scale cell changed by hand on the landing's page, the data right.
        string[] edited = page.Select(l => l.StartsWith("| House | Hut |", StringComparison.Ordinal) ? l[..^" 1.25 |".Length] + " 1.5 |" : l).ToArray();
        Assert.Equal(new[] { "teblor.md Buildings row Hut scale: page 1.5 vs data 1.25" }, BuildingContentTests.ScaleProblems(Copy, Teblor, edited));
    }
}
