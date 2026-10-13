using System;
using Rts.Sim.Determinism;

namespace Rts.Sim.Map;

/// <summary>Seeded generator of terraced terrain: level-0 ground, rectangular plateaus at levels 1 and 2, ramps between them (docs/02 "Map and terrain").</summary>
/// <remarks>
/// Steps: raise level-1 rectangles, raise level-2 rectangles inside them, then cut ramps into
/// plateau edges where a ramp fits. Every plateau edge without a ramp becomes a cliff in the
/// <see cref="NavGrid"/>. Then (M5-4a) it picks the start locations (<see cref="FindStarts"/>). A layout is rejected and
/// redrawn (at most <see cref="MapGenParams.MaxAttempts"/> times) if too little ground is passable, the start locations
/// don't fit, or a level is missing; after that it falls back to the best layout seen, or a flat map. Draws only from the <c>rng</c> passed by ref (<c>RngStream.MapGen</c>).
/// Allocates: call at load time only.
/// </remarks>
public static class MapGenerator
{
    /// <summary>Generates a map; the same params and RNG state always give the same map.</summary>
    /// <exception cref="InvalidOperationException">Start locations were asked for and not even the flat fallback has room for them (Validate rules this out).</exception>
    public static Heightmap Generate(MapGenParams p, ref SimRng rng)
    {
        p.Validate();
        Heightmap? fallback = null;
        double minPassable = (double)p.MinPassableFraction * p.Width * p.Height;
        var scratch = new RampScratch(p); // reused by every attempt
        int[]? halfWidth = p.StartLocations > 0 ? SiteHalfWidths(p.StartSiteRadius) : null;
        for (int attempt = 0; attempt < p.MaxAttempts; attempt++)
        {
            Heightmap map = BuildLayout(p, scratch, ref rng);
            var nav = new NavGrid(map);
            if (nav.PassableCount < minPassable) continue;
            if (halfWidth != null)
            {
                // M5-4a: a layout without room for the start locations is redrawn like one with too little ground.
                StartLocation[]? found = FindStarts(p, nav, scratch.NotFlatSums, halfWidth);
                if (found == null) continue;
                map = map.WithStartLocations(found);
            }
            if (AllLevelsPassable(nav)) return map;
            fallback ??= map;
        }
        if (fallback != null) return fallback;
        Heightmap flat = Flat(p);
        if (halfWidth == null) return flat;
        return flat.WithStartLocations(FindStarts(p, new NavGrid(flat), scratch.NotFlatSums, halfWidth)
            ?? throw new InvalidOperationException($"no room for {p.StartLocations} start locations on a flat {p.Width} x {p.Height} map"));
    }

    /// <summary>Half-width in cells of each row of a disc of radius <paramref name="r"/> (index dy + r), centre to centre, the edge counting.</summary>
    private static int[] SiteHalfWidths(int r)
    {
        var half = new int[2 * r + 1];
        for (int dy = -r; dy <= r; dy++)
        {
            int k = 0;
            while ((k + 1) * (k + 1) + dy * dy <= r * r) k++;
            half[dy + r] = k;
        }
        return half;
    }

    // M5-4a start rule. A start location is a centre cell whose site disc (radius StartSiteRadius: the clear disc out to
    // the start mines' reach, plus a cell) is all open ground: passable, not a ramp, no cliff, ramp or border cell among
    // its 8 neighbours (the resource placer's rule). Such a disc is one level, since any level step makes a cliff. Player
    // 0's centre keeps its clear disc StartEdgeMargin inside the map and its site disc in the west half; player 1's is the
    // point mirror through the map centre, players 2 and 3 the quarter turns (square maps). Every position's site disc
    // must be open, the discs apart, every two centres at least half the smaller map side apart, and every position on one
    // level (M5-H1, BUG-0621; a layout with no such centre is redrawn like one without room). Among the centres that work, the one nearest the middle of the west edge band wins
    // (ties to the lower cell). No RNG: an accepted layout is the same map with or without start locations.
    // rows: per row, prefix counts of cells that are not open ground ([y * (Width + 1) + x] counts cells 0 to x - 1); the ramp
    // scratch's summed-area table, idle once a layout is built, lends its storage.
    private static StartLocation[]? FindStarts(MapGenParams p, NavGrid nav, int[] rows, int[] halfWidth)
    {
        int w = p.Width, h = p.Height, stride = w + 1, rs = p.StartSiteRadius;
        for (int y = 0; y < h; y++)
        {
            rows[y * stride] = 0;
            for (int x = 0; x < w; x++)
            {
                bool open = x > 0 && y > 0 && x < w - 1 && y < h - 1 && ResourcePlacer.IsOpenGround(nav, x, y);
                rows[y * stride + x + 1] = rows[y * stride + x] + (open ? 0 : 1);
            }
        }
        int lo = p.StartEdgeMargin + p.StartClearRadius, hiX = w / 2 - 1 - rs, hiY = h - 1 - lo;
        float tx = lo, ty = (h - 1) / 2f, bestD = float.PositiveInfinity;
        int best = -1, n = p.StartLocations;
        Span<int> xs = stackalloc int[MapGenParams.MaxStartLocations];
        Span<int> ys = stackalloc int[MapGenParams.MaxStartLocations];
        for (int cy = lo; cy <= hiY; cy++)
        {
            for (int cx = lo; cx <= hiX; cx++)
            {
                float d = (cx - tx) * (cx - tx) + (cy - ty) * (cy - ty);
                if (d >= bestD) continue;
                Positions(w, h, cx, cy, n, xs, ys);
                if (Apart(w, h, rs, n, xs, ys) && Fits(rows, halfWidth, w, h, rs, n, xs, ys) && SameLevel(nav, n, xs, ys))
                {
                    best = cy * w + cx;
                    bestD = d;
                }
            }
        }
        if (best < 0) return null;
        Positions(w, h, best % w, best / w, n, xs, ys);
        var found = new StartLocation[n];
        for (int i = 0; i < n; i++)
            found[i] = new StartLocation(ys[i] * w + xs[i], nav.LevelAt(xs[i], ys[i]));
        return found;
    }

    /// <summary>Player 0 at (cx, cy); player 1 its point mirror; players 2 and 3 the quarter turns (the map is square then).</summary>
    internal static void Positions(int w, int h, int cx, int cy, int n, Span<int> xs, Span<int> ys)
    {
        xs[0] = cx; ys[0] = cy;
        xs[1] = w - 1 - cx; ys[1] = h - 1 - cy;
        if (n > 2) { xs[2] = w - 1 - cy; ys[2] = cx; }
        if (n > 3) { xs[3] = cy; ys[3] = h - 1 - cx; }
    }

    /// <summary>
    /// True if no two site discs overlap and (BUG-0621) every two centres are at least half the smaller map side apart
    /// (cells): no rush across a crowded map's middle. Shared with <see cref="MapGenParams.Validate"/>'s flat-map check.
    /// </summary>
    internal static bool Apart(int w, int h, int rs, int n, ReadOnlySpan<int> xs, ReadOnlySpan<int> ys)
    {
        long side = Math.Min(w, h);
        for (int i = 0; i < n; i++)
            for (int j = 0; j < i; j++)
            {
                long dx = xs[i] - xs[j], dy = ys[i] - ys[j], d2 = dx * dx + dy * dy;
                if (d2 <= 4L * rs * rs || 4 * d2 < side * side) return false;
            }
        return true;
    }

    /// <summary>BUG-0621: true if every position stands on one level (high ground sees down, low ground can't see up).</summary>
    private static bool SameLevel(NavGrid nav, int n, Span<int> xs, Span<int> ys)
    {
        int level = nav.LevelAt(xs[0], ys[0]);
        for (int i = 1; i < n; i++)
            if (nav.LevelAt(xs[i], ys[i]) != level) return false;
        return true;
    }

    /// <summary>True if every position's site disc is open ground (inside the map).</summary>
    private static bool Fits(int[] rows, int[] halfWidth, int w, int h, int rs, int n, Span<int> xs, Span<int> ys)
    {
        int stride = w + 1;
        for (int i = 0; i < n; i++)
        {
            for (int dy = -rs; dy <= rs; dy++)
            {
                int y = ys[i] + dy, hw = halfWidth[dy + rs], x0 = xs[i] - hw, x1 = xs[i] + hw;
                if (y < 0 || y >= h || x0 < 0 || x1 >= w) return false;
                if (rows[y * stride + x1 + 1] != rows[y * stride + x0]) return false;
            }
        }
        return true;
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
