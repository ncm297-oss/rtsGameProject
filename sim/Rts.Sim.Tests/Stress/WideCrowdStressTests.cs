using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// M7-5 (BUG-1222): how a crowd of wide units walks the crowd sweep's maps (<see cref="CrowdRowSweepStressTests"/>: 500
/// units to 4 points on seeds 1-40, 128 units to 64 goals on seeds 41-80, one player a point or goal, combat off). Every
/// unit is one fixture type: on <see cref="TestSim.TwoFactionData"/> at its shipped radius, then on a two-faction copy whose
/// <c>units.json</c> gives it the fields in <c>Stress/Fixtures/wide_crowd_unit.json</c> (the radius of the Teblor's scaled
/// units, 0.75 m). Only the radius differs between the two runs of a map. A report: it prints arrivals and give-ups and
/// asserts no bound (studio rule 48); the bounded rows stay on the two factions' mixed types.
/// </summary>
[Collection(SerialCollection.Name)]
public class WideCrowdStressTests(ITestOutputHelper output)
{
    private sealed record Fixture(GameData Wide, int UnitType);

    private static readonly Lazy<Fixture> s_fixture = new(Build);

    private static Fixture Build()
    {
        string path = Path.Combine(TestDataDir.RepoRoot(), "sim", "Rts.Sim.Tests", "Stress", "Fixtures", "wide_crowd_unit.json");
        JsonObject spec = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        string faction = (string)spec["faction"]!, unit = (string)spec["unit"]!;
        using TestDataDir dir = TestDataDir.CopyOfShippedTwoFactions();
        foreach (KeyValuePair<string, JsonNode?> field in spec["fields"]!.AsObject())
            dir.SetUnitField(faction, unit, field.Key, field.Value!.ToJsonString());
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        GameData wide = r.Data!;
        int type = wide.FindUnit(unit);
        Assert.True(type >= 0 && type < TestSim.ScenarioUnitTypes, unit);
        Assert.Equal(type, TestSim.TwoFactionData.FindUnit(unit)); // the same id on both data sets
        Assert.NotEqual(TestSim.TwoFactionData.Units[type].Radius, wide.Units[type].Radius); // precondition: the fixture changes the radius
        return new Fixture(wide, type);
    }

    /// <summary>Runs <paramref name="run"/> on every seed with the shipped and the wide data; prints each map and the min / mean / max.</summary>
    private void Report(string row, int units, ulong from, ulong to, Func<ulong, GameData, int, CrowdRows.Result> run)
    {
        Fixture f = s_fixture.Value;
        GameData[] data = { TestSim.TwoFactionData, f.Wide };
        var arrived = new[] { new List<int>(), new List<int>() };
        var gaveUp = new[] { new List<int>(), new List<int>() };
        var moving = new int[2];
        for (ulong seed = from; seed <= to; seed++)
        {
            string line = $"seed {seed}:";
            for (int k = 0; k < 2; k++)
            {
                CrowdRows.Result r = run(seed, data[k], f.UnitType);
                arrived[k].Add(r.Arrived);
                gaveUp[k].Add(r.GaveUp);
                moving[k] += r.StillMoving;
                line += $" {data[k].Units[f.UnitType].Radius:0.##} m: arrived {r.Arrived}, gave up {r.GaveUp}, moving {r.StillMoving}, {r.Ticks} ticks;";
            }
            output.WriteLine(line);
        }
        for (int k = 0; k < 2; k++)
            output.WriteLine($"{row}, seeds {from}-{to}, {units} x {data[k].Units[f.UnitType].Key} at {data[k].Units[f.UnitType].Radius:0.##} m: " +
                             $"arrived min {arrived[k].Min()}, mean {arrived[k].Average():F1}, max {arrived[k].Max()}; " +
                             $"gave up min {gaveUp[k].Min()}, mean {gaveUp[k].Average():F1}, max {gaveUp[k].Max()}; still moving (all maps) {moving[k]}");
    }

    /// <summary>500 units to 4 close points, one player a point, seeds 1-40 (the sweep's maps), at the shipped and the fixture's radius.</summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void FiveHundredToFourPoints_Seeds1To40_ShippedVsWideRadius_Report() =>
        Report("500 to 4 points", 500, 1, 40,
            (seed, d, type) => CrowdRows.ToFourPoints(seed, 500, 3000, onePlayerPerPoint: true, combat: false, data: d, unitType: type));

    /// <summary>128 units to 64 goals, one player a goal, seeds 41-80 (the sweep's maps), at the shipped and the fixture's radius.</summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void MoreGoalsThanCacheSlots_Seeds41To80_ShippedVsWideRadius_Report() =>
        Report("128 to 64 goals", 128, 41, 80,
            (seed, d, type) => CrowdRows.MoreGoalsThanCacheSlots(seed, combat: false, data: d, unitType: type));
}
