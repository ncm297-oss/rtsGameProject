using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;
using static Rts.Sim.Tests.BuildMaps;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ProductionMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests;

/// <summary>M3-4 criteria 1-6: production queues, refunds, population and cap, spawn placement, rally points, locked types.</summary>
[Collection(SerialCollection.Name)]
public class ProductionTests
{
    /// <summary>A one-player sim on a flat 40 x 30 map with a Keep at (25, 10) (cap 10) and a Barracks at (10, 10), plenty to spend.</summary>
    private static (Simulation Sim, int Barracks, int Keep) Base(int players = 1, int units = 64)
    {
        Simulation sim = BuildMaps.NewSim(Flat(40, 30), units: units, players: players);
        int keep = Building(sim, 25, 10).Index;
        int barracks = Building(sim, 10, 10, type: ProductionMaps.Barracks).Index;
        SetTotals(sim, 0, 5000, 5000);
        return (sim, barracks, keep);
    }

    // ---------- criterion 1: queue, pay at queue time, train time, spawn on ring 1 ----------

    [Fact]
    public void Train_PaysAtQueueTime_AndSpawnsAfterExactlyItsTrainTicks_OnAFreeRing1Cell()
    {
        (Simulation sim, int bar, _) = Base();
        World w = sim.World;
        UnitDef hi = w.Data.Units[Infantry];
        Assert.Equal((54, 20, 280), (hi.CostGold, hi.CostWood, hi.TrainTicks)); // round(14 s x 20)
        int units = w.Units.Count;
        sim.Enqueue(Command.Train(0, In(sim, bar), Infantry));
        sim.Tick();
        Assert.Equal((5000, 5000), (w.Gold[0], w.Wood[0]));
        sim.Tick(); // applies: paid, and the item starts training this tick
        Assert.Equal((5000 - 54, 5000 - 20), (w.Gold[0], w.Wood[0]));
        Assert.Equal((1, 1), (w.Buildings.QueueCount[bar], w.Buildings.Progress[bar]));
        Run(sim, 278);
        Assert.Equal(units, w.Units.Count);
        Assert.Equal(279, w.Buildings.Progress[bar]);
        sim.Tick(); // the 280th tick
        Assert.Equal(units + 1, w.Units.Count);
        Assert.Equal((0, 0), (w.Buildings.QueueCount[bar], w.Buildings.Progress[bar]));
        int i = LiveUnits(w).Single(s => w.Units.TypeId[s] == Infantry);
        Assert.Equal(0, w.Units.Owner[i]);
        Assert.True(w.NavGrid.WorldToCell(w.Units.Position[i], out int x, out int y));
        Assert.Equal(1, RingOf(sim, bar, x, y));
        Assert.True(w.NavGrid.IsPassable(x, y));
        Assert.Equal(w.NavGrid.LevelAt(10, 10), w.NavGrid.LevelAt(x, y));
        Assert.Equal(w.NavGrid.CellCenter(x, y), w.Units.Position[i]);
        Assert.Equal(UnitState.Idle, w.Units.State[i]);
    }

    [Fact]
    public void FiveItemsQueue_TheSixthIsDropped_TotalsUnchanged()
    {
        (Simulation sim, int bar, _) = Base();
        World w = sim.World;
        for (int n = 0; n < 5; n++) sim.Enqueue(Command.Train(0, In(sim, bar), Infantry));
        Run(sim, 2);
        Assert.Equal(5, w.Buildings.QueueCount[bar]);
        Assert.Equal((5000 - 5 * 54, 5000 - 5 * 20), (w.Gold[0], w.Wood[0]));
        Assert.False(w.CanTrain(0, bar, Infantry, out TrainError why));
        Assert.Equal(TrainError.QueueFull, why);
        Apply(sim, Command.Train(0, In(sim, bar), Infantry));
        Assert.Equal(5, w.Buildings.QueueCount[bar]);
        Assert.Equal((5000 - 5 * 54, 5000 - 5 * 20), (w.Gold[0], w.Wood[0]));
    }

    [Fact]
    public void Train_TheQueueDrains_OneUnitPerTrainTime_NextStartsTheTickThePreviousSpawns()
    {
        (Simulation sim, int bar, _) = Base();
        World w = sim.World;
        for (int n = 0; n < 3; n++) sim.Enqueue(Command.Train(0, In(sim, bar), Infantry));
        sim.Tick();
        int start = w.Units.Count;
        Run(sim, 3 * 280);
        Assert.Equal(start + 3, w.Units.Count);
        Assert.Equal(0, w.Buildings.QueueCount[bar]);
        var cells = LiveUnits(w).Select(i => w.Units.Position[i]).ToList();
        Assert.Equal(cells.Count, cells.Distinct().Count());
    }

    [Fact]
    public void CannotAfford_IsDropped()
    {
        (Simulation sim, int bar, _) = Base();
        SetTotals(sim, 0, 53, 1000);
        Apply(sim, Command.Train(0, In(sim, bar), Infantry));
        Assert.Equal(0, sim.World.Buildings.QueueCount[bar]);
        Assert.Equal((53, 1000), (sim.World.Gold[0], sim.World.Wood[0]));
    }

    // ---------- criterion 2: cancel refunds in full ----------

    [Fact]
    public void CancelTrain_OfAQueuedItem_RefundsInFull_AndLaterItemsShiftDown_TheHeadKeepsItsProgress()
    {
        (Simulation sim, int bar, _) = Base();
        World w = sim.World;
        for (int n = 0; n < 3; n++) sim.Enqueue(Command.Train(0, In(sim, bar), Infantry));
        Run(sim, 12);
        int progress = w.Buildings.Progress[bar];
        (int gold, int wood) = (w.Gold[0], w.Wood[0]);
        sim.Enqueue(Command.CancelTrain(0, In(sim, bar), 1));
        sim.Tick();
        sim.Tick(); // applies
        Assert.Equal((gold + 54, wood + 20), (w.Gold[0], w.Wood[0]));
        Assert.Equal(2, w.Buildings.QueueCount[bar]);
        Assert.Equal(progress + 2, w.Buildings.Progress[bar]);
        Assert.Equal(-1, w.Buildings.QueueTypeAt(bar, 2));
        Assert.Equal(0, w.Buildings.QueueEntry(bar, 2)); // the freed entry is default again
    }

    [Fact]
    public void RemovingAMiddleItem_ShiftsTheLaterTypesDown()
    {
        (Simulation sim, int bar, _) = Base();
        BuildingStore b = sim.World.Buildings;
        // Raw store entries (no cost taken here, so the refunds raise the totals): the order of types is what's checked.
        b.Enqueue(bar, Infantry);
        b.Enqueue(bar, Laborer);
        b.Enqueue(bar, Lancer);
        b.RemoveQueued(bar, 1);
        Assert.Equal((2, Infantry, Lancer), (b.QueueCount[bar], b.QueueTypeAt(bar, 0), b.QueueTypeAt(bar, 1)));
        Assert.Equal(0, b.QueueEntry(bar, 2));
    }

    [Fact]
    public void CancelTrain_OfTheHeadInProgress_RefundsInFull_LosesItsProgress_AndReleasesItsReservation()
    {
        (Simulation sim, int bar, _) = Base();
        World w = sim.World;
        sim.Enqueue(Command.Train(0, In(sim, bar), Infantry));
        sim.Enqueue(Command.Train(0, In(sim, bar), Infantry));
        Run(sim, 50);
        Assert.Equal(49, w.Buildings.Progress[bar]);
        Assert.Equal(2, w.HalfPop[0]); // the head's reservation; the second hasn't started
        (int gold, int wood) = (w.Gold[0], w.Wood[0]);
        sim.Enqueue(Command.CancelTrain(0, In(sim, bar), 0));
        sim.Tick();
        sim.Tick(); // applies
        Assert.Equal((gold + 54, wood + 20), (w.Gold[0], w.Wood[0]));
        Assert.Equal(1, w.Buildings.QueueCount[bar]);
        // The second item became the head and started afresh in the same tick (phase 3 after phase 1).
        Assert.Equal(1, w.Buildings.Progress[bar]);
        Assert.Equal(2, w.HalfPop[0]);
        Apply(sim, Command.CancelTrain(0, In(sim, bar), 0));
        Assert.Equal((0, 0, 0), (w.Buildings.QueueCount[bar], w.Buildings.Progress[bar], w.HalfPop[0]));
        Assert.Equal((5000, 5000), (w.Gold[0], w.Wood[0]));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(1)]   // past the count: an empty slot
    [InlineData(-1)]
    public void CancelTrain_OfABadIndex_IsDropped(int index)
    {
        (Simulation sim, int bar, _) = Base();
        World w = sim.World;
        Apply(sim, Command.Train(0, In(sim, bar), Infantry));
        (int gold, int wood, int progress) = (w.Gold[0], w.Wood[0], w.Buildings.Progress[bar]);
        Apply(sim, Command.CancelTrain(0, In(sim, bar), index));
        Assert.Equal((gold, wood, 1), (w.Gold[0], w.Wood[0], w.Buildings.QueueCount[bar]));
        Assert.Equal(progress + 2, w.Buildings.Progress[bar]);
    }

    [Fact]
    public void CancelTrain_OnAnotherPlayersBuilding_IsDropped()
    {
        (Simulation sim, int bar, _) = Base(players: 2);
        Apply(sim, Command.Train(0, In(sim, bar), Infantry));
        Apply(sim, Command.CancelTrain(1, In(sim, bar), 0));
        Assert.Equal(1, sim.World.Buildings.QueueCount[bar]);
    }

    // ---------- criterion 3: population and cap ----------

    [Fact]
    public void ATownHallGivesCap10_FiveSpawnedWorkersMakePop5_ALancerCounts4HalfPop()
    {
        Simulation sim = BuildMaps.NewSim(Flat(40, 30), units: 64);
        World w = sim.World;
        Assert.Equal((0, 0), (w.HalfPop[0], w.HalfPopCap[0]));
        Building(sim, 25, 10);
        Assert.Equal(20, w.HalfPopCap[0]);
        for (int n = 0; n < 5; n++) sim.Enqueue(Command.SpawnUnit(0, Laborer, At(sim, 3 + n, 3)));
        Run(sim, 2);
        Assert.Equal(10, w.HalfPop[0]);
        Unit(sim, At(sim, 3, 6), type: Lancer);
        Assert.Equal(14, w.HalfPop[0]);
    }

    [Fact]
    public void TrainingPauses_WithTheHeadUnstarted_WhenTheCapIsFull_AndResumesWhenAHouseStands()
    {
        Simulation sim = BuildMaps.NewSim(Flat(40, 30), units: 64);
        World w = sim.World;
        int keep = Building(sim, 25, 10).Index;
        SetTotals(sim, 0, 5000, 5000);
        for (int n = 0; n < 10; n++) sim.Enqueue(Command.SpawnUnit(0, Laborer, At(sim, 3 + n, 3)));
        Run(sim, 2);
        Assert.Equal((20, 20), (w.HalfPop[0], w.HalfPopCap[0]));
        Apply(sim, Command.Train(0, In(sim, keep), Laborer));
        Run(sim, 300);
        Assert.Equal((1, 0), (w.Buildings.QueueCount[keep], w.Buildings.Progress[keep]));
        Assert.Equal(20, w.HalfPop[0]);
        // A House (+8) placed by the dev command: phase 1, so production starts in the same tick.
        sim.Enqueue(Command.SpawnBuilding(0, House, At(sim, 5, 20)));
        sim.Tick();
        Assert.Equal(0, w.Buildings.Progress[keep]);
        sim.Tick();
        Assert.Equal(36, w.HalfPopCap[0]);
        Assert.Equal((1, 22), (w.Buildings.Progress[keep], w.HalfPop[0]));
    }

    [Fact]
    public void AHouseFinishedByItsBuilders_RaisesTheCap_AndTheWaitingHeadStartsTheNextTick()
    {
        Simulation sim = BuildMaps.NewSim(Flat(40, 30), units: 64);
        World w = sim.World;
        int keep = Building(sim, 25, 10).Index;
        SetTotals(sim, 0, 5000, 5000);
        for (int n = 0; n < 9; n++) sim.Enqueue(Command.SpawnUnit(0, Laborer, At(sim, 3 + n, 3)));
        Run(sim, 2);
        EntityHandle builder = Unit(sim, At(sim, 5, 18));
        Apply(sim, Command.Build(0, builder, House, At(sim, 5, 20)));
        int site = SiteAt(sim, 5, 20);
        Assert.True(w.Buildings.UnderConstruction[site]);
        Assert.Equal(20, w.HalfPopCap[0]); // a site provides nothing
        Apply(sim, Command.Train(0, In(sim, keep), Laborer));
        Assert.Equal(0, w.Buildings.Progress[keep]);
        int ticks = 0;
        while (w.Buildings.UnderConstruction[site] && ticks++ < 2000)
        {
            sim.Tick();
            if (w.Buildings.UnderConstruction[site]) Assert.Equal(0, w.Buildings.Progress[keep]);
        }
        Assert.Equal(36, w.HalfPopCap[0]);
        // Production (phase 3) runs before construction (phase 4): the head starts on the next tick.
        Assert.Equal(0, w.Buildings.Progress[keep]);
        sim.Tick();
        Assert.Equal(1, w.Buildings.Progress[keep]);
    }

    [Fact]
    public void TheCapNeverExceedsRulesPopCap_AndDevSpawnsPastItStillSpawnAndCount()
    {
        Simulation sim = BuildMaps.NewSim(Flat(60, 60), units: 128);
        World w = sim.World;
        Assert.Equal(200, w.Data.Rules.HalfPopCap);
        for (int n = 0; n < 12; n++) sim.Enqueue(Command.SpawnBuilding(0, Keep, At(sim, 3 + (n % 4) * 14, 3 + (n / 4) * 14)));
        Run(sim, 2);
        Assert.Equal(12, w.Buildings.Count);
        Assert.Equal(200, w.HalfPopCap[0]); // 12 x 20 = 240 provided
        Assert.Equal(240, w.Ledger.HalfPopProvided[0]);
        for (int n = 0; n < 105; n++) sim.Enqueue(Command.SpawnUnit(0, Laborer, At(sim, 2 + n % 50, 50 + n / 50)));
        Run(sim, 2);
        Assert.Equal(105, w.Units.Count);
        Assert.Equal(210, w.HalfPop[0]);
        Assert.Equal(RecountHalfPop(w, 0), w.HalfPop[0]);
    }

    [Fact]
    public void ABuildingDestroyed_LowersTheCap_RefundsItsQueue_ReleasesItsReservation_AndNothingDies()
    {
        (Simulation sim, int bar, int keep) = Base();
        World w = sim.World;
        for (int n = 0; n < 6; n++) sim.Enqueue(Command.SpawnUnit(0, Laborer, At(sim, 3 + n, 25)));
        for (int n = 0; n < 3; n++) sim.Enqueue(Command.Train(0, In(sim, bar), Infantry));
        sim.Enqueue(Command.Train(0, In(sim, keep), Laborer));
        Run(sim, 10);
        Assert.Equal(12 + 2 + 2, w.HalfPop[0]); // 6 workers, both heads started
        (int gold, int wood) = (w.Gold[0], w.Wood[0]);
        w.Buildings.Damage(w.Buildings.HandleOf(bar), 1_000_000);
        Assert.Equal((gold + 3 * 54, wood + 3 * 20), (w.Gold[0], w.Wood[0]));
        Assert.Equal(14, w.HalfPop[0]);
        w.Buildings.Damage(w.Buildings.HandleOf(keep), 1_000_000);
        Assert.Equal((0, 12), (w.HalfPopCap[0], w.HalfPop[0]));
        Assert.Equal((gold + 3 * 54 + 50, wood + 3 * 20), (w.Gold[0], w.Wood[0]));
        Assert.Equal(6, w.Units.Count);
        Run(sim, 5);
        Assert.Equal(6, w.Units.Count);
    }

    [Fact]
    public void UnitStoreFree_ReleasesTheUnitsPopulation()
    {
        Simulation sim = BuildMaps.NewSim(Flat(40, 30));
        EntityHandle lancer = Unit(sim, At(sim, 5, 5), type: Lancer);
        EntityHandle worker = Unit(sim, At(sim, 7, 5));
        Assert.Equal(6, sim.World.HalfPop[0]);
        sim.World.Units.Free(lancer);
        Assert.Equal(2, sim.World.HalfPop[0]);
        sim.World.Units.Free(worker);
        Assert.Equal(0, sim.World.HalfPop[0]);
    }

    [Fact]
    public void ALancerAtTheCorral_Reserves4HalfPop_WhenItStarts_NotWhenQueued()
    {
        Simulation sim = BuildMaps.NewSim(Flat(40, 30), units: 64);
        World w = sim.World;
        Building(sim, 25, 10);
        int corral = Building(sim, 10, 10, type: Corral).Index;
        SetTotals(sim, 0, 5000, 5000);
        for (int n = 0; n < 9; n++) sim.Enqueue(Command.SpawnUnit(0, Laborer, At(sim, 3 + n, 3)));
        Run(sim, 2);
        Assert.Equal(18, w.HalfPop[0]);
        Apply(sim, Command.Train(0, In(sim, corral), Lancer));
        Assert.Equal((0, 18), (w.Buildings.Progress[corral], w.HalfPop[0])); // 18 + 4 > 20: waits
        sim.World.Units.Free(new EntityHandle(0, sim.World.Units.Generation[0]));
        sim.Tick();
        Assert.Equal((1, 20), (w.Buildings.Progress[corral], w.HalfPop[0]));
    }

    // ---------- criterion 4: no free cell, no stacking ----------

    /// <summary>
    /// A <paramref name="size"/>-square level-0 map with an 8 x 8 level-1 plateau at (p, p) whose outer ring is cliff but
    /// for a ramp in the middle of its west side: open plateau cells are (p+1..p+6)^2 and (p, p+3). A Keep stands at
    /// (p+2, p+2), so its ring 1 is the 20 open cells round it and (p, p+3) is ring 2.
    /// </summary>
    public static (Simulation Sim, int Keep, List<(int X, int Y)> Free) PlateauKeep(int size, int p, int units = 64)
    {
        var rows = new string[size];
        for (int y = 0; y < size; y++)
        {
            var row = new char[size];
            for (int x = 0; x < size; x++) row[x] = x >= p && x <= p + 7 && y >= p && y <= p + 7 ? '1' : '0';
            if (y == p + 3) row[p - 1] = 'r';
            rows[y] = new string(row);
        }
        Simulation sim = BuildMaps.NewSim(FromRows(rows), units: units, players: 2, combat: false);
        int keep = Building(sim, p + 2, p + 2).Index;
        NavGrid g = sim.World.NavGrid;
        var free = new List<(int, int)>();
        for (int y = p; y <= p + 7; y++)
            for (int x = p; x <= p + 7; x++)
                if (g.IsPassable(x, y)) free.Add((x, y));
        return (sim, keep, free);
    }

    [Fact]
    public void ASpawnWithNoFreeCellOnTheLevel_Waits_ThenTakesTheCellThatFreesUp_OneUnitPerCell()
    {
        (Simulation sim, int keep, List<(int X, int Y)> free) = PlateauKeep(48, 20);
        World w = sim.World;
        Assert.Equal(21, free.Count); // 20 round the Keep + the ramp head
        // Player 1's units (they don't count toward player 0's population) on every free cell of the plateau.
        foreach ((int x, int y) in free) sim.Enqueue(Command.SpawnUnit(1, Raider, At(sim, x, y)));
        Run(sim, 2);
        SetTotals(sim, 0, 1000, 1000);
        sim.Enqueue(Command.Train(0, In(sim, keep), Laborer));
        sim.Enqueue(Command.Train(0, In(sim, keep), Laborer));
        int train = w.Data.Units[Laborer].TrainTicks;
        Run(sim, train + 50);
        Assert.Equal(21, w.Units.Count);
        Assert.Equal((2, train), (w.Buildings.QueueCount[keep], w.Buildings.Progress[keep]));
        Assert.Equal(2, w.HalfPop[0]); // still reserved
        // One cell frees up: the complete head spawns there next tick, the second starts; nothing stacks.
        int victim = LiveUnits(w).First(i => w.NavGrid.WorldToCell(w.Units.Position[i], out int x, out int y) && x == 23 && y == 21);
        w.Units.Free(new EntityHandle(victim, w.Units.Generation[victim]));
        sim.Tick();
        Assert.Equal(21, w.Units.Count);
        Assert.Equal((1, 1), (w.Buildings.QueueCount[keep], w.Buildings.Progress[keep]));
        Run(sim, train + 20);
        Assert.Equal(1, w.Buildings.QueueCount[keep]); // complete again, waiting
        var cells = new HashSet<(int, int)>();
        foreach (int i in LiveUnits(w))
        {
            Assert.True(w.NavGrid.WorldToCell(w.Units.Position[i], out int x, out int y));
            Assert.True(cells.Add((x, y)), $"two units in ({x}, {y})");
            Assert.True(w.NavGrid.IsPassable(x, y));
        }
    }

    [Fact]
    public void ManySpawnsRoundOneBuilding_NeverShareACell()
    {
        // A Keep training 5 workers and a Barracks next to it training 5 infantry, rings crowded by idle units.
        (Simulation sim, int bar, int keep) = Base(units: 128);
        World w = sim.World;
        Building(sim, 3, 3); Building(sim, 3, 20); // more cap
        for (int x = 8; x <= 14; x++) sim.Enqueue(Command.SpawnUnit(0, Laborer, At(sim, x, 9)));
        for (int n = 0; n < 5; n++)
        {
            sim.Enqueue(Command.Train(0, In(sim, keep), Laborer));
            sim.Enqueue(Command.Train(0, In(sim, bar), Infantry));
        }
        Run(sim, 5 * 280 + 10);
        Assert.Equal(0, w.Buildings.QueueCount[bar] + w.Buildings.QueueCount[keep]);
        var cells = new HashSet<(int, int)>();
        foreach (int i in LiveUnits(w))
        {
            Assert.True(w.NavGrid.WorldToCell(w.Units.Position[i], out int x, out int y));
            Assert.True(cells.Add((x, y)), $"two units in ({x}, {y})");
        }
    }

    // ---------- criterion 5: rally points ----------

    [Fact]
    public void ARalliedUnit_WalksToTheRallyPoint()
    {
        (Simulation sim, int bar, _) = Base();
        World w = sim.World;
        Vector2 rally = At(sim, 30, 25) + new Vector2(0.3f, -0.4f);
        Apply(sim, Command.SetRally(0, w.Buildings.Cell[bar] + 1, rally)); // any cell of the footprint
        Assert.True(w.Buildings.HasRally[bar]);
        Assert.Equal(rally, w.Buildings.RallyPosition[bar]);
        Apply(sim, Command.Train(0, In(sim, bar), Infantry));
        Run(sim, 280);
        int i = LiveUnits(w).Single(s => w.Units.TypeId[s] == Infantry);
        Assert.Equal(UnitState.Moving, w.Units.State[i]);
        Run(sim, 400);
        Assert.Equal(UnitState.Idle, w.Units.State[i]);
        Assert.True(Vector2.Distance(w.Units.Position[i], rally) <= MovementConstants.ArrivalDistance, $"at {w.Units.Position[i]}");
    }

    [Fact]
    public void AWorkerRalliedOntoAMine_EndsUpGatheringThatMine_AnInfantryWalksThereInstead()
    {
        (Simulation sim, int bar, int keep) = Base();
        World w = sim.World;
        EntityHandle mine = Spawn(w, ResourceMaps.Mine, 32, 22, 2500);
        Vector2 onMine = At(sim, 33, 23);
        Apply(sim, Command.SetRally(0, w.Buildings.Cell[keep], onMine));
        Apply(sim, Command.SetRally(0, w.Buildings.Cell[bar], onMine));
        Apply(sim, Command.Train(0, In(sim, keep), Laborer));
        Apply(sim, Command.Train(0, In(sim, bar), Infantry));
        Run(sim, 280);
        int worker = LiveUnits(w).Single(s => w.Units.TypeId[s] == Laborer);
        int soldier = LiveUnits(w).Single(s => w.Units.TypeId[s] == Infantry);
        Assert.Equal(mine, w.Units.GatherNode[worker]);
        Assert.Equal(default, w.Units.GatherNode[soldier]);
        Run(sim, 400);
        Assert.Equal(mine, w.Units.GatherNode[worker]);
        Assert.Contains(w.Units.State[worker], new[] { UnitState.Gathering, UnitState.Returning, UnitState.Moving });
        Assert.True(w.Units.Cargo[worker] > 0 || w.Gold[0] > 5000 - 50 - 54, "the worker never gathered");
        Assert.Equal(UnitState.Idle, w.Units.State[soldier]);
    }

    [Fact]
    public void ClearRally_LeavesTrainedUnitsIdleAtTheEdge()
    {
        (Simulation sim, int bar, _) = Base();
        World w = sim.World;
        Apply(sim, Command.SetRally(0, w.Buildings.Cell[bar], At(sim, 30, 25)));
        Apply(sim, Command.ClearRally(0, In(sim, bar)));
        Assert.Equal((false, Vector2.Zero), (w.Buildings.HasRally[bar], w.Buildings.RallyPosition[bar]));
        Apply(sim, Command.Train(0, In(sim, bar), Infantry));
        Run(sim, 290);
        int i = LiveUnits(w).Single(s => w.Units.TypeId[s] == Infantry);
        Assert.Equal((UnitState.Idle, -1), (w.Units.State[i], w.Units.GoalCell[i]));
        Assert.True(w.NavGrid.WorldToCell(w.Units.Position[i], out int x, out int y));
        Assert.Equal(1, RingOf(sim, bar, x, y));
    }

    [Fact]
    public void Rally_OnAnEnemysBuilding_ASite_NoBuilding_OrOffTheMap_IsDropped()
    {
        (Simulation sim, int bar, _) = Base(players: 2);
        World w = sim.World;
        int enemy = Building(sim, 3, 20, player: 1, type: HolyCamp).Index;
        EntityHandle builder = Unit(sim, At(sim, 20, 22));
        Apply(sim, Command.Build(0, builder, House, At(sim, 20, 24)));
        int site = SiteAt(sim, 20, 24);
        Vector2 target = At(sim, 30, 25);
        Apply(sim, Command.SetRally(0, w.Buildings.Cell[enemy], target));
        Apply(sim, Command.SetRally(0, w.Buildings.Cell[site], target));
        Apply(sim, Command.SetRally(0, BuildMaps.Cell(sim, 30, 2), target)); // open ground
        Apply(sim, Command.SetRally(0, -5, target));
        Apply(sim, Command.SetRally(0, w.Buildings.Cell[bar], new Vector2(-10f, 5f))); // target off the map
        for (int k = 0; k < w.Buildings.Capacity; k++) Assert.False(w.Buildings.HasRally[k]);
        // And Train on the site or the enemy's building is dropped too.
        Apply(sim, Command.Train(0, In(sim, site), Laborer));
        Apply(sim, Command.Train(0, In(sim, enemy), Laborer));
        Assert.Equal(0, w.Buildings.QueueCount[site] + w.Buildings.QueueCount[enemy]);
        Assert.Equal(TrainError.NoBuilding, Reason(w, 0, site, Laborer));
        Assert.Equal(TrainError.NoBuilding, Reason(w, 0, enemy, CampFollower));
    }

    // ---------- criterion 6: locked, wrong building, wrong faction ----------

    private static TrainError Reason(World w, int player, int slot, int type)
    {
        w.CanTrain(player, slot, type, out TrainError why);
        return why;
    }

    [Fact]
    public void LockedTypes_TypesTrainedElsewhere_AndOtherFactionsTypes_AreDropped_TotalsUnchanged()
    {
        (Simulation sim, int bar, int keep) = Base(players: 2);
        World w = sim.World;
        int yard = Building(sim, 18, 20, type: EngineersYard).Index;
        int camp = Building(sim, 3, 20, player: 1, type: RaiderCamp).Index;
        Building(sim, 30, 22, player: 1, type: HolyCamp);
        SetTotals(sim, 1, 5000, 5000);
        Assert.Equal(new[] { "age_ii" }, w.Data.Units[Sapper].Requires);
        Assert.Equal(TrainError.LockedByRequirement, Reason(w, 0, yard, Sapper));
        Assert.Equal(TrainError.LockedByRequirement, Reason(w, 1, camp, Zealot));
        Assert.Equal(TrainError.NotTrainedHere, Reason(w, 0, bar, Laborer));
        Assert.Equal(TrainError.WrongFaction, Reason(w, 1, camp, Laborer));
        Assert.Equal(TrainError.UnknownType, Reason(w, 0, bar, w.Data.Units.Length));
        Assert.Equal(TrainError.None, Reason(w, 1, camp, Raider));
        sim.Enqueue(Command.Train(0, In(sim, yard), Sapper));
        sim.Enqueue(Command.Train(1, In(sim, camp), Zealot));
        sim.Enqueue(Command.Train(0, In(sim, bar), Laborer));
        sim.Enqueue(Command.Train(1, In(sim, camp), Laborer));
        sim.Enqueue(Command.Train(0, In(sim, keep), -1));
        sim.Enqueue(Command.Train(0, In(sim, keep), 9999));
        Run(sim, 2);
        for (int k = 0; k < w.Buildings.Capacity; k++) Assert.Equal(0, w.Buildings.QueueCount[k]);
        Assert.Equal((5000, 5000, 5000, 5000), (w.Gold[0], w.Wood[0], w.Gold[1], w.Wood[1]));
    }

    [Fact]
    public void UnitsTrainedAt_ListsEachBuildingsUnits_Sorted()
    {
        GameData d = TestSim.Data;
        Assert.Equal(new[] { Laborer }, d.UnitsTrainedAt(Keep));
        Assert.Equal(new[] { Infantry }, d.UnitsTrainedAt(ProductionMaps.Barracks));
        int[] yard = d.UnitsTrainedAt(EngineersYard).ToArray();
        Assert.Equal(new[] { d.FindUnit("malazan_catapult"), Sapper }.OrderBy(x => x), yard);
        Assert.Empty(d.UnitsTrainedAt(House));
        Assert.Empty(d.UnitsTrainedAt(-1));
        Assert.Empty(d.UnitsTrainedAt(d.Buildings.Length));
        Assert.Equal(d.Units.Length, Enumerable.Range(0, d.Buildings.Length).Sum(b => d.UnitsTrainedAt(b).Length));
    }

    [Fact]
    public void Commands_AreNotQueueable_AndMalformedPositionsAreDropped()
    {
        (Simulation sim, int bar, _) = Base();
        Assert.Throws<ArgumentException>(() => sim.Enqueue(Command.Train(0, In(sim, bar), Infantry) with { Flags = Command.QueuedFlag }));
        Apply(sim, Command.Train(0, new Vector2(float.NaN, 3f), Infantry));
        Apply(sim, Command.SetRally(0, sim.World.Buildings.Cell[bar], new Vector2(1f, float.PositiveInfinity)));
        Assert.Equal((0, false), (sim.World.Buildings.QueueCount[bar], sim.World.Buildings.HasRally[bar]));
    }
}
