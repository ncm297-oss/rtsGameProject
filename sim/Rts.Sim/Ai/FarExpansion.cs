using System;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;

namespace Rts.Sim.Ai;

/// <summary>
/// M5-9b (BUG-1068 (a); docs/03 "Implementation (M5-9b)"): the far expansion. A seat whose own gold is spent and that knows
/// no other goes and takes the map's unclaimed mines: it looks at the unused start locations, then puts its cheapest gold-free
/// drop-off by a free gold mine it has seen, wherever on the map, so its workers stop walking home with every load.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Arm</b> (the census, <see cref="AiPlayer"/>'s <c>UpdateFarArm</c>): gold short for production's pick
/// (<see cref="AiPlayer.ShortForPick"/>), no own worker at gold, no gold node it may gather known. Phase
/// <see cref="Armed"/>.</item>
/// <item><b>Armed</b>: once it knows a free gold node (<see cref="Candidate"/>) it orders the drop-off by it
/// (<see cref="ExpansionType"/>: the gold-free depot, the Town Hall only when it can pay for one now) by the usual site search
/// round the node, the leg's worker as the builder when it can; the Build sent, phase <see cref="Building"/>. Until then the
/// leg: one worker walks to the nearest free start location (no player starts there, or its player is defeated) it has not
/// looked at, and round it
/// (<see cref="LookPoints"/>), then the next. The arm clears (phase 0) when an own worker is at gold again and no candidate
/// is known: gold comes in without a far drop-off.</item>
/// <item><b>Building</b>: the Build judged at the first think after it applied (refused: its anchor marked, armed again); the
/// accepted site followed until it stands (phase 0: done, the economy's rebalance moves gatherers to the mine) or is gone
/// (phase 0: no rebuild; the seat knows the mine now, so it does not arm again for it).</item>
/// </list>
/// With <c>ai.farExpandReach</c> 0 nothing runs (the phase never leaves 0, no leg worker, no command). One expansion at a time.
/// Fog: the candidate is a node the view answers (one the player has seen); the start list is map data every seat is given
/// (<see cref="PlayerView.StartCount"/>). Allocates nothing.
/// </remarks>
internal static class FarExpansion
{
    /// <summary><see cref="Blackboard.FarPhase"/>: armed (looking, or waiting to order the drop-off).</summary>
    internal const byte Armed = 1;

    /// <summary><see cref="Blackboard.FarPhase"/>: the drop-off's Build sent or its site standing.</summary>
    internal const byte Building = 2;

    /// <summary>Start locations the leg can mark looked at (<see cref="Blackboard.FarLooked"/>'s bits); a map has at most a handful.</summary>
    internal const int MaxStarts = 31;

    /// <summary>The look points of a start: its centre, then the four compass points <see cref="LookRadius"/> off it.</summary>
    internal const int LookPoints = 5;

    /// <summary>
    /// How far round a start's centre the leg looks (m): half the map contract's start-mine distance
    /// (<see cref="MapGenParams.StartMineDistance"/>, 18 m: every start's mines lie within it, hand-made maps too), so a
    /// worker of 13 m sight or more walking the four points sees every footprint the contract allows. Geometry from the map
    /// contract, not a balance number.
    /// </summary>
    internal static readonly float LookRadius = MapGenParams.Default.StartMineDistance * 0.5f;

    private static readonly Vector2[] s_look = { Vector2.Zero, new(1, 0), new(0, 1), new(-1, 0), new(0, -1) };

    /// <summary>
    /// The faction's cheapest drop-off building that costs no gold (least wood, then the lowest id), -1 for none: what a
    /// broke seat can still build by a far mine (docs/02 "Buildings": the Camp is the drop-off for expansions). Load-time data.
    /// </summary>
    internal static int GoldFreeDropOff(GameData data, int faction)
    {
        int best = -1;
        foreach (BuildingDef b in data.Buildings)
        {
            if (b.Faction != faction || !b.DropOff || b.CostGold != 0) continue;
            if (best < 0 || b.CostWood < data.Buildings[best].CostWood) best = b.Id;
        }
        return best;
    }

    /// <summary>Runs the far expansion's phase (see the type's remarks).</summary>
    internal static void Run(AiPlayer ai)
    {
        Blackboard board = ai.Board;
        if (ai.Rules.FarExpandReach <= 0f || board.FarPhase == 0) return;
        if (board.FarPhase == Building)
        {
            Follow(ai);
            if (board.FarPhase != Armed) return; // a refused Build arms it again and is retried at once
        }
        if (!ai.HasHall || ai.WorkerCount == 0) return;
        int node = Candidate(ai, out int nodeType);
        if (node < 0)
        {
            if (ai.Gathering(EconomyManager.Gold))
            {
                Disarm(ai); // gold comes in again without a far drop-off
                return;
            }
            Leg(ai);
            return;
        }
        // A free mine is known: the leg is over; its worker, standing by the mine, is the builder if it can be. The start
        // and look point stay (BUG-1159): a later arm walks the rest of that start, not the whole of it again.
        int builder = board.FarScoutSlot, generation = board.FarScoutGeneration;
        EndLeg(ai, stop: false);
        if (ai.Now < board.FarNoSiteUntil) return;
        int type = ExpansionType(ai);
        if (type < 0) return;
        ResourceDef def = ai.Data.Resources[nodeType];
        ai.NoSiteFound = false;
        if (ai.TryBuild(type, ai.CentreCellOf(def.FootprintWidth, def.FootprintHeight, node), -1, ref board.FarPending, builder, generation))
            board.FarPhase = Building;
        else if (ai.NoSiteFound) board.FarNoSiteUntil = ai.Now + BuildOrderExecutor.NoSiteRetryThinks * ai.ThinkTicks;
    }

    /// <summary>The drop-off to build: the Town Hall when the seat can pay for one now or has no gold-free drop-off, else that (-1: neither).</summary>
    internal static int ExpansionType(AiPlayer ai)
    {
        if (ai.DepotType < 0) return ai.TownHallType;
        if (ai.TownHallType < 0) return ai.DepotType;
        BuildingDef hall = ai.Data.Buildings[ai.TownHallType];
        return ai.CanAfford(hall.CostGold, hall.CostWood) ? ai.TownHallType : ai.DepotType;
    }

    /// <summary>
    /// The anchor cell (and type, <paramref name="nodeType"/>) of the free gold node the far expansion builds by, -1 for none:
    /// of the gold nodes the player knows (the view's node memory: seen, so no fog read), one not marked dropped by its
    /// gatherers, not seen empty, within <c>ai.farExpandReach</c> of the hall's centre, with no own drop-off (finished or a
    /// site) within <c>ai.gatherReach</c> of it (it needs none), no known enemy drop-off (seen, or a ghost of a seat still in
    /// the match) within <c>ai.gatherReach</c> of it, and no own or seen hall within <c>ai.expandHallReach</c>
    /// (<see cref="BuildOrderExecutor.HallNear"/>, the expand step's rule); the nearest the hall (slot order on ties).
    /// </summary>
    internal static int Candidate(AiPlayer ai, out int nodeType)
    {
        nodeType = -1;
        Vector2 hall = ai.HallPoint;
        float reach = ai.Rules.FarExpandReach * ai.Rules.FarExpandReach, gather = ai.Rules.GatherReach * ai.Rules.GatherReach;
        float best = float.MaxValue;
        int found = -1, s = -1;
        while (ai.View.NextResource(ref s, out SeenResource node))
        {
            if (ai.Data.Resources[node.TypeId].Resource != ResourceKind.Gold || ai.Board.NodeDropped[s] == node.Handle.Generation || node.Remaining == 0) continue;
            Vector2 centre = ai.NodeCentre(node.TypeId, node.Cell);
            float d = Vector2.DistanceSquared(centre, hall);
            if (d > reach || d >= best) continue;
            if (OwnDropOffNear(ai, centre, gather) || EnemyDropOffNear(ai, centre, gather)
                || BuildOrderExecutor.HallNear(ai, node.Cell, ai.Rules.ExpandReachCells)) continue;
            best = d;
            found = node.Cell;
            nodeType = node.TypeId;
        }
        return found;
    }

    private static bool OwnDropOffNear(AiPlayer ai, Vector2 centre, float reach2)
    {
        for (int k = 0; k < ai.OwnBuildingCount; k++)
            if (ai.Data.Buildings[ai.OwnBuildingTypes[k]].DropOff
                && Vector2.DistanceSquared(ai.BuildingCentre(ai.OwnBuildingTypes[k], ai.OwnBuildingCells[k]), centre) <= reach2) return true;
        return false;
    }

    private static bool EnemyDropOffNear(AiPlayer ai, Vector2 centre, float reach2)
    {
        int s = -1;
        while (ai.View.NextBuilding(ref s, out SeenBuilding b))
            if (ai.View.IsEnemy(b.Owner) && ai.Data.Buildings[b.TypeId].DropOff
                && Vector2.DistanceSquared(ai.BuildingCentre(b.TypeId, b.Cell), centre) <= reach2) return true;
        s = -1;
        while (ai.View.NextGhost(ref s, out Vision.BuildingGhost g))
            if (ai.IsLiveEnemy(g.Owner) && ai.Data.Buildings[g.TypeId].DropOff
                && Vector2.DistanceSquared(ai.BuildingCentre(g.TypeId, g.Cell), centre) <= reach2) return true;
        return false;
    }

    /// <summary>
    /// <see cref="Building"/>: judges the drop-off's Build once it applied (refused: the anchor marked, <see cref="Armed"/>
    /// again), then follows the accepted site: finished, the expansion is done (phase 0); gone, it is given up (phase 0).
    /// </summary>
    private static void Follow(AiPlayer ai)
    {
        Blackboard board = ai.Board;
        ref PendingOrder pending = ref board.FarPending;
        if (pending.Kind != PendingKind.None)
        {
            if (ai.Now <= pending.AppliesAt) return; // not applied yet
            if (!ai.BuildAccepted(pending))
            {
                board.MarkBad(pending.TypeId, pending.Cell);
                pending = default;
                board.FarPhase = Armed;
                return;
            }
            board.FarSiteType = pending.TypeId;
            board.FarSiteCell = pending.Cell;
            board.FarExpansions++;
            pending = default;
        }
        for (int k = 0; k < ai.OwnBuildingCount; k++)
        {
            if (ai.OwnBuildingTypes[k] != board.FarSiteType || ai.OwnBuildingCells[k] != board.FarSiteCell) continue;
            if (ai.OwnBuildingSites[k]) return; // still a site: the economy's stalled-site rule keeps a builder on it
            break; // standing
        }
        board.FarSiteType = board.FarSiteCell = -1;
        board.FarPhase = 0;
    }

    /// <summary>Clears the arm: the leg ends (its worker stopped if walking) and the phase goes to 0.</summary>
    private static void Disarm(AiPlayer ai)
    {
        EndLeg(ai, stop: true);
        ai.Board.FarPhase = 0;
    }

    /// <summary>
    /// Ends the leg: no leg worker. With <paramref name="stop"/> (the arm cleared, or no start left) a worker walking to a look
    /// point is stopped, so the economy finds it idle, and no start is walked to. Without it (a candidate found mid-leg) the
    /// start and its look point are kept (BUG-1159): the leg's worker is the builder now, and a later arm (the mine spent)
    /// walks that start's remaining look points before the next start rather than its centre again.
    /// </summary>
    private static void EndLeg(AiPlayer ai, bool stop)
    {
        Blackboard board = ai.Board;
        int slot = board.FarScoutSlot;
        if (stop && slot >= 0 && ai.OwnUnit(slot, board.FarScoutGeneration, out SeenUnit u) && u.State == UnitState.Moving && !ai.OrderedNotApplied(slot, u.Handle.Generation) && ai.Send(Command.Stop(ai.Player, u.Handle)))
            ai.MarkOrdered(slot, u.Handle.Generation, 0);
        board.FarScoutSlot = -1;
        board.FarScoutGeneration = 0;
        board.FarMoved = false;
        if (!stop) return;
        board.FarStart = -1;
        board.FarLook = 0;
    }

    /// <summary>
    /// The leg: the start being looked at (else the nearest unused one not looked at, <see cref="NextStart"/>), a worker for
    /// it (the one walking, else <see cref="AiPlayer.PickBuilder"/>'s nearest the point), and one Move a think at most to the
    /// look point it is at; a point is looked at once the worker is within <c>ai.scoutReach</c> of it or stands still after the
    /// Move to it applied (as close as it could get). The start's last point looked at marks the start, and the leg goes on to
    /// the next; with none left it ends and the seat waits armed. M5-H6 (BUG-1158): a leg worker that died is replaced only
    /// while the player's own unit losses are at most <c>ai.scoutReplacements</c> (the scout's cap, BUG-1052); past that the
    /// leg is over for the match (every start marked looked). Why the losses and not a count of leg deaths: a count would be
    /// a new saved field (a save-format bump), and a broke seat, the only one that arms, can't train its workers back.
    /// </summary>
    private static void Leg(AiPlayer ai)
    {
        Blackboard board = ai.Board;
        if (board.FarStart < 0 && !NextLeg(ai)) return;
        Vector2 point = LookPoint(ai, board.FarStart, board.FarLook);
        int slot = board.FarScoutSlot;
        if (slot < 0 || !ai.OwnUnit(slot, board.FarScoutGeneration, out SeenUnit u))
        {
            // None yet, or it died on the way: another worker takes the leg from the point it was walking to. BUG-1158: as the
            // scout's (BUG-1052), only while the player's own unit losses are at most ai.scoutReplacements; past that the leg
            // is over (every start counts looked), so a broke seat, which can't train its workers back, stops feeding them to
            // whatever guards a start.
            if (slot >= 0 && ai.View.OwnUnitLosses > ai.Rules.ScoutReplacements)
            {
                board.FarLooked |= AllStarts(ai);
                EndLeg(ai, stop: true);
                return;
            }
            board.FarScoutSlot = -1;
            board.FarScoutGeneration = 0;
            int picked = ai.PickBuilder(point, out int generation);
            if (picked < 0) return;
            board.FarScoutSlot = picked;
            board.FarScoutGeneration = generation;
            board.FarMoved = false;
            ai.View.TryGetUnit(picked, out u);
            slot = picked;
        }
        if (ai.OrderedNotApplied(slot, u.Handle.Generation)) return;
        float reach = ai.Rules.ScoutReach * ai.Rules.ScoutReach;
        if (board.FarMoved && (Vector2.DistanceSquared(u.Position, point) <= reach || u.State == UnitState.Idle))
        {
            board.FarMoved = false;
            if (++board.FarLook == LookPoints)
            {
                board.FarLooked |= 1 << board.FarStart;
                board.FarStart = -1;
                board.FarLook = 0;
                if (!NextLeg(ai)) return;
            }
            point = LookPoint(ai, board.FarStart, board.FarLook);
        }
        if (board.FarMoved) return; // on its way
        if (!ai.Send(Command.Move(ai.Player, u.Handle, point))) return;
        ai.MarkOrdered(slot, u.Handle.Generation, 0);
        board.FarMoved = true;
    }

    /// <summary>Starts the leg at the next start (<see cref="NextStart"/>); with none, ends the leg (its worker stopped) and returns false.</summary>
    private static bool NextLeg(AiPlayer ai)
    {
        int next = NextStart(ai);
        if (next < 0)
        {
            EndLeg(ai, stop: true);
            return false;
        }
        ai.Board.FarStart = next;
        ai.Board.FarLook = 0;
        ai.Board.FarMoved = false;
        return true;
    }

    /// <summary>
    /// The start location to look at next: of the free ones (no player starts there: index at or past
    /// <see cref="PlayerView.PlayerCount"/>, player p starts on location p; or its player is defeated,
    /// <see cref="PlayerView.IsDefeated"/>, public under the win rule) not yet looked at, the one nearest the hall's centre in
    /// whole cells (squared); on a tie the seat's own turn of the tied ones: the starts taken in a ring from index
    /// <c>PlayerCount + Player</c> (mod their count). Why: on a symmetric map two seats are often exactly as far from two free
    /// starts (Pale Hills 1v1: both free starts 129 cells from both halls); the lowest index sent both to one start, where
    /// they fought over it and lost their workers, while the other start's 9,000 gold stayed untouched. M5-H6 (BUG-1159): a
    /// start whose centre is farther than <c>ai.farExpandReach</c> from the hall's is skipped (no mine there could be a
    /// <see cref="Candidate"/>). -1 for none (a 2-player map with two starts has none while both play).
    /// </summary>
    internal static int NextStart(AiPlayer ai)
    {
        int players = ai.View.PlayerCount, count = Math.Min(ai.View.StartCount, MaxStarts), best = -1;
        if (count == 0) return -1;
        int w = ai.View.MapWidth;
        int hx = (int)(ai.HallPoint.X / MapConstants.CellSize), hy = (int)(ai.HallPoint.Y / MapConstants.CellSize);
        float reach = ai.Rules.FarExpandReach * ai.Rules.FarExpandReach;
        long bestDistance = long.MaxValue;
        int bestTurn = int.MaxValue;
        for (int k = 0; k < count; k++)
        {
            if ((ai.Board.FarLooked & (1 << k)) != 0 || k < players && (k == ai.Player || !ai.View.IsDefeated(k))) continue;
            int cell = ai.View.StartCell(k);
            // BUG-1159: a start farther from the hall than ai.farExpandReach has no mine Candidate would take; not walked to.
            if (Vector2.DistanceSquared(ai.CellCentre(cell), ai.HallPoint) > reach) continue;
            long dx = cell % w - hx, dy = cell / w - hy, d = dx * dx + dy * dy;
            int turn = ((k - players - ai.Player) % count + count) % count;
            if (d > bestDistance || d == bestDistance && turn >= bestTurn) continue;
            bestDistance = d;
            bestTurn = turn;
            best = k;
        }
        return best;
    }

    /// <summary>BUG-1158: <see cref="Blackboard.FarLooked"/> with every start location the map has (up to <see cref="MaxStarts"/>) marked.</summary>
    private static int AllStarts(AiPlayer ai)
    {
        int count = Math.Min(ai.View.StartCount, MaxStarts);
        return count >= MaxStarts ? int.MaxValue : (1 << count) - 1;
    }

    /// <summary>Look point <paramref name="look"/> of start <paramref name="start"/>: its centre cell's centre, then east, south, west, north <see cref="LookRadius"/> off it, clamped inside the map.</summary>
    internal static Vector2 LookPoint(AiPlayer ai, int start, int look) =>
        ai.ClampToMap(ai.CellCentre(ai.View.StartCell(start)) + s_look[look] * LookRadius);
}
