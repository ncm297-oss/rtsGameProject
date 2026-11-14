using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>
/// M8-1 criterion 2: <c>faction.json</c> <c>forestStealth</c> loads (status, delay in ticks, excepted slots as a mask) and
/// enters <see cref="GameData.ContentHash"/> only when a file carries it: the shipped data's hash is unchanged (its pins stay
/// green with no new literal), a block moves it, and each field moves it again. The refusals are
/// <c>DataValidationTests.BrokenFactionForestStealth_YieldsExactlyOneError</c>.
/// </summary>
public class ForestStealthLoaderTests
{
    private static GameData With(JsonNode? block, string faction = "malazan") => AiScenes.DataWith(dir =>
        dir.EditJson($"factions/{faction}/faction.json", r => r["forestStealth"] = block));

    private static JsonObject Block(double delay, params string[] except)
    {
        var o = new JsonObject { ["status"] = "stealthed", ["delay"] = delay };
        if (except.Length > 0) o["exceptSlots"] = new JsonArray(except.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());
        return o;
    }

    [Fact]
    public void TheBlock_Loads_StatusDelayAndExceptedSlots()
    {
        GameData d = With(Block(3, "siege"));
        FactionDef m = d.Factions[d.FindFaction("malazan")];
        Assert.True(m.HasForestStealth);
        Assert.Equal(d.FindStatus("stealthed"), m.ForestStealthStatus);
        Assert.Equal(60, m.ForestStealthDelayTicks);
        Assert.Equal(1 << (int)UnitSlot.Siege, m.ForestStealthExceptSlots);
        Assert.True(d.AnyForestStealth());
        foreach (FactionDef f in d.Factions)
            if (f.Key != "malazan") Assert.Equal((-1, 0, 0, false), (f.ForestStealthStatus, f.ForestStealthDelayTicks, f.ForestStealthExceptSlots, f.HasForestStealth));

        GameData two = With(Block(5, "worker", "siege", "unique"));
        FactionDef m2 = two.Factions[two.FindFaction("malazan")];
        Assert.Equal(100, m2.ForestStealthDelayTicks);
        Assert.Equal((1 << (int)UnitSlot.Worker) | (1 << (int)UnitSlot.Siege) | (1 << (int)UnitSlot.Unique), m2.ForestStealthExceptSlots);

        GameData none = With(Block(0.05)); // one tick, the shortest; no exceptSlots = none excepted
        Assert.Equal((1, 0), (none.Factions[none.FindFaction("malazan")].ForestStealthDelayTicks, none.Factions[none.FindFaction("malazan")].ForestStealthExceptSlots));
        GameData max = With(Block(DataLimits.MaxForestStealthDelay, Array.Empty<string>()));
        Assert.Equal(1200, max.Factions[max.FindFaction("malazan")].ForestStealthDelayTicks);
    }

    [Fact]
    public void TheShippedData_HasNoBlock_AndNoFactionHasTheBonus()
    {
        Assert.False(TestSim.Data.AnyForestStealth());
        Assert.All(TestSim.Data.Factions, f => Assert.Equal(-1, f.ForestStealthStatus));
    }

    /// <summary>Absent: the hash is the shipped one (a copy's load hashes as the shipped data); present: it moves, and each field moves it again.</summary>
    [Fact]
    public void TheHash_IsUnchangedWithoutTheBlock_MovesWithIt_AndWithEachField()
    {
        ulong shipped = TestSim.Data.ContentHash();
        Assert.Equal(shipped, AiScenes.DataWith(_ => { }).ContentHash());
        ulong block = With(Block(3, "siege")).ContentHash();
        Assert.NotEqual(shipped, block);
        var hashes = new List<ulong>
        {
            shipped, block,
            With(Block(5, "siege")).ContentHash(), // delay
            With(Block(3, "siege", "worker")).ContentHash(), // exceptSlots
            With(Block(3)).ContentHash(), // no exceptSlots
            With(Block(3, "siege"), faction: "whirlwind").ContentHash(), // on another faction
        };
        // status: a second stealthed-kind status (statuses.json gains a row in both copies, so only the field differs).
        JsonObject row = new() { ["id"] = "zz_test_shade", ["displayName"] = "Test", ["description"] = "Test only.", ["kind"] = "stealthed" };
        GameData Shade(string status) => AiScenes.DataWith(dir =>
        {
            dir.EditJson("common/statuses.json", r => r["statuses"]!.AsArray().Add(JsonNode.Parse(row.ToJsonString())));
            dir.EditJson("factions/malazan/faction.json", r => r["forestStealth"] = new JsonObject { ["status"] = status, ["delay"] = 3, ["exceptSlots"] = new JsonArray("siege") });
        });
        hashes.Add(Shade("stealthed").ContentHash());
        hashes.Add(Shade("zz_test_shade").ContentHash());
        Assert.Equal(hashes.Count, hashes.Distinct().Count());
        // The same block twice hashes the same.
        Assert.Equal(block, With(Block(3, "siege")).ContentHash());
    }
}
