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
/// The sweep ends when every point is visited, or once a forest and a gold mine within <c>ai.gatherReach</c> of the hall
/// (M6-S5, BUG-1045: one farther, known through an ally or a raider, doesn't count) and an enemy building (seen or ghost)
/// are all known: the scout is released (stopped if still walking) and the economy sends it to gather. None is sent if all three are
/// known already. M6-S7 (BUG-1047 / 1048): the sweep's end no longer decides the gather reach; the head ring being seen does
/// (<see cref="HeadRingSeen"/>, <see cref="AiPlayer.ReachLifted"/>). So a scout that dies before the ring is seen is replaced
/// by the next think's free worker, whose sweep starts with the ring points not yet seen; one that dies after is not, unless
/// (M6-SH1, BUG-1053) the player knows no node of some resource kind at all (<see cref="KnowsEveryKind"/>). M6-SH1
/// (BUG-1052): a scout is sent again either way only while the player's own unit losses are at most <c>ai.scoutReplacements</c> (derived from the saved match stats,
/// so the blackboard keeps no count); past that the sweep ends and the reach lifts.
/// <para>
/// M6-S5 (BUG-1042), a player with an ally: a head waypoint within <c>ai.scoutRadius</c> of an ally's building is dropped
/// (the base there is the ally's: with teams the hall's mirror may be the ally's start), and past the head the next grid
/// point is the unvisited one farthest from the ally's buildings (the largest distance to the nearest of them; list order on
/// ties), so the sweep heads for the enemy's half first. With shared vision the ally's buildings are always known, and the
/// ground the ally explored is skipped like the player's own. A free-for-all sweeps as before.
/// </para>
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
            // Dead. M6-S7 (BUG-1048): a scout lost before its head ring was seen is replaced (phase 0: the next think sends
            // another worker, which skips the ring points already seen), since the gather reach lifts only once the ring is
            // seen. M6-SH1 (BUG-1053): one lost after is replaced only while a resource kind has no known node at all (else
            // the reach has lifted and the economy has one to go to). M6-SH1 (BUG-1052): either only while the own unit losses
            // are at most ai.scoutReplacements; past that the sweep ends (phase 2), so the reach lifts as a death did in M6-S5.
            bool again = ai.View.OwnUnitLosses <= ai.Rules.ScoutReplacements && (!ai.ReachLifted() || !KnowsEveryKind(ai));
            board.ScoutPhase = again ? (byte)0 : (byte)2;
            board.ScoutSlot = -1;
            return;
        }
        if (ai.OrderedNotApplied(slot, u.Handle.Generation)) return;
        Vector2[] points = board.ScoutPoints;
        bool[] visited = board.ScoutVisited;
        bool hasAlly = ai.View.HasAlly;
        if (hasAlly) DropAlliedHeadPoints(ai, points, visited);
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
        int next = Next(ai, board, u.Position, hasAlly);
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
    /// are visited. M6-S5 (BUG-1042): with <paramref name="hasAlly"/>, the unvisited grid point farthest from the ally's
    /// buildings instead (<see cref="AlliedDistance"/>; list order on ties).
    /// </summary>
    private static int Next(AiPlayer ai, Blackboard board, Vector2 at, bool hasAlly)
    {
        bool[] visited = board.ScoutVisited;
        for (int k = 0; k < Blackboard.ScoutHead; k++)
            if (!visited[k]) return k;
        if (board.ScoutTarget >= Blackboard.ScoutHead && !visited[board.ScoutTarget]) return board.ScoutTarget;
        int best = -1;
        float bestDistance = hasAlly ? -1f : float.MaxValue;
        for (int k = Blackboard.ScoutHead; k < visited.Length; k++)
        {
            if (visited[k]) continue;
            if (hasAlly)
            {
                float far = AlliedDistance(ai, board.ScoutPoints[k]);
                if (far <= bestDistance) continue;
                bestDistance = far;
                best = k;
                continue;
            }
            float d = Vector2.DistanceSquared(at, board.ScoutPoints[k]);
            if (d >= bestDistance) continue;
            bestDistance = d;
            best = k;
        }
        return best;
    }

    /// <summary>Whether <paramref name="owner"/> is an ally of the AI's player (not the player itself).</summary>
    private static bool IsAlly(AiPlayer ai, int owner) => owner != ai.Player && !ai.View.IsEnemy(owner);

    /// <summary>
    /// M6-S5 (BUG-1042): marks visited every unvisited head waypoint within <c>ai.scoutRadius</c> of a building of an ally
    /// the player sees (one pass over the buildings it sees): the base there is the ally's, nothing to find.
    /// </summary>
    private static void DropAlliedHeadPoints(AiPlayer ai, Vector2[] points, bool[] visited)
    {
        bool any = false;
        for (int k = 0; k < Blackboard.ScoutHead; k++) any |= !visited[k];
        if (!any) return;
        float r2 = ai.Rules.ScoutRadius * ai.Rules.ScoutRadius;
        int s = -1;
        while (ai.View.NextBuilding(ref s, out SeenBuilding b))
        {
            if (!IsAlly(ai, b.Owner)) continue;
            Vector2 centre = ai.BuildingCentre(b.TypeId, b.Cell);
            for (int k = 0; k < Blackboard.ScoutHead; k++)
                if (!visited[k] && Vector2.DistanceSquared(points[k], centre) <= r2) visited[k] = true;
        }
    }

    /// <summary>
    /// M6-S5: the squared distance (m^2) from <paramref name="p"/> to the nearest building of an ally the player sees;
    /// <see cref="float.MaxValue"/> when it sees none (every point ties, so list order decides).
    /// </summary>
    private static float AlliedDistance(AiPlayer ai, Vector2 p)
    {
        float nearest = float.MaxValue;
        int s = -1;
        while (ai.View.NextBuilding(ref s, out SeenBuilding b))
        {
            if (!IsAlly(ai, b.Owner)) continue;
            float d = Vector2.DistanceSquared(p, ai.BuildingCentre(b.TypeId, b.Cell));
            if (d < nearest) nearest = d;
        }
        return nearest;
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

    /// <summary>
    /// Whether the AI knows an enemy building and, M6-S5 (BUG-1045), a forest and a gold mine within <c>ai.gatherReach</c> of
    /// its hall's centre (a node farther, seen through an ally's eyes or a raider's, doesn't count: the scout still looks near
    /// home). False with no hall.
    /// </summary>
    internal static bool FoundEnough(AiPlayer ai)
    {
        if (ai.Board.KnownEnemyBuildings == 0 || !ai.TryHallPoint(out Vector2 hall)) return false;
        float reach = ai.Rules.GatherReach * ai.Rules.GatherReach;
        bool wood = false, gold = false;
        int s = -1;
        while (!(wood && gold) && ai.View.NextResource(ref s, out SeenResource node))
        {
            if (Vector2.DistanceSquared(ai.NodeCentre(node.TypeId, node.Cell), hall) > reach) continue;
            if (ai.Data.Resources[node.TypeId].Resource == ResourceKind.Wood) wood = true;
            else gold = true;
        }
        return wood && gold;
    }

    /// <summary>
    /// M6-SH1 (BUG-1053): whether the player knows a node of every resource kind, at any distance (one the economy dropped as
    /// unreachable doesn't count): with the reach lifted the economy will walk to any of them. One pass over the nodes it
    /// knows, ending at the first of each kind; allocates nothing.
    /// </summary>
    internal static bool KnowsEveryKind(AiPlayer ai)
    {
        bool wood = false, gold = false;
        int s = -1;
        while (!(wood && gold) && ai.View.NextResource(ref s, out SeenResource node))
        {
            if (ai.Board.NodeDropped[s] == node.Handle.Generation) continue;
            if (ai.Data.Resources[node.TypeId].Resource == ResourceKind.Wood) wood = true;
            else gold = true;
        }
        return wood && gold;
    }

    /// <summary>
    /// M6-S7 (BUG-1047 / 1048): whether every point of the head ring round the hall's current centre (the 8 compass points
    /// <c>ai.scoutRadius</c> off, clamped inside the map, as <see cref="Waypoints"/> lays them) has been seen: its cell explored
    /// by the player (the team's explored bits with shared vision), or the point marked visited on the scout's list (a point
    /// the scout could only get near, or one an ally's base covers). False with no hall. At most 8 explored-bit reads plus
    /// 8 x 8 point compares; allocates nothing. <see cref="AiPlayer.ReachLifted"/> asks it once a think.
    /// </summary>
    internal static bool HeadRingSeen(AiPlayer ai)
    {
        if (!ai.TryHallPoint(out Vector2 hall)) return false;
        Blackboard board = ai.Board;
        for (int k = 0; k < 8; k++)
        {
            Vector2 p = ai.ClampToMap(hall + s_compass[k] * ai.Rules.ScoutRadius);
            if (Explored(ai, p) || VisitedHeadPoint(board, p)) continue;
            return false;
        }
        return true;
    }

    /// <summary>Whether <paramref name="p"/> is one of the scout's head-ring points and marked visited.</summary>
    private static bool VisitedHeadPoint(Blackboard board, Vector2 p)
    {
        for (int k = 0; k < 8; k++)
            if (board.ScoutVisited[k] && board.ScoutPoints[k] == p) return true;
        return false;
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
        // M6-S7 (BUG-1048): a replacement (the points were laid before: a laid point is never (0, 0), it is clamped half a
        // cell inside the map) skips the ring points already seen; a first scout walks the whole ring, as before.
        bool replacement = points[0] != Vector2.Zero;
        for (int k = 0; k < 8; k++)
            points[k] = ai.ClampToMap(hall + s_compass[(first + k) % 8] * ai.Rules.ScoutRadius);
        points[8] = ai.ClampToMap(size - hall);
        int cols = GridLines(ai.View.MapWidth, ai.Rules.ScoutGrid), rows = GridLines(ai.View.MapHeight, ai.Rules.ScoutGrid);
        int n = Blackboard.ScoutHead;
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
                points[n++] = ai.ClampToMap(new Vector2((x + 0.5f) * size.X / cols, (y + 0.5f) * size.Y / rows));
        Array.Clear(board.ScoutVisited);
        if (!replacement) return;
        for (int k = 0; k < 8; k++)
            if (Explored(ai, points[k])) board.ScoutVisited[k] = true;
    }
}
