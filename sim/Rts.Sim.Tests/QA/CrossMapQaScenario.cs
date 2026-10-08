using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;
using Rts.Sim.Pathfinding;

namespace Rts.Sim.Tests.QA;

/// <summary>Where the QA cross-map army starts.</summary>
public enum CrossMapStart
{
    /// <summary>Around the passable cell nearest the middle of the west edge (the dev scenario's start).</summary>
    WestEdge,

    /// <summary>Around the ramp cell nearest the middle of the west edge, so the spawn region spans a ramp and both its levels.</summary>
    OnRamp,
}

/// <summary>
/// QA's own build of the M1-5 cross-map scenario (independent of the dev's <c>CrossMapScenario</c>),
/// with knobs the dev version lacks: army size, owners, minimum goal level (2 = two ramps in series),
/// and a start region that straddles a ramp. Runs with per-tick invariant checks.
/// </summary>
/// <remarks>
/// Time limit, as docs/03 "Measured (M1-5)": 2 x ceil(longest spawn-to-goal field cost x CellSize /
/// slowest shipped speed per tick). Computed here independently from the data and a QA-built field.
/// </remarks>
public sealed class CrossMapQaScenario
{
    public required Simulation Sim { get; init; }
    public required int StartCell { get; init; }
    public required int GoalCell { get; init; }
    public required Vector2 Goal { get; init; }
    public required int GoalLevel { get; init; }
    public required float PathMeters { get; init; }
    public required float LongestMeters { get; init; }
    public required int LimitTicks { get; init; }
    public required int SpawnLevelsSeen { get; init; }
    public required int RampSpawns { get; init; }
    public required int Units { get; init; }

    /// <summary>Builds a scenario; returns null if the map has no reachable cell at <paramref name="goalMinLevel"/> or higher (or no ramp for <see cref="CrossMapStart.OnRamp"/>).</summary>
    public static CrossMapQaScenario? Create(ulong seed, int units, float startRadius, int players = 1, int goalMinLevel = 1,
        CrossMapStart start = CrossMapStart.WestEdge, bool combat = true)
    {
        var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: players, UnitCapacity: units, CommandCapacity: 2 * units + 8) with { Combat = combat });
        NavGrid g = sim.World.NavGrid;
        int west = FlowField.NearestPassable(g, g.Height / 2 * g.Width + 1);
        int startCell = west;
        if (start == CrossMapStart.OnRamp)
        {
            // The ramp cell with the lowest path cost from the west-edge cell (ties: lowest index).
            FlowField fromWest = FlowField.Build(g, west);
            startCell = -1;
            float bestCost = float.PositiveInfinity;
            for (int c = 0; c < g.Width * g.Height; c++)
            {
                if ((g.FlagsAt(c % g.Width, c / g.Width) & NavFlags.Ramp) == 0) continue;
                float cost = fromWest.CostAt(c);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    startCell = c;
                }
            }
            if (startCell < 0) return null;
        }

        FlowField fromStart = FlowField.Build(g, startCell);
        int goal = -1;
        float far = -1f;
        var near = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
        {
            float cost = fromStart.CostAt(c);
            if (float.IsPositiveInfinity(cost)) continue;
            if (cost <= startRadius) near.Add(c);
            if (g.LevelAt(c % g.Width, c / g.Width) >= goalMinLevel && cost > far)
            {
                far = cost;
                goal = c;
            }
        }
        if (goal < 0) return null;

        var rng = new SimRng(seed, 9001);
        var levels = new HashSet<int>();
        int rampSpawns = 0;
        for (int i = 0; i < units; i++)
        {
            int cell = near[rng.NextInt(0, near.Count)];
            levels.Add(g.LevelAt(cell % g.Width, cell / g.Width));
            if ((g.FlagsAt(cell % g.Width, cell / g.Width) & NavFlags.Ramp) != 0) rampSpawns++;
            var offset = new Vector2(0.05f + rng.NextFloat() * 1.9f, 0.05f + rng.NextFloat() * 1.9f);
            Vector2 corner = MoveScenario.Center(g, cell) - new Vector2(MapConstants.CellSize / 2);
            sim.Enqueue(Command.SpawnUnit(i % players, i % TestSim.UnitTypeCount, corner + offset));
        }
        sim.Tick();
        sim.Tick();
        Assert.Equal(units, sim.World.Units.Count);

        FlowField toGoal = FlowField.Build(g, goal);
        float longest = 0f;
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            Assert.True(g.WorldToCell(u.Position[i], out int x, out int y));
            float cost = toGoal.CostAt(y * g.Width + x);
            Assert.False(float.IsPositiveInfinity(cost), $"unit {i} spawned where the goal is unreachable");
            longest = MathF.Max(longest, cost);
        }
        float slowest = float.MaxValue;
        foreach (UnitDef def in TestSim.Data.Units) slowest = MathF.Min(slowest, def.SpeedPerTick);
        int limit = 2 * (int)MathF.Ceiling(longest * MapConstants.CellSize / slowest);

        return new CrossMapQaScenario
        {
            Sim = sim,
            StartCell = startCell,
            GoalCell = goal,
            Goal = MoveScenario.Center(g, goal),
            GoalLevel = g.LevelAt(goal % g.Width, goal / g.Width),
            PathMeters = toGoal.CostAt(startCell) * MapConstants.CellSize,
            LongestMeters = longest * MapConstants.CellSize,
            LimitTicks = limit,
            SpawnLevelsSeen = levels.Count,
            RampSpawns = rampSpawns,
            Units = units,
        };
    }

    /// <summary>Result of one run.</summary>
    public readonly record struct Outcome(int Ticks, int Arrived, int GaveUp, int StillMoving, string? Pack, int MaxStuck)
    {
        public bool AllArrived(int units) => Arrived == units && GaveUp == 0 && StillMoving == 0;
    }

    /// <summary>
    /// Orders every unit to the goal and ticks until none is Moving or the limit passes. After every
    /// tick: no unit on blocked ground or off the map, positions and velocities finite, a Moving unit's
    /// stuck count in [0, GiveUpTicks), no unit moved further than its speed plus its shove allowance.
    /// </summary>
    public Outcome Run(int? limitOverride = null)
    {
        int limit = limitOverride ?? LimitTicks;
        World w = Sim.World;
        UnitStore u = w.Units;
        MoveScenario.MoveAll(Sim, Goal);
        int ticks = 0, maxStuck = 0;
        while ((ticks < 2 || AnyMoving(u)) && ticks < limit)
        {
            Sim.Tick();
            ticks++;
            int bad = MoveScenario.FirstUnitOnBlockedGround(w);
            Assert.True(bad < 0, $"tick {ticks}: unit {bad} at {(bad < 0 ? default : u.Position[bad])} on blocked ground or off the map");
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                Vector2 p = u.Position[i], v = u.Velocity[i];
                Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(v.X) && float.IsFinite(v.Y), $"tick {ticks}: unit {i} non-finite");
                if (u.State[i] == UnitState.Moving)
                {
                    Assert.InRange(u.StuckTicks[i], 0, MovementConstants.GiveUpTicks - 1);
                    if (u.StuckTicks[i] > maxStuck) maxStuck = u.StuckTicks[i];
                }
                // A walker moves by its own velocity (<= speed); a shoved Idle unit at most its speed too.
                float moved = Vector2.Distance(p, u.PrevPosition[i]);
                Assert.True(moved <= u.Speed[i] * 1.001f + 1e-4f, $"tick {ticks}: unit {i} moved {moved} > speed {u.Speed[i]}");
            }
        }
        int arrived = 0;
        foreach (bool a in MoveScenario.Arrived(w)) if (a) arrived++;
        int gaveUp = 0, moving = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            if (u.State[i] == UnitState.Moving) moving++;
            else if (u.GoalCell[i] == -1) gaveUp++;
        }
        return new Outcome(ticks, arrived, gaveUp, moving, MoveScenario.FirstPackViolation(w), maxStuck);
    }

    private static bool AnyMoving(UnitStore u)
    {
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.State[i] == UnitState.Moving) return true;
        return false;
    }

    /// <summary>A one-line description for test output.</summary>
    public string Describe()
    {
        int w = Sim.World.NavGrid.Width;
        return $"start ({StartCell % w},{StartCell / w}) goal ({GoalCell % w},{GoalCell / w}) L{GoalLevel}, path {PathMeters:F0} m, longest {LongestMeters:F0} m, " +
            $"spawn levels {SpawnLevelsSeen}, ramp spawns {RampSpawns}, limit {LimitTicks}";
    }
}
