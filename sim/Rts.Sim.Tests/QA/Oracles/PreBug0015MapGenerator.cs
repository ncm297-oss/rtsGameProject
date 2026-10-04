using Rts.Sim.Determinism;
using Rts.Sim.Map;

namespace Rts.Sim.Tests.QA.Oracles;

/// <summary>
/// QA oracle: MapGenerator as of 4b204f6, before BUG-0015 replaced the cell-by-cell ramp footprint
/// scan with a summed-area table and rectangle overlap. Logic copied verbatim; only names changed.
/// Do not "fix" it: the optimized generator must produce identical maps and RNG state.
/// </summary>
public static class PreBug0015MapGenerator
{
    public static Heightmap Generate(MapGenParams p, ref SimRng rng)
    {
        p.Validate();
        Heightmap? fallback = null;
        double minPassable = (double)p.MinPassableFraction * p.Width * p.Height;
        for (int attempt = 0; attempt < p.MaxAttempts; attempt++)
        {
            Heightmap map = BuildLayout(p, ref rng);
            var nav = new PreBug0015NavGrid(map);
            if (nav.PassableCount < minPassable) continue;
            if (AllLevelsPassable(nav)) return map;
            fallback ??= map;
        }
        return fallback ?? Flat(p);
    }

    private readonly record struct Rect(int X, int Y, int W, int H, int Level);

    private static Heightmap BuildLayout(MapGenParams p, ref SimRng rng)
    {
        int w = p.Width, h = p.Height;
        var levels = new byte[w * h];
        var elevations = new float[w * h];
        var rects = new Rect[p.Level1Plateaus + p.Level2Plateaus];
        int rectCount = 0;

        int lo = 1 + p.EdgeMargin;
        int spanX = w - 2 * lo, spanY = h - 2 * lo;
        for (int i = 0; i < p.Level1Plateaus; i++)
        {
            int rw = rng.NextInt(p.Level1MinSize, p.Level1MaxSize + 1);
            int rh = rng.NextInt(p.Level1MinSize, p.Level1MaxSize + 1);
            var r = new Rect(lo + rng.NextInt(0, spanX - rw + 1), lo + rng.NextInt(0, spanY - rh + 1), rw, rh, 1);
            Raise(levels, w, r);
            rects[rectCount++] = r;
        }

        int parents = rectCount;
        for (int i = 0; i < p.Level2Plateaus && parents > 0; i++)
        {
            Rect parent = rects[rng.NextInt(0, parents)];
            int availW = parent.W - 2 * p.Level2Inset, availH = parent.H - 2 * p.Level2Inset;
            if (availW < p.Level2MinSize || availH < p.Level2MinSize) continue;
            int rw = rng.NextInt(p.Level2MinSize, Math.Min(p.Level2MaxSize, availW) + 1);
            int rh = rng.NextInt(p.Level2MinSize, Math.Min(p.Level2MaxSize, availH) + 1);
            int rx = parent.X + p.Level2Inset + rng.NextInt(0, availW - rw + 1);
            int ry = parent.Y + p.Level2Inset + rng.NextInt(0, availH - rh + 1);
            var r = new Rect(rx, ry, rw, rh, 2);
            Raise(levels, w, r);
            rects[rectCount++] = r;
        }

        for (int i = 0; i < levels.Length; i++)
            elevations[i] = levels[i] * MapConstants.LevelHeight;

        for (int r = 0; r < rectCount; r++)
        {
            for (int k = 0; k < p.RampsPerPlateau; k++)
            {
                for (int t = 0; t < p.RampTries; t++)
                {
                    if (TryPlaceRamp(p, levels, elevations, rects[r], ref rng)) break;
                }
            }
        }
        return new Heightmap(w, h, levels, elevations);
    }

    private static void Raise(byte[] levels, int w, Rect r)
    {
        for (int y = r.Y; y < r.Y + r.H; y++)
        {
            for (int x = r.X; x < r.X + r.W; x++)
                levels[y * w + x] = (byte)r.Level;
        }
    }

    private static bool TryPlaceRamp(MapGenParams p, byte[] levels, float[] elevations, Rect r, ref SimRng rng)
    {
        int w = p.Width, h = p.Height;
        int side = rng.NextInt(0, 4);
        int sideLength = side < 2 ? r.W : r.H;
        int offset = rng.NextInt(1, sideLength - p.RampWidth);
        int mx, my, ax, ay, dx, dy;
        switch (side)
        {
            case 0: mx = r.X + offset; my = r.Y; ax = 1; ay = 0; dx = 0; dy = -1; break;
            case 1: mx = r.X + offset; my = r.Y + r.H - 1; ax = 1; ay = 0; dx = 0; dy = 1; break;
            case 2: mx = r.X; my = r.Y + offset; ax = 0; ay = 1; dx = -1; dy = 0; break;
            default: mx = r.X + r.W - 1; my = r.Y + offset; ax = 0; ay = 1; dx = 1; dy = 0; break;
        }
        int upper = r.Level, lower = r.Level - 1;

        for (int j = -1; j <= p.RampWidth; j++)
        {
            int cx = mx + j * ax, cy = my + j * ay;
            if (!IsGround(p, levels, elevations, cx, cy, upper, Footing.Any)) return false;
            bool inMouth = j >= 0 && j < p.RampWidth;
            if (inMouth && !IsGround(p, levels, elevations, cx - dx, cy - dy, upper, Footing.Open)) return false;
            for (int k = 1; k <= p.RampLength + 1; k++)
            {
                int fx = cx + k * dx, fy = cy + k * dy;
                if (fx < 1 || fy < 1 || fx > w - 2 || fy > h - 2) return false;
                if (!IsGround(p, levels, elevations, fx, fy, lower, Footing.Flat)) return false;
            }
        }

        for (int j = 0; j < p.RampWidth; j++)
        {
            for (int k = 1; k <= p.RampLength; k++)
            {
                int i = (my + j * ay + k * dy) * w + (mx + j * ax + k * dx);
                float rise = (float)(p.RampLength + 1 - k) / (p.RampLength + 1);
                elevations[i] = (lower + rise) * MapConstants.LevelHeight;
            }
        }
        return true;
    }

    private enum Footing
    {
        Any,
        Open,
        Flat,
    }

    private static bool IsGround(MapGenParams p, byte[] levels, float[] elevations, int x, int y, int level, Footing footing)
    {
        if ((uint)x >= (uint)p.Width || (uint)y >= (uint)p.Height) return false;
        int i = y * p.Width + x;
        if (levels[i] != level || PreBug0015NavGrid.IsRampCell(levels, elevations, i)) return false;
        return footing switch
        {
            Footing.Any => true,
            Footing.Open => !PreBug0015NavGrid.IsCliff(levels, elevations, p.Width, p.Height, x, y),
            _ => !HasLowerNeighbor(p, levels, x, y) && !HasRampNeighbor(p, levels, elevations, x, y),
        };
    }

    private static bool HasRampNeighbor(MapGenParams p, byte[] levels, float[] elevations, int x, int y)
    {
        int w = p.Width;
        return (x > 0 && PreBug0015NavGrid.IsRampCell(levels, elevations, y * w + x - 1))
            || (x < w - 1 && PreBug0015NavGrid.IsRampCell(levels, elevations, y * w + x + 1))
            || (y > 0 && PreBug0015NavGrid.IsRampCell(levels, elevations, (y - 1) * w + x))
            || (y < p.Height - 1 && PreBug0015NavGrid.IsRampCell(levels, elevations, (y + 1) * w + x));
    }

    private static bool HasLowerNeighbor(MapGenParams p, byte[] levels, int x, int y)
    {
        int level = levels[y * p.Width + x];
        return (x > 0 && levels[y * p.Width + x - 1] < level)
            || (x < p.Width - 1 && levels[y * p.Width + x + 1] < level)
            || (y > 0 && levels[(y - 1) * p.Width + x] < level)
            || (y < p.Height - 1 && levels[(y + 1) * p.Width + x] < level);
    }

    private static bool AllLevelsPassable(PreBug0015NavGrid nav)
    {
        Span<bool> seen = stackalloc bool[MapConstants.LevelCount];
        for (int y = 0; y < nav.Height; y++)
        {
            for (int x = 0; x < nav.Width; x++)
            {
                if (nav.IsPassable(x, y)) seen[nav.LevelAt(x, y)] = true;
            }
        }
        for (int l = 0; l < seen.Length; l++)
        {
            if (!seen[l]) return false;
        }
        return true;
    }

    private static Heightmap Flat(MapGenParams p) =>
        new(p.Width, p.Height, new byte[p.Width * p.Height], new float[p.Width * p.Height]);
}
