using System;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;

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
/// <c>AttackMove</c> to the known enemy building nearest the rally point. The cooldown (<c>ai.waveCooldown</c>) starts.
/// M5-9a round 2: a seat starved for <c>ai.starvedWaveAfter</c> (<see cref="AiPlayer.StarvedWaveDue"/>) starts one whatever
/// the threshold, keeping none, and hunts with no wave sent before.</item>
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
/// scout's map grid (<see cref="HuntPoint"/>: its cell centres and interior corners, on odd passes the cell edges' midpoints
/// instead, and a border ring; the hunt's own marks), the unvisited point nearest the army first, then the one nearest the
/// last, a tie within a map cell of the least distance going to the one nearer where the sweep began
/// (<see cref="NextSweep"/>); the first ring point reached starts the perimeter leg, the whole ring in order
/// (<see cref="AfterReached"/>). A point is reached when a member comes within <c>ai.rallyReach</c> of it, or every member
/// stands Idle having had <see cref="MaxTries"/> orders toward it without getting nearer (as close as they get): a sweep point is then visited, a sighting not seen again since is forgotten. Any other
/// sweep point the seat sees is visited too (<see cref="SeenPoints"/>). A fresher sighting (more than
/// <c>ai.rallyReach</c> from the target) re-targets at once. Members are sent <c>AttackMove</c> only. It ends as a wave does
/// (the retreat, the idle cooldown, the defense); an enemy building coming into view makes it a wave on that building.
/// </para>
/// <para>
/// M5-4c slice 2, the Hard knobs (docs/03 "Implementation (M5-4c slice 2)"): with <c>pullBackWounded</c> a member that falls
/// below its faction's <c>retreat.hpFraction</c> of its max hp goes home out of the wave, and goes out again with the next
/// one, which it fights out (<see cref="Wounded"/>, <see cref="PullBack"/>, <see cref="FightsOn"/>); with <c>focusFire</c> the wave's members and the defenders about in
/// attack range of the lowest-hp enemy unit seen are sent <c>Attack</c> on it (<see cref="Focus"/>). Both are off on Easy
/// and Normal, which play exactly as before.
/// </para>
/// </summary>
internal static class MilitaryManager
{
    /// <summary><see cref="Blackboard.WaveFlag"/>: an AttackMove to the wave's target is owed.</summary>
    internal const byte ToAttack = 1;

    /// <summary><see cref="Blackboard.WaveFlag"/>: a Move to the rally point is owed (a retreat or a wave's end).</summary>
    internal const byte ToRally = 2;

    /// <summary>
    /// BUG-1121: <see cref="Blackboard.WaveFlag"/> of a member <see cref="Focus"/> sent <c>Attack</c>: it is a fighter still,
    /// and once Idle with its focus dead it is re-sent on toward the wave's point without spending one of its
    /// <see cref="MaxTries"/> (a focus that it gave up, out of reach, spends one as before, so the churn stays bounded).
    /// Only ever set on a <c>focusFire</c> seat.
    /// </summary>
    internal const byte Focused = 3;

    /// <summary>
    /// BUG-1123: a bit of <see cref="Blackboard.WaveFlag"/> beside the order owed (<see cref="Owed"/>): the member was wounded
    /// (<see cref="Wounded"/>) when its wave or hunt started, so it fights that one out: <see cref="PullBack"/> leaves it in.
    /// Set only at a start (<see cref="TryStart"/>, <see cref="TryHunt"/>) and so only on a <c>pullBackWounded</c> seat; an order
    /// owed is set keeping it (<see cref="Owe"/>), and the member's leaving (a <see cref="ToRally"/>, the defense's pull) drops it.
    /// </summary>
    internal const byte FightsOn = 0x80;

    /// <summary>
    /// Orders (<c>AttackMove</c> or <c>Attack</c>) a member gets toward one target without getting a cell nearer it before the
    /// AI stops ordering it (BUG-0595/0596). Its nearest distance only falls, by a cell each reset, so a member's orders per
    /// target are bounded. Not a balance number: a guard against re-sending an order that does nothing.
    /// </summary>
    internal const int MaxTries = 3;

    /// <summary>
    /// M5-4c slice 2: the smallest bucket side (m) of the focus pass's fighter grid (<see cref="AiPlayer.FocusHeads"/>): 128 x
    /// 128 buckets on the largest map whatever <c>ai.focusReach</c>. A memory bound, not a balance number.
    /// </summary>
    internal const float MinFocusBucket = 16f;

    /// <summary>Rally, wave and retreat (see the type's summary).</summary>
    internal static void Run(AiPlayer ai)
    {
        if (ai.TryHallPoint(out Vector2 hall)) RallyPoint(ai, hall);
        if (ai.Board.WaveActive) Wave(ai);
        else if (!ai.Board.Defending && !TryStart(ai)) TryHunt(ai); // M5-4b: no wave while the base is defended
        Focus(ai); // M5-4c slice 2: before the orders, so a fighter it orders gets nothing else this think
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
            cost += def.UnitWorth; // M7-3 (BUG-1168): a pack unit's share of the item
            count++;
        }
        float scale = ai.Knobs.WaveSizeMultiplier;
        // M5-9a round 2 (BUG-1153): a seat starved for ai.starvedWaveAfter goes with what it has, whatever the threshold.
        bool starved = ai.StarvedWaveDue;
        if (count == 0 || !starved && (half < ai.Opening.AttackMinHalfPop * scale || cost < ai.Opening.AttackMinCost * scale)) return false;
        if (!ai.NearestEnemyBuilding(board.Rally, out EntityHandle target, out int type, out int cell)) return false;
        Array.Clear(board.WaveGeneration);
        Array.Clear(board.WaveFlag);
        for (int k = 0; k < ai.ArmyCount; k++)
        {
            int slot = ai.ArmySlots[k];
            if (!Free(ai, slot, out SeenUnit u)) continue;
            board.WaveGeneration[slot] = u.Handle.Generation;
            board.WaveFlag[slot] = Wounded(ai, u) ? (byte)(ToAttack | FightsOn) : ToAttack; // BUG-1123: a wounded unit goes, to stay
        }
        SetTarget(ai, target, type, cell);
        board.WaveActive = true;
        board.WaveStartHalfPop = half;
        // A starved wave keeps no threshold (as a hunt: the defense may pull any member).
        board.WaveMinHalfPop = starved ? 0f : ai.Opening.AttackMinHalfPop * scale;
        board.WaveMinCost = starved ? 0f : ai.Opening.AttackMinCost * scale;
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
        // M5-9a round 2: a starved seat hunts even with no wave sent before.
        if (board.KnownEnemyBuildings > 0 || board.WavesStarted == 0 && !ai.StarvedWaveDue || ai.View.MatchOver || ai.Now < board.WaveCooldownUntil) return;
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
            board.WaveFlag[slot] = Wounded(ai, u) ? (byte)(ToAttack | FightsOn) : ToAttack; // BUG-1123: a wounded unit goes, to stay
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
        board.HuntRing = -1;
        board.HuntRingLeft = board.HuntPass = 0;
        board.HuntsStarted++;
        if (Fresh(ai)) HuntTarget(ai, board.EnemyUnitPoint, -1, army);
        else StartSweep(ai, army);
    }

    /// <summary>
    /// M5-4d: the sweep (re)starts from the army at <paramref name="army"/>: its origin, and the point nearest it (BUG-1107
    /// fix round 2: or, while a perimeter leg is in progress, BUG-1111: the leg's ring point before <see cref="Blackboard.HuntRing"/>,
    /// the last one it reached, so the stretch of edge the sighting cut is walked from its start; reaching it hands back to
    /// the leg, <see cref="AfterReached"/>).
    /// </summary>
    private static void StartSweep(AiPlayer ai, Vector2 army)
    {
        ai.Board.HuntOrigin = army;
        int ring = ai.Board.HuntRing;
        HuntTarget(ai, army, ring >= 0 ? RingPrevious(Cols(ai), Rows(ai), ring) : NextSweep(ai, army, -1), army); // a leg in progress goes on
    }

    /// <summary>BUG-1111: the ring point whose next (<see cref="RingNext"/>) is ring point <paramref name="k"/>; one pass over the ring.</summary>
    internal static int RingPrevious(int cols, int rows, int k)
    {
        int ring = RingStart(cols, rows), end = HuntPoints(cols, rows);
        for (int j = ring; j < end; j++)
            if (RingNext(cols, rows, j) == k) return j;
        return k;
    }

    /// <summary>M5-4d: whether the last enemy unit seen is a sighting the hunt follows: at most <c>ai.huntMemory</c> old.</summary>
    private static bool Fresh(AiPlayer ai) =>
        ai.Board.EnemyUnitTick >= 0 && ai.Now - ai.Board.EnemyUnitTick <= ai.Rules.HuntMemoryTicks;

    /// <summary>
    /// M5-4d: the hunt's target becomes <paramref name="sweep"/>'s grid point, or with -1 <paramref name="point"/> (a
    /// sighting), clamped inside the map and (M5-H5, BUG-1120) pushed off any allied building toward the members at
    /// <paramref name="army"/> (<see cref="SightingPoint"/>); every member is owed an AttackMove to it.
    /// </summary>
    private static void HuntTarget(AiPlayer ai, Vector2 point, int sweep, Vector2 army)
    {
        Blackboard board = ai.Board;
        board.HuntSweep = sweep;
        board.WaveTargetPoint = sweep >= 0 ? HuntPoint(ai, Cols(ai), Rows(ai), sweep) : SightingPoint(ai, point, army);
        for (int k = 0; k < ai.ArmyCount; k++)
            if (Member(ai, ai.ArmySlots[k], out _)) Owe(board, ai.ArmySlots[k], ToAttack);
    }

    /// <summary>
    /// M5-H5 (BUG-1120, the Producer's default): the point a hunt sends its members to for a sighting at
    /// <paramref name="seen"/>: clamped inside the map, and when it lies on or within <see cref="AllySlack"/> of a building of
    /// an ally the seat sees (not its own, not an enemy's), moved along the line toward the members at
    /// <paramref name="army"/> until it is <see cref="AllySlack"/> clear of that footprint, then half a map cell more (a point
    /// on the members themselves goes out from the footprint's centre). An enemy unit hitting an ally's building was seen
    /// right by its wall, and an AttackMove there was an order "onto an ally's building" (QA's team soak, seed 1 adjacent);
    /// the push keeps the order beside the fight, on the hunter's side. At most <see cref="MaxPushes"/> pushes (one per
    /// footprint met; a point boxed in by allied buildings keeps the last). Reads the seat's view only; no allocation.
    /// </summary>
    internal static Vector2 SightingPoint(AiPlayer ai, Vector2 seen, Vector2 army)
    {
        Vector2 p = ai.ClampToMap(seen);
        if (!ai.View.HasAlly) return p;
        for (int push = 0; push < MaxPushes; push++)
        {
            bool moved = false;
            int s = -1;
            while (ai.View.NextBuilding(ref s, out SeenBuilding b))
            {
                if (b.Owner == ai.Player || ai.View.IsEnemy(b.Owner)) continue;
                BuildingDef def = ai.Data.Buildings[b.TypeId];
                int w = ai.View.MapWidth;
                Vector2 min = new Vector2(b.Cell % w, b.Cell / w) * MapConstants.CellSize - new Vector2(AllySlack);
                Vector2 max = min + new Vector2(def.FootprintWidth, def.FootprintHeight) * MapConstants.CellSize + new Vector2(2 * AllySlack);
                if (p.X < min.X || p.Y < min.Y || p.X > max.X || p.Y > max.Y) continue;
                Vector2 d = army - p;
                if (d.LengthSquared() < 1e-6f) d = p - (min + max) * 0.5f;
                if (d.LengthSquared() < 1e-6f) d = Vector2.UnitX;
                d = Vector2.Normalize(d);
                // The ray p + t d leaves the slack box at the least exit t over the axes it moves along.
                float tx = d.X > 0 ? (max.X - p.X) / d.X : d.X < 0 ? (min.X - p.X) / d.X : float.MaxValue;
                float ty = d.Y > 0 ? (max.Y - p.Y) / d.Y : d.Y < 0 ? (min.Y - p.Y) / d.Y : float.MaxValue;
                p = ai.ClampToMap(p + d * (Math.Min(tx, ty) + MapConstants.CellSize * 0.5f));
                moved = true;
                break;
            }
            if (!moved) break;
        }
        return p;
    }

    /// <summary>M5-H5 (BUG-1120): how close (m) to an allied building's footprint a hunt's point may lie (QA's soak oracle's slack).</summary>
    internal const float AllySlack = 0.5f;

    /// <summary>M5-H5 (BUG-1120): the most footprints <see cref="SightingPoint"/> pushes a point off (a bound, not a balance number).</summary>
    private const int MaxPushes = 4;

    /// <summary>
    /// M5-4d: the next sweep point: the unvisited one nearest <paramref name="from"/> (the last point, or the army), never
    /// <paramref name="skip"/>; distances within a map cell of each other tie, and the tie goes to the point nearer
    /// <see cref="Blackboard.HuntOrigin"/> (then list order); once all are visited the marks clear and the sweep starts over.
    /// Why the origin tie-break: nearest-first with list order on ties walked straight away from where the enemy was last
    /// (Pale Hills seed 1: a last building 50 m from the razed hall, the sweep heading for the far edge on a four-way tie).
    /// Why only a tie-break (BUG-1107 fix round 1; before it, the least distance from the last point plus distance from the
    /// origin): that sum pulled the army back to inner points it had left, 15 s of walking per point on Pale Hills, and with
    /// the border ring a sweep there outlasted the 60-minute match; nearest-first walks 13 s per point. A restart counts the
    /// pass (<see cref="Blackboard.HuntPass"/>, BUG-1107 fix round 2), so the next pass sweeps the other interior lattice
    /// (<see cref="HuntPoint"/>). Two passes over the grid points (four at a restart).
    /// </summary>
    internal static int NextSweep(AiPlayer ai, Vector2 from, int skip)
    {
        bool[] visited = ai.Board.HuntVisited;
        if (ai.Board.HuntsStarted > 1)
        {
            int start = NextHuntStart(ai, from, skip); // M5-H6 (BUG-1157): from the match's second hunt on, the free starts first
            if (start >= 0) return start;
        }
        int best = NearestUnvisited(ai, visited, from, skip);
        if (best >= 0) return best;
        Array.Clear(visited);
        ai.Board.HuntPass++; // the other interior lattice (HuntPoint)
        best = NearestUnvisited(ai, visited, from, skip);
        return best >= 0 ? best : 0; // a one-point grid: that point again
    }

    /// <summary>
    /// The unvisited point (not <paramref name="skip"/>) nearest <paramref name="from"/>, ties (within a map cell of the
    /// least distance, QA round 1: of the true least, so the pick does not hang on list order) to the one nearer
    /// <see cref="Blackboard.HuntOrigin"/>, then list order; -1 with none left. Two passes over the points, no allocation.
    /// </summary>
    internal static int NearestUnvisited(AiPlayer ai, bool[] visited, Vector2 from, int skip)
    {
        int best = -1, cols = Cols(ai), rows = Rows(ai);
        float least = float.MaxValue, bestOrigin = float.MaxValue;
        for (int i = 0; i < visited.Length; i++)
            if (!visited[i] && i != skip) least = Math.Min(least, Vector2.Distance(from, HuntPoint(ai, cols, rows, i)));
        Vector2 origin = ai.Board.HuntOrigin;
        for (int i = 0; i < visited.Length; i++)
        {
            if (visited[i] || i == skip) continue;
            Vector2 p = HuntPoint(ai, cols, rows, i);
            if (Vector2.Distance(from, p) >= least + MapConstants.CellSize) continue;
            float o = Vector2.Distance(origin, p);
            if (o >= bestOrigin) continue;
            bestOrigin = o;
            best = i;
        }
        return best;
    }

    /// <summary>
    /// M5-4d: how many sweep points a <paramref name="cols"/> x <paramref name="rows"/> scout grid gives the hunt: its cell
    /// centres, then its interior corners, then the border ring. Why the corners too: the centres alone are a cell apart
    /// (51 m on Pale Hills at the shipped <c>ai.scoutGrid</c>), past a unit's sight (14 m), and a side's last units standing
    /// 29-35 m from the nearest centre were missed for 40,000 ticks (seed 5). Centres alone leave ground half a cell's
    /// diagonal from any point (36 m there); with the corners, at most half a cell (25.6 m) inside the interior. Why the ring
    /// (BUG-1107): nothing else lies near the map's border, and a last unit by an edge or in a corner (29-30 m from the
    /// nearest interior point on Raraku) was never seen. The ring is the four map corners and each edge's points level with
    /// the cell centres, <c>2 (cols + rows) + 4</c> points: with it every point of the map is within half a cell of a sweep
    /// point, the border band as the interior, and a corner on one. Why not every half cell along the border (twice the
    /// points): measured, a sweep of Pale Hills then outlasted the 60-minute match (with the old order; the perimeter leg,
    /// <see cref="AfterReached"/>, is what sees the edge between ring points now). The count is the same on an odd pass.
    /// </summary>
    internal static int HuntPoints(int cols, int rows) => cols * rows + (cols - 1) * (rows - 1) + 2 * (cols + rows) + 4;

    private static int Cols(AiPlayer ai) => ScoutManager.GridLines(ai.View.MapWidth, ai.Rules.ScoutGrid);

    private static int Rows(AiPlayer ai) => ScoutManager.GridLines(ai.View.MapHeight, ai.Rules.ScoutGrid);

    /// <summary>
    /// M5-4d: sweep point <paramref name="k"/> of a <paramref name="cols"/> x <paramref name="rows"/> grid, clamped inside the
    /// map: below <c>cols x rows</c> the scout's grid point (<see cref="ScoutManager.GridPoint"/>'s cell centre, row order),
    /// then the grid's interior corners in row order, then (BUG-1107) the border ring in row order: the top edge's west corner,
    /// its points level with the cell centres west to east, its east corner; each cell row's west and east ends; the bottom
    /// edge likewise. Clamping puts a ring point half a map cell inside the border, so no stat sets where it stands.
    /// BUG-1107 fix round 2: on an odd pass (<see cref="Blackboard.HuntPass"/>) the indices below the ring are the cell
    /// edges' midpoints instead, the spots half a cell from four even-pass points (QA's interior hide, 25 m from every
    /// point and passed 18 m off by the diagonal walk between them, was never seen on the generated map); the two passes
    /// together are every point of the half-cell lattice. Allocates nothing.
    /// </summary>
    internal static Vector2 HuntPoint(AiPlayer ai, int cols, int rows, int k)
    {
        int centres = cols * rows, corners = (cols - 1) * (rows - 1);
        Vector2 size = ai.MapSize;
        if (k < centres + corners && ai.Board.HuntPass % 2 == 1)
        {
            // BUG-1107 fix round 2, an odd pass: the cell edges' midpoints, the west / east edges' (between vertically
            // adjacent corners) in row order, then the north / south edges' (between horizontally adjacent centres); one
            // index more than they need is the map's centre.
            int vertical = (cols - 1) * rows, j = k;
            if (j < vertical) return ai.ClampToMap(new Vector2((j % (cols - 1) + 1) * size.X / cols, (j / (cols - 1) + 0.5f) * size.Y / rows));
            j -= vertical;
            if (j < cols * (rows - 1)) return ai.ClampToMap(new Vector2((j % cols + 0.5f) * size.X / cols, (j / cols + 1) * size.Y / rows));
            return ai.ClampToMap(size * 0.5f);
        }
        if (k < centres) return ScoutManager.GridPoint(ai, Blackboard.ScoutHead + k);
        if (k < centres + corners)
        {
            int j = k - centres, x = j % (cols - 1), y = j / (cols - 1);
            return ai.ClampToMap(new Vector2((x + 1) * size.X / cols, (y + 1) * size.Y / rows));
        }
        // The ring, on the lattice of half cells (hx in 0..2 cols, hy in 0..2 rows): an edge's corners and its odd half cells.
        int r = k - centres - corners, across = cols + 2, hx, hy;
        if (r < across || r >= across + 2 * rows)
        {
            int j = r < across ? r : r - across - 2 * rows;
            hx = j == 0 ? 0 : j == across - 1 ? 2 * cols : 2 * j - 1;
            hy = r < across ? 0 : 2 * rows;
        }
        else
        {
            int side = r - across;
            hx = side % 2 == 0 ? 0 : 2 * cols;
            hy = 2 * (side / 2) + 1;
        }
        return ai.ClampToMap(new Vector2(hx * size.X / (2 * cols), hy * size.Y / (2 * rows)));
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
            board.HuntSweep = board.HuntRing = -1;
            board.HuntRingLeft = 0;
            SetTarget(ai, target, type, cell);
            for (int k = 0; k < ai.ArmyCount; k++)
                if (Member(ai, ai.ArmySlots[k], out _)) Owe(board, ai.ArmySlots[k], ToAttack);
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
            if (u.State != UnitState.Idle || ai.OrderedNotApplied(slot, u.Handle.Generation) || Owed(board, slot) == ToAttack
                || board.WaveTries[slot] < MaxTries) stuck = false;
        }
        if (board.HuntSweep >= 0) SeenPoints(ai);
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
            // BUG-1120: against the point the sighting would be sent to, so a pushed one is not re-sent every think.
            if (board.HuntSweep >= 0 || Vector2.DistanceSquared(SightingPoint(ai, board.EnemyUnitPoint, army), board.WaveTargetPoint) > reach)
                HuntTarget(ai, board.EnemyUnitPoint, -1, army);
            return;
        }
        if (board.HuntSweep < 0) StartSweep(ai, army); // a sighting forgotten (or none): sweep from here
        else if (next) HuntTarget(ai, board.WaveTargetPoint, AfterReached(ai, board.HuntSweep), army);
    }

    /// <summary>
    /// M5-4d (BUG-1107 fix round 2): the sweep point after <paramref name="reached"/> (just visited). Reaching a border-ring
    /// point with no leg in progress starts the <b>perimeter leg</b>: every other ring point in clockwise order, visited or
    /// not, then this one again (<see cref="Blackboard.HuntRing"/>, <see cref="Blackboard.HuntRingLeft"/>); then every ring
    /// point counts visited for this pass and the sweep goes on nearest-first (<see cref="NextSweep"/>). Why: ring points are
    /// a scout cell apart (51 m) and sight is 14 m, so an edge spot level with a grid line is seen only from the edge between
    /// its two ring points. Nearest-first never walked it: from a ring point the cell centre half a cell inward beats the
    /// next ring point a cell along, and each pass took the same route (QA's worst-point hides, 11 of 16 never found).
    /// Walking the ring in order traces every edge once a pass. Why visited points too: <see cref="SeenPoints"/> marks a ring
    /// point once its cell is seen, and skipping it would skip the edge on either side. A leg interrupted by a sighting is
    /// picked up where it stopped (<see cref="StartSweep"/>), else the edge past the sighting went unwalked (Pale Hills
    /// <c>cadre_rush</c>: a sighting 175 m inland mid-leg, the north edge's east half never walked).
    /// </summary>
    private static int AfterReached(AiPlayer ai, int reached)
    {
        Blackboard board = ai.Board;
        int cols = Cols(ai), rows = Rows(ai), ring = RingStart(cols, rows);
        if (board.HuntRing >= 0)
        {
            if (reached != board.HuntRing) return board.HuntRing; // not a leg step: back to the leg
            if (--board.HuntRingLeft > 0) return board.HuntRing = RingNext(cols, rows, reached);
            board.HuntRing = -1; // round the ring: one leg a pass
            for (int i = ring; i < board.HuntVisited.Length; i++) board.HuntVisited[i] = true;
        }
        else if (reached >= ring)
        {
            board.HuntRingLeft = board.HuntVisited.Length - ring;
            return board.HuntRing = RingNext(cols, rows, reached);
        }
        return NextSweep(ai, HuntPoint(ai, cols, rows, reached), reached);
    }

    /// <summary>
    /// M5-H6 (BUG-1157): the sweep point that stands for a start location centred at <paramref name="centre"/>: the interior
    /// point (a cell centre or interior corner, on an odd pass a cell edge's midpoint; never a ring point, so reaching it
    /// starts no perimeter leg) nearest it, the lowest index on ties. Its visited mark is the start's. One pass over the
    /// interior points.
    /// </summary>
    internal static int StartProxy(AiPlayer ai, int cols, int rows, Vector2 centre)
    {
        int ring = RingStart(cols, rows), best = 0;
        float least = float.MaxValue;
        for (int i = 0; i < ring; i++)
        {
            float d = Vector2.DistanceSquared(centre, HuntPoint(ai, cols, rows, i));
            if (d >= least) continue;
            least = d;
            best = i;
        }
        return best;
    }

    /// <summary>
    /// M5-H6 (BUG-1157, the brief's lever (i)): the sweep point the hunt walks to next when it looks at the start locations
    /// first: of the free starts (no player starts there, or its player is defeated: <see cref="FarExpansion.NextStart"/>'s
    /// set, the start list every seat is given), the one whose <see cref="StartProxy"/> is unvisited (and not
    /// <paramref name="skip"/>) and whose centre is nearest <paramref name="from"/> (start order on ties); once every free
    /// start's proxy is visited, a live enemy's own start the same way (fix round 1, BUG-1162: a beaten side's last worker
    /// often stands at home, and re-planning nearest-first from a far free start left it unseen for the hour); -1 with none.
    /// Why: since the far expansion (M5-9b) a beaten side's last depot and workers often stand at a free start across the map,
    /// which nearest-first from where its base fell reached only after the perimeter leg (Pale Hills seed 2: five hunts and
    /// 79,504 ticks; Hard v Normal seed 2: ten hunts). Only from the match's second hunt on (<see cref="NextSweep"/>): the
    /// first sweeps from where the enemy was beaten, where a remnant usually is (QA's hide rows, whose quarter-cell hide on
    /// Pale Hills went unseen for the hour when every hunt began at the starts). Allocates nothing.
    /// </summary>
    internal static int NextHuntStart(AiPlayer ai, Vector2 from, int skip)
    {
        int free = NearestHuntStart(ai, from, skip, liveEnemy: false);
        return free >= 0 ? free : NearestHuntStart(ai, from, skip, liveEnemy: true);
    }

    /// <summary>
    /// M5-H6: <see cref="NextHuntStart"/>'s pass over the start list: the free starts, or with <paramref name="liveEnemy"/>
    /// the starts of enemies still in the match; the unvisited proxy (not <paramref name="skip"/>) of the one nearest
    /// <paramref name="from"/>, -1 with none.
    /// </summary>
    private static int NearestHuntStart(AiPlayer ai, Vector2 from, int skip, bool liveEnemy)
    {
        int cols = Cols(ai), rows = Rows(ai), players = ai.View.PlayerCount, count = Math.Min(ai.View.StartCount, FarExpansion.MaxStarts), best = -1;
        float least = float.MaxValue;
        for (int k = 0; k < count; k++)
        {
            bool taken = k < players && !ai.View.IsDefeated(k); // a player still in the match starts here
            if (k == ai.Player || (liveEnemy ? !taken || !ai.View.IsEnemy(k) : taken)) continue;
            Vector2 centre = ai.CellCentre(ai.View.StartCell(k));
            int point = StartProxy(ai, cols, rows, centre);
            if (ai.Board.HuntVisited[point] || point == skip) continue;
            float d = Vector2.DistanceSquared(from, centre);
            if (d >= least) continue;
            least = d;
            best = point;
        }
        return best;
    }

    /// <summary>M5-4d: the index of the first border-ring point (after the cell centres and the interior corners).</summary>
    internal static int RingStart(int cols, int rows) => cols * rows + (cols - 1) * (rows - 1);

    /// <summary>
    /// M5-4d (BUG-1107 fix round 2): the ring point after ring point <paramref name="k"/> going clockwise round the map
    /// (north edge west to east, east edge north to south, south edge east to west, west edge south to north), in
    /// <see cref="HuntPoint"/>'s numbering.
    /// </summary>
    internal static int RingNext(int cols, int rows, int k)
    {
        int ring = RingStart(cols, rows), r = k - ring, across = cols + 2, bottom = across + 2 * rows, next;
        if (r < across) next = r < across - 1 ? r + 1 : across + 1;                     // north edge; its east corner turns south
        else if (r >= bottom) next = r > bottom ? r - 1 : across + 2 * (rows - 1);      // south edge; its west corner turns north
        else
        {
            int side = r - across, row = side / 2;
            if (side % 2 == 1) next = row < rows - 1 ? across + 2 * (row + 1) + 1 : bottom + across - 1; // east edge, down
            else next = row > 0 ? across + 2 * (row - 1) : 0;                                             // west edge, up
        }
        return ring + next;
    }

    /// <summary>
    /// M5-4d (BUG-1107, fix round 1): every unvisited sweep point other than the target whose cell the seat's own fog shows
    /// visible this think is visited too: the ground there is being looked at, so the army need not walk to it. Why: with
    /// the border ring a pass over Pale Hills' grid outlasted the 60-minute match, the army walking to points its members'
    /// sight had already swept on the way to others. The target keeps its own rule (a member within
    /// <c>ai.rallyReach</c>, or every member stuck), so each point the army aims at it walks right up to; counting the target
    /// reached once visible was measured and rejected (the army re-targeted every few seconds, past QA's order bound).
    /// Reads only the seat's <see cref="PlayerView"/>; allocates nothing.
    /// </summary>
    private static void SeenPoints(AiPlayer ai)
    {
        Blackboard board = ai.Board;
        bool[] visited = board.HuntVisited;
        int cols = Cols(ai), rows = Rows(ai), width = ai.View.MapWidth, height = ai.View.MapHeight;
        for (int i = 0; i < visited.Length; i++)
        {
            if (visited[i] || i == board.HuntSweep) continue;
            Vector2 p = HuntPoint(ai, cols, rows, i);
            int x = Math.Clamp((int)(p.X / MapConstants.CellSize), 0, width - 1), y = Math.Clamp((int)(p.Y / MapConstants.CellSize), 0, height - 1);
            if (ai.View.IsVisible(y * width + x)) visited[i] = true;
        }
    }

    private static void Wave(AiPlayer ai)
    {
        Blackboard board = ai.Board;
        if (ai.Knobs.PullBackWounded) PullBack(ai); // M5-4c slice 2: before the count, so a pulled member is no loss
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
        if (ai.EnemyBuildingKnown(board.WaveTarget, out _))
        {
            if (Looking(ai)) Look(ai); // M5-4d slice 2
            return;
        }
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
            if (Member(ai, ai.ArmySlots[k], out _)) Owe(board, ai.ArmySlots[k], ToAttack);
    }

    /// <summary>Ends the wave (M5-4d: or the hunt): every live member owes a Move to the rally point (also the defense's, M5-4b).</summary>
    internal static void End(AiPlayer ai)
    {
        Blackboard board = ai.Board;
        for (int k = 0; k < ai.ArmyCount; k++)
            if (Member(ai, ai.ArmySlots[k], out _)) board.WaveFlag[ai.ArmySlots[k]] = ToRally;
        board.WaveActive = false;
        board.Hunting = false;
        board.HuntSweep = board.HuntRing = -1;
        board.HuntRingLeft = 0;
    }

    private static void SetTarget(AiPlayer ai, EntityHandle target, int type, int cell)
    {
        Blackboard board = ai.Board;
        board.WaveTarget = target;
        board.WaveTargetType = type;
        board.WaveTargetCell = cell;
        board.WaveTargetPoint = ai.BuildingCentre(type, cell);
        if (LookPoint(ai, type, cell, out Vector2 look)) board.WaveTargetPoint = look; // M5-4d slice 2
    }

    /// <summary>
    /// M5-4d slice 2 (docs/02 "High ground": the AI scouts ramps before attacking up them): the <b>look point</b> a wave on
    /// building <paramref name="type"/> at <paramref name="cell"/> goes to first, when the building's level is above the
    /// level under the members' centroid and the seat doesn't see its cell: the top of the ramp nearest the building onto
    /// its plateau, the centre of the plateau cell (the building's own plateau by the terrain's labels, so joined to it by
    /// same-level ground) with a ramp cell one level below beside it, nearest the building's centre (row order on ties),
    /// not under the building. False with none (a flat map, a same-level target, a plateau with no ramp). Reads only
    /// static terrain, the members and the seat's own fog through <see cref="PlayerView"/>; one pass over the map's ramp
    /// tops (<see cref="AiPlayer.RampTops"/>, listed at construction), run only when a wave's target is set; no allocation.
    /// </summary>
    internal static bool LookPoint(AiPlayer ai, int type, int cell, out Vector2 look)
    {
        look = default;
        int n = 0;
        Vector2 sum = Vector2.Zero;
        for (int k = 0; k < ai.ArmyCount; k++)
        {
            if (!Member(ai, ai.ArmySlots[k], out SeenUnit u)) continue;
            sum += u.Position;
            n++;
        }
        return n > 0 && LookPoint(ai, type, cell, ai.View.LevelAt(CellOf(ai, sum / n)), out look);
    }

    /// <summary>See <see cref="LookPoint(AiPlayer, int, int, out Vector2)"/>, from members standing at level <paramref name="fromLevel"/>.</summary>
    internal static bool LookPoint(AiPlayer ai, int type, int cell, int fromLevel, out Vector2 look)
    {
        look = default;
        PlayerView v = ai.View;
        int level = v.LevelAt(cell);
        if (level <= fromLevel || v.IsVisible(cell)) return false;
        int plateau = v.PlateauAt(cell);
        if (plateau < 0) return false;
        BuildingDef def = ai.Data.Buildings[type];
        int w = v.MapWidth, bx = cell % w, by = cell / w;
        Vector2 centre = ai.BuildingCentre(type, cell);
        int best = -1;
        float bestDistance = float.MaxValue;
        int[] tops = ai.RampTops; // row order, so a tie keeps the first
        for (int k = 0; k < tops.Length; k++)
        {
            int c = tops[k], x = c % w, y = c / w;
            if (v.PlateauAt(c) != plateau || x >= bx && x < bx + def.FootprintWidth && y >= by && y < by + def.FootprintHeight) continue;
            float d = Vector2.DistanceSquared(ai.CellCentre(c), centre);
            if (d >= bestDistance) continue;
            bestDistance = d;
            best = c;
        }
        if (best < 0) return false;
        look = ai.CellCentre(best);
        return true;
    }

    /// <summary>
    /// M5-4d slice 2: every ramp top of the map in row order: a plateau cell (<see cref="PlayerView.PlateauAt"/>) with a ramp
    /// cell one level below it beside it (4-neighbours). Static terrain, so listed once when a seat is built (two passes over
    /// the map: count, then fill); <see cref="LookPoint(AiPlayer, int, int, int, out Vector2)"/> then reads only this list
    /// (a Vathar Crossing bank is 27,000 cells, its tops a few dozen).
    /// </summary>
    internal static int[] RampTops(PlayerView v)
    {
        int w = v.MapWidth, n = w * v.MapHeight, count = 0;
        int[]? tops = null;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int c = 0; c < n; c++)
            {
                if (v.PlateauAt(c) < 0) continue;
                int level = v.LevelAt(c);
                if (!RampBelow(v, c - 1, level) && !RampBelow(v, c + 1, level) && !RampBelow(v, c - w, level) && !RampBelow(v, c + w, level)) continue;
                if (tops != null) tops[count] = c;
                count++;
            }
            if (tops != null) break;
            tops = new int[count];
            count = 0;
        }
        return tops!;
    }

    /// <summary>Whether <paramref name="cell"/> is a ramp cell one level below <paramref name="level"/> (a plateau cell is never on the border, so its neighbours are on the map).</summary>
    private static bool RampBelow(PlayerView v, int cell, int level) => v.IsRamp(cell) && v.LevelAt(cell) == level - 1;

    /// <summary>The cell under <paramref name="p"/> (m), clamped onto the map.</summary>
    private static int CellOf(AiPlayer ai, Vector2 p)
    {
        int w = ai.View.MapWidth, h = ai.View.MapHeight;
        int x = Math.Clamp((int)(p.X / MapConstants.CellSize), 0, w - 1), y = Math.Clamp((int)(p.Y / MapConstants.CellSize), 0, h - 1);
        return y * w + x;
    }

    /// <summary>
    /// M5-4d slice 2: whether the wave in progress is on its way to a look point: a building wave whose point is not its
    /// target's centre (<see cref="SetTarget"/> put it elsewhere; read from fields a save already holds, so no new one).
    /// </summary>
    internal static bool Looking(AiPlayer ai)
    {
        Blackboard board = ai.Board;
        return board.WaveActive && !board.Hunting && board.WaveTargetType >= 0
            && board.WaveTargetPoint != ai.BuildingCentre(board.WaveTargetType, board.WaveTargetCell);
    }

    /// <summary>
    /// M5-4d slice 2: a wave think on the way to the look point: the building becomes the point (every member owed an
    /// <c>AttackMove</c> to it) once a member is within <c>ai.rallyReach</c> of the look point, the seat's own fog shows the
    /// target's cell, or every member stands Idle with no order pending or owed after <see cref="MaxTries"/> orders toward
    /// it without getting nearer (as close as they get: the hunt's rule).
    /// </summary>
    private static void Look(AiPlayer ai)
    {
        Blackboard board = ai.Board;
        bool done = ai.View.IsVisible(board.WaveTargetCell), stuck = true;
        float reach = ai.Rules.RallyReach * ai.Rules.RallyReach;
        for (int k = 0; k < ai.ArmyCount && !done; k++)
        {
            int slot = ai.ArmySlots[k];
            if (!Member(ai, slot, out SeenUnit u)) continue;
            if (Vector2.DistanceSquared(u.Position, board.WaveTargetPoint) <= reach) done = true;
            if (u.State != UnitState.Idle || ai.OrderedNotApplied(slot, u.Handle.Generation) || Owed(board, slot) == ToAttack
                || board.WaveTries[slot] < MaxTries) stuck = false;
        }
        if (!done && !stuck) return;
        board.WaveTargetPoint = ai.BuildingCentre(board.WaveTargetType, board.WaveTargetCell);
        for (int k = 0; k < ai.ArmyCount; k++)
            if (Member(ai, ai.ArmySlots[k], out _)) Owe(board, ai.ArmySlots[k], ToAttack);
    }

    /// <summary>Every army unit's command this think, in slot order (one each at most).</summary>
    private static void Orders(AiPlayer ai)
    {
        Blackboard board = ai.Board;
        float near = 0f;
        // M5-4d: a hunt's target is a point, no building; M5-4d slice 2: so is a look point, until Look hands over to the building.
        bool hunting = board.WaveActive && (board.Hunting || Looking(ai));
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
            bool tagged = board.WaveGeneration[slot] == generation, resume = false;
            Command c;
            if (tagged && board.WaveFlag[slot] == ToRally)
                c = Command.Move(ai.Player, u.Handle, board.Rally);
            else if (tagged && board.WaveActive)
            {
                float d = Vector2.Distance(u.Position, board.WaveTargetPoint);
                if (Owed(board, slot) == ToAttack)
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
                    // BUG-1121: its focus killed (not given up): back on toward the point, no try spent.
                    resume = Owed(board, slot) == Focused && !ai.View.TryGetGivenUp(slot, out _);
                    // M5-4d: a hunt's member is only ever sent on toward the point; out of orders it waits there, a member,
                    // until the hunt moves the point (every member as close as it gets counts as reached).
                    if (hunting && !resume && board.WaveTries[slot] >= MaxTries) continue;
                    bool atTarget = targetSeen && d <= near;
                    UnitDef def = ai.Data.Units[u.TypeId];
                    if (hunting || resume) c = Command.AttackMove(ai.Player, u.Handle, board.WaveTargetPoint);
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
            if (tagged) Owe(board, slot, 0);
            if (tagged && !home) board.WaveBusyTick = ai.Now;
            if (tagged && !home && !resume && board.WaveTries[slot] < byte.MaxValue) board.WaveTries[slot]++;
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

    /// <summary>
    /// M5-4c slice 2 (<c>pullBackWounded</c>, docs/02 "AI opponent": Hard pulls back wounded units): whether own unit
    /// <paramref name="u"/> is wounded: the difficulty pulls back the wounded and its hp is below its faction's
    /// <c>retreat.hpFraction</c> (ai.json: "a unit below it is wounded") of its max hp (its type's plus the player's hp techs).
    /// Always false on a difficulty that doesn't, so Easy and Normal play as before. BUG-1122: compared in decimal, the
    /// stored float fraction read back to its shortest decimal (0.3f is 0.3), so a unit at exactly the fraction of its max hp
    /// is not wounded for any max hp (in float 0.3f x 50 rounds up past 15).
    /// </summary>
    internal static bool Wounded(AiPlayer ai, in SeenUnit u) =>
        ai.Knobs.PullBackWounded && u.Hp < (decimal)ai.Def.RetreatHpFraction * ai.View.MaxHpOf(u.TypeId);

    /// <summary>
    /// M5-4c slice 2: every wounded member (<see cref="Wounded"/>) leaves the wave alive, unless it was wounded when the wave
    /// started (<see cref="FightsOn"/>): it is owed a Move to the rally point (sent by <see cref="Orders"/>, kept if the queue
    /// is full) and its half-pop leaves the start's, so it counts neither as live nor as a loss toward the retreat (M5-3's "a
    /// member that leaves alive is not a loss", as <see cref="Leave"/>). At home it answers the defense. BUG-1123: nothing
    /// heals yet, so the next wave or hunt takes it along like any unit (counted toward its start), flagged
    /// <see cref="FightsOn"/>, and it fights that one out: each unit is pulled back once, and a pulled-back unit fights again
    /// (before the fix it was in no wave again, so a Hard army shrank over a long match and lost to Normal).
    /// </summary>
    private static void PullBack(AiPlayer ai)
    {
        Blackboard board = ai.Board;
        for (int k = 0; k < ai.ArmyCount; k++)
        {
            int slot = ai.ArmySlots[k];
            if (!Member(ai, slot, out SeenUnit u) || (board.WaveFlag[slot] & FightsOn) != 0 || !Wounded(ai, u)) continue;
            board.WaveFlag[slot] = ToRally; // drops FightsOn: it leaves
            board.WaveStartHalfPop -= ai.Data.Units[u.TypeId].HalfPop;
        }
    }

    /// <summary>
    /// M5-4c slice 2 (<c>focusFire</c>, docs/02 "AI opponent": Hard focuses fire): with a wave (or hunt) in progress or the
    /// base defended, the <b>fighters</b> are the wave's members owed nothing and the defenders inside the base (<see cref="Fighter"/>). Of
    /// BUG-1121: a fighter's <b>reach</b> is about its attack range: its type's <c>attack.range</c> + its radius + one map
    /// cell, at most <c>ai.focusReach</c>. Of the enemy units the seat sees now within a fighter's reach, the <b>focus</b> is
    /// the one with the fewest hp, an army unit before a worker, slot order on ties (<see cref="AiPlayer.FocusTarget"/>;
    /// picked afresh at every think, so a focus that dies or leaves sight is replaced at the next). Every fighter with it in
    /// its reach gets <c>Attack</c> on it, unless it already holds that order or gave that unit up (out of reach: the give-up
    /// memory, which its next order clears), so no fighter is re-ordered onto a target it can't get to (BUG-0595/0596's
    /// churn); the others keep their own fights (a melee army no longer crowds one enemy while the rest hit it, BUG-1121). A
    /// wave member so ordered is <see cref="Focused"/>: when its focus dies it goes on without spending a try. No enemy
    /// unit in reach: nothing, and the wave's and the defense's rules go on as before. Reads only the seat's view; one pass
    /// over the army (the fighters into a bucket grid, <see cref="AiPlayer.FocusHeads"/>) and one over the enemy seen (each
    /// tested against the fighters of the 3 x 3 buckets round it only when it lies in their box and would be the new best);
    /// no allocation.
    /// </summary>
    private static void Focus(AiPlayer ai)
    {
        if (!ai.Knobs.FocusFire) return;
        Blackboard board = ai.Board;
        if (!board.WaveActive && !board.Defending) return;
        float reach = ai.Rules.FocusReach;
        int n = 0;
        Vector2 lo = new(float.MaxValue), hi = new(float.MinValue);
        for (int k = 0; k < ai.ArmyCount; k++)
        {
            int slot = ai.ArmySlots[k];
            if (!Fighter(ai, slot, out SeenUnit u)) continue;
            int bucket = Bucket(ai, u.Position);
            UnitDef def = ai.Data.Units[u.TypeId];
            float own = Math.Min(reach, def.Attack.Range + def.Radius + MapConstants.CellSize);
            ai.FocusSlots[n] = slot;
            ai.FocusPoints[n] = u.Position;
            ai.FocusReach2[n] = own * own;
            ai.FocusNext[n] = ai.FocusHeads[bucket];
            ai.FocusHeads[bucket] = ++n;
            lo = Vector2.Min(lo, u.Position);
            hi = Vector2.Max(hi, u.Position);
        }
        if (n == 0) return;
        lo -= new Vector2(reach);
        hi += new Vector2(reach);
        EntityHandle best = default;
        Vector2 at = default;
        int bestHp = int.MaxValue;
        bool bestArmy = false;
        int s = -1;
        while (ai.View.NextUnit(ref s, out SeenUnit e))
        {
            if (e.Owner == ai.Player || !ai.View.IsEnemy(e.Owner)) continue;
            Vector2 p = e.Position;
            if (p.X < lo.X || p.Y < lo.Y || p.X > hi.X || p.Y > hi.Y) continue;
            bool army = !ai.IsWorkerType[e.TypeId];
            if (bestArmy && !army || army == bestArmy && e.Hp >= bestHp) continue; // slot order: a tie keeps the first
            if (!NearAFighter(ai, p)) continue;
            (best, at, bestHp, bestArmy) = (e.Handle, p, e.Hp, army);
        }
        if (best.Generation != 0)
        {
            ai.FocusTarget = best;
            for (int i = 0; i < n; i++)
            {
                int slot = ai.FocusSlots[i];
                if (Vector2.DistanceSquared(ai.FocusPoints[i], at) > ai.FocusReach2[i]) continue;
                if (ai.View.TryGetAttackOrder(slot, out EntityHandle held) && held == best) continue; // on it already
                if (ai.View.TryGetGivenUp(slot, out EntityHandle given) && given == best) continue; // it couldn't get there
                ai.View.TryGetUnit(slot, out SeenUnit u);
                if (!ai.Send(Command.Attack(ai.Player, u.Handle, best, isBuilding: false))) break; // queue full
                ai.MarkOrdered(slot, u.Handle.Generation, 0);
                if (board.WaveActive && board.WaveGeneration[slot] == u.Handle.Generation) Owe(board, slot, Focused);
            }
        }
        // Scratch: empty between thinks, so a loaded seat equals the saved one.
        for (int i = 0; i < n; i++) ai.FocusHeads[Bucket(ai, ai.FocusPoints[i])] = 0;
        Array.Clear(ai.FocusSlots, 0, n);
        Array.Clear(ai.FocusPoints, 0, n);
        Array.Clear(ai.FocusReach2, 0, n);
        Array.Clear(ai.FocusNext, 0, n);
    }

    /// <summary>
    /// Whether <paramref name="p"/> lies within the reach of one of the focus pass's fighters (<see cref="AiPlayer.FocusReach2"/>):
    /// the 3 x 3 buckets round it (a bucket is at least <c>ai.focusReach</c>, the largest reach, across), each walked to its
    /// first hit.
    /// </summary>
    private static bool NearAFighter(AiPlayer ai, Vector2 p)
    {
        int columns = ai.FocusColumns, rows = ai.FocusHeads.Length / columns;
        int bx = Math.Clamp((int)(p.X / ai.FocusBucket), 0, columns - 1), by = Math.Clamp((int)(p.Y / ai.FocusBucket), 0, rows - 1);
        for (int y = Math.Max(0, by - 1); y <= Math.Min(rows - 1, by + 1); y++)
            for (int x = Math.Max(0, bx - 1); x <= Math.Min(columns - 1, bx + 1); x++)
                for (int j = ai.FocusHeads[y * columns + x]; j != 0; j = ai.FocusNext[j - 1])
                    if (Vector2.DistanceSquared(ai.FocusPoints[j - 1], p) <= ai.FocusReach2[j - 1]) return true;
        return false;
    }

    /// <summary>The focus grid's bucket under <paramref name="p"/> (m), clamped onto the grid.</summary>
    private static int Bucket(AiPlayer ai, Vector2 p)
    {
        int columns = ai.FocusColumns, rows = ai.FocusHeads.Length / columns;
        int x = Math.Clamp((int)(p.X / ai.FocusBucket), 0, columns - 1), y = Math.Clamp((int)(p.Y / ai.FocusBucket), 0, rows - 1);
        return y * columns + x;
    }

    /// <summary>
    /// M5-4c slice 2: whether own army unit <paramref name="slot"/> is a fighter for the focus pass: free (alive, not casting,
    /// no AI command pending), not the scout, its attack able to hit units, and either a member of the wave in progress owed
    /// nothing but its re-send after a focus (<see cref="Focused"/>; its AttackMove to a new target or its Move home goes first, from <see cref="Orders"/>), or a defender while
    /// the base is defended (picked, or every unit once the whole wave was called: <see cref="DefenseManager"/>) that stands
    /// inside the base (within <c>ai.defendRadius</c> of the hall's centre, as the defense's own AttackMove rule).
    /// </summary>
    private static bool Fighter(AiPlayer ai, int slot, out SeenUnit u)
    {
        if (!Free(ai, slot, out u)) return false;
        int generation = u.Handle.Generation;
        if (ai.IsScout(slot, generation)) return false;
        UnitDef def = ai.Data.Units[u.TypeId];
        if (def.Attack.Value <= 0 || def.Attack.Targets == AttackTargets.Buildings) return false;
        Blackboard board = ai.Board;
        bool tagged = board.WaveGeneration[slot] == generation;
        if (tagged && Owed(board, slot) != 0 && Owed(board, slot) != Focused) return false;
        if (tagged && board.WaveActive) return true;
        if (!board.Defending || !board.DefendAll && board.DefenderGeneration[slot] != generation) return false;
        // A defender out of the base walks home on the defense's Move first (BUG-0670): an Attack there would keep it in
        // the enemy's fight, so it focuses only once inside (within ai.defendRadius of the hall's centre).
        float radius = ai.Rules.DefendRadius;
        return !ai.HasHall || Vector2.DistanceSquared(u.Position, ai.HallPoint) <= radius * radius;
    }

    /// <summary>Own army unit <paramref name="slot"/> as seen, if it is free for an order: alive, not casting, no AI command pending.</summary>
    internal static bool Free(AiPlayer ai, int slot, out SeenUnit u) =>
        ai.View.TryGetUnit(slot, out u) && u.State != UnitState.Casting && !ai.OrderedNotApplied(slot, u.Handle.Generation);

    /// <summary>The order owed to the member in unit slot <paramref name="slot"/>: its <see cref="Blackboard.WaveFlag"/> without the <see cref="FightsOn"/> bit.</summary>
    internal static byte Owed(Blackboard board, int slot) => (byte)(board.WaveFlag[slot] & ~FightsOn);

    /// <summary>Sets the order owed to the member in unit slot <paramref name="slot"/> (0 none, <see cref="ToAttack"/>, <see cref="Focused"/>), keeping its <see cref="FightsOn"/> bit.</summary>
    private static void Owe(Blackboard board, int slot, byte order) => board.WaveFlag[slot] = (byte)(board.WaveFlag[slot] & FightsOn | order);

    /// <summary>Whether own unit slot <paramref name="slot"/> holds a live member of the wave in progress (or just ended, not yet sent home).</summary>
    internal static bool Member(AiPlayer ai, int slot, out SeenUnit u) =>
        ai.View.TryGetUnit(slot, out u) && ai.Board.WaveGeneration[slot] == u.Handle.Generation && ai.Board.WaveFlag[slot] != ToRally;
}
