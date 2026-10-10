using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Vision;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-4b-2 criterion 3: Blinded (docs/02 "Status effects": sight 2 m, can't acquire or attack beyond 3 m; both numbers in
/// <c>statuses.json</c>). A Blinded Desert Archer takes nothing at 5 m, takes an enemy at 2.5 m, fires at an ordered
/// target only once within 3 m, and its fog circle is 2 m until the status ends.
/// </summary>
[Collection(SerialCollection.Name)]
public class BlindedTests
{
    private static int Archer => TestSim.Data.FindUnit("whirlwind_desert_archer");

    private static int Blinded => TestSim.Data.FindStatus("blinded");

    /// <summary>Blinds unit <paramref name="h"/> for <paramref name="ticks"/> ticks (as a zone would, a test seam).</summary>
    private static void Blind(Simulation sim, EntityHandle h, int ticks) =>
        StatusSystem.Apply(sim.World, h.Index, Blinded, 0f, ticks, 1 - sim.World.Units.Owner[h.Index]);

    [Fact]
    public void ABlindedArcher_TakesNothingAt5m_ButTakesAnEnemyAt2_5m()
    {
        Simulation sim = Flat();
        UnitStore u = sim.World.Units;
        EntityHandle archer = Place(sim, 1, Archer, At(sim, 20, 20));
        EntityHandle far = Place(sim, 0, Laborer, At(sim, 20, 20, 5f)); // a worker never fights back
        Blind(sim, archer, 4000);
        int hp = u.Hp[far.Index];
        GatherMaps.Run(sim, 80);
        Assert.Equal(0, u.Target[archer.Index].Generation);
        Assert.Equal(hp, u.Hp[far.Index]);
        Assert.Equal(At(sim, 20, 20), u.Position[archer.Index]); // it did not walk to it either

        EntityHandle near = Place(sim, 0, Laborer, At(sim, 20, 20, 0f, -2.5f));
        int nearHp = u.Hp[near.Index];
        RunUntil(sim, () => u.Hp[near.Index] < nearHp, 200);
        Assert.True(u.Hp[near.Index] < nearHp, "the Blinded archer never shot the enemy 2.5 m away");
        Assert.Equal(hp, u.Hp[far.Index]);
    }

    [Fact]
    public void ABlindedArcher_FiresAtAnOrderedTarget_OnlyWithin3m()
    {
        Simulation sim = Flat();
        UnitStore u = sim.World.Units;
        EntityHandle archer = Place(sim, 1, Archer, At(sim, 20, 20));
        EntityHandle target = Place(sim, 0, Laborer, At(sim, 20, 20, 6f));
        Blind(sim, archer, 4000);
        sim.Enqueue(Command.Attack(1, archer, target, false));
        int hp = u.Hp[target.Index];
        bool fired = false;
        for (int t = 0; t < 400 && !fired; t++)
        {
            int shots = sim.World.Projectiles.Count;
            sim.Tick();
            float d = Vector2.Distance(u.Position[archer.Index], u.Position[target.Index]);
            if (u.State[archer.Index] == UnitState.Attacking) Assert.True(d <= 3f + 1e-4f, $"planted to shoot at {d} m");
            if (sim.World.Projectiles.Count > shots)
            {
                fired = true;
                Assert.True(d <= 3f + 1e-4f, $"fired from {d} m");
            }
        }
        Assert.True(fired, "the Blinded archer never fired at its ordered target");
        Assert.Equal(target, u.Target[archer.Index]);
        Assert.True(Vector2.Distance(At(sim, 20, 20), u.Position[archer.Index]) > 2.9f, "it walked closer");
        RunUntil(sim, () => u.Hp[target.Index] < hp, 100);
        Assert.True(u.Hp[target.Index] < hp);
    }

    [Fact]
    public void ABlindedUnitsFogCircle_Is2m_AndReturnsToItsTypesSightWhenTheStatusEnds()
    {
        Simulation sim = Flat();
        FogStore fog = sim.World.Fog;
        int w = fog.Width;
        EntityHandle archer = Place(sim, 1, Archer, At(sim, 20, 20));
        Blind(sim, archer, 10);
        Assert.Equal(2f, StatusSystem.SightOf(sim.World, archer.Index));
        fog.Update();
        Assert.True(fog.IsVisible(1, 20 * w + 20));
        Assert.True(fog.IsVisible(1, 20 * w + 21));  // 2 m east
        Assert.True(fog.IsVisible(1, 19 * w + 20));  // 2 m north
        Assert.False(fog.IsVisible(1, 21 * w + 21)); // 2.83 m: outside a 2 m circle
        Assert.False(fog.IsVisible(1, 20 * w + 22)); // 4 m
        Assert.False(fog.IsVisible(1, 20 * w + 18));
        GatherMaps.Run(sim, 12); // the status ends, and a fog update follows
        Assert.Equal(-1, sim.World.Units.Statuses.BlindOf(archer.Index));
        Assert.Equal(18f, StatusSystem.SightOf(sim.World, archer.Index)); // the type's sight
        Assert.True(fog.IsVisible(1, 21 * w + 21));
        Assert.True(fog.IsVisible(1, 20 * w + 28)); // 16 m east
        Assert.False(fog.IsVisible(1, 20 * w + 30)); // 20 m: beyond 18
    }

    /// <summary>A Blinded caster still casts at its ability's range: a cast point is not a target it acquires.</summary>
    [Fact]
    public void ABlindedCaster_StillCastsAtFullRange()
    {
        Simulation sim = AbilityScenes.NoFights();
        EntityHandle priest = Place(sim, 1, ZoneSystemTests.Priest, At(sim, 10, 24));
        Blind(sim, priest, 4000);
        sim.Enqueue(Command.UseAbility(1, priest, 0, At(sim, 18, 24))); // 16 m
        int start = AbilityScenes.TickOf(sim, priest, resolved: false);
        Assert.Equal(At(sim, 10, 24), sim.World.Units.Position[priest.Index]); // cast from where it stood
        AbilityScenes.TickOf(sim, priest, resolved: true);
        Assert.Equal(1, sim.World.Zones.Count);
        Assert.True(start > 0);
    }

    /// <summary>A Blinded unit hit from beyond its reach does not take its attacker (no retaliation it couldn't land).</summary>
    [Fact]
    public void ABlindedUnit_HitFromBeyondItsReach_DoesNotRetaliate()
    {
        Simulation sim = Flat();
        UnitStore u = sim.World.Units;
        EntityHandle raider = Place(sim, 1, Raider, At(sim, 20, 20));
        EntityHandle shooter = Place(sim, 0, Crossbowman, At(sim, 20, 20, 8f));
        u.Hold[shooter.Index] = true;
        Blind(sim, raider, 4000);
        int hp = u.Hp[raider.Index];
        RunUntil(sim, () => u.Hp[raider.Index] < hp, 200);
        Assert.True(u.Hp[raider.Index] < hp);
        GatherMaps.Run(sim, 8);
        Assert.Equal(0, u.Target[raider.Index].Generation);
        Assert.Equal(At(sim, 20, 20), u.Position[raider.Index]);
    }
}
