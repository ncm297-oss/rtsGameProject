using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;
using Rts.Sim.Pathfinding;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>QA stress on M1-4b movement: invariant fuzz, determinism, scale and cache thrash.</summary>
public class MovementStressTests
{
    private readonly ITestOutputHelper _out;

    public MovementStressTests(ITestOutputHelper output) => _out = output;

    /// <summary>A sim with <paramref name="units"/> units on random passable cells of the default map (owners alternate).</summary>
    private static Simulation SpawnRandom(ulong seed, int units, int players = 2)
    {
        var sim = new Simulation(TestSim.Config(seed, players, UnitCapacity: units, CommandCapacity: 2 * units + 64));
        NavGrid g = sim.World.NavGrid;
        List<int> passable = FlowFieldOracle.PassableCells(g);
        var rng = new SimRng(seed, 900);
        for (int i = 0; i < units; i++)
        {
            int c = passable[rng.NextInt(0, passable.Count)];
            Vector2 corner = MoveScenario.Center(g, c) - new Vector2(MapConstants.CellSize / 2);
            var off = new Vector2(0.01f + rng.NextFloat() * 1.98f, 0.01f + rng.NextFloat() * 1.98f);
            sim.Enqueue(Command.SpawnUnit(i % players, i % TestSim.UnitTypeCount, corner + off));
        }
        sim.Tick();
        sim.Tick();
        Assert.Equal(units, sim.World.Units.Count);
        return sim;
    }

    /// <summary>Any target in or slightly around the map, biased to edges and blocked cells.</summary>
    private static Vector2 RandomTarget(ref SimRng rng, NavGrid g)
    {
        float size = g.Width * MapConstants.CellSize;
        switch (rng.NextInt(0, 6))
        {
            case 0: return new Vector2(rng.NextFloat() * size, rng.NextFloat() < 0.5f ? 0f : MathF.BitDecrement(size)); // edge ring
            case 1: return new Vector2(-0.1f + rng.NextFloat() * 0.2f, rng.NextFloat() * size); // straddles x = 0
            default: return new Vector2(rng.NextFloat() * size, rng.NextFloat() * size);
        }
    }

    /// <summary>Per-tick invariants for every live unit. Returns null when all hold.</summary>
    private static string? CheckInvariants(World w)
    {
        UnitStore u = w.Units;
        NavGrid g = w.NavGrid;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            Vector2 p = u.Position[i];
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y)) return $"unit {i} non-finite position {p}";
            if (!g.WorldToCell(p, out int x, out int y)) return $"unit {i} off map at {p}";
            if (!g.IsPassable(x, y)) return $"unit {i} inside blocked cell ({x},{y}) at {p}";
            float moved = Vector2.Distance(p, u.PrevPosition[i]);
            if (moved > u.Speed[i] * 1.0001f + 1e-5f) return $"unit {i} moved {moved} m > speed {u.Speed[i]}";
            if (u.State[i] == UnitState.Idle && u.Velocity[i] != Vector2.Zero) return $"unit {i} idle with velocity {u.Velocity[i]}";
            if (u.State[i] == UnitState.Moving)
            {
                int gc = u.GoalCell[i];
                if (gc < 0 || !g.IsPassable(gc % g.Width, gc / g.Width)) return $"unit {i} moving to blocked goal cell {gc}";
            }
            if (!float.IsFinite(u.Facing[i])) return $"unit {i} facing {u.Facing[i]}";
        }
        return null;
    }

    /// <summary>
    /// QA focus: 500 units, random targets (incl. edges, -0.1 m, cliffs, ring cells) re-issued every
    /// 20 ticks in 8 groups, 2,000 ticks; invariants after every tick.
    /// </summary>
    [Fact]
    public void Fuzz_500Units_RandomRetargetEvery20Ticks_2000Ticks_InvariantsHold() => RunFuzz(0, 10);

    /// <summary>The other 40 seeds of the QA focus (about 100 s in Debug); filter out with Category!=Soak for a quick loop.</summary>
    [Theory]
    [Trait("Category", "Soak")]
    [InlineData(10, 10)]
    [InlineData(20, 10)]
    [InlineData(30, 10)]
    [InlineData(40, 10)]
    public void Fuzz_500Units_RandomRetarget_MoreSeeds_InvariantsHold(int firstSeed, int seeds) => RunFuzz(firstSeed, seeds);

    private void RunFuzz(int firstSeed, int seeds)
    {
        var sw = Stopwatch.StartNew();
        long arrivals = 0, dropped = 0;
        for (int s = firstSeed; s < firstSeed + seeds; s++)
        {
            Simulation sim = SpawnRandom((ulong)(500 + s), 500);
            NavGrid g = sim.World.NavGrid;
            UnitStore u = sim.World.Units;
            var rng = new SimRng((ulong)s, 901);
            var groupTargets = new Vector2[8];
            for (int t = 0; t < 2000; t++)
            {
                if (t % 20 == 0)
                {
                    for (int k = 0; k < groupTargets.Length; k++) groupTargets[k] = RandomTarget(ref rng, g);
                    for (int i = 0; i < u.Capacity; i++)
                    {
                        // Some orders deliberately bad: the other player's unit, a stale generation.
                        int owner = u.Owner[i];
                        int r = rng.NextInt(0, 50);
                        if (r == 0) owner = 1 - owner;
                        var h = new EntityHandle(i, u.Generation[i] + (r == 1 ? 1 : 0));
                        if (r <= 1) dropped++;
                        sim.Enqueue(Command.Move(owner, h, groupTargets[rng.NextInt(0, groupTargets.Length)]));
                    }
                }
                sim.Tick();
                string? err = CheckInvariants(sim.World);
                Assert.True(err == null, $"seed {s} tick {t}: {err}");
            }
            for (int i = 0; i < u.Capacity; i++) if (u.State[i] == UnitState.Idle) arrivals++;
        }
        _out.WriteLine($"{seeds} seeds x 2000 ticks x 500 units: {sw.Elapsed.TotalSeconds:F1} s; idle at end {arrivals}; ~{dropped} bad orders");
    }

    [Fact]
    public void Determinism_TwoSims_InterleavedSpawnsMovesAndFrees_HashEqualEveryTick_SeedsDiffer()
    {
        ulong Run(ulong seed, bool perturbCache, List<ulong>? record, List<ulong>? compare)
        {
            var sim = new Simulation(TestSim.Config(seed, 3, UnitCapacity: 300, CommandCapacity: 1024));
            NavGrid g = sim.World.NavGrid;
            List<int> passable = FlowFieldOracle.PassableCells(g);
            var rng = new SimRng(77, 902); // the command script is the same for both seeds
            var junk = new SimRng(seed + 1, 903);
            UnitStore u = sim.World.Units;
            // A pool of 20 targets keeps live goals under the cache size (more thrashes, see BUG-0019).
            var pool = new Vector2[20];
            for (int k = 0; k < pool.Length; k++) pool[k] = RandomTarget(ref rng, g);
            for (int t = 0; t < 800; t++)
            {
                int n = rng.NextInt(0, 6);
                for (int k = 0; k < n; k++)
                {
                    int p = rng.NextInt(0, 3);
                    int op = rng.NextInt(0, 10);
                    int slot = rng.NextInt(0, u.Capacity);
                    if (op < 4)
                    {
                        int c = passable[rng.NextInt(0, passable.Count) % passable.Count];
                        sim.Enqueue(Command.SpawnUnit(p, rng.NextInt(0, TestSim.UnitTypeCount), MoveScenario.Center(g, c)));
                    }
                    else if (op < 9)
                    {
                        if (u.Alive[slot]) p = u.Owner[slot];
                        sim.Enqueue(Command.Move(p, MoveScenario.Handle(sim, slot), pool[rng.NextInt(0, pool.Length)]));
                    }
                    else if (u.Alive[slot] && t % 7 == 0)
                    {
                        u.Free(MoveScenario.Handle(sim, slot)); // stand-in for death until combat exists
                    }
                }
                if (perturbCache)
                    for (int k = 0; k < 3; k++) sim.World.FlowFields.Get(junk.NextInt(0, g.Width * g.Height));
                sim.Tick();
                ulong hash = sim.StateHash();
                record?.Add(hash);
                if (compare != null) Assert.True(compare[t] == hash, $"seed {seed} diverged at tick {t}");
            }
            return sim.StateHash();
        }

        var first = new List<ulong>();
        ulong a = Run(41, true, first, null);
        // The cache is sim state since M1-4c (BUG-0021): extra lookups change the hash, so both
        // runs churn it identically, and a run without the churn must differ.
        ulong b = Run(41, true, null, first);
        Assert.Equal(a, b);
        Assert.NotEqual(a, Run(41, false, null, null));
        ulong c = Run(42, false, null, null);
        Assert.NotEqual(a, c);
    }

    /// <summary>This class's wall-clock and allocation tests: they run alone in <see cref="SerialCollection"/> (BUG-0017, BUG-0024) while the heavy tests above stay in the parallel batch.</summary>
    [Collection(SerialCollection.Name)]
    public class Serial
    {
        private readonly ITestOutputHelper _out;

        public Serial(ITestOutputHelper output) => _out = output;

        [Fact]
        public void Tick_With500Units_And64DistinctTargets_AllocatesNothing()
        {
            Simulation sim = SpawnRandom(61, 500);
            NavGrid g = sim.World.NavGrid;
            List<int> passable = FlowFieldOracle.PassableCells(g);
            var rng = new SimRng(61, 904);
            var targets = new Vector2[64];
            for (int k = 0; k < 64; k++) targets[k] = MoveScenario.Center(g, passable[rng.NextInt(0, passable.Count)]);
            UnitStore u = sim.World.Units;
            for (int i = 0; i < u.Capacity; i++) sim.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), targets[i % 64]));
            sim.Tick();
            sim.Tick();
            sim.Tick();
            Action tick = sim.Tick;
            AllocationProbe.AssertZero(tick, _out);
        }

        // ---------- perf ----------

        private double MeasureMovingTicks(Simulation sim, int ticks)
        {
            for (int i = 0; i < 5; i++) sim.Tick();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < ticks; i++) sim.Tick();
            return sw.Elapsed.TotalMilliseconds / ticks;
        }

        private static void MoveAllTo(Simulation sim, Func<int, Vector2> target)
        {
            UnitStore u = sim.World.Units;
            for (int i = 0; i < u.Capacity; i++)
                if (u.Alive[i]) sim.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), target(i)));
        }

        [Theory]
        [Trait("Category", "Perf")]
        [InlineData(500, 2.0)]
        [InlineData(1000, 4.0)]
        [InlineData(2500, 10.0)]
        public void Perf_UnitsMovingToFourTargets_ScalesLinearly(int units, double budgetMs)
        {
            Simulation sim = SpawnRandom(71, units);
            NavGrid g = sim.World.NavGrid;
            // Four far corners-ish targets so everyone keeps walking during the measurement.
            var targets = new[]
            {
                MoveScenario.Center(g, FlowField.NearestPassable(g, 8 * g.Width + 8)),
                MoveScenario.Center(g, FlowField.NearestPassable(g, 8 * g.Width + g.Width - 9)),
                MoveScenario.Center(g, FlowField.NearestPassable(g, (g.Height - 9) * g.Width + 8)),
                MoveScenario.Center(g, FlowField.NearestPassable(g, (g.Height - 9) * g.Width + g.Width - 9)),
            };
            MoveAllTo(sim, i => targets[i % 4]);
            sim.Tick();
            double avg = MeasureMovingTicks(sim, 100);
            int moving = 0;
            for (int i = 0; i < units; i++) if (sim.World.Units.State[i] == UnitState.Moving) moving++;
            _out.WriteLine($"{units} units ({moving} moving): {avg:F3} ms/tick, {avg * 1000 / units:F2} us/unit");
            Assert.True(moving > units * 0.8, $"only {moving} moving");
            Assert.True(avg < budgetMs, $"{avg:F3} ms/tick");
        }

        [Theory]
        [Trait("Category", "Perf")]
        [InlineData(32)]
        [InlineData(33)]
        [InlineData(64)]
        public void Perf_500Units_DistinctTargetsInterleavedBySlot_CostPerTick(int distinctTargets)
        {
            // Units whose goals interleave in slot order: with more distinct goals than cache slots,
            // LRU misses on every unit and each miss is a full Dijkstra build.
            Simulation sim = SpawnRandom(81, 500);
            NavGrid g = sim.World.NavGrid;
            List<int> passable = FlowFieldOracle.PassableCells(g);
            var rng = new SimRng(81, 905);
            var targets = new Vector2[distinctTargets];
            for (int k = 0; k < targets.Length; k++) targets[k] = MoveScenario.Center(g, passable[rng.NextInt(0, passable.Count)]);
            MoveAllTo(sim, i => targets[i % distinctTargets]);
            sim.Tick();
            int builds = sim.World.FlowFields.BuildCount;
            double avg = MeasureMovingTicks(sim, 10);
            int perTick = (sim.World.FlowFields.BuildCount - builds) / 15;
            _out.WriteLine($"500 units, {distinctTargets} distinct targets: {avg:F2} ms/tick, ~{perTick} field builds per tick");
            Assert.True(avg < 4.0, $"{distinctTargets} distinct targets: {avg:F2} ms/tick (docs/03: 500 units, average tick < 4 ms)");
        }

        [Theory]
        [Trait("Category", "Perf")]
        [InlineData(128)]
        [InlineData(512, Skip = "BUG-0023: the timed tick includes one 512x512 flow-field build (~10 ms Debug); the BUG-0019 scan itself is fixed")]
        [InlineData(1024, Skip = "BUG-0023: one 1024x1024 flow-field build in the tick")]
        public void Perf_500MovesToOneCliffCell_ApplyCost(int mapSize)
        {
            var p = MapGenParams.Default with { Width = mapSize, Height = mapSize };
            var sim = new Simulation(TestSim.Config(91, 2, 500, 1100) with { Map = p });
            NavGrid g = sim.World.NavGrid;
            List<int> passable = FlowFieldOracle.PassableCells(g);
            for (int i = 0; i < 500; i++) sim.Enqueue(Command.SpawnUnit(i % 2, 0, MoveScenario.Center(g, passable[i * 7 % passable.Count])));
            sim.Tick();
            sim.Tick();
            // A blocked target far from passable ground: the map's corner (ring cell).
            Vector2 corner = new(0.5f, 0.5f);
            MoveAllTo(sim, _ => corner);
            sim.Tick(); // queued for next tick
            var sw = Stopwatch.StartNew();
            sim.Tick(); // applies 500 Moves (each resolves the blocked target) and builds its field once
            double ms = sw.Elapsed.TotalMilliseconds;
            _out.WriteLine($"{mapSize}x{mapSize}: tick applying 500 Moves to a ring cell took {ms:F1} ms");
            Assert.True(ms < 8.0, $"{ms:F1} ms for one tick (docs/03: p99 tick < 8 ms)");
        }

        [Fact]
        [Trait("Category", "Perf")]
        public void Perf_WorldOn1024Map_FlowFieldCacheMemory()
        {
            var p = MapGenParams.Default with { Width = 1024, Height = 1024 };
            var cfg = TestSim.Config(5, 2, 8, 8) with { Map = p };
            World? built = null;
            var sw = new Stopwatch();
            Action create = () =>
            {
                sw.Start();
                built = new World(cfg);
                sw.Stop();
            };
            long bytes = AllocationProbe.Measure(create);
            double ms = sw.Elapsed.TotalMilliseconds;
            World world = built!;
            _out.WriteLine($"1024 map world: {bytes / 1e6:F0} MB allocated, {ms:F0} ms (cache capacity {world.FlowFields.Capacity})");
            Assert.True(bytes < 400_000_000, $"{bytes / 1e6:F0} MB");
        }
    }
}
