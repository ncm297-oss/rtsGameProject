using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests;

/// <summary>M3-2 criterion 3: the Gather command's apply rules, resolution and shift-queueing.</summary>
public class GatherCommandTests
{
    // 40 x 24 flat map, two players; a mine at (10, 10), a tree at (30, 10).
    private static Simulation Map(out EntityHandle mine, out EntityHandle tree)
    {
        Simulation sim = GatherMaps.NewSim(Flat(40, 24), players: 2);
        mine = Spawn(sim.World, Mine, 10, 10, 2500);
        tree = Spawn(sim.World, Tree, 30, 10, TreeWood);
        return sim;
    }

    [Fact]
    public void OwnWorkerOnALiveNode_IsAccepted_AndWalksToIt()
    {
        Simulation sim = Map(out EntityHandle mine, out _);
        EntityHandle w = Unit(sim, At(sim, 4, 4));
        sim.Enqueue(Command.Gather(0, w, At(sim, 11, 11))); // any point of the 2 x 2 footprint
        Run(sim, 2);
        UnitStore u = sim.World.Units;
        Assert.Equal(mine, u.GatherNode[w.Index]);
        Assert.Equal(UnitState.Moving, u.State[w.Index]);
        Assert.Equal(ResourceKind.Gold, u.CargoKind[w.Index]);
        Assert.Equal(new Vector2(22f, 22f), u.GatherSite[w.Index]); // the footprint's center
    }

    /// <summary>The map with four units: an own worker, another player's worker, an own soldier, and an own worker freed again.</summary>
    private static Simulation FourUnits(out EntityHandle worker, out EntityHandle theirs, out EntityHandle soldier, out EntityHandle dead)
    {
        Simulation sim = Map(out _, out _);
        worker = Unit(sim, At(sim, 4, 4));
        theirs = Unit(sim, At(sim, 4, 6), player: 1);
        soldier = Unit(sim, At(sim, 4, 8), type: Infantry);
        dead = Unit(sim, At(sim, 4, 12));
        sim.World.Units.Free(dead);
        return sim;
    }

    [Fact]
    public void DeadForeignAndNonWorkerUnits_AnEmptyFarCell_AndOffMap_AreDropped_LikeNoops()
    {
        Simulation sim = FourUnits(out EntityHandle worker, out EntityHandle theirs, out EntityHandle soldier, out EntityHandle dead);
        Simulation twin = FourUnits(out _, out _, out _, out _);
        foreach (Command c in new[]
        {
            Command.Gather(0, dead, At(sim, 10, 10)),
            Command.Gather(0, theirs, At(sim, 10, 10)),
            Command.Gather(0, soldier, At(sim, 10, 10)),
            Command.Gather(0, worker, At(sim, 20, 20)), // no node there and none within 20 m
            Command.Gather(0, worker, new Vector2(-5f, 3f)), // off the map
        })
        {
            sim.Enqueue(c);
            twin.Enqueue(Command.Noop(0));
            Run(sim, 2);
            Run(twin, 2);
            Assert.Equal(twin.StateHash(), sim.StateHash());
        }
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
            Assert.False(u.Alive[i] && EconomySystem.OnLoop(u, i), $"slot {i} on a gather loop");
    }

    [Fact]
    public void AnEmptyCellNearANode_GathersThatNode()
    {
        Simulation sim = Map(out EntityHandle mine, out _);
        EntityHandle w = Unit(sim, At(sim, 4, 4));
        sim.Enqueue(Command.Gather(0, w, At(sim, 14, 14))); // open ground about 10 m from the mine's center
        Run(sim, 2);
        Assert.Equal(mine, sim.World.Units.GatherNode[w.Index]);
    }

    [Fact]
    public void ShiftQueuedGather_AfterAMove_RunsWhenTheMoveEnds()
    {
        Simulation sim = Map(out _, out EntityHandle tree);
        EntityHandle w = Unit(sim, At(sim, 4, 4));
        sim.Enqueue(Command.Move(0, w, At(sim, 20, 4)));
        sim.Enqueue(Command.Gather(0, w, At(sim, 30, 10), queued: true));
        Run(sim, 2);
        UnitStore u = sim.World.Units;
        Assert.Equal(1, u.QueueCount[w.Index]);
        Assert.False(EconomySystem.OnLoop(u, w.Index));
        int started = -1;
        for (int t = 0; t < 400 && started < 0; t++)
        {
            sim.Tick();
            if (EconomySystem.OnLoop(u, w.Index)) started = t;
        }
        Assert.True(started > 0, "queued Gather never started");
        Assert.True(Vector2.Distance(u.Position[w.Index], At(sim, 20, 4)) < 2f, "started before the Move ended");
        Assert.Equal(tree, u.GatherNode[w.Index]);
        Assert.Equal(0, u.QueueCount[w.Index]);
    }

    [Fact]
    public void QueuedGatherToANonWorker_IsNotQueued()
    {
        Simulation sim = Map(out _, out _);
        EntityHandle soldier = Unit(sim, At(sim, 4, 8), type: Infantry);
        sim.Enqueue(Command.Gather(0, soldier, At(sim, 10, 10), queued: true));
        Run(sim, 2);
        Assert.Equal(0, sim.World.Units.QueueCount[soldier.Index]);
    }

    [Fact]
    public void AMoveEndsTheLoopButKeepsTheCargo_AndAGatherOfTheOtherKindDiscardsIt()
    {
        Simulation sim = Map(out EntityHandle mine, out EntityHandle tree);
        EntityHandle w = Unit(sim, At(sim, 29, 10)); // next to the tree
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.Gather(0, w, At(sim, 30, 10)));
        for (int t = 0; t < 600 && u.Cargo[w.Index] < 4; t++) sim.Tick();
        Assert.Equal(4, u.Cargo[w.Index]);
        sim.Enqueue(Command.Move(0, w, At(sim, 25, 15)));
        Run(sim, 2);
        Assert.False(EconomySystem.OnLoop(u, w.Index));
        Assert.Equal(4, u.Cargo[w.Index]);
        Assert.Equal(ResourceKind.Wood, u.CargoKind[w.Index]);

        sim.Enqueue(Command.Gather(0, w, At(sim, 10, 10))); // gold: the wood is dropped (docs/03 cargo rules)
        Run(sim, 2);
        Assert.Equal(mine, u.GatherNode[w.Index]);
        Assert.Equal(0, u.Cargo[w.Index]);
        Assert.Equal(ResourceKind.Gold, u.CargoKind[w.Index]);
        Assert.Equal(TreeWood - 4, sim.World.Resources.Remaining[tree.Index]);
    }
}
