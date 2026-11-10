using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using static Rts.Sim.Tests.ProductionMaps;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-2 criteria 2-3: a multi-spawn queue item (<c>units.json</c> <c>spawnCount</c>; docs/factions/teblor.md "Unit notes",
/// the war-dog pack: "one queue item spawns 3 normal-sized dogs (1 pop each)") on a test copy where Malazan Heavy Infantry
/// (Age I, pop 1, 54 / 20, 280 ticks) has <c>spawnCount</c> 3: one Train, one item, the pack's cost and train time, every
/// dog's population reserved at the start, all three spawned at once or none (docs/03 "Implementation (M7-2)").
/// </summary>
[Collection(SerialCollection.Name)]
public class PackProductionTests
{
    internal static readonly Lazy<GameData> s_pack = new(() => AiScenes.DataWith(dir =>
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "spawnCount", "3")));

    private static GameData D => s_pack.Value;

    private static int Heavy => D.FindUnit("malazan_heavy_infantry");

    private static int Laborer => D.FindUnit("malazan_laborer");

    [Fact]
    public void TheCopy_HasAPackOfThree_AtOnePopEach()
    {
        UnitDef h = D.Units[Heavy];
        Assert.Equal((3, 2, 6), (h.SpawnCount, h.HalfPop, h.ItemHalfPop));
        Assert.Equal((54, 20, 280), (h.CostGold, h.CostWood, h.TrainTicks));
        Assert.True(h.RequiresTechs.IsEmpty && h.RequiresBuildings.IsEmpty, "an Age I unit");
    }

    /// <summary>A one-player flat sim on the copy: a Keep (cap 10 = 20 half-pop) at (25, 10), a Barracks at (10, 10), plenty to spend, <paramref name="laborers"/> laborers.</summary>
    private static (Simulation Sim, int Barracks) Base(int laborers, int units = 64)
    {
        var sim = new Simulation(new SimConfig(5, 1, units, 544) { Data = D }, LocalMovementTests.Flat(40));
        World w = sim.World;
        Assert.True(w.Buildings.Spawn(0, D.FindBuilding("malazan_garrison_keep"), 10 * 40 + 25, out _));
        Assert.True(w.Buildings.Spawn(0, D.FindBuilding("malazan_barracks"), 10 * 40 + 10, out EntityHandle barracks));
        BuildMaps.SetTotals(sim, 0, 5000, 5000);
        for (int k = 0; k < laborers; k++) CombatScenes.Place(sim, 0, Laborer, CombatScenes.At(sim, 4 + k % 8, 28 + k / 8));
        return (sim, barracks.Index);
    }

    private static int Count(World w, int type, int owner = 0)
    {
        int n = 0;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.TypeId[i] == type && w.Units.Owner[i] == owner) n++;
        return n;
    }

    /// <summary>
    /// Criterion 2: one Train is one queue item, paid once; the tick it starts <c>HalfPop</c> rises by 6 (three dogs at pop 1);
    /// after the pack's 280 ticks exactly three Heavy Infantry of player 0 stand, and the count is unchanged by the spawn
    /// (the reservation becomes the three units' own counts). The recount holds every tick.
    /// </summary>
    [Fact]
    public void OneTrain_IsOneItem_ReservesSixHalves_AndSpawnsExactlyThreeUnits()
    {
        (Simulation sim, int bar) = Base(laborers: 2);
        World w = sim.World;
        Assert.Equal(4, w.HalfPop[0]);
        sim.Enqueue(Command.Train(0, In(sim, bar), Heavy));
        sim.Tick();
        sim.Tick(); // applies: paid once; the item starts this tick
        Assert.Equal((5000 - 54, 5000 - 20), (w.Gold[0], w.Wood[0]));
        Assert.Equal((1, 1), (w.Buildings.QueueCount[bar], w.Buildings.Progress[bar]));
        Assert.Equal((10, 6), (w.HalfPop[0], w.Buildings.ReservedHalfPop(bar)));
        int trainedBefore = w.Ledger.Stats.UnitsTrained[0];
        for (int k = 0; k < 278; k++)
        {
            sim.Tick();
            Assert.Equal(10, w.HalfPop[0]);
            Assert.Equal(10, RecountHalfPop(w, 0));
        }
        Assert.Equal((0, 279), (Count(w, Heavy), w.Buildings.Progress[bar]));
        sim.Tick(); // the 280th tick: all three at once
        Assert.Equal(3, Count(w, Heavy));
        Assert.Equal((0, 0), (w.Buildings.QueueCount[bar], w.Buildings.Progress[bar]));
        Assert.Equal((10, 10), (w.HalfPop[0], RecountHalfPop(w, 0)));
        Assert.Equal(trainedBefore + 3, w.Ledger.Stats.UnitsTrained[0]); // three units trained
        // Each dog counts its own pop: one dies, two remain at 2 half-pop each.
        int one = LiveUnits(w).First(i => w.Units.TypeId[i] == Heavy);
        w.Units.Free(new EntityHandle(one, w.Units.Generation[one]));
        Assert.Equal(8, w.HalfPop[0]);
    }

    /// <summary>
    /// Criterion 2: a cancel of the started pack (applied at progress <paramref name="ticksIn"/> of 280: just started, half
    /// way, one tick from done) refunds its whole cost and releases all 6 halves; a cancel of an unstarted one refunds the cost.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(150)]
    [InlineData(279)]
    public void ACancel_OfTheStartedPack_RefundsTheCost_AndReleasesTheWholeReservation(int ticksIn)
    {
        (Simulation sim, int bar) = Base(laborers: 2);
        World w = sim.World;
        sim.Enqueue(Command.Train(0, In(sim, bar), Heavy));
        sim.Enqueue(Command.Train(0, In(sim, bar), Heavy));
        sim.Tick();
        sim.Tick();
        Assert.Equal((2, 1, 10), (w.Buildings.QueueCount[bar], w.Buildings.Progress[bar], w.HalfPop[0]));
        // A command queued now applies in phase 1 of the tick after next, before that tick's production step.
        while (w.Buildings.Progress[bar] < ticksIn - 1) sim.Tick();
        Assert.Equal((5000 - 108, 5000 - 40), (w.Gold[0], w.Wood[0]));
        sim.Enqueue(Command.CancelTrain(0, In(sim, bar), 1)); // the queued one: cost back, nothing reserved
        sim.Enqueue(Command.CancelTrain(0, In(sim, bar), 0)); // then the started head
        sim.Tick();
        Assert.Equal((2, ticksIn, 10), (w.Buildings.QueueCount[bar], w.Buildings.Progress[bar], w.HalfPop[0]));
        sim.Tick();
        Assert.Equal((5000, 5000), (w.Gold[0], w.Wood[0]));
        Assert.Equal((0, 0), (w.Buildings.QueueCount[bar], w.Buildings.Progress[bar]));
        Assert.Equal((4, 4), (w.HalfPop[0], RecountHalfPop(w, 0)));
        Assert.Equal(0, Count(w, Heavy));
    }

    /// <summary>
    /// Criterion 2: with room under the cap for one dog (or two) but not three the pack is queued and paid but never starts
    /// (the producer's cap check, today's rule for an item that doesn't fit: it waits at the head); with room for three it starts.
    /// </summary>
    [Fact]
    public void APack_WaitsUnstarted_WithRoomForTwoDogs_AndStartsWithRoomForThree()
    {
        (Simulation sim, int bar) = Base(laborers: 8); // 16 of 20: room for 2 dogs (4 halves), not 3 (6)
        World w = sim.World;
        Assert.Equal((16, 20), (w.HalfPop[0], w.HalfPopCap[0]));
        sim.Enqueue(Command.Train(0, In(sim, bar), Heavy));
        for (int k = 0; k < 60; k++) sim.Tick();
        Assert.Equal((1, 0), (w.Buildings.QueueCount[bar], w.Buildings.Progress[bar]));
        Assert.Equal((16, 0), (w.HalfPop[0], w.Buildings.ReservedHalfPop(bar)));
        Assert.Equal((5000 - 54, 5000 - 20), (w.Gold[0], w.Wood[0])); // paid at queue time, as any item
        // A single Heavy Infantry (spawnCount 1) of the shipped data would have started here: 16 + 2 <= 20.
        Assert.True(16 + D.Units[Heavy].HalfPop <= 20);
        int laborer = LiveUnits(w).First(i => w.Units.TypeId[i] == Laborer);
        w.Units.Free(new EntityHandle(laborer, w.Units.Generation[laborer])); // 14: room for 6
        sim.Tick();
        Assert.Equal((1, 20, 6), (w.Buildings.Progress[bar], w.HalfPop[0], w.Buildings.ReservedHalfPop(bar)));
    }

    /// <summary>
    /// The plateau scene of <c>ProductionTests.PlateauKeep</c> with a Barracks on the plateau instead: an 8 x 8 level-1 plateau
    /// at (p, p) on a <paramref name="size"/> map, ramp on its west side; the Barracks at (p+2, p+2), a Keep for the cap on
    /// level 0. Returns the plateau's free cells (the open cells off the footprint).
    /// </summary>
    private static (Simulation Sim, int Barracks, List<(int X, int Y)> Free) PlateauBarracks(int size, int p)
    {
        var rows = new string[size];
        for (int y = 0; y < size; y++)
        {
            var row = new char[size];
            for (int x = 0; x < size; x++) row[x] = x >= p && x <= p + 7 && y >= p && y <= p + 7 ? '1' : '0';
            if (y == p + 3) row[p - 1] = 'r';
            rows[y] = new string(row);
        }
        var sim = TestSim.Explored(new Simulation(new SimConfig(5, 2, 64, 512) { Data = D, Combat = false }, ResourceMaps.FromRows(rows)));
        World w = sim.World;
        Assert.True(w.Buildings.Spawn(0, D.FindBuilding("malazan_garrison_keep"), 36 * size + 36, out _));
        Assert.True(w.Buildings.Spawn(0, D.FindBuilding("malazan_barracks"), (p + 2) * size + p + 2, out EntityHandle barracks));
        NavGrid g = w.NavGrid;
        var free = new List<(int, int)>();
        for (int y = p; y <= p + 7; y++)
            for (int x = p; x <= p + 7; x++)
                if (g.IsPassable(x, y)) free.Add((x, y));
        return (sim, barracks.Index, free);
    }

    /// <summary>
    /// Criterion 3, the spawn rule (all or nothing per tick): with two free cells on the plateau the complete pack spawns
    /// nobody and stays at the head, its reservation held; the tick a third cell frees, all three spawn.
    /// </summary>
    [Fact]
    public void WithRoomForTwo_NothingSpawns_AndTheItemWaits_WithRoomForThree_AllThreeSpawn()
    {
        (Simulation sim, int bar, List<(int X, int Y)> free) = PlateauBarracks(48, 20);
        World w = sim.World;
        Assert.Equal(28, free.Count); // 36 open cells - the 3 x 3 footprint + the ramp head
        var blockers = new List<EntityHandle>();
        foreach ((int x, int y) in free) blockers.Add(CombatScenes.Place(sim, 1, D.FindUnit("whirlwind_raider"), CombatScenes.At(sim, x, y)));
        // Two cells free.
        for (int k = 0; k < 2; k++) w.Units.Free(blockers[k]);
        sim.Enqueue(Command.Train(0, In(sim, bar), Heavy));
        int train = D.Units[Heavy].TrainTicks;
        for (int k = 0; k < train + 40; k++) sim.Tick();
        Assert.Equal(0, Count(w, Heavy));
        Assert.Equal((1, train), (w.Buildings.QueueCount[bar], w.Buildings.Progress[bar])); // complete, waiting at the head
        Assert.Equal((6, 6), (w.HalfPop[0], w.Buildings.ReservedHalfPop(bar))); // still reserved
        Assert.Equal(6, RecountHalfPop(w, 0));
        // A third cell frees: all three spawn the next tick, one per cell, on the three free cells.
        w.Units.Free(blockers[2]);
        sim.Tick();
        Assert.Equal(3, Count(w, Heavy));
        Assert.Equal((0, 0), (w.Buildings.QueueCount[bar], w.Buildings.Progress[bar]));
        Assert.Equal((6, 6), (w.HalfPop[0], RecountHalfPop(w, 0)));
        var cells = new HashSet<(int, int)>();
        foreach (int i in LiveUnits(w))
        {
            Assert.True(w.NavGrid.WorldToCell(w.Units.Position[i], out int x, out int y));
            Assert.True(cells.Add((x, y)), $"two units in ({x}, {y})");
        }
        var opened = new HashSet<(int, int)> { free[0], free[1], free[2] };
        foreach (int i in LiveUnits(w).Where(i => w.Units.TypeId[i] == Heavy))
        {
            w.NavGrid.WorldToCell(w.Units.Position[i], out int x, out int y);
            Assert.Contains((x, y), opened);
        }
    }

    /// <summary>The spawn rule with the store short of slots: two free slots for a pack of three spawn nobody.</summary>
    [Fact]
    public void WithTwoFreeUnitSlots_NothingSpawns()
    {
        (Simulation sim, int bar) = Base(laborers: 2, units: 8);
        World w = sim.World;
        sim.Enqueue(Command.Train(0, In(sim, bar), Heavy));
        sim.Tick();
        sim.Tick();
        // 2 laborers; 4 more fill 6 of the 8 slots (18 of 20 halves with the pack's 6 reserved).
        for (int k = 0; k < 4; k++) CombatScenes.Place(sim, 0, Laborer, CombatScenes.At(sim, 34, 30 + k));
        Assert.Equal((2, 18), (w.Units.FreeCount, w.HalfPop[0]));
        for (int k = 0; k < 290; k++) sim.Tick();
        Assert.Equal((0, 1), (Count(w, Heavy), w.Buildings.QueueCount[bar]));
        int laborer = LiveUnits(w).First(i => w.Units.TypeId[i] == Laborer);
        w.Units.Free(new EntityHandle(laborer, w.Units.Generation[laborer]));
        sim.Tick();
        Assert.Equal((3, 0), (Count(w, Heavy), w.Buildings.QueueCount[bar]));
    }

    /// <summary>Criterion 3, rally order: the three spawn on the three ring cells nearest the rally point, nearest first, the same cells in the same order every run.</summary>
    [Fact]
    public void ThePack_SpawnsOnTheThreeCellsNearestTheRally_InTheSameOrderEveryRun()
    {
        (int, int)[] first = RallySpawn(), second = RallySpawn();
        Assert.Equal(first, second);
        Assert.Equal(3, first.Distinct().Count());
    }

    private static (int, int)[] RallySpawn()
    {
        (Simulation sim, int bar) = Base(laborers: 0);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        Vector2 rally = CombatScenes.At(sim, 30, 25, 0.3f, -0.2f);
        sim.Enqueue(Command.SetRally(0, w.Buildings.Cell[bar], rally));
        sim.Enqueue(Command.Train(0, In(sim, bar), Heavy));
        int done = CombatScenes.RunUntil(sim, () => Count(w, Heavy) == 3, 400);
        Assert.True(done < 400);
        var spawned = new List<(int, int)>();
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.TypeId[i] == Heavy)
            {
                // The spawn tick's position is the previous one (they walk from the next tick's phase 9).
                Assert.True(g.WorldToCell(w.Units.PrevPosition[i], out int x, out int y));
                Assert.Equal(1, RingOf(sim, bar, x, y));
                spawned.Add((x, y));
            }
        // The ring-1 cells by distance to the rally (ties to the lower cell index): the pack holds the first three, in slot order.
        var ring = new List<(int X, int Y)>();
        for (int y = 9; y <= 13; y++)
            for (int x = 9; x <= 13; x++)
                if (RingOf(sim, bar, x, y) == 1) ring.Add((x, y));
        var expected = ring.OrderBy(c => Vector2.DistanceSquared(rally, g.CellCenter(c.X, c.Y))).ThenBy(c => c.Y * g.Width + c.X).Take(3).ToArray();
        Assert.Equal(expected, spawned.ToArray());
        return spawned.ToArray();
    }

    /// <summary>The AI copy: a pop step at 5 pop (10 half) on every opening, nothing else in the build order.</summary>
    private static readonly Lazy<GameData> s_aiPack = new(() => AiScenes.DataWith(dir =>
    {
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "spawnCount", "3");
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            foreach (JsonNode? o in root["openings"]!.AsArray())
                o!["buildOrder"] = AiScenes.BuildOrder(("pop", 5, "build", "malazan_armory", 0));
        });
    }));

    /// <summary>
    /// Criterion 2: the AI's <c>PlayerView.HalfPop</c> counts the pack from its start (live + started items), so a <c>pop</c>
    /// step fires on it before any dog stands: two workers (4 halves) + a started pack (6) reach the step's 10.
    /// </summary>
    [Fact]
    public void TheAisPopTrigger_CountsTheStartedPack_BeforeItSpawns()
    {
        GameData d = s_aiPack.Value;
        Assert.Equal(10, d.Ai[d.FindFaction("malazan")].Openings[0].BuildOrder[0].At);
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Easy, SeatController.Ai, SeatController.Human), d, workers: 2);
        World w = sim.World;
        Assert.True(w.Buildings.Spawn(0, d.FindBuilding("malazan_barracks"), 30 * AiScenes.FlatSize + 20, out EntityHandle barracks));
        AiPlayer ai = sim.AiOf(0)!;
        Assert.Equal(4, ai.View.HalfPop);
        sim.Tick();
        Assert.Equal(-1, ai.Board.StepDueTick[0]); // not due on the workers alone
        BuildMaps.SetTotals(sim, 0, 1000, 1000);
        sim.Enqueue(Command.Train(0, In(sim, barracks.Index), d.FindUnit("malazan_heavy_infantry")));
        int ran = CombatScenes.RunUntil(sim, () => ai.Board.StepDueTick[0] >= 0, 200);
        Assert.True(ran < 200, "the pop step never fired");
        Assert.True(w.Buildings.Progress[barracks.Index] > 0, "fired before the pack started");
        Assert.Equal(0, Count(w, d.FindUnit("malazan_heavy_infantry"))); // no dog stands yet
        Assert.True(ai.View.HalfPop >= 10 && ai.View.HalfPop - 6 < 10, $"pop {ai.View.HalfPop}: the pack's 6 made the step");
    }
}
