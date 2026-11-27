using System.Diagnostics;
using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M7-9, Request 59): <c>buildOrders</c> shapes the developer's loader rows do not hold. Every bad shape must be a load
/// error naming the file (never a crash, a hang or a silent default); the good shapes the docs allow must load; and the
/// content hash must follow the lists, not the file's layout. Every edit looks the opening up by id and replaces whatever
/// the shipped file carries (rule 58).
/// </summary>
public class M79PerDifficultyLoaderQaTests
{
    private const string MalazanAi = "factions/malazan/ai.json";
    private const string Train = """{ "at": { "time": 0 }, "do": "train", "id": "malazan_laborer", "count": 4 }""";
    private const string Barracks = """{ "at": { "pop": 6 }, "do": "build", "id": "malazan_barracks" }""";

    private readonly ITestOutputHelper _out;

    public M79PerDifficultyLoaderQaTests(ITestOutputHelper output) => _out = output;

    private static (int Index, JsonObject Opening) Find(JsonNode root, string id)
    {
        JsonArray openings = root["openings"]!.AsArray();
        for (int i = 0; i < openings.Count; i++)
            if ((string?)openings[i]!["id"] == id) return (i, openings[i]!.AsObject());
        throw new InvalidOperationException($"no opening '{id}' in the Malazan ai.json");
    }

    /// <summary>A copy of the shipped data whose Malazan opening <paramref name="id"/> gets <paramref name="edit"/> (its <c>buildOrders</c> removed first); the opening's index.</summary>
    private static DataLoadResult Load(string id, Action<JsonObject> edit, out int index)
    {
        int i = -1;
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(MalazanAi, root =>
        {
            (i, JsonObject o) = Find(root, id);
            o.Remove("buildOrders");
            edit(o);
        });
        index = i;
        return DataLoader.LoadAll(dir.Path);
    }

    private static DataLoadResult LoadField(string raw, out int index) => Load("standard", o => o["buildOrders"] = JsonNode.Parse(raw), out index);

    private string Show(DataLoadResult r) => string.Join(" | ", r.Errors.Select(e => $"{e.File} {e.Path}: {e.Message}"));

    /// <summary>A 10,000-step entry is one error at the entry (no per-step flood), fast, and the file does not load.</summary>
    [Fact]
    public void ATenThousandStepEntry_IsOneError_AndLoadsFast()
    {
        var steps = new JsonArray();
        for (int k = 0; k < 10_000; k++)
            steps.Add(JsonNode.Parse($$"""{ "at": { "time": {{k}} }, "do": "train", "id": "malazan_laborer" }"""));
        var watch = Stopwatch.StartNew();
        DataLoadResult r = Load("standard", o => o["buildOrders"] = new JsonObject { ["hard"] = steps }, out int i);
        watch.Stop();
        _out.WriteLine($"{watch.ElapsedMilliseconds} ms: {Show(r)}");
        DataError e = Assert.Single(r.Errors);
        Assert.Equal(MalazanAi, e.File);
        Assert.Equal($"openings[{i}].buildOrders.hard", e.Path);
        Assert.Contains("10000 steps is above the maximum", e.Message);
        Assert.Null(r.Data);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"the load took {watch.Elapsed}");
    }

    /// <summary>A valid entry beside two broken ones: exactly the broken ones' errors, at their paths; nothing loads.</summary>
    [Fact]
    public void MixedValidAndInvalidEntries_ReportOnlyTheBrokenOnes()
    {
        DataLoadResult r = LoadField($$"""
            { "easy": [ {{Train}}, {{Barracks}} ],
              "normal": null,
              "hard": [ {{Train}}, { "at": { "time": 0 }, "do": "build", "id": "malazan_nothing" } ] }
            """, out int i);
        _out.WriteLine(Show(r));
        Assert.Null(r.Data);
        // Since M8-4 (BUG-1770) ruling (a) no longer fires here: the null entry is not an entry, so only two difficulties have
        // one. The null entry and the bad id are the file's real problems, and the only ones.
        string p = $"openings[{i}].buildOrders";
        Assert.Contains(r.Errors, e => e.Path == p + ".normal");
        Assert.Contains(r.Errors, e => e.Path == p + ".hard[1].id");
        Assert.DoesNotContain(r.Errors, e => e.Path == p);
        Assert.DoesNotContain(r.Errors, e => e.Path.StartsWith(p + ".easy", StringComparison.Ordinal));
        Assert.All(r.Errors, e => Assert.Equal(MalazanAi, e.File));
    }

    /// <summary>Two openings each with a broken entry: both reported, each under its own opening.</summary>
    [Fact]
    public void BrokenEntriesInTwoOpenings_AreBothReported()
    {
        int a = -1, b = -1;
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(MalazanAi, root =>
        {
            (a, JsonObject x) = Find(root, "standard");
            (b, JsonObject y) = Find(root, "cadre_rush");
            x["buildOrders"] = JsonNode.Parse("""{ "hard": [] }""");
            y["buildOrders"] = JsonNode.Parse("""{ "easy": [ { "at": { "time": 0 }, "do": "train", "id": "teblor_warrior" } ] }""");
        });
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        _out.WriteLine(Show(r));
        Assert.Equal(new[] { $"openings[{a}].buildOrders.hard", $"openings[{b}].buildOrders.easy[0].id" }.OrderBy(x => x, StringComparer.Ordinal),
            r.Errors.Select(e => e.Path).OrderBy(x => x, StringComparer.Ordinal));
    }

    /// <summary>Another faction's ids in every kind of step of an entry: one error each, naming the owner.</summary>
    [Fact]
    public void AnEntryNamingAnotherFactionsIds_IsOneErrorPerStep()
    {
        DataLoadResult r = LoadField("""
            { "normal": [
                { "at": { "time": 0 }, "do": "train", "id": "teblor_warrior" },
                { "at": { "time": 10 }, "do": "build", "id": "whirlwind_camp_follower" },
                { "at": { "time": 20 }, "do": "build", "id": "teblor_war_lodge" } ] }
            """, out int i);
        _out.WriteLine(Show(r));
        Assert.Null(r.Data);
        string p = $"openings[{i}].buildOrders.normal";
        Assert.Equal(new[] { p + "[0].id", p + "[1].id", p + "[2].id" }, r.Errors.Select(e => e.Path).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Contains("teblor", r.Errors.Single(e => e.Path == p + "[0].id").Message);
        Assert.Contains("teblor", r.Errors.Single(e => e.Path == p + "[2].id").Message);
    }

    /// <summary>The same step in <c>buildOrder</c> and in an entry, and in two entries: the no-step-twice rule is per list, so it loads.</summary>
    [Fact]
    public void TheSameStep_InBuildOrderAndEntries_Loads()
    {
        DataLoadResult r = Load("standard", o =>
        {
            JsonNode first = o["buildOrder"]![0]!;
            o["buildOrders"] = new JsonObject
            {
                ["easy"] = new JsonArray(first.DeepClone()),
                ["hard"] = new JsonArray(first.DeepClone(), JsonNode.Parse(Barracks)),
            };
        }, out _);
        Assert.True(r.Ok, Show(r));
    }

    /// <summary>Keys in odd case or with spaces, at both levels: an error naming the file, never silently "no entry".</summary>
    [Theory]
    [InlineData("""{ "Easy": [ TRAIN ] }""")]
    [InlineData("""{ "EASY": [ TRAIN ] }""")]
    [InlineData("""{ "Normal": [ TRAIN ] }""")]
    [InlineData("""{ "HARD": [ TRAIN ] }""")]
    [InlineData("""{ "easy ": [ TRAIN ] }""")]
    [InlineData("""{ " hard": [ TRAIN ] }""")]
    [InlineData("""{ "": [ TRAIN ] }""")]
    [InlineData("""{ "0": [ TRAIN ] }""")]
    public void AnOddKey_IsALoadError(string raw)
    {
        DataLoadResult r = LoadField(raw.Replace("TRAIN", Train), out _);
        _out.WriteLine($"{raw} -> {Show(r)}");
        Assert.Null(r.Data);
        Assert.Contains(r.Errors, e => e.File == MalazanAi);
    }

    /// <summary>The field's own name in another case on the opening is the unknown-property error, not an ignored key.</summary>
    [Theory]
    [InlineData("BuildOrders")]
    [InlineData("buildorders")]
    [InlineData("buildOrderS")]
    [InlineData("build_orders")]
    public void TheFieldNameInAnotherCase_IsALoadError(string name)
    {
        DataLoadResult r = Load("standard", o => o[name] = JsonNode.Parse($$"""{ "hard": [ {{Train}} ] }"""), out _);
        _out.WriteLine($"{name} -> {Show(r)}");
        Assert.Null(r.Data);
        Assert.Contains(r.Errors, e => e.File == MalazanAi && (e.Path + " " + e.Message).Contains(name, StringComparison.Ordinal));
    }

    /// <summary>A key written twice: the loader's duplicate-key error (System.Text.Json would keep the last silently).</summary>
    [Fact]
    public void AKeyWrittenTwice_IsTheDuplicateKeyError()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        string path = Path.Combine(dir.Path, "factions", "malazan", "ai.json");
        string text = File.ReadAllText(path);
        int at = text.IndexOf("\"compositions\"", StringComparison.Ordinal); // the first opening's
        Assert.True(at > 0);
        text = text[..at] + $$"""
            "buildOrders": { "hard": [ {{Train}} ], "hard": [ {{Barracks}} ] },
            """ + text[at..];
        File.WriteAllText(path, text);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        _out.WriteLine(Show(r));
        Assert.Null(r.Data);
        Assert.Contains(r.Errors, e => e.File == MalazanAi && e.Message.Contains("duplicate key 'hard'", StringComparison.Ordinal));
    }

    /// <summary>The field or an entry of the wrong JSON type: an error saying what was expected, never a crash.</summary>
    [Theory]
    [InlineData("[]")]
    [InlineData("[ [ 1 ] ]")]
    [InlineData("\"easy\"")]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("""{ "easy": {} }""")]
    [InlineData("""{ "easy": "train" }""")]
    [InlineData("""{ "hard": 3 }""")]
    [InlineData("""{ "hard": [ null ] }""")]
    [InlineData("""{ "hard": [ 7 ] }""")]
    [InlineData("""{ "hard": [ [ ] ] }""")]
    public void TheWrongJsonType_IsALoadError(string raw)
    {
        DataLoadResult r = LoadField(raw, out _);
        _out.WriteLine($"{raw} -> {Show(r)}");
        Assert.Null(r.Data);
        Assert.Contains(r.Errors, e => e.File == MalazanAi);
    }

    /// <summary>
    /// An entry on every opening of a faction (each one or two difficulties) loads, and every list goes to its difficulty;
    /// Easy's entry on an opening other than the first loads (docs/03 "For the data track (M7-9)": never played, Question 1).
    /// </summary>
    [Fact]
    public void EntriesOnEveryOpening_Load()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(MalazanAi, root =>
        {
            foreach (JsonNode? o in root["openings"]!.AsArray())
                o!["buildOrders"] = JsonNode.Parse($$"""{ "easy": [ {{Train}} ], "hard": [ {{Barracks}} ] }""");
        });
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, Show(r));
        AiDef ai = r.Data!.Ai[r.Data.FindFaction("malazan")];
        foreach (AiOpening o in ai.Openings)
        {
            Assert.Equal(AiStepAction.Train, Assert.Single(o.BuildOrderFor(AiDifficulty.Easy)).Action);
            Assert.Equal(AiStepAction.Build, Assert.Single(o.BuildOrderFor(AiDifficulty.Hard)).Action);
            Assert.True(o.BuildOrderFor(AiDifficulty.Normal).Length > 1);
        }
    }

    /// <summary>The M7-8 switches written <c>false</c> or the expand step's <c>noReserve</c> in an entry load as in <c>buildOrder</c>.</summary>
    [Fact]
    public void SwitchesInAnEntry_LoadAsInBuildOrder()
    {
        DataLoadResult r = LoadField("""
            { "easy": [
                { "at": { "time": 0 }, "do": "train", "id": "malazan_laborer", "throughReserve": true },
                { "at": { "time": 5 }, "do": "train", "id": "malazan_laborer", "throughReserve": false },
                { "at": { "time": 10 }, "do": "build", "id": "malazan_barracks", "noReserve": true },
                { "at": { "time": 300 }, "do": "expand", "noReserve": true } ] }
            """, out _);
        Assert.True(r.Ok, Show(r));
    }

    // --- The content hash.

    private ulong Hash(Action<JsonNode> edit)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(MalazanAi, root =>
        {
            foreach (JsonNode? o in root["openings"]!.AsArray()) o!.AsObject().Remove("buildOrders");
            edit(root);
        });
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, Show(r));
        return r.Data!.ContentHash();
    }

    /// <summary>
    /// The hash follows the lists and where they are: the same entry on another opening, a switch written false (as without
    /// it), the field placed before or after <c>buildOrder</c> in the object; and with no field anywhere the shipped hash
    /// (when the shipped Malazan file carries none).
    /// </summary>
    [Fact]
    public void TheHash_FollowsTheOpeningAndTheLists_NotTheLayout()
    {
        string entry = $$"""{ "hard": [ {{Train}}, {{Barracks}} ] }""";
        ulong none = Hash(_ => { });
        ulong onStandard = Hash(root => Find(root, "standard").Opening["buildOrders"] = JsonNode.Parse(entry));
        ulong onCadre = Hash(root => Find(root, "cadre_rush").Opening["buildOrders"] = JsonNode.Parse(entry));
        ulong falseSwitch = Hash(root => Find(root, "standard").Opening["buildOrders"] =
            JsonNode.Parse($$"""{ "hard": [ { "at": { "time": 0 }, "do": "train", "id": "malazan_laborer", "count": 4, "throughReserve": false }, {{Barracks}} ] }"""));
        ulong fieldFirst = Hash(root =>
        {
            JsonObject o = Find(root, "standard").Opening;
            var copy = new JsonObject { ["buildOrders"] = JsonNode.Parse(entry) };
            foreach (var kv in o.ToList())
            {
                o.Remove(kv.Key);
                copy[kv.Key] = kv.Value;
            }
            foreach (var kv in copy.ToList())
            {
                copy.Remove(kv.Key);
                o[kv.Key] = kv.Value;
            }
        });
        bool shippedHasNone = TestSim.Data.Ai[TestSim.Data.FindFaction("malazan")].Openings.All(o => o.BuildOrders.IsEmpty);
        if (shippedHasNone) Assert.Equal(TestSim.Data.ContentHash(), none);
        Assert.NotEqual(none, onStandard);
        Assert.NotEqual(none, onCadre);
        Assert.NotEqual(onStandard, onCadre);
        Assert.Equal(onStandard, falseSwitch);
        Assert.Equal(onStandard, fieldFirst);
    }
}
