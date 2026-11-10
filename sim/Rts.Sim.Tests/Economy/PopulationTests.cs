using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-1 criterion 2: half-step population end to end (docs/02 "Economy": pop costs are integers or half-steps; the sim counts
/// half-pop, docs/03 "Implementation (M3-4)"), on a test copy of the shipped data where Malazan Heavy Infantry is pop 1.5 (3
/// half-pop) and the Crossbowman pop 3 (6): the producer's cap check, the bar's text, the AI's <c>pop</c> step trigger and
/// <c>attack.minArmyPop</c>, and the count kept exact through spawns, deaths and cancels.
/// </summary>
public class PopulationTests
{
    private static readonly Lazy<GameData> s_half = new(() => AiScenes.DataWith(HalfSteps));

    private static void HalfSteps(TestDataDir dir)
    {
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "pop", "1.5");
        dir.SetUnitField("malazan", "malazan_crossbowman", "pop", "3");
    }

    private static int Heavy(GameData d) => d.FindUnit("malazan_heavy_infantry");

    private static int Laborer(GameData d) => d.FindUnit("malazan_laborer");

    [Fact]
    public void TheCopy_HasAHalfStepUnit_AndAThreePopOne()
    {
        GameData d = s_half.Value;
        Assert.Equal((3, 6), (d.Units[Heavy(d)].HalfPop, d.Units[d.FindUnit("malazan_crossbowman")].HalfPop));
    }

    /// <summary>A one-player flat sim on the copy: a Keep (cap 10 = 20 half-pop) and a Barracks, plenty to spend, and the units given.</summary>
    private static (Simulation Sim, int Barracks) Base(int heavies, int laborers)
    {
        GameData d = s_half.Value;
        var sim = new Simulation(new SimConfig(5, 1, 64, 544) { Data = d }, LocalMovementTests.Flat(40));
        World w = sim.World;
        Assert.True(w.Buildings.Spawn(0, d.FindBuilding("malazan_garrison_keep"), 10 * 40 + 25, out _));
        Assert.True(w.Buildings.Spawn(0, d.FindBuilding("malazan_barracks"), 10 * 40 + 10, out EntityHandle barracks));
        BuildMaps.SetTotals(sim, 0, 5000, 5000);
        for (int k = 0; k < heavies; k++) CombatScenes.Place(sim, 0, Heavy(d), CombatScenes.At(sim, 4 + k, 25));
        for (int k = 0; k < laborers; k++) CombatScenes.Place(sim, 0, Laborer(d), CombatScenes.At(sim, 4 + k, 28));
        return (sim, barracks.Index);
    }

    /// <summary>One half short (cap - pop = 2 for a 3 half-pop item): the head waits unstarted; at exactly 3 it starts.</summary>
    [Fact]
    public void AProducer_RefusesAHalfStepItem_OneHalfShort_AndStartsItAtExactlyItsHalfPop()
    {
        (Simulation sim, int bar) = Base(heavies: 2, laborers: 6); // 6 + 12 = 18 of 20
        World w = sim.World;
        Assert.Equal((18, 20), (w.HalfPop[0], w.HalfPopCap[0]));
        Assert.Equal("9", PopText.Format(w.HalfPop[0]));
        sim.Enqueue(Command.Train(0, ProductionMaps.In(sim, bar), Heavy(w.Data)));
        for (int k = 0; k < 60; k++) sim.Tick();
        Assert.Equal((1, 0), (w.Buildings.QueueCount[bar], w.Buildings.Progress[bar])); // queued and paid, never started
        Assert.Equal(18, w.HalfPop[0]);
        Assert.False(PopText.AtCap(w.HalfPop[0], w.HalfPopCap[0]));

        // A laborer leaves (2 half-pop): 16 of 20, room for 4, so the head starts and reserves its 3.
        int laborer = ProductionMaps.LiveUnits(w).First(i => w.Units.TypeId[i] == Laborer(w.Data));
        w.Units.Free(new EntityHandle(laborer, w.Units.Generation[laborer]));
        Assert.Equal("8", PopText.Format(w.HalfPop[0]));
        sim.Tick();
        Assert.Equal((1, 19), (w.Buildings.Progress[bar], w.HalfPop[0]));
        Assert.Equal("9.5", PopText.Format(w.HalfPop[0]));
    }

    [Fact]
    public void AProducer_WithExactlyThreeHalvesFree_StartsTheItem_AndTheBarPrintsTheHalf()
    {
        (Simulation sim, int bar) = Base(heavies: 1, laborers: 7); // 3 + 14 = 17 of 20
        World w = sim.World;
        Assert.Equal((17, 20), (w.HalfPop[0], w.HalfPopCap[0]));
        Assert.Equal("8.5", PopText.Format(w.HalfPop[0]));
        sim.Enqueue(Command.Train(0, ProductionMaps.In(sim, bar), Heavy(w.Data)));
        sim.Tick();
        sim.Tick(); // applies, and the item starts this tick
        Assert.Equal((1, 1), (w.Buildings.QueueCount[bar], w.Buildings.Progress[bar]));
        Assert.Equal(20, w.HalfPop[0]);
        Assert.True(PopText.AtCap(w.HalfPop[0], w.HalfPopCap[0]));
        Assert.Equal("10", PopText.Format(w.HalfPop[0]));
        int units = w.Units.Count;
        for (int k = 0; k < w.Data.Units[Heavy(w.Data)].TrainTicks; k++) sim.Tick();
        Assert.Equal(units + 1, w.Units.Count); // spawned: the reservation became the unit's own count
        Assert.Equal(20, w.HalfPop[0]);
        Assert.Equal(20, ProductionMaps.RecountHalfPop(w, 0));
    }

    /// <summary>The AI copy: a build order of one pop step (4.5 pop = 9 half-pop) on every opening, nothing else trained.</summary>
    private static readonly Lazy<GameData> s_popStep = new(() => AiScenes.DataWith(dir =>
    {
        HalfSteps(dir);
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            foreach (JsonNode? o in root["openings"]!.AsArray())
                o!["buildOrder"] = AiScenes.BuildOrder(("pop", 4.5, "build", "malazan_armory", 0));
        });
    }));

    /// <summary>The AI's <c>pop</c> trigger reads the half: 4.5 pop is due at 9 half-pop (two laborers and a 1.5 unit + one more laborer), not at 8.</summary>
    [Theory]
    [InlineData(3, 1, true)]  // 6 + 3 = 9: pop 4.5
    [InlineData(4, 0, false)] // 8: pop 4
    [InlineData(2, 1, false)] // 4 + 3 = 7: pop 3.5
    public void TheAisPopTrigger_CountsTheHalf(int workers, int heavies, bool due)
    {
        GameData d = s_popStep.Value;
        Assert.Equal(9, d.Ai[d.FindFaction("malazan")].Openings[0].BuildOrder[0].At);
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Easy, SeatController.Ai, SeatController.Human), d, workers: workers);
        for (int k = 0; k < heavies; k++) CombatScenes.Place(sim, 0, Heavy(d), sim.World.NavGrid.CellCenter(20 + k, 20));
        AiPlayer ai = sim.AiOf(0)!;
        Assert.Equal(2 * workers + 3 * heavies, ai.View.HalfPop);
        sim.Tick(); // tick 0's think
        Assert.Equal(due, ai.Board.StepDueTick[0] == 0);
    }

    /// <summary>The AI copy for waves: a quiet build order and a wave at <paramref name="pop"/> pop and no cost, Normal at x 1.</summary>
    private static GameData WaveData(double pop) => AiScenes.DataWith(dir =>
    {
        HalfSteps(dir);
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            foreach (JsonNode? o in root["openings"]!.AsArray())
            {
                o!["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1));
                o["attack"] = new JsonObject { ["minArmyPop"] = pop, ["minArmyCost"] = 0 };
            }
            root["difficulties"]!["normal"]!["waveSizeMultiplier"] = 1;
        });
    });

    /// <summary><c>attack.minArmyPop</c> counts the half: three 1.5-pop Heavy Infantry (9 half-pop) meet 4.5 and not 5.</summary>
    [Theory]
    [InlineData(4.5, 3, 1)]
    [InlineData(5, 3, 0)]
    [InlineData(4.5, 2, 0)]
    public void TheAisMinArmyPop_CountsTheHalf(double pop, int heavies, int waves)
    {
        GameData d = WaveData(pop);
        Assert.Equal((int)(pop * 2), d.Ai[d.FindFaction("malazan")].Openings[0].AttackMinHalfPop);
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), d);
        World w = sim.World;
        // Player 0 sees player 1's hall through a billet of its own beside it (MilitaryManagerTests' scene).
        Assert.True(w.Buildings.Spawn(0, d.FindBuilding("malazan_billet"), 31 * AiScenes.FlatSize + 47, out _));
        sim.Tick();
        Assert.True(sim.AiOf(0)!.Board.KnownEnemyBuildings > 0);
        for (int k = 0; k < heavies; k++) CombatScenes.Place(sim, 0, Heavy(d), w.NavGrid.CellCenter(18 + k, 28));
        int think = sim.AiOf(0)!.ThinkTicks;
        for (int k = 0; k < 3 * think; k++) sim.Tick();
        Assert.Equal(waves, sim.AiOf(0)!.Board.WavesStarted);
    }

    /// <summary>
    /// The invariant over a 2,000-tick scripted match on the copy: every tick, each player's <c>HalfPop</c> equals its live
    /// units' half-pop plus its started queue items' (<see cref="ProductionMaps.RecountHalfPop"/>), through trains (Heavy
    /// Infantry 1.5, Crossbowmen 3), spawns, the cap's refusals, cancels of started heads, and deaths in fights with Raiders.
    /// </summary>
    [Fact]
    public void HalfPop_IsTheSumOfLiveUnitsAndStartedItems_EveryTick_ThroughSpawnsDeathsAndCancels()
    {
        GameData d = s_half.Value;
        var sim = new Simulation(new SimConfig(9, 2, 256, 2080) { Data = d }, LocalMovementTests.Flat(64));
        World w = sim.World;
        Assert.True(w.Buildings.Spawn(0, d.FindBuilding("malazan_garrison_keep"), 10 * 64 + 6, out _));
        Assert.True(w.Buildings.Spawn(0, d.FindBuilding("malazan_barracks"), 30 * 64 + 6, out EntityHandle barracks));
        Assert.True(w.Buildings.Spawn(0, d.FindBuilding("malazan_crossbow_range"), 40 * 64 + 6, out EntityHandle range));
        Assert.True(w.Buildings.Spawn(1, d.FindBuilding("whirlwind_holy_camp"), 30 * 64 + 56, out _));
        BuildMaps.SetTotals(sim, 0, 100_000, 100_000);
        Vector2 atBarracks = ProductionMaps.In(sim, barracks.Index), atRange = ProductionMaps.In(sim, range.Index);
        int heavy = Heavy(d), bow = d.FindUnit("malazan_crossbowman"), raider = d.FindUnit("whirlwind_raider");
        int odd = 0, refused = 0, deaths = 0, cancels = 0;
        while (sim.TickNumber < 2000)
        {
            int t = sim.TickNumber;
            if (t % 40 == 0) sim.Enqueue(Command.Train(0, atBarracks, heavy));
            if (t % 90 == 0) sim.Enqueue(Command.Train(0, atRange, bow));
            if (t % 170 == 85 && w.Buildings.Progress[barracks.Index] > 0)
            {
                sim.Enqueue(Command.CancelTrain(0, atBarracks, 0));
                cancels++;
            }
            // Raiders appear far east, and player 0's army walks out to meet them (fights away from the buildings).
            if (t % 250 == 100)
                for (int k = 0; k < 3; k++) sim.Enqueue(Command.SpawnUnit(1, raider, CombatScenes.At(sim, 52 + k, 32)));
            if (t % 250 == 50)
                foreach (int i in ProductionMaps.LiveUnits(w))
                    if (w.Units.Owner[i] == 0) sim.Enqueue(Command.AttackMove(0, new EntityHandle(i, w.Units.Generation[i]), CombatScenes.At(sim, 50, 32)));
            int lossesBefore = w.Losses[0] + w.Losses[1];
            sim.Tick();
            deaths += w.Losses[0] + w.Losses[1] - lossesBefore;
            for (int p = 0; p < 2; p++)
                Assert.True(ProductionMaps.RecountHalfPop(w, p) == w.HalfPop[p], $"tick {sim.TickNumber}, player {p}: {w.HalfPop[p]} counted, {ProductionMaps.RecountHalfPop(w, p)} live + started");
            if (w.HalfPop[0] % 2 == 1) odd++;
            foreach (EntityHandle b in new[] { barracks, range })
                if (w.Buildings.QueueCount[b.Index] > 0 && w.Buildings.Progress[b.Index] == 0) refused++;
        }
        Assert.True(odd > 0, "the count never stood at a half");
        Assert.True(refused > 0, $"the cap never held an item back: pop {w.HalfPop[0]} / {w.HalfPopCap[0]}");
        Assert.True(deaths > 0, "nobody died");
        Assert.True(cancels > 0, "no started head was cancelled");
    }
}
