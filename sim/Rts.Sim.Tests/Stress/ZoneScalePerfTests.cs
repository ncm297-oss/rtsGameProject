using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA (M4-4b-2, 2026-10-10-0215): a 500-unit brawl (250 a side) with 0, 16 and 64 (the store full) live blocking
/// Sandstorms of a third player away from the fight. While any blocker lives the one-level scan shortcut is off and every
/// candidate asks <see cref="Vision.VisionSystem.ZoneHides"/>, which walks every zone: the cost must stay modest (the full
/// store at most 2.5x the zone-free tick (measured 1.85x on 2026-10-10, BUG-0363), and under the 500-unit tick budget of docs/03, 5 ms) and allocate nothing.
/// </summary>
[Collection(SerialCollection.Name)]
public class ZoneScalePerfTests
{
    private readonly ITestOutputHelper _out;

    public ZoneScalePerfTests(ITestOutputHelper output) => _out = output;

    private static double AvgTickMs(int zones, out long bytes)
    {
        var sim = TestSim.Explored(new Simulation(TestSim.Config(Seed: 3, PlayerCount: 3, UnitCapacity: 520, CommandCapacity: 4096), LocalMovementTests.Flat(160)));
        CombatScenes.FlatBrawl(sim, 250, new Vector2(80f, 80f), 12f);
        AbilityDef storm = TestSim.Data.Abilities[TestSim.Data.FindAbility("sandstorm")];
        for (int k = 0; k < zones; k++)
            ZoneSystem.Create(sim.World, 2, storm, new Vector2(20f + 15f * (k % 8), 200f + 15f * (k / 8))); // y 200+: away from the fight
        for (int t = 0; t < 20; t++) sim.Tick(); // warm up, engage
        var sw = Stopwatch.StartNew();
        const int ticks = 150;
        bytes = 0;
        for (int t = 0; t < ticks; t++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            sim.Tick();
            bytes += GC.GetAllocatedBytesForCurrentThread() - before;
        }
        sw.Stop();
        Assert.Equal(zones, sim.World.Zones.Count);
        return sw.Elapsed.TotalMilliseconds / ticks;
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void ABrawlWith64LiveBlockers_CostsAtMost2_5xTheZoneFreeTick_AndAllocatesNothing()
    {
        AvgTickMs(0, out _); // JIT
        double none = AvgTickMs(0, out long b0);
        double sixteen = AvgTickMs(16, out long b16);
        double full = AvgTickMs(64, out long b64);
        _out.WriteLine($"500-unit brawl: {none:F3} ms with no zone, {sixteen:F3} ms with 16 blockers, {full:F3} ms with 64; bytes {b0}/{b16}/{b64}");
        Assert.Equal(0, b16);
        Assert.Equal(0, b64);
        Assert.True(full <= 2.5 * none + 0.1, $"64 blockers: {full:F3} ms vs {none:F3} ms without");
        Assert.True(full < 5.0, $"64 blockers: {full:F3} ms a tick");
    }
}
