using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-SH3 C (docs/03 "Implementation (M6-SH3)" C): the AI cures, each with a row that failed on the base 0159c425 and a
/// determinism twin: BUG-1857 (the worker floor's room test counts the worker items queued and not started), BUG-1852 (a
/// loaded idle worker's Gather to a gone node is judged and the node marked), Q7 (the kept rally point is forgotten when the
/// hall point changes).
/// </summary>
public class M6Sh3AiCureTests
{
    private readonly ITestOutputHelper _out;

    public M6Sh3AiCureTests(ITestOutputHelper output) => _out = output;

    // ---------------------------------------------------------------- BUG-1857: the floor's room

    /// <summary>Every opening of every faction: one worker Train at time 0, nothing else (the floor alone trains after it).</summary>
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

    /// <summary>
    /// A Normal Malazan AI seat on the flat map (combat off; a human Whirlwind seat far away) after 2,000 ticks of play, cut down
    /// to one Laborer, with Heavy Infantry placed until its population has room for exactly <paramref name="roomFor"/> Laborers,
    /// 1,000 gold and no wood (so no house is built while the room is measured).
    /// </summary>
    private static Simulation FloorRoomScene(int roomFor, out int lab)
    {
        var seats = new[] { new PlayerSeat(0, SeatController.Ai, AiDifficulty.Normal), new PlayerSeat(1, SeatController.Human, AiDifficulty.Normal) };
        Simulation sim = AiScenes.Flat(seats, s_oneWorker.Value);
        World w = sim.World;
        BuildMaps.Give(sim, 0, 1000, 1000);
        GatherMaps.Run(sim, 2000);
        UnitStore u = w.Units;
        bool kept = false;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.Owner[i] != 0 || !w.Data.Units[u.TypeId[i]].IsWorker) continue;
            if (!kept) kept = true;
            else u.Free(new EntityHandle(i, u.Generation[i]));
        }
        int heavy = w.Data.FindUnit("malazan_heavy_infantry"), worker = w.Data.FindUnit("malazan_laborer");
        lab = w.Data.Units[worker].ItemHalfPop;
        int hv = w.Data.Units[heavy].ItemHalfPop;
        for (int k = 0; k < 300 && w.HalfPop[0] + hv + roomFor * lab <= w.HalfPopCap[0]; k++)
            CombatScenes.Place(sim, 0, heavy, w.NavGrid.CellCenter(56 + k % 6, 2 + k / 6));
        // Then the smallest soldier, to bring the room under one Laborer more.
        int small = -1;
        foreach (int t in w.Data.Factions[w.FactionOf(0)].Units)
            if (!w.Data.Units[t].IsWorker && (small < 0 || w.Data.Units[t].ItemHalfPop < w.Data.Units[small].ItemHalfPop)) small = t;
        for (int k = 0; k < 300 && w.HalfPopCap[0] - w.HalfPop[0] - w.Data.Units[small].ItemHalfPop >= roomFor * lab && w.HalfPopCap[0] - w.HalfPop[0] >= (roomFor + 1) * lab; k++)
            CombatScenes.Place(sim, 0, small, w.NavGrid.CellCenter(50 + k % 4, 2 + k / 4));
        int room = w.HalfPopCap[0] - w.HalfPop[0];
        Assert.InRange(room, roomFor * lab, (roomFor + 1) * lab - 1);
        BuildMaps.SetTotals(sim, 0, 1000, 0);
        return sim;
    }

    /// <summary>
    /// BUG-1857 (QA's row, un-skipped, covers room for two; red on the base: 3 Trains with room for 2, a paid Laborer waiting at
    /// the cap 2,091 ticks): with room for 0, 1, 2 or 3 Laborers and the floor wanting 3 more, the seat sends at most that many
    /// worker Trains before its cap moves, and no paid worker ever waits at the head of a queue without room.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Floor_WithRoomForN_SendsAtMostN_AndNoPaidWorkerWaitsAtTheCap(int roomFor)
    {
        Simulation sim = FloorRoomScene(roomFor, out int lab);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        List<Command> log = AiScenes.Log(sim, 0);
        int worker = w.Data.FindUnit("malazan_laborer");
        int cap = w.HalfPopCap[0], from = sim.TickNumber, capMoved = -1, waiting = 0;
        while (sim.TickNumber < from + 60 * ai.ThinkTicks)
        {
            sim.Tick();
            if (capMoved < 0 && w.HalfPopCap[0] != cap) capMoved = sim.TickNumber;
            BuildingStore b = w.Buildings;
            for (int k = 0; k < b.Capacity; k++)
                if (b.Alive[k] && b.Owner[k] == 0 && b.QueueCount[k] > 0 && !b.QueueIsTechAt(k, 0) && b.QueueTypeAt(k, 0) == worker
                    && b.Progress[k] == 0 && w.HalfPop[0] + lab > w.HalfPopCap[0]) waiting++;
        }
        int bound = capMoved < 0 ? int.MaxValue : capMoved;
        int trains = log.Count(c => c.Tick > from && c.Tick < bound && c.Kind == CommandKind.Train && c.TypeId == worker);
        _out.WriteLine($"room for {roomFor}: {trains} worker Trains before the cap moved (at {capMoved}); a paid worker waited at the cap {waiting} ticks");
        Assert.True(trains <= roomFor, $"{trains} worker Trains with room for {roomFor}");
        Assert.True(trains >= Math.Min(roomFor, ai.Rules.MinWorkers - 1), $"only {trains} worker Trains with room for {roomFor}: the floor did not fill the room");
        Assert.Equal(0, waiting);
    }

    // ---------------------------------------------------------------- BUG-1852: a Gather refused at a gone node

    /// <summary>A quiet Malazan order (one step an hour away) and <c>ai.gatherReach</c> at its maximum, so every remembered node is a candidate (QA's BUG-0570 scene's data).</summary>
    private static readonly Lazy<GameData> s_quietAnyReach = new(() => AiScenes.DataWith(dir =>
    {
        dir.EditJson("factions/malazan/ai.json", root =>
            AiScenes.SetBuildOrder(AiScenes.Opening(root), AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1))));
        dir.EditJson("common/rules.json", r => r["ai"]!["gatherReach"] = 512);
    }));

    /// <summary>
    /// The state BUG-1852's trace found (Raraku seed 3, unit 45 at 34,021: Idle, a full load of gold, its Town Hall standing,
    /// every Gather to the remembered mine refused <c>NoResourceNode</c>): a Hard Malazan AI seat on the flat map keeps its
    /// hall; every node of the map is taken out before the first think, so the seat remembers the far ones (out of its sight)
    /// and they are gone; its workers stand Idle, each with a full load of gold.
    /// </summary>
    private static Simulation LoadedIdleWorkersScene(out int remembered)
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Human);
        Simulation sim = AiScenes.Flat(seats, s_quietAnyReach.Value);
        World w = sim.World;
        ResourceStore r = w.Resources;
        for (int k = 0; k < r.Capacity; k++)
            if (r.Alive[k]) r.Take(new EntityHandle(k, r.Generation[k]), int.MaxValue);
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.Owner[i] != 0 || !w.Data.Units[u.TypeId[i]].IsWorker) continue;
            u.CargoKind[i] = ResourceKind.Gold;
            u.Cargo[i] = w.Data.CarryOf(u.TypeId[i]);
        }
        w.RecountDraw();
        sim.Tick(); // tick 0: the fog, the first think
        var view = new PlayerView(w, 0);
        remembered = 0;
        int s = -1;
        while (view.NextResource(ref s, out SeenResource n)) remembered++;
        Assert.True(remembered > 0, "precondition: the seat remembers gone nodes");
        return sim;
    }

    /// <summary>
    /// BUG-1852 (red on the base: the same Gathers every think, the loaded workers never judged because of BUG-0574's full-load
    /// exemption): each worker's Gather to a gone node is refused at most once for that node, every remembered node ends marked
    /// dropped, and once all are marked no Gather is sent.
    /// </summary>
    [Fact]
    public void ALoadedIdleWorker_AGatherToAGoneNode_IsRefusedAtMostOnceForThatNode()
    {
        Simulation sim = LoadedIdleWorkersScene(out int remembered);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        List<Command> log = AiScenes.Log(sim, 0);
        int think = ai.ThinkTicks;
        for (int t = 0; t < 40 * think; t++) sim.Tick();
        int early = log.Count(c => c.Kind == CommandKind.Gather);
        for (int t = 0; t < 40 * think; t++) sim.Tick();
        var gathers = log.Where(c => c.Kind == CommandKind.Gather).ToList();
        var perTarget = gathers.GroupBy(c => (c.Unit, c.Position)).Select(g => g.Count()).DefaultIfEmpty(0).Max();
        int marked = 0;
        foreach (int m in ai.Board.NodeDropped) if (m != 0) marked++;
        _out.WriteLine($"remembered gone {remembered}; Gathers after 40 thinks {early}, after 80 {gathers.Count}; most to one node by one worker {perTarget}; marked {marked}");
        Assert.True(gathers.Count > 0, "the scene sent no Gather: it does not test the rule");
        Assert.True(perTarget <= 1, $"a worker's Gather to one gone node was sent {perTarget} times");
        Assert.Equal(early, gathers.Count); // all marked within 40 thinks: none after
    }

    /// <summary>BUG-1852's twin: the scene twice, hash-equal every think, the same marks.</summary>
    [Fact]
    public void ALoadedIdleWorker_RunTwice_SameHashesAndMarks()
    {
        Simulation a = LoadedIdleWorkersScene(out _), b = LoadedIdleWorkersScene(out _);
        int think = a.AiOf(0)!.ThinkTicks;
        for (int t = 0; t < 40 * think; t++)
        {
            a.Tick();
            b.Tick();
            if (t % think == 0) Assert.Equal(a.StateHash(), b.StateHash());
        }
        Assert.Equal(a.AiOf(0)!.Board.NodeDropped, b.AiOf(0)!.Board.NodeDropped);
    }

    // ---------------------------------------------------------------- Q7: the kept rally point and the hall point

    /// <summary>
    /// <see cref="RallyKeepTests"/>' fall-back scene (the kept point (28, 64) m in force, no point of the line acceptable), then
    /// player 0's Keep is freed between thinks, so its hall point moves to its next building (the Billet).
    /// </summary>
    private static (Simulation Sim, Vector2 HallBefore) HallMovedScene()
    {
        (Simulation sim, _) = RallyKeepTests.ToTheFallBack();
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        Assert.True(ai.Board.HasRallyKept, "the scene: a kept point in force");
        Vector2 before = ai.HallPoint;
        // Two more Billets over cells 21-24 x 31-32, past the passage on the new hall's line, so no point of that line is
        // acceptable (back toward the hall: the passage; out past the nominal point: the Billets) and the fall-back decides.
        int billet = w.Data.FindBuilding("malazan_billet");
        Assert.True(w.Buildings.Spawn(0, billet, 31 * AiScenes.FlatSize + 21, out _));
        Assert.True(w.Buildings.Spawn(0, billet, 31 * AiScenes.FlatSize + 23, out _));
        int keep = w.Data.FindBuilding("malazan_garrison_keep");
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 0 && w.Buildings.TypeId[k] == keep) Assert.True(w.Buildings.Free(w.Buildings.HandleOf(k)));
        return (sim, before);
    }

    /// <summary>
    /// Q7 (docs/01 2026-11-27 first row (3); red on the base: the kept point outlived the move): the hall point changes and the
    /// kept rally point is dropped on the next think; a think with the hall point unchanged keeps it (the M6-S14 rows,
    /// <see cref="RallyKeepTests"/>).
    /// </summary>
    [Fact]
    public void TheHallPointChanges_TheKeptRallyPoint_IsDroppedOnTheNextThink()
    {
        (Simulation sim, Vector2 before) = HallMovedScene();
        AiPlayer ai = sim.AiOf(0)!;
        Assert.True(ai.Board.HasRallyKept, "kept until the next think");
        RallyKeepTests.NextThink(sim);
        _out.WriteLine($"hall {before} -> {ai.HallPoint} (has hall {ai.HasHall}); kept {ai.Board.HasRallyKept} {ai.Board.RallyKept}; rally {ai.Board.Rally}");
        Assert.NotEqual(before, ai.HallPoint);
        Assert.True(!ai.Board.HasRallyKept || ai.Board.RallyKept != new Vector2(28f, 64f), "the old hall's kept point survived the move");
        Assert.NotEqual(new Vector2(28f, 64f), ai.Board.Rally); // the base fell back to it (inside the new hall's own Billet)
        // From here the point is kept again only as the M6-S14 rule keeps it, on the new hall's line.
        Vector2 hall = ai.HallPoint;
        RallyKeepTests.NextThink(sim);
        Assert.Equal(hall, ai.HallPoint);
    }

    /// <summary>Q7's twin and save: the scene twice is hash-equal for 1,000 ticks; a save taken between the Keep's fall and the next think loads equal and follows the run.</summary>
    [Fact]
    public void TheHallPointChanges_Twins_AndASaveBeforeTheThinkFollowsTheRun()
    {
        (Simulation a, _) = HallMovedScene();
        (Simulation b, _) = HallMovedScene();
        byte[] bytes = Save.SaveTestRun.Save(a);
        Simulation load = Save.SaveTestRun.Load(RallyKeepTests.s_quiet.Value, bytes);
        Save.SaveTestRun.AssertSameState(a, load, "before the think");
        for (int t = 0; t < 1000; t++)
        {
            a.Tick();
            b.Tick();
            load.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"twins diverged at tick {a.TickNumber}");
            Assert.True(a.StateHash() == load.StateHash(), $"the load diverged at tick {a.TickNumber}");
        }
        Assert.Equal(a.AiOf(0)!.Board.HasRallyKept, load.AiOf(0)!.Board.HasRallyKept);
    }

    /// <summary>BUG-1857's twin: the room-for-two scene twice, hash-equal every think for 60 thinks.</summary>
    [Fact]
    public void Floor_RoomForTwo_RunTwice_SameHashes()
    {
        Simulation a = FloorRoomScene(2, out _), b = FloorRoomScene(2, out _);
        int think = a.AiOf(0)!.ThinkTicks;
        for (int t = 0; t < 60 * think; t++)
        {
            a.Tick();
            b.Tick();
            if (t % think == 0) Assert.Equal(a.StateHash(), b.StateHash());
        }
    }
}
