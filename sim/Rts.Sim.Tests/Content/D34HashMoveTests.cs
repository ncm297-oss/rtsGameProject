using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Replays;
using Rts.Sim.Save;
using Rts.Sim.Tests.QA.Content;
using Rts.Sim.Tests.Save;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D34 fix round 1 (BUG-1641): the Teblor <c>standard</c> opening moved (QA's order Q2 and the softened description), so the
/// shipped data hash moved 318E8C4256456C05 -> A732F387C2BB7E4D. A save and a recording made on D33's data with a Teblor AI
/// seat (its step board is the one stored) are refused <c>DataMismatch</c> on the shipped data, never thrown, and still load
/// and play on D33's data rebuilt from its fixture (the refusal is the hash, not a broken file).
/// </summary>
public class D34HashMoveTests
{
    private static readonly Lazy<GameData> s_d33 = new(() => TeblorOpeningVariantsReportTests.DataWith(TeblorOpeningFixtureTests.D33Fixture()));

    private static Simulation D33AiMatch(int ticks, bool record, out ReplayRecorder? rec)
    {
        GameData d = s_d33.Value;
        Assert.Equal(D33QaTests.D33Hash, d.ContentHash());
        PlayerSeat[] seats =
        {
            new(d.FindFaction("teblor"), SeatController.Ai, AiDifficulty.Normal),
            new(d.FindFaction("malazan"), SeatController.Ai, AiDifficulty.Normal),
        };
        Simulation sim = AiScenes.Generated(seats, 1, record, out rec, data: d);
        Assert.Equal("standard", sim.AiOf(0)!.Opening.Id);
        while (sim.TickNumber < ticks) sim.Tick();
        return sim;
    }

    [Fact]
    public void TheShippedHash_IsD34s_AndNotD33s()
    {
        Assert.Equal(D34QaTests.D34Hash, TestSim.Data.ContentHash());
        Assert.NotEqual(D33QaTests.D33Hash, TestSim.Data.ContentHash());
    }

    [Fact]
    public void AnAiSeatSaveFromD33_IsRefusedDataMismatch_NotThrown_AndLoadsOnD33sData()
    {
        // 10:30: past the 10:00 steps, so the stored step board is D33's order at work.
        Simulation sim = D33AiMatch(12_600, false, out _);
        byte[] bytes = SaveTestRun.Save(sim);
        Assert.Equal(D33QaTests.D33Hash, BitConverter.ToUInt64(bytes, SaveBytes.HeaderOffsets(bytes).DataHash));
        SaveError onShipped = SaveError.None;
        Exception? thrown = Record.Exception(() => onShipped = SaveTestRun.TryLoad(TestSim.Data, bytes));
        Assert.Null(thrown);
        Assert.Equal(SaveError.DataMismatch, onShipped);
        Simulation back = SaveTestRun.Load(s_d33.Value, bytes);
        Assert.Equal(sim.StateHash(), back.StateHash());
    }

    [Fact]
    public void AnAiSeatRecordingFromD33_IsRefusedBeforeTick1_AndPlaysOnD33sData()
    {
        D33AiMatch(1_200, true, out ReplayRecorder? rec);
        Replay replay = rec!.ToReplay();
        Assert.Equal(D33QaTests.D33Hash, replay.DataHash);
        ReplayResult refused = ReplayPlayer.Run(replay, TestSim.Data);
        Assert.Equal(ReplayError.DataMismatch, refused.Error);
        Assert.Equal(0, refused.TicksRun);
        ReplayResult ok = ReplayPlayer.Run(replay, s_d33.Value);
        Assert.True(ok.Ok, $"{ok.Error} at {ok.Tick}");
    }
}
