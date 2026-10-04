using System.Text;
using Rts.Sim.Map;

namespace Rts.Sim.Tests;

/// <summary>Checks the M1-3 terrain invariants on a generated map and renders maps as text for test output.</summary>
public static class MapAssert
{
    /// <summary>Returns the first broken invariant, or null if the map is sound.</summary>
    /// <remarks>Plain ifs instead of xUnit asserts per cell: 300 maps x 16K cells must stay fast.</remarks>
    public static string? FindViolation(Heightmap hm, NavGrid nav, bool requireAllLevels, float minPassableFraction = 0.5f)
    {
        int w = hm.Width, h = hm.Height;
        if (nav.Width != w || nav.Height != h) return "nav grid size differs from heightmap";
        var passableLevels = new bool[MapConstants.LevelCount];
        int passable = 0;
        float maxRise = MapConstants.MaxRampSlope * MapConstants.CellSize + 1e-4f; // docs/02: nothing steeper than 30 degrees
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int level = hm.LevelAt(x, y);
                float e = hm.ElevationAt(x, y);
                float floor = level * MapConstants.LevelHeight;
                NavFlags f = nav.FlagsAt(x, y);
                bool open = nav.IsPassable(x, y);
                string at = $"cell ({x}, {y})";
                if (level < 0 || level > MapConstants.MaxLevel) return $"{at}: level {level} out of range";
                if (nav.LevelAt(x, y) != level) return $"{at}: nav level differs";
                bool ramp = hm.IsRamp(x, y);
                if (ramp != ((f & NavFlags.Ramp) != 0)) return $"{at}: ramp flag mismatch";
                if (ramp && !(e > floor && e < floor + MapConstants.LevelHeight)) return $"{at}: ramp height {e} not strictly between levels";
                if (!ramp && e != floor) return $"{at}: plateau height {e} != level x LevelHeight";
                if ((f & NavFlags.Cliff) != 0 && open) return $"{at}: cliff is passable";
                if (nav.CostAt(x, y) != (open ? 1 : 255)) return $"{at}: cost {nav.CostAt(x, y)} for passable={open}";
                bool border = x == 0 || y == 0 || x == w - 1 || y == h - 1;
                if (border && open) return $"{at}: border cell is passable";
                if (!open) continue;
                passable++;
                passableLevels[level] = true;
                foreach ((int nx, int ny) in new[] { (x + 1, y), (x, y + 1) })
                {
                    if (!nav.IsPassable(nx, ny)) continue;
                    int dl = Math.Abs(nav.LevelAt(nx, ny) - level);
                    if (dl > 1) return $"{at} -> ({nx}, {ny}): level step {dl}";
                    if (dl == 1 && !ramp && !hm.IsRamp(nx, ny)) return $"{at} -> ({nx}, {ny}): level step without a ramp";
                    float rise = Math.Abs(hm.ElevationAt(nx, ny) - e);
                    if (rise > maxRise) return $"{at} -> ({nx}, {ny}): passable step rises {rise} m, steeper than 30 degrees";
                }
            }
        }
        if (passable != nav.PassableCount) return $"PassableCount {nav.PassableCount} but counted {passable}";
        if (passable < minPassableFraction * w * h) return $"only {passable} of {w * h} cells passable";
        int reached = FloodCount(nav);
        if (reached != passable) return $"passable cells not connected: flood reached {reached} of {passable}";
        if (requireAllLevels)
        {
            for (int l = 0; l < passableLevels.Length; l++)
                if (!passableLevels[l]) return $"no passable cell at level {l}";
        }
        return null;
    }

    /// <summary>Number of passable cells 4-connected to the first passable cell.</summary>
    public static int FloodCount(NavGrid nav)
    {
        int w = nav.Width, h = nav.Height;
        var seen = new bool[w * h];
        var queue = new Queue<(int X, int Y)>();
        for (int i = 0; i < w * h && queue.Count == 0; i++)
        {
            if (nav.IsPassable(i % w, i / w))
            {
                seen[i] = true;
                queue.Enqueue((i % w, i / w));
            }
        }
        int count = 0;
        while (queue.Count > 0)
        {
            (int x, int y) = queue.Dequeue();
            count++;
            foreach ((int nx, int ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
            {
                if (!nav.IsPassable(nx, ny) || seen[ny * w + nx]) continue;
                seen[ny * w + nx] = true;
                queue.Enqueue((nx, ny));
            }
        }
        return count;
    }

    /// <summary>One character per cell: '#' cliff, 'x' other blocked, '/' ramp, else the level digit.</summary>
    public static string Render(NavGrid nav)
    {
        var sb = new StringBuilder();
        for (int y = 0; y < nav.Height; y++)
        {
            for (int x = 0; x < nav.Width; x++)
            {
                NavFlags f = nav.FlagsAt(x, y);
                char c = (f & NavFlags.Cliff) != 0 ? '#'
                    : (f & NavFlags.Blocked) != 0 ? 'x'
                    : (f & NavFlags.Ramp) != 0 ? '/'
                    : (char)('0' + nav.LevelAt(x, y));
                sb.Append(c);
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
