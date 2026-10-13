using System;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;

namespace Rts.Sim.Ai;

/// <summary>
/// The AI's base defense (M5-4b, docs/03 "Implementation (M5-4b)"), first in a think after the census: docs/02 "AI opponent"
/// puts defending the base before every other behaviour.
/// <list type="bullet">
/// <item><b>Trigger.</b> An own building (a site too) within <c>ai.defendRadius</c> of the hall's centre has fewer hp than at
/// the last think (a building seen for the first time doesn't count), or an enemy non-worker unit is seen within
/// <c>ai.defendRadius</c> of the hall. The threat holds for <c>ai.defendFor</c> after the last trigger.</item>
/// <item><b>Point.</b> The centroid of the enemy non-workers seen within the radius, else the footprint centre of the damaged
/// building (slot order); it moves (a new version) when a new threat starts, or on the second trigger in a row whose point is
/// more than <c>ai.rallyReach</c> away (BUG-0673).</item>
/// <item><b>While the threat holds.</b> A wave in progress ends like a retreat (members home, the cooldown); the military
/// starts no wave and rallies nobody. Every free army unit (not casting, no AI command pending, not owed its Move home)
/// farther than <c>ai.rallyReach</c> from the point and not yet sent to this version gets one <c>AttackMove</c> there if it
/// is within <c>ai.defendRadius</c> of the hall, else a <c>Move</c> there and the <c>AttackMove</c> once it is (BUG-0670).
/// Every own worker within <c>ai.workerFleeRadius</c> of the point gets one <c>Move</c> to the nearest own finished drop-off
/// (one outside that radius first), once a threat, and the economy leaves it alone for <c>ai.defendFor</c> after its flee
/// (BUG-0671), then gathers only at nodes away from the point while the threat holds.</item>
/// </list>
/// Reads only the player's view (own buildings' hp are own state; the enemy through <see cref="SeenUnit"/>).
/// </summary>
internal static class DefenseManager
{
    /// <summary>Notes hits, updates the threat and its point, and sends the army and the workers while it holds.</summary>
    internal static void Run(AiPlayer ai)
    {
        Blackboard board = ai.Board;
        bool hit = NoteHits(ai, out Vector2 hitAt);
        if (hit || ai.IntruderCount > 0)
        {
            bool fresh = !Holds(ai, board.DefendTick);
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
            // The threat is over: the flee marks go, so the next threat may send those workers fleeing again.
            if (board.Defending) Array.Clear(board.FledGeneration);
            board.Defending = false;
            board.DefensePointAway = false;
            return;
        }
        board.Defending = true;
        if (board.WaveActive)
        {
            MilitaryManager.End(ai);
            board.WaveCooldownUntil = ai.Now + ai.Rules.WaveCooldownTicks;
        }
        if (Army(ai)) Workers(ai);
    }

    /// <summary>Whether a trigger at <paramref name="tick"/> still holds at this think (never for -1).</summary>
    private static bool Holds(AiPlayer ai, int tick) => tick >= 0 && ai.Now - tick <= ai.Rules.DefendForTicks;

    /// <summary>
    /// Compares each own building's hp to the one kept from the last think and keeps the new one; true, with its footprint
    /// centre, for the first (slot order) within the radius of the hall that lost hp.
    /// </summary>
    private static bool NoteHits(AiPlayer ai, out Vector2 at)
    {
        Blackboard board = ai.Board;
        at = default;
        bool found = false;
        float radius = ai.Rules.DefendRadius;
        for (int k = 0; k < ai.OwnBuildingCount; k++)
        {
            int slot = ai.OwnBuildingSlots[k];
            if (!ai.View.TryGetBuilding(slot, out SeenBuilding b)) continue;
            if (!found && ai.HasHall && board.KeptHpGeneration[slot] == b.Handle.Generation && b.Hp < board.KeptHp[slot])
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
    /// Per free army unit and version: one AttackMove to the point when it is inside the base (within <c>ai.defendRadius</c>
    /// of the hall's centre), else first a Move to the point and the AttackMove once it is inside (BUG-0670: an AttackMove
    /// from the enemy base fights on there). False when the queue filled.
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
