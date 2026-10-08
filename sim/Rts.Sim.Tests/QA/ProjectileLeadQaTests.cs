using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M4-2b round 1 (session 2026-10-08-0913): attacks on the BUG-0183 lead rule (docs/03 "Leading"). An aimed shot at a
/// unit stepping no faster than its projectile's <c>leadSpeed</c> is aimed where the unit will be and re-led every tick it
/// flies. These rows change the target's motion in flight (stop, reverse, random turns, shove, death and slot reuse),
/// lead into the blocked map ring and around a cliff, and test a unit exactly at the lead speed.
/// </summary>
public class ProjectileLeadQaTests
{
    private readonly ITestOutputHelper _out;

    public ProjectileLeadQaTests(ITestOutputHelper output) => _out = output;

    private static int HorseRaider => TestSim.Data.FindUnit("whirlwind_horse_raider");

    /// <summary>What happened to one shot.</summary>
    private readonly record struct Shot(bool Hit, int Flight, bool StayedOnMap, float TargetMoved);

    /// <summary>
    /// A held Crossbowman (swings blocked) at cell (20, 32) of a <paramref name="size"/>-cell map, and a <paramref name="type"/>
    /// of player 1 walking <paramref name="headingDeg"/> (0 = away from the shooter) that is <paramref name="distance"/> m
    /// east of it after a 12-tick warm-up. One bolt is fired; <paramref name="inFlight"/> runs before every tick of its
    /// flight (tick index from 0). Every tick checks the bolt stays finite and on the map.
    /// </summary>
    private static Shot WalkerShot(GameData data, int type, float distance, float headingDeg,
        Action<Simulation, EntityHandle, int>? inFlight = null, Heightmap? map = null, Vector2? shooterAt = null, float walk = 30f)
    {
        map ??= LocalMovementTests.Flat(64);
        var sim = new Simulation(TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: 16, CommandCapacity: 512) with { Data = data }, map);
        World w = sim.World;
        UnitStore u = w.Units;
        Vector2 from = shooterAt ?? At(sim, 20, 32);
        EntityHandle s = Place(sim, 0, data.FindUnit("malazan_crossbowman"), from);
        u.CooldownTicks[s.Index] = 1_000_000;
        sim.Enqueue(Command.HoldPosition(0, s));
        float rad = headingDeg * MathF.PI / 180f;
        var dir = new Vector2(MathF.Cos(rad), MathF.Sin(rad));
        const int warmup = 12;
        float step = data.Units[type].SpeedPerTick;
        Vector2 atFire = from + new Vector2(distance, 0f);
        Vector2 start = atFire - dir * (step * warmup);
        EntityHandle t = Place(sim, 1, type, start);
        // A goal past the map edge is no order at all (off the map): clamp it into the outer ring, which resolves to the
        // nearest passable cell's center, so the walker heads for the edge.
        float extent = w.NavGrid.Width * MapConstants.CellSize - 0.5f;
        sim.Enqueue(Command.Move(1, t, Vector2.Clamp(start + dir * walk, new Vector2(0.5f), new Vector2(extent))));
        for (int k = 0; k < warmup; k++) sim.Tick();
        Assert.Equal(UnitState.Moving, u.State[t.Index]);
        u.Target[s.Index] = t;
        ProjectileSystem.Fire(w, s.Index);
        u.Target[s.Index] = default;
        Assert.Equal(1, w.Projectiles.Count);
        Vector2 fired = u.Position[t.Index];
        float mapW = w.NavGrid.Width * MapConstants.CellSize, mapH = w.NavGrid.Height * MapConstants.CellSize;
        bool onMap = true;
        int flight = 0;
        while (w.Impacts.Length == 0)
        {
            Assert.True(flight < 400, "the bolt never landed");
            inFlight?.Invoke(sim, t, flight);
            sim.Tick();
            flight++;
            ProjectileStore p = w.Projectiles;
            for (int k = 0; k < p.Capacity; k++)
            {
                if (!p.Alive[k]) continue;
                Vector2 pos = p.Position[k], aim = p.Target[k];
                Assert.True(float.IsFinite(pos.X) && float.IsFinite(pos.Y) && float.IsFinite(aim.X) && float.IsFinite(aim.Y));
                if (pos.X < 0 || pos.Y < 0 || pos.X > mapW || pos.Y > mapH) onMap = false;
            }
        }
        ProjectileImpact hit = w.Impacts[0];
        if (hit.Position.X < 0 || hit.Position.Y < 0 || hit.Position.X > mapW || hit.Position.Y > mapH) onMap = false;
        float moved = u.IsAlive(t) ? Vector2.Distance(fired, u.Position[t.Index]) : -1f;
        return new Shot(hit.Hit, flight, onMap, moved);
    }

    private static GameData WithUnitSpeed(string unit, string faction, double speed)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField(faction, unit, "speed", speed.ToString(System.Globalization.CultureInfo.InvariantCulture));
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        return r.Data!;
    }

    // ---------- the lead-speed boundary ----------

    /// <summary>
    /// docs/03: a unit "no faster than" the lead speed is led. A step of exactly the lead speed (0.25 m a tick) in each of
    /// 360 headings, as float trig makes it: every one must be led, not only the axis-aligned ones.
    /// </summary>
    [Fact(Skip = "BUG-0184: a step of exactly the lead speed fails step^2 <= lead^2 in 13 of 360 headings (float rounding)")]
    public void Lead_AStepOfExactlyTheLeadSpeed_IsLedInEveryHeading()
    {
        ProjectileDef bolt = TestSim.Data.Projectiles[TestSim.Data.FindProjectile("bolt")];
        float lead = bolt.LeadSpeedPerTick;
        Vector2 from = Vector2.Zero, at = new(12f, 0f);
        int notLed = 0;
        var which = new List<int>();
        for (int deg = 0; deg < 360; deg++)
        {
            float r = deg * MathF.PI / 180f;
            Vector2 v = lead * new Vector2(MathF.Cos(r), MathF.Sin(r));
            if (ProjectileSystem.Lead(from, at, v, bolt) == at) { notLed++; if (which.Count < 12) which.Add(deg); }
        }
        _out.WriteLine($"exactly {lead} m a tick: {notLed} of 360 headings not led (first: {string.Join(", ", which)})");
        Assert.True(notLed == 0, $"{notLed} of 360 headings at exactly the lead speed are not led: {string.Join(", ", which)}");
    }

    /// <summary>
    /// The same boundary in play: a Heavy Infantry given exactly 5 m/s (data copy) walking in 100 headings at 12 m is led
    /// and hit at least 95 %; at 5.2 m/s (just over) crossing it is not led and dodges most shots.
    /// </summary>
    [Fact]
    public void AWalkerAtExactlyTheLeadSpeed_IsLed_JustOverItIsNot()
    {
        GameData at5 = WithUnitSpeed("malazan_heavy_infantry", "malazan", 5.0);
        int hi = at5.FindUnit("malazan_heavy_infantry");
        int hits = 0;
        var missed = new List<float>();
        for (int k = 0; k < 100; k++)
        {
            float h = k * 3.6f;
            if (WalkerShot(at5, hi, 12f, h).Hit) hits++; else if (missed.Count < 10) missed.Add(h);
        }
        GameData over = WithUnitSpeed("malazan_heavy_infantry", "malazan", 5.2);
        int overHits = 0;
        for (int k = 0; k < 40; k++)
            if (WalkerShot(over, hi, 12f, (k % 2 == 0 ? 90f : 270f) + (k % 5 - 2) * 5f).Hit) overHits++;
        _out.WriteLine($"5.0 m/s walker at 12 m: {hits} / 100 hit (missed headings: {string.Join(", ", missed)}); 5.2 m/s crossing: {overHits} / 40");
        Assert.True(hits >= 95, $"5.0 m/s: {hits} / 100; missed headings {string.Join(", ", missed)}");
        Assert.True(overHits < 20, $"5.2 m/s crossing: {overHits} / 40");
    }

    /// <summary>
    /// Report row for the boundary: a 5.0 m/s walker (data copy) on open ground in 360 headings, 20 ticks each; counts the
    /// ticks whose real step fails the lead test (<c>step^2 &lt;= lead^2</c>) though the unit walks exactly at the lead speed.
    /// </summary>
    [Fact]
    public void Report_RealStepsOfAFiveMpsWalker_AgainstTheLeadTest()
    {
        GameData at5 = WithUnitSpeed("malazan_heavy_infantry", "malazan", 5.0);
        int hi = at5.FindUnit("malazan_heavy_infantry");
        float lead = at5.Projectiles[at5.FindProjectile("bolt")].LeadSpeedPerTick;
        int steps = 0, over = 0;
        float longest = 0f;
        for (int deg = 0; deg < 360; deg += 3)
        {
            var sim = new Simulation(TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: 4, CommandCapacity: 64) with { Data = at5 }, LocalMovementTests.Flat(64));
            float r = deg * MathF.PI / 180f;
            Vector2 start = At(sim, 32, 32);
            EntityHandle t = Place(sim, 1, hi, start);
            sim.Enqueue(Command.Move(1, t, start + 25f * new Vector2(MathF.Cos(r), MathF.Sin(r))));
            for (int k = 0; k < 20; k++)
            {
                sim.Tick();
                Vector2 v = sim.World.Units.Velocity[t.Index];
                if (v == Vector2.Zero) continue;
                steps++;
                longest = MathF.Max(longest, v.Length());
                if (!(v.LengthSquared() <= lead * lead)) over++;
            }
        }
        _out.WriteLine($"5.0 m/s walker: {over} of {steps} steps fail the lead test (longest step {longest:R} m, lead {lead:R} m)");
        Assert.True(steps > 1000);
    }

    // ---------- the target changes its motion in flight ----------

    /// <summary>A walking Heavy Infantry ordered to Stop on each tick of the flight in turn (the landing tick too): every shot hits.</summary>
    [Theory]
    [InlineData(8f)]
    [InlineData(16f)]
    public void AWalkerStoppedOnAnyTickOfTheFlight_IsHit(float distance)
    {
        int flight = WalkerShot(TestSim.Data, HeavyInfantry, distance, 90f).Flight;
        var missed = new List<int>();
        for (int stopAt = 0; stopAt < flight; stopAt++)
        {
            int when = stopAt;
            Shot s = WalkerShot(TestSim.Data, HeavyInfantry, distance, 90f, (sim, t, k) =>
            {
                if (k == when) sim.Enqueue(Command.Stop(1, t));
            });
            if (!s.Hit) missed.Add(stopAt);
        }
        _out.WriteLine($"{distance} m, {flight}-tick flight: stopped on tick k missed for k = [{string.Join(", ", missed)}]");
        Assert.Empty(missed);
    }

    /// <summary>A walker that reverses every tick (Move alternately 20 m ahead and behind): its step flips each tick, so the lead swings 2 x step x ticks left; it must still be hit.</summary>
    [Theory]
    [InlineData(8f, 90f)]
    [InlineData(12f, 90f)]
    [InlineData(16f, 90f)]
    [InlineData(12f, 0f)]
    [InlineData(12f, 180f)]
    public void AWalkerThatReversesEveryTick_IsHit(float distance, float heading)
    {
        float rad = heading * MathF.PI / 180f;
        var dir = new Vector2(MathF.Cos(rad), MathF.Sin(rad));
        Shot s = WalkerShot(TestSim.Data, HeavyInfantry, distance, heading, (sim, t, k) =>
        {
            Vector2 p = sim.World.Units.Position[t.Index];
            sim.Enqueue(Command.Move(1, t, p + dir * (k % 2 == 0 ? -20f : 20f)));
        });
        _out.WriteLine($"{distance} m heading {heading}: flight {s.Flight}, hit {s.Hit}, moved {s.TargetMoved:F2} m");
        Assert.True(s.Hit);
    }

    /// <summary>A walker given a new random heading every 2 ticks of the flight (seeded): 60 shots at 10-16 m, hit at least 95 %.</summary>
    [Fact]
    public void AWalkerThatTurnsAtRandomInFlight_IsHitAtLeast95Percent()
    {
        var rng = new SimRng(7, 1183);
        int hits = 0;
        for (int n = 0; n < 60; n++)
        {
            float distance = 10f + rng.NextFloat() * 6f, heading = rng.NextFloat() * 360f;
            ulong turnSeed = (ulong)rng.NextInt(1, 1_000_000);
            var turns = new SimRng(turnSeed, 3);
            if (WalkerShot(TestSim.Data, HeavyInfantry, distance, heading, (sim, t, k) =>
                {
                    if (k % 2 != 0) return;
                    float r = turns.NextFloat() * 2f * MathF.PI;
                    sim.Enqueue(Command.Move(1, t, sim.World.Units.Position[t.Index] + 10f * new Vector2(MathF.Cos(r), MathF.Sin(r))));
                }).Hit) hits++;
        }
        _out.WriteLine($"random turns every 2 ticks: {hits} / 60");
        Assert.True(hits >= 57, $"{hits} / 60");
    }

    /// <summary>
    /// A galloping Horse Raider (not led at firing) ordered to Stop in flight: from then on it steps 0, under the lead
    /// speed, so the bolt is re-led onto it (docs/03: "a unit stepping faster than leadSpeed that tick" is the only
    /// exemption). Pinned: a horseman who pulls up is hit; one who keeps galloping dodges.
    /// </summary>
    [Fact]
    public void AGallopingHorseRaider_ThatPullsUpInFlight_IsHit_OneThatKeepsGalloping_Dodges()
    {
        Shot galloping = WalkerShot(TestSim.Data, HorseRaider, 12f, 90f);
        Shot pulledUp = WalkerShot(TestSim.Data, HorseRaider, 12f, 90f, (sim, t, k) =>
        {
            if (k == 3) sim.Enqueue(Command.Stop(1, t));
        });
        _out.WriteLine($"Horse Raider crossing at 12 m: galloping hit {galloping.Hit}; pulled up on tick 3 of {pulledUp.Flight}: hit {pulledUp.Hit}");
        Assert.False(galloping.Hit);
        Assert.True(pulledUp.Hit);
    }

    // ---------- shoved, killed, slot reused ----------

    /// <summary>
    /// An Idle Heavy Infantry 12 m off, shoved through the flight by a file of friendly walkers pushing past it: its
    /// Velocity stays 0 (a shove is not a step), so the bolt is re-aimed at where it stands each tick, and hits.
    /// </summary>
    [Fact]
    public void AShovedTarget_IsHit()
    {
        var sim = new Simulation(TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: 32, CommandCapacity: 512), LocalMovementTests.Flat(64));
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle s = Place(sim, 0, Crossbowman, At(sim, 20, 32));
        u.CooldownTicks[s.Index] = 1_000_000;
        sim.Enqueue(Command.HoldPosition(0, s));
        Vector2 spot = At(sim, 20, 32) + new Vector2(12f, 0f);
        EntityHandle t = Place(sim, 1, HeavyInfantry, spot);
        var file = new List<EntityHandle>();
        for (int k = 0; k < 6; k++)
        {
            EntityHandle f = Place(sim, 1, HeavyInfantry, spot + new Vector2(0.3f, -1.0f - 0.85f * k));
            file.Add(f);
            sim.Enqueue(Command.Move(1, f, spot + new Vector2(0.3f, 20f)));
        }
        RunUntil(sim, () => Vector2.Distance(u.Position[file[0].Index], spot) < 1.2f, 40);
        Assert.Equal(UnitState.Idle, u.State[t.Index]);
        Vector2 before = u.Position[t.Index];
        u.Target[s.Index] = t;
        ProjectileSystem.Fire(w, s.Index);
        u.Target[s.Index] = default;
        float maxVel = 0f;
        int ran = RunUntil(sim, () =>
        {
            maxVel = MathF.Max(maxVel, u.Velocity[t.Index].Length());
            return w.Impacts.Length > 0;
        }, 60);
        float shoved = Vector2.Distance(before, u.Position[t.Index]);
        _out.WriteLine($"shoved {shoved:F3} m over a {ran}-tick flight (target velocity max {maxVel}); hit {w.Impacts[0].Hit}");
        Assert.True(shoved > 0.05f, $"setup: the target was shoved only {shoved:F3} m");
        Assert.True(w.Impacts[0].Hit);
    }

    /// <summary>
    /// The target dies in flight and a new enemy walker takes its slot (a new generation) 6 m away: the bolt is not
    /// re-led after the death (its impact point stays put), does not follow the new unit, misses, and hurts nobody.
    /// </summary>
    [Fact]
    public void ATargetKilledInFlight_TheBoltKeepsItsCourse_AndIgnoresTheSlotsNewUnit()
    {
        Simulation sim = Flat(size: 64, units: 8);
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle s = Place(sim, 0, Crossbowman, At(sim, 20, 32));
        u.CooldownTicks[s.Index] = 1_000_000;
        sim.Enqueue(Command.HoldPosition(0, s));
        EntityHandle t = Place(sim, 1, HeavyInfantry, At(sim, 26, 28));
        sim.Enqueue(Command.Move(1, t, At(sim, 26, 50)));
        for (int k = 0; k < 10; k++) sim.Tick();
        u.Target[s.Index] = t;
        ProjectileSystem.Fire(w, s.Index);
        u.Target[s.Index] = default;
        for (int k = 0; k < 3; k++) sim.Tick();
        Vector2 aim = w.Projectiles.Target[0];
        u.Free(t);
        EntityHandle reborn = Place(sim, 1, HeavyInfantry, aim + new Vector2(0f, 6f));
        Assert.Equal(t.Index, reborn.Index);
        Assert.NotEqual(t.Generation, reborn.Generation);
        sim.Enqueue(Command.Move(1, reborn, aim + new Vector2(0f, -20f))); // walks through the impact point
        int full = u.Hp[reborn.Index];
        while (w.Impacts.Length == 0)
        {
            sim.Tick();
            if (w.Projectiles.Count > 0) Assert.Equal(aim, w.Projectiles.Target[0]);
        }
        _out.WriteLine($"impact at {w.Impacts[0].Position}, hit {w.Impacts[0].Hit}; the new unit {Vector2.Distance(u.Position[reborn.Index], aim):F2} m off");
        Assert.Equal(aim, w.Impacts[0].Position);
        Assert.False(w.Impacts[0].Hit);
        Assert.Equal(full, u.Hp[reborn.Index]);
    }

    // ---------- leading into blocked ground ----------

    /// <summary>
    /// A walker heading into the map's blocked ring (and into the corner): the lead points past the ring; the walker
    /// arrives at its goal against the ring and stops; the bolt is re-led onto it, hits, and never leaves the map.
    /// </summary>
    [Theory]
    [InlineData("malazan_heavy_infantry", 0f)]   // away from the shooter, into the east ring
    [InlineData("malazan_heavy_infantry", 45f)]  // into the north-east corner
    [InlineData("malazan_heavy_infantry", 90f)]  // across, into the north ring
    [InlineData("whirlwind_zealot", 0f)]         // the fastest led foot unit (4.4 m/s): its lead reaches past the ring
    [InlineData("whirlwind_zealot", 45f)]
    [InlineData("whirlwind_zealot", -45f)]       // into the south-east corner
    public void AWalkerIntoTheMapRing_IsHit_AndTheBoltStaysOnTheMap(string unit, float heading)
    {
        // A 32-cell map (64 m); the ring (cells 0 and 31) is blocked, so a center can reach 62 m less its radius. At firing
        // the walker is 1.2-2.4 m short of its goal (61 m, the last passable cell center) along its heading, 12-16 m east of the shooter.
        Heightmap map = LocalMovementTests.Flat(32);
        // Its goal (40 m on, in the ring) resolves to the nearest passable cell's center: 61 m (3 m on the south side).
        int type = TestSim.Data.FindUnit(unit);
        const float goal = 61f;
        var hits = new List<bool>();
        bool onMap = true;
        for (int v = 0; v < 5; v++)
        {
            float shortOf = 1.2f + v * 0.3f, distance = 12f + v;
            float x = heading == 90f ? 40f : goal - shortOf;
            float y = heading == 0f ? 32f : heading > 0f ? goal - shortOf : 64f - goal + shortOf;
            var atFire = new Vector2(x, y);
            Shot s = WalkerShot(TestSim.Data, type, distance, heading, map: map, shooterAt: atFire - new Vector2(distance, 0f), walk: 40f);
            hits.Add(s.Hit);
            onMap &= s.StayedOnMap;
        }
        _out.WriteLine($"{unit} heading {heading}: hits [{string.Join(", ", hits)}], bolt stayed on the map {onMap}");
        Assert.All(hits, Assert.True);
        Assert.True(onMap, "a led bolt left the map");
    }

    /// <summary>
    /// A walker whose path bends around a cliff block (blocked cells): its walk hugs and turns at the block's corners, and
    /// its lead points into the cliff while it walks at it. 24 shots in a fan of headings: hit at least 22.
    /// </summary>
    [Fact]
    public void AWalkerBendingAroundACliff_IsHit()
    {
        var rows = new string[64];
        for (int y = 0; y < 64; y++)
        {
            char[] row = new string('0', 64).ToCharArray();
            if (y >= 28 && y <= 36)
                for (int x = 30; x <= 33; x++) row[x] = '1';
            rows[y] = new string(row);
        }
        Heightmap map = LocalMovementTests.Rows(rows);
        int hits = 0;
        var missed = new List<float>();
        for (int k = 0; k < 24; k++)
        {
            // The target starts 12-15 m east of the shooter at (20, 32) and walks east, into and around the block at x 60-68 m.
            float heading = -30f + k * 2.5f;
            Shot s = WalkerShot(TestSim.Data, HeavyInfantry, 13f + (k % 4) * 0.7f, heading, map: map, walk: 40f);
            if (s.Hit) hits++; else missed.Add(heading);
        }
        _out.WriteLine($"around a cliff: {hits} / 24 (missed headings {string.Join(", ", missed)})");
        Assert.True(hits >= 22, $"{hits} / 24; missed {string.Join(", ", missed)}");
    }
}
