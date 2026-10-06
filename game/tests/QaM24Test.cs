using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>QA (M2-4): minimap input routing against the rest of the match (drags that cross its border, border pixels, overflow), refresh keyed to ticks at 8x speed, dots across death and respawn as the enemy, the outline with a clamped focus.</summary>
/// <remarks>
/// Headless. Run: <c>&amp; $env:GODOT --headless --path game res://tests/QaM24Test.tscn</c>; prints
/// "QA M2-4 TEST PASS" and exits 0, or prints each failure and exits 1.
/// </remarks>
public partial class QaM24Test : Node
{
    private readonly List<string> _failures = new();
    private Minimap _mini = null!;
    private RtsCamera _camera = null!;
    private SelectionController _sel = null!;
    private SimRunner _runner = null!;
    private Simulation _sim = null!;
    private ColorRect _box = null!;
    private GameData _data = null!;
    private Rect2 _rect;

    public override async void _Ready()
    {
        try
        {
            await Setup();
            await DragFrom3DReleasedOverMinimap();
            await MiddleDragReleasedOverMinimap();
            await MinimapDragReleasedOn3D();
            BorderPixelsAndSelection();
            Overflow();
            await RefreshKeyedToTicks();
            await RespawnAsEnemy();
            OutlineWithClampedFocus();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        Engine.TimeScale = 1.0;
        foreach (string f in _failures) GD.Print($"QA M2-4 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M2-4 TEST PASS");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    private async Task Setup()
    {
        GetTree().Root.Size = new Vector2I(1152, 648);
        await Frame();
        DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
        if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
        _data = loaded.Data!;
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        match.Start(_data, LaunchOptions.Parse(new[] { "--units", "1000" }));
        _mini = match.GetNode<Minimap>("Hud/Minimap");
        _camera = match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _sel = match.GetNode<SelectionController>("SelectionController");
        _box = match.GetNode<ColorRect>("SelectionController/BoxLayer/Box");
        _runner = match.GetNode<SimRunner>("SimRunner");
        _sim = _runner.Simulation!;
        while (_sim.TickNumber < 3) await Frame();
        _rect = _mini.GetGlobalRect();
    }

    private void SelectOwn(int n)
    {
        UnitStore u = _sim.World.Units;
        _sel.Selection.Clear();
        for (int i = 0; i < u.Capacity && _sel.Selection.Count < n; i++)
            if (u.Alive[i] && u.Owner[i] == SelectionController.LocalPlayer) _sel.Selection.Add(new EntityHandle(i, u.Generation[i]));
    }

    // Developer's open risk: a box started on the 3D view and released over the minimap.
    private async Task DragFrom3DReleasedOverMinimap()
    {
        SelectOwn(5);
        Vector2 start = new(600, 150), over = _rect.Position + new Vector2(110, 110);
        Push(Button(MouseButton.Left, true, start));
        Push(Motion(new Vector2(500, 300), MouseButtonMask.Left));
        bool boxShown = _box.Visible;
        Push(Motion(over, MouseButtonMask.Left));
        Push(Button(MouseButton.Left, false, over));
        await Frame();
        Check(boxShown, "box did not appear during the 3D drag (test setup)");
        Check(!_box.Visible, "box drag released over the minimap: the box stayed visible");
        // Afterwards a plain motion on the 3D view must not drag a box.
        Push(Motion(new Vector2(800, 200), 0));
        Check(!_box.Visible, "box reappeared on a buttonless motion after a release over the minimap");
        GD.Print($"3D drag released over minimap: box visible {_box.Visible}, selection {_sel.Selection.Count}");
    }

    private async Task MiddleDragReleasedOverMinimap()
    {
        Vector2 start = new(600, 300), over = _rect.Position + new Vector2(110, 110);
        Push(Button(MouseButton.Middle, true, start));
        Push(Motion(new Vector2(560, 320), MouseButtonMask.Middle, new Vector2(-40, 20)));
        Push(Motion(over, MouseButtonMask.Middle, over - new Vector2(560, 320)));
        Push(Button(MouseButton.Middle, false, over));
        await Frame();
        System.Numerics.Vector2 before = _camera.Focus;
        Push(Motion(new Vector2(700, 300), 0, new Vector2(300, 0)));
        Check(_camera.Focus == before, $"middle-drag released over the minimap: the camera keeps dragging ({before} -> {_camera.Focus})");
    }

    private async Task MinimapDragReleasedOn3D()
    {
        SelectOwn(12);
        Push(Button(MouseButton.Left, true, _rect.Position + new Vector2(50, 50)));
        Push(Motion(new Vector2(700, 200), MouseButtonMask.Left));
        Push(Button(MouseButton.Left, false, new Vector2(700, 200)));
        await Frame();
        Check(_sel.Selection.Count == 12, $"minimap drag released on the 3D view changed the selection to {_sel.Selection.Count}");
        Check(!_box.Visible, "minimap drag released on the 3D view showed a box");
        System.Numerics.Vector2 before = _camera.Focus;
        Push(Motion(_rect.Position + new Vector2(150, 150), 0));
        Check(_camera.Focus == before, "after release, hovering the minimap still moves the camera");
    }

    private void BorderPixelsAndSelection()
    {
        SelectOwn(40);
        MinimapTransform fit = _mini.Fit;
        var heightmap = _sim.World.Heightmap;
        float w = heightmap.Width * Rts.Sim.Map.MapConstants.CellSize, h = heightmap.Height * Rts.Sim.Map.MapConstants.CellSize;
        (Vector2 local, System.Numerics.Vector2 want)[] rows =
        {
            (new Vector2(0f, 0f), new(0, 0)),
            (new Vector2(219.99f, 219.99f), new(w, h)),
            (new Vector2(219.99f, 0f), new(w, 0)),
            (new Vector2(0f, 219.99f), new(0, h)),
        };
        foreach ((Vector2 local, System.Numerics.Vector2 want) in rows)
        {
            Push(Button(MouseButton.Left, true, _rect.Position + local, doubleClick: false));
            Push(Button(MouseButton.Left, false, _rect.Position + local));
            System.Numerics.Vector2 clamped = CameraLimits.ClampFocus(want, heightmap.Width, heightmap.Height);
            Check(System.Numerics.Vector2.Distance(_camera.Focus, clamped) <= 1f, $"border pixel {local}: focus {_camera.Focus}, want {clamped}");
            // Double click and shift-click on the minimap: still never touch the selection.
            Push(Button(MouseButton.Left, true, _rect.Position + local, doubleClick: true));
            Push(Button(MouseButton.Left, false, _rect.Position + local));
            Input.ActionPress("select_add");
            Push(Button(MouseButton.Left, true, _rect.Position + local));
            Push(Button(MouseButton.Left, false, _rect.Position + local));
            Input.ActionRelease("select_add");
        }
        Check(_sel.Selection.Count == 40, $"minimap clicks changed the selection to {_sel.Selection.Count}");
        // Wheel over the minimap: no zoom (documented), no selection change.
        float zoomBefore = _camera.Position.Y;
        Push(Button(MouseButton.WheelUp, true, _rect.Position + new Vector2(100, 100)));
        Push(Button(MouseButton.WheelUp, false, _rect.Position + new Vector2(100, 100)));
        Check(_camera.Position.Y == zoomBefore, "wheel over the minimap zoomed");
        // A left click a hair above the minimap is a 3D click: the camera stays.
        System.Numerics.Vector2 focus = _camera.Focus;
        Push(Button(MouseButton.Left, true, new Vector2(_rect.Position.X + 100, _rect.Position.Y - 0.5f)));
        Push(Button(MouseButton.Left, false, new Vector2(_rect.Position.X + 100, _rect.Position.Y - 0.5f)));
        Check(_camera.Focus == focus, "a click just above the minimap moved the camera");
        GD.Print($"border pixels: last focus {_camera.Focus}, selection kept {_sel.Selection.Count}");
    }

    // Right-click with the whole own army selected against a nearly full queue: same rule as the 3D right-click (M2-2).
    private void Overflow()
    {
        SelectOwn(1000);
        int n = _sel.Selection.Count;
        Check(n == 1000, $"only {n} own units to select");
        int cap = _sim.World.Config.CommandCapacity;
        int fill = cap - n + 1 - _sim.PendingCommandCount;
        for (int i = 0; i < fill; i++) _sim.Enqueue(Command.Noop(0));
        int pending = _sim.PendingCommandCount, dropped = _sel.DroppedOrders;
        Vector2 at = _rect.Position + new Vector2(110, 60);
        Push(Button(MouseButton.Right, true, at));
        Push(Button(MouseButton.Right, false, at));
        Check(_sim.PendingCommandCount == pending, $"overflowing minimap order enqueued {_sim.PendingCommandCount - pending} commands");
        Check(_sel.DroppedOrders == dropped + 1, $"overflowing minimap order: DroppedOrders {dropped} -> {_sel.DroppedOrders}");
        Check(_sel.Selection.Count == n, "overflow changed the selection");
        // One fewer queued command: exactly fits, all n go out and the queue is exactly full.
        // (The queue only drains on Tick, which runs in _Process; this method is synchronous.)
        GD.Print($"overflow: {pending} pending + {n} selected > {cap}: dropped {_sel.DroppedOrders - dropped}, enqueued {_sim.PendingCommandCount - pending}");
    }

    private async Task RefreshKeyedToTicks()
    {
        await Frame(); // drain the Noops
        foreach (double speed in new[] { 8.0, 1.0, 0.25 })
        {
            _runner.GameSpeed = speed;
            int lastTick = -1, minGap = int.MaxValue, refreshes = 0, frames = 0, refreshesSeen = _mini.Refreshes;
            int t0 = _sim.TickNumber;
            ulong f0 = Engine.GetProcessFrames();
            double wall0 = Time.GetTicksMsec();
            while (Time.GetTicksMsec() - wall0 < 1500)
            {
                await Frame();
                frames++;
                if (_mini.Refreshes != refreshesSeen)
                {
                    Check(_mini.Refreshes == refreshesSeen + 1, "more than one refresh in a frame");
                    refreshesSeen = _mini.Refreshes;
                    if (lastTick >= 0) minGap = Math.Min(minGap, _sim.TickNumber - lastTick);
                    lastTick = _sim.TickNumber;
                    refreshes++;
                }
            }
            int ticks = _sim.TickNumber - t0;
            GD.Print($"speed {speed}: {ticks} ticks, {frames} frames, {refreshes} refreshes, min gap {minGap} ticks");
            Check(minGap >= Minimap.RefreshTicks, $"speed {speed}: refreshes {minGap} ticks apart (< {Minimap.RefreshTicks})");
            Check(refreshes <= ticks / Minimap.RefreshTicks + 1, $"speed {speed}: {refreshes} refreshes for {ticks} ticks");
            Check(ticks < 8 || refreshes >= ticks / (Minimap.RefreshTicks * 3), $"speed {speed}: only {refreshes} refreshes for {ticks} ticks");
        }
        _runner.GameSpeed = 1.0;
    }

    private async Task RespawnAsEnemy()
    {
        UnitStore u = _sim.World.Units;
        int a = -1;
        for (int i = 0; i < u.Capacity && a < 0; i++) if (u.Alive[i] && u.Owner[i] == 0) a = i;
        System.Numerics.Vector2 spot = u.Position[a];
        _mini.Raster!.TryPixelOf(spot, out int px, out int py);
        // Kill everything else on that cell so the pixel is a's alone.
        for (int i = 0; i < u.Capacity; i++)
        {
            if (i == a || !u.Alive[i]) continue;
            _mini.Raster.TryPixelOf(u.Position[i], out int x, out int y);
            if (x == px && y == py) u.Free(new EntityHandle(i, u.Generation[i]));
        }
        u.Free(new EntityHandle(a, u.Generation[a]));
        int r0 = _mini.Refreshes;
        while (_mini.Refreshes == r0) await Frame();
        Color dead = _mini.DotsImage.GetPixel(px, py);
        _sim.Enqueue(Command.SpawnUnit(1, _data.Factions[1].Units[0], spot));
        int t0 = _sim.TickNumber;
        while (_sim.TickNumber < t0 + 2) await Frame(); // applies on the tick after enqueue
        Check(u.Alive[a] && u.Owner[a] == 1, $"slot {a} not reused by the enemy");
        r0 = _mini.Refreshes;
        while (_mini.Refreshes == r0) await Frame();
        Color got = _mini.DotsImage.GetPixel(px, py);
        Color want = UnitViews.ColorFromRgb(_data.Factions[1].PrimaryColor);
        Check(dead.A < 0.01f || ColourAtOtherSlot(px, py), $"pixel ({px}, {py}) still opaque {dead} after its only unit died");
        Check(Mathf.Abs(got.R - want.R) < 0.01f && Mathf.Abs(got.G - want.G) < 0.01f && Mathf.Abs(got.B - want.B) < 0.01f && got.A > 0.99f,
            $"respawned enemy in slot {a}: pixel {got}, want {want}");
    }

    private bool ColourAtOtherSlot(int px, int py)
    {
        UnitStore u = _sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            _mini.Raster!.TryPixelOf(u.Position[i], out int x, out int y);
            if (x == px && y == py) return true; // someone walked onto it meanwhile
        }
        return false;
    }

    private void OutlineWithClampedFocus()
    {
        (System.Numerics.Vector2 pos, System.Numerics.Vector2 size) = _mini.Fit.MapRect;
        var mapRect = new Rect2(pos.X - 0.01f, pos.Y - 0.01f, size.X + 0.02f, size.Y + 0.02f);
        foreach (float zoom in new[] { CameraLimits.MinZoom, CameraLimits.MaxZoom, -50f, 1e6f })
        {
            foreach (System.Numerics.Vector2 f in new System.Numerics.Vector2[] { new(-1000, -1000), new(1e6f, -5), new(float.NaN, 3), new(5, 1e9f) })
            {
                _camera.SetZoom(zoom);
                _camera.SetFocus(f.X, f.Y);
                _mini.UpdateOutline();
                ReadOnlySpan<Vector2> o = _mini.Outline;
                for (int i = 0; i < 4; i++)
                    Check(float.IsFinite(o[i].X) && float.IsFinite(o[i].Y) && mapRect.HasPoint(o[i]), $"zoom {zoom} focus {f}: outline corner {i} {o[i]} outside {mapRect}");
            }
        }
        _camera.SetZoom(CameraLimits.DefaultZoom);
    }

    private void Push(InputEvent e) => GetViewport().PushInput(e);

    private static InputEventMouseMotion Motion(Vector2 at, MouseButtonMask mask, Vector2 relative = default) =>
        new() { Position = at, GlobalPosition = at, ButtonMask = mask, Relative = relative };

    private static InputEventMouseButton Button(MouseButton b, bool pressed, Vector2 at, bool doubleClick = false) =>
        new() { ButtonIndex = b, Pressed = pressed, Position = at, GlobalPosition = at, DoubleClick = doubleClick,
            ButtonMask = pressed ? b switch { MouseButton.Left => MouseButtonMask.Left, MouseButton.Right => MouseButtonMask.Right, MouseButton.Middle => MouseButtonMask.Middle, _ => 0 } : 0 };

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private void Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
    }
}
