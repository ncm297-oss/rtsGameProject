using Rts.Sim.Ai;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Vision;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-2: <see cref="PlayerView"/>'s resource-node memory (BUG-0540: a node felled out of the player's sight stays answered
/// until a cell of its footprint is visible again, the view's <c>SeenResources</c> rule) and its fog-safe placement rule
/// (<see cref="PlayerView.CanPlace"/>: own buildings, seen and remembered enemy buildings, explored ground, terrain, known
/// nodes; never a hidden enemy building the player didn't see).
/// </summary>
public class PlayerViewMemoryTests
{
    private const int Size = 64;

    private static int Tree(World w)
    {
        for (int r = 0; r < w.Data.Resources.Length; r++)
            if (w.Data.Resources[r].FootprintWidth == 1 && w.Data.Resources[r].FootprintHeight == 1) return r;
        throw new InvalidOperationException("no 1 x 1 resource");
    }

    /// <summary>Player 0 sees a tree at (40, 40) through a scout, then the scout leaves (the cell explored, not visible).</summary>
    private static Simulation SeenThenLeft(out EntityHandle node)
    {
        Simulation sim = Flat(size: Size);
        World w = sim.World;
        Assert.True(w.Resources.Spawn(Tree(w), 40 * Size + 40, 100, out node));
        EntityHandle scout = Place(sim, 0, Laborer, At(sim, 38, 40));
        FogMaps.RunThroughNextUpdate(sim);
        w.Units.Free(scout);
        Place(sim, 0, Laborer, At(sim, 5, 5));
        FogMaps.RunThroughNextUpdate(sim);
        Assert.False(w.Fog.IsVisible(0, 40 * Size + 40));
        return sim;
    }

    [Fact]
    public void ANodeFelledOutOfSight_IsRemembered_UntilItsGroundIsSeen_ThenDropped()
    {
        Simulation sim = SeenThenLeft(out EntityHandle node);
        World w = sim.World;
        var v = new PlayerView(w, 0);
        w.Resources.Take(node, int.MaxValue); // felled out of player 0's sight
        Assert.False(w.Resources.Alive[node.Index]);
        for (int t = 0; t < 8; t++) sim.Tick(); // updates come and go: player 0 still doesn't see that cell
        Assert.True(v.TryGetResource(node.Index, out SeenResource r));
        Assert.Equal(new SeenResource(node, Tree(w), 40 * Size + 40, -1), r);
        int s = -1;
        Assert.True(v.NextResource(ref s, out SeenResource next) && next == r);
        Assert.True(w.Fog.NodeAt(0, node.Index).Known);

        // A scout walks in: from the update that shows the cell, the node is gone for player 0 (and the entry dropped).
        Place(sim, 0, Laborer, At(sim, 38, 40));
        FogMaps.RunThroughNextUpdate(sim);
        Assert.True(w.Fog.IsVisible(0, 40 * Size + 40));
        Assert.False(v.TryGetResource(node.Index, out SeenResource none));
        Assert.Equal(default, none);
        s = -1;
        Assert.False(v.NextResource(ref s, out _));
        Assert.False(w.Fog.NodeAt(0, node.Index).Known);
    }

    /// <summary>A node felled where the player looks leaves its answers at once (it sees the ground empty, before the next update drops the entry).</summary>
    [Fact]
    public void ANodeFelledInSight_LeavesAtOnce()
    {
        Simulation sim = Flat(size: Size);
        World w = sim.World;
        Assert.True(w.Resources.Spawn(Tree(w), 40 * Size + 40, 100, out EntityHandle node));
        Place(sim, 0, Laborer, At(sim, 38, 40));
        FogMaps.RunThroughNextUpdate(sim);
        var v = new PlayerView(w, 0);
        Assert.True(v.TryGetResource(node.Index, out SeenResource seen));
        Assert.Equal(100, seen.Remaining);
        w.Resources.Take(node, int.MaxValue);
        Assert.False(v.TryGetResource(node.Index, out _));
        Assert.True(w.Fog.NodeAt(0, node.Index).Known); // the entry waits for the next update
        FogMaps.RunThroughNextUpdate(sim);
        Assert.False(w.Fog.NodeAt(0, node.Index).Known);
    }

    /// <summary>A node the player never saw isn't answered, though its cell is explored now (the explore seam aside, only a test can put one there).</summary>
    [Fact]
    public void ANodeTheyNeverSaw_IsNotAnswered()
    {
        Simulation sim = SeenThenLeft(out _);
        World w = sim.World;
        Assert.True(w.Fog.IsExplored(0, 40 * Size + 41));
        Assert.True(w.Resources.Spawn(Tree(w), 40 * Size + 41, 100, out EntityHandle unseen));
        Assert.False(new PlayerView(w, 0).TryGetResource(unseen.Index, out _));
    }

    // ---------- PlayerView.CanPlace (fog-safe) ----------

    /// <summary>A flat 64 map; player 0 has explored the west half through a holding scout row (x 8-24), player 1 nothing of it.</summary>
    private static Simulation Placement()
    {
        Simulation sim = Flat(size: Size);
        for (int y = 6; y < 58; y += 6)
            for (int x = 8; x <= 24; x += 8)
            {
                EntityHandle h = Place(sim, 0, Laborer, At(sim, x, y));
                sim.World.Units.Hold[h.Index] = true;
            }
        FogMaps.RunThroughNextUpdate(sim);
        BuildMaps.SetTotals(sim, 0, 1000, 1000);
        return sim;
    }

    private static PlacementError ViewSays(Simulation sim, int type, int x, int y)
    {
        new PlayerView(sim.World, 0).CanPlace(type, y * Size + x, out PlacementError e);
        return e;
    }

    private static PlacementError WorldSays(Simulation sim, int type, int x, int y)
    {
        sim.World.CanPlace(0, type, y * Size + x, out PlacementError e);
        return e;
    }

    [Fact]
    public void CanPlace_OpenExploredGround_Passes_AndOwnBuildingsTerrainAndFogRefuse()
    {
        Simulation sim = Placement();
        int house = BuildMaps.House;
        Assert.Equal(PlacementError.None, ViewSays(sim, house, 14, 14));
        Assert.True(sim.World.Buildings.Spawn(0, house, 14 * Size + 14, out _));
        Assert.Equal(PlacementError.Blocked, ViewSays(sim, house, 15, 15)); // its own building
        EntityHandle edge = Place(sim, 0, Laborer, At(sim, 2, 20));
        sim.World.Units.Hold[edge.Index] = true;
        FogMaps.RunThroughNextUpdate(sim);
        Assert.Equal(PlacementError.Blocked, ViewSays(sim, house, 0, 20)); // the map's blocked ring
        Assert.Equal(PlacementError.Unexplored, ViewSays(sim, house, 50, 20));
        Assert.Equal(PlacementError.WrongFaction, ViewSays(sim, BuildMaps.WhirlwindHouse, 20, 20));
        Assert.Equal(PlacementError.OffMap, ViewSays(sim, house, Size - 1, 20));
        Assert.Equal(PlacementError.UnknownType, ViewSays(sim, -1, 20, 20));
        BuildMaps.SetTotals(sim, 0, 0, 0);
        Assert.Equal(PlacementError.CannotAfford, ViewSays(sim, house, 20, 20));
    }

    /// <summary>
    /// BUG-0572: <see cref="PlayerView.CanPlaceAnywhere"/> (the cell-free rules the site search checks once) answers as
    /// <see cref="PlayerView.CanPlace"/> does on open explored ground: none, wrong faction, unknown type, can't afford.
    /// </summary>
    [Fact]
    public void CanPlaceAnywhere_AnswersAsCanPlace_OnOpenGround()
    {
        Simulation sim = Placement();
        var view = new PlayerView(sim.World, 0);
        int house = BuildMaps.House;
        foreach (int type in new[] { house, BuildMaps.WhirlwindHouse, -1 })
        {
            view.CanPlaceAnywhere(type, out PlacementError anywhere);
            Assert.Equal(ViewSays(sim, type, 20, 20), anywhere);
        }
        Assert.True(view.CanPlaceAnywhere(house, out _));
        BuildMaps.SetTotals(sim, 0, 0, 0);
        Assert.False(view.CanPlaceAnywhere(house, out PlacementError poor));
        Assert.Equal(PlacementError.CannotAfford, poor);
        Assert.Equal(poor, ViewSays(sim, house, 20, 20));
    }

    /// <summary>An enemy building the player never saw (built in its explored, unseen ground) doesn't refuse in the view, as it does in <see cref="World.CanPlace"/>: the view reveals nothing.</summary>
    [Fact]
    public void CanPlace_AHiddenEnemyBuildingNeverSeen_DoesNotRefuse()
    {
        Simulation sim = Placement();
        // Ground player 0 explored and then lost: the top scout row walks away (the next row, at y 12, sees down to y 5).
        int house = BuildMaps.House;
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == 0 && u.Position[i].Y < 7 * 2f) u.Position[i] = At(sim, 16, 30);
        FogMaps.RunThroughNextUpdate(sim);
        Assert.True(sim.World.Fog.IsExplored(0, 2 * Size + 16) && !sim.World.Fog.IsVisible(0, 2 * Size + 16));
        Assert.True(sim.World.Buildings.Spawn(1, BuildMaps.WhirlwindHouse, 2 * Size + 16, out _));
        Assert.Equal(PlacementError.Blocked, WorldSays(sim, house, 16, 2));
        Assert.Equal(PlacementError.None, ViewSays(sim, house, 16, 2));
        Assert.False(new PlayerView(sim.World, 0).TryGetGhost(0, out _));
    }

    /// <summary>An enemy building seen and then out of sight refuses through the ghost list; still so after it died unseen (the player believes it stands), while <see cref="World.CanPlace"/> lets the spot go.</summary>
    [Fact]
    public void CanPlace_ARememberedEnemyBuilding_Refuses_EvenGone()
    {
        Simulation sim = Placement();
        int house = BuildMaps.House;
        Assert.True(sim.World.Buildings.Spawn(1, BuildMaps.WhirlwindHouse, 2 * Size + 16, out EntityHandle enemy));
        FogMaps.RunThroughNextUpdate(sim); // seen by the top scouts
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == 0 && u.Position[i].Y < 7 * 2f) u.Position[i] = At(sim, 16, 30);
        FogMaps.RunThroughNextUpdate(sim);
        Assert.False(sim.World.Fog.CanSeeBuilding(0, enemy.Index));
        Assert.True(new PlayerView(sim.World, 0).TryGetGhost(enemy.Index, out _));
        Assert.Equal(PlacementError.Blocked, ViewSays(sim, house, 16, 2));
        Assert.Equal(PlacementError.Blocked, ViewSays(sim, house, 17, 3)); // overlapping its far corner
        Assert.True(sim.World.Buildings.Free(enemy)); // dies out of sight
        Assert.Equal(PlacementError.None, WorldSays(sim, house, 16, 2));
        Assert.Equal(PlacementError.Blocked, ViewSays(sim, house, 16, 2));
    }

    /// <summary>A node felled out of the player's sight still refuses a site over it in the view (the player believes it stands); the world lets it go.</summary>
    [Fact]
    public void CanPlace_ARememberedFelledNode_Refuses()
    {
        Simulation sim = SeenThenLeft(out EntityHandle node);
        World w = sim.World;
        BuildMaps.SetTotals(sim, 0, 1000, 1000);
        int house = BuildMaps.House;
        Assert.Equal(PlacementError.Blocked, ViewSays(sim, house, 40, 40));
        w.Resources.Take(node, int.MaxValue);
        Assert.Equal(PlacementError.None, WorldSays(sim, house, 39, 39));
        Assert.Equal(PlacementError.Blocked, ViewSays(sim, house, 39, 39));
        Assert.Equal(PlacementError.None, ViewSays(sim, house, 37, 39)); // beside it
    }
}
