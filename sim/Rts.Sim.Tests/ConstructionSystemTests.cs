using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using static Rts.Sim.Tests.BuildMaps;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests;

/// <summary>M3-3 criteria 2-4 and 6-8: placing, building with several workers, joining, cancelling, and the damage seam.</summary>
[Collection(SerialCollection.Name)]
public class ConstructionSystemTests
{
    [Fact]
    public void AValidBuild_PaysAtApply_PlacesASiteAtLowHp_AndBumpsBlockVersionOnce()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20));
        EntityHandle w = Unit(sim, At(sim, 5, 5));
        SetTotals(sim, 0, 300, 300);
        NavGrid g = sim.World.NavGrid;
        int block = g.BlockVersion, version = g.Version;
        sim.Enqueue(Command.Build(0, w, Keep, At(sim, 10, 10)));
        sim.Tick();
        Assert.Equal(300, sim.World.Gold[0]); // not yet: commands apply on the next tick
        sim.Tick();
        int k = SiteAt(sim, 10, 10);
        BuildingStore b = sim.World.Buildings;
        Assert.True(k >= 0 && b.UnderConstruction[k]);
        Assert.Equal((25, 25), (sim.World.Gold[0], sim.World.Wood[0]));
        Assert.Equal(1, b.Hp[k]);
        Assert.Equal((block + 1, version + 1), (g.BlockVersion, g.Version));
        Assert.Equal(b.HandleOf(k), sim.World.Units.BuildTarget[w.Index]);
        Assert.Equal(UnitState.Moving, sim.World.Units.State[w.Index]);
    }

    [Theory]
    [InlineData(274, 300)]
    [InlineData(300, 274)]
    public void AnUnaffordableBuild_IsDroppedWithNoSideEffect(int gold, int wood)
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20));
        EntityHandle w = Unit(sim, At(sim, 5, 5));
        SetTotals(sim, 0, gold, wood);
        sim.Tick();
        Simulation twin = BuildMaps.NewSim(Flat(30, 20));
        Unit(twin, At(twin, 5, 5));
        SetTotals(twin, 0, gold, wood);
        twin.Tick();
        sim.Enqueue(Command.Build(0, w, Keep, At(sim, 10, 10)));
        twin.Enqueue(Command.Noop(0));
        Run(sim, 2);
        Run(twin, 2);
        Assert.Equal(0, sim.World.Buildings.Count);
        Assert.Equal((gold, wood), (sim.World.Gold[0], sim.World.Wood[0]));
        Assert.Equal(twin.StateHash(), sim.StateHash()); // a Noop and a dropped Build leave the same state
    }

    /// <summary>Criterion 3: a 20 s House with n builders in reach from the first tick completes after ceil(3 x 400 / (n + 2)) ticks of work.</summary>
    [Theory]
    [InlineData(1, 400)]
    [InlineData(2, 300)]
    [InlineData(4, 200)]
    [InlineData(8, 120)]
    public void NBuilders_FinishAHouse_InTheDocs02Time(int n, int expected)
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20));
        BuildingDef house = TestSim.Data.Buildings[House];
        Assert.Equal(400, house.BuildTicks);
        EntityHandle[] ws = WorkersRound(sim, 10, 10, 2, 2, n);
        foreach (EntityHandle w in ws) sim.Enqueue(Command.Build(0, w, House, At(sim, 10, 10)));
        sim.Tick(); // queued for the next tick
        BuildingStore b = sim.World.Buildings;
        int ticks = 0, lastHp = 0, k = -1;
        while (k < 0 || b.UnderConstruction[k])
        {
            sim.Tick();
            k = SiteAt(sim, 10, 10);
            Assert.True(b.Hp[k] >= lastHp && b.Hp[k] >= 1, $"hp went {lastHp} -> {b.Hp[k]}");
            lastHp = b.Hp[k];
            ticks++;
            Assert.True(ticks <= 500);
        }
        Assert.Equal(expected, ticks);
        Assert.Equal(house.Hp, b.Hp[k]);
        Assert.All(ws, w => Assert.Equal(UnitState.Idle, sim.World.Units.State[w.Index]));
        Assert.All(ws, w => Assert.Equal(default, sim.World.Units.BuildTarget[w.Index]));
        Assert.Equal(150, sim.World.Wood[0]); // paid once: 200 - 50
    }

    [Fact]
    public void FiveWorkersOneAnchorOneTick_PlaceOneSite_PayOnce_AndAllBuildIt()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20));
        EntityHandle[] ws = Enumerable.Range(0, 5).Select(i => Unit(sim, At(sim, 3 + i, 3))).ToArray();
        foreach (EntityHandle w in ws) sim.Enqueue(Command.Build(0, w, House, At(sim, 12, 10)));
        Run(sim, 2);
        Assert.Equal(1, sim.World.Buildings.Count);
        Assert.Equal(150, sim.World.Wood[0]);
        EntityHandle site = sim.World.Buildings.HandleOf(SiteAt(sim, 12, 10));
        Assert.All(ws, w => Assert.Equal(site, sim.World.Units.BuildTarget[w.Index]));
        Run(sim, 200);
        Assert.Equal(5, ws.Count(w => sim.World.Units.State[w.Index] == UnitState.Building));
        Assert.True(sim.World.Buildings.Work[site.Index] > 0);

        // A Build of another type on the site's anchor is dropped: the worker keeps building.
        EntityHandle extra = Unit(sim, At(sim, 3, 15));
        sim.Enqueue(Command.Build(0, ws[0], Keep, At(sim, 12, 10)));
        sim.Enqueue(Command.Build(0, extra, TestSim.Data.FindBuilding("malazan_depot"), At(sim, 12, 10)));
        Run(sim, 2);
        Assert.Equal(site, sim.World.Units.BuildTarget[ws[0].Index]);
        Assert.Equal(default, sim.World.Units.BuildTarget[extra.Index]);
        Assert.Equal(1, sim.World.Buildings.Count);
    }

    [Fact]
    public void AnotherOrder_EndsBuilding_AndAQueuedBuildStartsWhenIdle()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20));
        EntityHandle w = WorkersRound(sim, 10, 10, 2, 2, 1)[0];
        sim.Enqueue(Command.Build(0, w, House, At(sim, 10, 10)));
        Run(sim, 5);
        UnitStore u = sim.World.Units;
        Assert.Equal(UnitState.Building, u.State[w.Index]);
        sim.Enqueue(Command.Stop(0, w));
        sim.Enqueue(Command.Build(0, w, House, At(sim, 20, 10), queued: true));
        Run(sim, 2); // the Stop leaves it Idle, so phase 7 of the same tick starts the queued Build
        Assert.Equal(0, u.QueueCount[w.Index]);
        Assert.Equal(sim.World.Buildings.HandleOf(SiteAt(sim, 20, 10)), u.BuildTarget[w.Index]);
        Assert.Equal(100, sim.World.Wood[0]);
    }

    /// <summary>Criterion 6: Cancel refunds floor(cost x unbuilt fraction), reopens the cells (an opening change) and idles the builders.</summary>
    [Theory]
    [InlineData(0, 275)]
    [InlineData(2700, 137)]  // half of 5,400: floor(137.5)
    [InlineData(5399, 0)]    // one short of done: floor(275 / 5,400)
    public void Cancel_RefundsTheUnbuiltFraction_FreesTheCells_AndIdlesTheBuilders(int work, int refund)
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20));
        EntityHandle w = WorkersRound(sim, 10, 10, 4, 4, 1)[0];
        SetTotals(sim, 0, 275, 275);
        sim.Enqueue(Command.Build(0, w, Keep, At(sim, 10, 10)));
        Run(sim, 2);
        int k = SiteAt(sim, 10, 10);
        BuildingStore b = sim.World.Buildings;
        Assert.Equal(5400, b.WorkNeeded(Keep));
        NavGrid g = sim.World.NavGrid;
        sim.Enqueue(Command.Cancel(0, At(sim, 12, 11))); // any cell of the footprint
        sim.Tick();
        b.SetWork(k, work); // just before the tick the Cancel applies in
        int block = g.BlockVersion, version = g.Version;
        sim.Tick();
        Assert.False(b.Alive[k]);
        Assert.Equal((refund, refund), (sim.World.Gold[0], sim.World.Wood[0]));
        Assert.Equal((block, version + 1), (g.BlockVersion, g.Version));
        Assert.True(g.IsPassable(11, 11));
        Assert.Equal(UnitState.Idle, sim.World.Units.State[w.Index]);
        Assert.Equal(default, sim.World.Units.BuildTarget[w.Index]);
    }

    [Fact]
    public void Cancel_OnAFinishedBuildingOrAnEnemySite_IsDropped()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20), players: 2);
        Building(sim, 3, 3);
        EntityHandle enemy = Unit(sim, At(sim, 20, 5), player: 1);
        sim.Enqueue(Command.Build(1, enemy, WhirlwindHouse, At(sim, 20, 10)));
        Run(sim, 2);
        int wood = sim.World.Wood[0];
        sim.Enqueue(Command.Cancel(0, At(sim, 4, 4)));
        sim.Enqueue(Command.Cancel(0, At(sim, 20, 10)));
        sim.Enqueue(Command.Cancel(0, At(sim, 25, 15))); // nothing there
        Run(sim, 2);
        Assert.Equal(2, sim.World.Buildings.Count);
        Assert.Equal(wood, sim.World.Wood[0]);
    }

    /// <summary>Criterion 8: a building damaged to 0 is freed; its cells reopen as an opening change, so a field cached before stays usable.</summary>
    [Fact]
    public void DamageToZero_FreesTheBuilding_AsAnOpeningChange()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20));
        EntityHandle keep = Building(sim, 10, 8);
        EntityHandle w = Unit(sim, At(sim, 3, 3));
        sim.Enqueue(Command.Move(0, w, At(sim, 25, 10)));
        Run(sim, 2);
        FlowFieldCache cache = sim.World.FlowFields;
        int goal = Cell(sim, 25, 10);
        Assert.NotNull(cache.PeekCached(goal));
        NavGrid g = sim.World.NavGrid;
        int block = g.BlockVersion, version = g.Version;
        BuildingStore b = sim.World.Buildings;
        b.Damage(keep, 2399);
        Assert.True(b.IsAlive(keep));
        Assert.Equal(1, b.Hp[keep.Index]);
        b.Damage(keep, 5);
        Assert.False(b.IsAlive(keep));
        Assert.Equal((block, version + 1), (g.BlockVersion, g.Version));
        Assert.True(g.IsPassable(11, 9));
        Assert.NotNull(cache.PeekCached(goal));
        b.Damage(keep, 5); // a stale handle: nothing
        Assert.Equal(version + 1, g.Version);
    }
}
