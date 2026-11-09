using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Rts.Sim.Ai;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D26a (D14 c's content): each faction page's "How the AI adapts" sub-section (under "AI build order (Normal)", before the
/// first opening) against the data that drives the three rules it describes, both ways. Data to page: the difficulties'
/// <c>counterStrength</c> (<c>ai.json</c>) and the multipliers they make for a seat that has seen only Shock (1 + strength),
/// <c>rules.json</c> <c>ai.counterMemory</c> / <c>starvedWaveAfter</c> / <c>starvedMinArmy</c> / <c>farExpandReach</c>,
/// <c>ai.counters</c> as this faction's unit names per enemy slot, and the far expansion's drop-off as the sim picks it
/// (<c>FarExpansion.GoldFreeDropOff</c>: name, id, gold / wood cost from <c>buildings.json</c>) with the faction's Town Hall
/// and worker names. Page to data: every number in the sub-section is one of those values (so no match tick, digest or
/// tally can be quoted there unpinned), the "What answers what" list names exactly the six non-worker enemy slots, and the
/// BUG-1156 sentence's claim (no opening's Age I mix lists a unit that answers an enemy Line) holds in the file.
/// <see cref="Problems"/> returns every mismatch as text naming the faction, so a one-sided edit says which side moved.
/// </summary>
public class AiAdaptsContentTests
{
    private static readonly string[] Factions = { "malazan", "whirlwind" };

    /// <summary>The sub-section's heading on each page.</summary>
    public const string Heading = "#### How the AI adapts";

    /// <summary>The enemy slots the "What answers what" bullet lists, in page order (workers never enter the memory).</summary>
    private static readonly UnitSlot[] EnemySlots =
        { UnitSlot.Line, UnitSlot.Ranged, UnitSlot.Shock, UnitSlot.Caster, UnitSlot.Siege, UnitSlot.Unique };

    private const double Tps = SimConstants.TicksPerSecond;

    /// <summary>A number standing alone in prose: not part of an id, a doc name ("docs/02"), a task ("M5-9a") or a bug ("BUG-1156").</summary>
    private static readonly Regex Number = new(@"(?<![\w./\-])\d+(?:\.\d+)?(?![\w\-])", RegexOptions.CultureInvariant);

    private const string N = @"(\d+(?:\.\d+)?)";

    private static string Text(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static double Parse(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    /// <summary>
    /// The sub-section's text: its lines up to the next heading, trimmed and joined with spaces, bold markers and backticks
    /// removed. Null when the heading is missing or is not inside "AI build order (Normal)" before the first opening.
    /// </summary>
    public static string? SectionText(string[] pageLines, out string? where)
    {
        int section = Array.IndexOf(pageLines, "## " + AiContentTests.Section);
        int h = Array.IndexOf(pageLines, Heading);
        where = null;
        if (section < 0 || h < 0) { where = $"no '{Heading}' heading under '## {AiContentTests.Section}'"; return null; }
        for (int i = section + 1; i < h; i++)
            if (pageLines[i].StartsWith("## ", StringComparison.Ordinal) || pageLines[i].StartsWith("### ", StringComparison.Ordinal))
            {
                where = $"'{Heading}' is not in the shared part of '{AiContentTests.Section}' (it comes after '{pageLines[i]}')";
                return null;
            }
        var text = new List<string>();
        for (int i = h + 1; i < pageLines.Length && !pageLines[i].StartsWith('#'); i++)
            if (pageLines[i].Trim().Length > 0) text.Add(pageLines[i].Trim());
        return string.Join(" ", text).Replace("**", "").Replace("`", "");
    }

    /// <summary>This faction's non-worker unit names whose slot answers (<c>ai.counters</c>) the enemy slot <paramref name="enemy"/>.</summary>
    private static SortedSet<string> Answers(GameData d, int faction, UnitSlot enemy)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (UnitDef u in d.Units)
            if (u.Faction == faction && !u.IsWorker && d.AiRules.Counters(u.Slot, enemy)) names.Add(u.DisplayName);
        return names;
    }

    private static UnitDef Worker(GameData d, int faction) => d.Units.First(u => u.Faction == faction && u.IsWorker);

    private static BuildingDef TownHall(GameData d, int faction) => d.Buildings.First(b => b.Faction == faction && b.Slot == BuildingSlot.TownHall);

    /// <summary>"Heavy Infantry" or "Raiders": the page may write a unit's name in the plural.</summary>
    private static bool NamesUnit(string page, string name) => page == name || page == name + "s";

    /// <summary>
    /// Every way the page's "How the AI adapts" disagrees with <paramref name="d"/> for <paramref name="faction"/>; empty when
    /// they match. <paramref name="pageLines"/> is the whole page (tests pass an edited copy).
    /// </summary>
    public static List<string> Problems(GameData d, string faction, string[] pageLines)
    {
        var problems = new List<string>();
        string? text = SectionText(pageLines, out string? where);
        if (text == null) { problems.Add($"{faction}.md: {where}"); return problems; }
        int f = d.FindFaction(faction);
        AiDef ai = d.Ai[f];
        AiRulesDef rules = d.AiRules;
        float easy = ai.Difficulty(AiDifficulty.Easy).CounterStrength;
        float normal = ai.Difficulty(AiDifficulty.Normal).CounterStrength;
        float hard = ai.Difficulty(AiDifficulty.Hard).CounterStrength;
        double memory = rules.CounterMemoryTicks / Tps, waveAfter = rules.StarvedWaveAfterTicks / Tps;
        int dropOff = FarExpansion.GoldFreeDropOff(d, f);
        if (dropOff < 0) { problems.Add($"{faction}: the data has no gold-free drop-off, the page names one"); return problems; }
        BuildingDef depot = d.Buildings[dropOff];
        string hall = TownHall(d, f).DisplayName, worker = Worker(d, f).DisplayName;

        void Expect(string label, string pattern, params string[] want)
        {
            Match m = Regex.Match(text, pattern, RegexOptions.CultureInvariant);
            if (!m.Success) { problems.Add($"{faction} {label}: the page has no '{pattern}'"); return; }
            for (int g = 0; g < want.Length; g++)
            {
                string page = m.Groups[g + 1].Value;
                bool same = double.TryParse(page, NumberStyles.Float, CultureInfo.InvariantCulture, out double p)
                    && double.TryParse(want[g], NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                    ? Math.Abs(p - v) < 1e-6 : page == want[g];
                if (!same) problems.Add($"{faction} {label}: page '{page}' vs data '{want[g]}'");
            }
        }

        // The counter shift: the memory, the three strengths, and what they make of a seat that has seen only Shock.
        Expect("ai.counterMemory", @"ai\.counterMemory " + N + " s", Text(memory));
        Expect("counterStrength", @"counterStrength in ai\.json: Easy " + N + @" \(an Easy AI never adapts\), Normal " + N + ", Hard " + N,
            Text(easy), Text(normal), Text(hard));
        if (easy != 0f) problems.Add($"{faction}: the page says an Easy AI never adapts, the data's Easy counterStrength is {Text(easy)}");
        Match only = Regex.Match(text, @"seen only cavalry weighs its ([\w' ]+?) ×" + N + " on Normal and ×" + N + " on Hard", RegexOptions.CultureInvariant);
        SortedSet<string> shockAnswers = Answers(d, f, UnitSlot.Shock);
        if (!only.Success) problems.Add($"{faction}: no 'seen only cavalry weighs its <unit> ×N on Normal and ×N on Hard' sentence");
        else
        {
            if (shockAnswers.Count != 1 || !NamesUnit(only.Groups[1].Value, shockAnswers.Min!))
                problems.Add($"{faction} the cavalry answer: page '{only.Groups[1].Value}' vs data '{string.Join(", ", shockAnswers)}'");
            if (Math.Abs(Parse(only.Groups[2].Value) - (1.0 + normal)) > 1e-6) problems.Add($"{faction} Normal's multiplier: page ×{only.Groups[2].Value} vs data ×{Text(1.0 + normal)}");
            if (Math.Abs(Parse(only.Groups[3].Value) - (1.0 + hard)) > 1e-6) problems.Add($"{faction} Hard's multiplier: page ×{only.Groups[3].Value} vs data ×{Text(1.0 + hard)}");
        }

        // What answers what: exactly the six non-worker enemy slots, each with this faction's answering units (a set).
        MatchCollection rows = Regex.Matches(text, @"enemy (\w+) → ([^;.]+)", RegexOptions.CultureInvariant);
        string[] slots = rows.Select(r => r.Groups[1].Value).ToArray();
        string[] wantSlots = EnemySlots.Select(s => s.ToString()).ToArray();
        if (!slots.SequenceEqual(wantSlots)) problems.Add($"{faction} What answers what: page slots '{string.Join(", ", slots)}' vs '{string.Join(", ", wantSlots)}'");
        foreach (Match r in rows)
        {
            if (!Enum.TryParse(r.Groups[1].Value, out UnitSlot enemy)) continue; // reported above
            string cell = r.Groups[2].Value.Trim();
            var page = new SortedSet<string>(cell == "none" ? Array.Empty<string>() : cell.Split(',').Select(s => s.Trim()), StringComparer.Ordinal);
            SortedSet<string> data = Answers(d, f, enemy);
            if (!page.SetEquals(data)) problems.Add($"{faction} enemy {enemy}: page '{string.Join(", ", page)}' vs data (ai.counters) '{(data.Count == 0 ? "none" : string.Join(", ", data))}'");
        }

        // The starved charge.
        Expect("ai.starvedMinArmy", @"at least ai\.starvedMinArmy " + N + " combat unit", rules.StarvedMinArmy.ToString(CultureInfo.InvariantCulture));
        Expect("ai.starvedWaveAfter", @"ai\.starvedWaveAfter " + N + " s", Text(waveAfter));

        // The far expansion: the reach, the faction's Town Hall and worker, and the drop-off the sim builds.
        Expect("ai.farExpandReach", @"ai\.farExpandReach " + N + @" m of its ([\w' ]+?), with no enemy drop-off", Text(rules.FarExpandReach), hall);
        Expect("the far drop-off", @"it builds an? ([\w' ]+?) \((\w+), " + N + " gold / " + N + " wood\\)",
            depot.DisplayName, depot.Key, depot.CostGold.ToString(CultureInfo.InvariantCulture), depot.CostWood.ToString(CultureInfo.InvariantCulture));
        Expect("the hall fallback", @"; an? ([\w' ]+?) only if it can pay for one", hall);
        Expect("the leg's worker", @"short of gold for its next unit, no ([\w' ]+?) on gold and no gold mine", worker);

        // BUG-1156's Age I sentence: the names are the answers to an enemy Line, and no Age I mix lists one of them.
        Match ageOne = Regex.Match(text, @"no opening's Age I mix lists an? ([\w' ]+?) or an? ([\w' ]+?)\.", RegexOptions.CultureInvariant);
        SortedSet<string> lineAnswers = Answers(d, f, UnitSlot.Line);
        if (!text.Contains("Where the shift doesn't show (BUG-1156):", StringComparison.Ordinal)) problems.Add($"{faction}: no 'Where the shift doesn't show (BUG-1156):' bullet");
        if (!ageOne.Success) problems.Add($"{faction}: no 'no opening's Age I mix lists a <unit> or a <unit>.' sentence");
        else
        {
            var page = new SortedSet<string>(new[] { ageOne.Groups[1].Value, ageOne.Groups[2].Value }, StringComparer.Ordinal);
            if (!page.SetEquals(lineAnswers)) problems.Add($"{faction} Age I's Line answers: page '{string.Join(", ", page)}' vs data '{string.Join(", ", lineAnswers)}'");
        }
        foreach (AiOpening o in ai.Openings)
            foreach (AiComposition c in o.Compositions)
                if (c.Age == 1)
                    foreach (int u in c.Units)
                        if (d.AiRules.Counters(d.Units[u].Slot, UnitSlot.Line))
                            problems.Add($"{faction} {o.Id}: the Age I mix lists {d.Units[u].DisplayName}, which answers an enemy Line; the page says none does");

        // Page to data: every number in the sub-section is one of the pinned values.
        var allowed = new[]
        {
            memory, easy, normal, hard, 1.0 + normal, 1.0 + hard, rules.StarvedMinArmy, waveAfter, rules.FarExpandReach,
            depot.CostGold, depot.CostWood,
        };
        foreach (Match m in Number.Matches(text))
            if (!allowed.Any(a => Math.Abs(a - Parse(m.Value)) < 1e-6))
                problems.Add($"{faction}: the number {m.Value} (\"…{Around(text, m.Index)}…\") is not a value this pin reads from the data");
        return problems;
    }

    private static string Around(string text, int at)
    {
        int from = Math.Max(0, at - 30), to = Math.Min(text.Length, at + 20);
        return text[from..to];
    }

    private static string[] Page(string faction) => PageTables.FactionLines(faction);

    [Theory]
    [InlineData("malazan")]
    [InlineData("whirlwind")]
    public void HowTheAiAdapts_MatchesTheData_BothWays(string faction)
    {
        List<string> problems = Problems(TestSim.Data, faction, Page(faction));
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>Page side: an edit to any pinned value or name on the page alone is caught, naming what moved.</summary>
    [Theory]
    [InlineData("malazan", "Normal 0.5, Hard 1.0", "Normal 0.6, Hard 1.0", "malazan counterStrength: page '0.6' vs data '0.5'")]
    [InlineData("whirlwind", "Easy\n  0 (an Easy", "Easy\n  0.1 (an Easy", "whirlwind counterStrength: page '0.1' vs data '0'")]
    [InlineData("malazan", "`ai.counterMemory`\n  180 s", "`ai.counterMemory`\n  120 s", "malazan ai.counterMemory: page '120' vs data '180'")]
    [InlineData("whirlwind", "×2 on Hard", "×3 on Hard", "whirlwind Hard's multiplier: page ×3 vs data ×2")]
    [InlineData("malazan", "`ai.starvedWaveAfter` 60 s", "`ai.starvedWaveAfter` 90 s", "malazan ai.starvedWaveAfter: page '90' vs data '60'")]
    [InlineData("whirlwind", "`ai.starvedMinArmy` 1 combat", "`ai.starvedMinArmy` 2 combat", "whirlwind ai.starvedMinArmy: page '2' vs data '1'")]
    [InlineData("malazan", "`ai.farExpandReach` 384 m", "`ai.farExpandReach` 400 m", "malazan ai.farExpandReach: page '400' vs data '384'")]
    [InlineData("malazan", "0 gold / 75 wood", "0 gold / 50 wood", "malazan the far drop-off: page '50' vs data '75'")]
    [InlineData("whirlwind", "a Supply\n  Cache", "a Supply\n  Tent", "whirlwind the far drop-off: page 'Supply Tent' vs data 'Supply Cache'")]
    [InlineData("malazan", "enemy Shock → Heavy Infantry;", "enemy Shock → Heavy Infantry, Crossbowman;", "malazan enemy Shock: page 'Crossbowman, Heavy Infantry' vs data (ai.counters) 'Heavy Infantry'")]
    [InlineData("whirlwind", "enemy Unique → none.", "enemy Unique → Zealot.", "whirlwind enemy Unique: page 'Zealot' vs data (ai.counters) 'none'")]
    [InlineData("malazan", "One at a time.", "One at a time; the first comes at 12:30.", "malazan: the number 12 ")]
    public void APageEdit_IsCaught(string faction, string from, string to, string expected)
    {
        string page = string.Join("\n", Page(faction));
        Assert.Contains(from, page, StringComparison.Ordinal);
        string[] edited = page.Replace(from, to, StringComparison.Ordinal).Split('\n');
        List<string> problems = Problems(TestSim.Data, faction, edited);
        Assert.True(problems.Any(p => p.StartsWith(expected, StringComparison.Ordinal)), $"expected '{expected}', got:\n{string.Join("\n", problems)}");
    }

    /// <summary>The sub-section must sit in the shared part of "AI build order (Normal)", before the first opening's heading.</summary>
    [Fact]
    public void TheSubSection_OutsideTheSharedPart_IsCaught()
    {
        List<string> lines = Page("malazan").ToList();
        int h = lines.IndexOf(Heading);
        int end = h + 1;
        while (!lines[end].StartsWith('#')) end++;
        List<string> block = lines.GetRange(h, end - h);
        lines.RemoveRange(h, end - h);
        lines.InsertRange(lines.IndexOf("## Difficulty"), block);
        List<string> problems = Problems(TestSim.Data, "malazan", lines.ToArray());
        Assert.Single(problems);
        Assert.Contains("is not in the shared part", problems[0], StringComparison.Ordinal);
    }

    /// <summary>Data side: moving a strength, a quoted knob, the counters table or the drop-off's name or cost fails the shipped page.</summary>
    [Theory]
    [InlineData("factions/malazan/ai.json", "difficulties.normal.counterStrength", "0.75", "malazan", "malazan counterStrength: page '0.5' vs data '0.75'")]
    [InlineData("factions/whirlwind/ai.json", "difficulties.hard.counterStrength", "0.8", "whirlwind", "whirlwind counterStrength: page '1.0' vs data '0.8'")]
    [InlineData("factions/malazan/ai.json", "difficulties.easy.counterStrength", "0.2", "malazan", "malazan counterStrength: page '0' vs data '0.2'")]
    [InlineData("common/rules.json", "ai.counterMemory", "120", "whirlwind", "whirlwind ai.counterMemory: page '180' vs data '120'")]
    [InlineData("common/rules.json", "ai.starvedWaveAfter", "90", "malazan", "malazan ai.starvedWaveAfter: page '60' vs data '90'")]
    [InlineData("common/rules.json", "ai.starvedMinArmy", "2", "whirlwind", "whirlwind ai.starvedMinArmy: page '1' vs data '2'")]
    [InlineData("common/rules.json", "ai.farExpandReach", "300", "malazan", "malazan ai.farExpandReach: page '384' vs data '300'")]
    [InlineData("common/rules.json", "ai.counters.shock", """["line", "ranged"]""", "malazan", "malazan enemy Shock: page 'Heavy Infantry' vs data (ai.counters) 'Crossbowman, Heavy Infantry'")]
    public void ADataEdit_FailsTheShippedPage(string file, string dotted, string json, string faction, string expected)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(file, root =>
        {
            string[] parts = dotted.Split('.');
            JsonObject parent = root;
            for (int i = 0; i < parts.Length - 1; i++) parent = parent[parts[i]]!.AsObject();
            parent[parts[^1]] = JsonNode.Parse(json);
        });
        AssertDataEditCaught(dir, faction, expected);
    }

    /// <summary>Data side, <c>buildings.json</c>: the drop-off's display name and its wood cost.</summary>
    [Theory]
    [InlineData("malazan", "malazan_depot", "displayName", "\"Supply Depot\"", "malazan the far drop-off: page 'Quartermaster's Depot' vs data 'Supply Depot'")]
    [InlineData("whirlwind", "whirlwind_supply_cache", "cost", """{ "gold": 0, "wood": 80 }""", "whirlwind the far drop-off: page '75' vs data '80'")]
    public void ADropOffEdit_FailsTheShippedPage(string faction, string id, string field, string json, string expected)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson($"factions/{faction}/buildings.json", root =>
            root["buildings"]!.AsArray().Select(n => n!.AsObject()).Single(b => (string)b["id"]! == id)[field] = JsonNode.Parse(json));
        AssertDataEditCaught(dir, faction, expected);
    }

    private static void AssertDataEditCaught(TestDataDir dir, string faction, string expected)
    {
        DataLoadResult result = DataLoader.LoadAll(dir.Path);
        Assert.True(result.Ok, string.Join("\n", result.Errors));
        List<string> problems = Problems(result.Data!, faction, Page(faction));
        Assert.True(problems.Any(p => p.StartsWith(expected, StringComparison.Ordinal)), $"expected '{expected}', got:\n{string.Join("\n", problems)}");
    }
}
