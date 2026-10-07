using Rts.Sim.Data;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M3-V2: <see cref="BuildMenu"/>, a worker build menu's entries from a slot list.</summary>
[Collection(SerialCollection.Name)]
public class BuildMenuTests
{
    private static readonly BuildingSlot[] Basic =
        { BuildingSlot.House, BuildingSlot.Camp, BuildingSlot.InfantryHall, BuildingSlot.RangedHall, BuildingSlot.ShockHall, BuildingSlot.Forge };
    private static readonly BuildingSlot[] Advanced = { BuildingSlot.CasterHall, BuildingSlot.SiegeWorks, BuildingSlot.WatchTower };

    private readonly ITestOutputHelper _out;

    public BuildMenuTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Entries_AreTheFactionsBuildingOfEachSlot_InListOrder_BothFactions()
    {
        GameData data = TestSim.Data;
        var types = new int[15];
        for (int f = 0; f < data.Factions.Length; f++)
        {
            foreach (BuildingSlot[] list in new[] { Basic, Advanced })
            {
                int n = BuildMenu.Entries(data.Buildings, f, list, types);
                Assert.Equal(list.Length, n);
                for (int k = 0; k < n; k++)
                {
                    BuildingDef def = data.Buildings[types[k]];
                    Assert.True(def.Faction == f && def.Slot == list[k], $"faction {f} entry {k}: {def.Key}");
                    Assert.Equal(StartBase.BuildingOfSlot(data, f, list[k]), types[k]);
                }
            }
        }
        // Malazan, by name: the six Age I buildings of docs/factions/malazan.md in slot order.
        int m = BuildMenu.Entries(data.Buildings, 0, Basic, types);
        string[] keys = types.Take(m).Select(t => data.Buildings[t].Key).ToArray();
        Assert.Equal(new[] { "malazan_billet", "malazan_depot", "malazan_barracks", "malazan_crossbow_range", "malazan_wickan_corral", "malazan_armory" }, keys);
    }

    [Fact]
    public void Entries_SkipsMissingSlotsAndStopsAtTheSpan()
    {
        GameData data = TestSim.Data;
        var two = new int[2];
        Assert.Equal(2, BuildMenu.Entries(data.Buildings, 0, Basic, two));
        Assert.Equal(StartBase.BuildingOfSlot(data, 0, BuildingSlot.Camp), two[1]);
        var types = new int[15];
        Assert.Equal(0, BuildMenu.Entries(data.Buildings, 99, Basic, types)); // no such faction: nothing
        Assert.Equal(0, BuildMenu.Entries(data.Buildings, 0, ReadOnlySpan<BuildingSlot>.Empty, types));
    }

    [Fact]
    public void TryParseSlot_KnowsEveryJsonSlotId()
    {
        for (int i = 0; i < DataLimits.BuildingSlotIds.Length; i++)
        {
            Assert.True(BuildMenu.TryParseSlot(DataLimits.BuildingSlotIds[i], out BuildingSlot s));
            Assert.Equal((BuildingSlot)i, s);
        }
        Assert.False(BuildMenu.TryParseSlot("smithy", out _));
        Assert.False(BuildMenu.TryParseSlot("", out _));
        Assert.False(BuildMenu.TryParseSlot("House", out _));
    }

    [Fact]
    public void Entries_AllocatesZeroBytes()
    {
        GameData data = TestSim.Data;
        var types = new int[15];
        BuildingSlot[] basic = Basic;
        long sum = 0;
        Action block = () =>
        {
            for (int i = 0; i < 100; i++) sum += BuildMenu.Entries(data.Buildings, i & 1, basic, types);
        };
        block();
        int runs = AllocationProbe.AssertZero(block, _out);
        _out.WriteLine($"BuildMenu: 0 bytes (runs {runs}), checksum {sum}");
    }
}
