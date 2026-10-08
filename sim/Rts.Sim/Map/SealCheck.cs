using System;

namespace Rts.Sim.Map;

/// <summary>
/// The never-seal rule for building placement (M3-3, BUG-0078): would blocking a footprint cut some passable cells
/// off from others? The resource placer's ring-then-flood test (<c>ResourcePlacer.StaysConnected</c>) on scratch
/// it borrows (the flow-field builder's queue storage, idle outside a field build), so a query never allocates.
/// </summary>
/// <remarks>
/// A 4-connected path between two cells that crossed the footprint enters it from a passable cell 4-adjacent to it
/// and leaves it into another, so blocking the footprint separates nothing exactly when those cells (the "sides")
/// still reach each other without it. First the ring test: when every cell 8-adjacent to the footprint is passable,
/// they form a loop round it and the answer is yes without touching the rest of the map. Otherwise one flood from
/// every side at once, through passable cells outside the footprint, each side's cells carrying its label: where two
/// floods meet their labels merge (yes once one label is left), and a label whose cells are all expanded without
/// meeting another is a region cut off (no). So a "no" costs about the cut-off pocket times the number of sides, not
/// the map. Unlike the placer it doesn't need the whole map connected beforehand: it keeps whatever regions there are
/// (a destroyed building enclosed by others leaves a pocket that is then left alone).
/// </remarks>
internal sealed class SealCheck
{
    // Most sides a footprint has: 2 (w + h) cells for the largest footprint (DataLimits.MaxFootprint).
    private const int MaxSides = 4 * Data.DataLimits.MaxFootprint;

    private readonly NavGrid _grid;
    // Cells [0, n): 0 unvisited, -1 footprint, else the label (1 to MaxSides) that reached it; [n, 2n): the queue.
    private readonly int[] _scratch;
    private readonly int[] _parent = new int[MaxSides + 1];
    private readonly int[] _queued = new int[MaxSides + 1];
    // The last flood's question and answer (BUG-0096): the answer depends only on the grid, which can't change without
    // NavGrid.Version moving, so the same footprint asked again at the same version is answered without a flood.
    private int _memoX0 = -1, _memoY0, _memoW, _memoH, _memoVersion;
    private bool _memoAnswer;

    /// <summary>A check on <paramref name="grid"/> using <paramref name="scratch"/> (at least two ints a cell), which it may overwrite at every query.</summary>
    public SealCheck(NavGrid grid, int[] scratch)
    {
        if (scratch.Length < 2 * grid.Width * grid.Height) throw new ArgumentException("scratch too small", nameof(scratch));
        _grid = grid;
        _scratch = scratch;
    }

    /// <summary>True if blocking the <paramref name="fw"/> x <paramref name="fh"/> footprint at (<paramref name="x0"/>, <paramref name="y0"/>) (in the map, every cell passable) leaves every two passable cells that connect now still connected.</summary>
    /// <remarks>
    /// The last flood's answer is kept with its footprint and <see cref="NavGrid.Version"/> (M3-H2, BUG-0096): a group
    /// Build of 100 workers at one sealing spot floods once, not 100 times. Derived scratch, not hashed: it only
    /// repeats an answer the flood would give again.
    /// </remarks>
    public bool KeepsConnected(int x0, int y0, int fw, int fh)
    {
        NavFlags[] flags = _grid.Flags;
        if (RingAllPassable(flags, _grid.Width, x0, y0, fw, fh)) return true;
        int version = _grid.Version;
        if (x0 == _memoX0 && y0 == _memoY0 && fw == _memoW && fh == _memoH && version == _memoVersion) return _memoAnswer;
        bool answer = Flood(flags, x0, y0, fw, fh);
        Floods++;
        (_memoX0, _memoY0, _memoW, _memoH, _memoVersion, _memoAnswer) = (x0, y0, fw, fh, version, answer);
        return answer;
    }

    /// <summary>Number of floods run so far (diagnostics and tests; derived, not hashed).</summary>
    internal long Floods { get; private set; }

    /// <summary>Test seam: forgets the kept answer, so the next query floods.</summary>
    internal void ForgetForTests() => _memoX0 = -1;

    /// <summary>The labelled flood from the footprint's passable sides; true once they all meet.</summary>
    private bool Flood(NavFlags[] flags, int x0, int y0, int fw, int fh)
    {
        int w = _grid.Width, n = w * _grid.Height;

        int[] s = _scratch;
        Array.Clear(s, 0, n);
        for (int y = y0; y < y0 + fh; y++)
            for (int x = x0; x < x0 + fw; x++)
                s[y * w + x] = -1;
        int head = n, tail = n, labels = 0;
        for (int k = 0; k < 2 * (fw + fh); k++)
        {
            RingCell(x0, y0, fw, fh, k, out int x, out int y);
            if (!_grid.IsPassable(x, y)) continue;
            labels++;
            _parent[labels] = labels;
            _queued[labels] = 1;
            s[y * w + x] = labels;
            s[tail++] = y * w + x;
        }
        int roots = labels;
        if (roots <= 1) return true;

        while (head < tail)
        {
            int i = s[head++];
            int li = Find(s[i]);
            _queued[li]--;
            // A passable cell is never on the border ring, so its 4 neighbors are in bounds.
            for (int d = 0; d < 4; d++)
            {
                int j = d == 0 ? i - 1 : d == 1 ? i + 1 : d == 2 ? i - w : i + w;
                int v = s[j];
                if (v == 0)
                {
                    if ((flags[j] & NavFlags.Blocked) != 0) continue;
                    s[j] = li;
                    _queued[li]++;
                    s[tail++] = j;
                }
                else if (v > 0)
                {
                    int lj = Find(v);
                    if (lj == li) continue;
                    _parent[lj] = li;
                    _queued[li] += _queued[lj];
                    if (--roots == 1) return true;
                }
            }
            if (_queued[li] == 0) return false; // li's whole region is expanded and met no other side
        }
        return false;
    }

    private int Find(int label)
    {
        while (_parent[label] != label) label = _parent[label];
        return label;
    }

    /// <summary>True if every cell 8-adjacent to the footprint is in the map and passable.</summary>
    private static bool RingAllPassable(NavFlags[] flags, int w, int x0, int y0, int fw, int fh)
    {
        int h = flags.Length / w;
        if (x0 < 1 || y0 < 1 || x0 + fw >= w || y0 + fh >= h) return false;
        for (int x = x0 - 1; x <= x0 + fw; x++)
        {
            if ((flags[(y0 - 1) * w + x] & NavFlags.Blocked) != 0 || (flags[(y0 + fh) * w + x] & NavFlags.Blocked) != 0) return false;
        }
        for (int y = y0; y < y0 + fh; y++)
        {
            if ((flags[y * w + x0 - 1] & NavFlags.Blocked) != 0 || (flags[y * w + x0 + fw] & NavFlags.Blocked) != 0) return false;
        }
        return true;
    }

    /// <summary>The <paramref name="k"/>-th cell (0 to 2 (w + h) - 1) 4-adjacent to the footprint: the row above, the row below, the column left, the column right.</summary>
    internal static void RingCell(int x0, int y0, int fw, int fh, int k, out int x, out int y)
    {
        if (k < fw) { x = x0 + k; y = y0 - 1; return; }
        k -= fw;
        if (k < fw) { x = x0 + k; y = y0 + fh; return; }
        k -= fw;
        if (k < fh) { x = x0 - 1; y = y0 + k; return; }
        x = x0 + fw;
        y = y0 + k - fh;
    }
}
