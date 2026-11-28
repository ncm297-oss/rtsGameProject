using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Replays;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// M6-SH2 criterion 7, the retrain path (BUG-1465 (a)) and the drop-off rule (BUG-1520) in a whole match: Raraku Normal v
/// Normal, a seed on which a seat loses every worker and retrains. Twins hash alike on every tick to the first retrain plus
/// 2,000 ticks; a save taken the tick the first retrain is sent loads, saves byte-identical and follows the run hash for hash
/// for 2,000 ticks; the recording replays to its end with every checkpoint equal.
/// M6-S12 fix round 1 (BUG-1559, a re-stage, the checks unchanged): the row played seed 1, the match BUG-1465 was found on,
/// while it stalled (BUG-1460). Under M6-S12 seed 1 ends at 24,354 (seat 0); the loser, seat 1, loses its last worker at
/// 21,920 with its hall standing but 3 gold, short of a worker, so it never retrains (the retrain rule as built) and the row
/// proved nothing. It plays <see cref="Seed"/> 3, the lowest Raraku seed whose loser retrains (seat 0 has no worker at 24,260
/// with 1,087 gold and sends a worker Train for 24,261; over at 27,394). Was seed 1.
/// D33 (the data track's balance pass, handed this class by name: the Malazan <c>cadre_rush</c> waves at 16 army pop, the
/// Whirlwind <c>holy_war</c> at 14; a re-stage, the checks unchanged): seed 3 now ends at 28,915 with no seat retraining, so
/// the row proved nothing ("no seat retrained a worker"). It plays seed 5, the lowest Raraku seed whose loser retrains
/// (seat 0 sends a worker Train for 22,761; over at 27,212; seeds 1, 2, 4, 6, 7 and 8 retrain nowhere:
/// <c>Content/D33BalanceReportTests.RarakuRetrainSeeds_Report</c>). Was seed 3.
/// M6-S14 (BUG-1660, the worker floor): the retrain path is the floor's (<c>ai.minWorkers</c>), so the row watches for the first
/// worker Train sent with fewer live workers than the floor (with the floor at 4 no seat on seed 5 fell to zero workers:
/// "no seat retrained a worker"); seed and checks unchanged.
/// M6-S15 (C: the AI spends what it has, BUG-1890; a re-stage, the checks unchanged): seed 5 now ends at 25,913 with no seat
/// under the floor ("no seat retrained a worker"). It plays seed 1, the lowest Raraku seed whose seat retrains (a worker
/// Train sent for 24,781 with fewer live workers than the floor; seeds 2-8 retrain nowhere on M6-S15's tree). Was seed 5.
/// </summary>
[Trait("Category", "Stress")]
public class M6Sh2DeterminismStressTests(ITestOutputHelper output)
{
    private const int After = 2000, Limit = 45_000;
    private const ulong Seed = 1; // M6-S15: was 5 (no retrain there since C); D33: was 3 (no retrain there since D33's waves)

    [Fact]
    public void Raraku_TheRetrain_TwinsSaveAndReplay()
    {
        Simulation a = MapFileTests.MapMatch("raraku", Seed, true, out ReplayRecorder? recorder);
        Simulation b = MapFileTests.MapMatch("raraku", Seed, false, out _);
        int retrainAt = -1;
        for (int p = 0; p < 2; p++)
        {
            AiPlayer ai = a.AiOf(p)!;
            ai.SentForTests = c =>
            {
                // M6-S14 (BUG-1660): the retrain is a floor, so its first Train is one sent with fewer live workers than
                // ai.minWorkers (was: with none; a seat now rarely reaches none, it trains back up before).
                if (retrainAt < 0 && c.Kind == CommandKind.Train && ai.WorkerCount < ai.Rules.MinWorkers && ai.IsWorkerType[c.TypeId]) retrainAt = c.Tick;
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
