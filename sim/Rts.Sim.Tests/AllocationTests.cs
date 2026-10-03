using System.Numerics;
using Rts.Sim.Commands;

namespace Rts.Sim.Tests;

/// <summary>CLAUDE.md rule 5: per-tick code must not allocate.</summary>
public class AllocationTests
{
    private static Simulation WarmSim()
    {
        var sim = new Simulation(new SimConfig(Seed: 3, PlayerCount: 2, UnitCapacity: 512, CommandCapacity: 256));
        // Warm-up: JIT both the empty-tick and the spawn path before measuring.
        sim.Tick();
        sim.Enqueue(Command.SpawnUnit(1, typeId: 0, Vector2.One));
        sim.Enqueue(Command.Noop(0));
        sim.Tick();
        sim.Tick();
        return sim;
    }

    [Fact]
    public void Tick_WithNoCommands_AllocatesNothing()
    {
        Simulation sim = WarmSim();

        long before = GC.GetAllocatedBytesForCurrentThread();
        sim.Tick();
        long delta = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, delta);
    }

    [Fact]
    public void Tick_With100SpawnCommands_AllocatesNothing()
    {
        Simulation sim = WarmSim();
        // Enqueue from both players, out of player order, so the sort has real work to do.
        for (int i = 0; i < 100; i++)
            sim.Enqueue(Command.SpawnUnit(1 - (i % 2), typeId: i, new Vector2(i, i)));

        // Two ticks: the first sorts the unsorted batch, the second applies it.
        long before = GC.GetAllocatedBytesForCurrentThread();
        sim.Tick();
        sim.Tick();
        long delta = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, delta);
        Assert.Equal(101, sim.World.Units.Count);
    }
}
