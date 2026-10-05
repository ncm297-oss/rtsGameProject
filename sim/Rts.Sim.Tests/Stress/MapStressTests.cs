using System.Diagnostics;
using Rts.Sim.Determinism;
using Rts.Sim.Map;
using Rts.Sim.Tests.QA;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>QA stress on the M1-3 terrain generator: wide seed sweeps, large maps, worst-case bounded work.</summary>
[Collection(SerialCollection.Name)]
public class MapStressTests
{
    private readonly ITestOutputHelper _out;

    public MapStressTests(ITestOutputHelper output) => _out = output;

    private static Heightmap Gen(ulong seed, MapGenParams p, out SimRng after)
    {
        var rng = new SimRng(seed, RngStream.MapGen);
        Heightmap hm = MapGenerator.Generate(p, ref rng);
        after = rng;
        return hm;
    }

    /// <summary>A 32 x 32 setup QA chose independently of the developer's.</summary>
    private static readonly MapGenParams Tiny = MapGenParams.Default with
    {
        Width = 32,
        Height = 32,
        EdgeMargin = 2,
        Level1Plateaus = 3,
        Level1MinSize = 6,
        Level1MaxSize = 14,
        Level2Plateaus = 2,
        Level2MinSize = 5,
        Level2MaxSize = 8,
        Level2Inset = 1,
    };

    /// <summary>The 1,000-seed sweeps (about 12 s each); tagged Perf so the quick loop skips them.</summary>
    [Theory]
    [Trait("Category", "Perf")]
    [InlineData("default", 0UL, 1000)]
    [InlineData("default-high", ulong.MaxValue - 999, 1000)]
    public void SeedSweep_Wide(string setup, ulong firstSeed, int count) =>
        SeedSweep_AllInvariantsHold_EveryMapHasAllLevels(setup, firstSeed, count);

    [Theory]
    [InlineData("default", 5000UL, 100)]
    [InlineData("default-high", ulong.MaxValue - 99, 100)]
    [InlineData("tiny", 0UL, 500)]
    [InlineData("128x64", 0UL, 200)]
    public void SeedSweep_AllInvariantsHold_EveryMapHasAllLevels(string setup, ulong firstSeed, int count)
    {
        MapGenParams p = setup switch
        {
            "tiny" => Tiny,
            "128x64" => MapGenParams.Default with { Width = 128, Height = 64, Level1MaxSize = 30 },
            _ => MapGenParams.Default,
        };
        int missingLevels = 0, failures = 0;
        double minFrac = 1, worstMs = 0, totalMs = 0;
        var hashes = new HashSet<ulong>();
        var firstErrors = new List<string>();
        for (int k = 0; k < count; k++)
        {
            ulong seed = unchecked(firstSeed + (ulong)k);
            var sw = Stopwatch.StartNew();
            Heightmap hm = Gen(seed, p, out _);
            sw.Stop();
            worstMs = Math.Max(worstMs, sw.Elapsed.TotalMilliseconds);
            totalMs += sw.Elapsed.TotalMilliseconds;
            var nav = new NavGrid(hm);
            hashes.Add(hm.ContentHash());
            minFrac = Math.Min(minFrac, (double)nav.PassableCount / (hm.Width * hm.Height));
            if (!MapQaChecker.AllLevelsPassable(nav)) missingLevels++;
            // Criterion 2 (all three levels) is promised for the default params only; smaller setups may
            // legitimately fall back to a layout missing a level, which is counted and reported.
            List<string> errors = MapQaChecker.Check(hm, nav, requireAllLevels: setup.StartsWith("default", StringComparison.Ordinal), cap: 3);
            if (errors.Count > 0)
            {
                failures++;
                if (firstErrors.Count < 10) firstErrors.Add($"seed {seed}: {string.Join("; ", errors)}");
            }
        }
        _out.WriteLine($"{setup}: {count} seeds, failures {failures}, missing-level maps {missingLevels}, distinct hashes {hashes.Count}, " +
            $"min passable {minFrac:P1}, mean {totalMs / count:F2} ms, worst {worstMs:F2} ms");
        Assert.True(failures == 0, string.Join("\n", firstErrors));
        if (setup.StartsWith("default", StringComparison.Ordinal)) Assert.Equal(0, missingLevels);
        Assert.Equal(count, hashes.Count);
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void Generate512x512_TimeAndMemory()
    {
        MapGenParams p = MapGenParams.Default with { Width = 512, Height = 512 };
        Gen(0, p, out _); // JIT warm-up
        double worst = 0;
        long worstBytes = 0;
        for (ulong seed = 1; seed <= 5; seed++)
        {
            Heightmap? hm = null;
            var sw = new Stopwatch();
            ulong s = seed;
            Action gen = () =>
            {
                sw.Start();
                hm = Gen(s, p, out _);
                sw.Stop();
            };
            long bytes = AllocationProbe.Measure(gen);
            worst = Math.Max(worst, sw.Elapsed.TotalMilliseconds);
            worstBytes = Math.Max(worstBytes, bytes);
            Heightmap built = hm!;
            Assert.Empty(MapQaChecker.Check(built, new NavGrid(built), requireAllLevels: false));
        }
        _out.WriteLine($"512x512: worst {worst:F1} ms, worst allocation {worstBytes / 1024.0 / 1024.0:F1} MiB");
        // 16x the default area; a linear generator stays well under 16 x 50 ms.
        Assert.True(worst < 800, $"512x512 generation took {worst:F1} ms");
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void WorstCaseValidParams_StillBounded()
    {
        // Every attempt rejected (100% passable is impossible), maximum attempts, ramps and tries,
        // on a 512 x 512 map (1024 x 1024 measured at ~30 s by QA on 2026-10-03): generation must still finish in bounded time.
        MapGenParams p = MapGenParams.Default with
        {
            Width = 512,
            Height = 512,
            Level1Plateaus = MapGenParams.MaxPlateaus,
            Level1MaxSize = 200,
            Level2Plateaus = MapGenParams.MaxPlateaus,
            RampsPerPlateau = 16,
            RampTries = MapGenParams.MaxRampTries,
            MinPassableFraction = 1f,
            MaxAttempts = MapGenParams.MaxMaxAttempts, // Validate's caps since BUG-0013 (QA measured the old 64/1024/64 caps at ~35 s)
        };
        var sw = Stopwatch.StartNew();
        Heightmap hm = Gen(11, p, out _);
        sw.Stop();
        _out.WriteLine($"512x512 worst case: {sw.Elapsed.TotalSeconds:F2} s, flat fallback {hm.Levels.IndexOfAnyExcept((byte)0) < 0}");
        Assert.True(sw.Elapsed.TotalSeconds < 3, $"took {sw.Elapsed.TotalSeconds:F1} s");
    }
}
