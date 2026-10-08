using System.Diagnostics;
using System.Numerics;
using System.Text;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;
using Rts.Sim.Orders;
using Rts.Sim.Replays;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA attacks on M1-7 orders (session 2026-10-05-2330): Hold under shove storms and chain /
/// parked-line pushes, plugs, queue abuse, respawn races, replay flags, hash coverage.
/// </summary>
public class OrderQaTests
{
    private readonly ITestOutputHelper _out;

    public OrderQaTests(ITestOutputHelper output) => _out = output;

    private static EntityHandle H(Simulation sim, int slot) => MoveScenario.Handle(sim, slot);

    private static void ApplyPending(Simulation sim)
    {
        while (sim.PendingCommandCount > 0) sim.Tick();
    }

    private static bool SameBits(Vector2 a, Vector2 b) =>
        BitConverter.SingleToInt32Bits(a.X) == BitConverter.SingleToInt32Bits(b.X)
        && BitConverter.SingleToInt32Bits(a.Y) == BitConverter.SingleToInt32Bits(b.Y);

    /// <summary>A 1-cell corridor along row 2 from x 1 to <paramref name="length"/>, with 8x5 rooms at both ends.</summary>
    private static Heightmap RoomCorridorRoom(int length)
    {
        var rows = new string[9];
        for (int y = 0; y < 9; y++)
            rows[y] = new string('0', 10) + new string(y == 4 ? '0' : '1', length) + new string('0', 10);
        return LocalMovementTests.Rows(rows);
    }

    // ------------------------------------------------------------ Hold under shove storms

    /// <summary>
    /// Open ground: one holding unit with a ring of goal-less units around it (chain-shove fodder);
    /// N walkers from the west are ordered to the holder's own point, then to the far east, for 400
    /// ticks. The holder's position stays bit-identical on every tick.
    /// </summary>
    [Theory]
    [InlineData(30)]
    [InlineData(100)]
    [InlineData(200)]
    public void HoldingUnit_InAShoveStorm_NeverMoves(int walkers)
    {
        const int ring = 8;
        Simulation sim = new(TestSim.Config(Seed: 3, PlayerCount: 2, UnitCapacity: walkers + ring + 1, CommandCapacity: 2 * walkers + 64), LocalMovementTests.Flat(64));
        NavGrid g = sim.World.NavGrid;
        Vector2 stand = g.CellCenter(40, 32);
        sim.Enqueue(Command.SpawnUnit(0, LocalMovementTests.TypeWithRadius(0.4f), stand));
        for (int k = 0; k < ring; k++)
        {
            float a = k * MathF.PI * 2f / ring;
            sim.Enqueue(Command.SpawnUnit(0, LocalMovementTests.TypeWithRadius(0.4f), stand + new Vector2(MathF.Cos(a), MathF.Sin(a)) * 0.85f));
        }
        for (int k = 0; k < walkers; k++)
            sim.Enqueue(Command.SpawnUnit(0, k % TestSim.UnitTypeCount, g.CellCenter(4 + k % 20, 18 + k / 20 * 2 % 28) + new Vector2(0.3f * (k % 3), 0.2f * (k % 2))));
        ApplyPending(sim);
        UnitStore u = sim.World.Units;
        Assert.Equal(walkers + ring + 1, u.Count);
        sim.Enqueue(Command.HoldPosition(0, H(sim, 0)));
        for (int i = ring + 1; i < u.Capacity; i++)
        {
            sim.Enqueue(Command.Move(0, H(sim, i), stand));
            sim.Enqueue(Command.Move(0, H(sim, i), g.CellCenter(60, 32 + (i % 5) - 2), queued: true));
        }
        ApplyPending(sim);
        Assert.True(u.Hold[0]);
        Vector2 at = u.Position[0];
        int touching = 0;
        for (int t = 0; t < 400; t++)
        {
            sim.Tick();
            Assert.True(SameBits(at, u.Position[0]), $"tick {sim.TickNumber}: holder moved {at} -> {u.Position[0]}");
            for (int i = 1; i < u.Capacity; i++)
                if (Vector2.Distance(u.Position[i], at) < u.Radius[i] + u.Radius[0] + 0.05f) { touching++; break; }
        }
        Assert.True(u.Hold[0] && u.State[0] == UnitState.Idle);
        Assert.True(touching > 50, $"the storm barely touched the holder ({touching} ticks)");
        _out.WriteLine($"{walkers} walkers: holder touched on {touching} of 400 ticks, never moved");
    }

    /// <summary>
    /// Chain shove: in a 1-cell corridor a walker pushes two goal-less units, the second touching a
    /// holding unit, with another goal-less unit beyond it. The holder never moves, nor does the
    /// unit beyond it (the chain ends at the holder); the walker gives up in bounded time.
    /// </summary>
    [Fact]
    public void ChainShove_StopsAtAHoldingUnit()
    {
        Simulation sim = new(TestSim.Config(Seed: 1, PlayerCount: 1, UnitCapacity: 8, CommandCapacity: 32), RoomCorridorRoom(24));
        NavGrid g = sim.World.NavGrid;
        int small = LocalMovementTests.TypeWithRadius(0.4f), wide = LocalMovementTests.TypeWithRadius(0.9f);
        Vector2 hold = g.CellCenter(20, 4);
        // Wide line units: a 0.4 m walker can't squeeze past a 0.9 m unit in a 2 m corridor.
        sim.Enqueue(Command.SpawnUnit(0, wide, hold)); // 0 holder
        sim.Enqueue(Command.SpawnUnit(0, wide, hold - new Vector2(1.8f, 0f))); // 1 touching it
        sim.Enqueue(Command.SpawnUnit(0, wide, hold - new Vector2(3.6f, 0f))); // 2
        sim.Enqueue(Command.SpawnUnit(0, wide, hold + new Vector2(1.8f, 0f))); // 3 beyond
        sim.Enqueue(Command.SpawnUnit(0, small, g.CellCenter(5, 4))); // 4 walker
        ApplyPending(sim);
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.HoldPosition(0, H(sim, 0)));
        sim.Enqueue(Command.Move(0, H(sim, 4), g.CellCenter(38, 4)));
        ApplyPending(sim);
        Vector2 at0 = u.Position[0], at3 = u.Position[3];
        int t = 0;
        float deepest = 0f;
        for (; t < 1000 && u.State[4] == UnitState.Moving; t++)
        {
            sim.Tick();
            for (int i = 1; i < 5; i++) deepest = MathF.Max(deepest, u.Radius[0] + u.Radius[i] - Vector2.Distance(u.Position[i], at0));
            Assert.True(SameBits(at0, u.Position[0]), $"tick {t}: holder moved {at0} -> {u.Position[0]}");
            Assert.True(SameBits(at3, u.Position[3]), $"tick {t}: unit beyond the holder moved {at3} -> {u.Position[3]}");
        }
        _out.WriteLine($"walker stopped after {t} ticks at {u.Position[4]}, goal cell {u.GoalCell[4]}; unit 1 at {u.Position[1]}, unit 2 at {u.Position[2]}; deepest overlap with the holder {deepest:F3} m");
        Assert.True(u.Position[4].X < at0.X, "the walker passed the holder");
        Assert.True(u.State[4] == UnitState.Idle, "the blocked walker never gave up");
        Assert.True(u.Position[1].X < at0.X, "unit 1 was pushed through the holder");
    }

    /// <summary>
    /// Parked-line yield: two units parked in a 1-cell corridor (ordered to points there and arrived)
    /// with a holding unit between them; a blocked walker that may push parked lines (pushArrived)
    /// never moves the holder, and gives up in bounded time.
    /// </summary>
    [Fact]
    public void ParkedLineYield_NeverMovesAHoldingUnit()
    {
        Simulation sim = new(TestSim.Config(Seed: 1, PlayerCount: 1, UnitCapacity: 8, CommandCapacity: 32), RoomCorridorRoom(24));
        NavGrid g = sim.World.NavGrid;
        int small = LocalMovementTests.TypeWithRadius(0.4f), wide = LocalMovementTests.TypeWithRadius(0.9f);
        // Wide line units: a 0.4 m walker can't squeeze past a 0.9 m unit in a 2 m corridor.
        sim.Enqueue(Command.SpawnUnit(0, wide, g.CellCenter(18, 4))); // 0 parked
        sim.Enqueue(Command.SpawnUnit(0, wide, g.CellCenter(19, 4))); // 1 holder
        sim.Enqueue(Command.SpawnUnit(0, wide, g.CellCenter(20, 4))); // 2 parked
        sim.Enqueue(Command.SpawnUnit(0, small, g.CellCenter(5, 4))); // 3 walker
        ApplyPending(sim);
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.Move(0, H(sim, 0), g.CellCenter(18, 4) + new Vector2(0.1f, 0f)));
        sim.Enqueue(Command.Move(0, H(sim, 2), g.CellCenter(20, 4) + new Vector2(0.1f, 0f)));
        sim.Enqueue(Command.HoldPosition(0, H(sim, 1)));
        ApplyPending(sim);
        for (int k = 0; k < 40; k++) sim.Tick();
        Assert.Equal(UnitState.Idle, u.State[0]);
        Assert.Equal(UnitState.Idle, u.State[2]);
        Assert.True(u.GoalCell[0] >= 0 && u.GoalCell[2] >= 0, "the parked units lost their goals before the test began");
        Vector2 at = u.Position[1];
        sim.Enqueue(Command.Move(0, H(sim, 3), g.CellCenter(38, 4)));
        ApplyPending(sim);
        int t = 0;
        float deepest = 0f;
        for (; t < 1500 && u.State[3] == UnitState.Moving; t++)
        {
            sim.Tick();
            Assert.True(SameBits(at, u.Position[1]), $"tick {t}: holder moved {at} -> {u.Position[1]}");
            foreach (int i in new[] { 0, 2, 3 }) deepest = MathF.Max(deepest, u.Radius[1] + u.Radius[i] - Vector2.Distance(u.Position[i], at));
        }
        _out.WriteLine($"walker stopped after {t} ticks at {u.Position[3]}; parked 0 at {u.Position[0]}; deepest overlap with the holder {deepest:F3} m");
        Assert.True(u.State[3] == UnitState.Idle, "the blocked walker never gave up");
        for (int k = 0; k < 200; k++) sim.Tick(); // walk-backs, if any, have time to fire
        Assert.True(SameBits(at, u.Position[1]));
        _out.WriteLine($"walker ended at {u.Position[3]}, holder at {at}");
    }

    // ------------------------------------------------------------ plugs

    /// <summary>
    /// A wide holding unit plugs a 1-cell corridor; 30 walkers are sent through it. Returns how many
    /// ended past it and the deepest any walker ever overlapped it; asserts the holder never moved
    /// and every walker is Idle within a bounded time.
    /// </summary>
    private (int Passed, float Deepest, int SettledBy) RunPlug(int walkerPlayer, bool hold = true)
    {
        const int walkers = 30;
        Simulation sim = new(TestSim.Config(Seed: 2, PlayerCount: 2, UnitCapacity: walkers + 1, CommandCapacity: 2 * walkers + 8), RoomCorridorRoom(20));
        NavGrid g = sim.World.NavGrid;
        Vector2 stand = g.CellCenter(20, 4);
        sim.Enqueue(Command.SpawnUnit(0, LocalMovementTests.TypeWithRadius(0.9f), stand));
        int k = 0;
        for (int y = 1; y <= 7 && k < walkers; y++)
            for (int x = 1; x <= 8 && k < walkers; x++, k++)
                sim.Enqueue(Command.SpawnUnit(walkerPlayer, k % TestSim.UnitTypeCount, g.CellCenter(x, y)));
        ApplyPending(sim);
        UnitStore u = sim.World.Units;
        sim.Enqueue(hold ? Command.HoldPosition(0, H(sim, 0)) : Command.Stop(0, H(sim, 0)));
        for (int i = 1; i <= walkers; i++) sim.Enqueue(Command.Move(walkerPlayer, H(sim, i), g.CellCenter(35, 4)));
        ApplyPending(sim);
        Vector2 at = u.Position[0];
        int t = 0, lastMoving = 0;
        float deepest = 0f;
        for (; t < 4000; t++)
        {
            sim.Tick();
            if (hold || walkerPlayer != 0) Assert.True(SameBits(at, u.Position[0]), $"tick {t}: holder moved");
            int moving = 0;
            for (int i = 1; i <= walkers; i++)
            {
                deepest = MathF.Max(deepest, u.Radius[0] + u.Radius[i] - Vector2.Distance(u.Position[i], at));
                if (u.State[i] == UnitState.Moving) moving++;
            }
            if (moving > 0) lastMoving = t;
            else if (t > lastMoving + 50) break;
        }
        int passed = 0;
        for (int i = 1; i <= walkers; i++) if (u.Position[i].X > at.X) passed++;
        _out.WriteLine($"hold {hold}, player {walkerPlayer} walkers: {passed} of {walkers} past the holder, deepest overlap {deepest:F3} m, all Idle by tick {lastMoving + 1}");
        Assert.True(lastMoving < 1500, $"walkers behind the plug were still Moving at tick {lastMoving}");
        return (passed, deepest, lastMoving + 1);
    }

    /// <summary>An enemy holding plug in a 1-cell corridor: nobody passes (hard wall), the holder never moves, walkers settle.</summary>
    [Fact]
    public void EnemyHoldingPlug_InA1CellCorridor_LetsNobodyThrough()
    {
        (int passed, float deepest, _) = RunPlug(1);
        Assert.Equal(0, passed);
        // Walkers shoved by their own army end up to 0.16 m into an enemy plug whether it holds or
        // merely stands (pre-existing, cf. BUG-0045); Hold must not make it worse.
        (_, float stoppedDeepest, _) = RunPlug(1, hold: false);
        Assert.True(deepest <= stoppedDeepest, $"an enemy walker got {deepest:F3} m into the holding plug, {stoppedDeepest:F3} m into a stopped one");
    }

    /// <summary>A friendly holding plug: the holder never moves and walkers settle (nobody passing is BUG-0055, below).</summary>
    [Fact]
    public void FriendlyHoldingPlug_NeverMoves_AndWalkersSettle() => RunPlug(0);

    /// <summary>The QA focus's rule for M1-7: a holding unit plugging a choke lets nobody through, its own army included.</summary>
    [Fact]
    public void FriendlyHoldingPlug_InA1CellCorridor_LetsNobodyThrough() => Assert.Equal(0, RunPlug(0).Passed);

    // ------------------------------------------------------------ queue abuse

    /// <summary>4,096 queued orders to one unit in one tick: 8 kept (the first 8), the rest dropped, cheaply.</summary>
    [Fact]
    public void FourThousandQueuedOrders_OnOneUnit_KeepTheFirstEight()
    {
        const int n = 4096;
        Simulation sim = new(TestSim.Config(Seed: 1, PlayerCount: 1, UnitCapacity: 2, CommandCapacity: n + 8), LocalMovementTests.Flat(32));
        sim.Enqueue(Command.SpawnUnit(0, LocalMovementTests.TypeWithRadius(0.4f), new Vector2(5f, 31f)));
        ApplyPending(sim);
        sim.Enqueue(Command.Move(0, H(sim, 0), new Vector2(57f, 31f)));
        ApplyPending(sim);
        UnitStore u = sim.World.Units;
        for (int k = 0; k < n; k++)
        {
            Vector2 p = new(3f + (k % 25) * 2f, 3f + (k / 25 % 25) * 2f);
            sim.Enqueue((k % 4) switch
            {
                0 => Command.Move(0, H(sim, 0), p, true),
                1 => Command.AttackMove(0, H(sim, 0), p, true),
                2 => Command.Move(0, H(sim, 0), p, true),
                _ => Command.AttackMove(0, H(sim, 0), p, true),
            });
        }
        var sw = Stopwatch.StartNew();
        ApplyPending(sim);
        sw.Stop();
        _out.WriteLine($"{n} queued orders applied in {sw.Elapsed.TotalMilliseconds:F2} ms");
        Assert.Equal(OrderConstants.QueueCapacity, u.QueueCount[0]);
        for (int k = 0; k < OrderConstants.QueueCapacity; k++)
        {
            Assert.Equal(k % 2 == 0 ? CommandKind.Move : CommandKind.AttackMove, u.QueueKind[k]);
            Assert.Equal(new Vector2(3f + k * 2f, 3f), u.QueuePosition[k]);
        }
        Assert.Equal(UnitState.Moving, u.State[0]);
        Assert.Equal(new Vector2(57f, 31f), u.Goal[0]);
        Assert.True(sw.Elapsed.TotalMilliseconds < 500, $"applying {n} queued orders took {sw.Elapsed.TotalMilliseconds:F1} ms");
    }

    /// <summary>A queued Move to the cell the unit stands in (arrived there, or never ordered) doesn't stall the queue.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void QueuedMove_ToOwnPosition_DoesNotStallTheChain(bool arrivedFirst)
    {
        Simulation sim = new(TestSim.Config(Seed: 1, PlayerCount: 1, UnitCapacity: 2, CommandCapacity: 16), LocalMovementTests.Flat(32));
        var start = new Vector2(9.3f, 9.4f);
        sim.Enqueue(Command.SpawnUnit(0, LocalMovementTests.TypeWithRadius(0.4f), start));
        ApplyPending(sim);
        UnitStore u = sim.World.Units;
        if (arrivedFirst)
        {
            sim.Enqueue(Command.Move(0, H(sim, 0), start));
            for (int k = 0; k < 20; k++) sim.Tick();
            Assert.Equal(UnitState.Idle, u.State[0]);
        }
        Vector2 here = u.Position[0], next = new(29f, 9f);
        sim.Enqueue(Command.Move(0, H(sim, 0), here, queued: true));
        sim.Enqueue(Command.AttackMove(0, H(sim, 0), here, queued: true));
        sim.Enqueue(Command.Move(0, H(sim, 0), next, queued: true));
        int t = 0;
        for (; t < 300 && !(u.State[0] == UnitState.Idle && u.QueueCount[0] == 0 && sim.PendingCommandCount == 0); t++) sim.Tick();
        _out.WriteLine($"arrivedFirst {arrivedFirst}: chain finished after {t} ticks at {u.Position[0]}");
        Assert.True(Vector2.Distance(u.Position[0], next) <= MovementConstants.ArrivalDistance, $"ended at {u.Position[0]}");
        // Direct walk for comparison: same unit, same start, one Move to next.
        Simulation d = new(TestSim.Config(Seed: 1, PlayerCount: 1, UnitCapacity: 2, CommandCapacity: 16), LocalMovementTests.Flat(32));
        d.Enqueue(Command.SpawnUnit(0, LocalMovementTests.TypeWithRadius(0.4f), here));
        ApplyPending(d);
        d.Enqueue(Command.Move(0, H(d, 0), next));
        int direct = 0;
        for (; direct < 300 && !(d.World.Units.State[0] == UnitState.Idle && d.PendingCommandCount == 0); direct++) d.Tick();
        _out.WriteLine($"direct walk: {direct} ticks");
        Assert.True(t <= direct + 4, $"chain took {t} ticks, a direct walk {direct}");
    }

    /// <summary>A queued leg into a walled-in (sealed, so blocked) pocket resolves to the nearest passable cell, ends in bounded time, and the chain goes on.</summary>
    [Fact]
    public void QueuedLeg_ToAnUnreachableCell_EndsAndTheChainContinues()
    {
        var rows = new string[30];
        for (int y = 0; y < 30; y++)
            rows[y] = y == 8 || y == 20 ? new string('0', 12) + new string('1', 13) + new string('0', 5)
                : y > 8 && y < 20 ? new string('0', 12) + "1" + new string('0', 11) + "1" + new string('0', 5)
                : new string('0', 30); // a blocked ring around an unreachable pocket
        Simulation sim = new(TestSim.Config(Seed: 1, PlayerCount: 1, UnitCapacity: 2, CommandCapacity: 16), LocalMovementTests.Rows(rows));
        NavGrid g = sim.World.NavGrid;
        Assert.False(g.IsPassable(18, 14), "NavGrid seals unreachable pockets (SealPockets): the target is blocked, resolved to the nearest passable cell");
        sim.Enqueue(Command.SpawnUnit(0, LocalMovementTests.TypeWithRadius(0.4f), g.CellCenter(3, 3)));
        ApplyPending(sim);
        UnitStore u = sim.World.Units;
        Vector2 last = g.CellCenter(3, 26);
        sim.Enqueue(Command.Move(0, H(sim, 0), g.CellCenter(8, 3), true));
        sim.Enqueue(Command.AttackMove(0, H(sim, 0), g.CellCenter(18, 14), true)); // walled in
        sim.Enqueue(Command.Move(0, H(sim, 0), last, true));
        int t = 0;
        for (; t < 2000 && !(sim.PendingCommandCount == 0 && u.QueueCount[0] == 0 && u.State[0] == UnitState.Idle && Vector2.Distance(u.Position[0], last) <= MovementConstants.ArrivalDistance); t++) sim.Tick();
        _out.WriteLine($"chain with an unreachable leg finished after {t} ticks at {u.Position[0]}");
        Assert.True(t < 2000, $"stuck: state {u.State[0]}, queue {u.QueueCount[0]}, at {u.Position[0]}");
    }

    /// <summary>Stop and Move alternating every tick for 2,000 ticks: deterministic (twin), and the unit settles once the spam ends.</summary>
    [Fact]
    public void StopMoveAlternatingEveryTick_IsDeterministic_AndTerminates()
    {
        Simulation Run(out ulong[] hashes)
        {
            Simulation sim = MoveScenario.Spawn(seed: 4, units: 40, maxCost: 10f, out int center, combat: false);
            NavGrid g = sim.World.NavGrid;
            UnitStore u = sim.World.Units;
            hashes = new ulong[2000];
            Vector2 far = MoveScenario.Center(g, center) + new Vector2(20f, 0f);
            for (int t = 0; t < 2000; t++)
            {
                for (int i = 0; i < u.Capacity; i++)
                {
                    if (!u.Alive[i]) continue;
                    bool queued = i % 3 == 0;
                    sim.Enqueue(t % 2 == 0 ? Command.Move(u.Owner[i], H(sim, i), far, queued) : Command.Stop(u.Owner[i], H(sim, i), queued && t % 4 == 1));
                }
                sim.Tick();
                hashes[t] = sim.StateHash();
            }
            for (int t = 0; t < 600; t++) sim.Tick();
            return sim;
        }
        Simulation a = Run(out ulong[] ha), b = Run(out ulong[] hb);
        Assert.Equal(ha, hb);
        Assert.Equal(a.StateHash(), b.StateHash());
        UnitStore u = a.World.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i])
            {
                Assert.True(u.State[i] == UnitState.Idle, $"unit {i} still Moving 600 ticks after the spam");
                Assert.Equal(0, u.QueueCount[i]);
            }
    }

    // ------------------------------------------------------------ death / respawn races

    /// <summary>
    /// A unit with a queue is freed and its slot respawned between ticks; queued orders to the old
    /// handle arrive the same tick as the spawn. The new unit starts with no Hold and no queue and
    /// never runs the old unit's orders.
    /// </summary>
    [Fact]
    public void QueueOfAFreedUnit_NeverRunsOnItsSlotsNextUnit()
    {
        Simulation sim = new(TestSim.Config(Seed: 1, PlayerCount: 1, UnitCapacity: 1, CommandCapacity: 32), LocalMovementTests.Flat(32));
        int small = LocalMovementTests.TypeWithRadius(0.4f);
        sim.Enqueue(Command.SpawnUnit(0, small, new Vector2(5f, 5f)));
        ApplyPending(sim);
        UnitStore u = sim.World.Units;
        EntityHandle old = H(sim, 0);
        sim.Enqueue(Command.Move(0, old, new Vector2(50f, 5f)));
        sim.Enqueue(Command.Move(0, old, new Vector2(50f, 50f), queued: true));
        sim.Enqueue(Command.HoldPosition(0, old, queued: true));
        ApplyPending(sim);
        Assert.Equal(2, u.QueueCount[0]);
        u.Free(old); // test seam: death (combat is M4)
        sim.Enqueue(Command.SpawnUnit(0, small, new Vector2(9f, 41f)));
        sim.Enqueue(Command.Move(0, old, new Vector2(30f, 30f), queued: true)); // to the dead handle, same tick
        sim.Enqueue(Command.Stop(0, old));
        ApplyPending(sim);
        EntityHandle now = H(sim, 0);
        Assert.NotEqual(old, now);
        Assert.True(u.IsAlive(now));
        Assert.False(u.Hold[0]);
        Assert.Equal(0, u.QueueCount[0]);
        for (int k = 0; k < 200; k++) sim.Tick();
        Assert.Equal(new Vector2(9f, 41f), u.Position[0]);
        Assert.Equal(UnitState.Idle, u.State[0]);
        Assert.False(u.Hold[0]);
    }

    // ------------------------------------------------------------ determinism

    /// <summary>
    /// Six units far apart, each with its own 5-leg chain (Move, AttackMove, a give-up-free path) and
    /// a queued Hold at the end, spawned in two different orders: every unit (matched by spawn
    /// point) ends bit-identically, holding, with an empty queue.
    /// </summary>
    [Fact]
    public void QueuedChains_DoNotDependOnSpawnOrder()
    {
        Vector2[] starts = { new(8f, 8f), new(56f, 8f), new(8f, 56f), new(56f, 56f), new(32f, 8f), new(32f, 56f) };
        (Vector2 Pos, bool Hold, int Queue)[] Run(int[] order)
        {
            // M4-2b: combat off (config only, BUG-0135): the radius-0.4 / type-0 unit is the Cadre Mage, which fights now
            Simulation sim = new(TestSim.ConfigNoCombat(Seed: 5, PlayerCount: 2, UnitCapacity: 6, CommandCapacity: 64), LocalMovementTests.Flat(33));
            foreach (int s in order) sim.Enqueue(Command.SpawnUnit(s % 2, LocalMovementTests.TypeWithRadius(0.4f), starts[s]));
            ApplyPending(sim);
            UnitStore u = sim.World.Units;
            // Commands apply in (player, sequence) order, so slots don't follow the spawn list: map by position.
            var startOf = new int[6];
            for (int slot = 0; slot < 6; slot++) startOf[slot] = Array.IndexOf(starts, u.Position[slot]);
            Assert.DoesNotContain(-1, startOf);
            for (int slot = 0; slot < 6; slot++)
            {
                int s = startOf[slot];
                Vector2 o = starts[s];
                Vector2 d = o.X < 30f ? new Vector2(1f, 0f) : new Vector2(-1f, 0f);
                Vector2 e = o.Y < 30f ? new Vector2(0f, 1f) : new Vector2(0f, -1f);
                EntityHandle h = H(sim, slot);
                sim.Enqueue(Command.Move(s % 2, h, o + d * 6.3f));
                sim.Enqueue(Command.AttackMove(s % 2, h, o + d * 6.3f + e * 5.1f, true));
                sim.Enqueue(Command.Move(s % 2, h, o + e * 5.7f, true));
                sim.Enqueue(Command.Move(s % 2, h, o + e * 5.7f, true)); // the same point again
                sim.Enqueue(Command.AttackMove(s % 2, h, o + d * 2.2f + e * 1.1f, true));
                sim.Enqueue(Command.HoldPosition(s % 2, h, true));
            }
            for (int t = 0; t < 1200; t++) sim.Tick();
            var result = new (Vector2, bool, int)[6];
            for (int slot = 0; slot < 6; slot++) result[startOf[slot]] = (u.Position[slot], u.Hold[slot], u.QueueCount[slot]);
            return result;
        }
        var a = Run(new[] { 0, 1, 2, 3, 4, 5 });
        var b = Run(new[] { 5, 3, 1, 4, 0, 2 });
        for (int s = 0; s < 6; s++)
        {
            Assert.True(SameBits(a[s].Pos, b[s].Pos), $"unit from start {s}: {a[s].Pos} vs {b[s].Pos}");
            Assert.True(a[s].Hold && b[s].Hold, $"unit from start {s} is not holding at the end");
            Assert.Equal(0, a[s].Queue);
            Assert.Equal(0, b[s].Queue);
        }
    }

    // ------------------------------------------------------------ hash

    /// <summary>Swapping two queue entries, or moving an entry past the count, changes the hash.</summary>
    [Fact]
    public void StateHash_SeesQueueOrder_AndEntryPlacement()
    {
        Simulation sim = new(TestSim.Config(Seed: 1, PlayerCount: 1, UnitCapacity: 2, CommandCapacity: 16), LocalMovementTests.Flat(32));
        sim.Enqueue(Command.SpawnUnit(0, LocalMovementTests.TypeWithRadius(0.4f), new Vector2(5f, 31f)));
        ApplyPending(sim);
        sim.Enqueue(Command.Move(0, H(sim, 0), new Vector2(57f, 31f)));
        sim.Enqueue(Command.Move(0, H(sim, 0), new Vector2(9f, 9f), true));
        sim.Enqueue(Command.Move(0, H(sim, 0), new Vector2(19f, 9f), true));
        ApplyPending(sim);
        UnitStore u = sim.World.Units;
        Assert.Equal(2, u.QueueCount[0]);
        ulong h0 = sim.StateHash();
        (u.QueuePosition[0], u.QueuePosition[1]) = (u.QueuePosition[1], u.QueuePosition[0]);
        Assert.NotEqual(h0, sim.StateHash());
        (u.QueuePosition[0], u.QueuePosition[1]) = (u.QueuePosition[1], u.QueuePosition[0]);
        Assert.Equal(h0, sim.StateHash());
        // Entry 1 moved to entry 2 (count unchanged): a different (invalid) state, must hash differently.
        (u.QueueKind[2], u.QueuePosition[2], u.QueueKind[1], u.QueuePosition[1]) = (u.QueueKind[1], u.QueuePosition[1], CommandKind.Noop, Vector2.Zero);
        Assert.NotEqual(h0, sim.StateHash());
    }

    // ------------------------------------------------------------ replay flags

    private static string ValidOrdersReplayText()
    {
        (_, ReplayRecorder rec) = ReplayTestRun.RecordOrders(seed: 7, ticks: 200);
        return Encoding.ASCII.GetString(ReplayFormat.Write(rec.ToReplay()));
    }

    private static string Body(string text) => text[..(text.LastIndexOf("checksum ", StringComparison.Ordinal))];

    /// <summary>Flags other than 0 / 1 on a resealed file are refused as InvalidCommand.</summary>
    [Theory]
    [InlineData("2")]
    [InlineData("3")]
    [InlineData("-1")]
    [InlineData("2147483647")]
    [InlineData("-2147483648")]
    [InlineData("256")]
    public void ReplayFlags_OutOfRange_AreRefused(string flags)
    {
        string body = Body(ValidOrdersReplayText());
        string[] lines = body.Split('\n');
        int idx = Array.FindIndex(lines, l => l.StartsWith("c ", StringComparison.Ordinal));
        string[] f = lines[idx].Split(' ');
        f[^1] = flags;
        lines[idx] = string.Join(' ', f);
        ReplayError e = ReplayFormat.TryRead(ReplayFormat.Seal(string.Join('\n', lines)), out Replay? r);
        Assert.Equal(ReplayError.InvalidCommand, e);
        Assert.Null(r);
    }

    /// <summary>The recorded order mix uses flags 1; a raw bit flip of a flags digit is caught by the checksum.</summary>
    [Fact]
    public void ReplayFlags_BitFlip_IsCaughtByTheChecksum()
    {
        string text = ValidOrdersReplayText();
        string[] lines = text.Split('\n');
        Assert.Contains(lines, l => l.StartsWith("c ", StringComparison.Ordinal) && l.EndsWith(" 1", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("c ", StringComparison.Ordinal) && l.EndsWith(" 0", StringComparison.Ordinal));
        int idx = Array.FindIndex(lines, l => l.StartsWith("c ", StringComparison.Ordinal) && l.EndsWith(" 1", StringComparison.Ordinal));
        lines[idx] = lines[idx][..^1] + "0"; // '1' -> '0' is a single bit flip (0x31 -> 0x30)
        Assert.Equal(ReplayError.ChecksumMismatch, ReplayFormat.TryRead(Encoding.ASCII.GetBytes(string.Join('\n', lines)), out _));
    }

    /// <summary>
    /// Resealed (a deliberate edit): turning one queued order into an unqueued one is a valid file
    /// whose playback diverges, reported as a checkpoint mismatch (not a crash, not a silent pass).
    /// </summary>
    [Fact]
    public void ReplayFlags_EditedAndResealed_PlaybackReportsMismatch()
    {
        string body = Body(ValidOrdersReplayText());
        string[] lines = body.Split('\n');
        int flipped = 0;
        for (int i = 0; i < lines.Length; i++)
            if (lines[i].StartsWith("c ", StringComparison.Ordinal) && lines[i].EndsWith(" 1", StringComparison.Ordinal))
            {
                lines[i] = lines[i][..^1] + "0";
                flipped++;
            }
        Assert.True(flipped > 5);
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Seal(string.Join('\n', lines)), out Replay? r));
        ReplayResult res = ReplayPlayer.Run(r!, TestSim.Data);
        _out.WriteLine($"{flipped} flags cleared: {res.Error} at tick {res.Tick}");
        Assert.Equal(ReplayError.CheckpointMismatch, res.Error);
    }

    /// <summary>
    /// A format-1 file (9-field command lines, the M1-6 golden's shape) is refused with
    /// FormatVersionMismatch, not Malformed, even though its command lines are now too short.
    /// </summary>
    [Fact]
    public void Format1File_IsRefusedAsVersionMismatch()
    {
        string body = Body(File.ReadAllText(ReplayTestRun.GoldenPath).Replace("\r\n", "\n"));
        var sb = new StringBuilder();
        foreach (string line in body.TrimEnd('\n').Split('\n'))
        {
            if (line.StartsWith("rts-replay ", StringComparison.Ordinal)) sb.Append("rts-replay 1\n");
            else if (line.StartsWith("c ", StringComparison.Ordinal)) sb.Append(line[..line.LastIndexOf(' ')]).Append('\n');
            else sb.Append(line).Append('\n');
        }
        Assert.Equal(ReplayError.FormatVersionMismatch, ReplayFormat.TryRead(ReplayFormat.Seal(sb.ToString()), out Replay? r));
        Assert.Null(r);
    }

    /// <summary>
    /// Everything Enqueue accepts is recorded; a session in which a command with an unknown flag bit
    /// was offered should still produce a replay that reads and plays back. Since the BUG-0054 fix
    /// Enqueue refuses it (ArgumentException, as for an unknown player), so it is never recorded.
    /// </summary>
    [Fact]
    public void UnknownFlagsEnqueued_RecordedReplay_StillReads()
    {
        var sim = new Simulation(TestSim.Config(Seed: 3, PlayerCount: 1, UnitCapacity: 4, CommandCapacity: 16));
        var rec = new ReplayRecorder(sim, checkpointInterval: 10);
        sim.Enqueue(Command.SpawnUnit(0, 0, MoveScenario.Center(sim.World.NavGrid, MoveScenario.CentralCell(sim.World.NavGrid))));
        sim.Tick();
        sim.Tick();
        Assert.Throws<ArgumentException>(() => sim.Enqueue(Command.Stop(0, H(sim, 0)) with { Flags = 2 }));
        for (int t = 0; t < 18; t++) sim.Tick();
        byte[] bytes = ReplayFormat.Write(rec.ToReplay());
        ReplayError e = ReplayFormat.TryRead(bytes, out Replay? r);
        Assert.Equal(ReplayError.None, e);
        Assert.True(ReplayPlayer.Run(r!, TestSim.Data).Ok);
    }
}
