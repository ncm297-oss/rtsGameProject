using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Spatial;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>QA scale runs for the M1-4a spatial hash: 1x/2x/5x unit counts, big maps, worst-case clustering.</summary>
[Collection(SerialCollection.Name)]
public class SpatialHashStressTests
{
    private readonly ITestOutputHelper _out;

    public SpatialHashStressTests(ITestOutputHelper output) => _out = output;

    private static UnitStore Units(int count, float spreadM, float originM, ulong seed)
    {
        var u = new UnitStore(count);
        var rng = new SimRng(seed, 3);
        for (int i = 0; i < count; i++)
        {
            u.Alloc();
            u.Position[i] = new Vector2(originM + rng.NextFloat() * spreadM, originM + rng.NextFloat() * spreadM);
            u.Owner[i] = i % 4;
        }
        return u;
    }

    private static double AvgMs(Action a, int runs)
    {
        for (int i = 0; i < Math.Max(5, runs / 10); i++) a();
        double best = double.MaxValue;
        for (int round = 0; round < 3; round++)
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < runs; i++) a();
            sw.Stop();
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds / runs);
        }
        return best;
    }

    [Theory]
    [Trait("Category", "Perf")]
    [InlineData(500, 128)]
    [InlineData(1000, 128)]
    [InlineData(2500, 128)]
    [InlineData(2500, 256)]
    public void Rebuild_DesignMaps_UnderHalfMillisecond(int count, int mapCells)
    {
        UnitStore u = Units(count, mapCells * 2f, 0f, 1);
        var h = new SpatialHash(count, mapCells, mapCells);
        double ms = AvgMs(() => h.Rebuild(u), 300);
        _out.WriteLine($"rebuild {count} units on {mapCells}^2: {ms * 1000:F1} us");
        Assert.True(ms < 0.5, $"{ms:F3} ms");
    }

    [Theory]
    [Trait("Category", "Perf")]
    [InlineData(2500, 1024)]
    [InlineData(12500, 1024)]
    public void Rebuild_MaxMap_Report(int count, int mapCells)
    {
        // 1024^2 cells = 262,144 buckets cleared and prefix-summed every rebuild regardless of unit count.
        UnitStore u = Units(count, mapCells * 2f, 0f, 2);
        var h = new SpatialHash(count, mapCells, mapCells);
        double ms = AvgMs(() => h.Rebuild(u), 100);
        _out.WriteLine($"rebuild {count} units on {mapCells}^2: {ms * 1000:F1} us");
        Assert.True(ms < 5.0, $"{ms:F3} ms"); // a 10% tick budget guard, not the 0.5 ms design-map criterion
    }

    [Theory]
    [Trait("Category", "Perf")]
    [InlineData(500, 1.0)]
    [InlineData(1000, 2.0)]
    [InlineData(2500, 5.0)]
    public void FiveHundredRadius8Queries_ScaleLinearly(int count, double budgetMs)
    {
        UnitStore u = Units(count, 256f, 0f, 3);
        var h = new SpatialHash(count, 128, 128);
        h.Rebuild(u);
        var buf = new int[count];
        long sink = 0;
        double ms = AvgMs(() =>
        {
            for (int i = 0; i < 500; i++) sink += h.QueryRadius(u.Position[i], 8f, buf);
        }, 20);
        _out.WriteLine($"500 radius-8 queries over {count} units: {ms * 1000:F1} us (sink {sink})");
        Assert.True(ms < budgetMs, $"{ms:F3} ms");
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void WorstCase_AllUnitsInOneBucket_Report()
    {
        // 2,500 units in one 4 m bucket (a deathball); every radius-8 query returns all of them and sorts.
        UnitStore u = Units(2500, 3.9f, 40f, 4);
        var h = new SpatialHash(2500, 128, 128);
        h.Rebuild(u);
        var buf = new int[2500];
        long sink = 0;
        double ms = AvgMs(() =>
        {
            for (int i = 0; i < 500; i++) sink += h.QueryRadius(u.Position[i], 8f, buf);
        }, 3);
        double nearest = AvgMs(() =>
        {
            for (int i = 0; i < 500; i++) sink += h.NearestEnemy(u.Position[i], 8f, i % 4, out int s) ? s : 0;
        }, 3);
        _out.WriteLine($"one bucket, 500 radius queries: {ms:F2} ms; 500 nearest: {nearest:F2} ms (sink {sink})");
        Assert.True(ms < 50, $"{ms:F1} ms"); // one full tick
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void HugeRadius_OnMaxMap_Report()
    {
        UnitStore u = Units(500, 2048f, 0f, 5);
        var h = new SpatialHash(500, 1024, 1024);
        h.Rebuild(u);
        var buf = new int[500];
        long sink = 0;
        double ms = AvgMs(() => sink += h.QueryRadius(new Vector2(1000, 1000), float.PositiveInfinity, buf), 20);
        double rect = AvgMs(() => sink += h.QueryRect(new Vector2(float.NegativeInfinity, float.NegativeInfinity), new Vector2(float.PositiveInfinity, float.PositiveInfinity), buf), 20);
        _out.WriteLine($"1024^2 map, infinite radius query: {ms:F3} ms; whole-map rect: {rect:F3} ms (sink {sink})");
        Assert.True(ms < 5 && rect < 5);
    }
}
