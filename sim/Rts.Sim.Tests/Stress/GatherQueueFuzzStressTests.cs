using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Orders;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// M6-S11 criterion 8: random queued and unqueued orders onto gathering workers (<see cref="GatherQueueScene"/>) over many
/// seeds, with the shipped gather overlap (0.5) and with it off. Asserted after every tick: no queue entry lost or
/// duplicated (a queue only ever loses its head, is cleared by an unqueued order or a terminal Stop / Hold, or gains the
/// queued orders applied that tick at its end), <c>QueueCount</c> in 0-8, no worker Gathering or Returning without a node,
/// and no cargo lost except by a delivery (or the documented drop when a Gather switches the resource kind). Measured and
/// printed, not asserted (rule 66): the longest wait from "queued behind a loop" to "started", and the waits still open
/// at the end (a worker held out of reach cannot deliver, so its queue waits: BUG-1463's shapes, counted, not fixed).
/// </summary>
public class GatherQueueFuzzStressTests
{
    private const int Ticks = 4000;
    private readonly ITestOutputHelper _out;

    public GatherQueueFuzzStressTests(ITestOutputHelper output) => _out = output;

    private readonly record struct Entry(CommandKind Kind, Vector2 Position, int TypeId);

    private static Entry[] QueueOf(UnitStore u, int i)
    {
        var q = new Entry[u.QueueCount[i]];
        for (int k = 0; k < q.Length; k++)
        {
            int at = i * OrderConstants.QueueCapacity + k;
            q[k] = new Entry(u.QueueKind[at], u.QueuePosition[at], u.QueueTypeId[at]);
        }
        return q;
    }

    [Theory]
    [Trait("Category", "Stress")]
    [InlineData(0.5)]
    [InlineData(0.0)]
    public void RandomOrdersOntoGatherers_KeepTheQueueAndCargoInvariants_AndReportTheWaits(double overlap)
    {
        GameData data = overlap == 0.5 ? TestSim.Data : GatherOverlapScenes.DataAt(overlap);
        int longest = 0, waits = 0, open = 0, discards = 0, deliveries = 0, queuedBehind = 0;
        string longestAt = "";
        for (ulong seed = 1; seed <= 12; seed++)
        {
            GatherQueueScene s = GatherQueueScene.Create(seed, data: data);
            Simulation sim = s.Sim;
            World w = sim.World;
            UnitStore u = w.Units;
            int n = u.Capacity;
            var waitFrom = new int[n];
            Array.Fill(waitFrom, -1);
            var pending = new List<Command>();
            for (int t = 0; t < Ticks; t++)
            {
                int before = sim.PendingCommandCount;
                s.Issue(t, sim);
                for (int k = before; k < sim.PendingCommandCount; k++) pending.Add(sim.PendingAt(k));
                var queues = new Entry[n][];
                var cargo = new int[n];
                var kind = new ResourceKind[n];
                var alive = new bool[n];
                var loopEmpty = new bool[n];
                for (int i = 0; i < n; i++)
                {
                    alive[i] = u.Alive[i];
                    if (!alive[i]) continue;
                    queues[i] = QueueOf(u, i);
                    cargo[i] = u.Cargo[i];
                    kind[i] = u.CargoKind[i];
                    loopEmpty[i] = EconomySystem.OnLoop(u, i) && u.QueueCount[i] == 0;
                }
                int[] gold = { w.GatheredGold[0], w.GatheredGold[1] }, wood = { w.GatheredWood[0], w.GatheredWood[1] };
                sim.Tick();
                bool applied = sim.PendingCommandCount == 0;
                // Cargo that left a worker, per player and old kind: "sure" with the kind unchanged (only a delivery does
                // that), "switched" when the kind changed in the tick (a Gather of the other kind drops the load, or the
                // worker delivered and then started a queued Gather of the other kind in the same tick).
                long[] sureGold = new long[2], sureWood = new long[2], switchedGold = new long[2], switchedWood = new long[2];
                for (int i = 0; i < n; i++)
                {
                    if (!alive[i] || !u.Alive[i]) continue;
                    string at = $"seed {seed} overlap {overlap} tick {sim.TickNumber} slot {i}";
                    int count = u.QueueCount[i];
                    Assert.True(count >= 0 && count <= OrderConstants.QueueCapacity, $"{at}: QueueCount {count}");
                    if (u.State[i] is UnitState.Gathering or UnitState.Returning)
                        Assert.True(EconomySystem.OnLoop(u, i), $"{at}: {u.State[i]} with no gather node");
                    // The queue's transition.
                    int queuedNow = 0;
                    bool unqueued = false;
                    if (applied)
                        foreach (Command c in pending)
                            if (c.Unit.Index == i && c.Unit.Generation == u.Generation[i])
                            {
                                if (c.IsQueued) queuedNow++;
                                else unqueued = true;
                            }
                    Entry[] old = queues[i], now = QueueOf(u, i);
                    bool ok = false;
                    for (int d = 0; d <= 1 && d <= old.Length && !ok; d++)
                    {
                        int kept = old.Length - d, added = now.Length - kept;
                        if (added < 0 || added > queuedNow) continue;
                        bool same = true;
                        for (int k = 0; k < kept && same; k++) same = now[k] == old[k + d];
                        ok = same;
                    }
                    // A clear: an unqueued order, or a terminal Stop / Hold at the head (queued entries applied this tick
                    // went before the pop, so they go with it).
                    if (!ok && now.Length <= queuedNow && unqueued) ok = true;
                    if (!ok && now.Length == 0)
                    {
                        CommandKind head = old.Length > 0 ? old[0].Kind : CommandKind.Noop;
                        if (old.Length == 0 && queuedNow > 0)
                            foreach (Command c in pending)
                                if (c.Unit.Index == i && c.IsQueued) { head = c.Kind; break; }
                        ok = head is CommandKind.Stop or CommandKind.HoldPosition;
                    }
                    Assert.True(ok, $"{at}: queue went from [{string.Join(",", old.Select(e => e.Kind))}] to [{string.Join(",", now.Select(e => e.Kind))}] with {queuedNow} queued and unqueued {unqueued} applied");
                    // Cargo: lost only by a delivery (summed per player and kind below) or a kind switch.
                    if (u.CargoKind[i] != kind[i] && cargo[i] > 0)
                    {
                        if (kind[i] == ResourceKind.Gold) switchedGold[u.Owner[i]] += cargo[i];
                        else switchedWood[u.Owner[i]] += cargo[i];
                    }
                    else if (u.Cargo[i] < cargo[i])
                    {
                        if (kind[i] == ResourceKind.Gold) sureGold[u.Owner[i]] += cargo[i] - u.Cargo[i];
                        else sureWood[u.Owner[i]] += cargo[i] - u.Cargo[i];
                    }
                    // The wait: from the tick a queued order lands on a worker on a loop with an empty queue, to the tick it
                    // is off its loop or its queue has lost its head.
                    if (loopEmpty[i] && count > 0 && waitFrom[i] < 0)
                    {
                        waitFrom[i] = sim.TickNumber;
                        queuedBehind++;
                    }
                    else if (waitFrom[i] >= 0 && (count == 0 || !EconomySystem.OnLoop(u, i) || count < old.Length))
                    {
                        int wait = sim.TickNumber - waitFrom[i];
                        waits++;
                        if (wait > longest) { longest = wait; longestAt = $"seed {seed} slot {i} from tick {waitFrom[i]}"; }
                        waitFrom[i] = -1;
                    }
                }
                for (int p = 0; p < 2; p++)
                {
                    long dg = w.GatheredGold[p] - gold[p], dw = w.GatheredWood[p] - wood[p];
                    Assert.True(sureGold[p] <= dg && dg <= sureGold[p] + switchedGold[p], $"seed {seed} tick {sim.TickNumber} p{p}: gold cargo left {sureGold[p]} (+{switchedGold[p]} switched), delivered {dg}");
                    Assert.True(sureWood[p] <= dw && dw <= sureWood[p] + switchedWood[p], $"seed {seed} tick {sim.TickNumber} p{p}: wood cargo left {sureWood[p]} (+{switchedWood[p]} switched), delivered {dw}");
                    discards += (int)(switchedGold[p] - (dg - sureGold[p]) + switchedWood[p] - (dw - sureWood[p]));
                    if (dg + dw > 0) deliveries++;
                }
                for (int i = 0; i < n; i++) if (!u.Alive[i]) waitFrom[i] = -1;
                if (applied) pending.Clear();
            }
            for (int i = 0; i < n; i++)
                if (waitFrom[i] >= 0)
                {
                    open++;
                    _out.WriteLine($"seed {seed}: slot {i} still waiting at the end, {sim.TickNumber - waitFrom[i]} ticks (state {u.State[i]}, cargo {u.Cargo[i]}, queue {u.QueueCount[i]})");
                }
        }
        _out.WriteLine($"overlap {overlap}: {queuedBehind} queues landed behind a loop, {waits} started; longest wait {longest} ticks ({longest / (double)SimConstants.TicksPerSecond:0.0} s, {longestAt}); "
            + $"{open} still waiting at the end; {deliveries} delivery ticks; {discards} resources dropped by a kind switch");
        Assert.True(waits > 100, $"only {waits} waits measured: the row proves little");
    }
}
