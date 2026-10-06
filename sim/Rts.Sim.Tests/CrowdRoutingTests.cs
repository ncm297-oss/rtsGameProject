using System.Numerics;
using System.Reflection;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M1-4d-3 crowd routing (docs/03 "Local movement", "Implementation (M1-4d-3)"): the local detour,
/// queuing behind units waiting for a field, the one walk-back per order, parked lines yielding as a
/// chain, owner-aware groupmates (BUG-0037), the hard-wall fallback keeping friendly clips (BUG-0038)
/// and a re-order inside an arrived unit's goal cell (BUG-0030).
/// </summary>
public class CrowdRoutingTests
{
    private readonly ITestOutputHelper _out;

    public CrowdRoutingTests(ITestOutputHelper output) => _out = output;

    private static bool PendingWalkBack(UnitStore u)
    {
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.WalkBack[i] >= UnitStore.WalkBackPending) return true;
        return false;
    }

    private static int CountMoving(UnitStore u)
    {
        int n = 0;
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.State[i] == UnitState.Moving) n++;
        return n;
    }

    // ---------- detour ----------

    /// <summary>Seven radius-0.4 units in a hexagon round <paramref name="c"/>, 0.8 m apart (touching).</summary>
    private static Vector2[] Hexagon(Vector2 c)
    {
        var at = new Vector2[7];
        at[0] = c;
        for (int k = 0; k < 6; k++)
        {
            float a = k * MathF.PI / 3f;
            at[k + 1] = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * 0.8f;
        }
        return at;
    }

    /// <summary>
    /// One radius-0.4 walker crossing a flat map along y = 41 (cell row 20's centers, which it follows)
    /// past a standing hexagon of units (another player's, or friendly units with no goal) centred on
    /// (41, 41 - <paramref name="offset"/>): a positive offset puts the walker on the blob's +y side.
    /// Returns the walker's position every tick (by identity, whatever the spawn order) and the closest
    /// its center came to a blob unit's center.
    /// </summary>
    private static (Vector2[] Path, float Closest, bool Arrived) WalkPastHexagon(float offset, int blobOwner, bool reversed)
    {
        Vector2[] blob = Hexagon(new Vector2(41f, 41f - offset));
        int small = LocalMovementTests.TypeWithRadius(0.4f);
        var sim = new Simulation(TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: 8, CommandCapacity: 64), LocalMovementTests.Flat(40));
        var spawns = new (int Owner, Vector2 At)[8];
        for (int k = 0; k < 7; k++) spawns[k] = (blobOwner, blob[k]);
        spawns[7] = (0, new Vector2(25f, 41f));
        // Commands apply by (player, sequence), so reversing the spawn order reverses the slots within each player.
        for (int k = 0; k < 8; k++)
        {
            (int owner, Vector2 at) = spawns[reversed ? 7 - k : k];
            sim.Enqueue(Command.SpawnUnit(owner, small, at));
        }
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        int walker = -1;
        for (int i = 0; i < 8; i++) if (u.Position[i] == spawns[7].At) walker = i;
        Vector2 goal = new(57f, 41f);
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, walker), goal));
        sim.Tick();
        var path = new List<Vector2>();
        float closest = float.MaxValue;
        for (int t = 0; t < 400 && (t < 2 || u.State[walker] == UnitState.Moving); t++)
        {
            sim.Tick();
            path.Add(u.Position[walker]);
            for (int i = 0; i < 8; i++)
                if (i != walker) closest = MathF.Min(closest, Vector2.Distance(u.Position[walker], u.Position[i]));
        }
        bool arrived = u.State[walker] == UnitState.Idle && Vector2.Distance(u.Position[walker], goal) <= MovementConstants.ArrivalDistance;
        return (path.ToArray(), closest, arrived);
    }

    /// <summary>
    /// The detour goes round the side with the shorter arc toward the aim (ties right, here -y for a
    /// walker heading +x), passes without ever touching the blob, and arrives; an enemy blob and a
    /// friendly one with no goal alike. Before M1-4d-3 the walker walked into the blob and slid round it.
    /// </summary>
    [Theory]
    [InlineData(0.3f, 1, 1)]
    [InlineData(-0.3f, 1, -1)]
    [InlineData(0f, 1, -1)]
    [InlineData(0.3f, 0, 1)]
    [InlineData(-0.3f, 0, -1)]
    public void WalkerMeetingAStandingBlobInTheOpen_GoesRoundTheNearerSide_WithoutTouching(float offset, int blobOwner, int side)
    {
        (Vector2[] path, float closest, bool arrived) = WalkPastHexagon(offset, blobOwner, reversed: false);
        Vector2 abreast = path.First(p => p.X >= 41f);
        _out.WriteLine($"offset {offset}, blob owner {blobOwner}: passed the blob at y {abreast.Y:F2}, closest center {closest:F3} m (touching 0.8), arrived {arrived} after {path.Length} ticks");
        Assert.True(side > 0 ? abreast.Y > 41f - offset + 0.8f : abreast.Y < 41f - offset - 0.8f, $"passed at y {abreast.Y:F2}, expected the {(side > 0 ? "+y" : "-y")} side");
        Assert.True(closest >= 0.8f - 1e-3f, $"touched the blob: centers {closest:F3} m apart");
        Assert.True(arrived, "the walker didn't arrive");
    }

    /// <summary>Criterion 4: the detour's side and path don't depend on slot order (start-of-tick reads, order-free sums): reversed spawns give bit-equal positions.</summary>
    [Theory]
    [InlineData(0.3f)]
    [InlineData(0f)]
    [InlineData(-0.1f)]
    public void Detour_ReversedSpawnOrder_BitEqualPositions(float offset)
    {
        Vector2[] a = WalkPastHexagon(offset, 1, reversed: false).Path;
        Vector2[] b = WalkPastHexagon(offset, 1, reversed: true).Path;
        Assert.Equal(a.Length, b.Length);
        for (int t = 0; t < a.Length; t++) Assert.True(a[t] == b[t], $"tick {t}: {a[t]} vs {b[t]}");
    }

    // ---------- units waiting for a field are queued behind, not walls to give up at ----------

    /// <summary>
    /// A 24 x 14 map: open rows 1-7, a 1-cell corridor on row 9 (cliffs on rows 8 and 10) joined to the
    /// open area at cell (22, 8).
    /// </summary>
    private static Heightmap CorridorUnderAField()
    {
        var rows = new string[14];
        for (int y = 0; y < 14; y++)
            rows[y] = y == 8 ? new string('1', 22) + "01" : y == 10 ? new string('1', 24) : new string('0', 24);
        return LocalMovementTests.Rows(rows);
    }

    /// <summary>
    /// BUG-0028 b: a walker in a 1-cell corridor right behind a unit that is Moving but waiting for its
    /// field under the build cap (60 newer goals queue ahead of that field, about 30 ticks; the cache
    /// holds them all, so the wait ends). The walker holds its count (all but one tick in
    /// QueueOnWaitStride) instead of giving up after GiveUpTicks, follows once the field is built,
    /// and arrives. Before M1-4d-3 the waiting unit was a wall and the walker gave up.
    /// </summary>
    [Fact]
    public void WalkerBehindAUnitWaitingForItsField_Queues_ThenFollowsAndArrives()
    {
        const int others = 60;
        // Capacity 1,024 gives the field cache 128 slots: all 61 goals fit, so the wait is a queue, not churn.
        var sim = new Simulation(TestSim.Config(Seed: 1, PlayerCount: 1, UnitCapacity: 1024, CommandCapacity: 4 * others + 16), CorridorUnderAField());
        NavGrid g = sim.World.NavGrid;
        Assert.True(g.IsPassable(22, 8) && !g.IsPassable(21, 8) && g.IsPassable(5, 9) && !g.IsPassable(5, 10));
        int wide = LocalMovementTests.TypeWithRadius(0.9f), small = LocalMovementTests.TypeWithRadius(0.4f);
        sim.Enqueue(Command.SpawnUnit(0, wide, g.CellCenter(4, 9)));   // slot 0: the walker
        sim.Enqueue(Command.SpawnUnit(0, wide, g.CellCenter(5, 9)));   // slot 1: the unit ahead, soon waiting
        for (int k = 0; k < others; k++) sim.Enqueue(Command.SpawnUnit(0, small, g.CellCenter(1 + k % 22, 1 + k / 22)));
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        Vector2 walkerGoal = g.CellCenter(20, 9);
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), walkerGoal));
        sim.Tick();
        sim.Tick(); // the walker's order applies first, so its field is built first
        // 60 distinct goals in the open area, then the unit ahead's goal: the highest cell, built last.
        for (int k = 0; k < others; k++)
            sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 2 + k), g.CellCenter(1 + (k * 7) % 22, 1 + k % 6)));
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 1), g.CellCenter(22, 7)));
        sim.Tick();
        int waitedWhileTouching = 0, ticks = 0, maxStuck = 0;
        do
        {
            sim.Tick();
            ticks++;
            bool waiting = u.State[1] == UnitState.Moving && u.Velocity[1] == Vector2.Zero && !sim.World.FlowFields.Contains(u.GoalCell[1]);
            if (waiting && Vector2.Distance(u.Position[0], u.Position[1]) < u.Radius[0] + u.Radius[1] + 0.1f) waitedWhileTouching++;
            maxStuck = Math.Max(maxStuck, u.StuckTicks[0]);
        } while (u.State[0] == UnitState.Moving && ticks < 2000);
        _out.WriteLine($"walker blocked by a field-waiting unit for {waitedWhileTouching} ticks (max stuck count {maxStuck}); idle after {ticks} ticks at {u.Position[0]}, goal cell {u.GoalCell[0]}");
        Assert.True(waitedWhileTouching > MovementConstants.GiveUpTicks, $"the scenario blocked the walker for only {waitedWhileTouching} ticks");
        Assert.True(Vector2.Distance(u.Position[0], walkerGoal) <= MovementConstants.ArrivalDistance, $"the walker stopped at {u.Position[0]} (goal cell {u.GoalCell[0]})");
        Assert.NotEqual(-1, u.GoalCell[0]);
    }

    // ---------- walk-back: once per order ----------

    /// <summary>A 1-cell corridor along cell row 2, cells x 1..22.</summary>
    private static Heightmap Corridor() => LocalMovementTests.Rows(
        "000000000000000000000000",
        "111111111111111111111111",
        "000000000000000000000000",
        "111111111111111111111111",
        "000000000000000000000000");

    /// <summary>
    /// A unit parked alone in a 1-cell corridor is pushed far off its point by a blocked walker going
    /// the same way; the anchor re-check drops its goal; once nobody has shoved it for
    /// WalkBackDelayTicks it walks back (Moving, same goal cell, order tick kept). Coming back it meets
    /// the walker parked on its own point, pushes it off in turn, and gets home; the walker walks back
    /// too and pushes it off again: now its walk-back is used, so it stays put without a goal. Every
    /// unit walks back at most once and everything stops (no corridor ping-pong). Before M1-4d-3 a unit
    /// cut off its blob stayed where the shove left it.
    /// </summary>
    [Fact]
    public void UnitShovedOffItsPoint_WalksBackOnce_AndNoMore()
    {
        Simulation sim = LocalMovementTests.SimOn(Corridor(), 2);
        NavGrid g = sim.World.NavGrid;
        int type = LocalMovementTests.TypeWithRadius(0.9f);
        Vector2 parked = g.CellCenter(8, 2), walkerGoal = g.CellCenter(14, 2);
        sim.Enqueue(Command.SpawnUnit(0, type, g.CellCenter(2, 2)));
        sim.Enqueue(Command.SpawnUnit(0, type, parked));
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 1), parked));
        sim.Tick();
        sim.Tick();
        sim.Tick();
        int parkedCell = u.GoalCell[1], orderTick = u.OrderTick[1];
        Assert.True(u.State[1] == UnitState.Idle && parkedCell >= 0, "the unit did not park");
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), walkerGoal));
        sim.Tick();
        var walkBacks = new int[2];
        var wasIdle = new bool[2];
        int ticks = 0;
        do
        {
            for (int i = 0; i < 2; i++) wasIdle[i] = u.State[i] == UnitState.Idle;
            int[] before = { u.WalkBack[0], u.WalkBack[1] };
            sim.Tick();
            ticks++;
            for (int i = 0; i < 2; i++)
            {
                if (before[i] >= UnitStore.WalkBackPending && u.WalkBack[i] == UnitStore.WalkBackUsed)
                {
                    walkBacks[i]++;
                    Assert.True(wasIdle[i] && u.State[i] == UnitState.Moving, $"unit {i} used its walk-back without starting to walk");
                    Assert.Equal(i == 1 ? parkedCell : g.WorldToCell(walkerGoal, out int wx, out int wy) ? wy * g.Width + wx : -2, u.GoalCell[i]);
                    Assert.Equal(0, u.StuckTicks[i]);
                }
            }
            Assert.Equal(-1, MoveScenario.FirstUnitOnBlockedGround(sim.World));
        } while ((CountMoving(u) > 0 || u.WalkBack[0] >= UnitStore.WalkBackPending || u.WalkBack[1] >= UnitStore.WalkBackPending) && ticks < 3000);
        _out.WriteLine($"settled after {ticks} ticks; walk-backs {walkBacks[0]} / {walkBacks[1]}; walker at {u.Position[0]} goal cell {u.GoalCell[0]}, parked unit at {u.Position[1]} goal cell {u.GoalCell[1]}");
        Assert.Equal(0, CountMoving(u));
        Assert.Equal(1, walkBacks[1]);
        Assert.True(walkBacks[0] <= 1, $"the walker walked back {walkBacks[0]} times");
        Assert.Equal(orderTick, u.OrderTick[1]);
        Assert.True(ticks < 3000);
    }

    /// <summary>A new Move resets the walk-back: the unit may walk back once on the new order again.</summary>
    [Fact]
    public void NewOrder_ResetsTheWalkBack()
    {
        Simulation sim = LocalMovementTests.SimOn(LocalMovementTests.Flat(16), 1);
        sim.Enqueue(Command.SpawnUnit(0, LocalMovementTests.TypeWithRadius(0.4f), new Vector2(9f, 9f)));
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        u.WalkBack[0] = UnitStore.WalkBackUsed; // test seam: as if this order's walk-back was spent
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), new Vector2(15f, 9f)));
        sim.Tick();
        sim.Tick();
        Assert.Equal(UnitState.Moving, u.State[0]);
        Assert.Equal(UnitStore.WalkBackNone, u.WalkBack[0]);
    }

    // ---------- parked lines yield as a chain; a blob's point holds ----------

    /// <summary>
    /// BUG-0033: two units parked on one point in a 1-cell corridor (packed along it) and a walker
    /// blocked behind them. Once the walker has been stuck PushAfterStuckTicks the whole parked line
    /// yields together: on the first push tick both parked units move, the far one without touching
    /// the walker (a chain shove), and the walker gets through to its goal.
    /// </summary>
    [Fact]
    public void ParkedPairInACorridor_YieldsAsAChain_ToABlockedWalker()
    {
        Simulation sim = LocalMovementTests.SimOn(Corridor(), 3);
        NavGrid g = sim.World.NavGrid;
        int type = LocalMovementTests.TypeWithRadius(0.9f);
        Vector2 parked = g.CellCenter(8, 2), goal = g.CellCenter(14, 2);
        sim.Enqueue(Command.SpawnUnit(0, type, g.CellCenter(2, 2)));
        sim.Enqueue(Command.SpawnUnit(0, type, parked));
        sim.Enqueue(Command.SpawnUnit(0, type, parked + new Vector2(1.8f, 0f)));
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 1), parked));
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 2), parked));
        sim.Tick();
        int t0 = 0;
        do { sim.Tick(); t0++; } while (CountMoving(u) > 0 && t0 < 400);
        Assert.True(u.GoalCell[1] >= 0 && u.GoalCell[2] >= 0, "the pair did not park");
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), goal));
        sim.Tick();
        int ticks = 0, chainTicks = 0;
        do
        {
            Vector2 a = u.Position[1], b = u.Position[2], w = u.Position[0];
            sim.Tick();
            ticks++;
            // The far unit moved while the walker never came within touching of it: shoved through the near one.
            if (u.Position[2] != b && Vector2.Distance(w, b) > u.Radius[0] + u.Radius[2] + u.Speed[0] && u.Position[1] != a) chainTicks++;
            // Ticked until everything has settled, walk-backs included (BUG-0042, BUG-0047): the pair the
            // walker pushed past walks home afterwards and must not push it off its goal.
        } while ((CountMoving(u) > 0 || PendingWalkBack(u)) && ticks < 4000);
        _out.WriteLine($"settled after {ticks} ticks: walker at {u.Position[0]} (goal cell {u.GoalCell[0]}); {chainTicks} chain-shove ticks; pair at {u.Position[1]} / {u.Position[2]}");
        Assert.True(chainTicks > 0, "the far parked unit never moved with the near one");
        Assert.True(Vector2.Distance(u.Position[0], goal) <= MovementConstants.ArrivalDistance && u.GoalCell[0] >= 0, $"walker ends at {u.Position[0]}, goal cell {u.GoalCell[0]}");
    }

    /// <summary>
    /// The chain rule moves a parked line only if it holds every groupmate of its members: the point of
    /// a blob (groupmates touching it on every side) never yields, even to a walker blocked for long.
    /// </summary>
    [Fact]
    public void BlobPoint_WithGroupmatesAllRound_NeverYieldsToABlockedWalker()
    {
        Simulation sim = LocalMovementTests.SimOn(LocalMovementTests.Flat(24), 8);
        int small = LocalMovementTests.TypeWithRadius(0.4f);
        Vector2[] blob = Hexagon(new Vector2(24f, 24f));
        for (int k = 0; k < 7; k++) sim.Enqueue(Command.SpawnUnit(0, small, blob[k]));
        sim.Enqueue(Command.SpawnUnit(0, small, new Vector2(18f, 24f)));
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        Vector2 point = new(24f, 24f);
        for (int k = 0; k < 7; k++) sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, k), point));
        sim.Tick();
        int t = 0;
        do { sim.Tick(); t++; } while (CountMoving(u) > 0 && t < 400);
        Assert.Equal(0, CountMoving(u));
        int center = -1;
        for (int k = 0; k < 7; k++) if (Vector2.Distance(u.Position[k], point) <= MovementConstants.ArrivalDistance && (center < 0 || Vector2.Distance(u.Position[k], point) < Vector2.Distance(u.Position[center], point))) center = k;
        Assert.True(center >= 0);
        Vector2 centerAt = u.Position[center];
        // A walker aimed straight through the blob's point, to a goal just behind it.
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 7), centerAt + new Vector2(1.5f, 0f)));
        sim.Tick();
        int ticks = 0;
        do { sim.Tick(); ticks++; } while (u.State[7] == UnitState.Moving && ticks < 400);
        _out.WriteLine($"blob point {center} moved {Vector2.Distance(u.Position[center], centerAt):F3} m; walker idle after {ticks} ticks at {u.Position[7]}");
        Assert.True(Vector2.Distance(u.Position[center], point) <= MovementConstants.ArrivalDistance, "the blob's point was pushed off it");
        Assert.True(u.GoalCell[center] >= 0, "the blob's point lost its goal");
    }

    // ---------- owner-aware groupmates (BUG-0037) ----------

    /// <summary>
    /// BUG-0037: an enemy holding the walker's own goal point (same goal cell, another player) is a
    /// wall, not an arrived groupmate. The walker never goes deeper into it, never anchors to the goal
    /// through it, and, kept more than ArrivalDistance from the point, gives up there (GoalCell -1).
    /// Before, it counted the enemy as its blob and "arrived" overlapping it.
    /// </summary>
    [Fact]
    public void EnemyHoldingTheWalkersGoalPoint_IsAWall_NotAnAnchor()
    {
        var sim = new Simulation(TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: 2, CommandCapacity: 8), LocalMovementTests.Flat(24));
        NavGrid g = sim.World.NavGrid;
        Vector2 p = g.CellCenter(15, 10);
        sim.Enqueue(Command.SpawnUnit(0, LocalMovementTests.TypeWithRadius(0.4f), p - new Vector2(8f, 0f)));
        sim.Enqueue(Command.SpawnUnit(1, LocalMovementTests.TypeWithRadius(0.9f), p));
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.Move(1, MoveScenario.Handle(sim, 1), p));
        for (int t = 0; t < 4; t++) sim.Tick();
        Assert.True(u.State[1] == UnitState.Idle && u.GoalCell[1] == 10 * g.Width + 15, "the enemy did not arrive at the point");
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), p + new Vector2(0.3f, 0f)));
        float worst = 0f;
        int ticks = 0;
        do
        {
            float was = u.Radius[0] + u.Radius[1] - Vector2.Distance(u.Position[0], u.Position[1]);
            bool moving = u.State[0] == UnitState.Moving;
            sim.Tick();
            ticks++;
            float now = u.Radius[0] + u.Radius[1] - Vector2.Distance(u.Position[0], u.Position[1]);
            if (moving) worst = MathF.Max(worst, now - MathF.Max(was, 0f));
        } while ((ticks < 2 || u.State[0] == UnitState.Moving) && ticks < 400);
        _out.WriteLine($"walker {u.State[0]} after {ticks} ticks at {u.Position[0]}, goal cell {u.GoalCell[0]}, {Vector2.Distance(u.Position[0], p):F2} m from the point; worst deepening {worst:E2}");
        Assert.True(worst <= 1e-4f, $"went {worst:F4} m deeper into the enemy");
        Assert.Equal(UnitState.Idle, u.State[0]);
        Assert.Equal(-1, u.GoalCell[0]);
        Assert.False(MoveScenario.Arrived(sim.World)[0]);
    }

    /// <summary>
    /// BUG-0037 in the anchor re-check: a friendly unit touching the point only through an enemy that
    /// holds the same goal cell is cut off when a shove makes the group re-check, never kept through the enemy.
    /// </summary>
    [Fact]
    public void AnchorRecheck_NeverLinksThroughAnEnemyOnTheSameGoalCell()
    {
        var sim = new Simulation(TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: 3, CommandCapacity: 8), LocalMovementTests.Flat(24));
        NavGrid g = sim.World.NavGrid;
        int small = LocalMovementTests.TypeWithRadius(0.4f);
        Vector2 p = g.CellCenter(10, 10);
        sim.Enqueue(Command.SpawnUnit(0, small, p + new Vector2(1.5f, 0f)));   // slot 0: friendly, linked only through the enemy
        sim.Enqueue(Command.SpawnUnit(0, small, new Vector2(30f, 30f)));       // slot 1: a friendly to shove it
        sim.Enqueue(Command.SpawnUnit(1, small, p + new Vector2(0.75f, 0f)));  // slot 2: enemy between it and the point
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        int cell = 10 * g.Width + 10;
        // Test seam: both settled as if they had arrived at p (same goal cell), Idle.
        u.GoalCell[0] = cell; u.Goal[0] = p;
        u.GoalCell[2] = cell; u.Goal[2] = p;
        MethodInfo recheck = typeof(MovementSystem).GetMethod("RecheckAnchors", BindingFlags.NonPublic | BindingFlags.Static)!;
        recheck.Invoke(null, new object[] { sim.World, cell });
        Assert.Equal(-1, u.GoalCell[0]);
        Assert.Equal(UnitStore.WalkBackPending, u.WalkBack[0]);
        Assert.Equal(cell, u.GoalCell[2]); // the enemy stands on the point itself
    }

    // ---------- hard-wall fallback keeps friendly clips (BUG-0038) ----------

    private static readonly MethodInfo s_constrain =
        typeof(MovementSystem).GetMethod("Constrain", BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// BUG-0038: when the hard-wall fallback fires (the slot-order clips slid a step into an enemy), the
    /// result still respects every friendly standing unit the step touches. Here two enemies and a
    /// friendly parked on its own point surround the walker; the desired step leans into all three.
    /// Before, the fallback restarted from the enemies alone and went 0.1+ m into the friendly.
    /// </summary>
    [Theory]
    [InlineData(0.6f, 0.8f, 1f, 0f)]
    [InlineData(-0.8f, 0.6f, 0.2f, -1f)]
    public void HardWallFallback_KeepsTheFriendlyClip(float fx, float fy, float dx, float dy)
    {
        var sim = new Simulation(TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: 4, CommandCapacity: 8), LocalMovementTests.Flat(24));
        int small = LocalMovementTests.TypeWithRadius(0.4f);
        for (int k = 0; k < 4; k++) sim.Enqueue(Command.SpawnUnit(k < 2 ? 1 : 0, small, new Vector2(5f + 4f * k, 5f)));
        sim.Tick();
        sim.Tick();
        World w = sim.World;
        UnitStore u = w.Units;
        NavGrid g = w.NavGrid;
        // Slots 0, 1: enemies (player 1); 2: the friendly; 3: the walker.
        var p = new Vector2(21f, 21f);
        Vector2 nE1 = Vector2.Normalize(new Vector2(0f, -1f)), nE2 = Vector2.Normalize(new Vector2(1f, -0.3f)), nF = Vector2.Normalize(new Vector2(fx, fy));
        u.Position[3] = p;
        u.Position[0] = p + nE1 * 0.8f;
        u.Position[1] = p + nE2 * 0.8f;
        u.Position[2] = p + nF * 0.8f;
        g.WorldToCell(u.Position[2], out int cx, out int cy);
        u.GoalCell[2] = cy * g.Width + cx;
        u.Goal[2] = u.Position[2];
        int walkerGoal = 2 * g.Width + 22;
        u.State[3] = UnitState.Moving;
        u.GoalCell[3] = walkerGoal;
        Vector2 desired = Vector2.Normalize(new Vector2(dx, dy) + nF) * 0.8f * u.Speed[3];
        var step = (Vector2)s_constrain.Invoke(null, new object[] { w, g, u, 3, p, desired, new[] { 0, 1, 2 }, 3, walkerGoal, false })!;
        float intoF = Vector2.Dot(step, nF), intoE1 = Vector2.Dot(step, nE1), intoE2 = Vector2.Dot(step, nE2);
        _out.WriteLine($"desired {desired}, step {step}: into enemies {intoE1:F4} / {intoE2:F4}, into the friendly {intoF:F4}");
        Assert.True(intoE1 <= 1e-4f && intoE2 <= 1e-4f, "entered an enemy");
        Assert.True(intoF <= 1e-4f, $"entered the parked friendly {intoF:F4} m");
    }

    // ---------- a re-order inside the goal cell (BUG-0030) ----------

    /// <summary>
    /// BUG-0030: an arrived unit ordered to another point of its own goal cell, farther than
    /// ArrivalDistance from where it was sent, takes a new order and walks there (a new order tick); a
    /// point within ArrivalDistance of its goal is the order it already has (click spam: no-op).
    /// </summary>
    [Fact]
    public void ArrivedUnit_ReorderedWithinItsGoalCell_MovesOnlyForAPointFartherThanArrivalDistance()
    {
        Simulation sim = LocalMovementTests.SimOn(LocalMovementTests.Flat(32), 1);
        sim.Enqueue(Command.SpawnUnit(0, LocalMovementTests.TypeWithRadius(0.4f), new Vector2(21f, 31f)));
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        Vector2 a = new(30.2f, 30.2f), near = a + new Vector2(0.6f, 0.6f), far = new(31.8f, 31.8f);
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), a));
        int t = 0;
        do { sim.Tick(); t++; } while ((t < 2 || u.State[0] == UnitState.Moving) && t < 600);
        Assert.Equal(UnitState.Idle, u.State[0]);
        int orderTick = u.OrderTick[0];
        Vector2 at = u.Position[0];
        // Within ArrivalDistance of its goal: the same order.
        for (int k = 0; k < 5; k++) { sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), near)); sim.Tick(); }
        sim.Tick();
        Assert.Equal(UnitState.Idle, u.State[0]);
        Assert.Equal(at, u.Position[0]);
        Assert.Equal(orderTick, u.OrderTick[0]);
        // Farther: a new order to the same cell.
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), far));
        sim.Tick();
        sim.Tick();
        Assert.Equal(UnitState.Moving, u.State[0]);
        Assert.NotEqual(orderTick, u.OrderTick[0]);
        t = 0;
        do { sim.Tick(); t++; } while (u.State[0] == UnitState.Moving && t < 600);
        Assert.True(Vector2.Distance(u.Position[0], far) <= MovementConstants.ArrivalDistance, $"stopped {Vector2.Distance(u.Position[0], far):F2} m from the new point");
    }

    /// <summary>
    /// A Moving unit re-ordered to jittered points of its goal cell every tick (an AI refreshing) keeps
    /// its order age and stuck count, and moving the goal never counts as progress (its best estimate
    /// drops by twice the shift): blocked from its point by an enemy standing on it, it still gives up
    /// within a bounded time instead of walking forever.
    /// </summary>
    [Fact]
    public void BlockedUnit_JitterReorderedInItsGoalCellEveryTick_StillGivesUp()
    {
        var sim = new Simulation(TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: 6, CommandCapacity: 64), LocalMovementTests.Flat(24));
        NavGrid g = sim.World.NavGrid;
        Vector2 center = g.CellCenter(12, 12);
        sim.Enqueue(Command.SpawnUnit(0, LocalMovementTests.TypeWithRadius(0.4f), center - new Vector2(3f, 0f)));
        // Five wide enemies on the goal cell's corners and center: no point of the cell is open to the walker.
        int wide = LocalMovementTests.TypeWithRadius(0.9f);
        sim.Enqueue(Command.SpawnUnit(1, wide, center));
        for (int k = 0; k < 4; k++) sim.Enqueue(Command.SpawnUnit(1, wide, center + new Vector2(k % 2 == 0 ? -1f : 1f, k < 2 ? -1f : 1f)));
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        var rng = new Determinism.SimRng(7, 1);
        Vector2 corner = center - new Vector2(1f, 1f);
        int ticks = 0, orderTick = -1;
        do
        {
            if (ticks < 2 || u.State[0] == UnitState.Moving)
                sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), corner + new Vector2(0.6f + rng.NextFloat() * 0.8f, 0.6f + rng.NextFloat() * 0.8f)));
            sim.Tick();
            ticks++;
            if (ticks == 3) orderTick = u.OrderTick[0];
            if (ticks > 3 && u.State[0] == UnitState.Moving) Assert.Equal(orderTick, u.OrderTick[0]);
        } while ((ticks < 3 || u.State[0] == UnitState.Moving) && ticks < 1000);
        _out.WriteLine($"jitter-reordered unit {u.State[0]} after {ticks} ticks at {u.Position[0]}, goal cell {u.GoalCell[0]}");
        Assert.Equal(UnitState.Idle, u.State[0]);
        Assert.True(ticks < 200, $"took {ticks} ticks to stop");
    }

    /// <summary>
    /// The rule behind the test above (BUG-0043 fix): a Moving unit re-ordered to another point of its
    /// goal cell keeps its order tick and stuck count, and its best estimate moves by exactly what the
    /// new point changes in the estimate where it stands: nothing far from the goal cell (there the
    /// estimate doesn't depend on the point; a 2 x shift penalty made walking units give up mid-route),
    /// and the exact difference inside the goal cell (so moving the point there can't pass for progress).
    /// </summary>
    [Fact]
    public void MovingUnit_RetargetedInItsGoalCell_KeepsItsOrder_BestMovesByTheEstimateShiftOnly()
    {
        Simulation sim = LocalMovementTests.SimOn(LocalMovementTests.Flat(32), 1);
        sim.Enqueue(Command.SpawnUnit(0, LocalMovementTests.TypeWithRadius(0.4f), new Vector2(11f, 31f)));
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        NavGrid g = sim.World.NavGrid;
        Vector2 a = new(30.2f, 30.4f), b = new(31.6f, 31.5f), c = new(30.4f, 31.8f);
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), a));
        for (int t = 0; t < 10; t++) sim.Tick();
        Assert.Equal(UnitState.Moving, u.State[0]);
        Assert.True(float.IsFinite(u.BestRemaining[0]));
        u.StuckTicks[0] = 3; // test seam: a count in progress
        int orderTick = u.OrderTick[0];
        MethodInfo apply = typeof(Simulation).GetMethod("ApplyMove", BindingFlags.NonPublic | BindingFlags.Instance)!;
        // Far from the goal cell: the best estimate is untouched.
        float bestFar = u.BestRemaining[0];
        apply.Invoke(sim, new object[] { Command.Move(0, MoveScenario.Handle(sim, 0), b) });
        Assert.Equal(b, u.Goal[0]);
        Assert.Equal(orderTick, u.OrderTick[0]);
        Assert.Equal(3, u.StuckTicks[0]);
        Assert.Equal(bestFar, u.BestRemaining[0]);
        // Inside the goal cell: it moves by the estimate's change at the unit's position.
        u.Position[0] = new Vector2(30.9f, 30.3f); // test seam: in the goal cell (15, 15)
        float bestIn = u.BestRemaining[0];
        Vector2 pos = u.Position[0], center = g.CellCenter(15, 15);
        float expected = (Vector2.Distance(c, pos) - Vector2.Distance(c, center)) - (Vector2.Distance(b, pos) - Vector2.Distance(b, center));
        apply.Invoke(sim, new object[] { Command.Move(0, MoveScenario.Handle(sim, 0), c) });
        Assert.Equal(bestIn + expected, u.BestRemaining[0], 4);
    }

    // ---------- determinism ----------

    /// <summary>Ticks two sims built the same way side by side until neither moves (or the limit), asserting equal state hashes every tick; returns the ticks run.</summary>
    private static int TwinRun(Func<Simulation> make, int limit)
    {
        Simulation a = make(), b = make();
        Assert.Equal(a.StateHash(), b.StateHash());
        int t = 0;
        do
        {
            a.Tick();
            b.Tick();
            t++;
            Assert.True(a.StateHash() == b.StateHash(), $"hashes differ at tick {t}");
        } while ((t < 3 || CountMoving(a.World.Units) > 0) && t < limit);
        return t;
    }

    /// <summary>Criterion 4: 500 units to 4 points, two players (one per point): two sims hash equal every tick.</summary>
    [Fact]
    public void Crowd500ToFourPoints_TwoPlayers_TwoSimsHashEqualEveryTick()
    {
        Simulation Make()
        {
            Simulation sim = MoveScenario.Spawn(3, 500, 40f, out int goalCell);
            Vector2[] goals = CrowdRows.FourPoints(MoveScenario.Center(sim.World.NavGrid, goalCell));
            UnitStore u = sim.World.Units;
            for (int i = 0; i < u.Capacity; i++)
                sim.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), goals[CrowdRows.PointOf(u, i, true)]));
            return sim;
        }
        int ticks = TwinRun(Make, 3000);
        _out.WriteLine($"{ticks} ticks, equal every tick");
    }

    /// <summary>Criterion 4: the parked pair in the corridor, chain-shoved and walking back: two sims hash equal every tick.</summary>
    [Fact]
    public void CorridorPair_TwoSimsHashEqualEveryTick()
    {
        Simulation Make()
        {
            Simulation sim = LocalMovementTests.SimOn(Corridor(), 3);
            NavGrid g = sim.World.NavGrid;
            int type = LocalMovementTests.TypeWithRadius(0.9f);
            Vector2 parked = g.CellCenter(8, 2);
            sim.Enqueue(Command.SpawnUnit(0, type, g.CellCenter(2, 2)));
            sim.Enqueue(Command.SpawnUnit(0, type, parked));
            sim.Enqueue(Command.SpawnUnit(0, type, parked + new Vector2(1.8f, 0f)));
            sim.Tick();
            sim.Tick();
            sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 1), parked));
            sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 2), parked));
            for (int t = 0; t < 60; t++) sim.Tick();
            sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), g.CellCenter(14, 2)));
            return sim;
        }
        int ticks = TwinRun(Make, 3000);
        _out.WriteLine($"{ticks} ticks, equal every tick");
    }

    /// <summary>Criterion 4: 200 walkers crossing a settled 300-unit blob of mixed owners (PreMix(73), the target row): two sims hash equal every tick.</summary>
    [Fact]
    public void BlobCrossing_TwoSimsHashEqualEveryTick()
    {
        int ticks = TwinRun(() => QA.ShoveQaTests.Crossing(TestSeeds.PreMix(73), 300, 200, "mixed", out _), 4000);
        _out.WriteLine($"{ticks} ticks, equal every tick");
    }

    /// <summary>This class's allocation test: runs alone in <see cref="SerialCollection"/>.</summary>
    [Collection(SerialCollection.Name)]
    public class Serial
    {
        private readonly ITestOutputHelper _out;

        public Serial(ITestOutputHelper output) => _out = output;

        /// <summary>
        /// Criterion 4: 2,500 units to 4 points (one player per point) mid-crowd, detouring, queuing,
        /// chain-shoving and walking back, allocate 0 bytes per tick.
        /// </summary>
        [Fact]
        public void Crowd2500ToFourPoints_WithDetoursAndWalkBacks_AllocatesNothing()
        {
            Simulation? sim = null;
            // A fresh, ordered sim for each measured run, so a re-run repeats exactly the same ticks.
            Action setup = () =>
            {
                Simulation s = MoveScenario.Spawn(1, 2500, 70f, out int goalCell);
                Vector2[] goals = CrowdRows.FourPoints(MoveScenario.Center(s.World.NavGrid, goalCell));
                UnitStore u0 = s.World.Units;
                for (int i = 0; i < u0.Capacity; i++)
                    s.Enqueue(Command.Move(u0.Owner[i], MoveScenario.Handle(s, i), goals[CrowdRows.PointOf(u0, i, true)]));
                sim = s;
            };
            Action ticks = () => { for (int t = 0; t < 1000; t++) sim!.Tick(); };
            int runs = AllocationProbe.AssertZero(ticks, _out, setup);
            UnitStore u = sim!.World.Units;
            int walkBacks = 0, moving = 0;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (u.WalkBack[i] == UnitStore.WalkBackUsed) walkBacks++;
                if (u.State[i] == UnitState.Moving) moving++;
            }
            _out.WriteLine($"{runs} run(s) of 1,000 ticks measured: {walkBacks} walk-backs started, {moving} still moving at the end");
            Assert.True(walkBacks > 0, "no walk-back started during the measured ticks");
        }
    }
}
