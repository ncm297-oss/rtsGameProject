using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Vision;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-3a criterion 2: the fog stamp on a flat map. A lone unit's disc against the brute-force oracle, a building's stamp
/// from its footprint centre, enemies never contributing, visible to explored, the update cadence, a dead unit's circle,
/// and the initial stamp at tick 0.
/// </summary>
[Collection(SerialCollection.Name)] // FogUpdates_AllocateNothing measures allocation
public class FogStampTests
{
    private static Simulation FlatSim(int size = 64) => FogMaps.Sim(LocalMovementTests.Flat(size), combat: false);

    [Fact]
    public void ALoneUnitWithSight18_SeesExactlyTheDiscOfCellsWithin18m_AndNothingElse()
    {
        Simulation sim = FlatSim();
        Assert.Equal(18f, sim.World.Data.Units[Crossbowman].Sight);
        Place(sim, 0, Crossbowman, FogMaps.Cell(32, 32));
        sim.Tick();
        World w = sim.World;
        FogMaps.AssertMatchesOracle(w, 0);
        // The disc of cell centres within 18 m (9 cells): dx^2 + dy^2 <= 81, counted by brute force.
        int disc = 0;
        for (int dy = -12; dy <= 12; dy++)
            for (int dx = -12; dx <= 12; dx++)
                if (Vector2.Distance(Vector2.Zero, new Vector2(dx, dy) * MapConstants.CellSize) <= 18f) disc++;
        Assert.Equal(disc, FogMaps.Count(w, 0, VisionConstants.Visible));
        Assert.Equal(64 * 64 - disc, FogMaps.Count(w, 0, VisionConstants.Unexplored));
        Assert.True(w.Fog.IsVisible(0, 32 * 64 + 41)); // 18 m east, on the edge
        Assert.False(w.Fog.IsVisible(0, 32 * 64 + 42));
        Assert.True(w.Fog.IsExplored(0, 32 * 64 + 41));
        Assert.True(disc > 200);
    }

    [Fact]
    public void ABuilding_StampsFromItsFootprintCentre_WithItsSight()
    {
        Simulation sim = FlatSim();
        int tower = sim.World.Data.FindBuilding("malazan_watchtower");
        Assert.Equal(24f, sim.World.Data.Buildings[tower].Sight);
        EntityHandle b = GatherMaps.Building(sim, 30, 30, player: 0, type: tower);
        FogMaps.RunThroughNextUpdate(sim);
        World w = sim.World;
        FogMaps.AssertMatchesOracle(w, 0);
        // The 2 x 2 footprint at (30, 30) stamps from cell (31, 31): 24 m (12 cells) east of it is seen, 13 cells is not.
        Assert.True(w.Fog.IsVisible(0, 31 * 64 + 43));
        Assert.False(w.Fog.IsVisible(0, 31 * 64 + 44));
        Assert.True(w.Fog.IsVisible(0, 31 * 64 + 19));
        Assert.False(w.Fog.IsVisible(0, 31 * 64 + 18));
        Assert.True(w.Fog.CanSeeBuilding(0, b.Index));
        Assert.False(w.Fog.CanSeeBuilding(1, b.Index));
        // Every other building has rules.json's buildingSight (12 m).
        int keep = GatherMaps.Keep;
        Assert.Equal(12f, w.Data.Buildings[keep].Sight);
    }

    [Fact]
    public void AnEnemysUnits_NeverAddToAPlayersFog()
    {
        Simulation sim = FlatSim();
        Place(sim, 0, Crossbowman, FogMaps.Cell(10, 10));
        EntityHandle enemy = Place(sim, 1, Crossbowman, FogMaps.Cell(50, 50));
        sim.Tick();
        World w = sim.World;
        FogMaps.AssertMatchesOracle(w, 0);
        FogMaps.AssertMatchesOracle(w, 1);
        Assert.False(w.Fog.IsExplored(0, 50 * 64 + 50));
        Assert.True(w.Fog.IsVisible(1, 50 * 64 + 50));
        Assert.False(w.Fog.CanSeeUnit(0, enemy.Index));
        Assert.True(w.Fog.CanSeeUnit(1, enemy.Index));
    }

    [Fact]
    public void LeftCells_TurnExplored_AndStayExploredForTheMatch()
    {
        Simulation sim = FlatSim();
        EntityHandle h = Place(sim, 0, Crossbowman, FogMaps.Cell(12, 32));
        sim.Tick();
        World w = sim.World;
        int start = 32 * 64 + 12;
        Assert.True(w.Fog.IsVisible(0, start));
        sim.Enqueue(Command.Move(0, h, FogMaps.Cell(52, 32)));
        sim.Tick();
        sim.Tick(); // the Move applies
        for (int t = 0; t < 1000 && w.Units.State[h.Index] != UnitState.Idle; t++) sim.Tick();
        Assert.Equal(UnitState.Idle, w.Units.State[h.Index]);
        FogMaps.RunThroughNextUpdate(sim);
        Assert.True(VisionConstants.Explored == w.Fog.Visibility(0)[start], $"at {w.Units.Position[h.Index]}, tick {sim.TickNumber}, visible {FogMaps.Count(w, 0, VisionConstants.Visible)}");
        FogMaps.AssertMatchesOracle(w, 0);
        for (int t = 0; t < 200; t++) sim.Tick();
        Assert.Equal(VisionConstants.Explored, w.Fog.Visibility(0)[start]);
        // Every cell the walk passed is explored: the whole strip between the two ends along the row.
        for (int x = 12; x <= 52; x++) Assert.True(w.Fog.IsExplored(0, 32 * 64 + x), $"x {x}");
    }

    [Fact]
    public void AMoveBetweenUpdates_ShowsAtTheNextUpdateTick_AndNotBefore()
    {
        Simulation sim = FlatSim();
        EntityHandle h = Place(sim, 0, Crossbowman, FogMaps.Cell(12, 32));
        sim.Tick();
        sim.Tick(); // ticks 0-1; the update of tick 1 ran
        World w = sim.World;
        int far = 32 * 64 + 50;
        int version = w.Fog.Version(0);
        // A teleport between ticks 1 and 2 (a test seam): ticks 2, 3, 4 keep the old fog.
        w.Units.Position[h.Index] = w.Units.PrevPosition[h.Index] = FogMaps.Cell(50, 32);
        for (int t = 2; t <= 4; t++)
        {
            sim.Tick();
            Assert.False(w.Fog.IsVisible(0, far), $"tick {t}");
            Assert.True(w.Fog.IsVisible(0, 32 * 64 + 12), $"tick {t}");
            Assert.Equal(version, w.Fog.Version(0));
        }
        sim.Tick(); // tick 5 updates
        Assert.True(w.Fog.IsVisible(0, far));
        Assert.Equal(VisionConstants.Explored, w.Fog.Visibility(0)[32 * 64 + 12]);
        Assert.Equal(version + 1, w.Fog.Version(0));
        FogMaps.AssertMatchesOracle(w, 0);
    }

    [Fact]
    public void ADeadUnitsCircle_IsGoneAtTheNextUpdate()
    {
        Simulation sim = FlatSim();
        Place(sim, 0, Crossbowman, FogMaps.Cell(40, 40));
        EntityHandle doomed = Place(sim, 0, Crossbowman, FogMaps.Cell(12, 12));
        sim.Tick();
        sim.Tick(); // ticks 0-1
        World w = sim.World;
        Assert.True(w.Fog.IsVisible(0, 12 * 64 + 12));
        w.Units.Free(doomed);
        for (int t = 2; t <= 4; t++) sim.Tick();
        Assert.True(w.Fog.IsVisible(0, 12 * 64 + 12)); // not yet: the update is on tick 5
        sim.Tick();
        Assert.Equal(VisionConstants.Explored, w.Fog.Visibility(0)[12 * 64 + 12]);
        Assert.True(w.Fog.IsVisible(0, 40 * 64 + 40));
        FogMaps.AssertMatchesOracle(w, 0);
    }

    [Fact]
    public void TheInitialStamp_ExistsAtTick0_ForPlacedAndCommandSpawnedUnits()
    {
        Simulation sim = FlatSim();
        Place(sim, 0, Crossbowman, FogMaps.Cell(12, 12));
        sim.Enqueue(Command.SpawnUnit(1, Crossbowman, FogMaps.Cell(50, 50)));
        World w = sim.World;
        Assert.Equal(0, FogMaps.Count(w, 0, VisionConstants.Visible)); // nothing before the first tick
        sim.Tick(); // tick 0: the initial stamp, before its commands
        Assert.True(w.Fog.IsVisible(0, 12 * 64 + 12));
        Assert.Equal(1, w.Fog.Version(0));
        Assert.Equal(0, FogMaps.Count(w, 1, VisionConstants.Visible)); // a command queued before tick 0 applies in tick 1
        sim.Tick(); // tick 1: its spawn, then the first phase-12 update
        Assert.True(w.Fog.IsVisible(1, 50 * 64 + 50));
        Assert.Equal(2, w.Fog.Version(0));
        FogMaps.AssertMatchesOracle(w, 0);
        FogMaps.AssertMatchesOracle(w, 1);
    }

    [Fact]
    public void ManyUnitsOnOneCell_AndMixedRadii_MatchTheOracle()
    {
        // The per-cell "stamped with this radius" skip must never drop a bigger circle stamped after a smaller one.
        Simulation sim = FlatSim();
        Place(sim, 0, HeavyInfantry, FogMaps.Cell(30, 30, 0.2f));   // sight 14
        Place(sim, 0, Crossbowman, FogMaps.Cell(30, 30, 0.6f));     // sight 18, same cell, after
        Place(sim, 0, HeavyInfantry, FogMaps.Cell(30, 30, -0.4f));
        Place(sim, 0, Laborer, FogMaps.Cell(1, 1));                 // at the map's edge: the circle is clipped
        sim.Tick();
        FogMaps.AssertMatchesOracle(sim.World, 0);
        Assert.True(sim.World.Fog.IsVisible(0, 30 * 64 + 39));
    }

    [Fact]
    public void FogUpdates_AllocateNothing()
    {
        Simulation sim = FlatSim();
        for (int k = 0; k < 40; k++) Place(sim, k % 2, k % 3 == 0 ? Crossbowman : HeavyInfantry, FogMaps.Cell(5 + k, 10 + k % 7));
        for (int t = 0; t < 8; t++) sim.Tick();
        World w = sim.World;
        AllocationProbe.AssertZero(() =>
        {
            for (int k = 0; k < 8; k++) w.Fog.Update();
        });
    }
}
