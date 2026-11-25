using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;
using static Rts.Sim.Tests.ReserveSwitchTests;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M7-8 (Request 55): the build-order reserve switches' edges beyond the developer's rows: malformed values, a typo, a
/// step written twice that differs only in a switch, JSON null on the wrong action; both switches on neighbouring steps;
/// a reopened (<c>ReopenLost</c>) noReserve step; and a match where the reserve gate is shut (<c>MayReserve</c> false), in
/// which switches on every step must change nothing at all.
/// </summary>
public class ReserveSwitchQaTests
{
    private readonly ITestOutputHelper _out;

    public ReserveSwitchQaTests(ITestOutputHelper output) => _out = output;

    private const string MalazanAi = "factions/malazan/ai.json";

    private static DataLoadResult LoadWith(Action<JsonNode> edit)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(MalazanAi, edit);
        return DataLoader.LoadAll(dir.Path);
    }

    private static JsonObject FirstStepOf(JsonNode root, string what, out int i, out int j)
    {
        (i, j) = ReserveSwitchLoaderTests.FirstStep(root, what);
        return root["openings"]![i]!["buildOrder"]![j]!.AsObject();
    }

    /// <summary>A switch written as a string, a number or an object is refused with an error (no exception, no silent default).</summary>
    [Theory]
    [InlineData("train", "throughReserve", "\"yes\"")]
    [InlineData("train", "throughReserve", "1")]
    [InlineData("build", "noReserve", "\"true\"")]
    [InlineData("expand", "noReserve", "{}")]
    public void ASwitch_OfTheWrongJsonType_IsAnError(string what, string field, string json)
    {
        DataLoadResult r = LoadWith(root => FirstStepOf(root, what, out _, out _)[field] = JsonNode.Parse(json));
        _out.WriteLine(string.Join("\n", r.Errors));
        Assert.False(r.Ok);
        Assert.NotEmpty(r.Errors);
        Assert.All(r.Errors, e => Assert.Equal(MalazanAi, e.File));
    }

    /// <summary>A misspelt switch is an unknown field, an error, not a silent "off".</summary>
    [Theory]
    [InlineData("train", "throughreserve")]
    [InlineData("build", "noReserves")]
    public void AMisspeltSwitch_IsAnError(string what, string field)
    {
        DataLoadResult r = LoadWith(root => FirstStepOf(root, what, out _, out _)[field] = true);
        _out.WriteLine(string.Join("\n", r.Errors));
        Assert.False(r.Ok);
    }

    /// <summary>JSON null on the wrong action reads as absent (docs/03: "left out or JSON null is off"): it loads, the shipped hash.</summary>
    [Theory]
    [InlineData("build", "throughReserve")]
    [InlineData("train", "noReserve")]
    public void ANullSwitch_OnTheWrongAction_ReadsAsAbsent(string what, string field)
    {
        DataLoadResult r = LoadWith(root => FirstStepOf(root, what, out _, out _)[field] = null);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Assert.Equal(TestSim.Data.ContentHash(), r.Data!.ContentHash());
    }

    /// <summary>
    /// Two steps that differ only in a switch are still one step written twice (a switch is not part of the key): one error,
    /// at the second, as for any duplicate.
    /// </summary>
    [Fact]
    public void TwoStepsDifferingOnlyInASwitch_AreAStepWrittenTwice()
    {
        int i = -1, j = -1;
        DataLoadResult r = LoadWith(root =>
        {
            JsonObject step = FirstStepOf(root, "train", out i, out j);
            JsonObject twin = step.DeepClone().AsObject();
            twin["throughReserve"] = true;
            root["openings"]![i]!["buildOrder"]!.AsArray().Insert(j + 1, twin);
        });
        DataError e = Assert.Single(r.Errors);
        _out.WriteLine(e.ToString());
        Assert.Equal($"openings[{i}].buildOrder[{j + 1}]", e.Path);
        Assert.Contains("the same step as", e.Message);
    }

    /// <summary>
    /// Both switches on neighbouring steps, then the reserve's owner: a noReserve Age II and a noReserve Armory, then a
    /// throughReserve Crossbowman. At 60 / 60 nothing is reserved (both reservable steps opted out) and the Crossbowman
    /// (36 / 45) is sent from the whole bank.
    /// </summary>
    [Fact]
    public void TwoNoReserveSteps_ThenAThroughReserveTrainStep_NothingReserved_TheTrainIsSent()
    {
        GameData d = ReserveSwitchTests.Data(false, Step("time", 0, "research", "age_ii", noReserve: true), Step("time", 0, "build", "malazan_armory", noReserve: true),
            Step("time", 0, "train", "malazan_crossbowman", 1, through: true));
        Simulation sim = Scene(d);
        List<Command> log = AiScenes.Log(sim, 0);
        (List<Command> sent, Seen seen) = Think(sim, log, 60, 60);
        Assert.Equal((-1, 0, 0), (seen.Step, seen.Gold, seen.Wood));
        Assert.Contains(sent, c => c.Kind == CommandKind.Train && c.TypeId == d.FindUnit("malazan_crossbowman"));
        Assert.DoesNotContain(sent, c => c.Kind is CommandKind.Build or CommandKind.Research);
    }

    /// <summary>
    /// A throughReserve train step right after the reserved step, with a noReserve step before both: a noReserve Armory, a
    /// reserved Age II (400 / 200), a throughReserve Crossbowman. At 90 / 90 the reserved step is Age II's, the Crossbowman is
    /// sent through it, and production's Heavy Infantry stays above the reserve (not sent).
    /// </summary>
    [Fact]
    public void ANoReserveStep_AReservedStep_AndAThroughTrainStep_Together()
    {
        GameData d = ReserveSwitchTests.Data(false, Step("time", 0, "build", "malazan_armory", noReserve: true), Step("time", 0, "research", "age_ii"),
            Step("time", 0, "train", "malazan_crossbowman", 3, through: true));
        Simulation sim = Scene(d);
        List<Command> log = AiScenes.Log(sim, 0);
        (List<Command> sent, Seen seen) = Think(sim, log, 90, 90);
        Assert.Equal((1, 400, 200), (seen.Step, seen.Gold, seen.Wood));
        Assert.False(seen.InfantryAboveReserve);
        Assert.Contains(sent, c => c.Kind == CommandKind.Train && c.TypeId == d.FindUnit("malazan_crossbowman"));
        Assert.DoesNotContain(sent, c => c.Kind == CommandKind.Train && c.TypeId == d.FindUnit("malazan_heavy_infantry"));
    }

    /// <summary>
    /// A reopened step keeps its switch: a noReserve Armory built, then razed. <c>ReopenLost</c> makes the step due again;
    /// short of its cost it is not the reserved step (the twin without the switch reserves it).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AReopenedNoReserveStep_IsNotReserved(bool noReserve)
    {
        GameData d = ReserveSwitchTests.Data(false, Step("time", 0, "build", "malazan_armory", noReserve: noReserve ? true : null));
        Simulation sim = Scene(d);
        AiPlayer ai = sim.AiOf(0)!;
        List<Command> log = AiScenes.Log(sim, 0);
        int armory = d.FindBuilding("malazan_armory");
        EntityHandle site = default;
        for (int k = 0; k < 60 && site.Generation == 0; k++)
        {
            Think(sim, log, 300, 300);
            for (int s = 0; s < sim.World.Buildings.Capacity; s++)
                if (sim.World.Buildings.Alive[s] && sim.World.Buildings.Owner[s] == 0 && sim.World.Buildings.TypeId[s] == armory)
                    site = new EntityHandle(s, sim.World.Buildings.Generation[s]);
        }
        Assert.True(site.Generation != 0, "the Armory was never placed");
        for (int k = 0; k < 3; k++) Think(sim, log, 300, 300);
        Assert.True(ai.Board.StepDone[0]);
        Assert.True(sim.World.Buildings.Free(site));
        Seen seen = Think(sim, log, 60, 60).Seen;
        seen = Think(sim, log, 60, 60).Seen;
        Assert.False(ai.Board.StepDone[0], "the step was not reopened");
        Assert.Equal(noReserve ? -1 : 0, seen.Step);
    }

    /// <summary>
    /// The gate shut: every opening of every faction with its wave's cost out of reach (<c>attack.minArmyCost</c> 1,000,000 and
    /// <c>reserveArmyShare</c> 1), so <c>MayReserve</c> is false at every think and no step is ever reserved. With no reserve,
    /// a switch has nothing to act on: the same data with every switch written <c>true</c> on every step that may carry one
    /// plays a Hard Teblor v Normal Whirlwind match on Pale Hills to the same state hash at every 500 ticks for 6,000 ticks.
    /// </summary>
    [Fact]
    public void WithTheReserveGateShut_EverySwitchOn_PlaysTheSameMatch()
    {
        static void Shut(TestDataDir dir, bool switches)
        {
            foreach (string f in TestSim.ShippedFactions)
                dir.EditJson($"factions/{f}/ai.json", root =>
                {
                    foreach (JsonNode? o in root["openings"]!.AsArray())
                    {
                        if (o!["attack"] == null) o["attack"] = new JsonObject { ["minArmyPop"] = 8 };
                        JsonObject attack = o["attack"]!.AsObject();
                        attack["minArmyCost"] = 1_000_000;
                        attack["reserveArmyShare"] = 1;
                        if (!switches) continue;
                        foreach (JsonNode? s in o["buildOrder"]!.AsArray())
                            s![ReserveSwitchLoaderTests.SwitchFor((string)s["do"]!)] = true;
                    }
                });
        }
        GameData off = AiScenes.DataWith(dir => Shut(dir, false)), on = AiScenes.DataWith(dir => Shut(dir, true));
        Assert.NotEqual(off.ContentHash(), on.ContentHash());
        var seats = new[]
        {
            new PlayerSeat(TestSim.Data.FindFaction("teblor"), SeatController.Ai, AiDifficulty.Hard),
            new PlayerSeat(TestSim.Data.FindFaction("whirlwind"), SeatController.Ai, AiDifficulty.Normal),
        };
        Simulation a = TeblorSwitchOrders.Match(off, "pale_hills", seats, 5UL), b = TeblorSwitchOrders.Match(on, "pale_hills", seats, 5UL);
        int reserved = 0;
        for (int p = 0; p < 2; p++)
        {
            a.AiOf(p)!.ThoughtForTests = ai => { if (ai.ReserveStep >= 0) reserved++; };
            b.AiOf(p)!.ThoughtForTests = ai => { if (ai.ReserveStep >= 0) reserved++; };
        }
        while (a.TickNumber < 6_000)
        {
            a.Tick();
            b.Tick();
            if (a.TickNumber % 500 == 0) Assert.True(a.StateHash() == b.StateHash(), $"differs at tick {a.TickNumber}");
        }
        _out.WriteLine($"6,000 ticks equal; end hash {a.StateHash():X16}; reserved thinks {reserved}");
        Assert.Equal(0, reserved);
    }
}
