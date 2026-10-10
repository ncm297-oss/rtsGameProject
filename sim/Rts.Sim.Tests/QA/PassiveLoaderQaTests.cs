using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M4-4b-3a (session 2026-10-10-1015): malformed <c>passives</c> shapes and a selfAura <c>range</c> of 0 are refused
/// with an error naming the file and a path, never a crash or a silent default; a broken statuses file is reported once
/// and the passives that name it don't crash the load.
/// </summary>
public class PassiveLoaderQaTests
{
    private const string WhirlwindUnits = "factions/whirlwind/units.json";

    private static DataLoadResult LoadWithZealot(Action<JsonObject> edit, Action<TestDataDir>? also = null)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(WhirlwindUnits, root =>
        {
            foreach (JsonNode? n in root["units"]!.AsArray())
                if ((string)n!["id"]! == "whirlwind_zealot") edit(n.AsObject());
        });
        also?.Invoke(dir);
        return DataLoader.LoadAll(dir.Path);
    }

    private static void AssertFailsIn(DataLoadResult r, string file, string pathPart)
    {
        Assert.False(r.Ok);
        Assert.True(r.Errors.Any(e => e.File == file && (e.Path ?? "").Contains(pathPart)), string.Join("\n", r.Errors));
    }

    [Theory]
    [InlineData("{}")]                                 // an object, not a list
    [InlineData("\"whenBelowHp\"")]                    // a string
    [InlineData("[\"whenBelowHp\"]")]                  // a list of strings
    [InlineData("[{\"trigger\": 1}]")]                 // a number for the trigger
    [InlineData("[{\"trigger\": \"WhenBelowHp\", \"hpFraction\": 0.5, \"status\": \"frenzied\", \"magnitude\": 0.5}]")] // wrong case
    [InlineData("[{\"trigger\": \"whenBelowHp\", \"hpFraction\": \"0.5\", \"status\": \"frenzied\", \"magnitude\": 0.5}]")] // a string number
    [InlineData("[{\"trigger\": \"onDeath\", \"status\": \"frenzied\", \"magnitude\": 0.5, \"duration\": 5, \"radius\": 6, \"units\": [null]}]")]
    [InlineData("[{\"trigger\": \"onDeath\", \"status\": \"frenzied\", \"magnitude\": 0.5, \"duration\": 5, \"radius\": 6, \"units\": \"whirlwind_zealot\"}]")]
    [InlineData("[{\"trigger\": \"onDeath\", \"status\": \"frenzied\", \"magnitude\": 0.5, \"duration\": 0.01, \"radius\": 6, \"units\": [\"whirlwind_zealot\"]}]")] // under a tick
    [InlineData("[{\"trigger\": \"onDeath\", \"status\": \"frenzied\", \"magnitude\": 0.5, \"duration\": 5, \"radius\": 6, \"units\": [\"whirlwind_raider_camp\"]}]")] // a building id
    [InlineData("[{\"trigger\": \"whenBelowHp\", \"hpFraction\": 0.5, \"status\": \"frenzied\", \"magnitude\": 1e300}]")] // above the decimal maximum
    public void MalformedPassives_AreErrorsInTheUnitsFile_NeverACrash(string rawPassives)
    {
        DataLoadResult r = LoadWithZealot(z => z["passives"] = JsonNode.Parse(rawPassives));
        Assert.False(r.Ok, "loaded: " + rawPassives);
        Assert.Contains(r.Errors, e => e.File == WhirlwindUnits);
    }

    [Fact]
    public void AnEmptyPassivesList_Loads_AsNoPassive()
    {
        DataLoadResult r = LoadWithZealot(z => z["passives"] = new JsonArray());
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        UnitDef zealot = r.Data!.Units[r.Data.FindUnit("whirlwind_zealot")];
        Assert.True(zealot.Passives.IsEmpty);
        Assert.Equal((-1, -1), (zealot.BelowHpPassive, zealot.DeathPassive));
    }

    [Fact]
    public void ABrokenStatusesFile_IsReported_AndThePassivesNamingItDontCrash()
    {
        DataLoadResult r = LoadWithZealot(_ => { }, dir => File.WriteAllText(dir.FullPath("common/statuses.json"), "{ \"statuses\": [ {"));
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.File == "common/statuses.json");
    }

    [Fact]
    public void ASelfAuraWithRange0_IsAnErrorAtItsRange()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        SelfAuraTests.AddTo(dir);
        dir.EditJson("factions/malazan/abilities.json", root =>
        {
            foreach (JsonNode? n in root["abilities"]!.AsArray())
                if ((string)n!["id"]! == "zz_test_rally") n["range"] = 0;
        });
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        AssertFailsIn(r, "factions/malazan/abilities.json", "range");
    }
}
