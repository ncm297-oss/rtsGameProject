using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Spatial;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>QA attacks on the M1-4a spatial hash, with QA's own brute-force oracle (independent of SpatialHashTests).</summary>
public class SpatialHashQaTests
{
    private readonly ITestOutputHelper _out;

    public SpatialHashQaTests(ITestOutputHelper output) => _out = output;

    // ---------- oracle: the documented rules, applied to every live slot ----------

    private static bool RadiusMatch(Vector2 p, Vector2 c, float r)
    {
        float dx = p.X - c.X;
        float dy = p.Y - c.Y;
        return dx * dx + dy * dy <= r * r;
    }

    private static List<int> OracleRadius(UnitStore u, Vector2 c, float r)
    {
        var res = new List<int>();
        if (float.IsNaN(r) || r < 0 || !float.IsFinite(c.X) || !float.IsFinite(c.Y)) return res;
        for (int s = 0; s < u.Capacity; s++)
            if (u.Alive[s] && RadiusMatch(u.Position[s], c, r)) res.Add(s);
        return res;
    }

    private static List<int> OracleRect(UnitStore u, Vector2 a, Vector2 b)
    {
        var res = new List<int>();
        if (float.IsNaN(a.X) || float.IsNaN(a.Y) || float.IsNaN(b.X) || float.IsNaN(b.Y)) return res;
        float x0 = a.X < b.X ? a.X : b.X, x1 = a.X < b.X ? b.X : a.X;
        float y0 = a.Y < b.Y ? a.Y : b.Y, y1 = a.Y < b.Y ? b.Y : a.Y;
        for (int s = 0; s < u.Capacity; s++)
        {
            if (!u.Alive[s]) continue;
            Vector2 p = u.Position[s];
            if (x0 <= p.X && p.X <= x1 && y0 <= p.Y && p.Y <= y1) res.Add(s);
        }
        return res;
    }

    private static int OracleNearest(UnitStore u, Vector2 c, float r, int player)
    {
        List<int> inRange = OracleRadius(u, c, r);
        int best = -1;
        float bestD = float.PositiveInfinity;
        foreach (int s in inRange) // ascending slot order, so strict < keeps the lowest slot on ties
        {
            if (u.Owner[s] == player) continue;
            float dx = u.Position[s].X - c.X, dy = u.Position[s].Y - c.Y;
            float d = dx * dx + dy * dy;
            if (best < 0 || d < bestD)
            {
                best = s;
                bestD = d;
            }
        }
        return best;
    }

    // ---------- world builders ----------

    private enum Layout { Random, Clustered, OneBucket, BucketEdges, OutsideMap, SamePoint }

    private static Vector2 Place(ref SimRng rng, Layout layout, float mapM, Vector2[] clusters)
    {
        float B = SpatialHash.BucketSize;
        switch (layout)
        {
            case Layout.Random:
                return new Vector2(rng.NextFloat() * mapM, rng.NextFloat() * mapM);
            case Layout.Clustered:
            {
                Vector2 c = clusters[rng.NextInt(0, clusters.Length)];
                return c + new Vector2((rng.NextFloat() - 0.5f) * 12f, (rng.NextFloat() - 0.5f) * 12f);
            }
            case Layout.OneBucket:
                return new Vector2(2 * B + rng.NextFloat() * B * 0.999f, 2 * B + rng.NextFloat() * B * 0.999f);
            case Layout.BucketEdges:
            {
                int buckets = (int)(mapM / B);
                float x = rng.NextInt(0, buckets + 1) * B, y = rng.NextInt(0, buckets + 1) * B;
                int k = rng.NextInt(0, 3);
                if (k == 1) x = MathF.BitDecrement(x);
                if (k == 2) y = MathF.BitIncrement(y);
                return new Vector2(x, y);
            }
            case Layout.OutsideMap:
                switch (rng.NextInt(0, 6))
                {
                    case 0: return new Vector2(-rng.NextFloat() * 30f, rng.NextFloat() * mapM);
                    case 1: return new Vector2(mapM + rng.NextFloat() * 30f, -rng.NextFloat() * 30f);
                    case 2: return new Vector2(rng.NextInt(0, 2) == 0 ? -1e7f : 1e7f, rng.NextInt(0, 2) == 0 ? -1e7f : mapM);
                    case 3: return new Vector2(mapM, mapM); // exactly the far corner
                    case 4: return new Vector2(-0f, MathF.BitDecrement(0f));
                    default: return new Vector2(rng.NextFloat() * mapM, rng.NextFloat() * mapM);
                }
            default:
                return new Vector2(7.5f, 7.5f);
        }
    }

    private static Vector2 QueryPoint(ref SimRng rng, float mapM, UnitStore u)
    {
        float B = SpatialHash.BucketSize;
        switch (rng.NextInt(0, 7))
        {
            case 0: return new Vector2(rng.NextInt(-2, (int)(mapM / B) + 3) * B, rng.NextInt(-2, (int)(mapM / B) + 3) * B);
            case 1:
            {
                int s = rng.NextInt(0, u.Capacity);
                return u.Position[s]; // dead slots too: their old point
            }
            case 2: return new Vector2(-20f - rng.NextFloat() * 100f, mapM + rng.NextFloat() * 100f);
            case 3: return new Vector2(rng.NextInt(0, 2) == 0 ? -1e7f : 1e7f, rng.NextFloat() * mapM);
            default: return new Vector2(rng.NextFloat() * (mapM + 40f) - 20f, rng.NextFloat() * (mapM + 40f) - 20f);
        }
    }

    private static float QueryRadiusValue(ref SimRng rng, float mapM)
    {
        switch (rng.NextInt(0, 12))
        {
            case 0: return 0f;
            case 1: return -0f;
            case 2: return float.Epsilon;
            case 3: return SpatialHash.BucketSize;
            case 4: return 8f;
            case 5: return mapM * 2f;
            case 6: return float.PositiveInfinity;
            case 7: return float.MaxValue;
            case 8: return float.NaN;
            case 9: return -1f;
            case 10: return 1e30f;
            default: return rng.NextFloat() * 24f;
        }
    }

    private static Vector2 RectCorner(ref SimRng rng, float mapM, UnitStore u)
    {
        switch (rng.NextInt(0, 8))
        {
            case 0: return new Vector2(float.NegativeInfinity, rng.NextFloat() * mapM);
            case 1: return new Vector2(rng.NextFloat() * mapM, float.PositiveInfinity);
            case 2: return rng.NextInt(0, 4) == 0 ? new Vector2(float.NaN, 0) : u.Position[rng.NextInt(0, u.Capacity)];
            default: return QueryPoint(ref rng, mapM, u);
        }
    }

    private delegate int SpanQuery(Span<int> results);

    private const int Guard = 8;
    private const int Sentinel = -7777;

    /// <summary>Calls a query with a buffer of <paramref name="len"/> slots followed by guard cells; checks count, prefix, and no overrun.</summary>
    private static void CheckList(string what, List<int> expected, int len, SpanQuery query)
    {
        var buf = new int[len + Guard];
        Array.Fill(buf, Sentinel);
        int n = query(buf.AsSpan(0, len));
        Assert.True(n == expected.Count, $"{what}: returned {n}, expected {expected.Count}");
        int w = Math.Min(n, len);
        for (int i = 0; i < w; i++)
            Assert.True(buf[i] == expected[i], $"{what}: result[{i}] = {buf[i]}, expected {expected[i]} (len {len})");
        for (int i = w; i < len + Guard; i++)
            Assert.True(buf[i] == Sentinel, $"{what}: wrote past the match count or the span at {i} (len {len}, n {n})");
    }

    public static IEnumerable<object[]> FuzzCases()
    {
        foreach (int map in new[] { 32, 33, 1024 })
            foreach (Layout layout in Enum.GetValues<Layout>())
                foreach (int count in new[] { 1, 2, 7, 64, 500, 2500 })
                    yield return new object[] { map, layout.ToString(), count };
    }

    [Theory]
    [MemberData(nameof(FuzzCases))]
    public void Fuzz_AllQueries_MatchOracle_BeforeAndAfterFreeAndRespawn(int mapCells, string layoutName, int count)
    {
        Layout layout = Enum.Parse<Layout>(layoutName);
        float mapM = mapCells * 2f;
        var rng = new SimRng((ulong)(mapCells * 1000 + count), (ulong)layout);
        var clusters = new Vector2[4];
        for (int i = 0; i < clusters.Length; i++) clusters[i] = new Vector2(rng.NextFloat() * mapM, rng.NextFloat() * mapM);

        int capacity = count + 3;
        var u = new UnitStore(capacity);
        var handles = new EntityHandle[capacity];
        Array.Fill(handles, new EntityHandle(-1, -1)); // never alive
        for (int i = 0; i < count; i++)
        {
            handles[i] = u.Alloc();
            u.Position[handles[i].Index] = Place(ref rng, layout, mapM, clusters);
            u.Owner[handles[i].Index] = rng.NextInt(0, 3);
        }
        var hash = new SpatialHash(capacity, mapCells, mapCells);

        int queries = 0;
        for (int phase = 0; phase < 3; phase++)
        {
            hash.Rebuild(u);
            Assert.Equal(u.Count, hash.Count);
            int perKind = count >= 2500 ? 25 : 60;
            for (int q = 0; q < perKind; q++)
            {
                Vector2 c = QueryPoint(ref rng, mapM, u);
                float r = QueryRadiusValue(ref rng, mapM);
                List<int> exp = OracleRadius(u, c, r);
                int len = rng.NextInt(0, 4) == 0 ? rng.NextInt(0, exp.Count + 2) : capacity;
                CheckList($"radius c={c} r={r} phase {phase}", exp, len, span => hash.QueryRadius(c, r, span));

                Vector2 a = RectCorner(ref rng, mapM, u), b = RectCorner(ref rng, mapM, u);
                List<int> expR = OracleRect(u, a, b);
                len = rng.NextInt(0, 4) == 0 ? rng.NextInt(0, expR.Count + 2) : capacity;
                CheckList($"rect {a}..{b} phase {phase}", expR, len, span => hash.QueryRect(a, b, span));
                CheckList($"rect swapped {b}..{a}", expR, capacity, span => hash.QueryRect(b, a, span));

                int player = rng.NextInt(-1, 4);
                int expN = OracleNearest(u, c, r, player);
                bool found = hash.NearestEnemy(c, r, player, out int slot);
                Assert.True(found == (expN >= 0) && slot == expN, $"nearest c={c} r={r} p={player} phase {phase}: got {found}/{slot}, expected {expN}");
                queries += 4;
            }

            // Free about a third, then respawn into the freed slots (LIFO free list) somewhere else.
            for (int i = 0; i < capacity; i++)
            {
                if (u.IsAlive(handles[i]) && rng.NextInt(0, 3) == 0) u.Free(handles[i]);
            }
            int respawn = rng.NextInt(0, u.FreeCount + 1);
            for (int k = 0; k < respawn; k++)
            {
                EntityHandle h = u.Alloc();
                handles[h.Index] = h;
                u.Position[h.Index] = Place(ref rng, (Layout)rng.NextInt(0, 6), mapM, clusters);
                u.Owner[h.Index] = rng.NextInt(0, 3);
            }
        }
        Assert.True(queries > 0);
    }

    [Fact]
    public void FuzzCaseCount_IsAtLeastAThousandQueries()
    {
        // 108 worlds x 3 phases x >= 25 x 4 queries; guards against the fuzz shrinking unnoticed.
        Assert.True(FuzzCases().Count() * 3 * 25 * 4 >= 1000);
    }

    [Fact]
    public void FreedSlots_NeverSurface_AndRespawnUsesNewPositionOnly()
    {
        var u = new UnitStore(4);
        EntityHandle a = u.Alloc(), b = u.Alloc();
        u.Position[a.Index] = new Vector2(10, 10);
        u.Position[b.Index] = new Vector2(100, 100);
        u.Owner[b.Index] = 1;
        var hash = new SpatialHash(4, 64, 64);
        hash.Rebuild(u);
        u.Free(b);
        hash.Rebuild(u);
        var buf = new int[4];
        Assert.Equal(0, hash.QueryRadius(new Vector2(100, 100), 5f, buf));
        Assert.False(hash.NearestEnemy(new Vector2(100, 100), 500f, 0, out _));

        EntityHandle c = u.Alloc(); // same slot, next generation
        Assert.Equal(b.Index, c.Index);
        u.Position[c.Index] = new Vector2(20, 20);
        u.Owner[c.Index] = 1;
        hash.Rebuild(u);
        Assert.Equal(0, hash.QueryRadius(new Vector2(100, 100), 5f, buf));
        Assert.Equal(1, hash.QueryRadius(new Vector2(20, 20), 0f, buf));
        Assert.Equal(c.Index, buf[0]);
        Assert.True(hash.NearestEnemy(new Vector2(100, 100), 500f, 0, out int s));
        Assert.Equal(c.Index, s);
    }

    [Fact]
    public void SameResults_EveryCall_AndAcrossTwoSimulations()
    {
        Simulation MakeSim()
        {
            var sim = new Simulation(new SimConfig(Seed: 77, PlayerCount: 3, UnitCapacity: 700, CommandCapacity: 1024));
            var rng = new SimRng(5, 9);
            for (int i = 0; i < 600; i++)
                sim.Enqueue(Command.SpawnUnit(rng.NextInt(0, 3), 0, new Vector2(rng.NextFloat() * 300f - 22f, rng.NextInt(0, 64) * 4f)));
            sim.Tick();
            sim.Tick();
            return sim;
        }
        Simulation s1 = MakeSim(), s2 = MakeSim();
        var b1 = new int[700];
        var b2 = new int[700];
        var b3 = new int[700];
        var rq = new SimRng(1, 1);
        for (int q = 0; q < 300; q++)
        {
            var c = new Vector2(rq.NextFloat() * 300f - 22f, rq.NextFloat() * 280f);
            float r = rq.NextFloat() * 30f;
            int n1 = s1.World.Spatial.QueryRadius(c, r, b1);
            int n3 = s1.World.Spatial.QueryRadius(c, r, b3);
            int n2 = s2.World.Spatial.QueryRadius(c, r, b2);
            Assert.Equal(n1, n2);
            Assert.Equal(n1, n3);
            Assert.True(b1.AsSpan(0, n1).SequenceEqual(b2.AsSpan(0, n2)));
            Assert.True(b1.AsSpan(0, n1).SequenceEqual(b3.AsSpan(0, n3)));
            Assert.Equal(s1.World.Spatial.NearestEnemy(c, r, q % 3, out int x1), s2.World.Spatial.NearestEnemy(c, r, q % 3, out int x2));
            Assert.Equal(x1, x2);
        }
        Assert.Equal(s1.StateHash(), s2.StateHash());
    }

    [Fact]
    public void ExplicitRebuild_DoesNotChangeStateHash_OrLaterTicks()
    {
        var a = new Simulation(new SimConfig(3, 2, 64, 64));
        var b = new Simulation(new SimConfig(3, 2, 64, 64));
        for (int i = 0; i < 40; i++)
        {
            a.Enqueue(Command.SpawnUnit(i % 2, 0, new Vector2(i * 3f, i * 2f)));
            b.Enqueue(Command.SpawnUnit(i % 2, 0, new Vector2(i * 3f, i * 2f)));
        }
        for (int t = 0; t < 10; t++)
        {
            a.Tick();
            b.Tick();
            b.World.Spatial.Rebuild(b.World.Units);
            b.World.Spatial.Rebuild(b.World.Units);
            Assert.Equal(a.StateHash(), b.StateHash());
        }
    }

    // ---------- edge arguments ----------

    [Fact]
    public void RadiusZero_AndNegativeZero_MatchExactPointOnly()
    {
        var u = new UnitStore(3);
        u.Alloc();
        u.Alloc();
        u.Position[0] = new Vector2(8f, 8f); // exactly on a bucket corner
        u.Position[1] = new Vector2(MathF.BitIncrement(8f), 8f);
        var h = new SpatialHash(3, 32, 32);
        h.Rebuild(u);
        var buf = new int[3];
        Assert.Equal(1, h.QueryRadius(new Vector2(8f, 8f), 0f, buf));
        Assert.Equal(0, buf[0]);
        Assert.Equal(1, h.QueryRadius(new Vector2(8f, 8f), -0f, buf));
        Assert.Equal(0, h.QueryRadius(new Vector2(8f, 8f), float.NaN, buf));
        Assert.Equal(0, h.QueryRadius(new Vector2(8f, 8f), float.NegativeInfinity, buf));
        Assert.Equal(2, h.QueryRadius(new Vector2(8f, 8f), float.PositiveInfinity, buf));
        Assert.Equal(0, h.QueryRadius(new Vector2(float.NaN, 8f), 100f, buf));
        Assert.Equal(0, h.QueryRadius(new Vector2(float.PositiveInfinity, 8f), float.PositiveInfinity, buf));
        Assert.Equal(1, h.QueryRect(new Vector2(8f, 8f), new Vector2(8f, 8f), buf)); // degenerate rect = the point
        Assert.Equal(2, h.QueryRect(new Vector2(float.PositiveInfinity, float.PositiveInfinity), new Vector2(float.NegativeInfinity, float.NegativeInfinity), buf));
    }

    [Fact]
    public void EmptyBuffer_ReturnsCount_WritesNothing()
    {
        var u = new UnitStore(10);
        for (int i = 0; i < 10; i++)
        {
            u.Alloc();
            u.Position[i] = new Vector2(5f, 5f);
        }
        var h = new SpatialHash(10, 32, 32);
        h.Rebuild(u);
        Assert.Equal(10, h.QueryRadius(new Vector2(5, 5), 1f, Span<int>.Empty));
        Assert.Equal(10, h.QueryRect(new Vector2(0, 0), new Vector2(9, 9), Span<int>.Empty));
    }

    [Fact(Skip = "BUG-0016: NearestEnemy returns a NaN-positioned unit that QueryRadius excludes; un-skip when fixed")]
    public void NearestEnemy_IgnoresUnitWithNaNPosition_LikeQueryRadius()
    {
        // docs/03 + XML doc: NearestEnemy "uses the QueryRadius match rule". A NaN point fails that rule
        // (NaN <= r2 is false), so it must not be the nearest enemy either. Spawn rejects NaN today
        // (BUG-0006), but a future movement bug could write one; the queries must stay consistent.
        var u = new UnitStore(3);
        u.Alloc();
        u.Alloc();
        u.Position[0] = new Vector2(float.NaN, float.NaN);
        u.Owner[0] = 1;
        u.Position[1] = new Vector2(1f, 1f);
        u.Owner[1] = 1;
        var h = new SpatialHash(3, 32, 32);
        h.Rebuild(u);
        var buf = new int[3];
        int n = h.QueryRadius(new Vector2(0, 0), 10f, buf);
        Assert.Equal(1, n);
        Assert.Equal(1, buf[0]);
        Assert.True(h.NearestEnemy(new Vector2(0, 0), 10f, 0, out int slot));
        Assert.Equal(1, slot); // fails: returns slot 0, the NaN unit
    }

    [Fact]
    public void SimTick_RebuildsAfterExternalFree_NoStaleSlot()
    {
        var sim = new Simulation(new SimConfig(1, 2, 8, 16));
        sim.Enqueue(Command.SpawnUnit(1, 0, new Vector2(50, 50)));
        sim.Tick();
        sim.Tick();
        var buf = new int[8];
        Assert.Equal(1, sim.World.Spatial.QueryRadius(new Vector2(50, 50), 1f, buf));
        sim.World.Units.Free(new EntityHandle(buf[0], sim.World.Units.Generation[buf[0]]));
        sim.Tick();
        Assert.Equal(0, sim.World.Spatial.QueryRadius(new Vector2(50, 50), 1f, buf));
        Assert.False(sim.World.Spatial.NearestEnemy(new Vector2(50, 50), 1000f, 0, out _));
    }

    [Fact]
    public void MillionQueries_AllocateNothing()
    {
        var u = new UnitStore(512);
        var rng = new SimRng(8, 8);
        for (int i = 0; i < 500; i++)
        {
            u.Alloc();
            u.Position[i] = new Vector2(rng.NextFloat() * 300f - 20f, rng.NextFloat() * 300f - 20f);
            u.Owner[i] = i % 3;
        }
        var h = new SpatialHash(512, 128, 128);
        var buf = new int[16];
        long sink = Run(h, u, buf, 2000);
        long before = GC.GetAllocatedBytesForCurrentThread();
        h.Rebuild(u);
        sink += Run(h, u, buf, 1_000_000);
        long delta = GC.GetAllocatedBytesForCurrentThread() - before;
        _out.WriteLine($"1M queries allocated {delta} bytes (sink {sink})");
        Assert.Equal(0, delta);

        static long Run(SpatialHash h, UnitStore u, int[] buf, int n)
        {
            long s = 0;
            for (int i = 0; i < n; i++)
            {
                Vector2 c = u.Position[i % 500];
                switch (i & 3)
                {
                    case 0: s += h.QueryRadius(c, (i % 17) * 1.5f, buf); break;
                    case 1: s += h.QueryRect(c, c - new Vector2(9f, -7f), buf); break;
                    case 2: s += h.NearestEnemy(c, 12f, i % 3, out int x) ? x : -1; break;
                    default: s += h.QueryRadius(c, i % 50 == 0 ? float.PositiveInfinity : float.NaN, buf); break;
                }
            }
            return s;
        }
    }
}
