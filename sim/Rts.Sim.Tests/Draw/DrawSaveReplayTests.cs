using System.Buffers.Binary;
using Rts.Sim.Replays;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Draw;

/// <summary>
/// M6-S13 criterion 7: the draw state in a save (the <c>DRAW</c> part inside <c>WRLD</c>, present exactly when the data has a
/// draw rule and the clock or the drawn flag is not 0) and in a replay. A save taken mid-clock loads and draws on the same
/// tick with the same hashes (in process and in a second process); a save taken after the draw loads drawn; a forged part
/// is refused; a recorded match that draws replays checkpoint for checkpoint.
/// </summary>
public class DrawSaveReplayTests
{
    private readonly ITestOutputHelper _out;

    public DrawSaveReplayTests(ITestOutputHelper output) => _out = output;

    private const string ChildVariable = "RTS_DRAW_SAVE_CHILD";
    private const string ChildLine = "DRAW-SAVE ";

    /// <summary>The spent scene saved at clock <paramref name="clockAt"/>, the load and the unsaved run ticked side by side to the draw: (draw tick, final hash).</summary>
    private static (int DrawTick, ulong Hash) SaveMidClockAndFollow(int clockAt)
    {
        Simulation sim = DrawScenes.Spent(DrawScenes.Short);
        while (sim.World.DrawClock < clockAt) sim.Tick();
        byte[] bytes = SaveTestRun.Save(sim);
        Simulation loaded = SaveTestRun.Load(DrawScenes.Short, bytes);
        Assert.Equal(sim.StateHash(), loaded.StateHash());
        Assert.Equal(clockAt, loaded.World.DrawClock);
        while (!sim.MatchOver)
        {
            sim.Tick();
            loaded.Tick();
            Assert.True(sim.StateHash() == loaded.StateHash(), $"diverged at tick {sim.TickNumber}");
            Assert.Equal(sim.World.DrawClock, loaded.World.DrawClock);
        }
        Assert.True(loaded.MatchOver);
        Assert.True(sim.World.MatchDrawn && loaded.World.MatchDrawn);
        SaveTestRun.AssertSameState(sim, loaded, "at the draw");
        return (sim.TickNumber, sim.StateHash());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(40)]
    [InlineData(DrawScenes.ShortTicks - 1)]
    public void ASaveTakenMidClock_DrawsOnTheSameTick_WithTheSameHashes(int clockAt)
    {
        (int tick, ulong hash) = SaveMidClockAndFollow(clockAt);
        _out.WriteLine($"saved at clock {clockAt}: drawn at tick {tick}, hash {hash:X16}");
    }

    /// <summary>The same through two processes (<see cref="MapFileTests.RunChild"/>): the child saves, loads and plays to the draw; both print the draw tick and the hash.</summary>
    [Fact]
    public void ASaveTakenMidClock_TwoProcesses_SameDrawTickAndHash()
    {
        (int tick, ulong hash) = SaveMidClockAndFollow(40);
        string mine = $"{tick} {hash:X16}";
        if (Environment.GetEnvironmentVariable(ChildVariable) == "1")
        {
            _out.WriteLine(ChildLine + mine);
            return;
        }
        string child = MapFileTests.RunChild(typeof(DrawSaveReplayTests), nameof(ASaveTakenMidClock_TwoProcesses_SameDrawTickAndHash), ChildVariable, ChildLine);
        _out.WriteLine($"this process {mine}; the child {child}");
        Assert.Equal(mine, child);
    }

    [Fact]
    public void ASaveTakenAfterTheDraw_LoadsOverAndDrawn()
    {
        Simulation sim = DrawScenes.Spent(DrawScenes.Short);
        DrawScenes.RunToEnd(sim, 2 * DrawScenes.ShortTicks);
        for (int t = 0; t < 10; t++) sim.Tick();
        Assert.True(sim.World.MatchDrawn);
        Simulation loaded = SaveTestRun.Load(DrawScenes.Short, SaveTestRun.Save(sim));
        Assert.True(loaded.MatchOver);
        Assert.True(loaded.World.MatchDrawn);
        Assert.Equal(sim.World.DrawClock, loaded.World.DrawClock);
        Assert.Equal(sim.StateHash(), loaded.StateHash());
        for (int t = 0; t < 50; t++)
        {
            sim.Tick();
            loaded.Tick();
            Assert.Equal(sim.StateHash(), loaded.StateHash());
        }
        Assert.True(loaded.World.MatchDrawn);
    }

    /// <summary>"Zero is free": with the clock at 0 the save has no DRAW part, and on data without the rule never.</summary>
    [Fact]
    public void TheDrawPart_IsWrittenOnlyWhileTheClockRunsOrAfterTheDraw()
    {
        Simulation sim = DrawScenes.Spent(DrawScenes.Short);
        Assert.Equal(-1, DrawAt(SaveTestRun.Save(sim)));
        sim.Tick();
        Assert.True(DrawAt(SaveTestRun.Save(sim)) > 0);
        Simulation off = DrawScenes.Spent(DrawScenes.Off);
        for (int t = 0; t < 3 * DrawScenes.ShortTicks; t++) off.Tick();
        Assert.Equal(0, off.World.DrawClock);
        Assert.Equal(-1, DrawAt(SaveTestRun.Save(off)));
    }

    public static TheoryData<string> Forgeries() => new() { "clock -1", "clock above the wait", "clock at the wait, not drawn", "drawn, not over", "drawn below the wait", "empty part", "drawn byte 2" };

    [Theory]
    [MemberData(nameof(Forgeries))]
    public void AForgedDrawPart_IsRefused(string forgery)
    {
        Simulation sim = DrawScenes.Spent(DrawScenes.Short);
        int wait = sim.World.DrawWaitTicks;
        bool afterDraw = forgery.StartsWith("drawn", StringComparison.Ordinal);
        if (afterDraw) DrawScenes.RunToEnd(sim, 2 * wait);
        else for (int t = 0; t < 10; t++) sim.Tick();
        byte[] bytes = SaveTestRun.Save(sim);
        int at = DrawAt(bytes);
        Assert.True(at > 0);
        int clock = at + 4, drawn = at + 8, latched = at - 1;
        switch (forgery)
        {
            case "clock -1": BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(clock), -1); break;
            case "clock above the wait": BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(clock), wait + 1); break;
            case "clock at the wait, not drawn": BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(clock), wait); break;
            case "drawn, not over": bytes[latched] = 0; break;
            case "drawn below the wait": BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(clock), wait - 1); break;
            case "empty part": BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(clock), 0); break;
            case "drawn byte 2": bytes[drawn] = 2; break;
        }
        SaveError e = SaveTestRun.TryLoad(DrawScenes.Short, SaveBytes.Rechecksum(bytes));
        _out.WriteLine($"{forgery}: {e}");
        Assert.Equal(SaveError.Malformed, e);
    }

    /// <summary>A save made on data without the rule carries no part, so a DRAW part spliced in is refused too (the reader meets a tag it does not expect).</summary>
    [Fact]
    public void ADrawPart_OnDataWithoutTheRule_IsRefused()
    {
        Simulation on = DrawScenes.Spent(DrawScenes.Short);
        for (int t = 0; t < 10; t++) on.Tick();
        byte[] withPart = SaveTestRun.Save(on);
        int at = DrawAt(withPart);
        // The same bytes on data with no rule: the header's data hash differs first, so forge the hash too.
        byte[] forged = (byte[])withPart.Clone();
        SaveBytes.Offsets o = SaveBytes.HeaderOffsets(forged);
        BinaryPrimitives.WriteUInt64LittleEndian(forged.AsSpan(o.DataHash), DrawScenes.Off.ContentHash());
        Assert.True(at > 0);
        Assert.Equal(SaveError.Malformed, SaveTestRun.TryLoad(DrawScenes.Off, SaveBytes.Rechecksum(forged)));
    }

    /// <summary>A recorded match built by commands that draws replays to the same draw tick with every checkpoint equal.</summary>
    [Fact]
    public void ARecordedMatchThatDraws_ReplaysCheckpointForCheckpoint()
    {
        Simulation sim = DrawScenes.SpentByCommands(DrawScenes.ShortBroke, 3, 2, 2, true, out ReplayRecorder? rec);
        DrawScenes.RunToEnd(sim, 10 * DrawScenes.ShortTicks);
        Assert.True(sim.World.MatchDrawn, $"no draw by tick {sim.TickNumber}");
        int drawTick = sim.TickNumber;
        for (int t = 0; t < 30; t++) sim.Tick();
        Replay replay = rec!.ToReplay();
        Assert.True(replay.Checkpoints.Length > 2);
        ReplayResult r = ReplayPlayer.Run(replay, DrawScenes.ShortBroke);
        _out.WriteLine($"drawn at {drawTick}; replay {r.Error} at {r.Tick}, {replay.Checkpoints.Length} checkpoints");
        Assert.True(r.Ok, $"{r.Error} at {r.Tick}");
        Assert.Equal(sim.TickNumber, r.TicksRun);
    }

    /// <summary>Offset of the DRAW tag in a save (after the WRLD tag), or -1.</summary>
    internal static int DrawAt(byte[] bytes)
    {
        int world = bytes.AsSpan().IndexOf("WRLD"u8);
        Assert.True(world > 0);
        int rng = bytes.AsSpan(world).IndexOf("RNGS"u8);
        int at = bytes.AsSpan(world, rng).IndexOf("DRAW"u8);
        return at < 0 ? -1 : world + at;
    }
}
