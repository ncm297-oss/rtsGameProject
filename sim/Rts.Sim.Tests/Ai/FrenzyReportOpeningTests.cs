using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;
using Rts.Sim.Tests.Stress;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-H2: the Frenzy report's opening-from-a-file configuration (<see cref="FrenzyCastReportTests.FileOpening"/>, named by
/// <see cref="FrenzyCastReportTests.OpeningVariable"/>), without the 32 matches: unset is no configuration; the D29 fixture
/// goes in as the Teblor's <c>standard</c> on the report's copy; a missing file, a file that is not one JSON object and an
/// opening the loader refuses each fail with a message naming the path. And the landing share is looked up, not a literal.
/// </summary>
public class FrenzyReportOpeningTests
{
    private const string Fixture = "Content/Fixtures/teblor_standard_d29.json";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Unset_IsNoConfiguration(string? path) => Assert.Null(FrenzyCastReportTests.FileOpening(path));

    [Fact]
    public void TheD29Fixture_GoesInAsTheTeblorsStandard_OnTheReportsCopy()
    {
        (string name, GameData data) = FrenzyCastReportTests.FileOpening(Fixture)!.Value;
        Assert.Contains(Fixture, name);
        JsonObject fixture = TeblorOpeningHorizonReportTests.HandOver();
        AiOpening standard = FrenzyCastReportTests.Standard(data);
        Assert.Equal((string)fixture["displayName"]!, standard.DisplayName);
        Assert.Equal(fixture["buildOrder"]!.AsArray().Count, standard.BuildOrder.Length);
        Assert.True(data.FindStatus(BloodOilScenes.Status) >= 0, "not the report's BloodOilScenes copy");
        // The same file named absolutely is the same opening.
        (_, GameData again) = FrenzyCastReportTests.FileOpening(TeblorOpeningHorizonReportTests.HandOverPath)!.Value;
        Assert.Equal(data.ContentHash(), again.ContentHash());
    }

    [Fact]
    public void AMissingFile_FailsNamingIt()
    {
        var e = Assert.Throws<InvalidOperationException>(() => FrenzyCastReportTests.FileOpening("Content/Fixtures/no_such_opening.json"));
        Assert.Contains("no_such_opening.json", e.Message);
        Assert.Contains(FrenzyCastReportTests.OpeningVariable, e.Message);
    }

    [Theory]
    [InlineData("{ \"id\": \"standard\", ", "not valid JSON")]
    [InlineData("[ 1, 2 ]", "not one JSON object")]
    [InlineData("{ \"id\": \"standard\" }", "does not load")]
    public void AMalformedOpening_FailsNamingIt(string text, string why)
    {
        string path = Path.Combine(Path.GetTempPath(), $"rts-frenzy-opening-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, text);
        try
        {
            var e = Assert.Throws<InvalidOperationException>(() => FrenzyCastReportTests.FileOpening(path));
            Assert.Contains(path, e.Message);
            Assert.Contains(why, e.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AnOpeningTheLoaderRefuses_FailsNamingThePathAndTheError()
    {
        JsonObject bad = TeblorOpeningHorizonReportTests.HandOver();
        bad["buildOrder"]![0]!["id"] = "no_such_unit";
        string path = Path.Combine(Path.GetTempPath(), $"rts-frenzy-opening-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, bad.ToJsonString());
        try
        {
            var e = Assert.Throws<InvalidOperationException>(() => FrenzyCastReportTests.FileOpening(path));
            Assert.Contains(path, e.Message);
            Assert.Contains("no_such_unit", e.Message);
        }
        finally { File.Delete(path); }
    }

    /// <summary>The landing column's share: the shipped standard opening's own when it carries one, else D31's provisional fallback; the landing copy carries it.</summary>
    [Fact]
    public void TheLandingShare_IsTheShippedStandardsOwn_OrTheFallback()
    {
        float? shipped = FrenzyCastReportTests.Standard(TestSim.Data).ReserveArmyShare;
        Assert.Equal(shipped ?? FrenzyCastReportTests.FallbackLandingShare, FrenzyCastReportTests.LandingShare());
        GameData landing = FrenzyCastReportTests.LandingData();
        Assert.Equal(FrenzyCastReportTests.LandingShare(), FrenzyCastReportTests.Standard(landing).ReserveArmyShare);
        Assert.Equal(TeblorOpeningHorizonReportTests.HandOver()["buildOrder"]!.AsArray().Count, FrenzyCastReportTests.Standard(landing).BuildOrder.Length);
    }
}
