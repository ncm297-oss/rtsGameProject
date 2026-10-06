using Rts.Sim.Entities;
using Rts.Sim.Map;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests;

/// <summary>M3-1: <see cref="ResourceStore"/> slots, handles, placement refusals and <see cref="ResourceStore.Take"/>.</summary>
public class ResourceStoreTests
{
    // 10 x 8: a level-1 plateau at x 2-5, y 2-4 (rim cells are cliffs) with a ramp at (3, 5).
    private static readonly string[] Rows =
    {
        "0000000000",
        "0000000000",
        "0011110000",
        "0011110000",
        "0011110000",
        "000r000000",
        "0000000000",
        "0000000000",
    };

    private static World NewWorld(int capacity = ResourceStore.DefaultCapacity) => NewSim(FromRows(Rows), resourceCapacity: capacity).World;

    [Fact]
    public void Spawn_TakesSlotsInOrder_BlocksTheFootprint_AndBumpsVersionOncePerNode()
    {
        World w = NewWorld();
        ResourceStore r = w.Resources;
        NavGrid g = w.NavGrid;
        Assert.Equal(0, r.Count);
        Assert.Equal(0, g.Version);
        int passable = g.PassableCount;

        EntityHandle tree = Spawn(w, Tree, 7, 1, TreeWood);
        EntityHandle mine = Spawn(w, Mine, 7, 5, 2500);
        Assert.Equal(new EntityHandle(0, 1), tree);
        Assert.Equal(new EntityHandle(1, 1), mine);
        Assert.Equal(2, r.Count);
        Assert.Equal(2, g.Version); // one bump per node, not per cell
        Assert.Equal(passable - 5, g.PassableCount);

        Assert.True(r.IsAlive(tree) && r.IsAlive(mine));
        Assert.Equal(Tree, r.TypeId[0]);
        Assert.Equal(1 * g.Width + 7, r.Cell[0]);
        Assert.Equal(TreeWood, r.Remaining[0]);
        Assert.Equal(5 * g.Width + 7, r.Cell[1]); // anchor: lowest x, y of the footprint
        Assert.Equal(2500, r.Remaining[1]);
        foreach ((int x, int y) in new[] { (7, 1), (7, 5), (8, 5), (7, 6), (8, 6) })
        {
            Assert.Equal(NavFlags.Blocked | NavFlags.Resource, g.FlagsAt(x, y));
            Assert.False(g.IsPassable(x, y));
            Assert.Equal(MapConstants.CostBlocked, g.CostAt(x, y));
        }
        Assert.Equal(NavFlags.None, g.FlagsAt(6, 5)); // next to the mine, untouched
    }

    [Fact]
    public void Spawn_OnRampCliffBorderOccupiedOrOffMapCells_IsRefused_WithoutThrowingOrChangingAnything()
    {
        World w = NewWorld();
        ResourceStore r = w.Resources;
        NavGrid g = w.NavGrid;
        Spawn(w, Tree, 7, 1, TreeWood);
        Assert.Equal(NavFlags.Ramp, g.FlagsAt(3, 5) & NavFlags.Ramp);
        Assert.NotEqual(NavFlags.None, g.FlagsAt(2, 2) & NavFlags.Cliff);
        int version = g.Version, count = r.Count, passable = g.PassableCount;
        NavFlags[] flags = Flags(g);
        int W = g.Width;

        (int Type, int Cell, int Amount, string Why)[] refused =
        {
            (Tree, 5 * W + 3, TreeWood, "ramp"),
            (Tree, 2 * W + 2, TreeWood, "cliff"),
            (Tree, 0, TreeWood, "border corner"),
            (Tree, 3 * W + 9, TreeWood, "border edge"),
            (Tree, 1 * W + 7, TreeWood, "occupied by a tree"),
            (Mine, 0 * W + 6, 2500, "mine overlapping the tree"),
            (Mine, 6 * W + 1, 2500, "mine reaching the bottom border"),
            (Mine, 5 * W + 2, 2500, "mine on the ramp wall and ramp"),
            (Mine, 3 * W + 5, 2500, "mine on two levels (cliff)"),
            (Mine, 3 * W + 9, 2500, "mine past the right edge"),
            (Tree, -1, TreeWood, "negative cell"),
            (Tree, W * g.Height, TreeWood, "cell past the map"),
            (Tree, int.MaxValue, TreeWood, "huge cell"),
            (-1, 6 * W + 6, TreeWood, "negative type"),
            (TestSim.Data.Resources.Length, 6 * W + 6, TreeWood, "unknown type"),
            (Tree, 6 * W + 6, 0, "zero amount"),
            (Tree, 6 * W + 6, -5, "negative amount"),
        };
        foreach ((int type, int cell, int amount, string why) in refused)
        {
            Assert.False(r.Spawn(type, cell, amount, out EntityHandle h), why);
            Assert.Equal(default, h);
            Assert.False(type >= 0 && type < TestSim.Data.Resources.Length && amount > 0 && r.Fits(type, cell), why);
        }
        Assert.Equal(count, r.Count);
        Assert.Equal(version, g.Version);
        Assert.Equal(passable, g.PassableCount);
        Assert.Equal(flags, Flags(g));

        Assert.True(r.Fits(Tree, 6 * W + 6));
        Assert.True(r.Fits(Mine, 5 * W + 5)); // (5..6, 5..6): open level 0 beside the ramp wall
    }

    [Fact]
    public void Spawn_WhenFull_IsRefused_AndAFreedSlotIsReusedWithANewGeneration()
    {
        World w = NewWorld(capacity: 2);
        ResourceStore r = w.Resources;
        EntityHandle a = Spawn(w, Tree, 1, 6, TreeWood);
        Spawn(w, Tree, 2, 6, TreeWood);
        Assert.Equal(0, r.FreeCount);
        Assert.False(r.Spawn(Tree, 6 * w.NavGrid.Width + 6, TreeWood, out _));
        Assert.True(w.NavGrid.IsPassable(6, 6));
        Assert.Equal(2, w.NavGrid.Version);

        Assert.Equal(TreeWood, r.Take(a, int.MaxValue));
        Assert.False(r.IsAlive(a));
        EntityHandle c = Spawn(w, Tree, 6, 6, TreeWood);
        Assert.Equal(new EntityHandle(a.Index, a.Generation + 1), c);
        Assert.False(r.IsAlive(a)); // the old handle stays stale on the reused slot
        Assert.True(r.IsAlive(c));
    }

    [Fact]
    public void Take_Clamps_FreesAtZero_ReopensTheCells_AndBumpsVersionExactlyOnce()
    {
        World w = NewWorld();
        ResourceStore r = w.Resources;
        NavGrid g = w.NavGrid;
        int passable = g.PassableCount;
        EntityHandle mine = Spawn(w, Mine, 7, 5, 50);
        int v0 = g.Version;

        Assert.Equal(20, r.Take(mine, 20));
        Assert.Equal(30, r.Remaining[mine.Index]);
        Assert.True(r.IsAlive(mine));
        Assert.Equal(v0, g.Version); // taking without depleting changes no passability
        Assert.Equal(0, r.Take(mine, 0));
        Assert.Equal(0, r.Take(mine, -7));
        Assert.Equal(30, r.Remaining[mine.Index]);

        Assert.Equal(30, r.Take(mine, 1000)); // clamped to what is left
        Assert.False(r.IsAlive(mine));
        Assert.Equal(0, r.Count);
        Assert.Equal(v0 + 1, g.Version);
        Assert.Equal(passable, g.PassableCount);
        Assert.Equal(mine.Generation + 1, r.Generation[mine.Index]);
        Assert.False(r.Alive[mine.Index]);
        Assert.Equal(-1, r.Cell[mine.Index]);
        foreach ((int x, int y) in new[] { (7, 5), (8, 5), (7, 6), (8, 6) })
        {
            Assert.Equal(NavFlags.None, g.FlagsAt(x, y));
            Assert.True(g.IsPassable(x, y));
            Assert.Equal(MapConstants.CostPassable, g.CostAt(x, y));
        }
    }

    [Fact]
    public void Take_DeadStaleOrForeignHandles_ReturnZero_AndChangeNothing()
    {
        World w = NewWorld();
        ResourceStore r = w.Resources;
        EntityHandle tree = Spawn(w, Tree, 6, 6, 10);
        EntityHandle other = Spawn(w, Tree, 1, 6, 10);
        Assert.Equal(10, r.Take(tree, 10));
        int version = w.NavGrid.Version;

        foreach (EntityHandle h in new[]
        {
            tree,                                            // freed
            new EntityHandle(other.Index, other.Generation + 1), // wrong generation
            new EntityHandle(other.Index, 0),
            default,                                         // generation 0 never resolves
            new EntityHandle(-1, 1),
            new EntityHandle(r.Capacity, 1),
            new EntityHandle(int.MaxValue, 1),
        })
        {
            Assert.False(r.IsAlive(h));
            Assert.Equal(0, r.Take(h, 5));
        }
        Assert.Equal(10, r.Remaining[other.Index]);
        Assert.Equal(version, w.NavGrid.Version);
        Assert.Equal(1, r.Count);
    }

    [Fact]
    public void NewWorld_HasAnEmptyStoreOfTheConfiguredCapacity()
    {
        var sim = new Simulation(TestSim.Config(Seed: 1, PlayerCount: 1, UnitCapacity: 4, CommandCapacity: 4));
        ResourceStore r = sim.World.Resources;
        Assert.Equal(ResourceStore.DefaultCapacity, r.Capacity);
        Assert.Equal(4096, r.Capacity);
        Assert.Equal(0, r.Count);
        Assert.Equal(0, sim.World.NavGrid.Version);
        Assert.Equal(default, sim.World.ResourcePlacement);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Simulation(TestSim.Config(Seed: 1, PlayerCount: 1, UnitCapacity: 4, CommandCapacity: 4) with { ResourceCapacity = 0 }));
    }

    private static NavFlags[] Flags(NavGrid g)
    {
        var f = new NavFlags[g.Width * g.Height];
        for (int i = 0; i < f.Length; i++) f[i] = g.FlagsAt(i % g.Width, i / g.Width);
        return f;
    }
}
