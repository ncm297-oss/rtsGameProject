using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M3-2, session 2026-10-06-1503): <c>buildings.json</c> robustness beyond the developer's rows: out-of-range
/// and absurd numbers, wrong shapes, a duplicate inside one file. The loader must report each as exactly one
/// error (or at least one, never a throw or a silent default) and return no data.
/// </summary>
public class BuildingDataQaTests
{
    private const string File = "factions/malazan/buildings.json";

    private static DataLoadResult LoadWith(Action<JsonNode> edit)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(File, edit);
        return DataLoader.LoadAll(dir.Path);
    }

    private static void SetField(JsonNode root, string dotted, string? raw)
    {
        JsonObject parent = root["buildings"]![0]!.AsObject();
        string[] parts = dotted.Split('.');
        for (int i = 0; i < parts.Length - 1; i++) parent = parent[parts[i]]!.AsObject();
        if (raw == null) parent.Remove(parts[^1]);
        else parent[parts[^1]] = JsonNode.Parse(raw);
    }

    [Theory]
    [InlineData("popProvided", "1e300")]
    [InlineData("popProvided", "-0.5")]
    [InlineData("hp", "1e12")]
    [InlineData("hp", "2.5")]
    [InlineData("armor", "-1")]
    [InlineData("armor", "1e10")]
    [InlineData("cost.gold", "-1")]
    [InlineData("cost.wood", "1e15")]
    [InlineData("buildTime", "1e9")]
    [InlineData("buildTime", "-3")]
    [InlineData("footprint.width", "2.5")]
    [InlineData("footprint.height", "\"4\"")]
    [InlineData("slot", "\"\"")]
    [InlineData("slot", "\"TOWN_HALL\"")]
    [InlineData("description", "\"\"")]
    public void AbsurdOrWrongTypedBuildingField_IsOneError_NoData(string field, string? raw)
    {
        DataLoadResult r = LoadWith(root => SetField(root, field, raw));
        Assert.Null(r.Data);
        DataError e = Assert.Single(r.Errors);
        Assert.Equal(File, e.File);
        Assert.Contains(field.Split('.')[0], e.Path);
    }

    /// <summary>A missing <c>footprint</c> is one error (the loader says so); a missing <c>cost</c> was three (cost, cost.gold, cost.wood) until M3-3 (BUG-0079).</summary>
    [Fact]
    public void MissingCostObject_IsOneError_LikeAMissingFootprint()
    {
        DataLoadResult r = LoadWith(root => SetField(root, "cost", null));
        Assert.Null(r.Data);
        Assert.Equal("buildings[0].cost", Assert.Single(r.Errors).Path);
    }

    [Fact]
    public void DuplicateIdInsideOneFile_IsOneErrorAtTheSecond()
    {
        // D1 (2026-10-06-1744): the roster now has ten entries, so the copy of the Town Hall is inserted at index 1
        // (not appended) to keep the second occurrence at a fixed path.
        DataLoadResult r = LoadWith(root => root["buildings"]!.AsArray().Insert(1, JsonNode.Parse(root["buildings"]![0]!.ToJsonString())));
        Assert.Null(r.Data);
        DataError e = Assert.Single(r.Errors);
        Assert.Equal("buildings[1].id", e.Path);
    }

    [Theory]
    [InlineData("{\"buildings\": {}}")]
    [InlineData("{\"buildings\": [null]}")]
    [InlineData("{\"buildings\": null}")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"buildings\": [], \"extra\": 1}")]
    public void WrongShapedFile_IsReported_NeverThrows(string json)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        System.IO.File.WriteAllText(Path.Combine(dir.Path, File), json);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.Null(r.Data);
        Assert.NotEmpty(r.Errors);
        Assert.All(r.Errors, e => Assert.False(string.IsNullOrWhiteSpace(e.Message)));
    }

    [Fact]
    public void AnEmptyBuildingList_Loads_AndTheFactionSimplyHasNoTownHall_Note()
    {
        // Not an error today (only the Town Hall slot ships and slots aren't required yet, M3-6); pinned so a later
        // "every faction has a Town Hall" rule is a deliberate change.
        DataLoadResult r = LoadWith(root => root["buildings"]!.AsArray().Clear());
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        // D1 (2026-10-06-1744): Whirlwind ships ten buildings now, so "only the other faction's buildings remain"
        // replaces the old Assert.Single.
        GameData d = r.Data!;
        Assert.Equal(10, d.Buildings.Length);
        Assert.All(d.Buildings, b => Assert.Equal("whirlwind", d.Factions[b.Faction].Key));
    }
}
