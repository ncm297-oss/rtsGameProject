using System.Numerics;
using System.Reflection;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA attacks on the BUG-0035 fix: enemies that aren't walking are hard walls (<c>MovementSystem.Constrain</c>
/// re-checks every hard wall after the per-wall clips and falls back to <c>ClosestAllowed</c>).
/// </summary>
public class HardWallQaTests
{
    private readonly ITestOutputHelper _out;

    public HardWallQaTests(ITestOutputHelper output) => _out = output;

    private static readonly MethodInfo s_closestAllowed =
        typeof(MovementSystem).GetMethod("ClosestAllowed", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("MovementSystem.ClosestAllowed not found");

    private static readonly MethodInfo s_constrain =
        typeof(MovementSystem).GetMethod("Constrain", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("MovementSystem.Constrain not found");

    private static Vector2 ClosestAllowed(Vector2 desired, Vector2[] normals, float[] limits, int n) =>
        (Vector2)s_closestAllowed.Invoke(null, new object[] { desired, normals, limits, n })!;

    // ---------------------------------------------------------------- ClosestAllowed, in isolation

    /// <summary>
    /// The closest point to <paramref name="d"/> in the intersection of half-planes dot(x, n_k) &lt;= l_k
    /// (all l_k &gt;= 0, so 0 is feasible), in double precision: the minimum over the feasible
    /// candidates desired / one projection / one corner. Independent re-implementation, for reference.
    /// </summary>
    private static (double X, double Y) ReferenceClosest(Vector2 d, Vector2[] normals, float[] limits, int n)
    {
        bool Feasible(double x, double y)
        {
            for (int k = 0; k < n; k++)
                if (x * normals[k].X + y * normals[k].Y > limits[k] + 1e-9) return false;
            return true;
        }
        if (Feasible(d.X, d.Y)) return (d.X, d.Y);
        double bx = 0, by = 0, best = (double)d.X * d.X + (double)d.Y * d.Y;
        for (int k = 0; k < n; k++)
        {
            double over = d.X * (double)normals[k].X + d.Y * (double)normals[k].Y - limits[k];
            double cx = d.X - normals[k].X * over, cy = d.Y - normals[k].Y * over;
            double dd = (cx - d.X) * (cx - d.X) + (cy - d.Y) * (cy - d.Y);
            if (dd < best && Feasible(cx, cy)) { best = dd; bx = cx; by = cy; }
        }
        for (int k = 0; k < n; k++)
            for (int l = k + 1; l < n; l++)
            {
                double ax = normals[k].X, ay = normals[k].Y, cx2 = normals[l].X, cy2 = normals[l].Y;
                double det = ax * cy2 - ay * cx2;
                if (Math.Abs(det) < 1e-12) continue;
                double x = (limits[k] * cy2 - limits[l] * ay) / det, y = (ax * limits[l] - cx2 * limits[k]) / det;
                double dd = (x - d.X) * (x - d.X) + (y - d.Y) * (y - d.Y);
                if (dd < best && Feasible(x, y)) { best = dd; bx = x; by = y; }
            }
        return (bx, by);
    }

    /// <summary>
    /// 50,000 random wall sets (1-8 walls, limits 0-0.35 m, a third of them exactly 0, plus near-parallel,
    /// opposite and duplicated normals) and desired steps up to 0.4 m: the result is finite, enters no
    /// wall past its limit by more than 0.1 mm, and is no farther from the desired step than the true
    /// closest allowed step (double-precision reference) plus 0.1 mm.
    /// </summary>
    [Fact]
    public void ClosestAllowed_Fuzz_AllowedAndOptimal()
    {
        var rng = new SimRng(2026, 35);
        var normals = new Vector2[8];
        var limits = new float[8];
        double worstOver = 0, worstExtra = 0;
        int fallbacks = 0;
        for (int c = 0; c < 50_000; c++)
        {
            int n = 1 + rng.NextInt(0, 8);
            for (int k = 0; k < n; k++)
            {
                float a = rng.NextFloat() * 2f * MathF.PI;
                int kind = rng.NextInt(0, 10);
                if (k > 0 && kind == 0) a = MathF.Atan2(normals[k - 1].Y, normals[k - 1].X) + 1e-6f * (rng.NextFloat() - 0.5f); // near-parallel
                else if (k > 0 && kind == 1) a = MathF.Atan2(normals[k - 1].Y, normals[k - 1].X) + MathF.PI;                     // opposite
                normals[k] = new Vector2(MathF.Cos(a), MathF.Sin(a));
                if (k > 0 && kind == 2) normals[k] = normals[k - 1];                                                           // duplicate
                limits[k] = rng.NextInt(0, 3) == 0 ? 0f : rng.NextFloat() * 0.35f;
            }
            float da = rng.NextFloat() * 2f * MathF.PI, dl = rng.NextFloat() * 0.4f;
            var desired = new Vector2(MathF.Cos(da) * dl, MathF.Sin(da) * dl);
            Vector2 r = ClosestAllowed(desired, normals, limits, n);
            Assert.True(float.IsFinite(r.X) && float.IsFinite(r.Y), $"case {c}: non-finite {r}");
            for (int k = 0; k < n; k++)
            {
                double over = Vector2.Dot(r, normals[k]) - limits[k];
                worstOver = Math.Max(worstOver, over);
                Assert.True(over <= 1e-4, $"case {c}: result {r} enters wall {k} {over:E2} m past its limit");
            }
            (double rx, double ry) = ReferenceClosest(desired, normals, limits, n);
            double refDist = Math.Sqrt((rx - desired.X) * (rx - desired.X) + (ry - desired.Y) * (ry - desired.Y));
            double gotDist = Vector2.Distance(r, desired);
            if (gotDist > 1e-6) fallbacks++;
            worstExtra = Math.Max(worstExtra, gotDist - refDist);
            Assert.True(gotDist <= refDist + 1e-4, $"case {c}: {gotDist:F5} m from desired, best possible {refDist:F5} (n {n}, result {r}, reference ({rx:F5}, {ry:F5}))");
        }
        _out.WriteLine($"50,000 cases, {fallbacks} needed a change; worst overshoot {worstOver:E2} m, worst extra distance {worstExtra:E2} m");
    }

    // ---------------------------------------------------------------- Constrain, hand-placed

    /// <summary>
    /// A walker touching an Idle enemy straight below it and an anchored friendly (Idle on its own
    /// point, so it can't be shoved) up and to the right, with a desired step right and down. The
    /// per-wall clips (enemy first, by slot) slide the step into the enemy, so the hard-wall fallback
    /// runs: it must still keep the friendly's clip (docs/03: "the army's own standing units keep the
    /// single clip"). The fallback's candidates are built from the desired step and the hard walls
    /// alone, so it returns the slide along the enemy, straight into the friendly.
    /// </summary>
    [Fact]
    public void Constrain_EnemyBelowAndAnchoredFriendAbove_FallbackStillRespectsTheFriend()
    {
        Simulation sim = ThreeUnits();
        World w = sim.World;
        UnitStore u = w.Units;
        NavGrid g = w.NavGrid;
        // Slot 0 is the enemy E (visited first, as in ascending slot order), 1 the friendly F, 2 the walker W.
        u.Owner[0] = 0;
        u.Owner[1] = 1;
        u.Owner[2] = 1;
        const int e = 0, f = 1, wk = 2;
        var p = new Vector2(21f, 21f);
        Vector2 nE = new(0f, -1f), nF = new(0.6f, 0.8f);
        u.Position[wk] = p;
        u.Position[e] = p + nE * (u.Radius[wk] + u.Radius[e]);
        u.Position[f] = p + nF * (u.Radius[wk] + u.Radius[f]);
        // F holds its own point (a goal cell other than the walker's): no shove without pushArrived.
        g.WorldToCell(u.Position[f], out int fx, out int fy);
        u.GoalCell[f] = fy * g.Width + fx;
        u.Goal[f] = u.Position[f];
        int walkerGoal = 2 * g.Width + 22;
        Assert.NotEqual(u.GoalCell[f], walkerGoal);
        u.State[wk] = UnitState.Moving;
        u.GoalCell[wk] = walkerGoal;
        float s = 0.8f * u.Speed[wk] / MathF.Sqrt(1.25f);
        var desired = new Vector2(s, -0.5f * s);
        int[] near = { e, f };
        var step = (Vector2)s_constrain.Invoke(null, new object[] { w, g, u, wk, p, desired, near, 2, walkerGoal, false })!;
        float intoE = Vector2.Dot(step, nE), intoF = Vector2.Dot(step, nF);
        _out.WriteLine($"desired {desired}, step {step}: {intoE:F4} m into the enemy, {intoF:F4} m into the anchored friendly");
        Assert.True(intoE <= 1e-4f, $"enemy entered {intoE:F4} m");
        Assert.True(intoF <= 1e-4f, $"anchored friendly entered {intoF:F4} m (the fallback dropped its clip)");
    }

    /// <summary>
    /// The same corner, but with no hard wall involved (both standing units friendly): the single clip
    /// leaves the anchored one at "no deeper". Pins the baseline the test above compares against.
    /// </summary>
    [Fact]
    public void Constrain_TwoAnchoredFriends_SingleClipKeepsTheLastOne()
    {
        Simulation sim = ThreeUnits();
        World w = sim.World;
        UnitStore u = w.Units;
        NavGrid g = w.NavGrid;
        for (int k = 0; k < 3; k++) u.Owner[k] = 1;
        var p = new Vector2(21f, 21f);
        Vector2 nA = new(0f, -1f), nB = new(0.6f, 0.8f);
        u.Position[2] = p;
        u.Position[0] = p + nA * (u.Radius[2] + u.Radius[0]);
        u.Position[1] = p + nB * (u.Radius[2] + u.Radius[1]);
        for (int k = 0; k < 2; k++)
        {
            g.WorldToCell(u.Position[k], out int x, out int y);
            u.GoalCell[k] = y * g.Width + x;
            u.Goal[k] = u.Position[k];
        }
        int walkerGoal = 2 * g.Width + 22;
        u.State[2] = UnitState.Moving;
        u.GoalCell[2] = walkerGoal;
        float s = 0.8f * u.Speed[2] / MathF.Sqrt(1.25f);
        var step = (Vector2)s_constrain.Invoke(null, new object[] { w, g, u, 2, p, new Vector2(s, -0.5f * s), new[] { 0, 1 }, 2, walkerGoal, false })!;
        _out.WriteLine($"step {step}: {Vector2.Dot(step, nA):F4} into A, {Vector2.Dot(step, nB):F4} into B");
        Assert.True(Vector2.Dot(step, nB) <= 1e-4f);
    }

    /// <summary>Three radius-0.4 units on a flat 24 x 24 map, spawned; the tests then place them and set owners and orders by hand.</summary>
    private static Simulation ThreeUnits()
    {
        int small = LocalMovementTests.TypeWithRadius(0.4f);
        var sim = new Simulation(TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: 3, CommandCapacity: 8), LocalMovementTests.Rows(Flat(24, 24)));
        for (int k = 0; k < 3; k++) sim.Enqueue(Command.SpawnUnit(0, small, new Vector2(5f + 4f * k, 5f)));
        sim.Tick();
        sim.Tick(); // commands queued before a tick apply at the start of the next
        Assert.Equal(3, sim.World.Units.Count);
        return sim;
    }

    private static string[] Flat(int w, int h)
    {
        var rows = new string[h];
        for (int y = 0; y < h; y++) rows[y] = new string('0', w);
        return rows;
    }

    // ---------------------------------------------------------------- scenarios

    /// <summary>A 48 x 24 map split by a cliff column at x = 20, open only through rows lo..hi.</summary>
    private static Heightmap GapMap(int lo, int hi)
    {
        var rows = new string[24];
        for (int y = 0; y < 24; y++)
        {
            char[] r = new string('0', 48).ToCharArray();
            if (y < lo || y > hi) r[20] = '1';
            rows[y] = new string(r);
        }
        return LocalMovementTests.Rows(rows);
    }

    private static int CountMoving(UnitStore u, int owner)
    {
        int n = 0;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.State[i] == UnitState.Moving && u.Owner[i] == owner) n++;
        return n;
    }

    /// <summary>
    /// Plugs of different widths, plug-unit sizes, and positions jittered up to 0.3 m off the cell
    /// centers (uneven slits, every one narrower than the narrowest walker): every tick, no walker
    /// ends deeper in an Idle enemy than it started, nobody passes, and everybody stops in bounded time.
    /// </summary>
    [Theory]
    [InlineData(2, 0.9f, 1UL)]
    [InlineData(2, 0.7f, 2UL)]
    [InlineData(3, 0.7f, 3UL)]
    [InlineData(3, 0.9f, 4UL)]
    [InlineData(4, 0.9f, 5UL)]
    [InlineData(4, 0.7f, 6UL)]
    [InlineData(5, 0.9f, 7UL)]
    public void PluggedGap_VariedWidthsSizesAndJitter_NoWalkerGoesDeeper_NobodyPasses(int width, float plugRadius, ulong seed)
    {
        const int crowd = 60;
        int lo = 12 - width / 2, hi = lo + width - 1;
        var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: crowd + width, CommandCapacity: 4 * crowd + 16), GapMap(lo, hi));
        NavGrid g = sim.World.NavGrid;
        var rng = new SimRng(seed, 351);
        for (int i = 0; i < crowd; i++)
            sim.Enqueue(Command.SpawnUnit(0, i % TestSim.UnitTypeCount, new Vector2(24.1f + rng.NextFloat() * 13.8f, 12.1f + rng.NextFloat() * 21.8f)));
        int plugType = LocalMovementTests.TypeWithRadius(plugRadius);
        for (int y = lo; y <= hi; y++)
        {
            // Jitter along x only, so neighbors stay closer than 2 r + 0.8 m (no walker fits between).
            var jitter = new Vector2((rng.NextFloat() - 0.5f) * 0.6f, 0f);
            sim.Enqueue(Command.SpawnUnit(1, plugType, g.CellCenter(20, y) + jitter));
        }
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        for (int i = 0; i < crowd; i++) sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, i), g.CellCenter(36, 11)));
        var plugAt = new Vector2[width];
        for (int k = 0; k < width; k++) plugAt[k] = u.Position[crowd + k];
        var wasMoving = new bool[crowd];
        var depth = new float[crowd, width];
        float slowest = float.MaxValue;
        foreach (var def in TestSim.Data.Units) slowest = MathF.Min(slowest, def.SpeedPerTick);
        int bound = (int)(16f / slowest) + 10 * MovementConstants.GiveUpTicks;
        int ticks = 0, deepest = 0;
        float worst = 0f;
        do
        {
            for (int i = 0; i < crowd; i++)
            {
                wasMoving[i] = u.State[i] == UnitState.Moving;
                for (int k = 0; k < width; k++) depth[i, k] = u.Radius[i] + u.Radius[crowd + k] - Vector2.Distance(u.Position[i], u.Position[crowd + k]);
            }
            sim.Tick();
            ticks++;
            Assert.Equal(-1, MoveScenario.FirstUnitOnBlockedGround(sim.World));
            for (int k = 0; k < width; k++) Assert.True(u.Position[crowd + k] == plugAt[k], "a plug unit moved");
            for (int i = 0; i < crowd; i++)
            {
                if (!wasMoving[i]) continue;
                for (int k = 0; k < width; k++)
                {
                    float now = u.Radius[i] + u.Radius[crowd + k] - Vector2.Distance(u.Position[i], u.Position[crowd + k]);
                    float deeper = now - MathF.Max(depth[i, k], 0f);
                    if (deeper > worst) worst = deeper;
                    Assert.True(deeper <= 1e-4f, $"tick {ticks}: walker {i} went {deeper:F4} m deeper into enemy {k}");
                }
            }
            for (int i = 0; i < crowd; i++)
                for (int k = 0; k < width; k++)
                    deepest = Math.Max(deepest, (int)(1000f * (u.Radius[i] + u.Radius[crowd + k] - Vector2.Distance(u.Position[i], u.Position[crowd + k]))));
        } while ((ticks < 2 || CountMoving(u, 0) > 0) && ticks < 4 * bound);
        int past = 0, gaveUp = 0;
        for (int i = 0; i < crowd; i++)
        {
            if (u.Position[i].X > 21 * MapConstants.CellSize) past++;
            if (u.GoalCell[i] == -1) gaveUp++;
        }
        _out.WriteLine($"width {width}, plug radius {plugRadius}, seed {seed}: stopped after {ticks} ticks (bound {bound}), past {past}, gave up {gaveUp}/{crowd}, worst per-tick deepening {worst:E2} m, deepest overlap with an enemy {deepest} mm");
        Assert.Equal(0, CountMoving(u, 0));
        Assert.Equal(0, past);
        Assert.True(ticks <= bound, $"{ticks} ticks > {bound}");
    }

    /// <summary>
    /// Open-field fuzz, both players walking and standing: 90 units per player (every type), a third
    /// of each army scattered Idle plus tight enemy pairs and triples (0.2 m slits), the rest re-ordered
    /// to random points every 120 ticks (so units switch between walking and standing), for 1,500
    /// ticks. Every tick: a unit that was Moving never ends deeper in an enemy that was Idle at the start
    /// of the tick and didn't move, unless the enemy holds the walker's own goal cell (BUG-0037: such an
    /// enemy counts as an arrived groupmate; those deepenings are only counted); positions finite,
    /// none on blocked ground. Also reports how often, and how far, a walker went deeper into a
    /// friendly unit standing on its own point (an anchor, which no walker may shove; BUG-0038).
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
    public void OpenField_TwoArmiesMixingWithIdleEnemyClusters_NoWalkerEverGoesDeeperIntoAStandingEnemy(ulong seed)
    {
        const int perPlayer = 90, clusters = 6;
        int cap = 2 * perPlayer + 2 * clusters * 3;
        var rows = new string[40];
        for (int y = 0; y < 40; y++)
        {
            char[] r = new string('0', 40).ToCharArray();
            // A few cliff blocks for walls and corners.
            if (y >= 8 && y <= 11 && r.Length > 12) { r[10] = '1'; r[11] = '1'; }
            if (y >= 25 && y <= 27) { r[28] = '1'; r[29] = '1'; r[30] = '1'; }
            rows[y] = new string(r);
        }
        var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: cap, CommandCapacity: 8 * cap), LocalMovementTests.Rows(rows));
        NavGrid g = sim.World.NavGrid;
        var rng = new SimRng(seed, 352);
        Vector2 RandomPassable(ref SimRng r)
        {
            while (true)
            {
                var at = new Vector2(2.1f + r.NextFloat() * 75.8f, 2.1f + r.NextFloat() * 75.8f);
                if (g.WorldToCell(at, out int x, out int y) && g.IsPassable(x, y)) return at;
            }
        }
        int wide = LocalMovementTests.TypeWithRadius(0.9f);
        for (int p = 0; p < 2; p++)
        {
            for (int i = 0; i < perPlayer; i++) sim.Enqueue(Command.SpawnUnit(p, i % TestSim.UnitTypeCount, RandomPassable(ref rng)));
            for (int c = 0; c < clusters; c++)
            {
                // Two or three wide units in a row, 2 m apart (0.2 m slits), standing for good.
                Vector2 at = RandomPassable(ref rng);
                bool vertical = rng.NextInt(0, 2) == 0;
                int len = 2 + rng.NextInt(0, 2);
                for (int k = 0; k < len; k++)
                {
                    Vector2 q = at + (vertical ? new Vector2(0f, 2f * k) : new Vector2(2f * k, 0f));
                    if (g.WorldToCell(q, out int x, out int y) && g.IsPassable(x, y)) sim.Enqueue(Command.SpawnUnit(p, wide, q));
                }
            }
        }
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        var wasMoving = new bool[cap];
        var wasIdle = new bool[cap];
        var before = new Vector2[cap];
        var goalBefore = new int[cap];
        var orderBefore = new int[cap];
        // Walkers: two thirds of each army's scattered units (spawned first per player, and spawns
        // apply in player order); the clusters and the other third stay put.
        var walkers = new List<int>();
        int[] seen = new int[2];
        for (int i = 0; i < cap; i++)
        {
            if (!u.Alive[i]) continue;
            int p = u.Owner[i];
            if (seen[p]++ < perPlayer && seen[p] % 3 != 0) walkers.Add(i);
        }
        float worst = 0f, worstSameGoal = 0f, worstAnchor = 0f;
        long checks = 0;
        int sameGoal = 0, anchorDeeper = 0;
        const float arrival2 = MovementConstants.ArrivalDistance * MovementConstants.ArrivalDistance;
        for (int t = 0; t < 1500; t++)
        {
            if (t % 120 == 0)
            {
                foreach (int i in walkers)
                    sim.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), RandomPassable(ref rng)));
            }
            for (int i = 0; i < cap; i++)
            {
                wasMoving[i] = u.Alive[i] && u.State[i] == UnitState.Moving;
                wasIdle[i] = u.Alive[i] && u.State[i] == UnitState.Idle;
                before[i] = u.Position[i];
                goalBefore[i] = u.GoalCell[i];
                orderBefore[i] = u.OrderTick[i];
            }
            sim.Tick();
            Assert.Equal(-1, MoveScenario.FirstUnitOnBlockedGround(sim.World));
            for (int i = 0; i < cap; i++)
            {
                if (!u.Alive[i]) continue;
                Assert.True(float.IsFinite(u.Position[i].X) && float.IsFinite(u.Position[i].Y), $"tick {t}: unit {i} at {u.Position[i]}");
                if (!wasMoving[i]) continue;
                for (int j = 0; j < cap; j++)
                {
                    // Standing for the whole tick: Idle before and after, not moved, and no order applied
                    // this tick (a fresh order makes it Moving during the plan, a documented soft wall).
                    if (!u.Alive[j] || !wasIdle[j] || u.State[j] != UnitState.Idle || u.Position[j] != before[j] || u.OrderTick[j] != orderBefore[j]) continue;
                    float sum = u.Radius[i] + u.Radius[j];
                    float was = sum - Vector2.Distance(before[i], before[j]);
                    float now = sum - Vector2.Distance(u.Position[i], u.Position[j]);
                    if (now <= 0f) continue;
                    float deeper = now - MathF.Max(was, 0f);
                    if (u.Owner[j] == u.Owner[i])
                    {
                        // A friendly anchor (on its own point, a goal other than the walker's): a wall the walker can't shove.
                        if (goalBefore[j] >= 0 && goalBefore[j] != goalBefore[i] && Vector2.DistanceSquared(before[j], u.Goal[j]) <= arrival2 && deeper > 1e-2f)
                        {
                            anchorDeeper++;
                            worstAnchor = MathF.Max(worstAnchor, deeper);
                        }
                        continue;
                    }
                    if (goalBefore[j] >= 0 && goalBefore[j] == goalBefore[i])
                    {
                        if (deeper > 1e-4f) sameGoal++;
                        worstSameGoal = MathF.Max(worstSameGoal, deeper);
                        continue;
                    }
                    checks++;
                    if (deeper > worst) worst = deeper;
                    Assert.True(deeper <= 1e-4f, $"tick {t}: walker {i} (owner {u.Owner[i]}) went {deeper:F4} m deeper into standing enemy {j} (was {was:F3}, now {now:F3}; goal cells {u.GoalCell[i]} / {u.GoalCell[j]}, state now {u.State[i]})");
                }
            }
        }
        _out.WriteLine($"seed {seed}: 1,500 ticks, {checks} walker-in-enemy contacts checked, worst deepening {worst:E2} m; "
            + $"same-goal enemies entered deeper {sameGoal} times (worst {worstSameGoal:F3} m); friendly anchors entered > 1 cm deeper {anchorDeeper} times (worst {worstAnchor:F3} m)");
    }

    /// <summary>
    /// Shoves are held to <see cref="MovementConstants.ShoveSpacing"/> (0.5 x the radii's sum) from
    /// standing units, enemies included, and a shove is clipped once per neighbor. Idle friendlies with no
    /// goal (they yield to any shove) stand packed in front of a 3-cell gap plugged by three wide enemies
    /// (0.2 m slits); a crowd of walkers pushes them toward the gap. A unit's own step can't get it past
    /// the plug now; this measures whether being shoved can. Report only.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void ShovedIdleFriendliesAtAPluggedGap_Report(ulong seed)
    {
        const int crowd = 50, parked = 15;
        int cap = crowd + parked + 3;
        var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: cap, CommandCapacity: 4 * cap), GapMap(10, 12));
        NavGrid g = sim.World.NavGrid;
        var rng = new SimRng(seed, 353);
        // Parked, never ordered: cells x 18-19, rows 9-13, small units (radius 0.4).
        int small = LocalMovementTests.TypeWithRadius(0.4f);
        for (int i = 0; i < parked; i++)
            sim.Enqueue(Command.SpawnUnit(0, small, new Vector2(36.6f + (i % 3) * 1.2f, 19.0f + (i / 3) * 1.2f)));
        for (int i = 0; i < crowd; i++)
            sim.Enqueue(Command.SpawnUnit(0, i % TestSim.UnitTypeCount, new Vector2(16.1f + rng.NextFloat() * 18f, 12.1f + rng.NextFloat() * 21.8f)));
        int wide = LocalMovementTests.TypeWithRadius(0.9f);
        for (int y = 10; y <= 12; y++) sim.Enqueue(Command.SpawnUnit(1, wide, g.CellCenter(20, y)));
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        for (int i = parked; i < parked + crowd; i++) sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, i), g.CellCenter(36, 11)));
        int ticks = 0;
        float deepest = 0f;
        do
        {
            sim.Tick();
            ticks++;
            Assert.Equal(-1, MoveScenario.FirstUnitOnBlockedGround(sim.World));
            for (int i = 0; i < parked + crowd; i++)
                for (int k = parked + crowd; k < cap; k++)
                    deepest = MathF.Max(deepest, u.Radius[i] + u.Radius[k] - Vector2.Distance(u.Position[i], u.Position[k]));
        } while ((ticks < 2 || CountMoving(u, 0) > 0) && ticks < 3000);
        int parkedPast = 0, walkersPast = 0;
        for (int i = 0; i < parked + crowd; i++)
            if (u.Position[i].X > 21 * MapConstants.CellSize) { if (i < parked) parkedPast++; else walkersPast++; }
        _out.WriteLine($"seed {seed}: stopped after {ticks} ticks; parked units past the plug {parkedPast}/{parked}, walkers past {walkersPast}/{crowd}; deepest overlap with an enemy {deepest:F3} m");
        Assert.Equal(0, CountMoving(u, 0));
    }

    /// <summary>
    /// docs/03: other players' Idle units are hard walls, a step never goes deeper into one. An enemy
    /// that arrived at a point (Idle, holding that goal cell) and a walker of the other player ordered
    /// to a point in the same cell, walking straight at it: the walker must stop at touching. It
    /// counts the enemy as an arrived groupmate instead (soft push only), walks into it and "arrives"
    /// overlapping it, anchored to the goal through the enemy.
    /// </summary>
    [Fact]
    public void IdleEnemyHoldingTheWalkersGoalCell_IsStillAHardWall()
    {
        var sim = new Simulation(TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: 2, CommandCapacity: 8), LocalMovementTests.Rows(Flat(24, 24)));
        NavGrid g = sim.World.NavGrid;
        Vector2 p = g.CellCenter(15, 10);
        int fastest = 0;
        for (int t = 1; t < TestSim.UnitTypeCount; t++)
            if (TestSim.Data.Units[t].Radius == 0.4f && TestSim.Data.Units[t].SpeedPerTick > TestSim.Data.Units[fastest].SpeedPerTick) fastest = t;
        sim.Enqueue(Command.SpawnUnit(0, fastest, p - new Vector2(8f, 0f)));
        sim.Enqueue(Command.SpawnUnit(1, LocalMovementTests.TypeWithRadius(0.9f), p));
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        int walker = u.Owner[0] == 0 ? 0 : 1, enemy = 1 - walker;
        sim.Enqueue(Command.Move(1, MoveScenario.Handle(sim, enemy), p));
        for (int t = 0; t < 5; t++) sim.Tick();
        Assert.Equal(UnitState.Idle, u.State[enemy]);
        Assert.Equal(10 * g.Width + 15, u.GoalCell[enemy]);
        Vector2 enemyAt = u.Position[enemy];
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, walker), p + new Vector2(0.6f, 0f)));
        float worst = 0f;
        for (int t = 0; t < 200; t++)
        {
            bool moving = u.State[walker] == UnitState.Moving;
            float sum = u.Radius[walker] + u.Radius[enemy];
            float was = sum - Vector2.Distance(u.Position[walker], u.Position[enemy]);
            sim.Tick();
            Assert.True(u.Position[enemy] == enemyAt, "the enemy moved");
            float now = sum - Vector2.Distance(u.Position[walker], u.Position[enemy]);
            if (moving) worst = MathF.Max(worst, now - MathF.Max(was, 0f));
        }
        _out.WriteLine($"walker ended {u.State[walker]}, goal cell {u.GoalCell[walker]}, overlapping the enemy by {u.Radius[walker] + u.Radius[enemy] - Vector2.Distance(u.Position[walker], u.Position[enemy]):F3} m; worst per-tick deepening {worst:F3} m");
        Assert.True(worst <= 1e-4f, $"the walker went {worst:F3} m deeper into the Idle enemy in one tick");
    }

    /// <summary>The plugged-gap run hashes the same in two sims after every tick (the fallback reads only start-of-tick state).</summary>
    [Fact]
    public void PluggedGap_TwoSims_HashEqualEveryTick()
    {
        Simulation Make()
        {
            var sim = new Simulation(TestSim.Config(Seed: 9, PlayerCount: 2, UnitCapacity: 63, CommandCapacity: 300), GapMap(10, 12));
            NavGrid g = sim.World.NavGrid;
            var rng = new SimRng(9, 354);
            for (int i = 0; i < 60; i++)
                sim.Enqueue(Command.SpawnUnit(0, i % TestSim.UnitTypeCount, new Vector2(24.1f + rng.NextFloat() * 13.8f, 12.1f + rng.NextFloat() * 21.8f)));
            for (int y = 10; y <= 12; y++) sim.Enqueue(Command.SpawnUnit(1, LocalMovementTests.TypeWithRadius(0.9f), g.CellCenter(20, y)));
            sim.Tick();
            sim.Tick();
            for (int i = 0; i < 60; i++) sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, i), g.CellCenter(36, 11)));
            return sim;
        }
        Simulation a = Make(), b = Make();
        for (int t = 0; t < 600; t++)
        {
            a.Tick();
            b.Tick();
            Assert.Equal(a.StateHash(), b.StateHash());
        }
    }
}
