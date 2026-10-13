using System;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;

namespace Rts.Sim.Ai;

/// <summary>
/// One AI seat (M5-2, docs/03 "Implementation (M5-2)"): every <see cref="ThinkTicks"/> ticks it reads its own
/// <see cref="PlayerView"/> and sends commands through an <see cref="ICommandSink"/> (the sim's command door), never touching
/// the world. A think runs the faction's <c>ai.json</c> build order (<see cref="BuildOrderExecutor"/>), the scout
/// (<see cref="ScoutManager"/>, M5-3), the economy (<see cref="EconomyManager"/>: idle workers to gold and wood, houses before
/// the cap), production (<see cref="ProductionManager"/>: the composition, when the build order sent nothing), then the army
/// (<see cref="MilitaryManager"/>, M5-3: rally, attack waves, retreat).
/// </summary>
/// <remarks>
/// Built by the sim for each AI seat with a kept view and its own <see cref="SimRng"/> (seed and player). Its state (the
/// <see cref="Blackboard"/>, the census, the RNG) is not sim state: not hashed, and a replay plays back without it (the AI's
/// commands are in the log). Allocates nothing after construction: every buffer is sized from the match here.
/// </remarks>
internal sealed class AiPlayer
{
    private readonly ICommandSink _sink;
    private SimRng _rng;
    private bool _queueFull;

    /// <summary>Builds the AI of <paramref name="view"/>'s player with <paramref name="def"/> (its faction's <c>ai.json</c>) at <paramref name="difficulty"/>.</summary>
    internal AiPlayer(PlayerView view, GameData data, AiDef def, AiDifficulty difficulty, SimRng rng, ICommandSink sink)
    {
        View = view ?? throw new ArgumentNullException(nameof(view));
        Data = data ?? throw new ArgumentNullException(nameof(data));
        Def = def ?? throw new ArgumentNullException(nameof(def));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _rng = rng;
        Player = view.Player;
        Faction = view.Faction;
        Knobs = def.Difficulty(difficulty);
        Rules = data.AiRules;
        ThinkTicks = Math.Max(1, Knobs.ThinkTicks);
        Board = new Blackboard(def.BuildOrder.Length, view.UnitSlots, view.BuildingSlots, view.ResourceSlots,
            ScoutManager.GridLines(view.MapWidth, Rules.ScoutGrid) * ScoutManager.GridLines(view.MapHeight, Rules.ScoutGrid));
        Live = new int[data.Units.Length];
        Queued = new int[data.Units.Length];
        WorkerSlots = new int[view.UnitSlots];
        ArmySlots = new int[view.UnitSlots];
        OwnBuildingSlots = new int[view.BuildingSlots];
        OwnBuildingTypes = new int[view.BuildingSlots];
        OwnBuildingCells = new int[view.BuildingSlots];
        OwnBuildingSites = new bool[view.BuildingSlots];
        TownHallType = OfSlot(BuildingSlot.TownHall);
        HouseType = OfSlot(BuildingSlot.House);
        CampType = OfSlot(BuildingSlot.Camp);
        IsWorkerType = new bool[data.Units.Length];
        for (int t = 0; t < data.Units.Length; t++) IsWorkerType[t] = data.Units[t].Slot == UnitSlot.Worker;
    }

    /// <summary>The player it plays.</summary>
    internal int Player { get; }

    /// <summary>The faction it plays.</summary>
    internal int Faction { get; }

    /// <summary>Ticks between thinks (its difficulty's <c>thinkInterval</c>): it thinks when the tick number is a multiple.</summary>
    internal int ThinkTicks { get; }

    /// <summary>Its window on the match.</summary>
    internal PlayerView View { get; }

    /// <summary>The match's data (load-time, read-only).</summary>
    internal GameData Data { get; }

    /// <summary>Its faction's <c>ai.json</c>.</summary>
    internal AiDef Def { get; }

    /// <summary>Its difficulty's knobs.</summary>
    internal AiDifficultyDef Knobs { get; }

    /// <summary>The generic AI tunables (<c>rules.json</c> <c>ai</c>).</summary>
    internal AiRulesDef Rules { get; }

    /// <summary>Its bookkeeping.</summary>
    internal Blackboard Board { get; }

    /// <summary>Its own random stream (seed and player; M5-3's managers draw from it). Not the world's: drawing moves no hashed stream.</summary>
    internal ref SimRng Rng => ref _rng;

    /// <summary>Thinks completed (for tests and tooling).</summary>
    internal int Thinks { get; private set; }

    /// <summary>Commands it has had accepted at the door (for tests and tooling).</summary>
    internal int CommandsSent { get; private set; }

    /// <summary>Test seam: told of every command accepted at the door, stamped with the tick it applies in. Null in a match.</summary>
    internal Action<Command>? SentForTests { get; set; }

    // ---------- the census, rebuilt at the start of every think ----------

    /// <summary>The tick number of the current think.</summary>
    internal int Now { get; private set; }

    /// <summary>Per unit type, the player's live units.</summary>
    internal int[] Live { get; }

    /// <summary>Per unit type, the units in the player's queues (the head in progress included).</summary>
    internal int[] Queued { get; }

    /// <summary>Per unit type, whether it is a worker (<see cref="UnitSlot.Worker"/>).</summary>
    internal bool[] IsWorkerType { get; }

    /// <summary>The player's live workers' unit slots, slot order (first <see cref="WorkerCount"/>).</summary>
    internal int[] WorkerSlots { get; }

    /// <summary>How many live workers the player has.</summary>
    internal int WorkerCount { get; private set; }

    /// <summary>M5-3: the player's live non-worker units' unit slots, slot order (first <see cref="ArmyCount"/>).</summary>
    internal int[] ArmySlots { get; }

    /// <summary>How many live non-worker units the player has.</summary>
    internal int ArmyCount { get; private set; }

    /// <summary>M5-3 (BUG-0574): whether the player has a finished drop-off building.</summary>
    internal bool HasDropOff { get; private set; }

    /// <summary>The player's buildings (sites included), slot order (first <see cref="OwnBuildingCount"/>): slot, type, anchor and whether a site.</summary>
    internal int[] OwnBuildingSlots { get; }

    /// <summary>See <see cref="OwnBuildingSlots"/>.</summary>
    internal int[] OwnBuildingTypes { get; }

    /// <summary>See <see cref="OwnBuildingSlots"/>.</summary>
    internal int[] OwnBuildingCells { get; }

    /// <summary>See <see cref="OwnBuildingSlots"/>.</summary>
    internal bool[] OwnBuildingSites { get; }

    /// <summary>How many buildings the player has.</summary>
    internal int OwnBuildingCount { get; private set; }

    /// <summary>The player's Town Hall the AI builds round: its first finished one, else its first site; -1 for none.</summary>
    internal int TownHallCell { get; private set; }

    /// <summary>The faction's Town Hall, House and Camp building types (-1 when the data has none).</summary>
    internal int TownHallType { get; }

    /// <summary>See <see cref="TownHallType"/>.</summary>
    internal int HouseType { get; }

    /// <summary>See <see cref="TownHallType"/>.</summary>
    internal int CampType { get; }

    /// <summary>Gold and wood left this think after the commands sent so far (the view's amounts at its start).</summary>
    internal int Gold { get; private set; }

    /// <summary>See <see cref="Gold"/>.</summary>
    internal int Wood { get; private set; }

    /// <summary>Whether the build-order executor sent a command this think (the production manager then waits).</summary>
    internal bool ExecutorSent { get; set; }

    /// <summary>
    /// One think if the tick number is a multiple of <see cref="ThinkTicks"/>, at the start of the tick before phase 1 (the
    /// sim calls it every tick for each AI seat in player order); true when it thought.
    /// </summary>
    internal bool Think()
    {
        int now = View.TickNumber;
        if (now % ThinkTicks != 0) return false;
        Now = now;
        Gold = View.Gold;
        Wood = View.Wood;
        ExecutorSent = false;
        _queueFull = false;
        _plannedCount = 0;
        Census();
        BuildOrderExecutor.Run(this);
        ScoutManager.Run(this);
        EconomyManager.Run(this);
        ProductionManager.Run(this);
        MilitaryManager.Run(this);
        Thinks++;
        return true;
    }

    private void Census()
    {
        Array.Clear(Live);
        Array.Clear(Queued);
        WorkerCount = 0;
        ArmyCount = 0;
        OwnBuildingCount = 0;
        TownHallCell = -1;
        HasDropOff = false;
        bool hallFinished = false;
        int s = -1, enemies = 0;
        Vector2 sum = Vector2.Zero;
        while (View.NextUnit(ref s, out SeenUnit u))
        {
            if (u.Owner != Player)
            {
                // The enemy memory: the enemy army seen this think (workers aside).
                if (IsWorkerType[u.TypeId]) continue;
                enemies++;
                sum += u.Position;
                continue;
            }
            Live[u.TypeId]++;
            if (IsWorkerType[u.TypeId]) WorkerSlots[WorkerCount++] = s;
            else ArmySlots[ArmyCount++] = s;
        }
        if (enemies > 0)
        {
            Board.EnemyArmyCentroid = sum / enemies;
            Board.EnemyArmyCount = enemies;
            Board.EnemyArmyTick = Now;
        }
        int known = 0;
        s = -1;
        while (View.NextBuilding(ref s, out SeenBuilding b))
        {
            if (b.Owner != Player)
            {
                known++;
                continue;
            }
            if (!b.UnderConstruction && Data.Buildings[b.TypeId].DropOff) HasDropOff = true;
            int k = OwnBuildingCount++;
            OwnBuildingSlots[k] = s;
            OwnBuildingTypes[k] = b.TypeId;
            OwnBuildingCells[k] = b.Cell;
            OwnBuildingSites[k] = b.UnderConstruction;
            if (b.TypeId == TownHallType && (TownHallCell < 0 || !hallFinished && !b.UnderConstruction))
            {
                TownHallCell = b.Cell;
                hallFinished = !b.UnderConstruction;
            }
            int n = View.QueueCount(s);
            for (int item = 0; item < n; item++)
                if (View.TryGetQueueItem(s, item, out int type, out bool isTech) && !isTech) Queued[type]++;
        }
        s = -1;
        while (View.NextGhost(ref s, out _)) known++;
        Board.KnownEnemyBuildings = known;
    }

    // ---------- M5-3: the enemy as known, and the base ----------

    /// <summary>Whether own unit slot <paramref name="slot"/> (generation <paramref name="generation"/>) is the scout on its sweep: no other manager orders it.</summary>
    internal bool IsScout(int slot, int generation) =>
        Board.ScoutPhase == 1 && Board.ScoutSlot == slot && Board.ScoutGeneration == generation;

    /// <summary>
    /// The known enemy building nearest <paramref name="from"/> (its footprint centre; slot order on ties, seen ones before
    /// ghosts): one the player sees now, or a ghost. False when it knows none.
    /// </summary>
    internal bool NearestEnemyBuilding(Vector2 from, out EntityHandle handle, out int type, out int cell)
    {
        handle = default;
        type = cell = -1;
        float best = float.MaxValue;
        int s = -1;
        while (View.NextBuilding(ref s, out SeenBuilding b))
        {
            if (b.Owner == Player) continue;
            float d = Vector2.DistanceSquared(BuildingCentre(b.TypeId, b.Cell), from);
            if (d >= best) continue;
            best = d;
            (handle, type, cell) = (b.Handle, b.TypeId, b.Cell);
        }
        s = -1;
        while (View.NextGhost(ref s, out Vision.BuildingGhost g))
        {
            float d = Vector2.DistanceSquared(BuildingCentre(g.TypeId, g.Cell), from);
            if (d >= best) continue;
            best = d;
            (handle, type, cell) = (new EntityHandle(s, g.Generation), g.TypeId, g.Cell);
        }
        return type >= 0;
    }

    /// <summary>Whether enemy building <paramref name="building"/> is still known: seen now (<paramref name="seen"/>) or a ghost of that building.</summary>
    internal bool EnemyBuildingKnown(EntityHandle building, out bool seen)
    {
        seen = View.TryGetBuilding(building.Index, out SeenBuilding b) && b.Handle == building && b.Owner != Player;
        return seen || View.TryGetGhost(building.Index, out Vision.BuildingGhost g) && g.Generation == building.Generation;
    }

    /// <summary>The centre (m) of the player's Town Hall (as <see cref="BuildOrderExecutor.HallCentre"/> picks it, else its first building); false with no building.</summary>
    internal bool TryHallPoint(out Vector2 point)
    {
        int k = -1;
        for (int i = 0; i < OwnBuildingCount && k < 0; i++)
            if (OwnBuildingCells[i] == TownHallCell && OwnBuildingTypes[i] == TownHallType) k = i;
        if (k < 0 && OwnBuildingCount > 0) k = 0;
        point = k < 0 ? default : BuildingCentre(OwnBuildingTypes[k], OwnBuildingCells[k]);
        return k >= 0;
    }

    /// <summary>The map's size in meters.</summary>
    internal Vector2 MapSize => new(View.MapWidth * MapConstants.CellSize, View.MapHeight * MapConstants.CellSize);

    /// <summary><paramref name="p"/> clamped half a cell inside the map.</summary>
    internal Vector2 ClampToMap(Vector2 p)
    {
        const float m = MapConstants.CellSize * 0.5f;
        Vector2 size = MapSize;
        return new Vector2(Math.Clamp(p.X, m, size.X - m), Math.Clamp(p.Y, m, size.Y - m));
    }

    // ---------- sending ----------

    /// <summary>The tick a command sent this think applies in.</summary>
    internal int Stamp => Now + 1;

    /// <summary>Sends <paramref name="command"/> through the door; false (and nothing more is sent this think) when the queue is full.</summary>
    internal bool Send(in Command command)
    {
        if (_queueFull) return false;
        if (!_sink.TryEnqueue(command))
        {
            _queueFull = true;
            return false;
        }
        CommandsSent++;
        if (SentForTests != null)
        {
            Command stamped = command;
            stamped.Tick = Stamp;
            SentForTests(stamped);
        }
        return true;
    }

    /// <summary>Whether <paramref name="gold"/> and <paramref name="wood"/> fit what is left this think.</summary>
    internal bool CanAfford(int gold, int wood) => Gold >= gold && Wood >= wood;

    /// <summary>Takes a sent command's cost off what is left this think.</summary>
    internal void Spend(int gold, int wood)
    {
        Gold -= gold;
        Wood -= wood;
    }

    // ---------- shared orders ----------

    /// <summary>Whether own unit slot <paramref name="slot"/> (generation <paramref name="generation"/>) has an AI command that hasn't applied yet.</summary>
    internal bool OrderedNotApplied(int slot, int generation) =>
        Board.UnitGeneration[slot] == generation && Board.UnitOrderedUntil[slot] >= Now;

    /// <summary>Notes a command to unit slot <paramref name="slot"/> applying at <see cref="Stamp"/> (a Gather: the kind and the node it is for).</summary>
    internal void MarkOrdered(int slot, int generation, byte gatherKind, EntityHandle gatherNode = default)
    {
        Board.UnitGeneration[slot] = generation;
        Board.UnitOrderedUntil[slot] = Stamp;
        Board.GatherKind[slot] = gatherKind;
        Board.GatherNode[slot] = gatherNode;
    }

    /// <summary>
    /// Sends a Build of <paramref name="type"/> anchored at a site found round <paramref name="centreCell"/>, to an idle
    /// worker (the nearest) else the nearest gatherer; fills <paramref name="pending"/> to judge it. False when there is no
    /// site, no worker, not enough left this think, or the queue is full.
    /// </summary>
    internal bool TryBuild(int type, int centreCell, ref PendingOrder pending)
    {
        if ((uint)type >= (uint)Data.Buildings.Length || centreCell < 0) return false;
        BuildingDef def = Data.Buildings[type];
        if (!CanAfford(def.CostGold, def.CostWood)) return false;
        int anchor = FindSite(type, centreCell);
        if (anchor < 0) return false;
        Vector2 at = CellCentre(anchor);
        int worker = PickBuilder(at, out int generation);
        if (worker < 0) return false;
        if (!Send(Command.Build(Player, new EntityHandle(worker, generation), type, at))) return false;
        Spend(def.CostGold, def.CostWood);
        MarkOrdered(worker, generation, 0);
        if (_plannedCount < PlannedScratch)
        {
            _plannedTypes[_plannedCount] = type;
            _plannedCells[_plannedCount++] = anchor;
        }
        pending = new PendingOrder { Kind = PendingKind.Build, AppliesAt = Stamp, TypeId = type, Cell = anchor };
        return true;
    }

    /// <summary>A Build judged: true when an own building of its type stands (or is a site) at its anchor.</summary>
    internal bool BuildAccepted(in PendingOrder p)
    {
        for (int k = 0; k < OwnBuildingCount; k++)
            if (OwnBuildingTypes[k] == p.TypeId && OwnBuildingCells[k] == p.Cell) return true;
        return false;
    }

    /// <summary>
    /// Sends a Train of <paramref name="unitType"/> at the own finished building that trains it with room in its queue and no
    /// command of the AI waiting on it, the shortest queue first (slot order on ties); with <paramref name="emptyQueueOnly"/>
    /// only an empty queue. Fills <paramref name="pending"/> to judge it. False when none can, or not enough is left.
    /// </summary>
    internal bool TryTrain(int unitType, bool emptyQueueOnly, ref PendingOrder pending)
    {
        if ((uint)unitType >= (uint)Data.Units.Length) return false;
        UnitDef def = Data.Units[unitType];
        if (!CanAfford(def.CostGold, def.CostWood)) return false;
        int best = -1, bestQueue = int.MaxValue;
        for (int k = 0; k < OwnBuildingCount; k++)
        {
            int slot = OwnBuildingSlots[k];
            if (OwnBuildingSites[k] || OwnBuildingTypes[k] != def.TrainedAtTypeId || Board.BuildingBusyUntil[slot] >= Now) continue;
            int queue = View.QueueCount(slot);
            if (emptyQueueOnly && queue > 0 || queue >= bestQueue) continue;
            if (!View.CanTrain(slot, unitType, out _)) continue;
            best = k;
            bestQueue = queue;
        }
        if (best < 0) return false;
        int b = OwnBuildingSlots[best];
        if (!Send(Command.Train(Player, BuildingCentre(OwnBuildingTypes[best], OwnBuildingCells[best]), unitType))) return false;
        Spend(def.CostGold, def.CostWood);
        Board.BuildingBusyUntil[b] = Stamp;
        View.TryGetBuilding(b, out SeenBuilding seen);
        int ofType = 0, head = -1, n = View.QueueCount(b);
        for (int item = 0; item < n; item++)
        {
            if (!View.TryGetQueueItem(b, item, out int type, out bool isTech)) continue;
            if (item == 0) head = isTech ? -1 : type;
            if (!isTech && type == unitType) ofType++;
        }
        // The head may finish before the judging think only once its remaining ticks have run.
        int popAt = head == unitType ? Now + Math.Max(0, def.TrainTicks - View.QueueProgress(b)) : int.MaxValue;
        pending = new PendingOrder
        {
            Kind = PendingKind.Train, AppliesAt = Stamp, TypeId = unitType, Slot = b, Generation = seen.Handle.Generation,
            QueuedOfType = ofType, HeadMayPopAt = popAt,
        };
        return true;
    }

    /// <summary>
    /// A Train judged: its building still the same own building, and its queue holds one more of the type than when sent,
    /// less one if the head (of the type) may have finished since. The one case it can't tell (refused while that head had
    /// not finished) counts as accepted.
    /// </summary>
    internal bool TrainAccepted(in PendingOrder p)
    {
        if (!View.TryGetBuilding(p.Slot, out SeenBuilding b) || b.Owner != Player || b.Handle.Generation != p.Generation) return false;
        int ofType = 0, n = View.QueueCount(p.Slot);
        for (int item = 0; item < n; item++)
            if (View.TryGetQueueItem(p.Slot, item, out int type, out bool isTech) && !isTech && type == p.TypeId) ofType++;
        int mayHavePopped = Now >= p.HeadMayPopAt ? 1 : 0;
        return ofType >= p.QueuedOfType + 1 - mayHavePopped;
    }

    /// <summary>Whether tech <paramref name="tech"/> is researched or in one of the player's queues.</summary>
    internal bool TechResearchedOrQueued(int tech)
    {
        if (View.HasTech(tech)) return true;
        for (int k = 0; k < OwnBuildingCount; k++)
        {
            int slot = OwnBuildingSlots[k], n = View.QueueCount(slot);
            for (int item = 0; item < n; item++)
                if (View.TryGetQueueItem(slot, item, out int type, out bool isTech) && isTech && type == tech) return true;
        }
        return false;
    }

    /// <summary>Sends a Research of <paramref name="tech"/> at the first own finished building that may research it now; false when none may or not enough is left.</summary>
    internal bool TryResearch(int tech, ref PendingOrder pending)
    {
        if ((uint)tech >= (uint)Data.Techs.Length) return false;
        TechDef def = Data.Techs[tech];
        if (!CanAfford(def.CostGold, def.CostWood)) return false;
        for (int k = 0; k < OwnBuildingCount; k++)
        {
            int slot = OwnBuildingSlots[k];
            if (OwnBuildingSites[k] || Board.BuildingBusyUntil[slot] >= Now || !View.CanResearch(slot, tech, out _)) continue;
            if (!Send(Command.Research(Player, BuildingCentre(OwnBuildingTypes[k], OwnBuildingCells[k]), tech))) return false;
            Spend(def.CostGold, def.CostWood);
            Board.BuildingBusyUntil[slot] = Stamp;
            pending = new PendingOrder { Kind = PendingKind.Research, AppliesAt = Stamp, TypeId = tech };
            return true;
        }
        return false;
    }

    // ---------- placement ----------

    /// <summary>
    /// A site for a building of <paramref name="type"/>: the first anchor, ring by ring outward from
    /// <paramref name="centreCell"/> (Chebyshev rings up to <see cref="AiRulesDef.PlacementSearchCells"/>, each walked in a
    /// fixed order), whose footprint, centred on the ring cell, carries no retry mark, keeps <see cref="AiRulesDef.SpacingCells"/>
    /// free round the player's buildings (and this think's sites) and the gold mines it knows nearby, and passes <see cref="PlayerView.CanPlace"/>;
    /// -1 for none.
    /// </summary>
    internal int FindSite(int type, int centreCell)
    {
        // A rule no cell can pass (cost, a full building store, the type's requires) fails every candidate: don't search (BUG-0572).
        if (!View.CanPlaceAnywhere(type, out _)) return -1;
        BuildingDef def = Data.Buildings[type];
        int w = View.MapWidth, h = View.MapHeight, fw = def.FootprintWidth, fh = def.FootprintHeight;
        int cx = centreCell % w, cy = centreCell / w, radius = Rules.PlacementSearchCells;
        CollectMines(cx, cy, radius + Rules.SpacingCells + DataLimits.MaxFootprint);
        for (int r = 0; r <= radius; r++)
        {
            int ring = r == 0 ? 1 : 8 * r;
            for (int i = 0; i < ring; i++)
            {
                RingCell(cx, cy, r, i, out int x, out int y);
                int ax = x - fw / 2, ay = y - fh / 2;
                if (ax < 0 || ay < 0 || ax + fw > w || ay + fh > h) continue;
                int anchor = ay * w + ax;
                if (Board.IsBad(type, anchor) || !Spaced(ax, ay, fw, fh)) continue;
                if (View.CanPlace(type, anchor, out _)) return anchor;
            }
        }
        return -1;
    }

    /// <summary>Cell <paramref name="i"/> of the Chebyshev ring of radius <paramref name="r"/> round (cx, cy): the top row left to right, the right column down, the bottom row right to left, the left column up.</summary>
    private static void RingCell(int cx, int cy, int r, int i, out int x, out int y)
    {
        if (r == 0)
        {
            x = cx;
            y = cy;
            return;
        }
        int side = 2 * r;
        if (i < side) { x = cx - r + i; y = cy - r; return; }
        i -= side;
        if (i < side) { x = cx + r; y = cy - r + i; return; }
        i -= side;
        if (i < side) { x = cx + r - i; y = cy + r; return; }
        i -= side;
        x = cx - r;
        y = cy + r - i;
    }

    // The sites this think's Builds took (they stand in the world only once the Builds apply): the spacing rule keeps clear of them.
    private const int PlannedScratch = 16;
    private readonly int[] _plannedTypes = new int[PlannedScratch];
    private readonly int[] _plannedCells = new int[PlannedScratch];
    private int _plannedCount;

    private const int MineScratch = 16;
    private readonly int[] _mineCells = new int[MineScratch];
    private readonly int[] _mineTypes = new int[MineScratch];
    private int _mineCount;

    /// <summary>The known gold mines within <paramref name="reach"/> cells of (cx, cy), at most <see cref="MineScratch"/>, for the spacing rule.</summary>
    private void CollectMines(int cx, int cy, int reach)
    {
        _mineCount = 0;
        int w = View.MapWidth, s = -1;
        while (_mineCount < MineScratch && View.NextResource(ref s, out SeenResource node))
        {
            if (Data.Resources[node.TypeId].Resource != ResourceKind.Gold) continue;
            int dx = node.Cell % w - cx, dy = node.Cell / w - cy;
            if (Math.Abs(dx) > reach || Math.Abs(dy) > reach) continue;
            _mineCells[_mineCount] = node.Cell;
            _mineTypes[_mineCount] = node.TypeId;
            _mineCount++;
        }
    }

    /// <summary>Whether a footprint at (ax, ay) keeps the spacing ring free of the player's buildings, the sites this think's Builds took, and the nearby mines.</summary>
    private bool Spaced(int ax, int ay, int fw, int fh)
    {
        int s = Rules.SpacingCells, w = View.MapWidth;
        int x0 = ax - s, y0 = ay - s, bw = fw + 2 * s, bh = fh + 2 * s;
        for (int k = 0; k < OwnBuildingCount; k++)
        {
            BuildingDef d = Data.Buildings[OwnBuildingTypes[k]];
            if (Overlaps(OwnBuildingCells[k], d.FootprintWidth, d.FootprintHeight, x0, y0, bw, bh, w)) return false;
        }
        for (int k = 0; k < _plannedCount; k++)
        {
            BuildingDef d = Data.Buildings[_plannedTypes[k]];
            if (Overlaps(_plannedCells[k], d.FootprintWidth, d.FootprintHeight, x0, y0, bw, bh, w)) return false;
        }
        for (int k = 0; k < _mineCount; k++)
        {
            ResourceDef d = Data.Resources[_mineTypes[k]];
            if (Overlaps(_mineCells[k], d.FootprintWidth, d.FootprintHeight, x0, y0, bw, bh, w)) return false;
        }
        return true;
    }

    private static bool Overlaps(int cell, int aw, int ah, int x0, int y0, int bw, int bh, int width)
    {
        int ax = cell % width, ay = cell / width;
        return ax < x0 + bw && x0 < ax + aw && ay < y0 + bh && y0 < ay + ah;
    }

    /// <summary>The worker to send to build at <paramref name="at"/>: the nearest idle one, else the nearest gathering or returning one, never one with a command not yet applied nor the scout; -1 for none.</summary>
    internal int PickBuilder(Vector2 at, out int generation)
    {
        int best = -1, bestRank = int.MaxValue;
        float bestDistance = float.MaxValue;
        generation = 0;
        for (int k = 0; k < WorkerCount; k++)
        {
            int slot = WorkerSlots[k];
            if (!View.TryGetUnit(slot, out SeenUnit u) || OrderedNotApplied(slot, u.Handle.Generation) || IsScout(slot, u.Handle.Generation)) continue;
            int rank = u.State == UnitState.Idle ? 0 : u.State is UnitState.Gathering or UnitState.Returning ? 1 : 2;
            if (rank == 2) continue;
            float d = Vector2.DistanceSquared(u.Position, at);
            if (rank > bestRank || rank == bestRank && d >= bestDistance) continue;
            best = slot;
            bestRank = rank;
            bestDistance = d;
            generation = u.Handle.Generation;
        }
        return best;
    }

    // ---------- geometry and data ----------

    /// <summary>The centre (m) of cell <paramref name="cell"/>.</summary>
    internal Vector2 CellCentre(int cell)
    {
        int w = View.MapWidth;
        return new Vector2((cell % w + 0.5f) * MapConstants.CellSize, (cell / w + 0.5f) * MapConstants.CellSize);
    }

    /// <summary>The centre (m) of a footprint of building type <paramref name="type"/> anchored at <paramref name="anchor"/>.</summary>
    internal Vector2 BuildingCentre(int type, int anchor)
    {
        BuildingDef d = Data.Buildings[type];
        int w = View.MapWidth;
        return new Vector2((anchor % w + d.FootprintWidth * 0.5f) * MapConstants.CellSize, (anchor / w + d.FootprintHeight * 0.5f) * MapConstants.CellSize);
    }

    /// <summary>The centre (m) of a resource footprint of type <paramref name="type"/> anchored at <paramref name="anchor"/>.</summary>
    internal Vector2 NodeCentre(int type, int anchor)
    {
        ResourceDef d = Data.Resources[type];
        int w = View.MapWidth;
        return new Vector2((anchor % w + d.FootprintWidth * 0.5f) * MapConstants.CellSize, (anchor / w + d.FootprintHeight * 0.5f) * MapConstants.CellSize);
    }

    /// <summary>The cell a <paramref name="width"/> x <paramref name="height"/> footprint anchored at <paramref name="anchor"/> is centred on (what <see cref="FindSite"/> searches round).</summary>
    internal int CentreCellOf(int width, int height, int anchor)
    {
        int w = View.MapWidth;
        return (anchor / w + height / 2) * w + anchor % w + width / 2;
    }

    /// <summary>The faction's building of <paramref name="slot"/>, or -1.</summary>
    private int OfSlot(BuildingSlot slot)
    {
        foreach (BuildingDef b in Data.Buildings)
            if (b.Faction == Faction && b.Slot == slot) return b.Id;
        return -1;
    }
}
