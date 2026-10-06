using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA (M3-1): the resource placer checked by an independent oracle over hundreds of seeds and several
/// parameter sets (dense forests, many mines on small and non-square maps, the caps). The oracle
/// re-derives every rule from the bare map (same seed, no resources) and never calls the placer's own
/// helpers: open-ground footprints, exact flag/cost deltas, no overlaps, 4-connected reach of every
/// passable cell, mine spacing, forest groups (no partial forest), one version bump per node,
/// determinism on a second world, RNG streams; then fells every node in a seeded order and checks
/// the grid returns exactly to the bare map with connectivity kept at every step.
/// </summary>
[Collection(SerialCollection.Name)] // the 1024-map setup row is a wall-clock Perf test
public class ResourcePlacementStressTests
{
    private readonly ITestOutputHelper _out;

    public ResourcePlacementStressTests(ITestOutputHelper output) => _out = output;

    private static readonly MapGenParams Small48 = MapGenParams.Default with
    {
        Width = 48, Height = 48, EdgeMargin = 2, Level1Plateaus = 3, Level1MinSize = 6, Level1MaxSize = 16,
        Level2Plateaus = 2, Level2MinSize = 5, Level2MaxSize = 8, Level2Inset = 1,
    };

    private static readonly MapGenParams Rect64x32 = MapGenParams.Default with
    {
        Width = 64, Height = 32, EdgeMargin = 2, Level1Plateaus = 3, Level1MinSize = 6, Level1MaxSize = 14,
        Level2Plateaus = 2, Level2MinSize = 5, Level2MaxSize = 8, Level2Inset = 1,
    };

    public static IEnumerable<object[]> Sets()
    {
        // name, params, first seed, seed count
        yield return new object[] { "design 128 F12 M8", MapGenParams.Default with { Forests = 12, GoldMines = 8 }, 21UL, 80 };
        yield return new object[] { "dense 128 F40 x 60", MapGenParams.Default with { Forests = 40, ForestMinTrees = 60, ForestMaxTrees = 60, GoldMines = 8 }, 1UL, 40 };
        yield return new object[] { "48x48 M20 F4", Small48 with { GoldMines = 20, Forests = 4, MineSpacing = 6f }, 1UL, 50 };
        yield return new object[] { "64x32 M20 F6", Rect64x32 with { GoldMines = 20, Forests = 6, MineSpacing = 6f }, 1UL, 50 };
        yield return new object[] { "48x48 M64 spacing 0, F64 x 1-256", Small48 with { GoldMines = 64, MineSpacing = 0f, Forests = 64, ForestMinTrees = 1, ForestMaxTrees = 256 }, 1UL, 20 };
        yield return new object[] { "caps 128 F64 x 200-256 M64", MapGenParams.Default with { Forests = 64, ForestMinTrees = 200, ForestMaxTrees = 256, GoldMines = 64, MineSpacing = 0f }, 1UL, 6 };
    }

    [Theory]
    [Trait("Category", "Soak")] // about 45 s in Debug; filter out with Category!=Soak for a quick loop
    [MemberData(nameof(Sets))]
    public void Placement_IndependentOracle_ManySeeds(string name, MapGenParams map, ulong firstSeed, int seeds)
    {
        int requestedF = 0, placedF = 0, requestedM = 0, placedM = 0, trees = 0, hollowSeeds = 0;
        long worstMs = 0;
        for (ulong seed = firstSeed; seed < firstSeed + (ulong)seeds; seed++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            World w = NewWorld(seed, map);
            sw.Stop();
            worstMs = Math.Max(worstMs, sw.ElapsedMilliseconds);
            World bare = NewWorld(seed, map with { Forests = 0, GoldMines = 0 });
            string? err = ResourceOracle.Check(w, bare, map);
            Assert.True(err == null, $"{name} seed {seed}: {err}");

            // Determinism: a second world places the very same nodes.
            World again = NewWorld(seed, map);
            Assert.True(w.Resources.Cell.SequenceEqual(again.Resources.Cell), $"{name} seed {seed}: second world differs");
            Assert.True(w.Resources.TypeId.SequenceEqual(again.Resources.TypeId));
            Assert.Equal(new Simulation(w.Config).StateHash(), new Simulation(again.Config).StateHash());

            // Fell everything as gathering can (a node is taken only once a 4-neighbor of it is reachable):
            // every passable cell must stay reachable, and the grid must end equal to the bare map.
            err = ResourceOracle.FellAll(w, bare, new SimRng(seed, 4242), checkEvery: 7, reachableOnly: true, out _);
            Assert.True(err == null, $"{name} seed {seed}: {err}");
            // In any order (an interior tree first), on the first 5 seeds: the grid still ends equal to the bare map;
            // fells after which some open cell can't be reached are counted (BUG-0075).
            if (seed < firstSeed + 5)
            {
                World w2 = NewWorld(seed, map);
                err = ResourceOracle.FellAll(w2, bare, new SimRng(seed, 4243), checkEvery: 5, reachableOnly: false, out int hollow);
                Assert.True(err == null, $"{name} seed {seed} (any order): {err}");
                if (hollow > 0) hollowSeeds++;
            }

            requestedF += map.Forests;
            placedF += w.ResourcePlacement.Forests;
            requestedM += map.GoldMines;
            placedM += w.ResourcePlacement.Mines;
            trees += w.ResourcePlacement.Trees;
        }
        _out.WriteLine($"{name}: {seeds} seeds; forests {placedF}/{requestedF}, mines {placedM}/{requestedM}, trees {trees}; slowest world (gen + place) {worstMs} ms; of the first 5 seeds felled in any order, {hollowSeeds} left an open cell nobody can reach");
    }

    [Fact]
    public void HandMap_PlateauWithRamp_DenseForests_NeverSealTheRampOrThePlateau()
    {
        // A 40 x 40 map: a level-1 plateau with one 2-wide ramp going down to the west, 64 forests of 1-256 trees and 16 mines.
        var rows = new string[40];
        for (int y = 0; y < 40; y++)
        {
            var row = new char[40];
            for (int x = 0; x < 40; x++)
            {
                bool plateau = x >= 14 && x <= 30 && y >= 10 && y <= 28;
                bool ramp = x == 13 && (y == 18 || y == 19); // one cell long: its foot is (12, y), its mouth (14, y)
                row[x] = ramp ? 'r' : plateau ? '1' : '0';
            }
            rows[y] = new string(row);
        }
        Heightmap hm = ResourceMaps.FromRows(rows);
        int placedTrees = 0;
        for (ulong seed = 1; seed <= 60; seed++)
        {
            MapGenParams p = MapGenParams.Default with { Forests = 64, ForestMinTrees = 1, ForestMaxTrees = 256, GoldMines = 16, MineSpacing = 4f };
            var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 1, UnitCapacity: 4, CommandCapacity: 8) with { Map = p }, hm);
            var bare = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 1, UnitCapacity: 4, CommandCapacity: 8), hm);
            Assert.True(bare.World.NavGrid.LevelAt(20, 20) == 1 && bare.World.NavGrid.IsPassable(20, 20), "plateau must be reachable on the bare map");
            string? err = ResourceOracle.Check(sim.World, bare.World, p);
            Assert.True(err == null, $"seed {seed}: {err}");
            placedTrees += sim.World.ResourcePlacement.Trees;
        }
        _out.WriteLine($"hand plateau+ramp map: {placedTrees} trees over 60 seeds");
        Assert.True(placedTrees > 0);
    }

    /// <summary>docs/03 "Implementation (M3-1)": the worst setup (1024 map, 64 forests of up to 256, 64 mines) is about 1 s on top of the terrain.</summary>
    [Theory]
    [Trait("Category", "Perf")]
    [InlineData(1UL)]
    [InlineData(2UL)]
    public void WorstSetup_1024Map_AtTheCaps_UnderFiveSeconds(ulong seed)
    {
        MapGenParams bareMap = MapGenParams.Default with { Width = 1024, Height = 1024 };
        MapGenParams full = bareMap with { Forests = 64, ForestMinTrees = 1, ForestMaxTrees = 256, GoldMines = 64, MineSpacing = 0f };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        World bare = NewWorld(seed, bareMap);
        double bareMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        World w = NewWorld(seed, full);
        double fullMs = sw.Elapsed.TotalMilliseconds;
        _out.WriteLine($"1024 map seed {seed}: terrain {bareMs:F0} ms, terrain + caps placement {fullMs:F0} ms ({w.ResourcePlacement})");
        Assert.True(fullMs < 5000, $"{fullMs:F0} ms");
        string? err = ResourceOracle.Check(w, bare, full);
        Assert.True(err == null, err);
    }

    private static World NewWorld(ulong seed, MapGenParams map) =>
        new Simulation(TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: 4, CommandCapacity: 8) with { Map = map }).World;
}

/// <summary>QA's independent checks of a placed map against the same map without resources (M3-1).</summary>
public static class ResourceOracle
{
    /// <summary>Every placement rule, re-derived; null when all hold, else the first failure.</summary>
    public static string? Check(World w, World bare, MapGenParams p)
    {
        NavGrid g = w.NavGrid, g0 = bare.NavGrid;
        ResourceStore r = w.Resources;
        GameData data = w.Data;
        int W = g.Width, H = g.Height, n = W * H;
        if (w.Heightmap.ContentHash() != bare.Heightmap.ContentHash()) return "heightmap differs from the bare map";

        // Coverage from the store alone.
        var owner = new int[n];
        Array.Fill(owner, -1);
        int nodes = 0, mines = 0, treeCount = 0;
        var mineCenters = new List<(float X, float Y)>();
        for (int s = 0; s < r.Capacity; s++)
        {
            if (!r.Alive[s]) continue;
            nodes++;
            if (r.Generation[s] != 1) return $"slot {s} generation {r.Generation[s]} after placement";
            ResourceDef d = data.Resources[r.TypeId[s]];
            int ax = r.Cell[s] % W, ay = r.Cell[s] / W;
            int level = g0.LevelAt(ax, ay);
            for (int y = ay; y < ay + d.FootprintHeight; y++)
            {
                for (int x = ax; x < ax + d.FootprintWidth; x++)
                {
                    if (!g0.InBounds(x, y)) return $"slot {s} footprint off the map";
                    int i = y * W + x;
                    if (owner[i] >= 0) return $"slots {owner[i]} and {s} overlap at ({x}, {y})";
                    owner[i] = s;
                    if (g0.FlagsAt(x, y) != NavFlags.None) return $"slot {s} on ({x}, {y}) with bare flags {g0.FlagsAt(x, y)}";
                    if (g0.LevelAt(x, y) != level) return $"slot {s} spans levels";
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx <= 0 || ny <= 0 || nx >= W - 1 || ny >= H - 1) return $"slot {s} cell ({x}, {y}) touches the border";
                            if ((g0.FlagsAt(nx, ny) & (NavFlags.Cliff | NavFlags.Ramp | NavFlags.Blocked)) != 0)
                                return $"slot {s} cell ({x}, {y}) next to {g0.FlagsAt(nx, ny)} at ({nx}, {ny})";
                        }
                }
            }
            if (d.Resource == ResourceKind.Gold)
            {
                mines++;
                if (r.Remaining[s] != data.Rules.StartMineGold) return $"mine {s} holds {r.Remaining[s]}";
                mineCenters.Add(((ax + d.FootprintWidth / 2f) * MapConstants.CellSize, (ay + d.FootprintHeight / 2f) * MapConstants.CellSize));
            }
            else
            {
                treeCount++;
                if (r.Remaining[s] != data.Rules.TreeWood) return $"tree {s} holds {r.Remaining[s]}";
            }
        }
        if (nodes != r.Count) return $"Count {r.Count} but {nodes} alive";

        // Flags and costs: resource cells are exactly Blocked|Resource at blocked cost; every other cell as on the bare map.
        int passable = 0;
        for (int i = 0; i < n; i++)
        {
            int x = i % W, y = i / W;
            NavFlags f = g.FlagsAt(x, y);
            if (owner[i] >= 0)
            {
                if (f != (NavFlags.Blocked | NavFlags.Resource)) return $"node cell ({x}, {y}) flags {f}";
                if (g.CostAt(x, y) != MapConstants.CostBlocked) return $"node cell ({x}, {y}) cost {g.CostAt(x, y)}";
            }
            else
            {
                if (f != g0.FlagsAt(x, y)) return $"cell ({x}, {y}) flags {f} vs bare {g0.FlagsAt(x, y)}";
                if (g.CostAt(x, y) != g0.CostAt(x, y)) return $"cell ({x}, {y}) cost changed";
            }
            if ((f & NavFlags.Blocked) == 0) passable++;
        }
        if (passable != g.PassableCount) return $"PassableCount {g.PassableCount} but {passable} open cells";

        string? reach = Reach(g);
        if (reach != null) return reach;

        // Spacing; separation between nodes: no cell of one node 8-touches another node's cell.
        for (int a = 0; a < mineCenters.Count; a++)
            for (int b = a + 1; b < mineCenters.Count; b++)
            {
                float dx = mineCenters[a].X - mineCenters[b].X, dy = mineCenters[a].Y - mineCenters[b].Y;
                if (dx * dx + dy * dy < p.MineSpacing * p.MineSpacing) return $"mines {a} and {b} closer than {p.MineSpacing} m";
            }
        for (int i = 0; i < n; i++)
        {
            if (owner[i] < 0) continue;
            bool isTree = data.Resources[r.TypeId[owner[i]]].Resource == ResourceKind.Wood;
            int x = i % W, y = i / W;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int j = (y + dy) * W + x + dx;
                    if (owner[j] < 0 || owner[j] == owner[i]) continue;
                    bool otherTree = data.Resources[r.TypeId[owner[j]]].Resource == ResourceKind.Wood;
                    if (!(isTree && otherTree)) return $"node {owner[i]} touches node {owner[j]} at ({x}, {y})";
                }
        }

        // Forests: 8-connected tree groups, each in range; count and total match the report.
        var group = new int[n];
        Array.Fill(group, -1);
        var queue = new int[n];
        int groups = 0, total = 0;
        for (int i = 0; i < n; i++)
        {
            if (owner[i] < 0 || group[i] >= 0 || data.Resources[r.TypeId[owner[i]]].Resource != ResourceKind.Wood) continue;
            int head = 0, tail = 0;
            queue[tail++] = i;
            group[i] = groups;
            while (head < tail)
            {
                int c = queue[head++];
                int cx = c % W, cy = c / W;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int j = (cy + dy) * W + cx + dx;
                        if (owner[j] < 0 || group[j] >= 0 || data.Resources[r.TypeId[owner[j]]].Resource != ResourceKind.Wood) continue;
                        group[j] = groups;
                        queue[tail++] = j;
                    }
            }
            if (tail < p.ForestMinTrees || tail > p.ForestMaxTrees) return $"forest of {tail} trees outside {p.ForestMinTrees}-{p.ForestMaxTrees} (partial forest?)";
            total += tail;
            groups++;
        }
        ResourcePlacement placed = w.ResourcePlacement;
        if (placed.Forests != groups || placed.Trees != total || placed.Mines != mines || total != treeCount)
            return $"report {placed} but found {groups} forests, {total} trees, {mines} mines";
        if (placed.Forests > p.Forests || placed.Mines > p.GoldMines) return "placed more than requested";
        if (g.Version != nodes) return $"Version {g.Version} with {nodes} nodes";

        // Only the map stream moved.
        if (w.Rng(RngStream.Combat).State != bare.Rng(RngStream.Combat).State) return "combat stream moved";
        for (int pl = 0; pl < w.Config.PlayerCount; pl++)
            if (w.Rng(RngStream.Ai(pl)).State != bare.Rng(RngStream.Ai(pl)).State) return $"AI stream {pl} moved";
        return null;
    }

    /// <summary>4-connected flood from the first passable cell must reach every passable cell.</summary>
    public static string? Reach(NavGrid g)
    {
        int W = g.Width, n = W * g.Height, start = -1, open = 0;
        for (int i = 0; i < n; i++)
        {
            if (!g.IsPassable(i % W, i / W)) continue;
            open++;
            if (start < 0) start = i;
        }
        if (start < 0) return null;
        var seen = new bool[n];
        var q = new int[n];
        int h = 0, t = 0;
        q[t++] = start;
        seen[start] = true;
        while (h < t)
        {
            int c = q[h++];
            int x = c % W, y = c / W;
            foreach ((int nx, int ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
            {
                if (!g.IsPassable(nx, ny)) continue;
                int j = ny * W + nx;
                if (seen[j]) continue;
                seen[j] = true;
                q[t++] = j;
            }
        }
        return t == open ? null : $"pocket: flood reaches {t} of {open} passable cells";
    }

    /// <summary>
    /// Takes every node to 0 with random bites, in a seeded order: with <paramref name="reachableOnly"/>, only nodes with a
    /// footprint cell 4-adjacent to a cell reachable from the main region (as a gatherer standing next to it), asserting
    /// reach every <paramref name="checkEvery"/> fells; otherwise any node, counting fells after which some open cell was
    /// unreachable in <paramref name="hollowFells"/>. Either way the grid must end equal to the bare one.
    /// </summary>
    public static string? FellAll(World w, World bare, SimRng rng, int checkEvery, bool reachableOnly, out int hollowFells)
    {
        hollowFells = 0;
        ResourceStore r = w.Resources;
        NavGrid g = w.NavGrid, g0 = bare.NavGrid;
        GameData data = w.Data;
        int W = g.Width;
        var live = new List<int>();
        for (int s = 0; s < r.Capacity; s++) if (r.Alive[s]) live.Add(s);
        int startVersion = g.Version, nodes = live.Count, step = 0;
        bool[] reach = ReachSet(g);
        while (live.Count > 0)
        {
            int k = rng.NextInt(0, live.Count);
            if (reachableOnly)
            {
                int tries = 0;
                while (!Touches(r, data, W, reach, live[k]))
                {
                    k = (k + 1) % live.Count;
                    if (++tries > live.Count) return $"{live.Count} nodes left, none next to reachable ground";
                }
            }
            int s = live[k];
            EntityHandle h = r.HandleOf(s);
            int left = r.Remaining[s];
            int bite = rng.NextInt(1, Math.Max(2, left / 2 + 1));
            int got = r.Take(h, bite);
            if (got != Math.Min(bite, left)) return $"Take({bite}) of {left} returned {got}";
            if (r.IsAlive(h) == (got == left)) return "alive state wrong after Take";
            if (got != left) continue;
            live[k] = live[^1];
            live.RemoveAt(live.Count - 1);
            if (r.Take(h, 1) != 0) return "stale handle still takes";
            step++;
            if (reachableOnly)
            {
                Grow(g, reach);
                if (step % checkEvery == 0)
                {
                    string? bad = Reach(g);
                    if (bad != null) return $"after felling {step} reachable nodes: {bad}";
                }
            }
            else if (step % checkEvery == 0 && Reach(g) != null)
                hollowFells++;
        }
        if (g.Version != startVersion + nodes) return $"Version {g.Version}, expected {startVersion + nodes}";
        if (g.PassableCount != g0.PassableCount) return $"PassableCount {g.PassableCount} vs bare {g0.PassableCount}";
        for (int y = 0; y < g.Height; y++)
            for (int x = 0; x < g.Width; x++)
                if (g.FlagsAt(x, y) != g0.FlagsAt(x, y) || g.CostAt(x, y) != g0.CostAt(x, y)) return $"cell ({x}, {y}) not restored";
        if (r.Count != 0) return $"Count {r.Count} after felling all";
        return Reach(g);
    }

    // Adds to the reach set every passable cell 4-connected to it (cells only ever open, so the set only grows).
    private static void Grow(NavGrid g, bool[] reach)
    {
        int W = g.Width, n = W * g.Height, t = 0;
        var q = new int[n];
        for (int i = 0; i < n; i++)
        {
            if (reach[i] || !g.IsPassable(i % W, i / W)) continue;
            if (reach[i - 1] || reach[i + 1] || reach[i - W] || reach[i + W]) { reach[i] = true; q[t++] = i; }
        }
        int h = 0;
        while (h < t)
        {
            int c = q[h++];
            foreach (int j in new[] { c - 1, c + 1, c - W, c + W })
            {
                if (reach[j] || !g.IsPassable(j % W, j / W)) continue;
                reach[j] = true;
                q[t++] = j;
            }
        }
    }

    private static bool Touches(ResourceStore r, GameData data, int W, bool[] reach, int slot)
    {
        ResourceDef d = data.Resources[r.TypeId[slot]];
        int ax = r.Cell[slot] % W, ay = r.Cell[slot] / W;
        for (int y = ay; y < ay + d.FootprintHeight; y++)
            for (int x = ax; x < ax + d.FootprintWidth; x++)
                foreach (int j in new[] { y * W + x - 1, y * W + x + 1, (y - 1) * W + x, (y + 1) * W + x })
                    if (reach[j]) return true;
        return false;
    }

    /// <summary>Cells 4-connected to the largest passable region's first cell (the main region).</summary>
    public static bool[] ReachSet(NavGrid g)
    {
        int W = g.Width, n = W * g.Height;
        var seen = new bool[n];
        // The main region: the one holding the most cells, found by flooding each unseen passable cell.
        var label = new int[n];
        Array.Fill(label, -1);
        var q = new int[n];
        int best = -1, bestSize = 0, regions = 0;
        for (int start = 0; start < n; start++)
        {
            if (label[start] >= 0 || !g.IsPassable(start % W, start / W)) continue;
            int h = 0, t = 0;
            q[t++] = start;
            label[start] = regions;
            while (h < t)
            {
                int c = q[h++];
                int x = c % W, y = c / W;
                foreach ((int nx, int ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
                {
                    if (!g.IsPassable(nx, ny)) continue;
                    int j = ny * W + nx;
                    if (label[j] >= 0) continue;
                    label[j] = regions;
                    q[t++] = j;
                }
            }
            if (t > bestSize) { bestSize = t; best = regions; }
            regions++;
        }
        for (int i = 0; i < n; i++) seen[i] = label[i] == best && best >= 0;
        return seen;
    }
}
