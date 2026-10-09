using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>
/// QA (M4-VH1, BUG-0222 item 1): <see cref="ProjectileTracker"/>'s new lob "off the launch line" test under hostile slot
/// reuse: a few slots, every shot a lob of one owner and type at one of two impact points (so type, owner and target never
/// change on reuse), launches random or on the old shot's own line (ahead of and behind its launch), observers that skip
/// ticks at random. An every-tick observer must never restart a flight (no false "off line"), also at far map
/// coordinates where float steps drift most; a skipping observer's launch must lie on the live shot's line, at or ahead
/// of its real launch and not past it.
/// </summary>
public class LobReuseHostileQaTests
{
    private readonly ITestOutputHelper _out;

    public LobReuseHostileQaTests(ITestOutputHelper output) => _out = output;

    private static GameData Data => TestSim.Data;
    private static ReadOnlySpan<ProjectileDef> Defs => Data.Projectiles.AsSpan();

    // A fuzzed store: `slots` slots, lobs of `type` at `targets`; returns (every-tick reused, skip stale count, lob observations).
    private (long everyReused, int stale, int observed, int restarts, string firstStale) Run(int seed, int slots, Vector2 origin, bool collinear, double observeChance)
    {
        int type = Data.FindProjectile("sharper");
        float speed = Data.Projectiles[type].SpeedPerTick;
        var s = new ProjectileStore(slots);
        var every = new ProjectileTracker(slots);
        var skip = new ProjectileTracker(slots);
        var trueLaunch = new Vector2[slots];
        var freedNow = new bool[slots];
        var rnd = new Random(seed);
        Vector2[] targets = { origin + new Vector2(30f, 20f), origin + new Vector2(30f, 20.5f) };
        int stale = 0, observed = 0;
        string first = "";
        long lastSkipStarted = 0;
        int restarts = 0;
        for (long tick = 1; tick < 4000; tick++)
        {
            s.Fly();
            Array.Clear(freedNow);
            for (int i = 0; i < slots; i++)
                if (s.Alive[i] && s.TicksLeft[i] <= 0) { s.Free(i); freedNow[i] = true; }
            // Up to two spawns a tick into slots free since an earlier tick (the sim never refills a slot on the tick it lands,
            // so an every-tick observer sees every slot dead in between; ProjectileViewQaTests' ground-truth row).
            for (int n = rnd.Next(3); n > 0; n--)
            {
                Vector2 to = targets[rnd.Next(2)];
                Vector2 from;
                if (collinear && rnd.Next(2) == 0)
                {
                    // Every collinear shot on one line into its target (due west of it), so a reuse lands ahead of or behind the old launch.
                    from = new Vector2(to.X - (1f + 22f * (float)rnd.NextDouble()), to.Y);
                }
                else
                {
                    from = to + new Vector2(-24f + 22f * (float)rnd.NextDouble(), -12f + 24f * (float)rnd.NextDouble());
                }
                int free = -1;
                for (int i = 0; i < slots && free < 0; i++) if (!s.Alive[i] && !freedNow[i]) free = i;
                if (free < 0) break;
                // TrySpawn takes the lowest free slot: park a placeholder in each lower slot freed this tick, free it after.
                int parked = 0;
                for (int i = 0; i < free; i++)
                    if (freedNow[i] && !s.Alive[i]) { Assert.True(s.TrySpawn(to, to, speed, type, 0, 0, default, default, false)); parked |= 1 << i; }
                Assert.True(s.TrySpawn(from, to, speed, type, 0, 0, default, default, false));
                Assert.True(s.Alive[free] && s.Position[free] == from, $"spawned into the wrong slot (want {free})");
                for (int i = 0; i < free; i++) if ((parked & (1 << i)) != 0) s.Free(i);
                trueLaunch[free] = from;
            }
            every.Observe(s, Defs, tick);
            for (int i = 0; i < slots; i++)
                if (s.Alive[i]) Assert.True(every.Launch[i] == trueLaunch[i], $"seed {seed} tick {tick} slot {i}: every-tick launch {every.Launch[i]}, truth {trueLaunch[i]}");
            if (rnd.NextDouble() >= observeChance) continue;
            skip.Observe(s, Defs, tick);
            restarts += (int)(skip.Started - lastSkipStarted);
            lastSkipStarted = skip.Started;
            for (int i = 0; i < slots; i++)
            {
                if (!s.Alive[i]) continue;
                observed++;
                Vector2 l0 = trueLaunch[i], dir = s.Target[i] - l0, rel = skip.Launch[i] - l0;
                float len2 = dir.LengthSquared();
                float cross = len2 > 1e-8f ? MathF.Abs(dir.X * rel.Y - dir.Y * rel.X) / MathF.Sqrt(len2) : rel.Length();
                float along = len2 > 1e-8f ? Vector2.Dot(rel, dir) / len2 : 0f;
                float pastPos = Vector2.Distance(skip.Launch[i], l0) - Vector2.Distance(s.Position[i], l0);
                if (cross > 1e-2f || along < -1e-3f || pastPos > 1e-3f)
                {
                    if (stale == 0) first = $"tick {tick} slot {i}: true launch {l0}, tracked {skip.Launch[i]}, pos {s.Position[i]}, target {s.Target[i]} (cross {cross:F3}, along {along:F3})";
                    stale++;
                }
            }
        }
        return (every.Reused, stale, observed, restarts, first);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    public void RandomLaunches_SameOwnerTypeTarget_SkippingObserver_NeverDrawsAnotherShotsLine(int seed, bool collinear)
    {
        var r = Run(seed, 3, new Vector2(40f, 40f), collinear, 0.3);
        _out.WriteLine($"seed {seed}: every-tick reused {r.everyReused}; skipping: {r.observed} lob observations, {r.restarts} starts, {r.stale} stale {r.firstStale}");
        Assert.Equal(0L, r.everyReused);
        Assert.True(r.observed > 1000, $"only {r.observed} observations");
        Assert.True(r.stale == 0, $"seed {seed}: {r.stale} observations draw another shot's line; first {r.firstStale}");
    }

    // Collinear reuse (a shot from the same line, ahead of or behind the old launch): off-line can't see it. Behind is
    // harmless (the old launch lies on the new flight, as after any mid-flight start); ahead puts the arc's start before
    // the real launch. Measured, not asserted: unreachable in the Match, which observes every tick.
    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void CollinearReuse_SkippingObserver_Measured(int seed)
    {
        var r = Run(seed, 3, new Vector2(40f, 40f), true, 0.3);
        _out.WriteLine($"seed {seed} collinear: every-tick reused {r.everyReused}; skipping: {r.observed} lob observations, {r.restarts} starts, {r.stale} with the arc's start before the real launch; first {r.firstStale}");
        Assert.Equal(0L, r.everyReused);
    }

    // Far map coordinates (a 512-cell map is 1024 m wide), where launch + j x step drifts most: an every-tick observer
    // must never call a real flight off its line.
    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    public void FarCoordinates_EveryTickObserver_NeverRestartsARealFlight(int seed)
    {
        var r = Run(seed, 4, new Vector2(990f, 990f), false, 0.5);
        _out.WriteLine($"seed {seed} at (1020, 1010): every-tick reused {r.everyReused}; skipping stale {r.stale} of {r.observed}");
        Assert.Equal(0L, r.everyReused);
        Assert.True(r.stale == 0, r.firstStale);
    }
}
