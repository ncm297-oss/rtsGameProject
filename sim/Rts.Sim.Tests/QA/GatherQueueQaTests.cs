using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Orders;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA, M6-S11 (session 2026-11-15-1536): attacks on "an order Shift-queued behind a gathering worker starts once its trip
/// in hand is done". The sweep lands a queued Move on every tick of a first trip (walking out, working, one swing from full,
/// walking back, the delivery tick, the tick after it), on gold and wood, a Laborer and a Teblor Gatherer, with the node
/// running out mid-trip (another node near / none), and with the drop-off razed on the tick the order lands (the only one /
/// one of two). On every tick a save is taken with the command still pending and the loaded twin must follow hash for hash.
/// Plus the queue attacks: 8 then a ninth, a queued Gather on the same node, a queued Build it cannot afford at its turn,
/// queued and unqueued orders in one tick, and the worker killed on the tick its order applies.
/// </summary>
public class GatherQueueQaTests
{
    private readonly ITestOutputHelper _out;

    public GatherQueueQaTests(ITestOutputHelper output) => _out = output;

    public enum Raze
    {
        /// <summary>Nothing razed.</summary>
        None,
        /// <summary>The only drop-off razed on the tick the order is queued.</summary>
        Only,
        /// <summary>The nearer of two drop-offs razed on the tick the order is queued.</summary>
        Nearer,
    }

    private sealed class Scene
    {
        public required Simulation Sim;
        public required EntityHandle Worker, Keep, NodeA;
        public required bool Wood;
        public int I => Worker.Index;
        public UnitStore U => Sim.World.Units;
        public int Carry => Sim.World.Data.CarryOf(U.TypeId[I]);
        public int Gathered => Wood ? Sim.World.GatheredWood[0] : Sim.World.GatheredGold[0];
    }

    // 48 x 24 flat map, one player: a Keep on cells 8-11 x 8-11 (and optionally a second Keep far east), node A, node B.
    private static Scene Make(bool gatherer, bool wood, int nodeAmount = 0, bool nodeB = true, bool secondKeep = false)
    {
        Simulation sim = BuildMaps.NewSim(Flat(48, 24), units: 16, players: 1);
        World w = sim.World;
        EntityHandle a = wood ? Spawn(w, Tree, 18, 16, nodeAmount > 0 ? nodeAmount : TreeWood) : Spawn(w, Mine, 18, 9, nodeAmount > 0 ? nodeAmount : 2500);
        if (nodeB)
        {
            if (wood) Spawn(w, Tree, 22, 16, TreeWood);
            else Spawn(w, Mine, 23, 9, 2500);
        }
        EntityHandle worker = CombatScenes.Place(sim, 0, gatherer ? GatherOverlapScenes.GathererType : Laborer, At(sim, 14, wood ? 14 : 9));
        EntityHandle keep = Building(sim, 8, 8);
        if (secondKeep) Building(sim, 40, 14);
        BuildMaps.Give(sim, 0, 1000, 1000);
        sim.Enqueue(Command.Gather(0, worker, CellOf(sim, a)));
        ApplyPending(sim);
        return new Scene { Sim = sim, Worker = worker, Keep = keep, NodeA = a, Wood = wood };
    }

    private static Vector2 CellOf(Simulation sim, EntityHandle node)
    {
        int cell = sim.World.Resources.Cell[node.Index], width = sim.World.NavGrid.Width;
        return sim.World.NavGrid.CellCenter(cell % width, cell / width);
    }

    private static void ApplyPending(Simulation sim)
    {
        while (sim.PendingCommandCount > 0) sim.Tick();
    }

    private static Vector2 Target(Simulation sim) => At(sim, 30, 21);

    private static bool StartedMove(Scene s) =>
        !EconomySystem.OnLoop(s.U, s.I) && s.U.QueueCount[s.I] == 0 && s.U.State[s.I] == UnitState.Moving
        && Vector2.Distance(s.U.Goal[s.I], Target(s.Sim)) < 0.01f;

    /// <summary>
    /// The sweep. For each tick T from the Gather's apply to 40 ticks past the first delivery (or the loop's end) of a control
    /// run: a fresh scene runs to T, (optionally razes a drop-off), queues a Move, saves with the Move pending and loads a twin;
    /// both run until the Move has started. Asserted on every T: the twin hashes alike every tick; at most one delivery, of a
    /// full load; the Move starts in the delivery's own tick, or (no delivery) within 2 ticks of applying, or when the loop ends
    /// on its own (node gone and none other); a worker carrying cargo with a drop-off left delivers exactly once; one walking
    /// out empty, not in reach of its node on the apply tick, delivers nothing; cargo is never lost without a delivery.
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")] // about 3,000 scenes and saves: 6-15 minutes, kept out of the default loop
    [InlineData(false, false, 0, true, Raze.None)]
    [InlineData(true, false, 0, true, Raze.None)]
    [InlineData(false, true, 0, true, Raze.None)]
    [InlineData(true, true, 0, true, Raze.None)]
    [InlineData(false, false, 6, true, Raze.None)]  // the mine runs out mid-trip, another mine near
    [InlineData(false, false, 6, false, Raze.None)] // the mine runs out mid-trip, no other node
    [InlineData(false, false, 0, true, Raze.Only)]
    [InlineData(true, true, 0, true, Raze.Only)]
    [InlineData(false, false, 0, true, Raze.Nearer)]
    public void AQueuedMove_OnEveryTickOfATrip_DeliversAtMostOnce_AndStartsWhenTheTripIsDone(bool gatherer, bool wood, int nodeAmount, bool nodeB, Raze raze)
    {
        bool two = raze == Raze.Nearer;
        Scene control = Make(gatherer, wood, nodeAmount, nodeB, two);
        int first = control.Sim.TickNumber, g0 = control.Gathered, end = -1;
        for (int k = 0; k < 4000 && end < 0; k++)
        {
            control.Sim.Tick();
            if (control.Gathered != g0 || !EconomySystem.OnLoop(control.U, control.I)) end = control.Sim.TickNumber;
        }
        Assert.True(end > 0, "control: no delivery and no loop end in 4,000 ticks");
        int last = end + 40;
        int walkingOut = 0, walkingOutDelivered = 0, carrying = 0, emptyInReach = 0, deliveryTick = 0, longest = 0, selfEnded = 0;
        for (int t = first + 1; t <= last; t++)
        {
            Scene s = Make(gatherer, wood, nodeAmount, nodeB, two);
            while (s.Sim.TickNumber < t - 1) s.Sim.Tick();
            UnitStore u = s.U;
            int i = s.I;
            // A command enqueued between ticks N and N + 1 applies in tick N + 2 (phase 1): enqueued now, at T - 1, it applies in
            // tick T + 1. The save is taken with it pending; the twin runs the pre-apply tick T too.
            s.Sim.Enqueue(Command.Move(0, s.Worker, Target(s.Sim), queued: true));
            byte[] bytes = SaveTestRun.Save(s.Sim);
            Simulation twin = SaveTestRun.Load(TestSim.Data, bytes);
            SaveTestRun.AssertSameState(s.Sim, twin, $"T {t}: at the save");
            s.Sim.Tick();
            twin.Tick();
            Assert.True(s.Sim.StateHash() == twin.StateHash(), $"T {t}: the loaded twin differs at tick {s.Sim.TickNumber}");
            Assert.Equal(1, s.Sim.PendingCommandCount);
            if (!EconomySystem.OnLoop(u, i)) continue; // the loop already ended on its own (the no-node case)
            int c0 = u.Cargo[i], gathered = s.Gathered;
            bool outEmpty = c0 == 0 && u.State[i] == UnitState.Moving;
            if (raze != Raze.None)
            {
                s.Sim.World.Buildings.Damage(s.Keep, int.MaxValue);
                twin.World.Buildings.Damage(twin.World.Buildings.HandleOf(s.Keep.Index), int.MaxValue);
            }
            int applied = t + 1, deliveries = 0, deliveredAt = -1, started = -1;
            bool inReachAtApply = false;
            for (int k = 0; k < 4000 && started < 0; k++)
            {
                int before = s.Gathered, cargo = u.Cargo[i];
                EntityHandle node = u.GatherNode[i];
                s.Sim.Tick();
                twin.Tick();
                Assert.True(s.Sim.StateHash() == twin.StateHash(), $"T {t}: the loaded twin differs at tick {s.Sim.TickNumber}");
                if (s.Sim.TickNumber == applied && s.Sim.World.Resources.IsAlive(node))
                {
                    // Phase 4's "in reach of its node" (docs/03 M6-S11) is judged after phase 3 moved the worker: the same position.
                    World w = s.Sim.World;
                    Rts.Sim.Data.ResourceDef def = w.Data.Resources[w.Resources.TypeId[node.Index]];
                    inReachAtApply = DistanceToFootprint(w.NavGrid, u.Position[i], w.Resources.Cell[node.Index], def.FootprintWidth, def.FootprintHeight) <= EconomyConstants.Reach;
                }
                if (s.Gathered != before)
                {
                    deliveries++;
                    deliveredAt = s.Sim.TickNumber;
                    Assert.True(s.Gathered - before == s.Carry, $"T {t}: delivered {s.Gathered - before}, carry {s.Carry}");
                }
                else if (u.Cargo[i] < cargo)
                    Assert.Fail($"T {t}: cargo {cargo} -> {u.Cargo[i]} at tick {s.Sim.TickNumber} with no delivery");
                if (StartedMove(s)) started = s.Sim.TickNumber;
            }
            Assert.True(started > 0, $"T {t}: the queued Move never started (state {u.State[i]}, on loop {EconomySystem.OnLoop(u, i)}, cargo {u.Cargo[i]}, queue {u.QueueCount[i]})");
            Assert.True(deliveries <= 1, $"T {t}: {deliveries} deliveries before the Move started");
            longest = Math.Max(longest, started - applied);
            if (deliveries == 1)
            {
                Assert.True(started == deliveredAt, $"T {t}: delivered at {deliveredAt}, the Move started at {started}");
                Assert.Equal(0, u.Cargo[i]);
                if (s.Sim.TickNumber == applied) deliveryTick++;
            }
            else if (started > applied + 1)
            {
                // No delivery and later than the apply tick: only when the loop ran out on its own (node gone, none other).
                Assert.True(nodeAmount > 0 && !nodeB, $"T {t}: no delivery, but the Move started {started - applied} ticks after applying (cargo at T {c0})");
                selfEnded++;
            }
            if (c0 > 0 && raze != Raze.Only && (nodeAmount == 0 || nodeB))
            {
                carrying++;
                Assert.True(deliveries == 1, $"T {t}: carrying {c0} with a drop-off left, but delivered {deliveries} times");
            }
            if (raze == Raze.Only)
            {
                Assert.True(deliveries == 0 && started <= applied + 1, $"T {t}: the only drop-off razed: delivered {deliveries}, started {started - applied} ticks after applying");
                Assert.True(u.Cargo[i] >= c0, $"T {t}: cargo {c0} -> {u.Cargo[i]} with the drop-off razed");
            }
            if (outEmpty)
            {
                walkingOut++;
                if (deliveries > 0)
                {
                    walkingOutDelivered++;
                    Assert.True(inReachAtApply, $"T {t}: walking out empty and out of reach (> {EconomyConstants.Reach} m) on the apply tick, yet it ran a whole trip");
                }
            }
            if (c0 == 0 && !outEmpty) emptyInReach++;
        }
        _out.WriteLine($"ticks {first}..{last} (control's trip ends at {end}): walking out empty {walkingOut} (of which arrived on the apply tick and ran the trip: {walkingOutDelivered}), "
            + $"carrying {carrying}, empty and standing {emptyInReach}, queued on the delivery tick {deliveryTick}, loop ended on its own {selfEnded}; longest wait {longest} ticks");
    }

    /// <summary>A worker walking out with part of a load (it was re-sent to another node of its kind): it has a trip to finish, so it fills up and delivers first.</summary>
    [Fact]
    public void WalkingOutWithPartOfALoad_FinishesTheTripFirst()
    {
        Scene s = Make(gatherer: false, wood: false);
        UnitStore u = s.U;
        for (int k = 0; k < 3000 && !(u.State[s.I] == UnitState.Gathering && u.Cargo[s.I] >= 3); k++) s.Sim.Tick();
        Assert.True(u.Cargo[s.I] >= 3);
        s.Sim.Enqueue(Command.Gather(0, s.Worker, At(s.Sim, 23, 9))); // mine B, same kind: cargo kept
        ApplyPending(s.Sim);
        Assert.Equal(UnitState.Moving, u.State[s.I]);
        int cargo = u.Cargo[s.I], gathered = s.Gathered;
        Assert.True(cargo > 0);
        s.Sim.Enqueue(Command.Move(0, s.Worker, Target(s.Sim), queued: true));
        ApplyPending(s.Sim);
        for (int k = 0; k < 4000 && !StartedMove(s); k++) s.Sim.Tick();
        Assert.True(StartedMove(s), "never started");
        Assert.Equal(gathered + s.Carry, s.Gathered);
    }

    /// <summary>Walking out with a load of the other kind is not a thing: a Gather of the other kind drops it (the documented AoE rule), so it walks out empty and starts the queue at once.</summary>
    [Fact]
    public void ReSentToTheOtherKind_WalksOutEmpty_AndStartsTheQueueAtOnce()
    {
        Simulation sim = BuildMaps.NewSim(Flat(48, 24), units: 16, players: 1);
        World w = sim.World;
        Spawn(w, Mine, 18, 9, 2500);
        EntityHandle tree = Spawn(w, Tree, 30, 18, TreeWood);
        EntityHandle worker = CombatScenes.Place(sim, 0, Laborer, At(sim, 14, 9));
        Building(sim, 8, 8);
        sim.Enqueue(Command.Gather(0, worker, At(sim, 18, 9)));
        ApplyPending(sim);
        UnitStore u = w.Units;
        int i = worker.Index;
        for (int k = 0; k < 3000 && !(u.State[i] == UnitState.Gathering && u.Cargo[i] >= 3); k++) sim.Tick();
        int gold = w.GatheredGold[0];
        sim.Enqueue(Command.Gather(0, worker, CellOf(sim, tree)));
        sim.Enqueue(Command.Move(0, worker, Target(sim), queued: true));
        ApplyPending(sim);
        sim.Tick();
        Assert.False(EconomySystem.OnLoop(u, i));
        Assert.Equal(0, u.QueueCount[i]);
        Assert.True(Vector2.Distance(u.Goal[i], Target(sim)) < 0.01f);
        Assert.Equal(0, u.Cargo[i]);
        Assert.Equal(gold, w.GatheredGold[0]);
    }

    /// <summary>One swing from full: it takes the last unit, walks back, delivers a full load and only then starts the Move.</summary>
    [Fact]
    public void OneSwingFromFull_DeliversAFullLoad_ThenStarts()
    {
        Scene s = Make(gatherer: false, wood: false);
        UnitStore u = s.U;
        for (int k = 0; k < 3000 && !(u.State[s.I] == UnitState.Gathering && u.Cargo[s.I] == s.Carry - 1); k++) s.Sim.Tick();
        Assert.Equal(s.Carry - 1, u.Cargo[s.I]);
        int gathered = s.Gathered;
        s.Sim.Enqueue(Command.Move(0, s.Worker, Target(s.Sim), queued: true));
        ApplyPending(s.Sim);
        for (int k = 0; k < 3000 && !StartedMove(s); k++)
        {
            s.Sim.Tick();
            if (!StartedMove(s)) Assert.Equal(gathered, s.Gathered);
        }
        Assert.Equal(gathered + s.Carry, s.Gathered);
    }

    /// <summary>8 queued then a ninth: the ninth is refused, the 8 run in order after the delivery.</summary>
    [Fact]
    public void EightQueuedThenANinth_TheNinthIsDropped_TheEightRunInOrder()
    {
        Scene s = Make(gatherer: false, wood: false);
        UnitStore u = s.U;
        for (int k = 0; k < 3000 && !(u.State[s.I] == UnitState.Gathering && u.Cargo[s.I] > 0); k++) s.Sim.Tick();
        var points = new Vector2[OrderConstants.QueueCapacity + 1];
        for (int k = 0; k < points.Length; k++) points[k] = At(s.Sim, 4 + 4 * k, 20);
        foreach (Vector2 p in points) s.Sim.Enqueue(Command.Move(0, s.Worker, p, queued: true));
        ApplyPending(s.Sim);
        Assert.Equal(OrderConstants.QueueCapacity, u.QueueCount[s.I]);
        var goals = new List<Vector2>();
        Vector2 lastGoal = u.Goal[s.I];
        for (int k = 0; k < 20000 && (u.QueueCount[s.I] > 0 || u.State[s.I] != UnitState.Idle); k++)
        {
            s.Sim.Tick();
            if (!EconomySystem.OnLoop(u, s.I) && u.Goal[s.I] != lastGoal) goals.Add(lastGoal = u.Goal[s.I]);
        }
        Assert.Equal(OrderConstants.QueueCapacity, goals.Count);
        for (int k = 0; k < goals.Count; k++) Assert.True(Vector2.Distance(points[k], goals[k]) < 0.01f, $"leg {k}: {goals[k]} v {points[k]}");
        Assert.True(Vector2.Distance(points[OrderConstants.QueueCapacity - 1], u.Position[s.I]) < 1f, "never reached the 8th point");
    }

    /// <summary>
    /// BUG-1490 case 2 (a measurement of today's rule, not the Producer's answer): "Gather the node it stands at, then Move",
    /// queued behind a worker walking back loaded, pops the Gather at the drop-off out of reach (walks, so it ends at once).
    /// Queued behind a worker that delivers at a drop-off beside node B, "Gather B, then Move" pops in reach and runs a whole
    /// trip at B first. Printed and pinned so a change of the rule shows here.
    /// </summary>
    [Fact]
    public void Bug1490_APoppedGatherWithOrdersBehind_RunsATripOnlyWhenItPopsInReach()
    {
        Simulation sim = BuildMaps.NewSim(Flat(48, 24), units: 16, players: 1);
        World w = sim.World;
        Spawn(w, Mine, 30, 9, 2500);                 // node A, far east
        EntityHandle b = Spawn(w, Mine, 12, 9, 2500); // node B, flush on the Keep's east face (Keep cells 8-11)
        EntityHandle worker = CombatScenes.Place(sim, 0, Laborer, At(sim, 27, 9));
        Building(sim, 8, 8);
        sim.Enqueue(Command.Gather(0, worker, At(sim, 30, 9)));
        ApplyPending(sim);
        UnitStore u = w.Units;
        int i = worker.Index;
        for (int k = 0; k < 3000 && !(u.State[i] == UnitState.Gathering && u.Cargo[i] > 0); k++) sim.Tick();
        sim.Enqueue(Command.Gather(0, worker, CellOf(sim, b), queued: true));
        sim.Enqueue(Command.Move(0, worker, Target(sim), queued: true));
        ApplyPending(sim);
        int gold = w.GatheredGold[0], carry = w.Data.CarryOf(u.TypeId[i]), deliveries = 0, startedAt = -1, onB = -1;
        for (int k = 0; k < 4000 && startedAt < 0; k++)
        {
            int before = w.GatheredGold[0];
            sim.Tick();
            if (w.GatheredGold[0] != before) deliveries++;
            if (onB < 0 && u.GatherNode[i] == b) onB = sim.TickNumber;
            if (!EconomySystem.OnLoop(u, i) && u.QueueCount[i] == 0 && Vector2.Distance(u.Goal[i], Target(sim)) < 0.01f) startedAt = sim.TickNumber;
        }
        _out.WriteLine($"popped Gather on B at tick {onB}; the Move started at {startedAt} after {deliveries} deliveries ({w.GatheredGold[0] - gold} gold)");
        Assert.True(onB > 0 && startedAt > 0);
        Assert.Equal(2, deliveries); // node A's load, then one whole trip at B (today's rule, BUG-1490)
        Assert.Equal(gold + 2 * carry, w.GatheredGold[0]);
    }

    /// <summary>A queued Gather on the same node: one delivery, then the loop goes on at that node with an empty queue, delivering every trip.</summary>
    [Fact]
    public void AQueuedGatherOnTheSameNode_TheLoopGoesOnThere()
    {
        Scene s = Make(gatherer: false, wood: false);
        UnitStore u = s.U;
        for (int k = 0; k < 3000 && !(u.State[s.I] == UnitState.Gathering && u.Cargo[s.I] > 0); k++) s.Sim.Tick();
        s.Sim.Enqueue(Command.Gather(0, s.Worker, CellOf(s.Sim, s.NodeA), queued: true));
        ApplyPending(s.Sim);
        int gathered = s.Gathered;
        GatherMaps.Run(s.Sim, 3000);
        Assert.True(EconomySystem.OnLoop(u, s.I));
        Assert.Equal(s.NodeA, u.GatherNode[s.I]);
        Assert.Equal(0, u.QueueCount[s.I]);
        Assert.True(s.Gathered - gathered >= 3 * s.Carry, $"delivered only {s.Gathered - gathered} in 3,000 ticks");
    }

    /// <summary>A queued Build it cannot afford at its turn: dropped when it pops, the worker stands Idle off its loop (no site), and a Move behind it starts.</summary>
    [Fact]
    public void AQueuedBuildItCannotAfford_IsDropped_AndTheNextOrderStarts()
    {
        Scene s = Make(gatherer: false, wood: false);
        UnitStore u = s.U;
        for (int k = 0; k < 3000 && !(u.State[s.I] == UnitState.Gathering && u.Cargo[s.I] > 0); k++) s.Sim.Tick();
        s.Sim.Enqueue(Command.Build(0, s.Worker, BuildMaps.House, At(s.Sim, 14, 20), queued: true));
        s.Sim.Enqueue(Command.Move(0, s.Worker, Target(s.Sim), queued: true));
        ApplyPending(s.Sim);
        BuildMaps.SetTotals(s.Sim, 0, 0, 0); // the gold load it delivers is all it will have
        int sites = s.Sim.World.Buildings.Count;
        for (int k = 0; k < 4000 && !StartedMove(s); k++) s.Sim.Tick();
        Assert.True(StartedMove(s), $"the Move behind the refused Build never started: state {u.State[s.I]}, queue {u.QueueCount[s.I]}");
        Assert.Equal(sites, s.Sim.World.Buildings.Count);
        Assert.Equal(0, u.BuildTarget[s.I].Generation);
    }

    /// <summary>A queued order and an unqueued one in the same tick: the unqueued replaces all (queue empty, cargo kept); the other order way round, the queued one follows the unqueued Move.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void QueuedAndUnqueuedInOneTick(bool queuedFirst)
    {
        Scene s = Make(gatherer: false, wood: false);
        UnitStore u = s.U;
        for (int k = 0; k < 3000 && !(u.State[s.I] == UnitState.Gathering && u.Cargo[s.I] > 0); k++) s.Sim.Tick();
        Vector2 other = At(s.Sim, 4, 20);
        Command queued = Command.Move(0, s.Worker, Target(s.Sim), queued: true), now = Command.Move(0, s.Worker, other);
        s.Sim.Enqueue(queuedFirst ? queued : now);
        s.Sim.Enqueue(queuedFirst ? now : queued);
        int cargo = u.Cargo[s.I], gathered = s.Gathered;
        ApplyPending(s.Sim);
        Assert.False(EconomySystem.OnLoop(u, s.I));
        Assert.True(Vector2.Distance(u.Goal[s.I], other) < 0.01f);
        Assert.Equal(queuedFirst ? 0 : 1, u.QueueCount[s.I]);
        Assert.True(u.Cargo[s.I] >= cargo);
        Assert.Equal(gathered, s.Gathered);
    }

    /// <summary>The worker killed on the tick its queued order applies: no throw, its slot's queue is cleared, and a unit spawned into the slot starts clean.</summary>
    [Fact]
    public void TheWorkerDiesOnTheTickItsQueuedOrderApplies_TheSlotComesBackClean()
    {
        Scene s = Make(gatherer: false, wood: false);
        UnitStore u = s.U;
        for (int k = 0; k < 3000 && !(u.State[s.I] == UnitState.Gathering && u.Cargo[s.I] > 0); k++) s.Sim.Tick();
        int i = s.I;
        s.Sim.Enqueue(Command.Move(0, s.Worker, Target(s.Sim), queued: true));
        s.Sim.Enqueue(Command.Move(0, s.Worker, At(s.Sim, 4, 20), queued: true));
        u.Free(s.Worker);
        s.Sim.Tick();
        Assert.Equal(0, u.QueueCount[i]);
        EntityHandle next = CombatScenes.Place(s.Sim, 0, Laborer, At(s.Sim, 14, 18));
        Assert.Equal(0, u.QueueCount[next.Index]);
        Assert.False(EconomySystem.OnLoop(u, next.Index));
        GatherMaps.Run(s.Sim, 100);
        Assert.Equal(UnitState.Idle, u.State[next.Index]);
    }
}
