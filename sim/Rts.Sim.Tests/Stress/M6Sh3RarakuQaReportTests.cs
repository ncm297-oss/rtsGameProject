using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA, M6-SH3 (session 2026-11-27-0817, QA focus 4; measure and report): Raraku has two starts (the config refuses four seats),
/// so its rows are Normal v Normal, seeds 1-8, the draw rule on, to 144,000 ticks, with <see cref="DrawStalledRowsReportTests"/>'
/// builder and player; one <c>QARRROW</c> line each. Run on the branch and on its base (copy the file) to find a match that ended
/// on the base and no longer ends. Nothing is asserted but that the match was played. Category Soak: run by name.
/// </summary>
public class M6Sh3RarakuQaReportTests
{
    private readonly ITestOutputHelper _out;

    public M6Sh3RarakuQaReportTests(ITestOutputHelper output) => _out = output;

    private static string Clock(int ticks) => $"{ticks / 1200}:{ticks / 20 % 60:D2}";

    [Theory]
    [Trait("Category", "Soak")]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    [InlineData(5UL)]
    [InlineData(6UL)]
    [InlineData(7UL)]
    [InlineData(8UL)]
    public void Raraku_TwoSeats_Report(ulong seed)
    {
        Simulation sim = DrawStalledRowsReportTests.Build("raraku", seed, 2, null);
        DrawStalledRowsReportTests.Outcome o = DrawStalledRowsReportTests.Play(sim, 144_000);
        string end = !o.Over ? $"NO END by {o.End}" : o.Drawn ? $"DRAW at {o.End} ({Clock(o.End)})" : $"WON by {o.Winner} at {o.End} ({Clock(o.End)})";
        _out.WriteLine($"QARRROW raraku 1v1 seed {seed}: {end}; first spent {(o.FirstSpent < 0 ? "never" : o.FirstSpent.ToString())}, restarts {o.Restarts}; hash {o.Hash:X16}");
        Assert.True(o.End > 0);
    }
}
