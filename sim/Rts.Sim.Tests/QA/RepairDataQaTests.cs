using System.Text.Json.Nodes;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests.QA;

/// <summary>QA (M3-3, session 2026-10-06-2114): the <c>rules.json</c> repair block at the edges of what the loader accepts, and how such data plays.</summary>
public class RepairDataQaTests
{
    private readonly ITestOutputHelper _out;

    public RepairDataQaTests(ITestOutputHelper output) => _out = output;

    private static DataLoadResult LoadWith(string field, string json)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("common/rules.json", r => r["repair"]![field] = JsonNode.Parse(json));
        return DataLoader.LoadAll(dir.Path);
    }

    [Theory]
    [InlineData("rateFactor", "\"0.5\"")]
    [InlineData("rateFactor", "true")]
    [InlineData("costFactor", "null")]
    [InlineData("costFactor", "{}")]
    public void WrongJsonTypes_AreRejected_NotDefaulted(string field, string json)
    {
        DataLoadResult r = LoadWith(field, json);
        _out.WriteLine(string.Join("; ", r.Errors.Select(e => $"{e.Path}: {e.Message}")));
        Assert.NotEmpty(r.Errors);
        Assert.Contains(r.Errors, e => e.Path.Contains("repair"));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("0.0001")]
    public void FactorsAtTheEdgesOfTheRange_Load(string json)
    {
        DataLoadResult r = LoadWith("rateFactor", json);
        Assert.Empty(r.Errors);
    }

    /// <summary>
    /// A rate factor below 2^-17 rounded to 0 in the 2^16 fixed point: a repair that restored nothing and cost nothing,
    /// its workers standing "repairing" for ever. M3-H1 (BUG-0092): the loader refuses factors below 2^-16.
    /// </summary>
    [Fact]
    public void ATinyRateFactor_IsRefusedByTheLoader()
    {
        DataLoadResult load = LoadWith("rateFactor", "0.000001");
        _out.WriteLine(string.Join("; ", load.Errors.Select(e => $"{e.Path}: {e.Message}")));
        Assert.Contains(load.Errors, e => e.Path == "repair.rateFactor");
    }
}
