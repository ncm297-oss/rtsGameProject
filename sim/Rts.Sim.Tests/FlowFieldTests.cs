using System.Diagnostics;
using Rts.Sim.Determinism;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Xunit.Abstractions;
using static Rts.Sim.Tests.FlowFieldOracle;

namespace Rts.Sim.Tests;

/// <summary>Flow fields (M1-4b): costs and directions against independent oracles, no corner cutting, blocked targets.</summary>
public class FlowFieldTests
{
    private readonly ITestOutputHelper _out;

    public FlowFieldTests(ITestOutputHelper output) => _out = output;

    private static readonly string[] UPocket =
    {
        "00000000000000",
        "00000000000000",
        "00111111111000",
        "00100000001000",
        "00100000001000",
        "00100000001000",
        "00100000001000",
        "00000000000000",
        "00000000000000",
        "00000000000000",
    };

    // Two 3 x 3 blocks touching only at a corner: (4,4) and (5,5) are blocked, (5,4) and (4,5) open.
    private static readonly string[] DiagonalGap =
    {
        "0000000000",
        "0000000000",
        "0011100000",
        "0011100000",
        "0011100000",
        "0000011100",
        "0000011100",
        "0000011100",
        "0000000000",
        "0000000000",
    };

    public static TheoryData<string, int, int> HandGrids => new()
    {
        { "ramp", 7, 3 },    // target on the plateau, reached only through the ramp
        { "ramp", 7, 15 },   // target below the ramp
        { "upocket", 6, 3 }, // deep inside the U
        { "upocket", 6, 8 }, // outside, below the opening
        { "diagonal", 4, 5 },
        { "diagonal", 8, 2 },
    };

    private static NavGrid Hand(string name) => name switch
    {
        "ramp" => RampCorridor(),
        "upocket" => FromRows(UPocket),
        _ => FromRows(DiagonalGap),
    };

    private static void AssertMatchesOracle(NavGrid g, int target, FlowField f)
    {
        (float[] cost, byte[] dir, int resolved) = Solve(g, target);
        Assert.Equal(resolved, f.TargetCell);
        for (int c = 0; c < cost.Length; c++)
        {
            if (cost[c] != f.CostAt(c)) Assert.Fail($"cell {c}: cost {f.CostAt(c)}, oracle {cost[c]}");
            if (dir[c] != f.DirectionAt(c)) Assert.Fail($"cell {c}: direction {f.DirectionAt(c)}, oracle {dir[c]}");
        }
    }

    [Theory]
    [MemberData(nameof(HandGrids))]
    public void HandBuiltGrids_MatchDijkstraAndRelaxationOracles(string grid, int tx, int ty)
    {
        NavGrid g = Hand(grid);
        int target = ty * g.Width + tx;
        Assert.True(g.IsPassable(tx, ty));
        FlowField f = FlowField.Build(g, target);
        AssertMatchesOracle(g, target, f);
        float[] relaxed = RelaxCosts(g, target);
        for (int c = 0; c < relaxed.Length; c++)
            Assert.Equal(relaxed[c], f.CostAt(c));
    }

    [Fact]
    public void RampCorridor_PlateauIsReachedOnlyThroughTheRamp()
    {
        NavGrid g = RampCorridor();
        FlowField f = FlowField.Build(g, 3 * g.Width + 7);
        // From open ground left of the ramp, the path must walk to the ramp foot (row 11) first.
        int c = 13 * g.Width + 2;
        Assert.True(float.IsFinite(f.CostAt(c)));
        bool usedRamp = false;
        for (int guard = 0; guard < 200 && c != f.TargetCell; guard++)
        {
            int d = f.DirectionAt(c);
            Assert.NotEqual(FlowField.NoDirection, d);
            int x = c % g.Width + FlowField.OffsetX(d), y = c / g.Width + FlowField.OffsetY(d);
            Assert.True(g.IsPassable(x, y));
            if ((g.FlagsAt(x, y) & NavFlags.Ramp) != 0) usedRamp = true;
            c = y * g.Width + x;
        }
        Assert.Equal(f.TargetCell, c);
        Assert.True(usedRamp);
    }

    [Fact]
    public void DiagonalGap_IsNotCutThrough()
    {
        NavGrid g = FromRows(DiagonalGap);
        Assert.False(g.IsPassable(4, 4));
        Assert.False(g.IsPassable(5, 5));
        FlowField f = FlowField.Build(g, 5 * g.Width + 4);
        int c = 4 * g.Width + 5;
        Assert.NotEqual((byte)3, f.DirectionAt(c)); // 3 = (-1, +1), straight through the touching corners
        Assert.True(f.CostAt(c) > 2f, $"cost {f.CostAt(c)}: the corner was cut");
    }

    [Fact]
    public void GeneratedMaps_20Maps_5TargetsEach_MatchOracle()
    {
        for (ulong seed = 0; seed < 20; seed++)
        {
            NavGrid g = Generated(seed);
            List<int> open = PassableCells(g);
            var rng = new SimRng(seed, 7);
            for (int t = 0; t < 5; t++)
            {
                int target = open[rng.NextInt(0, open.Count)];
                AssertMatchesOracle(g, target, FlowField.Build(g, target));
            }
        }
    }

    [Fact]
    public void GeneratedMaps_DirectionsNeverLeadIntoBlockedCellsOrCutCorners()
    {
        for (ulong seed = 0; seed < 20; seed++)
        {
            NavGrid g = Generated(seed);
            List<int> open = PassableCells(g);
            int target = open[new SimRng(seed, 8).NextInt(0, open.Count)];
            FlowField f = FlowField.Build(g, target);
            Assert.Equal(FlowField.NoDirection, f.DirectionAt(target));
            Assert.Equal(0f, f.CostAt(target));
            foreach (int c in open)
            {
                if (c == target) continue;
                int d = f.DirectionAt(c);
                // Every passable cell is reachable (NavGrid seals pockets), so every one has a direction.
                Assert.True(d != FlowField.NoDirection, $"seed {seed}: cell {c} has no direction");
                int x = c % g.Width, y = c / g.Width;
                int dx = FlowField.OffsetX(d), dy = FlowField.OffsetY(d);
                Assert.True(g.IsPassable(x + dx, y + dy), $"seed {seed}: cell {c} points into a blocked cell");
                if (dx != 0 && dy != 0)
                    Assert.True(g.IsPassable(x + dx, y) && g.IsPassable(x, y + dy), $"seed {seed}: cell {c} cuts a corner");
                Assert.True(f.CostAt((y + dy) * g.Width + x + dx) < f.CostAt(c), $"seed {seed}: cell {c} points uphill");
            }
        }
    }

    [Fact]
    public void BlockedTarget_CliffCell_ResolvesToNearestPassableCell()
    {
        int checkedMaps = 0;
        for (ulong seed = 0; seed < 5; seed++)
        {
            NavGrid g = Generated(seed);
            int cliff = -1;
            for (int c = 0; c < g.Width * g.Height && cliff < 0; c++)
                if ((g.FlagsAt(c % g.Width, c / g.Width) & NavFlags.Cliff) != 0) cliff = c;
            if (cliff < 0) continue;
            checkedMaps++;
            FlowField f = FlowField.Build(g, cliff);
            Assert.Equal(cliff, f.RequestedCell);
            Assert.Equal(Nearest(g, cliff), f.TargetCell);
            Assert.Equal(Nearest(g, cliff), FlowField.NearestPassable(g, cliff));
            Assert.True(g.IsPassable(f.TargetCell % g.Width, f.TargetCell / g.Width));
            Assert.Equal(0f, f.CostAt(f.TargetCell));
            Assert.True(float.IsPositiveInfinity(f.CostAt(cliff)));
            AssertMatchesOracle(g, cliff, f);
        }
        Assert.True(checkedMaps > 0);
    }

    [Fact]
    public void NearestPassable_RingSearch_MatchesFullScanOracle()
    {
        // BUG-0019 replaced the full-map scan with rings outward; the answer (ties included) must not change.
        for (ulong seed = 0; seed < 2; seed++)
        {
            NavGrid g = Generated(seed);
            for (int c = 0; c < g.Width * g.Height; c += 3)
            {
                if (g.IsPassable(c % g.Width, c / g.Width)) continue;
                Assert.Equal(Nearest(g, c), FlowField.NearestPassable(g, c));
            }
        }
        NavGrid u = FromRows(UPocket);
        for (int c = 0; c < u.Width * u.Height; c++)
            Assert.Equal(Nearest(u, c), FlowField.NearestPassable(u, c));
    }

    [Fact]
    public void BlockedTarget_TieGoesToLowestRowThenColumn()
    {
        // The U's top wall at (6,2): (6,1) above and (6,3) below are both at distance 1; row 1 wins.
        NavGrid g = FromRows(UPocket);
        int wall = 2 * g.Width + 6;
        Assert.False(g.IsPassable(6, 2));
        Assert.Equal(1 * g.Width + 6, FlowField.NearestPassable(g, wall));
        // A sealed block center: (3,3) of the diagonal grid; four cells at distance 4 ((3,1) ... (1,3)) and row 1 wins.
        NavGrid d = FromRows(DiagonalGap);
        Assert.Equal(1 * d.Width + 3, FlowField.NearestPassable(d, 3 * d.Width + 3));
    }

    [Fact]
    public void Build_RejectsOutOfRangeTarget()
    {
        NavGrid g = FromRows(DiagonalGap);
        Assert.Throws<ArgumentOutOfRangeException>(() => FlowField.Build(g, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => FlowField.Build(g, g.Width * g.Height));
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void Build_OnDefaultMap_AveragesUnderTwoMilliseconds()
    {
        NavGrid g = Generated(1);
        List<int> open = PassableCells(g);
        var cache = new FlowFieldCache(g, capacity: 1); // every new target is a miss
        var rng = new SimRng(1, 9);
        for (int i = 0; i < 10; i++) cache.Get(open[rng.NextInt(0, open.Count)]); // JIT warm-up
        const int runs = 100;
        int builds = cache.BuildCount;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < runs; i++) cache.Get(open[(i * 7919) % open.Count]);
        sw.Stop();
        Assert.Equal(builds + runs, cache.BuildCount);
        double avg = sw.Elapsed.TotalMilliseconds / runs;
        _out.WriteLine($"flow field build on 128 x 128: {avg:F3} ms average");
        Assert.True(avg < 2.0, $"build took {avg:F3} ms");
    }
}
