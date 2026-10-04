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

    /// <summary>Increments whenever passability changes, so cached flow fields can tell they're stale. Nothing changes it until buildings and trees exist (M3).</summary>
    public int Version { get; private set; }

    /// <summary>Test seam: marks passability as changed so cached flow fields go stale. No production caller until M3 adds buildings.</summary>
    internal void BumpVersionForTests() => Version++;

    /// <summary>Number of passable cells.</summary>
    public int PassableCount { get; }

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

    // Labels 4-connected passable regions and blocks all but the largest (first found on a tie).
    // Load-time only, so the scratch arrays are fine.
    private void SealPockets()
    {
        int n = Width * Height;
        var region = new int[n];
        Array.Fill(region, -1);
        var queue = new int[n];
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
