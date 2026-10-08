using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Vision;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-3a criterion 4: combat takes only what the owner sees. On the two-level map (<see cref="FogMaps.TwoLevel"/>) a low
/// Crossbowman can't shoot a still enemy up on the plateau, the high one shoots down, the low one answers the revealed
/// shooter and loses it 40 ticks after the last hit; an Attack on an unseen enemy is dropped; a chased target walking up
/// out of vision is given up.
/// </summary>
public class FogTargetingTests
{
    /// <summary>Level 0, 10 m west of <see cref="High"/>.</summary>
    private static readonly Vector2 Low = FogMaps.Cell(16, 30);

    /// <summary>Level 1, on the plateau (x 21: the column at x 20 is the cliff).</summary>
    private static readonly Vector2 High = FogMaps.Cell(21, 30);

    [Fact]
    public void TheScene_Is10mApart_OnLevels0And1()
    {
        Simulation sim = FogMaps.Sim(FogMaps.TwoLevel());
        Assert.Equal(10f, Vector2.Distance(Low, High));
        Assert.Equal(0, sim.World.Fog.LevelAt(Low));
        Assert.Equal(1, sim.World.Fog.LevelAt(High));
        Assert.True(sim.World.NavGrid.IsPassable(21, 30));
    }

    [Fact]
    public void ALowCrossbowman_NeverTakesAStillEnemyOnTheHighGround_AndDealsNoDamage()
    {
        Simulation sim = FogMaps.Sim(FogMaps.TwoLevel());
        UnitStore u = sim.World.Units;
        EntityHandle shooter = Place(sim, 0, Crossbowman, Low);
        EntityHandle still = Place(sim, 1, HeavyInfantry, High);
        sim.Enqueue(Command.HoldPosition(1, still)); // melee, holding: it never comes down
        int full = u.Hp[still.Index];
        for (int t = 0; t < 200; t++)
        {
            sim.Tick();
            Assert.Equal(default, u.Target[shooter.Index]);
        }
        Assert.Equal(full, u.Hp[still.Index]);
        Assert.Equal(0, sim.World.Projectiles.Count);
        Assert.False(sim.World.Fog.CanSeeUnit(0, still.Index));
        Assert.True(sim.World.Fog.CanSeeUnit(1, shooter.Index)); // high ground sees down
    }

    [Fact]
    public void TheHighCrossbowmanShoots_TheLowOneFiresBackWithin40Ticks_AndLosesIt40TicksAfterTheLastHit()
    {
        Simulation sim = FogMaps.Sim(FogMaps.TwoLevel());
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle low = Place(sim, 0, Crossbowman, Low);
        EntityHandle high = Place(sim, 1, Crossbowman, High);
        int lowFull = u.Hp[low.Index], highFull = u.Hp[high.Index];
        // The high one takes the low one and fires.
        RunUntil(sim, () => u.Hp[low.Index] < lowFull, 200);
        Assert.True(u.Hp[low.Index] < lowFull, "the high crossbowman never hit");
        int firstHit = sim.TickNumber - 1;
        Assert.Equal(high, u.Target[low.Index]); // retaliation took the revealed shooter at once
        // The low one fires back within 40 ticks of the first hit.
        int fired = -1;
        for (int t = 0; t < 40 && fired < 0; t++)
        {
            sim.Tick();
            for (int k = 0; k < w.Projectiles.Capacity; k++)
                if (w.Projectiles.Alive[k] && w.Projectiles.Owner[k] == 0) fired = sim.TickNumber - 1;
        }
        Assert.True(fired >= 0 && fired - firstHit <= 40, $"first hit at tick {firstHit}, low shot at {fired}");
        RunUntil(sim, () => u.Hp[high.Index] < highFull, 100);
        Assert.True(u.Hp[high.Index] < highFull, "the low crossbowman's shot never hit");

        // The high one stops shooting (a test seam: its cooldown never ends); the last hit's reveal runs out 40 ticks later.
        u.CooldownTicks[high.Index] = 1_000_000;
        int lastHit = firstHit, hp = u.Hp[low.Index];
        for (int t = 0; t < 120; t++)
        {
            sim.Tick();
            if (u.Hp[low.Index] < hp)
            {
                hp = u.Hp[low.Index];
                lastHit = sim.TickNumber - 1;
            }
        }
        Assert.True(sim.TickNumber > lastHit + 60);
        Assert.Equal(lastHit + VisionConstants.HighGroundRevealTicks, w.Fog.RevealEnd(0, high.Index));
        Assert.False(w.Fog.CanSeeUnit(0, high.Index));
        Assert.Equal(default, u.Target[low.Index]);
        // Its scans take nothing from now on, and nothing more hits the high one.
        int highHp = u.Hp[high.Index];
        for (int t = 0; t < 200; t++)
        {
            sim.Tick();
            Assert.Equal(default, u.Target[low.Index]);
        }
        Assert.Equal(highHp, u.Hp[high.Index]);
    }

    [Fact]
    public void TheRevealEndsExactly40TicksAfterTheHit()
    {
        Simulation sim = FogMaps.Sim(FogMaps.TwoLevel());
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle low = Place(sim, 0, HeavyInfantry, Low);
        EntityHandle high = Place(sim, 1, Crossbowman, High);
        sim.Enqueue(Command.HoldPosition(0, low)); // melee, holding: it can't answer
        int full = u.Hp[low.Index];
        RunUntil(sim, () => u.Hp[low.Index] < full, 200);
        int hit = sim.TickNumber - 1;
        u.CooldownTicks[high.Index] = 1_000_000;
        while (sim.TickNumber < hit + VisionConstants.HighGroundRevealTicks)
        {
            Assert.True(w.Fog.CanSeeUnit(0, high.Index), $"tick {sim.TickNumber}");
            sim.Tick();
        }
        Assert.False(w.Fog.CanSeeUnit(0, high.Index));
    }

    [Fact]
    public void AnAttackOnAnUnseenEnemy_IsDropped_AndOnASeenOneTaken()
    {
        Simulation sim = FogMaps.Sim(FogMaps.TwoLevel());
        UnitStore u = sim.World.Units;
        EntityHandle a = Place(sim, 0, Crossbowman, Low);
        EntityHandle up = Place(sim, 1, HeavyInfantry, High);
        EntityHandle down = Place(sim, 1, Laborer, FogMaps.Cell(10, 30)); // level 0, 12 m west
        sim.Enqueue(Command.HoldPosition(1, up));
        sim.Enqueue(Command.HoldPosition(1, down));
        sim.Enqueue(Command.HoldPosition(0, a));
        sim.Tick();
        sim.Enqueue(Command.Attack(0, a, up, isBuilding: false));
        sim.Enqueue(Command.Attack(0, a, up, isBuilding: false, queued: true));
        sim.Tick();
        sim.Tick(); // a command applies in the tick after the one running when it was queued
        Assert.Equal(0, sim.PendingCommandCount);
        Assert.Equal(default, u.Target[a.Index]);
        Assert.NotEqual(CombatMode.Ordered, u.Mode[a.Index]);
        Assert.Equal(0, u.QueueCount[a.Index]);
        Assert.False(CombatSystem.MayAttack(sim.World, a.Index, up, false));
        sim.Enqueue(Command.Attack(0, a, down, isBuilding: false));
        sim.Tick();
        sim.Tick();
        Assert.Equal(down, u.Target[a.Index]);
        Assert.Equal(CombatMode.Ordered, u.Mode[a.Index]);
    }

    [Fact]
    public void AChasedTargetWalkingUpOutOfVision_IsGivenUp()
    {
        Simulation sim = FogMaps.Sim(FogMaps.TwoLevel());
        World w = sim.World;
        UnitStore u = w.Units;
        // The chaser (Heavy Infantry, 3 m/s, sight 14) 10 m behind a Laborer (4 m/s) on the ramp, both level 0.
        EntityHandle chaser = Place(sim, 0, HeavyInfantry, FogMaps.Cell(12, 19));
        EntityHandle runner = Place(sim, 1, Laborer, FogMaps.Cell(17, 19));
        sim.Enqueue(Command.Attack(0, chaser, runner, isBuilding: false));
        sim.Enqueue(Command.Move(1, runner, FogMaps.Cell(34, 19))); // up the ramp and across the plateau
        sim.Tick();
        sim.Tick();
        Assert.Equal(runner, u.Target[chaser.Index]);
        int lostAt = RunUntil(sim, () => u.Target[chaser.Index] == default, 400);
        Assert.Equal(default, u.Target[chaser.Index]);
        Assert.True(u.IsAlive(runner));
        Assert.Equal(1, w.Fog.LevelAt(u.Position[runner.Index]));
        Assert.Equal(0, w.Fog.LevelAt(u.Position[chaser.Index]));
        Assert.True(Vector2.Distance(u.Position[runner.Index], u.Position[chaser.Index]) > VisionConstants.LipRadius);
        Assert.Equal(runner, u.Ignored[chaser.Index]); // given up (it was falling behind), not just dropped
        Assert.Equal(CombatMode.None, u.Mode[chaser.Index]);
        Assert.True(Vector2.Distance(u.Position[runner.Index], u.Position[chaser.Index]) < TestSim.Data.Units[HeavyInfantry].Sight,
            "lost on the plateau, not by walking out of sight range");
        Assert.True(lostAt > 0);
        // Nothing takes it again while it stands up there out of sight.
        for (int t = 0; t < 100; t++)
        {
            sim.Tick();
            Assert.Equal(default, u.Target[chaser.Index]);
        }
    }
}
