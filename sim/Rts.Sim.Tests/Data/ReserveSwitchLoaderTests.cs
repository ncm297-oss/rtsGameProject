using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-8 (Request 55, docs/03 "Implementation (M7-8)"): a build-order step's optional <c>throughReserve</c> (train steps) and
/// <c>noReserve</c> (build, research and expand steps) load to <see cref="AiStep.ThroughReserve"/> /
/// <see cref="AiStep.NoReserve"/>; absent, JSON <c>null</c> and <c>false</c> load the same step and hash as the file without
/// them, and <c>true</c> moves <see cref="GameData.ContentHash"/>. The wrong-action rows are in
/// <see cref="DataValidationTests"/>. Every edit sets a field on a step found by its action in the shipped file (rule 58).
/// </summary>
public class ReserveSwitchLoaderTests
{
    private const string MalazanAi = "factions/malazan/ai.json";

    /// <summary>The (opening, step) indices of the first step doing <paramref name="what"/> in an ai.json root, file order.</summary>
    internal static (int Opening, int Step) FirstStep(JsonNode root, string what)
    {
        JsonArray openings = root["openings"]!.AsArray();
        for (int i = 0; i < openings.Count; i++)
        {
            JsonArray order = openings[i]!["buildOrder"]!.AsArray();
            for (int j = 0; j < order.Count; j++)
                if ((string?)order[j]!["do"] == what) return (i, j);
        }
        throw new InvalidOperationException($"no {what} step in the file");
    }

    /// <summary>The switch a step doing <paramref name="what"/> may carry.</summary>
    internal static string SwitchFor(string what) => what == "train" ? "throughReserve" : "noReserve";

    /// <summary>A copy of the shipped data with <paramref name="edit"/> applied to the Malazan ai.json; the loaded data (the load must succeed).</summary>
    private static GameData LoadWith(Action<JsonNode> edit)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(MalazanAi, edit);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        return r.Data!;
    }

    private static AiStep StepOf(GameData d, (int Opening, int Step) at) => d.Ai[d.FindFaction("malazan")].Openings[at.Opening].BuildOrder[at.Step];

    /// <summary>
    /// Each switch loads on each action it is valid on: <c>true</c> sets the property and moves the hash; <c>false</c>,
    /// <c>null</c> and absent leave the step equal to the shipped one and the hash the shipped hash.
    /// </summary>
    [Theory]
    [InlineData("train")]
    [InlineData("build")]
    [InlineData("research")]
    [InlineData("expand")]
    public void ASwitch_LoadsOnItsActions_FalseNullAndAbsentAreTheShippedStep(string what)
    {
        string field = SwitchFor(what);
        (int, int) at = (0, 0);
        GameData With(string? json) => LoadWith(root =>
        {
            at = FirstStep(root, what);
            JsonObject step = root["openings"]![at.Item1]!["buildOrder"]![at.Item2]!.AsObject();
            step.Remove(field);
            if (json != null) step[field] = JsonNode.Parse(json);
        });
        GameData on = With("true"), off = With("false"), nul = With("null"), absent = With(null);
        AiStep shipped = StepOf(TestSim.Data, at);
        Assert.Equal(what == "train", StepOf(on, at).ThroughReserve);
        Assert.Equal(what != "train", StepOf(on, at).NoReserve);
        Assert.Equal(shipped with { ThroughReserve = what == "train", NoReserve = what != "train" }, StepOf(on, at));
        foreach (GameData d in new[] { off, nul, absent })
        {
            Assert.Equal(shipped, StepOf(d, at));
            Assert.Equal(TestSim.Data.ContentHash(), d.ContentHash());
        }
        Assert.NotEqual(TestSim.Data.ContentHash(), on.ContentHash());
    }

    /// <summary>The two switches move the hash differently from each other: a throughReserve train step and a noReserve build step are not one marker.</summary>
    [Fact]
    public void TheTwoSwitches_HashApart()
    {
        GameData through = LoadWith(root =>
        {
            (int i, int j) = FirstStep(root, "train");
            root["openings"]![i]!["buildOrder"]![j]!["throughReserve"] = true;
        });
        GameData both = LoadWith(root =>
        {
            (int i, int j) = FirstStep(root, "train");
            root["openings"]![i]!["buildOrder"]![j]!["throughReserve"] = true;
            (i, j) = FirstStep(root, "build");
            root["openings"]![i]!["buildOrder"]![j]!["noReserve"] = true;
        });
        Assert.NotEqual(TestSim.Data.ContentHash(), through.ContentHash());
        Assert.NotEqual(through.ContentHash(), both.ContentHash());
    }

    /// <summary>
    /// Criterion 7's data half: the shipped data with both switches written <c>false</c> on every step of every faction's
    /// openings that may carry one (<see cref="WriteFalseSwitches"/>) loads to the same build orders and hashes to the shipped
    /// content hash. The match half is <see cref="ReserveSwitchOffTwinTests"/>.
    /// </summary>
    [Fact]
    public void TheShippedDataWithEverySwitchWrittenFalse_IsTheShippedData()
    {
        int written = 0;
        GameData d = AiScenes.DataWith(dir => written = WriteFalseSwitches(dir));
        Assert.True(written > 0);
        Assert.Equal(TestSim.Data.ContentHash(), d.ContentHash());
        Assert.Equal(TestSim.Data.Ai.Length, d.Ai.Length);
        for (int f = 0; f < d.Ai.Length; f++)
            for (int o = 0; o < d.Ai[f].Openings.Length; o++)
                Assert.True(TestSim.Data.Ai[f].Openings[o].BuildOrder.SequenceEqual(d.Ai[f].Openings[o].BuildOrder), $"faction {f} opening {o}");
    }

    /// <summary>
    /// Writes <c>"throughReserve": false</c> on every train step and <c>"noReserve": false</c> on every build, research and
    /// expand step of every shipped faction's openings, leaving a step that already carries its switch as it is (rule 58:
    /// a value the data track ships stays); how many fields were written.
    /// </summary>
    internal static int WriteFalseSwitches(TestDataDir dir)
    {
        int written = 0;
        foreach (string f in TestSim.ShippedFactions)
            dir.EditJson($"factions/{f}/ai.json", root =>
            {
                foreach (JsonNode? o in root["openings"]!.AsArray())
                    foreach (JsonNode? s in o!["buildOrder"]!.AsArray())
                    {
                        string field = SwitchFor((string)s!["do"]!);
                        if (s.AsObject().ContainsKey(field)) continue;
                        s[field] = false;
                        written++;
                    }
            });
        return written;
    }
}
