using System.Diagnostics;
using Rts.Sim.Determinism;
using Rts.Sim.Map;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>Terraced map generator (M1-3): invariants over many seeds, determinism, RNG isolation, bounded work.</summary>
public class MapGeneratorTests
{
    /// <summary>A 32 x 32 setup scaled down from the defaults.</summary>
    public static readonly MapGenParams Small = MapGenParams.Default with
    {
        Width = 32,
        Height = 32,
        EdgeMargin = 3,
        Level1Plateaus = 2,
        Level1MinSize = 8,
        Level1MaxSize = 16,
        Level2Plateaus = 1,
        Level2MinSize = 5,
        Level2MaxSize = 6,
        Level2Inset = 1,
    };

    private readonly ITestOutputHelper _out;

    public MapGeneratorTests(ITestOutputHelper output) => _out = output;

    private static Heightmap Gen(ulong seed, MapGenParams p)
    {
        var rng = new SimRng(seed, RngStream.MapGen);
        return MapGenerator.Generate(p, ref rng);
    }

    [Fact]
    public void ManySeeds_DefaultAndSmall_SatisfyLevelRampCliffAndConnectivityInvariants()
    {
        for (ulong seed = 0; seed < 200; seed++)
        {
            Heightmap hm = Gen(seed, MapGenParams.Default);
            var nav = new NavGrid(hm);
            string? bad = MapAssert.FindViolation(hm, nav, requireAllLevels: true);
            Assert.True(bad == null, $"128x128 seed {seed}: {bad}\n{MapAssert.Render(nav)}");
        }
        for (ulong seed = 0; seed < 100; seed++)
        {
            Heightmap hm = Gen(seed, Small);
            var nav = new NavGrid(hm);
            string? bad = MapAssert.FindViolation(hm, nav, requireAllLevels: false);
            Assert.True(bad == null, $"32x32 seed {seed}: {bad}\n{MapAssert.Render(nav)}");
        }
    }

    [Fact]
    public void DefaultMap_HasRampsAndCliffs()
    {
        NavGrid nav = new(Gen(1, MapGenParams.Default));
        int ramps = 0, cliffs = 0;
        for (int y = 0; y < nav.Height; y++)
        for (int x = 0; x < nav.Width; x++)
        {
            if ((nav.FlagsAt(x, y) & NavFlags.Ramp) != 0) ramps++;
            if ((nav.FlagsAt(x, y) & NavFlags.Cliff) != 0) cliffs++;
        }
        _out.WriteLine(MapAssert.Render(nav));
        Assert.True(ramps >= 2 * MapGenParams.Default.RampWidth * MapGenParams.Default.RampLength, $"{ramps} ramp cells");
        Assert.True(cliffs > 100, $"{cliffs} cliff cells");
    }

    [Fact]
    public void SameSeedAndParams_GiveIdenticalMaps()
    {
        foreach (MapGenParams p in new[] { MapGenParams.Default, Small })
        {
            for (ulong seed = 0; seed < 10; seed++)
            {
                Heightmap a = Gen(seed, p), b = Gen(seed, p);
                Assert.True(a.Levels.SequenceEqual(b.Levels));
                Assert.True(a.Elevations.SequenceEqual(b.Elevations));
                var na = new NavGrid(a);
                var nb = new NavGrid(b);
                for (int y = 0; y < p.Height; y++)
                for (int x = 0; x < p.Width; x++)
                    Assert.Equal(na.FlagsAt(x, y), nb.FlagsAt(x, y));
                Assert.Equal(a.ContentHash(), b.ContentHash());
            }
        }
    }

    [Fact]
    public void DifferentSeeds_GiveDifferentMaps()
    {
        var hashes = new HashSet<ulong>();
        for (ulong seed = 0; seed < 50; seed++)
            Assert.True(hashes.Add(Gen(seed, MapGenParams.Default).ContentHash()), $"seed {seed} repeats a map");
        // Not ulong.MaxValue: PCG seeding makes that stream seed 0's shifted by one draw, and the
        // extra first draw (0) is rejected by NextInt, so the map comes out identical to seed 0's.
        Assert.NotEqual(Gen(0, MapGenParams.Default).ContentHash(), Gen(1UL << 63, MapGenParams.Default).ContentHash());
    }

    [Fact]
    public void Generate_AdvancesTheCallersRng_ByRef()
    {
        var rng = new SimRng(5, RngStream.MapGen);
        ulong before = rng.State;
        Heightmap first = MapGenerator.Generate(MapGenParams.Default, ref rng);
        Assert.NotEqual(before, rng.State);
        Heightmap second = MapGenerator.Generate(MapGenParams.Default, ref rng); // continues the stream
        Assert.NotEqual(first.ContentHash(), second.ContentHash());
    }

    [Fact]
    public void World_MapGeneration_DrawsOnlyFromMapGenStream()
    {
        var bigMap = TestSim.Config(Seed: 77, PlayerCount: 2, UnitCapacity: 8, CommandCapacity: 8);
        var smallMap = bigMap with { Map = Small };
        var a = new World(bigMap);
        var b = new World(smallMap);
        foreach (int s in new[] { RngStream.Combat, RngStream.Ai(0), RngStream.Ai(1) })
        {
            var fresh = new SimRng(77, (ulong)s);
            uint expected = fresh.NextUInt();
            Assert.Equal(expected, a.Rng(s).NextUInt());
            Assert.Equal(expected, b.Rng(s).NextUInt());
        }
        Assert.NotEqual(new SimRng(77, RngStream.MapGen).State, a.Rng(RngStream.MapGen).State);
        Assert.NotEqual(a.Rng(RngStream.MapGen).State, b.Rng(RngStream.MapGen).State);
    }

    [Fact]
    public void World_ExposesGeneratedTerrain()
    {
        var config = TestSim.Config(Seed: 3, PlayerCount: 2, UnitCapacity: 8, CommandCapacity: 8);
        var world = new World(config);
        Assert.Equal(128, world.Heightmap.Width);
        Assert.Equal(128, world.NavGrid.Height);
        Assert.Equal(Gen(3, MapGenParams.Default).ContentHash(), world.Heightmap.ContentHash());
        Assert.Null(MapAssert.FindViolation(world.Heightmap, world.NavGrid, requireAllLevels: true));
    }

    [Fact]
    public void ImpossibleRequirement_StillTerminates_WithFlatFallback()
    {
        // No layout can be 100% passable (cliffs and the border), so every attempt is rejected.
        var p = MapGenParams.Default with { MinPassableFraction = 1f, MaxAttempts = 3 };
        var sw = Stopwatch.StartNew();
        Heightmap hm = Gen(9, p);
        sw.Stop();
        Assert.All(hm.Levels.ToArray(), l => Assert.Equal(0, l));
        Assert.Null(MapAssert.FindViolation(hm, new NavGrid(hm), requireAllLevels: false, minPassableFraction: 0.9f));
        Assert.True(sw.ElapsedMilliseconds < 2000);
    }

    [Fact]
    public void NoRoomForLevel2_StillValid_AndTerminates()
    {
        // Level-2 rectangles can never fit their parents; generation falls back to a two-level map.
        var p = MapGenParams.Default with { Level1MaxSize = 20, Level2MinSize = 18, Level2MaxSize = 18 };
        Heightmap hm = Gen(4, p);
        Assert.Null(MapAssert.FindViolation(hm, new NavGrid(hm), requireAllLevels: false));
        Assert.DoesNotContain((byte)2, hm.Levels.ToArray());
    }

    public static IEnumerable<object[]> BadParams() => new[]
    {
        new object[] { MapGenParams.Default with { Width = 8 } },
        new object[] { MapGenParams.Default with { Height = 5000 } },
        new object[] { MapGenParams.Default with { EdgeMargin = 0 } },
        new object[] { MapGenParams.Default with { RampWidth = 0 } },
        new object[] { MapGenParams.Default with { RampLength = 1 } }, // 4 m over 4 m is 45 degrees
        new object[] { MapGenParams.Default with { RampsPerPlateau = 0 } },
        new object[] { MapGenParams.Default with { RampTries = 0 } },
        new object[] { MapGenParams.Default with { Level1Plateaus = -1 } },
        new object[] { MapGenParams.Default with { Level1MinSize = 4 } }, // narrower than a ramp mouth + 2
        new object[] { MapGenParams.Default with { Level1MaxSize = 10 } }, // below the minimum
        new object[] { MapGenParams.Default with { Level1MaxSize = 200 } }, // bigger than the map
        new object[] { MapGenParams.Default with { Level2Inset = 0 } },
        new object[] { MapGenParams.Default with { Level2MaxSize = 3 } },
        new object[] { MapGenParams.Default with { MinPassableFraction = float.NaN } },
        new object[] { MapGenParams.Default with { MinPassableFraction = 1.5f } },
        new object[] { MapGenParams.Default with { MaxAttempts = 0 } },
        new object[] { MapGenParams.Default with { MaxAttempts = 1000 } },
        // BUG-0012: sizes above the smaller map side, including values whose arithmetic used to wrap.
        new object[] { MapGenParams.Default with { RampWidth = 129 } },
        new object[] { MapGenParams.Default with { RampWidth = int.MaxValue } },
        new object[] { MapGenParams.Default with { RampLength = 129 } },
        new object[] { MapGenParams.Default with { RampLength = int.MaxValue } },
        new object[] { MapGenParams.Default with { EdgeMargin = 129 } },
        new object[] { MapGenParams.Default with { EdgeMargin = int.MaxValue } },
        new object[] { MapGenParams.Default with { Level2Inset = 129 } },
        new object[] { MapGenParams.Default with { Level2Inset = int.MaxValue } },
        new object[] { MapGenParams.Default with { Width = 200, Height = 64, Level1MaxSize = 30, RampLength = 65 } }, // the smaller side counts
        // BUG-0013: caps that bound the worst-case generation time.
        new object[] { MapGenParams.Default with { RampTries = MapGenParams.MaxRampTries + 1 } },
        new object[] { MapGenParams.Default with { MaxAttempts = MapGenParams.MaxMaxAttempts + 1 } },
        new object[] { MapGenParams.Default with { Level1Plateaus = MapGenParams.MaxPlateaus + 1 } },
        new object[] { MapGenParams.Default with { Level2Plateaus = MapGenParams.MaxPlateaus + 1 } },
    };

    [Theory]
    [MemberData(nameof(BadParams))]
    public void Validate_RejectsOutOfRangeParams(MapGenParams p)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => p.Validate());
        var rng = new SimRng(1, RngStream.MapGen);
        Assert.Throws<ArgumentOutOfRangeException>(() => MapGenerator.Generate(p, ref rng));
        var config = TestSim.Config(1, 1, 1, 1) with { Map = p };
        Assert.Throws<ArgumentOutOfRangeException>(() => new World(config));
    }

    [Fact]
    public void Validate_AcceptsDefaultAndSmall()
    {
        MapGenParams.Default.Validate();
        Small.Validate();
    }

    [Fact]
    public void Validate_AcceptsEveryValueAtItsCap()
    {
        (MapGenParams.Default with
        {
            RampTries = MapGenParams.MaxRampTries,
            MaxAttempts = MapGenParams.MaxMaxAttempts,
            Level1Plateaus = MapGenParams.MaxPlateaus,
            Level2Plateaus = MapGenParams.MaxPlateaus,
            Level2Inset = 128,
        }).Validate();
        // RampLength and RampWidth at the map side: legal, they just never fit (no ramps placed).
        Heightmap hm = Gen(2, MapGenParams.Default with { Width = 16, Height = 16, EdgeMargin = 1, Level1MinSize = 3, Level1MaxSize = 12, Level2MinSize = 3, Level2MaxSize = 3, RampLength = 16, RampWidth = 1 });
        Assert.Null(MapAssert.FindViolation(hm, new NavGrid(hm), requireAllLevels: false, minPassableFraction: 0f));
    }

    /// <summary>The seeds QA's 2,000-seed sweep uses (Stress.MapStressTests.SeedSweep_Wide), checked against MapAssert's 30-degree step rule.</summary>
    [Theory]
    [Trait("Category", "Perf")]
    [InlineData(0UL)]
    [InlineData(ulong.MaxValue - 999)]
    public void ThousandSeedSweep_NoPassableStepSteeperThan30Degrees(ulong firstSeed)
    {
        for (int k = 0; k < 1000; k++)
        {
            ulong seed = unchecked(firstSeed + (ulong)k);
            Heightmap hm = Gen(seed, MapGenParams.Default);
            var nav = new NavGrid(hm);
            string? bad = MapAssert.FindViolation(hm, nav, requireAllLevels: true);
            Assert.True(bad == null, $"seed {seed}: {bad}");
        }
    }

    public static IEnumerable<object[]> PinnedMaps()
    {
        MapGenParams d = MapGenParams.Default;
        var sets = new (string Name, MapGenParams P, ulong[] Hashes)[]
        {
            ("default", d, new[] { 0xAD038364261B98E4UL, 0xB561BDC4866E54C1UL, 0x663A1A6C3EC016A5UL }),
            ("rampWidth6 long", d with { RampWidth = 6, RampLength = 9, Level1MinSize = 20, Level2MinSize = 9, Level2MaxSize = 16 },
                new[] { 0x03EDBEED582BB778UL, 0x51F4C9340F4D48E1UL, 0xE5B2F1A8AD76F1D1UL }),
            ("dense ramps", d with { Level1Plateaus = 20, RampsPerPlateau = 8, Level2Plateaus = 12 },
                new[] { 0xFD3C566A74011E9AUL, 0x7E173AA42A12B668UL, 0x82EAA235AF8E7B9DUL }),
            ("256 crowded", d with { Width = 256, Height = 256, Level1Plateaus = 32, Level2Plateaus = 32, RampsPerPlateau = 6 },
                new[] { 0xF285EC467DB6B3FBUL, 0x74F33719D1EDBC2AUL, 0x979A2DAE6271F7CEUL }),
            ("crowded small ramps", d with { Level1Plateaus = 32, Level2Plateaus = 32, RampsPerPlateau = 16, RampTries = 128, Level1MinSize = 5, Level1MaxSize = 20, Level2MinSize = 5, Level2MaxSize = 10, Level2Inset = 1 },
                new[] { 0x16065A92DA6D5FFAUL, 0xBDDFEBC999498947UL, 0x2958E202885AD5A3UL }),
        };
        ulong[] seeds = { 1, 7, 42 };
        foreach (var s in sets)
            for (int i = 0; i < seeds.Length; i++)
                yield return new object[] { s.Name, s.P, seeds[i], s.Hashes[i] };
    }

    /// <summary>BUG-0015 replaced the cell-by-cell ramp footprint scan with O(1) checks that must accept exactly the same tries.</summary>
    /// <remarks>
    /// Hashes come from the generator as it was before BUG-0015 (commit 4b204f6), which a scratch
    /// copy also matched cell for cell (levels, heights, nav flags, RNG state after) on 3,000+
    /// seeds over ten param sets. If a deliberate generator change moves them, regenerate and say why.
    /// </remarks>
    [Theory]
    [MemberData(nameof(PinnedMaps))]
    public void Generate_MatchesMapsFromBeforeTheFastRampChecks(string name, MapGenParams p, ulong seed, ulong expected)
    {
        Assert.True(Gen(seed, p).ContentHash() == expected, $"{name} seed {seed}: map changed");
    }

    /// <summary>BUG-0015: ramp size no longer multiplies the cost of a ramp try. These shapes took 18-67 s in Debug before.</summary>
    [Theory]
    [Trait("Category", "Perf")]
    [InlineData(20, 300)]
    [InlineData(100, 200)]
    [InlineData(200, 100)]
    public void WorstValidParams_1024Map_LargeRamps_UnderFiveSeconds(int rampWidth, int rampLength)
    {
        var p = MapGenParams.Default with
        {
            Width = 1024,
            Height = 1024,
            Level1Plateaus = MapGenParams.MaxPlateaus,
            Level1MinSize = rampWidth + 2,
            Level1MaxSize = 400,
            Level2Plateaus = MapGenParams.MaxPlateaus,
            Level2MinSize = rampWidth + 2,
            Level2MaxSize = 300,
            RampWidth = rampWidth,
            RampLength = rampLength,
            RampsPerPlateau = 16,
            RampTries = MapGenParams.MaxRampTries,
            MinPassableFraction = 1f, // unreachable, so every attempt runs
            MaxAttempts = MapGenParams.MaxMaxAttempts,
        };
        var sw = Stopwatch.StartNew();
        Heightmap hm = Gen(11, p);
        var nav = new NavGrid(hm);
        sw.Stop();
        _out.WriteLine($"{rampWidth} x {rampLength} ramps on 1024 x 1024: {sw.Elapsed.TotalSeconds:F2} s");
        Assert.True(sw.Elapsed.TotalSeconds < 5, $"took {sw.Elapsed.TotalSeconds:F1} s");
        Assert.Null(MapAssert.FindViolation(hm, nav, requireAllLevels: false, minPassableFraction: 0f));
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void DefaultGeneration_IsUnder50Ms()
    {
        Gen(0, MapGenParams.Default); // warm up the JIT
        double worst = 0;
        for (ulong seed = 1; seed <= 10; seed++)
        {
            var sw = Stopwatch.StartNew();
            Gen(seed, MapGenParams.Default);
            sw.Stop();
            worst = Math.Max(worst, sw.Elapsed.TotalMilliseconds);
        }
        _out.WriteLine($"worst 128x128 generation: {worst:F2} ms");
        Assert.True(worst < 50, $"generation took {worst:F2} ms");
    }
}
