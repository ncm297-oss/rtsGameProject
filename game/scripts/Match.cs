using System;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

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
        _runner.Forests = options.Forests;
        _runner.GoldMines = options.Mines;
        _runner.Start(data);
        Simulation sim = _runner.Simulation!;

        Heightmap map = sim.World.Heightmap;
        GetNode<TerrainView>("World3D/TerrainView").Build(map);
        var props = GetNode<PropsView>("World3D/PropsView");
        props.Bind(data, sim.World.Resources.Capacity);
        props.Runner = _runner;
        props.Sync(sim.World);

        var camera = GetNode<RtsCamera>("RtsCamera");
        camera.SetMap(map.Width, map.Height);
        camera.EdgePanEnabled = options.ScreenshotPath == null;

        var units = GetNode<UnitViews>("World3D/UnitViews");
        units.Bind(data, sim.World.Units.Capacity);
        units.Runner = _runner;

        var selection = GetNode<SelectionController>("SelectionController");
        selection.Init(_runner, camera, GetNode<SelectionRings>("World3D/SelectionRings"));

        System.Numerics.Vector2 focus = SpawnArmies(sim, options.UnitsPerPlayer);
        camera.SetFocus(focus.X, focus.Y);
        if (options.Zoom is float zoom) camera.SetZoom(zoom);

        var hud = GetNode<CanvasLayer>("Hud");
        hud.Visible = !options.NoHud;
        if (!options.NoHud)
        {
            // Player p plays faction p until the M6 lobby, so a player's dot colour is that faction's.
            var playerRgb = new uint[sim.World.Config.PlayerCount];
            for (int p = 0; p < playerRgb.Length; p++) playerRgb[p] = data.Factions[p % data.Factions.Length].PrimaryColor;
            hud.GetNode<Minimap>("Minimap").Init(_runner, camera, selection, playerRgb);
        }

        GetNode<DebugOverlay>("DebugOverlay").Init(_runner, selection, camera,
            GetNode<NavOverlayView>("World3D/NavOverlay"), GetNode<FlowArrowsView>("World3D/FlowArrows"), options.DebugOverlay);
        GetNode<Screenshotter>("Screenshotter").Arm(options.ScreenshotPath, options.ScreenshotAfter);
        // The seed printed is the one the sim uses (BUG-0041: the long export printed 2^64-1 as -1).
        ResourcePlacement placed = sim.World.ResourcePlacement;
        GD.Print($"Match started: seed {unchecked((ulong)_runner.Seed)}, map {map.Width} x {map.Height}, " +
            $"speed {_runner.GameSpeed:0.##}x, {options.UnitsPerPlayer} units per player, " +
            $"forests {placed.Forests} trees {placed.Trees} mines {placed.Mines}");
    }

    /// <summary>Enqueues each player's start army in its <see cref="StartLayout"/> block; returns player 0's block centre (meters).</summary>
    /// <remarks>
    /// Until the M6 lobby, player p plays faction p (ids in data order: malazan, whirlwind) and
    /// spawns its roster round-robin. The units appear on the sim's next tick.
    /// </remarks>
    private static System.Numerics.Vector2 SpawnArmies(Simulation sim, int perPlayer)
    {
        GameData data = sim.World.Data;
        NavGrid grid = sim.World.NavGrid;
        var focus = new System.Numerics.Vector2(grid.Width, grid.Height) * MapConstants.CellSize / 2;
        int players = Math.Min(sim.World.Config.PlayerCount, 2); // two start blocks: west and east
        for (int p = 0; p < players; p++)
        {
            FactionDef faction = data.Factions[p % data.Factions.Length];
            float maxRadius = 0f;
            foreach (int t in faction.Units) maxRadius = Math.Max(maxRadius, data.Units[t].Radius);
            System.Numerics.Vector2[] spots = StartLayout.Block(grid, perPlayer, west: p == 0, maxRadius);
            if (spots.Length < perPlayer)
                GD.PushWarning($"Player {p}: only {spots.Length} of {perPlayer} start positions fit.");
            var sum = System.Numerics.Vector2.Zero;
            for (int k = 0; k < spots.Length; k++)
            {
                sim.Enqueue(Command.SpawnUnit(p, faction.Units[k % faction.Units.Length], spots[k]));
                sum += spots[k];
            }
            if (p == 0 && spots.Length > 0) focus = sum / spots.Length;
        }
        return focus;
    }

    public override void _ExitTree()
    {
        // Lets the headless smoke log show the sim actually ticked.
        if (_runner.Simulation != null)
            GD.Print($"Match stopped at tick {_runner.Simulation.TickNumber}");
    }
}
