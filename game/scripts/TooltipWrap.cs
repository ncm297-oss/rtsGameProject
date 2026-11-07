using System.Text;

namespace Rts.Game;

/// <summary>
/// Pre-wraps a data <c>description</c> for a tooltip (M6-VH5, BUG-1081 item 2): Godot draws a tooltip, and an
/// <c>OptionButton</c> item's tooltip in its popup, as one unwrapped line, so a 180-character description was a panel about as
/// wide as the window.
/// </summary>
/// <remarks>
/// Breaks at spaces only, so words stay whole: a space where the next word would take the line past <see cref="Width"/>
/// characters becomes a line break, and nothing else changes (a word longer than the width gets a line of its own, unbroken).
/// So turning every break the wrap added back into a space gives the data text again; a line break already in the text is
/// kept and each of its lines is wrapped on its own. The text itself stays the data's: this only places the breaks.
/// </remarks>
public static class TooltipWrap
{
    /// <summary>The longest line, in characters (roughly 450 px at the default 16 px font).</summary>
    public const int Width = 60;

    /// <summary><paramref name="text"/> with a line break at the last space before each line would pass <paramref name="width"/> characters.</summary>
    public static string Wrap(string text, int width = Width)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= width) return text ?? "";
        var sb = new StringBuilder(text);
        int lineStart = 0, lastSpace = -1;
        for (int i = 0; i < sb.Length; i++)
        {
            char c = sb[i];
            if (c == '\n')
            {
                lineStart = i + 1;
                lastSpace = -1;
                continue;
            }
            if (c == ' ') lastSpace = i;
            // The line [lineStart, i] is now one character too long: break at its last space, if it has one.
            if (i - lineStart + 1 > width && lastSpace >= lineStart)
            {
                sb[lastSpace] = '\n';
                lineStart = lastSpace + 1;
                lastSpace = -1;
            }
        }
        return sb.ToString();
    }
}
