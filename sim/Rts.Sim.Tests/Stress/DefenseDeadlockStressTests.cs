using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Replays;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA (M5-4b round 1, session 2026-10-13-0916): the AI-vs-AI deadlock of BUG-0670 / 0671 re-run after fix 2a3da00. The
/// scene of <see cref="AiWaveStressTests"/> (1): Malazan v Whirlwind, Normal, early-wave data, three line units of each
/// player parked among the other's workers, generated 128 map, 6,000 ticks. Before the fix both seats defended from tick
/// ~20 to the end with 0 Gathers and 0 waves. Gated per seat: the economy runs in the second half (the bank rises after tick
/// 3,000) and the seat is not defending for the whole second half; and at least one wave in the match.
/// </summary>
public class DefenseDeadlockStressTests
{
    private readonly ITestOutputHelper _out;

    public DefenseDeadlockStressTests(ITestOutputHelper output) => _out = output;

    private static readonly Lazy<GameData> s_early = new(() => AiScenes.EarlyWaves(2));

    [Theory]
    [InlineData(1UL)]
    [InlineData(6UL)]
    [InlineData(11UL)]
    public void ParkedRaidersInBothBases_NoSeatStaysDefendingAndIdleForGood(ulong seed)
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai);
        Simulation sim = AiScenes.Generated(seats, seed, record: false, out _, data: s_early.Value, forests: 12, raiders: 3);
        var logs = new[] { AiScenes.Log(sim, 0), AiScenes.Log(sim, 1) };
        var defendingTicks = new int[2];
        var defendingLate = new int[2];
        var lateIncome = new int[2];
        var bank = new int[2];
        for (int t = 0; t < 6000; t++)
        {
            sim.Tick();
            for (int p = 0; p < 2; p++)
            {
                int now = sim.World.Gold[p] + sim.World.Wood[p];
                if (sim.TickNumber > 3000 && now > bank[p]) lateIncome[p] += now - bank[p];
                bank[p] = now;
                if (!sim.AiOf(p)!.Board.Defending) continue;
                defendingTicks[p]++;
                if (sim.TickNumber > 3000) defendingLate[p]++;
            }
        }
        int waves = 0;
        for (int p = 0; p < 2; p++)
        {
            AiPlayer ai = sim.AiOf(p)!;
            int gathers = logs[p].Count(c => c.Kind == CommandKind.Gather);
            int lateGathers = logs[p].Count(c => c.Kind == CommandKind.Gather && c.Tick > 3000);
            waves += ai.Board.WavesStarted;
            _out.WriteLine($"seed {seed} p{p}: defending {defendingTicks[p]} ticks ({defendingLate[p]} after 3000), threats {ai.Board.Defenses}, "
                + $"gathers {gathers} ({lateGathers} after 3000), income after 3000 {lateIncome[p]}, waves {ai.Board.WavesStarted}, gold {sim.World.Gold[p]} wood {sim.World.Wood[p]}, "
                + $"kills {sim.World.Kills[p]} losses {sim.World.Losses[p]}, defeated {sim.World.IsDefeated(p)}");
        }
        for (int p = 0; p < 2; p++)
        {
            if (sim.World.IsDefeated(p)) continue;
            Assert.True(lateIncome[p] > 0, $"seed {seed} p{p}: no income after tick 3000: the economy is stalled");
            Assert.True(defendingLate[p] < 3000, $"seed {seed} p{p}: defending every tick of the second half");
        }
        Assert.True(waves > 0, $"seed {seed}: no wave in 6,000 ticks");
    }
}
