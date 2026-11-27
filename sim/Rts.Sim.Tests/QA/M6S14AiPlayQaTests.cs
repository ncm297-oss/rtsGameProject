using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// QA, M6-S14 (session 2026-11-26-1847, tier full): attacks on the AI-play session's rules beyond the developer's rows.
/// The worker floor (B, <c>ai.minWorkers</c>): every faction and difficulty, a hall still being built, two halls, the
/// population room it claims to respect, and a save taken on the tick a floor Train is in flight. The rally search's memory
/// (C, BUG-1555's family): build, raze and rebuild out of sight in every order, and a node never seen, each read cell by cell
/// and as a block. The hunt's resume (A): two finds in a row, and a resumed hunt still reaching a far building.
/// </summary>
public class M6S14AiPlayQaTests
{
    private readonly ITestOutputHelper _out;

    public M6S14AiPlayQaTests(ITestOutputHelper output) => _out = output;

    // ---------------------------------------------------------------- the floor (item B)

    /// <summary>Every faction's every opening: one worker Train at time 0, nothing else (the shipped floor).</summary>
    private static readonly Lazy<GameData> s_oneWorker = new(() => AiScenes.DataWith(dir =>
    {
        foreach (string f in TestSim.ShippedFactions)
            dir.EditJson($"factions/{f}/ai.json", root =>
            {
                string worker = AiScenes.UnitKey(f, UnitSlot.Worker);
                foreach (JsonNode? o in root["openings"]!.AsArray())
                    AiScenes.SetBuildOrder(o, AiScenes.BuildOrder(("time", 0, "train", worker, 1)));
            });
    }));

    /// <summary>Player 0 an AI of <paramref name="faction"/> at <paramref name="difficulty"/>, player 1 a human of another faction; combat off.</summary>
    private static Simulation FloorScene(string faction = "malazan", AiDifficulty difficulty = AiDifficulty.Normal)
    {
        int f = TestSim.Data.FindFaction(faction), other = f == 1 ? 0 : 1;
        var seats = new[] { new PlayerSeat(f, SeatController.Ai, difficulty), new PlayerSeat(other, SeatController.Human, difficulty) };
        Simulation sim = AiScenes.Flat(seats, s_oneWorker.Value);
        BuildMaps.Give(sim, 0, 1000, 1000);
        return sim;
    }

    private static bool IsWorker(World w, int type) => w.Data.Units[type].IsWorker;

    /// <summary>Frees every worker of player 0 but the lowest slot's.</summary>
    private static void KeepOneWorker(Simulation sim)
    {
        UnitStore u = sim.World.Units;
        bool kept = false;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.Owner[i] != 0 || !IsWorker(sim.World, u.TypeId[i])) continue;
            if (!kept) kept = true;
            else u.Free(new EntityHandle(i, u.Generation[i]));
        }
        Assert.True(kept);
    }

    private static int LiveWorkers(World w)
    {
        int n = 0;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 0 && IsWorker(w, w.Units.TypeId[i])) n++;
        return n;
    }

    private static int QueuedWorkers(World w)
    {
        int n = 0;
        for (int k = 0; k < w.Buildings.Capacity; k++)
        {
            if (!w.Buildings.Alive[k] || w.Buildings.Owner[k] != 0) continue;
            for (int q = 0; q < w.Buildings.QueueCount[k]; q++)
                if (!w.Buildings.QueueIsTechAt(k, q) && IsWorker(w, w.Buildings.QueueTypeAt(k, q))) n++;
        }
        return n;
    }

    private static EntityHandle OwnHall(World w, int player = 0)
    {
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == player && w.Data.Buildings[w.Buildings.TypeId[k]].Slot == BuildingSlot.TownHall)
                return w.Buildings.HandleOf(k);
        return default;
    }

    /// <summary>
    /// Every shipped faction (the Teblor gatherer carries 15 and costs 75) at every difficulty: a seat cut to one worker
    /// trains one worker a think until live + queued reach <c>ai.minWorkers</c>, then none, never two in one think.
    /// </summary>
    [Theory]
    [InlineData("malazan", AiDifficulty.Easy)]
    [InlineData("malazan", AiDifficulty.Hard)]
    [InlineData("whirlwind", AiDifficulty.Easy)]
    [InlineData("whirlwind", AiDifficulty.Normal)]
    [InlineData("whirlwind", AiDifficulty.Hard)]
    [InlineData("teblor", AiDifficulty.Easy)]
    [InlineData("teblor", AiDifficulty.Normal)]
    [InlineData("teblor", AiDifficulty.Hard)]
    public void Floor_EveryFactionAndDifficulty_TrainsUpToTheFloor_OneAThink(string faction, AiDifficulty difficulty)
    {
        Simulation sim = FloorScene(faction, difficulty);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        List<Command> log = AiScenes.Log(sim, 0);
        GatherMaps.Run(sim, 2000);
        KeepOneWorker(sim);
        BuildMaps.SetTotals(sim, 0, 1000, 1000);
        int from = sim.TickNumber;
        GatherMaps.Run(sim, 60 * ai.ThinkTicks);
        var trains = log.Where(c => c.Tick > from && c.Kind == CommandKind.Train && IsWorker(w, c.TypeId)).ToList();
        _out.WriteLine($"{faction} {difficulty}: think {ai.ThinkTicks} ticks; worker Trains {trains.Count} at {string.Join(", ", trains.Select(c => c.Tick))}; live {LiveWorkers(w)} queued {QueuedWorkers(w)}");
        Assert.Equal(ai.Rules.MinWorkers - 1, trains.Count);
        Assert.Equal(trains.Count, trains.Select(c => c.Tick).Distinct().Count());
        Assert.Equal(ai.Rules.MinWorkers, LiveWorkers(w) + QueuedWorkers(w));
    }

    /// <summary>
    /// A seat whose only hall is a construction site (no finished building trains its worker) sends no worker Train and
    /// nothing throws, however long it waits with one worker and the gold; once its worker finishes the hall, the floor fills.
    /// </summary>
    [Fact]
    public void Floor_AHallUnderConstructionOnly_SendsNoTrain_UntilItIsFinished()
    {
        Simulation sim = FloorScene();
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        List<Command> log = AiScenes.Log(sim, 0);
        GatherMaps.Run(sim, 2000);
        KeepOneWorker(sim);
        EntityHandle hall = OwnHall(w);
        int type = w.Buildings.TypeId[hall.Index], cell = w.Buildings.Cell[hall.Index];
        Assert.True(w.Buildings.Free(hall));
        Assert.True(w.Buildings.Spawn(0, type, cell, out EntityHandle site, site: true));
        BuildMaps.SetTotals(sim, 0, 1000, 0);
        int from = sim.TickNumber, finished = -1, firstTrain = -1;
        for (int t = 0; t < 12_000 && (finished < 0 || sim.TickNumber < finished + 40 * ai.ThinkTicks); t++)
        {
            sim.Tick();
            if (finished < 0 && w.Buildings.IsAlive(site) && !w.Buildings.UnderConstruction[site.Index]) finished = sim.TickNumber;
            if (firstTrain < 0 && log.Any(c => c.Tick > from && c.Kind == CommandKind.Train && IsWorker(w, c.TypeId))) firstTrain = sim.TickNumber;
        }
        _out.WriteLine($"site placed {from}, finished {finished}, first worker Train {firstTrain}; live {LiveWorkers(w)} queued {QueuedWorkers(w)}");
        Assert.True(finished > 0, "the seat's worker never finished its hall");
        Assert.True(firstTrain < 0 || firstTrain >= finished, $"a worker Train at {firstTrain} before the hall was finished at {finished}");
        Assert.Equal(ai.Rules.MinWorkers, LiveWorkers(w) + QueuedWorkers(w));
    }

    /// <summary>Two finished halls: the floor counts both queues, still one Train a think, and stops at the floor.</summary>
    [Fact]
    public void Floor_TwoHalls_StillOneTrainAThink_AndStopsAtTheFloor()
    {
        Simulation sim = FloorScene();
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        List<Command> log = AiScenes.Log(sim, 0);
        GatherMaps.Run(sim, 2000);
        KeepOneWorker(sim);
        EntityHandle hall = OwnHall(w);
        Assert.True(w.Buildings.Spawn(0, w.Buildings.TypeId[hall.Index], 50 * AiScenes.FlatSize + 10, out _));
        BuildMaps.SetTotals(sim, 0, 1000, 0);
        int from = sim.TickNumber;
        GatherMaps.Run(sim, 40 * ai.ThinkTicks);
        var trains = log.Where(c => c.Tick > from && c.Kind == CommandKind.Train && IsWorker(w, c.TypeId)).ToList();
        _out.WriteLine($"two halls: worker Trains {trains.Count} at {string.Join(", ", trains.Select(c => c.Tick))}; live {LiveWorkers(w)} queued {QueuedWorkers(w)}");
        Assert.Equal(ai.Rules.MinWorkers - 1, trains.Count);
        Assert.Equal(trains.Count, trains.Select(c => c.Tick).Distinct().Count());
        Assert.Equal(ai.Rules.MinWorkers, LiveWorkers(w) + QueuedWorkers(w));
    }

    /// <summary>
    /// docs/03 M6-S14 B: "with a worker alive or queued it also needs population room now (a Train accepted at the cap waits
    /// paid in the queue, BUG-1731's case)". A seat with one worker and room for exactly two more workers under its cap
    /// (soldiers fill the rest): while the cap has not moved, the floor may send at most two worker Trains, and no paid worker
    /// may sit in a queue unable to start for want of room. A worker in a queue that has not started reserves no population
    /// (<c>ProductionSystem.TryStart</c>), so the room test reads the same room on the next think (BUG-1857).
    /// </summary>
    [Fact(Skip = "BUG-1857: the floor's room test does not count workers queued but not started (QA measured 3 Trains with room for 2, a paid worker waiting at the cap 2,091 ticks); un-skip with the fix")]
    public void Floor_RoomForTwoWorkers_SendsAtMostTwo_NoPaidWorkerWaitsAtTheCap()
    {
        Simulation sim = FloorScene();
        World w = sim.World;
        List<Command> log = AiScenes.Log(sim, 0);
        GatherMaps.Run(sim, 2000);
        KeepOneWorker(sim);
        int heavy = w.Data.FindUnit("malazan_heavy_infantry"), worker = w.Data.FindUnit("malazan_laborer");
        int lab = w.Data.Units[worker].ItemHalfPop, hv = w.Data.Units[heavy].ItemHalfPop;
        for (int k = 0; k < 300 && w.HalfPop[0] + hv + 2 * lab <= w.HalfPopCap[0]; k++)
            CombatScenes.Place(sim, 0, heavy, w.NavGrid.CellCenter(56 + k % 6, 2 + k / 6));
        int cap = w.HalfPopCap[0], room = cap - w.HalfPop[0];
        Assert.InRange(room, 2 * lab, 3 * lab - 1);
        BuildMaps.SetTotals(sim, 0, 1000, 0);
        int from = sim.TickNumber, capMoved = -1, waitingTicks = 0, firstWaiting = -1;
        EntityHandle hall = OwnHall(w);
        while (sim.TickNumber < from + 3000)
        {
            sim.Tick();
            if (capMoved < 0 && w.HalfPopCap[0] != cap) capMoved = sim.TickNumber;
            int k = hall.Index;
            bool headWaits = w.Buildings.QueueCount[k] > 0 && !w.Buildings.QueueIsTechAt(k, 0) && IsWorker(w, w.Buildings.QueueTypeAt(k, 0))
                && w.Buildings.ProgressOf(k) == 0 && w.HalfPop[0] + lab > w.HalfPopCap[0];
            if (headWaits) { waitingTicks++; if (firstWaiting < 0) firstWaiting = sim.TickNumber; }
        }
        int bound = capMoved < 0 ? int.MaxValue : capMoved;
        var trains = log.Where(c => c.Tick > from && c.Tick < bound && c.Kind == CommandKind.Train && c.TypeId == worker).ToList();
        _out.WriteLine($"room {room} half-pop ({lab} a worker), cap {cap}; worker Trains before the cap moved (at {capMoved}): {trains.Count} at {string.Join(", ", trains.Select(c => c.Tick))}; "
            + $"a paid worker waited at the cap {waitingTicks} ticks (first {firstWaiting}); end: live {LiveWorkers(w)}, queued {QueuedWorkers(w)}, half-pop {w.HalfPop[0]} / {w.HalfPopCap[0]}");
        Assert.True(trains.Count <= room / lab, $"{trains.Count} worker Trains sent with room for {room / lab}");
        Assert.True(waitingTicks == 0, $"a paid worker waited at the cap for {waitingTicks} ticks from {firstWaiting}");
    }

    /// <summary>
    /// Criterion 8 at the awkward tick: a save taken right after the think that sent a floor Train (the command in flight, the
    /// board's pending order set) and one taken the tick it applies each load equal field for field, save again to the same
    /// bytes, and follow the run hash for hash for 2,000 ticks; a same-seed twin follows it too.
    /// </summary>
    [Fact]
    public void Floor_SavesOnTheTickAFloorTrainIsSentAndApplies_FollowTheRun()
    {
        Simulation a = FloorScene(), b = FloorScene();
        AiPlayer ai = a.AiOf(0)!;
        World w = a.World;
        List<Command> log = AiScenes.Log(a, 0);
        GatherMaps.Run(a, 2000);
        GatherMaps.Run(b, 2000);
        KeepOneWorker(a);
        KeepOneWorker(b);
        int from = a.TickNumber;
        while (!log.Any(c => c.Tick > from && c.Kind == CommandKind.Train && IsWorker(w, c.TypeId)) && a.TickNumber < from + 400)
        {
            a.Tick();
            b.Tick();
        }
        Assert.True(a.PendingCommandCount > 0, "the scene: no floor Train in flight");
        byte[] sent = SaveTestRun.Save(a);
        Simulation loadSent = SaveTestRun.Load(a.World.Data, sent);
        Assert.Equal(sent, SaveTestRun.Save(loadSent));
        SaveTestRun.AssertSameState(a, loadSent, "the floor Train in flight");
        a.Tick();
        b.Tick();
        loadSent.Tick();
        Assert.True(QueuedWorkers(w) > 0, "the floor Train did not apply");
        byte[] applied = SaveTestRun.Save(a);
        Simulation loadApplied = SaveTestRun.Load(a.World.Data, applied);
        Assert.Equal(applied, SaveTestRun.Save(loadApplied));
        for (int t = 0; t < 2000; t++)
        {
            a.Tick();
            b.Tick();
            loadSent.Tick();
            loadApplied.Tick();
            ulong h = a.StateHash();
            Assert.True(h == b.StateHash(), $"twins diverged at {a.TickNumber}");
            Assert.True(h == loadSent.StateHash(), $"the in-flight save diverged at {a.TickNumber}");
            Assert.True(h == loadApplied.StateHash(), $"the applied save diverged at {a.TickNumber}");
        }
        SaveTestRun.AssertSameState(a, loadSent, "in-flight save at the end");
        SaveTestRun.AssertSameState(a, loadApplied, "applied save at the end");
        Assert.Equal(ai.Rules.MinWorkers, LiveWorkers(w) + QueuedWorkers(w));
    }

    // ---------------------------------------------------------------- the memory (item C, BUG-1555's family)

    private static void See(World w) => w.Fog.Update();

    /// <summary>Every cell of the block around (<paramref name="x"/>, <paramref name="y"/>) read by the cell and by the window: equal; returns the cell answers.</summary>
    private static bool[] Read(PlayerView v, int x0, int y0, int side)
    {
        var cells = new bool[side * side];
        var window = new bool[side * side];
        v.KnownClosedWindow(x0, y0, side, side, window);
        for (int dy = 0; dy < side; dy++)
            for (int dx = 0; dx < side; dx++)
            {
                cells[dy * side + dx] = v.IsKnownClosed(x0 + dx, y0 + dy);
                Assert.True(cells[dy * side + dx] == window[dy * side + dx], $"cell ({x0 + dx}, {y0 + dy}): IsKnownClosed {cells[dy * side + dx]}, window {window[dy * side + dx]}");
            }
        return cells;
    }

    private static bool Footprint(World w, int type, int anchor, int x, int y)
    {
        BuildingDef d = w.Data.Buildings[type];
        int ax = anchor % w.NavGrid.Width, ay = anchor / w.NavGrid.Width;
        return x >= ax && x < ax + d.FootprintWidth && y >= ay && y < ay + d.FootprintHeight;
    }

    /// <summary>
    /// BUG-1555's family, the orders the developer's rows do not play (player 0 an AI, player 1 an enemy, nothing explored):
    /// (1) an enemy building built and razed wholly out of sight is never read closed; (2) one seen, then razed out of sight
    /// and replaced out of sight by a bigger building: the ghost's footprint reads closed, the new building's cells outside it
    /// read open (nothing learned through the fog), and once seen the new one's whole footprint reads closed; (3) one razed
    /// while seen reads open at once; (4) a tree never seen, felled, never reads closed. Cell reads and block reads agree.
    /// </summary>
    [Fact]
    public void KnownClosed_BuildRazeRebuildOutOfSight_LearnsNothingThroughTheFog()
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), explored: false);
        World w = sim.World;
        PlayerView v = sim.AiOf(0)!.View;
        int size = AiScenes.FlatSize;
        int tent = w.Data.FindBuilding("whirlwind_tent");
        int big = w.Data.FindBuilding(AiScenes.BuildingKey("whirlwind", BuildingSlot.TownHall));
        int laborer = w.Data.FindUnit("malazan_laborer");
        int x0 = 28, y0 = 48, side = 12, anchor = 52 * size + 32;
        See(w);
        Assert.False(w.Fog.IsVisible(0, anchor), "precondition: the spot is out of player 0's sight");

        // (1) built and razed unseen.
        Assert.True(w.Buildings.Spawn(1, tent, anchor, out EntityHandle unseen));
        See(w);
        bool[] r1 = Read(v, x0, y0, side);
        Assert.True(w.Buildings.Free(unseen));
        See(w);
        bool[] r1b = Read(v, x0, y0, side);
        Assert.DoesNotContain(true, r1);
        Assert.DoesNotContain(true, r1b);

        // (2) seen, razed unseen, rebuilt bigger unseen.
        Assert.True(w.Buildings.Spawn(1, tent, anchor, out EntityHandle seenTent));
        EntityHandle eye = CombatScenes.Place(sim, 0, laborer, w.NavGrid.CellCenter(30, 46));
        See(w);
        Assert.True(w.Fog.IsVisible(0, anchor), "the scene: the eye sees the tent");
        w.Units.Free(eye);
        See(w);
        Assert.False(w.Fog.IsVisible(0, anchor));
        Assert.True(w.Buildings.Free(seenTent));
        Assert.True(w.Buildings.Spawn(1, big, anchor, out EntityHandle rebuilt));
        See(w);
        bool[] r2 = Read(v, x0, y0, side);
        int ghostClosed = 0, newOutsideOpen = 0, newOutside = 0;
        for (int dy = 0; dy < side; dy++)
            for (int dx = 0; dx < side; dx++)
            {
                int x = x0 + dx, y = y0 + dy;
                bool inGhost = Footprint(w, tent, anchor, x, y), inNew = Footprint(w, big, anchor, x, y);
                bool closed = r2[dy * side + dx];
                if (inGhost) { Assert.True(closed, $"ghost cell ({x}, {y}) read open"); ghostClosed++; }
                else if (inNew) { newOutside++; if (!closed) newOutsideOpen++; Assert.False(closed, $"the unseen new building's cell ({x}, {y}) read closed: learned through the fog"); }
                else Assert.False(closed, $"cell ({x}, {y}), nothing known there, read closed");
            }
        Assert.True(newOutside > 0, "the scene: the new building is not bigger than the ghost");
        CombatScenes.Place(sim, 0, laborer, w.NavGrid.CellCenter(30, 46));
        See(w);
        bool[] r2b = Read(v, x0, y0, side);
        for (int dy = 0; dy < side; dy++)
            for (int dx = 0; dx < side; dx++)
            {
                int x = x0 + dx, y = y0 + dy;
                if (!w.Fog.IsVisible(0, y * size + x)) continue;
                Assert.Equal(Footprint(w, big, anchor, x, y), r2b[dy * side + dx]);
            }

        // (3) razed while seen: open at once.
        Assert.True(w.Buildings.Free(rebuilt));
        See(w);
        bool[] r3 = Read(v, x0, y0, side);
        for (int dy = 0; dy < side; dy++)
            for (int dx = 0; dx < side; dx++)
                if (w.Fog.IsVisible(0, (y0 + dy) * size + x0 + dx)) Assert.False(r3[dy * side + dx], $"cell ({x0 + dx}, {y0 + dy}) razed in sight read closed");

        // (4) a tree never seen, felled: never closed.
        int tree = w.Data.FindResource("tree"), tx = 56, ty = 58;
        Assert.True(w.Resources.Spawn(tree, ty * size + tx, w.Data.Rules.TreeWood, out EntityHandle node));
        See(w);
        Assert.False(w.Fog.IsExplored(0, ty * size + tx), "precondition: the tree's ground is unexplored");
        bool before = v.IsKnownClosed(tx, ty);
        w.Resources.Take(node, int.MaxValue);
        sim.Tick();
        See(w);
        bool after = v.IsKnownClosed(tx, ty);
        Read(v, 50, 52, 12);
        _out.WriteLine($"(1) unseen: none closed; (2) ghost cells closed {ghostClosed}, the new building's cells outside it {newOutside}, open {newOutsideOpen}; (4) tree unseen {before} / felled {after}");
        Assert.False(before);
        Assert.False(after);
    }

    // ---------------------------------------------------------------- the hunt (item A)

    private static int Heavy => TestSim.Data.FindUnit("malazan_heavy_infantry");

    /// <summary>The developer's hunt data (<c>Ai/HuntResumeTests</c>): a quiet Malazan order, a wave threshold any army reaches.</summary>
    private static readonly Lazy<GameData> s_huntData = new(() => AiScenes.DataWith(dir =>
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            foreach (JsonNode? o in root["openings"]!.AsArray())
            {
                AiScenes.SetBuildOrder(o, AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1)));
                o["attack"] = new JsonObject { ["minArmyPop"] = 2, ["minArmyCost"] = 0 };
            }
        })));

    private static readonly StartLocation[] s_starts =
        { new(7 * 256 + 136, 0), new(240 * 256 + 128, 0), new(200 * 256 + 200, 0), new(200 * 256 + 40, 0) };

    private static void NextThink(Simulation sim)
    {
        int think = sim.AiOf(0)!.ThinkTicks;
        do sim.Tick(); while ((sim.TickNumber - 1) % think != 0);
    }

    /// <summary>The army's centre cell.</summary>
    private static (int X, int Y) ArmyCell(Simulation sim)
    {
        AiPlayer ai = sim.AiOf(0)!;
        Vector2 sum = Vector2.Zero;
        for (int k = 0; k < ai.ArmyCount; k++) sum += sim.World.Units.Position[ai.ArmySlots[k]];
        return ((int)(sum.X / ai.ArmyCount / MapConstants.CellSize), (int)(sum.Y / ai.ArmyCount / MapConstants.CellSize));
    }

    /// <summary>
    /// The developer's free-start scene (256 x 256 cells, four starts, two free), to a hunt in progress with a mark: player 1
    /// keeps only a Town Hall (at (90, 120) cells unless given), out of the army's sight. Returns the hall's handle.
    /// </summary>
    private static EntityHandle ToAHunt(out Simulation sim, int hallCell = 120 * 256 + 90)
    {
        sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), s_huntData.Value,
            map: LocalMovementTests.Flat(256).WithStartLocations(s_starts));
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        ai.Board.WaveCooldownUntil = int.MaxValue;
        sim.Tick();
        for (int i = 0; i < w.Buildings.Capacity; i++)
            if (w.Buildings.Alive[i] && w.Buildings.Owner[i] == 1) Assert.True(w.Buildings.Free(new EntityHandle(i, w.Buildings.Generation[i])));
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 1) w.Units.Free(new EntityHandle(i, w.Units.Generation[i]));
        int hall = s_huntData.Value.FindBuilding(AiScenes.BuildingKey("whirlwind", BuildingSlot.TownHall));
        Assert.True(w.Buildings.Spawn(1, hall, hallCell, out EntityHandle h));
        for (int k = 0; k < 4; k++) CombatScenes.Place(sim, 0, Heavy, w.NavGrid.CellCenter(70 + k % 2, 100 + k / 2));
        NextThink(sim);
        ai.Board.WavesStarted = 1;
        ai.Board.HuntsStarted = 1;
        ai.Board.WaveCooldownUntil = 0;
        NextThink(sim);
        Assert.True(ai.Board.Hunting, "no hunt began");
        while (Array.IndexOf(ai.Board.HuntVisited, true) < 0 && sim.TickNumber < 8_000) NextThink(sim);
        Assert.True(ai.Board.Hunting && Array.IndexOf(ai.Board.HuntVisited, true) >= 0, "the sweep marked nothing");
        return h;
    }

    /// <summary>A tent of player 1 six cells from the army's centre (in its sight), the hunt turned into a wave on it, then razed by the test; its centre.</summary>
    private static Vector2 FindAndRaze(Simulation sim)
    {
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        int tent = w.Data.FindBuilding("whirlwind_tent");
        (int cx, int cy) = ArmyCell(sim);
        int side = cx < 128 ? 1 : -1, cell = cy * 256 + cx + 6 * side;
        Assert.True(w.Buildings.Spawn(1, tent, cell, out EntityHandle find));
        for (int k = 0; k < 4 && ai.Board.Hunting; k++) NextThink(sim);
        Assert.False(ai.Board.Hunting, "the hunt never turned into a wave on the tent");
        Assert.True(ai.Board.WaveFromHunt);
        Assert.Equal(find, ai.Board.WaveTarget);
        Assert.True(w.Buildings.Free(find));
        return ai.BuildingCentre(tent, cell);
    }

    /// <summary>
    /// Two finds in a row: the resumed hunt finds a second building and razes it; the hunt after that resumes from the
    /// second find (its point and tick), keeps every mark, and its first target is the unvisited point nearest the second
    /// find. A save between the second raze and the next think follows the run for 1,000 ticks.
    /// </summary>
    [Fact]
    public void Hunt_TwoFindsInARow_TheThirdHuntResumesFromTheSecond()
    {
        ToAHunt(out Simulation sim);
        AiPlayer ai = sim.AiOf(0)!;
        Vector2 first = FindAndRaze(sim);
        NextThink(sim);
        Assert.True(ai.Board.Hunting && MilitaryManager.Resumes(ai));
        Assert.Equal(first, ai.Board.HuntFindPoint);
        int firstTick = ai.Board.HuntFindTick;
        for (int k = 0; k < 6; k++) NextThink(sim); // the army walks on
        Vector2 second = FindAndRaze(sim);
        bool[] marks = (bool[])ai.Board.HuntVisited.Clone();
        byte[] bytes = SaveTestRun.Save(sim);
        Simulation load = SaveTestRun.Load(s_huntData.Value, bytes);
        Assert.Equal(bytes, SaveTestRun.Save(load));
        NextThink(sim);
        _out.WriteLine($"first find {first} at {firstTick}; second {second} at {ai.Board.HuntFindTick}; hunts {ai.Board.HuntsStarted}; target {ai.Board.HuntSweep}");
        Assert.True(ai.Board.Hunting && MilitaryManager.Resumes(ai), "the hunt after the second find does not resume");
        Assert.Equal(second, ai.Board.HuntFindPoint);
        Assert.True(ai.Board.HuntFindTick > firstTick);
        for (int k = 0; k < marks.Length; k++) Assert.True(!marks[k] || ai.Board.HuntVisited[k], $"point {k}: a mark was cleared");
        int cols = ScoutManager.GridLines(256, ai.Rules.ScoutGrid), best = -1;
        float least = float.MaxValue;
        for (int k = 0; k < ai.Board.HuntVisited.Length; k++)
        {
            if (ai.Board.HuntVisited[k]) continue;
            float d = Vector2.Distance(second, MilitaryManager.HuntPoint(ai, cols, cols, k));
            if (d < least) (least, best) = (d, k);
        }
        Assert.Equal(best, ai.Board.HuntSweep);
        int at = load.TickNumber;
        while (load.TickNumber < sim.TickNumber) load.Tick();
        for (int t = 0; t < 1000; t++)
        {
            sim.Tick();
            load.Tick();
            Assert.True(sim.StateHash() == load.StateHash(), $"the save of tick {at} diverged at {sim.TickNumber}");
        }
    }

    /// <summary>
    /// "A razed find with nothing near it: does the hunt still cover the map": the resumed hunt keeps the last hunt's marks
    /// and pass, so it must still reach the only enemy building left, a Town Hall in the map's far corner (30, 30) cells, out of sight.
    /// Reported: ticks from the raze to the wave on the hall, hunts, the pass, marks; asserted: found within 30,000 ticks (QA
    /// measured 25,900 on 0058e09d, 118 of 225 marks).
    /// </summary>
    [Fact]
    public void Hunt_AResumedHunt_WithNothingNearTheFind_StillReachesTheLastBuilding()
    {
        EntityHandle hall = ToAHunt(out Simulation sim, hallCell: 30 * 256 + 30);
        AiPlayer ai = sim.AiOf(0)!;
        Vector2 find = FindAndRaze(sim);
        int razed = sim.TickNumber, found = -1;
        Vector2 hallAt = ai.BuildingCentre(sim.World.Buildings.TypeId[hall.Index], sim.World.Buildings.Cell[hall.Index]);
        while (sim.TickNumber < razed + 30_000)
        {
            NextThink(sim);
            if (ai.Board.WaveActive && !ai.Board.Hunting && ai.Board.WaveTarget == hall) { found = sim.TickNumber; break; }
        }
        int marked = ai.Board.HuntVisited.Count(m => m);
        _out.WriteLine($"find {find}; the hall {hallAt} ({Vector2.Distance(find, hallAt):F0} m off); raze {razed}; the wave on the hall at {found} ({(found < 0 ? -1 : found - razed)} ticks); hunts {ai.Board.HuntsStarted}, pass {ai.Board.HuntPass}, marks {marked} / {ai.Board.HuntVisited.Length}");
        Assert.True(Vector2.Distance(find, hallAt) > 60f, "the scene: the hall is near the find");
        Assert.True(found > 0, $"the resumed hunt never reached the last building in 30,000 ticks (hunts {ai.Board.HuntsStarted}, pass {ai.Board.HuntPass}, marks {marked})");
    }
}
