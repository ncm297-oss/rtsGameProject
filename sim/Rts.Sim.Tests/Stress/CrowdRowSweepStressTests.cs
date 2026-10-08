using Rts.Sim.Movement;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA sweeps of the M1-4d-3 re-bounded crowd rows (BUG-0039) beyond the seeds the bounds were fitted
/// on: 500 units to 4 points (one player per point) on seeds 1-40 with the per-tick invariants, 2,500
/// on seeds 11-20, and the 128-unit / 64-goal row on seeds 41-80; plus termination hunts (Soak). Each
/// asserts the bound the dev test asserts (40% / 22% arrived, at most 48 of 128 gave up), termination
/// and the build cap, and prints min / mean / max for docs/03.
/// </summary>
public class CrowdRowSweepStressTests
{
    private readonly ITestOutputHelper _out;

    public CrowdRowSweepStressTests(ITestOutputHelper output) => _out = output;

    private void SweepFourPoints(int units, int limit, int minPercent, ulong from, ulong to)
    {
        var arrived = new List<int>();
        var failures = new List<string>();
        for (ulong seed = from; seed <= to; seed++)
        {
            Func<World, string?>? check = null;
            if (units <= 500)
            {
                var lastPos = new System.Numerics.Vector2[units];
                var lastIdle = new bool[units];
                check = w => LocalMovementStressTests.CheckInvariants(w, lastPos, lastIdle);
            }
            CrowdRows.Result r = CrowdRows.ToFourPoints(seed, units, limit, onePlayerPerPoint: true, everyTick: check, combat: false);
            CrowdRows.AssertBuildCap(r);
            arrived.Add(r.Arrived);
            _out.WriteLine($"seed {seed}: arrived {r.Arrived} ({100.0 * r.Arrived / units:F0}%), gave up {r.GaveUp}, still moving {r.StillMoving}, {r.Ticks} ticks");
            if (r.StillMoving != 0) failures.Add($"seed {seed}: {r.StillMoving} still moving");
            if (r.Arrived * 100 < minPercent * units) failures.Add($"seed {seed}: {r.Arrived} of {units} arrived (< {minPercent}%)");
        }
        _out.WriteLine($"{units} to 4 points, seeds {from}-{to}: arrived min {arrived.Min()}, mean {arrived.Average():F0}, max {arrived.Max()}");
        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    /// <summary>500 units to 4 points, one player per point, seeds 1-40: every tick's invariants, termination, at least 40% arrived (the dev row's bound).</summary>
    [Fact]
    public void FiveHundredToFourPoints_Seeds1To40_BoundAndInvariantsHold() => SweepFourPoints(500, 3000, 40, 1, 40);

    /// <summary>2,500 units to 4 points, one player per point, seeds 11-20 (the dev row sweeps 1-10; QA ran 11-40 once at M1-4d-3: min 621, mean 844): termination, at least 22% arrived.</summary>
    [Fact]
    [Trait("Category", "Soak")] // about 1 min in Debug; filter out with Category!=Soak for a quick loop
    public void TwentyFiveHundredToFourPoints_Seeds11To20_BoundHolds() => SweepFourPoints(2500, 6000, 22, 11, 20);

    /// <summary>128 units to 64 goals, one player per goal, seeds 41-80: termination, build cap, every Idle unit arrived or gave up, at most the dev test's bound gave up (re-set on seeds 1-80 for BUG-0049: seed 51, the BUG-0048 map, gives up 73-77).</summary>
    [Fact]
    public void MoreGoalsThanCacheSlots_Seeds41To80_BoundHolds()
    {
        var gaveUp = new List<int>();
        var failures = new List<string>();
        int pack = 0;
        for (ulong seed = 41; seed <= 80; seed++)
        {
            CrowdRows.Result r = CrowdRows.MoreGoalsThanCacheSlots(seed, combat: false);
            CrowdRows.AssertBuildCap(r);
            gaveUp.Add(r.GaveUp);
            if (r.Pack != null) pack++;
            if (r.StillMoving != 0) failures.Add($"seed {seed}: {r.StillMoving} still moving");
            if (r.Arrived + r.GaveUp != 128) failures.Add($"seed {seed}: {128 - r.Arrived - r.GaveUp} neither arrived nor gave up");
            if (r.GaveUp > MovementSystemTests.MoreGoalsMaxGaveUp) failures.Add($"seed {seed}: {r.GaveUp} of 128 gave up");
        }
        gaveUp.Sort();
        _out.WriteLine($"seeds 41-80: gave up min {gaveUp[0]}, median {(gaveUp[19] + gaveUp[20]) / 2.0}, max {gaveUp[^1]}; pack rule broken on {pack}/40");
        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    /// <summary>
    /// Termination hunt: the 128-unit / 64-goal row (more live goals than cache slots) on seeds 81-140 (QA ran
    /// 81-200 once at M1-4d-3: all stop). Every map must stop within 3,000 ticks (it stops within about 600 when it stops at all).
    /// </summary>
    [Fact]
    [Trait("Category", "Soak")] // about 1 min in Debug; filter out with Category!=Soak for a quick loop
    public void MoreGoalsThanCacheSlots_Seeds81To140_AllTerminate()
    {
        var stuck = new List<string>();
        for (ulong seed = 81; seed <= 140; seed++)
        {
            // Combat off (config only; docs/03 "Combat switch"): a movement row with a walking bound (3,000 ticks). On combat the
            // scene ends only when one side's fighters have cut down the other side's units that can't fight back yet (ranged and
            // casters until M4-2b): a slow, one-sided brawl that ends by itself (seed 110: tick 3,759; the other 59 seeds stop within 3,000 ticks on combat) (BUG-0150's fix round; the
            // ping-pong itself is fixed and GridChangeFuzzStressTests runs on combat). Combat-on termination is CombatTerminationTests'.
            CrowdRows.Result r = CrowdRows.MoreGoalsThanCacheSlots(seed, combat: false);
            if (r.StillMoving != 0) stuck.Add($"seed {seed}: {r.StillMoving} still moving after {r.Ticks} ticks");
        }
        _out.WriteLine($"seeds 81-140: {stuck.Count} of 60 maps never stop. {string.Join("; ", stuck)}");
        Assert.True(stuck.Count == 0, string.Join("; ", stuck));
    }

    /// <summary>
    /// Termination hunt: 500 units (two players) to 500 random goals on seeds 1-4 (QA ran 1-10 once at M1-4d-3:
    /// all stop, gave up mean 11.3 of 500; a goal per unit, far
    /// more than the cache holds). Everything must stop within 30,000 ticks.
    /// </summary>
    [Fact]
    [Trait("Category", "Soak")] // about 1 min in Debug; filter out with Category!=Soak for a quick loop
    public void FiveHundredUnitsTo500RandomGoals_Seeds1To4_AllTerminate()
    {
        var stuck = new List<string>();
        var gaveUps = new List<int>();
        for (ulong seed = 1; seed <= 4; seed++)
        {
            Simulation sim = MoveScenario.Spawn(seed, 500, 60f, out _);
            Map.NavGrid g = sim.World.NavGrid;
            var rng = new Determinism.SimRng(seed, 501);
            Entities.UnitStore u = sim.World.Units;
            var passable = new List<int>();
            for (int c = 0; c < g.Width * g.Height; c++) if (g.IsPassable(c % g.Width, c / g.Width)) passable.Add(c);
            for (int i = 0; i < u.Capacity; i++)
                sim.Enqueue(Commands.Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), MoveScenario.Center(g, passable[rng.NextInt(0, passable.Count)])));
            sim.Tick();
            int t = 0, moving;
            do
            {
                sim.Tick();
                t++;
                moving = 0;
                for (int i = 0; i < u.Capacity; i++) if (u.State[i] == Entities.UnitState.Moving) moving++;
            } while (moving > 0 && t < 30_000);
            int gaveUp = 0;
            for (int i = 0; i < u.Capacity; i++) if (u.GoalCell[i] < 0) gaveUp++;
            gaveUps.Add(gaveUp);
            _out.WriteLine($"seed {seed}: stopped after {t} ticks ({moving} still moving), gave up {gaveUp}");
            if (moving > 0) stuck.Add($"seed {seed}: {moving} still moving after {t} ticks");
        }
        _out.WriteLine($"gave up min {gaveUps.Min()}, mean {gaveUps.Average():F1}, max {gaveUps.Max()} of 500");
        Assert.True(stuck.Count == 0, string.Join("; ", stuck));
    }
}
