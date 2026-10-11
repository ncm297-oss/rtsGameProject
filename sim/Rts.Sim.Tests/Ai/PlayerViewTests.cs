using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Vision;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-1: <see cref="PlayerView"/>'s rules one at a time (the fuzz, <see cref="PlayerViewOracleFuzzTests"/>, checks them all
/// together): stealth hides an enemy from the moment it starts, until detected; a tower revealed by its own hit from high
/// ground is answered while revealed and remembered after; a building that died unseen stays a ghost until its ground is
/// seen; a player outside the match has no view.
/// </summary>
public class PlayerViewTests
{
    [Fact]
    public void AView_NeedsAWorld_AndAPlayerOfTheMatch()
    {
        Simulation sim = Flat(players: 3);
        Assert.Throws<ArgumentNullException>(() => new PlayerView(null!, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerView(sim.World, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerView(sim.World, 3));
        var view = new PlayerView(sim.World, 2);
        Assert.Equal(2, view.Player);
        Assert.Equal(sim.World.FactionOf(2), view.Faction);
    }

    /// <summary>
    /// A stealthed Camp Follower of player 1 with a Malazan Crossbowman of player 0 6 m off (its cell in sight, outside the
    /// 3 m proximity) and a Laborer of player 2 (a 10 m detector in the test data) 5 m off: player 0's view hides it from the
    /// moment the status starts (no tick of lag), player 2's shows it, player 1's always does. When the Laborer leaves, player
    /// 2's view hides it too.
    /// </summary>
    [Fact]
    public void AStealthedEnemy_IsHiddenFromTheMomentItStarts_UnlessDetected_AndItsOwnerAlwaysSeesIt()
    {
        Simulation sim = StealthTests.Scene(players: 3, combat: false);
        World w = sim.World;
        int worker = w.Data.FindUnit("whirlwind_camp_follower");
        EntityHandle hidden = Place(sim, 1, worker, At(sim, 20, 20));
        Place(sim, 0, w.Data.FindUnit("malazan_crossbowman"), At(sim, 23, 20));
        EntityHandle detector = Place(sim, 2, w.Data.FindUnit("malazan_laborer"), At(sim, 20, 22, dy: 1f));
        FogMaps.RunThroughNextUpdate(sim);
        PlayerView v0 = new(w, 0), v1 = new(w, 1), v2 = new(w, 2);
        Assert.True(v0.TryGetUnit(hidden.Index, out SeenUnit seen) && seen.Handle == hidden);

        StealthTests.Stealth(sim, hidden); // between ticks, as a cast resolving mid-tick would
        Assert.True(w.IsStealthed(hidden.Index));
        Assert.False(v0.TryGetUnit(hidden.Index, out SeenUnit none));
        Assert.Equal(default, none);
        Assert.True(v1.TryGetUnit(hidden.Index, out _));
        Assert.True(v2.TryGetUnit(hidden.Index, out _));
        Assert.Equal(1, Enumerated(v0, 1)); // player 0 lists the Laborer of player 2 and nothing of player 1
        sim.Tick();
        Assert.False(v0.TryGetUnit(hidden.Index, out _));
        Assert.True(v2.TryGetUnit(hidden.Index, out _));

        w.Units.Position[detector.Index] = At(sim, 40, 40); // a test seam: the detector walks off
        sim.Tick();
        Assert.False(v2.TryGetUnit(hidden.Index, out _));
        Assert.True(v1.TryGetUnit(hidden.Index, out _));
    }

    /// <summary>How many other players' units the view lists, asserting none is <paramref name="hiddenOwner"/>'s.</summary>
    private static int Enumerated(PlayerView v, int hiddenOwner)
    {
        int n = 0, s = -1;
        while (v.NextUnit(ref s, out SeenUnit u))
        {
            Assert.NotEqual(hiddenOwner, u.Owner);
            if (u.Owner != v.Player) n++;
        }
        return n;
    }

    /// <summary>
    /// BUG-0270's scene (TowerTests): a Watchtower of player 0 on the plateau shooting a Crossbowman of player 1 below. Before
    /// the first hit player 1's view has no tower; from the hit it answers the tower (seen, as <c>Fog.CanSeeBuilding</c> says);
    /// once the reveal is over (the Crossbowman set down out of reach) the tower is a ghost in player 1's view.
    /// </summary>
    [Fact]
    public void ATowerRevealedByItsHitFromHighGround_IsAnsweredWhileRevealed_ThenRemembered()
    {
        Simulation sim = FogMaps.Sim(FogMaps.TwoLevel());
        World w = sim.World;
        EntityHandle tower = TowerTests.PlaceBuilding(sim, 0, TowerTests.Watchtower, 22, 30);
        EntityHandle xb = Place(sim, 1, Crossbowman, FogMaps.Cell(14, 31));
        sim.Enqueue(Command.HoldPosition(1, xb));
        var v1 = new PlayerView(w, 1);
        int hp = w.Units.Hp[xb.Index];
        for (int t = 0; t < 200 && w.Units.Hp[xb.Index] == hp; t++)
        {
            Assert.False(v1.TryGetBuilding(tower.Index, out _), $"tick {sim.TickNumber}: the tower is answered before any hit");
            Assert.False(v1.TryGetGhost(tower.Index, out _));
            sim.Tick();
        }
        Assert.True(w.Units.Hp[xb.Index] < hp, "the tower never hit the Crossbowman");
        Assert.True(w.Fog.BuildingRevealed(1, tower.Index));
        Assert.True(v1.TryGetBuilding(tower.Index, out SeenBuilding b));
        Assert.Equal(tower, b.Handle);
        Assert.False(v1.TryGetGhost(tower.Index, out _)); // seen now: the live answer, not the memory

        w.Units.Position[xb.Index] = FogMaps.Cell(2, 31); // out of the tower's reach (TowerTests' seam)
        for (int t = 0; t < 120; t++) sim.Tick();
        Assert.False(w.Fog.BuildingRevealed(1, tower.Index));
        Assert.False(v1.TryGetBuilding(tower.Index, out _));
        Assert.True(v1.TryGetGhost(tower.Index, out BuildingGhost g));
        Assert.Equal((tower.Generation, TowerTests.Watchtower, 0), (g.Generation, g.TypeId, g.Owner));
    }

    /// <summary>
    /// GhostListTests' scene: a Tent of player 1 seen by a scout of player 0, then left (the scout removed): a ghost; destroyed
    /// unseen: still a ghost (player 0 doesn't know); a scout back in sight of its ground: gone at the next fog update.
    /// </summary>
    [Fact]
    public void ABuildingThatDiedUnseen_StaysAGhost_UntilItsGroundIsSeen()
    {
        Simulation sim = Flat(size: 64);
        World w = sim.World;
        EntityHandle tent = TowerTests.PlaceBuilding(sim, 1, TowerTests.Tent, 40, 30);
        EntityHandle scout = Place(sim, 0, Laborer, At(sim, 36, 31));
        var v0 = new PlayerView(w, 0);
        FogMaps.RunThroughNextUpdate(sim);
        Assert.True(v0.TryGetBuilding(tent.Index, out SeenBuilding seen) && seen.Owner == 1);
        Assert.False(v0.TryGetGhost(tent.Index, out _));

        w.Units.Free(scout);
        FogMaps.RunThroughNextUpdate(sim);
        Assert.False(v0.TryGetBuilding(tent.Index, out _));
        Assert.True(v0.TryGetGhost(tent.Index, out BuildingGhost g) && g.Generation == tent.Generation);

        Assert.True(w.Buildings.Free(tent)); // destroyed where player 0 can't see
        FogMaps.RunThroughNextUpdate(sim);
        Assert.True(v0.TryGetGhost(tent.Index, out _));
        int s = -1;
        Assert.True(v0.NextGhost(ref s, out BuildingGhost listed) && s == tent.Index && listed == g);

        Place(sim, 0, Laborer, At(sim, 36, 31));
        FogMaps.RunThroughNextUpdate(sim);
        Assert.False(v0.TryGetGhost(tent.Index, out _));
        s = -1;
        Assert.False(v0.NextGhost(ref s, out _));
    }

    /// <summary>Another player's building answers no queue, no progress and no item, even when that player sees it.</summary>
    [Fact]
    public void AnEnemyBuildingsQueue_IsNeverAnswered()
    {
        Simulation sim = TestSim.Explored(Flat(size: 64));
        World w = sim.World;
        EntityHandle tent = TowerTests.PlaceBuilding(sim, 1, w.Data.FindBuilding("whirlwind_holy_camp"), 30, 30);
        Place(sim, 0, Laborer, At(sim, 28, 28));
        sim.Enqueue(Command.Train(1, At(sim, 31, 31), w.Data.FindUnit("whirlwind_camp_follower")));
        FogMaps.RunThroughNextUpdate(sim);
        Assert.True(w.Buildings.QueueCount[tent.Index] > 0);
        var v0 = new PlayerView(w, 0);
        var v1 = new PlayerView(w, 1);
        Assert.True(v0.TryGetBuilding(tent.Index, out _));
        Assert.Equal(0, v0.QueueCount(tent.Index));
        Assert.Equal(0, v0.QueueProgress(tent.Index));
        Assert.False(v0.TryGetQueueItem(tent.Index, 0, out int type, out bool isTech));
        Assert.Equal((-1, false), (type, isTech));
        Assert.Equal(w.Buildings.QueueCount[tent.Index], v1.QueueCount(tent.Index));
        Assert.True(v1.TryGetQueueItem(tent.Index, 0, out type, out _));
        Assert.Equal(w.Data.FindUnit("whirlwind_camp_follower"), type);
    }
}
