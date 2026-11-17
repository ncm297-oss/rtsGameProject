using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// M6-S10 criterion 11 (docs/03 "Implementation (M6-S10)"): the M5-5 criterion rows' maps re-measured on the final tree (the
/// Keep-corner cure and <c>gatherOverlap</c> 0.5), every check of <see cref="CriterionMatch"/> on, to 72,000 ticks: per row the
/// end tick, the winner, the defeats, waves and hunts, the refusals and any check error. Vathar Crossing seeds 1-8 for the
/// literal row's rule (the lowest seed that ends inside 24,000 with a winner and every check clean), Raraku seeds 1-8 (seed 1
/// no longer ends: BUG-1460), the generated map seeds 1-6 for its row's rule. A report: the rows' bounds live in <see cref="MatchCriterionTests"/>.
/// </summary>
[Trait("Category", "Stress")]
public class MatchRowsM6S10Report(ITestOutputHelper output)
{
    [Theory]
    [InlineData("vathar_crossing", 1UL, 2)]
    [InlineData("vathar_crossing", 2UL, 2)]
    [InlineData("vathar_crossing", 3UL, 2)]
    [InlineData("vathar_crossing", 4UL, 2)]
    [InlineData("vathar_crossing", 5UL, 2)]
    [InlineData("vathar_crossing", 6UL, 2)]
    [InlineData("vathar_crossing", 7UL, 2)]
    [InlineData("vathar_crossing", 8UL, 2)]
    [InlineData("raraku", 1UL, 2)]
    [InlineData("raraku", 2UL, 2)]
    [InlineData("raraku", 3UL, 2)]
    [InlineData("raraku", 4UL, 2)]
    [InlineData("raraku", 5UL, 2)]
    [InlineData("raraku", 6UL, 2)]
    [InlineData("raraku", 7UL, 2)]
    [InlineData("raraku", 8UL, 2)]
    [InlineData("pale_hills", 1UL, 2)]
    [InlineData("pale_hills", 2UL, 2)] // M6-S12: Pale Hills seeds 2-8 for the brief's seeds 1-8 table
    [InlineData("pale_hills", 3UL, 2)]
    [InlineData("pale_hills", 4UL, 2)]
    [InlineData("pale_hills", 5UL, 2)]
    [InlineData("pale_hills", 6UL, 2)]
    [InlineData("pale_hills", 7UL, 2)]
    [InlineData("pale_hills", 8UL, 2)]
    [InlineData("generated", 1UL, 2)]
    [InlineData("generated", 2UL, 2)]
    [InlineData("generated", 3UL, 2)]
    [InlineData("generated", 4UL, 2)]
    [InlineData("generated", 5UL, 2)]
    [InlineData("generated", 6UL, 2)]
    [InlineData("vathar_crossing", 2UL, 4)]
    [InlineData("vathar_crossing", 1UL, -4)]
    public void Row_Report(string map, ulong seed, int players)
    {
        int[]? teams = players < 0 ? new[] { 1, 2, 1, 2 } : null;
        int seats = Math.Abs(players);
        Simulation sim = CriterionMatch.Build(map, seed, seats, teams);
        CriterionMatch.Result r = CriterionMatch.Play(sim, 72_000, checks: true);
        output.WriteLine($"ROW {map} seed {seed}, {seats} seats{(teams == null ? "" : " 2v2")}: over {r.Over} at {r.EndTick} ({CriterionMatch.Clock(r.EndTick)}); winner {r.Winner}; "
            + $"defeated at {string.Join(" / ", r.DefeatedAt)}; waves {r.Waves}, hunts {r.Hunts}; refused: {r.RefusalText}; longest refusal run {r.LongestStreak}; "
            + $"error {(r.Error ?? "none")}; fold {r.Fold:X16}, end hash {r.EndHash:X16}");
    }
}
