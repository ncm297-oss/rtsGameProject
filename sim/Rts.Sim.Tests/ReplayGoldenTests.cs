using Rts.Sim.Replays;

namespace Rts.Sim.Tests;

/// <summary>
/// M1-6: the checked-in golden replay (docs/03 "Testing strategy") must reproduce every checkpoint.
/// A deliberate change to outcomes (movement, tick order, RNG use, data that this run uses) breaks it: rerun with
/// <c>RTS_REGEN_GOLDEN=1</c> to rewrite it, then without, and say why in the commit message.
/// M6-SH1: the file's <c>data-hash</c> line is the shipped data's hash at recording time, not a pin. The run plays the file
/// with that header replaced by the shipped hash, so the <c>k</c> lines are the pin: a data change that doesn't move them
/// (an AI knob, a faction this run doesn't spawn) needs no regeneration. The product's refusal of a recording made with
/// other data is pinned by <see cref="TheGoldenAsRecorded_IsRefusedForItsDataHash_WhenTheShippedHashHasMoved"/>.
/// </summary>
public class ReplayGoldenTests
{
    /// <summary>The env var that makes the golden test rewrite the file (and fail, so a regen is never silent).</summary>
    public const string RegenFlag = "RTS_REGEN_GOLDEN";

    [Fact]
    public void CrossMapSeed1_ReproducesEveryCheckpoint()
    {
        string path = ReplayTestRun.GoldenPath;
        if (Environment.GetEnvironmentVariable(RegenFlag) == "1")
        {
            ReplayFormat.WriteFile(ReplayTestRun.RecordGolden(), path);
            Assert.Fail("golden regenerated; rerun without the flag");
        }

        Assert.True(File.Exists(path), $"missing {path}; run once with {RegenFlag}=1");
        byte[] bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length < 64 * 1024, $"golden is {bytes.Length} bytes");
        Assert.All(bytes, b => Assert.True(b == (byte)'\n' || (b >= 0x20 && b <= 0x7E), "golden is not LF ASCII text"));

        ReplayError read = ReplayFormat.TryRead(bytes, out Replay? replay);
        Assert.True(read == ReplayError.None, $"golden does not read: {read}");
        Assert.Equal(1UL, replay!.Seed);
        Assert.Equal(1500, replay.TickCount);
        Assert.Equal(100, replay.CheckpointInterval);
        Assert.Equal(15, replay.Checkpoints.Length);
        Assert.Equal(ScenarioTests.Army, replay.UnitCapacity);
        Assert.Equal(2 * ScenarioTests.Army, replay.Commands.Length); // one spawn and one Move per unit

        // M6-SH1: the header is the hash at recording time; the checkpoints decide.
        ReplayResult result = ReplayPlayer.Run(ReplayTestRun.WithShippedDataHash(replay), TestSim.Data);
        Assert.True(result.Ok,
            $"golden replay: {result.Error} at tick {result.Tick} (expected {result.ExpectedHash:X16}, got {result.ActualHash:X16}). " +
            $"If the change is deliberate, rerun with {RegenFlag}=1, then without, and explain it in the commit.");
        Assert.Equal(1500, result.TicksRun);
    }

    /// <summary>
    /// M6-SH1: played as recorded (its own header, no substitution), the golden is refused before tick 1 with
    /// <see cref="ReplayError.DataMismatch"/> whenever the shipped hash differs from the header: a recording made with other
    /// data is not played. When they are equal (the golden was just regenerated) it plays.
    /// </summary>
    [Fact]
    public void TheGoldenAsRecorded_IsRefusedForItsDataHash_WhenTheShippedHashHasMoved()
    {
        Assert.Equal(ReplayError.None, ReplayFormat.TryReadFile(ReplayTestRun.GoldenPath, out Replay? replay));
        ReplayResult result = ReplayPlayer.Run(replay!, TestSim.Data);
        if (replay!.DataHash == TestSim.Data.ContentHash())
        {
            Assert.True(result.Ok, $"{result.Error} at tick {result.Tick}");
            return;
        }
        Assert.Equal(ReplayError.DataMismatch, result.Error);
        Assert.Equal(0, result.TicksRun);
    }

    /// <summary>M6-SH1: the refusal does not hang on the shipped hash having moved: the golden with any other header is refused too.</summary>
    [Fact]
    public void TheGoldenWithAForeignDataHash_IsRefused()
    {
        Assert.Equal(ReplayError.None, ReplayFormat.TryReadFile(ReplayTestRun.GoldenPath, out Replay? replay));
        ReplayResult result = ReplayPlayer.Run(ReplayTestRun.With(replay!, dataHash: ~TestSim.Data.ContentHash()), TestSim.Data);
        Assert.Equal(ReplayError.DataMismatch, result.Error);
        Assert.Equal(0, result.TicksRun);
    }

    [Fact]
    public void RecordingTheGoldenRunTwice_GivesTheSameBytes()
    {
        Assert.Equal(ReplayFormat.Write(ReplayTestRun.RecordGolden()), ReplayFormat.Write(ReplayTestRun.RecordGolden()));
    }
}
