using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA (D-R38): shadow.md, written ahead of its data, read by the content pins' own parsers rather than only by
/// <see cref="PagePinReadinessTests"/>'s shape check. Where a pin needs a faction's data, the page runs against a shipped
/// faction with the Shadow names substituted (the D-R36 method). Also: every Units / Buildings number against docs/02's
/// template or the page's "Against docs/02's template" table, the opening's totals, and the Owner review texts.
/// </summary>
public class DR38ShadowPageQaTests
{
    private static string[] Page() => PageTables.FactionLines("shadow");

    /// <summary>Shadow names to Malazan names, longest first so "Thrall Camp" goes before "Thrall".</summary>
    private static readonly (string Shadow, string Malazan)[] ToMalazan =
    {
        ("shadow_thrall_camp", "malazan_depot"),
        ("Thrall Camp", "Quartermaster's Depot"),
        ("Edur Longhall", "Garrison Keep"),
        ("Warrior Lodge", "Legion Barracks"),
        ("K'risnan Sorcerer", "Cadre Mage"),
        ("Aptorian Stalker", "Wickan Lancer"),
        ("Hound of Shadow", "Sapper"),
        ("Shadow Archer", "Crossbowman"),
        ("Edur Warrior", "Heavy Infantry"),
        ("Edur Ram", "Catapult"),
        ("Thrall", "Laborer"),
    };

    private static string[] AsMalazan(string[] lines) =>
        lines.Select(l => ToMalazan.Aggregate(l, (s, p) => s.Replace(p.Shadow, p.Malazan, StringComparison.Ordinal))).ToArray();

    /// <summary>
    /// "How the AI adapts" through <see cref="AiAdaptsContentTests.Problems"/> (every number must be one its own sentence
    /// reads, the six enemy slots, the far drop-off) with the Malazan names: no stray number, no sentence the pin can't find.
    /// </summary>
    [Fact]
    public void HowTheAiAdapts_ParsesWithThePinsOwnPatterns_UnderSubstitution()
    {
        List<string> problems = AiAdaptsContentTests.Problems(TestSim.Data, "malazan", AsMalazan(Page()));
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>The substitution is not vacuous: a stray number added to shadow.md's sub-section is caught.</summary>
    [Fact]
    public void HowTheAiAdapts_AStrayNumber_IsCaught()
    {
        string[] lines = Page();
        int i = Array.FindIndex(lines, l => l.Contains("One at a time.", StringComparison.Ordinal));
        Assert.True(i >= 0);
        lines[i] = lines[i].Replace("One at a time.", "One at a time, within 3 minutes.", StringComparison.Ordinal);
        List<string> problems = AiAdaptsContentTests.Problems(TestSim.Data, "malazan", AsMalazan(lines));
        Assert.Contains(problems, p => p.StartsWith("malazan: the number 3 ", StringComparison.Ordinal));
    }

    /// <summary>The Difficulty table through the pin (<see cref="AiContentTests.DifficultyProblems"/>): the shared rows, cell for cell.</summary>
    [Fact]
    public void Difficulty_ParsesWithThePin_AndIsTheSharedRows()
    {
        foreach (string faction in new[] { "malazan", "teblor" })
        {
            List<string> problems = AiContentTests.DifficultyProblems(TestSim.Data, faction, Page());
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }
    }

    /// <summary>The bullets of "### Unit notes" that announce a passive, by the words alone ("passive **Name**:", any case).</summary>
    private static readonly Regex AnyPassive = new(@"\b[Pp]assive \*\*(?<name>[^*]+)\*\*:", RegexOptions.CultureInvariant);

    /// <summary>
    /// BUG-1450 item 1: every passive the page announces in its unit notes is one <see cref="PassiveContentTests.PagePassives"/>
    /// reads. shadow.md writes "passive **Shadowed**:" and "passive **Pack Hunter**:" in lower case; the pin's sentence is
    /// "Passive **Name**:" (case-sensitive), so it reads neither and the page-to-data check skips both silently.
    /// </summary>
    [Fact(Skip = "BUG-1450 item 1 (open): shadow.md's unit notes write 'passive **X**:' in lower case; PassiveContentTests reads only 'Passive **X**:'")]
    public void EveryPassiveTheNotesAnnounce_IsReadByThePassivePin()
    {
        string[] lines = Page();
        int h = Array.IndexOf(lines, "### Unit notes");
        string notes = string.Join("\n", lines[h..Array.FindIndex(lines, h + 1, l => l.StartsWith('#'))]);
        string[] announced = AnyPassive.Matches(notes).Select(m => m.Groups["name"].Value).ToArray();
        string[] read = PassiveContentTests.PagePassives(lines, "shadow.md").Select(p => p.Name).ToArray();
        Assert.Equal(new[] { "Shadowed", "Pack Hunter" }, announced);
        Assert.Equal(announced, read);
    }

    /// <summary>The fix is one letter each: with "Passive" capitalised the pin reads both passives with their units.</summary>
    [Fact]
    public void WithPassiveCapitalised_ThePinReadsBothSentences()
    {
        string[] lines = Page().Select(l => l.Replace("passive **", "Passive **", StringComparison.Ordinal)).ToArray();
        var read = PassiveContentTests.PagePassives(lines, "shadow.md").Select(p => (p.Unit, p.Name)).ToArray();
        Assert.Equal(new[] { ("Shadow Archer", "Shadowed"), ("Hound of Shadow", "Pack Hunter") }, read);
    }

    /// <summary>
    /// BUG-1450 item 2: the Summon Wraiths row through <see cref="AbilityContentTests.Compare"/>, on a teblor copy that carries a
    /// spawn-only summon of that name on the Shaman (the loader's own shape for it: no range, no radius, 1.0 s cast, 50 s
    /// cooldown). Its number columns must pass. The page writes Radius "—"; the pin wants "0 m" (it allows "—" for Range only).
    /// </summary>
    [Fact(Skip = "BUG-1450 item 2 (open): shadow.md writes the spawn-only summon's Radius as '—'; AbilityContentTests reads 'page '—', data 0 m'")]
    public void SummonWraithsRow_PassesTheAbilityPin_OnASummonOfTheLoadersShape()
    {
        AbilityContentTests.Result r = CompareSummonRow();
        string[] numberColumns = r.Problems.Where(p => p.Contains("'Summon Wraiths' ", StringComparison.Ordinal)
            && Regex.IsMatch(p, @"'Summon Wraiths' (Kind|Range|Radius|Cast|Cooldown|Duration):", RegexOptions.CultureInvariant)).ToArray();
        Assert.True(numberColumns.Length == 0, string.Join("\n", numberColumns));
    }

    /// <summary>The probe above is live: the same copy reports the Radius cell (and only from the number columns, that one).</summary>
    [Fact]
    public void SummonWraithsRow_TheAbilityPinReadsItsNumberColumns()
    {
        AbilityContentTests.Result r = CompareSummonRow();
        string[] numberColumns = r.Problems.Where(p =>
            Regex.IsMatch(p, @"'Summon Wraiths' (Kind|Range|Radius|Cast|Cooldown|Duration):", RegexOptions.CultureInvariant)).ToArray();
        // Today exactly the Radius cell; when BUG-1450 item 2 is fixed (page or pin) this becomes empty: update both tests.
        Assert.True(numberColumns.All(p => p.Contains("'Summon Wraiths' Radius:", StringComparison.Ordinal)), string.Join("\n", numberColumns));
    }

    private static AbilityContentTests.Result CompareSummonRow()
    {
        GameData data = AiScenes.DataWith(dir =>
        {
            dir.EditJson("factions/teblor/abilities.json", root => root["abilities"]!.AsArray().Add(new JsonObject
            {
                ["id"] = "summon_wraiths", ["displayName"] = "Summon Wraiths", ["description"] = "Summons 3 beside the caster for 20 seconds.",
                ["kind"] = "summon", ["castTime"] = 1.0, ["cooldown"] = 50,
                ["effects"] = new JsonArray(new JsonObject { ["kind"] = "spawn", ["unit"] = "teblor_war_dog", ["count"] = 3, ["lifetime"] = 20 }),
            }));
            dir.EditJson("factions/teblor/units.json", root =>
            {
                JsonObject shaman = root["units"]!.AsArray().Single(u => (string)u!["id"]! == "teblor_shaman")!.AsObject();
                shaman["abilities"]!.AsArray().Add("summon_wraiths");
            });
        });
        string shadowRow = Page().Single(l => l.StartsWith("| Summon Wraiths |", StringComparison.Ordinal)).Replace("K'risnan Sorcerer", "Shaman", StringComparison.Ordinal);
        List<string> lines = PageTables.FactionLines("teblor").ToList();
        int frenzy = lines.FindIndex(l => l.StartsWith("| Blood-oil Frenzy |", StringComparison.Ordinal));
        Assert.True(frenzy >= 0);
        lines.Insert(frenzy + 1, shadowRow);
        return AbilityContentTests.Compare(data, data.Abilities, "teblor", lines.ToArray());
    }

    // ---------- numbers against docs/02 ----------

    private static readonly string[] Stats = { "HP", "Armor", "Attack", "Type", "CD", "Range", "Speed", "Sight", "Cost", "Pop", "Train" };

    /// <summary>
    /// Every number of the Units table equals docs/02's "Template baseline stats" row for its slot, or the stat is named in a
    /// row of the page's "Against docs/02's template" table for that unit (and no stat is named there that equals the
    /// template). The class is docs/02's default armor class for the slot.
    /// </summary>
    [Fact]
    public void UnitsTable_EqualsTheTemplate_ExceptTheListedDifferences()
    {
        string[] lines = Page();
        (string[] uh, List<string[]> units) = PageTables.NamedTable(lines, "Units", "shadow.md");
        string[][] template = FactionPage.DocTable("docs/02-game-design.md", "Template baseline stats", 3);
        string[] th = PageTables.Cells("| Slot | HP | Armor | Attack | Type | Cooldown | Range | Speed | Sight | Cost (G/W) | Pop | Train |");
        int h = Array.IndexOf(lines, "### Against docs/02's template");
        Assert.True(h >= 0);
        List<string[]> diffs = PageTables.FirstTableRows(lines, h, stripBackticks: true);
        var defaultClass = new Dictionary<string, string> { ["Worker"] = "Light", ["Line"] = "Heavy", ["Ranged"] = "Light", ["Shock"] = "Mounted", ["Caster"] = "Light", ["Siege"] = "Heavy" };
        var problems = new List<string>();
        foreach (string[] u in units)
        {
            string slot = u[Array.IndexOf(uh, "Slot")], name = u[Array.IndexOf(uh, "Unit")];
            if (slot == "Unique") continue;
            string[] t = template.Single(r => r[0] == (slot == "Siege" ? "Heavy / siege" : slot));
            string PageStat(string stat) => u[Array.IndexOf(uh, stat == "Cost" ? "Cost (G/W)" : stat)];
            string Tmpl(string stat) => t[Array.IndexOf(th, stat switch { "CD" => "Cooldown", "Cost" => "Cost (G/W)", _ => stat })];
            static bool Same(string a, string b) =>
                double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out double x) && double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
                    ? Math.Abs(x - y) < 1e-9 : a == b;
            var differs = Stats.Where(s => !Same(PageStat(s), Tmpl(s))).ToHashSet();
            var listed = diffs.Where(d => d[0] == name).SelectMany(d => d[1].Split(" / ").Select(s => s.Trim())).ToHashSet();
            foreach (string s in differs.Except(listed)) problems.Add($"{name} {s}: page {PageStat(s)} vs template {Tmpl(s)}, not listed");
            foreach (string s in listed.Except(differs)) problems.Add($"{name} {s}: listed as a difference but equals the template ({PageStat(s)})");
            string cls = u[Array.IndexOf(uh, "Class")];
            if (cls != defaultClass[slot]) problems.Add($"{name} Class: {cls} vs docs/02's {defaultClass[slot]}");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>The Buildings table is docs/02's "Buildings" table, row for row (HP, armor, cost, build time, footprint), the Watch Tower's text too.</summary>
    [Fact]
    public void BuildingsTable_IsTheTemplate()
    {
        (string[] bh, List<string[]> rows) = PageTables.NamedTable(Page(), "Buildings", "shadow.md");
        string[][] template = FactionPage.DocTable("docs/02-game-design.md", "Buildings");
        Assert.Equal(10, rows.Count);
        var problems = new List<string>();
        foreach (string[] r in rows)
        {
            string[] t = template.Single(x => x[0] == r[0]);
            foreach ((int p, int q) in new[] { (3, 1), (4, 2), (5, 3), (6, 4) })
                if (r[p] != t[q]) problems.Add($"{r[1]} {bh[p]}: page {r[p]} vs docs/02 {t[q]}");
            if (r[7] != t[5].Replace(" cells", "")) problems.Add($"{r[1]} Footprint: page {r[7]} vs docs/02 {t[5]}");
        }
        Assert.Contains(rows, r => r[0] == "Watch Tower" && r[9] == template.Single(x => x[0] == "Watch Tower")[6]);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>
    /// The opening's totals: Age I weights 65 and Age II 100 (and the composition bullet's percentages equal the Age II
    /// column); the wave's gold + wood is 18 pop of the Age I mix at the Units table's cost per pop, rounded down; twelve
    /// Hearth-houses take the cap past 100; workers 5 + 15 in Age I and + 8 in Age II.
    /// </summary>
    [Fact]
    public void Opening_TotalsAgreeWithTheUnitsTable()
    {
        string[] lines = Page();
        int oh = Array.IndexOf(lines, "### Opening 1: `standard` (Forest ambush)");
        Assert.True(oh >= 0);
        List<List<string>> tables = PageTables.TablesInSection(lines[oh..Array.IndexOf(lines, "## Difficulty")], 0);
        Assert.Equal(2, tables.Count);
        string[][] steps = tables[0].Skip(2).Select(r => PageTables.Cells(r, stripBackticks: true)).ToArray();
        string[][] mix = tables[1].Skip(2).Select(r => PageTables.Cells(r, stripBackticks: true)).ToArray();
        double W(string cell) => cell == "—" ? 0 : double.Parse(cell, CultureInfo.InvariantCulture);
        Assert.Equal(65, mix.Sum(r => W(r[1])));
        Assert.Equal(100, mix.Sum(r => W(r[2])));

        (string[] uh, List<string[]> units) = PageTables.NamedTable(lines, "Units", "shadow.md");
        double PerPop(string id)
        {
            string[] u = units.Single(r => r[Array.IndexOf(uh, "Id")] == id);
            (int g, int w) = FactionPage.Cost(u[Array.IndexOf(uh, "Cost (G/W)")]);
            return (g + w) / double.Parse(u[Array.IndexOf(uh, "Pop")], CultureInfo.InvariantCulture);
        }
        double weighted = mix.Where(r => W(r[1]) > 0).Sum(r => W(r[1]) * PerPop(r[0])) / 65.0;
        int waveCost = (int)Math.Floor(18 * weighted);
        string wave = string.Join(" ", lines.Where(l => l.Contains("Attack waves:", StringComparison.Ordinal) || l.Contains("down: Edur Warrior", StringComparison.Ordinal)));
        Assert.Contains($"first at 18 army pop and {waveCost.ToString("N0", CultureInfo.InvariantCulture)} gold + wood", wave);

        int houses = steps.Where(s => s[2] == "build" && s[3] == "shadow_hearth_house").Sum(s => int.Parse(s[4], CultureInfo.InvariantCulture));
        Assert.Equal(12, houses);
        Assert.True(10 + houses * 8 >= 100);
        int[] thralls = steps.Where(s => s[2] == "train" && s[3] == "shadow_thrall").Select(s => int.Parse(s[4], CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(new[] { 15, 8 }, thralls);
    }

    // ---------- Owner review ----------

    /// <summary>
    /// The Owner review table: a row for every unit (the Wraith too), building, tech, ability and the opening of the page,
    /// each description at most 144 characters and ending in a full stop, and no release name of the codename table.
    /// </summary>
    [Fact]
    public void OwnerReview_CoversEveryRow_WithinTheLimit_WithoutAReleaseName()
    {
        string[] lines = Page();
        int h = Array.IndexOf(lines, "## Owner review (D-R38, proposed)");
        Assert.True(h >= 0);
        List<string[]> review = PageTables.FirstTableRows(lines, h, stripBackticks: true);
        string[] ids = review.Select(r => r[1]).ToArray();
        var want = new List<string> { "shadow", "shadow_wraith", "standard", "shadow_step", "summon_wraiths" };
        want.AddRange(FactionPage.Table("shadow", "Units").Select(r => r[2]));
        want.AddRange(FactionPage.Table("shadow", "Buildings").Select(r => r[2]));
        want.AddRange(FactionPage.Table("shadow", "Techs").Select(r => r[0]));
        foreach (string id in want) Assert.Contains(id, ids);

        List<string[]> codenames = PageTables.FirstTableRows(lines, Array.IndexOf(lines, "## Codename → release name"), stripBackticks: true);
        string[] release = codenames.SelectMany(r => r[1].Split(" / ")).Select(s => s.Trim())
            .Where(s => !s.StartsWith('(') && !s.StartsWith("Keep", StringComparison.Ordinal)).ToArray();
        Assert.Contains("Shade Hound", release);
        var problems = new List<string>();
        foreach (string[] r in review)
        {
            string text = r[2] + " " + r[3];
            if (r[3].Length > 144) problems.Add($"{r[1]}: {r[3].Length} characters");
            if (!r[3].EndsWith('.')) problems.Add($"{r[1]}: no full stop");
            foreach (string name in release)
                if (text.Contains(name, StringComparison.OrdinalIgnoreCase)) problems.Add($"{r[1]}: says the release name '{name}'");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }
}
