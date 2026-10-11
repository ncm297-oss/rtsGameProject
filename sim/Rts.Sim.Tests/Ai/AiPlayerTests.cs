using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Replays;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-2 criterion 3: <see cref="AiPlayer"/>'s build-order executor, economy and production managers (docs/03 "Implementation
/// (M5-2)"), on the flat AI scene (<see cref="AiScenes.Flat"/>). Every command is read back from a replay recorder: the AI
/// sends only through the command door.
/// </summary>
public class AiPlayerTests
{
    private readonly ITestOutputHelper _out;

    public AiPlayerTests(ITestOutputHelper output) => _out = output;

    private static int Unit(string key) => TestSim.Data.FindUnit(key);

    private static int Building(string key) => TestSim.Data.FindBuilding(key);

    /// <summary>The shipped ai.json steps run: each faction's AI trains its worker in its first think (tick 0, applied at tick 1), and the step completes.</summary>
    [Theory]
    [InlineData(AiDifficulty.Easy)]
    [InlineData(AiDifficulty.Normal)]
    [InlineData(AiDifficulty.Hard)]
    public void ShippedSteps_Run_BothFactions(AiDifficulty difficulty)
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(difficulty, SeatController.Ai, SeatController.Ai));
        List<Command>[] logs = { AiScenes.Log(sim, 0), AiScenes.Log(sim, 1) };
        int think = sim.AiOf(0)!.ThinkTicks;
        Assert.Equal(TestSim.Data.Ai[0].Difficulty(difficulty).ThinkTicks, think);
        // The step is due at 0 and waits the difficulty's slack: sent at the first think from there.
        int slack = TestSim.Data.Ai[0].Difficulty(difficulty).BuildOrderSlackTicks;
        int firstThink = (slack + think - 1) / think * think;
        for (int t = 0; t < firstThink + think + 2; t++) sim.Tick();
        for (int p = 0; p < 2; p++)
        {
            AiStep step = TestSim.Data.Ai[sim.World.FactionOf(p)].BuildOrder[0];
            Assert.Equal(AiStepAction.Train, step.Action);
            Command train = Assert.Single(logs[p], c => c.Kind == CommandKind.Train);
            Assert.Equal(step.Target, train.TypeId);
            Assert.Equal(firstThink + 1, train.Tick); // sent at that think, applied in the next tick
            AiPlayer ai = sim.AiOf(p)!;
            Assert.True(ai.Board.StepDone[0], $"player {p}: the shipped step did not complete");
            Assert.Equal(1, ai.Board.StepQueued[0]);
            // The idle workers were sent to gather.
            Assert.Contains(logs[p], c => c.Kind == CommandKind.Gather);
        }
    }

    /// <summary>The 10-step test order (<see cref="TenSteps"/>): triggers of every kind, counts, slack, concurrent steps.</summary>
    private static GameData TenStepData() => AiScenes.DataWithAi("malazan", root =>
    {
        root["buildOrder"] = AiScenes.BuildOrder(
            ("time", 0, "train", "malazan_laborer", 3),          // 0: due at once, three Trains over three thinks
            ("time", 2, "build", "malazan_depot", 0),            // 1: a camp, by the forest
            ("workers", 7, "build", "malazan_barracks", 0),      // 2: due once two laborers are out
            ("time", 6, "build", "malazan_crossbow_range", 0),   // 3
            ("pop", 8, "build", "malazan_armory", 0),            // 4: pop 8 = 16 half-pop
            ("workers", 8, "research", "armor_1", 0),            // 5: waits for the armory
            ("time", 1, "train", "malazan_heavy_infantry", 2),   // 6: waits for the barracks
            ("pop", 9, "train", "malazan_crossbowman", 1),       // 7: waits for the range
            ("time", 20, "research", "melee_weapons_1", 0),      // 8: waits for the armory (and step 5's queue)
            ("time", 30, "expand", null, 0));                    // 9: the middle mine
        // Production would train the composition's line and ranged units, which steps 6 and 7 train: give it a unit no
        // step trains and nothing here unlocks (the corral is never built), so every Train below is a step's.
        root["compositions"] = JsonNode.Parse("""[{ "age": 1, "units": { "malazan_wickan_lancer": 1 } }, { "age": 2, "units": { "malazan_wickan_lancer": 1 } }]""");
        foreach (string d in new[] { "easy", "normal", "hard" }) root["difficulties"]![d]!["expandAfter"] = 10;
    });

    private static readonly Lazy<GameData> s_tenSteps = new(TenStepData);

    /// <summary>
    /// A 10-step build order with time, workers and pop triggers, counts, slack and concurrent steps fires in the documented
    /// order on Easy (2 s thinks, 30 s slack) and Hard (0.5 s, no slack): every step becomes due at the first think its trigger
    /// holds (sampled here at every think from the world, as the view shows it), sends nothing before due + slack, and a step
    /// that can act at once (resources plenty, a worker free, its building there) sends its first command at the first think
    /// from due + slack; within a think the executor's commands follow file order; at most one command per step per think; a
    /// train step queues exactly its count; every step completes.
    /// </summary>
    [Theory]
    [InlineData(AiDifficulty.Easy)]
    [InlineData(AiDifficulty.Hard)]
    public void TenStepBuildOrder_FiresInTheDocumentedOrder(AiDifficulty difficulty)
    {
        GameData data = s_tenSteps.Value;
        Simulation sim = AiScenes.Flat(AiScenes.Seats(difficulty, SeatController.Ai, SeatController.Human), data, expansionMine: true);
        BuildMaps.SetTotals(sim, 0, 100_000, 100_000);
        List<Command> rec = AiScenes.Log(sim, 0);
        AiPlayer ai = sim.AiOf(0)!;
        AiDef def = data.Ai[0];
        int think = ai.ThinkTicks, slack = def.Difficulty(difficulty).BuildOrderSlackTicks, expandAfter = def.Difficulty(difficulty).ExpandAfterTicks;
        int steps = def.BuildOrder.Length;
        Assert.Equal(10, steps);
        var expectedDue = Enumerable.Repeat(-1, steps).ToArray();
        int worker = Unit("malazan_laborer");
        World w = sim.World;
        for (int t = 0; t < 6000 && !Enumerable.Range(0, steps).All(i => ai.Board.StepDone[i]); t++)
        {
            if (sim.TickNumber % think == 0)
            {
                int workers = 0;
                for (int i = 0; i < w.Units.Capacity; i++)
                    if (w.Units.Alive[i] && w.Units.Owner[i] == 0 && w.Units.TypeId[i] == worker) workers++;
                for (int i = 0; i < steps; i++)
                {
                    if (expectedDue[i] >= 0) continue;
                    AiStep s = def.BuildOrder[i];
                    bool on = s.Trigger switch
                    {
                        AiStepTrigger.Time => sim.TickNumber >= s.At,
                        AiStepTrigger.Workers => workers >= s.At,
                        _ => w.HalfPop[0] >= s.At,
                    };
                    if (on) expectedDue[i] = s.Trigger == AiStepTrigger.Time ? s.At : sim.TickNumber;
                }
            }
            sim.Tick();
        }
        for (int i = 0; i < steps; i++) Assert.True(ai.Board.StepDone[i], $"{difficulty}: step {i} never completed (due {ai.Board.StepDueTick[i]}, queued {ai.Board.StepQueued[i]})");
        Assert.Equal(expectedDue, ai.Board.StepDueTick);

        // Each executor command, attributed to its step by (kind, type), with the think it was sent at (its stamp - 1).
        int hall = Building("malazan_garrison_keep");
        int StepOf(Command c)
        {
            for (int i = 0; i < steps; i++)
            {
                AiStep s = def.BuildOrder[i];
                if (s.Action == AiStepAction.Expand ? c.Kind == CommandKind.Build && c.TypeId == hall
                    : c.TypeId == s.Target && c.Kind == s.Action switch { AiStepAction.Train => CommandKind.Train, AiStepAction.Build => CommandKind.Build, _ => CommandKind.Research })
                    return i;
            }
            return -1;
        }
        var byStep = new List<int>[steps];
        for (int i = 0; i < steps; i++) byStep[i] = new List<int>();
        int lastThink = -1, lastStep = -1;
        foreach (Command c in rec)
        {
            int i = StepOf(c);
            if (i < 0) continue; // the economy's Gathers and houses
            int at = c.Tick - 1;
            Assert.Equal(0, at % think);
            if (at == lastThink) Assert.True(i > lastStep, $"{difficulty}: at tick {at} step {i} sent after step {lastStep} (file order)");
            lastThink = at;
            lastStep = i;
            byStep[i].Add(at);
        }
        for (int i = 0; i < steps; i++)
        {
            List<int> sent = byStep[i];
            Assert.NotEmpty(sent);
            Assert.Equal(sent.Count, sent.Distinct().Count()); // at most one command per step per think
            int from = expectedDue[i] + slack;
            if (def.BuildOrder[i].Action == AiStepAction.Expand) from = Math.Max(from, expandAfter);
            int firstThink = (from + think - 1) / think * think;
            Assert.True(sent[0] >= from, $"{difficulty}: step {i} sent at {sent[0]}, before due {expectedDue[i]} + slack {slack}");
            if (i is 0 or 1 or 2 or 3 or 4 or 9)
                Assert.True(sent[0] == firstThink, $"{difficulty}: step {i} sent first at {sent[0]}, not at the first think from {from} ({firstThink})");
            _out.WriteLine($"{difficulty} step {i} ({def.BuildOrder[i].Action} {def.BuildOrder[i].TargetKey}): due {expectedDue[i]}, sent at {string.Join(", ", sent)}");
        }
        Assert.Equal(3, ai.Board.StepQueued[0]);
        Assert.Equal(3, byStep[0].Count); // all accepted: no retry
        Assert.Equal(2, ai.Board.StepQueued[6]);
        Assert.Equal(1, ai.Board.StepQueued[7]);
        // The camp went by the forest (rows 39-41 below player 0's hall), the expansion by the middle mine.
        Assert.Contains(rec, c => c.Kind == CommandKind.Build && c.TypeId == Building("malazan_depot") && c.Position.Y > 34 * 2f);
        Assert.Contains(rec, c => c.Kind == CommandKind.Build && c.TypeId == hall && Math.Abs(c.Position.X - 31 * 2f) < 16 * 2f);
    }

    /// <summary>
    /// A Build with no wood is not sent (no command at all) and is retried at every think, and sent at the first think after
    /// the wood arrives, once; the tick runs on meanwhile.
    /// </summary>
    [Fact]
    public void ABuildWithoutWood_WaitsThenIsSentAtTheNextThink_Once()
    {
        GameData data = AiScenes.DataWithAi("malazan", root =>
            root["buildOrder"] = AiScenes.BuildOrder(("time", 0, "build", "malazan_barracks", 0)));
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Human), data);
        BuildMaps.SetTotals(sim, 0, 0, 0);
        List<Command> rec = AiScenes.Log(sim, 0);
        AiPlayer ai = sim.AiOf(0)!;
        int think = ai.ThinkTicks;
        for (int t = 0; t < 10 * think; t++) sim.Tick();
        Assert.DoesNotContain(rec, c => c.Kind == CommandKind.Build);
        Assert.Equal(10, ai.Thinks);
        BuildMaps.SetTotals(sim, 0, 0, 150); // the barracks' cost
        int given = sim.TickNumber;
        for (int t = 0; t < 3 * think; t++) sim.Tick();
        Command build = Assert.Single(rec, c => c.Kind == CommandKind.Build);
        Assert.Equal(given + 1, build.Tick); // the first think after the wood (tick `given`) sent it
        Assert.True(ai.Board.StepDone[0]);
    }

    /// <summary>
    /// A Build the sim refuses (an enemy unit holding in the footprint: the AI's fog-safe placement doesn't look at units, the
    /// Build does) is judged at the next think, its anchor marked, and sent again at another anchor at that think: one Build
    /// per think, never two at the same refused anchor, and the step then completes. The run never stalls.
    /// </summary>
    [Fact]
    public void ARefusedBuild_IsRetriedAtTheNextThink_AtAnotherAnchor_NotSpammed()
    {
        GameData data = AiScenes.DataWithAi("malazan", root =>
            root["buildOrder"] = AiScenes.BuildOrder(("time", 0, "build", "malazan_barracks", 0)));
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Human);
        int barracks = Building("malazan_barracks");

        // A dry run finds where the AI places the barracks.
        Simulation dry = AiScenes.Flat(seats, data);
        List<Command> dryRec = AiScenes.Log(dry, 0);
        dry.Tick();
        Command first = Assert.Single(dryRec, c => c.Kind == CommandKind.Build);
        Assert.True(dry.World.NavGrid.WorldToCell(first.Position, out int ax, out int ay));

        // The same scene with an enemy unit holding inside that footprint.
        Simulation sim = AiScenes.Flat(seats, data);
        EntityHandle blocker = CombatScenes.Place(sim, 1, Unit("whirlwind_raider"), sim.World.NavGrid.CellCenter(ax + 1, ay + 1));
        sim.World.Units.Hold[blocker.Index] = true;
        List<Command> rec = AiScenes.Log(sim, 0);
        AiPlayer ai = sim.AiOf(0)!;
        int think = ai.ThinkTicks;
        for (int t = 0; t < 6 * think; t++) sim.Tick();
        List<Command> builds = rec.Where(c => c.Kind == CommandKind.Build).ToList();
        foreach (Command c in builds) _out.WriteLine($"Build at tick {c.Tick}, {c.Position}");
        // The first at the dry run's anchor, then one a think at the next anchor out, until one whose footprint misses the
        // blocker is accepted: each refused one is marked, none repeated.
        Assert.Equal(first.Position, builds[0].Position);
        Assert.True(builds.Count >= 2, "the refused Build was not retried");
        BuildingDef def = TestSim.Data.Buildings[barracks];
        for (int k = 0; k < builds.Count; k++)
        {
            Assert.Equal(1 + k * think, builds[k].Tick); // one a think, at consecutive thinks
            Assert.True(sim.World.NavGrid.WorldToCell(builds[k].Position, out int bx, out int by));
            bool covers = 1 + ax >= bx && 1 + ax < bx + def.FootprintWidth && 1 + ay >= by && 1 + ay < by + def.FootprintHeight;
            Assert.True(covers == (k < builds.Count - 1), $"Build {k} at ({bx}, {by}): covers the blocker {covers}");
            if (k < builds.Count - 1) Assert.True(ai.Board.IsBad(barracks, by * AiScenes.FlatSize + bx));
        }
        Assert.Equal(builds.Count, builds.Select(c => c.Position).Distinct().Count());
        Assert.True(ai.Board.StepDone[0]);
        Assert.Equal(6 * think, sim.TickNumber);
    }

    /// <summary>
    /// The house rule: with the cap within 2 pop of the population, a house is placed, and no second one while it is under
    /// construction (nor before its Build applied); once finished the cap is far enough again; at the rules' cap none.
    /// </summary>
    [Fact]
    public void HouseRule_OneHouseWhenNearTheCap_NoneWhileOneIsBuilding()
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Human);
        // 9 workers: pop 18 half-pop of the hall's 20, within the rule's 4.
        Simulation sim = AiScenes.Flat(seats, workers: 9);
        BuildMaps.SetTotals(sim, 0, 1000, 1000);
        List<Command> rec = AiScenes.Log(sim, 0);
        int house = Building("malazan_billet");
        int think = sim.AiOf(0)!.ThinkTicks;
        Assert.Equal(18, sim.World.HalfPop[0]);
        Assert.Equal(20, sim.World.HalfPopCap[0]);
        int built = -1;
        for (int t = 0; t < 3000 && built < 0; t++)
        {
            sim.Tick();
            if (sim.World.HalfPopCap[0] > 20) built = sim.TickNumber;
        }
        Assert.True(built > 0, "the house never finished");
        List<Command> houses = rec.Where(c => c.Kind == CommandKind.Build && c.TypeId == house).ToList();
        Command one = Assert.Single(houses);
        Assert.Equal(1, one.Tick); // the first think saw the cap within 2 pop
        // Pop stays at or below 20 + 2 (the shipped order trains one laborer): 36 - 20 > 4, so no house for a while.
        for (int t = 0; t < 20 * think; t++) sim.Tick();
        Assert.Single(rec, c => c.Kind == CommandKind.Build && c.TypeId == house);
    }

    [Fact]
    public void HouseRule_AtTheRulesCap_PlacesNone()
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Human);
        Simulation sim = AiScenes.Flat(seats, workers: 9);
        BuildMaps.SetTotals(sim, 0, 1000, 1000);
        // Houses enough for the cap (100 pop): the hall's 10 and 12 billets of 8, spawned straight into the store.
        int house = Building("malazan_billet");
        for (int k = 0; k < 12; k++) Assert.True(sim.World.Buildings.Spawn(0, house, (2 + 3 * (k / 6)) * AiScenes.FlatSize + 20 + 3 * (k % 6), out _));
        Assert.Equal(200, sim.World.HalfPopCap[0]);
        // Pop 99 of 100: 90 more laborers, so only the rules' cap keeps the house rule quiet.
        for (int k = 0; k < 90; k++) CombatScenes.Place(sim, 0, Unit("malazan_laborer"), sim.World.NavGrid.CellCenter(2 + k % 45, 46 + 2 * (k / 45)));
        Assert.Equal(198, sim.World.HalfPop[0]);
        List<Command> rec = AiScenes.Log(sim, 0);
        for (int t = 0; t < 10 * sim.AiOf(0)!.ThinkTicks; t++) sim.Tick();
        Assert.DoesNotContain(rec, c => c.Kind == CommandKind.Build && c.TypeId == house);
    }

    /// <summary>
    /// The composition fill (Heavy Infantry 2 : Crossbowman 1, shipped Age I): with a barracks and a crossbow range free and
    /// plenty to spend, the first think the build order sends nothing trains the most under-weighted unit (own live + queued
    /// per unit of weight; composition order, ascending unit ids, on a tie); the think the executor sent something (tick 0's
    /// worker) trains nothing.
    /// </summary>
    [Theory]
    [InlineData(0, 0)] // a tie: the composition's first unit
    [InlineData(2, 0)] // 2 / 2 v 0 / 1: the crossbowman
    [InlineData(0, 1)] // 0 / 2 v 1 / 1: the heavy infantry
    [InlineData(4, 1)] // 4 / 2 v 1 / 1: the crossbowman
    [InlineData(1, 2)] // 1 / 2 v 2 / 1: the heavy infantry
    public void CompositionFill_TrainsTheMostUnderWeightedUnit(int heavyLive, int bowLive)
    {
        Simulation sim = CompositionScene(out int heavy, out int bow);
        for (int k = 0; k < heavyLive; k++) CombatScenes.Place(sim, 0, heavy, sim.World.NavGrid.CellCenter(40 + k, 50));
        for (int k = 0; k < bowLive; k++) CombatScenes.Place(sim, 0, bow, sim.World.NavGrid.CellCenter(40 + k, 52));
        List<Command> log = AiScenes.Log(sim, 0);
        int think = sim.AiOf(0)!.ThinkTicks;
        for (int t = 0; t < think + 1; t++) sim.Tick();
        AiComposition mix = TestSim.Data.Ai[0].Compositions[0];
        float Share(int type, int live) => live / mix.Weights[mix.Units.IndexOf(type)];
        float h = Share(heavy, heavyLive), b = Share(bow, bowLive);
        int expected = h < b ? heavy : b < h ? bow : mix.Units[0];
        Assert.DoesNotContain(log, c => c.Kind == CommandKind.Train && c.Tick == 1 && c.TypeId != Unit("malazan_laborer"));
        Command first = Assert.Single(log, c => c.Kind == CommandKind.Train && c.TypeId != Unit("malazan_laborer"));
        Assert.Equal(think + 1, first.Tick);
        Assert.Equal(expected, first.TypeId);
    }

    /// <summary>
    /// Over a long run: at most one production Train a think, only into an empty queue (no building ever holds two), and the
    /// buildings are kept busy: each trains its unit whenever it is free, so the mix follows the buildings, not the weights,
    /// once only one of them is free (the documented reading: the most under-weighted among the units a free building trains).
    /// </summary>
    [Fact]
    public void CompositionFill_OneTrainAThink_OnlyIntoEmptyQueues()
    {
        Simulation sim = CompositionScene(out int heavy, out int bow);
        List<Command> log = AiScenes.Log(sim, 0);
        int laborer = Unit("malazan_laborer");
        BuildingStore bs = sim.World.Buildings;
        for (int t = 0; t < 2400; t++)
        {
            sim.Tick();
            for (int k = 0; k < bs.Capacity; k++)
                if (bs.Alive[k] && bs.Owner[k] == 0 && bs.TypeId[k] != Building("malazan_garrison_keep"))
                    Assert.True(bs.QueueCount[k] <= 1, $"tick {sim.TickNumber}: building {k} holds {bs.QueueCount[k]}");
        }
        List<Command> trains = log.Where(c => c.Kind == CommandKind.Train && c.TypeId != laborer).ToList();
        Assert.All(trains.GroupBy(c => c.Tick), g => Assert.Single(g));
        int hc = trains.Count(c => c.TypeId == heavy), bc = trains.Count(c => c.TypeId == bow);
        _out.WriteLine($"heavy {hc}, crossbow {bc}");
        Assert.True(hc >= 8 && bc >= 8, $"heavy {hc}, crossbow {bc}: a building stood idle");
    }

    /// <summary>The flat scene for player 0 (Hard) with a finished barracks and crossbow range and plenty to spend.</summary>
    private static Simulation CompositionScene(out int heavy, out int bow)
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Human));
        Assert.True(sim.World.Buildings.Spawn(0, Building("malazan_barracks"), 20 * AiScenes.FlatSize + 20, out _));
        Assert.True(sim.World.Buildings.Spawn(0, Building("malazan_crossbow_range"), 20 * AiScenes.FlatSize + 26, out _));
        BuildMaps.SetTotals(sim, 0, 100_000, 100_000);
        heavy = Unit("malazan_heavy_infantry");
        bow = Unit("malazan_crossbowman");
        return sim;
    }
}
