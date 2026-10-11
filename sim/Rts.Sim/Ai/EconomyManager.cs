using System;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.Ai;

/// <summary>
/// The AI's economy (M5-2, docs/03 "Implementation (M5-2)"): every idle worker is sent to gather, to gold while fewer than
/// <see cref="AiRulesDef.GoldShare"/> of the gathering workers are on gold, else to wood (the known node of that kind nearest
/// the worker; the other kind when none is known); and a house is placed when the population cap is within
/// <see cref="AiRulesDef.HouseHalfPopMargin"/> of the population (and below the rules' cap) and no house is under
/// construction or waiting to be judged.
/// </summary>
internal static class EconomyManager
{
    private const byte Gold = 1, Wood = 2;

    /// <summary>Set on <see cref="Blackboard.GatherKind"/> for this think when the worker's last Gather was dropped (BUG-0570).</summary>
    private const byte Dropped = 0x80;

    /// <summary>Runs the house rule, then sends the idle workers to gather.</summary>
    internal static void Run(AiPlayer ai)
    {
        House(ai);
        Gatherers(ai);
    }

    private static void House(AiPlayer ai)
    {
        Blackboard board = ai.Board;
        if (ai.HouseType < 0) return;
        if (board.HousePending.Kind != PendingKind.None)
        {
            if (ai.Now <= board.HousePending.AppliesAt) return; // not applied yet
            if (!ai.BuildAccepted(board.HousePending)) board.MarkBad(board.HousePending.TypeId, board.HousePending.Cell);
            board.HousePending = default;
        }
        PlayerView v = ai.View;
        if (v.HalfPopCap >= ai.Data.Rules.HalfPopCap || v.HalfPopCap - v.HalfPop > ai.Rules.HouseHalfPopMargin) return;
        for (int k = 0; k < ai.OwnBuildingCount; k++)
            if (ai.OwnBuildingSites[k] && ai.OwnBuildingTypes[k] == ai.HouseType) return; // one under construction
        int centre = BuildOrderExecutor.HallCentre(ai);
        if (centre >= 0) ai.TryBuild(ai.HouseType, centre, ref board.HousePending);
    }

    private static void Gatherers(AiPlayer ai)
    {
        Blackboard board = ai.Board;
        PlayerView v = ai.View;
        // Who gathers what now, from what each worker was last sent to (only workers still at it count).
        int gold = 0, wood = 0, idle = 0;
        for (int k = 0; k < ai.WorkerCount; k++)
        {
            int slot = ai.WorkerSlots[k];
            if (!v.TryGetUnit(slot, out SeenUnit u) || ai.OrderedNotApplied(slot, u.Handle.Generation)) continue;
            if (u.State == UnitState.Idle)
            {
                idle++;
                NoteDroppedGather(ai, slot, u.Handle.Generation);
                continue;
            }
            if (board.UnitGeneration[slot] != u.Handle.Generation) continue;
            board.GatherNode[slot] = default; // at work after its last order applied: that Gather wasn't dropped
            if (u.State is UnitState.Building) continue;
            if (board.GatherKind[slot] == Gold) gold++;
            else if (board.GatherKind[slot] == Wood) wood++;
        }
        if (idle == 0) return;
        int total = gold + wood + idle;
        int wantGold = (int)MathF.Floor(ai.Rules.GoldShare * total + 0.5f);
        for (int k = 0; k < ai.WorkerCount; k++)
        {
            int slot = ai.WorkerSlots[k];
            if (!v.TryGetUnit(slot, out SeenUnit u) || u.State != UnitState.Idle || ai.OrderedNotApplied(slot, u.Handle.Generation)) continue;
            // A worker whose Gather was just dropped tries the other kind first (as with none of that kind known), so it
            // doesn't spend a think on each remembered node that is gone (BUG-0570).
            byte last = board.GatherKind[slot];
            byte kind = (last & Dropped) != 0 ? ((last & Wood) != 0 ? Gold : Wood) : gold < wantGold ? Gold : Wood;
            int node = NearestNode(ai, kind, u, out int nodeType, out EntityHandle nodeHandle);
            if (node < 0)
            {
                kind = kind == Gold ? Wood : Gold;
                node = NearestNode(ai, kind, u, out nodeType, out nodeHandle);
                if (node < 0) continue;
            }
            if (!ai.Send(Command.Gather(ai.Player, u.Handle, ai.NodeCentre(nodeType, node)))) return;
            ai.MarkOrdered(slot, u.Handle.Generation, kind, nodeHandle);
            if (kind == Gold) gold++;
            else wood++;
        }
    }

    /// <summary>
    /// BUG-0570: an idle worker whose last AI command was a Gather that has applied, to a node the player doesn't see (no
    /// <c>Remaining</c>), had it dropped: the node is gone (a Gather to a live node never leaves the worker Idle out of the
    /// node's sight). The node is marked so no worker is sent there again; the player's memory forgets it once it sees the
    /// ground. A node in sight is never marked: what it shows is the truth. The worker is flagged to try the other kind first.
    /// </summary>
    private static void NoteDroppedGather(AiPlayer ai, int slot, int generation)
    {
        Blackboard board = ai.Board;
        if (board.UnitGeneration[slot] != generation) return;
        board.GatherKind[slot] &= unchecked((byte)~Dropped);
        EntityHandle node = board.GatherNode[slot];
        if (node.Generation == 0) return;
        board.GatherNode[slot] = default; // judged once
        if (!ai.View.TryGetResource(node.Index, out SeenResource seen) || seen.Handle != node || seen.Remaining >= 0) return;
        board.NodeDropped[node.Index] = node.Generation;
        board.GatherKind[slot] |= Dropped;
    }

    /// <summary>The anchor cell of the known node of <paramref name="kind"/> nearest worker <paramref name="u"/> (slot order on ties), skipping nodes a Gather was dropped at; -1 for none.</summary>
    private static int NearestNode(AiPlayer ai, byte kind, in SeenUnit u, out int nodeType, out EntityHandle handle)
    {
        ResourceKind want = kind == Gold ? ResourceKind.Gold : ResourceKind.Wood;
        int best = -1, s = -1;
        nodeType = -1;
        handle = default;
        float bestDistance = float.MaxValue;
        while (ai.View.NextResource(ref s, out SeenResource node))
        {
            if (ai.Data.Resources[node.TypeId].Resource != want || ai.Board.NodeDropped[s] == node.Handle.Generation) continue;
            float d = System.Numerics.Vector2.DistanceSquared(ai.NodeCentre(node.TypeId, node.Cell), u.Position);
            if (d >= bestDistance) continue;
            best = node.Cell;
            bestDistance = d;
            nodeType = node.TypeId;
            handle = node.Handle;
        }
        return best;
    }
}
