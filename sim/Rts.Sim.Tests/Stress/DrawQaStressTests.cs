using Rts.Sim.Tests.Draw;
using Rts.Sim.Tests.QA;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA (M6-S13, session 2026-11-25-1437), the brief's QA focus at scale: criterion 5's invariant (no cancels: over within the
/// bound, no restart without a death or a defeat) on 1,000 seeds past the developer's 200, and the oracle fuzz with cancels,
/// trains, gathers and research (<see cref="DrawRuleQaTests.Fuzz"/>: the clock and every seat's answer equal an independent
/// scan every tick) on 1,000 seeds.
/// </summary>
public class DrawQaStressTests
{
    private readonly ITestOutputHelper _out;

    public DrawQaStressTests(ITestOutputHelper output) => _out = output;

    public static TheoryData<int> Batches()
    {
        var rows = new TheoryData<int>();
        for (int b = 0; b < 10; b++) rows.Add(b);
        return rows;
    }

    /// <summary>Criterion 5 on seeds 201-1,200 (the developer's class runs 1-200).</summary>
    [Theory]
    [Trait("Category", "Stress")]
    [MemberData(nameof(Batches))]
    public void Criterion5_OnAThousandMoreSeeds(int batch)
    {
        int drawn = 0, restarts = 0;
        for (int k = 0; k < 100; k++)
        {
            (bool d, int r) = DrawInvariantFuzzTests.Play((ulong)(201 + batch * 100 + k));
            if (d) drawn++;
            restarts += r;
        }
        _out.WriteLine($"batch {batch}: 100 over, {drawn} drawn, {restarts} restarts");
    }

    /// <summary>
    /// A stalled four-seat AI match on the shipped data (Pale Hills free-for-all seed 6, the developer's "draw at 51,216, 5
    /// restarts"): the independent oracle agrees with the clock and every seat's answer on every tick, and the counters equal a
    /// scan, to the draw. Re-measures the developer's row with a second reading of the rule.
    /// M6-S14 (the AI-play session; a re-stage, the checks unchanged, the name kept): seed 6 is never spent now (over at
    /// 81,444, seat 1 the winner), so the row plays seed 1 (spent from 26,961, drawn at 58,257: <c>HuntStressTests</c>' seed-1
    /// row). Was seed 6.
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void PaleHillsSeed6_FourSeats_TheOracleAgreesEveryTick_ToTheDraw()
    {
        Simulation sim = DrawStalledRowsReportTests.Build("pale_hills", 1, 4, null); // M6-S14: was seed 6
        var oracle = new DrawQaOracle(sim);
        int firstSpent = -1;
        while (!sim.MatchOver && sim.TickNumber < 144_000)
        {
            oracle.BeforeTick();
            sim.Tick();
            string? fail = oracle.AfterTick() ?? DrawRuleQaTests.CountersAgree(sim.World);
            if (fail != null) Assert.Fail(fail);
            if (firstSpent < 0 && sim.World.DrawClock > 0) firstSpent = sim.TickNumber;
        }
        _out.WriteLine($"pale_hills seed 6: first spent {firstSpent}, restarts death {oracle.RestartsOnDeath} defeat {oracle.RestartsOnDefeat} unspent {oracle.RestartsUnspent}, over {sim.MatchOver} drawn {sim.World.MatchDrawn} at {sim.TickNumber}");
        Assert.True(sim.World.MatchDrawn);
    }

    /// <summary>The oracle fuzz with cancels on seeds 1,001-2,000, 3,000 ticks each.</summary>
    [Theory]
    [Trait("Category", "Stress")]
    [MemberData(nameof(Batches))]
    public void TheOracleFuzz_WithCancels_OnAThousandSeeds(int batch)
    {
        int drawn = 0, won = 0, open = 0, restarts = 0;
        for (int k = 0; k < 100; k++)
        {
            DrawRuleQaTests.FuzzResult r = DrawRuleQaTests.Fuzz((ulong)(1001 + batch * 100 + k), 3000);
            if (r.Drawn) drawn++;
            else if (r.Won) won++;
            else open++;
            restarts += r.Restarts;
        }
        _out.WriteLine($"batch {batch}: drawn {drawn}, won {won}, still open {open}, clock restarts {restarts}");
    }
}
