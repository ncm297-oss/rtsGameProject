using Rts.Sim.Commands;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Orders;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;
using static Rts.Sim.Tests.BuildMaps;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>
/// M6-V27 (Request 58's view half, docs/03 "Implementation (M6-V27)"): <see cref="OrderNotices"/>, the dropped queued orders the
/// view tells a player about (only that player's; a refused Build with its error; queue-full flagged; every other kind left
/// out; an entry whose unit died or whose slot was reused reported not alive), 0 bytes over a full list, and reading it
/// leaves the state hash alone. Then <see cref="NoticeLines"/>, the pool of lines over the units.
/// </summary>
[Collection(SerialCollection.Name)]
public class OrderNoticesTests
{
    private readonly ITestOutputHelper _out;

    public OrderNoticesTests(ITestOutputHelper output) => _out = output;

    private static int Keep => TestSim.Data.FindBuilding("malazan_garrison_keep");

    private static OrderNotice[] Read(Simulation sim, int player)
    {
        var into = new OrderNotice[64];
        UnitStore u = sim.World.Units;
        int n = OrderNotices.Collect(sim.World.DroppedOrders, player, u.Alive, u.Generation, into);
        return into.AsSpan(0, n).ToArray();
    }

    /// <summary>Two players' workers each refused a queued House they cannot pay: each player's read gives only its own unit's notice, with CannotAfford, alive.</summary>
    [Fact]
    public void OnlyTheAskedPlayersEntries_ARefusedBuildCarriesItsError()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20), players: 2, combat: false);
        EntityHandle mine = Unit(sim, At(sim, 3, 3), player: 0);
        EntityHandle theirs = Unit(sim, At(sim, 3, 15), player: 1);
        SetTotals(sim, 0, 0, 0);
        SetTotals(sim, 1, 0, 0);
        sim.Enqueue(Command.Build(0, mine, House, At(sim, 20, 4), queued: true));
        sim.Enqueue(Command.Build(1, theirs, House, At(sim, 20, 14), queued: true));
        sim.Tick();
        sim.Tick();
        Assert.Equal(2, sim.World.DroppedOrders.Length);
        OrderNotice own = Assert.Single(Read(sim, 0));
        Assert.Equal(new OrderNotice(mine, true, CommandKind.Build, PlacementError.CannotAfford, false), own);
        OrderNotice other = Assert.Single(Read(sim, 1));
        Assert.Equal(theirs, other.Unit);
        Assert.Empty(Read(sim, 2));
    }

    /// <summary>A queued Keep onto a site a House already stands on: the notice carries Blocked, not CannotAfford.</summary>
    [Fact]
    public void ABlockedSite_CarriesBlocked()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20));
        EntityHandle w = Unit(sim, At(sim, 3, 3));
        Assert.True(sim.World.Buildings.Spawn(0, House, Cell(sim, 10, 10), out _));
        SetTotals(sim, 0, 500, 500);
        sim.Enqueue(Command.Build(0, w, Keep, At(sim, 9, 9), queued: true));
        sim.Tick();
        sim.Tick();
        OrderNotice n = Assert.Single(Read(sim, 0));
        Assert.Equal((w, true, CommandKind.Build, PlacementError.Blocked, false), (n.Unit, n.Alive, n.Kind, n.Placement, n.QueueFull));
    }

    /// <summary>A ninth queued order on a full queue: one notice flagged QueueFull, with the refused command's kind and no placement error.</summary>
    [Fact]
    public void AFullQueue_IsFlagged()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20));
        EntityHandle w = Unit(sim, At(sim, 3, 3));
        sim.Enqueue(Command.Move(0, w, At(sim, 28, 18)));
        sim.Tick();
        sim.Tick();
        for (int k = 0; k < OrderConstants.QueueCapacity; k++) sim.Enqueue(Command.Move(0, w, At(sim, 3 + k, 15), queued: true));
        sim.Enqueue(Command.Build(0, w, House, At(sim, 20, 10), queued: true));
        sim.Tick();
        sim.Tick();
        OrderNotice n = Assert.Single(Read(sim, 0));
        Assert.Equal(new OrderNotice(w, true, CommandKind.Build, PlacementError.None, true), n);
    }

    /// <summary>Every other kind dropped at its turn (a Gather with no node, a Repair with nothing there) gives no notice, though the sim lists it.</summary>
    [Fact]
    public void OtherKinds_GiveNoNotice()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20));
        EntityHandle w = Unit(sim, At(sim, 3, 3));
        sim.Enqueue(Command.Gather(0, w, At(sim, 20, 10), queued: true));
        sim.Tick();
        sim.Tick();
        Assert.Single(sim.World.DroppedOrders.ToArray());
        Assert.Empty(Read(sim, 0));
        sim.Enqueue(Command.Repair(0, w, At(sim, 20, 10), queued: true));
        sim.Tick();
        sim.Tick();
        Assert.Single(sim.World.DroppedOrders.ToArray());
        Assert.Empty(Read(sim, 0));
    }

    /// <summary>
    /// A real refusal read after its unit died (the slot freed, as a death later in the same tick leaves it) is not alive; a new
    /// unit spawned into the freed slot (a higher generation) does not make the old entry alive.
    /// </summary>
    [Fact]
    public void AUnitThatDied_OrWhoseSlotWasReused_IsNotAlive()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20));
        EntityHandle w = Unit(sim, At(sim, 3, 3));
        SetTotals(sim, 0, 0, 0);
        sim.Enqueue(Command.Build(0, w, House, At(sim, 20, 10), queued: true));
        sim.Tick();
        sim.Tick();
        DroppedOrder[] list = sim.World.DroppedOrders.ToArray();
        Assert.Single(list);
        UnitStore u = sim.World.Units;
        var into = new OrderNotice[4];
        Assert.Equal(1, OrderNotices.Collect(list, 0, u.Alive, u.Generation, into));
        Assert.True(into[0].Alive);
        u.Free(w);
        Assert.Equal(1, OrderNotices.Collect(list, 0, u.Alive, u.Generation, into));
        Assert.False(into[0].Alive);
        Assert.Equal(w, into[0].Unit);
        EntityHandle next = Unit(sim, At(sim, 5, 5));
        _out.WriteLine($"dead {w}, next spawn {next}");
        Assert.Equal(1, OrderNotices.Collect(list, 0, u.Alive, u.Generation, into));
        Assert.False(into[0].Alive);

        // The same two cases on forged arrays, so the slot reuse is certain: slot 2 dead; slot 3 alive at generation 5, not 4.
        var forged = new[]
        {
            new DroppedOrder(new EntityHandle(2, 1), 0, CommandKind.Build, PlacementError.CannotAfford, false),
            new DroppedOrder(new EntityHandle(3, 4), 0, CommandKind.Build, PlacementError.Blocked, false),
            new DroppedOrder(new EntityHandle(4, 1), 0, CommandKind.Move, PlacementError.None, true),
            new DroppedOrder(new EntityHandle(99, 1), 0, CommandKind.Move, PlacementError.None, true), // past the arrays: not alive, no throw
        };
        bool[] alive = { false, false, false, true, true };
        int[] gen = { 0, 0, 1, 5, 1 };
        Assert.Equal(4, OrderNotices.Collect(forged, 0, alive, gen, into));
        Assert.Equal(new[] { false, false, true, false }, into.Select(n => n.Alive).ToArray());
    }

    /// <summary>The output span is filled as far as it goes and no further.</summary>
    [Fact]
    public void ASmallSpan_TakesTheFirstNotices()
    {
        var forged = new DroppedOrder[10];
        for (int k = 0; k < forged.Length; k++) forged[k] = new DroppedOrder(new EntityHandle(k, 1), 0, CommandKind.Build, PlacementError.CannotAfford, false);
        var into = new OrderNotice[3];
        Assert.Equal(3, OrderNotices.Collect(forged, 0, new bool[10], new int[10], into));
        Assert.Equal(2, into[2].Unit.Index);
        Assert.Equal(0, OrderNotices.Collect(forged, 0, new bool[10], new int[10], Span<OrderNotice>.Empty));
    }

    /// <summary>Criterion 1's allocation row: 100 calls on a full list (4,096 units + 64 commands, every entry a notice of the asked player) allocate 0 bytes.</summary>
    [Fact]
    public void HundredCallsOnAFullList_AllocateNothing()
    {
        const int units = 4096, entries = units + 64;
        var list = new DroppedOrder[entries];
        var alive = new bool[units];
        var gen = new int[units];
        for (int k = 0; k < entries; k++)
            list[k] = new DroppedOrder(new EntityHandle(k % units, 1), 0, CommandKind.Build, k % 2 == 0 ? PlacementError.CannotAfford : PlacementError.Blocked, k % 3 == 0);
        for (int k = 0; k < units; k++) { alive[k] = k % 5 != 0; gen[k] = 1; }
        var into = new OrderNotice[entries];
        int total = 0;
        void Block()
        {
            for (int c = 0; c < 100; c++) total += OrderNotices.Collect(list, 0, alive, gen, into);
        }
        Block();
        AllocationProbe.AssertZero(Block, _out, setup: () => total = 0);
        Assert.Equal(100 * entries, total);
    }

    /// <summary>Reading every tick (the view's read) leaves the state hash equal to a bare twin's, with refusals in the run.</summary>
    [Fact]
    public void ReadingEveryTick_LeavesTheStateHashAlone()
    {
        ulong[] Run(bool read)
        {
            Simulation sim = BuildMaps.NewSim(Flat(30, 20));
            EntityHandle w = Unit(sim, At(sim, 3, 3));
            SetTotals(sim, 0, 0, 0);
            var into = new OrderNotice[8];
            int seen = 0;
            var hashes = new ulong[120];
            for (int t = 0; t < hashes.Length; t++)
            {
                if (t % 10 == 0) sim.Enqueue(Command.Build(0, w, House, At(sim, 20, 10), queued: true));
                sim.Tick();
                if (read) seen += OrderNotices.Collect(sim.World.DroppedOrders, 0, sim.World.Units.Alive, sim.World.Units.Generation, into);
                hashes[t] = sim.StateHash();
            }
            if (read) Assert.True(seen >= 10, $"only {seen} notices read");
            return hashes;
        }
        Assert.Equal(Run(false), Run(true));
    }

    // ---- NoticeLines ----

    /// <summary>One line a unit: a second notice restarts it with the new text; the line lasts LifetimeSeconds and fades over the last FadeSeconds.</summary>
    [Fact]
    public void ALine_RestartsForTheSameUnit_AndExpiresOnTime()
    {
        var lines = new NoticeLines(4);
        var a = new EntityHandle(7, 1);
        int slot = lines.Show(a, PlacementError.CannotAfford, false);
        Assert.Equal(1, lines.Count);
        Assert.Equal(1f, lines.Opacity(slot));
        Assert.Equal(0, lines.Advance(1.5f));
        int again = lines.Show(a, PlacementError.Blocked, false);
        Assert.Equal(slot, again);
        Assert.Equal(1, lines.Count);
        Assert.Equal(1, lines.Restarted);
        Assert.Equal(PlacementError.Blocked, lines.Placement[slot]);
        Assert.Equal(0f, lines.Age[slot]);
        // A new handle in the same slot (the old unit died) takes the line over.
        int reused = lines.Show(new EntityHandle(7, 2), PlacementError.None, true);
        Assert.Equal(slot, reused);
        Assert.Equal(new EntityHandle(7, 2), lines.Unit[slot]);
        Assert.True(lines.QueueFull[slot]);
        Assert.Equal(0, lines.Advance(NoticeLines.LifetimeSeconds - NoticeLines.FadeSeconds / 2f));
        Assert.InRange(lines.Opacity(slot), 0.49f, 0.51f);
        Assert.Equal(1, lines.Advance(NoticeLines.FadeSeconds));
        Assert.Equal(0, lines.Count);
        Assert.Equal(0f, lines.Opacity(slot));
        Assert.Equal(0, lines.Advance(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NoticeLines(0));
    }

    /// <summary>Twelve units at once take twelve lines; past capacity the oldest line is replaced; Clear empties the pool.</summary>
    [Fact]
    public void TwelveUnits_TwelveLines_ThenTheOldestIsReplaced()
    {
        var lines = new NoticeLines(12);
        for (int k = 0; k < 12; k++)
        {
            lines.Show(new EntityHandle(k, 1), PlacementError.CannotAfford, false);
            lines.Advance(0.01f);
        }
        Assert.Equal(12, lines.Count);
        int slot = lines.Show(new EntityHandle(40, 1), PlacementError.Blocked, false);
        Assert.Equal(1, lines.Replaced);
        Assert.Equal(0, slot); // unit 0's line was the oldest
        Assert.Equal(12, lines.Count);
        lines.Clear();
        Assert.Equal(0, lines.Count);
        Assert.Equal(0f, lines.Opacity(slot));
    }

    /// <summary>Showing, ageing and reading lines allocates nothing.</summary>
    [Fact]
    public void Lines_AllocateNothing()
    {
        var lines = new NoticeLines();
        float sink = 0f;
        void Block()
        {
            for (int f = 0; f < 1000; f++)
            {
                if (f % 7 == 0) lines.Show(new EntityHandle(f % 90, 1), PlacementError.CannotAfford, f % 2 == 0);
                lines.Advance(1f / 60f);
                for (int i = 0; i < lines.Capacity; i++) if (lines.Active[i]) sink += lines.Opacity(i);
            }
        }
        Block();
        AllocationProbe.AssertZero(Block, _out);
        Assert.True(sink > 0f);
    }
}
