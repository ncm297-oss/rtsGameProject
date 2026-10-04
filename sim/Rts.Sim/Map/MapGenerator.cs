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
        var scratch = new RampScratch(p); // reused by every attempt
        for (int attempt = 0; attempt < p.MaxAttempts; attempt++)
        {
            Heightmap map = BuildLayout(p, scratch, ref rng);
            var nav = new NavGrid(map);
            if (nav.PassableCount < minPassable) continue;
            if (AllLevelsPassable(nav)) return map;
            fallback ??= map;
        }
        return fallback ?? Flat(p);
    }

    private readonly record struct Rect(int X, int Y, int W, int H, int Level);

    /// <summary>Per-layout bookkeeping that lets ramp tries be checked without scanning the footprint (BUG-0015).</summary>
    private sealed class RampScratch
    {
        public RampScratch(MapGenParams p)
        {
            NotFlatSums = new int[(p.Width + 1) * (p.Height + 1)];
            Ramps = new Rect[(p.Level1Plateaus + p.Level2Plateaus) * p.RampsPerPlateau];
        }

        /// <summary>Summed-area table of cells no footprint may use, see <see cref="BuildNotFlatSums"/>.</summary>
        public int[] NotFlatSums { get; }

        /// <summary>Cell rectangles of the ramps placed so far in this layout.</summary>
        public Rect[] Ramps { get; }

        public int RampCount { get; set; }
    }

    private static Heightmap BuildLayout(MapGenParams p, RampScratch scratch, ref SimRng rng)
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

        BuildNotFlatSums(p, levels, scratch.NotFlatSums);
        scratch.RampCount = 0;
        for (int r = 0; r < rectCount; r++)
        {
            for (int k = 0; k < p.RampsPerPlateau; k++)
            {
                for (int t = 0; t < p.RampTries; t++)
                {
                    if (TryPlaceRamp(p, levels, elevations, scratch, rects[r], ref rng)) break;
                }
            }
        }
        return new Heightmap(w, h, levels, elevations);
    }

    private static void Raise(byte[] levels, int w, Rect r)
    {
        for (int y = r.Y; y < r.Y + r.H; y++)
            levels.AsSpan(y * w + r.X, r.W).Fill((byte)r.Level);
    }

    // A ramp runs outward from a random stretch ("mouth") of one rectangle side, down onto the
    // level below. It is placed only where the whole footprint plus a one-cell ring is open
    // lower ground and the mouth sits mid-side on plateau ground, so the only level step it
    // creates is ramp -> mouth (NavGrid's cliff rule then leaves the mouth open).
    //
    // Footprint rule: every cell of the footprint plus ring is lower-level ground that is inside the
    // border ring, has no lower 4-neighbor, and is neither a ramp cell nor a 4-neighbor of one
    // (NavGrid walls the cells flanking a ramp, BUG-0011, so ramps must not touch). It is checked
    // in O(1) + O(ramps placed) per try rather than cell by cell, so a try's cost doesn't grow with
    // RampWidth x RampLength (BUG-0015):
    //  - border and lower-neighbor cells come from a summed-area table built before any ramp is cut
    //    (cutting ramps changes heights, never levels, so these cells don't change);
    //  - with none of those inside, the footprint is one level: two 4-adjacent cells of different
    //    levels would give the higher one a lower neighbor, so one cell's level stands for all;
    //  - "a ramp cell or a 4-neighbor of one" is the cross-shaped region around each placed ramp
    //    (its rectangle grown by one cell along x, or along y), tested by rectangle overlap.
    private static bool TryPlaceRamp(MapGenParams p, byte[] levels, float[] elevations, RampScratch scratch, Rect r, ref SimRng rng)
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

        // Footprint plus ring: cells (j across the slope, k down it) with j in [-1, RampWidth] and
        // k in [1, RampLength + 1]; its corners give its bounding rectangle.
        int fx0 = mx - ax + dx, fy0 = my - ay + dy;
        int fx1 = mx + p.RampWidth * ax + (p.RampLength + 1) * dx, fy1 = my + p.RampWidth * ay + (p.RampLength + 1) * dy;
        int x0 = Math.Min(fx0, fx1), x1 = Math.Max(fx0, fx1), y0 = Math.Min(fy0, fy1), y1 = Math.Max(fy0, fy1);
        if (x0 < 1 || y0 < 1 || x1 > w - 2 || y1 > h - 2) return false; // stay off the border ring
        if (CountNotFlat(scratch.NotFlatSums, w, x0, y0, x1, y1) > 0) return false;
        if (levels[y0 * w + x0] != lower) return false;

        // The mouth line, plateau shoulders included (j = -1 and RampWidth), is plateau ground.
        for (int j = -1; j <= p.RampWidth; j++)
        {
            if (!IsGround(p, levels, elevations, mx + j * ax, my + j * ay, upper, Footing.Any)) return false;
        }

        // Newest first: a clash is most likely with this plateau's own earlier ramps.
        for (int i = scratch.RampCount - 1; i >= 0; i--)
        {
            Rect q = scratch.Ramps[i];
            if (Overlaps(x0, y0, x1, y1, q.X - 1, q.Y, q.X + q.W, q.Y + q.H - 1)
                || Overlaps(x0, y0, x1, y1, q.X, q.Y - 1, q.X + q.W - 1, q.Y + q.H))
                return false;
        }

        // Open plateau (not a cliff) behind each mouth cell. Last, as the costliest per cell.
        for (int j = 0; j < p.RampWidth; j++)
        {
            if (!IsGround(p, levels, elevations, mx + j * ax - dx, my + j * ay - dy, upper, Footing.Open)) return false;
        }

        // Ramp cells: j in [0, RampWidth), k in [1, RampLength].
        int rx0 = mx + dx, ry0 = my + dy;
        int rx1 = mx + (p.RampWidth - 1) * ax + p.RampLength * dx, ry1 = my + (p.RampWidth - 1) * ay + p.RampLength * dy;
        int qx = Math.Min(rx0, rx1), qy = Math.Min(ry0, ry1);
        scratch.Ramps[scratch.RampCount++] = new Rect(qx, qy, Math.Abs(rx1 - rx0) + 1, Math.Abs(ry1 - ry0) + 1, lower);

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
    }

    /// <summary>True if the cell is in bounds, at the given level, not a ramp, and meets the footing rule.</summary>
    private static bool IsGround(MapGenParams p, byte[] levels, float[] elevations, int x, int y, int level, Footing footing)
    {
        if ((uint)x >= (uint)p.Width || (uint)y >= (uint)p.Height) return false;
        int i = y * p.Width + x;
        if (levels[i] != level || NavGrid.IsRampCell(levels, elevations, i)) return false;
        return footing == Footing.Any || !NavGrid.IsCliff(levels, elevations, p.Width, p.Height, x, y);
    }

    /// <summary>True if two inclusive cell rectangles share a cell.</summary>
    private static bool Overlaps(int ax0, int ay0, int ax1, int ay1, int bx0, int by0, int bx1, int by1) =>
        ax0 <= bx1 && bx0 <= ax1 && ay0 <= by1 && by0 <= ay1;

    // Summed-area table of the cells a ramp footprint may never use that don't depend on ramps:
    // the border ring and every cell with a lower 4-neighbor. Built per layout once the plateaus
    // are raised. sums[(y + 1) * (w + 1) + (x + 1)] counts such cells in [0, x] x [0, y].
    private static void BuildNotFlatSums(MapGenParams p, byte[] levels, int[] sums)
    {
        int w = p.Width, h = p.Height, stride = w + 1;
        Array.Clear(sums);
        for (int y = 0; y < h; y++)
        {
            int rowSum = 0;
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                bool border = x == 0 || y == 0 || x == w - 1 || y == h - 1;
                // Inlined lower-neighbor test (the border cells are already counted, so no bounds checks).
                if (border || levels[i - 1] < levels[i] || levels[i + 1] < levels[i]
                    || levels[i - w] < levels[i] || levels[i + w] < levels[i])
                    rowSum++;
                sums[(y + 1) * stride + x + 1] = sums[y * stride + x + 1] + rowSum;
            }
        }
    }

    /// <summary>Off-limits cells (see <see cref="BuildNotFlatSums"/>) in the inclusive cell rectangle.</summary>
    private static int CountNotFlat(int[] sums, int w, int x0, int y0, int x1, int y1)
    {
        int stride = w + 1;
        return sums[(y1 + 1) * stride + x1 + 1] - sums[y0 * stride + x1 + 1]
            - sums[(y1 + 1) * stride + x0] + sums[y0 * stride + x0];
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
