using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>
/// M3-6 criteria 1-2: <c>requires</c> resolves to tech and building type ids at load, Age II's <c>requiresAnyOf</c> ships
/// as any two of the four halls, and each new loader rule (any-of shape, cycles, overlapping ids, empty filters, duplicate
/// JSON keys, template slots) is one error at its field.
/// </summary>
public class RequirementLoaderTests
{
    private const string Common = "common/techs.json";
    private const string MalazanTechs = "factions/malazan/techs.json";
    private const string MalazanBuildings = "factions/malazan/buildings.json";
    private const string MalazanUnits = "factions/malazan/units.json";

    private static GameData D => TestSim.Data;

    private static DataError OneError(TestDataDir dir)
    {
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Errors.Count == 1, string.Join("\n", r.Errors));
        Assert.Null(r.Data);
        return r.Errors[0];
    }

    private static GameData Loads(TestDataDir dir)
    {
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        return r.Data!;
    }

    private static void EditTech(TestDataDir dir, string file, int index, Action<JsonObject> edit) =>
        dir.EditJson(file, root => edit(root["techs"]![index]!.AsObject()));

    private static void EditBuilding(TestDataDir dir, string file, string id, Action<JsonObject> edit) =>
        dir.EditJson(file, root => edit(root["buildings"]!.AsArray().Single(b => (string)b!["id"]! == id)!.AsObject()));

    private static int[] Ids(params string[] techs) => techs.Select(D.FindTech).Order().ToArray();

    // ---------- criterion 1: shipped data ----------

    [Fact]
    public void ShippedData_LoadsClean_AndAgeIIRequiresAnyTwoOfTheFourHalls()
    {
        DataLoadResult r = DataLoader.LoadAll(TestDataDir.Shipped);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        TechDef age = D.Techs[D.FindTech("age_ii")];
        Assert.Equal(2, age.RequiresAnyOfCount);
        Assert.Equal(new[] { "infantry_hall", "ranged_hall", "shock_hall", "forge" }, age.RequiresAnyOf.ToArray());
        Assert.Equal(new[] { BuildingSlot.InfantryHall, BuildingSlot.RangedHall, BuildingSlot.ShockHall, BuildingSlot.Forge }.Select(s => (int)s).ToArray(),
            age.RequiresAnyOfSlots.ToArray());
        Assert.Empty(age.RequiresTechs);
        Assert.Empty(age.RequiresBuildings);
        // Only Age II carries an any-of rule.
        Assert.All(D.Techs.Where(t => t.Key != "age_ii"), t => Assert.Equal((0, 0, 0), (t.RequiresAnyOfCount, t.RequiresAnyOf.Length, t.RequiresAnyOfSlots.Length)));
    }

    [Theory]
    [InlineData("malazan_sapper")]
    [InlineData("whirlwind_zealot")]
    public void TheUniques_ResolveToAgeII(string unit)
    {
        UnitDef u = D.Units[D.FindUnit(unit)];
        Assert.Equal(Ids("age_ii"), u.RequiresTechs.ToArray());
        Assert.Empty(u.RequiresBuildings);
    }

    [Theory]
    [InlineData("melee_weapons_2", "melee_weapons_1")]
    [InlineData("ranged_weapons_2", "ranged_weapons_1")]
    [InlineData("armor_2", "armor_1")]
    public void LevelTwoUpgrades_ResolveToLevelOneAndAgeII_Ascending(string tech, string level1)
    {
        TechDef t = D.Techs[D.FindTech(tech)];
        Assert.Equal(Ids(level1, "age_ii"), t.RequiresTechs.ToArray());
        Assert.Empty(t.RequiresBuildings);
    }

    [Theory]
    [InlineData("moranth_supply")]
    [InlineData("dryjhnas_prophecy")]
    public void FactionUpgrades_ResolveToAgeII(string tech) =>
        Assert.Equal(Ids("age_ii"), D.Techs[D.FindTech(tech)].RequiresTechs.ToArray());

    [Fact]
    public void EveryResolvedRequirement_MatchesItsStrings()
    {
        void Check(string what, IEnumerable<string> strings, IEnumerable<int> techs, IEnumerable<int> buildings)
        {
            string[] expected = strings.Distinct().Order(StringComparer.Ordinal).ToArray();
            string[] got = techs.Select(t => D.Techs[t].Key).Concat(buildings.Select(b => D.Buildings[b].Key)).Order(StringComparer.Ordinal).ToArray();
            Assert.True(expected.SequenceEqual(got), $"{what}: {string.Join(",", expected)} vs {string.Join(",", got)}");
        }
        foreach (UnitDef u in D.Units) Check(u.Key, u.Requires, u.RequiresTechs, u.RequiresBuildings);
        foreach (BuildingDef b in D.Buildings) Check(b.Key, b.Requires, b.RequiresTechs, b.RequiresBuildings);
        foreach (TechDef t in D.Techs) Check(t.Key, t.Requires, t.RequiresTechs, t.RequiresBuildings);
    }

    [Fact]
    public void ARequiresNamingABuilding_ResolvesToItsTypeId_AndTechsStaySeparate()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "requires", "[\"malazan_crossbow_range\", \"armor_1\", \"malazan_armory\"]");
        EditBuilding(dir, MalazanBuildings, "malazan_cadre_tower", b => b["requires"] = JsonNode.Parse("[\"age_ii\", \"malazan_barracks\"]"));
        GameData d = Loads(dir);
        UnitDef u = d.Units[d.FindUnit("malazan_heavy_infantry")];
        Assert.Equal(new[] { d.FindTech("armor_1") }, u.RequiresTechs.ToArray());
        Assert.Equal(new[] { d.FindBuilding("malazan_armory"), d.FindBuilding("malazan_crossbow_range") }.Order().ToArray(), u.RequiresBuildings.ToArray());
        BuildingDef b = d.Buildings[d.FindBuilding("malazan_cadre_tower")];
        Assert.Equal((d.FindTech("age_ii"), d.FindBuilding("malazan_barracks")), (b.RequiresTechs.Single(), b.RequiresBuildings.Single()));
        Assert.NotEqual(D.ContentHash(), d.ContentHash());
    }

    [Fact]
    public void AFactionTechsAnyOf_MayNameItsOwnBuildings_WhichCountAsTheirSlots()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        EditTech(dir, MalazanTechs, 0, t => t["requiresAnyOf"] = JsonNode.Parse("{\"count\": 1, \"of\": [\"malazan_barracks\", \"siege_works\"]}"));
        GameData d = Loads(dir);
        TechDef t = d.Techs[d.FindTech("moranth_supply")];
        Assert.Equal(new[] { (int)BuildingSlot.InfantryHall, (int)BuildingSlot.SiegeWorks }, t.RequiresAnyOfSlots.ToArray());
        Assert.Equal(new[] { "malazan_barracks", "siege_works" }, t.RequiresAnyOf.ToArray());
    }

    // ---------- criterion 2: requiresAnyOf, one error at the field ----------

    [Theory]
    [InlineData("{\"count\": 0, \"of\": [\"forge\"]}", "techs[0].requiresAnyOf.count", "outside 1-1")]
    [InlineData("{\"count\": 5, \"of\": [\"infantry_hall\", \"ranged_hall\", \"shock_hall\", \"forge\"]}", "techs[0].requiresAnyOf.count", "outside 1-4")]
    [InlineData("{\"count\": -1, \"of\": [\"forge\"]}", "techs[0].requiresAnyOf.count", "outside")]
    [InlineData("{\"of\": [\"forge\"]}", "techs[0].requiresAnyOf.count", "missing")]
    [InlineData("{\"count\": 1}", "techs[0].requiresAnyOf.of", "missing")]
    [InlineData("{\"count\": 1, \"of\": []}", "techs[0].requiresAnyOf.count", "outside 1-0")]
    [InlineData("{\"count\": 1, \"of\": [\"barracks\"]}", "techs[0].requiresAnyOf.of[0]", "not a building slot id")]
    [InlineData("{\"count\": 1, \"of\": [\"malazan_barracks\"]}", "techs[0].requiresAnyOf.of[0]", "a common tech names slots")]
    [InlineData("{\"count\": 1, \"of\": [\"forge\", \"Forge\"]}", "techs[0].requiresAnyOf.of[1]", "not snake_case")]
    [InlineData("{\"count\": 1, \"of\": [\"forge\", \"forge\"]}", "techs[0].requiresAnyOf.of[1]", "again")]
    [InlineData("{\"count\": 1, \"of\": [\"forge\"], \"min\": 2}", "", "")] // unknown member: a JSON error
    public void ABrokenAnyOfOnACommonTech_IsOneErrorAtTheField(string raw, string path, string message)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        EditTech(dir, Common, 0, t => t["requiresAnyOf"] = JsonNode.Parse(raw));
        DataError e = OneError(dir);
        Assert.Equal(Common, e.File);
        if (path.Length > 0) Assert.Equal(path, e.Path);
        Assert.Contains(message, e.Message);
    }

    [Theory]
    [InlineData("whirlwind_raider_camp", "neither a building slot id nor a building of faction 'malazan'")]
    [InlineData("malazan_sapper", "neither a building slot id")]
    public void AFactionTechsAnyOfNamingAnotherFactionsBuildingOrNoBuilding_IsOneError(string id, string message)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        EditTech(dir, MalazanTechs, 0, t => t["requiresAnyOf"] = JsonNode.Parse($"{{\"count\": 1, \"of\": [\"forge\", \"{id}\"]}}"));
        DataError e = OneError(dir);
        Assert.Equal((MalazanTechs, "techs[0].requiresAnyOf.of[1]"), (e.File, e.Path));
        Assert.Contains(message, e.Message);
    }

    [Fact]
    public void AFactionTechsAnyOfNamingABuildingAndItsSlot_IsOneError()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        EditTech(dir, MalazanTechs, 0, t => t["requiresAnyOf"] = JsonNode.Parse("{\"count\": 1, \"of\": [\"forge\", \"malazan_armory\"]}"));
        DataError e = OneError(dir);
        Assert.Equal((MalazanTechs, "techs[0].requiresAnyOf.of[1]"), (e.File, e.Path));
        Assert.Contains("names slot 'forge' again", e.Message);
    }

    // ---------- criterion 2: requires cycles and overlapping ids (BUG-0099 items 1 and 3) ----------

    [Fact]
    public void ATwoTechCycle_IsOneErrorAtTheEntryThatClosesIt()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        // melee_weapons_2 (techs[2]) requires melee_weapons_1 (techs[1]); now 1 requires 2 as well.
        EditTech(dir, Common, 1, t => t["requires"] = JsonNode.Parse("[\"melee_weapons_2\"]"));
        DataError e = OneError(dir);
        Assert.Equal(Common, e.File);
        Assert.Equal("techs[2].requires[0]", e.Path); // DFS from melee_weapons_1 (the lower id) closes at 2's entry
        Assert.Contains("requires cycle", e.Message);
    }

    [Fact]
    public void ATechRequiringItself_IsOneError()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        EditTech(dir, Common, 3, t => t["requires"] = JsonNode.Parse("[\"ranged_weapons_1\"]"));
        DataError e = OneError(dir);
        Assert.Equal((Common, "techs[3].requires[0]"), (e.File, e.Path));
        Assert.Contains("'ranged_weapons_1' requires itself", e.Message);
    }

    [Fact]
    public void ABuildingRequiringItself_IsOneError()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        EditBuilding(dir, MalazanBuildings, "malazan_cadre_tower", b => b["requires"] = JsonNode.Parse("[\"malazan_cadre_tower\"]"));
        DataError e = OneError(dir);
        Assert.Equal(MalazanBuildings, e.File);
        Assert.EndsWith(".requires[0]", e.Path);
        Assert.Contains("requires itself", e.Message);
    }

    [Fact]
    public void ACycleThroughATechAndABuilding_IsOneError()
    {
        // The Cadre Tower needs Armor; Armor needs the Cadre Tower.
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        EditBuilding(dir, MalazanBuildings, "malazan_cadre_tower", b => b["requires"] = JsonNode.Parse("[\"armor_1\"]"));
        EditTech(dir, Common, 1 + 4, t => t["requires"] = JsonNode.Parse("[\"malazan_cadre_tower\"]")); // techs[5] = armor_1
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.Null(r.Data);
        DataError e = Assert.Single(r.Errors);
        Assert.Contains("requires cycle", e.Message);
    }

    [Fact]
    public void AThreeTechCycle_IsOneError_AndAChainWithoutACycleLoads()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        // A chain: ranged_weapons_1 -> armor_1 -> age_ii (no cycle).
        dir.EditJson(Common, root =>
        {
            JsonArray techs = root["techs"]!.AsArray();
            JsonObject ByKey(string k) => techs.Single(t => (string)t!["id"]! == k)!.AsObject();
            ByKey("ranged_weapons_1")["requires"] = JsonNode.Parse("[\"armor_1\"]");
            ByKey("armor_1")["requires"] = JsonNode.Parse("[\"age_ii\"]");
        });
        GameData d = Loads(dir);
        Assert.Equal(new[] { d.FindTech("armor_1") }, d.Techs[d.FindTech("ranged_weapons_1")].RequiresTechs.ToArray());
        // Close it: age_ii -> ranged_weapons_1.
        EditTech(dir, Common, 0, t => t["requires"] = JsonNode.Parse("[\"ranged_weapons_1\"]"));
        DataError e = OneError(dir);
        Assert.Contains("requires cycle", e.Message);
    }

    [Fact]
    public void ATechIdEqualToABuildingId_IsOneErrorAtTheTechsId()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        EditTech(dir, MalazanTechs, 0, t => t["id"] = "malazan_armory");
        DataError e = OneError(dir);
        Assert.Equal((MalazanTechs, "techs[0].id"), (e.File, e.Path));
        Assert.Contains("also a building id", e.Message);
    }

    // ---------- criterion 2: empty filters (BUG-0098) ----------

    [Theory]
    [InlineData("units")]
    [InlineData("tags")]
    public void AnEmptyUnitsOrTagsFilter_IsOneErrorAtTheList(string filter)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        EditTech(dir, Common, 1, t => t["effects"]![0]!["appliesTo"] = JsonNode.Parse($"{{\"{filter}\": []}}"));
        DataError e = OneError(dir);
        Assert.Equal((Common, $"techs[1].effects[0].appliesTo.{filter}"), (e.File, e.Path));
        Assert.Contains("matches no unit", e.Message);
    }

    // ---------- criterion 2: duplicate JSON keys (BUG-0008), every file kind ----------

    [Theory]
    [InlineData("common/damage_table.json")]
    [InlineData("common/rules.json")]
    [InlineData("common/resources.json")]
    [InlineData("common/techs.json")]
    [InlineData("factions/malazan/faction.json")]
    [InlineData("factions/malazan/units.json")]
    [InlineData("factions/whirlwind/buildings.json")]
    [InlineData("factions/whirlwind/techs.json")]
    public void ADuplicateKey_IsOneErrorAtItsPath_InEveryFileKind(string rel)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        string text = File.ReadAllText(dir.FullPath(rel));
        // The first "key": scalar pair, written twice with the same value, so only the duplicate itself is wrong.
        Match m = Regex.Match(text, "\"([A-Za-z_]+)\"\\s*:\\s*(\"[^\"]*\"|-?[0-9.]+|true|false)\\s*,");
        Assert.True(m.Success);
        File.WriteAllText(dir.FullPath(rel), text[..m.Index] + m.Value + " " + m.Value + text[(m.Index + m.Length)..]);
        DataError e = OneError(dir);
        Assert.Equal(rel, e.File);
        Assert.True(e.Path == m.Groups[1].Value || e.Path.EndsWith("." + m.Groups[1].Value), e.Path);
        Assert.Contains($"duplicate key '{m.Groups[1].Value}'", e.Message);
    }

    [Fact]
    public void ADuplicateKeyDeepInAUnit_NamesTheFullPath()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        string text = File.ReadAllText(dir.FullPath(MalazanUnits));
        int unit = text.IndexOf("\"malazan_crossbowman\"", StringComparison.Ordinal);
        int bonus = text.IndexOf("\"heavy\": 1.3", unit, StringComparison.Ordinal);
        Assert.True(unit >= 0 && bonus > unit);
        File.WriteAllText(dir.FullPath(MalazanUnits), text[..bonus] + "\"heavy\": 1.3, " + text[bonus..]);
        DataError e = OneError(dir);
        Assert.Matches(@"^units\[\d+\]\.attack\.bonusVs\.heavy$", e.Path);
    }

    [Fact]
    public void KeysEqualInDifferentObjects_AreNotDuplicates_AndMalformedJsonIsOneError()
    {
        DataLoadResult shipped = DataLoader.LoadAll(TestDataDir.Shipped); // every unit repeats "id", "hp" ...: no error
        Assert.True(shipped.Ok, string.Join("\n", shipped.Errors));
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        // A duplicate before a syntax error: only the malformed-JSON error is reported.
        File.WriteAllText(dir.FullPath("common/rules.json"), "{ \"popCap\": 100, \"popCap\": 100, ");
        DataError e = OneError(dir);
        Assert.Contains("malformed JSON", e.Message);
    }

    // ---------- criterion 2: template slots (BUG-0010) ----------

    [Fact]
    public void AFactionWithNoUnits_HasOneErrorInItsUnitsFile()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        File.WriteAllText(dir.FullPath("factions/whirlwind/units.json"), "{\"units\": []}");
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.Null(r.Data);
        DataError e = Assert.Single(r.Errors, x => x.File == "factions/whirlwind/units.json");
        Assert.Equal("units", e.Path);
        Assert.Contains("no units", e.Message);
        // The rest are the faction's techs naming its (now missing) units: reported at those references.
        Assert.All(r.Errors.Where(x => x != e), x => Assert.Equal("factions/whirlwind/techs.json", x.File));
    }

    [Fact]
    public void AMissingUnitSlot_IsOneError()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(MalazanUnits, root =>
        {
            JsonArray units = root["units"]!.AsArray();
            units.Remove(units.Single(u => (string)u!["id"]! == "malazan_crossbowman"));
        });
        DataError e = OneError(dir);
        Assert.Equal((MalazanUnits, "units"), (e.File, e.Path));
        Assert.Contains("no unit in slot 'ranged'", e.Message);
    }

    [Fact]
    public void ADoubledUnitSlot_IsOneErrorAtTheSecond_PlusTheSlotLeftEmpty()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", "malazan_sapper", "slot", "\"ranged\"");
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.Null(r.Data);
        Assert.Equal(2, r.Errors.Count);
        Assert.Contains(r.Errors, e => e.File == MalazanUnits && e.Path.EndsWith("].slot") && e.Message.Contains("slot 'ranged' is already filled by 'malazan_crossbowman'"));
        Assert.Contains(r.Errors, e => e.File == MalazanUnits && e.Path == "units" && e.Message.Contains("no unit in slot 'unique'"));
    }

    [Fact]
    public void AMissingBuildingSlot_IsOneError_AndAFactionWithNoBuildingsIsOne()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(MalazanBuildings, root =>
        {
            JsonArray list = root["buildings"]!.AsArray();
            list.Remove(list.Single(b => (string)b!["id"]! == "malazan_watchtower"));
        });
        DataError e = OneError(dir);
        Assert.Equal((MalazanBuildings, "buildings"), (e.File, e.Path));
        Assert.Contains("no building in slot 'watch_tower'", e.Message);

        File.WriteAllText(dir.FullPath(MalazanBuildings), "{\"buildings\": []}");
        e = OneError(dir); // the trainedAt / researchedAt / requires checks into a broken buildings file are skipped
        Assert.Equal((MalazanBuildings, "buildings"), (e.File, e.Path));
        Assert.Contains("no buildings", e.Message);
    }

    [Fact]
    public void ADoubledBuildingSlot_IsOneErrorAtTheSecond_PlusTheSlotLeftEmpty()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        EditBuilding(dir, MalazanBuildings, "malazan_watchtower", b => b["slot"] = "house");
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.Null(r.Data);
        Assert.Equal(2, r.Errors.Count);
        Assert.Contains(r.Errors, e => e.Path == "buildings[9].slot" && e.Message.Contains("slot 'house' is already filled by 'malazan_billet'"));
        Assert.Contains(r.Errors, e => e.Path == "buildings" && e.Message.Contains("no building in slot 'watch_tower'"));
    }
}
