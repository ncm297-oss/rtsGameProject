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

    /// <summary>The <c>--bench</c> runner, or null for a normal run.</summary>
    public BenchRunner? Bench { get; private set; }

    /// <summary>The start bases placed by <see cref="Start"/> (Town Hall anchor and worker spots per player); null before it and under <c>--no-bases</c>.</summary>
    public StartBasePlan? Bases { get; private set; }

    /// <summary>Starting workers per player this match asked for (<c>--workers</c>, else <c>rules.json</c> <c>startingWorkers</c>).</summary>
    public int WorkersPerPlayer { get; private set; }

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
        _runner.Combat = !options.NoCombat;
        WorkersPerPlayer = options.NoBases ? 0 : options.Workers ?? data.Rules.StartingWorkers;
        // The start workers come on top of the armies (the bench keeps its 100 / 1,000 a side), so the store grows past 2,000 when needed.
        _runner.UnitCapacity = Math.Max(_runner.UnitCapacity, _runner.PlayerCount * (options.UnitsPerPlayer + WorkersPerPlayer));
        _runner.Start(data);
        Simulation sim = _runner.Simulation!;

        Heightmap map = sim.World.Heightmap;
        // The local player's fog (M4-V4): bound before the views, whose materials and hide rule read it.
        var fog = GetNode<FogOfWar>("World3D/FogOfWar");
        fog.Bind(sim.World, SelectionController.LocalPlayer, enabled: !options.NoFog);
        fog.Runner = _runner;
        GetNode<TerrainView>("World3D/TerrainView").Build(map, fog);
        var props = GetNode<PropsView>("World3D/PropsView");
        props.Bind(data, sim.World.Resources.Capacity, fog);
        props.Runner = _runner;
        props.Sync(sim.World);

        var camera = GetNode<RtsCamera>("RtsCamera");
        camera.SetMap(map.Width, map.Height);
        // Scripted runs: the mouse must not move the shot or the benchmark.
        camera.EdgePanEnabled = options.ScreenshotPath == null && options.BenchSeconds == null;
        if (options.Vsync is bool vsync && DisplayServer.GetName() != "headless")
            DisplayServer.WindowSetVsyncMode(vsync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);

        var units = GetNode<UnitViews>("World3D/UnitViews");
        units.Bind(data, sim.World.Units.Capacity);
        units.Runner = _runner;
        units.Fog = fog;
        units.Camera = camera;

        // Player p plays faction p until the M6 lobby (World.FactionOf), so a player's colour is that faction's.
        var playerRgb = new uint[sim.World.Config.PlayerCount];
        for (int p = 0; p < playerRgb.Length; p++) playerRgb[p] = data.Factions[sim.World.FactionOf(p)].PrimaryColor;
        var buildings = GetNode<BuildingViews>("World3D/BuildingViews");
        buildings.Bind(data, sim.World.Buildings.Capacity, playerRgb);
        buildings.Runner = _runner;
        buildings.Fog = fog;
        var combat = GetNode<CombatViews>("World3D/CombatViews");
        combat.Fog = fog;
        combat.Bind(data, sim.World.Units.Capacity, playerRgb);
        combat.Runner = _runner;
        combat.Camera = camera;
        var shots = GetNode<ProjectileViews>("World3D/ProjectileViews");
        shots.Bind(data, sim.World.Projectiles.Capacity, playerRgb);
        shots.Runner = _runner;
        shots.Fog = fog;

        Sfx.SetMuted(options.Mute);
        var selection = GetNode<SelectionController>("SelectionController");
        selection.Init(_runner, camera, GetNode<SelectionRings>("World3D/SelectionRings"), GetNode<Sfx>("Sfx"));
        selection.Outline = GetNode<BuildingOutline>("World3D/BuildingOutline");
        selection.Fog = fog;
        GetNode<RallyMarker>("World3D/RallyMarker").Init(_runner, selection);
        var targetRing = GetNode<TargetRing>("World3D/TargetRing");
        targetRing.Init(_runner);
        targetRing.Fog = fog;
        selection.TargetRing = targetRing;
        var abilityViews = GetNode<AbilityViews>("World3D/AbilityViews");
        abilityViews.Fog = fog;
        abilityViews.Camera = camera;
        abilityViews.Bind(data, sim.World.Units.Capacity, _runner, selection);

        System.Numerics.Vector2[][] blocks = SpawnArmies(sim, options.UnitsPerPlayer, out System.Numerics.Vector2 focus);
        Bases = options.NoBases ? null : SpawnBases(sim, blocks, WorkersPerPlayer, out _);
        // No army: look at player 0's Town Hall instead of the map centre.
        if (options.UnitsPerPlayer == 0 && Bases != null && Bases.HallAnchor[0] >= 0)
            focus = StartBase.FootprintCenter(sim.World.NavGrid, data.Buildings[Bases.HallType[0]], Bases.HallAnchor[0]);
        camera.SetFocus(focus.X, focus.Y);
        if (options.Zoom is float zoom) camera.SetZoom(zoom);

        var hud = GetNode<CanvasLayer>("Hud");
        hud.Visible = !options.NoHud;
        Minimap? minimap = null;
        if (!options.NoHud)
        {
            minimap = hud.GetNode<Minimap>("Minimap");
            minimap.Fog = fog;
            minimap.Init(_runner, camera, selection, playerRgb);
            // Resource names come from the local player's faction data (CLAUDE.md rule 8).
            FactionDef local = data.Factions[sim.World.FactionOf(SelectionController.LocalPlayer)];
            UiText? ui = UiText.Shared;
            hud.GetNode<ResourceBar>("ResourceBar").Init(_runner, SelectionController.LocalPlayer, local.GoldName, local.WoodName, ui?.Hud(HudText.Pop) ?? "",
                ui?.Hud(HudText.Kills) ?? "", ui?.Hud(HudText.Losses) ?? "");
            // The card's and panel's labels and menus are view data (ui.json); without them they stay hidden and the errors are logged.
            var card = hud.GetNode<CommandCard>("CommandCard");
            var panel = hud.GetNode<SelectionPanel>("SelectionPanel");
            if (ui != null)
            {
                var ghost = GetNode<BuildGhost>("World3D/BuildGhost");
                ghost.Init(_runner, camera, ui, SelectionController.LocalPlayer);
                card.Init(_runner, selection, ghost, ui);
                panel.Init(_runner, selection, ui);
            }
            else
            {
                card.Visible = false;
                panel.Visible = false;
            }
            hud.GetNode<ProductionQueueStrip>("QueueStrip").Init(_runner, selection);
        }

        GetNode<DebugOverlay>("DebugOverlay").Init(_runner, selection, camera,
            GetNode<NavOverlayView>("World3D/NavOverlay"), GetNode<FlowArrowsView>("World3D/FlowArrows"), options.DebugOverlay);
        var shot = GetNode<Screenshotter>("Screenshotter");
        shot.Arm(options.ScreenshotPath, options.ScreenshotAfter, quitAfter: options.BenchSeconds == null);
        if (options.BenchSeconds is double benchSeconds)
        {
            Bench = new BenchRunner { Name = "BenchRunner" };
            AddChild(Bench);
            Bench.Init(_runner, selection, camera, minimap, shot, benchSeconds, camera.Zoom);
        }
        // The seed printed is the one the sim uses (BUG-0041: the long export printed 2^64-1 as -1).
        ResourcePlacement placed = sim.World.ResourcePlacement;
        GD.Print($"Match started: seed {unchecked((ulong)_runner.Seed)}, map {map.Width} x {map.Height}, " +
            $"speed {_runner.GameSpeed:0.##}x, {options.UnitsPerPlayer} units per player, " +
            $"forests {placed.Forests} trees {placed.Trees} mines {placed.Mines}, " +
            $"town halls {Halls(Bases)}, {WorkersPerPlayer} workers per player" + (options.NoCombat ? ", combat off" : "") + (options.NoFog ? ", fog not drawn" : ""));
    }

    /// <summary>Enqueues each player's start army in its <see cref="StartLayout"/> block; returns each player's block, and player 0's block centre (meters) in <paramref name="focus"/>.</summary>
    /// <remarks>
    /// Until the M6 lobby, player p plays faction p (ids in data order: malazan, whirlwind) and
    /// spawns its roster round-robin. The units appear on the sim's next tick.
    /// </remarks>
    private static System.Numerics.Vector2[][] SpawnArmies(Simulation sim, int perPlayer, out System.Numerics.Vector2 focus)
    {
        GameData data = sim.World.Data;
        NavGrid grid = sim.World.NavGrid;
        focus = new System.Numerics.Vector2(grid.Width, grid.Height) * MapConstants.CellSize / 2;
        int players = Math.Min(sim.World.Config.PlayerCount, 2); // two start blocks: west and east
        var blocks = new System.Numerics.Vector2[players][];
        for (int p = 0; p < players; p++)
        {
            FactionDef faction = data.Factions[p % data.Factions.Length];
            float maxRadius = 0f;
            foreach (int t in faction.Units) maxRadius = Math.Max(maxRadius, data.Units[t].Radius);
            System.Numerics.Vector2[] spots = blocks[p] = StartLayout.Block(grid, perPlayer, west: p == 0, maxRadius);
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
        return blocks;
    }

    /// <summary>
    /// Enqueues each player's start base after its army (M3-V1, docs/02 "Economy" starting state): a finished Town Hall
    /// (the faction's <c>town_hall</c> slot, through the dev <c>SpawnBuilding</c>) on the <see cref="StartBase"/> spot
    /// beside its block, then <paramref name="workers"/> of its <c>worker</c> slot round it. A player with no spot gets one
    /// warning and no base; the match runs on. Returns the plan; <paramref name="warnings"/> counts the players skipped.
    /// </summary>
    /// <remarks>Commands apply in (player, sequence) order next tick, so the hall sees its own army in place; the plan keeps clear of every army cell.</remarks>
    public static StartBasePlan SpawnBases(Simulation sim, System.Numerics.Vector2[][] blocks, int workers, out int warnings)
    {
        World world = sim.World;
        float maxRadius = 0f;
        foreach (UnitDef def in world.Data.Units) maxRadius = Math.Max(maxRadius, def.Radius);
        var factions = new int[blocks.Length];
        for (int p = 0; p < factions.Length; p++) factions[p] = world.FactionOf(p);
        StartBasePlan plan = StartBase.Plan(world.NavGrid, world.Data, world.Buildings, world.Resources.Alive, world.Resources.TypeId,
            world.Resources.Cell, factions, blocks, workers, maxRadius);
        NavGrid g = world.NavGrid;
        warnings = 0;
        for (int p = 0; p < blocks.Length; p++)
        {
            int anchor = plan.HallAnchor[p];
            if (anchor < 0)
            {
                warnings++;
                GD.PushWarning($"Player {p}: no open spot for a Town Hall beside the start block; no Town Hall or workers.");
                continue;
            }
            sim.Enqueue(Command.SpawnBuilding(p, plan.HallType[p], g.CellCenter(anchor % g.Width, anchor / g.Width)));
            foreach (System.Numerics.Vector2 spot in plan.Workers[p]) sim.Enqueue(Command.SpawnUnit(p, plan.WorkerType[p], spot));
            if (plan.Workers[p].Length < workers)
                GD.PushWarning($"Player {p}: only {plan.Workers[p].Length} of {workers} workers fit beside the Town Hall.");
        }
        return plan;
    }

    private static int Halls(StartBasePlan? plan)
    {
        if (plan == null) return 0;
        int n = 0;
        foreach (int anchor in plan.HallAnchor) if (anchor >= 0) n++;
        return n;
    }

    public override void _ExitTree()
    {
        // Lets the headless smoke log show the sim actually ticked.
        if (_runner.Simulation != null)
            GD.Print($"Match stopped at tick {_runner.Simulation.TickNumber}");
    }
}
