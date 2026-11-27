using System.Globalization;
using System.Text.RegularExpressions;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D39: a unit's limit (M8-4, <c>units.json</c> <c>maxAlive</c>, docs/03 "Implementation (M8-4)") in its description, read
/// both ways as D35's <see cref="UnitHitBonusText"/> reads the hit bonuses. A number after limit words ("at most", "no more
/// than", "never more than", "up to", "a maximum of", "limited to", "capped at") that counts how many of the unit are alive
/// (D-H11, BUG-1880: the number then "alive" / "live" / "living"; "of them" or the unit's own name then one of those or
/// "at once"; another noun then said to live. Never a count of targets or projectiles: see <see cref="Stated"/>) is the
/// unit limit: it must be the unit's <c>maxAlive</c>, and a unit with no <c>maxAlive</c> must state none. A unit with a <c>maxAlive</c> must state it. A number followed by a percent, metres or
/// seconds is never a limit ("up to 30%"), and a count written in words ("three at a time") is not read. Problems read
/// <c>&lt;unit id&gt; description maxAlive: ...</c>.
/// </summary>
public static class UnitLimitText
{
    public const string Field = "maxAlive";

    private const RegexOptions Words = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    /// <summary>"No more than 7", "at most 7": limit words and a whole number that is not a percent, a distance or a time.</summary>
    private static readonly Regex Limit = new(
        @"\b(?:at most|no more than|never more than|up to|a maximum of|limited to|capped at)\s+(?<n>\d+)\b(?!\.\d|\s*(?:%|per ?cent\b|m\b|metres?\b|meters?\b|s\b|sec(?:ond)?s?\b))",
        Words);

    /// <summary>The words that say the counted thing is alive.</summary>
    private static readonly Regex AliveWords = new(@"\b(?:alive|live|living)\b", Words);

    private static readonly Regex AtOnce = new(@"\bat once\b", Words);

    /// <summary>A word of a clause (letters, with inner hyphens or apostrophes: "War-dogs", "Breaker's").</summary>
    private static readonly Regex Word = new(@"[A-Za-z]+(?:['’-][A-Za-z]+)*", RegexOptions.CultureInvariant);

    /// <summary>Words that may stand between a count and its "live" without being a counted noun ("7 can be alive", "7 Hounds may ever live").</summary>
    private static readonly HashSet<string> Linking = new(StringComparer.OrdinalIgnoreCase)
        { "can", "may", "might", "could", "will", "would", "shall", "ever", "be", "are", "is", "stay", "remain" };

    /// <summary>Words after a count that are not a counted noun ("at most 6 alive on the field", "up to 3 at once").</summary>
    private static readonly HashSet<string> Prepositions = new(StringComparer.OrdinalIgnoreCase)
        { "on", "in", "at", "within", "from", "near", "across", "around", "for", "during", "while", "and", "or", "but", "per", "of", "there", "here", "to", "beside", "inside" };

    /// <summary>Every unit of <paramref name="units"/> through <see cref="Problems(UnitDef, string?)"/>.</summary>
    public static List<string> Problems(IEnumerable<UnitDef> units) => units.SelectMany(u => Problems(u)).ToList();

    /// <summary>
    /// The limits <paramref name="text"/> states: each number read as a unit limit, with the words that state it.
    /// <paramref name="unitName"/> is the unit's display name, so "at most 7 War-dogs at once" reads on the War-dog.
    /// </summary>
    /// <remarks>
    /// D-H11 (BUG-1880): what follows the number in its clause decides whether it counts the unit. The number counts the
    /// unit when it is followed by (after any "alive" / "living") nothing or a linking word and then an alive word ("No more
    /// than 7 can live at once", "Never more than 7 alive"), by "of them" or the unit's own name (up to its head noun) with
    /// an alive word or "at once" in the clause ("at most 7 War-dogs at once", "up to 7 of them alive"), or by any other noun that
    /// is then said to live ("No more than 7 Hounds live at once"). Any other noun is a count of targets or projectiles
    /// ("Strikes up to 3 foes at once", "at most 2 alive targets at once"), and a bare "at once" is not a limit.
    /// </remarks>
    public static List<(int Limit, string Token)> Stated(string text, string? unitName = null)
    {
        var stated = new List<(int, string)>();
        foreach (Match m in Limit.Matches(text))
            if (CountsTheUnit(ClauseAfter(text, m.Index + m.Length), unitName))
                stated.Add((int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture), m.Value.Trim()));
        return stated;
    }

    /// <summary>Whether <paramref name="rest"/> (the clause after a limit's number) makes the number a count of the unit alive.</summary>
    private static bool CountsTheUnit(string rest, string? unitName)
    {
        string[] words = Word.Matches(rest).Select(w => w.Value).ToArray();
        int i = 0;
        bool alive = false;
        while (i < words.Length && AliveWords.IsMatch(words[i]) && !words[i].Equals("live", StringComparison.OrdinalIgnoreCase)) { alive = true; i++; }
        bool aliveAfter = AliveWords.IsMatch(string.Join(" ", words.Skip(i)));
        if (i == words.Length || Linking.Contains(words[i]) || words[i].Equals("live", StringComparison.OrdinalIgnoreCase))
            return alive || aliveAfter; // the number is the subject: "7 can live", "7 alive"
        if (words[i].Equals("of", StringComparison.OrdinalIgnoreCase) && i + 1 < words.Length && words[i + 1].Equals("them", StringComparison.OrdinalIgnoreCase)
            || NamesTheUnit(words, i, unitName))
            return alive || aliveAfter || AtOnce.IsMatch(rest);
        // A preposition: the number stood alone ("at most 6 alive on the field"); only an "alive" right after it counts,
        // so "up to 3 at once while alive" is not a limit.
        if (Prepositions.Contains(words[i])) return alive;
        // Another noun: a count of the unit only when that noun is then said to live ("7 Hounds can live").
        int j = i + 1;
        while (j < words.Length && Linking.Contains(words[j])) j++;
        return j < words.Length && AliveWords.IsMatch(words[j]);
    }

    /// <summary>
    /// Whether <paramref name="words"/> from <paramref name="i"/> name <paramref name="unitName"/>: its words up to its head
    /// noun (the word before "of", else the last), the head singular or plural ("Hounds" for "Hound of Shadow", "Edur
    /// Warriors", "War-dogs"). Another word of the name alone is not the unit ("up to 3 shadow bolts" on the Hound).
    /// </summary>
    private static bool NamesTheUnit(string[] words, int i, string? unitName)
    {
        if (string.IsNullOrEmpty(unitName)) return false;
        string[] name = Word.Matches(unitName).Select(w => w.Value).ToArray();
        int of = Array.FindIndex(name, w => w.Equals("of", StringComparison.OrdinalIgnoreCase));
        int head = of > 0 ? of - 1 : name.Length - 1;
        if (head < 0 || i + head >= words.Length) return false;
        for (int k = 0; k < head; k++)
            if (!words[i + k].Equals(name[k], StringComparison.OrdinalIgnoreCase)) return false;
        string w = words[i + head], h = name[head];
        return w.Equals(h, StringComparison.OrdinalIgnoreCase) || w.Equals(h + "s", StringComparison.OrdinalIgnoreCase)
            || w.Equals(h + "es", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The text of <paramref name="text"/> from <paramref name="at"/> to the end of its clause (the sentence's end, or a comma).</summary>
    private static string ClauseAfter(string text, int at)
    {
        int start = SentenceStart(text, at);
        string rest = SentenceAt(text, at)[(at - start)..];
        int comma = rest.IndexOf(',');
        if (comma >= 0) rest = rest[..comma];
        return rest.TrimEnd('.', ' ');
    }

    /// <summary>
    /// <paramref name="u"/>'s description (or <paramref name="description"/> in its place: a test's or a page's wording on a
    /// loaded unit) against its <c>maxAlive</c>, both ways: a stated limit must be the field's, and a field must be stated.
    /// </summary>
    public static List<string> Problems(UnitDef u, string? description = null)
    {
        string text = description ?? u.Description;
        var problems = new List<string>();
        int want = u.MaxAlive; // 0: no limit
        bool said = false;
        foreach ((int n, string token) in Stated(text, u.DisplayName))
        {
            if (want > 0 && n == want) said = true;
            else problems.Add($"{u.Key} description {Field}: '{token}' reads as the unit limit, data {(want > 0 ? want.ToString(CultureInfo.InvariantCulture) : "none")}");
        }
        if (want > 0 && !said)
            problems.Add($"{u.Key} description {Field}: data {want}, which the text does not state ('no more than <n> can live at once', 'at most <n> alive') ('{text}')");
        return problems;
    }

    /// <summary>The sentence of <paramref name="text"/> holding offset <paramref name="at"/> (split at ". ", "; " and ": ", as <see cref="UnitHitBonusText"/> splits).</summary>
    private static string SentenceAt(string text, int at)
    {
        int start = SentenceStart(text, at);
        int end = text.Length;
        foreach (string sep in new[] { ". ", "; ", ": " })
        {
            int e = text.IndexOf(sep, at, StringComparison.Ordinal);
            if (e >= 0) end = Math.Min(end, e);
        }
        return text[start..end];
    }

    /// <summary>Where the sentence of <paramref name="text"/> holding offset <paramref name="at"/> starts (see <see cref="SentenceAt"/>).</summary>
    private static int SentenceStart(string text, int at)
    {
        int start = 0;
        foreach (string sep in new[] { ". ", "; ", ": " })
        {
            int s = text.LastIndexOf(sep, Math.Max(0, at - 1), StringComparison.Ordinal);
            if (s >= 0 && s + sep.Length <= at) start = Math.Max(start, s + sep.Length);
        }
        return start;
    }
}
