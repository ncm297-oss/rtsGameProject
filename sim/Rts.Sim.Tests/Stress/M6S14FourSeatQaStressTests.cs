using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA, M6-S14 (session 2026-11-26-1847, QA focus 3; measure and report, rule 48): the four-seat Normal AI matches, free-for-all
/// and 1v1v2 (teams 0 / 1 / 0 / 1: team 0 is "no team", so seats 0 and 2 are each a side of their own against the team of
/// seats 1 and 3; M6-S15, BUG-1892: the rows were called "2v2"), on the shipped four-start maps (Pale Hills, Vathar Crossing; Raraku has two starts, so the config refuses four
/// seats there), seeds 1-8, the draw rule on, to 144,000 ticks, with
/// <see cref="DrawStalledRowsReportTests"/>' builder and player. Each row prints one <c>QA4ROW</c> line (end, winner or draw,
/// first spent tick, the end hash). Run on the branch and on its base to say which end, against the base. One class per seed so
/// xUnit runs the seeds side by side. Nothing is asserted but that the match was played. Category Soak (about 32 matches of
/// up to 144,000 ticks): run it by name at a pin move, e.g. `--filter FullyQualifiedName~M6S14FourSeatQaStress`.
/// </summary>
public abstract class M6S14FourSeatQaStressBase
{
    private readonly ITestOutputHelper _out;

    protected M6S14FourSeatQaStressBase(ITestOutputHelper output) => _out = output;

    protected abstract ulong Seed { get; }

    private const int Bound = 144_000;

    private static string Clock(int ticks) => $"{ticks / 1200}:{ticks / 20 % 60:D2}";

    [Theory]
    [Trait("Category", "Soak")] // 32 long matches: run on demand at a pin move, not in every Stress run
    [InlineData("pale_hills", false)]
    [InlineData("vathar_crossing", false)]
    [InlineData("pale_hills", true)]
    [InlineData("vathar_crossing", true)]
    public void FourSeats_Report(string map, bool oneVsOneVsTwo)
    {
        Simulation sim = DrawStalledRowsReportTests.Build(map, Seed, 4, oneVsOneVsTwo ? new[] { 0, 1, 0, 1 } : null);
        DrawStalledRowsReportTests.Outcome o = DrawStalledRowsReportTests.Play(sim, Bound);
        string end = !o.Over ? $"NO END by {o.End}" : o.Drawn ? $"DRAW at {o.End} ({Clock(o.End)})" : $"WON by {o.Winner} at {o.End} ({Clock(o.End)})";
        _out.WriteLine($"QA4ROW {map} {(oneVsOneVsTwo ? "1v1v2" : "ffa")} seed {Seed}: {end}; first spent {(o.FirstSpent < 0 ? "never" : o.FirstSpent.ToString())}, restarts {o.Restarts}; gold left on the map {sim.World.Resources.GoldLeft}; hash {o.Hash:X16}");
        Assert.True(o.End > 0);
    }
}

public class M6S14FourSeatQaStressSeed1(ITestOutputHelper o) : M6S14FourSeatQaStressBase(o) { protected override ulong Seed => 1; }

public class M6S14FourSeatQaStressSeed2(ITestOutputHelper o) : M6S14FourSeatQaStressBase(o) { protected override ulong Seed => 2; }

public class M6S14FourSeatQaStressSeed3(ITestOutputHelper o) : M6S14FourSeatQaStressBase(o) { protected override ulong Seed => 3; }

public class M6S14FourSeatQaStressSeed4(ITestOutputHelper o) : M6S14FourSeatQaStressBase(o) { protected override ulong Seed => 4; }

public class M6S14FourSeatQaStressSeed5(ITestOutputHelper o) : M6S14FourSeatQaStressBase(o) { protected override ulong Seed => 5; }

public class M6S14FourSeatQaStressSeed6(ITestOutputHelper o) : M6S14FourSeatQaStressBase(o) { protected override ulong Seed => 6; }

public class M6S14FourSeatQaStressSeed7(ITestOutputHelper o) : M6S14FourSeatQaStressBase(o) { protected override ulong Seed => 7; }

public class M6S14FourSeatQaStressSeed8(ITestOutputHelper o) : M6S14FourSeatQaStressBase(o) { protected override ulong Seed => 8; }
