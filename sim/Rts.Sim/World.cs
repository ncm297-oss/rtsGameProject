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
    public World(SimConfig config)
    {
        config.Validate();
        Config = config;
        Units = new UnitStore(config.UnitCapacity);
        _rngs = new SimRng[RngStream.Count(config.PlayerCount)];
        for (int i = 0; i < _rngs.Length; i++)
            _rngs[i] = new SimRng(config.Seed, (ulong)i);
        Heightmap = MapGenerator.Generate(config.Map, ref _rngs[RngStream.MapGen]);
        NavGrid = new NavGrid(Heightmap);
        Spatial = new SpatialHash(config.UnitCapacity, NavGrid.Width, NavGrid.Height);
        FlowFields = new FlowFieldCache(NavGrid);
        MoveOrder = new long[config.UnitCapacity];
    }

    /// <summary>Scratch for <see cref="Movement.MovementSystem"/>: Moving units keyed by (goal cell, slot); derived, not hashed.</summary>
    internal long[] MoveOrder { get; }

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

    /// <summary>Flow fields for move targets, built on demand by movement. Derived state.</summary>
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
