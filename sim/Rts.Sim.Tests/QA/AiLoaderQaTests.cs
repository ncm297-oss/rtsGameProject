using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>
/// QA (M5-1, session 2026-10-11-0314): hostile <c>ai.json</c> files beyond the developer's rows. Every row loads a copy of the
/// shipped data with one fault and asserts the loader reports it (never throws, never silently defaults): raw-text faults
/// (repeated keys, NaN / Infinity literals, a non-object root), numbers that pass the double checks but break once narrowed
/// to the float the sim stores, and a missing file for the first faction.
/// </summary>
public class AiLoaderQaTests
{
    private const string MalazanAi = "factions/malazan/ai.json";

    private static IReadOnlyList<DataError> ErrorsOfText(Func<string, string> edit, string rel = MalazanAi)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        string path = dir.FullPath(rel);
        File.WriteAllText(path, edit(File.ReadAllText(path)));
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        if (r.Errors.Count > 0) Assert.Null(r.Data);
        return r.Errors;
    }

    private static DataLoadResult LoadWith(Action<JsonObject> edit)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(MalazanAi, edit);
        return DataLoader.LoadAll(dir.Path);
    }

    /// <summary>docs/03 "For the data track (M5-1)": "Unknown fields and repeated keys are errors, as everywhere".</summary>
    [Theory]
    [InlineData("\"easy\": {", "\"easy\": { \"thinkInterval\": 3, ")] // a repeated key inside one difficulty
    [InlineData("{ \"age\": 1, \"units\": { ", "{ \"age\": 1, \"units\": { \"malazan_heavy_infantry\": 7, ")] // a repeated unit in a composition map (any shipped Age I mix has the heavy infantry; BUG-0571)
    [InlineData("\"attack\":", "\"retreat\": { \"hpFraction\": 0.9, \"lossFraction\": 0.9 }, \"attack\":")] // a repeated top-level block
    public void ARepeatedKey_IsAnError(string find, string replace)
    {
        IReadOnlyList<DataError> errors = ErrorsOfText(t =>
        {
            Assert.Contains(find, t);
            return ReplaceFirst(t, find, replace);
        });
        Assert.NotEmpty(errors);
        Assert.All(errors, e => Assert.Equal(MalazanAi, e.File));
    }

    /// <summary>The first value of <paramref name="key"/> in the file, whatever the shipped content writes there (BUG-0571), replaced by the raw literal <paramref name="literal"/>.</summary>
    [Theory]
    [InlineData("gatherBonus", "NaN")]
    [InlineData("gatherBonus", "Infinity")]
    [InlineData("gatherBonus", "\"NaN\"")]
    [InlineData("hpFraction", "-Infinity")]
    [InlineData("minArmyCost", "1e400")]
    [InlineData("count", "3000000000")]
    public void NonFiniteAndOverflowingLiterals_AreOneErrorEach_NeverAThrow(string key, string literal)
    {
        IReadOnlyList<DataError> errors = ErrorsOfText(t =>
        {
            Match m = Regex.Match(t, "\"" + key + "\": *([-0-9.eE+]+|true|false)");
            Assert.True(m.Success, $"no number at \"{key}\" in the shipped file");
            Group v = m.Groups[1];
            return string.Concat(t.AsSpan(0, v.Index), literal, t.AsSpan(v.Index + v.Length));
        });
        DataError e = Assert.Single(errors);
        Assert.Equal(MalazanAi, e.File);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{}")]
    public void ANonObjectOrEmptyFile_IsReported(string text)
    {
        IReadOnlyList<DataError> errors = ErrorsOfText(_ => text);
        Assert.NotEmpty(errors);
        Assert.All(errors, e => Assert.Equal(MalazanAi, e.File));
    }

    /// <summary>The first faction's file missing (the developer's row deletes the second's): one error, at that file.</summary>
    [Fact]
    public void TheFirstFactionsAiJsonMissing_IsOneError()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        File.Delete(dir.FullPath(MalazanAi));
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        DataError e = Assert.Single(r.Errors);
        Assert.Equal(MalazanAi, e.File);
        Assert.Null(r.Data);
    }

    /// <summary>
    /// A composition weight is checked as a double (above 0, at most 1e6) and stored as a float: a weight that passes the
    /// check must still be above 0 once stored (docs/03: "weights above 0"; <c>AiComposition.Weights</c>: "above 0").
    /// </summary>
    [Theory] // BUG-0541 item 1 fixed in M5-3 (a weight rounding to 0f is a load error); 1e-40 is a subnormal float above 0: it loads
    [InlineData(1e-300)]
    [InlineData(1e-46)]
    [InlineData(1e-40)]
    public void ATinyCompositionWeight_IsRefused_OrStoredAboveZero(double weight)
    {
        DataLoadResult r = LoadWith(root => AiScenes.Opening(root)["compositions"]![0]!["units"]!["malazan_crossbowman"] = weight);
        if (!r.Ok) return; // refused: fine
        AiComposition c = r.Data!.Ai[r.Data.FindFaction("malazan")].Openings[0].Compositions[0];
        Assert.All(c.Weights, w => Assert.True(w > 0f, $"a weight of {weight} loaded and is stored as {w}"));
    }

    /// <summary>The loader's limits at their edges load (256 steps, count 200, wave multiplier 10, fractions 0 and 1, pop 0).</summary>
    [Fact]
    public void EveryLimit_AtItsEdge_Loads()
    {
        DataLoadResult r = LoadWith(root =>
        {
            var steps = new JsonArray();
            for (int i = 0; i < DataLimits.MaxAiBuildOrderSteps; i++)
                steps.Add(JsonNode.Parse($$"""{ "at": { "time": {{i}} }, "do": "train", "id": "malazan_laborer", "count": {{DataLimits.MaxAiStepCount}} }"""));
            AiScenes.Opening(root)["buildOrder"] = steps;
            root["difficulties"]!["easy"]!["waveSizeMultiplier"] = DataLimits.MaxAiWaveSizeMultiplier;
            root["difficulties"]!["easy"]!["gatherBonus"] = 1;
            root["difficulties"]!["easy"]!["thinkInterval"] = 0.05; // exactly one tick
            root["difficulties"]!["easy"]!["expandAfter"] = 3600;
            root["retreat"]!["hpFraction"] = 0;
            root["retreat"]!["lossFraction"] = 1;
            AiScenes.Opening(root)["attack"]!["minArmyPop"] = 0;
            AiScenes.Opening(root)["attack"]!["minArmyCost"] = 0;
        });
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        AiDef ai = r.Data!.Ai[r.Data.FindFaction("malazan")];
        Assert.Equal(DataLimits.MaxAiBuildOrderSteps, ai.Openings[0].BuildOrder.Length);
        Assert.All(ai.Openings[0].BuildOrder, s => Assert.Equal(DataLimits.MaxAiStepCount, s.Count));
        Assert.Equal(1, ai.Difficulty(AiDifficulty.Easy).ThinkTicks);
        Assert.Equal(72_000, ai.Difficulty(AiDifficulty.Easy).ExpandAfterTicks);
    }

    /// <summary>One step past each limit: 257 steps, count 201, multiplier 10.001: each one error.</summary>
    [Fact]
    public void OnePastEachLimit_IsOneError()
    {
        DataLoadResult steps = LoadWith(root =>
        {
            var list = new JsonArray();
            for (int i = 0; i <= DataLimits.MaxAiBuildOrderSteps; i++) list.Add(JsonNode.Parse($$"""{ "at": { "workers": {{i}} }, "do": "expand" }"""));
            AiScenes.Opening(root)["buildOrder"] = list;
        });
        Assert.Equal("openings[0].buildOrder", Assert.Single(steps.Errors).Path);
        DataLoadResult count = LoadWith(root => AiScenes.Opening(root)["buildOrder"]![0]!["count"] = DataLimits.MaxAiStepCount + 1);
        Assert.Equal("openings[0].buildOrder[0].count", Assert.Single(count.Errors).Path);
        DataLoadResult wave = LoadWith(root => root["difficulties"]!["hard"]!["waveSizeMultiplier"] = 10.001);
        Assert.Equal("difficulties.hard.waveSizeMultiplier", Assert.Single(wave.Errors).Path);
    }

    /// <summary>Two expand steps at the same time are the same step written twice (no id): one error at the second.</summary>
    [Fact]
    public void TwoExpandsAtOneTime_AreADuplicate_ButAtDifferentTriggers_AreNot()
    {
        DataLoadResult dup = LoadWith(root =>
        {
            AiScenes.Opening(root)["buildOrder"]!.AsArray().Add(JsonNode.Parse("""{ "at": { "time": 60 }, "do": "expand" }"""));
            AiScenes.Opening(root)["buildOrder"]!.AsArray().Add(JsonNode.Parse("""{ "at": { "time": 60 }, "do": "expand", "count": 2 }"""));
        });
        DataError e = Assert.Single(dup.Errors);
        int shipped = TestSim.Data.Ai[TestSim.Data.FindFaction("malazan")].Openings[0].BuildOrder.Length; // whatever the shipped order's length (BUG-0571)
        Assert.Equal($"openings[0].buildOrder[{shipped + 1}]", e.Path);
        // time 60 s and workers 60 are different triggers with the same number: not a duplicate.
        DataLoadResult ok = LoadWith(root =>
        {
            AiScenes.Opening(root)["buildOrder"]!.AsArray().Add(JsonNode.Parse("""{ "at": { "time": 60 }, "do": "expand" }"""));
            AiScenes.Opening(root)["buildOrder"]!.AsArray().Add(JsonNode.Parse("""{ "at": { "workers": 1200 }, "do": "expand" }"""));
        });
        Assert.True(ok.Ok, string.Join("\n", ok.Errors));
    }

    /// <summary>Loading the shipped data twice gives equal AI data (load is deterministic; record equality over the immutable arrays' contents).</summary>
    [Fact]
    public void ShippedAi_LoadsTheSame_Twice()
    {
        GameData a = DataLoader.LoadAll(TestDataDir.Shipped).Data!, b = DataLoader.LoadAll(TestDataDir.Shipped).Data!;
        Assert.Equal(a.Ai.Length, b.Ai.Length);
        for (int f = 0; f < a.Ai.Length; f++)
        {
            Assert.Equal(a.Ai[f].Difficulties, b.Ai[f].Difficulties);
            Assert.Equal(a.Ai[f].Openings[0].BuildOrder, b.Ai[f].Openings[0].BuildOrder);
            for (int k = 0; k < a.Ai[f].Openings[0].Compositions.Length; k++)
            {
                Assert.Equal(a.Ai[f].Openings[0].Compositions[k].Units, b.Ai[f].Openings[0].Compositions[k].Units);
                Assert.Equal(a.Ai[f].Openings[0].Compositions[k].Weights, b.Ai[f].Openings[0].Compositions[k].Weights);
            }
        }
    }

    private static string ReplaceFirst(string text, string find, string replace)
    {
        int i = text.IndexOf(find, StringComparison.Ordinal);
        return text[..i] + replace + text[(i + find.Length)..];
    }
}
