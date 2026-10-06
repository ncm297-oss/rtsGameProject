using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA attacks on M1-4d-3 crowd routing (detour, widened queuing, walk-back once per order, chain
/// shove, owner-aware groups, BUG-0030): two-sim hash twins every tick on new scenarios, spawn-order
/// independence of the detour side, oscillation hunts with strict per-tick invariants (passable
/// ground, no blocked corner crossed, never deeper into a standing enemy, displacement at most
/// speed, StuckTicks in range, one walk-back per order), enemy plugs against chain shoves, the
/// corridor pair on 20 more seeds, and the developer's claim about the old crowd rows' owners.
/// </summary>
public class CrowdRoutingQaTests
{
    private readonly ITestOutputHelper _out;

    public CrowdRoutingQaTests(ITestOutputHelper output) => _out = output;

    // ---------- strict per-tick invariants ----------

    /// <summary>Start-of-tick copy for <see cref="Check"/>, plus walk-back bookkeeping per order.</summary>
    internal sealed class Watch
    {
        public readonly Vector2[] Pos;
        public readonly UnitState[] State;
        public readonly Vector2[] Vel;
        public readonly int[] Owner;
        public readonly bool[] Alive;
        public readonly int[] WalkBack;
        public readonly int[] OrderTick;
        public readonly int[] WalkBacksThisOrder;
        public int WalkBacks;
        public float SoftDeepest, ShovedDeepest;
        public int ShovedIntoEnemyTicks;

        public Watch(int capacity)
        {
            Pos = new Vector2[capacity];
            State = new UnitState[capacity];
            Vel = new Vector2[capacity];
            Owner = new int[capacity];
            Alive = new bool[capacity];
            WalkBack = new int[capacity];
            OrderTick = new int[capacity];
            WalkBacksThisOrder = new int[capacity];
        }

        public void Capture(UnitStore u)
        {
            Array.Copy(u.Position, Pos, Pos.Length);
            Array.Copy(u.State, State, State.Length);
            Array.Copy(u.Velocity, Vel, Vel.Length);
            Array.Copy(u.Owner, Owner, Owner.Length);
            Array.Copy(u.Alive, Alive, Alive.Length);
            Array.Copy(u.WalkBack, WalkBack, WalkBack.Length);
            Array.Copy(u.OrderTick, OrderTick, OrderTick.Length);
        }
    }

    private static bool LegalStep(NavGrid g, int ax, int ay, int bx, int by)
    {
        if (!g.IsPassable(bx, by)) return false;
        int dx = bx - ax, dy = by - ay;
        if (Math.Abs(dx) > 1 || Math.Abs(dy) > 1) return false;
        if (dx != 0 && dy != 0) return g.IsPassable(ax + dx, ay) && g.IsPassable(ax, ay + dy);
        return true;
    }

    /// <summary>
    /// After one tick (captured in <paramref name="b"/> before it): every live unit finite, on the map,
    /// on a passable cell, moved at most its speed through a legal step (no blocked corner), StuckTicks
    /// in [0, GiveUpTicks), WalkBack a known value; no unit (walker or shoved) ended deeper into another
    /// player's unit that stood still at the start of the tick; at most one walk-back per order.
    /// Returns an error or null.
    /// </summary>
    internal static string? Check(World w, Watch b)
    {
        UnitStore u = w.Units;
        NavGrid g = w.NavGrid;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            Vector2 p = u.Position[i];
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y)) return $"unit {i} non-finite {p}";
            if (!g.WorldToCell(p, out int x, out int y)) return $"unit {i} off map {p}";
            if (!g.IsPassable(x, y)) return $"unit {i} on blocked cell ({x},{y})";
            if (u.StuckTicks[i] < 0 || u.StuckTicks[i] >= MovementConstants.GiveUpTicks) return $"unit {i} StuckTicks {u.StuckTicks[i]}";
            if (u.WalkBack[i] < 0) return $"unit {i} WalkBack {u.WalkBack[i]}";
            if (u.State[i] == UnitState.Idle && u.Velocity[i] != Vector2.Zero) return $"unit {i} Idle with velocity";
            if (!b.Alive[i]) continue;
            float moved = Vector2.Distance(p, b.Pos[i]);
            if (moved > u.Speed[i] * 1.0001f + 1e-5f) return $"unit {i} moved {moved} > speed {u.Speed[i]}";
            if (g.WorldToCell(b.Pos[i], out int px, out int py) && (px != x || py != y) && !LegalStep(g, px, py, x, y))
                return $"unit {i} crossed ({px},{py}) -> ({x},{y}) illegally";
            if (moved > 0f)
            {
                for (int j = 0; j < u.Capacity; j++)
                {
                    if (j == i || !b.Alive[j] || b.Owner[j] == b.Owner[i]) continue;
                    // State as the planner saw it: after this tick's commands (a re-ordered unit is Moving).
                    bool idleEnemy = b.State[j] != UnitState.Moving && u.OrderTick[j] == b.OrderTick[j];
                    if (!idleEnemy && b.Vel[j] != Vector2.Zero) continue;
                    float sum = u.Radius[i] + u.Radius[j];
                    float before = sum - Vector2.Distance(b.Pos[i], b.Pos[j]);
                    // Against j's start-of-tick position: the planner's frame. (An Idle enemy shoved by its
                    // own side into this unit is that side's shove, not this unit's step.)
                    float after = sum - Vector2.Distance(p, b.Pos[j]);
                    float deeper = after - MathF.Max(before, 0f);
                    if (after <= 0f || deeper <= 1e-4f) continue;
                    // The design's hard wall (M1-5): a walker never goes deeper into an Idle enemy.
                    bool walker = b.State[i] == UnitState.Moving || u.OrderTick[i] != b.OrderTick[i];
                    if (walker && idleEnemy)
                        return $"walker {i} went {deeper:E2} m deeper into Idle enemy {j}";
                    // Measured, not asserted: a walker into a standing Moving enemy (a soft wall by design),
                    // and a shoved Idle unit into any standing enemy (SqueezeLimit allows down to ShoveSpacing).
                    if (walker) b.SoftDeepest = MathF.Max(b.SoftDeepest, deeper);
                    else { b.ShovedDeepest = MathF.Max(b.ShovedDeepest, deeper); b.ShovedIntoEnemyTicks++; }
                }
            }
            // A new order resets the walk-back budget.
            if (u.OrderTick[i] != b.OrderTick[i]) b.WalkBacksThisOrder[i] = 0;
            if (b.WalkBack[i] >= UnitStore.WalkBackPending && u.WalkBack[i] == UnitStore.WalkBackUsed)
            {
                b.WalkBacks++;
                if (++b.WalkBacksThisOrder[i] > 1) return $"unit {i} walked back twice on one order";
            }
        }
        return null;
    }

    private static string Report(Watch w) =>
        $"walk-backs {w.WalkBacks}, deepest walker step into a Moving-standing enemy {w.SoftDeepest:E2} m, deepest shove into an enemy {w.ShovedDeepest:E2} m ({w.ShovedIntoEnemyTicks} unit-ticks)";

    private static int CountMoving(UnitStore u)
    {
        int n = 0;
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.State[i] == UnitState.Moving) n++;
        return n;
    }

    private static bool AnyPending(UnitStore u)
    {
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.WalkBack[i] >= UnitStore.WalkBackPending) return true;
        return false;
    }

    /// <summary>
    /// Builds two sims with <paramref name="make"/>, ticks them in lockstep asserting equal hashes and
    /// the invariants every tick, until nothing moves and no walk-back is pending (or the limit).
    /// <paramref name="each"/> may enqueue commands on both before a tick. Returns (ticks, sim a, the watch with walk-backs and measured depths).
    /// </summary>
    private static (int Ticks, Simulation Sim, Watch W) Twin(Func<Simulation> make, int limit, Action<Simulation, int>? each = null, int minTicks = 3)
    {
        Simulation a = make(), b = make();
        Assert.Equal(a.StateHash(), b.StateHash());
        var watch = new Watch(a.World.Units.Capacity);
        int t = 0;
        do
        {
            each?.Invoke(a, t);
            each?.Invoke(b, t);
            watch.Capture(a.World.Units);
            a.Tick();
            b.Tick();
            t++;
            Assert.True(a.StateHash() == b.StateHash(), $"hashes differ at tick {t}");
            string? err = Check(a.World, watch);
            Assert.True(err == null, $"tick {t}: {err}");
        } while ((t < minTicks || CountMoving(a.World.Units) > 0 || AnyPending(a.World.Units)) && t < limit);
        return (t, a, watch);
    }

    private static Heightmap Map(params string[] rows) => LocalMovementTests.Rows(rows);

    // ---------- the developer's claim: the old split sent both players to every point ----------

    /// <summary>
    /// Verifies the M1-4d-3 claim behind the crowd-row re-setup: MoveScenario.Spawn alternates owners
    /// in spawn order, but commands apply by (player, sequence), so player 0 takes the low slots and
    /// player 1 the high ones; the old <c>slot % 4</c> goal split (and <c>slot % 64</c>) therefore sent
    /// both players to every point, contrary to the old remark "neighboring points belong to different
    /// players". And the new <see cref="CrowdRows.PointOf"/> gives each point exactly one player, with
    /// each point's two neighbors the enemy's.
    /// </summary>
    [Fact]
    public void OldCrowdSplit_SentBothPlayersToEveryPoint_NewSplitGivesOnePlayerPerPoint()
    {
        Simulation sim = MoveScenario.Spawn(seed: 1, units: 500, maxCost: 40f, out _);
        UnitStore u = sim.World.Units;
        for (int i = 0; i < 250; i++) Assert.Equal(0, u.Owner[i]);
        for (int i = 250; i < 500; i++) Assert.Equal(1, u.Owner[i]);
        var oldOwners = new HashSet<int>[4];
        var newOwners = new HashSet<int>[4];
        var newCount = new int[4];
        for (int k = 0; k < 4; k++) { oldOwners[k] = new HashSet<int>(); newOwners[k] = new HashSet<int>(); }
        for (int i = 0; i < u.Capacity; i++)
        {
            oldOwners[i % 4].Add(u.Owner[i]);
            int p = CrowdRows.PointOf(u, i, true);
            newOwners[p].Add(u.Owner[i]);
            newCount[p]++;
        }
        for (int k = 0; k < 4; k++)
        {
            Assert.Equal(2, oldOwners[k].Count);
            Assert.Single(newOwners[k]);
            Assert.Equal(125, newCount[k]);
        }
        // Points: 0 (-3,-3), 1 (3,-3), 2 (-3,3), 3 (3,3). Side neighbors of 0 are 1 and 2: the enemy's.
        Assert.NotEqual(newOwners[0].Single(), newOwners[1].Single());
        Assert.NotEqual(newOwners[0].Single(), newOwners[2].Single());
        Assert.Equal(newOwners[0].Single(), newOwners[3].Single());
    }

    // ---------- detour side: spawn-order independence ----------

    /// <summary>
    /// A walker crossing an enemy cluster of mixed radii at random offsets, the enemy spawns applied in
    /// six different permutations: the walker's path must be bit-equal in every permutation (the detour
    /// side and turn read start-of-tick state only; sums are order-free).
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    [InlineData(5UL)]
    [InlineData(6UL, Skip = "BUG-0046: walker positions depend on the neighbors' spawn order (Constrain clip order)")]
    public void DetourPastAMixedEnemyCluster_AnySpawnPermutation_BitEqualWalkerPath(ulong seed)
    {
        const int cluster = 12;
        var rng = new SimRng(seed, 4401);
        float[] radii = { 0.4f, 0.7f, 0.9f };
        var spots = new (int Type, Vector2 At)[cluster];
        var c = new Vector2(41f, 41f + (rng.NextFloat() - 0.5f) * 2f);
        int placed = 0;
        while (placed < cluster)
        {
            float r = radii[rng.NextInt(0, 3)];
            var at = c + new Vector2((rng.NextFloat() - 0.5f) * 6f, (rng.NextFloat() - 0.5f) * 6f);
            bool clear = true;
            for (int k = 0; k < placed; k++)
                if (Vector2.Distance(at, spots[k].At) < r + TestSim.Data.Units[spots[k].Type].Radius) clear = false;
            if (!clear) continue;
            spots[placed++] = (LocalMovementTests.TypeWithRadius(r), at);
        }
        List<Vector2> Run(int[] perm)
        {
            var sim = new Simulation(TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: cluster + 1, CommandCapacity: 64), LocalMovementTests.Flat(40));
            sim.Enqueue(Command.SpawnUnit(0, LocalMovementTests.TypeWithRadius(0.4f), new Vector2(23f, 41.3f)));
            foreach (int k in perm) sim.Enqueue(Command.SpawnUnit(1, spots[k].Type, spots[k].At));
            sim.Tick();
            sim.Tick();
            UnitStore u = sim.World.Units;
            Assert.Equal(0, u.Owner[0]);
            sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), new Vector2(59f, 41f)));
            var path = new List<Vector2>();
            for (int t = 0; t < 600 && (t < 3 || u.State[0] == UnitState.Moving); t++)
            {
                sim.Tick();
                path.Add(u.Position[0]);
            }
            return path;
        }
        int[] id = Enumerable.Range(0, cluster).ToArray();
        List<Vector2> reference = Run(id);
        var prng = new SimRng(seed, 4402);
        for (int p = 0; p < 6; p++)
        {
            int[] perm = (int[])id.Clone();
            for (int k = cluster - 1; k > 0; k--) { int s = prng.NextInt(0, k + 1); (perm[k], perm[s]) = (perm[s], perm[k]); }
            if (p == 0) Array.Reverse(perm = (int[])id.Clone());
            List<Vector2> path = Run(perm);
            Assert.Equal(reference.Count, path.Count);
            for (int t = 0; t < path.Count; t++) Assert.True(reference[t] == path[t], $"permutation {p}, tick {t}: {reference[t]} vs {path[t]}");
        }
        _out.WriteLine($"seed {seed}: {reference.Count} ticks, end {reference[^1]}, bit-equal in 7 spawn orders");
    }

    /// <summary>
    /// Several walkers of one player (their own slots permuted too) crossing an enemy cluster to one
    /// point: positions by identity must be bit-equal for every spawn permutation of both players.
    /// Walkers interact (sidestep, push, queue, detour round each other's standing units, arrival).
    /// </summary>
    [Theory]
    [InlineData(11UL, Skip = "BUG-0046: walker positions depend on the neighbors' spawn order (Constrain clip order)")]
    [InlineData(12UL, Skip = "BUG-0046: walker positions depend on the neighbors' spawn order (Constrain clip order)")]
    [InlineData(13UL, Skip = "BUG-0046: walker positions depend on the neighbors' spawn order (Constrain clip order)")]
    public void ManyWalkersDetouringAnEnemyCluster_AnySpawnPermutation_BitEqualPositions(ulong seed)
    {
        const int walkers = 16, cluster = 10;
        var rng = new SimRng(seed, 4403);
        var walkerAt = new Vector2[walkers];
        for (int k = 0; k < walkers; k++) walkerAt[k] = new Vector2(12f + (k % 4) * 1.9f + rng.NextFloat() * 0.1f, 36f + (k / 4) * 1.9f + rng.NextFloat() * 0.1f);
        var clusterAt = new Vector2[cluster];
        for (int k = 0; k < cluster; k++) clusterAt[k] = new Vector2(38f + (k % 5) * 1.85f, 38f + (k / 5) * 1.85f + rng.NextFloat() * 0.05f);
        int small = LocalMovementTests.TypeWithRadius(0.4f), wide = LocalMovementTests.TypeWithRadius(0.9f);
        Vector2 goal = new(66f, 40f);
        Vector2[][] Run(int[] wp, int[] cp)
        {
            var sim = new Simulation(TestSim.Config(Seed: 2, PlayerCount: 2, UnitCapacity: walkers + cluster, CommandCapacity: 256), LocalMovementTests.Flat(40));
            foreach (int k in wp) sim.Enqueue(Command.SpawnUnit(0, small, walkerAt[k]));
            foreach (int k in cp) sim.Enqueue(Command.SpawnUnit(1, wide, clusterAt[k]));
            sim.Tick();
            sim.Tick();
            UnitStore u = sim.World.Units;
            // slot of walker k is the index of k in wp
            for (int s = 0; s < walkers; s++) sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, s), goal));
            var frames = new List<Vector2[]>();
            for (int t = 0; t < 900 && (t < 3 || CountMoving(u) > 0); t++)
            {
                sim.Tick();
                var f = new Vector2[walkers];
                for (int s = 0; s < walkers; s++) f[wp[s]] = u.Position[s];
                frames.Add(f);
            }
            return frames.ToArray();
        }
        int[] wid = Enumerable.Range(0, walkers).ToArray(), cid = Enumerable.Range(0, cluster).ToArray();
        Vector2[][] reference = Run(wid, cid);
        var prng = new SimRng(seed, 4404);
        int firstDiff = -1;
        string? detail = null;
        for (int p = 0; p < 4 && firstDiff < 0; p++)
        {
            int[] wp = (int[])wid.Clone(), cp = (int[])cid.Clone();
            for (int k = walkers - 1; k > 0; k--) { int s = prng.NextInt(0, k + 1); (wp[k], wp[s]) = (wp[s], wp[k]); }
            for (int k = cluster - 1; k > 0; k--) { int s = prng.NextInt(0, k + 1); (cp[k], cp[s]) = (cp[s], cp[k]); }
            Vector2[][] frames = Run(wp, cp);
            int n = Math.Min(frames.Length, reference.Length);
            for (int t = 0; t < n && firstDiff < 0; t++)
                for (int k = 0; k < walkers; k++)
                    if (frames[t][k] != reference[t][k]) { firstDiff = t; detail = $"permutation {p}, tick {t}, walker {k}: {reference[t][k]} vs {frames[t][k]}"; break; }
            if (firstDiff < 0 && frames.Length != reference.Length) { firstDiff = n; detail = $"permutation {p}: {reference.Length} vs {frames.Length} ticks"; }
        }
        _out.WriteLine(detail ?? $"seed {seed}: bit-equal in 5 spawn orders over {reference.Length} ticks");
        Assert.True(firstDiff < 0, detail);
    }

    // ---------- oscillation hunts (hash twins + invariants every tick) ----------

    /// <summary>A 1-cell corridor along cell row 3 from x 2 to 29, open 4-row rooms at both ends.</summary>
    private static Heightmap CorridorWithRooms()
    {
        var rows = new string[7];
        for (int y = 0; y < 7; y++)
        {
            char[] r = new string('0', 32).ToCharArray();
            if (y != 3) for (int x = 6; x < 26; x++) r[x] = '1';
            rows[y] = new string(r);
        }
        return Map(rows);
    }

    /// <summary>
    /// Two groups swap rooms through a 1-cell corridor (corridor ping-pong): same player or two
    /// players. Every tick: twins hash equal, invariants hold, at most one walk-back per order; everything
    /// stops within the limit (no endless back-and-forth).
    /// </summary>
    [Theory]
    [InlineData(4, 1, 1UL)]
    [InlineData(4, 2, 2UL)]
    [InlineData(8, 1, 3UL)]
    [InlineData(8, 2, 4UL)]
    [InlineData(12, 1, 5UL)]
    [InlineData(12, 2, 6UL)]
    public void TwoGroupsSwapRoomsThroughA1CellCorridor_TerminatesWithInvariants(int perSide, int players, ulong seed)
    {
        Simulation Make()
        {
            var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: 2 * perSide, CommandCapacity: 16 * perSide + 16), CorridorWithRooms());
            NavGrid g = sim.World.NavGrid;
            var rng = new SimRng(seed, 4410);
            for (int side = 0; side < 2; side++)
                for (int k = 0; k < perSide; k++)
                {
                    int x = side == 0 ? 1 + k % 4 : 27 + k % 4, y = 1 + (k / 4) * 2 % 5;
                    var at = g.CellCenter(x, y) + new Vector2((rng.NextFloat() - 0.5f) * 0.4f, (rng.NextFloat() - 0.5f) * 0.4f);
                    sim.Enqueue(Command.SpawnUnit(players == 2 ? side : 0, k % TestSim.UnitTypeCount, at));
                }
            sim.Tick();
            sim.Tick();
            UnitStore u = sim.World.Units;
            Vector2 left = g.CellCenter(2, 3), right = g.CellCenter(29, 3);
            for (int i = 0; i < u.Capacity; i++)
                sim.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), u.Position[i].X < 32f ? right : left));
            return sim;
        }
        (int ticks, Simulation a, Watch w) = Twin(Make, 6000);
        UnitStore ua = a.World.Units;
        bool[] arrived = MoveScenario.Arrived(a.World);
        int arrivedN = arrived.Count(x => x);
        _out.WriteLine($"{perSide}/side, {players} player(s): stopped after {ticks} ticks, arrived {arrivedN}/{2 * perSide}, {Report(w)}");
        Assert.True(ticks < 6000, "still moving or a walk-back pending after 6000 ticks");
    }

    /// <summary>A 40 x 24 map with a cliff wall at x 20 and a 1-cell gap at row 11.</summary>
    private static Heightmap OneGapWall()
    {
        var rows = new string[24];
        for (int y = 0; y < 24; y++)
        {
            char[] r = new string('0', 40).ToCharArray();
            if (y != 11) r[20] = '1';
            rows[y] = new string(r);
        }
        return Map(rows);
    }

    /// <summary>
    /// Two groups swap points through one 1-cell gap (each group's point is near the other's side, so
    /// they meet in the gap), mixed types, same or different players, with random click re-orders by
    /// some units. Twins, invariants, termination.
    /// </summary>
    [Theory]
    [InlineData(20, 1, 1UL)]
    [InlineData(20, 2, 2UL)]
    [InlineData(40, 1, 3UL)]
    [InlineData(40, 2, 4UL)]
    [InlineData(60, 2, 5UL)]
    public void TwoGroupsSwapPointsThroughOneGap_TerminatesWithInvariants(int perSide, int players, ulong seed)
    {
        Simulation Make()
        {
            var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: 2 * perSide, CommandCapacity: 16 * perSide + 16), OneGapWall());
            NavGrid g = sim.World.NavGrid;
            var rng = new SimRng(seed, 4420);
            for (int side = 0; side < 2; side++)
                for (int k = 0; k < perSide; k++)
                {
                    int x = side == 0 ? 6 + k % 8 : 26 + k % 8, y = 6 + (k / 8) % 12;
                    var at = g.CellCenter(x, y) + new Vector2((rng.NextFloat() - 0.5f) * 0.6f, (rng.NextFloat() - 0.5f) * 0.6f);
                    sim.Enqueue(Command.SpawnUnit(players == 2 ? side : 0, k % TestSim.UnitTypeCount, at));
                }
            sim.Tick();
            sim.Tick();
            UnitStore u = sim.World.Units;
            Vector2 west = g.CellCenter(15, 11), east = g.CellCenter(25, 11);
            for (int i = 0; i < u.Capacity; i++)
                sim.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), u.Position[i].X < 40f ? east : west));
            return sim;
        }
        // Re-click the same points for a few units every 40 ticks for the first 400 ticks (players re-ordering).
        void Each(Simulation s, int t)
        {
            if (t == 0 || t > 400 || t % 40 != 0) return;
            UnitStore u = s.World.Units;
            NavGrid g = s.World.NavGrid;
            for (int i = t / 40 % 5; i < u.Capacity; i += 5)
                s.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(s, i), i < perSide ? g.CellCenter(25, 11) : g.CellCenter(15, 11)));
        }
        (int ticks, Simulation a, Watch w) = Twin(Make, 8000, Each);
        int arrivedN = MoveScenario.Arrived(a.World).Count(x => x);
        _out.WriteLine($"{perSide}/side, {players} player(s): stopped after {ticks} ticks, arrived {arrivedN}/{2 * perSide}, {Report(w)}");
        Assert.True(ticks < 8000, "no termination");
    }

    /// <summary>
    /// Ring of enemies: walkers inside a closed ring of standing enemy units with their goal outside
    /// (and walkers outside with the goal at the ring's center). Nobody gets through the ring, nobody
    /// goes deeper into an enemy, everyone stops; twins hash equal.
    /// </summary>
    [Theory]
    [InlineData(true, 1UL)]
    [InlineData(false, 2UL)]
    [InlineData(true, 3UL)]
    [InlineData(false, 4UL)]
    public void RingOfEnemies_WalkersNeverPass_AllStop(bool inside, ulong seed)
    {
        const int ring = 16, walkers = 6;
        var center = new Vector2(40f, 40f);
        float ringR = 4.5f;
        Simulation Make()
        {
            var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: ring + walkers, CommandCapacity: 128), LocalMovementTests.Flat(40));
            int wide = LocalMovementTests.TypeWithRadius(0.9f), small = LocalMovementTests.TypeWithRadius(0.4f);
            var rng = new SimRng(seed, 4430);
            for (int k = 0; k < walkers; k++)
            {
                float a = k * MathF.Tau / walkers + rng.NextFloat() * 0.3f;
                float r = inside ? 1.2f + rng.NextFloat() * 0.8f : 12f + rng.NextFloat() * 3f;
                sim.Enqueue(Command.SpawnUnit(0, small, center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r));
            }
            // 16 wide units on a 4.5 m circle: chord 1.76 m < 1.8 m, so they touch all round.
            for (int k = 0; k < ring; k++)
            {
                float a = k * MathF.Tau / ring;
                sim.Enqueue(Command.SpawnUnit(1, wide, center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * ringR));
            }
            sim.Tick();
            sim.Tick();
            Vector2 goal = inside ? center + new Vector2(14f, 3f) : center;
            for (int i = 0; i < walkers; i++) sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, i), goal));
            return sim;
        }
        (int ticks, Simulation s, Watch w) = Twin(Make, 3000);
        UnitStore u = s.World.Units;
        int crossed = 0;
        for (int i = 0; i < walkers; i++)
        {
            float d = Vector2.Distance(u.Position[i], center);
            if (inside ? d > ringR : d < ringR) crossed++;
        }
        _out.WriteLine($"inside {inside}: stopped after {ticks} ticks, crossed the ring {crossed}, {Report(w)}");
        Assert.Equal(0, crossed);
        Assert.True(ticks < 3000);
    }

    /// <summary>
    /// Walk-back loop hunt: a 1-cell corridor with several units parked alone at points along it and
    /// a stream of walkers from both ends to the far room, ordered in waves. Each unit walks back at
    /// most once per order; all stops; twins hash equal.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    [InlineData(5UL)]
    [InlineData(6UL)]
    public void ParkedUnitsAlongACorridor_WavesOfWalkersBothWays_WalkBackOncePerOrder_AllStop(ulong seed)
    {
        const int parked = 5, waves = 3, perWave = 4;
        int cap = parked + 2 * waves * perWave;
        Simulation Make()
        {
            var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 1, UnitCapacity: cap, CommandCapacity: 16 * cap), CorridorWithRooms());
            NavGrid g = sim.World.NavGrid;
            var rng = new SimRng(seed, 4440);
            for (int k = 0; k < parked; k++) sim.Enqueue(Command.SpawnUnit(0, rng.NextInt(0, TestSim.UnitTypeCount), g.CellCenter(8 + 4 * k, 3)));
            for (int k = 0; k < waves * perWave; k++)
            {
                sim.Enqueue(Command.SpawnUnit(0, rng.NextInt(0, TestSim.UnitTypeCount), g.CellCenter(1 + k % 4, 1 + k / 4 % 5)));
                sim.Enqueue(Command.SpawnUnit(0, rng.NextInt(0, TestSim.UnitTypeCount), g.CellCenter(27 + k % 4, 1 + k / 4 % 5)));
            }
            sim.Tick();
            sim.Tick();
            UnitStore u = sim.World.Units;
            for (int k = 0; k < parked; k++) sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, k), g.CellCenter(8 + 4 * k, 3)));
            return sim;
        }
        void Each(Simulation s, int t)
        {
            if (t < 10 || (t - 10) % 120 != 0 || (t - 10) / 120 >= waves) return;
            int wave = (t - 10) / 120;
            NavGrid g = s.World.NavGrid;
            for (int k = 0; k < 2 * perWave; k++)
            {
                int slot = parked + 2 * (wave * perWave) + k;
                bool fromLeft = k % 2 == 0;
                s.Enqueue(Command.Move(0, MoveScenario.Handle(s, slot), fromLeft ? g.CellCenter(29, 1 + wave) : g.CellCenter(2, 1 + wave)));
            }
        }
        (int ticks, Simulation a, Watch w) = Twin(Make, 8000, Each, minTicks: 10 + 120 * waves);
        _out.WriteLine($"seed {seed}: stopped after {ticks} ticks, {Report(w)}");
        Assert.True(ticks < 8000);
    }

    // ---------- enemy plugs hold against chain shoves ----------

    /// <summary>
    /// A 1-cell corridor plugged by one wide enemy at its middle. Ahead of the plug (on the walkers'
    /// side) stand friendly units: a goal-less line and a parked pair, which chain shoves would push
    /// into the plug. A crowd of walkers to the far room pushes from behind for long. No player-0 unit
    /// ever gets past the plug, the plug never moves, nobody goes deeper into it.
    /// </summary>
    [Theory]
    [InlineData(1UL, 6)]
    [InlineData(2UL, 10)]
    [InlineData(3UL, 16)]
    public void CorridorPluggedByAnEnemy_FriendlyLinesAhead_ChainShovesNeverSqueezeAnyonePast(ulong seed, int crowd)
    {
        const int line = 4;
        int cap = 1 + line + crowd;
        Simulation Make()
        {
            var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: cap, CommandCapacity: 16 * cap), CorridorWithRooms());
            NavGrid g = sim.World.NavGrid;
            var rng = new SimRng(seed, 4450);
            int wide = LocalMovementTests.TypeWithRadius(0.9f), small = LocalMovementTests.TypeWithRadius(0.4f);
            for (int k = 0; k < line; k++) sim.Enqueue(Command.SpawnUnit(0, k % 2 == 0 ? small : wide, g.CellCenter(15 - k, 3) + new Vector2(0.3f, 0f)));
            for (int k = 0; k < crowd; k++) sim.Enqueue(Command.SpawnUnit(0, rng.NextInt(0, TestSim.UnitTypeCount), g.CellCenter(1 + k % 4, 1 + k / 4 % 5)));
            sim.Enqueue(Command.SpawnUnit(1, wide, g.CellCenter(16, 3)));
            sim.Tick();
            sim.Tick();
            // Units 2, 3 park on one point ahead of the plug; 0, 1 stay goal-less.
            sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 2), g.CellCenter(13, 3)));
            sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 3), g.CellCenter(13, 3)));
            return sim;
        }
        float plugX = 0f, worstRatio = float.MaxValue, maxX = 0f;
        Vector2 plugAt = default;
        int plugSlot = cap - 1;
        string? worstAt = null;
        void Each(Simulation s, int t)
        {
            NavGrid g = s.World.NavGrid;
            UnitStore u = s.World.Units;
            if (t == 1) { plugAt = u.Position[plugSlot]; plugX = plugAt.X; }
            if (t >= 1)
            {
                Assert.True(u.Position[plugSlot] == plugAt, $"tick {t}: the plug moved");
                for (int i = 0; i < plugSlot; i++)
                {
                    float ratio = Vector2.Distance(u.Position[i], plugAt) / (u.Radius[i] + u.Radius[plugSlot]);
                    if (ratio < worstRatio) { worstRatio = ratio; worstAt = $"tick {t}: unit {i} ({u.State[i]}, goal cell {u.GoalCell[i]}, r {u.Radius[i]}) at {u.Position[i]}"; }
                    maxX = MathF.Max(maxX, u.Position[i].X);
                    // Through: its center beyond the plug's far edge.
                    Assert.True(u.Position[i].X < plugX + u.Radius[plugSlot], $"tick {t}: unit {i} ({u.State[i]}, goal cell {u.GoalCell[i]}, r {u.Radius[i]}) got through the plug to {u.Position[i]}");
                }
            }
            // The crowd re-ordered to the far room every 100 ticks for 1,000 ticks: long pressure.
            if (t >= 60 && t <= 1000 && (t - 60) % 100 == 0)
                for (int i = line; i < plugSlot; i++) s.Enqueue(Command.Move(0, MoveScenario.Handle(s, i), g.CellCenter(29, 3)));
        }
        (int ticks, Simulation a, Watch w) = Twin(Make, 6000, Each, minTicks: 1100);
        _out.WriteLine($"seed {seed}, crowd {crowd}: stopped after {ticks} ticks, {Report(w)}; nobody through; max x {maxX:F2} (plug at {plugX:F2}); closest to the plug {worstRatio:F3} x the radii's sum, {worstAt}");
        // A friendly unit may be shoved into the plug down to the pack limit (ShoveSpacing x the radii's sum), never deeper.
        Assert.True(worstRatio >= MovementConstants.ShoveSpacing - 1e-3f, $"pressed onto the enemy plug: {worstRatio:F3} x the radii's sum, {worstAt}");
    }

    /// <summary>A corridor <paramref name="width"/> cells wide (rows 3..3+width-1) from x 6 to 25, open 3-row rooms above and below it at both ends.</summary>
    private static Heightmap WideCorridorWithRooms(int width)
    {
        int h = 7 + width;
        var rows = new string[h];
        for (int y = 0; y < h; y++)
        {
            char[] r = new string('0', 32).ToCharArray();
            if (y < 3 || y >= 3 + width) for (int x = 6; x < 26; x++) r[x] = '1';
            rows[y] = new string(r);
        }
        return Map(rows);
    }

    /// <summary>
    /// The plug attack (BUG-0045) beyond 1-cell passages, where the round-1 cone rules don't apply: a
    /// corridor <paramref name="width"/> cells wide, plugged at x cell 16 by one wide Idle enemy per row
    /// (0.2 m slits between them), goal-less friendly units and a parked pair ahead of the plug, a crowd
    /// pushing to the far room for 1,000 ticks. Nobody gets through, nobody is pressed past the pack limit.
    /// </summary>
    [Theory]
    [InlineData(1, 4UL, 12)]
    [InlineData(1, 5UL, 16)]
    [InlineData(2, 1UL, 12, Skip = "BUG-0045: outside 1-cell passages shoves still press friendly units into and through an enemy plug")]
    [InlineData(2, 2UL, 16, Skip = "BUG-0045: outside 1-cell passages shoves still press friendly units into and through an enemy plug")]
    [InlineData(2, 3UL, 20, Skip = "BUG-0045: outside 1-cell passages shoves still press friendly units into and through an enemy plug")]
    [InlineData(3, 4UL, 20, Skip = "BUG-0045: outside 1-cell passages shoves still press friendly units into and through an enemy plug")]
    public void CorridorOfWidthPluggedByEnemies_FriendlyLinesAhead_NobodyThrough(int width, ulong seed, int crowd)
    {
        int line = 4 * width;
        int cap = width + line + crowd;
        Simulation Make()
        {
            var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: cap, CommandCapacity: 16 * cap), WideCorridorWithRooms(width));
            NavGrid g = sim.World.NavGrid;
            var rng = new SimRng(seed, 4451);
            int wide = LocalMovementTests.TypeWithRadius(0.9f), small = LocalMovementTests.TypeWithRadius(0.4f);
            for (int row = 0; row < width; row++)
                for (int k = 0; k < 4; k++)
                    sim.Enqueue(Command.SpawnUnit(0, (k + row) % 2 == 0 ? small : wide, g.CellCenter(15 - k, 3 + row) + new Vector2(0.3f, 0f)));
            for (int k = 0; k < crowd; k++) sim.Enqueue(Command.SpawnUnit(0, rng.NextInt(0, TestSim.UnitTypeCount), g.CellCenter(1 + k % 4, 1 + k / 4 % (5 + width))));
            for (int row = 0; row < width; row++) sim.Enqueue(Command.SpawnUnit(1, wide, g.CellCenter(16, 3 + row)));
            sim.Tick();
            sim.Tick();
            // Two units of each line park on one point ahead of the plug; the others stay goal-less.
            for (int row = 0; row < width; row++)
            {
                sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 4 * row + 2), g.CellCenter(13, 3 + row)));
                sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 4 * row + 3), g.CellCenter(13, 3 + row)));
            }
            return sim;
        }
        int firstPlug = cap - width;
        float plugX = 0f, worstRatio = float.MaxValue;
        var plugAt = new Vector2[width];
        string? worstAt = null;
        void Each(Simulation s, int t)
        {
            NavGrid g = s.World.NavGrid;
            UnitStore u = s.World.Units;
            if (t == 1) { for (int k = 0; k < width; k++) plugAt[k] = u.Position[firstPlug + k]; plugX = plugAt[0].X; }
            if (t >= 1)
            {
                for (int k = 0; k < width; k++) Assert.True(u.Position[firstPlug + k] == plugAt[k], $"tick {t}: a plug unit moved");
                for (int i = 0; i < firstPlug; i++)
                {
                    for (int k = 0; k < width; k++)
                    {
                        float ratio = Vector2.Distance(u.Position[i], plugAt[k]) / (u.Radius[i] + 0.9f);
                        if (ratio < worstRatio) { worstRatio = ratio; worstAt = $"tick {t}: unit {i} ({u.State[i]}, goal cell {u.GoalCell[i]}, r {u.Radius[i]}) at {u.Position[i]}"; }
                    }
                    Assert.True(u.Position[i].X < plugX + 0.9f, $"tick {t}: unit {i} ({u.State[i]}, goal cell {u.GoalCell[i]}, r {u.Radius[i]}) got through the plug to {u.Position[i]}");
                }
            }
            if (t >= 60 && t <= 1000 && (t - 60) % 100 == 0)
                for (int i = line; i < firstPlug; i++) s.Enqueue(Command.Move(0, MoveScenario.Handle(s, i), g.CellCenter(29, 3)));
        }
        (int ticks, Simulation a, Watch w) = Twin(Make, 6000, Each, minTicks: 1100);
        _out.WriteLine($"width {width}, seed {seed}, crowd {crowd}: stopped after {ticks} ticks, {Report(w)}; nobody through; closest to a plug unit {worstRatio:F3} x the radii's sum, {worstAt}");
        Assert.True(worstRatio >= MovementConstants.ShoveSpacing - 1e-3f, $"pressed onto the enemy plug: {worstRatio:F3} x the radii's sum, {worstAt}");
    }

    // ---------- corridor pair on 20 more seeds ----------

    private static Heightmap Corridor() => Map(
        "000000000000000000000000",
        "111111111111111111111111",
        "000000000000000000000000",
        "111111111111111111111111",
        "000000000000000000000000");

    /// <summary>
    /// The parked-friendly-pair corridor (BUG-0033) on seeds 1-20: radii, the pair's spot and spacing,
    /// the walker's start and goal vary. The walker arrives; at most one walk-back per order;
    /// invariants and twin hashes every tick; everything stops. Measured at M1-4d-3: the walker ends at
    /// its goal on 6 of 20 (base 7f741f1: 1 of 20); the other seeds are skipped under BUG-0042.
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
    [InlineData(9UL)]
    [InlineData(10UL)]
    [InlineData(11UL)]
    [InlineData(12UL)]
    [InlineData(13UL)]
    [InlineData(14UL)]
    [InlineData(15UL)]
    [InlineData(16UL)]
    [InlineData(17UL)]
    [InlineData(18UL)]
    [InlineData(19UL)]
    [InlineData(20UL)]
    public void ParkedPairInACorridor_VariedSeeds_WalkerArrives(ulong seed)
    {
        float[] radii = { 0.4f, 0.7f, 0.9f };
        var rng0 = new SimRng(seed, 4460);
        int wType = LocalMovementTests.TypeWithRadius(radii[rng0.NextInt(0, 3)]);
        int aType = LocalMovementTests.TypeWithRadius(radii[rng0.NextInt(0, 3)]);
        int bType = LocalMovementTests.TypeWithRadius(radii[rng0.NextInt(0, 3)]);
        int parkX = 6 + rng0.NextInt(0, 6);
        float jitter = (rng0.NextFloat() - 0.5f) * 0.8f;
        int startX = 1 + rng0.NextInt(0, 3);
        int goalX = parkX + 4 + rng0.NextInt(0, 6);
        Vector2 walkerGoal = default;
        Simulation Make()
        {
            Simulation sim = LocalMovementTests.SimOn(Corridor(), 3);
            NavGrid g = sim.World.NavGrid;
            Vector2 parked = g.CellCenter(parkX, 2) + new Vector2(jitter, 0f);
            walkerGoal = g.CellCenter(goalX, 2);
            float gap = TestSim.Data.Units[aType].Radius + TestSim.Data.Units[bType].Radius;
            sim.Enqueue(Command.SpawnUnit(0, wType, g.CellCenter(startX, 2)));
            sim.Enqueue(Command.SpawnUnit(0, aType, parked));
            sim.Enqueue(Command.SpawnUnit(0, bType, parked + new Vector2(gap, 0f)));
            sim.Tick();
            sim.Tick();
            sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 1), parked));
            sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 2), parked));
            for (int t = 0; t < 80; t++) sim.Tick();
            Assert.True(sim.World.Units.GoalCell[1] >= 0 && sim.World.Units.GoalCell[2] >= 0, "the pair did not park");
            sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), walkerGoal));
            return sim;
        }
        (int ticks, Simulation a, Watch w) = Twin(Make, 4000);
        UnitStore u = a.World.Units;
        float d = Vector2.Distance(u.Position[0], walkerGoal);
        _out.WriteLine($"seed {seed}: radii {TestSim.Data.Units[wType].Radius}/{TestSim.Data.Units[aType].Radius}/{TestSim.Data.Units[bType].Radius}, park x {parkX}, goal x {goalX}: stopped after {ticks}, walker {d:F2} m from goal (goal cell {u.GoalCell[0]}), {Report(w)}");
        Assert.True(ticks < 4000);
        Assert.True(d <= MovementConstants.ArrivalDistance, $"walker stopped {d:F2} m from its goal");
    }

    /// <summary>
    /// The exact BUG-0033 repro (QA/ShoveQaTests.WalkerInOneCellCorridor_PastAParkedFriendlyPair_Arrives:
    /// radius-0.9 units, pair parked at cell 8, walker from cell 2 to cell 14), but ticked until nothing
    /// moves and no walk-back is pending, not only until the walker first goes Idle. The pair the walker
    /// pushed past its goal walks back; the walker must still be at its goal at the end. Also the
    /// goal-x sweep 12-20 (the repro's 14 among them).
    /// </summary>
    [Theory]
    [InlineData(14)]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(18)]
    [InlineData(20)]
    public void WalkerPastAParkedPair_StillAtItsGoalOnceTheWalkBacksSettle(int goalX)
    {
        Vector2 goal = default;
        int firstIdleTick = -1;
        float firstIdleDistance = -1f;
        Simulation Make()
        {
            Simulation sim = LocalMovementTests.SimOn(Corridor(), 3);
            NavGrid g = sim.World.NavGrid;
            int type = LocalMovementTests.TypeWithRadius(0.9f);
            Vector2 parked = g.CellCenter(8, 2);
            goal = g.CellCenter(goalX, 2);
            sim.Enqueue(Command.SpawnUnit(0, type, g.CellCenter(2, 2)));
            sim.Enqueue(Command.SpawnUnit(0, type, parked));
            sim.Enqueue(Command.SpawnUnit(0, type, parked + new Vector2(1.8f, 0f)));
            sim.Tick();
            sim.Tick();
            sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 1), parked));
            sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 2), parked));
            for (int t = 0; t < 80; t++) sim.Tick();
            sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), goal));
            return sim;
        }
        void Each(Simulation s, int t)
        {
            UnitStore u = s.World.Units;
            if (firstIdleTick < 0 && t > 2 && u.State[0] == UnitState.Idle)
            {
                firstIdleTick = t;
                firstIdleDistance = Vector2.Distance(u.Position[0], goal);
            }
        }
        firstIdleTick = -1;
        (int ticks, Simulation a, Watch w) = Twin(Make, 4000, Each);
        UnitStore ua = a.World.Units;
        float d = Vector2.Distance(ua.Position[0], goal);
        _out.WriteLine($"goal x {goalX}: walker first Idle at tick {firstIdleTick}, {firstIdleDistance:F2} m from its goal; settled after {ticks} ticks: walker {d:F2} m from its goal (goal cell {ua.GoalCell[0]}), pair at {ua.Position[1]} / {ua.Position[2]} (goal cells {ua.GoalCell[1]} / {ua.GoalCell[2]}); {Report(w)}");
        Assert.True(d <= MovementConstants.ArrivalDistance && ua.GoalCell[0] >= 0, $"walker ends {d:F2} m from its goal, goal cell {ua.GoalCell[0]} (first Idle {firstIdleDistance:F2} m from it)");
    }

    // ---------- BUG-0030 vs click spam ----------

    /// <summary>
    /// A lone unit walking ~40 m across open ground, re-ordered every <paramref name="every"/> ticks to
    /// a point of its goal cell jittered by up to <paramref name="jitter"/> m from the cell center (a
    /// player spam-clicking at one spot, or an AI refreshing). Nothing blocks it: it must arrive,
    /// not give up mid-field. M1-4d-3 lowers its best estimate by 2 x the shift on every retarget.
    /// </summary>
    [Theory]
    [InlineData(1, 0.05f)]
    [InlineData(2, 0.1f)]
    [InlineData(2, 0.25f)]
    [InlineData(4, 0.25f)]
    [InlineData(1, 0.5f)]
    [InlineData(3, 0.5f)]
    public void FreeWalker_JitterSpamClickedWithinItsGoalCell_StillArrives(int every, float jitter)
    {
        Simulation sim = LocalMovementTests.SimOn(LocalMovementTests.Flat(32), 1);
        sim.Enqueue(Command.SpawnUnit(0, LocalMovementTests.TypeWithRadius(0.9f), new Vector2(11f, 31f))); // slowest type
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        NavGrid g = sim.World.NavGrid;
        Vector2 center = g.CellCenter(26, 15);
        var rng = new SimRng(9, (ulong)(every * 100 + (int)(jitter * 100)));
        int t = 0;
        do
        {
            if (t % every == 0 && (t < 2 || u.State[0] == UnitState.Moving))
            {
                var p = center + new Vector2((rng.NextFloat() * 2f - 1f) * jitter, (rng.NextFloat() * 2f - 1f) * jitter);
                sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), p));
            }
            sim.Tick();
            t++;
        } while ((t < 3 || u.State[0] == UnitState.Moving) && t < 2000);
        float d = Vector2.Distance(u.Position[0], center);
        _out.WriteLine($"every {every} ticks, jitter {jitter} m: {u.State[0]} after {t} ticks, {d:F2} m from the cell center, goal cell {u.GoalCell[0]}");
        Assert.True(u.GoalCell[0] >= 0 && d <= MovementConstants.ArrivalDistance + jitter * 1.5f, $"gave up {d:F2} m from its point after {t} ticks");
    }

    /// <summary>
    /// One single re-order mid-walk to another point of the same goal cell, <paramref name="shift"/> m
    /// from the first (a player correcting the destination inside one 2 m cell). Nothing blocks the
    /// walker; it must still arrive at the new point. M1-4d-3 lowers the walker's best estimate by
    /// 2 x the shift, which outside the goal cell is pure deficit (the field cost doesn't depend on the
    /// point inside the goal cell): a slow unit can't make it up within GiveUpTicks and gives up.
    /// </summary>
    [Theory]
    [InlineData(0.9f, 0.8f)]
    [InlineData(0.9f, 1.2f)]
    [InlineData(0.9f, 1.6f)]
    [InlineData(0.4f, 1.6f)]
    [InlineData(0.4f, 2.4f)]
    public void FreeWalker_ReorderedOnceToAnotherPointOfItsGoalCell_StillArrives(float radius, float shift)
    {
        Simulation sim = LocalMovementTests.SimOn(LocalMovementTests.Flat(32), 1);
        sim.Enqueue(Command.SpawnUnit(0, LocalMovementTests.TypeWithRadius(radius), new Vector2(11f, 31f)));
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        NavGrid g = sim.World.NavGrid;
        Vector2 corner = g.CellCenter(26, 15) - new Vector2(0.95f, 0.95f);
        Vector2 first = corner + new Vector2(0.05f, 0.05f);
        Vector2 second = first + Vector2.Normalize(new Vector2(1f, 1f)) * shift;
        Assert.True(g.WorldToCell(second, out int sx, out int sy) && sx == 26 && sy == 15, "second point left the cell");
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), first));
        for (int t = 0; t < 40; t++) sim.Tick();
        Assert.Equal(UnitState.Moving, u.State[0]);
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), second));
        int ticks = 0;
        do { sim.Tick(); ticks++; } while (u.State[0] == UnitState.Moving && ticks < 1000);
        float d = Vector2.Distance(u.Position[0], second);
        _out.WriteLine($"radius {radius} (speed {u.Speed[0]:F2} m/tick), shift {shift} m: Idle after {ticks} more ticks, {d:F2} m from the new point, goal cell {u.GoalCell[0]}");
        Assert.True(u.GoalCell[0] >= 0 && d <= MovementConstants.ArrivalDistance, $"gave up {d:F2} m from the new point");
    }

    /// <summary>
    /// BUG-0029 still holds after BUG-0030: an arrived blob of 60 re-clicked at exactly the same point
    /// every tick for 300 ticks never moves, never changes order tick, never restarts.
    /// </summary>
    [Fact]
    public void ArrivedBlob_ClickSpammedAtTheSamePoint_NoUnitRestartsOrMoves()
    {
        Simulation sim = MoveScenario.Spawn(seed: 21, units: 60, maxCost: 20f, out int goalCell, players: 1);
        Vector2 goal = MoveScenario.Center(sim.World.NavGrid, goalCell) + new Vector2(0.37f, -0.21f);
        UnitStore u = sim.World.Units;
        MoveScenario.MoveAll(sim, goal);
        int t = 0;
        do { sim.Tick(); t++; } while ((t < 3 || CountMoving(u) > 0 || AnyPending(u)) && t < 3000);
        var pos = (Vector2[])u.Position.Clone();
        var order = (int[])u.OrderTick.Clone();
        for (int k = 0; k < 300; k++)
        {
            MoveScenario.MoveAll(sim, goal);
            sim.Tick();
            for (int i = 0; i < u.Capacity; i++)
            {
                if (u.GoalCell[i] < 0) continue; // given-up units take the click as a new order, as designed
                Assert.True(u.Position[i] == pos[i] && u.OrderTick[i] == order[i] && u.State[i] == UnitState.Idle, $"tick {k}: unit {i} restarted or moved");
            }
        }
    }

    // ---------- termination under field-cache churn ----------

    /// <summary>
    /// BUG-0048: the 64-goal row (more live goals than cache slots) on map seed 51 must stop. At
    /// M1-4d-3 94 units stay Moving for 20,000+ ticks with 2 field builds every tick; base stopped at 238.
    /// </summary>
    [Fact]
    public void MoreGoalsThanCacheSlots_Seed51_Terminates()
    {
        CrowdRows.Result r = CrowdRows.MoreGoalsThanCacheSlots(51);
        _out.WriteLine($"seed 51: arrived {r.Arrived}, gave up {r.GaveUp}, still moving {r.StillMoving} after {r.Ticks} ticks");
        Assert.Equal(0, r.StillMoving);
        Assert.Equal(128, r.Arrived + r.GaveUp);
    }

    // ---------- allocation: chain shoves and walk-backs ----------

    /// <summary>Runs alone (allocation measurement).</summary>
    [Collection(SerialCollection.Name)]
    public class Serial
    {
        private readonly ITestOutputHelper _out;

        public Serial(ITestOutputHelper output) => _out = output;

        /// <summary>
        /// The corridor pair (chain shove, un-anchoring, walk-backs on both sides): the measured ticks
        /// allocate 0 bytes, and the measured window really contains chain-shove and walk-back ticks.
        /// </summary>
        [Fact]
        public void CorridorPair_ChainShovesAndWalkBacks_AllocateNothing()
        {
            Simulation? sim = null;
            Action setup = () =>
            {
                Simulation s = LocalMovementTests.SimOn(Corridor(), 3);
                NavGrid g = s.World.NavGrid;
                int type = LocalMovementTests.TypeWithRadius(0.9f);
                Vector2 parked = g.CellCenter(8, 2);
                s.Enqueue(Command.SpawnUnit(0, type, g.CellCenter(2, 2)));
                s.Enqueue(Command.SpawnUnit(0, type, parked));
                s.Enqueue(Command.SpawnUnit(0, type, parked + new Vector2(1.8f, 0f)));
                s.Tick();
                s.Tick();
                s.Enqueue(Command.Move(0, MoveScenario.Handle(s, 1), parked));
                s.Enqueue(Command.Move(0, MoveScenario.Handle(s, 2), parked));
                for (int t = 0; t < 80; t++) s.Tick();
                s.Enqueue(Command.Move(0, MoveScenario.Handle(s, 0), g.CellCenter(16, 2)));
                s.Tick();
                sim = s;
            };
            Action ticks = () => { for (int t = 0; t < 600; t++) sim!.Tick(); };
            int runs = AllocationProbe.AssertZero(ticks, _out, setup);
            UnitStore u = sim!.World.Units;
            int used = 0;
            for (int i = 0; i < 3; i++) if (u.WalkBack[i] == UnitStore.WalkBackUsed) used++;
            _out.WriteLine($"{runs} run(s); walk-backs used {used}");
            Assert.True(used >= 2, "the measured window had no walk-backs");
        }
    }

    // ---------- 2-player crowd with spam, twins ----------

    /// <summary>
    /// 300 units, two players, one point each 6 m apart, while 1 in 7 units is re-ordered to a random
    /// point of its own goal cell every 15 ticks: twins hash equal and invariants every tick.
    /// </summary>
    [Fact]
    public void TwoPlayerCrowdWithJitteredReorders_TwinsAndInvariantsEveryTick()
    {
        Simulation Make()
        {
            Simulation sim = MoveScenario.Spawn(7, 300, 30f, out int goalCell);
            Vector2 c = MoveScenario.Center(sim.World.NavGrid, goalCell);
            UnitStore u = sim.World.Units;
            for (int i = 0; i < u.Capacity; i++)
                sim.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), c + new Vector2(u.Owner[i] == 0 ? -3f : 3f, 0f)));
            return sim;
        }
        void Each(Simulation s, int t)
        {
            if (t == 0 || t > 900 || t % 15 != 0) return;
            UnitStore u = s.World.Units;
            NavGrid g = s.World.NavGrid;
            for (int i = t / 15 % 7; i < u.Capacity; i += 7)
            {
                int gc = u.GoalCell[i];
                if (gc < 0) continue;
                // Deterministic jitter from slot and tick (same for both twins).
                float jx = ((i * 7919 + t * 104729) % 1000) / 1000f - 0.5f, jy = ((i * 104723 + t * 7907) % 1000) / 1000f - 0.5f;
                s.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(s, i), MoveScenario.Center(g, gc) + new Vector2(jx, jy) * 1.8f));
            }
        }
        (int ticks, _, Watch w) = Twin(Make, 4000, Each, minTicks: 920);
        _out.WriteLine($"stopped after {ticks} ticks, {Report(w)}");
        Assert.True(ticks < 4000);
    }
}
