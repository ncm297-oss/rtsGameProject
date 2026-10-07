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

    private static void AssertSameAsFresh(FlowField cached, NavGrid? grid = null)
    {
        grid ??= Grid;
        FlowField fresh = FlowField.Build(grid, cached.RequestedCell);
        Assert.Equal(fresh.TargetCell, cached.TargetCell);
        for (int c = 0; c < grid.Width * grid.Height; c++)
        {
            if (fresh.CostAt(c) != cached.CostAt(c) || fresh.DirectionAt(c) != cached.DirectionAt(c))
                Assert.Fail($"cell {c} differs from a fresh build");
        }
    }

    // ---------- M3-2b: usable (only cells opened since the build) vs current ----------

    private static (NavGrid Grid, FlowFieldCache Cache, int[] Targets, int X, int Y) FiveCachedFieldsAndATree(ulong seed)
    {
        NavGrid grid = Generated(seed); // own grid: the changes must not leak into other tests
        List<int> open = PassableCells(grid);
        var targets = new int[5];
        for (int k = 0; k < 5; k++) targets[k] = open[(k + 1) * open.Count / 7];
        int tree = open.First(c => grid.CanTakeResource(c % grid.Width, c / grid.Width) && Array.IndexOf(targets, c) < 0);
        int x = tree % grid.Width, y = tree / grid.Width;
        grid.SetResource(x, y, 1, 1);
        var cache = new FlowFieldCache(grid);
        foreach (int t in targets) cache.Get(t);
        return (grid, cache, targets, x, y);
    }

    [Fact]
    public void OpeningChange_KeepsEveryCachedFieldUsable_ButStale()
    {
        (NavGrid grid, FlowFieldCache cache, int[] targets, int x, int y) = FiveCachedFieldsAndATree(6);
        FlowField[] fields = targets.Select(t => cache.PeekCached(t)!).ToArray();
        grid.ClearResource(x, y, 1, 1); // the tree falls
        int builds = cache.BuildCount;
        for (int k = 0; k < targets.Length; k++)
        {
            Assert.Same(fields[k], cache.PeekCached(targets[k]));
            Assert.True(cache.Contains(targets[k]));
            Assert.Same(fields[k], cache.TryGetCached(targets[k]));
            Assert.NotEqual(grid.Version, fields[k].Version);
            Assert.Equal(grid.BlockVersion, fields[k].BlockVersion);
            Assert.Equal(FlowField.NoDirection, fields[k].DirectionAt(y * grid.Width + x)); // the opened cell isn't in the old field
        }
        Assert.Equal(builds, cache.BuildCount); // no lookup builds, stale or not
    }

    [Fact]
    public void ClosingChange_LeavesNoCachedFieldUsable()
    {
        (NavGrid grid, FlowFieldCache cache, int[] targets, int x, int y) = FiveCachedFieldsAndATree(7);
        grid.ClearResource(x, y, 1, 1);
        grid.SetBuilding(x, y, 1, 1); // closes the cell again
        int builds = cache.BuildCount;
        foreach (int t in targets)
        {
            Assert.Null(cache.PeekCached(t));
            Assert.False(cache.Contains(t));
            Assert.Null(cache.TryGetCached(t));
        }
        Assert.Equal(builds, cache.BuildCount);
    }

    [Fact]
    public void Get_OnAUsableStaleSlot_RebuildsInPlace()
    {
        (NavGrid grid, FlowFieldCache cache, int[] targets, int x, int y) = FiveCachedFieldsAndATree(8);
        FlowField stale = cache.PeekCached(targets[2])!;
        grid.ClearResource(x, y, 1, 1);
        int builds = cache.BuildCount, count = cache.Count;
        FlowField rebuilt = cache.Get(targets[2]);
        Assert.Same(stale, rebuilt);
        Assert.Equal(builds + 1, cache.BuildCount);
        Assert.Equal(count, cache.Count);
        Assert.Equal((grid.Version, grid.BlockVersion), (rebuilt.Version, rebuilt.BlockVersion));
        AssertSameAsFresh(rebuilt, grid);
        cache.Get(targets[2]);
        Assert.Equal(builds + 1, cache.BuildCount); // current now: a hit
    }

    /// <summary>
    /// Every field the cache builds, as nodes come and go, has exactly the Dijkstra oracle's costs and
    /// directions: the lazy-deletion bucket queue, the unrolled directions picked at relax time and the
    /// sliding-window step masks (BUG-0082) change speed, not results.
    /// </summary>
    [Fact]
    public void Get_AfterNodesComeAndGo_MatchesTheOracleOnEveryCell()
    {
        NavGrid grid = Generated(11);
        List<int> open = PassableCells(grid);
        var cache = new FlowFieldCache(grid, capacity: 4);
        for (int round = 0; round < 6; round++)
        {
            for (int t = 0; t < 4; t++)
            {
                int target = open[(round * 4 + t) * 211 % open.Count];
                FlowField f = cache.Get(target);
                (float[] cost, byte[] dir, int resolved) = Solve(grid, target);
                Assert.Equal(resolved, f.TargetCell);
                for (int c = 0; c < cost.Length; c++)
                {
                    if (cost[c] != f.CostAt(c)) Assert.Fail($"round {round} target {target} cell {c}: cost {f.CostAt(c)}, oracle {cost[c]}");
                    if (dir[c] != f.DirectionAt(c)) Assert.Fail($"round {round} target {target} cell {c}: direction {f.DirectionAt(c)}, oracle {dir[c]}");
                }
            }
            for (int k = round % 3; k < open.Count; k += 17) // nodes appear on a scatter of cells, then go again
            {
                int x = open[k] % grid.Width, y = open[k] / grid.Width;
                if ((grid.FlagsAt(x, y) & NavFlags.Resource) != 0) grid.ClearResource(x, y, 1, 1);
                else if (round % 2 == 0 && grid.CanTakeResource(x, y)) grid.SetResource(x, y, 1, 1);
            }
        }
    }

    /// <summary>The step masks' fast path (inner cells read the flags directly) gives the per-cell definition's bits on every cell, also after nodes come and go.</summary>
    [Fact]
    public void ComputeSteps_MatchesTheBoundsCheckedDefinition_OnEveryCell()
    {
        NavGrid grid = Generated(9);
        List<int> open = PassableCells(grid);
        var steps = new byte[grid.Width * grid.Height];
        for (int round = 0; round < 3; round++)
        {
            FlowField.ComputeSteps(grid, steps);
            for (int c = 0; c < steps.Length; c++)
                if (steps[c] != FlowField.StepMask(grid, c % grid.Width, c / grid.Width)) Assert.Fail($"round {round}: cell {c}");
            for (int k = round % 2; k < open.Count; k += 13) // block two scatters of cells, then reopen the first
            {
                int x = open[k] % grid.Width, y = open[k] / grid.Width;
                if (round < 2 && grid.CanTakeResource(x, y)) grid.SetResource(x, y, 1, 1);
                else if (round == 2 && (grid.FlagsAt(x, y) & NavFlags.Resource) != 0) grid.ClearResource(x, y, 1, 1);
            }
        }
    }
}
