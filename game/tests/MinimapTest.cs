using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>M2-4: the real Match scene's <see cref="Minimap"/> with 2,000 units: dot colours, refresh cost, left-click/drag camera jumps and right-click orders pushed through the viewport, clicks outside the rect, and the camera outline at the zoom limits and map corners.</summary>
/// <remarks>
/// Headless. Run: <c>&amp; $env:GODOT --headless --path game res://tests/MinimapTest.tscn</c>; prints
/// "MINIMAP TEST PASS" and exits 0, or prints each failure and exits 1.
/// </remarks>
public partial class MinimapTest : Node
{
    private readonly List<string> _failures = new();
    private Minimap _mini = null!;
    private RtsCamera _camera = null!;
    private SelectionController _sel = null!;
    private Simulation _sim = null!;

    public override async void _Ready()
    {
        try
        {
            await Run();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        Engine.TimeScale = 1.0;
        foreach (string f in _failures) GD.Print($"MINIMAP TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("MINIMAP TEST PASS");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    private async Task Run()
    {
        // Headless windows are 64 x 64; use the project's default size so the minimap has room.
        GetTree().Root.Size = new Vector2I(1152, 648);
        await Frame();
        DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
        if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
        GameData data = loaded.Data!;
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        match.Start(data, LaunchOptions.Parse(new[] { "--units", "1000" }));
        _mini = match.GetNode<Minimap>("Hud/Minimap");
        _camera = match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _sel = match.GetNode<SelectionController>("SelectionController");
        _sim = match.GetNode<SimRunner>("SimRunner").Simulation!;
        UnitStore u = _sim.World.Units;
        Heightmap map = _sim.World.Heightmap;
        await Frame();
        Rect2 rect = _mini.GetGlobalRect();
        GD.Print($"minimap rect {rect}, raster {_mini.Raster!.Width} x {_mini.Raster.Height}");
        Check(rect.Size.Y == 220f && rect.Position.X < 20f && rect.End.Y > 620f, $"minimap not 220 px tall at bottom-left: {rect}");

        // Dots: after the first refresh that sees the spawned units, every unit's pixel has its faction colour.
        int refreshes = 0;
        while (_sim.TickNumber < 3 || _mini.Refreshes == refreshes) { if (_sim.TickNumber < 3) refreshes = _mini.Refreshes; await Frame(); }
        Check(u.Count == 2000, $"{u.Count} units spawned, expected 2000");
        int wrong = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            _mini.Raster.TryPixelOf(u.Position[i], out int x, out int y);
            Color want = UnitViews.ColorFromRgb(data.Factions[u.Owner[i]].PrimaryColor);
            Color got = _mini.DotsImage.GetPixel(x, y);
            if (Mathf.Abs(got.R - want.R) > 0.01f || Mathf.Abs(got.G - want.G) > 0.01f || Mathf.Abs(got.B - want.B) > 0.01f || got.A < 0.99f)
            {
                if (wrong++ < 3) _failures.Add($"slot {i} pixel ({x}, {y}) is {got}, want {want}");
            }
        }
        Check(wrong == 0, $"{wrong} units with the wrong dot colour");

        // Refresh cost at 2,000 units.
        double total = 0, worst = 0;
        for (int k = 0; k < 50; k++)
        {
            _mini.Refresh(_sim);
            total += _mini.LastRefreshMs;
            worst = Math.Max(worst, _mini.LastRefreshMs);
        }
        GD.Print($"minimap refresh at {u.Count} units: avg {total / 50:F3} ms, worst {worst:F3} ms");
        Check(total / 50 <= 0.5, $"refresh avg {total / 50:F3} ms > 0.5 ms");

        // Select 40 own units; a left click on the minimap must not reach the selection controller.
        _sel.Selection.Clear();
        for (int i = 0; i < u.Capacity && _sel.Selection.Count < 40; i++)
            if (u.Alive[i] && u.Owner[i] == SelectionController.LocalPlayer) _sel.Selection.Add(new EntityHandle(i, u.Generation[i]));

        // Left click jumps the camera to the matching map point.
        MinimapTransform fit = _mini.Fit;
        var local = new Vector2(60, 150);
        Check(fit.TryToMap(new(local.X, local.Y), out System.Numerics.Vector2 want1), "test pixel off the map");
        Push(Button(MouseButton.Left, true, rect.Position + local));
        Push(Button(MouseButton.Left, false, rect.Position + local));
        ExpectFocus("left click", want1);
        Check(_sel.Selection.Count == 40, $"minimap left click changed the selection to {_sel.Selection.Count}");

        // Drag with the button held follows the cursor.
        Vector2 from = new(30, 30), to = new(180, 120);
        fit.TryToMap(new(to.X, to.Y), out System.Numerics.Vector2 want2);
        Push(Button(MouseButton.Left, true, rect.Position + from));
        Push(new InputEventMouseMotion { Position = rect.Position + (from + to) / 2, ButtonMask = MouseButtonMask.Left });
        Push(new InputEventMouseMotion { Position = rect.Position + to, ButtonMask = MouseButtonMask.Left });
        Push(Button(MouseButton.Left, false, rect.Position + to));
        ExpectFocus("drag", want2);

        // A click just outside the minimap rect is not the minimap's: the focus stays and nothing is ordered.
        System.Numerics.Vector2 focusBefore = _camera.Focus;
        int pending = _sim.PendingCommandCount;
        var selected = _sel.Selection.Items.ToArray();
        _sel.Selection.Clear();
        var outside = new Vector2(rect.End.X + 4, rect.Position.Y + 100);
        Push(Button(MouseButton.Left, true, outside));
        Push(Button(MouseButton.Left, false, outside));
        Push(Button(MouseButton.Right, true, outside));
        Push(Button(MouseButton.Right, false, outside));
        Check(_camera.Focus == focusBefore, $"a click outside the minimap moved the camera {focusBefore} -> {_camera.Focus}");
        Check(_sim.PendingCommandCount == pending, "a click outside the minimap with nothing selected enqueued commands");
        // Right click on the minimap with an empty selection: nothing.
        Push(Button(MouseButton.Right, true, rect.Position + local));
        Push(Button(MouseButton.Right, false, rect.Position + local));
        Check(_sim.PendingCommandCount == pending, "minimap right click with nothing selected enqueued commands");

        // Right click with 40 selected: 40 Moves to the matching map point, all Moving 3 s later.
        _sel.Selection.Clear();
        foreach (EntityHandle h in selected) _sel.Selection.Add(h);
        System.Numerics.Vector2 target = fit.MapSize * new System.Numerics.Vector2(0.5f, 0.2f);
        System.Numerics.Vector2 tpx = fit.ToPixel(target);
        Push(Button(MouseButton.Right, true, rect.Position + new Vector2(tpx.X, tpx.Y)));
        Push(Button(MouseButton.Right, false, rect.Position + new Vector2(tpx.X, tpx.Y)));
        Check(_sim.PendingCommandCount - pending == 40, $"{_sim.PendingCommandCount - pending} commands for 40 selected units");
        int start = _sim.TickNumber;
        Engine.TimeScale = 4.0;
        while (_sim.TickNumber < start + 61) await Frame();
        Engine.TimeScale = 1.0;
        int moving = 0;
        foreach (EntityHandle h in selected)
        {
            if (u.State[h.Index] == UnitState.Moving) moving++;
            if (System.Numerics.Vector2.Distance(u.Goal[h.Index], target) > MapConstants.CellSize * 2)
                _failures.Add($"slot {h.Index} goal {u.Goal[h.Index]} is not the minimap point {target}");
        }
        GD.Print($"minimap right-click: 40 orders to ({target.X:F1}, {target.Y:F1}); after 3 s {moving} Moving");
        Check(moving == 40, $"{moving} of 40 Moving 3 s after the minimap order");

        // The camera outline stays inside the map rect at both zoom limits, centred and at the map corners.
        (System.Numerics.Vector2 mapPos, System.Numerics.Vector2 mapSize) = fit.MapRect;
        var mapRect = new Rect2(mapPos.X - 0.01f, mapPos.Y - 0.01f, mapSize.X + 0.02f, mapSize.Y + 0.02f);
        float w = map.Width * MapConstants.CellSize, hgt = map.Height * MapConstants.CellSize;
        foreach (float zoom in new[] { CameraLimits.MinZoom, CameraLimits.MaxZoom })
        {
            foreach (System.Numerics.Vector2 f in new System.Numerics.Vector2[] { new(w / 2, hgt / 2), new(0, 0), new(w, hgt), new(0, hgt) })
            {
                _camera.SetZoom(zoom);
                _camera.SetFocus(f.X, f.Y);
                _mini.UpdateOutline();
                Vector2[] o = _mini.Outline.ToArray();
                for (int i = 0; i < 4; i++)
                    Check(mapRect.HasPoint(o[i]), $"zoom {zoom} focus {f}: outline corner {i} {o[i]} outside {mapRect}");
                if (f.X == w / 2)
                {
                    // Seen from behind on +Z: the far (top) edge is above and wider than the near (bottom) edge.
                    float top = o[1].X - o[0].X, bottom = o[2].X - o[3].X;
                    System.Numerics.Vector2 fp = fit.ToPixel(f);
                    Check(o[0].Y < fp.Y && o[3].Y > fp.Y && top > bottom && bottom > 0f,
                        $"zoom {zoom}: outline is not a trapezoid around the focus: {o[0]} {o[1]} {o[2]} {o[3]} focus {fp}");
                    GD.Print($"outline at zoom {zoom}: top {top:F1} px, bottom {bottom:F1} px, height {o[3].Y - o[0].Y:F1} px");
                }
            }
        }
    }

    private void ExpectFocus(string what, System.Numerics.Vector2 want)
    {
        System.Numerics.Vector2 clamped = CameraLimits.ClampFocus(want, _sim.World.Heightmap.Width, _sim.World.Heightmap.Height);
        float err = System.Numerics.Vector2.Distance(clamped, _camera.Focus);
        GD.Print($"{what}: focus {_camera.Focus}, expected {clamped} (error {err:F3} m)");
        Check(err <= MapConstants.CellSize, $"{what}: focus {_camera.Focus}, expected {clamped}");
    }

    private void Push(InputEvent e) => GetViewport().PushInput(e);

    private static InputEventMouseButton Button(MouseButton b, bool pressed, Vector2 at) =>
        new() { ButtonIndex = b, Pressed = pressed, Position = at, GlobalPosition = at,
            ButtonMask = pressed ? (b == MouseButton.Left ? MouseButtonMask.Left : MouseButtonMask.Right) : 0 };

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private void Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
    }
}
