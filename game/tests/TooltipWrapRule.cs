namespace Rts.Game.Tests;

/// <summary>
/// The rule a <see cref="TooltipWrap.Wrap"/> output must keep (M7-VH1, BUG-1126, the Producer's ruling), shared by
/// <c>QaVh5Test</c>'s and <c>QaVh6Test</c>'s fuzz. Walking the text and the output side by side: every word and every data
/// line break is the text's, in order; a run of spaces is either kept whole or consumed whole beside a line break (the
/// data's or one the wrap added in its place); the text's own edge spaces with no break beside them are kept. On top: no
/// line edge beside a break is a space, the wrap makes no blank line, a line over the width holds no space (edge spaces
/// kept at the text's ends aside), and a break is added only where the next word would not fit (greedy).
/// </summary>
public static class TooltipWrapRule
{
    /// <summary>Why <paramref name="w"/> is not a valid wrap of <paramref name="text"/> at <paramref name="width"/>, or null when it is; <paramref name="blankMade"/> is whether the fault is a blank line the wrap made.</summary>
    public static string? Fault(string text, string w, int width, out bool blankMade)
    {
        blankMade = false;
        // The text's edges: spaces before the first non-space and after the last (kept unless a data break is beside them).
        int i = 0, j = 0;
        // Per added break: its output index and the spaces it consumed.
        var added = new System.Collections.Generic.List<(int At, int Gap)>();
        while (i < text.Length)
        {
            if (j < w.Length && text[i] == w[j])
            {
                i++;
                j++;
                continue;
            }
            if (text[i] != ' ') return $"text character {i} '{Esc(text[i])}' is not output character {j}";
            int run = i;
            while (i < text.Length && text[i] == ' ') i++;
            int gap = i - run;
            bool afterBreak = j > 0 && w[j - 1] == '\n';
            if (i == text.Length)
            {
                // Trailing spaces dropped: only right after a data break (the output ends with it).
                if (j != w.Length) return $"trailing spaces at {run} dropped but the output goes on at {j}";
                if (!afterBreak) return $"the text's trailing spaces at {run} dropped with no break before them";
                return Lines(text, w, width, added);
            }
            if (text[i] == '\n')
            {
                // Spaces before a data break consumed; the data break itself is matched next.
                if (j >= w.Length || w[j] != '\n') return $"spaces at {run} before a data break dropped, output {j} is not the break";
                continue;
            }
            // A word follows the spaces: either an added break replaced them, or a break before them consumed them.
            if (j < w.Length && w[j] == '\n')
            {
                if (j == 0 || afterBreak)
                {
                    blankMade = true;
                    return $"a break added at output {j} makes a blank line";
                }
                added.Add((j, gap));
                j++;
                continue;
            }
            if (!afterBreak) return $"spaces at {run} dropped in the middle of a line (output {j})";
        }
        if (j != w.Length) return $"output has {w.Length - j} characters past the text";
        return Lines(text, w, width, added);
    }

    private static string? Lines(string text, string w, int width, System.Collections.Generic.List<(int At, int Gap)> added)
    {
        string[] lines = w.Split('\n');
        for (int li = 0; li < lines.Length; li++)
        {
            string l = lines[li];
            if (li > 0 && l.StartsWith(' ')) return $"line {li} starts with a space after a break";
            if (li < lines.Length - 1 && l.EndsWith(' ')) return $"line {li} ends with a space before a break";
            // The kept edges (the first line's lead, the last line's trail) are not counted toward the width.
            string m = l;
            if (li == 0) m = m.TrimStart(' ');
            if (li == lines.Length - 1) m = m.TrimEnd(' ');
            if (m.Length > width && m.Contains(' ')) return $"line {li} of {m.Length} (> {width}) holds a space";
        }
        // Greedy: the line before an added break plus the spaces it consumed plus the next word would not have fit.
        foreach ((int at, int gap) in added)
        {
            int lineStart = w.LastIndexOf('\n', at - 1) + 1;
            int lineLength = at - lineStart;
            if (lineStart == 0) lineLength -= LeadingSpaces(w);
            int next = at + 1, word = 0;
            while (next + word < w.Length && w[next + word] != ' ' && w[next + word] != '\n') word++;
            if (lineLength + gap + word <= width) return $"a break at output {at} though the next word ({word}) fit after {lineLength} + {gap} spaces";
        }
        return null;
    }

    private static int LeadingSpaces(string s)
    {
        int n = 0;
        while (n < s.Length && s[n] == ' ') n++;
        return n;
    }

    private static string Esc(char c) => c == '\n' ? "\n" : c.ToString();
}
