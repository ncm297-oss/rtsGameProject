using System.Diagnostics;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>M3-1 criterion 8: resource nodes cost (almost) nothing per tick, and hashing a full store is cheap.</summary>
[Collection(SerialCollection.Name)]
public class ResourcePerfTests
{
    private const double HashBudgetMs = 0.05;
    private const double TickOverheadBudgetMs = 0.05;

    private readonly ITestOutputHelper _out;

    public ResourcePerfTests(ITestOutputHelper output) => _out = output;

    /// <summary>Spawns trees on cells that take one, in index order, until <paramref name="count"/> stand.</summary>
    private static void FillTrees(Simulation sim, int count)
    {
        NavGrid g = sim.World.NavGrid;
        ResourceStore r = sim.World.Resources;
        for (int c = 0; c < g.Width * g.Height && r.Count < count; c++)
            r.Spawn(ResourceMaps.Tree, c, ResourceMaps.TreeWood, out _);
        Assert.Equal(count, r.Count);
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void HashingAFullStoreOf4096Nodes_TakesUnder50Microseconds()
    {
        var sim = new Simulation(TestSim.Config(Seed: 1, PlayerCount: 1, UnitCapacity: 8, CommandCapacity: 8));
        ResourceStore r = sim.World.Resources;
        Assert.Equal(4096, r.Capacity);
        FillTrees(sim, r.Capacity); // every slot live: the worst case
        const int Rounds = 2000;
        ulong sink = 0;
        for (int i = 0; i < 50; i++)
        {
            var w = new StateHasher();
            r.AddToHash(ref w);
            sink ^= w.Value;
        }
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < Rounds; i++)
        {
            var h = new StateHasher();
            r.AddToHash(ref h);
            sink ^= h.Value;
        }
        double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency / Rounds;
        _out.WriteLine($"hashing {r.Capacity} live resource slots: {ms * 1000:F1} us per hash (budget {HashBudgetMs * 1000:F0} us); sink {sink:X}");
        Assert.True(ms < HashBudgetMs, $"hashing the resource store took {ms:F4} ms");
    }

    /// <summary>Average tick (50 idle units) with 2,000 standing trees against the same map without them, interleaved to share machine noise.</summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void TickWith2000StandingTrees_CostsAtMost50MicrosecondsMore()
    {
        Simulation bare = Idle(trees: 0), forest = Idle(trees: 2000);
        const int Rounds = 20, TicksPerRound = 100;
        double bareMs = 0, forestMs = 0;
        long freq = Stopwatch.Frequency;
        for (int round = 0; round < Rounds; round++)
        {
            long t0 = Stopwatch.GetTimestamp();
            for (int t = 0; t < TicksPerRound; t++) bare.Tick();
            long t1 = Stopwatch.GetTimestamp();
            for (int t = 0; t < TicksPerRound; t++) forest.Tick();
            long t2 = Stopwatch.GetTimestamp();
            bareMs += (t1 - t0) * 1000.0 / freq;
            forestMs += (t2 - t1) * 1000.0 / freq;
        }
        bareMs /= Rounds * TicksPerRound;
        forestMs /= Rounds * TicksPerRound;
        _out.WriteLine($"tick with 50 idle units: no trees {bareMs * 1000:F1} us, 2,000 trees {forestMs * 1000:F1} us (budget +{TickOverheadBudgetMs * 1000:F0} us)");
        Assert.Equal(2000, forest.World.Resources.Count);
        Assert.True(forestMs - bareMs <= TickOverheadBudgetMs, $"2,000 trees add {(forestMs - bareMs) * 1000:F1} us per tick");
    }

    private static Simulation Idle(int trees)
    {
        Simulation sim = MoveScenario.Spawn(seed: 5, units: 50, maxCost: 12f, out _, players: 1);
        if (trees > 0) FillTrees(sim, trees);
        for (int t = 0; t < 20; t++) sim.Tick(); // warm-up
        return sim;
    }
}
