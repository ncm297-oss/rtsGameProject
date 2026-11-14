using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D31 scope item 6: the shipped Blood-oil Frenzy in a combat scene, on the shipped data (<see cref="TestSim.Data"/>, no
/// copy): a Teblor Shaman casts; a Warrior 4 m from it, hitting a Battering Ram (a million hit points; it hits buildings
/// only, so it never answers), strikes 20% harder (22 melee x 1.2 = 26.4, rounded to 26, less the Ram's 6 armor = 20,
/// against 16) and 40% faster (the 1.8 s = 36-tick cooldown over 1.4 = 26 ticks) for 10 s (200 ticks), 14 s (280) once
/// Blood-oil Anointing is researched, then is Slowed 20% for 4 s (80 ticks: its speed x 0.8) and then plays as before; a
/// second Warrior 10 m from the Shaman (outside the 8 m aura, edge to centre 9.25 m), hitting its own Ram, is untouched
/// throughout: no status, 16 a blow, a blow every 36 ticks. After its cast the Shaman walks away, so only the Warriors hit
/// the Rams.
/// </summary>
public class TeblorFrenzyShippedSceneTests
{
    private const int Tps = SimConstants.TicksPerSecond;

    private sealed record Scene(Simulation Sim, EntityHandle Shaman, EntityHandle Inside, EntityHandle Outside, EntityHandle RamIn, EntityHandle RamOut);

    private static Scene Build(bool anointed)
    {
        GameData d = TestSim.Data;
        var sim = new Simulation(new SimConfig(1, 2, 64, 8 * 64 + 32) { Data = d }, LocalMovementTests.Flat(48));
        World w = sim.World;
        EntityHandle shaman = BloodOilScenes.Place(sim, 0, "teblor_shaman", At(sim, 10, 20));
        EntityHandle inside = BloodOilScenes.Place(sim, 0, "teblor_warrior", At(sim, 12, 20));   // 4 m
        EntityHandle ramIn = BloodOilScenes.Place(sim, 1, "whirlwind_battering_ram", At(sim, 13, 20));
        EntityHandle outside = BloodOilScenes.Place(sim, 0, "teblor_warrior", At(sim, 10, 25));  // 10 m
        EntityHandle ramOut = BloodOilScenes.Place(sim, 1, "whirlwind_battering_ram", At(sim, 10, 26));
        w.Units.Hp[ramIn.Index] = 1_000_000;
        w.Units.Hp[ramOut.Index] = 1_000_000;
        if (anointed) w.Techs.Set(0, d.FindTech("teblor_blood_oil_anointing"), true);
        w.Fog.Update();
        sim.Enqueue(Command.Attack(0, inside, ramIn, false));
        sim.Enqueue(Command.Attack(0, outside, ramOut, false));
        return new Scene(sim, shaman, inside, outside, ramIn, ramOut);
    }

    private static float MagnitudeOf(World w, EntityHandle unit, int status)
    {
        int at = w.Units.Statuses.IndexOf(unit.Index, status);
        return at < 0 ? -1f : w.Units.Statuses.Magnitude[at];
    }

    private static int[] Gaps(List<int> ticks) => Enumerable.Range(1, Math.Max(0, ticks.Count - 1)).Select(k => ticks[k] - ticks[k - 1]).ToArray();

    [Theory]
    [InlineData(false, 200)] // 10 s
    [InlineData(true, 280)]  // 14 s: the Anointing's +4 s
    public void TheShippedFrenzy_InsideIsHarderAndFaster_ThenSlowed_OutsideUntouched(bool anointed, int frenzyTicks)
    {
        GameData d = TestSim.Data;
        int frenzy = d.FindStatus("teblor_frenzied"), slowed = d.FindStatus("slowed");
        Assert.True(frenzy >= 0, "the shipped data has no teblor_frenzied status");
        Assert.Equal(frenzy, d.Abilities[d.FindAbility("blood_oil_frenzy")].Effects[0].Status); // the shipped ability applies it
        Scene s = Build(anointed);
        Simulation sim = s.Sim;
        World w = sim.World;
        UnitStore u = w.Units;
        float baseSpeed = d.Units[d.FindUnit("teblor_warrior")].SpeedPerTick;

        // Let both Warriors settle into their swings, then the Shaman casts.
        for (int t = 0; t < 100; t++) sim.Tick();
        Assert.Equal(0, u.Statuses.Count[s.Inside.Index]);
        BloodOilScenes.Cast(sim, 0, s.Shaman);
        Assert.True(RunUntil(sim, () => BloodOilScenes.TicksOf(w, s.Inside, frenzy) > 0, 60) < 60, "the cast never reached the Warrior inside");
        Assert.Equal(frenzyTicks, BloodOilScenes.TicksOf(w, s.Inside, frenzy));
        Assert.Equal(0.4f, MagnitudeOf(w, s.Inside, frenzy));
        sim.Enqueue(Command.Move(0, s.Shaman, At(sim, 10, 2))); // out of reach of both Rams

        var hitsIn = new List<(int Tick, int Damage, bool On)>();
        var hitsOut = new List<(int Tick, int Damage)>();
        int onTicks = 1, frenzyEnd = -1, slowTicks = 0, slowEnd = -1, outsideStatusTicks = 0;
        for (int t = 0; t < frenzyTicks + 400; t++)
        {
            int inBefore = u.Hp[s.RamIn.Index], outBefore = u.Hp[s.RamOut.Index];
            sim.Tick();
            int now = sim.TickNumber;
            bool on = BloodOilScenes.TicksOf(w, s.Inside, frenzy) > 0;
            if (on) onTicks++;
            if (u.Hp[s.RamIn.Index] < inBefore) hitsIn.Add((now, inBefore - u.Hp[s.RamIn.Index], on));
            if (u.Hp[s.RamOut.Index] < outBefore) hitsOut.Add((now, outBefore - u.Hp[s.RamOut.Index]));
            if (u.Statuses.Count[s.Outside.Index] > 0) outsideStatusTicks++;
            if (!on && frenzyEnd < 0)
            {
                frenzyEnd = now;
                // The aftermath: Slowed 20% for 4 s, the speed x 0.8.
                Assert.Equal(80, BloodOilScenes.TicksOf(w, s.Inside, slowed));
                Assert.Equal(0.2f, MagnitudeOf(w, s.Inside, slowed));
                Assert.Equal(baseSpeed * 0.8f, u.Speed[s.Inside.Index]);
            }
            if (BloodOilScenes.TicksOf(w, s.Inside, slowed) > 0) slowTicks++;
            else if (frenzyEnd >= 0 && slowEnd < 0) slowEnd = now;
        }

        // 10 s (14 s anointed) of Frenzy, then 4 s of Slowed, then the Warrior's own speed again.
        Assert.Equal(frenzyTicks, onTicks);
        Assert.Equal(80, slowTicks);
        Assert.True(slowEnd > frenzyEnd, "the aftermath never ended");
        Assert.Equal(baseSpeed, u.Speed[s.Inside.Index]);
        Assert.Equal(0, u.Statuses.Count[s.Inside.Index]);

        // Inside: 20 a blow while Frenzied (16 otherwise), a blow every 26 ticks (after the first swing begun before the cast).
        Assert.All(hitsIn, h => Assert.Equal(h.On ? 20 : 16, h.Damage));
        List<int> onHits = hitsIn.Where(h => h.On).Select(h => h.Tick).ToList();
        int[] onGaps = Gaps(onHits).Skip(1).ToArray();
        Assert.True(onGaps.Length >= (anointed ? 8 : 5), $"only {onGaps.Length} Frenzied gaps");
        Assert.All(onGaps, g => Assert.Equal(26, g));
        List<int> afterHits = hitsIn.Where(h => h.Tick > frenzyEnd + 40).Select(h => h.Tick).ToList();
        Assert.True(afterHits.Count >= 3);
        Assert.All(Gaps(afterHits), g => Assert.Equal(36, g));

        // Outside the 8 m: nothing at all.
        Assert.Equal(0, outsideStatusTicks);
        Assert.True(hitsOut.Count >= 10);
        Assert.All(hitsOut, h => Assert.Equal(16, h.Damage));
        Assert.All(Gaps(hitsOut.Select(h => h.Tick).ToList()), g => Assert.Equal(36, g));
    }
}
