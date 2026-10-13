using System;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.Ai;

/// <summary>
/// The AI's army (M5-3, docs/03 "Implementation (M5-3)"), run last in a think. The army is the player's live non-worker units.
/// <list type="bullet">
/// <item><b>Rally.</b> The point <c>ai.rallyDistance</c> from the hall's centre toward the known enemy building nearest the
/// hall (the map centre with none known). An idle army unit not in a wave, farther than <c>ai.rallyReach</c> from it, gets one
/// Move there, never again to the same point (it got as close as it could), nor while a command to it is pending.</item>
/// <item><b>Wave start.</b> No wave in progress, the cooldown over, and the army free to go (not casting, no command pending)
/// at least <c>attack.minArmyPop</c> half-pop and <c>attack.minArmyCost</c> gold + wood, each times the difficulty's
/// <c>waveSizeMultiplier</c>, with an enemy building known: every such unit becomes a member and is sent
/// <c>AttackMove</c> to the known enemy building nearest the rally point. The cooldown (<c>ai.waveCooldown</c>) starts.</item>
/// <item><b>In a wave.</b> Dead members drop out. A member Idle near the target while it is seen gets <c>Attack</c> on it,
/// once per target: Idle there again, it can't fight it, and goes home out of the wave (BUG-0591). Idle elsewhere,
/// <c>AttackMove</c> again. A target no longer known (not seen live, no ghost of it) is replaced by the known
/// enemy building nearest the members (all sent again), or with none the wave ends and the members go home.</item>
/// <item><b>Retreat.</b> Live members' half-pop below <c>(1 - retreat.lossFraction)</c> of the start's: every live member
/// Moves to the rally point, the wave ends, and the cooldown starts again.</item>
/// </list>
/// One command per unit per think at most; a full queue ends the think and the rest go next think (the per-slot flags keep
/// what is owed).
/// </summary>
internal static class MilitaryManager
{
    /// <summary><see cref="Blackboard.WaveFlag"/>: an AttackMove to the wave's target is owed.</summary>
    internal const byte ToAttack = 1;

    /// <summary><see cref="Blackboard.WaveFlag"/>: a Move to the rally point is owed (a retreat or a wave's end).</summary>
    internal const byte ToRally = 2;

    /// <summary>
    /// <see cref="Blackboard.WaveFlag"/>: the member was sent <c>Attack</c> on this target (BUG-0591). Idle by the seen target
    /// again, it can't fight it (its attack doesn't hit buildings, it can't reach it): it is sent home and leaves the wave.
    /// A new target clears it, so a member gets one <c>Attack</c> per target.
    /// </summary>
    internal const byte AttackSent = 3;

    /// <summary>Rally, wave and retreat (see the type's summary).</summary>
    internal static void Run(AiPlayer ai)
    {
        if (ai.TryHallPoint(out Vector2 hall)) RallyPoint(ai, hall);
        if (ai.Board.WaveActive) Wave(ai);
        else TryStart(ai);
        Orders(ai);
    }

    private static void RallyPoint(AiPlayer ai, Vector2 hall)
    {
        Blackboard board = ai.Board;
        Vector2 toward = ai.NearestEnemyBuilding(hall, out _, out int type, out int cell) ? ai.BuildingCentre(type, cell) : ai.MapSize * 0.5f;
        Vector2 d = toward - hall;
        float length = d.Length();
        Vector2 p = ai.ClampToMap(length > 0.001f ? hall + d * (ai.Rules.RallyDistance / length) : hall);
        if (p == board.Rally && board.RallyVersion > 0) return;
        board.Rally = p;
        board.RallyVersion++; // units already rallied go again to the new point
    }

    private static void TryStart(AiPlayer ai)
    {
        Blackboard board = ai.Board;
        if (ai.Now < board.WaveCooldownUntil) return;
        int half = 0, cost = 0, count = 0;
        for (int k = 0; k < ai.ArmyCount; k++)
        {
            if (!Free(ai, ai.ArmySlots[k], out SeenUnit u)) continue;
            UnitDef def = ai.Data.Units[u.TypeId];
            half += def.HalfPop;
            cost += def.CostGold + def.CostWood;
            count++;
        }
        float scale = ai.Knobs.WaveSizeMultiplier;
        if (count == 0 || half < ai.Def.AttackMinHalfPop * scale || cost < ai.Def.AttackMinCost * scale) return;
        if (!ai.NearestEnemyBuilding(board.Rally, out EntityHandle target, out int type, out int cell)) return;
        Array.Clear(board.WaveGeneration);
        Array.Clear(board.WaveFlag);
        for (int k = 0; k < ai.ArmyCount; k++)
        {
            int slot = ai.ArmySlots[k];
            if (!Free(ai, slot, out SeenUnit u)) continue;
            board.WaveGeneration[slot] = u.Handle.Generation;
            board.WaveFlag[slot] = ToAttack;
        }
        SetTarget(ai, target, type, cell);
        board.WaveActive = true;
        board.WaveStartHalfPop = half;
        board.WaveStartTick = ai.Now;
        board.WaveCooldownUntil = ai.Now + ai.Rules.WaveCooldownTicks;
        board.WavesStarted++;
    }

    private static void Wave(AiPlayer ai)
    {
        Blackboard board = ai.Board;
        int live = 0, half = 0;
        Vector2 sum = Vector2.Zero;
        for (int k = 0; k < ai.ArmyCount; k++)
        {
            int slot = ai.ArmySlots[k];
            if (!Member(ai, slot, out SeenUnit u)) continue;
            live++;
            half += ai.Data.Units[u.TypeId].HalfPop;
            sum += u.Position;
        }
        if (live == 0 || half < (1f - ai.Def.RetreatLossFraction) * board.WaveStartHalfPop)
        {
            End(ai);
            board.WaveCooldownUntil = ai.Now + ai.Rules.WaveCooldownTicks; // retreat: no new wave for a while
            return;
        }
        if (ai.EnemyBuildingKnown(board.WaveTarget, out _)) return;
        // The target is gone (razed, or seen gone): the nearest known enemy building to the members, or home.
        if (!ai.NearestEnemyBuilding(sum / live, out EntityHandle target, out int type, out int cell))
        {
            End(ai);
            return;
        }
        SetTarget(ai, target, type, cell);
        for (int k = 0; k < ai.ArmyCount; k++)
            if (Member(ai, ai.ArmySlots[k], out _)) board.WaveFlag[ai.ArmySlots[k]] = ToAttack;
    }

    /// <summary>Ends the wave: every live member owes a Move to the rally point.</summary>
    private static void End(AiPlayer ai)
    {
        Blackboard board = ai.Board;
        for (int k = 0; k < ai.ArmyCount; k++)
            if (Member(ai, ai.ArmySlots[k], out _)) board.WaveFlag[ai.ArmySlots[k]] = ToRally;
        board.WaveActive = false;
    }

    private static void SetTarget(AiPlayer ai, EntityHandle target, int type, int cell)
    {
        Blackboard board = ai.Board;
        board.WaveTarget = target;
        board.WaveTargetType = type;
        board.WaveTargetCell = cell;
        board.WaveTargetPoint = ai.BuildingCentre(type, cell);
    }

    /// <summary>Every army unit's command this think, in slot order (one each at most).</summary>
    private static void Orders(AiPlayer ai)
    {
        Blackboard board = ai.Board;
        float near = 0f;
        if (board.WaveActive)
        {
            // Near the target: within rallyReach of its footprint's edge, roughly (a unit can't stand at the centre).
            BuildingDef target = ai.Data.Buildings[board.WaveTargetType];
            near = ai.Rules.RallyReach + Math.Max(target.FootprintWidth, target.FootprintHeight) * Map.MapConstants.CellSize * 0.5f;
        }
        bool targetSeen = board.WaveActive && ai.EnemyBuildingKnown(board.WaveTarget, out bool seen) && seen;
        float rallyReach = ai.Rules.RallyReach * ai.Rules.RallyReach;
        for (int k = 0; k < ai.ArmyCount; k++)
        {
            int slot = ai.ArmySlots[k];
            if (!Free(ai, slot, out SeenUnit u)) continue;
            int generation = u.Handle.Generation;
            bool tagged = board.WaveGeneration[slot] == generation;
            Command c;
            if (tagged && board.WaveFlag[slot] == ToRally)
                c = Command.Move(ai.Player, u.Handle, board.Rally);
            else if (tagged && board.WaveActive && board.WaveFlag[slot] == ToAttack)
                c = Command.AttackMove(ai.Player, u.Handle, board.WaveTargetPoint);
            else if (tagged && board.WaveActive)
            {
                if (u.State != UnitState.Idle) continue;
                bool atTarget = targetSeen && Vector2.Distance(u.Position, board.WaveTargetPoint) <= near;
                if (atTarget && board.WaveFlag[slot] == AttackSent) c = Command.Move(ai.Player, u.Handle, board.Rally); // gave up
                else if (atTarget) c = Command.Attack(ai.Player, u.Handle, board.WaveTarget, isBuilding: true);
                else c = Command.AttackMove(ai.Player, u.Handle, board.WaveTargetPoint);
            }
            else
            {
                // Not in a wave: an idle unit away from the rally point, not yet sent to this point.
                bool rallied = board.UnitGeneration[slot] == generation && board.RalliedVersion[slot] == board.RallyVersion;
                if (board.RallyVersion == 0 || u.State != UnitState.Idle || rallied || Vector2.DistanceSquared(u.Position, board.Rally) <= rallyReach) continue;
                c = Command.Move(ai.Player, u.Handle, board.Rally);
            }
            if (!ai.Send(c)) return; // queue full: the flags keep the rest for the next think
            ai.MarkOrdered(slot, generation, 0);
            bool home = c.Kind == CommandKind.Move;
            board.RalliedVersion[slot] = home ? board.RallyVersion : 0;
            if (tagged) board.WaveFlag[slot] = c.Kind == CommandKind.Attack ? AttackSent : (byte)0;
            if (tagged && home) board.WaveGeneration[slot] = 0; // home: no longer a member
        }
    }

    /// <summary>Own army unit <paramref name="slot"/> as seen, if it is free for an order: alive, not casting, no AI command pending.</summary>
    private static bool Free(AiPlayer ai, int slot, out SeenUnit u) =>
        ai.View.TryGetUnit(slot, out u) && u.State != UnitState.Casting && !ai.OrderedNotApplied(slot, u.Handle.Generation);

    /// <summary>Whether own unit slot <paramref name="slot"/> holds a live member of the wave in progress.</summary>
    private static bool Member(AiPlayer ai, int slot, out SeenUnit u) =>
        ai.View.TryGetUnit(slot, out u) && ai.Board.WaveGeneration[slot] == u.Handle.Generation && ai.Board.WaveFlag[slot] != ToRally;
}
