using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D39: the page side of M8-4's unit limit (<c>units.json</c> <c>maxAlive</c>, docs/03 "Implementation (M8-4)"), in the
/// shape of D35's <see cref="HitBonusPagePin"/>. (1) A unit note's "At most N &lt;units&gt; alive" against the unit's
/// <c>maxAlive</c>, both ways (<see cref="LimitProblems"/>). (2) A page written ahead of its data carries the row its
/// roster will hold in its "Schema gaps" table (shadow.md's G4: <c>`"maxAlive": 7`</c> beside <c>`shadow_hound`</c>,
/// docs/03 "For the data track (M8-4)"); <see cref="RowProblems"/> holds that row to the page's own unit note and to the
/// unit's "Owner review" description (read by <see cref="UnitLimitText"/>), both ways for a page ahead of its data.
/// </summary>
public static class UnitLimitPagePin
{
    private const RegexOptions Words = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    /// <summary>"At most 7 Hounds alive", "no more than 2 living at once": a note's limit (the number is not a percent).</summary>
    private static readonly Regex NoteLimit = new(@"\b(?:at most|no more than)\s+(?<n>\d+)\b(?!\.\d|\s*%)[^.;:%]*?\b(?:alive|live|living)\b", Words);

    private static readonly Regex CodeSpan = new(@"`(?<c>[^`]+)`", RegexOptions.CultureInvariant);

    private static readonly Regex UnitId = new(@"^[a-z][a-z0-9]*_[a-z0-9_]+$", RegexOptions.CultureInvariant);

    /// <summary>One <c>maxAlive</c> row a page carries: the gap it closes, the unit, the limit.</summary>
    public sealed record LimitRow(string Gap, string Unit, int MaxAlive);

    /// <summary>The limit <paramref name="noteText"/> states ("At most N ... alive"), or null.</summary>
    public static int? LimitIn(string noteText)
    {
        Match m = NoteLimit.Match(noteText);
        return m.Success ? int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>
    /// <paramref name="faction"/>'s units' <c>maxAlive</c> against the unit notes in <paramref name="lines"/>, both ways: a
    /// unit with a limit needs a note stating it, a note stating one needs the unit to have that limit.
    /// <paramref name="maxAliveOf"/> stands in for <c>UnitDef.MaxAlive</c> (in-memory mutants). Problems read
    /// <c>&lt;faction&gt;.md "Unit notes" '&lt;Unit&gt;' maxAlive: ...</c>.
    /// </summary>
    public static List<string> LimitProblems(GameData data, string faction, string[] lines, Func<UnitDef, int>? maxAliveOf = null)
    {
        Func<UnitDef, int> of = maxAliveOf ?? (u => u.MaxAlive);
        var problems = new List<string>();
        string where = $"{faction}.md \"Unit notes\"";
        int f = data.FindFaction(faction);
        UnitDef[] units = data.Units.Where(u => u.Faction == f).ToArray();
        List<(string Unit, string Text)> notes = HitBonusPagePin.Notes(lines);
        foreach (UnitDef u in units)
        {
            if (of(u) <= 0) continue;
            int? page = notes.Where(n => n.Unit == u.DisplayName).Select(n => LimitIn(n.Text)).FirstOrDefault(p => p != null);
            if (page == null) problems.Add($"{where} '{u.DisplayName}' {UnitLimitText.Field}: data {of(u)}, its note states no 'At most <n> ... alive'");
            else if (page.Value != of(u)) problems.Add($"{where} '{u.DisplayName}' {UnitLimitText.Field}: page {page}, data {of(u)}");
        }
        foreach ((string unit, string text) in notes)
        {
            if (LimitIn(text) is not int page) continue;
            UnitDef? u = units.FirstOrDefault(x => x.DisplayName == unit);
            if (u == null) problems.Add($"{where} '{unit}' {UnitLimitText.Field}: page {page}, no {faction} unit is named '{unit}'");
            else if (of(u) <= 0) problems.Add($"{where} '{unit}' {UnitLimitText.Field}: page {page}, data none");
        }
        return problems;
    }

    /// <summary>
    /// The <c>maxAlive</c> rows in the tables of <paramref name="lines"/>' "## Schema gaps" section: a row whose cells hold a
    /// code span <c>"maxAlive": N</c>, with the gap (its first cell) and the unit (the code span that is an id). A span that
    /// is not one whole-number property, or a row naming no unit, is a problem in <paramref name="problems"/>.
    /// </summary>
    public static List<LimitRow> Rows(string[] lines, string page, List<string> problems)
    {
        var rows = new List<LimitRow>();
        int h = PageTables.HeadingStartingWith(lines, "## Schema gaps");
        if (h < 0) return rows;
        foreach (List<string> table in PageTables.TablesInSection(lines, h))
            foreach (string line in table.Skip(2))
            {
                string[] spans = CodeSpan.Matches(line).Select(m => m.Groups["c"].Value).ToArray();
                string? json = spans.FirstOrDefault(s => s.StartsWith("\"maxAlive\":", StringComparison.Ordinal));
                if (json == null) continue;
                string gap = PageTables.Cells(line)[0];
                string? unit = spans.FirstOrDefault(s => UnitId.IsMatch(s));
                int? limit = null;
                try
                {
                    if (JsonNode.Parse("{" + json + "}") is JsonObject o && o.Count == 1 && o["maxAlive"] is JsonValue v && v.TryGetValue(out int n)) limit = n;
                }
                catch (System.Text.Json.JsonException) { }
                if (limit == null) { problems.Add($"{page} Schema gaps {gap}: '{json}' is not one whole-number maxAlive"); continue; }
                if (unit == null) { problems.Add($"{page} Schema gaps {gap}: no `unit_id` code span says which unit carries '{json}'"); continue; }
                rows.Add(new LimitRow(gap, unit, limit.Value));
            }
        return rows;
    }

    /// <summary>
    /// The page's <see cref="Rows"/> against its own unit notes and its "Owner review" descriptions: each row's limit is its
    /// unit's note limit and the limit its description states. With <paramref name="ahead"/> (a page whose faction has no
    /// data yet) the other way too: a note or a description stating a limit needs its row. Problems read
    /// <c>&lt;page&gt; Schema gaps &lt;gap&gt; `&lt;unit&gt;` maxAlive: ...</c> or <c>&lt;page&gt; Unit notes / Owner review ...</c>.
    /// </summary>
    public static List<string> RowProblems(string page, string[] lines, bool ahead)
    {
        var problems = new List<string>();
        List<LimitRow> rows = Rows(lines, page, problems);
        if (Array.IndexOf(lines, "## Units") < 0) return problems;
        (string[] header, List<string[]> table) = PageTables.NamedTable(lines, "Units", page);
        int idCol = Array.IndexOf(header, "Id"), nameCol = Array.IndexOf(header, "Unit");
        if (idCol < 0 || nameCol < 0) return problems;
        var nameOf = table.Where(r => r.Length == header.Length).ToDictionary(r => r[idCol], r => r[nameCol], StringComparer.Ordinal);
        List<(string Unit, string Text)> notes = HitBonusPagePin.Notes(lines);
        Dictionary<string, string> descriptions = OwnerReviewUnitDescriptions(lines);

        foreach (LimitRow r in rows)
        {
            string where = $"{page} Schema gaps {r.Gap} `{r.Unit}` {UnitLimitText.Field}";
            if (!nameOf.TryGetValue(r.Unit, out string? name)) { problems.Add($"{page} Schema gaps {r.Gap} `{r.Unit}`: not an id of the Units table"); continue; }
            int? said = notes.Where(n => n.Unit == name).Select(n => LimitIn(n.Text)).FirstOrDefault(p => p != null);
            if (said == null) problems.Add($"{where}: row {r.MaxAlive}, the '{name}' note states no 'At most <n> ... alive'");
            else if (said.Value != r.MaxAlive) problems.Add($"{where}: row {r.MaxAlive}, the '{name}' note {said}");
            if (descriptions.TryGetValue(r.Unit, out string? text))
            {
                List<(int Limit, string Token)> stated = UnitLimitText.Stated(text);
                if (stated.Count == 0) problems.Add($"{where}: row {r.MaxAlive}, the Owner review description states no limit ('{text}')");
                foreach ((int n, string token) in stated)
                    if (n != r.MaxAlive) problems.Add($"{where}: row {r.MaxAlive}, the Owner review description '{token}'");
            }
        }

        if (!ahead) return problems;
        string Id(string unitName) => nameOf.FirstOrDefault(kv => kv.Value == unitName).Key ?? unitName;
        foreach ((string unit, string text) in notes)
            if (LimitIn(text) != null && !rows.Any(r => r.Unit == Id(unit)))
                problems.Add($"{page} Unit notes '{unit}': states a limit, but no Schema gaps row carries `{Id(unit)}`'s \"maxAlive\": ...");
        foreach ((string id, string text) in descriptions)
            if (UnitLimitText.Stated(text).Count > 0 && !rows.Any(r => r.Unit == id))
                problems.Add($"{page} Owner review `{id}`: the description states a limit, but no Schema gaps row carries `{id}`'s \"maxAlive\": ...");
        return problems;
    }

    /// <summary>The <c>units.json</c> rows of the page's "## Owner review" table: unit id to its proposed description (empty when the page has none).</summary>
    public static Dictionary<string, string> OwnerReviewUnitDescriptions(string[] lines)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        int h = PageTables.HeadingStartingWith(lines, "## Owner review");
        if (h < 0) return result;
        foreach (string[] r in PageTables.FirstTableRows(lines, h, stripBackticks: true))
            if (r.Length >= 4 && r[0] == "units.json") result[r[1]] = r[3];
        return result;
    }
}
