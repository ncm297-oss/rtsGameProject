using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M7-VH1 (BUG-1179): <see cref="ShotEnds"/>, the read-only accessor for a projectile's attacker type and victim.</summary>
[Collection(SerialCollection.Name)]
public class ShotEndsTests
{
    private static GameData Data => TestSim.Data;
    private static int Bolt => Data.FindProjectile("bolt");

    private static EntityHandle Unit(UnitStore u, string key, int owner = 0)
    {
        int type = Data.FindUnit(key);
        Assert.True(u.TrySpawn(owner, type, Data.Units[type], new Vector2(5f, 5f), out EntityHandle h));
        return h;
    }

    private static void Shot(ProjectileStore s, int attackerType, EntityHandle attacker, EntityHandle victim,
        bool victimIsBuilding = false, bool attackerIsBuilding = false) =>
        Assert.True(s.TrySpawn(new Vector2(1f, 1f), new Vector2(9f, 1f), Data.Projectiles[Bolt].SpeedPerTick, Bolt, 0,
            attackerType, attacker, victim, victimIsBuilding, attackerIsBuilding: attackerIsBuilding));

    [Fact]
    public void AFreeOrOutOfRangeSlot_IsNotLive_AndNamesNoOne()
    {
        var s = new ProjectileStore(2);
        var u = new UnitStore(4);
        foreach (int slot in new[] { 0, 1, -1, 2 })
        {
            ShotEnds e = ShotEnds.Of(s, slot);
            Assert.False(e.Live);
            Assert.Equal(-1, e.ShooterUnitType);
            Assert.Equal(-1, e.VictimUnitType(u.Alive, u.Generation, u.TypeId));
        }
    }

    [Fact]
    public void AUnitsShot_NamesItsShooterType_AndItsLiveVictimsType()
    {
        var s = new ProjectileStore(2);
        var u = new UnitStore(4);
        EntityHandle archer = Unit(u, "whirlwind_desert_archer");
        EntityHandle crossbow = Unit(u, "malazan_crossbowman", owner: 1);
        Shot(s, u.TypeId[archer.Index], archer, crossbow);
        ShotEnds e = ShotEnds.Of(s, 0);
        Assert.True(e.Live);
        Assert.False(e.AttackerIsBuilding);
        Assert.Equal(archer, e.Attacker);
        Assert.Equal(crossbow, e.Victim);
        Assert.Equal(Data.FindUnit("whirlwind_desert_archer"), e.ShooterUnitType);
        Assert.Equal(Data.FindUnit("malazan_crossbowman"), e.VictimUnitType(u.Alive, u.Generation, u.TypeId));
    }

    [Fact]
    public void AShooterKilledTheTickItFired_KeepsItsType()
    {
        // BUG-1179: the inference found no live shooter at the launch point and drew the shot from 1.2 m.
        var s = new ProjectileStore(2);
        var u = new UnitStore(4);
        EntityHandle archer = Unit(u, "whirlwind_desert_archer");
        EntityHandle victim = Unit(u, "malazan_crossbowman", owner: 1);
        Shot(s, u.TypeId[archer.Index], archer, victim);
        u.Free(archer);
        Unit(u, "malazan_laborer"); // the archer's slot taken again: the shot still knows its type
        Assert.Equal(Data.FindUnit("whirlwind_desert_archer"), ShotEnds.Of(s, 0).ShooterUnitType);
    }

    [Fact]
    public void AVictimKilledAfterTheShot_IsItsCorpsesType_UntilItsSlotIsTakenAgain()
    {
        var s = new ProjectileStore(2);
        var u = new UnitStore(4);
        EntityHandle archer = Unit(u, "whirlwind_desert_archer");
        EntityHandle victim = Unit(u, "malazan_crossbowman", owner: 1);
        Shot(s, u.TypeId[archer.Index], archer, victim);
        u.Free(victim);
        Assert.Equal(Data.FindUnit("malazan_crossbowman"), ShotEnds.Of(s, 0).VictimUnitType(u.Alive, u.Generation, u.TypeId));
        EntityHandle next = Unit(u, "malazan_laborer");
        Assert.Equal(victim.Index, next.Index); // the free list is last in, first out
        Assert.Equal(-1, ShotEnds.Of(s, 0).VictimUnitType(u.Alive, u.Generation, u.TypeId));
        u.Free(next); // dead again, two generations on: still not the victim
        Assert.Equal(-1, ShotEnds.Of(s, 0).VictimUnitType(u.Alive, u.Generation, u.TypeId));
    }

    [Fact]
    public void ATowersShot_HasNoShooterUnitType_AndABuildingVictimHasNoUnitType()
    {
        var s = new ProjectileStore(2);
        var u = new UnitStore(4);
        int tower = Data.FindBuilding("malazan_watchtower");
        EntityHandle victim = Unit(u, "malazan_crossbowman", owner: 1);
        Shot(s, tower, new EntityHandle(3, 1), victim, attackerIsBuilding: true);
        Shot(s, Data.FindUnit("malazan_crossbowman"), new EntityHandle(0, 0), new EntityHandle(2, 0), victimIsBuilding: true);
        ShotEnds fromTower = ShotEnds.Of(s, 0);
        Assert.True(fromTower.AttackerIsBuilding);
        Assert.Equal(tower, fromTower.AttackerType);
        Assert.Equal(-1, fromTower.ShooterUnitType);
        Assert.Equal(Data.FindUnit("malazan_crossbowman"), fromTower.VictimUnitType(u.Alive, u.Generation, u.TypeId));
        ShotEnds atBuilding = ShotEnds.Of(s, 1);
        Assert.True(atBuilding.VictimIsBuilding);
        Assert.Equal(-1, atBuilding.VictimUnitType(u.Alive, u.Generation, u.TypeId));
    }

    [Fact]
    public void ReadingEverySlotEveryTick_ChangesNoHash_AllocatesNothing_AndEveryShotHasBothEnds()
    {
        Simulation a = Archers(), b = Archers();
        int shots = 0;
        long bytes = 0;
        for (int t = 0; t < 400; t++)
        {
            a.Tick();
            b.Tick();
            ulong before = a.StateHash();
            ProjectileStore s = a.World.Projectiles;
            long b0 = GC.GetAllocatedBytesForCurrentThread();
            int live = 0, named = 0;
            for (int i = 0; i < s.Capacity; i++)
            {
                ShotEnds e = ShotEnds.Of(s, i);
                if (!e.Live) continue;
                live++;
                int v = e.VictimUnitType(a.World.Units.Alive, a.World.Units.Generation, a.World.Units.TypeId);
                if (e.ShooterUnitType >= 0 && v >= 0) named++;
            }
            bytes += GC.GetAllocatedBytesForCurrentThread() - b0;
            Assert.Equal(before, a.StateHash());
            Assert.Equal(b.StateHash(), a.StateHash());
            shots += live;
            // Every shot is seen within a tick of its firing here, so its victim is alive or still its corpse.
            Assert.True(named == live, $"tick {t}: {live - named} of {live} live shots without both ends");
        }
        Assert.True(shots > 50, $"only {shots} shot-ticks seen");
        Assert.Equal(0, bytes);
    }

    // Twelve Crossbowmen against twelve Raiders on a flat map, attack-moved at each other.
    private static Simulation Archers()
    {
        var sim = new Simulation(TestSim.Config(Seed: 5, PlayerCount: 2, UnitCapacity: 64, CommandCapacity: 512), LocalMovementTests.Flat(48));
        int crossbow = Data.FindUnit("malazan_crossbowman"), raider = Data.FindUnit("whirlwind_raider");
        for (int k = 0; k < 12; k++)
        {
            float y = 48f + (k - 5.5f) * 1.4f;
            sim.Enqueue(Command.SpawnUnit(0, crossbow, new Vector2(36f, y)));
            sim.Enqueue(Command.SpawnUnit(1, raider, new Vector2(60f, y)));
        }
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            int p = u.Owner[i];
            sim.Enqueue(Command.AttackMove(p, new EntityHandle(i, u.Generation[i]), new Vector2(p == 0 ? 70f : 26f, 48f)));
        }
        return sim;
    }
}
