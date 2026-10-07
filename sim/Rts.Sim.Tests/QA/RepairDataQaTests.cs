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
    /// A rate factor the loader accepts (above 0) but below 2^-17 rounds to 0 in the 2^16 fixed point: then a repair
    /// restores nothing, costs nothing, and its workers stand "repairing" for ever. Reports what happens.
    /// </summary>
    [Fact(Skip = "BUG-0092: a repair rateFactor the loader accepts (above 0) but below 2^-17 rounds to 0: workers repair for ever without restoring a hit point; un-skip when fixed")]
    public void ATinyAcceptedRateFactor_Report()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("common/rules.json", r => r["repair"]!["rateFactor"] = JsonNode.Parse("0.000001"));
        DataLoadResult load = DataLoader.LoadAll(dir.Path);
        Assert.Empty(load.Errors);
        GameData data = load.Data!;
        var sim = new Simulation(new SimConfig(5, 1, 16, 64) { Data = data }, Flat(30, 24));
        sim.Enqueue(Command.SpawnBuilding(0, data.FindBuilding("malazan_garrison_keep"), At(sim, 10, 10)));
        sim.Enqueue(Command.SpawnUnit(0, data.FindUnit("malazan_laborer"), At(sim, 9, 11)));
        Run(sim, 2);
        BuildingStore b = sim.World.Buildings;
        b.Damage(b.HandleOf(0), 1000);
        sim.Enqueue(Command.Repair(0, new EntityHandle(0, sim.World.Units.Generation[0]), At(sim, 11, 11)));
        Run(sim, 2000);
        _out.WriteLine($"rateFactor 1e-6: after 100 s hp {b.Hp[0]} / 2400, worker state {sim.World.Units.State[0]}, totals {sim.World.Gold[0]} / {sim.World.Wood[0]}");
        Assert.True(b.Hp[0] > 1400 || sim.World.Units.State[0] != UnitState.Building,
            "an accepted repair rate makes workers repair for ever without restoring a hit point");
    }
}
