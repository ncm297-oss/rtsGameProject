using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using static Rts.Sim.Tests.AbilityScenes;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M4-4a, session 2026-10-09-0724): boundaries of UseAbility / Telas Fire and the status store the developer's rows
/// don't pin: the exact range edge, the cooldown boundary through real commands, cancels by Stop / Hold / a second cast,
/// a DoT kill on the pulse tick (with a second status on the victim), two casters of different players stacking, and the
/// slot a DoT victim leaves behind.
/// </summary>
[Collection(SerialCollection.Name)]
public class AbilityQaTests
{
    private static int Slot(EntityHandle h) => h.Index * DataLimits.MaxUnitAbilities;

    /// <summary>A point exactly 16 m away is in range (cast starts on the apply tick, no step); a hair past it walks.</summary>
    [Fact]
    public void RangeEdge_Exactly16m_CastsInPlace_JustPast_Walks()
    {
        Simulation sim = NoFights();
        UnitStore u = sim.World.Units;
        EntityHandle a = Place(sim, 0, Mage, At(sim, 4, 10));
        EntityHandle b = Place(sim, 0, Mage, At(sim, 4, 30));
        Vector2 pa = u.Position[a.Index] + new Vector2(16f, 0f);
        Vector2 pb = u.Position[b.Index] + new Vector2(16.05f, 0f);
        Vector2 a0 = u.Position[a.Index], b0 = u.Position[b.Index];
        sim.Enqueue(Command.UseAbility(0, a, 0, pa));
        sim.Enqueue(Command.UseAbility(0, b, 0, pb));
        sim.Tick();
        sim.Tick(); // a command applies in the tick after the one it was enqueued before
        Assert.Equal(UnitState.Casting, u.State[a.Index]);
        Assert.Equal(a0, u.Position[a.Index]);
        Assert.True(Had(sim, a, resolved: false), "the in-range cast started on its apply tick");
        Assert.Equal(UnitState.Moving, u.State[b.Index]);
        Assert.Equal(0, u.CastAbility[b.Index]);
        Assert.False(Had(sim, b, resolved: false));
        // b starts as soon as it has stepped inside 16 m.
        TickOf(sim, b, resolved: false, max: 40);
        float d = Vector2.Distance(u.Position[b.Index], pb);
        Assert.True(d <= 16f && Vector2.Distance(u.Position[b.Index], b0) < 1.5f, $"b cast from {d} m");
    }

    /// <summary>Cooldown boundary through real commands: a UseAbility applying on resolve + 499 is dropped, on resolve + 500 taken.</summary>
    [Fact]
    public void CooldownBoundary_CommandApplyingTheTickBefore_IsDropped_TheTickOf_IsTaken()
    {
        Simulation sim = NoFights();
        UnitStore u = sim.World.Units;
        EntityHandle mage = Place(sim, 0, Mage, At(sim, 10, 20));
        Vector2 point = At(sim, 14, 20);
        sim.Enqueue(Command.UseAbility(0, mage, 0, point));
        int resolve = TickOf(sim, mage, resolved: true);
        Assert.Equal(resolve + 500, u.AbilityReadyTick[Slot(mage)]);
        while (sim.TickNumber < resolve + 498) sim.Tick();
        sim.Enqueue(Command.UseAbility(0, mage, 0, point)); // applies in tick resolve + 499
        sim.Tick();
        sim.Enqueue(Command.UseAbility(0, mage, 0, point)); // applies in tick resolve + 500
        sim.Tick();
        Assert.Equal(resolve + 500, sim.TickNumber);
        Assert.Equal(-1, u.CastAbility[mage.Index]);
        Assert.Equal(UnitState.Idle, u.State[mage.Index]);
        sim.Tick();
        Assert.Equal(UnitState.Casting, u.State[mage.Index]);
        Assert.Equal(0, u.CastAbility[mage.Index]);
    }

    /// <summary>A queued cast popped while on cooldown is dropped and the queue goes on to its next order.</summary>
    [Fact]
    public void QueuedCast_PoppedOnCooldown_IsDropped_AndTheNextQueuedOrderRuns()
    {
        Simulation sim = NoFights();
        UnitStore u = sim.World.Units;
        EntityHandle mage = Place(sim, 0, Mage, At(sim, 10, 20));
        Vector2 point = At(sim, 14, 20);
        sim.Enqueue(Command.Move(0, mage, At(sim, 12, 20)));
        // Accepted at apply (off cooldown) ...
        sim.Enqueue(Command.UseAbility(0, mage, 0, point, queued: true));
        sim.Enqueue(Command.Move(0, mage, At(sim, 12, 30), queued: true));
        sim.Tick();
        sim.Tick();
        Assert.Equal(2, u.QueueCount[mage.Index]);
        // ... but put on cooldown before it pops (a test seam: the cooldown of a cast that resolved meanwhile).
        u.AbilityReadyTick[Slot(mage)] = sim.TickNumber + 10_000;
        RunUntil(sim, () => u.QueueCount[mage.Index] == 0 && u.State[mage.Index] == UnitState.Idle, 400);
        Assert.Equal(-1, u.CastAbility[mage.Index]);
        Assert.True(Vector2.Distance(u.Position[mage.Index], At(sim, 12, 30)) < 2f, "the Move after the dropped cast ran");
    }

    /// <summary>Stop, HoldPosition and a second UseAbility each cancel a cast in progress with no cooldown; the second cast's timer starts over.</summary>
    [Theory]
    [InlineData(0)] // Stop
    [InlineData(1)] // HoldPosition
    [InlineData(2)] // a second UseAbility
    public void MidCast_StopHoldOrRecast_CancelsWithNoCooldown(int how)
    {
        Simulation sim = NoFights();
        UnitStore u = sim.World.Units;
        EntityHandle mage = Place(sim, 0, Mage, At(sim, 10, 20));
        Vector2 point = At(sim, 14, 20);
        EntityHandle target = Place(sim, 1, Crossbowman, point);
        sim.Enqueue(Command.UseAbility(0, mage, 0, point));
        for (int t = 0; t < 8; t++) sim.Tick();
        Assert.Equal(UnitState.Casting, u.State[mage.Index]);
        sim.Enqueue(how switch
        {
            0 => Command.Stop(0, mage),
            1 => Command.HoldPosition(0, mage),
            _ => Command.UseAbility(0, mage, 0, point),
        });
        sim.Tick();
        sim.Tick();
        int restart = sim.TickNumber - 1;
        if (how < 2)
        {
            Assert.Equal(-1, u.CastAbility[mage.Index]);
            Assert.NotEqual(UnitState.Casting, u.State[mage.Index]);
            for (int t = 0; t < 30; t++) sim.Tick();
            Assert.Equal(0, u.AbilityReadyTick[Slot(mage)]);
            Assert.Equal(0, u.Statuses.Count[target.Index]);
            Assert.True(AbilitySystem.CanUse(sim.World, mage.Index, 0, point));
            return;
        }
        Assert.Equal(UnitState.Casting, u.State[mage.Index]);
        Assert.Equal(0, u.AbilityReadyTick[Slot(mage)]); // the first cast left no cooldown
        int resolve = TickOf(sim, mage, resolved: true, max: 40);
        Assert.Equal(16, resolve - restart + 1); // a full 0.8 s from the recast
        Assert.Equal((10f, 80), StatusOf(sim, target, Burning));
    }

    /// <summary>
    /// The pulse that kills: a Burning victim on 10 HP (with Slowed beside it) dies on the first pulse, the kill and the
    /// death event are the caster owner's, the store's counters stay true, and the next unit in the slot starts clean.
    /// </summary>
    [Fact]
    public void ADotKillOnThePulseTick_CreditsTheSource_ClearsTheSlot_AndCountersStayTrue()
    {
        Simulation sim = NoFights();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle victim = Place(sim, 1, Crossbowman, At(sim, 20, 20));
        EntityHandle other = Place(sim, 1, Crossbowman, At(sim, 30, 20));
        StatusSystem.Apply(w, victim.Index, Slowed, 0.3f, 200, 0);
        StatusSystem.Apply(w, victim.Index, Burning, 10f, 80, 0);
        StatusSystem.Apply(w, other.Index, Burning, 10f, 80, 0);
        u.Hp[victim.Index] = 10;
        Assert.Equal(2, u.Statuses.UnitsWithStatuses);
        int kills = w.Kills[0];
        for (int t = 0; t < 19; t++) sim.Tick();
        Assert.True(u.IsAlive(victim));
        sim.Tick(); // the pulse tick
        Assert.False(u.IsAlive(victim));
        Assert.Equal(kills + 1, w.Kills[0]);
        bool found = false;
        foreach (DeathEvent d in w.Deaths)
            if (d.Victim == victim) { found = true; Assert.Equal(0, d.KillerOwner); }
        Assert.True(found, "no death event for the DoT kill");
        Assert.Equal(0, u.Statuses.Count[victim.Index]);
        Assert.Equal(1, u.Statuses.UnitsWithStatuses);
        Assert.Equal(55 - 10, u.Hp[other.Index]); // the other victim's pulse landed in the same tick
        // The slot's next unit: no statuses, full speed, no cooldowns.
        EntityHandle next = Place(sim, 1, Crossbowman, At(sim, 20, 20));
        Assert.Equal(victim.Index, next.Index);
        Assert.Equal(0, u.Statuses.Count[next.Index]);
        Assert.Equal(TestSim.Data.Units[Crossbowman].SpeedPerTick, u.Speed[next.Index]);
        for (int t = 0; t < 100; t++) sim.Tick();
        Assert.Equal(55, u.Hp[next.Index]);
        Assert.Equal(0, u.Statuses.UnitsWithStatuses);
    }

    /// <summary>
    /// Two casters of two different players on one victim (3 players): the same magnitude takes the later caster as the
    /// source (it is credited with the kill); a weaker later application refreshes the duration but keeps the stronger's source.
    /// </summary>
    [Fact]
    public void TwoCastersOfDifferentPlayers_TheSourceFollowsTheRule_AndTheKillIsItsOwners()
    {
        var sim = TestSim.Explored(new Simulation(TestSim.ConfigNoCombat(Seed: 3, PlayerCount: 3, UnitCapacity: 32, CommandCapacity: 128), LocalMovementTests.Flat(48)));
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle m0 = Place(sim, 0, Mage, At(sim, 10, 20));
        EntityHandle m2 = Place(sim, 2, Mage, At(sim, 10, 26));
        Vector2 point = At(sim, 15, 22);
        EntityHandle victim = Place(sim, 1, Crossbowman, point);
        EntityHandle bystander = Place(sim, 2, Crossbowman, Off(point, 1f)); // player 0's fire burns player 2's units too
        sim.Enqueue(Command.UseAbility(0, m0, 0, point));
        TickOf(sim, m0, resolved: true);
        StatusStore s = u.Statuses;
        Assert.Equal(0, s.SourcePlayer[s.IndexOf(victim.Index, Burning)]);
        Assert.Equal((10f, 80), StatusOf(sim, bystander, Burning));
        for (int t = 0; t < 10; t++) sim.Tick();
        sim.Enqueue(Command.UseAbility(2, m2, 0, point));
        TickOf(sim, m2, resolved: true);
        Assert.Equal((10f, 80), StatusOf(sim, victim, Burning)); // refreshed
        Assert.Equal(2, s.SourcePlayer[s.IndexOf(victim.Index, Burning)]); // a tie: the latest
        Assert.Equal(0, s.SourcePlayer[s.IndexOf(bystander.Index, Burning)]); // player 2's own unit: untouched by its cast
        // A weaker application by player 0 refreshes, but the stronger (player 2's) keeps the source.
        StatusSystem.Apply(w, victim.Index, Burning, 5f, 80, 0);
        Assert.Equal(2, s.SourcePlayer[s.IndexOf(victim.Index, Burning)]);
        u.Hp[victim.Index] = 1;
        int k0 = w.Kills[0], k2 = w.Kills[2];
        RunUntil(sim, () => !u.IsAlive(victim), 40);
        Assert.False(u.IsAlive(victim));
        Assert.Equal((k0, k2 + 1), (w.Kills[0], w.Kills[2]));
    }

    /// <summary>
    /// BUG-0301: Burning refreshed more often than once a second (several casters, or a slice-2 zone that reapplies every
    /// tick) still burns at 10 a second (docs/02 "Burning: damage over time"). The pulse clock was the remaining count, so
    /// every refresh reset it and a refresh every 10 ticks never let it reach a multiple of 20; it is now its own counter.
    /// </summary>
    [Fact]
    public void BurningRefreshedEvery10Ticks_StillBurns10ASecond()
    {
        Simulation sim = NoFights();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle h = Place(sim, 1, HeavyInfantry, At(sim, 20, 20));
        u.Hp[h.Index] = 100_000;
        for (int t = 0; t < 200; t++)
        {
            if (t % 10 == 0) StatusSystem.Apply(w, h.Index, Burning, 10f, 80, 0);
            sim.Tick();
        }
        // 10 s under fire: about 10 pulses of 13 (Heavy) whatever the refresh cadence.
        Assert.True(100_000 - u.Hp[h.Index] >= 9 * 13, $"took {100_000 - u.Hp[h.Index]} in 10 s of constant Burning");
    }

    /// <summary>A Slowed unit walks slower, a weaker second slow leaves the speed, a stronger one lowers it, and expiry of the strongest restores the type's speed (not a stale one).</summary>
    [Fact]
    public void SlowStacking_StrongestWins_ExpiryRestoresTheTypesSpeed()
    {
        Simulation sim = NoFights();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle h = Place(sim, 1, Crossbowman, At(sim, 20, 20));
        float baseSpeed = TestSim.Data.Units[Crossbowman].SpeedPerTick;
        StatusSystem.Apply(w, h.Index, Slowed, 0.3f, 40, 0);
        Assert.Equal(baseSpeed * 0.7f, u.Speed[h.Index], 5);
        StatusSystem.Apply(w, h.Index, Slowed, 0.1f, 10, 1);
        Assert.Equal(baseSpeed * 0.7f, u.Speed[h.Index], 5);
        StatusSystem.Apply(w, h.Index, Slowed, 0.5f, 5, 1);
        Assert.Equal(baseSpeed * 0.5f, u.Speed[h.Index], 5);
        // One entry per status: the 0.5 holds for the longer remaining 40 ticks, then speed returns in full.
        for (int t = 0; t < 39; t++) sim.Tick();
        Assert.Equal(baseSpeed * 0.5f, u.Speed[h.Index], 5);
        sim.Tick();
        Assert.Equal(0, u.Statuses.Count[h.Index]);
        Assert.Equal(baseSpeed, u.Speed[h.Index]);
    }
}
