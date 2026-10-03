using Rts.Sim.Map;

namespace Rts.Sim.Tests.QA;

/// <summary>QA's own terrain invariant checker (M1-3), written independently of the developer's MapAssert.</summary>
/// <remarks>
/// Returns every violation (capped) instead of the first, so a sweep can report how widespread a problem is.
/// Uses only public APIs: Heightmap and NavGrid queries, plus a plain array flood fill.
/// </remarks>
public static class MapQaChecker
{
    private static readonly (int Dx, int Dy)[] Four = { (1, 0), (-1, 0), (0, 1), (0, -1) };
    private static readonly (int Dx, int Dy)[] Diagonals = { (1, 1), (1, -1), (-1, 1), (-1, -1) };

    /// <summary>All broken invariants (at most <paramref name="cap"/>); empty if the map is sound.</summary>
    public static List<string> Check(Heightmap hm, NavGrid nav, bool requireAllLevels, double minPassableFraction = 0.5, int cap = 20)
    {
        var errors = new List<string>();
        void Fail(string s)
        {
            if (errors.Count < cap) errors.Add(s);
        }

        int w = hm.Width, h = hm.Height;
        if (nav.Width != w || nav.Height != h)
        {
            Fail($"size mismatch hm {w}x{h} nav {nav.Width}x{nav.Height}");
            return errors;
        }

        var levelsPresent = new bool[MapConstants.LevelCount];
        int passable = 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int lv = hm.LevelAt(x, y);
                float e = hm.ElevationAt(x, y);
                if (lv < 0 || lv > MapConstants.MaxLevel) Fail($"({x},{y}) level {lv}");
                else levelsPresent[lv] = true;
                if (!float.IsFinite(e)) Fail($"({x},{y}) elevation {e}");
                float floor = lv * MapConstants.LevelHeight;
                bool ramp = hm.IsRamp(x, y);
                if (ramp && !(e > floor && e < floor + MapConstants.LevelHeight)) Fail($"({x},{y}) ramp elevation {e} not strictly inside level {lv}..{lv + 1}");
                if (!ramp && e != floor) Fail($"({x},{y}) plateau elevation {e} != {floor}");
                if (nav.LevelAt(x, y) != lv) Fail($"({x},{y}) nav level {nav.LevelAt(x, y)} != hm level {lv}");
                NavFlags f = nav.FlagsAt(x, y);
                if (((f & NavFlags.Ramp) != 0) != ramp) Fail($"({x},{y}) ramp flag {f} vs heightmap ramp {ramp}");
                bool open = nav.IsPassable(x, y);
                if (open == ((f & NavFlags.Blocked) != 0)) Fail($"({x},{y}) IsPassable {open} but flags {f}");
                if ((f & NavFlags.Cliff) != 0 && (f & NavFlags.Blocked) == 0) Fail($"({x},{y}) cliff without blocked");
                byte cost = nav.CostAt(x, y);
                if (cost != (open ? 1 : 255)) Fail($"({x},{y}) cost {cost} passable {open}");
                if ((x == 0 || y == 0 || x == w - 1 || y == h - 1) && open) Fail($"({x},{y}) border passable");
                if (!open) continue;
                passable++;

                // 4-neighbour step rule (criterion 3).
                foreach ((int dx, int dy) in Four)
                {
                    int nx = x + dx, ny = y + dy;
                    if (!nav.IsPassable(nx, ny)) continue;
                    int dl = nav.LevelAt(nx, ny) - lv;
                    if (Math.Abs(dl) > 1) Fail($"({x},{y})->({nx},{ny}) level step {dl}");
                    // dl == 1 (neighbour higher): the lower one must be a ramp (ramps keep the lower level).
                    if (dl == 1 && !ramp) Fail($"({x},{y})->({nx},{ny}) climbs a level from a non-ramp cell");
                    if (dl == -1 && !hm.IsRamp(nx, ny)) Fail($"({x},{y})->({nx},{ny}) drops a level onto a non-ramp cell");
                }

                // Diagonal leak: two passable diagonal cells on different levels where neither
                // orthogonal side cell is blocked would let an 8-connected flow field (no corner
                // cutting = both sides passable) cross a level change without a ramp.
                foreach ((int dx, int dy) in Diagonals)
                {
                    int nx = x + dx, ny = y + dy;
                    if (!nav.IsPassable(nx, ny)) continue;
                    int dl = nav.LevelAt(nx, ny) - lv;
                    if (dl == 0) continue;
                    bool sidesOpen = nav.IsPassable(x + dx, y) && nav.IsPassable(x, y + dy);
                    if (!sidesOpen) continue;
                    if (Math.Abs(dl) > 1) Fail($"({x},{y})->({nx},{ny}) diagonal leak across {dl} levels");
                    bool lowerIsRamp = dl > 0 ? ramp : hm.IsRamp(nx, ny);
                    if (!lowerIsRamp) Fail($"({x},{y})->({nx},{ny}) diagonal level change {dl} with no ramp and both sides open");
                }
            }
        }

        if (passable != nav.PassableCount) Fail($"PassableCount {nav.PassableCount} != counted {passable}");
        if (passable < minPassableFraction * w * h) Fail($"passable {passable}/{w * h} below {minPassableFraction:P0}");
        int reached = Flood(nav, out _);
        if (reached != passable) Fail($"flood reached {reached} of {passable} passable cells");
        if (requireAllLevels)
        {
            for (int l = 0; l < levelsPresent.Length; l++)
                if (!levelsPresent[l]) Fail($"level {l} missing");
        }
        return errors;
    }

    /// <summary>Plain 4-connected flood fill from the first passable cell; returns cells reached.</summary>
    public static int Flood(NavGrid nav, out bool[] seen)
    {
        int w = nav.Width, h = nav.Height;
        seen = new bool[w * h];
        var stack = new int[w * h];
        int top = 0;
        for (int i = 0; i < w * h; i++)
        {
            if (nav.IsPassable(i % w, i / w))
            {
                stack[top++] = i;
                seen[i] = true;
                break;
            }
        }
        int count = 0;
        while (top > 0)
        {
            int i = stack[--top];
            count++;
            int x = i % w, y = i / w;
            foreach ((int dx, int dy) in Four)
            {
                int nx = x + dx, ny = y + dy;
                if (!nav.IsPassable(nx, ny)) continue;
                int j = ny * w + nx;
                if (seen[j]) continue;
                seen[j] = true;
                stack[top++] = j;
            }
        }
        return count;
    }

    /// <summary>True if every level 0..MaxLevel has at least one passable cell.</summary>
    public static bool AllLevelsPassable(NavGrid nav)
    {
        var seen = new bool[MapConstants.LevelCount];
        for (int y = 0; y < nav.Height; y++)
            for (int x = 0; x < nav.Width; x++)
                if (nav.IsPassable(x, y)) seen[nav.LevelAt(x, y)] = true;
        return seen.All(b => b);
    }
}
