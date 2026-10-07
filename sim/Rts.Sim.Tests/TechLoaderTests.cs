using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>M3-5 criteria 1, 2 and 10: the shipped techs match docs/02 and the faction pages; each loader rule is one error at its field.</summary>
public class TechLoaderTests
{
    private const string Common = "common/techs.json";
    private const string MalazanTechs = "factions/malazan/techs.json";
    private const string MalazanBuildings = "factions/malazan/buildings.json";
    private const string MalazanUnits = "factions/malazan/units.json";

    private static GameData D => TestSim.Data;

    private static TechDef Tech(string key)
    {
        int id = D.FindTech(key);
        Assert.True(id >= 0, key);
        return D.Techs[id];
    }

    // ---------- criterion 1: shipped data ----------

    [Fact]
    public void ShippedData_HoldsNineTechs_SevenCommonAndOnePerFaction_InOrdinalIdOrder()
    {
        Assert.Equal(9, D.Techs.Length);
        Assert.Equal(7, D.Techs.Count(t => t.Faction == -1));
        Assert.Equal(D.FindFaction("malazan"), Tech("moranth_supply").Faction);
        Assert.Equal(D.FindFaction("whirlwind"), Tech("dryjhnas_prophecy").Faction);
        for (int i = 0; i < D.Techs.Length; i++)
        {
            Assert.Equal(i, D.Techs[i].Id);
            if (i > 0) Assert.True(string.CompareOrdinal(D.Techs[i - 1].Key, D.Techs[i].Key) < 0);
            Assert.False(string.IsNullOrWhiteSpace(D.Techs[i].DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(D.Techs[i].Description));
        }
        Assert.Equal(-1, D.FindTech("age_iii"));
    }

    [Fact]
    public void AgeII_IsResearchedAtTheTownHall_For400Gold200Wood_In1200Ticks_AndIsTheAgeTech()
    {
        TechDef t = Tech("age_ii");
        Assert.Equal((BuildingSlot.TownHall, 400, 200, 1200), (t.ResearchedAtSlot, t.CostGold, t.CostWood, t.ResearchTicks));
        Assert.Empty(t.Requires); // the "any two of" rule is M3-6
        Assert.Empty(t.Effects);
        Assert.Equal(new[] { t.Id }, D.AgeTechs.ToArray());
    }

    /// <summary>docs/02 "Forge upgrades", row by row: level 2 adds the second point (+1 / +2 are the totals) and needs level 1 and Age II.</summary>
    [Theory]
    [InlineData("melee_weapons_1", 100, 50, 30, "attack", "melee", -1, "")]
    [InlineData("melee_weapons_2", 175, 100, 40, "attack", "melee", -1, "melee_weapons_1")]
    [InlineData("ranged_weapons_1", 100, 50, 30, "attack", "pierce", -1, "")]
    [InlineData("ranged_weapons_2", 175, 100, 40, "attack", "pierce", -1, "ranged_weapons_1")]
    [InlineData("armor_1", 125, 50, 35, "armor", null, 0, "")]
    [InlineData("armor_2", 200, 125, 45, "armor", null, 0, "armor_1")]
    public void ForgeUpgrades_MatchDocs02(string key, int gold, int wood, int seconds, string stat, string? attackType, int siege, string level1)
    {
        TechDef t = Tech(key);
        Assert.Equal(-1, t.Faction);
        Assert.Equal((BuildingSlot.Forge, gold, wood, seconds * 20), (t.ResearchedAtSlot, t.CostGold, t.CostWood, t.ResearchTicks));
        Assert.Equal(level1.Length == 0 ? Array.Empty<string>() : new[] { level1, "age_ii" }, t.Requires.ToArray());
        TechEffect e = Assert.Single(t.Effects);
        Assert.Equal(DataLimits.TechStatIds.IndexOf(stat), (int)e.Stat);
        Assert.Equal(1f, e.Amount);
        Assert.Equal(attackType == null ? -1 : D.DamageTable.DamageTypeKeys.IndexOf(attackType), e.AttackType);
        Assert.Equal(siege, e.Siege);
        Assert.Empty(e.Units);
        Assert.Empty(e.Tags);
    }

    [Fact]
    public void MoranthSupply_MatchesTheMalazanPage()
    {
        TechDef t = Tech("moranth_supply");
        Assert.Equal((BuildingSlot.Forge, 200, 150, 900), (t.ResearchedAtSlot, t.CostGold, t.CostWood, t.ResearchTicks));
        Assert.Equal(new[] { "age_ii" }, t.Requires.ToArray());
        Assert.Equal(2, t.Effects.Length);
        Assert.Equal((TechStat.AbilityCooldown, -300f), (t.Effects[0].Stat, t.Effects[0].Amount)); // -15 s: Cusser 45 -> 30 s
        Assert.Equal(new[] { D.FindUnit("malazan_sapper") }, t.Effects[0].Units.ToArray());
        Assert.Equal((TechStat.Range, 4f), (t.Effects[1].Stat, t.Effects[1].Amount));
        Assert.Equal(new[] { D.FindUnit("malazan_catapult") }, t.Effects[1].Units.ToArray());
        Assert.All(t.Effects, e => Assert.Equal((-1, -1), (e.AttackType, e.Siege)));
    }

    [Fact]
    public void DryjhnasProphecy_MatchesTheWhirlwindPage()
    {
        TechDef t = Tech("dryjhnas_prophecy");
        Assert.Equal((BuildingSlot.Forge, 200, 150, 900), (t.ResearchedAtSlot, t.CostGold, t.CostWood, t.ResearchTicks));
        Assert.Equal(new[] { "age_ii" }, t.Requires.ToArray());
        Assert.Equal(2, t.Effects.Length);
        Assert.Equal((TechStat.Hp, 20f), (t.Effects[0].Stat, t.Effects[0].Amount));
        Assert.Equal(new[] { D.FindUnit("whirlwind_zealot") }, t.Effects[0].Units.ToArray());
        Assert.Equal((TechStat.AbilityCooldown, -300f), (t.Effects[1].Stat, t.Effects[1].Amount)); // Sandstorm 45 -> 30 s
        Assert.Equal(new[] { D.FindUnit("whirlwind_priest") }, t.Effects[1].Units.ToArray());
    }

    [Fact]
    public void TechsResearchableAt_TheTownHallAgeII_TheForgeItsUpgrades_OthersNothing()
    {
        int[] forgeCommon = new[] { "armor_1", "armor_2", "melee_weapons_1", "melee_weapons_2", "ranged_weapons_1", "ranged_weapons_2" }.Select(D.FindTech).ToArray();
        Assert.Equal(new[] { D.FindTech("age_ii") }, D.TechsResearchableAt(D.FindBuilding("malazan_garrison_keep")).ToArray());
        Assert.Equal(new[] { D.FindTech("age_ii") }, D.TechsResearchableAt(D.FindBuilding("whirlwind_holy_camp")).ToArray());
        Assert.Equal(forgeCommon.Append(D.FindTech("moranth_supply")).Order().ToArray(), D.TechsResearchableAt(D.FindBuilding("malazan_armory")).ToArray());
        Assert.Equal(forgeCommon.Append(D.FindTech("dryjhnas_prophecy")).Order().ToArray(), D.TechsResearchableAt(D.FindBuilding("whirlwind_smithy")).ToArray());
        int listed = 0;
        for (int b = 0; b < D.Buildings.Length; b++) listed += D.TechsResearchableAt(b).Length;
        Assert.Equal(2 * 1 + 2 * 7, listed);
        Assert.True(D.TechsResearchableAt(D.FindBuilding("malazan_barracks")).IsEmpty);
        Assert.True(D.TechsResearchableAt(-1).IsEmpty);
        Assert.True(D.TechsResearchableAt(D.Buildings.Length).IsEmpty);
    }

    [Fact]
    public void ShippedRequires_NameExistingTechsOrBuildings_AndTheUniquesRequireAnExistingTech()
    {
        // The buildings' requires are faction content (D3 fills them); whatever they hold must name a tech or a building.
        foreach (BuildingDef b in D.Buildings)
            foreach (string r in b.Requires) Assert.True(D.FindTech(r) >= 0 || D.FindBuilding(r) >= 0, $"{b.Key} requires {r}");
        foreach (UnitDef u in D.Units)
            foreach (string r in u.Requires) Assert.True(D.FindTech(r) >= 0 || D.FindBuilding(r) >= 0, $"{u.Key} requires {r}");
        Assert.Equal(new[] { "age_ii" }, D.Units[D.FindUnit("malazan_sapper")].Requires.ToArray());
    }

    // ---------- criterion 2: one error each, at the field ----------

    private static DataError OneError(TestDataDir dir)
    {
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.Null(r.Data);
        return Assert.Single(r.Errors);
    }

    private static void EditTech(TestDataDir dir, string file, int index, Action<JsonObject> edit) =>
        dir.EditJson(file, root => edit(root["techs"]![index]!.AsObject()));

    [Theory]
    [InlineData("stat", "\"speed\"", "techs[1].effects[0].stat", "unknown stat 'speed'")]
    [InlineData("stat", "\"Attack\"", "techs[1].effects[0].stat", "unknown stat 'Attack'")]
    [InlineData("amount", "1.5", "techs[1].effects[0].amount", "not a whole number")]
    [InlineData("amount", "0", "techs[1].effects[0].amount", "non-zero")]
    [InlineData("amount", null, "techs[1].effects[0].amount", "missing")]
    [InlineData("appliesTo", null, "techs[1].effects[0].appliesTo", "missing")]
    [InlineData("appliesTo", "{\"attackType\": \"fire\"}", "techs[1].effects[0].appliesTo.attackType", "unknown damage type 'fire'")]
    [InlineData("appliesTo", "{\"tags\": [\"undead\"]}", "techs[1].effects[0].appliesTo.tags[0]", "unknown tag 'undead'")]
    [InlineData("appliesTo", "{\"units\": [\"malazan_sappers\"]}", "techs[1].effects[0].appliesTo.units[0]", "unknown unit 'malazan_sappers'")]
    [InlineData("appliesTo", "{\"siege\": true, \"colour\": 1}", "", "")] // unknown member: a JSON error
    public void ABrokenEffectField_IsOneErrorAtTheField(string field, string? raw, string path, string message)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        EditTech(dir, Common, 1, t =>
        {
            JsonObject e = t["effects"]![0]!.AsObject();
            if (raw == null) e.Remove(field);
            else e[field] = JsonNode.Parse(raw);
        });
        DataError err = OneError(dir);
        Assert.Equal(Common, err.File);
        if (path.Length > 0) Assert.Equal(path, err.Path);
        Assert.Contains(message, err.Message);
    }

    [Fact]
    public void AFactionUpgradeNamingAnotherFactionsUnit_IsOneError()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        EditTech(dir, MalazanTechs, 0, t => t["effects"]![0]!["appliesTo"] = JsonNode.Parse("{\"units\": [\"whirlwind_zealot\"]}"));
        DataError e = OneError(dir);
        Assert.Equal((MalazanTechs, "techs[0].effects[0].appliesTo.units[0]"), (e.File, e.Path));
        Assert.Contains("belongs to faction 'whirlwind', not 'malazan'", e.Message);
    }

    [Theory]
    [InlineData("\"smithy\"", "unknown slot 'smithy'")]
    [InlineData("\"Forge\"", "unknown slot 'Forge'")]
    public void AnUnknownResearchedAtSlot_IsOneErrorAtTheField(string raw, string message)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        EditTech(dir, Common, 1, t => t["researchedAt"] = JsonNode.Parse(raw));
        DataError e = OneError(dir);
        Assert.Equal((Common, "techs[1].researchedAt"), (e.File, e.Path));
        Assert.Contains(message, e.Message);
    }

    [Fact]
    public void ASlotTheFactionLacks_IsReportedAtTheBuildingsFile_NotAtEachTech()
    {
        // The Armory turned into a second House: since M3-6 (BUG-0010) that is two errors in the buildings file (a
        // doubled house, no forge), and the per-tech researchedAt check (M3-5: one error per tech researched at the
        // forge) is skipped, like every check into a broken buildings file.
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(MalazanBuildings, root => root["buildings"]!.AsArray().Single(b => (string)b!["id"]! == "malazan_armory")!["slot"] = "house");
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.Null(r.Data);
        Assert.Equal(2, r.Errors.Count);
        Assert.All(r.Errors, e => Assert.Equal(MalazanBuildings, e.File));
        Assert.Contains(r.Errors, e => e.Path == "buildings[6].slot" && e.Message.Contains("slot 'house' is already filled by 'malazan_billet'"));
        Assert.Contains(r.Errors, e => e.Path == "buildings" && e.Message.Contains("no building in slot 'forge'"));
    }

    [Fact]
    public void ADuplicateTechIdAcrossFiles_IsOneErrorAtTheSecondDefinition()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        EditTech(dir, MalazanTechs, 0, t => t["id"] = "armor_1");
        DataError e = OneError(dir);
        Assert.Equal((MalazanTechs, "techs[0].id"), (e.File, e.Path));
        Assert.Contains("duplicate tech id 'armor_1' (first defined in common/techs.json)", e.Message);
    }

    [Fact]
    public void ADuplicateTechIdInOneFile_IsOneError()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        EditTech(dir, Common, 2, t => t["id"] = "melee_weapons_1");
        DataError e = OneError(dir);
        Assert.Equal((Common, "techs[2].id"), (e.File, e.Path));
    }

    [Fact]
    public void ATechRequiresNamingNothing_IsOneErrorAtTheEntry()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        EditTech(dir, Common, 2, t => t["requires"] = JsonNode.Parse("[\"age_ii\", \"melee_weapon_1\"]"));
        DataError e = OneError(dir);
        Assert.Equal((Common, "techs[2].requires[1]"), (e.File, e.Path));
        Assert.Contains("unknown tech or building 'melee_weapon_1'", e.Message);
    }

    [Fact]
    public void ABuildingRequiresNamingNothing_IsOneErrorAtTheEntry()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(MalazanBuildings, root => root["buildings"]![5]!["requires"] = JsonNode.Parse("[\"malazan_barracks\", \"no_such_hall\"]"));
        DataError e = OneError(dir);
        Assert.Equal((MalazanBuildings, "buildings[5].requires[1]"), (e.File, e.Path));
        Assert.Contains("unknown tech or building 'no_such_hall'", e.Message);
    }

    [Fact]
    public void AUnitRequiresNamingNothing_IsOneErrorAtTheEntry()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", "malazan_crossbowman", "requires", "[\"age_iii\"]");
        DataError e = OneError(dir);
        Assert.Equal((MalazanUnits, "units[2].requires[0]"), (e.File, e.Path));
        Assert.Contains("unknown tech or building 'age_iii'", e.Message);
    }

    [Theory]
    [InlineData(Common)]
    [InlineData(MalazanTechs)]
    [InlineData("factions/whirlwind/techs.json")]
    public void AMissingTechsFile_IsOneError(string rel)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        File.Delete(dir.FullPath(rel));
        DataError e = OneError(dir);
        Assert.Equal(rel, e.File);
        Assert.Contains("missing", e.Message);
    }

    [Fact]
    public void AnUnknownTechField_IsReportedNotIgnored()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        EditTech(dir, Common, 0, t => t["researchTme"] = 60);
        DataError e = OneError(dir);
        Assert.Equal(Common, e.File);
        Assert.Contains("researchTme", e.Path + e.Message);
    }

    [Fact]
    public void TheAgeTechMissing_IsOneError()
    {
        // age_ii renamed and every reference to it moved along: only the age rule (DataLimits.AgeTechIds) is left broken.
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        foreach (string file in new[] { Common, MalazanTechs, "factions/whirlwind/techs.json", MalazanUnits, "factions/whirlwind/units.json",
            MalazanBuildings, "factions/whirlwind/buildings.json" })
            File.WriteAllText(dir.FullPath(file), File.ReadAllText(dir.FullPath(file)).Replace("\"age_ii\"", "\"age_two\""));
        DataError e = OneError(dir);
        Assert.Equal((Common, "techs"), (e.File, e.Path));
        Assert.Contains("no common tech 'age_ii'", e.Message);
    }

    // ---------- criterion 10: buildings accept requires ----------

    [Fact]
    public void ABuildingRequiresNamingATechAndABuilding_Loads_AndIsKeptInOrder()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(MalazanBuildings, root => root["buildings"]![7]!["requires"] = JsonNode.Parse("[\"age_ii\", \"malazan_barracks\"]"));
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Assert.Equal(new[] { "age_ii", "malazan_barracks" }, r.Data!.Buildings[r.Data.FindBuilding("malazan_cadre_tower")].Requires.ToArray());
        Assert.NotEqual(D.ContentHash(), r.Data.ContentHash());
    }

    [Fact]
    public void ABuildingWithoutRequires_LoadsAsEmpty()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(MalazanBuildings, root => root["buildings"]![7]!.AsObject().Remove("requires"));
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Assert.Empty(r.Data!.Buildings[r.Data.FindBuilding("malazan_cadre_tower")].Requires);
    }

    [Fact]
    public void ABuildingRequiresEntryNotAnId_IsOneError()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(MalazanBuildings, root => root["buildings"]![7]!["requires"] = JsonNode.Parse("[\"Age II\"]"));
        DataError e = OneError(dir);
        Assert.Equal((MalazanBuildings, "buildings[7].requires[0]"), (e.File, e.Path));
        Assert.Contains("not snake_case", e.Message);
    }

    [Fact]
    public void TheRequiresCheck_IsSkipped_WhenTheTechsFileIsBroken()
    {
        // The units naming age_ii must not cascade into errors when common/techs.json can't be read.
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        File.WriteAllText(dir.FullPath(Common), "{ \"techs\": [ ");
        DataError e = OneError(dir);
        Assert.Equal(Common, e.File);
        Assert.Contains("malformed JSON", e.Message);
    }
}
