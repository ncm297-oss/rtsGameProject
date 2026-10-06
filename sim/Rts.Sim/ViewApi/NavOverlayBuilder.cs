using System;
using System.Numerics;
using Rts.Sim.Map;

namespace Rts.Sim.ViewApi;

/// <summary>The debug overlay's nav-grid layer (M2-5): one vertex-coloured quad per cell, a few cm above the drawn terrain, coloured by the cell's <see cref="NavFlags"/>.</summary>
/// <remarks>
/// Geometry (positions, triangle indices) depends only on the heightmap and is built once in the
/// constructor; colours depend on the grid's flags and are refilled by <see cref="Refresh"/> only
/// when the grid's <see cref="NavGrid.Version"/> differs from the last fill. Quads are inset by
/// <see cref="Inset"/> on every side so the terrain shows through as thin lines between cells, and
/// follow the cell's surface plane (ramps tilt). Axes as <see cref="TerrainMesh"/>: X = sim x,
/// Y = elevation, Z = sim y, clockwise front faces. Keeps no reference to the map or the grid;
/// <see cref="Refresh"/> allocates nothing.
/// </remarks>
public sealed class NavOverlayBuilder
{
    /// <summary>Height of the quads above the terrain surface, in meters (clears the terrain without z-fighting).</summary>
    public const float Lift = 0.05f;

    /// <summary>Gap between a quad's edge and its cell's edge, in meters.</summary>
    public const float Inset = 0.08f;

    /// <summary>Open ground: faint white, sRGB RGBA.</summary>
    public static readonly Vector4 PassableColor = new(1f, 1f, 1f, 0.10f);

    /// <summary>Blocked, not a cliff (map border, sealed pockets; later buildings, trees, resources): red.</summary>
    public static readonly Vector4 BlockedColor = new(0.95f, 0.12f, 0.10f, 0.45f);

    /// <summary>Blocked cliff cell (plateau lip, ramp wall): dark red.</summary>
    public static readonly Vector4 CliffColor = new(0.45f, 0.02f, 0.02f, 0.60f);

    /// <summary>Passable ramp cell: orange.</summary>
    public static readonly Vector4 RampColor = new(1f, 0.55f, 0.08f, 0.40f);

    /// <summary>Width in cells.</summary>
    public int Width { get; }

    /// <summary>Height in cells.</summary>
    public int Height { get; }

    /// <summary>Four vertices per cell, cell <c>i = y * Width + x</c> owning vertices <c>4i</c> to <c>4i + 3</c>: (x0, y0), (x1, y0), (x0, y1), (x1, y1).</summary>
    public Vector3[] Positions { get; }

    /// <summary>One sRGB RGBA colour per vertex (all four of a cell equal); transparent until the first <see cref="Refresh"/>.</summary>
    public Vector4[] Colors { get; }

    /// <summary>Two triangles per cell.</summary>
    public int[] Indices { get; }

    /// <summary>The <see cref="NavGrid.Version"/> the colours were last filled from; -1 before the first fill.</summary>
    public int BuiltVersion { get; private set; } = -1;

    /// <summary>Number of colour fills so far (test and debug readout).</summary>
    public int Builds { get; private set; }

    /// <summary>Builds the quad geometry on the drawn terrain surface of <paramref name="map"/> (only read).</summary>
    public NavOverlayBuilder(Heightmap map)
    {
        Width = map.Width;
        Height = map.Height;
        int cells = Width * Height;
        Positions = new Vector3[cells * 4];
        Colors = new Vector4[cells * 4];
        Indices = new int[cells * 6];
        const float cs = MapConstants.CellSize;
        const float t = Inset / cs; // inset as a fraction of the cell, for the corner lerp
        Span<float> c = stackalloc float[4];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int i = y * Width + x;
                TerrainHeight.CellCorners(map, x, y, c);
                float x0 = x * cs + Inset, x1 = (x + 1) * cs - Inset;
                float z0 = y * cs + Inset, z1 = (y + 1) * cs - Inset;
                // The cell's surface is bilinear in its corners (a plane on ramps), so the inset corners are a lerp.
                Positions[4 * i] = new Vector3(x0, Bilerp(c, t, t) + Lift, z0);
                Positions[4 * i + 1] = new Vector3(x1, Bilerp(c, 1f - t, t) + Lift, z0);
                Positions[4 * i + 2] = new Vector3(x0, Bilerp(c, t, 1f - t) + Lift, z1);
                Positions[4 * i + 3] = new Vector3(x1, Bilerp(c, 1f - t, 1f - t) + Lift, z1);
                // Seen from above (+Y), with +Z toward the viewer's bottom: (x0,z0) -> (x1,z0) -> (x0,z1) is clockwise.
                int v = 4 * i, k = 6 * i;
                Indices[k] = v;
                Indices[k + 1] = v + 1;
                Indices[k + 2] = v + 2;
                Indices[k + 3] = v + 2;
                Indices[k + 4] = v + 1;
                Indices[k + 5] = v + 3;
            }
        }
    }

    private static float Bilerp(ReadOnlySpan<float> c, float u, float v)
    {
        float top = c[0] * (1f - u) + c[1] * u;
        float bottom = c[2] * (1f - u) + c[3] * u;
        return top * (1f - v) + bottom * v;
    }

    /// <summary>The colour of a cell with these flags.</summary>
    /// <remarks>
    /// Any cell with <see cref="NavFlags.Blocked"/> is blocked whatever else is set (dark red if it is also a
    /// <see cref="NavFlags.Cliff"/>, else red); an unblocked cell is orange if it is a <see cref="NavFlags.Ramp"/>, else
    /// faint. Bits this class doesn't know (added by later milestones) never change the colour.
    /// </remarks>
    public static Vector4 ColorFor(NavFlags flags)
    {
        if ((flags & NavFlags.Blocked) != 0)
            return (flags & NavFlags.Cliff) != 0 ? CliffColor : BlockedColor;
        return (flags & NavFlags.Ramp) != 0 ? RampColor : PassableColor;
    }

    /// <summary>Refills the colours from <paramref name="grid"/> if its version differs from the last fill (or nothing was filled yet); returns true if it did.</summary>
    public bool Refresh(NavGrid grid)
    {
        if (grid.Width != Width || grid.Height != Height)
            throw new ArgumentException("grid size differs from the overlay's", nameof(grid));
        if (Builds > 0 && grid.Version == BuiltVersion) return false;
        Vector4[] colors = Colors;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                Vector4 col = ColorFor(grid.FlagsAt(x, y));
                int v = 4 * (y * Width + x);
                colors[v] = col;
                colors[v + 1] = col;
                colors[v + 2] = col;
                colors[v + 3] = col;
            }
        }
        BuiltVersion = grid.Version;
        Builds++;
        return true;
    }
}
