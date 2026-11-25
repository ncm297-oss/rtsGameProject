using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D35: the page side of M8-3's two hit bonuses. (1) The first shot from stealth: a unit note's "The first shot from
/// stealth deals +N% damage" against <c>units.json</c> <c>attack.fromStealthBonus</c>, both ways (<see cref="FirstShotProblems"/>;
/// the pack bonus is <see cref="PassiveContentTests"/>' <c>nearbyOwn</c> sentence). (2) A page written ahead of its data
/// carries the <c>units.json</c> rows its roster will hold in its "Schema gaps" table (shadow.md's G2 and G3, docs/03 "For
/// the data track (M8-3)"); <see cref="RowProblems"/> holds those rows to the page's own unit notes, both ways, so the
/// roster's landing is checked against the page twice: the rows are what the files carry, the notes what the pins compare.
/// </summary>
public static class HitBonusPagePin
{
    private const RegexOptions Opts = RegexOptions.CultureInvariant;

    /// <summary>"The first shot from stealth deals +50% damage" (or "Its first shot from hiding deals 50% ...").</summary>
    private static readonly Regex FirstShot = new(@"\b(?:[Tt]he|[Ii]ts) first shot from (?:stealth|hiding) deals \+?(?<n>\d+(?:\.\d+)?) ?%", Opts);

    /// <summary>
    /// "**Hound of Shadow (unique, Age II):**": a note's unit, any parenthesis dropped (PassiveContentTests' reading); the
    /// bold is optional here, since whether the passive pin reads a bullet is BUG-1450's readiness check, not this one's.
    /// </summary>
    private static readonly Regex BulletUnit = new(@"^(?:\*\*)?(?<unit>[^*:(]+?)\s*(?:\([^)]*\))?:(?:\*\*)?", Opts);

    private static readonly Regex CodeSpan = new(@"`(?<c>[^`]+)`", Opts);

    private static readonly Regex UnitId = new(@"^[a-z][a-z0-9]*_[a-z0-9_]+$", Opts);

    /// <summary>One <c>units.json</c> row a page carries: the gap it closes, the unit, and the JSON (<c>"attack"</c> or <c>"passives"</c>).</summary>
    public sealed record PageRow(string Gap, string Unit, string Key, JsonNode Value);

    /// <summary>The "**Unit:**" bullets under "### Unit notes", continuation lines joined, bold removed from the text.</summary>
    public static List<(string Unit, string Text)> Notes(string[] lines)
    {
        var notes = new List<(string, string)>();
        int h = Array.IndexOf(lines, "### Unit notes");
        if (h < 0) return notes;
        var bullets = new List<string>();
        for (int i = h + 1; i < lines.Length && !lines[i].StartsWith('#'); i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("- ", StringComparison.Ordinal)) bullets.Add(line[2..]);
            else if (line.Length > 0 && bullets.Count > 0 && lines[i].StartsWith(' ')) bullets[^1] += " " + line;
        }
        foreach (string b in bullets)
        {
            Match m = BulletUnit.Match(b);
            if (m.Success) notes.Add((m.Groups["unit"].Value.Trim(), b[m.Length..].Replace("**", "").Trim()));
        }
        return notes;
    }

    /// <summary>The first-shot percentage <paramref name="note"/> states, or null.</summary>
    public static double? FirstShotPercent(string note)
    {
        Match m = FirstShot.Match(note);
        return m.Success ? double.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>
    /// <paramref name="faction"/>'s units' <c>attack.fromStealthBonus</c> against the unit notes in <paramref name="lines"/>,
    /// both ways: a unit with the field needs a note stating it, a note stating one needs the field. <paramref name="stealthOf"/>
    /// stands in for <c>Attack.FromStealthBonus</c> (in-memory mutants). Problems read
    /// <c>&lt;faction&gt;.md "Unit notes" '&lt;Unit&gt;' attack.fromStealthBonus: ...</c>.
    /// </summary>
    public static List<string> FirstShotProblems(GameData data, string faction, string[] lines, Func<UnitDef, float>? stealthOf = null)
    {
        Func<UnitDef, float> of = stealthOf ?? (u => u.Attack.FromStealthBonus);
        var problems = new List<string>();
        string where = $"{faction}.md \"Unit notes\"";
        int f = data.FindFaction(faction);
        UnitDef[] units = data.Units.Where(u => u.Faction == f).ToArray();
        List<(string Unit, string Text)> notes = Notes(lines);
        foreach (UnitDef u in units)
        {
            if (of(u) <= 0) continue;
            double want = Math.Round(of(u) * 100.0, 4);
            double? page = notes.Where(n => n.Unit == u.DisplayName).Select(n => FirstShotPercent(n.Text)).FirstOrDefault(p => p != null);
            if (page == null) problems.Add($"{where} '{u.DisplayName}' {UnitHitBonusText.StealthField}: data +{FactionPage.Text(want)}%, its note states no 'The first shot from stealth deals +<n>% damage'");
            else if (Math.Abs(page.Value - want) > 1e-3) problems.Add($"{where} '{u.DisplayName}' {UnitHitBonusText.StealthField}: page +{FactionPage.Text(page.Value)}%, data +{FactionPage.Text(want)}%");
        }
        foreach ((string unit, string text) in notes)
        {
            if (FirstShotPercent(text) is not double page) continue;
            UnitDef? u = units.FirstOrDefault(x => x.DisplayName == unit);
            if (u == null) problems.Add($"{where} '{unit}' {UnitHitBonusText.StealthField}: page +{FactionPage.Text(page)}%, no {faction} unit is named '{unit}'");
            else if (of(u) <= 0) problems.Add($"{where} '{unit}' {UnitHitBonusText.StealthField}: page +{FactionPage.Text(page)}%, data none");
        }
        return problems;
    }

    /// <summary>
    /// The <c>units.json</c> rows in the tables of <paramref name="lines"/>' "## Schema gaps" section: a row whose cells hold a
    /// code span <c>"attack": { ... }</c> or <c>"passives": [ ... ]</c>, with the gap (its first cell) and the unit (the code
    /// span that is an id). A span that does not parse is a problem in <paramref name="problems"/>.
    /// </summary>
    public static List<PageRow> Rows(string[] lines, string page, List<string> problems)
    {
        var rows = new List<PageRow>();
        int h = PageTables.HeadingStartingWith(lines, "## Schema gaps");
        if (h < 0) return rows;
        foreach (List<string> table in PageTables.TablesInSection(lines, h))
            foreach (string line in table.Skip(2))
            {
                string[] spans = CodeSpan.Matches(line).Select(m => m.Groups["c"].Value).ToArray();
                string? json = spans.FirstOrDefault(s => s.StartsWith("\"attack\":", StringComparison.Ordinal) || s.StartsWith("\"passives\":", StringComparison.Ordinal));
                if (json == null) continue;
                string gap = PageTables.Cells(line)[0];
                string? unit = spans.FirstOrDefault(s => UnitId.IsMatch(s));
                JsonObject? o = null;
                try { o = JsonNode.Parse("{" + json + "}") as JsonObject; }
                catch (System.Text.Json.JsonException) { }
                if (o == null || o.Count != 1) { problems.Add($"{page} Schema gaps {gap}: '{json}' is not one JSON property"); continue; }
                if (unit == null) { problems.Add($"{page} Schema gaps {gap}: no `unit_id` code span says which unit carries '{json}'"); continue; }
                (string key, JsonNode? value) = o.First();
                rows.Add(new PageRow(gap, unit, key, value!.DeepClone()));
            }
        return rows;
    }

    /// <summary>A row's <c>nearbyOwn</c> passive object, or null.</summary>
    public static JsonObject? NearbyOf(PageRow r) =>
        r.Key == "passives" && r.Value is JsonArray a ? a.OfType<JsonObject>().FirstOrDefault(p => (string?)p["trigger"] == "nearbyOwn") : null;

    /// <summary>A row's <c>fromStealthBonus</c>, or null.</summary>
    public static double? StealthOf(PageRow r) =>
        r.Key == "attack" && r.Value is JsonObject o && o["fromStealthBonus"] is JsonValue v && v.TryGetValue(out double d) ? d : null;

    /// <summary>
    /// The page's <see cref="Rows"/> against its own unit notes: each <c>fromStealthBonus</c> row is its unit's "first shot"
    /// sentence, each <c>nearbyOwn</c> row its unit's passive sentence (per-mate, cap, radius, counted types by the Units
    /// table's names). With <paramref name="ahead"/> (a page whose faction has no data yet) the other way too: a note stating
    /// either bonus needs its row. Problems read <c>&lt;page&gt; Schema gaps &lt;gap&gt; `&lt;unit&gt;` &lt;field&gt;: ...</c>.
    /// </summary>
    public static List<string> RowProblems(string page, string[] lines, bool ahead)
    {
        var problems = new List<string>();
        List<PageRow> rows = Rows(lines, page, problems);
        int uh = Array.IndexOf(lines, "## Units");
        if (uh < 0) return problems;
        (string[] header, List<string[]> table) = PageTables.NamedTable(lines, "Units", page);
        int idCol = Array.IndexOf(header, "Id"), nameCol = Array.IndexOf(header, "Unit");
        if (idCol < 0 || nameCol < 0) return problems;
        var nameOf = table.Where(r => r.Length == header.Length).ToDictionary(r => r[idCol], r => r[nameCol], StringComparer.Ordinal);
        List<(string Unit, string Text)> notes = Notes(lines);

        foreach (PageRow r in rows)
        {
            string where = $"{page} Schema gaps {r.Gap} `{r.Unit}`";
            if (!nameOf.TryGetValue(r.Unit, out string? name)) { problems.Add($"{where}: not an id of the Units table"); continue; }
            if (r.Key == "attack")
            {
                if (StealthOf(r) is not double fs) { problems.Add($"{where} fromStealthBonus: the row's \"attack\" has no fromStealthBonus number"); continue; }
                double want = Math.Round(fs * 100.0, 4);
                double? said = notes.Where(n => n.Unit == name).Select(n => FirstShotPercent(n.Text)).FirstOrDefault(p => p != null);
                if (said == null) problems.Add($"{where} fromStealthBonus: row {FactionPage.Text(fs)}, the '{name}' note states no 'The first shot from stealth deals +<n>% damage'");
                else if (Math.Abs(said.Value - want) > 1e-3) problems.Add($"{where} fromStealthBonus: row {FactionPage.Text(fs)} (+{FactionPage.Text(want)}%), the '{name}' note +{FactionPage.Text(said.Value)}%");
            }
            else
            {
                if (NearbyOf(r) is not JsonObject p) { problems.Add($"{where} passives: the row has no nearbyOwn passive"); continue; }
                PassiveContentTests.NearbyReading? said = notes.Where(n => n.Unit == name).Select(n => NearbyIn(n.Text)).FirstOrDefault(x => x != null);
                if (said == null) { problems.Add($"{where} nearbyOwn: the '{name}' note has no 'Passive **Name**: +<n>% damage for each other <Unit> within <n> m' sentence"); continue; }
                double bonus = Num(p["damageBonus"]), stacks = Num(p["maxStacks"]), radius = Num(p["radius"]);
                double perMate = Math.Round(bonus * 100.0, 4), cap = Math.Round(stacks * bonus * 100.0, 4);
                if (Math.Abs(said.PerMate - perMate) > 1e-3) problems.Add($"{where} damageBonus: row {FactionPage.Text(bonus)} (+{FactionPage.Text(perMate)}%), the '{name}' note +{FactionPage.Text(said.PerMate)}%");
                if (said.Cap is not double c || Math.Abs(c - cap) > 1e-3)
                    problems.Add($"{where} maxStacks: row {FactionPage.Text(stacks)} x +{FactionPage.Text(perMate)}% = +{FactionPage.Text(cap)}%, the '{name}' note {(said.Cap is double cc ? "max +" + FactionPage.Text(cc) + "%" : "states no cap")}");
                if (Math.Abs(said.Radius - radius) > 1e-3) problems.Add($"{where} radius: row {FactionPage.Text(radius)}, the '{name}' note within {FactionPage.Text(said.Radius)} m");
                string[] want = (p["units"] as JsonArray ?? new JsonArray()).Select(x => (string?)x ?? "").Select(id => nameOf.TryGetValue(id, out string? n) ? n : id).OrderBy(x => x, StringComparer.Ordinal).ToArray();
                string[] got = said.Units.OrderBy(x => x, StringComparer.Ordinal).ToArray();
                if (!got.SequenceEqual(want)) problems.Add($"{where} units: row {string.Join(", ", want)}, the '{name}' note '{string.Join(", ", said.Units)}'");
            }
        }

        if (!ahead) return problems;
        string Id(string unitName) => nameOf.FirstOrDefault(kv => kv.Value == unitName).Key ?? unitName;
        foreach ((string unit, string text) in notes)
            if (FirstShotPercent(text) != null && !rows.Any(r => r.Key == "attack" && r.Unit == Id(unit)))
                problems.Add($"{page} Unit notes '{unit}': states a first shot from stealth, but no Schema gaps row carries `{Id(unit)}`'s \"attack\": {{ \"fromStealthBonus\": ... }}");
        foreach ((string unit, string text) in notes)
            if (NearbyIn(text) != null && !rows.Any(r => NearbyOf(r) != null && r.Unit == Id(unit)))
                problems.Add($"{page} Unit notes '{unit}': a nearbyOwn passive sentence, but no Schema gaps row carries `{Id(unit)}`'s \"passives\": [{{ \"trigger\": \"nearbyOwn\", ... }}]");
        return problems;
    }

    /// <summary>"passive (any case) &lt;Name&gt;:" in a note's text (bold removed).</summary>
    private static readonly Regex PassiveHead = new(@"\b[Pp]assive [^:]+:\s*", Opts);

    /// <summary>
    /// The first <c>nearbyOwn</c> passive sentence of a note's text, whatever the case of its "Passive" (whether the passive
    /// pin reads the sentence is <c>PagePinReadinessTests</c>' BUG-1450 check, not this one's).
    /// </summary>
    public static PassiveContentTests.NearbyReading? NearbyIn(string noteText)
    {
        foreach (Match m in PassiveHead.Matches(noteText))
            if (PassiveContentTests.ReadNearby(noteText[(m.Index + m.Length)..]) is { } r) return r;
        return null;
    }

    private static double Num(JsonNode? n) => n is JsonValue v && v.TryGetValue(out double d) ? d : double.NaN;
}
