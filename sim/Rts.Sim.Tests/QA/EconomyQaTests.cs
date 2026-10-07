using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.Replays;
using Xunit.Abstractions;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M3-2, session 2026-10-06-1503): the gather loop attacked at its races and edges, independently of the
/// developer's suites: nodes dying under walkers / workers in reach / depositors, drop-offs appearing late,
/// two players on one mine, a mine on a plateau, cramped stand cells, the reach boundary, orders that end or
/// change the loop, buildings at the edges, the command door, crowding at 20 / 40 / 80 workers, and the
/// BUG-0073 measurement at real felling cadence.
/// </summary>
[Collection(SerialCollection.Name)]
public class EconomyQaTests
{
    private readonly ITestOutputHelper _out;

    public EconomyQaTests(ITestOutputHelper output) => _out = output;

    private static void AssertConserved(Simulation sim, (long Gold, long Wood) expected, string at)
    {
        (long g, long w) = Conserved(sim.World);
        Assert.True(g == expected.Gold && w == expected.Wood, $"{at}: gold {g} (want {expected.Gold}), wood {w} (want {expected.Wood})");
    }

    // ------------------------------------------------------------------ races

    [Fact]
    public void NodeDiesWhileAWorkerWalksToIt_TheWalkerRetargetsBeforeArriving_AndNothingIsLost()
    {
        Simulation sim = GatherMaps.NewSim(Flat(40, 20));
        EntityHandle t1 = Spawn(sim.World, Tree, 22, 9, 1);
        EntityHandle t2 = Spawn(sim.World, Tree, 24, 9, TreeWood);
        Building(sim, 2, 8);
        EntityHandle a = Unit(sim, At(sim, 21, 9));
        EntityHandle b = Unit(sim, At(sim, 8, 9));
        sim.Enqueue(Command.Gather(0, a, At(sim, 22, 9)));
        sim.Enqueue(Command.Gather(0, b, At(sim, 22, 9)));
        var total = Conserved(sim.World);
        UnitStore u = sim.World.Units;
        int t = 0;
        for (; t < 200 && sim.World.Resources.IsAlive(t1); t++)
        {
            sim.Tick();
            AssertConserved(sim, total, $"tick {sim.TickNumber}");
        }
        Assert.False(sim.World.Resources.IsAlive(t1));
        Assert.Equal(UnitState.Moving, u.State[b.Index]); // still on its way
        sim.Tick();
        Assert.Equal(t2, u.GatherNode[b.Index]);
        Assert.Equal(t2, u.GatherNode[a.Index]);
        for (int k = 0; k < 1200; k++)
        {
            sim.Tick();
            AssertConserved(sim, total, $"tick {sim.TickNumber}");
        }
        Assert.True(u.Cargo[b.Index] > 0 || sim.World.Wood[0] > 201, "the walker never worked the replacement tree");
    }

    [Fact]
    public void NodeDiesWhileTwoWorkersAreInReach_BothMoveToTheNextTree_WithExactConservation()
    {
        Simulation sim = GatherMaps.NewSim(Flat(40, 20));
        EntityHandle t1 = Spawn(sim.World, Tree, 20, 9, 3);
        EntityHandle t2 = Spawn(sim.World, Tree, 20, 13, TreeWood);
        Building(sim, 2, 8);
        EntityHandle a = Unit(sim, At(sim, 19, 9));
        EntityHandle b = Unit(sim, At(sim, 21, 9));
        sim.Enqueue(Command.Gather(0, a, At(sim, 20, 9)));
        sim.Enqueue(Command.Gather(0, b, At(sim, 20, 9)));
        var total = Conserved(sim.World);
        UnitStore u = sim.World.Units;
        for (int t = 0; t < 600; t++)
        {
            sim.Tick();
            AssertConserved(sim, total, $"tick {sim.TickNumber}");
        }
        Assert.False(sim.World.Resources.IsAlive(t1));
        Assert.Equal(3, u.Cargo[a.Index] + u.Cargo[b.Index] + (sim.World.Wood[0] - 200) - (TreeWood - sim.World.Resources.Remaining[t2.Index]));
        Assert.Equal(t2, u.GatherNode[a.Index]);
        Assert.Equal(t2, u.GatherNode[b.Index]);
        Assert.True(sim.World.Resources.Remaining[t2.Index] < TreeWood, "nobody worked the next tree");
    }

    [Fact]
    public void NodeDiesWhileItsWorkerIsAwayDepositing_TheDepositorGoesToTheMineWithin20m()
    {
        Simulation sim = GatherMaps.NewSim(Flat(40, 20));
        EntityHandle m1 = Spawn(sim.World, Mine, 14, 9, 2500);
        EntityHandle m2 = Spawn(sim.World, Mine, 20, 9, 2500); // centers 12 m apart
        Building(sim, 2, 8);
        EntityHandle w = Unit(sim, At(sim, 13, 9));
        sim.Enqueue(Command.Gather(0, w, At(sim, 14, 9)));
        UnitStore u = sim.World.Units;
        int t = 0;
        for (; t < 800 && u.Cargo[w.Index] < 10; t++) sim.Tick();
        Assert.Equal(10, u.Cargo[w.Index]);
        sim.Tick(); // on its way back
        // Another player's worth of gathering, done by the seam: the mine is gone while the worker walks home.
        Assert.Equal(2490, sim.World.Resources.Take(m1, 2490));
        Assert.False(sim.World.Resources.IsAlive(m1));
        for (t = 0; t < 600 && sim.World.Gold[0] == 200; t++) sim.Tick();
        Assert.Equal(210, sim.World.Gold[0]);
        Run(sim, 2);
        Assert.Equal(m2, u.GatherNode[w.Index]);
        Run(sim, 900);
        Assert.True(sim.World.Resources.Remaining[m2.Index] < 2500, "never worked the second mine");
    }

    [Fact]
    public void DropOffSpawnedAfterTheOrder_IsUsedForTheFirstLoad()
    {
        Simulation sim = GatherMaps.NewSim(Flat(40, 20));
        Spawn(sim.World, Mine, 20, 9, 2500);
        EntityHandle w = Unit(sim, At(sim, 19, 9));
        sim.Enqueue(Command.Gather(0, w, At(sim, 20, 9)));
        Run(sim, 100); // part of a load, no drop-off anywhere
        Assert.True(sim.World.Units.Cargo[w.Index] is > 0 and < 10);
        Building(sim, 8, 8);
        for (int t = 0; t < 900 && sim.World.Gold[0] == 200; t++) sim.Tick();
        Assert.Equal(210, sim.World.Gold[0]);
        Assert.True(EconomySystem.OnLoop(sim.World.Units, w.Index));
    }

    [Fact]
    public void FullLoadWithNoDropOff_GoesIdleKeepingIt_AndASameKindGatherAfterAKeepAppears_DeliversIt()
    {
        Simulation sim = GatherMaps.NewSim(Flat(40, 20));
        EntityHandle mine = Spawn(sim.World, Mine, 20, 9, 2500);
        EntityHandle w = Unit(sim, At(sim, 19, 9));
        sim.Enqueue(Command.Gather(0, w, At(sim, 20, 9)));
        UnitStore u = sim.World.Units;
        Run(sim, 400);
        Assert.Equal(10, u.Cargo[w.Index]);
        Assert.False(EconomySystem.OnLoop(u, w.Index)); // docs/03: no own drop-off -> Idle, cargo kept
        Assert.Equal(UnitState.Idle, u.State[w.Index]);
        Assert.Equal(2490, sim.World.Resources.Remaining[mine.Index]);
        Building(sim, 8, 8); // too late for the old loop: nothing re-arms it
        Run(sim, 100);
        Assert.Equal(200, sim.World.Gold[0]);
        sim.Enqueue(Command.Gather(0, w, At(sim, 20, 9)));
        for (int t = 0; t < 900 && sim.World.Gold[0] == 200; t++) sim.Tick();
        Assert.Equal(210, sim.World.Gold[0]);
    }

    [Fact]
    public void TwoPlayersOnOneMine_EachDepositsOnlyAtItsOwnKeep_AndTheSumIsConserved()
    {
        Simulation sim = GatherMaps.NewSim(Flat(48, 24), units: 16, players: 2);
        EntityHandle mine = Spawn(sim.World, Mine, 22, 10, 2500);
        Building(sim, 2, 9, player: 0);
        Building(sim, 30, 9, player: 1, type: TestSim.Data.FindBuilding("whirlwind_holy_camp"));
        var p0 = new[] { Unit(sim, At(sim, 21, 9)), Unit(sim, At(sim, 21, 12)), Unit(sim, At(sim, 20, 10)) };
        var p1 = new[] { Unit(sim, At(sim, 24, 9), player: 1), Unit(sim, At(sim, 24, 12), player: 1), Unit(sim, At(sim, 25, 10), player: 1) };
        foreach (EntityHandle h in p0) sim.Enqueue(Command.Gather(0, h, At(sim, 22, 10)));
        foreach (EntityHandle h in p1) sim.Enqueue(Command.Gather(1, h, At(sim, 23, 11)));
        var total = Conserved(sim.World);
        for (int t = 0; t < 2400; t++)
        {
            sim.Tick();
            AssertConserved(sim, total, $"tick {sim.TickNumber}");
        }
        _out.WriteLine($"two players on one mine, 120 s: p0 gold {sim.World.Gold[0]}, p1 gold {sim.World.Gold[1]}, mine {sim.World.Resources.Remaining[mine.Index]}");
        Assert.True(sim.World.Gold[0] >= 200 + 30 && sim.World.Gold[1] >= 200 + 30, $"p0 {sim.World.Gold[0]} p1 {sim.World.Gold[1]}");
    }

    [Fact]
    public void AFullWorkerNextToAnEnemyKeep_DoesNotDepositThere()
    {
        Simulation sim = GatherMaps.NewSim(Flat(40, 20), players: 2);
        Spawn(sim.World, Mine, 20, 9, 2500);
        Building(sim, 24, 8, player: 1, type: TestSim.Data.FindBuilding("whirlwind_holy_camp")); // touches the mine's stand cells
        EntityHandle w = Unit(sim, At(sim, 22, 9));
        sim.Enqueue(Command.Gather(0, w, At(sim, 20, 9)));
        Run(sim, 400);
        Assert.Equal(200, sim.World.Gold[0]);
        Assert.Equal(200, sim.World.Gold[1]);
        Assert.Equal(10, sim.World.Units.Cargo[w.Index]);
        Assert.False(EconomySystem.OnLoop(sim.World.Units, w.Index));
    }

    [Fact]
    public void AMineOnAPlateau_IsReachedByTheRamp_AndPaysOut()
    {
        var rows = new string[30];
        for (int y = 0; y < 30; y++)
        {
            var row = new char[40];
            for (int x = 0; x < 40; x++)
            {
                bool plateau = x >= 16 && x <= 34 && y >= 4 && y <= 24;
                bool ramp = x == 15 && y >= 20 && y <= 22;
                row[x] = ramp ? 'r' : plateau ? '1' : '0';
            }
            rows[y] = new string(row);
        }
        Simulation sim = GatherMaps.NewSim(FromRows(rows));
        NavGrid g = sim.World.NavGrid;
        Assert.True(g.IsPassable(25, 8) && g.LevelAt(25, 8) == 1);
        EntityHandle mine = Spawn(sim.World, Mine, 24, 8, 2500);
        Building(sim, 3, 6);
        EntityHandle w = Unit(sim, At(sim, 9, 8));
        Assert.Null(Stress.ResourceOracle.Reach(g));
        sim.Enqueue(Command.Gather(0, w, At(sim, 24, 8)));
        UnitStore u = sim.World.Units;
        bool upThere = false;
        for (int t = 0; t < 3000 && sim.World.Gold[0] < 220; t++)
        {
            sim.Tick();
            upThere |= g.WorldToCell(u.Position[w.Index], out int x, out int y) && g.LevelAt(x, y) == 1;
        }
        _out.WriteLine($"plateau mine: gold {sim.World.Gold[0]} at tick {sim.TickNumber}");
        Assert.True(upThere, "never climbed the plateau");
        Assert.True(sim.World.Gold[0] >= 220, $"gold {sim.World.Gold[0]}");
        Assert.True(sim.World.Resources.Remaining[mine.Index] <= 2480);
    }

    // ------------------------------------------------------------------ cramped stand cells and the reach boundary

    [Fact]
    public void ThreeWorkersOnATreeAtTheEndOfAOneCellCorridor_TakeTurns_AndWoodComesIn()
    {
        // A corridor one cell wide (y = 10) between two walls of trees, 6 cells long, ending at the target tree.
        Simulation sim = GatherMaps.NewSim(Flat(40, 22));
        for (int x = 14; x <= 20; x++)
        {
            Spawn(sim.World, Tree, x, 9, TreeWood);
            Spawn(sim.World, Tree, x, 11, TreeWood);
        }
        EntityHandle end = Spawn(sim.World, Tree, 21, 10, TreeWood);
        Spawn(sim.World, Tree, 21, 9, TreeWood);
        Spawn(sim.World, Tree, 21, 11, TreeWood);
        Building(sim, 4, 8);
        var w = new[] { Unit(sim, At(sim, 10, 10)), Unit(sim, At(sim, 10, 12)), Unit(sim, At(sim, 10, 8)) };
        foreach (EntityHandle h in w) sim.Enqueue(Command.Gather(0, h, At(sim, 21, 10)));
        var total = Conserved(sim.World);
        UnitStore u = sim.World.Units;
        for (int t = 0; t < 2400 * 2; t++)
        {
            sim.Tick();
            AssertConserved(sim, total, $"tick {sim.TickNumber}");
        }
        string states = string.Join(", ", w.Select(h => $"{u.State[h.Index]} cargo {u.Cargo[h.Index]} node {u.GatherNode[h.Index].Index}"));
        _out.WriteLine($"one-cell corridor, 240 s: wood {sim.World.Wood[0]}, end tree {sim.World.Resources.Remaining[end.Index]}; {states}");
        Assert.True(sim.World.Wood[0] >= 200 + 60, $"wood {sim.World.Wood[0]}: {states}");
        foreach (EntityHandle h in w) Assert.True(EconomySystem.OnLoop(u, h.Index), $"slot {h.Index} fell off the loop");
    }

    [Fact]
    public void ReachBoundary_AWorkerWithin1p25mWorksAtOnce_OneJustOutsideWalksFirst()
    {
        Simulation sim = GatherMaps.NewSim(Flat(30, 20));
        Spawn(sim.World, Tree, 10, 10, TreeWood); // footprint x 20-22 m, y 20-22 m
        EntityHandle inside = Unit(sim, new Vector2(22f + 1.2f, 21f));
        EntityHandle outside = Unit(sim, new Vector2(21f, 20f - 1.35f));
        UnitStore u = sim.World.Units;
        Assert.Equal(new Vector2(23.2f, 21f), u.Position[inside.Index]);
        sim.Enqueue(Command.Gather(0, inside, At(sim, 10, 10)));
        sim.Enqueue(Command.Gather(0, outside, At(sim, 10, 10)));
        Run(sim, 2);
        Assert.Equal(UnitState.Gathering, u.State[inside.Index]);
        Assert.NotEqual(UnitState.Gathering, u.State[outside.Index]);
        Run(sim, 80);
        Assert.True(u.Cargo[inside.Index] >= 2 && u.Cargo[outside.Index] >= 1, $"cargo {u.Cargo[inside.Index]} / {u.Cargo[outside.Index]}");
        Assert.True(DistanceToFootprint(sim.World.NavGrid, u.Position[outside.Index], 10 * 30 + 10, 1, 1) <= EconomyConstants.Reach);
    }

    [Fact]
    public void ATreeWhoseOnlyOpenNeighbourHoldsAHoldingSoldier_TheWorkerSqueezesInOrWaits_NoCrash_AStopEndsIt()
    {
        // A tree in a pocket of trees whose only open 4-neighbour is (12, 10); a friendly soldier holds that cell.
        Simulation sim = GatherMaps.NewSim(Flat(30, 22));
        EntityHandle target = Spawn(sim.World, Tree, 11, 10, TreeWood);
        foreach ((int x, int y) in new[] { (10, 10), (11, 9), (11, 11), (10, 9), (10, 11), (12, 9), (12, 11) })
            Spawn(sim.World, Tree, x, y, TreeWood);
        Building(sim, 22, 2);
        EntityHandle guard = Unit(sim, At(sim, 12, 10), type: Infantry);
        sim.Enqueue(Command.HoldPosition(0, guard));
        Run(sim, 2);
        EntityHandle w = Unit(sim, At(sim, 18, 10));
        sim.Enqueue(Command.Gather(0, w, At(sim, 11, 10)));
        UnitStore u = sim.World.Units;
        var total = Conserved(sim.World);
        int walks = 0;
        UnitState last = UnitState.Idle;
        for (int t = 0; t < 1200; t++)
        {
            sim.Tick();
            AssertConserved(sim, total, $"tick {sim.TickNumber}");
            if (u.State[w.Index] == UnitState.Moving && last != UnitState.Moving) walks++;
            last = u.State[w.Index];
        }
        _out.WriteLine($"stand cell held: target remaining {sim.World.Resources.Remaining[target.Index]}, worker node slot {u.GatherNode[w.Index].Index}, state {u.State[w.Index]}, walks started {walks}, cargo {u.Cargo[w.Index]}, wood {sim.World.Wood[0]}");
        Assert.True(EconomySystem.OnLoop(u, w.Index) || u.State[w.Index] == UnitState.Idle);
        sim.Enqueue(Command.Stop(0, w));
        Run(sim, 2);
        Assert.False(EconomySystem.OnLoop(u, w.Index));
        Assert.Equal(UnitState.Idle, u.State[w.Index]);
    }

    // ------------------------------------------------------------------ orders against the loop

    [Fact]
    public void ShiftQueuedMoveBehindAGather_RunsOnceTheLoopEndsOnDepletion()
    {
        Simulation sim = GatherMaps.NewSim(Flat(40, 20));
        Spawn(sim.World, Mine, 20, 9, 5); // nothing else within 20 m
        Building(sim, 10, 8);
        EntityHandle w = Unit(sim, At(sim, 19, 9));
        sim.Enqueue(Command.Gather(0, w, At(sim, 20, 9)));
        sim.Enqueue(Command.Move(0, w, At(sim, 30, 16), queued: true));
        UnitStore u = sim.World.Units;
        Run(sim, 2);
        Assert.Equal(1, u.QueueCount[w.Index]);
        for (int t = 0; t < 900 && Vector2.Distance(u.Position[w.Index], At(sim, 30, 16)) > 1.5f; t++) sim.Tick();
        Assert.True(Vector2.Distance(u.Position[w.Index], At(sim, 30, 16)) <= 1.5f, $"ended at {u.Position[w.Index]} ({u.State[w.Index]})");
        Assert.Equal(5, u.Cargo[w.Index]);
    }

    [Fact]
    public void GatherToAHoldingWorker_ClearsHold_AndStopHoldMoveAttackMoveAllKeepTheCargo()
    {
        Simulation sim = GatherMaps.NewSim(Flat(40, 20));
        Spawn(sim.World, Tree, 20, 9, TreeWood);
        EntityHandle w = Unit(sim, At(sim, 19, 9));
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.HoldPosition(0, w));
        Run(sim, 2);
        Assert.True(u.Hold[w.Index]);
        sim.Enqueue(Command.Gather(0, w, At(sim, 20, 9)));
        Run(sim, 2);
        Assert.False(u.Hold[w.Index]);
        Assert.True(EconomySystem.OnLoop(u, w.Index));
        for (int t = 0; t < 400 && u.Cargo[w.Index] < 3; t++) sim.Tick();
        foreach (Command c in new[] { Command.Stop(0, w), Command.HoldPosition(0, w), Command.Move(0, w, At(sim, 15, 5)), Command.AttackMove(0, w, At(sim, 16, 5)) })
        {
            sim.Enqueue(Command.Gather(0, w, At(sim, 20, 9)));
            Run(sim, 3);
            int cargo = u.Cargo[w.Index];
            sim.Enqueue(c);
            Run(sim, 2);
            Assert.False(EconomySystem.OnLoop(u, w.Index), $"{c.Kind} left the loop on");
            Assert.True(u.Cargo[w.Index] == cargo, $"{c.Kind}: cargo {cargo} -> {u.Cargo[w.Index]}");
            Assert.Equal(ResourceKind.Wood, u.CargoKind[w.Index]);
        }
    }

    [Fact]
    public void ThirtyAlternatingKindGathersInOneTick_LoseExactlyTheOneLoadCarried()
    {
        Simulation sim = GatherMaps.NewSim(Flat(40, 20), units: 8);
        Spawn(sim.World, Tree, 20, 9, TreeWood);
        Spawn(sim.World, Mine, 24, 9, 2500);
        EntityHandle w = Unit(sim, At(sim, 19, 9));
        sim.Enqueue(Command.Gather(0, w, At(sim, 20, 9)));
        UnitStore u = sim.World.Units;
        for (int t = 0; t < 400 && u.Cargo[w.Index] < 4; t++) sim.Tick();
        var before = Conserved(sim.World);
        for (int k = 0; k < 30; k++) sim.Enqueue(Command.Gather(0, w, k % 2 == 0 ? At(sim, 24, 9) : At(sim, 20, 9)));
        Run(sim, 2);
        var after = Conserved(sim.World);
        Assert.Equal(before.Gold, after.Gold);
        Assert.Equal(before.Wood - 4, after.Wood); // the four logs, dropped once; nothing created
        Assert.Equal(ResourceKind.Wood, u.CargoKind[w.Index]); // the last order was the tree
    }

    // ------------------------------------------------------------------ buildings and the door

    [Theory]
    [InlineData("crosses the east edge", 27, 5)]
    [InlineData("crosses the south edge", 5, 17)]
    [InlineData("anchor on the border", 0, 5)]
    [InlineData("anchor far off the map", 500, 500)]
    public void SpawnBuildingOverTheMapEdge_IsDropped_HashAndVersionUnchanged(string why, int x, int y)
    {
        Simulation sim = GatherMaps.NewSim(Flat(30, 20)), twin = GatherMaps.NewSim(Flat(30, 20));
        int version = sim.World.NavGrid.Version;
        sim.Enqueue(Command.SpawnBuilding(0, Keep, new Vector2(x * 2f + 1f, y * 2f + 1f)));
        twin.Enqueue(Command.Noop(0));
        Run(sim, 2);
        Run(twin, 2);
        Assert.True(sim.World.Buildings.Count == 0, why);
        Assert.Equal(version, sim.World.NavGrid.Version);
        Assert.Equal(twin.StateHash(), sim.StateHash());
    }

    [Fact]
    public void AColumnWithNoBuildingDropped_Arrives_ControlRow() => ColumnPastADroppedBuilding(drop: false);

    /// <summary>BUG-0077: the walkers' progress mark (<c>BestRemaining</c>) survives a closing grid change, so the detour round the new building counts as no progress and half the column gives up after 20 ticks.</summary>
    [Fact]
    public void ABuildingDroppedOnAMarchingColumnsPath_NoUnitCenterEverEntersItsCells_AndTheColumnStillArrives() => ColumnPastADroppedBuilding(drop: true);

    [Fact]
    public void ABuildingDroppedOnAMarchingColumnsPath_NoUnitCenterEverEntersItsCells()
    {
        // The safety half of the row above, which holds today.
        Simulation sim = GatherMaps.NewSim(Flat(48, 24), units: 24);
        var units = new List<EntityHandle>();
        for (int k = 0; k < 16; k++) units.Add(Unit(sim, At(sim, 3 + k % 4, 8 + k / 4), type: Infantry));
        foreach (EntityHandle h in units) sim.Enqueue(Command.Move(0, h, At(sim, 42, 10)));
        Run(sim, 30);
        sim.Enqueue(Command.SpawnBuilding(0, Keep, At(sim, 20, 8)));
        UnitStore u = sim.World.Units;
        NavGrid g = sim.World.NavGrid;
        for (int t = 0; t < 600; t++)
        {
            sim.Tick();
            foreach (EntityHandle h in units)
                Assert.True(g.WorldToCell(u.Position[h.Index], out int cx, out int cy) && (g.FlagsAt(cx, cy) & NavFlags.Building) == 0, $"tick {sim.TickNumber}: slot {h.Index} inside the building at {u.Position[h.Index]}");
        }
        Assert.Equal(1, sim.World.Buildings.Count);
    }

    private void ColumnPastADroppedBuilding(bool drop)
    {
        Simulation sim = GatherMaps.NewSim(Flat(48, 24), units: 24);
        var units = new List<EntityHandle>();
        for (int k = 0; k < 16; k++) units.Add(Unit(sim, At(sim, 3 + k % 4, 8 + k / 4), type: Infantry));
        foreach (EntityHandle h in units) sim.Enqueue(Command.Move(0, h, At(sim, 42, 10)));
        Run(sim, 30);
        if (drop) sim.Enqueue(Command.SpawnBuilding(0, Keep, At(sim, 20, 8)));
        UnitStore u = sim.World.Units;
        NavGrid g = sim.World.NavGrid;
        int allIdle = -1;
        for (int t = 0; t < 1200; t++)
        {
            sim.Tick();
            foreach (EntityHandle h in units)
            {
                Assert.True(g.WorldToCell(u.Position[h.Index], out int cx, out int cy));
                Assert.True((g.FlagsAt(cx, cy) & NavFlags.Building) == 0, $"tick {sim.TickNumber}: slot {h.Index} inside the building at {u.Position[h.Index]}");
            }
            if (allIdle < 0 && units.All(h => u.State[h.Index] == UnitState.Idle)) allIdle = t;
        }
        int near = units.Count(h => Vector2.Distance(u.Position[h.Index], At(sim, 42, 10)) < 6f);
        _out.WriteLine($"building on the path {drop}: placed {sim.World.Buildings.Count}; all Idle at tick {allIdle}; within 6 m of the goal {near}/16; positions {string.Join(" ", units.Select(h => $"({u.Position[h.Index].X:F0},{u.Position[h.Index].Y:F0})"))}");
        Assert.True(near >= 14, $"{near}/16 near the goal");
    }

    /// <summary>
    /// The exposure rule counts any passable 4-neighbour, also one sealed off by buildings: a tree whose only open
    /// neighbour is a pocket walled in by Keeps is "exposed", so a worker ordered onto it is sent to a stand cell it can
    /// never reach and retries for ever, while an exposed reachable tree stands right next to it.
    /// </summary>
    [Fact(Skip = "BUG-0078: exposure counts a passable neighbour nobody can reach; a worker sent to a tree exposed only to a pocket sealed by buildings retries for ever; un-skip when fixed")]
    public void ATreeExposedOnlyToAPocketSealedByBuildings_IsNotWhereTheWorkerEndsUpStuck()
    {
        Simulation sim = GatherMaps.NewSim(Flat(40, 30));
        EntityHandle sealedTree = Spawn(sim.World, Tree, 20, 15, TreeWood);
        EntityHandle open = Spawn(sim.World, Tree, 21, 15, TreeWood); // its east neighbour (22, 15) leads out
        Building(sim, 16, 11);
        Building(sim, 16, 16);
        Building(sim, 12, 13);
        Building(sim, 20, 11);
        Building(sim, 20, 16);
        Building(sim, 30, 2); // a drop-off outside
        NavGrid g = sim.World.NavGrid;
        Assert.True(g.IsPassable(19, 15) && g.IsPassable(22, 15));
        Assert.NotNull(Stress.ResourceOracle.Reach(g)); // the corridor x 16-19, y 15 is sealed
        EntityHandle w = Unit(sim, At(sim, 30, 8));
        sim.Enqueue(Command.Gather(0, w, At(sim, 20, 15)));
        UnitStore u = sim.World.Units;
        Run(sim, 1200);
        _out.WriteLine($"sealed-pocket tree: node slot {u.GatherNode[w.Index].Index} (sealed {sealedTree.Index}, open {open.Index}), state {u.State[w.Index]}, at {u.Position[w.Index]}, cargo {u.Cargo[w.Index]}, wood {sim.World.Wood[0]}");
        Assert.True(u.Cargo[w.Index] > 0 || sim.World.Wood[0] > 200, "60 s on the loop and nothing gathered");
    }

    [Fact]
    public void BuildingCapacityOne_SecondSpawnDropped_NoThrow_AndZeroCapacityIsRejected()
    {
        var sim = new Simulation(TestSim.Config(Seed: 3, PlayerCount: 1, UnitCapacity: 4, CommandCapacity: 16) with { BuildingCapacity = 1 }, Flat(40, 20));
        sim.Enqueue(Command.SpawnBuilding(0, Keep, At(sim, 2, 2)));
        sim.Enqueue(Command.SpawnBuilding(0, Keep, At(sim, 20, 10)));
        Run(sim, 2);
        Assert.Equal(1, sim.World.Buildings.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Simulation(TestSim.Config(Seed: 3, PlayerCount: 1, UnitCapacity: 4, CommandCapacity: 16) with { BuildingCapacity = 0 }, Flat(40, 20)));
    }

    [Fact]
    public void Door_QueuedSpawnBuildingAndUnknownFlagOnGather_AreMalformed_NaNGatherIsDroppedAtApply()
    {
        Simulation sim = GatherMaps.NewSim(Flat(30, 20));
        Spawn(sim.World, Tree, 10, 10, TreeWood);
        EntityHandle w = Unit(sim, At(sim, 9, 10));
        ulong h = sim.StateHash();
        var queuedBuilding = Command.SpawnBuilding(0, Keep, At(sim, 2, 2));
        queuedBuilding.Flags = Command.QueuedFlag;
        Assert.Throws<ArgumentException>(() => sim.Enqueue(queuedBuilding));
        var badFlag = Command.Gather(0, w, At(sim, 10, 10));
        badFlag.Flags = 4;
        Assert.Throws<ArgumentException>(() => sim.Enqueue(badFlag));
        Assert.Equal(h, sim.StateHash());
        sim.Enqueue(Command.Gather(0, w, new Vector2(float.NaN, 3f)));
        sim.Enqueue(Command.Gather(0, w, new Vector2(float.PositiveInfinity, float.NegativeInfinity), queued: true));
        Run(sim, 2);
        Assert.False(EconomySystem.OnLoop(sim.World.Units, w.Index));
        Assert.Equal(0, sim.World.Units.QueueCount[w.Index]);
    }

    [Fact]
    public void Replay_WithSpawnBuildingAndGathers_OnAGeneratedMap_PlaysBackToEveryCheckpoint()
    {
        SimConfig config = TestSim.Config(Seed: 11, PlayerCount: 2, UnitCapacity: 48, CommandCapacity: 256) with { Map = MapGenParams.Default with { Forests = 12, GoldMines = 8 } };
        var sim = new Simulation(config);
        var rec = new ReplayRecorder(sim, checkpointInterval: 25);
        EconomyScenario.Setup(sim, 12, player: 0, mineIndex: 0);
        EconomyScenario.Setup(sim, 12, player: 1, mineIndex: 1);
        for (int t = 0; t < 1500; t++)
        {
            if (t % 300 == 150)
            {
                // Re-order every worker of player 0 onto the other kind: discards in the log.
                UnitStore u = sim.World.Units;
                for (int i = 0; i < u.Capacity; i++)
                {
                    if (!u.Alive[i] || u.Owner[i] != 0 || !EconomySystem.OnLoop(u, i)) continue;
                    sim.Enqueue(Command.Gather(0, new EntityHandle(i, u.Generation[i]), u.GatherSite[i] + new Vector2(t % 600 == 150 ? 9f : -9f, 0f)));
                }
            }
            sim.Tick();
        }
        Replay r = rec.ToReplay();
        Assert.Equal(3, Replay.CurrentFormatVersion);
        Assert.Equal(3, r.FormatVersion);
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(r), out Replay? back));
        ReplayResult res = ReplayPlayer.Run(back!, TestSim.Data);
        Assert.True(res.Ok, $"replay {res.Error} at tick {res.Tick}");
        Assert.Contains(r.Commands, c => c.Kind == CommandKind.SpawnBuilding);
        Assert.Contains(r.Commands, c => c.Kind == CommandKind.Gather);
        _out.WriteLine($"replay: {r.Commands.Length} commands, {r.Checkpoints.Length} checkpoints; gold {sim.World.Gold[0]}/{sim.World.Gold[1]} wood {sim.World.Wood[0]}/{sim.World.Wood[1]}");
    }

    // ------------------------------------------------------------------ allocation on the rare paths

    [Fact]
    public void TicksWithFallsRetargetsDepositsAndABuildingPlaced_AllocateNothing()
    {
        // Twelve workers on a grove of trees drained to 1-2 wood: falls, depleted-node redirects, Idle-on-depletion
        // and deposits all happen inside the probed ticks; one probe also applies a SpawnBuilding.
        Simulation sim = GatherMaps.NewSim(Flat(48, 32), units: 16);
        var trees = new List<EntityHandle>();
        for (int y = 10; y < 22; y++)
            for (int x = 20; x < 30; x++)
                if ((x + y) % 2 == 0) trees.Add(Spawn(sim.World, Tree, x, y, 1 + (x + y) % 3));
        Building(sim, 12, 12);
        var w = new List<EntityHandle>();
        for (int k = 0; k < 12; k++) w.Add(Unit(sim, At(sim, 18, 9 + k)));
        for (int k = 0; k < 12; k++) sim.Enqueue(Command.Gather(0, w[k], At(sim, 20 + k % 10, 10 + k)));
        Run(sim, 40);
        int version = sim.World.NavGrid.Version, deposits = sim.World.Wood[0];
        for (int k = 0; k < 60; k++) AllocationProbe.AssertZero(() => sim.Tick(), _out);
        sim.Enqueue(Command.SpawnBuilding(0, Keep, At(sim, 2, 24)));
        sim.Tick();
        AllocationProbe.AssertZero(() => sim.Tick(), _out);
        _out.WriteLine($"probed 61 ticks: {sim.World.NavGrid.Version - version} grid changes, wood +{sim.World.Wood[0] - deposits}, buildings {sim.World.Buildings.Count}");
        Assert.True(sim.World.NavGrid.Version - version >= 3, "precondition: falls inside the probe");
    }

    // ------------------------------------------------------------------ crowding

    [Theory]
    [InlineData(20)]
    [InlineData(40)]
    [InlineData(80)]
    public void CrowdingOneMine_EveryWorkerDeliversAndNobodyIsStuckForever(int workers)
    {
        Simulation sim = GatherMaps.NewSim(Flat(64, 48), units: workers + 4);
        EntityHandle mine = Spawn(sim.World, Mine, 31, 23, 100000); // deeper than a real mine: the loop must not end by depletion inside the window
        Building(sim, 22, 22); // the Keep's east edge 10 m from the mine's west edge
        var w = new EntityHandle[workers];
        int placed = 0;
        for (int y = 8; y < 40 && placed < workers; y += 2)
            for (int x = 8; x < 56 && placed < workers; x += 2)
            {
                if (x >= 20 && x <= 34 && y >= 20 && y <= 28) continue;
                sim.Enqueue(Command.SpawnUnit(0, Laborer, At(sim, x, y)));
                placed++;
            }
        Run(sim, 2);
        UnitStore u = sim.World.Units;
        int k0 = 0;
        for (int i = 0; i < u.Capacity && k0 < workers; i++)
            if (u.Alive[i]) w[k0++] = new EntityHandle(i, u.Generation[i]);
        Assert.Equal(workers, k0);
        foreach (EntityHandle h in w) sim.Enqueue(Command.Gather(0, h, At(sim, 31, 23)));
        var firstDeposit = new int[workers];
        Array.Fill(firstDeposit, -1);
        var lastChange = new int[workers];
        var lastCargo = new int[workers];
        int longestIdle = 0, gold = sim.World.Gold[0];
        const int ticks = 20 * 240;
        var total = Conserved(sim.World);
        long tickTicks = 0;
        for (int t = 0; t < ticks; t++)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            sim.Tick();
            tickTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            Assert.True(sim.World.Gold[0] >= gold, "income went down");
            gold = sim.World.Gold[0];
            for (int k = 0; k < workers; k++)
            {
                int c = u.Cargo[w[k].Index];
                if (c < lastCargo[k] && firstDeposit[k] < 0) firstDeposit[k] = t;
                if (c != lastCargo[k]) lastChange[k] = t;
                lastCargo[k] = c;
                longestIdle = Math.Max(longestIdle, t - lastChange[k]);
            }
        }
        AssertConserved(sim, total, "end");
        int delivered = firstDeposit.Count(d => d >= 0);
        int worstFirst = firstDeposit.Max();
        int onLoop = w.Count(h => EconomySystem.OnLoop(u, h.Index));
        double ms = tickTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / ticks;
        _out.WriteLine($"{workers} workers on one 2 x 2 mine, 240 s: +{sim.World.Gold[0] - 200} gold ({(sim.World.Gold[0] - 200) / 240.0:F2}/s, mine {sim.World.Resources.Remaining[mine.Index]}); delivered at least once {delivered}/{workers}, slowest first delivery {(worstFirst < 0 ? "never" : $"{worstFirst / 20.0:F1} s")}; longest cargo-unchanged stretch {longestIdle / 20.0:F1} s; on the loop at the end {onLoop}/{workers}; avg tick {ms:F3} ms");
        Assert.Equal(workers, onLoop);
        Assert.Equal(workers, delivered);
        Assert.True(longestIdle < 20 * 120, $"a worker carried the same load for {longestIdle / 20.0:F1} s");
    }

    // ------------------------------------------------------------------ BUG-0073 at real cadence (measure, do not re-file)

    [Theory]
    [Trait("Category", "Perf")]
    [InlineData(100, 12000)] // shipped treeWood: the real cadence, 10 minutes
    [InlineData(20, 2400)]   // trees drained to two loads: five times the felling rate
    public void Bug0073_TwentyWorkersChopping_32MarchingGroups_Report(int woodPerTree, int ticks)
    {
        // 120 x 72 flat map; a forest block of 1 x 1 trees (felled at the shipped 100 wood each, i.e. real cadence)
        // with a Keep next to it; 20 workers chopping; 32 walkers with 32 distinct goals marching end to end and back.
        // Unit capacity 1,000 as in a match, so the field cache has its full 125 slots (with 64 the 32-slot cache
        // thrashes on 52 goal groups, which is a configuration artefact, not BUG-0073).
        Simulation sim = GatherMaps.NewSim(Flat(120, 72), units: 1000);
        NavGrid g = sim.World.NavGrid;
        for (int y = 30; y < 42; y++)
            for (int x = 54; x < 66; x++)
                Spawn(sim.World, Tree, x, y, TreeWood); // solid block: eaten from the outside, no pockets
        Building(sim, 48, 34);
        var workers = new EntityHandle[20];
        for (int k = 0; k < 20; k++) workers[k] = Unit(sim, At(sim, 50 + k % 4, 28 - k / 4));
        ResourceStore r = sim.World.Resources;
        for (int n = 0; n < r.Capacity; n++)
            if (r.Alive[n] && r.Remaining[n] > woodPerTree) r.Take(r.HandleOf(n), r.Remaining[n] - woodPerTree);
        for (int k = 0; k < 20; k++) sim.Enqueue(Command.Gather(0, workers[k], At(sim, 54 + k % 12, k < 12 ? 30 : 41)));
        var walkers = new EntityHandle[32];
        for (int k = 0; k < 32; k++) walkers[k] = Unit(sim, At(sim, 4 + k % 2 * 2, 4 + k * 2), type: Infantry);
        var east = new bool[32];
        var orderedAt = new int[32];
        for (int k = 0; k < 32; k++) sim.Enqueue(Command.Move(0, walkers[k], At(sim, 110 + k % 2 * 3, 4 + k * 2)));
        UnitStore u = sim.World.Units;
        FlowFieldCache cache = sim.World.FlowFields;
        var wait = new int[32];
        var workerWait = new int[20];
        int workerLongest = 0;
        int longest = 0, longWaits = 0, falls = 0, arrivals = 0, version = g.Version;
        double total = 0, worst = 0;
        for (int t = 0; t < ticks; t++)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            sim.Tick();
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            total += ms;
            if (t > 20) worst = Math.Max(worst, ms);
            if (g.Version != version) { falls += g.Version - version; version = g.Version; }
            for (int k = 0; k < 20; k++)
            {
                int i = workers[k].Index;
                bool waiting = u.State[i] == UnitState.Moving && u.Velocity[i] == Vector2.Zero && !cache.Contains(u.GoalCell[i]);
                workerWait[k] = waiting ? workerWait[k] + 1 : 0;
                workerLongest = Math.Max(workerLongest, workerWait[k]);
            }
            for (int k = 0; k < 32; k++)
            {
                int i = walkers[k].Index;
                bool waiting = u.State[i] == UnitState.Moving && u.Velocity[i] == Vector2.Zero && !cache.Contains(u.GoalCell[i]);
                wait[k] = waiting ? wait[k] + 1 : 0;
                if (wait[k] == 41) longWaits++;
                longest = Math.Max(longest, wait[k]);
                if (u.State[i] == UnitState.Idle && t - orderedAt[k] > 3)
                {
                    orderedAt[k] = t;
                    arrivals++;
                    east[k] = !east[k];
                    sim.Enqueue(Command.Move(0, walkers[k], east[k] ? At(sim, 4 + k % 2 * 2, 4 + k * 2) : At(sim, 110 + k % 2 * 3, 4 + k * 2)));
                }
            }
        }
        _out.WriteLine($"BUG-0073 at cadence ({woodPerTree} wood per tree, 120 x 72, 20 workers chopping, 32 marching groups, {ticks / 20} s): {falls} grid changes ({falls / (ticks / 20.0):F2}/s); walkers' longest field wait {longest} ticks, waits over 40 ticks {longWaits}; workers' longest field wait {workerLongest} ticks; field cache {cache.Count}/{cache.Capacity}; trips finished {arrivals}; wood {sim.World.Wood[0] - 200}; avg tick {total / ticks:F3} ms, worst {worst:F3} ms");
        Assert.True(sim.World.Wood[0] > 200, "gathering itself broke");
    }
}
