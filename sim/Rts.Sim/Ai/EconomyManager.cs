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
/// construction or waiting to be judged. M5-4b: a worker sent fleeing by the defense is left alone until
/// <c>ai.defendFor</c> has passed since it fled, and while the threat holds no worker is sent to a node within
/// <c>ai.workerFleeRadius</c> of the defense point (BUG-0671). M5-H1 (BUG-0640): an own site no worker has as its build target for
/// <see cref="StalledThinks"/> thinks gets a worker sent to join it (a Build of its type at its anchor: the idle worker
/// nearest it, else the nearest gatherer), and such a stalled house site does not hold the house rule off. M5-H2 (BUG-0650):
/// after <see cref="MaxJoins"/> joins with no rise in the site's hit points (no worker ever got to work it) it is left
/// alone until they rise.
/// </summary>
internal static class EconomyManager
{
    private const byte Gold = 1, Wood = 2;

    /// <summary>Set on <see cref="Blackboard.GatherKind"/> for this think when the worker's last Gather was dropped (BUG-0570).</summary>
    private const byte Dropped = 0x80;

    /// <summary>
    /// Thinks an own site may go without a builder before a worker is sent to it (BUG-0640): a Build that just applied, or a
    /// worker sent and not yet counted, isn't taken for an abandoned site. Not a balance number: a guard.
    /// </summary>
    internal const int StalledThinks = 3;

    /// <summary>
    /// Joins sent to one site without its hit points rising before the AI stops sending workers to it (BUG-0650: a site no
    /// worker can reach). Not a balance number: a guard, like <see cref="MilitaryManager.MaxTries"/>.
    /// </summary>
    internal const int MaxJoins = 3;

    /// <summary>Resumes stalled sites, runs the house rule, then sends the idle workers to gather.</summary>
    internal static void Run(AiPlayer ai)
    {
        Sites(ai);
        House(ai);
        ClearSeenMarks(ai);
        Gatherers(ai);
    }

    /// <summary>
    /// M5-3 (BUG-0574): a marked node the player sees alive again (its <c>Remaining</c> is known only then) loses its mark: what
    /// it shows is the truth, so a mark that was wrong doesn't keep workers off it for good. Runs only while a mark is set.
    /// </summary>
    private static void ClearSeenMarks(AiPlayer ai)
    {
        Blackboard board = ai.Board;
        if (board.NodeMarks == 0) return;
        int[] marks = board.NodeDropped;
        for (int s = 0; s < marks.Length; s++)
        {
            if (marks[s] == 0 || !ai.View.TryGetResource(s, out SeenResource node) || node.Handle.Generation != marks[s] || node.Remaining < 0) continue;
            marks[s] = 0;
            board.NodeMarks--;
        }
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
            if (ai.OwnBuildingSites[k] && ai.OwnBuildingTypes[k] == ai.HouseType && !Stalled(ai, ai.OwnBuildingSlots[k])) return; // one being built
        int centre = BuildOrderExecutor.HallCentre(ai);
        if (centre >= 0) ai.TryBuild(ai.HouseType, centre, ref board.HousePending);
    }

    /// <summary>
    /// BUG-0640: counts each own site's thinks with no builder (no own worker has it as its build target); one without for
    /// <see cref="StalledThinks"/> thinks gets a worker sent to join it, and its count restarts. One join a think at most.
    /// </summary>
    private static void Sites(AiPlayer ai)
    {
        Blackboard board = ai.Board;
        for (int k = 0; k < ai.WorkerCount; k++)
            if (ai.View.TryGetBuildTarget(ai.WorkerSlots[k], out EntityHandle target)) board.SiteBuiltAt[target.Index] = ai.Now;
        bool sent = false;
        for (int k = 0; k < ai.OwnBuildingCount; k++)
        {
            int slot = ai.OwnBuildingSlots[k];
            if (!ai.OwnBuildingSites[k] || !ai.View.TryGetBuilding(slot, out SeenBuilding site)) continue;
            if (board.SiteGeneration[slot] != site.Handle.Generation)
            {
                board.SiteGeneration[slot] = site.Handle.Generation;
                board.SiteStill[slot] = 0;
                board.SiteJoins[slot] = 0;
            }
            if (board.SiteBuiltAt[slot] == ai.Now) board.SiteStill[slot] = 0;
            else if (board.SiteStill[slot] < byte.MaxValue) board.SiteStill[slot]++;
            if (sent || board.SiteStill[slot] < StalledThinks) continue;
            if (site.Hp > board.SiteJoinHp[slot]) board.SiteJoins[slot] = 0; // worked since the last join
            if (board.SiteJoins[slot] >= MaxJoins) continue; // nobody gets there: left alone until it is worked
            int worker = ai.PickBuilder(ai.BuildingCentre(site.TypeId, site.Cell), out int generation);
            if (worker < 0) continue;
            if (!ai.Send(Command.Build(ai.Player, new EntityHandle(worker, generation), site.TypeId, ai.CellCentre(site.Cell)))) return;
            ai.MarkOrdered(worker, generation, 0);
            board.SiteStill[slot] = 0;
            board.SiteJoins[slot]++;
            board.SiteJoinHp[slot] = site.Hp;
            sent = true;
        }
    }

    /// <summary>Whether own site <paramref name="slot"/> has stood still for <see cref="StalledThinks"/> thinks (nobody builds it).</summary>
    private static bool Stalled(AiPlayer ai, int slot) => ai.Board.SiteStill[slot] >= StalledThinks;

    private static void Gatherers(AiPlayer ai)
    {
        Blackboard board = ai.Board;
        PlayerView v = ai.View;
        // Who gathers what now, from what each worker was last sent to (only workers still at it count).
        int gold = 0, wood = 0, idle = 0;
        for (int k = 0; k < ai.WorkerCount; k++)
        {
            int slot = ai.WorkerSlots[k];
            if (!v.TryGetUnit(slot, out SeenUnit u) || ai.OrderedNotApplied(slot, u.Handle.Generation) || ai.IsScout(slot, u.Handle.Generation)
                || ai.IsFleeing(slot, u.Handle.Generation)) continue;
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
        if (idle == 0)
        {
            Rebalance(ai, gold, wood);
            return;
        }
        int total = gold + wood + idle;
        int wantGold = (int)MathF.Floor(ai.Rules.GoldShare * total + 0.5f);
        for (int k = 0; k < ai.WorkerCount; k++)
        {
            int slot = ai.WorkerSlots[k];
            if (!v.TryGetUnit(slot, out SeenUnit u) || u.State != UnitState.Idle || ai.OrderedNotApplied(slot, u.Handle.Generation) || ai.IsScout(slot, u.Handle.Generation)
                || ai.IsFleeing(slot, u.Handle.Generation)) continue;
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
    /// M5-3: with no idle worker, one gatherer a think moves to the other kind while the split is off
    /// <see cref="AiRulesDef.GoldShare"/> (more than <c>round(goldShare x gatherers)</c> on gold, or fewer) and a node of the
    /// other kind is known: of those at work on the kind with too many (Gathering, not carrying a load home), the one carrying
    /// the least (M5-H1, BUG-0594: the other kind's Gather drops the load), slot order on ties.
    /// Without it a base whose workers all went to gold before its scout found a forest never gathers wood (no worker is ever
    /// idle again once the cap stops training).
    /// </summary>
    private static void Rebalance(AiPlayer ai, int gold, int wood)
    {
        int want = (int)MathF.Floor(ai.Rules.GoldShare * (gold + wood) + 0.5f);
        if (gold == want) return;
        byte from = gold > want ? Gold : Wood, to = gold > want ? Wood : Gold;
        Blackboard board = ai.Board;
        // BUG-0594: the one carrying the least (slot order on ties), since a Gather of the other kind drops its load.
        int pick = -1, least = int.MaxValue;
        SeenUnit picked = default;
        for (int k = 0; k < ai.WorkerCount && least > 0; k++)
        {
            int slot = ai.WorkerSlots[k];
            if (!ai.View.TryGetUnit(slot, out SeenUnit u) || u.State != UnitState.Gathering || board.UnitGeneration[slot] != u.Handle.Generation
                || board.GatherKind[slot] != from || ai.OrderedNotApplied(slot, u.Handle.Generation) || ai.IsScout(slot, u.Handle.Generation)
                || ai.IsFleeing(slot, u.Handle.Generation)) continue;
            ai.View.TryGetCargo(slot, out int load, out _);
            if (load >= least) continue;
            (pick, least, picked) = (slot, load, u);
        }
        if (pick < 0) return;
        int node = NearestNode(ai, to, picked, out int nodeType, out EntityHandle nodeHandle);
        if (node < 0 || !ai.Send(Command.Gather(ai.Player, picked.Handle, ai.NodeCentre(nodeType, node)))) return;
        ai.MarkOrdered(pick, picked.Handle.Generation, to, nodeHandle);
    }

    /// <summary>
    /// BUG-0570: an idle worker whose last AI command was a Gather that has applied, to a node the player doesn't see (no
    /// <c>Remaining</c>), had it dropped: the node is gone (a Gather to a live node never leaves the worker Idle out of the
    /// node's sight). The node is marked so no worker is sent there again; the player's memory forgets it once it sees the
    /// ground. A node in sight is never marked: what it shows is the truth. The worker is flagged to try the other kind first.
    /// M5-3 (BUG-0574): not judged (and not marked) while the player has no finished drop-off or the worker carries a full
    /// load of the kind it was sent to: such a Gather goes to a drop-off first and ends at once with none, the node aside.
    /// </summary>
    private static void NoteDroppedGather(AiPlayer ai, int slot, int generation)
    {
        Blackboard board = ai.Board;
        if (board.UnitGeneration[slot] != generation) return;
        board.GatherKind[slot] &= unchecked((byte)~Dropped);
        EntityHandle node = board.GatherNode[slot];
        if (node.Generation == 0) return;
        board.GatherNode[slot] = default; // judged once
        if (!ai.HasDropOff || FullLoad(ai, slot, board.GatherKind[slot])) return;
        if (!ai.View.TryGetResource(node.Index, out SeenResource seen) || seen.Handle != node || seen.Remaining >= 0) return;
        if (board.NodeDropped[node.Index] == 0) board.NodeMarks++;
        board.NodeDropped[node.Index] = node.Generation;
        board.GatherKind[slot] |= Dropped;
    }

    /// <summary>Whether own worker <paramref name="slot"/> carries a full load of <paramref name="kind"/> (1 gold, 2 wood).</summary>
    private static bool FullLoad(AiPlayer ai, int slot, byte kind)
    {
        if (!ai.View.TryGetCargo(slot, out int amount, out ResourceKind carried)) return false;
        return amount >= ai.Data.Rules.WorkerCarry && carried == (kind == Gold ? ResourceKind.Gold : ResourceKind.Wood);
    }

    /// <summary>The anchor cell of the known node of <paramref name="kind"/> nearest worker <paramref name="u"/> (slot order on ties), skipping nodes a Gather was dropped at; -1 for none.</summary>
    private static int NearestNode(AiPlayer ai, byte kind, in SeenUnit u, out int nodeType, out EntityHandle handle)
    {
        ResourceKind want = kind == Gold ? ResourceKind.Gold : ResourceKind.Wood;
        int best = -1, s = -1;
        nodeType = -1;
        handle = default;
        float bestDistance = float.MaxValue;
        // BUG-0671: while the base is defended, no worker is sent to a node within the flee radius of the defense point.
        float flee = ai.Board.Defending ? ai.Rules.WorkerFleeRadius * ai.Rules.WorkerFleeRadius : -1f;
        while (ai.View.NextResource(ref s, out SeenResource node))
        {
            if (ai.Data.Resources[node.TypeId].Resource != want || ai.Board.NodeDropped[s] == node.Handle.Generation) continue;
            System.Numerics.Vector2 centre = ai.NodeCentre(node.TypeId, node.Cell);
            if (System.Numerics.Vector2.DistanceSquared(centre, ai.Board.DefensePoint) <= flee) continue;
            float d = System.Numerics.Vector2.DistanceSquared(centre, u.Position);
            if (d >= bestDistance) continue;
            best = node.Cell;
            bestDistance = d;
            nodeType = node.TypeId;
            handle = node.Handle;
        }
        return best;
    }
}
