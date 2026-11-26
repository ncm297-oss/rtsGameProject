using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Replays;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Draw;

/// <summary>
/// M6-S13 criteria 6 and 8 (the default-loop half; the AI matches that draw are <c>Stress/DrawStalledRowsReportTests</c>):
/// the rule off is the base (no clock, no draw, for 144,000 ticks of a spent match), a never-spent match hashes the same with
/// and without the rule ("zero is free", every tick), a twin whose wait is out of reach is hash-equal to the drawing run up to
/// its draw tick, and the counters the check reads always equal a recount from the stores.
/// </summary>
public class DrawTwinTests
{
    private readonly ITestOutputHelper _out;

    public DrawTwinTests(ITestOutputHelper output) => _out = output;

    /// <summary>6(a): a spent match on a copy of the shipped data with <c>draw</c> removed: the clock stays 0, never drawn, for 144,000 ticks.</summary>
    [Fact]
    public void TheRuleOff_ASpentMatch_NeverDraws_In144000Ticks()
    {
        Simulation sim = DrawScenes.Spent(DrawScenes.Off);
        World w = sim.World;
        Assert.Equal(0, w.DrawWaitTicks);
        Assert.True(w.MatchSpent, "spent is still read without the rule");
        for (int t = 0; t < 144_000; t++)
        {
            sim.Tick();
            if (w.DrawClock != 0 || sim.MatchOver) Assert.Fail($"tick {sim.TickNumber}: clock {w.DrawClock}, over {sim.MatchOver}");
        }
        Assert.False(w.MatchDrawn);
        Assert.Equal(-1, w.DrawTicksLeft);
    }

    /// <summary>"Zero is free": a Normal v Normal AI match that is never spent hashes the same every tick on the shipped data and on a copy without <c>draw</c>.</summary>
    [Fact]
    public void ANeverSpentAiMatch_HashesTheSame_WithAndWithoutTheRule_EveryTick()
    {
        PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Ai, AiDifficulty.Normal) };
        Simulation on = AiScenes.Generated(seats, 3, false, out _);
        Simulation off = AiScenes.Generated(seats, 3, false, out _, data: DrawScenes.Off);
        for (int t = 0; t < 6000; t++)
        {
            on.Tick();
            off.Tick();
            Assert.False(on.World.DrawStateSet, $"tick {on.TickNumber}: the clock ran");
            Assert.True(on.StateHash() == off.StateHash(), $"tick {on.TickNumber}: the hashes differ");
        }
    }

    public static TheoryData<string> DrawingScenes() => new() { "two seats", "three seats", "four seats 2v2", "by commands" };

    private static Simulation Scene(string name, GameData data) => name switch
    {
        "two seats" => DrawScenes.Spent(data),
        "three seats" => DrawScenes.Spent(data, players: 3, soldiers: 3),
        "four seats 2v2" => DrawScenes.Spent(data, players: 4, teams: new[] { 1, 2, 1, 2 }),
        _ => DrawScenes.SpentByCommands(data, 5, 2, 3, false, out _),
    };

    /// <summary>
    /// 6(b): the same scene on the short-wait data and on a twin with the wait out of reach: hash-equal at every tick before the
    /// draw (the clock runs alike in both), different at the draw tick (the drawn flag), and the twin never draws.
    /// </summary>
    [Theory]
    [MemberData(nameof(DrawingScenes))]
    public void ATwinWithTheWaitOutOfReach_IsHashEqual_UpToTheDrawTick(string name)
    {
        GameData shortData = name == "by commands" ? DrawScenes.ShortBroke : DrawScenes.Short;
        GameData farData = name == "by commands" ? DrawScenes.DataWith(DataLimits.MaxDrawSeconds, DrawScenes.AddBrokePreset) : DrawScenes.OutOfReach;
        Simulation a = Scene(name, shortData), b = Scene(name, farData);
        int compared = 0;
        while (!a.MatchOver && a.TickNumber < 20 * DrawScenes.ShortTicks)
        {
            a.Tick();
            b.Tick();
            if (a.World.MatchDrawn) break;
            Assert.True(a.StateHash() == b.StateHash(), $"{name}: tick {a.TickNumber}: the twins differ before the draw");
            Assert.Equal(a.World.DrawClock, b.World.DrawClock);
            compared++;
        }
        Assert.True(a.World.MatchDrawn, $"{name}: no draw by {a.TickNumber}");
        Assert.NotEqual(a.StateHash(), b.StateHash());
        Assert.False(b.MatchOver);
        Assert.Equal(DrawScenes.ShortTicks, b.World.DrawClock);
        _out.WriteLine($"{name}: drawn at tick {a.TickNumber}, {compared} ticks hash-equal before it");
    }

    /// <summary>
    /// The counts the per-tick check reads (live workers, unit items queued, gold carried, gold left on the map) kept by the stores'
    /// own paths equal a recount at every 50th tick of an AI match (gathering, training, cancelling, deaths), and the check's answer
    /// is the one a recount gives.
    /// </summary>
    [Fact]
    public void TheCounters_EqualARecount_ThroughAnAiMatch()
    {
        PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Ai, AiDifficulty.Hard) };
        Simulation sim = AiScenes.Generated(seats, 2, false, out _);
        World w = sim.World;
        int checks = 0;
        long carriedSeen = 0;
        for (int t = 0; t < 24_000 && !sim.MatchOver; t++)
        {
            sim.Tick();
            if (t % 50 != 0) continue;
            int n = w.Config.PlayerCount;
            int[] workers = (int[])w.Ledger.LiveWorkers.Clone(), queued = (int[])w.Ledger.QueuedUnits.Clone();
            long[] carried = (long[])w.Ledger.CarriedGold.Clone();
            long goldLeft = 0;
            for (int i = 0; i < w.Resources.Capacity; i++)
                if (w.Resources.Alive[i] && w.Data.Resources[w.Resources.TypeId[i]].Resource == ResourceKind.Gold) goldLeft += w.Resources.Remaining[i];
            bool[] can = new bool[n];
            for (int p = 0; p < n; p++) can[p] = w.CanStillTrain(p);
            w.RecountDraw();
            for (int p = 0; p < n; p++)
            {
                Assert.True(workers[p] == w.Ledger.LiveWorkers[p], $"tick {sim.TickNumber} p{p}: workers {workers[p]} counted, {w.Ledger.LiveWorkers[p]} recounted");
                Assert.True(queued[p] == w.Ledger.QueuedUnits[p], $"tick {sim.TickNumber} p{p}: queued {queued[p]} counted, {w.Ledger.QueuedUnits[p]} recounted");
                Assert.True(carried[p] == w.Ledger.CarriedGold[p], $"tick {sim.TickNumber} p{p}: carried {carried[p]} counted, {w.Ledger.CarriedGold[p]} recounted");
                Assert.Equal(can[p], w.CanStillTrain(p));
                carriedSeen += carried[p];
            }
            Assert.Equal(goldLeft, w.Resources.GoldLeft);
            checks++;
        }
        Assert.True(carriedSeen > 0, "no gold was ever carried at a check");
        _out.WriteLine($"{checks} checks to tick {sim.TickNumber}");
    }
}
