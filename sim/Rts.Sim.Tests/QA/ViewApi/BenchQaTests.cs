using System.Reflection;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>QA (M2-7): the bench script and frame-time histogram against independent oracles under random frame deltas, plus the read-only proof for the new ViewApi types.</summary>
/// <remarks>
/// The oracles are written from docs/03 "Implementation (M2-7)": steps fire in table order, looping;
/// the first is due at time 0 and each next one its predecessor's <c>Seconds</c> later (cumulative
/// schedule); at most one per call; nothing fires once the elapsed time reaches the duration; bad
/// deltas add no time. Percentiles are nearest-rank, reported as a bin's upper edge (so never below
/// the true value and at most one bin above it), capped at the worst sample.
/// </remarks>
[Collection(SerialCollection.Name)]
public class BenchQaTests
{
    private readonly ITestOutputHelper _out;

    public BenchQaTests(ITestOutputHelper output) => _out = output;

    private static double RandomDelta(Random rng)
    {
        int kind = rng.Next(100);
        return kind switch
        {
            0 => double.NaN,
            1 => double.PositiveInfinity,
            2 => double.NegativeInfinity,
            3 => -rng.NextDouble(),
            4 => 0,
            5 => rng.NextDouble() * 3.0,          // a stall longer than any step
            6 => double.Epsilon,
            _ => rng.NextDouble() * 0.05,         // 0-50 ms frames
        };
    }

    [Theory]
    [InlineData(1, 0.01)]
    [InlineData(2, 3.0)]
    [InlineData(3, 10.0)]
    [InlineData(4, 25.0)]
    [InlineData(5, 61.7)]
    [InlineData(6, 0.25)]
    [InlineData(7, 1e-9)]
    public void Script_RandomDeltas_MatchesTheCumulativeScheduleOracle(int seed, double duration)
    {
        var rng = new Random(seed);
        var script = new BenchScript(duration);
        double elapsed = 0, nextAt = 0;
        int fired = 0, calls = 0;
        bool finishedSeen = false;
        while (calls < 200_000)
        {
            double d = RandomDelta(rng);
            double before = script.Elapsed;
            bool any = script.Advance(d, out BenchScript.Entry e);
            calls++;
            if (!finishedSeen && d > 0 && double.IsFinite(d)) elapsed += d;
            Assert.True(script.Elapsed >= before, "elapsed went backwards");
            if (finishedSeen)
            {
                Assert.False(any, "a step fired after the run finished");
                Assert.True(script.Finished);
                if (calls > 50) break; // keep poking a finished script for a while
                continue;
            }
            Assert.Equal(elapsed, script.Elapsed, 12);
            if (elapsed >= duration)
            {
                Assert.True(script.Finished, $"elapsed {elapsed} >= duration {duration} but not finished");
                Assert.False(any, "a step fired on the call that reached the duration");
                finishedSeen = true;
                calls = 0;
                continue;
            }
            Assert.False(script.Finished, $"finished early at {elapsed} of {duration}");
            if (any)
            {
                BenchScript.Entry want = BenchScript.Sequence[fired % BenchScript.Sequence.Length];
                Assert.Equal(want, e);
                Assert.True(elapsed >= nextAt - 1e-12, $"step {fired} ({e.Step}) fired at {elapsed}, scheduled {nextAt}");
                nextAt += want.Seconds;
                fired++;
            }
            else
            {
                // Nothing fired: so the next step can't be due yet.
                Assert.True(elapsed < nextAt + 1e-12, $"step {fired} due at {nextAt} but nothing fired at {elapsed}");
            }
            Assert.Equal(fired, script.StepsRun);
            Assert.Equal(fired / BenchScript.Sequence.Length, script.Loops);
        }
        Assert.True(finishedSeen, $"never finished in {calls} calls (elapsed {script.Elapsed})");
        _out.WriteLine($"seed {seed}, {duration} s: {fired} steps, {script.Loops} loops, elapsed {script.Elapsed:F3}");
    }

    [Fact]
    public void Script_SteadyFrames_EveryStepFiresWithinOneFrameOfItsTime_AndJumpsBefore3s()
    {
        foreach (double dt in new[] { 1.0 / 30, 1.0 / 60, 1.0 / 144, 1.0 / 1500, 0.2 })
        {
            var script = new BenchScript(30);
            double nextAt = 0;
            int k = 0;
            double lastJump = 0;
            while (!script.Finished)
            {
                if (!script.Advance(dt, out BenchScript.Entry e)) continue;
                Assert.InRange(script.Elapsed, nextAt, nextAt + dt + 1e-9);
                if (k < BenchScript.Sequence.Length && e.Step == BenchStep.MinimapJump) lastJump = script.Elapsed;
                nextAt += e.Seconds;
                k++;
            }
            Assert.True(lastJump < 3.0, $"dt {dt}: the 4th minimap jump of loop 1 at {lastJump} s, after the documented 3 s");
            int expected = 0;
            double t = 0;
            for (int i = 0; t < script.Elapsed - dt; i++)
            {
                expected++;
                t += BenchScript.Sequence[i % BenchScript.Sequence.Length].Seconds;
            }
            Assert.InRange(script.StepsRun, expected - 1, expected + 1);
        }
    }

    [Fact]
    public void Script_LoopIsTenSeconds_AsDocumented()
    {
        Assert.Equal(10.0, BenchScript.LoopSeconds, 9);
        Assert.Equal(16, BenchScript.Sequence.Length);
        foreach (BenchScript.Entry e in BenchScript.Sequence) Assert.True(e.Seconds > 0, $"{e.Step} waits {e.Seconds}");
    }

    // Nearest-rank oracle from a sorted copy.
    private static double Oracle(double[] sorted, double p)
    {
        long need = Math.Max(1, (long)Math.Ceiling(p * sorted.Length));
        return sorted[need - 1];
    }

    [Theory]
    [InlineData(11, 1)]
    [InlineData(12, 2)]
    [InlineData(13, 99)]
    [InlineData(14, 100)]
    [InlineData(15, 1000)]
    [InlineData(16, 54321)]
    public void Stats_RandomSamples_MatchSortedOracle(int seed, int count)
    {
        var rng = new Random(seed);
        var s = new FrameTimeStats();
        var samples = new List<double>();
        for (int i = 0; i < count; i++)
        {
            int kind = rng.Next(20);
            double ms = kind switch
            {
                0 => rng.NextDouble() * 1000,                        // spikes, some past the histogram
                1 => Math.Round(rng.NextDouble() * 50, 2),           // exactly on a bin edge
                2 => 0,
                3 => FrameTimeStats.MaxMs,
                4 => FrameTimeStats.MaxMs - 1e-7,
                _ => 0.3 + rng.NextDouble() * 20,
            };
            s.Add(ms);
            samples.Add(ms);
            if (rng.Next(50) == 0) s.Add(double.NaN); // ignored
        }
        double[] sorted = samples.OrderBy(x => x).ToArray();
        Assert.Equal(count, s.Count);
        Assert.Equal(sorted[^1], s.Worst);
        Assert.Equal(samples.Average(), s.Average, 6);
        double lastBinStart = FrameTimeStats.MaxMs - FrameTimeStats.BinMs;
        foreach (double p in new[] { 0, 1e-9, 0.01, 0.25, 0.5, 0.9, 0.99, 0.999, 1 })
        {
            double want = Oracle(sorted, p), got = s.Percentile(p);
            if (want >= lastBinStart - 1e-9)
            {
                // Everything from the last bin on reports the worst sample.
                Assert.True(got >= want - 1e-9 && got <= s.Worst, $"p{p}: got {got}, oracle {want}, worst {s.Worst}");
                continue;
            }
            Assert.True(got >= want - 1e-9, $"p{p}: {got} below the true nearest-rank value {want}");
            Assert.True(got <= want + FrameTimeStats.BinMs + 1e-9, $"p{p}: {got} more than one bin above {want}");
            Assert.True(got <= s.Worst, $"p{p}: {got} above worst {s.Worst}");
        }
        // Monotone in p.
        double prev = -1;
        for (double p = 0; p <= 1.0001; p += 0.01)
        {
            double v = s.Percentile(p);
            Assert.True(v >= prev, $"percentile not monotone at {p}: {v} < {prev}");
            prev = v;
        }
        Assert.Equal(s.Percentile(1), s.Percentile(double.NaN)); // NaN reads as 1, documented-ish
        Assert.Equal(s.Percentile(0), s.Percentile(-5));
        Assert.Equal(s.Percentile(1), s.Percentile(5));
    }

    [Fact]
    public void Stats_HistogramCoversTheDocumentedRange()
    {
        // 250 / 0.01 in floating point must still give a bin for every 0.01 ms up to 250 ms.
        var s = new FrameTimeStats();
        s.Add(249.985);
        s.Add(1000);
        Assert.InRange(s.Percentile(0.5), 249.985, 249.985 + FrameTimeStats.BinMs + 1e-9);
        Assert.Equal(1000, s.Percentile(1));
    }

    [Fact]
    public void Stats_Percentile_AndLoopSeconds_AllocateNothing()
    {
        var s = new FrameTimeStats();
        for (int i = 0; i < 5000; i++) s.Add(i * 0.05);
        double sink = 0;
        Action block = () =>
        {
            for (int i = 0; i < 20; i++) sink += s.Percentile(i / 20.0);
            sink += BenchScript.LoopSeconds;
        };
        block();
        AllocationProbe.AssertZero(block, _out);
        Assert.True(sink > 0);
    }

    [Fact]
    public void NewViewApiTypes_ReferenceNoSimState()
    {
        // Read-only by construction: nothing in BenchScript / BenchStep / FrameTimeStats can reach a World.
        foreach (Type t in new[] { typeof(BenchScript), typeof(BenchScript.Entry), typeof(BenchStep), typeof(FrameTimeStats) })
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var touched = new List<Type>();
            touched.AddRange(t.GetFields(all).Select(f => f.FieldType));
            touched.AddRange(t.GetProperties(all).Select(p => p.PropertyType));
            foreach (MethodBase m in t.GetMethods(all).Cast<MethodBase>().Concat(t.GetConstructors(all)))
                touched.AddRange(m.GetParameters().Select(p => p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType));
            foreach (Type x in touched)
            {
                Type e = x.IsArray ? x.GetElementType()! : x;
                bool ok = e.Assembly != typeof(World).Assembly || e.Namespace == "Rts.Sim.ViewApi";
                Assert.True(ok, $"{t.Name} touches {e.FullName}");
            }
        }
    }

    [Fact]
    public void BenchDrivenAlongsideASim_HashEqualsABareTwin_EveryTick()
    {
        // The bench objects run beside the sim every "frame"; the commands are the same in both twins.
        var cfg = TestSim.Config(5, PlayerCount: 2, UnitCapacity: 256, CommandCapacity: 1024);
        var a = new Simulation(cfg);
        var b = new Simulation(TestSim.Config(5, PlayerCount: 2, UnitCapacity: 256, CommandCapacity: 1024));
        var spots = StartLayout.Block(a.World.NavGrid, 40, west: true, 1f);
        int type = a.World.Data.Factions[0].Units[0];
        foreach (var p in spots)
        {
            a.Enqueue(Command.SpawnUnit(0, type, p));
            b.Enqueue(Command.SpawnUnit(0, type, p));
        }
        var script = new BenchScript(15);
        var stats = new FrameTimeStats();
        var clock = new FixedStepClock();
        var rng = new Random(9);
        int ticks = 0;
        while (!script.Finished)
        {
            double dt = 0.004 + rng.NextDouble() * 0.03;
            ulong before = a.StateHash();
            stats.Add(dt * 1000);
            bool step = script.Advance(dt, out _);
            _ = stats.Percentile(0.99);
            Assert.Equal(before, a.StateHash());
            if (step && a.World.Units.Count > 0)
            {
                // What the view does for an order step: one move for the first live unit (same in both twins).
                for (int i = 0; i < a.World.Units.Capacity; i++)
                {
                    if (!a.World.Units.Alive[i]) continue;
                    var h = new EntityHandle(i, a.World.Units.Generation[i]);
                    var to = spots[rng.Next(spots.Length)];
                    a.Enqueue(Command.Move(0, h, to));
                    b.Enqueue(Command.Move(0, h, to));
                    break;
                }
            }
            int n = clock.Advance(dt, 1.0);
            for (int k = 0; k < n; k++)
            {
                a.Tick();
                b.Tick();
                ticks++;
                Assert.Equal(b.StateHash(), a.StateHash());
            }
        }
        Assert.True(ticks >= 290, $"{ticks} ticks");
        _out.WriteLine($"{ticks} ticks, {script.StepsRun} steps, {stats.Count} frames, hashes equal throughout");
    }
}
