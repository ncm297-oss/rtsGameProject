using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA (M3-2, session 2026-10-06-1503): the gather loop under seeded hostile command streams.
/// <list type="bullet">
/// <item>Conservation per resource kind after every tick: player totals + carried cargo + node
/// <c>Remaining</c> changes only by the documented discard (a worker whose cargo kind switched this tick
/// loses exactly the load it carried), with twin hashes, a replay round trip and the unit invariants.</item>
/// <item>Exposure on the 246 placement-oracle maps (the six sets of <see cref="ResourcePlacementStressTests"/>):
/// workers with random gather orders fell nodes drained near empty; after every fall every passable cell
/// reaches every other, and no node is ever taken while no 4-neighbour of it is open.</item>
/// </list>
/// </summary>
[Collection(SerialCollection.Name)]
public class EconomyFuzzStressTests
{
    private readonly ITestOutputHelper _out;

    public EconomyFuzzStressTests(ITestOutputHelper output) => _out = output;

    // ------------------------------------------------------------------ conservation fuzz

    [Theory]
    [Trait("Category", "Soak")]
    [InlineData(1UL, false)]
    [InlineData(2UL, false)]
    [InlineData(3UL, false)]
    [InlineData(4UL, false)]
    [InlineData(5UL, true)]
    [InlineData(6UL, true)]
    [InlineData(7UL, true)]
    [InlineData(8UL, true)]
    public void HostileGatherStream_3000Ticks_ConservesEveryKind_TwinsMatch_ReplayPlaysBack(ulong seed, bool drained)
    {
        const int ticks = 3000, capacity = 96, players = 2;
        SimConfig config = TestSim.Config(Seed: seed, PlayerCount: players, UnitCapacity: capacity, CommandCapacity: 512)
            with { Map = MapGenParams.Default with { Forests = 12, GoldMines = 8 }, Combat = false };
        var a = new Simulation(config);
        var rec = new ReplayRecorder(a, checkpointInterval: 100);
        var b = new Simulation(config);
        // Same setup in both (EconomyScenario enqueues and ticks; it is deterministic).
        EconomyScenario.Setup(a, 10, player: 0, mineIndex: 0);
        EconomyScenario.Setup(a, 10, player: 1, mineIndex: 1);
        EconomyScenario.Setup(b, 10, player: 0, mineIndex: 0);
        EconomyScenario.Setup(b, 10, player: 1, mineIndex: 1);
        if (drained)
        {
            // Nodes drained near empty by the test seam (in both twins) so falls, redirects and Idle-on-depletion happen;
            // the seam is not in the replay log, so these seeds skip the replay round trip.
            var drain = new SimRng(seed, 99);
            for (int n = 0; n < a.World.Resources.Capacity; n++)
            {
                if (!a.World.Resources.Alive[n]) continue;
                bool mine = a.World.Data.Resources[a.World.Resources.TypeId[n]].Resource == ResourceKind.Gold;
                int keep = mine ? 20 + drain.NextInt(0, 40) : 2 + drain.NextInt(0, 12);
                int take = a.World.Resources.Remaining[n] - keep;
                a.World.Resources.Take(a.World.Resources.HandleOf(n), take);
                b.World.Resources.Take(b.World.Resources.HandleOf(n), take);
            }
        }
        Assert.Equal(a.StateHash(), b.StateHash());

        World w = a.World;
        NavGrid g = w.NavGrid;
        UnitStore u = w.Units;
        ResourceStore r = w.Resources;
        var open = new List<Vector2>();
        for (int c = 0; c < g.Width * g.Height; c += 5)
            if (g.IsPassable(c % g.Width, c / g.Width)) open.Add(g.CellCenter(c % g.Width, c / g.Width));
        var nodeCells = new List<Vector2>(); // every node's anchor at the start; dead ones stay in the list (hostile: no node there any more)
        for (int n = 0; n < r.Capacity; n++)
            if (r.Alive[n]) nodeCells.Add(g.CellCenter(r.Cell[n] % g.Width, r.Cell[n] / g.Width) + new Vector2(0.4f, -0.3f));
        var keepCells = new List<Vector2>();
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k]) keepCells.Add(g.CellCenter(w.Buildings.Cell[k] % g.Width, w.Buildings.Cell[k] / g.Width) + new Vector2(2f, 2f));

        var rng = new SimRng(seed, 31337);
        int carry = w.Data.Rules.WorkerCarry;
        var born = new int[capacity];
        Array.Fill(born, -1);
        var bornOnGround = new bool[capacity];
        var genBefore = new int[capacity];
        var aliveBefore = new bool[capacity];
        var kindBefore = new ResourceKind[capacity];
        var cargoBefore = new int[capacity];
        var remBefore = new int[r.Capacity];
        var nodeAliveBefore = new bool[r.Capacity];
        var nodeCellBefore = new int[r.Capacity];
        var nodeTypeBefore = new int[r.Capacity];
        var touched = new HashSet<int>();
        long goldDiscarded = 0, woodDiscarded = 0, taken = 0;
        int gathers = 0, spawnsB = 0, falls = 0;

        for (int t = 0; t < ticks; t++)
        {
            touched.Clear();
            // Hostile bursts (200 ticks of up to 6 orders a tick), then 400 calmer ticks so loops run whole trips.
            int count = t % 600 < 200 ? (rng.NextInt(0, 2) == 0 ? rng.NextInt(0, 3) : rng.NextInt(0, 7)) : (rng.NextInt(0, 8) == 0 ? 1 : 0);
            for (int k = 0; k < count; k++)
            {
                Command c = RandomCommand(ref rng, a, open, nodeCells, keepCells, players, touched);
                if (c.Kind == CommandKind.Noop) continue;
                if (c.Kind == CommandKind.Gather) gathers++;
                if (c.Kind == CommandKind.SpawnBuilding) spawnsB++;
                a.Enqueue(c);
                b.Enqueue(c);
            }

            (long gold0, long wood0) = GatherMaps.Conserved(w);
            for (int i = 0; i < capacity; i++)
            {
                aliveBefore[i] = u.Alive[i];
                genBefore[i] = u.Generation[i];
                kindBefore[i] = u.CargoKind[i];
                cargoBefore[i] = u.Cargo[i];
            }
            r.Remaining.CopyTo(remBefore);
            r.Alive.CopyTo(nodeAliveBefore);
            r.Cell.CopyTo(nodeCellBefore);
            r.TypeId.CopyTo(nodeTypeBefore);
            int totalsGold = w.Gold[0] + w.Gold[1], totalsWood = w.Wood[0] + w.Wood[1];

            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {a.TickNumber}");

            // The documented discard: a live unit whose cargo kind switched lost the load it carried.
            long lossGold = 0, lossWood = 0;
            for (int i = 0; i < capacity; i++)
            {
                if (!aliveBefore[i] || !u.Alive[i] || genBefore[i] != u.Generation[i]) continue;
                if (u.CargoKind[i] == kindBefore[i]) continue;
                if (kindBefore[i] == ResourceKind.Gold) lossGold += cargoBefore[i];
                else lossWood += cargoBefore[i];
            }
            goldDiscarded += lossGold;
            woodDiscarded += lossWood;
            (long gold1, long wood1) = GatherMaps.Conserved(w);
            Assert.True(gold1 == gold0 - lossGold, $"seed {seed} tick {a.TickNumber}: gold {gold0} -> {gold1}, documented discard {lossGold}");
            Assert.True(wood1 == wood0 - lossWood, $"seed {seed} tick {a.TickNumber}: wood {wood0} -> {wood1}, documented discard {lossWood}");
            Assert.True(w.Gold[0] + w.Gold[1] >= totalsGold && w.Wood[0] + w.Wood[1] >= totalsWood, $"seed {seed} tick {a.TickNumber}: a total went down");

            // Nodes: positive while alive; a node that lost wood or gold had an open 4-neighbour.
            for (int n = 0; n < r.Capacity; n++)
            {
                if (r.Alive[n]) Assert.True(r.Remaining[n] > 0, $"seed {seed} tick {a.TickNumber}: live node {n} remaining {r.Remaining[n]}");
                if (!nodeAliveBefore[n]) continue;
                bool fell = !r.Alive[n]; // nothing spawns nodes in this fuzz, so a slot never comes back
                int lost = fell ? remBefore[n] : remBefore[n] - r.Remaining[n];
                if (lost <= 0) continue;
                taken += lost;
                if (!r.Alive[n]) falls++;
                ResourceDef def = w.Data.Resources[nodeTypeBefore[n]];
                if (!RingOpen(g, nodeCellBefore[n], def.FootprintWidth, def.FootprintHeight))
                    Assert.Fail($"seed {seed} tick {a.TickNumber}: node {n} at cell {nodeCellBefore[n]} lost {lost} with no open 4-neighbour");
            }

            CheckUnits(a, born, bornOnGround, carry, players, seed);
        }

        // Replay round trip with SpawnBuilding and Gather in the log.
        Replay rep = rec.ToReplay();
        if (!drained)
        {
            Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(rep), out Replay? back));
            ReplayResult res = ReplayPlayer.Run(back!, TestSim.Data, combat: false); // recorded with combat off (BUG-0135)
            Assert.True(res.Ok, $"seed {seed}: replay {res.Error} at tick {res.Tick}");
        }
        _out.WriteLine($"seed {seed}: {gathers} Gather + {spawnsB} SpawnBuilding sent; {w.Buildings.Count} buildings; {falls} nodes fell; {taken} taken; discarded on kind change gold {goldDiscarded} wood {woodDiscarded}; totals p0 {w.Gold[0]}/{w.Wood[0]} p1 {w.Gold[1]}/{w.Wood[1]}; {u.Count} units; replay {rep.Commands.Length} commands, {rep.Checkpoints.Length} checkpoints {(drained ? "(drained: not replayed)" : "matched")}");
        Assert.True(gathers > 500 && taken > 100 && w.Gold[0] + w.Gold[1] + w.Wood[0] + w.Wood[1] > 800 + 50 && (!drained || falls > 5), "precondition: the fuzz gathered and delivered");
    }

    private static bool RingOpen(NavGrid g, int anchor, int fw, int fh)
    {
        int x0 = anchor % g.Width, y0 = anchor / g.Width;
        for (int x = x0; x < x0 + fw; x++)
            if (g.IsPassable(x, y0 - 1) || g.IsPassable(x, y0 + fh)) return true;
        for (int y = y0; y < y0 + fh; y++)
            if (g.IsPassable(x0 - 1, y) || g.IsPassable(x0 + fw, y)) return true;
        return false;
    }

    private static Command RandomCommand(ref SimRng rng, Simulation sim, List<Vector2> open, List<Vector2> nodes, List<Vector2> keeps, int players, HashSet<int> touched)
    {
        UnitStore u = sim.World.Units;
        int slot = rng.NextInt(0, u.Capacity);
        if (u.Alive[slot] && !touched.Add(slot)) return Command.Noop(0); // one order per live unit per tick keeps the discard accounting exact
        EntityHandle h = rng.NextInt(0, 12) switch
        {
            0 => new EntityHandle(slot, u.Generation[slot] + 1), // stale
            1 => default,
            2 => new EntityHandle(u.Capacity + 3, 1),
            _ => new EntityHandle(slot, u.Generation[slot]),
        };
        int player = u.Alive[slot] && rng.NextInt(0, 6) != 0 ? u.Owner[slot] : rng.NextInt(0, players); // some foreign
        Vector2 p = rng.NextInt(0, 20) switch
        {
            0 => new Vector2(-3f, 5f),                                   // off the map
            1 => new Vector2(0.5f, 0.5f),                                // border cell
            2 => keeps[rng.NextInt(0, keeps.Count)],                     // inside a Keep
            < 11 => nodes[rng.NextInt(0, nodes.Count)],                  // on a node (maybe dead now)
            _ => open[rng.NextInt(0, open.Count)] + new Vector2(rng.NextFloat() - 0.5f, rng.NextFloat() - 0.5f),
        };
        bool queued = rng.NextInt(0, 5) == 0;
        return rng.NextInt(0, 100) switch
        {
            < 45 => Command.Gather(player, h, p, queued),
            < 65 => Command.Move(player, h, p, queued),
            < 73 => Command.Stop(player, h, queued),
            < 77 => Command.HoldPosition(player, h, queued),
            < 81 => Command.SpawnBuilding(rng.NextInt(0, players), rng.NextInt(-1, TestSim.Data.Buildings.Length + 1),
                rng.NextInt(0, 2) == 0 ? nodes[rng.NextInt(0, nodes.Count)] + new Vector2(-6f, 2f) : p),
            < 93 => Command.SpawnUnit(rng.NextInt(0, players), rng.NextInt(0, 3) == 0 ? GatherMaps.Infantry : GatherMaps.Laborer, p),
            _ => Command.AttackMove(player, h, p, queued),
        };
    }

    private static void CheckUnits(Simulation sim, int[] born, bool[] bornOnGround, int carry, int players, ulong seed)
    {
        UnitStore u = sim.World.Units;
        NavGrid g = sim.World.NavGrid;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            string at = $"seed {seed} tick {sim.TickNumber} unit {i}";
            Vector2 p = u.Position[i];
            Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y), $"{at}: position {p}");
            if (born[i] != u.Generation[i])
            {
                born[i] = u.Generation[i];
                bornOnGround[i] = g.WorldToCell(u.PrevPosition[i], out int bx, out int by) && g.IsPassable(bx, by);
            }
            if (bornOnGround[i]) Assert.True(g.WorldToCell(p, out int x, out int y) && g.IsPassable(x, y), $"{at}: on blocked ground at {p} ({u.State[i]})");
            Assert.True((uint)u.Owner[i] < (uint)players, $"{at}: owner");
            Assert.True(u.Cargo[i] >= 0 && u.Cargo[i] <= carry, $"{at}: cargo {u.Cargo[i]}");
            Assert.True(u.GatherProgress[i] >= 0f && u.GatherProgress[i] < 1f, $"{at}: progress {u.GatherProgress[i]}");
            bool onLoop = EconomySystem.OnLoop(u, i);
            if (onLoop)
            {
                Assert.True(EconomySystem.IsWorker(sim.World, i), $"{at}: a non-worker on a gather loop");
                Assert.True((uint)u.GatherNode[i].Index < (uint)sim.World.Resources.Capacity, $"{at}: node handle {u.GatherNode[i].Index}");
            }
            if (u.State[i] is UnitState.Gathering or UnitState.Returning) Assert.True(onLoop, $"{at}: {u.State[i]} off the loop");
            if (u.Hold[i]) Assert.True(!onLoop && u.State[i] == UnitState.Idle, $"{at}: holding but {u.State[i]}, on loop {onLoop}");
        }
    }

    // ------------------------------------------------------------------ exposure fuzz on the placement maps

    private static readonly MapGenParams Small48 = MapGenParams.Default with
    {
        Width = 48, Height = 48, EdgeMargin = 2, Level1Plateaus = 3, Level1MinSize = 6, Level1MaxSize = 16,
        Level2Plateaus = 2, Level2MinSize = 5, Level2MaxSize = 8, Level2Inset = 1,
    };

    private static readonly MapGenParams Rect64x32 = MapGenParams.Default with
    {
        Width = 64, Height = 32, EdgeMargin = 2, Level1Plateaus = 3, Level1MinSize = 6, Level1MaxSize = 14,
        Level2Plateaus = 2, Level2MinSize = 5, Level2MaxSize = 8, Level2Inset = 1,
    };

    /// <summary>The six sets (246 seeds) of <see cref="ResourcePlacementStressTests.Sets"/>, same parameters and seeds.</summary>
    public static IEnumerable<object[]> PlacementSets()
    {
        yield return new object[] { "design 128 F12 M8", MapGenParams.Default with { Forests = 12, GoldMines = 8 }, 21UL, 80 };
        yield return new object[] { "dense 128 F40 x 60", MapGenParams.Default with { Forests = 40, ForestMinTrees = 60, ForestMaxTrees = 60, GoldMines = 8 }, 1UL, 40 };
        yield return new object[] { "48x48 M20 F4", Small48 with { GoldMines = 20, Forests = 4, MineSpacing = 6f }, 1UL, 50 };
        yield return new object[] { "64x32 M20 F6", Rect64x32 with { GoldMines = 20, Forests = 6, MineSpacing = 6f }, 1UL, 50 };
        yield return new object[] { "48x48 M64 spacing 0, F64 x 1-256", Small48 with { GoldMines = 64, MineSpacing = 0f, Forests = 64, ForestMinTrees = 1, ForestMaxTrees = 256 }, 1UL, 20 };
        yield return new object[] { "caps 128 F64 x 200-256 M64", MapGenParams.Default with { Forests = 64, ForestMinTrees = 200, ForestMaxTrees = 256, GoldMines = 64, MineSpacing = 0f }, 1UL, 6 };
    }

    [Theory]
    [Trait("Category", "Soak")]
    [MemberData(nameof(PlacementSets))]
    public void RandomGatherOrders_OnThePlacementMaps_NeverOpenAPocket_NeverTakeAnInteriorNode(string name, MapGenParams map, ulong firstSeed, int seeds)
    {
        const int workers = 24, ticks = 400;
        long falls = 0, takes = 0, preTickUnexposedTakes = 0;
        for (ulong seed = firstSeed; seed < firstSeed + (ulong)seeds; seed++)
        {
            var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 1, UnitCapacity: workers, CommandCapacity: 256) with { Map = map });
            World w = sim.World;
            NavGrid g = w.NavGrid;
            ResourceStore r = w.Resources;
            var flood = new Flood(g);
            Assert.True(flood.Connected(), $"{name} seed {seed}: pocket before anything was felled");
            var rng = new SimRng(seed, 777);

            // Drain every node to 1-3 (a test seam that never fells), so felling happens fast.
            var nodes = new List<int>();
            var startCells = new int[r.Capacity];
            for (int n = 0; n < r.Capacity; n++)
            {
                if (!r.Alive[n]) continue;
                nodes.Add(n);
                startCells[n] = r.Cell[n];
                int keep = 1 + rng.NextInt(0, 3);
                if (r.Remaining[n] > keep) r.Take(r.HandleOf(n), r.Remaining[n] - keep);
            }
            Assert.True(nodes.Count > 0, $"{name} seed {seed}: no nodes");
            // Workers on open cells 4-adjacent to random nodes, each ordered onto a random node (often an interior one).
            for (int k = 0; k < workers; k++)
            {
                int n = nodes[rng.NextInt(0, nodes.Count)];
                ResourceDef def = w.Data.Resources[r.TypeId[n]];
                if (TryRingCell(g, r.Cell[n], def.FootprintWidth, def.FootprintHeight, ref rng, out Vector2 at))
                    sim.Enqueue(Command.SpawnUnit(0, GatherMaps.Laborer, at));
            }
            sim.Tick();
            sim.Tick();
            var types = new int[r.Capacity];
            var cells = new int[r.Capacity];
            var rem = new int[r.Capacity];
            var alive = new bool[r.Capacity];
            UnitStore u = w.Units;
            var orderedAt = new int[u.Capacity];
            Array.Fill(orderedAt, -100);
            for (int t = 0; t < ticks; t++)
            {
                // Idle workers (and now and then a busy one) get a new random order onto any node, alive or not.
                for (int i = 0; i < u.Capacity; i++)
                {
                    // Off the loop (it ended) and not ordered in the last 3 ticks, or now and then a busy one.
                    bool idle = !EconomySystem.OnLoop(u, i) && t - orderedAt[i] > 3;
                    if (!u.Alive[i] || (!idle && rng.NextInt(0, 400) != 0)) continue;
                    orderedAt[i] = t;
                    int n = nodes[rng.NextInt(0, nodes.Count)];
                    int cell = startCells[n]; // the node may be gone: then the order resolves to a neighbour or drops
                    sim.Enqueue(Command.Gather(0, new EntityHandle(i, u.Generation[i]), g.CellCenter(cell % g.Width, cell / g.Width), queued: false));
                }
                r.Alive.CopyTo(alive);
                r.Remaining.CopyTo(rem);
                for (int n = 0; n < r.Capacity; n++)
                {
                    if (!alive[n]) continue;
                    cells[n] = r.Cell[n];
                    types[n] = r.TypeId[n];
                }
                int version = g.Version;
                // Pre-tick exposure of every node, judged from the grid before the tick.
                var exposedBefore = new bool[r.Capacity];
                for (int n = 0; n < r.Capacity; n++)
                {
                    if (!alive[n]) continue;
                    ResourceDef d = w.Data.Resources[types[n]];
                    exposedBefore[n] = RingOpen(g, cells[n], d.FootprintWidth, d.FootprintHeight);
                }
                sim.Tick();
                for (int n = 0; n < r.Capacity; n++)
                {
                    if (!alive[n]) continue;
                    bool dead = !r.Alive[n];
                    int lost = dead ? rem[n] : rem[n] - r.Remaining[n];
                    if (lost <= 0) continue;
                    takes++;
                    if (dead) falls++;
                    ResourceDef d = w.Data.Resources[types[n]];
                    Assert.True(RingOpen(g, cells[n], d.FootprintWidth, d.FootprintHeight),
                        $"{name} seed {seed} tick {sim.TickNumber}: node {n} at cell {cells[n]} taken with no open 4-neighbour (an interior node)");
                    if (!exposedBefore[n]) preTickUnexposedTakes++; // only legal when a neighbour fell earlier in the same phase
                }
                if (g.Version != version)
                    Assert.True(flood.Connected(), $"{name} seed {seed} tick {sim.TickNumber}: a fall left an open cell nobody can reach ({flood.Last})");
            }
        }
        _out.WriteLine($"{name}: {seeds} seeds x {ticks} ticks, {workers} workers: {takes} node-ticks taken, {falls} falls, every fall checked for pockets; takes from a node unexposed before the tick (a neighbour fell earlier the same tick): {preTickUnexposedTakes}");
        Assert.True(falls > seeds, $"{name}: precondition, only {falls} falls");
    }

    private static bool TryRingCell(NavGrid g, int anchor, int fw, int fh, ref SimRng rng, out Vector2 at)
    {
        int x0 = anchor % g.Width, y0 = anchor / g.Width, ring = 2 * (fw + fh), start = rng.NextInt(0, ring);
        for (int k = 0; k < ring; k++)
        {
            int q = (start + k) % ring, x, y;
            if (q < fw) { x = x0 + q; y = y0 - 1; }
            else if (q < 2 * fw) { x = x0 + q - fw; y = y0 + fh; }
            else if (q < 2 * fw + fh) { x = x0 - 1; y = y0 + q - 2 * fw; }
            else { x = x0 + fw; y = y0 + q - 2 * fw - fh; }
            if (!g.IsPassable(x, y)) continue;
            at = g.CellCenter(x, y);
            return true;
        }
        at = default;
        return false;
    }

    /// <summary>4-connected flood over passable cells with reused buffers: true when one region holds them all.</summary>
    private sealed class Flood
    {
        private readonly NavGrid _g;
        private readonly int[] _seen;
        private readonly int[] _queue;
        private int _stamp;

        public Flood(NavGrid g)
        {
            _g = g;
            _seen = new int[g.Width * g.Height];
            _queue = new int[g.Width * g.Height];
        }

        public string Last { get; private set; } = "";

        public bool Connected()
        {
            int w = _g.Width, n = w * _g.Height, open = 0, start = -1;
            for (int i = 0; i < n; i++)
            {
                if (!_g.IsPassable(i % w, i / w)) continue;
                open++;
                if (start < 0) start = i;
            }
            if (start < 0) return true;
            _stamp++;
            int h = 0, t = 0;
            _queue[t++] = start;
            _seen[start] = _stamp;
            while (h < t)
            {
                int c = _queue[h++], x = c % w, y = c / w;
                Visit(x - 1, y, ref t);
                Visit(x + 1, y, ref t);
                Visit(x, y - 1, ref t);
                Visit(x, y + 1, ref t);
            }
            Last = $"flood reaches {t} of {open} passable cells";
            return t == open;
        }

        private void Visit(int x, int y, ref int t)
        {
            if (!_g.IsPassable(x, y)) return;
            int j = y * _g.Width + x;
            if (_seen[j] == _stamp) return;
            _seen[j] = _stamp;
            _queue[t++] = j;
        }
    }
}
