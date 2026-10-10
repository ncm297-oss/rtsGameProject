using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using static Rts.Sim.Tests.AbilityScenes;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-4b-2 criterion 2: Sandstorm (docs/factions/whirlwind.md "Abilities") cast by a Priest leaves a zone for exactly 12 s
/// (240 ticks) that Blinds and Slows (0.3) the enemy units inside every tick, spares its owner's, and lets its statuses
/// linger 1 s after a unit leaves; a cast while the zone store is full makes no zone.
/// </summary>
[Collection(SerialCollection.Name)]
public class ZoneSystemTests
{
    private static int Slot(EntityHandle h) => h.Index * DataLimits.MaxUnitAbilities;

    /// <summary>A Priest of player 1 at cell (10, 24) casts Sandstorm 10 m east, at cell (15, 24)'s centre; returns the resolve tick.</summary>
    private static int CastSandstorm(Simulation sim, out EntityHandle priest, out Vector2 point)
    {
        priest = Place(sim, 1, Priest, At(sim, 10, 24));
        point = At(sim, 15, 24);
        sim.Enqueue(Command.UseAbility(1, priest, 0, point));
        return TickOf(sim, priest, resolved: true);
    }

    [Fact]
    public void Sandstorm_LivesExactly240Ticks_ThenIsFreed()
    {
        Simulation sim = NoFights();
        ZoneStore z = sim.World.Zones;
        Assert.Equal(0, z.Count);
        int resolve = CastSandstorm(sim, out EntityHandle priest, out Vector2 point);
        Assert.Equal(1, z.Count);
        Assert.True(z.Alive[0]);
        Assert.Equal(1, z.Owner[0]);
        Assert.Equal(point, z.Center[0]);
        Assert.Equal(sim.World.Data.FindAbility("sandstorm"), z.AbilityId[0]);
        Assert.Equal(240, z.TicksRemaining[0]);
        Assert.Equal(6f, z.Radius(0));
        Assert.True(z.BlocksVision(0));
        Assert.Equal(1, z.BlockerCount);
        Assert.Equal(resolve + 900, sim.World.Units.AbilityReadyTick[Slot(priest)]); // 45 s
        GatherMaps.Run(sim, 239); // ticks resolve + 1 .. resolve + 239: its 240th tick is the last
        Assert.Equal(1, z.Count);
        Assert.Equal(1, z.TicksRemaining[0]);
        sim.Tick(); // resolve + 240: freed in phase 5
        Assert.Equal(0, z.Count);
        Assert.Equal(0, z.BlockerCount);
        Assert.False(z.Alive[0]);
    }

    [Fact]
    public void AnEnemyInside_IsBlindedAndSlowedEveryTick_TheOwnersUnitIsUntouched()
    {
        Simulation sim = NoFights();
        UnitStore u = sim.World.Units;
        GameData d = sim.World.Data;
        Vector2 point = At(sim, 15, 24);
        EntityHandle enemy = Place(sim, 0, Crossbowman, Off(point, 3f));
        EntityHandle edge = Place(sim, 0, Crossbowman, Off(point, 0f, 6f));     // exactly on the radius: inside
        EntityHandle outside = Place(sim, 0, Crossbowman, Off(point, 0f, -6.01f));
        EntityHandle own = Place(sim, 1, Raider, Off(point, -2f));
        int blinded = d.FindStatus("blinded");
        CastSandstorm(sim, out _, out _);
        float speed = d.Units[Crossbowman].SpeedPerTick;
        for (int t = 0; t < 239; t++)
        {
            foreach (EntityHandle h in new[] { enemy, edge })
            {
                Assert.Equal((0f, 20), StatusOf(sim, h, blinded));
                Assert.Equal((0.3f, 20), StatusOf(sim, h, Slowed));
                Assert.Equal(blinded, u.Statuses.BlindOf(h.Index));
                Assert.Equal(speed * 0.7f, u.Speed[h.Index], 5);
            }
            Assert.Equal(0, u.Statuses.Count[outside.Index]);
            Assert.Equal(0, u.Statuses.Count[own.Index]);
            Assert.Equal(-1, u.Statuses.BlindOf(own.Index));
            sim.Tick();
        }
        // Freed in the next tick's phase 5: what it applied last lingers its 20 ticks.
        sim.Tick();
        Assert.Equal(0, sim.World.Zones.Count);
        Assert.Equal((0f, 19), StatusOf(sim, enemy, blinded));
        GatherMaps.Run(sim, 19);
        Assert.Equal(0, u.Statuses.Count[enemy.Index]);
        Assert.Equal(-1, u.Statuses.BlindOf(enemy.Index));
        Assert.Equal(speed, u.Speed[enemy.Index]);
    }

    [Fact]
    public void AUnitThatStepsOut_LosesBothStatuses1sLater()
    {
        Simulation sim = NoFights();
        UnitStore u = sim.World.Units;
        int blinded = sim.World.Data.FindStatus("blinded");
        Vector2 point = At(sim, 15, 24);
        EntityHandle enemy = Place(sim, 0, Crossbowman, Off(point, 0f, 5f));
        CastSandstorm(sim, out _, out _);
        GatherMaps.Run(sim, 30);
        Assert.Equal((0f, 20), StatusOf(sim, enemy, blinded));
        // It steps out between two ticks (a test seam: no walk, so nothing else changes).
        u.Position[enemy.Index] = Off(point, 0f, 6.5f);
        for (int t = 1; t <= 19; t++)
        {
            sim.Tick();
            Assert.Equal((0f, 20 - t), StatusOf(sim, enemy, blinded));
            Assert.Equal((0.3f, 20 - t), StatusOf(sim, enemy, Slowed));
        }
        sim.Tick(); // the 20th tick outside: 1 s
        Assert.Equal(0, u.Statuses.Count[enemy.Index]);
        Assert.Equal(-1, u.Statuses.BlindOf(enemy.Index));
        // Back in, it takes both again on the next tick.
        u.Position[enemy.Index] = Off(point, 0f, 5f);
        sim.Tick();
        Assert.Equal((0f, 20), StatusOf(sim, enemy, blinded));
    }

    /// <summary>The documented store-full rule: a zone cast while every slot is taken is not made; the cast still resolves and its cooldown starts.</summary>
    [Fact]
    public void AStoreFullCast_MakesNoZone_ButResolvesWithItsCooldown()
    {
        Simulation sim = TestSim.Explored(new Simulation(
            TestSim.ConfigNoCombat(Seed: 1, PlayerCount: 2, UnitCapacity: 64, CommandCapacity: 512) with { ZoneCapacity = 1 }, LocalMovementTests.Flat(48)));
        ZoneStore z = sim.World.Zones;
        Assert.Equal(1, z.Capacity);
        CastSandstorm(sim, out _, out Vector2 first);
        EntityHandle second = Place(sim, 1, Priest, At(sim, 10, 30));
        EntityHandle victim = Place(sim, 0, Crossbowman, At(sim, 15, 32));
        sim.Enqueue(Command.UseAbility(1, second, 0, At(sim, 15, 32)));
        int resolve = TickOf(sim, second, resolved: true);
        Assert.Equal(1, z.Count);
        Assert.Equal(first, z.Center[0]);
        Assert.Equal(resolve + 900, sim.World.Units.AbilityReadyTick[Slot(second)]);
        sim.Tick();
        Assert.Equal(0, sim.World.Units.Statuses.Count[victim.Index]);
    }

    /// <summary>Dryjhna's Prophecy (docs/factions/whirlwind.md) takes Sandstorm's cooldown from 45 s to 30 s (600 ticks).</summary>
    [Fact]
    public void DryjhnasProphecy_SandstormCooldown30s()
    {
        Simulation sim = NoFights();
        sim.World.Techs.Set(1, sim.World.Data.FindTech("dryjhnas_prophecy"), true);
        int resolve = CastSandstorm(sim, out EntityHandle priest, out _);
        Assert.Equal(resolve + 600, sim.World.Units.AbilityReadyTick[Slot(priest)]);
    }

    /// <summary>The cast stands 1.2 s (24 ticks counting its first) before the zone is made; the zone is made on the resolve tick.</summary>
    [Fact]
    public void Sandstorm_Casts24Ticks_TheZoneAppearsOnTheResolve()
    {
        Simulation sim = NoFights();
        EntityHandle priest = Place(sim, 1, Priest, At(sim, 10, 24));
        sim.Enqueue(Command.UseAbility(1, priest, 0, At(sim, 15, 24)));
        int start = TickOf(sim, priest, resolved: false);
        Assert.Equal(0, sim.World.Zones.Count);
        int resolve = 0;
        for (int t = 0; t < 30 && sim.World.Zones.Count == 0; t++)
        {
            sim.Tick();
            resolve = sim.TickNumber - 1;
        }
        Assert.True(Had(sim, priest, resolved: true));
        Assert.Equal(24, resolve - start + 1);
    }

    /// <summary>With no zone the phase-5 pass is skipped whole: no allocation, and an empty store adds nothing to the hash.</summary>
    [Fact]
    public void NoZones_RunAllocatesNothing_AndHashesNothing()
    {
        Simulation sim = NoFights();
        ulong before = sim.StateHash();
        long bytes = AllocationProbe.Measure(() => ZoneSystem.Run(sim.World));
        Assert.Equal(0, bytes);
        Assert.Equal(before, sim.StateHash());
    }

    /// <summary>The Priest of the Whirlwind (Sandstorm is its ability 0).</summary>
    public static int Priest => TestSim.Data.FindUnit("whirlwind_priest");
}
