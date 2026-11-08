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

    /// <summary>
    /// The checked-in golden's header is a data hash the reader parses, whatever its value: the <c>k</c> lines are the pin.
    /// M5-H5 (BUG-1059): no literal and no "differs from the shipped hash" half any more, so a deliberate regen touches the
    /// replay file alone (the literal re-coupled every regen to this test, and the inequality was red in the session that
    /// regenerated it). That the header is informational is proved by <see cref="AJunkDataHashLine_StillPlaysEveryCheckpoint_Decoupled_AndIsRefusedAsRecorded"/>.
    /// </summary>
    [Fact]
    public void TheCheckedInHeader_ParsesAsADataHash_WhateverItsValue()
    {
        Assert.Equal(ReplayError.None, ReplayFormat.TryReadFile(ReplayTestRun.GoldenPath, out Replay? r));
        Assert.NotEqual(0UL, r!.DataHash);
        ReplayResult played = ReplayPlayer.Run(ReplayTestRun.WithShippedDataHash(r), TestSim.Data);
        Assert.True(played.Ok, played.ToString());
    }
}
