using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-SH2 fix round (BUG-1151, met again as BUG-1537's Pale Hills seed 2 row): the economy's rebalance moves a gatherer at
/// work to the other kind's nearest known node. When that node is a remembered mine that is gone, the Gather is dropped at
/// apply and the worker goes on gathering its old kind, so it is never Idle and BUG-0570's mark (judged on idle workers) never
/// came: the next think sent another gatherer to the same mine, every think (Pale Hills seed 2, player 1, ticks 26,301 to
/// 26,481: nine thinks in a row). A Gather that applies sets the worker's cargo kind at once, so a worker at work that still
/// carries the other kind had its Gather dropped: the node is marked once, as an idle worker's would be.
/// </summary>
public class RebalanceDroppedGatherTests
{
    private const byte Wood = 2; // EconomyManager's gather kinds on Blackboard.GatherKind

    private readonly ITestOutputHelper _out;

    public RebalanceDroppedGatherTests(ITestOutputHelper output) => _out = output;

    /// <summary>The quiet order (no build step for an hour) and every node in gather reach, so the economy alone sends.</summary>
    private static readonly Lazy<GameData> s_quiet = new(() => AiScenes.DataWith(dir =>
    {
        dir.EditJson("factions/malazan/ai.json", root =>
            AiScenes.Opening(root)["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1)));
        dir.EditJson("common/rules.json", r => r["ai"]!["gatherReach"] = 512);
    }));

    /// <summary>
    /// Every worker on wood, none idle, and every gold mine gone (the near ones in sight, so forgotten; the rest remembered out
    /// of sight): the rebalance wants gold and finds only gone mines. Each is tried at most once: the Gathers dropped at apply
    /// never go to one mine twice (without the fix one mine was tried 5 times in 40 thinks, and the loop goes on while the
    /// workers are never idle).
    /// </summary>
    [Fact]
    public void ARebalanceGather_ToAGoneMine_IsTriedOnce_NotEveryThink()
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human);
        Simulation sim = AiScenes.Flat(seats, s_quiet.Value, workers: 8);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        sim.Tick(); // the first think
        int tree = w.Data.FindResource("tree"), mine = w.Data.FindResource("gold_mine");
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.Owner[i] != 0 || !EconomySystem.IsWorker(w, i)) continue;
            int best = -1;
            float bestD = float.MaxValue;
            for (int n = 0; n < w.Resources.Capacity; n++)
            {
                if (!w.Resources.Alive[n] || w.Resources.TypeId[n] != tree || !EconomySystem.IsExposed(w, n)) continue;
                float d = System.Numerics.Vector2.DistanceSquared(u.Position[i], w.NavGrid.CellCenter(w.Resources.Cell[n] % w.NavGrid.Width, w.Resources.Cell[n] / w.NavGrid.Width));
                if (d < bestD) (best, bestD) = (n, d);
            }
            EconomySystem.StartGather(w, i, best);
            ai.Board.GatherKind[i] = Wood;
            ai.Board.GatherNode[i] = default;
            ai.Board.UnitGeneration[i] = u.Generation[i];
            ai.Board.UnitOrderedUntil[i] = -1;
        }
        int gone = 0;
        for (int n = 0; n < w.Resources.Capacity; n++)
        {
            if (!w.Resources.Alive[n] || w.Resources.TypeId[n] != mine) continue;
            w.Resources.Take(w.Resources.HandleOf(n), int.MaxValue);
            gone++;
        }
        // The first think's Gathers (sent before the scene was set) apply at tick 1, to the near mines just taken: not counted.
        var dropped = new List<System.Numerics.Vector2>();
        sim.RefusedForTests = (c, why, _) =>
        {
            if (c.Player != 0 || c.Kind != CommandKind.Gather || why != CommandRefusal.NoResourceNode || c.Tick <= 1) return;
            dropped.Add(c.Position);
            _out.WriteLine($"tick {c.Tick}: a Gather of unit {c.Unit.Index} to {c.Position} dropped ({u.State[c.Unit.Index]})");
        };
        List<Command> log = AiScenes.Log(sim, 0);
        int think = ai.ThinkTicks;
        for (int t = 0; t < 40 * think; t++) sim.Tick();
        int idleAtEnd = 0;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == 0 && EconomySystem.IsWorker(w, i) && u.State[i] == UnitState.Idle) idleAtEnd++;
        _out.WriteLine($"gold mines gone {gone}; Gathers sent {log.Count(c => c.Kind == CommandKind.Gather)}, dropped at apply {dropped.Count}; idle at the end {idleAtEnd}; marks {ai.Board.NodeMarks}");
        Assert.True(dropped.Count >= 1, "precondition: the rebalance tried a gone mine");
        // Without the fix: one mine (28, 50) dropped 5 times in 40 thinks (ticks 41 to 401); with it, each gone mine once at most.
        Assert.True(dropped.Distinct().Count() == dropped.Count, $"a gone mine tried again: {string.Join(", ", dropped)}");
        Assert.True(dropped.Count <= gone, $"{dropped.Count} Gathers dropped at gone mines (only {gone} gone mines)");
        Assert.True(w.GatheredWood[0] > 0, "the workers kept gathering wood");
    }
}
