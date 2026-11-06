using System.Collections.Immutable;
using Rts.Sim.Map;

namespace Rts.Sim.Data;

/// <summary>
/// M6-S7: one hand-made map from <c>game/data/maps/&lt;file&gt;.json</c> (docs/03 "Implementation (M6-S7)"): terrain as a
/// text heightmap of levels 0-2 with ramps, start locations and resource nodes, picked by <see cref="SimConfig.MapId"/>.
/// The loader checks the generator's start contract on it (one level, half a map apart, the start mines and a forest near
/// each start, the map connected) and builds the terrain once (<see cref="Terrain"/>); a match builds its nav grid and
/// spawns the nodes from it at world construction. Every public field is in <see cref="GameData.ContentHash"/>.
/// </summary>
public sealed class MapDef
{
    internal MapDef()
    {
    }

    /// <summary>Index in <see cref="GameData.Maps"/> (file order: the files' names in ordinal order).</summary>
    public int Id { get; init; }

    /// <summary>The map's snake_case id (what <see cref="SimConfig.MapId"/>, a save and a replay name it by).</summary>
    public string Key { get; init; } = "";

    /// <summary>Player-facing name (the setup's map list).</summary>
    public string DisplayName { get; init; } = "";

    /// <summary>Player-facing description.</summary>
    public string Description { get; init; } = "";

    /// <summary>How many start locations it has, 2 to <see cref="DataLimits.MaxMapPlayers"/>: the most players a match on it may have.</summary>
    public int Players { get; init; }

    /// <summary>Its side in cells (square): one of <see cref="DataLimits.MapSizes"/>.</summary>
    public int Size { get; init; }

    /// <summary>Per cell, row-major (<c>y * Size + x</c>), its level 0-2 as the file's text rows give it (a ramp cell keeps its lower level).</summary>
    public ImmutableArray<byte> Levels { get; init; } = ImmutableArray<byte>.Empty;

    /// <summary>The ramps in file order.</summary>
    public ImmutableArray<MapRampDef> Ramps { get; init; } = ImmutableArray<MapRampDef>.Empty;

    /// <summary>The start locations' centre cells in player order.</summary>
    public ImmutableArray<int> Starts { get; init; } = ImmutableArray<int>.Empty;

    /// <summary>Every resource node in file order: the mines, then each forest's trees row by row; a match spawns them in this order.</summary>
    public ImmutableArray<MapNodeDef> Nodes { get; init; } = ImmutableArray<MapNodeDef>.Empty;

    /// <summary>How many forests the file lists (for <see cref="World.ResourcePlacement"/>).</summary>
    public int ForestCount { get; init; }

    /// <summary>The terrain built from <see cref="Levels"/> and <see cref="Ramps"/> with <see cref="Starts"/> as its start locations; derived, immutable, shared by every match on the map.</summary>
    internal Heightmap Terrain { get; init; } = null!;
}
