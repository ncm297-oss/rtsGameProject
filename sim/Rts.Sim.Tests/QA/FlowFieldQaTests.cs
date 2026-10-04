using System.Diagnostics;
using Rts.Sim.Determinism;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA attacks on the M1-4b flow field and cache, checked against QA's own double-precision oracle
/// (written from docs/03 and the M1-4b brief, independent of the developer's FlowFieldOracle).
/// </summary>
public class FlowFieldQaTests
{
    private readonly ITestOutputHelper _out;

    public FlowFieldQaTests(ITestOutputHelper output) => _out = output;

    // ---------- QA oracle ----------

    private static readonly int[] Dx = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private static readonly int[] Dy = { 0, 1, 1, 1, 0, -1, -1, -1 };

    private static bool Pass(NavGrid g, int x, int y) =>
        x >= 0 && y >= 0 && x < g.Width && y < g.Height && (g.FlagsAt(x, y) & NavFlags.Blocked) == 0;

    /// <summary>docs/03: 8-connected, no corner cutting (both side cells of a diagonal passable).</summary>
    internal static bool QaAllowed(NavGrid g, int x, int y, int d)
    {
        int dx = Dx[d], dy = Dy[d];
        if (!Pass(g, x, y) || !Pass(g, x + dx, y + dy)) return false;
        return dx == 0 || dy == 0 || (Pass(g, x + dx, y) && Pass(g, x, y + dy));
    }

    private static double QaStep(int d) => (d & 1) == 1 ? 1.41421 : 1.0;

    internal static int QaNearest(NavGrid g, int cell)
    {
        int tx = cell % g.Width, ty = cell / g.Width;
        int best = -1;
        long bd = long.MaxValue;
        for (int y = 0; y < g.Height; y++)
            for (int x = 0; x < g.Width; x++)
            {
                if (!Pass(g, x, y)) continue;
                long d = (long)(x - tx) * (x - tx) + (long)(y - ty) * (y - ty);
                if (d < bd || (d == bd && (y < best / g.Width || (y == best / g.Width && x < best % g.Width))))
                {
                    bd = d;
                    best = y * g.Width + x;
                }
            }
        return best;
    }

    /// <summary>Double-precision Dijkstra from the resolved target.</summary>
    internal static (double[] Cost, int Target) QaSolve(NavGrid g, int requested)
    {
        int w = g.Width, n = w * g.Height;
        var cost = new double[n];
        Array.Fill(cost, double.PositiveInfinity);
        int target = Pass(g, requested % w, requested / w) ? requested : QaNearest(g, requested);
        if (target < 0) return (cost, target);
        var pq = new PriorityQueue<int, double>();
        cost[target] = 0;
        pq.Enqueue(target, 0);
        while (pq.TryDequeue(out int c, out double pc))
        {
            if (pc > cost[c]) continue;
            int cx = c % w, cy = c / w;
            for (int d = 0; d < 8; d++)
            {
                if (!QaAllowed(g, cx, cy, d)) continue;
                int nb = (cy + Dy[d]) * w + cx + Dx[d];
                double nc = pc + QaStep(d);
                if (nc < cost[nb])
                {
                    cost[nb] = nc;
                    pq.Enqueue(nb, nc);
                }
            }
        }
        return (cost, target);
    }

    /// <summary>Checks one field against the oracle; returns null when fine, else a message.</summary>
    internal static string? Check(NavGrid g, FlowField f, int requested)
    {
        (double[] oc, int target) = QaSolve(g, requested);
        if (f.RequestedCell != requested) return $"RequestedCell {f.RequestedCell} != {requested}";
        if (f.TargetCell != target) return $"TargetCell {f.TargetCell} != oracle {target} (requested {requested})";
        int w = g.Width;
        for (int c = 0; c < w * g.Height; c++)
        {
            int cx = c % w, cy = c / w;
            float fc = f.CostAt(c);
            byte dir = f.DirectionAt(c);
            if (double.IsPositiveInfinity(oc[c]))
            {
                if (!float.IsPositiveInfinity(fc)) return $"cell ({cx},{cy}) unreachable but cost {fc}";
                if (dir != FlowField.NoDirection) return $"cell ({cx},{cy}) unreachable but dir {dir}";
                continue;
            }
            if (!float.IsFinite(fc)) return $"cell ({cx},{cy}) reachable (oracle {oc[c]}) but cost {fc}";
            if (Math.Abs(fc - oc[c]) > 1e-4 * (1 + oc[c])) return $"cell ({cx},{cy}) cost {fc} vs oracle {oc[c]}";
            if (c == target)
            {
                if (dir != FlowField.NoDirection) return $"target ({cx},{cy}) has dir {dir}";
                continue;
            }
            if (dir >= 8) return $"reachable cell ({cx},{cy}) has no direction";
            if (!QaAllowed(g, cx, cy, dir)) return $"cell ({cx},{cy}) dir {dir} is not an allowed step (blocked or corner cut)";
            int nb = (cy + Dy[dir]) * w + cx + Dx[dir];
            if (Math.Abs(oc[nb] + QaStep(dir) - oc[c]) > 1e-4 * (1 + oc[c]))
                return $"cell ({cx},{cy}) dir {dir} is not on a shortest path ({oc[nb]} + step vs {oc[c]})";
            if (!(f.CostAt(nb) < fc)) return $"cell ({cx},{cy}) dir {dir} does not strictly descend ({f.CostAt(nb)} vs {fc})";
            // Tie rule: lowest direction number among exact float minima of the field's own costs.
            float best = f.CostAt(nb) + (float)((dir & 1) == 1 ? FlowField.DiagonalCost : 1f);
            for (int d = 0; d < 8; d++)
            {
                if (d == dir || !QaAllowed(g, cx, cy, d)) continue;
                int m = (cy + Dy[d]) * w + cx + Dx[d];
                float via = f.CostAt(m) + ((d & 1) == 1 ? FlowField.DiagonalCost : 1f);
                if (d < dir ? !(via > best) : via < best) return $"cell ({cx},{cy}) tie rule: dir {dir} ({best}) but dir {d} gives {via}";
            }
        }
        return null;
    }

    private static NavGrid Gen(MapGenParams p, ulong seed)
    {
        var rng = new SimRng(seed, RngStream.MapGen);
        return new NavGrid(MapGenerator.Generate(p, ref rng));
    }

    private static readonly MapGenParams[] FuzzParams =
    {
        MapGenParams.Default,
        MapGenParams.Default with { RampWidth = 1, RampLength = 6 },
        MapGenParams.Default with { Width = 96, Height = 48, Level1Plateaus = 3, Level1MaxSize = 20, Level2MaxSize = 10 },
        MapGenParams.Default with { Level1Plateaus = 12, Level2Plateaus = 8, RampsPerPlateau = 1 },
        MapGenParams.Default with { Width = 64, Height = 64, Level1Plateaus = 6, Level1MaxSize = 20, Level2MaxSize = 10, RampWidth = 2 },
    };

    /// <summary>A random cell of the requested kind (0 passable, 1 interior blocked, 2 border ring), or -1.</summary>
    private static int PickCell(NavGrid g, ref SimRng rng, int kind)
    {
        int w = g.Width, h = g.Height;
        if (kind == 2)
        {
            return rng.NextInt(0, 4) switch
            {
                0 => rng.NextInt(0, w),
                1 => (h - 1) * w + rng.NextInt(0, w),
                2 => rng.NextInt(0, h) * w,
                _ => rng.NextInt(0, h) * w + w - 1,
            };
        }
        for (int tries = 0; tries < 20000; tries++)
        {
            int x = rng.NextInt(1, w - 1), y = rng.NextInt(1, h - 1);
            if (Pass(g, x, y) == (kind == 0)) return y * w + x;
        }
        return -1;
    }

    // ---------- fuzz: generated maps ----------

    [Fact]
    public void Fuzz_200GeneratedMaps_x4Targets_MatchQaOracle()
    {
        var sw = Stopwatch.StartNew();
        int checkedFields = 0, blockedTargets = 0;
        for (int m = 0; m < 200; m++)
        {
            MapGenParams p = FuzzParams[m % FuzzParams.Length];
            NavGrid g = Gen(p, (ulong)(1000 + m));
            var cache = new FlowFieldCache(g, 2);
            var rng = new SimRng((ulong)m, 77);
            for (int kind = 0; kind < 4; kind++)
            {
                int cell = kind == 3 ? 0 : PickCell(g, ref rng, kind);
                if (cell < 0) continue;
                if (kind > 0) blockedTargets++;
                FlowField f = cache.Get(cell);
                string? err = Check(g, f, cell);
                Assert.True(err == null, $"map {m} ({p.Width}x{p.Height}) kind {kind}: {err}");
                checkedFields++;
            }
        }
        _out.WriteLine($"{checkedFields} fields ({blockedTargets} blocked targets) on 200 maps in {sw.ElapsedMilliseconds} ms");
        Assert.True(checkedFields >= 780);
    }

    // ---------- fuzz: adversarial random heightmaps (dense cliffs, pockets, diagonal gaps) ----------

    private static NavGrid RandomHeightmap(ref SimRng rng, int w, int h, int style)
    {
        var levels = new byte[w * h];
        var elev = new float[w * h];
        for (int i = 0; i < w * h; i++)
        {
            int lvl = style switch
            {
                0 => rng.NextInt(0, 3),                     // salt-and-pepper: cliffs everywhere
                1 => rng.NextInt(0, 10) == 0 ? 1 : 0,       // sparse single-cell walls: diagonal gaps
                _ => (i % w / 3 + i / w / 3) % 2,           // checkerboard of 3x3 blocks: corner touches
            };
            levels[i] = (byte)lvl;
            elev[i] = lvl * MapConstants.LevelHeight;
        }
        return new NavGrid(new Heightmap(w, h, levels, elev));
    }

    [Fact]
    public void Fuzz_600AdversarialHeightmaps_MatchQaOracle()
    {
        var rng = new SimRng(4242, 1);
        int fields = 0, noPassable = 0;
        for (int i = 0; i < 600; i++)
        {
            int w = rng.NextInt(1, 41), h = rng.NextInt(1, 41);
            NavGrid g = RandomHeightmap(ref rng, w, h, i % 3);
            var cache = new FlowFieldCache(g, 3);
            for (int t = 0; t < 4; t++)
            {
                int cell = rng.NextInt(0, w * h);
                FlowField f = cache.Get(cell);
                if (f.TargetCell < 0) noPassable++;
                string? err = Check(g, f, cell);
                Assert.True(err == null, $"heightmap {i} ({w}x{h}, style {i % 3}) target {cell}: {err}");
                fields++;
            }
        }
        _out.WriteLine($"{fields} fields, {noPassable} on grids with no passable cell");
        Assert.True(noPassable > 0, "the fuzz never produced an all-blocked grid");
    }

    [Fact]
    public void LongPaths_SerpentineMaze_CostsStayExact()
    {
        // A 1-wide serpentine corridor makes the longest possible path on a 1024 x 64 map: the
        // bucket queue's ring and the float costs both see values in the tens of thousands.
        const int w = 1024, h = 64;
        var levels = new byte[w * h];
        var elev = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool wall = y % 4 == 0 && !(y / 4 % 2 == 0 ? x == w - 3 : x == 2);
                byte lvl = (byte)(wall ? 1 : 0);
                levels[y * w + x] = lvl;
                elev[y * w + x] = lvl * MapConstants.LevelHeight;
            }
        var g = new NavGrid(new Heightmap(w, h, levels, elev));
        FlowField f = FlowField.Build(g, 2 * w + 2);
        Assert.Null(Check(g, f, 2 * w + 2));
        float max = 0;
        for (int c = 0; c < w * h; c++) if (float.IsFinite(f.CostAt(c))) max = Math.Max(max, f.CostAt(c));
        _out.WriteLine($"max path cost {max}");
        Assert.True(max > 5000, $"maze too short: {max}");
    }

    // ---------- cache ----------

    [Fact]
    public void Cache_RandomAccess_MatchesLruModel()
    {
        NavGrid g = FlowFieldOracle.RampCorridor();
        foreach (int capacity in new[] { 1, 2, 7, 32 })
        {
            var cache = new FlowFieldCache(g, capacity);
            var model = new List<int>(); // most recent last
            var rng = new SimRng((ulong)capacity, 3);
            int expectedBuilds = 0;
            for (int i = 0; i < 3000; i++)
            {
                int cell = rng.NextInt(0, capacity + 6); // a working set a little bigger than the cache
                bool hit = model.Remove(cell);
                if (!hit)
                {
                    expectedBuilds++;
                    if (model.Count == capacity) model.RemoveAt(0);
                }
                model.Add(cell);
                FlowField f = cache.Get(cell);
                Assert.Equal(cell, f.RequestedCell);
                Assert.Equal(expectedBuilds, cache.BuildCount);
                if (i % 97 == 0)
                    for (int c = 0; c < capacity + 6; c++)
                        Assert.Equal(model.Contains(c), cache.Contains(c));
            }
        }
    }

    [Fact]
    public void Cache_VersionBump_RebuildsEveryCachedFieldOnce_AndContainsSeesStale()
    {
        NavGrid g = FlowFieldOracle.Generated(9);
        var cache = new FlowFieldCache(g, 4);
        int a = MoveScenario.CentralCell(g);
        int b = FlowField.NearestPassable(g, 10 * g.Width + 10);
        FlowField fa = cache.Get(a);
        cache.Get(b);
        Assert.Equal(2, cache.BuildCount);
        g.BumpVersionForTests();
        Assert.False(cache.Contains(a));
        Assert.False(cache.Contains(b));
        Assert.Same(fa, cache.Get(a)); // rebuilt in place
        Assert.Equal(g.Version, fa.Version);
        Assert.Equal(3, cache.BuildCount);
        cache.Get(a);
        Assert.Equal(3, cache.BuildCount);
        Assert.True(cache.Contains(a));
        Assert.False(cache.Contains(b));
        Assert.Null(Check(g, fa, a));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(128 * 128)]
    [InlineData(int.MaxValue)]
    public void Cache_Get_OutOfRangeCell_Throws_AndLeavesCacheUsable(int cell)
    {
        NavGrid g = FlowFieldOracle.Generated(9);
        var cache = new FlowFieldCache(g, 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.Get(cell));
        Assert.Equal(0, cache.Count);
        int c = MoveScenario.CentralCell(g);
        Assert.Null(Check(g, cache.Get(c), c));
    }

    [Fact]
    public void Cache_Thrash_64TargetsRoundRobin_AllocatesNothing_AndMeasuresBuildCost()
    {
        NavGrid g = FlowFieldOracle.Generated(21);
        var cache = new FlowFieldCache(g);
        List<int> passable = FlowFieldOracle.PassableCells(g);
        var targets = new int[64];
        var rng = new SimRng(21, 5);
        for (int i = 0; i < targets.Length; i++) targets[i] = passable[rng.NextInt(0, passable.Count)];
        for (int i = 0; i < targets.Length; i++) cache.Get(targets[i]); // JIT + fill
        int builds = cache.BuildCount;

        var sw = new Stopwatch();
        long before = GC.GetAllocatedBytesForCurrentThread();
        sw.Start();
        for (int round = 0; round < 3; round++)
            for (int i = 0; i < targets.Length; i++) cache.Get(targets[i]);
        sw.Stop();
        long delta = GC.GetAllocatedBytesForCurrentThread() - before;

        // Round-robin over 64 targets with 32 slots is LRU's worst case: every Get misses.
        Assert.Equal(builds + 3 * 64, cache.BuildCount);
        _out.WriteLine($"192 thrashing Gets: {sw.Elapsed.TotalMilliseconds:F1} ms, {sw.Elapsed.TotalMilliseconds / 192:F3} ms per build, {delta} bytes");
        Assert.Equal(0, delta);
    }
}
