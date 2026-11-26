using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-9 (Request 59, docs/03 "Implementation (M7-9)"): an opening's optional <c>buildOrders</c>, a build order per difficulty
/// id. Loader rows (criterion 1: entries load; the Producer's rulings (a)-(c) are one error each; <c>buildOrder</c>'s step
/// errors reproduce under <c>buildOrders.&lt;difficulty&gt;</c>; several errors all come out), the accessor
/// <see cref="AiOpening.BuildOrderFor"/> (criterion 2) and the content hash (criterion 6). Every edit finds the Malazan
/// <c>standard</c> opening by id and writes the field whatever the shipped file carried (rule 58).
/// </summary>
public class PerDifficultyBuildOrderLoaderTests
{
    private const string MalazanAi = "factions/malazan/ai.json";

    private const string Train = """{ "at": { "time": 0 }, "do": "train", "id": "malazan_laborer", "count": 4 }""";
    private const string Barracks = """{ "at": { "pop": 6 }, "do": "build", "id": "malazan_barracks" }""";
    private const string Expand = """{ "at": { "time": 300 }, "do": "expand" }""";

    /// <summary>The index and object of the Malazan <c>standard</c> opening in an ai.json root.</summary>
    private static (int Index, JsonObject Opening) Standard(JsonNode root)
    {
        JsonArray openings = root["openings"]!.AsArray();
        for (int i = 0; i < openings.Count; i++)
            if ((string?)openings[i]!["id"] == "standard") return (i, openings[i]!.AsObject());
        throw new InvalidOperationException("no standard opening in the Malazan ai.json");
    }

    /// <summary>Loads a copy of the shipped data whose Malazan <c>standard</c> opening's <c>buildOrders</c> is <paramref name="json"/> (null: taken out); the opening's index.</summary>
    private static DataLoadResult Load(string? json, out int index)
    {
        int i = -1;
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(MalazanAi, root =>
        {
            (i, JsonObject o) = Standard(root);
            o.Remove("buildOrders");
            if (json != null) o["buildOrders"] = JsonNode.Parse(json);
        });
        index = i;
        return DataLoader.LoadAll(dir.Path);
    }

    private static AiOpening StandardOf(GameData d)
    {
        AiDef ai = d.Ai[d.FindFaction("malazan")];
        return ai.Openings[ai.FindOpening("standard")];
    }

    [Fact]
    public void OneEntry_Loads_AndOnlyItsDifficultyHasIt()
    {
        DataLoadResult r = Load($$"""{ "hard": [ {{Train}}, {{Barracks}} ] }""", out _);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        AiOpening o = StandardOf(r.Data!);
        Assert.Equal(3, o.BuildOrders.Length);
        Assert.True(o.BuildOrders[0].IsEmpty && o.BuildOrders[1].IsEmpty);
        Assert.Equal(2, o.BuildOrders[2].Length);
        Assert.Equal((AiStepAction.Train, r.Data!.FindUnit("malazan_laborer"), 4), (o.BuildOrders[2][0].Action, o.BuildOrders[2][0].Target, o.BuildOrders[2][0].Count));
        Assert.Equal((AiStepTrigger.Pop, 12, AiStepAction.Build, r.Data.FindBuilding("malazan_barracks")),
            (o.BuildOrders[2][1].Trigger, o.BuildOrders[2][1].At, o.BuildOrders[2][1].Action, o.BuildOrders[2][1].Target));
        Assert.True(o.BuildOrder.SequenceEqual(StandardOf(TestSim.Data).BuildOrder)); // buildOrder untouched
    }

    [Fact]
    public void TwoEntries_Load_InDifficultyOrder_WhateverTheKeyOrder()
    {
        DataLoadResult r = Load($$"""{ "hard": [ {{Barracks}} ], "easy": [ {{Train}}, {{Expand}} ] }""", out _);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        AiOpening o = StandardOf(r.Data!);
        Assert.Equal(2, o.BuildOrders[(int)AiDifficulty.Easy].Length);
        Assert.True(o.BuildOrders[(int)AiDifficulty.Normal].IsEmpty);
        Assert.Single(o.BuildOrders[(int)AiDifficulty.Hard]);
        Assert.Equal(AiStepAction.Expand, o.BuildOrders[(int)AiDifficulty.Easy][1].Action);
        Assert.Equal(-1, o.BuildOrders[(int)AiDifficulty.Easy][1].Target);
    }

    /// <summary>Ruling (d): an entry equal to <c>buildOrder</c> step for step is allowed.</summary>
    [Fact]
    public void AnEntryEqualToBuildOrder_Loads()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(MalazanAi, root =>
        {
            JsonObject o = Standard(root).Opening;
            o["buildOrders"] = new JsonObject { ["normal"] = o["buildOrder"]!.DeepClone() };
        });
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        AiOpening a = StandardOf(r.Data!);
        Assert.True(a.BuildOrders[1].SequenceEqual(a.BuildOrder));
    }

    /// <summary>Rulings (a) and (b) and the M7-8 step rules under the entry's path: exactly one error each, at its path.</summary>
    [Theory]
    // (a) every difficulty has an entry: buildOrder could never be played.
    [InlineData($$"""{ "easy": [ {{Train}} ], "normal": [ {{Train}} ], "hard": [ {{Train}} ] }""", "", "write one of them as buildOrder")]
    // (b) null, {}, an entry null, an entry [].
    [InlineData("null", "", "null is not a set of build orders")]
    [InlineData("{}", "", "no entries")]
    [InlineData("""{ "easy": null }""", ".easy", "null is not a build order")]
    [InlineData("""{ "hard": [] }""", ".hard", "the build order has no steps")]
    // buildOrder's step errors, under buildOrders.<difficulty>.
    [InlineData("""{ "hard": [ { "at": { "time": 0 }, "do": "build", "id": "malazan_nothing" } ] }""", ".hard[0].id", "unknown building 'malazan_nothing'")]
    [InlineData("""{ "hard": [ { "at": { "time": 0 }, "do": "train", "id": "whirlwind_camp_follower" } ] }""", ".hard[0].id", "belongs to faction 'whirlwind'")]
    [InlineData("""{ "easy": [ { "at": { "time": 0 }, "do": "build", "id": "malazan_barracks", "throughReserve": true } ] }""", ".easy[0].throughReserve", "throughReserve is for a train step only")]
    [InlineData($$"""{ "easy": [ {{Train}}, { "at": { "time": 0 }, "do": "train", "id": "malazan_laborer", "noReserve": false } ] }""", ".easy[1].noReserve", "noReserve is for a build")]
    [InlineData($$"""{ "normal": [ {{Barracks}}, {{Barracks}} ] }""", ".normal[1]", "the same step as openings[{0}].buildOrders.normal[0]")]
    [InlineData("""{ "hard": [ { "at": { "time": 0 }, "do": "expand", "id": "malazan_garrison_keep" } ] }""", ".hard[0].id", "an expand step names no id")]
    [InlineData("""{ "hard": [ { "at": { "time": 0 }, "do": "train", "id": "malazan_laborer", "count": 0 } ] }""", ".hard[0].count", "0 is outside 1-")]
    [InlineData("""{ "hard": [ { "at": { "time": 0 }, "do": "research", "id": "age_ii", "count": 2 } ] }""", ".hard[0].count", "a research step takes no count")]
    [InlineData("""{ "easy": [ { "at": { "time": 0, "pop": 4 }, "do": "train", "id": "malazan_laborer" } ] }""", ".easy[0].at", "set exactly one of")]
    public void ABrokenEntry_IsExactlyOneError_AtItsPath(string json, string suffix, string message)
    {
        DataLoadResult r = Load(json, out int i);
        DataError e = Assert.Single(r.Errors);
        Assert.Equal(MalazanAi, e.File);
        Assert.Equal($"openings[{i}].buildOrders{suffix}", e.Path);
        Assert.Contains(string.Format(message, i), e.Message);
    }

    /// <summary>A step error names the opening and the step (M7-8's message) under the entry too.</summary>
    [Fact]
    public void ASwitchOnTheWrongAction_NamesTheOpeningAndTheStep()
    {
        DataLoadResult r = Load("""{ "hard": [ { "at": { "time": 0 }, "do": "build", "id": "malazan_barracks", "throughReserve": true } ] }""", out _);
        DataError e = Assert.Single(r.Errors);
        Assert.Contains("opening 'standard' step 0 (build malazan_barracks)", e.Message);
    }

    /// <summary>More steps than <see cref="DataLimits.MaxAiBuildOrderSteps"/> is one error at the entry.</summary>
    [Fact]
    public void MoreStepsThanTheLimit_IsOneError_AtTheEntry()
    {
        var steps = new JsonArray();
        for (int k = 0; k <= DataLimits.MaxAiBuildOrderSteps; k++)
            steps.Add(JsonNode.Parse($$"""{ "at": { "time": {{k}} }, "do": "train", "id": "malazan_laborer" }"""));
        DataLoadResult r = Load(new JsonObject { ["easy"] = steps.DeepClone() }.ToJsonString(), out int i);
        DataError e = Assert.Single(r.Errors);
        Assert.Equal($"openings[{i}].buildOrders.easy", e.Path);
        Assert.Contains($"{DataLimits.MaxAiBuildOrderSteps + 1} steps is above the maximum {DataLimits.MaxAiBuildOrderSteps}", e.Message);
        // The limit itself loads.
        steps.RemoveAt(0);
        Assert.True(Load(new JsonObject { ["easy"] = steps.DeepClone() }.ToJsonString(), out _).Ok);
    }

    /// <summary>Ruling (c): an unknown key is the loader's unknown-property error (one error naming it; the file does not load).</summary>
    [Theory]
    [InlineData("medium")]
    [InlineData("Hard")]
    [InlineData("buildOrder")]
    public void AnUnknownKey_IsAnError(string key)
    {
        DataLoadResult r = Load($$"""{ "easy": [ {{Train}} ], "{{key}}": [ {{Train}} ] }""", out _);
        DataError e = Assert.Single(r.Errors);
        Assert.Equal(MalazanAi, e.File);
        Assert.Contains(key, e.Path + " " + e.Message);
        Assert.Null(r.Data);
    }

    /// <summary>Several errors in one file are all reported, each at its path (an entry's and another opening field's).</summary>
    [Fact]
    public void SeveralErrors_AreAllReported()
    {
        int i = -1;
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(MalazanAi, root =>
        {
            (i, JsonObject o) = Standard(root);
            o["buildOrders"] = JsonNode.Parse($$"""
                {
                  "easy": [ { "at": { "time": 0 }, "do": "build", "id": "malazan_nothing" }, {{Expand}}, {{Expand}} ],
                  "hard": [ { "at": { "time": 0 }, "do": "train", "id": "malazan_laborer", "count": 999 },
                            { "at": { "time": 0 }, "do": "research", "id": "age_ii", "noReserve": true, "throughReserve": true } ]
                }
                """);
            o["displayName"] = "";
        });
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        string p = $"openings[{i}]";
        Assert.Equal(new[]
        {
            $"{p}.displayName", $"{p}.buildOrders.easy[0].id", $"{p}.buildOrders.easy[2]", $"{p}.buildOrders.hard[0].count",
            $"{p}.buildOrders.hard[1].throughReserve",
        }.OrderBy(x => x, StringComparer.Ordinal), r.Errors.Select(e => e.Path).OrderBy(x => x, StringComparer.Ordinal));
    }

    /// <summary>Criterion 2: each difficulty with and without its own entry gets the right list (built directly).</summary>
    [Theory]
    [InlineData(AiDifficulty.Easy)]
    [InlineData(AiDifficulty.Normal)]
    [InlineData(AiDifficulty.Hard)]
    public void BuildOrderFor_IsTheEntry_ElseBuildOrder(AiDifficulty difficulty)
    {
        ImmutableArray<AiStep> main = ImmutableArray.Create(new AiStep { Action = AiStepAction.Train, TargetKey = "main", Count = 1 });
        ImmutableArray<AiStep> own = ImmutableArray.Create(new AiStep { Action = AiStepAction.Build, TargetKey = "own", Count = 1 });
        ImmutableArray<AiStep> other = ImmutableArray.Create(new AiStep { Action = AiStepAction.Expand, TargetKey = "", Count = 1 });
        AiOpening Make(ImmutableArray<ImmutableArray<AiStep>> lists) => new()
        {
            Id = "x", DisplayName = "X", Description = "", BuildOrder = main, BuildOrders = lists, Compositions = ImmutableArray<AiComposition>.Empty,
        };
        int d = (int)difficulty;
        // No field at all.
        Assert.Equal(main, Make(default).BuildOrderFor(difficulty));
        // The field with entries for the other difficulties only.
        var without = new ImmutableArray<AiStep>[3];
        for (int k = 0; k < 3; k++) if (k != d) without[k] = other;
        AiOpening o = Make(ImmutableArray.Create(without));
        Assert.Equal(main, o.BuildOrderFor(difficulty));
        // The field with its own entry (and one other).
        var with = new ImmutableArray<AiStep>[3];
        with[d] = own;
        with[(d + 1) % 3] = other;
        o = Make(ImmutableArray.Create(with));
        Assert.Equal(own, o.BuildOrderFor(difficulty));
        Assert.Equal(other, o.BuildOrderFor((AiDifficulty)((d + 1) % 3)));
        Assert.Equal(main, o.BuildOrderFor((AiDifficulty)((d + 2) % 3)));
    }

    /// <summary>Criterion 2 on loaded data: easy and hard entries, normal plays buildOrder.</summary>
    [Fact]
    public void BuildOrderFor_OnLoadedData()
    {
        DataLoadResult r = Load($$"""{ "easy": [ {{Train}} ], "hard": [ {{Barracks}}, {{Expand}} ] }""", out _);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        AiOpening o = StandardOf(r.Data!);
        Assert.Equal(AiStepAction.Train, Assert.Single(o.BuildOrderFor(AiDifficulty.Easy)).Action);
        Assert.Equal(o.BuildOrder, o.BuildOrderFor(AiDifficulty.Normal));
        Assert.Equal(2, o.BuildOrderFor(AiDifficulty.Hard).Length);
        Assert.Equal(StandardOf(TestSim.Data).BuildOrder.Length, o.BuildOrderFor(AiDifficulty.Normal).Length);
    }

    /// <summary>
    /// Criterion 6: data with the field hashes apart from data without it (even with an entry equal to buildOrder); two files
    /// one step apart in <c>buildOrders.hard</c> hash apart; the key order inside the object does not move the hash; and the
    /// field taken out is the shipped hash again.
    /// </summary>
    [Fact]
    public void TheHash_FollowsTheEntries_NotTheKeyOrder()
    {
        ulong Hash(string? json)
        {
            DataLoadResult r = Load(json, out _);
            Assert.True(r.Ok, string.Join("\n", r.Errors));
            return r.Data!.ContentHash();
        }
        ulong none = Hash(null);
        Assert.Equal(StandardOf(TestSim.Data).BuildOrders.IsEmpty ? TestSim.Data.ContentHash() : none, none);
        ulong easyHard = Hash($$"""{ "easy": [ {{Train}} ], "hard": [ {{Barracks}}, {{Expand}} ] }""");
        ulong hardEasy = Hash($$"""{ "hard": [ {{Barracks}}, {{Expand}} ], "easy": [ {{Train}} ] }""");
        ulong oneStepApart = Hash($$"""{ "easy": [ {{Train}} ], "hard": [ {{Barracks}}, { "at": { "time": 301 }, "do": "expand" } ] }""");
        ulong hardOnly = Hash($$"""{ "hard": [ {{Barracks}}, {{Expand}} ] }""");
        ulong sameAsHard = Hash($$"""{ "normal": [ {{Barracks}}, {{Expand}} ] }""");
        Assert.NotEqual(none, easyHard);
        Assert.Equal(easyHard, hardEasy);
        Assert.NotEqual(easyHard, oneStepApart);
        Assert.NotEqual(easyHard, hardOnly);
        Assert.NotEqual(hardOnly, sameAsHard); // the same list under another difficulty
        // An entry equal to buildOrder step for step is still the field: it moves the hash.
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(MalazanAi, root =>
        {
            JsonObject o = Standard(root).Opening;
            o["buildOrders"] = new JsonObject { ["normal"] = o["buildOrder"]!.DeepClone() };
        });
        DataLoadResult copy = DataLoader.LoadAll(dir.Path);
        Assert.True(copy.Ok);
        Assert.NotEqual(none, copy.Data!.ContentHash());
    }
}
