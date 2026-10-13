using System;
using Rts.Sim.Determinism;

namespace Rts.Sim.Map;

/// <summary>Terrain of one map: per-cell elevation level and height in meters. Immutable after construction.</summary>
/// <remarks>
/// Cells are indexed <c>y * Width + x</c>. A plateau cell sits exactly at <c>level x LevelHeight</c>;
/// a ramp cell keeps the lower level it connects to (docs/02 "High ground") and its height lies
/// strictly between that level and the next, so "ramp" is read from the height, not stored twice.
/// </remarks>
public sealed class Heightmap
{
    private readonly byte[] _levels;
    private readonly float[] _elevations;
    private readonly StartLocation[] _starts;

    /// <summary>Builds a heightmap from row-major arrays (copied); throws if sizes or values are out of range.</summary>
    /// <param name="width">Width in cells.</param>
    /// <param name="height">Height in cells.</param>
    /// <param name="levels">Per-cell levels.</param>
    /// <param name="elevations">Per-cell heights in meters.</param>
    /// <param name="startLocations">The map's start locations in player order (M5-4a; copied); each must name a cell of its level. Empty for none.</param>
    public Heightmap(int width, int height, ReadOnlySpan<byte> levels, ReadOnlySpan<float> elevations,
        ReadOnlySpan<StartLocation> startLocations = default)
    {
        if (width < 1) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 1) throw new ArgumentOutOfRangeException(nameof(height));
        long cells = (long)width * height; // int multiplication would wrap and accept empty arrays (BUG-0012)
        if (cells > Array.MaxLength) throw new ArgumentOutOfRangeException(nameof(width), $"{width} x {height} cells is too large");
        int n = (int)cells;
        if (levels.Length != n) throw new ArgumentException($"expected {n} levels, got {levels.Length}", nameof(levels));
        if (elevations.Length != n) throw new ArgumentException($"expected {n} elevations, got {elevations.Length}", nameof(elevations));
        for (int i = 0; i < n; i++)
        {
            float floor = levels[i] * MapConstants.LevelHeight;
            bool ok = levels[i] <= MapConstants.MaxLevel && elevations[i] >= floor
                && elevations[i] < floor + MapConstants.LevelHeight
                && (elevations[i] == floor || levels[i] < MapConstants.MaxLevel); // nothing above the top level to ramp to
            if (!ok) throw new ArgumentException($"cell {i}: level {levels[i]} with elevation {elevations[i]} m is invalid");
        }
        Width = width;
        Height = height;
        _levels = levels.ToArray();
        _elevations = elevations.ToArray();
        foreach (StartLocation st in startLocations)
        {
            if ((uint)st.Cell >= (uint)n || levels[st.Cell] != st.Level)
                throw new ArgumentException($"start location {st} is off the map or not on its cell's level", nameof(startLocations));
        }
        _starts = startLocations.ToArray();
    }

    // The generator's copy with start locations: shares the (immutable) terrain arrays.
    private Heightmap(Heightmap terrain, StartLocation[] starts)
    {
        Width = terrain.Width;
        Height = terrain.Height;
        _levels = terrain._levels;
        _elevations = terrain._elevations;
        _starts = starts;
    }

    /// <summary>This terrain with <paramref name="starts"/> as its start locations (the generator checked them).</summary>
    internal Heightmap WithStartLocations(StartLocation[] starts) => new(this, starts);

    /// <summary>
    /// The start locations the generator chose (M5-4a), in player order; empty for a hand-built map or
    /// <see cref="MapGenParams.StartLocations"/> 0. Not in <see cref="ContentHash"/> or the state hash: like the terrain,
    /// they follow from seed and params.
    /// </summary>
    public ReadOnlySpan<StartLocation> StartLocations => _starts;

    /// <summary>Width in cells.</summary>
    public int Width { get; }

    /// <summary>Height in cells.</summary>
    public int Height { get; }

    /// <summary>Elevation level (0..<see cref="MapConstants.MaxLevel"/>) of a cell; ramps report their lower level.</summary>
    public int LevelAt(int x, int y) => _levels[Index(x, y)];

    /// <summary>Ground height of a cell's center in meters.</summary>
    public float ElevationAt(int x, int y) => _elevations[Index(x, y)];

    /// <summary>True if the cell slopes up toward the next level.</summary>
    public bool IsRamp(int x, int y)
    {
        int i = Index(x, y);
        return _elevations[i] > _levels[i] * MapConstants.LevelHeight;
    }

    /// <summary>Row-major levels, for building the nav grid and for tests.</summary>
    public ReadOnlySpan<byte> Levels => _levels;

    /// <summary>The row-major level array itself, for the fog's per-cell compares (M4-3a); the sim never writes it.</summary>
    internal byte[] LevelArray => _levels;

    /// <summary>Row-major elevations in meters.</summary>
    public ReadOnlySpan<float> Elevations => _elevations;

    /// <summary>FNV-1a hash of size, levels and exact elevation bits; equal maps hash equal in every run.</summary>
    public ulong ContentHash()
    {
        var h = new StateHasher();
        h.Add(Width);
        h.Add(Height);
        for (int i = 0; i < _levels.Length; i++)
        {
            h.Add((int)_levels[i]);
            h.Add(_elevations[i]);
        }
        return h.Value;
    }

    private int Index(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            throw new ArgumentOutOfRangeException(nameof(x), $"cell ({x}, {y}) is outside the {Width} x {Height} map");
        return y * Width + x;
    }
}
