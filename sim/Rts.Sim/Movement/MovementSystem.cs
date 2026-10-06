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
/// (flow or direct aim, detour, sidestep, overlap push, arrival), and only then are the plans
/// applied. So separation is symmetric and no unit's result depends on which unit was walked first.
/// Walkers also plan shoves on friendly Idle units in their way (and the line ahead of them, M1-4d-3);
/// those move last, after every walker (M1-4d-2). A unit a shove cuts off its blob walks back once.
/// Not yet: formation offsets, <c>Stop</c>/<c>Hold</c>.
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
    private const byte ActQueued = 7;  // no progress, but a walker just ahead made some last tick: take PlannedStep; the stuck count holds (at least 1)

    // Meters a shove stops short of ShoveSpacing (SqueezeLimit): more than a position's float rounding.
    private const float ShoveRoundingMargin = 1e-4f;

    // Meters a step may go past a hard wall's limit: the float noise a clip leaves (Constrain).
    private const float WallTolerance = 1e-5f;

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
            bool onField = false; // aiming along the field (a cell center), not at the goal itself
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
                        onField = true;
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
            float aimDistance = 0f;
            if (noAim == ActWalk)
            {
                Vector2 delta = aim - pos;
                aimDistance = delta.Length();
                if (aimDistance > 0f)
                {
                    forward = delta / aimDistance;
                    flowStep = forward * MathF.Min(speed, aimDistance);
                }
            }
            // Detour only on the way: near the goal (aiming at it) a walker presses on into its blob;
            // steering round the units packed there cost arrivals (docs/03). Walls in the way are
            // collected in the neighbor loop below (DetourInterval), the turn worked out after it.
            // Nor inside a 1-cell passage: turning past a small unit there wedged the walker against the
            // next, wider one at a slant (BUG-0042); it presses on and pushes the line along instead.
            // (In a passage or not is worked out only when needed: -1 not yet, then 0 or 1.)
            int passage = -1;
            bool detour = onField && forward != Vector2.Zero;
            float detourReach = aimDistance + MovementConstants.AvoidRange;
            int detourWalls = 0;
            bool detourCovers = false;

            float ri = u.Radius[i];
            int owner = u.Owner[i];
            // Summed in fixed point (FixedAdd): integer addition is associative, so the sums don't
            // depend on the slot order neighbors are visited in (spawn order, M1-4d-3).
            long pushX = 0, pushY = 0, sideX = 0, sideY = 0;
            float maxShare = 0f;
            bool touchingArrived = false, crowded = false, queued = false, queuedOnWait = false;
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
                // Same goal cell *and* same player: an enemy holding this goal cell is a wall, never a
                // groupmate (BUG-0037).
                bool sameGroup = u.GoalCell[j] == goalCell && u.Owner[j] == owner;
                // An Idle unit of this group that kept the goal cell arrived there: the group's blob.
                bool groupmate = idle && sameGroup;
                // Other units that didn't move last tick (idle, waiting for a field, refused) are
                // walls, handled by Constrain below (friendly Idle ones yield to a shove); walkers
                // get the soft push.
                bool standing = idle || u.Velocity[j] == Vector2.Zero;
                // A wall within reach of this step's line is in the detour's way; its own group's
                // units mid-move (just ordered, queued, refused) walk on, so they are followed instead.
                if (detour && standing && !groupmate && !(sameGroup && !idle)
                    && DetourInterval(world, ri + u.Radius[j], -away, d2, forward, detourReach, detourWalls, j))
                {
                    if (world.DetourLo[detourWalls] <= 0f && world.DetourHi[detourWalls] >= 0f) detourCovers = true;
                    detourWalls++;
                }
                // SidestepAndQueue, written out here: Debug builds (where the tick budget is measured)
                // don't inline calls, and this loop runs for every neighbor of every walker. Keep the two
                // in step.
                if (!queued && !idle && (d2 < sum * sum || Vector2.Dot(away, forward) < 0f))
                {
                    float reach = sum + MovementConstants.QueueRange;
                    if (d2 < reach * reach)
                    {
                        if (!standing) queued = u.StuckTicks[j] == 0;
                        else if (!queuedOnWait) queuedOnWait = IsWaitingForField(world, u, j);
                    }
                }
                if (forward != Vector2.Zero && !groupmate)
                {
                    float sideReach = sum + MovementConstants.AvoidRange;
                    if (d2 < sideReach * sideReach && Vector2.Dot(away, forward) < 0f && (standing || Vector2.Dot(u.Velocity[j], forward) < 0f))
                    {
                        float w = MathF.Min(1f, (sideReach - MathF.Sqrt(d2)) / MovementConstants.AvoidRange);
                        var right = new Vector2(forward.Y, -forward.X);
                        float cross = forward.X * -away.Y + forward.Y * away.X;
                        Vector2 sideDir = cross >= 0f ? right : -right;
                        if (standing && passage < 0) passage = InPassage(grid, cx, cy) ? 1 : 0;
                        if (!(standing && passage == 1) || (grid.WorldToCell(u.Position[j] + sideDir * sum, out int px, out int py) && grid.IsPassable(px, py)))
                        {
                            Vector2 sv = sideDir * (w * speed);
                            sideX += (long)(sv.X * FixedScale);
                            sideY += (long)(sv.Y * FixedScale);
                        }
                    }
                }
                // Past the pack limit (closer than ShoveSpacing x the radii's sum) a unit is pushed out
                // whatever the other is doing: two units of a group pressed together and both standing
                // took each other for walls, which never push out of an overlap, and stopped there.
                if (d2 < sum * sum && (!standing || groupmate || d2 < MovementConstants.ShoveSpacing * MovementConstants.ShoveSpacing * sum * sum))
                {
                    float dist = MathF.Sqrt(d2);
                    Vector2 dir = dist > 0f ? away / dist : CoincidentDirection(i, j);
                    float share = (sum - dist) * (groupmate ? MovementConstants.SeparationShareStill : MovementConstants.SeparationShareMoving);
                    Vector2 pv = dir * share;
                    pushX += (long)(pv.X * FixedScale);
                    pushY += (long)(pv.Y * FixedScale);
                    if (share > maxShare) maxShare = share;
                    // Only through a legal step, so a blob never reaches across a blocked corner (BUG-0020).
                    if (groupmate && !touchingArrived && grid.WorldToCell(u.Position[j], out int jx, out int jy)
                        && IsLegalStep(grid, cx, cy, jx, jy))
                        touchingArrived = true;
                }
            }
            if (detourCovers && passage < 0) passage = InPassage(grid, cx, cy) ? 1 : 0;
            if (detourCovers && passage == 0)
            {
                Vector2 turned = DetourTurn(world, grid, u, i, pos, cx, cy, forward, detourWalls);
                if (turned != forward)
                {
                    // The walk follows the detour, and so do the sidestep and queuing: worked out again
                    // along the new direction (only on the ticks a detour turns, which are few).
                    forward = turned;
                    flowStep = forward * MathF.Min(speed, aimDistance);
                    sideX = sideY = 0;
                    queued = queuedOnWait = false;
                    for (int m = 0; m < count; m++)
                    {
                        int j = near[m];
                        if (j == i || !u.Alive[j]) continue;
                        Vector2 away = pos - u.Position[j];
                        bool idle = u.State[j] != UnitState.Moving;
                        bool groupmate = idle && u.GoalCell[j] == goalCell && u.Owner[j] == owner;
                        bool standing = idle || u.Velocity[j] == Vector2.Zero;
                        SidestepAndQueue(world, u, j, away, away.LengthSquared(), ri + u.Radius[j], idle, standing, groupmate, false, forward, speed, ref sideX, ref sideY, ref queued, ref queuedOnWait);
                    }
                }
            }
            Vector2 push = FixedValue(pushX, pushY), side = FixedValue(sideX, sideY);
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
            byte act = progress ? ActWalk : queued ? ActQueued : ActStuck;
            // Behind a unit waiting for its field: a queued tick too, but only most of the time. Waiting
            // counts as neither progress nor stuck, so two units each waiting in turn and queuing behind
            // the other held each other forever under more live goals than cache slots (BUG-0048): one
            // tick in QueueOnWaitStride still counts, so a jam behind waiters ends.
            if (act == ActStuck && queuedOnWait && (world.TickNumber - u.OrderTick[i]) % MovementConstants.QueueOnWaitStride != 0)
                act = ActQueued;
            bool pushArrived = false;
            // A unit walking back (its walk-back used, Moving again) never pushes a unit off its point:
            // the pair a walker pushed through a corridor walked home and shoved the walker back off its
            // goal (BUG-0042). It waits behind, or gives up, instead.
            if (!progress && u.StuckTicks[i] >= MovementConstants.PushAfterStuckTicks && u.WalkBack[i] != UnitStore.WalkBackUsed)
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
            remaining[i] = act == ActStuck || act == ActQueued ? best : after;
        }
    }

    /// <summary>
    /// For walker heading <paramref name="forward"/>, neighbor <paramref name="j"/> (at
    /// <paramref name="away"/> from it, squared distance <paramref name="d2"/>, radii sum
    /// <paramref name="sum"/>): its sidestep share, added to (sideX, sideY), and whether it makes a
    /// no-progress tick a queued one. The same rules are written out inline in Plan's neighbor loop
    /// (the hot path); this copy serves the re-run after a detour turns.
    /// </summary>
    private static void SidestepAndQueue(World world, UnitStore u, int j, Vector2 away, float d2, float sum, bool idle, bool standing, bool groupmate, bool inPassage,
        Vector2 forward, float speed, ref long sideX, ref long sideY, ref bool queued, ref bool queuedOnWait)
    {
        // (Idle neighbors never queue anyone: test that first, it is the cheap part.)
        if (!queued && !idle && (Vector2.Dot(away, forward) < 0f || d2 < sum * sum))
        {
            float reach = sum + MovementConstants.QueueRange;
            if (d2 < reach * reach)
            {
                // A walker ahead (or touching) within reach that made progress last tick (its count
                // reset, and only progress resets it while Moving): the traffic is still moving, so
                // this unit waits its turn, it isn't blocked. M1-5 read groupmates only; M1-4d-3 any
                // walker, since groups crossing each other jammed for a second and gave up. A unit
                // ahead waiting for its field (Moving, standing, field not cached, outside its goal
                // cell) will move once the field is built: queuing too (BUG-0028).
                if (!standing) queued = u.StuckTicks[j] == 0;
                else if (!queuedOnWait) queuedOnWait = IsWaitingForField(world, u, j);
            }
        }
        if (forward == Vector2.Zero || groupmate) return;
        // Sidestep a unit ahead that is standing or coming at us, before touching it.
        // (One walking our way is left alone: followers don't fan out.)
        float side = sum + MovementConstants.AvoidRange;
        Vector2 toJ = -away;
        if (d2 < side * side && Vector2.Dot(toJ, forward) > 0f && (standing || Vector2.Dot(u.Velocity[j], forward) < 0f))
        {
            float w = MathF.Min(1f, (side - MathF.Sqrt(d2)) / MovementConstants.AvoidRange);
            var right = new Vector2(forward.Y, -forward.X);
            // Step away from the side it is on; dead ahead, step right. Both units of a head-on pair
            // step right, which are opposite ways, so they pass.
            float cross = forward.X * toJ.Y - forward.Y * toJ.X;
            Vector2 away2 = cross >= 0f ? right : -right;
            // Inside a 1-cell passage, round a standing unit only toward a side where the walker could
            // pass it (its center level with the unit on that side on passable ground): sidestepping
            // where there is no room wedged the walker against the corridor wall at a slant, and it
            // pushed parked units into the wall instead of along the corridor (BUG-0042). Elsewhere as
            // before: the same check in the open cost crowd arrivals (2,500 to 4 points, seed 6: 699 -> 547).
            NavGrid grid = world.NavGrid;
            bool checkRoom = standing && inPassage;
            if (!checkRoom || (grid.WorldToCell(u.Position[j] + away2 * sum, out int px, out int py) && grid.IsPassable(px, py)))
                FixedAdd(ref sideX, ref sideY, away2 * (w * speed));
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
                    else if (action[i] == ActQueued)
                    {
                        // Waiting in a flowing group: the count holds. Never at 0, which neighbors
                        // read as "made progress last tick"; so a holding unit can't hold up others,
                        // and every hold traces back to a real progress tick, of which each order
                        // has finitely many: a jammed group still gives up.
                        if (u.StuckTicks[i] == 0) u.StuckTicks[i] = 1;
                    }
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
    /// <para>
    /// Each wall allows a half-plane of steps (into it no further than the gap), and every half-plane
    /// holds the zero step. One clip per wall, in slot order, can carry the step back into a wall
    /// already passed: a unit between two standing units slid along the second straight into the
    /// first, and so worked its way through a slit far narrower than itself (BUG-0035). Hard walls
    /// (<see cref="IsHardWall"/>: other players' units holding their ground) are therefore checked
    /// again after the clips, and if one is still entered, the step becomes the one closest to the
    /// desired step that every hard wall allows (<see cref="ClosestAllowed"/>): a walker never goes
    /// deeper into an enemy, whatever else it touches. Its own army's standing units and units mid-move
    /// keep the single clip: a walker pressed between two of them may still slip through, which crowds
    /// of one army rely on (holding those to the exact rule too cut arrivals in the crowd tests, docs/03).
    /// </para>
    /// </remarks>
    private static Vector2 Constrain(World world, NavGrid grid, UnitStore u, int i, Vector2 pos, Vector2 step, int[] near, int count, int goalCell, bool pushArrived)
    {
        Vector2[] normals = world.WallNormals;
        float[] limits = world.WallLimits;
        int[] hardWalls = world.HardWalls;
        Vector2 desired = step;
        float stepLength = step.Length();
        int walls = 0, hard = 0;
        for (int m = 0; m < count; m++)
        {
            int j = near[m];
            if (!WallLimit(world, grid, u, i, pos, j, stepLength, goalCell, pushArrived, out Vector2 normal, out float limit)) continue;
            float into = Vector2.Dot(step, normal);
            if (into > limit) step -= normal * (into - limit);
            normals[walls] = normal;
            limits[walls] = limit;
            if (!IsHardWall(u, i, j))
            {
                walls++;
                continue;
            }
            hardWalls[hard++] = walls++;
            // Already overlapping another player's standing unit: only within 45 degrees of straight
            // away from it, so nobody slides round an enemy inside it (a corridor plug, BUG-0045).
            float sum = u.Radius[i] + u.Radius[j];
            if (Vector2.DistanceSquared(u.Position[j], pos) >= sum * sum || !IsPlug(world, i, pos, j, near, count)) continue;
            var across = new Vector2(normal.Y, -normal.X);
            for (int c = 0; c < 2; c++)
            {
                Vector2 cone = Vector2.Normalize((c == 0 ? across : -across) + normal);
                float coneInto = Vector2.Dot(step, cone);
                if (coneInto > 0f) step -= cone * coneInto;
                normals[walls] = cone;
                limits[walls] = 0f;
                hardWalls[hard++] = walls++;
            }
        }
        if (hard > 0 && !AllowedByHard(step, normals, limits, hardWalls, hard))
        {
            // The fallback holds every wall this step touches, the army's own standing units too, so a
            // friendly's clip isn't lost while the enemies' are restored (BUG-0038). Zero is always allowed.
            // A wall whose limit is at least twice the desired step can't bind any candidate the
            // search keeps (each is closer to the desired step than zero is): leave it out.
            float reachable = 2f * desired.Length();
            int kept = 0;
            for (int w = 0; w < walls; w++)
            {
                if (limits[w] >= reachable) continue;
                normals[kept] = normals[w];
                limits[kept++] = limits[w];
            }
            step = ClosestAllowed(desired, normals, limits, kept);
        }
        return ClampLength(step, u.Speed[i]);
    }

    /// <summary>True if <paramref name="step"/> goes along each of the first <paramref name="n"/> wall normals no further than its limit (within <see cref="WallTolerance"/>).</summary>
    private static bool Allowed(Vector2 step, Vector2[] normals, float[] limits, int n)
    {
        for (int k = 0; k < n; k++)
            if (Vector2.Dot(step, normals[k]) > limits[k] + WallTolerance) return false;
        return true;
    }

    /// <summary>True if <paramref name="step"/> goes no further than its limit along each hard wall's normal (indices <paramref name="hardWalls"/>[0..<paramref name="n"/>)).</summary>
    private static bool AllowedByHard(Vector2 step, Vector2[] normals, float[] limits, int[] hardWalls, int n)
    {
        for (int k = 0; k < n; k++)
        {
            int w = hardWalls[k];
            if (Vector2.Dot(step, normals[w]) > limits[w] + WallTolerance) return false;
        }
        return true;
    }

    /// <summary>
    /// The step closest to <paramref name="desired"/> that goes along no wall normal further than its
    /// limit: <paramref name="desired"/> itself, its projection onto one wall's line, or the corner of
    /// two walls' lines, whichever is allowed and closest. Zero is always allowed (no limit is negative).
    /// </summary>
    private static Vector2 ClosestAllowed(Vector2 desired, Vector2[] normals, float[] limits, int n)
    {
        if (Allowed(desired, normals, limits, n)) return desired;
        Vector2 best = Vector2.Zero;
        float bestD2 = desired.LengthSquared();
        for (int k = 0; k < n; k++)
        {
            float over = Vector2.Dot(desired, normals[k]) - limits[k];
            if (over <= 0f) continue;
            Vector2 c = desired - normals[k] * over;
            float d2 = Vector2.DistanceSquared(c, desired);
            if (d2 < bestD2 && Allowed(c, normals, limits, n)) { best = c; bestD2 = d2; }
        }
        for (int k = 0; k < n; k++)
        {
            for (int l = k + 1; l < n; l++)
            {
                Vector2 a = normals[k], b = normals[l];
                float det = a.X * b.Y - a.Y * b.X;
                if (MathF.Abs(det) < 1e-6f) continue; // parallel: no corner
                var c = new Vector2((limits[k] * b.Y - limits[l] * a.Y) / det, (a.X * limits[l] - b.X * limits[k]) / det);
                float d2 = Vector2.DistanceSquared(c, desired);
                if (d2 < bestD2 && Allowed(c, normals, limits, n)) { best = c; bestD2 = d2; }
            }
        }
        return best;
    }

    /// <summary>
    /// True if wall <paramref name="j"/> is a hard wall to walker <paramref name="i"/>: a unit of another
    /// player that isn't walking under an order (Idle now; holding or fighting later). See <see cref="Constrain"/>.
    /// </summary>
    private static bool IsHardWall(UnitStore u, int i, int j) => u.State[j] != UnitState.Moving && u.Owner[j] != u.Owner[i];

    /// <summary>
    /// For <see cref="Constrain"/>: false if neighbor <paramref name="j"/> is no wall to walker
    /// <paramref name="i"/> (a groupmate, a walker, or out of reach of a step of
    /// <paramref name="stepLength"/>); otherwise the unit normal toward j and how far (meters, never
    /// negative) a step may go along it: the gap to touching, plus what j yields to a shove.
    /// </summary>
    private static bool WallLimit(World world, NavGrid grid, UnitStore u, int i, Vector2 pos, int j, float stepLength, int goalCell, bool pushArrived,
        out Vector2 normal, out float limit)
    {
        normal = Vector2.Zero;
        limit = 0f;
        if (j == i || !u.Alive[j]) return false;
        // Not a wall (written out: hot path, Debug builds call every helper): a walker, or an arrived groupmate (same goal cell
        // and owner); those get the soft push instead.
        if (u.State[j] == UnitState.Moving ? u.Velocity[j] != Vector2.Zero : u.GoalCell[j] == goalCell && u.Owner[j] == u.Owner[i]) return false;
        Vector2 toJ = u.Position[j] - pos;
        float d2 = toJ.LengthSquared();
        float sum = u.Radius[i] + u.Radius[j];
        float reach = sum + stepLength + u.Speed[j];
        if (d2 >= reach * reach) return false;
        float dist = MathF.Sqrt(d2);
        normal = dist > 0f ? toJ / dist : -CoincidentDirection(i, j);
        float gap = dist - sum;
        Vector2 yield = ShoveDirection(world, u, i, j, goalCell, pushArrived, normal) * u.Speed[j];
        if (yield != Vector2.Zero && grid.WorldToCell(u.Position[j], out int jx, out int jy)
            && CanStep(grid, jx, jy, u.Position[j] + yield))
            gap += Vector2.Dot(yield, normal);
        limit = MathF.Max(gap, 0f);
        return true;
    }

    /// <summary>
    /// Which way walker <paramref name="i"/>, pushing along <paramref name="normal"/> (from i toward j),
    /// may shove unit <paramref name="j"/>: the push itself, or zero if j can't be shoved. Only Idle
    /// units of i's owner that aren't i's arrived groupmates and aren't holding position can be. One with no goal (never ordered,
    /// or gave up) always yields. One holding another goal yields too, but only as far as it stays
    /// touching the groupmates it touches (<see cref="KeepLinks"/>), and one standing on its point
    /// (within <see cref="MovementConstants.ArrivalDistance"/>) holds it, unless the walker is blocked
    /// (<paramref name="pushArrived"/>, see Plan) and the line it would push holds every groupmate
    /// of its members (<see cref="BuildChain"/>, <see cref="ChainHoldsItsGroups"/>): a lone parked unit,
    /// or a parked pair in a corridor, yields; a blob's point does not.
    /// </summary>
    private static Vector2 ShoveDirection(World world, UnitStore u, int i, int j, int goalCell, bool pushArrived, Vector2 normal)
    {
        // A unit holding position (M1-7) is never shoved: a plain wall, soft to friends, hard to enemies.
        if (u.State[j] != UnitState.Idle || u.Owner[j] != u.Owner[i] || u.GoalCell[j] == goalCell || u.Hold[j]) return Vector2.Zero;
        if (u.GoalCell[j] < 0) return normal;
        float toGoal2 = Vector2.DistanceSquared(u.Position[j], u.Goal[j]);
        if (toGoal2 > MovementConstants.ArrivalDistance * MovementConstants.ArrivalDistance) return normal; // tethered: KeepLinks
        // On its point: it holds it, unless the walker is blocked and the whole parked line yields
        // together. Pushing a blob's points away un-anchored whole blobs; a unit or a pair parked in a
        // corridor blocked its own army (BUG-0033).
        if (!pushArrived) return Vector2.Zero;
        int n = BuildChain(world, i, j, normal, goalCell, true);
        return ChainHoldsItsGroups(world, n) ? normal : Vector2.Zero;
    }

    /// <summary>
    /// The line a shove of <paramref name="j"/> along <paramref name="normal"/> moves (chain shove,
    /// M1-4d-3), into <c>World.ChainMembers</c>; returns its length (j first). Breadth first from j:
    /// Idle units of walker <paramref name="i"/>'s owner, not its groupmates nor holding position, touching a member and
    /// ahead of it along the push. Units holding a goal join only when
    /// <paramref name="withAnchored"/>. At most <see cref="MovementConstants.MaxChainShove"/> units.
    /// </summary>
    /// <remarks>Start-of-tick positions (the spatial hash), neighbors in ascending slot order: the same line every run.</remarks>
    private static int BuildChain(World world, int i, int j, Vector2 normal, int goalCell, bool withAnchored)
    {
        UnitStore u = world.Units;
        int[] chain = world.ChainMembers;
        int[] near = world.ShoveNeighbors;
        int owner = u.Owner[i];
        int n = 1;
        chain[0] = j;
        for (int head = 0; head < n; head++)
        {
            int m = chain[head];
            Vector2 pm = u.Position[m];
            int count = world.Spatial.QueryRadius(pm, u.Radius[m] + world.MaxUnitRadius, near);
            if (head > 0 && TouchesEnemy(u, m, near, count))
            {
                // Pressed against another player's unit: a chain never pushes a unit into an enemy
                // (crowds behind a plug would squeeze given-up units past it). Kept, marked, so it
                // isn't found again; dropped below.
                chain[head] = ~m;
                continue;
            }
            for (int q = 0; q < count && n < chain.Length; q++)
            {
                int k = near[q];
                if (k == i || !u.Alive[k] || u.State[k] != UnitState.Idle || u.Owner[k] != owner || u.GoalCell[k] == goalCell || u.Hold[k]) continue;
                if (u.GoalCell[k] >= 0 && !withAnchored) continue;
                if (InChain(chain, n, k)) continue;
                Vector2 d = u.Position[k] - pm;
                float sum = u.Radius[m] + u.Radius[k];
                if (d.LengthSquared() >= sum * sum || Vector2.Dot(d, normal) <= 0f) continue;
                chain[n++] = k;
            }
        }
        int kept = 0;
        for (int c = 0; c < n; c++)
            if (chain[c] >= 0) chain[kept++] = chain[c];
        return kept;
    }

    /// <summary>True if one of the <paramref name="count"/> units in <paramref name="near"/> belongs to another player than <paramref name="m"/>, stands still and touches it.</summary>
    private static bool TouchesEnemy(UnitStore u, int m, int[] near, int count)
    {
        for (int q = 0; q < count; q++)
        {
            int k = near[q];
            if (!u.Alive[k] || u.Owner[k] == u.Owner[m] || (u.State[k] == UnitState.Moving && u.Velocity[k] != Vector2.Zero)) continue;
            float sum = u.Radius[m] + u.Radius[k];
            if (Vector2.DistanceSquared(u.Position[m], u.Position[k]) < sum * sum) return true;
        }
        return false;
    }

    /// <summary>True if every Idle unit touching a member of the first <paramref name="n"/> of <c>World.ChainMembers</c> with that member's goal cell and owner is a member too: moving the line cuts no group apart.</summary>
    private static bool ChainHoldsItsGroups(World world, int n)
    {
        UnitStore u = world.Units;
        int[] chain = world.ChainMembers;
        int[] near = world.ShoveNeighbors;
        for (int c = 0; c < n; c++)
        {
            int m = chain[c];
            if (u.GoalCell[m] < 0) continue;
            Vector2 pm = u.Position[m];
            int count = world.Spatial.QueryRadius(pm, u.Radius[m] + world.MaxUnitRadius, near);
            for (int q = 0; q < count; q++)
            {
                int k = near[q];
                if (k == m || !u.Alive[k] || u.State[k] != UnitState.Idle || u.GoalCell[k] != u.GoalCell[m] || u.Owner[k] != u.Owner[m]) continue;
                float sum = u.Radius[m] + u.Radius[k];
                if (Vector2.DistanceSquared(pm, u.Position[k]) < sum * sum && !InChain(chain, n, k)) return false;
            }
        }
        return true;
    }

    private static bool InChain(int[] chain, int n, int k)
    {
        for (int c = 0; c < n; c++)
            if (chain[c] == k || chain[c] == ~k) return true;
        return false;
    }

    /// <summary>
    /// Adds to <paramref name="shove"/> what walker <paramref name="i"/>'s planned step asks of each
    /// shovable neighbor: the overlap it would leave past touching (along the line between their
    /// start-of-tick centers, so an existing overlap counts too), in that unit's shove direction.
    /// The same shove goes to the line of units ahead of it (<see cref="BuildChain"/>): goal-less ones
    /// always, units holding a goal only for a blocked walker and only if the line holds their
    /// groups whole, so a shoved unit isn't squeezed against the next one in a corridor.
    /// </summary>
    /// <remarks>Called in the sorted walk order, so each unit's shove is the same float sum every run.</remarks>
    private static void AddShoves(World world, UnitStore u, int i, Vector2 pos, Vector2 step, int[] near, int count, int goalCell, bool pushArrived, Vector2[] shove)
    {
        float ri = u.Radius[i];
        int[] chain = world.ChainMembers;
        for (int m = 0; m < count; m++)
        {
            int j = near[m];
            if (j == i || !u.Alive[j]) continue;
            Vector2 toJ = u.Position[j] - pos;
            float dist = toJ.Length();
            Vector2 normal = dist > 0f ? toJ / dist : -CoincidentDirection(i, j);
            float amount = Vector2.Dot(step, normal) - (dist - ri - u.Radius[j]);
            if (amount <= 0f) continue;
            Vector2 s = ShoveDirection(world, u, i, j, goalCell, pushArrived, normal) * amount;
            if (s == Vector2.Zero) continue;
            shove[j] += s;
            // A line ahead moves with it only behind a goal-less unit, or for a walker blocked long
            // enough to push parked units (each needs a neighbor query, so not for every shove).
            if (!pushArrived && u.GoalCell[j] >= 0) continue;
            int n = BuildChain(world, i, j, normal, goalCell, pushArrived);
            if (pushArrived && !ChainHoldsItsGroups(world, n)) n = BuildChain(world, i, j, normal, goalCell, false);
            for (int c = 1; c < n; c++) shove[chain[c]] += s;
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
            if (shove[i] == Vector2.Zero)
            {
                // Un-anchored by a shove and left alone long enough that nobody is pushing it any
                // more: walk back to its point (once per order).
                if (u.WalkBack[i] >= UnitStore.WalkBackPending && u.Alive[i]
                    && ++u.WalkBack[i] - UnitStore.WalkBackPending >= MovementConstants.WalkBackDelayTicks)
                    StartWalkBack(world, i);
                continue;
            }
            if (u.WalkBack[i] > UnitStore.WalkBackPending) u.WalkBack[i] = UnitStore.WalkBackPending; // shoved again: wait anew
            shoved[count++] = i;
            Vector2 s = Vector2.Zero;
            if (u.Alive[i] && u.State[i] == UnitState.Idle && grid.WorldToCell(u.Position[i], out int cx, out int cy))
            {
                s = SqueezeLimit(world, i, ClampLength(shove[i], u.Speed[i]), out bool enemyNear);
                s = KeepOffWalls(grid, cx, cy, u.Position[i], u.Radius[i], KeepLinks(world, i, s));
                // The wall trim can undo SqueezeLimit's trim against an enemy: check the final step.
                if (!CanStep(grid, cx, cy, u.Position[i] + s) || (enemyNear && ShovedIntoEnemy(world, i, s))) s = Vector2.Zero;
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
    /// A unit the anchor re-check cut off its blob, left unshoved for <see cref="MovementConstants.WalkBackDelayTicks"/>, walks back:
    /// Moving again toward its stored <see cref="UnitStore.Goal"/> and that goal's cell, with a fresh
    /// stuck count and progress estimate, and its order tick kept (the order's age is unchanged). Its
    /// one walk-back on this order is then used (M1-4d-3).
    /// </summary>
    private static void StartWalkBack(World world, int i)
    {
        UnitStore u = world.Units;
        NavGrid grid = world.NavGrid;
        u.WalkBack[i] = UnitStore.WalkBackUsed;
        if (u.State[i] != UnitState.Idle || u.GoalCell[i] >= 0 || u.Hold[i] || !grid.WorldToCell(u.Goal[i], out int x, out int y)) return;
        u.State[i] = UnitState.Moving;
        u.GoalCell[i] = y * grid.Width + x;
        u.StuckTicks[i] = 0;
        u.BestRemaining[i] = float.PositiveInfinity;
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
            if (k == i || !u.Alive[k] || u.State[k] != UnitState.Idle || u.GoalCell[k] != u.GoalCell[i] || u.Owner[k] != u.Owner[i]) continue;
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
    private static Vector2 SqueezeLimit(World world, int i, Vector2 step, out bool enemyNear)
    {
        UnitStore u = world.Units;
        enemyNear = false;
        int[] near = world.Neighbors;
        Vector2 pos = u.Position[i];
        // The hash holds start-of-tick positions; no unit has moved more than MaxUnitSpeed since.
        int count = world.Spatial.QueryRadius(pos, u.Radius[i] + world.MaxUnitRadius + 2f * world.MaxUnitSpeed, near);
        for (int m = 0; m < count; m++)
        {
            int k = near[m];
            if (k == i || !u.Alive[k] || (u.State[k] == UnitState.Moving && u.Velocity[k] != Vector2.Zero)) continue;
            if (u.Owner[k] != u.Owner[i]) enemyNear = true; // a standing enemy in reach: ShovedIntoEnemy checks the final step
            Vector2 toK = u.Position[k] - pos;
            float dist = toK.Length();
            Vector2 normal = dist > 0f ? toK / dist : -CoincidentDirection(i, k);
            // A hair short of the spacing: a shove trimmed to land exactly on it can land a float's
            // rounding (about 1e-5 m this far from the origin) inside it.
            float room = MathF.Max(dist - MovementConstants.ShoveSpacing * (u.Radius[i] + u.Radius[k]) - ShoveRoundingMargin, 0f);
            if (world.ShoveStep[k] != Vector2.Zero) room *= 0.5f;
            float into = Vector2.Dot(step, normal);
            if (into > room) step -= normal * (into - room);
        }
        return ClampLength(step, u.Speed[i]);
    }

    /// <summary>
    /// True if shove <paramref name="step"/> would take unit <paramref name="i"/> past the pack limit
    /// (<see cref="MovementConstants.ShoveSpacing"/> x the radii's sum) into another player's standing
    /// unit, or, already overlapping one, move it more than 45 degrees off straight away from it (sliding
    /// round an enemy inside it carried units through corridor plugs, BUG-0045).
    /// </summary>
    private static bool ShovedIntoEnemy(World world, int i, Vector2 step)
    {
        if (step == Vector2.Zero) return false;
        UnitStore u = world.Units;
        int[] near = world.Neighbors;
        Vector2 pos = u.Position[i], next = pos + step;
        float stepLength = step.Length();
        int count = world.Spatial.QueryRadius(pos, u.Radius[i] + world.MaxUnitRadius + 2f * world.MaxUnitSpeed, near);
        for (int m = 0; m < count; m++)
        {
            int k = near[m];
            if (k == i || !u.Alive[k] || u.Owner[k] == u.Owner[i] || (u.State[k] == UnitState.Moving && u.Velocity[k] != Vector2.Zero)) continue;
            float sum = u.Radius[i] + u.Radius[k];
            Vector2 pk = u.Position[k];
            float d1 = Vector2.Distance(next, pk);
            if (d1 >= sum || !IsPlug(world, i, pos, k, near, count)) continue;
            float d0 = Vector2.Distance(pos, pk);
            if (d1 < d0 && d1 < MovementConstants.ShoveSpacing * sum - ShoveRoundingMargin) return true;
            if (d0 < sum && d0 > 0f && Vector2.Dot(step, (pos - pk) / d0) < ConeCos * stepLength) return true;
        }
        return false;
    }

    /// <summary>
    /// True if standing enemy <paramref name="k"/>, which unit <paramref name="i"/> overlaps, is part of a
    /// plug: a line of other players' standing units, each too close to the next for i to pass between
    /// (gap under i's diameter), that reaches blocked ground on two opposite sides (each wall gap under
    /// i's diameter too) within <see cref="MovementConstants.MaxPlugSpan"/> units, or k alone in a 1-cell
    /// passage. An enemy blob pressed against one cliff is no plug: its walls are all on one side. Then i may only
    /// move straight-ish away from k (BUG-0045: units squeezed through the 0.1-0.2 m slits of enemy plugs
    /// in corridors 1-3 cells wide). In the open, sliding round enemy units is how two-player crowds flow,
    /// and holding that too cost crowd arrivals, so it is left alone.
    /// </summary>
    /// <remarks>Breadth first from k over start-of-tick positions, neighbors in ascending slot order: the same answer every run.</remarks>
    private static bool IsPlug(World world, int i, Vector2 pos, int k, int[] near, int count)
    {
        NavGrid grid = world.NavGrid;
        UnitStore u = world.Units;
        if (grid.WorldToCell(u.Position[k], out int kx, out int ky) && InPassage(grid, kx, ky)) return true;
        int[] line = world.PlugMembers;
        int[] found = world.ShoveNeighbors;
        float pass = 2f * u.Radius[i];
        int n = 1, walls = 0;
        line[0] = k;
        for (int head = 0; head < n; head++)
        {
            int m = line[head];
            walls |= WallSides(grid, u.Position[m], u.Radius[m], pass);
            // Blocked ground on opposite sides (left and right, or above and below): the line spans the passage.
            if ((walls & 3) == 3 || (walls & 12) == 12) return true;
            int c = world.Spatial.QueryRadius(u.Position[m], u.Radius[m] + world.MaxUnitRadius + pass, found);
            for (int q = 0; q < c && n < line.Length; q++)
            {
                int e = found[q];
                if (e == i || !u.Alive[e] || u.Owner[e] == u.Owner[i] || (u.State[e] == UnitState.Moving && u.Velocity[e] != Vector2.Zero)) continue;
                if (Array.IndexOf(line, e, 0, n) >= 0) continue;
                float gap = Vector2.Distance(u.Position[m], u.Position[e]) - u.Radius[m] - u.Radius[e];
                if (gap < pass) line[n++] = e;
            }
        }
        return false;
    }

    /// <summary>
    /// Which sides of a unit at <paramref name="p"/> (radius <paramref name="r"/>) are blocked ground
    /// closer than <paramref name="pass"/> beyond its disk: bit 1 +x, 2 -x, 4 +y, 8 -y (its cell's blocked
    /// 4-neighbors, measured from the cell edge).
    /// </summary>
    private static int WallSides(NavGrid grid, Vector2 p, float r, float pass)
    {
        if (!grid.WorldToCell(p, out int x, out int y)) return 0;
        const float cell = MapConstants.CellSize;
        int sides = 0;
        if (!grid.IsPassable(x + 1, y) && (x + 1) * cell - (p.X + r) < pass) sides |= 1;
        if (!grid.IsPassable(x - 1, y) && (p.X - r) - x * cell < pass) sides |= 2;
        if (!grid.IsPassable(x, y + 1) && (y + 1) * cell - (p.Y + r) < pass) sides |= 4;
        if (!grid.IsPassable(x, y - 1) && (p.Y - r) - y * cell < pass) sides |= 8;
        return sides;
    }

    /// <summary>True if cell (x, y) is walled on two opposite sides: a 1-cell passage.</summary>
    private static bool InPassage(NavGrid grid, int x, int y) =>
        (!grid.IsPassable(x + 1, y) && !grid.IsPassable(x - 1, y)) || (!grid.IsPassable(x, y + 1) && !grid.IsPassable(x, y - 1));

    // cos 45 degrees: the cone of directions a unit overlapping an enemy may still move in.
    private const float ConeCos = 0.70710677f;

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
                if (linked[j] || !u.Alive[j] || u.State[j] != UnitState.Idle || u.GoalCell[j] != goalCell || u.Owner[j] != u.Owner[i]) continue;
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
        {
            if (!u.Alive[i] || u.State[i] != UnitState.Idle || u.GoalCell[i] != goalCell || linked[i]) continue;
            u.GoalCell[i] = -1;
            // Cut off its blob by a shove, not by giving up: it may walk back once (StartWalkBack).
            if (u.WalkBack[i] == UnitStore.WalkBackNone) u.WalkBack[i] = UnitStore.WalkBackPending;
        }
        for (int k = 0; k < tail; k++) linked[queue[k]] = false;
    }

    /// <summary>True if a unit that is a wall to unit <paramref name="i"/> (see <see cref="Constrain"/>) overlaps a unit of i's radius standing at <paramref name="aim"/>.</summary>
    private static bool AimCovered(UnitStore u, int i, Vector2 aim, int[] near, int count, int goalCell)
    {
        for (int m = 0; m < count; m++)
        {
            int j = near[m];
            if (j == i || !u.Alive[j]) continue;
            if (u.State[j] == UnitState.Moving ? u.Velocity[j] != Vector2.Zero : u.GoalCell[j] == goalCell && u.Owner[j] == u.Owner[i]) continue; // not a wall: a walker or an arrived groupmate (as in WallLimit)
            float sum = u.Radius[i] + u.Radius[j];
            if (Vector2.DistanceSquared(aim, u.Position[j]) < sum * sum) return true;
        }
        return false;
    }

    /// <summary>
    /// True if Moving unit <paramref name="j"/> will wait for its field this tick: it stands outside
    /// its goal cell and the field isn't cached after this tick's builds (<see cref="FlowFieldCache.Contains"/>
    /// changes no LRU state). Read only for a unit that didn't move last tick.
    /// </summary>
    private static bool IsWaitingForField(World world, UnitStore u, int j)
    {
        if (world.FlowFields.Contains(u.GoalCell[j])) return false;
        NavGrid grid = world.NavGrid;
        return grid.WorldToCell(u.Position[j], out int x, out int y) && y * grid.Width + x != u.GoalCell[j];
    }

    /// <summary>
    /// Detour, part 1 (M1-4d-3, BUG-0028): stores at index <paramref name="k"/> of the world's detour
    /// scratch the directions in which a walker's disk would run into wall <paramref name="j"/> (offset
    /// <paramref name="toJ"/>, squared distance <paramref name="d2"/>, radii sum <paramref name="sum"/>),
    /// as an angle interval relative to <paramref name="forward"/>; false if the wall is coincident or
    /// more than <paramref name="reach"/> beyond touching (not in the way of this step's line). One
    /// already touching blocks the whole half-plane toward it.
    /// </summary>
    private static bool DetourInterval(World world, float sum, Vector2 toJ, float d2, Vector2 forward, float reach, int k, int j)
    {
        float dist = MathF.Sqrt(d2);
        if (dist <= 0f || dist - sum >= reach) return false;
        float center = SimMath.Atan2(forward.X * toJ.Y - forward.Y * toJ.X, Vector2.Dot(toJ, forward));
        float sine = MathF.Min(sum / dist, 1f);
        float half = SimMath.Atan2(sine, SimMath.Sqrt(1f - sine * sine)) + MovementConstants.DetourMargin;
        world.DetourLo[k] = center - half;
        world.DetourHi[k] = center + half;
        world.DetourWall[k] = j;
        return true;
    }

    /// <summary>
    /// Detour, part 2: the walking direction round the walls in the way. The intervals that chain to
    /// the straight line (direction 0) form one block; its two edges are the tangents past the block's
    /// sides, and the smaller turn is the shorter way round; on a tie, to the right. A side that turns
    /// more than <see cref="MovementConstants.MaxDetourTurn"/>, or has no ground to pass on
    /// (<see cref="OpenAlong"/>), isn't taken; then the other side is tried; else
    /// <paramref name="forward"/> unchanged.
    /// </summary>
    /// <remarks>
    /// The block is a union of intervals and its edges a min and a max, so the result doesn't depend on
    /// the order the walls were collected in (start-of-tick state only): reversed spawns walk bit-equal.
    /// </remarks>
    private static Vector2 DetourTurn(World world, NavGrid grid, UnitStore u, int i, Vector2 pos, int cx, int cy, Vector2 forward, int walls)
    {
        float[] los = world.DetourLo, his = world.DetourHi;
        float lo = 0f, hi = 0f;
        int loWall = -1, hiWall = -1;
        // Grow the block from the straight line until no wall's interval touches it any more.
        for (int round = 0; round <= walls; round++)
        {
            bool grew = false;
            for (int w = 0; w < walls; w++)
            {
                float a = los[w], b = his[w];
                if (a > hi || b < lo) continue;
                if (a < lo) { lo = a; loWall = w; grew = true; }
                if (b > hi) { hi = b; hiWall = w; grew = true; }
            }
            if (!grew) break;
        }
        float ri = u.Radius[i];
        // Negative angles are to the walker's right (see the sidestep in Plan).
        bool rightFirst = -lo <= hi;
        var rightOf = new Vector2(forward.Y, -forward.X);
        for (int side = 0; side < 2; side++)
        {
            bool right = rightFirst == (side == 0);
            float turn = right ? lo : hi;
            int edge = right ? loWall : hiWall;
            if (edge < 0 || MathF.Abs(turn) > MovementConstants.MaxDetourTurn) continue;
            float c = SimMath.Cos(turn), s = SimMath.Sin(turn);
            var dir = new Vector2(forward.X * c - forward.Y * s, forward.X * s + forward.Y * c);
            int j = world.DetourWall[edge];
            float sum = ri + u.Radius[j];
            float dist = Vector2.Distance(u.Position[j], pos);
            // Where the walker passes the edge wall: along the tangent, and level with it on this side.
            float passAt = MathF.Max(SimMath.Sqrt(MathF.Max(dist * dist - sum * sum, 0f)), ri);
            Vector2 abreast = u.Position[j] + (right ? rightOf : -rightOf) * sum;
            if (OpenAlong(grid, cx, cy, pos, dir, ri, passAt, abreast)) return dir;
        }
        return forward;
    }

    /// <summary>
    /// True if a detour along <paramref name="dir"/> has ground to walk on, by the walkers' own rule
    /// (centers on passable cells): the first <paramref name="first"/> meters a legal step from cell
    /// (cx, cy), the point where the tangent passes the edge wall (<paramref name="pass"/> meters) and
    /// the point <paramref name="abreast"/> of it on passable cells. A detour into a cliff, or along a
    /// corridor too narrow to pass the wall in, isn't taken.
    /// </summary>
    private static bool OpenAlong(NavGrid grid, int cx, int cy, Vector2 pos, Vector2 dir, float first, float pass, Vector2 abreast)
    {
        if (!CanStep(grid, cx, cy, pos + dir * MathF.Min(first, pass))) return false;
        return Passable(grid, pos + dir * pass) && Passable(grid, abreast);
    }

    private static bool Passable(NavGrid grid, Vector2 p) => grid.WorldToCell(p, out int x, out int y) && grid.IsPassable(x, y);

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

    // Fixed-point scale for order-independent sums: 2^32 units per meter. A power of two, so scaling a
    // float is exact and the cast only drops what lies below 2^-32 m; a sum of thousands of
    // meter-sized terms still fits a long.
    private const float FixedScale = 4294967296f;
    private const float FixedUnit = 1f / FixedScale;

    /// <summary>Adds <paramref name="v"/> to a fixed-point sum (see <see cref="FixedScale"/>): the total is the same whatever order terms are added in.</summary>
    private static void FixedAdd(ref long x, ref long y, Vector2 v)
    {
        x += (long)(v.X * FixedScale);
        y += (long)(v.Y * FixedScale);
    }

    private static Vector2 FixedValue(long x, long y) => new(x * FixedUnit, y * FixedUnit);

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
