using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
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
}
