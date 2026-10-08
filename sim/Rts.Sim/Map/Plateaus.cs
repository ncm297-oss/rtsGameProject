using System;

namespace Rts.Sim.Map;

/// <summary>
/// The terrain's plateaus (M3-H2, BUG-0097): each 4-connected component of passable cells of one level, as the grid
/// stood at load (terrain only: cliffs, the border and sealed pockets are in no plateau; nodes and buildings don't
/// change them). A ramp reports its lower level, so it belongs to the ground at its foot and joins nothing above it:
/// two plateaus of the same level are different plateaus unless same-level ground joins them. Each plateau has its
/// own bounding box. Derived from terrain, so not hashed; levels never change after load, so neither do plateaus.
/// </summary>
/// <remarks>The production spawn and construction push-out ring search (<c>FreeCellSearch</c>) stays on one plateau and inside its box.</remarks>
internal sealed class Plateaus
{
    private readonly int[] _id;
    // Per plateau: min x, min y, max x, max y of its cells (inclusive).
    private readonly int[] _bounds;

    /// <summary>
    /// Labels the passable cells of <paramref name="grid"/> as it stands (call before anything but terrain blocks a
    /// cell). Load time; the per-cell labels and the flood queue reuse the grid's load scratch when it still has it.
    /// </summary>
    public Plateaus(NavGrid grid)
    {
        int w = grid.Width, h = grid.Height, n = w * h;
        (int[]? cells, int[]? scratchQueue) = grid.TakeLoadScratch();
        _id = cells ?? new int[n];
        Array.Fill(_id, -1);
        int[] queue = scratchQueue ?? new int[n];
        var bounds = new System.Collections.Generic.List<int>();
        NavFlags[] flags = grid.Flags;
        int count = 0;
        for (int start = 0; start < n; start++)
        {
            if (_id[start] >= 0 || (flags[start] & NavFlags.Blocked) != 0) continue;
            int level = grid.LevelAt(start % w, start / w);
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            int head = 0, tail = 0;
            _id[start] = count;
            queue[tail++] = start;
            while (head < tail)
            {
                int c = queue[head++], x = c % w, y = c / w;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
                // A passable cell is never on the border ring, so its 4 neighbors are in bounds.
                for (int d = 0; d < 4; d++)
                {
                    int j = d == 0 ? c - 1 : d == 1 ? c + 1 : d == 2 ? c - w : c + w;
                    if (_id[j] >= 0 || (flags[j] & NavFlags.Blocked) != 0 || grid.LevelAt(j % w, j / w) != level) continue;
                    _id[j] = count;
                    queue[tail++] = j;
                }
            }
            bounds.Add(minX);
            bounds.Add(minY);
            bounds.Add(maxX);
            bounds.Add(maxY);
            count++;
        }
        _bounds = bounds.ToArray();
        Count = count;
    }

    /// <summary>Number of plateaus (ids 0 to this minus 1).</summary>
    public int Count { get; }

    /// <summary>The plateau of cell <paramref name="cell"/> (<c>y * Width + x</c>), or -1 for a cell in none (terrain-blocked, or off the map).</summary>
    public int At(int cell) => (uint)cell < (uint)_id.Length ? _id[cell] : -1;

    /// <summary>The bounding box of plateau <paramref name="plateau"/> (inclusive cell coordinates); false for no such plateau.</summary>
    public bool Bounds(int plateau, out int minX, out int minY, out int maxX, out int maxY)
    {
        minX = minY = maxX = maxY = 0;
        if ((uint)plateau >= (uint)Count) return false;
        minX = _bounds[4 * plateau];
        minY = _bounds[4 * plateau + 1];
        maxX = _bounds[4 * plateau + 2];
        maxY = _bounds[4 * plateau + 3];
        return true;
    }
}
