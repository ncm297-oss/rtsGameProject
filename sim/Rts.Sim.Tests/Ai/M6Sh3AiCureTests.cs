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
