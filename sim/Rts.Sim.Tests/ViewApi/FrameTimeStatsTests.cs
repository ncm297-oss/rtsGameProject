using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M2-7: bench frame-time mean, worst and percentiles from the histogram.</summary>
[Collection(SerialCollection.Name)]
public class FrameTimeStatsTests
{
    private readonly ITestOutputHelper _out;

    public FrameTimeStatsTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Empty_IsAllZero()
    {
        var s = new FrameTimeStats();
        Assert.Equal(0, s.Count);
        Assert.Equal(0, s.Average);
        Assert.Equal(0, s.Worst);
        Assert.Equal(0, s.Percentile(0.5));
    }

    [Fact]
    public void HundredFrames_MeanWorstAndPercentiles()
    {
        var s = new FrameTimeStats();
        // 1..100 ms: mean 50.5, p50 = 50, p99 = 99, worst 100.
        for (int i = 100; i >= 1; i--) s.Add(i);
        Assert.Equal(100, s.Count);
        Assert.Equal(50.5, s.Average, 9);
        Assert.Equal(100, s.Worst);
        Assert.InRange(s.Percentile(0.5), 50, 50 + FrameTimeStats.BinMs + 1e-9);
        Assert.InRange(s.Percentile(0.99), 99, 99 + FrameTimeStats.BinMs + 1e-9);
        Assert.Equal(100, s.Percentile(1), 9);
        Assert.InRange(s.Percentile(0), 1, 1 + FrameTimeStats.BinMs + 1e-9);
    }

    [Fact]
    public void OneSpikeIn100_ShowsInWorstNotP99()
    {
        var s = new FrameTimeStats();
        for (int i = 0; i < 199; i++) s.Add(16.6);
        s.Add(400); // past the histogram: last bin, exact worst
        Assert.Equal(400, s.Worst);
        Assert.InRange(s.Percentile(0.99), 16.6, 16.6 + FrameTimeStats.BinMs + 1e-9);
        Assert.Equal(400, s.Percentile(1), 9);
        Assert.Equal((199 * 16.6 + 400) / 200, s.Average, 9);
    }

    [Fact]
    public void BadSamplesIgnored_ClearResets()
    {
        var s = new FrameTimeStats();
        s.Add(double.NaN);
        s.Add(-1);
        s.Add(double.PositiveInfinity);
        Assert.Equal(0, s.Count);
        s.Add(5);
        s.Clear();
        Assert.Equal(0, s.Count);
        Assert.Equal(0, s.Worst);
        Assert.Equal(0, s.Percentile(0.99));
    }

    [Fact]
    public void Add_AllocatesNothing()
    {
        var s = new FrameTimeStats();
        Action block = () =>
        {
            for (int i = 0; i < 1000; i++) s.Add(i * 0.37);
        };
        block();
        AllocationProbe.AssertZero(block, _out);
    }
}
