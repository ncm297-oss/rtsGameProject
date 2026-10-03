using System;
using Rts.Sim.Determinism;

namespace Rts.Sim.Map;

/// <summary>Seeded generator of terraced terrain: level-0 ground, rectangular plateaus at levels 1 and 2, ramps between them (docs/02 "Map and terrain").</summary>
/// <remarks>
/// Steps: raise level-1 rectangles, raise level-2 rectangles inside them, then cut ramps into
/// plateau edges where a ramp fits. Every plateau edge without a ramp becomes a cliff in the
/// <see cref="NavGrid"/>. A layout is rejected and redrawn (at most <see cref="MapGenParams.MaxAttempts"/>
/// times) if too little ground is passable or a level is missing; after that it falls back to the
/// best layout seen, or a flat map. Draws only from the <c>rng</c> passed by ref (<c>RngStream.MapGen</c>).
/// Allocates: call at load time only.
/// </remarks>
public static class MapGenerator
{
    /// <summary>Generates a map; the same params and RNG state always give the same map.</summary>
    public static Heightmap Generate(MapGenParams p, ref SimRng rng)
    {
        p.Validate();
        Heightmap? fallback = null;
        double minPassable = (double)p.MinPassableFraction * p.Width * p.Height;
        for (int attempt = 0; attempt < p.MaxAttempts; attempt++)
        {
            Heightmap map = BuildLayout(p, ref rng);
            var nav = new NavGrid(map);
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

        // Level 1: anywhere inside the border ring plus the edge margin.
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

        // Level 2: inside a random level-1 rectangle, skipped if that one is too small.
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

    // A ramp runs outward from a random stretch ("mouth") of one rectangle side, down onto the
    // level below. It is placed only where the whole footprint plus a one-cell ring is open
    // lower ground and the mouth sits mid-side on plateau ground, so the only level step it
    // creates is ramp -> mouth (NavGrid's cliff rule then leaves the mouth open).
    private static bool TryPlaceRamp(MapGenParams p, byte[] levels, float[] elevations, Rect r, ref SimRng rng)
    {
        int w = p.Width, h = p.Height;
        int side = rng.NextInt(0, 4);
        int sideLength = side < 2 ? r.W : r.H;
        int offset = rng.NextInt(1, sideLength - p.RampWidth); // leaves a plateau cell beside each end of the mouth
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
                if (fx < 1 || fy < 1 || fx > w - 2 || fy > h - 2) return false; // stay off the border ring
                if (!IsGround(p, levels, elevations, fx, fy, lower, Footing.Flat)) return false;
            }
        }

        for (int j = 0; j < p.RampWidth; j++)
        {
            for (int k = 1; k <= p.RampLength; k++)
            {
                int i = (my + j * ay + k * dy) * w + (mx + j * ax + k * dx);
                // k = 1 touches the mouth; heights step evenly and stay strictly between the levels.
                float rise = (float)(p.RampLength + 1 - k) / (p.RampLength + 1);
                elevations[i] = (lower + rise) * MapConstants.LevelHeight;
            }
        }
        return true;
    }

    private enum Footing
    {
        Any,  // cliff edge allowed (the mouth, before the ramp opens it)
        Open, // not a cliff
        Flat, // no lower neighbor at all, so a ramp never lands on another ramp's mouth
    }

    /// <summary>True if the cell is in bounds, at the given level, not a ramp, and meets the footing rule.</summary>
    private static bool IsGround(MapGenParams p, byte[] levels, float[] elevations, int x, int y, int level, Footing footing)
    {
        if ((uint)x >= (uint)p.Width || (uint)y >= (uint)p.Height) return false;
        int i = y * p.Width + x;
        if (levels[i] != level || NavGrid.IsRampCell(levels, elevations, i)) return false;
        return footing switch
        {
            Footing.Any => true,
            Footing.Open => !NavGrid.IsCliff(levels, elevations, p.Width, p.Height, x, y),
            _ => !HasLowerNeighbor(p, levels, x, y),
        };
    }

    private static bool HasLowerNeighbor(MapGenParams p, byte[] levels, int x, int y)
    {
        int level = levels[y * p.Width + x];
        return (x > 0 && levels[y * p.Width + x - 1] < level)
            || (x < p.Width - 1 && levels[y * p.Width + x + 1] < level)
            || (y > 0 && levels[(y - 1) * p.Width + x] < level)
            || (y < p.Height - 1 && levels[(y + 1) * p.Width + x] < level);
    }

    private static bool AllLevelsPassable(NavGrid nav)
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
