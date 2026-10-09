using System.Diagnostics;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Rts.Cli;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.Replays;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA attacks on M1-8's headless CLI (tools/Rts.Cli): argument fuzz, unwritable and odd paths,
/// broken data, every truncation of a replay, format-1 files, CLI-vs-direct determinism over seeds
/// 1-5 at 200 / 500 units, and two separate OS processes agreeing.
/// </summary>
public class CliQaTests
{
    private static readonly Regex HashLine = new(@"^tick (\d+) hash ([0-9A-F]{16})$");

    private readonly record struct CliResult(int Exit, string Out, string Err);

    private static CliResult Cli(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exit = CliRunner.Run(args, stdout, stderr);
        return new CliResult(exit, stdout.ToString(), stderr.ToString());
    }

    private static string[] Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();

    private static string[] HashLines(string text) => Lines(text).Where(l => HashLine.IsMatch(l)).ToArray();

    private static string OneErrorLine(CliResult r)
    {
        string[] lines = Lines(r.Err);
        Assert.True(lines.Length == 1, $"expected one stderr line, got {lines.Length}:\n{r.Err}");
        Assert.DoesNotContain("Exception", lines[0]);
        Assert.DoesNotContain("   at ", lines[0]);
        return lines[0];
    }

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "rts-cli-qa", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>The CLI scenario rebuilt independently from docs/03 "Headless CLI": one player, west block, round-robin types, Move at TickNumber 2 to the farthest cell from the mid-west edge.</summary>
    private static List<string> DirectRun(ulong seed, int units, int ticks, int checkpoint)
    {
        var sim = new Simulation(TestSim.Config(seed, 1, units, units));
        NavGrid g = sim.World.NavGrid;
        Vector2[] block = StartLayout.Block(g, units, west: true, TestSim.Data.Units.Max(u => u.Radius));
        for (int k = 0; k < block.Length; k++) sim.Enqueue(Command.SpawnUnit(0, k % TestSim.UnitTypeCount, block[k]));
        FlowField from = FlowField.Build(g, FlowField.NearestPassable(g, g.Height / 2 * g.Width + 1));
        int goal = -1;
        float far = -1f;
        for (int c = 0; c < g.Width * g.Height; c++)
        {
            float cost = from.CostAt(c);
            if (!float.IsPositiveInfinity(cost) && cost > far) { far = cost; goal = c; }
        }
        var lines = new List<string>();
        while (sim.TickNumber < ticks)
        {
            if (sim.TickNumber == 2)
            {
                UnitStore u = sim.World.Units;
                for (int i = 0; i < u.Capacity; i++)
                    if (u.Alive[i]) sim.Enqueue(Command.Move(0, new EntityHandle(i, u.Generation[i]), g.CellCenter(goal % g.Width, goal / g.Width)));
            }
            sim.Tick();
            if (sim.TickNumber % checkpoint == 0) lines.Add($"tick {sim.TickNumber} hash {sim.StateHash():X16}");
        }
        return lines;
    }

    [Theory]
    [InlineData(1UL, 200)] [InlineData(2UL, 200)] [InlineData(3UL, 200)] [InlineData(4UL, 200)] [InlineData(5UL, 200)]
    [InlineData(1UL, 500)] [InlineData(2UL, 500)] [InlineData(3UL, 500)] [InlineData(4UL, 500)] [InlineData(5UL, 500)]
    public void CliHashes_EqualDirectRun_AndRecordingDoesNotChangeThem(ulong seed, int units)
    {
        const int ticks = 600, checkpoint = 50;
        string[] args = { "run", "--seed", seed.ToString(), "--units", units.ToString(), "--ticks", ticks.ToString(), "--checkpoint", checkpoint.ToString(), "--data", TestDataDir.Shipped };
        CliResult plain = Cli(args);
        Assert.Equal(0, plain.Exit);
        Assert.Equal(DirectRun(seed, units, ticks, checkpoint), HashLines(plain.Out));

        string path = Path.Combine(TempDir(), "r.replay");
        CliResult recorded = Cli(args.Concat(new[] { "--record", path }).ToArray());
        Assert.Equal(0, recorded.Exit);
        Assert.Equal(HashLines(plain.Out), HashLines(recorded.Out));
        CliResult played = Cli("play", path, "--data", TestDataDir.Shipped);
        Assert.Equal(0, played.Exit);
        Assert.Equal(HashLines(plain.Out), HashLines(played.Out));
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(6UL)]
    public void TwoPlayers_RecordThenPlay_Matches(ulong seed)
    {
        string path = Path.Combine(TempDir(), "two.replay");
        CliResult r = Cli("run", "--seed", seed.ToString(), "--units", "301", "--players", "2", "--ticks", "400", "--checkpoint", "40", "--record", path, "--data", TestDataDir.Shipped);
        Assert.Equal(0, r.Exit);
        Assert.Contains("units 301 players 2", r.Out);
        CliResult p = Cli("play", path, "--data", TestDataDir.Shipped);
        Assert.Equal(0, p.Exit);
        Assert.Equal(HashLines(r.Out), HashLines(p.Out));
        Assert.Equal(10, HashLines(p.Out).Length);
    }

    [Theory]
    [InlineData("run --seed 1 --units 100001")]
    [InlineData("run --seed 1 --units 2147483648")]
    [InlineData("run --seed 1 --units -2147483649")]
    [InlineData("run --seed 1 --units 1e3")]
    [InlineData("run --seed 1 --units 0x10")]
    [InlineData("run --seed 1 --units 10,0")]
    [InlineData("run --seed 1 --units ５")]
    [InlineData("run --seed 1 --units 10 --ticks 2147483648")]
    [InlineData("run --seed 1 --units 10 --ticks -5")]
    [InlineData("run --seed 1 --units 10 --checkpoint 1728001")]
    [InlineData("run --seed 1 --units 10 --players 0")]
    [InlineData("run --seed 1 --units 10 --players -1")]
    [InlineData("run --seed 18446744073709551616 --units 10")]
    [InlineData("run --seed +1 --units 10")]
    [InlineData("run --seed 1.0 --units 10")]
    [InlineData("run --seed 1 --units 10 --record")]
    [InlineData("run --seed 1 --units 10 --data")]
    [InlineData("run --seed 1 --units 10 --SEED 1")]
    [InlineData("play --data")]
    [InlineData("play x.replay --record y")]
    [InlineData("RUN --seed 1 --units 10")]
    public void BadUsage_MoreRows_Exit1_OneLine(string argLine)
    {
        CliResult r = Cli(argLine.Split(' '));
        Assert.Equal(1, r.Exit);
        Assert.Equal("", r.Out);
        Assert.StartsWith("error: ", OneErrorLine(r));
    }

    [Fact]
    public void Run_UnitsAboveWhatTheBlockHolds_SpawnsWhatFits_AndExits0()
    {
        // 100,000 is the CLI cap; the west block holds far fewer, so the header reports what fit.
        CliResult r = Cli("run", "--seed", "1", "--units", "100000", "--ticks", "3", "--data", TestDataDir.Shipped);
        Assert.Equal(0, r.Exit);
        Match m = Regex.Match(r.Out, @"^seed 1 units (\d+) players 1", RegexOptions.Multiline);
        Assert.True(m.Success, r.Out);
        int spawned = int.Parse(m.Groups[1].Value);
        Assert.InRange(spawned, 1, 99_999);
    }

    [Fact]
    public void Run_UnwritableRecordPaths_Exit1_OneLine()
    {
        string dir = TempDir();
        foreach (string bad in new[] { "", dir, Path.Combine(dir, "no", "such", "dir", "x.replay"), Path.Combine(dir, "a<b>|?.replay"), new string('x', 400) + ".replay" })
        {
            CliResult r = Cli("run", "--seed", "1", "--units", "3", "--ticks", "5", "--record", bad, "--data", TestDataDir.Shipped);
            Assert.True(r.Exit == 1, $"'{bad}': exit {r.Exit}\n{r.Err}");
            Assert.StartsWith("error: cannot write replay", OneErrorLine(r));
        }
    }

    [Fact]
    public void RecordAndPlay_NonAsciiPathWithSpaces_RoundTrips()
    {
        string dir = Path.Combine(TempDir(), "Ünïcødé 日本 dir");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "ré play.replay");
        CliResult r = Cli("run", "--seed", "2", "--units", "20", "--ticks", "120", "--checkpoint", "30", "--record", path, "--data", TestDataDir.Shipped);
        Assert.Equal(0, r.Exit);
        CliResult p = Cli("play", path, "--data", TestDataDir.Shipped);
        Assert.Equal(0, p.Exit);
        Assert.Equal(HashLines(r.Out), HashLines(p.Out));
    }

    [Fact]
    public void BrokenJsonDataDir_Exits1_NamingTheFile_ForRunAndPlay()
    {
        using TestDataDir data = TestDataDir.CopyOfShipped();
        File.WriteAllText(data.FullPath("factions/malazan/units.json"), "{ \"units\": [ {");
        File.WriteAllText(data.FullPath("common/rules.json"), "not json");
        CliResult r = Cli("run", "--seed", "1", "--units", "5", "--data", data.Path);
        Assert.Equal(1, r.Exit);
        Assert.Equal("", r.Out);
        string err = OneErrorLine(r);
        Assert.Contains("did not load", err);
        Assert.Matches(@"\((\d+) errors\)", err);
        Assert.True(err.Contains("rules.json") || err.Contains("units.json"), err);

        string path = Path.Combine(TempDir(), "ok.replay");
        Assert.Equal(0, Cli("run", "--seed", "1", "--units", "5", "--ticks", "20", "--checkpoint", "10", "--record", path, "--data", TestDataDir.Shipped).Exit);
        CliResult p = Cli("play", path, "--data", data.Path);
        Assert.Equal(1, p.Exit);
        Assert.Contains("did not load", OneErrorLine(p));
    }

    [Fact]
    public void Play_EveryTruncation_Exits2_OneLine_NeverThrows()
    {
        string path = Path.Combine(TempDir(), "small.replay");
        Assert.Equal(0, Cli("run", "--seed", "3", "--units", "3", "--ticks", "40", "--checkpoint", "10", "--record", path, "--data", TestDataDir.Shipped).Exit);
        byte[] bytes = File.ReadAllBytes(path);
        Assert.InRange(bytes.Length, 200, 20_000);
        string cut = Path.Combine(TempDir(), "cut.replay");
        var codes = new Dictionary<int, int>();
        for (int len = 0; len < bytes.Length; len++)
        {
            File.WriteAllBytes(cut, bytes[..len]);
            CliResult r = Cli("play", cut, "--data", TestDataDir.Shipped);
            Assert.True(r.Exit == 2, $"length {len}: exit {r.Exit}\n{r.Err}");
            Assert.StartsWith("replay failed: ", OneErrorLine(r));
            Assert.Equal("", r.Out);
            codes[r.Exit] = codes.GetValueOrDefault(r.Exit) + 1;
        }
        Assert.Equal(bytes.Length, codes[2]);
    }

    [Fact]
    public void Play_Format1File_Exits2_FormatVersionMismatch()
    {
        string path = Path.Combine(TempDir(), "v2.replay");
        Assert.Equal(0, Cli("run", "--seed", "4", "--units", "4", "--ticks", "30", "--checkpoint", "10", "--record", path, "--data", TestDataDir.Shipped).Exit);
        string text = File.ReadAllText(path);
        string body = text[..text.IndexOf("checksum ", StringComparison.Ordinal)];
        Assert.StartsWith($"rts-replay {Replay.CurrentFormatVersion}\n", body); // M3-1: format 3
        // Format 1 had no flags field on command lines (M1-7 added it).
        var sb = new StringBuilder("rts-replay 1\n");
        foreach (string line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1))
            sb.Append(line.StartsWith("c ", StringComparison.Ordinal) ? line[..line.LastIndexOf(' ')] : line).Append('\n');
        string v1 = Path.Combine(TempDir(), "v1.replay");
        File.WriteAllBytes(v1, ReplayFormat.Seal(sb.ToString()));

        CliResult r = Cli("play", v1, "--data", TestDataDir.Shipped);
        Assert.Equal(2, r.Exit);
        Assert.Contains("FormatVersionMismatch", OneErrorLine(r));
    }

    /// <summary>Checksum-valid replays whose command fields are hostile: play must exit 0 or 2 with at most one stderr line, never throw.</summary>
    [Theory]
    [InlineData(5, "9999")]       // type id past the data
    [InlineData(5, "-1")]         // negative type id
    [InlineData(6, "7FC00000")]   // NaN x
    [InlineData(6, "7F800000")]   // +inf x
    [InlineData(7, "FF800000")]   // -inf y
    [InlineData(6, "7F7FFFFF")]   // float.MaxValue x
    [InlineData(8, "-5")]         // unit index (spawn ignores it)
    [InlineData(13, "1")]         // queued flag on a spawn (field 13 since format 4)
    public void Play_HostileCommandFields_NeverThrow(int field, string value)
    {
        string path = Path.Combine(TempDir(), "h.replay");
        Assert.Equal(0, Cli("run", "--seed", "4", "--units", "4", "--ticks", "30", "--checkpoint", "10", "--record", path, "--data", TestDataDir.Shipped).Exit);
        string text = File.ReadAllText(path);
        string body = text[..text.IndexOf("checksum ", StringComparison.Ordinal)];
        string[] lines = body.Split('\n');
        int first = Array.FindIndex(lines, l => l.StartsWith("c ", StringComparison.Ordinal));
        string[] f = lines[first].Split(' ');
        f[field] = value;
        lines[first] = string.Join(' ', f);
        string hostile = Path.Combine(TempDir(), "hostile.replay");
        File.WriteAllBytes(hostile, ReplayFormat.Seal(string.Join('\n', lines)));

        CliResult r = Cli("play", hostile, "--data", TestDataDir.Shipped);
        Assert.True(r.Exit is 0 or 2, $"exit {r.Exit}: {r.Err}");
        if (r.Exit == 2) Assert.StartsWith("replay failed: ", OneErrorLine(r));
    }

    /// <summary>
    /// M1-9 (BUG-0054 / BUG-0056): a command row with an undefined kind or flag bits its kind doesn't
    /// take (what Enqueue now refuses) is refused at read: play exits 2 with one "InvalidCommand" line,
    /// never throws (the player would otherwise hit Enqueue's ArgumentException, which it doesn't catch).
    /// </summary>
    [Theory]
    [InlineData(4, "99")]          // undefined kind
    [InlineData(4, "-1")]          // negative kind
    [InlineData(4, "18")]          // one past the last kind (17, UseAbility, since M4-4a)
    [InlineData(4, "-2147483648")] // int.MinValue kind
    [InlineData(13, "2")]          // unknown flag bit
    [InlineData(13, "-1")]         // every flag bit
    [InlineData(13, "1")]          // queued flag on a spawn
    public void Play_MalformedKindOrFlags_Exit2_InvalidCommand(int field, string value)
    {
        string path = Path.Combine(TempDir(), "k.replay");
        Assert.Equal(0, Cli("run", "--seed", "4", "--units", "4", "--ticks", "30", "--checkpoint", "10", "--record", path, "--data", TestDataDir.Shipped).Exit);
        string text = File.ReadAllText(path);
        string body = text[..text.IndexOf("checksum ", StringComparison.Ordinal)];
        string[] lines = body.Split('\n');
        int first = Array.FindIndex(lines, l => l.StartsWith("c ", StringComparison.Ordinal));
        string[] f = lines[first].Split(' ');
        f[field] = value;
        lines[first] = string.Join(' ', f);
        string hostile = Path.Combine(TempDir(), "hostile.replay");
        File.WriteAllBytes(hostile, ReplayFormat.Seal(string.Join('\n', lines)));

        CliResult r = Cli("play", hostile, "--data", TestDataDir.Shipped);
        Assert.True(r.Exit == 2, $"exit {r.Exit}: {r.Err}");
        Assert.Contains("InvalidCommand", OneErrorLine(r));
    }

    /// <summary>Hostile Move commands: the unit handle and target are attacker-chosen; play must not throw.</summary>
    [Theory]
    [InlineData("2147483647", "0", "42F20000", "42FE0000")]
    [InlineData("-1", "0", "42F20000", "42FE0000")]
    [InlineData("0", "-7", "42F20000", "42FE0000")]
    [InlineData("0", "0", "7FC00000", "7FC00000")]
    [InlineData("0", "0", "7F800000", "FF800000")]
    [InlineData("0", "0", "CB000000", "4B000000")]
    public void Play_HostileMoveCommands_NeverThrow(string unitIndex, string generation, string xBits, string yBits)
    {
        string path = Path.Combine(TempDir(), "m.replay");
        Assert.Equal(0, Cli("run", "--seed", "4", "--units", "4", "--ticks", "30", "--checkpoint", "10", "--record", path, "--data", TestDataDir.Shipped).Exit);
        string text = File.ReadAllText(path);
        string body = text[..text.IndexOf("checksum ", StringComparison.Ordinal)];
        string[] lines = body.Split('\n');
        int move = Array.FindIndex(lines, l => l.StartsWith("c ", StringComparison.Ordinal) && l.Split(' ')[4] == ((int)CommandKind.Move).ToString());
        Assert.True(move > 0, "the CLI run should log Move commands");
        string[] f = lines[move].Split(' ');
        f[6] = xBits; f[7] = yBits; f[8] = unitIndex; f[9] = generation;
        lines[move] = string.Join(' ', f);
        string hostile = Path.Combine(TempDir(), "hostile.replay");
        File.WriteAllBytes(hostile, ReplayFormat.Seal(string.Join('\n', lines)));

        CliResult r = Cli("play", hostile, "--data", TestDataDir.Shipped);
        Assert.True(r.Exit is 0 or 2, $"exit {r.Exit}: {r.Err}");
        if (r.Exit == 2) Assert.StartsWith("replay failed: ", OneErrorLine(r));
    }

    [Fact]
    public void Play_WrongDataHash_Exits2_DataMismatch()
    {
        string path = Path.Combine(TempDir(), "d.replay");
        Assert.Equal(0, Cli("run", "--seed", "4", "--units", "4", "--ticks", "20", "--checkpoint", "10", "--record", path, "--data", TestDataDir.Shipped).Exit);
        using TestDataDir data = TestDataDir.CopyOfShipped();
        string unitsFile = data.FullPath("factions/malazan/units.json");
        string json = File.ReadAllText(unitsFile);
        Match hp = Regex.Match(json, "\"hp\"\\s*:\\s*(\\d+)");
        Assert.True(hp.Success);
        File.WriteAllText(unitsFile, json[..hp.Groups[1].Index] + (int.Parse(hp.Groups[1].Value) + 1) + json[(hp.Groups[1].Index + hp.Groups[1].Length)..]);
        CliResult r = Cli("play", path, "--data", data.Path);
        Assert.Equal(2, r.Exit);
        Assert.Contains("DataMismatch", OneErrorLine(r));
    }

    [Fact]
    public void TwoSeparateProcesses_PrintIdenticalHashLines_FromARelativeNonAsciiWorkingDir()
    {
        string cliDll = typeof(CliRunner).Assembly.Location;
        Assert.True(File.Exists(Path.ChangeExtension(cliDll, ".runtimeconfig.json")), "Rts.Cli runtimeconfig missing next to the dll");
        string work = Path.Combine(TempDir(), "wörk dir");
        Directory.CreateDirectory(Path.Combine(work, "out"));

        string[] outputs = new string[2];
        for (int run = 0; run < 2; run++)
        {
            var psi = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = work,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (string a in new[] { cliDll, "run", "--seed", "5", "--units", "200", "--ticks", "300", "--checkpoint", "50",
                         "--record", Path.Combine("out", $"r{run}.replay"), "--data", TestDataDir.Shipped })
                psi.ArgumentList.Add(a);
            using Process p = Process.Start(psi)!;
            string stdout = p.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();
            Assert.True(p.WaitForExit(120_000), "CLI process hung");
            Assert.True(p.ExitCode == 0, $"exit {p.ExitCode}: {stderr}");
            outputs[run] = string.Join("\n", HashLines(stdout));
            Assert.True(File.Exists(Path.Combine(work, "out", $"r{run}.replay")), "relative --record path not resolved against the working dir");
        }
        Assert.Equal(6, outputs[0].Split('\n').Length);
        Assert.Equal(outputs[0], outputs[1]);
        Assert.Equal(string.Join("\n", DirectRun(5, 200, 300, 50)), outputs[0]);
    }
}
