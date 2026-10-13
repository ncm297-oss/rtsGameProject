using System.Diagnostics;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Map;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>M5-4a: the generator's start locations (mirrored open discs) and the placer's start mines and forest.</summary>
[Collection(SerialCollection.Name)]
public class MapGenTests
{
    private readonly ITestOutputHelper _out;

    public MapGenTests(ITestOutputHelper output) => _out = output;

    /// <summary>The match's map (12 forests, 8 mines) at <paramref name="size"/>.</summary>
    internal static World MatchWorld(ulong seed, int size) =>
        new(TestSim.Config(seed, 2, 64, 64) with { Map = MapGenParams.Default with { Width = size, Height = size, Forests = 12, GoldMines = 8 } });

    public static IEnumerable<object[]> SeedsBySize()
    {
        foreach (int size in new[] { 128, 192, 256 })
            for (ulong seed = 1; seed <= 12; seed++)
                yield return new object[] { size, seed };
    }

    [Theory]
    [MemberData(nameof(SeedsBySize))]
    public void GeneratedMap_HasTwoMirroredStartLocations_EachWithTwoMinesAndAForest(int size, ulong seed)
    {
        World w = MatchWorld(seed, size);
        MapGenParams p = w.Config.Map;
        NavGrid g = w.NavGrid;
        ReadOnlySpan<StartLocation> starts = w.StartLocations;
        Assert.Equal(2, starts.Length);
        int x0 = starts[0].Cell % size, y0 = starts[0].Cell / size, x1 = starts[1].Cell % size, y1 = starts[1].Cell / size;
        Assert.True(Math.Abs(x1 - (size - x0)) <= 1 && Math.Abs(y1 - (size - y0)) <= 1, $"({x0},{y0}) and ({x1},{y1}) are not mirrored");
        Assert.True(x0 < size / 2, "player 0 is not in the west half");
        string ctx = $"{size} seed {seed}";
        foreach (StartLocation st in starts)
        {
            int cx = st.Cell % size, cy = st.Cell / size, r = p.StartClearRadius;
            int inside = Math.Min(Math.Min(cx - r, cy - r), Math.Min(size - 1 - cx - r, size - 1 - cy - r));
            Assert.True(inside >= p.StartEdgeMargin, $"{ctx}: {st} is {inside} cells inside");
            for (int y = cy - r; y <= cy + r; y++)
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) > r * r) continue;
                    Assert.True(g.IsPassable(x, y) && !w.Heightmap.IsRamp(x, y) && g.LevelAt(x, y) == st.Level,
                        $"{ctx}: clear disc cell ({x},{y}) of {st} is blocked, a ramp or off level");
                }
            Assert.True(StartMines(w, st) >= 2, $"{ctx}: {st} has {StartMines(w, st)} mines within {p.StartMineDistance} m");
            int forest = LargestNearForest(w, st);
            Assert.True(forest >= p.ForestMinTrees, $"{ctx}: {st}'s nearest forest within {p.StartForestDistance} m has {forest} trees");
        }
        _out.WriteLine($"{ctx}: starts ({x0},{y0}) L{starts[0].Level} / ({x1},{y1}) L{starts[1].Level}, placed {w.ResourcePlacement}");
    }

    /// <summary>Live gold nodes on the location's level whose centre is within the start mine distance and whose footprint is off the clear disc.</summary>
    internal static int StartMines(World w, StartLocation st)
    {
        int size = w.NavGrid.Width, cx = st.Cell % size, cy = st.Cell / size, r = w.Config.Map.StartClearRadius, n = 0;
        for (int k = 0; k < w.Resources.Capacity; k++)
        {
            if (!w.Resources.Alive[k]) continue;
            ResourceDef def = w.Data.Resources[w.Resources.TypeId[k]];
            if (def.Resource != ResourceKind.Gold) continue;
            int ax = w.Resources.Cell[k] % size, ay = w.Resources.Cell[k] / size;
            float dx = (ax + def.FootprintWidth * 0.5f - cx - 0.5f) * MapConstants.CellSize, dy = (ay + def.FootprintHeight * 0.5f - cy - 0.5f) * MapConstants.CellSize;
            if (dx * dx + dy * dy > w.Config.Map.StartMineDistance * w.Config.Map.StartMineDistance || w.NavGrid.LevelAt(ax, ay) != st.Level) continue;
            for (int y = ay; y < ay + def.FootprintHeight; y++)
                for (int x = ax; x < ax + def.FootprintWidth; x++)
                    Assert.True((x - cx) * (x - cx) + (y - cy) * (y - cy) > r * r, $"mine at ({ax},{ay}) covers the clear disc of {st}");
            Assert.Equal(w.Data.Rules.StartMineGold, w.Resources.Remaining[k]);
            n++;
        }
        return n;
    }

    /// <summary>The largest 8-connected group of trees with a tree on the location's level within the start forest distance (centre to centre).</summary>
    internal static int LargestNearForest(World w, StartLocation st)
    {
        NavGrid g = w.NavGrid;
        int size = g.Width, cx = st.Cell % size, cy = st.Cell / size, best = 0;
        float max = w.Config.Map.StartForestDistance / MapConstants.CellSize;
        var seen = new bool[size * g.Height];
        for (int i = 0; i < seen.Length; i++)
        {
            int x = i % size, y = i / size;
            if (seen[i] || !IsTree(w, x, y) || g.LevelAt(x, y) != st.Level || (x - cx) * (x - cx) + (y - cy) * (y - cy) > max * max) continue;
            var queue = new Queue<int>();
            queue.Enqueue(i);
            seen[i] = true;
            int count = 0;
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                count++;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = c % size + dx, ny = c / size + dy;
                        if (!g.InBounds(nx, ny) || seen[ny * size + nx] || !IsTree(w, nx, ny)) continue;
                        seen[ny * size + nx] = true;
                        queue.Enqueue(ny * size + nx);
                    }
            }
            best = Math.Max(best, count);
        }
        return best;
    }

    private static bool IsTree(World w, int x, int y)
    {
        if ((w.NavGrid.FlagsAt(x, y) & NavFlags.Resource) == 0) return false;
        int node = ViewApi.ViewReads.NodeAt(w, y * w.NavGrid.Width + x);
        return node >= 0 && w.Data.Resources[w.Resources.TypeId[node]].Resource == ResourceKind.Wood;
    }

    [Fact]
    public void FourStartLocations_AreQuarterTurns_AndApart()
    {
        foreach (int n in new[] { 3, 4 })
        {
            var p = MapGenParams.Default with { StartLocations = n };
            var rng = new SimRng(5, RngStream.MapGen);
            Heightmap hm = MapGenerator.Generate(p, ref rng);
            ReadOnlySpan<StartLocation> s = hm.StartLocations;
            Assert.Equal(n, s.Length);
            int size = p.Width, x0 = s[0].Cell % size, y0 = s[0].Cell / size;
            Assert.Equal((size - 1 - x0) + (size - 1 - y0) * size, s[1].Cell);
            Assert.Equal((size - 1 - y0) + x0 * size, s[2].Cell);
            if (n == 4) Assert.Equal(y0 + (size - 1 - x0) * size, s[3].Cell);
            for (int i = 0; i < n; i++)
                for (int j = 0; j < i; j++)
                {
                    int dx = s[i].Cell % size - s[j].Cell % size, dy = s[i].Cell / size - s[j].Cell / size;
                    Assert.True(dx * dx + dy * dy > 4 * p.StartSiteRadius * p.StartSiteRadius);
                }
        }
    }

    [Fact]
    public void NoStartLocations_GivesEmptySpans_AndTheSameTerrainAsAFittingLayout()
    {
        var rng0 = new SimRng(1, RngStream.MapGen);
        var rng2 = rng0;
        Heightmap none = MapGenerator.Generate(MapGenParams.Default with { StartLocations = 0 }, ref rng0);
        Heightmap two = MapGenerator.Generate(MapGenParams.Default, ref rng2);
        Assert.True(none.StartLocations.IsEmpty);
        Assert.True(new NavGrid(none).StartLocations.IsEmpty);
        // Seed 1's first layout fits the pair, and choosing them draws nothing.
        Assert.Equal(none.ContentHash(), two.ContentHash());
        Assert.Equal(rng0.State, rng2.State);
        Assert.Equal(2, two.StartLocations.Length);
    }

    [Fact]
    public void SameSeed_GivesTheSameStartLocationsAndResources()
    {
        World a = MatchWorld(7, 128), b = MatchWorld(7, 128);
        Assert.True(a.StartLocations.SequenceEqual(b.StartLocations));
        Assert.Equal(a.ResourcePlacement, b.ResourcePlacement);
        Assert.True(a.Resources.Cell.SequenceEqual(b.Resources.Cell));
    }

    [Fact]
    public void HandBuiltHeightmap_HasNoStartLocations_AndRefusesABadOne()
    {
        var levels = new byte[16 * 16];
        var heights = new float[16 * 16];
        Assert.True(new Heightmap(16, 16, levels, heights).StartLocations.IsEmpty);
        var hm = new Heightmap(16, 16, levels, heights, new[] { new StartLocation(8 * 16 + 4, 0) });
        Assert.Equal(new StartLocation(8 * 16 + 4, 0), new NavGrid(hm).StartLocations[0]);
        Assert.Throws<ArgumentException>(() => new Heightmap(16, 16, levels, heights, new[] { new StartLocation(256, 0) }));
        Assert.Throws<ArgumentException>(() => new Heightmap(16, 16, levels, heights, new[] { new StartLocation(3, 1) }));
    }

    public static IEnumerable<object[]> BadStartParams()
    {
        MapGenParams d = MapGenParams.Default;
        yield return new object[] { d with { StartLocations = 1 }, nameof(MapGenParams.StartLocations) };
        yield return new object[] { d with { StartLocations = 5 }, nameof(MapGenParams.StartLocations) };
        yield return new object[] { d with { StartLocations = -2 }, nameof(MapGenParams.StartLocations) };
        yield return new object[] { d with { StartLocations = 4, Width = 192 }, nameof(MapGenParams.StartLocations) };
        yield return new object[] { d with { StartClearRadius = 1 }, nameof(MapGenParams.StartClearRadius) };
        yield return new object[] { d with { StartClearRadius = 33 }, nameof(MapGenParams.StartClearRadius) };
        yield return new object[] { d with { StartEdgeMargin = 0 }, nameof(MapGenParams.StartEdgeMargin) };
        yield return new object[] { d with { StartEdgeMargin = 129 }, nameof(MapGenParams.StartEdgeMargin) };
        yield return new object[] { d with { StartMineDistance = 16f }, nameof(MapGenParams.StartMineDistance) };
        yield return new object[] { d with { StartMineDistance = 32.5f }, nameof(MapGenParams.StartMineDistance) };
        yield return new object[] { d with { StartMineDistance = float.NaN }, nameof(MapGenParams.StartMineDistance) };
        yield return new object[] { d with { StartForestDistance = 16f }, nameof(MapGenParams.StartForestDistance) };
        yield return new object[] { d with { StartForestDistance = 128.5f }, nameof(MapGenParams.StartForestDistance) };
        yield return new object[] { d with { StartForestDistance = float.NaN }, nameof(MapGenParams.StartForestDistance) };
        // The disc can't fit: a tiny map, a disc too large for the half map, a margin too wide.
        yield return new object[] { d with { Width = 32, Height = 32, EdgeMargin = 3, Level1MaxSize = 20 }, nameof(MapGenParams.StartClearRadius) };
        yield return new object[] { d with { StartClearRadius = 28, StartMineDistance = 58f, StartForestDistance = 60f }, nameof(MapGenParams.StartClearRadius) };
        yield return new object[] { d with { StartEdgeMargin = 46 }, nameof(MapGenParams.StartClearRadius) };
        yield return new object[] { d with { Width = 128, Height = 36, Level1MaxSize = 20 }, nameof(MapGenParams.StartClearRadius) };
    }

    [Theory]
    [MemberData(nameof(BadStartParams))]
    public void Validate_RefusesOutOfRangeStartParams_ByName(MapGenParams p, string name)
    {
        var e = Assert.Throws<ArgumentOutOfRangeException>(p.Validate);
        Assert.Equal(name, e.ParamName);
        var rng = new SimRng(1, RngStream.MapGen);
        Assert.Throws<ArgumentOutOfRangeException>(() => MapGenerator.Generate(p, ref rng));
    }

    [Fact]
    public void TinyMap_FitsOnlyWithoutStartLocations()
    {
        var tiny = MapGenParams.Default with { Width = 32, Height = 32, EdgeMargin = 3, Level1MaxSize = 20, StartLocations = 0 };
        tiny.Validate();
        var rng = new SimRng(1, RngStream.MapGen);
        Assert.True(MapGenerator.Generate(tiny, ref rng).StartLocations.IsEmpty);
        // The smallest square map the default start rule fits: 2 x (margin 10 + radius 8 + site radius 10 + 1) = 58.
        (MapGenParams.Default with { Width = 58, Height = 58, Level1MaxSize = 30 }).Validate();
        Assert.Throws<ArgumentOutOfRangeException>(() => (MapGenParams.Default with { Width = 57, Height = 57, Level1MaxSize = 30 }).Validate());
    }

    [Fact]
    public void SmallestMap_AlwaysGetsItsPair_EvenOnTheFlatFallback()
    {
        // Plateaus everywhere leave no open disc; the generator redraws, then falls back to the flat map, which has room.
        var crowded = MapGenParams.Default with { Width = 58, Height = 58, EdgeMargin = 1, Level1Plateaus = 32, Level1MinSize = 10, Level1MaxSize = 30, MaxAttempts = 2 };
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var rng = new SimRng(seed, RngStream.MapGen);
            Assert.Equal(2, MapGenerator.Generate(crowded, ref rng).StartLocations.Length);
        }
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void Generate256_WithStartLocations_IsUnder50Ms()
    {
        var p = MapGenParams.Default with { Width = 256, Height = 256 };
        double worst = 0;
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var rng = new SimRng(seed, RngStream.MapGen);
            var sw = Stopwatch.StartNew();
            MapGenerator.Generate(p, ref rng);
            worst = Math.Max(worst, sw.Elapsed.TotalMilliseconds);
        }
        _out.WriteLine($"256 map with start locations: worst {worst:F1} ms over 12 seeds");
        Assert.True(worst < 50, $"took {worst:F1} ms");
    }
}
