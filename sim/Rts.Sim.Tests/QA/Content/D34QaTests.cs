using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA D34 (session 2026-11-18-0926). D34 shipped no data: the shipped hash is still D33's, the Teblor opening is still the
/// D33 fixture, and the developer's reason for not softening the tooltip (the description is part of the content hash, so a
/// text-only change moves it) holds. The proposed later text fits D21's 200.
/// </summary>
public class D34QaTests
{
    /// <summary>The text teblor.md's D34 Question 1 proposes for the next Teblor hash move.</summary>
    internal const string ProposedDescription =
        "War Lodge and Javelin Ground first, then Age II; four Warriors raid at 8 army pop, then Javelin Throwers and War-dogs join them; Shamans, Breakers, Destrier Riders and a second Longhouse may follow.";

    [Fact]
    public void TheShippedData_IsStillD33s()
    {
        Assert.Equal(D33QaTests.D33Hash, TestSim.Data.ContentHash());
        JsonObject shipped = TeblorOpeningVariantsReportTests.Shipped(), d33 = TeblorOpeningFixtureTests.D33Fixture();
        Assert.True(JsonNode.DeepEquals(shipped, d33), "the shipped Teblor opening is not the D33 fixture");
    }

    [Fact]
    public void ADescriptionOnlyEdit_MovesTheContentHash()
    {
        JsonObject o = TeblorOpeningVariantsReportTests.Shipped();
        Assert.Equal(D33QaTests.D33Hash, TeblorOpeningVariantsReportTests.DataWith(o).ContentHash());
        o["description"] = ProposedDescription;
        Assert.NotEqual(D33QaTests.D33Hash, TeblorOpeningVariantsReportTests.DataWith(o).ContentHash());
    }

    [Fact]
    public void TheProposedDescription_Is198Characters_AndThePageQuotesIt()
    {
        Assert.Equal(198, ProposedDescription.Length);
        string page = File.ReadAllText(Path.Combine(TestDataDir.RepoRoot(), "docs", "factions", "teblor.md")).Replace("\r\n", "\n");
        string flat = System.Text.RegularExpressions.Regex.Replace(page, @"\s*\n\s*", " ");
        Assert.Contains(ProposedDescription, flat);
    }
}
