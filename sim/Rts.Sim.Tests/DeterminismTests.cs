using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;

namespace Rts.Sim.Tests;

/// <summary>M1-6 criterion 3 (docs/03 "Determinism"): same seed and commands give the same state hash; another seed doesn't.</summary>
public class DeterminismTests
{
    private const int Ticks = 2000;
    private const int Every = 100;

    /// <summary>200 units on the default map, re-ordered every 250 ticks to a point picked from the seed, so they keep moving; returns the hash every 100 ticks.</summary>
    private static ulong[] Run(ulong seed, out int movingTicks)
    {
        Simulation sim = MoveScenario.Spawn(seed, units: 200, maxCost: 30f, out int goalCell);
        var g = sim.World.NavGrid;
        UnitStore u = sim.World.Units;
        var rng = new Determinism.SimRng(seed, 71);
        var hashes = new ulong[Ticks / Every];
        movingTicks = 0;
        for (int t = 1; t <= Ticks; t++)
        {
            if (t % 250 == 1)
            {
                Vector2 target = MoveScenario.Center(g, goalCell) + new Vector2(rng.NextInt(-12, 13) * 2f, rng.NextInt(-12, 13) * 2f);
                MoveScenario.MoveAll(sim, target);
            }
            sim.Tick();
            for (int i = 0; i < u.Capacity; i++)
            {
                if (u.Alive[i] && u.State[i] == UnitState.Moving)
                {
                    movingTicks++;
                    break;
                }
            }
            if (t % Every == 0) hashes[t / Every - 1] = sim.StateHash();
        }
        return hashes;
    }

    [Fact]
    public void SameSeedAndCommands_EqualHashEvery100Ticks_For2000TicksOfMovement()
    {
        ulong[] a = Run(17, out int moving), b = Run(17, out _);
        Assert.True(moving > Ticks / 2, $"units moved on only {moving} of {Ticks} ticks");
        for (int i = 0; i < a.Length; i++)
            Assert.True(a[i] == b[i], $"hashes differ at tick {(i + 1) * Every}");
    }

    /// <summary>
    /// M3-1: <see cref="Run"/>'s movement on a map with 12 forests and 8 mines, plus a scripted
    /// <c>Take</c> of 37 every 5 ticks between ticks (cycling over the placed nodes), so trees fall,
    /// cells reopen and fields rebuild mid-run; returns the hash every 100 ticks.
    /// </summary>
    private static ulong[] RunWithResources(ulong seed, out int freed, out int versionAtEnd)
    {
        var map = new Map.MapGenParams { Forests = 12, GoldMines = 8 };
        Simulation sim = MoveScenario.Spawn(seed, units: 200, maxCost: 30f, out int goalCell, map: map);
        var g = sim.World.NavGrid;
        ResourceStore r = sim.World.Resources;
        var nodes = new List<EntityHandle>();
        for (int i = 0; i < r.Capacity; i++)
            if (r.Alive[i]) nodes.Add(r.HandleOf(i));
        Assert.True(nodes.Count > 100, $"only {nodes.Count} nodes placed");
        var rng = new Determinism.SimRng(seed, 71);
        var hashes = new ulong[Ticks / Every];
        freed = 0;
        for (int t = 1; t <= Ticks; t++)
        {
            if (t % 250 == 1)
            {
                Vector2 target = MoveScenario.Center(g, goalCell) + new Vector2(rng.NextInt(-12, 13) * 2f, rng.NextInt(-12, 13) * 2f);
                MoveScenario.MoveAll(sim, target);
            }
            if (t % 5 == 0)
            {
                EntityHandle node = nodes[t / 15 * 7 % nodes.Count]; // three takes per node in a row
                if (r.IsAlive(node))
                {
                    r.Take(node, 37);
                    if (!r.IsAlive(node)) freed++;
                }
            }
            sim.Tick();
            if (t % Every == 0) hashes[t / Every - 1] = sim.StateHash();
        }
        versionAtEnd = g.Version;
        return hashes;
    }

    [Fact]
    public void ForestsAndMines_WithScriptedTakes_EqualHashEvery100Ticks_For2000Ticks()
    {
        ulong[] a = RunWithResources(17, out int freed, out int version), b = RunWithResources(17, out int freedB, out int versionB);
        Assert.True(freed > 50, $"only {freed} nodes depleted");
        Assert.Equal((freed, version), (freedB, versionB));
        for (int i = 0; i < a.Length; i++)
            Assert.True(a[i] == b[i], $"hashes differ at tick {(i + 1) * Every}");
        Assert.NotEqual(Run(17, out _)[^1], a[^1]);
    }

    [Fact]
    public void DifferentSeeds_DifferByTick100()
    {
        ulong[] a = Run(17, out _), b = Run(18, out _);
        Assert.NotEqual(a[0], b[0]);
    }

    /// <summary>
    /// M1-7: two sims fed the same random mix of every unit-order kind (Move, AttackMove, Stop,
    /// HoldPosition; about half shift-queued, a few stale, foreign or off-map) hash equal after every
    /// tick for 2,000 ticks. The mix must really exercise the queue: units holding, queues filling up
    /// to capacity, and queued orders starting.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void RandomOrderMixes_TwoSimsHashEqualEveryTick_For2000Ticks(ulong seed)
    {
        Simulation a = OrderMix.Spawn(seed, units: 96, reach: 20f, out List<int> cells);
        Simulation b = OrderMix.Spawn(seed, units: 96, reach: 20f, out _);
        Assert.Equal(a.StateHash(), b.StateHash());
        var rngA = new Determinism.SimRng(seed, 47);
        var rngB = new Determinism.SimRng(seed, 47);
        UnitStore u = a.World.Units;
        int holdTicks = 0, fullQueues = 0, pops = 0;
        var lastCount = new int[u.Capacity];
        for (int t = 0; t < Ticks; t++)
        {
            if (t % 2 == 0)
            {
                OrderMix.Issue(a, ref rngA, cells, 6);
                OrderMix.Issue(b, ref rngB, cells, 6);
            }
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: hashes differ after tick {a.TickNumber}");
            for (int i = 0; i < u.Capacity; i++)
            {
                if (u.Hold[i]) { holdTicks++; }
                if (u.QueueCount[i] == Orders.OrderConstants.QueueCapacity) fullQueues++;
                if (u.QueueCount[i] < lastCount[i] && u.QueueCount[i] > 0) pops++; // popped, not cleared
                lastCount[i] = u.QueueCount[i];
            }
        }
        Assert.True(holdTicks > 0 && fullQueues > 0 && pops > 0, $"seed {seed}: hold {holdTicks}, full {fullQueues}, pops {pops}");
    }

    /// <summary>M1-7: a recorded 2,000-tick run of random order mixes writes, reads back, and plays back with every checkpoint matching.</summary>
    [Fact]
    public void RandomOrderMix_ReplayRoundTrip_MatchesEveryCheckpoint()
    {
        Replay recorded = ReplayTestRun.RecordOrders(seed: 11, ticks: Ticks).Recorder.ToReplay();
        Assert.Equal(Ticks / Every, recorded.Checkpoints.Length);
        foreach (CommandKind kind in new[] { CommandKind.Move, CommandKind.AttackMove, CommandKind.Stop, CommandKind.HoldPosition })
        {
            Assert.Contains(recorded.Commands, c => c.Kind == kind && c.IsQueued);
            Assert.Contains(recorded.Commands, c => c.Kind == kind && !c.IsQueued);
        }
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(recorded), out Replay? parsed));
        ReplayResult played = ReplayPlayer.Run(parsed!, TestSim.Data);
        Assert.True(played.Ok, played.ToString());
        Assert.Equal(Ticks, played.TicksRun);
    }

    /// <summary>
    /// M3-2b: 20 workers gathering (half of them felling trees, so cells open while the march goes on), 64
    /// marchers in 32 goal groups re-ordered every 300 ticks, and a Keep dropped every 300 ticks (closing
    /// changes): two sims hash equal after every tick for 3,000 ticks, and the recorded run plays back.
    /// </summary>
    [Fact]
    public void ChoppingMarchingAndBuildingDrops_TwinsHashEqualEveryTick_For3000Ticks_AndTheReplayRoundTrips()
    {
        const int ticks = 3000;
        (Simulation a, Func<int, bool> stepA, ReplayRecorder? recorder) = GridChangeScene(31, record: true);
        (Simulation b, Func<int, bool> stepB, _) = GridChangeScene(31, record: false);
        int wood0 = Trees(a.World), dropped = 0;
        int block0 = a.World.NavGrid.BlockVersion, version0 = a.World.NavGrid.Version;
        Assert.Equal(a.StateHash(), b.StateHash());
        for (int t = 0; t < ticks; t++)
        {
            if (stepA(t)) dropped++;
            stepB(t);
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"hashes differ after tick {a.TickNumber}");
        }
        int felled = wood0 - Trees(a.World);
        Assert.True(felled >= 2, $"only {felled} trees felled"); // 10 workers on trees fell 2 in 3,000 ticks (seed 31)
        Assert.True(dropped >= 8 && a.World.Buildings.Count >= 8, $"{dropped} drops, {a.World.Buildings.Count} buildings");
        Assert.True(a.World.NavGrid.BlockVersion - block0 >= 8 && a.World.NavGrid.Version - version0 >= 11);

        Replay recorded = recorder!.ToReplay();
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(recorded), out Replay? parsed));
        ReplayResult played = ReplayPlayer.Run(parsed!, TestSim.Data);
        Assert.True(played.Ok, played.ToString());
        Assert.Equal(recorded.TickCount, played.TicksRun);
    }

    private static int Trees(World w)
    {
        int n = 0;
        for (int k = 0; k < w.Resources.Capacity; k++)
            if (w.Resources.Alive[k] && w.Data.Resources[w.Resources.TypeId[k]].Resource == Data.ResourceKind.Wood) n++;
        return n;
    }

    /// <summary>The scene of the test above, set up to the first march tick; the step issues tick <c>t</c>'s commands and returns true when it dropped a Keep.</summary>
    private static (Simulation Sim, Func<int, bool> Step, ReplayRecorder? Recorder) GridChangeScene(ulong seed, bool record)
    {
        var map = MapGenParams.Default with { Forests = 8, GoldMines = 2 };
        var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 1, UnitCapacity: 96, CommandCapacity: 256) with { Map = map });
        ReplayRecorder? recorder = record ? new ReplayRecorder(sim, checkpointInterval: 100) : null;
        NavGrid g = sim.World.NavGrid;
        EconomyScenario.Setup(sim, 20);
        UnitStore u = sim.World.Units;
        var rng = new Determinism.SimRng(seed, 73);
        List<int> open = FlowFieldOracle.PassableCells(g);
        var before = (bool[])u.Alive.Clone();
        for (int k = 0; k < 64; k++) sim.Enqueue(Command.SpawnUnit(0, GatherMaps.Infantry, MoveScenario.Center(g, open[rng.NextInt(0, open.Count)])));
        sim.Tick();
        sim.Tick();
        var marchers = new List<EntityHandle>();
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && !before[i]) marchers.Add(new EntityHandle(i, u.Generation[i]));
        Assert.Equal(64, marchers.Count);
        bool Step(int t)
        {
            if (t % 300 == 0)
            {
                for (int k = 0; k < 32; k++)
                {
                    Vector2 goal = MoveScenario.Center(g, open[rng.NextInt(0, open.Count)]);
                    sim.Enqueue(Command.Move(0, marchers[2 * k], goal));
                    sim.Enqueue(Command.Move(0, marchers[2 * k + 1], goal));
                }
            }
            if (t % 300 != 150) return false;
            for (int tries = 0; tries < 200; tries++)
            {
                int c = open[rng.NextInt(0, open.Count)];
                if (!sim.World.Buildings.Fits(GatherMaps.Keep, c)) continue;
                sim.Enqueue(Command.SpawnBuilding(0, GatherMaps.Keep, MoveScenario.Center(g, c)));
                return true;
            }
            return false;
        }
        return (sim, Step, recorder);
    }
}
