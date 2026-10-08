using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>QA stress on M1-4d-1 local movement: crowds, walls and gaps, settle bounds, jitter, determinism, perf.</summary>
public class LocalMovementStressTests
{
    private readonly ITestOutputHelper _out;

    public LocalMovementStressTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// Per-tick invariants (as MovementStressTests) plus: an Idle unit that was Idle last tick has not
    /// moved, unless some unit walked this tick (since M1-4d-2 walkers shove friendly Idle units aside,
    /// at most their speed per tick, which the speed check covers). Once nothing moves, nothing jitters.
    /// </summary>
    internal static string? CheckInvariants(World w, Vector2[] lastPos, bool[] lastIdle)
    {
        UnitStore u = w.Units;
        NavGrid g = w.NavGrid;
        bool anyWalker = false;
        for (int i = 0; i < u.Capacity && !anyWalker; i++)
            if (u.Alive[i] && (!lastIdle[i] || u.State[i] == UnitState.Moving)) anyWalker = true;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            Vector2 p = u.Position[i];
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y)) return $"unit {i} non-finite position {p}";
            if (!g.WorldToCell(p, out int x, out int y)) return $"unit {i} off map at {p}";
            if (!g.IsPassable(x, y)) return $"unit {i} inside blocked cell ({x},{y}) at {p}";
            float moved = Vector2.Distance(p, u.PrevPosition[i]);
            if (moved > u.Speed[i] * 1.0001f + 1e-5f) return $"unit {i} moved {moved} m > speed {u.Speed[i]}";
            if (u.State[i] == UnitState.Idle && u.Velocity[i] != Vector2.Zero) return $"unit {i} idle with velocity {u.Velocity[i]}";
            if (u.State[i] == UnitState.Moving)
            {
                int gc = u.GoalCell[i];
                if (gc < 0 || !g.IsPassable(gc % g.Width, gc / g.Width)) return $"unit {i} moving to blocked goal cell {gc}";
            }
            if (!float.IsFinite(u.Facing[i])) return $"unit {i} facing {u.Facing[i]}";
            if (u.StuckTicks[i] < 0 || u.StuckTicks[i] >= MovementConstants.GiveUpTicks) return $"unit {i} stuck counter {u.StuckTicks[i]}";
            if (float.IsNaN(u.BestRemaining[i])) return $"unit {i} best remaining NaN";
            if (!anyWalker && lastIdle[i] && u.State[i] == UnitState.Idle && p != lastPos[i]) return $"idle unit {i} jittered {lastPos[i]} -> {p} with no unit walking";
            lastPos[i] = p;
            lastIdle[i] = u.State[i] == UnitState.Idle;
        }
        return null;
    }

    private static int CountMoving(UnitStore u)
    {
        int n = 0;
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.State[i] == UnitState.Moving) n++;
        return n;
    }

    // ---------- crowds ----------

    /// <summary>
    /// QA focus "Crowds": every unit to one point, or to 4 points 3 cells (6 m) apart. Invariants
    /// every tick; all Idle within the limit; at least <paramref name="minArrivedPercent"/> arrived;
    /// report settle ticks, arrivals and give-ups.
    /// </summary>
    /// <remarks>
    /// M1-4d-3 (BUG-0039): swept over plain seeds, each bound holding on every swept seed (the
    /// worst seed less a little headroom), no longer one pre-M1-6 map. One point: one player (two
    /// players at one point are enemies contesting it, BUG-0037). Four points: one player per point,
    /// neighboring points the enemy's (<see cref="CrowdRows.PointOf"/>; the old slot % 4 split sent both
    /// players to every point, see <see cref="Crowd_ToFourPoints_BothPlayersAtEveryPoint_Report"/>).
    /// Measured over seeds 1-10 (docs/03 "Implementation (M1-4d-3)"): 500 to 4 points 230-280
    /// arrived, 2,500 to 4 points 699-966.
    /// </remarks>
    [Theory]
    [InlineData(500, 1, 3000, 99, 1UL)]
    [InlineData(500, 1, 3000, 99, 2UL)]
    [InlineData(500, 1, 3000, 99, 3UL)]
    [InlineData(2500, 1, 6000, 99, 1UL)]
    [InlineData(500, 4, 3000, MinArrived500To4, 1UL)]
    [InlineData(500, 4, 3000, MinArrived500To4, 2UL)]
    [InlineData(500, 4, 3000, MinArrived500To4, 3UL)]
    [InlineData(500, 4, 3000, MinArrived500To4, 4UL)]
    [InlineData(500, 4, 3000, MinArrived500To4, 5UL)]
    [InlineData(500, 4, 3000, MinArrived500To4, 6UL)]
    [InlineData(500, 4, 3000, MinArrived500To4, 7UL)]
    [InlineData(500, 4, 3000, MinArrived500To4, 8UL)]
    [InlineData(500, 4, 3000, MinArrived500To4, 9UL)]
    [InlineData(500, 4, 3000, MinArrived500To4, 10UL)]
    [InlineData(2500, 4, 6000, MinArrived2500To4, 1UL)]
    [InlineData(2500, 4, 6000, MinArrived2500To4, 2UL)]
    [InlineData(2500, 4, 6000, MinArrived2500To4, 3UL)]
    [InlineData(2500, 4, 6000, MinArrived2500To4, 4UL)]
    [InlineData(2500, 4, 6000, MinArrived2500To4, 5UL)]
    [InlineData(2500, 4, 6000, MinArrived2500To4, 6UL)]
    [InlineData(2500, 4, 6000, MinArrived2500To4, 7UL)]
    [InlineData(2500, 4, 6000, MinArrived2500To4, 8UL)]
    [InlineData(2500, 4, 6000, MinArrived2500To4, 9UL)]
    [InlineData(2500, 4, 6000, MinArrived2500To4, 10UL)]
    public void Crowd_ToOneOrFourClosePoints_InvariantsEveryTick_AllSettle(int units, int points, int limit, int minArrivedPercent, ulong seed)
    {
        Simulation sim = MoveScenario.Spawn(seed, units, units > 1000 ? 70f : 40f, out int goalCell, players: points == 1 ? 1 : 2, combat: false);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        Vector2 c = MoveScenario.Center(g, goalCell);
        Vector2[] four = CrowdRows.FourPoints(c);
        UnitStore u = w.Units;
        var goalOf = new Vector2[u.Capacity];
        for (int i = 0; i < u.Capacity; i++)
        {
            goalOf[i] = points == 1 ? c : four[CrowdRows.PointOf(u, i, true)];
            sim.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), goalOf[i]));
        }
        sim.Tick();
        var lastPos = new Vector2[u.Capacity];
        var lastIdle = new bool[u.Capacity];
        int ticks = 0;
        do
        {
            sim.Tick();
            ticks++;
            string? err = CheckInvariants(w, lastPos, lastIdle);
            Assert.True(err == null, $"tick {ticks}: {err}");
        } while (CountMoving(u) > 0 && ticks < limit);
        bool[] arrived = MoveScenario.Arrived(w);
        int arrivedCount = 0, gaveUp = 0;
        for (int i = 0; i < u.Capacity; i++) { if (arrived[i]) arrivedCount++; else if (u.GoalCell[i] == -1) gaveUp++; }
        // Jitter after settling: 100 more ticks, nothing may move.
        for (int t = 0; t < 100; t++)
        {
            sim.Tick();
            string? err = CheckInvariants(w, lastPos, lastIdle);
            Assert.True(err == null, $"post-settle tick {t}: {err}");
        }
        _out.WriteLine($"seed {seed}: {units} units to {points} point(s): {CountMoving(u)} moving after {ticks} ticks; arrived {arrivedCount}, gave up {gaveUp}, other {units - arrivedCount - gaveUp}; pack: {MoveScenario.FirstPackViolation(w) ?? "ok"}");
        // Where the give-ups stopped (distance to their own order's point) and what the stragglers do.
        var buckets = new int[5]; // <2, <4, <8, <16, more (m)
        for (int i = 0; i < u.Capacity; i++)
        {
            if (arrived[i] || u.GoalCell[i] != -1) continue;
            float d = Vector2.Distance(u.Position[i], goalOf[i]);
            buckets[d < 2f ? 0 : d < 4f ? 1 : d < 8f ? 2 : d < 16f ? 3 : 4]++;
        }
        _out.WriteLine($"  given-up distance to own point: <2 m {buckets[0]}, <4 m {buckets[1]}, <8 m {buckets[2]}, <16 m {buckets[3]}, more {buckets[4]}");
        Assert.True(CountMoving(u) == 0, $"{CountMoving(u)} units still Moving after {ticks} ticks");
        Assert.True(arrivedCount * 100 >= minArrivedPercent * units, $"{arrivedCount} of {units} arrived (< {minArrivedPercent}%)");
    }

    /// <summary>Lowest arrival share (percent) on every swept seed, 500 units to 4 points (worst measured 230 = 46%).</summary>
    private const int MinArrived500To4 = 40;

    /// <summary>Lowest arrival share (percent) on every swept seed, 2,500 units to 4 points (worst measured 699 = 28%).</summary>
    private const int MinArrived2500To4 = 22;

    /// <summary>
    /// Report row: the pre-M1-4d-3 split (slot % 4), which sends both players to every point. Since
    /// BUG-0037 every point is contested by enemies, so far fewer arrive; printed for docs/03, asserts
    /// only that everything stops.
    /// </summary>
    [Theory]
    [InlineData(500, 3000)]
    [InlineData(2500, 6000)]
    public void Crowd_ToFourPoints_BothPlayersAtEveryPoint_Report(int units, int limit)
    {
        for (ulong seed = 1; seed <= 3; seed++)
        {
            CrowdRows.Result r = CrowdRows.ToFourPoints(seed, units, limit, onePlayerPerPoint: false);
            _out.WriteLine($"seed {seed}: {units} units, both players at every point: arrived {r.Arrived}, gave up {r.GaveUp} after {r.Ticks} ticks");
            Assert.Equal(0, r.StillMoving);
        }
    }

    // ---------- walls and gaps ----------

    /// <summary>A 40 x 24 map: a cliff wall at x = 20 with a 1-cell gap (row 6) and a 3-cell gap (rows 15-17).</summary>
    private static Heightmap GappedWall()
    {
        var rows = new string[24];
        for (int y = 0; y < 24; y++)
        {
            char[] r = new string('0', 40).ToCharArray();
            bool gap = y == 6 || (y >= 15 && y <= 17);
            if (!gap) r[20] = '1';
            rows[y] = new string(r);
        }
        return LocalMovementTests.Rows(rows);
    }

    /// <summary>
    /// QA focus "Walls": 100 seeds x 500 ticks; 60 mixed units start west of the wall and are sent in
    /// groups to random points east of it (and some back), so they funnel through the gaps.
    /// Separation must never push anyone into a cliff cell or off the map.
    /// </summary>
    [Fact]
    public void Fuzz_GroupsFunneledThroughGaps_100Seeds_InvariantsEveryTick()
    {
        var sw = Stopwatch.StartNew();
        long moving = 0, gaveUp = 0, total = 0;
        for (int s = 0; s < 100; s++)
        {
            var sim = new Simulation(TestSim.Config(Seed: (ulong)(s + 1), PlayerCount: 2, UnitCapacity: 60, CommandCapacity: 512), GappedWall());
            NavGrid g = sim.World.NavGrid;
            Assert.True(g.IsPassable(20, 6) && g.IsPassable(20, 16) && !g.IsPassable(20, 10));
            var rng = new SimRng((ulong)s, 77);
            for (int i = 0; i < 60; i++)
            {
                var at = new Vector2(2.1f + rng.NextFloat() * 34f, 2.1f + rng.NextFloat() * 43.8f);
                sim.Enqueue(Command.SpawnUnit(i % 2, rng.NextInt(0, TestSim.UnitTypeCount), at));
            }
            sim.Tick();
            sim.Tick();
            UnitStore u = sim.World.Units;
            var lastPos = new Vector2[u.Capacity];
            var lastIdle = new bool[u.Capacity];
            for (int t = 0; t < 500; t++)
            {
                if (t % 150 == 0)
                {
                    bool east = (t / 150) % 2 == 0;
                    var targets = new Vector2[3];
                    for (int k = 0; k < 3; k++)
                        targets[k] = new Vector2((east ? 44f : 4f) + rng.NextFloat() * 30f, 4f + rng.NextFloat() * 40f);
                    for (int i = 0; i < u.Capacity; i++)
                        if (u.Alive[i]) sim.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), targets[rng.NextInt(0, 3)]));
                }
                sim.Tick();
                string? err = CheckInvariants(sim.World, lastPos, lastIdle);
                Assert.True(err == null, $"seed {s} tick {t}: {err}");
            }
            moving += CountMoving(u);
            for (int i = 0; i < u.Capacity; i++) if (u.State[i] == UnitState.Idle && u.GoalCell[i] == -1) gaveUp++;
            total += u.Capacity;
        }
        _out.WriteLine($"100 seeds x 500 ticks x 60 units through gaps: {sw.Elapsed.TotalSeconds:F1} s; at the end {moving} moving, {gaveUp} given up of {total}");
    }

    // ---------- determinism ----------

    /// <summary>
    /// QA focus: two-pass claim. Same units, spawned in reverse order: the set of final positions
    /// must match within 1e-3 m (report the drift; not bit-exact since summation order differs).
    /// </summary>
    [Fact]
    public void ReversedSpawnOrder_SameFinalPositionSet_WithinTolerance_Report()
    {
        (Vector2 At, int Type)[] Positions(bool reversed)
        {
            Simulation probe = MoveScenario.Spawn(seed: 33, units: 120, maxCost: 25f, out int goalCell);
            UnitStore pu = probe.World.Units;
            var spawns = new List<(Vector2, int)>();
            for (int i = 0; i < pu.Capacity; i++) spawns.Add((pu.Position[i], pu.TypeId[i]));
            if (reversed) spawns.Reverse();
            var sim = new Simulation(TestSim.Config(Seed: 33, PlayerCount: 2, UnitCapacity: 120, CommandCapacity: 248));
            foreach ((Vector2 at, int type) in spawns) sim.Enqueue(Command.SpawnUnit(0, type, at));
            sim.Tick();
            sim.Tick();
            MoveScenario.MoveAll(sim, MoveScenario.Center(sim.World.NavGrid, goalCell));
            sim.Tick();
            for (int t = 0; t < 800; t++) sim.Tick();
            UnitStore u = sim.World.Units;
            var result = new (Vector2, int)[u.Capacity];
            for (int i = 0; i < u.Capacity; i++) result[i] = (u.Position[i], u.TypeId[i]);
            Array.Sort(result, (a, b) => a.Item1.X != b.Item1.X ? a.Item1.X.CompareTo(b.Item1.X) : a.Item1.Y.CompareTo(b.Item1.Y));
            return result;
        }
        var fwd = Positions(false);
        var rev = Positions(true);
        // Greedy nearest matching (sets, not slots).
        var used = new bool[rev.Length];
        float worst = 0f;
        int beyond = 0;
        foreach ((Vector2 p, int type) in fwd)
        {
            int best = -1;
            float bd = float.MaxValue;
            for (int j = 0; j < rev.Length; j++)
                if (!used[j] && rev[j].Item2 == type && Vector2.Distance(p, rev[j].Item1) < bd) { bd = Vector2.Distance(p, rev[j].Item1); best = j; }
            used[best] = true;
            worst = MathF.Max(worst, bd);
            if (bd > 1e-3f) beyond++;
        }
        _out.WriteLine($"reversed spawn order: worst matched drift {worst:F5} m, {beyond}/{fwd.Length} units beyond 1e-3 m");
    }

    /// <summary>Two sims, crowding with interleaved spawns, frees and retargets near one point: hash-equal every tick.</summary>
    [Fact]
    public void Determinism_CrowdingWithInterleavedSpawnsFreesRetargets_HashEqualEveryTick()
    {
        ulong[] Run(ulong seed)
        {
            Simulation sim = MoveScenario.Spawn(seed, units: 150, maxCost: 20f, out int center, capacity: 300);
            NavGrid g = sim.World.NavGrid;
            Vector2 c = MoveScenario.Center(g, center);
            var rng = new SimRng(5, 77);
            UnitStore u = sim.World.Units;
            var hashes = new ulong[1000];
            for (int t = 0; t < hashes.Length; t++)
            {
                int n = rng.NextInt(0, 8);
                for (int k = 0; k < n; k++)
                {
                    int slot = rng.NextInt(0, u.Capacity);
                    int op = rng.NextInt(0, 10);
                    if (op < 3) sim.Enqueue(Command.SpawnUnit(rng.NextInt(0, 2), rng.NextInt(0, TestSim.UnitTypeCount), c + new Vector2(rng.NextFloat() * 4f - 2f, rng.NextFloat() * 4f - 2f)));
                    else if (op < 9 && u.Alive[slot]) sim.Enqueue(Command.Move(u.Owner[slot], MoveScenario.Handle(sim, slot), c + new Vector2(rng.NextInt(-2, 3) * 3f, rng.NextInt(-2, 3) * 3f)));
                    else if (u.Alive[slot] && t % 5 == 0) u.Free(MoveScenario.Handle(sim, slot));
                }
                sim.Tick();
                hashes[t] = sim.StateHash();
            }
            return hashes;
        }
        ulong[] a = Run(81), b = Run(81);
        for (int t = 0; t < a.Length; t++) Assert.True(a[t] == b[t], $"diverged at tick {t}");
        Assert.NotEqual(a[^1], Run(82)[^1]);
    }

    /// <summary>Perf and allocation in one tight blob: this class runs alone (SerialCollection).</summary>
    [Collection(SerialCollection.Name)]
    public class Serial
    {
        private readonly ITestOutputHelper _out;

        public Serial(ITestOutputHelper output) => _out = output;

        /// <summary>All units spawned within a few cells of the goal: the worst case for QueryRadius (crowded buckets). One player (M1-4d-3: two players at one point are enemies contesting it, BUG-0037).</summary>
        private static Simulation TightBlob(int units)
        {
            Simulation sim = MoveScenario.Spawn(seed: 99, units: units, maxCost: units >= 2500 ? 12f : units >= 1000 ? 8f : 6f, out int goalCell, players: 1);
            MoveScenario.MoveAll(sim, MoveScenario.Center(sim.World.NavGrid, goalCell));
            sim.Tick();
            return sim;
        }

        [Trait("Category", "Perf")]
        [Theory]
        [InlineData(500, true)]
        [InlineData(1000, false)]
        [InlineData(2500, false)]
        public void Perf_TightBlob_AvgAndWorstTick(int units, bool enforce)
        {
            Simulation sim = TightBlob(units);
            for (int t = 0; t < 5; t++) sim.Tick(); // warm-up (JIT, field build)
            var times = new double[300];
            var sw = new Stopwatch();
            for (int t = 0; t < times.Length; t++)
            {
                sw.Restart();
                sim.Tick();
                times[t] = sw.Elapsed.TotalMilliseconds;
            }
            double avg = times.Average(), worst = times.Max();
            int moving = 0;
            UnitStore u = sim.World.Units;
            for (int i = 0; i < u.Capacity; i++) if (u.State[i] == UnitState.Moving) moving++;
            _out.WriteLine($"{units} units in a tight blob: avg {avg:F2} ms, worst {worst:F2} ms, {moving} still moving after {times.Length} ticks");
            if (!enforce) return;
            Assert.True(avg < 4.0, $"avg {avg:F2} ms (docs/03: < 4 ms)");
        }

        [Fact]
        public void Tick_TightBlobOf500_WhileArrivingAndOnceSettled_AllocatesNothing()
        {
            Simulation sim = TightBlob(500);
            for (int t = 0; t < 10; t++) sim.Tick();
            Action ticks = () => { for (int t = 0; t < 20; t++) sim.Tick(); };
            AllocationProbe.AssertZero(ticks, _out);
            for (int t = 0; t < 1500; t++) sim.Tick();
            AllocationProbe.AssertZero(ticks, _out);
        }
    }
}
