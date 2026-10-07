using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>QA (M2-3b): <c>--forests</c> / <c>--mines</c> parsing extremes; the live Match at the 64 / 64 caps with 2,000 units: drawn counts, the whole minimap resource layer against the store, minimap refresh cost, steady PropsView frames at 0 bytes; windowed only, frame time at zoom 60 with props on and off.</summary>
/// <remarks>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM23bTest.tscn</c>. Windowed (adds the frame-time
/// rows, vsync off): <c>&amp; $env:GODOT --path game res://tests/QaM23bTest.tscn</c>. Prints "QA M2-3B TEST PASS" and
/// exits 0, or prints each failure and exits 1.
/// </remarks>
public partial class QaM23bTest : Node
{
    private readonly List<string> _failures = new();
    private GameData _data = null!;

    public override async void _Ready()
    {
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            await Frame();
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            ParsingExtremes();
            await CapsMatch();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures) GD.Print($"QA M2-3B TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M2-3B TEST PASS");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    private void ParsingExtremes()
    {
        (string[] Args, int Forests, int Mines)[] rows =
        {
            (new[] { "--forests", "64", "--mines", "0" }, 64, 0),
            (new[] { "--forests", "065" }, 12, 8),                // 65 with a leading zero
            (new[] { "--forests", "064" }, 64, 8),
            (new[] { "--forests", "2147483648" }, 12, 8),         // int overflow
            (new[] { "--forests", "-2147483648" }, 12, 8),
            (new[] { "--mines", "99999999999999999999" }, 12, 8),
            (new[] { "--forests", "1e1", "--mines", "0x10" }, 12, 8),
            (new[] { "--forests", "٣" }, 12, 8),              // Arabic-Indic digit three
            (new[] { "--forests", "" }, 12, 8),
            (new[] { "--forests", "-0" }, 0, 8),
            (new[] { "--forests", "3", "--forests", "5" }, 5, 8), // last wins
            (new[] { "--forests", "70", "--forests", "4" }, 4, 8),
            (new[] { "--Forests", "3", "--MINES", "2" }, 12, 8),  // flags are case-sensitive, ignored
            (new[] { "--mines", "--forests", "--units", "5" }, 12, 8),
            (new[] { "--forests" }, 12, 8),
        };
        foreach ((string[] args, int f, int m) in rows)
        {
            LaunchOptions o = LaunchOptions.Parse(args);
            Check(o.Forests == f && o.Mines == m, $"[{string.Join(' ', args)}]: forests {o.Forests}, mines {o.Mines}; want {f} / {m}");
        }
        // The value after a refused --forests must not be eaten as another flag's value.
        LaunchOptions u = LaunchOptions.Parse(new[] { "--forests", "-5", "--units", "7" });
        Check(u.Forests == 12 && u.UnitsPerPlayer == 7, $"--forests -5 --units 7: forests {u.Forests}, units {u.UnitsPerPlayer}");
    }

    private async Task CapsMatch()
    {
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        match.Start(_data, LaunchOptions.Parse(new[] { "--seed", "7", "--units", "1000", "--forests", "64", "--mines", "64", "--no-bases" })); // armies only, as in M2 (M3-V1)
        var runner = match.GetNode<SimRunner>("SimRunner");
        Simulation sim = runner.Simulation!;
        World w = sim.World;
        ResourceStore r = w.Resources;
        var props = match.GetNode<PropsView>("World3D/PropsView");
        var mini = match.GetNode<Minimap>("Hud/Minimap");
        await Frame();
        await Frame();
        ResourcePlacement placed = w.ResourcePlacement;
        GD.Print($"caps match: placed forests {placed.Forests} trees {placed.Trees} mines {placed.Mines}, store {r.Count}");
        Check(w.Config.Map.Forests == 64 && w.Config.Map.GoldMines == 64, "caps not passed to the map");
        int shown = 0;
        for (int t = 0; t < _data.Resources.Length; t++)
        {
            int alive = 0;
            for (int i = 0; i < r.Capacity; i++) if (r.Alive[i] && r.TypeId[i] == t) alive++;
            Check(props.ShownCount(t) == alive, $"type {t}: shown {props.ShownCount(t)}, alive {alive}");
            shown += props.ShownCount(t);
        }
        Check(shown == r.Count && props.Uploads == 1, $"shown {shown} of {r.Count}, uploads {props.Uploads}");

        // Whole minimap resource layer against the store.
        int start = mini.Refreshes;
        while (mini.Refreshes == start) await Frame();
        int width = mini.Raster!.Width, bad = 0;
        var want = new Color?[width * mini.Raster.Height];
        for (int s = 0; s < r.Capacity; s++)
        {
            if (!r.Alive[s]) continue;
            ResourceDef def = _data.Resources[r.TypeId[s]];
            Color c = UnitViews.ColorFromRgb(def.Resource == ResourceKind.Gold ? 0xE6B422u : 0x1E5A1Eu);
            int x0 = r.Cell[s] % width, y0 = r.Cell[s] / width;
            for (int y = y0; y < y0 + def.FootprintHeight; y++)
                for (int x = x0; x < x0 + def.FootprintWidth; x++) want[y * width + x] = c;
        }
        for (int p = 0; p < want.Length; p++)
        {
            Color got = mini.ResourcesImage.GetPixel(p % width, p / width);
            bool ok = want[p] is Color c ? Mathf.Abs(got.R - c.R) < 0.01f && Mathf.Abs(got.G - c.G) < 0.01f && Mathf.Abs(got.B - c.B) < 0.01f && got.A > 0.99f : got.A < 0.01f;
            if (!ok && bad++ < 5) Check(false, $"minimap resource pixel ({p % width}, {p / width}) is {got}, want {want[p]}");
        }
        Check(bad == 0, $"{bad} minimap resource pixels wrong");

        // Minimap refresh cost with 2,000 dots and nothing changed (live, 20 refreshes).
        double worst = 0, sum = 0;
        for (int k = 0; k < 20; k++)
        {
            start = mini.Refreshes;
            while (mini.Refreshes == start) await Frame();
            sum += mini.LastRefreshMs;
            worst = Math.Max(worst, mini.LastRefreshMs);
        }
        GD.Print($"caps match minimap refresh (2,000 units, {r.Count} nodes, upload included): avg {sum / 20:F3} ms, worst {worst:F3} ms, resource draws {mini.Raster.ResourceDraws}");
        Check(mini.Raster.ResourceDraws == 1, $"resource layer redrawn {mini.Raster.ResourceDraws} times with no change");

        // Steady PropsView frames: no upload, 0 bytes.
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) props.Sync(w);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(bytes == 0 && props.Uploads == 1, $"steady Sync: {bytes} bytes, uploads {props.Uploads}");

        if (DisplayServer.GetName() != "headless")
        {
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            Engine.MaxFps = 0;
            var camera = match.GetNode<RtsCamera>("RtsCamera");
            camera.EdgePanEnabled = false;
            camera.SetZoom(CameraLimits.MaxZoom);
            foreach ((float fx, float fy) in new[] { (128f, 128f), (60f, 60f), (200f, 190f) })
            {
                camera.SetFocus(fx, fy);
                (double onAvg, double onP99) = await FrameTimes(300);
                props.Visible = false;
                (double offAvg, double offP99) = await FrameTimes(300);
                props.Visible = true;
                GD.Print($"frame time zoom 60 focus ({fx}, {fy}), 2,000 units, {r.Count} props: props on avg {onAvg:F2} ms p99 {onP99:F2} ms ({1000 / onAvg:F0} fps); props off avg {offAvg:F2} ms p99 {offP99:F2} ms");
                Check(onAvg <= 1000.0 / 60, $"props on: avg frame {onAvg:F2} ms > 16.7 ms at ({fx}, {fy})");
            }
        }
        RemoveChild(match);
        match.QueueFree();
        await Frame();
    }

    private async Task<(double Avg, double P99)> FrameTimes(int frames)
    {
        for (int i = 0; i < 30; i++) await Frame(); // settle
        var ms = new double[frames];
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < frames; i++)
        {
            await Frame();
            ms[i] = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
        }
        Array.Sort(ms);
        double sum = 0;
        foreach (double m in ms) sum += m;
        return (sum / frames, ms[(int)(frames * 0.99)]);
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private void Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
    }
}
