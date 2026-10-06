using System;
using System.Numerics;
using Rts.Sim.Map;

namespace Rts.Sim.ViewApi;

/// <summary>The minimap's pixels, one per map cell (RGBA8, row-major, row = map y): a terrain layer baked once and a unit-dot layer redrawn on demand.</summary>
/// <remarks>
/// Terrain uses the 3D mesh's palette (<see cref="TerrainMeshBuilder"/>): a ramp cell gets the ramp
/// tint, a nav-grid cliff cell (the lip above a drop, or a ramp wall) the cliff tint, any other cell
/// its level tint, and other impassable cells (map border, sealed pockets) are darkened by
/// <see cref="ImpassableShade"/>. Each live unit's dot is its cell in the owner's colour inside a
/// one-cell rim (3 x 3 cells) in <see cref="RimFor"/>'s contrasting shade, on a transparent layer:
/// the rim is what tells a lone dot from a 1-cell ramp tick or cliff lip in a similar colour
/// (BUG-0064). Every rim is drawn before any centre, so a crowd's centres are never hidden by a
/// neighbour's rim; among centres, later slots overwrite earlier ones. Keeps no reference to the
/// map or the sim; <see cref="DrawDots"/> allocates nothing.
/// </remarks>
public sealed class MinimapRaster
{
    /// <summary>Brightness factor for impassable cells that are not cliffs.</summary>
    public const float ImpassableShade = 0.6f;

    /// <summary>Rim colour (0xRRGGBB) around light dots.</summary>
    public const uint DarkRim = 0x141414;

    /// <summary>Rim colour (0xRRGGBB) around dark dots, where a dark rim would not stand out.</summary>
    public const uint LightRim = 0xE6E6E6;

    /// <summary>Cells a dot covers: its own and the eight around it.</summary>
    public const int DotCells = 9;

    private readonly uint[] _ownerRgb;
    private readonly uint[] _rimRgb;
    // Centre pixel and owner of each dot drawn last time (also what the next call clears).
    private readonly int[] _centre, _centreOwner;
    private int _drawn;

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
        _rimRgb = new uint[_ownerRgb.Length];
        for (int o = 0; o < _ownerRgb.Length; o++) _rimRgb[o] = RimFor(_ownerRgb[o]);
        _centre = new int[maxDots];
        _centreOwner = new int[maxDots];
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

    /// <summary>The rim colour for a dot of colour <paramref name="rgb"/>: <see cref="LightRim"/> when the dot is dark (Rec. 601 luma under 0.35), else <see cref="DarkRim"/>.</summary>
    public static uint RimFor(uint rgb)
    {
        int luma = 299 * (byte)(rgb >> 16) + 587 * (byte)(rgb >> 8) + 114 * (byte)rgb; // 0..255,000
        return luma < 89_250 ? LightRim : DarkRim;
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

    /// <summary>Clears the previous dots and draws one per live unit (centre cell plus rim); returns how many were drawn.</summary>
    public int DrawDots(ReadOnlySpan<bool> alive, ReadOnlySpan<Vector2> positions, ReadOnlySpan<int> owners)
    {
        byte[] d = Dots;
        int w = Width;
        // Clearing only the 3 x 3 blocks drawn last time keeps the cost proportional to units, not
        // map area. The loops are written out by hand: 2,000 dots are up to 18,000 pixel writes.
        for (int k = 0; k < _drawn; k++)
        {
            int c = _centre[k];
            if (IsInterior(c))
            {
                for (int row = c - w - 1; row <= c + w - 1; row += w)
                {
                    int i = row * 4 + 3;
                    d[i] = 0;
                    d[i + 4] = 0;
                    d[i + 8] = 0;
                }
            }
            else Block(d, c, 0u, 0);
        }
        _drawn = 0;
        int n = Math.Min(Math.Min(alive.Length, positions.Length), Math.Min(owners.Length, _centre.Length));
        // Pass 1: rims (and remember each centre). Pass 2: centres, so no rim ever covers a unit.
        for (int s = 0; s < n; s++)
        {
            if (!alive[s] || (uint)owners[s] >= (uint)_ownerRgb.Length || !TryPixelOf(positions[s], out int x, out int y)) continue;
            int c = y * w + x;
            uint rim = _rimRgb[owners[s]];
            if (IsInterior(c))
            {
                byte r = (byte)(rim >> 16), g = (byte)(rim >> 8), b = (byte)rim;
                for (int row = c - w - 1; row <= c + w - 1; row += w)
                {
                    for (int i = row * 4, end = i + 12; i < end; i += 4)
                    {
                        d[i + 3] = 255;
                        d[i] = r;
                        d[i + 1] = g;
                        d[i + 2] = b;
                    }
                }
            }
            else Block(d, c, rim, 255);
            _centre[_drawn] = c;
            _centreOwner[_drawn++] = owners[s];
        }
        for (int k = 0; k < _drawn; k++)
        {
            uint rgb = _ownerRgb[_centreOwner[k]];
            int i = _centre[k] * 4;
            d[i + 3] = 255;
            d[i] = (byte)(rgb >> 16);
            d[i + 1] = (byte)(rgb >> 8);
            d[i + 2] = (byte)rgb;
        }
        return _drawn;
    }

    // True when the whole 3 x 3 block around pixel c is on the raster.
    private bool IsInterior(int c)
    {
        int y = c / Width, x = c - y * Width;
        return x > 0 && y > 0 && x < Width - 1 && y < Height - 1;
    }

    // Fills the 3 x 3 block around pixel c, clipped to the raster (dots on the map's edge).
    private void Block(byte[] d, int c, uint rgb, byte alpha)
    {
        int y = c / Width, x = c - y * Width;
        int x0 = x > 0 ? x - 1 : 0, x1 = x < Width - 1 ? x + 1 : x;
        int y0 = y > 0 ? y - 1 : 0, y1 = y < Height - 1 ? y + 1 : y;
        for (int ry = y0; ry <= y1; ry++)
            for (int rx = x0; rx <= x1; rx++) Set(d, (ry * Width + rx) * 4, rgb, alpha);
    }

    private static void Set(byte[] d, int i, uint rgb, byte alpha)
    {
        d[i + 3] = alpha; // highest index first: one bounds check covers the other three in optimised code
        d[i] = (byte)(rgb >> 16);
        d[i + 1] = (byte)(rgb >> 8);
        d[i + 2] = (byte)rgb;
    }
}
