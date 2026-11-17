using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Replays;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// M6-SH2 criterion 7, the retrain path (BUG-1465 (a)) and the drop-off rule (BUG-1520) in a whole match: Raraku seed 1,
/// Normal v Normal (the match BUG-1465 was found on; seat 0 loses every worker and retrains). Twins hash alike on every tick to
/// the first retrain plus 2,000 ticks; a save taken the tick the first retrain is sent loads, saves byte-identical and follows
/// the run hash for hash for 2,000 ticks; the recording replays to its end with every checkpoint equal.
/// </summary>
[Trait("Category", "Stress")]
public class M6Sh2DeterminismStressTests(ITestOutputHelper output)
{
    private const int After = 2000, Limit = 45_000;

    [Fact]
    public void RarakuSeed1_TheRetrain_TwinsSaveAndReplay()
    {
        Simulation a = MapFileTests.MapMatch("raraku", 1, true, out ReplayRecorder? recorder);
        Simulation b = MapFileTests.MapMatch("raraku", 1, false, out _);
        int retrainAt = -1;
        for (int p = 0; p < 2; p++)
        {
            AiPlayer ai = a.AiOf(p)!;
            ai.SentForTests = c =>
            {
                if (retrainAt < 0 && c.Kind == CommandKind.Train && ai.WorkerCount == 0 && ai.IsWorkerType[c.TypeId]) retrainAt = c.Tick;
            };
        }
        byte[]? save = null;
        Simulation? loaded = null;
        while (a.TickNumber < Limit && !a.MatchOver && (retrainAt < 0 || a.TickNumber < retrainAt + After))
        {
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"twins differ after tick {a.TickNumber}");
            if (loaded != null)
            {
                loaded.Tick();
                Assert.True(a.StateHash() == loaded.StateHash(), $"the loaded save diverged at tick {a.TickNumber}");
            }
            else if (retrainAt >= 0 && a.TickNumber >= retrainAt - 1 && a.PendingCommandCount > 0)
            {
                save = SaveTestRun.Save(a);
                loaded = SaveTestRun.Load(TestSim.Data, save);
                Assert.Equal(save, SaveTestRun.Save(loaded));
                SaveTestRun.AssertSameState(a, loaded, "at the save");
            }
        }
        output.WriteLine($"first retrain sent for tick {retrainAt}; save at {(save == null ? "none" : "the same tick")}; ran to {a.TickNumber}, over {a.MatchOver}");
        Assert.True(retrainAt >= 0, "no seat retrained a worker: the row proves nothing");
        Assert.NotNull(loaded);
        Replay recorded = recorder!.ToReplay();
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(recorded), out Replay? parsed));
        ReplayResult played = ReplayPlayer.Run(parsed!, TestSim.Data);
        Assert.True(played.Ok, played.ToString());
        Assert.Equal(recorded.TickCount, played.TicksRun);
    }
}
