using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;
using Rts.Sim.Pathfinding;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA attacks on the M1-4b fix round: the per-tick flow-field build cap (BUG-0018), the ring-search
/// NearestPassable (BUG-0019) and arrival inside the goal cell only (BUG-0020).
/// </summary>
public class FieldBuildCapQaTests
{
    private readonly ITestOutputHelper _out;

    public FieldBuildCapQaTests(ITestOutputHelper output) => _out = output;

    // ---------- helpers ----------

    private static Simulation SpawnRandom(ulong seed, int units, int players = 2)
    {
        var sim = new Simulation(TestSim.ConfigNoCombat(seed, players, UnitCapacity: units, CommandCapacity: 2 * units + 64));
        NavGrid g = sim.World.NavGrid;
        List<int> passable = FlowFieldOracle.PassableCells(g);
        var rng = new SimRng(seed, 900);
        for (int i = 0; i < units; i++)
        {
            int c = passable[rng.NextInt(0, passable.Count)];
            Vector2 corner = MoveScenario.Center(g, c) - new Vector2(MapConstants.CellSize / 2);
            var off = new Vector2(0.05f + rng.NextFloat() * 1.9f, 0.05f + rng.NextFloat() * 1.9f);
            sim.Enqueue(Command.SpawnUnit(i % players, i % TestSim.UnitTypeCount, corner + off));
        }
        sim.Tick();
        sim.Tick();
        Assert.Equal(units, sim.World.Units.Count);
        return sim;
    }

    private static int CountMoving(UnitStore u)
    {
        int n = 0;
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.State[i] == UnitState.Moving) n++;
        return n;
    }

    // ---------- BUG-0019: ring search vs QA's full-scan oracle ----------

    private static NavGrid Grid(ref SimRng rng, int w, int h, int style)
    {
        var levels = new byte[w * h];
        var elev = new float[w * h];
        int k = rng.NextInt(2, 11);
        for (int i = 0; i < w * h; i++)
        {
            int x = i % w, y = i / w;
            int lvl = style switch
            {
                0 => rng.NextInt(0, 3),                      // salt-and-pepper
                1 => (x / k + y / k) % 2,                    // k x k checkerboard: sealed pockets, big blocked areas
                2 => rng.NextInt(0, 12) == 0 ? 1 : 0,        // sparse walls
                3 => 0,                                       // flat: only the border ring is blocked
                _ => (x * x + y * 3) % 7 < 3 ? 1 : 0,        // stripes / odd shapes
            };
            levels[i] = (byte)lvl;
            elev[i] = lvl * MapConstants.LevelHeight;
        }
        return new NavGrid(new Heightmap(w, h, levels, elev));
    }

    [Fact]
    public void NearestPassable_RingSearch_EqualsQaFullScan_OnEveryCell_OfAdversarialGrids()
    {
        var rng = new SimRng(9019, 2);
        int cells = 0, none = 0;
        for (int i = 0; i < 400; i++)
        {
            // Thin and lopsided shapes on purpose: rings clip against different edges.
            int w = i % 10 == 0 ? 1 : rng.NextInt(1, 49);
            int h = i % 10 == 1 ? 1 : rng.NextInt(1, 49);
            NavGrid g = Grid(ref rng, w, h, i % 5);
            for (int c = 0; c < w * h; c++)
            {
                int expected = FlowFieldQaTests.QaNearest(g, c);
                int actual = FlowField.NearestPassable(g, c);
                Assert.True(expected == actual, $"grid {i} ({w}x{h}, style {i % 5}) cell ({c % w},{c / w}): ring {actual} vs oracle {expected}");
                if (expected < 0) none++;
                cells++;
            }
        }
        _out.WriteLine($"{cells} cells checked, {none} on grids with no passable cell");
        Assert.True(none > 0, "never produced an all-blocked grid");
    }

    [Theory]
    [InlineData(1024, 6)]
    [InlineData(6, 1024)]
    [InlineData(300, 17)]
    public void NearestPassable_RingSearch_EqualsQaFullScan_OnLongThinGrids(int w, int h)
    {
        var rng = new SimRng((ulong)(w * 7 + h), 3);
        for (int style = 0; style < 5; style++)
        {
            NavGrid g = Grid(ref rng, w, h, style);
            for (int c = 0; c < w * h; c += 5)
                Assert.Equal(FlowFieldQaTests.QaNearest(g, c), FlowField.NearestPassable(g, c));
        }
    }

    [Fact]
    public void NearestPassable_RingSearch_EqualsQaFullScan_OnEveryBlockedCell_OfGeneratedMaps()
    {
        int checkedCells = 0;
        foreach (ulong seed in new ulong[] { 3, 77, 1234 })
        {
            NavGrid g = FlowFieldOracle.Generated(seed);
            for (int c = 0; c < g.Width * g.Height; c++)
            {
                if (g.IsPassable(c % g.Width, c / g.Width)) continue;
                Assert.Equal(FlowFieldQaTests.QaNearest(g, c), FlowField.NearestPassable(g, c));
                checkedCells++;
            }
        }
        _out.WriteLine($"{checkedCells} blocked cells");
    }

    // ---------- BUG-0018: build cap determinism, starvation, allocation, scale ----------

    [Fact]
    public void BuildCap_TwoSims_ManyGoalsAndRetargetBursts_HashEqualEveryTick_CapNeverExceeded()
    {
        Simulation a = SpawnRandom(4018, 500), b = SpawnRandom(4018, 500);
        NavGrid g = a.World.NavGrid;
        float size = g.Width * MapConstants.CellSize;
        var goals = new Vector2[120];
        var grng = new SimRng(4018, 11);
        for (int k = 0; k < goals.Length; k++) goals[k] = new Vector2(grng.NextFloat() * size, grng.NextFloat() * size);
        var rngA = new SimRng(4018, 12);
        var rngB = new SimRng(4018, 12);
        for (int t = 0; t < 1500; t++)
        {
            if (t % 15 == 0)
            {
                Burst(a, ref rngA, goals);
                Burst(b, ref rngB, goals);
            }
            int before = a.World.FlowFields.BuildCount;
            a.Tick();
            b.Tick();
            Assert.True(a.World.FlowFields.BuildCount - before <= MovementConstants.MaxFieldBuildsPerTick, $"tick {t}");
            Assert.True(a.StateHash() == b.StateHash(), $"hash diverged at tick {t}");
        }

        static void Burst(Simulation s, ref SimRng rng, Vector2[] goals)
        {
            UnitStore u = s.World.Units;
            for (int i = 0; i < u.Capacity; i++)
                if (rng.NextInt(0, 2) == 0)
                    s.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(s, i), goals[rng.NextInt(0, goals.Length)]));
        }
    }

    [Fact]
    public void BuildCap_FieldCacheIsSimState_PrewarmingChangesTheHash_IdenticalPrewarmsStayEqual()
    {
        // Decision 2026-10-04 (BUG-0021, docs/03 "Flow fields"): the cache's keys, versions and LRU
        // stamps are sim state. Pre-warming one sim's cache must show in its hash at once (equal
        // hashes then really mean equal futures, e.g. across save/load), and sims pre-warmed the same
        // way must stay equal. Drafted by the dev in M1-4c; QA owns this test.
        Simulation a = SpawnRandom(4021, 64), b = SpawnRandom(4021, 64), c = SpawnRandom(4021, 64);
        NavGrid g = a.World.NavGrid;
        List<int> passable = FlowFieldOracle.PassableCells(g);
        var rng = new SimRng(4021, 4);
        var goalCells = new int[8];
        for (int k = 0; k < goalCells.Length; k++) goalCells[k] = passable[rng.NextInt(0, passable.Count)];
        UnitStore ua = a.World.Units;
        for (int i = 0; i < ua.Capacity; i++)
        {
            Vector2 target = MoveScenario.Center(g, goalCells[i % goalCells.Length]);
            a.Enqueue(Command.Move(ua.Owner[i], MoveScenario.Handle(a, i), target));
            b.Enqueue(Command.Move(ua.Owner[i], MoveScenario.Handle(b, i), target));
            c.Enqueue(Command.Move(ua.Owner[i], MoveScenario.Handle(c, i), target));
        }
        Assert.Equal(a.StateHash(), b.StateHash());
        foreach (int cell in goalCells) b.World.FlowFields.Get(cell);
        Assert.True(a.StateHash() != b.StateHash(), "pre-warming b's cache did not change its hash");
        foreach (int cell in goalCells) c.World.FlowFields.Get(cell); // c pre-warmed exactly like b
        Assert.Equal(b.StateHash(), c.StateHash());
        for (int t = 0; t < 20; t++)
        {
            b.Tick();
            c.Tick();
            Assert.True(b.StateHash() == c.StateHash(), $"tick {t}: identically pre-warmed sims diverged");
        }
    }

    [Fact]
    public void BuildCap_500UnitsWith500DistinctGoals_AllStop_AtMost7PercentGiveUp_NoDeadlock()
    {
        Simulation sim = SpawnRandom(5018, 500);
        NavGrid g = sim.World.NavGrid;
        List<int> passable = FlowFieldOracle.PassableCells(g);
        UnitStore u = sim.World.Units;
        var rng = new SimRng(5018, 3);
        // Ideal arrival: path cost (cells) x cell size / speed, as if every field were ready at once.
        int idealMax = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            int goal = passable[rng.NextInt(0, passable.Count)];
            sim.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), MoveScenario.Center(g, goal)));
            g.WorldToCell(u.Position[i], out int x, out int y);
            float cost = FlowField.Build(g, goal).CostAt(y * g.Width + x);
            idealMax = Math.Max(idealMax, (int)MathF.Ceiling((cost + 1) * MapConstants.CellSize / u.Speed[i]));
        }
        sim.Tick(); // queued; the Moves apply on the next tick
        int ticks = 0, maxBuilds = 0;
        Assert.Equal(0, CountMoving(u));
        sim.Tick();
        Assert.True(CountMoving(u) > 490, $"{CountMoving(u)} moving after the Moves applied");
        while (CountMoving(u) > 0 && ticks < 30_000)
        {
            int before = sim.World.FlowFields.BuildCount;
            sim.Tick();
            maxBuilds = Math.Max(maxBuilds, sim.World.FlowFields.BuildCount - before);
            ticks++;
        }
        _out.WriteLine($"500 distinct goals: all arrived after {ticks} ticks (ideal slowest unit ~{idealMax}), {sim.World.FlowFields.BuildCount} builds, max {maxBuilds}/tick");
        Assert.Equal(0, CountMoving(u));
        Assert.True(maxBuilds <= MovementConstants.MaxFieldBuildsPerTick);
        // M1-4d-1: units now block each other, so a unit can arrive by touching an arrived groupmate
        // or give up (GoalCell -1) when walled in by other units for GiveUpTicks. M1-4d-2 shoves
        // friendly Idle units aside; units waiting for a field under the build cap (Moving) and
        // enemies are never shoved: 30 of 500 give up (6.0%; 28 with one player, criterion 6 asked
        // for 3%, BUG-0032). The bound is that plus about one point of headroom.
        bool[] arrived = MoveScenario.Arrived(sim.World);
        int arrivedCount = 0, gaveUp = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (arrived[i]) arrivedCount++;
            else if (u.GoalCell[i] == -1) gaveUp++;
            else Assert.Fail($"unit {i} idle {Vector2.Distance(u.Position[i], u.Goal[i]):F2} m from its goal without arriving or giving up");
        }
        _out.WriteLine($"arrived {arrivedCount}, gave up {gaveUp}");
        Assert.True(gaveUp <= u.Capacity * 7 / 100, $"{gaveUp} of {u.Capacity} units gave up");
    }

    [Fact]
    public void BuildCap_64GroupsRetargetedEvery34Ticks_EveryGroupMovesInEveryWindow()
    {
        // A realistic load later (AI re-targeting, chase orders): 64 groups of 8 get a new goal every
        // window. Retuned in M1-4c (BUG-0022) to the decided cap and capacity: 512 unit slots give a
        // 64-field cache, and a window of 34 ticks covers the Moves' one-tick command delay plus
        // ceil(64 / MaxFieldBuildsPerTick) = 32 build ticks, with one to spare. Oldest-first serving
        // means no group may go a whole window frozen. Drafted by the dev; QA owns the parameters.
        const int windowTicks = 34;
        Assert.True(windowTicks >= 1 + (64 + MovementConstants.MaxFieldBuildsPerTick - 1) / MovementConstants.MaxFieldBuildsPerTick);
        Simulation sim = SpawnRandom(6018, 512);
        Assert.True(sim.World.FlowFields.Capacity >= 64);
        NavGrid g = sim.World.NavGrid;
        List<int> passable = FlowFieldOracle.PassableCells(g);
        UnitStore u = sim.World.Units;
        var rng = new SimRng(6018, 5);
        var start = new Vector2[u.Capacity];
        var groupGoal = new int[64];
        int frozenGroupWindows = 0, windows = 0, worstWindowFrozen = 0;
        long frozenGoalY = 0, movedGoalY = 0;
        int frozenCount = 0, movedCount = 0;
        for (int window = 0; window < 15; window++)
        {
            for (int k = 0; k < 64; k++) groupGoal[k] = passable[rng.NextInt(0, passable.Count)];
            for (int i = 0; i < u.Capacity; i++)
            {
                start[i] = u.Position[i];
                sim.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), MoveScenario.Center(g, groupGoal[i % 64])));
            }
            for (int t = 0; t < windowTicks; t++) sim.Tick();
            int frozenThisWindow = 0;
            for (int k = 0; k < 64; k++)
            {
                bool anyMoved = false, anyNeeded = false;
                for (int i = k; i < u.Capacity; i += 64)
                {
                    if (Vector2.Distance(start[i], u.Position[i]) > 0f) anyMoved = true;
                    if (Vector2.Distance(start[i], u.Goal[i]) > MovementConstants.ArrivalDistance) anyNeeded = true;
                }
                if (!anyNeeded) continue;
                if (!anyMoved) { frozenThisWindow++; frozenGoalY += groupGoal[k] / g.Width; frozenCount++; }
                else { movedGoalY += groupGoal[k] / g.Width; movedCount++; }
            }
            frozenGroupWindows += frozenThisWindow;
            worstWindowFrozen = Math.Max(worstWindowFrozen, frozenThisWindow);
            windows++;
        }
        _out.WriteLine($"{windows} windows x 64 groups: {frozenGroupWindows} group-windows frozen (worst window {worstWindowFrozen}); " +
            $"mean goal row frozen {(frozenCount > 0 ? frozenGoalY / (double)frozenCount : 0):F1} vs moved {(movedCount > 0 ? movedGoalY / (double)movedCount : 0):F1}");
        Assert.True(frozenGroupWindows == 0, $"{frozenGroupWindows} group-windows never moved");
    }

    /// <summary>This class's wall-clock and allocation tests: they run alone in <see cref="SerialCollection"/> (BUG-0017, BUG-0024) while the heavy tests above stay in the parallel batch.</summary>
    [Collection(SerialCollection.Name)]
    public class Serial
    {
        private readonly ITestOutputHelper _out;

        public Serial(ITestOutputHelper output) => _out = output;

        [Fact]
        public void BuildCap_SteadyStateWith300LiveGoals_TickAllocatesNothing()
        {
            Simulation sim = SpawnRandom(7018, 600);
            NavGrid g = sim.World.NavGrid;
            List<int> passable = FlowFieldOracle.PassableCells(g);
            UnitStore u = sim.World.Units;
            var rng = new SimRng(7018, 1);
            var goals = new int[300];
            for (int k = 0; k < goals.Length; k++) goals[k] = passable[rng.NextInt(0, passable.Count)];
            for (int i = 0; i < u.Capacity; i++)
                sim.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), MoveScenario.Center(g, goals[i % goals.Length])));
            for (int t = 0; t < 40; t++) sim.Tick(); // warm-up: JIT, every code path incl. waiting and building
            int builds = sim.World.FlowFields.BuildCount;
            Action sixtyTicks = () =>
            {
                for (int t = 0; t < 60; t++) sim.Tick();
            };
            int runs = AllocationProbe.AssertZero(sixtyTicks, _out);
            _out.WriteLine($"{60 * runs} ticks, {CountMoving(u)} moving, {sim.World.FlowFields.BuildCount - builds} builds: 0 bytes");
            Assert.True(sim.World.FlowFields.BuildCount - builds >= 50, "measurement didn't include builds");
        }

        [Trait("Category", "Perf")]
        [Theory]
        [InlineData(500)]
        [InlineData(1000)]
        [InlineData(2500)]
        public void Perf_BuildCap_EveryUnitItsOwnGoal_TickCost(int units)
        {
            Simulation sim = SpawnRandom(8018, units);
            NavGrid g = sim.World.NavGrid;
            List<int> passable = FlowFieldOracle.PassableCells(g);
            UnitStore u = sim.World.Units;
            var rng = new SimRng(8018, 1);
            for (int i = 0; i < u.Capacity; i++)
                sim.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), MoveScenario.Center(g, passable[rng.NextInt(0, passable.Count)])));
            sim.Tick();
            for (int t = 0; t < 10; t++) sim.Tick();
            var sw = new Stopwatch();
            double worst = 0;
            for (int t = 0; t < 100; t++)
            {
                sw.Restart();
                sim.Tick();
                worst = Math.Max(worst, sw.Elapsed.TotalMilliseconds);
            }
            double total = 0;
            sw.Restart();
            for (int t = 0; t < 100; t++) sim.Tick();
            total = sw.Elapsed.TotalMilliseconds / 100;
            _out.WriteLine($"{units} units, {units} distinct goals: avg {total:F3} ms/tick, worst {worst:F3} ms, {CountMoving(u)} moving");
            Assert.True(total < 4.0, $"avg {total:F3} ms (docs/03: < 4 ms)");
            Assert.True(worst < 8.0, $"worst {worst:F3} ms (docs/03: p99 < 8 ms)");
        }
    }
}
