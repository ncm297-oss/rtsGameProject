using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA, M6-S11 (session 2026-11-15-1536): crowd attacks on "an order queued behind a gather loop starts once the trip is done".
/// 200 workers on one mine all queued a Move in one tick, and BUG-1463's shape (12 Teblor Gatherers at a 2 x 2 depot with the
/// mine flush on its face) queued a Move: asserted are the invariants (queue 0-8, no NaN, nobody on blocked ground, deliveries
/// only of full loads, every started worker off its loop with an empty queue, a save mid-wait loads and follows hash for hash);
/// measured and printed, not asserted (rule 66), the waits and the workers still waiting at the end.
/// </summary>
[Trait("Category", "Stress")]
public class GatherQueueQaStressTests(ITestOutputHelper output)
{
    private static int Depot => TestSim.Data.FindBuilding("malazan_depot");

    private static EntityHandle[] SpawnWorkers(Simulation sim, int count, int type, (int X0, int Y0, int X1, int Y1) area)
    {
        World w = sim.World;
        NavGrid g = w.NavGrid;
        var points = new List<Vector2>();
        for (int y = area.Y0; y <= area.Y1 && points.Count < count; y++)
            for (int x = area.X0; x <= area.X1 && points.Count < count; x++)
                if (g.IsPassable(x, y)) points.Add(g.CellCenter(x, y));
        Assert.Equal(count, points.Count);
        UnitStore u = w.Units;
        var before = (bool[])u.Alive.Clone();
        foreach (Vector2 p in points) sim.Enqueue(Command.SpawnUnit(0, type, p));
        Run(sim, 2);
        var hs = new List<EntityHandle>();
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && !before[i]) hs.Add(new EntityHandle(i, u.Generation[i]));
        Assert.Equal(count, hs.Count);
        return hs.ToArray();
    }

    /// <summary>Runs the crowd after every worker got a queued Move to <paramref name="target"/> in one tick; returns the line printed.</summary>
    private string Watch(string name, Simulation sim, EntityHandle[] hs, Vector2 target, int ticks, bool wood = false)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        int carry = w.Data.CarryOf(u.TypeId[hs[0].Index]);
        foreach (EntityHandle h in hs) sim.Enqueue(Command.Move(0, h, target, queued: true));
        while (sim.PendingCommandCount > 0) sim.Tick(); // the Moves apply (phase 1) in the second tick from here
        int queuedAt = sim.TickNumber;
        var startedAt = new int[hs.Length];
        Array.Fill(startedAt, -1);
        Simulation? twin = null;
        int gathered = wood ? w.GatheredWood[0] : w.GatheredGold[0];
        for (int t = 0; t < ticks; t++)
        {
            if (t == 300)
            {
                twin = SaveTestRun.Load(sim.World.Data, SaveTestRun.Save(sim));
                SaveTestRun.AssertSameState(sim, twin, $"{name}: at the save");
            }
            sim.Tick();
            twin?.Tick();
            if (twin != null) Assert.True(sim.StateHash() == twin.StateHash(), $"{name}: the loaded twin differs at tick {sim.TickNumber}");
            int now = wood ? w.GatheredWood[0] : w.GatheredGold[0];
            Assert.True((now - gathered) % carry == 0, $"{name}: a delivery of {now - gathered} at tick {sim.TickNumber} (carry {carry})");
            gathered = now;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                Vector2 p = u.Position[i];
                Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y), $"{name}: unit {i} not finite at tick {sim.TickNumber}");
                Assert.InRange(u.QueueCount[i], 0, OrderConstants.QueueCapacity);
            }
            int blocked = MoveScenario.FirstUnitOnBlockedGround(w);
            Assert.True(blocked < 0, $"{name}: unit {blocked} on blocked ground at tick {sim.TickNumber}");
            for (int k = 0; k < hs.Length; k++)
            {
                int i = hs[k].Index;
                if (startedAt[k] >= 0 || u.QueueCount[i] > 0) continue;
                Assert.False(EconomySystem.OnLoop(u, i), $"{name}: worker {i} on a loop with its queue gone");
                startedAt[k] = sim.TickNumber;
            }
        }
        int done = 0, longest = 0;
        long sum = 0;
        var open = new List<string>();
        for (int k = 0; k < hs.Length; k++)
        {
            int i = hs[k].Index;
            if (startedAt[k] < 0)
            {
                open.Add($"worker {i}: {u.State[i]} at {u.Position[i]}, cargo {u.Cargo[i]}, stuck {u.StuckTicks[i]}");
                continue;
            }
            done++;
            int wait = startedAt[k] - queuedAt;
            sum += wait;
            longest = Math.Max(longest, wait);
        }
        string line = $"{name}: {hs.Length} workers queued a Move in one tick; {done} started (mean wait {(done > 0 ? sum / (double)done / SimConstants.TicksPerSecond : 0):0.0} s, "
            + $"longest {longest / (double)SimConstants.TicksPerSecond:0.0} s); {open.Count} still waiting after {ticks / SimConstants.TicksPerSecond} s";
        output.WriteLine(line);
        foreach (string s in open) output.WriteLine("  WAITING " + s);
        return line;
    }

    /// <summary>200 Laborers on one mine (a Keep 10 m off) all queued a Move in one tick, 1,200 ticks into the loop.</summary>
    [Theory]
    [InlineData(0.5)]
    [InlineData(0.0)]
    public void TwoHundredWorkersOnOneMine_AllQueueAMoveInOneTick(double overlap)
    {
        GameData data = GatherOverlapScenes.DataAt(overlap);
        Simulation sim = GatherOverlapScenes.NewSim(data, 256, width: 96, height: 64);
        Spawn(sim.World, Mine, 40, 30, 1_000_000);
        Building(sim, 28, 29);
        EntityHandle[] hs = SpawnWorkers(sim, 200, GatherOverlapScenes.LaborerType, (4, 4, 24, 60));
        foreach (EntityHandle h in hs) sim.Enqueue(Command.Gather(0, h, At(sim, 40, 30)));
        Run(sim, 1200);
        Watch($"200 on one mine, overlap {overlap}", sim, hs, At(sim, 80, 50), 6000);
    }

    /// <summary>BUG-1463's shape: a 2 x 2 depot, the mine flush on its east face, 12 Teblor Gatherers (the Keep far); a queued Move after 1,500 ticks.</summary>
    [Theory]
    [InlineData(0.5)]
    [InlineData(0.0)]
    public void TeblorGatherersAtATwoByTwoDepot_QueueAMove_ReportTheWait(double overlap)
    {
        Simulation sim = GatherOverlapScenes.NewSim(GatherOverlapScenes.DataAt(overlap), 48, width: 56, height: 44);
        Spawn(sim.World, Mine, 22, 20, 50_000);
        Building(sim, 44, 30);
        Building(sim, 20, 20, type: Depot);
        EntityHandle[] hs = SpawnWorkers(sim, 12, GatherOverlapScenes.GathererType, (4, 8, 12, 36));
        foreach (EntityHandle h in hs) sim.Enqueue(Command.Gather(0, h, At(sim, 22, 20)));
        Run(sim, 1500);
        Watch($"Teblor x12 at a 2x2 depot, overlap {overlap}", sim, hs, At(sim, 6, 40), 6000);
    }
}
