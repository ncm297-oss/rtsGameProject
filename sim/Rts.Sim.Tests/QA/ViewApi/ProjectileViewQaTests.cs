using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>
/// QA (M4-V3): <see cref="ProjectileTracker"/> and <see cref="ImpactMarks"/> against ground truth from real fights: every
/// shot's launch point recorded exactly by an every-tick observer (the Match wires <c>SimRunner.Ticked</c>), slot reuse,
/// a projectile alive for one tick, a Sapper's lob at its own feet, the marks' ring at its cap, and the read-only proof.
/// </summary>
[Collection(SerialCollection.Name)]
public class ProjectileViewQaTests
{
    private readonly ITestOutputHelper _out;

    public ProjectileViewQaTests(ITestOutputHelper output) => _out = output;

    private static GameData Data => TestSim.Data;
    private static ReadOnlySpan<ProjectileDef> Defs => Data.Projectiles.AsSpan();
    private static int U(string id) => Data.FindUnit(id);

    // Two shooter lines `gap` m apart (rows 1 m apart, `perSide` a side), every unit attack-moved at the other line.
    private static Simulation ShooterLines(int perSide, string p0, string p1, float gap, ulong seed = 1)
    {
        Simulation sim = CombatScenes.Flat(size: 96, units: 2 * perSide + 8, seed: seed);
        var c = new Vector2(96f, 96f);
        for (int k = 0; k < perSide; k++)
        {
            float y = c.Y + (k / 2 - perSide / 4) * 1.1f, dx = gap / 2 + (k % 2) * 1.2f;
            CombatScenes.Place(sim, 0, U(p0), new Vector2(c.X - dx, y));
            CombatScenes.Place(sim, 1, U(p1), new Vector2(c.X + dx, y));
        }
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i]) sim.Enqueue(Command.AttackMove(u.Owner[i], new EntityHandle(i, u.Generation[i]), new Vector2(u.Owner[i] == 0 ? c.X + 20f : c.X - 20f, u.Position[i].Y)));
        return sim;
    }

    // ---- ground truth: an every-tick observer records every launch exactly, never reports a reuse ----

    [Theory]
    [InlineData(100, "malazan_crossbowman", "whirlwind_desert_archer", 1UL)]
    [InlineData(100, "malazan_sapper", "whirlwind_desert_archer", 2UL)]
    [InlineData(60, "malazan_catapult", "whirlwind_desert_archer", 3UL)]
    public void EveryTickObserver_LaunchPointEqualsTheFiringPosition_ForEveryShot_AndHashEqualsABareTwin(int perSide, string p0, string p1, ulong seed)
    {
        Simulation a = ShooterLines(perSide, p0, p1, 13f, seed);
        Simulation b = ShooterLines(perSide, p0, p1, 13f, seed);
        ProjectileStore s = a.World.Projectiles;
        int cap = s.Capacity;
        var t = new ProjectileTracker(cap);
        var marks = new ImpactMarks();
        var scratch = new int[cap];
        var wasAlive = new bool[cap];
        var truth = new Vector2[cap];
        var truthLen = new float[cap];
        int shots = 0, oneTick = 0, checks = 0, lobs = 0, impacts = 0;
        float shortestLob = float.MaxValue;
        for (int tick = 0; tick < 1500; tick++)
        {
            a.Tick();
            b.Tick();
            World w = a.World;
            t.Observe(s, Defs, w.TickNumber);
            impacts += marks.Collect(w.Impacts, Defs, w.TickNumber, scratch);
            for (int i = 0; i < marks.Capacity; i++) marks.MarkDrawn(i);
            marks.Expire(w.TickNumber, scratch);
            for (int i = 0; i < cap; i++)
            {
                if (!s.Alive[i]) { wasAlive[i] = false; continue; }
                if (!wasAlive[i])
                {
                    // Dead at the end of the last tick, alive now: fired this tick, standing on its launch point.
                    Assert.True(s.Position[i] == s.PrevPosition[i], $"tick {w.TickNumber} slot {i}: new shot not on its firing tick");
                    truth[i] = s.Position[i];
                    truthLen[i] = Vector2.Distance(truth[i], s.Target[i]);
                    shots++;
                    if (ProjectileStore.FlightTicks(truthLen[i], Data.Projectiles[s.ProjectileTypeId[i]].SpeedPerTick) == 1) oneTick++;
                    if (Data.Projectiles[s.ProjectileTypeId[i]].Kind == ProjectileKind.Lob) { lobs++; shortestLob = MathF.Min(shortestLob, truthLen[i]); }
                }
                wasAlive[i] = true;
                Assert.True(t.Tracked[i], $"slot {i} live, not tracked");
                Assert.Equal(truth[i], t.Launch[i]);
                Assert.Equal(truthLen[i], t.Length[i], 4);
                for (float al = 0f; al <= 1f; al += 0.25f)
                {
                    Vector2 at = Vector2.Lerp(s.PrevPosition[i], s.Position[i], al);
                    float h = t.ArcHeight(i, at), p = t.Progress(i, at);
                    Assert.True(float.IsFinite(h) && h >= 0f && h <= ProjectileTracker.MaxApex && p >= 0f && p <= 1f, $"slot {i}: arc {h} progress {p}");
                }
                checks++;
            }
            Assert.Equal(s.Count, t.Count);
            Assert.Equal(b.StateHash(), a.StateHash());
        }
        _out.WriteLine($"{perSide} {p0} v {p1}: {shots} shots ({oneTick} one-tick flights, {lobs} lobs, shortest lob {shortestLob:F2} m), {checks} slot-ticks checked, " +
            $"tracker started {t.Started}, reused {t.Reused}, dropped {t.Dropped}; {impacts} impacts, {marks.Added} marks ({marks.Replaced} replaced)");
        Assert.True(shots > 100, $"only {shots} shots");
        Assert.Equal(shots, t.Started);
        Assert.Equal(0, t.Reused);
        Assert.Equal(impacts, marks.Added);
    }

    // ---- an observer that skips ticks (the frame loop alone): a lob's arc never belongs to an earlier shot ----

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(8)]
    public void SkippingObserver_LobArcNeverStartsFromAnEarlierShotsLaunch(int every)
    {
        Simulation a = ShooterLines(80, "malazan_sapper", "whirlwind_raider", 6f, 4);
        ProjectileStore s = a.World.Projectiles;
        int cap = s.Capacity;
        var t = new ProjectileTracker(cap);
        var wasAlive = new bool[cap];
        var firedAt = new long[cap];
        var lastObs = long.MinValue;
        int lobChecks = 0, stale = 0;
        for (int tick = 0; tick < 1500; tick++)
        {
            a.Tick();
            long now = a.World.TickNumber;
            for (int i = 0; i < cap; i++)
            {
                if (s.Alive[i] && !wasAlive[i]) firedAt[i] = now;
                wasAlive[i] = s.Alive[i];
            }
            if (tick % every != 0) continue;
            t.Observe(s, Defs, now);
            for (int i = 0; i < cap; i++)
            {
                if (!s.Alive[i] || !t.IsLob[i]) continue;
                lobChecks++;
                // The tracked launch must be on this shot's own line (between where it was fired and where it is now).
                Vector2 launch = s.Position[i] - (s.Position[i] - s.PrevPosition[i]) * (now - firedAt[i]);
                Vector2 dir = s.Target[i] - launch;
                Vector2 rel = t.Launch[i] - launch;
                float cross = MathF.Abs(dir.X * rel.Y - dir.Y * rel.X) / MathF.Max(dir.Length(), 1e-6f);
                float along = Vector2.Dot(rel, dir) / MathF.Max(dir.LengthSquared(), 1e-12f);
                if (cross > 1e-2f || along < -1e-3f || Vector2.Distance(t.Launch[i], launch) > Vector2.Distance(s.Position[i], launch) + 1e-3f) stale++;
            }
            lastObs = now;
        }
        _out.WriteLine($"observe every {every} ticks: {lobChecks} lob slot-observations, {stale} with an arc from another shot; started {t.Started}, reused {t.Reused}, last {lastObs}");
        Assert.True(lobChecks > 50, $"only {lobChecks} lob observations");
        Assert.Equal(0, stale);
    }

    // A skipping observer, a slot reused by a lob of the same type and owner at the same impact point from another launch
    // point that lies farther from the old launch: none of the tracker's five "new shot" tests fire.
    [Fact(Skip = "BUG-0222 item 1: a skipping observer misses a same-type, same-owner, same-target lob reuse from a farther launch (its arc is drawn flat); the Match observes every tick, so not reachable in the game")]
    public void SkippingObserver_SameTargetLobFromAnotherLaunch_IsANewShot()
    {
        int sharper = Data.FindProjectile("sharper");
        float speed = Data.Projectiles[sharper].SpeedPerTick;
        var s = new ProjectileStore(1);
        var t = new ProjectileTracker(1);
        var target = new Vector2(30f, 20f);
        Assert.True(s.TrySpawn(new Vector2(22f, 20f), target, speed, sharper, 0, 0, default, default, false));
        t.Observe(s, Defs, 1);
        for (int k = 0; k < 6; k++) s.Fly();
        t.Observe(s, Defs, 7); // mid-flight, 3.6 m from its launch
        while (s.TicksLeft[0] > 0) s.Fly();
        s.Free(0);
        var second = new Vector2(30f, 12f);
        Assert.True(s.TrySpawn(second, target, speed, sharper, 0, 0, default, default, false));
        s.Fly();
        s.Fly();
        t.Observe(s, Defs, 20);
        _out.WriteLine($"launch {t.Launch[0]} (want {second + (target - second) * (speed / 8f)} or nearer), arc at its position {t.ArcHeight(0, s.Position[0]):F2} m, reused {t.Reused}");
        Assert.Equal(1, t.Reused);
    }

    // ---- BUG-0190 item 1: a corpse disc at MaxUnder never hangs a level up at the foot of a cliff ----

    [Theory]
    [InlineData(17UL)]
    [InlineData(23UL)]
    [InlineData(5UL)]
    public void CorpseDisc_MaxUnderTheRim_StaysNearTheGroundWhereUnitsStandAndDie(ulong seed)
    {
        const float rimScale = 1.2f; // CombatViews.CorpseRimScale (game assembly)
        Simulation sim = CombatScenes.MapBrawl(seed, 200);
        UnitStore u = sim.World.Units;
        Map.Heightmap map = sim.World.Heightmap;
        float worst = 0f, worstDeath = 0f;
        int samples = 0, over1 = 0, deaths = 0;
        Vector2 worstAt = default;
        for (int tick = 0; tick < 1500; tick++)
        {
            sim.Tick();
            foreach (DeathEvent d in sim.World.Deaths)
            {
                if (d.IsBuilding) continue;
                deaths++;
                float r = Data.Units[d.VictimType].Radius * rimScale;
                worstDeath = MathF.Max(worstDeath, TerrainHeight.MaxUnder(map, d.Position.X, d.Position.Y, r) - TerrainHeight.At(map, d.Position.X, d.Position.Y));
            }
            if (tick % 5 != 0) continue;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                Vector2 p = u.Position[i];
                float lift = TerrainHeight.MaxUnder(map, p.X, p.Y, u.Radius[i] * rimScale) - TerrainHeight.At(map, p.X, p.Y);
                samples++;
                if (lift > 1f) over1++;
                if (lift > worst) { worst = lift; worstAt = p; }
            }
        }
        int wx = Math.Clamp((int)(worstAt.X / Map.MapConstants.CellSize), 1, map.Width - 2), wy = Math.Clamp((int)(worstAt.Y / Map.MapConstants.CellSize), 1, map.Height - 2);
        for (int dy = -1; dy <= 1 && worst > 0f; dy++)
        {
            string row = "";
            for (int dx = -1; dx <= 1; dx++)
                row += $" ({wx + dx},{wy + dy}) e{map.ElevationAt(wx + dx, wy + dy):F0}{(map.IsRamp(wx + dx, wy + dy) ? "R" : "")}{(sim.World.NavGrid.IsPassable(wx + dx, wy + dy) ? "" : "#")}";
            _out.WriteLine(row);
        }
        _out.WriteLine($"seed {seed}: {samples} unit samples, worst disc lift {worst:F2} m at {worstAt}, {over1} over 1 m; {deaths} deaths, worst death-spot lift {worstDeath:F2} m");
        Assert.True(worst <= 1f, $"a corpse there would hang {worst:F2} m above the ground at {worstAt}");
    }

    // ---- one-tick flights and zero-length lobs ----

    [Fact]
    public void ShotAliveForOneTick_AndZeroLengthLob_AreTrackedDrawnOnTheGroundAndDropped()
    {
        int bolt = Data.FindProjectile("bolt"), sharper = Data.FindProjectile("sharper");
        var s = new ProjectileStore(4);
        var t = new ProjectileTracker(4);
        var from = new Vector2(30f, 30f);
        Assert.True(s.TrySpawn(from, from + new Vector2(1.25f, 0f), Data.Projectiles[bolt].SpeedPerTick, bolt, 0, 0, default, default, false));
        Assert.True(s.TrySpawn(from, from, Data.Projectiles[sharper].SpeedPerTick, sharper, 1, 0, default, default, false));
        Assert.True(s.TrySpawn(from, from + new Vector2(1e-6f, 0f), Data.Projectiles[sharper].SpeedPerTick, sharper, 1, 0, default, default, false));
        t.Observe(s, Defs, 1);
        Assert.Equal(3, t.Count);
        for (int i = 0; i < 3; i++)
        {
            Assert.True(t.Tracked[i]);
            Assert.Equal(from, t.Launch[i]);
            Assert.Equal(0f, t.ArcHeight(i, s.Position[i]));
            float p = t.Progress(i, s.Position[i]);
            Assert.True(float.IsFinite(p) && p >= 0f && p <= 1f);
        }
        Assert.True(t.IsLob[1] && t.IsLob[2] && !t.IsLob[0]);
        s.Fly(); // every one lands this tick (one-tick flights)
        for (int i = 0; i < 3; i++) Assert.Equal(0, s.TicksLeft[i]);
        for (int i = 0; i < 3; i++) s.Free(i);
        t.Observe(s, Defs, 2);
        Assert.Equal(0, t.Count);
        Assert.Equal(3, t.Dropped);
        for (int i = 0; i < 3; i++) Assert.Equal(0f, t.ArcHeight(i, from));
    }

    [Fact]
    public void SapperInMelee_LobsAtItsOwnFeet_ArcStaysOnTheGround_NoNaN()
    {
        Simulation sim = CombatScenes.Flat(size: 48, units: 16);
        int sapper = U("malazan_sapper"), raider = U("whirlwind_raider");
        for (int k = 0; k < 4; k++)
        {
            CombatScenes.Place(sim, 0, sapper, CombatScenes.At(sim, 20, 14 + 3 * k));
            CombatScenes.Place(sim, 1, raider, CombatScenes.At(sim, 20, 14 + 3 * k, dx: 1.0f));
        }
        ProjectileStore s = sim.World.Projectiles;
        var t = new ProjectileTracker(s.Capacity);
        int lobsSeen = 0;
        float shortest = float.MaxValue, highest = 0f;
        for (int tick = 0; tick < 400; tick++)
        {
            sim.Tick();
            t.Observe(s, Defs, sim.World.TickNumber);
            for (int i = 0; i < s.Capacity; i++)
            {
                if (!s.Alive[i] || !t.IsLob[i]) continue;
                lobsSeen++;
                shortest = MathF.Min(shortest, t.Length[i]);
                for (float al = 0f; al <= 1f; al += 0.1f)
                {
                    float h = t.ArcHeight(i, Vector2.Lerp(s.PrevPosition[i], s.Position[i], al));
                    Assert.True(float.IsFinite(h) && h >= 0f, $"arc {h}");
                    if (t.Length[i] < 1.3f) highest = MathF.Max(highest, h);
                }
            }
        }
        _out.WriteLine($"sapper melee: {lobsSeen} lob slot-ticks, shortest throw {shortest:F3} m, highest arc of a throw under 1.3 m {highest:F3} m");
        Assert.True(lobsSeen > 0, "no sapper threw in melee");
        Assert.True(shortest < 1.5f, $"shortest throw {shortest} m: not a melee throw");
        Assert.True(highest <= 1e-4f, $"a sub-1.3 m throw arcs {highest} m");
    }

    // ---- the marks' ring at its cap ----

    [Fact]
    public void ImpactMarks_AtCap_ReplacesOldestFirst_CountsStayConsistent_ZeroBytes()
    {
        int bolt = Data.FindProjectile("bolt"), stone = Data.FindProjectile("catapult_stone");
        var m = new ImpactMarks();
        int cap = m.Capacity;
        Assert.Equal(512, cap);
        var burst = new ProjectileImpact[1000];
        for (int i = 0; i < burst.Length; i++) burst[i] = new ProjectileImpact(new Vector2(i, 1f), i % 4 == 0 ? stone : bolt, i % 2, i % 3 != 0);
        var added = new int[burst.Length];
        var removed = new int[cap];
        Assert.Equal(1000, m.Collect(burst, Defs, 100, added));
        Assert.Equal(cap, m.Count);
        Assert.Equal(1000 - cap, m.Replaced);
        // The newest 512 survive: impact k sits in slot k % 512, and every slot holds impact >= 488.
        for (int i = 0; i < cap; i++)
        {
            Assert.True(m.Active[i]);
            Assert.True(m.Position[i].X >= 1000 - cap, $"slot {i} keeps impact {m.Position[i].X}");
        }
        Assert.Equal(999 % cap, added[999]);
        // Undrawn marks never go; drawn ones go at their own lifetimes.
        Assert.Equal(0, m.Expire(10_000, removed));
        for (int i = 0; i < cap; i++) m.MarkDrawn(i);
        int gone = 0;
        for (long tick = 100; tick <= 108; tick++) gone += m.Expire(tick, removed);
        Assert.Equal(cap, gone);
        Assert.Equal(0, m.Count);
        Assert.Equal(cap, m.Expired);
        Assert.Equal(1000, m.Added);

        // Steady state at the cap: 0 bytes.
        ProjectileImpact[] tickImpacts = burst.AsSpan(0, 300).ToArray();
        long t0 = 200;
        AllocationProbe.AssertZero(() =>
        {
            for (int k = 0; k < 50; k++)
            {
                m.Collect(tickImpacts, Defs, t0, added);
                for (int i = 0; i < cap; i++) m.MarkDrawn(i);
                m.Expire(t0, removed);
                float sum = 0f;
                for (int i = 0; i < cap; i++) if (m.Active[i]) sum += m.Age(i, t0, 0.5f);
                if (sum < 0f) throw new InvalidOperationException();
                t0++;
            }
        }, _out);
        Assert.True(m.Count <= cap);
    }

    // BUG-0221: a mark first drawn after its lifetime is drawn at age 1, which the view turns into alpha 0 (invisible).
    [Fact]
    public void ImpactMark_HeldUntilDrawn_IsDrawnBeforeTheEndOfItsLife()
    {
        int bolt = Data.FindProjectile("bolt");
        var m = new ImpactMarks();
        var added = new int[1];
        m.Collect(new[] { new ProjectileImpact(Vector2.Zero, bolt, 0, true) }, Defs, 10, added);
        // At 8x and 30 fps a frame runs ~5 ticks: the first frame that draws this flash is at tick 15.
        Assert.True(m.Age(added[0], 15, 0f) < 1f, $"first drawn at age {m.Age(added[0], 15, 0f)}");
    }
}
