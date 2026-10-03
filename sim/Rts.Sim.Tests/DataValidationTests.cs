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
    [InlineData("common/damage_table.json")]
    [InlineData("factions/whirlwind/faction.json")]
    [InlineData("factions/whirlwind/units.json")]
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

    private static GameData Load(string dir)
    {
        DataLoadResult result = DataLoader.LoadAll(dir);
        Assert.True(result.Ok, string.Join(Environment.NewLine, result.Errors));
        return result.Data!;
    }
}
