using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D29: the opening D29 hands over for BUG-1241 (<c>Content/Fixtures/teblor_standard_d29.json</c>; since fix round 1, BUG-1300,
/// the variant <see cref="TeblorOpeningHorizonReportTests.HandOverVariant"/> measured by
/// <see cref="TeblorOpeningHorizonReportTests"/>) is ready to land: a copy of the shipped data whose Teblor <c>standard</c>
/// opening is replaced by it whole passes the loader's validation, and the loaded opening is the fixture's (same id, step
/// count, composition count and wave threshold). The file is the measured variant, step for step, and it keeps every build,
/// research and expand step of the shipped opening. It does not touch the shipped files.
/// </summary>
public class TeblorOpeningFixtureTests
{
    [Fact]
    public void D29Fixture_OnAShippedDataCopy_LoadsAndValidates()
    {
        JsonObject fixture = TeblorOpeningHorizonReportTests.HandOver();
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/teblor/ai.json", root => root["openings"]![0] = JsonNode.Parse(fixture.ToJsonString()));
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Errors.Count == 0, string.Join("; ", r.Errors));
        Assert.NotNull(r.Data);

        AiDef ai = r.Data!.Ai[r.Data.FindFaction("teblor")];
        Assert.Single(ai.Openings);
        AiOpening o = ai.Openings[0];
        Assert.Equal("standard", (string)fixture["id"]!);
        Assert.Equal((string)fixture["id"]!, o.Id);
        Assert.Equal((string)fixture["displayName"]!, o.DisplayName);
        Assert.Equal((string)fixture["description"]!, o.Description);
        Assert.Equal(fixture["buildOrder"]!.AsArray().Count, o.BuildOrder.Length);
        Assert.Equal(fixture["compositions"]!.AsArray().Count, o.Compositions.Length);
        Assert.Equal((int)Math.Round(2 * (double)fixture["attack"]!["minArmyPop"]!), o.AttackMinHalfPop);
        Assert.Equal((int)fixture["attack"]!["minArmyCost"]!, o.AttackMinCost);
    }

    /// <summary>The file is the variant the report measured (build order, compositions and wave; only the description differs).</summary>
    [Fact]
    public void D29Fixture_IsTheMeasuredVariant()
    {
        JsonObject fixture = TeblorOpeningHorizonReportTests.HandOver();
        JsonObject measured = TeblorOpeningHorizonReportTests.Variants().Single(v => v.Name == TeblorOpeningHorizonReportTests.HandOverVariant).Opening;
        foreach (string field in new[] { "id", "displayName", "buildOrder", "compositions", "attack" })
            Assert.True(JsonNode.DeepEquals(measured[field], fixture[field]), $"{field} differs from {TeblorOpeningHorizonReportTests.HandOverVariant}");
    }

    /// <summary>Every build kind (as many steps of each), research and expand of the shipped opening is still in the hand-over, and its compositions are the shipped ones.</summary>
    [Fact]
    public void D29Fixture_KeepsEveryShippedStepAndComposition()
    {
        JsonObject shipped = TeblorOpeningVariantsReportTests.Shipped(), fixture = TeblorOpeningHorizonReportTests.HandOver();
        Assert.Equal(NonTrain(shipped), NonTrain(fixture));
        Assert.True(JsonNode.DeepEquals(shipped["compositions"], fixture["compositions"]));
    }

    private static string NonTrain(JsonObject o) => string.Join(", ", o["buildOrder"]!.AsArray()
        .Where(s => (string)s!["do"]! != "train")
        .Select(s => $"{(string)s!["do"]!} {(string?)s["id"]}")
        .OrderBy(k => k, StringComparer.Ordinal));
}
