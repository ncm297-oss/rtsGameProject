using System.Diagnostics;
using Rts.Sim.Tests.QA;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA scale-up of the M1-5 cross-map scenario: seeds 1-50 with one owner and two, 500 and 1,000
/// units, goals on a level-2 plateau (two ramps in series), a start region straddling a ramp, the
/// time-limit headroom per seed, two-sim hash equality every tick, and (Serial) perf and allocation.
/// </summary>
public class CrossMapStressTests
{
    private readonly ITestOutputHelper _out;

    public CrossMapStressTests(ITestOutputHelper output) => _out = output;

    /// <summary>Headroom below which a seed is flagged (brief: limit / actual ticks under 1.3).</summary>
    private const double FlagHeadroom = 1.3;

    private (int runs, int gaveUp, double minHeadroom) Sweep(ulong first, ulong last, int units, float radius, int players, int goalMinLevel,
        CrossMapStart start, bool requireAll)
    {
        int runs = 0, gaveUp = 0;
        double minHeadroom = double.MaxValue;
        var failures = new List<string>();
        for (ulong seed = first; seed <= last; seed++)
        {
            CrossMapQaScenario? s = CrossMapQaScenario.Create(seed, units, radius, players, goalMinLevel, start);
            if (s == null)
            {
                _out.WriteLine($"seed {seed}: no scenario (no level-{goalMinLevel} goal or no ramp)");
                continue;
            }
            runs++;
            CrossMapQaScenario.Outcome o = s.Run();
            double headroom = (double)s.LimitTicks / o.Ticks;
            minHeadroom = Math.Min(minHeadroom, headroom);
            gaveUp += o.GaveUp;
            string flag = headroom < FlagHeadroom ? " FLAG<1.3" : "";
            _out.WriteLine($"seed {seed}: {s.Describe()}; {o.Ticks} ticks (headroom {headroom:F2}{flag}); arrived {o.Arrived}/{units}, gave up {o.GaveUp}, " +
                $"moving {o.StillMoving}, max stuck {o.MaxStuck}, pack {(o.Pack == null ? "ok" : o.Pack)}");
            if (o.StillMoving != 0) failures.Add($"seed {seed}: {o.StillMoving} still moving at the limit {s.LimitTicks}");
            if (requireAll && !o.AllArrived(units)) failures.Add($"seed {seed}: arrived {o.Arrived}, gave up {o.GaveUp}");
        }
        _out.WriteLine($"{runs} runs, {gaveUp} gave up, min headroom {minHeadroom:F2}");
        Assert.True(failures.Count == 0, string.Join("; ", failures));
        return (runs, gaveUp, minHeadroom);
    }

    /// <summary>The dev scenario (200 units, one owner, west edge, level >= 1) over seeds 1-50, in 5 rows: all arrive, none gives up, never on blocked ground.</summary>
    [Theory]
    [InlineData(1UL, 10UL)]
    [InlineData(11UL, 20UL)]
    [InlineData(21UL, 30UL)]
    [InlineData(31UL, 40UL)]
    [InlineData(41UL, 50UL)]
    public void TwoHundred_OneOwner_Seeds1To50_AllArrive(ulong first, ulong last) =>
        Sweep(first, last, 200, 12f, 1, 1, CrossMapStart.WestEdge, requireAll: true);

    /// <summary>Owners alternating 0/1 over seeds 1-50: hard rules asserted (blocked ground, termination), arrivals reported.</summary>
    [Theory]
    [InlineData(1UL, 10UL)]
    [InlineData(11UL, 20UL)]
    [InlineData(21UL, 30UL)]
    [InlineData(31UL, 40UL)]
    [InlineData(41UL, 50UL)]
    public void TwoHundred_TwoOwners_Seeds1To50_Terminate_Report(ulong first, ulong last) =>
        Sweep(first, last, 200, 12f, 2, 1, CrossMapStart.WestEdge, requireAll: false);

    /// <summary>Goal in the farthest cell of a level-2 plateau (two ramps in series), seeds 1-20.</summary>
    [Fact]
    public void TwoHundred_ToLevel2_Seeds1To20_AllArrive()
    {
        (int runs, _, _) = Sweep(1, 20, 200, 12f, 1, 2, CrossMapStart.WestEdge, requireAll: true);
        Assert.True(runs >= 10, $"only {runs} maps with a reachable level-2 plateau");
    }

    /// <summary>The army spawns across a ramp (both levels and ramp cells in the start region), seeds 1-20.</summary>
    [Fact]
    public void TwoHundred_StartRegionOnARamp_Seeds1To20_AllArrive()
    {
        (int runs, _, _) = Sweep(1, 20, 200, 12f, 1, 1, CrossMapStart.OnRamp, requireAll: true);
        Assert.True(runs >= 10);
    }

    /// <summary>500 units (2.5x the scenario), seeds 1-10, start radius 16 cells: report arrivals and headroom; hard rules asserted.</summary>
    [Fact]
    public void FiveHundred_AcrossTheMap_Seeds1To10_Report() =>
        Sweep(1, 10, 500, 16f, 1, 1, CrossMapStart.WestEdge, requireAll: false);

    /// <summary>1,000 units (5x), seeds 1-5, start radius 20 cells: report; hard rules asserted.</summary>
    [Fact]
    public void OneThousand_AcrossTheMap_Seeds1To5_Report() =>
        Sweep(1, 5, 1000, 20f, 1, 1, CrossMapStart.WestEdge, requireAll: false);

    /// <summary>Two sims ticked in lockstep compare <see cref="Simulation.StateHash"/> after every tick (not only checkpoints), one and two owners.</summary>
    [Theory]
    [InlineData(3UL, 1)]
    [InlineData(14UL, 1)]
    [InlineData(4UL, 2)]
    public void TwoSims_HashEqualEveryTick(ulong seed, int players)
    {
        CrossMapQaScenario a = CrossMapQaScenario.Create(seed, 200, 12f, players)!;
        CrossMapQaScenario b = CrossMapQaScenario.Create(seed, 200, 12f, players)!;
        MoveScenario.MoveAll(a.Sim, a.Goal);
        MoveScenario.MoveAll(b.Sim, b.Goal);
        int ticks = 0;
        bool moving = true;
        while (moving && ticks < a.LimitTicks)
        {
            a.Sim.Tick();
            b.Sim.Tick();
            ticks++;
            Assert.True(a.Sim.StateHash() == b.Sim.StateHash(), $"seed {seed}: hashes differ at tick {ticks}");
            moving = ticks < 2;
            var u = a.Sim.World.Units;
            for (int i = 0; i < u.Capacity && !moving; i++) moving = u.Alive[i] && u.State[i] == Entities.UnitState.Moving;
        }
        _out.WriteLine($"seed {seed}, {players} owners: {ticks} ticks, hash equal every tick");
    }

    /// <summary>Different seeds give different end states (the hash isn't blind to the run).</summary>
    [Fact]
    public void DifferentSeeds_DifferentEndHashes()
    {
        var hashes = new HashSet<ulong>();
        for (ulong seed = 1; seed <= 4; seed++)
        {
            CrossMapQaScenario s = CrossMapQaScenario.Create(seed, 200, 12f)!;
            s.Run();
            Assert.True(hashes.Add(s.Sim.StateHash()), $"seed {seed} repeats an end hash");
        }
    }

    /// <summary>Wall-clock and allocation rows; run alone in <see cref="SerialCollection"/>.</summary>
    [Collection(SerialCollection.Name)]
    public class Serial
    {
        private readonly ITestOutputHelper _out;

        public Serial(ITestOutputHelper output) => _out = output;

        /// <summary>
        /// Tick cost of the whole crossing at 500 and 1,000 units. docs/03 budget: 500 units average
        /// under 4 ms, p99 under 8 ms (asserted at 500); 1,000 reported against the same numbers.
        /// </summary>
        [Theory]
        [Trait("Category", "Perf")]
        [InlineData(500, 16f)]
        [InlineData(1000, 20f)]
        public void Perf_CrossMap_WholeRun(int units, float radius)
        {
            // Warm the JIT on a small run first.
            CrossMapQaScenario.Create(2, 50, 8f)!.Run();
            CrossMapQaScenario s = CrossMapQaScenario.Create(1, units, radius)!;
            MoveScenario.MoveAll(s.Sim, s.Goal);
            var times = new List<double>();
            var sw = new Stopwatch();
            var u = s.Sim.World.Units;
            bool moving = true;
            while (moving && times.Count < s.LimitTicks)
            {
                sw.Restart();
                s.Sim.Tick();
                sw.Stop();
                times.Add(sw.Elapsed.TotalMilliseconds);
                moving = times.Count < 2;
                for (int i = 0; i < u.Capacity && !moving; i++) moving = u.Alive[i] && u.State[i] == Entities.UnitState.Moving;
            }
            times.Sort();
            double avg = times.Average();
            double p99 = times[(int)(times.Count * 0.99)];
            double max = times[^1];
            _out.WriteLine($"{units} units: {times.Count} ticks, avg {avg:F3} ms, p99 {p99:F3} ms, max {max:F3} ms");
            if (units <= 500)
            {
                Assert.True(avg < 4.0, $"avg {avg:F3} ms");
                Assert.True(p99 < 8.0, $"p99 {p99:F3} ms");
            }
        }

        /// <summary>A whole 1,000-unit crossing allocates 0 bytes (warm-up run first, then a fresh identical run measured).</summary>
        [Fact]
        public void OneThousand_WholeRun_AllocatesNothing()
        {
            CrossMapQaScenario warm = CrossMapQaScenario.Create(1, 1000, 20f)!;
            int ticks = warm.Run().Ticks;
            Simulation? sim = null;
            Action setup = () =>
            {
                CrossMapQaScenario s = CrossMapQaScenario.Create(1, 1000, 20f)!;
                MoveScenario.MoveAll(s.Sim, s.Goal);
                sim = s.Sim;
            };
            Action block = () =>
            {
                for (int t = 0; t < ticks; t++) sim!.Tick();
            };
            int runs = AllocationProbe.AssertZero(block, _out, setup);
            Assert.Equal(warm.Sim.StateHash(), sim!.StateHash());
            _out.WriteLine($"{ticks} ticks, {runs} run(s), 0 bytes");
        }
    }
}
