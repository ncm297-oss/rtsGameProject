using Rts.Sim.Map;

namespace Rts.Sim.Tests.QA.Oracles;

/// <summary>
/// QA oracle: the NavGrid flag derivation as of 4b204f6, before BUG-0015 added the flat-cell fast
/// path and dropped the bounds checks in Flood. Logic copied verbatim (queries trimmed to flags);
/// do not "fix" it, it exists so the optimized grid can be compared cell for cell.
/// </summary>
public sealed class PreBug0015NavGrid
{
    private readonly byte[] _levels;
    private readonly NavFlags[] _flags;

    public PreBug0015NavGrid(Heightmap map)
    {
        Width = map.Width;
        Height = map.Height;
        int n = Width * Height;
        ReadOnlySpan<byte> levels = map.Levels;
        ReadOnlySpan<float> elevations = map.Elevations;
        _levels = levels.ToArray();
        _flags = new NavFlags[n];

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int i = y * Width + x;
                if (IsRampCell(levels, elevations, i)) _flags[i] |= NavFlags.Ramp;
                if (x == 0 || y == 0 || x == Width - 1 || y == Height - 1) _flags[i] |= NavFlags.Blocked;
                if (IsCliff(levels, elevations, Width, Height, x, y) || IsRampWall(levels, elevations, Width, Height, x, y))
                    _flags[i] |= NavFlags.Cliff | NavFlags.Blocked;
            }
        }

        SealPockets();

        for (int i = 0; i < n; i++)
        {
            if ((_flags[i] & NavFlags.Blocked) == 0) PassableCount++;
        }
    }

    public int Width { get; }

    public int Height { get; }

    public int PassableCount { get; }

    public bool IsPassable(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height && (_flags[y * Width + x] & NavFlags.Blocked) == 0;

    public int LevelAt(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height ? _levels[y * Width + x] : -1;

    public NavFlags FlagsAt(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height ? _flags[y * Width + x] : NavFlags.Blocked;

    public static bool IsRampCell(ReadOnlySpan<byte> levels, ReadOnlySpan<float> elevations, int i) =>
        elevations[i] > levels[i] * MapConstants.LevelHeight;

    public static bool IsCliff(ReadOnlySpan<byte> levels, ReadOnlySpan<float> elevations, int width, int height, int x, int y)
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

    public static bool IsRampWall(ReadOnlySpan<byte> levels, ReadOnlySpan<float> elevations, int width, int height, int x, int y)
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
        int head = 0, tail = 0;
        queue[tail++] = start;
        region[start] = label;
        while (head < tail)
        {
            int i = queue[head++];
            int x = i % Width, y = i / Width;
            if (x > 0) Visit(i - 1, label, region, queue, ref tail);
            if (x < Width - 1) Visit(i + 1, label, region, queue, ref tail);
            if (y > 0) Visit(i - Width, label, region, queue, ref tail);
            if (y < Height - 1) Visit(i + Width, label, region, queue, ref tail);
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
