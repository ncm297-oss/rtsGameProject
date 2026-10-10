using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA M4-4b-3b (session 2026-10-10-1314): summons at scale. 80 Cadre Mages casting the 2 s summon (3 Heavy Infantry each)
/// on cooldown keep 200+ summoned units live, with dozens expiring and appearing every few ticks: the ticks allocate 0 B,
/// and the cost per tick is reported against the same world with no casts. Combat on, one player (no fights), so the
/// difference is the summon work and the extra units.
/// </summary>
[Collection(SerialCollection.Name)]
public class SummonScaleQaTests
{
    private const int Brief = 2;
    private readonly ITestOutputHelper _out;

    public SummonScaleQaTests(ITestOutputHelper output) => _out = output;

    private static Simulation Field(out int mage)
    {
        GameData d = SummonTests.Data;
        mage = d.FindUnit("malazan_cadre_mage");
        var sim = new Simulation(new SimConfig(1, 2, 400, 4096) { Data = d }, LocalMovementTests.Flat(128));
        for (int k = 0; k < 80; k++)
            CombatScenes.Place(sim, 0, mage, CombatScenes.At(sim, 10 + k % 10 * 9, 10 + k / 10 * 9));
        return sim;
    }

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

    /// <summary>With 200+ summoned units live and some expiring or appearing in the measured window, the ticks allocate nothing.</summary>
    [Fact]
    public void TwoHundredSummonsLiveAndExpiring_AllocateNothing()
    {
        Simulation sim = Field(out int mage);
        World w = sim.World;
        for (int t = 0; t < 30; t++)
        {
            Cast(sim, mage);
            sim.Tick();
        }
        Assert.True(w.Units.SummonedUnits >= 200, $"{w.Units.SummonedUnits} live after warm-up");
        int expired = 0, minLive = int.MaxValue, maxLive = 0;
        Action ticks = () =>
        {
            for (int t = 0; t < 100; t++)
            {
                Cast(sim, mage);
                sim.Tick();
                foreach (Combat.DeathEvent e in w.Deaths) if (e.Expired) expired++;
                minLive = Math.Min(minLive, w.Units.SummonedUnits);
                maxLive = Math.Max(maxLive, w.Units.SummonedUnits);
            }
        };
        AllocationProbe.AssertZero(ticks, _out);
        _out.WriteLine($"{expired} expiries; live summons {minLive}-{maxLive}");
        Assert.True(expired >= 200 && maxLive >= 200, $"{expired} expiries, at most {maxLive} live");
    }

    /// <summary>Reported, not gated: the per-tick cost with 80 Mages summoning on cooldown (240 live at peak) against the same field with no casts.</summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void SummonField_CostPerTick_Reported()
    {
        double Measure(bool cast)
        {
            Simulation sim = Field(out int mage);
            for (int t = 0; t < 30; t++) { if (cast) Cast(sim, mage); sim.Tick(); }
            var ms = new double[300];
            for (int t = 0; t < ms.Length; t++)
            {
                if (cast) Cast(sim, mage);
                long s = Stopwatch.GetTimestamp();
                sim.Tick();
                ms[t] = (Stopwatch.GetTimestamp() - s) * 1000.0 / Stopwatch.Frequency;
            }
            Array.Sort(ms);
            return ms[ms.Length / 2];
        }
        Measure(true); // warm
        double quiet = Measure(false), busy = Measure(true);
        _out.WriteLine($"80 Mages, median tick: no casts {quiet:F3} ms; summoning on cooldown (up to 240 summons live) {busy:F3} ms");
        Assert.True(busy < 4.0, $"{busy:F3} ms median with 240 summons");
    }
}
