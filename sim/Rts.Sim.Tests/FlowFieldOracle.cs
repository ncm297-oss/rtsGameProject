using Rts.Sim.Determinism;
using Rts.Sim.Map;

namespace Rts.Sim.Tests;

/// <summary>Independent reference implementations for flow-field tests, plus the grids they run on.</summary>
/// <remarks>Written from the docs/03 rules, not from FlowField's code; speed and allocation don't matter here.</remarks>
public static class FlowFieldOracle
{
    private const float H = MapConstants.LevelHeight;

    // The documented direction numbering: 0 = +x, then clockwise with +y pointing "down" the rows.
    public static readonly (int Dx, int Dy)[] Dirs = { (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1) };

    /// <summary>Whether a unit may step from (x, y) by (dx, dy): destination passable, and for diagonals both side cells.</summary>
    public static bool Allowed(NavGrid g, int x, int y, int dx, int dy)
    {
        if (!g.IsPassable(x, y) || !g.IsPassable(x + dx, y + dy)) return false;
        if (dx != 0 && dy != 0) return g.IsPassable(x + dx, y) && g.IsPassable(x, y + dy);
        return true;
    }

    public static float StepCost(int dx, int dy) => dx != 0 && dy != 0 ? 1.41421f : 1f;

    /// <summary>Nearest passable cell by squared distance, ties lowest (y, x); brute force.</summary>
    public static int Nearest(NavGrid g, int cell)
    {
        int tx = cell % g.Width, ty = cell / g.Width;
        var best = (D2: long.MaxValue, Y: int.MaxValue, X: int.MaxValue);
        for (int y = 0; y < g.Height; y++)
            for (int x = 0; x < g.Width; x++)
            {
                if (!g.IsPassable(x, y)) continue;
                long d2 = (long)(x - tx) * (x - tx) + (long)(y - ty) * (y - ty);
                if ((d2, y, x).CompareTo(best) < 0) best = (d2, y, x);
            }
        return best.D2 == long.MaxValue ? -1 : best.Y * g.Width + best.X;
    }

    /// <summary>Lazy-deletion Dijkstra from the (resolved) target with the BCL priority queue; returns costs and directions (255 = none).</summary>
    public static (float[] Cost, byte[] Dir, int Target) Solve(NavGrid g, int requested)
    {
        int w = g.Width, n = w * g.Height;
        int target = g.IsPassable(requested % w, requested / w) ? requested : Nearest(g, requested);
        var cost = new float[n];
        Array.Fill(cost, float.PositiveInfinity);
        var dir = new byte[n];
        Array.Fill(dir, (byte)255);
        if (target < 0) return (cost, dir, target);
        var done = new bool[n];
        var pq = new PriorityQueue<int, float>();
        cost[target] = 0f;
        pq.Enqueue(target, 0f);
        while (pq.TryDequeue(out int c, out float pc))
        {
            if (done[c] || pc > cost[c]) continue;
            done[c] = true;
            int cx = c % w, cy = c / w;
            foreach ((int dx, int dy) in Dirs)
            {
                if (!Allowed(g, cx, cy, dx, dy)) continue;
                int nb = (cy + dy) * w + cx + dx;
                float nc = cost[c] + StepCost(dx, dy);
                if (nc < cost[nb])
                {
                    cost[nb] = nc;
                    pq.Enqueue(nb, nc);
                }
            }
        }
        for (int c = 0; c < n; c++)
        {
            if (c == target || float.IsPositiveInfinity(cost[c])) continue;
            int cx = c % w, cy = c / w;
            float best = float.PositiveInfinity;
            for (int d = 0; d < Dirs.Length; d++)
            {
                (int dx, int dy) = Dirs[d];
                if (!Allowed(g, cx, cy, dx, dy)) continue;
                float via = cost[(cy + dy) * w + cx + dx] + StepCost(dx, dy);
                if (via < best)
                {
                    best = via;
                    dir[c] = (byte)d;
                }
            }
        }
        return (cost, dir, target);
    }

    /// <summary>Bellman-Ford style relaxation to a fixed point: the slowest, most obviously correct cost oracle.</summary>
    public static float[] RelaxCosts(NavGrid g, int target)
    {
        int w = g.Width, n = w * g.Height;
        var cost = new float[n];
        Array.Fill(cost, float.PositiveInfinity);
        cost[target] = 0f;
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int c = 0; c < n; c++)
            {
                int cx = c % w, cy = c / w;
                foreach ((int dx, int dy) in Dirs)
                {
                    if (!Allowed(g, cx, cy, dx, dy)) continue;
                    float via = cost[(cy + dy) * w + cx + dx] + StepCost(dx, dy);
                    if (via < cost[c])
                    {
                        cost[c] = via;
                        changed = true;
                    }
                }
            }
        }
        return cost;
    }

    /// <summary>Heightmap from rows: digit = plateau level ('1' blocks become walls, their insides sealed pockets).</summary>
    public static NavGrid FromRows(params string[] rows)
    {
        int w = rows[0].Length, h = rows.Length;
        var levels = new byte[w * h];
        var elevations = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                levels[y * w + x] = (byte)(rows[y][x] - '0');
                elevations[y * w + x] = levels[y * w + x] * H;
            }
        return new NavGrid(new Heightmap(w, h, levels, elevations));
    }

    /// <summary>A 16 x 18 map: a level-1 plateau (rows 1-6) reached only by a 3-wide, 4-long ramp at x 6-8 down to open ground.</summary>
    public static NavGrid RampCorridor()
    {
        const int w = 16, h = 18, rampLength = 4;
        var levels = new byte[w * h];
        var elevations = new float[w * h];
        for (int y = 1; y <= 6; y++)
            for (int x = 1; x < w - 1; x++)
            {
                levels[y * w + x] = 1;
                elevations[y * w + x] = H;
            }
        for (int k = 1; k <= rampLength; k++)
            for (int j = 0; j < 3; j++)
                elevations[(6 + k) * w + 6 + j] = H * (rampLength + 1 - k) / (rampLength + 1);
        return new NavGrid(new Heightmap(w, h, levels, elevations));
    }

    /// <summary>The nav grid of a default 128 x 128 generated map.</summary>
    public static NavGrid Generated(ulong seed)
    {
        var rng = new SimRng(seed, RngStream.MapGen);
        return new NavGrid(MapGenerator.Generate(MapGenParams.Default, ref rng));
    }

    /// <summary>Passable cell indices in scan order.</summary>
    public static List<int> PassableCells(NavGrid g)
    {
        var cells = new List<int>();
        for (int y = 0; y < g.Height; y++)
            for (int x = 0; x < g.Width; x++)
                if (g.IsPassable(x, y)) cells.Add(y * g.Width + x);
        return cells;
    }
}
