using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M7-9, Request 59): the per-difficulty build order in play. The opening draw of Normal / Hard seats with an entry on
/// only one of a faction's openings (the draw must not move; only the seat that drew that opening plays the entry); an
/// <c>easy</c> entry on an opening Easy never plays; the shortest and the longest lists a file may hold; saves across data
/// with and without the field (refused by the data hash both ways). Every forge looks the opening up by id and replaces
/// whatever the shipped file carries (rule 58).
/// </summary>
public class M79PerDifficultyAiQaTests
{
    private readonly ITestOutputHelper _out;

    public M79PerDifficultyAiQaTests(ITestOutputHelper output) => _out = output;

    private const string Train = """{ "at": { "time": 0 }, "do": "train", "id": "malazan_laborer", "count": 2 }""";
    private const string Barracks = """{ "at": { "time": 0 }, "do": "build", "id": "malazan_barracks" }""";

    private static JsonObject Opening(JsonNode root, string id)
    {
        foreach (JsonNode? o in root["openings"]!.AsArray())
            if ((string?)o!["id"] == id) return o.AsObject();
        throw new InvalidOperationException($"no opening '{id}'");
    }

    /// <summary>Shipped data with every Malazan opening's <c>buildOrders</c> removed, then <paramref name="entries"/> on opening <paramref name="id"/>.</summary>
    private static GameData MalazanWith(string id, string entries) => AiScenes.DataWithAi("malazan", root =>
    {
        foreach (JsonNode? o in root["openings"]!.AsArray()) o!.AsObject().Remove("buildOrders");
        Opening(root, id)["buildOrders"] = JsonNode.Parse(entries);
    });

    /// <summary>Shipped data with every Malazan opening's <c>buildOrders</c> removed (the baseline of the twins).</summary>
    private static readonly Lazy<GameData> s_malazanNone = new(() => AiScenes.DataWithAi("malazan", root =>
    {
        foreach (JsonNode? o in root["openings"]!.AsArray()) o!.AsObject().Remove("buildOrders");
    }));

    private static readonly Lazy<GameData> s_hardOnCrossbow = new(() => MalazanWith("crossbow_push", $$"""{ "hard": [ {{Train}}, {{Barracks}} ] }"""));

    private static Simulation Mirror(GameData data, AiDifficulty a, AiDifficulty b, ulong seed)
    {
        int m = data.FindFaction("malazan");
        return TeblorSwitchOrders.Match(data, "pale_hills", new[] { new PlayerSeat(m, SeatController.Ai, a), new PlayerSeat(m, SeatController.Ai, b) }, seed);
    }

    /// <summary>
    /// A Hard entry on the second Malazan opening only: over 24 seeds the Normal and Hard seats draw the same opening as on
    /// data without the field; the Hard seat plays the entry exactly when it drew that opening, else the drawn opening's
    /// <c>buildOrder</c>; the Normal seat always plays <c>buildOrder</c>. Both outcomes of the draw must occur.
    /// </summary>
    [Fact]
    public void AnEntryOnOneOpening_DoesNotMoveTheDraw_AndOnlyItsDrawerPlaysIt()
    {
        GameData none = s_malazanNone.Value, data = s_hardOnCrossbow.Value;
        AiDef def = data.Ai[data.FindFaction("malazan")];
        int crossbow = def.FindOpening("crossbow_push");
        int drewIt = 0, drewOther = 0;
        for (ulong seed = 1; seed <= 24; seed++)
        {
            Simulation on = Mirror(data, AiDifficulty.Hard, AiDifficulty.Normal, seed), off = Mirror(none, AiDifficulty.Hard, AiDifficulty.Normal, seed);
            for (int p = 0; p < 2; p++)
                Assert.Equal(off.AiOf(p)!.OpeningIndex, on.AiOf(p)!.OpeningIndex);
            AiPlayer hard = on.AiOf(0)!, normal = on.AiOf(1)!;
            AiOpening picked = def.Openings[hard.OpeningIndex];
            if (hard.OpeningIndex == crossbow)
            {
                drewIt++;
                Assert.Equal(2, hard.Steps.Length);
                Assert.True(hard.Steps.SequenceEqual(picked.BuildOrderFor(AiDifficulty.Hard)));
                Assert.Equal(2, hard.Board.StepDone.Length);
            }
            else
            {
                drewOther++;
                Assert.True(hard.Steps.SequenceEqual(picked.BuildOrder));
                Assert.Equal(picked.BuildOrder.Length, hard.Board.StepDone.Length);
            }
            Assert.True(normal.Steps.SequenceEqual(def.Openings[normal.OpeningIndex].BuildOrder));
        }
        _out.WriteLine($"Hard drew crossbow_push on {drewIt} of 24 seeds");
        Assert.True(drewIt > 0 && drewOther > 0, $"the draw never split: {drewIt} / {drewOther}");
    }

    /// <summary>The first seed in 1..24 whose Hard seat (player 0) draws (or does not draw) <c>crossbow_push</c>.</summary>
    private static ulong SeedWhere(bool drawsCrossbow)
    {
        GameData data = s_hardOnCrossbow.Value;
        int crossbow = data.Ai[data.FindFaction("malazan")].FindOpening("crossbow_push");
        for (ulong seed = 1; seed <= 24; seed++)
            if ((Mirror(data, AiDifficulty.Hard, AiDifficulty.Normal, seed).AiOf(0)!.OpeningIndex == crossbow) == drawsCrossbow) return seed;
        throw new InvalidOperationException("no such seed");
    }

    /// <summary>
    /// The twins of the draw: a Hard seat that drew another opening plays tick for tick as on data without the field (3,000
    /// ticks); one that drew the entry's opening parts from it (the entry is played).
    /// </summary>
    [Fact]
    public void ADrawOfAnotherOpening_PlaysAsWithoutTheField_TheEntrysDrawPartsFromIt()
    {
        GameData none = s_malazanNone.Value, data = s_hardOnCrossbow.Value;
        ulong other = SeedWhere(false), it = SeedWhere(true);
        Simulation on = Mirror(data, AiDifficulty.Hard, AiDifficulty.Normal, other), off = Mirror(none, AiDifficulty.Hard, AiDifficulty.Normal, other);
        while (on.TickNumber < 3_000)
        {
            on.Tick();
            off.Tick();
            Assert.True(on.StateHash() == off.StateHash(), $"seed {other}: another opening's draw differs at tick {on.TickNumber}");
        }
        on = Mirror(data, AiDifficulty.Hard, AiDifficulty.Normal, it);
        off = Mirror(none, AiDifficulty.Hard, AiDifficulty.Normal, it);
        int parted = -1;
        while (on.TickNumber < 3_000 && parted < 0)
        {
            on.Tick();
            off.Tick();
            if (on.StateHash() != off.StateHash()) parted = on.TickNumber;
        }
        _out.WriteLine($"seed {other} (another opening) equal 3,000 ticks; seed {it} (the entry's opening) parts at tick {parted}");
        Assert.True(parted > 0, $"seed {it}: the Hard seat on the entry's opening played as without it");
    }

    /// <summary>
    /// An <c>easy</c> entry on an opening other than the first is never played (Easy always plays opening 0): the Easy seat
    /// plays opening 0's <c>buildOrder</c> and is tick-identical to data without the field for 3,000 ticks.
    /// </summary>
    [Fact]
    public void AnEasyEntryOnALaterOpening_IsNeverPlayed()
    {
        GameData data = MalazanWith("cadre_rush", $$"""{ "easy": [ {{Train}} ] }"""), none = s_malazanNone.Value;
        Simulation on = Mirror(data, AiDifficulty.Easy, AiDifficulty.Easy, 2), off = Mirror(none, AiDifficulty.Easy, AiDifficulty.Easy, 2);
        AiDef def = data.Ai[data.FindFaction("malazan")];
        Assert.Equal(0, on.AiOf(0)!.OpeningIndex);
        Assert.True(on.AiOf(0)!.Steps.SequenceEqual(def.Openings[0].BuildOrder));
        while (on.TickNumber < 3_000)
        {
            on.Tick();
            off.Tick();
            Assert.True(on.StateHash() == off.StateHash(), $"differs at tick {on.TickNumber}");
        }
    }

    /// <summary>
    /// The shortest entry (one step) and the longest (256 steps, the limit) for a Hard seat on every Malazan opening: the
    /// seat sizes its arrays from them, plays 4,000 ticks without a fault, its one step gets done, and a save mid-run loads
    /// byte-identical and follows the run for 1,000 ticks.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(256)]
    public void TheShortestAndLongestEntries_PlaySaveAndLoad(int length)
    {
        var steps = new JsonArray();
        for (int k = 0; k < length; k++)
            steps.Add(JsonNode.Parse($$"""{ "at": { "time": {{k * 14}} }, "do": "train", "id": "malazan_laborer" }"""));
        GameData data = AiScenes.DataWithAi("malazan", root =>
        {
            foreach (JsonNode? o in root["openings"]!.AsArray())
            {
                o!.AsObject().Remove("buildOrders");
                o["buildOrders"] = new JsonObject { ["hard"] = steps.DeepClone() };
            }
        });
        Simulation sim = Mirror(data, AiDifficulty.Hard, AiDifficulty.Hard, 4);
        for (int p = 0; p < 2; p++)
        {
            Assert.Equal(length, sim.AiOf(p)!.Steps.Length);
            Assert.Equal(length, sim.AiOf(p)!.Board.StepDone.Length);
        }
        while (sim.TickNumber < 3_000) sim.Tick();
        Assert.True(sim.AiOf(0)!.Board.StepDone[0], "the first step was never done");
        byte[] bytes = SaveTestRun.Save(sim);
        Simulation load = SaveTestRun.Load(data, bytes);
        Assert.Equal(bytes, SaveTestRun.Save(load));
        while (sim.TickNumber < 4_000)
        {
            sim.Tick();
            load.Tick();
            Assert.True(sim.StateHash() == load.StateHash(), $"the load differs at tick {sim.TickNumber}");
        }
        int done = 0;
        foreach (bool d in sim.AiOf(0)!.Board.StepDone) if (d) done++;
        _out.WriteLine($"{length} steps: {done} done by tick 4,000");
    }

    /// <summary>
    /// Saves across the difference: a save made on per-difficulty data is refused <see cref="SaveError.DataMismatch"/> on the
    /// same data without the field, and the other way round; also when the entry equals <c>buildOrder</c> step for step
    /// (ruling (d): the field is there, so the data differs).
    /// </summary>
    [Fact]
    public void ASaveAcrossTheField_IsRefusedByTheDataHash_BothWays()
    {
        GameData none = PerDifficultyOrders.Data(PerDifficultyOrders.A());
        GameData with = PerDifficultyOrders.Data(PerDifficultyOrders.A(), (AiDifficulty.Hard, PerDifficultyOrders.B()));
        GameData equal = PerDifficultyOrders.Data(PerDifficultyOrders.A(), (AiDifficulty.Hard, PerDifficultyOrders.A()));
        Assert.NotEqual(none.ContentHash(), with.ContentHash());
        Assert.NotEqual(none.ContentHash(), equal.ContentHash());
        byte[] Saved(GameData d)
        {
            Simulation sim = TeblorSwitchOrders.Match(d, "pale_hills", TeblorSwitchOrders.Mirror(d, AiDifficulty.Hard), 1);
            while (sim.TickNumber < 1_000) sim.Tick();
            return SaveTestRun.Save(sim);
        }
        byte[] onWith = Saved(with), onNone = Saved(none), onEqual = Saved(equal);
        Assert.Equal(SaveError.None, SaveTestRun.TryLoad(with, onWith));
        Assert.Equal(SaveError.DataMismatch, SaveTestRun.TryLoad(none, onWith));
        Assert.Equal(SaveError.DataMismatch, SaveTestRun.TryLoad(with, onNone));
        Assert.Equal(SaveError.DataMismatch, SaveTestRun.TryLoad(none, onEqual));
        Assert.Equal(SaveError.DataMismatch, SaveTestRun.TryLoad(equal, onNone));
        // The equal entry plays as buildOrder: the two runs are the same game, only the data hash tells them apart.
        Simulation x = TeblorSwitchOrders.Match(none, "pale_hills", TeblorSwitchOrders.Mirror(none, AiDifficulty.Hard), 1);
        Simulation y = TeblorSwitchOrders.Match(equal, "pale_hills", TeblorSwitchOrders.Mirror(equal, AiDifficulty.Hard), 1);
        while (x.TickNumber < 1_000)
        {
            x.Tick();
            y.Tick();
        }
        Assert.Equal(x.StateHash(), y.StateHash());
    }

    /// <summary>
    /// The data track's rehearsal (docs/03 "For the data track (M7-9)", D38's shape): the Teblor <c>standard</c> opening keeps
    /// its <c>buildOrder</c> and gains <c>buildOrders</c> {easy: D36's K9KC10S, hard: D36's QRD1}. It loads; the Easy Teblor
    /// seat (against an Easy Malazan, D34's row set-up) is tick-identical for its first 15 minutes (18,000 ticks) to the run
    /// on data whose <c>buildOrder</c> is K9KC10S; a Hard seat equals QRD1-as-<c>buildOrder</c> and a Normal seat the data
    /// without the field, 6,000 ticks each.
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void TheDataTracksRehearsal_EasyPlaysK9KC10S_HardQrd1_NormalTheBuildOrder()
    {
        var all = global::Rts.Sim.Tests.Content.D36TeblorOrderSearchReportTests.AllVariants();
        JsonNode k9 = all.Single(v => v.Name == "K9KC10S").Opening["buildOrder"]!;
        JsonNode qrd1 = all.Single(v => v.Name == "QRD1").Opening["buildOrder"]!;
        GameData rehearsal = AiScenes.DataWithAi("teblor", root =>
        {
            JsonObject o = TeblorSwitchOrders.Standard(root);
            o.Remove("buildOrders");
            o["buildOrders"] = new JsonObject { ["easy"] = k9.DeepClone(), ["hard"] = qrd1.DeepClone() };
        });
        GameData asOrder(JsonNode order) => AiScenes.DataWithAi("teblor", root =>
        {
            JsonObject o = TeblorSwitchOrders.Standard(root);
            o.Remove("buildOrders");
            AiScenes.SetBuildOrder(o, order.DeepClone());
        });
        GameData none = AiScenes.DataWithAi("teblor", root => TeblorSwitchOrders.Standard(root).Remove("buildOrders"));
        // Not a vacuous twin: both measured orders differ from the opening's buildOrder.
        var shipped = PerDifficultyOrders.Standard(none).BuildOrder;
        Assert.False(PerDifficultyOrders.Standard(rehearsal).BuildOrderFor(AiDifficulty.Easy).SequenceEqual(shipped));
        Assert.False(PerDifficultyOrders.Standard(rehearsal).BuildOrderFor(AiDifficulty.Hard).SequenceEqual(shipped));
        Twin(rehearsal, asOrder(k9), AiDifficulty.Easy, 18_000);
        Twin(rehearsal, asOrder(qrd1), AiDifficulty.Hard, 6_000);
        Twin(rehearsal, none, AiDifficulty.Normal, 6_000);
    }

    private void Twin(GameData a, GameData b, AiDifficulty d, int ticks)
    {
        PlayerSeat[] Seats(GameData g) => new[]
        {
            new PlayerSeat(g.FindFaction("teblor"), SeatController.Ai, d), new PlayerSeat(g.FindFaction("malazan"), SeatController.Ai, d),
        };
        Simulation x = TeblorSwitchOrders.Match(a, "pale_hills", Seats(a), 1), y = TeblorSwitchOrders.Match(b, "pale_hills", Seats(b), 1);
        Assert.True(x.AiOf(0)!.Steps.SequenceEqual(y.AiOf(0)!.Steps), $"{d}: the seats hold different lists");
        while (x.TickNumber < ticks)
        {
            x.Tick();
            y.Tick();
            Assert.True(x.StateHash() == y.StateHash(), $"{d}: differs at tick {x.TickNumber}");
        }
        _out.WriteLine($"{d}: {ticks:N0} ticks equal, {x.AiOf(0)!.Steps.Length} steps, {x.AiOf(0)!.CommandsSent} commands, end {x.StateHash():X16}");
    }
}
