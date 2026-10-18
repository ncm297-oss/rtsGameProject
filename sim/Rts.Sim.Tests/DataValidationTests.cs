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

    /// <summary>M4-3b: both watch towers carry docs/02's "Attack 10 pierce / 2 s, range 18; sight 24; detector 16 m".</summary>
    [Theory]
    [InlineData("malazan_watchtower")]
    [InlineData("whirlwind_lookout_tower")]
    public void ShippedData_WatchTower_MatchesDocs02(string id)
    {
        GameData data = Load(TestDataDir.Shipped);
        BuildingDef b = data.Buildings[data.FindBuilding(id)];
        Assert.Equal(BuildingSlot.WatchTower, b.Slot);
        AttackDef a = b.Attack!;
        Assert.Equal(10, a.Value);
        Assert.Equal("pierce", data.DamageTable.DamageTypeKeys[a.DamageType]);
        Assert.Equal(40, a.CooldownTicks); // 2 s
        Assert.Equal(18f, a.Range);
        Assert.Equal(AttackTargets.Units, a.Targets);
        Assert.Equal(ProjectileKind.Aimed, data.Projectiles[a.ProjectileTypeId].Kind);
        Assert.Equal(24f, b.Sight);
        Assert.Equal(16f, b.Detector);
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
        Assert.Equal(0.85f / SimConstants.TicksPerSecond, r.GoldPerTick, 6); // M5-4f: 0.7 -> 0.85 (docs/02 Economy)
        Assert.Equal(0.75f / SimConstants.TicksPerSecond, r.WoodPerTick, 6);
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
    [InlineData("factions/whirlwind/ai.json")] // M5-1: required for every faction
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

    // ---------- M3-3: rules.json repair block ----------

    [Theory]
    [InlineData(null, null, "repair", "missing required field")]
    [InlineData("rateFactor", "0", "repair.rateFactor", "must be positive")]
    [InlineData("rateFactor", "1.5", "repair.rateFactor", "above the maximum 1")]
    [InlineData("costFactor", "-0.25", "repair.costFactor", "must be positive")]
    [InlineData("costFactor", "2", "repair.costFactor", "above the maximum 1")]
    [InlineData("costFactor", null, "repair.costFactor", "missing required field")]
    public void RepairBlock_MissingOrOutOfRange_IsOneError(string? field, string? value, string path, string message)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("common/rules.json", r =>
        {
            if (field == null) r.Remove("repair");
            else if (value == null) r["repair"]!.AsObject().Remove(field);
            else r["repair"]![field] = JsonNode.Parse(value);
        });
        DataError e = Assert.Single(DataLoader.LoadAll(dir.Path).Errors);
        Assert.Equal(path, e.Path);
        Assert.Contains(message, e.Message);
    }

    [Fact]
    public void ShippedData_RepairFactors_MatchDocs02()
    {
        RulesDef r = Load(TestDataDir.Shipped).Rules;
        Assert.Equal((0.5f, 0.25f), (r.RepairRateFactor, r.RepairCostFactor)); // 50% of the build rate, 25% of the cost
    }

    // ---------- M5-2: rules.json ai (the AI's generic tunables) ----------

    [Theory]
    [InlineData(null, null, "ai", "missing required field")]
    [InlineData("goldShare", null, "ai.goldShare", "missing required field")]
    [InlineData("goldShare", "1.5", "ai.goldShare", "outside 0-1")]
    [InlineData("goldShare", "-0.1", "ai.goldShare", "outside 0-1")]
    [InlineData("houseWhenPopWithin", "-1", "ai.houseWhenPopWithin", "must not be negative")]
    [InlineData("houseWhenPopWithin", "1.25", "ai.houseWhenPopWithin", "multiple of 0.5")]
    [InlineData("houseWhenPopWithin", "100.5", "ai.houseWhenPopWithin", "above the population cap 100")]
    [InlineData("placementSearchRadius", "0", "ai.placementSearchRadius", "must be positive")]
    [InlineData("placementSearchRadius", "64.5", "ai.placementSearchRadius", "above the maximum 64 m")]
    [InlineData("buildingSpacing", "-2", "ai.buildingSpacing", "must not be negative")]
    [InlineData("buildingSpacing", "9", "ai.buildingSpacing", "above the maximum 8 m")]
    [InlineData("expandHallReach", null, "ai.expandHallReach", "missing required field")]
    [InlineData("expandHallReach", "1e300", "ai.expandHallReach", "above the maximum")]
    [InlineData("scoutAfter", null, "ai.scoutAfter", "missing required field")] // M5-3's knobs
    [InlineData("scoutAfter", "-1", "ai.scoutAfter", "must not be negative")]
    [InlineData("scoutAfter", "3601", "ai.scoutAfter", "above the maximum 3600 s")]
    [InlineData("scoutRadius", null, "ai.scoutRadius", "missing required field")]
    [InlineData("scoutRadius", "0.5", "ai.scoutRadius", "outside 1-64 m")]
    [InlineData("scoutRadius", "65", "ai.scoutRadius", "outside 1-64 m")]
    [InlineData("scoutReach", null, "ai.scoutReach", "missing required field")]
    [InlineData("scoutReach", "0", "ai.scoutReach", "outside 1-16 m")]
    [InlineData("scoutReach", "16.5", "ai.scoutReach", "outside 1-16 m")]
    [InlineData("scoutGrid", null, "ai.scoutGrid", "missing required field")] // BUG-0593
    [InlineData("scoutGrid", "15.5", "ai.scoutGrid", "outside 16-256 m")]
    [InlineData("scoutGrid", "256.5", "ai.scoutGrid", "outside 16-256 m")]
    [InlineData("rallyDistance", null, "ai.rallyDistance", "missing required field")]
    [InlineData("rallyDistance", "-3", "ai.rallyDistance", "outside 1-64 m")]
    [InlineData("rallyDistance", "64.5", "ai.rallyDistance", "outside 1-64 m")]
    [InlineData("rallyReach", null, "ai.rallyReach", "missing required field")]
    [InlineData("rallyReach", "0.99", "ai.rallyReach", "outside 1-16 m")]
    [InlineData("rallyReach", "17", "ai.rallyReach", "outside 1-16 m")]
    [InlineData("waveCooldown", null, "ai.waveCooldown", "missing required field")]
    [InlineData("waveCooldown", "-0.5", "ai.waveCooldown", "must not be negative")]
    [InlineData("waveCooldown", "3600.5", "ai.waveCooldown", "above the maximum 3600 s")]
    [InlineData("defendRadius", null, "ai.defendRadius", "missing required field")] // M5-4b's knobs
    [InlineData("defendRadius", "7.5", "ai.defendRadius", "outside 8-256 m")]
    [InlineData("defendRadius", "256.5", "ai.defendRadius", "outside 8-256 m")]
    [InlineData("defendFor", null, "ai.defendFor", "missing required field")]
    [InlineData("defendFor", "-1", "ai.defendFor", "must not be negative")]
    [InlineData("defendFor", "600.5", "ai.defendFor", "above the maximum 600 s")]
    [InlineData("workerFleeRadius", null, "ai.workerFleeRadius", "missing required field")]
    [InlineData("workerFleeRadius", "0.5", "ai.workerFleeRadius", "outside 1-64 m")]
    [InlineData("workerFleeRadius", "64.5", "ai.workerFleeRadius", "outside 1-64 m")]
    [InlineData("castMinTargets", null, "ai.castMinTargets", "missing required field")] // M5-4c
    [InlineData("castMinTargets", "0", "ai.castMinTargets", "not a whole number in 1-50")]
    [InlineData("castMinTargets", "51", "ai.castMinTargets", "not a whole number in 1-50")]
    [InlineData("castMinTargets", "2.5", "ai.castMinTargets", "not a whole number in 1-50")]
    [InlineData("defendRatio", null, "ai.defendRatio", "missing required field")] // M5-4f
    [InlineData("defendRatio", "0.49", "ai.defendRatio", "outside 0.5-10")]
    [InlineData("defendRatio", "10.01", "ai.defendRatio", "outside 0.5-10")]
    [InlineData("defendMinValue", null, "ai.defendMinValue", "missing required field")]
    [InlineData("defendMinValue", "-0.5", "ai.defendMinValue", "outside 0-5000")]
    [InlineData("defendMinValue", "5000.5", "ai.defendMinValue", "outside 0-5000")]
    [InlineData("recallHallHp", null, "ai.recallHallHp", "missing required field")]
    [InlineData("recallHallHp", "-0.01", "ai.recallHallHp", "outside 0-1")]
    [InlineData("recallHallHp", "1.01", "ai.recallHallHp", "outside 0-1")]
    public void AiRulesBlock_MissingOrOutOfRange_IsOneError(string? field, string? value, string path, string message)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("common/rules.json", r =>
        {
            if (field == null) r.Remove("ai");
            else if (value == null) r["ai"]!.AsObject().Remove(field);
            else r["ai"]![field] = JsonNode.Parse(value);
        });
        DataError e = Assert.Single(DataLoader.LoadAll(dir.Path).Errors);
        Assert.Equal("common/rules.json", e.File);
        Assert.Equal(path, e.Path);
        Assert.Contains(message, e.Message);
    }

    /// <summary>BUG-0593: <c>ai.scoutGrid</c> loads at both edges, in meters.</summary>
    [Theory]
    [InlineData(16)]
    [InlineData(256)]
    public void AiScoutGrid_AtItsEdges_Loads(double meters)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("common/rules.json", r => r["ai"]!["scoutGrid"] = meters);
        DataLoadResult load = DataLoader.LoadAll(dir.Path);
        Assert.True(load.Ok, string.Join("; ", load.Errors));
        Assert.Equal((float)meters, load.Data!.AiRules.ScoutGrid);
    }

    [Fact]
    public void AiRulesBlock_UnknownField_IsReported()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("common/rules.json", r => r["ai"]!["aggression"] = 1);
        DataError e = Assert.Single(DataLoader.LoadAll(dir.Path).Errors);
        Assert.Contains("aggression", e.Path + e.Message);
    }

    [Fact]
    public void ShippedData_AiRules_InSimUnits()
    {
        AiRulesDef a = Load(TestDataDir.Shipped).AiRules;
        Assert.Equal(0.6f, a.GoldShare);
        Assert.Equal(4, a.HouseHalfPopMargin); // 2 pop
        Assert.Equal(16, a.PlacementSearchCells); // 32 m of 2 m cells
        Assert.Equal(1, a.SpacingCells); // 2 m
        Assert.Equal(20, a.ExpandReachCells); // 40 m
        Assert.Equal((300, 40f, 6f, 12f, 6f, 1200), (a.ScoutAfterTicks, a.ScoutRadius, a.ScoutReach, a.RallyDistance, a.RallyReach, a.WaveCooldownTicks)); // M5-3: 15 s, 60 s
        Assert.Equal(56f, a.ScoutGrid); // BUG-0593: meters
        Assert.Equal((48f, 400, 16f), (a.DefendRadius, a.DefendForTicks, a.WorkerFleeRadius)); // M5-4b: meters, 20 s
        Assert.Equal(2, a.CastMinTargets); // M5-4c
        Assert.Equal((1.5f, 150f, 0.5f), (a.DefendRatio, a.DefendMinValue, a.RecallHallHp)); // M5-4f
    }

    /// <summary>M5-4b: the defense knobs load at their edges (<c>defendFor</c> 0 and 600 s, <c>defendRadius</c> 8 and 256 m, <c>workerFleeRadius</c> 1 and 64 m).</summary>
    [Theory]
    [InlineData("defendFor", 0, 0)]
    [InlineData("defendFor", 600, 12000)]
    [InlineData("defendRadius", 8, 8)]
    [InlineData("defendRadius", 256, 256)]
    [InlineData("workerFleeRadius", 1, 1)]
    [InlineData("workerFleeRadius", 64, 64)]
    [InlineData("castMinTargets", 1, 1)] // M5-4c
    [InlineData("castMinTargets", 50, 50)]
    [InlineData("defendRatio", 0.5, 0.5)] // M5-4f
    [InlineData("defendRatio", 10, 10)]
    [InlineData("defendMinValue", 0, 0)]
    [InlineData("defendMinValue", 5000, 5000)]
    [InlineData("recallHallHp", 0, 0)]
    [InlineData("recallHallHp", 1, 1)]
    public void AiDefenseKnobs_AtTheirEdges_Load(string knob, double value, double loaded)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("common/rules.json", r => r["ai"]![knob] = value);
        DataLoadResult load = DataLoader.LoadAll(dir.Path);
        Assert.True(load.Ok, string.Join("; ", load.Errors));
        AiRulesDef a = load.Data!.AiRules;
        Assert.Equal(loaded, knob switch
        {
            "defendFor" => a.DefendForTicks, "defendRadius" => a.DefendRadius, "castMinTargets" => a.CastMinTargets,
            "defendRatio" => a.DefendRatio, "defendMinValue" => a.DefendMinValue, "recallHallHp" => a.RecallHallHp, _ => a.WorkerFleeRadius,
        }, 5);
    }

    /// <summary>M5-3 (inverted): the AI tunables are in the content hash, with ai.json (a row per field: DataContentHashTests).</summary>
    [Fact]
    public void AnAiRulesEdit_ChangesTheContentHash()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("common/rules.json", r => r["ai"]!["goldShare"] = 0.5);
        GameData edited = Load(dir.Path);
        Assert.Equal(0.5f, edited.AiRules.GoldShare);
        Assert.NotEqual(TestSim.Data.ContentHash(), edited.ContentHash());
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
        Assert.Equal(data.Buildings.Length, data.Buildings.Select(b => b.Key).Distinct(StringComparer.Ordinal).Count());
        foreach (FactionDef f in data.Factions)
        {
            // File order: each faction's buildings.json lists its Town Hall first.
            string path = Path.Combine(TestDataDir.Shipped, "factions", f.Key, "buildings.json");
            JsonNode first = JsonNode.Parse(File.ReadAllText(path))!["buildings"]![0]!;
            Assert.Equal("town_hall", (string?)first["slot"]);
            Assert.Equal(BuildingSlot.TownHall, data.Buildings[data.FindBuilding((string)first["id"]!)].Slot);
        }
        BuildingDef[] townHalls = data.Buildings.Where(b => b.Slot == BuildingSlot.TownHall).ToArray();
        Assert.Equal(new[] { "malazan_garrison_keep", "whirlwind_holy_camp" }, townHalls.Select(b => b.Key));
        foreach (BuildingDef b in townHalls)
        {
            Assert.Equal(BuildingSlot.TownHall, b.Slot);
            Assert.Equal((4, 4), (b.FootprintWidth, b.FootprintHeight));
            Assert.Equal((2400, 2), (b.Hp, b.Armor));
            Assert.Equal((275, 275), (b.CostGold, b.CostWood));
            Assert.Equal(90 * SimConstants.TicksPerSecond, b.BuildTicks);
            Assert.Equal(20, b.HalfPopProvided); // +10 pop
            Assert.True(b.DropOff);
            Assert.False(string.IsNullOrWhiteSpace(b.DisplayName) || string.IsNullOrWhiteSpace(b.Description));
            Assert.Equal(b.Key.Split('_')[0], data.Factions[b.Faction].Key);
            // The faction's worker is trained here (docs/factions: trainedAt names the Town Hall).
            Assert.Contains(data.Factions[b.Faction].Units, id => data.Units[id].Slot == UnitSlot.Worker && data.Units[id].TrainedAt == b.Key);
        }
        Assert.Equal("whirlwind_holy_camp", data.Buildings[data.FindBuilding("whirlwind_holy_camp")].Key);
        Assert.Equal(-1, data.FindBuilding("malazan_no_such_building"));
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
    [InlineData("dropOff", "\"yes\"", "$.buildings[0].dropOff", "wrong type of value at line")]
    [InlineData("dropOff", "1", "$.buildings[0].dropOff", "expected true or false")]
    [InlineData("hp", "\"400\"", "$.buildings[0].hp", "expected a whole number")]
    [InlineData("hp", "2.5", "$.buildings[0].hp", "expected a whole number")]
    [InlineData("hp", "1e10", "$.buildings[0].hp", "expected a whole number")]
    [InlineData("buildTime", "\"30\"", "$.buildings[0].buildTime", "expected a number")]
    [InlineData("slot", "3", "$.buildings[0].slot", "expected a string")]
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

    // ---------- M3-4: trainedAt resolves to an own-faction building ----------

    [Fact]
    public void ShippedData_TrainedAt_ResolvesForAll14Units()
    {
        GameData data = Load(TestDataDir.Shipped);
        Assert.Equal(14, data.Units.Length);
        foreach (UnitDef u in data.Units)
        {
            int b = data.FindBuilding(u.TrainedAt);
            Assert.True(b >= 0, u.Key);
            Assert.Equal(b, u.TrainedAtTypeId);
            Assert.Equal(u.Faction, data.Buildings[b].Faction);
            Assert.Contains(u.Id, data.UnitsTrainedAt(b));
        }
        Assert.Equal(data.FindBuilding("malazan_barracks"), data.Units[data.FindUnit("malazan_heavy_infantry")].TrainedAtTypeId);
    }

    [Theory]
    [InlineData("\"malazan_barack\"", "unknown building 'malazan_barack'")]
    [InlineData("\"whirlwind_archer_camp\"", "belongs to faction 'whirlwind', not 'malazan'")]
    public void TrainedAt_UnknownOrAnotherFactions_IsOneErrorAtTheField(string rawJson, string message)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", "malazan_crossbowman", "trainedAt", rawJson);

        DataError e = Assert.Single(DataLoader.LoadAll(dir.Path).Errors);
        Assert.Equal(MalazanUnits, e.File);
        Assert.Equal("units[2].trainedAt", e.Path);
        Assert.Contains(message, e.Message);
    }

    [Fact]
    public void TrainedAt_NotChecked_WhenTheBuildingsThemselvesHaveErrors()
    {
        // A broken buildings file is one error, not one more per unit naming its buildings.
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(MalazanBuildings, root => root["buildings"]![0]!["hp"] = 0);
        DataError e = Assert.Single(DataLoader.LoadAll(dir.Path).Errors);
        Assert.Equal(MalazanBuildings, e.File);
    }

    /// <summary>
    /// M5-1 criterion 4: each <c>ai.json</c> fault the brief names is exactly one error, at its file and field: an unknown id,
    /// another faction's id, a missing difficulty, a negative think interval, a repeated build-order step.
    /// </summary>
    [Theory]
    [InlineData("unknown id", "openings[0].buildOrder[0].id", "unknown unit 'malazan_ghost'")]
    [InlineData("foreign-faction id", "openings[0].buildOrder[0].id", "unit 'whirlwind_raider' belongs to faction 'whirlwind', not 'malazan'")]
    [InlineData("missing difficulty", "difficulties.hard", "missing required field")]
    [InlineData("negative interval", "difficulties.easy.thinkInterval", "-1 must be positive")]
    [InlineData("duplicate step", "openings[0].buildOrder[1]", "the same step as openings[0].buildOrder[0]")]
    public void BrokenAiJson_YieldsExactlyOneErrorAtTheField(string fault, string path, string message)
    {
        const string MalazanAi = "factions/malazan/ai.json";
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(MalazanAi, root =>
        {
            JsonArray steps = AiScenes.Opening(root)["buildOrder"]!.AsArray();
            switch (fault)
            {
                case "unknown id": steps[0]!["id"] = "malazan_ghost"; break;
                case "foreign-faction id": steps[0]!["id"] = "whirlwind_raider"; break;
                case "missing difficulty": root["difficulties"]!.AsObject().Remove("hard"); break;
                case "negative interval": root["difficulties"]!["easy"]!["thinkInterval"] = -1; break;
                case "duplicate step": steps.Insert(1, steps[0]!.DeepClone()); break; // at [1] whatever the shipped order's length (BUG-0571)
                default: throw new ArgumentException(fault);
            }
        });

        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        DataError e = Assert.Single(r.Errors);
        Assert.Null(r.Data);
        Assert.Equal(MalazanAi, e.File);
        Assert.Equal(path, e.Path);
        Assert.Contains(message, e.Message);
    }

    /// <summary>
    /// M5-8a criterion 2: each <c>openings</c> fault is exactly one error at its field: no list, an empty one, one above
    /// <see cref="DataLimits.MaxAiOpenings"/>, a repeated or non-snake_case id, a missing, blank or too-long
    /// <c>displayName</c>, a too-long <c>description</c>, each old top-level field (moved into <c>openings[]</c>), and a bad step
    /// inside the second opening, reported at <c>openings[1].buildOrder[j]</c>.
    /// </summary>
    [Theory]
    [InlineData("no openings", "openings", "missing required field")]
    [InlineData("empty openings", "openings", "no openings (at least one)")]
    [InlineData("nine openings", "openings", "9 openings is above the maximum 8")]
    [InlineData("duplicate id", "openings[1].id", "duplicate id 'standard' (also openings[0])")]
    [InlineData("camelCase id", "openings[1].id", "id 'rushOpening' is not snake_case")]
    [InlineData("missing displayName", "openings[1].displayName", "missing required text")]
    [InlineData("blank displayName", "openings[1].displayName", "missing required text")]
    [InlineData("long displayName", "openings[1].displayName", "41 characters is above the maximum 40")]
    [InlineData("long description", "openings[1].description", "201 characters is above the maximum 200")]
    [InlineData("old buildOrder", "buildOrder", "moved into openings[]")]
    [InlineData("old compositions", "compositions", "moved into openings[]")]
    [InlineData("old attack", "attack", "moved into openings[]")]
    [InlineData("bad inner step", "openings[1].buildOrder[2].id", "unknown building 'malazan_ghost'")]
    [InlineData("missing inner attack", "openings[1].attack", "missing required field")]
    public void BrokenAiOpenings_YieldExactlyOneErrorAtTheField(string fault, string path, string message)
    {
        const string MalazanAi = "factions/malazan/ai.json";
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(MalazanAi, root =>
        {
            JsonArray openings = root["openings"]!.AsArray();
            // Only the first shipped opening is kept (BUG-0980), so the indices and counts below hold whatever the file ships.
            while (openings.Count > 1) openings.RemoveAt(openings.Count - 1);
            JsonNode first = openings[0]!;
            JsonNode second = first.DeepClone();
            second["id"] = "second";
            openings.Add(second); // a valid second opening to break (the shipped first one stays as it is)
            switch (fault)
            {
                case "no openings": root.Remove("openings"); break;
                case "empty openings": openings.Clear(); break;
                case "nine openings":
                    for (int i = 2; i < 9; i++)
                    {
                        JsonNode more = first.DeepClone();
                        more["id"] = $"opening_{i}";
                        openings.Add(more);
                    }
                    break;
                case "duplicate id": second["id"] = "standard"; break;
                case "camelCase id": second["id"] = "rushOpening"; break;
                case "missing displayName": second.AsObject().Remove("displayName"); break;
                case "blank displayName": second["displayName"] = "   "; break;
                case "long displayName": second["displayName"] = new string('a', 41); break;
                case "long description": second["description"] = new string('a', 201); break;
                case "old buildOrder": root["buildOrder"] = first["buildOrder"]!.DeepClone(); break;
                case "old compositions": root["compositions"] = first["compositions"]!.DeepClone(); break;
                case "old attack": root["attack"] = first["attack"]!.DeepClone(); break;
                case "bad inner step": second["buildOrder"]![2] = JsonNode.Parse("""{ "at": { "time": 1 }, "do": "build", "id": "malazan_ghost" }"""); break;
                case "missing inner attack": second.AsObject().Remove("attack"); break;
                default: throw new ArgumentException(fault);
            }
        });

        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        DataError e = Assert.Single(r.Errors);
        Assert.Null(r.Data);
        Assert.Equal(MalazanAi, e.File);
        Assert.Equal(path, e.Path);
        Assert.Contains(message, e.Message);
    }

    /// <summary>M5-8a: a description is optional (absent loads as ""), and exactly 40 / 200 characters load.</summary>
    [Fact]
    public void AnOpeningDescription_IsOptional_AndTheLimitsAreInclusive()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            JsonArray openings = root["openings"]!.AsArray();
            // Only the first shipped opening is kept (BUG-0980), so the count of 2 holds whatever the file ships.
            while (openings.Count > 1) openings.RemoveAt(openings.Count - 1);
            JsonNode first = openings[0]!;
            JsonNode second = first.DeepClone();
            second["id"] = "second";
            second.AsObject().Remove("description");
            first["displayName"] = new string('n', 40);
            first["description"] = new string('d', 200);
            openings.Add(second);
        });
        GameData d = Load(dir.Path);
        AiDef ai = d.Ai[d.FindFaction("malazan")];
        Assert.Equal(2, ai.Openings.Length);
        Assert.Equal("", ai.Openings[1].Description);
        Assert.Equal(40, ai.Openings[0].DisplayName.Length);
        Assert.Equal(200, ai.Openings[0].Description.Length);
        Assert.Equal(1, ai.FindOpening("second"));
    }

    private static GameData Load(string dir)
    {
        DataLoadResult result = DataLoader.LoadAll(dir);
        Assert.True(result.Ok, string.Join(Environment.NewLine, result.Errors));
        return result.Data!;
    }
}
