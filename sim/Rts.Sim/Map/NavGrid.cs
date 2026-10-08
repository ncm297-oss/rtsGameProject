using System;
using System.Numerics;

namespace Rts.Sim.Map;

/// <summary>Per-cell passability, cost and elevation level for ground movement (docs/03 "Navigation grid").</summary>
/// <remarks>
/// Built once from a <see cref="Heightmap"/>: the outer ring of cells is blocked; a cell is a cliff
/// (blocked) when a 4-neighbor is lower and is not a ramp exactly one level down; a flat cell
/// flanking a ramp along its length is a cliff too ("ramp wall", BUG-0011), so a ramp is a corridor
/// entered only at its top and foot and no passable step is steeper than 30 degrees; and passable
/// cells outside the largest 4-connected region (pockets no ramp reaches) are blocked, so every
/// passable cell is reachable from every other. Queries never allocate and treat cells outside
/// the map as blocked. Cell (x, y) covers world meters [x*CellSize, (x+1)*CellSize) on each axis.
/// </remarks>
public sealed class NavGrid
{
    private readonly byte[] _levels;
    private readonly NavFlags[] _flags;
    private readonly byte[] _cost;
    private int[]? _scratch;

    /// <summary>Derives flags, costs and levels from terrain.</summary>
    public NavGrid(Heightmap map)
    {
        Width = map.Width;
        Height = map.Height;
        int n = Width * Height;
        ReadOnlySpan<byte> levels = map.Levels;
        ReadOnlySpan<float> elevations = map.Elevations;
        _levels = levels.ToArray();
        _flags = new NavFlags[n];
        _cost = new byte[n];

        // The generator builds one of these per layout attempt, so on a 1024 x 1024 map this loop is
        // a large share of generation time (BUG-0015); hence locals instead of properties and a fast path.
        int w = Width, h = Height;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                // An inner cell with all four neighbors at its exact height is plateau ground with no
                // ramp around it (a ramp's height never equals a level's), so it gets no flags.
                if (x > 0 && y > 0 && x < w - 1 && y < h - 1)
                {
                    float e = elevations[i];
                    if (elevations[i - 1] == e && elevations[i + 1] == e && elevations[i - w] == e
                        && elevations[i + w] == e && e == levels[i] * MapConstants.LevelHeight)
                        continue;
                }
                if (IsRampCell(levels, elevations, i)) _flags[i] |= NavFlags.Ramp;
                if (x == 0 || y == 0 || x == w - 1 || y == h - 1) _flags[i] |= NavFlags.Blocked;
                if (IsCliff(levels, elevations, w, h, x, y) || IsRampWall(levels, elevations, w, h, x, y))
                    _flags[i] |= NavFlags.Cliff | NavFlags.Blocked;
            }
        }

        SealPockets();

        int passable = 0;
        for (int i = 0; i < n; i++)
        {
            bool open = (_flags[i] & NavFlags.Blocked) == 0;
            _cost[i] = open ? MapConstants.CostPassable : MapConstants.CostBlocked;
            if (open) passable++;
        }
        PassableCount = passable;
    }

    /// <summary>Width in cells.</summary>
    public int Width { get; }

    /// <summary>Height in cells.</summary>
    public int Height { get; }

    /// <summary>Increments whenever passability changes (a resource node placed or depleted, a building placed or removed), so cached flow fields can tell they're stale. Part of the state hash.</summary>
    public int Version { get; private set; }

    /// <summary>
    /// Increments only on a *closing* change (cells blocked: a resource node or building placed), so a flow field built
    /// at the same value never points into a blocked cell, however many cells have opened since (M3-2b, docs/03 "Flow
    /// fields"). Every bump of it also bumps <see cref="Version"/>. Part of the state hash.
    /// </summary>
    public int BlockVersion { get; private set; }

    /// <summary>Test seam: marks passability as changed (a closing change: both versions bump) so cached flow fields go stale and unusable, with nothing else changed.</summary>
    internal void BumpVersionForTests()
    {
        Version++;
        BlockVersion++;
    }

    /// <summary>Number of passable cells (resource nodes take cells away; depleting them gives the cells back).</summary>
    public int PassableCount { get; private set; }

    /// <summary>
    /// True if a resource node may cover the cell: in the map, passable (so not a cliff, the border,
    /// a sealed pocket or another node) and not a ramp.
    /// </summary>
    internal bool CanTakeResource(int x, int y) =>
        InBounds(x, y) && (_flags[y * Width + x] & (NavFlags.Blocked | NavFlags.Ramp)) == 0;

    /// <summary>
    /// Blocks the <paramref name="width"/> x <paramref name="height"/> cells from (<paramref name="x"/>, <paramref name="y"/>)
    /// for a resource node (flags <see cref="NavFlags.Blocked"/> | <see cref="NavFlags.Resource"/>, cost blocked) and bumps
    /// <see cref="Version"/> and <see cref="BlockVersion"/> once. Every cell must pass <see cref="CanTakeResource"/>; the caller (the resource store) checks.
    /// </summary>
    internal void SetResource(int x, int y, int width, int height) => SetFootprint(x, y, width, height, NavFlags.Resource);

    /// <summary>As <see cref="SetResource"/>, for a building's footprint (<see cref="NavFlags.Building"/>, M3-2); the caller (the building store) checks every cell first.</summary>
    internal void SetBuilding(int x, int y, int width, int height) => SetFootprint(x, y, width, height, NavFlags.Building);

    /// <summary>As <see cref="ClearResource"/>, for a building's footprint (M3-2): a destroyed building or cancelled site (the pocket rule, M3-H1).</summary>
    internal void ClearBuilding(int x, int y, int width, int height) => ClearFootprint(x, y, width, height, NavFlags.Building);

    private void SetFootprint(int x, int y, int width, int height, NavFlags kind)
    {
        for (int cy = y; cy < y + height; cy++)
        {
            for (int cx = x; cx < x + width; cx++)
            {
                int i = cy * Width + cx;
                _flags[i] |= NavFlags.Blocked | kind;
                _cost[i] = MapConstants.CostBlocked;
            }
        }
        PassableCount -= width * height;
        Version++;
        BlockVersion++;
    }

    /// <summary>
    /// Frees the cells a depleted resource node covered (the reverse of <see cref="SetResource"/>) by the pocket rule
    /// (<see cref="ClearFootprint"/>): normally they go back to open ground and <see cref="Version"/> bumps once, an
    /// opening change (<see cref="BlockVersion"/> stays, so cached fields stay usable until rebuilt).
    /// </summary>
    internal void ClearResource(int x, int y, int width, int height) => ClearFootprint(x, y, width, height, NavFlags.Resource);

    /// <summary>Lends the grid scratch of at least two ints a cell for the pocket rule's flood (the flow-field builder's queue storage, idle between builds); without it the first flood allocates its own.</summary>
    internal void ShareScratch(int[] scratch)
    {
        if (scratch.Length < 2 * Width * Height) throw new ArgumentException("scratch too small", nameof(scratch));
        _scratch = scratch;
    }

    /// <summary>
    /// The pocket rule (M3-H1, BUG-0093): frees a footprint so that every passable cell still reaches every other. The
    /// footprint and the <see cref="NavFlags.Pocket"/> cells 4-connected to it through other pocket cells reopen together
    /// (one <see cref="Version"/> bump) if any of them is 4-adjacent to a passable cell; otherwise the footprint's cells
    /// stay blocked as pocket cells and nothing is published (no version bump: no cell a unit can use changed).
    /// </summary>
    /// <remarks>
    /// Passable cells form one 4-connected region (sealed at load, kept so by the never-seal placement rule); a union that
    /// touches it joins it, one that doesn't would be a region of its own. The common case (no pocket cell beside the
    /// footprint) reads only the footprint's 4-adjacent ring; a flood runs only when pockets exist next to it.
    /// </remarks>
    private void ClearFootprint(int x, int y, int width, int height, NavFlags kind)
    {
        int w = Width;
        for (int cy = y; cy < y + height; cy++)
            for (int cx = x; cx < x + width; cx++)
                _flags[cy * w + cx] &= ~kind;
        bool open = false, pocketNear = false;
        for (int k = 0; k < 2 * (width + height); k++)
        {
            SealCheck.RingCell(x, y, width, height, k, out int rx, out int ry);
            NavFlags f = FlagsAt(rx, ry);
            if ((f & NavFlags.Blocked) == 0) open = true;
            else if ((f & NavFlags.Pocket) != 0) pocketNear = true;
        }
        if (!pocketNear)
        {
            for (int cy = y; cy < y + height; cy++)
                for (int cx = x; cx < x + width; cx++)
                    SetOpen(cy * w + cx, open);
            if (open)
            {
                PassableCount += width * height;
                Version++;
            }
            return;
        }

        int n = w * Height;
        int[] s = _scratch ??= new int[2 * n];
        Array.Clear(s, 0, n); // [0, n): 1 = in the union; [n, 2n): the union's cells
        int tail = n;
        for (int cy = y; cy < y + height; cy++)
        {
            for (int cx = x; cx < x + width; cx++)
            {
                s[cy * w + cx] = 1;
                s[tail++] = cy * w + cx;
            }
        }
        // A footprint or pocket cell is never on the border ring, so its 4 neighbors are in bounds.
        for (int head = n; head < tail; head++)
        {
            int i = s[head];
            for (int d = 0; d < 4; d++)
            {
                int j = d == 0 ? i - 1 : d == 1 ? i + 1 : d == 2 ? i - w : i + w;
                if (s[j] != 0) continue;
                NavFlags f = _flags[j];
                if ((f & NavFlags.Blocked) == 0) open = true;
                else if ((f & NavFlags.Pocket) != 0)
                {
                    s[j] = 1;
                    s[tail++] = j;
                }
            }
        }
        if (!open)
        {
            for (int cy = y; cy < y + height; cy++)
                for (int cx = x; cx < x + width; cx++)
                    SetOpen(cy * w + cx, false);
            return;
        }
        for (int k = n; k < tail; k++) SetOpen(s[k], true);
        PassableCount += tail - n;
        Version++;
    }

    /// <summary>A freed cell (no node or building on it now): open ground, or a blocked <see cref="NavFlags.Pocket"/> cell.</summary>
    private void SetOpen(int i, bool open)
    {
        if (open)
        {
            _flags[i] &= ~(NavFlags.Blocked | NavFlags.Pocket);
            _cost[i] = MapConstants.CostPassable;
        }
        else _flags[i] |= NavFlags.Blocked | NavFlags.Pocket;
    }

    /// <summary>Every cell's flags, indexed <c>y * Width + x</c>, for whole-grid passes that can't afford a bounds check per read; the live array, so read it and never write it.</summary>
    /// <remarks>An array, not a span: Debug builds (the tests') don't inline a span's indexer, which made the per-change step-mask pass twice as slow (BUG-0082).</remarks>
    internal NavFlags[] Flags => _flags;

    /// <summary>True if (x, y) is a cell of this grid.</summary>
    public bool InBounds(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    /// <summary>True if ground units may stand in the cell; false outside the map.</summary>
    public bool IsPassable(int x, int y) => InBounds(x, y) && (_flags[y * Width + x] & NavFlags.Blocked) == 0;

    /// <summary>Elevation level of the cell (ramps report their lower level); -1 outside the map.</summary>
    public int LevelAt(int x, int y) => InBounds(x, y) ? _levels[y * Width + x] : -1;

    /// <summary>Flags of the cell; <see cref="NavFlags.Blocked"/> outside the map.</summary>
    public NavFlags FlagsAt(int x, int y) => InBounds(x, y) ? _flags[y * Width + x] : NavFlags.Blocked;

    /// <summary>Movement cost of the cell (1 passable, 255 blocked); 255 outside the map.</summary>
    public byte CostAt(int x, int y) => InBounds(x, y) ? _cost[y * Width + x] : MapConstants.CostBlocked;

    /// <summary>The cell containing a world position in meters; false (and -1, -1) if it is outside the map or not finite.</summary>
    public bool WorldToCell(Vector2 position, out int x, out int y)
    {
        float fx = position.X / MapConstants.CellSize;
        float fy = position.Y / MapConstants.CellSize;
        // Written so NaN fails the test; values are non-negative afterwards, so the cast floors.
        if (!(fx >= 0f && fx < Width && fy >= 0f && fy < Height))
        {
            x = -1;
            y = -1;
            return false;
        }
        x = (int)fx;
        y = (int)fy;
        return true;
    }

    /// <summary>World position in meters of a cell's center (not bounds-checked).</summary>
    public Vector2 CellCenter(int x, int y) =>
        new((x + 0.5f) * MapConstants.CellSize, (y + 0.5f) * MapConstants.CellSize);

    /// <summary>True if the cell slopes up to the next level (its height is above its level's plateau height).</summary>
    internal static bool IsRampCell(ReadOnlySpan<byte> levels, ReadOnlySpan<float> elevations, int i) =>
        elevations[i] > levels[i] * MapConstants.LevelHeight;

    /// <summary>The cliff rule shared with the generator: some 4-neighbor is lower and isn't a ramp exactly one level down.</summary>
    internal static bool IsCliff(ReadOnlySpan<byte> levels, ReadOnlySpan<float> elevations, int width, int height, int x, int y)
    {
        return DropsAt(levels, elevations, width, height, x, y, x - 1, y)
            || DropsAt(levels, elevations, width, height, x, y, x + 1, y)
            || DropsAt(levels, elevations, width, height, x, y, x, y - 1)
            || DropsAt(levels, elevations, width, height, x, y, x, y + 1);
    }

    private static bool DropsAt(ReadOnlySpan<byte> levels, ReadOnlySpan<float> elevations, int width, int height, int x, int y, int nx, int ny)
    {
        if ((uint)nx >= (uint)width || (uint)ny >= (uint)height) return false;
        int level = levels[y * width + x];
        int n = ny * width + nx;
        int below = levels[n];
        if (below >= level) return false;
        return !(below == level - 1 && IsRampCell(levels, elevations, n));
    }

    /// <summary>True for a flat cell beside a ramp cell of its own level that is not that ramp's foot ("ramp wall", BUG-0011).</summary>
    /// <remarks>
    /// The foot lies on the ramp's slope axis, so the cell beyond the ramp, seen from the foot, is
    /// higher (the next ramp cell or the plateau above). Seen from a flank it is a ramp cell at the
    /// same height or the other flank, never higher. Ramps don't store a direction; this reads it.
    /// </remarks>
    internal static bool IsRampWall(ReadOnlySpan<byte> levels, ReadOnlySpan<float> elevations, int width, int height, int x, int y)
    {
        if (IsRampCell(levels, elevations, y * width + x)) return false;
        return FlanksRamp(levels, elevations, width, height, x, y, 1, 0)
            || FlanksRamp(levels, elevations, width, height, x, y, -1, 0)
            || FlanksRamp(levels, elevations, width, height, x, y, 0, 1)
            || FlanksRamp(levels, elevations, width, height, x, y, 0, -1);
    }

    private static bool FlanksRamp(ReadOnlySpan<byte> levels, ReadOnlySpan<float> elevations, int width, int height, int x, int y, int dx, int dy)
    {
        int rx = x + dx, ry = y + dy;
        if ((uint)rx >= (uint)width || (uint)ry >= (uint)height) return false;
        int r = ry * width + rx;
        if (levels[r] != levels[y * width + x] || !IsRampCell(levels, elevations, r)) return false;
        int ox = rx + dx, oy = ry + dy;
        bool foot = (uint)ox < (uint)width && (uint)oy < (uint)height && elevations[oy * width + ox] > elevations[r];
        return !foot;
    }

    // Load-time scratch of SealPockets (a cell's region, a flood queue), kept for one later load-time pass to reuse.
    private int[]? _loadRegion, _loadQueue;

    /// <summary>
    /// Hands over the two cell-sized int arrays the load-time pocket sealing used (once; null after), so a later
    /// load-time pass (the plateau labels, M3-H2) reuses them instead of allocating 8 bytes a cell more: a 1024 map's
    /// world must stay within its memory bound (QA's <c>World_1024Map_CacheStays32_MemoryBounded</c>).
    /// </summary>
    internal (int[]? Cells, int[]? Queue) TakeLoadScratch()
    {
        (int[]? cells, int[]? queue) = (_loadRegion, _loadQueue);
        _loadRegion = _loadQueue = null;
        return (cells, queue);
    }

    // Labels 4-connected passable regions and blocks all but the largest (first found on a tie).
    // Load-time only, so the scratch arrays are fine.
    private void SealPockets()
    {
        int n = Width * Height;
        var region = new int[n];
        Array.Fill(region, -1);
        var queue = new int[n];
        (_loadRegion, _loadQueue) = (region, queue);
        int regions = 0, best = -1, bestSize = 0;
        for (int start = 0; start < n; start++)
        {
            if (region[start] >= 0 || (_flags[start] & NavFlags.Blocked) != 0) continue;
            int size = Flood(start, regions, region, queue);
            if (size > bestSize)
            {
                best = regions;
                bestSize = size;
            }
            regions++;
        }
        for (int i = 0; i < n; i++)
        {
            if (region[i] >= 0 && region[i] != best) _flags[i] |= NavFlags.Blocked;
        }
    }

    private int Flood(int start, int label, int[] region, int[] queue)
    {
        int head = 0, tail = 0, w = Width;
        queue[tail++] = start;
        region[start] = label;
        while (head < tail)
        {
            int i = queue[head++];
            // The outer ring is always blocked, so a passable cell's 4 neighbors are all in bounds.
            Visit(i - 1, label, region, queue, ref tail);
            Visit(i + 1, label, region, queue, ref tail);
            Visit(i - w, label, region, queue, ref tail);
            Visit(i + w, label, region, queue, ref tail);
        }
        return tail;
    }

    private void Visit(int i, int label, int[] region, int[] queue, ref int tail)
    {
        if (region[i] >= 0 || (_flags[i] & NavFlags.Blocked) != 0) return;
        region[i] = label;
        queue[tail++] = i;
    }
}
