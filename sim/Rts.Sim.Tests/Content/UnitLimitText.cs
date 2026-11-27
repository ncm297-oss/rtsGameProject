using System.Globalization;
using System.Text.RegularExpressions;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D39: a unit's limit (M8-4, <c>units.json</c> <c>maxAlive</c>, docs/03 "Implementation (M8-4)") in its description, read
/// both ways as D35's <see cref="UnitHitBonusText"/> reads the hit bonuses. A number after limit words ("at most", "no more
/// than", "never more than", "up to", "a maximum of", "limited to", "capped at") in a sentence about being alive ("alive",
/// "live", "living", "at once") is the unit limit: it must be the unit's <c>maxAlive</c>, and a unit with no
/// <c>maxAlive</c> must state none. A unit with a <c>maxAlive</c> must state it. A number followed by a percent, metres or
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

    /// <summary>The words that make a limit sentence about how many live at once.</summary>
    private static readonly Regex AliveWords = new(@"\b(?:alive|live|living)\b|\bat once\b", Words);

    /// <summary>Every unit of <paramref name="units"/> through <see cref="Problems(UnitDef, string?)"/>.</summary>
    public static List<string> Problems(IEnumerable<UnitDef> units) => units.SelectMany(u => Problems(u)).ToList();

    /// <summary>The limits <paramref name="text"/> states: each number read as a unit limit, with the words that state it.</summary>
    public static List<(int Limit, string Token)> Stated(string text)
    {
        var stated = new List<(int, string)>();
        foreach (Match m in Limit.Matches(text))
            if (AliveWords.IsMatch(SentenceAt(text, m.Index)))
                stated.Add((int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture), m.Value.Trim()));
        return stated;
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
        foreach ((int n, string token) in Stated(text))
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
        int start = 0;
        foreach (string sep in new[] { ". ", "; ", ": " })
        {
            int s = text.LastIndexOf(sep, Math.Max(0, at - 1), StringComparison.Ordinal);
            if (s >= 0 && s + sep.Length <= at) start = Math.Max(start, s + sep.Length);
        }
        int end = text.Length;
        foreach (string sep in new[] { ". ", "; ", ": " })
        {
            int e = text.IndexOf(sep, at, StringComparison.Ordinal);
            if (e >= 0) end = Math.Min(end, e);
        }
        return text[start..end];
    }
}
