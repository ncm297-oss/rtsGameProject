using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D37 (BUG-1612): the three per-difficulty fixtures, ready for D38 (the sim's Request 59). Each of
/// <c>Fixtures/teblor_standard_d37_easy.json</c>, <c>_normal.json</c> and <c>_hard.json</c> is the whole Teblor
/// <c>standard</c> opening as a plain opening (today's schema: one <c>buildOrder</c>, no <c>buildOrders</c> field) whose
/// steps are the order <see cref="D37TeblorNormalOrderReportTests"/> measured on that difficulty, with every other field the
/// shipped opening's. A row loads it through the shipped loader on a copy of the shipped data (in place of the shipped
/// opening), checks the loaded steps are the file's, and checks the tooltip (D34's description, unchanged) is still true of
/// the order: the claims <c>QA/Content/D32DescriptionQaTests</c> reads on the shipped opening, read here on the fixture.
/// </summary>
public class D37TeblorFixtureTests
{
    /// <summary>The fixture of <paramref name="difficulty"/> (easy, normal, hard).</summary>
    internal static JsonObject Fixture(string difficulty) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.RepoRoot(), "sim", "Rts.Sim.Tests", "Content", "Fixtures", $"teblor_standard_d37_{difficulty}.json")))!.AsObject();

    /// <summary>The order each fixture holds, by its name in the report class.</summary>
    internal static string OrderOf(string difficulty) => difficulty switch
    {
        "easy" => D37TeblorNormalOrderReportTests.EasyOrder,
        "normal" => D37TeblorNormalOrderReportTests.NormalOrder,
        "hard" => D37TeblorNormalOrderReportTests.HardOrder,
        _ => throw new ArgumentOutOfRangeException(nameof(difficulty)),
    };

    [Theory]
    [InlineData("easy")]
    [InlineData("normal")]
    [InlineData("hard")]
    public void TheFixture_LoadsOnAShippedCopy_IsTheMeasuredOrder_AndItsTooltipIsTrue(string difficulty)
    {
        JsonObject fixture = Fixture(difficulty);
        JsonObject measured = D37TeblorNormalOrderReportTests.AllVariants().Single(v => v.Name == OrderOf(difficulty)).Opening;
        List<string> problems = Problems(fixture, measured);
        Assert.True(problems.Count == 0, $"{difficulty} ({OrderOf(difficulty)}): " + string.Join("; ", problems));
    }

    /// <summary>The check can fail, by name: a step moved, a switch on the expand step, a <c>buildOrders</c> field, a new description, a kind's train step taken out.</summary>
    [Fact]
    public void TheCheck_NamesEachWayAFixtureCanBeWrong()
    {
        JsonObject measured = D37TeblorNormalOrderReportTests.AllVariants().Single(v => v.Name == OrderOf("hard")).Opening;
        JsonObject Mutant(Action<JsonObject> edit)
        {
            JsonObject c = Fixture("hard");
            edit(c);
            return c;
        }
        static JsonObject StepOf(JsonObject o, string what, string? id) =>
            o["buildOrder"]!.AsArray().Select(s => s!.AsObject()).First(s => (string)s["do"]! == what && (string?)s["id"] == id);

        Assert.Empty(Problems(Fixture("hard"), measured));
        Assert.Contains("buildOrder differs from the measured order", Problems(Mutant(c => StepOf(c, "build", "teblor_bloodwood_carver")["at"] = new JsonObject { ["time"] = 540 }), measured));
        Assert.Contains("noReserve on the expand step", Problems(Mutant(c => StepOf(c, "expand", null)["noReserve"] = true), measured));
        Assert.Contains("a buildOrders field", Problems(Mutant(c => c["buildOrders"] = new JsonObject()), measured));
        Assert.Contains("description differs from the shipped opening's", Problems(Mutant(c => c["description"] = "War Lodge first."), measured));
        Assert.Contains("the tooltip names Breakers but the order has no train step for teblor_breaker",
            Problems(Mutant(c => c["buildOrder"]!.AsArray().Remove(StepOf(c, "train", "teblor_breaker"))), measured));
    }

    /// <summary>Every way <paramref name="fixture"/> is not a loadable, measured, truthfully described plain opening.</summary>
    internal static List<string> Problems(JsonObject fixture, JsonObject measured)
    {
        var problems = new List<string>();
        JsonObject shipped = TeblorOpeningVariantsReportTests.Shipped();
        JsonArray steps = fixture["buildOrder"]!.AsArray();

        // The measured order, step for step; every other field the shipped opening's (so a plain opening, no new field).
        if (!JsonNode.DeepEquals(fixture["buildOrder"], measured["buildOrder"])) problems.Add("buildOrder differs from the measured order");
        foreach (string field in new[] { "id", "displayName", "description", "compositions", "attack" })
            if (!JsonNode.DeepEquals(fixture[field], shipped[field])) problems.Add($"{field} differs from the shipped opening's");
        if (fixture["buildOrders"] != null) problems.Add("a buildOrders field");
        if (fixture.Count != shipped.Count) problems.Add($"{fixture.Count} fields, the shipped opening {shipped.Count}");
        if (steps.Any(s => (string)s!["do"]! == "expand" && s["noReserve"] != null)) problems.Add("noReserve on the expand step");
        if (steps.Any(s => s!["noReserve"] != null)) problems.Add("noReserve on a step (left out of Teblor orders: docs/01 2026-11-26 (4))");

        // Through the shipped loader, on a copy of the shipped data whose standard opening is the fixture.
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/teblor/ai.json", root => root["openings"]![0] = JsonNode.Parse(fixture.ToJsonString()));
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        if (r.Errors.Count > 0 || r.Data == null)
        {
            problems.Add("load errors: " + string.Join("; ", r.Errors));
            return problems;
        }
        GameData d = r.Data;
        AiOpening o = d.Ai[d.FindFaction("teblor")].Openings[0];
        if (o.Id != "standard" || o.Description != (string)fixture["description"]!) problems.Add("the loaded opening is not the fixture's standard");
        if (o.BuildOrder.Length != steps.Count) problems.Add($"{o.BuildOrder.Length} steps loaded of {steps.Count}");
        else
            for (int i = 0; i < steps.Count; i++)
            {
                AiStep s = o.BuildOrder[i];
                JsonNode j = steps[i]!;
                string want = $"{(string)j["do"]!} {(string?)j["id"]} x{(int?)j["count"] ?? 1} through {(bool?)j["throughReserve"] ?? false}";
                string got = $"{s.Action.ToString().ToLowerInvariant()} {(s.Action == AiStepAction.Expand ? null : s.TargetKey)} x{(s.Action == AiStepAction.Train ? s.Count : 1)} through {s.ThroughReserve}";
                if (want != got) problems.Add($"step {i} loads as '{got}', the file says '{want}'");
            }

        // The tooltip, claim by claim (D32DescriptionQaTests' reading, on the fixture's order).
        string desc = o.Description;
        if (desc != D34TeblorStepOrderReportTests.Description) problems.Add("the tooltip is not D34's");
        int Index(AiStepAction a, string key)
        {
            for (int i = 0; i < o.BuildOrder.Length; i++)
                if (o.BuildOrder[i].Action == a && (a == AiStepAction.Expand || o.BuildOrder[i].TargetKey == key)) return i;
            return -1;
        }
        int age = Index(AiStepAction.Research, "age_ii");
        foreach (string b in new[] { "teblor_war_lodge", "teblor_javelin_ground" })
        {
            int i = Index(AiStepAction.Build, b);
            if (!(i >= 0 && i < age && o.BuildOrder[i].Trigger == AiStepTrigger.Pop)) problems.Add($"the tooltip says {b} first, then Age II");
        }
        AiComposition ageOne = o.Compositions.Single(c => c.Age == 1), ageTwo = o.Compositions.Single(c => c.Age == 2);
        int warrior = d.FindUnit("teblor_warrior");
        if (!desc.Contains("four Warriors raid at 8 army pop") || o.AttackMinHalfPop != 16 || !ageOne.Units.SequenceEqual(new[] { warrior })
            || o.AttackMinHalfPop / d.Units[warrior].HalfPop != 4)
            problems.Add("the tooltip's raid (four Warriors at 8 army pop) is not the wave");
        foreach (var (words, unit, trainer, ownStep) in new[]
        {
            ("Javelin Throwers", "teblor_javelin_thrower", "teblor_javelin_ground", false),
            ("War-dogs", "teblor_war_dog", "teblor_stables", true),
            ("Shamans", "teblor_shaman", "teblor_shamans_circle", true),
            ("Breakers", "teblor_breaker", "teblor_breakers_hall", true),
            ("Destrier Riders", "teblor_destrier_rider", "teblor_stables", true),
        })
        {
            if (!desc.Contains(words)) problems.Add($"the tooltip no longer names {words}");
            int k = ageTwo.Units.IndexOf(d.FindUnit(unit));
            if (k < 0 || ageTwo.Weights[k] <= 0) problems.Add($"the tooltip names {words} but {unit} is not in the Age II mix");
            int build = Index(AiStepAction.Build, trainer);
            if (build < 0) problems.Add($"the tooltip names {words} but nothing builds {trainer}");
            if (!ownStep) continue;
            int train = Index(AiStepAction.Train, unit);
            // "May follow" claims a step, not a time: a train step due before its hall waits for it (K9KC10S's Shaman at 9:00,
            // its Circle at 10:00), which D32's shipped-opening pin would refuse (a note for D38).
            if (train < 0 || o.BuildOrder[train].Count < 1) problems.Add($"the tooltip names {words} but the order has no train step for {unit}");
        }
        int expand = Index(AiStepAction.Expand, "");
        if (!desc.Contains("a second Longhouse may follow") || expand <= age) problems.Add("the tooltip's second Longhouse has no expand step after Age II");
        return problems;
    }
}
