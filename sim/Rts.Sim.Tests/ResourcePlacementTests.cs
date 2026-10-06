using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;

namespace Rts.Sim.Tests;

/// <summary>M3-1 criterion 5: the resource placer's invariants on generated maps, and that it leaves the terrain and other RNG streams alone.</summary>
public class ResourcePlacementTests
{
    private static readonly MapGenParams WithResources = new() { Forests = 12, GoldMines = 8 };

    private static World NewWorld(ulong seed, MapGenParams map) =>
        new Simulation(TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: 8, CommandCapacity: 8) with { Map = map }).World;

    public static IEnumerable<object[]> Seeds() => Enumerable.Range(1, 20).Select(s => new object[] { (ulong)s });

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Placement_OnSeeds1To20_KeepsEveryInvariant(ulong seed)
    {
        World w = NewWorld(seed, WithResources);
        World bare = NewWorld(seed, MapGenParams.Default);
        NavGrid g = w.NavGrid, g0 = bare.NavGrid;
        ResourceStore r = w.Resources;
        GameData data = w.Data;
        int width = g.Width, n = width * g.Height;
        ResourcePlacement p = w.ResourcePlacement;

        // Terrain and pre-placement flags are those of the map without resources.
        Assert.Equal(bare.Heightmap.ContentHash(), w.Heightmap.ContentHash());
        for (int i = 0; i < n; i++)
        {
            int x = i % width, y = i / width;
            Assert.Equal(g0.LevelAt(x, y), g.LevelAt(x, y));
            NavFlags f = g.FlagsAt(x, y);
            if ((f & NavFlags.Resource) != 0) Assert.Equal(NavFlags.None, g0.FlagsAt(x, y));
            else Assert.Equal(g0.FlagsAt(x, y), f);
        }

        // Every footprint cell was open flat ground of one level with no cliff, ramp or border 8-neighbour; no overlaps.
        var cover = new int[n];
        int trees = 0, mines = 0, covered = 0;
        var mineCenters = new List<(float X, float Y)>();
        for (int s = 0; s < r.Capacity; s++)
        {
            if (!r.Alive[s]) continue;
            ResourceDef def = data.Resources[r.TypeId[s]];
            int ax = r.Cell[s] % width, ay = r.Cell[s] / width;
            int level = g0.LevelAt(ax, ay);
            for (int y = ay; y < ay + def.FootprintHeight; y++)
            {
                for (int x = ax; x < ax + def.FootprintWidth; x++)
                {
                    Assert.Equal(NavFlags.None, g0.FlagsAt(x, y));
                    Assert.Equal(level, g0.LevelAt(x, y));
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        Assert.True(nx > 0 && ny > 0 && nx < width - 1 && ny < g.Height - 1, $"seed {seed}: ({x}, {y}) touches the border");
                        Assert.Equal(NavFlags.None, g0.FlagsAt(nx, ny) & (NavFlags.Cliff | NavFlags.Ramp));
                    }
                    cover[y * width + x]++;
                    covered++;
                }
            }
            if (def.Resource == ResourceKind.Wood)
            {
                trees++;
                Assert.Equal(data.Rules.TreeWood, r.Remaining[s]);
            }
            else
            {
                mines++;
                Assert.Equal(data.Rules.StartMineGold, r.Remaining[s]);
                mineCenters.Add(((ax + def.FootprintWidth / 2f) * MapConstants.CellSize, (ay + def.FootprintHeight / 2f) * MapConstants.CellSize));
            }
        }
        Assert.All(cover, c => Assert.True(c <= 1, $"seed {seed}: overlapping nodes"));
        Assert.Equal(covered, cover.Count(c => c == 1));
        Assert.Equal(covered, Enumerable.Range(0, n).Count(i => (g.FlagsAt(i % width, i / width) & NavFlags.Resource) != 0));
        Assert.Equal((p.Trees, p.Mines), (trees, mines));

        // Every passable cell still reaches every other.
        Assert.Equal(g.PassableCount, FloodReach(g));
        Assert.Equal(g0.PassableCount - covered, g.PassableCount);

        // Mines at least MineSpacing apart; forests are separate 8-connected groups of in-range size.
        for (int a = 0; a < mineCenters.Count; a++)
            for (int b = a + 1; b < mineCenters.Count; b++)
            {
                float dx = mineCenters[a].X - mineCenters[b].X, dy = mineCenters[a].Y - mineCenters[b].Y;
                Assert.True(MathF.Sqrt(dx * dx + dy * dy) >= WithResources.MineSpacing, $"seed {seed}: mines {a} and {b} too close");
            }
        List<int> forests = TreeGroups(w);
        Assert.Equal(p.Forests, forests.Count);
        Assert.All(forests, size => Assert.InRange(size, WithResources.ForestMinTrees, WithResources.ForestMaxTrees));

        // The design map has room: every request is met on these seeds (a short count is allowed in general).
        Assert.Equal(WithResources.GoldMines, p.Mines);
        Assert.Equal(WithResources.Forests, p.Forests);
        Assert.Equal(p.Trees + p.Mines, r.Count);
        Assert.Equal(r.Count, g.Version); // one bump per node placed

        // The placer draws only from the map stream.
        Assert.Equal(bare.Rng(RngStream.Combat).State, w.Rng(RngStream.Combat).State);
        for (int pl = 0; pl < 2; pl++) Assert.Equal(bare.Rng(RngStream.Ai(pl)).State, w.Rng(RngStream.Ai(pl)).State);
        Assert.NotEqual(bare.Rng(RngStream.MapGen).State, w.Rng(RngStream.MapGen).State);
    }

    [Fact]
    public void Placement_IsIdenticalOnASecondWorld()
    {
        for (ulong seed = 1; seed <= 3; seed++)
        {
            World a = NewWorld(seed, WithResources), b = NewWorld(seed, WithResources);
            Assert.Equal(a.ResourcePlacement, b.ResourcePlacement);
            Assert.True(a.Resources.Alive.SequenceEqual(b.Resources.Alive));
            Assert.True(a.Resources.TypeId.SequenceEqual(b.Resources.TypeId));
            Assert.True(a.Resources.Cell.SequenceEqual(b.Resources.Cell));
            Assert.True(a.Resources.Remaining.SequenceEqual(b.Resources.Remaining));
            Assert.Equal(a.NavGrid.Version, b.NavGrid.Version);
            Assert.Equal(a.Rng(RngStream.MapGen).State, b.Rng(RngStream.MapGen).State);
            Assert.Equal(new Simulation(a.Config).StateHash(), new Simulation(b.Config).StateHash());
        }
        Assert.NotEqual(NewWorld(1, WithResources).Resources.Cell.ToArray(), NewWorld(2, WithResources).Resources.Cell.ToArray());
    }

    [Fact]
    public void Defaults_PlaceNothing_AndDrawNothing()
    {
        Assert.Equal(0, MapGenParams.Default.Forests);
        Assert.Equal(0, MapGenParams.Default.GoldMines);
        World w = NewWorld(7, MapGenParams.Default);
        Assert.Equal(default, w.ResourcePlacement);
        Assert.Equal(0, w.Resources.Count);
        Assert.Equal(0, w.NavGrid.Version);
        var rng = new SimRng(7, RngStream.MapGen);
        MapGenerator.Generate(MapGenParams.Default, ref rng); // the terrain's draws, nothing after
        Assert.Equal(rng.State, w.Rng(RngStream.MapGen).State);
    }

    [Fact]
    public void TreeSlots_FollowCellOrderWithinEachForest_AndMinesComeFirst()
    {
        World w = NewWorld(4, WithResources);
        ResourceStore r = w.Resources;
        int mines = w.ResourcePlacement.Mines;
        for (int s = 0; s < mines; s++) Assert.Equal(ResourceKind.Gold, w.Data.Resources[r.TypeId[s]].Resource);
        for (int s = mines; s < r.Count; s++) Assert.Equal(ResourceKind.Wood, w.Data.Resources[r.TypeId[s]].Resource);
    }

    [Fact]
    public void OneForestOnly_ShortCountIsReported_WhenTheStoreIsTooSmall()
    {
        var map = new MapGenParams { Forests = 3, ForestMinTrees = 10, ForestMaxTrees = 10 };
        World w = new Simulation(TestSim.Config(Seed: 1, PlayerCount: 1, UnitCapacity: 4, CommandCapacity: 4) with { Map = map, ResourceCapacity = 25 }).World;
        Assert.Equal(new ResourcePlacement(2, 20, 0), w.ResourcePlacement);
        Assert.Equal(20, w.Resources.Count);
    }

    [Fact]
    public void ConnectivityRule_RejectsARingThatSealsCells_AndAcceptsALine()
    {
        NavGrid g = new Simulation(TestSim.Config(Seed: 1, PlayerCount: 1, UnitCapacity: 4, CommandCapacity: 4), ResourceMaps.Flat(12, 12)).World.NavGrid;
        int W = g.Width;
        int[] ring = { 4 * W + 4, 4 * W + 5, 4 * W + 6, 5 * W + 4, 5 * W + 6, 6 * W + 4, 6 * W + 5, 6 * W + 6 };
        Assert.False(ResourcePlacer.StaysConnected(g, ring)); // (5, 5) would be sealed inside
        int[] line = { 2 * W + 5, 3 * W + 5, 4 * W + 5, 5 * W + 5, 6 * W + 5, 7 * W + 5 };
        Assert.True(ResourcePlacer.StaysConnected(g, line));
        int[] wall = Enumerable.Range(1, 10).Select(y => y * W + 5).ToArray();
        Assert.False(ResourcePlacer.StaysConnected(g, wall)); // splits the map in two
    }

    [Fact]
    public void MapGenParams_ResourceFields_AreValidated()
    {
        foreach (MapGenParams bad in new[]
        {
            new MapGenParams { Forests = -1 },
            new MapGenParams { Forests = MapGenParams.MaxResourceGroups + 1 },
            new MapGenParams { GoldMines = -1 },
            new MapGenParams { GoldMines = MapGenParams.MaxResourceGroups + 1 },
            new MapGenParams { ForestMinTrees = 0 },
            new MapGenParams { ForestMinTrees = 20, ForestMaxTrees = 19 },
            new MapGenParams { ForestMaxTrees = MapGenParams.MaxForestTrees + 1 },
            new MapGenParams { MineSpacing = -1f },
            new MapGenParams { MineSpacing = float.NaN },
            new MapGenParams { MineSpacing = float.PositiveInfinity },
        })
        {
            Assert.Throws<ArgumentOutOfRangeException>(bad.Validate);
        }
        new MapGenParams { Forests = MapGenParams.MaxResourceGroups, GoldMines = MapGenParams.MaxResourceGroups, MineSpacing = 0f }.Validate();
    }

    /// <summary>Passable cells 4-connected to the first passable cell.</summary>
    private static int FloodReach(NavGrid g)
    {
        int w = g.Width, n = w * g.Height;
        var seen = new bool[n];
        var queue = new Queue<int>();
        for (int i = 0; i < n; i++)
        {
            if (!g.IsPassable(i % w, i / w)) continue;
            seen[i] = true;
            queue.Enqueue(i);
            break;
        }
        int reached = 0;
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            reached++;
            foreach (int j in new[] { i - 1, i + 1, i - w, i + w })
            {
                if (j < 0 || j >= n || seen[j] || !g.IsPassable(j % w, j / w)) continue;
                seen[j] = true;
                queue.Enqueue(j);
            }
        }
        return reached;
    }

    /// <summary>Sizes of the 8-connected groups of tree cells.</summary>
    private static List<int> TreeGroups(World world)
    {
        NavGrid g = world.NavGrid;
        ResourceStore r = world.Resources;
        int w = g.Width, n = w * g.Height;
        var tree = new bool[n];
        for (int s = 0; s < r.Capacity; s++)
            if (r.Alive[s] && world.Data.Resources[r.TypeId[s]].Resource == ResourceKind.Wood) tree[r.Cell[s]] = true;
        var seen = new bool[n];
        var sizes = new List<int>();
        for (int start = 0; start < n; start++)
        {
            if (!tree[start] || seen[start]) continue;
            int size = 0;
            var stack = new Stack<int>();
            stack.Push(start);
            seen[start] = true;
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                size++;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = i % w + dx, y = i / w + dy;
                    if (!g.InBounds(x, y)) continue;
                    int j = y * w + x;
                    if (tree[j] && !seen[j])
                    {
                        seen[j] = true;
                        stack.Push(j);
                    }
                }
            }
            sizes.Add(size);
        }
        return sizes;
    }
}
