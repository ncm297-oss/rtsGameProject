using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;
using Rts.Sim.Tests.Stress;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M7-H2 (session 2026-11-14-0616): the Frenzy report's opening-from-a-file configuration
/// (<see cref="FrenzyCastReportTests.FileOpening"/>) fed what the brief's QA focus names: a missing file (absolute this
/// time), an empty object, an opening naming a Malazan unit (in its build order, and in a composition), one with a
/// different id; and the near-empty files (<c>null</c>, nothing, whitespace). Each refused input throws one
/// <see cref="InvalidOperationException"/> naming the path; a different id goes in as <c>standard</c> with its own steps.
/// </summary>
public class M7H2FileOpeningQaTests
{
    private static string Temp(string text)
    {
        string path = Path.Combine(Path.GetTempPath(), $"rts-qa-m7h2-opening-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, text);
        return path;
    }

    private static InvalidOperationException Refused(string text, params string[] contains)
    {
        string path = Temp(text);
        try
        {
            var e = Assert.Throws<InvalidOperationException>(() => FrenzyCastReportTests.FileOpening(path));
            Assert.Contains(path, e.Message);
            foreach (string s in contains) Assert.Contains(s, e.Message);
            return e;
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AMissingAbsoluteFile_FailsNamingIt()
    {
        string path = Path.Combine(Path.GetTempPath(), $"rts-qa-m7h2-missing-{Guid.NewGuid():N}.json");
        var e = Assert.Throws<InvalidOperationException>(() => FrenzyCastReportTests.FileOpening(path));
        Assert.Contains(path, e.Message);
        Assert.Contains(FrenzyCastReportTests.OpeningVariable, e.Message);
    }

    [Fact]
    public void ADirectoryNotAFile_FailsNamingIt()
    {
        string path = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var e = Assert.Throws<InvalidOperationException>(() => FrenzyCastReportTests.FileOpening(path));
        Assert.Contains(path, e.Message);
    }

    [Fact]
    public void AnEmptyObject_IsRefused_NamingThePath() => Refused("{}", "does not load");

    [Theory]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("   \n")]
    [InlineData("\"standard\"")]
    public void ANearEmptyFile_IsRefused_NamingThePath(string text) => Refused(text);

    [Fact]
    public void AnOpeningTrainingAMalazanUnit_IsRefused_NamingTheUnit()
    {
        JsonObject o = TeblorOpeningHorizonReportTests.HandOver();
        o["buildOrder"]!.AsArray().Add(JsonNode.Parse("{\"at\":{\"time\":600},\"do\":\"train\",\"id\":\"malazan_heavy_infantry\"}"));
        Refused(o.ToJsonString(), "malazan_heavy_infantry");
    }

    [Fact]
    public void AnOpeningBuildingAMalazanBuilding_IsRefused_NamingIt()
    {
        JsonObject o = TeblorOpeningHorizonReportTests.HandOver();
        JsonNode b = JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.Shipped, "factions", "malazan", "buildings.json")))!;
        string building = (string)b["buildings"]![0]!["id"]!; // any Malazan building: which one does not matter (read, not indexed into data)
        o["buildOrder"]!.AsArray().Add(JsonNode.Parse($"{{\"at\":{{\"time\":600}},\"do\":\"build\",\"id\":\"{building}\"}}"));
        Refused(o.ToJsonString(), building);
    }

    [Fact]
    public void AnOpeningWhoseCompositionNamesAMalazanUnit_IsRefused_NamingTheUnit()
    {
        JsonObject o = TeblorOpeningHorizonReportTests.HandOver();
        o["compositions"]![1]!["units"]!["malazan_heavy_infantry"] = 10;
        Refused(o.ToJsonString(), "malazan_heavy_infantry");
    }

    /// <summary>A different id goes in as the Teblor's <c>standard</c>: its own steps, not the shipped standard's; the other openings untouched.</summary>
    [Fact]
    public void AnOpeningWithADifferentId_GoesInAsStandard_WithItsOwnSteps()
    {
        JsonObject o = TeblorOpeningHorizonReportTests.HandOver();
        o["id"] = "qa_some_other_id";
        string path = Temp(o.ToJsonString());
        try
        {
            (string name, GameData data) = FrenzyCastReportTests.FileOpening(path)!.Value;
            Assert.Contains(path, name);
            AiOpening standard = FrenzyCastReportTests.Standard(data);
            Assert.Equal(o["buildOrder"]!.AsArray().Count, standard.BuildOrder.Length);
            ImmutableArrayCount(data, TestSim.Data);
        }
        finally { File.Delete(path); }
    }

    /// <summary>An opening whose id is another shipped Teblor opening's: it replaces <c>standard</c> only, so no id is listed twice.</summary>
    [Fact]
    public void AnOpeningNamedLikeAnotherShippedOpening_ReplacesStandardOnly()
    {
        GameData shipped = TestSim.Data;
        var ids = shipped.Ai[shipped.FindFaction("teblor")].Openings.Select(x => x.Id).ToList();
        string? other = ids.FirstOrDefault(x => x != "standard");
        if (other == null) return; // only one shipped opening: nothing to collide with
        JsonObject o = TeblorOpeningHorizonReportTests.HandOver();
        o["id"] = other;
        string path = Temp(o.ToJsonString());
        try
        {
            (_, GameData data) = FrenzyCastReportTests.FileOpening(path)!.Value;
            Assert.Equal(ids, data.Ai[data.FindFaction("teblor")].Openings.Select(x => x.Id).ToList());
        }
        finally { File.Delete(path); }
    }

    private static void ImmutableArrayCount(GameData copy, GameData shipped)
    {
        var a = copy.Ai[copy.FindFaction("teblor")].Openings.Select(x => x.Id).ToList();
        var b = shipped.Ai[shipped.FindFaction("teblor")].Openings.Select(x => x.Id).ToList();
        Assert.Equal(b, a);
    }
}
