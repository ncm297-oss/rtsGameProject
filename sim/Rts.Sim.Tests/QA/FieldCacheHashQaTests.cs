using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;
using Rts.Sim.Pathfinding;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA attacks on M1-4c: the flow-field cache's metadata and OrderTick are hashed sim state
/// (BUG-0021), misses are served oldest order first at two per tick (BUG-0022), and the cache is
/// sized by <see cref="FlowFieldCache.CapacityFor"/>.
/// </summary>
public class FieldCacheHashQaTests
{
    private readonly ITestOutputHelper _out;

    public FieldCacheHashQaTests(ITestOutputHelper output) => _out = output;

    // ---------- helpers ----------

    private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

    private static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, Priv)!.GetValue(o)!;

    private static FlowField[] Slots(FlowFieldCache c) => Field<FlowField[]>(c, "_fields");
    private static long[] LastUse(FlowFieldCache c) => Field<long[]>(c, "_lastUse");
    private static int[] SlotOfCell(FlowFieldCache c) => Field<int[]>(c, "_slotOfCell");
    private static long Clock(FlowFieldCache c) => Field<long>(c, "_clock");

    /// <summary>QA's own view of the cache's sim-visible metadata, read by reflection (not through AddToHash).</summary>
    private static List<long> CacheSignature(FlowFieldCache c)
    {
        var sig = new List<long> { Clock(c), c.Count };
        FlowField[] f = Slots(c);
        long[] use = LastUse(c);
        for (int i = 0; i < c.Count; i++)
        {
            sig.Add(f[i].RequestedCell);
            sig.Add(f[i].Version);
            sig.Add(use[i]);
        }
        return sig;
    }

    /// <summary>QA's own view of everything a future tick reads: units and cache metadata.</summary>
    private static List<long> FullSignature(Simulation sim)
    {
        UnitStore u = sim.World.Units;
        var sig = new List<long> { sim.TickNumber };
        for (int i = 0; i < u.Capacity; i++)
        {
            sig.Add(u.Alive[i] ? 1 : 0);
            sig.Add(u.Generation[i]);
            if (!u.Alive[i]) continue;
            sig.Add(BitConverter.SingleToInt32Bits(u.Position[i].X));
            sig.Add(BitConverter.SingleToInt32Bits(u.Position[i].Y));
            sig.Add(BitConverter.SingleToInt32Bits(u.Velocity[i].X));
            sig.Add(BitConverter.SingleToInt32Bits(u.Velocity[i].Y));
            sig.Add(BitConverter.SingleToInt32Bits(u.Facing[i]));
            sig.Add(u.Owner[i]);
            sig.Add(u.TypeId[i]);
            sig.Add((int)u.State[i]);
            sig.Add(BitConverter.SingleToInt32Bits(u.Goal[i].X));
            sig.Add(BitConverter.SingleToInt32Bits(u.Goal[i].Y));
            sig.Add(u.GoalCell[i]);
            sig.Add(u.OrderTick[i]);
        }
        sig.AddRange(CacheSignature(sim.World.FlowFields));
        return sig;
    }

    /// <summary>The cell-to-slot index must be exactly the inverse of the used slots' requested cells.</summary>
    private static string? IndexMismatch(FlowFieldCache c)
    {
        int[] index = SlotOfCell(c);
        FlowField[] f = Slots(c);
        for (int s = 0; s < c.Count; s++)
        {
            int cell = f[s].RequestedCell;
            if (cell < 0) return $"used slot {s} has no requested cell";
            if (index[cell] != s) return $"slot {s} holds cell {cell} but index[{cell}] = {index[cell]}";
        }
        int mapped = 0;
        for (int cell = 0; cell < index.Length; cell++)
        {
            int s = index[cell];
            if (s < 0) continue;
            mapped++;
            if (s >= c.Count) return $"index[{cell}] = {s} points past Count {c.Count}";
            if (f[s].RequestedCell != cell) return $"index[{cell}] = {s} but slot {s} holds {f[s].RequestedCell}";
        }
        return mapped == c.Count ? null : $"{mapped} cells mapped for {c.Count} used slots";
    }

    // ---------- hash completeness: every cache mutation path changes StateHash ----------

    [Fact]
    public void Hash_ChangesOnEveryCacheMutationPath_MissHitTouchVersionRebuildEviction()
    {
        // Capacity 32 (64 unit slots on the default map), so eviction is reachable with 33 cells.
        var sim = new Simulation(TestSim.Config(4401, 2, UnitCapacity: 64, CommandCapacity: 64));
        FlowFieldCache c = sim.World.FlowFields;
        Assert.Equal(32, c.Capacity);
        List<int> open = FlowFieldOracle.PassableCells(sim.World.NavGrid);
        var seen = new HashSet<ulong> { sim.StateHash() };
        void Changed(string what)
        {
            ulong h = sim.StateHash();
            Assert.True(seen.Add(h), $"{what}: StateHash did not change (or repeated an earlier state)");
        }

        c.Get(open[10]); Changed("miss build into a free slot");
        c.Get(open[20]); Changed("second miss build");
        c.Get(open[10]); Changed("Get hit (LRU reorder, no build)");
        Assert.Equal(2, c.BuildCount);
        Assert.NotNull(c.TryGetCached(open[20])); Changed("TryGetCached hit (touch)");
        ulong beforeMiss = sim.StateHash();
        Assert.Null(c.TryGetCached(open[30]));
        Assert.Equal(beforeMiss, sim.StateHash()); // a TryGetCached miss changes nothing
        // The grid itself (and its Version) isn't hashed: it is immutable in production until
        // passability changes exist (M3+), and then the grid must join the hash. A bump alone only
        // makes fields stale; the rebuild below is what changes cache state.
        ulong beforeBump = sim.StateHash();
        sim.World.NavGrid.BumpVersionForTests();
        Assert.Equal(beforeBump, sim.StateHash());
        Assert.False(c.Contains(open[10]));
        c.Get(open[10]); Changed("stale rebuild in place");
        for (int k = 0; k < 30; k++) c.Get(open[100 + k]);
        Assert.Equal(32, c.Count);
        Changed("fill to capacity");
        int lruCell = open[20]; // stale and least recently used
        c.Get(open[500]); Changed("eviction build");
        Assert.False(c.Contains(lruCell));
        Assert.Null(IndexMismatch(c));
    }

    [Fact]
    public void Hash_OrderTickOfALiveUnitIsCovered_ButOfAFreeSlotIsNot_AndRespawnResetsIt()
    {
        Simulation sim = MoveScenario.Spawn(4402, units: 4, maxCost: 30f, out int goal);
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.Move(u.Owner[2], MoveScenario.Handle(sim, 2), MoveScenario.Center(sim.World.NavGrid, goal)));
        sim.Tick();
        sim.Tick();
        int stamped = u.OrderTick[2];
        Assert.True(stamped > 0);
        // A Move dropped for a foreign owner must leave OrderTick alone.
        sim.Enqueue(Command.Move(1 - u.Owner[2], MoveScenario.Handle(sim, 2), MoveScenario.Center(sim.World.NavGrid, goal)));
        sim.Tick();
        Assert.Equal(stamped, u.OrderTick[2]);
        // Free and respawn into the same slot: OrderTick back to 0.
        u.Free(MoveScenario.Handle(sim, 2));
        ulong freed = sim.StateHash();
        u.OrderTick[2] = 12345; // dead slot: not sim state
        Assert.Equal(freed, sim.StateHash());
        sim.Enqueue(Command.SpawnUnit(0, 0, MoveScenario.Center(sim.World.NavGrid, goal)));
        sim.Tick();
        sim.Tick();
        Assert.True(u.Alive[2]);
        Assert.Equal(0, u.OrderTick[2]);
    }

    /// <summary>
    /// Reflection audit: every instance field of FlowFieldCache and FlowField is either hashed or
    /// explained as derived here. A new field fails this until QA classifies it.
    /// </summary>
    [Fact]
    public void Reflection_EveryCacheField_IsHashedOrKnownDerived()
    {
        var cacheFields = new Dictionary<string, string>
        {
            ["_grid"] = "derived: the world's grid",
            ["_fields"] = "hashed per used slot (RequestedCell, Version); contents derived from grid + key",
            ["_lastUse"] = "hashed per used slot",
            ["_slotOfCell"] = "derived: inverse of the used slots' RequestedCell (QA index fuzz)",
            ["_queue"] = "scratch",
            ["_steps"] = "derived from the grid, recomputed per version",
            ["_stepsVersion"] = "derived: last grid version the steps were computed for",
            ["_count"] = "hashed",
            ["_clock"] = "hashed",
            ["<BuildCount>k__BackingField"] = "statistic; no sim decision reads it",
        };
        var fieldFields = new Dictionary<string, string>
        {
            ["_cost"] = "derived from grid + key",
            ["_direction"] = "derived from grid + key",
            ["_offset"] = "derived from width",
            ["<Width>k__BackingField"] = "derived",
            ["<Height>k__BackingField"] = "derived",
            ["<RequestedCell>k__BackingField"] = "hashed",
            ["<TargetCell>k__BackingField"] = "derived from grid + RequestedCell",
            ["<Version>k__BackingField"] = "hashed",
        };
        string Unknown(Type t, Dictionary<string, string> known) => string.Join(", ",
            t.GetFields(Priv | BindingFlags.Public).Select(f => f.Name).Where(n => !known.ContainsKey(n)));
        Assert.Equal("", Unknown(typeof(FlowFieldCache), cacheFields));
        Assert.Equal("", Unknown(typeof(FlowField), fieldFields));

        // The only public members must be read-only: nothing outside the sim can move hashed state.
        string[] publicMethods = typeof(FlowFieldCache)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName).Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "CapacityFor", "Contains" }, publicMethods);
        Assert.All(typeof(FlowFieldCache).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            p => Assert.False(p.SetMethod?.IsPublic ?? false, $"{p.Name} has a public setter"));
        Assert.False(typeof(World).GetProperty("FlowFields")!.SetMethod?.IsPublic ?? false);
    }

    [Fact]
    public void Contains_IsPure_DoesNotChangeTheHash()
    {
        var sim = new Simulation(TestSim.Config(4403, 2, UnitCapacity: 64, CommandCapacity: 64));
        List<int> open = FlowFieldOracle.PassableCells(sim.World.NavGrid);
        sim.World.FlowFields.Get(open[5]);
        ulong h = sim.StateHash();
        for (int k = 0; k < 50; k++) sim.World.FlowFields.Contains(open[k]);
        Assert.False(sim.World.FlowFields.Contains(-1));
        Assert.False(sim.World.FlowFields.Contains(int.MaxValue));
        Assert.Equal(h, sim.StateHash());
    }

    // ---------- CapacityFor boundaries ----------

    [Theory]
    [InlineData(263, 16384, 32)]          // 263 / 8 = 32
    [InlineData(264, 16384, 33)]          // first unit count above the floor
    [InlineData(1031, 16384, 128)]
    [InlineData(int.MaxValue, 16384, 128)]
    [InlineData(int.MaxValue, int.MaxValue, 32)] // no overflow in 5 x cells
    [InlineData(4096, 409_600, 32)]        // 640 x 640: 64 MiB / 2 MB = 32.77 -> 32
    [InlineData(4096, 65_536, 128)]        // 256 x 256: 204 by memory, 128 by units
    [InlineData(4096, 1, 128)]
    public void CapacityFor_Boundaries(int units, int cells, int expected) =>
        Assert.Equal(expected, FlowFieldCache.CapacityFor(units, cells));

    [Theory]
    [InlineData(0, 16384)]
    [InlineData(-1, 16384)]
    [InlineData(64, 0)]
    [InlineData(64, -5)]
    public void CapacityFor_RejectsNonPositive(int units, int cells) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FlowFieldCache.CapacityFor(units, cells));

    [Fact]
    public void CapacityFor_NeverExceedsMemoryBudgetAboveTheFloor()
    {
        for (int side = 16; side <= 2048; side += 16)
        {
            int cells = side * side;
            int cap = FlowFieldCache.CapacityFor(int.MaxValue, cells);
            Assert.InRange(cap, FlowFieldCache.DefaultCapacity, FlowFieldCache.MaxCapacity);
            if (cap > FlowFieldCache.DefaultCapacity)
                Assert.True((long)cap * FlowFieldCache.BytesPerCell * cells <= FlowFieldCache.MemoryBudgetBytes, $"side {side}: {cap} fields");
        }
    }

    // ---------- the cell-to-slot index (dev deviation) can't desync ----------

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(32)]
    public void IndexFuzz_RandomGetsTouchesAndVersionBumps_IndexAlwaysInvertsTheSlots(int capacity)
    {
        NavGrid g = FlowFieldOracle.Generated(4404);
        var cache = new FlowFieldCache(g, capacity);
        List<int> open = FlowFieldOracle.PassableCells(g);
        var rng = new SimRng(4404, (ulong)capacity);
        // A small pool (some blocked cells too) so hits, evictions and re-adds of evicted cells all happen.
        var pool = new int[capacity + 3];
        for (int k = 0; k < pool.Length; k++)
            pool[k] = k % 5 == 4 ? 0 : open[rng.NextInt(0, open.Count)]; // cell 0 is the blocked ring
        for (int op = 0; op < 3000; op++)
        {
            int cell = pool[rng.NextInt(0, pool.Length)];
            int r = rng.NextInt(0, 100);
            if (r < 60) cache.Get(cell);
            else if (r < 95) cache.TryGetCached(cell);
            else g.BumpVersionForTests();
            string? bad = IndexMismatch(cache);
            Assert.True(bad == null, $"op {op}: {bad}");
            // Contains agrees with a brute-force slot scan.
            bool brute = false;
            for (int s = 0; s < cache.Count; s++)
                brute |= Slots(cache)[s].RequestedCell == cell && Slots(cache)[s].Version == g.Version;
            Assert.Equal(brute, cache.Contains(cell));
        }
    }

    // ---------- save/load proxy: equal hash <=> equal state, 50 seeds x 500 ticks ----------

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(30)]
    [InlineData(40)]
    public void Fuzz_RandomMovesAndInternalGets_HashEqualIffStateEqual(int firstSeed)
    {
        int diverged = 0, mirrored = 0, oneSided = 0;
        for (int s = firstSeed; s < firstSeed + 10; s++)
        {
            ulong seed = 4500 + (ulong)s;
            // 48 unit slots: cache capacity 32, and a pool of 40 goals forces evictions.
            Simulation a = Spawn48(seed), b = Spawn48(seed);
            NavGrid g = a.World.NavGrid;
            List<int> open = FlowFieldOracle.PassableCells(g);
            var rng = new SimRng(seed, 960);
            var pool = new Vector2[40];
            for (int k = 0; k < pool.Length; k++) pool[k] = MoveScenario.Center(g, open[rng.NextInt(0, open.Count)]);
            bool apart = false;
            for (int t = 0; t < 500; t++)
            {
                int n = rng.NextInt(0, 4);
                for (int k = 0; k < n; k++)
                {
                    int slot = rng.NextInt(0, 48);
                    Vector2 target = pool[rng.NextInt(0, pool.Length)];
                    a.Enqueue(Command.Move(a.World.Units.Owner[slot], MoveScenario.Handle(a, slot), target));
                    b.Enqueue(Command.Move(b.World.Units.Owner[slot], MoveScenario.Handle(b, slot), target));
                }
                // Out-of-band internal lookups on b; mirrored on a about half the time.
                if (rng.NextInt(0, 25) == 0)
                {
                    int cell = open[rng.NextInt(0, open.Count)];
                    bool touchOnly = rng.NextInt(0, 2) == 0;
                    bool mirror = rng.NextInt(0, 2) == 0;
                    if (touchOnly) b.World.FlowFields.TryGetCached(cell); else b.World.FlowFields.Get(cell);
                    if (mirror)
                    {
                        if (touchOnly) a.World.FlowFields.TryGetCached(cell); else a.World.FlowFields.Get(cell);
                        mirrored++;
                    }
                    else oneSided++;
                }
                a.Tick();
                b.Tick();
                bool sigEqual = FullSignature(a).SequenceEqual(FullSignature(b));
                bool cacheEqual = CacheSignature(a.World.FlowFields).SequenceEqual(CacheSignature(b.World.FlowFields));
                bool hashEqual = a.StateHash() == b.StateHash();
                Assert.True(hashEqual == sigEqual, $"seed {seed} tick {t}: hash equal {hashEqual}, state equal {sigEqual}");
                if (!cacheEqual) Assert.False(hashEqual, $"seed {seed} tick {t}: caches differ, hashes equal");
                if (apart) Assert.False(hashEqual, $"seed {seed} tick {t}: re-converged after diverging (clock can't re-converge)");
                if (!hashEqual && !apart) { apart = true; diverged++; }
                Assert.Null(IndexMismatch(a.World.FlowFields));
                Assert.Null(IndexMismatch(b.World.FlowFields));
            }
        }
        _out.WriteLine($"seeds {firstSeed}-{firstSeed + 9}: {diverged} diverged, {mirrored} mirrored lookups, {oneSided} one-sided");
        Assert.True(diverged > 0, "no seed exercised a one-sided cache change");
    }

    private static Simulation Spawn48(ulong seed)
    {
        var sim = new Simulation(TestSim.Config(seed, 2, UnitCapacity: 48, CommandCapacity: 256));
        NavGrid g = sim.World.NavGrid;
        List<int> open = FlowFieldOracle.PassableCells(g);
        var rng = new SimRng(seed, 961);
        for (int i = 0; i < 48; i++)
            sim.Enqueue(Command.SpawnUnit(i % 2, i % TestSim.UnitTypeCount, MoveScenario.Center(g, open[rng.NextInt(0, open.Count)])));
        sim.Tick();
        sim.Tick();
        Assert.Equal(32, sim.World.FlowFields.Capacity);
        return sim;
    }

}
