using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Save;

namespace Rts.Sim.Tests;

/// <summary>
/// BUG-1050 (M6-S6 fix round 1, docs/03 "Implementation (M6-S6)"): the AI's spend-down rule. A starting-resources preset's
/// <c>aiSpendAbove</c> (shipped: <c>high</c> 200, <c>standard</c> none) turns it on; while an AI seat's gold + wood left this
/// think is above it, the build order pulls its next time step due early (one a think), production trains on a think the
/// build order sent, at every producer, up to <see cref="ProductionManager.SpendDownQueue"/> in a queue, and the gatherers
/// lean toward the kind the bank holds less of. At <c>standard</c> none of it can happen, whatever the bank. Scene: the flat AI
/// scene, a Hard Malazan seat (0.5 s thinks, no slack) against a human seat, two finished Barracks, the bank set just before a think.
/// </summary>
public class SpendDownTests
{
    private static readonly Lazy<GameData> s_data = new(() => AiScenes.DataWith(dir =>
    {
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            foreach (JsonNode? o in root["openings"]!.AsArray())
            {
                o!["buildOrder"] = AiScenes.BuildOrder(
                    ("time", 0, "train", "malazan_laborer", 30),  // 0: the build order sends at every think
                    ("time", 600, "build", "malazan_billet", 0),  // 1: due at 10:00 by the clock
                    ("time", 700, "build", "malazan_billet", 0)); // 2: due at 11:40 by the clock
                o["compositions"] = JsonNode.Parse("""[{ "age": 1, "units": { "malazan_heavy_infantry": 1 } }, { "age": 2, "units": { "malazan_heavy_infantry": 1 } }]""");
            }
        });
        dir.EditJson("common/rules.json", r => r["ai"]!["scoutAfter"] = 3600); // the scout stays home
    }));

    private static Simulation Scene(string preset)
    {
        GameData d = s_data.Value;
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Human), d, startingResources: preset);
        int barracks = d.FindBuilding("malazan_barracks");
        Assert.True(sim.World.Buildings.Spawn(0, barracks, 44 * AiScenes.FlatSize + 8, out _));
        Assert.True(sim.World.Buildings.Spawn(0, barracks, 44 * AiScenes.FlatSize + 16, out _));
        return sim;
    }

    /// <summary>Runs to the next think, sets player 0's bank just before it, thinks; the commands it sent and what the seam saw at its end.</summary>
    private static List<Command> Think(Simulation sim, List<Command> log, int gold, int wood, out (bool SpendingDown, float Share, int Gold, int Wood) end)
    {
        AiPlayer ai = sim.AiOf(0)!;
        (bool, float, int, int) seen = default;
        ai.ThoughtForTests = a => seen = (a.SpendingDown, a.WantedGoldShare(), a.Gold, a.Wood);
        while (sim.TickNumber % ai.ThinkTicks != 0) sim.Tick();
        BuildMaps.SetTotals(sim, 0, gold, wood);
        int before = log.Count;
        sim.Tick();
        end = seen;
        return log.Skip(before).ToList();
    }

    private static int Trains(List<Command> sent, string unit) =>
        sent.Count(c => c.Kind == CommandKind.Train && c.TypeId == s_data.Value.FindUnit(unit));

    /// <summary>The sim gives each AI seat its preset's <c>aiSpendAbove</c>: 0 at <c>standard</c>, 200 at <c>high</c>; a loaded save rebuilds it from the header's preset.</summary>
    [Theory]
    [InlineData("standard", 0)]
    [InlineData("high", 200)]
    public void EachAiSeat_HasItsPresetsThreshold_AndALoadedSaveToo(string preset, int threshold)
    {
        Simulation sim = Scene(preset);
        Assert.Equal(threshold, sim.AiOf(0)!.SpendAbove);
        for (int t = 0; t < 40; t++) sim.Tick();
        using var stream = new MemoryStream();
        Assert.True(sim.TrySave(stream, out SaveError saved), saved.ToString());
        stream.Position = 0;
        Assert.True(Simulation.TryLoad(s_data.Value, stream, out Simulation? loaded, out SaveError error), error.ToString());
        Assert.Equal(threshold, loaded!.AiOf(0)!.SpendAbove);
    }

    /// <summary>
    /// At <c>standard</c> the AI never spends down, whatever its bank: with 100,000 / 100,000 production still waits on a
    /// think the build order sent (only its Laborer), and no time step comes due early.
    /// </summary>
    [Fact]
    public void AtStandard_ABigBank_ChangesNothing()
    {
        Simulation sim = Scene("standard");
        List<Command> log = AiScenes.Log(sim, 0);
        for (int k = 0; k < 3; k++)
        {
            List<Command> sent = Think(sim, log, 100_000, 100_000, out var end);
            Assert.False(end.SpendingDown);
            Assert.Equal(1, Trains(sent, "malazan_laborer"));
            Assert.Equal(0, Trains(sent, "malazan_heavy_infantry"));
        }
        Blackboard board = sim.AiOf(0)!.Board;
        Assert.Equal((-1, -1), (board.StepDueTick[1], board.StepDueTick[2]));
    }

    /// <summary>
    /// At <c>high</c> with 2,000 / 2,000 production trains on the think the build order sent its Laborer, one Heavy Infantry
    /// at each of the two Barracks; a producer takes a second (the queue's waiting slot) at a later think, never a third.
    /// </summary>
    [Fact]
    public void AtHigh_ProductionTrainsAtEveryProducer_UpToTwoInAQueue()
    {
        Simulation sim = Scene("high");
        List<Command> log = AiScenes.Log(sim, 0);
        List<Command> sent = Think(sim, log, 2000, 2000, out var end);
        Assert.True(end.SpendingDown);
        Assert.Equal(1, Trains(sent, "malazan_laborer"));
        Assert.Equal(2, Trains(sent, "malazan_heavy_infantry"));
        int total = 2;
        for (int k = 0; k < 6; k++) total += Trains(Think(sim, log, 2000, 2000, out _), "malazan_heavy_infantry");
        // Two producers x two: the first units are 14 s from done, so none has left a queue in these 3.5 s.
        Assert.Equal(4, total);
        var b = sim.World.Buildings;
        int barracks = s_data.Value.FindBuilding("malazan_barracks");
        for (int s = 0; s < b.Capacity; s++)
            if (b.Alive[s] && b.Owner[s] == 0 && b.TypeId[s] == barracks) Assert.Equal(ProductionManager.SpendDownQueue, b.QueueCount[s]);
    }

    /// <summary>
    /// At <c>high</c> at or below the threshold the rule is off: with 150 / 50 (200, not above it) production waits on the
    /// think the build order sent, and no time step comes due early.
    /// </summary>
    [Fact]
    public void AtHigh_AtTheThreshold_TheRuleIsOff()
    {
        Simulation sim = Scene("high");
        List<Command> log = AiScenes.Log(sim, 0);
        List<Command> sent = Think(sim, log, 150, 50, out var end);
        Assert.False(end.SpendingDown);
        Assert.Equal(1, Trains(sent, "malazan_laborer"));
        Assert.Equal(0, Trains(sent, "malazan_heavy_infantry"));
        Blackboard board = sim.AiOf(0)!.Board;
        Assert.Equal((-1, -1), (board.StepDueTick[1], board.StepDueTick[2]));
    }

    /// <summary>
    /// At <c>high</c> while spending down, the first time step not yet due comes due at the think (its due tick that think's),
    /// one a think in file order: step 1 at the first think, step 2 at the next. Their clock times (10:00, 11:40) are far off.
    /// </summary>
    [Fact]
    public void AtHigh_TheNextTimeStep_ComesDueEarly_OneAThink()
    {
        Simulation sim = Scene("high");
        List<Command> log = AiScenes.Log(sim, 0);
        Blackboard board = sim.AiOf(0)!.Board;
        Think(sim, log, 2000, 2000, out _);
        int first = sim.TickNumber - 1;
        Assert.Equal((first, -1), (board.StepDueTick[1], board.StepDueTick[2]));
        Think(sim, log, 2000, 2000, out _);
        Assert.Equal(sim.TickNumber - 1, board.StepDueTick[2]);
        Assert.Equal(first, board.StepDueTick[1]); // stays due
    }

    /// <summary>
    /// While spending down with no reserved step short, the gather share moves by <c>ai.reserveShift</c> toward the kind the
    /// bank holds less of (gold short: up; wood short: down); at <c>standard</c> with the same bank it stays <c>goldShare</c>.
    /// </summary>
    [Theory]
    [InlineData("high", 0, 5000, 1)]
    [InlineData("high", 5000, 0, -1)]
    [InlineData("standard", 0, 5000, 0)]
    [InlineData("standard", 5000, 0, 0)]
    public void SpendingDown_TheGatherersLeanTowardTheKindTheBankLacks(string preset, int gold, int wood, int sign)
    {
        Simulation sim = Scene(preset);
        List<Command> log = AiScenes.Log(sim, 0);
        Think(sim, log, gold, wood, out var end);
        Assert.Equal(sign != 0, end.SpendingDown);
        Assert.Equal(gold < wood, end.Gold < end.Wood); // what was spent kept the bank lopsided the same way
        AiRulesDef r = s_data.Value.AiRules;
        Assert.Equal(r.GoldShare + sign * r.ReserveShift, end.Share, 5);
    }
}
