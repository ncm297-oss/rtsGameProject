using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>
/// M8-4 criteria 6-7 (Request 56 G4, docs/03 "Implementation (M8-4)"): <c>units.json</c> <c>maxAlive</c>, a unit type's
/// limit. Optional: absent is no limit (0); a whole number from the type's <c>spawnCount</c> to
/// <see cref="DataLimits.MaxUnitMaxAlive"/> loads; zero, a negative, a fraction, a string, <c>null</c>, a value above the
/// bound or below <c>spawnCount</c> is one error at <c>units[i].maxAlive</c> naming the unit. Hashed only when present.
/// Rule 58: every row strips the field from every shipped unit first, so it passes on data that already carries one (the
/// Shadow roster's Hound, once it lands).
/// </summary>
public class MaxAliveLoaderTests
{
    private const string MalazanUnits = "factions/malazan/units.json";
    internal const string Heavy = "malazan_heavy_infantry";

    /// <summary>Row <paramref name="id"/>'s index in the shipped Malazan units file (the error path's index).</summary>
    private static int RowOf(string id)
    {
        JsonArray a = JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.Shipped, MalazanUnits)))!["units"]!.AsArray();
        for (int i = 0; i < a.Count; i++)
            if ((string)a[i]!["id"]! == id) return i;
        throw new InvalidOperationException(id);
    }

    /// <summary>Removes <c>maxAlive</c> from every unit of every shipped faction (the copy is then "no file carries it").</summary>
    internal static void Strip(TestDataDir dir)
    {
        foreach (string f in TestSim.ShippedFactions)
            dir.EditJson($"factions/{f}/units.json", r =>
            {
                foreach (JsonNode? u in r["units"]!.AsArray()) u!.AsObject().Remove("maxAlive");
            });
    }

    /// <summary>A stripped copy with <paramref name="id"/>'s <c>maxAlive</c> written as <paramref name="json"/> (null: left out), and its <c>spawnCount</c> as <paramref name="spawnCount"/> when given.</summary>
    internal static DataLoadResult LoadWith(string id, string? json, string? spawnCount = null)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        Strip(dir);
        if (spawnCount != null) dir.SetUnitField("malazan", id, "spawnCount", spawnCount);
        if (json != null) dir.SetUnitField("malazan", id, "maxAlive", json);
        return DataLoader.LoadAll(dir.Path);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("1", 1)]
    [InlineData("7", 7)]
    [InlineData("7.0", 7)]
    [InlineData("1000", 1000)]
    public void AMaxAlive_Loads_AbsentIsNoLimit(string? json, int want)
    {
        DataLoadResult r = LoadWith(Heavy, json);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Equal(want, r.Data!.Units[r.Data.FindUnit(Heavy)].MaxAlive);
    }

    [Fact]
    public void NoShippedUnit_HasALimit()
    {
        Assert.All(TestSim.Data.Units, u => Assert.Equal(0, u.MaxAlive));
    }

    [Theory]
    [InlineData("0", "maxAlive 0 is not a whole number 1-1000")]
    [InlineData("-3", "maxAlive -3 is not a whole number 1-1000")]
    [InlineData("6.5", "maxAlive 6.5 is not a whole number 1-1000")]
    [InlineData("1001", "maxAlive 1001 is not a whole number 1-1000")]
    [InlineData("1e9", "maxAlive 1000000000 is not a whole number 1-1000")]
    [InlineData("\"7\"", "wrong type of value for maxAlive")]
    [InlineData("true", "wrong type of value for maxAlive")]
    [InlineData("[7]", "wrong type of value for maxAlive")]
    [InlineData("null", "null is not a maxAlive")]
    public void ABrokenMaxAlive_IsOneError_AtItsPath_NamingTheUnit(string json, string text)
    {
        DataLoadResult r = LoadWith(Heavy, json);
        AssertOneErrorAt(r, $"units[{RowOf(Heavy)}].maxAlive", text);
    }

    /// <summary>A pack bigger than its limit could never be trained: below <c>spawnCount</c> is one error; equal loads.</summary>
    [Theory]
    [InlineData("3", "2", false)]
    [InlineData("8", "7", false)]
    [InlineData("3", "3", true)]
    [InlineData("3", "7", true)]
    public void BelowSpawnCount_IsOneError_EqualLoads(string spawnCount, string maxAlive, bool loads)
    {
        DataLoadResult r = LoadWith(Heavy, maxAlive, spawnCount);
        if (loads)
        {
            Assert.True(r.Ok, string.Join("; ", r.Errors));
            UnitDef u = r.Data!.Units[r.Data.FindUnit(Heavy)];
            Assert.Equal((int.Parse(spawnCount), int.Parse(maxAlive)), (u.SpawnCount, u.MaxAlive));
            return;
        }
        AssertOneErrorAt(r, $"units[{RowOf(Heavy)}].maxAlive", $"maxAlive {maxAlive} is below spawnCount {spawnCount}");
    }

    private static void AssertOneErrorAt(DataLoadResult r, string path, string text)
    {
        Assert.False(r.Ok);
        Assert.True(r.Errors.Count == 1 && r.Errors[0].File == MalazanUnits && r.Errors[0].Path == path
            && r.Errors[0].Message.Contains(text, StringComparison.Ordinal) && r.Errors[0].Message.Contains(Heavy, StringComparison.Ordinal),
            $"want one error at {MalazanUnits} {path} ({text}, naming {Heavy}), got: " + string.Join("; ", r.Errors));
    }

    /// <summary>
    /// Criterion 7: without the field the shipped data hashes as the golden's header says (the one pin of the shipped hash,
    /// re-pinned by whichever task moves it: rule 73, no literal here) and as a copy with the field stripped; with it the hash
    /// moves, again with another value, and again on another unit.
    /// </summary>
    [Fact]
    public void TheHash_IsUnchangedWithoutTheField_MovesWithIt_AndWithItsValue()
    {
        ulong shipped = TestSim.Data.ContentHash();
        string golden = File.ReadAllText(Path.Combine(TestDataDir.RepoRoot(), "sim", "Rts.Sim.Tests", "Replays", "cross_map_seed1.replay"));
        Assert.Contains("data-hash " + shipped.ToString("X16"), golden);
        ulong stripped = AiScenes.DataWith(Strip).ContentHash();
        Assert.Equal(shipped, stripped);
        GameData With(string unit, string value) => AiScenes.DataWith(dir =>
        {
            Strip(dir);
            dir.SetUnitField("malazan", unit, "maxAlive", value);
        });
        var hashes = new List<ulong> { shipped, With(Heavy, "7").ContentHash(), With(Heavy, "8").ContentHash(), With("malazan_crossbowman", "7").ContentHash() };
        Assert.Equal(hashes.Count, hashes.Distinct().Count());
        Assert.Equal(hashes[1], With(Heavy, "7").ContentHash());
    }
}
