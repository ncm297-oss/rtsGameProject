using System;
using Rts.Sim.Data;
using Rts.Sim.Map;

namespace Rts.Sim;

/// <summary>Match setup that sizes every sim buffer up front, so ticks never grow arrays.</summary>
/// <param name="Seed">The single seed every RNG stream derives from.</param>
/// <param name="PlayerCount">Number of players (human and AI), indexed from 0.</param>
/// <param name="UnitCapacity">Maximum number of live units; the unit store never grows past it.</param>
/// <param name="CommandCapacity">Maximum number of commands that can be pending at once.</param>
public sealed record SimConfig(ulong Seed, int PlayerCount, int UnitCapacity, int CommandCapacity)
{
    /// <summary>Loaded unit, faction and rules data; every stat the sim uses comes from here (CLAUDE.md rule 6).</summary>
    public required GameData Data { get; init; }

    /// <summary>Terrain generator and resource placer settings; defaults to the docs/02 128 x 128 map without resources.</summary>
    public MapGenParams Map { get; init; } = MapGenParams.Default;

    /// <summary>Maximum number of live resource nodes (trees, mines); the store is allocated once at this size and refuses spawns past it. Default <see cref="Entities.ResourceStore.DefaultCapacity"/> (4,096).</summary>
    public int ResourceCapacity { get; init; } = Entities.ResourceStore.DefaultCapacity;

    /// <summary>Maximum number of live buildings; the store is allocated once at this size and refuses spawns past it. Default <see cref="Entities.BuildingStore.DefaultCapacity"/> (256).</summary>
    public int BuildingCapacity { get; init; } = Entities.BuildingStore.DefaultCapacity;

    /// <summary>Throws if any size is out of range.</summary>
    public void Validate()
    {
        if (PlayerCount < 1) throw new ArgumentOutOfRangeException(nameof(PlayerCount));
        if (UnitCapacity < 1) throw new ArgumentOutOfRangeException(nameof(UnitCapacity));
        if (CommandCapacity < 1) throw new ArgumentOutOfRangeException(nameof(CommandCapacity));
        if (ResourceCapacity < 1) throw new ArgumentOutOfRangeException(nameof(ResourceCapacity));
        if (BuildingCapacity < 1) throw new ArgumentOutOfRangeException(nameof(BuildingCapacity));
        if (Data == null) throw new ArgumentNullException(nameof(Data));
        if (Map == null) throw new ArgumentNullException(nameof(Map));
        Map.Validate();
    }
}
