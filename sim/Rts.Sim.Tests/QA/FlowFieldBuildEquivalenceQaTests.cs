using Rts.Sim.Determinism;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M3-2b re-check, BUG-0082): the rewritten flow-field build (three-bucket ring with lazy deletion,
/// directions picked at relax time, sliding-window step masks) gives bit-identical costs, directions,
/// targets and step masks to the a80c143 build (<see cref="LegacyFlowFieldA80c143"/>) on random terrain,
/// odd grid sizes, blocked targets, pockets sealed off by placed nodes and buildings, and node churn.
/// The field, queue and step buffer are reused across every build, as the cache reuses them.
/// </summary>
[Collection(SerialCollection.Name)]
public class FlowFieldBuildEquivalenceQaTests
{
    private readonly ITestOutputHelper _out;

    public FlowFieldBuildEquivalenceQaTests(ITestOutputHelper output) => _out = output;

    private static NavGrid RandomGrid(SimRng rng, int w, int h, int plateauPercent)
    {
        var levels = new byte[w * h];
        var elevations = new float[w * h];
        for (int i = 0; i < levels.Length; i++)
        {
            levels[i] = rng.NextInt(0, 100) < plateauPercent ? (byte)rng.NextInt(1, 3) : (byte)0;
            elevations[i] = levels[i] * MapConstants.LevelHeight;
        }
        return new NavGrid(new Heightmap(w, h, levels, elevations));
    }

    /// <summary>Finite-cost cells over every compared field: proof the comparisons covered real paths, not walled-in grids.</summary>
    private long _reached;

    private int CompareAll(NavGrid g, FlowField field, CellQueue queue, byte[] steps,
        LegacyFlowFieldA80c143.Queue oldQueue, byte[] oldSteps, IEnumerable<int> targets, string where)
    {
        FlowField.ComputeSteps(g, steps);
        LegacyFlowFieldA80c143.ComputeSteps(g, oldSteps);
        for (int c = 0; c < steps.Length; c++)
            if (steps[c] != oldSteps[c]) Assert.Fail($"{where}: step mask of cell {c} is {steps[c]}, a80c143 {oldSteps[c]}");
        int compared = 0;
        foreach (int t in targets)
        {
            field.Build(g, t, queue, steps);
            (float[] cost, byte[] dir, int target) = LegacyFlowFieldA80c143.Build(g, t, oldQueue, oldSteps);
            Assert.True(target == field.TargetCell, $"{where} request {t}: target {field.TargetCell}, a80c143 {target}");
            for (int c = 0; c < cost.Length; c++)
            {
                if (BitConverter.SingleToInt32Bits(cost[c]) != BitConverter.SingleToInt32Bits(field.CostAt(c)))
                    Assert.Fail($"{where} request {t}: cost of cell {c} is {field.CostAt(c):R}, a80c143 {cost[c]:R}");
                if (dir[c] != field.DirectionAt(c))
                    Assert.Fail($"{where} request {t}: direction of cell {c} is {field.DirectionAt(c)}, a80c143 {dir[c]}");
                if (!float.IsPositiveInfinity(cost[c])) _reached++;
            }
            compared++;
        }
        return compared;
    }

    /// <summary>Random places and clears: 1 x 1 nodes and 1-4 sided buildings on open non-ramp cells, which can wall off pockets.</summary>
    private static void Churn(NavGrid g, SimRng rng, List<(int X, int Y)> nodes, List<(int X, int Y, int W, int H)> buildings, int places)
    {
        for (int k = 0; k < places; k++)
        {
            int x = rng.NextInt(0, g.Width), y = rng.NextInt(0, g.Height);
            if (rng.NextInt(0, 3) > 0)
            {
                if (g.CanTakeResource(x, y)) { g.SetResource(x, y, 1, 1); nodes.Add((x, y)); }
            }
            else
            {
                int bw = rng.NextInt(1, 5), bh = rng.NextInt(1, 5);
                bool fits = true;
                for (int yy = y; yy < y + bh && fits; yy++)
                    for (int xx = x; xx < x + bw && fits; xx++)
                        fits = g.CanTakeResource(xx, yy);
                if (fits) { g.SetBuilding(x, y, bw, bh); buildings.Add((x, y, bw, bh)); }
            }
        }
        for (int k = 0; k < places && (nodes.Count > 0 || buildings.Count > 0); k++)
        {
            if (nodes.Count > 0 && (buildings.Count == 0 || rng.NextInt(0, 3) > 0))
            {
                int i = rng.NextInt(0, nodes.Count);
                g.ClearResource(nodes[i].X, nodes[i].Y, 1, 1);
                nodes.RemoveAt(i);
            }
            else
            {
                int i = rng.NextInt(0, buildings.Count);
                (int bx, int by, int bw, int bh) = buildings[i];
                g.ClearBuilding(bx, by, bw, bh);
                buildings.RemoveAt(i);
            }
        }
    }

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(1, 9, 0)]
    [InlineData(9, 1, 0)]
    [InlineData(2, 2, 0)]
    [InlineData(3, 3, 0)]
    [InlineData(3, 11, 10)]
    [InlineData(11, 3, 10)]
    [InlineData(4, 4, 0)]
    [InlineData(5, 7, 20)]
    [InlineData(13, 17, 15)]
    [InlineData(17, 11, 35)]
    [InlineData(31, 29, 5)]
    [InlineData(64, 33, 25)]
    [InlineData(33, 64, 50)]
    [InlineData(120, 72, 10)]
    [InlineData(128, 128, 20)]
    [InlineData(257, 131, 30)]
    public void RewrittenBuild_IsBitIdenticalToA80c143_OnRandomTerrainAndChurn(int w, int h, int plateauPercent)
    {
        var rng = new SimRng((ulong)(w * 7919 + h * 31 + plateauPercent), 3);
        var steps = new byte[w * h];
        var oldSteps = new byte[w * h];
        var queue = new CellQueue(w * h);
        var oldQueue = new LegacyFlowFieldA80c143.Queue(w * h);
        var field = new FlowField(w, h);
        int compared = 0;
        int mazes = w * h <= 2000 ? 12 : 3;
        for (int maze = 0; maze < mazes; maze++)
        {
            NavGrid g = RandomGrid(rng, w, h, plateauPercent);
            var nodes = new List<(int, int)>();
            var buildings = new List<(int, int, int, int)>();
            for (int round = 0; round < 6; round++)
            {
                IEnumerable<int> targets = w * h <= 400
                    ? Enumerable.Range(0, w * h)
                    : Enumerable.Range(0, 24).Select(_ => rng.NextInt(0, w * h)).ToArray();
                compared += CompareAll(g, field, queue, steps, oldQueue, oldSteps, targets, $"{w}x{h} maze {maze} round {round}");
                Churn(g, rng, nodes, buildings, Math.Max(1, w * h / 12));
            }
        }
        _out.WriteLine($"{w}x{h} {plateauPercent}%: {compared} fields bit-identical, {_reached} reached cells");
        if (w >= 5 && h >= 5) Assert.True(_reached > compared * 4L, $"only {_reached} reached cells over {compared} fields: the grids were walled in");
    }

    /// <summary>Long paths: costs past 1,024 and 2,048, where float rounding of cost + 1 can land on the next-but-one whole number.</summary>
    [Fact]
    public void RewrittenBuild_IsBitIdenticalToA80c143_OnAnOpen1024Map_CornerToCorner()
    {
        const int w = 1024, h = 1024;
        var rng = new SimRng(1024, 5);
        NavGrid g = RandomGrid(rng, w, h, 0);
        var steps = new byte[w * h];
        var oldSteps = new byte[w * h];
        int far = (h - 2) * w + (w - 2);
        int compared = CompareAll(g, new FlowField(w, h), new CellQueue(w * h), steps, new LegacyFlowFieldA80c143.Queue(w * h), oldSteps,
            new[] { w + 1, far, (h / 2) * w + 1 }, "1024 open");
        var field = new FlowField(w, h);
        field.Build(g, w + 1, new CellQueue(w * h), steps);
        _out.WriteLine($"{compared} fields; max cost {field.CostAt(far)}");
        Assert.True(field.CostAt(far) > 1400f);
    }

    /// <summary>A long serpentine corridor (costs into the thousands along a 1-wide path, many equal-cost ties at each bend).</summary>
    [Fact]
    public void RewrittenBuild_IsBitIdenticalToA80c143_OnASerpentine()
    {
        const int w = 201, h = 201;
        var levels = new byte[w * h];
        var elevations = new float[w * h];
        for (int y = 2; y < h - 2; y += 4)
            for (int x = 0; x < w; x++)
            {
                bool gap = (y / 4) % 2 == 0 ? x >= w - 6 : x < 6;
                if (!gap) { levels[y * w + x] = 1; elevations[y * w + x] = MapConstants.LevelHeight; }
            }
        var g = new NavGrid(new Heightmap(w, h, levels, elevations));
        int n = w * h;
        int compared = CompareAll(g, new FlowField(w, h), new CellQueue(n), new byte[n], new LegacyFlowFieldA80c143.Queue(n), new byte[n],
            new[] { w + 1, n - w - 2, n / 2, 0, n - 1 }, "serpentine");
        Assert.Equal(5, compared);
        _out.WriteLine($"serpentine: {_reached} reached cells over 5 fields");
        Assert.True(_reached > 4 * n / 2, $"{_reached} reached");
    }
}
