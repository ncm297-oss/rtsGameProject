using System.Text.RegularExpressions;
using Xunit.Sdk;

namespace Rts.Sim.Tests;

/// <summary>The re-measuring allocation helper (BUG-0017) passes clean blocks and reports both counts for steady allocators.</summary>
[Collection(SerialCollection.Name)]
public class AllocationProbeTests
{
    private static object? s_sink;

    [Fact]
    public void AssertZero_NonAllocatingBlock_RunsOnceAndPasses()
    {
        int counter = 0;
        Action block = () => counter++;
        block(); // JIT the lambda outside the measurement

        int runs = AllocationProbe.AssertZero(block);

        Assert.Equal(1, runs);
        Assert.Equal(2, counter);
    }

    [Fact]
    public void AssertZero_AllocatingBlock_FailsWithBothDeltasInTheMessage()
    {
        Action block = () => s_sink = new long[1000];
        block();

        var ex = Assert.ThrowsAny<XunitException>(() => AllocationProbe.AssertZero(block));

        Match m = Regex.Match(ex.Message, @"allocated (\d+) bytes, then (\d+) bytes on the re-run");
        Assert.True(m.Success, ex.Message);
        Assert.True(long.Parse(m.Groups[1].Value) >= 8000, ex.Message);
        Assert.True(long.Parse(m.Groups[2].Value) >= 8000, ex.Message);
        Assert.NotNull(s_sink);
    }

    [Fact]
    public void Measure_CountsTheBlocksAllocation()
    {
        Action block = () => s_sink = new byte[4096];
        block();
        Assert.True(AllocationProbe.Measure(block) >= 4096);
    }
}
