using Rts.Sim.Determinism;
using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>QA (M2-1): terrain mesh on adversarial and generated maps, and proof that building it changes no sim state.</summary>
public class TerrainMeshBuilderQaTests
{
    private const float L = MapConstants.LevelHeight;

    private static Heightmap Uniform(int w, int h, byte level)
    {
        var lv = new byte[w * h];
        var el = new float[w * h];
        Array.Fill(lv, level);
        Array.Fill(el, level * L);
        return new Heightmap(w, h, lv, el);
    }

    private static Heightmap FromRows(int w, int h, Func<int, int, (byte Level, float Elev)> cell)
    {
        var lv = new byte[w * h];
        var el = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                (lv[y * w + x], el[y * w + x]) = cell(x, y);
        return new Heightmap(w, h, lv, el);
    }

    // Any valid heightmap: random levels, about a third of the non-top cells ramps at random heights.
    private static Heightmap Random(int w, int h, ulong seed)
    {
        var rng = new SimRng(seed, 5);
        return FromRows(w, h, (_, _) =>
        {
            byte level = (byte)rng.NextInt(0, MapConstants.LevelCount);
            float e = level * L;
            if (level < MapConstants.MaxLevel && rng.NextInt(0, 3) == 0)
                e += 0.25f + rng.NextFloat() * (L - 0.5f);
            return (level, e);
        });
    }

    public static TheoryData<string> AdversarialNames => new()
    {
        "1x1 level 0", "1x1 level 2", "1x1 ramp", "1x9", "9x1", "1x9 ramps", "all level 2 16x16",
        "checker 0/2", "checker 0/1", "checker ramp/plateau", "ramp west edge", "ramp east edge",
        "ramp north edge", "ramp south edge", "corner ramps", "ramp straddles both axes",
        "ramp between two higher", "long ramp chain", "stripes 0/1/2",
    };

    private static Heightmap Adversarial(string name) => name switch
    {
        "1x1 level 0" => Uniform(1, 1, 0),
        "1x1 level 2" => Uniform(1, 1, 2),
        "1x1 ramp" => FromRows(1, 1, (_, _) => (0, 2f)),
        "1x9" => FromRows(1, 9, (_, y) => ((byte)(y % 3), y % 3 * L)),
        "9x1" => FromRows(9, 1, (x, _) => ((byte)(x % 3), x % 3 * L)),
        "1x9 ramps" => FromRows(1, 9, (_, y) => y % 2 == 0 ? ((byte)0, 0f) : ((byte)0, 2f)),
        "all level 2 16x16" => Uniform(16, 16, 2),
        "checker 0/2" => FromRows(8, 8, (x, y) => (x + y) % 2 == 0 ? ((byte)0, 0f) : ((byte)2, 2 * L)),
        "checker 0/1" => FromRows(8, 8, (x, y) => (x + y) % 2 == 0 ? ((byte)0, 0f) : ((byte)1, L)),
        "checker ramp/plateau" => FromRows(8, 8, (x, y) => (x + y) % 2 == 0 ? ((byte)1, L + 2f) : ((byte)1, L)),
        "ramp west edge" => FromRows(4, 4, (x, _) => x == 0 ? ((byte)0, 2f) : ((byte)1, L)),
        "ramp east edge" => FromRows(4, 4, (x, _) => x == 3 ? ((byte)0, 2f) : ((byte)1, L)),
        "ramp north edge" => FromRows(4, 4, (_, y) => y == 0 ? ((byte)0, 2f) : ((byte)0, 0f)),
        "ramp south edge" => FromRows(4, 4, (_, y) => y == 3 ? ((byte)1, L + 2f) : ((byte)2, 2 * L)),
        "corner ramps" => FromRows(5, 5, (x, y) => (x == 0 || x == 4) && (y == 0 || y == 4) ? ((byte)0, 1f) : ((byte)1, L)),
        // (1,1) is a ramp with lower ground west and north and higher ground east and south.
        "ramp straddles both axes" => FromRows(3, 3, (x, y) => (x, y) == (1, 1) ? ((byte)0, 2f) : x + y < 2 ? ((byte)0, 0f) : ((byte)1, L)),
        "ramp between two higher" => FromRows(3, 1, (x, _) => x == 1 ? ((byte)0, 2f) : ((byte)1, L)),
        "long ramp chain" => FromRows(10, 3, (x, _) => x == 0 ? ((byte)0, 0f) : x == 9 ? ((byte)1, L) : ((byte)0, x * L / 9f)),
        "stripes 0/1/2" => FromRows(12, 4, (x, _) => ((byte)(x / 4), x / 4 * L)),
        _ => throw new ArgumentException(name),
    };

    [Theory]
    [MemberData(nameof(AdversarialNames))]
    public void AdversarialMaps_AreWellFormedAndClosed(string name)
    {
        Heightmap map = Adversarial(name);
        TerrainMesh m = TerrainMeshBuilder.Build(map);
        TerrainMeshQaChecker.Check(map, m, rampsSpanOneLevel: false);
    }

    [Fact]
    public void UniformMap_HasNoWalls_OneQuadPerCell()
    {
        TerrainMesh m = TerrainMeshBuilder.Build(Uniform(16, 16, 1));
        Assert.Equal(16 * 16 * 4, m.Positions.Length);
        Assert.DoesNotContain(TerrainMeshBuilder.CliffColor, m.Colors);
    }

    [Fact]
    public void LongRampChain_IsMonotonicAndMeetsBothEnds()
    {
        Heightmap map = Adversarial("long ramp chain");
        TerrainMesh m = TerrainMeshBuilder.Build(map);
        // Along the middle row, ramp top heights at each x must never go down from west to east.
        var byX = new SortedDictionary<float, float>();
        for (int i = 0; i < m.Positions.Length; i++)
        {
            if (m.Colors[i] != TerrainMeshBuilder.RampColor) continue;
            Vector3 p = m.Positions[i];
            if (byX.TryGetValue(p.X, out float prev)) Assert.Equal(prev, p.Y, 4);
            else byX[p.X] = p.Y;
        }
        float last = -1f;
        foreach ((float _, float y) in byX)
        {
            Assert.True(y >= last, $"ramp descends: {y} after {last}");
            last = y;
        }
        Assert.Equal(0f, byX.First().Value, 4);
        Assert.Equal(L, byX.Last().Value, 4);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    [InlineData(5UL)]
    [InlineData(6UL)]
    [InlineData(7UL)]
    [InlineData(8UL)]
    public void RandomValidHeightmaps_AreWellFormedAndClosed(ulong seed)
    {
        for (int i = 0; i < 25; i++)
        {
            var rng = new SimRng(seed * 1000 + (ulong)i, 3);
            int w = rng.NextInt(1, 24), h = rng.NextInt(1, 24);
            Heightmap map = Random(w, h, seed * 1000 + (ulong)i);
            TerrainMeshQaChecker.Check(map, TerrainMeshBuilder.Build(map), rampsSpanOneLevel: false);
        }
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(6UL)]
    [InlineData(17UL)]
    [InlineData(99UL)]
    public void GeneratedMaps_AreClosed_AndEveryRampIsSloped(ulong seed)
    {
        var sim = new Simulation(TestSim.Config(seed, 2, 16, 16));
        Heightmap map = sim.World.Heightmap;
        TerrainMesh m = TerrainMeshBuilder.Build(map);
        TerrainMeshQaChecker.Check(map, m);

        int ramps = 0, flatRamps = 0;
        for (int q = 0; q < m.Positions.Length / 4; q++)
        {
            if (m.Colors[q * 4] != TerrainMeshBuilder.RampColor) continue;
            ramps++;
            float lo = float.MaxValue, hi = float.MinValue;
            for (int k = 0; k < 4; k++)
            {
                lo = Math.Min(lo, m.Positions[q * 4 + k].Y);
                hi = Math.Max(hi, m.Positions[q * 4 + k].Y);
            }
            if (hi - lo < 1e-3f) flatRamps++;
        }
        Assert.True(ramps > 0, "generated map has no ramps");
        Assert.True(flatRamps == 0, $"{flatRamps} of {ramps} ramp cells are flat (not a slope)");
    }

    [Fact]
    public void BuildingTheMesh_ChangesNoSimState_100BuildsBetweenTicks()
    {
        var withMesh = new Simulation(TestSim.Config(1, 2, 2000, 4096));
        var control = new Simulation(TestSim.Config(1, 2, 2000, 4096));
        ulong mapHash = control.World.Heightmap.ContentHash();
        Assert.Equal(control.StateHash(), withMesh.StateHash());

        TerrainMesh first = TerrainMeshBuilder.Build(withMesh.World.Heightmap);
        for (int tick = 0; tick < 20; tick++)
        {
            for (int i = 0; i < 5; i++)
            {
                TerrainMesh again = TerrainMeshBuilder.Build(withMesh.World.Heightmap);
                Assert.Equal(first.Positions.Length, again.Positions.Length);
                Assert.Equal(first.Indices.Length, again.Indices.Length);
            }
            Assert.Equal(control.StateHash(), withMesh.StateHash());
            withMesh.Tick();
            control.Tick();
            Assert.Equal(control.TickNumber, withMesh.TickNumber);
            Assert.Equal(control.StateHash(), withMesh.StateHash());
        }
        Assert.Equal(mapHash, withMesh.World.Heightmap.ContentHash());
        TerrainMesh last = TerrainMeshBuilder.Build(withMesh.World.Heightmap);
        Assert.Equal(first.Positions, last.Positions);
        Assert.Equal(first.Normals, last.Normals);
        Assert.Equal(first.Colors, last.Colors);
        Assert.Equal(first.Indices, last.Indices);
    }

    [Fact]
    public void ViewApiSource_HasNoSimMutatorsOrTickCalls()
    {
        string dir = Path.Combine(RepoRoot(), "sim", "Rts.Sim", "ViewApi");
        string[] forbidden =
        {
            @"\.Tick\s*\(", @"\.Enqueue\s*\(", @"\bWorld\b", @"\bSimulation\b", @"\bUnitStore\b",
            @"\bref\s+SimRng\b", @"MemoryMarshal", @"\bunsafe\b", @"Unsafe\.", @"\bfixed\s*\(",
        };
        var hits = new List<string>();
        foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimStart();
                if (line.StartsWith("//") || line.StartsWith("///")) continue;
                foreach (string p in forbidden)
                    if (System.Text.RegularExpressions.Regex.IsMatch(line, p))
                        hits.Add($"{Path.GetFileName(file)}:{i + 1}: {p}: {line}");
            }
        }
        Assert.True(hits.Count == 0, string.Join(Environment.NewLine, hits));
    }

    [Fact]
    public void ViewApiTypes_ExposeNoPublicMutableStaticState()
    {
        var types = typeof(SimInfo).Assembly.GetTypes().Where(t => t.Namespace == "Rts.Sim.ViewApi");
        Assert.NotEmpty(types);
        foreach (Type t in types)
        {
            foreach (var f in t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                Assert.True(f.IsLiteral || f.IsInitOnly, $"{t.Name}.{f.Name} is a writable public static field");
        }
    }

    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "RtsGame.sln"))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("RtsGame.sln not found");
    }

    /// <summary>Wall-clock and memory report; run alone in the serial collection.</summary>
    [Collection(SerialCollection.Name)]
    public class Serial
    {
        private readonly ITestOutputHelper _out;

        public Serial(ITestOutputHelper output) => _out = output;

        [Theory]
        [Trait("Category", "Perf")]
        [InlineData(256)]
        [InlineData(512)]
        [InlineData(1024)]
        public void LargeGeneratedMap_TimeAndMemoryReport(int size)
        {
            var rng = new SimRng(1, 0);
            Heightmap map = MapGenerator.Generate(MapGenParams.Default with { Width = size, Height = size }, ref rng);
            TerrainMeshBuilder.Build(Uniform(8, 8, 0)); // JIT warm-up
            long before = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            TerrainMesh m = TerrainMeshBuilder.Build(map);
            sw.Stop();
            long alloc = GC.GetAllocatedBytesForCurrentThread() - before;
            long kept = m.Positions.Length * 12L * 2 + m.Colors.Length * 16L + m.Indices.Length * 4L;
            _out.WriteLine($"{size} x {size}: {sw.Elapsed.TotalMilliseconds:F1} ms, {m.Positions.Length:N0} vertices, " +
                $"{m.Indices.Length / 3:N0} triangles, {alloc / 1048576.0:F0} MiB allocated, {kept / 1048576.0:F0} MiB kept");
            Assert.True(m.Indices.Length % 3 == 0);
            // Hang guard only: builds once per match, off the tick.
            Assert.True(sw.Elapsed.TotalSeconds < 10, $"{size}: {sw.Elapsed.TotalSeconds:F1} s");
        }
    }
}
