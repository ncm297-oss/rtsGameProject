using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>M4-1 criterion 8: what melee combat costs a tick (Debug, timed one tick at a time).</summary>
[Collection(SerialCollection.Name)]
public class CombatPerfTests
{
    private const double BudgetMs = 4.0;
    private readonly ITestOutputHelper _out;

    public CombatPerfTests(ITestOutputHelper output) => _out = output;

    private static double[] Time(Simulation sim, int ticks)
    {
        var ms = new double[ticks];
        long freq = Stopwatch.Frequency;
        for (int t = 0; t < ticks; t++)
        {
            long start = Stopwatch.GetTimestamp();
            sim.Tick();
            ms[t] = (Stopwatch.GetTimestamp() - start) * 1000.0 / freq;
        }
        return ms;
    }

    private static int CountState(UnitStore u, UnitState s)
    {
        int n = 0;
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.State[i] == s) n++;
        return n;
    }

    /// <summary>
    /// 500 Heavy Infantry against 500 Raiders on a flat 128 x 128 map, two battle lines 10 ranks deep and 50 m wide, 8 m
    /// apart, attack-moved into each other: the first 200 ticks after a 5-tick warm-up (the closing, the clash and the first
    /// deaths) average under 4 ms; ticks 205-605 are reported.
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void Brawl500v500_First200Ticks_AverageUnder4Ms()
    {
        (double avg, string line) = Brawl(ranks: 10);
        _out.WriteLine(line);
        Assert.True(avg < BudgetMs, $"avg {avg:F3} ms over the {BudgetMs} ms budget");
    }

    /// <summary>
    /// Reported, not asserted: the same armies as columns 25 ranks deep and 20 m wide, where most units stand pressing
    /// behind their own planted front rank; movement, not combat, is most of that tick.
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void Brawl500v500_DeepColumns_Report()
    {
        (_, string line) = Brawl(ranks: 25);
        _out.WriteLine(line);
    }

    private static (double First200Avg, string Line) Brawl(int ranks)
    {
        Simulation sim = CombatScenes.Flat(size: 128, units: 1000);
        CombatScenes.FlatBrawl(sim, 500, new Vector2(128f, 128f), gap: 8f, ranks: ranks);
        for (int t = 0; t < 5; t++) sim.Tick();
        double[] first = Time(sim, 200);
        World w = sim.World;
        int attacking = CountState(w.Units, UnitState.Attacking), kills = w.Kills[0] + w.Kills[1];
        double[] later = Time(sim, 400);
        double avg = first.Average();
        string line = $"500 v 500 brawl, {ranks} ranks deep, ticks 5-205: avg {avg:F3} ms, worst {first.Max():F3} ms ({attacking} Attacking, {kills} dead at tick 205); "
            + $"ticks 205-605: avg {later.Average():F3} ms, worst {later.Max():F3} ms ({w.Kills[0] + w.Kills[1]} dead, {w.Units.Count} alive)";
        Assert.True(w.Kills[0] + w.Kills[1] > 0, "nobody died: not a brawl");
        return (avg, line);
    }

    /// <summary>
    /// The chase question (brief): 250 Heavy Infantry attack-moving after 250 Crossbowmen (faster, and not fighting until
    /// M4-2) fleeing across a flat map. Reported: tick cost, flow-field builds, and how many chasers stand waiting for a
    /// field on an average tick; guarded at 4 ms.
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void Chase250After250_Report()
    {
        Simulation sim = CombatScenes.Flat(size: 128, units: 500);
        UnitStore u = sim.World.Units;
        for (int k = 0; k < 250; k++)
        {
            CombatScenes.Place(sim, 0, CombatScenes.HeavyInfantry, new Vector2(60f + k % 25 * 1.2f, 100f + k / 25 * 1.2f));
            CombatScenes.Place(sim, 1, CombatScenes.Crossbowman, new Vector2(92f + k % 25 * 1.2f, 100f + k / 25 * 1.2f));
        }
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            var h = new EntityHandle(i, u.Generation[i]);
            // Each crossbowman runs to its own point, so the chasers' targets spread over many cells.
            sim.Enqueue(u.Owner[i] == 0 ? Command.AttackMove(0, h, new Vector2(240f, 110f)) : Command.Move(1, h, new Vector2(240f, 20f + (i % 50) * 4f)));
        }
        for (int t = 0; t < 5; t++) sim.Tick();
        int builds0 = sim.World.FlowFields.BuildCount;
        long waiting = 0, chasing = 0;
        var ms = new double[300];
        long freq = Stopwatch.Frequency;
        for (int t = 0; t < ms.Length; t++)
        {
            long start = Stopwatch.GetTimestamp();
            sim.Tick();
            ms[t] = (Stopwatch.GetTimestamp() - start) * 1000.0 / freq;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i] || u.Owner[i] != 0 || u.Target[i] == default) continue;
                chasing++;
                if (u.State[i] == UnitState.Moving && u.Velocity[i] == Vector2.Zero) waiting++;
            }
        }
        int builds = sim.World.FlowFields.BuildCount - builds0;
        _out.WriteLine($"250 chasers after 250 fleeing, 300 ticks: avg {ms.Average():F3} ms, worst {ms.Max():F3} ms; {builds} field builds ({builds / 300.0:F2} a tick); "
            + $"chaser-ticks with a target {chasing}, of them standing still while Moving {waiting} ({(chasing == 0 ? 0 : 100.0 * waiting / chasing):F1}%)");
        Assert.True(chasing > 0);
        Assert.True(ms.Average() < BudgetMs);
    }
}
