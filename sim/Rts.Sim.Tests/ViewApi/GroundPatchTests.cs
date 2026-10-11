using System.Numerics;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>
/// M4-VH3 (BUG-0430, BUG-0462 item 1): <see cref="GroundPatch"/>, the terrain cells a zone disc or ability ring is drawn
/// on. Every quad is its cell's drawn top surface, so the disc's rim lies on <see cref="TerrainHeight.At"/> on both sides of
/// a cliff; the cells are exactly those the disc (or ring band) overlaps; allocation-free; reading it changes no sim state.
/// </summary>
[Collection(SerialCollection.Name)]
public class GroundPatchTests
{
    private const float Cs = MapConstants.CellSize;
    private readonly ITestOutputHelper _out;

    public GroundPatchTests(ITestOutputHelper output) => _out = output;

    // 16 x 12: columns 0-7 a level-1 plateau (4 m), 8-15 level 0, a straight cliff at x = 16 m; cell (8, 9) a ramp up west.
    private static Heightmap TwoLevels()
    {
        const int w = 16, h = 12;
        var levels = new byte[w * h];
        var elev = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool high = x < 8;
                levels[y * w + x] = (byte)(high ? 1 : 0);
                elev[y * w + x] = high ? MapConstants.LevelHeight : 0f;
            }
        elev[9 * w + 8] = MapConstants.LevelHeight / 2f; // a ramp cell (level 0, half way up)
        return new Heightmap(w, h, levels, elev);
    }

    /// <summary>The rim of a 6 m disc centred 1 m from the cliff: every point on it lies on its quad within 0.05 m of the terrain, on both levels.</summary>
    [Theory]
    [InlineData(15f, 12f, 6f)]  // on the plateau, 1 m from the drop
    [InlineData(19f, 12f, 6f)]  // on the low ground, 3 m from the drop
    [InlineData(16.5f, 17f, 3f)] // over the ramp cell and the cliff
    public void TwoLevelMap_EveryRimPoint_LiesOnTheTerrain(float x, float y, float r)
    {
        Heightmap map = TwoLevels();
        var quads = new GroundQuad[GroundPatch.MaxCells(r)];
        int n = GroundPatch.Collect(map, x, y, r, 0f, quads);
        Assert.True(n > 0);
        float lo = float.MaxValue, hi = float.MinValue;
        int points = 0;
        for (int a = 0; a < 720; a++)
        {
            float px = x + r * 0.999f * MathF.Cos(a * MathF.Tau / 720f), py = y + r * 0.999f * MathF.Sin(a * MathF.Tau / 720f);
            if (OnCellEdge(px) || OnCellEdge(py) || px < 0f || py < 0f || px >= map.Width * Cs || py >= map.Height * Cs) continue;
            int k = Find(quads, n, (int)(px / Cs), (int)(py / Cs));
            Assert.True(k >= 0, $"rim point ({px}, {py}) has no quad");
            float ground = TerrainHeight.At(map, px, py);
            Assert.True(MathF.Abs(GroundPatch.HeightIn(quads[k], px, py) - ground) <= 0.05f, $"rim point ({px}, {py}): drawn {GroundPatch.HeightIn(quads[k], px, py)}, ground {ground}");
            lo = MathF.Min(lo, ground);
            hi = MathF.Max(hi, ground);
            points++;
        }
        Assert.True(points > 600, $"{points} rim points");
        Assert.True(hi - lo >= MapConstants.LevelHeight - 1e-3f, $"the rim spans {lo}-{hi} m: not across the cliff");
        _out.WriteLine($"disc ({x}, {y}) r {r}: {n} cells, {points} rim points on {lo}-{hi} m, all on the ground");
    }

    /// <summary>Every quad vertex is its own cell's surface at that corner (which <see cref="TerrainHeight.At"/> gives just inside the cell), on generated maps and the two-level map, discs and rings.</summary>
    [Theory]
    [InlineData(0UL)]
    [InlineData(1UL)]
    [InlineData(6UL)]
    [InlineData(11UL)]
    public void EveryQuadVertex_IsItsCellsSurface_AndTheCellsAreExactlyTheOverlap(ulong seed)
    {
        Heightmap map = seed == 0 ? TwoLevels() : TerrainHeightTests.GeneratedMap(seed);
        var rng = new Random((int)seed + 17); // test data only
        int discs = 0, quadsSeen = 0;
        for (int i = 0; i < 300; i++)
        {
            float r = 0.5f + (float)rng.NextDouble() * 18f;
            float hole = i % 3 == 0 ? MathF.Max(0f, r - 0.22f) : 0f;
            float x = (float)rng.NextDouble() * map.Width * Cs, y = (float)rng.NextDouble() * map.Height * Cs;
            var quads = new GroundQuad[GroundPatch.MaxCells(r)];
            int n = GroundPatch.Collect(map, x, y, r, hole, quads);
            Assert.True(n <= quads.Length);
            for (int k = 0; k < n; k++)
            {
                GroundQuad q = quads[k];
                const float e = 1e-3f;
                float x0 = q.X * Cs, y0 = q.Y * Cs, x1 = x0 + Cs, y1 = y0 + Cs;
                // Exactly the cell's surface at its corner, and within 0.05 m of At just inside the cell (At takes the
                // larger-index cell on an edge, so on a cliff the corner itself belongs to a neighbour).
                Near(q.H00, TerrainHeight.InCell(map, q.X, q.Y, x0, y0), q, "00");
                Near(q.H10, TerrainHeight.InCell(map, q.X, q.Y, x1, y0), q, "10");
                Near(q.H01, TerrainHeight.InCell(map, q.X, q.Y, x0, y1), q, "01");
                Near(q.H11, TerrainHeight.InCell(map, q.X, q.Y, x1, y1), q, "11");
                Near(q.H00, TerrainHeight.At(map, x0 + e, y0 + e), q, "00 via At", 0.05f);
                Near(q.H10, TerrainHeight.At(map, x1 - e, y0 + e), q, "10 via At", 0.05f);
                Near(q.H01, TerrainHeight.At(map, x0 + e, y1 - e), q, "01 via At", 0.05f);
                Near(q.H11, TerrainHeight.At(map, x1 - e, y1 - e), q, "11 via At", 0.05f);
                Near(GroundPatch.HeightIn(q, x0 + 0.7f, y0 + 1.3f), TerrainHeight.At(map, x0 + 0.7f, y0 + 1.3f), q, "inside");
                for (int j = 0; j < k; j++) Assert.False(quads[j].X == q.X && quads[j].Y == q.Y, $"cell ({q.X}, {q.Y}) listed twice");
            }
            // The overlap by brute force: a cell is in the list iff some sampled point of it lies in the band.
            int cx0 = Math.Max(0, (int)((x - r) / Cs) - 1), cx1 = Math.Min(map.Width - 1, (int)((x + r) / Cs) + 1);
            int cy0 = Math.Max(0, (int)((y - r) / Cs) - 1), cy1 = Math.Min(map.Height - 1, (int)((y + r) / Cs) + 1);
            for (int cy = cy0; cy <= cy1; cy++)
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    bool inBand = false;
                    for (int s = 0; s <= 8 && !inBand; s++)
                        for (int t = 0; t <= 8 && !inBand; t++)
                        {
                            float d = Vector2.Distance(new Vector2((cx + s / 8f) * Cs, (cy + t / 8f) * Cs), new Vector2(x, y));
                            if (d < r - 1e-3f && d > hole + 1e-3f) inBand = true;
                        }
                    // Wholly outside the band: its nearest point beyond the radius, or its farthest corner inside the hole.
                    float nearest = Vector2.Distance(new Vector2(Math.Clamp(x, cx * Cs, (cx + 1) * Cs), Math.Clamp(y, cy * Cs, (cy + 1) * Cs)), new Vector2(x, y));
                    float farthest = 0f;
                    for (int corner = 0; corner < 4; corner++)
                        farthest = MathF.Max(farthest, Vector2.Distance(new Vector2((cx + (corner & 1)) * Cs, (cy + (corner >> 1)) * Cs), new Vector2(x, y)));
                    bool outBand = nearest > r + 1e-3f || farthest < hole - 1e-3f;
                    bool listed = Find(quads, n, cx, cy) >= 0;
                    if (inBand) Assert.True(listed, $"seed {seed} disc ({x}, {y}) r {r} hole {hole}: cell ({cx}, {cy}) overlaps but is missing");
                    if (outBand) Assert.False(listed, $"seed {seed} disc ({x}, {y}) r {r} hole {hole}: cell ({cx}, {cy}) is listed but far outside");
                }
            discs++;
            quadsSeen += n;
        }
        _out.WriteLine($"seed {seed}: {discs} discs and rings, {quadsSeen} quads, every vertex on its cell's surface");
    }

    [Fact]
    public void EdgeCases_OffTheMap_NonPositiveOrNaN_AndAShortOutput()
    {
        Heightmap map = TwoLevels();
        var quads = new GroundQuad[64];
        Assert.Equal(0, GroundPatch.Collect(map, 10f, 10f, 0f, 0f, quads));
        Assert.Equal(0, GroundPatch.Collect(map, 10f, 10f, float.NaN, 0f, quads));
        Assert.Equal(0, GroundPatch.Collect(map, float.NaN, 10f, 3f, 0f, quads));
        Assert.Equal(0, GroundPatch.Collect(map, -50f, -50f, 3f, 0f, quads)); // wholly off the map
        // A disc over the map's corner keeps only the cells on the map.
        int corner = GroundPatch.Collect(map, 0f, 0f, 3f, 0f, quads);
        Assert.Equal(4, corner); // cells (0..1, 0..1)
        Assert.Equal(3, GroundPatch.Collect(map, 15f, 12f, 6f, 0f, quads.AsSpan(0, 3)));
        Assert.Equal(1, GroundPatch.MaxCells(0f));
        Assert.True(GroundPatch.MaxCells(6f) >= 49);
    }

    [Fact]
    public void Collect_AllocatesZeroBytes()
    {
        Heightmap map = TerrainHeightTests.GeneratedMap(3);
        var quads = new GroundQuad[GroundPatch.MaxCells(16f)];
        int sum = 0;
        Action block = () =>
        {
            for (int i = 0; i < 400; i++) sum += GroundPatch.Collect(map, i * 0.53f, i * 0.41f, 1f + i % 16, i % 2 == 0 ? 0f : i % 16, quads);
        };
        block();
        AllocationProbe.AssertZero(block, _out);
        Assert.True(sum > 0);
    }

    /// <summary>The hash twin: collecting the ground under every live unit each tick of a brawl changes no state hash.</summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(6UL)]
    public void CollectEveryTick_HashEqualsABareTwin(ulong seed)
    {
        Simulation a = CombatScenes.MapBrawl(seed, 10), b = CombatScenes.MapBrawl(seed, 10);
        var quads = new GroundQuad[GroundPatch.MaxCells(6f)];
        long collected = 0;
        for (int t = 0; t < 600; t++)
        {
            ulong before = a.StateHash();
            var u = a.World.Units;
            for (int i = 0; i < u.Capacity; i++)
                if (u.Alive[i]) collected += GroundPatch.Collect(a.World.Heightmap, u.Position[i].X, u.Position[i].Y, 6f, t % 2 == 0 ? 0f : 5.78f, quads);
            Assert.Equal(before, a.StateHash());
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed} tick {a.World.TickNumber}: the collecting sim diverged from its twin");
        }
        Assert.True(collected > 1000);
        _out.WriteLine($"seed {seed}: {collected} quads collected over 600 ticks; hashes equal");
    }

    private static bool OnCellEdge(float v)
    {
        float f = v / Cs - MathF.Floor(v / Cs);
        return f < 0.001f || f > 0.999f;
    }

    private static int Find(GroundQuad[] quads, int n, int cx, int cy)
    {
        for (int k = 0; k < n; k++) if (quads[k].X == cx && quads[k].Y == cy) return k;
        return -1;
    }

    private static void Near(float drawn, float ground, GroundQuad q, string where, float tolerance = 1e-4f) =>
        Assert.True(MathF.Abs(drawn - ground) <= tolerance,$"cell ({q.X}, {q.Y}) corner {where}: drawn {drawn}, ground {ground}");
}
