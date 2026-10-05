using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
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

    /// <summary>Creates an empty world sized from the config, with terrain generated from the seed's map stream.</summary>
    public World(SimConfig config) : this(config, null)
    {
    }

    /// <summary>Test seam: a world on a hand-made map (null generates one from the seed, as in a match).</summary>
    internal World(SimConfig config, Heightmap? map)
    {
        config.Validate();
        Config = config;
        Units = new UnitStore(config.UnitCapacity);
        _rngs = new SimRng[RngStream.Count(config.PlayerCount)];
        for (int i = 0; i < _rngs.Length; i++)
            _rngs[i] = new SimRng(config.Seed, (ulong)i);
        Heightmap = map ?? MapGenerator.Generate(config.Map, ref _rngs[RngStream.MapGen]);
        NavGrid = new NavGrid(Heightmap);
        Spatial = new SpatialHash(config.UnitCapacity, NavGrid.Width, NavGrid.Height);
        FlowFields = new FlowFieldCache(NavGrid, FlowFieldCache.CapacityFor(config.UnitCapacity, NavGrid.Width * NavGrid.Height));
        MoveOrder = new long[config.UnitCapacity];
        FieldMisses = new long[config.UnitCapacity];
        Neighbors = new int[config.UnitCapacity];
        PlannedStep = new Vector2[config.UnitCapacity];
        PlannedAction = new byte[config.UnitCapacity];
        PlannedRemaining = new float[config.UnitCapacity];
        ShoveStep = new Vector2[config.UnitCapacity];
        ShovedGoals = new int[config.UnitCapacity];
        AnchorQueue = new int[config.UnitCapacity];
        AnchorLinked = new bool[config.UnitCapacity];
        float maxRadius = 0f, maxSpeed = 0f;
        for (int t = 0; t < config.Data.Units.Length; t++)
        {
            if (config.Data.Units[t].Radius > maxRadius) maxRadius = config.Data.Units[t].Radius;
            if (config.Data.Units[t].SpeedPerTick > maxSpeed) maxSpeed = config.Data.Units[t].SpeedPerTick;
        }
        MaxUnitRadius = maxRadius;
        MaxUnitSpeed = maxSpeed;
    }

    /// <summary>Largest unit collision radius in <see cref="Data"/>: a neighbor query of own radius plus this finds every unit that can touch.</summary>
    internal float MaxUnitRadius { get; }

    /// <summary>Largest unit speed (m/tick) in <see cref="Data"/>: no unit moves further than this in one tick.</summary>
    internal float MaxUnitSpeed { get; }

    /// <summary>Scratch for <see cref="Movement.MovementSystem"/>: the shove each standing Idle unit gets from friendly walkers this tick, summed in walk order; zero again once applied; derived, not hashed.</summary>
    internal Vector2[] ShoveStep { get; }

    /// <summary>Scratch for <see cref="Movement.MovementSystem"/>: goal cells of the units a shove moved this tick, whose groups re-check their anchors; derived, not hashed.</summary>
    internal int[] ShovedGoals { get; }

    /// <summary>Scratch for <see cref="Movement.MovementSystem"/>: the slots shoved this tick, then the anchor re-check's breadth-first queue of linked units; derived, not hashed.</summary>
    internal int[] AnchorQueue { get; }

    /// <summary>Scratch for <see cref="Movement.MovementSystem"/>'s anchor re-check: which slots are linked to their point; all false between re-checks; derived, not hashed.</summary>
    internal bool[] AnchorLinked { get; }

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

    /// <summary>Number of the next tick to run (0 before the first tick).</summary>
    public int TickNumber { get; internal set; }

    /// <summary>Number of RNG streams.</summary>
    public int RngCount => _rngs.Length;

    /// <summary>The RNG for a stream id from <see cref="RngStream"/>; returned by ref so draws persist.</summary>
    public ref SimRng Rng(int stream) => ref _rngs[stream];
}
