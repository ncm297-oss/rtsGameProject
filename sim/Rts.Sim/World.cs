using Rts.Sim.Determinism;
using Rts.Sim.Entities;

namespace Rts.Sim;

/// <summary>All gameplay state of one match: entity stores, RNG streams, and the tick counter.</summary>
/// <remarks>Only systems run by <see cref="Simulation.Tick"/> may write it.</remarks>
public sealed class World
{
    private readonly SimRng[] _rngs;

    /// <summary>Creates an empty world sized from the config.</summary>
    public World(SimConfig config)
    {
        config.Validate();
        Config = config;
        Units = new UnitStore(config.UnitCapacity);
        _rngs = new SimRng[RngStream.Count(config.PlayerCount)];
        for (int i = 0; i < _rngs.Length; i++)
            _rngs[i] = new SimRng(config.Seed, (ulong)i);
    }

    /// <summary>The match setup this world was built from.</summary>
    public SimConfig Config { get; }

    /// <summary>All units.</summary>
    public UnitStore Units { get; }

    /// <summary>Number of the next tick to run (0 before the first tick).</summary>
    public int TickNumber { get; internal set; }

    /// <summary>Number of RNG streams.</summary>
    public int RngCount => _rngs.Length;

    /// <summary>The RNG for a stream id from <see cref="RngStream"/>; returned by ref so draws persist.</summary>
    public ref SimRng Rng(int stream) => ref _rngs[stream];
}
