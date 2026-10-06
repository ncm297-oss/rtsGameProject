using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using static Rts.Sim.Tests.FlowFieldOracle;

namespace Rts.Sim.Tests;

/// <summary>FlowFieldCache (M1-4b): hits share one instance, LRU eviction at capacity, grid-version staleness.</summary>
public class FlowFieldCacheTests
{
    private static readonly NavGrid Grid = Generated(3);
    private static readonly List<int> Open = PassableCells(Grid);

    private static int Target(int k) => Open[k * 97 % Open.Count];

    [Fact]
    public void DefaultCapacity_Is32()
    {
        Assert.Equal(32, FlowFieldCache.DefaultCapacity);
        Assert.Equal(32, new FlowFieldCache(Grid).Capacity);
    }

    [Fact]
    public void Hit_ReturnsSameInstance_WithoutRebuilding()
    {
        var cache = new FlowFieldCache(Grid);
        FlowField a = cache.Get(Target(0));
        Assert.Equal(1, cache.BuildCount);
        FlowField b = cache.Get(Target(0));
        Assert.Same(a, b);
        Assert.Equal(1, cache.BuildCount);
        Assert.Equal(Target(0), a.RequestedCell);
    }

    [Fact]
    public void ThirtyThirdTarget_EvictsLeastRecentlyUsed()
    {
        var cache = new FlowFieldCache(Grid);
        for (int k = 0; k < 32; k++) cache.Get(Target(k));
        Assert.Equal(32, cache.Count);
        Assert.Equal(32, cache.BuildCount);

        FlowField newest = cache.Get(Target(32));
        Assert.Equal(33, cache.BuildCount);
        Assert.Equal(32, cache.Count);
        Assert.False(cache.Contains(Target(0)));      // the oldest went
        for (int k = 1; k <= 32; k++) Assert.True(cache.Contains(Target(k)));
        Assert.Equal(Target(32), newest.RequestedCell);
        AssertSameAsFresh(newest);                    // the reused arrays hold no trace of the old field
    }

    [Fact]
    public void Touching_MakesFieldMostRecent()
    {
        var cache = new FlowFieldCache(Grid);
        for (int k = 0; k < 32; k++) cache.Get(Target(k));
        FlowField first = cache.Get(Target(0)); // touch: now target 1 is the oldest
        Assert.Equal(32, cache.BuildCount);

        cache.Get(Target(32));
        Assert.True(cache.Contains(Target(0)));
        Assert.False(cache.Contains(Target(1)));
        Assert.Same(first, cache.Get(Target(0)));
        Assert.Equal(33, cache.BuildCount);
    }

    [Fact]
    public void Contains_DoesNotCountAsAUse()
    {
        var cache = new FlowFieldCache(Grid, capacity: 2);
        cache.Get(Target(0));
        cache.Get(Target(1));
        Assert.True(cache.Contains(Target(0)));
        cache.Get(Target(2)); // target 0 is still the least recently used
        Assert.False(cache.Contains(Target(0)));
        Assert.True(cache.Contains(Target(1)));
    }

    [Fact]
    public void TryGetCached_MissBuildsNothing_HitCountsAsAUse()
    {
        var cache = new FlowFieldCache(Grid, capacity: 2);
        Assert.Null(cache.TryGetCached(Target(0)));
        Assert.Equal(0, cache.BuildCount);
        FlowField a = cache.Get(Target(0));
        cache.Get(Target(1));
        Assert.Same(a, cache.TryGetCached(Target(0))); // touch: target 1 is now the oldest
        cache.Get(Target(2));
        Assert.True(cache.Contains(Target(0)));
        Assert.False(cache.Contains(Target(1)));
        Assert.Equal(3, cache.BuildCount);
    }

    [Fact]
    public void GridVersionChange_RebuildsOnNextGet()
    {
        NavGrid grid = Generated(4); // own grid: the version bump must not leak into other tests
        int target = PassableCells(grid)[10];
        var cache = new FlowFieldCache(grid);
        FlowField f = cache.Get(target);
        Assert.Equal(grid.Version, f.Version);

        grid.BumpVersionForTests();
        Assert.False(cache.Contains(target));
        FlowField g = cache.Get(target);
        Assert.Same(f, g);                      // rebuilt in place, not a second entry
        Assert.Equal(2, cache.BuildCount);
        Assert.Equal(1, cache.Count);
        Assert.Equal(grid.Version, g.Version);
        Assert.True(cache.Contains(target));
        cache.Get(target);
        Assert.Equal(2, cache.BuildCount);
    }

    [Fact]
    public void Get_RejectsOutOfRangeTarget()
    {
        var cache = new FlowFieldCache(Grid);
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.Get(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.Get(Grid.Width * Grid.Height));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FlowFieldCache(Grid, 0));
    }

    [Fact]
    public void PeekCached_Hit_ReturnsTheInstanceGetWould_AndIsNotAUse()
    {
        var cache = new FlowFieldCache(Grid, capacity: 2);
        FlowField a = cache.Get(Target(0));
        cache.Get(Target(1));
        Assert.Same(a, cache.PeekCached(Target(0))); // not a touch: target 0 stays the oldest
        Assert.Equal(2, cache.BuildCount);
        cache.Get(Target(2));
        Assert.False(cache.Contains(Target(0)));
        Assert.Null(cache.PeekCached(Target(0)));
        Assert.Same(cache.Get(Target(1)), cache.PeekCached(Target(1)));
        Assert.Equal(3, cache.BuildCount);
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void PeekCached_Miss_ReturnsNull_AndBuildsNothing()
    {
        var cache = new FlowFieldCache(Grid);
        Assert.Null(cache.PeekCached(Target(0)));
        Assert.Equal(0, cache.BuildCount);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void PeekCached_StaleVersion_ReturnsNull()
    {
        NavGrid grid = Generated(5); // own grid: the version bump must not leak into other tests
        int target = PassableCells(grid)[10];
        var cache = new FlowFieldCache(grid);
        FlowField f = cache.Get(target);
        Assert.Same(f, cache.PeekCached(target));
        grid.BumpVersionForTests();
        Assert.Null(cache.PeekCached(target));
        Assert.Equal(1, cache.BuildCount); // a peek never rebuilds
    }

    [Fact]
    public void PeekCached_OutOfRange_ReturnsNull()
    {
        var cache = new FlowFieldCache(Grid);
        cache.Get(Target(0));
        foreach (int cell in new[] { -1, int.MinValue, int.MaxValue, Grid.Width * Grid.Height })
            Assert.Null(cache.PeekCached(cell));
        Assert.Equal(1, cache.BuildCount);
    }

    /// <summary>
    /// Hash twin: two identical 500-unit marches, one peeked 500 times between every tick for 20
    /// ticks (10,000 peeks over every cell, including the goal's), keep equal state hashes, and the
    /// peeks themselves leave the hash unchanged.
    /// </summary>
    [Fact]
    public void PeekCached_TenThousandPeeksBetweenTicks_LeaveStateHashUnchanged()
    {
        const int units = 500, ticks = 20, peeksPerTick = 500;
        CrossMapScenario peeked = CrossMapScenario.Create(7, units, ScenarioTests.StartRadius);
        CrossMapScenario twin = CrossMapScenario.Create(7, units, ScenarioTests.StartRadius);
        peeked.OrderAll();
        twin.OrderAll();
        FlowFieldCache cache = peeked.Sim.World.FlowFields;
        int cells = peeked.Sim.World.NavGrid.Width * peeked.Sim.World.NavGrid.Height;

        int hits = 0, peeks = 0, next = 0;
        for (int t = 0; t < ticks; t++)
        {
            peeked.Sim.Tick();
            twin.Sim.Tick();
            ulong before = peeked.Sim.StateHash();
            int builds = cache.BuildCount;
            for (int k = 0; k < peeksPerTick; k++)
            {
                // Every other peek is the goal cell (a live field once built); the rest sweep the map.
                int cell = k % 2 == 0 ? peeked.GoalCell : next++ % cells;
                FlowField? f = cache.PeekCached(cell);
                if (f != null)
                {
                    hits++;
                    Assert.Equal(cell, f.RequestedCell);
                }
                peeks++;
            }
            Assert.Equal(builds, cache.BuildCount);
            Assert.Equal(before, peeked.Sim.StateHash());
            Assert.Equal(twin.Sim.StateHash(), peeked.Sim.StateHash());
        }
        Assert.Equal(10_000, peeks);
        Assert.True(hits >= (ticks - 1) * peeksPerTick / 2, $"only {hits} peeks hit; the goal field should be cached after the first tick");
    }

    private static void AssertSameAsFresh(FlowField cached)
    {
        FlowField fresh = FlowField.Build(Grid, cached.RequestedCell);
        Assert.Equal(fresh.TargetCell, cached.TargetCell);
        for (int c = 0; c < Grid.Width * Grid.Height; c++)
        {
            if (fresh.CostAt(c) != cached.CostAt(c) || fresh.DirectionAt(c) != cached.DirectionAt(c))
                Assert.Fail($"cell {c} differs from a fresh build");
        }
    }
}
