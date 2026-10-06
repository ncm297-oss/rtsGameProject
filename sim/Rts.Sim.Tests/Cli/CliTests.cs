using System.Numerics;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using Rts.Cli;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.Replays;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.Cli;

/// <summary>The headless CLI (M1-8, tools/Rts.Cli), called in-process through <see cref="CliRunner.Run"/>.</summary>
/// <remarks>Every temp file a test writes is deleted when it ends (BUG-0057); the shared recording is kept in memory.</remarks>
public sealed class CliTests : IDisposable
{
    private static readonly Regex HashLine = new(@"^tick (\d+) hash ([0-9A-F]{16})$");
    private static readonly Regex TimingLine = new(@"^ticks (\d+) avg \d+\.\d{3} ms p99 \d+\.\d{3} ms worst \d+\.\d{3} ms$");

    private static readonly string[] AcceptanceArgs = { "run", "--seed", "1", "--units", "200", "--ticks", "1500" };

    /// <summary>One recorded acceptance run shared by the replay tests: the file's bytes (each test writes its own copy) and the hash lines printed.</summary>
    private static readonly Lazy<(byte[] Bytes, string[] HashLines)> s_recorded = new(() =>
    {
        string path = NewTempPath();
        try
        {
            CliResult r = Cli(With(AcceptanceArgs, "--record", path, "--data", TestDataDir.Shipped));
            Assert.Equal(0, r.Exit);
            return (File.ReadAllBytes(path), HashLines(r.Out));
        }
        finally
        {
            File.Delete(path);
        }
    });

    private readonly List<string> _tempFiles = new();

    /// <summary>Deletes the temp files this test wrote.</summary>
    public void Dispose()
    {
        foreach (string path in _tempFiles) File.Delete(path);
    }

    /// <summary>A copy of the shared recording in a temp file of this test.</summary>
    private string RecordedCopy()
    {
        string path = TempPath();
        File.WriteAllBytes(path, s_recorded.Value.Bytes);
        return path;
    }

    [Fact]
    public void Run_PrintsTheSameHashesAsADirectSim()
    {
        CliResult r = Cli(With(AcceptanceArgs, "--data", TestDataDir.Shipped));
        Assert.Equal(0, r.Exit);
        Assert.Equal("", r.Err);
        string[] hashes = HashLines(r.Out);
        Assert.Equal(15, hashes.Length);
        Assert.Single(Lines(r.Out), l => TimingLine.IsMatch(l) && l.StartsWith("ticks 1500 ", StringComparison.Ordinal));

        // The same scenario built here from the docs' rule, fed only Command factories.
        var sim = new Simulation(TestSim.Config(Seed: 1, PlayerCount: 1, UnitCapacity: 200, CommandCapacity: 200));
        NavGrid g = sim.World.NavGrid;
        float maxRadius = TestSim.Data.Units.Max(u => u.Radius);
        Vector2[] block = StartLayout.Block(g, 200, west: true, maxRadius);
        Assert.Equal(200, block.Length);
        for (int k = 0; k < block.Length; k++)
            sim.Enqueue(Command.SpawnUnit(0, k % TestSim.UnitTypeCount, block[k]));
        sim.Tick();
        sim.Tick();
        FlowField fromWest = FlowField.Build(g, FlowField.NearestPassable(g, g.Height / 2 * g.Width + 1));
        int goal = Enumerable.Range(0, g.Width * g.Height)
            .Where(c => !float.IsPositiveInfinity(fromWest.CostAt(c)))
            .OrderByDescending(c => fromWest.CostAt(c)).ThenBy(c => c).First();
        Assert.True(goal % g.Width > g.Width / 2, "the march should end on the east half");
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i]) sim.Enqueue(Command.Move(0, new EntityHandle(i, u.Generation[i]), g.CellCenter(goal % g.Width, goal / g.Width)));

        var expected = new List<string>();
        while (sim.TickNumber < 1500)
        {
            sim.Tick();
            if (sim.TickNumber % 100 == 0) expected.Add($"tick {sim.TickNumber} hash {sim.StateHash():X16}");
        }
        Assert.Equal(expected, hashes);
    }

    [Fact]
    public void Run_TwiceWithSameArgs_PrintsByteIdenticalHashLines()
    {
        // No --data: also checks the default game/data lookup from the test binaries.
        string[] args = { "run", "--seed", "9", "--units", "120", "--ticks", "400", "--players", "2", "--checkpoint", "50" };
        CliResult a = Cli(args), b = Cli(args);
        Assert.Equal(0, a.Exit);
        Assert.Equal(0, b.Exit);
        string[] ha = HashLines(a.Out), hb = HashLines(b.Out);
        Assert.Equal(8, ha.Length);
        Assert.Equal(Encoding.ASCII.GetBytes(string.Join("\n", ha)), Encoding.ASCII.GetBytes(string.Join("\n", hb)));
    }

    [Fact]
    public void RecordThenPlay_MatchesEveryCheckpoint()
    {
        string[] recordedHashes = s_recorded.Value.HashLines;
        string path = RecordedCopy();
        Assert.Equal(15, recordedHashes.Length);
        CliResult r = Cli("play", path, "--data", TestDataDir.Shipped);
        Assert.Equal(0, r.Exit);
        Assert.Equal("", r.Err);
        Assert.Equal(recordedHashes, HashLines(r.Out));
        Assert.Contains("ok: 15 checkpoints matched over 1500 ticks", r.Out);
    }

    [Fact]
    public void Play_EditedCheckpointHash_Exits2NamingCheckpointMismatchAndTick()
    {
        string text = Encoding.ASCII.GetString(s_recorded.Value.Bytes);
        Match k = Regex.Match(text, @"^k 700 ([0-9A-F]{16})$", RegexOptions.Multiline);
        Assert.True(k.Success);
        string original = k.Groups[1].Value;
        string edited = (original[0] == '0' ? "1" : "0") + original[1..];
        string body = text[..text.IndexOf("checksum ", StringComparison.Ordinal)].Replace($"k 700 {original}\n", $"k 700 {edited}\n");
        string path = TempPath();
        File.WriteAllBytes(path, ReplayFormat.Seal(body)); // valid checksum, wrong hash

        CliResult r = Cli("play", path, "--data", TestDataDir.Shipped);
        Assert.Equal(2, r.Exit);
        string err = OneLine(r.Err);
        Assert.Contains("CheckpointMismatch", err);
        Assert.Contains("tick 700", err);
        Assert.Contains(edited, err);
        Assert.Contains(original, err);
    }

    [Fact]
    public void Play_TruncatedFile_Exits2()
    {
        byte[] bytes = s_recorded.Value.Bytes;
        string path = TempPath();
        File.WriteAllBytes(path, bytes[..(bytes.Length / 2)]);
        CliResult r = Cli("play", path, "--data", TestDataDir.Shipped);
        Assert.Equal(2, r.Exit);
        Assert.Contains("Truncated", OneLine(r.Err));
    }

    [Fact]
    public void Play_MissingFile_Exits1()
    {
        CliResult r = Cli("play", Path.Combine(Path.GetTempPath(), "rts-cli-tests", "no-such.replay"), "--data", TestDataDir.Shipped);
        Assert.Equal(1, r.Exit);
        Assert.StartsWith("error: cannot read replay", OneLine(r.Err));
    }

    [Fact]
    public void Play_MissingDataDir_Exits1()
    {
        CliResult r = Cli("play", RecordedCopy(), "--data", Path.Combine(Path.GetTempPath(), "rts-cli-tests", "no-such-data"));
        Assert.Equal(1, r.Exit);
        Assert.Contains("did not load", OneLine(r.Err));
    }

    [Fact]
    public void Run_MissingDataDir_Exits1()
    {
        CliResult r = Cli("run", "--seed", "1", "--units", "5", "--data", Path.Combine(Path.GetTempPath(), "rts-cli-tests", "no-such-data"));
        Assert.Equal(1, r.Exit);
        string err = OneLine(r.Err);
        Assert.Contains("did not load", err);
        Assert.Equal("", r.Out);
        // BUG-0057: no empty fields (": : ") and no "1 errors".
        Assert.Contains("(1 error)", err);
        Assert.DoesNotContain(": :", err);
    }

    /// <summary>BUG-0057: an unwritable --record path (missing folder, or a folder) fails at once, before a single tick runs.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Run_UnwritableRecordPath_FailsBeforeTicking(bool missingFolder)
    {
        string dir = Path.Combine(Path.GetTempPath(), "rts-cli-tests");
        Directory.CreateDirectory(dir);
        string path = missingFolder ? Path.Combine(dir, "no-such-folder", "x" + ReplayFormat.FileExtension) : dir;
        CliResult r = Cli("run", "--seed", "1", "--units", "5", "--ticks", "200", "--record", path, "--data", TestDataDir.Shipped);
        Assert.Equal(1, r.Exit);
        Assert.StartsWith($"error: cannot write replay '{path}'", OneLine(r.Err));
        Assert.Equal("", r.Out); // nothing ran: no header, no checkpoint, no timing line
    }

    /// <summary>BUG-0057: a replay shorter than one checkpoint interval plays back "ok", but says nothing was compared.</summary>
    [Fact]
    public void Play_ReplayWithNoCheckpoints_SaysNothingWasCompared()
    {
        string path = TempPath();
        Assert.Equal(0, Cli("run", "--seed", "1", "--units", "5", "--ticks", "50", "--record", path, "--data", TestDataDir.Shipped).Exit);
        CliResult r = Cli("play", path, "--data", TestDataDir.Shipped);
        Assert.Equal(0, r.Exit);
        Assert.Equal("ok: 0 checkpoints (nothing compared) over 50 ticks", Lines(r.Out).Last());
    }

    [Theory]
    [InlineData("run --seed 1 --units -1")]
    [InlineData("run --seed 1 --units 0")]
    [InlineData("run --seed 1 --units 10 --ticks 0")]
    [InlineData("run --seed 1 --units 10 --ticks 1728001")]
    [InlineData("run --seed 1 --units 10 --players 3")]
    [InlineData("run --seed 1 --units 10 --checkpoint 0")]
    [InlineData("run --seed -1 --units 10")]
    [InlineData("run --seed x --units 10")]
    [InlineData("run --units 10")]
    [InlineData("run --seed 1")]
    [InlineData("run --seed")]
    [InlineData("run --seed 1 --units")]
    [InlineData("run --seed 1 --units --ticks 5")]
    [InlineData("run --seed 1 --units 10 --units 10")]
    [InlineData("run --seed 1 --units 10 --bogus 3")]
    [InlineData("run --seed 1 --units 10 extra")]
    [InlineData("fly")]
    [InlineData("")]
    [InlineData("play")]
    [InlineData("play a.replay b.replay")]
    [InlineData("play a.replay --seed 1")]
    public void BadUsage_Exits1_WithOneUsageLine(string argLine)
    {
        string[] args = argLine.Length == 0 ? Array.Empty<string>() : argLine.Split(' ');
        CliResult r = Cli(args);
        Assert.Equal(1, r.Exit);
        Assert.Equal("", r.Out);
        string err = OneLine(r.Err);
        Assert.StartsWith("error: ", err);
        Assert.Contains(CliRunner.Usage, err);
        Assert.DoesNotContain("Exception", err);
    }

    [Fact]
    public void Help_PrintsUsage_Exits0()
    {
        CliResult r = Cli("--help");
        Assert.Equal(0, r.Exit);
        Assert.Equal(CliRunner.Usage, r.Out.TrimEnd());
    }

    [Fact]
    public void CliProject_IsStrict_AndReferencesOnlyRtsSim()
    {
        XDocument proj = XDocument.Load(Path.Combine(TestDataDir.RepoRoot(), "tools", "Rts.Cli", "Rts.Cli.csproj"));
        Assert.Empty(proj.Descendants("PackageReference"));
        Assert.Empty(proj.Descendants("Reference"));
        Assert.Equal(@"..\..\sim\Rts.Sim\Rts.Sim.csproj", Assert.Single(proj.Descendants("ProjectReference")).Attribute("Include")?.Value);
        Assert.Equal("true", proj.Descendants("TreatWarningsAsErrors").Last().Value.Trim(), ignoreCase: true);
        Assert.Equal("enable", proj.Descendants("Nullable").Last().Value.Trim());
        foreach (AssemblyName r in typeof(CliRunner).Assembly.GetReferencedAssemblies())
            Assert.False((r.Name ?? "").StartsWith("Godot", StringComparison.OrdinalIgnoreCase), $"Rts.Cli references {r.FullName}");
    }

    private readonly record struct CliResult(int Exit, string Out, string Err);

    private static CliResult Cli(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exit = CliRunner.Run(args, stdout, stderr);
        return new CliResult(exit, stdout.ToString(), stderr.ToString());
    }

    private static string[] With(string[] args, params string[] more) => args.Concat(more).ToArray();

    private static string[] Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();

    private static string[] HashLines(string text) => Lines(text).Where(l => HashLine.IsMatch(l)).ToArray();

    /// <summary>Asserts stderr is exactly one line (no stack trace) and returns it.</summary>
    private static string OneLine(string text)
    {
        string[] lines = Lines(text);
        Assert.True(lines.Length == 1, $"expected one stderr line, got {lines.Length}:\n{text}");
        return lines[0];
    }

    /// <summary>A fresh temp file path, deleted when this test ends.</summary>
    private string TempPath()
    {
        string path = NewTempPath();
        _tempFiles.Add(path);
        return path;
    }

    private static string NewTempPath()
    {
        string dir = Path.Combine(Path.GetTempPath(), "rts-cli-tests");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, Guid.NewGuid().ToString("N") + ReplayFormat.FileExtension);
    }
}
