using System.Text;

namespace Rts.Game;

/// <summary>
/// Pre-wraps a data <c>description</c> for a tooltip (M6-VH5, BUG-1081 item 2): Godot draws a tooltip, and an
/// <c>OptionButton</c> item's tooltip in its popup, as one unwrapped line, so a 180-character description was a panel about as
/// wide as the window.
/// </summary>
/// <remarks>
/// <para>Breaks at spaces only, so words stay whole: where the next word would take the line past <see cref="Width"/>
/// characters, the spaces before it become one line break (a word longer than the width gets a line of its own, unbroken).
/// A line break already in the text is kept and each of its lines is wrapped on its own. The words, their order and the
/// data's own line breaks are the text's: this only places the breaks.</para>
/// <para>At every line break, the data's or the wrap's, the wrap consumes the spaces beside it (M7-VH1, BUG-1126, the
/// Producer's ruling): a line never starts or ends with a space next to a break, so a space beside a data break or a double
/// space at a break never leaves a blank line, and a line passes the width only as one word longer than the width.
/// Spaces at the start or end of the whole text with no break beside them are kept as they are and left out of the wrap
/// (never a break, not counted toward the width), so they never leave an empty first or last line either (BUG-1115).
/// Elsewhere the text's spaces are unchanged (a double space inside a line stays).</para>
/// </remarks>
public static class TooltipWrap
{
    /// <summary>The longest line, in characters (about 470 px at the default 16 px font, measured on Raraku's widest line).</summary>
    public const int Width = 60;

    /// <summary><paramref name="text"/> with its words broken into lines of at most <paramref name="width"/> characters (see the remarks).</summary>
    public static string Wrap(string text, int width = Width)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        if (text.Length <= width && text.IndexOf('\n') < 0) return text;
        var sb = new StringBuilder(text.Length);
        int from = 0;
        while (true)
        {
            int nl = text.IndexOf('\n', from);
            int to = nl < 0 ? text.Length : nl;
            // The first data line's leading spaces and the last one's trailing spaces are the text's edges; any other
            // edge of a data line is beside a data break, and those spaces go.
            WrapLine(sb, text, from, to, keepLead: from == 0, keepTrail: nl < 0, width);
            if (nl < 0) break;
            sb.Append('\n');
            from = nl + 1;
        }
        return sb.ToString();
    }

    // One data line, text[from, to): greedily, each word on the current line if the spaces before it and it fit in the
    // width, else on a new line, the spaces before it consumed by the break.
    private static void WrapLine(StringBuilder sb, string text, int from, int to, bool keepLead, bool keepTrail, int width)
    {
        int start = from, end = to;
        while (start < end && text[start] == ' ') start++;
        while (end > start && text[end - 1] == ' ') end--;
        if (start == end)
        {
            // Only spaces: kept when nothing breaks beside them (the whole text is spaces), else consumed.
            if (keepLead && keepTrail) sb.Append(text, from, to - from);
            return;
        }
        if (keepLead) sb.Append(text, from, start - from);
        int i = start, lineLength = 0;
        bool first = true;
        while (i < end)
        {
            int gapStart = i;
            while (text[i] == ' ') i++;
            int wordStart = i;
            while (i < end && text[i] != ' ') i++;
            int gap = wordStart - gapStart, word = i - wordStart;
            if (first)
            {
                lineLength = word;
                first = false;
            }
            else if (lineLength + gap + word <= width)
            {
                sb.Append(text, gapStart, gap);
                lineLength += gap + word;
            }
            else
            {
                sb.Append('\n');
                lineLength = word;
            }
            sb.Append(text, wordStart, word);
        }
        if (keepTrail) sb.Append(text, end, to - end);
    }
}
