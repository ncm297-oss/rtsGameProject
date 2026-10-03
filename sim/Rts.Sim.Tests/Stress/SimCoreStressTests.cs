using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>QA stress and fuzz suites for the M1-1 sim core.</summary>
public class SimCoreStressTests
{
    private readonly ITestOutputHelper _out;

    public SimCoreStressTests(ITestOutputHelper output) => _out = output;

    /// <summary>Random alloc/free churn on a small store; returns a hash of every handle produced.</summary>
    private static ulong Churn(ulong seed, int capacity, int cycles, bool checkInvariants)
    {
        var store = new UnitStore(capacity);
        var rng = new SimRng(seed, 0);
        var live = new EntityHandle[capacity];
        int liveCount = 0;
        // Every handle ever freed, bounded ring, to check stale handles never resolve again.
        var dead = new EntityHandle[4096];
        int deadCount = 0;
        var hasher = new StateHasher();
        for (int c = 0; c < cycles; c++)
        {
            bool alloc = liveCount == 0 || (liveCount < capacity && rng.NextInt(0, 2) == 0);
            if (alloc)
            {
                EntityHandle h = store.Alloc();
                if (checkInvariants)
                {
                    Assert.True(store.IsAlive(h));
                    for (int i = 0; i < liveCount; i++)
                        Assert.NotEqual(live[i].Index, h.Index); // no slot handed out twice
                }
                live[liveCount++] = h;
                hasher.Add(h.Index);
                hasher.Add(h.Generation);
            }
            else
            {
                int k = rng.NextInt(0, liveCount);
                EntityHandle h = live[k];
                store.Free(h);
                live[k] = live[--liveCount];
                dead[deadCount++ % dead.Length] = h;
                if (checkInvariants)
                {
                    Assert.False(store.IsAlive(h));
                    // Double free rejected (sampled: exceptions are slow, 500k of them take seconds).
                    if ((c & 63) == 0)
                        Assert.Throws<ArgumentException>(() => store.Free(h));
                }
            }
            if (checkInvariants && (c & 1023) == 0)
            {
                int n = Math.Min(deadCount, dead.Length);
                for (int i = 0; i < n; i++)
                    Assert.False(store.IsAlive(dead[i]), $"cycle {c}: stale handle {dead[i]} resolves");
                Assert.Equal(liveCount, store.Count);
                Assert.Equal(capacity - liveCount, store.FreeCount);
            }
        }
        for (int i = 0; i < store.FreeCount; i++)
            hasher.Add(store.FreeListAt(i));
        return hasher.Value;
    }

    [Fact]
    public void HandleChurn_1M_Cycles_Capacity64_NoStaleResolve_NoDoubleFree()
    {
        Churn(seed: 1, capacity: 64, cycles: 1_000_000, checkInvariants: true);
    }

    [Fact]
    public void HandleChurn_FreeListOrder_IsDeterministicAcrossRuns()
    {
        ulong a = Churn(seed: 77, capacity: 64, cycles: 1_000_000, checkInvariants: false);
        ulong b = Churn(seed: 77, capacity: 64, cycles: 1_000_000, checkInvariants: false);
        ulong c = Churn(seed: 78, capacity: 64, cycles: 1_000_000, checkInvariants: false);
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void HandleChurn_SingleSlot_GenerationsStrictlyIncrease()
    {
        var store = new UnitStore(1);
        int last = 0;
        for (int i = 0; i < 1_000_000; i++)
        {
            EntityHandle h = store.Alloc();
            Assert.True(h.Generation > last);
            last = h.Generation;
            store.Free(h);
        }
    }

    /// <summary>Random command stream into a sim; returns hashes at every 100-tick checkpoint.</summary>
    private static ulong[] RunFuzz(ulong seed, int ticks, int players)
    {
        var sim = new Simulation(new SimConfig(seed, players, UnitCapacity: 256, CommandCapacity: 512));
        var input = new SimRng(seed ^ 0xABCDEF, 99); // test-side RNG for the command stream
        var hashes = new ulong[ticks / 100];
        for (int t = 0; t < ticks; t++)
        {
            int n = input.NextInt(0, 6);
            for (int k = 0; k < n; k++)
            {
                int p = input.NextInt(0, players);
                if (input.NextInt(0, 3) == 0)
                    sim.Enqueue(Command.Noop(p));
                else
                    sim.Enqueue(Command.SpawnUnit(p, input.NextInt(0, 10),
                        new Vector2(input.NextFloat() * 256f, input.NextFloat() * 256f)));
            }
            // Exercise the RNG streams the way systems will.
            sim.World.Rng(RngStream.Combat).NextUInt();
            sim.World.Rng(RngStream.Ai(t % players)).NextInt(0, 100);
            sim.Tick();

            UnitStore u = sim.World.Units;
            Assert.InRange(u.Count, 0, u.Capacity);
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                Assert.True(float.IsFinite(u.Position[i].X) && float.IsFinite(u.Position[i].Y));
                Assert.InRange(u.Owner[i], 0, players - 1);
            }
            if ((t + 1) % 100 == 0)
                hashes[(t + 1) / 100 - 1] = sim.StateHash();
        }
        return hashes;
    }

    [Fact]
    public void Determinism_FuzzedCommandStreams_20Runs_IdenticalAtEveryCheckpoint()
    {
        ulong[] reference = RunFuzz(seed: 1234, ticks: 2000, players: 4);
        for (int run = 0; run < 20; run++)
            Assert.Equal(reference, RunFuzz(seed: 1234, ticks: 2000, players: 4));
    }

    [Fact]
    public void Determinism_ManySeeds_AllDistinct_AndReproducible()
    {
        var finals = new HashSet<ulong>();
        for (ulong seed = 0; seed < 40; seed++)
        {
            ulong[] a = RunFuzz(seed, ticks: 1000, players: 3);
            ulong[] b = RunFuzz(seed, ticks: 1000, players: 3);
            Assert.Equal(a, b);
            Assert.True(finals.Add(a[^1]), $"seed {seed} collides with an earlier seed");
        }
    }

    [Fact]
    public void Determinism_WorldSeedOnly_ChangesHash()
    {
        // Same command stream, different world seed: RNG states differ, so hashes must differ.
        static ulong Run(ulong seed)
        {
            var sim = new Simulation(new SimConfig(seed, 2, 64, 64));
            for (int t = 0; t < 1000; t++)
            {
                if (t % 50 == 0) sim.Enqueue(Command.SpawnUnit(t % 2, 1, new Vector2(t, t)));
                sim.Tick();
            }
            return sim.StateHash();
        }
        Assert.Equal(Run(5), Run(5));
        Assert.NotEqual(Run(5), Run(6));
        Assert.NotEqual(Run(0), Run(ulong.MaxValue));
    }

    [Fact]
    public void Flood_10000Commands_OneTick_AllocatesNothing()
    {
        const int players = 8;
        const int n = 10_000;
        var sim = new Simulation(new SimConfig(1, players, UnitCapacity: 2 * n + 16, CommandCapacity: n));
        // Warm-up path.
        sim.Enqueue(Command.SpawnUnit(0, 0, Vector2.Zero));
        sim.Tick();
        sim.Tick();
        for (int i = 0; i < n; i++)
            sim.Enqueue(Command.SpawnUnit(players - 1 - (i % players), i, new Vector2(i, 0)));
        long before = GC.GetAllocatedBytesForCurrentThread();
        sim.Tick();
        sim.Tick();
        long delta = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, delta);
        Assert.Equal(n + 1, sim.World.Units.Count);
    }

    /// <summary>Worst time over 3 reps for two ticks that sort and apply n commands from 8 players.</summary>
    private static double FloodMs(int n)
    {
        // Worst realistic arrival order for the insertion sort: players arrive round-robin in
        // descending order, so almost every command has to move past many others.
        const int players = 8;
        double worstMs = 0;
        for (int rep = 0; rep < 3; rep++)
        {
            var sim = new Simulation(new SimConfig(1, players, UnitCapacity: n, CommandCapacity: n));
            sim.Tick();
            for (int i = 0; i < n; i++)
                sim.Enqueue(Command.Noop(players - 1 - (i % players)));
            var sw = Stopwatch.StartNew();
            sim.Tick(); // sorts the whole batch (applies on the next tick)
            sim.Tick();
            sw.Stop();
            worstMs = Math.Max(worstMs, sw.Elapsed.TotalMilliseconds);
        }
        return worstMs;
    }

    [Trait("Category", "Perf")]
    [Theory]
    [InlineData(500)]
    [InlineData(1000)]
    public void Flood_InterleavedCommands_UnderTickBudget(int n)
    {
        double ms = FloodMs(n);
        _out.WriteLine($"{n} interleaved commands: worst {ms:F2} ms over two ticks");
        // docs/03 perf budget: p99 tick < 8 ms.
        Assert.True(ms < 8, $"{n}-command tick took {ms:F2} ms");
    }

    [Trait("Category", "Perf")]
    [Fact(Skip = "BUG-0005: CommandQueue insertion sort is O(n^2); un-skip when fixed")]
    public void Flood_10000InterleavedCommands_NoFrameStall()
    {
        double ms = FloodMs(10_000);
        _out.WriteLine($"10000 interleaved commands: worst {ms:F1} ms over two ticks");
        // One tick longer than a whole 50 ms frame is a visible stall. Measured ~107 ms (Release).
        Assert.True(ms < 50, $"10k-command tick took {ms:F1} ms");
    }

    [Trait("Category", "Perf")]
    [Theory]
    [InlineData(500)]
    [InlineData(1000)]
    [InlineData(2500)]
    public void EmptyTick_WithManyUnits_IsCheap(int units)
    {
        var sim = new Simulation(new SimConfig(1, 2, UnitCapacity: units, CommandCapacity: units));
        for (int i = 0; i < units; i++)
            sim.Enqueue(Command.SpawnUnit(i % 2, 0, new Vector2(i, i)));
        sim.Tick();
        sim.Tick();
        Assert.Equal(units, sim.World.Units.Count);
        for (int i = 0; i < 100; i++) sim.Tick();
        var sw = Stopwatch.StartNew();
        const int ticks = 2000;
        for (int i = 0; i < ticks; i++) sim.Tick();
        sw.Stop();
        double avgMs = sw.Elapsed.TotalMilliseconds / ticks;
        var hsw = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++) sim.StateHash();
        hsw.Stop();
        _out.WriteLine($"{units} units: avg tick {avgMs * 1000:F1} us, StateHash {hsw.Elapsed.TotalMilliseconds / 100:F3} ms");
        Assert.True(avgMs < 0.5, $"avg empty tick {avgMs} ms with {units} units");
    }
}
