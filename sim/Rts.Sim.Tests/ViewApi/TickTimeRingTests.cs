using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M2-5 tick-time graph samples: the last 120 tick costs, oldest first.</summary>
public class TickTimeRingTests
{
    [Fact]
    public void HoldsTheLast120Samples_OldestFirst()
    {
        var ring = new TickTimeRing();
        Assert.Equal(120, ring.Capacity);
        Assert.Equal(0, ring.Count);
        Assert.Equal(0, ring.Average);
        Assert.Equal(0, ring.Worst);
        for (int i = 0; i < 50; i++) ring.Add(i);
        Assert.Equal(50, ring.Count);
        Assert.Equal(0, ring[0]);
        Assert.Equal(49, ring[49]);
        for (int i = 50; i < 300; i++) ring.Add(i);
        Assert.Equal(120, ring.Count);
        Assert.Equal(300, ring.Total);
        for (int i = 0; i < 120; i++) Assert.Equal(180 + i, ring[i]);
        Assert.Equal(Enumerable.Range(180, 120).Average(), ring.Average, 9);
        Assert.Equal(299, ring.Worst);
        Assert.Throws<ArgumentOutOfRangeException>(() => ring[120]);
        Assert.Throws<ArgumentOutOfRangeException>(() => ring[-1]);
    }

    [Fact]
    public void Clear_EmptiesIt_AndAddsStartOver()
    {
        var ring = new TickTimeRing(3);
        ring.Add(5);
        ring.Add(6);
        ring.Add(7);
        ring.Add(8);
        Assert.Equal(new[] { 6.0, 7.0, 8.0 }, new[] { ring[0], ring[1], ring[2] });
        ring.Clear();
        Assert.Equal(0, ring.Count);
        ring.Add(1);
        Assert.Equal(1, ring[0]);
        Assert.Equal(1, ring.Worst);
        Assert.Throws<ArgumentOutOfRangeException>(() => new TickTimeRing(0));
    }

    [Fact]
    public void BudgetIsTheDesignedFourMilliseconds() => Assert.Equal(4.0, TickTimeRing.BudgetMs);
}
