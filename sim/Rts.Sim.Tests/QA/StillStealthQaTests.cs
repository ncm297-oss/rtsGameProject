using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Abilities;
using Rts.Sim.Ai;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Save;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;
using static Rts.Sim.Tests.StillScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// QA (M8-2, session 2026-11-17-0816): attacks on the stand-still stealth beyond the developer's scene rows. The delay
/// boundary over several delays; a freed slot reused by a new unit; two stealth rules on two different statuses; a
/// model-based fuzz of the shared held entry (forest + stand-still + timed applications + reveals + hp losses in random
/// order, against a twin and an independent model, with a save / load in the middle); a save with the field in the data but
/// no such unit alive; the AI's PlayerView and the view's FogView never showing a still-hidden enemy; a packed crowd in which
/// every unit's hold is exactly its own last position change plus the delay; a full status store with both rules.
/// </summary>
public class StillStealthQaTests
{
    private readonly ITestOutputHelper _out;

    public StillStealthQaTests(ITestOutputHelper output) => _out = output;

    private static GameData D => Still;

    // ---------- the delay boundary ----------

    private static readonly Dictionary<double, GameData> s_byDelay = new();

    private static GameData DataAt(double seconds)
    {
        lock (s_byDelay)
        {
            if (!s_byDelay.TryGetValue(seconds, out GameData? d)) s_byDelay[seconds] = d = DataWith(seconds, forest: false);
            return d;
        }
    }

    /// <summary>
    /// A unit placed before tick 0 is first seen by tick 0's pass and hidden on exactly tick <c>delay</c>, not one before. For
    /// every delay the loader allows from one tick up (0.05 s) to the bound (60 s).
    /// </summary>
    [Theory]
    [InlineData(0.05, 1)]
    [InlineData(0.1, 2)]
    [InlineData(0.15, 3)]
    [InlineData(0.5, 10)]
    [InlineData(3.0, 60)]
    [InlineData(60.0, 1200)]
    public void FirstSeen_IsHiddenOnExactlyTheDelayTick(double seconds, int delay)
    {
        GameData d = DataAt(seconds);
        Simulation sim = Scene(d, combat: false);
        EntityHandle h = CombatScenes.Place(sim, 0, d.FindUnit("malazan_laborer"), At(sim, 20, 20));
        Assert.Equal(delay, d.Units[d.FindUnit("malazan_laborer")].StillStealthDelayTicks);
        for (int t = 0; t <= delay + 2; t++)
        {
            sim.Tick();
            Assert.True((t >= delay) == sim.World.IsStealthed(h.Index), $"delay {delay}: tick {t} stealthed {sim.World.IsStealthed(h.Index)}");
        }
    }

    /// <summary>
    /// A position change seen by tick k's pass (a nudge before tick k: as if movement moved it in tick k - 1) is dated k - 1,
    /// so the unit is visible from tick k and hidden again on exactly k - 1 + delay, for every delay of two ticks or more
    /// (the one-tick delay behaves as two: docs/03 M8-2 Known limits, Question 2).
    /// </summary>
    [Theory]
    [InlineData(0.1, 2)]
    [InlineData(0.15, 3)]
    [InlineData(0.5, 10)]
    [InlineData(3.0, 60)]
    public void AfterAMove_IsVisibleAtOnce_AndHiddenAgainOnExactlyTheDelayTick(double seconds, int delay)
    {
        GameData d = DataAt(seconds);
        Simulation sim = Scene(d, combat: false);
        UnitStore u = sim.World.Units;
        EntityHandle h = CombatScenes.Place(sim, 0, d.FindUnit("malazan_laborer"), At(sim, 20, 20));
        Ticks(sim, delay + 5);
        Assert.True(sim.World.IsStealthed(h.Index));
        for (int round = 0; round < 3; round++)
        {
            int k = sim.TickNumber;
            u.Position[h.Index] += new Vector2(round % 2 == 0 ? 0.001f : -0.001f, 0f);
            for (int t = k; t <= k - 1 + delay + 2; t++)
            {
                sim.Tick();
                bool expected = t >= k - 1 + delay;
                Assert.True(expected == sim.World.IsStealthed(h.Index), $"delay {delay} round {round}: nudged before {k}, tick {t} stealthed {sim.World.IsStealthed(h.Index)}");
            }
        }
    }

    // ---------- a freed slot reused ----------

    /// <summary>
    /// A held unit killed between ticks, then a new unit placed in its slot (a field type, then one without): the new unit
    /// inherits no timer and no hold; the field type hides exactly delay ticks after its own first pass, the other never.
    /// </summary>
    [Fact]
    public void AFreedSlot_ReusedByANewUnit_InheritsNoTimerAndNoHold()
    {
        Simulation sim = Scene(D, combat: false);
        World w = sim.World;
        UnitStore u = w.Units;
        int laborer = D.FindUnit("malazan_laborer");
        EntityHandle old = CombatScenes.Place(sim, 0, laborer, At(sim, 20, 20));
        Ticks(sim, 200);
        Assert.True(Held(sim, old));
        CombatSystem.HitUnit(w, new PendingHit(default, 1, old, false, u.Hp[old.Index]));
        Assert.False(u.IsAlive(old));
        Assert.False(u.Still!.HasState(old.Index));
        Assert.Equal(0, w.Units.Statuses.StealthedUnits);

        EntityHandle fresh = CombatScenes.Place(sim, 0, D.FindUnit("malazan_crossbowman"), At(sim, 30, 30));
        Assert.Equal(old.Index, fresh.Index);
        Assert.NotEqual(old.Generation, fresh.Generation);
        Assert.False(u.Still.HasState(fresh.Index));
        int first = sim.TickNumber;
        for (int t = 0; t < 70; t++)
        {
            sim.Tick();
            int now = sim.TickNumber - 1;
            Assert.True((now - first >= 60) == w.IsStealthed(fresh.Index), $"tick {now}: first seen {first}, stealthed {w.IsStealthed(fresh.Index)}");
        }
        Assert.Equal(first, u.Still.MovedTick[fresh.Index]);

        // A unit without the field in a slot a held unit left.
        CombatSystem.HitUnit(w, new PendingHit(default, 1, fresh, false, u.Hp[fresh.Index]));
        EntityHandle lancer = CombatScenes.Place(sim, 0, D.FindUnit("malazan_wickan_lancer"), At(sim, 30, 30));
        Assert.Equal(fresh.Index, lancer.Index);
        for (int t = 0; t < 100; t++)
        {
            sim.Tick();
            Assert.False(w.IsStealthed(lancer.Index));
            Assert.False(u.Still.HasState(lancer.Index));
            Assert.False(u.Statuses.HasStealth(lancer.Index));
        }
    }

    // ---------- a unit just trained ----------

    /// <summary>
    /// A Laborer (a field type in the copy) trained at a Malazan Town Hall: production spawns it in phase 3, so the pass of
    /// its spawn tick first sees it and dates it then. From its spawn on it is held exactly when the tick is at least 60 past
    /// its spawn or its last position change (it may step out of the way once). Two more in the queue spawn beside it.
    /// </summary>
    [Fact]
    public void AUnitJustTrained_IsDatedItsSpawnTick_AndHiddenExactlyTheDelayAfterItsLastChange()
    {
        Simulation sim = Scene(D, combat: false, units: 32);
        World w = sim.World;
        UnitStore u = w.Units;
        int hall = StartBase.BuildingOfSlot(D, w.FactionOf(0), BuildingSlot.TownHall);
        TowerTests.PlaceBuilding(sim, 0, hall, 20, 20);
        int laborer = D.FindUnit("malazan_laborer");
        for (int k = 0; k < 3; k++) sim.Enqueue(Command.Train(0, At(sim, 21, 21), laborer));
        var seenAt = new Dictionary<int, int>();
        var lastChange = new Dictionary<int, int>();
        var last = new Dictionary<int, Vector2>();
        int checkedTicks = 0, stopAt = int.MaxValue;
        for (int t = 0; t < 3000 && sim.TickNumber < stopAt; t++)
        {
            sim.Tick();
            int now = sim.TickNumber - 1;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i] || u.TypeId[i] != laborer) continue;
                if (!seenAt.ContainsKey(i))
                {
                    // Spawned in this tick's phase 3: the pass saw it where it was spawned.
                    seenAt[i] = now;
                    lastChange[i] = now;
                    Assert.Equal(now, u.Still!.MovedTick[i]);
                    last[i] = u.Still.StillAt[i]; // where the pass saw it: its spawn point (phase 3, before the pass)
                }
                bool due = now - lastChange[i] >= 60;
                Assert.True(due == Held(sim, new EntityHandle(i, u.Generation[i])), $"tick {now}: trained unit {i} (spawned {seenAt[i]}, last change {lastChange[i]}) held {u.Still!.Held[i]}");
                checkedTicks++;
                if (last[i] != u.Position[i]) lastChange[i] = now;
                last[i] = u.Position[i];
            }
            if (seenAt.Count == 3 && stopAt == int.MaxValue) stopAt = sim.TickNumber + 150;
        }
        Assert.Equal(3, seenAt.Count);
        foreach (int i in seenAt.Keys) Assert.True(Held(sim, new EntityHandle(i, u.Generation[i])), $"trained unit {i} never hid");
        _out.WriteLine($"spawned at {string.Join(", ", seenAt.Values)}; last changes {string.Join(", ", lastChange.Values)}");
    }

    // ---------- two rules on two different statuses ----------

    private static readonly Lazy<GameData> s_twoStatuses = new(() => AiScenes.DataWith(dir =>
    {
        dir.EditJson("common/statuses.json", r => r["statuses"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "zz_test_shadow", ["displayName"] = "Test", ["description"] = "Test only.", ["kind"] = "stealthed",
        }));
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "stillStealth", Block(3, "zz_test_shadow"));
        dir.EditJson("factions/malazan/faction.json", r => r["forestStealth"] = ForestScenes.Block(3));
    }));

    /// <summary>
    /// The forest holds <c>stealthed</c> and the stand-still rule holds a second stealth status on the same unit: two entries,
    /// one holder each, one stealth count; letting go of either leaves the other in force; with both gone no entry remains.
    /// </summary>
    [Fact]
    public void ForestAndStandStill_OnTwoDifferentStatuses_HoldTwoEntries_EachLetGoOnItsOwn()
    {
        GameData d = s_twoStatuses.Value;
        Simulation sim = Scene(d, combat: false);
        World w = sim.World;
        UnitStore u = w.Units;
        StatusStore s = u.Statuses;
        int a = d.FindStatus("stealthed"), b = d.FindStatus("zz_test_shadow");
        EntityHandle h = CombatScenes.Place(sim, 0, d.FindUnit("malazan_heavy_infantry"), At(sim, 21, 20));
        EntityHandle tree = ForestScenes.Tree(sim, 20, 20);
        Ticks(sim, 61);
        Assert.True(ForestHeld(sim, h));
        Assert.True(Held(sim, h));
        Assert.True(s.IndexOf(h.Index, a) >= 0 && s.IndexOf(h.Index, b) >= 0);
        Assert.Equal(1, StatusSystem.Holders(w, h.Index, a));
        Assert.Equal(1, StatusSystem.Holders(w, h.Index, b));
        Assert.Equal(1, s.StealthedUnits);
        for (int t = 0; t < 30; t++) sim.Tick(); // held entries never run out
        Assert.Equal(1, s.TicksRemaining[s.IndexOf(h.Index, a)]);
        Assert.Equal(1, s.TicksRemaining[s.IndexOf(h.Index, b)]);

        // The stand-still lets go (a nudge); the forest keeps its own entry and the unit stays hidden.
        u.Position[h.Index] += new Vector2(0.001f, 0f);
        sim.Tick();
        Assert.False(Held(sim, h));
        Assert.True(s.IndexOf(h.Index, b) < 0);
        Assert.True(ForestHeld(sim, h));
        Assert.True(w.IsStealthed(h.Index));
        Assert.Equal(1, s.StealthedUnits);

        // The forest lets go (the tree felled) while the stand-still holds again: the other way round.
        Ticks(sim, 60);
        Assert.True(Held(sim, h));
        Assert.True(w.Resources.Take(tree, int.MaxValue) > 0);
        sim.Tick();
        Assert.False(ForestHeld(sim, h));
        Assert.True(s.IndexOf(h.Index, a) < 0);
        Assert.True(Held(sim, h));
        Assert.True(w.IsStealthed(h.Index));

        // An hp loss lets go of the last one: nothing left.
        CombatSystem.HitUnit(w, new PendingHit(default, 1, h, false, 1));
        Assert.False(Held(sim, h));
        Assert.Equal(0, s.Count[h.Index]);
        Assert.Equal(0, s.StealthedUnits);
        Assert.False(w.IsStealthed(h.Index));
    }

    // ---------- the shared held entry against a model ----------

    /// <summary>
    /// The held-entry fuzz. A Malazan Heavy Infantry on the <see cref="StillScenes.Both"/> copy (both rules, one status) and a
    /// twin with neither rule (a Whirlwind Raider in its own sim). Between ticks, at random: a 1 mm nudge (the stand-still
    /// lets go), a tree planted or felled beside it (the forest takes hold or lets go), a 1-hp hit (both let go), a timed
    /// Stealthed (1-150 ticks) and a Revealed (1-80 ticks) applied to both. An independent model of when each rule holds
    /// (the nudges dated a tick early, the hits at the tick they precede) is checked after every tick, with the timed part
    /// tick-equal to the twin's, the entry existing exactly while a holder or the twin's timer is left, the stealth count, and
    /// the stealth predicate. Half-way the unit's sim is saved, loaded and replaced by the load.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    [InlineData(5UL)]
    [InlineData(6UL)]
    [InlineData(7UL)]
    [InlineData(8UL)]
    public void HeldEntry_RandomHoldsReleasesTimedAndReveals_MatchTheModelAndTheTwin_EveryTick(ulong seed)
    {
        GameData d = Both;
        const int ticks = 3000, delay = 60, saveAt = 1500;
        int status = Stealthed(d), revealed = d.FindStatus("revealed");
        Simulation main = Scene(d, combat: false), twin = Scene(d, combat: false);
        EntityHandle h = CombatScenes.Place(main, 0, d.FindUnit("malazan_heavy_infantry"), At(main, 21, 20));
        EntityHandle t = CombatScenes.Place(twin, 1, d.FindUnit("whirlwind_raider"), At(twin, 21, 20));
        var rng = new SimRng(seed, 1580);
        int lastMove = 0, lastHurt = -1, hits = 0;
        EntityHandle tree = default;
        bool treeUp = false;
        float nudge = 0.001f;
        int stillHeldTicks = 0, forestHeldTicks = 0, bothHeldTicks = 0, timedWhileHeld = 0;
        for (int k = 0; k < ticks; k++)
        {
            if (k == saveAt)
            {
                Simulation loaded = SaveTestRun.Load(d, SaveTestRun.Save(main));
                Assert.Equal(main.StateHash(), loaded.StateHash());
                main = loaded;
            }
            World w = main.World;
            UnitStore u = w.Units;
            if (rng.NextInt(0, 110) == 0)
            {
                u.Position[h.Index] += new Vector2(nudge, 0f);
                nudge = -nudge;
                lastMove = k - 1;
            }
            if (rng.NextInt(0, 50) == 0)
            {
                if (treeUp) Assert.True(w.Resources.Take(tree, int.MaxValue) > 0);
                else tree = ForestScenes.Tree(main, 20, 20);
                treeUp = !treeUp;
            }
            if (rng.NextInt(0, 160) == 0 && u.Hp[h.Index] > 5)
            {
                CombatSystem.HitUnit(w, new PendingHit(default, 1, h, false, 1));
                lastHurt = k;
                hits++;
            }
            if (rng.NextInt(0, 35) == 0)
            {
                int dur = rng.NextInt(1, 151);
                StatusSystem.Apply(w, h.Index, status, 0f, dur, 0);
                StatusSystem.Apply(twin.World, t.Index, status, 0f, dur, 1);
                if (Held(main, h) || ForestHeld(main, h)) timedWhileHeld++;
            }
            if (rng.NextInt(0, 120) == 0)
            {
                int dur = rng.NextInt(1, 81);
                StatusSystem.Apply(w, h.Index, revealed, 0f, dur, 1);
                StatusSystem.Apply(twin.World, t.Index, revealed, 0f, dur, 0);
            }
            main.Tick();
            twin.Tick();

            string at = $"seed {seed} tick {k}";
            bool hurtOk = lastHurt < 0 || k - lastHurt >= delay;
            bool still = k - lastMove >= delay && hurtOk, forest = treeUp && hurtOk;
            Assert.True(still == Held(main, h), $"{at}: stand-still held {Held(main, h)}, model {still} (last move {lastMove}, last hurt {lastHurt})");
            Assert.True(forest == ForestHeld(main, h), $"{at}: forest held {ForestHeld(main, h)}, model {forest}");
            Assert.Equal((still ? 1 : 0) + (forest ? 1 : 0), StatusSystem.Holders(main.World, h.Index, status));
            StatusStore ts = twin.World.Units.Statuses;
            int twinAt = ts.IndexOf(t.Index, status);
            int twinOwn = twinAt < 0 ? -1 : ts.TicksRemaining[twinAt];
            int own = OwnTicks(main, h);
            if (still || forest) Assert.True(own == Math.Max(0, twinOwn), $"{at}: timed part {own}, twin {twinOwn}");
            else Assert.True(own == twinOwn, $"{at}: entry {own}, twin {twinOwn}");
            Assert.True(main.World.Units.Statuses.StealthedUnits == (own >= 0 ? 1 : 0), $"{at}: stealth count");
            bool isRevealed = main.World.Units.Statuses.IndexOf(h.Index, revealed) >= 0;
            Assert.Equal(isRevealed, ts.IndexOf(t.Index, revealed) >= 0);
            Assert.True(((still || forest || twinOwn > 0) && !isRevealed) == main.World.IsStealthed(h.Index), $"{at}: stealthed {main.World.IsStealthed(h.Index)}");
            if (still) stillHeldTicks++;
            if (forest) forestHeldTicks++;
            if (still && forest) bothHeldTicks++;
        }
        _out.WriteLine($"seed {seed}: stand-still held {stillHeldTicks}, forest {forestHeldTicks}, both {bothHeldTicks} ticks; {hits} hits; {timedWhileHeld} timed applications onto a held entry");
        Assert.True(bothHeldTicks > 0 && timedWhileHeld > 0, "the fuzz never stacked both holders or a timed application on a hold");
    }

    // ---------- saves ----------

    /// <summary>
    /// Data with the field, no such unit alive (the one there was died): the save carries an all-default STIL part, loads,
    /// saves the same bytes again and continues hash-equal, also after a field unit is placed in both.
    /// </summary>
    [Fact]
    public void ASave_WithTheFieldInTheData_ButNoSuchUnitAlive_LoadsAndContinuesEqual()
    {
        Simulation sim = Scene(D, combat: false);
        World w = sim.World;
        EntityHandle field = CombatScenes.Place(sim, 0, D.FindUnit("malazan_laborer"), At(sim, 20, 20));
        CombatScenes.Place(sim, 0, D.FindUnit("malazan_wickan_lancer"), At(sim, 25, 20));
        CombatScenes.Place(sim, 1, D.FindUnit("whirlwind_raider"), At(sim, 30, 30));
        Ticks(sim, 80);
        Assert.True(Held(sim, field));
        CombatSystem.HitUnit(w, new PendingHit(default, 1, field, false, w.Units.Hp[field.Index]));
        Ticks(sim, 20);
        for (int i = 0; i < w.Units.Capacity; i++) Assert.False(w.Units.Still!.HasState(i));
        byte[] bytes = SaveTestRun.Save(sim);
        Assert.True(bytes.AsSpan().IndexOf("STIL"u8) > 0);
        Simulation loaded = SaveTestRun.Load(D, bytes);
        Assert.True(bytes.AsSpan().SequenceEqual(SaveTestRun.Save(loaded)));
        Assert.Equal(sim.StateHash(), loaded.StateHash());
        EntityHandle a = CombatScenes.Place(sim, 0, D.FindUnit("malazan_crossbowman"), At(sim, 10, 10));
        EntityHandle b = CombatScenes.Place(loaded, 0, D.FindUnit("malazan_crossbowman"), At(loaded, 10, 10));
        Assert.Equal(a, b);
        for (int t = 0; t < 120; t++)
        {
            sim.Tick();
            loaded.Tick();
            Assert.Equal(sim.StateHash(), loaded.StateHash());
        }
        Assert.True(Held(loaded, b));
    }

    // ---------- what the AI and the view are shown ----------

    /// <summary>
    /// A still-hidden enemy whose cell the player sees is absent from the AI's PlayerView (by slot and by the cursor) and from
    /// the view's FogView; its owner sees it; a nudge shows it to both at once, and it vanishes again a delay later.
    /// </summary>
    [Fact]
    public void PlayerViewAndFogView_NeverShowAStillHiddenEnemy()
    {
        Simulation sim = Scene(D, combat: false);
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle h = CombatScenes.Place(sim, 0, D.FindUnit("malazan_crossbowman"), At(sim, 20, 20));
        CombatScenes.Place(sim, 1, D.FindUnit("whirlwind_raider"), At(sim, 20, 20, dx: 8f)); // sees the cell, beyond 3 m
        var ai = new PlayerView(w, 1);
        var own = new PlayerView(w, 0);
        var fog = new FogView(w.Fog.Width, w.Fog.Height, u.Capacity, w.Buildings.Capacity, 1);
        bool Shown()
        {
            fog.Refresh(w.Fog, w.TickNumber, u.Alive, w.Buildings.Alive);
            bool byCursor = false;
            int slot = -1;
            while (ai.NextUnit(ref slot, out _))
                if (slot == h.Index) byCursor = true;
            bool bySlot = ai.TryGetUnit(h.Index, out _);
            Assert.Equal(bySlot, byCursor);
            Assert.Equal(bySlot, fog.UnitShown[h.Index]);
            Assert.True(own.TryGetUnit(h.Index, out _));
            return bySlot;
        }
        int shownWhileHidden = 0, hiddenTicks = 0;
        for (int round = 0; round < 3; round++)
        {
            for (int t = 0; t < 80; t++)
            {
                sim.Tick();
                if (!w.Fog.IsVisible(1, w.Fog.CellOf(u.Position[h.Index]))) continue;
                bool hidden = w.IsStealthed(h.Index) && !w.IsDetectedBy(1, h.Index);
                if (hidden) hiddenTicks++;
                if (Shown() == hidden) shownWhileHidden++;
            }
            u.Position[h.Index] += new Vector2(0.001f, 0f);
            sim.Tick();
            Assert.False(w.IsStealthed(h.Index));
            Assert.True(Shown(), $"round {round}: not shown the tick after it moved");
        }
        Assert.True(hiddenTicks > 50);
        Assert.Equal(0, shownWhileHidden);
    }

    // ---------- a packed crowd: shoves and jitter ----------

    /// <summary>
    /// 49 Crossbowmen packed 0.5 m apart (tighter than their bodies), combat off, with 8 Lancers walking through them now and
    /// then and random move orders to a third of the crowd: after every tick each live Crossbowman is held exactly when the
    /// tick is at least 60 past the last tick its own position changed in (a change in phase 9 of tick t is seen by t + 1's
    /// pass). Reports how long the crowd took to settle and whether separation kept any unit visible past 10 s.
    /// </summary>
    [Theory]
    [InlineData(11UL)]
    [InlineData(12UL)]
    public void APackedCrowd_EveryUnitIsHeldExactlyTheDelayAfterItsOwnLastPositionChange(ulong seed)
    {
        Simulation sim = new(new SimConfig(seed, 2, 128, 1024) { Data = D, Combat = false }, LocalMovementTests.Flat(64));
        World w = sim.World;
        UnitStore u = w.Units;
        int bow = D.FindUnit("malazan_crossbowman"), lancer = D.FindUnit("malazan_wickan_lancer");
        var crowd = new List<EntityHandle>();
        Vector2 centre = At(sim, 32, 32);
        for (int y = 0; y < 7; y++)
            for (int x = 0; x < 7; x++)
                crowd.Add(CombatScenes.Place(sim, 0, bow, centre + new Vector2((x - 3) * 0.5f, (y - 3) * 0.5f)));
        var walkers = new EntityHandle[8];
        for (int k = 0; k < walkers.Length; k++) walkers[k] = CombatScenes.Place(sim, 0, lancer, At(sim, 20, 28 + k));
        var lastChange = new int[u.Capacity];
        var last = new Vector2[u.Capacity];
        foreach (EntityHandle c in crowd) last[c.Index] = u.Position[c.Index];
        var rng = new SimRng(seed, 1581);
        int heldUnitTicks = 0, lateSettle = 0, worstSettle = 0;
        var orderedAt = new int[u.Capacity];
        Array.Fill(orderedAt, -1);
        for (int t = 0; t < 1600; t++)
        {
            if (t % 300 == 100)
            {
                Vector2 to = t % 600 == 100 ? At(sim, 44, 36) : At(sim, 20, 28);
                foreach (EntityHandle wk in walkers) sim.Enqueue(Command.Move(0, wk, to));
            }
            if (t % 250 == 50)
                foreach (EntityHandle c in crowd)
                    if (rng.NextInt(0, 3) == 0)
                    {
                        sim.Enqueue(Command.Move(0, c, centre + new Vector2(rng.NextInt(-6, 7), rng.NextInt(-6, 7))));
                        orderedAt[c.Index] = t;
                    }
            sim.Tick();
            foreach (EntityHandle c in crowd)
            {
                int i = c.Index;
                bool due = t - lastChange[i] >= 60;
                Assert.True(due == Held(sim, c), $"seed {seed} tick {t}: unit {i} held {Held(sim, c)}, last change {lastChange[i]}");
                Assert.True(due == w.IsStealthed(i), $"seed {seed} tick {t}: unit {i} stealthed {w.IsStealthed(i)}");
                if (due) heldUnitTicks++;
                if (u.Position[i] != last[i])
                {
                    last[i] = u.Position[i];
                    lastChange[i] = t;
                }
                // Settling: a unit whose order is done and that rested for the delay, measured from its arrival (Idle).
                if (orderedAt[i] >= 0 && u.State[i] == UnitState.Idle && due)
                {
                    int settle = t - orderedAt[i];
                    worstSettle = Math.Max(worstSettle, settle);
                    orderedAt[i] = -1;
                }
            }
        }
        foreach (EntityHandle c in crowd)
            if (orderedAt[c.Index] >= 0) lateSettle++;
        _out.WriteLine($"seed {seed}: {heldUnitTicks} held unit-ticks of {crowd.Count * 1600}; longest order-to-hidden {worstSettle} ticks; orders never followed by a hide {lateSettle}");
        Assert.True(heldUnitTicks > crowd.Count * 400);
    }

    // ---------- a full status store with both rules ----------

    /// <summary>
    /// Both rules due on a unit whose 8 slots are full: neither holds; when a slot frees, the forest's pass takes it and the
    /// stand-still pass joins the same entry in the same phase 5 (two holders, two held ticks); letting both go ends it.
    /// </summary>
    [Fact]
    public void AFullStore_WithBothRulesDue_HoldsNeither_ThenBothShareTheFreedSlot()
    {
        GameData d = Both;
        Simulation sim = Scene(d, combat: false);
        World w = sim.World;
        StatusStore s = w.Units.Statuses;
        int status = Stealthed(d);
        EntityHandle h = CombatScenes.Place(sim, 0, d.FindUnit("malazan_heavy_infantry"), At(sim, 21, 20));
        EntityHandle tree = ForestScenes.Tree(sim, 20, 20);
        for (int k = 0; k < StatusStore.PerUnit; k++)
            StatusSystem.Apply(w, h.Index, Mark(d, k), 0f, k == 3 ? 101 : 1000, 1);
        for (int t = 0; t <= 99; t++)
        {
            sim.Tick();
            Assert.False(Held(sim, h));
            Assert.False(ForestHeld(sim, h));
            Assert.False(s.HasStealth(h.Index));
        }
        sim.Tick();
        Assert.True(ForestHeld(sim, h));
        Assert.True(Held(sim, h));
        int at = s.IndexOf(h.Index, status);
        Assert.Equal(2, s.TicksRemaining[at]);
        Assert.Equal(2, StatusSystem.Holders(w, h.Index, status));
        Ticks(sim, 10);
        Assert.Equal(2, s.TicksRemaining[s.IndexOf(h.Index, status)]);
        Assert.True(w.Resources.Take(tree, int.MaxValue) > 0);
        sim.Tick();
        Assert.Equal(1, s.TicksRemaining[s.IndexOf(h.Index, status)]);
        CombatSystem.HitUnit(w, new PendingHit(default, 1, h, false, 1));
        Assert.True(s.IndexOf(h.Index, status) < 0);
        Assert.Equal(0, s.StealthedUnits);
    }
}
