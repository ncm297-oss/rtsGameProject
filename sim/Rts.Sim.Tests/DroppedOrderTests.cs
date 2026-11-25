using Rts.Sim.Commands;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Orders;
using Xunit.Abstractions;
using static Rts.Sim.Tests.BuildMaps;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-8 (Request 58, docs/03 "Implementation (M7-8)"): <see cref="World.DroppedOrders"/>, the shift-queued orders dropped in the
/// last tick: at their turn (phase 7 popped one that could not start; a refused Build with its <see cref="PlacementError"/>)
/// or on arrival with the unit's queue full. Emptied at the start of every tick, not hashed. One worker of player 0 on a
/// flat 30 x 20 map, the M3-3 scenes.
/// </summary>
[Collection(SerialCollection.Name)]
public class DroppedOrderTests
{
    private readonly ITestOutputHelper _out;

    public DroppedOrderTests(ITestOutputHelper output) => _out = output;

    /// <summary>Ticks once; the entries the tick left (a copy).</summary>
    private static DroppedOrder[] Tick(Simulation sim)
    {
        sim.Tick();
        return sim.World.DroppedOrders.ToArray();
    }

    /// <summary>The tick the commands enqueued now apply in (a command is stamped for the tick after the next): the first tick lists nothing; that tick's entries.</summary>
    private static DroppedOrder[] ApplyTick(Simulation sim)
    {
        Assert.Empty(Tick(sim));
        return Tick(sim);
    }

    /// <summary>
    /// A worker walking, then a shift-queued House it cannot pay (0 wood of its 50) behind the walk: nothing is listed while
    /// it walks; at its turn (the tick the walk ends and phase 7 pops the Build) the list holds it exactly once, with the
    /// unit, its owner, Build and <see cref="PlacementError.CannotAfford"/>; the next tick the list is empty again.
    /// </summary>
    [Fact]
    public void AQueuedBuildThatCannotBePaidAtItsTurn_IsListedOnce_ThenTheListIsEmpty()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20));
        EntityHandle w = Unit(sim, At(sim, 3, 3));
        SetTotals(sim, 0, 0, 0);
        sim.Enqueue(Command.Move(0, w, At(sim, 8, 3)));
        sim.Enqueue(Command.Build(0, w, House, At(sim, 20, 10), queued: true));
        int at = -1;
        for (int t = 0; t < 400 && at < 0; t++)
        {
            DroppedOrder[] list = Tick(sim);
            if (list.Length == 0) continue;
            at = sim.TickNumber;
            DroppedOrder e = Assert.Single(list);
            Assert.Equal(new DroppedOrder(w, 0, CommandKind.Build, PlacementError.CannotAfford, QueueFull: false), e);
        }
        Assert.True(at > 2, $"the Build was dropped at tick {at}: before the walk ended, or never");
        Assert.Equal(0, sim.World.Units.QueueCount[w.Index]);
        Assert.Empty(Tick(sim));
        Assert.Equal(0, sim.World.Buildings.Count);
    }

    /// <summary>A queued Build onto a blocked site (a finished House of the player's already stands there, paid for) is listed with that refusal's own error, not CannotAfford.</summary>
    [Fact]
    public void AQueuedBuildOnABlockedSite_IsListedWithItsOwnError()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20));
        EntityHandle w = Unit(sim, At(sim, 3, 3));
        Assert.True(sim.World.Buildings.Spawn(0, House, Cell(sim, 10, 10), out _));
        SetTotals(sim, 0, 500, 500);
        sim.Enqueue(Command.Build(0, w, Keep, At(sim, 9, 9), queued: true));
        DroppedOrder e = Assert.Single(ApplyTick(sim));
        Assert.Equal((w, 0, CommandKind.Build, false), (e.Unit, e.Owner, e.Kind, e.QueueFull));
        Assert.NotEqual(PlacementError.CannotAfford, e.Placement);
        Assert.NotEqual(PlacementError.None, e.Placement);
        Assert.Equal(PlacementError.Blocked, e.Placement);
        Assert.Equal((500, 500), (sim.World.Gold[0], sim.World.Wood[0]));
    }

    /// <summary>A queued Build that starts (paid, placeable) is not listed, at its turn or after.</summary>
    [Fact]
    public void AQueuedBuildThatStarts_IsNotListed()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20));
        EntityHandle w = Unit(sim, At(sim, 3, 3));
        SetTotals(sim, 0, 0, 300);
        sim.Enqueue(Command.Move(0, w, At(sim, 8, 3)));
        sim.Enqueue(Command.Build(0, w, House, At(sim, 20, 10), queued: true));
        for (int t = 0; t < 400; t++) Assert.Empty(Tick(sim));
        Assert.True(SiteAt(sim, 20, 10) >= 0, "the queued House never started");
    }

    /// <summary>
    /// The other kinds dropped at their turn are listed the same way, with no placement error: a queued Gather whose node is
    /// gone (here: no node at the point) and a queued Repair with nothing to repair.
    /// </summary>
    [Fact]
    public void AQueuedGatherOrRepairWithNothingThere_IsListedWithNoPlacementError()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20));
        EntityHandle w = Unit(sim, At(sim, 3, 3));
        sim.Enqueue(Command.Gather(0, w, At(sim, 20, 10), queued: true));
        DroppedOrder gather = Assert.Single(ApplyTick(sim));
        Assert.Equal(new DroppedOrder(w, 0, CommandKind.Gather, PlacementError.None, QueueFull: false), gather);
        sim.Enqueue(Command.Repair(0, w, At(sim, 20, 10), queued: true));
        DroppedOrder repair = Assert.Single(ApplyTick(sim));
        Assert.Equal(new DroppedOrder(w, 0, CommandKind.Repair, PlacementError.None, QueueFull: false), repair);
        Assert.Empty(Tick(sim));
    }

    /// <summary>
    /// A full queue's refusal on arrival (the command's <see cref="CommandRefusal.UnitQueueFull"/>, which before M7-8 reached
    /// only the test seam) is listed with <see cref="DroppedOrder.QueueFull"/>: a walking worker given
    /// <see cref="OrderConstants.QueueCapacity"/> queued Moves and one more.
    /// </summary>
    [Fact]
    public void AnOrderRefusedByAFullQueue_IsListedAsQueueFull()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20));
        EntityHandle w = Unit(sim, At(sim, 3, 3));
        sim.Enqueue(Command.Move(0, w, At(sim, 28, 18)));
        Assert.Empty(ApplyTick(sim));
        for (int k = 0; k < OrderConstants.QueueCapacity; k++) sim.Enqueue(Command.Move(0, w, At(sim, 3 + k, 15), queued: true));
        sim.Enqueue(Command.AttackMove(0, w, At(sim, 25, 15), queued: true));
        DroppedOrder e = Assert.Single(ApplyTick(sim));
        Assert.Equal(new DroppedOrder(w, 0, CommandKind.AttackMove, PlacementError.None, QueueFull: true), e);
        Assert.Equal(OrderConstants.QueueCapacity, sim.World.Units.QueueCount[w.Index]);
        Assert.Empty(Tick(sim));
    }

    /// <summary>The list is output, not state: two sims that differ only in a dropped queued Build and a Noop hash the same.</summary>
    [Fact]
    public void TheList_IsNotHashed()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20)), twin = BuildMaps.NewSim(Flat(30, 20));
        EntityHandle w = Unit(sim, At(sim, 3, 3));
        Unit(twin, At(twin, 3, 3));
        SetTotals(sim, 0, 0, 0);
        SetTotals(twin, 0, 0, 0);
        sim.Enqueue(Command.Build(0, w, House, At(sim, 20, 10), queued: true));
        twin.Enqueue(Command.Noop(0));
        Assert.Single(ApplyTick(sim));
        Assert.Empty(ApplyTick(twin));
        Assert.Equal(twin.StateHash(), sim.StateHash());
    }

    /// <summary>Criterion 12's allocation row: 100 ticks, each dropping a queued House the worker cannot pay, allocate nothing (the commands enqueued inside the block).</summary>
    [Fact]
    public void ATickThatDropsAnOrder_AllocatesNothing()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20));
        EntityHandle w = Unit(sim, At(sim, 3, 3));
        SetTotals(sim, 0, 0, 0);
        int dropped = 0;
        void Block()
        {
            for (int t = 0; t < 100; t++)
            {
                sim.Enqueue(Command.Build(0, w, House, At(sim, 20, 10), queued: true));
                sim.Tick();
                dropped += sim.World.DroppedOrders.Length;
            }
        }
        Block(); // warm: the first run's JIT
        dropped = 0;
        AllocationProbe.AssertZero(Block, _out, setup: () => dropped = 0);
        Assert.Equal(100, dropped);
    }
}
