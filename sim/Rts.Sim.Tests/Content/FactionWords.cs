using System.Text.RegularExpressions;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// The words that name a faction on their own (BUG-1081): each faction's id and the capitalised words of its display and
/// bonus names ("Malazan", "Empire", "Imperial", "Drill", "Whirlwind", "Apocalypse"). Generic text (a preset, a map) must
/// not say them, so "a Malazan quartermaster" is caught even though only "Malazan Empire" is a display name.
/// </summary>
internal static class FactionWords
{
    /// <summary>
    /// Name words a generic text may still use: "Army" (of the Apocalypse) is the plain word for any player's force.
    /// Words under four letters ("The", "of") are skipped by length.
    /// </summary>
    private static readonly string[] Generic = { "Army" };

    /// <summary>The faction words of <paramref name="d"/>, longest first, no duplicates (ignoring case).</summary>
    public static List<string> All(GameData d)
    {
        var words = new List<string>();
        foreach (FactionDef f in d.Factions)
        {
            foreach (string name in new[] { f.DisplayName, f.BonusDisplayName })
                foreach (string w in name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    if (w.Length >= 4 && char.IsUpper(w[0]) && Array.IndexOf(Generic, w) < 0) words.Add(w);
            words.Add(f.Key); // after the name words, so a duplicate keeps the capitalised spelling ("Malazan")
        }
        return words.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(w => w.Length).ThenBy(w => w, StringComparer.Ordinal).ToList();
    }

    /// <summary>The faction words <paramref name="text"/> says as whole words, ignoring case.</summary>
    public static List<string> In(GameData d, string text)
    {
        var found = new List<string>();
        foreach (string w in All(d))
            if (Regex.IsMatch(text, $@"\b{Regex.Escape(w)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                found.Add(w);
        return found;
    }
}
