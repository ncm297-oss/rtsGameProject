using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// Content (D14, per opening since D21): each shipped faction's <c>ai.json</c> is its page's "AI build order (Normal)"
/// section, both ways. The section's openings table lists every opening in file order (id, the player-facing name and
/// description); the retreat line is shared. Under each opening's own heading (<see cref="OpeningHeading"/>): the step
/// table row for row (trigger, action, id, count), the composition table weight for weight, the "Composition target"
/// percentages (Age II) and the wave threshold (pop and cost). Every id belongs to the faction and can be acted on: a unit
/// is trained at a building the opening places, a tech at a building slot the faction fills, and the Age I composition only
/// names units the opening's Age I steps can train. <see cref="Problems"/> returns every mismatch as text naming the
/// faction, the opening and the row, so a one-sided edit says which side moved.
/// </summary>
public class AiContentTests
{
    private static readonly string[] Factions = { "malazan", "whirlwind" };

    /// <summary>The heading of each page's AI section.</summary>
    public const string Section = "AI build order (Normal)";

    /// <summary>The openings table's header (the section's first table, before the first opening heading).</summary>
    public static readonly string[] OpeningColumns = { "#", "Id", "Name (shown to the player)", "Description (shown to the player)" };

    private static GameData Data => TestSim.Data;

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>The heading of opening <paramref name="k"/> (0-based) on its faction page: "### Opening 1: `standard` (Combined arms)".</summary>
    public static string OpeningHeading(int k, AiOpening o) => $"### Opening {k + 1}: `{o.Id}` ({o.DisplayName})";

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

    /// <summary>The lines under opening <paramref name="k"/>'s heading (heading included) up to the next heading of level 2 or 3; fails naming the page and opening if the heading is missing.</summary>
    public static string[] OpeningLines(string[] pageLines, string faction, int k, AiOpening o)
    {
        string heading = OpeningHeading(k, o);
        int h = Array.IndexOf(pageLines, heading);
        Assert.True(h >= 0, $"{faction}.md has no '{heading}' heading");
        int end = h + 1;
        while (end < pageLines.Length && !pageLines[end].StartsWith("## ", StringComparison.Ordinal) && !pageLines[end].StartsWith("### ", StringComparison.Ordinal)) end++;
        return pageLines[h..end];
    }

    /// <summary>The lines' bullets ("- **Attack waves:** ..."), continuation lines joined, bold markers removed.</summary>
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

    /// <summary>An opening's table row as the openings table writes it: "standard | Combined arms | Barracks, ...".</summary>
    public static string OpeningRowText(AiOpening o) => $"{o.Id} | {o.DisplayName} | {o.Description}";

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
        int firstOpening = Array.FindIndex(section, l => l.StartsWith("### ", StringComparison.Ordinal));
        string[] shared = firstOpening < 0 ? section : section[..firstOpening];

        // The openings table: one row per opening, in file order.
        List<List<string>> sharedTables = PageTables.TablesInSection(shared, 0);
        if (sharedTables.Count != 1) problems.Add($"{faction}: the section's shared part has {sharedTables.Count} tables, expected 1 (the openings)");
        else
        {
            string[] header = PageTables.Cells(sharedTables[0][0]);
            if (!header.SequenceEqual(OpeningColumns)) problems.Add($"{faction} openings header: page '{string.Join(" | ", header)}'");
            string[][] rows = sharedTables[0].Skip(2).Select(r => PageTables.Cells(r, stripBackticks: true)).ToArray();
            if (rows.Length != ai.Openings.Length) problems.Add($"{faction} openings: page {rows.Length} vs data {ai.Openings.Length}");
            for (int k = 0; k < Math.Max(rows.Length, ai.Openings.Length); k++)
            {
                string page = k < rows.Length ? string.Join(" | ", rows[k].Skip(1)) : "(none)";
                string data = k < ai.Openings.Length ? OpeningRowText(ai.Openings[k]) : "(none)";
                if (k < rows.Length && rows[k][0] != (k + 1).ToString(CultureInfo.InvariantCulture))
                    problems.Add($"{faction} opening {k + 1}: the page numbers it '{rows[k][0]}'");
                if (page != data) problems.Add($"{faction} opening {k + 1}: page '{page}' vs data '{data}'");
            }
        }

        // The opening headings: exactly one per opening, in file order, and no other level-3 heading in the section.
        string[] headings = section.Where(l => l.StartsWith("### ", StringComparison.Ordinal)).ToArray();
        string[] expected = ai.Openings.Select((o, k) => OpeningHeading(k, o)).ToArray();
        if (!headings.SequenceEqual(expected))
            problems.Add($"{faction} opening headings: page '{string.Join("; ", headings)}' vs data '{string.Join("; ", expected)}'");

        for (int k = 0; k < ai.Openings.Length; k++)
        {
            AiOpening o = ai.Openings[k];
            int h = Array.IndexOf(section, expected[k]);
            if (h < 0) continue; // reported above
            int end = h + 1;
            while (end < section.Length && !section[end].StartsWith("### ", StringComparison.Ordinal)) end++;
            OpeningProblems(d, faction, o, section[h..end], problems);
        }

        string? retreat = Bullet(Bullets(shared), "Retreat");
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

    /// <summary>One opening's subsection against its data: the step table, the composition table and its two bullets.</summary>
    private static void OpeningProblems(GameData d, string faction, AiOpening o, string[] lines, List<string> problems)
    {
        string who = $"{faction} {o.Id}";
        List<List<string>> tables = PageTables.TablesInSection(lines, 0);
        if (tables.Count != 2)
        {
            problems.Add($"{who}: the opening has {tables.Count} tables, expected 2 (steps, composition)");
            return;
        }

        // Steps, row for row.
        string[][] rows = tables[0].Skip(2).Select(r => PageTables.Cells(r, stripBackticks: true)).ToArray();
        var steps = o.BuildOrder;
        if (rows.Length != steps.Length) problems.Add($"{who} steps: page {rows.Length} vs data {steps.Length}");
        for (int i = 0; i < Math.Max(rows.Length, steps.Length); i++)
        {
            string page = i < rows.Length ? string.Join(" | ", rows[i].Skip(1).Take(4)) : "(none)";
            string data = i < steps.Length ? StepText(steps[i]) : "(none)";
            if (i < rows.Length && rows[i][0] != (i + 1).ToString(CultureInfo.InvariantCulture))
                problems.Add($"{who} step {i + 1}: the page numbers it '{rows[i][0]}'");
            if (page != data) problems.Add($"{who} step {i + 1}: page '{page}' vs data '{data}'");
        }

        // Composition table: unit | Age I | Age II, "—" for none.
        string[][] comp = tables[1].Skip(2).Select(r => PageTables.Cells(r, stripBackticks: true)).ToArray();
        for (int age = 1; age <= o.Compositions.Length; age++)
        {
            AiComposition c = o.Compositions[age - 1];
            string data = string.Join(", ", c.Units.Select((u, k) => $"{d.Units[u].Key} {Num(c.Weights[k])}").OrderBy(x => x, StringComparer.Ordinal));
            string page = string.Join(", ", comp.Where(r => r.Length > age && r[age] != "—").Select(r => $"{r[0]} {r[age]}").OrderBy(x => x, StringComparer.Ordinal));
            if (page != data) problems.Add($"{who} composition age {age}: page '{page}' vs data '{data}'");
        }

        List<string> bullets = Bullets(lines);

        // "Composition target (by pop): 35% Heavy Infantry, ..." is the Age II mix by display name.
        string? target = Bullet(bullets, "Composition target (by pop)");
        if (target == null) problems.Add($"{who}: no 'Composition target (by pop)' line");
        else
        {
            AiComposition last = o.Compositions[^1];
            string data = string.Join(", ", last.Units.Select((u, k) => $"{Num(last.Weights[k])}% {d.Units[u].DisplayName}").OrderBy(x => x, StringComparer.Ordinal));
            string page = string.Join(", ", Regex.Matches(target, @"(\d+(?:\.\d+)?)% ([^,]+)")
                .Select(m => $"{m.Groups[1].Value}% {m.Groups[2].Value.Trim().TrimEnd('.')}").OrderBy(x => x, StringComparer.Ordinal));
            if (page != data) problems.Add($"{who} composition target: page '{page}' vs data '{data}'");
        }

        string? attack = Bullet(bullets, "Attack waves");
        Match a = attack == null ? Match.Empty : Regex.Match(attack, @"first at (\d+(?:\.\d+)?) army pop and ([\d,]+) gold \+ wood");
        if (!a.Success) problems.Add($"{who}: no 'Attack waves: first at N army pop and N gold + wood' line");
        else
        {
            string page = $"{a.Groups[1].Value} pop, {a.Groups[2].Value.Replace(",", "")} cost";
            string data = $"{Num(o.AttackMinHalfPop / 2.0)} pop, {o.AttackMinCost.ToString(CultureInfo.InvariantCulture)} cost";
            if (page != data) problems.Add($"{who} attack: page '{page}' vs data '{data}'");
        }
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

    /// <summary>D21: both shipped files hold exactly three openings, the first <c>standard</c> (the one Easy plays).</summary>
    [Fact]
    public void A_BothFiles_HoldThreeOpenings_StandardFirst()
    {
        foreach (string faction in Factions)
        {
            AiDef ai = Data.Ai[Data.FindFaction(faction)];
            Assert.Equal(3, ai.Openings.Length);
            Assert.Equal("standard", ai.Openings[0].Id);
        }
    }

    /// <summary>
    /// D21, the pages' "An Easy AI always plays opening 1": on the shipped files an Easy seat of either faction picks
    /// opening 0 on every seed in 1-50, and a Normal seat picks each of the three somewhere in 1-50 (the reports'
    /// smallest-seed rule needs one), read from <see cref="Simulation.AiOpening"/>.
    /// </summary>
    [Fact]
    public void A_EasyPlaysTheFirstOpening_NormalReachesEveryOpening()
    {
        foreach (string faction in Factions)
        {
            int player = Data.FindFaction(faction); // AiScenes.Seats: player p plays faction p mod 2
            var normal = new HashSet<int>();
            for (ulong seed = 1; seed <= AiOpeningSeeds.MaxReportSeed; seed++)
            {
                Assert.True(AiOpeningSeeds.Pick(seed, player, AiDifficulty.Easy) == 0, $"{faction} Easy seed {seed}: not the first opening");
                normal.Add(AiOpeningSeeds.Pick(seed, player, AiDifficulty.Normal));
            }
            Assert.Equal(Enumerable.Range(0, Data.Ai[player].Openings.Length), normal.OrderBy(x => x));
        }
    }

    /// <summary>
    /// The pin is not vacuous: a one-line page edit (a count, a trigger, an id, a weight, the wave, the retreat, an opening's
    /// name, description or heading) and a missing or extra row each fail, naming the faction, the opening and the row.
    /// </summary>
    [Theory]
    [InlineData("| 1 | time 0 | train | `malazan_laborer` | 15 |", "| 1 | time 0 | train | `malazan_laborer` | 14 |", "malazan standard step 1: page 'time 0 | train | malazan_laborer | 14'")]
    [InlineData("| 2 | pop 6 | build |", "| 2 | pop 7 | build |", "malazan standard step 2: page 'pop 7")]
    [InlineData("| 3 | pop 8 | build | `malazan_depot`", "| 3 | pop 8 | build | `malazan_barracks`", "malazan standard step 3:")]
    [InlineData("| 26 | time 570 | build | `malazan_watchtower` | 1 | Watchtower |", "", "malazan standard steps: page 25 vs data 26")]
    [InlineData("| `malazan_sapper` | — | 10 |", "| `malazan_sapper` | 10 | 10 |", "malazan standard composition age 1:")]
    [InlineData("| `malazan_catapult` | — | 5 |", "| `malazan_catapult` | — | 6 |", "malazan standard composition age 2:")]
    [InlineData("10% Cadre Mage, 10% Sapper, 5% Catapult.", "10% Cadre Mage, 10% Sapper, 6% Catapult.", "malazan standard composition target:")]
    [InlineData("first at 16 army pop and 1,235 gold", "first at 16 army pop and 1,236 gold", "malazan standard attack: page '16 pop, 1236 cost'")]
    [InlineData("lost 50% falls back; a unit under 30% hp", "lost 40% falls back; a unit under 30% hp", "malazan retreat:")]
    [InlineData("| 2 | `crossbow_push` | Crossbow push |", "| 2 | `crossbow_push` | Crossbow Push |", "malazan opening 2: page 'crossbow_push | Crossbow Push")]
    [InlineData("then two Cadre Towers: few soldiers early", "then two Cadre Towers: few soldiers at first", "malazan opening 3: page 'cadre_rush | Fast Age II")]
    [InlineData("### Opening 3: `cadre_rush` (Fast Age II)", "### Opening 3: `cadre_rush` (Fast age II)", "malazan opening headings:")]
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

    /// <summary>The other way: a one-value edit of the file fails against the unchanged page, naming the opening and the row.</summary>
    [Fact]
    public void C_AOneValueFileEdit_FailsByName()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/whirlwind/ai.json", root =>
        {
            AiScenes.Opening(root)["buildOrder"]![3]!["at"] = JsonNode.Parse("""{ "pop": 10 }""");
            AiScenes.Opening(root)["attack"]!["minArmyPop"] = 15;
            AiScenes.Opening(root, 2)["buildOrder"]![2]!["at"] = JsonNode.Parse("""{ "pop": 9 }""");
            AiScenes.Opening(root, 1)["displayName"] = "Raider Rush";
        });
        DataLoadResult result = DataLoader.LoadAll(dir.Path);
        Assert.True(result.Ok, string.Join("\n", result.Errors));
        List<string> problems = Problems(result.Data!, "whirlwind", PageTables.FactionLines("whirlwind"));
        Assert.Contains("whirlwind standard step 4: page 'pop 9 | build | whirlwind_supply_cache | 1' vs data 'pop 10 | build | whirlwind_supply_cache | 1'", problems);
        Assert.Contains(problems, p => p.StartsWith("whirlwind standard attack: page '14 pop, 861 cost' vs data '15 pop", StringComparison.Ordinal));
        Assert.Contains("whirlwind holy_war step 3: page 'pop 8 | build | whirlwind_raider_camp | 1' vs data 'pop 9 | build | whirlwind_raider_camp | 1'", problems);
        Assert.Contains(problems, p => p.StartsWith("whirlwind opening 2: page 'raider_rush | Raider rush |", StringComparison.Ordinal));
        // The renamed opening's heading no longer matches either, so its subsection is not read (one problem for both).
        Assert.Contains(problems, p => p.StartsWith("whirlwind opening headings:", StringComparison.Ordinal));
        Assert.Equal(5, problems.Count);
    }

    /// <summary>The steps of every shipped opening of <paramref name="faction"/>, with the opening's index.</summary>
    private static IEnumerable<(int K, AiOpening O)> OpeningsOf(string faction) =>
        Data.Ai[Data.FindFaction(faction)].Openings.Select((o, k) => (k, o));

    /// <summary>
    /// Every id is the faction's and can be acted on, per opening: a train step's unit (and every composition unit) is
    /// trained at the Town Hall or at a building some step of the same opening places; a build step's required buildings are
    /// placed by a step of the opening too; a research step's tech is common or the faction's, at a building slot one of its
    /// steps fills; an Age II requirement is met by the opening's own Age II step.
    /// </summary>
    [Fact]
    public void D_EveryId_IsTheFactions_AndReachable()
    {
        foreach (string faction in Factions)
        {
            int f = Data.FindFaction(faction);
            foreach ((int k, AiOpening o) in OpeningsOf(faction))
            {
                var built = new HashSet<int>(o.BuildOrder.Where(s => s.Action == AiStepAction.Build).Select(s => s.Target));
                built.Add(Data.Buildings.Select((b, i) => (b, i)).Single(x => x.b.Faction == f && x.b.Slot == BuildingSlot.TownHall).i);
                var researched = new HashSet<int>(o.BuildOrder.Where(s => s.Action == AiStepAction.Research).Select(s => s.Target));
                var builtSlots = new HashSet<BuildingSlot>(built.Select(b => Data.Buildings[b].Slot));
                for (int i = 0; i < o.BuildOrder.Length; i++)
                {
                    AiStep s = o.BuildOrder[i];
                    string where = $"{faction} {o.Id} step {i + 1} ({s.TargetKey})";
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
                foreach (AiComposition c in o.Compositions)
                    foreach (int u in c.Units)
                    {
                        Assert.True(Data.Units[u].Faction == f, $"{faction} {o.Id} age {c.Age}: {Data.Units[u].Key} is another faction's");
                        Assert.True(Data.Units[u].Slot != UnitSlot.Worker, $"{faction} {o.Id} age {c.Age}: {Data.Units[u].Key} is a worker");
                        Assert.True(built.Contains(Data.Units[u].TrainedAtTypeId), $"{faction} {o.Id} age {c.Age}: no step places {Data.Units[u].TrainedAt}");
                    }
            }
        }
    }

    /// <summary>
    /// D21 (the brief's criterion 1): in every opening, every <c>build</c> or <c>research</c> step's requirement stands earlier
    /// in the same order or at the start: each required building is the Town Hall or placed by an earlier <c>build</c> step;
    /// each required tech is researched by an earlier step; a <c>requiresAnyOf</c> (Age II's "two kinds of building") is met
    /// by the hall and the earlier build steps' slots. The executor runs steps side by side, so file order is not time order,
    /// but a requirement written after its step reads as a dead end to the owner and is one when its trigger comes later.
    /// </summary>
    [Fact]
    public void D_EveryRequirement_StandsEarlierInTheSameOrder()
    {
        foreach (string faction in Factions)
        {
            int f = Data.FindFaction(faction);
            int hall = Data.Buildings.Select((b, i) => (b, i)).Single(x => x.b.Faction == f && x.b.Slot == BuildingSlot.TownHall).i;
            foreach ((int k, AiOpening o) in OpeningsOf(faction))
            {
                var built = new HashSet<int> { hall };
                var researched = new HashSet<int>();
                for (int i = 0; i < o.BuildOrder.Length; i++)
                {
                    AiStep s = o.BuildOrder[i];
                    string where = $"{faction} {o.Id} step {i + 1} ({s.TargetKey})";
                    if (s.Action == AiStepAction.Build)
                    {
                        BuildingDef b = Data.Buildings[s.Target];
                        foreach (int r in b.RequiresBuildings) Assert.True(built.Contains(r), $"{where}: needs {Data.Buildings[r].Key}, placed by no earlier step");
                        foreach (int t in b.RequiresTechs) Assert.True(researched.Contains(t), $"{where}: needs {Data.Techs[t].Key}, researched by no earlier step");
                        built.Add(s.Target);
                    }
                    else if (s.Action == AiStepAction.Research)
                    {
                        TechDef t = Data.Techs[s.Target];
                        Assert.True(built.Any(b => Data.Buildings[b].Slot == t.ResearchedAtSlot), $"{where}: no earlier step places a {t.ResearchedAtSlot}");
                        foreach (int x in t.RequiresTechs) Assert.True(researched.Contains(x), $"{where}: needs {Data.Techs[x].Key}, researched by no earlier step");
                        foreach (int r in t.RequiresBuildings) Assert.True(built.Contains(r), $"{where}: needs {Data.Buildings[r].Key}, placed by no earlier step");
                        if (t.RequiresAnyOfCount > 0)
                        {
                            int kinds = t.RequiresAnyOfSlots.Count(slot => built.Any(b => (int)Data.Buildings[b].Slot == slot));
                            Assert.True(kinds >= t.RequiresAnyOfCount, $"{where}: {kinds} of the {t.RequiresAnyOfCount} kinds of building it needs stand before it");
                        }
                        researched.Add(s.Target);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Age I's composition names only units the opening's Age I steps can train: the unit and its building need no age tech,
    /// and the building is placed by a step due before the Age II research (a pop step, or a time step earlier than it).
    /// Age II's composition is Age I's plus the rest of the army, the same weights for the shared units (the pages' one
    /// list per opening).
    /// </summary>
    [Fact]
    public void E_AgeI_NamesOnlyWhatAgeICanTrain_AndAgeIIExtendsIt()
    {
        int age2 = Data.FindTech(DataLimits.AgeTechIds[0]);
        foreach (string faction in Factions)
            foreach ((int k, AiOpening o) in OpeningsOf(faction))
            {
                AiStep ageStep = o.BuildOrder.Single(s => s.Action == AiStepAction.Research && s.Target == age2);
                Assert.Equal(AiStepTrigger.Time, ageStep.Trigger);
                var ageOne = new HashSet<int>(o.BuildOrder
                    .Where(s => s.Action == AiStepAction.Build && (s.Trigger != AiStepTrigger.Time || s.At < ageStep.At))
                    .Select(s => s.Target));
                AiComposition one = o.Compositions[0], two = o.Compositions[1];
                foreach (int u in one.Units)
                {
                    UnitDef unit = Data.Units[u];
                    string where = $"{faction} {o.Id} Age I {unit.Key}";
                    Assert.False(unit.RequiresTechs.Contains(age2), $"{where}: needs Age II");
                    Assert.False(Data.Buildings[unit.TrainedAtTypeId].RequiresTechs.Contains(age2), $"{where}: its building needs Age II");
                    Assert.True(ageOne.Contains(unit.TrainedAtTypeId), $"{where}: no Age I step places {unit.TrainedAt}");
                }
                for (int j = 0; j < one.Units.Length; j++)
                {
                    int at = two.Units.IndexOf(one.Units[j]);
                    Assert.True(at >= 0, $"{faction} {o.Id}: Age II drops {Data.Units[one.Units[j]].Key}");
                    Assert.Equal(one.Weights[j], two.Weights[at]);
                }
                // The pages' percentages: the Age II mix sums to 100.
                Assert.Equal(100f, two.Weights.Sum());
            }
    }

    /// <summary>
    /// Per opening: the wave cost is the cost of the wave's pop at the Age I mix (weights by pop), rounded down, so the mix
    /// reaches it at that pop; the worker steps take the starting workers to each age's target in the step's "Why"; Age II's
    /// step is at the time its "Why" says; the expand step is at Normal's <c>expandAfter</c>, once; the opening's start hall
    /// and houses alone reach the pop cap (the expansion is not counted: it waits for a known free mine, D21's pairs report).
    /// </summary>
    [Fact]
    public void F_TheThresholdsAndTargets_FollowFromTheData()
    {
        foreach (string faction in Factions)
        {
            int f = Data.FindFaction(faction);
            string[] page = PageTables.FactionLines(faction);
            foreach ((int k, AiOpening o) in OpeningsOf(faction))
            {
                AiComposition one = o.Compositions[0];
                double perPop = 0;
                for (int j = 0; j < one.Units.Length; j++)
                {
                    UnitDef u = Data.Units[one.Units[j]];
                    perPop += one.Weights[j] * (u.CostGold + u.CostWood) / (u.HalfPop / 2.0);
                }
                perPop /= one.Weights.Sum();
                Assert.True((int)Math.Floor(o.AttackMinHalfPop / 2.0 * perPop) == o.AttackMinCost,
                    $"{faction} {o.Id}: minArmyCost {o.AttackMinCost} vs {Num(o.AttackMinHalfPop / 2.0)} pop x {Num(perPop)} per pop");

                string[][] rows = PageTables.FirstTableRows(OpeningLines(page, faction, k, o), 0, stripBackticks: true).ToArray();
                int workers = Data.Rules.StartingWorkers;
                for (int i = 0; i < o.BuildOrder.Length; i++)
                {
                    AiStep s = o.BuildOrder[i];
                    string why = rows[i][5];
                    if (s.Action == AiStepAction.Train && Data.Units[s.Target].Slot == UnitSlot.Worker)
                    {
                        workers += s.Count;
                        Match m = Regex.Match(why, @"^Workers to (\d+) in Age I+\b");
                        Assert.True(m.Success, $"{faction} {o.Id} step {i + 1}: a worker step's 'Why' names no target: '{why}'");
                        Assert.Equal(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), workers);
                    }
                    if (s.Action == AiStepAction.Research && s.TargetKey == DataLimits.AgeTechIds[0])
                    {
                        Match m = Regex.Match(why, @"^Age II at (\d+):(\d\d)");
                        Assert.True(m.Success, $"{faction} {o.Id} step {i + 1}: '{why}'");
                        int seconds = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 60 + int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                        Assert.Equal(seconds * SimConstants.TicksPerSecond, s.At);
                    }
                    if (s.Action == AiStepAction.Expand)
                    {
                        Assert.Equal(AiStepTrigger.Time, s.Trigger);
                        Assert.Equal(Data.Ai[f].Difficulty(AiDifficulty.Normal).ExpandAfterTicks, s.At);
                    }
                }
                int expands = o.BuildOrder.Count(s => s.Action == AiStepAction.Expand);
                Assert.True(expands == 1, $"{faction} {o.Id}: {expands} expand steps, the brief's one");

                int hall = Data.Buildings.Single(b => b.Faction == f && b.Slot == BuildingSlot.TownHall).HalfPopProvided;
                int houses = o.BuildOrder.Where(s => s.Action == AiStepAction.Build && Data.Buildings[s.Target].Slot == BuildingSlot.House)
                    .Sum(s => Data.Buildings[s.Target].HalfPopProvided);
                Assert.True(hall + houses >= Data.Rules.HalfPopCap,
                    $"{faction} {o.Id}: the opening's hall and houses give {Num((hall + houses) / 2.0)} pop, below the cap");
            }
        }
    }

    /// <summary>D21: every opening is at most 40 steps (the brief's bound, well under the loader's 256).</summary>
    [Fact]
    public void F_EveryOpening_IsAtMost40Steps()
    {
        foreach (string faction in Factions)
            foreach ((int k, AiOpening o) in OpeningsOf(faction))
                Assert.True(o.BuildOrder.Length <= 40, $"{faction} {o.Id}: {o.BuildOrder.Length} steps");
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
        ("doesn't focus fire or pull back wounded", AiDifficulty.Normal, "focusFire", "false"),
        ("doesn't focus fire or pull back wounded", AiDifficulty.Normal, "pullBackWounded", "false"),
        ("focus fire", AiDifficulty.Hard, "focusFire", "true"),
        ("pulls back wounded", AiDifficulty.Hard, "pullBackWounded", "true"),
        ("uses abilities", AiDifficulty.Easy, "useAbilities", "true"), // D19: the owner's 2026-10-14 decision
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
        List<string> problems = Docs02Problems(PageTables.Lines(Path.Combine("docs", "02-game-design.md")));
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>
    /// Every <see cref="DesignStates"/> phrase docs/02's "AI opponent" bullets (in <paramref name="lines"/>, the whole file;
    /// tests pass an edited copy) no longer carry, as "docs/02 'Label' no longer says ...", plus a "rarely uses abilities"
    /// a flag can't hold (BUG-0541 item 3); empty when the doc says what the pins hold.
    /// </summary>
    public static List<string> Docs02Problems(string[] lines)
    {
        var problems = new List<string>();
        int h = PageTables.Heading(lines, "## AI opponent", "docs/02");
        string[] section = lines[h..].TakeWhile((l, k) => k == 0 || !l.StartsWith("## ", StringComparison.Ordinal)).ToArray();
        List<string> bullets = Bullets(section); // nested bullets too, continuation lines joined
        foreach (var s in DesignStates)
        {
            string label = DataLimits.AiDifficultyIds[(int)s.Difficulty];
            label = char.ToUpperInvariant(label[0]) + label[1..];
            string? bullet = Bullet(bullets, label);
            if (bullet == null) problems.Add($"docs/02 'AI opponent' has no '{label}:' bullet");
            else if (!bullet.Contains(s.Phrase, StringComparison.Ordinal))
                problems.Add($"docs/02 '{label}' no longer says \"{s.Phrase}\" ({s.Field} {s.Value}): update DesignStates with the doc");
        }
        if (string.Join(" ", section).Contains("rarely uses abilities", StringComparison.Ordinal))
            problems.Add("docs/02 'AI opponent' says \"rarely uses abilities\": useAbilities is a flag");
        return problems;
    }

    /// <summary>
    /// D17: Normal's two micro flags rest on the bullet saying it outright, not on "fair play at a relaxed pace" (which
    /// stays in the bullet and pins nothing): docs/02 with the new phrase cut fails naming both Normal fields, and the old
    /// phrase pins no row.
    /// </summary>
    [Fact]
    public void K_Docs02WithoutNormalsNoMicroPhrase_FailsNamingBothFields()
    {
        const string phrase = "doesn't focus fire or pull back wounded";
        string[] lines = PageTables.Lines(Path.Combine("docs", "02-game-design.md"));
        int at = Array.FindIndex(lines, l => l.Contains(phrase, StringComparison.Ordinal));
        Assert.True(at >= 0, $"docs/02 has no line with \"{phrase}\"");
        string[] edited = (string[])lines.Clone();
        edited[at] = edited[at].Replace(phrase, "plays it straight", StringComparison.Ordinal);
        List<string> problems = Docs02Problems(edited);
        Assert.Equal(new[]
        {
            $"docs/02 'Normal' no longer says \"{phrase}\" (focusFire false): update DesignStates with the doc",
            $"docs/02 'Normal' no longer says \"{phrase}\" (pullBackWounded false): update DesignStates with the doc",
        }, problems);
        Assert.DoesNotContain(DesignStates, s => s.Phrase.Contains("relaxed pace", StringComparison.Ordinal));
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
    [InlineData("whirlwind", "easy", "useAbilities", "false")]
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
