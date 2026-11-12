using System.Text.Json;
using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>Load-time conversions: seconds to ticks, pop to half-pop, and deterministic string-to-int ids.</summary>
public class DataConversionTests
{
    /// <summary>BUG-0057: a DataError prints "file: path: message" and leaves out an empty file or path with its separator (no ": : ").</summary>
    [Theory]
    [InlineData("units.json", "units[2].radius", "too big", "units.json: units[2].radius: too big")]
    [InlineData("units.json", "", "not JSON", "units.json: not JSON")]
    [InlineData("", "units[0]", "odd", "units[0]: odd")]
    [InlineData("", "", "data directory '' does not exist", "data directory '' does not exist")]
    public void DataError_ToString_LeavesOutEmptyFields(string file, string path, string message, string expected) =>
        Assert.Equal(expected, new DataError(file, path, message).ToString());

    [Theory]
    [InlineData(2.2, 44)]
    [InlineData(0.45, 9)]
    [InlineData(0.3, 6)]
    [InlineData(1.5, 30)]
    [InlineData(5.0, 100)]
    [InlineData(16, 320)]
    [InlineData(0, 0)]
    public void SecondsToTicks_At50ms(double seconds, int ticks)
    {
        Assert.Equal(50, SimConstants.TickMs);
        Assert.Equal(ticks, DataLoader.SecondsToTicks(seconds));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(1.5, 3)]
    [InlineData(0.5, 1)]
    [InlineData(3, 6)]
    [InlineData(0, 0)]
    [InlineData(1.25, -1)]
    public void ToHalfPop_StoresHalfSteps(double pop, int halfPop) => Assert.Equal(halfPop, DataLoader.ToHalfPop(pop));

    [Fact]
    public void LoadedUnits_AreConvertedToSimUnits()
    {
        GameData data = Load(TestDataDir.Shipped);
        UnitDef xbow = data.Units[data.FindUnit("malazan_crossbowman")];
        Assert.Equal(44, xbow.Attack.CooldownTicks);
        Assert.Equal(9, xbow.Attack.WindupTicks);
        Assert.Equal(320, xbow.TrainTicks);
        Assert.Equal(2, xbow.HalfPop);
        Assert.Equal(3.2f / SimConstants.TicksPerSecond, xbow.SpeedPerTick, 6);

        UnitDef lancer = data.Units[data.FindUnit("malazan_wickan_lancer")];
        Assert.Equal(4, lancer.HalfPop);
        UnitDef catapult = data.Units[data.FindUnit("malazan_catapult")];
        Assert.Equal(6, catapult.HalfPop);
        Assert.Equal(100, catapult.Attack.CooldownTicks);
        Assert.Equal(6f, catapult.Attack.MinRange);
        Assert.True(catapult.Attack.FriendlyFire);
    }

    [Fact]
    public void Pop_OneAndAHalf_LoadsAsThreeHalfPop()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", "malazan_cadre_mage", "pop", "1.5");
        GameData data = Load(dir.Path);
        Assert.Equal(3, data.Units[data.FindUnit("malazan_cadre_mage")].HalfPop);
    }

    /// <summary>M7-1 criterion 1: <c>scale</c> is optional: absent it is 1 (every Malazan and Whirlwind unit), and 1.5 loads as 1.5.</summary>
    [Fact]
    public void Scale_AbsentIsOne_AndOneAndAHalfLoads()
    {
        Assert.All(TestSim.TwoFactionData.Units, u => Assert.Equal(1f, u.Scale)); // M7-5 (BUG-1222): the two factions'; a third may scale
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "scale", "1.5");
        dir.SetUnitField("malazan", "malazan_laborer", "scale", "0.5");
        dir.SetUnitField("malazan", "malazan_catapult", "scale", "3");
        GameData data = Load(dir.Path);
        Assert.Equal(1.5f, data.Units[data.FindUnit("malazan_heavy_infantry")].Scale);
        Assert.Equal(0.5f, data.Units[data.FindUnit("malazan_laborer")].Scale);
        Assert.Equal(3f, data.Units[data.FindUnit("malazan_catapult")].Scale);
        Assert.Equal(1f, data.Units[data.FindUnit("malazan_crossbowman")].Scale);
        Assert.Equal(TestSim.Data.Units[data.FindUnit("malazan_heavy_infantry")].Radius, data.Units[data.FindUnit("malazan_heavy_infantry")].Radius); // collision unchanged
    }

    /// <summary>M7-2 criterion 1: <c>spawnCount</c> is optional: absent it is 1 (every Malazan and Whirlwind unit), 3 loads as 3, 8 (the maximum) as 8; cost, train time and pop stay the type's.</summary>
    [Fact]
    public void SpawnCount_AbsentIsOne_ThreeLoadsAsThree()
    {
        Assert.All(TestSim.TwoFactionData.Units, u => Assert.Equal((1, u.HalfPop), (u.SpawnCount, u.ItemHalfPop))); // M7-5: the two factions'
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "spawnCount", "3");
        dir.SetUnitField("malazan", "malazan_laborer", "spawnCount", "8");
        dir.SetUnitField("malazan", "malazan_crossbowman", "spawnCount", "1");
        GameData data = Load(dir.Path);
        UnitDef heavy = data.Units[data.FindUnit("malazan_heavy_infantry")], shipped = TestSim.Data.Units[heavy.Id];
        Assert.Equal((3, 3 * shipped.HalfPop), (heavy.SpawnCount, heavy.ItemHalfPop));
        Assert.Equal((shipped.CostGold, shipped.CostWood, shipped.TrainTicks, shipped.HalfPop), (heavy.CostGold, heavy.CostWood, heavy.TrainTicks, heavy.HalfPop));
        Assert.Equal(8, data.Units[data.FindUnit("malazan_laborer")].SpawnCount);
        Assert.Equal(1, data.Units[data.FindUnit("malazan_crossbowman")].SpawnCount);
    }

    /// <summary>M7-2 criterion 4: an <c>onHit</c> passive's <c>duration</c> converts to ticks (2 s = 40) on any unit.</summary>
    [Fact]
    public void OnHit_DurationConvertsToTicks()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "passives", "[{\"trigger\": \"onHit\", \"status\": \"slowed\", \"magnitude\": 0.2, \"duration\": 2}]");
        GameData data = Load(dir.Path);
        UnitDef heavy = data.Units[data.FindUnit("malazan_heavy_infantry")];
        Assert.Equal(0, heavy.HitPassive);
        Assert.Equal((PassiveTrigger.OnHit, data.FindStatus("slowed"), 0.2f, 40), (heavy.Passives[0].Trigger, heavy.Passives[0].Status, heavy.Passives[0].Magnitude, heavy.Passives[0].DurationTicks));
    }

    /// <summary>
    /// M7-1 criterion 3: <c>regen</c> absent is no regeneration (rate 0, delay 0); <c>{1, 6}</c> is 20 units a tick (at
    /// <c>CombatConstants.RegenFixedOne</c> = 400 a hit point) after 120 ticks; <c>{1.5, 5}</c> 30 after 100; a delay of 0 is allowed.
    /// </summary>
    [Theory]
    [InlineData(1, 6, 20, 120)]
    [InlineData(1.5, 5, 30, 100)]
    [InlineData(0.05, 0, 1, 0)]
    [InlineData(100, 3600, 2000, 72000)]
    public void Regen_ConvertsToFixedPointPerTick_AndDelayTicks(double hpPerSecond, double delay, int perTick, int delayTicks)
    {
        Assert.Equal(400, Rts.Sim.Combat.CombatConstants.RegenFixedOne);
        // M7-H1 (BUG-1191): the two shipped factions have no regen (a third may: the Teblor page's 1 HP/s after 6 s).
        Assert.All(TestSim.TwoFactionData.Factions, f => Assert.Equal((0, 0), (f.RegenPerTick, f.RegenDelayTicks)));
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/malazan/faction.json", r => r["regen"] = new System.Text.Json.Nodes.JsonObject { ["hpPerSecond"] = hpPerSecond, ["delay"] = delay });
        GameData data = Load(dir.Path);
        FactionDef m = data.Factions[data.FindFaction("malazan")], w = data.Factions[data.FindFaction("whirlwind")];
        Assert.Equal((perTick, delayTicks), (m.RegenPerTick, m.RegenDelayTicks));
        Assert.Equal((0, 0), (w.RegenPerTick, w.RegenDelayTicks));
    }

    [Fact]
    public void Ids_AreDenseAndOrdinalSorted()
    {
        GameData data = Load(TestDataDir.Shipped);
        // M7-H1 (BUG-1191): 14 is the two shipped factions' count. Since M7-3 the ids go faction by faction in the order
        // file's order, ordinal within a faction (a faction appended after Whirlwind may sort before it: teblor_ < whirlwind_).
        Assert.Equal(14, TestSim.TwoFactionData.Units.Length);
        for (int i = 0; i < data.Units.Length; i++)
        {
            Assert.Equal(i, data.Units[i].Id);
            Assert.Equal(i, data.FindUnit(data.Units[i].Key));
            if (i == 0) continue;
            if (data.Units[i - 1].Faction == data.Units[i].Faction) Assert.True(string.CompareOrdinal(data.Units[i - 1].Key, data.Units[i].Key) < 0);
            else Assert.True(data.Units[i - 1].Faction < data.Units[i].Faction);
        }
        for (int i = 0; i < data.Factions.Length; i++)
        {
            Assert.Equal(i, data.Factions[i].Id);
            Assert.Equal(i, data.FindFaction(data.Factions[i].Key));
        }
        Assert.Equal(new[] { "giant", "heavy", "light", "mounted", "structure" }, data.DamageTable.ArmorClassKeys);
        Assert.Equal(-1, data.FindUnit("malazan_dragon"));
        Assert.Equal(-1, data.FindFaction("no_such_faction")); // M7-H1 (BUG-1191): was "andii", a faction a later data set may add
    }

    [Fact]
    public void TwoLoads_GiveIdenticalData()
    {
        string a = Snapshot(Load(TestDataDir.Shipped));
        string b = Snapshot(Load(TestDataDir.Shipped));
        Assert.Contains("\"CooldownTicks\":44", a); // the snapshot really covers nested unit fields
        Assert.Contains("\"Multipliers\":[", a);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Ids_DoNotDependOnOrderInsideFiles()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        foreach (string faction in TestSim.ShippedFactions)
        {
            dir.EditJson($"factions/{faction}/units.json", root =>
            {
                JsonNode?[] units = root["units"]!.AsArray().ToArray();
                root["units"]!.AsArray().Clear();
                foreach (JsonNode? u in units.Reverse()) root["units"]!.AsArray().Add(u);
            });
        }
        dir.EditJson("common/damage_table.json", root =>
        {
            JsonNode?[] classes = root["armorClasses"]!.AsArray().ToArray();
            root["armorClasses"]!.AsArray().Clear();
            foreach (JsonNode? c in classes.Reverse()) root["armorClasses"]!.AsArray().Add(c);
        });

        Assert.Equal(Snapshot(Load(TestDataDir.Shipped)), Snapshot(Load(dir.Path)));
    }

    // Reflection-based serialization is fine here: it is test code, and it covers every public field.
    private static string Snapshot(GameData data) => JsonSerializer.Serialize(data);

    private static GameData Load(string dir)
    {
        DataLoadResult result = DataLoader.LoadAll(dir);
        Assert.True(result.Ok, string.Join(Environment.NewLine, result.Errors));
        return result.Data!;
    }
}
