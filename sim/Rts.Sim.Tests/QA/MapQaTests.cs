using System.Numerics;
using Rts.Sim.Determinism;
using Rts.Sim.Map;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>QA attacks on the M1-3 terrain: named seeds, degenerate params, RNG isolation, coordinate edges, allocation.</summary>
public class MapQaTests
{
    private readonly ITestOutputHelper _out;

    public MapQaTests(ITestOutputHelper output) => _out = output;

    private static Heightmap Gen(ulong seed, MapGenParams p, out SimRng after)
    {
        var rng = new SimRng(seed, RngStream.MapGen);
        Heightmap hm = MapGenerator.Generate(p, ref rng);
        after = rng;
        return hm;
    }

    // ---------- named seeds ----------

    [Theory]
    [InlineData(0UL)]
    [InlineData(1UL)]
    [InlineData(42UL)]
    [InlineData(ulong.MaxValue)]
    [InlineData(ulong.MaxValue - 1)]
    [InlineData(0x8000_0000_0000_0000UL)]
    public void NamedSeed_DefaultMap_MeetsCriteria2To4(ulong seed)
    {
        Heightmap hm = Gen(seed, MapGenParams.Default, out _);
        var nav = new NavGrid(hm);
        List<string> errors = MapQaChecker.Check(hm, nav, requireAllLevels: true);
        Assert.True(errors.Count == 0, string.Join("\n", errors));
        Assert.True(MapQaChecker.AllLevelsPassable(nav), "a level has no passable cell");
    }

    [Fact(Skip = "BUG-0014: seed ulong.MaxValue gives seed 0's map (PCG seeding + Lemire rejection); un-skip when fixed")]
    public void SeedMaxValue_AndSeedZero_GiveDifferentMaps()
    {
        // Different seeds -> different maps (criterion 5). Seed ulong.MaxValue is the seed most likely
        // to collide with another through the PCG seeding arithmetic.
        ulong h0 = Gen(0, MapGenParams.Default, out _).ContentHash();
        ulong hMax = Gen(ulong.MaxValue, MapGenParams.Default, out _).ContentHash();
        Assert.NotEqual(h0, hMax);
    }

    // ---------- degenerate params: Validate() throws ArgumentOutOfRangeException, or a valid map comes out ----------

    public static IEnumerable<object[]> DegenerateParams()
    {
        MapGenParams d = MapGenParams.Default;
        var list = new List<(string, MapGenParams)>
        {
            ("1x1", d with { Width = 1, Height = 1 }),
            ("3x3", d with { Width = 3, Height = 3 }),
            ("8x8", d with { Width = 8, Height = 8 }),
            ("16x16", d with { Width = 16, Height = 16 }),
            ("128x64", d with { Width = 128, Height = 64 }),
            ("64x128", d with { Width = 64, Height = 128 }),
            ("512x512", d with { Width = 512, Height = 512 }),
            ("1024x16", d with { Width = 1024, Height = 16 }),
            ("0x0", d with { Width = 0, Height = 0 }),
            ("neg", d with { Width = -128, Height = -128 }),
            ("intmax", d with { Width = int.MaxValue, Height = int.MaxValue }),
            ("rampWidth=map", d with { RampWidth = 128 }),
            ("rampWidth>map", d with { RampWidth = 1000 }),
            ("rampLength=map", d with { RampLength = 128 }),
            ("rampLength=1000", d with { RampLength = 1000 }),
            ("rampLength=intmax-1", d with { RampLength = int.MaxValue - 1 }),
            ("plateaus 0/0", d with { Level1Plateaus = 0, Level2Plateaus = 0 }),
            ("plateaus 0/2", d with { Level1Plateaus = 0 }),
            ("plateaus 64/64", d with { Level1Plateaus = 64, Level2Plateaus = 64 }),
            ("plateaus huge", d with { Level1Plateaus = 1_000_000, Level2Plateaus = 1_000_000 }),
            ("plateaus intmax", d with { Level1Plateaus = int.MaxValue, Level2Plateaus = int.MaxValue }),
            ("edgeMargin 2^30", d with { EdgeMargin = 1 << 30 }),
            ("edgeMargin 57", d with { EdgeMargin = 57 }),
            ("l1 size = interior", d with { Level1MinSize = 114, Level1MaxSize = 114, EdgeMargin = 6 }),
            ("l2 inset 2^30", d with { Level2Inset = 1 << 30 }),
            ("l2 max intmax", d with { Level2MaxSize = int.MaxValue }),
            ("l2 min intmax", d with { Level2MinSize = int.MaxValue, Level2MaxSize = int.MaxValue }),
            ("l1 min intmax", d with { Level1MinSize = int.MaxValue, Level1MaxSize = int.MaxValue }),
            ("ramps 16 tries 1024", d with { RampsPerPlateau = 16, RampTries = 1024 }),
            ("minPassable 0", d with { MinPassableFraction = 0f }),
            ("minPassable 1", d with { MinPassableFraction = 1f, MaxAttempts = 2 }),
            ("minPassable -0", d with { MinPassableFraction = -0f }),
            ("minPassable inf", d with { MinPassableFraction = float.PositiveInfinity }),
            ("maxAttempts 1", d with { MaxAttempts = 1 }),
            ("maxAttempts 64", d with { MaxAttempts = 64 }),
        };
        foreach ((string name, MapGenParams p) in list) yield return new object[] { name, p };
    }

    /// <summary>Int-overflow params: Validate's arithmetic wraps, accepts them, and Generate then throws an index/RNG error.</summary>
    public static IEnumerable<object[]> OverflowParams()
    {
        MapGenParams d = MapGenParams.Default;
        yield return new object[] { "rampWidth=intmax", d with { RampWidth = int.MaxValue } };
        yield return new object[] { "rampLength=intmax", d with { RampLength = int.MaxValue } };
        yield return new object[] { "edgeMargin intmax", d with { EdgeMargin = int.MaxValue } };
        yield return new object[] { "l2 inset intmax", d with { Level2Inset = int.MaxValue } };
    }

    [Theory] // BUG-0012 fixed in M1-4a
    [MemberData(nameof(OverflowParams))]
    public Task OverflowParams_ValidateThrowsOrMapIsValid(string name, MapGenParams p) =>
        DegenerateParams_ValidateThrowsOrMapIsValid(name, p);

    [Theory] // BUG-0012 fixed in M1-4a
    [InlineData(65536, 65536)] // width * height wraps to 0, so empty arrays would pass the length check
    [InlineData(1 << 20, 1 << 12)]
    public void Heightmap_SizeOverflow_IsRejected(int w, int h)
    {
        Exception? ex = Record.Exception(() => new Heightmap(w, h, ReadOnlySpan<byte>.Empty, ReadOnlySpan<float>.Empty));
        Assert.True(ex is ArgumentException, $"{w} x {h} with empty arrays: {ex?.GetType().Name ?? "accepted"}");
    }

    [Theory]
    [MemberData(nameof(DegenerateParams))]
    public async Task DegenerateParams_ValidateThrowsOrMapIsValid(string name, MapGenParams p)
    {
        Exception? validateEx = Record.Exception(() => p.Validate());
        if (validateEx != null)
        {
            Assert.IsType<ArgumentOutOfRangeException>(validateEx);
            _out.WriteLine($"{name}: Validate rejected ({((ArgumentOutOfRangeException)validateEx).ParamName})");
            return;
        }

        // Validate accepted: generation must finish quickly and give a sound map.
        Heightmap? hm = null;
        Exception? genEx = null;
        var task = Task.Run(() =>
        {
            try
            {
                hm = Gen(7, p, out _);
            }
            catch (Exception e)
            {
                genEx = e;
            }
        });
        Assert.True(await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(20))) == task, $"{name}: generation did not finish in 20 s");
        Assert.True(genEx == null, $"{name}: Validate accepted but Generate threw {genEx}");
        var nav = new NavGrid(hm!);
        double minFrac = Math.Min(0.5, p.MinPassableFraction);
        List<string> errors = MapQaChecker.Check(hm!, nav, requireAllLevels: false, minPassableFraction: Math.Max(0, minFrac));
        Assert.True(errors.Count == 0, $"{name}:\n" + string.Join("\n", errors));
        _out.WriteLine($"{name}: valid map, passable {nav.PassableCount}/{nav.Width * nav.Height}, all levels {MapQaChecker.AllLevelsPassable(nav)}");
    }

    // ---------- determinism and RNG isolation ----------

    [Fact]
    public void MapGenDrawCount_IsStablePerSeed()
    {
        for (ulong seed = 0; seed < 50; seed++)
        {
            Heightmap a = Gen(seed, MapGenParams.Default, out SimRng ra);
            Heightmap b = Gen(seed, MapGenParams.Default, out SimRng rb);
            Assert.Equal(a.ContentHash(), b.ContentHash());
            Assert.Equal(ra.State, rb.State);
            Assert.True(a.Levels.SequenceEqual(b.Levels));
            Assert.True(a.Elevations.SequenceEqual(b.Elevations));
        }
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(1UL)]
    [InlineData(ulong.MaxValue)]
    public void OtherStreams_UntouchedByMapGeneration_AcrossMapSizes(ulong seed)
    {
        var baseCfg = new SimConfig(seed, PlayerCount: 4, UnitCapacity: 4, CommandCapacity: 4);
        var configs = new[]
        {
            baseCfg,
            baseCfg with { Map = MapGenParams.Default with { Width = 512, Height = 512 } },
            baseCfg with { Map = MapGenParams.Default with { Width = 128, Height = 64 } },
            baseCfg with { Map = MapGenParams.Default with { Level1Plateaus = 0, Level2Plateaus = 0 } },
        };
        int streams = RngStream.Count(4);
        for (int s = 0; s < streams; s++)
        {
            if (s == RngStream.MapGen) continue;
            var fresh = new SimRng(seed, (ulong)s);
            uint e1 = fresh.NextUInt(), e2 = fresh.NextUInt();
            foreach (SimConfig c in configs)
            {
                var w = new World(c);
                Assert.Equal(e1, w.Rng(s).NextUInt());
                Assert.Equal(e2, w.Rng(s).NextUInt());
            }
        }
    }

    [Fact]
    public void World_TerrainMatchesDirectGeneration_AndIsDeterministic()
    {
        var cfg = new SimConfig(123, 2, 4, 4);
        var a = new World(cfg);
        var b = new World(cfg);
        Heightmap direct = Gen(123, MapGenParams.Default, out SimRng after);
        Assert.Equal(direct.ContentHash(), a.Heightmap.ContentHash());
        Assert.Equal(a.Heightmap.ContentHash(), b.Heightmap.ContentHash());
        Assert.Equal(after.State, a.Rng(RngStream.MapGen).State);
        Assert.Equal(a.Heightmap.Width, a.NavGrid.Width);
        Assert.Equal(a.NavGrid.PassableCount, new NavGrid(a.Heightmap).PassableCount);
    }

    // ---------- coordinate edges ----------

    [Theory]
    [InlineData(256f, 0f, false, -1, -1)]
    [InlineData(0f, 256f, false, -1, -1)]
    [InlineData(255.99998f, 255.99998f, true, 127, 127)] // largest float below 256
    [InlineData(-0f, -0f, true, 0, 0)]
    [InlineData(-1e-38f, 0f, false, -1, -1)]             // tiny negative must not truncate to cell 0
    [InlineData(1e-45f, 1e-45f, true, 0, 0)]
    [InlineData(-2f, 0f, false, -1, -1)]
    [InlineData(-1.9999999f, 0f, false, -1, -1)]          // truncation would give 0
    [InlineData(127.99999f, 128f, true, 63, 64)]
    public void WorldToCell_Boundaries(float px, float py, bool inside, int cx, int cy)
    {
        NavGrid nav = DefaultNav;
        Assert.Equal(inside, nav.WorldToCell(new Vector2(px, py), out int x, out int y));
        Assert.Equal(cx, x);
        Assert.Equal(cy, y);
    }

    [Fact]
    public void WorldToCell_NonSquareMap_UsesEachAxisOwnSize()
    {
        Heightmap hm = Gen(3, MapGenParams.Default with { Width = 128, Height = 64 }, out _);
        var nav = new NavGrid(hm);
        Assert.True(nav.WorldToCell(new Vector2(200f, 127.9f), out int x, out int y));
        Assert.Equal((100, 63), (x, y));
        Assert.False(nav.WorldToCell(new Vector2(200f, 128f), out _, out _));
        Assert.True(nav.WorldToCell(new Vector2(255.9f, 1f), out x, out _));
        Assert.Equal(127, x);
    }

    [Fact]
    public void CellCenter_RoundTripsForEveryCell_On512Map()
    {
        Heightmap hm = Gen(5, MapGenParams.Default with { Width = 512, Height = 512 }, out _);
        var nav = new NavGrid(hm);
        for (int y = 0; y < nav.Height; y++)
        {
            for (int x = 0; x < nav.Width; x++)
            {
                Vector2 c = nav.CellCenter(x, y);
                Assert.True(nav.WorldToCell(c, out int rx, out int ry));
                if (rx != x || ry != y) Assert.Fail($"({x},{y}) -> {c} -> ({rx},{ry})");
            }
        }
    }

    [Fact]
    public void Queries_FuzzedInputs_NeverThrow_AndOutsideIsBlocked()
    {
        NavGrid nav = DefaultNav;
        var rng = new SimRng(99, 99);
        float[] specials = { float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.MaxValue, float.MinValue, -0f, 256f, 255.99998f, -1e-38f, float.Epsilon };
        for (int i = 0; i < 200_000; i++)
        {
            int x = (int)rng.NextUInt(), y = (int)rng.NextUInt();
            if (i % 2 == 0)
            {
                x = rng.NextInt(-3, 131);
                y = rng.NextInt(-3, 131);
            }
            bool inb = nav.InBounds(x, y);
            if (!inb)
            {
                Assert.False(nav.IsPassable(x, y));
                Assert.Equal(-1, nav.LevelAt(x, y));
                Assert.Equal(NavFlags.Blocked, nav.FlagsAt(x, y));
                Assert.Equal(MapConstants.CostBlocked, nav.CostAt(x, y));
            }
            float px = i % 5 == 0 ? specials[rng.NextInt(0, specials.Length)] : (rng.NextFloat() * 300f) - 22f;
            float py = i % 7 == 0 ? specials[rng.NextInt(0, specials.Length)] : (rng.NextFloat() * 300f) - 22f;
            if (nav.WorldToCell(new Vector2(px, py), out int cx, out int cy))
            {
                Assert.True(nav.InBounds(cx, cy), $"({px},{py}) -> ({cx},{cy}) out of bounds");
                Assert.True(px >= 0 && px < 256 && py >= 0 && py < 256, $"({px},{py}) accepted");
            }
            else
            {
                Assert.Equal((-1, -1), (cx, cy));
                Assert.False(px >= 0 && px < 256 && py >= 0 && py < 256, $"({px},{py}) rejected");
            }
        }
    }

    // ---------- allocation ----------

    [Fact]
    public void MillionQueries_IncludingNaNAndOutside_AllocateZeroBytes()
    {
        NavGrid nav = DefaultNav;
        Heightmap hm = DefaultHm;
        long sink = Run(nav, hm, 2_000);
        long before = GC.GetAllocatedBytesForCurrentThread();
        sink += Run(nav, hm, 1_000_000);
        long delta = GC.GetAllocatedBytesForCurrentThread() - before;
        _out.WriteLine($"allocated {delta} bytes over 1,000,000 query rounds (sink {sink})");
        Assert.Equal(0, delta);
    }

    private static readonly float[] Positions = { float.NaN, -0.5f, 256f, 128.3f, float.PositiveInfinity, 3f };

    private static long Run(NavGrid nav, Heightmap hm, int n)
    {
        long sink = 0;
        for (int i = 0; i < n; i++)
        {
            int x = (i * 7919) % 136 - 4, y = (i * 104729) % 136 - 4;
            if (nav.IsPassable(x, y)) sink++;
            sink += nav.LevelAt(x, y) + nav.CostAt(x, y) + (int)nav.FlagsAt(x, y);
            if (nav.InBounds(x, y))
            {
                sink += hm.LevelAt(x, y);
                sink += (long)hm.ElevationAt(x, y);
                if (hm.IsRamp(x, y)) sink++;
            }
            if (nav.WorldToCell(new Vector2(Positions[i % Positions.Length], Positions[(i / 6) % Positions.Length]), out int cx, out int cy)) sink += cx + cy;
            Vector2 c = nav.CellCenter(x, y);
            sink += (long)c.X;
        }
        return sink;
    }

    // ---------- design conformance ----------

    [Fact] // BUG-0011 fixed in M1-4a (ramp walls)
    public void RampSides_NoPassableStepSteeperThan30Degrees()
    {
        // docs/02 "Map and terrain": slopes steeper than 30 degrees are impassable. Between two passable
        // 4-neighbours the height change over one 2 m cell must stay within tan 30.
        Heightmap hm = DefaultHm;
        NavGrid nav = DefaultNav;
        float maxRise = MapConstants.MaxRampSlope * MapConstants.CellSize;
        var bad = new List<string>();
        for (int y = 0; y < hm.Height; y++)
        {
            for (int x = 0; x + 1 < hm.Width; x++)
            {
                foreach ((int nx, int ny) in new[] { (x + 1, y), (x, y + 1) })
                {
                    if (!nav.IsPassable(x, y) || !nav.IsPassable(nx, ny)) continue;
                    float d = Math.Abs(hm.ElevationAt(x, y) - hm.ElevationAt(nx, ny));
                    if (d > maxRise + 1e-4f && bad.Count < 10) bad.Add($"({x},{y})->({nx},{ny}) rise {d} m");
                }
            }
        }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    private static readonly Heightmap DefaultHm = Gen(1, MapGenParams.Default, out _);
    private static readonly NavGrid DefaultNav = new(DefaultHm);
}
