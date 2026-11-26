using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA D34 (session 2026-11-18-0926). As inspected, D34 shipped no data: the shipped hash was still D33's and the Teblor
/// opening the D33 fixture, and the description is part of the content hash, so a text-only change moves it. The proposed
/// later text fits D21's 200. D34 fix round 1 (BUG-1641): QA's order Q2 ships with that proposed text, so the first row now
/// pins the shipped state to D34's hash and fixture (it was <c>TheShippedData_IsStillD33s</c>), and the second reads D33's
/// opening from its fixture instead of the shipped file.
/// </summary>
public class D34QaTests
{
    /// <summary>The hash D34 states (fix round 1, BUG-1641): Q2's order and the softened description on D33's data.</summary>
    internal const ulong D34Hash = 0xA732F387C2BB7E4DUL;

    /// <summary>The text teblor.md's D34 Question 1 proposed for the next Teblor hash move; D34's fix round ships it.</summary>
    internal const string ProposedDescription =
        "War Lodge and Javelin Ground first, then Age II; four Warriors raid at 8 army pop, then Javelin Throwers and War-dogs join them; Shamans, Breakers, Destrier Riders and a second Longhouse may follow.";

    /// <summary>
    /// D34 fix round 1: was <c>TheShippedData_IsStillD33s</c> (the shipped hash D33's 318E8C4256456C05 and the opening the
    /// D33 fixture). The shipped data now hashes to <see cref="D34Hash"/>, its Teblor opening is the D34 fixture (QA's Q2 with
    /// the proposed description), and D33's opening on a shipped copy still hashes to D33's.
    /// </summary>
    [Fact]
    public void TheShippedData_IsD34s_QasQ2WithTheProposedText()
    {
        // M6-S13 (sim track, the hash move): the shipped data is D34's plus the draw block, so D34's hash is the shipped files
        // with that block out (was TestSim.Data's), and D33's the D33 fixture on that copy (was a shipped copy).
        // D38 (data track, the hash move): the shipped Teblor opening is D37's three orders (Normal's buildOrder, Easy's and
        // Hard's in buildOrders), so D34's state is the D34 fixture on the shipped files with the draw block out (was
        // DrawScenes.Off), and the shipped opening is the D34 fixture with buildOrder and buildOrders the only fields changed:
        // D34's text still ships.
        Assert.Equal(D34Hash, Draw.DrawScenes.TeblorOpeningBeforeDraw(TeblorOpeningFixtureTests.D34Fixture()).ContentHash());
        JsonObject shipped = TeblorOpeningVariantsReportTests.Shipped(), d34 = TeblorOpeningFixtureTests.D34Fixture();
        Assert.False(JsonNode.DeepEquals(shipped, d34), "the shipped Teblor opening is still the D34 fixture");
        JsonObject asD34 = JsonNode.Parse(shipped.ToJsonString())!.AsObject();
        asD34.Remove("buildOrders");
        asD34["buildOrder"] = d34["buildOrder"]!.DeepClone();
        Assert.True(JsonNode.DeepEquals(asD34, d34), "the shipped Teblor opening differs from the D34 fixture outside its build orders");
        Assert.Equal(ProposedDescription, (string)shipped["description"]!);
        JsonObject q2 = D34OrderQaStressTests.AllVariants().Single(v => v.Name == "Q2").Opening;
        foreach (string field in new[] { "id", "displayName", "buildOrder", "compositions", "attack" })
            Assert.True(JsonNode.DeepEquals(q2[field], d34[field]), $"{field}: the D34 fixture is not QA's Q2");
        Assert.False(JsonNode.DeepEquals(q2["buildOrder"], shipped["buildOrder"]), "the shipped buildOrder is still QA's Q2");
        Assert.Equal(D33QaTests.D33Hash, Draw.DrawScenes.TeblorOpeningBeforeDraw(TeblorOpeningFixtureTests.D33Fixture()).ContentHash());
    }

    [Fact]
    public void ADescriptionOnlyEdit_MovesTheContentHash()
    {
        // D34 fix round 1: reads D33's opening from its fixture (was the shipped file, which is D34's now).
        JsonObject o = TeblorOpeningFixtureTests.D33Fixture();
        // M6-S13 (sim track, the hash move): on the shipped files with the draw block out (D33's data had none).
        Assert.Equal(D33QaTests.D33Hash, Draw.DrawScenes.TeblorOpeningBeforeDraw(o).ContentHash());
        o["description"] = ProposedDescription;
        Assert.NotEqual(D33QaTests.D33Hash, Draw.DrawScenes.TeblorOpeningBeforeDraw(o).ContentHash());
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
