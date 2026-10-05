using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Spatial;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>SpatialHash (M1-4a): brute-force equivalence, slot order, boundaries, clamping, truncation, nearest enemy, perf.</summary>
[Collection(SerialCollection.Name)]
public class SpatialHashTests
{
    private const int MapCells = 128;
    private const float MapMeters = MapCells * MapConstants.CellSize;
    private const float B = SpatialHash.BucketSize;

    private readonly ITestOutputHelper _out;

    public SpatialHashTests(ITestOutputHelper output) => _out = output;

    // ---------- helpers ----------

    /// <summary>A store with one unit per position (slot i = positions[i]); slots listed in <paramref name="dead"/> are freed.</summary>
    private static UnitStore Store(Vector2[] positions, int[]? owners = null, int[]? dead = null, int capacity = 0)
    {
        var u = new UnitStore(Math.Max(capacity, Math.Max(1, positions.Length)));
        var handles = new EntityHandle[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            handles[i] = u.Alloc();
            Assert.Equal(i, handles[i].Index);
            u.Position[i] = positions[i];
            u.Owner[i] = owners?[i] ?? 0;
        }
        foreach (int d in dead ?? Array.Empty<int>()) u.Free(handles[d]);
        return u;
    }

    private static SpatialHash Built(UnitStore u)
    {
        var hash = new SpatialHash(u.Capacity, MapCells, MapCells);
        hash.Rebuild(u);
        return hash;
    }

    // The documented match rules, applied to every slot in index order.
    private static List<int> BruteRadius(UnitStore u, Vector2 c, float r)
    {
        var list = new List<int>();
        if (!(r >= 0f) || !float.IsFinite(c.X) || !float.IsFinite(c.Y)) return list;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            float dx = u.Position[i].X - c.X, dy = u.Position[i].Y - c.Y;
            if (dx * dx + dy * dy <= r * r) list.Add(i);
        }
        return list;
    }

    private static List<int> BruteRect(UnitStore u, Vector2 a, Vector2 b)
    {
        var list = new List<int>();
        float lx = Math.Min(a.X, b.X), hx = Math.Max(a.X, b.X), ly = Math.Min(a.Y, b.Y), hy = Math.Max(a.Y, b.Y);
        for (int i = 0; i < u.Capacity; i++)
        {
            Vector2 p = u.Position[i];
            if (u.Alive[i] && p.X >= lx && p.X <= hx && p.Y >= ly && p.Y <= hy) list.Add(i);
        }
        return list;
    }

    private static int BruteNearest(UnitStore u, Vector2 c, float r, int player)
    {
        int best = -1;
        float bestD2 = 0;
        if (!(r >= 0f) || !float.IsFinite(c.X) || !float.IsFinite(c.Y)) return best;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.Owner[i] == player) continue;
            float dx = u.Position[i].X - c.X, dy = u.Position[i].Y - c.Y;
            float d2 = dx * dx + dy * dy;
            if (d2 > r * r) continue;
            if (best < 0 || d2 < bestD2) // strict: the first (lowest) slot wins ties
            {
                best = i;
                bestD2 = d2;
            }
        }
        return best;
    }

    private static int[] Radius(SpatialHash h, Vector2 c, float r)
    {
        var buf = new int[h.Capacity];
        int n = h.QueryRadius(c, r, buf);
        return buf.AsSpan(0, n).ToArray();
    }

    private static int[] Rect(SpatialHash h, Vector2 a, Vector2 b)
    {
        var buf = new int[h.Capacity];
        int n = h.QueryRect(a, b, buf);
        return buf.AsSpan(0, n).ToArray();
    }

    private static float Coord(ref SimRng rng)
    {
        switch (rng.NextInt(0, 8))
        {
            case 0: return rng.NextInt(0, MapCells / SpatialHash.BucketCells + 1) * B;          // bucket boundary
            case 1: return rng.NextInt(0, MapCells + 1) * MapConstants.CellSize;                // cell boundary
            case 2: return -rng.NextFloat() * 50f;                                              // outside, low side
            case 3: return MapMeters + rng.NextFloat() * 50f;                                   // outside, high side (incl. exactly the edge)
            case 4: return rng.NextInt(0, 2) == 0 ? -1e6f : 1e6f;                               // far outside
            case 5: return rng.NextInt(0, 64);                                                  // integer meters: many exact ties
            default: return rng.NextFloat() * MapMeters;
        }
    }

    private static float FuzzRadius(ref SimRng rng)
    {
        switch (rng.NextInt(0, 8))
        {
            case 0: return 0f;
            case 1: return B * rng.NextInt(1, 4);
            case 2: return 1000f;               // larger than the map
            case 3: return float.MaxValue;
            case 4: return rng.NextInt(0, 2) == 0 ? float.PositiveInfinity : -1f;
            default: return rng.NextFloat() * 20f;
        }
    }

    private static (UnitStore Units, Vector2[] Positions) RandomStore(ref SimRng rng, int count)
    {
        var positions = new Vector2[count];
        var owners = new int[count];
        for (int i = 0; i < count; i++)
        {
            positions[i] = new Vector2(Coord(ref rng), Coord(ref rng));
            owners[i] = rng.NextInt(0, 3);
        }
        var dead = new List<int>();
        for (int i = 0; i < count; i++)
            if (rng.NextInt(0, 6) == 0) dead.Add(i);
        return (Store(positions, owners, dead.ToArray(), capacity: count + 4), positions);
    }

    // ---------- brute-force equivalence ----------

    [Fact]
    public void Fuzz_RadiusRectAndNearest_MatchBruteForce()
    {
        var rng = new SimRng(1234, 0);
        int cases = 0, nonEmpty = 0;
        for (int world = 0; world < 60; world++)
        {
            (UnitStore u, Vector2[] pos) = RandomStore(ref rng, rng.NextInt(1, 120));
            SpatialHash h = Built(u);
            for (int q = 0; q < 20; q++)
            {
                // Half the centers sit exactly on a unit, so radius 0 has something to find.
                Vector2 c = rng.NextInt(0, 2) == 0 ? pos[rng.NextInt(0, pos.Length)] : new Vector2(Coord(ref rng), Coord(ref rng));
                float r = FuzzRadius(ref rng);
                int[] expected = BruteRadius(u, c, r).ToArray();
                Assert.Equal(expected, Radius(h, c, r));
                if (expected.Length > 0) nonEmpty++;

                Vector2 a = new(Coord(ref rng), Coord(ref rng)), b = new(Coord(ref rng), Coord(ref rng));
                Assert.Equal(BruteRect(u, a, b).ToArray(), Rect(h, a, b));

                int player = rng.NextInt(0, 3);
                int want = BruteNearest(u, c, r, player);
                bool found = h.NearestEnemy(c, r, player, out int got);
                Assert.True(want == got && found == (want >= 0), $"nearest at {c} r {r} player {player}: brute {want}, hash {got} ({found})");
                cases += 3;
            }
        }
        _out.WriteLine($"{cases} fuzzed queries, {nonEmpty} non-empty radius results");
        Assert.True(cases >= 3000);
        Assert.True(nonEmpty > 300);
    }

    [Fact]
    public void Results_AreInAscendingSlotOrder_AcrossBuckets()
    {
        // Slot order deliberately opposite to bucket order: slot 0 in the last bucket.
        var positions = new Vector2[16];
        for (int i = 0; i < positions.Length; i++) positions[i] = new Vector2(60f - i * 3.7f, 60f - i * 2.9f);
        SpatialHash h = Built(Store(positions));
        int[] all = Radius(h, new Vector2(40, 40), 100f);
        Assert.Equal(Enumerable.Range(0, 16).ToArray(), all);
        Assert.Equal(Enumerable.Range(0, 16).ToArray(), Rect(h, new Vector2(100, 100), new Vector2(0, 0)));
    }

    // ---------- boundaries and clamping ----------

    [Fact]
    public void UnitsOnBucketBoundaries_AreFound()
    {
        var positions = new[] { new Vector2(B, B), new Vector2(2 * B, 0f), new Vector2(0f, 0f), new Vector2(MapMeters, MapMeters) };
        SpatialHash h = Built(Store(positions));
        Assert.Equal(new[] { 0 }, Radius(h, new Vector2(B - 0.01f, B - 0.01f), 0.02f)); // query centered in the neighbouring bucket
        Assert.Equal(new[] { 0 }, Radius(h, new Vector2(B, B), 0f));
        Assert.Equal(new[] { 1 }, Rect(h, new Vector2(2 * B, 0f), new Vector2(2 * B, 0f)));       // degenerate rect, edges inclusive
        Assert.Equal(new[] { 0, 1, 2 }, Rect(h, new Vector2(0, 0), new Vector2(2 * B, B)));
        Assert.Equal(new[] { 3 }, Radius(h, new Vector2(MapMeters, MapMeters), 0f));           // the far edge is outside the map, clamped
    }

    [Fact]
    public void UnitsOutsideTheMap_ClampIntoEdgeBuckets_AndAreFound()
    {
        var positions = new[] { new Vector2(-50f, -50f), new Vector2(1e6f, 10f), new Vector2(10f, -1e9f), new Vector2(float.MaxValue, float.MaxValue) };
        SpatialHash h = Built(Store(positions));
        Assert.Equal(0, h.BucketX(-50f));
        Assert.Equal(h.BucketsX - 1, h.BucketX(1e6f));
        Assert.Equal(new[] { 0 }, Radius(h, new Vector2(-50f, -50f), 1f));
        Assert.Equal(new[] { 1 }, Radius(h, new Vector2(1e6f, 10f), 0f));
        Assert.Equal(new[] { 2 }, Rect(h, new Vector2(0f, -2e9f), new Vector2(20f, -1e8f)));
        Assert.Equal(new[] { 3 }, Rect(h, new Vector2(1e30f, 1e30f), new Vector2(float.MaxValue, float.MaxValue)));
        Assert.Equal(new[] { 0, 1, 2, 3 }, Radius(h, new Vector2(128f, 128f), float.PositiveInfinity));
        Assert.Equal(new[] { 0, 1, 2, 3 }, Rect(h, new Vector2(float.NegativeInfinity, float.NegativeInfinity), new Vector2(float.PositiveInfinity, float.PositiveInfinity)));
    }

    [Fact]
    public void InvalidQueryArguments_MatchNothing()
    {
        SpatialHash h = Built(Store(new[] { new Vector2(10, 10) }, new[] { 1 }));
        Assert.Empty(Radius(h, new Vector2(10, 10), -1f));
        Assert.Empty(Radius(h, new Vector2(10, 10), float.NaN));
        Assert.Empty(Radius(h, new Vector2(float.NaN, 10), 5f));
        Assert.Empty(Radius(h, new Vector2(float.PositiveInfinity, 10), 5f));
        Assert.Empty(Rect(h, new Vector2(float.NaN, 0), new Vector2(20, 20)));
        Assert.False(h.NearestEnemy(new Vector2(10, 10), float.NaN, 0, out int s));
        Assert.Equal(-1, s);
    }

    [Fact]
    public void SmallAndOddSizedMaps_CoverEveryCell()
    {
        var hash = new SpatialHash(4, 17, 3); // odd width: the last bucket column is half a bucket
        Assert.Equal(9, hash.BucketsX);
        Assert.Equal(2, hash.BucketsY);
        var u = Store(new[] { new Vector2(33.9f, 5.9f), new Vector2(0f, 0f) });
        hash.Rebuild(u);
        Assert.Equal(2, hash.Count);
        Span<int> buf = stackalloc int[4];
        Assert.Equal(1, hash.QueryRadius(new Vector2(33.9f, 5.9f), 0f, buf));
        Assert.Equal(0, buf[0]);
    }

    // ---------- truncation ----------

    [Fact]
    public void SmallBuffer_GetsLowestSlots_ReturnValueReportsTotal()
    {
        var positions = new Vector2[10];
        for (int i = 0; i < positions.Length; i++) positions[i] = new Vector2(100f - i * 5f, 50f);
        SpatialHash h = Built(Store(positions));
        Span<int> buf = stackalloc int[3];
        buf.Fill(-7);
        int n = h.QueryRadius(new Vector2(80, 50), 1000f, buf);
        Assert.Equal(10, n);                       // > buf.Length: truncated
        Assert.Equal(new[] { 0, 1, 2 }, buf.ToArray());
        Assert.Equal(10, h.QueryRect(new Vector2(0, 0), new Vector2(200, 200), buf));
        Assert.Equal(new[] { 0, 1, 2 }, buf.ToArray());
        Assert.Equal(10, h.QueryRadius(new Vector2(80, 50), 1000f, Span<int>.Empty));
    }

    // ---------- nearest enemy ----------

    [Fact]
    public void NearestEnemy_TiesGoToLowestSlot_IgnoresOwnUnitsAndDeadSlots()
    {
        var c = new Vector2(100f, 100f);
        var positions = new[]
        {
            new Vector2(100.5f, 100f), // 0: own unit, closest of all
            new Vector2(103f, 100f),   // 1: dead enemy at distance 3
            new Vector2(104f, 100f),   // 2: enemy at distance 4, in the bucket to the right
            new Vector2(96f, 100f),    // 3: enemy at distance 4, bucket to the left (visited first)
            new Vector2(100f, 96f),    // 4: enemy at distance 4, bucket above (visited before both)
            new Vector2(100f, 120f),   // 5: enemy beyond the radius
        };
        var owners = new[] { 0, 1, 1, 2, 1, 1 };
        SpatialHash h = Built(Store(positions, owners, dead: new[] { 1 }));
        Assert.True(h.NearestEnemy(c, 10f, player: 0, out int slot));
        Assert.Equal(2, slot);
        Assert.True(h.NearestEnemy(c, 10f, player: 1, out slot));
        Assert.Equal(0, slot);                    // player 1 sees player 0's unit as the enemy
        Assert.False(h.NearestEnemy(c, 3.99f, player: 0, out slot));
        Assert.Equal(-1, slot);
        Assert.True(h.NearestEnemy(c, 4f, player: 0, out slot)); // inclusive radius
        Assert.Equal(2, slot);
    }

    // ---------- simulation integration and determinism ----------

    private static Simulation SimWithUnits(int count)
    {
        var sim = new Simulation(TestSim.Config(Seed: 9, PlayerCount: 2, UnitCapacity: count + 8, CommandCapacity: count + 8));
        var rng = new SimRng(99, 0);
        for (int i = 0; i < count; i++)
            sim.Enqueue(Command.SpawnUnit(i % 2, typeId: 0, new Vector2(rng.NextFloat() * MapMeters, rng.NextFloat() * MapMeters)));
        sim.Tick();
        sim.Tick(); // the spawns apply on tick 1
        Assert.Equal(count, sim.World.Spatial.Count);
        return sim;
    }

    [Fact]
    public void SpawnedUnits_AreQueryableTheSameTick()
    {
        var sim = new Simulation(TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: 8, CommandCapacity: 8));
        sim.Enqueue(Command.SpawnUnit(1, typeId: 0, new Vector2(30f, 40f))); // stamped for tick 1
        sim.Tick();
        Assert.Equal(0, sim.World.Spatial.Count);
        sim.Tick(); // tick 1 applies the spawn, then rebuilds
        Assert.Equal(1, sim.World.Units.Count);
        Assert.Equal(1, sim.World.Spatial.Count);
        Assert.True(sim.World.Spatial.NearestEnemy(new Vector2(31f, 40f), 5f, player: 0, out int slot));
        Assert.Equal(0, slot);
    }

    [Fact]
    public void SameState_GivesIdenticalQueryOutput_RepeatedAndAcrossSimulations()
    {
        Simulation a = SimWithUnits(300), b = SimWithUnits(300);
        Assert.Equal(a.StateHash(), b.StateHash());
        var rng = new SimRng(5, 0);
        for (int q = 0; q < 200; q++)
        {
            var c = new Vector2(rng.NextFloat() * MapMeters, rng.NextFloat() * MapMeters);
            float r = rng.NextFloat() * 30f;
            int[] first = Radius(a.World.Spatial, c, r);
            Assert.Equal(first, Radius(a.World.Spatial, c, r));
            Assert.Equal(first, Radius(b.World.Spatial, c, r));
            var corner = c + new Vector2(20f, -15f);
            Assert.Equal(Rect(a.World.Spatial, c, corner), Rect(b.World.Spatial, c, corner));
            Assert.Equal(a.World.Spatial.NearestEnemy(c, r, 0, out int sa), b.World.Spatial.NearestEnemy(c, r, 0, out int sb));
            Assert.Equal(sa, sb);
        }
    }

    [Fact]
    public void Rebuild_DoesNotChangeStateHash()
    {
        Simulation sim = SimWithUnits(50);
        ulong before = sim.StateHash();
        sim.World.Spatial.Rebuild(sim.World.Units);
        Assert.Equal(before, sim.StateHash());
    }

    [Fact]
    public void Source_UsesNoHashCollectionsOrLinq()
    {
        // Per-tick code: the spatial hash (M1-4a), flow fields and movement (M1-4b).
        string sim = Path.Combine(TestDataDir.RepoRoot(), "sim", "Rts.Sim");
        var files = new List<string> { Path.Combine(sim, "Spatial", "SpatialHash.cs") };
        foreach (string dir in new[] { "Pathfinding", "Movement" })
        {
            string[] inDir = Directory.GetFiles(Path.Combine(sim, dir), "*.cs");
            Assert.NotEmpty(inDir);
            files.AddRange(inDir);
        }
        foreach (string file in files)
        {
            string src = File.ReadAllText(file);
            foreach (string banned in new[] { "Dictionary", "HashSet", "System.Linq", "params " })
                Assert.False(src.Contains(banned, StringComparison.Ordinal), $"{Path.GetFileName(file)} uses {banned}");
        }
    }

    // ---------- perf ----------

    [Fact]
    [Trait("Category", "Perf")]
    public void Rebuild2500Units_AverageUnderHalfAMillisecond()
    {
        var rng = new SimRng(3, 0);
        var positions = new Vector2[2500];
        for (int i = 0; i < positions.Length; i++) positions[i] = new Vector2(rng.NextFloat() * MapMeters, rng.NextFloat() * MapMeters);
        UnitStore u = Store(positions);
        var h = new SpatialHash(u.Capacity, MapCells, MapCells);
        for (int i = 0; i < 50; i++) h.Rebuild(u); // JIT warm-up
        const int runs = 500;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < runs; i++) h.Rebuild(u);
        sw.Stop();
        double avg = sw.Elapsed.TotalMilliseconds / runs;
        _out.WriteLine($"rebuild 2500 units: {avg * 1000:F1} us average");
        Assert.True(avg < 0.5, $"rebuild took {avg:F3} ms");
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void FiveHundredRadius8Queries_Over500Units_UnderOneMillisecond()
    {
        var rng = new SimRng(4, 0);
        var positions = new Vector2[500];
        for (int i = 0; i < positions.Length; i++) positions[i] = new Vector2(rng.NextFloat() * MapMeters, rng.NextFloat() * MapMeters);
        SpatialHash h = Built(Store(positions));
        var buf = new int[500];
        int sink = 0;
        for (int i = 0; i < 500; i++) sink += h.QueryRadius(positions[i], 8f, buf); // warm-up
        double best = double.MaxValue;
        for (int round = 0; round < 5; round++)
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 500; i++) sink += h.QueryRadius(positions[i], 8f, buf);
            sw.Stop();
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
        }
        _out.WriteLine($"500 radius-8 queries: {best * 1000:F1} us (best of 5), sink {sink}");
        Assert.True(best < 1.0, $"queries took {best:F3} ms");
    }
}
