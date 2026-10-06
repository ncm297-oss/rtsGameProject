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
        Simulation sim = MoveScenario.Spawn(seed: 41, units: 50, maxCost: 25f, out int goalCell, players: 1); // one point: one player (BUG-0037)
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

    /// <summary>
    /// A unit (player 0) in the middle of a ring of 8 idle units (no goal) that touch it, ordered out
    /// to the east. The ring belongs to player 1: friendly idle units would be shoved aside (M1-4d-2).
    /// </summary>
    private static Simulation Walled(out Vector2 center)
    {
        int type = TypeWithRadius(0.4f);
        center = new Vector2(31f, 31f);
        Simulation sim = SimOn(Flat(32), 9, players: 2);
        sim.Enqueue(Command.SpawnUnit(0, type, center));
        for (int k = 0; k < 8; k++)
        {
            float a = k * MathF.PI / 4f;
            sim.Enqueue(Command.SpawnUnit(1, type, center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * 0.8f));
        }
        sim.Tick();
        sim.Tick();
        return sim;
    }

    [Fact]
    public void UnitWalledInByEnemyIdleUnits_GoesIdleAfterExactlyGiveUpTicks_AndDropsItsGoal()
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

    /// <summary>A 48 x 24 map split by a cliff column at cell x = 20, open only through a 3-cell gap (rows 10..12): a ramp's width.</summary>
    private static Heightmap ThreeCellGap()
    {
        var rows = new string[24];
        for (int y = 0; y < 24; y++)
        {
            char[] r = new string('0', 48).ToCharArray();
            if (y < 10 || y > 12) r[20] = '1';
            rows[y] = new string(r);
        }
        return Rows(rows);
    }

    /// <summary>
    /// M1-5: 100 units of every type, one player, packed west of a 3-cell gap (a ramp's width) and sent
    /// to one point east of it, 10 seeds. The crowd funnels through the gap; units pinned at its
    /// corners wait while their group flows past (a queued tick holds the stuck count). Before M1-5
    /// such units gave up after 1 s although their group was still moving: 18 over these 10 seeds
    /// (9 of the 10 lost 1-4), and the cross-map scenario lost 0-9 units per run so.
    /// </summary>
    [Fact]
    public void CrowdThroughAThreeCellGap_10Seeds_NoneGiveUp_AllArrive()
    {
        const int units = 100;
        int gaveUp = 0, arrivedCount = 0;
        for (ulong seed = 1; seed <= 10; seed++)
        {
            var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 1, UnitCapacity: units, CommandCapacity: 2 * units + 8), ThreeCellGap());
            NavGrid g = sim.World.NavGrid;
            Assert.True(g.IsPassable(20, 11) && !g.IsPassable(20, 9) && !g.IsPassable(20, 13));
            var rng = new Determinism.SimRng(seed, 5);
            for (int i = 0; i < units; i++)
            {
                // Cells x 12..18, y 6..16: a compact crowd just west of the gap.
                var at = new Vector2(24.1f + rng.NextFloat() * 13.8f, 12.1f + rng.NextFloat() * 21.8f);
                sim.Enqueue(Command.SpawnUnit(0, i % TestSim.UnitTypeCount, at));
            }
            sim.Tick();
            sim.Tick();
            MoveScenario.MoveAll(sim, g.CellCenter(36, 11));
            UnitStore u = sim.World.Units;
            int ticks = 0;
            do
            {
                sim.Tick();
                ticks++;
                Assert.Equal(-1, MoveScenario.FirstUnitOnBlockedGround(sim.World));
            } while ((ticks < 2 || CountMoving(u) > 0) && ticks < 2000);
            Assert.Equal(0, CountMoving(u));
            int seedGaveUp = 0, seedArrived = 0;
            for (int i = 0; i < units; i++) if (u.GoalCell[i] == -1) seedGaveUp++;
            foreach (bool a in MoveScenario.Arrived(sim.World)) if (a) seedArrived++;
            _out.WriteLine($"seed {seed}: idle after {ticks} ticks, arrived {seedArrived}, gave up {seedGaveUp}");
            gaveUp += seedGaveUp;
            arrivedCount += seedArrived;
        }
        Assert.Equal(0, gaveUp);
        Assert.Equal(10 * units, arrivedCount);
    }

    /// <summary>
    /// BUG-0035: standing units are walls, also two side by side. A 2-cell gap in a cliff column is
    /// plugged by two wide enemies standing at its cell centers (a 0.2 m slit between them); 40 units of
    /// every type crowd at it. Every tick, a unit that was Moving at the start of the tick (so moved
    /// only by its own step) must end it no deeper into either enemy than it already was; nobody gets
    /// past the plug, and all give up. Before the fix, clipping the step against the second enemy slid
    /// it back into the first, and walkers worked their way through the slit.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void CrowdAtAGapPluggedByTwoEnemies_NoWalkerEverGoesDeeperIntoOne_NobodyPasses(ulong seed)
    {
        const int crowd = 40;
        var rows = new string[24];
        for (int y = 0; y < 24; y++)
        {
            char[] r = new string('0', 48).ToCharArray();
            if (y < 10 || y > 11) r[20] = '1';
            rows[y] = new string(r);
        }
        var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: crowd + 2, CommandCapacity: 4 * crowd + 16), Rows(rows));
        NavGrid g = sim.World.NavGrid;
        var rng = new Determinism.SimRng(seed, 35);
        for (int i = 0; i < crowd; i++)
            sim.Enqueue(Command.SpawnUnit(0, i % TestSim.UnitTypeCount, new Vector2(26.1f + rng.NextFloat() * 11.8f, 14.1f + rng.NextFloat() * 15.8f)));
        int wide = TypeWithRadius(0.9f);
        Vector2 e0 = g.CellCenter(20, 10), e1 = g.CellCenter(20, 11);
        sim.Enqueue(Command.SpawnUnit(1, wide, e0));
        sim.Enqueue(Command.SpawnUnit(1, wide, e1));
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        // Spawns apply in player order: the enemies take the last two slots.
        Assert.True(u.Owner[crowd] == 1 && u.Owner[crowd + 1] == 1);
        for (int i = 0; i < crowd; i++) sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, i), g.CellCenter(36, 10)));
        var wasMoving = new bool[crowd];
        var depth = new float[crowd, 2];
        int ticks = 0;
        do
        {
            for (int i = 0; i < crowd; i++)
            {
                wasMoving[i] = u.State[i] == UnitState.Moving;
                for (int e = 0; e < 2; e++) depth[i, e] = u.Radius[i] + u.Radius[crowd + e] - Vector2.Distance(u.Position[i], u.Position[crowd + e]);
            }
            sim.Tick();
            ticks++;
            Assert.True(u.Position[crowd] == e0 && u.Position[crowd + 1] == e1, "an enemy moved");
            for (int i = 0; i < crowd; i++)
            {
                if (!wasMoving[i]) continue;
                for (int e = 0; e < 2; e++)
                {
                    float now = u.Radius[i] + u.Radius[crowd + e] - Vector2.Distance(u.Position[i], u.Position[crowd + e]);
                    Assert.True(now <= MathF.Max(depth[i, e], 0f) + 1e-4f,
                        $"tick {ticks}: walker {i} went {now:F3} m into enemy {e} (was {depth[i, e]:F3})");
                }
            }
        } while ((ticks < 2 || CountMoving(u) > 0) && ticks < 2000);
        Assert.Equal(0, CountMoving(u));
        int past = 0, gaveUp = 0;
        for (int i = 0; i < crowd; i++)
        {
            if (u.Position[i].X > 21 * MapConstants.CellSize) past++;
            if (u.GoalCell[i] == -1) gaveUp++;
        }
        _out.WriteLine($"seed {seed}: idle after {ticks} ticks, past the plug {past}, gave up {gaveUp}/{crowd}");
        Assert.Equal(0, past);
        Assert.Equal(crowd, gaveUp);
    }

    /// <summary>
    /// M1-5 (BUG-0036): the queued hold must not keep a jammed crowd alive. 60 units of every type crowd
    /// at a 1-cell gap plugged by one wide enemy, so nobody gets through. While the crowd closes up,
    /// queued ticks do happen (asserted: a Moving unit's count held at 1-9, below the push threshold, so
    /// only a queued tick holds it). The rule's guards are checked every tick: a Moving unit reads
    /// StuckTicks 0 (the "made progress" signal) only right after a new best estimate, so a waiting
    /// unit never passes the signal on, and a queued tick never raises the best. And every walker gives
    /// up within the walk to the plug plus 10 give-up periods (about 200-250 ticks measured; dropping
    /// a guard keeps waiters holding each other for much longer).
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    [InlineData(5UL)]
    public void CrowdJammedAtAGapPluggedByAnEnemy_QueuedTicksHold_ButAllGiveUpInBoundedTime(ulong seed)
    {
        const int crowd = 60;
        var rows = new string[24];
        for (int y = 0; y < 24; y++)
        {
            char[] r = new string('0', 48).ToCharArray();
            if (y != 11) r[20] = '1';
            rows[y] = new string(r);
        }
        var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: crowd + 1, CommandCapacity: 4 * crowd + 16), Rows(rows));
        NavGrid g = sim.World.NavGrid;
        var rng = new Determinism.SimRng(seed, 36);
        for (int i = 0; i < crowd; i++)
            sim.Enqueue(Command.SpawnUnit(0, i % TestSim.UnitTypeCount, new Vector2(24.1f + rng.NextFloat() * 13.8f, 12.1f + rng.NextFloat() * 21.8f)));
        sim.Enqueue(Command.SpawnUnit(1, TypeWithRadius(0.9f), g.CellCenter(20, 11)));
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        Assert.Equal(1, u.Owner[crowd]);
        for (int i = 0; i < crowd; i++) sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, i), g.CellCenter(36, 11)));
        float slowest = float.MaxValue;
        foreach (var def in TestSim.Data.Units) slowest = MathF.Min(slowest, def.SpeedPerTick);
        // Farthest spawn to the plug: about 15 m at the slowest type's speed, then 10 give-up periods.
        int bound = (int)(16f / slowest) + 10 * MovementConstants.GiveUpTicks;
        var wasMoving = new bool[crowd];
        var stuck = new int[crowd];
        var best = new float[crowd];
        int ticks = 0, holds = 0;
        do
        {
            for (int i = 0; i < crowd; i++)
            {
                wasMoving[i] = u.State[i] == UnitState.Moving;
                stuck[i] = u.StuckTicks[i];
                best[i] = u.BestRemaining[i];
            }
            sim.Tick();
            ticks++;
            for (int i = 0; i < crowd; i++)
            {
                if (!wasMoving[i] || u.State[i] != UnitState.Moving) continue;
                Assert.True(u.BestRemaining[i] <= best[i], $"tick {ticks}: unit {i}'s best estimate rose from {best[i]} to {u.BestRemaining[i]}");
                if (u.StuckTicks[i] == 0)
                    Assert.True(u.BestRemaining[i] < best[i], $"tick {ticks}: unit {i} reads StuckTicks 0 without a new best");
                if (stuck[i] > 0 && stuck[i] < MovementConstants.PushAfterStuckTicks && u.StuckTicks[i] == stuck[i]) holds++;
            }
        } while ((ticks < 2 || CountMoving(u) > 0) && ticks < 4 * bound);
        int gaveUp = 0, past = 0;
        for (int i = 0; i < crowd; i++)
        {
            if (u.GoalCell[i] == -1) gaveUp++;
            if (u.Position[i].X > 21 * MapConstants.CellSize) past++;
        }
        _out.WriteLine($"seed {seed}: idle after {ticks} ticks (bound {bound}), {holds} queued holds, gave up {gaveUp}/{crowd}, past {past}");
        Assert.True(holds > 0, "no queued tick: the test doesn't exercise the hold");
        Assert.Equal(0, CountMoving(u));
        Assert.Equal(crowd, gaveUp);
        Assert.Equal(0, past);
        Assert.True(ticks <= bound, $"{ticks} ticks > {bound}");
    }

    /// <summary>
    /// M1-5: six units of one goal in a 1-cell corridor behind an enemy that blocks it all give up in
    /// a bounded time. The units ahead are blocked (zero velocity, so standing): this never queues a
    /// tick, it only pins the plain give-up in a jammed line. The hold itself is exercised by
    /// <see cref="CrowdJammedAtAGapPluggedByAnEnemy_QueuedTicksHold_ButAllGiveUpInBoundedTime"/>.
    /// </summary>
    [Fact]
    public void JammedQueueBehindAnEnemyInOneCellCorridor_AllGiveUp()
    {
        Simulation sim = SimOn(Corridor(), 7, players: 2);
        NavGrid g = sim.World.NavGrid;
        int wide = TypeWithRadius(0.9f); // too wide to pass each other in 2 m
        // Spawns apply in player order: player 0's six walkers take slots 0-5, the enemy slot 6.
        var spawns = new (int, int, Vector2)[7];
        for (int k = 0; k < 6; k++) spawns[k] = (0, wide, g.CellCenter(k + 1, 2));
        spawns[6] = (1, wide, g.CellCenter(14, 2));
        SpawnOwned(sim, spawns);
        UnitStore u = sim.World.Units;
        Assert.Equal(1, u.Owner[6]);
        for (int k = 0; k < 6; k++) sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, k), g.CellCenter(20, 2)));
        sim.Tick();
        int ticks = 0;
        do
        {
            sim.Tick();
            ticks++;
        } while ((ticks < 2 || CountMoving(u) > 0) && ticks < 1000);
        _out.WriteLine($"jammed queue: idle after {ticks} ticks");
        // Bound: the walk to the jam (at most 13 cells at the type's speed), then at most one
        // GiveUpTicks per unit (a unit that gives up becomes shovable, so the one behind may gain a
        // little and start its count again).
        float speed = TestSim.Data.Units[wide].SpeedPerTick;
        Assert.True(ticks <= (int)(13 * MapConstants.CellSize / speed) + 6 * MovementConstants.GiveUpTicks, $"{ticks} ticks");
        for (int k = 0; k < 6; k++)
        {
            Assert.Equal(UnitState.Idle, u.State[k]);
            Assert.Equal(-1, u.GoalCell[k]);
            // Pressed against the enemy (walkers may overlap a standing unit they slid into), never past it.
            Assert.True(u.Position[k].X < u.Position[6].X + u.Radius[k] + u.Radius[6], $"unit {k} at {u.Position[k]} got past the enemy at {u.Position[6]}");
        }
    }

    /// <summary>
    /// BUG-0027: a unit at its goal, hugging a cliff, overlapped by an idle unit with no goal. It is
    /// too crowded to stop, and its back-off points into the cliff and is refused. Back-off ticks
    /// count toward the limit, so it stops after GiveUpTicks, and keeps its goal (it is at it).
    /// </summary>
    [Fact]
    public void CrowdedAtGoal_BackOffRefusedByACliff_StopsAfterGiveUpTicks_KeepingItsGoal()
    {
        var rows = new string[16];
        for (int y = 0; y < 16; y++) rows[y] = y >= 1 && y <= 14 ? "0000100000000000" : new string('0', 16);
        Simulation sim = SimOn(Rows(rows), 2);
        Assert.False(sim.World.NavGrid.IsPassable(4, 5));
        int type = TypeWithRadius(0.4f);
        Spawn(sim, (type, new Vector2(10.05f, 11f)), (type, new Vector2(10.15f, 11f)));
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), new Vector2(10.3f, 11f)));
        sim.Tick(); // queued
        sim.Tick(); // the Move applies, then the first (crowded) tick
        Assert.Equal(UnitState.Moving, u.State[0]); // crowded: may not stop yet
        Assert.Equal(new Vector2(10.05f, 11f), u.Position[0]);
        int ticks = 1;
        while (u.State[0] == UnitState.Moving && ticks < 10 * MovementConstants.GiveUpTicks)
        {
            sim.Tick();
            ticks++;
        }
        Assert.Equal(UnitState.Idle, u.State[0]);
        Assert.True(ticks <= MovementConstants.GiveUpTicks + 1, $"{ticks} ticks");
        Assert.NotEqual(-1, u.GoalCell[0]);
        Assert.Equal(0, u.StuckTicks[0]);
    }

    /// <summary>BUG-0029: re-issuing the walled-in unit's own order every tick must not restart its stuck count.</summary>
    [Fact]
    public void SameMoveSpammedEveryTick_ToAWalledInUnit_StillGivesUpOnTime()
    {
        Simulation sim = Walled(out Vector2 center);
        UnitStore u = sim.World.Units;
        Vector2 target = center + new Vector2(20f, 0f);
        int ticks = 0;
        do
        {
            sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), target + new Vector2(0f, 0.01f * (ticks % 3))));
            sim.Tick();
            ticks++;
        } while ((ticks < 2 || u.State[0] == UnitState.Moving) && ticks < 10 * MovementConstants.GiveUpTicks);
        Assert.True(ticks <= MovementConstants.GiveUpTicks + 2, $"{ticks} ticks");
        Assert.Equal(-1, u.GoalCell[0]);
    }

    /// <summary>BUG-0029: an arrived blob re-ordered to the same point every tick (click spam, AI refresh) stays put.</summary>
    [Fact]
    public void ArrivedBlob_ReorderedToTheSamePointEveryTick_StaysIdleAndStill()
    {
        Simulation sim = MoveScenario.Spawn(seed: 3, units: 20, maxCost: 40f, out int goalCell, players: 1); // one point: one player (BUG-0037)
        Vector2 goal = MoveScenario.Center(sim.World.NavGrid, goalCell);
        MoveScenario.MoveAll(sim, goal);
        sim.Tick();
        UnitStore u = sim.World.Units;
        int t = 0;
        do { sim.Tick(); t++; } while (CountMoving(u) > 0 && t < 1200);
        Assert.Equal(0, CountMoving(u));
        bool[] before = MoveScenario.Arrived(sim.World);
        var positions = (Vector2[])u.Position.Clone();
        for (int k = 0; k < 100; k++)
        {
            MoveScenario.MoveAll(sim, goal);
            sim.Tick();
            Assert.Equal(0, CountMoving(u));
        }
        Assert.Equal(positions, u.Position);
        Assert.Equal(before, MoveScenario.Arrived(sim.World));
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

    // ---------- shoving (M1-4d-2) ----------

    private static void SpawnOwned(Simulation sim, params (int Owner, int Type, Vector2 At)[] units)
    {
        foreach ((int owner, int type, Vector2 at) in units) sim.Enqueue(Command.SpawnUnit(owner, type, at));
        sim.Tick();
        sim.Tick();
    }

    /// <summary>A 1-cell-wide (2 m) corridor along cell row 2 (y 4..6 m), cells x 1..22, cliffs above and below.</summary>
    private static Heightmap Corridor() => Rows(
        "000000000000000000000000",
        "111111111111111111111111",
        "000000000000000000000000",
        "111111111111111111111111",
        "000000000000000000000000");

    /// <summary>A 16 x 16 map with a cliff column at cell x = 4 (x 8..10 m) on rows 1..14: the same map as the BUG-0027 test.</summary>
    private static Heightmap CliffColumn()
    {
        var rows = new string[16];
        for (int y = 0; y < 16; y++) rows[y] = y >= 1 && y <= 14 ? "0000100000000000" : new string('0', 16);
        return Rows(rows);
    }

    /// <summary>
    /// Criterion 1: a walker in a 1-cell corridor, with an Idle unit of its own player standing
    /// between it and its goal, arrives. The standing unit stays Idle with no order, inside the
    /// corridor's passable cells, and its disk never overlaps a cliff. Where the two can't pass side
    /// by side (<paramref name="blocked"/>), the walker shoves it along ahead of itself.
    /// </summary>
    [Theory]
    [InlineData(0.9f, 0.9f, true)]
    [InlineData(0.4f, 0.9f, true)]
    [InlineData(0.7f, 0.7f, false)]
    [InlineData(0.9f, 0.4f, false)]
    [InlineData(0.4f, 0.4f, false)]
    public void WalkerInOneCellCorridor_PastAFriendlyIdleUnit_Arrives_ShovingItAlongWhenBlocked(float walkerRadius, float idleRadius, bool blocked)
    {
        Simulation sim = SimOn(Corridor(), 2);
        NavGrid g = sim.World.NavGrid;
        Vector2 start = g.CellCenter(2, 2), standing = g.CellCenter(8, 2), goal = g.CellCenter(14, 2);
        SpawnOwned(sim, (0, TypeWithRadius(walkerRadius), start), (0, TypeWithRadius(idleRadius), standing));
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), goal));
        sim.Tick(); // queued; the Move applies on the next tick
        int orderTick = u.OrderTick[1];
        float worstEdge = float.PositiveInfinity;
        int ticks = 0;
        do
        {
            sim.Tick();
            ticks++;
            Assert.Equal(-1, MoveScenario.FirstUnitOnBlockedGround(sim.World));
            Assert.Equal(UnitState.Idle, u.State[1]);
            Assert.Equal(Vector2.Zero, u.Velocity[1]);
            // The corridor's cliffs are the rows y < 4 m and y >= 6 m: the shoved disk stays between them.
            float edge = MathF.Min(u.Position[1].Y - idleRadius - 4f, 6f - u.Position[1].Y - idleRadius);
            worstEdge = MathF.Min(worstEdge, edge);
            Assert.True(edge >= -1e-4f, $"tick {ticks}: shoved unit at {u.Position[1]} overlaps the cliff by {-edge:F3} m");
        } while (u.State[0] == UnitState.Moving && ticks < 1000);
        _out.WriteLine($"radii {walkerRadius}/{idleRadius}: walker idle after {ticks} ticks, {Vector2.Distance(u.Position[0], goal):F2} m from its goal; shoved unit moved {Vector2.Distance(u.Position[1], standing):F2} m, closest to a cliff {worstEdge:F3} m");
        Assert.True(Vector2.Distance(u.Position[0], goal) <= MovementConstants.ArrivalDistance, $"walker stopped at {u.Position[0]}");
        Assert.NotEqual(-1, u.GoalCell[0]);
        if (blocked) Assert.True(u.Position[1].X > goal.X, $"the idle unit was not shoved along: {u.Position[1]}");
        Assert.Equal(-1, u.GoalCell[1]);
        Assert.Equal(orderTick, u.OrderTick[1]);
    }

    /// <summary>
    /// Criterion 2: an Idle unit standing against a cliff (or the map's blocked outer ring) is never
    /// shoved into it. The walker pushes straight or diagonally at the wall: the shove is refused, the
    /// standing unit never moves, the walker stops (arrives against it or gives up), and no unit
    /// stands on blocked ground at any tick.
    /// </summary>
    [Theory]
    [InlineData("cliff", 10.1f, 11f, 13.5f, 11f, 10.2f, 11f)]
    [InlineData("cliff", 10.1f, 11f, 12.5f, 9.5f, 10.2f, 12.6f)]
    [InlineData("ring", 2.1f, 20f, 5.5f, 20f, 2.2f, 20f)]
    [InlineData("ring", 2.1f, 2.1f, 4.5f, 4.5f, 2.2f, 2.2f)]
    public void IdleUnitAgainstAWall_IsNeverShovedIntoIt(string map, float sx, float sy, float wx, float wy, float gx, float gy)
    {
        Simulation sim = SimOn(map == "cliff" ? CliffColumn() : Flat(16), 2);
        Vector2 standing = new(sx, sy);
        int type = TypeWithRadius(0.4f);
        SpawnOwned(sim, (0, type, standing), (0, type, new Vector2(wx, wy)));
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 1), new Vector2(gx, gy)));
        sim.Tick();
        int ticks = 0;
        do
        {
            sim.Tick();
            ticks++;
            Assert.Equal(-1, MoveScenario.FirstUnitOnBlockedGround(sim.World));
            Assert.True(u.Position[0] == standing, $"tick {ticks}: the unit against the wall moved to {u.Position[0]}");
        } while (u.State[1] == UnitState.Moving && ticks < 10 * MovementConstants.GiveUpTicks);
        _out.WriteLine($"{map}: walker {u.State[1]} after {ticks} ticks at {u.Position[1]}, goal cell {u.GoalCell[1]}");
        Assert.Equal(UnitState.Idle, u.State[1]);
        Assert.Equal(UnitState.Idle, u.State[0]);
    }

    /// <summary>
    /// Criterion 3: an enemy Idle unit is never shoved. Walker (player 0) and standing unit (player 1)
    /// in the 1-cell corridor: the walker can't pass, so it gives up, and the enemy never moves.
    /// </summary>
    [Fact]
    public void EnemyIdleUnitInOneCellCorridor_IsNeverShoved_WalkerGivesUp()
    {
        Simulation sim = SimOn(Corridor(), 2, players: 2);
        NavGrid g = sim.World.NavGrid;
        Vector2 standing = g.CellCenter(8, 2), goal = g.CellCenter(14, 2);
        int type = TypeWithRadius(0.9f); // too wide to pass each other in 2 m
        SpawnOwned(sim, (0, type, g.CellCenter(2, 2)), (1, type, standing));
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), goal));
        sim.Tick();
        int ticks = 0;
        do
        {
            sim.Tick();
            ticks++;
            Assert.True(u.Position[1] == standing, $"tick {ticks}: the enemy unit moved to {u.Position[1]}");
        } while (u.State[0] == UnitState.Moving && ticks < 1000);
        Assert.Equal(UnitState.Idle, u.State[0]);
        Assert.Equal(-1, u.GoalCell[0]);
        Assert.True(u.Position[0].X < standing.X, $"walker got past the enemy: {u.Position[0]}");
    }

    /// <summary>
    /// Criterion 3: a Moving unit, walking or waiting for its field, is never moved by a shove. 128
    /// units of one player to 64 neighboring goals: under the build cap most wait for their field
    /// at first, standing among walkers and Idle units. Every tick, each unit that was Moving before
    /// it and still is moved by exactly its own step (its Velocity; zero while waiting).
    /// </summary>
    [Fact]
    public void MovingUnits_WalkingOrWaitingForAField_AreNeverShoved()
    {
        Simulation sim = MoveScenario.Spawn(seed: 21, units: 128, maxCost: 15f, out int center);
        UnitStore u = sim.World.Units;
        // One player, so every Idle unit is a candidate for shoving.
        for (int i = 0; i < u.Capacity; i++) u.Owner[i] = 0;
        NavGrid g = sim.World.NavGrid;
        var near = Pathfinding.FlowField.Build(g, center);
        var goals = new List<int>();
        for (int c = 0; c < g.Width * g.Height && goals.Count < 64; c++)
            if (near.CostAt(c) <= 15f) goals.Add(c);
        for (int i = 0; i < u.Capacity; i++)
            sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, i), MoveScenario.Center(g, goals[i % goals.Count])));
        sim.Tick();
        var wasMoving = new bool[u.Capacity];
        var wasIdle = new bool[u.Capacity];
        var before = new Vector2[u.Capacity];
        int waitingChecks = 0, walkingChecks = 0, shoves = 0, ticks = 0;
        do
        {
            for (int i = 0; i < u.Capacity; i++)
            {
                wasMoving[i] = u.State[i] == UnitState.Moving;
                wasIdle[i] = u.State[i] == UnitState.Idle;
                before[i] = u.Position[i];
            }
            sim.Tick();
            ticks++;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (wasIdle[i] && u.State[i] == UnitState.Idle && u.Position[i] != before[i]) shoves++;
                if (!wasMoving[i] || u.State[i] != UnitState.Moving) continue;
                // The same float sum MovementSystem does: anything else (a shove) shows as a difference.
                Assert.True(u.Position[i] == before[i] + u.Velocity[i], $"tick {ticks}: Moving unit {i} went {before[i]} -> {u.Position[i]} but stepped {u.Velocity[i]}");
                if (u.Velocity[i] == Vector2.Zero) waitingChecks++;
                else walkingChecks++;
            }
        } while (CountMoving(u) > 0 && ticks < 3000);
        _out.WriteLine($"{ticks} ticks: {walkingChecks} walking and {waitingChecks} standing Moving unit-ticks checked; {shoves} shoves of Idle units");
        Assert.True(waitingChecks > 100 && walkingChecks > 1000 && shoves > 20, "the scenario did not mix waiting, walking and shoving");
    }

    /// <summary>
    /// Criterion 4: three arrived units of one goal in a row, A within ArrivalDistance of the point,
    /// B touching A, C touching only B. A friendly walker overlapping B shoves it: B moves, but only
    /// as far as it stays touching A and C (KeepLinks), so the row stays linked to its point and all
    /// three keep their goal.
    /// </summary>
    [Fact]
    public void ShovedArrivedUnit_StaysTouchingItsBlob_AndTheRowKeepsItsGoal()
    {
        Simulation sim = SimOn(Flat(32), 4);
        NavGrid g = sim.World.NavGrid;
        Vector2 point = new(31f, 31f);
        Assert.True(g.WorldToCell(point, out int px, out int py));
        int cell = py * g.Width + px;
        Vector2 a = point + new Vector2(0.3f, 0f), b = point + new Vector2(1.05f, 0f), c = point + new Vector2(1.8f, 0f);
        int type = TypeWithRadius(0.4f);
        SpawnOwned(sim, (0, type, a), (0, type, b), (0, type, c), (0, type, b + new Vector2(0.35f, -0.35f)));
        UnitStore u = sim.World.Units;
        // A, B and C arrived at the point earlier (a test seam: their Move already played out).
        for (int i = 0; i < 3; i++)
        {
            u.Goal[i] = point;
            u.GoalCell[i] = cell;
        }
        bool[] before = MoveScenario.Arrived(sim.World);
        Assert.True(before[0] && before[1] && before[2], "the row is not linked to its point");
        // The walker overlaps B and heads away south-west.
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 3), point + new Vector2(-12f, 12f)));
        sim.Tick();
        int ticks = 0;
        bool bMoved = false;
        do
        {
            sim.Tick();
            ticks++;
            if (u.Position[1] != b) bMoved = true;
            bool[] arrived = MoveScenario.Arrived(sim.World);
            Assert.True(arrived[0] && arrived[1] && arrived[2], $"tick {ticks}: the row came apart: A {u.Position[0]}, B {u.Position[1]}, C {u.Position[2]}");
        } while (u.State[3] == UnitState.Moving && ticks < 1000);
        _out.WriteLine($"after {ticks} ticks: A {u.Position[0]}, B {u.Position[1]} (from {b}), C {u.Position[2]}; walker {u.State[3]} at {u.Position[3]}");
        Assert.True(bMoved, "B was never shoved");
        Assert.Equal(a, u.Position[0]); // A holds its point
        for (int i = 0; i < 3; i++) Assert.Equal(cell, u.GoalCell[i]);
    }

    /// <summary>
    /// The wait before pushing a parked unit (<see cref="MovementConstants.PushAfterStuckTicks"/>): a
    /// unit parked alone on its point in the 1-cell corridor is not moved until the blocked friendly
    /// walker has counted that many stuck ticks; then it is pushed and the walker gets through.
    /// </summary>
    [Fact]
    public void LoneParkedUnit_IsPushedOnlyAfterTheWalkerWasStuckPushAfterStuckTicks()
    {
        Simulation sim = SimOn(Corridor(), 2);
        NavGrid g = sim.World.NavGrid;
        int type = TypeWithRadius(0.9f);
        Vector2 parked = g.CellCenter(8, 2), goal = g.CellCenter(14, 2);
        SpawnOwned(sim, (0, type, g.CellCenter(2, 2)), (0, type, parked));
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 1), parked));
        sim.Tick();
        sim.Tick();
        Assert.True(u.GoalCell[1] >= 0 && u.State[1] == UnitState.Idle, "the unit did not park");
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), goal));
        sim.Tick();
        int ticks = 0, stuckBeforeFirstPush = -1, maxStuckBefore = 0;
        do
        {
            int stuck = u.StuckTicks[0];
            Vector2 before = u.Position[1];
            sim.Tick();
            ticks++;
            if (stuckBeforeFirstPush < 0)
            {
                if (u.Position[1] != before) stuckBeforeFirstPush = stuck;
                else maxStuckBefore = Math.Max(maxStuckBefore, stuck);
            }
        } while (u.State[0] == UnitState.Moving && ticks < 1000);
        _out.WriteLine($"first push after {stuckBeforeFirstPush} stuck ticks (most stuck before it: {maxStuckBefore}); walker idle after {ticks} ticks at {u.Position[0]}");
        Assert.Equal(MovementConstants.PushAfterStuckTicks, stuckBeforeFirstPush);
        Assert.True(Vector2.Distance(u.Position[0], goal) <= MovementConstants.ArrivalDistance, $"walker stopped at {u.Position[0]}");
    }

    /// <summary>
    /// Criterion 4 (BUG-0033): a unit parked alone on its point in the 1-cell corridor is shoved along
    /// by a blocked friendly walker. It keeps its goal while still within ArrivalDistance of its point,
    /// drops it (GoalCell -1) once pushed out of it, and walks back when re-ordered to the same point.
    /// </summary>
    [Fact]
    public void LoneUnitParkedInACorridor_ShovedOffItsPoint_DropsItsGoal_AndWalksBackWhenReordered()
    {
        Simulation sim = SimOn(Corridor(), 2);
        NavGrid g = sim.World.NavGrid;
        int type = TypeWithRadius(0.9f);
        Vector2 parked = g.CellCenter(8, 2), goal = g.CellCenter(14, 2);
        SpawnOwned(sim, (0, type, g.CellCenter(2, 2)), (0, type, parked));
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 1), parked));
        sim.Tick();
        sim.Tick();
        int cell = u.GoalCell[1];
        Assert.True(cell >= 0 && u.State[1] == UnitState.Idle, "the unit did not park");
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), goal));
        sim.Tick();
        int ticks = 0;
        do
        {
            sim.Tick();
            ticks++;
            Assert.Equal(UnitState.Idle, u.State[1]);
            float off = Vector2.Distance(u.Position[1], parked);
            if (off <= MovementConstants.ArrivalDistance - 1e-3f) Assert.Equal(cell, u.GoalCell[1]);
            if (off > MovementConstants.ArrivalDistance + 1e-3f) Assert.Equal(-1, u.GoalCell[1]);
        } while (u.State[0] == UnitState.Moving && ticks < 1000);
        _out.WriteLine($"walker idle after {ticks} ticks at {u.Position[0]}; parked unit pushed to {u.Position[1]}, goal cell {u.GoalCell[1]}");
        Assert.True(Vector2.Distance(u.Position[0], goal) <= MovementConstants.ArrivalDistance, $"walker stopped at {u.Position[0]}");
        Assert.Equal(-1, u.GoalCell[1]);
        // Sent back to its point, it walks there (the walker, now Idle at its own goal, is shoved too).
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 1), parked));
        sim.Tick();
        ticks = 0;
        do { sim.Tick(); ticks++; } while (u.State[1] == UnitState.Moving && ticks < 1000);
        _out.WriteLine($"re-ordered: idle after {ticks} ticks at {u.Position[1]}, goal cell {u.GoalCell[1]}");
        Assert.True(Vector2.Distance(u.Position[1], parked) <= MovementConstants.ArrivalDistance, $"stopped at {u.Position[1]}");
        Assert.Equal(cell, u.GoalCell[1]);
    }

    /// <summary>
    /// BUG-0031 (the Constrain fix, with nothing to shove): a unit hugging a cliff, overlapped by an
    /// enemy standing unit 0.1 m east of it, walks away along the wall or east. It used to be pushed
    /// out of the whole overlap, into the cliff, on every candidate step, and gave up.
    /// </summary>
    [Theory]
    [InlineData(10.5f, 25f)]
    [InlineData(10.5f, 3f)]
    [InlineData(25f, 11f)]
    public void UnitOverlappingAnEnemyStandingUnit_WithACliffBehind_WalksAway(float tx, float ty)
    {
        Simulation sim = SimOn(CliffColumn(), 2, players: 2);
        int type = TypeWithRadius(0.4f);
        Vector2 start = new(10.05f, 11f), enemy = new(10.15f, 11f);
        SpawnOwned(sim, (0, type, start), (1, type, enemy));
        UnitStore u = sim.World.Units;
        Vector2 target = new(tx, ty);
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), target));
        sim.Tick();
        int ticks = 0;
        do
        {
            sim.Tick();
            ticks++;
            Assert.Equal(-1, MoveScenario.FirstUnitOnBlockedGround(sim.World));
        } while (u.State[0] == UnitState.Moving && ticks < 2000);
        _out.WriteLine($"to {target}: idle after {ticks} ticks at {u.Position[0]}, goal cell {u.GoalCell[0]}");
        Assert.Equal(enemy, u.Position[1]);
        Assert.True(Vector2.Distance(u.Position[0], target) <= MovementConstants.ArrivalDistance, $"stopped at {u.Position[0]}");
    }

    // ---------- determinism ----------

    private const int Crowd = 35;

    /// <summary>
    /// A column of 5 walkers crosses a grid of 35 friendly Idle units (2 m apart, 1.2 m gaps, too
    /// narrow for a walker of radius 0.7) down its middle column, shoving units along and aside.
    /// Returns the sim's state hash
    /// per tick, and per tick a hash of every unit's state in spawn-list order (not slot order), so
    /// runs with different slot orders compare.
    /// </summary>
    private static (ulong[] State, ulong[] ByIdentity) RunColumnThroughIdleCrowd(bool reversed, out int shoved)
    {
        var spawns = new List<Vector2>();
        for (int k = 0; k < Crowd; k++) spawns.Add(new Vector2(27f + 2f * (k % 5), 24f + 2f * (k / 5)));
        for (int k = 0; k < 5; k++) spawns.Add(new Vector2(31f, 4f + 3f * k));
        int n = spawns.Count;
        Simulation sim = SimOn(Flat(32), n);
        int crowdType = TypeWithRadius(0.4f), walkerType = TypeWithRadius(0.7f);
        for (int k = 0; k < n; k++)
        {
            int id = reversed ? n - 1 - k : k;
            sim.Enqueue(Command.SpawnUnit(0, id < Crowd ? crowdType : walkerType, spawns[id]));
        }
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        int SlotOf(int k) => reversed ? n - 1 - k : k;
        for (int k = Crowd; k < n; k++) sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, SlotOf(k)), new Vector2(31f, 58f)));
        var state = new ulong[600];
        var byIdentity = new ulong[state.Length];
        for (int t = 0; t < state.Length; t++)
        {
            sim.Tick();
            state[t] = sim.StateHash();
            ulong h = 14695981039346656037UL;
            void Mix(uint bits) { h = (h ^ bits) * 1099511628211UL; }
            for (int k = 0; k < n; k++)
            {
                int i = SlotOf(k);
                Mix(BitConverter.SingleToUInt32Bits(u.Position[i].X));
                Mix(BitConverter.SingleToUInt32Bits(u.Position[i].Y));
                Mix(BitConverter.SingleToUInt32Bits(u.Velocity[i].X));
                Mix(BitConverter.SingleToUInt32Bits(u.Velocity[i].Y));
                Mix((uint)u.State[i]);
                Mix((uint)u.GoalCell[i]);
            }
            byIdentity[t] = h;
        }
        shoved = 0;
        for (int k = 0; k < Crowd; k++) if (u.Position[SlotOf(k)] != spawns[k]) shoved++;
        for (int k = Crowd; k < n; k++) Assert.Equal(UnitState.Idle, u.State[SlotOf(k)]);
        return (state, byIdentity);
    }

    /// <summary>
    /// Criterion 7: the column through the idle crowd hashes equal every tick when run twice, in
    /// either spawn order, and the two spawn orders give the same states unit for unit: a shove is
    /// the same whichever slot the shover or the shoved unit has.
    /// </summary>
    /// <remarks>
    /// Not a general guarantee: a walker sums its own neighbors' pushes and wall trims in ascending
    /// slot order (M1-4d-1), so where it touches three or more units at once the last bit of its step
    /// can depend on slot order (a 7 x 7 grid 1.6 m apart first differed at tick 27, in the lead
    /// walker's own velocity). Same seed and commands still give the same hash.
    /// </remarks>
    [Fact]
    public void Determinism_WalkerColumnThroughIdleCrowd_SameHashEveryTick_WithReversedSpawnOrder()
    {
        var a = RunColumnThroughIdleCrowd(false, out int shovedA);
        var again = RunColumnThroughIdleCrowd(false, out _);
        var b = RunColumnThroughIdleCrowd(true, out int shovedB);
        var bAgain = RunColumnThroughIdleCrowd(true, out _);
        int firstDiff = -1;
        for (int t = 0; t < a.ByIdentity.Length && firstDiff < 0; t++) if (a.ByIdentity[t] != b.ByIdentity[t]) firstDiff = t;
        _out.WriteLine($"{shovedA} (reversed: {shovedB}) of {Crowd} idle units shoved; spawn orders first differ at tick {firstDiff} (-1: never)");
        Assert.True(shovedA >= 5, $"only {shovedA} idle units were shoved: the column didn't cross the crowd");
        for (int t = 0; t < a.State.Length; t++)
        {
            Assert.True(a.State[t] == again.State[t], $"spawn order 1 diverged at tick {t}");
            Assert.True(b.State[t] == bAgain.State[t], $"reversed spawn order diverged at tick {t}");
        }
        Assert.Equal(-1, firstDiff);
    }

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

        /// <summary>
        /// 300 units (both players) settled in a blob at the map's central point, then 200 more spawned
        /// 20-32 m west of it and ordered 18 m east of it, so they walk through the blob and shove its
        /// friendly members. Returns the sim with the walkers' Moves applied; <paramref name="blob"/>
        /// holds each slot's position once the blob settled.
        /// </summary>
        private static Simulation CrossingTheBlob(out Vector2[] blob)
        {
            Simulation sim = MoveScenario.Spawn(seed: TestSeeds.PreMix(73), units: 300, maxCost: 25f, out int goalCell, capacity: 500); // pre-M1-6 map
            NavGrid g = sim.World.NavGrid;
            UnitStore u = sim.World.Units;
            Vector2 point = MoveScenario.Center(g, goalCell);
            MoveScenario.MoveAll(sim, point);
            sim.Tick();
            int t = 0;
            do { sim.Tick(); t++; } while (CountMoving(u) > 0 && t < 3000);
            Assert.Equal(0, CountMoving(u));
            blob = (Vector2[])u.Position.Clone();
            Pathfinding.FlowField field = Pathfinding.FlowField.Build(g, goalCell);
            var west = new List<int>();
            for (int c = 0; c < g.Width * g.Height; c++)
                if (field.CostAt(c) >= 10f && field.CostAt(c) <= 16f && MoveScenario.Center(g, c).X < point.X - 12f) west.Add(c);
            var rng = new Determinism.SimRng(73, 5);
            for (int k = 0; k < 200; k++)
            {
                Vector2 corner = MoveScenario.Center(g, west[rng.NextInt(0, west.Count)]) - new Vector2(MapConstants.CellSize / 2);
                sim.Enqueue(Command.SpawnUnit(k % 2, k % TestSim.UnitTypeCount, corner + new Vector2(0.05f + rng.NextFloat() * 1.9f, 0.05f + rng.NextFloat() * 1.9f)));
            }
            sim.Tick();
            sim.Tick();
            Assert.Equal(500, u.Count);
            Vector2 target = point + new Vector2(18f, 0f);
            for (int i = 300; i < 500; i++) sim.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), target));
            sim.Tick();
            return sim;
        }

        private static int ShovedSince(UnitStore u, Vector2[] blob)
        {
            int n = 0;
            for (int i = 0; i < 300; i++) if (u.State[i] == UnitState.Idle && u.Position[i] != blob[i]) n++;
            return n;
        }

        /// <summary>Criterion 8: 200 walkers crossing a settled 300-unit blob, shoving its members, allocate nothing.</summary>
        [Fact]
        public void Tick_200WalkersCrossingASettled300UnitBlob_AllocatesNothing()
        {
            Simulation sim = CrossingTheBlob(out Vector2[] blob);
            for (int t = 0; t < 120; t++) sim.Tick(); // the column reaches the blob
            int before = ShovedSince(sim.World.Units, blob);
            Action ticks = () => { for (int t = 0; t < 20; t++) sim.Tick(); };
            AllocationProbe.AssertZero(ticks, _out);
            int after = ShovedSince(sim.World.Units, blob);
            _out.WriteLine($"blob units moved by shoves: {before} before the measured ticks, {after} after; {CountMoving(sim.World.Units)} moving");
            Assert.True(after > before, "no blob unit was shoved during the measured ticks");
        }

        /// <summary>Criterion 9: tick cost while 200 walkers cross a settled 300-unit blob (docs/03 budget: under 4 ms average).</summary>
        [Trait("Category", "Perf")]
        [Fact]
        public void Perf_200WalkersCrossingASettled300UnitBlob_AvgAndWorstTick()
        {
            Simulation sim = CrossingTheBlob(out Vector2[] blob);
            for (int t = 0; t < 5; t++) sim.Tick(); // warm-up (JIT, field build)
            var times = new double[400];
            var sw = new Stopwatch();
            for (int t = 0; t < times.Length; t++)
            {
                sw.Restart();
                sim.Tick();
                times[t] = sw.Elapsed.TotalMilliseconds;
            }
            double avg = times.Average(), worst = times.Max();
            _out.WriteLine($"200 walkers crossing a 300-unit blob: avg {avg:F2} ms, worst {worst:F2} ms, {ShovedSince(sim.World.Units, blob)} blob units shoved, {CountMoving(sim.World.Units)} still moving after {times.Length} ticks");
            Assert.True(ShovedSince(sim.World.Units, blob) > 10, "the walkers didn't cross the blob");
            Assert.True(avg < 4.0, $"avg {avg:F2} ms (docs/03: < 4 ms)");
        }
    }
}
