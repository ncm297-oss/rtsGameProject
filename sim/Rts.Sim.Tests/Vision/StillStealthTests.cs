using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Vision;
using static Rts.Sim.Tests.StillScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M8-2 criteria 3-9: a unit type's stand-still stealth (docs/02 "Stealth and detection", docs/03 "Implementation (M8-2)")
/// on the <see cref="StillScenes"/> copies (the field, 3 s, on the Malazan Heavy Infantry, Crossbowman and Laborer, the
/// Whirlwind Desert Archer and the Teblor Javelin Thrower). "After tick t" is after <c>sim.Tick()</c> ran tick t; a unit
/// placed before the first tick is first seen by tick 0's pass.
/// </summary>
public class StillStealthTests
{
    private static GameData D => Still;

    private const int Delay = 60; // the copies' 3 s

    private static int Bow => D.FindUnit("malazan_crossbowman");

    private static int Laborer => D.FindUnit("malazan_laborer");

    private static int Infantry => D.FindUnit("malazan_heavy_infantry");

    private static int Lancer => D.FindUnit("malazan_wickan_lancer"); // no field

    private static int Archer => D.FindUnit("whirlwind_desert_archer");

    private static int Raider => D.FindUnit("whirlwind_raider");

    private static int Follower => D.FindUnit("whirlwind_camp_follower");

    private static int Priest => D.FindUnit("whirlwind_priest");

    private static int Revealed => D.FindStatus("revealed");

    /// <summary>
    /// Ticks <paramref name="sim"/> <paramref name="ticks"/> times (or until <paramref name="until"/> holds after a tick) and
    /// after each asserts <paramref name="h"/> is Stealthed exactly when the tick's phase 5 saw it rest for the delay: the
    /// tick is at least <see cref="Delay"/> past the last tick its position changed in (<paramref name="lastChange"/>, kept
    /// up to date here: a position that differs after a tick changed in that tick). For a unit that neither attacks nor is hurt.
    /// </summary>
    private static void Follow(Simulation sim, EntityHandle h, ref int lastChange, int ticks, Func<bool>? until = null)
    {
        UnitStore u = sim.World.Units;
        Vector2 last = u.Position[h.Index];
        for (int k = 0; k < ticks; k++)
        {
            sim.Tick();
            int now = sim.TickNumber - 1;
            bool due = now - lastChange >= Delay;
            Assert.True(due == sim.World.IsStealthed(h.Index), $"tick {now}: last change {lastChange}, stealthed {sim.World.IsStealthed(h.Index)}");
            Assert.Equal(due, Held(sim, h));
            if (u.Position[h.Index] != last)
            {
                last = u.Position[h.Index];
                lastChange = now;
            }
            if (until != null && until()) return;
        }
    }

    // ---------- criterion 3: the pinned tick, hidden and untargetable ----------

    [Fact]
    public void C3_AUnitThatStops_IsStealthedOnExactlyThe60thTickAfterItsPositionLastChanged()
    {
        Simulation sim = Scene(D, combat: false);
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle h = CombatScenes.Place(sim, 0, Bow, At(sim, 10, 10));
        int lastChange = 0; // first seen by tick 0's pass
        Follow(sim, h, ref lastChange, 1);
        sim.Enqueue(Command.Move(0, h, At(sim, 22, 10)));
        int hiddenAt = -1;
        Follow(sim, h, ref lastChange, 600, () =>
        {
            if (hiddenAt < 0 && w.IsStealthed(h.Index)) hiddenAt = sim.TickNumber - 1;
            return hiddenAt >= 0;
        });
        Assert.Equal(lastChange + Delay, hiddenAt);
        Assert.Equal(lastChange, u.Still!.MovedTick[h.Index]);
        // The pinned ticks of this walk (12 cells at the Crossbowman's speed, then its arrival settling): last move in tick 145,
        // hidden in tick 205.
        Assert.Equal((145, 205), (lastChange, hiddenAt));
        Assert.Equal(0, OwnTicks(sim, h)); // the rule's held tick alone: nothing timed
    }

    /// <summary>Hidden: an enemy that sees its cell does not see it, its scans never take it, and an explicit Attack on it is dropped; revealed, the same enemy takes it.</summary>
    [Fact]
    public void C3_Hidden_FromAnEnemyThatSeesItsCell_AndCannotBeTargetedByIt()
    {
        Simulation sim = Scene(D);
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle h = CombatScenes.Place(sim, 0, Laborer, At(sim, 20, 20)); // a worker never fights, so it never reveals itself
        Ticks(sim, Delay + 1);
        Assert.True(w.IsStealthed(h.Index));
        EntityHandle enemy = CombatScenes.Place(sim, 1, Raider, At(sim, 20, 20, dx: 8f)); // in its 14 m sight, beyond 3 m
        Ticks(sim, 4); // a fog update
        Assert.True(w.Fog.IsVisible(1, w.Fog.CellOf(u.Position[h.Index])));
        Assert.False(w.IsDetectedBy(1, h.Index));
        Assert.False(w.Fog.CanSeeUnit(1, h.Index));
        Assert.False(VisionSystem.UnitSeesUnit(w, enemy.Index, h.Index));
        int full = u.Hp[h.Index];
        for (int t = 0; t < 60; t++)
        {
            sim.Tick();
            Assert.NotEqual(h, u.Target[enemy.Index]);
        }
        sim.Enqueue(Command.Attack(1, enemy, h, isBuilding: false));
        Ticks(sim, 2);
        Assert.NotEqual(h, u.Target[enemy.Index]);
        Assert.Equal(full, u.Hp[h.Index]);
        Assert.True(w.IsStealthed(h.Index));
        // The control: revealed, the same enemy may take it.
        StatusSystem.Apply(w, h.Index, Revealed, 0f, 400, 0);
        Assert.False(w.IsStealthed(h.Index));
        sim.Enqueue(Command.Attack(1, enemy, h, isBuilding: false));
        Ticks(sim, 2);
        Assert.Equal(h, u.Target[enemy.Index]);
    }

    // ---------- criterion 4: any position change ----------

    [Fact]
    public void C4_AnOrderedMove_EndsItInTheNextPhase5_AndItHidesAgainExactlyTheDelayAfterItComesToRest()
    {
        Simulation sim = Scene(D, combat: false);
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle h = CombatScenes.Place(sim, 0, Bow, At(sim, 10, 10));
        int lastChange = 0;
        Follow(sim, h, ref lastChange, Delay + 1);
        Assert.True(w.IsStealthed(h.Index));
        Vector2 rest = u.Position[h.Index];
        sim.Enqueue(Command.Move(0, h, At(sim, 16, 10)));
        int moved = -1;
        for (int t = 0; t < 20 && moved < 0; t++)
        {
            sim.Tick(); // the order applies in phase 1, the unit steps in phase 9 (once its field is ready): phase 5 saw it at rest
            Assert.True(w.IsStealthed(h.Index));
            if (u.Position[h.Index] != rest) moved = sim.TickNumber - 1;
        }
        Assert.True(moved >= 0, "never stepped");
        sim.Tick(); // the next phase 5 sees it moved
        Assert.False(w.IsStealthed(h.Index));
        Assert.False(Held(sim, h));
        Assert.False(u.Statuses.HasStealth(h.Index)); // no linger
        lastChange = moved;
        int hiddenAt = -1;
        Follow(sim, h, ref lastChange, 600, () =>
        {
            if (w.IsStealthed(h.Index)) hiddenAt = sim.TickNumber - 1;
            return hiddenAt >= 0;
        });
        Assert.Equal(lastChange + Delay, hiddenAt);
    }

    [Fact]
    public void C4_AShoveByAnotherUnit_EndsItInTheNextPhase5_AndItHidesAgainExactlyTheDelayAfterItComesToRest()
    {
        Simulation sim = Scene(D, combat: false);
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle h = CombatScenes.Place(sim, 0, Laborer, At(sim, 20, 20));
        // Six Wickan Lancers (no field) a few cells off, later ordered onto its very spot: the arriving group shoves it aside.
        var walkers = new EntityHandle[6];
        for (int k = 0; k < walkers.Length; k++) walkers[k] = CombatScenes.Place(sim, 0, Lancer, At(sim, 14, 17 + k));
        int lastChange = 0;
        Follow(sim, h, ref lastChange, Delay + 1);
        Assert.True(w.IsStealthed(h.Index));
        Vector2 rest = u.Position[h.Index];
        foreach (EntityHandle walker in walkers) sim.Enqueue(Command.Move(0, walker, rest));
        int shoved = -1;
        for (int t = 0; t < 300 && shoved < 0; t++)
        {
            sim.Tick();
            Assert.True(w.IsStealthed(h.Index), "let go before it was moved");
            if (u.Position[h.Index] != rest) shoved = sim.TickNumber - 1;
        }
        Assert.True(shoved >= 0, "never shoved");
        Assert.Equal(UnitState.Idle, u.State[h.Index]); // no order of its own: the move was a shove
        Assert.Equal(-1, u.GoalCell[h.Index]);
        sim.Tick();
        Assert.False(w.IsStealthed(h.Index)); // the next phase 5
        lastChange = shoved;
        if (u.Position[h.Index] != u.PrevPosition[h.Index]) lastChange = sim.TickNumber - 1;
        int hiddenAt = -1;
        Follow(sim, h, ref lastChange, 600, () =>
        {
            if (w.IsStealthed(h.Index)) hiddenAt = sim.TickNumber - 1;
            return hiddenAt >= 0;
        });
        Assert.Equal(lastChange + Delay, hiddenAt);
        Assert.True(lastChange > shoved || lastChange == shoved);
    }

    [Fact]
    public void C4_AChangeOfFacingAlone_DoesNotEndIt()
    {
        Simulation sim = Scene(D, combat: false);
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle h = CombatScenes.Place(sim, 0, Bow, At(sim, 10, 10));
        Ticks(sim, Delay + 1);
        Assert.True(w.IsStealthed(h.Index));
        Vector2 rest = u.Position[h.Index];
        for (int t = 0; t < 100; t++)
        {
            u.Facing[h.Index] += 0.3f; // turning on the spot
            sim.Tick();
            Assert.True(w.IsStealthed(h.Index));
            Assert.Equal(rest, u.Position[h.Index]);
        }
    }

    // ---------- criterion 5: attack, cast, hp loss ----------

    [Fact]
    public void C5_ItsAttackLanding_RevealsIt_AndRestartsTheCount()
    {
        Simulation sim = Scene(D);
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle h = CombatScenes.Place(sim, 0, Bow, At(sim, 20, 20));
        sim.Enqueue(Command.HoldPosition(0, h));
        Ticks(sim, Delay + 1);
        Assert.True(w.IsStealthed(h.Index));
        EntityHandle victim = CombatScenes.Place(sim, 1, Follower, At(sim, 20, 20, dx: 6f)); // in range, beyond 3 m; a worker never fights
        int full = u.Hp[victim.Index];
        CombatScenes.RunUntil(sim, () => u.Hp[victim.Index] < full, 200);
        Assert.True(u.Hp[victim.Index] < full, "never hit");
        int t = sim.TickNumber - 1;
        Assert.Equal(t, u.Still!.LastAttackTick[h.Index]);
        Assert.Equal((0f, D.Rules.RevealTicks), AbilityScenes.StatusOf(sim, h, Revealed)); // the M4-5 rule
        Assert.False(w.IsStealthed(h.Index));
        u.Free(victim);
        sim.Tick();
        Assert.False(Held(sim, h)); // the next pass let go
        while (sim.TickNumber < t + Delay)
        {
            sim.Tick();
            Assert.False(w.IsStealthed(h.Index), $"stealthed {sim.TickNumber - 1 - t} ticks after its hit");
        }
        sim.Tick(); // tick t + 60
        Assert.True(Held(sim, h));
        Assert.True(w.IsStealthed(h.Index));
    }

    /// <summary>The test-only ability (<c>zz_test_stealth</c>, 60 s on itself): its cast resolving reveals the caster and restarts the count; its own 1,200 ticks are untouched.</summary>
    [Fact]
    public void C5_ItsCastResolving_RevealsIt_AndRestartsTheCount()
    {
        Simulation sim = Scene(D, combat: false);
        World w = sim.World;
        EntityHandle h = CombatScenes.Place(sim, 0, Infantry, At(sim, 20, 20));
        sim.Enqueue(Command.HoldPosition(0, h));
        Ticks(sim, Delay + 1);
        Assert.True(Held(sim, h));
        sim.Enqueue(Command.UseAbility(0, h, 0, w.Units.Position[h.Index]));
        int t = AbilityScenes.TickOf(sim, h, resolved: true);
        Assert.Equal(t, w.Units.Still!.LastAttackTick[h.Index]);
        Assert.Equal(1200, OwnTicks(sim, h));
        Assert.Equal((0f, D.Rules.RevealTicks), AbilityScenes.StatusOf(sim, h, Revealed));
        sim.Tick();
        Assert.False(Held(sim, h));
        Assert.False(w.IsStealthed(h.Index));
        Assert.Equal(1199, OwnTicks(sim, h));
        while (sim.TickNumber < t + Delay)
        {
            sim.Tick();
            Assert.False(Held(sim, h));
        }
        sim.Tick();
        Assert.True(Held(sim, h));
        Assert.True(w.IsStealthed(h.Index));
        Assert.Equal(1200 - Delay, OwnTicks(sim, h));
    }

    [Fact]
    public void C5_LosingHitPoints_LetsGoAtOnce_AndRestartsTheCount()
    {
        Simulation sim = Scene(D);
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle h = CombatScenes.Place(sim, 0, Laborer, At(sim, 20, 20));
        sim.Enqueue(Command.HoldPosition(0, h)); // never shoved
        Ticks(sim, Delay + 1);
        Assert.True(w.IsStealthed(h.Index));
        EntityHandle raider = CombatScenes.Place(sim, 1, Raider, At(sim, 20, 20, dx: 1.3f)); // within 3 m: detects, then strikes
        int hp = u.Hp[h.Index], hurt = -1;
        for (int t = 0; t < 300 && hurt < 0; t++)
        {
            bool before = w.IsStealthed(h.Index);
            sim.Tick();
            if (u.Hp[h.Index] >= hp) continue;
            hurt = sim.TickNumber - 1;
            Assert.True(before, "not stealthed before its hp loss");
            Assert.False(w.IsStealthed(h.Index), "still stealthed the tick it lost hit points");
            Assert.False(Held(sim, h));
            Assert.Equal(hurt, u.Still!.LastHurtTick[h.Index]);
            Assert.True(w.Fog.CanSeeUnit(1, h.Index));
        }
        Assert.True(hurt >= 0, "never lost hit points");
        u.Free(raider);
        while (sim.TickNumber < hurt + Delay)
        {
            sim.Tick();
            Assert.False(w.IsStealthed(h.Index));
        }
        sim.Tick();
        Assert.True(w.IsStealthed(h.Index)); // tick hurt + 60
        Assert.Equal(-1, u.Still!.LastAttackTick[h.Index]);
    }

    [Fact]
    public void C5_AHitThatTakesNoHitPoints_DoesNeither_AndOneThatTakesOne_LetsGoAtOnce()
    {
        Simulation sim = Scene(D);
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle h = CombatScenes.Place(sim, 0, Laborer, At(sim, 20, 20));
        EntityHandle raider = CombatScenes.Place(sim, 1, Raider, At(sim, 40, 40));
        sim.Enqueue(Command.HoldPosition(0, h));
        Ticks(sim, Delay + 1);
        Assert.True(Held(sim, h));
        int hp = u.Hp[h.Index];
        CombatSystem.HitUnit(w, new PendingHit(raider, 1, h, false, 0));
        Assert.Equal(hp, u.Hp[h.Index]);
        Assert.True(Held(sim, h));
        Assert.True(w.IsStealthed(h.Index));
        Assert.Equal(-1, u.Still!.LastHurtTick[h.Index]);
        sim.Tick();
        Assert.True(w.IsStealthed(h.Index));
        CombatSystem.HitUnit(w, new PendingHit(raider, 1, h, false, 1));
        Assert.Equal(hp - 1, u.Hp[h.Index]);
        Assert.False(Held(sim, h));
        Assert.False(w.IsStealthed(h.Index));
        Assert.Equal(sim.TickNumber, u.Still.LastHurtTick[h.Index]);
    }

    /// <summary>The hp loss in phase 5 (a pulse) lets go of an entry stored before the pulsing one: the countdown still visits every other entry once.</summary>
    [Fact]
    public void C5_APulse_ThatEndsAHeldEntryStoredBeforeIt_LeavesTheOtherEntriesCountedOnce()
    {
        Simulation sim = Scene(D, combat: false);
        World w = sim.World;
        StatusStore s = w.Units.Statuses;
        EntityHandle h = CombatScenes.Place(sim, 0, Laborer, At(sim, 20, 20));
        Ticks(sim, Delay + 1); // held: entry 0
        Assert.True(Held(sim, h));
        int burning = D.FindStatus("burning"), slowed = D.FindStatus("slowed");
        StatusSystem.Apply(w, h.Index, burning, 2f, 100, 1); // entry 1, pulses after 20 ticks
        StatusSystem.Apply(w, h.Index, slowed, 0f, 100, 1); // entry 2 (magnitude 0: it does not move it)
        Assert.Equal(3, s.Count[h.Index]);
        for (int k = 1; k <= 20; k++)
        {
            sim.Tick();
            Assert.Equal(100 - k, AbilityScenes.StatusOf(sim, h, burning).Ticks);
            Assert.Equal(100 - k, AbilityScenes.StatusOf(sim, h, slowed).Ticks);
        }
        Assert.False(s.HasStealth(h.Index)); // the pulse in the 20th tick ended the held entry
        Assert.False(Held(sim, h));
        Assert.Equal(2, s.Count[h.Index]);
        Assert.Equal(sim.TickNumber - 1, w.Units.Still!.LastHurtTick[h.Index]);
    }

    // ---------- criterion 6: detection ----------

    [Theory]
    [InlineData(3f, true)]
    [InlineData(2.5f, true)]
    [InlineData(3.05f, false)]
    public void C6_Proximity_DetectsAStillHiddenUnit(float distance, bool detected)
    {
        Simulation sim = Scene(D, combat: false);
        World w = sim.World;
        EntityHandle h = CombatScenes.Place(sim, 0, Laborer, At(sim, 20, 20));
        Ticks(sim, Delay + 1);
        Assert.True(w.IsStealthed(h.Index));
        CombatScenes.Place(sim, 1, Follower, w.Units.Position[h.Index] + new Vector2(0f, distance));
        Ticks(sim, 4);
        Assert.True(w.IsStealthed(h.Index));
        Assert.Equal(detected, w.IsDetectedBy(1, h.Index));
        Assert.Equal(detected, w.Fog.CanSeeUnit(1, h.Index));
    }

    [Theory]
    [InlineData(9.9f, true)]
    [InlineData(10.1f, false)]
    public void C6_AUnitDetector_DetectsAStillHiddenUnitWithinItsRadius(float distance, bool detected)
    {
        Simulation sim = Scene(D, combat: false);
        World w = sim.World;
        EntityHandle h = CombatScenes.Place(sim, 0, Laborer, At(sim, 20, 20));
        Ticks(sim, Delay + 1);
        CombatScenes.Place(sim, 1, Priest, w.Units.Position[h.Index] + new Vector2(distance, 0f));
        Ticks(sim, 4);
        Assert.True(w.IsStealthed(h.Index));
        Assert.Equal(detected, w.IsDetectedBy(1, h.Index));
        Assert.Equal(detected, w.Fog.CanSeeUnit(1, h.Index));
    }

    // ---------- criterion 8: summoned, a full store ----------

    [Fact]
    public void C8_ASummonedUnitOfAFieldType_IsNeverHidden_AndKeepsNoState()
    {
        Simulation sim = Scene(D, combat: false);
        World w = sim.World;
        UnitStore u = w.Units;
        Assert.True(u.TrySpawn(0, Bow, D.Units[Bow], At(sim, 20, 20), 100_000, out EntityHandle summoned));
        EntityHandle control = CombatScenes.Place(sim, 0, Bow, At(sim, 24, 20));
        for (int t = 0; t < 200; t++)
        {
            sim.Tick();
            Assert.False(w.IsStealthed(summoned.Index));
            Assert.False(u.Statuses.HasStealth(summoned.Index));
            Assert.False(u.Still!.HasState(summoned.Index));
        }
        Assert.True(w.IsStealthed(control.Index));
    }

    [Fact]
    public void C8_AUnitWithAllEightStatusSlotsFull_IsNotHeld_AndIsTriedAgainOnTheNextPass()
    {
        Simulation sim = Scene(D, combat: false);
        World w = sim.World;
        UnitStore u = w.Units;
        StatusStore s = u.Statuses;
        EntityHandle h = CombatScenes.Place(sim, 0, Bow, At(sim, 20, 20));
        // Eight do-nothing marks (the copy's frenzied at magnitude 0), the last one running out in tick 100's phase 5.
        for (int k = 0; k < StatusStore.PerUnit; k++)
            StatusSystem.Apply(w, h.Index, Mark(D, k), 0f, k == StatusStore.PerUnit - 1 ? 101 : 1000, 1);
        Assert.Equal(StatusStore.PerUnit, s.Count[h.Index]);
        for (int t = 0; t <= 99; t++)
        {
            sim.Tick();
            Assert.False(Held(sim, h)); // due from tick 60, but no room: dropped, tried again every pass
            Assert.False(s.HasStealth(h.Index));
            Assert.False(w.IsStealthed(h.Index));
        }
        sim.Tick(); // tick 100: the countdown ends the last mark, then the pass finds room
        Assert.Equal(StatusStore.PerUnit, s.Count[h.Index]);
        Assert.True(Held(sim, h));
        Assert.True(w.IsStealthed(h.Index));
    }

    // ---------- criterion 9: generic, and no data ----------

    [Fact]
    public void C9_TheField_WorksOnAUnitOfAFactionWithoutForestStealth()
    {
        Simulation sim = Scene(D, combat: false);
        World w = sim.World;
        Assert.False(w.AnyForestStealth);
        Assert.False(D.Factions[D.FindFaction("whirlwind")].HasForestStealth);
        EntityHandle h = CombatScenes.Place(sim, 1, Archer, At(sim, 20, 20));
        int lastChange = 0;
        Follow(sim, h, ref lastChange, Delay + 5); // hidden from tick 60 exactly (Follow checks every tick)
        Assert.Equal(0, lastChange);
        Assert.True(w.IsStealthed(h.Index));
        Assert.True(Held(sim, h));
    }

    [Fact]
    public void C9_OnDataWithoutTheField_NoStoreExists_AndThePassReturnsAtOnce()
    {
        Simulation sim = CombatScenes.Flat();
        Assert.False(sim.World.StillStealthInData);
        Assert.Null(sim.World.Units.Still);
        EntityHandle h = CombatScenes.Place(sim, 0, CombatScenes.Crossbowman, CombatScenes.At(sim, 10, 10));
        for (int t = 0; t < 100; t++)
        {
            StillStealthSystem.Run(sim.World);
            sim.Tick();
        }
        Assert.False(sim.World.IsStealthed(h.Index));
        Assert.Null(sim.World.Units.Still);
        // A unit type without the field on data with it keeps no state either.
        Simulation with = Scene(D, combat: false);
        EntityHandle lancer = CombatScenes.Place(with, 0, Lancer, At(with, 10, 10));
        Ticks(with, 100);
        Assert.False(with.World.Units.Still!.HasState(lancer.Index));
        Assert.False(with.World.IsStealthed(lancer.Index));
    }
}
