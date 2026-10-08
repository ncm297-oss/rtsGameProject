using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M4-V3: <see cref="ProjectileTracker"/> (launch points, slot reuse, the lob arc) and <see cref="ImpactMarks"/> (the landing marks' ring).</summary>
[Collection(SerialCollection.Name)]
public class ProjectileTrackerTests
{
    private readonly ITestOutputHelper _out;

    public ProjectileTrackerTests(ITestOutputHelper output) => _out = output;

    private static GameData Data => TestSim.Data;
    private static ReadOnlySpan<ProjectileDef> Defs => Data.Projectiles.AsSpan();
    private static int Bolt => Data.FindProjectile("bolt");
    private static int Stone => Data.FindProjectile("catapult_stone");
    private static float Speed(int type) => Data.Projectiles[type].SpeedPerTick;

    private static void Spawn(ProjectileStore s, Vector2 from, Vector2 to, int type, int owner = 0) =>
        Assert.True(s.TrySpawn(from, to, Speed(type), type, owner, 0, default, default, false));

    // ---- launch points ----

    [Fact]
    public void FirstSeenOnTheFiringTick_RecordsTheLaunch_AndDropsTheSlotWhenItDies()
    {
        var s = new ProjectileStore(4);
        var t = new ProjectileTracker(4);
        var from = new Vector2(10f, 10f);
        var to = new Vector2(20f, 10f);
        Spawn(s, from, to, Bolt);
        t.Observe(s, Defs, 1);
        Assert.True(t.Tracked[0]);
        Assert.Equal(from, t.Launch[0]);
        Assert.Equal(10f, t.Length[0], 4);
        Assert.False(t.IsLob[0]);
        for (long tick = 2; tick < 6; tick++)
        {
            s.Fly();
            t.Observe(s, Defs, tick);
            Assert.Equal(from, t.Launch[0]);
        }
        Assert.Equal(1, t.Count);
        Assert.Equal(1, t.Started);
        s.Free(0);
        t.Observe(s, Defs, 6);
        Assert.False(t.Tracked[0]);
        Assert.Equal(0, t.Count);
        Assert.Equal(1, t.Dropped);
    }

    [Fact]
    public void FirstSeenATickLate_StillGetsTheExactLaunch_LaterStartsWhereItIs()
    {
        var s = new ProjectileStore(2);
        var t = new ProjectileTracker(2);
        var from = new Vector2(10f, 10f);
        Spawn(s, from, new Vector2(30f, 10f), Bolt);
        s.Fly(); // one tick after the shot: PrevPosition is still the launch point
        t.Observe(s, Defs, 2);
        Assert.Equal(from, t.Launch[0]);
        var late = new ProjectileTracker(2);
        s.Fly();
        late.Observe(s, Defs, 3);
        Assert.Equal(s.PrevPosition[0], late.Launch[0]);
    }

    [Fact]
    public void ASlotThatDiesBeforeTheFirstObservation_IsNeverTracked()
    {
        var s = new ProjectileStore(2);
        var t = new ProjectileTracker(2);
        Spawn(s, new Vector2(10f, 10f), new Vector2(11f, 10f), Bolt);
        s.Fly();
        s.Free(0);
        t.Observe(s, Defs, 5);
        Assert.Equal(0, t.Count);
        Assert.Equal(0, t.Started);
        Assert.False(t.Tracked[0]);
        Assert.Equal(0f, t.ArcHeight(0, new Vector2(10.5f, 10f)));
    }

    // ---- slot reuse between two observations ----

    [Fact]
    public void SlotReused_OnTheNextTick_IsANewShot()
    {
        // The sim frees a slot in phase 11 and a shot takes it in phase 10 of the next tick; an observer that missed the
        // tick in between sees one live slot with a new shot standing on its firing tick.
        var s = new ProjectileStore(2);
        var t = new ProjectileTracker(2);
        Spawn(s, new Vector2(10f, 10f), new Vector2(30f, 10f), Bolt);
        t.Observe(s, Defs, 1);
        s.Fly();
        t.Observe(s, Defs, 2);
        s.Free(0);
        var second = new Vector2(50f, 40f);
        Spawn(s, second, new Vector2(40f, 40f), Bolt);
        t.Observe(s, Defs, 3);
        Assert.Equal(second, t.Launch[0]);
        Assert.Equal(1, t.Reused);
        Assert.Equal(1, t.Count);
    }

    [Fact]
    public void SlotReused_AndFlownBeforeTheObserverLooks_IsANewShot()
    {
        var s = new ProjectileStore(2);
        var t = new ProjectileTracker(2);
        // Same type and owner, observed every tick: the new shot's last position is not where the old one was.
        Spawn(s, new Vector2(10f, 10f), new Vector2(30f, 10f), Bolt);
        t.Observe(s, Defs, 1);
        s.Fly();
        t.Observe(s, Defs, 2);
        s.Free(0);
        Spawn(s, new Vector2(60f, 60f), new Vector2(40f, 60f), Bolt);
        s.Fly();
        t.Observe(s, Defs, 3);
        Assert.Equal(new Vector2(60f, 60f), t.Launch[0]);
        Assert.Equal(1, t.Reused);

        // Ticks skipped: a lob is a new shot when its impact point moved (a lob never re-aims) ... (Fly, then fire: phase 10's order.)
        s.Fly();
        Spawn(s, new Vector2(10f, 30f), new Vector2(30f, 30f), Stone);
        t.Observe(s, Defs, 4);
        for (int k = 0; k < 3; k++) s.Fly();
        t.Observe(s, Defs, 7);
        s.Free(1);
        Spawn(s, new Vector2(10f, 30f), new Vector2(10f, 50f), Stone);
        s.Fly();
        s.Fly();
        t.Observe(s, Defs, 10);
        // Seen two ticks after its shot: it starts where it was a tick ago, aimed at its own impact point.
        Assert.Equal(s.PrevPosition[1], t.Launch[1]);
        Assert.Equal(new Vector2(10f, 50f), s.Target[1]);
        Assert.Equal(Vector2.Distance(s.PrevPosition[1], s.Target[1]), t.Length[1], 4);
        Assert.Equal(2, t.Reused);
        // ... or another type or owner took the slot.
        s.Free(1);
        Spawn(s, new Vector2(5f, 5f), new Vector2(25f, 5f), Bolt, owner: 1);
        s.Fly();
        s.Fly();
        t.Observe(s, Defs, 14);
        Assert.Equal(s.PrevPosition[1], t.Launch[1]);
        Assert.False(t.IsLob[1]);
        Assert.Equal(3, t.Reused);
        Assert.Equal(2, t.Count);
    }

    // ---- capacity, idempotence, allocation ----

    [Fact]
    public void AFullStore_IsTrackedSlotForSlot_AMismatchedStoreThrows_SameTickIsANoOp_ZeroBytes()
    {
        const int n = 300;
        var s = new ProjectileStore(n);
        var t = new ProjectileTracker(n);
        for (int i = 0; i < n; i++) Spawn(s, new Vector2(i * 0.5f, 10f), new Vector2(i * 0.5f, 80f), i % 3 == 0 ? Stone : Bolt);
        Assert.False(s.TrySpawn(Vector2.Zero, Vector2.One, 1f, Bolt, 0, 0, default, default, false)); // full: the shot is lost
        t.Observe(s, Defs, 1);
        Assert.Equal(n, t.Count);
        Assert.Equal(n, s.Count);
        t.Observe(s, Defs, 1);
        Assert.Equal(n, t.Started);
        Assert.Throws<ArgumentException>(() => t.Observe(new ProjectileStore(n + 1), Defs, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProjectileTracker(0));

        long tick = 2;
        ProjectileDef[] defs = Data.Projectiles.ToArray();
        AllocationProbe.AssertZero(() =>
        {
            for (int k = 0; k < 20; k++)
            {
                s.Fly();
                t.Observe(s, defs, tick++);
                float sum = 0f;
                for (int i = 0; i < n; i++) sum += t.ArcHeight(i, s.Position[i]);
                if (sum < 0f) throw new InvalidOperationException();
            }
        }, _out);
        Assert.Equal(n, t.Count);
    }

    // ---- the arc ----

    [Fact]
    public void LobArc_OnTheGroundAtLaunchAndOnTheLastDrawnTick_HighMidFlight()
    {
        var s = new ProjectileStore(1);
        var t = new ProjectileTracker(1);
        var from = new Vector2(10f, 10f);
        var to = new Vector2(34f, 10f); // the Catapult's 24 m range
        Spawn(s, from, to, Stone);
        int ticks = ProjectileStore.FlightTicks(24f, Speed(Stone));
        t.Observe(s, Defs, 0);
        Assert.True(t.IsLob[0]);
        Assert.Equal(0f, t.ArcHeight(0, s.Position[0]));
        float peak = 0f;
        for (int k = 1; k < ticks; k++)
        {
            s.Fly();
            t.Observe(s, Defs, k);
            Assert.True(s.Alive[0]);
            for (float a = 0f; a <= 1f; a += 0.25f)
            {
                Vector2 at = Vector2.Lerp(s.PrevPosition[0], s.Position[0], a);
                float h = t.ArcHeight(0, at);
                peak = MathF.Max(peak, h);
                if (k == ticks - 1) Assert.True(h <= 1e-4f, $"last drawn tick, alpha {a}: arc {h} m");
                if (k == 1 && a == 0f) Assert.Equal(0f, h);
            }
        }
        Assert.Equal(ProjectileTracker.Apex(24f), peak, 1);
        Assert.Equal(6f, ProjectileTracker.Apex(24f), 4);
        _out.WriteLine($"24 m stone: {ticks} ticks, peak {peak:F2} m");
        // Short throws keep the minimum apex; a lob too short to arc stays on the ground; aimed shots never arc.
        Assert.Equal(ProjectileTracker.MinApex, ProjectileTracker.Apex(1f));
        Assert.Equal(ProjectileTracker.MinApex, ProjectileTracker.Apex(float.NaN));
        Assert.Equal(ProjectileTracker.MaxApex, ProjectileTracker.Apex(500f));
        var s2 = new ProjectileStore(2);
        var t2 = new ProjectileTracker(2);
        Spawn(s2, from, from + new Vector2(0.9f, 0f), Stone); // 2 ticks at 0.6 m a tick
        Spawn(s2, from, from + new Vector2(9f, 0f), Bolt);
        t2.Observe(s2, Defs, 0);
        s2.Fly();
        t2.Observe(s2, Defs, 1);
        Assert.Equal(0f, t2.ArcHeight(0, s2.Position[0]));
        Assert.Equal(0f, t2.ArcHeight(1, s2.Position[1]));
        Assert.Equal(0f, t2.Progress(-1, Vector2.Zero));
        Assert.Equal(0f, t2.ArcHeight(7, Vector2.Zero));
    }

    [Fact]
    public void Sapper_ShortestShippedThrowThatArcs_PeaksAboveHalfAMeter()
    {
        // A 3 m throw (5 ticks at 0.6 m a tick) still reads as a lob: its arc runs over 3 steps and peaks at MinApex.
        var s = new ProjectileStore(1);
        var t = new ProjectileTracker(1);
        int sharper = Data.FindProjectile("sharper");
        Spawn(s, Vector2.Zero, new Vector2(3f, 0f), sharper);
        t.Observe(s, Defs, 0);
        float peak = 0f;
        for (int k = 1; s.Alive[0] && k < 10; k++)
        {
            s.Fly();
            if (s.Position[0] == s.Target[0]) break; // the landing tick frees it before any frame
            t.Observe(s, Defs, k);
            for (float a = 0f; a <= 1f; a += 0.1f) peak = MathF.Max(peak, t.ArcHeight(0, Vector2.Lerp(s.PrevPosition[0], s.Position[0], a)));
        }
        Assert.True(peak > 0.5f, $"3 m throw peaks at {peak} m");
    }

    // ---- the tracker on a real brawl ----

    [Fact]
    public void MixedBrawl_TrackerMatchesTheStoreEveryTick_AndTheSimHashEqualsABareTwin()
    {
        Simulation a = CombatScenes.Flat(size: 64, units: 200);
        Simulation b = CombatScenes.Flat(size: 64, units: 200);
        var center = new Vector2(64f, 64f);
        CombatScenes.MixedBrawl(a, 60, center, 14f);
        CombatScenes.MixedBrawl(b, 60, center, 14f);
        World w = a.World;
        var every = new ProjectileTracker(w.Projectiles.Capacity);
        var third = new ProjectileTracker(w.Projectiles.Capacity);
        var marks = new ImpactMarks();
        var added = new int[w.Projectiles.Capacity];
        int maxInFlight = 0, lobs = 0, impacts = 0;
        for (int tick = 0; tick < 500; tick++)
        {
            a.Tick();
            b.Tick();
            every.Observe(w.Projectiles, Defs, w.TickNumber);
            if (tick % 3 == 0) third.Observe(w.Projectiles, Defs, w.TickNumber);
            impacts += marks.Collect(w.Impacts, Defs, w.TickNumber, added);
            for (int i = 0; i < marks.Capacity; i++) marks.MarkDrawn(i);
            marks.Expire(w.TickNumber, added);
            Assert.Equal(w.Projectiles.Count, every.Count);
            if (tick % 3 == 0) Assert.Equal(w.Projectiles.Count, third.Count);
            ReadOnlySpan<bool> alive = w.Projectiles.Alive;
            for (int i = 0; i < alive.Length; i++)
            {
                if (!alive[i]) continue;
                Assert.True(every.Tracked[i]);
                if (every.IsLob[i]) lobs++;
                // Within the arc's span, never off the line launch -> impact for a lob.
                float h = every.ArcHeight(i, w.Projectiles.Position[i]);
                Assert.True(h >= 0f && h <= ProjectileTracker.MaxApex, $"slot {i} arc {h}");
            }
            maxInFlight = Math.Max(maxInFlight, w.Projectiles.Count);
            Assert.Equal(b.StateHash(), a.StateHash());
        }
        _out.WriteLine($"500 ticks: {every.Started} shots seen, max {maxInFlight} in flight, {lobs} lob slot-ticks, {impacts} impacts, {marks.Added} marks ({marks.Replaced} replaced), every-3rd tracker reused {third.Reused}");
        Assert.True(every.Started > 100 && lobs > 0 && impacts > 100, "the brawl fired too little to prove anything");
        Assert.Equal(0, every.Reused); // an every-tick observer always sees the freed tick
        Assert.Equal(impacts, marks.Added);
    }

    // ---- impact marks ----

    [Fact]
    public void ImpactMarks_KindsLifetimesRingAndCollectOncePerTick()
    {
        var m = new ImpactMarks(4);
        var added = new int[8];
        var removed = new int[8];
        ProjectileImpact[] impacts =
        {
            new(new Vector2(1f, 1f), Bolt, 0, Hit: true),
            new(new Vector2(2f, 2f), Bolt, 1, Hit: false),
            new(new Vector2(3f, 3f), Stone, 0, Hit: true),
        };
        Assert.Equal(3, m.Collect(impacts, Defs, 10, added));
        Assert.Equal(0, m.Collect(impacts, Defs, 10, added)); // the same tick again adds nothing
        Assert.Equal(ImpactMarkKind.Flash, m.Kind[added[0]]);
        Assert.Equal(ImpactMarkKind.Dust, m.Kind[added[1]]);
        Assert.Equal(ImpactMarkKind.Burst, m.Kind[added[2]]);
        Assert.Equal(ImpactMarkKind.Dust, ImpactMarks.KindOf(new ProjectileImpact(Vector2.Zero, 99, 0, false), Defs));
        Assert.Equal(4, ImpactMarks.FlashTicks);
        Assert.Equal(6, ImpactMarks.DustTicks);
        Assert.Equal(new Vector2(3f, 3f), m.Position[added[2]]);
        Assert.Equal(1, m.Owner[added[1]]);
        Assert.Equal(0.5f, m.Age(added[0], 11, 1f), 4);

        // Not drawn yet: nothing expires, however late.
        Assert.Equal(0, m.Expire(100, removed));
        for (int i = 0; i < m.Capacity; i++) m.MarkDrawn(i);
        Assert.Equal(0, m.Expire(13, removed));
        Assert.Equal(1, m.Expire(14, removed)); // the flash: 0.2 s
        Assert.Equal(added[0], removed[0]);
        Assert.Equal(1, m.Expire(16, removed)); // the dust: 0.3 s
        Assert.Equal(1, m.Expire(18, removed)); // the burst: 0.4 s
        Assert.Equal(0, m.Count);

        // The ring: six into four slots replaces the two oldest.
        var six = new ProjectileImpact[6];
        for (int i = 0; i < 6; i++) six[i] = new ProjectileImpact(new Vector2(i, 0f), Bolt, 0, true);
        Assert.Equal(6, m.Collect(six, Defs, 20, added));
        Assert.Equal(4, m.Count);
        Assert.Equal(2, m.Replaced);
        Assert.Equal(9, m.Added);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImpactMarks(0));
        m.MarkDrawn(-1);
        m.MarkDrawn(99);
    }
}
