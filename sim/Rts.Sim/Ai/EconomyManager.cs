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
                continue;
            }
            if (board.UnitGeneration[slot] != u.Handle.Generation || u.State is UnitState.Building) continue;
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
            byte kind = gold < wantGold ? Gold : Wood;
            int node = NearestNode(ai, kind, u, out int nodeType);
            if (node < 0)
            {
                kind = kind == Gold ? Wood : Gold;
                node = NearestNode(ai, kind, u, out nodeType);
                if (node < 0) continue;
            }
            if (!ai.Send(Command.Gather(ai.Player, u.Handle, ai.NodeCentre(nodeType, node)))) return;
            ai.MarkOrdered(slot, u.Handle.Generation, kind);
            if (kind == Gold) gold++;
            else wood++;
        }
    }

    /// <summary>The anchor cell of the known node of <paramref name="kind"/> nearest worker <paramref name="u"/> (slot order on ties); -1 for none.</summary>
    private static int NearestNode(AiPlayer ai, byte kind, in SeenUnit u, out int nodeType)
    {
        ResourceKind want = kind == Gold ? ResourceKind.Gold : ResourceKind.Wood;
        int best = -1, s = -1;
        nodeType = -1;
        float bestDistance = float.MaxValue;
        while (ai.View.NextResource(ref s, out SeenResource node))
        {
            if (ai.Data.Resources[node.TypeId].Resource != want) continue;
            float d = System.Numerics.Vector2.DistanceSquared(ai.NodeCentre(node.TypeId, node.Cell), u.Position);
            if (d >= bestDistance) continue;
            best = node.Cell;
            bestDistance = d;
            nodeType = node.TypeId;
        }
        return best;
    }
}
