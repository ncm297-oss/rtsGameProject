using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-S11 (docs/01 2026-11-15 (b) item 5, docs/02 "Queued orders (Shift)", docs/03 "Implementation (M6-S11)"): an order
/// Shift-queued behind a gathering worker starts once its trip in hand is done. Walking out empty it starts at once; working
/// at its node or walking back loaded the worker delivers once, does not walk out again, and starts the queue in the phase 7
/// of the delivery tick. An unqueued order still replaces the loop at once (cargo kept).
/// </summary>
public class GatherQueueTests
{
    // 48 x 24 flat map, 2 players: a Keep on cells 8-11 x 8-11; gold mines A (18, 9) and B (28, 9); trees A (18, 16) and B (26, 16);
    // player 1's lone soldier far east, spotted for player 0 (the Attack target).
    private const int KeepX = 8, KeepY = 8;

    /// <summary>Where on its trip the worker is when the queued order applies.</summary>
    public enum Phase
    {
        /// <summary>Carrying nothing, walking to its node.</summary>
        WalkingOut,
        /// <summary>In reach of its node, part of a load carried.</summary>
        Working,
        /// <summary>A full load, walking to the Keep.</summary>
        WalkingBack,
    }

    /// <summary>The queued order's kind (every unit order a worker takes; abilities are not a worker's).</summary>
    public enum Queued { Move, AttackMove, Attack, GatherOther, Build, Repair, Stop, Hold }

    private sealed class Scene
    {
        public required Simulation Sim;
        public required EntityHandle Worker, Keep, Enemy, NodeA, NodeB;
        public required bool Wood;
        public int I => Worker.Index;
        public UnitStore U => Sim.World.Units;
        public int Carry => Sim.World.Data.CarryOf(U.TypeId[I]);
        public int Gathered => Wood ? Sim.World.GatheredWood[0] : Sim.World.GatheredGold[0];
    }

    private static Scene Make(int workerType, bool wood, bool otherNodes = true, int nodeAmount = 0)
    {
        Simulation sim = BuildMaps.NewSim(Flat(48, 24), units: 16, players: 2);
        World w = sim.World;
        EntityHandle nodeA = wood ? Spawn(w, Tree, 18, 16, nodeAmount > 0 ? nodeAmount : TreeWood) : Spawn(w, Mine, 18, 9, nodeAmount > 0 ? nodeAmount : 2500);
        EntityHandle nodeB = default;
        if (otherNodes) nodeB = wood ? Spawn(w, Tree, 26, 16, TreeWood) : Spawn(w, Mine, 28, 9, 2500);
        EntityHandle worker = CombatScenes.Place(sim, 0, workerType, At(sim, 14, wood ? 14 : 9));
        EntityHandle enemy = CombatScenes.Place(sim, 1, Infantry, At(sim, 45, 21));
        EntityHandle keep = Building(sim, KeepX, KeepY);
        CombatScenes.Spot(sim, 0, enemy);
        BuildMaps.Give(sim, 0, 1000, 1000);
        return new Scene { Sim = sim, Worker = worker, Keep = keep, Enemy = enemy, NodeA = nodeA, NodeB = nodeB, Wood = wood };
    }

    private static Vector2 NodeCell(Scene s, EntityHandle node)
    {
        int cell = s.Sim.World.Resources.Cell[node.Index];
        int width = s.Sim.World.NavGrid.Width;
        return s.Sim.World.NavGrid.CellCenter(cell % width, cell / width);
    }

    private static void ApplyPending(Simulation sim)
    {
        while (sim.PendingCommandCount > 0) sim.Tick();
    }

    /// <summary>Sends the worker to node A and ticks until it is at <paramref name="phase"/> of its first trip.</summary>
    private static void Reach(Scene s, Phase phase)
    {
        s.Sim.Enqueue(Command.Gather(0, s.Worker, NodeCell(s, s.NodeA)));
        ApplyPending(s.Sim);
        UnitStore u = s.U;
        for (int t = 0; t < 3000; t++)
        {
            bool there = phase switch
            {
                Phase.WalkingOut => u.State[s.I] == UnitState.Moving && u.Cargo[s.I] == 0,
                Phase.Working => u.State[s.I] == UnitState.Gathering && u.Cargo[s.I] > 0 && u.Cargo[s.I] < s.Carry,
                _ => u.State[s.I] == UnitState.Moving && u.Cargo[s.I] == s.Carry,
            };
            if (there && EconomySystem.OnLoop(u, s.I)) return;
            s.Sim.Tick();
        }
        Assert.Fail($"never reached {phase}");
    }

    private static Vector2 MoveTarget(Scene s) => At(s.Sim, 30, 20);

    private static Vector2 BuildSpot(Scene s) => At(s.Sim, 14, 20);

    private static void Enqueue(Scene s, Queued kind)
    {
        EntityHandle h = s.Worker;
        Command c = kind switch
        {
            Queued.Move => Command.Move(0, h, MoveTarget(s), queued: true),
            Queued.AttackMove => Command.AttackMove(0, h, MoveTarget(s), queued: true),
            Queued.Attack => Command.Attack(0, h, s.Enemy, isBuilding: false, queued: true),
            Queued.GatherOther => Command.Gather(0, h, NodeCell(s, s.NodeB), queued: true),
            Queued.Build => Command.Build(0, h, BuildMaps.House, BuildSpot(s), queued: true),
            Queued.Repair => Command.Repair(0, h, At(s.Sim, KeepX + 1, KeepY + 1), queued: true),
            Queued.Stop => Command.Stop(0, h, queued: true),
            _ => Command.HoldPosition(0, h, queued: true),
        };
        s.Sim.Enqueue(c);
    }

    /// <summary>Asserts the worker has started <paramref name="kind"/> (the queued order popped, state as that order sets it).</summary>
    private static void AssertStarted(Scene s, Queued kind, int queueAfter)
    {
        UnitStore u = s.U;
        int i = s.I;
        Assert.Equal(queueAfter, u.QueueCount[i]);
        switch (kind)
        {
            case Queued.Move:
            case Queued.AttackMove:
                Assert.False(EconomySystem.OnLoop(u, i));
                Assert.Equal(UnitState.Moving, u.State[i]);
                Assert.True(Vector2.Distance(MoveTarget(s), u.Goal[i]) < 0.01f, $"goal {u.Goal[i]}");
                if (kind == Queued.AttackMove) Assert.Equal(Combat.CombatMode.AttackMove, u.Mode[i]);
                break;
            case Queued.Attack:
                Assert.False(EconomySystem.OnLoop(u, i));
                Assert.Equal(s.Enemy, u.Target[i]);
                break;
            case Queued.GatherOther:
                Assert.Equal(s.NodeB, u.GatherNode[i]);
                break;
            case Queued.Build:
                Assert.False(EconomySystem.OnLoop(u, i));
                Assert.NotEqual(0, u.BuildTarget[i].Generation);
                Assert.True(s.Sim.World.Buildings.UnderConstruction[u.BuildTarget[i].Index]);
                break;
            case Queued.Repair:
                Assert.False(EconomySystem.OnLoop(u, i));
                Assert.Equal(s.Keep, u.BuildTarget[i]);
                break;
            case Queued.Stop:
            case Queued.Hold:
                Assert.False(EconomySystem.OnLoop(u, i));
                Assert.Equal(UnitState.Idle, u.State[i]);
                Assert.Equal(kind == Queued.Hold, u.Hold[i]);
                break;
        }
    }

    /// <summary>
    /// Ticks until the worker delivers: on every tick before, it stays on its loop with the queue untouched; on the delivery
    /// tick the total rises by the full load and the queued order has started (the phase 7 of that tick).
    /// </summary>
    private static void AssertDeliversOnceThenStarts(Scene s, Queued kind, int queueAfter, int queuedCount)
    {
        UnitStore u = s.U;
        int gathered = s.Gathered, carry = s.Carry;
        for (int t = 0; t < 3000; t++)
        {
            s.Sim.Tick();
            if (s.Gathered != gathered)
            {
                Assert.Equal(gathered + carry, s.Gathered);
                Assert.Equal(0, u.Cargo[s.I]);
                AssertStarted(s, kind, queueAfter);
                return;
            }
            Assert.True(EconomySystem.OnLoop(u, s.I), $"left the loop at tick {s.Sim.TickNumber} before delivering");
            Assert.Equal(queuedCount, u.QueueCount[s.I]);
        }
        Assert.Fail("never delivered");
    }

    /// <summary>Criterion 2: walking out empty, a queued Move starts within 2 ticks of the command applying (here: the same tick).</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void WalkingOutEmpty_AQueuedMove_StartsWithinTwoTicksOfApplying(bool gatherer, bool wood)
    {
        Scene s = Make(gatherer ? GatherOverlapScenes.GathererType : Laborer, wood);
        Reach(s, Phase.WalkingOut);
        int gathered = s.Gathered;
        Enqueue(s, Queued.Move);
        ApplyPending(s.Sim);
        int applied = s.Sim.TickNumber;
        while (EconomySystem.OnLoop(s.U, s.I) && s.Sim.TickNumber < applied + 2) s.Sim.Tick();
        AssertStarted(s, Queued.Move, 0);
        Assert.True(s.Sim.TickNumber - applied <= 2, $"started {s.Sim.TickNumber - applied} ticks after applying");
        Assert.Equal(gathered, s.Gathered);
    }

    /// <summary>Criterion 3, gold and wood, a Laborer and a Teblor Gatherer: working or walking back, it delivers its load once, then the queued Move starts.</summary>
    [Theory]
    [InlineData(false, false, Phase.Working)]
    [InlineData(false, false, Phase.WalkingBack)]
    [InlineData(true, false, Phase.Working)]
    [InlineData(true, false, Phase.WalkingBack)]
    [InlineData(false, true, Phase.Working)]
    [InlineData(false, true, Phase.WalkingBack)]
    [InlineData(true, true, Phase.Working)]
    [InlineData(true, true, Phase.WalkingBack)]
    public void WorkingOrWalkingBack_DeliversOnce_ThenStartsTheQueuedMove(bool gatherer, bool wood, Phase phase)
    {
        Scene s = Make(gatherer ? GatherOverlapScenes.GathererType : Laborer, wood);
        Reach(s, phase);
        Enqueue(s, Queued.Move);
        ApplyPending(s.Sim);
        AssertDeliversOnceThenStarts(s, Queued.Move, 0, 1);
    }

    /// <summary>Criterion 3, every queued kind (Laborer, gold), with a Move queued behind it: the order starts after the delivery and the Move stays queued (Stop and Hold drop it).</summary>
    [Theory]
    [InlineData(Queued.Move, Phase.Working)]
    [InlineData(Queued.AttackMove, Phase.Working)]
    [InlineData(Queued.Attack, Phase.Working)]
    [InlineData(Queued.GatherOther, Phase.Working)]
    [InlineData(Queued.Build, Phase.Working)]
    [InlineData(Queued.Repair, Phase.Working)]
    [InlineData(Queued.Stop, Phase.Working)]
    [InlineData(Queued.Hold, Phase.Working)]
    [InlineData(Queued.Move, Phase.WalkingBack)]
    [InlineData(Queued.AttackMove, Phase.WalkingBack)]
    [InlineData(Queued.Attack, Phase.WalkingBack)]
    [InlineData(Queued.GatherOther, Phase.WalkingBack)]
    [InlineData(Queued.Build, Phase.WalkingBack)]
    [InlineData(Queued.Repair, Phase.WalkingBack)]
    [InlineData(Queued.Stop, Phase.WalkingBack)]
    [InlineData(Queued.Hold, Phase.WalkingBack)]
    public void EveryQueuedKind_StartsAfterTheDelivery(Queued kind, Phase phase)
    {
        Scene s = Make(Laborer, wood: false);
        if (kind == Queued.Repair) s.Sim.World.Buildings.Damage(s.Keep, 300);
        Reach(s, phase);
        Enqueue(s, kind);
        s.Sim.Enqueue(Command.Move(0, s.Worker, At(s.Sim, 4, 20), queued: true));
        ApplyPending(s.Sim);
        bool terminal = kind is Queued.Stop or Queued.Hold;
        AssertDeliversOnceThenStarts(s, kind, terminal ? 0 : 1, 2);
    }

    /// <summary>Criterion 3: a queued Gather on another node, alone in the queue, ends at that node's loop; it delivers there in turn.</summary>
    [Fact]
    public void AQueuedGatherOnAnotherNode_EndsAtThatNodesLoop()
    {
        Scene s = Make(Laborer, wood: false);
        Reach(s, Phase.Working);
        Enqueue(s, Queued.GatherOther);
        ApplyPending(s.Sim);
        AssertDeliversOnceThenStarts(s, Queued.GatherOther, 0, 1);
        int bBefore = s.Sim.World.Resources.Remaining[s.NodeB.Index], gathered = s.Gathered;
        GatherMaps.Run(s.Sim, 2000);
        Assert.Equal(s.NodeB, s.U.GatherNode[s.I]);
        Assert.True(s.Sim.World.Resources.Remaining[s.NodeB.Index] < bBefore, "never worked node B");
        Assert.True(s.Gathered > gathered, "never delivered from node B");
    }

    /// <summary>Criterion 3: a queued Build builds: the house goes up after the delivery.</summary>
    [Fact]
    public void AQueuedBuild_Builds()
    {
        Scene s = Make(Laborer, wood: false);
        Reach(s, Phase.WalkingBack);
        Enqueue(s, Queued.Build);
        ApplyPending(s.Sim);
        AssertDeliversOnceThenStarts(s, Queued.Build, 0, 1);
        int site = s.U.BuildTarget[s.I].Index;
        for (int t = 0; t < 4000 && s.Sim.World.Buildings.UnderConstruction[site]; t++) s.Sim.Tick();
        Assert.True(s.Sim.World.Buildings.Alive[site] && !s.Sim.World.Buildings.UnderConstruction[site], "the house never finished");
    }

    /// <summary>Criterion 3: the rest of the queue follows as for any unit: two queued Moves, the second starts on arrival at the first.</summary>
    [Fact]
    public void TheRestOfTheQueue_FollowsAsForAnyUnit()
    {
        Scene s = Make(Laborer, wood: false);
        Reach(s, Phase.Working);
        Enqueue(s, Queued.Move);
        Vector2 second = At(s.Sim, 4, 20);
        s.Sim.Enqueue(Command.Move(0, s.Worker, second, queued: true));
        ApplyPending(s.Sim);
        AssertDeliversOnceThenStarts(s, Queued.Move, 1, 2);
        for (int t = 0; t < 2000 && s.U.QueueCount[s.I] > 0; t++) s.Sim.Tick();
        Assert.Equal(0, s.U.QueueCount[s.I]);
        Assert.True(Vector2.Distance(second, s.U.Goal[s.I]) < 0.01f);
        GatherMaps.Run(s.Sim, 600);
        Assert.True(Vector2.Distance(second, s.U.Position[s.I]) < 1f, $"at {s.U.Position[s.I]}");
        Assert.False(EconomySystem.OnLoop(s.U, s.I));
    }

    /// <summary>Criterion 4: an unqueued order to a loaded worker walking back replaces everything at once, cargo kept.</summary>
    [Theory]
    [InlineData(Phase.Working)]
    [InlineData(Phase.WalkingBack)]
    public void AnUnqueuedOrder_StillReplacesAtOnce_CargoKept(Phase phase)
    {
        Scene s = Make(Laborer, wood: false);
        Reach(s, phase);
        Enqueue(s, Queued.Build); // something queued first: the unqueued order drops it too
        ApplyPending(s.Sim);
        int cargo = s.U.Cargo[s.I], gathered = s.Gathered;
        Vector2 target = At(s.Sim, 4, 20);
        s.Sim.Enqueue(Command.Move(0, s.Worker, target));
        ApplyPending(s.Sim);
        Assert.False(EconomySystem.OnLoop(s.U, s.I));
        Assert.Equal(0, s.U.QueueCount[s.I]);
        Assert.Equal(UnitState.Moving, s.U.State[s.I]);
        Assert.True(Vector2.Distance(target, s.U.Goal[s.I]) < 0.01f);
        Assert.True(s.U.Cargo[s.I] >= cargo, $"cargo {s.U.Cargo[s.I]} was {cargo}"); // working: the apply tick may take one more unit
        Assert.Equal(gathered, s.Gathered);
    }

    /// <summary>Scope: a worker carrying cargo with no own drop-off standing starts its queue at once and keeps the cargo.</summary>
    [Theory]
    [InlineData(Phase.Working)]
    [InlineData(Phase.WalkingBack)]
    public void NoDropOffStanding_TheQueueStartsAtOnce_CargoKept(Phase phase)
    {
        Scene s = Make(Laborer, wood: false);
        Reach(s, phase);
        s.Sim.World.Buildings.Damage(s.Keep, int.MaxValue);
        Assert.False(s.Sim.World.Buildings.IsAlive(s.Keep));
        int cargo = s.U.Cargo[s.I];
        Enqueue(s, Queued.Move);
        ApplyPending(s.Sim);
        int applied = s.Sim.TickNumber;
        while (EconomySystem.OnLoop(s.U, s.I) && s.Sim.TickNumber < applied + 2) s.Sim.Tick();
        AssertStarted(s, Queued.Move, 0);
        Assert.True(s.U.Cargo[s.I] >= cargo && s.U.Cargo[s.I] > 0, $"cargo {s.U.Cargo[s.I]} was {cargo}");
    }

    /// <summary>Scope: where a loop leaves the worker Idle today (its node runs out, no other in reach of the search), the queue starts as today, cargo kept.</summary>
    [Fact]
    public void TheNodeRunsOutWithNoOther_TheQueueStarts_CargoKept()
    {
        Scene s = Make(Laborer, wood: false, otherNodes: false, nodeAmount: 4);
        Reach(s, Phase.Working);
        Enqueue(s, Queued.Move);
        ApplyPending(s.Sim);
        int gathered = s.Gathered;
        for (int t = 0; t < 2000 && EconomySystem.OnLoop(s.U, s.I); t++) s.Sim.Tick();
        AssertStarted(s, Queued.Move, 0);
        Assert.Equal(4, s.U.Cargo[s.I]);
        Assert.Equal(gathered, s.Gathered);
    }

    /// <summary>
    /// The rule as written (docs/02): a popped Gather with more orders behind it is a loop with a queue, and walking out empty
    /// it ends at once, so the next order starts the tick after the Gather did (BUG-1490: the Producer's call whether a queued
    /// Gather followed by more orders should do one trip first, which needs state the save does not hold).
    /// </summary>
    [Fact]
    public void APoppedGather_WithOrdersBehindIt_EndsWhileWalkingOutEmpty()
    {
        Scene s = Make(Laborer, wood: false);
        Reach(s, Phase.Working);
        Enqueue(s, Queued.GatherOther);
        s.Sim.Enqueue(Command.Move(0, s.Worker, MoveTarget(s), queued: true));
        ApplyPending(s.Sim);
        AssertDeliversOnceThenStarts(s, Queued.GatherOther, 1, 2);
        s.Sim.Tick();
        AssertStarted(s, Queued.Move, 0);
    }

    /// <summary>A worker with an empty queue goes on with its loop as before: it delivers and walks back out to the same node.</summary>
    [Fact]
    public void WithAnEmptyQueue_TheLoopGoesOn()
    {
        Scene s = Make(Laborer, wood: false);
        Reach(s, Phase.WalkingBack);
        int gathered = s.Gathered;
        for (int t = 0; t < 2000 && s.Gathered == gathered; t++) s.Sim.Tick();
        Assert.True(EconomySystem.OnLoop(s.U, s.I));
        Assert.Equal(s.NodeA, s.U.GatherNode[s.I]);
        Assert.Equal(UnitState.Moving, s.U.State[s.I]);
    }
}
