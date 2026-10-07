using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M3-V1: <see cref="BuildingBars"/>, the progress bar over a site and the hit-point bar over a damaged building.</summary>
public class BuildingBarsTests
{
    [Fact]
    public void AFinishedBuilding_HasNoBar_UntilDamaged_ThenItsHpShare()
    {
        Simulation sim = BuildMaps.NewSim(ResourceMaps.Flat(30, 20));
        EntityHandle keep = GatherMaps.Building(sim, 10, 10);
        World w = sim.World;
        Assert.Equal(BuildingBarKind.None, ViewReads.Bar(w, keep.Index, out float fill));
        Assert.Equal(0f, fill);
        int max = w.Data.Buildings[GatherMaps.Keep].Hp;
        w.Buildings.Damage(keep, max / 4);
        Assert.Equal(BuildingBarKind.HitPoints, ViewReads.Bar(w, keep.Index, out fill));
        Assert.Equal(0.75f, fill, 4);
        w.Buildings.Damage(keep, max); // destroyed
        Assert.Equal(BuildingBarKind.None, ViewReads.Bar(w, keep.Index, out fill));
        Assert.Equal(0f, fill);
    }

    [Fact]
    public void ASite_ShowsProgress_GrowingWithWork_ThenNoBarWhenDone()
    {
        Simulation sim = BuildMaps.NewSim(ResourceMaps.Flat(30, 20));
        EntityHandle worker = GatherMaps.Unit(sim, GatherMaps.At(sim, 9, 9));
        BuildMaps.SetTotals(sim, 0, 1000, 1000);
        int house = BuildMaps.House;
        sim.Enqueue(Command.Build(0, worker, house, GatherMaps.At(sim, 10, 10)));
        GatherMaps.Run(sim, 2);
        int k = BuildMaps.SiteAt(sim, 10, 10);
        Assert.True(k >= 0);
        World w = sim.World;
        Assert.Equal(BuildingBarKind.Progress, ViewReads.Bar(w, k, out float first));
        float last = first;
        int needed = w.Buildings.WorkNeeded(house);
        for (int t = 0; t < 2000 && w.Buildings.UnderConstruction[k]; t++)
        {
            sim.Tick();
            if (!w.Buildings.UnderConstruction[k]) break;
            Assert.Equal(BuildingBarKind.Progress, ViewReads.Bar(w, k, out float f));
            Assert.Equal((float)w.Buildings.Work[k] / needed, f, 5);
            Assert.True(f >= last);
            last = f;
        }
        Assert.True(last > first && last < 1f);
        Assert.False(w.Buildings.UnderConstruction[k]);
        Assert.Equal(BuildingBarKind.None, ViewReads.Bar(w, k, out _));
    }

    [Fact]
    public void DeadAndOutOfRangeSlots_HaveNoBar()
    {
        World w = BuildMaps.NewSim(ResourceMaps.Flat(30, 20)).World;
        Assert.Equal(BuildingBarKind.None, ViewReads.Bar(w, 0, out float f));
        Assert.Equal(0f, f);
        Assert.Equal(BuildingBarKind.None, ViewReads.Bar(w, -1, out _));
        Assert.Equal(BuildingBarKind.None, ViewReads.Bar(w, w.Buildings.Capacity, out _));
    }
}
