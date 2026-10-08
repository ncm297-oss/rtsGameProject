using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;
using Xunit.Abstractions;
using static Rts.Sim.Tests.BuildMaps;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ProductionMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M3-4 (2026-10-07-0925): production queue edges (cancel at trainTicks - 1 and at 100 %, cancel during a pop pause,
/// a building destroyed with 5 queued, the cap dropping below used pop, a full unit store), spawn placement (20 halls
/// firing the same tick into packed rings in two slot orders, a full plateau with another plateau of the same level),
/// and rally edges (tree, depleted mine, own footprint, an infantry onto a mine).
/// </summary>
[Collection(SerialCollection.Name)]
public class ProductionQaTests
{
    private readonly ITestOutputHelper _out;

    public ProductionQaTests(ITestOutputHelper output) => _out = output;

    private static (Simulation Sim, int Barracks, int Keep) Base(int units = 64)
    {
        Simulation sim = BuildMaps.NewSim(Flat(40, 30), units: units, players: 2);
        int keep = Building(sim, 25, 10).Index;
        int barracks = Building(sim, 10, 10, type: ProductionMaps.Barracks).Index;
        SetTotals(sim, 0, 5000, 5000);
        return (sim, barracks, keep);
    }

    private static void AssertPopMatchesRecount(World w)
    {
        for (int p = 0; p < w.HalfPop.Length; p++)
            Assert.True(RecountHalfPop(w, p) == w.HalfPop[p], $"player {p}: HalfPop {w.HalfPop[p]}, recount {RecountHalfPop(w, p)}");
    }

    private static void AssertOneUnitPerCell(World w)
    {
        var cells = new HashSet<(int, int)>();
        foreach (int i in LiveUnits(w))
        {
            Assert.True(w.NavGrid.WorldToCell(w.Units.Position[i], out int x, out int y), $"unit {i} off the map");
            Assert.True(cells.Add((x, y)), $"two units in ({x}, {y})");
            Assert.True(w.NavGrid.IsPassable(x, y), $"unit {i} on blocked ({x}, {y})");
        }
    }

    // ---------- queue edges ----------

    [Fact]
    public void CancelHead_AtTrainTicksMinusOne_RefundsInFull_NoSpawn_NextItemStartsFresh()
    {
        (Simulation sim, int bar, _) = Base();
        World w = sim.World;
        int train = w.Data.Units[Infantry].TrainTicks;
        sim.Enqueue(Command.Train(0, In(sim, bar), Infantry));
        sim.Enqueue(Command.Train(0, In(sim, bar), Infantry));
        Run(sim, 2); // applied: progress 1
        Run(sim, train - 3); // progress train - 2
        Assert.Equal(train - 2, w.Buildings.Progress[bar]);
        int units = w.Units.Count;
        sim.Enqueue(Command.CancelTrain(0, In(sim, bar), 0));
        sim.Tick(); // progress train - 1, the cancel not applied yet
        Assert.Equal(train - 1, w.Buildings.Progress[bar]);
        sim.Tick(); // the cancel applies in phase 1 of the tick the head would have completed
        Assert.Equal(units, w.Units.Count);
        Assert.Equal((1, 1), (w.Buildings.QueueCount[bar], w.Buildings.Progress[bar]));
        Assert.Equal((5000 - 54, 5000 - 20), (w.Gold[0], w.Wood[0]));
        Assert.Equal(2, w.HalfPop[0]); // only the new head's reservation
        AssertPopMatchesRecount(w);
        Run(sim, train - 1);
        Assert.Equal(units + 1, w.Units.Count); // the second takes exactly its own train ticks
        Assert.Equal(0, w.Buildings.QueueCount[bar]);
    }

    [Fact]
    public void CancelHead_Complete_WaitingForACell_RefundsInFull_AndReleasesTheReservation()
    {
        (Simulation sim, int keep, List<(int X, int Y)> free) = ProductionTests.PlateauKeep(48, 20);
        World w = sim.World;
        foreach ((int x, int y) in free) sim.Enqueue(Command.SpawnUnit(1, Raider, At(sim, x, y)));
        Run(sim, 2);
        SetTotals(sim, 0, 1000, 1000);
        Apply(sim, Command.Train(0, In(sim, keep), Laborer));
        int train = w.Data.Units[Laborer].TrainTicks;
        Run(sim, train + 10);
        Assert.Equal((1, train), (w.Buildings.QueueCount[keep], w.Buildings.Progress[keep]));
        Assert.Equal(2, w.HalfPop[0]);
        Apply(sim, Command.CancelTrain(0, In(sim, keep), 0));
        Assert.Equal((0, 0), (w.Buildings.QueueCount[keep], w.Buildings.Progress[keep]));
        Assert.Equal((1000, 1000), (w.Gold[0], w.Wood[0]));
        Assert.Equal(0, w.HalfPop[0]);
        AssertPopMatchesRecount(w);
    }

    [Fact]
    public void CancelDuringAPopPause_RefundsInFull_PopUnchanged_TheNextStaysUnstarted()
    {
        (Simulation sim, int bar, _) = Base();
        World w = sim.World;
        for (int n = 0; n < 10; n++) sim.Enqueue(Command.SpawnUnit(0, Laborer, At(sim, 2 + n, 25)));
        Run(sim, 2);
        Assert.Equal((20, 20), (w.HalfPop[0], w.HalfPopCap[0]));
        sim.Enqueue(Command.Train(0, In(sim, bar), Infantry));
        sim.Enqueue(Command.Train(0, In(sim, bar), Infantry));
        Run(sim, 30);
        Assert.Equal((2, 0), (w.Buildings.QueueCount[bar], w.Buildings.Progress[bar]));
        (int g, int wd) = (w.Gold[0], w.Wood[0]);
        Apply(sim, Command.CancelTrain(0, In(sim, bar), 0));
        Assert.Equal((g + 54, wd + 20), (w.Gold[0], w.Wood[0]));
        Assert.Equal((1, 0), (w.Buildings.QueueCount[bar], w.Buildings.Progress[bar]));
        Assert.Equal(20, w.HalfPop[0]);
        AssertPopMatchesRecount(w);
    }

    [Fact]
    public void ABuildingDestroyedWithFiveQueued_RefundsAllFive_ReleasesTheReservation_AndTheSlotReusedStartsClean()
    {
        (Simulation sim, int bar, int keep) = Base();
        World w = sim.World;
        Apply(sim, Command.SetRally(0, w.Buildings.Cell[bar], At(sim, 30, 25)));
        for (int n = 0; n < 5; n++) sim.Enqueue(Command.Train(0, In(sim, bar), Infantry));
        Run(sim, 12);
        Assert.Equal(5, w.Buildings.QueueCount[bar]);
        Assert.True(w.Buildings.Progress[bar] > 0);
        Assert.Equal(2, w.HalfPop[0]);
        Assert.Equal((5000 - 5 * 54, 5000 - 5 * 20), (w.Gold[0], w.Wood[0]));
        w.Buildings.Damage(w.Buildings.HandleOf(bar), 1_000_000);
        Assert.False(w.Buildings.Alive[bar]);
        Assert.Equal((5000, 5000), (w.Gold[0], w.Wood[0]));
        Assert.Equal((0, 20), (w.HalfPop[0], w.HalfPopCap[0]));
        Assert.Equal((0, 0, false), (w.Buildings.QueueCount[bar], w.Buildings.Progress[bar], w.Buildings.HasRally[bar]));
        // The Keep destroyed with 5 workers queued: the cap goes to 0, the queue comes back.
        for (int n = 0; n < 5; n++) sim.Enqueue(Command.Train(0, In(sim, keep), Laborer));
        Run(sim, 5);
        Assert.Equal(5000 - 5 * 50, w.Gold[0]);
        w.Buildings.Damage(w.Buildings.HandleOf(keep), 1_000_000);
        Assert.Equal((5000, 0, 0), (w.Gold[0], w.HalfPop[0], w.HalfPopCap[0]));
        // A new building in the freed slot inherits nothing.
        int again = Building(sim, 10, 10, type: ProductionMaps.Barracks).Index;
        Assert.Equal((0, 0, false, Vector2.Zero), (w.Buildings.QueueCount[again], w.Buildings.Progress[again], w.Buildings.HasRally[again], w.Buildings.RallyPosition[again]));
        Run(sim, 400);
        Assert.Equal(0, w.Units.Count);
    }

    [Fact]
    public void TheCapDroppingBelowUsedPop_KillsNothing_TheStartedHeadFinishes_LaterItemsWait()
    {
        (Simulation sim, int bar, _) = Base();
        World w = sim.World;
        int house = Building(sim, 2, 2, type: House).Index;
        for (int n = 0; n < 10; n++) sim.Enqueue(Command.SpawnUnit(0, Laborer, At(sim, 2 + n, 25)));
        Run(sim, 2);
        Assert.Equal((20, 36), (w.HalfPop[0], w.HalfPopCap[0]));
        for (int n = 0; n < 5; n++) sim.Enqueue(Command.Train(0, In(sim, bar), Infantry));
        Run(sim, 2 + 280 * 2 + 10); // two trained, the third started
        int units = w.Units.Count;
        Assert.Equal(12, units);
        Assert.Equal(3, w.Buildings.QueueCount[bar]);
        Assert.Equal(26, w.HalfPop[0]);
        w.Buildings.Damage(w.Buildings.HandleOf(house), 1_000_000);
        Assert.Equal((26, 20), (w.HalfPop[0], w.HalfPopCap[0]));
        Run(sim, 600);
        Assert.Equal(units + 1, w.Units.Count); // the started head still finished; nothing died
        Assert.Equal((2, 0), (w.Buildings.QueueCount[bar], w.Buildings.Progress[bar]));
        Assert.Equal(26, w.HalfPop[0]);
        AssertPopMatchesRecount(w);
        _out.WriteLine($"cap dropped to 20 under 26 used: the started head finished (pop 26 / 20), 2 wait unstarted");
    }

    [Fact]
    public void AFullUnitStore_HoldsTheCompleteHead_WithItsReservation_ThenSpawnsWhenASlotFrees()
    {
        Simulation sim = BuildMaps.NewSim(Flat(40, 30), units: 4, players: 1);
        World w = sim.World;
        int keep = Building(sim, 20, 10).Index;
        SetTotals(sim, 0, 1000, 1000);
        for (int n = 0; n < 4; n++) sim.Enqueue(Command.SpawnUnit(0, Laborer, At(sim, 2 + n, 25)));
        Run(sim, 2);
        Assert.Equal(0, w.Units.FreeCount);
        Apply(sim, Command.Train(0, In(sim, keep), Laborer));
        int train = w.Data.Units[Laborer].TrainTicks;
        Run(sim, train + 20);
        Assert.Equal((1, train), (w.Buildings.QueueCount[keep], w.Buildings.Progress[keep]));
        Assert.Equal(10, w.HalfPop[0]);
        AssertPopMatchesRecount(w);
        w.Units.Free(new EntityHandle(0, w.Units.Generation[0]));
        sim.Tick();
        Assert.Equal((0, 4), (w.Buildings.QueueCount[keep], w.Units.Count));
        Assert.Equal(8, w.HalfPop[0]);
        AssertPopMatchesRecount(w);
    }

    // ---------- spawn placement ----------

    /// <summary>
    /// 20 Barracks in two rows of ten with 2-cell gaps (rings shared between neighbours), every passable cell of the 120 x 24
    /// map packed with player 1's units except <paramref name="freeCells"/> cells in the middle gap row; all 20 train
    /// one infantry queued the same tick, so they complete the same tick. <paramref name="reverse"/> places the halls in
    /// the opposite order, so slot order is reversed against position.
    /// </summary>
    private static (Simulation Sim, List<int> Halls, HashSet<int> Fill) TwentyHalls(bool reverse, int freeCells)
    {
        var sim = new Simulation(TestSim.ConfigNoCombat(Seed: 5, PlayerCount: 2, UnitCapacity: 3000, CommandCapacity: 4096), Flat(120, 24));
        World w = sim.World;
        NavGrid g = w.NavGrid;
        var anchors = new List<(int X, int Y)>();
        for (int row = 0; row < 2; row++)
            for (int k = 0; k < 10; k++) anchors.Add((3 + 5 * k, row == 0 ? 4 : 11));
        if (reverse) anchors.Reverse();
        foreach ((int x, int y) in anchors) sim.Enqueue(Command.SpawnBuilding(0, ProductionMaps.Barracks, At(sim, x, y)));
        // Two Keeps far right for the cap (40 half-pop).
        sim.Enqueue(Command.SpawnBuilding(0, Keep, At(sim, 100, 4)));
        sim.Enqueue(Command.SpawnBuilding(0, Keep, At(sim, 100, 14)));
        Run(sim, 2);
        var halls = new List<int>();
        foreach ((int x, int y) in anchors) halls.Add(SiteAt(sim, x, y));
        Assert.DoesNotContain(-1, halls);
        // The free cells: in the gap row y = 8 between the rows.
        var open = new HashSet<(int, int)>();
        for (int n = 0; n < freeCells; n++) open.Add((4 + 4 * n, 8));
        int spawned = 0;
        for (int y = 0; y < g.Height; y++)
            for (int x = 0; x < g.Width; x++)
                if (g.IsPassable(x, y) && !open.Contains((x, y)))
                {
                    sim.Enqueue(Command.SpawnUnit(1, Raider, At(sim, x, y)));
                    spawned++;
                }
        Run(sim, 2);
        var fill = new HashSet<int>(LiveUnits(w));
        Assert.Equal(spawned, fill.Count);
        SetTotals(sim, 0, 100_000, 100_000);
        foreach (int k in halls) sim.Enqueue(Command.Train(0, In(sim, k), Infantry));
        return (sim, halls, fill);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TwentyHallsCompletingTheSameTick_IntoPackedRings_NeverStack_InEitherSlotOrder(bool reverse)
    {
        (Simulation sim, List<int> halls, HashSet<int> fill) = TwentyHalls(reverse, freeCells: 12);
        World w = sim.World;
        sim.Tick();
        sim.Tick(); // applied, all started this tick
        Assert.All(halls, k => Assert.Equal(1, w.Buildings.Progress[k]));
        Run(sim, 278);
        Assert.Equal(fill.Count, w.Units.Count);
        sim.Tick(); // all 20 complete this tick; only 12 cells are free
        Assert.Equal(fill.Count + 12, w.Units.Count);
        Assert.Equal(8, halls.Count(k => w.Buildings.QueueCount[k] == 1));
        AssertOneUnitPerCell(w);
        foreach (int i in LiveUnits(w))
        {
            if (fill.Contains(i)) continue;
            Assert.True(w.NavGrid.WorldToCell(w.Units.Position[i], out int x, out int y));
            Assert.Equal(8, y); // only the gap row had room
        }
        Assert.Equal(12 * 2 + 8 * 2, w.HalfPop[0]); // 12 spawned + 8 started
        AssertPopMatchesRecount(w);
        // The fill goes: the 8 waiting spawn on the next tick, still one per cell.
        foreach (int i in fill) w.Units.Free(new EntityHandle(i, w.Units.Generation[i]));
        sim.Tick();
        Assert.Equal(20, w.Units.Count);
        Assert.All(halls, k => Assert.Equal(0, w.Buildings.QueueCount[k]));
        AssertOneUnitPerCell(w);
        foreach (int i in LiveUnits(w))
        {
            Assert.True(w.NavGrid.WorldToCell(w.Units.Position[i], out int x, out int y));
            Assert.True(halls.All(k => RingOf(sim, k, x, y) >= 1), $"({x}, {y}) inside a footprint");
        }
    }

    /// <summary>
    /// Two 8 x 8 level-1 plateaus far apart. The Keep's plateau is packed; the other one is empty. Brief: "if no cell is
    /// free, the item stays complete and retries" and "on the building's level". BUG-0097 (fixed M3-H2): the ring walk is
    /// bounded by the building's plateau, so the trained unit waits instead of appearing on the other plateau, and spawns
    /// on its own plateau once a cell there frees. (Was the pin `..._LandsOnAnotherPlateauOfTheSameLevel_Bug0097Pin`.)
    /// </summary>
    [Fact]
    public void ASpawnOnAFullPlateau_Waits_NeverLandsOnAnotherPlateauOfTheSameLevel_Bug0097()
    {
        (Simulation sim, int keep, List<int> fill) = TwoPlateaus();
        World w = sim.World;
        SetTotals(sim, 0, 1000, 1000);
        Apply(sim, Command.Train(0, In(sim, keep), Laborer));
        Run(sim, w.Data.Units[Laborer].TrainTicks + 5);
        Assert.DoesNotContain(LiveUnits(w), i => w.Units.Owner[i] == 0);
        Assert.Equal(1, w.Buildings.QueueCount[keep]); // complete, waiting
        // A cell on plateau A frees: the unit spawns there, next to the Keep.
        w.Units.Free(new EntityHandle(fill[0], w.Units.Generation[fill[0]]));
        Run(sim, 2);
        int trained = LiveUnits(w).Single(i => w.Units.Owner[i] == 0);
        Assert.True(w.NavGrid.WorldToCell(w.Units.Position[trained], out int tx, out int ty));
        _out.WriteLine($"Keep at (12, 12) on plateau A (full); after a cell freed the trained Laborer stands at ({tx}, {ty}), ring {RingOf(sim, keep, tx, ty)}");
        Assert.True(tx >= 10 && tx <= 17 && ty >= 10 && ty <= 17, $"({tx}, {ty}) is off plateau A");
        Assert.Equal(0, w.Buildings.QueueCount[keep]);
    }

    /// <summary>BUG-0097's push-out half: a House dropped on own units on the packed plateau A sets none of them down on plateau B.</summary>
    [Fact]
    public void APushOutOnAFullPlateau_NeverLandsOnAnotherPlateauOfTheSameLevel_Bug0097()
    {
        (Simulation sim, int _, List<int> fill) = TwoPlateaus(fillPlayer: 0, keep: false);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        SetTotals(sim, 0, 1000, 1000);
        // A House on plateau A, on top of the own units standing there.
        int house = BuildMaps.House, anchor = -1;
        for (int c = 10 * g.Width; c < 18 * g.Width && anchor < 0; c++)
            if (c % g.Width >= 10 && c % g.Width <= 17 && w.CanPlace(0, house, c, out _)) anchor = c;
        Assert.True(anchor >= 0, "no House anchor on plateau A");
        int ax = anchor % g.Width, ay = anchor / g.Width;
        Apply(sim, Command.Build(0, new EntityHandle(fill[0], w.Units.Generation[fill[0]]), house, At(sim, ax, ay)));
        Assert.True(SiteAt(sim, ax, ay) >= 0);
        foreach (int i in LiveUnits(w))
        {
            Assert.True(g.WorldToCell(w.Units.Position[i], out int x, out int y));
            Assert.False(x >= 40 && y >= 40, $"unit {i} set down on plateau B at ({x}, {y})");
        }
    }

    /// <summary>Plateau A (10-17, 10-17) with a Keep at (12, 12) (unless not <paramref name="keep"/>; -1 then) and every other passable cell holding a unit of <paramref name="fillPlayer"/>; plateau B (40-47, 40-47) empty; a ramp each.</summary>
    private static (Simulation Sim, int Keep, List<int> Fill) TwoPlateaus(int fillPlayer = 1, bool keep = true)
    {
        const int size = 64;
        var rows = new string[size];
        for (int y = 0; y < size; y++)
        {
            var row = new char[size];
            for (int x = 0; x < size; x++)
            {
                bool a = x >= 10 && x <= 17 && y >= 10 && y <= 17;
                bool b = x >= 40 && x <= 47 && y >= 40 && y <= 47;
                row[x] = a || b ? '1' : '0';
            }
            if (y == 13) row[9] = 'r';
            if (y == 43) row[39] = 'r';
            rows[y] = new string(row);
        }
        Simulation sim = BuildMaps.NewSim(FromRows(rows), units: 64, players: 2, combat: false);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        int keepSlot = keep ? Building(sim, 12, 12).Index : -1;
        var freeA = new List<(int, int)>();
        for (int y = 10; y <= 17; y++)
            for (int x = 10; x <= 17; x++)
                if (g.IsPassable(x, y)) freeA.Add((x, y));
        foreach ((int x, int y) in freeA) sim.Enqueue(Command.SpawnUnit(fillPlayer, fillPlayer == 0 ? Laborer : Raider, At(sim, x, y)));
        Run(sim, 2);
        return (sim, keepSlot, LiveUnits(w).ToList());
    }

    [Fact]
    public void ASpawnBesideAWalkerEnteringTheCell_StaysFinite_AndSeparates()
    {
        (Simulation sim, int bar, _) = Base();
        World w = sim.World;
        // Rally the Barracks so its spawn cell is the ring-1 cell nearest (14, 11); a laborer walks through that cell.
        Apply(sim, Command.SetRally(0, w.Buildings.Cell[bar], At(sim, 14, 11)));
        Apply(sim, Command.Train(0, In(sim, bar), Infantry));
        EntityHandle walker = Unit(sim, At(sim, 13, 4));
        Run(sim, 280 - 4 - 7);
        sim.Enqueue(Command.Move(0, walker, At(sim, 13, 20)));
        bool spawned = false;
        for (int t = 0; t < 300; t++)
        {
            sim.Tick();
            spawned |= w.Units.Count == 2;
            foreach (int i in LiveUnits(w))
                Assert.True(float.IsFinite(w.Units.Position[i].X) && float.IsFinite(w.Units.Position[i].Y));
        }
        Assert.True(spawned);
        int[] live = LiveUnits(w);
        Assert.NotEqual(w.Units.Position[live[0]], w.Units.Position[live[1]]);
    }

    // ---------- rally edges ----------

    [Fact]
    public void AWorkerRalliedOntoATree_Gathers_OntoADepletedMine_WalksThereIdle_NoGather()
    {
        (Simulation sim, int _, int keep) = Base();
        World w = sim.World;
        EntityHandle tree = Spawn(w, Tree, 34, 4, TreeWood);
        Apply(sim, Command.SetRally(0, w.Buildings.Cell[keep], At(sim, 34, 4)));
        Apply(sim, Command.Train(0, In(sim, keep), Laborer));
        Run(sim, w.Data.Units[Laborer].TrainTicks);
        int worker = LiveUnits(w).Single();
        Assert.Equal(tree, w.Units.GatherNode[worker]);
        // A mine emptied after the rally was set: the next worker just walks to the spot.
        EntityHandle mine = Spawn(w, Mine, 34, 20, 100);
        Apply(sim, Command.SetRally(0, w.Buildings.Cell[keep], At(sim, 35, 21)));
        w.Resources.Take(mine, 100);
        Assert.False(w.Resources.IsAlive(mine));
        Apply(sim, Command.Train(0, In(sim, keep), Laborer));
        Run(sim, w.Data.Units[Laborer].TrainTicks);
        int second = LiveUnits(w).Single(i => i != worker);
        _out.WriteLine($"rally on a depleted mine: state {w.Units.State[second]}, gather node {w.Units.GatherNode[second]}");
        Run(sim, 400);
        Assert.True(float.IsFinite(w.Units.Position[second].X));
        Assert.True(w.NavGrid.WorldToCell(w.Units.Position[second], out int x, out int y) && w.NavGrid.IsPassable(x, y));
        AssertPopMatchesRecount(w);
    }

    [Fact]
    public void ARallyInsideTheOwnFootprint_SendsTheUnitToANearbyPassableCell()
    {
        (Simulation sim, int bar, _) = Base();
        World w = sim.World;
        Apply(sim, Command.SetRally(0, w.Buildings.Cell[bar], In(sim, bar) + new Vector2(2f, 2f)));
        Apply(sim, Command.Train(0, In(sim, bar), Infantry));
        Run(sim, 280 + 200);
        int i = LiveUnits(w).Single();
        Assert.True(w.NavGrid.WorldToCell(w.Units.Position[i], out int x, out int y));
        Assert.True(w.NavGrid.IsPassable(x, y));
        Assert.True(RingOf(sim, bar, x, y) >= 1);
        Assert.Equal(UnitState.Idle, w.Units.State[i]);
    }

    [Fact]
    public void SetRally_ByTheCellOfAnEnemyBuilding_OrOfASite_IsDropped_EvenIfThePositionIsOwn()
    {
        (Simulation sim, int bar, _) = Base();
        World w = sim.World;
        int enemy = Building(sim, 2, 20, player: 1, type: RaiderCamp).Index;
        Apply(sim, Command.SetRally(0, w.Buildings.Cell[enemy], In(sim, bar)));
        Assert.False(w.Buildings.HasRally[enemy]);
        Assert.False(w.Buildings.HasRally[bar]);
        Apply(sim, Command.Train(0, In(sim, enemy), Infantry));
        Apply(sim, Command.CancelTrain(1, In(sim, bar), 0));
        Assert.Equal(0, w.Buildings.QueueCount[enemy]);
        Assert.Equal(5000, w.Gold[0]);
    }

    [Fact]
    public void TicksWithACompleteHeadWaitingForACell_AndTwentyHallsSpawningIntoPackedRings_AllocateNothing()
    {
        (Simulation sim, int keep, List<(int X, int Y)> free) = ProductionTests.PlateauKeep(48, 20);
        World w = sim.World;
        foreach ((int x, int y) in free) sim.Enqueue(Command.SpawnUnit(1, Raider, At(sim, x, y)));
        Run(sim, 2);
        SetTotals(sim, 0, 1000, 1000);
        Apply(sim, Command.Train(0, In(sim, keep), Laborer));
        Run(sim, w.Data.Units[Laborer].TrainTicks + 2);
        Assert.Equal(1, w.Buildings.QueueCount[keep]);
        AllocationProbe.AssertZero(() => Run(sim, 50), _out);
        Assert.Equal(1, w.Buildings.QueueCount[keep]);

        (Simulation big, List<int> halls, _) = TwentyHalls(reverse: true, freeCells: 12);
        Run(big, 2 + 270);
        AllocationProbe.AssertZero(() => Run(big, 20), _out); // the spawn tick and 19 ticks of 8 heads waiting
        Assert.Equal(8, halls.Count(k => big.World.Buildings.QueueCount[k] == 1));
    }

    // ---------- scale (report) ----------

    [Fact]
    [Trait("Category", "Perf")]
    public void TwentyHallsAllWaitingForACell_EachTick_Under0point3Ms()
    {
        // The packed scene with no free cell at all: every tick, 20 complete heads wait. Since M3-H2 (BUG-0097) the first
        // head fills the plateau's box from the unit store, walks it once and marks the plateau full for the tick; the
        // other 19 wait without a walk.
        (Simulation sim, List<int> halls, HashSet<int> fill) = TwentyHalls(reverse: false, freeCells: 0);
        World w = sim.World;
        Run(sim, 2 + 280);
        Assert.All(halls, k => Assert.Equal(1, w.Buildings.QueueCount[k]));
        for (int n = 0; n < 20; n++) ProductionSystem.Run(w);
        long t0 = Stopwatch.GetTimestamp();
        for (int n = 0; n < 200; n++) ProductionSystem.Run(w);
        double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency / 200;
        _out.WriteLine($"20 complete heads waiting on a packed 120 x 24 map ({fill.Count} units): ProductionSystem.Run {ms:F3} ms");
        Assert.Equal(fill.Count, w.Units.Count);
        // QA 2026-10-07-0925 measured 1.7-1.9 ms (Debug) when each head repeated its full-box walk; the brief's bound.
        Assert.True(ms < 0.3, $"{ms:F3} ms per tick of waiting spawns");
    }
}
