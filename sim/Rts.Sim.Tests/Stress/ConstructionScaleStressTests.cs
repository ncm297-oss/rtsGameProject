using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.Tests.QA;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA stress (M3-3, session 2026-10-06-2114): tick cost of the felling + building scene at 1x, 2x and 5x the 500-unit
/// design number, and BUG-0080 at a realistic placement rate (one Build every 2 s while 32 goal groups march).
/// </summary>
[Collection(SerialCollection.Name)]
public class ConstructionScaleStressTests
{
    private readonly ITestOutputHelper _out;

    public ConstructionScaleStressTests(ITestOutputHelper output) => _out = output;

    private static readonly MapGenParams ResourceMap = MapGenParams.Default with { Forests = 12, GoldMines = 8 };

    /// <summary>
    /// <paramref name="marchers"/> units marching to 32 goals, plus 50 builders on 10 Keep sites and 30 workers felling
    /// trees, all on player 0's side. Returns the sim and the builder / gatherer slots.
    /// </summary>
    private static Simulation Scene(int marchers, out int firstWorker)
    {
        Simulation sim = MoveScenario.Spawn(7, units: marchers, maxCost: 60f, out _, capacity: marchers + 80, players: 2, map: ResourceMap);
        NavGrid g = sim.World.NavGrid;
        var rng = new Determinism.SimRng(7, 9);
        List<int> open = FlowFieldOracle.PassableCells(g);
        int[] goals = Enumerable.Range(0, 32).Select(_ => open[rng.NextInt(0, open.Count)]).ToArray();
        UnitStore u = sim.World.Units;
        for (int i = 0; i < marchers; i++) sim.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), MoveScenario.Center(g, goals[i % 32])));
        BuildMaps.SetTotals(sim, 0, 100_000, 100_000);
        var anchors = new List<int>();
        for (int c = 0; c < g.Width * g.Height && anchors.Count < 10; c += 97)
            if (sim.World.CanPlace(0, GatherMaps.Keep, c, out _) && anchors.All(a => Math.Abs(a % g.Width - c % g.Width) > 6 || Math.Abs(a / g.Width - c / g.Width) > 6)) anchors.Add(c);
        firstWorker = marchers;
        foreach (int a in anchors)
            for (int k = 0; k < 5; k++)
                sim.Enqueue(Command.SpawnUnit(0, GatherMaps.Laborer, g.CellCenter(a % g.Width + k % 4, a / g.Width - 1)));
        var trees = new List<int>();
        ResourceStore r = sim.World.Resources;
        for (int k = 0; k < r.Capacity; k++)
            if (r.Alive[k] && r.TypeId[k] == ResourceMaps.Tree) trees.Add(r.Cell[k]);
        for (int k = 0; k < 30; k++)
            sim.Enqueue(Command.SpawnUnit(0, GatherMaps.Laborer, MoveScenario.Center(g, open[rng.NextInt(0, open.Count)])));
        sim.Tick();
        sim.Tick();
        int slot = marchers;
        foreach (int a in anchors)
            for (int k = 0; k < 5; k++, slot++)
                sim.Enqueue(Command.Build(0, MoveScenario.Handle(sim, slot), GatherMaps.Keep, g.CellCenter(a % g.Width, a / g.Width)));
        for (int k = 0; k < 30; k++, slot++)
        {
            Vector2 p = u.Position[slot];
            int best = trees.OrderBy(t => Vector2.DistanceSquared(p, MoveScenario.Center(g, t))).First();
            sim.Enqueue(Command.Gather(0, MoveScenario.Handle(sim, slot), MoveScenario.Center(g, best)));
        }
        return sim;
    }

    [Theory]
    [Trait("Category", "Perf")]
    [InlineData(500)]
    [InlineData(1000)]
    [InlineData(2500)]
    public void FellingAndBuildingScene_TickCost_At1x2x5x(int marchers)
    {
        Simulation sim = Scene(marchers, out int first);
        for (int t = 0; t < 40; t++) sim.Tick();
        int treesBefore = sim.World.Resources.Count;
        var times = new double[300];
        ResourceStore r = sim.World.Resources;
        NavGrid g = sim.World.NavGrid;
        int felled = 0;
        for (int t = 0; t < times.Length; t++)
        {
            // One exposed tree felled a tick (an opening change), as in the M3-2b felling scene.
            for (int k = (t * 37) % r.Capacity, n = 0; n < r.Capacity; n++, k = (k + 1) % r.Capacity)
            {
                if (!r.Alive[k] || r.TypeId[k] != ResourceMaps.Tree) continue;
                int x = r.Cell[k] % g.Width, y = r.Cell[k] / g.Width;
                if (!g.IsPassable(x + 1, y) && !g.IsPassable(x - 1, y) && !g.IsPassable(x, y + 1) && !g.IsPassable(x, y - 1)) continue;
                r.Take(new EntityHandle(k, r.Generation[k]), ResourceMaps.TreeWood);
                felled++;
                break;
            }
            long s = Stopwatch.GetTimestamp();
            sim.Tick();
            times[t] = (Stopwatch.GetTimestamp() - s) * 1000.0 / Stopwatch.Frequency;
        }
        Array.Sort(times);
        double avg = times.Average(), p99 = times[(int)(times.Length * 0.99)], max = times[^1];
        UnitStore u = sim.World.Units;
        int building = Enumerable.Range(first, 50).Count(i => u.State[i] == UnitState.Building);
        int gathering = Enumerable.Range(first + 50, 30).Count(i => u.State[i] is UnitState.Gathering or UnitState.Returning or UnitState.Moving);
        int marching = Enumerable.Range(0, marchers).Count(i => u.State[i] == UnitState.Moving);
        _out.WriteLine($"{marchers} marching + 50 building + 30 felling: avg {avg:F3} ms, p99 {p99:F3} ms, max {max:F3} ms; {building} building, {gathering} on the gather loop, {marching} still marching, {felled} trees felled ({treesBefore} -> {sim.World.Resources.Count}), wood {sim.World.Wood[0]}");
        Assert.True(building >= 40, $"{building} building");
        if (marchers == 500) Assert.True(avg < 4.0 && p99 < 8.0, $"500-unit design budget: avg {avg:F3}, p99 {p99:F3}");
    }

    /// <summary>
    /// BUG-0080 at a realistic rate: 512 units in 32 goal groups march across the default map while player 0's worker
    /// places a House every 40 ticks (2 s) for 60 s. Reports the longest any unit waited for a usable field. Combat off
    /// (M4-1, BUG-0135): the row measures walkers under placement churn; the two owners' groups would otherwise meet and
    /// fight, and chasers' waits are <c>CombatScaleQaTests.ChasersUnderPlacementChurn_LongestFieldWait_AtMostOneSecond</c> (BUG-0144).
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void APlacementEveryTwoSeconds_32MarchingGroups_LongestFieldWait_Report()
    {
        Simulation sim = MoveScenario.Spawn(11, units: 512, maxCost: 30f, out _, capacity: 520, players: 2, map: ResourceMap, combat: false);
        NavGrid g = sim.World.NavGrid;
        var rng = new Determinism.SimRng(11, 4);
        List<int> open = FlowFieldOracle.PassableCells(g);
        // Far goals: each group's goal is a random cell at least 40 cells from the center.
        int center = MoveScenario.CentralCell(g);
        int[] goals = new int[32];
        for (int k = 0; k < 32; k++)
        {
            int c;
            do c = open[rng.NextInt(0, open.Count)];
            while (Math.Abs(c % g.Width - center % g.Width) + Math.Abs(c / g.Width - center / g.Width) < 40);
            goals[k] = c;
        }
        UnitStore u = sim.World.Units;
        for (int i = 0; i < 512; i++) sim.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), MoveScenario.Center(g, goals[i % 32])));
        sim.Enqueue(Command.SpawnUnit(0, GatherMaps.Laborer, MoveScenario.Center(g, center)));
        sim.Tick();
        sim.Tick();
        EntityHandle placer = MoveScenario.Handle(sim, 512);
        Assert.True(u.Alive[512]);
        BuildMaps.SetTotals(sim, 0, 100_000, 100_000);
        var wait = new int[u.Capacity];
        int longest = 0, placements = 0, longestUnit = -1;
        long blockBefore = g.BlockVersion;
        for (int t = 0; t < 1600; t++)
        {
            if (t < 1200 && t % 40 == 0)
            {
                for (int tries = 0; tries < 200; tries++)
                {
                    int c = open[rng.NextInt(0, open.Count)];
                    if (!sim.World.CanPlace(0, BuildMaps.House, c, out _)) continue;
                    sim.Enqueue(Command.Build(0, placer, BuildMaps.House, MoveScenario.Center(g, c)));
                    placements++;
                    break;
                }
            }
            sim.Tick();
            for (int i = 0; i < 512; i++)
            {
                wait[i] = GridChangeOracle.WaitingForField(sim.World, i) ? wait[i] + 1 : 0;
                if (wait[i] > longest) (longest, longestUnit) = (wait[i], i);
            }
        }
        bool[] arrived = MoveScenario.Arrived(sim.World);
        int moving = Enumerable.Range(0, 512).Count(i => u.State[i] == UnitState.Moving);
        _out.WriteLine($"{placements} placements one every 40 ticks ({g.BlockVersion - blockBefore} closing changes), 32 groups: longest field wait {longest} ticks ({longest / 20.0:F2} s, unit {longestUnit}); arrived {arrived.Take(512).Count(a => a)}, still moving {moving}");
        Assert.True(placements >= 25);
        Assert.True(longest <= 20, $"longest field wait {longest} ticks = {longest / 20.0:F2} s (S2 threshold 1 s)");
    }

    /// <summary>
    /// Push-out cost at scale: a Keep placed on 16 of the player's own units in the middle of a dense blob of
    /// <paramref name="blob"/> own units (2,560 slots), so the ring search walks many occupied cells, each checked against
    /// the whole unit store. Reports the apply tick.
    /// </summary>
    [Theory]
    [Trait("Category", "Perf")]
    [InlineData(0)]
    [InlineData(400)]
    [InlineData(2400)]
    public void PushOut_InADenseBlob_ApplyTickCost_Report(int blob)
    {
        var sim = new Simulation(TestSim.Config(Seed: 5, PlayerCount: 1, UnitCapacity: 2560, CommandCapacity: 4096), ResourceMaps.Flat(128, 128));
        NavGrid g = sim.World.NavGrid;
        const int x0 = 60, y0 = 60;
        // 16 inside, then the blob on the cells round the footprint, ring by ring (one unit a cell).
        for (int k = 0; k < 16; k++) sim.Enqueue(Command.SpawnUnit(0, GatherMaps.Laborer, g.CellCenter(x0 + k % 4, y0 + k / 4)));
        int placedBlob = 0;
        for (int r = 1; placedBlob < blob; r++)
            for (int y = y0 - r; y < y0 + 4 + r && placedBlob < blob; y++)
                for (int x = x0 - r; x < x0 + 4 + r && placedBlob < blob; x++)
                {
                    bool edge = y == y0 - r || y == y0 + 3 + r || x == x0 - r || x == x0 + 3 + r;
                    if (!edge) continue;
                    sim.Enqueue(Command.SpawnUnit(0, GatherMaps.Laborer, g.CellCenter(x, y)));
                    placedBlob++;
                }
        sim.Tick();
        sim.Tick();
        BuildMaps.SetTotals(sim, 0, 1000, 1000);
        sim.Enqueue(Command.Build(0, MoveScenario.Handle(sim, 0), GatherMaps.Keep, g.CellCenter(x0, y0)));
        sim.Tick();
        long s = Stopwatch.GetTimestamp();
        sim.Tick();
        double ms = (Stopwatch.GetTimestamp() - s) * 1000.0 / Stopwatch.Frequency;
        long t2 = Stopwatch.GetTimestamp();
        sim.Tick();
        double next = (Stopwatch.GetTimestamp() - t2) * 1000.0 / Stopwatch.Frequency;
        _out.WriteLine($"Keep on 16 own units inside a blob of {blob}: apply tick {ms:F2} ms (next tick {next:F2} ms); {sim.World.Buildings.Count} site");
        Assert.Equal(1, sim.World.Buildings.Count);
    }
}
