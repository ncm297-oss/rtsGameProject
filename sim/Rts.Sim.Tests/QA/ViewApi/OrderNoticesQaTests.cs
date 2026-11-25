using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Orders;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;
using static Rts.Sim.Tests.BuildMaps;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>
/// QA M6-V27 (session 2026-11-25-0924): <see cref="OrderNotices"/> and <see cref="NoticeLines"/> against the sim's truth. A
/// seeded fuzz of three players' workers with Shift-queued buildings, gold drained and sites blocked at random, queues
/// overfilled, enemy soldiers killing workers and new units taking freed slots, checked every tick by an oracle of QA's own
/// over <see cref="World.DroppedOrders"/>: every own notice-kind entry gives exactly one line over its unit with the last
/// error that unit got that tick, a foreign or non-notice entry none, a dead or reused slot none; one "sound" a tick at most;
/// and a twin that never reads has the same state hash at every tick. Plus a real (not forged) same-tick death and slot reuse.
/// </summary>
[Collection(SerialCollection.Name)]
public class OrderNoticesQaTests
{
    private readonly ITestOutputHelper _out;

    public OrderNoticesQaTests(ITestOutputHelper output) => _out = output;

    private const int Ticks = 1500;
    private const int Players = 3;

    // A tiny deterministic RNG of the test's own (not the sim's: reading must not touch the sim's RNG).
    private struct Rng
    {
        private ulong _s;
        public Rng(ulong seed) => _s = seed * 0x9E3779B97F4A7C15UL + 1;
        public int Next(int n)
        {
            _s ^= _s << 13;
            _s ^= _s >> 7;
            _s ^= _s << 17;
            return (int)(_s % (ulong)n);
        }
    }

    private sealed class Tally
    {
        public long Entries, OwnNotices, Shown, Dead, Foreign, OtherKinds, QueueFull, Sounds, Restarts;
        public readonly long[] ByError = new long[16];
    }

    // One seeded scene; read == false runs the identical command stream without any reads. Returns the hash at every tick.
    private ulong[] Run(int seed, bool read, Tally tally)
    {
        Simulation sim = BuildMaps.NewSim(Flat(44, 32), units: 40, players: Players, combat: true);
        var rng = new Rng((ulong)seed);
        World w = sim.World;
        // Each player keeps a finished town hall (so it is never out), in a row across the middle; the halls and a House
        // are the blockers queued sites hit. Workers and a soldier type of each player's own faction.
        var worker = new int[Players];
        for (int p = 0; p < Players; p++)
        {
            int hall = Rts.Sim.ViewApi.StartBase.BuildingOfSlot(w.Data, w.FactionOf(p), Rts.Sim.Data.BuildingSlot.TownHall);
            Assert.True(w.Buildings.Spawn(p, hall, Cell(sim, 6 + 14 * p, 14), out _));
            worker[p] = Rts.Sim.ViewApi.StartBase.UnitOfSlot(w.Data, w.FactionOf(p), Rts.Sim.Data.UnitSlot.Worker);
        }
        Assert.True(w.Buildings.Spawn(0, House, Cell(sim, 20, 24), out _));
        int soldier = Rts.Sim.ViewApi.StartBase.UnitOfSlot(w.Data, w.FactionOf(1), Rts.Sim.Data.UnitSlot.Line);
        for (int p = 0; p < Players; p++)
            for (int k = 0; k < 5; k++) sim.Enqueue(Command.SpawnUnit(p, worker[p], At(sim, 4 + 3 * k + 12 * p, 4 + p * 2)));
        for (int k = 0; k < 3; k++) sim.Enqueue(Command.SpawnUnit(1, soldier, At(sim, 30 + k, 28)));
        sim.Tick();
        sim.Tick();
        int p0Alive = 0;
        for (int i = 0; i < w.Units.Capacity; i++) if (w.Units.Alive[i] && w.Units.Owner[i] == 0) p0Alive++;
        Assert.True(p0Alive >= 5, $"only {p0Alive} units of player 0 spawned");

        var lines = new NoticeLines();
        var into = new OrderNotice[256];
        var hashes = new ulong[Ticks];
        var lastOfUnit = new Dictionary<int, (EntityHandle H, PlacementError P, bool Q)>(); // unit index -> its last live notice this tick
        UnitStore u = w.Units;
        for (int t = 0; t < Ticks; t++)
        {
            // Gold drained or refilled at random (a test-side poke on both twins alike).
            if (rng.Next(25) == 0)
                for (int p = 0; p < Players; p++) SetTotals(sim, p, rng.Next(3) == 0 ? 0 : rng.Next(600), rng.Next(3) == 0 ? 0 : rng.Next(600));
            int cmds = rng.Next(6);
            for (int c = 0; c < cmds; c++)
            {
                int slot = rng.Next(u.Capacity);
                int kind = rng.Next(12);
                if (!u.Alive[slot])
                {
                    if (kind < 2) { int sp = rng.Next(Players); sim.Enqueue(Command.SpawnUnit(sp, worker[sp], At(sim, 2 + rng.Next(40), 2 + rng.Next(28)))); }
                    continue;
                }
                var h = new EntityHandle(slot, u.Generation[slot]);
                int owner = u.Owner[slot];
                Vector2 pt = At(sim, 1 + rng.Next(42), 1 + rng.Next(30));
                switch (kind)
                {
                    case 0: case 1: case 2:
                        sim.Enqueue(Command.Build(owner, h, rng.Next(2) == 0 ? House : Keep, rng.Next(3) == 0 ? At(sim, 6 + 14 * rng.Next(Players), 14) : pt, queued: true));
                        break;
                    case 3:
                        sim.Enqueue(Command.Build(owner, h, House, At(sim, 20, 24), queued: true)); // Blocked (Malazan House: WrongFaction for another faction)
                        break;
                    case 4: // overfill: a burst of queued Moves, the 9th+ refused (queue full)
                        for (int k = 0; k < 10; k++) sim.Enqueue(Command.Move(owner, h, At(sim, 1 + rng.Next(42), 1 + rng.Next(30)), queued: true));
                        break;
                    case 5:
                        sim.Enqueue(Command.Gather(owner, h, pt, queued: true)); // nothing there: dropped, but not a notice kind
                        break;
                    case 6:
                        sim.Enqueue(Command.Stop(owner, h));
                        break;
                    case 7:
                        sim.Enqueue(Command.Move(owner, h, pt));
                        break;
                    case 8: // soldiers hunt someone; low hp makes same-tick deaths common
                        for (int i = 0; i < u.Capacity; i++)
                            if (u.Alive[i] && u.Owner[i] == 1 && u.TypeId[i] == soldier)
                                sim.Enqueue(Command.AttackMove(1, new EntityHandle(i, u.Generation[i]), u.Position[slot]));
                        if (owner != 1) u.Hp[slot] = Math.Min(u.Hp[slot], 1 + rng.Next(3));
                        break;
                    case 9:
                        sim.Enqueue(Command.Repair(owner, h, pt, queued: true));
                        break;
                    default:
                        sim.Enqueue(Command.AttackMove(owner, h, pt, queued: true));
                        break;
                }
            }
            sim.Tick();
            if (read)
            {
                ReadOnlySpan<DroppedOrder> dropped = w.DroppedOrders;
                for (int p = 0; p < Players; p++)
                {
                    // The oracle: QA's own filter over the raw list, in list order.
                    int expect = 0;
                    int n = OrderNotices.Collect(dropped, p, u.Alive, u.Generation, into);
                    foreach (DroppedOrder e in dropped)
                    {
                        if (e.Owner != p) continue;
                        bool notice = e.QueueFull || (e.Kind == CommandKind.Build && e.Placement != PlacementError.None);
                        if (!notice) continue;
                        bool alive = u.Alive[e.Unit.Index] && u.Generation[e.Unit.Index] == e.Unit.Generation;
                        Assert.True(expect < n, $"seed {seed} tick {sim.TickNumber} player {p}: Collect gave {n}, oracle wants more");
                        OrderNotice got = into[expect];
                        Assert.Equal(new OrderNotice(e.Unit, alive, e.Kind, e.QueueFull ? PlacementError.None : e.Placement, e.QueueFull), got);
                        expect++;
                    }
                    Assert.Equal(expect, n);
                }

                // The view's read for player 0, done as OrderNoticeView.Collect does it.
                lastOfUnit.Clear();
                int count = OrderNotices.Collect(dropped, 0, u.Alive, u.Generation, into);
                int shown = 0;
                for (int k = 0; k < count; k++)
                {
                    if (!into[k].Alive) { tally.Dead++; continue; }
                    long restartsBefore = lines.Restarted;
                    lines.Show(into[k].Unit, into[k].Placement, into[k].QueueFull);
                    tally.Restarts += lines.Restarted - restartsBefore;
                    lastOfUnit[into[k].Unit.Index] = (into[k].Unit, into[k].Placement, into[k].QueueFull);
                    shown++;
                    if (into[k].QueueFull) tally.QueueFull++;
                    else tally.ByError[(int)into[k].Placement]++;
                }
                if (shown > 0) tally.Sounds++;
                tally.Shown += shown;
                foreach (DroppedOrder e in dropped)
                {
                    tally.Entries++;
                    if (e.Owner != 0) tally.Foreign++;
                    else if (!OrderNotices.IsNotice(in e)) tally.OtherKinds++;
                    else tally.OwnNotices++;
                }

                // Every unit refused live this tick has exactly one line, with its last notice's text, and no line is over a
                // foreign unit or a slot index twice.
                foreach ((int index, (EntityHandle H, PlacementError P, bool Q) want) in lastOfUnit)
                {
                    int found = 0;
                    for (int i = 0; i < lines.Capacity; i++)
                    {
                        if (!lines.Active[i] || lines.Unit[i].Index != index) continue;
                        found++;
                        Assert.Equal(want.H, lines.Unit[i]);
                        Assert.Equal(want.P, lines.Placement[i]);
                        Assert.Equal(want.Q, lines.QueueFull[i]);
                        Assert.Equal(0f, lines.Age[i]);
                    }
                    Assert.Equal(1, found);
                }
                var seen = new HashSet<int>();
                for (int i = 0; i < lines.Capacity; i++)
                {
                    if (!lines.Active[i]) continue;
                    Assert.True(seen.Add(lines.Unit[i].Index), $"seed {seed}: two lines over unit slot {lines.Unit[i].Index}");
                    // A line is only ever started for player 0's unit (its owner when the line started: the slot may since hold another).
                    if (OrderNotices.IsLive(lines.Unit[i], u.Alive, u.Generation)) Assert.Equal(0, u.Owner[lines.Unit[i].Index]);
                }
                lines.Advance(1f / 20f); // one tick's worth of frame time at 1x
            }
            hashes[t] = sim.StateHash();
        }
        return hashes;
    }

    /// <summary>
    /// 16 seeds x 1,500 ticks: the accessor equals QA's oracle for every player every tick, the lines follow the view's rule
    /// (one a unit, the last error, restart not stack), and a twin that never reads has the same state hash every tick.
    /// </summary>
    [Fact]
    public void Fuzz16Seeds_NoticesMatchTheOracle_AndReadingLeavesTheHashAlone()
    {
        var total = new Tally();
        for (int seed = 1; seed <= 16; seed++)
        {
            var tally = new Tally();
            ulong[] withReads = Run(seed, read: true, tally);
            ulong[] bare = Run(seed, read: false, new Tally());
            Assert.Equal(bare, withReads);
            _out.WriteLine($"seed {seed}: {tally.Entries} entries (own notices {tally.OwnNotices}, foreign {tally.Foreign}, own other kinds {tally.OtherKinds}); " +
                $"shown {tally.Shown} (queue full {tally.QueueFull}, cannot afford {tally.ByError[(int)PlacementError.CannotAfford]}, blocked {tally.ByError[(int)PlacementError.Blocked]}, " +
                $"unit in the way {tally.ByError[(int)PlacementError.UnitInTheWay]}, seals {tally.ByError[(int)PlacementError.SealsGround]}), dead {tally.Dead}, sounds {tally.Sounds}, restarts {tally.Restarts}");
            total.Entries += tally.Entries; total.OwnNotices += tally.OwnNotices; total.Foreign += tally.Foreign; total.OtherKinds += tally.OtherKinds;
            total.Shown += tally.Shown; total.QueueFull += tally.QueueFull; total.Dead += tally.Dead; total.Sounds += tally.Sounds; total.Restarts += tally.Restarts;
            for (int k = 0; k < total.ByError.Length; k++) total.ByError[k] += tally.ByError[k];
        }
        _out.WriteLine($"total: {total.Entries} entries, {total.OwnNotices} own notices, {total.Shown} shown, {total.Dead} dead when read, {total.Foreign} foreign, {total.OtherKinds} other kinds, {total.Sounds} sounds, {total.Restarts} restarts");
        // The fuzz must have exercised every branch the oracle checks.
        Assert.True(total.QueueFull > 0, "no queue-full notice in 16 seeds");
        Assert.True(total.ByError[(int)PlacementError.CannotAfford] > 0, "no CannotAfford notice");
        Assert.True(total.ByError[(int)PlacementError.Blocked] > 0, "no Blocked notice");
        Assert.True(total.Foreign > 0 && total.OtherKinds > 0, "no foreign or other-kind entry");
        Assert.True(total.Restarts > 0, "no line was restarted");
        Assert.True(total.Dead > 0, "no own notice whose unit died later in its tick (the real BUG-1662 case)");
    }

    /// <summary>
    /// A real same-tick death (not forged): a 1-hp worker beside an enemy soldier is sent a ninth queued order every tick
    /// (refused on arrival, phase 1) until the soldier kills it (later in that tick). The tick it dies the list carries its
    /// queue-full entry and the accessor reports it not alive; a new unit spawned into its slot does not revive the entry, and
    /// a line started earlier for the dead unit is not drawn over the newcomer (IsLive false for the old handle).
    /// </summary>
    [Fact]
    public void ARealSameTickDeath_IsNotAlive_AndTheSlotsNewUnitGetsNoOldLine()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20), units: 8, players: 2, combat: true);
        EntityHandle w = Unit(sim, At(sim, 10, 10));
        EntityHandle foe = Unit(sim, At(sim, 11, 10), player: 1, type: Infantry);
        UnitStore u = sim.World.Units;
        // Fill the queue behind a long walk so every further queued order is refused on arrival.
        sim.Enqueue(Command.Move(0, w, At(sim, 28, 18)));
        for (int k = 0; k < OrderConstants.QueueCapacity; k++) sim.Enqueue(Command.Move(0, w, At(sim, 2 + k, 18), queued: true));
        sim.Tick();
        sim.Tick();
        Assert.Equal(OrderConstants.QueueCapacity, u.QueueCount[w.Index]);
        u.Hp[w.Index] = 1;
        sim.Enqueue(Command.Attack(1, foe, w, isBuilding: false));
        var lines = new NoticeLines();
        var into = new OrderNotice[8];
        int died = -1, lineSlot = -1;
        for (int t = 0; t < 400 && died < 0; t++)
        {
            if (u.IsAlive(w)) sim.Enqueue(Command.Move(0, w, At(sim, 5, 5), queued: true));
            sim.Tick();
            int n = OrderNotices.Collect(sim.World.DroppedOrders, 0, u.Alive, u.Generation, into);
            bool dead = !u.IsAlive(w);
            for (int k = 0; k < n; k++)
            {
                Assert.Equal(w, into[k].Unit);
                Assert.True(into[k].QueueFull);
                Assert.Equal(!dead, into[k].Alive);
                if (into[k].Alive) lineSlot = lines.Show(into[k].Unit, into[k].Placement, into[k].QueueFull);
            }
            if (dead)
            {
                died = sim.TickNumber;
                _out.WriteLine($"worker died at tick {died}; entries that tick {n}, all reported not alive");
                Assert.True(n >= 1, "no entry in the death tick: the same-tick case was not reached");
            }
        }
        Assert.True(died >= 0, "the worker never died");
        Assert.True(lineSlot >= 0);
        // A new unit takes the slot (the store reuses the lowest free slot).
        EntityHandle next = Unit(sim, At(sim, 3, 3));
        _out.WriteLine($"dead {w}, next {next}");
        if (next.Index == w.Index)
        {
            Assert.True(lines.Active[lineSlot] || lines.Count == 0);
            Assert.False(OrderNotices.IsLive(lines.Unit[lineSlot], u.Alive, u.Generation), "an old line is live over the slot's new unit");
        }
    }

    /// <summary>
    /// Two refusals of one unit a few ticks apart restart the line (age back to 0, the newer error) rather than stack a second
    /// line; the line then lives the full lifetime from the second refusal; time while "paused" (no Advance) does not age it.
    /// </summary>
    [Fact]
    public void TwoRefusalsAFewTicksApart_RestartNotStack_AndAPauseHolds()
    {
        var lines = new NoticeLines();
        var a = new EntityHandle(3, 2);
        int s1 = lines.Show(a, PlacementError.CannotAfford, false);
        for (int f = 0; f < 6; f++) lines.Advance(1f / 60f);
        int s2 = lines.Show(a, PlacementError.Blocked, false);
        Assert.Equal(s1, s2);
        Assert.Equal(1, lines.Count);
        Assert.Equal(PlacementError.Blocked, lines.Placement[s2]);
        // Paused: no Advance for any number of frames; then 2.4 s still shows, 2.5 s is gone.
        lines.Advance(NoticeLines.LifetimeSeconds - 0.1f);
        Assert.Equal(1, lines.Count);
        Assert.True(lines.Opacity(s2) > 0f);
        lines.Advance(0.1f);
        Assert.Equal(0, lines.Count);
        // Infinity (a huge hitch) expires everything rather than corrupting.
        lines.Show(a, PlacementError.CannotAfford, false);
        Assert.Equal(1, lines.Advance(float.PositiveInfinity));
        Assert.Equal(0, lines.Count);
    }
}
