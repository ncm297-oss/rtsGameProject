using System;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.Ai;

/// <summary>
/// The AI's base defense (M5-4b, docs/03 "Implementation (M5-4b)"; sized by the threat since M5-4f, docs/03 "Implementation
/// (M5-4f)"), first in a think after the census: docs/02 "AI opponent" puts defending the base before every other behaviour.
/// <list type="bullet">
/// <item><b>Trigger.</b> An own building (a site too) within <c>ai.defendRadius</c> of the hall's centre has fewer hp than at
/// the last think (a building seen for the first time doesn't count), or an enemy non-worker unit is seen within
/// <c>ai.defendRadius</c> of the hall. The threat holds for <c>ai.defendFor</c> after the last trigger.</item>
/// <item><b>Point.</b> The centroid of the enemy non-workers seen within the radius, else the footprint centre of the damaged
/// building (slot order); it moves (a new version) when a new threat starts, or on the second trigger in a row whose point is
/// more than <c>ai.rallyReach</c> away (BUG-0673).</item>
/// <item><b>The sized answer (M5-4f).</b> On a trigger the threat's worth is the gold + wood cost of the enemy non-workers
/// seen within the radius (a hit with none seen counts as <c>ai.defendMinValue</c>), and the answer's worth is
/// max(<c>ai.defendRatio</c> x the threat, <c>ai.defendMinValue</c>) (with none seen, the minimum); it is kept through the
/// hold. Defenders are picked and stay picked while the threat holds: first free army units outside the wave, nearest the
/// point, then members of the wave in progress nearest the hall while the members left keep the wave's start threshold;
/// each while the picked worth is short of the answer by more than half the unit's cost (the first always). The whole
/// wave comes home (ended like a retreat, the cooldown) and every free army unit answers until the threat ends when the
/// threat outweighs the picked, the home army and the members the threshold lets go together, or when the hall lost hp
/// this think and is below <c>ai.recallHallHp</c> of its max hp. Otherwise a wave in progress goes on with the rest. The
/// military starts no wave and rallies nobody while the threat holds.</item>
/// <item><b>Orders.</b> Every defender (every army unit once the whole wave was called) that is free (not casting, no AI
/// command pending, not owed its Move home), farther than <c>ai.rallyReach</c> from the point and not yet sent to this
/// version gets one <c>AttackMove</c> there if it is within <c>ai.defendRadius</c> of the hall, else a <c>Move</c> there and
/// the <c>AttackMove</c> once it is (BUG-0670). Every own worker within <c>ai.workerFleeRadius</c> of the point gets one
/// <c>Move</c> to the nearest own finished drop-off (one outside that radius first), once a threat, and the economy leaves
/// it alone for <c>ai.defendFor</c> after its flee (BUG-0671), then gathers only at nodes away from the point while the
/// threat holds.</item>
/// </list>
/// Reads only the player's view (own buildings' hp are own state; the enemy through <see cref="SeenUnit"/>; costs from
/// <see cref="GameData.Units"/>).
/// </summary>
internal static class DefenseManager
{
    /// <summary>Notes hits, updates the threat, its point and its answer, picks the defenders, and sends them and the workers while it holds.</summary>
    internal static void Run(AiPlayer ai)
    {
        Blackboard board = ai.Board;
        bool hit = NoteHits(ai, out Vector2 hitAt, out bool hallLow);
        bool trigger = hit || ai.IntruderCount > 0;
        if (trigger)
        {
            bool fresh = !Holds(ai, board.DefendTick);
            if (fresh)
            {
                // A new threat: the last one's picks go (they went at its end, unless no think fell in between).
                Array.Clear(board.DefenderGeneration);
                board.DefendAll = false;
            }
            board.DefendTick = ai.Now;
            Vector2 point = ai.IntruderCount > 0 ? ai.IntruderCentroid : hitAt;
            float reach = ai.Rules.RallyReach;
            bool away = Vector2.DistanceSquared(point, board.DefensePoint) > reach * reach;
            // BUG-0673: the point moves only on the second trigger in a row away from it, so threats taking turns at two
            // spots (the centroid jumping back and forth) don't re-send the army every think.
            if (fresh || away && board.DefensePointAway)
            {
                board.DefensePoint = point;
                board.DefenseVersion++;
                away = false;
            }
            board.DefensePointAway = away;
            if (fresh) board.Defenses++;
        }
        if (!Holds(ai, board.DefendTick))
        {
            // The threat is over: the flee marks and the picks go, so the next threat starts from scratch.
            if (board.Defending)
            {
                Array.Clear(board.FledGeneration);
                Array.Clear(board.DefenderGeneration);
            }
            board.Defending = false;
            board.DefendAll = false;
            board.DefensePointAway = false;
            return;
        }
        board.Defending = true;
        if (trigger) Size(ai, hit, hallLow);
        else if (!board.DefendAll) Pick(ai, board.DefendAnswer, 0f, recallTest: false); // the hold: replace the fallen
        if (Army(ai)) Workers(ai);
    }

    /// <summary>Whether a trigger at <paramref name="tick"/> still holds at this think (never for -1).</summary>
    private static bool Holds(AiPlayer ai, int tick) => tick >= 0 && ai.Now - tick <= ai.Rules.DefendForTicks;

    /// <summary>
    /// M5-4f, on a trigger: the threat's worth and the answer's (kept for the hold), then the picks; the whole wave is called
    /// home when the hall is low or the threat outweighs everything the AI may send without breaking the wave.
    /// </summary>
    private static void Size(AiPlayer ai, bool hit, bool hallLow)
    {
        Blackboard board = ai.Board;
        AiRulesDef rules = ai.Rules;
        float threat, answer;
        if (ai.IntruderCount > 0)
        {
            threat = ai.IntruderWorth;
            answer = Math.Max(rules.DefendRatio * threat, rules.DefendMinValue);
        }
        else
        {
            // Hit by something unseen (a catapult in the fog): the minimum threat, answered with the minimum.
            threat = hit ? rules.DefendMinValue : 0f;
            answer = rules.DefendMinValue;
        }
        board.DefendAnswer = answer;
        if (board.DefendAll) return;
        if (hallLow || !Pick(ai, answer, threat, recallTest: true)) Recall(ai);
    }

    /// <summary>The whole wave home (like a retreat: the cooldown), and every free army unit answers until the threat ends.</summary>
    private static void Recall(AiPlayer ai)
    {
        Blackboard board = ai.Board;
        board.DefendAll = true;
        if (!board.WaveActive) return;
        MilitaryManager.End(ai);
        board.WaveCooldownUntil = ai.Now + ai.Rules.WaveCooldownTicks;
        board.WaveRecalls++;
    }

    /// <summary>
    /// Picks defenders toward <paramref name="answer"/> (worth, gold + wood): the units picked earlier in this threat count
    /// first, then free army units outside the wave nearest the point, then free members of the wave in progress nearest
    /// the hall while the members left keep its start threshold (pulled: out of the wave, their half-pop out of its
    /// start's, like a member that leaves alive). A unit is picked while the picked worth is short of the answer by more
    /// than half its cost, or when none is picked yet. With <paramref name="recallTest"/>: false, nothing picked, when a wave
    /// is in progress and <paramref name="threat"/> outweighs the picked, the home army and the members the threshold lets
    /// go together. Ties in distance keep slot order. No allocation: the order is a fixed scratch array on the board, sorted
    /// in place only when a pick may follow.
    /// </summary>
    private static bool Pick(AiPlayer ai, float answer, float threat, bool recallTest)
    {
        Blackboard board = ai.Board;
        float picked = 0f, memberHalf = 0f, memberCost = 0f;
        // One pass: the picked so far, the members' totals, the home candidates from the front of the scratch order (keyed
        // by distance to the point) and the member candidates from its back (by distance to the hall), moved after them.
        int home = 0, top = board.DefenseOrder.Length, cheapest = int.MaxValue;
        for (int k = 0; k < ai.ArmyCount; k++)
        {
            int slot = ai.ArmySlots[k];
            if (!ai.View.TryGetUnit(slot, out SeenUnit u)) continue;
            int generation = u.Handle.Generation;
            UnitDef def = ai.Data.Units[u.TypeId];
            if (board.DefenderGeneration[slot] == generation)
            {
                picked += def.CostGold + def.CostWood;
                continue;
            }
            bool member = board.WaveActive && board.WaveGeneration[slot] == generation && board.WaveFlag[slot] != MilitaryManager.ToRally;
            if (member)
            {
                memberHalf += def.HalfPop;
                memberCost += def.CostGold + def.CostWood;
            }
            if (!Free(ai, slot, u)) continue;
            cheapest = Math.Min(cheapest, def.CostGold + def.CostWood);
            int at = member ? --top : home++;
            board.DefenseOrder[at] = slot;
            board.DefenseKey[at] = Vector2.DistanceSquared(u.Position, member ? ai.HallPoint : board.DefensePoint);
        }
        int count = home;
        for (int i = top; i < board.DefenseOrder.Length; i++, count++)
        {
            board.DefenseOrder[count] = board.DefenseOrder[i];
            board.DefenseKey[count] = board.DefenseKey[i];
        }
        board.DefenseOrderCount = count;
        bool membersSorted = false;
        if (recallTest && board.WaveActive)
        {
            // The home army counts whole (all of it may go: order doesn't matter); the members nearest the hall first.
            Sort(board, home, board.DefenseOrderCount);
            membersSorted = true;
            float spare = 0f, half = memberHalf, worth = memberCost;
            for (int i = 0; i < board.DefenseOrderCount; i++)
            {
                UnitDef def = TypeOf(ai, board.DefenseOrder[i]);
                int cost = def.CostGold + def.CostWood;
                if (i >= home)
                {
                    if (!Keeps(board, half - def.HalfPop, worth - cost)) continue;
                    half -= def.HalfPop;
                    worth -= cost;
                }
                spare += cost;
            }
            if (threat > picked + spare) return false;
        }
        // Met already: no candidate can be picked (picked + half the cheapest's cost is at least the answer): nothing sorted.
        if (count == 0 || picked > 0f && picked + cheapest * 0.5f >= answer) return true;
        // (a) The home army nearest first off a min-heap: only as many pops as picks need (a thousand units at home, a few
        // picks a think), stopping once even the cheapest candidate can't be picked.
        for (int i = home / 2 - 1; i >= 0; i--) SiftDown(board, 0, i, home, minHeap: true);
        for (int left = home; left > 0 && !(picked > 0f && picked + cheapest * 0.5f >= answer); )
        {
            int slot = board.DefenseOrder[0];
            Swap(board, 0, --left);
            SiftDown(board, 0, 0, left, minHeap: true);
            UnitDef def = TypeOf(ai, slot);
            int cost = def.CostGold + def.CostWood;
            if (picked > 0f && picked + cost * 0.5f >= answer) continue;
            ai.View.TryGetUnit(slot, out SeenUnit u);
            board.DefenderGeneration[slot] = u.Handle.Generation;
            picked += cost;
        }
        if (home == count || picked > 0f && picked + cheapest * 0.5f >= answer) return true;
        // (b) The members nearest the hall first, while the rest keep the start threshold.
        if (!membersSorted) Sort(board, home, count);
        for (int i = home; i < count; i++)
        {
            int slot = board.DefenseOrder[i];
            UnitDef def = TypeOf(ai, slot);
            int cost = def.CostGold + def.CostWood;
            if (picked > 0f && picked + cost * 0.5f >= answer || !Keeps(board, memberHalf - def.HalfPop, memberCost - cost)) continue;
            memberHalf -= def.HalfPop;
            memberCost -= cost;
            board.WaveGeneration[slot] = 0; // out of the wave: its own rule doesn't send it back
            board.WaveFlag[slot] = 0;
            board.WaveStartHalfPop -= def.HalfPop; // not a loss toward the retreat
            board.WavePulls++;
            ai.View.TryGetUnit(slot, out SeenUnit u);
            board.DefenderGeneration[slot] = u.Handle.Generation;
            picked += cost;
        }
        return true;
    }

    /// <summary>Whether members worth <paramref name="half"/> half-pop and <paramref name="cost"/> gold + wood keep the wave's start threshold.</summary>
    private static bool Keeps(Blackboard board, float half, float cost) => half >= board.WaveMinHalfPop && cost >= board.WaveMinCost;

    /// <summary>The type of own unit <paramref name="slot"/> (the caller saw it live this think).</summary>
    private static UnitDef TypeOf(AiPlayer ai, int slot)
    {
        ai.View.TryGetUnit(slot, out SeenUnit u);
        return ai.Data.Units[u.TypeId];
    }

    /// <summary>Whether own army unit <paramref name="u"/> may be ordered now: not casting, no AI command pending, not the scout, not owed its Move home.</summary>
    private static bool Free(AiPlayer ai, int slot, in SeenUnit u)
    {
        int generation = u.Handle.Generation;
        if (u.State == UnitState.Casting || ai.OrderedNotApplied(slot, generation) || ai.IsScout(slot, generation)) return false;
        return !(ai.Board.WaveGeneration[slot] == generation && ai.Board.WaveFlag[slot] == MilitaryManager.ToRally);
    }

    /// <summary>
    /// Sorts the scratch order's entries <paramref name="lo"/> to <paramref name="hi"/> (exclusive) ascending by key, then
    /// slot: a heapsort in place (n log n, no allocation; an insertion sort cost several ms a think with a thousand units at
    /// home), deterministic since (key, slot) orders every pair.
    /// </summary>
    private static void Sort(Blackboard board, int lo, int hi)
    {
        int n = hi - lo;
        for (int i = n / 2 - 1; i >= 0; i--) SiftDown(board, lo, i, n, minHeap: false);
        for (int end = n - 1; end > 0; end--)
        {
            Swap(board, lo, lo + end);
            SiftDown(board, lo, 0, end, minHeap: false);
        }
    }

    /// <summary>
    /// Restores the heap (the farthest on top, or with <paramref name="minHeap"/> the nearest) of the <paramref name="n"/>
    /// entries from <paramref name="lo"/> below node <paramref name="i"/>.
    /// </summary>
    private static void SiftDown(Blackboard board, int lo, int i, int n, bool minHeap)
    {
        while (true)
        {
            int child = 2 * i + 1;
            if (child >= n) return;
            if (child + 1 < n && Above(board, lo + child + 1, lo + child, minHeap)) child++;
            if (!Above(board, lo + child, lo + i, minHeap)) return;
            Swap(board, lo + i, lo + child);
            i = child;
        }
    }

    /// <summary>Whether scratch entry <paramref name="a"/> belongs above <paramref name="b"/> in the heap.</summary>
    private static bool Above(Blackboard board, int a, int b, bool minHeap) => minHeap ? After(board, b, a) : After(board, a, b);

    /// <summary>Whether scratch entry <paramref name="a"/> sorts after <paramref name="b"/> (farther, or as far with a higher slot).</summary>
    private static bool After(Blackboard board, int a, int b) =>
        board.DefenseKey[a] > board.DefenseKey[b] || board.DefenseKey[a] == board.DefenseKey[b] && board.DefenseOrder[a] > board.DefenseOrder[b];

    private static void Swap(Blackboard board, int a, int b)
    {
        (board.DefenseOrder[a], board.DefenseOrder[b]) = (board.DefenseOrder[b], board.DefenseOrder[a]);
        (board.DefenseKey[a], board.DefenseKey[b]) = (board.DefenseKey[b], board.DefenseKey[a]);
    }

    /// <summary>
    /// Compares each own building's hp to the one kept from the last think and keeps the new one; true, with its footprint
    /// centre, for the first (slot order) within the radius of the hall that lost hp. M5-4f: <paramref name="hallLow"/> when
    /// the hall's building is one that lost hp and is now below <c>ai.recallHallHp</c> of its type's max hp.
    /// </summary>
    private static bool NoteHits(AiPlayer ai, out Vector2 at, out bool hallLow)
    {
        Blackboard board = ai.Board;
        at = default;
        hallLow = false;
        bool found = false;
        float radius = ai.Rules.DefendRadius;
        for (int k = 0; k < ai.OwnBuildingCount; k++)
        {
            int slot = ai.OwnBuildingSlots[k];
            if (!ai.View.TryGetBuilding(slot, out SeenBuilding b)) continue;
            bool lost = ai.HasHall && board.KeptHpGeneration[slot] == b.Handle.Generation && b.Hp < board.KeptHp[slot];
            if (lost && slot == ai.HallSlot && b.Hp < ai.Rules.RecallHallHp * ai.Data.Buildings[b.TypeId].Hp) hallLow = true;
            if (!found && lost)
            {
                Vector2 centre = ai.BuildingCentre(b.TypeId, b.Cell);
                if (Vector2.DistanceSquared(centre, ai.HallPoint) <= radius * radius)
                {
                    at = centre;
                    found = true;
                }
            }
            board.KeptHp[slot] = b.Hp;
            board.KeptHpGeneration[slot] = b.Handle.Generation;
        }
        return found;
    }

    /// <summary>
    /// Per defender (M5-4f: every army unit once the whole wave was called) and version: one AttackMove to the point when it
    /// is inside the base (within <c>ai.defendRadius</c> of the hall's centre), else first a Move to the point and the
    /// AttackMove once it is inside (BUG-0670: an AttackMove from the enemy base fights on there). False when the queue filled.
    /// </summary>
    private static bool Army(AiPlayer ai)
    {
        Blackboard board = ai.Board;
        float reach = ai.Rules.RallyReach, radius = ai.Rules.DefendRadius;
        for (int k = 0; k < ai.ArmyCount; k++)
        {
            int slot = ai.ArmySlots[k];
            if (!ai.View.TryGetUnit(slot, out SeenUnit u) || u.State == UnitState.Casting) continue;
            int generation = u.Handle.Generation;
            if (!board.DefendAll && board.DefenderGeneration[slot] != generation) continue; // M5-4f: not picked
            if (ai.OrderedNotApplied(slot, generation) || ai.IsScout(slot, generation)) continue;
            // A wave member owed its Move home gets it from the military this think; it is sent here at the next.
            if (board.WaveGeneration[slot] == generation && board.WaveFlag[slot] == MilitaryManager.ToRally) continue;
            bool inside = !ai.HasHall || Vector2.DistanceSquared(u.Position, ai.HallPoint) <= radius * radius;
            // Sent to this version already: done, unless it was the Move home and the unit is inside now.
            if (board.UnitGeneration[slot] == generation && board.DefendedVersion[slot] == board.DefenseVersion
                && (!board.Homing[slot] || !inside)) continue;
            if (Vector2.DistanceSquared(u.Position, board.DefensePoint) <= reach * reach) continue;
            Command c = inside ? Command.AttackMove(ai.Player, u.Handle, board.DefensePoint) : Command.Move(ai.Player, u.Handle, board.DefensePoint);
            if (!ai.Send(c)) return false;
            ai.MarkOrdered(slot, generation, 0);
            board.DefendedVersion[slot] = board.DefenseVersion;
            board.Homing[slot] = !inside;
            board.RalliedVersion[slot] = 0; // after the threat the rally rule brings it back
        }
        return true;
    }

    /// <summary>One Move to the nearest own finished drop-off per worker within the flee radius of the point, once a threat.</summary>
    private static void Workers(AiPlayer ai)
    {
        Blackboard board = ai.Board;
        float flee = ai.Rules.WorkerFleeRadius;
        for (int k = 0; k < ai.WorkerCount; k++)
        {
            int slot = ai.WorkerSlots[k];
            if (!ai.View.TryGetUnit(slot, out SeenUnit u)) continue;
            int generation = u.Handle.Generation;
            if (board.FledGeneration[slot] == generation || ai.OrderedNotApplied(slot, generation) || ai.IsScout(slot, generation)) continue;
            if (Vector2.DistanceSquared(u.Position, board.DefensePoint) > flee * flee) continue;
            if (!NearestDropOff(ai, u.Position, out Vector2 to)) return;
            if (!ai.Send(Command.Move(ai.Player, u.Handle, to))) return;
            ai.MarkOrdered(slot, generation, 0);
            board.FledGeneration[slot] = generation;
            board.FledAt[slot] = ai.Now;
        }
    }

    /// <summary>
    /// The footprint centre of the own finished drop-off building nearest <paramref name="from"/> (slot order on ties), one
    /// farther than <c>ai.workerFleeRadius</c> from the point first (BUG-0671: not the building under attack when another
    /// will do); false with none.
    /// </summary>
    private static bool NearestDropOff(AiPlayer ai, Vector2 from, out Vector2 at)
    {
        at = default;
        float best = float.MaxValue, flee = ai.Rules.WorkerFleeRadius;
        bool bestClear = false;
        for (int k = 0; k < ai.OwnBuildingCount; k++)
        {
            if (ai.OwnBuildingSites[k] || !ai.Data.Buildings[ai.OwnBuildingTypes[k]].DropOff) continue;
            Vector2 centre = ai.BuildingCentre(ai.OwnBuildingTypes[k], ai.OwnBuildingCells[k]);
            bool clear = Vector2.DistanceSquared(centre, ai.Board.DefensePoint) > flee * flee;
            float d = Vector2.DistanceSquared(centre, from);
            if (bestClear && !clear || clear == bestClear && d >= best) continue;
            best = d;
            bestClear = clear;
            at = centre;
        }
        return best < float.MaxValue;
    }
}
