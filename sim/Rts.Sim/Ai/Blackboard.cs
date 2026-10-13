using System;
using System.Numerics;
using Rts.Sim.Entities;

namespace Rts.Sim.Ai;

/// <summary>
/// One AI's bookkeeping (M5-2, docs/03 "Implementation (M5-2)"): build-order step progress (when due, how many queued,
/// done, the command waiting to be judged), the house it is placing, retry marks (anchors a Build was refused at), and per
/// unit and building slot when its last AI command applies (so a think doesn't order it twice before the first applied) and
/// which resource a worker was sent to; since M5-3 the scout's sweep, the enemy memory and the military's rally and wave. Not
/// sim state: not hashed, never read by a sim rule; allocated once, sized from the match.
/// </summary>
internal sealed class Blackboard
{
    /// <summary>Retry marks kept: the oldest is forgotten past this many.</summary>
    internal const int RetryMarks = 16;

    private readonly int[] _badType = new int[RetryMarks];
    private readonly int[] _badCell = new int[RetryMarks];
    private int _badNext;

    /// <summary>Sizes the bookkeeping for <paramref name="steps"/> build-order steps and the match's unit and building slots.</summary>
    internal Blackboard(int steps, int unitSlots, int buildingSlots, int resourceSlots, int scoutGridPoints)
    {
        ScoutPoints = new Vector2[ScoutHead + scoutGridPoints];
        ScoutVisited = new bool[ScoutPoints.Length];
        StepDueTick = new int[steps];
        Array.Fill(StepDueTick, -1);
        StepQueued = new int[steps];
        StepDone = new bool[steps];
        StepPending = new PendingOrder[steps];
        UnitOrderedUntil = new int[unitSlots];
        Array.Fill(UnitOrderedUntil, -1);
        UnitGeneration = new int[unitSlots];
        GatherKind = new byte[unitSlots];
        GatherNode = new EntityHandle[unitSlots];
        NodeDropped = new int[resourceSlots];
        BuildingBusyUntil = new int[buildingSlots];
        Array.Fill(BuildingBusyUntil, -1);
        Array.Fill(_badCell, -1);
        RalliedVersion = new int[unitSlots];
        WaveGeneration = new int[unitSlots];
        WaveFlag = new byte[unitSlots];
    }

    // ---------- M5-3: the scout (ScoutManager) ----------

    /// <summary>The sweep's fixed-order head: the 8 compass points round the hall, then the hall's mirror through the map centre. The map grid's points follow (BUG-0593).</summary>
    internal const int ScoutHead = 9;

    /// <summary>0 not sent yet, 1 sweeping, 2 done (the sweep ended, or the scout died: it is not replaced).</summary>
    internal byte ScoutPhase;

    /// <summary>The scout's unit slot and generation while <see cref="ScoutPhase"/> is 1.</summary>
    internal int ScoutSlot = -1, ScoutGeneration;

    /// <summary>The waypoint the scout was last sent to (-1: none yet).</summary>
    internal int ScoutTarget = -1;

    /// <summary>The waypoints (m), computed once when the scout is sent: <see cref="ScoutHead"/> points, then the map grid's (sized at construction from the map and <c>ai.scoutGrid</c>).</summary>
    internal Vector2[] ScoutPoints { get; }

    /// <summary>Per waypoint, whether it is visited.</summary>
    internal bool[] ScoutVisited { get; }

    // ---------- M5-3: the enemy memory (from the view only, rebuilt each think) ----------

    /// <summary>How many enemy buildings the AI knows this think: those it sees plus its ghosts (the fog's memory, no copy).</summary>
    internal int KnownEnemyBuildings;

    /// <summary>The last enemy army seen: the centroid (m) and count of the enemy non-worker units seen at <see cref="EnemyArmyTick"/> (-1: never), kept until a think sees some again.</summary>
    internal Vector2 EnemyArmyCentroid;

    /// <summary>See <see cref="EnemyArmyCentroid"/>.</summary>
    internal int EnemyArmyCount, EnemyArmyTick = -1;

    // ---------- M5-3: the military (MilitaryManager) ----------

    /// <summary>The rally point (m) and its version, bumped whenever the point moves.</summary>
    internal Vector2 Rally;

    /// <summary>See <see cref="Rally"/>.</summary>
    internal int RallyVersion;

    /// <summary>Per unit slot, the rally version the unit (of <see cref="UnitGeneration"/>) was last sent to: it isn't sent again to the same point.</summary>
    internal int[] RalliedVersion { get; }

    /// <summary>Whether a wave is in progress.</summary>
    internal bool WaveActive;

    /// <summary>The wave's target building (slot and generation), its type and anchor as known, and its footprint centre (m).</summary>
    internal EntityHandle WaveTarget;

    /// <summary>See <see cref="WaveTarget"/>.</summary>
    internal int WaveTargetType, WaveTargetCell;

    /// <summary>See <see cref="WaveTarget"/>.</summary>
    internal Vector2 WaveTargetPoint;

    /// <summary>The wave's half-pop and tick at its start.</summary>
    internal int WaveStartHalfPop, WaveStartTick;

    /// <summary>No wave starts before this tick (set at a wave's start and at its retreat).</summary>
    internal int WaveCooldownUntil;

    /// <summary>Waves started (tests and tooling).</summary>
    internal int WavesStarted;

    /// <summary>Per unit slot, the generation of the wave member there (0: none).</summary>
    internal int[] WaveGeneration { get; }

    /// <summary>Per unit slot, the member's next order: 0 none, <c>MilitaryManager.ToAttack</c> an attack-move owed, <c>ToRally</c> a Move home owed (it may outlive the wave), <c>AttackSent</c> its one Attack on this target sent (BUG-0591).</summary>
    internal byte[] WaveFlag { get; }

    /// <summary>Per step, the tick it became due (its trigger first reached); -1 before.</summary>
    internal int[] StepDueTick { get; }

    /// <summary>Per step, the commands it had accepted (a train step's count so far).</summary>
    internal int[] StepQueued { get; }

    /// <summary>Per step, whether it is complete.</summary>
    internal bool[] StepDone { get; }

    /// <summary>Per step, its command waiting to be judged.</summary>
    internal PendingOrder[] StepPending { get; }

    /// <summary>The economy manager's house Build waiting to be judged.</summary>
    internal PendingOrder HousePending;

    /// <summary>The production manager's last Train (not judged: its building is busy until it applies, then the queue shows it).</summary>
    internal PendingOrder ProductionPending;

    /// <summary>Per unit slot, the tick the AI's last command to it applies (-1: none); with <see cref="UnitGeneration"/>, the unit it was for.</summary>
    internal int[] UnitOrderedUntil { get; }

    /// <summary>Per unit slot, the generation of the unit <see cref="UnitOrderedUntil"/> and <see cref="GatherKind"/> are about.</summary>
    internal int[] UnitGeneration { get; }

    /// <summary>Per unit slot, the resource the AI last sent the worker to: 0 none, 1 gold, 2 wood.</summary>
    internal byte[] GatherKind { get; }

    /// <summary>Per unit slot, the node the AI last sent the worker to gather at (default: none).</summary>
    internal EntityHandle[] GatherNode { get; }

    /// <summary>
    /// Per resource slot, the generation of the node there a Gather was dropped at (0: none; BUG-0570): the worker was still
    /// Idle after the order applied and the player doesn't see the node, so it is gone; the economy manager sends no more
    /// workers there. What a human sees too: the worker didn't move.
    /// </summary>
    internal int[] NodeDropped { get; }

    /// <summary>How many <see cref="NodeDropped"/> marks are set (M5-3, BUG-0574: the clear-on-seen pass runs only while some are).</summary>
    internal int NodeMarks;

    /// <summary>Per building slot, the tick the AI's last command to it applies (-1: none): until then its queue doesn't show it.</summary>
    internal int[] BuildingBusyUntil { get; }

    /// <summary>Remembers that a Build of <paramref name="type"/> at <paramref name="cell"/> was refused, so the site search skips it.</summary>
    internal void MarkBad(int type, int cell)
    {
        _badType[_badNext] = type;
        _badCell[_badNext] = cell;
        _badNext = (_badNext + 1) % RetryMarks;
    }

    /// <summary>Whether a Build of <paramref name="type"/> at <paramref name="cell"/> carries a retry mark.</summary>
    internal bool IsBad(int type, int cell)
    {
        for (int k = 0; k < RetryMarks; k++)
            if (_badCell[k] == cell && _badType[k] == type) return true;
        return false;
    }
}
