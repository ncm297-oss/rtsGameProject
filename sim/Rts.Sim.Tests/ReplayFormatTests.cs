using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using System.Text;
using Rts.Sim.Commands;
using Rts.Sim.Data;
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
            StartLocations = 0, StartClearRadius = 6, StartEdgeMargin = 7, StartMineDistance = 14.5f, StartForestDistance = 20.5f, // M5-4a
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
            Seats = ReplayTestRun.HumanSeats(3), // M5-2
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
    [InlineData(4, "18", "unknown kind (one past UseAbility, M4-4a)")]
    [InlineData(4, "-1", "negative kind")]
    [InlineData(1, "0", "tick 0 (Enqueue always stamps at least 1)")]
    [InlineData(1, "301", "tick past the end")]
    [InlineData(3, "5", "sequence gap")]
    [InlineData(13, "2", "unknown flag bit")]
    [InlineData(13, "3", "queued plus an unknown flag bit")]
    [InlineData(13, "-1", "every flag bit")]
    [InlineData(13, "1", "the queued flag on a spawn (not a unit order, BUG-0056)")]
    [InlineData(10, "1", "an attack target on a spawn (not an Attack, M4-2a)")]
    [InlineData(12, "1", "a building target flag on a spawn (M4-2a)")]
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

    /// <summary>M1-7: format 2 added the flags field; M3-1: format 3 the resource lines; M4-2a: format 4 the combat line and attack targets; M5-2: format 5 the seat lines. M5-4a: format 6 the start-location lines. Any version but 3 to 6 is refused by version first.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(0)]
    public void OtherFormatVersion_IsRefusedAsVersionMismatch(int version)
    {
        Assert.Equal(6, Replay.CurrentFormatVersion);
        Assert.Equal(3, Replay.OldestFormatVersion);
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

    // ---------- M4-2a: format 4 (the combat line, attack targets) and format 3 still read ----------

    /// <summary>The golden rewritten as format 3 wrote it: header 3, no combat line, 10-field command lines.</summary>
    private static string GoldenAsFormat3()
    {
        string text = Encoding.ASCII.GetString(File.ReadAllBytes(ReplayTestRun.GoldenPath));
        string[] lines = text[..text.LastIndexOf("checksum ", StringComparison.Ordinal)].Split('\n');
        Assert.Contains("combat 1", lines);
        // M5-2: and no seat lines (format 5's).
        var format3 = lines.Where(l => l != "combat 1" && !l.StartsWith("seat ", StringComparison.Ordinal) && !l.StartsWith("map.start-", StringComparison.Ordinal)).Select(l =>
            l.StartsWith("rts-replay ", StringComparison.Ordinal) ? "rts-replay 3"
            : l.StartsWith("c ", StringComparison.Ordinal) ? string.Join(' ', l.Split(' ')[..10]) + " " + l.Split(' ')[^1]
            : l);
        return string.Join('\n', format3);
    }

    [Fact]
    public void Format3File_StillReads_WithCombatOn_WritesBackAsFormat3_AndPlays()
    {
        byte[] bytes = ReplayFormat.Seal(GoldenAsFormat3());
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(bytes, out Replay? r));
        Assert.Equal(3, r!.FormatVersion);
        Assert.True(r.Combat);
        Assert.All(r.Commands, c => Assert.True(c.Target == default && !c.TargetIsBuilding));
        Assert.Equal(bytes, ReplayFormat.Write(r)); // a format 3 replay writes back as format 3, byte for byte
        ReplayResult played = ReplayPlayer.Run(r, TestSim.Data);
        Assert.True(played.Ok, played.ToString());
    }

    [Fact]
    public void Format3_CannotHoldAnAttackOrCombatOff()
    {
        Replay r3 = ReplayTestRun.With(Small, formatVersion: 3);
        Assert.Equal(ReplayError.None, r3.Validate());
        Assert.Equal(ReplayError.InvalidHeader, ReplayTestRun.With(r3, combat: false).Validate());
        Command[] commands = r3.Commands.ToArray();
        commands[^1] = Command.Attack(commands[^1].Player, new Rts.Sim.Entities.EntityHandle(0, 1), new Rts.Sim.Entities.EntityHandle(1, 1), false)
            with { Tick = commands[^1].Tick, Sequence = commands[^1].Sequence };
        Assert.Equal(ReplayError.InvalidCommand, ReplayTestRun.With(r3, commands: commands).Validate());
        Assert.Equal(ReplayError.None, ReplayTestRun.With(Small, commands: commands).Validate()); // format 4 can
    }

    [Fact]
    public void Format4_HasTheCombatLine_AndThirteenFieldCommandLines()
    {
        string text = Text(Small);
        Assert.StartsWith("rts-replay 6\n", text); // M5-2 / M5-4a: formats 5 and 6 keep format 4's combat line and command lines
        string[] lines = text.Split('\n');
        Assert.Equal("combat 1", lines[Array.FindIndex(lines, l => l.StartsWith("ticks ", StringComparison.Ordinal)) + 1]);
        Assert.All(lines.Where(l => l.StartsWith("c ", StringComparison.Ordinal)), l => Assert.Equal(14, l.Split(' ').Length));
    }

    [Theory]
    [InlineData("combat 2")]
    [InlineData("combat true")]
    [InlineData("combat")]
    [InlineData("combat 01")]
    public void Format4_ABadCombatLine_IsMalformed(string line)
    {
        string text = Text(Small).Replace("\ncombat 1\n", "\n" + line + "\n");
        Assert.Equal(ReplayError.Malformed, Read(Reseal(text)));
    }

    [Theory]
    [InlineData(" 0 0 2")]  // building flag not 0 / 1
    [InlineData(" 0 0")]    // 12 fields
    [InlineData(" 0 0 0 0")] // 14 fields
    [InlineData(" 00 0 0")] // non-canonical index
    [InlineData(" 0 x 0")]  // not a number
    public void Format4_ABadAttackTargetField_IsMalformed(string target)
    {
        string text = Text(Small);
        string first = FirstCommandLine(text);
        string[] f = first.Split(' ');
        string edited = string.Join(' ', f[..10]) + target + " " + f[^1];
        Assert.Equal(ReplayError.Malformed, Read(Reseal(text.Replace(first + "\n", edited + "\n"))));
    }

    [Fact]
    public void Format4_CombatOff_IsRecorded_AndPlayedBackOff_WithoutTheOverride()
    {
        var sim = new Simulation(TestSim.ConfigNoCombat(Seed: 3, PlayerCount: 2, UnitCapacity: 16, CommandCapacity: 32));
        var rec = new ReplayRecorder(sim, checkpointInterval: 10);
        NavGrid g = sim.World.NavGrid;
        int center = MoveScenario.CentralCell(g);
        Vector2 at = MoveScenario.Center(g, center);
        sim.Enqueue(Command.SpawnUnit(0, CombatScenes.HeavyInfantry, at));
        sim.Enqueue(Command.SpawnUnit(1, CombatScenes.HeavyInfantry, at + new Vector2(1f, 0f)));
        for (int t = 0; t < 60; t++) sim.Tick();
        Assert.Equal(0, sim.World.DeathCount + sim.World.Kills[0] + sim.World.Kills[1]);
        Replay r = rec.ToReplay();
        Assert.False(r.Combat);
        Assert.Contains("\ncombat 0\n", Text(r));
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(r), out Replay? back));
        Assert.False(back!.Combat);
        Assert.True(ReplayPlayer.Run(back, TestSim.Data).Ok);
        // Played with combat on, the two neighbours fight: the checkpoints say so.
        Assert.Equal(ReplayError.CheckpointMismatch, ReplayPlayer.Run(back, TestSim.Data, combat: true).Error);
    }

    [Fact]
    public void Format4_AMatchWithAttackOrders_RoundTripsAndPlaysToEveryCheckpoint()
    {
        Replay r = RecordAttackMatch();
        Assert.Contains(r.Commands, c => c.Kind == CommandKind.Attack && c.IsQueued);
        Assert.Contains(r.Commands, c => c.Kind == CommandKind.Attack && !c.IsQueued);
        byte[] bytes = ReplayFormat.Write(r);
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(bytes, out Replay? back));
        ReplayTestRun.AssertEqual(r, back!);
        Assert.Equal(bytes, ReplayFormat.Write(back!));
        ReplayResult played = ReplayPlayer.Run(back!, TestSim.Data);
        Assert.True(played.Ok, played.ToString());
        Assert.Equal(r.TickCount, played.TicksRun);
    }

    /// <summary>
    /// A MapBrawl (seed 4, 12 a side) recorded from its first command: half of each side gets an explicit Attack on an enemy
    /// (player 0's queued every other time), one an Attack on a building; 400 ticks, a checkpoint every 20.
    /// </summary>
    private static Replay RecordAttackMatch()
    {
        ReplayRecorder? rec = null;
        Simulation sim = CombatScenes.MapBrawl(4, perSide: 12, onCreated: s => rec = new ReplayRecorder(s, checkpointInterval: 20));
        Rts.Sim.Entities.UnitStore u = sim.World.Units;
        var p0 = new List<int>();
        var p1 = new List<int>();
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i]) (u.Owner[i] == 0 ? p0 : p1).Add(i);
        for (int k = 0; k < 6; k++)
        {
            int i = p0[k], j = p1[k];
            sim.Enqueue(Command.Attack(0, new(i, u.Generation[i]), new(j, u.Generation[j]), false, queued: k % 2 == 1));
            sim.Enqueue(Command.Attack(1, new(j, u.Generation[j]), new(i, u.Generation[i]), false));
        }
        // A building to attack: player 1 places one through the command door, then player 0's seventh unit is sent at it.
        NavGrid g = sim.World.NavGrid;
        int cell = MoveScenario.CentralCell(g);
        sim.Enqueue(Command.SpawnBuilding(1, TestSim.Data.FindBuilding("whirlwind_tent"), MoveScenario.Center(g, cell) + new Vector2(0f, 10f)));
        sim.Tick();
        sim.Tick();
        Rts.Sim.Entities.BuildingStore b = sim.World.Buildings;
        for (int k = 0; k < b.Capacity; k++)
        {
            if (!b.Alive[k]) continue;
            int i = p0[6];
            sim.Enqueue(Command.Attack(0, new(i, u.Generation[i]), b.HandleOf(k), true));
        }
        while (sim.TickNumber < 400) sim.Tick();
        return rec!.ToReplay();
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
            Seats = r.Seats, // M5-2
            SimVersion = r.SimVersion, DataHash = r.DataHash, Map = MapGenParams.Default, Seed = r.Seed, PlayerCount = r.PlayerCount,
            UnitCapacity = r.UnitCapacity, CommandCapacity = r.CommandCapacity, ResourceCapacity = r.ResourceCapacity,
            CheckpointInterval = r.CheckpointInterval, TickCount = r.TickCount, Commands = r.Commands, Checkpoints = r.Checkpoints,
        };
        Assert.Equal(ReplayError.CheckpointMismatch, ReplayPlayer.Run(bare, TestSim.Data).Error);
    }

    /// <summary>M1-7: a command line must have exactly its format's field count (13 in format 4, M4-2a); one short or one long is malformed, as is a non-number flags field.</summary>
    [Fact]
    public void NineFieldCommandLine_InAFormat2File_IsMalformed()
    {
        string text = Text(Small);
        string line = FirstCommandLine(text);
        Assert.Equal(13, line.Split(' ').Length - 1);
        Assert.Equal(ReplayError.Malformed, Read(Reseal(text.Replace(line + "\n", line[..line.LastIndexOf(' ')] + "\n"))));
        Assert.Equal(ReplayError.Malformed, Read(Reseal(text.Replace(line + "\n", line + " 0\n"))));
        Assert.Equal(ReplayError.Malformed, Read(Reseal(text.Replace(line + "\n", ReplaceField(line, 13, "x") + "\n"))));
        Assert.Equal(ReplayError.Malformed, Read(Reseal(text.Replace(line + "\n", ReplaceField(line, 13, "01") + "\n"))));
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
            Seats = Small.Seats, // M5-2
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

    // ---------- M5-2: format 5 (seat lines) and format 4 still reads ----------

    [Fact]
    public void Format5_HasOneSeatLinePerPlayer_RightAfterPlayers()
    {
        string text = Text(Small);
        Assert.StartsWith("rts-replay 6\n", text); // M5-4a: format 6 keeps the seat lines
        Assert.Contains("\nplayers 2\nseat 0 malazan human normal\nseat 1 whirlwind human normal\nunit-capacity ", text);
        Assert.Equal(new[] { new ReplaySeat("malazan", SeatController.Human, AiDifficulty.Normal), new ReplaySeat("whirlwind", SeatController.Human, AiDifficulty.Normal) }, Small.Seats.ToArray());
    }

    /// <summary>The golden rewritten as format 4 wrote it (header 4, no seat lines).</summary>
    private static string GoldenAsFormat4()
    {
        string text = Encoding.ASCII.GetString(File.ReadAllBytes(ReplayTestRun.GoldenPath));
        string[] lines = text[..text.LastIndexOf("checksum ", StringComparison.Ordinal)].Split('\n');
        Assert.Contains(lines, l => l.StartsWith("seat ", StringComparison.Ordinal));
        // M5-4a: and no start-location lines (format 6's).
        return string.Join('\n', lines.Where(l => !l.StartsWith("seat ", StringComparison.Ordinal) && !l.StartsWith("map.start-", StringComparison.Ordinal))
            .Select(l => l.StartsWith("rts-replay ", StringComparison.Ordinal) ? "rts-replay 4" : l));
    }

    [Fact]
    public void Format4File_StillReads_WithDefaultSeats_WritesBackByteForByte_AndPlays()
    {
        byte[] bytes = ReplayFormat.Seal(GoldenAsFormat4());
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(bytes, out Replay? r));
        Assert.Equal(4, r!.FormatVersion);
        Assert.True(r.Seats.IsEmpty);
        Assert.Equal(bytes, ReplayFormat.Write(r)); // a format 4 replay writes back as format 4, byte for byte
        ReplayResult played = ReplayPlayer.Run(r, TestSim.Data);
        Assert.True(played.Ok, played.ToString());
    }

    [Fact]
    public void SeatsBelongToFormat5Only()
    {
        Assert.Equal(ReplayError.InvalidHeader, ReplayTestRun.With(Small, formatVersion: 4, seats: Small.Seats).Validate());
        Assert.Equal(ReplayError.InvalidHeader, ReplayTestRun.With(Small, seats: Array.Empty<ReplaySeat>()).Validate());
        Assert.Equal(ReplayError.InvalidHeader, ReplayTestRun.With(Small, seats: Small.Seats.Take(1)).Validate());
        Assert.Equal(ReplayError.InvalidHeader, ReplayTestRun.With(Small, seats: new[] { Small.Seats[0], Small.Seats[1] with { Faction = "Whirl wind" } }).Validate());
        Assert.Equal(ReplayError.InvalidHeader, ReplayTestRun.With(Small, seats: new[] { Small.Seats[0], Small.Seats[1] with { Controller = (SeatController)5 } }).Validate());
        Assert.Equal(ReplayError.InvalidHeader, ReplayTestRun.With(Small, seats: new[] { Small.Seats[0], Small.Seats[1] with { Difficulty = (AiDifficulty)(-1) } }).Validate());
        Assert.Equal(ReplayError.None, ReplayTestRun.With(Small, formatVersion: 4).Validate());
    }

    [Theory]
    [InlineData("seat 0 malazan human normal\n", "", "player 0's seat missing")]
    [InlineData("seat 1 whirlwind human normal\n", "", "player 1's seat missing")]
    [InlineData("seat 0 malazan human normal\nseat 1 whirlwind human normal\n", "seat 1 whirlwind human normal\nseat 0 malazan human normal\n", "out of order")]
    [InlineData("seat 1 whirlwind human normal\n", "seat 1 whirlwind human normal\nseat 2 malazan human normal\n", "one seat too many")]
    [InlineData("players 2\n", "players 3\n", "fewer seats than players")]
    [InlineData("seat 0 malazan human normal\n", "seat 0 malazan robot normal\n", "an unknown controller")]
    [InlineData("seat 0 malazan human normal\n", "seat 0 malazan Human normal\n", "a controller not in lowercase")]
    [InlineData("seat 0 malazan human normal\n", "seat 0 malazan human insane\n", "an unknown difficulty")]
    [InlineData("seat 0 malazan human normal\n", "seat 0 Malazan human normal\n", "a faction not snake_case")]
    [InlineData("seat 0 malazan human normal\n", "seat 0 9malazan human normal\n", "a faction starting with a digit")]
    [InlineData("seat 0 malazan human normal\n", "seat 0 malazan human\n", "a field short")]
    [InlineData("seat 0 malazan human normal\n", "seat 0 malazan human normal x\n", "a field too many")]
    [InlineData("seat 0 malazan human normal\n", "seat 00 malazan human normal\n", "a non-canonical player")]
    [InlineData("seat 0 malazan human normal\n", "seat -0 malazan human normal\n", "a signed zero")]
    [InlineData("seat 0 malazan human normal\n", "seat  0 malazan human normal\n", "two spaces")]
    [InlineData("seat 0 malazan human normal\n", "seats 0 malazan human normal\n", "another key")]
    public void AHostileSeatLine_IsMalformed(string find, string replace, string what)
    {
        string text = Text(Small);
        Assert.Contains(find, text);
        Assert.Equal(ReplayError.Malformed, Read(Reseal(text.Replace(find, replace))));
        _ = what;
    }

    /// <summary>A seat naming a faction the data hasn't (or "-", for data without factions) reads, but playback refuses it before any tick.</summary>
    [Theory]
    [InlineData("atlantis")]
    [InlineData("-")]
    public void ASeatsUnknownFaction_ReadsButIsRefusedAtPlayback(string faction)
    {
        string text = Text(Small).Replace("seat 1 whirlwind ", "seat 1 " + faction + " ");
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(Reseal(text), out Replay? r));
        Assert.Equal(faction, r!.Seats[1].Faction);
        ReplayResult played = ReplayPlayer.Run(r, TestSim.Data);
        Assert.Equal(ReplayError.InvalidHeader, played.Error);
        Assert.Equal(-1, played.Tick);
    }

    /// <summary>
    /// Playback builds the sim with the header's factions: player 0 recorded as the Whirlwind (not its default Malazan) places a
    /// tent; the replay plays to every checkpoint, and the same log with the seat edited to Malazan diverges (the Build of a
    /// Whirlwind tent is refused for a Malazan player).
    /// </summary>
    [Fact]
    public void Playback_UsesTheHeadersFactions()
    {
        int whirlwind = TestSim.Data.FindFaction("whirlwind");
        SimConfig config = TestSim.Config(Seed: 4, PlayerCount: 1, UnitCapacity: 8, CommandCapacity: 16)
            with { Seats = ImmutableArray.Create(new PlayerSeat(whirlwind, SeatController.Human, AiDifficulty.Normal)) };
        var sim = new Simulation(config);
        var rec = new ReplayRecorder(sim, checkpointInterval: 20);
        NavGrid g = sim.World.NavGrid;
        int centre = MoveScenario.CentralCell(g);
        sim.Enqueue(Command.SpawnUnit(0, TestSim.Data.FindUnit("whirlwind_camp_follower"), MoveScenario.Center(g, centre)));
        for (int t = 0; t < 10; t++) sim.Tick();
        int anchor = -1;
        for (int r = 2; r < 8 && anchor < 0; r++)
            for (int dx = -r; dx <= r && anchor < 0; dx++)
            {
                int c = centre + r * g.Width + dx;
                if (sim.World.CanPlace(0, BuildMaps.WhirlwindHouse, c, out _)) anchor = c;
            }
        Assert.True(anchor >= 0);
        // Paid with the starting wood: a replay can't carry a test's gift.
        sim.Enqueue(Command.Build(0, MoveScenario.Handle(sim, 0), BuildMaps.WhirlwindHouse, g.CellCenter(anchor % g.Width, anchor / g.Width)));
        for (int t = 0; t < 70; t++) sim.Tick();
        Assert.Equal(1, sim.World.Buildings.Count);
        Replay played = rec.ToReplay();
        Assert.Equal("whirlwind", played.Seats[0].Faction);
        Assert.True(ReplayPlayer.Run(played, TestSim.Data).Ok);
        string text = Text(played).Replace("seat 0 whirlwind ", "seat 0 malazan ");
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(Reseal(text), out Replay? edited));
        Assert.Equal(ReplayError.CheckpointMismatch, ReplayPlayer.Run(edited!, TestSim.Data).Error);
    }

    // ---------- M5-4a: format 6 (the start-location lines); formats 3-5 read with none ----------

    [Fact]
    public void Format6_HasTheStartLines_AfterMineSpacing_AndFormat5HasNone()
    {
        string text = Text(Small);
        Assert.Contains("\nmap.mine-spacing 41C00000\nmap.start-locations 2\nmap.start-clear-radius 8\nmap.start-edge-margin 10\n"
            + "map.start-mine-distance 41900000\nmap.start-forest-distance 41C00000\ncommands ", text);
        // A format 5 file has no such lines and reads with StartLocations 0: its map as recorded before M5-4a.
        Replay r5 = ReplayTestRun.With(Small, formatVersion: 5);
        Assert.Equal(0, r5.Map.StartLocations);
        string text5 = Text(r5);
        Assert.DoesNotContain("map.start-", text5);
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(Encoding.ASCII.GetBytes(text5), out Replay? back));
        Assert.Equal(0, back!.Map.StartLocations);
        Assert.Equal(Encoding.ASCII.GetBytes(text5), ReplayFormat.Write(back));
        // A format 5 replay can't say "start locations"; a start line in a format 5 file is an unknown line.
        var bad5 = new Replay
        {
            FormatVersion = 5, Seats = Small.Seats, SimVersion = Small.SimVersion, DataHash = Small.DataHash, Map = Small.Map, Seed = Small.Seed,
            PlayerCount = Small.PlayerCount, UnitCapacity = Small.UnitCapacity, CommandCapacity = Small.CommandCapacity,
            ResourceCapacity = Small.ResourceCapacity, CheckpointInterval = Small.CheckpointInterval, TickCount = Small.TickCount,
            Commands = Small.Commands, Checkpoints = Small.Checkpoints,
        };
        Assert.Equal(ReplayError.InvalidHeader, bad5.Validate());
        string withLine = text5.Replace("\ncommands ", "\nmap.start-locations 2\ncommands ");
        Assert.Equal(ReplayError.Malformed, Read(Reseal(withLine)));
    }

    [Theory]
    [InlineData("map.start-locations 2\n", "")]
    [InlineData("map.start-locations 2\n", "map.start-locations 1\n")]
    [InlineData("map.start-clear-radius 8\n", "map.start-clear-radius 99\n")]
    [InlineData("map.start-mine-distance 41900000\n", "map.start-mine-distance 7FC00000\n")]
    [InlineData("map.start-forest-distance 41C00000\n", "")]
    public void Format6_AMissingOrBadStartLine_IsRefused(string find, string replace)
    {
        string text = Text(Small);
        Assert.Contains(find, text);
        Assert.NotEqual(ReplayError.None, Read(Reseal(text.Replace(find, replace))));
    }
}
