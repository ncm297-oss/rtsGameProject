using Rts.Sim.Replays;

namespace Rts.Sim.Tests;

/// <summary>
/// M1-6: the checked-in golden replay (docs/03 "Testing strategy") must reproduce every checkpoint.
/// A deliberate change to outcomes (movement, tick order, RNG use, data) breaks it: rerun with
/// <c>RTS_REGEN_GOLDEN=1</c> to rewrite it, then without, and say why in the commit message.
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

        ReplayResult result = ReplayPlayer.Run(replay, TestSim.Data);
        Assert.True(result.Ok,
            $"golden replay: {result.Error} at tick {result.Tick} (expected {result.ExpectedHash:X16}, got {result.ActualHash:X16}). " +
            $"If the change is deliberate, rerun with {RegenFlag}=1, then without, and explain it in the commit.");
        Assert.Equal(1500, result.TicksRun);
    }

    [Fact]
    public void RecordingTheGoldenRunTwice_GivesTheSameBytes()
    {
        Assert.Equal(ReplayFormat.Write(ReplayTestRun.RecordGolden()), ReplayFormat.Write(ReplayTestRun.RecordGolden()));
    }
}
