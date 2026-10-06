using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests;

/// <summary>M3-2 criterion 4: the gather / return loop on hand maps: fill time, conservation, deposit, return, income.</summary>
public class GatherLoopTests
{
    private readonly ITestOutputHelper _out;

    public GatherLoopTests(ITestOutputHelper output) => _out = output;

    // 32 x 20 flat map: a Garrison Keep on cells 8-11 x 7-10 (x 16-24 m) and a 2 x 2 mine on cells 16-17 x 8-9
    // (x 32-36 m): 8 m between them.
    private const int KeepX = 8, KeepY = 7, MineX = 16, MineY = 8;

    private static (Simulation Sim, EntityHandle Mine) KeepAndMine(int workers, out EntityHandle[] w, int mineGold = 2500)
    {
        Simulation sim = GatherMaps.NewSim(Flat(32, 20), units: 32);
        EntityHandle mine = Spawn(sim.World, Mine, MineX, MineY, mineGold);
        Building(sim, KeepX, KeepY);
        w = new EntityHandle[workers];
        for (int k = 0; k < workers; k++)
            w[k] = Unit(sim, At(sim, 13, 6 + k % 8) + new Vector2(k / 8 * 0.5f - 0.5f, 0f));
        return (sim, mine);
    }

    private static int InReachTicks(Simulation sim, EntityHandle worker, EntityHandle mine)
    {
        NavGrid g = sim.World.NavGrid;
        float d = DistanceToFootprint(g, sim.World.Units.Position[worker.Index], sim.World.Resources.Cell[mine.Index], 2, 2);
        return d <= EconomyConstants.Reach ? 1 : 0;
    }

    [Fact]
    public void OneWorkerOneMine_FillsAtTheDataRate_Deposits_AndGoesBackUnordered()
    {
        (Simulation sim, EntityHandle mine) = KeepAndMine(1, out EntityHandle[] w);
        UnitStore u = sim.World.Units;
        int i = w[0].Index;
        sim.Enqueue(Command.Gather(0, w[0], At(sim, MineX + 1, MineY)));
        int inReach = 0, firstFull = -1;
        for (int t = 0; t < 2000 && firstFull < 0; t++)
        {
            sim.Tick();
            if (u.State[i] == UnitState.Gathering) inReach++;
            if (u.Cargo[i] == 10) firstFull = t;
        }
        Assert.True(firstFull > 0, "never filled");
        int expected = (int)MathF.Ceiling(10f / GoldPerTick); // 14.3 s = 286 ticks
        _out.WriteLine($"filled after {inReach} ticks in reach (expected {expected}); remaining {sim.World.Resources.Remaining[mine.Index]}");
        Assert.InRange(inReach, expected - 1, expected + 1);
        Assert.Equal(2500 - 10, sim.World.Resources.Remaining[mine.Index]);
        Assert.Equal(200, sim.World.Gold[0]);

        int deposited = -1;
        for (int t = 0; t < 400 && deposited < 0; t++)
        {
            sim.Tick();
            if (sim.World.Gold[0] == 210) deposited = t;
        }
        Assert.True(deposited > 0, "never deposited");
        Assert.Equal(0, u.Cargo[i]);
        int back = -1;
        for (int t = 0; t < 400 && back < 0; t++)
        {
            sim.Tick();
            if (u.State[i] == UnitState.Gathering && InReachTicks(sim, w[0], mine) == 1) back = t;
        }
        _out.WriteLine($"deposited {deposited} ticks after filling, back at the mine {back} ticks later");
        Assert.True(back > 0, "never back in reach of the mine");
        Assert.True(EconomySystem.OnLoop(u, i));
    }

    [Fact]
    public void Income_OneWorkerIn120s_AndFiveWorkersOnOneMine()
    {
        int one = Income(1), five = Income(5);
        _out.WriteLine($"120 s: one worker +{one} gold, five +{five}");
        Assert.True(one >= 50, $"one worker +{one}");
        Assert.True(five >= 4 * one, $"five workers +{five}, one +{one}");
    }

    private static int Income(int workers)
    {
        (Simulation sim, _) = KeepAndMine(workers, out EntityHandle[] w);
        foreach (EntityHandle h in w) sim.Enqueue(Command.Gather(0, h, At(sim, MineX, MineY)));
        Run(sim, 120 * SimConstants.TicksPerSecond);
        return sim.World.Gold[0] - 200;
    }

    [Fact]
    public void WoodLoad_FillsIn16Point7Seconds()
    {
        Simulation sim = GatherMaps.NewSim(Flat(32, 20));
        EntityHandle tree = Spawn(sim.World, Tree, 16, 9, TreeWood);
        Building(sim, KeepX, KeepY);
        EntityHandle w = Unit(sim, At(sim, 15, 9));
        sim.Enqueue(Command.Gather(0, w, At(sim, 16, 9)));
        UnitStore u = sim.World.Units;
        int inReach = 0;
        for (int t = 0; t < 1000 && u.Cargo[w.Index] < 10; t++)
        {
            sim.Tick();
            if (u.State[w.Index] == UnitState.Gathering) inReach++;
        }
        int expected = (int)MathF.Ceiling(10f / TestSim.Data.Rules.WoodPerTick); // 16.7 s = 334 ticks
        Assert.InRange(inReach, expected - 1, expected + 1);
        Assert.Equal(TreeWood - 10, sim.World.Resources.Remaining[tree.Index]);
        Assert.Equal(ResourceKind.Wood, u.CargoKind[w.Index]);
    }

    [Fact]
    public void ConservationHoldsEveryTick_AndNoRemainingGoesNegative()
    {
        (Simulation sim, _) = KeepAndMine(6, out EntityHandle[] w, mineGold: 95);
        Spawn(sim.World, Tree, 20, 4, TreeWood);
        Spawn(sim.World, Tree, 22, 12, 7);
        for (int k = 0; k < w.Length; k++)
            sim.Enqueue(Command.Gather(0, w[k], k % 2 == 0 ? At(sim, MineX, MineY) : At(sim, 22, 12)));
        (long gold, long wood) = Conserved(sim.World);
        for (int t = 0; t < 4000; t++)
        {
            sim.Tick();
            Assert.Equal((gold, wood), Conserved(sim.World));
            for (int n = 0; n < sim.World.Resources.Capacity; n++)
                Assert.True(!sim.World.Resources.Alive[n] || sim.World.Resources.Remaining[n] > 0);
        }
        // The whole mine is out: deposited, or kept as cargo by a worker whose mine ran out with no other in range.
        int carried = 0;
        foreach (EntityHandle h in w)
            if (sim.World.Units.CargoKind[h.Index] == ResourceKind.Gold) carried += sim.World.Units.Cargo[h.Index];
        Assert.Equal(200 + 95, sim.World.Gold[0] + carried);
        Assert.True(sim.World.Gold[0] >= 200 + 80, $"gold {sim.World.Gold[0]}");
    }
}
