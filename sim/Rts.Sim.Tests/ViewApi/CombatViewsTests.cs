using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M4-V1: the hp bar, hit flash and death marker bookkeeping the combat views draw from, against the sim's own numbers.</summary>
public class CombatViewsTests
{
    private readonly ITestOutputHelper _out;

    public CombatViewsTests(ITestOutputHelper output) => _out = output;

    // ---- Hp bars ----

    [Theory]
    [InlineData(100, 100, false, 0f)]
    [InlineData(101, 100, false, 0f)]
    [InlineData(99, 100, true, 0.99f)]
    [InlineData(50, 100, true, 0.5f)]
    [InlineData(1, 130, true, 1f / 130f)]
    [InlineData(0, 100, true, 0f)]
    [InlineData(-5, 100, true, 0f)]
    [InlineData(5, 0, false, 0f)]
    public void HpBar_ShowsOnlyBelowMax_FillIsTheShare(int hp, int max, bool shows, float fill)
    {
        Assert.Equal(shows, UnitHpBars.Shows(hp, max, out float f));
        Assert.Equal(fill, f, 5);
    }

    [Fact]
    public void HpBar_Colour_GreenFull_YellowHalf_RedEmpty_Monotone()
    {
        Assert.Equal(UnitHpBars.Full, UnitHpBars.Color(1f));
        Assert.Equal(UnitHpBars.Half, UnitHpBars.Color(0.5f));
        Assert.Equal(UnitHpBars.Empty, UnitHpBars.Color(0f));
        Assert.Equal(UnitHpBars.Empty, UnitHpBars.Color(float.NaN));
        Assert.Equal(UnitHpBars.Full, UnitHpBars.Color(2f));
        // Green rises and red falls (or holds) as the fill grows.
        Vector3 last = UnitHpBars.Color(0f);
        for (int k = 1; k <= 100; k++)
        {
            Vector3 c = UnitHpBars.Color(k / 100f);
            Assert.True(c.Y >= last.Y - 1e-6f && c.X <= last.X + 1e-6f, $"fill {k / 100f}: {last} -> {c}");
            last = c;
        }
    }

    [Fact]
    public void HpBar_Collect_EqualsABruteForceListEveryTickOfTheBrawl()
    {
        Simulation sim = CombatViewScene.Create();
        CombatViewScene.Start(sim);
        UnitStore u = sim.World.Units;
        var slots = new int[u.Capacity];
        int maxBars = 0, ticks = 0;
        for (int t = 0; t < 1500; t++)
        {
            sim.Tick();
            ticks++;
            int n = UnitHpBars.Collect(u.Alive, u.TypeId, u.Hp, sim.World.Data.Units, slots);
            var want = new List<int>();
            for (int i = 0; i < u.Capacity; i++)
                if (u.Alive[i] && u.Hp[i] < sim.World.Data.Units[u.TypeId[i]].Hp) want.Add(i);
            Assert.Equal(want, slots.Take(n));
            maxBars = Math.Max(maxBars, n);
        }
        Assert.True(maxBars > 5, $"only {maxBars} hurt units at once: the brawl didn't fight");
        // A short span gets only what fits.
        Assert.True(UnitHpBars.Collect(u.Alive, u.TypeId, u.Hp, sim.World.Data.Units, Span<int>.Empty) == 0);
        _out.WriteLine($"{ticks} ticks, at most {maxBars} bars");
    }

    // ---- Hit flash ----

    [Fact]
    public void Flash_LitOnAHit_FadesAfterItsTime_RestartsOnARepeatHit()
    {
        var alive = new[] { true, true };
        var gen = new[] { 1, 1 };
        var hp = new[] { 100, 100 };
        var f = new HitFlash(2);
        f.Update(alive, gen, hp, 0.016f);
        Assert.False(f.IsLit(0) || f.IsLit(1));

        hp[0] = 90;
        f.Update(alive, gen, hp, 0.016f);
        Assert.True(f.IsLit(0) && f.WasHit(0));
        Assert.False(f.IsLit(1) || f.WasHit(1)); // never on an unhit unit
        Assert.Equal(HitFlash.DefaultSeconds, f.Left(0));
        Assert.Equal(1, f.HitCount);
        Assert.Equal(1, f.LitCount);

        // 0.1 s later: still lit, not a new hit.
        f.Update(alive, gen, hp, 0.1f);
        Assert.True(f.IsLit(0) && !f.WasHit(0));
        // Hit again at 0.1 s: the time starts again, so it stays lit past 0.15 s from the first hit.
        hp[0] = 80;
        f.Update(alive, gen, hp, 0.0f);
        Assert.True(f.IsLit(0) && f.WasHit(0));
        f.Update(alive, gen, hp, 0.1f);
        Assert.True(f.IsLit(0)); // 0.2 s after the first hit
        f.Update(alive, gen, hp, 0.06f);
        Assert.False(f.IsLit(0)); // 0.16 s after the second
        Assert.Equal(0, f.LitCount);
    }

    [Fact]
    public void Flash_HealOrNewUnitInTheSlotOrDeath_NeverLights()
    {
        var alive = new[] { true };
        var gen = new[] { 1 };
        var hp = new[] { 50 };
        var f = new HitFlash(1);
        f.Update(alive, gen, hp, 0.016f);
        hp[0] = 60; // repaired / healed
        f.Update(alive, gen, hp, 0.016f);
        Assert.False(f.IsLit(0));
        // A new unit in the slot with less hp than the old one had: not a hit.
        gen[0] = 2;
        hp[0] = 10;
        f.Update(alive, gen, hp, 0.016f);
        Assert.False(f.IsLit(0));
        // Lit, then dead: unlit at once.
        hp[0] = 5;
        f.Update(alive, gen, hp, 0.016f);
        Assert.True(f.IsLit(0));
        alive[0] = false;
        f.Update(alive, gen, hp, 0.016f);
        Assert.False(f.IsLit(0));
        // NaN or negative time doesn't fade or grow a flash.
        alive[0] = true;
        gen[0] = 3;
        hp[0] = 100;
        f.Update(alive, gen, hp, float.NaN);
        hp[0] = 99;
        f.Update(alive, gen, hp, -1f);
        Assert.Equal(HitFlash.DefaultSeconds, f.Left(0));
        f.Update(alive, gen, hp, float.NaN);
        Assert.Equal(HitFlash.DefaultSeconds, f.Left(0));
    }

    [Fact]
    public void Flash_EveryTickOfTheBrawl_HitsAreExactlyTheUnitsWhoseHpFell()
    {
        Simulation sim = CombatViewScene.Create();
        CombatViewScene.Start(sim);
        UnitStore u = sim.World.Units;
        var f = new HitFlash(u.Capacity);
        var lastHp = new int[u.Capacity];
        var lastGen = new int[u.Capacity];
        var lastHit = new int[u.Capacity];
        Array.Fill(lastHit, int.MinValue / 2);
        f.Update(u.Alive, u.Generation, u.Hp, 0.05f);
        for (int i = 0; i < u.Capacity; i++) { lastHp[i] = u.Hp[i]; lastGen[i] = u.Alive[i] ? u.Generation[i] : -1; }
        int hits = 0, repeatLit = 0;
        for (int t = 0; t < 1500; t++)
        {
            sim.Tick();
            f.Update(u.Alive, u.Generation, u.Hp, 0.06f); // one tick a frame
            for (int i = 0; i < u.Capacity; i++)
            {
                bool same = u.Alive[i] && lastGen[i] == u.Generation[i];
                bool hit = same && u.Hp[i] < lastHp[i];
                Assert.Equal(hit, f.WasHit(i));
                if (hit)
                {
                    hits++;
                    if (t - lastHit[i] <= 2) repeatLit++;
                    lastHit[i] = t;
                }
                // Lit exactly within the flash time after the last hit: 0.15 s over frames of 0.06 s is the hit frame and 2 more.
                bool wantLit = same && t - lastHit[i] <= 2;
                Assert.True(wantLit == f.IsLit(i), $"tick {t} slot {i}: lit {f.IsLit(i)}, want {wantLit} (last hit {lastHit[i]})");
                lastHp[i] = u.Hp[i];
                lastGen[i] = u.Alive[i] ? u.Generation[i] : -1;
                if (!u.Alive[i]) lastHit[i] = int.MinValue / 2;
            }
        }
        Assert.True(hits > 50, $"only {hits} hits");
        _out.WriteLine($"{hits} hits, {repeatLit} landed while the unit was still lit");
    }

    // ---- Death markers ----

    private static DeathEvent Death(int slot, bool building, float x = 10f, float y = 20f, int owner = 1, int type = 0) =>
        new(new EntityHandle(slot, 1), building, type, owner, 1 - owner, new Vector2(x, y));

    [Fact]
    public void Markers_UnitTenSeconds_BuildingTwenty_GameTicks()
    {
        Assert.Equal(200, DeathMarkers.UnitLifetimeTicks);
        Assert.Equal(400, DeathMarkers.BuildingLifetimeTicks);
        var m = new DeathMarkers(8);
        var removed = new int[8];
        int corpse = m.Add(Death(3, false, 1f, 2f), 100);
        int rubble = m.Add(Death(4, true, 5f, 6f, owner: 0, type: 2), 100);
        Assert.Equal(2, m.Count);
        Assert.Equal(new Vector2(1f, 2f), m.Position[corpse]);
        Assert.True(m.IsBuilding[rubble] && !m.IsBuilding[corpse]);
        Assert.Equal(0, m.Owner[rubble]);
        Assert.Equal(2, m.Type[rubble]);
        Assert.Equal(0, m.Expire(299, removed));
        Assert.Equal(1, m.Expire(300, removed));
        Assert.Equal(corpse, removed[0]);
        Assert.False(m.Active[corpse]);
        Assert.Equal(0, m.Expire(499, removed));
        Assert.Equal(1, m.Expire(500, removed));
        Assert.Equal(rubble, removed[0]);
        Assert.Equal(0, m.Count);
        Assert.Equal(2, m.Expired);
    }

    [Fact]
    public void Markers_CollectTakesATicksDeathsOnce()
    {
        var m = new DeathMarkers(16);
        var deaths = new[] { Death(1, false), Death(2, false), Death(3, true) };
        var added = new int[16];
        Assert.Equal(3, m.Collect(deaths, 7, added));
        Assert.Equal(0, m.Collect(deaths, 7, added)); // the frame loop after the tick hook: nothing twice
        Assert.Equal(3, m.Count);
        Assert.Equal(new[] { 0, 1, 2 }, added.Take(3));
        Assert.Equal(1, m.Collect(deaths.AsSpan(0, 1), 8, added));
        Assert.Equal(4, m.Count);
        Assert.Equal(7 + DeathMarkers.UnitLifetimeTicks, m.ExpiresAt[0]);
        Assert.Equal(7 + DeathMarkers.BuildingLifetimeTicks, m.ExpiresAt[2]);
        Assert.Equal(8, m.LastCollectedTick);
    }

    [Fact]
    public void Markers_DeathStorms_NeverPassTheCap_ReplaceTheOldest()
    {
        var m = new DeathMarkers();
        Assert.Equal(2000, m.Capacity);
        var storm = new DeathEvent[500];
        var added = new int[500];
        var removed = new int[m.Capacity];
        for (int tick = 1; tick <= 6; tick++)
        {
            for (int k = 0; k < storm.Length; k++) storm[k] = Death(k, k % 50 == 0, tick, k);
            Assert.Equal(500, m.Collect(storm, tick, added));
            Assert.True(m.Count <= m.Capacity);
            m.Expire(tick, removed);
        }
        Assert.Equal(2000, m.Count);
        Assert.Equal(3000, m.Added);
        Assert.Equal(1000, m.Replaced);
        // The ones left are the newest 2,000: ticks 3-6.
        for (int i = 0; i < m.Capacity; i++) Assert.True(m.Position[i].X >= 3f, $"slot {i} holds tick {m.Position[i].X}");
    }

    [Fact]
    public void Markers_TheBrawlsDeaths_OneMarkerEach_AtTheDeathPosition_RubbleForTheBuilding()
    {
        Simulation sim = CombatViewScene.Create();
        CombatViewScene.Start(sim);
        World w = sim.World;
        var m = new DeathMarkers();
        var added = new int[w.Units.Capacity + w.Buildings.Capacity];
        var removed = new int[m.Capacity];
        int unitDeaths = 0, buildingDeaths = 0, expired = 0;
        for (int t = 0; t < 8000 && (buildingDeaths == 0 || m.Count > 0); t++)
        {
            sim.Tick();
            ReadOnlySpan<DeathEvent> deaths = w.Deaths;
            int n = m.Collect(deaths, w.TickNumber, added);
            Assert.Equal(deaths.Length, n);
            for (int k = 0; k < n; k++)
            {
                int slot = added[k];
                Assert.Equal(deaths[k].Position, m.Position[slot]);
                Assert.Equal(deaths[k].IsBuilding, m.IsBuilding[slot]);
                Assert.Equal(deaths[k].VictimOwner, m.Owner[slot]);
                if (deaths[k].IsBuilding) buildingDeaths++;
                else
                {
                    unitDeaths++;
                    Assert.False(w.Units.IsAlive(deaths[k].Victim));
                }
            }
            int gone = m.Expire(w.TickNumber, removed);
            for (int k = 0; k < gone; k++) Assert.Equal(w.TickNumber, m.ExpiresAt[removed[k]]);
            expired += gone;
            Assert.Equal(w.Kills[0] + w.Kills[1], unitDeaths + buildingDeaths);
        }
        Assert.True(unitDeaths >= CombatViewScene.PerSide, $"{unitDeaths} unit deaths");
        Assert.Equal(1, buildingDeaths);
        Assert.Equal(unitDeaths + buildingDeaths, expired);
        _out.WriteLine($"{unitDeaths} unit deaths, {buildingDeaths} building (last marker gone at tick {w.TickNumber}), all expired on time; kills {w.Kills[0]} / {w.Kills[1]}");
    }

    // ---- Read-only proof and allocation ----

    [Fact]
    public void CombatViewReadsEveryTick_HashEqualsABareTwin()
    {
        Simulation a = CombatViewScene.Create(), b = CombatViewScene.Create();
        CombatViewScene.Start(a, b);
        World w = a.World;
        UnitStore u = w.Units;
        var slots = new int[u.Capacity];
        var flash = new HitFlash(u.Capacity);
        var markers = new DeathMarkers();
        var added = new int[u.Capacity + w.Buildings.Capacity];
        var removed = new int[markers.Capacity];
        long sink = 0;
        int deaths = 0;
        for (int t = 0; t < 4000; t++)
        {
            a.Tick();
            b.Tick();
            int n = UnitHpBars.Collect(u.Alive, u.TypeId, u.Hp, w.Data.Units, slots);
            for (int k = 0; k < n; k++)
            {
                UnitHpBars.Shows(u.Hp[slots[k]], w.Data.Units[u.TypeId[slots[k]]].Hp, out float fill);
                sink += (long)(UnitHpBars.Color(fill).X * 1000);
            }
            flash.Update(u.Alive, u.Generation, u.Hp, 0.05f);
            deaths += markers.Collect(w.Deaths, w.TickNumber, added);
            markers.Expire(w.TickNumber, removed);
            sink += flash.LitCount + w.Kills[0] + w.Losses[1];
            Assert.Equal(b.StateHash(), a.StateHash());
        }
        Assert.True(deaths > CombatViewScene.PerSide && markers.Added > 0, $"{deaths} deaths");
        Assert.True(w.Losses[0] + w.Losses[1] == deaths, "a death the markers missed");
        _out.WriteLine($"hash equal for 4,000 ticks, {deaths} deaths, sink {sink}");
    }

    /// <summary>Allocation rows run alone (serial collection).</summary>
    [Collection(SerialCollection.Name)]
    public class Serial
    {
        private readonly ITestOutputHelper _out;

        public Serial(ITestOutputHelper output) => _out = output;

        [Fact]
        public void BarsFlashMarkers_2000Units_DeathStorm_AllocateZeroBytes()
        {
            const int n = 2000;
            var store = new UnitStore(n);
            for (int i = 0; i < n; i++) store.Alloc();
            for (int i = 0; i < n; i++) store.Hp[i] = i % 3 == 0 ? 1 : 1000;
            var types = TestSim.Data.Units;
            var slots = new int[n];
            var flash = new HitFlash(n);
            var markers = new DeathMarkers();
            var storm = new DeathEvent[500];
            for (int k = 0; k < storm.Length; k++) storm[k] = new DeathEvent(new EntityHandle(k, 1), k % 7 == 0, 0, 0, 1, new Vector2(k, k));
            var added = new int[500];
            var removed = new int[markers.Capacity];
            long tick = 0;
            float sum = 0;
            Action block = () =>
            {
                int bars = UnitHpBars.Collect(store.Alive, store.TypeId, store.Hp, types, slots);
                for (int k = 0; k < bars; k++) sum += UnitHpBars.Color(k / (float)n).Y;
                for (int i = 0; i < n; i += 5) store.Hp[i]--;
                flash.Update(store.Alive, store.Generation, store.Hp, 0.016f);
                tick++;
                markers.Collect(storm, tick, added);
                markers.Expire(tick, removed);
                sum += flash.LitCount + markers.Count;
            };
            block();
            int runs = AllocationProbe.AssertZero(block, _out);
            Assert.True(markers.Count <= markers.Capacity);
            _out.WriteLine($"bars + flash + 500-death markers per frame: 0 bytes (runs {runs}), checksum {sum}");
        }
    }
}
