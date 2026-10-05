using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;

namespace Rts.Sim.Tests;

/// <summary>
/// The M1 headline scenario (docs/05 M1, task M1-5): an army spawned in a compact region near the
/// west edge of the default generated map, ordered to the farthest plateau cell, so its route climbs
/// at least one ramp.
/// </summary>
/// <remarks>
/// Start cell: the passable cell nearest the middle of the west edge, just inside the blocked border
/// ring (level 0, since the generator keeps an <see cref="MapGenParams.EdgeMargin"/> of level-0 ground
/// there). Units spawn at random points of random passable cells whose path cost to the start cell is
/// at most <c>startRadius</c> cells. Goal: the center of the passable level-1-or-higher cell with the
/// greatest path cost from the start cell, ties to the lowest index. The start is level 0 and the
/// goal isn't, and a level changes only along a ramp (docs/03 "Navigation grid"), so a ramp is
/// unavoidable.
/// </remarks>
public sealed class CrossMapScenario
{
    private CrossMapScenario(Simulation sim, int startCell, int goalCell, FlowField goalField)
    {
        Sim = sim;
        StartCell = startCell;
        GoalCell = goalCell;
        GoalField = goalField;
        NavGrid g = sim.World.NavGrid;
        Goal = MoveScenario.Center(g, goalCell);

        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || !g.WorldToCell(u.Position[i], out int x, out int y)) continue;
            LongestCost = MathF.Max(LongestCost, goalField.CostAt(y * g.Width + x));
        }

        // Walk the goal's field from the start cell, the route the army's middle takes.
        int cell = startCell, lastLevel = g.LevelAt(startCell % g.Width, startCell / g.Width);
        for (int steps = 0; cell != goalField.TargetCell && steps < g.Width * g.Height; steps++)
        {
            byte d = goalField.DirectionAt(cell);
            Assert.NotEqual(FlowField.NoDirection, d);
            cell += FlowField.OffsetY(d) * g.Width + FlowField.OffsetX(d);
            int x = cell % g.Width, y = cell / g.Width;
            if ((g.FlagsAt(x, y) & NavFlags.Ramp) != 0) RampCellsOnPath++;
            int level = g.LevelAt(x, y);
            if (level != lastLevel) LevelChanges++;
            lastLevel = level;
        }
        Assert.Equal(goalField.TargetCell, cell);

        SlowestSpeedPerTick = float.MaxValue;
        foreach (UnitDef def in TestSim.Data.Units) SlowestSpeedPerTick = MathF.Min(SlowestSpeedPerTick, def.SpeedPerTick);
        // docs/03 "Local movement", measured (M1-5): twice the undisturbed walk of the slowest
        // shipped unit over the longest start-to-goal path, read from the field and the data.
        LimitTicks = 2 * (int)MathF.Ceiling(LongestCost * MapConstants.CellSize / SlowestSpeedPerTick);
    }

    /// <summary>The sim, spawns applied, no orders yet.</summary>
    public Simulation Sim { get; }

    /// <summary>The level-0 cell near the west edge the army spawns around.</summary>
    public int StartCell { get; }

    /// <summary>The farthest level >= 1 cell from the start.</summary>
    public int GoalCell { get; }

    /// <summary>The goal point: <see cref="GoalCell"/>'s center.</summary>
    public Vector2 Goal { get; }

    /// <summary>A standalone field to the goal cell (same contents as the sim's cached one).</summary>
    public FlowField GoalField { get; }

    /// <summary>The largest path cost (cells) from any unit's spawn cell to the goal.</summary>
    public float LongestCost { get; }

    /// <summary>Path length (meters) from the start cell to the goal.</summary>
    public float PathMeters => GoalField.CostAt(StartCell) * MapConstants.CellSize;

    /// <summary>Times the level changes along the field's route from the start cell (a ramp's mouth is where it changes).</summary>
    public int LevelChanges { get; }

    /// <summary>Ramp cells on the field's route from the start cell.</summary>
    public int RampCellsOnPath { get; }

    /// <summary>The slowest shipped unit's speed (meters per tick), from the data.</summary>
    public float SlowestSpeedPerTick { get; }

    /// <summary>Ticks every unit must arrive within: 2 x ceil(<see cref="LongestCost"/> x CellSize / <see cref="SlowestSpeedPerTick"/>).</summary>
    public int LimitTicks { get; }

    /// <summary>Level of the goal cell.</summary>
    public int GoalLevel => Sim.World.NavGrid.LevelAt(GoalCell % Sim.World.NavGrid.Width, GoalCell / Sim.World.NavGrid.Width);

    /// <summary>Level of the start cell.</summary>
    public int StartLevel => Sim.World.NavGrid.LevelAt(StartCell % Sim.World.NavGrid.Width, StartCell / Sim.World.NavGrid.Width);

    /// <summary>
    /// Builds the scenario: <paramref name="units"/> units, type <c>i % UnitTypeCount</c> (every shipped
    /// type), owned by player 0, or alternating 0/1 when <paramref name="players"/> is 2.
    /// <paramref name="onCreated"/> sees the new sim before any command (e.g. to attach a replay recorder).
    /// </summary>
    public static CrossMapScenario Create(ulong seed, int units, float startRadius, int players = 1, Action<Simulation>? onCreated = null)
    {
        var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: players, UnitCapacity: units, CommandCapacity: 2 * units + 8));
        onCreated?.Invoke(sim);
        NavGrid g = sim.World.NavGrid;
        int start = FlowField.NearestPassable(g, g.Height / 2 * g.Width + 1);
        FlowField fromStart = FlowField.Build(g, start);

        int goal = -1;
        float far = -1f;
        var near = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
        {
            float cost = fromStart.CostAt(c);
            if (float.IsPositiveInfinity(cost)) continue;
            if (cost <= startRadius) near.Add(c);
            if (g.LevelAt(c % g.Width, c / g.Width) >= 1 && cost > far)
            {
                far = cost;
                goal = c;
            }
        }
        Assert.True(goal >= 0, "the map has no reachable plateau");

        var rng = new SimRng(seed, 47);
        for (int i = 0; i < units; i++)
        {
            int cell = near[rng.NextInt(0, near.Count)];
            // Stay 0.05 m inside the cell so float rounding can't put the spawn in a neighbor.
            var offset = new Vector2(0.05f + rng.NextFloat() * 1.9f, 0.05f + rng.NextFloat() * 1.9f);
            Vector2 corner = MoveScenario.Center(g, cell) - new Vector2(MapConstants.CellSize / 2);
            sim.Enqueue(Command.SpawnUnit(players == 1 ? 0 : i % players, i % TestSim.UnitTypeCount, corner + offset));
        }
        sim.Tick();
        sim.Tick();
        Assert.Equal(units, sim.World.Units.Count);
        return new CrossMapScenario(sim, start, goal, FlowField.Build(g, goal));
    }

    /// <summary>Orders every unit to <see cref="Goal"/> (one Move per unit from its owner); they apply on the next tick.</summary>
    public void OrderAll() => MoveScenario.MoveAll(Sim, Goal);

    /// <summary>True while any live unit is Moving.</summary>
    public bool AnyMoving()
    {
        UnitStore u = Sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.State[i] == UnitState.Moving) return true;
        return false;
    }

    /// <summary>Live units that gave up (Idle with no goal cell).</summary>
    public int GaveUp()
    {
        UnitStore u = Sim.World.Units;
        int n = 0;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.State[i] == UnitState.Idle && u.GoalCell[i] == -1) n++;
        return n;
    }

    /// <summary>A one-line description for test output.</summary>
    public string Describe() =>
        $"start cell ({StartCell % Sim.World.NavGrid.Width}, {StartCell / Sim.World.NavGrid.Width}) level {StartLevel}, " +
        $"goal cell ({GoalCell % Sim.World.NavGrid.Width}, {GoalCell / Sim.World.NavGrid.Width}) level {GoalLevel}, " +
        $"path {PathMeters:F0} m ({LevelChanges} level changes, {RampCellsOnPath} ramp cells), longest {LongestCost * MapConstants.CellSize:F0} m, " +
        $"limit {LimitTicks} ticks";
}
