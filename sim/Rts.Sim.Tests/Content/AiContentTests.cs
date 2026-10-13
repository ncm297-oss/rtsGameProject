using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// Content (D14): each shipped faction's <c>ai.json</c> is its page's "AI build order (Normal)" section, both ways: the
/// step table row for row (trigger, action, id, count), the composition table weight for weight, the "Composition target"
/// percentages (Age II), the wave threshold (pop and cost) and the retreat fractions. Every id belongs to the faction and
/// can be acted on: a unit is trained at a building the order places, a tech at a building slot the faction fills, and the
/// Age I composition only names units the Age I steps can train. <see cref="Problems"/> returns every mismatch as text
/// naming the faction and the row, so a one-sided edit says which side moved.
/// </summary>
public class AiContentTests
{
    private static readonly string[] Factions = { "malazan", "whirlwind" };

    /// <summary>The heading of each page's AI section.</summary>
    public const string Section = "AI build order (Normal)";

    private static GameData Data => TestSim.Data;

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>A step as the page's table writes it: "pop 6 | build | malazan_billet | 1".</summary>
    public static string StepText(AiStep s)
    {
        string at = s.Trigger switch
        {
            AiStepTrigger.Time => "time " + Num((double)s.At / SimConstants.TicksPerSecond),
            AiStepTrigger.Pop => "pop " + Num(s.At / 2.0),
            _ => "workers " + s.At.ToString(CultureInfo.InvariantCulture),
        };
        string id = s.Action == AiStepAction.Expand ? "—" : s.TargetKey;
        return $"{at} | {DataLimits.AiStepActionIds[(int)s.Action]} | {id} | {s.Count.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>The section's lines, from its heading to the next <c>## </c> heading; fails naming the page if missing.</summary>
    private static string[] SectionLines(string faction, string[] pageLines)
    {
        int h = PageTables.Heading(pageLines, "## " + Section, faction + ".md");
        int end = h + 1;
        while (end < pageLines.Length && !pageLines[end].StartsWith("## ", StringComparison.Ordinal)) end++;
        return pageLines[h..end];
    }

    /// <summary>The section's bullets ("- **Attack waves:** ..."), continuation lines joined, bold markers removed.</summary>
    private static List<string> Bullets(string[] section)
    {
        var bullets = new List<string>();
        foreach (string raw in section)
        {
            string line = raw.Trim();
            if (line.StartsWith("- ", StringComparison.Ordinal)) bullets.Add(line[2..]);
            else if (line.Length > 0 && bullets.Count > 0 && raw.StartsWith(' ')) bullets[^1] += " " + line;
        }
        return bullets.Select(b => b.Replace("**", "")).ToList();
    }

    private static string? Bullet(List<string> bullets, string label) =>
        bullets.FirstOrDefault(b => b.StartsWith(label + ":", StringComparison.Ordinal));

    /// <summary>
    /// Every way the page section disagrees with <paramref name="d"/>'s AI data for <paramref name="faction"/>; empty when
    /// they match. <paramref name="pageLines"/> is the whole page (tests pass an edited copy).
    /// </summary>
    public static List<string> Problems(GameData d, string faction, string[] pageLines)
    {
        var problems = new List<string>();
        int f = d.FindFaction(faction);
        AiDef ai = d.Ai[f];
        string[] section = SectionLines(faction, pageLines);
        List<List<string>> tables = PageTables.TablesInSection(section, 0);
        if (tables.Count != 2)
        {
            problems.Add($"{faction}: the section has {tables.Count} tables, expected 2 (steps, composition)");
            return problems;
        }

        // Steps, row for row.
        string[][] rows = tables[0].Skip(2).Select(r => PageTables.Cells(r, stripBackticks: true)).ToArray();
        if (rows.Length != ai.BuildOrder.Length)
            problems.Add($"{faction} steps: page {rows.Length} vs data {ai.BuildOrder.Length}");
        for (int i = 0; i < Math.Max(rows.Length, ai.BuildOrder.Length); i++)
        {
            string page = i < rows.Length ? string.Join(" | ", rows[i].Skip(1).Take(4)) : "(none)";
            string data = i < ai.BuildOrder.Length ? StepText(ai.BuildOrder[i]) : "(none)";
            if (i < rows.Length && rows[i][0] != (i + 1).ToString(CultureInfo.InvariantCulture))
                problems.Add($"{faction} step {i + 1}: the page numbers it '{rows[i][0]}'");
            if (page != data) problems.Add($"{faction} step {i + 1}: page '{page}' vs data '{data}'");
        }

        // Composition table: unit | Age I | Age II, "—" for none.
        string[][] comp = tables[1].Skip(2).Select(r => PageTables.Cells(r, stripBackticks: true)).ToArray();
        for (int age = 1; age <= ai.Compositions.Length; age++)
        {
            AiComposition c = ai.Compositions[age - 1];
            string data = string.Join(", ", c.Units.Select((u, k) => $"{d.Units[u].Key} {Num(c.Weights[k])}").OrderBy(x => x, StringComparer.Ordinal));
            string page = string.Join(", ", comp.Where(r => r.Length > age && r[age] != "—").Select(r => $"{r[0]} {r[age]}").OrderBy(x => x, StringComparer.Ordinal));
            if (page != data) problems.Add($"{faction} composition age {age}: page '{page}' vs data '{data}'");
        }

        List<string> bullets = Bullets(section);

        // "Composition target (by pop): 35% Heavy Infantry, ..." is the Age II mix by display name.
        string? target = Bullet(bullets, "Composition target (by pop)");
        if (target == null) problems.Add($"{faction}: no 'Composition target (by pop)' line");
        else
        {
            AiComposition last = ai.Compositions[^1];
            string data = string.Join(", ", last.Units.Select((u, k) => $"{Num(last.Weights[k])}% {d.Units[u].DisplayName}").OrderBy(x => x, StringComparer.Ordinal));
            string page = string.Join(", ", Regex.Matches(target, @"(\d+(?:\.\d+)?)% ([^,]+)")
                .Select(m => $"{m.Groups[1].Value}% {m.Groups[2].Value.Trim().TrimEnd('.')}").OrderBy(x => x, StringComparer.Ordinal));
            if (page != data) problems.Add($"{faction} composition target: page '{page}' vs data '{data}'");
        }

        string? attack = Bullet(bullets, "Attack waves");
        Match a = attack == null ? Match.Empty : Regex.Match(attack, @"first at (\d+(?:\.\d+)?) army pop and ([\d,]+) gold \+ wood");
        if (!a.Success) problems.Add($"{faction}: no 'Attack waves: first at N army pop and N gold + wood' line");
        else
        {
            string page = $"{a.Groups[1].Value} pop, {a.Groups[2].Value.Replace(",", "")} cost";
            string data = $"{Num(ai.AttackMinHalfPop / 2.0)} pop, {ai.AttackMinCost.ToString(CultureInfo.InvariantCulture)} cost";
            if (page != data) problems.Add($"{faction} attack: page '{page}' vs data '{data}'");
        }

        string? retreat = Bullet(bullets, "Retreat");
        Match r = retreat == null ? Match.Empty : Regex.Match(retreat, @"lost (\d+)% falls back; a unit under (\d+)% hp");
        if (!r.Success) problems.Add($"{faction}: no 'Retreat: ... lost N% falls back; a unit under N% hp' line");
        else
        {
            string page = $"loss {r.Groups[1].Value}%, hp {r.Groups[2].Value}%";
            string data = $"loss {Num(Math.Round(ai.RetreatLossFraction * 100.0, 2))}%, hp {Num(Math.Round(ai.RetreatHpFraction * 100.0, 2))}%";
            if (page != data) problems.Add($"{faction} retreat: page '{page}' vs data '{data}'");
        }
        return problems;
    }

    [Fact]
    public void A_EachPage_IsItsAiFile_BothWays()
    {
        foreach (string faction in Factions)
        {
            List<string> problems = Problems(Data, faction, PageTables.FactionLines(faction));
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }
    }

    /// <summary>
    /// The pin is not vacuous: a one-line page edit (a count, a trigger, an id, a weight, the wave, the retreat) and a
    /// missing or extra row each fail, naming the faction and the row.
    /// </summary>
    [Theory]
    [InlineData("| 1 | time 0 | train | `malazan_laborer` | 15 |", "| 1 | time 0 | train | `malazan_laborer` | 14 |", "malazan step 1: page 'time 0 | train | malazan_laborer | 14'")]
    [InlineData("| 2 | pop 6 | build |", "| 2 | pop 7 | build |", "malazan step 2: page 'pop 7")]
    [InlineData("| 3 | pop 8 | build | `malazan_depot`", "| 3 | pop 8 | build | `malazan_barracks`", "malazan step 3:")]
    [InlineData("| 26 | time 570 | build | `malazan_watchtower` | 1 | Watchtower |", "", "malazan steps: page 25 vs data 26")]
    [InlineData("| `malazan_sapper` | — | 10 |", "| `malazan_sapper` | 10 | 10 |", "malazan composition age 1:")]
    [InlineData("| `malazan_catapult` | — | 5 |", "| `malazan_catapult` | — | 6 |", "malazan composition age 2:")]
    [InlineData("10% Cadre Mage, 10% Sapper, 5% Catapult.", "10% Cadre Mage, 10% Sapper, 6% Catapult.", "malazan composition target:")]
    [InlineData("first at 20 army pop and 1,544 gold", "first at 20 army pop and 1,545 gold", "malazan attack: page '20 pop, 1545 cost'")]
    [InlineData("lost 50% falls back; a unit under 30% hp", "lost 40% falls back; a unit under 30% hp", "malazan retreat:")]
    public void B_AOneLinePageEdit_FailsByName(string find, string replace, string expected)
    {
        string[] lines = PageTables.FactionLines("malazan");
        int i = Array.FindIndex(lines, l => l.Contains(find, StringComparison.Ordinal));
        Assert.True(i >= 0, $"malazan.md has no line containing '{find}'");
        List<string> edited = lines.ToList();
        if (replace.Length == 0) edited.RemoveAt(i);
        else edited[i] = lines[i].Replace(find, replace, StringComparison.Ordinal);
        List<string> problems = Problems(Data, "malazan", edited.ToArray());
        Assert.Contains(problems, p => p.StartsWith(expected, StringComparison.Ordinal));
    }

    /// <summary>The other way: a one-value edit of the file fails against the unchanged page, naming the row.</summary>
    [Fact]
    public void C_AOneValueFileEdit_FailsByName()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/whirlwind/ai.json", root =>
        {
            root["buildOrder"]![3]!["at"] = JsonNode.Parse("""{ "pop": 10 }""");
            root["attack"]!["minArmyPop"] = 15;
        });
        DataLoadResult result = DataLoader.LoadAll(dir.Path);
        Assert.True(result.Ok, string.Join("\n", result.Errors));
        List<string> problems = Problems(result.Data!, "whirlwind", PageTables.FactionLines("whirlwind"));
        Assert.Contains("whirlwind step 4: page 'pop 9 | build | whirlwind_supply_cache | 1' vs data 'pop 10 | build | whirlwind_supply_cache | 1'", problems);
        Assert.Contains(problems, p => p.StartsWith("whirlwind attack: page '14 pop, 861 cost' vs data '15 pop", StringComparison.Ordinal));
        Assert.Equal(2, problems.Count);
    }

    /// <summary>
    /// Every id is the faction's and can be acted on: a train step's unit (and every composition unit) is trained at the
    /// Town Hall or at a building some step places; a build step's required buildings are placed by a step too; a research
    /// step's tech is common or the faction's, at a building slot one of its steps fills; an Age II requirement is met by
    /// the order's own Age II step.
    /// </summary>
    [Fact]
    public void D_EveryId_IsTheFactions_AndReachable()
    {
        foreach (string faction in Factions)
        {
            int f = Data.FindFaction(faction);
            AiDef ai = Data.Ai[f];
            var built = new HashSet<int>(ai.BuildOrder.Where(s => s.Action == AiStepAction.Build).Select(s => s.Target));
            built.Add(Data.Buildings.Select((b, i) => (b, i)).Single(x => x.b.Faction == f && x.b.Slot == BuildingSlot.TownHall).i);
            var researched = new HashSet<int>(ai.BuildOrder.Where(s => s.Action == AiStepAction.Research).Select(s => s.Target));
            var builtSlots = new HashSet<BuildingSlot>(built.Select(b => Data.Buildings[b].Slot));
            for (int i = 0; i < ai.BuildOrder.Length; i++)
            {
                AiStep s = ai.BuildOrder[i];
                string where = $"{faction} step {i + 1} ({s.TargetKey})";
                switch (s.Action)
                {
                    case AiStepAction.Train:
                        Assert.True(Data.Units[s.Target].Faction == f, $"{where}: another faction's unit");
                        Assert.True(built.Contains(Data.Units[s.Target].TrainedAtTypeId), $"{where}: no step places its building");
                        Assert.All(Data.Units[s.Target].RequiresTechs, t => Assert.True(researched.Contains(t), $"{where}: needs an unresearched tech"));
                        break;
                    case AiStepAction.Build:
                        Assert.True(Data.Buildings[s.Target].Faction == f, $"{where}: another faction's building");
                        Assert.All(Data.Buildings[s.Target].RequiresBuildings, b => Assert.True(built.Contains(b), $"{where}: needs an unplaced building"));
                        Assert.All(Data.Buildings[s.Target].RequiresTechs, t => Assert.True(researched.Contains(t), $"{where}: needs an unresearched tech"));
                        break;
                    case AiStepAction.Research:
                        TechDef t = Data.Techs[s.Target];
                        Assert.True(t.Faction < 0 || t.Faction == f, $"{where}: another faction's tech");
                        Assert.True(builtSlots.Contains(t.ResearchedAtSlot), $"{where}: no step places a {t.ResearchedAtSlot}");
                        Assert.All(t.RequiresTechs, x => Assert.True(researched.Contains(x), $"{where}: needs an unresearched tech"));
                        break;
                    default:
                        Assert.Equal(AiStepAction.Expand, s.Action);
                        Assert.Equal(-1, s.Target);
                        break;
                }
            }
            foreach (AiComposition c in ai.Compositions)
                foreach (int u in c.Units)
                {
                    Assert.True(Data.Units[u].Faction == f, $"{faction} age {c.Age}: {Data.Units[u].Key} is another faction's");
                    Assert.True(Data.Units[u].Slot != UnitSlot.Worker, $"{faction} age {c.Age}: {Data.Units[u].Key} is a worker");
                    Assert.True(built.Contains(Data.Units[u].TrainedAtTypeId), $"{faction} age {c.Age}: no step places {Data.Units[u].TrainedAt}");
                }
        }
    }

    /// <summary>
    /// Age I's composition names only units the Age I steps can train: the unit and its building need no age tech, and the
    /// building is placed by a step due before the Age II research (a pop step, or a time step earlier than it). Age II's
    /// composition is Age I's plus the rest of the army, the same weights for the shared units (the pages' one list).
    /// </summary>
    [Fact]
    public void E_AgeI_NamesOnlyWhatAgeICanTrain_AndAgeIIExtendsIt()
    {
        int age2 = Data.FindTech(DataLimits.AgeTechIds[0]);
        foreach (string faction in Factions)
        {
            AiDef ai = Data.Ai[Data.FindFaction(faction)];
            AiStep ageStep = ai.BuildOrder.Single(s => s.Action == AiStepAction.Research && s.Target == age2);
            Assert.Equal(AiStepTrigger.Time, ageStep.Trigger);
            var ageOne = new HashSet<int>(ai.BuildOrder
                .Where(s => s.Action == AiStepAction.Build && (s.Trigger != AiStepTrigger.Time || s.At < ageStep.At))
                .Select(s => s.Target));
            AiComposition one = ai.Compositions[0], two = ai.Compositions[1];
            foreach (int u in one.Units)
            {
                UnitDef unit = Data.Units[u];
                string where = $"{faction} Age I {unit.Key}";
                Assert.False(unit.RequiresTechs.Contains(age2), $"{where}: needs Age II");
                Assert.False(Data.Buildings[unit.TrainedAtTypeId].RequiresTechs.Contains(age2), $"{where}: its building needs Age II");
                Assert.True(ageOne.Contains(unit.TrainedAtTypeId), $"{where}: no Age I step places {unit.TrainedAt}");
            }
            for (int k = 0; k < one.Units.Length; k++)
            {
                int at = two.Units.IndexOf(one.Units[k]);
                Assert.True(at >= 0, $"{faction}: Age II drops {Data.Units[one.Units[k]].Key}");
                Assert.Equal(one.Weights[k], two.Weights[at]);
            }
            // The pages' percentages: the Age II mix sums to 100.
            Assert.Equal(100f, two.Weights.Sum());
        }
    }

    /// <summary>
    /// The wave cost is the cost of the wave's pop at the Age I mix (weights by pop), rounded down; the worker steps take
    /// the starting workers to each age's target in the step's "Why"; Age II's step is at the time its "Why" says; the
    /// expand step is at Normal's <c>expandAfter</c>; the order's halls and houses reach the pop cap.
    /// </summary>
    [Fact]
    public void F_TheThresholdsAndTargets_FollowFromTheData()
    {
        foreach (string faction in Factions)
        {
            int f = Data.FindFaction(faction);
            AiDef ai = Data.Ai[f];
            AiComposition one = ai.Compositions[0];
            double perPop = 0;
            for (int k = 0; k < one.Units.Length; k++)
            {
                UnitDef u = Data.Units[one.Units[k]];
                perPop += one.Weights[k] * (u.CostGold + u.CostWood) / (u.HalfPop / 2.0);
            }
            perPop /= one.Weights.Sum();
            Assert.True((int)Math.Floor(ai.AttackMinHalfPop / 2.0 * perPop) == ai.AttackMinCost,
                $"{faction}: minArmyCost {ai.AttackMinCost} vs {Num(ai.AttackMinHalfPop / 2.0)} pop x {Num(perPop)} per pop");

            string[][] rows = FactionPage.Table(faction, Section);
            int workers = Data.Rules.StartingWorkers;
            for (int i = 0; i < ai.BuildOrder.Length; i++)
            {
                AiStep s = ai.BuildOrder[i];
                string why = rows[i][5];
                if (s.Action == AiStepAction.Train && Data.Units[s.Target].Slot == UnitSlot.Worker)
                {
                    workers += s.Count;
                    Match m = Regex.Match(why, @"^Workers to (\d+) in Age I+\b");
                    Assert.True(m.Success, $"{faction} step {i + 1}: a worker step's 'Why' names no target: '{why}'");
                    Assert.Equal(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), workers);
                }
                if (s.Action == AiStepAction.Research && s.TargetKey == DataLimits.AgeTechIds[0])
                {
                    Match m = Regex.Match(why, @"^Age II at (\d+):(\d\d)");
                    Assert.True(m.Success, $"{faction} step {i + 1}: '{why}'");
                    int seconds = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 60 + int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    Assert.Equal(seconds * SimConstants.TicksPerSecond, s.At);
                }
                if (s.Action == AiStepAction.Expand)
                {
                    Assert.Equal(AiStepTrigger.Time, s.Trigger);
                    Assert.Equal(ai.Difficulty(AiDifficulty.Normal).ExpandAfterTicks, s.At);
                }
            }

            int hall = Data.Buildings.Single(b => b.Faction == f && b.Slot == BuildingSlot.TownHall).HalfPopProvided;
            int houses = ai.BuildOrder.Where(s => s.Action == AiStepAction.Build && Data.Buildings[s.Target].Slot == BuildingSlot.House)
                .Sum(s => Data.Buildings[s.Target].HalfPopProvided);
            Assert.True(hall + houses >= Data.Rules.HalfPopCap, $"{faction}: the order's halls and houses give {Num((hall + houses) / 2.0)} pop, below the cap");
        }
    }

    // ---- D16: the difficulty blocks (rows J-K) ----

    /// <summary>The heading of each page's difficulty table (after the AI build order).</summary>
    public const string DifficultySection = "Difficulty";

    /// <summary>The table's row labels, in <see cref="AiDifficulty"/> order.</summary>
    public static readonly string[] DifficultyRows = { "Easy", "Normal", "Hard" };

    /// <summary>The table's header, one column per <c>ai.json</c> difficulty field (the brief's order).</summary>
    public static readonly string[] DifficultyColumns =
    {
        "Difficulty", "Thinks every (s)", "Build-order slack (s)", "Wave size ×", "Gather bonus", "Focus fire",
        "Pulls back wounded", "Uses abilities", "Expands after (s)",
    };

    private static string YesNo(bool b) => b ? "yes" : "no";

    private static string Seconds(int ticks) => Num((double)ticks / SimConstants.TicksPerSecond);

    /// <summary>One difficulty's cells as the page writes them (seconds from ticks, the gather bonus as a percentage).</summary>
    public static string[] DifficultyCells(AiDifficultyDef k) => new[]
    {
        Seconds(k.ThinkTicks), Seconds(k.BuildOrderSlackTicks), Num(k.WaveSizeMultiplier),
        Num(Math.Round(k.GatherBonus * 100.0, 2)) + "%", YesNo(k.FocusFire), YesNo(k.PullBackWounded), YesNo(k.UseAbilities),
        Seconds(k.ExpandAfterTicks),
    };

    /// <summary>
    /// Every way the page's "Difficulty" table disagrees with <paramref name="d"/>'s difficulty blocks for
    /// <paramref name="faction"/>, each naming the faction, the row and the column; empty when they match.
    /// </summary>
    public static List<string> DifficultyProblems(GameData d, string faction, string[] pageLines)
    {
        var problems = new List<string>();
        AiDef ai = d.Ai[d.FindFaction(faction)];
        (string[] header, List<string[]> rows) = PageTables.NamedTable(pageLines, DifficultySection, faction + ".md");
        if (!header.SequenceEqual(DifficultyColumns))
        {
            problems.Add($"{faction} difficulty header: page '{string.Join(" | ", header)}' vs '{string.Join(" | ", DifficultyColumns)}'");
            return problems;
        }
        foreach (string row in rows.Select(r => r[0]).Distinct())
            if (Array.IndexOf(DifficultyRows, row) < 0 || rows.Count(r => r[0] == row) > 1)
                problems.Add($"{faction} difficulty row '{row}': not Easy / Normal / Hard once each");
        for (int k = 0; k < DifficultyRows.Length; k++)
        {
            string name = DifficultyRows[k];
            string[]? row = rows.FirstOrDefault(r => r[0] == name);
            if (row == null)
            {
                problems.Add($"{faction} {name}: no row");
                continue;
            }
            string[] data = DifficultyCells(ai.Difficulty((AiDifficulty)k));
            for (int c = 0; c < data.Length; c++)
            {
                string page = c + 1 < row.Length ? row[c + 1] : "(none)";
                if (page != data[c]) problems.Add($"{faction} {name} {DifficultyColumns[c + 1]}: page '{page}' vs data '{data[c]}'");
            }
        }
        return problems;
    }

    [Fact]
    public void J_EachPagesDifficultyTable_IsItsAiFile_BothWays()
    {
        foreach (string faction in Factions)
        {
            List<string> problems = DifficultyProblems(Data, faction, PageTables.FactionLines(faction));
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }
    }

    /// <summary>Every cell of both pages' tables, for <see cref="J_AOneCellPageEdit_FailsByFactionDifficultyAndColumn"/>.</summary>
    public static TheoryData<string, int, int> DifficultyCellsOfBothPages()
    {
        var rows = new TheoryData<string, int, int>();
        foreach (string faction in Factions)
            for (int k = 0; k < DifficultyRows.Length; k++)
                for (int c = 1; c < DifficultyColumns.Length; c++)
                    rows.Add(faction, k, c);
        return rows;
    }

    /// <summary>
    /// The pin is not vacuous: changing any one cell of either page (a yes / no flipped, a number moved) fails with exactly
    /// one problem, naming the faction, the difficulty and the column.
    /// </summary>
    [Theory]
    [MemberData(nameof(DifficultyCellsOfBothPages))]
    public void J_AOneCellPageEdit_FailsByFactionDifficultyAndColumn(string faction, int difficulty, int column)
    {
        string[] lines = PageTables.FactionLines(faction);
        string name = DifficultyRows[difficulty];
        int h = PageTables.Heading(lines, "## " + DifficultySection, faction + ".md");
        int i = Array.FindIndex(lines, h, l => l.StartsWith($"| {name} |", StringComparison.Ordinal));
        Assert.True(i >= 0, $"{faction}.md has no '{name}' difficulty row");
        string[] cells = PageTables.Cells(lines[i]);
        string old = cells[column];
        cells[column] = old switch
        {
            "yes" => "no",
            "no" => "yes",
            _ when old.EndsWith('%') => Num(double.Parse(old[..^1], CultureInfo.InvariantCulture) + 5) + "%",
            _ => Num(double.Parse(old, CultureInfo.InvariantCulture) + 1),
        };
        string[] edited = (string[])lines.Clone();
        edited[i] = "| " + string.Join(" | ", cells) + " |";
        List<string> problems = DifficultyProblems(Data, faction, edited);
        string expected = $"{faction} {name} {DifficultyColumns[column]}: page '{cells[column]}' vs data '{old}'";
        Assert.True(problems.SequenceEqual(new[] { expected }), $"expected only '{expected}', got:\n{string.Join("\n", problems)}");
    }

    /// <summary>A missing row, a doubled row and a renamed column each fail by name.</summary>
    [Fact]
    public void J_AMissingOrDoubledRowOrRenamedColumn_FailsByName()
    {
        string[] lines = PageTables.FactionLines("whirlwind");
        int normal = Array.FindIndex(lines, l => l.StartsWith("| Normal |", StringComparison.Ordinal));
        Assert.True(normal >= 0, "whirlwind.md has no '| Normal |' row");
        List<string> without = lines.ToList();
        without.RemoveAt(normal);
        Assert.Contains("whirlwind Normal: no row", DifficultyProblems(Data, "whirlwind", without.ToArray()));

        List<string> twice = lines.ToList();
        twice.Insert(normal, lines[normal]);
        Assert.Contains("whirlwind difficulty row 'Normal': not Easy / Normal / Hard once each", DifficultyProblems(Data, "whirlwind", twice.ToArray()));

        string[] renamed = (string[])lines.Clone();
        int header = Array.FindIndex(lines, l => l.StartsWith("| Difficulty |", StringComparison.Ordinal));
        renamed[header] = lines[header].Replace("| Uses abilities |", "| Abilities |", StringComparison.Ordinal);
        Assert.Contains(DifficultyProblems(Data, "whirlwind", renamed), p => p.StartsWith("whirlwind difficulty header:", StringComparison.Ordinal));
    }

    /// <summary>The other way: one-value edits of the file fail against the unchanged page, each naming its cell.</summary>
    [Fact]
    public void J_AOneValueFileEdit_FailsByName()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            root["difficulties"]!["hard"]!["waveSizeMultiplier"] = 1.5;
            root["difficulties"]!["easy"]!["thinkInterval"] = 2.5;
            root["difficulties"]!["normal"]!["expandAfter"] = 500;
        });
        DataLoadResult result = DataLoader.LoadAll(dir.Path);
        Assert.True(result.Ok, string.Join("\n", result.Errors));
        List<string> problems = DifficultyProblems(result.Data!, "malazan", PageTables.FactionLines("malazan"));
        Assert.Equal(new[]
        {
            "malazan Easy Thinks every (s): page '2' vs data '2.5'",
            "malazan Normal Expands after (s): page '480' vs data '500'",
            "malazan Hard Wave size ×: page '1.25' vs data '1.5'",
        }, problems);
    }

    /// <summary>
    /// What docs/02 "AI opponent" states about each difficulty, as <c>ai.json</c> field values: the phrase in that
    /// difficulty's bullet that says it, the difficulty, the field, the value. Normal's think interval, every slack, wave
    /// size and expand time are not stated there (builder defaults, docs/01 2026-10-11), so only the pages pin them (J).
    /// </summary>
    public static readonly (string Phrase, AiDifficulty Difficulty, string Field, string Value)[] DesignStates =
    {
        ("thinks every 2 s", AiDifficulty.Easy, "thinkInterval", "2"),
        ("thinks every 0.5 s", AiDifficulty.Hard, "thinkInterval", "0.5"),
        ("25% gather bonus", AiDifficulty.Hard, "gatherBonus", "0.25"),
        ("doesn't micro", AiDifficulty.Easy, "focusFire", "false"),
        ("doesn't micro", AiDifficulty.Easy, "pullBackWounded", "false"),
        ("fair play at a relaxed pace", AiDifficulty.Normal, "focusFire", "false"),
        ("fair play at a relaxed pace", AiDifficulty.Normal, "pullBackWounded", "false"),
        ("focus fire", AiDifficulty.Hard, "focusFire", "true"),
        ("pulls back wounded", AiDifficulty.Hard, "pullBackWounded", "true"),
        ("doesn't use abilities", AiDifficulty.Easy, "useAbilities", "false"),
        ("uses abilities", AiDifficulty.Normal, "useAbilities", "true"),
        ("good ability use", AiDifficulty.Hard, "useAbilities", "true"),
    };

    /// <summary>A difficulty's field as <c>ai.json</c> writes it (seconds, a fraction, a bool), from the loaded values.</summary>
    private static string FieldText(AiDifficultyDef k, string field) => field switch
    {
        "thinkInterval" => Seconds(k.ThinkTicks),
        "gatherBonus" => Num(k.GatherBonus),
        "focusFire" => k.FocusFire ? "true" : "false",
        "pullBackWounded" => k.PullBackWounded ? "true" : "false",
        "useAbilities" => k.UseAbilities ? "true" : "false",
        _ => throw new ArgumentException(field),
    };

    /// <summary>
    /// Every difficulty field of <paramref name="d"/> that contradicts <see cref="DesignStates"/>, as
    /// "faction difficulty.field: data X, docs/02 says Y (phrase)"; empty when both factions hold docs/02's numbers.
    /// </summary>
    public static List<string> DesignProblems(GameData d)
    {
        var problems = new List<string>();
        foreach (string faction in Factions)
        {
            AiDef ai = d.Ai[d.FindFaction(faction)];
            foreach (var s in DesignStates)
            {
                string data = FieldText(ai.Difficulty(s.Difficulty), s.Field);
                string at = $"{faction} {DataLimits.AiDifficultyIds[(int)s.Difficulty]}.{s.Field}";
                if (data != s.Value) problems.Add($"{at}: data {data}, docs/02 says {s.Value} (\"{s.Phrase}\")");
            }
        }
        return problems;
    }

    /// <summary>
    /// docs/02's "AI opponent" bullet for each difficulty still says what <see cref="DesignStates"/> pins, so a docs/02 edit
    /// that changes a stated number fails here until the pin follows it.
    /// </summary>
    [Fact]
    public void K_Docs02_StatesWhatThePinsHold()
    {
        string[] lines = PageTables.Lines(Path.Combine("docs", "02-game-design.md"));
        int h = PageTables.Heading(lines, "## AI opponent", "docs/02");
        string[] section = lines[h..].TakeWhile((l, k) => k == 0 || !l.StartsWith("## ", StringComparison.Ordinal)).ToArray();
        List<string> bullets = Bullets(section); // nested bullets too, continuation lines joined
        foreach (var s in DesignStates)
        {
            string label = DataLimits.AiDifficultyIds[(int)s.Difficulty];
            label = char.ToUpperInvariant(label[0]) + label[1..];
            string? bullet = Bullet(bullets, label);
            Assert.True(bullet != null, $"docs/02 'AI opponent' has no '{label}:' bullet");
            Assert.True(bullet!.Contains(s.Phrase, StringComparison.Ordinal),
                $"docs/02 '{label}' no longer says \"{s.Phrase}\" ({s.Field} {s.Value}): update DesignStates with the doc");
        }
        // A flag can't say "rarely" (BUG-0541 item 3): Easy doesn't use abilities.
        Assert.DoesNotContain("rarely uses abilities", string.Join(" ", section));
    }

    [Fact]
    public void K_BothFactions_HoldDocs02sNumbers()
    {
        List<string> problems = DesignProblems(Data);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>A mutation of any field docs/02 states, in either faction's file, fails naming the faction and the field.</summary>
    [Theory]
    [InlineData("malazan", "easy", "thinkInterval", "1.5")]
    [InlineData("whirlwind", "easy", "thinkInterval", "3")]
    [InlineData("malazan", "hard", "thinkInterval", "1")]
    [InlineData("whirlwind", "hard", "thinkInterval", "0.45")]
    [InlineData("malazan", "hard", "gatherBonus", "0.3")]
    [InlineData("whirlwind", "hard", "gatherBonus", "0")]
    [InlineData("whirlwind", "easy", "focusFire", "true")]
    [InlineData("malazan", "normal", "focusFire", "true")]
    [InlineData("whirlwind", "hard", "focusFire", "false")]
    [InlineData("malazan", "easy", "pullBackWounded", "true")]
    [InlineData("whirlwind", "normal", "pullBackWounded", "true")]
    [InlineData("malazan", "hard", "pullBackWounded", "false")]
    [InlineData("whirlwind", "easy", "useAbilities", "true")]
    [InlineData("malazan", "normal", "useAbilities", "false")]
    [InlineData("whirlwind", "hard", "useAbilities", "false")]
    public void K_AMutatedDesignField_FailsNamingTheField(string faction, string difficulty, string field, string json)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson($"factions/{faction}/ai.json", root => root["difficulties"]![difficulty]![field] = JsonNode.Parse(json));
        DataLoadResult result = DataLoader.LoadAll(dir.Path);
        Assert.True(result.Ok, string.Join("\n", result.Errors));
        List<string> problems = DesignProblems(result.Data!);
        Assert.Single(problems);
        Assert.StartsWith($"{faction} {difficulty}.{field}: data {json}, docs/02 says ", problems[0]);
    }
}
