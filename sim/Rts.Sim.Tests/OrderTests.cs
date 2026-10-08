using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;
using Rts.Sim.Orders;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M1-7 orders (docs/03 "Orders and unit states"): Stop, HoldPosition, AttackMove, and the
/// shift-queue that phase 7 (<see cref="OrderSystem.Run"/>) advances.
/// </summary>
public class OrderTests
{
    private readonly ITestOutputHelper _out;

    public OrderTests(ITestOutputHelper output) => _out = output;

    private static EntityHandle H(Simulation sim, int slot) => MoveScenario.Handle(sim, slot);

    /// <summary>Ticks until every enqueued command has applied: a command enqueued between ticks applies on tick <c>TickNumber + 1</c>, the second <c>Tick()</c> call.</summary>
    private static void ApplyPending(Simulation sim)
    {
        while (sim.PendingCommandCount > 0) sim.Tick();
    }

    /// <summary>One unit of radius 0.4 for player 0 at <paramref name="at"/> on a flat 32 x 32 map (2 players), spawned.</summary>
    private static Simulation OneUnit(Vector2 at, int capacity = 2)
    {
        Simulation sim = LocalMovementTests.SimOn(LocalMovementTests.Flat(32), capacity, players: 2);
        sim.Enqueue(Command.SpawnUnit(0, LocalMovementTests.TypeWithRadius(0.4f), at));
        sim.Tick();
        sim.Tick();
        return sim;
    }

    /// <summary>A unit walking east across the flat map, a few ticks into its walk.</summary>
    private static Simulation Walker()
    {
        Simulation sim = OneUnit(new Vector2(5f, 31f));
        sim.Enqueue(Command.Move(0, H(sim, 0), new Vector2(57f, 31f)));
        for (int t = 0; t < 6; t++) sim.Tick();
        UnitStore u = sim.World.Units;
        Assert.Equal(UnitState.Moving, u.State[0]);
        Assert.NotEqual(Vector2.Zero, u.Velocity[0]);
        return sim;
    }

    private static void AssertNoOrders(UnitStore u, int i)
    {
        Assert.False(u.Hold[i]);
        Assert.Equal(0, u.QueueCount[i]);
    }

    // ---------- API ----------

    [Fact]
    public void CommandKinds_AndFlags_HaveTheirWireValues()
    {
        Assert.Equal(3, (int)CommandKind.Stop);
        Assert.Equal(4, (int)CommandKind.HoldPosition);
        Assert.Equal(5, (int)CommandKind.AttackMove);
        Assert.Equal(1, Command.QueuedFlag);
        Assert.Equal(8, OrderConstants.QueueCapacity);
        var h = new EntityHandle(3, 4);
        Command move = Command.Move(1, h, new Vector2(2f, 3f)); // the 3-argument form the view calls
        Assert.Equal(0, move.Flags);
        Assert.Equal(Command.QueuedFlag, Command.Move(1, h, new Vector2(2f, 3f), queued: true).Flags);
        Command stop = Command.Stop(1, h, queued: true);
        Assert.Equal((CommandKind.Stop, 1, h, Command.QueuedFlag), (stop.Kind, stop.Player, stop.Unit, stop.Flags));
        Command hold = Command.HoldPosition(1, h);
        Assert.Equal((CommandKind.HoldPosition, 0), (hold.Kind, hold.Flags));
        Command am = Command.AttackMove(1, h, new Vector2(5f, 6f), queued: true);
        Assert.Equal((CommandKind.AttackMove, new Vector2(5f, 6f), Command.QueuedFlag), (am.Kind, am.Position, am.Flags));
        Assert.True(am.IsQueued && am.IsUnitOrder && am.IsValid());
        Assert.False(Command.AttackMove(1, h, new Vector2(float.NaN, 6f)).IsValid());
        Assert.False((move with { Flags = 2 }).IsValid()); // an unknown flag bit
    }

    // ---------- Stop ----------

    [Fact]
    public void Stop_HaltsAWalker_OnTheNextTick()
    {
        Simulation sim = Walker();
        UnitStore u = sim.World.Units;
        u.StuckTicks[0] = 4; // test seam: a count in progress
        sim.Enqueue(Command.Stop(0, H(sim, 0)));
        sim.Tick(); // the Stop is stamped for the next tick: this one still walks
        Assert.NotEqual(Vector2.Zero, u.Velocity[0]);
        sim.Tick();
        Assert.Equal(0, sim.PendingCommandCount);
        Assert.Equal(UnitState.Idle, u.State[0]);
        Assert.Equal(Vector2.Zero, u.Velocity[0]);
        Assert.Equal(-1, u.GoalCell[0]);
        Assert.Equal(UnitStore.WalkBackNone, u.WalkBack[0]);
        Assert.Equal(0, u.StuckTicks[0]);
        Assert.Equal(float.PositiveInfinity, u.BestRemaining[0]);
        Assert.Equal(u.PrevPosition[0], u.Position[0]); // it didn't take this tick's step
        Vector2 at = u.Position[0];
        for (int t = 0; t < 20; t++) sim.Tick();
        Assert.Equal(at, u.Position[0]);
    }

    [Fact]
    public void Stop_ClearsTheQueue_AndHold()
    {
        Simulation sim = Walker();
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.Move(0, H(sim, 0), new Vector2(9f, 9f), queued: true));
        ApplyPending(sim);
        Assert.Equal(1, u.QueueCount[0]);
        sim.Enqueue(Command.Stop(0, H(sim, 0)));
        ApplyPending(sim);
        AssertNoOrders(u, 0);
        Assert.Equal(CommandKind.Noop, u.QueueKind[0]); // the entry is reset, not just uncounted
        Assert.Equal(Vector2.Zero, u.QueuePosition[0]);
        u.Hold[0] = true; // test seam
        sim.Enqueue(Command.Stop(0, H(sim, 0)));
        ApplyPending(sim);
        Assert.False(u.Hold[0]);
    }

    /// <summary>Every unit order is dropped for a stale handle, a recycled slot's old handle, and another player's unit.</summary>
    [Theory]
    [InlineData(CommandKind.Stop)]
    [InlineData(CommandKind.HoldPosition)]
    [InlineData(CommandKind.AttackMove)]
    [InlineData(CommandKind.Move)]
    public void UnitOrders_AreDropped_ForDeadRecycledOrForeignUnits(CommandKind kind)
    {
        Simulation sim = Walker();
        UnitStore u = sim.World.Units;
        Command Order(int player, EntityHandle unit, bool queued) => kind switch
        {
            CommandKind.Stop => Command.Stop(player, unit, queued),
            CommandKind.HoldPosition => Command.HoldPosition(player, unit, queued),
            CommandKind.AttackMove => Command.AttackMove(player, unit, new Vector2(7f, 7f), queued),
            _ => Command.Move(player, unit, new Vector2(7f, 7f), queued),
        };
        EntityHandle live = H(sim, 0);
        Vector2 goal = u.Goal[0];
        foreach (bool queued in new[] { false, true })
        {
            sim.Enqueue(Order(1, live, queued)); // the wrong player for this unit
            sim.Enqueue(Order(0, live with { Generation = live.Generation + 1 }, queued)); // stale
            sim.Enqueue(Order(0, new EntityHandle(1, u.Generation[1]), queued)); // a free slot
            sim.Enqueue(Order(0, new EntityHandle(-1, 1), queued));
            ApplyPending(sim);
            Assert.Equal(UnitState.Moving, u.State[0]);
            Assert.Equal(goal, u.Goal[0]);
            AssertNoOrders(u, 0);
        }

        // Recycled: the slot is freed and taken by a new unit of the same player; the old handle is dead.
        u.Free(live); // test seam: no deaths until combat
        sim.Enqueue(Command.SpawnUnit(0, LocalMovementTests.TypeWithRadius(0.4f), new Vector2(11f, 11f)));
        ApplyPending(sim);
        Assert.True(u.IsAlive(H(sim, 0)) && H(sim, 0) != live);
        sim.Enqueue(Command.Move(0, H(sim, 0), new Vector2(21f, 11f)));
        ApplyPending(sim);
        Vector2 newGoal = u.Goal[0];
        sim.Enqueue(Order(0, live, false));
        sim.Enqueue(Order(0, live, true));
        ApplyPending(sim);
        Assert.Equal(UnitState.Moving, u.State[0]);
        Assert.Equal(newGoal, u.Goal[0]);
        AssertNoOrders(u, 0);
    }

    // ---------- HoldPosition ----------

    /// <summary>West room (x 1-9), a 1-cell corridor along row 4 (x 10-29), east room (x 30-38).</summary>
    private static Heightmap RoomsAndCorridor()
    {
        var rows = new string[9];
        for (int y = 0; y < 9; y++)
            rows[y] = new string('0', 10) + new string(y == 4 ? '0' : '1', 20) + new string('0', 10);
        return LocalMovementTests.Rows(rows);
    }

    /// <summary>
    /// A wide unit stands in the middle of a 1-cell corridor; 30 walkers are sent from the west room
    /// through it to the east room for 300 ticks. Holding, it ends bit-identical where it stood;
    /// stopped instead (goal-less, shovable), the same unit is pushed along the corridor.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HoldingUnit_InACorridor_IsNeverPushed_ButAStoppedOneIs(bool hold)
    {
        const int walkers = 30;
        Simulation sim = new(TestSim.Config(Seed: 1, PlayerCount: 1, UnitCapacity: walkers + 1, CommandCapacity: 2 * walkers + 8), RoomsAndCorridor());
        NavGrid g = sim.World.NavGrid;
        Vector2 stand = g.CellCenter(20, 4), goal = g.CellCenter(35, 4);
        sim.Enqueue(Command.SpawnUnit(0, LocalMovementTests.TypeWithRadius(0.9f), stand));
        int k = 0;
        for (int y = 1; y <= 7 && k < walkers; y++)
            for (int x = 1; x <= 8 && k < walkers; x++, k++)
                sim.Enqueue(Command.SpawnUnit(0, k % TestSim.UnitTypeCount, g.CellCenter(x, y)));
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        sim.Enqueue(hold ? Command.HoldPosition(0, H(sim, 0)) : Command.Stop(0, H(sim, 0)));
        for (int i = 1; i <= walkers; i++) sim.Enqueue(Command.Move(0, H(sim, i), goal));
        ApplyPending(sim);
        Assert.Equal(hold, u.Hold[0]);
        Vector2 at = u.Position[0];
        int pressed = 0;
        for (int t = 0; t < 300; t++)
        {
            sim.Tick();
            for (int i = 1; i <= walkers; i++)
                if (Vector2.Distance(u.Position[i], u.Position[0]) < u.Radius[i] + u.Radius[0] + 0.05f) { pressed++; break; }
            if (hold)
            {
                Assert.True(u.Position[0] == at, $"tick {t}: the holding unit moved from {at} to {u.Position[0]}");
                // A holder is a hard wall to its own army too (BUG-0055): nobody gets past it.
                for (int i = 1; i <= walkers; i++)
                    Assert.True(u.Position[i].X < at.X, $"tick {t}: walker {i} (r {u.Radius[i]}) got past the holder to {u.Position[i]}");
            }
        }
        _out.WriteLine($"hold {hold}: unit at {u.Position[0]} (stood at {at}), walkers touching it on {pressed} ticks");
        Assert.True(pressed > 0, "no walker ever reached the unit");
        if (hold)
        {
            Assert.Equal(BitConverter.SingleToInt32Bits(at.X), BitConverter.SingleToInt32Bits(u.Position[0].X));
            Assert.Equal(BitConverter.SingleToInt32Bits(at.Y), BitConverter.SingleToInt32Bits(u.Position[0].Y));
            Assert.Equal(UnitState.Idle, u.State[0]);
            Assert.True(u.Hold[0]);
        }
        else
        {
            Assert.True(u.Position[0].X > at.X + 2f, $"the stopped unit was not pushed along: {at} -> {u.Position[0]}");
        }
    }

    /// <summary>A holding unit cut off nothing it could walk back to; even with a pending walk-back (test seam) it never walks back.</summary>
    [Fact]
    public void HoldingUnit_NeverWalksBack()
    {
        Simulation sim = OneUnit(new Vector2(9f, 9f));
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.HoldPosition(0, H(sim, 0)));
        ApplyPending(sim);
        u.Goal[0] = new Vector2(25f, 9f); // test seam: as if cut off its blob with a goal to go back to
        u.WalkBack[0] = UnitStore.WalkBackPending;
        for (int t = 0; t < 3 * MovementConstants.WalkBackDelayTicks; t++) sim.Tick();
        Assert.Equal(UnitState.Idle, u.State[0]);
        Assert.Equal(new Vector2(9f, 9f), u.Position[0]);
    }

    [Fact]
    public void HoldPosition_StopsLikeStop_ThenHolds()
    {
        Simulation sim = Walker();
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.Move(0, H(sim, 0), new Vector2(9f, 9f), queued: true));
        sim.Enqueue(Command.HoldPosition(0, H(sim, 0)));
        ApplyPending(sim);
        Assert.Equal(UnitState.Idle, u.State[0]);
        Assert.Equal(Vector2.Zero, u.Velocity[0]);
        Assert.Equal(-1, u.GoalCell[0]);
        Assert.Equal(UnitStore.WalkBackNone, u.WalkBack[0]);
        Assert.Equal(0, u.QueueCount[0]);
        Assert.True(u.Hold[0]);
    }

    /// <summary>Any later unqueued order clears Hold; a queued Move clears it when it starts.</summary>
    [Theory]
    [InlineData(CommandKind.Move, false)]
    [InlineData(CommandKind.AttackMove, false)]
    [InlineData(CommandKind.Stop, false)]
    [InlineData(CommandKind.Move, true)]
    public void LaterOrder_ClearsHold(CommandKind kind, bool queued)
    {
        Simulation sim = OneUnit(new Vector2(9f, 9f));
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.HoldPosition(0, H(sim, 0)));
        ApplyPending(sim);
        Assert.True(u.Hold[0]);
        var target = new Vector2(25f, 9f);
        sim.Enqueue(kind switch
        {
            CommandKind.Move => Command.Move(0, H(sim, 0), target, queued),
            CommandKind.AttackMove => Command.AttackMove(0, H(sim, 0), target, queued),
            _ => Command.Stop(0, H(sim, 0), queued),
        });
        ApplyPending(sim);
        Assert.False(u.Hold[0]);
        Assert.Equal(kind == CommandKind.Stop ? UnitState.Idle : UnitState.Moving, u.State[0]);
    }

    // ---------- AttackMove ----------

    /// <summary>
    /// With no enemy anywhere an AttackMove walks exactly like a Move (M4-1; until then the two hashed equal): two one-player
    /// sims, one with Moves, one with AttackMoves, every unit's position, facing and state bit-equal after every tick for
    /// 500 ticks. The hashes differ now by the attack-move's mode and anchor, which nothing reads without an enemy.
    /// </summary>
    [Fact]
    public void AttackMove_WithNoEnemy_WalksExactlyLikeMove_For500Ticks()
    {
        Simulation Make() => MoveScenario.Spawn(seed: 9, units: 120, maxCost: 25f, out _, players: 1);
        Simulation a = Make(), b = Make();
        Assert.Equal(a.StateHash(), b.StateHash());
        NavGrid g = a.World.NavGrid;
        var rng = new Determinism.SimRng(9, 5);
        int center = MoveScenario.CentralCell(g);
        int moving = 0;
        bool hashesDiffered = false;
        UnitStore ua = a.World.Units, ub = b.World.Units;
        for (int t = 0; t < 500; t++)
        {
            if (t % 120 == 0)
            {
                Vector2 target = MoveScenario.Center(g, center) + new Vector2(rng.NextInt(-10, 11) * 2f, rng.NextInt(-10, 11) * 2f);
                for (int i = 0; i < ua.Capacity; i++)
                {
                    if (!ua.Alive[i]) continue;
                    a.Enqueue(Command.Move(ua.Owner[i], H(a, i), target));
                    b.Enqueue(Command.AttackMove(ua.Owner[i], H(b, i), target));
                }
            }
            a.Tick();
            b.Tick();
            for (int i = 0; i < ua.Capacity; i++)
            {
                Assert.Equal(ua.Alive[i], ub.Alive[i]);
                if (!ua.Alive[i]) continue;
                Assert.True(ua.Position[i] == ub.Position[i] && ua.Facing[i] == ub.Facing[i] && ua.State[i] == ub.State[i] && ua.GoalCell[i] == ub.GoalCell[i],
                    $"unit {i} differs after tick {a.TickNumber}");
                Assert.Equal(default, ub.Target[i]);
            }
            hashesDiffered |= a.PendingCommandCount == 0 && a.StateHash() != b.StateHash();
            for (int i = 0; i < ua.Capacity; i++)
                if (ua.State[i] == UnitState.Moving) { moving++; break; }
        }
        Assert.True(moving > 250, $"units moved on only {moving} of 500 ticks");
        Assert.True(hashesDiffered); // the attack-move's mode is state
    }

    // ---------- shift-queue ----------

    /// <summary>
    /// Ticks until the unit has walked every leg in <paramref name="points"/>: returns, per leg, whether
    /// it ended within ArrivalDistance of that leg's point while that leg was its order, and asserts
    /// it never started a leg before the previous one ended (arrived or, if <paramref name="mayGiveUp"/>
    /// holds that leg, gave up).
    /// </summary>
    private static bool[] WalkLegs(Simulation sim, int i, Vector2[] points, Func<int, bool> mayGiveUp, out bool[] gaveUp, int limit = 3000)
    {
        UnitStore u = sim.World.Units;
        var reached = new bool[points.Length];
        gaveUp = new bool[points.Length];
        int leg = 0;
        for (int t = 0; t < limit; t++)
        {
            sim.Tick();
            int now = Array.IndexOf(points, u.Goal[i]);
            if (now > leg)
            {
                Assert.True(now == leg + 1, $"tick {sim.TickNumber}: jumped from leg {leg} to leg {now}");
                Assert.True(reached[leg] || gaveUp[leg], $"tick {sim.TickNumber}: leg {now} started before leg {leg} ended");
                leg = now;
            }
            if (u.State[i] == UnitState.Idle && now == leg)
            {
                if (Vector2.Distance(u.Position[i], points[leg]) <= MovementConstants.ArrivalDistance) reached[leg] = true;
                else if (u.GoalCell[i] < 0) { Assert.True(mayGiveUp(leg), $"gave up on leg {leg}"); gaveUp[leg] = true; }
            }
            if (leg == points.Length - 1 && (reached[leg] || gaveUp[leg])) break;
        }
        return reached;
    }

    [Fact]
    public void ShiftQueuedChainOfFourMoves_VisitsEveryPointInOrder()
    {
        Simulation sim = OneUnit(new Vector2(5f, 5f));
        UnitStore u = sim.World.Units;
        Vector2[] points = { new(25.3f, 5.6f), new(25.1f, 25.2f), new(5.7f, 25.4f), new(15.2f, 15.9f) };
        sim.Enqueue(Command.Move(0, H(sim, 0), points[0]));
        for (int k = 1; k < points.Length; k++) sim.Enqueue(Command.Move(0, H(sim, 0), points[k], queued: true));
        bool[] reached = WalkLegs(sim, 0, points, _ => false, out _);
        Assert.All(reached, Assert.True);
        Assert.Equal(0, u.QueueCount[0]);
        Assert.Equal(UnitState.Idle, u.State[0]);
    }

    [Fact]
    public void QueuedOrders_OnAnIdleUnit_StartInTheSameTick()
    {
        Simulation sim = OneUnit(new Vector2(5f, 5f));
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.AttackMove(0, H(sim, 0), new Vector2(25f, 5f), queued: true));
        sim.Enqueue(Command.Move(0, H(sim, 0), new Vector2(25f, 25f), queued: true));
        ApplyPending(sim);
        Assert.Equal(UnitState.Moving, u.State[0]);
        Assert.Equal(new Vector2(25f, 5f), u.Goal[0]);
        Assert.Equal(1, u.QueueCount[0]);
        Assert.Equal(CommandKind.Move, u.QueueKind[0]);
        Assert.Equal(new Vector2(25f, 25f), u.QueuePosition[0]);
        Assert.NotEqual(Vector2.Zero, u.Velocity[0]); // it walked this tick: the pop runs before movement
    }

    /// <summary>A 1-cell corridor along cell row 2, cells x 1..22.</summary>
    private static Heightmap Corridor() => LocalMovementTests.Rows(
        "000000000000000000000000",
        "111111111111111111111111",
        "000000000000000000000000",
        "111111111111111111111111",
        "000000000000000000000000");

    /// <summary>Leg 2 runs into an enemy plugging the corridor and gives up; the unit still walks legs 3 and 4.</summary>
    [Fact]
    public void ShiftQueuedChain_ContinuesAfterAGiveUpOnLeg2()
    {
        Simulation sim = LocalMovementTests.SimOn(Corridor(), 2, players: 2);
        NavGrid g = sim.World.NavGrid;
        int wide = LocalMovementTests.TypeWithRadius(0.9f);
        sim.Enqueue(Command.SpawnUnit(0, LocalMovementTests.TypeWithRadius(0.4f), g.CellCenter(2, 2)));
        sim.Enqueue(Command.SpawnUnit(1, wide, g.CellCenter(10, 2)));
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        Vector2[] points = { g.CellCenter(5, 2), g.CellCenter(16, 2), g.CellCenter(2, 2), g.CellCenter(6, 2) };
        for (int k = 0; k < points.Length; k++) sim.Enqueue(Command.Move(0, H(sim, 0), points[k], queued: true));
        bool[] reached = WalkLegs(sim, 0, points, leg => leg == 1, out bool[] gaveUp);
        Assert.True(gaveUp[1], "leg 2 did not give up");
        Assert.True(reached[0] && reached[2] && reached[3], $"legs reached: {string.Join(", ", reached)}");
        Assert.Equal(g.CellCenter(10, 2), u.Position[1]); // the enemy plug held
    }

    [Theory]
    [InlineData(CommandKind.Stop)]
    [InlineData(CommandKind.HoldPosition)]
    public void QueuedStopOrHold_EndsTheChain(CommandKind terminal)
    {
        Simulation sim = OneUnit(new Vector2(5f, 5f));
        UnitStore u = sim.World.Units;
        Vector2 p1 = new(15f, 5f), p2 = new(15f, 15f), p3 = new(5f, 25f);
        sim.Enqueue(Command.Move(0, H(sim, 0), p1));
        sim.Enqueue(Command.Move(0, H(sim, 0), p2, queued: true));
        sim.Enqueue(terminal == CommandKind.Stop ? Command.Stop(0, H(sim, 0), queued: true) : Command.HoldPosition(0, H(sim, 0), queued: true));
        sim.Enqueue(Command.Move(0, H(sim, 0), p3, queued: true));
        ApplyPending(sim);
        Assert.Equal(3, u.QueueCount[0]);
        bool[] reached = WalkLegs(sim, 0, new[] { p1, p2 }, _ => false, out _);
        Assert.All(reached, Assert.True);
        for (int t = 0; t < 400; t++) sim.Tick();
        Assert.Equal(0, u.QueueCount[0]);
        Assert.Equal(UnitState.Idle, u.State[0]);
        Assert.True(Vector2.Distance(u.Position[0], p2) <= MovementConstants.ArrivalDistance, $"ended at {u.Position[0]}, not at the last leg before the {terminal}");
        Assert.Equal(-1, u.GoalCell[0]); // the popped Stop / Hold dropped the goal
        Assert.Equal(terminal == CommandKind.HoldPosition, u.Hold[0]);
    }

    [Fact]
    public void NinthQueuedOrder_IsDropped()
    {
        Simulation sim = Walker();
        UnitStore u = sim.World.Units;
        for (int k = 0; k < OrderConstants.QueueCapacity + 1; k++)
            sim.Enqueue(Command.Move(0, H(sim, 0), new Vector2(3f + 2f * k, 9f), queued: true));
        ApplyPending(sim);
        Assert.Equal(OrderConstants.QueueCapacity, u.QueueCount[0]);
        for (int k = 0; k < OrderConstants.QueueCapacity; k++)
        {
            Assert.Equal(CommandKind.Move, u.QueueKind[k]);
            Assert.Equal(new Vector2(3f + 2f * k, 9f), u.QueuePosition[k]);
        }
        sim.Enqueue(Command.Stop(0, H(sim, 0), queued: true));
        ApplyPending(sim);
        Assert.Equal(OrderConstants.QueueCapacity, u.QueueCount[0]);
        Assert.Equal(CommandKind.Move, u.QueueKind[OrderConstants.QueueCapacity - 1]);
    }

    [Fact]
    public void QueuedOrders_OnAMovingUnit_LeaveItsOrderUntouched()
    {
        Simulation sim = Walker();
        UnitStore u = sim.World.Units;
        int orderTick = u.OrderTick[0], goalCell = u.GoalCell[0];
        Vector2 goal = u.Goal[0];
        sim.Enqueue(Command.Move(0, H(sim, 0), new Vector2(9f, 9f), queued: true));
        sim.Enqueue(Command.AttackMove(0, H(sim, 0), new Vector2(9f, 19f), queued: true));
        sim.Enqueue(Command.HoldPosition(0, H(sim, 0), queued: true));
        sim.Enqueue(Command.Stop(0, H(sim, 0), queued: true));
        for (int t = 0; t < 6; t++)
        {
            sim.Tick();
            Assert.Equal(UnitState.Moving, u.State[0]);
            Assert.Equal(orderTick, u.OrderTick[0]);
            Assert.Equal(goalCell, u.GoalCell[0]);
            Assert.Equal(goal, u.Goal[0]);
            Assert.False(u.Hold[0]);
            Assert.Equal(sim.PendingCommandCount == 0 ? 4 : 0, u.QueueCount[0]);
        }
        Assert.Equal(new[] { CommandKind.Move, CommandKind.AttackMove, CommandKind.HoldPosition, CommandKind.Stop },
            u.QueueKind.AsSpan(0, 4).ToArray());
    }

    [Fact]
    public void UnqueuedMove_ReplacesTheQueue()
    {
        Simulation sim = Walker();
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.Move(0, H(sim, 0), new Vector2(9f, 9f), queued: true));
        sim.Enqueue(Command.Move(0, H(sim, 0), new Vector2(9f, 19f), queued: true));
        ApplyPending(sim);
        Assert.Equal(2, u.QueueCount[0]);
        sim.Enqueue(Command.AttackMove(0, H(sim, 0), new Vector2(29f, 29f)));
        ApplyPending(sim);
        Assert.Equal(0, u.QueueCount[0]);
        Assert.Equal(new Vector2(29f, 29f), u.Goal[0]);
        Assert.Equal(sim.TickNumber - 1, u.OrderTick[0]);
    }

    /// <summary>A Move to a target off the map is dropped whole, queued or not: no queue entry, the queue and Hold stay.</summary>
    [Fact]
    public void OffMapTarget_IsDropped_QueuedOrNot()
    {
        Simulation sim = OneUnit(new Vector2(9f, 9f));
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.HoldPosition(0, H(sim, 0)));
        ApplyPending(sim);
        u.Hold[0] = true;
        sim.Enqueue(Command.Move(0, H(sim, 0), new Vector2(-3f, 9f), queued: true));
        sim.Enqueue(Command.AttackMove(0, H(sim, 0), new Vector2(9f, 900f)));
        ApplyPending(sim);
        Assert.True(u.Hold[0]);
        Assert.Equal(0, u.QueueCount[0]);
        Assert.Equal(UnitState.Idle, u.State[0]);
    }

    [Fact]
    public void FreeAndAlloc_ResetHoldAndQueue()
    {
        var store = new UnitStore(2);
        EntityHandle a = store.Alloc();
        store.Hold[a.Index] = true;
        store.QueueCount[a.Index] = 2;
        store.QueueKind[a.Index * OrderConstants.QueueCapacity + 1] = CommandKind.Move;
        store.QueuePosition[a.Index * OrderConstants.QueueCapacity + 1] = new Vector2(3f, 4f);
        store.Free(a);
        Assert.False(store.Hold[a.Index]);
        Assert.Equal(0, store.QueueCount[a.Index]);
        Assert.Equal(CommandKind.Noop, store.QueueKind[a.Index * OrderConstants.QueueCapacity + 1]);
        Assert.Equal(Vector2.Zero, store.QueuePosition[a.Index * OrderConstants.QueueCapacity + 1]);
        store.Hold[a.Index] = true; // a stale write after free
        store.QueueCount[a.Index] = 1;
        EntityHandle b = store.Alloc();
        Assert.Equal(a.Index, b.Index);
        Assert.False(store.Hold[b.Index]);
        Assert.Equal(0, store.QueueCount[b.Index]);
    }
}
