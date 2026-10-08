using System.Diagnostics;
using System.Numerics;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M2-3b cost and allocation rows at a full 4,096-node store: the prop relist (<= 1 ms), the minimap's 2,000 dots (<= 0.25 ms) and its forced resource redraw (<= 0.2 ms) timed apart, 0 bytes.</summary>
[Collection(SerialCollection.Name)]
public class PropsMeasureTests
{
    private static readonly uint[] Colors = { 0x3366CC, 0xE08020 };
    private readonly ITestOutputHelper _out;

    public PropsMeasureTests(ITestOutputHelper output) => _out = output;

    // A flat 128 map with a full store (mines on a lattice, trees in the gaps) and 2,000 unit positions.
    private static (World W, bool[] Alive, Vector2[] Pos, int[] Owner) Crowd()
    {
        World w = ResourceMaps.NewSim(ResourceMaps.Flat(128, 128)).World;
        for (int y = 2; y < 124; y += 6)
            for (int x = 2; x < 124; x += 6) ResourceMaps.Spawn(w, ResourceMaps.Mine, x, y, 10);
        for (int c = 0; c < 128 * 128 && w.Resources.Count < w.Resources.Capacity; c++)
            if (w.Resources.Fits(ResourceMaps.Tree, c)) ResourceMaps.Spawn(w, ResourceMaps.Tree, c % 128, c / 128, 10);
        Assert.Equal(w.Resources.Capacity, w.Resources.Count);
        const int units = 2000;
        var pos = new Vector2[units];
        var owner = new int[units];
        for (int i = 0; i < units; i++) (pos[i], owner[i]) = (new Vector2(i * 0.127f % 256f, i * 0.311f % 256f), i % 2);
        return (w, Enumerable.Repeat(true, units).ToArray(), pos, owner);
    }

    private double Time(string what, World w, Action refresh, double limitMs)
    {
        var watch = new Stopwatch();
        double total = 0, worst = 0;
        const int runs = 50;
        for (int k = 0; k < runs; k++)
        {
            w.NavGrid.BumpVersionForTests(); // worst case: a passability change before every refresh
            watch.Restart();
            refresh();
            watch.Stop();
            total += watch.Elapsed.TotalMilliseconds;
            worst = Math.Max(worst, watch.Elapsed.TotalMilliseconds);
        }
        _out.WriteLine($"{what}: avg {total / runs:F3} ms, worst {worst:F3} ms");
        Assert.True(total / runs <= limitMs, $"{what}: avg {total / runs:F3} ms > {limitMs} ms");
        return total / runs;
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void PropRelist_4096Nodes_UnderOneMillisecond()
    {
        (World w, _, _, _) = Crowd();
        PropLayout l = PropLayoutTests.Layout(w);
        PropLayoutTests.Refresh(l, w);
        PropLayoutTests.AssertMatchesStore(l, w);
        Time("PropLayout relist at 4,096 nodes", w, () => PropLayoutTests.Refresh(l, w), 1.0);
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void MinimapRefresh_2000Units_4096Nodes_DotsAndForcedRedraw_TimedApart()
    {
        // BUG-0105: the combined row sat at 0.28-0.29 ms of its 0.3 ms limit. The two halves are timed apart: the 2,000
        // dots run on every 5 Hz refresh; the forced 4,096-node resource redraw only on a refresh after a node was felled
        // (BUG-0107: building changes no longer force it). Limits: the dots get QA's DrawDots budget (0.25 ms, half the
        // 0.5 ms refresh budget; MinimapQaTests); the redraw 0.2 ms (measured 0.127 ms alone, Debug). The sum is printed.
        (World w, bool[] alive, Vector2[] pos, int[] owner) = Crowd();
        var r = new MinimapRaster(w.Heightmap, w.NavGrid, Colors, alive.Length);
        MinimapRasterResourceTests.DrawResources(r, w);
        MinimapRasterResourceTests.AssertResourceLayer(r, w);
        double dots = Time("minimap dots (2,000 units)", w, () => r.DrawDots(alive, pos, owner), 0.25);
        int draws = r.ResourceDraws;
        double redraw = Time("minimap forced resource redraw (4,096 nodes)", w, () => MinimapRasterResourceTests.DrawResources(r, w), 0.2);
        Assert.Equal(draws + 50, r.ResourceDraws); // every timed run redrew
        _out.WriteLine($"minimap refresh with a forced redraw (sum): {dots + redraw:F3} ms");
    }

    [Fact]
    public void Relists_Redraws_AndSteadyFrames_AllocateZeroBytes()
    {
        (World w, _, _, _) = Crowd();
        PropLayout l = PropLayoutTests.Layout(w);
        var r = new MinimapRaster(w.Heightmap, w.NavGrid, Colors, 1);
        int count = 0;
        Action setup = w.NavGrid.BumpVersionForTests;
        Action changed = () => count += (PropLayoutTests.Refresh(l, w) ? 1 : 0) + (MinimapRasterResourceTests.DrawResources(r, w) ? 1 : 0);
        Action steady = () => { for (int i = 0; i < 10; i++) changed(); };
        changed(); // first fill outside the probe: the probe measures a relist after a version change
        int runs = AllocationProbe.AssertZero(changed, _out, setup) + AllocationProbe.AssertZero(steady, _out);
        Assert.True(l.Rebuilds >= 2 && r.ResourceDraws >= 2, $"relists {l.Rebuilds}, redraws {r.ResourceDraws}");
        _out.WriteLine($"prop relist + minimap resource redraw + steady frames: 0 bytes (runs {runs}), checksum {count}, relists {l.Rebuilds}");
    }
}
