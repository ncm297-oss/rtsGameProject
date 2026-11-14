using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-7 (Request 48): <c>units.json</c> <c>carry</c>, a worker's own load a trip. Optional: absent means <c>rules.json</c>
/// <c>workerCarry</c>; a whole number 1-1,000,000 on a worker loads; every other value, and the field on a unit that is not a
/// worker, is one error at <c>units[i].carry</c>. Hashed only when present. Rules 58 / 59: every row sets or removes the
/// field itself, so it passes on a copy that already carries one (the data track's Gatherer row, once it lands).
/// </summary>
public class UnitCarryLoaderTests
{
    private const string TeblorUnits = "factions/teblor/units.json";
    private const string Gatherer = "teblor_gatherer";
    private const string Warrior = "teblor_warrior";

    /// <summary>Row <paramref name="id"/>'s index in the shipped Teblor units file (the error path's index).</summary>
    private static int RowOf(string id)
    {
        JsonArray a = JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.Shipped, TeblorUnits)))!["units"]!.AsArray();
        for (int i = 0; i < a.Count; i++)
            if ((string)a[i]!["id"]! == id) return i;
        throw new InvalidOperationException(id);
    }

    /// <summary>A copy with every unit's <c>carry</c> removed, then <paramref name="id"/>'s set to <paramref name="json"/> (null: left out).</summary>
    internal static DataLoadResult LoadWith(string id, string? json)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        StripCarry(dir);
        if (json != null) dir.SetUnitField("teblor", id, "carry", json);
        return DataLoader.LoadAll(dir.Path);
    }

    /// <summary>Removes <c>carry</c> from every unit of every shipped faction (the copy is then "no file carries it").</summary>
    internal static void StripCarry(TestDataDir dir)
    {
        foreach (string f in TestSim.ShippedFactions)
            dir.EditJson($"factions/{f}/units.json", r =>
            {
                foreach (JsonNode? u in r["units"]!.AsArray()) u!.AsObject().Remove("carry");
            });
    }

    private static void AssertOneErrorAt(DataLoadResult r, string path, string text)
    {
        Assert.False(r.Ok);
        Assert.True(r.Errors.Count == 1 && r.Errors[0].File == TeblorUnits && r.Errors[0].Path == path && r.Errors[0].Message.Contains(text),
            $"want one error at {TeblorUnits} {path} ({text}), got: " + string.Join("; ", r.Errors));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("15")]
    [InlineData("1000000")]
    public void ACarry_Loads_AbsentIsTheRulesValue(string? json)
    {
        DataLoadResult r = LoadWith(Gatherer, json);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        GameData d = r.Data!;
        int g = d.FindUnit(Gatherer);
        int? want = json == null ? null : int.Parse(json);
        Assert.Equal(want ?? 0, d.Units[g].Carry); // 0: none
        Assert.Equal(want ?? d.Rules.WorkerCarry, d.CarryOf(g));
        // Every other unit is untouched: no carry, the rules' value.
        Assert.All(d.Units.Where(u => u.Id != g), u => Assert.Equal(0, u.Carry));
        Assert.All(d.Units.Where(u => u.Id != g), u => Assert.Equal(d.Rules.WorkerCarry, d.CarryOf(u.Id)));
    }

    [Theory]
    [InlineData("0", "is not a whole number 1-1000000")]
    [InlineData("1000001", "is not a whole number 1-1000000")]
    [InlineData("-1", "is not a whole number 1-1000000")]
    [InlineData("1.5", "carry 1.5 is not a whole number")]
    [InlineData("1e400", "is not a whole number")] // beyond a double: NaN
    [InlineData("\"15\"", "wrong type of value")]
    [InlineData("true", "wrong type of value")]
    [InlineData("[15]", "wrong type of value")]
    [InlineData("{\"amount\": 15}", "wrong type of value")]
    [InlineData("null", "null is not a carry")]
    public void ABrokenCarry_IsOneErrorAtIt(string json, string text) =>
        AssertOneErrorAt(LoadWith(Gatherer, json), $"units[{RowOf(Gatherer)}].carry", text);

    [Fact]
    public void ACarry_OnAUnitThatIsNotAWorker_IsOneErrorAtIt() =>
        AssertOneErrorAt(LoadWith(Warrior, "15"), $"units[{RowOf(Warrior)}].carry", "only a worker carries");

    /// <summary>
    /// Rule 49 (no literal): data without the field hashes as the shipped data when the shipped data carries none (and in
    /// any case as a second copy without it); a file carrying it, at any value (the rules' own too), hashes differently,
    /// and two values differ.
    /// </summary>
    [Fact]
    public void ACarry_InTheFiles_ChangesTheHash_AbsentDoesNot()
    {
        ulong Hash(string? json)
        {
            DataLoadResult r = LoadWith(Gatherer, json);
            Assert.True(r.Ok, string.Join("; ", r.Errors));
            return r.Data!.ContentHash();
        }
        ulong absent = Hash(null);
        Assert.Equal(absent, Hash(null));
        if (TestSim.Data.Units.All(u => u.Carry == 0)) Assert.Equal(TestSim.Data.ContentHash(), absent);
        ulong fifteen = Hash("15"), rules = Hash(TestSim.Data.Rules.WorkerCarry.ToString());
        Assert.NotEqual(absent, fifteen);
        Assert.NotEqual(absent, rules);
        Assert.NotEqual(fifteen, rules);
    }
}
