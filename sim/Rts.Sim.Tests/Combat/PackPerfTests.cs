using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M8-3 criteria 11 and 12: a 200 v 200 brawl of pack units (Heavy Infantry against Whirlwind Raiders, each type's
/// <c>nearbyOwn</c> passive counting its own kind, 8 m, in blocks 1 m apart: every blow counts a full pack) allocates nothing
/// a tick; and a report (measured, not pass / fail) of its tick cost with and without the passive.
/// </summary>
[Collection(SerialCollection.Name)]
public class PackPerfTests
{
    private readonly ITestOutputHelper _out;

    public PackPerfTests(ITestOutputHelper output) => _out = output;

    /// <summary>The brawl on <paramref name="data"/>: 200 a side, the front ranks 4 m apart across the middle of a 128-cell map.</summary>
    private static Simulation Brawl(GameData data)
    {
        var sim = new Simulation(new SimConfig(1, 2, 420, 4096) { Data = data }, LocalMovementTests.Flat(128));
        CombatScenes.FlatBrawl(sim, 200, new Vector2(128f, 128f), 4f);
        return sim;
    }

    /// <summary>Criterion 11: 100 ticks of the brawl in full contact, every blow working out its pack, allocate nothing.</summary>
    [Fact]
    public void PackBrawl200v200_Ticks_AllocateNothing()
    {
        Simulation sim = Brawl(HitBonusScenes.PackOnly);
        Assert.True(sim.World.HitBonusInData);
        for (int t = 0; t < 60; t++) sim.Tick(); // into contact
        int packed = 0;
        Action ticks = () =>
        {
            for (int t = 0; t < 100; t++)
            {
                sim.Tick();
                sim.StateHash();
            }
        };
        AllocationProbe.AssertZero(ticks, _out);
        World w = sim.World;
        for (int i = 0; i < w.Units.Capacity; i++)
        {
            if (!w.Units.Alive[i]) continue;
            UnitDef d = w.Data.Units[w.Units.TypeId[i]];
            if (HitFactor.PackCount(w, i, d.Passives[d.NearbyPassive]) == HitBonusScenes.PackStacks) packed++;
        }
        _out.WriteLine($"{w.Units.Count} units left, {packed} with a full pack; kills {string.Join("/", w.Kills.ToArray())}");
        Assert.True(packed > 100, $"only {packed} units with a full pack: not a pack brawl");
    }

    /// <summary>
    /// Criterion 12 (a report, not pass / fail; run it alone, rule 42): the brawl with the passive (radius 8) and without it
    /// (the shipped data: the same types, no passive), three alternating runs, ticks 5-405. Prints each run's average and worst
    /// tick and the outcome (the passive changes the fight, so the two runs differ in what is left).
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void Report_PackBrawl200v200_TickCost_WithAndWithoutThePassive()
    {
        var with = new List<double>();
        var without = new List<double>();
        for (int run = 0; run < 3; run++)
            foreach (bool pack in new[] { true, false })
            {
                Simulation sim = Brawl(pack ? HitBonusScenes.PackOnly : TestSim.Data);
                for (int t = 0; t < 5; t++) sim.Tick();
                long freq = Stopwatch.Frequency, total = 0, worst = 0;
                for (int t = 0; t < 400; t++)
                {
                    long start = Stopwatch.GetTimestamp();
                    sim.Tick();
                    long took = Stopwatch.GetTimestamp() - start;
                    total += took;
                    worst = Math.Max(worst, took);
                }
                double avg = total * 1000.0 / freq / 400, worstMs = worst * 1000.0 / freq;
                (pack ? with : without).Add(avg);
                _out.WriteLine($"run {run} {(pack ? "with the passive   " : "without the passive")}: avg {avg:F3} ms, worst {worstMs:F3} ms, {sim.World.Units.Count} units left");
            }
        _out.WriteLine($"mean of runs: with {with.Average():F3} ms, without {without.Average():F3} ms ({(with.Average() / without.Average() - 1) * 100:+0.0;-0.0} %)");
        // The helper alone: 10,000 factors of a unit in a full pack, against 10,000 status factors.
        Simulation last = Brawl(HitBonusScenes.PackOnly);
        for (int t = 0; t < 60; t++) last.Tick();
        World w = last.World;
        int slot = 0;
        while (!w.Units.Alive[slot]) slot++;
        var sw = Stopwatch.StartNew();
        float sum = 0f;
        for (int k = 0; k < 10_000; k++) sum += HitFactor.Of(w, slot);
        double helperUs = sw.Elapsed.TotalMilliseconds * 1000.0 / 10_000;
        _out.WriteLine($"HitFactor.Of for a unit in a pack: {helperUs:F3} us a call (factor {sum / 10_000:F2})");
    }
}
