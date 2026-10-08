using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-3a criterion 1: a building's optional <c>sight</c> and <c>rules.json</c>'s required <c>buildingSight</c> (sim-owned
/// schema): the shipped values, the default, and the loader's errors, each at its field and naming the id.
/// </summary>
public class BuildingSightLoaderTests
{
    private const string MalazanBuildings = "factions/malazan/buildings.json";

    private static DataLoadResult LoadWith(Action<TestDataDir> edit)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        edit(dir);
        return DataLoader.LoadAll(dir.Path);
    }

    /// <summary>Sets (or with null removes) the watchtower's <c>sight</c> as raw JSON.</summary>
    private static void SetTowerSight(TestDataDir dir, string? raw) =>
        dir.EditJson(MalazanBuildings, root =>
        {
            JsonObject tower = root["buildings"]!.AsArray().Select(n => n!.AsObject()).Single(b => (string)b["id"]! == "malazan_watchtower");
            if (raw == null) tower.Remove("sight");
            else tower["sight"] = JsonNode.Parse(raw);
        });

    private static int TowerIndex()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        JsonArray list = JsonNode.Parse(File.ReadAllText(dir.FullPath(MalazanBuildings)))!["buildings"]!.AsArray();
        for (int i = 0; i < list.Count; i++)
            if ((string)list[i]!["id"]! == "malazan_watchtower") return i;
        throw new InvalidOperationException("no malazan_watchtower");
    }

    [Fact]
    public void Shipped_BothTowersSee24m_EveryOtherBuildingTheRulesDefaultOf12m()
    {
        GameData d = TestSim.Data;
        Assert.Equal(12f, d.Rules.BuildingSight);
        foreach (BuildingDef b in d.Buildings)
        {
            bool tower = b.Key is "malazan_watchtower" or "whirlwind_lookout_tower";
            Assert.True(b.Sight == (tower ? 24f : 12f), $"{b.Key}: sight {b.Sight}");
        }
        Assert.Equal(2, d.Buildings.Count(b => b.Slot == BuildingSlot.WatchTower));
    }

    [Fact]
    public void ABuildingWithoutSight_TakesTheRulesDefault()
    {
        DataLoadResult r = LoadWith(dir =>
        {
            dir.EditJson("common/rules.json", root => root["buildingSight"] = 20);
            SetTowerSight(dir, null);
        });
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Assert.Equal(20f, r.Data!.Buildings[r.Data.FindBuilding("malazan_watchtower")].Sight);
        Assert.Equal(20f, r.Data.Buildings[r.Data.FindBuilding("malazan_garrison_keep")].Sight);
        Assert.Equal(24f, r.Data.Buildings[r.Data.FindBuilding("whirlwind_lookout_tower")].Sight);
    }

    [Theory]
    [InlineData("64", true)]
    [InlineData("0.5", true)]
    [InlineData("64.01", false)]
    [InlineData("1e300", false)]
    [InlineData("0", false)]
    [InlineData("-3", false)]
    public void ABuildingSight_IsAboveZeroAndAtMost64(string raw, bool ok)
    {
        DataLoadResult r = LoadWith(dir => SetTowerSight(dir, raw));
        Assert.Equal(ok, r.Ok);
        if (ok) return;
        DataError e = Assert.Single(r.Errors);
        Assert.Equal(MalazanBuildings, e.File);
        Assert.Equal($"buildings[{TowerIndex()}].sight", e.Path);
        Assert.Contains("malazan_watchtower", e.Message);
        Assert.DoesNotContain("Infinity", e.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("\"24\"")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("[24]")]
    public void ABuildingSightOfTheWrongType_IsAnErrorAtTheField(string raw)
    {
        DataLoadResult r = LoadWith(dir => SetTowerSight(dir, raw));
        if (raw == "null")
        {
            // JSON null is "not given": the default applies (as for every optional field).
            Assert.True(r.Ok, string.Join("\n", r.Errors));
            Assert.Equal(12f, r.Data!.Buildings[r.Data.FindBuilding("malazan_watchtower")].Sight);
            return;
        }
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.File == MalazanBuildings && e.Path.Contains($"buildings[{TowerIndex()}].sight"));
    }

    [Fact]
    public void AnUnknownBuildingField_IsAnError()
    {
        DataLoadResult r = LoadWith(dir => dir.EditJson(MalazanBuildings, root =>
            root["buildings"]!.AsArray()[0]!.AsObject()["sightRadius"] = 12));
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.File == MalazanBuildings && e.Path.Contains("sightRadius"));
    }

    [Fact]
    public void AMissingBuildingSightRule_IsAnErrorAtTheField()
    {
        DataLoadResult r = LoadWith(dir => dir.EditJson("common/rules.json", root => root.Remove("buildingSight")));
        Assert.False(r.Ok);
        DataError e = Assert.Single(r.Errors);
        Assert.Equal("common/rules.json", e.File);
        Assert.Equal("buildingSight", e.Path);
        Assert.Contains("missing", e.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65")]
    [InlineData("\"12\"")]
    public void ABadBuildingSightRule_IsAnErrorAtTheField(string raw)
    {
        DataLoadResult r = LoadWith(dir => dir.EditJson("common/rules.json", root => root["buildingSight"] = JsonNode.Parse(raw)));
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.File == "common/rules.json" && e.Path.Contains("buildingSight"));
    }

    [Fact]
    public void AUnitSightAbove64_IsAnErrorNamingTheUnit()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", "malazan_crossbowman", "sight", "65");
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        DataError e = Assert.Single(r.Errors);
        Assert.Equal("units[2].sight", e.Path);
        Assert.Contains("malazan_crossbowman", e.Message);
    }

    [Fact]
    public void TheContentHash_CoversABuildingsSight_AndTheRule()
    {
        ulong shipped = TestSim.Data.ContentHash();
        DataLoadResult tower = LoadWith(dir => SetTowerSight(dir, "23"));
        DataLoadResult rule = LoadWith(dir => dir.EditJson("common/rules.json", root => root["buildingSight"] = 13));
        Assert.True(tower.Ok && rule.Ok);
        Assert.NotEqual(shipped, tower.Data!.ContentHash());
        Assert.NotEqual(shipped, rule.Data!.ContentHash());
        Assert.NotEqual(tower.Data.ContentHash(), rule.Data.ContentHash());
    }
}
