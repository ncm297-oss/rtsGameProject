using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>The data-validation test (CLAUDE.md "Testing rules"): shipped data loads clean, and broken data is reported, not thrown.</summary>
public class DataValidationTests
{
    private const string MalazanUnits = "factions/malazan/units.json";

    [Fact]
    public void ShippedData_LoadsWithNoErrors()
    {
        DataLoadResult result = DataLoader.LoadAll(TestDataDir.Shipped);
        Assert.True(result.Errors.Count == 0, string.Join(Environment.NewLine, result.Errors));
        Assert.True(result.Ok);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public void ShippedData_MalazanAndWhirlwind_EachFillAllSevenSlotsOnce()
    {
        GameData data = Load(TestDataDir.Shipped);
        Assert.Equal(new[] { "malazan", "whirlwind" }, data.Factions.Select(f => f.Key));
        foreach (FactionDef f in data.Factions)
        {
            Assert.Equal(7, f.Units.Length);
            UnitSlot[] slots = f.Units.Select(id => data.Units[id].Slot).OrderBy(s => s).ToArray();
            Assert.Equal(Enum.GetValues<UnitSlot>(), slots);
            Assert.All(f.Units, id => Assert.Equal(f.Id, data.Units[id].Faction));
        }
    }

    [Fact]
    public void ShippedData_Crossbowman_MatchesFactionPage()
    {
        GameData data = Load(TestDataDir.Shipped);
        UnitDef u = data.Units[data.FindUnit("malazan_crossbowman")];
        DamageTable t = data.DamageTable;
        Assert.Equal("malazan", data.Factions[u.Faction].Key);
        Assert.Equal(UnitSlot.Ranged, u.Slot);
        Assert.Equal(55, u.Hp);
        Assert.Equal(0, u.Armor);
        Assert.Equal("light", t.ArmorClassKeys[u.ArmorClass]);
        Assert.Equal(9, u.Attack.Value);
        Assert.Equal("pierce", t.DamageTypeKeys[u.Attack.DamageType]);
        Assert.Equal(44, u.Attack.CooldownTicks); // 2.2 s
        Assert.Equal(15f, u.Attack.Range);
        Assert.Equal(1.3f, u.Attack.BonusVs[t.ArmorClassKeys.IndexOf("heavy")]);
        Assert.Equal(1f, u.Attack.BonusVs[t.ArmorClassKeys.IndexOf("light")]);
        Assert.Equal(36, u.CostGold);
        Assert.Equal(45, u.CostWood);
        Assert.Equal(2, u.HalfPop);
        Assert.Equal(320, u.TrainTicks); // 16 s
        Assert.Equal("malazan_crossbow_range", u.TrainedAt);
    }

    [Fact]
    public void ShippedData_DamageTableAndRules_MatchDocs02()
    {
        GameData data = Load(TestDataDir.Shipped);
        DamageTable t = data.DamageTable;
        int Type(string k) => t.DamageTypeKeys.IndexOf(k);
        int Class(string k) => t.ArmorClassKeys.IndexOf(k);
        Assert.Equal(4, t.DamageTypeKeys.Length);
        Assert.Equal(5, t.ArmorClassCount);
        Assert.Equal(0.6f, t.Multiplier(Type("pierce"), Class("heavy")));
        Assert.Equal(3.0f, t.Multiplier(Type("siege"), Class("structure")));
        Assert.Equal(1.25f, t.Multiplier(Type("magic"), Class("giant")));
        Assert.True(t.IgnoresArmor[Type("magic")]);
        Assert.False(t.IgnoresArmor[Type("pierce")]);

        RulesDef r = data.Rules;
        Assert.Equal(200, r.StartingGold);
        Assert.Equal(200, r.StartingWood);
        Assert.Equal(5, r.StartingWorkers);
        Assert.Equal(200, r.HalfPopCap); // hard cap 100
        Assert.Equal(10, r.WorkerCarry);
        Assert.Equal(0.7f / SimConstants.TicksPerSecond, r.GoldPerTick, 6);
        Assert.Equal(2500, r.StartMineGold);
    }

    [Theory]
    [InlineData("armorClass", "\"titanium\"", "units[2].armorClass", "unknown armor class 'titanium'")]
    [InlineData("attack.type", "\"fire\"", "units[2].attack.type", "unknown damage type 'fire'")]
    [InlineData("attack.bonusVs", "{\"titanium\": 1.3}", "units[2].attack.bonusVs.titanium", "unknown armor class")]
    [InlineData("slot", "\"wizard\"", "units[2].slot", "unknown slot")]
    [InlineData("hp", "0", "units[2].hp", "minimum")]
    [InlineData("hp", "-5", "units[2].hp", "minimum")]
    [InlineData("speed", "0", "units[2].speed", "positive")]
    [InlineData("attack.cooldown", "0", "units[2].attack.cooldown", "positive")]
    [InlineData("attack.cooldown", "-2.2", "units[2].attack.cooldown", "positive")]
    [InlineData("radius", "0.3", "units[2].radius", "outside")]
    [InlineData("radius", "1.2", "units[2].radius", "outside")]
    [InlineData("pop", "1.25", "units[2].pop", "multiple of 0.5")]
    [InlineData("displayName", null, "units[2].displayName", "missing")]
    [InlineData("attack.windup", null, "units[2].attack.windup", "missing")]
    // BUG-0007: bounds are checked before narrowing, so messages show the input, not an overflowed int.
    [InlineData("attack.cooldown", "1e10", "units[2].attack.cooldown", "10000000000 is above the maximum 1000000")]
    [InlineData("trainTime", "7200", "units[2].trainTime", "7200 s is above the maximum 3600 s")]
    [InlineData("pop", "1e10", "units[2].pop", "is above the maximum")]
    [InlineData("speed", "1e300", "units[2].speed", "is above the maximum")]
    [InlineData("hp", "2000000000", "units[2].hp", "2000000000 is above the maximum 1000000")]
    // BUG-0009: list entries are ids too.
    [InlineData("requires", "[\"Age_II\"]", "units[2].requires[0]", "not snake_case")]
    [InlineData("tags", "[\"infantry\", null]", "units[2].tags[1]", "missing")]
    public void BrokenUnitField_YieldsExactlyOneErrorNamingFileAndField(string field, string? rawJson, string path, string message)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", "malazan_crossbowman", field, rawJson);

        DataError e = Assert.Single(DataLoader.LoadAll(dir.Path).Errors);
        Assert.Equal(MalazanUnits, e.File);
        Assert.Equal(path, e.Path);
        Assert.Contains(message, e.Message);
    }

    [Fact]
    public void DuplicateUnitId_AcrossFactions_YieldsOneErrorAtSecondDefinition()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("whirlwind", "whirlwind_zealot", "id", "\"malazan_crossbowman\"");

        DataError e = Assert.Single(DataLoader.LoadAll(dir.Path).Errors);
        Assert.Equal("factions/whirlwind/units.json", e.File);
        Assert.Equal("units[6].id", e.Path);
        Assert.Contains("duplicate unit id 'malazan_crossbowman'", e.Message);
        Assert.Contains(MalazanUnits, e.Message);
    }

    [Fact]
    public void DuplicateUnitId_InSameFile_YieldsOneError()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", "malazan_sapper", "id", "\"malazan_laborer\"");

        DataError e = Assert.Single(DataLoader.LoadAll(dir.Path).Errors);
        Assert.Equal(MalazanUnits, e.File);
        Assert.Equal("units[6].id", e.Path);
        Assert.Contains("duplicate", e.Message);
    }

    [Fact]
    public void BrokenReferenceAndDuplicateId_BothReportedFromOneLoad()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", "malazan_crossbowman", "armorClass", "\"titanium\"");
        dir.SetUnitField("whirlwind", "whirlwind_zealot", "id", "\"whirlwind_raider\"");

        DataLoadResult result = DataLoader.LoadAll(dir.Path);
        Assert.False(result.Ok);
        Assert.Null(result.Data);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.File == MalazanUnits && e.Path == "units[2].armorClass");
        Assert.Contains(result.Errors, e => e.File == "factions/whirlwind/units.json" && e.Path == "units[6].id");
    }

    [Fact]
    public void MalformedJson_YieldsErrorWithPosition_AndOtherFilesStillValidated()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        File.WriteAllText(dir.FullPath("factions/whirlwind/units.json"), "{\n  \"units\": [\n    { \"id\": ,\n");
        dir.SetUnitField("malazan", "malazan_crossbowman", "armorClass", "\"titanium\"");

        DataLoadResult result = DataLoader.LoadAll(dir.Path);
        Assert.Equal(2, result.Errors.Count);
        DataError e = Assert.Single(result.Errors, x => x.File == "factions/whirlwind/units.json");
        Assert.Contains("malformed JSON at line 3", e.Message);
        Assert.Contains(result.Errors, x => x.File == MalazanUnits && x.Path == "units[2].armorClass");
    }

    [Fact]
    public void UnknownField_IsReportedNotIgnored()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", "malazan_crossbowman", "attack.cooldwon", "2.2");

        DataError e = Assert.Single(DataLoader.LoadAll(dir.Path).Errors);
        Assert.Equal(MalazanUnits, e.File);
        Assert.Contains("cooldwon", e.Path + e.Message);
    }

    [Theory]
    [InlineData("common/rules.json")]
    [InlineData("common/resources.json")]
    [InlineData("common/damage_table.json")]
    [InlineData("factions/whirlwind/faction.json")]
    [InlineData("factions/whirlwind/units.json")]
    [InlineData("factions/whirlwind/buildings.json")]
    public void MissingRequiredFile_YieldsOneError(string rel)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        File.Delete(dir.FullPath(rel));

        DataLoadResult result = DataLoader.LoadAll(dir.Path);
        Assert.Null(result.Data);
        // Without the damage table, unit armor/damage refs can't be checked, but they must not cascade into errors.
        DataError e = Assert.Single(result.Errors);
        Assert.Equal(rel, e.File);
        Assert.Contains("missing", e.Message);
    }

    [Fact]
    public void MissingDataDirectory_YieldsErrorNotException()
    {
        DataLoadResult result = DataLoader.LoadAll(Path.Combine(Path.GetTempPath(), "rts-data-tests", "does-not-exist"));
        Assert.False(result.Ok);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void FactionIdNotMatchingFolder_YieldsOneError()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/whirlwind/faction.json", f => f["id"] = "seven_cities");

        DataError e = Assert.Single(DataLoader.LoadAll(dir.Path).Errors);
        Assert.Equal("factions/whirlwind/faction.json", e.File);
        Assert.Equal("id", e.Path);
        Assert.Contains("folder", e.Message);
    }

    [Fact]
    public void DamageTable_MissingMultiplier_YieldsOneError()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("common/damage_table.json", t =>
            t["damageTypes"]![0]!["multipliers"]!.AsObject().Remove("structure"));

        DataError e = Assert.Single(DataLoader.LoadAll(dir.Path).Errors);
        Assert.Equal("common/damage_table.json", e.File);
        Assert.Equal("damageTypes[0].multipliers", e.Path);
        Assert.Contains("'structure'", e.Message);
    }

    [Fact]
    public void BadPaletteColor_YieldsOneError()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/malazan/faction.json", f => f["palette"]!["accent"] = "bronze");

        DataError e = Assert.Single(DataLoader.LoadAll(dir.Path).Errors);
        Assert.Equal("palette.accent", e.Path);
    }

    [Fact]
    public void ManyFaults_AllReported_NoException()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", "malazan_crossbowman", "hp", "0");
        dir.SetUnitField("malazan", "malazan_crossbowman", "radius", "5");
        dir.SetUnitField("malazan", "malazan_catapult", "slot", "\"artillery\"");
        dir.SetUnitField("whirlwind", "whirlwind_raider", "attack.type", "\"poison\"");
        dir.EditJson("common/rules.json", r => r.Remove("treeWood"));

        Assert.Equal(5, DataLoader.LoadAll(dir.Path).Errors.Count);
    }

    // ---------- M3-1: common/resources.json ----------

    private const string ResourcesFile = "common/resources.json";

    [Fact]
    public void ShippedData_Resources_TreeAndGoldMine_MatchDocs02()
    {
        GameData data = Load(TestDataDir.Shipped);
        Assert.Equal(new[] { "gold_mine", "tree" }, data.Resources.Select(r => r.Key)); // ordinal id order
        ResourceDef mine = data.Resources[data.FindResource("gold_mine")];
        ResourceDef tree = data.Resources[data.FindResource("tree")];
        Assert.Equal(ResourceKind.Gold, mine.Resource);
        Assert.Equal((2, 2), (mine.FootprintWidth, mine.FootprintHeight));
        Assert.Equal(ResourceKind.Wood, tree.Resource);
        Assert.Equal((1, 1), (tree.FootprintWidth, tree.FootprintHeight));
        Assert.All(data.Resources, r => Assert.False(string.IsNullOrWhiteSpace(r.DisplayName) || string.IsNullOrWhiteSpace(r.Description)));
        for (int i = 0; i < data.Resources.Length; i++) Assert.Equal(i, data.Resources[i].Id);
        Assert.Equal(-1, data.FindResource("stone"));
        // Amounts stay in rules.json (docs/02 "Economy").
        Assert.Equal(100, data.Rules.TreeWood);
        Assert.Equal(2500, data.Rules.StartMineGold);
    }

    /// <summary>Sets (or with <paramref name="rawJson"/> null, removes) a dotted field of resource entry <paramref name="index"/>.</summary>
    private static void SetResourceField(TestDataDir dir, int index, string dottedField, string? rawJson)
    {
        dir.EditJson(ResourcesFile, root =>
        {
            JsonObject parent = root["resources"]![index]!.AsObject();
            string[] parts = dottedField.Split('.');
            for (int i = 0; i < parts.Length - 1; i++)
                parent = parent[parts[i]]!.AsObject();
            if (rawJson == null) parent.Remove(parts[^1]);
            else parent[parts[^1]] = JsonNode.Parse(rawJson);
        });
    }

    [Theory]
    [InlineData("resource", "\"stone\"", "resources[1].resource", "unknown resource 'stone'")]
    [InlineData("resource", "\"Wood\"", "resources[1].resource", "unknown resource 'Wood'")]
    [InlineData("id", "\"Oak_Tree\"", "resources[1].id", "not snake_case")]
    [InlineData("id", "\"oak tree\"", "resources[1].id", "not snake_case")]
    [InlineData("footprint.width", "0", "resources[1].footprint.width", "outside 1-4")]
    [InlineData("footprint.height", "5", "resources[1].footprint.height", "outside 1-4")]
    [InlineData("footprint.width", "-1", "resources[1].footprint.width", "outside 1-4")]
    [InlineData("footprint.height", null, "resources[1].footprint.height", "missing")]
    [InlineData("footprint", null, "resources[1].footprint", "missing")]
    [InlineData("displayName", null, "resources[1].displayName", "missing")]
    [InlineData("description", "\" \"", "resources[1].description", "missing")]
    [InlineData("resource", null, "resources[1].resource", "missing")]
    public void BrokenResourceField_YieldsExactlyOneError(string field, string? rawJson, string path, string message)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        SetResourceField(dir, 1, field, rawJson);

        DataLoadResult result = DataLoader.LoadAll(dir.Path);
        Assert.Null(result.Data);
        DataError e = Assert.Single(result.Errors);
        Assert.Equal(ResourcesFile, e.File);
        Assert.Equal(path, e.Path);
        Assert.Contains(message, e.Message);
    }

    [Fact]
    public void DuplicateResourceId_YieldsOneErrorAtSecondDefinition()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        SetResourceField(dir, 1, "id", "\"gold_mine\"");

        DataError e = Assert.Single(DataLoader.LoadAll(dir.Path).Errors);
        Assert.Equal(ResourcesFile, e.File);
        Assert.Equal("resources[1].id", e.Path);
        Assert.Contains("duplicate resource id 'gold_mine'", e.Message);
    }

    [Theory]
    [InlineData("amount")]  // amounts live in rules.json, not here
    [InlineData("footprint.depth")]
    public void UnknownResourceField_IsReportedNotIgnored(string field)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        SetResourceField(dir, 0, field, "3");

        DataError e = Assert.Single(DataLoader.LoadAll(dir.Path).Errors);
        Assert.Equal(ResourcesFile, e.File);
        Assert.Contains(field.Split('.')[^1], e.Path + e.Message);
    }

    [Fact]
    public void ResourcesListMissing_YieldsOneError()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(ResourcesFile, root => root.Remove("resources"));

        DataError e = Assert.Single(DataLoader.LoadAll(dir.Path).Errors);
        Assert.Equal(ResourcesFile, e.File);
        Assert.Equal("resources", e.Path);
    }

    [Fact]
    public void ResourceIds_AreOrdinalOrder_NotFileOrder()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(ResourcesFile, root =>
        {
            JsonArray list = root["resources"]!.AsArray();
            JsonNode first = list[0]!;
            list.RemoveAt(0);
            list.Add(first); // tree first in the file now
        });
        GameData data = Load(dir.Path);
        Assert.Equal(new[] { "gold_mine", "tree" }, data.Resources.Select(r => r.Key));
        Assert.Equal(TestSim.Data.ContentHash(), data.ContentHash());
    }

    // ---------------------------------------------------------------- M3-2: buildings.json

    private const string MalazanBuildings = "factions/malazan/buildings.json";

    [Fact]
    public void ShippedData_Buildings_TownHallOfEachFaction_MatchesDocs02()
    {
        GameData data = Load(TestDataDir.Shipped);
        Assert.Equal(new[] { "malazan_garrison_keep", "whirlwind_holy_camp" }, data.Buildings.Select(b => b.Key));
        foreach (BuildingDef b in data.Buildings)
        {
            Assert.Equal(BuildingSlot.TownHall, b.Slot);
            Assert.Equal((4, 4), (b.FootprintWidth, b.FootprintHeight));
            Assert.Equal((2400, 5), (b.Hp, b.Armor));
            Assert.Equal((275, 275), (b.CostGold, b.CostWood));
            Assert.Equal(90 * SimConstants.TicksPerSecond, b.BuildTicks);
            Assert.Equal(20, b.HalfPopProvided); // +10 pop
            Assert.True(b.DropOff);
            Assert.False(string.IsNullOrWhiteSpace(b.DisplayName) || string.IsNullOrWhiteSpace(b.Description));
            Assert.Equal(b.Key.Split('_')[0], data.Factions[b.Faction].Key);
            // The faction's worker is trained here (docs/factions: trainedAt names the Town Hall).
            Assert.Contains(data.Factions[b.Faction].Units, id => data.Units[id].Slot == UnitSlot.Worker && data.Units[id].TrainedAt == b.Key);
        }
        Assert.Equal(1, data.FindBuilding("whirlwind_holy_camp"));
        Assert.Equal(-1, data.FindBuilding("malazan_barracks"));
    }

    private static void SetBuildingField(TestDataDir dir, string dottedField, string? rawJson)
    {
        dir.EditJson(MalazanBuildings, root =>
        {
            JsonObject parent = root["buildings"]![0]!.AsObject();
            string[] parts = dottedField.Split('.');
            for (int i = 0; i < parts.Length - 1; i++)
                parent = parent[parts[i]]!.AsObject();
            if (rawJson == null) parent.Remove(parts[^1]);
            else parent[parts[^1]] = JsonNode.Parse(rawJson);
        });
    }

    [Theory]
    [InlineData("slot", "\"barracks\"", "buildings[0].slot", "unknown slot 'barracks'")]
    [InlineData("footprint.width", "0", "buildings[0].footprint.width", "outside 1-4")]
    [InlineData("footprint.height", "5", "buildings[0].footprint.height", "outside 1-4")]
    [InlineData("dropOff", "\"yes\"", "$.buildings[0].dropOff", "malformed JSON")]
    [InlineData("dropOff", null, "buildings[0].dropOff", "missing")]
    [InlineData("hp", "0", "buildings[0].hp", "minimum")]
    [InlineData("buildTime", "0", "buildings[0].buildTime", "positive")]
    [InlineData("popProvided", "2.25", "buildings[0].popProvided", "multiple of 0.5")]
    [InlineData("id", "\"Garrison_Keep\"", "buildings[0].id", "not snake_case")]
    [InlineData("displayName", null, "buildings[0].displayName", "missing")]
    public void BrokenBuildingField_YieldsExactlyOneError(string field, string? rawJson, string path, string message)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        SetBuildingField(dir, field, rawJson);

        DataLoadResult result = DataLoader.LoadAll(dir.Path);
        Assert.Null(result.Data);
        DataError e = Assert.Single(result.Errors);
        Assert.Equal(MalazanBuildings, e.File);
        Assert.Equal(path, e.Path);
        Assert.Contains(message, e.Message);
    }

    [Theory]
    [InlineData("model")]
    [InlineData("footprint.depth")]
    public void UnknownBuildingField_IsReportedNotIgnored(string field)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        SetBuildingField(dir, field, "3");

        DataError e = Assert.Single(DataLoader.LoadAll(dir.Path).Errors);
        Assert.Equal(MalazanBuildings, e.File);
        Assert.Contains(field.Split('.')[^1], e.Path + e.Message);
    }

    [Fact]
    public void DuplicateBuildingId_AcrossFactions_YieldsOneErrorAtSecondDefinition()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/whirlwind/buildings.json", root => root["buildings"]![0]!["id"] = "malazan_garrison_keep");

        DataError e = Assert.Single(DataLoader.LoadAll(dir.Path).Errors);
        Assert.Equal("factions/whirlwind/buildings.json", e.File);
        Assert.Equal("buildings[0].id", e.Path);
        Assert.Contains("duplicate building id 'malazan_garrison_keep'", e.Message);
        Assert.Contains(MalazanBuildings, e.Message);
    }

    private static GameData Load(string dir)
    {
        DataLoadResult result = DataLoader.LoadAll(dir);
        Assert.True(result.Ok, string.Join(Environment.NewLine, result.Errors));
        return result.Data!;
    }
}
