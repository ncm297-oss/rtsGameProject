using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;
using Rts.Sim.Pathfinding;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA attacks on M1-4d-2 shoving: shove legality every tick (who may move without an order, how
/// far, where to), wall abuse, groups shoving each other back and forth, anchor drift, enemy and
/// Moving units never shoved, determinism under crossing crowds, and the give-up rows measured with
/// one owner (no enemies in the way).
/// </summary>
public class ShoveQaTests
{
    private readonly ITestOutputHelper _out;

    public ShoveQaTests(ITestOutputHelper output) => _out = output;

    // ---------- helpers ----------

    internal static int CountMoving(UnitStore u)
    {
        int n = 0;
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.State[i] == UnitState.Moving) n++;
        return n;
    }

    /// <summary>Start-of-tick copy of what the shove checker needs.</summary>
    internal sealed class Before
    {
        public readonly Vector2[] Pos;
        public readonly UnitState[] State;
        public readonly int[] OrderTick;
        public readonly int[] Generation;
        public readonly bool[] Alive;

        public Before(int capacity)
        {
            Pos = new Vector2[capacity];
            State = new UnitState[capacity];
            OrderTick = new int[capacity];
            Generation = new int[capacity];
            Alive = new bool[capacity];
        }

        public void Capture(UnitStore u)
        {
            Array.Copy(u.Position, Pos, Pos.Length);
            Array.Copy(u.State, State, State.Length);
            Array.Copy(u.OrderTick, OrderTick, OrderTick.Length);
            Array.Copy(u.Generation, Generation, Generation.Length);
            Array.Copy(u.Alive, Alive, Alive.Length);
        }
    }

    /// <summary>
    /// After one tick in which no unit got a command: every unit that was Idle and still is, and
    /// moved, was shoved, so it must have moved at most its speed, kept velocity 0 and its order tick,
    /// stand on passable ground, and a Moving unit of its own player must have started the tick
    /// within touching-plus-one-step of it (shoves never chain and never cross players). Every unit
    /// that was Moving and still is moved by exactly its own velocity. Returns an error or null.
    /// </summary>
    internal static string? CheckShoves(World w, Before b, ref int shoves)
    {
        UnitStore u = w.Units;
        NavGrid g = w.NavGrid;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!b.Alive[i] || !u.Alive[i] || u.Generation[i] != b.Generation[i]) continue;
            Vector2 p = u.Position[i];
            if (b.State[i] == UnitState.Moving && u.State[i] == UnitState.Moving && p != b.Pos[i] + u.Velocity[i])
                return $"Moving unit {i} went {b.Pos[i]} -> {p} but stepped {u.Velocity[i]}";
            if (b.State[i] != UnitState.Idle || u.State[i] != UnitState.Idle || p == b.Pos[i]) continue;
            shoves++;
            float d = Vector2.Distance(p, b.Pos[i]);
            if (d > u.Speed[i] + 1e-4f) return $"Idle unit {i} shoved {d:F4} m > speed {u.Speed[i]:F4}";
            if (u.Velocity[i] != Vector2.Zero) return $"shoved unit {i} has velocity {u.Velocity[i]}";
            if (u.OrderTick[i] != b.OrderTick[i]) return $"shoved unit {i} order tick {b.OrderTick[i]} -> {u.OrderTick[i]}";
            if (!g.WorldToCell(p, out int cx, out int cy) || !g.IsPassable(cx, cy)) return $"shoved unit {i} onto blocked ground at {p}";
            bool walker = false;
            for (int k = 0; k < u.Capacity && !walker; k++)
            {
                // A walker: Moving at the start of the tick, or given its Move this tick (commands apply first).
                bool moving = b.State[k] == UnitState.Moving || u.State[k] == UnitState.Moving || u.OrderTick[k] != b.OrderTick[k];
                if (!b.Alive[k] || !u.Alive[k] || !moving || u.Owner[k] != u.Owner[i]) continue;
                float reach = u.Radius[i] + u.Radius[k] + u.Speed[k] + 0.01f;
                if (Vector2.DistanceSquared(b.Pos[k], b.Pos[i]) <= reach * reach) walker = true;
            }
            if (!walker)
            {
                int nearest = -1;
                float nd = float.MaxValue;
                for (int k = 0; k < u.Capacity; k++)
                {
                    if (!b.Alive[k] || b.State[k] != UnitState.Moving) continue;
                    float dk = Vector2.Distance(b.Pos[k], b.Pos[i]);
                    if (dk < nd) { nd = dk; nearest = k; }
                }
                string who = nearest < 0 ? "none" : $"unit {nearest} (player {u.Owner[nearest]}, r {u.Radius[nearest]}, speed {u.Speed[nearest]:F3}) {nd:F3} m away";
                return $"Idle unit {i} (player {u.Owner[i]}, r {u.Radius[i]}, goal cell {u.GoalCell[i]}) moved {b.Pos[i]} -> {p} with no friendly walker in reach; nearest Moving: {who}";
            }
        }
        return null;
    }

    /// <summary>Idle units keeping a goal cell that are not linked to their point (MoveScenario.Arrived); first one described.</summary>
    internal static int StrayAnchors(World w, out string? first)
    {
        bool[] arrived = MoveScenario.Arrived(w);
        UnitStore u = w.Units;
        int n = 0;
        first = null;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.State[i] != UnitState.Idle || u.GoalCell[i] < 0 || arrived[i]) continue;
            n++;
            first ??= $"unit {i} at {u.Position[i]}, {Vector2.Distance(u.Position[i], u.Goal[i]):F2} m from its point {u.Goal[i]}";
        }
        return n;
    }

    /// <summary>Spawns <paramref name="count"/> units at random points of random cells of <paramref name="cells"/>, owner by <paramref name="ownerOf"/>.</summary>
    private static void SpawnIn(Simulation sim, SimRng rng, List<int> cells, int count, Func<int, int> ownerOf)
    {
        NavGrid g = sim.World.NavGrid;
        for (int k = 0; k < count; k++)
        {
            Vector2 corner = MoveScenario.Center(g, cells[rng.NextInt(0, cells.Count)]) - new Vector2(MapConstants.CellSize / 2);
            sim.Enqueue(Command.SpawnUnit(ownerOf(k), k % TestSim.UnitTypeCount, corner + new Vector2(0.05f + rng.NextFloat() * 1.9f, 0.05f + rng.NextFloat() * 1.9f)));
        }
    }

    /// <summary>
    /// <paramref name="blob"/> units settled at the map's central point (owners by
    /// <paramref name="blobOwner"/>: "same" = player 0, "enemy" = player 1, "mixed" = alternating),
    /// then <paramref name="walkers"/> player-0 units spawned 20-32 m west, their Moves to 18 m east of
    /// the point queued (not yet applied). Slots: blob first, walkers after.
    /// </summary>
    internal static Simulation Crossing(ulong seed, int blob, int walkers, string blobOwner, out Vector2 target)
    {
        int cap = blob + walkers;
        var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: cap, CommandCapacity: 2 * cap + 8));
        NavGrid g = sim.World.NavGrid;
        int goalCell = MoveScenario.CentralCell(g);
        Vector2 point = MoveScenario.Center(g, goalCell);
        FlowField field = FlowField.Build(g, goalCell);
        float maxCost = blob >= 1500 ? 60f : blob >= 600 ? 40f : 25f;
        var near = new List<int>();
        var west = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
        {
            float cost = field.CostAt(c);
            if (cost <= maxCost) near.Add(c);
            if (cost >= 10f && cost <= 16f && MoveScenario.Center(g, c).X < point.X - 12f) west.Add(c);
        }
        Assert.NotEmpty(west);
        var rng = new SimRng(seed, 17);
        SpawnIn(sim, rng, near, blob, k => blobOwner == "same" ? 0 : blobOwner == "enemy" ? 1 : k % 2);
        sim.Tick();
        sim.Tick();
        MoveScenario.MoveAll(sim, point);
        sim.Tick();
        UnitStore u = sim.World.Units;
        int t = 0;
        do { sim.Tick(); t++; } while (CountMoving(u) > 0 && t < 5000);
        Assert.Equal(0, CountMoving(u));
        SpawnIn(sim, rng, west, walkers, _ => 0);
        sim.Tick();
        sim.Tick();
        Assert.Equal(cap, u.Count);
        target = point + new Vector2(18f, 0f);
        for (int i = blob; i < cap; i++) sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, i), target));
        return sim;
    }

    // ---------- shove legality under crossing crowds ----------

    /// <summary>
    /// QA focus "enemy and Moving units never move without an order" and "anchor drift": 200 walkers
    /// cross a settled 300-unit blob of their own player, of the enemy, or mixed. Every tick: the
    /// shove checker (no shove without a friendly walker in reach, at most own speed, order tick and
    /// velocity kept, passable ground, Moving units move by exactly their step) and no stray anchor.
    /// After all stop: no stray anchor, nobody on blocked ground, then 200 more ticks with no order
    /// and no unit moves at all (no jitter).
    /// </summary>
    [Theory]
    [InlineData("same", 73UL)]
    [InlineData("same", 74UL)]
    [InlineData("enemy", 73UL)]
    [InlineData("mixed", 73UL)]
    [InlineData("mixed", 75UL)]
    public void WalkersCrossingASettledBlob_ShoveRulesAndAnchorsHoldEveryTick_ThenNoJitter(string blobOwner, ulong seed)
    {
        Simulation sim = Crossing(seed, 300, 200, blobOwner, out _);
        World w = sim.World;
        UnitStore u = w.Units;
        var blobStart = (Vector2[])u.Position.Clone();
        var b = new Before(u.Capacity);
        sim.Tick(); // the Moves apply (order ticks change this tick)
        int shoves = 0, ticks = 1, worstStray = 0;
        string? firstStray = null;
        do
        {
            b.Capture(u);
            sim.Tick();
            ticks++;
            string? err = CheckShoves(w, b, ref shoves);
            Assert.True(err == null, $"tick {ticks}: {err}");
            int stray = StrayAnchors(w, out string? s);
            if (stray > worstStray) { worstStray = stray; firstStray = $"tick {ticks}: {s}"; }
        } while (CountMoving(u) > 0 && ticks < 4000);
        int enemyMoved = 0, blobMoved = 0;
        for (int i = 0; i < 300; i++)
        {
            if (u.Position[i] != blobStart[i]) blobMoved++;
            if (u.Owner[i] != 0 && u.Position[i] != blobStart[i]) enemyMoved++;
        }
        int arrived = 0, gaveUp = 0;
        bool[] arr = MoveScenario.Arrived(w);
        for (int i = 300; i < 500; i++) { if (arr[i]) arrived++; else if (u.GoalCell[i] < 0) gaveUp++; }
        _out.WriteLine($"{blobOwner}/{seed}: stopped after {ticks} ticks; {shoves} shove unit-ticks, {blobMoved} blob units moved ({enemyMoved} enemy); walkers arrived {arrived}, gave up {gaveUp}; worst stray anchors in one tick {worstStray} {firstStray}");
        Assert.Equal(0, CountMoving(u));
        Assert.Equal(0, enemyMoved);
        Assert.True(worstStray == 0, $"stray anchors: {worstStray}, {firstStray}");
        Assert.Equal(-1, MoveScenario.FirstUnitOnBlockedGround(w));
        if (blobOwner == "same") Assert.True(blobMoved > 10, "the walkers did not shove the blob");
        var settled = (Vector2[])u.Position.Clone();
        ulong h = sim.StateHash();
        for (int t = 0; t < 200; t++) sim.Tick();
        for (int i = 0; i < u.Capacity; i++) Assert.True(u.Position[i] == settled[i], $"unit {i} moved with no walker: {settled[i]} -> {u.Position[i]}");
    }

    /// <summary>
    /// Units waiting for a field under the build cap (Moving, velocity 0) among walkers and Idle
    /// units: the shove checker every tick. The scenario is the criterion-6 row of
    /// <c>FieldBuildCapQaTests.BuildCap_500UnitsWith500DistinctGoals_...</c> (seed 5018, same draws),
    /// with two players as there and with one (every Idle unit shovable). Reports the give-up rate
    /// against criterion 6 (at most 3%).
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(1)]
    public void BuildCap_500UnitsTo500RandomGoals_ShoveRulesHoldEveryTick_ReportGiveUps(int players)
    {
        const ulong seed = 5018;
        var sim = new Simulation(TestSim.Config(seed, players, UnitCapacity: 500, CommandCapacity: 2 * 500 + 64));
        World w = sim.World;
        NavGrid g = w.NavGrid;
        List<int> passable = FlowFieldOracle.PassableCells(g);
        var spawnRng = new SimRng(seed, 900);
        for (int i = 0; i < 500; i++)
        {
            int c = passable[spawnRng.NextInt(0, passable.Count)];
            Vector2 corner = MoveScenario.Center(g, c) - new Vector2(MapConstants.CellSize / 2);
            sim.Enqueue(Command.SpawnUnit(i % players, i % TestSim.UnitTypeCount, corner + new Vector2(0.05f + spawnRng.NextFloat() * 1.9f, 0.05f + spawnRng.NextFloat() * 1.9f)));
        }
        sim.Tick();
        sim.Tick();
        UnitStore u = w.Units;
        var rng = new SimRng(seed, 3);
        for (int i = 0; i < u.Capacity; i++)
            sim.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), MoveScenario.Center(g, passable[rng.NextInt(0, passable.Count)])));
        sim.Tick();
        var b = new Before(u.Capacity);
        int shoves = 0, ticks = 1, waitingTicks = 0;
        do
        {
            b.Capture(u);
            sim.Tick();
            ticks++;
            for (int i = 0; i < u.Capacity; i++) if (u.State[i] == UnitState.Moving && u.Velocity[i] == Vector2.Zero) waitingTicks++;
            string? err = CheckShoves(w, b, ref shoves);
            Assert.True(err == null, $"tick {ticks}: {err}");
        } while (CountMoving(u) > 0 && ticks < 30_000);
        bool[] arrived = MoveScenario.Arrived(w);
        int arrivedCount = 0, gaveUp = 0;
        for (int i = 0; i < u.Capacity; i++) { if (arrived[i]) arrivedCount++; else if (u.GoalCell[i] < 0) gaveUp++; }
        int stray = StrayAnchors(w, out string? first);
        _out.WriteLine($"{players} player(s): {ticks} ticks, {waitingTicks} standing-Moving unit-ticks, {shoves} shoves; arrived {arrivedCount}, gave up {gaveUp} ({gaveUp * 100.0 / u.Capacity:F1}%), stray {stray} {first}");
        Assert.Equal(0, CountMoving(u));
        Assert.Equal(0, stray);
    }

    /// <summary>
    /// Criterion 6's 128-units-to-64-neighboring-goals row (<c>MovementSystemTests.MoreGoalsThanCacheSlots_...</c>,
    /// seed 21) with every unit owned by player 0: reports give-ups against the 5% target; shove checker every tick.
    /// </summary>
    [Fact]
    public void MoreGoalsThanCacheSlots_OneOwner_ShoveRulesHold_ReportGiveUps()
    {
        Simulation sim = MoveScenario.Spawn(seed: 21, units: 128, maxCost: 15f, out int center);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++) u.Owner[i] = 0; // test seam: one player
        FlowField near = FlowField.Build(g, center);
        var goals = new List<int>();
        for (int c = 0; c < g.Width * g.Height && goals.Count < 64; c++) if (near.CostAt(c) <= 15f) goals.Add(c);
        for (int i = 0; i < u.Capacity; i++)
            sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, i), MoveScenario.Center(g, goals[i % goals.Count])));
        sim.Tick();
        var b = new Before(u.Capacity);
        int shoves = 0, ticks = 0;
        do
        {
            b.Capture(u);
            sim.Tick();
            ticks++;
            string? err = CheckShoves(w, b, ref shoves);
            Assert.True(err == null, $"tick {ticks}: {err}");
        } while (CountMoving(u) > 0 && ticks < 3000);
        bool[] arrived = MoveScenario.Arrived(w);
        int arrivedCount = 0, gaveUp = 0;
        for (int i = 0; i < u.Capacity; i++) { if (arrived[i]) arrivedCount++; else if (u.GoalCell[i] < 0) gaveUp++; }
        _out.WriteLine($"128 units (one owner), 64 neighboring goals: {ticks} ticks, {shoves} shoves; arrived {arrivedCount}, gave up {gaveUp} ({gaveUp * 100.0 / u.Capacity:F1}%)");
        Assert.Equal(0, CountMoving(u));
        Assert.Equal(0, StrayAnchors(w, out _));
    }

    // ---------- shove abuse ----------

    /// <summary>A 16 x 40 map with a cliff column at cell x = 4 (x 8..10 m) on rows 1..38.</summary>
    private static Heightmap LongCliffColumn()
    {
        var rows = new string[40];
        for (int y = 0; y < 40; y++) rows[y] = y >= 1 && y <= 38 ? "0000100000000000" : new string('0', 16);
        return LocalMovementTests.Rows(rows);
    }

    /// <summary>
    /// QA focus "shove abuse": a lone friendly unit (no goal) hugs a cliff; a column of 6 walkers
    /// (radius 0.7) marches along the wall past it, turned round every 300 ticks, for 1,200 ticks.
    /// Every tick: the lone unit stays Idle with velocity 0 and its order tick, never on blocked
    /// ground, its disk never deeper into the cliff than at the start, moves at most its speed, and
    /// stays within the map. Reports its total and net displacement.
    /// </summary>
    [Theory]
    [InlineData(10.45f, 0.4f)]
    [InlineData(10.95f, 0.9f)]
    [InlineData(10.05f, 0.4f)] // already overlapping the cliff by 0.35 m (walker-style center-only placement)
    public void WalkerColumnAlongAWall_ShovingALoneUnit_1200Ticks_NeverIntoTheCliff(float x, float radius)
    {
        Simulation sim = LocalMovementTests.SimOn(LongCliffColumn(), 7);
        int lonerType = LocalMovementTests.TypeWithRadius(radius), walkerType = LocalMovementTests.TypeWithRadius(0.7f);
        Vector2 start = new(x, 40f);
        sim.Enqueue(Command.SpawnUnit(0, lonerType, start));
        for (int k = 0; k < 6; k++) sim.Enqueue(Command.SpawnUnit(0, walkerType, new Vector2(10.8f, 6f + 1.6f * k)));
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        NavGrid g = sim.World.NavGrid;
        int orderTick = u.OrderTick[0];
        float startDepth = MathF.Max(10f - (start.X - radius), 0f);
        float path = 0f, worstDepth = 0f, worstStep = 0f, worstOverlap = 0f;
        int legs = 0, legArrivals = 0;
        Vector2 last = start;
        int moves = 0;
        for (int t = 0; t < 1200; t++)
        {
            if (t % 300 == 0)
            {
                Vector2 to = (t / 300) % 2 == 0 ? new Vector2(10.8f, 74f) : new Vector2(10.8f, 4f);
                for (int k = 1; k <= 6; k++) sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, k), to));
            }
            sim.Tick();
            Vector2 p = u.Position[0];
            Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y), $"tick {t}: {p}");
            Assert.Equal(UnitState.Idle, u.State[0]);
            Assert.Equal(Vector2.Zero, u.Velocity[0]);
            Assert.Equal(orderTick, u.OrderTick[0]);
            Assert.True(g.WorldToCell(p, out int cx, out int cy) && g.IsPassable(cx, cy), $"tick {t}: lone unit on blocked ground at {p}");
            Assert.Equal(-1, MoveScenario.FirstUnitOnBlockedGround(sim.World));
            float step = Vector2.Distance(p, last);
            Assert.True(step <= u.Speed[0] + 1e-4f, $"tick {t}: lone unit moved {step:F4} m > speed {u.Speed[0]:F4}");
            // Cliff x 8..10 on rows 1..38 (y 2..78): the disk may not go deeper into it than it started.
            if (p.Y > 2f && p.Y < 78f)
            {
                float depth = MathF.Max(10f - (p.X - radius), 0f);
                worstDepth = MathF.Max(worstDepth, depth);
                Assert.True(depth <= startDepth + 1e-4f, $"tick {t}: lone unit at {p} overlaps the cliff by {depth:F3} m (started at {startDepth:F3})");
            }
            for (int k = 1; k <= 6; k++)
                worstOverlap = MathF.Max(worstOverlap, u.Radius[0] + u.Radius[k] - Vector2.Distance(p, u.Position[k]));
            if (t % 300 == 299)
            {
                legs++;
                for (int k = 1; k <= 6; k++) if (u.State[k] == UnitState.Idle && u.GoalCell[k] >= 0) legArrivals++;
            }
            if (step > 0f) moves++;
            worstStep = MathF.Max(worstStep, step);
            path += step;
            last = p;
        }
        _out.WriteLine($"x {x} r {radius}: shoved on {moves} ticks, path {path:F2} m, net {Vector2.Distance(last, start):F2} m, worst step {worstStep:F3}, worst cliff depth {worstDepth:F3} (start {startDepth:F3}); ends at {last}; worst walker overlap {worstOverlap:F3} m; walkers holding their goal at leg ends {legArrivals}/{legs * 6}");
    }

    /// <summary>
    /// QA focus "two groups shoving each other's blobs back and forth": two groups of 60 (one player)
    /// at points 10 m apart swap points 4 times through each other. Each swap must terminate (all
    /// Idle within 1,500 ticks), the shove checker holds every tick, no stray anchors; then 300
    /// ticks with no order and nothing moves.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void TwoFriendlyGroups_SwappingPointsThroughEachOther_Terminate_NoJitterAfter(int players)
    {
        Simulation sim = LocalMovementTests.SimOn(LocalMovementTests.Flat(40), 120, players);
        Vector2 a = new(30f, 40f), bPoint = new(50f, 40f);
        var rng = new SimRng(11, 4);
        for (int k = 0; k < 120; k++)
        {
            Vector2 c = k < 60 ? a : bPoint;
            sim.Enqueue(Command.SpawnUnit(players == 1 ? 0 : k % 2, k % TestSim.UnitTypeCount, c + new Vector2(rng.NextFloat() * 8f - 4f, rng.NextFloat() * 8f - 4f)));
        }
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        var before = new Before(u.Capacity);
        int shoves = 0;
        for (int round = 0; round < 5; round++)
        {
            bool swap = round % 2 == 1;
            for (int i = 0; i < 120; i++)
                sim.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), (i < 60) ^ swap ? a : bPoint));
            sim.Tick();
            sim.Tick();
            int t = 0;
            while (CountMoving(u) > 0 && t < 1500)
            {
                before.Capture(u);
                sim.Tick();
                t++;
                string? err = CheckShoves(sim.World, before, ref shoves);
                Assert.True(err == null, $"round {round} tick {t}: {err}");
            }
            int stray = StrayAnchors(sim.World, out string? first);
            bool[] arr = MoveScenario.Arrived(sim.World);
            int arrived = 0;
            for (int i = 0; i < 120; i++) if (arr[i]) arrived++;
            _out.WriteLine($"round {round}: settled in {t} ticks, arrived {arrived}/120, stray {stray}, shoves so far {shoves}");
            Assert.Equal(0, CountMoving(u));
            Assert.True(stray == 0, first);
            Assert.Equal(-1, MoveScenario.FirstUnitOnBlockedGround(sim.World));
        }
        var settled = (Vector2[])u.Position.Clone();
        for (int t = 0; t < 300; t++) sim.Tick();
        for (int i = 0; i < u.Capacity; i++) Assert.True(u.Position[i] == settled[i], $"unit {i} jittered");
    }

    /// <summary>A 1-cell-wide (2 m) corridor along cell row 2 (y 4..6 m), cells x 1..22, cliffs above and below.</summary>
    private static Heightmap Corridor() => LocalMovementTests.Rows(
        "000000000000000000000000",
        "111111111111111111111111",
        "000000000000000000000000",
        "111111111111111111111111",
        "000000000000000000000000");

    /// <summary>
    /// Criterion 1 / the brief's goal ("standing Idle friendly units step aside for walkers") for a
    /// friendly unit that was *ordered* into a 1-cell corridor and arrived there (keeps its goal
    /// cell, within ArrivalDistance of its point), not just one with no goal. Two wide units (radius
    /// 0.9) can't pass side by side: the walker must get through (the parked unit may lose its goal).
    /// </summary>
    [Theory]
    [InlineData(0f)]   // parked exactly on its point
    [InlineData(0.9f)] // parked 0.9 m east of its point (within ArrivalDistance)
    public void WalkerInOneCellCorridor_PastAFriendlyUnitParkedThereByAMove_Arrives(float offset)
    {
        Simulation sim = LocalMovementTests.SimOn(Corridor(), 2);
        NavGrid g = sim.World.NavGrid;
        int type = LocalMovementTests.TypeWithRadius(0.9f);
        Vector2 parked = g.CellCenter(8, 2), goal = g.CellCenter(14, 2);
        sim.Enqueue(Command.SpawnUnit(0, type, g.CellCenter(2, 2)));
        sim.Enqueue(Command.SpawnUnit(0, type, parked + new Vector2(offset, 0f)));
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 1), parked));
        sim.Tick();
        sim.Tick();
        Assert.Equal(UnitState.Idle, u.State[1]);
        Assert.True(u.GoalCell[1] >= 0, "the parked unit did not arrive");
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), goal));
        sim.Tick();
        int ticks = 0;
        do { sim.Tick(); ticks++; } while (u.State[0] == UnitState.Moving && ticks < 1000);
        bool through = Vector2.Distance(u.Position[0], goal) <= MovementConstants.ArrivalDistance;
        _out.WriteLine($"parked {offset} m off its point: walker {(through ? "arrived" : "gave up")} after {ticks} ticks at {u.Position[0]} (goal cell {u.GoalCell[0]}); parked unit at {u.Position[1]}, goal cell {u.GoalCell[1]}");
        Assert.Equal(UnitState.Idle, u.State[0]);
        Assert.True(through, $"walker stopped at {u.Position[0]}, {Vector2.Distance(u.Position[0], goal):F2} m short");
        Assert.Equal(-1, MoveScenario.FirstUnitOnBlockedGround(sim.World));
    }

    // ---------- give-up rows with one owner (criterion 6 without enemies) ----------

    /// <summary>
    /// Criterion 6's crowd rows (500 / 2,500 units to 4 points 6 m apart) with every unit owned by
    /// player 0, so every Idle unit in a walker's way can be shoved: reports arrived / gave up, to
    /// test the claim that the misses come from enemy blobs. Asserts only termination and anchors.
    /// </summary>
    [Theory]
    [InlineData(500, 3000)]
    [InlineData(2500, 6000)]
    public void Crowd_ToFourClosePoints_OneOwner_Report(int units, int limit)
    {
        Simulation sim = MoveScenario.Spawn(seed: (ulong)(900 + units + 4), units: units, maxCost: units > 1000 ? 70f : 40f, out int goalCell);
        World w = sim.World;
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++) u.Owner[i] = 0; // test seam: one player
        Vector2 c = MoveScenario.Center(w.NavGrid, goalCell);
        // The stress row's layout: 4 points 6 m apart round the central cell.
        var targets = new[] { c + new Vector2(-3f, -3f), c + new Vector2(3f, -3f), c + new Vector2(-3f, 3f), c + new Vector2(3f, 3f) };
        for (int i = 0; i < u.Capacity; i++) sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, i), targets[i % 4]));
        sim.Tick();
        int ticks = 0;
        do { sim.Tick(); ticks++; } while (CountMoving(u) > 0 && ticks < limit);
        bool[] arr = MoveScenario.Arrived(w);
        int arrived = 0, gaveUp = 0;
        for (int i = 0; i < u.Capacity; i++) { if (arr[i]) arrived++; else if (u.GoalCell[i] < 0) gaveUp++; }
        int stray = StrayAnchors(w, out string? first);
        _out.WriteLine($"{units} units (one owner) to 4 points: settled after {ticks} ticks; arrived {arrived} ({arrived * 100.0 / units:F1}%), gave up {gaveUp}, stray {stray} {first}");
        Assert.Equal(0, CountMoving(u));
        Assert.Equal(0, stray);
    }

    // ---------- determinism ----------

    /// <summary>
    /// Two sims, 150 walkers crossing a settled 300 blob (mixed owners) while every tick random
    /// spawns, frees and retargets land among them: hash-equal every tick; another seed differs.
    /// </summary>
    [Fact]
    public void Determinism_CrossingCrowdWithInterleavedSpawnsFreesRetargets_HashEqualEveryTick()
    {
        ulong[] Run(ulong seed)
        {
            Simulation sim = Crossing(seed, 300, 150, "same", out Vector2 target);
            UnitStore u = sim.World.Units;
            NavGrid g = sim.World.NavGrid;
            Vector2 c = target - new Vector2(18f, 0f);
            var rng = new SimRng(seed, 91);
            var hashes = new ulong[900];
            for (int t = 0; t < hashes.Length; t++)
            {
                int n = rng.NextInt(0, 6);
                for (int k = 0; k < n; k++)
                {
                    int slot = rng.NextInt(0, u.Capacity);
                    int op = rng.NextInt(0, 10);
                    if (op < 2) sim.Enqueue(Command.SpawnUnit(0, rng.NextInt(0, TestSim.UnitTypeCount), c + new Vector2(rng.NextFloat() * 6f - 3f, rng.NextFloat() * 6f - 3f)));
                    else if (op < 9 && u.Alive[slot]) sim.Enqueue(Command.Move(u.Owner[slot], MoveScenario.Handle(sim, slot), c + new Vector2(rng.NextInt(-3, 4) * 3f, rng.NextInt(-3, 4) * 3f)));
                    else if (u.Alive[slot] && t % 3 == 0) u.Free(MoveScenario.Handle(sim, slot));
                }
                sim.Tick();
                hashes[t] = sim.StateHash();
            }
            return hashes;
        }
        ulong[] x = Run(31), y = Run(31);
        for (int t = 0; t < x.Length; t++) Assert.True(x[t] == y[t], $"diverged at tick {t}");
        Assert.NotEqual(x[^1], Run(32)[^1]);
    }

    /// <summary>
    /// Three walkers of one goal (radius 0.4, not touching each other) each overlap one friendly Idle
    /// unit with no goal (radius 0.9) by 0.05 m, from the west, south-west and north-west. Run with
    /// the walkers in slot order 1,2,3 and reversed. docs/03 says shoves are order-independent: on
    /// the first tick, whenever the three walkers end bit-equal in both orders, the shoved unit must too.
    /// </summary>
    [Fact]
    public void ThreeWalkersShovingOneUnit_SameTick_SlotOrderDoesNotChangeTheShove()
    {
        Vector2[] Run(bool reversed)
        {
            Simulation sim = LocalMovementTests.SimOn(LocalMovementTests.Flat(32), 4);
            Vector2 idle = new(31.013f, 31.007f);
            var walkers = new Vector2[3];
            for (int k = 0; k < 3; k++)
            {
                float a = MathF.PI * (0.75f + 0.25f * k);
                walkers[k] = idle + new Vector2(MathF.Cos(a), MathF.Sin(a)) * 1.25f;
            }
            sim.Enqueue(Command.SpawnUnit(0, LocalMovementTests.TypeWithRadius(0.9f), idle));
            for (int k = 0; k < 3; k++) sim.Enqueue(Command.SpawnUnit(0, LocalMovementTests.TypeWithRadius(0.4f), walkers[reversed ? 2 - k : k]));
            sim.Tick();
            sim.Tick();
            for (int s = 1; s <= 3; s++) sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, s), new Vector2(50f, 31f)));
            sim.Tick();
            sim.Tick(); // the Moves apply, the walkers plan and shove
            UnitStore u = sim.World.Units;
            var r = new Vector2[4];
            r[0] = u.Position[0];
            for (int k = 0; k < 3; k++) r[1 + k] = u.Position[reversed ? 3 - k : 1 + k];
            return r;
        }
        Vector2[] a = Run(false), b = Run(true);
        for (int k = 0; k < 4; k++) _out.WriteLine($"unit {k}: {a[k].X:R},{a[k].Y:R} vs reversed {b[k].X:R},{b[k].Y:R}");
        Assert.True(a[0] != new Vector2(31.013f, 31.007f), "not shoved");
        bool walkersEqual = a[1] == b[1] && a[2] == b[2] && a[3] == b[3];
        _out.WriteLine($"walkers bit-equal: {walkersEqual}");
        if (walkersEqual) Assert.True(a[0] == b[0], $"slot order changed only the shove: {a[0]} vs {b[0]}");
    }

    /// <summary>
    /// Measuring: the crossing crowd (same owner) with spawns in forward and reversed order; reports
    /// the first tick the per-identity states differ (docs/03: a walker's own sums are slot-ordered).
    /// </summary>
    [Fact]
    public void ReversedSpawnOrder_CrossingCrowd_FirstDivergence_Report()
    {
        (ulong[] ById, int Shoved) Run(bool reversed)
        {
            const int blob = 120, walkers = 60, n = blob + walkers;
            var spawns = new List<Vector2>();
            var rng = new SimRng(5, 5);
            for (int k = 0; k < blob; k++) spawns.Add(new Vector2(40f + rng.NextFloat() * 8f, 40f + rng.NextFloat() * 8f));
            for (int k = 0; k < walkers; k++) spawns.Add(new Vector2(10f + rng.NextFloat() * 6f, 38f + rng.NextFloat() * 12f));
            Simulation sim = LocalMovementTests.SimOn(LocalMovementTests.Flat(48), n);
            int SlotOf(int k) => reversed ? n - 1 - k : k;
            for (int s = 0; s < n; s++)
            {
                int k = reversed ? n - 1 - s : s;
                sim.Enqueue(Command.SpawnUnit(0, k % TestSim.UnitTypeCount, spawns[k]));
            }
            sim.Tick();
            sim.Tick();
            UnitStore u = sim.World.Units;
            for (int k = 0; k < blob; k++) sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, SlotOf(k)), new Vector2(44f, 44f)));
            for (int t = 0; t < 400; t++) sim.Tick();
            Vector2[] settled = (Vector2[])u.Position.Clone();
            for (int k = blob; k < n; k++) sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, SlotOf(k)), new Vector2(80f, 44f)));
            var byId = new ulong[800];
            for (int t = 0; t < byId.Length; t++)
            {
                sim.Tick();
                ulong h = 14695981039346656037UL;
                for (int k = 0; k < n; k++)
                {
                    int i = SlotOf(k);
                    h = (h ^ BitConverter.SingleToUInt32Bits(u.Position[i].X)) * 1099511628211UL;
                    h = (h ^ BitConverter.SingleToUInt32Bits(u.Position[i].Y)) * 1099511628211UL;
                    h = (h ^ (uint)u.GoalCell[i]) * 1099511628211UL;
                }
                byId[t] = h;
            }
            int shoved = 0;
            for (int k = 0; k < blob; k++) if (u.Position[SlotOf(k)] != settled[SlotOf(k)]) shoved++;
            return (byId, shoved);
        }
        var f = Run(false);
        var r = Run(true);
        int first = -1;
        for (int t = 0; t < f.ById.Length && first < 0; t++) if (f.ById[t] != r.ById[t]) first = t;
        _out.WriteLine($"blob units shoved {f.Shoved} / {r.Shoved}; per-identity state first differs at crossing tick {first} (-1: never)");
    }

    // ---------- perf and allocation ----------

    /// <summary>Perf and allocation for shoving at 1x/2x/5x: this class runs alone (SerialCollection).</summary>
    [Collection(SerialCollection.Name)]
    public class Serial
    {
        private readonly ITestOutputHelper _out;

        public Serial(ITestOutputHelper output) => _out = output;

        private static (double Avg, double Worst) Time(Simulation sim, int ticks)
        {
            var times = new double[ticks];
            var sw = new Stopwatch();
            for (int t = 0; t < ticks; t++)
            {
                sw.Restart();
                sim.Tick();
                times[t] = sw.Elapsed.TotalMilliseconds;
            }
            return (times.Average(), times.Max());
        }

        /// <summary>Criterion 9 at 2x/5x: walkers crossing a settled blob (same owner, the most shoving). Avg under 4 ms at 500 (docs/03); 1,000 and 2,500 reported.</summary>
        [Trait("Category", "Perf")]
        [Theory]
        [InlineData(300, 200, true)]
        [InlineData(600, 400, false)]
        [InlineData(1500, 1000, false)]
        public void Perf_WalkersCrossingASameOwnerBlob(int blob, int walkers, bool enforce)
        {
            Simulation sim = Crossing(73, blob, walkers, "same", out _);
            for (int t = 0; t < 5; t++) sim.Tick();
            (double avg, double worst) = Time(sim, 400);
            _out.WriteLine($"{walkers} walkers crossing a {blob}-unit same-owner blob: avg {avg:F2} ms, worst {worst:F2} ms, {CountMoving(sim.World.Units)} still moving");
            if (enforce) Assert.True(avg < 4.0, $"avg {avg:F2} ms");
        }

        /// <summary>
        /// Many small arrived groups (2,000 units in groups of 4 at 500 points) crossed by 500 walkers:
        /// every shoved arrived unit re-checks its whole group, a full-capacity scan per distinct goal.
        /// </summary>
        [Trait("Category", "Perf")]
        [Fact]
        public void Perf_500WalkersThrough500SmallArrivedGroups()
        {
            var sim = new Simulation(TestSim.Config(Seed: 9, PlayerCount: 1, UnitCapacity: 2500, CommandCapacity: 6000), LocalMovementTests.Flat(96));
            var rng = new SimRng(9, 1);
            var points = new Vector2[500];
            for (int p = 0; p < 500; p++) points[p] = new Vector2(40f + (p % 25) * 4.5f + 0.7f, 40f + (p / 25) * 4.5f + 0.7f);
            for (int k = 0; k < 2000; k++)
                sim.Enqueue(Command.SpawnUnit(0, 0, points[k / 4] + new Vector2(rng.NextFloat() * 2.4f - 1.2f, rng.NextFloat() * 2.4f - 1.2f)));
            for (int k = 0; k < 500; k++)
                sim.Enqueue(Command.SpawnUnit(0, 0, new Vector2(6f + rng.NextFloat() * 20f, 40f + rng.NextFloat() * 90f)));
            sim.Tick();
            sim.Tick();
            UnitStore u = sim.World.Units;
            for (int k = 0; k < 2000; k++) sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, k), points[k / 4]));
            sim.Tick();
            int t0 = 0;
            do { sim.Tick(); t0++; } while (CountMoving(u) > 0 && t0 < 3000);
            for (int k = 2000; k < 2500; k++) sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, k), new Vector2(180f, 40f + (k - 2000) * 0.18f)));
            sim.Tick();
            for (int t = 0; t < 30; t++) sim.Tick();
            int goalLost = 0;
            (double avg, double worst) = Time(sim, 400);
            for (int k = 0; k < 2000; k++) if (u.GoalCell[k] < 0) goalLost++;
            _out.WriteLine($"500 walkers through 500 groups of 4 (settled in {t0} ticks): avg {avg:F2} ms, worst {worst:F2} ms, {goalLost} group units lost their goal, {CountMoving(u)} moving");
            Assert.True(avg < 8.0, $"avg {avg:F2} ms");
        }

        /// <summary>Criterion 8 at 5x: 1,000 walkers crossing a 1,500-unit same-owner blob allocate nothing; 2,500 units to 4 points (one owner) too.</summary>
        [Fact]
        public void Tick_1000WalkersCrossing1500Blob_AllocatesNothing()
        {
            Simulation sim = Crossing(73, 1500, 1000, "same", out _);
            for (int t = 0; t < 150; t++) sim.Tick();
            Action ticks = () => { for (int t = 0; t < 20; t++) sim.Tick(); };
            AllocationProbe.AssertZero(ticks, _out);
            for (int t = 0; t < 200; t++) sim.Tick();
            AllocationProbe.AssertZero(ticks, _out);
        }
    }
}
