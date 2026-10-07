using System;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>For looking, not pass/fail (M2-7): player 0's army marches across the map; when units are on a ramp the camera goes to them and two PNGs are saved (the ramp crossing and the minimap corner).</summary>
/// <remarks>
/// Windowed: <c>&amp; $env:GODOT --path game res://tests/MarchShot.tscn -- --out-dir C:\temp\shots [--units 100] [--zoom 30]</c>
/// writes <c>ramp-crossing.png</c> (full window) and <c>minimap-corner.png</c> (the bottom-left 240 px of
/// the same frame). Seed 1. The whole army is box-selected and ordered east through the real
/// <see cref="SelectionController"/>; the march runs at 4x until at least <see cref="MinOnRamp"/>
/// own units stand on ramp cells (or 60 s of game time pass), then at 1x for the shot. Prints the
/// count on the ramp. Headless runs print that capture is unavailable and quit 0.
/// </remarks>
public partial class MarchShot : Node
{
    /// <summary>Own units that must be on ramp cells before the shot.</summary>
    public const int MinOnRamp = 10;

    private Match _match = null!;
    private SimRunner _runner = null!;
    private Simulation _sim = null!;
    private SelectionController _sel = null!;
    private RtsCamera _camera = null!;
    private string _outDir = "user://";
    private float _zoom = 30f;
    private int _phase, _frames, _onRamp;

    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        string units = "100";
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--units") units = args[i + 1];
            else if (args[i] == "--out-dir") _outDir = args[i + 1];
            else if (args[i] == "--zoom") _zoom = float.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture);
        }
        DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
        if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _match.Start(loaded.Data!, LaunchOptions.Parse(new[] { "--units", units, "--zoom", "60", "--mute" }));
        _runner = _match.GetNode<SimRunner>("SimRunner");
        _sim = _runner.Simulation!;
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
    }

    public override void _Process(double delta)
    {
        if (DisplayServer.GetName() == "headless")
        {
            GD.Print("MarchShot: capture unavailable in headless mode.");
            GetTree().Quit(0);
            return;
        }
        UnitStore u = _sim.World.Units;
        Heightmap map = _sim.World.Heightmap;
        switch (_phase)
        {
            case 0: // armies spawned: box the screen, order east
                if (_sim.TickNumber < 3) return;
                Vector2 view = GetViewport().GetVisibleRect().Size;
                _sel.BoxSelect(new Vector2(1, 1), view - new Vector2(1, 1), add: false);
                System.Numerics.Vector2 east = StartLayout.Block(_sim.World.NavGrid, 1, west: false, 1f)[0];
                _sel.Order(CommandKind.Move, new Vector2(east.X, east.Y), queued: false);
                GD.Print($"MarchShot: {_sel.Selection.Count} selected, ordered to ({east.X}, {east.Y})");
                _runner.GameSpeed = 4;
                _phase = 1;
                return;
            case 1: // march until enough units stand on ramp cells
                _onRamp = CountOnRamp(u, map, out System.Numerics.Vector2 centre);
                if (_onRamp < MinOnRamp && _sim.TickNumber < 1200) return;
                _runner.GameSpeed = 1;
                _camera.SetZoom(_zoom);
                _camera.SetFocus(centre.X, centre.Y);
                _phase = 2;
                return;
            case 2: // let the camera and interpolation settle, follow the ramp group
                if (++_frames < 6) return;
                _onRamp = CountOnRamp(u, map, out _);
                Save();
                return;
        }
    }

    private static int CountOnRamp(UnitStore u, Heightmap map, out System.Numerics.Vector2 centre)
    {
        var sum = System.Numerics.Vector2.Zero;
        int n = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.Owner[i] != SelectionController.LocalPlayer) continue;
            int cx = (int)(u.Position[i].X / MapConstants.CellSize), cy = (int)(u.Position[i].Y / MapConstants.CellSize);
            if (cx < 0 || cy < 0 || cx >= map.Width || cy >= map.Height || !map.IsRamp(cx, cy)) continue;
            sum += u.Position[i];
            n++;
        }
        centre = n > 0 ? sum / n : default;
        return n;
    }

    private void Save()
    {
        Image shot = GetViewport().GetTexture().GetImage();
        string dir = ProjectSettings.GlobalizePath(_outDir);
        string ramp = System.IO.Path.Combine(dir, "ramp-crossing.png");
        string mini = System.IO.Path.Combine(dir, "minimap-corner.png");
        Error a = shot.SavePng(ramp);
        int side = Math.Min(240, Math.Min(shot.GetWidth(), shot.GetHeight()));
        Error b = shot.GetRegion(new Rect2I(0, shot.GetHeight() - side, side, side)).SavePng(mini);
        GD.Print($"MarchShot: {_onRamp} own units on ramp cells at tick {_sim.TickNumber}; saved {ramp} ({a}), {mini} ({b})");
        GetTree().Quit(a == Error.Ok && b == Error.Ok ? 0 : 1);
    }
}
