using System.Diagnostics;
using Rts.Sim.Determinism;
using Rts.Sim.Map;
using Rts.Sim.Tests.QA;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA re-check of BUG-0015: hunts for slow valid params beyond the shapes the developer timed
/// (many tiny ramps so the overlap list is long, mouth-wide ramps, ramps as long as the map,
/// non-square maps). Brief M1-4a: worst valid case ~2 s, fail above 5 s (Debug).
/// </summary>
public class MapGenWorstCaseHuntTests
{
    private readonly ITestOutputHelper _out;

    public MapGenWorstCaseHuntTests(ITestOutputHelper output) => _out = output;

    private static MapGenParams Base(int w, int h) => MapGenParams.Default with
    {
        Width = w,
        Height = h,
        EdgeMargin = 1,
        Level1Plateaus = MapGenParams.MaxPlateaus,
        Level2Plateaus = MapGenParams.MaxPlateaus,
        Level2Inset = 1,
        RampsPerPlateau = 16,
        RampTries = MapGenParams.MaxRampTries,
        MinPassableFraction = 1f, // unreachable, so every attempt runs
        MaxAttempts = MapGenParams.MaxMaxAttempts,
    };

    public static IEnumerable<object[]> Shapes()
    {
        // Many small plateaus, width-1 ramps: maximizes ramps placed, so the overlap scan is longest.
        yield return new object[] { "many tiny ramps", Base(1024, 1024) with { RampWidth = 1, RampLength = 3, Level1MinSize = 3, Level1MaxSize = 40, Level2MinSize = 3, Level2MaxSize = 20 } };
        // Huge plateaus, tiny ramps: tries mostly pass the O(1) tests and reach the mouth/overlap loops.
        yield return new object[] { "huge plateaus tiny ramps", Base(1024, 1024) with { RampWidth = 1, RampLength = 3, Level1MinSize = 900, Level1MaxSize = 1020, Level2MinSize = 3, Level2MaxSize = 1000 } };
        // Mouth as wide as Validate allows: the per-try mouth loop is ~1,000 cells.
        yield return new object[] { "max-width mouth", Base(1024, 1024) with { RampWidth = 1000, RampLength = 3, Level1MinSize = 1002, Level1MaxSize = 1020, Level2MinSize = 1002, Level2MaxSize = 1020 } };
        yield return new object[] { "half-width mouth", Base(1024, 1024) with { RampWidth = 400, RampLength = 3, Level1MinSize = 402, Level1MaxSize = 700, Level2MinSize = 402, Level2MaxSize = 700 } };
        // Ramp as long as the map side.
        yield return new object[] { "max-length ramp", Base(1024, 1024) with { RampWidth = 1, RampLength = 1024, Level1MinSize = 3, Level1MaxSize = 200, Level2MinSize = 3, Level2MaxSize = 100 } };
        // Non-square extremes.
        yield return new object[] { "1024x16", Base(1024, 16) with { RampWidth = 1, RampLength = 3, Level1MinSize = 3, Level1MaxSize = 12, Level2MinSize = 3, Level2MaxSize = 8 } };
        yield return new object[] { "16x1024", Base(16, 1024) with { RampWidth = 1, RampLength = 3, Level1MinSize = 3, Level1MaxSize = 12, Level2MinSize = 3, Level2MaxSize = 8 } };
        // Developer-style shape with midsize everything, as a control.
        yield return new object[] { "mid 10x40", Base(1024, 1024) with { RampWidth = 10, RampLength = 40, Level1MinSize = 12, Level1MaxSize = 400, Level2MinSize = 12, Level2MaxSize = 300 } };
    }

    [Theory]
    [Trait("Category", "Perf")]
    [MemberData(nameof(Shapes))]
    public void WorstCaseHunt_1024Maps_UnderFiveSeconds(string name, MapGenParams p)
    {
        p.Validate();
        double worst = 0;
        foreach (ulong seed in new ulong[] { 3, 11 })
        {
            var rng = new SimRng(seed, RngStream.MapGen);
            var sw = Stopwatch.StartNew();
            Heightmap hm = MapGenerator.Generate(p, ref rng);
            var nav = new NavGrid(hm);
            sw.Stop();
            worst = Math.Max(worst, sw.Elapsed.TotalSeconds);
            Assert.True(MapQaChecker.Check(hm, nav, requireAllLevels: false, minPassableFraction: 0).Count == 0, $"{name} seed {seed}: invalid map");
        }
        _out.WriteLine($"{name}: worst {worst:F2} s");
        Assert.True(worst < 5, $"{name}: took {worst:F1} s");
    }
}
