using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>M2-7: <c>--bench</c> parsing, the real game binary's one <c>bench:</c> line and exit code, the script driving the real selection / order / camera code, and the pinned 100-unit frame-time row.</summary>
/// <remarks>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/BenchTest.tscn</c>; prints
/// "BENCH TEST PASS" and exits 0, or prints each failure and exits 1. The frame-time row needs a GPU:
/// headless it prints "BENCH TEST SKIP" for it. Windowed (<c>&amp; $env:GODOT --path game res://tests/BenchTest.tscn</c>)
/// it runs a 10 s bench at 100 units per player with vsync off and needs avg &lt; 16.7 ms, p99 &lt; 33 ms.
/// The process rows start the game itself headless as a child process (<see cref="OS.Execute"/>).
/// </remarks>
public partial class BenchTest : Node
{
    /// <summary>The documented result line.</summary>
    public static readonly Regex LineShape = new(
        @"^bench: seconds (\d+\.\d) frames (\d+) avg (\d+\.\d\d) ms p50 (\d+\.\d\d) ms p99 (\d+\.\d\d) ms worst (\d+\.\d\d) ms fps (\d+\.\d) ticks (\d+) avgTick (\d+\.\d\d\d) ms$");

    private const double PinnedSeconds = 10;
    private const double AvgLimitMs = 16.7, P99LimitMs = 33.0;

    private readonly List<string> _failures = new();
    private GameData _data = null!;

    public override async void _Ready()
    {
        try
        {
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            ParseRows();
            ProcessRows();
            await ScriptDrivesRealCode();
            await PinnedFrameTime();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures) GD.Print($"BENCH TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("BENCH TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private void ParseRows()
    {
        Check(LaunchOptions.Parse(new[] { "--bench", "60" }).BenchSeconds == 60, "--bench 60");
        Check(LaunchOptions.Parse(new[] { "--bench", "2.5", "--mute" }).BenchSeconds == 2.5, "--bench 2.5");
        Check(LaunchOptions.Parse(new[] { "--bench", "3600" }).BenchSeconds == LaunchOptions.MaxBenchSeconds, "--bench 3600 (the cap itself)");
        foreach (string bad in new[] { "0", "-1", "-0.5", "abc", "NaN", "Infinity", "1e400", "1e308", "3600.5" }) // BUG-0103: capped at an hour
            Check(LaunchOptions.Parse(new[] { "--bench", bad }).BenchSeconds == null, $"--bench {bad} should be ignored");
        LaunchOptions missing = LaunchOptions.Parse(new[] { "--bench", "--mute" });
        Check(missing.BenchSeconds == null && missing.Mute, "--bench with no value must not eat --mute");
        Check(LaunchOptions.Parse(new[] { "--vsync", "off" }).Vsync == false, "--vsync off");
        Check(LaunchOptions.Parse(new[] { "--vsync", "on" }).Vsync == true, "--vsync on");
        Check(LaunchOptions.Parse(new[] { "--vsync", "OFF" }).Vsync == null && LaunchOptions.Parse(new[] { "--vsync" }).Vsync == null, "bad --vsync should be ignored");
        Check(LaunchOptions.Parse(Array.Empty<string>()).BenchSeconds == null && LaunchOptions.Parse(Array.Empty<string>()).Vsync == null, "defaults");
        LaunchOptions all = LaunchOptions.Parse(new[] { "--bench", "3", "--mute", "--no-hud", "--units", "50", "--zoom", "60", "--seed", "4", "--screenshot", "C:/x.png", "--vsync", "off" });
        Check(all.BenchSeconds == 3 && all.Mute && all.NoHud && all.UnitsPerPlayer == 50 && all.Zoom == 60 && all.Seed == 4 && all.ScreenshotPath == "C:/x.png" && all.Vsync == false,
            "--bench with every other flag");
    }

    // Criterion 1 on the real binary: one bench line in the documented shape, exit 0, on time; a bad value warns, no ERROR, no line.
    private void ProcessRows()
    {
        (int exit, string[] lines, double seconds) = RunGame("--", "--bench", "2", "--mute");
        int benchLines = 0;
        foreach (string l in lines)
        {
            if (!l.StartsWith(BenchRunner.LinePrefix, StringComparison.Ordinal)) continue;
            benchLines++;
            Check(LineShape.IsMatch(l), $"bench line not in the documented shape: '{l}'");
            System.Text.RegularExpressions.Match m = LineShape.Match(l);
            if (m.Success)
            {
                double secs = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), fps = double.Parse(m.Groups[7].Value, CultureInfo.InvariantCulture);
                int frames = int.Parse(m.Groups[2].Value);
                Check(secs >= 2.0 && frames > 0 && int.Parse(m.Groups[8].Value) > 0, $"bench line numbers implausible: '{l}'");
                // BUG-0102 on the real binary: fps = frames / seconds (seconds is printed to 0.1 s, so allow 2 % + that rounding).
                Check(secs > 0 && Math.Abs(fps - frames / secs) <= 0.02 * frames / secs + frames * 0.05 / (secs * secs), $"fps {fps} but frames / seconds = {frames / secs:F1}: '{l}'");
            }
        }
        GD.Print($"game --bench 2: exit {exit} after {seconds:F1} s, {benchLines} bench line(s)");
        Check(exit == 0, $"--bench 2 exited {exit}");
        Check(benchLines == 1, $"--bench 2 printed {benchLines} bench lines");
        Check(seconds < 2 + 5, $"--bench 2 took {seconds:F1} s (limit 7 s)");
        CheckNoErrors(lines, "--bench 2");

        foreach (string bad in new[] { "0", "-3", "abc" })
        {
            (exit, lines, seconds) = RunGame("--quit-after", "30", "--", "--bench", bad, "--mute");
            bool warned = false, line = false;
            foreach (string l in lines)
            {
                if (l.Contains("WARNING", StringComparison.Ordinal) && l.Contains("--bench", StringComparison.Ordinal)) warned = true;
                if (l.StartsWith(BenchRunner.LinePrefix, StringComparison.Ordinal)) line = true;
            }
            GD.Print($"game --bench {bad}: exit {exit}, warned {warned}, bench line {line}");
            Check(exit == 0 && warned && !line, $"--bench {bad}: exit {exit}, warning {warned}, bench line {line}");
            CheckNoErrors(lines, $"--bench {bad}");
        }
    }

    // Criterion 2 in-process: a 3 s bench through the real controllers changes the selection, enqueues commands and moves the camera.
    private async Task ScriptDrivesRealCode()
    {
        // BUG-0103: on a comma-decimal PC the two info lines printed "10,56 ms"; run this one under de-DE.
        CultureInfo culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        Match match = StartMatch("--bench", "3", "--mute");
        BenchRunner bench = match.Bench!;
        bench.QuitOnFinish = false;
        var sel = match.GetNode<SelectionController>("SelectionController");
        Simulation sim = match.GetNode<SimRunner>("SimRunner").Simulation!;
        int selectedBefore = sel.Selection.Count;
        int maxSelected = 0;
        var watch = Stopwatch.StartNew();
        while (!bench.Finished && watch.Elapsed.TotalSeconds < 20)
        {
            maxSelected = Math.Max(maxSelected, sel.Selection.Count);
            await Frame();
        }
        // The formatters themselves, called here under de-DE (the frame callbacks may not see this async method's culture).
        string start = BenchRunner.StartLineText(10.5, new Vector2(1920, 1061), "Disabled", 59.94, "Windows");
        string worst = BenchRunner.WorstLineText(10.56, 3.25, BenchStep.OrderAcross);
        CultureInfo.CurrentCulture = culture;
        Check(start == "Bench running for 10.5 s (16 steps per 10 s loop), viewport 1920 x 1061, vsync Disabled at 59.9 Hz, display Windows", $"start line under de-DE: '{start}'");
        Check(worst == "Bench worst frame 10.56 ms at 3.25 s, after step OrderAcross", $"worst line under de-DE: '{worst}'");
        var comma = new Regex(@"\d,\d");
        Check(bench.StartLine != null && bench.StartLine.Contains("3 s (") && !comma.IsMatch(bench.StartLine), $"start line under de-DE: '{bench.StartLine}'");
        Check(bench.WorstLine != null && Regex.IsMatch(bench.WorstLine, @"^Bench worst frame \d+\.\d\d ms at \d+\.\d\d s, after step \w+$"), $"worst line under de-DE: '{bench.WorstLine}'");
        Check(bench.Line != null && !comma.IsMatch(bench.Line), $"bench line under de-DE: '{bench.Line}'");
        int issued = 0;
        foreach (CommandKind k in Enum.GetValues<CommandKind>()) issued += sel.IssuedCount(k);
        UnitStore u = sim.World.Units;
        int moved = 0;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == SelectionController.LocalPlayer && u.State[i] != UnitState.Idle) moved++;
        GD.Print($"--bench 3 in-process: {bench.Line}; selected {selectedBefore} -> box {bench.LastBoxSelected} (max {maxSelected}), " +
            $"commands {issued} (move {sel.IssuedCount(CommandKind.Move)}), jumps {bench.Jumps} at {string.Join(" ", bench.JumpFocus)}, busy units {moved}");
        Check(bench.Finished, "the 3 s bench never finished");
        Check(bench.Line != null && LineShape.IsMatch(bench.Line), $"line '{bench.Line}'");
        Check(selectedBefore == 0 && bench.LastBoxSelected >= 90, $"selection {selectedBefore} -> {bench.LastBoxSelected}: the box should take the whole 100-unit army");
        Check(issued > 0 && sel.IssuedCount(CommandKind.Move) >= bench.LastBoxSelected, $"{issued} commands enqueued");
        Check(bench.Jumps == 4, $"{bench.Jumps} minimap jumps in 3 s, expected 4");
        for (int a = 0; a < 4; a++)
            for (int b = a + 1; b < 4; b++)
                Check(System.Numerics.Vector2.Distance(bench.JumpFocus[a], bench.JumpFocus[b]) > 50f, $"jumps {a} and {b} left the camera at {bench.JumpFocus[a]} and {bench.JumpFocus[b]}");
        Check(bench.Ticks >= 50, $"{bench.Ticks} ticks in 3 s");
        // BUG-0102: fps is the frames drawn in the timed span over its length (was a mean of Godot's once-a-second counter, 7-10 % low on short runs).
        System.Text.RegularExpressions.Match line = LineShape.Match(bench.Line ?? "");
        if (line.Success)
        {
            double fps = double.Parse(line.Groups[7].Value, CultureInfo.InvariantCulture);
            double drawn = bench.Stats.Count / bench.Script!.Elapsed, fromAvg = 1000.0 / bench.Stats.Average;
            GD.Print($"fps {fps}: frames / elapsed {drawn:F2}, 1000 / avg {fromAvg:F2}");
            Check(Math.Abs(fps - drawn) <= 0.02 * drawn && Math.Abs(fps - fromAvg) <= 0.02 * fromAvg, $"fps {fps} vs frames / elapsed {drawn:F2} and 1000 / avg {fromAvg:F2}");
        }
        match.QueueFree();
        await Frame();
        await Frame();
    }

    // Criterion 3: the 100-unit default with vsync off, HUD and sound on; needs a real renderer.
    private async Task PinnedFrameTime()
    {
        if (DisplayServer.GetName() == "headless")
        {
            GD.Print("BENCH TEST SKIP: pinned 100-unit frame-time row needs a GPU (headless dummy renderer)");
            return;
        }
        Match match = StartMatch("--bench", PinnedSeconds.ToString(CultureInfo.InvariantCulture), "--vsync", "off");
        BenchRunner bench = match.Bench!;
        bench.QuitOnFinish = false;
        var watch = Stopwatch.StartNew();
        while (!bench.Finished && watch.Elapsed.TotalSeconds < PinnedSeconds + 20) await Frame();
        Vector2I size = DisplayServer.WindowGetSize();
        GD.Print($"pinned row at {size.X} x {size.Y}, vsync off: {bench.Line}");
        Check(bench.Finished, "pinned bench never finished");
        Check(bench.Stats.Average < AvgLimitMs, $"avg frame {bench.Stats.Average:F2} ms >= {AvgLimitMs} ms");
        Check(bench.Stats.Percentile(0.99) < P99LimitMs, $"p99 frame {bench.Stats.Percentile(0.99):F2} ms >= {P99LimitMs} ms");
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Enabled);
        match.QueueFree();
        await Frame();
    }

    private Match StartMatch(params string[] args)
    {
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        match.Start(_data, LaunchOptions.Parse(args));
        return match;
    }

    // Starts the game binary headless on this project with the given extra args; blocks until it exits.
    private static (int exit, string[] lines, double seconds) RunGame(params string[] extra)
    {
        var args = new List<string> { "--headless", "--path", ProjectSettings.GlobalizePath("res://") };
        args.AddRange(extra);
        var output = new Godot.Collections.Array();
        var watch = Stopwatch.StartNew();
        int exit = OS.Execute(OS.GetExecutablePath(), args.ToArray(), output, readStderr: true);
        double seconds = watch.Elapsed.TotalSeconds;
        var lines = new List<string>();
        foreach (Variant chunk in output)
            foreach (string l in chunk.AsString().Split('\n'))
                lines.Add(l.TrimEnd('\r'));
        return (exit, lines.ToArray(), seconds);
    }

    private void CheckNoErrors(string[] lines, string what)
    {
        foreach (string l in lines)
            if (l.Contains("ERROR", StringComparison.Ordinal) || l.Contains("Unhandled", StringComparison.Ordinal))
                _failures.Add($"{what}: log line '{l}'");
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private void Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
    }
}
