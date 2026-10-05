using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>Measures managed allocation of a block on the current thread; every allocation test goes through it (BUG-0017).</summary>
/// <remarks>
/// Callers create the delegates before measuring (creating a closure allocates). A first non-zero
/// count can be a one-time runtime charge (JIT, type loading) under load, so the block runs once
/// more and the second count must be 0: a steady allocation fails both times, and the message
/// shows both counts. An optional unmeasured <c>setup</c> runs before each measurement, so a
/// re-run repeats the same work (e.g. re-queues the commands the block's ticks apply). Callers
/// belong in <see cref="SerialCollection"/>.
/// </remarks>
public static class AllocationProbe
{
    /// <summary>Bytes allocated on this thread while <paramref name="block"/> runs once.</summary>
    public static long Measure(Action block)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        block();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    /// <summary>Asserts the block allocates nothing, re-running it once after a non-zero first count; returns how many times it ran (1 or 2).</summary>
    public static int AssertZero(Action block, ITestOutputHelper? log = null, Action? setup = null)
    {
        setup?.Invoke();
        long first = Measure(block);
        if (first == 0) return 1;
        setup?.Invoke();
        long second = Measure(block);
        log?.WriteLine($"allocation probe: first run {first} bytes, re-run {second} bytes");
        Assert.True(second == 0,
            $"allocated {first} bytes, then {second} bytes on the re-run (non-zero twice: a steady allocation, not a one-time runtime charge)");
        return 2;
    }
}
