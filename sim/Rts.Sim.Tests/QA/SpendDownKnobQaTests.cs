using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA, M6-SH1 (session 2026-11-05-2148, BUG-1051 item 6): <c>ai.spendDownQueue</c> is read, not just loaded and hashed.
/// <c>Ai/SpendDownTests</c>' scene (a Hard Malazan seat at <c>high</c>, two finished Barracks, the bank held at 2,000 /
/// 2,000 over seven thinks) fills each Barracks' queue to exactly the knob, at 1, 2 (shipped) and 3. The mutant "the knob
/// ignored (a constant 2)" passes every developer row; it fails these at 1 and 3.
/// </summary>
public class SpendDownKnobQaTests
{
    private static GameData DataWithQueue(int queue) => AiScenes.DataWith(dir =>
    {
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            foreach (JsonNode? o in root["openings"]!.AsArray())
            {
                o!["buildOrder"] = AiScenes.BuildOrder(
                    ("time", 0, "train", "malazan_laborer", 30),
                    ("time", 600, "build", "malazan_billet", 0),
                    ("time", 700, "build", "malazan_billet", 0));
                o["compositions"] = JsonNode.Parse("""[{ "age": 1, "units": { "malazan_heavy_infantry": 1 } }, { "age": 2, "units": { "malazan_heavy_infantry": 1 } }]""");
            }
        });
        dir.EditJson("common/rules.json", r =>
        {
            r["ai"]!["scoutAfter"] = 3600;
            r["ai"]!["spendDownQueue"] = queue;
        });
    });

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AtHigh_EachProducersQueue_FillsToTheKnob(int queue)
    {
        GameData d = DataWithQueue(queue);
        Assert.Equal(queue, d.AiRules.SpendDownQueue);
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Human), d, startingResources: "high");
        int barracks = d.FindBuilding("malazan_barracks");
        Assert.True(sim.World.Buildings.Spawn(0, barracks, 44 * AiScenes.FlatSize + 8, out _));
        Assert.True(sim.World.Buildings.Spawn(0, barracks, 44 * AiScenes.FlatSize + 16, out _));
        AiPlayer ai = sim.AiOf(0)!;
        for (int k = 0; k < 7; k++)
        {
            while (sim.TickNumber % ai.ThinkTicks != 0) sim.Tick();
            BuildMaps.SetTotals(sim, 0, 2000, 2000);
            sim.Tick();
        }
        var b = sim.World.Buildings;
        int seen = 0;
        for (int s = 0; s < b.Capacity; s++)
        {
            if (!b.Alive[s] || b.Owner[s] != 0 || b.TypeId[s] != barracks) continue;
            Assert.Equal(queue, b.QueueCount[s]);
            seen++;
        }
        Assert.Equal(2, seen);
    }
}
