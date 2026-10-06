using System;
using System.Numerics;
using Rts.Sim.Map;

namespace Rts.Sim.ViewApi;

/// <summary>The minimap's pixels, one per map cell (RGBA8, row-major, row = map y): a terrain layer baked once and a unit-dot layer redrawn on demand.</summary>
/// <remarks>
/// Terrain uses the 3D mesh's palette (<see cref="TerrainMeshBuilder"/>): a ramp cell gets the ramp
/// tint, a nav-grid cliff cell (the lip above a drop, or a ramp wall) the cliff tint, any other cell
/// its level tint, and other impassable cells (map border, sealed pockets) are darkened by
/// <see cref="ImpassableShade"/>. Dots are opaque, one cell per live unit in its owner's colour, on
/// a transparent layer; later slots overwrite earlier ones. Keeps no reference to the map or the
/// sim; <see cref="DrawDots"/> allocates nothing.
/// </remarks>
public sealed class MinimapRaster
{
    /// <summary>Brightness factor for impassable cells that are not cliffs.</summary>
    public const float ImpassableShade = 0.6f;

    private readonly uint[] _ownerRgb;
    private readonly int[] _written;
    private int _writtenCount;

    /// <summary>Width in pixels (= map cells).</summary>
    public int Width { get; }

    /// <summary>Height in pixels (= map cells).</summary>
    public int Height { get; }

    /// <summary>Terrain layer, Width x Height x 4 bytes.</summary>
    public byte[] Terrain { get; }

    /// <summary>Unit-dot layer, same size; transparent where no unit stands.</summary>
    public byte[] Dots { get; }

    /// <summary>Bakes the terrain layer and sizes the dot layer.</summary>
    /// <param name="map">Terrain (only read).</param>
    /// <param name="grid">Passability of the same map (only read).</param>
    /// <param name="ownerRgb">Dot colour 0xRRGGBB per owner index; units of other owners draw nothing.</param>
    /// <param name="maxDots">Most units one <see cref="DrawDots"/> call can draw (the unit store's capacity).</param>
    public MinimapRaster(Heightmap map, NavGrid grid, uint[] ownerRgb, int maxDots)
    {
        Width = map.Width;
        Height = map.Height;
        Terrain = new byte[Width * Height * 4];
        Dots = new byte[Terrain.Length];
        _ownerRgb = (uint[])ownerRgb.Clone();
        _written = new int[maxDots];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                Vector4 c = CellColor(map, grid, x, y);
                int i = (y * Width + x) * 4;
                Terrain[i] = ToByte(c.X);
                Terrain[i + 1] = ToByte(c.Y);
                Terrain[i + 2] = ToByte(c.Z);
                Terrain[i + 3] = ToByte(c.W);
            }
        }
    }

    /// <summary>Terrain colour (sRGB, before byte conversion) of cell (x, y).</summary>
    public static Vector4 CellColor(Heightmap map, NavGrid grid, int x, int y)
    {
        if (map.IsRamp(x, y)) return TerrainMeshBuilder.RampColor;
        NavFlags flags = grid.FlagsAt(x, y);
        if ((flags & NavFlags.Cliff) != 0) return TerrainMeshBuilder.CliffColor;
        Vector4 c = TerrainMeshBuilder.LevelColor(map.LevelAt(x, y));
        if ((flags & NavFlags.Blocked) != 0) c = new Vector4(c.X * ImpassableShade, c.Y * ImpassableShade, c.Z * ImpassableShade, c.W);
        return c;
    }

    /// <summary>A 0..1 colour channel as a byte (rounded).</summary>
    public static byte ToByte(float channel) => (byte)MathF.Round(Math.Clamp(channel, 0f, 1f) * 255f);

    /// <summary>Pixel (cell) a ground point in meters falls in, clamped onto the map; false for non-finite points.</summary>
    public bool TryPixelOf(Vector2 position, out int x, out int y)
    {
        x = y = 0;
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y)) return false;
        x = (int)Math.Clamp(MathF.Floor(position.X / MapConstants.CellSize), 0f, Width - 1);
        y = (int)Math.Clamp(MathF.Floor(position.Y / MapConstants.CellSize), 0f, Height - 1);
        return true;
    }

    /// <summary>Clears the previous dots and draws one per live unit; returns how many were drawn.</summary>
    public int DrawDots(ReadOnlySpan<bool> alive, ReadOnlySpan<Vector2> positions, ReadOnlySpan<int> owners)
    {
        // Clearing only the pixels written last time keeps the cost proportional to units, not map area.
        for (int k = 0; k < _writtenCount; k++) Dots[_written[k] + 3] = 0;
        _writtenCount = 0;
        int n = Math.Min(Math.Min(alive.Length, positions.Length), Math.Min(owners.Length, _written.Length));
        for (int s = 0; s < n; s++)
        {
            if (!alive[s] || (uint)owners[s] >= (uint)_ownerRgb.Length || !TryPixelOf(positions[s], out int x, out int y)) continue;
            uint rgb = _ownerRgb[owners[s]];
            int i = (y * Width + x) * 4;
            Dots[i] = (byte)(rgb >> 16);
            Dots[i + 1] = (byte)(rgb >> 8);
            Dots[i + 2] = (byte)rgb;
            Dots[i + 3] = 255;
            _written[_writtenCount++] = i;
        }
        return _writtenCount;
    }
}
