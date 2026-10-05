using System;
using System.Numerics;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;

namespace Rts.Sim.Movement;

/// <summary>Tick phases 8-9: walks Moving units along their goal's flow field at their data speed, with separation, crowded arrival, giving up and shoving (docs/03 "Local movement").</summary>
/// <remarks>
/// Units are grouped by goal cell, then slot, so each goal's field is fetched once per tick. A
/// build pass first serves goals without a cached field, oldest order first, at most
/// <see cref="MovementConstants.MaxFieldBuildsPerTick"/> per tick; units whose field is still
/// missing wait. Which goals get built (and so which units wait) depends on order ticks, goal cells
/// and the hashed cache state, never on slot order alone.
/// <para>
/// Then two passes: every Moving unit plans its step from start-of-tick positions and states
/// (flow or direct aim, sidestep, overlap push, arrival), and only then are the plans applied. So
/// separation is symmetric and no unit's result depends on which unit was walked first. Walkers
/// also plan shoves on friendly Idle units in their way; those move last, after every walker
/// (M1-4d-2). Not yet: formation offsets, <c>Stop</c>/<c>Hold</c>.
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
    private const byte ActPush = 6;    // blocked, moving only by pushing parked units: take PlannedStep; the stuck count holds

    /// <summary>Advances every Moving unit by one tick, then moves the Idle units they shoved.</summary>
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
        ApplyShoves(world);
    }

    /// <summary>Plans every Moving unit's outcome and step, and the shoves it gives, from start-of-tick state; writes only the world's scratch arrays (and LRU touches).</summary>
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
        Vector2[] shove = world.ShoveStep;
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
                // walls, handled by Constrain below (friendly Idle ones yield to a shove); walkers
                // get the soft push.
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
                Vector2 back = Constrain(world, grid, u, i, pos, ClampLength(push, speed), near, count, goalCell, false);
                action[i] = ActBackOff;
                planned[i] = CanStep(grid, cx, cy, pos + back) ? back : Vector2.Zero;
                AddShoves(world, u, i, pos, planned[i], near, count, goalCell, false, shove);
                continue;
            }
            if (noAim != ActWalk)
            {
                action[i] = noAim;
                continue;
            }

            Vector2 desired = ClampLength(flowStep + ClampLength(side, speed) + push, speed);
            Vector2 step = WalkStep(world, grid, u, i, pos, cx, cy, desired, flowStep, forward, near, count, goalCell, false);
            // Progress: the estimated path meters left beat the best so far (or where the unit
            // stands now) by a margin. The estimate is this cell's field cost, less the way already
            // made from the cell's center toward the aim; it runs on nearly continuously across
            // cell edges. A best that only ever falls means jostling back and forth (even across a
            // cell edge, where the aim changes) never counts as progress.
            float cellCost = inGoalCell ? 0f : field!.CostAt(cell) * MapConstants.CellSize;
            float aimRemaining = cellCost - Vector2.Distance(aim, grid.CellCenter(cx, cy));
            float best = MathF.Min(u.BestRemaining[i], aimRemaining + Vector2.Distance(aim, pos));
            float margin = MovementConstants.StuckFraction * speed;
            float after = aimRemaining + Vector2.Distance(aim, pos + step);
            bool progress = after < best - margin;
            byte act = progress ? ActWalk : ActStuck;
            bool pushArrived = false;
            if (!progress && u.StuckTicks[i] >= MovementConstants.PushAfterStuckTicks)
            {
                // Blocked for a while: friendly units parked at other goals yield too, if that moves
                // it forward (BUG-0033: a unit parked in a corridor blocked its own army). Not before,
                // so walkers that can get round a blob don't plough through it.
                Vector2 pushing = WalkStep(world, grid, u, i, pos, cx, cy, desired, flowStep, forward, near, count, goalCell, true);
                float afterPushing = aimRemaining + Vector2.Distance(aim, pos + pushing);
                if (pushing != step && afterPushing < aimRemaining + Vector2.Distance(aim, pos) - margin)
                {
                    step = pushing;
                    after = MathF.Min(best, afterPushing);
                    act = ActPush;
                    pushArrived = true;
                }
            }
            planned[i] = step;
            AddShoves(world, u, i, pos, step, near, count, goalCell, pushArrived, shove);
            action[i] = act;
            remaining[i] = act == ActStuck ? best : after;
        }
    }

    /// <summary>
    /// A walker's step: <paramref name="desired"/> constrained by standing units; if that ends in a
    /// blocked cell, off the map or across a blocked corner, a slide along the wall (one axis of the
    /// step, the one gaining more along <paramref name="forward"/>), else the flow step alone, else
    /// nothing. <paramref name="pushArrived"/>: see <see cref="ShoveDirection"/>.
    /// </summary>
    private static Vector2 WalkStep(World world, NavGrid grid, UnitStore u, int i, Vector2 pos, int cx, int cy, Vector2 desired, Vector2 flowStep,
        Vector2 forward, int[] near, int count, int goalCell, bool pushArrived)
    {
        Vector2 step = Constrain(world, grid, u, i, pos, desired, near, count, goalCell, pushArrived);
        if (CanStep(grid, cx, cy, pos + step)) return step;
        // Never step into a blocked cell or across a blocked corner; sliding keeps neighbors from
        // pinning a unit against a wall.
        Vector2 alongX = Constrain(world, grid, u, i, pos, new Vector2(step.X, 0f), near, count, goalCell, pushArrived);
        Vector2 alongY = Constrain(world, grid, u, i, pos, new Vector2(0f, step.Y), near, count, goalCell, pushArrived);
        bool okX = CanStep(grid, cx, cy, pos + alongX), okY = CanStep(grid, cx, cy, pos + alongY);
        if (okX && (!okY || Vector2.Dot(alongX, forward) >= Vector2.Dot(alongY, forward))) return alongX;
        if (okY) return alongY;
        step = Constrain(world, grid, u, i, pos, flowStep, near, count, goalCell, pushArrived);
        return CanStep(grid, cx, cy, pos + step) ? step : Vector2.Zero;
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
                    else if (action[i] == ActPush) { } // moving, but only by pushing: the count holds
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
    /// part of <paramref name="step"/> that would go deeper into one than touching. Collide-and-slide:
    /// a walker glances round them, and one pressed dead against them stops. A friendly Idle unit that
    /// can be shoved (<see cref="ShoveDirection"/>) by a legal step yields as far as its own speed
    /// carries it along the push, so the walker may press that far into it (see <see cref="AddShoves"/>).
    /// </summary>
    /// <remarks>
    /// An existing overlap is never pushed out here, only not deepened, so a unit is never pushed
    /// further than its own step (BUG-0031: a push-out of the whole overlap pointed into a cliff and
    /// pinned the unit whatever its order). Standing units read at their start-of-tick positions;
    /// neighbors are visited in ascending slot order.
    /// </remarks>
    private static Vector2 Constrain(World world, NavGrid grid, UnitStore u, int i, Vector2 pos, Vector2 step, int[] near, int count, int goalCell, bool pushArrived)
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
            float reach = sum + stepLength + u.Speed[j];
            if (d2 >= reach * reach) continue;
            float dist = MathF.Sqrt(d2);
            Vector2 normal = dist > 0f ? toJ / dist : -CoincidentDirection(i, j);
            float gap = dist - sum;
            Vector2 yield = ShoveDirection(world, u, i, j, goalCell, pushArrived, normal) * u.Speed[j];
            if (yield != Vector2.Zero && grid.WorldToCell(u.Position[j], out int jx, out int jy)
                && CanStep(grid, jx, jy, u.Position[j] + yield))
                gap += Vector2.Dot(yield, normal);
            float into = Vector2.Dot(step, normal);
            float limit = MathF.Max(gap, 0f);
            if (into > limit) step -= normal * (into - limit);
        }
        return ClampLength(step, u.Speed[i]);
    }

    /// <summary>
    /// Which way walker <paramref name="i"/>, pushing along <paramref name="normal"/> (from i toward j),
    /// may shove unit <paramref name="j"/>: the push itself, or zero if j can't be shoved. Only Idle
    /// units of i's owner that aren't i's arrived groupmates can be. One with no goal (never ordered,
    /// or gave up) always yields. One holding another goal yields too, but only as far as it stays
    /// touching the groupmates it touches (<see cref="KeepLinks"/>), and one standing on its point
    /// (within <see cref="MovementConstants.ArrivalDistance"/>) holds it, unless it stands there alone
    /// and the walker is blocked (<paramref name="pushArrived"/>, see Plan).
    /// </summary>
    private static Vector2 ShoveDirection(World world, UnitStore u, int i, int j, int goalCell, bool pushArrived, Vector2 normal)
    {
        if (u.State[j] != UnitState.Idle || u.Owner[j] != u.Owner[i] || u.GoalCell[j] == goalCell) return Vector2.Zero;
        if (u.GoalCell[j] < 0) return normal;
        float toGoal2 = Vector2.DistanceSquared(u.Position[j], u.Goal[j]);
        if (toGoal2 > MovementConstants.ArrivalDistance * MovementConstants.ArrivalDistance) return normal; // tethered: KeepLinks
        // On its point: it holds it, unless it stands there alone and the walker is blocked. Pushing
        // a blob's points away un-anchored whole blobs; a lone one parked in a corridor blocked its
        // own army (BUG-0033).
        return pushArrived && IsLoneAnchor(world, j) ? normal : Vector2.Zero;
    }

    /// <summary>True if arrived unit <paramref name="j"/> touches no Idle unit holding its goal cell: it stands at its point alone.</summary>
    private static bool IsLoneAnchor(World world, int j)
    {
        UnitStore u = world.Units;
        int[] near = world.ShoveNeighbors;
        Vector2 pos = u.Position[j];
        int count = world.Spatial.QueryRadius(pos, u.Radius[j] + world.MaxUnitRadius, near);
        for (int m = 0; m < count; m++)
        {
            int k = near[m];
            if (k == j || !u.Alive[k] || u.State[k] != UnitState.Idle || u.GoalCell[k] != u.GoalCell[j]) continue;
            float sum = u.Radius[j] + u.Radius[k];
            if (Vector2.DistanceSquared(pos, u.Position[k]) < sum * sum) return false;
        }
        return true;
    }

    /// <summary>
    /// Adds to <paramref name="shove"/> what walker <paramref name="i"/>'s planned step asks of each
    /// shovable neighbor: the overlap it would leave past touching (along the line between their
    /// start-of-tick centers, so an existing overlap counts too), in that unit's shove direction.
    /// </summary>
    /// <remarks>Called in the sorted walk order, so each unit's shove is the same float sum every run.</remarks>
    private static void AddShoves(World world, UnitStore u, int i, Vector2 pos, Vector2 step, int[] near, int count, int goalCell, bool pushArrived, Vector2[] shove)
    {
        float ri = u.Radius[i];
        for (int m = 0; m < count; m++)
        {
            int j = near[m];
            if (j == i || !u.Alive[j]) continue;
            Vector2 toJ = u.Position[j] - pos;
            float dist = toJ.Length();
            Vector2 normal = dist > 0f ? toJ / dist : -CoincidentDirection(i, j);
            float amount = Vector2.Dot(step, normal) - (dist - ri - u.Radius[j]);
            if (amount <= 0f) continue;
            shove[j] += ShoveDirection(world, u, i, j, goalCell, pushArrived, normal) * amount;
        }
    }

    /// <summary>
    /// Moves every shoved unit (after the walkers): each takes its summed shove, clamped to its own
    /// speed and kept out of other standing units (<see cref="SqueezeLimit"/>), unless that ends in
    /// a blocked cell, off the map or across a blocked corner (then it stays). It stays Idle with no
    /// new order. Then every goal group that had a member moved re-checks its anchors
    /// (<see cref="RecheckAnchors"/>), so no unit keeps a goal cell away from its point.
    /// </summary>
    /// <remarks>
    /// Every shove step is worked out before any shoved unit moves, from positions after the walkers
    /// moved, and goal groups are re-checked once each in goal-cell order: no result depends on
    /// slot order.
    /// </remarks>
    private static void ApplyShoves(World world)
    {
        UnitStore u = world.Units;
        NavGrid grid = world.NavGrid;
        Vector2[] shove = world.ShoveStep;
        Vector2[] step = world.PlannedStep;
        int[] shoved = world.AnchorQueue;
        int count = 0;
        for (int i = 0; i < shove.Length; i++)
        {
            if (shove[i] == Vector2.Zero) continue;
            shoved[count++] = i;
            Vector2 s = Vector2.Zero;
            if (u.Alive[i] && u.State[i] == UnitState.Idle && grid.WorldToCell(u.Position[i], out int cx, out int cy))
            {
                s = SqueezeLimit(world, i, ClampLength(shove[i], u.Speed[i]));
                s = KeepOffWalls(grid, cx, cy, u.Position[i], u.Radius[i], KeepLinks(world, i, s));
                if (!CanStep(grid, cx, cy, u.Position[i] + s)) s = Vector2.Zero;
            }
            step[i] = s;
        }
        int[] goals = world.ShovedGoals;
        int goalCount = 0;
        for (int k = 0; k < count; k++)
        {
            int i = shoved[k];
            shove[i] = Vector2.Zero;
            if (step[i] == Vector2.Zero) continue;
            u.Position[i] += step[i];
            if (u.GoalCell[i] >= 0) goals[goalCount++] = u.GoalCell[i];
        }
        Array.Sort(goals, 0, goalCount);
        for (int k = 0; k < goalCount; k++)
            if (k == 0 || goals[k] != goals[k - 1]) RecheckAnchors(world, goals[k]);
    }

    /// <summary>
    /// Trims a shove so the unit's disk goes no deeper into a blocked side neighbor of its cell (x or
    /// y) than it already is: a shoved unit never ends up overlapping a cliff it was clear of.
    /// </summary>
    /// <remarks>Walkers' steps are only checked by center (<see cref="CanStep"/>); a shove is not the unit's own move, so it is held to more.</remarks>
    private static Vector2 KeepOffWalls(NavGrid grid, int cx, int cy, Vector2 pos, float radius, Vector2 step)
    {
        Vector2 next = pos + step;
        float left = cx * MapConstants.CellSize + radius, right = (cx + 1) * MapConstants.CellSize - radius;
        float top = cy * MapConstants.CellSize + radius, bottom = (cy + 1) * MapConstants.CellSize - radius;
        if (step.X < 0f && next.X < left && !grid.IsPassable(cx - 1, cy)) next.X = MathF.Min(pos.X, left);
        if (step.X > 0f && next.X > right && !grid.IsPassable(cx + 1, cy)) next.X = MathF.Max(pos.X, right);
        if (step.Y < 0f && next.Y < top && !grid.IsPassable(cx, cy - 1)) next.Y = MathF.Min(pos.Y, top);
        if (step.Y > 0f && next.Y > bottom && !grid.IsPassable(cx, cy + 1)) next.Y = MathF.Max(pos.Y, bottom);
        return next - pos;
    }

    /// <summary>
    /// Scales a shoved arrived unit's <paramref name="step"/> down so it ends still touching every Idle
    /// groupmate it touches now (half the slack toward one that is shoved too), so a shove doesn't cut
    /// a blob apart and un-anchor the units linked through it. Units with no goal are not held.
    /// </summary>
    /// <remarks>
    /// Scaling, not projecting, so every link holds at once. Rarely a link still breaks (both ends
    /// shoved, or a wall trim after this); the anchor re-check then drops the goal as usual.
    /// </remarks>
    private static Vector2 KeepLinks(World world, int i, Vector2 step)
    {
        UnitStore u = world.Units;
        if (u.GoalCell[i] < 0) return step;
        int[] near = world.Neighbors;
        Vector2 pos = u.Position[i];
        int count = world.Spatial.QueryRadius(pos, u.Radius[i] + world.MaxUnitRadius + world.MaxUnitSpeed, near);
        float scale = 1f;
        for (int m = 0; m < count; m++)
        {
            int k = near[m];
            if (k == i || !u.Alive[k] || u.State[k] != UnitState.Idle || u.GoalCell[k] != u.GoalCell[i]) continue;
            Vector2 toK = u.Position[k] - pos;
            float dist = toK.Length();
            float sum = u.Radius[i] + u.Radius[k];
            if (dist >= sum || dist <= 0f) continue;
            // Largest t in [0, 1] with |toK - t * step| <= reach: still touching k where it stood.
            float reach = sum - 1e-3f;
            if (world.ShoveStep[k] != Vector2.Zero) reach = dist + 0.5f * (reach - dist); // k may move too
            if (reach < dist) reach = dist; // already at the edge: just no further
            float ss = step.LengthSquared();
            if (ss <= 0f) break;
            float b = Vector2.Dot(toK, step);
            float t = (b + MathF.Sqrt(b * b - ss * (dist * dist - reach * reach))) / ss;
            if (t < scale) scale = MathF.Max(t, 0f);
        }
        return step * scale;
    }

    /// <summary>
    /// Trims shoved unit <paramref name="i"/>'s <paramref name="step"/> so it ends no closer than
    /// <see cref="MovementConstants.ShoveSpacing"/> x the radii's sum to any unit standing still
    /// (Idle or not walking this tick), and never goes deeper into one that is closer already. Toward
    /// a unit that is being shoved too, it takes only half the room, since that one may close the rest.
    /// </summary>
    /// <remarks>Without this, a walker pressed friendly units on top of each other.</remarks>
    private static Vector2 SqueezeLimit(World world, int i, Vector2 step)
    {
        UnitStore u = world.Units;
        int[] near = world.Neighbors;
        Vector2 pos = u.Position[i];
        // The hash holds start-of-tick positions; no unit has moved more than MaxUnitSpeed since.
        int count = world.Spatial.QueryRadius(pos, u.Radius[i] + world.MaxUnitRadius + 2f * world.MaxUnitSpeed, near);
        for (int m = 0; m < count; m++)
        {
            int k = near[m];
            if (k == i || !u.Alive[k] || (u.State[k] == UnitState.Moving && u.Velocity[k] != Vector2.Zero)) continue;
            Vector2 toK = u.Position[k] - pos;
            float dist = toK.Length();
            Vector2 normal = dist > 0f ? toK / dist : -CoincidentDirection(i, k);
            float room = MathF.Max(dist - MovementConstants.ShoveSpacing * (u.Radius[i] + u.Radius[k]), 0f);
            if (world.ShoveStep[k] != Vector2.Zero) room *= 0.5f;
            float into = Vector2.Dot(step, normal);
            if (into > room) step -= normal * (into - room);
        }
        return ClampLength(step, u.Speed[i]);
    }

    /// <summary>
    /// The arrival rule of Plan, re-applied to a whole goal group at end-of-tick positions: an Idle
    /// unit keeps <paramref name="goalCell"/> only if it is in that cell within
    /// <see cref="MovementConstants.ArrivalDistance"/> of its goal, or linked to such a unit by a chain
    /// of touching Idle groupmates (each link a legal step). The rest drop their goal (-1).
    /// </summary>
    /// <remarks>
    /// A shove can carry an anchor away from its point, and with it every unit that touched the blob
    /// only through it, so checking the shoved units alone would leave stray anchors. Which units are
    /// reachable is a set property, so the visiting order changes nothing.
    /// </remarks>
    private static void RecheckAnchors(World world, int goalCell)
    {
        UnitStore u = world.Units;
        NavGrid grid = world.NavGrid;
        bool[] linked = world.AnchorLinked;
        int[] queue = world.AnchorQueue;
        int[] near = world.Neighbors;
        const float arrival2 = MovementConstants.ArrivalDistance * MovementConstants.ArrivalDistance;
        int tail = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.State[i] != UnitState.Idle || u.GoalCell[i] != goalCell) continue;
            if (grid.WorldToCell(u.Position[i], out int cx, out int cy) && cy * grid.Width + cx == goalCell
                && Vector2.DistanceSquared(u.Position[i], u.Goal[i]) <= arrival2)
            {
                linked[i] = true;
                queue[tail++] = i;
            }
        }
        // The hash holds start-of-tick positions; no unit has moved more than MaxUnitSpeed since.
        float extra = world.MaxUnitRadius + world.MaxUnitSpeed;
        for (int head = 0; head < tail; head++)
        {
            int i = queue[head];
            Vector2 pos = u.Position[i];
            grid.WorldToCell(pos, out int cx, out int cy);
            int count = world.Spatial.QueryRadius(pos, u.Radius[i] + extra, near);
            for (int m = 0; m < count; m++)
            {
                int j = near[m];
                if (linked[j] || !u.Alive[j] || u.State[j] != UnitState.Idle || u.GoalCell[j] != goalCell) continue;
                float sum = u.Radius[i] + u.Radius[j];
                if (Vector2.DistanceSquared(pos, u.Position[j]) < sum * sum
                    && grid.WorldToCell(u.Position[j], out int jx, out int jy) && IsLegalStep(grid, cx, cy, jx, jy))
                {
                    linked[j] = true;
                    queue[tail++] = j;
                }
            }
        }
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.State[i] == UnitState.Idle && u.GoalCell[i] == goalCell && !linked[i]) u.GoalCell[i] = -1;
        for (int k = 0; k < tail; k++) linked[queue[k]] = false;
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
