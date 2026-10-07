using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>M2-2: drives the real Match scene's <see cref="SelectionController"/> with injected mouse events: click, Shift toggle, box, Shift box, empty-ground clear, right-click move, off-map click, pruning.</summary>
/// <remarks>
/// Headless. Run: <c>&amp; $env:GODOT --headless --path game res://tests/SelectionTest.tscn</c>; prints
/// "SELECTION TEST PASS" and exits 0, or prints each failure and exits 1.
/// </remarks>
public partial class SelectionTest : Node
{
    private readonly List<string> _failures = new();
    private SelectionController _sel = null!;
    private SelectionRings _rings = null!;
    private RtsCamera _camera = null!;
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
        Input.ActionRelease("select_add");
        foreach (string f in _failures) GD.Print($"SELECTION TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("SELECTION TEST PASS");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    private async Task Run()
    {
        // Headless windows are 64 x 64; give the picker a real screen (the project's default size).
        GetTree().Root.Size = new Vector2I(1152, 648);
        await Frame();
        DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
        if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        match.Start(loaded.Data!, LaunchOptions.Parse(new[] { "--units", "40", "--no-bases" })); // armies only, as in M2 (M3-V1)
        _sel = match.GetNode<SelectionController>("SelectionController");
        _rings = match.GetNode<SelectionRings>("World3D/SelectionRings");
        _camera = match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _sim = match.GetNode<SimRunner>("SimRunner").Simulation!;
        UnitStore u = _sim.World.Units;
        while (_sim.TickNumber < 3) await Frame();
        Check(u.Count == 80, $"{u.Count} units spawned, expected 80");
        Vector2 screen = _camera.GetViewport().GetVisibleRect().Size;

        // Own and enemy units whose centres are on screen.
        var own = new List<(int Slot, Vector2 At)>();
        var enemy = new List<(int Slot, Vector2 At)>();
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || !_sel.TryScreenPosition(i, out Vector2 p) || !OnScreen(p, screen)) continue;
            (u.Owner[i] == SelectionController.LocalPlayer ? own : enemy).Add((i, p));
        }
        GD.Print($"screen {screen}; on screen: {own.Count} own, {enemy.Count} enemy units");
        Check(own.Count >= 10, "too few own units on screen at the start camera");
        (int a, Vector2 atA) = own[0];
        (int b, Vector2 atB) = own[^1];
        EntityHandle ha = Handle(a), hb = Handle(b);

        // Click: exactly A.
        Click(atA);
        Expect("click A", ha);
        // Shift + click toggles B in, then out again.
        Input.ActionPress("select_add");
        Click(atB);
        Expect("shift-click B", ha, hb);
        Click(atB);
        Expect("shift-click B again", ha);
        Input.ActionRelease("select_add");

        // Enemies are never selected: clicking one is a click on no own unit, which clears.
        foreach ((int e, Vector2 atE) in enemy)
        {
            if (NearestOwnDistance(own, atE) < 2 * ScreenPicker.MinPickRadiusPx) continue;
            Click(atE);
            Expect($"click enemy {e}");
            break;
        }

        // Box over the whole screen: every own unit on screen, no enemy.
        Box(new Vector2(1, 1), screen - new Vector2(1, 1));
        Check(_sel.Selection.Count == own.Count, $"full-screen box selected {_sel.Selection.Count}, expected {own.Count}");
        foreach (EntityHandle h in _sel.Selection.Items.ToArray())
            Check(u.Owner[h.Index] == SelectionController.LocalPlayer, $"box selected enemy slot {h.Index}");
        await Frame();
        Check(_rings.ShownCount == _sel.Selection.Count, $"{_rings.ShownCount} rings for {_sel.Selection.Count} selected");

        // Plain click replaces; Shift + box adds; a plain box replaces.
        Click(atA);
        Input.ActionPress("select_add");
        Box(atB - new Vector2(6, 6), atB + new Vector2(6, 6));
        Input.ActionRelease("select_add");
        Check(_sel.Selection.Contains(ha) && _sel.Selection.Contains(hb), "shift-box should add B to A");
        Box(atB + new Vector2(6, 6), atB - new Vector2(6, 6)); // inverted drag corners
        Check(!_sel.Selection.Contains(ha) && _sel.Selection.Contains(hb), "plain box should replace the selection with B");

        // Click on empty ground clears.
        Vector2 empty = FindEmpty(own, enemy, screen);
        Click(empty);
        Expect("click empty ground");

        // Right-click: one Move per selected unit; 3 s later each is Moving or closer to the point.
        Box(new Vector2(1, 1), screen - new Vector2(1, 1));
        int selected = _sel.Selection.Count;
        Vector3 o = _camera.ProjectRayOrigin(empty), d = _camera.ProjectRayNormal(empty);
        Check(GroundPicker.TryPick(_sim.World.Heightmap, new(o.X, o.Y, o.Z), new(d.X, d.Y, d.Z), out System.Numerics.Vector3 hit), "order point missed the map");
        var target = new System.Numerics.Vector2(hit.X, hit.Z);
        var before = new Dictionary<int, float>();
        foreach (EntityHandle h in _sel.Selection.Items.ToArray()) before[h.Index] = System.Numerics.Vector2.Distance(u.Position[h.Index], target);
        int pending = _sim.PendingCommandCount;
        RightClick(empty);
        Check(_sim.PendingCommandCount - pending == selected, $"{_sim.PendingCommandCount - pending} commands for {selected} selected units");
        int start = _sim.TickNumber;
        Engine.TimeScale = 4.0;
        while (_sim.TickNumber < start + 60) await Frame();
        Engine.TimeScale = 1.0;
        int moving = 0, closer = 0;
        foreach (EntityHandle h in _sel.Selection.Items.ToArray())
        {
            float now = System.Numerics.Vector2.Distance(u.Position[h.Index], target);
            bool isMoving = u.State[h.Index] == UnitState.Moving;
            if (isMoving) moving++;
            if (now < before[h.Index]) closer++;
            Check(isMoving || now < before[h.Index], $"slot {h.Index} neither Moving nor closer ({before[h.Index]:F1} -> {now:F1} m)");
        }
        GD.Print($"right-click: {selected} orders to ({target.X:F1}, {target.Y:F1}); after 3 s {moving} Moving, {closer} closer");

        // A unit dying mid-drag is not selected; dead handles are pruned and lose their ring.
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(1, 1) });
        u.Free(Handle(a));
        _sel._UnhandledInput(new InputEventMouseMotion { Position = screen - new Vector2(1, 1) });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = screen - new Vector2(1, 1) });
        Check(!_sel.Selection.Contains(ha), "a unit that died mid-drag was selected");
        int count = _sel.Selection.Count;
        EntityHandle victim = _sel.Selection.Items[0];
        u.Free(victim);
        await Frame();
        Check(_sel.Selection.Count == count - 1 && !_sel.Selection.Contains(victim), "dead handle not pruned");
        Check(_rings.ShownCount == _sel.Selection.Count, $"{_rings.ShownCount} rings for {_sel.Selection.Count} selected after a death");

        // Off-map right-click enqueues nothing: focus the north-west corner and click the top-left pixel.
        _camera.SetFocus(0f, 0f);
        await Frame();
        o = _camera.ProjectRayOrigin(new Vector2(4, 4));
        d = _camera.ProjectRayNormal(new Vector2(4, 4));
        Check(!GroundPicker.TryPick(_sim.World.Heightmap, new(o.X, o.Y, o.Z), new(d.X, d.Y, d.Z), out _), "corner ray should miss the map");
        pending = _sim.PendingCommandCount;
        RightClick(new Vector2(4, 4));
        Check(_sim.PendingCommandCount == pending, "off-map right-click enqueued commands");
    }

    private EntityHandle Handle(int slot) => new(slot, _sim.World.Units.Generation[slot]);

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private static bool OnScreen(Vector2 p, Vector2 size) => p.X > 2 && p.Y > 2 && p.X < size.X - 2 && p.Y < size.Y - 2;

    private void Click(Vector2 at)
    {
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
    }

    private void Box(Vector2 from, Vector2 to)
    {
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = from });
        _sel._UnhandledInput(new InputEventMouseMotion { Position = (from + to) / 2 });
        _sel._UnhandledInput(new InputEventMouseMotion { Position = to });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = to });
    }

    private void RightClick(Vector2 at)
    {
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = at });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = at });
    }

    private static float NearestOwnDistance(List<(int Slot, Vector2 At)> own, Vector2 p)
    {
        float best = float.MaxValue;
        foreach ((int _, Vector2 at) in own) best = Mathf.Min(best, at.DistanceTo(p));
        return best;
    }

    // A screen point at least 30 px from every unit, in the middle half of the screen (so it is on the map).
    private static Vector2 FindEmpty(List<(int Slot, Vector2 At)> own, List<(int Slot, Vector2 At)> enemy, Vector2 size)
    {
        for (float y = size.Y * 0.3f; y < size.Y * 0.8f; y += 10)
        {
            for (float x = size.X * 0.1f; x < size.X * 0.9f; x += 10)
            {
                var p = new Vector2(x, y);
                if (NearestOwnDistance(own, p) > 30f && NearestOwnDistance(enemy, p) > 30f) return p;
            }
        }
        throw new InvalidOperationException("no empty ground on screen");
    }

    private void Expect(string what, params EntityHandle[] handles)
    {
        bool ok = _sel.Selection.Count == handles.Length;
        foreach (EntityHandle h in handles) ok &= _sel.Selection.Contains(h);
        GD.Print($"selection check {what}: {_sel.Selection.Count} selected");
        Check(ok, $"{what}: {_sel.Selection.Count} selected, expected [{string.Join(", ", handles)}]");
    }

    private void Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
    }
}
