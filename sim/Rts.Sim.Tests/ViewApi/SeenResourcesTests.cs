using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M4-VH2 (BUG-0281 item 1): <see cref="SeenResources"/>, the resource nodes as the player last saw them: a fell out of sight keeps the last-seen tree until its footprint is seen; the props relist only when that copy changes.</summary>
[Collection(SerialCollection.Name)]
public class SeenResourcesTests
{
    private readonly ITestOutputHelper _out;

    public SeenResourcesTests(ITestOutputHelper output) => _out = output;

    private static bool Update(SeenResources seen, World w, bool fogEnabled = true) =>
        seen.Update(w.NavGrid.Version, w.Resources.Alive, w.Resources.TypeId, w.Resources.Cell, w.Fog, 0, fogEnabled, w.NavGrid.Width);

    // A 48 x 48 flat map, explored by player 0: a tree beside a spotter (in sight), a tree and a 2 x 2 mine far away (explored, not visible).
    private static (Simulation Sim, EntityHandle Near, EntityHandle Far, EntityHandle Mine) Stage()
    {
        Simulation sim = TestSim.Explored(ResourceMaps.NewSim(ResourceMaps.Flat(48, 48)));
        World w = sim.World;
        EntityHandle near = ResourceMaps.Spawn(w, ResourceMaps.Tree, 5, 5, 50);
        EntityHandle far = ResourceMaps.Spawn(w, ResourceMaps.Tree, 40, 40, 50);
        EntityHandle mine = ResourceMaps.Spawn(w, ResourceMaps.Mine, 40, 5, 500);
        Spotter(sim, 0, w.NavGrid.CellCenter(6, 5));
        Assert.True(w.Fog.IsVisible(0, 5 * 48 + 5));
        Assert.False(w.Fog.IsVisible(0, 40 * 48 + 40) || w.Fog.IsVisible(0, 5 * 48 + 40));
        return (sim, near, far, mine);
    }

    [Fact]
    public void AFellOutOfSight_KeepsTheLastSeenTree_UntilItsFootprintIsSeen_AFellInSightGoesAtOnce()
    {
        (Simulation sim, EntityHandle near, EntityHandle far, _) = Stage();
        World w = sim.World;
        var seen = new SeenResources(w.Data.Resources, w.Resources.Capacity);
        Assert.Equal(-1, seen.Version);
        Assert.True(Update(seen, w));
        Assert.Equal(0, seen.Version);
        Assert.True(seen.Alive[near.Index] && seen.Alive[far.Index]);

        Assert.Equal(50, w.Resources.Take(far, 50)); // felled out of sight
        Assert.False(w.Resources.Alive[far.Index]);
        Assert.False(Update(seen, w));
        Assert.True(seen.Alive[far.Index]);
        Assert.Equal(1, seen.PendingCount);
        Assert.Equal((ResourceMaps.Tree, 40 * 48 + 40), (seen.TypeId[far.Index], seen.Cell[far.Index]));
        Assert.False(Update(seen, w)); // steady: still pending, nothing moves
        Assert.Equal(0, seen.Version);

        Assert.Equal(50, w.Resources.Take(near, 50)); // felled in sight
        Assert.True(Update(seen, w));
        Assert.Equal(1, seen.Version);
        Assert.False(seen.Alive[near.Index]);
        Assert.True(seen.Alive[far.Index]);

        Spotter(sim, 0, w.NavGrid.CellCenter(40, 41)); // the far stump comes into sight
        Assert.True(Update(seen, w));
        Assert.Equal(2, seen.Version);
        Assert.False(seen.Alive[far.Index]);
        Assert.Equal(0, seen.PendingCount);
    }

    [Fact]
    public void WithoutFog_TheCopyFollowsTheStoreAtOnce_ABuildingChangeMovesNothing_PropsRelistOnlyOnTheCopysVersion()
    {
        (Simulation sim, _, _, EntityHandle mine) = Stage();
        World w = sim.World;
        var seen = new SeenResources(w.Data.Resources, w.Resources.Capacity);
        Update(seen, w, fogEnabled: false);
        PropLayout layout = PropLayoutTests.Layout(w);
        Assert.True(layout.Refresh(w.Heightmap, w.NavGrid.Width, seen.Version, seen.Alive, seen.TypeId, seen.Cell));
        int trees = layout.CountOf(ResourceMaps.Tree), mines = layout.CountOf(ResourceMaps.Mine);
        Assert.Equal((2, 1), (trees, mines));

        // A building change bumps the store key (NavGrid.Version) but no node changed: no new version, no relist (BUG-0126 item 3).
        Assert.False(seen.Update(w.NavGrid.Version + 7, w.Resources.Alive, w.Resources.TypeId, w.Resources.Cell, w.Fog, 0, true, w.NavGrid.Width));
        Assert.False(layout.Refresh(w.Heightmap, w.NavGrid.Width, seen.Version, seen.Alive, seen.TypeId, seen.Cell));

        // The mine (out of sight) mined out: with the fog on it stays; with --no-fog it goes at once.
        Assert.Equal(500, w.Resources.Take(mine, 500));
        Assert.False(Update(seen, w));
        Assert.False(layout.Refresh(w.Heightmap, w.NavGrid.Width, seen.Version, seen.Alive, seen.TypeId, seen.Cell));
        Assert.Equal(1, layout.CountOf(ResourceMaps.Mine));
        Assert.True(Update(seen, w, fogEnabled: false));
        Assert.True(layout.Refresh(w.Heightmap, w.NavGrid.Width, seen.Version, seen.Alive, seen.TypeId, seen.Cell));
        Assert.Equal(0, layout.CountOf(ResourceMaps.Mine));
        Assert.Equal(trees, layout.CountOf(ResourceMaps.Tree));
    }

    [Fact]
    public void AReusedSlot_FollowsWhenEitherFootprintIsSeen_AndUpdatesAllocateNothing()
    {
        (Simulation sim, _, EntityHandle far, _) = Stage();
        World w = sim.World;
        var seen = new SeenResources(w.Data.Resources, w.Resources.Capacity);
        Update(seen, w);
        w.Resources.Take(far, 50);
        // A new tree in the freed slot, in sight of the spotter: the slot's new node is seen, so the copy takes it.
        EntityHandle again = ResourceMaps.Spawn(w, ResourceMaps.Tree, 7, 6, 50);
        Assert.Equal(far.Index, again.Index);
        Assert.True(Update(seen, w));
        Assert.True(seen.Alive[again.Index]);
        Assert.Equal(6 * 48 + 7, seen.Cell[again.Index]);

        long before = GC.GetAllocatedBytesForCurrentThread();
        int changes = 0;
        for (int i = 0; i < 1000; i++) changes += Update(seen, w) ? 1 : 0;
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(0, changes);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SeenResources(w.Data.Resources, -1));
    }
}
