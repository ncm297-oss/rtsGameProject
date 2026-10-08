using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Game.Tests;

/// <summary>QA (M2-7): facing blend against an independent shortest-arc oracle (10,000 random triples), the bench under <c>--no-hud</c>, how far the scripted orders move the army, and the game binary's bench edge cases.</summary>
/// <remarks>
/// Headless. Run: <c>&amp; $env:GODOT --headless --path game res://tests/QaM27Test.tscn</c>; prints
/// "QA M2-7 TEST PASS" and exits 0, or prints each failure and exits 1. Scan the log for ERROR too.
/// Measurement rows print "QA M2-7 NOTE" lines and don't fail.
/// </remarks>
public partial class QaM27Test : Node
{
    // Smallest local-army centre displacement a 10 s bench must reach (see OrderReach). Fitted to seed 1, the only seed
    // this scene runs (22.1 m there); other seeds march 19.1-26.2 m and QaH2Test holds them to 15 m (BUG-0104).
    private const float MinCentreShift = 20f;

    private readonly List<string> _failures = new();
    private GameData _data = null!;

    public override async void _Ready()
    {
        try
        {
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            FacingFuzz();
            FacingContinuity();
            await NoHudBench();
            await OrderReach();
            ProcessEdges();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures) GD.Print($"QA M2-7 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M2-7 TEST PASS");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    // Shortest signed turn from a to b in double, in [-π, π).
    private static double ShortTurn(double a, double b)
    {
        double d = b - a;
        d -= Math.Floor((d + Math.PI) / (2 * Math.PI)) * 2 * Math.PI;
        return d;
    }

    private static double AngleGap(double a, double b) => Math.Abs(ShortTurn(a, b));

    private void FacingFuzz()
    {
        var rng = new Random(2707);
        int bad = 0, halfTurns = 0;
        double worst = 0;
        for (int i = 0; i < 10_000; i++)
        {
            float prev = (float)((rng.NextDouble() * 2 - 1) * Math.PI);
            float cur = (float)((rng.NextDouble() * 2 - 1) * Math.PI);
            switch (rng.Next(12))
            {
                case 0: cur = prev; break;
                case 1: prev = Mathf.Pi; break;
                case 2: cur = -Mathf.Pi; break;
                case 3: cur = prev + Mathf.Pi * (rng.Next(2) == 0 ? 1 : -1) * (1f - 1e-6f * rng.Next(3)); break; // ~half turns
                case 4: cur = -prev; break;
            }
            float alpha = rng.Next(10) switch
            {
                0 => 0f,
                1 => 1f,
                2 => (float)(rng.NextDouble() * 4 - 2),
                3 => float.NaN,
                _ => (float)rng.NextDouble(),
            };
            double a = float.IsNaN(alpha) ? 1 : Math.Clamp(alpha, 0, 1);
            double d = ShortTurn(prev, cur);
            double want = prev + d * a;
            float got = UnitViews.BlendFacing(prev, cur, alpha);
            double err = AngleGap(got, want);
            bool nearHalf = Math.Abs(Math.Abs(d) - Math.PI) < 1e-4;
            if (nearHalf)
            {
                // Either way round is a shortest arc; the float may round to the other side of ±π.
                halfTurns++;
                double other = prev + (d > 0 ? d - 2 * Math.PI : d + 2 * Math.PI) * a;
                err = Math.Min(err, AngleGap(got, other));
            }
            // Short-arc property, independent of the oracle: never turned further than |d| * alpha from prev.
            bool arcOk = AngleGap(prev, got) <= Math.Abs(d) * a + 1e-4 || nearHalf;
            bool finite = float.IsFinite(got) && Math.Abs(got) <= 2 * Math.PI + 1e-3;
            if (err > 1e-4 || !arcOk || !finite)
            {
                if (bad++ < 10) _failures.Add($"BlendFacing({prev:R}, {cur:R}, {alpha:R}) = {got:R}, oracle {want:R} (err {err:E2}, arc ok {arcOk})");
            }
            worst = Math.Max(worst, err);
        }
        GD.Print($"facing fuzz: 10000 triples, {halfTurns} near half turns, worst error {worst:E2} rad, {bad} bad");
    }

    // Sweeping alpha 0 -> 1 turns in small steady steps (no flip through the long way at any alpha).
    private void FacingContinuity()
    {
        var rng = new Random(77);
        for (int i = 0; i < 500; i++)
        {
            float prev = (float)((rng.NextDouble() * 2 - 1) * Math.PI), cur = (float)((rng.NextDouble() * 2 - 1) * Math.PI);
            double step = AngleGap(prev, cur) / 100 + 1e-4;
            float last = UnitViews.BlendFacing(prev, cur, 0f);
            for (int k = 1; k <= 100; k++)
            {
                float v = UnitViews.BlendFacing(prev, cur, k / 100f);
                if (AngleGap(last, v) > step)
                {
                    _failures.Add($"continuity {prev} -> {cur}: alpha {k / 100f} jumped {AngleGap(last, v):F4} rad");
                    break;
                }
                last = v;
            }
            if (AngleGap(last, cur) > 1e-4) _failures.Add($"continuity {prev} -> {cur}: alpha 1 ends at {last}");
        }
    }

    // Without a HUD the jumps go to the camera directly: still four, at four different points.
    private async Task NoHudBench()
    {
        Match match = StartMatch("--bench", "3", "--mute", "--no-hud", "--no-combat"); // combat off: a world without fights, as in M2 (M4-V1, BUG-0147)
        BenchRunner bench = match.Bench!;
        bench.QuitOnFinish = false;
        var watch = Stopwatch.StartNew();
        while (!bench.Finished && watch.Elapsed.TotalSeconds < 20) await Frame();
        GD.Print($"--no-hud --bench 3: {bench.Line}; jumps {bench.Jumps} at {string.Join(" ", bench.JumpFocus)}; box {bench.LastBoxSelected}");
        Check(bench.Finished && bench.Line != null && BenchTest.LineShape.IsMatch(bench.Line), $"--no-hud bench line '{bench.Line}'");
        Check(bench.Jumps == 4, $"--no-hud: {bench.Jumps} jumps");
        for (int a = 0; a < 4; a++)
            for (int b = a + 1; b < 4; b++)
                Check(System.Numerics.Vector2.Distance(bench.JumpFocus[a], bench.JumpFocus[b]) > 50f, $"--no-hud jumps {a}/{b} at {bench.JumpFocus[a]} / {bench.JumpFocus[b]}");
        Check(bench.LastBoxSelected >= 90, $"--no-hud box took {bench.LastBoxSelected}");
        match.QueueFree();
        await Frame();
        await Frame();
    }

    // How far a 10 s bench sends and moves the local army (the brief: "order across the map"; M2-H2 asserts it).
    private async Task OrderReach()
    {
        Match match = StartMatch("--bench", "10", "--mute", "--speed", "1", "--no-combat"); // combat off: a world without fights, as in M2 (M4-V1, BUG-0147)
        BenchRunner bench = match.Bench!;
        bench.QuitOnFinish = false;
        Simulation sim = match.GetNode<SimRunner>("SimRunner").Simulation!;
        var watch = Stopwatch.StartNew();
        while (!bench.Running && watch.Elapsed.TotalSeconds < 20) await Frame();
        UnitStore u = sim.World.Units;
        var start = new System.Numerics.Vector2[u.Capacity];
        System.Numerics.Vector2 c0 = Centre(u, start, record: true);
        float maxCentreShift = 0, maxUnit = 0;
        while (!bench.Finished && watch.Elapsed.TotalSeconds < 40)
        {
            await Frame();
            maxCentreShift = Math.Max(maxCentreShift, System.Numerics.Vector2.Distance(c0, Centre(u, start, record: false)));
            for (int i = 0; i < u.Capacity; i++)
                if (u.Alive[i] && u.Owner[i] == SelectionController.LocalPlayer)
                    maxUnit = Math.Max(maxUnit, System.Numerics.Vector2.Distance(start[i], u.Position[i]));
        }
        float mapW = sim.World.NavGrid.Width * Rts.Sim.Map.MapConstants.CellSize;
        float reach = System.Numerics.Vector2.Distance(bench.AcrossFrom, bench.AcrossTarget);
        GD.Print($"QA M2-7 NOTE: 10 s bench sent the army {reach:F1} m (from {bench.AcrossFrom} to {bench.AcrossTarget}) and moved the local army centre at most {maxCentreShift:F1} m and any unit at most {maxUnit:F1} m on a {mapW:F0} m map");
        Check(bench.Finished, "10 s bench never finished");
        // BUG-0101: "order across the map" must go across (was the enemy block ~15 m away; the centre moved 17.4 m).
        // The march crosses the idle enemy block, which halves its pace after ~5 s: an uninterrupted 10 s sim march
        // moves seed 1's centre 24.2 m (29.3 m with no enemy), so the bench's 8.75 s march is held to 20 m.
        Check(bench.AcrossOrders >= 1, "the 10 s bench never ordered the army across");
        Check(reach >= 100f, $"order across targets {bench.AcrossTarget}, only {reach:F1} m from the army at {bench.AcrossFrom}");
        Check(maxCentreShift >= MinCentreShift, $"the army centre moved at most {maxCentreShift:F1} m in 10 s (want >= {MinCentreShift} m)");
        match.QueueFree();
        await Frame();
        await Frame();
    }

    private static System.Numerics.Vector2 Centre(UnitStore u, System.Numerics.Vector2[] start, bool record)
    {
        var sum = System.Numerics.Vector2.Zero;
        int n = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.Owner[i] != SelectionController.LocalPlayer) continue;
            if (record) start[i] = u.Position[i];
            sum += u.Position[i];
            n++;
        }
        return n > 0 ? sum / n : sum;
    }

    // The binary's edge cases: every run exits 0 with one bench line and no ERROR.
    private void ProcessEdges()
    {
        string shot = ProjectSettings.GlobalizePath("user://qa-m27-headless.png");
        (string what, string[] args, string mustPrecede)[] rows =
        {
            ("--screenshot then --bench (headless)", new[] { "--", "--bench", "1.5", "--screenshot", shot, "--mute" }, "Screenshot unavailable"),
            ("--units 0", new[] { "--", "--bench", "1.5", "--units", "0", "--mute" }, ""),
            ("--bench 0.01", new[] { "--", "--bench", "0.01", "--mute" }, ""),
            ("--vsync with no value", new[] { "--", "--vsync", "--bench", "1", "--mute" }, "Ignoring --vsync"),
            ("--speed 8", new[] { "--", "--bench", "1.5", "--speed", "8", "--mute" }, ""),
        };
        foreach ((string what, string[] args, string mustPrecede) in rows)
        {
            (int exit, string[] lines, double seconds) = RunGame(args);
            int benchLines = 0, firstBench = -1, precede = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith(BenchRunner.LinePrefix, StringComparison.Ordinal)) { benchLines++; if (firstBench < 0) firstBench = i; }
                if (mustPrecede.Length > 0 && precede < 0 && lines[i].Contains(mustPrecede, StringComparison.Ordinal)) precede = i;
                if (lines[i].Contains("ERROR", StringComparison.Ordinal) || lines[i].Contains("Unhandled", StringComparison.Ordinal))
                    _failures.Add($"{what}: log line '{lines[i]}'");
            }
            GD.Print($"{what}: exit {exit}, {benchLines} bench line(s), {seconds:F1} s");
            Check(exit == 0, $"{what}: exit {exit}");
            Check(benchLines == 1, $"{what}: {benchLines} bench lines");
            if (firstBench >= 0) Check(BenchTest.LineShape.IsMatch(lines[firstBench]), $"{what}: line '{lines[firstBench]}'");
            if (mustPrecede.Length > 0) Check(precede >= 0 && precede < firstBench, $"{what}: '{mustPrecede}' at line {precede}, bench line at {firstBench}");
            Check(seconds < 15, $"{what}: took {seconds:F1} s");
        }
    }

    private Match StartMatch(params string[] args)
    {
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        match.Start(_data, LaunchOptions.Parse(args));
        return match;
    }

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

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private void Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
    }
}
