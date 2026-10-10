using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-4b-3a criteria 3 and 7: what the passives cost. A world with no passive unit pays nothing for them (the phase-5 pass
/// is a counter test and allocates nothing); a 500-unit brawl with 100 Zealots (Frenzied swings, Martyrdom on every Zealot
/// death) stays inside the existing brawl rows' 4 ms budget and allocates nothing.
/// </summary>
[Collection(SerialCollection.Name)]
public class PassivePerfTests
{
    private const double BudgetMs = 4.0; // the brawl rows' budget (CombatPerfTests)
    private readonly ITestOutputHelper _out;

    public PassivePerfTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// 250 Heavy Infantry (player 0) against 150 Raiders and 100 Zealots (player 1), in two blocks 10 ranks deep, 1 m apart,
    /// <paramref name="gap"/> m between the front ranks, attack-moved into each other on a flat map.
    /// </summary>
    private static Simulation ZealotBrawl(int size, float gap, out Vector2 centre)
    {
        var sim = new Simulation(TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: 500, CommandCapacity: 4032), LocalMovementTests.Flat(size));
        centre = new Vector2(size, size);
        int zealot = sim.World.Data.FindUnit("whirlwind_zealot");
        const int ranks = 10, perSide = 250;
        var mid = new Vector2[2];
        for (int side = 0; side < 2; side++)
        {
            float dir = side == 0 ? -1f : 1f;
            int rows = perSide / ranks;
            mid[side] = centre + new Vector2(dir * (gap / 2 + (ranks - 1) * 0.5f), 0f);
            for (int k = 0; k < perSide; k++)
            {
                int col = k % ranks, row = k / ranks;
                var at = centre + new Vector2(dir * (gap / 2 + col * 1f), (row - (rows - 1) / 2f) * 1f);
                // Zealots in every second slot of the Whirlwind block's first eight ranks, so they die and Frenzy in the clash.
                int type = side == 0 ? CombatScenes.HeavyInfantry : col < 8 && (k / ranks + col) % 2 == 0 ? zealot : CombatScenes.Raider;
                CombatScenes.Place(sim, side, type, at);
            }
        }
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i]) sim.Enqueue(Command.AttackMove(u.Owner[i], new EntityHandle(i, u.Generation[i]), mid[1 - u.Owner[i]]));
        return sim;
    }

    [Fact]
    public void TheZealotBrawl_Has100Zealots()
    {
        Simulation sim = ZealotBrawl(128, 8f, out _);
        Assert.Equal(500, sim.World.Units.Count);
        Assert.Equal(100, sim.World.Units.BelowHpUnits);
    }

    /// <summary>The brawl's ticks in full swing (Frenzied swings, Zealot deaths and their Martyrdom) allocate nothing.</summary>
    [Fact]
    public void ZealotBrawlTicks_WithFrenzyAndMartyrdom_AllocateNothing()
    {
        Simulation sim = ZealotBrawl(128, 6f, out _);
        World w = sim.World;
        int zealot = w.Data.FindUnit("whirlwind_zealot"), frenzied = w.Data.FindStatus("frenzied");
        CombatScenes.RunUntil(sim, () => w.Units.BelowHpUnits <= 98, 3000);
        Assert.True(w.Units.BelowHpUnits <= 98, "no Zealot died");
        int deathsBefore = w.Units.BelowHpUnits, frenziedSeen = 0;
        Action ticks = () =>
        {
            for (int t = 0; t < 100; t++)
            {
                sim.Tick();
                for (int i = 0; i < w.Units.Capacity; i++)
                    if (w.Units.Alive[i] && w.Units.TypeId[i] == zealot && w.Units.Statuses.IndexOf(i, frenzied) >= 0) frenziedSeen++;
            }
        };
        AllocationProbe.AssertZero(ticks, _out);
        _out.WriteLine($"{frenziedSeen} Frenzied Zealot-ticks; Zealots {deathsBefore} -> {w.Units.BelowHpUnits}");
        Assert.True(frenziedSeen > 0, "no Zealot was Frenzied inside the measured ticks");
        Assert.True(w.Units.BelowHpUnits < deathsBefore, "no Zealot died inside the measured ticks");
    }

    /// <summary>
    /// 500 units, 100 of them Zealots, on the flat 128 x 128 map, 8 m apart: the first 200 ticks after a 5-tick warm-up
    /// average under the brawl rows' 4 ms; ticks 205-605 reported with the Zealot deaths and Frenzied counts.
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void ZealotBrawl500_First200Ticks_AverageUnder4Ms()
    {
        Simulation sim = ZealotBrawl(128, 8f, out _);
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
        _out.WriteLine($"500-unit brawl with 100 Zealots, ticks 5-205: avg {avg:F3} ms, worst {first.Max():F3} ms; ticks 205-605: avg {later.Average():F3} ms, "
            + $"worst {later.Max():F3} ms; {w.Kills[0] + w.Kills[1]} dead, {w.Units.BelowHpUnits} Zealots left");
        Assert.True(w.Units.BelowHpUnits < 100, "no Zealot died: not a brawl");
        Assert.True(avg < BudgetMs, $"avg {avg:F3} ms over the {BudgetMs} ms budget");
    }

    /// <summary>
    /// Criterion 3: with 2,500 units and no passive among them, phase 5's passive pass is the counter test alone: no state
    /// changes, nothing allocated, and 10,000 calls take well under a millisecond (it never visits a slot).
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void NoPassiveUnits_ThePassIsACounterTest_AndAllocatesNothing()
    {
        var sim = new Simulation(TestSim.ConfigNoCombat(Seed: 1, PlayerCount: 2, UnitCapacity: 2500, CommandCapacity: 64), LocalMovementTests.Flat(128));
        for (int k = 0; k < 2500; k++)
            CombatScenes.Place(sim, k % 2, k % 2 == 0 ? CombatScenes.Raider : CombatScenes.HeavyInfantry, CombatScenes.At(sim, 4 + k % 50 * 2, 4 + k / 50 * 2));
        sim.Tick();
        Assert.Equal(0, sim.World.Units.BelowHpUnits);
        ulong h = sim.StateHash();
        World w = sim.World;
        Action pass = () => { for (int k = 0; k < 10_000; k++) PassiveSystem.Run(w); };
        AllocationProbe.AssertZero(pass, _out);
        var sw = Stopwatch.StartNew();
        pass();
        double ms = sw.Elapsed.TotalMilliseconds;
        _out.WriteLine($"10,000 passive passes over 2,500 slots with no passive unit: {ms:F3} ms ({ms * 100:F3} ns a pass)");
        Assert.Equal(h, sim.StateHash());
        Assert.True(ms < 1.0, $"{ms:F3} ms for 10,000 passes: the pass visits slots with no passive unit");
    }
}
