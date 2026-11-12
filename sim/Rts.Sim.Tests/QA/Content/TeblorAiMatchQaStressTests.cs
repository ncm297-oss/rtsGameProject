using Rts.Sim.Ai;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA D28 (session 2026-11-11-1317): a Normal Teblor AI seat on the shipped data, 12,000 ticks (10 minutes) on the generated
/// map and on each of the three map files, against a Normal AI of each older faction (the Teblor on seat 0 against the
/// Malazan, on seat 1 against the Whirlwind), under <see cref="CriterionMatch"/>'s checks: no exception, the view oracle and
/// census clean, every live entity in range, every refused command of a documented kind and no AI command refused more than
/// <see cref="CriterionMatch.MaxStreak"/> thinks in a row on one target. Every refused kind is printed. And the twins: the
/// same two pairings on the generated map fold the same per-tick hashes in this process and a fresh child process.
/// </summary>
[Trait("Category", "Stress")]
public class TeblorAiMatchQaStressTests(ITestOutputHelper output)
{
    private const int Bound = 12_000;

    private static PlayerSeat[] Seats(string opponent)
    {
        int teblor = TestSim.Data.FindFaction("teblor"), other = TestSim.Data.FindFaction(opponent);
        PlayerSeat t = new(teblor, SeatController.Ai, AiDifficulty.Normal), o = new(other, SeatController.Ai, AiDifficulty.Normal);
        return opponent == "malazan" ? new[] { t, o } : new[] { o, t };
    }

    public static TheoryData<string, string> Rows()
    {
        var rows = new TheoryData<string, string>();
        foreach (string map in new[] { "generated", "raraku", "pale_hills", "vathar_crossing" })
            foreach (string opp in new[] { "malazan", "whirlwind" })
                rows.Add(map, opp);
        return rows;
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void ATeblorAiSeat_Plays12000Ticks_WithNoRefusalStreak_AndCleanChecks(string map, string opponent)
    {
        Simulation sim = Stress.HuntStressTests.Match(map, Seats(opponent), 1);
        int seat = opponent == "malazan" ? 0 : 1;
        Assert.Equal(TestSim.Data.FindFaction("teblor"), sim.World.FactionOf(seat));
        CriterionMatch.Result r = CriterionMatch.Play(sim, Bound, checks: true);
        World w = sim.World;
        output.WriteLine($"{map} teblor (seat {seat}) v {opponent}: ended {r.EndTick}, over {r.Over}, winner {r.Winner}; teblor age {w.Age(seat)}, "
            + $"waves started {sim.AiOf(seat)!.Board.WavesStarted}, defeated at {r.DefeatedAt[seat]}; {opponent} defeated at {r.DefeatedAt[1 - seat]}; "
            + $"longest streak {r.LongestStreak} ({r.LongestStreakAt}); refusals: {r.RefusalText}");
        Assert.True(r.Error == null, r.Error);
        Assert.True(r.Thinks > 0);
    }

    private const string TwinVariable = "RTS_QA_D28_TEBLOR_TWIN_CHILD";
    private const string TwinLine = "QA-D28-TEBLOR-TWIN ";

    [Theory]
    [InlineData("malazan")]
    [InlineData("whirlwind")]
    public void TeblorTwins_OnTheGeneratedMap_TwoProcesses_SamePerTickHashes(string opponent)
    {
        string? childOf = Environment.GetEnvironmentVariable(TwinVariable);
        if (childOf != null && childOf != opponent) return; // the child runs only the row that asked for it
        Simulation sim = Stress.HuntStressTests.Match("generated", Seats(opponent), 3);
        var h = new Rts.Sim.Determinism.StateHasher();
        while (sim.TickNumber < Bound)
        {
            sim.Tick();
            h.Add(sim.StateHash());
        }
        string mine = $"{h.Value:X16} {sim.StateHash():X16}";
        if (childOf == opponent)
        {
            output.WriteLine(TwinLine + mine);
            return;
        }
        output.WriteLine($"teblor v {opponent}, generated seed 3: this process {mine}");
        string child = MapFileTests.RunChild(typeof(TeblorAiMatchQaStressTests), nameof(TeblorTwins_OnTheGeneratedMap_TwoProcesses_SamePerTickHashes),
            TwinVariable, TwinLine, seconds: 600, value: opponent);
        Assert.Equal(mine, child);
    }
}
