using System.Collections.Immutable;
using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Vision;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-S5 criteria 2-3 (docs/02 "Vision and fog of war"): allies share vision. Player 0 and its ally player 1 (team 1) stand
/// far apart on a flat 64 map; player 2 is an enemy (team 0). What only player 1's units see, player 0 sees: the cell (visible,
/// and explored once it is left), an enemy's unit and building, the building as a ghost for both once nobody looks, a
/// stealthed enemy one of player 1's detectors finds, and a high-ground reveal. The same scenes as a free-for-all (every team
/// 0) give player 0 none of it.
/// </summary>
public class SharedVisionTests
{
    // Player 0 looks round cell (8, 8); its ally (or, in the free-for-all twin, another player) round (50, 50). Sight 14 m = 7 cells.
    private static readonly (int X, int Y) Home = (8, 8), Far = (50, 50);

    private static Simulation Scene(bool allies, GameData? data = null)
    {
        int[] teams = allies ? new[] { 1, 1, 0 } : new[] { 0, 0, 0 };
        ImmutableArray<PlayerSeat> seats = teams.Select((t, p) => new PlayerSeat(p % 2, SeatController.Human, AiDifficulty.Normal, t)).ToImmutableArray();
        return new Simulation(new SimConfig(1, 3, 64, 1024) { Data = data ?? TestSim.Data, Combat = false, Seats = seats }, LocalMovementTests.Flat(64));
    }

    private static void Ticks(Simulation sim, int n)
    {
        for (int t = 0; t < n; t++) sim.Tick();
    }

    /// <summary>Player 0's eyes at home, player 1's far off, player 2's unit beside player 1's (5 m off, inside its sight and outside player 0's).</summary>
    private static (EntityHandle Own, EntityHandle Ally, EntityHandle Enemy) ThreeUnits(Simulation sim, int type)
    {
        EntityHandle own = Place(sim, 0, type, At(sim, Home.X, Home.Y));
        EntityHandle ally = Place(sim, 1, type, At(sim, Far.X, Far.Y));
        EntityHandle enemy = Place(sim, 2, type, At(sim, Far.X, Far.Y, dx: 5f));
        Ticks(sim, 6);
        return (own, ally, enemy);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnEnemyUnit_InsideOnlyTheAllysSight_IsSeenByThePlayer_AndItsCellIsVisible(bool allies)
    {
        Simulation sim = Scene(allies);
        World w = sim.World;
        (_, EntityHandle ally, EntityHandle enemy) = ThreeUnits(sim, HeavyInfantry);
        int cell = w.Fog.CellOf(w.Units.Position[enemy.Index]);
        Assert.True(w.Fog.IsVisible(1, cell) && w.Fog.CanSeeUnit(1, enemy.Index), "setup: player 1 sees the enemy");
        Assert.True(w.Fog.OwnVisibility(0)[cell] == VisionConstants.Unexplored, "setup: player 0's own eyes never reach it");
        Assert.Equal(allies, w.Fog.IsVisible(0, cell));
        Assert.Equal(allies, w.Fog.IsExplored(0, cell));
        Assert.Equal(allies ? VisionConstants.Visible : VisionConstants.Unexplored, w.Fog.Visibility(0)[cell]);
        Assert.Equal(allies, w.Fog.CanSeeUnit(0, enemy.Index));
        Assert.Equal(allies, new PlayerView(w, 0).TryGetUnit(enemy.Index, out _));
        // The ally's own unit: an ally's unit is seen as an own one; another player's far off is not.
        Assert.Equal(allies, w.Fog.CanSeeUnit(0, ally.Index));
        Assert.Equal(allies, new PlayerView(w, 0).IsVisible(cell));
        // The enemy sees nothing more for it (team 0: its own eyes).
        Assert.False(w.Fog.IsVisible(2, w.Fog.CellOf(At(sim, Home.X, Home.Y))));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACellTheAllyHasLeft_StaysExploredForThePlayer(bool allies)
    {
        Simulation sim = Scene(allies);
        World w = sim.World;
        (_, EntityHandle ally, _) = ThreeUnits(sim, HeavyInfantry);
        int cell = w.Fog.CellOf(At(sim, Far.X, Far.Y - 3));
        Assert.Equal(allies, w.Fog.IsVisible(0, cell));
        // The ally's unit (and the enemy's) leave: the seam puts them in a far corner between ticks.
        w.Units.Position[ally.Index] = At(sim, 56, 8);
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 2) w.Units.Position[i] = At(sim, 8, 56);
        Ticks(sim, 4);
        Assert.False(w.Fog.IsVisible(1, cell), "setup: nobody looks there now");
        Assert.True(w.Fog.IsExplored(1, cell));
        Assert.False(w.Fog.IsVisible(0, cell));
        Assert.Equal(allies, w.Fog.IsExplored(0, cell));
        Assert.Equal(allies ? VisionConstants.Explored : VisionConstants.Unexplored, w.Fog.Visibility(0)[cell]);
        Assert.Equal(allies, new PlayerView(w, 0).IsExplored(cell));
    }

    /// <summary>
    /// An enemy building only player 1's unit sees: player 0 sees it now (CanSeeBuilding, its PlayerView); once the unit walks
    /// off it is a ghost for both (the last-known list, PlayerView's ghost cursor), and stays one until either looks again.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnEnemyBuilding_SeenOnlyByTheAlly_IsSeenThenAGhostForBoth(bool allies)
    {
        Simulation sim = Scene(allies);
        World w = sim.World;
        Place(sim, 0, HeavyInfantry, At(sim, Home.X, Home.Y));
        EntityHandle ally = Place(sim, 1, HeavyInfantry, At(sim, Far.X, Far.Y));
        Place(sim, 2, HeavyInfantry, At(sim, 8, 56)); // keeps player 2 in, far from both
        EntityHandle house = GatherMaps.Building(sim, Far.X + 3, Far.Y - 1, player: 2);
        Ticks(sim, 4);
        Assert.True(w.Fog.CanSeeBuilding(1, house.Index), "setup: player 1 sees the building");
        Assert.Equal(allies, w.Fog.CanSeeBuilding(0, house.Index));
        Assert.Equal(allies, new PlayerView(w, 0).TryGetBuilding(house.Index, out _));
        Assert.Equal(allies, w.Fog.HasGhost(0, house));
        w.Units.Position[ally.Index] = At(sim, 56, 8);
        Ticks(sim, 4);
        Assert.False(w.Fog.CanSeeBuilding(1, house.Index), "setup: nobody looks now");
        Assert.False(w.Fog.CanSeeBuilding(0, house.Index));
        Assert.True(new PlayerView(w, 1).TryGetGhost(house.Index, out BuildingGhost g1) && g1.Owner == 2);
        Assert.Equal(allies, new PlayerView(w, 0).TryGetGhost(house.Index, out BuildingGhost g0));
        if (allies) Assert.Equal(g1, g0);
        Assert.Equal(allies ? 1 : 0, w.Fog.GhostCount(0));
    }

    /// <summary>An ally's building is never remembered as a ghost (it is always seen live): neither its slot nor the count.</summary>
    [Fact]
    public void AnAllysBuilding_IsSeenLive_NeverAGhost()
    {
        Simulation sim = Scene(allies: true);
        World w = sim.World;
        Place(sim, 0, HeavyInfantry, At(sim, Home.X, Home.Y));
        Place(sim, 1, HeavyInfantry, At(sim, Far.X, Far.Y));
        EntityHandle house = GatherMaps.Building(sim, Far.X + 3, Far.Y - 1, player: 1);
        Ticks(sim, 4);
        Assert.True(w.Fog.CanSeeBuilding(0, house.Index));
        Assert.False(w.Fog.HasGhost(0, house));
        Assert.Equal(0, w.Fog.GhostCount(0));
    }

    /// <summary>
    /// M6-S5 criterion 3: a stealthed enemy (player 2's worker, stealthed) beside player 1's detector (a Laborer, 10 m) and far
    /// from player 0: detected by both allies, and seen by both; in the free-for-all only the detector's owner.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AStealthedEnemy_DetectedOnlyByTheAllysDetector_IsDetectedAndSeenByBoth(bool allies)
    {
        Simulation sim = Scene(allies, StealthTests.Data);
        World w = sim.World;
        GameData d = w.Data;
        Place(sim, 0, d.FindUnit("malazan_heavy_infantry"), At(sim, Home.X, Home.Y));
        Place(sim, 1, d.FindUnit("malazan_laborer"), At(sim, Far.X, Far.Y));
        EntityHandle hidden = Place(sim, 2, d.FindUnit("whirlwind_camp_follower"), At(sim, Far.X, Far.Y, dx: 6f));
        StealthTests.Stealth(sim, hidden);
        Ticks(sim, 6);
        Assert.True(w.IsStealthed(hidden.Index));
        Assert.True(w.IsDetectedBy(1, hidden.Index) && w.Fog.CanSeeUnit(1, hidden.Index), "setup: the detector's owner detects it");
        Assert.Equal(allies, w.IsDetectedBy(0, hidden.Index));
        Assert.Equal(allies, w.Fog.CanSeeUnit(0, hidden.Index));
        Assert.Equal(allies, new PlayerView(w, 0).TryGetUnit(hidden.Index, out _));
        // Its own side never needs to.
        Assert.False(w.IsDetectedBy(2, hidden.Index));
    }

    /// <summary>
    /// Shared vision is not shared detection out of nothing: a stealthed enemy whose cell the team sees, with no detector of
    /// either ally within reach (player 1's unit there is Heavy Infantry, 6 m off: past the 3 m proximity), stays hidden from both.
    /// </summary>
    [Fact]
    public void AStealthedEnemy_FarFromEveryDetector_StaysHiddenFromBothAllies()
    {
        Simulation sim = Scene(true, StealthTests.Data);
        World w = sim.World;
        GameData d = w.Data;
        Place(sim, 0, d.FindUnit("malazan_heavy_infantry"), At(sim, Home.X, Home.Y));
        Place(sim, 1, d.FindUnit("malazan_heavy_infantry"), At(sim, Far.X, Far.Y));
        EntityHandle hidden = Place(sim, 2, d.FindUnit("whirlwind_camp_follower"), At(sim, Far.X, Far.Y, dx: 6f));
        StealthTests.Stealth(sim, hidden);
        Ticks(sim, 6);
        Assert.True(w.Fog.IsVisible(0, w.Fog.CellOf(w.Units.Position[hidden.Index])), "setup: the team sees its cell");
        Assert.False(w.IsDetectedBy(0, hidden.Index) || w.IsDetectedBy(1, hidden.Index));
        Assert.False(w.Fog.CanSeeUnit(0, hidden.Index) || w.Fog.CanSeeUnit(1, hidden.Index));
    }

    /// <summary>A hit from high ground on player 1's unit reveals the attacker to player 1 and (allies) to player 0, for 2 s.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AHighGroundReveal_OnTheAlly_IsSharedWithThePlayer(bool allies)
    {
        Simulation sim = Scene(allies);
        World w = sim.World;
        (_, _, EntityHandle enemy) = ThreeUnits(sim, HeavyInfantry);
        VisionSystem.OnHit(w, enemy, attackerIsBuilding: false, attackerLevel: 1, victimOwner: 1, victimLevel: 0);
        Assert.True(w.Fog.Revealed(1, enemy.Index));
        Assert.Equal(allies, w.Fog.Revealed(0, enemy.Index));
        Assert.False(w.Fog.Revealed(2, enemy.Index));
        Assert.Equal(w.Fog.RevealEnd(1, enemy.Index), allies ? w.Fog.RevealEnd(0, enemy.Index) : w.Fog.RevealEnd(1, enemy.Index));
        // Seen by player 0 through it even where no eye of the team reaches (the enemy walks off between ticks).
        w.Units.Position[enemy.Index] = At(sim, 30, 56);
        Ticks(sim, 4);
        Assert.Equal(allies, w.Fog.CanSeeUnit(0, enemy.Index));
    }

    /// <summary>
    /// The team maps are derived state: scribbled over (bits, box, byte map) they leave the state hash alone, and the next fog
    /// update rebuilds them from the own bits, so the answers come back.
    /// </summary>
    [Fact]
    public void TheTeamMaps_AreDerived_NotHashed_AndRebuiltByTheNextUpdate()
    {
        Simulation a = Scene(allies: true), b = Scene(allies: true);
        foreach (Simulation sim in new[] { a, b }) ThreeUnits(sim, HeavyInfantry);
        World w = a.World;
        int far = w.Fog.CellOf(At(a, Far.X, Far.Y));
        Assert.True(w.Fog.IsVisible(0, far));
        static T Field<T>(World w, string name) => (T)typeof(FogStore).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(w.Fog)!;
        // Team 1's arrays (players 0 and 1 share them; player 2, alone, reads its own hashed arrays through the same slot).
        Array.Clear(Field<ulong[][]>(w, "_teamVisible")[0]);
        Array.Clear(Field<ulong[][]>(w, "_teamExplored")[0]);
        Array.Fill(Field<int[]>(w, "_teamBox"), int.MaxValue, 0, 8);
        Assert.False(w.Fog.IsVisible(0, far), "setup: the scribble took");
        Assert.Equal(b.StateHash(), a.StateHash());
        Ticks(a, 4);
        Ticks(b, 4);
        Assert.Equal(b.StateHash(), a.StateHash());
        Assert.True(w.Fog.IsVisible(0, far));
        Assert.True(w.Fog.Visibility(0).SequenceEqual(b.World.Fog.Visibility(0)));
    }

    /// <summary>
    /// A 2v1 (teams 1, 1, 0): the union is only the team's. Player 2 (no team) sees neither ally's ground; each ally sees the
    /// other's; the hash moves with the teams, but every player's own fog bits (what is hashed and saved) are its own eyes.
    /// </summary>
    [Fact]
    public void TheOwnBits_StayEachPlayersOwnEyes_TheUnionIsDerived()
    {
        Simulation shared = Scene(allies: true), alone = Scene(allies: false);
        foreach (Simulation sim in new[] { shared, alone }) ThreeUnits(sim, HeavyInfantry);
        for (int p = 0; p < 3; p++)
            Assert.True(shared.World.Fog.OwnVisibility(p).SequenceEqual(alone.World.Fog.OwnVisibility(p)), $"player {p}'s own map");
        int far = shared.World.Fog.CellOf(At(shared, Far.X, Far.Y));
        Assert.False(shared.World.Fog.IsVisible(2, shared.World.Fog.CellOf(At(shared, Home.X, Home.Y))));
        Assert.True(shared.World.Fog.IsVisible(0, far) && !alone.World.Fog.IsVisible(0, far));
    }
}
