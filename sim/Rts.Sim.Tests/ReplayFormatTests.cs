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
        Assert.StartsWith($"rts-replay {Replay.CurrentFormatVersion}\nsim-version " + SimInfo.Version + "\n", text);
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
            Forests = 3, ForestMinTrees = 5, ForestMaxTrees = 9, GoldMines = 2, MineSpacing = 10.5f, // M3-1
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
            UnitCapacity = 7, CommandCapacity = 9, ResourceCapacity = 33, CheckpointInterval = 7, TickCount = 20,
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
    [InlineData(4, "16", "unknown kind (one past Research, M3-5)")]
    [InlineData(4, "-1", "negative kind")]
    [InlineData(1, "0", "tick 0 (Enqueue always stamps at least 1)")]
    [InlineData(1, "301", "tick past the end")]
    [InlineData(3, "5", "sequence gap")]
    [InlineData(10, "2", "unknown flag bit")]
    [InlineData(10, "3", "queued plus an unknown flag bit")]
    [InlineData(10, "-1", "every flag bit")]
    [InlineData(10, "1", "the queued flag on a spawn (not a unit order, BUG-0056)")]
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

    /// <summary>M1-7: format 2 added the flags field; M3-1: format 3 the resource lines. Any other version is refused by version first.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(0)]
    public void OtherFormatVersion_IsRefusedAsVersionMismatch(int version)
    {
        Assert.Equal(3, Replay.CurrentFormatVersion);
        string text = Text(Small).Replace($"rts-replay {Replay.CurrentFormatVersion}\n", $"rts-replay {version}\n");
        Assert.Equal(ReplayError.FormatVersionMismatch, Read(Reseal(text)));
    }

    /// <summary>M1-7: the golden as format 1 wrote it (header 1, 9-field command lines) is refused with the version code.</summary>
    [Fact]
    public void Format1File_IsRefusedAsVersionMismatch()
    {
        string text = Encoding.ASCII.GetString(File.ReadAllBytes(ReplayTestRun.GoldenPath));
        string[] lines = text[..text.LastIndexOf("checksum ", StringComparison.Ordinal)].Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("rts-replay ", StringComparison.Ordinal)) lines[i] = "rts-replay 1";
            else if (lines[i].StartsWith("c ", StringComparison.Ordinal)) lines[i] = lines[i][..lines[i].LastIndexOf(' ')];
        }
        Assert.Equal(ReplayError.FormatVersionMismatch, Read(ReplayFormat.Seal(string.Join('\n', lines))));
    }

    /// <summary>M3-1: the golden as format 2 wrote it (header 2, no resource-capacity or resource map lines) is refused with the version code.</summary>
    [Fact]
    public void Format2File_IsRefusedAsVersionMismatch()
    {
        string text = Encoding.ASCII.GetString(File.ReadAllBytes(ReplayTestRun.GoldenPath));
        string[] lines = text[..text.LastIndexOf("checksum ", StringComparison.Ordinal)].Split('\n');
        string[] dropped = { "resource-capacity ", "map.forests ", "map.forest-min-trees ", "map.forest-max-trees ", "map.gold-mines ", "map.mine-spacing " };
        Assert.All(dropped, key => Assert.Contains(lines, l => l.StartsWith(key, StringComparison.Ordinal)));
        var format2 = lines.Where(l => !dropped.Any(key => l.StartsWith(key, StringComparison.Ordinal)))
            .Select(l => l.StartsWith("rts-replay ", StringComparison.Ordinal) ? "rts-replay 2" : l);
        Assert.Equal(ReplayError.FormatVersionMismatch, Read(ReplayFormat.Seal(string.Join('\n', format2))));
    }

    /// <summary>M3-1: a replay on a map with forests and mines records their params and plays back.</summary>
    [Fact]
    public void ForestsAndMines_RoundTripAndPlayBack()
    {
        var map = new MapGenParams { Forests = 6, GoldMines = 4, ForestMinTrees = 8, ForestMaxTrees = 20, MineSpacing = 30f };
        var sim = new Simulation(TestSim.Config(Seed: 9, PlayerCount: 2, UnitCapacity: 40, CommandCapacity: 80) with { Map = map, ResourceCapacity = 900 });
        var recorder = new ReplayRecorder(sim, checkpointInterval: 50);
        Assert.True(sim.World.ResourcePlacement.Trees > 0 && sim.World.ResourcePlacement.Mines > 0);
        NavGrid g = sim.World.NavGrid;
        int center = MoveScenario.CentralCell(g);
        var cells = OrderMix.Cells(g, 14f);
        for (int i = 0; i < 40; i++) sim.Enqueue(Commands.Command.SpawnUnit(i % 2, i % TestSim.UnitTypeCount, MoveScenario.Center(g, cells[i * 7 % cells.Count])));
        for (int t = 0; t < 200; t++)
        {
            if (t == 2) MoveScenario.MoveAll(sim, MoveScenario.Center(g, center));
            sim.Tick();
        }
        Replay r = recorder.ToReplay();
        Assert.Equal(map, r.Map);
        Assert.Equal(900, r.ResourceCapacity);
        string text = Text(r);
        Assert.Contains("\nresource-capacity 900\n", text);
        Assert.Contains("\nmap.forests 6\nmap.forest-min-trees 8\nmap.forest-max-trees 20\nmap.gold-mines 4\nmap.mine-spacing 41F00000\n", text);
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(r), out Replay? parsed));
        ReplayTestRun.AssertEqual(r, parsed!);
        ReplayResult played = ReplayPlayer.Run(parsed!, TestSim.Data);
        Assert.True(played.Ok, played.ToString());
        Assert.Equal(4, played.TicksRun / 50);

        // Played on the default (resource-free) map instead, the same log diverges at the first checkpoint.
        Replay bare = new()
        {
            SimVersion = r.SimVersion, DataHash = r.DataHash, Map = MapGenParams.Default, Seed = r.Seed, PlayerCount = r.PlayerCount,
            UnitCapacity = r.UnitCapacity, CommandCapacity = r.CommandCapacity, ResourceCapacity = r.ResourceCapacity,
            CheckpointInterval = r.CheckpointInterval, TickCount = r.TickCount, Commands = r.Commands, Checkpoints = r.Checkpoints,
        };
        Assert.Equal(ReplayError.CheckpointMismatch, ReplayPlayer.Run(bare, TestSim.Data).Error);
    }

    /// <summary>M1-7: a command line must have exactly 10 fields; the old 9-field shape in a format 2 file is malformed, as is a non-number flags field.</summary>
    [Fact]
    public void NineFieldCommandLine_InAFormat2File_IsMalformed()
    {
        string text = Text(Small);
        string line = FirstCommandLine(text);
        Assert.Equal(10, line.Split(' ').Length - 1);
        Assert.Equal(ReplayError.Malformed, Read(Reseal(text.Replace(line + "\n", line[..line.LastIndexOf(' ')] + "\n"))));
        Assert.Equal(ReplayError.Malformed, Read(Reseal(text.Replace(line + "\n", line + " 0\n"))));
        Assert.Equal(ReplayError.Malformed, Read(Reseal(text.Replace(line + "\n", ReplaceField(line, 10, "x") + "\n"))));
        Assert.Equal(ReplayError.Malformed, Read(Reseal(text.Replace(line + "\n", ReplaceField(line, 10, "01") + "\n"))));
    }

    /// <summary>M1-7: queued orders keep their flags through the file and play back.</summary>
    [Fact]
    public void QueuedFlags_RoundTrip_AndPlayBack()
    {
        Replay r = ReplayTestRun.RecordOrders(seed: 2, ticks: 300).Recorder.ToReplay();
        Assert.Contains(r.Commands, c => c.Flags == Command.QueuedFlag);
        Assert.Contains(r.Commands, c => c.Flags == 0 && c.IsUnitOrder);
        string text = Text(r);
        Assert.Contains(text.Split('\n'), l => l.StartsWith("c ", StringComparison.Ordinal) && l.EndsWith(" 1", StringComparison.Ordinal));
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(r), out Replay? parsed));
        ReplayTestRun.AssertEqual(r, parsed!);
        Assert.True(ReplayPlayer.Run(parsed!, TestSim.Data).Ok);
    }

    [Theory]
    [InlineData("players 2\n", "players 0\n", ReplayError.InvalidHeader)]
    [InlineData("players 2\n", "players 17\n", ReplayError.InvalidHeader)]
    [InlineData("checkpoint-interval 100\n", "checkpoint-interval 0\n", ReplayError.InvalidHeader)]
    [InlineData("map.width 128\n", "map.width 8\n", ReplayError.InvalidHeader)]
    [InlineData("resource-capacity 4096\n", "resource-capacity 0\n", ReplayError.InvalidHeader)]
    [InlineData("resource-capacity 4096\n", "resource-capacity 1000001\n", ReplayError.InvalidHeader)]
    [InlineData("resource-capacity 4096\n", "", ReplayError.Malformed)]
    [InlineData("map.forests 0\n", "map.forests 65\n", ReplayError.InvalidHeader)]
    [InlineData("map.gold-mines 0\n", "map.gold-mines -1\n", ReplayError.InvalidHeader)]
    [InlineData("map.forest-min-trees 12\n", "map.forest-min-trees 41\n", ReplayError.InvalidHeader)]
    [InlineData("map.mine-spacing 41C00000\n", "map.mine-spacing 7FC00000\n", ReplayError.InvalidHeader)]
    [InlineData("map.mine-spacing 41C00000\n", "map.mine-spacing 24\n", ReplayError.Malformed)]
    [InlineData("map.forests 0\n", "", ReplayError.Malformed)]
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
    /// <summary>BUG-0040: the tick count is capped at 24 h (1,728,000 ticks) and the interval at the same limit; the limit itself reads.</summary>
    [Fact]
    public void TickCountAndInterval_HaveAFormatLimit()
    {
        Replay Header(int ticks, int interval) => new()
        {
            SimVersion = Small.SimVersion,
            DataHash = Small.DataHash,
            Map = Small.Map,
            Seed = Small.Seed,
            PlayerCount = Small.PlayerCount,
            UnitCapacity = Small.UnitCapacity,
            CommandCapacity = Small.CommandCapacity,
            CheckpointInterval = interval,
            TickCount = ticks,
            Commands = ImmutableArray<Command>.Empty,
            Checkpoints = Enumerable.Range(1, ticks / interval).Select(k => new ReplayCheckpoint(k * interval, 0UL)).ToImmutableArray(),
        };
        Assert.Equal(1_728_000, Replay.MaxTickCount);
        Assert.Equal(ReplayError.None, Header(Replay.MaxTickCount, Replay.MaxTickCount).Validate());
        Assert.Equal(ReplayError.None, Header(50, 100).Validate()); // shorter than one interval: what the recorder writes
        Assert.Equal(ReplayError.InvalidHeader, Header(Replay.MaxTickCount + 1, Replay.MaxTickCount).Validate());
        Assert.Equal(ReplayError.InvalidHeader, Header(100, Replay.MaxTickCount + 1).Validate());
        Assert.Equal(ReplayError.InvalidHeader, Header(int.MaxValue, int.MaxValue).Validate());
        var sim = new Simulation(TestSim.Config(Seed: 3, PlayerCount: 1, UnitCapacity: 4, CommandCapacity: 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReplayRecorder(sim, checkpointInterval: Replay.MaxTickCount + 1));
    }

    /// <summary>BUG-0047: a recording past the format limit is refused when written, not written and then refused on read.</summary>
    [Fact]
    public void Recorder_PastTheTickLimit_RefusesToWrite()
    {
        var sim = new Simulation(TestSim.Config(Seed: 3, PlayerCount: 1, UnitCapacity: 4, CommandCapacity: 4));
        var recorder = new ReplayRecorder(sim);
        sim.World.TickNumber = Replay.MaxTickCount; // test seam: a 24 h recording
        Assert.Equal(Replay.MaxTickCount, recorder.ToReplay().TickCount); // at the limit: written (checkpoints skipped by the seam)
        sim.World.TickNumber = Replay.MaxTickCount + 1;
        Assert.Throws<InvalidOperationException>(() => recorder.ToReplay());
    }
}
