using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;
using Rts.Sim.Pathfinding;

namespace Rts.Sim.Orders;

/// <summary>
/// Unit orders (docs/03 "Orders and unit states"): applies Move, AttackMove, Stop, HoldPosition,
/// Gather, Build, Repair and Attack commands in phase 1, and in phase 7 starts the next shift-queued order of every Idle unit.
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
            // A unit with a target is fighting (an arrived chaser stands Idle a tick): its queue waits for the fight to end.
            if (!u.Alive[i] || u.QueueCount[i] == 0 || u.State[i] != UnitState.Idle || u.Target[i].Generation != 0) continue;
            int head = i * OrderConstants.QueueCapacity;
            CommandKind kind = u.QueueKind[head];
            Vector2 target = u.QueuePosition[head];
            int typeId = u.QueueTypeId[head];
            EntityHandle attackTarget = kind == CommandKind.Attack ? u.QueuedTarget(head) : default;
            Pop(u, i);
            Execute(world, i, kind, target, typeId, attackTarget);
        }
    }

    /// <summary>Phase 1: applies a valid unit-order command; dropped for a dead or recycled unit or another player's.</summary>
    internal static void Apply(World world, in Command command)
    {
        UnitStore u = world.Units;
        if (!u.IsAlive(command.Unit) || u.Owner[command.Unit.Index] != command.Player) return;
        int i = command.Unit.Index;
        bool positional = command.Kind is CommandKind.Move or CommandKind.AttackMove or CommandKind.Gather or CommandKind.Build or CommandKind.Repair;
        // A target off the map is dropped whether queued or not, so it never takes a queue entry.
        if (positional && !world.NavGrid.WorldToCell(command.Position, out _, out _)) return;
        // Only workers gather, build and repair (M3-2, M3-3); such an order to anyone else is dropped, queued or not.
        bool workerOrder = command.Kind is CommandKind.Gather or CommandKind.Build or CommandKind.Repair;
        if (workerOrder && !EconomySystem.IsWorker(world, i)) return;
        // An Attack on nothing it may fight is dropped, queued or not (M4-2a); a queued one is checked again when it starts.
        if (command.Kind == CommandKind.Attack && !CombatSystem.MayAttack(world, i, command.Target, command.TargetIsBuilding)) return;
        if (command.IsQueued)
        {
            int at = Append(u, i, command.Kind, positional ? command.Position : Vector2.Zero, command.Kind == CommandKind.Build ? command.TypeId : 0);
            if (at >= 0 && command.Kind == CommandKind.Attack) u.SetQueuedTarget(at, command.Target, command.TargetIsBuilding);
            return;
        }
        if (command.Kind == CommandKind.Attack)
        {
            u.ClearQueue(i);
            // The target it already fights (click spam, an AI refreshing its orders): the swing in progress and the chase go
            // on (BUG-0152); only the mode becomes Ordered, as the order says.
            if (u.Target[i].Generation != 0 && u.Target[i] == command.Target && u.TargetIsBuilding[i] == command.TargetIsBuilding)
            {
                u.Hold[i] = false;
                CombatSystem.ReaffirmAttack(world, i);
                return;
            }
            StartAttack(world, i, command.Target, command.TargetIsBuilding);
            return;
        }
        // A Build or Repair with nothing to build or repair drops the whole command: queue and Hold stay.
        if (command.Kind == CommandKind.Build)
        {
            if (ConstructionSystem.StartBuild(world, i, command.TypeId, command.Position, replaceQueue: true)) CombatSystem.ClearForOrder(u, i);
            return;
        }
        if (command.Kind == CommandKind.Repair)
        {
            if (ConstructionSystem.StartRepair(world, i, command.Position, replaceQueue: true)) CombatSystem.ClearForOrder(u, i);
            return;
        }
        if (command.Kind == CommandKind.Gather)
        {
            // No node to work drops the whole command, like a Move with no passable target.
            int node = EconomySystem.ResolveNode(world, command.Position);
            if (node < 0) return;
            u.ClearQueue(i);
            CombatSystem.ClearForOrder(u, i);
            EconomySystem.StartGather(world, i, node);
            return;
        }
        if (positional)
        {
            // A target that resolves to no passable cell drops the whole command: queue and Hold stay.
            if (!ResolveTarget(world.NavGrid, command.Position, out int cell, out Vector2 goal)) return;
            u.ClearQueue(i);
            if (command.Kind == CommandKind.AttackMove && world.CombatEnabled)
            {
                // Re-issuing an attack-move (click spam, an AI refreshing its orders) never throws a fight away (BUG-0152):
                // on the leg it already walks a fight on the way goes on, chase and all. Any other point is the player
                // asking for a re-pick (BUG-0154): a unit fighting in reach or mid-swing re-picks by priority in this
                // tick's phase 7, keeping its swing only if the pick is the target it has. Only the leg's own point (within
                // ArrivalDistance, the Move rule's "already there") skips the re-pick; a new cell also forgets the give-up
                // memory, as any new order does. It walks to the leg's end when the fight is over.
                bool sameLeg = OnLegTo(world, i, cell);
                if (u.Target[i].Generation != 0 && (sameLeg || CombatSystem.FightsInReach(world, i)))
                {
                    const float same2 = MovementConstants.ArrivalDistance * MovementConstants.ArrivalDistance;
                    bool samePoint = sameLeg && Vector2.DistanceSquared(goal, u.AnchorPosition[i]) <= same2;
                    u.Hold[i] = false;
                    CombatSystem.KeepFightForAttackMove(u, i, goal, newOrder: !sameLeg, repick: !samePoint);
                    return;
                }
                if (sameLeg)
                {
                    // Unengaged on that leg: the Move rule's same-target case (no restart), its give-up memory kept.
                    Move(world, i, cell, goal);
                    CombatSystem.StartAttackMove(u, i, u.Goal[i]);
                    return;
                }
            }
            u.Hold[i] = false;
            EndLoop(u, i);
            CombatSystem.ClearForOrder(u, i);
            Move(world, i, cell, goal);
            if (command.Kind == CommandKind.AttackMove && world.CombatEnabled) CombatSystem.StartAttackMove(u, i, u.Goal[i]);
            return;
        }
        Execute(world, i, command.Kind, Vector2.Zero, 0, default);
    }

    /// <summary>Starts an order now: the unqueued semantics, minus clearing the queue for Move, AttackMove, Gather, Build, Repair and Attack.</summary>
    private static void Execute(World world, int i, CommandKind kind, Vector2 target, int typeId, EntityHandle attackTarget)
    {
        UnitStore u = world.Units;
        switch (kind)
        {
            case CommandKind.Move:
            case CommandKind.AttackMove: // M4-1: walks like a Move, scanning on the way (CombatSystem)
                if (!ResolveTarget(world.NavGrid, target, out int cell, out Vector2 goal)) return;
                u.Hold[i] = false;
                EndLoop(u, i);
                CombatSystem.ClearForOrder(u, i);
                Move(world, i, cell, goal);
                if (kind == CommandKind.AttackMove && world.CombatEnabled) CombatSystem.StartAttackMove(u, i, u.Goal[i]);
                break;
            case CommandKind.Gather:
                // Popped from the queue: the rest of the queue stays, for when the loop ends.
                int node = EconomySystem.ResolveNode(world, target);
                if (node < 0) break;
                CombatSystem.ClearForOrder(u, i);
                EconomySystem.StartGather(world, i, node);
                break;
            case CommandKind.Build: // popped: the rest of the queue stays, as for Gather
                if (ConstructionSystem.StartBuild(world, i, typeId, target, replaceQueue: false)) CombatSystem.ClearForOrder(u, i);
                break;
            case CommandKind.Repair:
                if (ConstructionSystem.StartRepair(world, i, target, replaceQueue: false)) CombatSystem.ClearForOrder(u, i);
                break;
            case CommandKind.Attack: // popped: dropped if the target died or turned invalid while it waited; the rest of the queue stays
                if (CombatSystem.MayAttack(world, i, attackTarget, typeId == 1)) StartAttack(world, i, attackTarget, typeId == 1);
                break;
            case CommandKind.Stop:
                CombatSystem.ClearForOrder(u, i);
                u.ClearQueue(i);
                Stop(u, i);
                u.Hold[i] = false;
                break;
            case CommandKind.HoldPosition:
                CombatSystem.ClearForOrder(u, i);
                u.ClearQueue(i);
                Stop(u, i);
                u.Hold[i] = true;
                break;
        }
    }

    /// <summary>Whether unit <paramref name="i"/> walks (or fights on) an attack-move leg whose end resolves to <paramref name="cell"/>.</summary>
    private static bool OnLegTo(World world, int i, int cell)
    {
        UnitStore u = world.Units;
        return u.Mode[i] == CombatMode.AttackMove
            && ResolveTarget(world.NavGrid, u.AnchorPosition[i], out int legCell, out _) && legCell == cell;
    }

    /// <summary>Appends an entry to unit <paramref name="i"/>'s queue; returns its index in the flat queue arrays, or -1 when the queue is full (the order is dropped silently).</summary>
    private static int Append(UnitStore u, int i, CommandKind kind, Vector2 target, int typeId)
    {
        int n = u.QueueCount[i];
        if (n >= OrderConstants.QueueCapacity) return -1;
        int at = i * OrderConstants.QueueCapacity + n;
        u.QueueKind[at] = kind;
        u.QueuePosition[at] = target;
        u.QueueTypeId[at] = typeId;
        u.QueueCount[i] = n + 1;
        return at;
    }

    /// <summary>
    /// Starts an explicit Attack (M4-2a; the target already checked with <c>CombatSystem.MayAttack</c>): Hold, loops and the
    /// old engagement end, the unit stops where it stands, and phase 7 chases the target from this tick on.
    /// </summary>
    private static void StartAttack(World world, int i, EntityHandle target, bool isBuilding)
    {
        UnitStore u = world.Units;
        CombatSystem.ClearForOrder(u, i);
        Stop(u, i);
        u.Hold[i] = false;
        CombatSystem.StartAttack(world, i, target, isBuilding);
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
            u.QueueTypeId[head + k - 1] = u.QueueTypeId[head + k];
        }
        u.QueueKind[head + n - 1] = default;
        u.QueuePosition[head + n - 1] = default;
        u.QueueTypeId[head + n - 1] = default;
        u.QueueCount[i] = n - 1;
    }

    /// <summary>Any other order ends a gather loop (cargo kept) or a build / repair order; a worker standing on one counts as Idle for the order's same-target rule.</summary>
    private static void EndLoop(UnitStore u, int i)
    {
        u.GatherNode[i] = default;
        u.BuildTarget[i] = default;
        if (u.State[i] is UnitState.Gathering or UnitState.Returning or UnitState.Building) u.State[i] = UnitState.Idle;
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
    internal static bool ResolveTarget(NavGrid grid, Vector2 target, out int cell, out Vector2 goal)
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

    /// <summary>The Move rule to <paramref name="target"/> for unit <paramref name="i"/> without touching its queue, Hold or loops (a trained unit sent to its rally point, M3-4); false, nothing changed, for a target with no passable cell.</summary>
    internal static bool MoveTo(World world, int i, Vector2 target)
    {
        if (!ResolveTarget(world.NavGrid, target, out int cell, out Vector2 goal)) return false;
        Move(world, i, cell, goal);
        return true;
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
