using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA, M6-SH2 fix round 1 re-check (BUG-1151's fix, <c>EconomyManager.NoteDroppedRebalance</c>): the mirror case (every
/// worker on gold, every tree gone: the rebalance's wood Gathers to remembered gone trees), no false marks when the
/// rebalance's Gather applies (live mines), and a save / load taken mid-run follows the run hash for hash with the AI's
/// marks equal. The developer's row (<see cref="RebalanceDroppedGatherTests"/>) covers gold.
/// </summary>
[Collection(SerialCollection.Name)]
public class RebalanceDroppedGatherQaTests(ITestOutputHelper output)
{
    private const byte Gold = 1, Wood = 2; // EconomyManager's gather kinds on Blackboard.GatherKind

    private static readonly Lazy<GameData> s_quiet = new(() => AiScenes.DataWith(dir =>
    {
        dir.EditJson("factions/malazan/ai.json", root =>
            AiScenes.Opening(root)["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1)));
        dir.EditJson("common/rules.json", r => r["ai"]!["gatherReach"] = 512);
    }));

    /// <summary>The flat scene, the AI's first think done, every own worker set to gather the nearest exposed node of <paramref name="kind"/>.</summary>
    private static (Simulation Sim, AiPlayer Ai) Scene(ResourceKind kind)
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human);
        Simulation sim = AiScenes.Flat(seats, s_quiet.Value, workers: 8);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        sim.Tick();
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.Owner[i] != 0 || !EconomySystem.IsWorker(w, i)) continue;
            int best = -1;
            float bestD = float.MaxValue;
            for (int n = 0; n < w.Resources.Capacity; n++)
            {
                if (!w.Resources.Alive[n] || w.Data.Resources[w.Resources.TypeId[n]].Resource != kind || !EconomySystem.IsExposed(w, n)) continue;
                float d = System.Numerics.Vector2.DistanceSquared(u.Position[i], w.NavGrid.CellCenter(w.Resources.Cell[n] % w.NavGrid.Width, w.Resources.Cell[n] / w.NavGrid.Width));
                if (d < bestD) (best, bestD) = (n, d);
            }
            EconomySystem.StartGather(w, i, best);
            ai.Board.GatherKind[i] = kind == ResourceKind.Gold ? Gold : Wood;
            ai.Board.GatherNode[i] = default;
            ai.Board.UnitGeneration[i] = u.Generation[i];
            ai.Board.UnitOrderedUntil[i] = -1;
        }
        return (sim, ai);
    }

    private static int TakeAll(World w, ResourceKind kind)
    {
        int gone = 0;
        for (int n = 0; n < w.Resources.Capacity; n++)
        {
            if (!w.Resources.Alive[n] || w.Data.Resources[w.Resources.TypeId[n]].Resource != kind) continue;
            w.Resources.Take(w.Resources.HandleOf(n), int.MaxValue);
            gone++;
        }
        return gone;
    }

    /// <summary>
    /// The mirror of the developer's row: every worker on gold, every tree taken. The rebalance wants wood and finds only
    /// remembered trees that are gone; no gone tree is tried twice, and the workers keep gathering gold.
    /// </summary>
    [Fact]
    public void ARebalanceGather_ToAGoneTree_IsTriedOnce()
    {
        (Simulation sim, AiPlayer ai) = Scene(ResourceKind.Gold);
        World w = sim.World;
        int gone = TakeAll(w, ResourceKind.Wood);
        var dropped = new List<System.Numerics.Vector2>();
        sim.RefusedForTests = (c, why, _) =>
        {
            if (c.Player == 0 && c.Kind == CommandKind.Gather && why == CommandRefusal.NoResourceNode && c.Tick > 1) dropped.Add(c.Position);
        };
        List<Command> log = AiScenes.Log(sim, 0);
        for (int t = 0; t < 60 * ai.ThinkTicks; t++) sim.Tick();
        output.WriteLine($"trees gone {gone}; Gathers sent {log.Count(c => c.Kind == CommandKind.Gather)}, dropped {dropped.Count} ({dropped.Distinct().Count()} distinct); marks {ai.Board.NodeMarks}; gold gathered {w.GatheredGold[0]}");
        Assert.True(dropped.Count >= 1, "precondition: the rebalance tried a gone tree");
        Assert.True(dropped.Distinct().Count() == dropped.Count, $"a gone tree tried again: {string.Join(", ", dropped)}");
        Assert.True(w.GatheredGold[0] > 0, "the workers kept gathering gold");
    }

    /// <summary>
    /// No false marks: every worker on wood and every mine live. The rebalance's gold Gathers apply (the worker's cargo kind
    /// turns to gold), so nothing is marked, no Gather is refused, and gold comes in.
    /// </summary>
    [Fact]
    public void ARebalanceGather_ThatApplies_MarksNothing()
    {
        (Simulation sim, AiPlayer ai) = Scene(ResourceKind.Wood);
        World w = sim.World;
        int refused = 0;
        sim.RefusedForTests = (c, why, _) => { if (c.Player == 0 && c.Kind == CommandKind.Gather && c.Tick > 1) refused++; };
        List<Command> log = AiScenes.Log(sim, 0);
        for (int t = 0; t < 60 * ai.ThinkTicks; t++) sim.Tick();
        int onGold = 0;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 0 && EconomySystem.IsWorker(w, i) && w.Units.CargoKind[i] == ResourceKind.Gold) onGold++;
        output.WriteLine($"Gathers sent {log.Count(c => c.Kind == CommandKind.Gather)}, refused {refused}; marks {ai.Board.NodeMarks}; on gold {onGold}; gold gathered {w.GatheredGold[0]}");
        Assert.True(log.Count(c => c.Kind == CommandKind.Gather) >= 1, "precondition: the rebalance moved a gatherer");
        Assert.Equal(0, refused);
        Assert.Equal(0, ai.Board.NodeMarks);
        Assert.True(onGold >= 1 && w.GatheredGold[0] > 0, "the rebalance's gatherer went to gold");
    }

    /// <summary>
    /// The developer's gone-mine scene, saved and loaded every 3 thinks while the marks are being made: each load follows the
    /// run hash for hash for 2 thinks, with the AI's marks (count and per node) equal at the end.
    /// </summary>
    [Fact]
    public void TheGoneMineScene_SavedMidRun_FollowsTheRun_MarksEqual()
    {
        (Simulation sim, AiPlayer ai) = Scene(ResourceKind.Wood);
        TakeAll(sim.World, ResourceKind.Gold);
        int think = ai.ThinkTicks, checkedLoads = 0;
        for (int round = 0; round < 12; round++)
        {
            for (int t = 0; t < 3 * think; t++) sim.Tick();
            Simulation twin = SaveTestRun.Load(s_quiet.Value, SaveTestRun.Save(sim));
            for (int t = 0; t < 2 * think; t++)
            {
                sim.Tick();
                twin.Tick();
                Assert.Equal(sim.StateHash(), twin.StateHash());
            }
            Blackboard a = ai.Board, b = twin.AiOf(0)!.Board;
            Assert.Equal(a.NodeMarks, b.NodeMarks);
            Assert.Equal(a.NodeDropped, b.NodeDropped);
            Assert.Equal(a.GatherKind, b.GatherKind);
            checkedLoads++;
        }
        output.WriteLine($"{checkedLoads} loads followed; marks at the end {ai.Board.NodeMarks}");
        Assert.True(ai.Board.NodeMarks >= 1, "precondition: a mark was made during the run");
    }
}
