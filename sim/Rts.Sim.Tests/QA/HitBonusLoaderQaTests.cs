using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>
/// QA (M8-3, session 2026-11-18-0926): the two new schema fields at their exact bounds (accepted), just past them and in the
/// wrong JSON type (refused with one error naming the unit and the field, never a crash), a JSON null on a building's attack
/// (absent), and every bound at once on one unit.
/// </summary>
public class HitBonusLoaderQaTests
{
    private const string Unit = "malazan_crossbowman";

    private static DataLoadResult Load(Action<TestDataDir> edit)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        edit(dir);
        return DataLoader.LoadAll(dir.Path);
    }

    private static void SetPack(TestDataDir dir, string json)
    {
        JsonObject pack = new()
        {
            ["trigger"] = "nearbyOwn", ["units"] = new JsonArray(Unit), ["radius"] = 8, ["damageBonus"] = 0.1, ["maxStacks"] = 3,
        };
        foreach (KeyValuePair<string, JsonNode?> kv in JsonNode.Parse(json)!.AsObject())
            pack[kv.Key] = kv.Value?.DeepClone();
        dir.SetUnitField("malazan", Unit, "passives", new JsonArray(pack).ToJsonString());
    }

    [Theory]
    [InlineData("4")] // the bound itself
    [InlineData("4.0")]
    [InlineData("1e-9")] // anything above 0
    public void FromStealthBonus_AtTheBound_Loads(string value)
    {
        DataLoadResult r = Load(d => d.SetUnitField("malazan", Unit, "attack.fromStealthBonus", value));
        Assert.True(r.Data != null, string.Join("; ", r.Errors));
        Assert.Equal((float)double.Parse(value, System.Globalization.CultureInfo.InvariantCulture), r.Data!.Units[r.Data.FindUnit(Unit)].Attack.FromStealthBonus);
    }

    [Theory]
    [InlineData("4.000001")]
    [InlineData("\"0.5\"")] // a string
    [InlineData("true")]
    [InlineData("[0.5]")]
    public void FromStealthBonus_PastTheBoundOrOfTheWrongType_IsRefusedCleanly(string value)
    {
        DataLoadResult r = Load(d => d.SetUnitField("malazan", Unit, "attack.fromStealthBonus", value));
        Assert.Null(r.Data);
        Assert.NotEmpty(r.Errors);
        Assert.Contains(r.Errors, e => e.ToString().Contains("fromStealthBonus") || e.ToString().Contains("units.json"));
    }

    [Fact]
    public void FromStealthBonus_PastTheBound_IsOneErrorNamingTheUnitAndTheField()
    {
        DataLoadResult r = Load(d => d.SetUnitField("malazan", Unit, "attack.fromStealthBonus", "4.000001"));
        string e = Assert.Single(r.Errors).ToString();
        Assert.Contains("fromStealthBonus", e);
        Assert.Contains(Unit, e);
    }

    [Fact]
    public void FromStealthBonus_NullOnABuildingsAttack_ReadsAsAbsent()
    {
        DataLoadResult r = Load(d => d.EditJson("factions/malazan/buildings.json", root =>
        {
            foreach (JsonNode? b in root["buildings"]!.AsArray())
                if (b!["attack"] is JsonObject a) a["fromStealthBonus"] = null;
        }));
        Assert.True(r.Data != null, string.Join("; ", r.Errors));
    }

    [Theory]
    [InlineData("{\"damageBonus\": 1}")]
    [InlineData("{\"maxStacks\": 8}")]
    [InlineData("{\"maxStacks\": 8.0}")]
    [InlineData("{\"maxStacks\": 1}")]
    [InlineData("{\"radius\": 64}")]
    [InlineData("{\"radius\": 64, \"damageBonus\": 1, \"maxStacks\": 8}")]
    public void NearbyOwn_AtEveryBound_Loads(string json)
    {
        DataLoadResult r = Load(d => SetPack(d, json));
        Assert.True(r.Data != null, string.Join("; ", r.Errors));
        UnitDef u = r.Data!.Units[r.Data.FindUnit(Unit)];
        Assert.True(u.NearbyPassive >= 0);
        Assert.True(r.Data.AnyHitBonus());
    }

    [Theory]
    [InlineData("{\"damageBonus\": 1.000001}", "damageBonus")]
    [InlineData("{\"maxStacks\": 8.000001}", "maxStacks")]
    [InlineData("{\"maxStacks\": 1e300}", "maxStacks")]
    [InlineData("{\"maxStacks\": -1}", "maxStacks")]
    [InlineData("{\"maxStacks\": \"3\"}", "")]
    [InlineData("{\"damageBonus\": \"0.1\"}", "")]
    [InlineData("{\"radius\": 64.0001}", "radius")]
    public void NearbyOwn_PastABoundOrOfTheWrongType_IsRefusedCleanly(string json, string field)
    {
        DataLoadResult r = Load(d => SetPack(d, json));
        Assert.Null(r.Data);
        if (field.Length > 0)
        {
            string e = Assert.Single(r.Errors).ToString();
            Assert.Contains(field, e);
            Assert.Contains(Unit, e);
        }
        else Assert.NotEmpty(r.Errors);
    }
}
