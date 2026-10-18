using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA (D15): the attack list D14's <c>ai.json</c> orders didn't get, kept as rows so the next edit of either file (an owner
/// tweak, M7's Teblor order) fails by name. Each row mutates a copy of a shipped file (a <see cref="TestDataDir"/> temp folder
/// or an edited copy of a page's lines, never the shipped files) and must fail at the loader's field path, in
/// <see cref="AiContentTests.Problems"/>, or in this file's oracles, which restate <c>AiContentTests</c> D, E and F for any
/// <see cref="GameData"/> (theirs read only the shipped data). The shipped files pass every row. Rows: (a) another faction's
/// id; (b) an Age II unit in Age I's mix; (c) a step written twice; (d) a pop off the half-pop grid; (e) a research no step can
/// act on; (f) a one-line edit of every build-order row of each page; (g) the composition tables and their sums; (h)
/// <c>minArmyCost</c> recomputed from <c>units.json</c>; (i) the count and step-count limits; (j, D21) a step moved before
/// its requirement.
/// <para>
/// D21 (2026-10-18): every row runs for every opening of each file (<c>openings[k]</c>, k = 0-2 today), never for the first
/// only; the problems name the opening (<c>"malazan crossbow_push step 4: ..."</c>).
/// </para>
/// </summary>
public class AiContentQaTests
{
    private static readonly string[] FactionIds = { "malazan", "whirlwind" };

    private static GameData Data => TestSim.Data;

    private static string AiFile(string faction) => $"factions/{faction}/ai.json";

    private static string Num(double v) => v.ToString(CultureInfo.InvariantCulture);

    private static string Other(string faction) => faction == "malazan" ? "whirlwind" : "malazan";

    private static AiOpening Opening(string faction, int k) => Data.Ai[Data.FindFaction(faction)].Openings[k];

    /// <summary>Every (faction, opening index) of the shipped files.</summary>
    public static TheoryData<string, int> Openings()
    {
        var rows = new TheoryData<string, int>();
        foreach (string faction in FactionIds)
            for (int k = 0; k < Data.Ai[Data.FindFaction(faction)].Openings.Length; k++) rows.Add(faction, k);
        return rows;
    }

    /// <summary>Loads a copy of the shipped data with <paramref name="edit"/> applied to <paramref name="faction"/>'s ai.json.</summary>
    private static DataLoadResult LoadWithAi(string faction, Action<JsonObject> edit)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(AiFile(faction), edit);
        return DataLoader.LoadAll(dir.Path);
    }

    /// <summary>The load failed with exactly one error, in the faction's ai.json at <paramref name="path"/>, its message starting <paramref name="message"/>.</summary>
    private static void AssertRefused(DataLoadResult r, string faction, string path, string message)
    {
        string all = string.Join("\n", r.Errors);
        Assert.False(r.Ok, $"loaded, expected '{AiFile(faction)}: {path}: {message}'");
        Assert.True(r.Errors.Count == 1, $"expected one error, got:\n{all}");
        DataError e = r.Errors[0];
        Assert.True(e.File == AiFile(faction) && e.Path == path && e.Message.StartsWith(message, StringComparison.Ordinal),
            $"expected '{AiFile(faction)}: {path}: {message}...', got:\n{all}");
    }

    private static JsonArray Steps(JsonObject root, int k) => AiScenes.Opening(root, k)["buildOrder"]!.AsArray();

    /// <summary>The index of the first step of <paramref name="action"/> (and id, when given) in opening <paramref name="k"/> of the file.</summary>
    private static int StepIndex(JsonObject root, int k, string action, string? id = null)
    {
        JsonArray steps = Steps(root, k);
        for (int i = 0; i < steps.Count; i++)
            if ((string)steps[i]!["do"]! == action && (id == null || (string?)steps[i]!["id"] == id)) return i;
        Assert.Fail($"opening {k}: no '{action}' step{(id == null ? "" : " for " + id)}");
        return -1;
    }

    private static JsonObject Shipped(string faction) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.Shipped, AiFile(faction))))!.AsObject();

    private static int StepIndex(string faction, int k, string action, string? id = null) => StepIndex(Shipped(faction), k, action, id);

    private static JsonObject Composition(JsonObject root, int k, int age) =>
        AiScenes.Opening(root, k)["compositions"]!.AsArray().Single(c => (int)c!["age"]! == age)!["units"]!.AsObject();

    // ---------- the oracles (AiContentTests D, E, F for any data) ----------

    /// <summary>
    /// Opening <paramref name="k"/>'s Age I mix names only units the Age I steps can train (no Age II on the unit or its
    /// building, the building placed by a step due before the Age II research); Age II keeps Age I's units at the same
    /// weights and sums to 100.
    /// </summary>
    internal static List<string> CompositionProblems(GameData d, string faction, int k)
    {
        var problems = new List<string>();
        AiOpening o = d.Ai[d.FindFaction(faction)].Openings[k];
        string who = $"{faction} {o.Id}";
        int age2 = d.FindTech(DataLimits.AgeTechIds[0]);
        AiStep ageStep = o.BuildOrder.Single(s => s.Action == AiStepAction.Research && s.Target == age2);
        var ageOne = o.BuildOrder.Where(s => s.Action == AiStepAction.Build && (s.Trigger != AiStepTrigger.Time || s.At < ageStep.At))
            .Select(s => s.Target).ToHashSet();
        AiComposition one = o.Compositions[0], two = o.Compositions[1];
        for (int j = 0; j < one.Units.Length; j++)
        {
            UnitDef u = d.Units[one.Units[j]];
            string where = $"{who} Age I {u.Key}:";
            if (u.RequiresTechs.Contains(age2)) problems.Add($"{where} needs Age II");
            else if (d.Buildings[u.TrainedAtTypeId].RequiresTechs.Contains(age2)) problems.Add($"{where} its building {u.TrainedAt} needs Age II");
            else if (!ageOne.Contains(u.TrainedAtTypeId)) problems.Add($"{where} no step before Age II places {u.TrainedAt}");
            int at = two.Units.IndexOf(one.Units[j]);
            if (at < 0) problems.Add($"{where} Age II drops it");
            else if (two.Weights[at] != one.Weights[j]) problems.Add($"{where} weight {one.Weights[j]} vs Age II {two.Weights[at]}");
        }
        if (two.Weights.Sum() != 100f) problems.Add($"{who} Age II weights sum to {two.Weights.Sum()}, not 100");
        return problems;
    }

    /// <summary>Every research step of opening <paramref name="k"/>: its tech is common or the faction's, at a building slot some build step (or the hall) fills.</summary>
    internal static List<string> ResearchProblems(GameData d, string faction, int k)
    {
        var problems = new List<string>();
        AiOpening o = d.Ai[d.FindFaction(faction)].Openings[k];
        var slots = o.BuildOrder.Where(s => s.Action == AiStepAction.Build).Select(s => d.Buildings[s.Target].Slot).ToHashSet();
        slots.Add(BuildingSlot.TownHall);
        for (int i = 0; i < o.BuildOrder.Length; i++)
        {
            AiStep s = o.BuildOrder[i];
            if (s.Action != AiStepAction.Research) continue;
            TechDef t = d.Techs[s.Target];
            if (!slots.Contains(t.ResearchedAtSlot)) problems.Add($"{faction} {o.Id} step {i + 1} ({s.TargetKey}): no step places a {t.ResearchedAtSlot}");
        }
        return problems;
    }

    /// <summary>
    /// D21 (the brief's criterion 1, restated for any data): every build or research step of opening <paramref name="k"/>
    /// whose required building or tech no earlier step (or the hall) gives, as "faction id step n (key): needs X".
    /// </summary>
    internal static List<string> OrderProblems(GameData d, string faction, int k)
    {
        var problems = new List<string>();
        int f = d.FindFaction(faction);
        AiOpening o = d.Ai[f].Openings[k];
        var built = new HashSet<int> { d.Buildings.Select((b, i) => (b, i)).Single(x => x.b.Faction == f && x.b.Slot == BuildingSlot.TownHall).i };
        var researched = new HashSet<int>();
        for (int i = 0; i < o.BuildOrder.Length; i++)
        {
            AiStep s = o.BuildOrder[i];
            string where = $"{faction} {o.Id} step {i + 1} ({s.TargetKey})";
            if (s.Action == AiStepAction.Build)
            {
                foreach (int t in d.Buildings[s.Target].RequiresTechs) if (!researched.Contains(t)) problems.Add($"{where}: needs {d.Techs[t].Key}");
                foreach (int b in d.Buildings[s.Target].RequiresBuildings) if (!built.Contains(b)) problems.Add($"{where}: needs {d.Buildings[b].Key}");
                built.Add(s.Target);
            }
            else if (s.Action == AiStepAction.Research)
            {
                foreach (int t in d.Techs[s.Target].RequiresTechs) if (!researched.Contains(t)) problems.Add($"{where}: needs {d.Techs[t].Key}");
                researched.Add(s.Target);
            }
        }
        return problems;
    }

    /// <summary>
    /// Opening <paramref name="k"/>'s <c>minArmyCost</c> recomputed from the raw files under <paramref name="dataDir"/>: the gold
    /// + wood of <c>minArmyPop</c> pop of the Age I mix (each unit's weight by pop over its pop), rounded down.
    /// </summary>
    internal static int RecomputedMinArmyCost(string dataDir, string faction, int k)
    {
        JsonObject ai = JsonNode.Parse(File.ReadAllText(Path.Combine(dataDir, "factions", faction, "ai.json")))!.AsObject();
        JsonArray units = JsonNode.Parse(File.ReadAllText(Path.Combine(dataDir, "factions", faction, "units.json")))!["units"]!.AsArray();
        double perPop = 0, weights = 0;
        foreach ((string id, JsonNode? w) in Composition(ai, k, 1))
        {
            JsonNode u = units.Single(x => (string)x!["id"]! == id)!;
            double cost = (double)u["cost"]!["gold"]! + (double)u["cost"]!["wood"]!;
            perPop += (double)w! * cost / (double)u["pop"]!;
            weights += (double)w!;
        }
        return (int)Math.Floor((double)AiScenes.Opening(ai, k)["attack"]!["minArmyPop"]! * perPop / weights);
    }

    /// <summary>Null when opening <paramref name="k"/>'s loaded <c>minArmyCost</c> is the recomputed one, else the mismatch with the recomputed number.</summary>
    internal static string? MinArmyCostProblem(GameData d, string dataDir, string faction, int k)
    {
        AiOpening o = d.Ai[d.FindFaction(faction)].Openings[k];
        int recomputed = RecomputedMinArmyCost(dataDir, faction, k);
        return o.AttackMinCost == recomputed ? null : $"{faction} {o.Id}: minArmyCost {o.AttackMinCost} vs recomputed {recomputed}";
    }

    [Theory]
    [MemberData(nameof(Openings))]
    public void Shipped_PassesEveryOracle(string faction, int k)
    {
        List<string> problems = CompositionProblems(Data, faction, k).Concat(ResearchProblems(Data, faction, k)).Concat(OrderProblems(Data, faction, k)).ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    // ---------- (a) another faction's id ----------

    public static TheoryData<string, string, int> ActionsPerOpening()
    {
        var rows = new TheoryData<string, string, int>();
        foreach ((string faction, int k) in Openings().Select(r => ((string)r[0], (int)r[1])))
            foreach (string action in new[] { "build", "train", "research" }) rows.Add(faction, action, k);
        return rows;
    }

    [Theory]
    [MemberData(nameof(ActionsPerOpening))]
    public void A_AStepNamingTheOtherFactionsId_IsRefusedAtItsId(string faction, string action, int k)
    {
        int other = Data.FindFaction(Other(faction));
        (string kind, string id) = action switch
        {
            "build" => ("building", Data.Buildings.First(b => b.Faction == other && b.Slot == BuildingSlot.House).Key),
            "train" => ("unit", Data.Units.First(u => u.Faction == other && u.Slot == UnitSlot.Worker).Key),
            _ => ("tech", Data.Techs.First(t => t.Faction == other).Key),
        };
        // The research step swapped is the faction's own tech's, so the order stays one step per (at, do, id).
        int i = StepIndex(faction, k, action, action == "research" ? Data.Techs.First(t => t.Faction == Data.FindFaction(faction)).Key : null);
        DataLoadResult r = LoadWithAi(faction, root => Steps(root, k)[i]!["id"] = id);
        AssertRefused(r, faction, $"openings[{k}].buildOrder[{i}].id", $"{kind} '{id}' belongs to faction '{Other(faction)}', not '{faction}'");
    }

    [Theory]
    [MemberData(nameof(Openings))]
    public void A_ACompositionNamingTheOtherFactionsUnit_IsRefusedAtTheUnit(string faction, int k)
    {
        int other = Data.FindFaction(Other(faction));
        string id = Data.Units.First(u => u.Faction == other && u.Slot == UnitSlot.Line).Key;
        DataLoadResult r = LoadWithAi(faction, root => Composition(root, k, 1)[id] = 10);
        AssertRefused(r, faction, $"openings[{k}].compositions[0].units.{id}", $"unit '{id}' belongs to faction '{Other(faction)}', not '{faction}'");
    }

    // ---------- (b) an Age II unit in the Age I mix ----------

    public static TheoryData<string, int, string> AgeTwoOnlyUnits()
    {
        var rows = new TheoryData<string, int, string>();
        foreach ((string faction, int k) in Openings().Select(r => ((string)r[0], (int)r[1])))
        {
            AiOpening o = Opening(faction, k);
            foreach (int u in o.Compositions[1].Units.Except(o.Compositions[0].Units))
                if (Data.Units[u].RequiresTechs.Length > 0 || Data.Buildings[Data.Units[u].TrainedAtTypeId].RequiresTechs.Length > 0
                    || !o.BuildOrder.Any(s => s.Action == AiStepAction.Build && s.Target == Data.Units[u].TrainedAtTypeId && s.Trigger == AiStepTrigger.Pop))
                    rows.Add(faction, k, Data.Units[u].Key);
        }
        return rows;
    }

    /// <summary>The loader takes it (a faction unit); the Age I oracle names it, and the page-file pin names the opening's Age I table.</summary>
    [Theory]
    [MemberData(nameof(AgeTwoOnlyUnits))]
    public void B_AnAgeIIUnitInTheAgeIMix_FailsNamingIt(string faction, int k, string unit)
    {
        GameData d = AiScenes.DataWithAi(faction, root => Composition(root.AsObject(), k, 1)[unit] = 10);
        string who = $"{faction} {Opening(faction, k).Id}";
        Assert.Contains(CompositionProblems(d, faction, k), p => p.StartsWith($"{who} Age I {unit}:", StringComparison.Ordinal));
        Assert.Contains(AiContentTests.Problems(d, faction, PageTables.FactionLines(faction)),
            p => p.StartsWith($"{who} composition age 1:", StringComparison.Ordinal));
    }

    // ---------- (c) the same at + do + id twice ----------

    public static TheoryData<string, string, int> AllActionsPerOpening()
    {
        var rows = new TheoryData<string, string, int>();
        foreach ((string faction, int k) in Openings().Select(r => ((string)r[0], (int)r[1])))
            foreach (string action in new[] { "train", "build", "research", "expand" }) rows.Add(faction, action, k);
        return rows;
    }

    [Theory]
    [MemberData(nameof(AllActionsPerOpening))]
    public void C_AStepWrittenTwice_IsRefusedAtTheSecond(string faction, string action, int k)
    {
        int i = StepIndex(faction, k, action), n = -1;
        DataLoadResult r = LoadWithAi(faction, root =>
        {
            JsonNode copy = JsonNode.Parse(Steps(root, k)[i]!.ToJsonString())!;
            if (action == "train") copy["count"] = 1; // another count is still the same step
            Steps(root, k).Add(copy);
            n = Steps(root, k).Count - 1;
        });
        AssertRefused(r, faction, $"openings[{k}].buildOrder[{n}]", $"the same step as openings[{k}].buildOrder[{i}]");
    }

    // ---------- (d) a pop off the half-pop grid ----------

    [Theory]
    [MemberData(nameof(Openings))]
    public void D_APopNotAMultipleOfAHalf_IsRefusedAtItsField(string faction, int k)
    {
        JsonObject shipped = Shipped(faction);
        int i = Enumerable.Range(0, Steps(shipped, k).Count).First(j => Steps(shipped, k)[j]!["at"]!["pop"] != null);
        double pop = (double)Steps(shipped, k)[i]!["at"]!["pop"]!;
        AssertRefused(LoadWithAi(faction, root => Steps(root, k)[i]!["at"]!["pop"] = pop + 0.25), faction, $"openings[{k}].buildOrder[{i}].at.pop",
            $"{Num(pop + 0.25)} is not a multiple of 0.5");
        double army = Opening(faction, k).AttackMinHalfPop / 2.0 + 0.25;
        AssertRefused(LoadWithAi(faction, root => AiScenes.Opening(root, k)["attack"]!["minArmyPop"] = army), faction, $"openings[{k}].attack.minArmyPop",
            $"{Num(army)} is not a multiple of 0.5");
        // The control: half a pop is on the grid.
        Assert.True(LoadWithAi(faction, root => Steps(root, k)[i]!["at"]!["pop"] = pop + 0.5).Ok);
    }

    // ---------- (e) a research no step can act on ----------

    /// <summary>
    /// "A tech no building of the faction researches" can't be written today: every common or own tech's slot is filled by
    /// one of the faction's buildings (pinned here). The reachable form is a research whose building no step places: the
    /// loader takes it (pinned: it has no build-order reachability rule), <c>AiContentTests.D</c> catches it in the shipped
    /// file, this oracle names it, and at run time the step waits and sends nothing. D21: the edited opening is made the
    /// file's only one, so the AI seat plays it whatever the seed picks.
    /// </summary>
    [Theory]
    [MemberData(nameof(Openings))]
    public void E_AResearchWhoseBuildingNoStepPlaces_FailsByName_AndWaitsAtRunTime(string faction, int k)
    {
        int f = Data.FindFaction(faction);
        foreach (TechDef t in Data.Techs.Where(t => t.Faction < 0 || t.Faction == f))
            Assert.True(Data.Buildings.Any(b => b.Faction == f && b.Slot == t.ResearchedAtSlot), $"{faction}: no building researches {t.Key}");

        // The order's first research outside the hall (its Forge upgrade) loses the step that places its building.
        AiOpening o = Opening(faction, k);
        AiStep pick = o.BuildOrder.First(s => s.Action == AiStepAction.Research && Data.Techs[s.Target].ResearchedAtSlot != BuildingSlot.TownHall);
        string tech = pick.TargetKey;
        int research = StepIndex(faction, k, "research", tech);
        BuildingSlot slot = Data.Techs[pick.Target].ResearchedAtSlot;
        int forge = o.BuildOrder.ToList().FindIndex(s => s.Action == AiStepAction.Build && Data.Buildings[s.Target].Slot == slot);
        Assert.True(forge >= 0 && forge < research, $"{faction} {o.Id}: the order places no {slot} before step {research + 1}");
        GameData d = AiScenes.DataWithAi(faction, root =>
        {
            JsonNode only = JsonNode.Parse(AiScenes.Opening(root, k).ToJsonString())!;
            only["buildOrder"]!.AsArray().RemoveAt(forge);
            root["openings"] = new JsonArray(only);
        });
        int at = research - 1; // the step after the removed one moved up
        Assert.Contains($"{faction} {o.Id} step {at + 1} ({tech}): no step places a {slot}", ResearchProblems(d, faction, 0));

        int player = f;
        SeatController[] seats = { SeatController.Human, SeatController.Human };
        seats[player] = SeatController.Ai;
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, seats), d);
        Assert.Equal(0, sim.AiOpening(player));
        List<Command> log = AiScenes.Log(sim, player);
        // Reads AiPlayer internals Blackboard.StepDueTick and Blackboard.StepDone (no public-to-tests surface carries a step's state).
        int due = -1;
        for (int t = 0; t < 14_000 && (due < 0 || sim.TickNumber < due + 1200); t++) // D21: cadre_rush's first Forge research is due at 570 s
        {
            sim.Tick();
            if (due < 0 && sim.AiOf(player)!.Board.StepDueTick[at] >= 0) due = sim.TickNumber;
        }
        Assert.True(due >= 0, $"{faction} {o.Id}: the research step never came due");
        Assert.False(sim.AiOf(player)!.Board.StepDone[at]);
        Assert.DoesNotContain(log, c => c.Kind == CommandKind.Research && c.TypeId == Data.FindTech(tech));
    }

    // ---------- (f) a one-line edit of each page's build-order table ----------

    public static TheoryData<string, int, int> PageRows()
    {
        var rows = new TheoryData<string, int, int>();
        foreach ((string faction, int k) in Openings().Select(r => ((string)r[0], (int)r[1])))
            for (int n = 1; n <= Opening(faction, k).BuildOrder.Length; n++) rows.Add(faction, k, n);
        return rows;
    }

    /// <summary>Opening k's row n: its At, Do, Id and Count, each edited alone, fail <c>A_EachPage_IsItsAiFile_BothWays</c> naming the faction, the opening and step n.</summary>
    [Theory]
    [MemberData(nameof(PageRows))]
    public void F_EachBuildOrderRow_EditedAlone_FailsNamingTheStep(string faction, int k, int n)
    {
        string[] lines = PageTables.FactionLines(faction);
        AiOpening o = Opening(faction, k);
        int heading = PageTables.Heading(lines, AiContentTests.OpeningHeading(k, o), faction + ".md");
        int row = Array.FindIndex(lines, heading, l => l.StartsWith($"| {n} | ", StringComparison.Ordinal));
        Assert.True(row > heading, $"{faction}.md {o.Id}: no build-order row {n}");
        for (int cell = 2; cell <= 5; cell++) // At, Do, Id, Count (parts[0] is the empty text before the first '|')
        {
            string[] parts = lines[row].Split('|');
            string v = parts[cell].Trim();
            parts[cell] = " " + (cell switch
            {
                2 => Regex.Replace(v, @"\d+(\.\d+)?$", m => (double.Parse(m.Value, CultureInfo.InvariantCulture) + 0.5).ToString(CultureInfo.InvariantCulture)),
                3 or 4 => "x" + v,
                _ => (int.Parse(v, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture),
            }) + " ";
            string[] edited = (string[])lines.Clone();
            edited[row] = string.Join("|", parts);
            List<string> problems = AiContentTests.Problems(Data, faction, edited);
            Assert.True(problems.Any(p => p.StartsWith($"{faction} {o.Id} step {n}: page '", StringComparison.Ordinal)),
                $"{faction}.md {o.Id} row {n} cell {cell} '{v}' -> '{parts[cell].Trim()}': {string.Join("; ", problems)}");
        }
    }

    // ---------- (g) the composition tables and their sums ----------

    /// <summary>
    /// Each opening's composition table is the file's weights (every cell, edited alone, fails naming the opening and age);
    /// Age II sums to 100 (the "Composition target" percentages) and Age I to the sum the opening's note states ("weights
    /// sum to N"), Age II's weights restricted to Age I's units: Age I is a subset of the one mix, not a mix of its own.
    /// </summary>
    [Theory]
    [MemberData(nameof(Openings))]
    public void G_TheCompositionTable_IsTheFilesWeights_AndTheSumsHold(string faction, int k)
    {
        AiOpening o = Opening(faction, k);
        string who = $"{faction} {o.Id}";
        string[] lines = PageTables.FactionLines(faction);
        int heading = PageTables.Heading(lines, AiContentTests.OpeningHeading(k, o), faction + ".md");
        string section = string.Join(" ", AiContentTests.OpeningLines(lines, faction, k, o));
        Match stated = Regex.Match(section, @"weights sum\s+to (\d+)");
        Assert.True(stated.Success, $"{who}: no 'weights sum to N' line");
        Assert.Equal(float.Parse(stated.Groups[1].Value, CultureInfo.InvariantCulture), o.Compositions[0].Weights.Sum());
        Assert.Equal(100f, o.Compositions[1].Weights.Sum());

        int table = Array.FindIndex(lines, heading, l => l.StartsWith("| Unit | Age I | Age II |", StringComparison.Ordinal));
        Assert.True(table > heading, $"{who}: no composition table");
        for (int row = table + 2; lines[row].StartsWith('|'); row++)
            for (int age = 1; age <= 2; age++)
            {
                string[] parts = lines[row].Split('|');
                string v = parts[age + 1].Trim();
                parts[age + 1] = v == "—" ? " 5 " : $" {int.Parse(v, CultureInfo.InvariantCulture) + 1} ";
                string[] edited = (string[])lines.Clone();
                edited[row] = string.Join("|", parts);
                Assert.Contains(AiContentTests.Problems(Data, faction, edited), p => p.StartsWith($"{who} composition age {age}:", StringComparison.Ordinal));
            }

        // The file side: an Age I weight edited alone breaks the subset rule and the page.
        string first = Data.Units[o.Compositions[0].Units[0]].Key;
        GameData d = AiScenes.DataWithAi(faction, root => Composition(root.AsObject(), k, 1)[first] = (double)Composition(root.AsObject(), k, 1)[first]! + 1);
        Assert.Contains(CompositionProblems(d, faction, k), p => p.StartsWith($"{who} Age I {first}: weight", StringComparison.Ordinal));
        Assert.Contains(AiContentTests.Problems(d, faction, lines), p => p.StartsWith($"{who} composition age 1:", StringComparison.Ordinal));
    }

    // ---------- (h) minArmyCost from units.json ----------

    [Theory]
    [MemberData(nameof(Openings))]
    public void H_MinArmyCost_IsRecomputedFromUnitsJson(string faction, int k)
    {
        AiOpening o = Opening(faction, k);
        int expected = RecomputedMinArmyCost(TestDataDir.Shipped, faction, k);
        Assert.Null(MinArmyCostProblem(Data, TestDataDir.Shipped, faction, k));
        string page = string.Join(" ", AiContentTests.OpeningLines(PageTables.FactionLines(faction), faction, k, o));
        Match m = Regex.Match(page, @"first at [\d.]+ army pop and ([\d,]+) gold \+ wood");
        Assert.Equal(expected.ToString("#,0", CultureInfo.InvariantCulture), m.Groups[1].Value);

        // A unit of the Age I mix costing one gold more moves the recomputed number off the file's, and the check says by what.
        string unit = Data.Units[o.Compositions[0].Units[0]].Key;
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField(faction, unit, "cost.gold", (Data.Units[Data.FindUnit(unit)].CostGold + 1).ToString(CultureInfo.InvariantCulture));
        int moved = RecomputedMinArmyCost(dir.Path, faction, k);
        Assert.True(moved > expected, $"{faction} {o.Id}: {unit} +1 gold leaves the recomputed cost at {moved}");
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Assert.Equal($"{faction} {o.Id}: minArmyCost {expected} vs recomputed {moved}", MinArmyCostProblem(r.Data!, dir.Path, faction, k));
    }

    // ---------- (i) the count and step-count limits ----------

    [Theory]
    [MemberData(nameof(Openings))]
    public void I_ACountPastTheLimit_AndAStepPastTheLimit_AreRefusedAtTheirFields(string faction, int k)
    {
        int i = StepIndex(faction, k, "train");
        Assert.True(LoadWithAi(faction, root => Steps(root, k)[i]!["count"] = DataLimits.MaxAiStepCount).Ok);
        AssertRefused(LoadWithAi(faction, root => Steps(root, k)[i]!["count"] = DataLimits.MaxAiStepCount + 1),
            faction, $"openings[{k}].buildOrder[{i}].count", $"{DataLimits.MaxAiStepCount + 1} is outside 1-{DataLimits.MaxAiStepCount}");

        string house = Data.Buildings.First(b => b.Faction == Data.FindFaction(faction) && b.Slot == BuildingSlot.House).Key;
        void PadTo(JsonObject root, int count)
        {
            for (int j = 0; Steps(root, k).Count < count; j++)
                Steps(root, k).Add(JsonNode.Parse($$"""{ "at": { "time": {{1000 + j}} }, "do": "build", "id": "{{house}}" }"""));
        }
        Assert.True(LoadWithAi(faction, root => PadTo(root, DataLimits.MaxAiBuildOrderSteps)).Ok);
        AssertRefused(LoadWithAi(faction, root => PadTo(root, DataLimits.MaxAiBuildOrderSteps + 1)),
            faction, $"openings[{k}].buildOrder", $"{DataLimits.MaxAiBuildOrderSteps + 1} steps is above the maximum {DataLimits.MaxAiBuildOrderSteps}");
    }

    // ---------- (j) D21: a step moved before its requirement ----------

    /// <summary>
    /// The QA focus's dead end, per opening: the opening's first <c>build</c> step whose building needs Age II (a Shrine,
    /// a Cadre Tower, an Engineers' Yard) moved to the front of the order loads (the loader has no order rule) and fails
    /// <see cref="OrderProblems"/> naming it and Age II; so does the first step whose building needs another building
    /// (Horse Lines or the Corral) moved to the front.
    /// </summary>
    [Theory]
    [MemberData(nameof(Openings))]
    public void J_AStepMovedBeforeItsRequirement_FailsNamingIt(string faction, int k)
    {
        AiOpening o = Opening(faction, k);
        foreach (Func<BuildingDef, bool> needs in new Func<BuildingDef, bool>[] { b => b.RequiresTechs.Length > 0, b => b.RequiresBuildings.Length > 0 })
        {
            int i = o.BuildOrder.ToList().FindIndex(s => s.Action == AiStepAction.Build && needs(Data.Buildings[s.Target]));
            if (i < 0) continue; // e.g. holy_war places no Horse Lines
            AiStep s = o.BuildOrder[i];
            GameData d = AiScenes.DataWithAi(faction, root =>
            {
                JsonArray steps = Steps(root.AsObject(), k);
                JsonNode moved = steps[i]!;
                steps.RemoveAt(i);
                steps.Insert(0, moved);
            });
            Assert.Contains(OrderProblems(d, faction, k), p => p.StartsWith($"{faction} {o.Id} step 1 ({s.TargetKey}): needs ", StringComparison.Ordinal));
        }
    }
}
