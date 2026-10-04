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

    [Fact]
    public void SpatialHash_RebuildAnd1000Queries_On500Units_AllocateNothing()
    {
        var sim = new Simulation(new SimConfig(Seed: 4, PlayerCount: 2, UnitCapacity: 512, CommandCapacity: 512));
        for (int i = 0; i < 500; i++)
            sim.Enqueue(Command.SpawnUnit(i % 2, typeId: 0, new Vector2((i * 37) % 256, (i * 91) % 256)));
        sim.Tick();
        sim.Tick(); // the spawns apply on tick 1
        Spatial.SpatialHash hash = sim.World.Spatial;
        Assert.Equal(500, hash.Count);
        int[] buffer = new int[64]; // smaller than some results, so truncation runs too
        int sink = RunHashQueries(sim, hash, buffer, 50); // JIT warm-up

        long before = GC.GetAllocatedBytesForCurrentThread();
        sim.Tick(); // includes the rebuild
        hash.Rebuild(sim.World.Units);
        sink += RunHashQueries(sim, hash, buffer, 1000);
        long delta = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, delta);
        Assert.NotEqual(0, sink);
    }

    private static int RunHashQueries(Simulation sim, Spatial.SpatialHash hash, int[] buffer, int count)
    {
        int sink = 0;
        for (int i = 0; i < count; i++)
        {
            var c = new Vector2((i * 13) % 300 - 20, (i * 29) % 300 - 20);
            switch (i % 3)
            {
                case 0: sink += hash.QueryRadius(c, 2f + i % 40, buffer); break;
                case 1: sink += hash.QueryRect(c, c + new Vector2(-30f, 25f), buffer); break;
                default: if (hash.NearestEnemy(c, 30f, i % 2, out int slot)) sink += slot + 1; break;
            }
        }
        return sink + sim.TickNumber;
    }
}
