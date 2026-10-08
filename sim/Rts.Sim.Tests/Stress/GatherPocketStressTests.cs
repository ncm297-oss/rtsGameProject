using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA M4-2a (session 2026-10-08-0313), BUG-0146 attack: gatherers on nodes in pockets of every shape (a tree open on
/// one side, a one-cell staircase corridor, a two-cell corridor walled by a Depot, a tree inside a tree ring with one
/// entrance, a gold mine open on one side) with 1 / 4 / 12 / 40 workers, 8 seeds (worker spawn spots) x 3,000 ticks.
/// After every tick: resources conserved exactly; no worker on the node leg of a gather loop stands out of reach of its
/// node, with a clear line to it (no unit's body across the line, nearer the node, and no blocked cell on it), within 0.5 m of one spot, for more than 2 retry periods (40 ticks).
/// Also an income report per worker per minute on generated maps (compared with the M3 numbers in the QA report).
/// </summary>
[Collection(SerialCollection.Name)]
public class GatherPocketStressTests
{
    private readonly ITestOutputHelper _out;

    public GatherPocketStressTests(ITestOutputHelper output) => _out = output;

    public enum Shape { OneSide, Staircase, DepotCorridor, TreeRing, MineOneSide }

    private const int MapW = 32, MapH = 26, Ticks = 3000, Bound = 2 * EconomyConstants.RetryTicks;

    private static int Depot => TestSim.Data.FindBuilding("malazan_depot");

    /// <summary>Builds the pocket; returns the target node's handle.</summary>
    private static EntityHandle BuildPocket(Simulation sim, Shape shape)
    {
        World w = sim.World;
        EntityHandle node = default;
        void T(int x, int y) => Spawn(w, Tree, x, y, TreeWood);
        switch (shape)
        {
            case Shape.OneSide:
                // N at (12, 4): trees west, east and north; open only to the south.
                for (int x = 10; x <= 14; x++) T(x, 3);
                T(10, 4); T(11, 4); T(13, 4); T(14, 4);
                node = Spawn(w, Tree, 12, 4, TreeWood);
                break;
            case Shape.Staircase:
            {
                // N at (12, 4); its only open side leads into a one-cell staircase corridor going down and right.
                var path = new HashSet<int>();
                (int, int)[] cells = { (12, 5), (13, 5), (13, 6), (14, 6), (14, 7), (15, 7), (15, 8), (16, 8), (16, 9) };
                foreach ((int x, int y) in cells) path.Add(y * MapW + x);
                for (int y = 3; y <= 9; y++)
                    for (int x = 9; x <= 19; x++)
                    {
                        if (x == 12 && y == 4) continue;
                        if (!path.Contains(y * MapW + x)) T(x, y);
                    }
                node = Spawn(w, Tree, 12, 4, TreeWood);
                break;
            }
            case Shape.DepotCorridor:
                // N at (12, 4); a two-cell corridor (x 12-13, y 5-10) walled by trees on the west and a Depot then trees on the east.
                for (int x = 10; x <= 15; x++) T(x, 3);
                T(11, 4); T(13, 4); T(14, 4);
                for (int y = 5; y <= 10; y++) T(11, y);
                for (int y = 7; y <= 10; y++) T(14, y);
                node = Spawn(w, Tree, 12, 4, TreeWood);
                Building(sim, 14, 5, type: Depot);
                break;
            case Shape.TreeRing:
                // A ring of trees (x 7-19, y 2-12) with one entrance at (13, 12); N alone in the middle.
                for (int x = 7; x <= 19; x++)
                {
                    T(x, 2);
                    if (x != 13) T(x, 12);
                }
                for (int y = 3; y <= 11; y++)
                {
                    T(7, y);
                    T(19, y);
                }
                node = Spawn(w, Tree, 13, 7, TreeWood);
                break;
            case Shape.MineOneSide:
                // A 2 x 2 mine at (12, 3)-(13, 4): trees west, east and north; open to the south.
                for (int x = 10; x <= 15; x++) T(x, 2);
                for (int y = 3; y <= 4; y++)
                {
                    T(10, y); T(11, y); T(14, y); T(15, y);
                }
                node = Spawn(w, Mine, 12, 3, 100_000);
                break;
        }
        return node;
    }

    [Theory]
    [InlineData(Shape.OneSide, 1)]
    [InlineData(Shape.OneSide, 4)]
    [InlineData(Shape.OneSide, 12)]
    [InlineData(Shape.OneSide, 40)]
    [InlineData(Shape.Staircase, 1)]
    [InlineData(Shape.Staircase, 4)]
    [InlineData(Shape.Staircase, 12)]
    [InlineData(Shape.Staircase, 40)]
    [InlineData(Shape.DepotCorridor, 1)]
    [InlineData(Shape.DepotCorridor, 4)]
    [InlineData(Shape.DepotCorridor, 12)]
    [InlineData(Shape.DepotCorridor, 40)]
    [InlineData(Shape.TreeRing, 1)]
    [InlineData(Shape.TreeRing, 4)]
    [InlineData(Shape.TreeRing, 12)]
    [InlineData(Shape.TreeRing, 40)]
    [InlineData(Shape.MineOneSide, 1)]
    [InlineData(Shape.MineOneSide, 4)]
    [InlineData(Shape.MineOneSide, 12)]
    [InlineData(Shape.MineOneSide, 40)]
    public void Pocket_NoGathererWedgesOutOfReach_ConservationExact(Shape shape, int workers)
    {
        var report = new List<string>();
        var failures = new List<string>();
        for (ulong seed = 1; seed <= 8; seed++)
        {
            Simulation sim = GatherMaps.NewSim(Flat(MapW, MapH), units: 48, seed: seed);
            World w = sim.World;
            UnitStore u = w.Units;
            EntityHandle node = BuildPocket(sim, shape);
            Building(sim, 24, 18); // the Keep: a drop-off for both kinds
            var rng = new SimRng(seed, 907);
            var hs = new EntityHandle[workers];
            var before = (bool[])u.Alive.Clone();
            for (int k = 0; k < workers; k++)
                sim.Enqueue(Command.SpawnUnit(0, Laborer, new Vector2(4f + rng.NextFloat() * 36f, 30f + rng.NextFloat() * 16f)));
            Run(sim, 2);
            int n = 0;
            for (int i = 0; i < u.Capacity; i++)
                if (u.Alive[i] && !before[i]) hs[n++] = new EntityHandle(i, u.Generation[i]);
            Assert.Equal(workers, n);
            int cell = w.Resources.Cell[node.Index];
            foreach (EntityHandle h in hs) sim.Enqueue(Command.Gather(0, h, At(sim, cell % MapW, cell / MapW)));
            (long gold0, long wood0) = Conserved(w);
            var run = new int[workers];
            var anchor = new Vector2[workers];
            int worst = 0, worstUnit = -1, worstTick = 0;
            int startGold = w.Gold[0], startWood = w.Wood[0];
            for (int t = 1; t <= Ticks; t++)
            {
                sim.Tick();
                (long gold, long wood) = Conserved(w);
                Assert.True(gold == gold0 && wood == wood0, $"{shape} x{workers} seed {seed} tick {t}: conservation broken: gold {gold0} -> {gold}, wood {wood0} -> {wood}");
                for (int k = 0; k < workers; k++)
                {
                    int i = hs[k].Index;
                    if (!u.IsAlive(hs[k])) continue;
                    bool nodeLeg = EconomySystem.OnLoop(u, i) && w.Resources.IsAlive(u.GatherNode[i]) && u.Cargo[i] < w.Data.Rules.WorkerCarry;
                    if (!nodeLeg || InReach(w, i) || SomeoneBetween(w, i) || Vector2.Distance(u.Position[i], anchor[k]) > 0.5f)
                    {
                        anchor[k] = u.Position[i];
                        run[k] = 0;
                        continue;
                    }
                    if (++run[k] > worst) (worst, worstUnit, worstTick) = (run[k], i, t);
                }
            }
            int income = w.Gold[0] - startGold + w.Wood[0] - startWood;
            report.Add($"seed {seed}: longest wedge {worst} (unit {worstUnit}, tick {worstTick}), income {income}");
            if (worst > Bound)
            {
                int i = worstUnit;
                failures.Add($"seed {seed}: unit {i} stood out of reach with nobody between for {worst} ticks (to tick {worstTick}); now at {u.Position[i]} state {u.State[i]} node {u.GatherNode[i]}");
            }
        }
        _out.WriteLine($"{shape} x{workers}:\n  " + string.Join("\n  ", report));
        Assert.True(failures.Count == 0, $"{shape} x{workers}: " + string.Join("; ", failures));
    }

    /// <summary>Unit i's center is within Reach of its node's footprint.</summary>
    private static bool InReach(World w, int i)
    {
        int n = w.Units.GatherNode[i].Index;
        ResourceDef def = w.Data.Resources[w.Resources.TypeId[n]];
        return DistanceToFootprint(w.NavGrid, w.Units.Position[i], w.Resources.Cell[n], def.FootprintWidth, def.FootprintHeight) <= EconomyConstants.Reach;
    }

    /// <summary>
    /// The line from unit i to the nearest point of its node's footprint is not clear: another live unit's body lies across
    /// it (center within the two radii of the segment) nearer the node, or a blocked cell other than the node's own lies on it.
    /// A worker waiting behind others at a crowded node is queueing, not wedged.
    /// </summary>
    private static bool SomeoneBetween(World w, int i)
    {
        UnitStore u = w.Units;
        NavGrid g = w.NavGrid;
        int n = u.GatherNode[i].Index;
        ResourceDef def = w.Data.Resources[w.Resources.TypeId[n]];
        int cell = w.Resources.Cell[n];
        const float cs = MapConstants.CellSize;
        float x0 = cell % g.Width * cs, y0 = cell / g.Width * cs;
        Vector2 min = new(x0, y0), max = new(x0 + def.FootprintWidth * cs, y0 + def.FootprintHeight * cs);
        Vector2 p = u.Position[i], q = Vector2.Clamp(p, min, max);
        float mine = Vector2.Distance(p, q);
        Vector2 d = q - p;
        float len2 = d.LengthSquared();
        for (int j = 0; j < u.Capacity; j++)
        {
            if (j == i || !u.Alive[j]) continue;
            Vector2 pj = u.Position[j];
            float s = len2 > 0f ? Math.Clamp(Vector2.Dot(pj - p, d) / len2, 0f, 1f) : 0f;
            float r = u.Radius[i] + u.Radius[j];
            if (Vector2.DistanceSquared(pj, p + d * s) > r * r) continue;
            if (Vector2.Distance(pj, Vector2.Clamp(pj, min, max)) < mine) return true;
        }
        int steps = (int)(mine / 0.25f);
        for (int k = 1; k < steps; k++)
        {
            Vector2 at = p + d * (k / (float)steps);
            if (!g.WorldToCell(at, out int cx, out int cy)) continue;
            bool own = at.X >= min.X && at.X <= max.X && at.Y >= min.Y && at.Y <= max.Y;
            if (!own && !g.IsPassable(cx, cy)) return true;
        }
        return false;
    }

    /// <summary>
    /// Report: income per worker per minute (1,200 ticks after a 400-tick warm-up) with 1 / 5 / 10 / 20 workers on
    /// EconomyScenario's Keep (half on the mine, half on the nearest tree), generated maps seeds 1-8. Compared base vs
    /// branch in the QA report; asserts only that income is positive.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    public void IncomePerWorkerPerMinute_Report(int workers)
    {
        var parts = new List<string>();
        double total = 0;
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 1, UnitCapacity: 32, CommandCapacity: 256) with { Map = new MapGenParams { Forests = 12, GoldMines = 4 } });
            EconomyScenario.Setup(sim, workers);
            Run(sim, 400);
            int g0 = sim.World.Gold[0], w0 = sim.World.Wood[0];
            Run(sim, 1200);
            double perWorker = (sim.World.Gold[0] - g0 + sim.World.Wood[0] - w0) / (double)workers;
            total += perWorker;
            parts.Add($"{perWorker:F1}");
            Assert.True(perWorker > 0, $"seed {seed}: no income");
        }
        _out.WriteLine($"{workers} workers: per worker per minute (gold + wood), seeds 1-8: {string.Join(", ", parts)}; mean {total / 8:F1}");
    }
}
