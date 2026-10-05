using System;
using System.Numerics;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;

namespace Rts.Sim.Movement;

/// <summary>Tick phases 8-9: walks Moving units along their goal's flow field at their data speed, with separation, crowded arrival and giving up (docs/03 "Local movement").</summary>
/// <remarks>
/// Units are grouped by goal cell, then slot, so each goal's field is fetched once per tick. A
/// build pass first serves goals without a cached field, oldest order first, at most
/// <see cref="MovementConstants.MaxFieldBuildsPerTick"/> per tick; units whose field is still
/// missing wait. Which goals get built (and so which units wait) depends on order ticks, goal cells
/// and the hashed cache state, never on slot order alone.
/// <para>
/// Then two passes: every Moving unit plans its step from start-of-tick positions and states
/// (flow or direct aim, sidestep, overlap push, arrival), and only then are the plans applied. So
/// separation is symmetric and no unit's result depends on which unit was walked first. Not yet:
/// shoving idle units aside, formation offsets, <c>Stop</c>/<c>Hold</c> (M1-4d-2 and later).
/// </para>
/// </remarks>
public static class MovementSystem
{
    // Planned outcomes, written by Plan and carried out by Apply.
    private const byte ActWalk = 0;    // take PlannedStep: progress, the stuck count resets
    private const byte ActStuck = 1;   // take PlannedStep (possibly zero: refused): a stuck tick
    private const byte ActBackOff = 2; // too tight to stop: take PlannedStep; a stuck tick, but keeps the goal at the limit
    private const byte ActWait = 3;    // no field yet: hold still, keep the order
    private const byte ActArrive = 4;  // stop here; keep GoalCell so later units can pack against it
    private const byte ActAbandon = 5; // off the map or no route: stop and drop the goal

    /// <summary>Advances every Moving unit by one tick.</summary>
    public static void Run(World world)
    {
        UnitStore u = world.Units;
        long[] order = world.MoveOrder;
        int n = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (u.Alive[i] && u.State[i] == UnitState.Moving)
                order[n++] = ((long)u.GoalCell[i] << 32) | (uint)i;
        }
        // Fetching per unit in slot order made LRU evict the very field the next unit needed once
        // live goals outnumbered cache slots: a full build per unit per tick (BUG-0018).
        Array.Sort(order, 0, n);
        BuildMissingFields(world, n);
        Plan(world, n);
        Apply(world, n);
    }

    /// <summary>Plans every Moving unit's outcome and step from start-of-tick state; writes only the world's scratch arrays (and LRU touches).</summary>
    private static void Plan(World world, int n)
    {
        UnitStore u = world.Units;
        NavGrid grid = world.NavGrid;
        FlowFieldCache cache = world.FlowFields;
        long[] order = world.MoveOrder;
        int[] near = world.Neighbors;
        Vector2[] planned = world.PlannedStep;
        float[] remaining = world.PlannedRemaining;
        byte[] action = world.PlannedAction;
        float queryExtra = world.MaxUnitRadius + MovementConstants.AvoidRange;

        const float arrival2 = MovementConstants.ArrivalDistance * MovementConstants.ArrivalDistance;
        int groupCell = -1;
        FlowField? field = null;
        bool fetched = false;
        for (int k = 0; k < n; k++)
        {
            int i = (int)(order[k] & 0xFFFFFFFF);
            int goalCell = (int)(order[k] >> 32);
            if (goalCell != groupCell)
            {
                groupCell = goalCell;
                fetched = false;
            }
            planned[i] = Vector2.Zero;
            Vector2 pos = u.Position[i];
            Vector2 goal = u.Goal[i];
            if (!grid.WorldToCell(pos, out int cx, out int cy))
            {
                // Off the map, where no field can lead anywhere.
                action[i] = ActAbandon;
                continue;
            }

            // Neighbors, read at their start-of-tick positions and states (ascending slot order).
            int count = world.Spatial.QueryRadius(pos, u.Radius[i] + queryExtra, near);

            // Where the unit wants to go. noAim is ActWalk when it has an aim point.
            int cell = cy * grid.Width + cx;
            bool inGoalCell = cell == goalCell;
            byte noAim = ActWalk;
            Vector2 aim = goal;
            if (!inGoalCell)
            {
                if (!fetched)
                {
                    fetched = true;
                    field = cache.TryGetCached(goalCell);
                }
                if (field == null)
                {
                    // Over this tick's build cap: wait, still Moving, for a later tick's build.
                    noAim = ActWait;
                }
                else
                {
                    byte d = field.DirectionAt(cell);
                    if (d == FlowField.NoDirection)
                    {
                        // Standing in a blocked cell (e.g. spawned on a cliff) or one the goal can't reach.
                        noAim = ActAbandon;
                    }
                    else if (!IsLegalStep(grid, cx, cy, goalCell % grid.Width, goalCell / grid.Width))
                    {
                        // The next cell's center: a straight line there stays inside this cell, the next
                        // one and (for a diagonal) the two side cells, which no-corner-cutting keeps passable.
                        int ax = cx + FlowField.OffsetX(d), ay = cy + FlowField.OffsetY(d);
                        aim = grid.CellCenter(ax, ay);
                        if (AimCovered(u, i, aim, near, count, goalCell))
                        {
                            // A unit stands on that center: walking at it would pin us against it, so
                            // aim one cell further along the field (the step check still guards walls).
                            byte d2 = field.DirectionAt(ay * grid.Width + ax);
                            aim = d2 == FlowField.NoDirection
                                ? goal
                                : grid.CellCenter(ax + FlowField.OffsetX(d2), ay + FlowField.OffsetY(d2));
                        }
                    }
                    // Otherwise the goal cell is one legal step away: aim straight at the goal, a line
                    // that stays inside the same cells.
                }
            }

            float speed = u.Speed[i];
            Vector2 forward = Vector2.Zero, flowStep = Vector2.Zero;
            if (noAim == ActWalk)
            {
                Vector2 delta = aim - pos;
                float dist = delta.Length();
                if (dist > 0f)
                {
                    forward = delta / dist;
                    flowStep = forward * MathF.Min(speed, dist);
                }
            }

            float ri = u.Radius[i];
            Vector2 push = Vector2.Zero, side = Vector2.Zero;
            float maxShare = 0f;
            bool touchingArrived = false, crowded = false;
            for (int m = 0; m < count; m++)
            {
                int j = near[m];
                if (j == i || !u.Alive[j]) continue;
                Vector2 away = pos - u.Position[j];
                float d2 = away.LengthSquared();
                float sum = ri + u.Radius[j];
                float spacing = MovementConstants.ArrivalSpacing * sum;
                if (d2 < spacing * spacing) crowded = true;
                bool idle = u.State[j] != UnitState.Moving;
                // An Idle unit that kept this goal cell arrived there: the group's blob.
                bool groupmate = idle && u.GoalCell[j] == goalCell;
                // Other units that didn't move last tick (idle, waiting for a field, refused) are
                // walls, handled by Constrain below; walkers get the soft push.
                bool standing = idle || u.Velocity[j] == Vector2.Zero;
                if (d2 < sum * sum && (!standing || groupmate))
                {
                    float dist = MathF.Sqrt(d2);
                    Vector2 dir = dist > 0f ? away / dist : CoincidentDirection(i, j);
                    float share = (sum - dist) * (groupmate ? MovementConstants.SeparationShareStill : MovementConstants.SeparationShareMoving);
                    push += dir * share;
                    if (share > maxShare) maxShare = share;
                    // Only through a legal step, so a blob never reaches across a blocked corner (BUG-0020).
                    if (groupmate && !touchingArrived && grid.WorldToCell(u.Position[j], out int jx, out int jy)
                        && IsLegalStep(grid, cx, cy, jx, jy))
                        touchingArrived = true;
                }
                if (forward != Vector2.Zero && !groupmate)
                {
                    // Sidestep a unit ahead that is standing or coming at us, before touching it.
                    // (One walking our way is left alone: followers don't fan out.)
                    float reach = sum + MovementConstants.AvoidRange;
                    Vector2 toJ = -away;
                    if (d2 < reach * reach && Vector2.Dot(toJ, forward) > 0f && (standing || Vector2.Dot(u.Velocity[j], forward) < 0f))
                    {
                        float w = MathF.Min(1f, (reach - MathF.Sqrt(d2)) / MovementConstants.AvoidRange);
                        var right = new Vector2(forward.Y, -forward.X);
                        // Step away from the side it is on; dead ahead, step right. Both units of a
                        // head-on pair step right, which are opposite ways, so they pass.
                        float cross = forward.X * toJ.Y - forward.Y * toJ.X;
                        side += (cross >= 0f ? right : -right) * (w * speed);
                    }
                }
            }
            // Many neighbors pushing one way must not add up to more than the deepest overlap, or a
            // packed unit overshoots and jitters.
            float pushLength = push.Length();
            if (pushLength > maxShare) push *= maxShare / pushLength;

            bool atGoal = inGoalCell && Vector2.DistanceSquared(pos, goal) <= arrival2;
            if (atGoal || touchingArrived)
            {
                if (!crowded)
                {
                    action[i] = ActArrive;
                    continue;
                }
                // Too tight to stop here: back off along the push alone, and try again next tick.
                // Counts toward the stuck limit (BUG-0027); at the limit the unit stops anyway.
                Vector2 back = Constrain(u, i, pos, ClampLength(push, speed), near, count, goalCell);
                action[i] = ActBackOff;
                planned[i] = CanStep(grid, cx, cy, pos + back) ? back : Vector2.Zero;
                continue;
            }
            if (noAim != ActWalk)
            {
                action[i] = noAim;
                continue;
            }

            Vector2 step = Constrain(u, i, pos, ClampLength(flowStep + ClampLength(side, speed) + push, speed), near, count, goalCell);
            if (!CanStep(grid, cx, cy, pos + step))
            {
                // Refused: never step into a blocked cell or across a blocked corner. Slide along the
                // wall (keep one axis of the step, the one gaining more along the aim), else retry
                // the flow step alone, so neighbors can't pin a unit against a wall.
                Vector2 alongX = Constrain(u, i, pos, new Vector2(step.X, 0f), near, count, goalCell);
                Vector2 alongY = Constrain(u, i, pos, new Vector2(0f, step.Y), near, count, goalCell);
                bool okX = CanStep(grid, cx, cy, pos + alongX), okY = CanStep(grid, cx, cy, pos + alongY);
                if (okX && (!okY || Vector2.Dot(alongX, forward) >= Vector2.Dot(alongY, forward))) step = alongX;
                else if (okY) step = alongY;
                else
                {
                    step = Constrain(u, i, pos, flowStep, near, count, goalCell);
                    if (!CanStep(grid, cx, cy, pos + step)) step = Vector2.Zero;
                }
            }
            planned[i] = step;
            // Progress: the estimated path meters left beat the best so far (or where the unit
            // stands now) by a margin. The estimate is this cell's field cost, less the way already
            // made from the cell's center toward the aim; it runs on nearly continuously across
            // cell edges. A best that only ever falls means jostling back and forth (even across a
            // cell edge, where the aim changes) never counts as progress.
            float cellCost = inGoalCell ? 0f : field!.CostAt(cell) * MapConstants.CellSize;
            float aimRemaining = cellCost - Vector2.Distance(aim, grid.CellCenter(cx, cy));
            float best = MathF.Min(u.BestRemaining[i], aimRemaining + Vector2.Distance(aim, pos));
            float after = aimRemaining + Vector2.Distance(aim, pos + step);
            bool progress = after < best - MovementConstants.StuckFraction * speed;
            action[i] = progress ? ActWalk : ActStuck;
            remaining[i] = progress ? after : best;
        }
    }

    /// <summary>Carries out the plans: moves units, stops arrivals, and counts stuck ticks toward giving up.</summary>
    private static void Apply(World world, int n)
    {
        UnitStore u = world.Units;
        long[] order = world.MoveOrder;
        Vector2[] planned = world.PlannedStep;
        float[] remaining = world.PlannedRemaining;
        byte[] action = world.PlannedAction;
        for (int k = 0; k < n; k++)
        {
            int i = (int)(order[k] & 0xFFFFFFFF);
            switch (action[i])
            {
                case ActWait:
                    u.Velocity[i] = Vector2.Zero;
                    break;
                case ActArrive:
                    Stop(u, i);
                    break;
                case ActAbandon:
                    Stop(u, i);
                    u.GoalCell[i] = -1;
                    break;
                default:
                    Vector2 step = planned[i];
                    u.Velocity[i] = step;
                    if (step != Vector2.Zero)
                    {
                        u.Position[i] += step;
                        u.Facing[i] = SimMath.Atan2(step.Y, step.X);
                    }
                    // Back-off leaves the progress estimate alone (moving away from the goal is not
                    // a new best), but it is not progress either, so it counts toward the limit:
                    // a back-off refused by a wall, or swinging in and out at a blob's edge, must
                    // end (BUG-0027).
                    if (action[i] != ActBackOff) u.BestRemaining[i] = remaining[i];
                    if (action[i] == ActWalk)
                        u.StuckTicks[i] = 0;
                    else if (++u.StuckTicks[i] >= MovementConstants.GiveUpTicks)
                    {
                        bool backingOff = action[i] == ActBackOff;
                        Stop(u, i);
                        // Blocked for a short time: give up where it stands and drop the goal, so
                        // it doesn't anchor a blob away from the goal. A unit still backing off is
                        // at its goal or touching its blob: it settles there, crowded, and keeps
                        // the goal so groupmates still pack against it.
                        if (!backingOff) u.GoalCell[i] = -1;
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Phase 8: touches every needed field that is cached, then builds the missing ones with the
    /// oldest orders, at most <see cref="MovementConstants.MaxFieldBuildsPerTick"/>, without allocating.
    /// </summary>
    /// <remarks>
    /// A goal group's age is its oldest unit's <see cref="UnitStore.OrderTick"/>; ties go to the lower
    /// goal cell (BUG-0022). Touching the hits first means a build evicts a field nobody used this
    /// tick whenever one exists. A group needs a field only if one of its units stands on the map
    /// outside the goal cell; the others arrive or stop without one.
    /// </remarks>
    private static void BuildMissingFields(World world, int n)
    {
        UnitStore u = world.Units;
        NavGrid grid = world.NavGrid;
        FlowFieldCache cache = world.FlowFields;
        long[] order = world.MoveOrder;
        long[] misses = world.FieldMisses;
        int missCount = 0;
        int k = 0;
        while (k < n)
        {
            int goalCell = (int)(order[k] >> 32);
            int oldest = int.MaxValue;
            bool needsField = false;
            for (; k < n && (int)(order[k] >> 32) == goalCell; k++)
            {
                int i = (int)(order[k] & 0xFFFFFFFF);
                if (u.OrderTick[i] < oldest) oldest = u.OrderTick[i];
                if (!needsField && grid.WorldToCell(u.Position[i], out int cx, out int cy) && cy * grid.Width + cx != goalCell)
                    needsField = true;
            }
            if (needsField && cache.TryGetCached(goalCell) == null)
                misses[missCount++] = ((long)oldest << 32) | (uint)goalCell;
        }
        Array.Sort(misses, 0, missCount);
        int builds = Math.Min(missCount, MovementConstants.MaxFieldBuildsPerTick);
        for (int b = 0; b < builds; b++)
            cache.Get((int)(misses[b] & 0xFFFFFFFF));
    }

    /// <summary>True if a unit in cell (x0, y0) may end a step in cell (x1, y1): the same cell or an 8-neighbor, passable, and for a diagonal both side cells passable.</summary>
    internal static bool IsLegalStep(NavGrid grid, int x0, int y0, int x1, int y1)
    {
        int dx = x1 - x0, dy = y1 - y0;
        if (dx < -1 || dx > 1 || dy < -1 || dy > 1 || !grid.IsPassable(x1, y1)) return false;
        return dx == 0 || dy == 0 || (grid.IsPassable(x0 + dx, y0) && grid.IsPassable(x0, y0 + dy));
    }

    /// <summary>
    /// Treats units standing still that aren't this unit's arrived groupmates as walls: removes the
    /// part of <paramref name="step"/> that would end inside one, and pushes out of an existing
    /// overlap. Collide-and-slide: a walker glances round them, and one pressed dead against them stops.
    /// </summary>
    /// <remarks>They don't move this tick, so the result depends only on start-of-tick state; neighbors are visited in ascending slot order.</remarks>
    private static Vector2 Constrain(UnitStore u, int i, Vector2 pos, Vector2 step, int[] near, int count, int goalCell)
    {
        float ri = u.Radius[i];
        float stepLength = step.Length();
        for (int m = 0; m < count; m++)
        {
            int j = near[m];
            if (j == i || !u.Alive[j]) continue;
            bool idle = u.State[j] != UnitState.Moving;
            if (idle ? u.GoalCell[j] == goalCell : u.Velocity[j] != Vector2.Zero) continue; // groupmate or walker: soft push instead
            Vector2 toJ = u.Position[j] - pos;
            float d2 = toJ.LengthSquared();
            float sum = ri + u.Radius[j];
            float reach = sum + stepLength;
            if (d2 >= reach * reach) continue;
            float dist = MathF.Sqrt(d2);
            Vector2 normal = dist > 0f ? toJ / dist : -CoincidentDirection(i, j);
            float gap = dist - sum;
            float into = Vector2.Dot(step, normal);
            if (into > gap) step -= normal * (into - gap);
        }
        return ClampLength(step, u.Speed[i]);
    }

    /// <summary>True if a unit that is a wall to unit <paramref name="i"/> (see <see cref="Constrain"/>) overlaps a unit of i's radius standing at <paramref name="aim"/>.</summary>
    private static bool AimCovered(UnitStore u, int i, Vector2 aim, int[] near, int count, int goalCell)
    {
        for (int m = 0; m < count; m++)
        {
            int j = near[m];
            if (j == i || !u.Alive[j]) continue;
            bool idle = u.State[j] != UnitState.Moving;
            if (idle ? u.GoalCell[j] == goalCell : u.Velocity[j] != Vector2.Zero) continue;
            float sum = u.Radius[i] + u.Radius[j];
            if (Vector2.DistanceSquared(aim, u.Position[j]) < sum * sum) return true;
        }
        return false;
    }

    private static bool CanStep(NavGrid grid, int cx, int cy, Vector2 next) =>
        grid.WorldToCell(next, out int nx, out int ny) && IsLegalStep(grid, cx, cy, nx, ny);

    /// <summary>Push direction for unit <paramref name="i"/> away from a unit at exactly its position: opposite for the two units of a pair, varied between pairs.</summary>
    private static Vector2 CoincidentDirection(int i, int j)
    {
        int lo = Math.Min(i, j), hi = Math.Max(i, j);
        int d = (lo + hi) & (FlowField.DirectionCount - 1);
        var dir = new Vector2(FlowField.OffsetX(d), FlowField.OffsetY(d));
        if ((d & 1) == 1) dir *= 0.70710677f; // diagonals: unit length
        return i == lo ? dir : -dir;
    }

    private static Vector2 ClampLength(Vector2 v, float max)
    {
        float l2 = v.LengthSquared();
        return l2 <= max * max ? v : v * (max / MathF.Sqrt(l2));
    }

    private static void Stop(UnitStore u, int i)
    {
        u.State[i] = UnitState.Idle;
        u.Velocity[i] = Vector2.Zero;
        u.StuckTicks[i] = 0;
        u.BestRemaining[i] = float.PositiveInfinity;
    }
}
