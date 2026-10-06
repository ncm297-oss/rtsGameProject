using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.Replays;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>QA stress for M1-7 orders: invariant fuzzing over random order mixes, twins, replays, and scale.</summary>
public class OrderStressTests
{
    private readonly ITestOutputHelper _out;

    public OrderStressTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// Per-tick invariants of the order state, plus: a unit holding at the end of two consecutive
    /// ticks did not move between them (nothing but its own new order may move a holding unit, and
    /// any such order clears Hold first).
    /// </summary>
    private static void CheckInvariants(Simulation sim, bool[] heldBefore, Vector2[] posBefore)
    {
        UnitStore u = sim.World.Units;
        NavGrid g = sim.World.NavGrid;
        int qc = OrderConstants.QueueCapacity;
        for (int i = 0; i < u.Capacity; i++)
        {
            string at = $"tick {sim.TickNumber} unit {i}";
            Assert.True((uint)u.QueueCount[i] <= (uint)qc, $"{at}: queue count {u.QueueCount[i]}");
            for (int k = 0; k < qc; k++)
            {
                CommandKind kind = u.QueueKind[i * qc + k];
                Vector2 p = u.QueuePosition[i * qc + k];
                if (k >= u.QueueCount[i])
                    Assert.True(kind == CommandKind.Noop && p == Vector2.Zero, $"{at}: stale entry {k} past count {u.QueueCount[i]}");
                else
                {
                    Assert.True(kind is CommandKind.Move or CommandKind.AttackMove or CommandKind.Stop or CommandKind.HoldPosition, $"{at}: entry {k} kind {kind}");
                    if (kind is CommandKind.Stop or CommandKind.HoldPosition) Assert.True(p == Vector2.Zero, $"{at}: {kind} with a target");
                    else Assert.True(g.WorldToCell(p, out _, out _), $"{at}: queued target {p} off the map");
                }
            }
            if (!u.Alive[i])
            {
                Assert.False(u.Hold[i], $"{at}: dead slot holding");
                Assert.Equal(0, u.QueueCount[i]);
                continue;
            }
            Vector2 pos = u.Position[i];
            Assert.True(float.IsFinite(pos.X) && float.IsFinite(pos.Y), $"{at}: position {pos}");
            Assert.True(g.WorldToCell(pos, out int cx, out int cy) && g.IsPassable(cx, cy), $"{at}: on blocked ground or off the map at {pos}");
            if (u.Hold[i])
            {
                Assert.Equal(UnitState.Idle, u.State[i]);
                Assert.Equal(-1, u.GoalCell[i]);
                if (heldBefore[i]) Assert.True(pos == posBefore[i], $"{at}: holding unit moved {posBefore[i]} -> {pos}");
            }
            if (u.State[i] == UnitState.Idle && u.QueueCount[i] > 0)
            {
                // Phase 7 runs before movement: an Idle unit can keep a queue only if it went Idle in
                // movement this tick (arrived or gave up). Then its velocity is zero.
                Assert.True(u.Velocity[i] == Vector2.Zero, $"{at}: Idle with a queue but a velocity");
            }
        }
        for (int i = 0; i < u.Capacity; i++)
        {
            heldBefore[i] = u.Alive[i] && u.Hold[i];
            posBefore[i] = u.Position[i];
        }
    }

    /// <summary>Random mixes of every kind (OrderMix), 3,000 ticks, 8 more seeds; twin hash every tick; invariants after every tick.</summary>
    [Theory]
    [InlineData(4UL)]
    [InlineData(5UL)]
    [InlineData(6UL)]
    [InlineData(7UL)]
    [InlineData(8UL)]
    [InlineData(9UL)]
    [InlineData(10UL)]
    [InlineData(11UL)]
    public void RandomOrderMix_TwinAndInvariants(ulong seed)
    {
        const int units = 96, ticks = 3000, perTick = 12;
        Simulation a = OrderMix.Spawn(seed, units, 14f, out List<int> cellsA, perTick);
        Simulation b = OrderMix.Spawn(seed, units, 14f, out List<int> cellsB, perTick);
        var ra = new SimRng(seed, 77);
        var rb = new SimRng(seed, 77);
        var held = new bool[units];
        var pos = new Vector2[units];
        int maxHold = 0, maxQueued = 0;
        for (int t = 0; t < ticks; t++)
        {
            if (t % 2 == 0)
            {
                OrderMix.Issue(a, ref ra, cellsA, perTick);
                OrderMix.Issue(b, ref rb, cellsB, perTick);
            }
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {a.TickNumber}");
            CheckInvariants(a, held, pos);
            int hold = 0, queued = 0;
            for (int i = 0; i < units; i++)
            {
                if (a.World.Units.Hold[i]) hold++;
                queued += a.World.Units.QueueCount[i];
            }
            maxHold = Math.Max(maxHold, hold);
            maxQueued = Math.Max(maxQueued, queued);
        }
        _out.WriteLine($"seed {seed}: peak {maxHold} holding, peak {maxQueued} queued entries");
        Assert.True(maxHold > 0 && maxQueued > 0);
    }

    /// <summary>Recorded random order mixes for 3 more seeds: written, read back, played back to every checkpoint.</summary>
    [Theory]
    [InlineData(21UL)]
    [InlineData(22UL)]
    [InlineData(23UL)]
    public void RandomOrderMix_ReplayRoundTrip(ulong seed)
    {
        (_, ReplayRecorder rec) = ReplayTestRun.RecordOrders(seed, ticks: 1500, checkpointInterval: 50);
        Replay r = rec.ToReplay();
        Assert.Contains(r.Commands, c => c.Flags == Command.QueuedFlag);
        foreach (CommandKind k in new[] { CommandKind.Stop, CommandKind.HoldPosition, CommandKind.AttackMove, CommandKind.Move })
            Assert.Contains(r.Commands, c => c.Kind == k);
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(r), out Replay? back));
        ReplayTestRun.AssertEqual(r, back!);
        ReplayResult res = ReplayPlayer.Run(back!, TestSim.Data);
        Assert.True(res.Ok, $"{res.Error} at tick {res.Tick}");
        Assert.Equal(1500, res.TicksRun);
    }

    /// <summary>Report: 2,500 units on the default map, each with a full queue (an unqueued Move plus 8 queued legs).</summary>
    [Collection(SerialCollection.Name)]
    public class Serial
    {
        private readonly ITestOutputHelper _out;

        public Serial(ITestOutputHelper output) => _out = output;

        [Fact]
        [Trait("Category", "Perf")]
        public void TwoThousandFiveHundredUnits_FullQueues_Report()
        {
            const int units = 2500;
            Simulation sim = MoveScenario.Spawn(seed: 11, units: units, maxCost: 30f, out int center, capacity: units);
            // Command capacity is 2 * units + 8 and we need 9 per unit: tick between batches.
            NavGrid g = sim.World.NavGrid;
            List<int> cells = OrderMix.Cells(g, 40f);
            var rng = new SimRng(11, 3);
            UnitStore u = sim.World.Units;
            int commandCap = sim.World.Config.CommandCapacity;
            int pending = 0;
            for (int leg = 0; leg <= OrderConstants.QueueCapacity; leg++)
            {
                for (int i = 0; i < u.Capacity; i++)
                {
                    if (!u.Alive[i]) continue;
                    if (pending + 1 > commandCap) { while (sim.PendingCommandCount > 0) sim.Tick(); pending = 0; }
                    Vector2 p = MoveScenario.Center(g, cells[rng.NextInt(0, cells.Count)]);
                    sim.Enqueue(leg == 0 ? Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), p) : Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), p, queued: true));
                    pending++;
                }
            }
            while (sim.PendingCommandCount > 0) sim.Tick();
            int full = 0;
            for (int i = 0; i < u.Capacity; i++) if (u.QueueCount[i] >= OrderConstants.QueueCapacity - 1) full++;
            for (int t = 0; t < 5; t++) sim.Tick();
            var ms = new double[200];
            for (int t = 0; t < ms.Length; t++)
            {
                long s = Stopwatch.GetTimestamp();
                sim.Tick();
                ms[t] = (Stopwatch.GetTimestamp() - s) * 1000.0 / Stopwatch.Frequency;
            }
            int moving = 0, queued = 0;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (u.State[i] == UnitState.Moving) moving++;
                queued += u.QueueCount[i];
            }
            Array.Sort(ms);
            _out.WriteLine($"2,500 units, {full} with (near-)full queues at start: avg {ms.Average():F3} ms, p99 {ms[197]:F3} ms, worst {ms[^1]:F3} ms; at end {moving} Moving, {queued} queued entries");
        }
    }
}
