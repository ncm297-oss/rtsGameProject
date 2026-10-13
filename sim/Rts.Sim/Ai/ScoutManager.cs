using System;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;

namespace Rts.Sim.Ai;

/// <summary>
/// The AI's early scout (M5-3, docs/03 "Implementation (M5-3)"). At the first think at or after <c>ai.scoutAfter</c> the
/// worker nearest the hall with no AI command pending (an idle one first, else a gatherer) becomes the scout and sweeps a
/// waypoint list computed once. Its head, in order: the 8 compass points <c>ai.scoutRadius</c> from the hall's centre (clamped
/// inside the map; the first is the one most toward the map centre, then clockwise), then the hall's mirror through the map
/// centre (docs/02 "Map": layouts are symmetric, so the other base is there or near it). Then the map grid (BUG-0593): the
/// centres of the map cut into equal cells at most <c>ai.scoutGrid</c> wide, each taken nearest the scout first, so a base
/// that isn't at the mirror is still found. One Move a think at most, to the next point, only once its last command applied.
/// A point is visited when the scout comes within <c>ai.scoutReach</c> of it, or the scout is Idle after the Move to it
/// applied (it got as close as it could); a grid point also once the player has explored its cell (nothing new to see there),
/// unless it is the one the scout walks to.
/// The sweep ends when every point is visited, or once a forest, a gold mine and an enemy building (seen or ghost) are all
/// known: the scout is released (stopped if still walking) and the economy sends it to gather. None is sent if all three are
/// known already. A dead scout is not replaced.
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

    /// <summary>How many grid lines (columns or rows) a map side of <paramref name="cells"/> cells is cut into: cells at most <paramref name="grid"/> m wide.</summary>
    internal static int GridLines(int cells, float grid) =>
        grid > 0f ? Math.Max(1, (int)MathF.Ceiling(cells * MapConstants.CellSize / grid)) : 1;

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
        Vector2[] points = board.ScoutPoints;
        bool[] visited = board.ScoutVisited;
        float reach = ai.Rules.ScoutReach * ai.Rules.ScoutReach;
        for (int k = 0; k < points.Length; k++)
        {
            if (visited[k]) continue;
            // A grid point already explored is skipped, but not the one it walks to: explored from 14 m off, it would turn
            // away before seeing past it.
            if (Vector2.DistanceSquared(u.Position, points[k]) <= reach || k >= Blackboard.ScoutHead && k != board.ScoutTarget && Explored(ai, points[k]))
                visited[k] = true;
        }
        // Idle after the Move applied: there, or as close as it could get (a waypoint on a cliff or in the water).
        if (board.ScoutTarget >= 0 && u.State == UnitState.Idle) visited[board.ScoutTarget] = true;
        int next = Next(board, u.Position);
        if (next < 0 || FoundEnough(ai))
        {
            Release(ai, u);
            return;
        }
        if (next == board.ScoutTarget) return; // still on its way
        if (!ai.Send(Command.Move(ai.Player, u.Handle, points[next]))) return;
        ai.MarkOrdered(slot, u.Handle.Generation, 0);
        board.ScoutTarget = next;
    }

    /// <summary>
    /// The point to walk to: the first unvisited one of the head; past the head, the one it walks to while unvisited (so it
    /// isn't turned each think), else the unvisited grid point nearest <paramref name="at"/> (list order on ties); -1 when all
    /// are visited.
    /// </summary>
    private static int Next(Blackboard board, Vector2 at)
    {
        bool[] visited = board.ScoutVisited;
        for (int k = 0; k < Blackboard.ScoutHead; k++)
            if (!visited[k]) return k;
        if (board.ScoutTarget >= Blackboard.ScoutHead && !visited[board.ScoutTarget]) return board.ScoutTarget;
        int best = -1;
        float bestDistance = float.MaxValue;
        for (int k = Blackboard.ScoutHead; k < visited.Length; k++)
        {
            if (visited[k]) continue;
            float d = Vector2.DistanceSquared(at, board.ScoutPoints[k]);
            if (d >= bestDistance) continue;
            bestDistance = d;
            best = k;
        }
        return best;
    }

    /// <summary>Whether the player has explored the cell under <paramref name="p"/> (a point inside the map).</summary>
    private static bool Explored(AiPlayer ai, Vector2 p) =>
        ai.View.IsExplored((int)(p.Y / MapConstants.CellSize) * ai.View.MapWidth + (int)(p.X / MapConstants.CellSize));

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
        Vector2[] points = board.ScoutPoints;
        for (int k = 0; k < 8; k++)
            points[k] = ai.ClampToMap(hall + s_compass[(first + k) % 8] * ai.Rules.ScoutRadius);
        points[8] = ai.ClampToMap(size - hall);
        int cols = GridLines(ai.View.MapWidth, ai.Rules.ScoutGrid), rows = GridLines(ai.View.MapHeight, ai.Rules.ScoutGrid);
        int n = Blackboard.ScoutHead;
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
                points[n++] = ai.ClampToMap(new Vector2((x + 0.5f) * size.X / cols, (y + 0.5f) * size.Y / rows));
        Array.Clear(board.ScoutVisited);
    }
}
