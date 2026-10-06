using System;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>For looking, not pass/fail (M2-5): the debug overlay on, player 0's army ordered across the map, camera on the walkers, then one PNG.</summary>
/// <remarks>
/// Windowed: <c>&amp; $env:GODOT --path game res://tests/DebugOverlayShot.tscn -- --out C:\temp\overlay.png [--units 100] [--zoom 40]</c>.
/// Seed 1 (the scene's default). Every own unit gets a Move through the real
/// <see cref="SelectionController.Order"/> to the passable cell nearest the map's north-east quarter;
/// the shot is taken 3 s of game time later, when the field is cached and the walkers are on the move.
/// </remarks>
public partial class DebugOverlayShot : Node
{
    private Match _match = null!;
    private Simulation _sim = null!;
    private SelectionController _sel = null!;
    private RtsCamera _camera = null!;
    private string _out = "user://debug-overlay.png";
    private int _orderedAt = -1;
    private int _framesAfter;

    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        string units = "100", zoom = "40";
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--units") units = args[i + 1];
            else if (args[i] == "--out") _out = args[i + 1];
            else if (args[i] == "--zoom") zoom = args[i + 1];
        }
        DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
        if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _match.Start(loaded.Data!, LaunchOptions.Parse(new[] { "--units", units, "--zoom", zoom, "--debug-overlay" }));
        _sim = _match.GetNode<SimRunner>("SimRunner").Simulation!;
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
    }

    public override void _Process(double delta)
    {
        UnitStore u = _sim.World.Units;
        NavGrid g = _sim.World.NavGrid;
        if (_orderedAt < 0)
        {
            if (u.Count == 0) return;
            for (int i = 0; i < u.Capacity; i++)
                if (u.Alive[i] && u.Owner[i] == SelectionController.LocalPlayer) _sel.Selection.Add(new EntityHandle(i, u.Generation[i]));
            int goal = FlowField.NearestPassable(g, (g.Height / 4) * g.Width + g.Width * 3 / 4);
            System.Numerics.Vector2 p = g.CellCenter(goal % g.Width, goal / g.Width);
            _sel.Order(Rts.Sim.Commands.CommandKind.Move, new Vector2(p.X, p.Y), false);
            _orderedAt = _sim.TickNumber;
            GD.Print($"ordered {_sel.Selection.Count} units to cell {goal} ({p.X}, {p.Y})");
            return;
        }
        if (_sim.TickNumber < _orderedAt + 60) return;
        if (_framesAfter++ == 0)
        {
            // Camera between the walkers' mean and a bit toward the goal, so both arrows and units are in view.
            System.Numerics.Vector2 sum = default;
            int n = 0;
            foreach (EntityHandle h in _sel.Selection.Items) { sum += u.Position[h.Index]; n++; }
            if (n > 0) _camera.SetFocus(sum.X / n + 12f, sum.Y / n - 8f);
            return;
        }
        if (_framesAfter < 10) return;
        Image shot = GetViewport().GetTexture().GetImage();
        string full = ProjectSettings.GlobalizePath(_out);
        Error err = shot.SavePng(full);
        var overlay = _match.GetNode<DebugOverlay>("DebugOverlay");
        GD.Print($"saved {full} ({err}): tick {_sim.TickNumber}, goal {overlay.ShownGoal}, arrows {_match.GetNode<FlowArrowsView>("World3D/FlowArrows").ShownCount}, " +
            $"moving {overlay.MovingUnits}, fields {overlay.CachedFields}, overlay frame {overlay.LastLayersMs:F3} ms");
        GetTree().Quit(err == Error.Ok ? 0 : 1);
    }
}
