using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA, M6-SH3 (session 2026-11-27-0817, tier full, QA focus 5): the three AI cures beyond the developer's rows. BUG-1857 with a
/// house finishing while worker items wait; BUG-1852 with the node dying while the worker walks to it, twice in a row, and with
/// two workers sent to one node; Q7 with the hall razed and rebuilt on the same cells (the kept rally point always belongs to
/// the hall point in force).
/// </summary>
public class M6Sh3AiQaTests
{
    private readonly ITestOutputHelper _out;

    public M6Sh3AiQaTests(ITestOutputHelper output) => _out = output;

    // ---------------------------------------------------------------- BUG-1857 with a house mid-queue

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
    /// A Normal Malazan AI cut to one Laborer with room for exactly one more (1,000 gold, no wood, so it builds no house itself);
    /// the floor wants three more. After its first worker Train a finished Billet is placed (the cap moves while a worker item may
    /// still be queued). Every tick: no paid worker waits at the head of a queue without room. At the end live + queued workers
    /// equal the floor and never exceeded it.
    /// </summary>
    [Fact]
    public void Floor_AHouseFinishesMidQueue_NoPaidWorkerWaits_AndTheFloorIsMetNotExceeded()
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
        int lab = w.Data.Units[worker].ItemHalfPop, hv = w.Data.Units[heavy].ItemHalfPop;
        for (int k = 0; k < 300 && w.HalfPop[0] + hv + lab <= w.HalfPopCap[0]; k++)
            CombatScenes.Place(sim, 0, heavy, w.NavGrid.CellCenter(56 + k % 6, 2 + k / 6));
        Assert.InRange(w.HalfPopCap[0] - w.HalfPop[0], lab, hv + lab - 1);
        BuildMaps.SetTotals(sim, 0, 1000, 0);
        AiPlayer ai = sim.AiOf(0)!;
        List<Command> log = AiScenes.Log(sim, 0);
        int from = sim.TickNumber, waiting = 0, most = 0, houseAt = -1, capBefore = w.HalfPopCap[0];
        int billet = w.Data.FindBuilding("malazan_billet");
        while (sim.TickNumber < from + 80 * ai.ThinkTicks)
        {
            sim.Tick();
            if (houseAt < 0 && log.Any(c => c.Tick > from && c.Kind == CommandKind.Train && c.TypeId == worker))
            {
                Assert.True(w.Buildings.Spawn(0, billet, 40 * AiScenes.FlatSize + 4, out _));
                houseAt = sim.TickNumber;
            }
            int live = Workers(w, queued: false), queued = Workers(w, queued: true);
            most = Math.Max(most, live + queued);
            BuildingStore b = w.Buildings;
            for (int k = 0; k < b.Capacity; k++)
                if (b.Alive[k] && b.Owner[k] == 0 && b.QueueCount[k] > 0 && !b.QueueIsTechAt(k, 0) && b.QueueTypeAt(k, 0) == worker
                    && b.Progress[k] == 0 && w.HalfPop[0] + lab > w.HalfPopCap[0]) waiting++;
        }
        int trains = log.Count(c => c.Tick > from && c.Kind == CommandKind.Train && c.TypeId == worker);
        _out.WriteLine($"house placed at {houseAt} (cap {capBefore} -> {w.HalfPopCap[0]}); worker Trains {trains}; most live + queued {most}; end live {Workers(w, false)} queued {Workers(w, true)}; waiting ticks {waiting}");
        Assert.True(houseAt > 0, "the floor never trained");
        Assert.Equal(0, waiting);
        Assert.True(most <= ai.Rules.MinWorkers, $"live + queued workers reached {most} over the floor {ai.Rules.MinWorkers}");
        Assert.Equal(ai.Rules.MinWorkers, Workers(w, false) + Workers(w, true));
    }

    private static int Workers(World w, bool queued)
    {
        int n = 0;
        if (!queued)
        {
            for (int i = 0; i < w.Units.Capacity; i++)
                if (w.Units.Alive[i] && w.Units.Owner[i] == 0 && w.Data.Units[w.Units.TypeId[i]].IsWorker) n++;
            return n;
        }
        for (int k = 0; k < w.Buildings.Capacity; k++)
        {
            if (!w.Buildings.Alive[k] || w.Buildings.Owner[k] != 0) continue;
            for (int q = 0; q < w.Buildings.QueueCount[k]; q++)
                if (!w.Buildings.QueueIsTechAt(k, q) && w.Data.Units[w.Buildings.QueueTypeAt(k, q)].IsWorker) n++;
        }
        return n;
    }

    // ---------------------------------------------------------------- BUG-1852: the node dies while the worker walks

    private static readonly Lazy<GameData> s_quietAnyReach = new(() => AiScenes.DataWith(dir =>
    {
        dir.EditJson("factions/malazan/ai.json", root =>
            AiScenes.SetBuildOrder(AiScenes.Opening(root), AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1))));
        dir.EditJson("common/rules.json", r => r["ai"]!["gatherReach"] = 512);
    }));

    /// <summary>
    /// A Hard Malazan AI on the flat map with an expansion mine; its own mines and every tree are taken out before the first think,
    /// so its five idle workers are sent to the far mines (the middle one and the human seat's two; several workers to one node).
    /// Each node a Gather names is emptied 10 ticks after the Gather applies, while the workers walk; up to three nodes die so
    /// (twice in a row and more for a worker). For 120 thinks: no worker is sent to one node handle twice, and no Gather is sent
    /// to a node that is already dead when the think sends it and that the seat has marked.
    /// </summary>
    [Fact]
    public void TheNodeDiesWhileTheWorkerWalks_TwiceInARow_TwoWorkersOnOneNode_NoGatherIsResent()
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Human);
        Simulation sim = AiScenes.Flat(seats, s_quietAnyReach.Value, expansionMine: true);
        World w = sim.World;
        ResourceStore r = w.Resources;
        (int hx, int hy) = AiScenes.HallAt(0);
        for (int k = 0; k < r.Capacity; k++)
        {
            if (!r.Alive[k]) continue;
            int cell = r.Cell[k], x = cell % AiScenes.FlatSize, y = cell / AiScenes.FlatSize;
            bool own = Math.Abs(x - hx) < 12 && Math.Abs(y - hy) < 12;
            if (own || w.Data.Resources[r.TypeId[k]].Resource == ResourceKind.Wood) r.Take(new EntityHandle(k, r.Generation[k]), int.MaxValue);
        }
        AiPlayer ai = sim.AiOf(0)!;
        List<Command> log = AiScenes.Log(sim, 0);
        var sends = new Dictionary<(int Unit, int Node, int Gen), int>();
        var kill = new List<(int At, EntityHandle Node)>();
        var killed = new List<EntityHandle>();
        int seen = 0, toDead = 0;
        for (int t = 0; t < 120 * ai.ThinkTicks; t++)
        {
            sim.Tick();
            for (; seen < log.Count; seen++)
            {
                Command c = log[seen];
                if (c.Kind != CommandKind.Gather) continue;
                EntityHandle node = NodeAt(w, c.Position);
                if (node.Generation == 0)
                {
                    toDead++; // sent to a node that is gone (the think's memory): must be judged once, then never again
                    continue;
                }
                var key = (c.Unit.Index, node.Index, node.Generation);
                sends[key] = sends.TryGetValue(key, out int n) ? n + 1 : 1;
                if (killed.Count + kill.Count < 3 && !killed.Contains(node) && !kill.Exists(x => x.Node == node)) kill.Add((c.Tick + 10, node));
            }
            for (int k = kill.Count - 1; k >= 0; k--)
            {
                if (sim.TickNumber < kill[k].At) continue;
                if (r.IsAlive(kill[k].Node)) r.Take(kill[k].Node, int.MaxValue);
                killed.Add(kill[k].Node);
                kill.RemoveAt(k);
            }
        }
        int marked = 0;
        foreach (EntityHandle h in killed) if (ai.Board.NodeDropped[h.Index] == h.Generation) marked++;
        var gathers = log.Where(c => c.Kind == CommandKind.Gather).ToList();
        var perUnitTarget = gathers.GroupBy(c => (c.Unit.Index, c.Position)).Select(g => g.Count()).DefaultIfEmpty(0).Max();
        _out.WriteLine($"Gathers {gathers.Count} ({string.Join("; ", gathers.Select(c => $"t{c.Tick} u{c.Unit.Index} {c.Position}"))}); killed {killed.Count}, marked {marked}; sends to an already gone node {toDead}; most to one (unit, point) {perUnitTarget}");
        Assert.True(killed.Count >= 2, "the scene: fewer than two nodes died under walking workers");
        Assert.True(sends.Values.DefaultIfEmpty(0).Max() <= 1, "a worker was sent to the same live node twice");
        Assert.True(perUnitTarget <= 2, $"a worker was sent to one point {perUnitTarget} times (live once, gone once at most)");
    }

    /// <summary>The live resource whose footprint holds <paramref name="p"/>; default for none.</summary>
    private static EntityHandle NodeAt(World w, Vector2 p)
    {
        ResourceStore r = w.Resources;
        int n = w.NavGrid.Width;
        int cx = (int)(p.X / Map.MapConstants.CellSize), cy = (int)(p.Y / Map.MapConstants.CellSize);
        for (int k = 0; k < r.Capacity; k++)
        {
            if (!r.Alive[k]) continue;
            ResourceDef d = w.Data.Resources[r.TypeId[k]];
            int x = r.Cell[k] % n, y = r.Cell[k] / n;
            if (cx >= x && cx < x + d.FootprintWidth && cy >= y && cy < y + d.FootprintHeight) return new EntityHandle(k, r.Generation[k]);
        }
        return default;
    }

    // ---------------------------------------------------------------- Q7: the hall razed and rebuilt on the same cells

    /// <summary>
    /// <see cref="RallyKeepTests"/>' fall-back scene (a kept point in force); the Keep is razed, three thinks pass, a new Keep is
    /// spawned on the same anchor, eight thinks pass. After every think: whenever a kept point is in force, it was kept at a think
    /// whose hall point is the one in force now (never a point laid on another hall's line); the hall point ends where it began.
    /// </summary>
    [Fact]
    public void TheHallRazedAndRebuiltOnTheSameCells_TheKeptPoint_AlwaysBelongsToTheHallInForce()
    {
        (Simulation sim, _) = RallyKeepTests.ToTheFallBack();
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        Assert.True(ai.Board.HasRallyKept);
        Vector2 start = ai.HallPoint, keptFor = ai.HallPoint, lastKept = ai.Board.RallyKept;
        bool lastHas = true;
        int keep = w.Data.FindBuilding("malazan_garrison_keep"), anchor = -1;
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 0 && w.Buildings.TypeId[k] == keep)
            {
                anchor = w.Buildings.Cell[k];
                Assert.True(w.Buildings.Free(w.Buildings.HandleOf(k)));
            }
        Assert.True(anchor >= 0);
        var trace = new List<string>();
        void Think(int n)
        {
            for (int k = 0; k < n; k++)
            {
                RallyKeepTests.NextThink(sim);
                bool has = ai.Board.HasRallyKept;
                if (has && (!lastHas || ai.Board.RallyKept != lastKept)) keptFor = ai.HallPoint; // kept (again) at this think
                trace.Add($"t{sim.TickNumber}: hall {ai.HallPoint} kept {(has ? ai.Board.RallyKept.ToString() : "-")} rally {ai.Board.Rally}");
                Assert.True(!has || keptFor == ai.HallPoint, $"tick {sim.TickNumber}: the kept point {ai.Board.RallyKept} was kept for the hall point {keptFor}, the hall point is {ai.HallPoint}");
                (lastHas, lastKept) = (has, ai.Board.RallyKept);
            }
        }
        Think(3);
        Assert.NotEqual(start, ai.HallPoint);
        Assert.True(w.Buildings.Spawn(0, keep, anchor, out _));
        Think(8);
        _out.WriteLine(string.Join("\n", trace));
        Assert.Equal(start, ai.HallPoint);
    }
}
