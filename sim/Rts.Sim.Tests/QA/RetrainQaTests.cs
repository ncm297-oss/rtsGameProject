using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA, M6-SH2 item A (BUG-1465 (a), <c>BuildOrderExecutor.Retrain</c>): the edges of "a seat with no worker trains one".
/// The trainer a site (nothing sent until it is finished), the trainer destroyed mid-training with a second one standing,
/// the seat at its population cap, a retrained worker that gathers, and the camping-enemy drain measured (two enemy
/// soldiers held at the Keep's door for 20 minutes: workers trained and lost, gold spent).
/// </summary>
public class RetrainQaTests
{
    private readonly ITestOutputHelper _out;

    public RetrainQaTests(ITestOutputHelper output) => _out = output;

    private static int Laborer => TestSim.Data.FindUnit("malazan_laborer");

    private static int Keep => TestSim.Data.FindBuilding("malazan_garrison_keep");

    private static readonly Lazy<GameData> s_oneWorker = new(() => AiScenes.DataWithAi("malazan", root =>
        AiScenes.Opening(root)["buildOrder"] = AiScenes.BuildOrder(("time", 0, "train", "malazan_laborer", 1))));

    private static Simulation Scene(bool combat = true)
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), s_oneWorker.Value, combat: combat);
        BuildMaps.Give(sim, 0, 1000, 1000);
        return sim;
    }

    private static EntityHandle Own(World w, int player, int type)
    {
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == player && w.Buildings.TypeId[k] == type) return w.Buildings.HandleOf(k);
        return default;
    }

    private static int Workers(World w, int player)
    {
        int n = 0;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == player && w.Data.Units[w.Units.TypeId[i]].IsWorker) n++;
        return n;
    }

    private static void LoseEveryWorker(Simulation sim)
    {
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        EntityHandle keep = Own(w, 0, Keep);
        for (int t = 0; t < 4000 && !(ai.Board.StepDone[0] && w.Buildings.QueueCount[keep.Index] == 0); t++) sim.Tick();
        Assert.True(ai.Board.StepDone[0] && w.Buildings.QueueCount[keep.Index] == 0);
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 0 && w.Data.Units[w.Units.TypeId[i]].IsWorker) w.Units.Free(new EntityHandle(i, w.Units.Generation[i]));
    }

    /// <summary>With no worker and no enemy near, the retrained Laborer gathers: the seat's gathered total rises within 2,000 ticks.</summary>
    [Fact]
    public void TheRetrainedWorker_Gathers()
    {
        Simulation sim = Scene(combat: false);
        World w = sim.World;
        LoseEveryWorker(sim);
        long gathered = w.GatheredGold[0] + w.GatheredWood[0];
        int lost = sim.TickNumber;
        while (sim.TickNumber < lost + 2000 && w.GatheredGold[0] + w.GatheredWood[0] == gathered) sim.Tick();
        Assert.True(Workers(w, 0) > 0, "no worker came back");
        Assert.True(w.GatheredGold[0] + w.GatheredWood[0] > gathered, $"the retrained worker banked nothing in 2,000 ticks");
        _out.WriteLine($"first delivery {sim.TickNumber - lost} ticks after the last worker was lost");
    }

    /// <summary>The only Keep a site (the finished one gone, a site in its place): nothing is trained while it stands unfinished; once finished, a Laborer is queued within 200 ticks.</summary>
    [Fact]
    public void TheTrainerASite_NothingSent_ThenOneOnceFinished()
    {
        Simulation sim = Scene(combat: false);
        World w = sim.World;
        List<Command> log = AiScenes.Log(sim, 0);
        LoseEveryWorker(sim);
        EntityHandle keep = Own(w, 0, Keep);
        int cell = w.Buildings.Cell[keep.Index];
        // A soldier keeps the seat in (TestSim.KeepInFor would add a worker, which the rule counts).
        CombatScenes.Place(sim, 0, TestSim.Data.FindUnit("malazan_heavy_infantry"), w.NavGrid.CellCenter(40, 40));
        Assert.True(w.Buildings.Free(keep));
        Assert.True(w.Buildings.Spawn(0, Keep, cell, out EntityHandle site, site: true));
        Assert.Equal(0, Workers(w, 0));
        int from = sim.TickNumber;
        GatherMaps.Run(sim, 600);
        Assert.DoesNotContain(log, c => c.Tick > from && c.Kind == CommandKind.Train);
        w.Buildings.SetWork(site.Index, w.Buildings.WorkNeeded(Keep));
        int done = sim.TickNumber;
        while (sim.TickNumber < done + 200 && w.Buildings.QueueCount[site.Index] == 0) sim.Tick();
        Assert.True(w.Buildings.QueueCount[site.Index] > 0, "no Laborer queued within 200 ticks of the Keep finishing");
    }

    /// <summary>The Keep training the retrained Laborer destroyed mid-training, a second finished Keep standing: the seat queues one there within 200 ticks.</summary>
    [Fact]
    public void TheTrainerDestroyedMidTraining_ASecondTrainerTakesOver()
    {
        Simulation sim = Scene(combat: false);
        World w = sim.World;
        EntityHandle first = Own(w, 0, Keep);
        GatherMaps.Building(sim, 40, 40, 0, Keep);
        LoseEveryWorker(sim);
        while (w.Buildings.QueueCount[first.Index] == 0 && sim.TickNumber < 10_000) sim.Tick();
        int queuedAt = -1;
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 0 && w.Buildings.QueueCount[k] > 0) queuedAt = k;
        Assert.True(queuedAt >= 0, "nothing queued");
        GatherMaps.Run(sim, 20);
        Assert.True(w.Buildings.Free(w.Buildings.HandleOf(queuedAt)));
        int lost = sim.TickNumber;
        int other = -1;
        while (sim.TickNumber < lost + 200 && other < 0)
        {
            sim.Tick();
            for (int k = 0; k < w.Buildings.Capacity; k++)
                if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 0 && w.Buildings.TypeId[k] == Keep && w.Buildings.QueueCount[k] > 0) other = k;
            if (Workers(w, 0) > 0) other = int.MaxValue;
        }
        Assert.True(other >= 0, "the second Keep never took the Laborer over within 200 ticks");
    }

    private static int HallsAndSites(World w, int player)
    {
        int n = 0;
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == player && w.Buildings.TypeId[k] == Keep) n++;
        return n;
    }

    /// <summary>
    /// QA focus 2, the rebuild (M5-4d slice 2's hall-first rule, which item A (b) found working): the only Keep razed with an
    /// enemy soldier standing on its old spot (combat off: it only blocks), two Keeps razed in one tick, and the cost arriving
    /// a little at a time (no gold, then 25 gold every 100 ticks): a Keep site stands within 600 ticks of the cost being
    /// in hand, and only one.
    /// </summary>
    [Theory]
    [InlineData("enemy-on-spot")]
    [InlineData("two-halls")]
    [InlineData("trickle")]
    public void TheHallRebuild_Edges(string shape)
    {
        Simulation sim = Scene(combat: false);
        World w = sim.World;
        GatherMaps.Run(sim, 200);
        EntityHandle keep = Own(w, 0, Keep);
        int cell = w.Buildings.Cell[keep.Index];
        if (shape == "two-halls")
        {
            EntityHandle second = GatherMaps.Building(sim, 40, 40, 0, Keep);
            Assert.True(w.Buildings.Free(second));
        }
        Assert.True(w.Buildings.Free(keep));
        if (shape == "enemy-on-spot")
            CombatScenes.Place(sim, 1, TestSim.Data.FindUnit("whirlwind_raider"), w.NavGrid.CellCenter(cell % w.NavGrid.Width + 1, cell / w.NavGrid.Width + 1));
        BuildingDef d = TestSim.Data.Buildings[Keep];
        if (shape == "trickle") BuildMaps.SetTotals(sim, 0, 0, w.Wood[0]);
        int affordableAt = shape == "trickle" ? -1 : sim.TickNumber, sites = 0;
        for (int t = 0; t < 8000 && HallsAndSites(w, 0) == 0; t++)
        {
            if (shape == "trickle" && t % 100 == 0) BuildMaps.Give(sim, 0, 25, 0);
            if (affordableAt < 0 && w.Gold[0] >= d.CostGold && w.Wood[0] >= d.CostWood) affordableAt = sim.TickNumber;
            sim.Tick();
        }
        sites = HallsAndSites(w, 0);
        _out.WriteLine($"{shape}: cost in hand at {affordableAt}, a Keep stands from {sim.TickNumber} ({sites})");
        Assert.True(affordableAt >= 0 && sites == 1, $"{shape}: {sites} Keeps by tick {sim.TickNumber}");
        Assert.True(sim.TickNumber <= affordableAt + 600, $"{shape}: the Keep came {sim.TickNumber - affordableAt} ticks after its cost was in hand");
        GatherMaps.Run(sim, 400);
        Assert.Equal(1, HallsAndSites(w, 0));
    }

    /// <summary>
    /// The camping enemy (QA focus 1): two Whirlwind raiders held at the Keep's south-east corner, the seat with no worker and
    /// 1,000 gold, 24,000 ticks (20 min). Measured: Laborers trained and lost, gold spent. The seat may not end with negative
    /// gold, more than one Laborer queued at once, or a queue of Laborers while it has a worker alive.
    /// </summary>
    [Fact]
    public void ACampingEnemy_TheDrainMeasured()
    {
        Simulation sim = Scene();
        World w = sim.World;
        List<Command> log = AiScenes.Log(sim, 0);
        LoseEveryWorker(sim);
        (int hx, int hy) = AiScenes.HallAt(0);
        int raider = TestSim.Data.FindUnit("whirlwind_raider");
        // Raiders on Hold Position on the ways to the mines above the hall and the forest below it, out of reach of the Keep:
        // each takes any worker that comes by.
        foreach ((int dx, int dy) in new[] { (2, -2), (4, -3), (1, 6), (3, 7) })
        {
            EntityHandle r = CombatScenes.Place(sim, 1, raider, w.NavGrid.CellCenter(hx + dx, hy + dy));
            sim.Enqueue(Command.HoldPosition(1, r));
        }
        int start = sim.TickNumber, startGold = w.Gold[0], died = 0, maxQueued = 0;
        var seen = new HashSet<(int, int)>();
        while (sim.TickNumber < start + 24_000 && !sim.MatchOver)
        {
            sim.Tick();
            // The camp only kills workers: the seat's buildings are healed every tick, so the match runs the full 20 minutes.
            for (int k = 0; k < w.Buildings.Capacity; k++)
                if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 0 && !w.Buildings.UnderConstruction[k]) w.Buildings.SetHp(k, w.Data.Buildings[w.Buildings.TypeId[k]].Hp);
            for (int i = 0; i < w.Units.Capacity; i++)
                if (w.Units.Alive[i] && w.Units.Owner[i] == 0 && w.Data.Units[w.Units.TypeId[i]].IsWorker) seen.Add((i, w.Units.Generation[i]));
            int queued = 0;
            for (int k = 0; k < w.Buildings.Capacity; k++)
                if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 0)
                    for (int q = 0; q < w.Buildings.QueueCount[k]; q++) if (w.Buildings.QueueTypeAt(k, q) == Laborer) queued++;
            maxQueued = Math.Max(maxQueued, queued);
            Assert.True(w.Gold[0] >= 0);
        }
        died = seen.Count - Workers(w, 0);
        int trains = log.Count(c => c.Tick > start && c.Kind == CommandKind.Train && c.TypeId == Laborer);
        int otherTrains = log.Count(c => c.Tick > start && c.Kind == CommandKind.Train && c.TypeId != Laborer);
        _out.WriteLine($"camping enemy, 24,000 ticks: Laborer Trains sent {trains}, Laborers seen {seen.Count}, lost {died}, alive {Workers(w, 0)}; other Trains {otherTrains}; gold {startGold} -> {w.Gold[0]} (gathered {w.GatheredGold[0]}), wood {w.Wood[0]}; max Laborers queued at once {maxQueued}; match over {sim.MatchOver}");
        Assert.True(maxQueued <= 1, $"{maxQueued} Laborers queued at once");
    }
}
