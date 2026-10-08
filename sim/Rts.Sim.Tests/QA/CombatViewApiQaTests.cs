using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Tests.ViewApi;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M4-V1 (2026-10-08-0313): the combat ViewApi pieces (<see cref="UnitHpBars"/>, <see cref="HitFlash"/>,
/// <see cref="DeathMarkers"/>) driven the way the game drives them: frames of 0 to 8 ticks (pause and 8x), a random view
/// delta per frame (0 while paused), markers collected after every tick (the <c>SimRunner.Ticked</c> hook) and again per
/// frame (deduplicated), and reinforcements spawned into the brawl so freed slots are re-used between two frames. After
/// every frame the invariants hold and the sim's hash equals a bare twin fed the same commands.
/// </summary>
public class CombatViewApiQaTests
{
    private readonly ITestOutputHelper _out;

    public CombatViewApiQaTests(ITestOutputHelper output) => _out = output;

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(7UL)]
    [InlineData(19UL)]
    public void MultiTickFrames_SlotReuse_Pauses_InvariantsAndHashTwin(ulong seed)
    {
        Simulation a = CombatViewScene.Create(seed), b = CombatViewScene.Create(seed);
        CombatViewScene.Start(a, b);
        World w = a.World;
        UnitStore u = w.Units;
        var rng = new Random((int)seed * 7919);
        var flash = new HitFlash(u.Capacity);
        var markers = new DeathMarkers();
        var added = new int[u.Capacity + w.Buildings.Capacity];
        var removed = new int[markers.Capacity];
        var slots = new int[u.Capacity];
        var lastHp = new int[u.Capacity];
        var lastGen = new int[u.Capacity];
        Array.Fill(lastGen, -1);
        int hi = w.Data.FindUnit("malazan_heavy_infantry"), raider = w.Data.FindUnit("whirlwind_raider");
        long deaths = 0;
        int frames = 0, reused = 0, reusedFrames = 0, multiTick = 0, paused = 0, hits = 0;
        var freedSince = new HashSet<int>();

        flash.Update(u.Alive, u.Generation, u.Hp, 0.016f);
        for (int i = 0; i < u.Capacity; i++) { lastHp[i] = u.Hp[i]; lastGen[i] = u.Alive[i] ? u.Generation[i] : -1; }

        while (w.TickNumber < 3000)
        {
            // Reinforcements now and then, so freed slots are taken again (LIFO free list) between two frames.
            if (rng.Next(40) == 0 && u.Count < u.Capacity - 4)
            {
                for (int k = 0; k < 3; k++)
                {
                    int p = rng.Next(2);
                    var at = new Vector2(48f + (p == 0 ? -6f : 6f), 44f + rng.Next(9));
                    var c = Command.SpawnUnit(p, p == 0 ? hi : raider, at);
                    a.Enqueue(c);
                    b.Enqueue(c);
                }
            }
            int ticks = rng.Next(9); // 0 = a paused frame, up to 8 a frame at 8x
            if (ticks == 0) paused++;
            if (ticks > 1) multiTick++;
            for (int t = 0; t < ticks; t++)
            {
                a.Tick();
                b.Tick();
                foreach (DeathEvent d in w.Deaths) if (!d.IsBuilding) freedSince.Add(d.Victim.Index);
                deaths += markers.Collect(w.Deaths, w.TickNumber, added); // the Ticked hook
            }
            Assert.Equal(0, markers.Collect(w.Deaths, w.TickNumber, added)); // the frame loop: nothing twice
            markers.Expire(w.TickNumber, removed);
            float dt = ticks == 0 ? (rng.Next(2) == 0 ? 0f : 0.016f) : 0.004f + (float)rng.NextDouble() * 0.05f;
            flash.Update(u.Alive, u.Generation, u.Hp, dt);
            frames++;

            int n = UnitHpBars.Collect(u.Alive, u.TypeId, u.Hp, w.Data.Units, slots);
            int k2 = 0;
            for (int i = 0; i < u.Capacity; i++)
            {
                bool hurt = u.Alive[i] && u.Hp[i] < w.Data.Units[u.TypeId[i]].Hp;
                if (hurt) Assert.True(k2 < n && slots[k2++] == i, $"seed {seed} tick {w.TickNumber}: hurt unit {i} missing from the bars");
                bool sameUnit = u.Alive[i] && u.Generation[i] == lastGen[i];
                bool hit = sameUnit && u.Hp[i] < lastHp[i];
                Assert.True(hit == flash.WasHit(i), $"seed {seed} tick {w.TickNumber} slot {i}: WasHit {flash.WasHit(i)}, want {hit}");
                if (flash.IsLit(i)) Assert.True(sameUnit, $"seed {seed} tick {w.TickNumber} slot {i}: lit but dead or a new unit");
                if (hit) hits++;
                if (u.Alive[i] && lastGen[i] != -1 && u.Generation[i] != lastGen[i])
                {
                    reusedFrames++;
                    Assert.False(flash.IsLit(i)); // a new unit in a slot never inherits the old one's flash
                }
                if (u.Alive[i] && freedSince.Remove(i)) reused++;
                lastHp[i] = u.Hp[i];
                lastGen[i] = u.Alive[i] ? u.Generation[i] : -1;
            }
            Assert.Equal(k2, n);
            Assert.Equal(w.Losses[0] + w.Losses[1], deaths);
            Assert.Equal(deaths, markers.Added);
            Assert.True(markers.Count <= markers.Capacity);
            for (int i = 0; i < markers.Capacity; i++)
                if (markers.Active[i]) Assert.True(markers.ExpiresAt[i] > w.TickNumber);
            Assert.Equal(b.StateHash(), a.StateHash());
        }
        Assert.True(deaths > CombatViewScene.PerSide, $"{deaths} deaths");
        Assert.True(reused > 0, "no freed slot was re-used: the row proves nothing");
        _out.WriteLine($"seed {seed}: {frames} frames ({paused} paused, {multiTick} multi-tick), {deaths} deaths, {hits} hits, {reused} slots re-used ({reusedFrames} seen with a new generation), hash equal every frame");
    }

    [Fact]
    public void Markers_StormBiggerThanThePool_CollectsAll_KeepsTheNewest_SlotListShort()
    {
        // A tick with more deaths than the pool holds: Collect counts them all, the pool stays at its cap, and the added
        // list (shorter than the deaths) is filled with no overrun.
        var m = new DeathMarkers(16);
        var storm = new DeathEvent[40];
        for (int k = 0; k < storm.Length; k++) storm[k] = new DeathEvent(new EntityHandle(k, 1), false, 0, 0, 1, new Vector2(k, 0));
        var added = new int[10];
        Assert.Equal(40, m.Collect(storm, 5, added));
        Assert.Equal(16, m.Count);
        Assert.Equal(40, m.Added);
        Assert.Equal(24, m.Replaced);
        for (int i = 0; i < m.Capacity; i++) Assert.True(m.Position[i].X >= 24f, $"slot {i} kept death {m.Position[i].X}");
        var removed = new int[4];
        Assert.Equal(16, m.Expire(5 + DeathMarkers.UnitLifetimeTicks, removed)); // more than the removed list holds: still all gone
        Assert.Equal(0, m.Count);
    }

    [Fact]
    public void Flash_PausedFrames_DoNotFade_NegativeAndHugeDeltas_Bounded()
    {
        var alive = new[] { true };
        var gen = new[] { 4 };
        var hp = new[] { 100 };
        var f = new HitFlash(1);
        f.Update(alive, gen, hp, 0.016f);
        hp[0] = 90;
        f.Update(alive, gen, hp, 0.016f);
        for (int k = 0; k < 100; k++) f.Update(alive, gen, hp, 0f);
        Assert.Equal(HitFlash.DefaultSeconds, f.Left(0));
        f.Update(alive, gen, hp, float.PositiveInfinity);
        Assert.False(f.IsLit(0));
        Assert.Equal(0f, f.Left(0));
        // Spans shorter than the capacity: only the covered slots are touched, nothing throws.
        var g = new HitFlash(8);
        g.Update(alive, gen, hp, 0.016f);
        Assert.False(g.IsLit(7) || g.WasHit(-1) || g.IsLit(99));
    }
}
