using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-2b criterion 4 (<c>attack.minRange</c>, docs/03 "Implementation (M4-2b)"): no swing at a target inside the minimum
/// range (edge to edge); a scan never takes one, so an attack-mover or an Idle unit re-picks one outside it; an explicit
/// Attack on a too-near target keeps it and waits where it stands (no step back: no kiting in this slice).
/// </summary>
public class MinRangeTests
{
    private static int Catapult => ProjectileTests.Catapult;

    private static float MinRange => TestSim.Data.Units[Catapult].Attack.MinRange;

    /// <summary>Center distance for an edge-to-edge gap of <paramref name="gap"/> m between a Catapult (0.9 m) and a Raider (0.4 m).</summary>
    private static float Centers(float gap) => gap + TestSim.Data.Units[Catapult].Radius + TestSim.Data.Units[Raider].Radius;

    [Fact]
    public void TheShippedCatapult_HasTheTemplates6mMinimumRange()
    {
        Assert.Equal(6f, MinRange);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACatapult_NeverFiresAtARaiderInsideItsMinimumRange(bool ordered)
    {
        Simulation sim = Flat();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle c = Place(sim, 0, Catapult, At(sim, 20, 20));
        EntityHandle r = Place(sim, 1, Raider, At(sim, 20, 20, dx: Centers(MinRange - 0.2f)));
        sim.Enqueue(Command.HoldPosition(1, r)); // holds out of its own melee reach: never strikes back
        if (ordered) sim.Enqueue(Command.Attack(0, c, r, isBuilding: false));
        int full = u.Hp[r.Index];
        Vector2 at = u.Position[c.Index];
        for (int t = 0; t < 200; t++)
        {
            sim.Tick();
            Assert.Equal(0, u.WindupTicks[c.Index]);
            Assert.Equal(0, w.Projectiles.Count);
            Assert.NotEqual(UnitState.Attacking, u.State[c.Index]);
        }
        Assert.Equal(full, u.Hp[r.Index]);
        if (ordered)
        {
            // The explicit Attack keeps its target and waits where it stands: no chase closer, no step back.
            Assert.Equal(r, u.Target[c.Index]);
            Assert.Equal(CombatMode.Ordered, u.Mode[c.Index]);
            Assert.Equal(UnitState.Idle, u.State[c.Index]);
            Assert.Equal(at, u.Position[c.Index]);
        }
        else
        {
            Assert.Equal(default, u.Target[c.Index]); // the scan never takes it
        }
    }

    [Fact]
    public void ACatapult_FiresAtARaiderJustOutsideItsMinimumRange()
    {
        Simulation sim = Flat();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle c = Place(sim, 0, Catapult, At(sim, 20, 20));
        EntityHandle r = Place(sim, 1, Raider, At(sim, 20, 20, dx: Centers(MinRange + 0.2f)));
        sim.Enqueue(Command.HoldPosition(1, r));
        int full = u.Hp[r.Index];
        RunUntil(sim, () => u.Hp[r.Index] < full, 200);
        Assert.True(u.Hp[r.Index] < full, "never fired");
        Assert.Equal(r, u.Target[c.Index]);
    }

    [Fact]
    public void AnOrderedCatapult_WhoseTargetStepsInsideTheMinimumRange_StopsWalking_AndFiresOnceItIsOutAgain()
    {
        Simulation sim = Flat();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle c = Place(sim, 0, Catapult, At(sim, 10, 20));
        EntityHandle r = Place(sim, 1, Raider, At(sim, 10, 20, dx: 40f));
        sim.Enqueue(Command.HoldPosition(1, r));
        Spot(sim, 0, r); // M4-3a: 40 m off, past the Catapult's sight (18 m)
        sim.Enqueue(Command.Attack(0, c, r, isBuilding: false));
        RunUntil(sim, () => u.State[c.Index] == UnitState.Moving, 20);
        Assert.Equal(UnitState.Moving, u.State[c.Index]); // chasing into range
        // The Raider is carried right up to it: the chase stops at once, it waits.
        u.Position[r.Index] = u.Position[c.Index] + new Vector2(Centers(2f), 0f);
        RunUntil(sim, () => false, 3);
        Assert.Equal(UnitState.Idle, u.State[c.Index]);
        Vector2 at = u.Position[c.Index];
        RunUntil(sim, () => false, 40);
        Assert.Equal(at, u.Position[c.Index]);
        Assert.Equal(r, u.Target[c.Index]);
        // Back out of the minimum range: it fires.
        int full = u.Hp[r.Index];
        u.Position[r.Index] = u.Position[c.Index] + new Vector2(Centers(MinRange + 3f), 0f);
        RunUntil(sim, () => u.Hp[r.Index] < full, 300);
        Assert.True(u.Hp[r.Index] < full, "never fired once the target was out of the minimum range");
    }

    [Fact]
    public void AnAttackMovingCatapult_RePicksAUnitOutsideTheMinimumRange_WhenItsTargetComesTooNear()
    {
        Simulation sim = Flat();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle c = Place(sim, 0, Catapult, At(sim, 20, 20));
        EntityHandle near = Place(sim, 1, Raider, At(sim, 20, 20, dx: Centers(8f)));
        EntityHandle far = Place(sim, 1, Raider, At(sim, 20, 20, dx: Centers(12f), dy: 2f));
        sim.Enqueue(Command.HoldPosition(1, near));
        sim.Enqueue(Command.HoldPosition(1, far));
        sim.Enqueue(Command.AttackMove(0, c, At(sim, 20, 20, dx: 30f)));
        RunUntil(sim, () => u.Target[c.Index] != default, 20);
        Assert.Equal(near, u.Target[c.Index]); // the nearer one first
        // The near one steps inside the minimum range: the next scan takes the far one.
        u.Position[near.Index] = u.Position[c.Index] + new Vector2(Centers(3f), 0f);
        // Mid-swing it keeps the target; the swing then ends with it too near (lost), and the next scan re-picks.
        RunUntil(sim, () => u.Target[c.Index] == far, TestSim.Data.Units[Catapult].Attack.WindupTicks + 2 * CombatConstants.ScanInterval);
        Assert.Equal(far, u.Target[c.Index]);
        Assert.Equal(CombatMode.AttackMove, u.Mode[c.Index]);
        int full = u.Hp[far.Index];
        RunUntil(sim, () => u.Hp[far.Index] < full, 300);
        Assert.True(u.Hp[far.Index] < full, "never fired at the far one");
        Assert.Equal(TestSim.Data.Units[Raider].Hp, u.Hp[near.Index]);
    }

    [Fact]
    public void AHitFromAnEnemyInsideTheMinimumRange_IsNotRetaliatedOn()
    {
        Simulation sim = Flat();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle c = Place(sim, 0, Catapult, At(sim, 20, 20));
        EntityHandle r = Place(sim, 1, Raider, At(sim, 20, 20, dx: Centers(0.2f)));
        sim.Enqueue(Command.Attack(1, r, c, isBuilding: false));
        int full = u.Hp[c.Index];
        RunUntil(sim, () => u.Hp[c.Index] < full, 100);
        Assert.True(u.Hp[c.Index] < full);
        Assert.Equal(r, u.LastAttacker[c.Index]);
        Assert.Equal(default, u.Target[c.Index]);
        RunUntil(sim, () => false, 20);
        Assert.Equal(default, u.Target[c.Index]);
        Assert.Equal(0, w.Projectiles.Count);
    }
}
