using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;
using Rts.Sim.Pathfinding;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M1-6): BUG-0014's seed mixing moved every test onto new maps, and six tests went back to their
/// old maps through <c>TestSeeds.PreMix</c>. These sweeps run one of those scenarios on many new-seed
/// maps, so a bound that holds on one map only is visible (BUG-0039).
/// </summary>
public class SeedSweepQaTests
{
    private readonly ITestOutputHelper _out;

    public SeedSweepQaTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// The dev's <c>MoreGoalsThanCacheSlots</c> scenario on one map (M1-4d-3: <see cref="CrowdRows.MoreGoalsThanCacheSlots"/>,
    /// one player per goal by default): (gave up, arrived, first pack violation or null, still moving).
    /// </summary>
    private static (int GaveUp, int Arrived, string? Pack, int StillMoving) MoreGoals(ulong seed, bool onePlayerPerGoal = true)
    {
        CrowdRows.Result r = CrowdRows.MoreGoalsThanCacheSlots(seed, onePlayerPerGoal);
        CrowdRows.AssertBuildCap(r);
        return (r.GaveUp, r.Arrived, r.Pack, r.StillMoving);
    }

    /// <summary>Report: the 128-unit, 64-neighbouring-goal scenario on new-seed maps 1-40 (no assert on the rates).</summary>
    [Fact]
    public void Report_MoreGoalsThanCacheSlots_OnNewSeedMaps1To40()
    {
        int pack = 0, worst = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var r = MoreGoals(seed);
            Assert.Equal(0, r.StillMoving); // termination must hold on every map
            if (r.Pack != null) pack++;
            worst = Math.Max(worst, r.GaveUp);
            _out.WriteLine($"seed {seed}: arrived {r.Arrived}, gave up {r.GaveUp}{(r.Pack == null ? "" : ", PACK: " + r.Pack)}");
        }
        _out.WriteLine($"pack rule broken on {pack}/40 maps; worst give-up {worst}/128");
    }

    /// <summary>
    /// Report (M1-4d-3): the pre-M1-4d-3 split (slot % 64), which gives every goal one unit of each
    /// player. Since BUG-0037 they are enemies contesting each goal, so many more give up; printed for
    /// docs/03, asserts only termination. (Replaces the pre-M1-6-maps report: the row is swept on new seeds now.)
    /// </summary>
    [Fact]
    public void Report_MoreGoalsThanCacheSlots_BothPlayersAtEveryGoal_OnNewSeedMaps1To40()
    {
        var gaveUp = new List<int>();
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var r = MoreGoals(seed, onePlayerPerGoal: false);
            Assert.Equal(0, r.StillMoving);
            gaveUp.Add(r.GaveUp);
        }
        gaveUp.Sort();
        _out.WriteLine($"both players at every goal: gave up median {(gaveUp[19] + gaveUp[20]) / 2.0}, min {gaveUp[0]}, max {gaveUp[^1]} of 128");
    }

    /// <summary>
    /// The dev test's rules on every new-seed map 1-40 (BUG-0039, rewritten at M1-4d-3): termination, the
    /// build cap, every Idle unit arrived or gave up, and the give-up bound that holds on every swept
    /// map (worst measured 42 of 128). The pack rule does not hold everywhere (15 of 40 maps break
    /// it): report-only, docs/03 names the limit.
    /// </summary>
    [Fact]
    public void MoreGoalsThanCacheSlots_TerminationAndGiveUpBound_HoldOnNewSeedMaps1To40()
    {
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var r = MoreGoals(seed);
            Assert.True(r.StillMoving == 0, $"seed {seed}: {r.StillMoving} still moving");
            Assert.True(r.Arrived + r.GaveUp == 128, $"seed {seed}: {128 - r.Arrived - r.GaveUp} Idle units neither arrived nor gave up");
            Assert.True(r.GaveUp <= 48, $"seed {seed}: {r.GaveUp} of 128 gave up");
        }
    }
}
