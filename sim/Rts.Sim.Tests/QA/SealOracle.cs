using Rts.Sim.Map;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M3-3): an independent oracle for the never-seal rule, written from the rule's statement in docs/03 ("every two
/// passable cells that connect now still connect without the footprint"), not from <c>SealCheck</c>'s algorithm.
/// Plain whole-map BFS on 4-connected passable cells (the flow field forbids corner cutting, so 4-connectivity is the
/// movement graph's connectivity). Slow and allocating on purpose: tests only.
/// </summary>
public static class SealOracle
{
    private static readonly int[] Dx = { 1, -1, 0, 0 }, Dy = { 0, 0, 1, -1 };

    /// <summary>Component label per cell (4-connected passable cells), -1 for blocked; <paramref name="extraBlocked"/> cells count as blocked.</summary>
    public static int[] Labels(NavGrid g, Func<int, int, bool>? extraBlocked = null)
    {
        int w = g.Width, h = g.Height, n = w * h;
        var label = new int[n];
        Array.Fill(label, -2);
        var q = new Queue<int>();
        int next = 0;
        for (int s = 0; s < n; s++)
        {
            if (label[s] != -2) continue;
            int sx = s % w, sy = s / w;
            if (!g.IsPassable(sx, sy) || (extraBlocked?.Invoke(sx, sy) ?? false))
            {
                label[s] = -1;
                continue;
            }
            label[s] = next;
            q.Enqueue(s);
            while (q.Count > 0)
            {
                int c = q.Dequeue();
                for (int d = 0; d < 4; d++)
                {
                    int x = c % w + Dx[d], y = c / w + Dy[d];
                    if (!g.InBounds(x, y)) continue;
                    int j = y * w + x;
                    if (label[j] != -2) continue;
                    if (!g.IsPassable(x, y) || (extraBlocked?.Invoke(x, y) ?? false)) continue; // decided when the scan reaches it
                    label[j] = next;
                    q.Enqueue(j);
                }
            }
            next++;
        }
        return label;
    }

    /// <summary>True if blocking the footprint (every cell passable) would split two cells that are connected now.</summary>
    public static bool Seals(NavGrid g, int x0, int y0, int fw, int fh)
    {
        bool InFp(int x, int y) => x >= x0 && x < x0 + fw && y >= y0 && y < y0 + fh;
        int[] before = Labels(g);
        int[] after = Labels(g, InFp);
        return LostConnection(before, after) >= 0;
    }

    /// <summary>
    /// The first cell whose "before" component maps to two different "after" components (a connection lost), or -1.
    /// Cells blocked in either labelling are skipped (the footprint, and any cell that opened or closed in between).
    /// </summary>
    public static int LostConnection(int[] before, int[] after)
    {
        var map = new Dictionary<int, int>();
        for (int i = 0; i < before.Length; i++)
        {
            if (before[i] < 0 || after[i] < 0) continue;
            if (map.TryGetValue(before[i], out int a))
            {
                if (a != after[i]) return i;
            }
            else map[before[i]] = after[i];
        }
        return -1;
    }

    /// <summary>True if every cell 8-adjacent to the footprint is in the map and passable (the trivially "no seal" case).</summary>
    public static bool RingOpen(NavGrid g, int x0, int y0, int fw, int fh)
    {
        for (int y = y0 - 1; y <= y0 + fh; y++)
            for (int x = x0 - 1; x <= x0 + fw; x++)
            {
                bool inside = x >= x0 && x < x0 + fw && y >= y0 && y < y0 + fh;
                if (!inside && !g.IsPassable(x, y)) return false;
            }
        return true;
    }
}
