using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Tests.ViewApi;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>
/// QA M3-V1 (2026-10-07-0925): the economy view reads (<see cref="ResourcePicker"/>, <see cref="StartBase"/>,
/// <see cref="BuildingBars"/>, <see cref="DebugCounts.InState"/>) attacked independently of the developer's tests:
/// a hash twin with the picker on every cell every tick, the picker against an oracle through random felling and
/// against the sim's own Gather resolution at footprint edges, and the start-base plan over many seeds and worker counts.
/// </summary>
[Collection(SerialCollection.Name)]
public class EconomyViewQaTests
{
    private readonly ITestOutputHelper _out;

    public EconomyViewQaTests(ITestOutputHelper output) => _out = output;

    // Independent oracle: per cell, the live node whose footprint covers it, else -1.
    private static int[] Owners(World w)
    {
        NavGrid g = w.NavGrid;
        var owner = new int[g.Width * g.Height];
        Array.Fill(owner, -1);
        ResourceStore r = w.Resources;
        for (int i = 0; i < r.Capacity; i++)
        {
            if (!r.Alive[i]) continue;
            ResourceDef d = w.Data.Resources[r.TypeId[i]];
            int ax = r.Cell[i] % g.Width, ay = r.Cell[i] / g.Width;
            for (int y = ay; y < ay + d.FootprintHeight; y++)
                for (int x = ax; x < ax + d.FootprintWidth; x++)
                    owner[y * g.Width + x] = i;
        }
        return owner;
    }

    private static List<EntityHandle> Workers(World w, int player, int type)
    {
        var list = new List<EntityHandle>();
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == player && u.TypeId[i] == type) list.Add(new EntityHandle(i, u.Generation[i]));
        return list;
    }

    private static Vector2 NodePoint(World w, int node)
    {
        NavGrid g = w.NavGrid;
        return g.CellCenter(w.Resources.Cell[node] % g.Width, w.Resources.Cell[node] / g.Width);
    }

    [Theory]
    [InlineData(1UL, 5)]
    [InlineData(6UL, 30)]
    public void HashTwin_PickerOnEveryCell_AllNewReads_EveryTick_400Ticks(ulong seed, int workers)
    {
        (Simulation a, Vector2[][] blocks, StartBasePlan plan) = StartBaseTests.MatchSetup(seed, 100, workers);
        (Simulation b, _, _) = StartBaseTests.MatchSetup(seed, 100, workers);
        StartBaseTests.Apply(a, blocks, plan);
        StartBaseTests.Apply(b, blocks, plan);
        World w = a.World;
        NavGrid g = w.NavGrid;
        UnitStore u = w.Units;
        int cells = g.Width * g.Height;
        Assert.Equal(b.StateHash(), a.StateHash());

        // Every worker of both players gathers: alternating the nearest mine and a tree (queued half of them),
        // plus a house site per player that is cancelled later, and right-click Moves for a few soldiers onto nodes.
        var rng = new Random(unchecked((int)seed) * 7919);
        var commands = new List<Command>();
        for (int p = 0; p < 2; p++)
        {
            List<EntityHandle> ws = Workers(w, p, plan.WorkerType[p]).Where(h => plan.Workers[p].Contains(u.Position[h.Index])).ToList();
            Assert.Equal(workers, ws.Count);
            Vector2 hall = StartBase.FootprintCenter(g, w.Data.Buildings[plan.HallType[p]], plan.HallAnchor[p]);
            int mine = -1, tree = -1;
            float dm = float.MaxValue, dt = float.MaxValue;
            for (int n = 0; n < w.Resources.Capacity; n++)
            {
                if (!w.Resources.Alive[n]) continue;
                float d = Vector2.Distance(NodePoint(w, n), hall);
                bool gold = w.Data.Resources[w.Resources.TypeId[n]].Resource == ResourceKind.Gold;
                if (gold && d < dm) (mine, dm) = (n, d);
                if (!gold && d < dt) (tree, dt) = (n, d);
            }
            for (int k = 0; k < ws.Count; k++)
                commands.Add(Command.Gather(p, ws[k], NodePoint(w, k % 2 == 0 ? mine : tree), queued: k % 4 == 3));
            int house = StartBase.BuildingOfSlot(w.Data, w.FactionOf(p), BuildingSlot.House);
            for (int cell = 0; cell < cells; cell++)
            {
                if (!w.CanPlace(p, house, cell, out _) || Vector2.Distance(g.CellCenter(cell % g.Width, cell / g.Width), hall) > 24f) continue;
                commands.Add(Command.Build(p, ws[0], house, g.CellCenter(cell % g.Width, cell / g.Width), queued: true));
                break;
            }
        }
        foreach (Command c in commands)
        {
            a.Enqueue(c);
            b.Enqueue(c);
        }

        long sink = 0;
        int nodeHits = 0;
        int[] owner = Owners(w);
        for (int tick = 0; tick < 400; tick++)
        {
            if (tick == 300)
            {
                for (int k = 0; k < w.Buildings.Capacity; k++)
                {
                    if (!w.Buildings.Alive[k] || !w.Buildings.UnderConstruction[k]) continue;
                    int c = w.Buildings.Cell[k];
                    Command cancel = Command.Cancel(w.Buildings.Owner[k], g.CellCenter(c % g.Width, c / g.Width));
                    a.Enqueue(cancel);
                    b.Enqueue(cancel);
                }
            }
            ulong before = a.StateHash();
            owner = Owners(w);
            for (int cell = 0; cell < cells; cell++)
            {
                int got = ViewReads.NodeAt(w, cell);
                Assert.True(got == owner[cell], $"tick {tick} cell {cell}: picker {got}, oracle {owner[cell]}");
                if (got >= 0) nodeHits++;
            }
            for (int k = 0; k < 64; k++)
            {
                var pt = new Vector2((float)(rng.NextDouble() * (g.Width + 4) * 2 - 4), (float)(rng.NextDouble() * (g.Height + 4) * 2 - 4));
                sink += ViewReads.NodeAtPoint(w, pt);
            }
            sink += ViewReads.NodeAtPoint(w, new Vector2(float.NaN, 1f)) + ViewReads.NodeAtPoint(w, new Vector2(1f, float.NegativeInfinity));
            for (int k = -1; k <= w.Buildings.Capacity; k++) sink += (int)ViewReads.Bar(w, k, out float f) + (int)(f * 100);
            foreach (UnitState s in Enum.GetValues<UnitState>()) sink += DebugCounts.InState(u.Alive, u.State, s);
            if (tick % 50 == 0)
            {
                StartBasePlan again = ViewReads.Plan(w, blocks, workers, StartBaseTests.MaxRadius(w.Data));
                sink += again.HallAnchor[0] + again.HallAnchor[1];
                var taken = new bool[cells];
                sink += StartBase.WorkerSpots(g, w.Data.Buildings[plan.HallType[0]], plan.HallAnchor[0], 50, taken, new Vector2[50], Vector2.Zero);
                sink += ViewReads.NearestMine(w, Vector2.Zero, out Vector2 m) ? (long)m.X : 0;
            }
            Assert.Equal(before, a.StateHash());
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed} tick {a.TickNumber}: diverged from the bare twin");
        }
        int gathering = DebugCounts.InState(u.Alive, u.State, UnitState.Gathering) + DebugCounts.InState(u.Alive, u.State, UnitState.Returning);
        _out.WriteLine($"seed {seed} workers {workers}: {nodeHits} node-cell hits over 400 ticks, gathering/returning at end {gathering}, gold {w.Gold[0]}/{w.Gold[1]} wood {w.Wood[0]}/{w.Wood[1]}, checksum {sink}");
        Assert.True(gathering > 0);
    }

    [Fact]
    public void Picker_EqualsOracle_ThroughRandomFellingAndPartialTakes_3Seeds()
    {
        foreach (ulong seed in new ulong[] { 2, 9, 17 })
        {
            World w = PropLayoutTests.MatchSim(seed, units: 8, forests: 64, mines: 64).World;
            var rng = new Random((int)seed);
            int cells = w.NavGrid.Width * w.NavGrid.Height, fells = 0;
            for (int round = 0; round < 120; round++)
            {
                for (int k = 0; k < 20; k++)
                {
                    int n = rng.Next(w.Resources.Capacity);
                    if (!w.Resources.Alive[n]) continue;
                    int amount = rng.Next(3) == 0 ? w.Resources.Remaining[n] : rng.Next(1, 30);
                    w.Resources.Take(w.Resources.HandleOf(n), amount);
                    if (!w.Resources.Alive[n]) fells++;
                }
                int[] owner = Owners(w);
                for (int cell = 0; cell < cells; cell++)
                {
                    int got = ViewReads.NodeAt(w, cell);
                    Assert.True(got == owner[cell], $"seed {seed} round {round} cell {cell}: picker {got}, oracle {owner[cell]}");
                }
            }
            _out.WriteLine($"seed {seed}: {fells} nodes felled, picker equal to the oracle after every round");
            Assert.True(fells > 100);
        }
    }

    /// <summary>
    /// The view sends Gather only where the picker sees a node, with the clicked point; the sim then resolves the node from
    /// that point. At footprint edges (exact cell borders, a hair inside, a hair outside) both must agree on which cell
    /// the point lies in: an exposed node the picker names is the node the sim's Gather works.
    /// </summary>
    [Fact]
    public void FootprintEdges_PickerAgreesWithTheSimsGatherResolution_10Seeds()
    {
        int checkedPts = 0, exposedAgree = 0;
        float[] offs = { 0f, 1e-4f, 0.5f, 1f, 2f - 1e-4f, 2f, 2f + 1e-4f, -1e-4f };
        for (ulong seed = 1; seed <= 10; seed++)
        {
            World w = PropLayoutTests.MatchSim(seed, units: 8).World;
            NavGrid g = w.NavGrid;
            ResourceStore r = w.Resources;
            for (int n = 0; n < r.Capacity; n++)
            {
                if (!r.Alive[n]) continue;
                ResourceDef d = w.Data.Resources[r.TypeId[n]];
                float x0 = r.Cell[n] % g.Width * MapConstants.CellSize, y0 = r.Cell[n] / g.Width * MapConstants.CellSize;
                for (int cx = -1; cx <= d.FootprintWidth; cx++)
                {
                    foreach (float ox in offs)
                    {
                        foreach (float oy in offs)
                        {
                            var p = new Vector2(x0 + cx * MapConstants.CellSize + ox, y0 + oy);
                            int picked = ViewReads.NodeAtPoint(w, p);
                            int oracle = g.WorldToCell(p, out int x, out int y) ? Owners1(w, x, y) : -1;
                            Assert.True(picked == oracle, $"seed {seed} node {n} point {p}: picker {picked}, cell oracle {oracle}");
                            checkedPts++;
                            if (picked >= 0 && EconomySystem.IsExposed(w, picked))
                            {
                                int resolved = EconomySystem.ResolveNode(w, p);
                                Assert.True(resolved == picked, $"seed {seed} point {p}: picker {picked} (exposed), sim resolves {resolved}");
                                exposedAgree++;
                            }
                        }
                    }
                }
            }
        }
        _out.WriteLine($"{checkedPts} edge points, {exposedAgree} on exposed nodes resolved identically by the sim");
        Assert.True(exposedAgree > 1000);
    }

    private static int Owners1(World w, int x, int y)
    {
        NavGrid g = w.NavGrid;
        ResourceStore r = w.Resources;
        for (int i = 0; i < r.Capacity; i++)
        {
            if (!r.Alive[i]) continue;
            ResourceDef d = w.Data.Resources[r.TypeId[i]];
            int ax = r.Cell[i] % g.Width, ay = r.Cell[i] / g.Width;
            if (x >= ax && y >= ay && x < ax + d.FootprintWidth && y < ay + d.FootprintHeight) return i;
        }
        return -1;
    }

    /// <summary>
    /// The start-base plan over many seeds, army sizes, worker counts and densities: every planned hall is accepted by
    /// the sim, every planned worker spawns at its spot on open ground outside every footprint, the spots are unique, and
    /// every worker can walk to its hall's ring (4-connected passable flood). The plan is deterministic and read-only.
    /// </summary>
    [Theory]
    [InlineData(100, 5, 12, 8, 1, 120)]
    [InlineData(1000, 5, 12, 8, 1, 30)]
    [InlineData(100, 200, 12, 8, 1, 30)]
    [InlineData(1000, 200, 12, 8, 31, 50)]
    [InlineData(100, 5, 64, 64, 1, 40)]
    [InlineData(0, 5, 12, 8, 1, 30)]
    [InlineData(1000, 200, 64, 64, 1, 40)]
    public void StartBases_ManySeeds_HallsAccepted_WorkersSpawnReachable(int perPlayer, int workers, int forests, int mines, int firstSeed, int lastSeed)
    {
        int noSpot = 0, shortWorkers = 0, unreachable = 0, seeds = 0;
        var notes = new List<string>();
        for (ulong seed = (ulong)firstSeed; seed <= (ulong)lastSeed; seed++)
        {
            seeds++;
            (Simulation sim, Vector2[][] blocks, StartBasePlan plan) = StartBaseTests.MatchSetup(seed, perPlayer, workers, forests, mines, units: 2 * (perPlayer + workers) + 10);
            World w = sim.World;
            NavGrid g = w.NavGrid;
            ulong h0 = sim.StateHash();
            StartBasePlan again = ViewReads.Plan(w, blocks, workers, StartBaseTests.MaxRadius(w.Data));
            Assert.Equal(h0, sim.StateHash());
            for (int p = 0; p < 2; p++)
            {
                Assert.Equal(plan.HallAnchor[p], again.HallAnchor[p]);
                Assert.Equal(plan.Workers[p], again.Workers[p]);
            }
            StartBaseTests.Apply(sim, blocks, plan);
            int halls = 0;
            var spotSet = new HashSet<Vector2>();
            for (int p = 0; p < 2; p++)
            {
                int a = plan.HallAnchor[p];
                if (a < 0)
                {
                    noSpot++;
                    notes.Add($"seed {seed} p{p}: no spot");
                    continue;
                }
                halls++;
                if (plan.Workers[p].Length < workers)
                {
                    shortWorkers++;
                    notes.Add($"seed {seed} p{p}: {plan.Workers[p].Length}/{workers} workers");
                }
                BuildingDef def = w.Data.Buildings[plan.HallType[p]];
                bool[] reach = Flood(g, a % g.Width - 1, a / g.Width - 1);
                int placed = 0;
                foreach (Vector2 s in plan.Workers[p])
                {
                    Assert.True(spotSet.Add(s), $"seed {seed}: two workers planned on {s}");
                    Assert.True(g.WorldToCell(s, out int x, out int y) && g.IsPassable(x, y), $"seed {seed} p{p}: worker spot {s} not passable");
                    Assert.Equal(-1, w.Buildings.SlotAt(x, y));
                    if (!reach[y * g.Width + x])
                    {
                        unreachable++;
                        notes.Add($"seed {seed} p{p}: worker at ({x},{y}) cannot reach its hall at ({a % g.Width},{a / g.Width})");
                    }
                    for (int i = 0; i < w.Units.Capacity; i++)
                        if (w.Units.Alive[i] && w.Units.Owner[i] == p && w.Units.TypeId[i] == plan.WorkerType[p] && w.Units.Position[i] == s) { placed++; break; }
                }
                Assert.True(placed == plan.Workers[p].Length, $"seed {seed} p{p}: {placed} of {plan.Workers[p].Length} planned workers spawned");
                // A worker standing in a block cell would have been planned on top of an army unit.
                foreach (Vector2[] block in blocks)
                    foreach (Vector2 v in block)
                        Assert.DoesNotContain(v, plan.Workers[p]);
            }
            Assert.True(halls == w.Buildings.Count, $"seed {seed}: {w.Buildings.Count} halls stand, {halls} planned");
            Assert.True(plan.HallAnchor[0] >= 0 || perPlayer > 0, "no army and no hall");
        }
        foreach (string n in notes.Take(30)) _out.WriteLine(n);
        _out.WriteLine($"{seeds} seeds at {perPlayer}/side, {workers} workers, {forests}/{mines}: no spot {noSpot}, short of workers {shortWorkers}, unreachable workers {unreachable}");
        Assert.True(unreachable == 0, $"{unreachable} planned workers cannot walk to their Town Hall");
    }

    // 4-connected flood over passable cells from (x, y).
    private static bool[] Flood(NavGrid g, int sx, int sy)
    {
        var seen = new bool[g.Width * g.Height];
        if (!g.IsPassable(sx, sy)) return seen;
        var q = new Queue<int>();
        q.Enqueue(sy * g.Width + sx);
        seen[sy * g.Width + sx] = true;
        while (q.Count > 0)
        {
            int c = q.Dequeue(), x = c % g.Width, y = c / g.Width;
            foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = x + dx, ny = y + dy;
                if (!g.IsPassable(nx, ny) || seen[ny * g.Width + nx]) continue;
                seen[ny * g.Width + nx] = true;
                q.Enqueue(ny * g.Width + nx);
            }
        }
        return seen;
    }

    [Fact]
    public void BuildingBars_StoreFull_256Buildings_AndRecycledSlots()
    {
        Simulation sim = BuildMaps.NewSim(ResourceMaps.Flat(128, 128), players: 2);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        int house = BuildMaps.House;
        BuildingDef def = w.Data.Buildings[house];
        int placed = 0;
        for (int y = 2; y + def.FootprintHeight < g.Height - 1; y += def.FootprintHeight + 1)
            for (int x = 2; x + def.FootprintWidth < g.Width - 1; x += def.FootprintWidth + 1)
            {
                sim.Enqueue(Command.SpawnBuilding(placed % 2, house, g.CellCenter(x, y)));
                placed++;
                if (sim.PendingCommandCount >= 16) sim.Tick();
            }
        sim.Tick();
        Assert.Equal(w.Buildings.Capacity, w.Buildings.Count);
        for (int k = 0; k < w.Buildings.Capacity; k++)
        {
            Assert.Equal(BuildingBarKind.None, ViewReads.Bar(w, k, out float f));
            Assert.Equal(0f, f);
        }
        long bytes = AllocationProbe.Measure(() =>
        {
            for (int k = 0; k < w.Buildings.Capacity; k++) ViewReads.Bar(w, k, out _);
        });
        _out.WriteLine($"{placed} spawns enqueued, {w.Buildings.Count} stand (capacity {w.Buildings.Capacity}); bars over a full store: {bytes} bytes");
        Assert.Equal(0, bytes);
    }

    [Fact]
    public void DebugCounts_InState_OddSpans_AndEveryState()
    {
        var alive = new[] { true, false, true, true };
        var state = new[] { UnitState.Gathering, UnitState.Gathering, UnitState.Returning };
        Assert.Equal(1, DebugCounts.InState(alive, state, UnitState.Gathering));
        Assert.Equal(1, DebugCounts.InState(alive, state, UnitState.Returning));
        Assert.Equal(0, DebugCounts.InState(alive, state, UnitState.Building));
        Assert.Equal(0, DebugCounts.InState(ReadOnlySpan<bool>.Empty, state, UnitState.Gathering));
        Assert.Equal(0, DebugCounts.InState(alive, state, (UnitState)250));
    }
}
