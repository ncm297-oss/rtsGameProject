using Rts.Sim.Abilities;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-4 criterion 2: a unit with a frenzied-kind entry whose status has <c>damageBonus</c> 0.2 (Blood-oil Frenzy, on the
/// <see cref="BloodOilScenes"/> copy) deals <c>attack x 1.2</c>, rounded by <see cref="DamageCalc"/>'s rule, on melee and
/// ranged hits against a unit and a building; without the entry, and with a plain <c>frenzied</c> entry, the old value. The
/// bonus is read where the attack value and tech bonus are (a blow at the strike, a shot at its landing), so it stops with
/// the entry, and a shot in flight when the entry ends lands without it.
/// </summary>
public class FrenzyDamageTests
{
    private enum Bearing { None, Plain, Blood }

    /// <summary>
    /// The first hit player 0's <paramref name="attacker"/> lands on player 1's Battering Ram (heavy, armor 6, a million hit
    /// points; it hits buildings only, so it never answers) or on a Whirlwind Tent (structure, armor 1), with the given entry
    /// on the attacker (+40 % attack speed, for good).
    /// </summary>
    private static int FirstHit(string attacker, bool building, Bearing bearing)
    {
        Simulation sim = BloodOilScenes.Flat();
        World w = sim.World;
        GameData d = w.Data;
        EntityHandle a = BloodOilScenes.Place(sim, 0, attacker, At(sim, 14, 20));
        EntityHandle target;
        if (building)
        {
            Assert.True(w.Buildings.Spawn(1, d.FindBuilding("whirlwind_tent"), 20 * 48 + 18, out target));
            BloodOilScenes.Place(sim, 1, "whirlwind_battering_ram", At(sim, 40, 40)); // a live unit, so player 1 is not defeated
        }
        else
        {
            target = BloodOilScenes.Place(sim, 1, "whirlwind_battering_ram", At(sim, 18, 20));
            w.Units.Hp[target.Index] = 1_000_000;
        }
        if (bearing != Bearing.None)
            StatusSystem.Apply(w, a.Index, bearing == Bearing.Blood ? BloodOilScenes.Frenzy : BloodOilScenes.PlainFrenzied, 0.4f, 100_000, 0);
        w.Fog.Update();
        sim.Enqueue(Command.Attack(0, a, target, building));
        int Hp() => building ? w.Buildings.Hp[target.Index] : w.Units.Hp[target.Index];
        int full = Hp();
        Assert.True(RunUntil(sim, () => Hp() < full, 600) < 600, "no hit landed");
        return full - Hp();
    }

    /// <summary>
    /// The worked number of docs/03 "Implementation (M7-4)": the Warrior's 22 melee x 1.2 = 26.4, rounded half up to 26,
    /// less the Ram's 6 armor = 20 (22 - 6 = 16 without). Structure: 22 x 0.7 x 1.2 = 18.48 -> 18, less the Tent's 1 = 17
    /// (15.4 -> 15 - 1 = 14 without). The Javelin Thrower's 12 pierce: 12 x 0.6 x 1.2 = 8.64 -> 9 - 6 = 3 on the Ram (7.2 -> 7
    /// - 6 = 1 without); 12 x 0.35 x 1.2 = 5.04 -> 5 - 1 = 4 on the Tent (4.2 -> 4 - 1 = 3 without).
    /// </summary>
    [Theory]
    [InlineData("teblor_warrior", false, 16, 20)]
    [InlineData("teblor_warrior", true, 14, 17)]
    [InlineData("teblor_javelin_thrower", false, 1, 3)]
    [InlineData("teblor_javelin_thrower", true, 3, 4)]
    public void AHit_WithTheBloodOilEntry_IsTheRoundedBonus_WithoutItOrPlainFrenziedTheOldValue(string attacker, bool building, int old, int frenzied)
    {
        Assert.Equal(old, FirstHit(attacker, building, Bearing.None));
        Assert.Equal(old, FirstHit(attacker, building, Bearing.Plain)); // the Zealot's frenzied has no damageBonus
        Assert.Equal(frenzied, FirstHit(attacker, building, Bearing.Blood));
    }

    [Fact]
    public void TheRule_IsDamageCalcs_WithTheFactorLast()
    {
        GameData d = BloodOilScenes.Data;
        AttackDef warrior = d.Units[d.FindUnit("teblor_warrior")].Attack;
        int heavy = d.DamageTable.ArmorClassKeys.IndexOf("heavy");
        Assert.Equal(26, DamageCalc.Compute(d.DamageTable, warrior, 0, heavy, 0, 1.2f)); // 26.4 rounds half up to 26
        Assert.Equal(22, DamageCalc.Compute(d.DamageTable, warrior, 0, heavy, 0, 1f));
        Assert.Equal(DamageCalc.Compute(d.DamageTable, warrior, 3, heavy, 2), DamageCalc.Compute(d.DamageTable, warrior, 3, heavy, 2, 1f));
        Assert.Equal(30, DamageCalc.Compute(d.DamageTable, warrior, 3, heavy, 0, 1.2f)); // a tech's +3 is multiplied too: 25 x 1.2
    }

    [Fact]
    public void TheFactor_IsOnePlusTheLargestBonus_AndOneWithout()
    {
        Simulation sim = BloodOilScenes.Flat();
        World w = sim.World;
        EntityHandle a = BloodOilScenes.Place(sim, 0, "teblor_warrior", At(sim, 10, 10));
        Assert.Equal(1f, StatusSystem.DamageFactor(w, a.Index));
        StatusSystem.Apply(w, a.Index, BloodOilScenes.PlainFrenzied, 0.5f, 100, 0);
        StatusSystem.Apply(w, a.Index, BloodOilScenes.Slowed, 0.3f, 100, 1);
        Assert.Equal(1f, StatusSystem.DamageFactor(w, a.Index));
        StatusSystem.Apply(w, a.Index, BloodOilScenes.Frenzy, 0.4f, 100, 0);
        Assert.Equal(1.2f, StatusSystem.DamageFactor(w, a.Index));
    }

    /// <summary>Hit damages on the Ram, in landing order, over <paramref name="ticks"/> ticks.</summary>
    private static List<(int Tick, int Damage)> Hits(Simulation sim, EntityHandle ram, int ticks)
    {
        var hits = new List<(int, int)>();
        UnitStore u = sim.World.Units;
        for (int t = 0; t < ticks; t++)
        {
            int before = u.Hp[ram.Index];
            sim.Tick();
            if (u.Hp[ram.Index] < before) hits.Add((sim.TickNumber - 1, before - u.Hp[ram.Index]));
        }
        return hits;
    }

    private static (Simulation Sim, EntityHandle Attacker, EntityHandle Ram) Duel(string attacker, int gapCells)
    {
        Simulation sim = BloodOilScenes.Flat();
        World w = sim.World;
        EntityHandle a = BloodOilScenes.Place(sim, 0, attacker, At(sim, 14, 20));
        EntityHandle ram = BloodOilScenes.Place(sim, 1, "whirlwind_battering_ram", At(sim, 14 + gapCells, 20));
        w.Units.Hp[ram.Index] = 1_000_000;
        w.Fog.Update();
        sim.Enqueue(Command.Attack(0, a, ram, false));
        return (sim, a, ram);
    }

    [Fact]
    public void TheBonus_StopsWithTheEntry_OnMeleeBlows()
    {
        (Simulation sim, EntityHandle a, EntityHandle ram) = Duel("teblor_warrior", 1);
        World w = sim.World;
        UnitStore u = w.Units;
        StatusSystem.Apply(w, a.Index, BloodOilScenes.Frenzy, 0.4f, 100, 0);
        int with = 0, without = 0, slowedTicks = 0;
        for (int t = 0; t < 300; t++)
        {
            int before = u.Hp[ram.Index];
            sim.Tick();
            // A blow is struck and lands in phases 10-11, after phase 5: the entry at the tick's end is the one it struck with.
            bool on = BloodOilScenes.TicksOf(w, a, BloodOilScenes.Frenzy) > 0;
            if (BloodOilScenes.TicksOf(w, a, BloodOilScenes.Slowed) > 0) slowedTicks++;
            if (u.Hp[ram.Index] == before) continue;
            Assert.Equal(on ? 20 : 16, before - u.Hp[ram.Index]);
            if (on) with++;
            else without++;
        }
        Assert.True(with >= 3 && without >= 4, $"{with} blows with the Frenzy, {without} after");
        Assert.Equal(BloodOilScenes.AftermathTicks, slowedTicks); // and its aftermath, 80 ticks
    }

    /// <summary>
    /// The answer for a shot in flight: the bonus is read at the landing (as the tech bonus is), so a javelin thrown with the
    /// Frenzy on and landing after it ended deals the old 1, and one thrown before the Frenzy and landing with it on deals 3.
    /// </summary>
    [Fact]
    public void AShotInFlight_WhenTheEntryEnds_LandsWithoutTheBonus_AndOneThrownBeforeItLandsWithIt()
    {
        (Simulation sim, EntityHandle a, EntityHandle ram) = Duel("teblor_javelin_thrower", 4);
        World w = sim.World;
        StatusSystem.Apply(w, a.Index, BloodOilScenes.Frenzy, 0.4f, 100_000, 0);
        Assert.True(RunUntil(sim, () => w.Projectiles.Count > 0, 300) < 300, "no javelin thrown");
        // The Frenzy runs out in the next phase 5, before the javelin (8 m at 1.25 m a tick) lands.
        w.Units.Statuses.TicksRemaining[w.Units.Statuses.IndexOf(a.Index, BloodOilScenes.Frenzy)] = 1;
        var first = Hits(sim, ram, 20);
        Assert.Single(first);
        Assert.Equal(1, first[0].Damage);
        Assert.Equal(-1, BloodOilScenes.TicksOf(w, a, BloodOilScenes.Frenzy));
        Assert.True(BloodOilScenes.TicksOf(w, a, BloodOilScenes.Slowed) > 0);

        // Without the entry at the throw, with it at the landing: the bonus.
        Assert.True(RunUntil(sim, () => w.Projectiles.Count > 0, 300) < 300, "no second javelin");
        StatusSystem.Apply(w, a.Index, BloodOilScenes.Frenzy, 0.4f, 100_000, 0);
        var second = Hits(sim, ram, 20);
        Assert.Single(second);
        Assert.Equal(3, second[0].Damage);
    }

    /// <summary>A shooter that dies with its javelin in the air: the shot lands without the bonus (the statuses went with the slot).</summary>
    [Fact]
    public void AShotWhoseShooterDied_LandsWithoutTheBonus()
    {
        (Simulation sim, EntityHandle a, EntityHandle ram) = Duel("teblor_javelin_thrower", 4);
        World w = sim.World;
        StatusSystem.Apply(w, a.Index, BloodOilScenes.Frenzy, 0.4f, 100_000, 0);
        Assert.True(RunUntil(sim, () => w.Projectiles.Count > 0, 300) < 300, "no javelin thrown");
        w.Units.Free(a);
        var hits = Hits(sim, ram, 20);
        Assert.Single(hits);
        Assert.Equal(1, hits[0].Damage);
    }

    /// <summary>
    /// M7-6 (BUG-1281): every <c>DamageToUnit</c> / <c>DamageToBuilding</c> form takes the status damage factor: the
    /// (attackerType, owner) short forms, which had no caller and left the factor out, are gone, so no later caller can miss
    /// the Frenzy's bonus by picking one.
    /// </summary>
    [Fact]
    public void EveryDamageHelper_TakesTheStatusDamageFactor()
    {
        var forms = typeof(CombatSystem).GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .Where(m => m.Name is "DamageToUnit" or "DamageToBuilding").ToArray();
        Assert.Equal(2, forms.Length);
        Assert.All(forms, m => Assert.Contains(m.GetParameters(), p => p.Name == "factor" && p.ParameterType == typeof(float)));
    }
}
