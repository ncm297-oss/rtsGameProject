using Rts.Sim.Data;

namespace Rts.Sim.Tests.QA;

/// <summary>QA M3-4 (2026-10-07-0925): <c>trainedAt</c> resolution edges: blank, missing, wrong case, a type of the other faction's matching slot, and the resolved table.</summary>
public class TrainedAtLoaderQaTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Malazan_Barracks")] // ids are case-sensitive
    [InlineData("whirlwind_raider_camp")] // the same slot of the other faction
    public void ABadTrainedAt_IsRejected_AtTheField(string trainedAt)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/malazan/units.json", root => root["units"]![1]!["trainedAt"] = trainedAt);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.Null(r.Data);
        Assert.Contains(r.Errors, e => e.File == "factions/malazan/units.json" && e.Path == "units[1].trainedAt");
    }

    [Fact]
    public void AMissingTrainedAt_IsRejected_Once()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/malazan/units.json", root => root["units"]![1]!.AsObject().Remove("trainedAt"));
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.Null(r.Data);
        DataError e = Assert.Single(r.Errors);
        Assert.Equal("units[1].trainedAt", e.Path);
    }

    [Fact]
    public void ShippedData_EveryUnitTrainsAtAnOwnFactionBuilding_AndTheTableIsItsInverse()
    {
        GameData d = TestSim.Data;
        Assert.Equal(14, d.Units.Length);
        for (int id = 0; id < d.Units.Length; id++)
        {
            UnitDef u = d.Units[id];
            Assert.InRange(u.TrainedAtTypeId, 0, d.Buildings.Length - 1);
            Assert.Equal(u.TrainedAt, d.Buildings[u.TrainedAtTypeId].Key);
            Assert.Equal(u.Faction, d.Buildings[u.TrainedAtTypeId].Faction);
            Assert.Contains(id, d.UnitsTrainedAt(u.TrainedAtTypeId));
        }
        int listed = 0;
        for (int b = 0; b < d.Buildings.Length; b++)
        {
            var list = d.UnitsTrainedAt(b);
            listed += list.Length;
            for (int k = 1; k < list.Length; k++) Assert.True(list[k - 1] < list[k]);
            foreach (int id in list) Assert.Equal(b, d.Units[id].TrainedAtTypeId);
        }
        Assert.Equal(d.Units.Length, listed);
        Assert.True(d.UnitsTrainedAt(-1).IsEmpty);
        Assert.True(d.UnitsTrainedAt(d.Buildings.Length).IsEmpty);
    }
}
