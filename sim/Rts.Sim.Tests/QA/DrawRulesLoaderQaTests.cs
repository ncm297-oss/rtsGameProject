using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M6-S13): <c>rules.json</c> <c>draw</c> shapes the developer's loader rows do not hold: the block itself of the wrong JSON
/// type, an unknown key beside <c>afterSeconds</c> (a typo must not silently mean "no rule" or be ignored), a fraction that
/// rounds to one tick, and the cap exactly. Every bad shape must be a load error, never a crash or a silent default.
/// </summary>
public class DrawRulesLoaderQaTests
{
    private readonly ITestOutputHelper _out;

    public DrawRulesLoaderQaTests(ITestOutputHelper output) => _out = output;

    private static DataLoadResult LoadWith(Action<JsonObject> editRules)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("common/rules.json", editRules);
        return DataLoader.LoadAll(dir.Path);
    }

    [Theory]
    [InlineData("300")]
    [InlineData("\"300\"")]
    [InlineData("[300]")]
    [InlineData("true")]
    public void TheBlockOfTheWrongType_IsALoadError(string raw)
    {
        DataLoadResult r = LoadWith(j => j["draw"] = JsonNode.Parse(raw));
        _out.WriteLine($"draw: {raw} -> {string.Join("; ", r.Errors)}");
        Assert.False(r.Ok, $"draw: {raw} loaded (wait {r.Data?.Rules.DrawAfterTicks})");
        Assert.Contains(r.Errors, e => e.File == "common/rules.json");
    }

    [Fact]
    public void ATypoInTheBlock_IsALoadError()
    {
        DataLoadResult r = LoadWith(j => j["draw"] = new JsonObject { ["afterSecond"] = 300 });
        _out.WriteLine(string.Join("; ", r.Errors));
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.Path.Contains("draw.afterSecond", StringComparison.Ordinal)); // the JSON reader's path: "$.draw.afterSecond"
    }

    [Fact]
    public void AnUnknownKeyBesideTheWait_IsALoadError()
    {
        DataLoadResult r = LoadWith(j => j["draw"] = new JsonObject { ["afterSeconds"] = 300, ["afterMinutes"] = 5 });
        _out.WriteLine(string.Join("; ", r.Errors));
        Assert.False(r.Ok, "an unknown key in the draw block was ignored");
    }

    [Theory]
    [InlineData(0.05, 1)]
    [InlineData(0.03, 1)]
    [InlineData(86_400, 1_728_000)]
    public void TheEdgesThatLoad_LoadAsTheirTicks(double seconds, int ticks)
    {
        DataLoadResult r = LoadWith(j => j["draw"] = new JsonObject { ["afterSeconds"] = seconds });
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Equal(ticks, r.Data!.Rules.DrawAfterTicks);
    }
}
