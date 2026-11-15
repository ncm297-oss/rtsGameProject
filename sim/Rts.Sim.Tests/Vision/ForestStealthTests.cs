using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Vision;
using Rts.Sim.ViewApi;
using static Rts.Sim.Tests.ForestScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M8-1 criterion 3: a faction's forest stealth (docs/02 "Stealth and detection", docs/03 "Implementation (M8-1)") on the
/// <see cref="ForestScenes"/> copies: player 0 (Malazan) has <c>forestStealth</c> (<c>stealthed</c>, 3 s or 5 s, siege
/// excepted), player 1 (Whirlwind) not. "After tick t" is after <c>sim.Tick()</c> ran tick t; the rule reads positions as
/// phase 5 of that tick saw them (<see cref="UnitStore.PrevPosition"/> after it).
/// </summary>
public class ForestStealthTests
{
    private static GameData D => Data3;

    private static int Infantry => D.FindUnit("malazan_heavy_infantry");

    private static int Laborer => D.FindUnit("malazan_laborer");

    private static int Catapult => D.FindUnit("malazan_catapult");

    private static int Raider => D.FindUnit("whirlwind_raider");

    private static int Follower => D.FindUnit("whirlwind_camp_follower");

    private static int Priest => D.FindUnit("whirlwind_priest");

    private static int Revealed(GameData d) => d.FindStatus("revealed");

    // ---------- (a), (b): beside a tree, and no longer ----------

    [Fact]
    public void A_ANeverHitUnitSteppingBesideATree_IsStealthedThatTick_B_AndNotTheTickItIsNoLongerBesideOne()
    {
        Simulation sim = Scene(D, combat: false);
        World w = sim.World;
        Tree(sim, 20, 20);
        EntityHandle h = CombatScenes.Place(sim, 0, Infantry, At(sim, 26, 20));
        sim.Tick();
        Assert.False(w.IsStealthed(h.Index)); // three cells off: not beside
        sim.Enqueue(Command.Move(0, h, At(sim, 21, 20)));
        bool hidden = false;
        for (int t = 0; t < 200 && !hidden; t++)
        {
            sim.Tick();
            // Exactly the tick phase 5 saw it beside the tree, never before.
            Assert.Equal(WasBesideTree(sim, h), w.IsStealthed(h.Index));
            hidden = w.IsStealthed(h.Index);
        }
        Assert.True(hidden, "never stealthed beside the tree");
        Assert.True(Held(sim, h));
        Assert.Equal(1, w.Units.Statuses.StealthedUnits);
        Assert.Equal(0, OwnTicks(sim, h)); // the forest's held tick alone: nothing timed
        Ticks(sim, 30);
        Assert.True(w.IsStealthed(h.Index));

        // (b): walks out; stealthed exactly while phase 5 still sees it beside one, not the tick after.
        sim.Enqueue(Command.Move(0, h, At(sim, 30, 20)));
        for (int t = 0; t < 200 && hidden; t++)
        {
            sim.Tick();
            Assert.Equal(WasBesideTree(sim, h), w.IsStealthed(h.Index));
            hidden = w.IsStealthed(h.Index);
        }
        Assert.False(hidden, "still stealthed away from the tree");
        Assert.False(w.Units.Statuses.HasStealth(h.Index)); // the entry ended with the hold: no linger
        Assert.Equal(0, w.Units.Statuses.StealthedUnits);
    }

    [Fact]
    public void EachOfTheNineCells_Counts_TheTenthDoesNot()
    {
        for (int dx = -2; dx <= 2; dx++)
            for (int dy = -2; dy <= 2; dy++)
            {
                if (dx == 0 && dy == 0) continue; // the tree's own cell is blocked
                Simulation sim = Scene(D, combat: false);
                Tree(sim, 20, 20);
                EntityHandle h = CombatScenes.Place(sim, 0, Infantry, At(sim, 20 + dx, 20 + dy));
                sim.Enqueue(Command.HoldPosition(0, h));
                sim.Tick();
                bool beside = Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1;
                Assert.True(beside == sim.World.IsStealthed(h.Index), $"({dx}, {dy})");
            }
    }

    // ---------- (c): an attack from stealth ----------

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public void C_AnAttackLandingFromStealth_Reveals_AndItHidesAgainOnlyWhenRevealedHasEndedAndTheDelayHasPassed(int seconds)
    {
        GameData d = seconds == 3 ? Data3 : Data5;
        int delay = seconds * SimConstants.TicksPerSecond;
        Simulation sim = Scene(d);
        World w = sim.World;
        UnitStore u = w.Units;
        Tree(sim, 20, 20);
        EntityHandle h = CombatScenes.Place(sim, 0, Infantry, At(sim, 21, 20));
        EntityHandle victim = CombatScenes.Place(sim, 1, Follower, At(sim, 21, 20, dx: 1.2f)); // in reach; a worker never fights
        sim.Enqueue(Command.HoldPosition(0, h));
        sim.Tick();
        Assert.True(w.IsStealthed(h.Index));
        int full = u.Hp[victim.Index];
        CombatScenes.RunUntil(sim, () => u.Hp[victim.Index] < full, 100);
        Assert.True(u.Hp[victim.Index] < full, "never hit");
        int t = sim.TickNumber - 1;
        Assert.Equal(t, u.Forest!.LastAttackTick[h.Index]);
        Assert.Equal((0f, d.Rules.RevealTicks), AbilityScenes.StatusOf(sim, h, Revealed(d)));
        Assert.False(w.IsStealthed(h.Index));
        Assert.True(w.Fog.CanSeeUnit(1, h.Index));
        u.Free(victim); // nothing left in reach: no second hit
        while (sim.TickNumber < t + delay)
        {
            sim.Tick();
            Assert.False(w.IsStealthed(h.Index), $"stealthed again {sim.TickNumber - 1 - t} ticks after its hit (delay {delay})");
            if (sim.TickNumber - 1 >= t + d.Rules.RevealTicks) Assert.Equal((0f, 0), AbilityScenes.StatusOf(sim, h, Revealed(d))); // Revealed over, still waiting
        }
        sim.Tick(); // tick t + delay: Revealed over and the delay passed
        Assert.True(w.IsStealthed(h.Index));
        Assert.True(Held(sim, h));
    }

    // ---------- (d): attacked in the open, then walks to a tree ----------

    [Fact]
    public void D_AUnitThatAttackedInTheOpen_AndWalksToATreeInsideTheDelay_WaitsItOut()
    {
        Simulation sim = Scene(D);
        World w = sim.World;
        UnitStore u = w.Units;
        Tree(sim, 20, 20);
        EntityHandle h = CombatScenes.Place(sim, 0, Infantry, At(sim, 23, 20)); // two cells off the tree: not beside
        EntityHandle victim = CombatScenes.Place(sim, 1, Follower, At(sim, 23, 20, dx: 1.2f));
        sim.Enqueue(Command.HoldPosition(0, h));
        int full = u.Hp[victim.Index];
        CombatScenes.RunUntil(sim, () => u.Hp[victim.Index] < full, 100);
        int t = sim.TickNumber - 1;
        Assert.Equal(t, u.Forest!.LastAttackTick[h.Index]);
        Assert.False(w.Units.Statuses.HasStealth(h.Index)); // it never had a stealth: no Revealed either
        u.Free(victim);
        sim.Enqueue(Command.Move(0, h, At(sim, 21, 20)));
        int besideAt = -1;
        while (sim.TickNumber < t + 60)
        {
            sim.Tick();
            if (besideAt < 0 && WasBesideTree(sim, h)) besideAt = sim.TickNumber - 1;
            Assert.False(w.IsStealthed(h.Index));
        }
        Assert.True(besideAt >= 0 && besideAt < t + 50, $"precondition: beside the tree well inside the delay (at {besideAt - t})");
        sim.Tick();
        Assert.True(w.IsStealthed(h.Index)); // tick t + 60
    }

    // ---------- (e): losing hit points ----------

    /// <summary>
    /// Runs until <paramref name="h"/> (stealthed, beside a tree) loses hit points: it was stealthed right before that tick
    /// and is not right after it; then <paramref name="afterFirst"/> takes the source away, and every later tick it is
    /// stealthed exactly when both waits are over (<c>delay</c> since its last hp loss and since its last landed attack).
    /// </summary>
    private static void EndsOnHpLoss_ReturnsAfterTheDelay(Simulation sim, EntityHandle h, Action afterFirst, int maxTicks = 400)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        int delay = w.Data.Factions[w.Data.FindFaction("malazan")].ForestStealthDelayTicks;
        int hp = u.Hp[h.Index];
        bool hit = false;
        for (int t = 0; t < maxTicks && !hit; t++)
        {
            bool before = w.IsStealthed(h.Index);
            sim.Tick();
            if (u.Hp[h.Index] >= hp) continue;
            hit = true;
            Assert.True(before, "not stealthed before its hp loss");
            Assert.False(w.IsStealthed(h.Index), "still stealthed the tick it lost hit points");
            Assert.False(Held(sim, h));
            Assert.Equal(sim.TickNumber - 1, u.LastDamagedTick[h.Index]);
        }
        Assert.True(hit, "never lost hit points");
        afterFirst();
        bool back = false;
        for (int t = 0; t < maxTicks && !back; t++)
        {
            sim.Tick();
            int now = sim.TickNumber - 1;
            int attacked = u.Forest!.LastAttackTick[h.Index];
            bool due = now - u.LastDamagedTick[h.Index] >= delay && (attacked < 0 || now - attacked >= delay);
            Assert.True(due == w.IsStealthed(h.Index), $"tick {now}: last hp loss {u.LastDamagedTick[h.Index]}, stealthed {w.IsStealthed(h.Index)}");
            back = due;
        }
        Assert.True(back, "never stealthed again");
    }

    [Fact]
    public void E_AMeleeHit_EndsItThatTick_AndItReturnsAfterTheDelay()
    {
        Simulation sim = Scene(D);
        Tree(sim, 20, 20);
        EntityHandle h = CombatScenes.Place(sim, 0, Laborer, At(sim, 21, 20)); // a worker never fights back
        sim.Enqueue(Command.HoldPosition(0, h));
        sim.Tick();
        Assert.True(sim.World.IsStealthed(h.Index));
        EntityHandle raider = CombatScenes.Place(sim, 1, Raider, At(sim, 21, 20, dx: 1.3f)); // within 3 m: detects, then strikes
        EndsOnHpLoss_ReturnsAfterTheDelay(sim, h, () => sim.World.Units.Free(raider));
    }

    [Fact]
    public void E_AProjectile_EndsItThatTick_AndItReturnsAfterTheDelay()
    {
        Simulation sim = Scene(D);
        Tree(sim, 20, 20);
        EntityHandle h = CombatScenes.Place(sim, 0, Laborer, At(sim, 21, 20));
        sim.Enqueue(Command.HoldPosition(0, h));
        sim.Tick();
        EntityHandle priest = CombatScenes.Place(sim, 1, Priest, At(sim, 21, 20, dx: 8f)); // its 10 m detector, its bolt
        sim.Enqueue(Command.HoldPosition(1, priest));
        bool flying = false;
        EndsOnHpLoss_ReturnsAfterTheDelay(sim, h, () =>
        {
            flying = sim.World.Impacts.Length > 0;
            sim.World.Units.Free(priest);
        });
        Assert.True(flying, "the loss was not a projectile's landing");
    }

    [Fact]
    public void E_ASplash_EndsItThatTick_AndItReturnsAfterTheDelay()
    {
        Simulation sim = Scene(D);
        World w = sim.World;
        Tree(sim, 20, 20);
        EntityHandle h = CombatScenes.Place(sim, 0, Laborer, At(sim, 21, 20));
        // The Priest's target: an own Laborer 1.2 m east, in the next cell but one from the tree (not beside it: seen).
        EntityHandle target = CombatScenes.Place(sim, 0, Laborer, At(sim, 21, 20, dx: 1.2f));
        EntityHandle priest = CombatScenes.Place(sim, 1, Priest, At(sim, 21, 20, dx: 1.2f + 11f)); // 12.2 m from h: no detection
        foreach (EntityHandle e in new[] { h, target }) sim.Enqueue(Command.HoldPosition(0, e));
        sim.Enqueue(Command.HoldPosition(1, priest));
        sim.Tick();
        Assert.True(w.IsStealthed(h.Index));
        Assert.False(w.IsStealthed(target.Index));
        int targetHp = w.Units.Hp[target.Index];
        EndsOnHpLoss_ReturnsAfterTheDelay(sim, h, () =>
        {
            Assert.True(w.Units.Hp[target.Index] < targetHp, "h was hit, but not by the splash of the bolt on its neighbour");
            Assert.False(w.IsDetectedBy(1, h.Index) && w.Units.Statuses.HasStealth(h.Index));
            w.Units.Free(priest);
        });
    }

    [Fact]
    public void E_AZonesDamageTick_EndsItThatTick_AndItReturnsAfterTheDelay()
    {
        Simulation sim = Scene(D);
        World w = sim.World;
        Tree(sim, 20, 20);
        EntityHandle h = CombatScenes.Place(sim, 0, Laborer, At(sim, 21, 20));
        sim.Enqueue(Command.HoldPosition(0, h));
        sim.Tick();
        Assert.True(w.IsStealthed(h.Index));
        ZoneSystem.Create(w, 1, D.Abilities[D.FindAbility("zz_test_fire")], w.Units.Position[h.Index]);
        Assert.True(w.Units.Statuses.IndexOf(h.Index, D.FindStatus("burning")) >= 0); // a hit, not an acquisition: it reaches the hidden unit
        Assert.True(w.IsStealthed(h.Index)); // a status alone is no hp loss
        EndsOnHpLoss_ReturnsAfterTheDelay(sim, h, () => { });
    }

    /// <summary>The hp loss in phase 5 (a pulse) lets go of an entry stored before the pulsing one: the countdown still visits every other entry once.</summary>
    [Fact]
    public void APulse_ThatEndsAHeldEntryStoredBeforeIt_LeavesTheOtherEntriesCountedOnce()
    {
        Simulation sim = Scene(D, combat: false);
        World w = sim.World;
        StatusStore s = w.Units.Statuses;
        Tree(sim, 20, 20);
        EntityHandle h = CombatScenes.Place(sim, 0, Laborer, At(sim, 21, 20));
        sim.Tick(); // held: entry 0
        StatusSystem.Apply(w, h.Index, D.FindStatus("burning"), 2f, 100, 1); // entry 1, pulses after 20 ticks
        StatusSystem.Apply(w, h.Index, D.FindStatus("slowed"), 0.2f, 100, 1); // entry 2
        Assert.Equal(3, s.Count[h.Index]);
        int burning = D.FindStatus("burning"), slowed = D.FindStatus("slowed");
        for (int k = 1; k <= 20; k++)
        {
            sim.Tick();
            Assert.Equal(100 - k, AbilityScenes.StatusOf(sim, h, burning).Ticks);
            Assert.Equal(100 - k, AbilityScenes.StatusOf(sim, h, slowed).Ticks);
        }
        Assert.False(w.Units.Statuses.HasStealth(h.Index)); // the pulse in tick 20 ended the held entry
        Assert.Equal(2, s.Count[h.Index]);
    }

    // ---------- (f): never ----------

    [Fact]
    public void F_AnExceptedSlot_ASummonedUnit_AndAnotherFactionsUnit_BesideTheSameTree_AreNeverStealthed()
    {
        Simulation sim = Scene(D, combat: false);
        World w = sim.World;
        UnitStore u = w.Units;
        Tree(sim, 20, 20);
        EntityHandle catapult = CombatScenes.Place(sim, 0, Catapult, At(sim, 21, 20));
        Assert.True(u.TrySpawn(0, Laborer, D.Units[Laborer], At(sim, 19, 20), 100_000, out EntityHandle summoned));
        EntityHandle raider = CombatScenes.Place(sim, 1, Raider, At(sim, 20, 21));
        EntityHandle control = CombatScenes.Place(sim, 0, Infantry, At(sim, 20, 19));
        Assert.Equal(UnitSlot.Siege, D.Units[Catapult].Slot);
        for (int t = 0; t < 200; t++)
        {
            sim.Tick();
            foreach (EntityHandle e in new[] { catapult, summoned, raider })
            {
                Assert.False(w.IsStealthed(e.Index));
                Assert.False(u.Statuses.HasStealth(e.Index));
                Assert.Equal(0, u.Forest!.Held[e.Index]);
            }
            Assert.True(w.IsStealthed(control.Index));
        }
        Assert.Equal(-1, u.Forest!.LastAttackTick[raider.Index]);
    }

    // ---------- (g): the tree felled ----------

    [Fact]
    public void G_TheTreeFelled_EndsItThatTick()
    {
        Simulation sim = Scene(D, combat: false);
        World w = sim.World;
        EntityHandle tree = Tree(sim, 20, 20, wood: 1);
        EntityHandle h = CombatScenes.Place(sim, 0, Infantry, At(sim, 21, 20));
        sim.Enqueue(Command.HoldPosition(0, h));
        int hall = StartBase.BuildingOfSlot(D, D.FindFaction("whirlwind"), BuildingSlot.TownHall);
        TowerTests.PlaceBuilding(sim, 1, hall, 20, 26);
        EntityHandle gatherer = CombatScenes.Place(sim, 1, Follower, At(sim, 19, 22));
        sim.Tick();
        Assert.True(w.IsStealthed(h.Index));
        sim.Enqueue(Command.Gather(1, gatherer, At(sim, 20, 20)));
        bool felled = false;
        for (int t = 0; t < 400 && !felled; t++)
        {
            sim.Tick();
            felled = !w.Resources.IsAlive(tree);
            Assert.Equal(!felled, w.IsStealthed(h.Index)); // the felling tick's phase 4 then its phase 5: gone that tick
        }
        Assert.True(felled, "the tree was never felled");
        Assert.False(w.Units.Statuses.HasStealth(h.Index));
    }

    // ---------- (h): an ability's timed Stealthed ----------

    /// <summary>
    /// A timed Stealthed (the test seam, as an ability's effect applies it) on a unit the forest holds, re-applied while held,
    /// then the unit walks out: its entry ends in the same tick as the same script's on a map without the tree, its own
    /// ticks equal tick for tick. Forest or not, the timed part is never shortened or lengthened.
    /// </summary>
    [Fact]
    public void H_AnAbilitysTimedStealthed_KeepsItsOwnDuration_InAndOutOfTheForest()
    {
        Simulation forest = Scene(D, combat: false), open = Scene(D, combat: false);
        Tree(forest, 20, 20);
        int stealthed = Stealthed(D);
        var hs = new EntityHandle[2];
        Simulation[] sims = { forest, open };
        for (int k = 0; k < 2; k++)
        {
            hs[k] = CombatScenes.Place(sims[k], 0, Infantry, At(sims[k], 21, 20));
            StatusSystem.Apply(sims[k].World, hs[k].Index, stealthed, 0f, 300, 0); // before any hold
        }
        int endForest = -1, endOpen = -1;
        for (int t = 1; t <= 400; t++)
        {
            for (int k = 0; k < 2; k++)
            {
                if (t == 100) StatusSystem.Apply(sims[k].World, hs[k].Index, stealthed, 0f, 260, 0); // re-applied while held
                if (t == 150) sims[k].Enqueue(Command.Move(0, hs[k], At(sims[k], 30, 20))); // walks out
                sims[k].Tick();
            }
            if (t == 20) Assert.True(Held(forest, hs[0]));
            if (t < 150) Assert.True(forest.World.IsStealthed(hs[0].Index) && open.World.IsStealthed(hs[1].Index));
            Assert.Equal(OwnTicks(open, hs[1]), OwnTicks(forest, hs[0]));
            if (endForest < 0 && !forest.World.Units.Statuses.HasStealth(hs[0].Index)) endForest = t;
            if (endOpen < 0 && !open.World.Units.Statuses.HasStealth(hs[1].Index)) endOpen = t;
        }
        Assert.True(endOpen > 300, $"precondition: the re-application ran past the walk-out ({endOpen})");
        Assert.Equal(endOpen, endForest);
        Assert.False(Held(forest, hs[0]));
    }

    /// <summary>The ability's own entry running out while the unit stands in the forest leaves it stealthed by the forest.</summary>
    [Fact]
    public void H_AnAbilitysStealthed_RunningOutInTheForest_LeavesTheForestsHold()
    {
        Simulation sim = Scene(D, combat: false);
        Tree(sim, 20, 20);
        EntityHandle h = CombatScenes.Place(sim, 0, Infantry, At(sim, 21, 20));
        StatusSystem.Apply(sim.World, h.Index, Stealthed(D), 0f, 40, 0);
        for (int t = 1; t <= 100; t++)
        {
            sim.Tick();
            Assert.True(sim.World.IsStealthed(h.Index));
            Assert.Equal(Math.Max(0, 40 - t), OwnTicks(sim, h));
        }
        Assert.Equal(1, sim.World.Units.Statuses.TicksRemaining[sim.World.Units.Statuses.IndexOf(h.Index, Stealthed(D))]);
    }

    /// <summary>
    /// The test-only ability (<c>zz_test_stealth</c>, 60 s) cast in the forest: the cast resolving from stealth reveals the
    /// caster (M4-5) and restarts the forest's wait; its own 1,200 ticks run as in the open, and the forest holds it again once
    /// Revealed has ended and the delay has passed.
    /// </summary>
    [Fact]
    public void H_TheTestStealthAbility_CastInTheForest_RunsItsOwn60Seconds()
    {
        Simulation sim = Scene(D, combat: false);
        World w = sim.World;
        Tree(sim, 20, 20);
        EntityHandle h = CombatScenes.Place(sim, 0, Infantry, At(sim, 21, 20));
        sim.Enqueue(Command.HoldPosition(0, h));
        sim.Tick();
        Assert.True(Held(sim, h));
        sim.Enqueue(Command.UseAbility(0, h, 0, w.Units.Position[h.Index]));
        int t = AbilityScenes.TickOf(sim, h, resolved: true);
        Assert.Equal(t, w.Units.Forest!.LastAttackTick[h.Index]);
        Assert.Equal(1200, OwnTicks(sim, h)); // in the open it is 1,200 after the resolve tick too
        Assert.Equal((0f, D.Rules.RevealTicks), AbilityScenes.StatusOf(sim, h, Revealed(D)));
        sim.Tick(); // the cast restarted the wait: the forest lets go; the ability's entry stays, dormant under Revealed
        Assert.False(Held(sim, h));
        Assert.False(w.IsStealthed(h.Index));
        Assert.True(w.Units.Statuses.HasStealth(h.Index));
        Assert.Equal(1199, OwnTicks(sim, h));
        while (sim.TickNumber < t + 60)
        {
            sim.Tick();
            Assert.False(Held(sim, h));
        }
        sim.Tick(); // tick t + 60: Revealed over, the wait over
        Assert.True(Held(sim, h));
        Assert.True(w.IsStealthed(h.Index));
        Assert.Equal(1200 - 60, OwnTicks(sim, h));
    }

    // ---------- (i): detection and the one predicate ----------

    [Theory]
    [InlineData(3f, true)]
    [InlineData(2.5f, true)]
    [InlineData(3.05f, false)]
    public void I_Proximity_DetectsAsInM45(float distance, bool detected)
    {
        Simulation sim = Scene(D, combat: false);
        World w = sim.World;
        Tree(sim, 20, 20);
        EntityHandle h = CombatScenes.Place(sim, 0, Laborer, At(sim, 21, 20));
        EntityHandle near = CombatScenes.Place(sim, 1, Follower, w.Units.Position[h.Index] + new Vector2(0f, distance));
        Ticks(sim, 2);
        Assert.True(w.IsStealthed(h.Index));
        Assert.Equal(detected, w.IsDetectedBy(1, h.Index));
        Assert.Equal(detected, w.Fog.CanSeeUnit(1, h.Index));
        Assert.True(w.Units.IsAlive(near));
    }

    [Theory]
    [InlineData(9.9f, true)]
    [InlineData(10.1f, false)]
    public void I_AUnitDetector_DetectsWithinItsRadius(float distance, bool detected)
    {
        Simulation sim = Scene(D, combat: false);
        World w = sim.World;
        Tree(sim, 20, 20);
        EntityHandle h = CombatScenes.Place(sim, 0, Laborer, At(sim, 21, 20));
        CombatScenes.Place(sim, 1, Priest, w.Units.Position[h.Index] + new Vector2(distance, 0f));
        Ticks(sim, 2);
        Assert.True(w.IsStealthed(h.Index));
        Assert.Equal(detected, w.IsDetectedBy(1, h.Index));
        Assert.Equal(detected, w.Fog.CanSeeUnit(1, h.Index));
    }

    [Theory]
    [InlineData(15.9f, true)]
    [InlineData(16.1f, false)]
    public void I_ATower_DetectsWithinItsRadius(float distance, bool detected)
    {
        Simulation sim = Scene(D, combat: false, size: 64);
        World w = sim.World;
        int tower = D.FindBuilding("whirlwind_lookout_tower");
        Assert.Equal(16f, D.Buildings[tower].Detector);
        EntityHandle b = TowerTests.PlaceBuilding(sim, 1, tower, 30, 30);
        Vector2 centre = Combat.CombatSystem.BuildingCentre(w, b.Index);
        Vector2 at = centre - new Vector2(distance, 0f);
        Assert.True(w.NavGrid.WorldToCell(at, out int cx, out int cy));
        Tree(sim, cx, cy - 1);
        EntityHandle h = CombatScenes.Place(sim, 0, Laborer, at);
        Ticks(sim, 2);
        Assert.True(w.IsStealthed(h.Index));
        Assert.Equal(detected, w.IsDetectedBy(1, h.Index));
        Assert.Equal(detected, VisionSystem.BuildingSeesUnit(w, b.Index, h.Index));
    }

    /// <summary>Undetected: an enemy in sight never acquires it, an explicit Attack on it is dropped, and the enemy AI's view does not hold it; revealed, all three do.</summary>
    [Fact]
    public void I_Undetected_NoScanAcquiresIt_AnAttackIsDropped_AndTheEnemyAisViewDoesNotHoldIt()
    {
        var seats = System.Collections.Immutable.ImmutableArray.Create(
            new PlayerSeat(D.FindFaction("malazan"), SeatController.Human, AiDifficulty.Normal),
            new PlayerSeat(D.FindFaction("whirlwind"), SeatController.Ai, AiDifficulty.Normal));
        var sim = new Simulation(new SimConfig(1, 2, 64, 544) { Data = D, Seats = seats }, LocalMovementTests.Flat(48));
        World w = sim.World;
        UnitStore u = w.Units;
        Tree(sim, 20, 20);
        EntityHandle h = CombatScenes.Place(sim, 0, Laborer, At(sim, 21, 20));
        EntityHandle enemy = CombatScenes.Place(sim, 1, Raider, At(sim, 21, 20, dx: 8f)); // in its 14 m sight, beyond 3 m
        Ticks(sim, 2);
        AiPlayer ai = sim.AiOf(1)!;
        Assert.True(w.IsStealthed(h.Index));
        Assert.True(w.Fog.IsVisible(1, w.Fog.CellOf(u.Position[h.Index])));
        Assert.False(w.Fog.CanSeeUnit(1, h.Index));
        Assert.False(ai.View.TryGetUnit(h.Index, out _));
        Assert.False(VisionSystem.UnitSeesUnit(w, enemy.Index, h.Index));
        int full = u.Hp[h.Index];
        for (int t = 0; t < 60; t++)
        {
            sim.Tick();
            Assert.NotEqual(h, u.Target[enemy.Index]);
            Assert.False(ai.View.TryGetUnit(h.Index, out _));
        }
        sim.Enqueue(Command.Attack(1, enemy, h, isBuilding: false));
        Ticks(sim, 2);
        Assert.NotEqual(h, u.Target[enemy.Index]);
        Assert.Equal(full, u.Hp[h.Index]);
        // The control: revealed (the one predicate's other half), the same view holds it and the same enemy may take it.
        StatusSystem.Apply(w, h.Index, Revealed(D), 0f, 400, 0);
        Assert.False(w.IsStealthed(h.Index));
        Assert.True(ai.View.TryGetUnit(h.Index, out _));
        sim.Enqueue(Command.Attack(1, enemy, h, isBuilding: false));
        Ticks(sim, 2);
        Assert.Equal(h, u.Target[enemy.Index]);
    }

    // ---------- the shipped data ----------

    [Fact]
    public void OnDataWithoutTheBlock_NoPerUnitStateExists_AndThePassReturnsAtOnce()
    {
        Simulation sim = CombatScenes.Flat();
        Assert.False(sim.World.AnyForestStealth);
        Assert.False(sim.World.ForestStealthInData);
        Assert.Null(sim.World.Units.Forest);
        
        Assert.False(sim.World.Resources.IsWoodCell(0));
    }

    [Fact]
    public void ASeatWithoutTheBonus_OnDataWithIt_KeepsTheStateButThePassDoesNothing()
    {
        var seats = System.Collections.Immutable.ImmutableArray.Create(
            new PlayerSeat(D.FindFaction("whirlwind"), SeatController.Human, AiDifficulty.Normal),
            new PlayerSeat(D.FindFaction("teblor"), SeatController.Human, AiDifficulty.Normal));
        var sim = new Simulation(new SimConfig(1, 2, 64, 544) { Data = D, Seats = seats }, LocalMovementTests.Flat(48));
        Assert.False(sim.World.AnyForestStealth);
        Assert.True(sim.World.ForestStealthInData);
        Assert.NotNull(sim.World.Units.Forest);
    }
}
