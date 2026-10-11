using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;
using static Rts.Sim.Tests.AbilityScenes;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>
/// QA M4-V7b (session 2026-10-10-1954): attacks on the per-slot hp maximum (BUG-0450) and on a summon's expiry as the view
/// reads it. The view's <c>UnitViews.FillMaxHp</c> lives in <c>game/</c>, so <see cref="FillMax"/> restates it from docs/03
/// "Implementation (M4-V1)": each live slot's <c>World.MaxHp</c>, 0 for a dead one.
/// </summary>
public class MaxHpExpiryQaTests
{
    private readonly ITestOutputHelper _out;

    public MaxHpExpiryQaTests(ITestOutputHelper output) => _out = output;

    private static void FillMax(World w, int[] max)
    {
        for (int i = 0; i < max.Length; i++) max[i] = w.Units.Alive[i] ? w.MaxHp(i) : 0;
    }

    private static int[] Bars(World w, int[] max)
    {
        var slots = new int[w.Units.Capacity];
        FillMax(w, max);
        int n = UnitHpBars.Collect(w.Units.Alive, w.Units.Hp, max, slots);
        return slots.Take(n).ToArray();
    }

    /// <summary>A Prophecy Zealot hurt exactly to its type's hp (63 / 83) is hurt: a bar of 63 / 83 and a first-sight flash.</summary>
    [Fact]
    public void ProphecyZealotAtExactlyTheTypeHp_63Of83_ShowsABar_AndFlashesOnFirstSight()
    {
        Simulation sim = NoFights();
        World w = sim.World;
        int zealot = w.Data.FindUnit("whirlwind_zealot");
        ProductionSystem.Researched(w, 0, w.Data.FindTech("dryjhnas_prophecy"));
        EntityHandle z = Place(sim, 0, zealot, At(sim, 10, 10));
        EntityHandle full = Place(sim, 0, zealot, At(sim, 14, 10));
        w.Units.Hp[z.Index] = w.Data.Units[zealot].Hp; // 63
        var max = new int[w.Units.Capacity];
        Assert.Equal(new[] { z.Index }, Bars(w, max));
        Assert.True(UnitHpBars.Shows(w.Units.Hp[z.Index], max[z.Index], out float fill));
        Assert.Equal(63f / 83f, fill, 5);
        var flash = new HitFlash(w.Units.Capacity);
        flash.UpdateWithSlotMax(w.Units.Alive, w.Units.Generation, w.Units.Hp, max, 0.016f);
        Assert.True(flash.IsLit(z.Index));
        Assert.False(flash.IsLit(full.Index));
    }

    /// <summary>A unit at its type's hp with no tech researched shows no bar and no first-sight flash (no false positives from the per-slot read).</summary>
    [Fact]
    public void NoTech_UnitsAtTypeHp_ShowNoBar_NoFlash()
    {
        Simulation sim = NoFights();
        World w = sim.World;
        var handles = new List<EntityHandle>();
        for (int t = 0; t < w.Data.Units.Length && handles.Count < 12; t++)
            handles.Add(Place(sim, t % 2, t, At(sim, 4 + 3 * (handles.Count % 6), 4 + 4 * (handles.Count / 6))));
        var max = new int[w.Units.Capacity];
        Assert.Empty(Bars(w, max));
        foreach (EntityHandle h in handles) Assert.Equal(w.Data.Units[w.Units.TypeId[h.Index]].Hp, max[h.Index]);
        var flash = new HitFlash(w.Units.Capacity);
        flash.UpdateWithSlotMax(w.Units.Alive, w.Units.Generation, w.Units.Hp, max, 0.016f);
        Assert.Equal(0, flash.LitCount);
    }

    /// <summary>
    /// Prophecy researched mid-fight: the frame after the research the bars read the new maximum (41 / 63 becomes 61 / 83 and
    /// keeps a bar at the new fill; 63 / 63 becomes 83 / 83 and has none), the other player's bars stay, and the hp rise lights no flash.
    /// </summary>
    [Fact]
    public void ProphecyResearchedMidFight_BarsFlipOnTheSameFrame_AndNoFlash()
    {
        Simulation sim = NoFights();
        World w = sim.World;
        UnitStore u = w.Units;
        int zealot = w.Data.FindUnit("whirlwind_zealot");
        EntityHandle hurt = Place(sim, 0, zealot, At(sim, 10, 10));
        EntityHandle full = Place(sim, 0, zealot, At(sim, 14, 10));
        EntityHandle other = Place(sim, 1, zealot, At(sim, 10, 20));
        u.Hp[hurt.Index] = 41;
        u.Hp[other.Index] = 41;
        var max = new int[u.Capacity];
        var flash = new HitFlash(u.Capacity);
        Assert.Equal(new[] { hurt.Index, other.Index }.OrderBy(i => i), Bars(w, max));
        flash.UpdateWithSlotMax(u.Alive, u.Generation, u.Hp, max, 1f);
        flash.UpdateWithSlotMax(u.Alive, u.Generation, u.Hp, max, 1f); // first-sight flashes faded
        Assert.Equal(0, flash.LitCount);

        ProductionSystem.Researched(w, 0, w.Data.FindTech("dryjhnas_prophecy"));
        Assert.Equal((61, 83, 41), (u.Hp[hurt.Index], u.Hp[full.Index], u.Hp[other.Index]));
        Assert.Equal(new[] { hurt.Index, other.Index }.OrderBy(i => i), Bars(w, max));
        Assert.Equal((83, 83, 63), (max[hurt.Index], max[full.Index], max[other.Index]));
        Assert.True(UnitHpBars.Shows(u.Hp[hurt.Index], max[hurt.Index], out float fill));
        Assert.Equal(61f / 83f, fill, 5);
        Assert.False(UnitHpBars.Shows(u.Hp[full.Index], max[full.Index], out _));
        flash.UpdateWithSlotMax(u.Alive, u.Generation, u.Hp, max, 0.016f);
        Assert.Equal(0, flash.LitCount);
    }

    /// <summary>
    /// A synthetic tick holding expiries and kills interleaved: only the kills get markers, the slots written are those kills'
    /// in order, and the count returned is the markers added even when the slot list is shorter (CombatViews' redraw path
    /// keys on that count).
    /// </summary>
    [Fact]
    public void DeathMarkers_ExpiriesInterleavedWithKills_OnlyKillsMarked_CountIsMarkersAdded()
    {
        var m = new DeathMarkers(16);
        var deaths = new[]
        {
            new DeathEvent(new EntityHandle(1, 1), false, 0, 0, -1, new Vector2(1, 1), Expired: true),
            new DeathEvent(new EntityHandle(2, 1), false, 0, 0, 1, new Vector2(2, 2)),
            new DeathEvent(new EntityHandle(3, 1), false, 0, 0, -1, new Vector2(3, 3), Expired: true),
            new DeathEvent(new EntityHandle(4, 1), false, 0, 1, 0, new Vector2(4, 4)),
            new DeathEvent(new EntityHandle(5, 1), true, 0, 1, 0, new Vector2(5, 5)),
            new DeathEvent(new EntityHandle(6, 1), false, 0, 0, -1, new Vector2(6, 6), Expired: true),
        };
        var added = new int[2];
        Assert.Equal(3, m.Collect(deaths, 7, added));
        Assert.Equal(3, m.Count);
        Assert.Equal((new Vector2(2, 2), new Vector2(4, 4)), (m.Position[added[0]], m.Position[added[1]]));
        // All expiries: nothing, 0 returned, and the tick is taken (a second call adds nothing either).
        var m2 = new DeathMarkers(4);
        Assert.Equal(0, m2.Collect(new[] { deaths[0], deaths[2] }, 9, added));
        Assert.Equal(0, m2.Collect(deaths, 9, added));
        Assert.Equal(0, m2.Count);
    }

    // A summon sim of the given seed with combat on (SummonTests' data: the Cadre Mage's ability 2 is the brief summon, 3 Heavy
    // Infantry for 2 s).
    private static Simulation SummonSim(ulong seed, bool combat) =>
        TestSim.Explored(new Simulation(new SimConfig(seed, 2, 64, 512) { Data = SummonTests.Data, Combat = combat }, LocalMovementTests.Flat(48)));

    /// <summary>
    /// The expiry on the same tick as a kill: an enemy Zealot at 1 hp is placed next to a Heavy at a delay that makes its
    /// death land on the summons' expiry tick (searched deterministically). On that tick the deaths list has three expiries
    /// and one kill; the marker ring adds exactly one corpse (the kill's), and K / L move by that kill only.
    /// </summary>
    [Fact]
    public void SummonExpiry_OnTheSameTickAsAKill_OnlyTheKillLeavesACorpse()
    {
        // Kills land on the attack scan's 4-tick grid; the cast is shifted 0-3 ticks so the expiry can meet one.
        int matched = 0;
        for (int run = 0; run < 4 * 24 && matched == 0; run++)
        {
            int shift = run / 24, delay = 26 + run % 24;
            Simulation sim = SummonSim(1, combat: true);
            World w = sim.World;
            UnitStore u = w.Units;
            int mageType = w.Data.FindUnit("malazan_cadre_mage"), heavy = w.Data.FindUnit("malazan_heavy_infantry"), zealot = w.Data.FindUnit("whirlwind_zealot");
            EntityHandle mage = Place(sim, 0, mageType, At(sim, 6, 6));
            EntityHandle killer = Place(sim, 0, heavy, At(sim, 40, 40));
            u.Hold[killer.Index] = true;
            for (int k = 0; k <= shift; k++) sim.Tick();
            sim.Enqueue(Command.UseAbility(0, mage, 2, At(sim, 6, 6)));
            var markers = new DeathMarkers();
            var added = new int[16];
            int expiryTick = -1, killTick = -1, expiries = 0, kills = 0;
            EntityHandle victim = default;
            for (int t = 0; t < 200; t++)
            {
                if (t == delay) victim = Place(sim, 1, zealot, At(sim, 41, 40));
                if (t == delay) u.Hp[victim.Index] = 1;
                sim.Tick();
                int tick = w.TickNumber;
                int e = 0, k = 0;
                foreach (DeathEvent d in w.Deaths) { if (d.Expired) e++; else k++; }
                if (e > 0) expiryTick = tick;
                if (k > 0) killTick = tick;
                expiries += e;
                kills += k;
                int n = markers.Collect(w.Deaths, tick, added);
                Assert.Equal(k, n);
                if (e > 0 && k > 0)
                {
                    matched++;
                    Assert.Equal(1, n);
                    Assert.False(markers.IsBuilding[added[0]]);
                    Assert.True(Vector2.Distance(u.Position[killer.Index], markers.Position[added[0]]) < 5f); // the victim by the killer, not a summon by the mage
                }
            }
            Assert.Equal(3, expiries);
            Assert.Equal(1, kills);
            Assert.Equal(1, markers.Added);
            Assert.Equal((1, 0, 0, 1), (w.Kills[0], w.Kills[1], w.Losses[0], w.Losses[1]));
            if (matched > 0) _out.WriteLine($"shift {shift}, delay {delay}: the kill and the three expiries on tick {expiryTick}; 1 marker");
        }
        Assert.True(matched > 0, "no delay put the kill on the expiry tick");
    }

    /// <summary>
    /// Slot reuse: after the summons expire, fresh units placed into the store take the freed slots. A fresh Heavy at full hp
    /// in an expired slot gets no bar and no flash; a Prophecy Zealot of the other player in another freed slot reads its own
    /// maximum (83), not the Heavy's; set below that before its first frame it flashes (a new generation) and gets a bar.
    /// </summary>
    [Fact]
    public void SlotReuse_AfterExpiry_FreshUnitsReadTheirOwnMaximum_AndInheritNoFlash()
    {
        Simulation sim = SummonSim(6, combat: false);
        World w = sim.World;
        UnitStore u = w.Units;
        int heavy = w.Data.FindUnit("malazan_heavy_infantry"), zealot = w.Data.FindUnit("whirlwind_zealot");
        ProductionSystem.Researched(w, 1, w.Data.FindTech("dryjhnas_prophecy"));
        EntityHandle mage = Place(sim, 0, w.Data.FindUnit("malazan_cadre_mage"), At(sim, 20, 20));
        sim.Tick();
        sim.Enqueue(Command.UseAbility(0, mage, 2, At(sim, 20, 20)));
        var max = new int[u.Capacity];
        var flash = new HitFlash(u.Capacity);
        List<EntityHandle> summoned = new();
        for (int t = 0; t < 40 && summoned.Count == 0; t++)
        {
            sim.Tick();
            summoned = SummonTests.Summoned(w);
        }
        Assert.Equal(3, summoned.Count);
        // The summoned units get hurt and lit while alive.
        FillMax(w, max);
        flash.UpdateWithSlotMax(u.Alive, u.Generation, u.Hp, max, 0.016f);
        foreach (EntityHandle h in summoned) u.Hp[h.Index] -= 5;
        FillMax(w, max);
        flash.UpdateWithSlotMax(u.Alive, u.Generation, u.Hp, max, 0.016f);
        foreach (EntityHandle h in summoned) Assert.True(flash.IsLit(h.Index));
        for (int t = 0; t < 100 && summoned.Any(h => u.IsAlive(h)); t++) sim.Tick();
        Assert.All(summoned, h => Assert.False(u.IsAlive(h)));

        // No frame between the expiry and the reuse: the flash still remembers the old units.
        EntityHandle freshHeavy = Place(sim, 0, heavy, At(sim, 30, 30));
        EntityHandle freshZealot = Place(sim, 1, zealot, At(sim, 34, 30));
        var freed = summoned.Select(h => h.Index).ToHashSet();
        Assert.Contains(freshHeavy.Index, freed);
        Assert.Contains(freshZealot.Index, freed);
        u.Hp[freshZealot.Index] = 70;
        int[] bars = Bars(w, max);
        Assert.Equal(new[] { freshZealot.Index }, bars);
        Assert.Equal(w.Data.Units[heavy].Hp, max[freshHeavy.Index]);
        Assert.Equal(83, max[freshZealot.Index]);
        flash.UpdateWithSlotMax(u.Alive, u.Generation, u.Hp, max, 0.016f);
        Assert.False(flash.IsLit(freshHeavy.Index));
        Assert.True(flash.IsLit(freshZealot.Index));
        Assert.Equal(1, flash.LitCount);
    }

    /// <summary>
    /// Read-only proof and K / L across seeds: two sims of the same seed run a summon cycle (cast, three units, expiry, a
    /// second cast) under a light fight; one also runs every new ViewApi read each tick (the per-slot bars and flash, the
    /// marker ring, IsNoTarget). Hashes equal every tick, no marker from an expiry, and the expiries move no kill or loss.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(6UL)]
    [InlineData(42UL)]
    public void NewViewApiReads_LeaveTheHashUnchanged_AndExpiriesMoveNoKOrL(ulong seed)
    {
        Simulation a = SummonSim(seed, combat: true), b = SummonSim(seed, combat: true);
        foreach (Simulation s in new[] { a, b })
        {
            int mageType = s.World.Data.FindUnit("malazan_cadre_mage");
            Place(s, 0, mageType, At(s, 10, 10));
            Place(s, 0, mageType, At(s, 36, 36));
        }
        World w = a.World;
        UnitStore u = w.Units;
        var max = new int[u.Capacity];
        var slots = new int[u.Capacity];
        var flash = new HitFlash(u.Capacity);
        var markers = new DeathMarkers();
        var added = new int[16];
        int expiries = 0, kinds = 0;
        for (int t = 0; t < 400; t++)
        {
            if (t == 1 || t == 120)
                foreach (Simulation s in new[] { a, b })
                    for (int i = 0; i < s.World.Units.Capacity; i++)
                        if (s.World.Units.Alive[i] && s.World.Units.TypeId[i] == s.World.Data.FindUnit("malazan_cadre_mage"))
                            s.Enqueue(Command.UseAbility(0, new EntityHandle(i, s.World.Units.Generation[i]), 2, s.World.Units.Position[i]));
            a.Tick();
            b.Tick();
            FillMax(w, max);
            int n = UnitHpBars.Collect(u.Alive, u.Hp, max, slots);
            for (int k = 0; k < n; k++) UnitHpBars.Shows(u.Hp[slots[k]], max[slots[k]], out _);
            flash.UpdateWithSlotMax(u.Alive, u.Generation, u.Hp, max, 0.05f);
            markers.Collect(w.Deaths, w.TickNumber, added);
            foreach (AbilityDef d in w.Data.Abilities) if (AbilityCaster.IsNoTarget(d.Kind)) kinds++;
            foreach (DeathEvent d in w.Deaths) if (d.Expired) expiries++;
            Assert.Equal(b.StateHash(), a.StateHash());
        }
        Assert.Equal(12, expiries); // two mages, two casts, three units each
        Assert.Equal(0, markers.Added);
        Assert.Equal((0, 0, 0, 0), (w.Kills[0], w.Kills[1], w.Losses[0], w.Losses[1]));
        Assert.Equal(0, u.SummonedUnits);
        Assert.True(kinds > 0);
        _out.WriteLine($"seed {seed}: 12 expiries, 0 markers, K/L 0, hash equal 400 ticks");
    }

    // An enemy (player 1) Priest's Sandstorm at `at`, cast from 12 m east; returns the zone slot once it exists.
    private static int EnemyStorm(Simulation sim, Vector2 at)
    {
        World w = sim.World;
        EntityHandle priest = Place(sim, 1, w.Data.FindUnit("whirlwind_priest"), at + new Vector2(12f, 0f));
        w.Units.Hold[priest.Index] = true;
        sim.Enqueue(Command.UseAbility(1, priest, 0, at));
        for (int t = 0; t < 80; t++)
        {
            sim.Tick();
            for (int k = 0; k < w.Zones.Capacity; k++)
                if (w.Zones.Alive[k] && w.Zones.Owner[k] == 1) return k;
        }
        throw new Xunit.Sdk.XunitException("no enemy storm in 80 ticks");
    }

    private static bool Marked(World w, int unit, int status, StatusMark[] marks)
    {
        int n = StatusMarkers.Collect(w.Units.Statuses, w.Units.Alive, w.Data.Statuses.Length, marks);
        for (int k = 0; k < n; k++) if (marks[k].Unit == unit && marks[k].Status == status) return true;
        return false;
    }

    /// <summary>
    /// An own Heavy blinded inside an enemy storm walks out: its Blinded marker stays while it is inside and for the
    /// status's 1 s (20 ticks) after its last tick inside, then goes (docs/02 Sandstorm row: the zone re-applies a 1 s
    /// status each tick). Measured by tick, not by frame.
    /// </summary>
    [Fact]
    public void BlindedMarker_GoesAboutOneSecondAfterTheUnitLeavesTheStorm()
    {
        Simulation sim = SummonSim(1, combat: false);
        World w = sim.World;
        int blinded = w.Data.FindStatus("blinded");
        EntityHandle heavy = Place(sim, 0, w.Data.FindUnit("malazan_heavy_infantry"), At(sim, 20, 24));
        sim.Tick();
        int zone = EnemyStorm(sim, w.Units.Position[heavy.Index]);
        Vector2 centre = w.Zones.Center[zone];
        float radius = w.Zones.Radius(zone);
        var marks = new StatusMark[64];
        for (int t = 0; t < 5; t++) sim.Tick();
        Assert.True(Marked(w, heavy.Index, blinded, marks));
        sim.Enqueue(Command.Move(0, heavy, At(sim, 20, 4)));
        int lastInside = -1, lastMarked = -1;
        for (int t = 0; t < 200; t++)
        {
            sim.Tick();
            int tick = w.TickNumber;
            if (Vector2.Distance(w.Units.Position[heavy.Index], centre) <= radius) lastInside = tick;
            if (Marked(w, heavy.Index, blinded, marks)) lastMarked = tick;
        }
        Assert.True(w.Zones.Alive[zone], "the storm ended before the test did");
        int gap = lastMarked - lastInside;
        _out.WriteLine($"last tick inside {lastInside}, last tick with the Blinded marker {lastMarked}: {gap} ticks");
        Assert.InRange(gap, 18, 22);
    }

    /// <summary>
    /// The enemy disc goes when the last own unit seeing the storm goes: three summoned Heavies stand in an enemy storm with
    /// no other own unit within sight of it (the mage walked off), so the disc is drawn; at their expiry it is no longer drawn
    /// from the first fog update after it, and on every tick the disc equals ZoneDiscs.SeesStorm on player 0's fog.
    /// </summary>
    [Fact]
    public void EnemyDisc_GoesWhenTheLastSeeingUnit_ASummon_Expires()
    {
        Simulation sim = SummonSim(6, combat: false);
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle mage = Place(sim, 0, w.Data.FindUnit("malazan_cadre_mage"), At(sim, 10, 24));
        sim.Tick();
        sim.Enqueue(Command.UseAbility(0, mage, 1, u.Position[mage.Index])); // 3 Heavies for 20 s
        List<EntityHandle> summoned = new();
        for (int t = 0; t < 40 && summoned.Count == 0; t++) { sim.Tick(); summoned = SummonTests.Summoned(w); }
        Assert.Equal(3, summoned.Count);
        foreach (EntityHandle h in summoned) u.Hold[h.Index] = true;
        int expires = u.ExpiresAtTick[summoned[0].Index];
        sim.Enqueue(Command.Move(0, mage, At(sim, 44, 44)));
        while (w.TickNumber < expires - 200) sim.Tick();
        Vector2 mid = (u.Position[summoned[0].Index] + u.Position[summoned[1].Index] + u.Position[summoned[2].Index]) / 3f;
        int zone = EnemyStorm(sim, mid);
        Vector2 centre = w.Zones.Center[zone];
        float radius = w.Zones.Radius(zone);
        float clear = w.Data.Units[u.TypeId[mage.Index]].Sight + radius + ZoneDiscs.RimWidth + 2f;
        Assert.True(Vector2.Distance(u.Position[mage.Index], centre) > clear, $"the mage is only {Vector2.Distance(u.Position[mage.Index], centre)} m off (want {clear})");
        var discs = new ZoneDisc[16];
        int drawnBefore = 0, drawnAfter = 0, firstGone = -1, mismatches = 0;
        while (w.Zones.Alive[zone] && w.TickNumber < expires + 40)
        {
            sim.Tick();
            int n = ZoneDiscs.Collect(w.Zones, w.Fog, 0, true, discs);
            bool drawn = false;
            for (int k = 0; k < n; k++) if (discs[k].Slot == zone && !discs[k].Own) drawn = true;
            if (drawn != ZoneDiscs.SeesStorm(w.Fog.Visibility(0), w.Fog.Width, w.Fog.Height, centre, radius)) mismatches++;
            if (w.TickNumber <= expires) { if (drawn) drawnBefore++; }
            else if (drawn) drawnAfter++;
            else if (firstGone < 0) firstGone = w.TickNumber;
        }
        Assert.True(w.Zones.Alive[zone], "the storm ended before the expiry window closed");
        Assert.All(summoned, h => Assert.False(u.IsAlive(h)));
        _out.WriteLine($"expiry tick {expires}: disc drawn on {drawnBefore} ticks before, {drawnAfter} after; first tick without it {firstGone}; {mismatches} mismatches");
        Assert.Equal(0, mismatches);
        Assert.True(drawnBefore > 100);
        Assert.InRange(firstGone, expires + 1, expires + VisionConstantsInterval);
        Assert.True(drawnAfter <= VisionConstantsInterval);
    }

    private const int VisionConstantsInterval = Rts.Sim.Vision.VisionConstants.UpdateInterval;
}
