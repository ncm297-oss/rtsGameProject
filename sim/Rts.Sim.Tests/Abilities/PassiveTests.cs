using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Combat;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using static Rts.Sim.Tests.AbilityScenes;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-4b-3a criterion 3: the Zealot's passives from data (docs/factions/whirlwind.md "Unit notes"). Frenzy of the
/// Apocalypse: Frenzied +50 % while below 50 % of its maximum hit points (the hp techs included), not at exactly the
/// line. Martyrdom: a dying Zealot gives the owner's Zealots whose center is within 6 m (the edge counts) Frenzied +50 %
/// for 5 s, never itself, a Raider or an enemy; same-tick deaths resolve in death order. A unit without a passive costs
/// the pass nothing.
/// </summary>
public class PassiveTests
{
    private static int Zealot => TestSim.Data.FindUnit("whirlwind_zealot");

    private static int Frenzied => TestSim.Data.FindStatus("frenzied");

    /// <summary>Kills live unit <paramref name="h"/> between ticks through the death path (one hit of its hit points, credited to player 1), as phase 11 would.</summary>
    private static void Kill(Simulation sim, EntityHandle h)
    {
        CombatSystem.HitUnit(sim.World, new PendingHit(default, 1, h, false, sim.World.Units.Hp[h.Index]));
        Assert.False(sim.World.Units.IsAlive(h));
    }

    [Fact]
    public void Frenzy_At31Of63_IsOn_At32Of63_IsNot()
    {
        Simulation sim = NoFights();
        UnitStore u = sim.World.Units;
        EntityHandle z = Place(sim, 0, Zealot, At(sim, 10, 10));
        Assert.Equal(1, u.BelowHpUnits);
        Assert.Equal(63, PassiveSystem.MaxHp(sim.World, z.Index));
        sim.Tick();
        Assert.Equal(0, u.Statuses.Count[z.Index]); // full hp
        u.Hp[z.Index] = 32; // 50.8 %: not below
        sim.Tick();
        Assert.Equal(0, u.Statuses.Count[z.Index]);
        u.Hp[z.Index] = 31; // 49.2 %: below
        for (int t = 0; t < 30; t++)
        {
            sim.Tick();
            Assert.Equal((0.5f, 1), StatusOf(sim, z, Frenzied)); // re-applied each phase 5: in force until the next one
            Assert.Equal(1, u.Statuses.Count[z.Index]);
        }
        u.Hp[z.Index] = 32; // a test seam: nothing heals a unit yet
        sim.Tick();
        Assert.Equal(0, u.Statuses.Count[z.Index]);
        Assert.True(PassiveSystem.IsBelow(31, 63, 0.5f));
        Assert.False(PassiveSystem.IsBelow(32, 63, 0.5f));
        Assert.False(PassiveSystem.IsBelow(5, 10, 0.5f)); // exactly at the fraction is not below
    }

    [Fact]
    public void Frenzy_WithDryjhnasProphecy_41Of83_IsOn_42Of83_IsNot()
    {
        Simulation sim = NoFights();
        UnitStore u = sim.World.Units;
        EntityHandle z = Place(sim, 1, Zealot, At(sim, 10, 10));
        EntityHandle other = Place(sim, 0, Zealot, At(sim, 20, 10)); // player 0 has no tech: its line stays 31.5
        sim.World.Techs.Set(1, sim.World.Data.FindTech("dryjhnas_prophecy"), true);
        Assert.Equal(83, PassiveSystem.MaxHp(sim.World, z.Index));
        Assert.Equal(63, PassiveSystem.MaxHp(sim.World, other.Index));
        u.Hp[z.Index] = 42;
        u.Hp[other.Index] = 41;
        sim.Tick();
        Assert.Equal(0, u.Statuses.Count[z.Index]);
        Assert.Equal(0, u.Statuses.Count[other.Index]);
        u.Hp[z.Index] = 41;
        sim.Tick();
        Assert.Equal((0.5f, 1), StatusOf(sim, z, Frenzied));
        Assert.Equal(0, u.Statuses.Count[other.Index]);
    }

    [Fact]
    public void Frenzy_MakesTheZealotSwingEvery13Ticks_OnceBelowHalf()
    {
        Simulation sim = Flat();
        UnitStore u = sim.World.Units;
        EntityHandle z = Place(sim, 0, Zealot, At(sim, 10, 10));
        EntityHandle dummy = Place(sim, 1, sim.World.Data.FindUnit("whirlwind_battering_ram"), At(sim, 10, 10, 1.5f));
        u.Hp[dummy.Index] = 1_000_000;
        u.Hp[z.Index] = 31;
        sim.Enqueue(Commands.Command.Attack(0, z, dummy, false));
        var starts = new List<int>();
        for (int t = 0; t < 100; t++)
        {
            int before = u.CooldownTicks[z.Index];
            sim.Tick();
            if (u.CooldownTicks[z.Index] > before) starts.Add(sim.TickNumber - 1);
        }
        Assert.True(starts.Count >= 6);
        Assert.All(starts.Zip(starts.Skip(1), (a, b) => b - a), g => Assert.Equal(13, g));
    }

    [Fact]
    public void Martyrdom_ReachesOwnZealotsAt6m_NotAt601_NotRaiders_NotEnemies_NotItself_For100Ticks()
    {
        Simulation sim = NoFights();
        UnitStore u = sim.World.Units;
        Vector2 p = At(sim, 20, 20);
        EntityHandle dying = Place(sim, 0, Zealot, p);
        EntityHandle edge = Place(sim, 0, Zealot, Off(p, 6f));           // exactly 6 m: the edge counts
        EntityHandle diagonal = Place(sim, 0, Zealot, Off(p, 0f, 6f)); // 6 m the other way
        EntityHandle near = Place(sim, 0, Zealot, Off(p, 0f, -2f));
        EntityHandle outside = Place(sim, 0, Zealot, Off(p, -6.01f));
        EntityHandle raider = Place(sim, 0, Raider, Off(p, 0f, 2f));
        EntityHandle enemy = Place(sim, 1, Zealot, Off(p, 2f, 2f));
        sim.Tick(); // into the spatial hash
        Kill(sim, dying);
        foreach (EntityHandle h in new[] { edge, diagonal, near })
            Assert.Equal((0.5f, 100), StatusOf(sim, h, Frenzied)); // 5 s
        foreach (EntityHandle h in new[] { outside, raider, enemy })
            Assert.Equal(0, u.Statuses.Count[h.Index]);
        Assert.Equal(0, u.Statuses.Count[dying.Index]); // freed: its store is empty
        Assert.Equal(0, u.Statuses.SourcePlayer[u.Statuses.IndexOf(near.Index, Frenzied)]); // the owner is the source
        // In force for exactly 100 ticks from the death (as a phase 11 death: that tick's phase 10 is over, so ticks + 1 .. + 99, then freed in phase 5).
        for (int t = 1; t < 100; t++)
        {
            sim.Tick();
            Assert.Equal((0.5f, 100 - t), StatusOf(sim, near, Frenzied));
        }
        sim.Tick();
        Assert.Equal(0, u.Statuses.Count[near.Index]);
        Assert.Equal(5, u.BelowHpUnits); // 4 own and 1 enemy Zealots alive; the dead one's count went with it
    }

    [Fact]
    public void APassiveFrenziedZealot_KeepsItAfterMartyrdomsTicks()
    {
        Simulation sim = NoFights();
        UnitStore u = sim.World.Units;
        Vector2 p = At(sim, 20, 20);
        EntityHandle dying = Place(sim, 0, Zealot, p);
        EntityHandle hurt = Place(sim, 0, Zealot, Off(p, 3f));
        u.Hp[hurt.Index] = 20;
        sim.Tick();
        Assert.Equal((0.5f, 1), StatusOf(sim, hurt, Frenzied));
        Kill(sim, dying);
        Assert.Equal((0.5f, 100), StatusOf(sim, hurt, Frenzied)); // one entry: the longer duration kept
        for (int t = 1; t <= 150; t++)
        {
            sim.Tick();
            Assert.Equal((0.5f, Math.Max(1, 100 - t)), StatusOf(sim, hurt, Frenzied));
            Assert.Equal(1, u.Statuses.Count[hurt.Index]);
        }
    }

    /// <summary>
    /// Two Zealots dying one after the other (the documented order: death order, an earlier death's Martyrdom reaches a unit
    /// that dies later the same tick while it still lives; a later death's never reaches one already freed).
    /// </summary>
    [Fact]
    public void TwoDeaths_ResolveInDeathOrder()
    {
        Simulation sim = NoFights();
        UnitStore u = sim.World.Units;
        Vector2 p = At(sim, 20, 20);
        EntityHandle first = Place(sim, 0, Zealot, p);
        EntityHandle second = Place(sim, 0, Zealot, Off(p, 5f));
        EntityHandle firstsNeighbour = Place(sim, 0, Zealot, Off(p, -4f)); // 9 m from the second
        EntityHandle secondsNeighbour = Place(sim, 0, Zealot, Off(p, 9f)); // 9 m from the first
        sim.Tick();
        Kill(sim, first);
        Assert.Equal((0.5f, 100), StatusOf(sim, second, Frenzied)); // still live: it takes the first's Martyrdom
        Assert.Equal((0.5f, 100), StatusOf(sim, firstsNeighbour, Frenzied));
        Assert.Equal(0, u.Statuses.Count[secondsNeighbour.Index]);
        Kill(sim, second);
        Assert.Equal((0.5f, 100), StatusOf(sim, secondsNeighbour, Frenzied));
        Assert.Equal(0, u.Statuses.Count[first.Index]);
        Assert.Equal(2, u.BelowHpUnits);
    }

    /// <summary>
    /// The same within one tick: two burning Zealots whose damage over time kills both in one phase 5 pulse, in slot order.
    /// The later one took the earlier's Martyrdom (it was alive), and both neighbours are Frenzied.
    /// </summary>
    [Fact]
    public void TwoDeathsInOneTick_BothNeighboursAreFrenzied()
    {
        Simulation sim = NoFights();
        UnitStore u = sim.World.Units;
        Vector2 p = At(sim, 20, 20);
        EntityHandle first = Place(sim, 0, Zealot, p);
        EntityHandle second = Place(sim, 0, Zealot, Off(p, 5f));
        EntityHandle a = Place(sim, 0, Zealot, Off(p, -4f));
        EntityHandle b = Place(sim, 0, Zealot, Off(p, 9f));
        sim.Tick();
        foreach (EntityHandle h in new[] { first, second })
            StatusSystem.Apply(sim.World, h.Index, Burning, 1000f, 20, 1);
        int deaths = 0;
        for (int t = 0; t < 25 && deaths == 0; t++)
        {
            sim.Tick();
            deaths = sim.World.Deaths.Length;
        }
        Assert.Equal(2, deaths);
        Assert.False(u.IsAlive(first) || u.IsAlive(second));
        Assert.Equal(0.5f, StatusOf(sim, a, Frenzied).Magnitude);
        Assert.Equal(0.5f, StatusOf(sim, b, Frenzied).Magnitude);
        Assert.Equal(first, sim.World.Deaths[0].Victim); // slot order
    }

    /// <summary>A unit whose type has no passive is never counted, so phase 5's pass returns at once; spawning and freeing keeps the count.</summary>
    [Fact]
    public void NoPassiveUnits_CountZero_ThePassSkips_AndTheCountFollowsSpawnsAndDeaths()
    {
        Simulation sim = NoFights(units: 128);
        UnitStore u = sim.World.Units;
        for (int k = 0; k < 40; k++) Place(sim, k % 2, k % 2 == 0 ? Raider : HeavyInfantry, At(sim, 5 + k % 20, 5 + k / 20));
        Assert.Equal(0, u.BelowHpUnits);
        for (int i = 0; i < u.Capacity; i++) Assert.False(u.HasBelowHpPassive(i));
        sim.Tick();
        ulong before = sim.StateHash();
        PassiveSystem.Run(sim.World); // nothing to do: no state changes
        Assert.Equal(before, sim.StateHash());
        EntityHandle z = Place(sim, 0, Zealot, At(sim, 30, 30));
        Assert.Equal(1, u.BelowHpUnits);
        Assert.True(u.HasBelowHpPassive(z.Index));
        sim.Tick();
        Kill(sim, z);
        Assert.Equal(0, u.BelowHpUnits);
        Assert.False(u.HasBelowHpPassive(z.Index));
    }
    /// <summary>
    /// M4-4b-3b criterion 2 (BUG-0420): the line is exact. 0.15 x 100 is 15, which is not below it (single precision made it
    /// 15.000001); 14 is. 0.3 x 63 is 18.9: 18 is below, 19 is not.
    /// </summary>
    [Fact]
    public void TheLine_IsExact_015Of100Is15NotBelow_03Of63Is18Point9()
    {
        Assert.False(PassiveSystem.IsBelow(15, 100, 0.15f));
        Assert.True(PassiveSystem.IsBelow(14, 100, 0.15f));
        Assert.True(PassiveSystem.IsBelow(18, 63, 0.3f));
        Assert.False(PassiveSystem.IsBelow(19, 63, 0.3f));
        Assert.Equal(150_000, PassiveSystem.LineOf(0.15f));
        Assert.Equal(300_000, PassiveSystem.LineOf(0.3f));
        Assert.Equal(500_000, PassiveSystem.LineOf(0.5f));
    }

    /// <summary>The same rows end to end: a Zealot edited to 100 hp and <c>hpFraction</c> 0.15 is not Frenzied at 15, is at 14; with 0.3 and 63 hp it is at 18, not at 19.</summary>
    [Theory]
    [InlineData(100, 0.15, 15, false)]
    [InlineData(100, 0.15, 14, true)]
    [InlineData(63, 0.3, 18, true)]
    [InlineData(63, 0.3, 19, false)]
    public void TheLine_IsExact_InTheSim(int hp, double fraction, int at, bool frenzied)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("whirlwind", "whirlwind_zealot", "hp", hp.ToString(System.Globalization.CultureInfo.InvariantCulture));
        dir.SetUnitField("whirlwind", "whirlwind_zealot", "passives", "[{ \"trigger\": \"whenBelowHp\", \"hpFraction\": "
            + fraction.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", \"status\": \"frenzied\", \"magnitude\": 0.5 }]");
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        var sim = new Simulation(TestSim.ConfigNoCombat(Seed: 1, PlayerCount: 2, UnitCapacity: 16, CommandCapacity: 64) with { Data = r.Data! }, LocalMovementTests.Flat(48));
        EntityHandle z = Place(sim, 0, r.Data!.FindUnit("whirlwind_zealot"), At(sim, 10, 10));
        Assert.Equal(hp, sim.World.Units.Hp[z.Index]);
        sim.World.Units.Hp[z.Index] = at;
        sim.Tick();
        Assert.Equal(frenzied, sim.World.Units.Statuses.Count[z.Index] == 1);
    }
}
