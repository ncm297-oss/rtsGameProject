using Rts.Sim.Determinism;
using Rts.Sim.Map;
using Rts.Sim.Tests.QA.Oracles;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA re-check of BUG-0015: the summed-area/overlap ramp checks and the NavGrid fast path must give
/// exactly what the pre-fix code gave (QA oracles copied from 4b204f6), on fuzzed params and on
/// adversarial heightmaps the generator never produces.
/// </summary>
public class MapGenEquivalenceQaTests
{
    private readonly ITestOutputHelper _out;

    public MapGenEquivalenceQaTests(ITestOutputHelper output) => _out = output;

    /// <summary>Random params biased toward edges: tiny and non-square maps, margin 1, ramp width 1, ramps as long as the map, crowding.</summary>
    private static MapGenParams? RandomParams(ref SimRng r)
    {
        int Pick(ref SimRng rr, int lo, int hi) => rr.NextInt(lo, hi + 1);
        int w = r.NextInt(0, 4) == 0 ? 16 + r.NextInt(0, 8) : Pick(ref r, 16, 160);
        int h = r.NextInt(0, 4) == 0 ? 16 + r.NextInt(0, 8) : Pick(ref r, 16, 160);
        int minDim = Math.Min(w, h);
        int margin = r.NextInt(0, 2) == 0 ? 1 : Pick(ref r, 1, Math.Max(1, minDim / 6));
        int interior = minDim - 2 * (1 + margin);
        int rampWidth = r.NextInt(0, 3) == 0 ? 1 : Pick(ref r, 1, Math.Max(1, minDim / 3));
        int rampLength = r.NextInt(0, 3) == 0 ? 3 : Pick(ref r, 3, Math.Max(3, minDim / 2));
        int minSide = Math.Max(3, rampWidth + 2);
        if (interior < minSide) return null;
        int l1Min = Pick(ref r, minSide, interior);
        int l1Max = Pick(ref r, l1Min, interior);
        int l2Min = Pick(ref r, minSide, Math.Max(minSide, l1Max));
        int l2Max = Pick(ref r, l2Min, l2Min + 20);
        var p = MapGenParams.Default with
        {
            Width = w,
            Height = h,
            EdgeMargin = margin,
            RampWidth = rampWidth,
            RampLength = rampLength,
            Level1Plateaus = Pick(ref r, 0, MapGenParams.MaxPlateaus),
            Level1MinSize = l1Min,
            Level1MaxSize = l1Max,
            Level2Plateaus = Pick(ref r, 0, MapGenParams.MaxPlateaus),
            Level2MinSize = l2Min,
            Level2MaxSize = l2Max,
            Level2Inset = Pick(ref r, 1, 3),
            RampsPerPlateau = Pick(ref r, 1, 16),
            RampTries = Pick(ref r, 1, 64),
            MinPassableFraction = r.NextInt(0, 3) == 0 ? 1f : r.NextFloat(),
            MaxAttempts = Pick(ref r, 1, MapGenParams.MaxMaxAttempts),
        };
        try
        {
            p.Validate();
            return p;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string? Diff(Heightmap a, Heightmap b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return $"size {a.Width}x{a.Height} vs {b.Width}x{b.Height}";
        for (int i = 0; i < a.Levels.Length; i++)
        {
            if (a.Levels[i] != b.Levels[i]) return $"level at {i % a.Width},{i / a.Width}";
            if (BitConverter.SingleToInt32Bits(a.Elevations[i]) != BitConverter.SingleToInt32Bits(b.Elevations[i]))
                return $"elevation at {i % a.Width},{i / a.Width}: {a.Elevations[i]} vs {b.Elevations[i]}";
        }
        return null;
    }

    private static string? NavDiff(Heightmap hm)
    {
        var fast = new NavGrid(hm);
        var slow = new PreBug0015NavGrid(hm);
        if (fast.PassableCount != slow.PassableCount) return $"PassableCount {fast.PassableCount} vs {slow.PassableCount}";
        for (int y = 0; y < hm.Height; y++)
        {
            for (int x = 0; x < hm.Width; x++)
            {
                if (fast.FlagsAt(x, y) != slow.FlagsAt(x, y)) return $"flags at {x},{y}: {fast.FlagsAt(x, y)} vs {slow.FlagsAt(x, y)}";
                if (fast.CostAt(x, y) != (slow.IsPassable(x, y) ? MapConstants.CostPassable : MapConstants.CostBlocked)) return $"cost at {x},{y}";
            }
        }
        return null;
    }

    /// <summary>Guards the oracle itself: it must reproduce the developer's hashes pinned from 4b204f6.</summary>
    [Theory]
    [MemberData(nameof(MapGeneratorTests.PinnedMaps), MemberType = typeof(MapGeneratorTests))]
    public void Oracle_ReproducesHashesPinnedFromBeforeTheFix(string name, MapGenParams p, ulong seed, ulong expected)
    {
        var rng = new SimRng(seed, RngStream.MapGen);
        Assert.True(PreBug0015MapGenerator.Generate(p, ref rng).ContentHash() == expected, $"{name} seed {seed}: oracle drifted from 4b204f6");
    }

    public static IEnumerable<object[]> FuzzBatches() => Enumerable.Range(0, 8).Select(i => new object[] { i });

    /// <summary>Every fuzzed param set x seeds: same map bits, same RNG state after, same nav flags as the pre-fix code.</summary>
    [Theory]
    [MemberData(nameof(FuzzBatches))]
    public void FuzzedParams_FastRampChecks_MatchPreFixGenerator(int batch)
    {
        var pr = new SimRng(0xB0150015UL + (ulong)batch, 99);
        int sets = 0, maps = 0, ramped = 0;
        while (sets < 40)
        {
            MapGenParams? p = RandomParams(ref pr);
            if (p is null) continue;
            sets++;
            for (ulong seed = 1; seed <= 6; seed++)
            {
                var r1 = new SimRng(seed * 7919, RngStream.MapGen);
                var r2 = r1;
                Heightmap fast = MapGenerator.Generate(p, ref r1);
                Heightmap slow = PreBug0015MapGenerator.Generate(p, ref r2);
                string ctx = $"batch {batch} set {sets} seed {seed}: {p}";
                Assert.True(Diff(fast, slow) is null, $"{ctx}: {Diff(fast, slow)}");
                Assert.True(r1.State == r2.State, $"{ctx}: RNG state differs after generation");
                Assert.True(NavDiff(fast) is null, $"{ctx}: {NavDiff(fast)}");
                maps++;
                for (int i = 0; i < fast.Elevations.Length; i++)
                {
                    if (fast.Elevations[i] > fast.Levels[i] * MapConstants.LevelHeight) { ramped++; break; }
                }
            }
        }
        _out.WriteLine($"batch {batch}: {sets} param sets, {maps} maps, {ramped} with ramps");
        Assert.True(ramped > maps / 4, $"only {ramped}/{maps} maps had ramps: the fuzz isn't exercising ramp placement");
    }

    public static IEnumerable<object[]> EdgeSets()
    {
        MapGenParams d = MapGenParams.Default;
        // Minimum map, margin 1: plateaus touch the border ring + 1, so ramps run to the map edge.
        yield return new object[] { "16x16 margin1", d with { Width = 16, Height = 16, EdgeMargin = 1, Level1Plateaus = 8, Level1MinSize = 3, Level1MaxSize = 12, Level2Plateaus = 8, Level2MinSize = 3, Level2MaxSize = 6, Level2Inset = 1, RampWidth = 1, RampLength = 3, RampsPerPlateau = 16, RampTries = 128 } };
        // Very non-square: catches a stride/width-height swap in the summed-area table.
        yield return new object[] { "16x400", d with { Width = 16, Height = 400, EdgeMargin = 1, Level1Plateaus = 32, Level1MinSize = 3, Level1MaxSize = 12, Level2Plateaus = 32, Level2MinSize = 3, Level2MaxSize = 8, Level2Inset = 1, RampWidth = 1, RampLength = 3, RampsPerPlateau = 16, RampTries = 128 } };
        yield return new object[] { "400x16", d with { Width = 400, Height = 16, EdgeMargin = 1, Level1Plateaus = 32, Level1MinSize = 3, Level1MaxSize = 12, Level2Plateaus = 32, Level2MinSize = 3, Level2MaxSize = 8, Level2Inset = 1, RampWidth = 1, RampLength = 3, RampsPerPlateau = 16, RampTries = 128 } };
        yield return new object[] { "200x37 wide ramps", d with { Width = 200, Height = 37, EdgeMargin = 1, Level1Plateaus = 32, Level1MinSize = 9, Level1MaxSize = 33, Level2Plateaus = 32, Level2MinSize = 9, Level2MaxSize = 20, Level2Inset = 1, RampWidth = 7, RampLength = 5, RampsPerPlateau = 16, RampTries = 128 } };
        // Ramp length near the map side: footprints mostly fail the border test, a few just fit.
        yield return new object[] { "long ramps 64", d with { Width = 64, Height = 64, EdgeMargin = 1, Level1Plateaus = 32, Level1MinSize = 3, Level1MaxSize = 20, Level2Plateaus = 32, Level2MinSize = 3, Level2MaxSize = 8, Level2Inset = 1, RampWidth = 1, RampLength = 25, RampsPerPlateau = 16, RampTries = 128 } };
        // Crowded small ramps: maximal neighbor-zone overlaps between placed ramps.
        yield return new object[] { "crowded 96", d with { Width = 96, Height = 96, EdgeMargin = 1, Level1Plateaus = 32, Level1MinSize = 3, Level1MaxSize = 30, Level2Plateaus = 32, Level2MinSize = 3, Level2MaxSize = 10, Level2Inset = 1, RampWidth = 1, RampLength = 3, RampsPerPlateau = 16, RampTries = 128 } };
        yield return new object[] { "default", d };
    }

    [Theory]
    [MemberData(nameof(EdgeSets))]
    public void EdgeParams_FastRampChecks_MatchPreFixGenerator(string name, MapGenParams p)
    {
        int ramps = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var r1 = new SimRng(seed, RngStream.MapGen);
            var r2 = r1;
            Heightmap fast = MapGenerator.Generate(p, ref r1);
            Heightmap slow = PreBug0015MapGenerator.Generate(p, ref r2);
            Assert.True(Diff(fast, slow) is null, $"{name} seed {seed}: {Diff(fast, slow)}");
            Assert.True(r1.State == r2.State, $"{name} seed {seed}: RNG state differs");
            Assert.True(NavDiff(fast) is null, $"{name} seed {seed}: {NavDiff(fast)}");
            for (int i = 0; i < fast.Elevations.Length; i++)
                if (fast.Elevations[i] > fast.Levels[i] * MapConstants.LevelHeight) ramps++;
        }
        _out.WriteLine($"{name}: {ramps} ramp cells over 40 seeds");
    }

    /// <summary>Random valid heightmaps (any level pattern, ramp heights anywhere, sizes 1 to 40): the NavGrid fast path and the unchecked Flood must match the old grid and never index out of range.</summary>
    [Fact]
    public void AdversarialHeightmaps_NavGrid_MatchesPreFixGrid()
    {
        var r = new SimRng(0xADE5UL, 3);
        int cases = 0;
        for (int iter = 0; iter < 3000; iter++)
        {
            int w = iter < 100 ? 1 + iter % 10 : r.NextInt(1, 41);
            int h = iter < 100 ? 1 + iter / 10 : r.NextInt(1, 41);
            int n = w * h;
            var levels = new byte[n];
            var elev = new float[n];
            int mode = r.NextInt(0, 4);
            for (int i = 0; i < n; i++)
            {
                byte lv = mode switch
                {
                    0 => 0,                                   // flat: all passable interior, one big flood
                    1 => (byte)r.NextInt(0, 3),               // noise: many pockets
                    2 => (byte)((i % w) * 3 / Math.Max(1, w)), // bands
                    _ => (byte)(r.NextInt(0, 8) == 0 ? 1 : 0), // sparse bumps
                };
                levels[i] = lv;
                float floor = lv * MapConstants.LevelHeight;
                elev[i] = lv < MapConstants.MaxLevel && r.NextInt(0, 5) == 0
                    ? floor + MapConstants.LevelHeight * (r.NextInt(1, 1000) / 1000f) // ramp height anywhere
                    : floor;
            }
            var hm = new Heightmap(w, h, levels, elev);
            string? d = NavDiff(hm);
            Assert.True(d is null, $"iter {iter} {w}x{h} mode {mode}: {d}");
            cases++;
        }
        _out.WriteLine($"{cases} heightmaps compared");
    }
}
