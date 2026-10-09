using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Vision;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA attacks on M4-3b (session 2026-10-09-0125): towers against the high-ground vision rule on a hand-built three-level
/// map (a level-2 tower shoots down onto both lower levels; a level-1 tower ignores a level-2 unit beyond the lip and takes
/// one inside it); the last-known list under hostile sequences (seen, destroyed unseen, its slot reused elsewhere, seen
/// again; an Attack on the old handle; an Attack on a ghost whose slot now holds an own building); placement at the fog
/// edge (one unexplored cell of four) and the rule order around it; and an ordered Attack on a remembered building on a
/// plateau up a cliff, ordered from its foot.
/// </summary>
public class TowerGhostQaTests
{
    private const int Size = 40;

    /// <summary>
    /// 40 x 40: level 0 for x &lt; <paramref name="l1"/>, level 1 up to <paramref name="l2"/>, level 2 beyond (no ramp; each
    /// level's first column is a cliff). Only the largest band stays passable (the map is sealed to one region at load), so
    /// a scene builds its tower there and places its units straight into the store elsewhere: vision doesn't read passability.
    /// </summary>
    private static Heightmap ThreeLevel(int l1 = 14, int l2 = 18)
    {
        var levels = new byte[Size * Size];
        var elevations = new float[Size * Size];
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int c = y * Size + x;
                levels[c] = (byte)(x >= l2 ? 2 : x >= l1 ? 1 : 0);
                elevations[c] = levels[c] * MapConstants.LevelHeight;
            }
        return new Heightmap(Size, Size, levels, elevations);
    }

    private static int ShotsOf(World w, int owner)
    {
        int n = 0;
        for (int k = 0; k < w.Projectiles.Capacity; k++)
            if (w.Projectiles.Alive[k] && w.Projectiles.Owner[k] == owner && w.Projectiles.AttackerIsBuilding[k]) n++;
        return n;
    }

    private static EntityHandle Holder(Simulation sim, int owner, int type, Vector2 at)
    {
        EntityHandle h = Place(sim, owner, type, at);
        sim.World.Units.Hold[h.Index] = true;
        return h;
    }

    [Fact]
    public void Level2Tower_ShootsDownOnALevel0Unit()
    {
        Simulation sim = FogMaps.Sim(ThreeLevel());
        World w = sim.World;
        TowerGhostQaTests_Place(sim, 0, TowerTests.Watchtower, 19, 30); // level 2, footprint x 38-42 m, centre (40, 62)
        EntityHandle low = Holder(sim, 1, TowerTests.CampFollower, FogMaps.Cell(12, 31)); // level 0, 13 m from the footprint
        Assert.Equal(2, w.Fog.LevelAt(new Vector2(40f, 62f)));
        Assert.Equal(0, w.Fog.LevelAt(w.Units.Position[low.Index]));
        int hp = w.Units.Hp[low.Index];
        RunUntil(sim, () => w.Units.Hp[low.Index] < hp || !w.Units.IsAlive(low), 120);
        Assert.True(!w.Units.IsAlive(low) || w.Units.Hp[low.Index] < hp, "a level-2 tower never hit a level-0 unit 13 m below it");
    }

    [Fact]
    public void Level1Tower_IgnoresALevel2UnitBeyondTheLip_ForAWholeMinute()
    {
        Simulation sim = FogMaps.Sim(ThreeLevel(l1: 4, l2: 24));
        World w = sim.World;
        EntityHandle tower = TowerGhostQaTests_Place(sim, 0, TowerTests.Watchtower, 21, 30); // level 1, x 42-46 m, centre (44, 62)
        EntityHandle high = Holder(sim, 1, TowerTests.CampFollower, FogMaps.Cell(27, 31)); // level 2, 9 m from the footprint, 11 m from its centre
        Assert.Equal(1, w.Fog.BuildingLevel(tower.Index));
        for (int t = 0; t < 1200; t++)
        {
            sim.Tick();
            Assert.Equal(0, ShotsOf(w, 0));
            Assert.Equal(default, w.Buildings.TowerTarget[tower.Index]);
        }
        Assert.Equal(w.Data.Units[TowerTests.CampFollower].Hp, w.Units.Hp[high.Index]);
        // Control: a unit on the tower's own level, as near, is taken at the next scan.
        EntityHandle level = Holder(sim, 1, TowerTests.CampFollower, FogMaps.Cell(18, 31)); // level 1, 5 m west of the footprint
        RunUntil(sim, () => w.Buildings.TowerTarget[tower.Index] == level, 8);
        Assert.Equal(level, w.Buildings.TowerTarget[tower.Index]);
    }

    private static EntityHandle TowerGhostQaTests_Place(Simulation sim, int owner, int type, int x, int y, bool site = false) =>
        TowerTests.PlaceBuilding(sim, owner, type, x, y, site);

    /// <summary>A flat 64 map: player 1's Tent at cells (30, 30) seen once by player 0's scout, the scout then gone and the fog updated (the Tent is a ghost).</summary>
    private static (Simulation Sim, EntityHandle Tent) Ghosted()
    {
        Simulation sim = Flat(size: 64);
        World w = sim.World;
        EntityHandle tent = TowerTests.PlaceBuilding(sim, 1, TowerTests.Tent, 30, 30);
        EntityHandle scout = Spotter(sim, 0, new Vector2(55f, 62f));
        Assert.True(w.Fog.Ghosts(0)[tent.Index].Known);
        w.Units.Free(scout);
        FogMaps.RunThroughNextUpdate(sim);
        Assert.False(w.Fog.CanSeeBuilding(0, tent.Index));
        Assert.True(w.Fog.Ghosts(0)[tent.Index].Known);
        return (sim, tent);
    }

    [Fact]
    public void Ghost_DestroyedUnseen_SlotReusedElsewhereUnseen_OldGhostKept_AttackOnTheOldHandleEnds_NewOneUntouched_ThenSeenAgain()
    {
        (Simulation sim, EntityHandle tent) = Ghosted();
        World w = sim.World;
        Assert.True(w.Buildings.Free(tent));
        EntityHandle again = TowerTests.PlaceBuilding(sim, 1, TowerTests.Tent, 10, 50); // same slot, next generation, far away
        Assert.Equal(tent.Index, again.Index);
        FogMaps.RunThroughNextUpdate(sim);
        BuildingGhost g = w.Fog.Ghosts(0)[tent.Index];
        Assert.Equal(new BuildingGhost(tent.Generation, TowerTests.Tent, 30 * 64 + 30, 1), g);

        // An ordered Attack on the old handle: accepted, walks to the old ground, ends there; the new Tent is never hit.
        EntityHandle hi = Place(sim, 0, HeavyInfantry, new Vector2(20f, 62f));
        sim.Enqueue(Command.Attack(0, hi, tent, isBuilding: true));
        sim.Tick();
        sim.Tick();
        Assert.Equal(tent, w.Units.Target[hi.Index]);
        int newHp = w.Buildings.Hp[again.Index];
        int ran = RunUntil(sim, () => w.Units.Target[hi.Index].Generation == 0, 600);
        Assert.True(ran < 600, "the order on a gone building never ended");
        Assert.Equal(newHp, w.Buildings.Hp[again.Index]);
        FogMaps.RunThroughNextUpdate(sim);
        Assert.False(w.Fog.Ghosts(0)[tent.Index].Known);
        Assert.Equal(0, w.Fog.GhostCount(0));

        // The new Tent seen: listed with its generation and cell.
        Spotter(sim, 0, new Vector2(18f, 102f));
        Assert.Equal(new BuildingGhost(again.Generation, TowerTests.Tent, 50 * 64 + 10, 1), w.Fog.Ghosts(0)[again.Index]);
        Assert.Equal(1, w.Fog.GhostCount(0));
    }

    [Fact]
    public void Ghost_ReplacedByASeenNewBuildingInItsSlot_AnOrderOnTheOldHandleEndsAtTheNextPhase7()
    {
        (Simulation sim, EntityHandle tent) = Ghosted();
        World w = sim.World;
        EntityHandle hi = Place(sim, 0, HeavyInfantry, new Vector2(20f, 20f));
        Assert.True(w.Buildings.Free(tent));
        sim.Enqueue(Command.Attack(0, hi, tent, isBuilding: true));
        sim.Tick();
        sim.Tick();
        Assert.Equal(tent, w.Units.Target[hi.Index]);
        // The slot reused by a new Tent right beside the Heavy Infantry (it sees it at once).
        EntityHandle again = TowerTests.PlaceBuilding(sim, 1, TowerTests.Tent, 14, 10);
        Assert.Equal(tent.Index, again.Index);
        FogMaps.RunThroughNextUpdate(sim);
        Assert.Equal(again.Generation, w.Fog.Ghosts(0)[again.Index].Generation);
        sim.Tick();
        // The old order is over (the player can't target what it no longer remembers); no swing at the old handle.
        Assert.NotEqual(tent, w.Units.Target[hi.Index]);
        Assert.Equal(0, w.Units.WindupTicks[hi.Index] > 0 && w.Units.Target[hi.Index] == tent ? 1 : 0);
    }

    [Fact]
    public void Ghost_WhoseSlotNowHoldsAnOwnBuilding_AttackIsAccepted_EndsCleanly_TheOwnBuildingIsNeverHit()
    {
        (Simulation sim, EntityHandle tent) = Ghosted();
        World w = sim.World;
        Assert.True(w.Buildings.Free(tent));
        int billet = w.Data.FindBuilding("malazan_billet");
        EntityHandle own = TowerTests.PlaceBuilding(sim, 0, billet, 6, 6); // player 0's own, in the enemy Tent's old slot
        Assert.Equal(tent.Index, own.Index);
        FogMaps.RunThroughNextUpdate(sim);
        Assert.True(w.Fog.Ghosts(0)[tent.Index].Known, "the ghost went without its ground being seen");
        Assert.Equal(tent.Generation, w.Fog.Ghosts(0)[tent.Index].Generation);

        EntityHandle hi = Place(sim, 0, HeavyInfantry, new Vector2(20f, 62f));
        sim.Enqueue(Command.Attack(0, hi, tent, isBuilding: true));
        // An Attack naming the own building's live handle is refused.
        EntityHandle hi2 = Place(sim, 0, HeavyInfantry, new Vector2(20f, 30f));
        sim.Enqueue(Command.Attack(0, hi2, own, isBuilding: true));
        sim.Tick();
        sim.Tick();
        Assert.Equal(tent, w.Units.Target[hi.Index]);
        Assert.Equal(default, w.Units.Target[hi2.Index]);
        int hp = w.Buildings.Hp[own.Index];
        int ran = 0;
        for (; ran < 600 && w.Units.Target[hi.Index].Generation != 0; ran++)
        {
            sim.Tick();
            Assert.True(w.Units.WindupTicks[hi.Index] == 0 && w.Units.State[hi.Index] != UnitState.Attacking, $"tick {ran}: it swings at a gone building");
            Assert.Equal(hp, w.Buildings.Hp[own.Index]);
        }
        Assert.True(ran < 600, "the order never ended");
        // It walked to the ghost's ground (near (62, 62)), not to the own Billet at (12-16, 12-16).
        Assert.True(w.Units.Position[hi.Index].X > 40f, $"it stopped at {w.Units.Position[hi.Index]}");
        Assert.Equal(hp, w.Buildings.Hp[own.Index]);
    }

    /// <summary>A flat 64 map, player 0's Laborer at cell (10, 10) for one fog update, then gone (explored, not visible).</summary>
    private static Simulation Explored10()
    {
        Simulation sim = Flat(size: 64);
        sim.World.Ledger.Gold[0] = 100_000;
        sim.World.Ledger.Wood[0] = 100_000;
        EntityHandle lab = Place(sim, 0, Laborer, At(sim, 10, 10));
        sim.World.Fog.Update();
        sim.World.Units.Free(lab);
        FogMaps.RunThroughNextUpdate(sim);
        return sim;
    }

    [Fact]
    public void Placement_AtTheFogEdge_OneUnexploredCellOfFour_IsUnexplored_AllExploredButNotVisible_IsAllowed()
    {
        Simulation sim = Explored10();
        World w = sim.World;
        int billet = w.Data.FindBuilding("malazan_billet");
        int width = w.NavGrid.Width;
        Assert.Equal(2, w.Data.Buildings[billet].FootprintWidth);
        Assert.Equal(2, w.Data.Buildings[billet].FootprintHeight);
        int edge = 0, inside = 0;
        for (int y = 1; y < 30; y++)
            for (int x = 1; x < 30; x++)
            {
                int unexplored = 0;
                for (int dy = 0; dy < 2; dy++)
                    for (int dx = 0; dx < 2; dx++)
                        if (!w.Fog.IsExplored(0, (y + dy) * width + x + dx)) unexplored++;
                for (int dy = 0; dy < 2; dy++)
                    for (int dx = 0; dx < 2; dx++)
                        Assert.False(w.Fog.IsVisible(0, (y + dy) * width + x + dx));
                w.CanPlace(0, billet, y * width + x, out PlacementError e);
                if (unexplored == 1)
                {
                    edge++;
                    Assert.Equal(PlacementError.Unexplored, e);
                }
                else if (unexplored == 0)
                {
                    inside++;
                    Assert.Equal(PlacementError.None, e);
                }
                else Assert.Equal(PlacementError.Unexplored, e);
            }
        Assert.True(edge > 4 && inside > 4, $"edge {edge}, inside {inside}");
    }

    [Fact]
    public void Placement_RuleOrder_BlockedBeatsUnexplored_UnexploredBeatsUnitsInTheWayAndCost_AndABuildThereIsRefusedFree()
    {
        Simulation sim = Explored10();
        World w = sim.World;
        int billet = w.Data.FindBuilding("malazan_billet");
        int width = w.NavGrid.Width;
        // An unexplored anchor far away (40, 40), an enemy unit standing in it, and no money: Unexplored.
        int far = 40 * width + 40;
        Assert.False(w.Fog.IsExplored(0, far));
        Place(sim, 1, Raider, At(sim, 40, 40, 1f, 1f));
        w.Ledger.Gold[0] = 0;
        w.Ledger.Wood[0] = 0;
        w.CanPlace(0, billet, far, out PlacementError e);
        Assert.Equal(PlacementError.Unexplored, e);
        // The map's blocked ring (cell 0, 0), unexplored: Blocked first.
        w.CanPlace(0, billet, 0, out e);
        Assert.Equal(PlacementError.Blocked, e);
        // Off the map beats everything.
        w.CanPlace(0, billet, 63 * width + 63, out e);
        Assert.Equal(PlacementError.OffMap, e);

        // A Build there by a Laborer: refused, no site, nothing paid.
        w.Ledger.Gold[0] = 1000;
        w.Ledger.Wood[0] = 1000;
        EntityHandle lab = Place(sim, 0, Laborer, At(sim, 12, 12));
        sim.Enqueue(Command.Build(0, lab, billet, w.NavGrid.CellCenter(40, 40)));
        int before = w.Buildings.Count;
        for (int t = 0; t < 10; t++) sim.Tick();
        Assert.Equal(before, w.Buildings.Count);
        Assert.Equal(1000, w.Ledger.Gold[0]);
        Assert.Equal(1000, w.Ledger.Wood[0]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARangedOrderOnARememberedBuildingUpACliff_EndsWithin2Minutes(bool gone)
    {
        Simulation sim = FogMaps.Sim(FogMaps.TwoLevel());
        World w = sim.World;
        // Player 1's Tent on the plateau 4 cells in from the cliff (x 24-25, 48-52 m; y 32-33), seen once by a scout up there.
        EntityHandle tent = TowerTests.PlaceBuilding(sim, 1, TowerTests.Tent, 24, 32);
        EntityHandle scout = Spotter(sim, 0, FogMaps.Cell(23, 33));
        Assert.True(w.Fog.Ghosts(0)[tent.Index].Known);
        w.Units.Free(scout);
        FogMaps.RunThroughNextUpdate(sim);
        if (gone) Assert.True(w.Buildings.Free(tent));
        // A Crossbowman (range 15) below: at the cliff foot it stands within range of the footprint but can't see it.
        EntityHandle xb = Place(sim, 0, Crossbowman, FogMaps.Cell(10, 33));
        sim.Enqueue(Command.Attack(0, xb, tent, isBuilding: true));
        sim.Tick();
        sim.Tick();
        Assert.Equal(tent, w.Units.Target[xb.Index]);
        int ran = RunUntil(sim, () => w.Units.Target[xb.Index].Generation == 0, 2400);
        Assert.True(ran < 2400, $"after {ran} ticks the Crossbowman still holds the order at {w.Units.Position[xb.Index]} "
            + $"(mode {w.Units.Mode[xb.Index]}, state {w.Units.State[xb.Index]})");
    }
}
