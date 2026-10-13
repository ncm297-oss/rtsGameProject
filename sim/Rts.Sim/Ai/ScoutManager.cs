using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.Ai;

/// <summary>
/// The AI's early scout (M5-3, docs/03 "Implementation (M5-3)"). At the first think at or after <c>ai.scoutAfter</c> the
/// worker nearest the hall with no AI command pending (an idle one first, else a gatherer) becomes the scout and sweeps a
/// fixed list of waypoints computed once: the 8 compass points <c>ai.scoutRadius</c> from the hall's centre (clamped inside
/// the map; the first is the one most toward the map centre, then clockwise), the hall's mirror through the map centre (where
/// a generated map puts the other base), then the 4 quadrant centres. One Move a think at most, to the next unvisited point,
/// only once its last command applied. A point is visited when the scout comes within <c>ai.scoutReach</c> of it, or the scout
/// is Idle after the Move to it applied (it got as close as it could). The sweep ends when every point is visited, or once a
/// forest, a gold mine and an enemy building (seen or ghost) are all known: the scout is released (stopped if still walking)
/// and the economy sends it to gather. None is sent if all three are known already. A dead scout is not replaced.
/// </summary>
internal static class ScoutManager
{
    // Compass unit vectors, clockwise from east (y grows south on the map): E, SE, S, SW, W, NW, N, NE.
    private const float Diagonal = 0.70710678f;

    private static readonly Vector2[] s_compass =
    {
        new(1, 0), new(Diagonal, Diagonal), new(0, 1), new(-Diagonal, Diagonal),
        new(-1, 0), new(-Diagonal, -Diagonal), new(0, -1), new(Diagonal, -Diagonal),
    };

    /// <summary>Sends, judges or releases the scout (see the type's summary).</summary>
    internal static void Run(AiPlayer ai)
    {
        Blackboard board = ai.Board;
        if (board.ScoutPhase == 2) return;
        if (board.ScoutPhase == 0)
        {
            if (ai.Now < ai.Rules.ScoutAfterTicks || !ai.TryHallPoint(out Vector2 hall)) return;
            if (FoundEnough(ai))
            {
                board.ScoutPhase = 2; // nothing left to look for
                return;
            }
            int slot = ai.PickBuilder(hall, out int generation);
            if (slot < 0) return; // no free worker yet: the next think tries again
            Waypoints(ai, hall);
            board.ScoutSlot = slot;
            board.ScoutGeneration = generation;
            board.ScoutTarget = -1;
            board.ScoutPhase = 1;
        }
        Sweep(ai);
    }

    private static void Sweep(AiPlayer ai)
    {
        Blackboard board = ai.Board;
        int slot = board.ScoutSlot;
        if (!ai.View.TryGetUnit(slot, out SeenUnit u) || u.Owner != ai.Player || u.Handle.Generation != board.ScoutGeneration)
        {
            board.ScoutPhase = 2; // dead: not replaced (periodic re-scouting is M5-4)
            board.ScoutSlot = -1;
            return;
        }
        if (ai.OrderedNotApplied(slot, u.Handle.Generation)) return;
        float reach = ai.Rules.ScoutReach * ai.Rules.ScoutReach;
        for (int k = 0; k < Blackboard.ScoutWaypoints; k++)
            if (Vector2.DistanceSquared(u.Position, board.ScoutPoints[k]) <= reach) board.ScoutVisited[k] = true;
        // Idle after the Move applied: there, or as close as it could get (a waypoint on a cliff or in the water).
        if (board.ScoutTarget >= 0 && u.State == UnitState.Idle) board.ScoutVisited[board.ScoutTarget] = true;
        int next = -1;
        for (int k = 0; k < Blackboard.ScoutWaypoints && next < 0; k++)
            if (!board.ScoutVisited[k]) next = k;
        if (next < 0 || FoundEnough(ai))
        {
            Release(ai, u);
            return;
        }
        if (next == board.ScoutTarget) return; // still on its way
        if (!ai.Send(Command.Move(ai.Player, u.Handle, board.ScoutPoints[next]))) return;
        ai.MarkOrdered(slot, u.Handle.Generation, 0);
        board.ScoutTarget = next;
    }

    /// <summary>Ends the sweep: a scout still walking to a waypoint is stopped, so it is Idle for the economy's next Gather.</summary>
    private static void Release(AiPlayer ai, in SeenUnit u)
    {
        Blackboard board = ai.Board;
        if (board.ScoutTarget >= 0 && u.State == UnitState.Moving) // walking to a waypoint (a gatherer never sent keeps gathering)
        {
            if (!ai.Send(Command.Stop(ai.Player, u.Handle))) return; // queue full: release next think
            ai.MarkOrdered(u.Handle.Index, u.Handle.Generation, 0);
        }
        board.ScoutPhase = 2;
        board.ScoutSlot = -1;
    }

    /// <summary>Whether the AI knows a forest, a gold mine and an enemy building.</summary>
    private static bool FoundEnough(AiPlayer ai)
    {
        if (ai.Board.KnownEnemyBuildings == 0) return false;
        bool wood = false, gold = false;
        int s = -1;
        while (!(wood && gold) && ai.View.NextResource(ref s, out SeenResource node))
        {
            if (ai.Data.Resources[node.TypeId].Resource == ResourceKind.Wood) wood = true;
            else gold = true;
        }
        return wood && gold;
    }

    /// <summary>The waypoint list (see the type's summary), computed once from the hall's centre and the map.</summary>
    private static void Waypoints(AiPlayer ai, Vector2 hall)
    {
        Blackboard board = ai.Board;
        Vector2 size = ai.MapSize, centre = size * 0.5f, toCentre = centre - hall;
        int first = 0;
        float best = float.MinValue;
        for (int k = 0; k < 8; k++)
        {
            float dot = Vector2.Dot(s_compass[k], toCentre);
            if (dot <= best) continue;
            best = dot;
            first = k;
        }
        for (int k = 0; k < 8; k++)
            board.ScoutPoints[k] = ai.ClampToMap(hall + s_compass[(first + k) % 8] * ai.Rules.ScoutRadius);
        board.ScoutPoints[8] = ai.ClampToMap(size - hall);
        board.ScoutPoints[9] = new Vector2(size.X * 0.25f, size.Y * 0.25f);
        board.ScoutPoints[10] = new Vector2(size.X * 0.75f, size.Y * 0.25f);
        board.ScoutPoints[11] = new Vector2(size.X * 0.75f, size.Y * 0.75f);
        board.ScoutPoints[12] = new Vector2(size.X * 0.25f, size.Y * 0.75f);
        System.Array.Clear(board.ScoutVisited);
    }
}
