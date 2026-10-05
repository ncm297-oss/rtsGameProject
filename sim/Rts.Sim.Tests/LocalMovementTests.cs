using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>M1-4d-1 local movement: separation, crowded arrival, giving up, adjacent-cell steering, determinism.</summary>
public class LocalMovementTests
{
    private readonly ITestOutputHelper _out;

    public LocalMovementTests(ITestOutputHelper output) => _out = output;

    // ---------- helpers ----------

    /// <summary>A heightmap from rows of level digits; a level-1 cell next to level 0 is a cliff (blocked), and the outer ring is always blocked.</summary>
    internal static Heightmap Rows(params string[] rows)
    {
        int w = rows[0].Length, h = rows.Length;
        var levels = new byte[w * h];
        var elevations = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                levels[y * w + x] = (byte)(rows[y][x] - '0');
                elevations[y * w + x] = levels[y * w + x] * MapConstants.LevelHeight;
            }
        return new Heightmap(w, h, levels, elevations);
    }

    /// <summary>An all-flat map of <paramref name="size"/> x <paramref name="size"/> cells (only the ring is blocked).</summary>
    internal static Heightmap Flat(int size)
    {
        var rows = new string[size];
        for (int y = 0; y < size; y++) rows[y] = new string('0', size);
        return Rows(rows);
    }

    internal static Simulation SimOn(Heightmap map, int units, int players = 1) =>
        new(TestSim.Config(Seed: 1, PlayerCount: players, UnitCapacity: units, CommandCapacity: 4 * units + 16), map);

    /// <summary>The first unit type whose radius is <paramref name="radius"/>.</summary>
    internal static int TypeWithRadius(float radius)
    {
        for (int t = 0; t < TestSim.UnitTypeCount; t++)
            if (TestSim.Data.Units[t].Radius == radius) return t;
        throw new InvalidOperationException($"no unit type with radius {radius}");
    }

    private static void Spawn(Simulation sim, params (int Type, Vector2 At)[] units)
    {
        foreach ((int type, Vector2 at) in units) sim.Enqueue(Command.SpawnUnit(0, type, at));
        sim.Tick();
        sim.Tick();
    }

    private static int CountMoving(UnitStore u)
    {
        int n = 0;
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.State[i] == UnitState.Moving) n++;
        return n;
    }

    // ---------- separation ----------

    [Fact]
    public void TwoOverlappingMovingUnits_PushApartSymmetrically_WhicheverSlotComesFirst()
    {
        int type = TypeWithRadius(0.4f);
        // Mirror images about the row y = 21 (cell row 10's center), 0.5 m apart: overlapping by 0.3 m.
        Vector2 low = new(20.5f, 20.75f), high = new(20.5f, 21.25f);
        Vector2 goal = new(41f, 21f);
        Vector2[] Run(bool lowFirst)
        {
            Simulation sim = SimOn(Flat(32), 2);
            Spawn(sim, (type, lowFirst ? low : high), (type, lowFirst ? high : low));
            sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), goal));
            sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 1), goal));
            sim.Tick();
            sim.Tick(); // applies the Moves and takes the first step
            UnitStore u = sim.World.Units;
            return lowFirst ? new[] { u.Velocity[0], u.Velocity[1] } : new[] { u.Velocity[1], u.Velocity[0] };
        }
        Vector2[] a = Run(true), b = Run(false);
        Assert.Equal(a, b); // slot order changes nothing
        Vector2 vLow = a[0], vHigh = a[1];
        Assert.Equal(vLow.X, vHigh.X);
        Assert.Equal(vLow.Y, -vHigh.Y);
        Assert.True(vLow.Y < 0f, $"lower unit not pushed away: {vLow}");
        float speed = TestSim.Data.Units[type].SpeedPerTick;
        Assert.True(vLow.Length() <= speed * (1 + 1e-5f));
    }

    [Fact]
    public void LoneUnit_IsUnaffected_ByAUnitOutsideItsNeighborRange()
    {
        int type = TypeWithRadius(0.4f);
        Vector2 start = new(9f, 21f), goal = new(51f, 21f);
        List<Vector2> Path(bool withBystander)
        {
            Simulation sim = SimOn(Flat(32), 2);
            if (withBystander) Spawn(sim, (type, start), (TypeWithRadius(0.9f), new Vector2(30f, 26f)));
            else Spawn(sim, (type, start));
            sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), goal));
            var path = new List<Vector2>();
            for (int t = 0; t < 300 && (t < 2 || sim.World.Units.State[0] == UnitState.Moving); t++)
            {
                sim.Tick();
                path.Add(sim.World.Units.Position[0]);
            }
            return path;
        }
        // The bystander stands 5 m off the line; the query reaches 0.4 + 0.9 + 1 = 2.3 m.
        List<Vector2> alone = Path(false), passing = Path(true);
        Assert.Equal(alone, passing);
        Assert.True(Vector2.Distance(alone[^1], goal) <= MovementConstants.ArrivalDistance);
    }

    // ---------- crowded arrival ----------

    [Fact]
    public void FiftyUnits_ToOneOpenPoint_AllIdleWithin600Ticks_PackedNeverBlockedOrOffMap()
    {
        Simulation sim = MoveScenario.Spawn(seed: 41, units: 50, maxCost: 25f, out int goalCell);
        World w = sim.World;
        Vector2 goal = MoveScenario.Center(w.NavGrid, goalCell);
        MoveScenario.MoveAll(sim, goal);
        sim.Tick(); // the Moves apply on the next tick
        UnitStore u = w.Units;
        int ticks = 0;
        do
        {
            sim.Tick();
            ticks++;
            int bad = MoveScenario.FirstUnitOnBlockedGround(w);
            Assert.True(bad < 0, $"tick {ticks}: unit {bad} on blocked ground or off the map");
        } while (CountMoving(u) > 0 && ticks < 600);
        _out.WriteLine($"50 units idle after {ticks} ticks");
        Assert.Equal(0, CountMoving(u));
        bool[] arrived = MoveScenario.Arrived(w);
        for (int i = 0; i < 50; i++)
            Assert.True(arrived[i], $"unit {i} ({u.GoalCell[i]}) idle {Vector2.Distance(u.Position[i], goal):F2} m from the goal, not in the blob");
        string? pack = MoveScenario.FirstPackViolation(w);
        Assert.True(pack == null, pack);
    }

    /// <summary>Two units swap ends of a 1-cell-wide (2 m) corridor head-on.</summary>
    [Theory]
    [InlineData(0.4f, 0.4f)]
    [InlineData(0.4f, 0.7f)]
    [InlineData(0.7f, 0.7f)]
    [InlineData(0.4f, 0.9f)]
    [InlineData(0.9f, 0.9f)]
    public void HeadOnInOneCellCorridor_NeverOverlapMoreThanHalfTheSmallerRadius_BothArrive(float ra, float rb)
    {
        Heightmap map = Rows(
            "000000000000000000000000",
            "111111111111111111111111",
            "000000000000000000000000",
            "111111111111111111111111",
            "000000000000000000000000");
        Simulation sim = SimOn(map, 2);
        NavGrid g = sim.World.NavGrid;
        for (int x = 1; x < 23; x++)
        {
            Assert.True(g.IsPassable(x, 2));
            Assert.False(g.IsPassable(x, 1));
            Assert.False(g.IsPassable(x, 3));
        }
        Vector2 west = g.CellCenter(2, 2), east = g.CellCenter(21, 2);
        Spawn(sim, (TypeWithRadius(ra), west), (TypeWithRadius(rb), east));
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), east));
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 1), west));
        sim.Tick(); // queued; the Moves apply on the next tick
        UnitStore u = sim.World.Units;
        float limit = 0.5f * MathF.Min(ra, rb), worst = float.NegativeInfinity;
        int ticks = 0;
        do
        {
            sim.Tick();
            ticks++;
            float overlap = ra + rb - Vector2.Distance(u.Position[0], u.Position[1]);
            worst = MathF.Max(worst, overlap);
            Assert.True(overlap <= limit, $"tick {ticks}: overlap {overlap:F3} m > {limit:F3} at {u.Position[0]} / {u.Position[1]}");
            Assert.Equal(-1, MoveScenario.FirstUnitOnBlockedGround(sim.World));
        } while (CountMoving(u) > 0 && ticks < 1000);
        _out.WriteLine($"radii {ra}/{rb}: both stopped after {ticks} ticks, worst overlap {worst:F3} m (limit {limit:F3})");
        Assert.True(Vector2.Distance(u.Position[0], east) <= MovementConstants.ArrivalDistance, $"west unit stopped at {u.Position[0]}");
        Assert.True(Vector2.Distance(u.Position[1], west) <= MovementConstants.ArrivalDistance, $"east unit stopped at {u.Position[1]}");
        Assert.NotEqual(-1, u.GoalCell[0]);
        Assert.NotEqual(-1, u.GoalCell[1]);
    }

    // ---------- giving up ----------

    /// <summary>A unit in the middle of a ring of 8 idle units (no goal) that touch it, ordered out to the east.</summary>
    private static Simulation Walled(out Vector2 center)
    {
        int type = TypeWithRadius(0.4f);
        center = new Vector2(31f, 31f);
        var units = new List<(int, Vector2)> { (type, center) };
        for (int k = 0; k < 8; k++)
        {
            float a = k * MathF.PI / 4f;
            units.Add((type, center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * 0.8f));
        }
        Simulation sim = SimOn(Flat(32), units.Count);
        Spawn(sim, units.ToArray());
        return sim;
    }

    [Fact]
    public void UnitWalledInByIdleUnits_GoesIdleAfterExactlyGiveUpTicks_AndDropsItsGoal()
    {
        Simulation sim = Walled(out Vector2 center);
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), center + new Vector2(20f, 0f)));
        sim.Tick(); // queued
        for (int t = 1; t < MovementConstants.GiveUpTicks; t++)
        {
            sim.Tick();
            Assert.Equal(UnitState.Moving, u.State[0]);
            Assert.Equal(t, u.StuckTicks[0]);
            // It may jostle inside the ring, but never gets out.
            Assert.True(Vector2.Distance(u.Position[0], center) < 0.25f, $"tick {t}: moved to {u.Position[0]}");
        }
        sim.Tick();
        Assert.Equal(UnitState.Idle, u.State[0]);
        Assert.Equal(0, u.StuckTicks[0]);
        Assert.Equal(-1, u.GoalCell[0]);
        Assert.Equal(Vector2.Zero, u.Velocity[0]);
    }

    [Fact]
    public void UnitWhoseEveryStepIsRefused_ByACliff_GivesUpAfterExactlyGiveUpTicks()
    {
        // As MovementSystemTests' refused-step case: goal across the edge, inside a cliff.
        var world = new World(TestSim.Config(Seed: 7, PlayerCount: 1, UnitCapacity: 4, CommandCapacity: 4));
        NavGrid g = world.NavGrid;
        int cx = -1, cy = -1;
        for (int c = 0; c < g.Width * g.Height && cx < 0; c++)
        {
            int x = c % g.Width, y = c / g.Width;
            if (g.IsPassable(x, y) && !g.IsPassable(x + 1, y)) (cx, cy) = (x, y);
        }
        UnitStore u = world.Units;
        EntityHandle h = u.Alloc();
        Vector2 start = g.CellCenter(cx, cy) + new Vector2(0.95f, 0f);
        u.Position[h.Index] = start;
        u.Speed[h.Index] = TestSim.Data.Units[0].SpeedPerTick;
        u.Radius[h.Index] = TestSim.Data.Units[0].Radius;
        u.State[h.Index] = UnitState.Moving;
        u.Goal[h.Index] = g.CellCenter(cx + 1, cy);
        u.GoalCell[h.Index] = cy * g.Width + cx;
        for (int t = 1; t <= MovementConstants.GiveUpTicks; t++)
        {
            world.Spatial.Rebuild(u);
            MovementSystem.Run(world);
            Assert.Equal(start, u.Position[h.Index]);
            if (t < MovementConstants.GiveUpTicks)
            {
                Assert.Equal(UnitState.Moving, u.State[h.Index]);
                Assert.Equal(t, u.StuckTicks[h.Index]);
            }
        }
        Assert.Equal(UnitState.Idle, u.State[h.Index]);
        Assert.Equal(-1, u.GoalCell[h.Index]);
    }

    [Fact]
    public void NewMove_ResetsTheStuckCounter()
    {
        Simulation sim = Walled(out Vector2 center);
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), center + new Vector2(20f, 0f)));
        sim.Tick();
        for (int t = 0; t < 10; t++) sim.Tick();
        Assert.Equal(10, u.StuckTicks[0]);
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), center + new Vector2(0f, -20f)));
        sim.Tick(); // stuck tick 11 of the old order
        Assert.Equal(11, u.StuckTicks[0]);
        sim.Tick(); // the Move applies (counter 0), then a stuck tick
        Assert.Equal(1, u.StuckTicks[0]);
        for (int t = 2; t < MovementConstants.GiveUpTicks; t++) sim.Tick();
        Assert.Equal(UnitState.Moving, u.State[0]);
        sim.Tick();
        Assert.Equal(UnitState.Idle, u.State[0]);
    }

    [Fact]
    public void UnitMakingProgress_NeverCountsTowardGivingUp()
    {
        Simulation sim = SimOn(Flat(32), 1);
        int type = TypeWithRadius(0.9f); // the slowest kind
        Spawn(sim, (type, new Vector2(5.3f, 5.7f)));
        Vector2 goal = new(57.1f, 50.2f);
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), goal));
        sim.Tick();
        UnitStore u = sim.World.Units;
        int maxStuck = 0, ticks = 0;
        do
        {
            sim.Tick();
            ticks++;
            maxStuck = Math.Max(maxStuck, u.StuckTicks[0]);
        } while (u.State[0] == UnitState.Moving && ticks < 2000);
        Assert.True(Vector2.Distance(u.Position[0], goal) <= MovementConstants.ArrivalDistance, $"stopped at {u.Position[0]} after {ticks} ticks");
        Assert.NotEqual(-1, u.GoalCell[0]);
        // The path estimate (octile field cost + straight line) can jump up by under a meter where
        // the field zigzags; that costs a few stuck ticks, never close to giving up.
        _out.WriteLine($"max stuck ticks while walking: {maxStuck}");
        Assert.True(maxStuck <= MovementConstants.GiveUpTicks / 2, $"stuck counter reached {maxStuck}");
    }

    // ---------- adjacent-cell steering ----------

    [Fact]
    public void GoalOneDiagonalCellAway_CornerOpen_WalksTheStraightLine()
    {
        Simulation sim = SimOn(Flat(16), 1);
        Vector2 start = new(10.3f, 10.7f), goal = new(13.4f, 12.6f); // cells (5, 5) and (6, 6)
        Spawn(sim, (TypeWithRadius(0.9f), start));
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), goal));
        sim.Tick();
        UnitStore u = sim.World.Units;
        Vector2 line = Vector2.Normalize(goal - start);
        int ticks = 0;
        do
        {
            sim.Tick();
            ticks++;
            Vector2 off = u.Position[0] - start;
            float cross = off.X * line.Y - off.Y * line.X;
            Assert.True(MathF.Abs(cross) < 1e-4f, $"tick {ticks}: {u.Position[0]} is {cross} m off the straight line");
        } while (u.State[0] == UnitState.Moving && ticks < 200);
        Assert.Equal(UnitState.Idle, u.State[0]);
        Assert.True(Vector2.Distance(u.Position[0], goal) <= MovementConstants.ArrivalDistance);
    }

    [Fact]
    public void GoalOneDiagonalCellAway_CornerBlocked_TakesTheLegalRoute()
    {
        // Cell (6, 5) is a cliff, so (5, 5) -> (6, 6) would cut its corner.
        var rows = new string[16];
        for (int y = 0; y < 16; y++) rows[y] = y == 5 ? "0000001000000000" : new string('0', 16);
        Simulation sim = SimOn(Rows(rows), 1);
        NavGrid g = sim.World.NavGrid;
        Assert.False(g.IsPassable(6, 5));
        Assert.True(g.IsPassable(5, 6));
        Vector2 start = new(11.7f, 10.3f), goal = new(12.4f, 12.3f); // cells (5, 5) and (6, 6)
        Spawn(sim, (TypeWithRadius(0.4f), start));
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), goal));
        sim.Tick();
        UnitStore u = sim.World.Units;
        bool visitedSide = false;
        int ticks = 0;
        do
        {
            sim.Tick();
            ticks++;
            Assert.True(g.WorldToCell(u.Position[0], out int x, out int y) && g.IsPassable(x, y), $"tick {ticks}: at {u.Position[0]}");
            if (x == 5 && y == 6) visitedSide = true;
        } while (u.State[0] == UnitState.Moving && ticks < 200);
        Assert.True(visitedSide, "never went through the open side cell (5, 6)");
        Assert.Equal(UnitState.Idle, u.State[0]);
        Assert.True(Vector2.Distance(u.Position[0], goal) <= MovementConstants.ArrivalDistance);
    }

    // ---------- determinism ----------

    /// <summary>300 units in 4 groups heading for 4 nearby points, rotated every 250 ticks so the groups cross through each other's blobs.</summary>
    private static ulong[] RunCrowds(ulong seed)
    {
        Simulation sim = MoveScenario.Spawn(seed, units: 300, maxCost: 30f, out int center);
        NavGrid g = sim.World.NavGrid;
        Vector2 c = MoveScenario.Center(g, center);
        var goals = new[] { c + new Vector2(8f, 0f), c + new Vector2(0f, 8f), c + new Vector2(-8f, 0f), c + new Vector2(0f, -8f) };
        UnitStore u = sim.World.Units;
        var hashes = new ulong[1000];
        for (int t = 0; t < hashes.Length; t++)
        {
            if (t % 250 == 0)
                for (int i = 0; i < u.Capacity; i++)
                    sim.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), goals[(i + t / 250) % 4]));
            sim.Tick();
            hashes[t] = sim.StateHash();
        }
        return hashes;
    }

    [Fact]
    public void Determinism_300UnitsIn4CrowdedGroups_HashEqualEveryTick_SeedsDiffer()
    {
        ulong[] a = RunCrowds(61), b = RunCrowds(61);
        for (int t = 0; t < a.Length; t++)
            Assert.True(a[t] == b[t], $"diverged at tick {t}");
        Assert.NotEqual(a[^1], RunCrowds(62)[^1]);
    }

    /// <summary>This class's allocation and wall-clock tests: they run alone in <see cref="SerialCollection"/>.</summary>
    [Collection(SerialCollection.Name)]
    public class Serial
    {
        private readonly ITestOutputHelper _out;

        public Serial(ITestOutputHelper output) => _out = output;

        private static Simulation Converging(int units, out Vector2 goal)
        {
            Simulation sim = MoveScenario.Spawn(seed: 71, units: units, maxCost: 40f, out int goalCell);
            goal = MoveScenario.Center(sim.World.NavGrid, goalCell);
            MoveScenario.MoveAll(sim, goal);
            sim.Tick(); // the Moves apply on the next tick
            return sim;
        }

        [Fact]
        public void Tick_500UnitsConvergingOnOneBlob_AllocatesNothing()
        {
            Simulation sim = Converging(500, out _);
            for (int t = 0; t < 250; t++) sim.Tick(); // the blob is forming: arrivals, contacts, back-offs
            UnitStore u = sim.World.Units;
            int moving = CountMoving(u);
            Assert.True(moving > 50 && moving < 450, $"{moving} moving: not a converging crowd");
            Action ticks = () => { for (int t = 0; t < 20; t++) sim.Tick(); };
            AllocationProbe.AssertZero(ticks, _out);
        }

        [Trait("Category", "Perf")]
        [Theory]
        [InlineData(500, true)]
        [InlineData(1000, false)]
        [InlineData(2500, false)]
        public void Perf_UnitsConvergingOnOnePoint_AvgAndWorstTick(int units, bool enforce)
        {
            Simulation sim = Converging(units, out _);
            var times = new double[400];
            var sw = new Stopwatch();
            for (int t = 0; t < times.Length; t++)
            {
                sw.Restart();
                sim.Tick();
                times[t] = sw.Elapsed.TotalMilliseconds;
            }
            double avg = times.Average(), worst = times.Max();
            _out.WriteLine($"{units} units converging: avg {avg:F2} ms, worst {worst:F2} ms, {CountMoving(sim.World.Units)} still moving after {times.Length} ticks");
            if (!enforce) return; // information only (brief M1-4d-1)
            Assert.True(avg < 4.0, $"avg {avg:F2} ms (docs/03: < 4 ms)");
            Assert.True(worst < 8.0, $"worst {worst:F2} ms (< 8 ms)");
        }
    }
}
