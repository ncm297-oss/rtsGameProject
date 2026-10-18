using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-H1 (BUG-0594 items 1-2): the economy's rebalance moves the gatherer carrying the least (a Gather of the other kind
/// drops a load), and its idle pass never orders the scout on its sweep.
/// </summary>
public class EconomyRebalanceTests
{
    private const byte Gold = 1; // EconomyManager's gather kinds on Blackboard.GatherKind

    private static readonly Lazy<GameData> s_quietOrder = new(() => AiScenes.DataWithAi("malazan", root =>
        AiScenes.Opening(root)["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1))));

    /// <summary>
    /// Every worker remembered on gold (the split off <c>goldShare</c>), wood known, none idle: the one rebalance Gather of
    /// the think goes to the gathering worker with the smallest load, not the first in slot order (which carries a load).
    /// </summary>
    [Fact]
    public void TheRebalance_MovesTheGathererCarryingTheLeast()
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human);
        Simulation sim = AiScenes.Flat(seats, s_quietOrder.Value, workers: 8);
        AiPlayer ai = sim.AiOf(0)!;
        UnitStore u = sim.World.Units;
        List<Command> rec = AiScenes.Log(sim, 0);
        int think = ai.ThinkTicks;
        for (int tries = 0; tries < 200; tries++)
        {
            // Run to the tick before a think, then set the scene up for it.
            do sim.Tick(); while (sim.TickNumber % think != 0 || sim.TickNumber < 600);
            var gathering = new List<int>();
            bool idle = false;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i] || u.Owner[i] != 0 || sim.World.Data.Units[u.TypeId[i]].Slot != UnitSlot.Worker) continue;
                if (u.State[i] == UnitState.Gathering) gathering.Add(i);
                idle |= u.State[i] == UnitState.Idle;
                ai.Board.GatherKind[i] = Gold;
            }
            if (idle || gathering.Count < 2 || ai.Board.UnitOrderedUntil.Any(t => t >= sim.TickNumber)) continue;
            int first = gathering[0], last = gathering[^1];
            foreach (int i in gathering) (u.Cargo[i], u.CargoKind[i]) = (i == last ? 0 : 3, ResourceKind.Gold);
            int before = rec.Count;
            sim.Tick();
            Command moved = Assert.Single(rec.Skip(before), c => c.Kind == CommandKind.Gather);
            Assert.True(moved.Unit.Index == last, $"rebalanced slot {moved.Unit.Index} (first {first}, empty-handed {last})");
            return;
        }
        Assert.Fail("no think found with every worker gathering");
    }

    /// <summary>
    /// The idle pass skips the scout on its sweep: an Idle scout with no AI command pending (a state the scout manager, which
    /// runs first in a think, normally leaves with a Move pending) gets no Gather from the economy.
    /// </summary>
    [Fact]
    public void TheIdlePass_NeverOrdersTheScout()
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human);
        Simulation sim = AiScenes.Flat(seats, s_quietOrder.Value);
        AiPlayer ai = sim.AiOf(0)!;
        UnitStore u = sim.World.Units;
        List<Command> rec = AiScenes.Log(sim, 0);
        while (ai.Board.ScoutPhase != 1 && sim.TickNumber < 2000) sim.Tick();
        Assert.Equal(1, ai.Board.ScoutPhase);
        int scout = ai.Board.ScoutSlot;
        while (u.State[scout] != UnitState.Moving) sim.Tick();
        // Another worker idle too, so the idle pass runs (with the scout the only idle one the pass is skipped whole).
        int other = Enumerable.Range(0, u.Capacity).First(i => i != scout && u.Alive[i] && u.Owner[i] == 0
            && sim.World.Data.Units[u.TypeId[i]].Slot == UnitSlot.Worker);
        foreach (int i in new[] { scout, other })
        {
            u.State[i] = UnitState.Idle;
            u.GoalCell[i] = -1;
            u.GatherNode[i] = default;
            ai.Board.UnitOrderedUntil[i] = -1;
        }
        int before = rec.Count;
        EconomyManager.Run(ai);
        Assert.Contains(rec.Skip(before), c => c.Unit.Index == other && c.Kind == CommandKind.Gather);
        Assert.DoesNotContain(rec.Skip(before), c => c.Unit.Index == scout);
    }
}
