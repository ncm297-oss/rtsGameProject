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

/// <summary>QA (M2-2): BUG-0041 parser rows, the facing sign at every angle, pool and selection across a slot freed and respawned (as an enemy) mid-drag.</summary>
/// <remarks>
/// Headless. Run: <c>&amp; $env:GODOT --headless --path game res://tests/QaM22Test.tscn</c>; prints
/// "QA M2-2 TEST PASS" and exits 0, or prints each failure and exits 1.
/// </remarks>
public partial class QaM22Test : Node
{
    private readonly List<string> _failures = new();

    public override async void _Ready()
    {
        try
        {
            ParserRows();
            FacingAtEveryAngle();
            await RespawnMidDrag();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures) GD.Print($"QA M2-2 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M2-2 TEST PASS");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    private void ParserRows()
    {
        LaunchOptions o = LaunchOptions.Parse(new[] { "--seed", "--speed", "2" });
        Check(o.Seed == null && o.Speed == 2.0, $"--seed --speed 2: seed {o.Seed}, speed {o.Speed}");
        o = LaunchOptions.Parse(new[] { "--seed", "18446744073709551615" });
        Check(o.Seed == ulong.MaxValue, $"max seed parsed as {o.Seed}");
        o = LaunchOptions.Parse(new[] { "--units", "--zoom", "30" });
        Check(o.UnitsPerPlayer == LaunchOptions.DefaultUnitsPerPlayer && o.Zoom == 30f, $"--units --zoom 30: units {o.UnitsPerPlayer}, zoom {o.Zoom}");
        o = LaunchOptions.Parse(new[] { "--units", "1001" });
        Check(o.UnitsPerPlayer == LaunchOptions.DefaultUnitsPerPlayer, $"--units 1001 accepted as {o.UnitsPerPlayer}");
        o = LaunchOptions.Parse(new[] { "--units", "-1", "--speed", "3" });
        Check(o.UnitsPerPlayer == LaunchOptions.DefaultUnitsPerPlayer && o.Speed == 3.0, $"--units -1 --speed 3: units {o.UnitsPerPlayer}, speed {o.Speed}");
        o = LaunchOptions.Parse(new[] { "--units", "0" });
        Check(o.UnitsPerPlayer == 0, "--units 0 rejected");
        o = LaunchOptions.Parse(new[] { "--units", "1000" });
        Check(o.UnitsPerPlayer == 1000, "--units 1000 rejected");
        o = LaunchOptions.Parse(new[] { "--screenshot", "--units", "5" });
        Check(o.ScreenshotPath == null && o.UnitsPerPlayer == 5, $"--screenshot --units 5: path {o.ScreenshotPath}, units {o.UnitsPerPlayer}");
        o = LaunchOptions.Parse(new[] { "--zoom", "NaN", "--speed" });
        Check(o.Zoom == null && o.Speed == null, "--zoom NaN or trailing --speed accepted");
        o = LaunchOptions.Parse(new[] { "--bogus", "7", "--units", "9" });
        Check(o.UnitsPerPlayer == 9, "an unknown flag swallowed the next one");
    }

    // The developer's facing check walks +x only (theta = 0), where -theta - pi/2 and theta - pi/2 agree.
    private void FacingAtEveryAngle()
    {
        float worst = 0f;
        for (int k = 0; k < 64; k++)
        {
            float theta = -Mathf.Pi + k * Mathf.Tau / 64f + 0.013f;
            Vector3 forward = -new Basis(Vector3.Up, UnitViews.Yaw(theta)).Z;
            worst = Mathf.Max(worst, forward.DistanceTo(new Vector3(Mathf.Cos(theta), 0, Mathf.Sin(theta))));
        }
        GD.Print($"facing: worst forward error over 64 angles {worst:E2}");
        Check(worst < 1e-4f, $"yaw sign: worst forward error {worst}");
    }

    private async Task RespawnMidDrag()
    {
        GetTree().Root.Size = new Vector2I(1152, 648);
        await Frame();
        DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
        if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
        GameData data = loaded.Data!;
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        match.Start(data, LaunchOptions.Parse(new[] { "--units", "30" }));
        var sel = match.GetNode<SelectionController>("SelectionController");
        var views = match.GetNode<UnitViews>("World3D/UnitViews");
        var camera = match.GetNode<RtsCamera>("RtsCamera");
        camera.EdgePanEnabled = false;
        Simulation sim = match.GetNode<SimRunner>("SimRunner").Simulation!;
        UnitStore u = sim.World.Units;
        while (sim.TickNumber < 3) await Frame();
        Vector2 screen = camera.GetViewport().GetVisibleRect().Size;

        // Find an own unit on screen and select it by a click, then also find another.
        int a = -1;
        for (int i = 0; i < u.Capacity && a < 0; i++)
            if (u.Alive[i] && u.Owner[i] == 0 && sel.TryScreenPosition(i, out Vector2 p) && p.X > 5 && p.Y > 5 && p.X < screen.X - 5 && p.Y < screen.Y - 5) a = i;
        Check(a >= 0, "no own unit on screen");
        if (a < 0) return;
        var ha = new EntityHandle(a, u.Generation[a]);
        MeshInstance3D? nodeA = views.ViewOf(a);

        // Start a full-screen drag, kill A, respawn an ENEMY into A's slot (LIFO free list) at A's spot, release.
        sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(1, 1) });
        sel._UnhandledInput(new InputEventMouseMotion { Position = screen / 2 });
        System.Numerics.Vector2 spot = u.Position[a];
        u.Free(ha);
        sim.Enqueue(Command.SpawnUnit(1, data.Factions[1].Units[0], spot));
        int t0 = sim.TickNumber;
        while (sim.TickNumber < t0 + 2) await Frame();
        Check(u.Alive[a] && u.Owner[a] == 1, $"respawn did not reuse slot {a} for the enemy");
        sel._UnhandledInput(new InputEventMouseMotion { Position = screen - new Vector2(1, 1) });
        sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = screen - new Vector2(1, 1) });
        foreach (EntityHandle h in sel.Selection.Items.ToArray())
            Check(u.Owner[h.Index] == 0 && u.IsAlive(h), $"selection holds slot {h.Index} owner {u.Owner[h.Index]} alive {u.IsAlive(h)}");
        Check(!sel.Selection.Contains(new EntityHandle(a, u.Generation[a])), "enemy respawned mid-drag was selected");
        Check(sel.Selection.Count > 0, "full-screen box selected nothing");
        await Frame();
        Check(ReferenceEquals(views.ViewOf(a), nodeA) && nodeA != null && nodeA.Visible, "respawned slot did not reuse its visible node");
        Check(nodeA != null && nodeA.Mesh != null, "respawned node has no mesh");

        // Right-click with a stale handle in the selection: only live own units get orders.
        int selected = sel.Selection.Count;
        EntityHandle victim = sel.Selection.Items[0];
        u.Free(victim);
        int pending = sim.PendingCommandCount;
        Vector2 mid = screen / 2;
        sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = mid });
        int sent = sim.PendingCommandCount - pending;
        Check(sent == selected - 1, $"{sent} move commands after one of {selected} died (expected {selected - 1})");

        // Shift + click on empty ground keeps the selection (documented deviation).
        int before = sel.Selection.Count;
        Input.ActionPress("select_add");
        sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(2, screen.Y - 2) });
        sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = new Vector2(2, screen.Y - 2) });
        Input.ActionRelease("select_add");
        GD.Print($"respawn mid-drag: slot {a} enemy, {selected} selected, {sent} orders; shift-click empty kept {sel.Selection.Count} of {before}");
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private void Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
    }
}
