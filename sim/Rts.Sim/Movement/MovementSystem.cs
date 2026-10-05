using System;
using System.Numerics;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;

namespace Rts.Sim.Movement;

/// <summary>Tick phases 8-9: walks Moving units along their goal's flow field at their data speed (docs/03 "Local movement").</summary>
/// <remarks>
/// No separation, shoving or arrival slots yet (M1-4d), so units heading to one point stack on it.
/// Units update grouped by goal cell, then slot, so each goal's field is fetched once per tick. A
/// build pass first serves goals without a cached field, oldest order first, at most
/// <see cref="MovementConstants.MaxFieldBuildsPerTick"/> per tick; units whose field is still
/// missing wait. Which goals get built (and so which units wait) depends on order ticks, goal cells
/// and the hashed cache state, never on slot order alone.
/// </remarks>
public static class MovementSystem
{
    /// <summary>Advances every Moving unit by one tick.</summary>
    public static void Run(World world)
    {
        UnitStore u = world.Units;
        NavGrid grid = world.NavGrid;
        FlowFieldCache cache = world.FlowFields;
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
            Vector2 pos = u.Position[i];
            Vector2 goal = u.Goal[i];
            if (!grid.WorldToCell(pos, out int cx, out int cy))
            {
                // Off the map, where no field can lead anywhere.
                Stop(u, i);
                continue;
            }

            Vector2 aim;
            int cell = cy * grid.Width + cx;
            if (cell == goalCell)
            {
                // Arrival counts only inside the goal cell: a goal under 1 m away across a blocked
                // corner can be far by path (BUG-0020).
                if (Vector2.DistanceSquared(pos, goal) <= arrival2)
                {
                    Stop(u, i);
                    continue;
                }
                aim = goal;
            }
            else
            {
                if (!fetched)
                {
                    fetched = true;
                    field = cache.TryGetCached(goalCell);
                }
                if (field == null)
                {
                    // Over this tick's build cap: wait, still Moving, for a later tick's build.
                    u.Velocity[i] = Vector2.Zero;
                    continue;
                }
                byte d = field.DirectionAt(cell);
                if (d == FlowField.NoDirection)
                {
                    // Standing in a blocked cell (e.g. spawned on a cliff) or one the goal can't reach.
                    Stop(u, i);
                    continue;
                }
                // The next cell's center: a straight line there stays inside this cell, the next
                // one and (for a diagonal) the two side cells, which no-corner-cutting keeps passable.
                aim = grid.CellCenter(cx + FlowField.OffsetX(d), cy + FlowField.OffsetY(d));
            }

            Vector2 delta = aim - pos;
            float dist = delta.Length();
            if (dist <= 0f)
            {
                u.Velocity[i] = Vector2.Zero;
                continue;
            }
            Vector2 step = delta * (MathF.Min(u.Speed[i], dist) / dist);
            Vector2 next = pos + step;
            if (!grid.WorldToCell(next, out int nx, out int ny) || !grid.IsPassable(nx, ny))
            {
                // Refused: never step into a blocked cell. Stays Moving; M1-4d adds give-up rules.
                u.Velocity[i] = Vector2.Zero;
                continue;
            }
            u.Position[i] = next;
            u.Velocity[i] = step;
            u.Facing[i] = SimMath.Atan2(step.Y, step.X);
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

    private static void Stop(UnitStore u, int i)
    {
        u.State[i] = UnitState.Idle;
        u.Velocity[i] = Vector2.Zero;
    }
}
