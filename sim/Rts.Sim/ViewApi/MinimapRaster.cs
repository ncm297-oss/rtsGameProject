using System;
using System.Collections.Immutable;
using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Map;

namespace Rts.Sim.ViewApi;

/// <summary>The minimap's pixels, one per map cell (RGBA8, row-major, row = map y): a terrain layer baked once, a resource layer redrawn when passability changes, and a unit-dot layer redrawn on demand.</summary>
/// <remarks>
/// Terrain uses the 3D mesh's palette (<see cref="TerrainMeshBuilder"/>): a ramp cell gets the ramp
/// tint, a nav-grid cliff cell (the lip above a drop, or a ramp wall) the cliff tint, any other cell
/// its level tint, and other impassable cells (map border, sealed pockets) are darkened by
/// <see cref="ImpassableShade"/>; a cell blocked only by a resource node keeps its ground colour, since
/// the node is drawn on the resource layer and the ground shows again once it is gone (M2-3b). Each live unit's dot is
/// a 2 x 2 block in the owner's colour (the four cells around the cell corner nearest the unit, its own cell among them)
/// inside a one-cell rim (4 x 4 cells) in <see cref="RimFor"/>'s contrasting shade, on a transparent layer: the rim is
/// what tells a lone dot from a 1-cell ramp tick or cliff lip in a similar colour (BUG-0064), and the 2 x 2 centre
/// keeps the owner's colour readable at the shipped 220 px for 128 cells (BUG-0069). Every rim is drawn before any
/// centre, so a crowd's centres are never hidden by a neighbour's rim; among centres later slots overwrite earlier
/// ones, and each unit's own cell is drawn last, so it always shows a unit's colour. Keeps no reference to the
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

    /// <summary>Cells a lone dot covers away from the map edge: a 2 x 2 centre inside a one-cell rim (4 x 4).</summary>
    public const int DotCells = 16;

    /// <summary>Resource-layer colour (0xRRGGBB) of a node yielding wood (trees): dark green.</summary>
    public const uint WoodRgb = 0x1E5A1E;

    /// <summary>Resource-layer colour (0xRRGGBB) of a node yielding gold (mines): gold.</summary>
    public const uint GoldRgb = 0xE6B422;

    private readonly uint[] _ownerRgb;
    private readonly uint[] _rimRgb;
    // The dot layer as one 32-bit RGBA8 word per pixel (copied to Dots after each draw: a quarter of the writes),
    // and each owner's centre and rim colour as such a word.
    private readonly uint[] _dotPx;
    private readonly uint[] _ownerPx, _rimPx;
    // Centre (top-left of the 2 x 2), own cell and owner of each dot drawn last time (the centres are also what the next call clears).
    private readonly int[] _centre, _own, _centreOwner;
    // The unit slot and position of each dot drawn last time (BUG-0281 item 3: the minimap click picks among these).
    private readonly int[] _dotSlot;
    private readonly Vector2[] _dotPos;
    private int _drawn;
    // Pixels the resource layer painted last time: what the next fill clears.
    private readonly int[] _resourcePixels;
    private int _resourceCount;

    /// <summary>Width in pixels (= map cells).</summary>
    public int Width { get; }

    /// <summary>Height in pixels (= map cells).</summary>
    public int Height { get; }

    /// <summary>Terrain layer, Width x Height x 4 bytes.</summary>
    public byte[] Terrain { get; }

    /// <summary>Unit-dot layer, same size; transparent where no unit stands.</summary>
    public byte[] Dots { get; }

    /// <summary>Resource layer, same size, drawn between terrain and dots; transparent where no node stands.</summary>
    public byte[] Resources { get; }

    /// <summary>
    /// Fog layer (M4-V4), same size, drawn over the resources and under the dots: black, with alpha
    /// <see cref="FogView.MinimapAlpha"/> of the cell's fog state (opaque unexplored, 60 % explored, clear visible).
    /// Opaque black until the first <see cref="DrawFog"/>.
    /// </summary>
    public byte[] Fog { get; }

    /// <summary>Times the fog layer was redrawn (test and debug readout).</summary>
    public int FogDraws { get; private set; }

    /// <summary>The <see cref="NavGrid.Version"/> of the last resource fill; -1 before the first.</summary>
    public int ResourceVersion { get; private set; } = -1;

    /// <summary>Times the resource layer was redrawn (test and debug readout).</summary>
    public int ResourceDraws { get; private set; }

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
        Resources = new byte[Terrain.Length];
        Fog = new byte[Terrain.Length];
        for (int i = 3; i < Fog.Length; i += 4) Fog[i] = 255;
        _resourcePixels = new int[Width * Height];
        _ownerRgb = (uint[])ownerRgb.Clone();
        _rimRgb = new uint[_ownerRgb.Length];
        _ownerPx = new uint[_ownerRgb.Length];
        _rimPx = new uint[_ownerRgb.Length];
        for (int o = 0; o < _ownerRgb.Length; o++)
        {
            _rimRgb[o] = RimFor(_ownerRgb[o]);
            _ownerPx[o] = Pixel(_ownerRgb[o]);
            _rimPx[o] = Pixel(_rimRgb[o]);
        }
        _dotPx = new uint[Width * Height];
        _centre = new int[maxDots];
        _own = new int[maxDots];
        _centreOwner = new int[maxDots];
        _dotSlot = new int[maxDots];
        _dotPos = new Vector2[maxDots];
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
        bool blocked = (flags & NavFlags.Blocked) != 0 && (flags & NavFlags.Resource) == 0;
        if (blocked) c = new Vector4(c.X * ImpassableShade, c.Y * ImpassableShade, c.Z * ImpassableShade, c.W);
        return c;
    }

    /// <summary>Resource-layer colour (0xRRGGBB) of a node yielding <paramref name="kind"/>.</summary>
    public static uint ResourceRgb(ResourceKind kind) => kind == ResourceKind.Gold ? GoldRgb : WoodRgb;

    /// <summary>Redraws the resource layer from the resource store's spans if <paramref name="version"/> (a key that changes whenever the set of live nodes does) differs from the last fill; returns true if it did.</summary>
    /// <remarks>Every footprint cell of a live node is painted its kind's colour; the cells painted last time are cleared first, so a depleted node's cells show the terrain again. Allocates nothing.</remarks>
    /// <param name="types">Resource types (<c>GameData.Resources</c>): footprint and kind.</param>
    /// <param name="version">The resource-set key: the minimap passes the store's free-slot count, which rises with every node felled or mined out and nothing else (nodes are only spawned at map load), so a building's spawn, site or cancel doesn't redraw (BUG-0107).</param>
    /// <param name="alive">The resource store's <c>Alive</c>.</param>
    /// <param name="typeId">The resource store's <c>TypeId</c>.</param>
    /// <param name="cell">The resource store's <c>Cell</c> (footprint anchor).</param>
    public bool DrawResources(ImmutableArray<ResourceDef> types, int version, ReadOnlySpan<bool> alive, ReadOnlySpan<int> typeId, ReadOnlySpan<int> cell)
    {
        if (ResourceDraws > 0 && version == ResourceVersion) return false;
        byte[] d = Resources;
        for (int k = 0; k < _resourceCount; k++) Set(d, _resourcePixels[k] * 4, 0u, 0);
        _resourceCount = 0;
        int n = Math.Min(alive.Length, Math.Min(typeId.Length, cell.Length));
        int cells = Width * Height;
        for (int s = 0; s < n; s++)
        {
            int t = typeId[s], a = cell[s];
            if (!alive[s] || (uint)t >= (uint)types.Length || (uint)a >= (uint)cells) continue;
            ResourceDef def = types[t];
            uint rgb = ResourceRgb(def.Resource);
            int x0 = a % Width, y0 = a / Width;
            int x1 = Math.Min(x0 + def.FootprintWidth, Width), y1 = Math.Min(y0 + def.FootprintHeight, Height);
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    int p = y * Width + x;
                    if (d[p * 4 + 3] == 0 && _resourceCount < _resourcePixels.Length) _resourcePixels[_resourceCount++] = p;
                    Set(d, p * 4, rgb, 255);
                }
            }
        }
        ResourceVersion = version;
        ResourceDraws++;
        return true;
    }

    /// <summary>
    /// Redraws the fog layer from a player's fog bytes (<c>Fog.Visibility(player)</c> or <see cref="FogView.Texture"/>: one byte
    /// a cell, row-major, the raster's size): every pixel black with alpha <see cref="FogView.MinimapAlpha"/>. A short span
    /// leaves the cells past its end unexplored. Allocates nothing.
    /// </summary>
    public void DrawFog(ReadOnlySpan<byte> visibility)
    {
        byte[] d = Fog;
        int cells = Width * Height;
        for (int c = 0; c < cells; c++)
        {
            byte state = c < visibility.Length ? visibility[c] : (byte)0;
            d[c * 4 + 3] = FogView.MinimapAlpha(state); // RGB stay black
        }
        FogDraws++;
    }

    /// <summary>
    /// The enemy unit whose dot covers the minimap pixel of ground point <paramref name="point"/> (m), for the minimap's
    /// right-click Attack (M4-V4): a unit of another player than <paramref name="localPlayer"/> that <paramref name="shown"/>
    /// marks (the view's live and seen units: a hidden enemy has no dot, so a click there is a Move) whose 4 x 4 dot block
    /// (<see cref="CentreOf"/>'s 2 x 2 centre and its rim) holds that pixel, at its position now. The nearest to the point
    /// wins (ties to the lower slot); -1 for none or a non-finite point. Allocates nothing.
    /// </summary>
    /// <param name="shown">Per unit slot, whether its dot is drawn (<see cref="FogView.UnitShown"/>).</param>
    /// <param name="positions">The unit store's <c>Position</c>.</param>
    /// <param name="owners">The unit store's <c>Owner</c>.</param>
    public int EnemyDotAt(Vector2 point, ReadOnlySpan<bool> shown, ReadOnlySpan<Vector2> positions, ReadOnlySpan<int> owners, int localPlayer)
    {
        if (!TryPixelOf(point, out int px, out int py)) return -1;
        int n = Math.Min(shown.Length, Math.Min(positions.Length, owners.Length));
        int best = -1;
        float bestD = float.PositiveInfinity;
        for (int s = 0; s < n; s++)
        {
            if (!shown[s] || owners[s] == localPlayer || (uint)owners[s] >= (uint)_ownerRgb.Length) continue;
            Vector2 p = positions[s];
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y)) continue;
            int c = CentreOf(p);
            int cy = c / Width, cx = c - cy * Width;
            if (px < cx - 1 || px > cx + 2 || py < cy - 1 || py > cy + 2) continue;
            float d = Vector2.DistanceSquared(p, point);
            if (d < bestD)
            {
                bestD = d;
                best = s;
            }
        }
        return best;
    }

    /// <summary>Dots the last <see cref="DrawDots"/> drew.</summary>
    public int DrawnCount => _drawn;

    /// <summary>The unit slot of dot <paramref name="k"/> (0 to <see cref="DrawnCount"/> - 1) as last drawn.</summary>
    public int DrawnSlot(int k) => _dotSlot[k];

    /// <summary>
    /// The enemy dot the last <see cref="DrawDots"/> drew over the minimap pixel of ground point <paramref name="point"/>
    /// (M4-VH2, BUG-0281 item 3): <see cref="EnemyDotAt"/>'s rule (another owner than <paramref name="localPlayer"/>, the 4 x 4
    /// block holds the pixel, nearest wins, ties to the earlier dot) over the dots as drawn, at their drawn positions, so a
    /// click acts on what the minimap shows rather than on units and positions that moved since. Returns the dot's index
    /// (<see cref="DrawnSlot"/> gives its unit), or -1. Allocates nothing.
    /// </summary>
    public int DrawnEnemyDotAt(Vector2 point, int localPlayer)
    {
        if (!TryPixelOf(point, out int px, out int py)) return -1;
        int best = -1;
        float bestD = float.PositiveInfinity;
        for (int k = 0; k < _drawn; k++)
        {
            if (_centreOwner[k] == localPlayer) continue;
            int c = _centre[k];
            int cy = c / Width, cx = c - cy * Width;
            if (px < cx - 1 || px > cx + 2 || py < cy - 1 || py > cy + 2) continue;
            float d = Vector2.DistanceSquared(_dotPos[k], point);
            if (d < bestD)
            {
                bestD = d;
                best = k;
            }
        }
        return best;
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

    /// <summary>Clears the previous dots and draws one per live unit (a 2 x 2 owner-coloured centre plus rim); returns how many were drawn.</summary>
    public int DrawDots(ReadOnlySpan<bool> alive, ReadOnlySpan<Vector2> positions, ReadOnlySpan<int> owners)
    {
        uint[] d = _dotPx;
        int w = Width;
        // Clearing only the 4 x 4 blocks drawn last time keeps the cost proportional to units, not
        // map area. The loops are written out by hand: 2,000 dots are up to 32,000 rim pixel writes.
        for (int k = 0; k < _drawn; k++)
        {
            int c = _centre[k];
            if (IsInterior(c))
            {
                for (int i = c - w - 1, end = c + 3 * w - 1; i < end; i += w)
                {
                    d[i + 3] = 0; // highest index first: one bounds check covers the row
                    d[i] = 0;
                    d[i + 1] = 0;
                    d[i + 2] = 0;
                }
            }
            else Fill(d, c, -1, 4, 0u);
        }
        _drawn = 0;
        int n = Math.Min(Math.Min(alive.Length, positions.Length), Math.Min(owners.Length, _centre.Length));
        // Pass 1: rims (and remember each centre). Pass 2: 2 x 2 centres, so no rim ever covers a
        // unit. Pass 3: each unit's own cell, so a neighbour's centre never hides where a unit stands.
        for (int s = 0; s < n; s++)
        {
            if (!alive[s] || (uint)owners[s] >= (uint)_ownerRgb.Length || !TryPixelOf(positions[s], out int x, out int y)) continue;
            int c = CentreOf(positions[s]);
            uint rim = _rimPx[owners[s]];
            if (IsInterior(c))
            {
                for (int i = c - w - 1, end = c + 3 * w - 1; i < end; i += w)
                {
                    d[i + 3] = rim;
                    d[i] = rim;
                    d[i + 1] = rim;
                    d[i + 2] = rim;
                }
            }
            else Fill(d, c, -1, 4, rim);
            _centre[_drawn] = c;
            _dotSlot[_drawn] = s;
            _dotPos[_drawn] = positions[s];
            _own[_drawn] = y * w + x;
            _centreOwner[_drawn++] = owners[s];
        }
        for (int k = 0; k < _drawn; k++)
        {
            int c = _centre[k];
            uint px = _ownerPx[_centreOwner[k]];
            if (IsInterior(c))
            {
                d[c + w + 1] = px;
                d[c] = px;
                d[c + 1] = px;
                d[c + w] = px;
            }
            else Fill(d, c, 0, 2, px);
        }
        for (int k = 0; k < _drawn; k++) d[_own[k]] = _ownerPx[_centreOwner[k]];
        Buffer.BlockCopy(d, 0, Dots, 0, Dots.Length);
        return _drawn;
    }

    /// <summary>Top-left pixel (row-major index) of the 2 x 2 dot centre for a unit at <paramref name="position"/> (finite): the four cells around the cell corner nearest the unit, so the unit's own cell is always one of them; clamped onto the raster.</summary>
    public int CentreOf(Vector2 position)
    {
        // The corner nearest the unit is (kx, ky); the centre is cells kx-1..kx by ky-1..ky.
        int kx = (int)Math.Clamp(MathF.Floor(position.X / MapConstants.CellSize + 0.5f), 1f, Math.Max(1, Width - 1));
        int ky = (int)Math.Clamp(MathF.Floor(position.Y / MapConstants.CellSize + 0.5f), 1f, Math.Max(1, Height - 1));
        return (ky - 1) * Width + kx - 1;
    }

    // True when the whole 4 x 4 block around centre pixel c (top-left of the 2 x 2) is on the raster.
    private bool IsInterior(int c)
    {
        int y = c / Width, x = c - y * Width;
        return x > 0 && y > 0 && x + 2 < Width && y + 2 < Height;
    }

    // Fills the size x size block whose top-left is `offset` cells (each axis) from pixel c, clipped to the raster.
    private void Fill(uint[] d, int c, int offset, int size, uint px)
    {
        int y = c / Width, x = c - y * Width;
        int x0 = Math.Max(0, x + offset), x1 = Math.Min(Width - 1, x + offset + size - 1);
        int y0 = Math.Max(0, y + offset), y1 = Math.Min(Height - 1, y + offset + size - 1);
        for (int ry = y0; ry <= y1; ry++)
            for (int rx = x0; rx <= x1; rx++) d[ry * Width + rx] = px;
    }

    // An opaque 0xRRGGBB colour as the 32-bit word whose bytes in memory are R, G, B, A (either byte order).
    private static uint Pixel(uint rgb) =>
        BitConverter.ToUInt32(new[] { (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, (byte)255 }, 0);

    private static void Set(byte[] d, int i, uint rgb, byte alpha)
    {
        d[i + 3] = alpha; // highest index first: one bounds check covers the other three in optimised code
        d[i] = (byte)(rgb >> 16);
        d[i + 1] = (byte)(rgb >> 8);
        d[i + 2] = (byte)rgb;
    }
}
