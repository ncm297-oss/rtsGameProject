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
/// M6-S14 item B (BUG-1660): the retrain is a floor, <c>ai.minWorkers</c> (shipped 4): a seat with fewer live + queued workers
/// trains one a think up to it; the M6-SH2 rows that mean "one, then none" run on a copy with the floor at 1, the old rule.
/// </summary>
[Collection(SerialCollection.Name)] // measures allocation (criterion 9)
public class RetrainTests
{
    private static int Laborer => TestSim.Data.FindUnit("malazan_laborer");

    private static int Keep => TestSim.Data.FindBuilding("malazan_garrison_keep");

    private static int Raider => TestSim.Data.FindUnit("whirlwind_raider");

    /// <summary>One Laborer at time 0 (the opening's only worker step, done at once) and nothing else; the shipped floor.</summary>
    private static readonly Lazy<GameData> s_oneWorker = new(() => OneWorkerData(floor: null));

    /// <summary>M6-S14: the same with <c>ai.minWorkers</c> 1, the M6-SH2 rule (a worker only once none is alive or queued).</summary>
    private static readonly Lazy<GameData> s_floorOne = new(() => OneWorkerData(floor: 1));

    /// <summary>The one-worker opening; <paramref name="floor"/> written over <c>ai.minWorkers</c> (rule 58: the shipped file carries it), or the shipped value.</summary>
    private static GameData OneWorkerData(int? floor) => AiScenes.DataWith(dir =>
    {
        dir.EditJson("factions/malazan/ai.json", root =>
            AiScenes.SetBuildOrder(AiScenes.Opening(root), AiScenes.BuildOrder(("time", 0, "train", "malazan_laborer", 1))));
        if (floor != null) dir.EditJson("common/rules.json", r => r["ai"]!["minWorkers"] = floor.Value);
    });

    /// <summary>Player 0 the Malazan AI (the test's opening), player 1 a human Whirlwind; combat on unless <paramref name="combat"/> says.</summary>
    private static Simulation Scene(GameData? data = null, bool combat = true)
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), data ?? s_oneWorker.Value, combat: combat);
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

    /// <summary>
    /// Criterion 1: the beaten seat has a Laborer in its Keep's queue within 200 ticks, while it defends against the raiders.
    /// M6-S14: on the copy with <c>ai.minWorkers</c> 1, so "one, not one a think" is the M6-SH2 rule's (the shipped floor
    /// trains one a think up to 4: <see cref="OneWorker_AKeepAndGold_TrainsUpToTheFloor_OneATrainAThink"/>).
    /// </summary>
    [Fact]
    public void NoWorker_AKeepAndGold_TrainsOne_WithinTwoHundredTicks_WhileRazed()
    {
        Simulation sim = Scene(s_floorOne.Value);
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
        Simulation sim = Scene(s_floorOne.Value);
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

    /// <summary>Frees every worker of player 0 but one (the lowest slot).</summary>
    private static void KeepOneWorker(Simulation sim)
    {
        UnitStore u = sim.World.Units;
        bool kept = false;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.Owner[i] != 0 || !sim.World.Data.Units[u.TypeId[i]].IsWorker) continue;
            if (!kept) kept = true;
            else u.Free(new EntityHandle(i, u.Generation[i]));
        }
        Assert.True(kept);
    }

    /// <summary>Player 0's live workers plus the workers in its queues.</summary>
    private static int LivePlusQueued(Simulation sim)
    {
        World w = sim.World;
        int n = 0;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 0 && w.Data.Units[w.Units.TypeId[i]].IsWorker) n++;
        for (int k = 0; k < w.Buildings.Capacity; k++)
        {
            if (!w.Buildings.Alive[k] || w.Buildings.Owner[k] != 0) continue;
            for (int q = 0; q < w.Buildings.QueueCount[k]; q++)
                if (!w.Buildings.QueueIsTechAt(k, q) && w.Data.Units[w.Buildings.QueueTypeAt(k, q)].IsWorker) n++;
        }
        return n;
    }

    /// <summary>Ticks to just after player 0's next think (its commands of that think are logged by then).</summary>
    private static void ToAfterAThink(Simulation sim)
    {
        int think = sim.AiOf(0)!.ThinkTicks;
        do sim.Tick(); while ((sim.TickNumber - 1) % think != 0);
    }

    /// <summary>
    /// M6-S14 criterion 3 (failed on the base: no Train in 40 thinks, the M6-SH2 rule waits for zero workers): a seat with a
    /// finished Keep, one worker (its opening's worker step long done) and the gold sends one Laborer Train a think until its
    /// live + queued workers reach <c>ai.minWorkers</c> (4), and none after; never two in a think. Combat off, no enemy near.
    /// </summary>
    [Fact]
    public void OneWorker_AKeepAndGold_TrainsUpToTheFloor_OneATrainAThink()
    {
        Simulation sim = Scene(combat: false);
        AiPlayer ai = sim.AiOf(0)!;
        Assert.Equal(4, ai.Rules.MinWorkers);
        List<Command> log = AiScenes.Log(sim, 0);
        GatherMaps.Run(sim, 2000);
        KeepOneWorker(sim);
        int from = sim.TickNumber;
        Assert.Equal(1, LivePlusQueued(sim));
        GatherMaps.Run(sim, 40 * ai.ThinkTicks);
        var trains = log.Where(c => c.Tick > from && c.Kind == CommandKind.Train && c.TypeId == Laborer).ToList();
        Assert.Equal(ai.Rules.MinWorkers - 1, trains.Count);
        Assert.Equal(trains.Count, trains.Select(c => c.Tick).Distinct().Count()); // one a think
        Assert.Equal(ai.Rules.MinWorkers, LivePlusQueued(sim));
    }

    /// <summary>M6-S14 criterion 3: with <c>ai.minWorkers</c> 1 (the M6-SH2 rule) the same seat with one worker trains none.</summary>
    [Fact]
    public void OneWorker_TheFloorAtOne_TrainsNone()
    {
        Simulation sim = Scene(s_floorOne.Value, combat: false);
        AiPlayer ai = sim.AiOf(0)!;
        Assert.Equal(1, ai.Rules.MinWorkers);
        List<Command> log = AiScenes.Log(sim, 0);
        GatherMaps.Run(sim, 2000);
        KeepOneWorker(sim);
        int from = sim.TickNumber;
        GatherMaps.Run(sim, 40 * ai.ThinkTicks);
        Assert.DoesNotContain(log, c => c.Tick > from && c.Kind == CommandKind.Train && c.TypeId == Laborer);
        Assert.Equal(1, LivePlusQueued(sim));
    }

    /// <summary>
    /// M6-S14 criterion 3: a seat under the floor sends no worker Train, and nothing throws, with no hall (the Keep freed:
    /// nothing else trains a Laborer), with no gold, and with no population room (soldiers fill the cap).
    /// </summary>
    [Theory]
    [InlineData("no hall")]
    [InlineData("no gold")]
    [InlineData("no room")]
    public void UnderTheFloor_NoHallNoGoldOrNoRoom_SendsNoTrain(string what)
    {
        Simulation sim = Scene(combat: false);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        List<Command> log = AiScenes.Log(sim, 0);
        GatherMaps.Run(sim, 2000);
        KeepOneWorker(sim); // first: freeing workers frees population
        switch (what)
        {
            case "no hall":
                Assert.True(w.Buildings.Free(Own(w, 0, Keep)));
                BuildMaps.SetTotals(sim, 0, 100, 0); // a Laborer's gold, not a Keep's: no hall goes up meanwhile
                break;
            case "no gold":
                BuildMaps.SetTotals(sim, 0, 0, 1000);
                break;
            default:
                int heavy = TestSim.Data.FindUnit("malazan_heavy_infantry");
                for (int k = 0; k < 200 && w.HalfPop[0] + w.Data.Units[Laborer].HalfPop <= w.HalfPopCap[0]; k++)
                    CombatScenes.Place(sim, 0, heavy, w.NavGrid.CellCenter(56 + k % 6, 2 + k / 6));
                Assert.True(w.HalfPop[0] + w.Data.Units[Laborer].HalfPop > w.HalfPopCap[0], "the scene: the population has room");
                BuildMaps.SetTotals(sim, 0, 1000, 0); // no wood: no Billet raises the cap meanwhile (the economy's house rule)
                break;
        }
        ToAfterAThink(sim);
        int from = sim.TickNumber;
        GatherMaps.Run(sim, 20 * ai.ThinkTicks);
        Assert.DoesNotContain(log, c => c.Tick >= from && c.Kind == CommandKind.Train && c.TypeId == Laborer);
        Assert.True(LivePlusQueued(sim) <= 1);
    }

    /// <summary>
    /// M6-S14 criterion 9: the floor's check allocates nothing, sending a Train (one worker left, under the floor) or not (at
    /// the floor).
    /// </summary>
    [Fact]
    public void TheFloor_AllocatesNothing()
    {
        Simulation sim = Scene(combat: false);
        AiPlayer ai = sim.AiOf(0)!;
        GatherMaps.Run(sim, 2000);
        KeepOneWorker(sim);
        while (sim.TickNumber % ai.ThinkTicks != 0) sim.Tick();
        AllocationProbe.AssertZero(() => GatherMaps.Run(sim, ai.ThinkTicks)); // the think that sends a floor Train
        GatherMaps.Run(sim, 10 * ai.ThinkTicks);
        Assert.Equal(ai.Rules.MinWorkers, LivePlusQueued(sim));
        AllocationProbe.AssertZero(() => BuildOrderExecutor.Retrain(ai)); // at the floor: nothing sent
    }
}
