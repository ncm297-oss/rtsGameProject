using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>Shared sim setup for tests: the shipped <c>game/data</c>, loaded once per test run.</summary>
public static class TestSim
{
    private static readonly Lazy<GameData> s_data = new(() =>
    {
        DataLoadResult result = DataLoader.LoadAll(TestDataDir.Shipped);
        Assert.True(result.Ok, string.Join(Environment.NewLine, result.Errors));
        return result.Data!;
    });

    /// <summary>The shipped data (cached; GameData is immutable, so sharing it is safe).</summary>
    public static GameData Data => s_data.Value;

    private static readonly Lazy<GameData> s_withoutBuildingRequires = new(() =>
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        foreach (string faction in Directory.GetDirectories(dir.FullPath("factions")))
            dir.EditJson($"factions/{System.IO.Path.GetFileName(faction)}/buildings.json", root =>
            {
                foreach (JsonNode? b in root["buildings"]!.AsArray()) b!["requires"] = new JsonArray();
            });
        DataLoadResult result = DataLoader.LoadAll(dir.Path);
        Assert.True(result.Ok, string.Join(Environment.NewLine, result.Errors));
        return result.Data!;
    });

    /// <summary>
    /// The shipped data with every building's <c>requires</c> cleared (BUG-0112): for geometry oracles (the seal flood,
    /// never-seal) that place every type anywhere, where a D3 gate answering <c>Requires</c> first would starve them.
    /// Every id is the same as in <see cref="Data"/> (ids follow the string ids).
    /// </summary>
    public static GameData DataWithoutBuildingRequires => s_withoutBuildingRequires.Value;

    /// <summary>Number of unit types in the shipped data; valid type ids are 0 to this minus 1.</summary>
    public static int UnitTypeCount => Data.Units.Length;

    /// <summary>A <see cref="SimConfig"/> carrying the shipped data.</summary>
    public static SimConfig Config(ulong Seed, int PlayerCount, int UnitCapacity, int CommandCapacity) =>
        new(Seed, PlayerCount, UnitCapacity, CommandCapacity) { Data = Data };

    /// <summary>A <see cref="SimConfig"/> carrying <see cref="DataWithoutBuildingRequires"/>.</summary>
    public static SimConfig ConfigWithoutBuildingRequires(ulong Seed, int PlayerCount, int UnitCapacity, int CommandCapacity) =>
        new(Seed, PlayerCount, UnitCapacity, CommandCapacity) { Data = DataWithoutBuildingRequires };
}
