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

    /// <summary>The dev's <c>MoreGoalsThanCacheSlots</c> scenario on one map: (gave up, worst Idle-pair gap ratio, first pack violation or null).</summary>
    private static (int GaveUp, int Arrived, string? Pack, int StillMoving) MoreGoals(ulong seed)
    {
        Simulation sim = MoveScenario.Spawn(seed, units: 128, maxCost: 15f, out int center);
        NavGrid g = sim.World.NavGrid;
        FlowField near = FlowField.Build(g, center);
        var goals = new List<int>();
        for (int c = 0; c < g.Width * g.Height && goals.Count < 64; c++)
            if (near.CostAt(c) <= 15f) goals.Add(c);
        Assert.Equal(64, goals.Count);
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
            sim.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), MoveScenario.Center(g, goals[i % goals.Count])));
        sim.Tick();
        int moving = u.Count;
        for (int t = 0; t < 3000 && moving > 0; t++)
        {
            int before = sim.World.FlowFields.BuildCount;
            sim.Tick();
            Assert.True(sim.World.FlowFields.BuildCount - before <= MovementConstants.MaxFieldBuildsPerTick);
            moving = 0;
            for (int i = 0; i < u.Capacity; i++) if (u.State[i] == UnitState.Moving) moving++;
        }
        bool[] arrived = MoveScenario.Arrived(sim.World);
        int arrivedCount = 0, gaveUp = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (arrived[i]) arrivedCount++;
            else if (u.GoalCell[i] == -1) gaveUp++;
        }
        return (gaveUp, arrivedCount, MoveScenario.FirstPackViolation(sim.World), moving);
    }

    /// <summary>Report: the 128-unit, 64-neighbouring-goal scenario on new-seed maps 1-40 (no assert on the rates).</summary>
    [Fact]
    public void Report_MoreGoalsThanCacheSlots_OnNewSeedMaps1To40()
    {
        int pack = 0, overBound = 0, worst = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var r = MoreGoals(seed);
            Assert.Equal(0, r.StillMoving); // termination must hold on every map
            if (r.Pack != null) pack++;
            if (r.GaveUp > 128 * 22 / 100) overBound++;
            worst = Math.Max(worst, r.GaveUp);
            _out.WriteLine($"seed {seed}: arrived {r.Arrived}, gave up {r.GaveUp}{(r.Pack == null ? "" : ", PACK: " + r.Pack)}");
        }
        _out.WriteLine($"pack rule broken on {pack}/40 maps; give-up over 22% on {overBound}/40 (worst {worst}/128)");
    }

    /// <summary>Report: the same scenario on the pre-M1-6 maps of old seeds 1-20 (TestSeeds.PreMix), to show the spread predates the seed mixing.</summary>
    [Fact]
    public void Report_MoreGoalsThanCacheSlots_OnPreMixMaps1To20()
    {
        int pack = 0, overBound = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var r = MoreGoals(TestSeeds.PreMix(seed));
            Assert.Equal(0, r.StillMoving);
            if (r.Pack != null) pack++;
            if (r.GaveUp > 128 * 22 / 100) overBound++;
            _out.WriteLine($"old seed {seed}: arrived {r.Arrived}, gave up {r.GaveUp}{(r.Pack == null ? "" : ", PACK: " + r.Pack)}");
        }
        _out.WriteLine($"old maps: pack rule broken on {pack}/20; give-up over 22% on {overBound}/20");
    }

    /// <summary>The dev test's own two rules, on every new-seed map 1-40.</summary>
    [Fact(Skip = "BUG-0039: the MoreGoalsThanCacheSlots bounds hold on the PreMix(21) map only; most new-seed maps break the pack rule or the 22% bound")]
    public void MoreGoalsThanCacheSlots_PackRuleAnd22PercentBound_HoldOnNewSeedMaps1To40()
    {
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var r = MoreGoals(seed);
            Assert.True(r.GaveUp <= 128 * 22 / 100, $"seed {seed}: {r.GaveUp} of 128 gave up");
            Assert.True(r.Pack == null, $"seed {seed}: {r.Pack}");
        }
    }
}
