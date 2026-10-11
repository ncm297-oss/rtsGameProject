using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-5 criterion 7: what stealth costs. A world with no stealthed unit pays a counter test for the detection pass (and on
/// every vision check); a 500-unit brawl with 50 stealthed Heavy Infantry among player 0's ranks and 10 detecting Laborers
/// behind player 1's allocates nothing a tick and stays inside the brawl rows' 4 ms budget.
/// </summary>
[Collection(SerialCollection.Name)]
public class StealthPerfTests
{
    private const double BudgetMs = 4.0; // the brawl rows' budget (CombatPerfTests, SummonPerfTests)
    private readonly ITestOutputHelper _out;

    public StealthPerfTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// 250 Heavy Infantry of player 0 (every fifth one stealthed for the whole scene: 50) against 240 Raiders and 10 Laborers
    /// (10 m detectors, in the back rank) of player 1, in two blocks 10 ranks deep, 1 m apart, 8 m between the front ranks,
    /// the fighters attack-moved into each other.
    /// </summary>
    private static Simulation StealthBrawl()
    {
        const int size = 128, ranks = 10, perSide = 250;
        GameData d = StealthTests.Data;
        var sim = new Simulation(new SimConfig(1, 2, 520, 4096) { Data = d }, LocalMovementTests.Flat(size));
        var centre = new Vector2(size, size);
        int heavy = d.FindUnit("malazan_heavy_infantry"), raider = d.FindUnit("whirlwind_raider"), laborer = d.FindUnit("malazan_laborer");
        var mid = new Vector2[2];
        int detectors = 0, stealthed = 0;
        for (int side = 0; side < 2; side++)
        {
            float dir = side == 0 ? -1f : 1f;
            int rows = perSide / ranks;
            mid[side] = centre + new Vector2(dir * (8f / 2 + (ranks - 1) * 0.5f), 0f);
            for (int k = 0; k < perSide; k++)
            {
                int col = k % ranks, row = k / ranks;
                var at = centre + new Vector2(dir * (8f / 2 + col * 1f), (row - (rows - 1) / 2f) * 1f);
                int type = side == 0 ? heavy : col == ranks - 1 && row % 2 == 0 && detectors < 10 ? laborer : raider;
                if (type == laborer) detectors++;
                EntityHandle h = CombatScenes.Place(sim, side, type, at);
                if (side == 0 && k % 5 == 0)
                {
                    StealthTests.Stealth(sim, h);
                    stealthed++;
                }
            }
        }
        Assert.Equal((10, 50), (detectors, stealthed));
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.TypeId[i] != laborer) sim.Enqueue(Command.AttackMove(u.Owner[i], new EntityHandle(i, u.Generation[i]), mid[1 - u.Owner[i]]));
        return sim;
    }

    [Fact]
    public void TheStealthBrawl_Has500Units_50Stealthed_10Detectors()
    {
        Simulation sim = StealthBrawl();
        Assert.Equal(500, sim.World.Units.Count);
        Assert.Equal(50, sim.World.Units.Statuses.StealthedUnits);
    }

    /// <summary>The brawl's ticks with stealthed units hidden, detected, revealed by their hits and dying allocate nothing.</summary>
    [Fact]
    public void StealthBrawlTicks_AllocateNothing()
    {
        Simulation sim = StealthBrawl();
        World w = sim.World;
        for (int t = 0; t < 40; t++) sim.Tick();
        int detected = 0, hidden = 0;
        Action ticks = () =>
        {
            for (int t = 0; t < 100; t++)
            {
                sim.Tick();
                for (int j = 0; j < w.Units.Capacity; j++)
                {
                    if (!w.IsStealthed(j)) continue;
                    if (w.IsDetectedBy(1, j)) detected++; else hidden++;
                }
            }
        };
        AllocationProbe.AssertZero(ticks, _out);
        _out.WriteLine($"{detected} detected and {hidden} hidden stealthed unit-ticks, {w.Units.Statuses.StealthedUnits} carriers left, {w.Units.Count} units");
        Assert.True(detected > 0 && hidden > 0, $"{detected} detected, {hidden} hidden: not a stealth row");
    }

    /// <summary>The stealth brawl: ticks 5-205 average under the brawl rows' 4 ms; ticks 205-605 reported.</summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void StealthBrawl500_First200Ticks_AverageUnder4Ms()
    {
        Simulation sim = StealthBrawl();
        World w = sim.World;
        for (int t = 0; t < 5; t++) sim.Tick();
        var first = new double[200];
        var later = new double[400];
        long freq = Stopwatch.Frequency;
        for (int t = 0; t < first.Length + later.Length; t++)
        {
            long start = Stopwatch.GetTimestamp();
            sim.Tick();
            double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / freq;
            if (t < first.Length) first[t] = ms; else later[t - first.Length] = ms;
        }
        double avg = first.Average();
        _out.WriteLine($"500-unit brawl with 50 stealthed and 10 detectors, ticks 5-205: avg {avg:F3} ms, worst {first.Max():F3} ms; ticks 205-605: avg {later.Average():F3} ms, "
            + $"worst {later.Max():F3} ms; {w.Units.Statuses.StealthedUnits} carriers left, {w.Kills[0] + w.Kills[1]} killed");
        Assert.True(avg < BudgetMs, $"avg {avg:F3} ms over the {BudgetMs} ms budget");
    }

    /// <summary>With 2,500 units and no stealthed one, phase 5's detection pass is the counter test alone: 10,000 calls well under a millisecond, nothing allocated.</summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void NoStealthedUnits_TheDetectionPassIsACounterTest_AndAllocatesNothing()
    {
        var sim = new Simulation(TestSim.ConfigNoCombat(Seed: 1, PlayerCount: 2, UnitCapacity: 2500, CommandCapacity: 64), LocalMovementTests.Flat(128));
        for (int k = 0; k < 2500; k++)
            CombatScenes.Place(sim, k % 2, k % 2 == 0 ? CombatScenes.Raider : CombatScenes.HeavyInfantry, CombatScenes.At(sim, 4 + k % 50 * 2, 4 + k / 50 * 2));
        sim.Tick();
        Assert.Equal(0, sim.World.Units.Statuses.StealthedUnits);
        ulong h = sim.StateHash();
        World w = sim.World;
        Action pass = () => { for (int k = 0; k < 10_000; k++) StealthSystem.Run(w); };
        AllocationProbe.AssertZero(pass, _out);
        var sw = Stopwatch.StartNew();
        pass();
        double ms = sw.Elapsed.TotalMilliseconds;
        _out.WriteLine($"10,000 detection passes over 2,500 slots with no stealthed unit: {ms:F3} ms ({ms * 100:F3} ns a pass)");
        Assert.Equal(h, sim.StateHash());
        Assert.True(ms < 1.0, $"{ms:F3} ms for 10,000 passes: the pass visits slots with no stealthed unit");
    }
}
