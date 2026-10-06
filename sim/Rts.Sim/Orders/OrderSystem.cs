using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;
using Rts.Sim.Pathfinding;

namespace Rts.Sim.Orders;

/// <summary>
/// Unit orders (docs/03 "Orders and unit states"): applies Move, AttackMove, Stop, HoldPosition and
/// Gather commands in phase 1, and in phase 7 starts the next shift-queued order of every Idle unit.
/// </summary>
/// <remarks>
/// An unqueued order replaces the unit's queue and clears Hold; a queued one is appended. A popped
/// order goes through the same code as an unqueued one, except that a popped Move or AttackMove
/// keeps the rest of the queue (Stop and HoldPosition are terminal and drop it).
/// </remarks>
public static class OrderSystem
{
    /// <summary>Phase 7: every live Idle unit with a queued order, in slot order, starts its next one.</summary>
    /// <remarks>One order per unit per tick; a unit that arrives or gives up in movement starts its next leg on the next tick.</remarks>
    public static void Run(World world)
    {
        UnitStore u = world.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.QueueCount[i] == 0 || u.State[i] != UnitState.Idle) continue;
            int head = i * OrderConstants.QueueCapacity;
            CommandKind kind = u.QueueKind[head];
            Vector2 target = u.QueuePosition[head];
            Pop(u, i);
            Execute(world, i, kind, target);
        }
    }

    /// <summary>Phase 1: applies a valid unit-order command; dropped for a dead or recycled unit or another player's.</summary>
    internal static void Apply(World world, in Command command)
    {
        UnitStore u = world.Units;
        if (!u.IsAlive(command.Unit) || u.Owner[command.Unit.Index] != command.Player) return;
        int i = command.Unit.Index;
        bool positional = command.Kind is CommandKind.Move or CommandKind.AttackMove or CommandKind.Gather;
        // A target off the map is dropped whether queued or not, so it never takes a queue entry.
        if (positional && !world.NavGrid.WorldToCell(command.Position, out _, out _)) return;
        // Only workers gather (M3-2); a Gather to anyone else is dropped, queued or not.
        if (command.Kind == CommandKind.Gather && !EconomySystem.IsWorker(world, i)) return;
        if (command.IsQueued)
        {
            Append(u, i, command.Kind, positional ? command.Position : Vector2.Zero);
            return;
        }
        if (command.Kind == CommandKind.Gather)
        {
            // No node to work drops the whole command, like a Move with no passable target.
            int node = EconomySystem.ResolveNode(world, command.Position);
            if (node < 0) return;
            u.ClearQueue(i);
            EconomySystem.StartGather(world, i, node);
            return;
        }
        if (positional)
        {
            // A target that resolves to no passable cell drops the whole command: queue and Hold stay.
            if (!ResolveTarget(world.NavGrid, command.Position, out int cell, out Vector2 goal)) return;
            u.ClearQueue(i);
            u.Hold[i] = false;
            EndLoop(u, i);
            Move(world, i, cell, goal);
            return;
        }
        Execute(world, i, command.Kind, Vector2.Zero);
    }

    /// <summary>Starts an order now: the unqueued semantics, minus clearing the queue for Move and AttackMove.</summary>
    private static void Execute(World world, int i, CommandKind kind, Vector2 target)
    {
        UnitStore u = world.Units;
        switch (kind)
        {
            case CommandKind.Move:
            case CommandKind.AttackMove: // walks like a Move until combat targeting (M4)
                if (!ResolveTarget(world.NavGrid, target, out int cell, out Vector2 goal)) return;
                u.Hold[i] = false;
                EndLoop(u, i);
                Move(world, i, cell, goal);
                break;
            case CommandKind.Gather:
                // Popped from the queue: the rest of the queue stays, for when the loop ends.
                int node = EconomySystem.ResolveNode(world, target);
                if (node >= 0) EconomySystem.StartGather(world, i, node);
                break;
            case CommandKind.Stop:
                u.ClearQueue(i);
                Stop(u, i);
                u.Hold[i] = false;
                break;
            case CommandKind.HoldPosition:
                u.ClearQueue(i);
                Stop(u, i);
                u.Hold[i] = true;
                break;
        }
    }

    private static void Append(UnitStore u, int i, CommandKind kind, Vector2 target)
    {
        int n = u.QueueCount[i];
        if (n >= OrderConstants.QueueCapacity) return; // full: dropped silently
        int at = i * OrderConstants.QueueCapacity + n;
        u.QueueKind[at] = kind;
        u.QueuePosition[at] = target;
        u.QueueCount[i] = n + 1;
    }

    /// <summary>Removes the head entry, shifting the rest forward; the freed last entry goes back to default.</summary>
    private static void Pop(UnitStore u, int i)
    {
        int head = i * OrderConstants.QueueCapacity;
        int n = u.QueueCount[i];
        for (int k = 1; k < n; k++)
        {
            u.QueueKind[head + k - 1] = u.QueueKind[head + k];
            u.QueuePosition[head + k - 1] = u.QueuePosition[head + k];
        }
        u.QueueKind[head + n - 1] = default;
        u.QueuePosition[head + n - 1] = default;
        u.QueueCount[i] = n - 1;
    }

    /// <summary>Any other order ends a gather loop (cargo kept); a worker standing on it counts as Idle for the order's same-target rule.</summary>
    private static void EndLoop(UnitStore u, int i)
    {
        u.GatherNode[i] = default;
        if (u.State[i] is UnitState.Gathering or UnitState.Returning) u.State[i] = UnitState.Idle;
    }

    /// <summary>Stands the unit still with no goal: Idle, shovable, nothing to walk back to, no gather loop (cargo kept).</summary>
    private static void Stop(UnitStore u, int i)
    {
        EndLoop(u, i);
        u.State[i] = UnitState.Idle;
        u.GoalCell[i] = -1;
        u.Velocity[i] = Vector2.Zero;
        u.StuckTicks[i] = 0;
        u.BestRemaining[i] = float.PositiveInfinity;
        u.WalkBack[i] = UnitStore.WalkBackNone;
    }

    /// <summary>
    /// The goal cell and point for a move target: its own cell, or (blocked) the nearest passable
    /// cell's center, the flow field's rule. False when off the map or nothing is passable.
    /// </summary>
    private static bool ResolveTarget(NavGrid grid, Vector2 target, out int cell, out Vector2 goal)
    {
        goal = target;
        cell = -1;
        if (!grid.WorldToCell(target, out int x, out int y)) return false;
        cell = y * grid.Width + x;
        if (grid.IsPassable(x, y)) return true;
        cell = FlowField.NearestPassable(grid, cell);
        if (cell < 0) return false;
        goal = grid.CellCenter(cell % grid.Width, cell / grid.Width);
        return true;
    }

    /// <summary>
    /// The gather loop's walk (M3-2): a fresh Move to <paramref name="goal"/> in <paramref name="cell"/> (passable),
    /// without the same-target rule of a player's re-order, so a worker waiting at a mine's edge really walks
    /// again. Keeps the queue, Hold (already clear) and the gather loop.
    /// </summary>
    internal static void Walk(World world, int i, int cell, Vector2 goal)
    {
        UnitStore u = world.Units;
        u.State[i] = UnitState.Moving;
        u.Goal[i] = goal;
        u.GoalCell[i] = cell;
        u.OrderTick[i] = world.TickNumber;
        u.StuckTicks[i] = 0;
        u.BestRemaining[i] = float.PositiveInfinity;
        u.WalkBack[i] = UnitStore.WalkBackNone;
    }

    private static void Move(World world, int i, int cell, Vector2 goal)
    {
        UnitStore u = world.Units;
        const float same2 = MovementConstants.ArrivalDistance * MovementConstants.ArrivalDistance;
        if (u.GoalCell[i] == cell && (u.State[i] == UnitState.Moving || Vector2.DistanceSquared(goal, u.Goal[i]) <= same2))
        {
            // The order the unit already has (click spam, an AI refreshing its orders): it doesn't
            // restart. A Moving one takes the new point but keeps its order age and stuck count, so
            // spam (even jittered within the cell) can't keep it Moving forever (BUG-0029). An Idle
            // unit that kept its goal cell has arrived: a point within ArrivalDistance of its goal is
            // where it already is, so it stays put; a point farther away in the same cell is a new
            // order, since a player's short repositioning must move it (BUG-0030).
            if (u.State[i] == UnitState.Moving)
            {
                // The progress estimate depends on the goal point only where the unit aims at the goal
                // itself (in its goal cell, or one legal step from it); move the best by exactly what
                // the new point changes there, so a re-order neither passes for progress (a jittered
                // re-order keeping a blocked unit Moving) nor costs a walking unit its progress (BUG-0043).
                if (float.IsFinite(u.BestRemaining[i])) u.BestRemaining[i] += GoalEstimateShift(world, i, u.Goal[i], goal);
                u.Goal[i] = goal;
            }
            return;
        }
        Walk(world, i, cell, goal);
    }

    /// <summary>
    /// How much unit <paramref name="i"/>'s progress estimate (MovementSystem.Plan) changes where it
    /// stands if its goal moves from <paramref name="from"/> to <paramref name="to"/> in the same cell:
    /// nonzero only when it aims at the goal itself (in the goal cell, or a legal step from it), where
    /// the estimate is the field cost, less the goal's offset from this cell's center, plus the distance to it.
    /// </summary>
    private static float GoalEstimateShift(World world, int i, Vector2 from, Vector2 to)
    {
        UnitStore u = world.Units;
        NavGrid grid = world.NavGrid;
        Vector2 pos = u.Position[i];
        int goalCell = u.GoalCell[i];
        if (!grid.WorldToCell(pos, out int cx, out int cy)) return 0f;
        if (cy * grid.Width + cx != goalCell && !MovementSystem.IsLegalStep(grid, cx, cy, goalCell % grid.Width, goalCell / grid.Width)) return 0f;
        Vector2 center = grid.CellCenter(cx, cy);
        return (Vector2.Distance(to, pos) - Vector2.Distance(to, center)) - (Vector2.Distance(from, pos) - Vector2.Distance(from, center));
    }
}
