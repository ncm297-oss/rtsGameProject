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

    /// <summary>
    /// Every build kind (as many steps of each), research and expand of the opening shipped before D31 (D28's, now the record
    /// <see cref="TeblorOpeningVariantsReportTests.D28"/>) is still in the hand-over, and its compositions are D28's.
    /// </summary>
    [Fact]
    public void D29Fixture_KeepsEveryShippedStepAndComposition()
    {
        JsonObject shipped = TeblorOpeningVariantsReportTests.D28(), fixture = TeblorOpeningHorizonReportTests.HandOver();
        Assert.Equal(NonTrain(shipped), NonTrain(fixture));
        Assert.True(JsonNode.DeepEquals(shipped["compositions"], fixture["compositions"]));
    }

    /// <summary>The D28 record is what <c>ai.json</c> shipped before D31 (V0 of the reports): not V24, the Stables at 8:15.</summary>
    [Fact]
    public void TheD28Record_IsV0_NotTheHandOver()
    {
        JsonObject d28 = TeblorOpeningVariantsReportTests.D28();
        Assert.Equal("standard", (string)d28["id"]!);
        Assert.False(JsonNode.DeepEquals(d28["buildOrder"], TeblorOpeningHorizonReportTests.HandOver()["buildOrder"]));
        Assert.Contains(d28["buildOrder"]!.AsArray(), s => (string?)s!["id"] == "teblor_stables" && (int?)s["at"]!["time"] == 495);
        Assert.Null(d28["attack"]!["reserveArmyShare"]);
    }

    /// <summary>
    /// D32: the shipped <c>standard</c> opening is <c>Fixtures/teblor_standard_d32.json</c> field for field (its description
    /// included, BUG-1390's), so a one-sided edit of either fails by name: a description, a step moved, a share or a wave.
    /// D31's pin (the shipped opening is the hand-over V24 plus its own share 0.25) is now
    /// <see cref="TheD31Record_IsTheHandOver_PlusItsOwnShare025"/> on the record of what D31 shipped.
    /// </summary>
    [Fact]
    public void TheShippedOpening_IsTheD32Fixture()
    {
        Assert.Empty(ShippedVersusD32(TeblorOpeningVariantsReportTests.Shipped()));
        JsonObject words = TeblorOpeningVariantsReportTests.Shipped();
        words["description"] = (string)words["description"]! + " ";
        Assert.Contains("description differs from the D32 fixture", ShippedVersusD32(words));
        JsonObject moved = TeblorOpeningVariantsReportTests.Shipped();
        moved["buildOrder"]![16]!["at"] = new JsonObject { ["time"] = 420 };
        Assert.Contains("buildOrder differs from the D32 fixture", ShippedVersusD32(moved));
        JsonObject share = TeblorOpeningVariantsReportTests.Shipped();
        share["attack"]!["reserveArmyShare"] = 0.5;
        Assert.Contains("attack differs from the D32 fixture", ShippedVersusD32(share));
    }

    /// <summary>The D32 fixture is the variant <see cref="D32TeblorOpeningReportTests.ShippedVariant"/> measured (build order, compositions, wave and share), with BUG-1390's description.</summary>
    [Fact]
    public void TheD32Fixture_IsTheMeasuredVariant_WithBug1390sDescription()
    {
        JsonObject fixture = D32TeblorOpeningReportTests.D32();
        JsonObject measured = D32TeblorOpeningReportTests.AllVariants().Single(v => v.Name == D32TeblorOpeningReportTests.ShippedVariant).Opening;
        foreach (string field in new[] { "id", "displayName", "buildOrder", "compositions", "attack" })
            Assert.True(JsonNode.DeepEquals(measured[field], fixture[field]), $"{field} differs from {D32TeblorOpeningReportTests.ShippedVariant}");
        Assert.Equal(D32TeblorOpeningReportTests.Description, (string)fixture["description"]!);
    }

    /// <summary>D31's pin, kept on its record (<c>Fixtures/teblor_standard_d31.json</c>): what D31 shipped is the hand-over V24 field for field plus its own share 0.25.</summary>
    [Fact]
    public void TheD31Record_IsTheHandOver_PlusItsOwnShare025()
    {
        JsonObject d31 = D32TeblorOpeningReportTests.D31(), handOver = TeblorOpeningHorizonReportTests.HandOver();
        foreach (string field in new[] { "id", "displayName", "description", "buildOrder", "compositions" })
            Assert.True(JsonNode.DeepEquals(d31[field], handOver[field]), $"{field} differs from the hand-over");
        JsonObject attack = JsonNode.Parse(d31["attack"]!.ToJsonString())!.AsObject();
        Assert.Equal(0.25, (double)attack["reserveArmyShare"]!);
        attack.Remove("reserveArmyShare");
        Assert.True(JsonNode.DeepEquals(attack, handOver["attack"]));
    }

    private static List<string> ShippedVersusD32(JsonObject shipped)
    {
        var problems = new List<string>();
        JsonObject d32 = D32TeblorOpeningReportTests.D32();
        foreach (string field in new[] { "id", "displayName", "description", "buildOrder", "compositions", "attack" })
            if (!JsonNode.DeepEquals(shipped[field], d32[field])) problems.Add($"{field} differs from the D32 fixture");
        if (shipped.Count != d32.Count) problems.Add($"the shipped opening has {shipped.Count} fields, the D32 fixture {d32.Count}");
        return problems;
    }

    private static string NonTrain(JsonObject o) => string.Join(", ", o["buildOrder"]!.AsArray()
        .Where(s => (string)s!["do"]! != "train")
        .Select(s => $"{(string)s!["do"]!} {(string?)s["id"]}")
        .OrderBy(k => k, StringComparer.Ordinal));
}
