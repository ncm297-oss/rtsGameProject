using System;
using System.Numerics;
using Rts.Sim.Entities;

namespace Rts.Sim.Ai;

/// <summary>
/// One AI's bookkeeping (M5-2, docs/03 "Implementation (M5-2)"): build-order step progress (when due, how many queued,
/// done, the command waiting to be judged), the house it is placing, retry marks (anchors a Build was refused at), and per
/// unit and building slot when its last AI command applies (so a think doesn't order it twice before the first applied) and
/// which resource a worker was sent to; since M5-3 the scout's sweep, the enemy memory and the military's rally and wave. Not
/// sim state: not hashed, never read by a sim rule; allocated once, sized from the match. Since M5-4b the defense's kept
/// building hp, point and per-unit marks; since M5-4c the casters' last casts.
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
        SiteGeneration = new int[buildingSlots];
        SiteBuiltAt = new int[buildingSlots];
        SiteStill = new byte[buildingSlots];
        SiteJoins = new byte[buildingSlots];
        SiteJoinHp = new int[buildingSlots];
        Array.Fill(_badCell, -1);
        RalliedVersion = new int[unitSlots];
        WaveGeneration = new int[unitSlots];
        WaveFlag = new byte[unitSlots];
        WaveTries = new byte[unitSlots];
        WaveBest = new float[unitSlots];
        DefendedVersion = new int[unitSlots];
        FledGeneration = new int[unitSlots];
        FledAt = new int[unitSlots];
        Homing = new bool[unitSlots];
        KeptHp = new int[buildingSlots];
        KeptHpGeneration = new int[buildingSlots];
        CastGeneration = new int[unitSlots];
        CastIndex = new byte[unitSlots];
        CastTries = new byte[unitSlots];
        CastReadyAt = new int[unitSlots];
        CastAt = new Vector2[unitSlots];
        SeenEnemies = new Vector2[unitSlots];
    }

    /// <summary>
    /// BUG-0702 item 2: the ground casts of non-stacking abilities sent and still running (first <see cref="RecentCastCount"/>,
    /// at most <c>AbilityManager.MaxRecentCasts</c>): each one's ability (its <see cref="Data.GameData.Abilities"/> index),
    /// point, and the tick its effect ends (sent + cast time + duration).
    /// </summary>
    internal readonly int[] RecentCastAbility = new int[AbilityManager.MaxRecentCasts];

    /// <summary>See <see cref="RecentCastAbility"/>.</summary>
    internal readonly Vector2[] RecentCastAt = new Vector2[AbilityManager.MaxRecentCasts];

    /// <summary>See <see cref="RecentCastAbility"/>.</summary>
    internal readonly int[] RecentCastUntil = new int[AbilityManager.MaxRecentCasts];

    /// <summary>BUG-0760: per remembered cast, its caster's unit slot (a send that never ran is forgotten; see <c>AbilityManager</c>).</summary>
    internal readonly int[] RecentCastSlot = new int[AbilityManager.MaxRecentCasts];

    /// <summary>See <see cref="RecentCastSlot"/>: the caster's generation.</summary>
    internal readonly int[] RecentCastGeneration = new int[AbilityManager.MaxRecentCasts];

    /// <summary>See <see cref="RecentCastSlot"/>: the ability's index in the caster's type's list.</summary>
    internal readonly byte[] RecentCastIndex = new byte[AbilityManager.MaxRecentCasts];

    /// <summary>See <see cref="RecentCastSlot"/>: the ability's ready tick when sent (moved later: it landed).</summary>
    internal readonly int[] RecentCastReadyAt = new int[AbilityManager.MaxRecentCasts];

    /// <summary>See <see cref="RecentCastAbility"/>.</summary>
    internal int RecentCastCount;

    // ---------- M5-4c: abilities (AbilityManager) ----------

    /// <summary>Per unit slot, the generation of the caster the last <c>UseAbility</c> went to (0: none); the next four are about that cast.</summary>
    internal int[] CastGeneration { get; }

    /// <summary>Per unit slot, the ability (index in the caster's type's list) of the last cast sent.</summary>
    internal byte[] CastIndex { get; }

    /// <summary>Per unit slot, casts sent at about <see cref="CastAt"/> (within a cell) with no cooldown moving since (bounded by <c>AbilityManager.MaxTries</c>).</summary>
    internal byte[] CastTries { get; }

    /// <summary>Per unit slot, the ability's ready tick when the last cast was sent: a later one means the cast landed.</summary>
    internal int[] CastReadyAt { get; }

    /// <summary>Per unit slot, the point (m) the last cast was sent at.</summary>
    internal Vector2[] CastAt { get; }

    /// <summary>Scratch, filled once a think when a caster is free: the positions (m) of the enemy units seen (first <see cref="SeenEnemyCount"/>), slot order.</summary>
    internal Vector2[] SeenEnemies { get; }

    /// <summary>See <see cref="SeenEnemies"/>.</summary>
    internal int SeenEnemyCount;

    /// <summary>Casts sent (tests and tooling).</summary>
    internal int CastsSent;

    // ---------- M5-4b: defense (DefenseManager) ----------

    /// <summary>Per building slot, the own building's hp at the last think (with <see cref="KeptHpGeneration"/>, the building it is for): a drop since is a hit.</summary>
    internal int[] KeptHp { get; }

    /// <summary>Per building slot, the generation of the own building <see cref="KeptHp"/> is about (0: none; a building seen for the first time doesn't trigger).</summary>
    internal int[] KeptHpGeneration { get; }

    /// <summary>The think of the last defense trigger (-1: never). The threat holds through <c>ai.defendFor</c> after it.</summary>
    internal int DefendTick = -1;

    /// <summary>Whether the threat held at the last think (the military starts no wave and rallies nobody meanwhile).</summary>
    internal bool Defending;

    /// <summary>Where the army defends (m), and its version, bumped when the point moves more than <c>ai.rallyReach</c> or a new threat starts.</summary>
    internal Vector2 DefensePoint;

    /// <summary>See <see cref="DefensePoint"/>.</summary>
    internal int DefenseVersion;

    /// <summary>BUG-0673: whether the last trigger's point was more than <c>ai.rallyReach</c> from <see cref="DefensePoint"/>: a second such trigger in a row moves the point.</summary>
    internal bool DefensePointAway;

    /// <summary>Threats met (a trigger with none holding; tests and tooling).</summary>
    internal int Defenses;

    /// <summary>Per unit slot, the defense version the army unit (of <see cref="UnitGeneration"/>) was last sent to: it isn't sent again to the same point.</summary>
    internal int[] DefendedVersion { get; }

    /// <summary>Per unit slot, the generation of the worker sent fleeing during this threat (0: none): it isn't sent fleeing again until the threat ends.</summary>
    internal int[] FledGeneration { get; }

    /// <summary>Per unit slot, the think the worker of <see cref="FledGeneration"/> fled at: the economy leaves it alone through <c>ai.defendFor</c> after it (BUG-0671).</summary>
    internal int[] FledAt { get; }

    /// <summary>Per unit slot, whether the army unit's order for <see cref="DefendedVersion"/> was the <c>Move</c> home (BUG-0670): it gets the <c>AttackMove</c> once inside the base.</summary>
    internal bool[] Homing { get; }

    // ---------- M5-H1: own construction sites (EconomyManager, BUG-0640) ----------

    /// <summary>Per building slot, the own site's generation <see cref="SiteStill"/> counts for (0: none tracked).</summary>
    internal int[] SiteGeneration { get; }

    /// <summary>Per building slot, the last think an own worker had the building as its build target (scratch, stamped each think).</summary>
    internal int[] SiteBuiltAt { get; }

    /// <summary>Per building slot, thinks in a row the own site had no builder (no worker walking to it or working on it), capped.</summary>
    internal byte[] SiteStill { get; }

    /// <summary>BUG-0650: per building slot, workers sent to join the own site since its hit points last rose (see <c>EconomyManager.MaxJoins</c>).</summary>
    internal byte[] SiteJoins { get; }

    /// <summary>BUG-0650: per building slot, the own site's hit points when the last worker was sent to join it.</summary>
    internal int[] SiteJoinHp { get; }

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

    /// <summary>The last tick a member was ordered, busy (not Idle) or had an order pending (BUG-0598: a wave idle for <c>ai.waveCooldown</c> ends).</summary>
    internal int WaveBusyTick;

    /// <summary>No wave starts before this tick (set at a wave's start and at its retreat).</summary>
    internal int WaveCooldownUntil;

    /// <summary>Waves started (tests and tooling).</summary>
    internal int WavesStarted;

    /// <summary>Per unit slot, the generation of the wave member there (0: none).</summary>
    internal int[] WaveGeneration { get; }

    /// <summary>Per unit slot, the member's next order: 0 none, <c>MilitaryManager.ToAttack</c> an attack-move owed, <c>ToRally</c> a Move home owed (it may outlive the wave).</summary>
    internal byte[] WaveFlag { get; }

    /// <summary>Per unit slot, the member's orders toward this target since it last got a cell nearer it (BUG-0595/0596; bounded by <c>MilitaryManager.MaxTries</c>).</summary>
    internal byte[] WaveTries { get; }

    /// <summary>Per unit slot, the member's nearest distance (m) to the target's centre when ordered toward it: getting a cell nearer is progress and clears <see cref="WaveTries"/>.</summary>
    internal float[] WaveBest { get; }

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
