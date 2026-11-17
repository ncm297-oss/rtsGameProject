using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>
/// M8-2 criteria 1 and 2: <c>units.json</c> <c>stillStealth</c> loads (status, delay in ticks), JSON <c>null</c> reads as
/// absent, and the field enters <see cref="GameData.ContentHash"/> only when a file carries it: the shipped data's hash is
/// unchanged, a block moves it, and each field moves it again. The refusals are
/// <c>DataValidationTests.BrokenUnitStillStealth_YieldsExactlyOneError</c>.
/// </summary>
public class StillStealthLoaderTests
{
    private static GameData With(string json, string faction = "malazan", string unit = "malazan_crossbowman") =>
        AiScenes.DataWith(dir => dir.SetUnitField(faction, unit, "stillStealth", json));

    [Fact]
    public void TheField_Loads_StatusAndDelay_OnOneUnitOnly()
    {
        GameData d = With(StillScenes.Block(3));
        UnitDef bow = d.Units[d.FindUnit("malazan_crossbowman")];
        Assert.True(bow.HasStillStealth);
        Assert.Equal(d.FindStatus("stealthed"), bow.StillStealthStatus);
        Assert.Equal(60, bow.StillStealthDelayTicks);
        Assert.True(d.AnyStillStealth());
        foreach (UnitDef u in d.Units)
            if (u.Key != "malazan_crossbowman") Assert.Equal((-1, 0, false), (u.StillStealthStatus, u.StillStealthDelayTicks, u.HasStillStealth));

        Assert.Equal(100, With(StillScenes.Block(5)).Units[d.FindUnit("malazan_crossbowman")].StillStealthDelayTicks);
        Assert.Equal(1, With(StillScenes.Block(0.05)).Units[d.FindUnit("malazan_crossbowman")].StillStealthDelayTicks); // one tick, the shortest
        Assert.Equal(1200, With(StillScenes.Block(DataLimits.MaxForestStealthDelay)).Units[d.FindUnit("malazan_crossbowman")].StillStealthDelayTicks);
        // Generic: a unit of a faction without forestStealth, any slot.
        GameData w = With(StillScenes.Block(3), "whirlwind", "whirlwind_battering_ram");
        Assert.Equal(60, w.Units[w.FindUnit("whirlwind_battering_ram")].StillStealthDelayTicks);
    }

    [Fact]
    public void Null_ReadsAsAbsent()
    {
        GameData d = With("null");
        Assert.False(d.AnyStillStealth());
        Assert.Equal(TestSim.Data.ContentHash(), d.ContentHash());
    }

    [Fact]
    public void TheShippedData_HasNoField()
    {
        Assert.False(TestSim.Data.AnyStillStealth());
        Assert.All(TestSim.Data.Units, u => Assert.Equal(-1, u.StillStealthStatus));
    }

    /// <summary>Absent: the hash is the shipped one; present: it moves, and each field (and the unit carrying it) moves it again.</summary>
    [Fact]
    public void TheHash_IsUnchangedWithoutTheField_MovesWithIt_AndWithEachField()
    {
        ulong shipped = TestSim.Data.ContentHash();
        Assert.Equal(0xB74DC7AE61B4862DUL, shipped); // criterion 2: the shipped data hash
        Assert.Equal(shipped, AiScenes.DataWith(_ => { }).ContentHash());
        ulong block = With(StillScenes.Block(3)).ContentHash();
        Assert.NotEqual(shipped, block);
        var hashes = new List<ulong>
        {
            shipped, block,
            With(StillScenes.Block(5)).ContentHash(), // delay
            With(StillScenes.Block(3), "whirlwind", "whirlwind_desert_archer").ContentHash(), // on another unit
        };
        JsonObject row = new() { ["id"] = "zz_test_shade", ["displayName"] = "Test", ["description"] = "Test only.", ["kind"] = "stealthed" };
        GameData Shade(string status) => AiScenes.DataWith(dir =>
        {
            dir.EditJson("common/statuses.json", r => r["statuses"]!.AsArray().Add(JsonNode.Parse(row.ToJsonString())));
            dir.SetUnitField("malazan", "malazan_crossbowman", "stillStealth", StillScenes.Block(3, status));
        });
        hashes.Add(Shade("stealthed").ContentHash());
        hashes.Add(Shade("zz_test_shade").ContentHash()); // status
        Assert.Equal(hashes.Count, hashes.Distinct().Count());
        Assert.Equal(block, With(StillScenes.Block(3)).ContentHash());
    }
}
