using System.Globalization;
using System.Text.RegularExpressions;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D35: the description-number reading (D-H2's, <see cref="AbilityContentTests.DescriptionProblems"/>) for a unit's two
/// M8-3 hit bonuses, which an ability's or status's text never carries. A unit with <c>attack.fromStealthBonus</c> must
/// state it as a percentage in a sentence about its first shot or stealth ("Its first shot from hiding deals 50% more
/// damage", "+50%"); a unit with a <c>nearbyOwn</c> passive must state its per-mate percent (in a sentence with "each" /
/// "per" / "for every"), its cap (<c>maxStacks</c> x <c>damageBonus</c>, after "up to" / "max" / "at most") and its radius
/// ("within N m"). Each percentage and each "within N m" is read in the role its words give it and must be that field's
/// number. D-H10 (BUG-1690, ruling docs/01 2026-11-24 ACCEPT (3)(b)): a percentage is read as a hit bonus only when the
/// unit has the matching field or its sentence has first-shot words (a stealth / first-shot sentence) or pack words
/// ("other ... within", "each other"); every other percentage (a Hamstring's "slows by 20%", a Frenzy's attack speed) is left to the
/// passive and status pins. A "within N m" likewise is the pack's reach only in a pack sentence ("other ... within"), or in
/// an "each" / "per" sentence of a unit that has the pack (an on-death "Warriors within 6 m" is not read). Seconds, counts
/// and other distances are not read here (a unit's other numbers have no description rule yet). Problems read
/// <c>&lt;unit id&gt; description &lt;field&gt;: ...</c>.
/// </summary>
public static class UnitHitBonusText
{
    public const string StealthField = "attack.fromStealthBonus";
    public const string PerMateField = "passives[nearbyOwn].damageBonus";
    public const string CapField = "passives[nearbyOwn].maxStacks";
    public const string RadiusField = "passives[nearbyOwn].radius";

    private const RegexOptions Words = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    /// <summary>"50%", "+50 %", "50 per cent": a percentage.</summary>
    private static readonly Regex Percent = new(@"(?<![\w.])\+?(?<n>\d+(?:\.\d+)?)\s?(?:%|per ?cent\b)", Words);

    /// <summary>"within 8 m", "within 8 metres": the pack's reach.</summary>
    private static readonly Regex Within = new(@"\bwithin\s+(?<n>\d+(?:\.\d+)?)[\s-]?(?:m|metres?|meters?)\b", Words);

    /// <summary>The cap's words right before its number ("up to +30%", "(max +30%)", "at most 30%").</summary>
    private static readonly Regex CapBefore = new(@"\b(?:up to|max(?:imum)?|at most|capped at)\s*\(?\s*$", Words);

    private static readonly Regex StealthWords = new(@"\b(?:first|stealth|stealthed|hiding|hidden|hides?)\b", Words);

    private static readonly Regex EachWords = new(@"\b(?:each|per|for every)\b", Words);

    /// <summary>
    /// BUG-1690: the pack's words in one sentence: "other ... within" ("+10% for each other War-dog within 8 m") or "each
    /// other" / "per other" / "for every other" ("Each other Warrior adds +10%"). "Each bite" is not one.
    /// </summary>
    private static readonly Regex PackWords = new(@"\bother\b.*\bwithin\b|\b(?:each|per|for every) other\b", Words);

    private enum Role { Stealth, PerMate, Cap, None }

    /// <summary>Every unit of <paramref name="units"/> through <see cref="Problems(UnitDef, string?)"/>.</summary>
    public static List<string> Problems(IEnumerable<UnitDef> units) => units.SelectMany(u => Problems(u)).ToList();

    /// <summary>
    /// <paramref name="u"/>'s description (or <paramref name="description"/> in its place: a test's wording on a loaded
    /// unit) against its <c>fromStealthBonus</c> and <c>nearbyOwn</c> passive, both ways: a stated number must be its
    /// field's, and each field the unit has must be stated.
    /// </summary>
    public static List<string> Problems(UnitDef u, string? description = null)
    {
        string text = description ?? u.Description;
        var problems = new List<string>();
        double? stealth = u.Attack.FromStealthBonus > 0 ? Pct(u.Attack.FromStealthBonus) : null;
        UnitPassive? pack = u.NearbyPassive >= 0 ? u.Passives[u.NearbyPassive] : null;
        double? perMate = pack is UnitPassive p1 ? Pct(p1.DamageBonus) : null;
        double? cap = pack is UnitPassive p2 ? Pct(p2.MaxStacks * p2.DamageBonus) : null;
        double? radius = pack is UnitPassive p3 ? Math.Round(p3.Radius, 4) : null;
        bool statedStealth = false, statedPerMate = false, statedCap = false, statedRadius = false;

        foreach (Match m in Percent.Matches(text))
        {
            double n = Number(m);
            string sentence = SentenceAt(text, m.Index);
            // BUG-1690: the pack's roles only for a unit with the pack or in a sentence with pack words; the first shot's only
            // in a sentence with first-shot words. Any other percentage is not a hit bonus and is not read here.
            bool packRead = pack != null || PackWords.IsMatch(sentence);
            Role role = packRead && CapBefore.IsMatch(text[..m.Index].TrimEnd('+', ' ')) ? Role.Cap
                : StealthWords.IsMatch(sentence) ? Role.Stealth
                : packRead && EachWords.IsMatch(sentence) ? Role.PerMate
                : Role.None;
            string token = m.Value.Trim();
            switch (role)
            {
                case Role.Stealth:
                    if (Same(stealth, n)) statedStealth = true;
                    else problems.Add($"{u.Key} description {StealthField}: '{token}' reads as the first shot from stealth, data {Show(stealth)}");
                    break;
                case Role.PerMate:
                    if (Same(perMate, n)) statedPerMate = true;
                    else problems.Add($"{u.Key} description {PerMateField}: '{token}' reads as the bonus for each other unit near, data {Show(perMate)}");
                    break;
                case Role.Cap:
                    if (Same(cap, n)) statedCap = true;
                    else problems.Add($"{u.Key} description {CapField}: '{token}' reads as the pack's cap, data {CapText(pack, cap)}");
                    break;
                default:
                    break; // BUG-1690: not a hit bonus; the passive and status pins own it
            }
        }
        foreach (Match m in Within.Matches(text))
        {
            string sentence = SentenceAt(text, m.Index);
            if (!PackWords.IsMatch(sentence) && !(pack != null && EachWords.IsMatch(sentence))) continue; // BUG-1690: not the pack's reach
            double n = Number(m);
            if (radius is double r && Math.Abs(r - n) < 1e-3) statedRadius = true;
            else problems.Add($"{u.Key} description {RadiusField}: '{m.Value.Trim()}' reads as the pack's reach, data {(radius is double rr ? FactionPage.Text(rr) + " m" : "none")}");
        }

        if (stealth != null && !statedStealth)
            problems.Add($"{u.Key} description {StealthField}: data +{FactionPage.Text(stealth.Value)}%, which the text does not state in a first-shot sentence ('{text}')");
        if (perMate != null && !statedPerMate)
            problems.Add($"{u.Key} description {PerMateField}: data +{FactionPage.Text(perMate.Value)}% for each other unit near, which the text does not state ('{text}')");
        if (cap != null && !statedCap)
            problems.Add($"{u.Key} description {CapField}: data cap {CapText(pack, cap)}, which the text does not state ('up to <n>%') ('{text}')");
        if (radius != null && !statedRadius)
            problems.Add($"{u.Key} description {RadiusField}: data {FactionPage.Text(radius.Value)} m, which the text does not state ('within <n> m') ('{text}')");
        return problems;
    }

    /// <summary>The sentence of <paramref name="text"/> holding offset <paramref name="at"/> (split at ". ", "; " and ": ").</summary>
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

    private static string CapText(UnitPassive? pack, double? cap) =>
        pack is UnitPassive p ? $"{p.MaxStacks} x +{FactionPage.Text(Pct(p.DamageBonus))}% = +{FactionPage.Text(cap!.Value)}%" : "none";

    private static string Show(double? pct) => pct is double v ? $"+{FactionPage.Text(v)}%" : "none";

    private static bool Same(double? want, double n) => want is double v && Math.Abs(v - n) < 1e-3;

    private static double Pct(float fraction) => Math.Round(fraction * 100.0, 4);

    private static double Number(Match m) => double.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);
}
