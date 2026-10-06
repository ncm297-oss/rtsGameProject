using System;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;

namespace Rts.Sim.Map;

/// <summary>Places gold mines and forests on a fresh map at world construction (docs/03 "Economy implementation", M3-1).</summary>
/// <remarks>
/// Runs once, after the nav grid and before any tick, drawing only from the map stream it is given
/// (after the heightmap's draws, so the terrain is the same with and without resources); with
/// <see cref="MapGenParams.GoldMines"/> and <see cref="MapGenParams.Forests"/> at 0 it draws nothing.
/// Mines go first, then forests. A node only covers "open" cells: passable, flat (no ramp), and with
/// no cliff, ramp or border cell among their 8 neighbors; a footprint is one level. A mine's center is
/// at least <see cref="MapGenParams.MineSpacing"/> from every other mine's. A forest grows from a
/// random seed cell as a 4-connected blob of a size drawn from the forest range, adding a random
/// frontier cell at a time. No cell of a new mine or forest touches (8-neighbor) an earlier node, so
/// workers reach a mine from every side and each forest is its own 8-connected group of trees.
/// Before a mine or forest is committed, a flood fill checks that every passable cell is still
/// reachable from every other; if not, that placement is dropped (a cheap local test on the cells
/// around it rejects most bad ones first; see <c>Scratch.StaysConnected</c>). Depletion only reopens
/// cells, so no pocket can appear later. Every placement gets at most <see cref="TriesPerPlacement"/>
/// tries; one that fails them all is skipped and the shortfall shows in the returned counts. Load
/// time only, so it allocates its scratch freely; no Dictionary or HashSet, cells in index order.
/// </remarks>
internal static class ResourcePlacer
{
    /// <summary>Random positions tried per mine or forest before it is skipped (bounds setup time).</summary>
    internal const int TriesPerPlacement = 32;

    /// <summary>Places the requested mines and forests into <paramref name="store"/>; returns what was placed.</summary>
    public static ResourcePlacement Place(MapGenParams p, NavGrid grid, ResourceStore store, GameData data, ref SimRng rng)
    {
        if (p.GoldMines == 0 && p.Forests == 0) return default;
        var s = new Scratch(grid);
        int mines = 0, forests = 0, trees = 0;

        int mineType = FirstOfKind(data, ResourceKind.Gold);
        if (mineType >= 0)
        {
            for (int m = 0; m < p.GoldMines && store.FreeCount > 0; m++)
            {
                for (int t = 0; t < TriesPerPlacement; t++)
                {
                    if (TryMine(p, grid, store, data, s, mineType, mines, ref rng))
                    {
                        mines++;
                        break;
                    }
                }
            }
        }

        int treeType = FirstOfKind(data, ResourceKind.Wood);
        if (treeType >= 0)
        {
            for (int f = 0; f < p.Forests; f++)
            {
                int size = rng.NextInt(p.ForestMinTrees, p.ForestMaxTrees + 1);
                if (store.FreeCount < size) break;
                for (int t = 0; t < TriesPerPlacement; t++)
                {
                    if (TryForest(grid, store, data, s, treeType, size, ref rng))
                    {
                        forests++;
                        trees += size;
                        break;
                    }
                }
            }
        }
        return new ResourcePlacement(forests, trees, mines);
    }

    /// <summary>The placer's reachability rule on its own: true if, with <paramref name="cells"/> blocked as well, every other passable cell of the grid still reaches every other. Allocates.</summary>
    internal static bool StaysConnected(NavGrid grid, ReadOnlySpan<int> cells)
    {
        var s = new Scratch(grid);
        s.BeginCandidates();
        foreach (int c in cells)
            s.AddCandidate(c);
        return s.StaysConnected();
    }

    /// <summary>The lowest resource type id yielding <paramref name="kind"/>, or -1 if the data has none.</summary>
    private static int FirstOfKind(GameData data, ResourceKind kind)
    {
        for (int i = 0; i < data.Resources.Length; i++)
            if (data.Resources[i].Resource == kind) return i;
        return -1;
    }

    private static bool TryMine(MapGenParams p, NavGrid grid, ResourceStore store, GameData data, Scratch s, int type, int placed, ref SimRng rng)
    {
        ResourceDef def = data.Resources[type];
        int w = grid.Width, fw = def.FootprintWidth, fh = def.FootprintHeight;
        // Anchors keep the whole footprint inside the border ring.
        int ax = rng.NextInt(1, w - fw), ay = rng.NextInt(1, grid.Height - fh);
        int level = grid.LevelAt(ax, ay);
        for (int y = ay; y < ay + fh; y++)
        {
            for (int x = ax; x < ax + fw; x++)
            {
                if (!s.Open(x, y) || grid.LevelAt(x, y) != level) return false;
            }
        }
        // No other node on the one-cell ring, so workers can reach the mine's edge from every side.
        for (int y = ay; y < ay + fh; y++)
        {
            for (int x = ax; x < ax + fw; x++)
            {
                if (TouchesNode(grid, x, y)) return false;
            }
        }
        float cx = (ax + fw * 0.5f) * MapConstants.CellSize, cy = (ay + fh * 0.5f) * MapConstants.CellSize;
        float min2 = p.MineSpacing * p.MineSpacing;
        for (int i = 0; i < placed; i++)
        {
            float dx = s.MineX[i] - cx, dy = s.MineY[i] - cy;
            if (dx * dx + dy * dy < min2) return false;
        }

        s.BeginCandidates();
        for (int y = ay; y < ay + fh; y++)
            for (int x = ax; x < ax + fw; x++)
                s.AddCandidate(y * w + x);
        if (!s.StaysConnected()) return false;

        if (!store.Spawn(type, ay * w + ax, data.Rules.StartMineGold, out _)) return false;
        s.MineX[placed] = cx;
        s.MineY[placed] = cy;
        return true;
    }

    private static bool TryForest(NavGrid grid, ResourceStore store, GameData data, Scratch s, int type, int size, ref SimRng rng)
    {
        int w = grid.Width;
        int sx = rng.NextInt(1, w - 1), sy = rng.NextInt(1, grid.Height - 1);
        if (!s.Open(sx, sy) || TouchesNode(grid, sx, sy)) return false;
        int level = grid.LevelAt(sx, sy);

        s.BeginCandidates();
        int seed = sy * w + sx;
        s.AddCandidate(seed);
        int frontier = Grow(grid, s, seed, level, 0);
        while (s.CandidateCount < size && frontier > 0)
        {
            int k = rng.NextInt(0, frontier);
            int c = s.Frontier[k];
            s.Frontier[k] = s.Frontier[--frontier];
            s.AddCandidate(c);
            frontier = Grow(grid, s, c, level, frontier);
        }
        if (s.CandidateCount < size || !s.StaysConnected()) return false;

        // Trees take slots in cell order, so slot order doesn't depend on how the blob grew.
        Array.Sort(s.Candidates, 0, s.CandidateCount);
        int wood = data.Rules.TreeWood;
        for (int i = 0; i < s.CandidateCount; i++)
            store.Spawn(type, s.Candidates[i], wood, out _); // fits: open cells not touching a node, checked above
        return true;
    }

    /// <summary>True if a node already covers one of the cell's 8 neighbors (the cell itself is open, so not on the border).</summary>
    private static bool TouchesNode(NavGrid grid, int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if ((grid.FlagsAt(x + dx, y + dy) & NavFlags.Resource) != 0) return true;
            }
        }
        return false;
    }

    /// <summary>Adds each 4-neighbor of <paramref name="cell"/> not yet seen this attempt that is open, on <paramref name="level"/> and not touching an earlier node to the frontier; returns its new length.</summary>
    private static int Grow(NavGrid grid, Scratch s, int cell, int level, int frontier)
    {
        int w = grid.Width;
        int x = cell % w, y = cell / w;
        frontier = Consider(grid, s, x + 1, y, level, frontier);
        frontier = Consider(grid, s, x - 1, y, level, frontier);
        frontier = Consider(grid, s, x, y + 1, level, frontier);
        frontier = Consider(grid, s, x, y - 1, level, frontier);
        return frontier;
    }

    private static int Consider(NavGrid grid, Scratch s, int x, int y, int level, int frontier)
    {
        int i = y * grid.Width + x;
        if (!grid.InBounds(x, y) || s.Seen[i] == s.Attempt) return frontier;
        s.Seen[i] = s.Attempt;
        if (!s.Open(x, y) || TouchesNode(grid, x, y) || grid.LevelAt(x, y) != level) return frontier;
        s.Frontier[frontier] = i;
        return frontier + 1;
    }

    /// <summary>Per-placement working arrays, sized from the map once.</summary>
    private sealed class Scratch
    {
        private readonly NavGrid _grid;
        private readonly bool[] _openGround;
        private readonly int[] _visited;
        private readonly int[] _queue;
        private readonly int[] _candidateMark;
        private readonly int[] _ringMark;
        private readonly int[] _ring;
        private int _visitStamp;

        public Scratch(NavGrid grid)
        {
            _grid = grid;
            int n = grid.Width * grid.Height;
            _openGround = new bool[n];
            _visited = new int[n];
            _queue = new int[n];
            _candidateMark = new int[n];
            _ringMark = new int[n];
            _ring = new int[n];
            Seen = new int[n];
            Frontier = new int[n];
            Candidates = new int[n];
            MineX = new float[MapGenParams.MaxResourceGroups];
            MineY = new float[MapGenParams.MaxResourceGroups];
            for (int y = 1; y < grid.Height - 1; y++)
            {
                for (int x = 1; x < grid.Width - 1; x++)
                    _openGround[y * grid.Width + x] = IsOpenGround(grid, x, y);
            }
        }

        /// <summary>Per cell: the attempt number that last saw it as a candidate or a frontier cell.</summary>
        public int[] Seen { get; }
        public int[] Frontier { get; }
        public int[] Candidates { get; }
        public int CandidateCount { get; private set; }
        /// <summary>The attempt in progress; <see cref="Seen"/> entries equal to it belong to it.</summary>
        public int Attempt { get; private set; }
        /// <summary>Centers (m) of the mines placed so far.</summary>
        public float[] MineX { get; }
        public float[] MineY { get; }

        /// <summary>True if a node may cover the cell now: open ground before placement began, and no node on it yet.</summary>
        public bool Open(int x, int y) => _grid.InBounds(x, y) && _openGround[y * _grid.Width + x] && _grid.CanTakeResource(x, y);

        public void BeginCandidates()
        {
            Attempt++;
            CandidateCount = 0;
        }

        public void AddCandidate(int cell)
        {
            Seen[cell] = Attempt;
            _candidateMark[cell] = Attempt;
            Candidates[CandidateCount++] = cell;
        }

        /// <summary>
        /// True if, with this attempt's candidates blocked too, every other passable cell is still reachable
        /// from every other (4-connected, which is what the 8-connected flow fields without corner cutting reach).
        /// </summary>
        /// <remarks>
        /// Two steps. First a local test on the ring (the cells 8-adjacent to a candidate): every ring cell
        /// must be passable and the ring 4-connected by itself. That is enough on its own (a path between two
        /// cells that crossed the candidates enters and leaves them through ring cells, so it can go round
        /// through the ring instead), and it rejects the common failure, a forest enclosing a hole, without
        /// touching the rest of the map. Then the flood fill of the whole map the rule asks for, which only
        /// placements that pass the ring test pay for.
        /// </remarks>
        public bool StaysConnected() => RingStaysConnected() && MapStaysConnected();

        private bool RingStaysConnected()
        {
            int w = _grid.Width, ring = 0;
            _visitStamp++;
            int stamp = _visitStamp;
            for (int k = 0; k < CandidateCount; k++)
            {
                int cx = Candidates[k] % w, cy = Candidates[k] / w;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = cx + dx, y = cy + dy;
                        if (!_grid.InBounds(x, y)) return false;
                        int i = y * w + x;
                        if (_candidateMark[i] == Attempt || _ringMark[i] == stamp) continue;
                        if (!_grid.IsPassable(x, y)) return false;
                        _ringMark[i] = stamp;
                        _ring[ring++] = i;
                    }
                }
            }
            if (ring == 0) return true;
            // Flood the ring through ring cells only; _visited uses the same stamp.
            int head = 0, tail = 0;
            _queue[tail++] = _ring[0];
            _visited[_ring[0]] = stamp;
            while (head < tail)
            {
                int i = _queue[head++];
                int x = i % w, y = i / w;
                tail = VisitRing(x - 1, y, stamp, tail);
                tail = VisitRing(x + 1, y, stamp, tail);
                tail = VisitRing(x, y - 1, stamp, tail);
                tail = VisitRing(x, y + 1, stamp, tail);
            }
            return tail == ring;
        }

        private int VisitRing(int x, int y, int stamp, int tail)
        {
            if (!_grid.InBounds(x, y)) return tail;
            int i = y * _grid.Width + x;
            if (_ringMark[i] != stamp || _visited[i] == stamp) return tail;
            _visited[i] = stamp;
            _queue[tail] = i;
            return tail + 1;
        }

        private bool MapStaysConnected()
        {
            int n = _grid.Width * _grid.Height, w = _grid.Width;
            int start = -1;
            for (int i = 0; i < n && start < 0; i++)
            {
                if (IsFree(i)) start = i;
            }
            int expected = _grid.PassableCount - CandidateCount;
            if (start < 0) return expected == 0;
            _visitStamp++;
            int head = 0, tail = 0;
            _queue[tail++] = start;
            _visited[start] = _visitStamp;
            while (head < tail)
            {
                int i = _queue[head++];
                // The border ring is always blocked, so a passable cell's 4 neighbors are in bounds.
                tail = Visit(i - 1, tail);
                tail = Visit(i + 1, tail);
                tail = Visit(i - w, tail);
                tail = Visit(i + w, tail);
            }
            return tail == expected;
        }

        private int Visit(int i, int tail)
        {
            if (_visited[i] == _visitStamp || !IsFree(i)) return tail;
            _visited[i] = _visitStamp;
            _queue[tail] = i;
            return tail + 1;
        }

        private bool IsFree(int i) => _candidateMark[i] != Attempt && _grid.IsPassable(i % _grid.Width, i / _grid.Width);

        /// <summary>Passable, not a ramp, and no cliff, ramp or border cell among the 8 neighbors (x, y is not on the border).</summary>
        private static bool IsOpenGround(NavGrid g, int x, int y)
        {
            if (!g.CanTakeResource(x, y)) return false;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx == 0 || ny == 0 || nx == g.Width - 1 || ny == g.Height - 1) return false;
                    if ((g.FlagsAt(nx, ny) & (NavFlags.Cliff | NavFlags.Ramp)) != 0) return false;
                }
            }
            return true;
        }
    }
}
