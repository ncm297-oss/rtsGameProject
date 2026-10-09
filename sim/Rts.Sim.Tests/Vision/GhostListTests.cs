using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Vision;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-3b criterion 3: each player's "last known buildings" list (docs/02: enemy buildings seen once stay as ghosts in
/// explored fog until the cell is seen again). A building seen once stays listed when its cells fall back to explored;
/// gone, it is dropped at the first update that shows its ground, not before; seen again it is refreshed; an Attack on a
/// listed building the player can't see now is accepted, walks there and attacks, and ends cleanly when the building is gone.
/// </summary>
public class GhostListTests
{
    /// <summary>A flat 64 map, combat on: player 0's scout (a holding Laborer, sight 14) beside player 1's Tent at (30, 30).</summary>
    private static (Simulation Sim, EntityHandle Scout, EntityHandle Tent) Scene()
    {
        Simulation sim = Flat(size: 64);
        EntityHandle tent = TowerTests.PlaceBuilding(sim, 1, TowerTests.Tent, 30, 30); // footprint 60-64 m
        EntityHandle scout = Spotter(sim, 0, new Vector2(55f, 62f)); // 5 m west of it; runs a fog update
        return (sim, scout, tent);
    }

    private static BuildingGhost Ghost(World w, int player, EntityHandle building) => w.Fog.Ghosts(player)[building.Index];

    [Fact]
    public void ABuildingSeenOnce_IsListed_WithItsTypeCellGenerationAndOwner_AndStaysWhenItsCellsFallBackToExplored()
    {
        (Simulation sim, EntityHandle scout, EntityHandle tent) = Scene();
        World w = sim.World;
        Assert.Equal(w.Buildings.Capacity, w.Fog.Ghosts(0).Length);
        Assert.Equal(new BuildingGhost(tent.Generation, TowerTests.Tent, 30 * 64 + 30, 1), Ghost(w, 0, tent));
        Assert.True(Ghost(w, 0, tent).Known);
        Assert.Equal(1, w.Fog.GhostCount(0));
        // Never one's own buildings: player 1 lists nothing (it sees no building of player 0's).
        Assert.Equal(0, w.Fog.GhostCount(1));
        // The scout gone, the cells turn explored at the next update; the ghost stays.
        w.Units.Free(scout);
        FogMaps.RunThroughNextUpdate(sim);
        int cell = 30 * 64 + 30;
        Assert.False(w.Fog.IsVisible(0, cell));
        Assert.True(w.Fog.IsExplored(0, cell));
        Assert.False(w.Fog.CanSeeBuilding(0, tent.Index));
        Assert.Equal(new BuildingGhost(tent.Generation, TowerTests.Tent, cell, 1), Ghost(w, 0, tent));
        for (int t = 0; t < 40; t++) sim.Tick();
        Assert.Equal(1, w.Fog.GhostCount(0));
    }

    [Fact]
    public void AGoneBuilding_StaysListedUnseen_AndIsDroppedTheFirstUpdateItsGroundIsVisible()
    {
        (Simulation sim, EntityHandle scout, EntityHandle tent) = Scene();
        World w = sim.World;
        w.Units.Free(scout);
        FogMaps.RunThroughNextUpdate(sim);
        Assert.True(w.Buildings.Free(tent)); // destroyed where player 0 can't see
        for (int t = 0; t < 20; t++) sim.Tick();
        Assert.True(Ghost(w, 0, tent).Known, "dropped before the ground was seen");
        // A new scout: placed between ticks, its circle shows the ground at the next update and not before.
        Place(sim, 0, GatherMaps.Laborer, new Vector2(55f, 62f));
        while (!VisionSystem.IsUpdateTick(sim.TickNumber))
        {
            sim.Tick();
            Assert.True(Ghost(w, 0, tent).Known, $"dropped on tick {sim.TickNumber - 1}, not an update tick");
        }
        sim.Tick(); // the update
        Assert.False(Ghost(w, 0, tent).Known);
        Assert.Equal(0, w.Fog.GhostCount(0));
    }

    [Fact]
    public void ABuildingSeenAgain_IsRefreshed_ANewOneInTheSameSlotReplacesTheOld()
    {
        (Simulation sim, EntityHandle scout, EntityHandle tent) = Scene();
        World w = sim.World;
        w.Units.Free(scout);
        FogMaps.RunThroughNextUpdate(sim);
        // Out of sight, the Tent is replaced: freed, and a new one placed in the same slot and cell (the next generation).
        Assert.True(w.Buildings.Free(tent));
        EntityHandle again = TowerTests.PlaceBuilding(sim, 1, TowerTests.Tent, 30, 30);
        Assert.Equal(tent.Index, again.Index);
        Assert.NotEqual(tent.Generation, again.Generation);
        FogMaps.RunThroughNextUpdate(sim);
        Assert.Equal(tent.Generation, Ghost(w, 0, tent).Generation); // unseen: the old one is what player 0 knows
        Spotter(sim, 0, new Vector2(55f, 62f));
        Assert.Equal(new BuildingGhost(again.Generation, TowerTests.Tent, 30 * 64 + 30, 1), Ghost(w, 0, again));
        Assert.Equal(1, w.Fog.GhostCount(0));
    }

    [Fact]
    public void AnAttackOnAListedBuildingNotSeenNow_IsAccepted_WalksThere_AndHitsItOnceSeen()
    {
        (Simulation sim, EntityHandle scout, EntityHandle tent) = Scene();
        World w = sim.World;
        w.Units.Free(scout);
        // Heavy Infantry (sight 14) 40 m west of the Tent: it doesn't see it.
        EntityHandle hi = Place(sim, 0, HeavyInfantry, new Vector2(20f, 62f));
        FogMaps.RunThroughNextUpdate(sim);
        Assert.False(w.Fog.CanSeeBuilding(0, tent.Index));
        Assert.True(Ghost(w, 0, tent).Known);
        sim.Enqueue(Command.Attack(0, hi, tent, isBuilding: true));
        sim.Tick();
        sim.Tick(); // a command applies in the tick after the one running when it was queued
        Assert.Equal(tent, w.Units.Target[hi.Index]);
        Assert.Equal(CombatMode.Ordered, w.Units.Mode[hi.Index]);
        int hp = w.Buildings.Hp[tent.Index];
        int t = RunUntil(sim, () => w.Buildings.Hp[tent.Index] < hp, 400);
        Assert.True(w.Buildings.Hp[tent.Index] < hp, "it never hit the Tent");
        Assert.Equal(tent, w.Units.Target[hi.Index]);
    }

    [Fact]
    public void AnAttackOnAListedBuildingThatIsGone_IsAccepted_AndEndsCleanlyWhenItsGroundComesIntoSight()
    {
        (Simulation sim, EntityHandle scout, EntityHandle tent) = Scene();
        World w = sim.World;
        w.Units.Free(scout);
        EntityHandle hi = Place(sim, 0, HeavyInfantry, new Vector2(20f, 62f));
        FogMaps.RunThroughNextUpdate(sim);
        Assert.True(w.Buildings.Free(tent)); // gone, unseen: player 0 still lists it
        sim.Enqueue(Command.Attack(0, hi, tent, isBuilding: true));
        sim.Tick();
        sim.Tick(); // a command applies in the tick after the one running when it was queued
        Assert.Equal(tent, w.Units.Target[hi.Index]);
        Assert.Equal(CombatMode.Ordered, w.Units.Mode[hi.Index]);
        Assert.Equal(UnitState.Moving, w.Units.State[hi.Index]);
        int ended = RunUntil(sim, () => w.Units.Target[hi.Index].Generation == 0, 400);
        sim.Tick(); // the next phase 7 settles the ended order (Idle where it stands, no mode)
        Assert.Equal(default, w.Units.Target[hi.Index]);
        Assert.Equal(CombatMode.None, w.Units.Mode[hi.Index]);
        Assert.Equal(UnitState.Idle, w.Units.State[hi.Index]);
        // It stopped as the ground came into its sight (14 m), not at the footprint.
        Vector2 p = w.Units.Position[hi.Index];
        float gap = 60f - p.X;
        Assert.True(gap > 10f && gap <= 14.5f, $"stopped {gap} m from the footprint after {ended} ticks");
        Assert.Equal(0, w.Projectiles.Count);
        FogMaps.RunThroughNextUpdate(sim);
        Assert.False(Ghost(w, 0, tent).Known);
    }

    [Fact]
    public void AnAttackOnAnUnlistedUnseenBuilding_IsStillDropped()
    {
        Simulation sim = Flat(size: 64);
        World w = sim.World;
        EntityHandle tent = TowerTests.PlaceBuilding(sim, 1, TowerTests.Tent, 30, 30);
        EntityHandle hi = Place(sim, 0, HeavyInfantry, new Vector2(20f, 62f));
        FogMaps.RunThroughNextUpdate(sim);
        Assert.False(Ghost(w, 0, tent).Known);
        sim.Enqueue(Command.Attack(0, hi, tent, isBuilding: true));
        sim.Tick();
        sim.Tick(); // a command applies in the tick after the one running when it was queued
        Assert.Equal(default, w.Units.Target[hi.Index]);
        Assert.Equal(CombatMode.None, w.Units.Mode[hi.Index]);
    }

    [Fact]
    public void ANonOrderedUnit_NeverTakesAListedBuildingItCannotSee()
    {
        // The list relaxes the explicit Attack only: an Idle unit's scans still take only what its owner sees.
        (Simulation sim, EntityHandle scout, EntityHandle tent) = Scene();
        World w = sim.World;
        w.Units.Free(scout);
        EntityHandle hi = Place(sim, 0, HeavyInfantry, new Vector2(36f, 62f)); // 24 m: out of its 14 m sight
        for (int t = 0; t < 100; t++)
        {
            sim.Tick();
            Assert.Equal(default, w.Units.Target[hi.Index]);
        }
    }
}
