using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-1: the <c>ai.json</c> schema (docs/03 "AI architecture", "For the data track (M5-1)"). The shipped files load with
/// docs/02's difficulty numbers; every rule the loader enforces is one error at its field, hostile files included; every
/// error comes out at once; and the file stays out of <see cref="GameData.ContentHash"/> until the AI sends commands (M5-2).
/// </summary>
public class AiLoaderTests
{
    private const string MalazanAi = "factions/malazan/ai.json";

    [Fact]
    public void BothShippedFactions_HaveAnAiDef_WithDocs02sDifficultyNumbers()
    {
        GameData d = TestSim.Data;
        Assert.Equal(d.Factions.Length, d.Ai.Length);
        for (int f = 0; f < d.Factions.Length; f++)
        {
            AiDef ai = d.Ai[f];
            Assert.Equal(f, ai.Faction);
            Assert.Equal(3, ai.Difficulties.Length);
            // docs/02 "AI opponent": Easy thinks every 2 s, Hard every 0.5 s with a 25 % gather bonus, focus fire, pull-back.
            Assert.Equal(40, ai.Difficulty(AiDifficulty.Easy).ThinkTicks);
            Assert.Equal(10, ai.Difficulty(AiDifficulty.Hard).ThinkTicks);
            Assert.Equal(0.25f, ai.Difficulty(AiDifficulty.Hard).GatherBonus);
            Assert.Equal(0f, ai.Difficulty(AiDifficulty.Easy).GatherBonus);
            Assert.Equal(0f, ai.Difficulty(AiDifficulty.Normal).GatherBonus);
            Assert.True(ai.Difficulty(AiDifficulty.Hard).FocusFire && ai.Difficulty(AiDifficulty.Hard).PullBackWounded);
            Assert.False(ai.Difficulty(AiDifficulty.Easy).FocusFire || ai.Difficulty(AiDifficulty.Easy).PullBackWounded);
            Assert.True(ai.Difficulty(AiDifficulty.Normal).UseAbilities);
            // At least one real step, naming one of the faction's own types, and one composition per age.
            Assert.NotEmpty(ai.BuildOrder);
            foreach (AiStep step in ai.BuildOrder)
            {
                if (step.Action == AiStepAction.Expand)
                {
                    Assert.Equal(-1, step.Target); // an expand names no type
                    continue;
                }
                Assert.True(step.Target >= 0);
                int owner = step.Action switch
                {
                    AiStepAction.Train => d.Units[step.Target].Faction,
                    AiStepAction.Build => d.Buildings[step.Target].Faction,
                    _ => d.Techs[step.Target].Faction < 0 ? f : d.Techs[step.Target].Faction,
                };
                Assert.Equal(f, owner);
            }
            Assert.Equal(new[] { 1, 2 }, ai.Compositions.Select(c => c.Age));
            foreach (AiComposition c in ai.Compositions)
            {
                Assert.NotEmpty(c.Units);
                Assert.Equal(c.Units.Length, c.Weights.Length);
                Assert.Equal(c.Units.OrderBy(x => x), c.Units);
                Assert.All(c.Units, u => Assert.Equal(f, d.Units[u].Faction));
                Assert.All(c.Weights, x => Assert.True(x > 0));
            }
        }
        // The steps, counts and thresholds themselves are content (the data track's, D14; BUG-0571): only the shape is pinned
        // here. Every shipped order opens by training its faction's worker at once.
        AiStep first = d.Ai[d.FindFaction("malazan")].BuildOrder[0];
        Assert.Equal((AiStepTrigger.Time, 0, AiStepAction.Train, d.FindUnit("malazan_laborer"), "malazan_laborer"),
            (first.Trigger, first.At, first.Action, first.Target, first.TargetKey));
        Assert.True(first.Count >= 1);
        Assert.All(d.Ai, ai => Assert.True(ai.AttackMinHalfPop > 0));
    }

    /// <summary>Every step kind and trigger resolves, seconds become ticks and pop half-pop.</summary>
    [Fact]
    public void EveryTriggerAndAction_Resolves()
    {
        GameData d = LoadWith(root => root["buildOrder"] = JsonNode.Parse("""
            [
              { "at": { "time": 12.5 }, "do": "build", "id": "malazan_billet", "count": 2 },
              { "at": { "workers": 8 }, "do": "train", "id": "malazan_laborer", "count": 4 },
              { "at": { "pop": 12.5 }, "do": "research", "id": "age_ii" },
              { "at": { "pop": 20 }, "do": "research", "id": "moranth_supply" },
              { "at": { "time": 300 }, "do": "expand" }
            ]
            """));
        var steps = d.Ai[d.FindFaction("malazan")].BuildOrder;
        Assert.Equal(5, steps.Length);
        Assert.Equal((AiStepTrigger.Time, 250, AiStepAction.Build, d.FindBuilding("malazan_billet"), 2), (steps[0].Trigger, steps[0].At, steps[0].Action, steps[0].Target, steps[0].Count));
        Assert.Equal((AiStepTrigger.Workers, 8, AiStepAction.Train, d.FindUnit("malazan_laborer"), 4), (steps[1].Trigger, steps[1].At, steps[1].Action, steps[1].Target, steps[1].Count));
        Assert.Equal((AiStepTrigger.Pop, 25, AiStepAction.Research, d.FindTech("age_ii"), 1), (steps[2].Trigger, steps[2].At, steps[2].Action, steps[2].Target, steps[2].Count));
        Assert.Equal(d.FindTech("moranth_supply"), steps[3].Target);
        Assert.Equal((AiStepTrigger.Time, 6000, AiStepAction.Expand, -1, "", 1), (steps[4].Trigger, steps[4].At, steps[4].Action, steps[4].Target, steps[4].TargetKey, steps[4].Count));
    }

    /// <summary>One error per fault, at the field named, with what the loader says.</summary>
    [Theory]
    [InlineData("buildOrder[0].do", "\"fortify\"", "buildOrder[0].do", "unknown action 'fortify' (expected one of build, train, research, expand)")]
    [InlineData("buildOrder[0].do", "\"build\"", "buildOrder[0].id", "unknown building 'malazan_laborer'")]
    [InlineData("buildOrder[0].id", "\"whirlwind_tent\"", "buildOrder[0].id", "unknown unit 'whirlwind_tent'")]
    [InlineData("buildOrder[0].id", "\"Malazan_Laborer\"", "buildOrder[0].id", "not snake_case")]
    [InlineData("buildOrder[0].id", null, "buildOrder[0].id", "missing required text")]
    [InlineData("buildOrder[0].count", "0", "buildOrder[0].count", "0 is outside 1-200")]
    [InlineData("buildOrder[0].count", "1000000000", "buildOrder[0].count", "outside 1-200")]
    [InlineData("buildOrder[0].count", "2.5", "$.buildOrder[0].count", "expected a whole number")]
    [InlineData("buildOrder[0].at", "{}", "buildOrder[0].at", "set exactly one of time, workers, pop (0 set)")]
    [InlineData("buildOrder[0].at", "{ \"time\": 5, \"workers\": 3 }", "buildOrder[0].at", "(2 set)")]
    [InlineData("buildOrder[0].at", "{ \"time\": -5 }", "buildOrder[0].at.time", "must not be negative")]
    [InlineData("buildOrder[0].at", "{ \"time\": 7200 }", "buildOrder[0].at.time", "above the maximum 3600 s")]
    [InlineData("buildOrder[0].at", "{ \"pop\": 2.25 }", "buildOrder[0].at.pop", "not a multiple of 0.5")]
    [InlineData("buildOrder[0].at", "{ \"workers\": -1 }", "buildOrder[0].at.workers", "below the minimum 0")]
    [InlineData("buildOrder[0].at", "{ \"tick\": 3 }", "$.buildOrder[0].at.tick", "")]
    [InlineData("buildOrder[0].at", null, "buildOrder[0].at", "missing required field")]
    [InlineData("buildOrder", "[]", "buildOrder", "no steps")]
    [InlineData("buildOrder", null, "buildOrder", "missing required field")]
    [InlineData("difficulties.normal.thinkInterval", "0.01", "difficulties.normal.thinkInterval", "rounds to 0 ticks")]
    [InlineData("difficulties.normal.thinkInterval", "0", "difficulties.normal.thinkInterval", "must be positive")]
    [InlineData("difficulties.hard.gatherBonus", "1.5", "difficulties.hard.gatherBonus", "outside 0-1")]
    [InlineData("difficulties.hard.gatherBonus", "-0.25", "difficulties.hard.gatherBonus", "outside 0-1")]
    [InlineData("difficulties.easy.waveSizeMultiplier", "0", "difficulties.easy.waveSizeMultiplier", "must be positive")]
    [InlineData("difficulties.easy.waveSizeMultiplier", "11", "difficulties.easy.waveSizeMultiplier", "above the maximum 10")]
    [InlineData("difficulties.easy.buildOrderSlack", "-3", "difficulties.easy.buildOrderSlack", "must not be negative")]
    [InlineData("difficulties.easy.expandAfter", "1e300", "difficulties.easy.expandAfter", "above the maximum")]
    [InlineData("difficulties.easy.focusFire", null, "difficulties.easy.focusFire", "missing required field")]
    [InlineData("difficulties.easy.focusFire", "1", "$.difficulties.easy.focusFire", "expected true or false")]
    [InlineData("difficulties.insane", "{}", "$.difficulties.insane", "")]
    [InlineData("difficulties", null, "difficulties", "missing required field")]
    [InlineData("attack.minArmyPop", "-2", "attack.minArmyPop", "must not be negative")]
    [InlineData("attack.minArmyPop", "7.3", "attack.minArmyPop", "not a multiple of 0.5")]
    [InlineData("attack.minArmyCost", "-1", "attack.minArmyCost", "below the minimum 0")]
    [InlineData("attack", null, "attack", "missing required field")]
    [InlineData("retreat.hpFraction", "1.01", "retreat.hpFraction", "outside 0-1")]
    [InlineData("retreat.lossFraction", null, "retreat.lossFraction", "missing required field")]
    [InlineData("compositions[0].units", "{}", "compositions[0].units", "no units (at least one)")]
    [InlineData("compositions[0].units", "{ \"malazan_heavy_infantry\": 0 }", "compositions[0].units.malazan_heavy_infantry", "must be positive")]
    [InlineData("compositions[0].units", "{ \"malazan_heavy_infantry\": -1 }", "compositions[0].units.malazan_heavy_infantry", "must be positive")]
    [InlineData("compositions[0].units", "{ \"whirlwind_raider\": 1 }", "compositions[0].units.whirlwind_raider", "belongs to faction 'whirlwind', not 'malazan'")]
    [InlineData("compositions[0].units", "{ \"malazan_ghost\": 1 }", "compositions[0].units.malazan_ghost", "unknown unit 'malazan_ghost'")]
    [InlineData("compositions[0].units", "{ \"Malazan\": 1 }", "compositions[0].units.Malazan", "not snake_case")]
    [InlineData("compositions[0].age", "3", "compositions[0].age", "above the last age, 2")]
    [InlineData("compositions[0].age", "0", "compositions[0].age", "below the minimum 1")]
    [InlineData("compositions[1].age", "1", "compositions[1].age", "a second composition for Age 1")]
    [InlineData("compositions", "[]", "compositions", "no composition for Age 1")]
    public void ABrokenField_IsOneErrorAtIt(string field, string? rawJson, string path, string message)
    {
        IReadOnlyList<DataError> errors = ErrorsWith(root => Set(root, field, rawJson));
        // An empty compositions list misses both ages: one error each, both at the list.
        if (field == "compositions") Assert.Equal(2, errors.Count);
        else Assert.True(errors.Count == 1, string.Join("\n", errors));
        DataError e = errors[0];
        Assert.Equal(MalazanAi, e.File);
        Assert.Equal(path, e.Path);
        Assert.Contains(message, e.Message);
    }

    [Theory]
    [InlineData("{ \"at\": { \"time\": 0 }, \"do\": \"expand\", \"id\": \"malazan_billet\" }", "buildOrder[0].id", "an expand step names no id")]
    [InlineData("{ \"at\": { \"time\": 0 }, \"do\": \"research\", \"id\": \"age_ii\", \"count\": 1 }", "buildOrder[0].count", "a research step takes no count")]
    [InlineData("{ \"at\": { \"time\": 0 }, \"do\": \"research\", \"id\": \"dryjhnas_prophecy\" }", "buildOrder[0].id", "tech 'dryjhnas_prophecy' belongs to faction 'whirlwind', not 'malazan'")]
    [InlineData("{ \"at\": { \"time\": 0 }, \"do\": \"research\", \"id\": \"malazan_laborer\" }", "buildOrder[0].id", "unknown tech 'malazan_laborer'")]
    [InlineData("{ \"at\": { \"time\": 0 }, \"do\": \"build\", \"id\": \"whirlwind_tent\" }", "buildOrder[0].id", "building 'whirlwind_tent' belongs to faction 'whirlwind', not 'malazan'")]
    [InlineData("{ \"at\": { \"time\": 0 }, \"do\": \"train\", \"id\": \"malazan_laborer\", \"cuont\": 2 }", "$.buildOrder[0].cuont", "")]
    [InlineData("null", "buildOrder[0]", "missing required field")]
    public void ABrokenStep_IsOneErrorAtIt(string step, string path, string message)
    {
        IReadOnlyList<DataError> errors = ErrorsWith(root => root["buildOrder"] = new JsonArray(JsonNode.Parse(step)));
        DataError e = Assert.Single(errors);
        Assert.Equal(path, e.Path);
        Assert.Contains(message, e.Message);
    }

    /// <summary>Two steps differing only in count are one step written twice; the same at with another do or id is not.</summary>
    [Fact]
    public void ADuplicateStep_IsTheSameAtDoAndId_WhateverTheCount()
    {
        DataError e = Assert.Single(ErrorsWith(root => root["buildOrder"] = JsonNode.Parse("""
            [
              { "at": { "workers": 4 }, "do": "train", "id": "malazan_laborer", "count": 1 },
              { "at": { "workers": 4 }, "do": "train", "id": "malazan_heavy_infantry" },
              { "at": { "workers": 5 }, "do": "train", "id": "malazan_laborer" },
              { "at": { "pop": 4 }, "do": "train", "id": "malazan_laborer" },
              { "at": { "workers": 4 }, "do": "train", "id": "malazan_laborer", "count": 3 }
            ]
            """)));
        Assert.Equal("buildOrder[4]", e.Path);
        Assert.Contains("the same step as buildOrder[0]", e.Message);
    }

    /// <summary>Hostile files: 10,000 steps is one error; NaN (not JSON) is one parse error; nothing throws.</summary>
    [Fact]
    public void HostileFiles_AreReportedNotThrown()
    {
        DataError many = Assert.Single(ErrorsWith(root =>
        {
            var steps = new JsonArray();
            for (int i = 0; i < 10_000; i++) steps.Add(JsonNode.Parse($$"""{ "at": { "time": {{i}} }, "do": "expand" }"""));
            root["buildOrder"] = steps;
        }));
        Assert.Equal("buildOrder", many.Path);
        Assert.Contains("10000 steps is above the maximum 256", many.Message);

        using TestDataDir dir = TestDataDir.CopyOfShipped();
        string text = File.ReadAllText(dir.FullPath(MalazanAi)).Replace("\"thinkInterval\": 2,", "\"thinkInterval\": NaN,");
        File.WriteAllText(dir.FullPath(MalazanAi), text);
        DataError nan = Assert.Single(DataLoader.LoadAll(dir.Path).Errors);
        Assert.Equal(MalazanAi, nan.File);
        Assert.Contains("malformed JSON", nan.Message);
    }

    /// <summary>Faults in both factions' files and in every block come out in one load.</summary>
    [Fact]
    public void EveryError_ComesOutAtOnce()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(MalazanAi, root =>
        {
            root["difficulties"]!["easy"]!["thinkInterval"] = -1;
            root["buildOrder"]![0]!["id"] = "whirlwind_raider";
            root["compositions"]![1]!["units"] = new JsonObject();
            root["retreat"]!["hpFraction"] = 2;
        });
        dir.EditJson("factions/whirlwind/ai.json", root => root["attack"]!["minArmyCost"] = -5);
        IReadOnlyList<DataError> errors = DataLoader.LoadAll(dir.Path).Errors;
        Assert.Equal(new[]
        {
            (MalazanAi, "difficulties.easy.thinkInterval"), (MalazanAi, "buildOrder[0].id"), (MalazanAi, "compositions[1].units"),
            (MalazanAi, "retreat.hpFraction"), ("factions/whirlwind/ai.json", "attack.minArmyCost"),
        }, errors.Select(e => (e.File, e.Path)).OrderBy(x => x.File, StringComparer.Ordinal).ThenBy(x => Order(x.Path)));
    }

    private static int Order(string path) =>
        path.StartsWith("difficulties") ? 0 : path.StartsWith("buildOrder") ? 1 : path.StartsWith("compositions") ? 2 : path.StartsWith("attack") ? 3 : 4;

    /// <summary>
    /// A broken units, buildings or techs file is reported once, not again through every ai.json entry naming its ids: a
    /// missing units file, and one with the Crossbowman (named by the shipped compositions) taken out.
    /// </summary>
    [Fact]
    public void AiIds_NotChecked_WhenTheFilesTheyNameAreBroken()
    {
        using (TestDataDir dir = TestDataDir.CopyOfShipped())
        {
            File.Delete(dir.FullPath("factions/malazan/units.json"));
            DataError e = Assert.Single(DataLoader.LoadAll(dir.Path).Errors);
            Assert.Equal("factions/malazan/units.json", e.File);
        }
        using (TestDataDir dir = TestDataDir.CopyOfShipped())
        {
            dir.EditJson("factions/malazan/units.json", root =>
            {
                JsonArray units = root["units"]!.AsArray();
                units.Remove(units.Single(u => (string)u!["id"]! == "malazan_crossbowman"));
            });
            IReadOnlyList<DataError> errors = DataLoader.LoadAll(dir.Path).Errors;
            Assert.DoesNotContain(errors, x => x.File.EndsWith("ai.json", StringComparison.Ordinal));
        }
    }

    /// <summary>The Producer's M5-1 decision: ai.json is not in the content hash until the AI sends commands (M5-2 adds it, with one golden regen).</summary>
    [Fact]
    public void AnAiJsonEdit_LeavesTheContentHash()
    {
        GameData edited = LoadWith(root =>
        {
            root["difficulties"]!["hard"]!["gatherBonus"] = 0.5;
            root["buildOrder"]![0]!["count"] = 3;
        });
        Assert.Equal(0.5f, edited.Ai[edited.FindFaction("malazan")].Difficulty(AiDifficulty.Hard).GatherBonus);
        Assert.Equal(TestSim.Data.ContentHash(), edited.ContentHash());
    }

    private static GameData LoadWith(Action<JsonObject> edit)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(MalazanAi, edit);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        return r.Data!;
    }

    private static IReadOnlyList<DataError> ErrorsWith(Action<JsonObject> edit)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(MalazanAi, edit);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.Null(r.Data);
        return r.Errors;
    }

    /// <summary>Sets (or with null removes) a dotted path; <c>name[i]</c> indexes a list.</summary>
    private static void Set(JsonObject root, string dotted, string? rawJson)
    {
        string[] parts = dotted.Split('.');
        JsonNode node = root;
        for (int i = 0; i < parts.Length - 1; i++) node = Step(node, parts[i]);
        string last = parts[^1];
        int bracket = last.IndexOf('[');
        if (bracket >= 0)
        {
            JsonArray list = node[last[..bracket]]!.AsArray();
            int index = int.Parse(last[(bracket + 1)..^1]);
            if (rawJson == null) list.RemoveAt(index); else list[index] = JsonNode.Parse(rawJson);
            return;
        }
        if (rawJson == null) node.AsObject().Remove(last);
        else node[last] = JsonNode.Parse(rawJson);
    }

    private static JsonNode Step(JsonNode node, string part)
    {
        int bracket = part.IndexOf('[');
        if (bracket < 0) return node[part]!;
        return node[part[..bracket]]![int.Parse(part[(bracket + 1)..^1])]!;
    }
}
