using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>QA (M2-H1): TerrainHeight.At over every float special on both axes, the rimmed minimap dots (4 x 4 since M2-H2) against a naive reference renderer, edge-heavy dot refresh cost and allocation, whole-millisecond double-taps, and a hash twin for the touched ViewApi reads.</summary>
public class ViewHardeningQaTests
{
    private const float Cs = MapConstants.CellSize;
    private static readonly uint[] Colors = { 0x4B4F55, 0xC8892E, 0xFFFFFF, 0x000000 };
    private readonly ITestOutputHelper _out;

    public ViewHardeningQaTests(ITestOutputHelper output) => _out = output;

    private static readonly float[] Specials =
    {
        float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.MaxValue, float.MinValue,
        1e10f, -1e10f, 4.3e9f, -4.3e9f, 2.2e9f, float.Epsilon, -float.Epsilon, 0f, -0f, 1e-30f, -1e-30f,
    };

    // 3 x 3 map whose left column holds a ramp in the middle row (at the map's x edge), so At's
    // clamps reach a ramp cell through Unit() rather than only flat border cells.
    private static Heightmap EdgeRampMap()
    {
        const float hi = MapConstants.LevelHeight;
        byte[] levels = { 1, 1, 1, 0, 0, 0, 0, 0, 0 };
        float[] elev = { hi, hi, hi, hi / 2, 0, 0, 0, 0, 0 };
        return new Heightmap(3, 3, levels, elev);
    }

    public static IEnumerable<object[]> HeightMaps()
    {
        yield return new object[] { "edge-ramp 3x3", 0UL };
        yield return new object[] { "hand 4x4", 0UL };
        yield return new object[] { "generated", 3UL };
        yield return new object[] { "generated", 17UL };
    }

    private static Heightmap Map(string kind, ulong seed) => kind switch
    {
        "edge-ramp 3x3" => EdgeRampMap(),
        "hand 4x4" => Rts.Sim.Tests.ViewApi.TerrainHeightTests.HandMap(),
        _ => Rts.Sim.Tests.ViewApi.TerrainHeightTests.GeneratedMap(seed),
    };

    // Where a special coordinate must land: +big onto the far edge, -big / NaN / tiny onto 0 (floor).
    private static float Clamped(float v, float size)
    {
        if (float.IsNaN(v) || v <= 0f) return 0f;
        return MathF.Min(v, size);
    }

    [Theory]
    [MemberData(nameof(HeightMaps))]
    public void At_EverySpecialOnBothAxes_IsFiniteAndEqualsTheClampedPoint(string kind, ulong seed)
    {
        Heightmap map = Map(kind, seed);
        float w = map.Width * Cs, h = map.Height * Cs;
        Assert.True(map.IsRamp(0, 1) || kind != "edge-ramp 3x3", "edge-ramp map lost its ramp");
        float top = MapConstants.MaxLevel * MapConstants.LevelHeight;
        var others = new List<float>(Specials) { 0.5f * Cs, w * 0.5f, w, w - 1e-3f, h, 1.5f * Cs };
        int checks = 0;
        foreach (float a in Specials)
        {
            foreach (float b in others)
            {
                foreach ((float x, float y) in new[] { (a, b), (b, a) })
                {
                    float got = TerrainHeight.At(map, x, y);
                    Assert.True(float.IsFinite(got) && got >= 0f && got <= top, $"{kind}: At({x}, {y}) = {got}");
                    float want = TerrainHeight.At(map, Clamped(x, w), Clamped(y, h));
                    Assert.True(got == want, $"{kind}: At({x}, {y}) = {got}, but the clamped point gives {want}");
                    checks++;
                }
            }
        }
        _out.WriteLine($"{kind}: {checks} special pairs, all finite and equal to the clamped point");
    }

    [Fact]
    public void InCell_EveryCellOfAnEdgeRamp_EverySpecial_IsFiniteAndInsideTheCellsCorners()
    {
        Heightmap map = EdgeRampMap();
        Span<float> c = stackalloc float[4];
        for (int cy = 0; cy < map.Height; cy++)
            for (int cx = 0; cx < map.Width; cx++)
            {
                TerrainHeight.CellCorners(map, cx, cy, c);
                float lo = MathF.Min(MathF.Min(c[0], c[1]), MathF.Min(c[2], c[3]));
                float hi = MathF.Max(MathF.Max(c[0], c[1]), MathF.Max(c[2], c[3]));
                foreach (float x in Specials)
                    foreach (float y in Specials)
                    {
                        float v = TerrainHeight.InCell(map, cx, cy, x, y);
                        Assert.True(float.IsFinite(v) && v >= lo && v <= hi, $"InCell({cx}, {cy}, {x}, {y}) = {v}, corners {lo}..{hi}");
                    }
            }
    }

    // ---- minimap dots ----

    // The documented picture, drawn the slow way (M2-H2, BUG-0069): clear everything; every rim (the 4 x 4 block around
    // the 2 x 2 centre at the cell corner nearest the unit, clipped); then every 2 x 2 centre in slot order; then every
    // unit's own cell in slot order.
    private static byte[] Reference(int w, int h, uint[] colors, bool[] alive, Vector2[] pos, int[] owner, int maxDots, out int drawn)
    {
        var d = new byte[w * h * 4];
        var dots = new List<(int x, int y, int kx, int ky, int o)>();
        for (int s = 0; s < alive.Length && s < maxDots; s++)
        {
            if (!alive[s] || owner[s] < 0 || owner[s] >= colors.Length) continue;
            Vector2 p = pos[s];
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y)) continue;
            int x = (int)Math.Clamp(MathF.Floor(p.X / Cs), 0f, w - 1), y = (int)Math.Clamp(MathF.Floor(p.Y / Cs), 0f, h - 1);
            int kx = (int)Math.Clamp(MathF.Floor(p.X / Cs + 0.5f), 1f, Math.Max(1, w - 1)), ky = (int)Math.Clamp(MathF.Floor(p.Y / Cs + 0.5f), 1f, Math.Max(1, h - 1));
            dots.Add((x, y, kx, ky, owner[s]));
            uint rim = MinimapRaster.RimFor(colors[owner[s]]);
            for (int yy = ky - 2; yy <= ky + 1; yy++)
                for (int xx = kx - 2; xx <= kx + 1; xx++)
                    if (xx >= 0 && yy >= 0 && xx < w && yy < h) Put(d, (yy * w + xx) * 4, rim);
        }
        foreach ((int _, int _, int kx, int ky, int o) in dots)
            for (int yy = ky - 1; yy <= ky; yy++)
                for (int xx = kx - 1; xx <= kx; xx++)
                    if (xx < w && yy < h) Put(d, (yy * w + xx) * 4, colors[o]);
        foreach ((int x, int y, int _, int _, int o) in dots) Put(d, (y * w + x) * 4, colors[o]);
        drawn = dots.Count;
        return d;
    }

    private static void Put(byte[] d, int i, uint rgb)
    {
        d[i] = (byte)(rgb >> 16);
        d[i + 1] = (byte)(rgb >> 8);
        d[i + 2] = (byte)rgb;
        d[i + 3] = 255;
    }

    private static Heightmap Flat(int w, int h) => new(w, h, new byte[w * h], new float[w * h]);

    [Theory]
    [InlineData(1, 1, 11UL)]
    [InlineData(1, 7, 12UL)]
    [InlineData(2, 2, 13UL)]
    [InlineData(3, 3, 14UL)]
    [InlineData(5, 2, 15UL)]
    [InlineData(17, 9, 16UL)]
    [InlineData(64, 64, 17UL)]
    public void DrawDots_MatchesANaiveReferenceRenderer_OverManyRefreshes(int w, int h, ulong seed)
    {
        Heightmap map = Flat(w, h);
        const int slots = 48, maxDots = 40;
        var raster = new MinimapRaster(map, new NavGrid(map), Colors, maxDots);
        var rng = new SimRng(seed, RngStream.Combat);
        var alive = new bool[slots];
        var pos = new Vector2[slots];
        var owner = new int[slots];
        float mw = w * Cs, mh = h * Cs;
        for (int frame = 0; frame < 400; frame++)
        {
            // A fresh crowd most frames; sometimes nothing at all (every dot must clear).
            int live = frame % 37 == 0 ? 0 : rng.NextInt(0, slots + 1);
            for (int s = 0; s < slots; s++)
            {
                alive[s] = s < live && rng.NextInt(0, 8) != 0;
                owner[s] = rng.NextInt(0, 12) == 0 ? rng.NextInt(-2, 6) : rng.NextInt(0, Colors.Length);
                int pick = rng.NextInt(0, 10);
                pos[s] = pick switch
                {
                    0 => new Vector2(-5f, rng.NextFloat() * mh),                                   // off the left edge
                    1 => new Vector2(mw + 3f, mh),                                                 // past the far corner
                    2 => new Vector2(rng.NextInt(0, w) * Cs, (h - 1) * Cs + Cs * 0.999f),         // last row
                    3 => new Vector2(float.NaN, 1f),
                    4 => new Vector2(mw, rng.NextFloat() * mh),                                    // exactly the far edge
                    _ => new Vector2(rng.NextFloat() * mw, rng.NextFloat() * mh),
                };
            }
            int got = raster.DrawDots(alive, pos, owner);
            byte[] want = Reference(w, h, Colors, alive, pos, owner, maxDots, out int drawn);
            Assert.Equal(drawn, got);
            for (int i = 0; i < want.Length; i += 4)
            {
                Assert.True(want[i + 3] == raster.Dots[i + 3], $"{w}x{h} frame {frame}: pixel {i / 4} alpha {raster.Dots[i + 3]}, want {want[i + 3]}");
                if (want[i + 3] == 0) continue;
                for (int k = 0; k < 3; k++)
                    Assert.True(want[i + k] == raster.Dots[i + k], $"{w}x{h} frame {frame}: pixel ({i / 4 % w}, {i / 4 / w}) channel {k} is {raster.Dots[i + k]}, want {want[i + k]}");
            }
        }
    }

    [Fact]
    public void ThreeByThreeDots_AlwaysShowTheOwnerColourSomewhere_AndAnEnemyCentreIsNeverRecoloured()
    {
        // A crowd packed into one 3 x 3 block, both owners alternating: each live unit's own cell shows an owner colour, never a rim colour.
        Heightmap map = Flat(12, 12);
        var raster = new MinimapRaster(map, new NavGrid(map), Colors, 64);
        var alive = new bool[64];
        var pos = new Vector2[64];
        var owner = new int[64];
        for (int s = 0; s < 64; s++)
        {
            alive[s] = true;
            pos[s] = new Vector2((5 + s % 3 + 0.5f) * Cs, (5 + s / 3 % 3 + 0.5f) * Cs);
            owner[s] = s % 2;
        }
        raster.DrawDots(alive, pos, owner);
        for (int y = 5; y < 8; y++)
            for (int x = 5; x < 8; x++)
            {
                int i = (y * 12 + x) * 4;
                uint rgb = (uint)(raster.Dots[i] << 16 | raster.Dots[i + 1] << 8 | raster.Dots[i + 2]);
                Assert.True(rgb == Colors[0] || rgb == Colors[1], $"crowd cell ({x}, {y}) is {rgb:X6}, a rim colour over a unit");
            }
    }

    [Fact]
    public void RimFor_IsASteppedFunctionOfLuma_AndTheLightAndDarkRimsAreThemselvesClassifiedConsistently()
    {
        // Greys around the threshold: below picks the light rim, at or above the dark one.
        uint prev = MinimapRaster.LightRim;
        int switches = 0;
        for (uint g = 0; g <= 255; g++)
        {
            uint rim = MinimapRaster.RimFor(g << 16 | g << 8 | g);
            if (rim != prev) switches++;
            prev = rim;
        }
        Assert.Equal(1, switches);
        Assert.Equal(MinimapRaster.DarkRim, MinimapRaster.RimFor(MinimapRaster.LightRim));
        Assert.Equal(MinimapRaster.LightRim, MinimapRaster.RimFor(MinimapRaster.DarkRim));
    }

    [Fact]
    public void DrawDots_2000UnitsAllOnTheMapEdges_AllocatesZeroBytes()
    {
        Heightmap map = Rts.Sim.Tests.ViewApi.TerrainHeightTests.GeneratedMap(3);
        const int n = 2000;
        var raster = new MinimapRaster(map, new NavGrid(map), Colors, n);
        var alive = new bool[n];
        var pos = new Vector2[n];
        var owner = new int[n];
        float mw = map.Width * Cs, mh = map.Height * Cs;
        for (int i = 0; i < n; i++)
        {
            alive[i] = true;
            float t = i / (float)n;
            pos[i] = (i % 4) switch { 0 => new(t * mw, 0), 1 => new(t * mw, mh), 2 => new(0, t * mh), _ => new(mw, t * mh) };
            owner[i] = i % Colors.Length;
        }
        int count = 0;
        Action block = () => count += raster.DrawDots(alive, pos, owner);
        block();
        int runs = AllocationProbe.AssertZero(block, _out);
        _out.WriteLine($"2,000 edge dots: 0 bytes (runs {runs}), {count} drawn");
    }

    // ---- control groups ----

    [Fact]
    public void Tap_AgreesWithAnIntegerMillisecondModel_OverRandomClocks()
    {
        var rng = new SimRng(77, RngStream.Combat);
        int disagreements = 0, doubles = 0;
        for (int trial = 0; trial < 20000; trial++)
        {
            var groups = new ControlGroups(1);
            // Clock values up to ~3 years of uptime in ms, as Time.GetTicksMsec() / 1000.0 delivers them.
            ulong t = (ulong)rng.NextInt(0, int.MaxValue) * 47UL + (ulong)rng.NextInt(0, 1000);
            int lastGroup = -1;
            ulong lastMs = 0;
            bool armed = false;
            for (int k = 0; k < 6; k++)
            {
                int g = rng.NextInt(0, 3);
                bool want = armed && g == lastGroup && t - lastMs <= 300;
                bool got = groups.Tap(g, t / 1000.0);
                if (got != want) disagreements++;
                if (got) doubles++;
                armed = !want;
                lastGroup = g;
                lastMs = t;
                int gap = rng.NextInt(0, 3) switch { 0 => 300, 1 => 301, _ => rng.NextInt(0, 400) };
                t += (ulong)gap;
            }
        }
        _out.WriteLine($"20,000 tap sequences: {doubles} double-taps, {disagreements} disagreements with the whole-ms model");
        Assert.Equal(0, disagreements);
        Assert.True(doubles > 1000);
    }

    // ---- read-only proof ----

    [Theory]
    [InlineData(21UL)]
    [InlineData(22UL)]
    public void HeightAndRimmedDots_ReadEveryTick_LeaveTheHashEqualToABareTwin(ulong seed)
    {
        SimConfig cfg = TestSim.Config(seed, PlayerCount: 2, UnitCapacity: 600, CommandCapacity: 4096);
        var a = new Simulation(cfg);
        var b = new Simulation(cfg);
        World world = a.World;
        UnitStore u = world.Units;
        var raster = new MinimapRaster(world.Heightmap, world.NavGrid, Colors, u.Capacity);
        var groups = new ControlGroups(u.Capacity);
        GameData data = world.Data;
        for (int p = 0; p < 2; p++)
        {
            FactionDef f = data.Factions[p];
            float maxR = 0f;
            foreach (int t in f.Units) maxR = Math.Max(maxR, data.Units[t].Radius);
            Vector2[] spots = StartLayout.Block(world.NavGrid, 250, p == 0, maxR);
            for (int k = 0; k < spots.Length; k++)
            {
                Command c = Command.SpawnUnit(p, f.Units[k % f.Units.Length], spots[k]);
                a.Enqueue(c);
                b.Enqueue(c);
            }
        }
        var rng = new SimRng(seed, RngStream.Combat);
        float mw = world.Heightmap.Width * Cs, mh = world.Heightmap.Height * Cs;
        float sum = 0f;
        while (a.TickNumber < 600)
        {
            ulong before = a.StateHash();
            raster.DrawDots(u.Alive, u.Position, u.Owner);
            for (int i = 0; i < u.Capacity; i++) if (u.Alive[i]) sum += TerrainHeight.At(world.Heightmap, u.Position[i].X, u.Position[i].Y);
            foreach (float s in Specials) sum += TerrainHeight.At(world.Heightmap, s, -s);
            groups.Tap(rng.NextInt(0, 9), a.TickNumber * 0.05);
            Assert.Equal(before, a.StateHash());
            if (a.TickNumber % 40 == 0)
            {
                var target = new Vector2(rng.NextFloat() * mw, rng.NextFloat() * mh);
                for (int i = 0; i < u.Capacity; i += 7)
                {
                    if (!u.Alive[i] || u.Owner[i] != 0) continue;
                    Command m = Command.Move(0, new EntityHandle(i, u.Generation[i]), target);
                    a.Enqueue(m);
                    b.Enqueue(m);
                }
            }
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"diverged at tick {a.TickNumber}");
        }
        _out.WriteLine($"seed {seed}: 600 ticks, hash {a.StateHash():X16}, height checksum {sum}");
        Assert.True(float.IsFinite(sum));
    }

    /// <summary>Wall-clock tests; they run alone in <see cref="SerialCollection"/>.</summary>
    [Collection(SerialCollection.Name)]
    public class Serial
    {
        private readonly ITestOutputHelper _out;

        public Serial(ITestOutputHelper output) => _out = output;

        [Theory]
        [Trait("Category", "Perf")]
        [InlineData("edges")]
        [InlineData("pile")]
        [InlineData("spread")]
        public void DotRefresh_2000Units_WorstLayouts_On256Map_UnderTheM24Budget(string layout)
        {
            var rng0 = new SimRng(4, RngStream.MapGen);
            Heightmap map = MapGenerator.Generate(MapGenParams.Default with { Width = 256, Height = 256 }, ref rng0);
            var raster = new MinimapRaster(map, new NavGrid(map), Colors, 2000);
            var alive = new bool[2000];
            var pos = new Vector2[2000];
            var owner = new int[2000];
            var rng = new SimRng(8, RngStream.Combat);
            float m = 256 * Cs;
            Vector2 Place(int i) => layout switch
            {
                // Every dot on the map border: all take the clipped Block() path.
                "edges" => (i % 4) switch { 0 => new(rng.NextFloat() * m, 0), 1 => new(rng.NextFloat() * m, m), 2 => new(0, rng.NextFloat() * m), _ => new(m, rng.NextFloat() * m) },
                "pile" => new Vector2(100f + rng.NextFloat() * 4f, 100f + rng.NextFloat() * 4f),
                _ => new Vector2(rng.NextFloat() * m, rng.NextFloat() * m),
            };
            for (int i = 0; i < 2000; i++)
            {
                alive[i] = true;
                pos[i] = Place(i);
                owner[i] = i & 1;
            }
            for (int i = 0; i < 20; i++) raster.DrawDots(alive, pos, owner);
            double worst = 0, total = 0;
            const int runs = 300;
            var sw = new Stopwatch();
            for (int r = 0; r < runs; r++)
            {
                for (int i = 0; i < 2000; i += 97) pos[i] = Place(i);
                sw.Restart();
                raster.DrawDots(alive, pos, owner);
                sw.Stop();
                total += sw.Elapsed.TotalMilliseconds;
                worst = Math.Max(worst, sw.Elapsed.TotalMilliseconds);
            }
            _out.WriteLine($"{layout}: DrawDots 2,000 units avg {total / runs:F4} ms, worst {worst:F4} ms");
            // Every dot on the impassable border can't happen in play, so that layout only has to fit the whole 0.5 ms refresh budget
            // (measured 0.21 ms on the dev machine); the playable layouts keep M2-4's 0.25 ms QA limit.
            double limit = layout == "edges" ? 0.5 : 0.25;
            Assert.True(total / runs <= limit, $"{layout}: DrawDots avg {total / runs:F4} ms, over the {limit} ms limit");
        }
    }
}
