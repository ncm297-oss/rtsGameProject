using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Replays;
using Rts.Sim.Save;
using Rts.Sim.Tests.Content;
using Rts.Sim.Tests.Save;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA re-check of D34 fix round 1 (session 2026-11-18-0926, BUG-1641): the data hash moved D33 -> D34. Beyond the developer's
/// AI-seat save and recording (<c>Content/D34HashMoveTests</c>): (a) <c>main</c>'s golden recording, byte for byte as
/// <c>main</c> has it (only its data-hash and checksum lines differ from today's file), reads, is refused
/// <c>DataMismatch</c> before tick 1 on the shipped data, and plays to the end on D33's data; (b) a save with no AI seat at
/// all (no Teblor step board stored) made on D33's data is refused too, so the refusal is the header hash, not the AI state.
/// </summary>
public class D34HashMoveQaTests
{
    private const string MainChecksum = "807C173DF2B5F439";

    // M6-S13 (sim track, the hash move): D33's data is the D33 fixture on the shipped files with the draw block out.
    private static readonly Lazy<GameData> s_d33 = new(() => Draw.DrawScenes.TeblorOpeningBeforeDraw(TeblorOpeningFixtureTests.D33Fixture()));

    private static byte[] MainGolden()
    {
        string path = Path.Combine(TestDataDir.RepoRoot(), "sim", "Rts.Sim.Tests", "Replays", "cross_map_seed1.replay");
        string[] lines = File.ReadAllText(path).Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("data-hash ", StringComparison.Ordinal)) lines[i] = $"data-hash {D33QaTests.D33Hash:X16}";
            if (lines[i].StartsWith("checksum ", StringComparison.Ordinal)) lines[i] = $"checksum {MainChecksum}";
        }
        return System.Text.Encoding.UTF8.GetBytes(string.Join('\n', lines));
    }

    [Fact]
    public void MainsGoldenRecording_IsRefusedOnTheShippedData_AndPlaysOnD33s()
    {
        Assert.Equal(D33QaTests.D33Hash, s_d33.Value.ContentHash());
        ReplayError read = ReplayFormat.TryRead(MainGolden(), out Replay? replay);
        Assert.Equal(ReplayError.None, read);
        Assert.Equal(D33QaTests.D33Hash, replay!.DataHash);
        ReplayResult refused = ReplayPlayer.Run(replay, TestSim.Data);
        Assert.Equal(ReplayError.DataMismatch, refused.Error);
        Assert.Equal(0, refused.TicksRun);
        ReplayResult ok = ReplayPlayer.Run(replay, s_d33.Value);
        Assert.True(ok.Ok, $"{ok.Error} at {ok.Tick}");
    }

    [Fact]
    public void AHumanOnlySaveFromD33_IsRefusedDataMismatch_NotThrown()
    {
        GameData d = s_d33.Value;
        PlayerSeat[] seats =
        {
            new(d.FindFaction("teblor"), SeatController.Human, AiDifficulty.Normal),
            new(d.FindFaction("malazan"), SeatController.Human, AiDifficulty.Normal),
        };
        Simulation sim = AiScenes.Generated(seats, 2, false, out _, data: d);
        while (sim.TickNumber < 400) sim.Tick();
        byte[] bytes = SaveTestRun.Save(sim);
        SaveError onShipped = SaveError.None;
        Exception? thrown = Record.Exception(() => onShipped = SaveTestRun.TryLoad(TestSim.Data, bytes));
        Assert.Null(thrown);
        Assert.Equal(SaveError.DataMismatch, onShipped);
        Assert.Equal(sim.StateHash(), SaveTestRun.Load(d, bytes).StateHash());
    }
}
