using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-4b-3b criterion 7: what summons cost. A world with no summoned unit pays a counter test for the expiry pass; a 500-unit
/// brawl whose 20 Cadre Mages summon 3 Heavy Infantry each on cooldown (<c>zz_test_summon_brief</c>: 2 s lifetime, so up to
/// 60 summons live and expiring all the time) allocates nothing a tick and stays inside the brawl rows' 4 ms budget.
/// </summary>
[Collection(SerialCollection.Name)]
public class SummonPerfTests
{
    private const double BudgetMs = 4.0; // the brawl rows' budget (CombatPerfTests, PassivePerfTests)
    private const int Brief = 2;         // the Mage's ability 2 in SummonTests.Data
    private readonly ITestOutputHelper _out;

    public SummonPerfTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// 250 of player 0 (20 Cadre Mages in the back ranks, 230 Heavy Infantry) against 250 Raiders, in two blocks 10 ranks
    /// deep, 1 m apart, 8 m between the front ranks, attack-moved into each other; room for 60 summons on top.
    /// </summary>
    private static Simulation SummonBrawl()
    {
        const int size = 128, ranks = 10, perSide = 250;
        GameData d = SummonTests.Data;
        var sim = new Simulation(new SimConfig(1, 2, 560, 4096) { Data = d }, LocalMovementTests.Flat(size));
        var centre = new Vector2(size, size);
        int mage = d.FindUnit("malazan_cadre_mage"), heavy = d.FindUnit("malazan_heavy_infantry"), raider = d.FindUnit("whirlwind_raider");
        var mid = new Vector2[2];
        int mages = 0;
        for (int side = 0; side < 2; side++)
        {
            float dir = side == 0 ? -1f : 1f;
            int rows = perSide / ranks;
            mid[side] = centre + new Vector2(dir * (8f / 2 + (ranks - 1) * 0.5f), 0f);
            for (int k = 0; k < perSide; k++)
            {
                int col = k % ranks, row = k / ranks;
                var at = centre + new Vector2(dir * (8f / 2 + col * 1f), (row - (rows - 1) / 2f) * 1f);
                int type = side == 1 ? raider : col == ranks - 1 && mages < 20 ? mage : heavy;
                if (type == mage) mages++;
                CombatScenes.Place(sim, side, type, at);
            }
        }
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.TypeId[i] != mage) sim.Enqueue(Command.AttackMove(u.Owner[i], new EntityHandle(i, u.Generation[i]), mid[1 - u.Owner[i]]));
        return sim;
    }

    /// <summary>Every live Mage whose brief summon is ready and that isn't casting casts it where it stands.</summary>
    private static void Cast(Simulation sim, int mage)
    {
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.TypeId[i] != mage || u.CastAbility[i] >= 0) continue;
            if (sim.TickNumber + 1 < u.AbilityReadyTick[i * DataLimits.MaxUnitAbilities + Brief]) continue;
            sim.Enqueue(Command.UseAbility(0, new EntityHandle(i, u.Generation[i]), Brief, u.Position[i]));
        }
    }

    [Fact]
    public void TheSummonBrawl_Has500Units_And20Mages()
    {
        Simulation sim = SummonBrawl();
        Assert.Equal(500, sim.World.Units.Count);
        int mage = sim.World.Data.FindUnit("malazan_cadre_mage"), n = 0;
        for (int i = 0; i < sim.World.Units.Capacity; i++)
            if (sim.World.Units.Alive[i] && sim.World.Units.TypeId[i] == mage) n++;
        Assert.Equal(20, n);
    }

    /// <summary>The brawl's ticks with summons cast, live and expiring (and some killed) allocate nothing.</summary>
    [Fact]
    public void SummonBrawlTicks_WithSummonsLiveAndExpiring_AllocateNothing()
    {
        Simulation sim = SummonBrawl();
        World w = sim.World;
        int mage = w.Data.FindUnit("malazan_cadre_mage");
        for (int t = 0; t < 30; t++) // the first casts resolve near tick 11 and expire near tick 51
        {
            Cast(sim, mage);
            sim.Tick();
        }
        Assert.True(w.Units.SummonedUnits > 0, "nothing summoned in the warm-up");
        int expired = 0, maxLive = 0;
        Action ticks = () =>
        {
            for (int t = 0; t < 100; t++)
            {
                Cast(sim, mage);
                sim.Tick();
                foreach (Combat.DeathEvent e in w.Deaths) if (e.Expired) expired++;
                if (w.Units.SummonedUnits > maxLive) maxLive = w.Units.SummonedUnits;
            }
        };
        AllocationProbe.AssertZero(ticks, _out);
        _out.WriteLine($"{expired} expiries, at most {maxLive} summons live, {w.Units.Count} units");
        Assert.True(expired > 0, "no summon expired inside the measured ticks");
        Assert.True(maxLive >= 30, $"at most {maxLive} summons live");
    }

    /// <summary>
    /// The brawl with summons: ticks 5-205 average under the brawl rows' 4 ms (casts every 3 s per Mage, 2 s lifetimes:
    /// summons appear and expire throughout), ticks 205-605 reported.
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void SummonBrawl500_First200Ticks_AverageUnder4Ms()
    {
        Simulation sim = SummonBrawl();
        World w = sim.World;
        int mage = w.Data.FindUnit("malazan_cadre_mage");
        for (int t = 0; t < 5; t++) sim.Tick();
        var first = new double[200];
        var later = new double[400];
        long freq = Stopwatch.Frequency;
        int expired = 0, maxLive = 0;
        for (int t = 0; t < first.Length + later.Length; t++)
        {
            Cast(sim, mage);
            long start = Stopwatch.GetTimestamp();
            sim.Tick();
            double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / freq;
            if (t < first.Length) first[t] = ms; else later[t - first.Length] = ms;
            foreach (Combat.DeathEvent e in w.Deaths) if (e.Expired) expired++;
            maxLive = Math.Max(maxLive, w.Units.SummonedUnits);
        }
        double avg = first.Average();
        _out.WriteLine($"500-unit brawl with 20 summoners, ticks 5-205: avg {avg:F3} ms, worst {first.Max():F3} ms; ticks 205-605: avg {later.Average():F3} ms, "
            + $"worst {later.Max():F3} ms; {expired} expiries, at most {maxLive} summons live, {w.Kills[0] + w.Kills[1]} killed");
        Assert.True(expired > 0 && maxLive >= 30, $"{expired} expiries, at most {maxLive} live: not a summon row");
        Assert.True(avg < BudgetMs, $"avg {avg:F3} ms over the {BudgetMs} ms budget");
    }

    /// <summary>With 2,500 units and no summoned one, phase 5's expiry pass is the counter test alone: 10,000 calls well under a millisecond, nothing allocated.</summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void NoSummonedUnits_TheExpiryPassIsACounterTest_AndAllocatesNothing()
    {
        var sim = new Simulation(TestSim.ConfigNoCombat(Seed: 1, PlayerCount: 2, UnitCapacity: 2500, CommandCapacity: 64), LocalMovementTests.Flat(128));
        for (int k = 0; k < 2500; k++)
            CombatScenes.Place(sim, k % 2, k % 2 == 0 ? CombatScenes.Raider : CombatScenes.HeavyInfantry, CombatScenes.At(sim, 4 + k % 50 * 2, 4 + k / 50 * 2));
        sim.Tick();
        Assert.Equal(0, sim.World.Units.SummonedUnits);
        ulong h = sim.StateHash();
        World w = sim.World;
        Action pass = () => { for (int k = 0; k < 10_000; k++) SummonSystem.Run(w); };
        AllocationProbe.AssertZero(pass, _out);
        var sw = Stopwatch.StartNew();
        pass();
        double ms = sw.Elapsed.TotalMilliseconds;
        _out.WriteLine($"10,000 expiry passes over 2,500 slots with no summoned unit: {ms:F3} ms ({ms * 100:F3} ns a pass)");
        Assert.Equal(h, sim.StateHash());
        Assert.True(ms < 1.0, $"{ms:F3} ms for 10,000 passes: the pass visits slots with no summoned unit");
    }
}
