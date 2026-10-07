using System.Numerics;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M2-H2 (BUG-0101): the bench's "order across the map" target is a far passable cell on the other side, with a corner fallback.</summary>
[Collection(SerialCollection.Name)] // the allocation row measures this thread
public class BenchTargetTests
{
    private const float Cs = MapConstants.CellSize;

    public static IEnumerable<object[]> Seeds()
    {
        for (ulong seed = 1; seed <= 20; seed++) yield return new object[] { seed };
    }

    private static Vector2 Centre(Vector2[] spots)
    {
        var sum = Vector2.Zero;
        foreach (Vector2 s in spots) sum += s;
        return sum / spots.Length;
    }

    // Independent oracle: every passable cell, nearest to the point, lowest index on ties.
    private static Vector2 Nearest(NavGrid g, Vector2 point, int px, int py, int radius)
    {
        Vector2 best = default;
        float bestD = float.MaxValue;
        for (int i = 0; i < g.Width * g.Height; i++)
        {
            int x = i % g.Width, y = i / g.Width;
            if (!g.IsPassable(x, y) || Math.Abs(x - px) > radius || Math.Abs(y - py) > radius) continue;
            float d = Vector2.DistanceSquared(g.CellCenter(x, y), point);
            if (d < bestD) { bestD = d; best = g.CellCenter(x, y); }
        }
        return best;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void FromEachStartBlock_TheTargetIsAFarPassableCellOnTheOtherSide_AtLeast100mAway(ulong seed)
    {
        NavGrid g = PropLayoutTests.MatchSim(seed).World.NavGrid;
        float w = g.Width * Cs, h = g.Height * Cs;
        float maxR = TestSim.Data.Units.Max(u => u.Radius);
        foreach (bool west in new[] { true, false })
        {
            Vector2 army = Centre(StartLayout.Block(g, 100, west, maxR));
            Assert.True(BenchTarget.TryAcross(g, army, out Vector2 to), $"seed {seed}: no target");
            Assert.True(g.WorldToCell(to, out int tx, out int ty) && g.IsPassable(tx, ty), $"seed {seed}: target {to} not passable");
            Assert.Equal(g.CellCenter(tx, ty), to);
            Assert.True(Vector2.Distance(army, to) >= 100f, $"seed {seed} west {west}: target {to} only {Vector2.Distance(army, to):F1} m from the army at {army}");
            Assert.True(west ? to.X > w / 2 : to.X < w / 2, $"seed {seed} west {west}: target {to} on the army's own side");
            // The nearest passable cell to the far point (or, with none near it, to the opposite corner).
            var far = new Vector2(west ? BenchTarget.FarFraction * w : (1 - BenchTarget.FarFraction) * w, h / 2);
            int fx = (int)(far.X / Cs), fy = (int)(far.Y / Cs);
            Vector2 want = Nearest(g, far, fx, fy, BenchTarget.SearchCells);
            if (want == default)
            {
                var corner = new Vector2(west ? w : 0, army.Y > h / 2 ? 0 : h);
                want = Nearest(g, corner, (int)Math.Min(corner.X / Cs, g.Width - 1), (int)Math.Min(corner.Y / Cs, g.Height - 1), 1000);
            }
            Assert.Equal(want, to);
        }
    }

    [Fact]
    public void WithNothingPassableNearTheFarPoint_ItFallsBackToTheOppositeCorner()
    {
        const int n = 32;
        var map = new Heightmap(n, n, new byte[n * n], new float[n * n]);
        var g = new NavGrid(map);
        // The far point for a west army is (0.85 * 64 m, 32 m) = cell (27, 16); block every cell within 8 of it.
        g.SetBuilding(19, 8, 12, 17);
        Assert.True(BenchTarget.TryAcross(g, new Vector2(10f, 10f), out Vector2 to));
        Assert.Equal(g.CellCenter(n - 2, n - 2), to); // south-east, the corner opposite a north-west army (the border ring is blocked)
        Assert.True(BenchTarget.TryAcross(g, new Vector2(10f, 50f), out to));
        Assert.Equal(g.CellCenter(n - 2, 1), to); // north-east for a south-west army
        // An east army's far point (0.15 * 64 m) is open: no fallback.
        Assert.True(BenchTarget.TryAcross(g, new Vector2(60f, 30f), out to));
        // (9.6, 32) sits on the line between rows 15 and 16: the tie goes to the lower index.
        Assert.True(g.WorldToCell(to, out int x, out int y) && x == 4 && y == 15, $"east army target {to}");
    }

    [Fact]
    public void AMapWithNoPassableCell_HasNoTarget()
    {
        var map = new Heightmap(2, 2, new byte[4], new float[4]); // all border ring
        Assert.False(BenchTarget.TryAcross(new NavGrid(map), new Vector2(1f, 1f), out _));
    }

    [Fact]
    public void TryAcross_AllocatesNothing_AndDoesNotChangeTheSim()
    {
        Simulation sim = PropLayoutTests.MatchSim(3);
        ulong hash = sim.StateHash();
        NavGrid g = sim.World.NavGrid;
        BenchTarget.TryAcross(g, new Vector2(100f, 128f), out _);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 20; i++) BenchTarget.TryAcross(g, new Vector2(i * 12f, 128f), out _);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(hash, sim.StateHash());
    }
}
