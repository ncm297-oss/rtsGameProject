using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Replays;

namespace Rts.Sim.Tests;

/// <summary>M1-6: the recorder captures what the sim accepted, and the player re-runs it, checks every stamp and checkpoint, and refuses bad input before any tick.</summary>
public class ReplayPlayerTests
{
    private static readonly Lazy<(Simulation Sim, Replay Replay)> s_run = new(() =>
    {
        (Simulation sim, ReplayRecorder rec) = ReplayTestRun.RecordSmall(seed: 9, ticks: 450, checkpointInterval: 50);
        return (sim, rec.ToReplay());
    });

    private static Replay Recorded => s_run.Value.Replay;

    [Fact]
    public void Recorder_CapturesStampsAndCheckpoints()
    {
        (Simulation sim, Replay r) = s_run.Value;
        Assert.Equal(450, r.TickCount);
        Assert.Equal(9, r.Checkpoints.Length);
        for (int i = 0; i < r.Checkpoints.Length; i++) Assert.Equal(50 * (i + 1), r.Checkpoints[i].Tick);
        Assert.Equal(sim.StateHash(), r.Checkpoints[^1].Hash); // the last checkpoint is the final tick, nothing queued since
        Assert.Equal(TestSim.Data.ContentHash(), r.DataHash);
        Assert.Equal(SimInfo.Version, r.SimVersion);
        Assert.Equal(9UL, r.Seed);
        Assert.Equal(64, r.UnitCapacity);
        Assert.Equal(128, r.CommandCapacity);
        // Spawns were queued before tick 0 ran: stamped tick 1, sequences 0.. per player.
        Assert.Equal(1, r.Commands[0].Tick);
        Assert.Equal(0, r.Commands[0].Sequence);
        Assert.Equal(0, r.Commands[1].Sequence); // player 1's first
        Assert.Equal(1, r.Commands[2].Sequence);
        Assert.Equal(ReplayError.None, r.Validate());
    }

    [Fact]
    public void Playback_MatchesEveryCheckpoint()
    {
        ReplayResult result = ReplayPlayer.Run(Recorded, TestSim.Data);
        Assert.True(result.Ok, result.ToString());
        Assert.Equal(450, result.TicksRun);
    }

    [Fact]
    public void Playback_ReStampsEveryCommandExactlyAsLogged()
    {
        // Re-record the playback by hand: the same feeding rule, a recorder on the new sim.
        Replay r = Recorded;
        var sim = new Simulation(new SimConfig(r.Seed, r.PlayerCount, r.UnitCapacity, r.CommandCapacity) { Data = TestSim.Data, Map = r.Map });
        var rec = new ReplayRecorder(sim, r.CheckpointInterval);
        int next = 0;
        while (true)
        {
            while (next < r.Commands.Length && r.Commands[next].Tick == sim.TickNumber + 1)
            {
                Command c = r.Commands[next++];
                c.Tick = -5; // the sim restamps both fields
                c.Sequence = 1234;
                sim.Enqueue(c);
            }
            if (sim.TickNumber == r.TickCount) break;
            sim.Tick();
        }
        ReplayTestRun.AssertEqual(r, rec.ToReplay());
    }

    [Fact]
    public void TamperedCheckpoint_ReportsThatTickAndBothHashes()
    {
        Replay r = Recorded;
        var cps = r.Checkpoints.ToArray();
        cps[3] = cps[3] with { Hash = cps[3].Hash ^ 1 };
        ReplayResult result = ReplayPlayer.Run(ReplayTestRun.With(r, checkpoints: cps), TestSim.Data);
        Assert.Equal(ReplayError.CheckpointMismatch, result.Error);
        Assert.Equal(200, result.Tick);
        Assert.Equal(cps[3].Hash, result.ExpectedHash);
        Assert.Equal(r.Checkpoints[3].Hash, result.ActualHash);
    }

    [Fact]
    public void ChangedCommand_DesyncsAtTheNextCheckpoint()
    {
        Replay r = Recorded;
        var commands = r.Commands.ToArray();
        int i = Array.FindIndex(commands, c => c.Kind == CommandKind.Move && c.Tick > 100 && float.IsFinite(c.Position.X));
        commands[i].Position += new Vector2(2f, 0f);
        ReplayResult result = ReplayPlayer.Run(ReplayTestRun.With(r, commands: commands), TestSim.Data);
        Assert.Equal(ReplayError.CheckpointMismatch, result.Error);
        Assert.Equal((commands[i].Tick + 49) / 50 * 50, result.Tick);
    }

    [Fact]
    public void DataWithOneStatChanged_IsRefusedBeforeAnyTick()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "hp", "999");
        DataLoadResult load = DataLoader.LoadAll(dir.Path);
        Assert.True(load.Ok, string.Join("\n", load.Errors));
        Assert.NotEqual(TestSim.Data.ContentHash(), load.Data!.ContentHash());

        ReplayResult result = ReplayPlayer.Run(Recorded, load.Data!);
        Assert.Equal(ReplayError.DataMismatch, result.Error);
        Assert.Equal(0, result.TicksRun);
        Assert.Equal(-1, result.Tick);
    }

    [Fact]
    public void OtherFormatVersion_IsRefusedBeforeAnyTick()
    {
        foreach (int version in new[] { 0, Replay.OldestFormatVersion - 1, Replay.CurrentFormatVersion + 1, -1 })
        {
            ReplayResult result = ReplayPlayer.Run(ReplayTestRun.With(Recorded, formatVersion: version), TestSim.Data);
            Assert.Equal(ReplayError.FormatVersionMismatch, result.Error);
            Assert.Equal(0, result.TicksRun);
        }
    }

    [Fact]
    public void InvalidReplay_IsRefusedBeforeAnyTick()
    {
        var commands = Recorded.Commands.ToArray();
        commands[0].Player = 2;
        ReplayResult result = ReplayPlayer.Run(ReplayTestRun.With(Recorded, commands: commands), TestSim.Data);
        Assert.Equal(ReplayError.InvalidCommand, result.Error);
        Assert.Equal(0, result.TicksRun);
    }

    [Fact]
    public void ToReplay_LeavesOutCommandsQueuedForTheNextTick()
    {
        var sim = new Simulation(TestSim.Config(Seed: 3, PlayerCount: 1, UnitCapacity: 4, CommandCapacity: 4));
        var rec = new ReplayRecorder(sim, checkpointInterval: 2);
        sim.Enqueue(Command.Noop(0));
        for (int t = 0; t < 4; t++) sim.Tick();
        sim.Enqueue(Command.SpawnUnit(0, 0, new Vector2(10f, 10f)));
        Replay r = rec.ToReplay();
        Assert.Equal(2, rec.CommandCount);
        Assert.Single(r.Commands);
        Assert.Equal(4, r.TickCount);
        Assert.Equal(2, r.Checkpoints.Length);
        Assert.True(ReplayPlayer.Run(r, TestSim.Data).Ok);
    }

    [Fact]
    public void EmptyReplay_PlaysZeroTicks()
    {
        var sim = new Simulation(TestSim.Config(Seed: 3, PlayerCount: 1, UnitCapacity: 4, CommandCapacity: 4));
        Replay r = new ReplayRecorder(sim).ToReplay();
        Assert.Equal(0, r.TickCount);
        ReplayResult result = ReplayPlayer.Run(r, TestSim.Data);
        Assert.True(result.Ok);
        Assert.Equal(0, result.TicksRun);
    }

    [Fact]
    public void Recorder_AttachesOnlyToAFreshGeneratedSim_AndOnlyOnce()
    {
        var ticked = new Simulation(TestSim.Config(Seed: 3, PlayerCount: 1, UnitCapacity: 4, CommandCapacity: 4));
        ticked.Tick();
        Assert.Throws<InvalidOperationException>(() => new ReplayRecorder(ticked));

        var queued = new Simulation(TestSim.Config(Seed: 3, PlayerCount: 1, UnitCapacity: 4, CommandCapacity: 4));
        queued.Enqueue(Command.Noop(0));
        Assert.Throws<InvalidOperationException>(() => new ReplayRecorder(queued));

        var fresh = new Simulation(TestSim.Config(Seed: 3, PlayerCount: 1, UnitCapacity: 4, CommandCapacity: 4));
        _ = new ReplayRecorder(fresh);
        Assert.Throws<InvalidOperationException>(() => new ReplayRecorder(fresh));

        var handMade = new Simulation(TestSim.Config(Seed: 3, PlayerCount: 1, UnitCapacity: 4, CommandCapacity: 4), LocalMovementTests.Flat(16));
        Assert.Throws<InvalidOperationException>(() => new ReplayRecorder(handMade));

        Assert.Throws<ArgumentOutOfRangeException>(() => new ReplayRecorder(
            new Simulation(TestSim.Config(Seed: 3, PlayerCount: 1, UnitCapacity: 4, CommandCapacity: 4)), checkpointInterval: 0));
    }

    [Fact]
    public void Recorder_DoesNotCountACommandTheFullQueueRefused()
    {
        var sim = new Simulation(TestSim.Config(Seed: 3, PlayerCount: 1, UnitCapacity: 4, CommandCapacity: 1));
        var rec = new ReplayRecorder(sim);
        sim.Enqueue(Command.Noop(0));
        Assert.Throws<InvalidOperationException>(() => sim.Enqueue(Command.Noop(0)));
        Assert.Equal(1, rec.CommandCount);
    }

    [Fact]
    public void Recorder_GrowsPastItsCapacities()
    {
        var sim = new Simulation(TestSim.Config(Seed: 3, PlayerCount: 1, UnitCapacity: 4, CommandCapacity: 8));
        var rec = new ReplayRecorder(sim, checkpointInterval: 1, tickCapacity: 2, commandCapacity: 1);
        for (int t = 0; t < 10; t++)
        {
            sim.Enqueue(Command.Noop(0));
            sim.Enqueue(Command.Noop(0));
            sim.Tick();
        }
        Assert.Equal(20, rec.CommandCount);
        Assert.Equal(10, rec.CheckpointCount);
        Assert.True(ReplayPlayer.Run(rec.ToReplay(), TestSim.Data).Ok);
    }
}
