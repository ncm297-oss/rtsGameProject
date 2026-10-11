using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;
using static Rts.Sim.Tests.AbilityScenes;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>
/// M4-V7b: the hp bar's and the first-sight flash's maximum is <see cref="World.MaxHp"/> per slot (BUG-0450: a Zealot with
/// Dryjhna's Prophecy at 70 / 83 is hurt), given to the ViewApi as a per-slot span (the view's <c>UnitViews.FillMaxHp</c>
/// copies it each frame; ViewApi never reads the <see cref="World"/>), and a summoned unit's expiry leaves no corpse marker.
/// </summary>
public class MaxHpAndExpiryViewTests
{
    private readonly ITestOutputHelper _out;

    public MaxHpAndExpiryViewTests(ITestOutputHelper output) => _out = output;

    // Each slot's World.MaxHp (0 for a dead slot): what UnitViews.FillMaxHp copies each frame.
    internal static int[] SlotMax(World w)
    {
        var max = new int[w.Units.Capacity];
        FillSlotMax(w, max);
        return max;
    }

    private static void FillSlotMax(World w, int[] max)
    {
        for (int i = 0; i < max.Length; i++) max[i] = w.Units.Alive[i] ? w.MaxHp(i) : 0;
    }

    // Player 0 has Prophecy (+20 Zealot hp); player 1 has not. Zealots at 70 / 83 and 83 / 83 for player 0, 62 / 63 and
    // 63 / 63 for player 1.
    private static (Simulation Sim, EntityHandle Hurt, EntityHandle Full, EntityHandle OtherHurt, EntityHandle OtherFull) Zealots()
    {
        Simulation sim = NoFights();
        World w = sim.World;
        int zealot = w.Data.FindUnit("whirlwind_zealot");
        ProductionSystem.Researched(w, 0, w.Data.FindTech("dryjhnas_prophecy"));
        EntityHandle hurt = Place(sim, 0, zealot, At(sim, 10, 10));
        EntityHandle full = Place(sim, 0, zealot, At(sim, 14, 10));
        EntityHandle otherHurt = Place(sim, 1, zealot, At(sim, 10, 20));
        EntityHandle otherFull = Place(sim, 1, zealot, At(sim, 14, 20));
        UnitStore u = w.Units;
        u.Hp[hurt.Index] = 70;
        u.Hp[full.Index] = 83;
        u.Hp[otherHurt.Index] = 62;
        return (sim, hurt, full, otherHurt, otherFull);
    }

    [Fact]
    public void Collect_PerSlotMax_UsesWorldMaxHp_ProphecyZealotAt70Of83Shows_83Of83DoesNot()
    {
        (Simulation sim, EntityHandle hurt, EntityHandle full, EntityHandle otherHurt, EntityHandle otherFull) = Zealots();
        World w = sim.World;
        UnitStore u = w.Units;
        Assert.Equal((83, 83, 63, 63), (w.MaxHp(hurt.Index), w.MaxHp(full.Index), w.MaxHp(otherHurt.Index), w.MaxHp(otherFull.Index)));
        Assert.Equal(83, u.Hp[full.Index]); // spawned at the upgraded maximum (BUG-0421)
        var slots = new int[8];
        int n = UnitHpBars.Collect(u.Alive, u.Hp, SlotMax(w), slots);
        Assert.Equal(new[] { hurt.Index, otherHurt.Index }.OrderBy(i => i), slots.Take(n));
        Assert.True(UnitHpBars.Shows(u.Hp[hurt.Index], w.MaxHp(hurt.Index), out float fill));
        Assert.Equal(70f / 83f, fill, 5);
        Assert.False(UnitHpBars.Shows(u.Hp[full.Index], w.MaxHp(full.Index), out _));
        // The M4-V1 form (each type's hp) is what the bug was: it lists only the unupgraded hurt one.
        n = UnitHpBars.Collect(u.Alive, u.TypeId, u.Hp, w.Data.Units, slots);
        Assert.Equal(new[] { otherHurt.Index }, slots.Take(n));
    }

    [Fact]
    public void Collect_PerSlotMax_SkipsSlotsNotShown_AndShortSpans()
    {
        (Simulation sim, EntityHandle hurt, _, EntityHandle otherHurt, _) = Zealots();
        World w = sim.World;
        UnitStore u = w.Units;
        var shown = new bool[u.Capacity];
        shown[otherHurt.Index] = true;
        var slots = new int[8];
        int[] max = SlotMax(w);
        Assert.Equal(1, UnitHpBars.Collect(shown, u.Hp, max, slots));
        Assert.Equal(otherHurt.Index, slots[0]);
        Assert.Equal(1, UnitHpBars.Collect(u.Alive, u.Hp, max, slots.AsSpan(0, 1)));
        Assert.Equal(Math.Min(hurt.Index, otherHurt.Index), slots[0]);
        Assert.Equal(0, UnitHpBars.Collect(u.Alive, u.Hp, max, Span<int>.Empty));
        Assert.Equal(0, UnitHpBars.Collect(ReadOnlySpan<bool>.Empty, u.Hp, max, slots));
        // A max span that ends before a hurt slot leaves it out; a max of 0 (a dead slot's) never shows.
        Assert.Equal(0, UnitHpBars.Collect(u.Alive, u.Hp, max.AsSpan(0, Math.Min(hurt.Index, otherHurt.Index)), slots));
        Assert.Equal(0, UnitHpBars.Collect(u.Alive, u.Hp, new int[u.Capacity], slots));
    }

    [Fact]
    public void Collect_PerSlotMax_EqualsABruteForceListOnWorldMaxHp_EveryTickOfTheBrawl()
    {
        Simulation sim = CombatViewScene.Create();
        CombatViewScene.Start(sim);
        World w = sim.World;
        UnitStore u = w.Units;
        var slots = new int[u.Capacity];
        var old = new int[u.Capacity];
        var max = new int[u.Capacity];
        int most = 0;
        for (int t = 0; t < 1500; t++)
        {
            sim.Tick();
            FillSlotMax(w, max);
            int n = UnitHpBars.Collect(u.Alive, u.Hp, max, slots);
            var want = new List<int>();
            for (int i = 0; i < u.Capacity; i++)
                if (u.Alive[i] && u.Hp[i] < w.MaxHp(i)) want.Add(i);
            Assert.Equal(want, slots.Take(n));
            // No hp tech in the brawl: the per-slot form equals the type-hp form.
            int m = UnitHpBars.Collect(u.Alive, u.TypeId, u.Hp, w.Data.Units, old);
            Assert.Equal(old.Take(m), slots.Take(n));
            most = Math.Max(most, n);
        }
        Assert.True(most > 5, $"only {most} hurt units at once");
    }

    [Fact]
    public void HitFlash_SlotMax_FirstSightBelowWorldMaxHp_Flashes_AtItDoesNot()
    {
        (Simulation sim, EntityHandle hurt, EntityHandle full, EntityHandle otherHurt, EntityHandle otherFull) = Zealots();
        World w = sim.World;
        UnitStore u = w.Units;
        var f = new HitFlash(u.Capacity);
        f.UpdateWithSlotMax(u.Alive, u.Generation, u.Hp, SlotMax(w), 0.016f);
        Assert.True(f.IsLit(hurt.Index) && f.WasHit(hurt.Index)); // 70 / 83: hit before its first frame
        Assert.False(f.IsLit(full.Index)); // 83 / 83
        Assert.True(f.IsLit(otherHurt.Index));
        Assert.False(f.IsLit(otherFull.Index));
        Assert.Equal(2, f.HitCount);
        // The per-type form (the bug): 70 is not below the type's 63, so no flash.
        var g = new HitFlash(w.Units.Capacity);
        var maxHp = w.Data.Units.Select(d => d.Hp).ToArray();
        g.Update(w.Units.Alive, w.Units.Generation, w.Units.Hp, w.Units.TypeId, maxHp, 0.016f);
        Assert.False(g.IsLit(hurt.Index));
        // Then the ordinary rule: a later hit lights it, a steady frame fades it.
        f.UpdateWithSlotMax(u.Alive, u.Generation, u.Hp, SlotMax(w), 1f);
        Assert.False(f.IsLit(hurt.Index));
        u.Hp[full.Index] = 80;
        f.UpdateWithSlotMax(u.Alive, u.Generation, u.Hp, SlotMax(w), 0.016f);
        Assert.True(f.WasHit(full.Index));
        // A slot past the max span's end gets no first-sight flash.
        var h = new HitFlash(u.Capacity);
        h.UpdateWithSlotMax(u.Alive, u.Generation, u.Hp, SlotMax(w).AsSpan(0, hurt.Index), 0.016f);
        Assert.False(h.IsLit(hurt.Index));
    }

    [Fact]
    public void DeathMarkers_Collect_AnExpiryAddsNoMarker()
    {
        var m = new DeathMarkers(8);
        var deaths = new[]
        {
            new DeathEvent(new EntityHandle(1, 1), false, 0, 0, 1, new Vector2(1, 1)),
            new DeathEvent(new EntityHandle(2, 1), false, 0, 0, -1, new Vector2(2, 2), Expired: true),
            new DeathEvent(new EntityHandle(3, 1), true, 0, 1, 0, new Vector2(3, 3)),
        };
        var added = new int[3];
        Assert.Equal(2, m.Collect(deaths, 5, added));
        Assert.Equal(2, m.Count);
        Assert.Equal(new Vector2(1, 1), m.Position[added[0]]);
        Assert.Equal(new Vector2(3, 3), m.Position[added[1]]);
        Assert.Equal(0, m.Collect(deaths.AsSpan(1, 1), 6, added));
        Assert.Equal(2, m.Count);
    }

    /// <summary>A real summon (the sim's test-only <c>zz_test_summon_brief</c>: 3 Heavy Infantry for 2 s): the three expiries are deaths in the store's list, and no marker.</summary>
    [Fact]
    public void DeathMarkers_ARealSummonsExpiry_LeavesNoCorpse_AndCountsNoKillOrLoss()
    {
        Simulation sim = SummonTests.Sim();
        World w = sim.World;
        EntityHandle mage = Place(sim, 0, w.Data.FindUnit("malazan_cadre_mage"), At(sim, 20, 20));
        sim.Tick();
        sim.Enqueue(Command.UseAbility(0, mage, 2, At(sim, 20, 20)));
        var markers = new DeathMarkers();
        var added = new int[16];
        int expiries = 0, summoned = 0;
        for (int t = 0; t < 200; t++)
        {
            sim.Tick();
            summoned = Math.Max(summoned, w.Units.SummonedUnits);
            foreach (DeathEvent d in w.Deaths) if (d.Expired) expiries++;
            markers.Collect(w.Deaths, w.TickNumber, added);
        }
        Assert.Equal(3, summoned);
        Assert.Equal(3, expiries);
        Assert.Equal(0, w.Units.SummonedUnits);
        Assert.Equal(0, markers.Added);
        Assert.Equal((0, 0, 0, 0), (w.Kills[0], w.Kills[1], w.Losses[0], w.Losses[1]));
        _out.WriteLine($"3 summoned, 3 expiries, {markers.Added} markers");
    }

    /// <summary>Allocation rows run alone (serial collection).</summary>
    [Collection(SerialCollection.Name)]
    public class Serial
    {
        private readonly ITestOutputHelper _out;

        public Serial(ITestOutputHelper output) => _out = output;

        [Fact]
        public void SlotMaxBarsAndFlash_AllocateZeroBytes()
        {
            (Simulation sim, _, _, _, _) = Zealots();
            World w = sim.World;
            UnitStore u = w.Units;
            var slots = new int[u.Capacity];
            var max = new int[u.Capacity];
            var flash = new HitFlash(u.Capacity);
            long sum = 0;
            Action block = () =>
            {
                for (int i = 0; i < max.Length; i++) max[i] = u.Alive[i] ? w.MaxHp(i) : 0;
                int n = UnitHpBars.Collect(u.Alive, u.Hp, max, slots);
                for (int k = 0; k < n; k++) sum += max[slots[k]];
                flash.UpdateWithSlotMax(u.Alive, u.Generation, u.Hp, max, 0.016f);
                sum += flash.LitCount;
            };
            block();
            int runs = AllocationProbe.AssertZero(block, _out);
            _out.WriteLine($"per-slot max + bars + flash: 0 bytes (runs {runs}), checksum {sum}");
        }
    }
}
