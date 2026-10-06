using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Replays;

namespace Rts.Sim.Tests;

/// <summary>CLAUDE.md rule 5: per-tick code must not allocate.</summary>
[Collection(SerialCollection.Name)]
public class AllocationTests
{
    private static Simulation WarmSim()
    {
        var sim = new Simulation(TestSim.Config(Seed: 3, PlayerCount: 2, UnitCapacity: 512, CommandCapacity: 256));
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

        Action tick = sim.Tick;
        AllocationProbe.AssertZero(tick);
    }

    [Fact]
    public void Tick_With100SpawnCommands_AllocatesNothing()
    {
        Simulation sim = WarmSim();
        // Enqueue from both players, out of player order, so the sort has real work to do.
        Action queue = () =>
        {
            for (int i = 0; i < 100; i++)
                sim.Enqueue(Command.SpawnUnit(1 - (i % 2), typeId: i % TestSim.UnitTypeCount, new Vector2(i, i)));
        };

        // Two ticks: the first sorts the unsorted batch, the second applies it.
        Action twoTicks = () =>
        {
            sim.Tick();
            sim.Tick();
        };
        int runs = AllocationProbe.AssertZero(twoTicks, setup: queue);

        Assert.Equal(1 + runs * 100, sim.World.Units.Count);
    }

    [Fact]
    public void SpatialHash_RebuildAnd1000Queries_On500Units_AllocateNothing()
    {
        var sim = new Simulation(TestSim.Config(Seed: 4, PlayerCount: 2, UnitCapacity: 512, CommandCapacity: 512));
        for (int i = 0; i < 500; i++)
            sim.Enqueue(Command.SpawnUnit(i % 2, typeId: 0, new Vector2((i * 37) % 256, (i * 91) % 256)));
        sim.Tick();
        sim.Tick(); // the spawns apply on tick 1
        Spatial.SpatialHash hash = sim.World.Spatial;
        Assert.Equal(500, hash.Count);
        int[] buffer = new int[64]; // smaller than some results, so truncation runs too
        int sink = RunHashQueries(sim, hash, buffer, 50); // JIT warm-up

        Action block = () =>
        {
            sim.Tick(); // includes the rebuild
            hash.Rebuild(sim.World.Units);
            sink += RunHashQueries(sim, hash, buffer, 1000);
        };
        AllocationProbe.AssertZero(block);

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

    [Fact]
    public void Tick_With500MovingUnits_AndACacheMiss_AllocatesNothing()
    {
        Simulation sim = MoveScenario.Spawn(seed: 13, units: 500, maxCost: float.MaxValue, out int goalCell);
        var g = sim.World.NavGrid;
        Vector2 goal = MoveScenario.Center(g, goalCell);
        // Warm-up: a first group Move (JITs Apply, a cache miss and the movement loop) and some walking.
        MoveScenario.MoveAll(sim, goal);
        for (int i = 0; i < 10; i++) sim.Tick();
        int builds = sim.World.FlowFields.BuildCount;
        Vector2 next = goal + new Vector2(6f, 0f);
        Assert.True(g.WorldToCell(next, out int nx, out int ny));
        Assert.False(sim.World.FlowFields.Contains(ny * g.Width + nx));

        Action block = () =>
        {
            sim.Tick();                     // a plain moving tick
            MoveScenario.MoveAll(sim, next); // a new target cell (or its nearest passable cell): not cached
            sim.Tick();
            sim.Tick();                     // applies the Moves and builds the field inside the tick
        };
        AllocationProbe.AssertZero(block);

        // A re-run re-issues the same Moves: the field is cached by then, so still one build.
        Assert.Equal(builds + 1, sim.World.FlowFields.BuildCount);
        int moving = 0;
        for (int i = 0; i < 500; i++) if (sim.World.Units.State[i] == Entities.UnitState.Moving) moving++;
        Assert.True(moving > 400, $"{moving} moving");
    }
    /// <summary>M1-6 criterion 7: a recorder's checkpoint hash runs inside Tick, into a preallocated buffer.</summary>
    [Fact]
    public void Tick_WithReplayRecorder_500TicksAnd5Checkpoints_AllocatesNothing()
    {
        (Simulation sim, ReplayRecorder rec) = ReplayTestRun.RecordSmall(seed: 13, ticks: 200);
        Assert.Equal(200, sim.TickNumber);
        Assert.Equal(2, rec.CheckpointCount); // warm: the checkpoint path has run
        var g = sim.World.NavGrid;
        var far = new[] { MoveScenario.Center(g, MoveScenario.CentralCell(g)) + new Vector2(30f, 0f), MoveScenario.Center(g, MoveScenario.CentralCell(g)) };
        int run = 0;
        Action order = () => MoveScenario.MoveAll(sim, far[run++ % 2]); // unmeasured; Enqueue is off-tick
        Action ticks = () =>
        {
            for (int t = 0; t < 500; t++) sim.Tick();
        };
        int before = rec.CheckpointCount;
        int runs = AllocationProbe.AssertZero(ticks, setup: order);
        Assert.Equal(before + 5 * runs, rec.CheckpointCount);
        Assert.Equal(200 + 500 * runs, sim.TickNumber);
    }

    /// <summary>
    /// M1-7 criterion 7: 500 units cycling shift-queued orders (an unqueued Move, then queued Move,
    /// AttackMove, and for some a terminal Stop or HoldPosition) among four nearby points: applying
    /// them and phase 7 popping them allocates nothing.
    /// </summary>
    [Fact]
    public void Tick_With500UnitsCyclingQueuedOrders_AllocatesNothing()
    {
        Simulation sim = MoveScenario.Spawn(seed: 14, units: 500, maxCost: 14f, out int goalCell, capacity: 1000, players: 1); // capacity: room for 3-4 commands per unit
        var g = sim.World.NavGrid;
        Vector2 c = MoveScenario.Center(g, goalCell);
        Vector2[] points = { c + new Vector2(-6f, -6f), c + new Vector2(6f, -6f), c + new Vector2(6f, 6f), c + new Vector2(-6f, 6f) };
        Entities.UnitStore u = sim.World.Units;
        int queuedPerRun = 0;
        Action order = () =>
        {
            queuedPerRun = 0;
            for (int i = 0; i < 500; i++)
            {
                var h = MoveScenario.Handle(sim, i);
                sim.Enqueue(Command.Move(0, h, points[i % 4]));
                sim.Enqueue(Command.Move(0, h, points[(i + 1) % 4], queued: true));
                sim.Enqueue(Command.AttackMove(0, h, points[(i + 2) % 4], queued: true));
                queuedPerRun += 2;
                if (i % 5 == 0) { sim.Enqueue(Command.Stop(0, h, queued: true)); queuedPerRun++; }
                else if (i % 7 == 0) { sim.Enqueue(Command.HoldPosition(0, h, queued: true)); queuedPerRun++; }
            }
        };
        Action ticks = () =>
        {
            for (int t = 0; t < 200; t++) sim.Tick();
        };
        order();
        ticks(); // warm-up: JITs the order paths and pops
        AllocationProbe.AssertZero(ticks, setup: order);
        int left = 0, holding = 0;
        for (int i = 0; i < 500; i++) { left += u.QueueCount[i]; if (u.Hold[i]) holding++; }
        Assert.True(left <= queuedPerRun - 150, $"{left} of {queuedPerRun} queued orders still waiting: too few pops measured");
        Assert.True(holding > 0, "no queued HoldPosition was reached");
    }
}
