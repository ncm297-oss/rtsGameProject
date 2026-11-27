using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-S12 (BUG-1531, docs/01 2026-11-16 item 3; docs/03 "Implementation (M6-S12)"): when the economy moves a gatherer from one
/// resource to the other (the rebalance: no worker idle, the split off <c>ai.goldShare</c>), it picks the known live node of the
/// wanted kind nearest the worker's nearest finished drop-off, not nearest the worker. The flat AI scene with one Laborer,
/// gathering wood at a far tree (cells 40, 52) with a gold mine 8 m from it (cells 43-44 x 50-51), while the scene's two home
/// mines stand 14-15 m from the Keep: the rebalance (one worker, 0.6 of it wanted on gold) sends it to a home mine. Before
/// the rule it went to the far mine (the one nearest the worker), as Laborer 28 went to a mine 174 m from its hall
/// (ReachRuleQaTests (2, 80)).
/// </summary>
public class RebalanceFromDropOffTests(ITestOutputHelper output)
{
    private const byte Wood = 2; // EconomyManager's gather kinds on Blackboard.GatherKind

    /// <summary>The quiet order (no build step for an hour) and every node in gather reach, so the economy alone sends.</summary>
    private static readonly Lazy<GameData> s_quiet = new(() => AiScenes.DataWith(dir =>
    {
        dir.EditJson("factions/malazan/ai.json", root =>
            AiScenes.SetBuildOrder(AiScenes.Opening(root), AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1))));
        dir.EditJson("common/rules.json", r => r["ai"]!["gatherReach"] = 512);
    }));

    /// <summary>The far scene: the one Laborer at work at the far tree, the far mine beside it, both in its sight.</summary>
    private static (Simulation Sim, Vector2 FarMine, Vector2 Hall) Scene(bool atWork = true)
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human);
        Simulation sim = AiScenes.Flat(seats, s_quiet.Value, workers: 0);
        World w = sim.World;
        int size = AiScenes.FlatSize, mine = w.Data.FindResource("gold_mine"), tree = w.Data.FindResource("tree");
        Assert.True(w.Resources.Spawn(mine, 50 * size + 43, w.Data.Rules.StartMineGold, out _));
        Assert.True(w.Resources.Spawn(tree, 52 * size + 40, w.Data.Rules.TreeWood, out EntityHandle far));
        EntityHandle lab = CombatScenes.Place(sim, 0, w.Data.FindUnit("malazan_laborer"), w.NavGrid.CellCenter(41, 52));
        int i = lab.Index;
        if (atWork)
        {
            EconomySystem.StartGather(w, i, far.Index);
            Assert.Equal(UnitState.Gathering, w.Units.State[i]);
            AiPlayer ai = sim.AiOf(0)!;
            ai.Board.GatherKind[i] = Wood;
            ai.Board.GatherNode[i] = default;
            ai.Board.UnitGeneration[i] = lab.Generation;
            ai.Board.UnitOrderedUntil[i] = -1;
        }
        w.Fog.Update(); // the far mine in the worker's sight before the first think
        (int hx, int hy) = AiScenes.HallAt(0);
        return (sim, new Vector2(44f * 2f, 51f * 2f), w.NavGrid.CellCenter(hx, hy) + new Vector2(3f, 3f));
    }

    /// <summary>Criterion 4 (red on the base: the Gather went to the far mine, 8 m from the worker and 74 m from the Keep).</summary>
    [Fact]
    public void AFarWoodGatherer_MovedToGold_GoesToTheMineByItsDropOff()
    {
        (Simulation sim, Vector2 farMine, Vector2 hall) = Scene();
        List<Command> log = AiScenes.Log(sim, 0);
        AiPlayer ai = sim.AiOf(0)!;
        for (int t = 0; t < 3 * ai.ThinkTicks && !log.Exists(c => c.Kind == CommandKind.Gather); t++) sim.Tick();
        Command gather = log.Find(c => c.Kind == CommandKind.Gather);
        Assert.True(gather.Kind == CommandKind.Gather, "no rebalance Gather was sent");
        float toHall = Vector2.Distance(gather.Position, hall), toFar = Vector2.Distance(gather.Position, farMine);
        output.WriteLine($"tick {gather.Tick}: Gather to {gather.Position}: {toHall:F1} m from the Keep's centre {hall}, {toFar:F1} m from the far mine {farMine}");
        Assert.True(toHall < 20f, $"the Gather went {toHall:F1} m from the Keep (the far mine is {Vector2.Distance(farMine, hall):F1} m out)");
        Assert.Equal(ResourceKind.Gold, sim.World.Data.Resources[NodeAt(sim.World, gather.Position)].Resource);
    }

    /// <summary>
    /// The control (green on the base and after): an idle worker's pick is unchanged, the node nearest the worker. The same
    /// Laborer standing idle by the far tree is sent to the far mine (gold, the kind wanted first).
    /// </summary>
    [Fact]
    public void AnIdleWorker_IsStillSentToTheNodeNearestIt()
    {
        (Simulation sim, Vector2 farMine, Vector2 hall) = Scene(atWork: false);
        AiPlayer ai = sim.AiOf(0)!;
        List<Command> log = AiScenes.Log(sim, 0);
        for (int t = 0; t < 3 * ai.ThinkTicks && !log.Exists(c => c.Kind == CommandKind.Gather); t++) sim.Tick();
        Command gather = log.Find(c => c.Kind == CommandKind.Gather);
        Assert.True(gather.Kind == CommandKind.Gather, "no Gather was sent");
        output.WriteLine($"tick {gather.Tick}: Gather to {gather.Position} (far mine {farMine}, Keep {hall})");
        Assert.True(Vector2.Distance(gather.Position, farMine) < 3f, $"the idle worker was sent to {gather.Position}, not the far mine {farMine}");
    }

    /// <summary>The type of the live node whose footprint holds <paramref name="p"/> (the Gather's node centre).</summary>
    private static int NodeAt(World w, Vector2 p)
    {
        int x = (int)(p.X / 2f), y = (int)(p.Y / 2f);
        for (int n = 0; n < w.Resources.Capacity; n++)
        {
            if (!w.Resources.Alive[n]) continue;
            int c = w.Resources.Cell[n], cx = c % w.NavGrid.Width, cy = c / w.NavGrid.Width;
            ResourceDef d = w.Data.Resources[w.Resources.TypeId[n]];
            if (x >= cx && x < cx + d.FootprintWidth && y >= cy && y < cy + d.FootprintHeight) return w.Resources.TypeId[n];
        }
        throw new InvalidOperationException($"no node at {p}");
    }
}
