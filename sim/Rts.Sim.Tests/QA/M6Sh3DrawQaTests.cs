using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Draw;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA, M6-SH3 (session 2026-11-27-0817, tier full, QA focus 1 and 3): attacks on the exact "can still train" (docs/02 "Game
/// flow" 4, docs/01 2026-11-27 second row) beyond the developer's rows. From the "wrongly early" side: a worker walking to a
/// finished drop-off with the last gold, wood carried, a drop-off finished inside the tick the clock would reach the wait,
/// a cancel's refund at the cap, an ally's drop-off (the rule follows deliveries). The counters against a scan through every
/// queue path (a mixed queue freed, a started head cancelled, a middle item cancelled, a cargo switch). Saves at every second
/// of a running clock and on every tick around a drop-off's completion.
/// </summary>
public class M6Sh3DrawQaTests
{
    private readonly ITestOutputHelper _out;

    public M6Sh3DrawQaTests(ITestOutputHelper output) => _out = output;

    /// <summary>Seat 0 an Infantry Hall; seat 1 an Infantry Hall and a soldier (spent: no worker, bank 0).</summary>
    private static Simulation Base(GameData? data = null)
    {
        Simulation sim = DrawScenes.Empty(data ?? DrawScenes.Short, 2);
        DrawScenes.Building(sim, 0, 8, 8);
        DrawScenes.Building(sim, 1, 44, 44);
        DrawScenes.Unit(sim, 1, 44, 50);
        DrawScenes.Broke(sim);
        return sim;
    }

    private static UnitDef CheapestGoldOnly(World w, int p)
    {
        UnitDef? best = null;
        foreach (int t in w.Data.Factions[w.FactionOf(p)].Units)
            if (w.Data.Units[t].CostWood == 0 && (best == null || w.Data.Units[t].CostGold < best.CostGold)) best = w.Data.Units[t];
        return best!;
    }

    private static void Check(World w)
    {
        string? fail = DrawRuleQaTests.CountersAgree(w);
        Assert.True(fail == null, fail);
        for (int p = 0; p < w.Config.PlayerCount; p++)
            if (!w.IsDefeated(p)) Assert.True(w.CanStillTrain(p) == DrawReachScan.NewCan(w, p), $"p{p}: sim {w.CanStillTrain(p)}, scan: {DrawReachScan.Line(w, p)}");
    }

    /// <summary>
    /// A worker carrying exactly the cheapest gold-only unit's gold beside its own finished Town Hall, bank 0, no gold on the map:
    /// the carried gold counts (not spent); it delivers, and the bank holds it (still not spent, the clock never runs); the
    /// same worker without the hall (razed) is spent.
    /// </summary>
    [Fact]
    public void TheLastGold_CarriedToAFinishedDropOff_KeepsTheSeatAbleToTrain_ThroughTheDelivery()
    {
        Simulation sim = Base();
        World w = sim.World;
        UnitDef unit = CheapestGoldOnly(w, 0);
        EntityHandle hall = DrawScenes.Building(sim, 0, 20, 8, BuildingSlot.TownHall);
        EntityHandle node = DrawScenes.GoldNode(sim, 30, 9, unit.CostGold);
        EntityHandle worker = DrawScenes.Unit(sim, 0, 28, 10, UnitSlot.Worker);
        sim.Enqueue(Command.Gather(0, worker, new Vector2(31f, 10f) * Map.MapConstants.CellSize));
        int maxClock = 0;
        for (int t = 0; t < 6000 && w.Gold[0] < unit.CostGold; t++)
        {
            sim.Tick();
            Check(w);
            Assert.True(w.CanStillTrain(0), $"tick {sim.TickNumber}: the seat's last gold (bank {w.Gold[0]}, carried {w.Ledger.CarriedGold[0]}, map {w.Resources.GoldLeft}) is reach");
            maxClock = Math.Max(maxClock, w.DrawClock);
        }
        _out.WriteLine($"delivered by {sim.TickNumber}: bank {w.Gold[0]}, node alive {w.Resources.IsAlive(node)} left {(w.Resources.IsAlive(node) ? w.Resources.Remaining[node.Index] : 0)}; worker state {w.Units.State[worker.Index]} cargo {w.Units.Cargo[worker.Index]} at {w.Units.Position[worker.Index]}; highest clock {maxClock}");
        Assert.Equal(unit.CostGold, w.Gold[0]);
        Assert.Equal(0, maxClock);
        // A second scene: the same load carried, the hall razed: nowhere to deliver it.
        Simulation s2 = Base();
        World w2 = s2.World;
        EntityHandle hall2 = DrawScenes.Building(s2, 0, 20, 8, BuildingSlot.TownHall);
        EntityHandle wk2 = DrawScenes.Unit(s2, 0, 28, 10, UnitSlot.Worker);
        w2.Units.CargoKind[wk2.Index] = ResourceKind.Gold;
        w2.Units.Cargo[wk2.Index] = unit.CostGold;
        w2.RecountDraw();
        Assert.True(w2.CanStillTrain(0));
        Assert.True(w2.Buildings.Free(hall2));
        Assert.False(w2.CanStillTrain(0), "a load with no drop-off of its own");
        Assert.True(w.Buildings.IsAlive(hall));
    }

    /// <summary>
    /// Wood carried counts: a seat with the gold for its cheapest gold-and-wood unit but no banked wood, a worker carrying exactly
    /// that unit's wood and a finished drop-off, no wood on the map, can train; one wood short, it cannot; the drop-off razed, it cannot.
    /// </summary>
    [Fact]
    public void WoodCarried_ToAFinishedDropOff_IsReach_OneShortIsNot()
    {
        Simulation sim = Base();
        World w = sim.World;
        UnitDef cheap = DrawReachTests.CheapestByGold(w, 0);
        Assert.True(cheap.CostWood > 0);
        Assert.Equal(0, w.Resources.WoodLeft);
        EntityHandle hall = DrawScenes.Building(sim, 0, 20, 8, BuildingSlot.TownHall);
        EntityHandle worker = DrawScenes.Unit(sim, 0, 28, 10, UnitSlot.Worker);
        w.Ledger.Gold[0] = cheap.CostGold;
        w.Units.CargoKind[worker.Index] = ResourceKind.Wood;
        w.Units.Cargo[worker.Index] = cheap.CostWood - 1;
        w.RecountDraw();
        Assert.Equal(cheap.CostWood - 1, w.Ledger.CarriedWood[0]);
        Assert.False(w.CanStillTrain(0), "one wood short");
        w.Units.Cargo[worker.Index] = cheap.CostWood;
        w.RecountDraw();
        Assert.True(w.CanStillTrain(0), "the carried wood pays");
        Check(w);
        // Delivered: the wood moves to the bank, the answer stays.
        for (int t = 0; t < 400 && w.Units.Cargo[worker.Index] != 0; t++)
        {
            if (t == 0) sim.Enqueue(Command.Move(0, worker, DrawScenes.Centre(sim, hall))); // a move does not deliver; just keeps the scene alive
            sim.Tick();
            Check(w);
            Assert.True(w.CanStillTrain(0));
        }
        Assert.True(w.Buildings.Free(hall));
        Assert.False(w.CanStillTrain(0), "the carried wood with nowhere to go");
    }

    /// <summary>
    /// A Town Hall site finished by a builder inside the very tick on which the clock would reach the wait (phase 4, before the
    /// draw check in phase 11c): no draw, the clock back to 0. A site finished one tick later (the clock already at the wait) is
    /// too late: drawn. Both from the same staged state.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ADropOffFinishedInsideTheLastTick_StopsTheDraw_OneTickLateDoesNot(bool inTime)
    {
        Simulation sim = Base();
        World w = sim.World;
        UnitDef unit = CheapestGoldOnly(w, 0);
        DrawScenes.GoldNode(sim, 34, 30, 10 * unit.CostGold);
        EntityHandle worker = DrawScenes.Unit(sim, 0, 20, 14, UnitSlot.Worker);
        EntityHandle site = DrawScenes.Building(sim, 0, 20, 8, BuildingSlot.TownHall, site: true);
        int type = w.Buildings.TypeId[site.Index];
        sim.Enqueue(Command.Build(0, worker, type, w.NavGrid.CellCenter(20, 8)));
        int wait = w.DrawWaitTicks;
        int finishAtClock = inTime ? wait : wait + 1;
        // Hold the site at 0 work until the tick before the one it should finish in.
        while (w.DrawClock < finishAtClock - 1 && !sim.MatchOver)
        {
            w.Buildings.SetWork(site.Index, 0);
            sim.Tick();
            Check(w);
        }
        Assert.Equal(finishAtClock - 1, w.DrawClock);
        if (!inTime)
        {
            Assert.True(w.MatchDrawn, "the clock reached the wait first");
            return;
        }
        Assert.True(w.Units.BuildTarget[worker.Index] == site, "precondition: the worker is at the site");
        w.Buildings.SetWork(site.Index, w.Buildings.WorkNeeded(type) - 1);
        Assert.True(w.Buildings.UnderConstruction[site.Index]);
        sim.Tick();
        _out.WriteLine($"tick {sim.TickNumber}: site finished {!w.Buildings.UnderConstruction[site.Index]}, clock {w.DrawClock}, drawn {w.MatchDrawn}");
        Assert.False(w.Buildings.UnderConstruction[site.Index], "the builder finished it inside the tick");
        Assert.False(w.MatchDrawn, "a drop-off finished on the last tick is a seat that can earn");
        Assert.Equal(0, w.DrawClock);
        Check(w);
    }

    /// <summary>
    /// A paid item waiting at the cap with an empty bank and no drop-off: spent (docs/01 2026-11-27 (1)(a)); cancelled, the refund
    /// lands in the bank and the clock stops on that tick ((b): room is not asked).
    /// </summary>
    [Fact]
    public void ACancelledItemAtTheCap_ItsRefund_StopsTheClockOnThatTick()
    {
        Simulation sim = Base();
        World w = sim.World;
        EntityHandle hall = new(FirstBuilding(w, 0), w.Buildings.Generation[FirstBuilding(w, 0)]);
        int type = w.Data.UnitsTrainedAt(w.Buildings.TypeId[hall.Index])[0];
        while (w.HalfPopCap[0] - w.HalfPop[0] >= w.Data.Units[type].ItemHalfPop) DrawScenes.Unit(sim, 0, 2 + w.HalfPop[0] % 20, 30);
        w.Buildings.Enqueue(hall.Index, type);
        Assert.True(w.MatchSpent);
        for (int t = 0; t < 40; t++) sim.Tick();
        Assert.Equal(40, w.DrawClock);
        sim.Enqueue(Command.CancelTrain(0, DrawScenes.Centre(sim, hall), 0)); // applies at the start of the tick after next
        sim.Tick();
        Assert.Equal(41, w.DrawClock);
        sim.Tick();
        Assert.Equal(0, w.Ledger.QueuedUnits[0]);
        Assert.True(w.Gold[0] >= w.Data.Units[type].CostGold);
        Assert.Equal(0, w.DrawClock);
        Assert.False(w.MatchSpent);
        Check(w);
    }

    /// <summary>
    /// An ally's drop-off (docs/01 2026-11-27 (1): "of their own"; the developer: deliveries go to the worker's own buildings only).
    /// Seat 0 (team 1) has a worker, a producer and no drop-off; its ally seat 2 (team 1, the same faction) a Town Hall beside a
    /// gold mine; seat 1 the enemy. Seat 0's worker gathers there for 1,500 ticks: its bank never grows (no delivery to the ally)
    /// and the rule never counts the map for it, consistently; the match draws.
    /// </summary>
    [Fact]
    public void AnAllysDropOff_NeitherTakesDeliveries_NorCountsAsReach()
    {
        Simulation sim = DrawScenes.Empty(DrawScenes.Short, 3, teams: new[] { 1, 2, 1 });
        World w = sim.World;
        Assert.Equal(w.FactionOf(0), w.FactionOf(2));
        Assert.True(w.AreAllied(0, 2));
        DrawScenes.Building(sim, 0, 6, 40);
        DrawScenes.Building(sim, 1, 44, 44);
        DrawScenes.Unit(sim, 1, 44, 50);
        DrawScenes.Building(sim, 2, 20, 8, BuildingSlot.TownHall);
        DrawScenes.GoldNode(sim, 26, 9, 400);
        EntityHandle worker = DrawScenes.Unit(sim, 0, 24, 12, UnitSlot.Worker);
        DrawScenes.Broke(sim);
        sim.Enqueue(Command.Gather(0, worker, new Vector2(27f, 10f) * Map.MapConstants.CellSize));
        int carriedMax = 0;
        while (!sim.MatchOver && sim.TickNumber < 1500)
        {
            sim.Tick();
            Check(w);
            Assert.Equal(0, w.Gold[0]);
            Assert.False(w.CanStillTrain(0));
            carriedMax = Math.Max(carriedMax, (int)w.Ledger.CarriedGold[0]);
        }
        _out.WriteLine($"the worker carried up to {carriedMax}; bank {w.Gold[0]}; drawn {w.MatchDrawn} at {sim.TickNumber}");
        Assert.True(carriedMax > 0, "the scene: the worker gathered");
        Assert.True(w.MatchDrawn);
    }

    /// <summary>
    /// Every queue path against a scan: a building with a started unit head, two waiting units and a research behind; a middle item
    /// cancelled; the started head cancelled (the next starts); the building freed with its queue; and a worker switching its cargo
    /// kind (the old load dropped) and dying with wood.
    /// </summary>
    [Fact]
    public void TheCounters_EqualAScan_ThroughEveryQueuePath()
    {
        Simulation sim = Base();
        World w = sim.World;
        w.Ledger.Gold[0] = 5000;
        w.Ledger.Wood[0] = 5000;
        EntityHandle hall = DrawScenes.Building(sim, 0, 20, 8, BuildingSlot.TownHall);
        int[] trains = w.Data.UnitsTrainedAt(w.Buildings.TypeId[hall.Index]).ToArray();
        Vector2 at = DrawScenes.Centre(sim, hall);
        for (int k = 0; k < 3; k++) sim.Enqueue(Command.Train(0, at, trains[0]));
        int tech = -1;
        BuildingSlot slot = w.Data.Buildings[w.Buildings.TypeId[hall.Index]].Slot;
        for (int t = 0; t < w.Data.Techs.Length && tech < 0; t++)
            if (w.Data.Techs[t].ResearchedAtSlot == slot && (w.Data.Techs[t].Faction < 0 || w.Data.Techs[t].Faction == w.FactionOf(0))) tech = t;
        if (tech >= 0) sim.Enqueue(Command.Research(0, at, tech));
        TwoTicks(sim);
        Assert.Equal(1, w.Ledger.TrainingUnits[0]);
        Assert.Equal(2, w.Ledger.WaitingUnits(0, trains[0]));
        sim.Enqueue(Command.CancelTrain(0, at, 1)); // a waiting middle item
        TwoTicks(sim);
        Assert.Equal(1, w.Ledger.WaitingUnits(0, trains[0]));
        sim.Enqueue(Command.CancelTrain(0, at, 0)); // the started head: the next starts the same tick or the next
        TwoTicks(sim);
        TwoTicks(sim);
        Assert.Equal(1, w.Ledger.TrainingUnits[0]);
        Assert.Equal(0, w.Ledger.WaitingUnits(0, trains[0]));
        sim.Enqueue(Command.Train(0, at, trains[0]));
        TwoTicks(sim);
        Assert.True(w.Buildings.Free(hall)); // the whole queue at once
        Check(w);
        Assert.Equal(0, w.Ledger.TrainingUnits[0]);
        Assert.Equal(0, w.Ledger.QueuedUnits[0]);
        // A worker with a load of gold sent to a tree: the gold dropped (counted out), wood gathered in.
        EntityHandle hall2 = DrawScenes.Building(sim, 0, 20, 20, BuildingSlot.TownHall);
        EntityHandle worker = DrawScenes.Unit(sim, 0, 30, 30, UnitSlot.Worker);
        w.Units.CargoKind[worker.Index] = ResourceKind.Gold;
        w.Units.Cargo[worker.Index] = 3;
        w.RecountDraw();
        int woodType = -1;
        for (int t = 0; t < w.Data.Resources.Length; t++)
            if (w.Data.Resources[t].Resource == ResourceKind.Wood) woodType = t;
        Assert.True(w.Resources.Spawn(woodType, 32 * w.NavGrid.Width + 33, 200, out _));
        Check(w);
        sim.Enqueue(Command.Gather(0, worker, new Vector2(33.5f, 32.5f) * Map.MapConstants.CellSize));
        for (int t = 0; t < 200 && w.Ledger.CarriedWood[0] == 0; t++)
        {
            sim.Tick();
            Check(w);
        }
        Assert.Equal(0, w.Ledger.CarriedGold[0]);
        Assert.True(w.Ledger.CarriedWood[0] > 0, "the worker never gathered wood");
        w.Units.Free(worker);
        Check(w);
        Assert.Equal(0, w.Ledger.CarriedWood[0]);
        Assert.True(w.Buildings.IsAlive(hall2));
    }

    /// <summary>
    /// The developer's new-counts scene (wood carried, a drop-off site, gold on the map, a paid item at the cap): a save at every
    /// second of the running clock (every 20th tick, clock 1 to 99) loads to the same hash, clock and counters and draws on the
    /// same tick with the same hash.
    /// </summary>
    [Fact]
    public void ASaveInEverySecondOfTheClock_LoadsEqual_AndDrawsOnTheSameTick()
    {
        Simulation sim = DrawReachPropertyTests.NewCountsScene();
        var saves = new List<(int Clock, byte[] Bytes)>();
        while (!sim.MatchOver)
        {
            sim.Tick();
            int c = sim.World.DrawClock;
            if (!sim.MatchOver && (c == 1 || c % 20 == 0 || c == sim.World.DrawWaitTicks - 1)) saves.Add((c, SaveTestRun.Save(sim)));
            Assert.True(sim.TickNumber < 1000);
        }
        Assert.True(sim.World.MatchDrawn);
        foreach ((int clock, byte[] bytes) in saves)
        {
            Simulation l = SaveTestRun.Load(DrawScenes.Short, bytes);
            Assert.Equal(clock, l.World.DrawClock);
            Check(l.World);
            while (!l.MatchOver && l.TickNumber <= sim.TickNumber) l.Tick();
            Assert.True(l.World.MatchDrawn, $"the save at clock {clock} did not draw");
            Assert.Equal(sim.TickNumber, l.TickNumber);
            Assert.Equal(sim.StateHash(), l.StateHash());
        }
        _out.WriteLine($"drawn at {sim.TickNumber}; {saves.Count} saves (clocks {string.Join(",", saves.Select(s => s.Clock))}) each drew on the same tick");
    }

    /// <summary>
    /// A builder finishing a Town Hall site while the clock runs: saves on each of the five ticks before the completion, the
    /// completion tick and the five after load equal, follow the run hash for hash to tick 400, and agree on the clock.
    /// </summary>
    [Fact]
    public void SavesOnEveryTickAroundADropOffsCompletion_FollowTheRun()
    {
        Simulation sim = Base();
        World w = sim.World;
        DrawScenes.GoldNode(sim, 34, 30, 500);
        EntityHandle worker = DrawScenes.Unit(sim, 0, 20, 14, UnitSlot.Worker);
        EntityHandle site = DrawScenes.Building(sim, 0, 20, 8, BuildingSlot.TownHall, site: true);
        int type = w.Buildings.TypeId[site.Index];
        sim.Enqueue(Command.Build(0, worker, type, w.NavGrid.CellCenter(20, 8)));
        // Run until the builder is at work, then put the site 5 ticks short of done.
        for (int t = 0; t < 200 && w.Buildings.Work[site.Index] == 0; t++) sim.Tick();
        Assert.True(w.Buildings.Work[site.Index] > 0, "the builder never started");
        int perTick = w.Buildings.Work[site.Index];
        sim.Tick();
        perTick = w.Buildings.Work[site.Index] - perTick;
        Assert.True(perTick > 0);
        w.Buildings.SetWork(site.Index, w.Buildings.WorkNeeded(type) - 5 * perTick - 1);
        Assert.True(w.DrawClock > 0, "the scene: the clock runs before the hall is done");
        var saves = new List<byte[]>();
        int finishedAt = -1;
        while (sim.TickNumber < 400)
        {
            if (finishedAt < 0 || sim.TickNumber <= finishedAt + 5) saves.Add(SaveTestRun.Save(sim));
            sim.Tick();
            if (finishedAt < 0 && !w.Buildings.UnderConstruction[site.Index]) finishedAt = sim.TickNumber;
        }
        Assert.True(finishedAt > 0);
        Assert.Equal(0, w.DrawClock);
        Assert.False(w.MatchSpent);
        int checkedSaves = 0;
        foreach (byte[] bytes in saves.Skip(Math.Max(0, saves.Count - 12)))
        {
            Simulation l = SaveTestRun.Load(DrawScenes.Short, bytes);
            Check(l.World);
            while (l.TickNumber < sim.TickNumber) l.Tick();
            Assert.Equal(sim.StateHash(), l.StateHash());
            Assert.Equal(w.DrawClock, l.World.DrawClock);
            checkedSaves++;
        }
        _out.WriteLine($"site finished at tick {finishedAt} ({perTick} work a tick); {checkedSaves} saves around it followed the run to {sim.TickNumber}");
        Assert.Equal(12, checkedSaves);
    }

    /// <summary>
    /// BUG-1893 (S3, a limit of docs/01 2026-11-27 (1): "a player who could still buy a new drop-off from the bank is not looked
    /// at (the bank is then nearly always enough for a unit anyway)"), pinned as built so a change of the rule is seen. Shipped
    /// costs: a Malazan seat with a Laborer, its Infantry Hall (a producer), no drop-off, gold on the map and a bank of one gold
    /// under its cheapest unit and the Depot's wood: it is spent, and the clock runs, although its Laborer can build the Depot
    /// from the bank and then mine the gold. Once the Depot is finished it is not spent (the clock stops).
    /// </summary>
    [Fact]
    public void Bug1893_ABankThatBuysADropOff_IsNotReach_TheSeatIsSpentUntilTheDropOffIsFinished()
    {
        Simulation sim = Base(TestSim.Data); // the shipped 300 s wait
        World w = sim.World;
        int depot = -1;
        for (int t = 0; t < w.Data.Buildings.Length; t++)
        {
            BuildingDef d = w.Data.Buildings[t];
            if (d.Faction == w.FactionOf(0) && d.DropOff && (depot < 0 || d.CostGold + d.CostWood < w.Data.Buildings[depot].CostGold + w.Data.Buildings[depot].CostWood)) depot = t;
        }
        BuildingDef dd = w.Data.Buildings[depot];
        int cheapestGold = DrawScenes.Cheapest(sim, 0);
        Assert.True(dd.CostGold < cheapestGold, $"precondition: {dd.Key} costs {dd.CostGold} gold, under the cheapest unit's {cheapestGold}");
        DrawScenes.GoldNode(sim, 34, 30, 2000);
        EntityHandle worker = DrawScenes.Unit(sim, 0, 20, 14, UnitSlot.Worker);
        w.Ledger.Gold[0] = cheapestGold - 1;
        w.Ledger.Wood[0] = Math.Max(dd.CostWood, 100);
        Assert.False(w.CanStillTrain(0), "as built: the bank's Depot is not reach");
        Assert.True(w.MatchSpent);
        sim.Enqueue(Command.Build(0, worker, depot, w.NavGrid.CellCenter(22, 10)));
        int spentTicks = 0;
        while (!sim.MatchOver && sim.TickNumber < 3000 && !w.CanStillTrain(0))
        {
            sim.Tick();
            if (w.DrawClock > 0) spentTicks++;
        }
        _out.WriteLine($"{dd.Key} ({dd.CostGold} gold / {dd.CostWood} wood, {dd.BuildTicks} ticks) bought from a bank of {cheapestGold - 1} gold; spent {spentTicks} ticks until it stood (tick {sim.TickNumber}); drawn {w.MatchDrawn}");
        Assert.True(w.CanStillTrain(0), "the finished Depot makes the map's gold reach");
        Assert.Equal(1, w.Ledger.FinishedDropOffs[0]);
    }

    private const string ChildVariable = "RTS_M6SH3_QA_DRAW_CHILD";
    private const string ChildLine = "M6SH3-QA-DRAW ";

    /// <summary>
    /// The new-counts scene with a save at every second of its clock, each loaded and played to the draw: a digest of every
    /// (clock, draw tick, end hash, fold of the loaded run's hashes) and the uninterrupted run's.
    /// </summary>
    private static string SavedClockDigest()
    {
        Simulation sim = DrawReachPropertyTests.NewCountsScene();
        var saves = new List<byte[]>();
        var fold = new Determinism.StateHasher();
        while (!sim.MatchOver && sim.TickNumber < 1000)
        {
            sim.Tick();
            fold.Add(sim.StateHash());
            if (!sim.MatchOver && sim.World.DrawClock % 20 == 0) saves.Add(SaveTestRun.Save(sim));
        }
        var parts = new List<string> { $"run {sim.TickNumber} {sim.StateHash():X16} {fold.Value:X16}" };
        foreach (byte[] bytes in saves)
        {
            Simulation l = SaveTestRun.Load(DrawScenes.Short, bytes);
            var f = new Determinism.StateHasher();
            int clock = l.World.DrawClock;
            while (!l.MatchOver && l.TickNumber < 1000)
            {
                l.Tick();
                f.Add(l.StateHash());
            }
            parts.Add($"{clock}:{l.TickNumber}:{l.StateHash():X16}:{f.Value:X16}");
        }
        return string.Join(" ", parts);
    }

    /// <summary>QA focus 3, two processes: the saved-clock digest above is the same in this process and in a child process.</summary>
    [Fact]
    public void ASaveInEverySecondOfTheClock_TwoProcesses_SameDigest()
    {
        string mine = SavedClockDigest();
        if (Environment.GetEnvironmentVariable(ChildVariable) == "1")
        {
            _out.WriteLine(ChildLine + mine);
            return;
        }
        string child = MapFileTests.RunChild(typeof(M6Sh3DrawQaTests), nameof(ASaveInEverySecondOfTheClock_TwoProcesses_SameDigest), ChildVariable, ChildLine);
        _out.WriteLine($"this process {mine}");
        Assert.Equal(mine, child);
    }

    private static void TwoTicks(Simulation sim)
    {
        sim.Tick();
        Check(sim.World);
        sim.Tick();
        Check(sim.World);
    }

    private static int FirstBuilding(World w, int p)
    {
        for (int i = 0; i < w.Buildings.Capacity; i++)
            if (w.Buildings.Alive[i] && w.Buildings.Owner[i] == p) return i;
        return -1;
    }
}
