using System.Globalization;
using System.Text.RegularExpressions;
using Rts.Sim.Data;
using Rts.Sim.Tests.QA.Content;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D-R36: every faction page has the sections and columns the content pins read, in a shape they can parse, for every
/// faction of the data and every page written ahead of its data (<see cref="ContentFactions.PagesAhead"/>; none since D28
/// shipped teblor.md's roster, whose page the edit rows below still use).
/// The pins themselves compare values; this checks only that there is something to compare, so a faction whose files land
/// is pinned the same session, and a page missing a section fails here naming the page, the section and the pin that
/// reads it, not as a bare index or key error deep in a pin.
/// </summary>
public class PagePinReadinessTests
{
    private const RegexOptions Opts = RegexOptions.CultureInvariant;

    private static bool IsInt(string s) => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out _);

    private static bool IsNum(string s) => double.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _);

    private static bool IsCost(string s) => Regex.IsMatch(s, @"^\d+ / \d+$", Opts);

    /// <summary>The header and body rows of the first table after line <paramref name="from"/>, before the next <c>## </c> heading; null when none.</summary>
    private static (string[] Header, List<string[]> Rows)? TableAfter(string[] lines, int from)
    {
        int i = from + 1;
        while (i < lines.Length && !lines[i].StartsWith('|') && !lines[i].StartsWith("## ", StringComparison.Ordinal)) i++;
        if (i >= lines.Length || !lines[i].StartsWith('|')) return null;
        return (PageTables.Cells(lines[i]), PageTables.FirstTableRows(lines, i, stripBackticks: true));
    }

    /// <summary>Bullets ("- ...") from line <paramref name="from"/> until the next heading, continuation lines joined, bold removed.</summary>
    private static List<string> BulletsAfter(string[] lines, int from, int to)
    {
        var bullets = new List<string>();
        for (int i = from + 1; i < to && !lines[i].StartsWith('#'); i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("- ", StringComparison.Ordinal)) bullets.Add(line[2..].Replace("**", ""));
            else if (line.Length > 0 && bullets.Count > 0 && lines[i].StartsWith(' ')) bullets[^1] += " " + line.Replace("**", "");
        }
        return bullets;
    }

    private static string Header(string[] cells) => string.Join(" | ", cells);

    /// <summary>
    /// Every section or column of <paramref name="lines"/> (the page <c>docs/factions/&lt;faction&gt;.md</c>) that a content pin
    /// reads and the page lacks or can't parse, each naming the page, the section and the reading pin; empty when ready.
    /// </summary>
    public static List<string> Problems(string faction, string[] lines)
    {
        var problems = new List<string>();
        string page = faction + ".md";
        int Section(string heading, string reader)
        {
            int i = Array.IndexOf(lines, heading);
            if (i < 0) problems.Add($"{page}: no '{heading}' section (read by {reader})");
            return i;
        }

        // TechContentTests B: the faction upgrade line under "## Faction upgrade: <name>".
        int upgrade = PageTables.HeadingStartingWith(lines, "## Faction upgrade: ");
        if (upgrade < 0) problems.Add($"{page}: no '## Faction upgrade: <name>' section (read by TechContentTests B)");
        else
        {
            string line = FactionPageParagraph(lines, upgrade);
            if (!TechContentTests.UpgradeHead.IsMatch(line))
                problems.Add($"{page} '{lines[upgrade]}': the line does not start '<requires>, at the <building>, <g> G / <w> W, <s> s. ' (TechContentTests B)");
        }

        // UnitContentTests G (and B / C through the typed roster): the Units table, one row per slot in order.
        string[] unitNames = Array.Empty<string>();
        int units = Section("## Units", "UnitContentTests G");
        var unitTable = units >= 0 ? TableAfter(lines, units) : null;
        if (unitTable != null)
        {
            (string[] uh, List<string[]> ur) = unitTable.Value;
            int before = problems.Count;
            foreach (string column in UnitContentTests.UnitColumns)
                if (Array.IndexOf(uh, column) < 0) problems.Add($"{page} Units: no '{column}' column (UnitContentTests G)");
            foreach (string column in uh)
                if (Array.IndexOf(UnitContentTests.UnitColumns, column) < 0 && Array.IndexOf(UnitContentTests.OptionalUnitColumns, column) < 0)
                    problems.Add($"{page} Units: column '{column}' is not one UnitContentTests G reads");
            UnitSlot[] slots = Enum.GetValues<UnitSlot>();
            if (ur.Count != slots.Length) problems.Add($"{page} Units: {ur.Count} rows, expected {slots.Length} (one per slot)");
            int Col(string name) => Array.IndexOf(uh, name);
            if (problems.Count == before)
            {
                unitNames = ur.Select(r => r[Col("Unit")]).ToArray();
                for (int k = 0; k < Math.Min(ur.Count, slots.Length); k++)
                {
                    string[] r = ur[k];
                    string where = $"{page} Units row {r[Col("Unit")]}";
                    if (r.Length != uh.Length) { problems.Add($"{where}: {r.Length} cells, header {uh.Length}"); continue; }
                    if (r[Col("Slot")].Replace(" ", "") != slots[k].ToString()) problems.Add($"{where} Slot: '{r[Col("Slot")]}', expected {slots[k]}");
                    foreach (string c in new[] { "HP", "Armor", "Attack", "Pop", "Train" })
                        if (!IsInt(r[Col(c)])) problems.Add($"{where} {c}: '{r[Col(c)]}' is not a whole number");
                    foreach (string c in new[] { "CD", "Speed", "Sight" })
                        if (!IsNum(r[Col(c)])) problems.Add($"{where} {c}: '{r[Col(c)]}' is not a number");
                    if (!IsCost(r[Col("Cost (G/W)")])) problems.Add($"{where} Cost (G/W): '{r[Col("Cost (G/W)")]}' is not '<gold> / <wood>'");
                    string range = r[Col("Range")];
                    if (range != "melee" && !Regex.IsMatch(range, @"^\d+(?:\.\d+)?(?: \(min \d+(?:\.\d+)?\))?$", Opts))
                        problems.Add($"{where} Range: '{range}' is not 'melee', '<m>' or '<m> (min <m>)'");
                    if (!DataLimits.AttackTargetIds.Contains(r[Col("Targets")]))
                        problems.Add($"{where} Targets: '{r[Col("Targets")]}' is not one of {string.Join(" / ", DataLimits.AttackTargetIds)}");
                    if (Col("Spawn") >= 0 && !IsInt(r[Col("Spawn")])) problems.Add($"{where} Spawn: '{r[Col("Spawn")]}' is not a whole number");
                    if (Col("Scale") >= 0 && !IsNum(r[Col("Scale")])) problems.Add($"{where} Scale: '{r[Col("Scale")]}' is not a number");
                }
            }
        }
        else if (units >= 0) problems.Add($"{page}: no table under '## Units' (UnitContentTests G)");

        // UnitContentTests H / J and PassiveContentTests: the unit notes, at most one per unit of the table.
        int notes = Section("### Unit notes", "UnitContentTests H / J, PassiveContentTests");
        if (notes >= 0)
        {
            List<string> bullets = BulletsAfter(lines, notes, lines.Length);
            if (bullets.Count == 0) problems.Add($"{page} Unit notes: no bullets (UnitContentTests H / J)");
            foreach (string name in unitNames)
            {
                int own = bullets.Count(b => b.StartsWith(name + ":", StringComparison.Ordinal) || b.StartsWith(name + " (", StringComparison.Ordinal));
                if (own > 1) problems.Add($"{page} Unit notes: {own} notes for '{name}' (UnitContentTests H reads one)");
            }
        }

        // AbilityContentTests: the shared header (columns by name).
        int abilities = Section("## Abilities", "AbilityContentTests");
        if (abilities >= 0)
        {
            var t = TableAfter(lines, abilities);
            string want = Header(PageTables.Cells(AbilityContentTests.Header));
            if (t == null) problems.Add($"{page}: no table under '## Abilities' (AbilityContentTests)");
            else if (Header(t.Value.Header) != want) problems.Add($"{page} Abilities header: '{Header(t.Value.Header)}' vs '{want}' (AbilityContentTests)");
            else if (t.Value.Rows.Count == 0) problems.Add($"{page} Abilities: no rows (AbilityContentTests)");
        }

        // BuildingContentTests G / J: ten rows in slot order, the numbers parseable.
        int buildings = Section("## Buildings", "BuildingContentTests G / J");
        if (buildings >= 0)
        {
            var t = TableAfter(lines, buildings);
            string want = Header(BuildingContentTests.BuildingColumns);
            BuildingSlot[] slots = Enum.GetValues<BuildingSlot>().OrderBy(s => DataLimits.BuildingSlotIds.IndexOf(SlotId(s))).ToArray();
            if (t == null) problems.Add($"{page}: no table under '## Buildings' (BuildingContentTests G)");
            // D30: the optional Scale column last (BuildingContentTests L).
            else if (!BuildingContentTests.IsBuildingHeader(t.Value.Header))
                problems.Add($"{page} Buildings header: '{Header(t.Value.Header)}' vs '{want}' (and optionally ' | {BuildingContentTests.ScaleColumn}') (BuildingContentTests G)");
            else
            {
                List<string[]> rows = t.Value.Rows;
                int width = t.Value.Header.Length;
                if (rows.Count != slots.Length) problems.Add($"{page} Buildings: {rows.Count} rows, expected {slots.Length} (one per slot)");
                for (int k = 0; k < Math.Min(rows.Count, slots.Length); k++)
                {
                    string[] r = rows[k];
                    string where = $"{page} Buildings row {r[1]}";
                    if (r.Length != width) { problems.Add($"{where}: {r.Length - width:+0;-0} cells against the header ({r.Length} for {width})"); continue; }
                    if (width > BuildingContentTests.BuildingColumns.Length && !IsNum(r[^1])) problems.Add($"{where} {BuildingContentTests.ScaleColumn}: '{r[^1]}' is not a number");
                    if (r[0].Replace(" ", "") != slots[k].ToString()) problems.Add($"{where} Slot: '{r[0]}', expected {slots[k]} (the file's slot order)");
                    if (!Regex.IsMatch(r[2], @"^[a-z][a-z0-9_]*$", Opts)) problems.Add($"{where} Id: '{r[2]}' is not a snake_case id");
                    foreach (int c in new[] { 3, 4, 6, 8 })
                        if (!IsInt(r[c])) problems.Add($"{where} {BuildingContentTests.BuildingColumns[c]}: '{r[c]}' is not a whole number");
                    if (!IsCost(r[5])) problems.Add($"{where} Cost (G/W): '{r[5]}' is not '<gold> / <wood>'");
                    if (!Regex.IsMatch(r[7], @"^\d+×\d+$", Opts)) problems.Add($"{where} Footprint: '{r[7]}' is not '<w>×<h>'");
                }
            }
        }

        // TechContentTests C: the Techs table.
        int techs = Section("## Techs", "TechContentTests C");
        if (techs >= 0)
        {
            var t = TableAfter(lines, techs);
            string want = Header(TechContentTests.TechColumns);
            if (t == null) problems.Add($"{page}: no table under '## Techs' (TechContentTests C)");
            else if (Header(t.Value.Header) != want) problems.Add($"{page} Techs header: '{Header(t.Value.Header)}' vs '{want}' (TechContentTests C)");
            else if (t.Value.Rows.Count == 0) problems.Add($"{page} Techs: no rows (TechContentTests C)");
            else
                foreach (string[] r in t.Value.Rows)
                {
                    if (r.Length != TechContentTests.TechColumns.Length) { problems.Add($"{page} Techs row {r[0]}: {r.Length} cells"); continue; }
                    if (!IsCost(r[3])) problems.Add($"{page} Techs row {r[0]} Cost (G/W): '{r[3]}' is not '<gold> / <wood>'");
                    if (!IsNum(r[4])) problems.Add($"{page} Techs row {r[0]} Time (s): '{r[4]}' is not a number");
                }
        }

        AiProblems(page, lines, Section, problems);

        // AiContentTests J: the Difficulty table, Easy / Normal / Hard.
        int difficulty = Section("## " + AiContentTests.DifficultySection, "AiContentTests J");
        if (difficulty >= 0)
        {
            var t = TableAfter(lines, difficulty);
            string want = Header(AiContentTests.DifficultyColumns);
            if (t == null) problems.Add($"{page}: no table under '## {AiContentTests.DifficultySection}' (AiContentTests J)");
            else if (Header(t.Value.Header) != want) problems.Add($"{page} Difficulty header: '{Header(t.Value.Header)}' vs '{want}' (AiContentTests J)");
            else if (!t.Value.Rows.Select(r => r[0]).SequenceEqual(AiContentTests.DifficultyRows))
                problems.Add($"{page} Difficulty rows: '{string.Join(", ", t.Value.Rows.Select(r => r[0]))}', expected '{string.Join(", ", AiContentTests.DifficultyRows)}' (AiContentTests J)");
        }
        return problems;
    }

    /// <summary>AiContentTests A / F and AiAdaptsContentTests: the AI section's shared part and every opening it lists.</summary>
    private static void AiProblems(string page, string[] lines, Func<string, string, int> section, List<string> problems)
    {
        int h = section("## " + AiContentTests.Section, "AiContentTests A, AiAdaptsContentTests");
        if (h < 0) return;
        int end = h + 1;
        while (end < lines.Length && !lines[end].StartsWith("## ", StringComparison.Ordinal)) end++;
        int firstOpening = Array.FindIndex(lines, h, end - h, l => l.StartsWith("### ", StringComparison.Ordinal));
        int sharedEnd = firstOpening < 0 ? end : firstOpening;
        string[] shared = lines[h..sharedEnd];

        List<List<string>> tables = PageTables.TablesInSection(shared, 0);
        string want = Header(AiContentTests.OpeningColumns);
        if (tables.Count != 1) { problems.Add($"{page} AI build order: {tables.Count} tables before the first opening, expected 1, the openings (AiContentTests A)"); return; }
        if (Header(PageTables.Cells(tables[0][0])) != want)
        {
            problems.Add($"{page} openings header: '{Header(PageTables.Cells(tables[0][0]))}' vs '{want}' (AiContentTests A)");
            return;
        }
        string[][] openings = tables[0].Skip(2).Select(r => PageTables.Cells(r, stripBackticks: true)).ToArray();
        if (openings.Length == 0) problems.Add($"{page} openings: no rows (AiContentTests A)");

        List<string> sharedBullets = BulletsAfter(shared, 0, shared.Length);
        string? retreat = sharedBullets.FirstOrDefault(b => b.StartsWith("Retreat:", StringComparison.Ordinal));
        if (retreat == null || !Regex.IsMatch(retreat, AiContentTests.RetreatPattern, Opts))
            problems.Add($"{page} AI build order: no 'Retreat: ... lost N% falls back; a unit under N% hp' bullet (AiContentTests A)");
        DefenseAndHighProblems(page, sharedBullets, openings, problems);
        if (AiAdaptsContentTests.SectionText(lines, out string? where) == null)
            problems.Add($"{page}: {where} (AiAdaptsContentTests)");

        for (int k = 0; k < openings.Length; k++)
        {
            string[] o = openings[k];
            if (o.Length != AiContentTests.OpeningColumns.Length) { problems.Add($"{page} opening row {k + 1}: {o.Length} cells"); continue; }
            string heading = $"### Opening {k + 1}: `{o[1]}` ({o[2]})";
            int oh = Array.IndexOf(lines, heading, h, end - h);
            if (oh < 0) { problems.Add($"{page}: no '{heading}' heading for opening row {k + 1} (AiContentTests A / F)"); continue; }
            int oend = oh + 1;
            while (oend < end && !lines[oend].StartsWith("### ", StringComparison.Ordinal)) oend++;
            string[] body = lines[oh..oend];
            List<List<string>> ot = PageTables.TablesInSection(body, 0);
            string who = $"{page} opening {k + 1} `{o[1]}`";
            if (ot.Count != 2) { problems.Add($"{who}: {ot.Count} tables, expected 2, steps and composition (AiContentTests A)"); continue; }
            if (Header(PageTables.Cells(ot[0][0])) != Header(AiContentTests.StepColumns))
                problems.Add($"{who} steps header: '{Header(PageTables.Cells(ot[0][0]))}' vs '{Header(AiContentTests.StepColumns)}'");
            if (Header(PageTables.Cells(ot[1][0])) != Header(AiContentTests.CompositionColumns))
                problems.Add($"{who} composition header: '{Header(PageTables.Cells(ot[1][0]))}' vs '{Header(AiContentTests.CompositionColumns)}'");
            foreach (string[] step in ot[0].Skip(2).Select(r => PageTables.Cells(r, stripBackticks: true)))
            {
                if (step.Length != AiContentTests.StepColumns.Length) { problems.Add($"{who} step {step[0]}: {step.Length} cells"); continue; }
                if (!Regex.IsMatch(step[1], @"^(?:time|pop|workers) \d+(?:\.\d+)?$", Opts)) problems.Add($"{who} step {step[0]} At: '{step[1]}' is not 'time|pop|workers <n>'");
                if (!DataLimits.AiStepActionIds.Contains(step[2])) problems.Add($"{who} step {step[0]} Do: '{step[2]}' is not one of {string.Join(" / ", DataLimits.AiStepActionIds)}");
                if (!IsInt(step[4])) problems.Add($"{who} step {step[0]} Count: '{step[4]}' is not a whole number");
            }
            List<string> bullets = BulletsAfter(body, 0, body.Length);
            if (!bullets.Any(b => b.StartsWith("Composition target (by pop):", StringComparison.Ordinal)))
                problems.Add($"{who}: no 'Composition target (by pop):' bullet (AiContentTests A)");
            string? attack = bullets.FirstOrDefault(b => b.StartsWith("Attack waves:", StringComparison.Ordinal));
            if (attack == null || !Regex.IsMatch(attack, AiContentTests.AttackPattern, Opts))
                problems.Add($"{who}: no 'Attack waves: first at N army pop and N gold + wood' bullet (AiContentTests A)");
        }
    }

    /// <summary>
    /// D-R37 (BUG-1186): the AI section's "Defense" bullet in the shape <see cref="D20PinQaTests"/> reads (the one-unit
    /// sentence, the three sized-defense knobs quoted with a value) and its "High starting resources" bullet in the shape
    /// <see cref="D22PresetTextQaTests"/> reads (<c>aiSpendAbove</c>, "Standard has no threshold", "not pinned", one of the
    /// page's own openings by id). The values are the pins' to compare once the faction's data ships.
    /// </summary>
    private static void DefenseAndHighProblems(string page, List<string> bullets, string[][] openings, List<string> problems)
    {
        string? defense = bullets.FirstOrDefault(b => b.StartsWith("Defense ", StringComparison.Ordinal));
        if (defense == null) problems.Add($"{page} AI build order: no 'Defense' bullet (D20PinQaTests, D20DefenseWordingQaTests)");
        else
        {
            if (!D20PinQaTests.OneRaiderCase.IsMatch(defense))
                problems.Add($"{page} Defense: no 'One X (N gold + wood) is answered with N worth, e.g. two Y from home (N), and a wave out keeps going.' sentence (D20PinQaTests)");
            var quoted = new HashSet<string>(D20PinQaTests.QuotedKnob.Matches(defense).Select(m => m.Groups[1].Value), StringComparer.Ordinal);
            foreach (string knob in D20PinQaTests.SizedDefenseKnobs)
                if (!quoted.Contains(knob)) problems.Add($"{page} Defense: does not quote `ai.{knob}` with its value (D20PinQaTests)");
        }

        string? high = bullets.FirstOrDefault(b => b.StartsWith("High starting resources ", StringComparison.Ordinal));
        if (high == null) problems.Add($"{page} AI build order: no 'High starting resources' bullet (D22PresetTextQaTests)");
        else
        {
            foreach (string phrase in new[] { "`aiSpendAbove`", "Standard has no threshold", "not pinned" })
                if (!high.Contains(phrase, StringComparison.Ordinal)) problems.Add($"{page} High starting resources: no '{phrase}' (D22PresetTextQaTests)");
            if (!openings.Any(o => o.Length > 1 && high.Contains($"`{o[1]}`", StringComparison.Ordinal)))
                problems.Add($"{page} High starting resources: names none of the page's openings by `id` (D22PresetTextQaTests)");
        }
    }

    /// <summary>The paragraph under line <paramref name="heading"/>, its lines joined with spaces, bold removed (as <see cref="FactionPage.Paragraph"/>).</summary>
    private static string FactionPageParagraph(string[] lines, int heading)
    {
        int i = heading + 1;
        while (i < lines.Length && lines[i].Trim().Length == 0) i++;
        var text = new List<string>();
        for (; i < lines.Length && lines[i].Trim().Length > 0; i++) text.Add(lines[i].Trim());
        return string.Join(" ", text).Replace("**", "");
    }

    /// <summary>"TownHall" -> "town_hall", the file's slot id.</summary>
    private static string SlotId(BuildingSlot s) => Regex.Replace(s.ToString(), "(?<=[a-z])(?=[A-Z])", "_").ToLowerInvariant();

    [Theory]
    [MemberData(nameof(ContentFactions.PageTheory), MemberType = typeof(ContentFactions))]
    public void EveryPage_HasTheSectionsThePinsRead(string faction)
    {
        List<string> problems = Problems(faction, PageTables.FactionLines(faction));
        Assert.True(problems.Count == 0, $"docs/factions/{faction}.md is not pin-ready:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void ThePagesChecked_AreTheDatasFactions_ThenThePagesAhead()
    {
        string[] keys = TestSim.Data.Factions.Select(f => f.Key).ToArray();
        Assert.Equal(keys, ContentFactions.Keys);
        Assert.Contains("malazan", keys);
        Assert.Contains("whirlwind", keys);
        Assert.Equal(keys.Concat(ContentFactions.PagesAhead.Where(p => !keys.Contains(p))), ContentFactions.Pages);
        foreach (string page in ContentFactions.Pages)
            Assert.True(File.Exists(Path.Combine(TestDataDir.RepoRoot(), PageTables.FactionPath(page))), $"no docs/factions/{page}.md");
    }

    /// <summary>The readiness check is not vacuous: one edit of teblor.md fails naming the page, the section and the pin.</summary>
    [Theory]
    [InlineData("## Techs", "## Tech", "teblor.md: no '## Techs' section (read by TechContentTests C)")]
    [InlineData("#### How the AI adapts", "#### How the AI learns", "teblor.md: no '#### How the AI adapts' heading under '## AI build order (Normal)' (AiAdaptsContentTests)")]
    [InlineData("### Opening 1: `standard` (War band)", "### Opening 1: `standard` (Warband)", "teblor.md: no '### Opening 1: `standard` (War band)' heading for opening row 1")]
    [InlineData("| Unique | War-dog | `teblor_war_dog` | 70 |", "| Unique | War-dog | `teblor_war_dog` | 70 each |", "teblor.md Units row War-dog HP: '70 each' is not a whole number")]
    [InlineData("| 120 / 0 | 1 | 24 | 3 |", "| 120 / 0 per pack | 1 | 24 | 3 |", "teblor.md Units row War-dog Cost (G/W): '120 / 0 per pack' is not '<gold> / <wood>'")]
    [InlineData("| Slot | Unit | Id | HP |", "| Slot | Unit | Key | HP |", "teblor.md Units: column 'Key' is not one UnitContentTests G reads")]
    [InlineData("| Hard | 0.5 |", "| Insane | 0.5 |", "teblor.md Difficulty rows: 'Easy, Normal, Insane'")]
    [InlineData("first at 8 army pop and 600 gold + wood", "first at 8 army pop", "teblor.md opening 1 `standard`: no 'Attack waves:")]
    [InlineData("a unit under 40% hp", "a unit below 40% hp", "teblor.md AI build order: no 'Retreat:")]
    [InlineData("| Siege Works | Breaker's Hall |", "| Siege Works | Breaker's Hall | `x` |", "teblor.md Buildings row Breaker's Hall: +1 cells against the header")]
    [InlineData("- **Defense** (the shared", "- **Defence** (the shared", "teblor.md AI build order: no 'Defense' bullet")]
    [InlineData("e.g. two Javelin Throwers from home", "e.g. a few Javelin Throwers from home", "teblor.md Defense: no 'One X (N gold + wood)")]
    [InlineData("`ai.recallHallHp` 0.5 of its", "`ai.recallHallHp` of its", "teblor.md Defense: does not quote `ai.recallHallHp`")]
    [InlineData("- **High starting resources** (docs/02", "- **High start** (docs/02", "teblor.md AI build order: no 'High starting resources' bullet")]
    [InlineData("Standard has no threshold", "Standard has none", "teblor.md High starting resources: no 'Standard has no threshold'")]
    [InlineData("(not pinned)", "(not yet)", "teblor.md High starting resources: no 'not pinned'")]
    [InlineData("`standard` at High", "the opening at High", "teblor.md High starting resources: names none of the page's openings")]
    [InlineData("Age II, at the Bloodwood Carver, 200 G / 150 W, 45 s.", "Age II at the Bloodwood Carver, 200 G / 150 W, 45 s.", "teblor.md '## Faction upgrade: Blood-oil Anointing': the line does not start")]
    public void AnEditedTeblorPage_FailsNamingTheSection(string find, string replace, string expected)
    {
        string[] lines = PageTables.FactionLines("teblor");
        int i = Array.FindIndex(lines, l => l.Contains(find, StringComparison.Ordinal));
        Assert.True(i >= 0, $"teblor.md has no line containing '{find}'");
        string[] edited = (string[])lines.Clone();
        edited[i] = lines[i].Replace(find, replace, StringComparison.Ordinal);
        List<string> problems = Problems("teblor", edited);
        Assert.True(problems.Any(p => p.StartsWith(expected, StringComparison.Ordinal)), $"expected '{expected}', got:\n{string.Join("\n", problems)}");
    }

    /// <summary>A pin's typed per-faction table fails naming the table and the page when the data ships a faction it lacks.</summary>
    [Fact]
    public void ATypedTableWithoutTheFaction_FailsNamingTheTableAndPage()
    {
        var table = new Dictionary<string, int> { ["malazan"] = 3 };
        var e = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => ContentFactions.Typed(table, "teblor", "AiContentTests.OpeningCounts"));
        Assert.Contains("AiContentTests.OpeningCounts has no 'teblor' row", e.Message, StringComparison.Ordinal);
        Assert.Contains("docs/factions/teblor.md", e.Message, StringComparison.Ordinal);
        Assert.Equal(3, ContentFactions.Typed(table, "malazan", "AiContentTests.OpeningCounts"));
    }
}
