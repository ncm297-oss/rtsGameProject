using System;
using System.Collections.Immutable;
using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Map;

namespace Rts.Sim.ViewApi;

/// <summary>Instance transforms of the resource-node props (M2-3b): one list per resource type, each instance at its footprint centre on the drawn terrain.</summary>
/// <remarks>
/// <see cref="Refresh"/> relists only when <see cref="NavGrid.Version"/> differs from the last fill: every
/// spawn and every node taken to 0 bumps it once, and a partly taken node looks the same. A dead node is
/// gone from its type's list (the list is compacted, in slot order), not collapsed to scale 0, so the
/// drawn instance count is the live count. Transforms are Godot's MultiMesh layout, 12 floats per
/// instance: the 3 x 4 matrix row by row (basis row, origin component), axes as <see cref="TerrainMesh"/>
/// (X = sim x, Y = elevation, Z = sim y). Rotation is a yaw about +Y from <see cref="YawOf"/>. Reads the
/// resource store only through the spans it is handed and keeps no reference to them; allocates
/// nothing after construction.
/// </remarks>
public sealed class PropLayout
{
    /// <summary>Floats per instance transform.</summary>
    public const int Stride = 12;

    private readonly int[] _width, _height;
    private readonly float[][] _transforms;
    private readonly int[][] _slots;
    private readonly int[][] _anchors;
    private readonly int[] _counts;
    private readonly bool[] _changed;
    private readonly int[] _oldCounts;

    /// <summary>Sizes one list per resource type for up to <paramref name="capacity"/> nodes (the store's capacity).</summary>
    /// <param name="types">Resource types (<c>GameData.Resources</c>); only footprints are read.</param>
    /// <param name="capacity">Most nodes the store can hold.</param>
    public PropLayout(ImmutableArray<ResourceDef> types, int capacity)
    {
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        int n = types.Length;
        _width = new int[n];
        _height = new int[n];
        _transforms = new float[n][];
        _slots = new int[n][];
        _anchors = new int[n][];
        _counts = new int[n];
        _changed = new bool[n];
        _oldCounts = new int[n];
        for (int t = 0; t < n; t++)
        {
            _width[t] = types[t].FootprintWidth;
            _height[t] = types[t].FootprintHeight;
            _transforms[t] = new float[capacity * Stride];
            _slots[t] = new int[capacity];
            _anchors[t] = new int[capacity];
        }
    }

    /// <summary>Number of resource types.</summary>
    public int TypeCount => _counts.Length;

    /// <summary>The version key of the last fill (<see cref="NavGrid.Version"/>, or <see cref="SeenResources.Version"/> under fog); -1 before the first.</summary>
    public int BuiltVersion { get; private set; } = -1;

    /// <summary>Times the lists were rebuilt (test and debug readout).</summary>
    public int Rebuilds { get; private set; }

    /// <summary>Live nodes of type <paramref name="type"/> at the last fill.</summary>
    public int CountOf(int type) => _counts[type];

    /// <summary>Instance transforms of type <paramref name="type"/>, <see cref="Stride"/> floats each.</summary>
    public ReadOnlySpan<float> TransformsOf(int type) => _transforms[type].AsSpan(0, _counts[type] * Stride);

    /// <summary>True if type <paramref name="type"/>'s list (its nodes' slots and anchors, so its transforms) changed in the last relist; a view uploads only those types (BUG-0086).</summary>
    public bool Changed(int type) => _changed[type];

    /// <summary>Store slot of each instance of type <paramref name="type"/>, parallel to <see cref="TransformsOf"/>, ascending.</summary>
    public ReadOnlySpan<int> SlotsOf(int type) => _slots[type].AsSpan(0, _counts[type]);

    /// <summary>Relists every type from the resource store's spans if the grid's version changed since the last fill (or nothing was filled yet); returns true if it did.</summary>
    /// <param name="map">Terrain the props stand on (only read).</param>
    /// <param name="grid">The sim's grid: its width and <see cref="NavGrid.Version"/> (only read).</param>
    /// <param name="alive">The resource store's <c>Alive</c>.</param>
    /// <param name="typeId">The resource store's <c>TypeId</c>.</param>
    /// <param name="cell">The resource store's <c>Cell</c> (footprint anchor, lowest x, y).</param>
    public bool Refresh(Heightmap map, NavGrid grid, ReadOnlySpan<bool> alive, ReadOnlySpan<int> typeId, ReadOnlySpan<int> cell) =>
        Refresh(map, grid.Width, grid.Version, alive, typeId, cell);

    /// <summary>
    /// Relists every type from the given spans if <paramref name="version"/> differs from the last fill's (or nothing was
    /// filled yet); returns true if it did. With fog the view hands it <see cref="SeenResources"/>' last-seen copy and its
    /// <see cref="SeenResources.Version"/> (M4-VH2, BUG-0281 item 1), so a tree felled out of sight stays listed and a
    /// building change relists nothing (BUG-0126 item 3).
    /// </summary>
    public bool Refresh(Heightmap map, int gridWidth, int version, ReadOnlySpan<bool> alive, ReadOnlySpan<int> typeId, ReadOnlySpan<int> cell)
    {
        if (Rebuilds > 0 && version == BuiltVersion) return false;
        // Each type's list is compared with the last one as it is rewritten: same slots at the same anchors, in the
        // same order and count, means the same transforms (the terrain never changes).
        for (int t = 0; t < _counts.Length; t++) _changed[t] = Rebuilds == 0;
        int types = _counts.Length;
        int[] oldCounts = _oldCounts;
        Array.Copy(_counts, oldCounts, types);
        Array.Clear(_counts);
        int n = Math.Min(alive.Length, Math.Min(typeId.Length, cell.Length));
        int w = gridWidth;
        for (int s = 0; s < n; s++)
        {
            int t = typeId[s];
            if (!alive[s] || (uint)t >= (uint)_counts.Length || cell[s] < 0) continue;
            int k = _counts[t];
            if (k >= _slots[t].Length) continue; // a store larger than the layout was sized for
            int fw = _width[t], fh = _height[t];
            Vector3 o = CentreOf(map, w, cell[s], fw, fh);
            float yaw = YawOf(cell[s], fw, fh);
            float c = SimMath.Cos(yaw), si = SimMath.Sin(yaw);
            if (fw != 1 || fh != 1) Snap(ref c, ref si);
            // Yaw about +Y: basis columns X = (c, 0, -s), Y = (0, 1, 0), Z = (s, 0, c); stored row by row.
            float[] b = _transforms[t];
            int i = k * Stride;
            b[i] = c; b[i + 1] = 0f; b[i + 2] = si; b[i + 3] = o.X;
            b[i + 4] = 0f; b[i + 5] = 1f; b[i + 6] = 0f; b[i + 7] = o.Y;
            b[i + 8] = -si; b[i + 9] = 0f; b[i + 10] = c; b[i + 11] = o.Z;
            if (k >= oldCounts[t] || _slots[t][k] != s || _anchors[t][k] != cell[s]) _changed[t] = true;
            _slots[t][k] = s;
            _anchors[t][k] = cell[s];
            _counts[t] = k + 1;
        }
        for (int t = 0; t < types; t++) if (_counts[t] != oldCounts[t]) _changed[t] = true;
        BuiltVersion = version;
        Rebuilds++;
        return true;
    }

    /// <summary>Godot-axis position of a footprint's centre on the drawn terrain: (x, elevation, y) in meters.</summary>
    /// <param name="map">Terrain (only read).</param>
    /// <param name="gridWidth">Map width in cells, to split the anchor index.</param>
    /// <param name="anchor">Footprint anchor cell (<c>y * width + x</c>, lowest x, y).</param>
    /// <param name="width">Footprint width in cells.</param>
    /// <param name="height">Footprint height in cells.</param>
    public static Vector3 CentreOf(Heightmap map, int gridWidth, int anchor, int width, int height)
    {
        float x = (anchor % gridWidth + width * 0.5f) * MapConstants.CellSize;
        float y = (anchor / gridWidth + height * 0.5f) * MapConstants.CellSize;
        return new Vector3(x, TerrainHeight.At(map, x, y), y);
    }

    /// <summary>A prop's yaw in radians, from a multiplicative hash of its anchor cell (variety without RNG, stable across relists).</summary>
    /// <remarks>
    /// A one-cell footprint takes any of 256 angles. A larger footprint turns only by whole quarter turns
    /// (square) or half turns (oblong), so a mesh that fills its footprint never pokes out of it.
    /// </remarks>
    public static float YawOf(int anchor, int width, int height)
    {
        uint h = unchecked((uint)anchor * 2654435761u); // Knuth's golden-ratio multiplier; the top bits mix best
        if (width == 1 && height == 1) return (h >> 24) * (2f * MathF.PI / 256f);
        if (width == height) return (h >> 30) * (MathF.PI / 2f);
        return (h >> 31) * MathF.PI;
    }

    // Quarter and half turns get exact 0 / +-1 entries, so a footprint-filling box stays axis-aligned.
    private static void Snap(ref float c, ref float s)
    {
        c = MathF.Round(c);
        s = MathF.Round(s);
    }
}
