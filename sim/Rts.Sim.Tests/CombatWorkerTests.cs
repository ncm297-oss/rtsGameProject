using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// Producer decision (M4-1 fix round, from the view's BUG-0147): a unit of the worker slot never fights on its own. It
/// does not scan while Idle and does not retaliate when hit; it scans and swings only while attack-moving.
/// </summary>
public class CombatWorkerTests
{
    [Fact]
    public void TwoIdleEnemyLaborers_5MApart_TakeNoTarget_In400Ticks()
    {
        Simulation sim = Flat();
        EntityHandle a = Place(sim, 0, Laborer, At(sim, 20, 20));
        EntityHandle b = Place(sim, 1, Laborer, At(sim, 20, 20, dx: 5f));
        UnitStore u = sim.World.Units;
        for (int t = 0; t < 400; t++)
        {
            sim.Tick();
            Assert.True(u.Target[a.Index] == default && u.Target[b.Index] == default, $"tick {t}: a worker took a target");
            Assert.Equal(CombatMode.None, u.Mode[a.Index]);
            Assert.Equal(CombatMode.None, u.Mode[b.Index]);
        }
        Assert.Equal(UnitState.Idle, u.State[a.Index]);
        Assert.Equal(UnitState.Idle, u.State[b.Index]);
    }

    [Fact]
    public void IdleLaborer_HitByAnEnemy_NeverRetaliates()
    {
        Simulation sim = Flat();
        EntityHandle w = Place(sim, 0, Laborer, At(sim, 20, 20));
        EntityHandle e = Place(sim, 1, HeavyInfantry, At(sim, 20, 20, dx: 4f));
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.AttackMove(1, e, u.Position[w.Index]));
        bool hit = false;
        for (int t = 0; t < 600 && u.IsAlive(w); t++)
        {
            sim.Tick();
            if (!u.IsAlive(w)) break;
            hit |= u.Hp[w.Index] < TestSim.Data.Units[Laborer].Hp;
            Assert.Equal(default, u.Target[w.Index]);
            Assert.Equal(CombatMode.None, u.Mode[w.Index]);
            Assert.NotEqual(UnitState.Attacking, u.State[w.Index]);
        }
        Assert.True(hit || !u.IsAlive(w));
        Assert.False(u.IsAlive(w)); // it took the hits to the end
        Assert.Equal(TestSim.Data.Units[HeavyInfantry].Hp, u.Hp[e.Index]);
    }

    [Fact]
    public void AttackMovedLaborer_AcquiresAnEnemy_AndSwings()
    {
        Simulation sim = Flat();
        EntityHandle w = Place(sim, 0, Laborer, At(sim, 10, 20));
        EntityHandle e = Place(sim, 1, Laborer, At(sim, 20, 20));
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.AttackMove(0, w, At(sim, 30, 20)));
        bool targeted = false, attacking = false;
        int full = TestSim.Data.Units[Laborer].Hp;
        RunUntil(sim, () =>
        {
            targeted |= u.Target[w.Index] == e;
            attacking |= u.State[w.Index] == UnitState.Attacking;
            return !u.IsAlive(e) || u.Hp[e.Index] < full;
        }, 600);
        Assert.True(targeted);
        Assert.True(attacking);
        Assert.True(!u.IsAlive(e) || u.Hp[e.Index] < full);
    }

    /// <summary>
    /// BUG-0147's scene in the sim: five Idle laborers of each owner, the groups 26 m apart, player 0's Depot between them
    /// (inside both groups' sight). Before the rule player 1's laborers took the Depot, then player 0's laborers.
    /// </summary>
    [Fact]
    public void PlayableStyleOpening_IdleLaborersAndADepotBetween_NoDeathsIn6000Ticks()
    {
        Simulation sim = Flat(size: 64);
        int depotType = TestSim.Data.FindBuilding("malazan_depot");
        EntityHandle depot = GatherMaps.Building(sim, 31, 31, player: 0, type: depotType);
        var units = new List<EntityHandle>();
        for (int k = 0; k < 5; k++)
        {
            units.Add(Place(sim, 0, Laborer, At(sim, 25, 28 + 2 * k)));
            units.Add(Place(sim, 1, Laborer, At(sim, 38, 28 + 2 * k)));
        }
        UnitStore u = sim.World.Units;
        Assert.Equal(26f, u.Position[units[1].Index].X - u.Position[units[0].Index].X, 3);
        int depotHp = sim.World.Buildings.Hp[depot.Index];
        for (int t = 0; t < 6000; t++)
        {
            sim.Tick();
            Assert.True(sim.World.Deaths.Length == 0, $"tick {t}: a death");
        }
        foreach (EntityHandle h in units)
        {
            Assert.True(u.IsAlive(h));
            Assert.Equal(TestSim.Data.Units[Laborer].Hp, u.Hp[h.Index]);
        }
        Assert.Equal(depotHp, sim.World.Buildings.Hp[depot.Index]);
        Assert.Equal(0, sim.World.Kills[0] + sim.World.Kills[1]);
    }
}
