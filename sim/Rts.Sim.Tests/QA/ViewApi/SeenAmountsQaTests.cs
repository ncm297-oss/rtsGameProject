using System.Numerics;

using Rts.Sim.Entities;

using Rts.Sim.ViewApi;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>
/// QA M6-V8 (2026-10-15-2325): <see cref="SeenResources.UpdateAmounts"/> / <see cref="SeenResources.InSight"/> across the fog
/// edge cases the dev rows don't stage: a node seen by a tower only (no unit), a node felled on the very tick its last
/// spotter leaves, two nodes on one cell row split by the sight edge, and a 2 x 2 mine straddling that edge.
/// </summary>
[Collection(SerialCollection.Name)]
public class SeenAmountsQaTests
{
    private readonly ITestOutputHelper _out;

    public SeenAmountsQaTests(ITestOutputHelper output) => _out = output;

    private static void Refresh(SeenResources seen, World w)
    {
        seen.Update(w.NavGrid.Version, w.Resources.Alive, w.Resources.TypeId, w.Resources.Cell, w.Fog, 0, true, w.NavGrid.Width);
        seen.UpdateAmounts(w.Resources.Alive, w.Resources.TypeId, w.Resources.Cell, w.Resources.Remaining, w.Fog, 0, true, w.NavGrid.Width);
    }

    private static bool InSight(SeenResources seen, World w, EntityHandle h) => seen.InSight(h.Index, w.Fog, 0, true, w.NavGrid.Width);

    // Moves a unit straight to a point (no tick) and recomputes the fog, as the next tick's fog pass would.
    private static void Teleport(World w, EntityHandle unit, Vector2 to)
    {
        w.Units.Position[unit.Index] = to;
        w.Units.PrevPosition[unit.Index] = to;
        w.Fog.Update();
    }

    [Fact]
    public void ANodeSeenByATowerOnly_FollowsTheStore_AndKeepsTheLastAmountOnceTheTowerFalls()
    {
        Simulation sim = TestSim.Explored(ResourceMaps.NewSim(ResourceMaps.Flat(64, 64)));
        World w = sim.World;
        EntityHandle mine = ResourceMaps.Spawn(w, ResourceMaps.Mine, 50, 10, 500);
        var seen = new SeenResources(w.Data.Resources, w.Resources.Capacity);
        Refresh(seen, w);
        Assert.False(InSight(seen, w, mine));

        EntityHandle tower = TowerTests.PlaceBuilding(sim, 0, TowerTests.Watchtower, 44, 10);
        w.Fog.Update();
        Assert.True(w.Fog.IsVisible(0, 10 * 64 + 50), "the Watchtower does not see the mine: restage");
        Assert.Equal(100, w.Resources.Take(mine, 100));
        Refresh(seen, w);
        Assert.True(InSight(seen, w, mine));
        Assert.Equal(400, seen.Amount[mine.Index]);

        w.Buildings.Damage(tower, 1_000_000);
        Assert.False(w.Buildings.Alive[tower.Index]);
        w.Fog.Update();
        Assert.False(w.Fog.IsVisible(0, 10 * 64 + 50));
        Assert.Equal(100, w.Resources.Take(mine, 100));
        Refresh(seen, w);
        Assert.False(InSight(seen, w, mine));
        Assert.True(seen.Alive[mine.Index]);
        Assert.Equal(400, seen.Amount[mine.Index]);
    }

    [Fact]
    public void ATreeFelledOnTheTickItsSpotterLeaves_StaysWithTheAmountLastSeen_UntilSeenAgain()
    {
        Simulation sim = TestSim.Explored(ResourceMaps.NewSim(ResourceMaps.Flat(48, 48)));
        World w = sim.World;
        EntityHandle tree = ResourceMaps.Spawn(w, ResourceMaps.Tree, 10, 10, 50);
        EntityHandle spotter = Spotter(sim, 0, w.NavGrid.CellCenter(11, 10));
        var seen = new SeenResources(w.Data.Resources, w.Resources.Capacity);
        Refresh(seen, w);
        Assert.Equal(20, w.Resources.Take(tree, 20));
        Refresh(seen, w);
        Assert.Equal(30, seen.Amount[tree.Index]);

        // One tick: the tree felled and its only spotter gone out of range, before the view looks again.
        Assert.Equal(30, w.Resources.Take(tree, 30));
        Assert.False(w.Resources.Alive[tree.Index]);
        Teleport(w, spotter, w.NavGrid.CellCenter(40, 40));
        Assert.False(w.Fog.IsVisible(0, 10 * 48 + 10));
        Refresh(seen, w);
        Assert.True(seen.Alive[tree.Index], "the copy dropped a tree the player never saw felled");
        Assert.Equal(30, seen.Amount[tree.Index]);
        Assert.False(InSight(seen, w, tree));

        Teleport(w, spotter, w.NavGrid.CellCenter(11, 10));
        Refresh(seen, w);
        Assert.False(seen.Alive[tree.Index]);
        Assert.False(InSight(seen, w, tree));
    }

    [Fact]
    public void TwoTreesOnOneRowSplitByTheSightEdge_AndAMineStraddlingIt_EachFollowOnlyWhatIsSeen()
    {
        Simulation sim = TestSim.Explored(ResourceMaps.NewSim(ResourceMaps.Flat(64, 64)));
        World w = sim.World;
        Spotter(sim, 0, w.NavGrid.CellCenter(10, 20));
        int edge = FirstUnseen(w, 20), mineEdge = FirstUnseen(w, 22);
        Assert.InRange(edge, 12, 40);
        EntityHandle a = ResourceMaps.Spawn(w, ResourceMaps.Tree, edge - 1, 20, 50);
        EntityHandle b = ResourceMaps.Spawn(w, ResourceMaps.Tree, edge, 20, 50);
        EntityHandle mine = ResourceMaps.Spawn(w, ResourceMaps.Mine, mineEdge - 1, 22, 500);
        w.Fog.Update();
        Assert.True(w.Fog.IsVisible(0, 20 * 64 + edge - 1) && !w.Fog.IsVisible(0, 20 * 64 + edge), "the trees changed the sight edge: restage");
        var seen = new SeenResources(w.Data.Resources, w.Resources.Capacity);
        Refresh(seen, w);

        w.Resources.Take(a, 10);
        w.Resources.Take(b, 10);
        w.Resources.Take(mine, 77);
        Refresh(seen, w);
        Assert.Equal((40, 50, 423), (seen.Amount[a.Index], seen.Amount[b.Index], seen.Amount[mine.Index]));
        Assert.True(InSight(seen, w, a) && !InSight(seen, w, b) && InSight(seen, w, mine));
        _out.WriteLine($"edge x {edge} (row 20), {mineEdge} (row 22)");
    }

    private static int FirstUnseen(World w, int row)
    {
        for (int x = 10; x < w.NavGrid.Width; x++)
            if (!w.Fog.IsVisible(0, row * w.NavGrid.Width + x)) return x;
        return -1;
    }
}
