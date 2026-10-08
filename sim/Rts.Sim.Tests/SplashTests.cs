using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-2b criterion 3 (docs/02 "Splash and friendly fire"): 100 % within 40 % of the radius, linear to 50 % at the edge,
/// nothing past it; friendly fire hits own units at half and never own buildings; an attack without it hits enemies only;
/// enemy buildings in the radius take structure damage.
/// </summary>
public class SplashTests
{
    private static int Catapult => ProjectileTests.Catapult;
    private static int Mage => TestSim.Data.FindUnit("malazan_cadre_mage");
    private static int Tent => TestSim.Data.FindBuilding("whirlwind_tent");
    private static int Billet => TestSim.Data.FindBuilding("malazan_billet");

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(0.4f, 1f)]
    [InlineData(0.7f, 0.75f)]
    [InlineData(1f, 0.5f)]
    [InlineData(1.004f, 0f)]
    public void Falloff_IsFullInside40Percent_ThenLinearTo50PercentAtTheEdge_ThenNothing(float fraction, float expected)
    {
        foreach (float r in new[] { 1.5f, 2f, 2.5f })
            Assert.Equal(expected, ProjectileSystem.Falloff(fraction * r, r), 4);
    }

    [Fact]
    public void Scale_RoundsHalfUp_AndIsAtLeastOne()
    {
        Assert.Equal(25, ProjectileSystem.Scale(25, 1f));
        Assert.Equal(19, ProjectileSystem.Scale(25, 0.75f)); // 18.75
        Assert.Equal(13, ProjectileSystem.Scale(25, 0.5f)); // 12.5
        Assert.Equal(1, ProjectileSystem.Scale(1, 0.25f));
    }

    /// <summary>
    /// A Catapult (player 0, 24 m west) is ordered to attack a holding enemy Laborer at the center; holding Laborers of
    /// <paramref name="victimOwner"/> stand <paramref name="fraction"/> x 2.5 m east of it. Returns the victim's damage
    /// from the one stone.
    /// </summary>
    private static int StoneDamage(int victimOwner, float fraction)
    {
        Simulation sim = Flat();
        World w = sim.World;
        UnitStore u = w.Units;
        Vector2 center = At(sim, 24, 24);
        EntityHandle c = Place(sim, 0, Catapult, center - new Vector2(20f, 0f));
        EntityHandle target = Place(sim, 1, Laborer, center);
        float radius = TestSim.Data.Units[Catapult].Attack.Splash;
        EntityHandle victim = Place(sim, victimOwner, Laborer, center + new Vector2(fraction * radius, 0f));
        sim.Enqueue(Command.HoldPosition(1, target));
        sim.Enqueue(Command.HoldPosition(victimOwner, victim));
        sim.Enqueue(Command.Attack(0, c, target, isBuilding: false));
        int full = u.Hp[victim.Index];
        RunUntil(sim, () => w.Projectiles.Count == 1, 300);
        Assert.Equal(center, w.Projectiles.Target[0]);
        ProjectileTests.RunUntilImpact(sim);
        Assert.True(w.Impacts[0].Hit);
        return full - u.Hp[victim.Index];
    }

    [Theory]
    [InlineData(0f, 25)] // 50 siege x 0.5 v light = 25, armor 0
    [InlineData(0.4f, 25)]
    [InlineData(0.7f, 19)] // x 0.75 = 18.75
    [InlineData(1f, 13)] // x 0.5 = 12.5
    [InlineData(1.004f, 0)] // past the edge
    public void ACatapultStone_HurtsEnemiesByTheFalloff(float fraction, int damage)
    {
        Assert.Equal(damage, StoneDamage(1, fraction));
    }

    [Theory]
    [InlineData(0f, 13)] // 25 x 0.5 friendly = 12.5
    [InlineData(0.7f, 9)] // 25 x 0.75 x 0.5 = 9.375
    [InlineData(1f, 6)] // 25 x 0.5 x 0.5 = 6.25
    [InlineData(1.004f, 0)]
    public void ACatapultStone_HurtsOwnUnitsAtHalf_FriendlyFire(float fraction, int damage)
    {
        Assert.Equal(damage, StoneDamage(0, fraction));
    }

    [Fact]
    public void ACatapultStone_NeverDamagesOwnBuildings_ButEnemyBuildingsTakeStructureDamage()
    {
        Simulation sim = Flat();
        World w = sim.World;
        BuildingStore b = w.Buildings;
        // An enemy Tent (cells 24-25) and an own Billet (cells 22-23) side by side, the stone aimed at the Tent's west edge.
        Assert.True(b.Spawn(1, Tent, 24 * w.NavGrid.Width + 24, out EntityHandle tent));
        Assert.True(b.Spawn(0, Billet, 24 * w.NavGrid.Width + 22, out EntityHandle billet));
        EntityHandle c = Place(sim, 0, Catapult, At(sim, 24, 34, dx: -1f));
        sim.Enqueue(Command.Attack(0, c, tent, isBuilding: true));
        int tentHp = b.Hp[tent.Index], billetHp = b.Hp[billet.Index];
        RunUntil(sim, () => w.Projectiles.Count == 1, 300);
        Vector2 impact = w.Projectiles.Target[0];
        Assert.True(CombatSystem.BuildingDistanceSquared(w, billet.Index, impact) <= 2.5f * 2.5f, "setup: the Billet is not in the splash");
        ProjectileTests.RunUntilImpact(sim);
        // 50 siege x 3.0 v structure = 150, less armor 3 = 147, at the footprint: full.
        Assert.Equal(147, tentHp - b.Hp[tent.Index]);
        Assert.Equal(billetHp, b.Hp[billet.Index]);
    }

    [Fact]
    public void ASplashReachesEnemyBuildingsAroundTheImpact_AsStructure_ByTheFalloff()
    {
        Simulation sim = Flat();
        World w = sim.World;
        UnitStore u = w.Units;
        BuildingStore b = w.Buildings;
        // A Tent at cells 26-27 (x 52-56 m); the stone lands on a Laborer at x 50 m (cell 24's east edge at 2 m from the Tent).
        Assert.True(b.Spawn(1, Tent, 24 * w.NavGrid.Width + 26, out EntityHandle tent));
        Vector2 at = new(50f, 50f);
        EntityHandle target = Place(sim, 1, Laborer, at);
        sim.Enqueue(Command.HoldPosition(1, target));
        EntityHandle c = Place(sim, 0, Catapult, at - new Vector2(16f, 0f));
        sim.Enqueue(Command.Attack(0, c, target, isBuilding: false));
        int tentHp = b.Hp[tent.Index];
        RunUntil(sim, () => w.Projectiles.Count == 1, 300);
        ProjectileTests.RunUntilImpact(sim);
        float d = MathF.Sqrt(CombatSystem.BuildingDistanceSquared(w, tent.Index, at));
        Assert.Equal(2f, d, 4);
        // 147 x falloff(2 m of 2.5 m) = 147 x (1 - 0.5 x 1 / 1.5) = 98.
        Assert.Equal(ProjectileSystem.Scale(147, ProjectileSystem.Falloff(2f, 2.5f)), tentHp - b.Hp[tent.Index]);
        Assert.Equal(98, tentHp - b.Hp[tent.Index]);
    }

    [Fact]
    public void ACadreMagesBolt_SplashesEnemiesOnly_NoFriendlyFire()
    {
        Simulation sim = Flat();
        World w = sim.World;
        UnitStore u = w.Units;
        Vector2 center = At(sim, 24, 24);
        EntityHandle mage = Place(sim, 0, Mage, center - new Vector2(10f, 0f));
        EntityHandle target = Place(sim, 1, Laborer, center);
        EntityHandle enemy = Place(sim, 1, Laborer, center + new Vector2(0f, 1f));
        EntityHandle own = Place(sim, 0, Laborer, center + new Vector2(0f, -1f));
        foreach ((int p, EntityHandle h) in new[] { (1, target), (1, enemy), (0, own) }) sim.Enqueue(Command.HoldPosition(p, h));
        sim.Enqueue(Command.Attack(0, mage, target, isBuilding: false));
        int full = u.Hp[target.Index];
        RunUntil(sim, () => w.Projectiles.Count == 1, 300);
        Assert.Equal(TestSim.Data.FindProjectile("magic_bolt"), w.Projectiles.ProjectileTypeId[0]);
        ProjectileTests.RunUntilImpact(sim);
        Assert.True(w.Impacts[0].Hit);
        // 9 magic (ignores armor) on the target; the splash at 1 m of 1.5 m: 9 x (1 - 0.5 x 0.4 / 0.9) = 7.
        Assert.Equal(9, full - u.Hp[target.Index]);
        Assert.Equal(7, full - u.Hp[enemy.Index]);
        Assert.Equal(full, u.Hp[own.Index]);
    }

    [Fact]
    public void AnAimedMiss_DoesNotSplash()
    {
        Simulation sim = Flat();
        World w = sim.World;
        UnitStore u = w.Units;
        Vector2 center = At(sim, 24, 24);
        EntityHandle mage = Place(sim, 0, Mage, center - new Vector2(10f, 0f));
        EntityHandle target = Place(sim, 1, Laborer, center);
        EntityHandle enemy = Place(sim, 1, Laborer, center + new Vector2(0f, 1f));
        sim.Enqueue(Command.HoldPosition(1, target));
        sim.Enqueue(Command.HoldPosition(1, enemy));
        sim.Enqueue(Command.Attack(0, mage, target, isBuilding: false));
        int full = u.Hp[enemy.Index];
        RunUntil(sim, () => w.Projectiles.Count == 1, 300);
        RunUntil(sim, () => w.Projectiles.TicksLeft[0] == 1, 20); // after its last re-lead (a shot tracks a slow target, BUG-0183)
        u.Position[target.Index] += new Vector2(0f, -3f);
        ProjectileTests.RunUntilImpact(sim);
        Assert.False(w.Impacts[0].Hit);
        Assert.Equal(full, u.Hp[enemy.Index]);
    }

    [Fact]
    public void FriendlyFireKills_CountForTheShootersOwner_AndAreNotRetaliatedOn()
    {
        Simulation sim = Flat();
        World w = sim.World;
        UnitStore u = w.Units;
        Vector2 center = At(sim, 24, 24);
        EntityHandle c = Place(sim, 0, Catapult, center - new Vector2(20f, 0f));
        EntityHandle target = Place(sim, 1, Laborer, center);
        EntityHandle own = Place(sim, 0, Laborer, center + new Vector2(0.9f, 0f)); // a worker: never strikes the enemy itself
        // A healthy own holder in the splash, out of reach of the enemy: it scans, so it would retaliate on a real attacker.
        EntityHandle survivor = Place(sim, 0, HeavyInfantry, center + new Vector2(-2f, 0f));
        sim.Enqueue(Command.HoldPosition(1, target));
        sim.Enqueue(Command.HoldPosition(0, own));
        sim.Enqueue(Command.HoldPosition(0, survivor));
        sim.Enqueue(Command.Attack(0, c, target, isBuilding: false));
        u.Hp[own.Index] = 1;
        int full = u.Hp[survivor.Index];
        RunUntil(sim, () => w.Projectiles.Count == 1, 300);
        ProjectileTests.RunUntilImpact(sim);
        Assert.False(u.IsAlive(own));
        Assert.Contains(w.Deaths.ToArray(), e => e.Victim == own && e.VictimOwner == 0 && e.KillerOwner == 0);
        Assert.Equal(1, w.Kills[0]);
        Assert.Equal(1, w.Losses[0]);
        Assert.True(u.Hp[survivor.Index] < full, "setup: the survivor was not in the splash");
        Assert.Equal(default, u.LastAttacker[survivor.Index]);
        Assert.Equal(default, u.Target[survivor.Index]);
        Assert.Equal(c, u.LastAttacker[target.Index]); // the enemy was attacked
    }

    [Fact]
    public void AMeleeAttackWithSplash_SplashesRoundItsVictim()
    {
        // No shipped melee attack has splash; the rule is general (a data hook), so hand it one.
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "attack.splash", "1.5");
        DataLoadResult load = DataLoader.LoadAll(dir.Path);
        Assert.True(load.Ok, string.Join("\n", load.Errors));
        var sim = new Simulation(new SimConfig(1, 2, 16, 160) { Data = load.Data! }, LocalMovementTests.Flat(48));
        World w = sim.World;
        UnitStore u = w.Units;
        Vector2 at = At(sim, 20, 20);
        EntityHandle hi = Place(sim, 0, HeavyInfantry, at);
        EntityHandle a = Place(sim, 1, Laborer, at + new Vector2(1f, 0f));
        EntityHandle b = Place(sim, 1, Laborer, at + new Vector2(1f, 1f));
        sim.Enqueue(Command.HoldPosition(1, a));
        sim.Enqueue(Command.HoldPosition(1, b));
        sim.Enqueue(Command.Attack(0, hi, a, isBuilding: false));
        int full = u.Hp[b.Index];
        RunUntil(sim, () => u.Hp[a.Index] < full, 200);
        // 10 melee on the target; b stands 1 m from it, in a 1.5 m splash: 10 x (1 - 0.5 x 0.4 / 0.9) = 7.8 -> 8.
        Assert.Equal(10, full - u.Hp[a.Index]);
        Assert.Equal(ProjectileSystem.Scale(10, ProjectileSystem.Falloff(1f, 1.5f)), full - u.Hp[b.Index]);
        Assert.Equal(8, full - u.Hp[b.Index]);
    }
}
