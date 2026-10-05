using Godot;
using Rts.Sim.Data;
using Rts.Sim.Map;

namespace Rts.Game;

/// <summary>Root of Match.tscn: starts the sim and wires the views to it once data is loaded.</summary>
public partial class Match : Node3D
{
    private SimRunner _runner = null!;

    public override void _Ready()
    {
        _runner = GetNode<SimRunner>("SimRunner");
    }

    /// <summary>Starts the match with loaded data and the command-line overrides.</summary>
    public void Start(GameData data, LaunchOptions options)
    {
        if (options.Seed is ulong seed) _runner.Seed = unchecked((long)seed);
        if (options.Speed is double speed) _runner.GameSpeed = speed;
        _runner.Start(data);

        Heightmap map = _runner.Simulation!.World.Heightmap;
        GetNode<TerrainView>("World3D/TerrainView").Build(map);

        var camera = GetNode<RtsCamera>("RtsCamera");
        camera.SetMap(map.Width, map.Height);
        camera.EdgePanEnabled = options.ScreenshotPath == null;

        GetNode<DebugOverlay>("DebugOverlay").Runner = _runner;
        GetNode<Screenshotter>("Screenshotter").Arm(options.ScreenshotPath, options.ScreenshotAfter);
        GD.Print($"Match started: seed {_runner.Seed}, map {map.Width} x {map.Height}, speed {_runner.GameSpeed:0.##}x");
    }

    public override void _ExitTree()
    {
        // Lets the headless smoke log show the sim actually ticked.
        if (_runner.Simulation != null)
            GD.Print($"Match stopped at tick {_runner.Simulation.TickNumber}");
    }
}
