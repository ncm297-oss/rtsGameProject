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
/// QA attacks on M1-4c's build pass (BUG-0022): oldest order first, two builds per tick, fairness
/// between players, starvation and eviction churn when live goals exceed the cache, and the tick's
/// allocation, cost and memory with the bigger cache.
/// </summary>
public class FieldBuildFairnessQaTests
{
    private readonly ITestOutputHelper _out;

    public FieldBuildFairnessQaTests(ITestOutputHelper output) => _out = output;

    // ---------- helpers ----------

    internal static List<int> CellsInRows(NavGrid g, int y0, int y1)
    {
        var list = new List<int>();
        for (int y = Math.Max(1, y0); y <= Math.Min(g.Height - 2, y1); y++)
            for (int x = 1; x < g.Width - 1; x++)
                if (g.IsPassable(x, y)) list.Add(y * g.Width + x);
        Assert.True(list.Count > 50, $"rows {y0}-{y1}: only {list.Count} passable cells");
        return list;
    }

    /// <summary>Units alternate owners 0/1; player 0 spawns in rows around <paramref name="rowP0"/>, player 1 around <paramref name="rowP1"/>.</summary>
    internal static Simulation SpawnRows(ulong seed, int unitCapacity, int units, int rowP0, int rowP1, out List<int>[] bases)
    {
        var sim = new Simulation(TestSim.ConfigNoCombat(seed, 2, UnitCapacity: unitCapacity, CommandCapacity: 4 * unitCapacity + 64));
        NavGrid g = sim.World.NavGrid;
        bases = new[] { CellsInRows(g, rowP0 - 4, rowP0 + 4), CellsInRows(g, rowP1 - 4, rowP1 + 4) };
        var rng = new SimRng(seed, 950);
        for (int i = 0; i < units; i++)
        {
            List<int> cells = bases[i % 2];
            sim.Enqueue(Command.SpawnUnit(i % 2, i % TestSim.UnitTypeCount, MoveScenario.Center(g, cells[rng.NextInt(0, cells.Count)])));
        }
        sim.Tick();
        sim.Tick();
        Assert.Equal(units, sim.World.Units.Count);
        return sim;
    }

    internal static Simulation SpawnRandom(ulong seed, int unitCapacity, int units)
    {
        var sim = new Simulation(TestSim.Config(seed, 2, UnitCapacity: unitCapacity, CommandCapacity: 2 * unitCapacity + 64));
        NavGrid g = sim.World.NavGrid;
        List<int> open = FlowFieldOracle.PassableCells(g);
        var rng = new SimRng(seed, 951);
        for (int i = 0; i < units; i++)
            sim.Enqueue(Command.SpawnUnit(i % 2, i % TestSim.UnitTypeCount, MoveScenario.Center(g, open[rng.NextInt(0, open.Count)])));
        sim.Tick();
        sim.Tick();
        Assert.Equal(units, sim.World.Units.Count);
        return sim;
    }

    private sealed class Order
    {
        public int Group, Player, ApplyTick, GoalCell, Backlog;
        public int Served = -1;
        public bool CachedAtApply, SharedGoal;
        public int Wait => Served - ApplyTick;
    }

    // ---------- fairness: two bases, capacity not exhausted ----------

    /// <summary>
    /// 64 groups of 4 (32 per player, bases at rows ~10 and ~118) with 512 unit slots, so the cache
    /// holds 64 fields and is never exhausted. Each 100-tick cycle the players re-order
    /// <paramref name="batch"/> groups per turn toward the other base, on alternating ticks (the
    /// player who goes first alternates by cycle) or both on the same tick. Every order must be
    /// served within ceil(B / 2) - 1 ticks of applying, where B counts the not-yet-served orders with
    /// a key (apply tick, goal cell) at most its own; no order may be served after a newer one;
    /// with alternating ticks the players' mean waits match within 1 tick.
    /// </summary>
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(4, false)]
    [InlineData(8, false)]
    [InlineData(4, true)]
    [InlineData(8, true)]
    public void Fairness_TwoBases_2000Ticks_OldestFirst_BoundedWait_PlayersEqual(int batch, bool simultaneous)
    {
        const int groupsPerPlayer = 32, unitsPerGroup = 4, ticks = 2000, cycle = 100;
        Simulation sim = SpawnRows(TestSeeds.PreMix(4600 + (ulong)batch), 512, 2 * groupsPerPlayer * unitsPerGroup, 10, 118, out List<int>[] bases); // pre-M1-6 maps
        World w = sim.World;
        NavGrid g = w.NavGrid;
        UnitStore u = w.Units;
        FlowFieldCache cache = w.FlowFields;
        Assert.Equal(64, cache.Capacity);
        // Spawns apply in (player, sequence) order, so group by owner, round-robin within a player.
        var members = new List<int>[2 * groupsPerPlayer];
        for (int k = 0; k < members.Length; k++) members[k] = new List<int>();
        var seen = new int[2];
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i]) members[u.Owner[i] * groupsPerPlayer + seen[u.Owner[i]]++ % groupsPerPlayer].Add(i);
        Assert.All(members, m => Assert.Equal(unitsPerGroup, m.Count));

        var rng = new SimRng(TestSeeds.PreMix(4600), (ulong)(batch * 2 + (simultaneous ? 1 : 0)));
        var next = new int[2];
        var orders = new List<Order>();
        var pending = new Order?[members.Length];
        var before = new Vector2[u.Capacity];
        var issued = new List<(int Group, int Cell, bool Cached)>();
        int issuedFor = -1;
        int turnsPerPlayer = groupsPerPlayer / batch;
        for (int t = 0; t < ticks; t++)
        {
            int T = sim.TickNumber;

            // Orders queued now apply on tick T + 1.
            int a = T + 1, phase = a % cycle, cycleIndex = a / cycle;
            var players = new List<int>();
            if (simultaneous) { if (phase < turnsPerPlayer) { players.Add(0); players.Add(1); } }
            else if (phase < 2 * turnsPerPlayer) players.Add((phase + cycleIndex) % 2);
            var newIssued = new List<(int, int, bool)>();
            foreach (int p in players)
            {
                for (int b = 0; b < batch; b++)
                {
                    int k = p * groupsPerPlayer + next[p]++ % groupsPerPlayer;
                    List<int> dest = bases[1 - p];
                    int cell = dest[rng.NextInt(0, dest.Count)];
                    foreach (int i in members[k]) sim.Enqueue(Command.Move(p, MoveScenario.Handle(sim, i), MoveScenario.Center(g, cell)));
                    newIssued.Add((k, cell, false));
                }
            }

            // The orders queued last iteration apply in this tick T: was their field cached going in?
            if (issuedFor == T)
                for (int j = 0; j < issued.Count; j++) issued[j] = (issued[j].Group, issued[j].Cell, cache.Contains(issued[j].Cell));

            Array.Copy(u.Position, before, before.Length);
            sim.Tick();

            if (issuedFor == T)
            {
                var applied = new List<Order>();
                foreach ((int k, int cell, bool cached) in issued)
                {
                    Order? prev = pending[k];
                    Assert.True(prev == null || prev.Served >= 0, $"group {k} re-ordered on tick {T} before its order from tick {prev?.ApplyTick} was served");
                    Assert.Equal(cell, u.GoalCell[members[k][0]]);
                    // A goal cell shared with another walking group takes that group's (older) age:
                    // the documented group rule, so it isn't counted as overtaking.
                    bool shared = false;
                    for (int i = 0; i < u.Capacity; i++)
                        if (u.Alive[i] && u.State[i] == UnitState.Moving && u.GoalCell[i] == cell && !members[k].Contains(i)) shared = true;
                    var o = new Order { Group = k, Player = k / groupsPerPlayer, ApplyTick = T, GoalCell = cell, CachedAtApply = cached, SharedGoal = shared };
                    applied.Add(o);
                }
                foreach (Order o in applied)
                {
                    if (o.CachedAtApply) continue;
                    int backlog = 0;
                    foreach (Order? q in pending)
                        if (q != null && q.Served < 0 && !q.CachedAtApply) backlog++;
                    foreach (Order q in applied)
                        if (!q.CachedAtApply && q.GoalCell <= o.GoalCell) backlog++;
                    o.Backlog = backlog;
                }
                foreach (Order o in applied)
                {
                    pending[o.Group] = o;
                    orders.Add(o);
                }
            }
            for (int k = 0; k < members.Length; k++)
            {
                Order? o = pending[k];
                if (o == null || o.Served >= 0) continue;
                foreach (int i in members[k])
                {
                    if (u.Position[i] != before[i]) { o.Served = T; break; }
                }
            }
            issued.Clear();
            foreach ((int, int, bool) x in newIssued) issued.Add(x);
            issuedFor = a;
        }

        List<Order> done = orders.Where(o => o.Served >= 0).ToList();
        Assert.True(done.Count >= orders.Count - 2 * groupsPerPlayer, $"only {done.Count} of {orders.Count} orders served");
        var mean = new double[2];
        var worst = new int[2];
        for (int p = 0; p < 2; p++)
        {
            List<Order> mine = done.Where(o => o.Player == p).ToList();
            mean[p] = mine.Average(o => o.Wait);
            worst[p] = mine.Max(o => o.Wait);
        }
        int overtakes = 0, overBound = 0, maxBacklog = 0;
        string firstBad = "", firstOvertake = "";
        List<Order> built = done.Where(o => !o.CachedAtApply).ToList();
        foreach (Order x in built)
        {
            maxBacklog = Math.Max(maxBacklog, x.Backlog);
            int bound = (x.Backlog + 1) / 2 - 1;
            if (x.Wait > bound)
            {
                overBound++;
                if (firstBad == "") firstBad = $"group {x.Group} applied {x.ApplyTick} waited {x.Wait} > {bound} (backlog {x.Backlog})";
            }
            foreach (Order y in built)
            {
                if (x.ApplyTick < y.ApplyTick && x.GoalCell != y.GoalCell && !y.SharedGoal && y.Served < x.Served)
                {
                    if (overtakes++ == 0)
                        firstOvertake = $"group {y.Group} (applied {y.ApplyTick}, cell {y.GoalCell}) served {y.Served} before group {x.Group} (applied {x.ApplyTick}, cell {x.GoalCell}, shared {x.SharedGoal}) served {x.Served}";
                }
            }
        }
        foreach (Order x in done.Where(o => o.CachedAtApply)) Assert.True(x.Wait == 0, $"group {x.Group}: cached goal but waited {x.Wait}");
        _out.WriteLine($"batch {batch}, {(simultaneous ? "same tick" : "alternating")}: {orders.Count} orders ({done.Count(o => o.CachedAtApply)} hit a cached goal), " +
                       $"max backlog {maxBacklog}; mean wait P0 {mean[0]:F2} / P1 {mean[1]:F2} ticks, worst {worst[0]} / {worst[1]}; " +
                       $"over bound {overBound}, overtakes {overtakes}");
        Assert.True(overBound == 0, $"{overBound} orders waited longer than ceil(B/2)-1; first: {firstBad}");
        Assert.True(overtakes == 0, $"{overtakes} overtakes; first: {firstOvertake}");
        if (!simultaneous)
            Assert.True(Math.Abs(mean[0] - mean[1]) <= 1.0, $"players' mean waits differ: {mean[0]:F2} vs {mean[1]:F2}");
    }

    // ---------- starvation and churn with more live goals than cache slots ----------

    /// <summary>
    /// QA focus probe: 300 groups (512 units, i % 300) get new random goals every 40 ticks on a
    /// 64-field cache, 15 windows. Records frozen group-windows and how many builds went to fields
    /// that were already built (evicted, then rebuilt) in the same window. M1-4b's comparable
    /// probe (64 groups, 20-tick windows, cap 1, 32 slots): 483 of ~960 group-windows frozen.
    /// </summary>
    [Fact]
    public void Starvation_300GroupsCyclingEvery40Ticks_Cache64_MeasuresFrozenAndChurn()
    {
        const int groups = 300, window = 40, windows = 15;
        Simulation sim = SpawnRandom(4700, 512, 512);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        UnitStore u = w.Units;
        FlowFieldCache cache = w.FlowFields;
        Assert.Equal(64, cache.Capacity);
        List<int> open = FlowFieldOracle.PassableCells(g);
        var rng = new SimRng(4700, 1);
        var goal = new int[groups];
        var start = new Vector2[u.Capacity];
        long frozen = 0, builds = 0, firstBuilds = 0, maxFrozenWindow = 0;
        for (int win = 0; win < windows; win++)
        {
            for (int k = 0; k < groups; k++) goal[k] = open[rng.NextInt(0, open.Count)];
            for (int i = 0; i < u.Capacity; i++)
                sim.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), MoveScenario.Center(g, goal[i % groups])));
            // The Moves apply one tick later; until then units still walk to last window's goals,
            // so the window (and the frozen check) starts once they have applied.
            sim.Tick();
            Array.Copy(u.Position, start, start.Length);
            int b0 = cache.BuildCount;
            var everCached = new HashSet<int>();
            for (int t = 0; t < window; t++)
            {
                int bt = cache.BuildCount;
                sim.Tick();
                Assert.True(cache.BuildCount - bt <= MovementConstants.MaxFieldBuildsPerTick);
                for (int k = 0; k < groups; k++) if (cache.Contains(goal[k])) everCached.Add(goal[k]);
            }
            int frozenNow = 0;
            for (int k = 0; k < groups; k++)
            {
                bool moved = false;
                for (int i = k; i < u.Capacity; i += groups) moved |= u.Position[i] != start[i];
                if (!moved) frozenNow++;
            }
            frozen += frozenNow;
            maxFrozenWindow = Math.Max(maxFrozenWindow, frozenNow);
            builds += cache.BuildCount - b0;
            firstBuilds += everCached.Count;
        }
        long churn = builds - firstBuilds;
        _out.WriteLine($"{groups} groups x {windows} windows of {window} ticks: frozen {frozen} of {groups * windows} group-windows " +
                       $"({100.0 * frozen / (groups * windows):F0}%), worst window {maxFrozenWindow}; builds {builds}, " +
                       $"distinct goals served {firstBuilds}, rebuilds after eviction {churn} ({100.0 * churn / Math.Max(1, builds):F0}% of builds)");
        // Two builds a tick can serve at most 80 goals a window, so most of 300 must freeze; the
        // check is that builds go to new goals, not to re-serving evicted ones.
        Assert.True(churn * 2 <= builds, $"{churn} of {builds} builds rebuilt an evicted field");
    }

    /// <summary>
    /// 32 groups ordered on one tick fill the 32-field cache; two more groups are ordered later.
    /// docs/03 "Build cap": "an order never waits behind a newer one". Measures, over 100 ticks, how
    /// often an older group stands still for lack of a field while the two newer groups walk, and how
    /// many builds the cache spends re-serving fields it evicted.
    /// </summary>
    [Fact(Skip = "BUG-0025: with live goals > cache slots the build pass evicts the lowest goal cell touched this tick, not the newest order; older groups wait while newer ones walk, and every build is churn")]
    public void LiveGoalsOneOverCapacity_OlderOrdersNeverWaitWhileNewerWalk_NoEndlessChurn()
    {
        (int olderWaits, int newerWaits, int builds) = RunOneOverCapacity();
        Assert.True(olderWaits == 0, $"older groups stood still {olderWaits} group-ticks while the newer groups walked");
        Assert.True(builds <= 4, $"{builds} builds in 100 ticks with 34 fixed goals on a 32-field cache (newer groups waited {newerWaits})");
    }

    [Fact]
    public void LiveGoalsOneOverCapacity_Measure()
    {
        (int olderWaits, int newerWaits, int builds) = RunOneOverCapacity();
        _out.WriteLine($"34 live goals on 32 slots, 100 ticks: {builds} builds, older groups waited {olderWaits} group-ticks, newer {newerWaits}");
        Assert.True(builds <= 2 * 100);
    }

    private static (int OlderWaits, int NewerWaits, int Builds) RunOneOverCapacity()
    {
        // 72 unit slots: a 32-field cache. 34 units of player 0 at row ~10, each its own goal at row ~118.
        Simulation sim = SpawnRows(4800, 72, 68, 10, 118, out List<int>[] bases);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        UnitStore u = w.Units;
        FlowFieldCache cache = w.FlowFields;
        Assert.Equal(32, cache.Capacity);
        var mine = new List<int>();
        for (int i = 0; i < u.Capacity && mine.Count < 34; i++) if (u.Alive[i] && u.Owner[i] == 0) mine.Add(i);
        List<int> far = bases[1];
        var goals = new int[34];
        for (int k = 0; k < 34; k++) goals[k] = far[k * (far.Count / 34)];
        for (int k = 0; k < 32; k++) sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, mine[k]), MoveScenario.Center(g, goals[k])));
        for (int t = 0; t < 30; t++) sim.Tick(); // 16 build ticks fill the cache
        for (int k = 32; k < 34; k++) sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, mine[k]), MoveScenario.Center(g, goals[k])));
        sim.Tick();
        sim.Tick(); // the two newer orders applied and built
        var before = new Vector2[u.Capacity];
        int olderWaits = 0, newerWaits = 0;
        int b0 = cache.BuildCount;
        for (int t = 0; t < 100; t++)
        {
            Array.Copy(u.Position, before, before.Length);
            sim.Tick();
            bool newerWalked = false;
            for (int k = 32; k < 34; k++)
            {
                int i = mine[k];
                Assert.Equal(UnitState.Moving, u.State[i]);
                if (u.Position[i] != before[i]) newerWalked = true; else newerWaits++;
            }
            for (int k = 0; k < 32; k++)
            {
                int i = mine[k];
                Assert.Equal(UnitState.Moving, u.State[i]);
                if (newerWalked && u.Position[i] == before[i] && u.Velocity[i] == Vector2.Zero) olderWaits++;
            }
        }
        return (olderWaits, newerWaits, cache.BuildCount - b0);
    }

    /// <summary>This class's wall-clock and allocation tests: they run alone in <see cref="SerialCollection"/>.</summary>
    [Collection(SerialCollection.Name)]
    public class Serial
    {
        private readonly ITestOutputHelper _out;

        public Serial(ITestOutputHelper output) => _out = output;

        private static Simulation ConstantTwoBuilds(int units, int goals, out FlowFieldCache cache)
        {
            Simulation sim = SpawnRandom(4900, units, units);
            cache = sim.World.FlowFields;
            NavGrid g = sim.World.NavGrid;
            List<int> open = FlowFieldOracle.PassableCells(g);
            var rng = new SimRng(4900, 2);
            var cells = new int[goals];
            for (int k = 0; k < goals; k++) cells[k] = open[rng.NextInt(0, open.Count)];
            UnitStore u = sim.World.Units;
            for (int i = 0; i < u.Capacity; i++)
                sim.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), MoveScenario.Center(g, cells[i % goals])));
            for (int t = 0; t < 80; t++) sim.Tick(); // fill the cache; from here on, evictions keep 2 builds a tick going
            return sim;
        }

        [Fact]
        public void Tick_1024Units_128SlotCache_TwoBuildsEveryTick_AllocatesNothing()
        {
            Simulation sim = ConstantTwoBuilds(1024, 300, out FlowFieldCache cache);
            Assert.Equal(128, cache.Capacity);
            int b0 = cache.BuildCount;
            Action ticks = () => { for (int t = 0; t < 50; t++) sim.Tick(); };
            int runs = AllocationProbe.AssertZero(ticks, _out);
            int builds = cache.BuildCount - b0;
            _out.WriteLine($"{50 * runs} ticks, {builds} builds, 0 bytes");
            Assert.True(builds >= 2 * 50 * runs - 2, $"only {builds} builds: the probe didn't keep the cap busy");
        }

        [Trait("Category", "Perf")]
        [Theory]
        [InlineData(500, 300)]
        [InlineData(1024, 300)]
        [InlineData(2500, 600)]
        public void Perf_TwoBuildsEveryTick_AvgAndWorstTick(int units, int goals)
        {
            Simulation sim = ConstantTwoBuilds(units, goals, out FlowFieldCache cache);
            var sw = new Stopwatch();
            var times = new double[200];
            int b0 = cache.BuildCount;
            for (int t = 0; t < times.Length; t++)
            {
                sw.Restart();
                sim.Tick();
                times[t] = sw.Elapsed.TotalMilliseconds;
            }
            int builds = cache.BuildCount - b0;
            double avg = times.Average();
            double[] sorted = times.OrderBy(x => x).ToArray();
            double p99 = sorted[(int)(sorted.Length * 0.99) - 1], worst = sorted[^1];
            _out.WriteLine($"{units} units, {goals} goals, cache {cache.Capacity}: {builds} builds in {times.Length} ticks; " +
                           $"avg {avg:F2} ms, p99 {p99:F2} ms, worst {worst:F2} ms");
            Assert.True(builds >= 2 * times.Length - 4, $"only {builds} builds");
            Assert.True(avg < 4.0, $"avg {avg:F2} ms (docs/03: < 4 ms)");
            Assert.True(p99 < 8.0, $"p99 {p99:F2} ms (docs/03: p99 < 8 ms)");
        }

        /// <summary>A 1024 x 1024 world keeps the 32-field floor and doesn't grow past M1-4b's 221 MB by more than the documented index (4.2 MB), fog (2.4 MB) and the itemised steps since.</summary>
        [Fact]
        public void World_1024Map_CacheStays32_MemoryBounded()
        {
            var p = MapGenParams.Default with { Width = 1024, Height = 1024 };
            var config = TestSim.Config(4950, 2, 4096, 64) with { Map = p };
            World? world = null;
            Action create = () => world = new World(config);
            long bytes = AllocationProbe.Measure(create);
            Assert.NotNull(world);
            Assert.Equal(32, world!.FlowFields.Capacity);
            _out.WriteLine($"1024 x 1024 world, 4096 unit slots: {bytes / 1e6:F1} MB ({bytes} bytes) allocated, cache {world.FlowFields.Capacity}");
            // M1-4b: 221 MB. M1-4c adds the 4-byte cell-to-slot index (4.2 MB here): 226 MB measured, bound 228 MB.
            // M4-3a (BUG-0214) re-baselines: before the fog 227,976,424 bytes were measured (dd5b5b9). The fog the design
            // requires (docs/03 "Vision, detection, fog") adds per player a byte a cell (1,048,576) and a packed explored
            // bit a cell (131,072), and per unit slot per player two reveal ints (4,096 x 2 x 2 x 4 = 65,536): 2 x 1,179,648
            // + 65,536 = 2,424,832 bytes, plus 26,592 of circle masks, per-player boxes and array headers. 230,427,848
            // measured; the bound keeps a 72 KB margin (the old one kept 2 MB over M1-4c's figure, 24 KB over dd5b5b9's).
            // M4-H1 (BUG-0215) re-baselines: the visible bits are hashed state, packed like the explored bits, one more bit a
            // cell a player (2 x 131,072 = 262,144 bytes, + array headers): 230,692,672 measured; the same 72 KB margin.
            // M4-4a re-baselines: the ability state docs/03 "Implementation (M4-4a)" requires, per unit slot: the cast (ability,
            // ticks, point: 16 bytes), 4 cooldown ready ticks (16), and the status store's count plus 8 entries of (id,
            // magnitude, ticks, source) (4 + 128): 164 x 4,096 = 671,744 bytes; and the one-tick cast event list, 2 per slot
            // at 28 bytes (229,376), plus array headers: 231,658,712 measured (+966,040); the same 72 KB margin.
            // M4-4a fix round 1 (BUG-0301): each status entry gains its own pulse clock (int, 4 bytes x 8 entries x 4,096
            // slots = 131,072): 231,789,560 measured (+130,848); the same 72 KB margin.
            // M4-4b-3a re-baselines (BUG-0302 b), every step since itemised:
            // - M4-4b-2, +18,536 (231,808,096 on 63efab2): the per-slot blind in force (StatusStore._blind, int x 4,096 =
            //   16,384 + a 24-byte header); the zone store's 64 slots (alive 64, owner 256, centre 512, ability 256, ticks 256,
            //   radius 256, vision flag 64 = 1,664, + 7 x 24 headers = 1,832); the last 296 bytes are the ZoneStore object and the
            //   fog's blind tables (the per-status mask index and the 2 m circle mask).
            // - M4-H2, +35,016 (231,843,112 on 5c2de85): UnitStore.ChaseSwitches + ChaseChainBest 4,096 x 5 = 20,480; the death
            //   list's extra BuildingCapacity 256 x 32-byte DeathEvent = 8,192; the towers' reveal pair 256 x 2 players x 4 x 2 =
            //   4,096; the zone fog records 64 x 2 players x 1 word x 8 x 2 = 2,048; about 200 bytes of array headers.
            // - M4-4b-3a, +4,144: the per-slot whenBelowHp passive flag (UnitStore._belowHp, bool x 4,096 = 4,096 + a 24-byte
            //   header); 24 bytes of the fog's per-status tables for one more status (frenzied) and their alignment.
            // 231,847,256 measured; the bound keeps the same 72 KB margin over it.
            // Under the bound since, itemised (BUG-0302 b):
            // - M4-4b-3b, +25,760 (231,873,016, measured by M4-5 as its base 3bfc6b8): UnitStore.ExpiresAtTick (an int a slot,
            //   16,408); the other 9.4 KB are the probe's allocation-context rounding (DeathEvent's Expired flag fits its
            //   32 bytes): this test's figure moves a few KB between runs of one tree (3584a3a: 231,889,912 to 231,897,088).
            // - M4-5, +17,424 (231,890,440): StatusStore._stealth 4,096 + 24, World._detectedBy 4,120, World.DetectorBuildings
            //   1,048, about 100 of counters and fog limit entries, the rest rounding (docs/03 "Implementation (M4-5)", Memory).
            // - M4-H3, +16,408 less about 4.4 KB of rounding (231,908,912 on its head, three runs alike; +11,824 to +12,040 B over
            //   its base's three runs, BUG-0513): World.DetectNeighbors, detection's own
            //   query scratch (BUG-0480), an int a slot: 4,096 x 4 = 16,384 + a 24-byte header. 11,088 bytes left under the
            //   bound: the next per-slot array (16 KB) crosses it, so that session re-baselines with these items.
            // - M5-1, +0 (re-baselined by item, the bound unchanged): the AI's PlayerView holds no per-player buffer (six
            //   references and two ints, 72 B a view, built by its reader, not by the world; Ai/PlayerViewPerfTests measures
            //   it) and the world gains no array; GameData.Ai is load-time data, outside this measurement (docs/03
            //   "Implementation (M5-1)", Memory). 231,900,840 measured alone on M5-1's tree (the probe's rounding: -8 KB).
            // M5-2 re-baselines (BUG-0540), every step itemised:
            // - the fog's per-player resource-node memory (FogStore._nodes, docs/03 "Implementation (M5-1)", Lifetime): one
            //   12-byte NodeMemory (generation, type, anchor cell) per resource slot per player: 12 x 4,096 x 2 = 98,304 bytes
            //   + a 24-byte array header = 98,328. The AI itself adds nothing here: an AiPlayer is built by the Simulation for
            //   an AI seat only (this test builds a World with none); its buffers are per AI (docs/03 "Implementation (M5-2)",
            //   Memory).
            // - the rest, +8,088, the probe's rounding coming back (M5-1's figure sat 8 KB under its tree's usual).
            // 232,007,256 measured on M5-2's tree; the bound keeps the same 72 KB margin over it.
            // M5-H2 re-baselines (232,064,400 measured on its base 8c39c08, 15.6 KB under the old bound), every step itemised:
            // - FogStore._nearBlocker (BUG-0701): a bit per map cell, 1,048,576 / 8 = 131,072 bytes + a 24-byte header.
            // - World.SurroundHolder gains a layer per owner (BUG-0732): one more player's 256 building slots x 36 spots x 4 B
            //   = 36,864 bytes.
            // - about 900 bytes of the probe's rounding. 232,233,296 measured; the bound keeps the same 72 KB margin over it.
            Assert.True(bytes < 232_306_000, $"{bytes / 1e6:F1} MB");
        }
    }
}
