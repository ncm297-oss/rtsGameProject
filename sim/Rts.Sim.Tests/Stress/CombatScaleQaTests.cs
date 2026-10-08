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
    /// the far corner (so every unit's scan runs, and finds only its own buckets) against the same blob alone, alternated
    /// three times in one process: the enemy costs at most 0.25 ms a tick on average. Also asserts the far enemy is
    /// never targeted and the blob's movement is bit-identical to the alone run.
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void TightBlob2500_OneEnemyAtTheFarCorner_ScansNearFree()
    {
        var alone = new List<double>();
        var withEnemy = new List<double>();
        for (int r = 0; r < 3; r++)
        {
            alone.Add(Average(Blob(farEnemy: false), 300));
            withEnemy.Add(Average(Blob(farEnemy: true), 300));
        }
        double a = alone.Average(), e = withEnemy.Average();
        _out.WriteLine($"2,500 one-player blob: alone {string.Join(" / ", alone.Select(x => x.ToString("F2")))} ms, one far enemy {string.Join(" / ", withEnemy.Select(x => x.ToString("F2")))} ms; delta {e - a:+0.000;-0.000} ms");

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
}
