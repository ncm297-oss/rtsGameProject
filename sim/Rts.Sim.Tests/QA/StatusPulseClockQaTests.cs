using System.Collections.Generic;
using Rts.Sim.Abilities;
using Rts.Sim.Entities;
using static Rts.Sim.Tests.AbilityScenes;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M4-4a re-check, session 2026-10-09-0724): the BUG-0301 fix's pulse clock pinned tick by tick. Pulses land every
/// 20 ticks from the first application whatever the refresh cadence, a refresh never adds or skips a pulse, and a trailing
/// fraction of a second lands nothing (docs/03 "Damage over time").
/// </summary>
[Collection(SerialCollection.Name)]
public class StatusPulseClockQaTests
{
    /// <summary>Ticks (1-based, counted from the apply) on which the victim lost HP, applying Burning on the given ticks.</summary>
    private static List<int> PulseTicks(int run, System.Func<int, int> refreshTicksAt)
    {
        Simulation sim = NoFights();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle h = Place(sim, 1, HeavyInfantry, At(sim, 20, 20));
        u.Hp[h.Index] = 100_000;
        var hits = new List<int>();
        for (int t = 0; t < run; t++)
        {
            int ticks = refreshTicksAt(t);
            if (ticks > 0) StatusSystem.Apply(w, h.Index, Burning, 10f, ticks, 0);
            int before = u.Hp[h.Index];
            sim.Tick();
            if (u.Hp[h.Index] != before)
            {
                Assert.Equal(13, before - u.Hp[h.Index]); // 10 magic on Heavy rounds to 13
                hits.Add(t + 1);
            }
        }
        Assert.Equal(0, u.Statuses.Count[h.Index]);
        return hits;
    }

    [Fact]
    public void SingleApply_4s_PulsesOnTicks20_40_60_80()
        => Assert.Equal(new[] { 20, 40, 60, 80 }, PulseTicks(120, t => t == 0 ? 80 : 0));

    [Fact]
    public void RefreshHalfASecondIn_KeepsTheCadence_AndTheTrailingHalfSecondLandsNothing()
        // Applied at 0 for 80, refreshed at 10 for 80 (ends on tick 90): pulses stay on 20/40/60/80, none at 90.
        => Assert.Equal(new[] { 20, 40, 60, 80 }, PulseTicks(140, t => t == 0 || t == 10 ? 80 : 0));

    [Fact]
    public void ZoneLikeRefreshEveryTick_PulsesExactlyOnceASecond()
    {
        // Reapplied every tick for 200 ticks, then left to run out (80 more ticks).
        List<int> hits = PulseTicks(320, t => t < 200 ? 80 : 0);
        var expected = new List<int>();
        for (int k = 20; k <= 279; k += 20) expected.Add(k);
        Assert.Equal(expected, hits);
    }

    [Fact]
    public void A2Point5sBurning_LandsTwoPulses()
        => Assert.Equal(new[] { 20, 40 }, PulseTicks(100, t => t == 0 ? 50 : 0));

    [Fact]
    public void UnderASecond_LandsNothing()
        => Assert.Empty(PulseTicks(60, t => t == 0 ? 19 : 0));
}
