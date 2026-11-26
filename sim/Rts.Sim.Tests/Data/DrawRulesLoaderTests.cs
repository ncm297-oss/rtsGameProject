using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-S13 criterion 1: <c>rules.json</c> <c>draw.afterSeconds</c>. 300 s loads as 6,000 ticks; the block left out is no rule;
/// zero, a negative, a non-number, a value over <see cref="DataLimits.MaxDrawSeconds"/> and the field missing in the block are
/// one load error each at the field's path; the value is in the content hash only when the block is there. (The JSON
/// <c>null</c> rows are in <see cref="DataNullPolicyTests"/>.)
/// </summary>
public class DrawRulesLoaderTests
{
    private readonly ITestOutputHelper _out;

    public DrawRulesLoaderTests(ITestOutputHelper output) => _out = output;

    private static DataLoadResult LoadWith(Action<JsonObject> editRules)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("common/rules.json", editRules);
        return DataLoader.LoadAll(dir.Path);
    }

    [Fact]
    public void TheShippedRules_Carry300Seconds_As6000Ticks()
    {
        // The shipped value read from the file, then its ticks from the loader's own conversion (no literal of the rule in C#).
        JsonObject rules = JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.Shipped, "common", "rules.json")))!.AsObject();
        double seconds = (double)rules["draw"]!["afterSeconds"]!;
        Assert.Equal(300, seconds); // docs/01 2026-11-25-1437 (3)
        Assert.Equal(DataLoader.SecondsToTicks(seconds), TestSim.Data.Rules.DrawAfterTicks);
        Assert.Equal(6000, TestSim.Data.Rules.DrawAfterTicks);
    }

    [Fact]
    public void TheBlockLeftOut_IsNoRule()
    {
        DataLoadResult r = LoadWith(j => j.Remove("draw"));
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Equal(0, r.Data!.Rules.DrawAfterTicks);
    }

    [Theory]
    [InlineData("0", "must be positive")]
    [InlineData("-5", "must be positive")]
    [InlineData("\"300\"", "expected a number")]
    [InlineData("true", "expected a number")]
    [InlineData("{}", "expected a number")]
    [InlineData("86401", "above the maximum")]
    [InlineData("1e9", "above the maximum")]
    [InlineData("0.01", "rounds to 0 ticks")]
    public void ABadWait_IsOneLoadError_NamingTheField(string raw, string message)
    {
        DataLoadResult r = LoadWith(j => j["draw"] = JsonNode.Parse($"{{ \"afterSeconds\": {raw} }}"));
        DataError e = Assert.Single(r.Errors);
        _out.WriteLine(e.ToString());
        Assert.Equal("common/rules.json", e.File);
        Assert.Equal("draw.afterSeconds", e.Path);
        Assert.Contains(message, e.Message);
    }

    [Fact]
    public void TheFieldMissingInTheBlock_IsOneLoadError()
    {
        DataLoadResult r = LoadWith(j => j["draw"] = new JsonObject());
        DataError e = Assert.Single(r.Errors);
        Assert.Equal("draw.afterSeconds", e.Path);
        Assert.Contains("missing", e.Message);
    }

    [Fact]
    public void TheCap_IsAGameDay_AndLoads()
    {
        Assert.True(DataLimits.MaxDrawSeconds >= 86_400);
        DataLoadResult r = LoadWith(j => j["draw"] = new JsonObject { ["afterSeconds"] = DataLimits.MaxDrawSeconds });
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Equal(DataLoader.SecondsToTicks(DataLimits.MaxDrawSeconds), r.Data!.Rules.DrawAfterTicks);
    }

    /// <summary>The wait is part of the data hash: the block removed, and each other value, hash differently; the same value hashes the same.</summary>
    [Fact]
    public void TheWait_IsInTheContentHash()
    {
        DataLoadResult off = LoadWith(j => j.Remove("draw"));
        DataLoadResult other = LoadWith(j => j["draw"] = new JsonObject { ["afterSeconds"] = 301 });
        DataLoadResult same = LoadWith(_ => { });
        Assert.NotEqual(TestSim.Data.ContentHash(), off.Data!.ContentHash());
        Assert.NotEqual(TestSim.Data.ContentHash(), other.Data!.ContentHash());
        Assert.NotEqual(off.Data.ContentHash(), other.Data.ContentHash());
        Assert.Equal(TestSim.Data.ContentHash(), same.Data!.ContentHash());
    }
}
