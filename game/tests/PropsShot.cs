using System;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;

namespace Rts.Game.Tests;

/// <summary>For looking, not pass/fail (M2-3b): seed 1, 100 units each, player 0's army sent through the forest nearest it; one PNG while it walks round the trees.</summary>
/// <remarks>
/// Windowed: <c>&amp; $env:GODOT --path game res://tests/PropsShot.tscn -- --out C:\temp\props.png</c>. The goal is
/// the passable cell nearest the point 40 m past the nearest tree on the line from the army's centre,
/// so the straight line crosses the forest. The shot is taken 6 s of game time after the order, camera
/// on that tree.
/// </remarks>
public partial class PropsShot : Node
{
    private Simulation _sim = null!;
    private SelectionController _sel = null!;
    private RtsCamera _camera = null!;
    private string _out = "user://props.png";
    private int _orderedAt = -1, _framesAfter;
    private System.Numerics.Vector2 _tree;

    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--out") _out = args[i + 1];
        DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
        if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        match.Start(loaded.Data!, LaunchOptions.Parse(new[] { "--seed", "1", "--units", "100" }));
        _sim = match.GetNode<SimRunner>("SimRunner").Simulation!;
        _sel = match.GetNode<SelectionController>("SelectionController");
        _camera = match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
    }

    public override void _Process(double delta)
    {
        World w = _sim.World;
        UnitStore u = w.Units;
        NavGrid g = w.NavGrid;
        if (_orderedAt < 0)
        {
            if (u.Count == 0) return;
            System.Numerics.Vector2 army = Mean(u);
            float best = float.MaxValue;
            ResourceStore r = w.Resources;
            for (int i = 0; i < r.Capacity; i++)
            {
                System.Numerics.Vector2 c = g.CellCenter(r.Cell[i] % g.Width, r.Cell[i] / g.Width);
                if (r.Alive[i] && r.TypeId[i] == w.Data.FindResource("tree") && System.Numerics.Vector2.Distance(c, army) < best)
                    (best, _tree) = (System.Numerics.Vector2.Distance(c, army), c);
            }
            System.Numerics.Vector2 through = _tree + System.Numerics.Vector2.Normalize(_tree - army) * 40f;
            int cx = Math.Clamp((int)(through.X / MapConstants.CellSize), 1, g.Width - 2), cy = Math.Clamp((int)(through.Y / MapConstants.CellSize), 1, g.Height - 2);
            int goal = FlowField.NearestPassable(g, cy * g.Width + cx);
            for (int i = 0; i < u.Capacity; i++)
                if (u.Alive[i] && u.Owner[i] == SelectionController.LocalPlayer) _sel.Selection.Add(new EntityHandle(i, u.Generation[i]));
            System.Numerics.Vector2 p = g.CellCenter(goal % g.Width, goal / g.Width);
            _sel.Order(Rts.Sim.Commands.CommandKind.Move, new Vector2(p.X, p.Y), false);
            _orderedAt = _sim.TickNumber;
            GD.Print($"army at ({army.X:F0}, {army.Y:F0}), tree at ({_tree.X:F0}, {_tree.Y:F0}), {_sel.Selection.Count} units ordered to ({p.X:F0}, {p.Y:F0})");
            return;
        }
        if (_sim.TickNumber < _orderedAt + 120) return;
        if (_framesAfter++ == 0) _camera.SetFocus(_tree.X, _tree.Y);
        if (_framesAfter < 10) return;
        string full = ProjectSettings.GlobalizePath(_out);
        Error err = GetViewport().GetTexture().GetImage().SavePng(full);
        GD.Print($"saved {full} ({err}): tick {_sim.TickNumber}, army mean {Mean(u)}");
        GetTree().Quit(err == Error.Ok ? 0 : 1);
    }

    private static System.Numerics.Vector2 Mean(UnitStore u)
    {
        System.Numerics.Vector2 sum = default;
        int n = 0;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == SelectionController.LocalPlayer) { sum += u.Position[i]; n++; }
        return n > 0 ? sum / n : sum;
    }
}
