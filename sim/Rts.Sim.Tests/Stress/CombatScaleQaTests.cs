using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA M4-1 scale: the scan early-out with one enemy far away, a 1,000 v 1,000 brawl, and allocation with an enemy on
/// the map. Debug wall clock like every tick budget; the class runs alone.
/// </summary>
[Collection(SerialCollection.Name)]
public class CombatScaleQaTests
{
    private readonly ITestOutputHelper _out;

    public CombatScaleQaTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// The CrowdPerfTests tight blob (2,500 units of player 0, seed 99, all ordered to the central cell), optionally with
    /// one Heavy Infantry of player 1 standing on the passable cell farthest from the blob, far outside every sight.
    /// </summary>
    private static Simulation Blob(bool farEnemy)
    {
        Simulation sim = MoveScenario.Spawn(seed: 99, units: 2500, maxCost: 12f, out int goalCell, capacity: 2501, players: 1);
        NavGrid g = sim.World.NavGrid;
        MoveScenario.MoveAll(sim, MoveScenario.Center(g, goalCell)); // before the enemy exists: it stands where it is put
        if (farEnemy)
        {
            FlowField field = FlowField.Build(g, goalCell);
            int far = -1;
            float best = -1f;
            for (int c = 0; c < g.Width * g.Height; c++)
            {
                float cost = field.CostAt(c);
                if (float.IsFinite(cost) && cost > best) { best = cost; far = c; }
            }
            CombatScenes.Place(sim, 1, CombatScenes.HeavyInfantry, MoveScenario.Center(g, far));
            Assert.True(Vector2.Distance(MoveScenario.Center(g, far), MoveScenario.Center(g, goalCell)) > 60f);
        }
        sim.Tick();
        return sim;
    }

    private static double Average(Simulation sim, int ticks)
    {
        for (int t = 0; t < 5; t++) sim.Tick();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long freq = Stopwatch.Frequency, total = 0;
        for (int t = 0; t < ticks; t++)
        {
            long start = Stopwatch.GetTimestamp();
            sim.Tick();
            total += Stopwatch.GetTimestamp() - start;
        }
        return total * 1000.0 / freq / ticks;
    }

    /// <summary>
    /// Brief criterion 8: "scanning in a world with no enemy in sight must be near free". The 2,500 blob with one enemy at
    /// the far corner (so every unit's scan runs, and finds only its own buckets) against the same blob alone: the enemy
    /// costs at most 0.25 ms a tick, median against median. BUG-0158: a first pair of runs is discarded (the machine slows
    /// after about two seconds of sustained load, which an alone-first order read as the enemy's cost), then four pairs
    /// run with the order alternating (alone first, then enemy first), so a slowdown's onset lands on both sides alike.
    /// Also asserts the far enemy is never targeted and the blob's movement is bit-identical to the alone run.
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void TightBlob2500_OneEnemyAtTheFarCorner_ScansNearFree()
    {
        Average(Blob(farEnemy: false), 300); // warm-up pair, discarded
        Average(Blob(farEnemy: true), 300);
        var alone = new List<double>();
        var withEnemy = new List<double>();
        for (int r = 0; r < 4; r++)
        {
            bool enemyFirst = r % 2 == 1;
            if (enemyFirst) withEnemy.Add(Average(Blob(farEnemy: true), 300));
            alone.Add(Average(Blob(farEnemy: false), 300));
            if (!enemyFirst) withEnemy.Add(Average(Blob(farEnemy: true), 300));
        }
        double a = Median(alone), e = Median(withEnemy);
        _out.WriteLine($"2,500 one-player blob: alone {string.Join(" / ", alone.Select(x => x.ToString("F2")))} ms, one far enemy {string.Join(" / ", withEnemy.Select(x => x.ToString("F2")))} ms; median delta {e - a:+0.000;-0.000} ms");

        // Same blob behavior with and without the far enemy: nobody engages it.
        Simulation s0 = Blob(false), s1 = Blob(true);
        UnitStore u0 = s0.World.Units, u1 = s1.World.Units;
        for (int t = 0; t < 200; t++)
        {
            s0.Tick();
            s1.Tick();
        }
        for (int i = 0; i < 2500; i++)
        {
            Assert.Equal(u0.Position[i], u1.Position[i]);
            Assert.Equal(default, u1.Target[i]);
        }
        Assert.True(e - a <= 0.25, $"one enemy at the far corner costs {e - a:F3} ms a tick over the blob alone ({a:F2} ms)");
    }

    /// <summary>The median of <paramref name="xs"/> (the mean of the middle two for an even count).</summary>
    internal static double Median(List<double> xs)
    {
        var sorted = new List<double>(xs);
        sorted.Sort();
        int n = sorted.Count;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }

    /// <summary>A 2,500 one-player blob with one enemy at the far corner: its scan ticks allocate nothing.</summary>
    [Fact]
    public void TightBlob2500_OneEnemyAtTheFarCorner_ScanTicksAllocateNothing()
    {
        Simulation sim = Blob(farEnemy: true);
        for (int t = 0; t < 8; t++) sim.Tick();
        AllocationProbe.AssertZero(() =>
        {
            for (int t = 0; t < 4; t++) sim.Tick();
        });
    }

    /// <summary>
    /// Report (2x the 500 v 500 row): 1,000 Heavy Infantry against 1,000 Raiders on a flat 192 x 192 map, battle lines
    /// 10 ranks deep, attack-moved into each other: ticks 5-205 and 205-605. Guarded only against a hang-like cost (an
    /// average over 40 ms, ten times the 500-unit budget).
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void Brawl1000v1000_Report()
    {
        Simulation sim = CombatScenes.Flat(size: 192, units: 2000);
        CombatScenes.FlatBrawl(sim, 1000, new Vector2(192f, 192f), gap: 8f, ranks: 10);
        for (int t = 0; t < 5; t++) sim.Tick();
        var first = new double[200];
        var later = new double[400];
        long freq = Stopwatch.Frequency;
        for (int t = 0; t < first.Length; t++)
        {
            long s = Stopwatch.GetTimestamp();
            sim.Tick();
            first[t] = (Stopwatch.GetTimestamp() - s) * 1000.0 / freq;
        }
        int deadAt205 = sim.World.Kills[0] + sim.World.Kills[1];
        for (int t = 0; t < later.Length; t++)
        {
            long s = Stopwatch.GetTimestamp();
            sim.Tick();
            later[t] = (Stopwatch.GetTimestamp() - s) * 1000.0 / freq;
        }
        _out.WriteLine($"1,000 v 1,000 brawl: ticks 5-205 avg {first.Average():F2} ms, worst {first.Max():F2} ms ({deadAt205} dead); ticks 205-605 avg {later.Average():F2} ms, worst {later.Max():F2} ms ({sim.World.Kills[0] + sim.World.Kills[1]} dead, {sim.World.Units.Count} alive)");
        Assert.True(sim.World.Kills[0] + sim.World.Kills[1] > 0);
        Assert.True(later.Average() < 40.0 && first.Average() < 40.0);
    }

    /// <summary>
    /// BUG-0144: <c>ConstructionScaleStressTests.APlacementEveryTwoSeconds_32MarchingGroups_LongestFieldWait_Report</c>'s
    /// scene (512 units of two owners in 32 mixed goal groups march across the default map while player 0's worker places
    /// a House every 2 s) on combat: the groups meet and fight, and a chaser re-aiming its goal while placements stale the
    /// cache waits up to 34 ticks for a usable field. Same 1 s (20-tick) bound as the walkers' row. A tick count, not wall
    /// clock: deterministic. Measured: combat off 16 ticks (= base 8655fb1), combat on without placements 14, with 34.
    /// </summary>
    [Fact(Skip = "BUG-0144: chasers wait up to 1.7 s for a flow field under placement churn")]
    public void ChasersUnderPlacementChurn_LongestFieldWait_AtMostOneSecond()
    {
        Simulation sim = MoveScenario.Spawn(11, units: 512, maxCost: 30f, out _, capacity: 520, players: 2,
            map: MapGenParams.Default with { Forests = 12, GoldMines = 8 });
        NavGrid g = sim.World.NavGrid;
        var rng = new Determinism.SimRng(11, 4);
        List<int> open = FlowFieldOracle.PassableCells(g);
        int center = MoveScenario.CentralCell(g);
        int[] goals = new int[32];
        for (int k = 0; k < 32; k++)
        {
            int c;
            do c = open[rng.NextInt(0, open.Count)];
            while (Math.Abs(c % g.Width - center % g.Width) + Math.Abs(c / g.Width - center / g.Width) < 40);
            goals[k] = c;
        }
        UnitStore u = sim.World.Units;
        for (int i = 0; i < 512; i++) sim.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), MoveScenario.Center(g, goals[i % 32])));
        sim.Enqueue(Command.SpawnUnit(0, GatherMaps.Laborer, MoveScenario.Center(g, center)));
        sim.Tick();
        sim.Tick();
        EntityHandle placer = MoveScenario.Handle(sim, 512);
        BuildMaps.SetTotals(sim, 0, 100_000, 100_000);
        var wait = new int[u.Capacity];
        int longest = 0, longestUnit = -1, placements = 0;
        bool longestChasing = false;
        for (int t = 0; t < 1600; t++)
        {
            if (t < 1200 && t % 40 == 0)
            {
                for (int tries = 0; tries < 200; tries++)
                {
                    int c = open[rng.NextInt(0, open.Count)];
                    if (!sim.World.CanPlace(0, BuildMaps.House, c, out _)) continue;
                    sim.Enqueue(Command.Build(0, placer, BuildMaps.House, MoveScenario.Center(g, c)));
                    placements++;
                    break;
                }
            }
            sim.Tick();
            for (int i = 0; i < 512; i++)
            {
                wait[i] = QA.GridChangeOracle.WaitingForField(sim.World, i) ? wait[i] + 1 : 0;
                if (wait[i] > longest) (longest, longestUnit, longestChasing) = (wait[i], i, u.Target[i] != default);
            }
        }
        _out.WriteLine($"{placements} placements, combat on: longest field wait {longest} ticks ({longest / 20.0:F2} s, unit {longestUnit}, chasing {longestChasing}); {sim.World.Kills[0] + sim.World.Kills[1]} dead");
        Assert.True(placements >= 25);
        Assert.True(sim.World.Kills[0] + sim.World.Kills[1] > 0, "setup: nobody fought");
        Assert.True(longest <= 20, $"longest field wait {longest} ticks = {longest / 20.0:F2} s (unit {longestUnit}, chasing {longestChasing})");
    }
}
