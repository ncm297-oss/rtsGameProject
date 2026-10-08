using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Vision;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-3a criterion 7: what the fog costs (Debug, like every tick budget in docs/03). The sim runs its ticks; on every
/// update tick the test times one more <see cref="FogStore.Update"/> on the same state (it is idempotent but for the
/// version), and divides the total by the ticks run: the amortized cost a tick pays. Those updates allocate nothing.
/// </summary>
[Collection(SerialCollection.Name)]
public class FogPerfTests
{
    private const int MeasuredTicks = 300;
    private readonly ITestOutputHelper _out;

    public FogPerfTests(ITestOutputHelper output) => _out = output;

    /// <summary>The fog's budget with 2,500 units of one player (Producer, M4-3a): a quarter of a millisecond a tick, amortized.</summary>
    private const double OnePlayerBudgetMs = 0.25;

    /// <summary>The fog's budget with 1,000 v 1,000 on a 3-level map (Producer, M4-3a).</summary>
    private const double TwoArmiesBudgetMs = 0.4;

    /// <summary>Places <paramref name="count"/> units of <paramref name="player"/> on random passable cells and sends each walking to another.</summary>
    private static void Spread(Simulation sim, int player, int count, ulong seed)
    {
        NavGrid g = sim.World.NavGrid;
        var cells = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
            if (g.IsPassable(c % g.Width, c / g.Width)) cells.Add(c);
        int[] types = { Crossbowman, HeavyInfantry, Raider, TestSim.Data.FindUnit("malazan_cadre_mage") }; // sights 18, 14, 14, 16
        var rng = new SimRng(seed, (ulong)(11 + player));
        for (int k = 0; k < count; k++)
        {
            int c = cells[rng.NextInt(0, cells.Count)];
            EntityHandle h = Place(sim, player, types[k % types.Length], g.CellCenter(c % g.Width, c / g.Width) + new Vector2(rng.NextFloat() - 0.5f, rng.NextFloat() - 0.5f));
            int to = cells[rng.NextInt(0, cells.Count)];
            sim.Enqueue(Command.Move(player, h, g.CellCenter(to % g.Width, to / g.Width)));
        }
    }

    /// <summary>Runs <see cref="MeasuredTicks"/> ticks after a warm-up; returns the timed fog updates' total over the ticks run (ms a tick).</summary>
    private double FogMsPerTick(Simulation sim, out double worstUpdateMs, out int updates)
    {
        World w = sim.World;
        for (int t = 0; t < 8; t++) sim.Tick();
        w.Fog.Update(); // JIT
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long freq = Stopwatch.Frequency, total = 0, worst = 0;
        updates = 0;
        for (int t = 0; t < MeasuredTicks; t++)
        {
            sim.Tick();
            if (!VisionSystem.IsUpdateTick(sim.TickNumber - 1)) continue;
            long start = Stopwatch.GetTimestamp();
            w.Fog.Update();
            long took = Stopwatch.GetTimestamp() - start;
            total += took;
            worst = Math.Max(worst, took);
            updates++;
        }
        worstUpdateMs = worst * 1000.0 / freq;
        AllocationProbe.AssertZero(() =>
        {
            for (int k = 0; k < 4; k++) w.Fog.Update();
        }, _out);
        return total * 1000.0 / freq / MeasuredTicks;
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void Fog2500OnePlayer_SpreadOverA128Map_AtMostAQuarterMsATickAmortized_AndAllocatesNothing()
    {
        var sim = new Simulation(TestSim.ConfigNoCombat(Seed: 1, PlayerCount: 1, UnitCapacity: 2500, CommandCapacity: 2600), LocalMovementTests.Flat(128));
        Spread(sim, 0, 2500, 1);
        double ms = FogMsPerTick(sim, out double worst, out int updates);
        int seen = FogMaps.Count(sim.World, 0, VisionConstants.Visible);
        _out.WriteLine($"2,500 units of one player on a flat 128 map: fog {ms:F4} ms a tick amortized over {MeasuredTicks} ticks ({updates} updates, worst update {worst:F3} ms); {seen} of {128 * 128} cells visible (target <= {OnePlayerBudgetMs} ms)");
        Assert.True(ms <= OnePlayerBudgetMs, $"fog {ms:F4} ms a tick (target <= {OnePlayerBudgetMs} ms)");
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void Fog1000v1000_OnAThreeLevelMap_AtMostPoint4MsATickAmortized_AndAllocatesNothing()
    {
        var sim = new Simulation(TestSim.ConfigNoCombat(Seed: 3, PlayerCount: 2, UnitCapacity: 2000, CommandCapacity: 2100));
        int levels = 0;
        foreach (byte l in sim.World.Heightmap.Levels) levels |= 1 << l;
        Assert.Equal(7, levels); // levels 0, 1 and 2
        Spread(sim, 0, 1000, 3);
        Spread(sim, 1, 1000, 3);
        double ms = FogMsPerTick(sim, out double worst, out int updates);
        _out.WriteLine($"1,000 v 1,000 on a generated 3-level 128 map: fog {ms:F4} ms a tick amortized over {MeasuredTicks} ticks ({updates} updates, worst update {worst:F3} ms) (target <= {TwoArmiesBudgetMs} ms)");
        Assert.True(ms <= TwoArmiesBudgetMs, $"fog {ms:F4} ms a tick (target <= {TwoArmiesBudgetMs} ms)");
    }

    /// <summary>
    /// Fog's share of <c>CrowdPerfTests.TightBlob2500_OnePlayer</c> (the same scene: seed 99, 2,500 units within 12 path
    /// cells of the centre, all moved to it): reported, and held to the one-player budget. Most of the blob's units share
    /// cells with others, so the per-cell stamp skip leaves a few hundred stamps an update.
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void FogInTheTightBlob2500_Report()
    {
        Simulation sim = MoveScenario.Spawn(seed: 99, units: 2500, maxCost: 12f, out int goalCell, players: 1, combat: true);
        MoveScenario.MoveAll(sim, MoveScenario.Center(sim.World.NavGrid, goalCell));
        sim.Tick();
        double ms = FogMsPerTick(sim, out double worst, out int updates);
        _out.WriteLine($"tight blob of 2,500, one player: fog {ms:F4} ms a tick amortized over {MeasuredTicks} ticks ({updates} updates, worst update {worst:F3} ms)");
        Assert.True(ms <= OnePlayerBudgetMs, $"fog {ms:F4} ms a tick (target <= {OnePlayerBudgetMs} ms)");
    }
}
