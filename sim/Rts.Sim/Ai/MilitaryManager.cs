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
/// <item><b>In a wave.</b> Dead members drop out. A member Idle near the target while it is seen gets <c>Attack</c> on it
/// (one whose attack hits units only can't fight it: it goes home out of the wave, BUG-0591); Idle elsewhere, <c>AttackMove</c>
/// again. Orders are bounded (BUG-0595/0596): after <see cref="MaxTries"/> orders toward this target without getting a cell
/// nearer it, a member by the target (crowded off it) is left standing there, still a member; one elsewhere (it can't get
/// there) goes home out of the wave. A member that leaves alive is not a loss: its half-pop leaves the start's too. A target
/// no longer known (not seen live, no ghost of it) is replaced by the known enemy building nearest the members (all sent
/// again), or with none the wave ends and the members go home.</item>
/// <item><b>Retreat.</b> Live members' half-pop below <c>(1 - retreat.lossFraction)</c> of the start's: every live member
/// Moves to the rally point, the wave ends, and the cooldown starts again. The same when no member has been busy (not Idle),
/// had an order pending or been ordered for more than <c>ai.waveCooldown</c> (BUG-0598: all crowded by a target none strikes).</item>
/// </list>
/// One command per unit per think at most; a full queue ends the think and the rest go next think (the per-slot flags keep
/// what is owed). M5-4b: while the base is defended (<see cref="DefenseManager"/>) no wave starts and nobody is rallied;
/// M5-4f: a wave in progress goes on (the defense may pull members out of it, or end it), and its start threshold is kept.
/// <para>
/// M5-4d, the <b>hunt</b> (docs/03 "Implementation (M5-4d)"): with no enemy building known, the match not over, the base not
/// defended and a wave sent before, the free army goes whatever its size (a wave with no threshold, <see cref="Blackboard.Hunting"/>),
/// and a wave whose last known target is gone turns into one. Its target is a point: the last enemy unit seen
/// (<see cref="Blackboard.EnemyUnitPoint"/>) while that sighting is at most <c>ai.huntMemory</c> old, else a sweep of the
/// scout's map grid (<see cref="HuntPoint"/>: its cell centres and its interior corners, the hunt's own marks), the point
/// nearest the army first, then the unvisited one with the least distance from the last plus distance from where the sweep
/// began (<see cref="NextSweep"/>). A point is reached when a member comes within <c>ai.rallyReach</c> of it, or every
/// member stands Idle having had <see cref="MaxTries"/> orders toward it without getting nearer (as close as they get):
/// a sweep point is then visited, a sighting not seen again since is forgotten. A fresher sighting (more than
/// <c>ai.rallyReach</c> from the target) re-targets at once. Members are sent <c>AttackMove</c> only. It ends as a wave does
/// (the retreat, the idle cooldown, the defense); an enemy building coming into view makes it a wave on that building.
/// </para>
/// </summary>
internal static class MilitaryManager
{
    /// <summary><see cref="Blackboard.WaveFlag"/>: an AttackMove to the wave's target is owed.</summary>
    internal const byte ToAttack = 1;

    /// <summary><see cref="Blackboard.WaveFlag"/>: a Move to the rally point is owed (a retreat or a wave's end).</summary>
    internal const byte ToRally = 2;

    /// <summary>
    /// Orders (<c>AttackMove</c> or <c>Attack</c>) a member gets toward one target without getting a cell nearer it before the
    /// AI stops ordering it (BUG-0595/0596). Its nearest distance only falls, by a cell each reset, so a member's orders per
    /// target are bounded. Not a balance number: a guard against re-sending an order that does nothing.
    /// </summary>
    internal const int MaxTries = 3;

    /// <summary>Rally, wave and retreat (see the type's summary).</summary>
    internal static void Run(AiPlayer ai)
    {
        if (ai.TryHallPoint(out Vector2 hall)) RallyPoint(ai, hall);
        if (ai.Board.WaveActive) Wave(ai);
        else if (!ai.Board.Defending && !TryStart(ai)) TryHunt(ai); // M5-4b: no wave while the base is defended
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

    /// <summary>Starts a wave if it may (see the type's summary); true when it did.</summary>
    private static bool TryStart(AiPlayer ai)
    {
        Blackboard board = ai.Board;
        if (ai.Now < board.WaveCooldownUntil) return false;
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
        if (count == 0 || half < ai.Opening.AttackMinHalfPop * scale || cost < ai.Opening.AttackMinCost * scale) return false;
        if (!ai.NearestEnemyBuilding(board.Rally, out EntityHandle target, out int type, out int cell)) return false;
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
        board.WaveMinHalfPop = ai.Opening.AttackMinHalfPop * scale;
        board.WaveMinCost = ai.Opening.AttackMinCost * scale;
        board.WaveStartTick = ai.Now;
        board.WaveBusyTick = ai.Now;
        board.WaveCooldownUntil = ai.Now + ai.Rules.WaveCooldownTicks;
        board.WavesStarted++;
        return true;
    }

    /// <summary>
    /// M5-4d: starts a hunt (see the type's summary) when no enemy building is known, a wave was sent before, the match is not
    /// over and the cooldown is over (the caller checked the defense): every free army unit becomes a member, whatever their
    /// number. The cooldown starts, as a wave's.
    /// </summary>
    private static void TryHunt(AiPlayer ai)
    {
        Blackboard board = ai.Board;
        if (board.KnownEnemyBuildings > 0 || board.WavesStarted == 0 || ai.View.MatchOver || ai.Now < board.WaveCooldownUntil) return;
        int half = 0, count = 0;
        Vector2 sum = Vector2.Zero;
        for (int k = 0; k < ai.ArmyCount; k++)
        {
            if (!Free(ai, ai.ArmySlots[k], out SeenUnit u)) continue;
            half += ai.Data.Units[u.TypeId].HalfPop;
            sum += u.Position;
            count++;
        }
        if (count == 0) return;
        Array.Clear(board.WaveGeneration);
        Array.Clear(board.WaveFlag);
        for (int k = 0; k < ai.ArmyCount; k++)
        {
            int slot = ai.ArmySlots[k];
            if (!Free(ai, slot, out SeenUnit u)) continue;
            board.WaveGeneration[slot] = u.Handle.Generation;
            board.WaveFlag[slot] = ToAttack;
        }
        board.WaveActive = true;
        board.WaveStartHalfPop = half;
        board.WaveStartTick = ai.Now;
        board.WaveBusyTick = ai.Now;
        board.WaveCooldownUntil = ai.Now + ai.Rules.WaveCooldownTicks;
        BeginHunt(ai, sum / count);
    }

    /// <summary>
    /// M5-4d: makes the wave in progress a hunt from the army at <paramref name="army"/>: no building target, no start
    /// threshold (the defense may pull any member), a fresh sweep, and the first target (every member owed an AttackMove).
    /// </summary>
    private static void BeginHunt(AiPlayer ai, Vector2 army)
    {
        Blackboard board = ai.Board;
        board.Hunting = true;
        board.WaveTarget = default;
        board.WaveTargetType = board.WaveTargetCell = -1;
        board.WaveMinHalfPop = board.WaveMinCost = 0f;
        Array.Clear(board.HuntVisited);
        board.HuntsStarted++;
        if (Fresh(ai)) HuntTarget(ai, board.EnemyUnitPoint, -1);
        else StartSweep(ai, army);
    }

    /// <summary>M5-4d: the sweep (re)starts from the army at <paramref name="army"/>: its origin, and the point nearest it.</summary>
    private static void StartSweep(AiPlayer ai, Vector2 army)
    {
        ai.Board.HuntOrigin = army;
        HuntTarget(ai, army, NextSweep(ai, army, -1));
    }

    /// <summary>M5-4d: whether the last enemy unit seen is a sighting the hunt follows: at most <c>ai.huntMemory</c> old.</summary>
    private static bool Fresh(AiPlayer ai) =>
        ai.Board.EnemyUnitTick >= 0 && ai.Now - ai.Board.EnemyUnitTick <= ai.Rules.HuntMemoryTicks;

    /// <summary>
    /// M5-4d: the hunt's target becomes <paramref name="sweep"/>'s grid point, or with -1 <paramref name="point"/> (a
    /// sighting), clamped inside the map; every member is owed an AttackMove to it.
    /// </summary>
    private static void HuntTarget(AiPlayer ai, Vector2 point, int sweep)
    {
        Blackboard board = ai.Board;
        board.HuntSweep = sweep;
        board.WaveTargetPoint = sweep >= 0 ? HuntPoint(ai, Cols(ai), Rows(ai), sweep) : ai.ClampToMap(point);
        for (int k = 0; k < ai.ArmyCount; k++)
            if (Member(ai, ai.ArmySlots[k], out _)) board.WaveFlag[ai.ArmySlots[k]] = ToAttack;
    }

    /// <summary>
    /// M5-4d: the next sweep point: the unvisited one with the least distance from <paramref name="from"/> (the last point,
    /// or the army) plus distance from <see cref="Blackboard.HuntOrigin"/>, never <paramref name="skip"/> (list order on ties);
    /// once all are visited the marks clear and the sweep starts over. Why the origin term: nearest-first alone walked
    /// straight away from where the enemy was last (Pale Hills seed 1: a last building 50 m from the razed hall, the sweep
    /// heading for the far edge on a tie); with it the sweep spreads outward from there, still walking point to point.
    /// One pass over the grid points (two at a restart).
    /// </summary>
    private static int NextSweep(AiPlayer ai, Vector2 from, int skip)
    {
        bool[] visited = ai.Board.HuntVisited;
        int best = NearestUnvisited(ai, visited, from, skip);
        if (best >= 0) return best;
        Array.Clear(visited);
        best = NearestUnvisited(ai, visited, from, skip);
        return best >= 0 ? best : 0; // a one-point grid: that point again
    }

    private static int NearestUnvisited(AiPlayer ai, bool[] visited, Vector2 from, int skip)
    {
        int best = -1, cols = Cols(ai), rows = Rows(ai);
        float bestDistance = float.MaxValue;
        Vector2 origin = ai.Board.HuntOrigin;
        for (int i = 0; i < visited.Length; i++)
        {
            if (visited[i] || i == skip) continue;
            Vector2 p = HuntPoint(ai, cols, rows, i);
            float d = Vector2.Distance(from, p) + Vector2.Distance(origin, p);
            if (d >= bestDistance) continue;
            bestDistance = d;
            best = i;
        }
        return best;
    }

    /// <summary>
    /// M5-4d: how many sweep points a <paramref name="cols"/> x <paramref name="rows"/> scout grid gives the hunt: its cell
    /// centres, then its interior corners. Why the corners too: the centres alone are a cell apart (51 m on Pale Hills at the
    /// shipped <c>ai.scoutGrid</c>), past a unit's sight (14 m), and a side's last units standing 29-35 m from the nearest
    /// centre were missed for 40,000 ticks (seed 5). Centres alone leave ground half a cell's diagonal from any point (36 m
    /// there); with the corners, at most half a cell (25.6 m).
    /// </summary>
    internal static int HuntPoints(int cols, int rows) => cols * rows + (cols - 1) * (rows - 1);

    private static int Cols(AiPlayer ai) => ScoutManager.GridLines(ai.View.MapWidth, ai.Rules.ScoutGrid);

    private static int Rows(AiPlayer ai) => ScoutManager.GridLines(ai.View.MapHeight, ai.Rules.ScoutGrid);

    /// <summary>
    /// M5-4d: sweep point <paramref name="k"/> of a <paramref name="cols"/> x <paramref name="rows"/> grid, clamped inside the
    /// map: below <c>cols x rows</c> the scout's grid point (<see cref="ScoutManager.GridPoint"/>'s cell centre, row order),
    /// past it the grid's interior corners in row order.
    /// </summary>
    internal static Vector2 HuntPoint(AiPlayer ai, int cols, int rows, int k)
    {
        int centres = cols * rows;
        if (k < centres) return ScoutManager.GridPoint(ai, Blackboard.ScoutHead + k);
        Vector2 size = ai.MapSize;
        int j = k - centres, x = j % (cols - 1), y = j / (cols - 1);
        return ai.ClampToMap(new Vector2((x + 1) * size.X / cols, (y + 1) * size.Y / rows));
    }

    /// <summary>
    /// M5-4d: a think of the hunt (the retreat and idle rules ran): an enemy building known makes it a wave on the one nearest
    /// <paramref name="army"/>; else the target is judged reached (see the type's summary) and moved: to a fresher sighting,
    /// or along the sweep.
    /// </summary>
    private static void Hunt(AiPlayer ai, Vector2 army)
    {
        Blackboard board = ai.Board;
        if (ai.View.MatchOver)
        {
            End(ai);
            return;
        }
        if (board.KnownEnemyBuildings > 0 && ai.NearestEnemyBuilding(army, out EntityHandle target, out int type, out int cell))
        {
            // Back to the wave rule: the hunt's members attack the building (no start threshold to keep: the defense may still pull any).
            board.Hunting = false;
            board.HuntSweep = -1;
            SetTarget(ai, target, type, cell);
            for (int k = 0; k < ai.ArmyCount; k++)
                if (Member(ai, ai.ArmySlots[k], out _)) board.WaveFlag[ai.ArmySlots[k]] = ToAttack;
            return;
        }
        float reach = ai.Rules.RallyReach * ai.Rules.RallyReach;
        bool reached = false, stuck = true;
        for (int k = 0; k < ai.ArmyCount; k++)
        {
            int slot = ai.ArmySlots[k];
            if (!Member(ai, slot, out SeenUnit u)) continue;
            if (Vector2.DistanceSquared(u.Position, board.WaveTargetPoint) <= reach) reached = true;
            // Not stuck: walking or fighting, an order pending or owed, or orders toward the point left to give.
            if (u.State != UnitState.Idle || ai.OrderedNotApplied(slot, u.Handle.Generation) || board.WaveFlag[slot] == ToAttack
                || board.WaveTries[slot] < MaxTries) stuck = false;
        }
        bool next = false;
        if (reached || stuck)
        {
            if (board.HuntSweep >= 0)
            {
                board.HuntVisited[board.HuntSweep] = true;
                next = true;
            }
            else if (board.EnemyUnitTick < ai.Now)
            {
                board.EnemyUnitTick = -1; // went there and nobody is seen: forgotten
                next = true;
            }
        }
        if (Fresh(ai))
        {
            if (board.HuntSweep >= 0 || Vector2.DistanceSquared(board.EnemyUnitPoint, board.WaveTargetPoint) > reach)
                HuntTarget(ai, board.EnemyUnitPoint, -1);
            return;
        }
        if (board.HuntSweep < 0) StartSweep(ai, army); // a sighting forgotten (or none): sweep from here
        else if (next) HuntTarget(ai, board.WaveTargetPoint, NextSweep(ai, board.WaveTargetPoint, board.HuntSweep));
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
            if (u.State != UnitState.Idle || ai.OrderedNotApplied(slot, u.Handle.Generation)) board.WaveBusyTick = ai.Now;
        }
        if (live == 0 || half < (1f - ai.Def.RetreatLossFraction) * board.WaveStartHalfPop)
        {
            End(ai);
            board.WaveCooldownUntil = ai.Now + ai.Rules.WaveCooldownTicks; // retreat: no new wave for a while
            return;
        }
        // BUG-0598: every member has stood Idle, unordered, for a cooldown (each crowded by a target none of them strikes):
        // nobody is fighting, so the wave goes home like a retreat rather than freezing the AI's offence.
        if (ai.Now - board.WaveBusyTick > ai.Rules.WaveCooldownTicks)
        {
            End(ai);
            board.WaveCooldownUntil = ai.Now + ai.Rules.WaveCooldownTicks;
            return;
        }
        if (board.Hunting)
        {
            Hunt(ai, sum / live); // M5-4d
            return;
        }
        if (ai.EnemyBuildingKnown(board.WaveTarget, out _)) return;
        // The target is gone (razed, or seen gone): the nearest known enemy building to the members; with none, M5-4d, a hunt
        // (the base not defended, the match not over), else home.
        if (!ai.NearestEnemyBuilding(sum / live, out EntityHandle target, out int type, out int cell))
        {
            if (!board.Defending && !ai.View.MatchOver) BeginHunt(ai, sum / live);
            else End(ai);
            return;
        }
        SetTarget(ai, target, type, cell);
        for (int k = 0; k < ai.ArmyCount; k++)
            if (Member(ai, ai.ArmySlots[k], out _)) board.WaveFlag[ai.ArmySlots[k]] = ToAttack;
    }

    /// <summary>Ends the wave (M5-4d: or the hunt): every live member owes a Move to the rally point (also the defense's, M5-4b).</summary>
    internal static void End(AiPlayer ai)
    {
        Blackboard board = ai.Board;
        for (int k = 0; k < ai.ArmyCount; k++)
            if (Member(ai, ai.ArmySlots[k], out _)) board.WaveFlag[ai.ArmySlots[k]] = ToRally;
        board.WaveActive = false;
        board.Hunting = false;
        board.HuntSweep = -1;
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
        bool hunting = board.WaveActive && board.Hunting; // M5-4d: a point, no building
        if (board.WaveActive && !hunting)
        {
            // Near the target: within rallyReach of its footprint's edge, roughly (a unit can't stand at the centre).
            BuildingDef target = ai.Data.Buildings[board.WaveTargetType];
            near = ai.Rules.RallyReach + Math.Max(target.FootprintWidth, target.FootprintHeight) * Map.MapConstants.CellSize * 0.5f;
        }
        bool targetSeen = board.WaveActive && !hunting && ai.EnemyBuildingKnown(board.WaveTarget, out bool seen) && seen;
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
            else if (tagged && board.WaveActive)
            {
                float d = Vector2.Distance(u.Position, board.WaveTargetPoint);
                if (board.WaveFlag[slot] == ToAttack)
                {
                    // A new target (wave start or re-target): progress is measured from here.
                    board.WaveBest[slot] = d;
                    board.WaveTries[slot] = 0;
                    c = Command.AttackMove(ai.Player, u.Handle, board.WaveTargetPoint);
                }
                else
                {
                    if (u.State != UnitState.Idle) continue;
                    if (d <= board.WaveBest[slot] - Map.MapConstants.CellSize)
                    {
                        board.WaveBest[slot] = d; // a cell nearer since the last order: it is getting somewhere
                        board.WaveTries[slot] = 0;
                    }
                    // M5-4d: a hunt's member is only ever sent on toward the point; out of orders it waits there, a member,
                    // until the hunt moves the point (every member as close as it gets counts as reached).
                    if (hunting && board.WaveTries[slot] >= MaxTries) continue;
                    bool atTarget = targetSeen && d <= near;
                    UnitDef def = ai.Data.Units[u.TypeId];
                    if (hunting) c = Command.AttackMove(ai.Player, u.Handle, board.WaveTargetPoint);
                    else if (atTarget && def.Attack.Targets == AttackTargets.Units) c = Leave(ai, slot, u.Handle, def); // can't harm a building
                    else if (board.WaveTries[slot] >= MaxTries)
                    {
                        if (atTarget) continue; // crowded off the target: it stays by it, a member, unordered
                        c = Leave(ai, slot, u.Handle, def); // it can't get there
                    }
                    else if (atTarget) c = Command.Attack(ai.Player, u.Handle, board.WaveTarget, isBuilding: true);
                    else c = Command.AttackMove(ai.Player, u.Handle, board.WaveTargetPoint);
                }
            }
            else
            {
                // Not in a wave: an idle unit away from the rally point, not yet sent to this point.
                bool rallied = board.UnitGeneration[slot] == generation && board.RalliedVersion[slot] == board.RallyVersion;
                // M5-4b: nobody is rallied while the base is defended (the defense placed the army).
                if (board.Defending || board.RallyVersion == 0 || u.State != UnitState.Idle || rallied || Vector2.DistanceSquared(u.Position, board.Rally) <= rallyReach) continue;
                c = Command.Move(ai.Player, u.Handle, board.Rally);
            }
            if (!ai.Send(c)) return; // queue full: the flags keep the rest for the next think
            ai.MarkOrdered(slot, generation, 0);
            bool home = c.Kind == CommandKind.Move;
            board.RalliedVersion[slot] = home ? board.RallyVersion : 0;
            if (tagged) board.WaveFlag[slot] = 0;
            if (tagged && !home) board.WaveBusyTick = ai.Now;
            if (tagged && !home && board.WaveTries[slot] < byte.MaxValue) board.WaveTries[slot]++;
            if (tagged && home) board.WaveGeneration[slot] = 0; // home: no longer a member
        }
    }

    /// <summary>
    /// A member gives up on the wave alive: it is owed a Move home (kept if the queue is full) and its half-pop leaves the
    /// start's, so it counts neither as live nor as a loss toward the retreat (BUG-0596).
    /// </summary>
    private static Command Leave(AiPlayer ai, int slot, EntityHandle unit, UnitDef def)
    {
        Blackboard board = ai.Board;
        board.WaveFlag[slot] = ToRally;
        board.WaveStartHalfPop -= def.HalfPop;
        return Command.Move(ai.Player, unit, board.Rally);
    }

    /// <summary>Own army unit <paramref name="slot"/> as seen, if it is free for an order: alive, not casting, no AI command pending.</summary>
    private static bool Free(AiPlayer ai, int slot, out SeenUnit u) =>
        ai.View.TryGetUnit(slot, out u) && u.State != UnitState.Casting && !ai.OrderedNotApplied(slot, u.Handle.Generation);

    /// <summary>Whether own unit slot <paramref name="slot"/> holds a live member of the wave in progress (or just ended, not yet sent home).</summary>
    internal static bool Member(AiPlayer ai, int slot, out SeenUnit u) =>
        ai.View.TryGetUnit(slot, out u) && ai.Board.WaveGeneration[slot] == u.Handle.Generation && ai.Board.WaveFlag[slot] != ToRally;
}
