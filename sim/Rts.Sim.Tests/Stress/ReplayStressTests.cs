using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Replays;
using Rts.Sim.Tests.QA;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>QA (M1-6): replays at volume: many recorded random-command runs, a large march, and the recorder's allocation at 2,500 units.</summary>
public class ReplayStressTests
{
    private readonly ITestOutputHelper _out;

    public ReplayStressTests(ITestOutputHelper output) => _out = output;

    /// <summary>Seeds 1-50, 500 ticks of fuzzed commands each: write, read back, play, every checkpoint matches.</summary>
    [Fact]
    public void FiftyRandomCommandRuns_Seeds1To50_500Ticks_AllReproduce()
    {
        long bytes = 0;
        int commands = 0;
        for (ulong seed = 1; seed <= 50; seed++)
        {
            (Simulation sim, ReplayRecorder rec) = ReplayQaTests.RecordRandom(seed, ticks: 500, checkpointInterval: 50, twin: seed <= 10);
            Replay r = rec.ToReplay();
            byte[] file = ReplayFormat.Write(r);
            bytes += file.Length;
            commands += r.Commands.Length;
            Assert.Equal(ReplayError.None, ReplayFormat.TryRead(file, out Replay? back));
            Assert.Equal(file, ReplayFormat.Write(back!));
            ReplayResult res = ReplayPlayer.Run(back!, TestSim.Data);
            Assert.True(res.Ok, $"seed {seed}: {res}");
            Assert.Equal(500, res.TicksRun);
            Assert.Equal(sim.StateHash(), r.Checkpoints[^1].Hash);
        }
        _out.WriteLine($"50 runs: {commands} commands, {bytes} bytes total ({bytes / 50} per file)");
    }

    /// <summary>Different seeds differ by tick 100, over 50 seeds with the same command script shape.</summary>
    [Fact]
    public void FiftySeeds_AllCheckpointHashesDistinctAtTick100()
    {
        var first = new HashSet<ulong>();
        for (ulong seed = 1; seed <= 50; seed++)
        {
            Replay r = ReplayQaTests.RecordRandom(seed, ticks: 100, twin: false).Recorder.ToReplay();
            Assert.True(first.Add(r.Checkpoints[0].Hash), $"seed {seed} repeats another seed's tick-100 hash");
        }
    }

    /// <summary>Report: file size and record/playback time for a 1,000-unit, 2,500-tick march across the map.</summary>
    [Fact]
    public void Report_1000UnitMarch_2500Ticks_FileSize_AndItReplays()
    {
        ReplayRecorder? rec = null;
        var sw = Stopwatch.StartNew();
        CrossMapScenario s = CrossMapScenario.Create(1, 1000, 20f, onCreated: sim => rec = new ReplayRecorder(sim));
        s.OrderAll();
        // A second order halfway, so the log has more than one wave.
        while (s.Sim.TickNumber < 1250) s.Sim.Tick();
        MoveScenario.MoveAll(s.Sim, MoveScenario.Center(s.Sim.World.NavGrid, s.StartCell));
        while (s.Sim.TickNumber < 2500) s.Sim.Tick();
        double recordMs = sw.Elapsed.TotalMilliseconds;
        Replay r = rec!.ToReplay();
        byte[] file = ReplayFormat.Write(r);
        Assert.Equal(25, r.Checkpoints.Length);
        Assert.Equal(3000, r.Commands.Length);
        sw.Restart();
        ReplayResult res = ReplayPlayer.Run(r, TestSim.Data);
        double playMs = sw.Elapsed.TotalMilliseconds;
        Assert.True(res.Ok, res.ToString());
        _out.WriteLine($"1,000 units, 2,500 ticks: {r.Commands.Length} commands, {r.Checkpoints.Length} checkpoints, " +
                       $"{file.Length} bytes ({file.Length / 1024.0:F1} KiB); record {recordMs:F0} ms, playback {playMs:F0} ms");
        Assert.True(file.Length < 512 * 1024, $"{file.Length} bytes");
    }

    [Collection(SerialCollection.Name)]
    public class Serial
    {
        private readonly ITestOutputHelper _out;

        public Serial(ITestOutputHelper output) => _out = output;

        /// <summary>Criterion 7 at 5x the budget: 2,500 units moving, recorder attached, 500 ticks incl. 5 checkpoints, 0 bytes.</summary>
        [Fact]
        public void Recorder_2500UnitsMoving_500TicksWith5Checkpoints_AllocatesNothing()
        {
            ReplayRecorder? rec = null;
            CrossMapScenario s = CrossMapScenario.Create(2, 2500, 30f, onCreated: sim => rec = new ReplayRecorder(sim));
            s.OrderAll();
            while (s.Sim.TickNumber < 102) s.Sim.Tick(); // warm, past the first checkpoint
            Assert.Equal(1, rec!.CheckpointCount);
            Simulation sim = s.Sim;
            Vector2 back = MoveScenario.Center(sim.World.NavGrid, s.StartCell);
            int run = 0;
            Action order = () => MoveScenario.MoveAll(sim, run++ % 2 == 0 ? back : s.Goal);
            int movingTicks = 0;
            Entities.UnitState[] state = sim.World.Units.State;
            Action ticks = () =>
            {
                for (int t = 0; t < 500; t++)
                {
                    sim.Tick();
                    int moving = 0;
                    for (int i = 0; i < state.Length; i++) if (state[i] == Entities.UnitState.Moving) moving++;
                    if (moving >= 1000) movingTicks++;
                }
            };
            int before = rec.CheckpointCount;
            int runs = AllocationProbe.AssertZero(ticks, _out, setup: order);
            Assert.Equal(before + 5 * runs, rec.CheckpointCount);
            _out.WriteLine($"ticks with >= 1,000 of 2,500 units moving: {movingTicks} of {500 * runs}; {runs} measured run(s)");
            Assert.True(movingTicks >= 100 * runs, "the measurement is not representative: the army stood still");
        }
    }
}
