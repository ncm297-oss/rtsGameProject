using System;
using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.Spatial;

namespace Rts.Sim;

/// <summary>All gameplay state of one match: terrain, entity stores, RNG streams, and the tick counter.</summary>
/// <remarks>Only systems run by <see cref="Simulation.Tick"/> may write it.</remarks>
public sealed class World
{
    private readonly SimRng[] _rngs;
    private readonly PlayerLedger _ledger;
    // Per level: min x, min y, max x, max y of its cells (min > max for a level with none); terrain only, never changes.
    private readonly int[] _levelBounds;

    /// <summary>Creates an empty world sized from the config, with terrain generated from the seed's map stream.</summary>
    public World(SimConfig config) : this(config, null)
    {
    }

    /// <summary>Test seam: a world on a hand-made map (null generates one from the seed, as in a match).</summary>
    internal World(SimConfig config, Heightmap? map)
    {
        config.Validate();
        Config = config;
        _ledger = new PlayerLedger(config.PlayerCount, config.Data.Rules);
        Units = new UnitStore(config.UnitCapacity, _ledger);
        _rngs = new SimRng[RngStream.Count(config.PlayerCount)];
        for (int i = 0; i < _rngs.Length; i++)
            _rngs[i] = new SimRng(config.Seed, (ulong)i);
        HasGeneratedMap = map == null;
        Heightmap = map ?? MapGenerator.Generate(config.Map, ref _rngs[RngStream.MapGen]);
        NavGrid = new NavGrid(Heightmap);
        // Resources before the flow-field cache, so its step mask starts from the grid with them.
        // The placer draws from the map stream after the terrain did, and nothing when counts are 0.
        Resources = new ResourceStore(config.ResourceCapacity, NavGrid, config.Data);
        ResourcePlacement = ResourcePlacer.Place(config.Map, NavGrid, Resources, config.Data, ref _rngs[RngStream.MapGen]);
        Buildings = new BuildingStore(config.BuildingCapacity, NavGrid, config.Data, _ledger);
        SiteWorkers = new int[config.BuildingCapacity];
        PushedUnits = new int[config.UnitCapacity];
        _levelBounds = LevelBoundsOf(NavGrid);
        SpawnCacheCleared = new bool[_levelBounds.Length / 4];
        Spatial = new SpatialHash(config.UnitCapacity, NavGrid.Width, NavGrid.Height);
        FlowFields = new FlowFieldCache(NavGrid, FlowFieldCache.CapacityFor(config.UnitCapacity, NavGrid.Width * NavGrid.Height));
        Seal = new SealCheck(NavGrid, FlowFields.BuildScratch);
        NavGrid.ShareScratch(FlowFields.BuildScratch); // the pocket rule's flood (BUG-0093)
        MoveOrder = new long[config.UnitCapacity];
        FieldMisses = new long[config.UnitCapacity];
        FieldRefreshes = new long[config.UnitCapacity];
        Neighbors = new int[config.UnitCapacity];
        PlannedStep = new Vector2[config.UnitCapacity];
        PlannedAction = new byte[config.UnitCapacity];
        PlannedRemaining = new float[config.UnitCapacity];
        ShoveStep = new Vector2[config.UnitCapacity];
        ShoveNeighbors = new int[config.UnitCapacity];
        ShovedGoals = new int[config.UnitCapacity];
        AnchorQueue = new int[config.UnitCapacity];
        AnchorLinked = new bool[config.UnitCapacity];
        BackedOffStops = new int[config.UnitCapacity];
        WallNormals = new Vector2[3 * config.UnitCapacity]; // a wall, plus two cone edges for an overlapped enemy
        WallLimits = new float[3 * config.UnitCapacity];
        HardWalls = new int[3 * config.UnitCapacity];
        ChainMembers = new int[Movement.MovementConstants.MaxChainShove];
        PlugMembers = new int[Movement.MovementConstants.MaxPlugCluster];
        RadiusClassOfType = new int[config.Data.Units.Length];
        RadiusClassCount = RadiusClasses(config.Data, RadiusClassOfType);
        PlugAnswers = new long[config.UnitCapacity * config.PlayerCount * RadiusClassCount];
        DetourLo = new float[config.UnitCapacity];
        DetourHi = new float[config.UnitCapacity];
        DetourWall = new int[config.UnitCapacity];
        float maxRadius = 0f, maxSpeed = 0f;
        for (int t = 0; t < config.Data.Units.Length; t++)
        {
            if (config.Data.Units[t].Radius > maxRadius) maxRadius = config.Data.Units[t].Radius;
            if (config.Data.Units[t].SpeedPerTick > maxSpeed) maxSpeed = config.Data.Units[t].SpeedPerTick;
        }
        MaxUnitRadius = maxRadius;
        MaxUnitSpeed = maxSpeed;
        // The placer's closings happened before any unit existed; only later ones reset progress marks.
        SeenBlockVersion = NavGrid.BlockVersion;
    }

    /// <summary>Scratch for the never-seal placement rule (M3-3); derived, not hashed.</summary>
    internal SealCheck Seal { get; }

    /// <summary>Scratch for <see cref="ConstructionSystem"/>: per building slot, the workers in reach this tick (-1: its workers stop); derived, not hashed.</summary>
    internal int[] SiteWorkers { get; }

    /// <summary>Scratch for <see cref="ProductionSystem"/>: per terrain level, whether this tick's spawns have cleared the level's box of the free-cell cache; derived, not hashed.</summary>
    internal bool[] SpawnCacheCleared { get; }

    /// <summary>Scratch for <see cref="ConstructionSystem"/>'s push-out: the units a new site sets down, slot order; derived, not hashed.</summary>
    internal int[] PushedUnits { get; }

    /// <summary>The faction <paramref name="player"/> plays: player p plays faction <c>p mod factions</c> in id order until a lobby picks them (M6); -1 for no such player.</summary>
    public int FactionOf(int player) =>
        _ledger.Has(player) && Data.Factions.Length > 0 ? player % Data.Factions.Length : -1;

    /// <summary>
    /// Whether <paramref name="player"/> may queue a unit of <paramref name="unitTypeId"/> at building slot
    /// <paramref name="buildingSlot"/> now (M3-4): the rule <c>Command.Train</c> applies, and what the view's production
    /// card asks. Read-only and allocation-free; <paramref name="reason"/> is the first rule broken, in <see cref="TrainError"/> order.
    /// </summary>
    public bool CanTrain(int player, int buildingSlot, int unitTypeId, out TrainError reason)
    {
        reason = ProductionSystem.Check(this, player, buildingSlot, unitTypeId);
        return reason == TrainError.None;
    }

    /// <summary>
    /// The bounding box of the cells on terrain level <paramref name="level"/> (inclusive cell coordinates); false for a
    /// level with no cells. Terrain only: buildings and nodes don't change it. Bounds the push-out / spawn ring search.
    /// </summary>
    internal bool LevelBounds(int level, out int minX, out int minY, out int maxX, out int maxY)
    {
        minX = minY = maxX = maxY = 0;
        if ((uint)level >= (uint)(_levelBounds.Length / 4)) return false;
        minX = _levelBounds[4 * level];
        minY = _levelBounds[4 * level + 1];
        maxX = _levelBounds[4 * level + 2];
        maxY = _levelBounds[4 * level + 3];
        return minX <= maxX;
    }

    private static int[] LevelBoundsOf(NavGrid g)
    {
        int levels = 1;
        for (int y = 0; y < g.Height; y++)
            for (int x = 0; x < g.Width; x++) levels = Math.Max(levels, g.LevelAt(x, y) + 1);
        var b = new int[4 * levels];
        for (int l = 0; l < levels; l++)
        {
            b[4 * l] = b[4 * l + 1] = int.MaxValue;
            b[4 * l + 2] = b[4 * l + 3] = int.MinValue;
        }
        for (int y = 0; y < g.Height; y++)
        {
            for (int x = 0; x < g.Width; x++)
            {
                int l = g.LevelAt(x, y);
                b[4 * l] = Math.Min(b[4 * l], x);
                b[4 * l + 1] = Math.Min(b[4 * l + 1], y);
                b[4 * l + 2] = Math.Max(b[4 * l + 2], x);
                b[4 * l + 3] = Math.Max(b[4 * l + 3], y);
            }
        }
        return b;
    }

    /// <summary>
    /// Whether <paramref name="player"/> may place a building of <paramref name="typeId"/> with its anchor (lowest x, y)
    /// at <paramref name="anchorCell"/> now (docs/02 "Buildings" placement rule, M3-3): the rule <c>Command.Build</c>
    /// applies, and the view's placement ghost asks. Read-only and allocation-free; <paramref name="reason"/> is the
    /// first rule broken, in <see cref="PlacementError"/> order.
    /// </summary>
    public bool CanPlace(int player, int typeId, int anchorCell, out PlacementError reason)
    {
        reason = ConstructionSystem.Check(this, player, typeId, anchorCell);
        return reason == PlacementError.None;
    }

    /// <summary>True (and the amounts taken) if <paramref name="player"/> has at least <paramref name="gold"/> and <paramref name="wood"/>.</summary>
    internal bool TrySpend(int player, int gold, int wood) => _ledger.TrySpend(player, gold, wood);

    /// <summary>Per-player totals and population, shared with the stores (M3-4).</summary>
    internal PlayerLedger Ledger => _ledger;

    /// <summary>Largest unit collision radius in <see cref="Data"/>: a neighbor query of own radius plus this finds every unit that can touch.</summary>
    internal float MaxUnitRadius { get; }

    /// <summary>Largest unit speed (m/tick) in <see cref="Data"/>: no unit moves further than this in one tick.</summary>
    internal float MaxUnitSpeed { get; }

    /// <summary>Scratch for <see cref="Movement.MovementSystem"/>: the shove each standing Idle unit gets from friendly walkers this tick, summed in walk order; zero again once applied; derived, not hashed.</summary>
    internal Vector2[] ShoveStep { get; }

    /// <summary>Scratch for <see cref="Movement.MovementSystem"/>: a second neighbor query while <see cref="Neighbors"/> is in use (does a parked unit stand alone?); derived, not hashed.</summary>
    internal int[] ShoveNeighbors { get; }

    /// <summary>Scratch for <see cref="Movement.MovementSystem"/>: goal cells of the units a shove moved this tick, whose groups re-check their anchors; derived, not hashed.</summary>
    internal int[] ShovedGoals { get; }

    /// <summary>Scratch for <see cref="Movement.MovementSystem"/>: the slots shoved this tick, then the anchor re-check's breadth-first queue of linked units; derived, not hashed.</summary>
    internal int[] AnchorQueue { get; }

    /// <summary>Scratch for <see cref="Movement.MovementSystem"/>'s anchor re-check: which slots are linked to their point; all false between re-checks; derived, not hashed.</summary>
    internal bool[] AnchorLinked { get; }

    /// <summary>Scratch for <see cref="Movement.MovementSystem"/>: the units that gave up while backing off this tick, checked again at its end; derived, not hashed.</summary>
    internal int[] BackedOffStops { get; }

    /// <summary>Scratch for <see cref="Movement.MovementSystem"/>'s wall check: the unit normal toward each hard wall a step touches; derived, not hashed.</summary>
    internal Vector2[] WallNormals { get; }

    /// <summary>Scratch for <see cref="Movement.MovementSystem"/>'s wall check: how far a step may go along <see cref="WallNormals"/>; derived, not hashed.</summary>
    internal float[] WallLimits { get; }

    /// <summary>Scratch for <see cref="Movement.MovementSystem"/>'s wall check: which of <see cref="WallNormals"/> are hard walls (other players' standing units); derived, not hashed.</summary>
    internal int[] HardWalls { get; }

    /// <summary>Scratch for <see cref="Movement.MovementSystem"/>'s detour: each wall's blocked interval of directions, low end (radians); derived, not hashed.</summary>
    internal float[] DetourLo { get; }

    /// <summary>Scratch for the detour: each wall's blocked interval, high end (radians); derived, not hashed.</summary>
    internal float[] DetourHi { get; }

    /// <summary>Scratch for the detour: the slot of each wall whose interval is in <see cref="DetourLo"/>; derived, not hashed.</summary>
    internal int[] DetourWall { get; }

    /// <summary>Scratch for <see cref="Movement.MovementSystem"/>'s plug test: the cluster of hard units searched; derived, not hashed.</summary>
    internal int[] PlugMembers { get; }

    /// <summary>
    /// Scratch for <see cref="Movement.MovementSystem"/>'s plug test: per unit, per walker owner and
    /// radius class (<see cref="RadiusClassCount"/>), the answer worked out in pass
    /// <see cref="PlugEpoch"/> (epoch x 4 + answer bits), so each cluster is searched about once per pass
    /// (BUG-0044); derived, not hashed.
    /// </summary>
    internal long[] PlugAnswers { get; }

    /// <summary>The movement pass <see cref="PlugAnswers"/> are valid for: bumped at the start of each pass that reads them; derived, not hashed.</summary>
    internal long PlugEpoch { get; set; }

    /// <summary>Number of distinct unit radii in <see cref="Data"/> (the plug test depends on the walker's radius only).</summary>
    internal int RadiusClassCount { get; }

    /// <summary>Each unit type's index among the distinct radii, in ascending radius order.</summary>
    internal int[] RadiusClassOfType { get; }

    /// <summary>Fills <paramref name="classOfType"/> with each type's rank among the distinct radii (ascending) and returns how many there are (at least 1).</summary>
    private static int RadiusClasses(GameData data, int[] classOfType)
    {
        int n = data.Units.Length, count = 0;
        for (int t = 0; t < n; t++)
        {
            // The rank is how many distinct radii are smaller; each radius counts once, at its first type.
            int rank = 0;
            for (int s = 0; s < n; s++)
                if (data.Units[s].Radius < data.Units[t].Radius && FirstWithRadius(data, s)) rank++;
            classOfType[t] = rank;
            if (FirstWithRadius(data, t)) count++;
        }
        return Math.Max(count, 1);
    }

    /// <summary>True if no type before <paramref name="t"/> has its radius.</summary>
    private static bool FirstWithRadius(GameData data, int t)
    {
        for (int s = 0; s < t; s++)
            if (data.Units[s].Radius == data.Units[t].Radius) return false;
        return true;
    }

    /// <summary>Scratch for <see cref="Movement.MovementSystem"/>'s chain shove: the line of units one shove moves; derived, not hashed.</summary>
    internal int[] ChainMembers { get; }

    /// <summary>Scratch for <see cref="Movement.MovementSystem"/>'s neighbor queries, sized to every slot so a query is never truncated; derived, not hashed.</summary>
    internal int[] Neighbors { get; }

    /// <summary>Scratch for <see cref="Movement.MovementSystem"/>: each Moving unit's planned step, applied once every unit has planned, then each shoved unit's trimmed shove; derived, not hashed.</summary>
    internal Vector2[] PlannedStep { get; }

    /// <summary>Scratch for <see cref="Movement.MovementSystem"/>: each Moving unit's planned outcome (walk, wait, arrive, abandon); derived, not hashed.</summary>
    internal byte[] PlannedAction { get; }

    /// <summary>Scratch for <see cref="Movement.MovementSystem"/>: each walking unit's estimated path left after its planned step; derived, not hashed.</summary>
    internal float[] PlannedRemaining { get; }

    /// <summary>Scratch for <see cref="Movement.MovementSystem"/>: Moving units keyed by (goal cell, slot); derived, not hashed.</summary>
    internal long[] MoveOrder { get; }

    /// <summary>Scratch for <see cref="Movement.MovementSystem"/>'s build pass: goals without a cached field keyed by (oldest order tick, goal cell); derived, not hashed.</summary>
    internal long[] FieldMisses { get; }

    /// <summary>Scratch for <see cref="Movement.MovementSystem"/>'s build pass: goals whose usable field is stale keyed by (field version, goal cell), refreshed after the misses; derived, not hashed.</summary>
    internal long[] FieldRefreshes { get; }

    /// <summary>
    /// <see cref="NavGrid.BlockVersion"/> as the last movement pass saw it; a difference makes the next pass reset
    /// every Moving unit's progress mark (M3-2b, BUG-0077). Hashed: a grid change between ticks (a test seam, or a
    /// phase after movement) leaves it behind the grid until the next pass, and it decides that reset.
    /// </summary>
    internal int SeenBlockVersion { get; set; }

    /// <summary>False for the test seam's hand-made maps, which a replay (seed + map params) can't rebuild.</summary>
    internal bool HasGeneratedMap { get; }

    /// <summary>The match setup this world was built from.</summary>
    public SimConfig Config { get; }

    /// <summary>Unit, faction and rules definitions (<see cref="SimConfig.Data"/>).</summary>
    public GameData Data => Config.Data;

    /// <summary>Terrain levels and heights.</summary>
    public Heightmap Heightmap { get; }

    /// <summary>Ground passability derived from <see cref="Heightmap"/>.</summary>
    public NavGrid NavGrid { get; }

    /// <summary>Unit neighbour index, rebuilt by <see cref="Simulation.Tick"/> right after commands apply. Derived state.</summary>
    public SpatialHash Spatial { get; }

    /// <summary>Flow fields for move targets, built on demand by movement. Its metadata is hashed sim state (BUG-0021); outside the sim, read it only through public members.</summary>
    public FlowFieldCache FlowFields { get; }

    /// <summary>All units.</summary>
    public UnitStore Units { get; }

    /// <summary>All resource nodes (trees, gold mines). Read-only outside the sim.</summary>
    public ResourceStore Resources { get; }

    /// <summary>All buildings and construction sites (M3-3: placed by workers' <c>Build</c> commands, or the dev command <c>SpawnBuilding</c>). Read-only outside the sim.</summary>
    public BuildingStore Buildings { get; }

    /// <summary>Each player's gold, indexed by player (starts at <c>rules.json</c> <c>startingGold</c>; workers deposit into it).</summary>
    public ReadOnlySpan<int> Gold => _ledger.Gold;

    /// <summary>Each player's wood, indexed by player (starts at <c>rules.json</c> <c>startingWood</c>).</summary>
    public ReadOnlySpan<int> Wood => _ledger.Wood;

    /// <summary>
    /// Each player's population in use, in half-pop units (pop 1 = 2), indexed by player (M3-4): every live unit counts
    /// its type's <c>pop</c> from its spawn (the dev <c>SpawnUnit</c> too, past the cap), and every production item that
    /// has started reserves its unit's. Derived from units and queues (kept incrementally), so not hashed.
    /// </summary>
    public ReadOnlySpan<int> HalfPop => _ledger.HalfPop;

    /// <summary>Each player's population cap in half-pop units, indexed by player (M3-4): the <c>popProvided</c> of its finished buildings, at most <c>rules.json</c> <c>popCap</c>. Derived from the buildings, so not hashed.</summary>
    public ReadOnlySpan<int> HalfPopCap => _ledger.HalfPopCap;

    /// <summary>Adds <paramref name="amount"/> of <paramref name="kind"/> to <paramref name="player"/>'s total, saturating at <see cref="int.MaxValue"/>.</summary>
    internal void AddToTotal(int player, ResourceKind kind, int amount) => _ledger.AddToTotal(player, kind, amount);

    /// <summary>What the resource placer put on the map at construction (counts can fall short of <see cref="SimConfig.Map"/>'s request).</summary>
    public ResourcePlacement ResourcePlacement { get; }

    /// <summary>Number of the next tick to run (0 before the first tick).</summary>
    public int TickNumber { get; internal set; }

    /// <summary>Number of RNG streams.</summary>
    public int RngCount => _rngs.Length;

    /// <summary>The RNG for a stream id from <see cref="RngStream"/>; returned by ref so draws persist.</summary>
    public ref SimRng Rng(int stream) => ref _rngs[stream];
}
