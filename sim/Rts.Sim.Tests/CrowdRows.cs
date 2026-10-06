using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;
using Rts.Sim.Pathfinding;

namespace Rts.Sim.Tests;

/// <summary>
/// The crowd rows of docs/03 "Local movement" (M1-4d-3): crowds to 4 close points, and 128 units to
/// 64 neighboring goals. Shared by the dev tests, the stress rows and QA's sweeps, so every bound is
/// measured on the same scenario.
/// </summary>
/// <remarks>
/// <see cref="MoveScenario.Spawn"/> alternates owners in spawn order, but commands apply sorted by
/// (player, sequence), so player 0 takes the low slots and player 1 the high ones. Picking a goal by
/// <c>slot % points</c> therefore sends both players to every point. Since BUG-0037 two players at
/// one point are enemies contesting it, not one blob: the rows give each point (or goal) one player,
/// which is what the stress test always meant ("neighboring points belong to different players").
/// The old both-players-everywhere variant stays as a report.
/// </remarks>
public static class CrowdRows
{
    /// <summary>The 4 points 6 m apart round <paramref name="c"/>: (-3,-3), (3,-3), (-3,3), (3,3).</summary>
    public static Vector2[] FourPoints(Vector2 c) =>
        new[] { c + new Vector2(-3f, -3f), c + new Vector2(3f, -3f), c + new Vector2(-3f, 3f), c + new Vector2(3f, 3f) };

    /// <summary>
    /// Which of the 4 points slot <paramref name="i"/> goes to. One player per point: player 0 takes
    /// (-3,-3) and (3,3), player 1 (3,-3) and (-3,3), alternating by slot, so the two points next to
    /// each point are the enemy's. Otherwise <c>i % 4</c>: both players at every point.
    /// </summary>
    public static int PointOf(UnitStore u, int i, bool onePlayerPerPoint)
    {
        if (!onePlayerPerPoint) return i % 4;
        if (u.Owner[i] == 0) return i % 2 == 0 ? 0 : 3;
        return i % 2 == 0 ? 1 : 2;
    }

    /// <summary>Outcome of a crowd row once nothing moves (or the tick limit hit).</summary>
    public readonly record struct Result(int Arrived, int GaveUp, int StillMoving, int Ticks, int MaxBuildsPerTick, string? Pack);

    /// <summary>
    /// <paramref name="units"/> units (owners alternate, <paramref name="players"/> players) to the 4
    /// points, ticked until none moves or <paramref name="limit"/> ticks. <paramref name="everyTick"/>
    /// (optional) checks invariants after each tick and returns an error or null.
    /// </summary>
    public static Result ToFourPoints(ulong seed, int units, int limit, bool onePlayerPerPoint, int players = 2, Func<World, string?>? everyTick = null)
    {
        Simulation sim = MoveScenario.Spawn(seed, units, units > 1000 ? 70f : 40f, out int goalCell, players: players);
        World w = sim.World;
        Vector2[] goals = FourPoints(MoveScenario.Center(w.NavGrid, goalCell));
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
            sim.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), goals[PointOf(u, i, onePlayerPerPoint)]));
        return Run(sim, limit, everyTick);
    }

    /// <summary>
    /// 128 units to the 64 cells nearest the central cell by path (more goals than the 32 cache slots,
    /// BUG-0018). One player per goal: each player's units take every other goal, two units per goal.
    /// Otherwise <c>slot % 64</c>, which gives every goal one unit of each player.
    /// </summary>
    public static Result MoreGoalsThanCacheSlots(ulong seed, bool onePlayerPerGoal = true, Func<World, string?>? everyTick = null)
    {
        Simulation sim = MoveScenario.Spawn(seed, units: 128, maxCost: 15f, out int center);
        NavGrid g = sim.World.NavGrid;
        FlowField near = FlowField.Build(g, center);
        var goals = new List<int>();
        for (int c = 0; c < g.Width * g.Height && goals.Count < 64; c++)
            if (near.CostAt(c) <= 15f) goals.Add(c);
        Assert.Equal(64, goals.Count);
        UnitStore u = sim.World.Units;
        int firstOfPlayer1 = 0;
        while (firstOfPlayer1 < u.Capacity && u.Owner[firstOfPlayer1] == 0) firstOfPlayer1++;
        for (int i = 0; i < u.Capacity; i++)
        {
            int k = !onePlayerPerGoal ? i % 64 : u.Owner[i] == 0 ? 2 * i % 64 : (2 * (i - firstOfPlayer1) + 1) % 64;
            sim.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), MoveScenario.Center(g, goals[k])));
        }
        return Run(sim, 3000, everyTick);
    }

    private static Result Run(Simulation sim, int limit, Func<World, string?>? everyTick)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        sim.Tick(); // the Moves are stamped for the next tick
        int ticks = 0, moving, maxBuilds = 0;
        do
        {
            int before = w.FlowFields.BuildCount;
            sim.Tick();
            ticks++;
            maxBuilds = Math.Max(maxBuilds, w.FlowFields.BuildCount - before);
            string? err = everyTick?.Invoke(w);
            Assert.True(err == null, $"tick {ticks}: {err}");
            moving = 0;
            for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.State[i] == UnitState.Moving) moving++;
        } while (moving > 0 && ticks < limit);
        bool[] arrived = MoveScenario.Arrived(w);
        int arrivedCount = 0, gaveUp = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (arrived[i]) arrivedCount++;
            else if (u.GoalCell[i] == -1) gaveUp++;
        }
        return new Result(arrivedCount, gaveUp, moving, ticks, maxBuilds, MoveScenario.FirstPackViolation(w));
    }

    /// <summary>Fails if <see cref="MovementConstants.MaxFieldBuildsPerTick"/> was ever exceeded.</summary>
    public static void AssertBuildCap(Result r) =>
        Assert.True(r.MaxBuildsPerTick <= MovementConstants.MaxFieldBuildsPerTick, $"{r.MaxBuildsPerTick} field builds in one tick");
}
