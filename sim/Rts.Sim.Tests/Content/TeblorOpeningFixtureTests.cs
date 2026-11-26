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
    /// D34 (BUG-1641): the shipped <c>standard</c> opening is <c>Fixtures/teblor_standard_d34.json</c> field for field (its
    /// description included), so a one-sided edit of either fails by name: a description, a step moved, a share or a wave.
    /// Was <c>TheShippedOpening_IsTheD33Fixture</c> (D33: the shipped opening was <c>Fixtures/teblor_standard_d33.json</c>),
    /// now <see cref="TheD33Fixture_IsTheMeasuredVariant_WithD33sDescription"/> on the record of what D33 shipped, D34's M.
    /// D38 (BUG-1612; the row's name kept): the shipped opening is no longer the D34 fixture but D37's three fixtures
    /// (<see cref="D38TeblorOrders.Problems"/>: <c>buildOrder</c> Normal's, <c>buildOrders</c> Easy's and Hard's, every other
    /// field each fixture's); the D34 fixture stays as the record of what D34 shipped, the order the base played, and a
    /// one-sided edit of the shipped opening still fails by name.
    /// </summary>
    [Fact]
    public void TheShippedOpening_IsTheD34Fixture()
    {
        Assert.Empty(D38TeblorOrders.Problems(TeblorOpeningVariantsReportTests.Shipped()));
        JsonObject words = TeblorOpeningVariantsReportTests.Shipped();
        words["description"] = (string)words["description"]! + " ";
        Assert.Contains("description differs from the D37 easy fixture's", D38TeblorOrders.Problems(words));
        JsonObject moved = TeblorOpeningVariantsReportTests.Shipped();
        moved["buildOrder"]![16]!["at"] = new JsonObject { ["time"] = 420 };
        Assert.Contains("buildOrder differs from the D37 normal fixture", D38TeblorOrders.Problems(moved));
        JsonObject share = TeblorOpeningVariantsReportTests.Shipped();
        share["attack"]!["reserveArmyShare"] = 0.5;
        Assert.Contains("attack differs from the D37 hard fixture's", D38TeblorOrders.Problems(share));
        // The D34 fixture (what the base shipped) differs from the shipped opening in buildOrder and buildOrders only.
        Assert.Equal(new[] { "buildOrder differs from the D34 fixture", "the shipped opening has 7 fields, the D34 fixture 6" },
            ShippedVersus(TeblorOpeningVariantsReportTests.Shipped(), D34Fixture(), "D34"));
        JsonObject asBase = TeblorOpeningVariantsReportTests.Shipped();
        asBase.Remove("buildOrders");
        asBase["buildOrder"] = D34Fixture()["buildOrder"]!.DeepClone();
        Assert.Empty(ShippedVersus(asBase, D34Fixture(), "D34"));
        Assert.NotEmpty(ShippedVersus(TeblorOpeningVariantsReportTests.Shipped(), D33Fixture(), "D33"));
    }

    /// <summary>
    /// The D34 fixture is the variant <see cref="D34TeblorStepOrderReportTests.ShippedVariant"/> measured (build order,
    /// compositions, wave and share), with D34's description (the D33 text softened to what most Normal matches field).
    /// </summary>
    [Fact]
    public void TheD34Fixture_IsTheMeasuredVariant_WithD34sDescription()
    {
        JsonObject fixture = D34Fixture();
        Assert.Equal("Q2", D34TeblorStepOrderReportTests.ShippedVariant);
        JsonObject measured = D34TeblorStepOrderReportTests.AllVariants().Single(v => v.Name == D34TeblorStepOrderReportTests.ShippedVariant).Opening;
        foreach (string field in new[] { "id", "displayName", "buildOrder", "compositions", "attack" })
            Assert.True(JsonNode.DeepEquals(measured[field], fixture[field]), $"{field} differs from {D34TeblorStepOrderReportTests.ShippedVariant}");
        Assert.Equal(D34TeblorStepOrderReportTests.Description, (string)fixture["description"]!);
    }

    /// <summary>The opening D34 ships, read from <c>Fixtures/teblor_standard_d34.json</c>.</summary>
    internal static JsonObject D34Fixture() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.RepoRoot(), "sim", "Rts.Sim.Tests", "Content", "Fixtures", "teblor_standard_d34.json")))!.AsObject();

    /// <summary>The D33 fixture is the variant <see cref="D33BalanceReportTests.ShippedVariant"/> measured (build order, compositions, wave and share), with D33's description.</summary>
    [Fact]
    public void TheD33Fixture_IsTheMeasuredVariant_WithD33sDescription()
    {
        JsonObject fixture = D33Fixture();
        JsonObject measured = D33BalanceReportTests.Variant(D33BalanceReportTests.ShippedVariant);
        foreach (string field in new[] { "id", "displayName", "buildOrder", "compositions", "attack" })
            Assert.True(JsonNode.DeepEquals(measured[field], fixture[field]), $"{field} differs from {D33BalanceReportTests.ShippedVariant}");
        Assert.Equal(D33BalanceReportTests.Description, (string)fixture["description"]!);
    }

    /// <summary>The opening D33 ships, read from <c>Fixtures/teblor_standard_d33.json</c>.</summary>
    internal static JsonObject D33Fixture() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.RepoRoot(), "sim", "Rts.Sim.Tests", "Content", "Fixtures", "teblor_standard_d33.json")))!.AsObject();

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

    private static List<string> ShippedVersus(JsonObject shipped, JsonObject fixture, string name)
    {
        var problems = new List<string>();
        foreach (string field in new[] { "id", "displayName", "description", "buildOrder", "compositions", "attack" })
            if (!JsonNode.DeepEquals(shipped[field], fixture[field])) problems.Add($"{field} differs from the {name} fixture");
        if (shipped.Count != fixture.Count) problems.Add($"the shipped opening has {shipped.Count} fields, the {name} fixture {fixture.Count}");
        return problems;
    }

    private static string NonTrain(JsonObject o) => string.Join(", ", o["buildOrder"]!.AsArray()
        .Where(s => (string)s!["do"]! != "train")
        .Select(s => $"{(string)s!["do"]!} {(string?)s["id"]}")
        .OrderBy(k => k, StringComparer.Ordinal));
}
