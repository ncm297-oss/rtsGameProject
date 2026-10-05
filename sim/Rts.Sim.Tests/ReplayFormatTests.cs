using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using Rts.Sim.Commands;
using Rts.Sim.Map;
using Rts.Sim.Replays;

namespace Rts.Sim.Tests;

/// <summary>M1-6: the replay text format: exact round trip, and every corrupt or invalid file refused with a code, never an exception.</summary>
public class ReplayFormatTests
{
    private static readonly Lazy<Replay> s_small = new(() => ReplayTestRun.RecordSmall(seed: 5, ticks: 300).Recorder.ToReplay());

    private static Replay Small => s_small.Value;

    private static string Text(Replay r) => Encoding.ASCII.GetString(ReplayFormat.Write(r));

    /// <summary>Re-seals edited text: drops the old checksum line and appends a valid one, so only the content check can refuse it.</summary>
    private static byte[] Reseal(string text)
    {
        string body = text[..(text.LastIndexOf("checksum ", StringComparison.Ordinal))];
        return ReplayFormat.Seal(body);
    }

    private static ReplayError Read(byte[] bytes)
    {
        ReplayError e = ReplayFormat.TryRead(bytes, out Replay? r);
        Assert.True((e == ReplayError.None) == (r != null), $"{e} with replay {(r == null ? "null" : "set")}");
        return e;
    }

    [Fact]
    public void RoundTrip_300TicksWithSpawnsAndMoves_ParsesEqualFieldForField_AndReplays()
    {
        Replay original = Small;
        Assert.Equal(300, original.TickCount);
        Assert.Equal(3, original.Checkpoints.Length);
        Assert.Contains(original.Commands, c => c.Kind == CommandKind.SpawnUnit);
        Assert.Contains(original.Commands, c => c.Kind == CommandKind.Move);
        Assert.Contains(original.Commands, c => c.Kind == CommandKind.Noop);
        Assert.Contains(original.Commands, c => float.IsNaN(c.Position.X));

        byte[] bytes = ReplayFormat.Write(original);
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(bytes, out Replay? parsed));
        ReplayTestRun.AssertEqual(original, parsed!);
        Assert.Equal(bytes, ReplayFormat.Write(parsed!)); // and writes back byte for byte

        ReplayResult played = ReplayPlayer.Run(parsed!, TestSim.Data);
        Assert.True(played.Ok, played.ToString());
        Assert.Equal(300, played.TicksRun);
    }

    [Fact]
    public void RoundTrip_ThroughAFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"rts-replay-{Environment.ProcessId}-{Environment.CurrentManagedThreadId}.replay");
        try
        {
            ReplayFormat.WriteFile(Small, path);
            Assert.Equal(ReplayError.None, ReplayFormat.TryReadFile(path, out Replay? parsed));
            ReplayTestRun.AssertEqual(Small, parsed!);
        }
        finally
        {
            File.Delete(path);
        }
        Assert.Equal(ReplayError.Unreadable, ReplayFormat.TryReadFile(path, out Replay? missing));
        Assert.Null(missing);
    }

    [Fact]
    public void Format_IsLfAsciiText_FloatsAsBitPatterns()
    {
        string text = Text(Small);
        Assert.DoesNotContain('\r', text);
        Assert.All(text, ch => Assert.True(ch == '\n' || (ch >= ' ' && ch <= '~')));
        Assert.StartsWith("rts-replay 1\nsim-version " + SimInfo.Version + "\n", text);
        Assert.Contains("\nmap.min-passable-fraction 3F000000\n", text); // 0.5f
        Assert.Contains($" {(uint)BitConverter.SingleToInt32Bits(float.NaN):X8} ", text); // the NaN Move's x, as its bits (FFC00000)
        Assert.EndsWith("\n", text);
    }

    [Fact]
    public void EveryMapGenParam_RoundTrips()
    {
        var map = new MapGenParams
        {
            Width = 100, Height = 96, EdgeMargin = 5, Level1Plateaus = 4, Level1MinSize = 15, Level1MaxSize = 30,
            Level2Plateaus = 3, Level2MinSize = 8, Level2MaxSize = 12, Level2Inset = 3, RampWidth = 4, RampLength = 5,
            RampsPerPlateau = 3, RampTries = 40, MinPassableFraction = 0.4375f, MaxAttempts = 7,
        };
        map.Validate();
        // A new MapGenParams property left at its default here fails: add it to the format and this list.
        foreach (PropertyInfo p in typeof(MapGenParams).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.Name == "EqualityContract") continue;
            Assert.True(!Equals(p.GetValue(map), p.GetValue(MapGenParams.Default)), $"{p.Name} is at its default; ReplayFormat may not cover it");
        }
        Replay r = new()
        {
            SimVersion = SimInfo.Version, DataHash = 0xFEDCBA9876543210UL, Map = map, Seed = ulong.MaxValue, PlayerCount = 3,
            UnitCapacity = 7, CommandCapacity = 9, CheckpointInterval = 7, TickCount = 20,
            Commands = ImmutableArrayOf(), Checkpoints = new[] { new ReplayCheckpoint(7, 1), new ReplayCheckpoint(14, ulong.MaxValue) }.ToImmutableArray(),
        };
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(r), out Replay? parsed));
        ReplayTestRun.AssertEqual(r, parsed!);
    }

    private static ImmutableArray<Command> ImmutableArrayOf(params Command[] c) => c.ToImmutableArray();

    [Fact]
    public void Truncated_AtEveryLineBoundary_IsRefusedAsTruncated()
    {
        byte[] golden = File.ReadAllBytes(ReplayTestRun.GoldenPath);
        int boundaries = 0;
        for (int len = 0; len < golden.Length; len++)
        {
            if (len > 0 && golden[len - 1] != (byte)'\n') continue;
            Assert.True(Read(golden[..len]) == ReplayError.Truncated, $"cut at byte {len}");
            boundaries++;
        }
        Assert.True(boundaries > 400, $"{boundaries} line boundaries");
    }

    [Fact]
    public void Truncated_AtEveryByte_IsRefused()
    {
        byte[] golden = File.ReadAllBytes(ReplayTestRun.GoldenPath);
        for (int len = 0; len < golden.Length; len++)
            Assert.True(Read(golden[..len]) != ReplayError.None, $"cut at byte {len}");
    }

    [Fact]
    public void AnySingleByteFlip_IsRefused_WithoutAnException()
    {
        byte[] golden = File.ReadAllBytes(ReplayTestRun.GoldenPath);
        byte[] masks = { 0x01, 0x02, 0x20, 0x80, 0xFF };
        for (int i = 0; i < golden.Length; i++)
        {
            byte keep = golden[i];
            foreach (byte m in masks)
            {
                golden[i] = (byte)(keep ^ m);
                ReplayError e = Read(golden);
                Assert.True(e != ReplayError.None, $"byte {i} ^ {m:X2} accepted");
            }
            golden[i] = keep;
        }
        Assert.Equal(ReplayError.None, Read(golden));
    }

    [Fact]
    public void CrLfLineEndings_AreRefused()
    {
        string text = Text(Small).Replace("\n", "\r\n");
        Assert.NotEqual(ReplayError.None, Read(Encoding.ASCII.GetBytes(text)));
        Assert.NotEqual(ReplayError.None, Read(Reseal(text)));
    }

    // ---------- valid checksum, invalid content ----------

    private static string FirstCommandLine(string text) => text.Split('\n').First(l => l.StartsWith("c ", StringComparison.Ordinal));

    private static string ReplaceField(string line, int field, string value)
    {
        string[] f = line.Split(' ');
        f[field] = value;
        return string.Join(' ', f);
    }

    [Theory]
    [InlineData(2, "2", "player == PlayerCount")]
    [InlineData(2, "99", "player far out of range")]
    [InlineData(2, "-1", "negative player")]
    [InlineData(4, "3", "unknown kind")]
    [InlineData(4, "-1", "negative kind")]
    [InlineData(1, "0", "tick 0 (Enqueue always stamps at least 1)")]
    [InlineData(1, "301", "tick past the end")]
    [InlineData(3, "5", "sequence gap")]
    public void CommandBreakingTheLogRules_IsRefusedAtRead(int field, string value, string what)
    {
        string text = Text(Small);
        string line = FirstCommandLine(text);
        Assert.Equal(ReplayError.InvalidCommand, Read(Reseal(text.Replace(line, ReplaceField(line, field, value)))));
        _ = what;
    }

    [Fact]
    public void CommandTicksGoingBackwards_AreRefused()
    {
        Replay r = Small;
        var commands = r.Commands.ToArray();
        int last = commands.Length - 1;
        Assert.True(commands[last].Tick > 1);
        commands[last].Tick = 1;
        Replay bad = ReplayTestRun.With(r, commands: commands);
        Assert.Equal(ReplayError.InvalidCommand, bad.Validate());
        Assert.Throws<ArgumentException>(() => ReplayFormat.Write(bad)); // the writer won't write it either
    }

    [Fact]
    public void FormatVersion2_IsRefusedAsVersionMismatch()
    {
        string text = Text(Small).Replace("rts-replay 1\n", "rts-replay 2\n");
        Assert.Equal(ReplayError.FormatVersionMismatch, Read(Reseal(text)));
    }

    [Theory]
    [InlineData("players 2\n", "players 0\n", ReplayError.InvalidHeader)]
    [InlineData("players 2\n", "players 17\n", ReplayError.InvalidHeader)]
    [InlineData("checkpoint-interval 100\n", "checkpoint-interval 0\n", ReplayError.InvalidHeader)]
    [InlineData("map.width 128\n", "map.width 8\n", ReplayError.InvalidHeader)]
    [InlineData("players 2\n", "players 02\n", ReplayError.Malformed)]
    [InlineData("players 2\n", "players  2\n", ReplayError.Malformed)]
    [InlineData("players 2\n", "player 2\n", ReplayError.Malformed)]
    [InlineData("unit-capacity 64\n", "", ReplayError.Malformed)]
    [InlineData("checkpoint-interval 100\n", "checkpoint-interval 150\n", ReplayError.InvalidCheckpoint)]
    [InlineData("ticks 300\n", "ticks 299\n", ReplayError.InvalidCheckpoint)]
    [InlineData("end\n", "end\nextra\n", ReplayError.Malformed)]
    [InlineData("end\n", "", ReplayError.Malformed)]
    public void HeaderAndStructureProblems_AreRefusedWithTheirCode(string find, string replace, ReplayError expected)
    {
        string text = Text(Small);
        Assert.Contains(find, text);
        Assert.Equal(expected, Read(Reseal(text.Replace(find, replace))));
    }

    [Fact]
    public void CountLines_MustMatchTheLinesPresent()
    {
        string text = Text(Small);
        string countLine = text.Split('\n').First(l => l.StartsWith("commands ", StringComparison.Ordinal));
        int n = int.Parse(countLine[9..]);
        Assert.Equal(ReplayError.Malformed, Read(Reseal(text.Replace(countLine + "\n", $"commands {n + 1}\n"))));
        Assert.Equal(ReplayError.Malformed, Read(Reseal(text.Replace(countLine + "\n", $"commands {n - 1}\n"))));
        Assert.Equal(ReplayError.Malformed, Read(Reseal(text.Replace(countLine + "\n", "commands 2147483647\n"))));
    }

    /// <summary>Appends a valid checksum line to raw body bytes (which may hold any byte).</summary>
    private static byte[] SealBytes(byte[] body) =>
        body.Concat(Encoding.ASCII.GetBytes($"checksum {ReplayFormat.Checksum(body):X16}\n")).ToArray();

    [Fact]
    public void NonAsciiOrControlBytes_AreRefusedEvenWithAValidChecksum()
    {
        string text = Text(Small);
        byte[] body = Encoding.ASCII.GetBytes(text[..text.LastIndexOf("checksum ", StringComparison.Ordinal)]);
        Assert.Equal(ReplayError.None, Read(SealBytes(body)));
        foreach (byte bad in new byte[] { 0x00, 0x09, 0x0D, 0x7F, 0x80, 0xC3, 0xFF })
        {
            byte[] copy = (byte[])body.Clone();
            copy[20] = bad;
            Assert.True(Read(SealBytes(copy)) == ReplayError.Malformed, $"byte {bad:X2}");
        }
    }

    [Fact]
    public void Garbage_IsRefused_WithoutAnException()
    {
        var rng = new Determinism.SimRng(77, 3);
        for (int n = 0; n < 2000; n++)
        {
            byte[] junk = new byte[rng.NextInt(0, 200)];
            for (int i = 0; i < junk.Length; i++) junk[i] = (byte)rng.NextInt(0, 256);
            Assert.NotEqual(ReplayError.None, Read(junk));
            byte[] lines = junk.Concat(new[] { (byte)'\n' }).ToArray();
            Assert.NotEqual(ReplayError.None, Read(SealBytes(lines)));
            // Printable junk with a valid checksum reaches the line parser.
            byte[] printable = lines.Select(b => b == (byte)'\n' ? b : (byte)(0x20 + b % 0x5F)).ToArray();
            Assert.NotEqual(ReplayError.None, Read(SealBytes(printable)));
        }
        Assert.Equal(ReplayError.Truncated, Read(Array.Empty<byte>()));
        Assert.Equal(ReplayError.Truncated, Read(ReplayFormat.Seal("")));
    }

    [Fact]
    public void Write_RefusesAnInvalidReplay()
    {
        Assert.Throws<ArgumentException>(() => ReplayFormat.Write(ReplayTestRun.With(Small, players: 1)));
        Assert.Throws<ArgumentException>(() => ReplayFormat.Write(ReplayTestRun.With(Small, simVersion: "0.0 1")));
        Assert.Throws<ArgumentException>(() => ReplayFormat.Write(ReplayTestRun.With(Small, checkpoints: Small.Checkpoints.Skip(1))));
    }
}
