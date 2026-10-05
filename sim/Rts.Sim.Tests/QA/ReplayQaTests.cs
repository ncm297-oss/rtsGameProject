using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.Replays;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M1-6): replay recorder, text format, player, data content hash and seed mixing, attacked
/// independently of the developer's suites. <see cref="RecordRandom"/> is the shared fuzz driver
/// (also used by <c>Stress/ReplayStressTests</c>).
/// </summary>
public class ReplayQaTests
{
    // ---------------------------------------------------------------- shared fuzz driver

    /// <summary>
    /// A seeded random command stream over <paramref name="ticks"/> ticks on 3 players: spawns (some with
    /// bad type ids, NaN, off-map or cliff positions), Moves to live, stale-generation, never-allocated
    /// and negative handles, foreign units, NaN and off-map targets, Noops, the same command spammed,
    /// and occasional whole-army bursts. A twin sim without a recorder gets the same commands and must
    /// keep the same StateHash every tick (the recorder is read-only).
    /// </summary>
    public static (Simulation Sim, ReplayRecorder Recorder) RecordRandom(ulong seed, int ticks, int checkpointInterval = 100,
        int players = 3, int unitCapacity = 96, int commandCapacity = 200, bool twin = true)
    {
        SimConfig config = TestSim.Config(Seed: seed, PlayerCount: players, UnitCapacity: unitCapacity, CommandCapacity: commandCapacity);
        var sim = new Simulation(config);
        var rec = new ReplayRecorder(sim, checkpointInterval);
        Simulation? other = twin ? new Simulation(config) : null;
        NavGrid g = sim.World.NavGrid;
        var open = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
            if (g.IsPassable(c % g.Width, c / g.Width)) open.Add(c);
        var rng = new SimRng(seed, 1234);
        UnitStore u = sim.World.Units;
        Command last = Command.Noop(0);

        void Send(Command c)
        {
            sim.Enqueue(c);
            other?.Enqueue(c);
            last = c;
        }

        Vector2 RandomPoint()
        {
            int pick = rng.NextInt(0, 20);
            if (pick == 0) return new Vector2(float.NaN, 5f);
            if (pick == 1) return new Vector2(-3f, 1e9f);
            if (pick == 2) return new Vector2(1f, 1f); // border ring, blocked
            int cell = open[rng.NextInt(0, open.Count)];
            return MoveScenario.Center(g, cell) + new Vector2(rng.NextFloat() - 0.5f, rng.NextFloat() - 0.5f);
        }

        EntityHandle RandomHandle()
        {
            int pick = rng.NextInt(0, 12);
            int slot = rng.NextInt(0, u.Capacity);
            return pick switch
            {
                0 => new EntityHandle(slot, u.Generation[slot] + 1 + rng.NextInt(0, 5)), // stale / future generation
                1 => new EntityHandle(-1, 0),
                2 => new EntityHandle(u.Capacity + rng.NextInt(0, 1000), 0),
                _ => new EntityHandle(slot, u.Generation[slot]),
            };
        }

        for (int t = 0; t < ticks; t++)
        {
            int n = rng.NextInt(0, 6);
            for (int k = 0; k < n; k++)
            {
                int player = rng.NextInt(0, players);
                switch (rng.NextInt(0, 10))
                {
                    case 0:
                    case 1:
                        Send(Command.SpawnUnit(player, rng.NextInt(-1, TestSim.UnitTypeCount + 1), RandomPoint()));
                        break;
                    case 2:
                        Send(Command.Noop(player));
                        break;
                    case 3:
                        // Spam the previous command (it is re-stamped with this player's next sequence).
                        for (int s = 0; s < 4; s++) Send(last with { Player = last.Player });
                        break;
                    default:
                    {
                        EntityHandle h = RandomHandle();
                        // Mostly the unit's own owner, sometimes a foreign player.
                        int owner = (uint)h.Index < (uint)u.Capacity && u.Alive[h.Index] && rng.NextInt(0, 4) != 0 ? u.Owner[h.Index] : player;
                        Send(Command.Move(owner, h, RandomPoint()));
                        break;
                    }
                }
            }
            if (t % 97 == 40)
            {
                // A whole-army burst to one point.
                Vector2 p = RandomPoint();
                for (int i = 0; i < u.Capacity; i++)
                    if (u.Alive[i]) Send(Command.Move(u.Owner[i], new EntityHandle(i, u.Generation[i]), p));
            }
            sim.Tick();
            if (other != null)
            {
                other.Tick();
                Assert.True(sim.StateHash() == other.StateHash(), $"seed {seed}: recorder changed the state at tick {sim.TickNumber}");
            }
        }
        return (sim, rec);
    }

    private static ReplayError Read(byte[] bytes)
    {
        ReplayError e = ReplayFormat.TryRead(bytes, out Replay? r);
        Assert.True((e == ReplayError.None) == (r != null), $"{e} with replay {(r == null ? "null" : "set")}");
        return e;
    }

    private static readonly Lazy<Replay> s_fuzz = new(() => RecordRandom(seed: 77, ticks: 400).Recorder.ToReplay());

    private static Replay Fuzz => s_fuzz.Value;

    private static string Text(Replay r) => Encoding.ASCII.GetString(ReplayFormat.Write(r));

    private static string Body(string text) => text[..text.LastIndexOf("checksum ", StringComparison.Ordinal)];

    private static byte[] Reseal(string text) => ReplayFormat.Seal(Body(text));

    // ---------------------------------------------------------------- recorder / player

    [Fact]
    public void RandomCommandStream_RoundTripsAndReplays_IncludingDeadAndInvalidHandles()
    {
        Replay r = Fuzz;
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(r), out Replay? back));
        ReplayTestRun.AssertEqual(r, back!);
        // The log must carry the commands the sim drops at apply time: they still consume a sequence number.
        Assert.Contains(r.Commands, c => c.Kind == CommandKind.Move && c.Unit.Index < 0);
        Assert.Contains(r.Commands, c => c.Kind == CommandKind.Move && c.Unit.Index >= r.UnitCapacity);
        Assert.Contains(r.Commands, c => c.Kind == CommandKind.SpawnUnit && (c.TypeId < 0 || c.TypeId >= TestSim.UnitTypeCount));
        Assert.Contains(r.Commands, c => float.IsNaN(c.Position.X));
        ReplayResult result = ReplayPlayer.Run(back!, TestSim.Data);
        Assert.True(result.Ok, result.ToString());
        Assert.Equal(400, result.TicksRun);
    }

    [Fact]
    public void MoveToAHandleThatIsStaleAtApplyTime_IsRecordedAndReplays()
    {
        // The handle is valid when enqueued in the recording... but units never die in M1, so use a
        // generation one ahead (it becomes "the future handle" of that slot) and a slot freed never.
        var sim = new Simulation(TestSim.Config(Seed: 3, PlayerCount: 2, UnitCapacity: 8, CommandCapacity: 16));
        var rec = new ReplayRecorder(sim, checkpointInterval: 10);
        NavGrid g = sim.World.NavGrid;
        Vector2 c = MoveScenario.Center(g, MoveScenario.CentralCell(g));
        sim.Enqueue(Command.SpawnUnit(0, 0, c));
        // Same tick as the spawn: the handle (0, gen) is "predicted", not yet alive when the Move is queued.
        sim.Enqueue(Command.Move(0, new EntityHandle(0, sim.World.Units.Generation[0]), c + new Vector2(6f, 0f)));
        sim.Enqueue(Command.Move(0, new EntityHandle(0, sim.World.Units.Generation[0] + 1), c + new Vector2(-6f, 0f)));
        for (int t = 0; t < 40; t++) sim.Tick();
        Replay r = rec.ToReplay();
        Assert.Equal(3, r.Commands.Length);
        Assert.True(ReplayPlayer.Run(r, TestSim.Data).Ok);
    }

    [Fact]
    public void TwoInterleavedPlayers_ReStamped_CrossPlayerReorderWithinATickStillPlays_SamePlayerSwapRefused()
    {
        var sim = new Simulation(TestSim.Config(Seed: 11, PlayerCount: 2, UnitCapacity: 40, CommandCapacity: 64));
        var rec = new ReplayRecorder(sim, checkpointInterval: 25);
        NavGrid g = sim.World.NavGrid;
        Vector2 c = MoveScenario.Center(g, MoveScenario.CentralCell(g));
        var rng = new SimRng(11, 5);
        for (int i = 0; i < 20; i++) sim.Enqueue(Command.SpawnUnit(rng.NextInt(0, 2), i % TestSim.UnitTypeCount, c + new Vector2(rng.NextFloat() * 4f, rng.NextFloat() * 4f)));
        for (int t = 0; t < 200; t++)
        {
            // Irregular interleaving: p1 p0 p0 p1 p1 p1 p0 ... within the same tick.
            int burst = rng.NextInt(0, 7);
            for (int k = 0; k < burst; k++)
            {
                int slot = rng.NextInt(0, 20);
                UnitStore u = sim.World.Units;
                int p = rng.NextInt(0, 2);
                sim.Enqueue(Command.Move(p, new EntityHandle(slot, u.Generation[slot]), c + new Vector2(rng.NextInt(-8, 9), rng.NextInt(-8, 9))));
            }
            sim.Tick();
        }
        Replay r = rec.ToReplay();
        Assert.True(ReplayPlayer.Run(r, TestSim.Data).Ok);

        // Find a tick with commands of both players where p1 precedes p0 somewhere, and a pair of one player's.
        Command[] cmds = r.Commands.ToArray();
        int cross = -1, same = -1;
        for (int i = 0; i + 1 < cmds.Length; i++)
        {
            if (cmds[i].Tick != cmds[i + 1].Tick) continue;
            if (cross < 0 && cmds[i].Player != cmds[i + 1].Player) cross = i;
            if (same < 0 && cmds[i].Player == cmds[i + 1].Player) same = i;
        }
        Assert.True(cross >= 0 && same >= 0, "the stream never interleaved");

        // Swapping two players' commands within a tick keeps each player's sequence order: the sim sorts
        // by (tick, player, sequence), so playback must still match.
        Command[] swapped = (Command[])cmds.Clone();
        (swapped[cross], swapped[cross + 1]) = (swapped[cross + 1], swapped[cross]);
        Replay crossSwap = ReplayTestRun.With(r, commands: swapped);
        Assert.Equal(ReplayError.None, crossSwap.Validate());
        Assert.True(ReplayPlayer.Run(crossSwap, TestSim.Data).Ok);

        // Swapping one player's two commands breaks its sequence order: refused at validation.
        Command[] bad = (Command[])cmds.Clone();
        (bad[same], bad[same + 1]) = (bad[same + 1], bad[same]);
        Assert.Equal(ReplayError.InvalidCommand, ReplayTestRun.With(r, commands: bad).Validate());
        Assert.Equal(ReplayError.InvalidCommand, ReplayPlayer.Run(ReplayTestRun.With(r, commands: bad), TestSim.Data).Error);
    }

    [Fact]
    public void ToReplay_MidRecording_PlaysBack_AndRecordingContinues()
    {
        (Simulation sim, ReplayRecorder rec) = RecordRandom(seed: 21, ticks: 250, twin: false);
        Replay early = rec.ToReplay();
        // Queue more, snapshot with them pending, keep going.
        sim.Enqueue(Command.Noop(2));
        Replay pending = rec.ToReplay();
        Assert.Equal(early.Commands.Length, pending.Commands.Length);
        for (int t = 0; t < 150; t++) sim.Tick();
        Replay late = rec.ToReplay();
        Assert.Equal(400, late.TickCount);
        Assert.Equal(early.Commands.Length + 1, late.Commands.Length);
        Assert.True(ReplayPlayer.Run(early, TestSim.Data).Ok);
        Assert.True(ReplayPlayer.Run(late, TestSim.Data).Ok);
        // The early replay is a prefix of the late one.
        for (int i = 0; i < early.Checkpoints.Length; i++) Assert.Equal(early.Checkpoints[i], late.Checkpoints[i]);
    }

    [Fact]
    public void Playback_CheckpointsEqualAnUnrecordedSimsHashes()
    {
        // Independent of the player's own recorder: feed the log to a bare sim and hash by hand.
        Replay r = Fuzz;
        var sim = new Simulation(new SimConfig(r.Seed, r.PlayerCount, r.UnitCapacity, r.CommandCapacity) { Data = TestSim.Data, Map = r.Map });
        int next = 0, k = 0;
        while (sim.TickNumber < r.TickCount)
        {
            while (next < r.Commands.Length && r.Commands[next].Tick == sim.TickNumber + 1) sim.Enqueue(r.Commands[next++]);
            sim.Tick();
            if (sim.TickNumber % r.CheckpointInterval == 0)
                Assert.True(r.Checkpoints[k++].Hash == sim.StateHash(), $"tick {sim.TickNumber}");
        }
        Assert.Equal(r.Checkpoints.Length, k);
        Assert.Equal(r.Commands.Length, next);
    }

    [Fact]
    public void TickCountNotAMultipleOfTheInterval_PlaysToTheExactTick()
    {
        (Simulation sim, ReplayRecorder rec) = RecordRandom(seed: 8, ticks: 237, checkpointInterval: 50, twin: false);
        Replay r = rec.ToReplay();
        Assert.Equal(4, r.Checkpoints.Length);
        ReplayResult res = ReplayPlayer.Run(r, TestSim.Data);
        Assert.True(res.Ok, res.ToString());
        Assert.Equal(237, res.TicksRun);
    }

    [Fact]
    public void DataMismatch_IsReportedBeforeAnyTick_EvenWhenTheFormatIsValid()
    {
        Replay r = ReplayTestRun.With(Fuzz, dataHash: Fuzz.DataHash ^ 1);
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(r), out Replay? back));
        ReplayResult res = ReplayPlayer.Run(back!, TestSim.Data);
        Assert.Equal(ReplayError.DataMismatch, res.Error);
        Assert.Equal(0, res.TicksRun);
        // Format version is checked first even when the data also mismatches.
        Assert.Equal(ReplayError.FormatVersionMismatch, ReplayPlayer.Run(ReplayTestRun.With(r, formatVersion: 2), TestSim.Data).Error);
    }

    // ---------------------------------------------------------------- parser attacks

    [Fact]
    public void Truncation_AtEveryByte_OfAFuzzReplay_NeverAccepted_NeverThrows()
    {
        byte[] bytes = ReplayFormat.Write(Fuzz);
        for (int len = 0; len < bytes.Length; len++)
        {
            ReplayError e = Read(bytes[..len]);
            Assert.True(e != ReplayError.None, $"cut at byte {len}");
            if (len == 0 || bytes[len - 1] == (byte)'\n') Assert.True(e == ReplayError.Truncated, $"cut at line boundary {len}: {e}");
        }
    }

    [Fact]
    public void EveryByteValue_AtEveryHeaderAndFirstCommandByte_IsRefused()
    {
        byte[] bytes = ReplayFormat.Write(Fuzz);
        string text = Encoding.ASCII.GetString(bytes);
        int firstC = text.IndexOf("\nc ", StringComparison.Ordinal) + 1;
        int end = text.IndexOf('\n', firstC) + 1;
        for (int i = 0; i < end; i++)
        {
            byte keep = bytes[i];
            for (int v = 0; v < 256; v++)
            {
                if (v == keep) continue;
                bytes[i] = (byte)v;
                Assert.True(Read(bytes) != ReplayError.None, $"byte {i} = {v:X2}");
            }
            bytes[i] = keep;
        }
    }

    [Fact]
    public void Utf8Bom_IsRefused_WithOrWithoutAValidChecksum()
    {
        byte[] bom = { 0xEF, 0xBB, 0xBF };
        byte[] plain = ReplayFormat.Write(Fuzz);
        Assert.Equal(ReplayError.ChecksumMismatch, Read(bom.Concat(plain).ToArray()));
        byte[] body = Encoding.ASCII.GetBytes(Body(Text(Fuzz)));
        byte[] withBom = bom.Concat(body).ToArray();
        byte[] sealedBom = withBom.Concat(Encoding.ASCII.GetBytes($"checksum {ReplayFormat.Checksum(withBom):X16}\n")).ToArray();
        Assert.Equal(ReplayError.Malformed, Read(sealedBom));
    }

    [Fact]
    public void CrLf_And_MixedLineEndings_AndTrailingSpaces_AreRefused()
    {
        string text = Text(Fuzz);
        Assert.NotEqual(ReplayError.None, Read(Encoding.ASCII.GetBytes(text.Replace("\n", "\r\n"))));
        Assert.Equal(ReplayError.Malformed, Read(Reseal(text.Replace("\n", "\r\n"))));
        // Only the checksum line CRLF.
        Assert.NotEqual(ReplayError.None, Read(Encoding.ASCII.GetBytes(text[..^1] + "\r\n")));
        // One header line CRLF.
        Assert.Equal(ReplayError.Malformed, Read(Reseal(text.Replace("players 3\n", "players 3\r\n"))));
        Assert.Equal(ReplayError.Malformed, Read(Reseal(text.Replace("players 3\n", "players 3 \n"))));
        Assert.Equal(ReplayError.Malformed, Read(Reseal(text.Replace("players 3\n", " players 3\n"))));
        Assert.Equal(ReplayError.Malformed, Read(Reseal(text.Replace("players 3\n", "players\t3\n"))));
        // No final LF at all.
        Assert.Equal(ReplayError.Truncated, Read(Encoding.ASCII.GetBytes(text[..^1])));
        // Blank line anywhere.
        Assert.Equal(ReplayError.Malformed, Read(Reseal(text.Replace("players 3\n", "players 3\n\n"))));
        Assert.Equal(ReplayError.Malformed, Read(Reseal("\n" + text)));
    }

    [Theory]
    [InlineData("players 3\n")]
    [InlineData("seed 77\n")]
    [InlineData("map.width 128\n")]
    [InlineData("sim-version 0.0.1\n")]
    [InlineData("end\n")]
    public void DuplicateHeaderLine_IsRefused(string line)
    {
        string text = Text(Fuzz);
        Assert.Contains(line, text);
        Assert.Equal(ReplayError.Malformed, Read(Reseal(text.Replace(line, line + line))));
    }

    [Fact]
    public void HeaderLinesInAnotherOrder_AreRefused()
    {
        string text = Text(Fuzz);
        string swapped = text.Replace("players 3\nunit-capacity 96\n", "unit-capacity 96\nplayers 3\n");
        Assert.NotEqual(text, swapped);
        Assert.Equal(ReplayError.Malformed, Read(Reseal(swapped)));
    }

    [Theory]
    [InlineData("k 0 ")]
    [InlineData("k -100 ")]
    [InlineData("k 99 ")]
    [InlineData("k 400 ")]
    public void CheckpointAtTheWrongTick_IncludingTickZeroAndNegative_IsRefused(string replacementPrefix)
    {
        string text = Text(Fuzz);
        int i = text.IndexOf("\nk 100 ", StringComparison.Ordinal);
        Assert.True(i >= 0);
        string edited = text[..(i + 1)] + replacementPrefix + text[(i + 1 + "k 100 ".Length)..];
        Assert.Equal(ReplayError.InvalidCheckpoint, Read(Reseal(edited)));
    }

    [Fact]
    public void DuplicatedOrMissingCheckpoint_IsRefused()
    {
        string text = Text(Fuzz);
        string[] lines = text.Split('\n');
        int k = Array.FindIndex(lines, l => l.StartsWith("k ", StringComparison.Ordinal));
        int countLine = Array.FindIndex(lines, l => l.StartsWith("checkpoints ", StringComparison.Ordinal));
        int n = int.Parse(lines[countLine][12..], CultureInfo.InvariantCulture);
        var dup = lines.ToList();
        dup.Insert(k, lines[k]);
        dup[countLine] = $"checkpoints {n + 1}";
        Assert.Equal(ReplayError.InvalidCheckpoint, Read(Reseal(string.Join('\n', dup))));
        var miss = lines.ToList();
        miss.RemoveAt(k);
        miss[countLine] = $"checkpoints {n - 1}";
        Assert.Equal(ReplayError.InvalidCheckpoint, Read(Reseal(string.Join('\n', miss))));
    }

    [Fact]
    public void CommandsOutOfTickOrder_InTheFile_AreRefused()
    {
        string text = Text(Fuzz);
        string[] lines = text.Split('\n');
        // Find two adjacent c lines with different ticks and swap them.
        int i = Enumerable.Range(0, lines.Length - 1).First(j =>
            lines[j].StartsWith("c ", StringComparison.Ordinal) && lines[j + 1].StartsWith("c ", StringComparison.Ordinal)
            && lines[j].Split(' ')[1] != lines[j + 1].Split(' ')[1]);
        (lines[i], lines[i + 1]) = (lines[i + 1], lines[i]);
        Assert.Equal(ReplayError.InvalidCommand, Read(Reseal(string.Join('\n', lines))));
    }

    [Theory]
    [InlineData("data-hash", "lower")]
    [InlineData("checksum", "lower")]
    [InlineData("data-hash", "short")]
    [InlineData("checksum", "short")]
    [InlineData("checksum", "0x")]
    public void NonCanonicalHex_IsRefused(string key, string how)
    {
        string text = Text(Fuzz);
        string line = text.Split('\n').First(l => l.StartsWith(key + " ", StringComparison.Ordinal));
        string hex = line[(key.Length + 1)..];
        string bad = how switch
        {
            "lower" => hex.ToLowerInvariant() == hex ? hex.Replace('0', 'a') : hex.ToLowerInvariant(),
            "short" => hex.TrimStart('0').Length == hex.Length ? hex[1..] : hex.TrimStart('0'),
            _ => "0x" + hex,
        };
        string edited = text.Replace(line + "\n", $"{key} {bad}\n");
        byte[] bytes = key == "checksum" ? Encoding.ASCII.GetBytes(edited) : Reseal(edited);
        Assert.NotEqual(ReplayError.None, Read(bytes));
    }

    [Fact]
    public void TwoChecksumLines_OrTextAfterTheChecksum_AreRefused()
    {
        string text = Text(Fuzz);
        string checksumLine = text[text.LastIndexOf("checksum ", StringComparison.Ordinal)..];
        Assert.NotEqual(ReplayError.None, Read(Encoding.ASCII.GetBytes(text + checksumLine)));
        Assert.NotEqual(ReplayError.None, Read(Encoding.ASCII.GetBytes(text + "end\n")));
        Assert.NotEqual(ReplayError.None, Read(Encoding.ASCII.GetBytes(text + "\n")));
    }

    [Theory]
    [InlineData(0, "c 1 0 0 0 0 00000000 00000000 0 0 0")] // 10 fields
    [InlineData(0, "c 1 0 0 0 0 00000000 00000000 0")] // 8 fields
    [InlineData(0, "C 1 0 0 0 0 00000000 00000000 0 0")]
    [InlineData(0, "c 1 0 0 0 0 0 0 0 0")] // floats must be 8 hex digits
    [InlineData(0, "c 1 0 0 0 0 3f800000 00000000 0 0")] // lowercase float bits
    [InlineData(0, "c 1 0 0 0 0 00000000 00000000 2147483648 0")] // int overflow
    [InlineData(0, "c 1 0 0 0 0 00000000 00000000 0 -0")]
    public void MalformedCommandLine_IsRefusedAsMalformed(int _, string line)
    {
        string text = Text(Fuzz);
        string first = text.Split('\n').First(l => l.StartsWith("c ", StringComparison.Ordinal));
        Assert.Equal(ReplayError.Malformed, Read(Reseal(text.Replace(first + "\n", line + "\n"))));
    }

    [Theory]
    [InlineData("seed 77\n", "seed 18446744073709551616\n")]
    [InlineData("seed 77\n", "seed -1\n")]
    [InlineData("seed 77\n", "seed 077\n")]
    [InlineData("ticks 400\n", "ticks -400\n")]
    [InlineData("commands ", "commands -1\ncommands ")]
    public void OutOfRangeOrNonCanonicalNumbers_AreRefused(string find, string replace)
    {
        string text = Text(Fuzz);
        Assert.Contains(find, text);
        Assert.NotEqual(ReplayError.None, Read(Reseal(text.Replace(find, replace))));
    }

    [Fact]
    public void PlayerAtOrAbovePlayerCount_IsRejectedAtRead_ForEveryCommandLine()
    {
        string text = Text(Fuzz);
        string[] lines = text.Split('\n');
        int checkedLines = 0;
        for (int i = 0; i < lines.Length && checkedLines < 60; i++)
        {
            if (!lines[i].StartsWith("c ", StringComparison.Ordinal)) continue;
            string[] f = lines[i].Split(' ');
            foreach (string p in new[] { "3", "4", "16", "2147483647" })
            {
                string[] g = (string[])f.Clone();
                g[2] = p;
                string[] copy = (string[])lines.Clone();
                copy[i] = string.Join(' ', g);
                Assert.Equal(ReplayError.InvalidCommand, Read(Reseal(string.Join('\n', copy))));
            }
            checkedLines++;
        }
    }

    [Fact]
    public void HugeDeclaredCapacities_AtTheFormatLimit_ReadButOverItRefused()
    {
        string text = Text(Fuzz);
        Assert.Equal(ReplayError.None, Read(Reseal(text.Replace("command-capacity 200\n", "command-capacity 1000000\n"))));
        Assert.Equal(ReplayError.InvalidHeader, Read(Reseal(text.Replace("unit-capacity 96\n", "unit-capacity 1000001\n"))));
        Assert.Equal(ReplayError.InvalidHeader, Read(Reseal(text.Replace("command-capacity 200\n", "command-capacity 2147483647\n"))));
        Assert.Equal(ReplayError.InvalidHeader, Read(Reseal(text.Replace("players 3\n", "players 2147483647\n"))));
    }

    /// <summary>A 1 KB file must not be able to declare a playback of years of game time (players and capacities have format limits; ticks don't).</summary>
    [Fact(Skip = "BUG-0040: TickCount and CheckpointInterval have no format limit; a crafted header makes ReplayPlayer.Run spin for hours")]
    public void AbsurdTickCount_IsRefusedAtRead()
    {
        string text = Text(Fuzz);
        string[] lines = text.Split('\n');
        int countLine = Array.FindIndex(lines, l => l.StartsWith("checkpoints ", StringComparison.Ordinal));
        var edited = lines.Take(countLine).ToList();
        edited.Add("checkpoints 1");
        edited.Add("k 2147483647 0000000000000000");
        edited.Add("end");
        string body = string.Join('\n', edited) + "\n";
        body = body.Replace("ticks 400\n", "ticks 2147483647\n").Replace("checkpoint-interval 100\n", "checkpoint-interval 2147483647\n");
        Assert.Equal(ReplayError.InvalidHeader, Read(ReplayFormat.Seal(body)));
    }

    [Fact]
    public void CommandCapacityBelowABurst_IsRefusedAtRead_NotAtPlayback()
    {
        // The fuzz stream has whole-army bursts in one tick; capacity 1 can't hold them.
        string text = Text(Fuzz);
        Assert.Equal(ReplayError.InvalidCommand, Read(Reseal(text.Replace("command-capacity 200\n", "command-capacity 1\n"))));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    [InlineData("fa-IR")]
    public void WriteAndRead_UnderAnotherCurrentCulture_GiveTheSameBytes(string culture)
    {
        byte[] invariant = ReplayFormat.Write(Fuzz);
        CultureInfo keep = CultureInfo.CurrentCulture, keepUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(culture);
            Assert.Equal(invariant, ReplayFormat.Write(Fuzz));
            Assert.Equal(ReplayError.None, ReplayFormat.TryRead(invariant, out Replay? back));
            ReplayTestRun.AssertEqual(Fuzz, back!);
        }
        finally
        {
            CultureInfo.CurrentCulture = keep;
            CultureInfo.CurrentUICulture = keepUi;
        }
    }

    [Fact]
    public void SpecialFloats_RoundTripByBitPattern()
    {
        float[] specials = { float.NaN, BitConverter.Int32BitsToSingle(0x7FC00001), BitConverter.Int32BitsToSingle(unchecked((int)0xFFFFFFFF)),
            float.PositiveInfinity, float.NegativeInfinity, -0f, float.Epsilon, -float.Epsilon, float.MaxValue, float.MinValue };
        var cmds = new List<Command>();
        for (int i = 0; i < specials.Length; i++)
            cmds.Add(new Command { Kind = CommandKind.Move, Player = 0, Tick = 1, Sequence = i, Position = new Vector2(specials[i], specials[^(i + 1)]), Unit = new EntityHandle(int.MinValue + i, int.MaxValue - i) });
        Replay r = new()
        {
            SimVersion = SimInfo.Version, DataHash = 0, Map = MapGenParams.Default, Seed = 0, PlayerCount = 1,
            UnitCapacity = 1, CommandCapacity = specials.Length, CheckpointInterval = 1, TickCount = 1,
            Commands = cmds.ToImmutableArray(), Checkpoints = ImmutableArray.Create(new ReplayCheckpoint(1, 0)),
        };
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(r), out Replay? back));
        ReplayTestRun.AssertEqual(r, back!);
    }

    [Fact]
    public void TryReadFile_MissingOrDirectoryPath_IsUnreadable_NoException()
    {
        string missing = Path.Combine(Path.GetTempPath(), "rts-qa-" + Environment.CurrentManagedThreadId + "-no-such.replay");
        Assert.Equal(ReplayError.Unreadable, ReplayFormat.TryReadFile(missing, out Replay? r1));
        Assert.Null(r1);
        Assert.Equal(ReplayError.Unreadable, ReplayFormat.TryReadFile(Path.GetTempPath(), out Replay? r2));
        Assert.Null(r2);
        Assert.Equal(ReplayError.Unreadable, ReplayFormat.TryReadFile("", out _));
        Assert.Equal(ReplayError.Unreadable, ReplayFormat.TryReadFile("bad\0path", out _));
    }

    // ---------------------------------------------------------------- data content hash

    [Fact]
    public void ContentHash_DoesNotDependOnTheDataFolderPath_OrJsonFormatting_OrUnitOrderInTheFile()
    {
        ulong shipped = TestSim.Data.ContentHash();
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        string moved = Path.Combine(Path.GetTempPath(), "rts-data-tests", "qa moved ü " + Guid.NewGuid().ToString("N"));
        Directory.Move(dir.Path, moved);
        try
        {
            DataLoadResult a = DataLoader.LoadAll(moved);
            Assert.True(a.Ok, string.Join("\n", a.Errors));
            Assert.Equal(shipped, a.Data!.ContentHash());
        }
        finally
        {
            Directory.Move(moved, dir.Path);
        }

        // Re-serialized (compact, no whitespace) and with the units array reversed: same defs, same hash.
        foreach (string faction in new[] { "malazan", "whirlwind" })
        {
            dir.EditJson($"factions/{faction}/units.json", root =>
            {
                JsonArray units = root["units"]!.AsArray();
                var items = units.Select(n => n!.DeepClone()).Reverse().ToList();
                units.Clear();
                foreach (JsonNode n in items) units.Add(n);
            });
            dir.EditJson($"factions/{faction}/faction.json", _ => { });
        }
        DataLoadResult b = DataLoader.LoadAll(dir.Path);
        Assert.True(b.Ok, string.Join("\n", b.Errors));
        Assert.Equal(shipped, b.Data!.ContentHash());
    }

    [Fact]
    public void ContentHash_RenamingAFactionFolder_EitherFailsToLoad_OrChangesTheHash()
    {
        // The folder name is the faction's key (an id the data refers to), and folder order sets
        // faction ids, so a consistent rename is a content change. A folder-only rename must not load.
        ulong shipped = TestSim.Data.ContentHash();
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        Directory.Move(dir.FullPath("factions/whirlwind"), dir.FullPath("factions/whirlwind2"));
        Assert.False(DataLoader.LoadAll(dir.Path).Ok, "folder renamed without its id still loads");
        dir.EditJson("factions/whirlwind2/faction.json", root => root["id"] = "whirlwind2");
        DataLoadResult renamed = DataLoader.LoadAll(dir.Path);
        if (renamed.Ok) Assert.NotEqual(shipped, renamed.Data!.ContentHash());
    }

    [Fact]
    public void ContentHash_StringBoundariesAreUnambiguous_AndNullDiffersFromEmpty()
    {
        GameData d = TestSim.Data;
        FactionDef f = d.Factions[0];
        GameData With(FactionDef nf) => new() { DamageTable = d.DamageTable, Rules = d.Rules, Factions = d.Factions.SetItem(0, nf), Units = d.Units };
        FactionDef Copy(string name, string desc) => new()
        {
            Id = f.Id, Key = f.Key, DisplayName = name, Description = desc, BonusDisplayName = f.BonusDisplayName,
            BonusDescription = f.BonusDescription, GoldName = f.GoldName, WoodName = f.WoodName, PrimaryColor = f.PrimaryColor,
            SecondaryColor = f.SecondaryColor, AccentColor = f.AccentColor, Units = f.Units,
        };
        Assert.NotEqual(With(Copy("ab", "c")).ContentHash(), With(Copy("a", "bc")).ContentHash());
        Assert.NotEqual(With(Copy("", "abc")).ContentHash(), With(Copy("abc", "")).ContentHash());
        // A non-ASCII display name hashes by UTF-16 code unit, so two names differing only above 0xFF differ.
        Assert.NotEqual(With(Copy("Ł", "x")).ContentHash(), With(Copy("A", "x")).ContentHash());

        UnitDef u = d.Units[0];
        AttackDef a = u.Attack;
        AttackDef WithProjectile(string? p) => new()
        {
            Value = a.Value, DamageType = a.DamageType, CooldownTicks = a.CooldownTicks, WindupTicks = a.WindupTicks, Range = a.Range,
            MinRange = a.MinRange, Splash = a.Splash, FriendlyFire = a.FriendlyFire, Projectile = p, BonusVs = a.BonusVs,
        };
        GameData WithAttack(AttackDef na)
        {
            UnitDef nu = new()
            {
                Id = u.Id, Key = u.Key, Faction = u.Faction, Slot = u.Slot, DisplayName = u.DisplayName, Description = u.Description,
                Model = u.Model, Hp = u.Hp, Armor = u.Armor, ArmorClass = u.ArmorClass, Attack = na, SpeedPerTick = u.SpeedPerTick,
                Sight = u.Sight, Radius = u.Radius, CostGold = u.CostGold, CostWood = u.CostWood, HalfPop = u.HalfPop,
                TrainTicks = u.TrainTicks, TrainedAt = u.TrainedAt, Requires = u.Requires, Tags = u.Tags,
            };
            return new() { DamageTable = d.DamageTable, Rules = d.Rules, Factions = d.Factions, Units = d.Units.SetItem(0, nu) };
        }
        Assert.NotEqual(WithAttack(WithProjectile(null)).ContentHash(), WithAttack(WithProjectile("")).ContentHash());
    }

    [Fact]
    public void ContentHash_IsPureAndRepeatable()
    {
        ulong a = TestSim.Data.ContentHash();
        for (int i = 0; i < 5; i++) Assert.Equal(a, TestSim.Data.ContentHash());
        Assert.NotEqual(0UL, a);
    }

    // ---------------------------------------------------------------- seed mixing (BUG-0014)

    private static uint[] Draws(ulong seed, ulong stream, int n)
    {
        var r = new SimRng(seed, stream);
        var d = new uint[n];
        for (int i = 0; i < n; i++) d[i] = r.NextUInt();
        return d;
    }

    /// <summary>True when b's first 8 draws appear anywhere in a's first 8 + maxShift draws.</summary>
    private static bool IsShift(uint[] a, uint[] b, int maxShift)
    {
        for (int k = 0; k <= maxShift; k++)
        {
            bool same = true;
            for (int i = 0; i < 8 && same; i++) same = a[k + i] == b[i];
            if (same) return true;
        }
        return false;
    }

    [Fact]
    public void SeedMixing_1000RandomSeedPairs_AllStreams_NeverEqualAndNeverShiftsWithin64Draws()
    {
        int streams = RngStream.Count(Replay.MaxPlayers);
        var pick = new SimRng(424242, 9);
        var seeds = new List<(ulong, ulong)>();
        for (int i = 0; i < 1000; i++)
        {
            ulong a = ((ulong)pick.NextUInt() << 32) | pick.NextUInt();
            ulong b = (i % 4) switch
            {
                0 => ((ulong)pick.NextUInt() << 32) | pick.NextUInt(),
                1 => unchecked(a + 1),
                2 => a ^ (1UL << pick.NextInt(0, 64)),
                _ => unchecked(a - (ulong)pick.NextInt(1, 1000)),
            };
            if (a == b) b = ~a;
            seeds.Add((a, b));
        }
        seeds.Add((0, ulong.MaxValue));
        seeds.Add((0, 1));
        seeds.Add((ulong.MaxValue, ulong.MaxValue - 1));
        seeds.Add((1UL << 63, (1UL << 63) - 1));
        foreach ((ulong a, ulong b) in seeds)
        {
            for (ulong s = 0; s < (ulong)streams; s++)
            {
                uint[] da = Draws(a, s, 72), db = Draws(b, s, 72);
                Assert.False(IsShift(da, db, 64), $"seed {b} stream {s} is a shift of seed {a}");
                Assert.False(IsShift(db, da, 64), $"seed {a} stream {s} is a shift of seed {b}");
            }
        }
    }

    [Fact]
    public void SeedMixing_MaxValueAndZero_DifferOnEveryStream_AndGiveDifferentWorlds()
    {
        for (ulong s = 0; s < (ulong)RngStream.Count(Replay.MaxPlayers); s++)
            Assert.NotEqual(Draws(0, s, 8), Draws(ulong.MaxValue, s, 8));
        var a = new Simulation(TestSim.Config(Seed: 0, PlayerCount: 2, UnitCapacity: 4, CommandCapacity: 4));
        var b = new Simulation(TestSim.Config(Seed: ulong.MaxValue, PlayerCount: 2, UnitCapacity: 4, CommandCapacity: 4));
        Assert.NotEqual(a.StateHash(), b.StateHash());
        Assert.NotEqual(a.World.Heightmap.ContentHash(), b.World.Heightmap.ContentHash());
    }

    [Fact]
    public void MixSeed_IsABijectionOnASample_AndPreMixInvertsItEverywhereTested()
    {
        var seen = new HashSet<ulong>();
        for (ulong s = 0; s < 200_000; s++) Assert.True(seen.Add(SimRng.MixSeed(s)), $"collision at {s}");
        var pick = new SimRng(5, 5);
        for (int i = 0; i < 10_000; i++)
        {
            ulong x = ((ulong)pick.NextUInt() << 32) | pick.NextUInt();
            Assert.Equal(x, SimRng.MixSeed(TestSeeds.PreMix(x)));
            Assert.Equal(x, TestSeeds.PreMix(SimRng.MixSeed(x)));
        }
    }
}
