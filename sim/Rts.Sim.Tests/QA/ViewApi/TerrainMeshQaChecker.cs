using System.Numerics;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>QA (M2-1): structural checker for <see cref="TerrainMesh"/>, written from the brief, not from the builder.</summary>
/// <remarks>
/// Checks: arrays consistent; quads of four vertices, two triangles each, indices only inside their
/// own quad; no NaN; unit normals; one top quad per cell covering exactly that cell; plateau tops
/// flat at the cell elevation in the level tint; ramp tops in the ramp tint between their level and
/// the next; walls vertical, axis-aligned, in the cliff tint; and the surface is closed: every
/// interior edge where the two tops meet at different heights has one wall that spans exactly the
/// gap at both ends, and edges that meet flush have none.
/// </remarks>
public static class TerrainMeshQaChecker
{
    public static void Check(Heightmap map, TerrainMesh m, bool rampsSpanOneLevel = true)
    {
        const float cs = MapConstants.CellSize;
        int w = map.Width, h = map.Height;
        Assert.Equal(m.Positions.Length, m.Normals.Length);
        Assert.Equal(m.Positions.Length, m.Colors.Length);
        Assert.Equal(0, m.Positions.Length % 4);
        int quads = m.Positions.Length / 4;
        Assert.Equal(quads * 6, m.Indices.Length);

        var tops = new int[w * h];
        Array.Fill(tops, -1);
        var walls = new Dictionary<(float, float, float, float), int>();

        for (int q = 0; q < quads; q++)
        {
            int b = q * 4;
            for (int k = 0; k < 6; k++)
            {
                int idx = m.Indices[q * 6 + k];
                Assert.True(idx >= b && idx < b + 4, $"quad {q}: index {idx} outside its own vertices");
            }
            for (int k = 0; k < 6; k += 3)
            {
                int i0 = m.Indices[q * 6 + k], i1 = m.Indices[q * 6 + k + 1], i2 = m.Indices[q * 6 + k + 2];
                Vector3 cross = Vector3.Cross(m.Positions[i1] - m.Positions[i0], m.Positions[i2] - m.Positions[i0]);
                if (cross.LengthSquared() > 1e-10f)
                    Assert.True(Vector3.Dot(cross, m.Normals[i0]) < 0f, $"quad {q}: triangle winds against its normal");
            }
            Vector3 n = m.Normals[b];
            Vector4 col = m.Colors[b];
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int k = 0; k < 4; k++)
            {
                Vector3 p = m.Positions[b + k];
                Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z), $"quad {q}: non-finite {p}");
                Assert.True(Math.Abs(m.Normals[b + k].Length() - 1f) < 1e-4f, $"quad {q}: normal not unit");
                Assert.Equal(n, m.Normals[b + k]);
                Assert.Equal(col, m.Colors[b + k]);
                Assert.InRange(p.Y, 0f, MapConstants.MaxLevel * MapConstants.LevelHeight);
                Assert.InRange(p.X, 0f, w * cs);
                Assert.InRange(p.Z, 0f, h * cs);
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z);
            }

            if (n.Y > 0.1f)
            {
                int cx = (int)MathF.Round(minX / cs), cy = (int)MathF.Round(minZ / cs);
                Assert.Equal(cx * cs, minX);
                Assert.Equal(cx * cs + cs, maxX);
                Assert.Equal(cy * cs, minZ);
                Assert.Equal(cy * cs + cs, maxZ);
                Assert.True(tops[cy * w + cx] < 0, $"cell ({cx}, {cy}) has two top quads");
                tops[cy * w + cx] = q;
                int level = map.LevelAt(cx, cy);
                if (map.IsRamp(cx, cy))
                {
                    Assert.Equal(TerrainMeshBuilder.RampColor, col);
                    // Generator-style maps: a ramp joins its level and the next. Arbitrary valid
                    // heightmaps (a ramp beside a cliff two levels up) only promise the slope stays
                    // within the heights of the cell and its four neighbours.
                    (float lo, float hi) = rampsSpanOneLevel
                        ? (level * MapConstants.LevelHeight, (level + 1) * MapConstants.LevelHeight)
                        : NeighbourRange(map, cx, cy);
                    for (int k = 0; k < 4; k++)
                        Assert.InRange(m.Positions[b + k].Y, lo, hi);
                }
                else
                {
                    Assert.Equal(TerrainMeshBuilder.LevelColor(level), col);
                    for (int k = 0; k < 4; k++)
                        Assert.Equal(map.ElevationAt(cx, cy), m.Positions[b + k].Y);
                }
            }
            else
            {
                Assert.True(n.Y == 0f && (Math.Abs(n.X) == 1f || Math.Abs(n.Z) == 1f), $"quad {q}: wall normal {n} not horizontal axis");
                Assert.Equal(TerrainMeshBuilder.CliffColor, col);
                Assert.True(minX == maxX || minZ == maxZ, $"quad {q}: wall not axis aligned");
                Assert.True(walls.TryAdd((minX, minZ, maxX, maxZ), q), $"two walls on edge {(minX, minZ, maxX, maxZ)}");
            }
        }

        for (int i = 0; i < tops.Length; i++)
            Assert.True(tops[i] >= 0, $"cell ({i % w}, {i / w}) has no top quad");

        int usedWalls = 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (x + 1 < w) usedWalls += CheckEdge(m, walls, tops[y * w + x], tops[y * w + x + 1],
                    new Vector2((x + 1) * cs, y * cs), new Vector2((x + 1) * cs, (y + 1) * cs));
                if (y + 1 < h) usedWalls += CheckEdge(m, walls, tops[y * w + x], tops[(y + 1) * w + x],
                    new Vector2(x * cs, (y + 1) * cs), new Vector2((x + 1) * cs, (y + 1) * cs));
            }
        }
        Assert.True(usedWalls == walls.Count, $"{walls.Count - usedWalls} walls not on any height-differing interior edge");
    }

    private static int CheckEdge(TerrainMesh m, Dictionary<(float, float, float, float), int> walls, int qa, int qb, Vector2 e0, Vector2 e1)
    {
        float a0 = HeightAt(m, qa, e0), a1 = HeightAt(m, qa, e1);
        float b0 = HeightAt(m, qb, e0), b1 = HeightAt(m, qb, e1);
        bool flush = Math.Abs(a0 - b0) <= 1e-4f && Math.Abs(a1 - b1) <= 1e-4f;
        bool has = walls.TryGetValue((e0.X, e0.Y, e1.X, e1.Y), out int wq);
        if (flush)
        {
            Assert.False(has, $"wall on flush edge {e0}-{e1}");
            return 0;
        }
        Assert.True(has, $"gap: no wall on edge {e0}-{e1} (heights {a0}/{b0} and {a1}/{b1})");
        Span<float> lo = stackalloc float[2], hi = stackalloc float[2];
        lo[0] = lo[1] = float.MaxValue; hi[0] = hi[1] = float.MinValue;
        for (int k = 0; k < 4; k++)
        {
            Vector3 p = m.Positions[wq * 4 + k];
            int end = new Vector2(p.X, p.Z) == e0 ? 0 : new Vector2(p.X, p.Z) == e1 ? 1 : -1;
            Assert.True(end >= 0, $"wall vertex {p} not at an end of {e0}-{e1}");
            lo[end] = Math.Min(lo[end], p.Y);
            hi[end] = Math.Max(hi[end], p.Y);
        }
        Assert.Equal(Math.Min(a0, b0), lo[0], 4);
        Assert.Equal(Math.Max(a0, b0), hi[0], 4);
        Assert.Equal(Math.Min(a1, b1), lo[1], 4);
        Assert.Equal(Math.Max(a1, b1), hi[1], 4);
        return 1;
    }

    private static (float, float) NeighbourRange(Heightmap map, int x, int y)
    {
        float lo = map.ElevationAt(x, y), hi = lo;
        foreach ((int dx, int dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
        {
            int nx = x + dx, ny = y + dy;
            if ((uint)nx >= (uint)map.Width || (uint)ny >= (uint)map.Height) continue;
            lo = Math.Min(lo, map.ElevationAt(nx, ny));
            hi = Math.Max(hi, map.ElevationAt(nx, ny));
        }
        return (lo, hi);
    }

    private static float HeightAt(TerrainMesh m, int quad, Vector2 corner)
    {
        for (int k = 0; k < 4; k++)
        {
            Vector3 p = m.Positions[quad * 4 + k];
            if (p.X == corner.X && p.Z == corner.Y) return p.Y;
        }
        throw new Xunit.Sdk.XunitException($"top quad {quad} has no corner at {corner}");
    }
}
