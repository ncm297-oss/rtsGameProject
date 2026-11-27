using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M8-4 criterion 10 (Request 56 G4): the computer player and a unit type's <c>maxAlive</c>, on test data only (no shipped
/// unit has one, so no shipped match moves). The copy: Malazan Heavy Infantry limited to 7; every Malazan opening's
/// compositions half Heavy Infantry, half Crossbowmen in both ages; and every opening's build order (written with
/// <see cref="AiScenes.SetBuildOrder"/>) ten Laborers, a Billet, a Barracks, a Crossbow Range, then a <c>train</c> step of
/// <b>9</b> Heavy Infantry and, behind it, one of 2 Crossbowmen (more Billets at 14 / 20 / 26 pop). A Malazan AI seat (player
/// 0) against an idle Whirlwind human seat on the flat scene, combat off (nothing dies), 12,000 ticks, each difficulty: the
/// seat reaches the limit and goes on training Crossbowmen (it does not sit on its gold for the capped type); the train
/// step of 9 is done at the limit having had at most 7 accepted, and the step behind it runs; no Train the seat sends is
/// refused for the limit; the kept count never passes 7 and equals the recount at every check.
/// </summary>
public class UnitLimitAiTests
{
    private readonly ITestOutputHelper _out;

    public UnitLimitAiTests(ITestOutputHelper output) => _out = output;

    internal const int HeavyStep = 4, CrossbowStep = 5;

    internal static readonly Lazy<GameData> s_data = new(() => Forge(Order()));

    /// <summary>
    /// The spend-down variant: the same with a second Barracks step (9 pop) and a second Crossbow Range step (10 pop), for the
    /// "high" starting resources, where production sends one Train per producer in a think: the seat's earlier Trains of the
    /// think must count against the limit before they apply.
    /// </summary>
    private static readonly Lazy<GameData> s_twoBarracks = new(() =>
    {
        JsonArray order = Order();
        order.Add(AiScenes.BuildOrder(("pop", 9, "build", "malazan_barracks", 0))[0]!.DeepClone());
        order.Add(AiScenes.BuildOrder(("pop", 10, "build", "malazan_crossbow_range", 0))[0]!.DeepClone());
        return Forge(order);
    });

    private static GameData Forge(JsonArray order) => AiScenes.DataWith(dir =>
    {
        MaxAliveLoaderTests.Strip(dir);
        dir.SetUnitField("malazan", MaxAliveLoaderTests.Heavy, "maxAlive", "7");
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            foreach (JsonNode? o in root["openings"]!.AsArray())
            {
                AiScenes.SetBuildOrder(o, order.DeepClone());
                var mix = new JsonObject { ["malazan_heavy_infantry"] = 50, ["malazan_crossbowman"] = 50 };
                o!["compositions"] = new JsonArray(
                    new JsonObject { ["age"] = 1, ["units"] = mix.DeepClone() },
                    new JsonObject { ["age"] = 2, ["units"] = mix.DeepClone() });
            }
        });
    });

    /// <summary>The test build order (step <see cref="HeavyStep"/> trains 9 Heavy Infantry, step <see cref="CrossbowStep"/> 2 Crossbowmen).</summary>
    internal static JsonArray Order() => AiScenes.BuildOrder(
        ("time", 0, "train", "malazan_laborer", 10),
        ("pop", 6, "build", "malazan_billet", 0),
        ("pop", 7, "build", "malazan_barracks", 0),
        ("pop", 8, "build", "malazan_crossbow_range", 0),
        ("time", 0, "train", "malazan_heavy_infantry", 9),
        ("time", 0, "train", "malazan_crossbowman", 2),
        ("pop", 14, "build", "malazan_billet", 0),
        ("pop", 20, "build", "malazan_billet", 0),
        ("pop", 26, "build", "malazan_billet", 0));

    internal sealed record Outcome(int ReachedAt, int CrossbowsAtLimit, int CrossbowsAtEnd, bool HeavyStepDone, int HeavyStepQueued, bool CrossbowStepDone,
        int LimitRefusals, int HeavyTrainsSent, int MaxCount, ulong EndHash);

    /// <summary>Plays the scene (see the type's summary) to <paramref name="ticks"/> at <paramref name="difficulty"/>, checking the count every 20 ticks.</summary>
    internal static Outcome Play(AiDifficulty difficulty, int ticks = 12_000, ulong seed = 3, bool high = false)
    {
        GameData d = high ? s_twoBarracks.Value : s_data.Value;
        Simulation sim = AiScenes.Flat(AiScenes.Seats(difficulty, SeatController.Ai, SeatController.Human), d, seed: seed,
            startingResources: high ? "high" : StartingResourcesDef.StandardId);
        World w = sim.World;
        int heavy = d.FindUnit(MaxAliveLoaderTests.Heavy), crossbow = d.FindUnit("malazan_crossbowman");
        int limitRefusals = 0;
        sim.RefusedForTests = (c, why, detail) => { if (why == CommandRefusal.TrainRefused && (TrainError)detail == TrainError.LimitReached) limitRefusals++; };
        List<Command> log = AiScenes.Log(sim, 0);
        AiPlayer ai = sim.AiOf(0)!;
        Assert.Equal(high ? 11 : 9, ai.Steps.Length);
        Assert.Equal((AiStepAction.Train, heavy, 9), (ai.Steps[HeavyStep].Action, ai.Steps[HeavyStep].Target, ai.Steps[HeavyStep].Count));
        int reachedAt = -1, crossAtLimit = -1, max = 0;
        while (sim.TickNumber < ticks)
        {
            sim.Tick();
            if (sim.TickNumber % 20 != 0) continue;
            int count = UnitLimitTests.Count(w, 0, heavy);
            max = Math.Max(max, count);
            Assert.True(count <= 7, $"{difficulty}: count {count} past the limit at tick {sim.TickNumber}");
            if (reachedAt < 0 && count == 7)
            {
                reachedAt = sim.TickNumber;
                crossAtLimit = Live(w, crossbow);
            }
        }
        int sent = 0;
        foreach (Command c in log) if (c.Kind == CommandKind.Train && c.TypeId == heavy) sent++;
        return new Outcome(reachedAt, crossAtLimit, Live(w, crossbow), ai.Board.StepDone[HeavyStep], ai.Board.StepQueued[HeavyStep], ai.Board.StepDone[CrossbowStep],
            limitRefusals, sent, max, sim.StateHash());
    }

    /// <summary>Player 0's live units of <paramref name="type"/> plus its queued ones.</summary>
    private static int Live(World w, int type) => UnitLimitTests.Recount(w, 0, type);

    [Theory]
    [InlineData(AiDifficulty.Easy, false)]
    [InlineData(AiDifficulty.Normal, false)]
    [InlineData(AiDifficulty.Hard, false)]
    [InlineData(AiDifficulty.Normal, true)]
    [InlineData(AiDifficulty.Hard, true)]
    public void ASeat_ReachesTheLimit_TrainsOtherTypes_AndSendsNoTrainRefusedForIt(AiDifficulty difficulty, bool high)
    {
        Outcome o = Play(difficulty, high: high);
        _out.WriteLine($"{difficulty}{(high ? " (high start, two Barracks)" : "")}: {o}");
        Assert.True(o.ReachedAt > 0, $"{difficulty}: the seat never reached 7 Heavy Infantry in 12,000 ticks");
        Assert.Equal(7, o.MaxCount);
        Assert.True(o.CrossbowsAtEnd > o.CrossbowsAtLimit, $"{difficulty}: no Crossbowman trained after the limit ({o.CrossbowsAtLimit} -> {o.CrossbowsAtEnd})");
        Assert.True(o.HeavyStepDone, $"{difficulty}: the train step of 9 is not done");
        Assert.InRange(o.HeavyStepQueued, 0, 7);
        Assert.True(o.CrossbowStepDone, $"{difficulty}: the step behind the capped one did not run");
        Assert.Equal(0, o.LimitRefusals);
        Assert.True(o.HeavyTrainsSent <= 7, $"{difficulty}: {o.HeavyTrainsSent} Heavy Infantry Trains sent with nothing dying");
    }
}
