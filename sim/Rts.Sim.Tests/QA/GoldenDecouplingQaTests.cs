using Rts.Sim.Replays;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA, M6-SH1 (session 2026-11-05-2148): the golden replay's pin is its <c>k</c> lines, not its <c>data-hash</c> header.
/// The checked-in file is rewritten in memory and resealed (the file itself is never touched): with its header junked it
/// still plays every checkpoint the way the decoupled tests play it (<see cref="ReplayTestRun.WithShippedDataHash"/>), and
/// as recorded the product refuses it (<see cref="ReplayError.DataMismatch"/>); with one <c>k</c> line edited the decoupled
/// play fails at that checkpoint. So the decoupling neither hides a moved game nor makes the product play a foreign file.
/// </summary>
public class GoldenDecouplingQaTests
{
    private static string Golden() => File.ReadAllText(ReplayTestRun.GoldenPath).Replace("\r\n", "\n");

    /// <summary>The golden's body with one line replaced, resealed (a fresh checksum), read back.</summary>
    private static Replay Rewritten(Func<string, string?> replaceLine)
    {
        string text = Golden();
        string body = text[..text.LastIndexOf("checksum ", StringComparison.Ordinal)];
        string[] lines = body.TrimEnd('\n').Split('\n');
        int changed = 0;
        for (int k = 0; k < lines.Length; k++)
        {
            string? to = replaceLine(lines[k]);
            if (to == null) continue;
            lines[k] = to;
            changed++;
        }
        Assert.Equal(1, changed);
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Seal(string.Join('\n', lines) + "\n"), out Replay? r));
        return r!;
    }

    [Theory]
    [InlineData("0000000000000000")]
    [InlineData("DEADBEEFDEADBEEF")]
    [InlineData("FFFFFFFFFFFFFFFF")]
    public void AJunkDataHashLine_StillPlaysEveryCheckpoint_Decoupled_AndIsRefusedAsRecorded(string junk)
    {
        Replay r = Rewritten(l => l.StartsWith("data-hash ", StringComparison.Ordinal) ? "data-hash " + junk : null);
        Assert.Equal(Convert.ToUInt64(junk, 16), r.DataHash);
        ReplayResult played = ReplayPlayer.Run(ReplayTestRun.WithShippedDataHash(r), TestSim.Data);
        Assert.True(played.Ok, played.ToString());
        Assert.Equal(1500, played.TicksRun);
        ReplayResult asRecorded = ReplayPlayer.Run(r, TestSim.Data);
        Assert.Equal(ReplayError.DataMismatch, asRecorded.Error);
        Assert.Equal(0, asRecorded.TicksRun);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(800)]
    [InlineData(1500)]
    public void OneKLineEdited_FailsTheDecoupledPlay_AtThatCheckpoint(int tick)
    {
        string prefix = $"k {tick} ";
        Replay r = Rewritten(l =>
        {
            if (!l.StartsWith(prefix, StringComparison.Ordinal)) return null;
            char last = l[^1];
            return l[..^1] + (last == '0' ? '1' : '0');
        });
        ReplayResult played = ReplayPlayer.Run(ReplayTestRun.WithShippedDataHash(r), TestSim.Data);
        Assert.Equal(ReplayError.CheckpointMismatch, played.Error);
        Assert.Equal(tick, played.Tick);
    }

    /// <summary>The checked-in golden's header is still the recording-time hash, and the shipped hash has moved off it (the brief's proof).</summary>
    [Fact]
    public void TheCheckedInHeader_IsTheRecordingTimeHash_NotTheShippedOne()
    {
        Assert.Equal(ReplayError.None, ReplayFormat.TryReadFile(ReplayTestRun.GoldenPath, out Replay? r));
        Assert.Equal(0x30C51BB619A7E650UL, r!.DataHash);
        Assert.NotEqual(r.DataHash, TestSim.Data.ContentHash());
    }
}
