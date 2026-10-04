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
