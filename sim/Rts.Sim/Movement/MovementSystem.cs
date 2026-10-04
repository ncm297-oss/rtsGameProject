using System;
using System.Numerics;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;

namespace Rts.Sim.Movement;

/// <summary>Tick phases 8-9: walks Moving units along their goal's flow field at their data speed (docs/03 "Local movement").</summary>
/// <remarks>
/// M1-4b only: no separation, shoving or arrival slots yet (M1-4c), so units heading to one point
/// stack on it. Units update in slot order.
/// </remarks>
public static class MovementSystem
{
    /// <summary>Advances every Moving unit by one tick.</summary>
    public static void Run(World world)
    {
        UnitStore u = world.Units;
        NavGrid grid = world.NavGrid;
        FlowFieldCache cache = world.FlowFields;
        const float arrival2 = MovementConstants.ArrivalDistance * MovementConstants.ArrivalDistance;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.State[i] != UnitState.Moving) continue;
            Vector2 pos = u.Position[i];
            Vector2 goal = u.Goal[i];
            if (Vector2.DistanceSquared(pos, goal) <= arrival2 || !grid.WorldToCell(pos, out int cx, out int cy))
            {
                // Arrived; or off the map, where no field can lead anywhere.
                Stop(u, i);
                continue;
            }

            Vector2 aim;
            int cell = cy * grid.Width + cx;
            if (cell == u.GoalCell[i])
            {
                aim = goal;
            }
            else
            {
                // Phase 8: a cache miss builds the field here, inside the tick, without allocating.
                FlowField field = cache.Get(u.GoalCell[i]);
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
                // Refused: never step into a blocked cell. Stays Moving; M1-4c adds give-up rules.
                u.Velocity[i] = Vector2.Zero;
                continue;
            }
            u.Position[i] = next;
            u.Velocity[i] = step;
            u.Facing[i] = SimMath.Atan2(step.Y, step.X);
        }
    }

    private static void Stop(UnitStore u, int i)
    {
        u.State[i] = UnitState.Idle;
        u.Velocity[i] = Vector2.Zero;
    }
}
