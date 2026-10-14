using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using static Rts.Sim.Tests.AbilityScenes;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-4b-3b criterion 1 (BUG-0421): an <c>hp</c> tech raises hit points. A unit spawns with its type's hp plus its owner's hp
/// bonus; when the tech lands, every live unit of the owner whose maximum it changes gains the change to its current hit
/// points at once, so maximum and current move together (docs/02 "Techs"). Dryjhna's Prophecy: Zealot +20 (63 to 83).
/// </summary>
[Collection(SerialCollection.Name)]
public class HpTechTests
{
    private static int Zealot => TestSim.Data.FindUnit("whirlwind_zealot");

    private static int Prophecy => TestSim.Data.FindTech("dryjhnas_prophecy");

    private static int Frenzied => TestSim.Data.FindStatus("frenzied");

    /// <summary>
    /// A flat 40 x 30 map, combat off: player 1 (Whirlwind) with a Smithy at (32, 4), Age II, and gold and wood for
    /// Dryjhna's Prophecy (researched there in 45 s).
    /// </summary>
    private static Simulation ResearchScene(out int smithy)
    {
        Simulation sim = BuildMaps.NewSim(ResourceMaps.Flat(40, 30), units: 64, players: 2, combat: false);
        TestSim.KeepIn(sim, 1); // M5-4e: a unit keeps the player in (a House, Forge, depot, tower or site alone no longer does)
        smithy = GatherMaps.Building(sim, 32, 4, player: 1, type: ResearchMaps.Smithy).Index;
        BuildMaps.SetTotals(sim, 1, 5000, 5000);
        sim.World.Techs.Set(1, ResearchMaps.AgeII, true); // the faction upgrade needs Age II (M3-6); Age II has no effects
        return sim;
    }

    /// <summary>
    /// The regression (fails before the fix): Prophecy researched for real. Until it lands the Zealots read 63 and 41 of 63;
    /// the tick it lands they read 83 / 83 and 61 / 83, and the hurt one is not Frenzied (61 is above the 41.5 line). Player
    /// 0's Zealot and player 1's Raider are untouched.
    /// </summary>
    [Fact]
    public void ProphecyResearched_FullZealotReads83Of83_HurtOneReads61Of83AndIsNotFrenzied_OthersUntouched()
    {
        Simulation sim = ResearchScene(out int smithy);
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle full = Place(sim, 1, Zealot, At(sim, 10, 10));
        EntityHandle hurt = Place(sim, 1, Zealot, At(sim, 12, 10));
        EntityHandle theirs = Place(sim, 0, Zealot, At(sim, 10, 20));
        EntityHandle raider = Place(sim, 1, Raider, At(sim, 14, 10));
        u.Hp[hurt.Index] = 41;
        int raiderHp = w.Data.Units[Raider].Hp;
        sim.Enqueue(Command.Research(1, ProductionMaps.In(sim, smithy), Prophecy));
        int landed = -1;
        for (int t = 0; t < 2000 && landed < 0; t++)
        {
            sim.Tick();
            if (w.HasTech(1, Prophecy)) landed = sim.TickNumber - 1;
            else
            {
                Assert.Equal((63, 41), (u.Hp[full.Index], u.Hp[hurt.Index]));
                Assert.Equal(63, w.MaxHp(full.Index));
            }
        }
        Assert.True(landed > 0, "Prophecy never landed");
        Assert.Equal((83, 83), (u.Hp[full.Index], w.MaxHp(full.Index)));
        Assert.Equal((61, 83), (u.Hp[hurt.Index], w.MaxHp(hurt.Index)));
        Assert.Equal(0, u.Statuses.Count[hurt.Index]); // not below 41.5
        Assert.Equal((63, 63), (u.Hp[theirs.Index], w.MaxHp(theirs.Index)));
        Assert.Equal((raiderHp, raiderHp), (u.Hp[raider.Index], w.MaxHp(raider.Index)));
        for (int t = 0; t < 40; t++)
        {
            sim.Tick();
            Assert.Equal(0, u.Statuses.Count[hurt.Index]);
            Assert.Equal(61, u.Hp[hurt.Index]); // once: the change is applied when the tech lands, not every tick
        }
    }

    /// <summary>A Zealot spawned after the tech (the dev spawn command, and the store's spawn every path uses) starts at 83; another player's still at 63.</summary>
    [Fact]
    public void AZealotSpawnedAfterTheTech_Reads83_AnotherPlayersReads63()
    {
        Simulation sim = NoFights();
        World w = sim.World;
        UnitStore u = w.Units;
        ProductionSystem.Researched(w, 1, Prophecy);
        EntityHandle placed = Place(sim, 1, Zealot, At(sim, 10, 10));
        Assert.Equal((83, 83), (u.Hp[placed.Index], w.MaxHp(placed.Index)));
        sim.Enqueue(Command.SpawnUnit(1, Zealot, At(sim, 20, 20)));
        sim.Enqueue(Command.SpawnUnit(0, Zealot, At(sim, 30, 20)));
        sim.Tick();
        sim.Tick(); // a command applies in the tick after the one it was queued before
        int ours = -1, theirs = -1;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || i == placed.Index) continue;
            if (u.Owner[i] == 1) ours = i; else theirs = i;
        }
        Assert.Equal(83, u.Hp[ours]);
        Assert.Equal(63, u.Hp[theirs]);
        Assert.Equal(0, u.Statuses.Count[ours]);
    }

    /// <summary>A unit killed before the tech lands stays dead (its slot isn't touched); one taken to exactly 0 by a hit on the landing tick, after the landing, dies of it (the change didn't save a unit already gone, nor resurrect one).</summary>
    [Fact]
    public void DeadUnits_AreNotHealed_ByTheLanding()
    {
        Simulation sim = NoFights();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle gone = Place(sim, 1, Zealot, At(sim, 10, 10));
        EntityHandle doomed = Place(sim, 1, Zealot, At(sim, 14, 10));
        sim.Tick();
        CombatSystem.HitUnit(w, new PendingHit(default, 0, gone, false, 63));
        Assert.False(u.IsAlive(gone));
        int count = u.Count;
        ProductionSystem.Researched(w, 1, Prophecy); // as phase 3 of the next tick
        Assert.False(u.IsAlive(gone));
        Assert.Equal(0, u.Hp[gone.Index]);
        Assert.Equal(count, u.Count);
        Assert.Equal(83, u.Hp[doomed.Index]);
        // Later in that tick (phase 11) a hit of its new full hit points takes it to exactly 0: dead, a loss counted.
        int losses = w.Losses[1];
        CombatSystem.HitUnit(w, new PendingHit(default, 0, doomed, false, 83));
        Assert.False(u.IsAlive(doomed));
        Assert.Equal(losses + 1, w.Losses[1]);
        sim.Tick();
        Assert.False(u.IsAlive(doomed));
        Assert.Equal(0, u.Hp[doomed.Index]);
    }

    /// <summary>
    /// A unit taken to 0 by a Burning pulse in phase 5 of the very tick the tech lands (phase 3) is dead after that tick: the
    /// landing comes first and the pulse kills it anyway. The landing tick is found from a twin, then the burn timed onto it.
    /// </summary>
    [Fact]
    public void AUnitBurnedTo0OnTheLandingTick_IsDead()
    {
        int landed = LandingTick(burnAt: -1, out _);
        int pulseDelay = FirstPulseDelay();
        int burnAt = landed - pulseDelay;
        Assert.True(burnAt > 0);
        LandingTick(burnAt, out (bool Alive, int Hp, int DeathTick) victim);
        Assert.False(victim.Alive);
        Assert.Equal(0, victim.Hp);
        Assert.Equal(landed, victim.DeathTick);
    }

    // Runs the research scene with a Zealot of player 1 that a deadly Burning is put on between ticks just before tick
    // burnAt (-1 never); returns the tick Prophecy landed in and the Zealot's fate.
    private static int LandingTick(int burnAt, out (bool Alive, int Hp, int DeathTick) victim)
    {
        Simulation sim = ResearchScene(out int smithy);
        World w = sim.World;
        EntityHandle z = Place(sim, 1, Zealot, At(sim, 10, 10));
        sim.Enqueue(Command.Research(1, ProductionMaps.In(sim, smithy), Prophecy));
        int landed = -1, death = -1;
        for (int t = 0; t < 2000 && (landed < 0 || t < burnAt + 60); t++)
        {
            if (sim.TickNumber == burnAt) StatusSystem.Apply(w, z.Index, Burning, 100_000f, 20, 0);
            sim.Tick();
            if (landed < 0 && w.HasTech(1, Prophecy)) landed = sim.TickNumber - 1;
            if (death < 0 && !w.Units.IsAlive(z)) death = sim.TickNumber - 1;
        }
        victim = (w.Units.IsAlive(z), w.Units.Hp[z.Index], death);
        return landed;
    }

    // Ticks from a Burning put on between ticks (before tick T) to the tick of its first pulse.
    private static int FirstPulseDelay()
    {
        Simulation sim = NoFights();
        EntityHandle z = Place(sim, 1, Zealot, At(sim, 10, 10));
        for (int t = 0; t < 5; t++) sim.Tick();
        int start = sim.TickNumber;
        StatusSystem.Apply(sim.World, z.Index, Burning, 100_000f, 20, 0);
        for (int t = 0; t < 40; t++)
        {
            sim.Tick();
            if (!sim.World.Units.IsAlive(z)) return sim.TickNumber - 1 - start;
        }
        throw new Xunit.Sdk.XunitException("the burn never pulsed");
    }

    /// <summary>A tech with no hp effect (Melee Weapons 1) changes no hit points; a tech researched twice changes them once.</summary>
    [Fact]
    public void ATechWithoutAnHpEffect_AndASecondLanding_ChangeNothing()
    {
        Simulation sim = NoFights();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle z = Place(sim, 1, Zealot, At(sim, 10, 10));
        EntityHandle hi = Place(sim, 0, HeavyInfantry, At(sim, 14, 10));
        int hiHp = u.Hp[hi.Index];
        ProductionSystem.Researched(w, 0, ResearchMaps.Melee1);
        ProductionSystem.Researched(w, 0, ResearchMaps.Armor1);
        Assert.Equal(hiHp, u.Hp[hi.Index]);
        ProductionSystem.Researched(w, 1, Prophecy);
        ProductionSystem.Researched(w, 1, Prophecy);
        Assert.Equal(83, u.Hp[z.Index]);
    }

    /// <summary>
    /// QA's passive row re-stated to the real rule (PassiveQaTests.ProphecyResearchedMidFight_*): Prophecy landing on a 41 / 63
    /// Zealot makes it 61 / 83, not Frenzied; hit back down to 41 / 83 it is Frenzied the next phase 5.
    /// </summary>
    [Fact]
    public void ProphecyMidFight_41Of63Becomes61Of83_NotFrenzied_UntilHitBelow41Point5()
    {
        Simulation sim = NoFights();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle z = Place(sim, 0, Zealot, At(sim, 10, 10));
        u.Hp[z.Index] = 41;
        sim.Tick();
        ProductionSystem.Researched(w, 0, Prophecy);
        sim.Tick();
        Assert.Equal((61, 83), (u.Hp[z.Index], w.MaxHp(z.Index)));
        Assert.Equal(0, u.Statuses.Count[z.Index]);
        CombatSystem.HitUnit(w, new PendingHit(default, 1, z, false, 20));
        sim.Tick();
        Assert.Equal((0.5f, 1), StatusOf(sim, z, Frenzied));
    }
}
