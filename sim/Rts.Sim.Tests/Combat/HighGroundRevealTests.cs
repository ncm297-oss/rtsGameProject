using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Vision;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-3a: the high-ground reveal (docs/02 "High ground": attacking from high ground reveals the attacker to the target's
/// owner for 2 s). A projectile keeps its firing level; a melee hit from above reveals too; a same-level shooter outside
/// every own sight circle is not revealed; every hit refreshes it; a recycled slot never inherits it; a dead shooter
/// reveals nothing.
/// </summary>
public class HighGroundRevealTests
{
    private static readonly Vector2 Low = FogMaps.Cell(16, 30);
    private static readonly Vector2 High = FogMaps.Cell(21, 30);

    [Fact]
    public void AProjectile_KeepsTheLevelItWasFiredFrom()
    {
        Simulation sim = FogMaps.Sim(FogMaps.TwoLevel());
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle low = Place(sim, 0, Crossbowman, Low);
        EntityHandle high = Place(sim, 1, Crossbowman, High);
        u.Target[high.Index] = low;
        ProjectileSystem.Fire(w, high.Index);
        u.Target[low.Index] = high;
        ProjectileSystem.Fire(w, low.Index);
        Assert.Equal(2, w.Projectiles.Count);
        Assert.Equal(1, w.Projectiles.Level[0]);
        Assert.Equal(0, w.Projectiles.Level[1]);
        w.Projectiles.Free(0);
        Assert.Equal(0, w.Projectiles.Level[0]); // a freed slot is back at its never-used value
    }

    [Fact]
    public void EveryHitFromAbove_RevealsTheShooterToTheVictimsOwnerFor40Ticks()
    {
        Simulation sim = FogMaps.Sim(FogMaps.TwoLevel());
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle victim = Place(sim, 0, HeavyInfantry, Low);
        EntityHandle shooter = Place(sim, 1, Crossbowman, High);
        sim.Enqueue(Command.HoldPosition(0, victim));
        int hp = u.Hp[victim.Index], hits = 0;
        Assert.Equal(0, w.Fog.RevealEnd(0, shooter.Index));
        for (int t = 0; t < 400 && hits < 3; t++)
        {
            sim.Tick();
            if (u.Hp[victim.Index] == hp) continue;
            hp = u.Hp[victim.Index];
            hits++;
            int tick = sim.TickNumber - 1;
            Assert.Equal(tick + VisionConstants.HighGroundRevealTicks, w.Fog.RevealEnd(0, shooter.Index));
            Assert.True(w.Fog.CanSeeUnit(0, shooter.Index));
            Assert.Equal(0, w.Fog.RevealEnd(1, shooter.Index)); // only to the victim's owner
        }
        Assert.Equal(3, hits);
        Assert.False(w.Fog.IsVisible(0, w.Fog.CellOf(High))); // seen through the reveal, not the fog
    }

    [Fact]
    public void AMeleeHitFromAbove_RevealsTheAttacker()
    {
        Simulation sim = FogMaps.Sim(FogMaps.TwoLevel());
        World w = sim.World;
        UnitStore u = w.Units;
        // The plateau's edge cell at the ramp's top (20, 19), and the ramp's top cell (19, 19) below it: 1.1 m apart.
        EntityHandle attacker = Place(sim, 0, HeavyInfantry, new Vector2(40.1f, 39f));
        EntityHandle victim = Place(sim, 1, Laborer, new Vector2(39f, 39f));
        Assert.Equal(1, w.Fog.LevelAt(u.Position[attacker.Index]));
        Assert.Equal(0, w.Fog.LevelAt(u.Position[victim.Index]));
        sim.Enqueue(Command.HoldPosition(1, victim));
        sim.Enqueue(Command.Attack(0, attacker, victim, isBuilding: false));
        int full = u.Hp[victim.Index];
        RunUntil(sim, () => u.Hp[victim.Index] < full, 100);
        Assert.True(u.Hp[victim.Index] < full, "never hit");
        Assert.Equal(sim.TickNumber - 1 + VisionConstants.HighGroundRevealTicks, w.Fog.RevealEnd(1, attacker.Index));
    }

    [Fact]
    public void ASameLevelShooterOutsideEveryOwnSightCircle_IsNotRevealed_NorAnswered()
    {
        Simulation sim = Flat(size: 48, units: 8);
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle victim = Place(sim, 0, HeavyInfantry, FogMaps.Cell(10, 24));          // sight 14
        EntityHandle shooter = Place(sim, 1, Crossbowman, FogMaps.Cell(10, 24) + new Vector2(15.6f, 0f)); // range 15 edge to edge
        sim.Enqueue(Command.HoldPosition(1, shooter));
        int full = u.Hp[victim.Index];
        RunUntil(sim, () => u.Hp[victim.Index] < full, 200);
        Assert.True(u.Hp[victim.Index] < full, "never hit");
        Assert.Equal(0, w.Fog.RevealEnd(0, shooter.Index));
        Assert.False(w.Fog.CanSeeUnit(0, shooter.Index));
        // Hit, but blind to the shooter: no retaliation (until M4-5's Revealed status, docs/02).
        Assert.Equal(default, u.Target[victim.Index]);
        Assert.Equal(shooter, u.LastAttacker[victim.Index]);
    }

    [Fact]
    public void ARecycledSlot_NeverInheritsAReveal_AndTheHashSaysSo()
    {
        Simulation a = FogMaps.Sim(FogMaps.TwoLevel()), b = FogMaps.Sim(FogMaps.TwoLevel());
        EntityHandle ha = Place(a, 1, Crossbowman, High), hb = Place(b, 1, Crossbowman, High);
        a.Tick();
        b.Tick();
        a.World.Fog.Reveal(ha, 0, 10_000);
        Assert.True(a.World.Fog.CanSeeUnit(0, ha.Index));
        Assert.NotEqual(a.StateHash(), b.StateHash());
        a.World.Units.Free(ha);
        b.World.Units.Free(hb);
        EntityHandle na = Place(a, 1, Crossbowman, FogMaps.Cell(34, 34)), nb = Place(b, 1, Crossbowman, FogMaps.Cell(34, 34));
        Assert.Equal(ha.Index, na.Index); // the same slot, a new generation
        Assert.Equal(nb.Index, na.Index);
        Assert.False(a.World.Fog.CanSeeUnit(0, na.Index));
        Assert.Equal(0, a.World.Fog.RevealEnd(0, na.Index));
        Assert.Equal(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void AShotLandingAfterItsShooterDied_RevealsNothing()
    {
        Simulation sim = FogMaps.Sim(FogMaps.TwoLevel());
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle victim = Place(sim, 0, HeavyInfantry, Low);
        EntityHandle shooter = Place(sim, 1, Crossbowman, High);
        sim.Enqueue(Command.HoldPosition(0, victim));
        sim.Enqueue(Command.HoldPosition(1, shooter));
        sim.Tick();
        u.CooldownTicks[shooter.Index] = 1_000_000;
        u.Target[shooter.Index] = victim;
        ProjectileSystem.Fire(w, shooter.Index);
        u.Free(shooter);
        int full = u.Hp[victim.Index];
        RunUntil(sim, () => w.Projectiles.Count == 0, 40);
        Assert.True(u.Hp[victim.Index] < full, "the bolt missed");
        var until = (int[])typeof(FogStore).GetField("_revealUntil", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(w.Fog)!;
        Assert.All(until, e => Assert.Equal(0, e)); // no entry was ever written
    }
}
