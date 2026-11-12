using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D29: the opening D29 hands over for BUG-1241 (<c>Content/Fixtures/teblor_standard_d29.json</c>, measured as V4 by
/// <see cref="TeblorOpeningVariantsReportTests"/>) is ready to land: a copy of the shipped data whose Teblor <c>standard</c>
/// opening is replaced by it whole passes the loader's validation, and the loaded opening is the fixture's (same id, step
/// count, composition count and wave threshold). It does not touch the shipped files.
/// </summary>
public class TeblorOpeningFixtureTests
{
    [Fact]
    public void D29Fixture_OnAShippedDataCopy_LoadsAndValidates()
    {
        JsonObject fixture = TeblorOpeningVariantsReportTests.Fixture();
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
}
