using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-SH2 item A (BUG-1465, docs/02 "AI opponent"; docs/03 "Implementation (M6-SH2)"), the flat scene with combat on.
/// (a) A seat left with a finished worker-trainer, no worker, free population and the gold for one trains one, while enemy
/// soldiers raze its buildings and with its opening's worker step long done; with no gold nothing is sent. (b) A seat with a
/// worker, no Town Hall and the hall's cost in hand places a hall (M5-4d slice 2's rule, guarded here).
/// </summary>
[Collection(SerialCollection.Name)] // measures allocation (criterion 9)
public class RetrainTests
{
    private static int Laborer => TestSim.Data.FindUnit("malazan_laborer");

    private static int Keep => TestSim.Data.FindBuilding("malazan_garrison_keep");

    private static int Raider => TestSim.Data.FindUnit("whirlwind_raider");

    /// <summary>One Laborer at time 0 (the opening's only worker step, done at once) and nothing else.</summary>
    private static readonly Lazy<GameData> s_oneWorker = new(() => AiScenes.DataWithAi("malazan", root =>
        AiScenes.SetBuildOrder(AiScenes.Opening(root), AiScenes.BuildOrder(("time", 0, "train", "malazan_laborer", 1)))));

    /// <summary>Player 0 the Malazan AI (the test's opening), player 1 a human Whirlwind; combat on.</summary>
    private static Simulation Scene()
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), s_oneWorker.Value, combat: true);
        BuildMaps.Give(sim, 0, 1000, 1000);
        return sim;
    }

    private static EntityHandle Own(World w, int player, int type)
    {
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == player && w.Buildings.TypeId[k] == type) return w.Buildings.HandleOf(k);
        return default;
    }

    /// <summary>Runs until the opening's worker step is done and its Laborer is out, then frees every worker of player 0.</summary>
    private static void LoseEveryWorker(Simulation sim)
    {
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        EntityHandle keep = Own(w, 0, Keep);
        for (int t = 0; t < 4000 && !(ai.Board.StepDone[0] && w.Buildings.QueueCount[keep.Index] == 0); t++) sim.Tick();
        Assert.True(ai.Board.StepDone[0] && w.Buildings.QueueCount[keep.Index] == 0, "the worker step never finished");
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == 0 && w.Data.Units[u.TypeId[i]].IsWorker) u.Free(new EntityHandle(i, u.Generation[i]));
    }

    /// <summary>Three Whirlwind raiders beside player 0's Keep, attacking it.</summary>
    private static void Raze(Simulation sim)
    {
        World w = sim.World;
        EntityHandle keep = Own(w, 0, Keep);
        (int hx, int hy) = AiScenes.HallAt(0);
        for (int k = 0; k < 3; k++)
        {
            EntityHandle r = CombatScenes.Place(sim, 1, Raider, w.NavGrid.CellCenter(hx + 5, hy + k));
            sim.Enqueue(Command.Attack(1, r, keep, isBuilding: true));
        }
    }

    /// <summary>Criterion 1: the beaten seat has a Laborer in its Keep's queue within 200 ticks, while it defends against the raiders.</summary>
    [Fact]
    public void NoWorker_AKeepAndGold_TrainsOne_WithinTwoHundredTicks_WhileRazed()
    {
        Simulation sim = Scene();
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        List<Command> log = AiScenes.Log(sim, 0);
        LoseEveryWorker(sim);
        Raze(sim);
        int lost = sim.TickNumber;
        bool defended = false;
        EntityHandle keep = Own(w, 0, Keep);
        for (; sim.TickNumber < lost + 200 && w.Buildings.QueueCount[keep.Index] == 0; sim.Tick())
            defended |= ai.Board.Defending;
        Assert.True(w.Buildings.QueueCount[keep.Index] > 0, $"no Laborer in training by tick {sim.TickNumber} (lost at {lost})");
        Assert.Equal(Laborer, w.Buildings.QueueTypeAt(keep.Index, 0));
        Assert.Single(log, c => c.Tick > lost && c.Kind == CommandKind.Train && c.TypeId == Laborer);
        for (int t = 0; t < 4 * ai.ThinkTicks; t++) { sim.Tick(); defended |= ai.Board.Defending; }
        Assert.True(defended, "the seat never defended: the scene does not test the rule under a raid");
        Assert.Single(log, c => c.Tick > lost && c.Kind == CommandKind.Train && c.TypeId == Laborer); // one, not one a think
    }

    /// <summary>Criterion 9: the retrain check allocates nothing, sending (a worker trained) or not (one in the queue).</summary>
    [Fact]
    public void Retrain_AllocatesNothing()
    {
        Simulation sim = Scene();
        AiPlayer ai = sim.AiOf(0)!;
        LoseEveryWorker(sim);
        while (sim.TickNumber % ai.ThinkTicks != 0) sim.Tick();
        AllocationProbe.AssertZero(() => GatherMaps.Run(sim, ai.ThinkTicks)); // from the think that sends the Train
        Assert.True(sim.World.Buildings.QueueCount[Own(sim.World, 0, Keep).Index] > 0 || sim.PendingCommandCount > 0);
        AllocationProbe.AssertZero(() => BuildOrderExecutor.Retrain(ai)); // nothing sent: one is coming
    }

    /// <summary>Criterion 1: with no gold for a Laborer nothing is sent.</summary>
    [Fact]
    public void NoWorker_AKeep_NoGold_SendsNothing()
    {
        Simulation sim = Scene();
        List<Command> log = AiScenes.Log(sim, 0);
        LoseEveryWorker(sim);
        BuildMaps.SetTotals(sim, 0, 0, 1000);
        Raze(sim);
        int lost = sim.TickNumber;
        GatherMaps.Run(sim, 400);
        Assert.DoesNotContain(log, c => c.Tick > lost && c.Kind == CommandKind.Train);
    }

    /// <summary>
    /// Criterion 2 (the guard): a seat with a worker, no Town Hall standing or being built, and the hall's cost in hand places
    /// one within 600 ticks (M5-4d slice 2's hall-first rule; the scene passes on the base too: see the report).
    /// </summary>
    [Fact]
    public void AWorker_NoHall_TheCostInHand_PlacesAHallWithinSixHundredTicks()
    {
        Simulation sim = Scene();
        World w = sim.World;
        GatherMaps.Run(sim, 200);
        EntityHandle keep = Own(w, 0, Keep);
        Assert.True(w.Buildings.Free(keep));
        int razed = sim.TickNumber;
        for (; sim.TickNumber < razed + 600 && Own(w, 0, Keep).Generation == 0; sim.Tick()) { }
        Assert.True(Own(w, 0, Keep).Generation != 0, $"no hall placed by tick {sim.TickNumber} (razed at {razed})");
    }
}
