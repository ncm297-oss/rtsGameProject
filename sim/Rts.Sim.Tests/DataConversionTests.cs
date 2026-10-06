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

    [Fact]
    public void Ids_AreDenseAndOrdinalSorted()
    {
        GameData data = Load(TestDataDir.Shipped);
        Assert.Equal(14, data.Units.Length);
        for (int i = 0; i < data.Units.Length; i++)
        {
            Assert.Equal(i, data.Units[i].Id);
            Assert.Equal(i, data.FindUnit(data.Units[i].Key));
            if (i > 0) Assert.True(string.CompareOrdinal(data.Units[i - 1].Key, data.Units[i].Key) < 0);
        }
        for (int i = 0; i < data.Factions.Length; i++)
        {
            Assert.Equal(i, data.Factions[i].Id);
            Assert.Equal(i, data.FindFaction(data.Factions[i].Key));
        }
        Assert.Equal(new[] { "giant", "heavy", "light", "mounted", "structure" }, data.DamageTable.ArmorClassKeys);
        Assert.Equal(-1, data.FindUnit("malazan_dragon"));
        Assert.Equal(-1, data.FindFaction("andii"));
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
        foreach (string faction in new[] { "malazan", "whirlwind" })
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
