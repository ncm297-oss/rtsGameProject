using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA attacks on M1-8's <see cref="FlowFieldCache.PeekCached"/>: a peek of every map cell between
/// every tick, through a 64-goal churn that evicts, and across a <see cref="NavGrid.Version"/> bump,
/// never changes the state hash or the build count, and never returns a stale or wrong field.
/// </summary>
public class PeekCachedQaTests
{
    private static readonly int[] OddCells = { -1, int.MinValue, int.MaxValue, int.MinValue + 1, int.MaxValue - 1 };

    /// <summary>A sim with <paramref name="units"/> spawned in the west start block, ticked until they exist.</summary>
    private static Simulation Spawned(ulong seed, int units)
    {
        var sim = new Simulation(TestSim.Config(seed, 1, units, units));
        float maxRadius = TestSim.Data.Units.Max(u => u.Radius);
        Vector2[] block = StartLayout.Block(sim.World.NavGrid, units, west: true, maxRadius);
        for (int k = 0; k < block.Length; k++)
            sim.Enqueue(Command.SpawnUnit(0, k % TestSim.UnitTypeCount, block[k]));
        sim.Tick();
        sim.Tick();
        return sim;
    }

    /// <summary>Orders the live units round-robin to <paramref name="goals"/> distinct goal cells, picked as an even stride over the passable cells, offset by <paramref name="offset"/>.</summary>
    private static void OrderToManyGoals(Simulation sim, int goals, int offset)
    {
        NavGrid g = sim.World.NavGrid;
        var passable = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
            if (g.IsPassable(c % g.Width, c / g.Width)) passable.Add(c);
        UnitStore u = sim.World.Units;
        int n = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            int goal = passable[(offset + (n++ % goals) * (passable.Count / goals)) % passable.Count];
            sim.Enqueue(Command.Move(0, new EntityHandle(i, u.Generation[i]), g.CellCenter(goal % g.Width, goal / g.Width)));
        }
    }

    /// <summary>Peeks every cell plus the int extremes; asserts each hit is current and for that cell, and agrees with <see cref="FlowFieldCache.Contains"/>. Returns the hit cells.</summary>
    private static HashSet<int> PeekAll(Simulation sim)
    {
        FlowFieldCache cache = sim.World.FlowFields;
        NavGrid g = sim.World.NavGrid;
        var hits = new HashSet<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
        {
            FlowField? f = cache.PeekCached(c);
            Assert.Equal(cache.Contains(c), f != null);
            if (f == null) continue;
            Assert.Equal(c, f.RequestedCell);
            Assert.Equal(g.Version, f.Version);
            hits.Add(c);
        }
        foreach (int c in OddCells) Assert.Null(cache.PeekCached(c));
        Assert.Null(cache.PeekCached(g.Width * g.Height));
        return hits;
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(4UL)]
    public void FullMapPeeks_EveryTick_Through64GoalChurn_EvictionsAndVersionBump_LeaveHashUnchanged(ulong seed)
    {
        const int units = 256;
        Simulation peeked = Spawned(seed, units), twin = Spawned(seed, units);
        Assert.Equal(twin.StateHash(), peeked.StateHash());
        FlowFieldCache cache = peeked.World.FlowFields;
        Assert.True(cache.Capacity < 64, $"capacity {cache.Capacity}: 64 goals must overflow it to force evictions");

        OrderToManyGoals(peeked, 64, 0);
        OrderToManyGoals(twin, 64, 0);
        PeekAll(peeked); // between enqueue and tick too

        HashSet<int> previous = new();
        int evictions = 0, maxHits = 0;
        for (int t = 0; t < 160; t++)
        {
            if (t == 60)
            {
                // Second wave of 64 other goals: more misses and evictions.
                OrderToManyGoals(peeked, 64, 37);
                OrderToManyGoals(twin, 64, 37);
            }
            if (t == 100)
            {
                // A grid change makes every cached field stale; a peek must not return any of them.
                Assert.NotEmpty(PeekAll(peeked));
                peeked.World.NavGrid.BumpVersionForTests();
                twin.World.NavGrid.BumpVersionForTests();
                Assert.Empty(PeekAll(peeked));
                Assert.Equal(twin.StateHash(), peeked.StateHash());
            }
            peeked.Tick();
            twin.Tick();

            ulong before = peeked.StateHash();
            int builds = cache.BuildCount, count = cache.Count;
            HashSet<int> hits = PeekAll(peeked);
            hits.UnionWith(PeekAll(peeked)); // a second full pass sees the same thing
            Assert.Equal(builds, cache.BuildCount);
            Assert.Equal(count, cache.Count);
            Assert.Equal(before, peeked.StateHash());
            Assert.Equal(twin.StateHash(), peeked.StateHash());

            evictions += previous.Count(c => !hits.Contains(c));
            maxHits = Math.Max(maxHits, hits.Count);
            previous = hits;
        }
        Assert.True(evictions > 0, "the churn should evict cached fields");
        Assert.True(maxHits <= cache.Capacity);
        Assert.True(cache.BuildCount > cache.Capacity, $"builds {cache.BuildCount}");
    }

    [Fact]
    public void Peek_AfterVersionBump_ThenRebuild_ReturnsTheFreshField()
    {
        Simulation sim = Spawned(2, 64);
        OrderToManyGoals(sim, 4, 0);
        for (int t = 0; t < 10; t++) sim.Tick();
        HashSet<int> before = PeekAll(sim);
        Assert.NotEmpty(before);
        sim.World.NavGrid.BumpVersionForTests();
        Assert.Empty(PeekAll(sim));
        for (int t = 0; t < 10; t++) sim.Tick();
        HashSet<int> after = PeekAll(sim); // PeekAll asserts every hit carries the new version
        Assert.NotEmpty(after);
        foreach (int c in after)
        {
            FlowField fresh = FlowField.Build(sim.World.NavGrid, c);
            FlowField peeked = sim.World.FlowFields.PeekCached(c)!;
            for (int cell = 0; cell < sim.World.NavGrid.Width * sim.World.NavGrid.Height; cell++)
            {
                Assert.Equal(fresh.DirectionAt(cell), peeked.DirectionAt(cell));
                Assert.Equal(fresh.CostAt(cell), peeked.CostAt(cell));
            }
        }
    }

    [Fact]
    public void Peek_OnAFreshSim_IsNullEverywhere_AndBuildsNothing()
    {
        var sim = new Simulation(TestSim.Config(3, 1, 8, 8));
        ulong h = sim.StateHash();
        Assert.Empty(PeekAll(sim));
        Assert.Equal(0, sim.World.FlowFields.BuildCount);
        Assert.Equal(h, sim.StateHash());
    }
}
