using Rts.Sim.Commands;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Orders;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;
using static Rts.Sim.Tests.BuildMaps;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M7-8 (Request 58): attacks on <see cref="World.DroppedOrders"/> beyond the developer's rows: two entries for one unit in
/// one tick, the list at and past its capacity, a list left unread for 1,000 ticks, an entry for a unit that dies in the
/// same tick, and a save taken with entries in the list.
/// </summary>
[Collection(SerialCollection.Name)]
public class DroppedOrderQaTests
{
    private readonly ITestOutputHelper _out;

    public DroppedOrderQaTests(ITestOutputHelper output) => _out = output;

    private static DroppedOrder[] Tick(Simulation sim)
    {
        sim.Tick();
        return sim.World.DroppedOrders.ToArray();
    }

    /// <summary>
    /// One unit, one tick, two entries: an Idle worker whose queue is one short of full is sent two queued Gathers to a point
    /// with no node. Phase 1 takes the first (the queue is full) and refuses the second (an entry with QueueFull); phase 7
    /// pops the head, a Gather with no node, and drops it (a second entry, not QueueFull). Order: phase 1's first.
    /// </summary>
    [Fact]
    public void OneUnit_CanHaveAQueueFullAndATurnEntry_InTheSameTick()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20), combat: false);
        EntityHandle w = Unit(sim, At(sim, 3, 3));
        Assert.Equal(UnitState.Idle, sim.World.Units.State[w.Index]);
        // From Idle every queued Gather to nothing pops and drops one a tick; so fill the queue in one tick: capacity + 1
        // commands, the last refused, and the head popped and dropped in the same tick.
        for (int k = 0; k < OrderConstants.QueueCapacity + 1; k++) sim.Enqueue(Command.Gather(0, w, At(sim, 20, 2 + k % 8), queued: true));
        Assert.Empty(Tick(sim));
        DroppedOrder[] list = Tick(sim);
        _out.WriteLine(string.Join("\n", list.Select(e => e.ToString())));
        Assert.Equal(2, list.Length);
        Assert.Equal(new DroppedOrder(w, 0, CommandKind.Gather, PlacementError.None, QueueFull: true), list[0]);
        Assert.Equal(new DroppedOrder(w, 0, CommandKind.Gather, PlacementError.None, QueueFull: false), list[1]);
        Assert.Equal(OrderConstants.QueueCapacity - 1, sim.World.Units.QueueCount[w.Index]);
    }

    /// <summary>
    /// The list at its capacity: <c>UnitCapacity + CommandCapacity</c> entries are recorded, the next one is not (and does not
    /// throw); the next tick starts empty. Recorded through the internal door, since the sim's own phases can't reach it.
    /// </summary>
    [Fact]
    public void TheList_AtCapacity_KeepsWhatFits_AndTheNextTickStartsEmpty()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20), units: 4, combat: false);
        World world = sim.World;
        int capacity = world.Config.UnitCapacity + world.Config.CommandCapacity;
        var e = new DroppedOrder(new EntityHandle(0, 1), 0, CommandKind.Move, PlacementError.None, QueueFull: false);
        for (int k = 0; k < capacity + 50; k++) world.RecordDroppedOrder(e);
        Assert.Equal(capacity, world.DroppedOrders.Length);
        sim.Tick();
        Assert.Equal(0, world.DroppedOrders.Length);
    }

    /// <summary>
    /// The heaviest load the phases can give, sustained and never read for 1,000 ticks: every unit of a small store Idle with
    /// a queued Gather to nothing dropped every tick (phase 7) and every command slot spent on a queued order refused by a
    /// full queue (phase 1). The list holds exactly that tick's entries each tick (it never accumulates), stays within its
    /// capacity, and the 1,000 ticks allocate nothing.
    /// </summary>
    [Fact]
    public void TheList_UnderSustainedDrops_UnreadFor1000Ticks_NeverAccumulates_AndAllocatesNothing()
    {
        const int units = 4;
        var config = TestSim.Config(Seed: 5, PlayerCount: 1, UnitCapacity: units, CommandCapacity: 128) with { Combat = false }; // two ticks' batches are pending at once
        Simulation sim = TestSim.Explored(new Simulation(config, Flat(30, 20)));
        var workers = new EntityHandle[units];
        for (int k = 0; k < units; k++) workers[k] = Unit(sim, At(sim, 3 + 2 * k, 3));
        int capacity = sim.World.Config.UnitCapacity + sim.World.Config.CommandCapacity;
        int max = 0, ticks = 0;
        long total = 0;
        var points = new System.Numerics.Vector2[OrderConstants.QueueCapacity];
        for (int k = 0; k < points.Length; k++) points[k] = At(sim, 20, 2 + k);
        void Block()
        {
            for (int t = 0; t < 1000; t++)
            {
                // 16 queued Gathers a unit a tick (64 commands): the first ones refill the queue, the rest are refused.
                for (int k = 0; k < units; k++)
                    for (int c = 0; c < 16; c++) sim.Enqueue(Command.Gather(0, workers[k], points[c % points.Length], queued: true));
                sim.Tick();
                int n = sim.World.DroppedOrders.Length;
                total += n;
                if (n > max) max = n;
                ticks++;
            }
        }
        Block(); // warm
        _out.WriteLine($"warm: {ticks} ticks, {total} entries, at most {max} a tick, capacity {capacity}");
        Assert.True(max <= capacity);
        Assert.True(max >= units, "the scene dropped fewer orders than it should");
        // Every tick after the first two: 4 phase-7 drops, and per unit 16 commands of which only those that fit are taken.
        (max, total, ticks) = (0, 0, 0);
        AllocationProbe.AssertZero(Block, _out, setup: () => (max, total, ticks) = (0, 0, 0));
        Assert.True(max <= capacity);
        Assert.Equal(1000L * (16 * units - units + units), total); // per unit: 1 accepted (refills to 8), 15 refused, 1 popped-and-dropped
    }

    /// <summary>
    /// A unit whose queued order is dropped in phase 7 and that is killed in phase 11 of the same tick: the entry is still
    /// listed (an order was dropped), and its handle is dead by the time the view reads it, as a <see cref="World.Deaths"/>
    /// victim's is. A view must not assume the unit is alive.
    /// </summary>
    [Fact]
    public void ADroppedOrder_OfAUnitKilledInTheSameTick_IsListed_WithADeadHandle()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20), units: 8, players: 2, combat: true);
        EntityHandle w = Unit(sim, At(sim, 10, 10));
        EntityHandle foe = Unit(sim, At(sim, 11, 10), player: 1, type: Infantry);
        sim.World.Units.Hp[w.Index] = 1;
        int found = -1;
        for (int t = 0; t < 400 && found < 0; t++)
        {
            if (sim.World.Units.IsAlive(w)) sim.Enqueue(Command.Gather(0, w, At(sim, 20, 2), queued: true));
            DroppedOrder[] list = Tick(sim);
            bool died = false;
            foreach (var d in sim.World.Deaths) if (!d.IsBuilding && d.Victim == w) died = true;
            if (!died) continue;
            found = sim.TickNumber;
            _out.WriteLine($"the worker died at tick {found}; that tick's list: {string.Join(", ", list.Select(e => e.ToString()))}");
            if (list.Length == 0) return; // the worker was fighting back that tick (a target set): no pop, no entry; nothing to check
            DroppedOrder e = Assert.Single(list);
            Assert.Equal(w, e.Unit);
            Assert.False(sim.World.Units.IsAlive(e.Unit));
        }
        Assert.True(found >= 0, "the worker never died");
    }

    /// <summary>
    /// A save taken right after a tick with entries: the loaded sim lists none (not saved, as docs/03 says), and the next tick
    /// lists in the loaded sim exactly what it lists in the uninterrupted one, at the same state hash.
    /// </summary>
    [Fact]
    public void ASaveTakenWithEntries_LoadsWithNone_AndTheNextTickListsTheSame()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20), combat: false);
        EntityHandle w = Unit(sim, At(sim, 3, 3));
        for (int k = 0; k < OrderConstants.QueueCapacity; k++) sim.Enqueue(Command.Gather(0, w, At(sim, 20, 2 + k), queued: true));
        Tick(sim);
        Assert.Single(Tick(sim));
        Simulation loaded = SaveTestRun.Load(sim.World.Data, SaveTestRun.Save(sim));
        Assert.Equal(0, loaded.World.DroppedOrders.Length);
        Assert.Equal(sim.StateHash(), loaded.StateHash());
        for (int t = 0; t < 10; t++)
        {
            DroppedOrder[] a = Tick(sim), b = Tick(loaded);
            Assert.Equal(a, b);
            Assert.Equal(sim.StateHash(), loaded.StateHash());
        }
    }
}
