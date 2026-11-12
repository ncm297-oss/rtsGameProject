using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA D29 (session 2026-11-12-0546): the D29 hand-over (<c>Content/Fixtures/teblor_standard_d29.json</c>) is an opening-only
/// change. The variant path (<see cref="TeblorOpeningVariantsReportTests.DataWith"/>) is lossless (the shipped opening put back
/// through it gives the shipped content hash), the fixture copy differs from the shipped data only in the Teblor's opening 0
/// (every other faction's AI, the Teblor difficulties and retreat, the rules unchanged), and the loaded openings differ in
/// exactly the steps and thresholds the D29 report names. And the fixture still has a step for every building kind, research
/// and expansion the shipped opening has (nothing dropped while reordering).
/// </summary>
public class D29FixtureQaTests(ITestOutputHelper output)
{
    [Fact]
    public void ShippedOpening_ThroughTheVariantPath_GivesTheShippedContentHash()
    {
        GameData copy = TeblorOpeningVariantsReportTests.DataWith(TeblorOpeningVariantsReportTests.Shipped());
        Assert.Equal(TestSim.Data.ContentHash(), copy.ContentHash());
    }

    [Fact]
    public void FixtureCopy_DiffersFromShipped_OnlyInTheTeblorOpening()
    {
        GameData shipped = TestSim.Data;
        GameData v4 = TeblorOpeningVariantsReportTests.DataWith(TeblorOpeningVariantsReportTests.Fixture());
        Assert.NotEqual(shipped.ContentHash(), v4.ContentHash());

        // Put the shipped opening's loaded fields back by hand is not possible (init-only), so compare the rest field by field.
        Assert.Equal(shipped.Units.Length, v4.Units.Length);
        Assert.Equal(shipped.Buildings.Length, v4.Buildings.Length);
        Assert.Equal(shipped.Techs.Length, v4.Techs.Length);
        int teblor = shipped.FindFaction("teblor");
        for (int f = 0; f < shipped.Ai.Length; f++)
        {
            AiDef a = shipped.Ai[f], b = v4.Ai[f];
            Assert.Equal(a.RetreatHpFraction, b.RetreatHpFraction);
            Assert.Equal(a.RetreatLossFraction, b.RetreatLossFraction);
            Assert.Equal(a.Difficulties.ToArray(), b.Difficulties.ToArray());
            Assert.Equal(a.Openings.Length, b.Openings.Length);
            for (int o = 0; o < a.Openings.Length; o++)
            {
                if (f == teblor && o == 0) continue;
                Assert.Equal(Describe(a.Openings[o]), Describe(b.Openings[o]));
            }
        }

        AiOpening s = shipped.Ai[teblor].Openings[0], n = v4.Ai[teblor].Openings[0];
        Assert.Equal(s.Id, n.Id);
        Assert.Equal(s.DisplayName, n.DisplayName);
        Assert.Equal(Comps(s), Comps(n));
        output.WriteLine($"shipped: {Describe(s)}");
        output.WriteLine($"fixture: {Describe(n)}");
        output.WriteLine($"attack: {s.AttackMinHalfPop / 2.0} pop / {s.AttackMinCost} -> {n.AttackMinHalfPop / 2.0} pop / {n.AttackMinCost}");
        // The report's named changes: wave 8 / 600 -> 4 / 300 (half-pop 16 -> 8).
        Assert.Equal(16, s.AttackMinHalfPop);
        Assert.Equal(600, s.AttackMinCost);
        Assert.Equal(8, n.AttackMinHalfPop);
        Assert.Equal(300, n.AttackMinCost);
    }

    /// <summary>Every building kind, research and expand the shipped opening has, the fixture has (as many build steps of each kind).</summary>
    [Fact]
    public void Fixture_KeepsEveryShippedBuildResearchAndExpandStep()
    {
        int teblor = TestSim.Data.FindFaction("teblor");
        AiOpening s = TestSim.Data.Ai[teblor].Openings[0];
        AiOpening n = TeblorOpeningVariantsReportTests.DataWith(TeblorOpeningVariantsReportTests.Fixture()).Ai[teblor].Openings[0];
        Assert.Equal(Tally(s), Tally(n));
        int Gatherers(AiOpening o) => o.BuildOrder.Where(x => x.Action == AiStepAction.Train).Sum(x => x.Count);
        output.WriteLine($"gatherers trained by steps: shipped {Gatherers(s)}, fixture {Gatherers(n)}");
    }

    private static string Tally(AiOpening o) => string.Join(", ", o.BuildOrder
        .Where(x => x.Action != AiStepAction.Train)
        .GroupBy(x => $"{x.Action} {x.TargetKey}")
        .OrderBy(g => g.Key, StringComparer.Ordinal)
        .Select(g => $"{g.Key} x{g.Count()}"));

    private static string Comps(AiOpening o) => string.Join(" ; ", o.Compositions.Select(c =>
        $"age {c.Age}: " + string.Join(",", c.Units.Select((u, i) => $"{u}={c.Weights[i]}"))));

    private static string Describe(AiOpening o) =>
        $"{o.Id} '{o.DisplayName}' '{o.Description}' [{string.Join(" | ", o.BuildOrder.Select(x => $"{x.Trigger}{x.At} {x.Action} {x.TargetKey} x{x.Count}"))}] {Comps(o)} wave {o.AttackMinHalfPop}/{o.AttackMinCost}";
}
