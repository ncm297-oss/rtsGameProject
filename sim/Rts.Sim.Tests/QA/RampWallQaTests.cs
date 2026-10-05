using System.Diagnostics;
using System.Reflection;
using Rts.Sim.Determinism;
using Rts.Sim.Map;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>QA attacks on M1-4a terrain fixes: ramp walls (BUG-0011), param overflow (BUG-0012), worst-case time (BUG-0013).</summary>
public class RampWallQaTests
{
    private readonly ITestOutputHelper _out;

    public RampWallQaTests(ITestOutputHelper output) => _out = output;

    private static Heightmap Gen(ulong seed, MapGenParams p)
    {
        var rng = new SimRng(seed, RngStream.MapGen);
        return MapGenerator.Generate(p, ref rng);
    }

    /// <summary>docs/02: nothing passable steeper than 30 degrees. 4-neighbours over 2 m; diagonals (only when both side cells are open) over 2.83 m.</summary>
    public static List<string> StepViolations(Heightmap hm, NavGrid nav, int cap = 10)
    {
        var bad = new List<string>();
        float max4 = MapConstants.MaxRampSlope * MapConstants.CellSize + 1e-4f;
        float maxDiag = MapConstants.MaxRampSlope * MapConstants.CellSize * MathF.Sqrt(2f) + 1e-4f;
        for (int y = 0; y < hm.Height; y++)
        {
            for (int x = 0; x < hm.Width; x++)
            {
                if (!nav.IsPassable(x, y)) continue;
                float e = hm.ElevationAt(x, y);
                foreach ((int dx, int dy) in new[] { (1, 0), (0, 1) })
                {
                    int nx = x + dx, ny = y + dy;
                    if (!nav.IsPassable(nx, ny)) continue;
                    float d = Math.Abs(hm.ElevationAt(nx, ny) - e);
                    if (d > max4 && bad.Count < cap) bad.Add($"({x},{y})->({nx},{ny}) rise {d:F3} m");
                }
                foreach ((int dx, int dy) in new[] { (1, 1), (1, -1) })
                {
                    int nx = x + dx, ny = y + dy;
                    if (!nav.IsPassable(nx, ny) || !nav.IsPassable(x + dx, y) || !nav.IsPassable(x, y + dy)) continue;
                    float d = Math.Abs(hm.ElevationAt(nx, ny) - e);
                    if (d > maxDiag && bad.Count < cap) bad.Add($"({x},{y})->({nx},{ny}) diagonal rise {d:F3} m");
                }
            }
        }
        return bad;
    }

    public static IEnumerable<object[]> SweepSets()
    {
        MapGenParams d = MapGenParams.Default;
        yield return new object[] { "default", d, 300 };
        yield return new object[] { "tiny32", d with { Width = 32, Height = 32, EdgeMargin = 2, Level1Plateaus = 3, Level1MinSize = 6, Level1MaxSize = 14, Level2Plateaus = 2, Level2MinSize = 5, Level2MaxSize = 8, Level2Inset = 1 }, 300 };
        yield return new object[] { "rampWidth1", d with { RampWidth = 1 }, 300 };
        yield return new object[] { "rampWidth6 long", d with { RampWidth = 6, RampLength = 9, Level1MinSize = 20, Level2MinSize = 9, Level2MaxSize = 16 }, 200 };
        yield return new object[] { "dense ramps", d with { Level1Plateaus = 20, RampsPerPlateau = 8, Level2Plateaus = 12 }, 200 };
        yield return new object[] { "rect 160x48", d with { Width = 160, Height = 48, EdgeMargin = 2, Level1MaxSize = 30, Level1MinSize = 10, Level2MinSize = 5, Level2MaxSize = 8 }, 200 };
        yield return new object[] { "256 crowded", d with { Width = 256, Height = 256, Level1Plateaus = 32, Level2Plateaus = 32, RampsPerPlateau = 6 }, 40 };
    }

    [Theory]
    [MemberData(nameof(SweepSets))]
    public void Sweep_NoSteepPassableStep_InvariantsHold(string name, MapGenParams p, int seeds)
    {
        int failed = 0;
        var firstErrors = new List<string>();
        for (ulong seed = 0; seed < (ulong)seeds; seed++)
        {
            Heightmap hm = Gen(seed, p);
            var nav = new NavGrid(hm);
            List<string> errors = StepViolations(hm, nav);
            errors.AddRange(MapQaChecker.Check(hm, nav, requireAllLevels: false, minPassableFraction: p.MinPassableFraction));
            if (errors.Count > 0)
            {
                failed++;
                if (firstErrors.Count < 6) firstErrors.Add($"seed {seed}: {string.Join("; ", errors.Take(3))}");
            }
        }
        _out.WriteLine($"{name}: {seeds - failed}/{seeds} seeds clean");
        Assert.True(failed == 0, $"{name}: {failed}/{seeds} seeds broken\n{string.Join("\n", firstErrors)}");
    }

    // ---------- BUG-0012: every int field at extreme values ----------

    public static IEnumerable<object[]> ExtremeFieldValues()
    {
        foreach (PropertyInfo prop in typeof(MapGenParams).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.PropertyType == typeof(int))
                foreach (int v in new[] { int.MinValue, -1, 0, 1, int.MaxValue, int.MaxValue - 1, 1 << 30 })
                    yield return new object[] { prop.Name, (double)v };
            else if (prop.PropertyType == typeof(float))
                foreach (float v in new[] { float.NaN, float.NegativeInfinity, float.PositiveInfinity, -1f, float.Epsilon, 2f })
                    yield return new object[] { prop.Name, (double)v };
        }
    }

    [Theory]
    [MemberData(nameof(ExtremeFieldValues))]
    public async Task EveryField_AtExtremes_ValidateThrowsOrMapIsValid(string field, double value)
    {
        MapGenParams p = MapGenParams.Default with { };
        PropertyInfo prop = typeof(MapGenParams).GetProperty(field)!;
        object boxed = prop.PropertyType == typeof(int) ? (object)(int)value : (object)(float)value;
        prop.SetValue(p, boxed); // init-only setter is still callable by reflection on our private copy
        Exception? ex = Record.Exception(() => p.Validate());
        if (ex != null)
        {
            Assert.IsType<ArgumentOutOfRangeException>(ex);
            return;
        }
        Heightmap? hm = null;
        Exception? genEx = null;
        var task = Task.Run(() =>
        {
            try { hm = Gen(5, p); }
            catch (Exception e) { genEx = e; }
        });
        Assert.True(await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10))) == task, $"{field}={value}: generation hung");
        Assert.True(genEx == null, $"{field}={value}: Validate accepted but Generate threw {genEx}");
        var nav = new NavGrid(hm!);
        var errors = StepViolations(hm!, nav);
        errors.AddRange(MapQaChecker.Check(hm!, nav, requireAllLevels: false, minPassableFraction: Math.Clamp(p.MinPassableFraction, 0f, 0.5f)));
        Assert.True(errors.Count == 0, $"{field}={value}:\n{string.Join("\n", errors)}");
    }

    // ---------- BUG-0013: worst valid case at the new caps ----------

    /// <summary>Valid params at every BUG-0013 cap on the largest map; "bigRamps" adds wide, long ramps (still within Validate).</summary>
    private static MapGenParams Worst(bool bigRamps)
    {
        var p = MapGenParams.Default with
        {
            Width = 1024,
            Height = 1024,
            Level1Plateaus = MapGenParams.MaxPlateaus,
            Level1MaxSize = 400,
            Level2Plateaus = MapGenParams.MaxPlateaus,
            Level2MaxSize = 300,
            RampsPerPlateau = 16,
            RampTries = MapGenParams.MaxRampTries,
            MinPassableFraction = 1f,
            MaxAttempts = MapGenParams.MaxMaxAttempts,
        };
        return bigRamps
            ? p with { RampWidth = 50, RampLength = 100, Level1MinSize = 52, Level1MaxSize = 200, Level2MinSize = 52, Level2MaxSize = 150 }
            : p;
    }

    /// <summary>This class's wall-clock and allocation tests: they run alone in <see cref="SerialCollection"/> (BUG-0017, BUG-0024) while the heavy tests above stay in the parallel batch.</summary>
    [Collection(SerialCollection.Name)]
    public class Serial
    {
        private readonly ITestOutputHelper _out;

        public Serial(ITestOutputHelper output) => _out = output;

        [Theory]
        [Trait("Category", "Perf")]
        [InlineData(false)]
        [InlineData(true)]
        public void WorstValidParams_1024Map_AtCaps_UnderFiveSeconds(bool bigRamps)
        {
            // Brief M1-4a: "worst valid case ~2 s, fail above 5 s". docs/03 only claims 512 x 512; Validate accepts 1024 x 1024.
            MapGenParams p = Worst(bigRamps);
            p.Validate();
            var sw = Stopwatch.StartNew();
            Heightmap hm = Gen(11, p);
            var nav = new NavGrid(hm);
            sw.Stop();
            _out.WriteLine($"1024x1024 worst case: {sw.Elapsed.TotalSeconds:F2} s");
            Assert.True(sw.Elapsed.TotalSeconds < 5, $"took {sw.Elapsed.TotalSeconds:F1} s");
            Assert.True(MapQaChecker.Check(hm, nav, requireAllLevels: false, minPassableFraction: 0).Count == 0);
        }
    }
}
