using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>The pure terrain mesh the view uploads to Godot: plateaus, cliffs, ramps, tints.</summary>
public class TerrainMeshBuilderTests
{
    private const float Lo = 0f;
    private const float Hi = MapConstants.LevelHeight;

    // 4 x 4, rows are y. Columns 0-1 are a level-1 plateau, 2-3 level-0 ground, and cell (2, 1) is
    // a level-0 ramp rising west to the plateau. Every other level change is a cliff.
    private static Heightmap SmallMap()
    {
        byte[] levels =
        {
            1, 1, 0, 0,
            1, 1, 0, 0,
            1, 1, 0, 0,
            1, 1, 0, 0,
        };
        float[] elev =
        {
            Hi, Hi, Lo, Lo,
            Hi, Hi, Hi / 2, Lo,
            Hi, Hi, Lo, Lo,
            Hi, Hi, Lo, Lo,
        };
        return new Heightmap(4, 4, levels, elev);
    }

    // The ramp cell's footprint in meters.
    private const float RampX0 = 2 * MapConstants.CellSize, RampX1 = 3 * MapConstants.CellSize;
    private const float RampZ0 = 1 * MapConstants.CellSize, RampZ1 = 2 * MapConstants.CellSize;

    private static bool OnRampFootprint(Vector3 p) =>
        p.X >= RampX0 && p.X <= RampX1 && p.Z >= RampZ0 && p.Z <= RampZ1;

    [Fact]
    public void SmallMap_VertexHeights_AreCellElevationsOrBetweenLevelsOnTheRamp()
    {
        TerrainMesh m = TerrainMeshBuilder.Build(SmallMap());
        Assert.NotEmpty(m.Positions);
        foreach (Vector3 p in m.Positions)
        {
            bool atLevel = p.Y == Lo || p.Y == Hi;
            bool onRamp = OnRampFootprint(p) && p.Y > Lo && p.Y < Hi;
            Assert.True(atLevel || onRamp, $"vertex {p} is neither at a level nor on the ramp");
        }
    }

    [Fact]
    public void SmallMap_ArraysAreConsistent_IndicesInRange_NoNaN()
    {
        TerrainMesh m = TerrainMeshBuilder.Build(SmallMap());
        Assert.Equal(m.Positions.Length, m.Normals.Length);
        Assert.Equal(m.Positions.Length, m.Colors.Length);
        Assert.Equal(0, m.Indices.Length % 3);
        foreach (int i in m.Indices)
            Assert.InRange(i, 0, m.Positions.Length - 1);
        for (int i = 0; i < m.Positions.Length; i++)
        {
            Vector3 p = m.Positions[i], n = m.Normals[i];
            Assert.False(float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsNaN(p.Z), $"NaN position {i}");
            Assert.False(float.IsNaN(n.X) || float.IsNaN(n.Y) || float.IsNaN(n.Z), $"NaN normal {i}");
            Assert.Equal(1f, n.Length(), 4);
        }
        // 16 tops + cliffs: (1,y)-(2,y) for y = 0, 2, 3 and the ramp's two side walls.
        Assert.Equal((16 + 3 + 2) * 4, m.Positions.Length);
    }

    [Fact]
    public void SmallMap_HasVerticalCliffFace_BetweenLevel0AndLevel1()
    {
        TerrainMesh m = TerrainMeshBuilder.Build(SmallMap());
        float boundaryX = 2 * MapConstants.CellSize;
        bool found = false;
        for (int t = 0; t < m.Indices.Length; t += 3)
        {
            int a = m.Indices[t], b = m.Indices[t + 1], c = m.Indices[t + 2];
            bool horizontalNormal = MathF.Abs(m.Normals[a].Y) < 1e-6f;
            bool onBoundary = m.Positions[a].X == boundaryX && m.Positions[b].X == boundaryX && m.Positions[c].X == boundaryX;
            bool spansLevels = MathF.Max(m.Positions[a].Y, MathF.Max(m.Positions[b].Y, m.Positions[c].Y)) == Hi
                && MathF.Min(m.Positions[a].Y, MathF.Min(m.Positions[b].Y, m.Positions[c].Y)) == Lo;
            if (horizontalNormal && onBoundary && spansLevels && m.Positions[a].Z >= RampZ1)
            {
                // Faces the low ground (+x), and the triangle winds clockwise seen from there (Godot front face).
                Assert.Equal(Vector3.UnitX, m.Normals[a]);
                Vector3 cross = Vector3.Cross(m.Positions[b] - m.Positions[a], m.Positions[c] - m.Positions[a]);
                Assert.True(Vector3.Dot(cross, m.Normals[a]) < 0f, "cliff triangle must wind clockwise from its front");
                found = true;
            }
        }
        Assert.True(found, "no vertical cliff face on the level-0 / level-1 boundary");
    }

    [Fact]
    public void SmallMap_TopTriangles_WindClockwiseSeenFromAbove()
    {
        TerrainMesh m = TerrainMeshBuilder.Build(SmallMap());
        for (int t = 0; t < m.Indices.Length; t += 3)
        {
            int a = m.Indices[t], b = m.Indices[t + 1], c = m.Indices[t + 2];
            Vector3 cross = Vector3.Cross(m.Positions[b] - m.Positions[a], m.Positions[c] - m.Positions[a]);
            if (cross.LengthSquared() < 1e-12f) continue; // degenerate wall end
            Assert.True(Vector3.Dot(cross, m.Normals[a]) < 0f, $"triangle {t / 3} winds counter-clockwise from its front");
        }
    }

    [Fact]
    public void SmallMap_RampIsAContinuousSlope_MonotonicDownhillEast()
    {
        TerrainMesh m = TerrainMeshBuilder.Build(SmallMap());
        var ramp = new List<Vector3>();
        for (int i = 0; i < m.Positions.Length; i++)
        {
            if (m.Colors[i] == TerrainMeshBuilder.RampColor) ramp.Add(m.Positions[i]);
        }
        Assert.Equal(4, ramp.Count);
        foreach (Vector3 a in ramp)
        {
            foreach (Vector3 b in ramp)
            {
                if (a.X < b.X) Assert.True(a.Y > b.Y, $"ramp not descending east: {a} vs {b}");
                if (a.X == b.X) Assert.Equal(a.Y, b.Y); // level across the slope
            }
        }
        // It meets the plateau lip and the low ground exactly: no step at either end.
        Assert.Contains(ramp, p => p.X == RampX0 && p.Y == Hi);
        Assert.Contains(ramp, p => p.X == RampX1 && p.Y == Lo);
        // Its top normal tilts toward the low side (+x) but still points up.
        int first = Array.IndexOf(m.Colors, TerrainMeshBuilder.RampColor);
        Assert.True(m.Normals[first].X > 0f && m.Normals[first].Y > 0f);
    }

    [Fact]
    public void Colors_DifferPerLevel_AndRampAndCliffAreDistinct()
    {
        Vector4[] all =
        {
            TerrainMeshBuilder.LevelColor(0), TerrainMeshBuilder.LevelColor(1), TerrainMeshBuilder.LevelColor(2),
            TerrainMeshBuilder.RampColor, TerrainMeshBuilder.CliffColor,
        };
        for (int i = 0; i < all.Length; i++)
            for (int j = i + 1; j < all.Length; j++)
                Assert.NotEqual(all[i], all[j]);

        TerrainMesh m = TerrainMeshBuilder.Build(SmallMap());
        Assert.Contains(TerrainMeshBuilder.LevelColor(0), m.Colors);
        Assert.Contains(TerrainMeshBuilder.LevelColor(1), m.Colors);
        Assert.Contains(TerrainMeshBuilder.RampColor, m.Colors);
        Assert.Contains(TerrainMeshBuilder.CliffColor, m.Colors);
    }

    [Fact]
    public void DefaultMap_IsWellFormed_AndBuildingChangesNoState()
    {
        var sim = new Simulation(TestSim.Config(1, 2, 2000, 4096));
        Heightmap hm = sim.World.Heightmap;
        Assert.Equal(128, hm.Width);
        ulong mapHash = hm.ContentHash();
        ulong stateHash = sim.StateHash();

        TerrainMesh a = TerrainMeshBuilder.Build(hm);
        TerrainMesh b = TerrainMeshBuilder.Build(hm);

        Assert.Equal(0, a.Indices.Length % 3);
        Assert.Equal(a.Positions, b.Positions);
        Assert.Equal(a.Indices, b.Indices);
        Assert.Equal(mapHash, hm.ContentHash());
        Assert.Equal(stateHash, sim.StateHash());
        foreach (int i in a.Indices)
            Assert.InRange(i, 0, a.Positions.Length - 1);
        foreach (Vector3 p in a.Positions)
            Assert.InRange(p.Y, 0f, MapConstants.MaxLevel * MapConstants.LevelHeight);
        // The default map has all three levels, ramps and cliffs, so all five colors appear.
        Assert.Contains(TerrainMeshBuilder.LevelColor(2), a.Colors);
        Assert.Contains(TerrainMeshBuilder.RampColor, a.Colors);
        Assert.Contains(TerrainMeshBuilder.CliffColor, a.Colors);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(42UL)]
    public void DefaultMap_HasAWallOnEveryEdgeWhereTheTwoCellsDiffer_AndNowhereElse(ulong seed)
    {
        // Independent count: an edge between neighbours needs a wall when their top corners on it
        // differ (CellCorners is the surface both TerrainHeight and the mesh use). Catches a builder
        // that drops walls (for example on the last row or column) or adds stray ones.
        var sim = new Simulation(TestSim.Config(seed, 2, 16, 64));
        int count = AssertWallsExactlyWhereCellsDiffer(sim.World.Heightmap);
        Assert.True(count > 100, $"only {count} wall edges on the default map");
    }

    [Fact]
    public void HandMapWithStepsOnTheLastRowAndColumn_HasThoseWalls_AndNoOthers()
    {
        // BUG-0070: the default maps' border ring is flat, so a builder that skipped the last row or column of
        // walls passed the seed 1 / 42 rows. Here the last column and the last row are a level up.
        const int n = 6;
        var levels = new byte[n * n];
        var elevations = new float[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                levels[y * n + x] = (byte)(x == n - 1 || y == n - 1 ? 1 : 0);
                elevations[y * n + x] = levels[y * n + x] * MapConstants.LevelHeight;
            }
        int count = AssertWallsExactlyWhereCellsDiffer(new Heightmap(n, n, levels, elevations));
        Assert.Equal(2 * (n - 1), count); // x = 4 | 5 on rows 0-4, and y = 4 | 5 on columns 0-4
    }

    // Independent count: every edge whose two cells' top corners differ has exactly one wall quad, and no other edge has one. Returns the edge count.
    private static int AssertWallsExactlyWhereCellsDiffer(Heightmap hm)
    {
        int w = hm.Width, h = hm.Height;
        const float cs = MapConstants.CellSize, eps = 1e-4f;
        Span<float> c = stackalloc float[4], n = stackalloc float[4];
        var expected = new HashSet<(float, float)>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                TerrainHeight.CellCorners(hm, x, y, c);
                if (x + 1 < w)
                {
                    TerrainHeight.CellCorners(hm, x + 1, y, n);
                    if (MathF.Abs(c[1] - n[0]) > eps || MathF.Abs(c[3] - n[2]) > eps) expected.Add(((x + 1) * cs, (y + 0.5f) * cs));
                }
                if (y + 1 < h)
                {
                    TerrainHeight.CellCorners(hm, x, y + 1, n);
                    if (MathF.Abs(c[2] - n[0]) > eps || MathF.Abs(c[3] - n[1]) > eps) expected.Add(((x + 0.5f) * cs, (y + 1) * cs));
                }
            }

        TerrainMesh m = TerrainMeshBuilder.Build(hm);
        var walls = new HashSet<(float, float)>();
        int wallQuads = 0;
        for (int q = 0; q < m.Positions.Length; q += 4)
        {
            if (m.Normals[q].Y > 1e-3f) continue;
            wallQuads++;
            Vector3 mid = (m.Positions[q] + m.Positions[q + 1] + m.Positions[q + 2] + m.Positions[q + 3]) / 4f;
            walls.Add((mid.X, mid.Z));
        }
        Assert.Equal(expected.Count, wallQuads);
        Assert.Equal(w * h + expected.Count, m.Positions.Length / 4);
        Assert.True(walls.SetEquals(expected), $"{walls.Except(expected).Count()} stray walls, {expected.Except(walls).Count()} missing");
        return expected.Count;
    }

    /// <summary>Wall-clock budget; run alone in the serial collection.</summary>
    [Collection(SerialCollection.Name)]
    public class Serial
    {
        private readonly ITestOutputHelper _out;

        public Serial(ITestOutputHelper output) => _out = output;

        [Fact]
        [Trait("Category", "Perf")]
        public void DefaultMap_BuildsUnder100Ms()
        {
            var sim = new Simulation(TestSim.Config(1, 2, 2000, 4096));
            Heightmap hm = sim.World.Heightmap;
            TerrainMeshBuilder.Build(hm); // warm-up (JIT)

            long best = long.MaxValue;
            for (int run = 0; run < 3; run++)
            {
                var sw = Stopwatch.StartNew();
                TerrainMesh m = TerrainMeshBuilder.Build(hm);
                sw.Stop();
                best = Math.Min(best, sw.ElapsedTicks);
                Assert.NotEmpty(m.Indices);
            }
            double ms = best * 1000.0 / Stopwatch.Frequency;
            _out.WriteLine($"TerrainMeshBuilder.Build on the default 128 x 128 map: {ms:F2} ms (best of 3)");
            Assert.True(ms < 100.0, $"build took {ms:F2} ms");
        }
    }
}
