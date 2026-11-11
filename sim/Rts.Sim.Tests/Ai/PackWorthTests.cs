using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-3 (BUG-1168): a pack unit's worth is its share of the item (<see cref="UnitDef.UnitCostGold"/> /
/// <see cref="UnitDef.UnitCostWood"/> = the item's cost / <c>spawnCount</c>, the remainder dropped), read by every per-unit
/// worth sum; the AI's queue census counts a queued pack as its units; a build-order <c>train</c> step's <c>count</c> counts
/// items. On test copies where Malazan Heavy Infantry is a pack of 3 (54 / 20 an item: 18 / 6 a unit).
/// </summary>
public class PackWorthTests
{
    private static GameData Copy(int minArmyCost, (string Trigger, double At, string Do, string? Id, int Count) step) => AiScenes.DataWith(dir =>
    {
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "spawnCount", "3");
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            foreach (JsonNode? o in root["openings"]!.AsArray())
            {
                o!["buildOrder"] = AiScenes.BuildOrder(step);
                o["attack"] = new JsonObject { ["minArmyPop"] = 1, ["minArmyCost"] = minArmyCost };
                // As AiPlayerTests' ten steps: production trains a unit nothing here unlocks (no corral), so every Train is a step's.
                o["compositions"] = JsonNode.Parse("""[{ "age": 1, "units": { "malazan_wickan_lancer": 1 } }, { "age": 2, "units": { "malazan_wickan_lancer": 1 } }]""");
            }
            root["difficulties"]!["normal"]!["waveSizeMultiplier"] = 1;
        });
    });

    private static readonly Lazy<GameData> s_wave = new(() => Copy(100, ("time", 3600, "train", "malazan_laborer", 1)));

    private static readonly Lazy<GameData> s_train = new(() => Copy(100_000, ("time", 0, "train", "malazan_heavy_infantry", 2)));

    private static int Heavy(GameData d) => d.FindUnit("malazan_heavy_infantry");

    [Fact]
    public void APackUnitsWorth_IsTheItemsCostOverSpawnCount_RemainderDropped()
    {
        UnitDef pack = s_wave.Value.Units[Heavy(s_wave.Value)];
        Assert.Equal((54, 20, 3), (pack.CostGold, pack.CostWood, pack.SpawnCount));
        Assert.Equal((18, 6, 24), (pack.UnitCostGold, pack.UnitCostWood, pack.UnitWorth));
        // A single unit's worth is its cost.
        UnitDef single = TestSim.Data.Units[Heavy(TestSim.Data)];
        Assert.Equal((single.CostGold, single.CostWood), (single.UnitCostGold, single.UnitCostWood));
    }

    /// <summary>Player 0 (AI, Malazan, Normal) and a billet in sight of player 1's hall, so a wave has a target; one tick in.</summary>
    private static Simulation WaveScene(GameData data)
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), data);
        Assert.True(sim.World.Buildings.Spawn(0, data.FindBuilding("malazan_billet"), 31 * AiScenes.FlatSize + 47, out _));
        sim.Tick();
        Assert.True(sim.AiOf(0)!.Board.KnownEnemyBuildings > 0);
        return sim;
    }

    /// <summary>
    /// The wave's <c>minArmyCost</c> (100) counted per dog at the item / 3: four pack units are worth 96 and stay home (the
    /// old count, the item's 74 each, made them 296 and launched); five are worth 120 and go.
    /// </summary>
    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    public void ANormalSeat_LaunchesAtMinArmyCost_CountedPerDogAtTheItemOverThree(int units, bool launches)
    {
        GameData d = s_wave.Value;
        Simulation sim = WaveScene(d);
        AiPlayer ai = sim.AiOf(0)!;
        for (int k = 0; k < units; k++) CombatScenes.Place(sim, 0, Heavy(d), sim.World.NavGrid.CellCenter(18 + k, 28));
        for (int t = 0; t < 5 * ai.ThinkTicks; t++) sim.Tick();
        ai.ArmySize(out _, out int cost);
        Assert.Equal(units * 24, cost);
        Assert.True(units * (d.Units[Heavy(d)].CostGold + d.Units[Heavy(d)].CostWood) >= 100, "the scene: the x3 count would launch");
        Assert.Equal(launches ? 1 : 0, ai.Board.WavesStarted);
    }

    /// <summary>
    /// The census counts a queued pack as its 3 units (<see cref="AiPlayer.Queued"/>), and the build-order step
    /// <c>train malazan_heavy_infantry count 2</c> counts items: two Trains, two packs, six units (counted in units, the
    /// first pack's three would have ended the step after one Train).
    /// </summary>
    [Fact]
    public void AQueuedPack_CountsThree_AndATrainStepsCount_CountsItems()
    {
        GameData d = s_train.Value;
        int heavy = Heavy(d);
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), d);
        World w = sim.World;
        Assert.True(w.Buildings.Spawn(0, d.FindBuilding("malazan_barracks"), 40 * AiScenes.FlatSize + 12, out EntityHandle barracks));
        BuildMaps.SetTotals(sim, 0, 100_000, 100_000);
        List<Command> log = AiScenes.Log(sim, 0);
        AiPlayer ai = sim.AiOf(0)!;
        bool sawQueued = false;
        for (int t = 0; t < 40 * ai.ThinkTicks && !sawQueued; t++)
        {
            sim.Tick();
            // The census is per think: read it on a think tick, against the queue it saw.
            if ((sim.TickNumber - 1) % ai.ThinkTicks == 0 && ai.Queued[heavy] > 0)
            {
                Assert.Equal(3 * w.Buildings.QueueCount[barracks.Index], ai.Queued[heavy]);
                sawQueued = true;
            }
        }
        Assert.True(sawQueued, "the AI never queued the pack");
        for (int t = 0; t < 2000; t++) sim.Tick();
        Assert.Equal(2, log.Count(c => c.Kind == CommandKind.Train && c.TypeId == heavy));
        Assert.Equal((true, 2), (ai.Board.StepDone[0], ai.Board.StepQueued[0]));
        int live = 0;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 0 && w.Units.TypeId[i] == heavy) live++;
        Assert.Equal(6, live);
    }
}
