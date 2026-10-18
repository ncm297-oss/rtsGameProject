using System;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>Root of Match.tscn: starts the sim and wires the views to it once data is loaded.</summary>
public partial class Match : Node3D
{
    private SimRunner _runner = null!;
    private int[] _defeatedAt = Array.Empty<int>();

    /// <summary>The <c>--bench</c> runner, or null for a normal run.</summary>
    public BenchRunner? Bench { get; private set; }

    /// <summary>The start bases placed by <see cref="Start"/> (Town Hall anchor and worker spots per player); null before it and under <c>--no-bases</c>.</summary>
    public StartBasePlan? Bases { get; private set; }

    /// <summary>The "Match over:" dev lines printed so far: one per <c>World.DefeatEvents</c> entry seen (M6-V4; M6-V2 counted building drops).</summary>
    public int MatchOverLines { get; private set; }

    /// <summary>The local player's result, latched the first tick it holds (M6-V4, <see cref="Outcome"/>'s rule); presentation state.</summary>
    public MatchOutcome Outcome { get; private set; }

    /// <summary>Raised once per match, after the tick that gave <see cref="Outcome"/> its value; the shell opens the banner on it.</summary>
    public event Action<MatchOutcome>? Ended;

    /// <summary>The running simulation; null before <see cref="Start"/>.</summary>
    public Simulation? Simulation => _runner?.Simulation;

    /// <summary>The seats the match was started with (one per player); empty before <see cref="Start"/>.</summary>
    /// <remarks>M6-V9: the seats as played (<see cref="SimRunner.PlayedSeats"/>), so a replay's AI seats show as AI though its sim runs them human.</remarks>
    public System.Collections.Immutable.ImmutableArray<PlayerSeat> Seats => _runner?.PlayedSeats ?? System.Collections.Immutable.ImmutableArray<PlayerSeat>.Empty;

    /// <summary>
    /// The match clock (<c>TickNumber</c>, ticks of 50 ms) when player <paramref name="player"/>'s defeat was seen: the end of the
    /// tick it fell in (<c>DefeatEvent.Tick</c> + 1); -1 while it stands or out of range. Presentation state for the stats screen.
    /// </summary>
    public int DefeatedAt(int player) => (uint)player < (uint)_defeatedAt.Length ? _defeatedAt[player] : -1;


    /// <summary>Army units per player this match asked for (<c>--units</c>; 0 from the menu and <c>--skirmish</c>, M6-V3).</summary>
    public int UnitsPerPlayer { get; private set; }

    /// <summary>Where the camera looked at the start (meters; the local army's centre, or with no army the local Town Hall).</summary>
    public System.Numerics.Vector2 StartFocus { get; private set; }

    /// <summary>Each player's colour (0xRRGGBB) every view draws it in (<see cref="PlayerColours"/>); empty before <see cref="Start"/>.</summary>
    public uint[] PlayerRgb { get; private set; } = Array.Empty<uint>();

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
        if (options.MapSize is int mapSize) _runner.MapSize = mapSize;
        _runner.Forests = options.Forests;
        _runner.GoldMines = options.Mines;
        _runner.Combat = !options.NoCombat;
        // M6-V5: --players seats that many players on as many generator start locations (free-for-all).
        if (options.Players is int players)
        {
            _runner.PlayerCount = players;
            _runner.StartLocations = players;
        }
        PickSeats(data, options);
        UnitsPerPlayer = options.UnitsPerPlayer;
        WorkersPerPlayer = options.NoBases ? 0 : options.Workers ?? data.Rules.StartingWorkers;
        // The start workers come on top of the armies (the bench keeps its 100 / 1,000 a side), so the store grows past 2,000 when needed.
        _runner.UnitCapacity = Math.Max(_runner.UnitCapacity, _runner.PlayerCount * (options.UnitsPerPlayer + WorkersPerPlayer));
        _runner.Start(data);
        Wire(data, options, null);
    }

    /// <summary>The file a replay match plays (M6-V9), or null for a live match.</summary>
    public string? ReplayPath { get; private set; }

    /// <summary>True for a replay playback match (M6-V9): the sim plays a recording and no order reaches it.</summary>
    public bool IsPlayback => _runner?.Playback != null;

    /// <summary>
    /// Starts a replay playback match (M6-V9): the runner plays <paramref name="replay"/> (<see cref="SimRunner.StartPlayback"/>;
    /// its refusal code is returned and nothing is wired), then the views are wired as for a live match, with no army or base
    /// spawned (the recording's own commands make them), the selection's orders off and the command card greyed. Of
    /// <paramref name="options"/> only the view's flags count (<c>--speed</c>, <c>--no-fog</c>, <c>--no-hud</c>, <c>--mute</c>, colours ...).
    /// </summary>
    public ReplayError StartReplay(GameData data, Replay replay, LaunchOptions options, string path)
    {
        if (options.Speed is double speed) _runner.GameSpeed = speed;
        ReplayError refused = _runner.StartPlayback(data, replay);
        if (refused != ReplayError.None) return refused;
        ReplayPath = path;
        UnitsPerPlayer = 0;
        WorkersPerPlayer = 0;
        Wire(data, options, replay);
        return ReplayError.None;
    }

    // The views, HUD and input wired to the runner's new sim; a live match (replay null) also enqueues its armies and bases.
    private void Wire(GameData data, LaunchOptions options, Replay? replay)
    {
        bool live = replay == null;
        Simulation sim = _runner.Simulation!;
        _defeatedAt = new int[sim.World.Config.PlayerCount];
        Array.Fill(_defeatedAt, -1);
        _runner.Ticked += OnTicked;

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

        uint[] playerRgb = PlayerRgb = PlayerColours(data, sim.World, options, UiText.Shared);
        var units = GetNode<UnitViews>("World3D/UnitViews");
        units.Bind(data, sim.World.Units.Capacity, playerRgb);
        units.Runner = _runner;
        units.Fog = fog;
        units.Camera = camera;

        var buildings = GetNode<BuildingViews>("World3D/BuildingViews");
        buildings.Bind(data, sim.World.Buildings.Capacity, playerRgb);
        buildings.Runner = _runner;
        buildings.Fog = fog;
        var combat = GetNode<CombatViews>("World3D/CombatViews");
        combat.Fog = fog;
        combat.MaxHp = units.MaxHp; // one max-hp fill a frame for the bars and the hit flash (BUG-0490)
        combat.Bind(data, sim.World.Units.Capacity, playerRgb);
        combat.Runner = _runner;
        combat.Camera = camera;
        var shots = GetNode<ProjectileViews>("World3D/ProjectileViews");
        shots.Bind(data, sim.World.Projectiles.Capacity, playerRgb);
        shots.Runner = _runner;
        shots.Fog = fog;

        Sfx.SetMuted(options.Mute);
        var selection = GetNode<SelectionController>("SelectionController");
        selection.OrdersEnabled = live; // M6-V9: no order of any kind reaches a replay's sim
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
        abilityViews.Bind(data, sim.World.Units.Capacity, _runner, selection, playerRgb: playerRgb, zoneCapacity: sim.World.Zones.Capacity);
        abilityViews.Sound = GetNode<Sfx>("Sfx"); // M5-V1: the lost-cast sound

        // M6-V3: no army (the menu's match, --skirmish, --units 0) spawns none; the bases are planned exactly as with one
        // (StartBase.Plan stands a one-unit block in for an empty one).
        System.Numerics.Vector2[][] blocks = Array.Empty<System.Numerics.Vector2[]>();
        System.Numerics.Vector2 focus;
        if (!live) focus = ReplayFocus(sim, replay!); // M6-V9: the recording's commands spawn everything
        else if (options.UnitsPerPlayer > 0) blocks = SpawnArmies(sim, options.UnitsPerPlayer, out focus);
        else
        {
            blocks = new System.Numerics.Vector2[sim.World.Config.PlayerCount][]; // M6-V5: a base per player (it was two at most)
            for (int p = 0; p < blocks.Length; p++) blocks[p] = Array.Empty<System.Numerics.Vector2>();
            focus = new System.Numerics.Vector2(sim.World.NavGrid.Width, sim.World.NavGrid.Height) * MapConstants.CellSize / 2;
        }
        if (live)
        {
            Bases = options.NoBases ? null : SpawnBases(sim, blocks, WorkersPerPlayer, out _);
            // No army: look at player 0's Town Hall instead of the map centre.
            if (options.UnitsPerPlayer == 0 && Bases != null && Bases.HallAnchor[0] >= 0)
                focus = StartBase.FootprintCenter(sim.World.NavGrid, data.Buildings[Bases.HallType[0]], Bases.HallAnchor[0]);
        }
        camera.SetFocus(focus.X, focus.Y);
        StartFocus = camera.Focus;
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
            // M5-V1: the words over a lost cast's caster are HUD text (none without the HUD or ui.json).
            abilityViews.CastLostText = ui?.Hud(HudText.CastLost) ?? "";
            hud.GetNode<ResourceBar>("ResourceBar").Init(_runner, SelectionController.LocalPlayer, local.GoldName, local.WoodName, ui?.Hud(HudText.Pop) ?? "",
                ui?.Hud(HudText.Kills) ?? "", ui?.Hud(HudText.Losses) ?? "");
            // The card's and panel's labels and menus are view data (ui.json); without them they stay hidden and the errors are logged.
            var card = hud.GetNode<CommandCard>("CommandCard");
            var panel = hud.GetNode<SelectionPanel>("SelectionPanel");
            if (ui != null)
            {
                var ghost = GetNode<BuildGhost>("World3D/BuildGhost");
                ghost.Init(_runner, camera, ui, SelectionController.LocalPlayer);
                card.ReadOnly = !live; // M6-V9: the card follows the selection, greyed
                card.Init(_runner, selection, ghost, ui);
                panel.Init(_runner, selection, ui);
            }
            else
            {
                card.Visible = false;
                panel.Visible = false;
            }
            hud.GetNode<ProductionQueueStrip>("QueueStrip").Init(_runner, selection);
            // BUG-0830: a replay is marked on screen (load.playing), so playback doesn't pass for a match ignoring its orders.
            if (!live && ui != null) hud.AddChild(ReplayMark(ui.LoadGame(LoadText.Playing)));
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
        if (!live)
        {
            GD.Print($"Replay started: {ReplayPath}, seed {replay!.Seed}, map {map.Width} x {map.Height}, {replay.TickCount} ticks, " +
                $"{replay.Commands.Length} commands, {replay.Checkpoints.Length} checkpoints, seats {SeatsText(data, _runner.PlayedSeats)}");
            return;
        }
        GD.Print($"Match started: seed {unchecked((ulong)_runner.Seed)}, map {map.Width} x {map.Height}, " +
            $"speed {_runner.GameSpeed:0.##}x, {options.UnitsPerPlayer} units per player, " +
            $"forests {placed.Forests} trees {placed.Trees} mines {placed.Mines}, " +
            $"town halls {Halls(Bases)}, {WorkersPerPlayer} workers per player" + (options.NoCombat ? ", combat off" : "") + (options.NoFog ? ", fog not drawn" : "") +
            $", seats {SeatsText(data, _runner.Seats)}");
    }

    /// <summary>
    /// Where a replay's camera starts (M6-V9): the footprint centre of player 0's first logged <c>SpawnBuilding</c> (its start
    /// Town Hall, as a live match looks at it), else the map centre. Read from the log: the hall stands only after tick 1.
    /// </summary>
    public static System.Numerics.Vector2 ReplayFocus(Simulation sim, Replay replay)
    {
        NavGrid g = sim.World.NavGrid;
        foreach (Command c in replay.Commands)
        {
            if (c.Kind != CommandKind.SpawnBuilding || c.Player != SelectionController.LocalPlayer || (uint)c.TypeId >= (uint)sim.World.Data.Buildings.Length) continue;
            int x = (int)(c.Position.X / MapConstants.CellSize), y = (int)(c.Position.Y / MapConstants.CellSize);
            if (!g.InBounds(x, y)) break;
            return StartBase.FootprintCenter(g, sim.World.Data.Buildings[c.TypeId], y * g.Width + x);
        }
        return new System.Numerics.Vector2(g.Width, g.Height) * MapConstants.CellSize / 2;
    }

    /// <summary>
    /// Each player's colour (M6-V3, docs/02 "Game flow" 2): slot p's <c>--colour</c> pick (the setup's colour list), else
    /// colour p, from <paramref name="ui"/>'s table (<see cref="UiText.ColourRgb"/>); players past the table wrap round it.
    /// Without <c>ui.json</c> a player takes its seat's faction's <c>PrimaryColor</c>, as before M6-V3. The one colour source:
    /// every view (units, buildings, ghosts, corpses, shots, zone rims, minimap dots) is bound with this array.
    /// </summary>
    public static uint[] PlayerColours(GameData data, World world, LaunchOptions options, UiText? ui)
    {
        var rgb = new uint[world.Config.PlayerCount];
        for (int p = 0; p < rgb.Length; p++)
            rgb[p] = ui != null ? ui.ColourRgb(options.ColourIndex(p) ?? p % UiText.ColourCount) : data.Factions[world.FactionOf(p)].PrimaryColor;
        return rgb;
    }

    // M6-V4: after every tick (several a frame at speed), read-only. Each DefeatEvent prints the dev line and stamps the stats'
    // defeat time; then the local result, latched: Draw when the match is over and nobody stands (the last ones fell on one
    // tick), else Defeat the moment the local player is defeated (others may stand on), else Victory when the match is over.
    private void OnTicked(Simulation sim)
    {
        World w = sim.World;
        foreach (DefeatEvent e in w.DefeatEvents)
        {
            if ((uint)e.Player < (uint)_defeatedAt.Length) _defeatedAt[e.Player] = e.Tick + 1;
            MatchOverLines++;
            GD.Print($"Match over: player {e.Player} is defeated (tick {sim.TickNumber})"); // the sim's rule decides why (M5-4b, M5-4e)
        }
        if (Outcome != MatchOutcome.None) return;
        MatchOutcome now = ResultFor(w, SelectionController.LocalPlayer);
        if (now == MatchOutcome.None) return;
        Outcome = now;
        Ended?.Invoke(now);
    }

    /// <summary>The local result the sim's state gives now (<see cref="Outcome"/>'s rule, unlatched): Draw, Defeat, Victory or None.</summary>
    public static MatchOutcome ResultFor(World w, int local)
    {
        bool over = w.MatchOver;
        if (over && Standing(w) == 0) return MatchOutcome.Draw;
        if (w.IsDefeated(local)) return MatchOutcome.Defeat;
        return over ? MatchOutcome.Victory : MatchOutcome.None;
    }

    // Players that ever built and still stand (the sim's MatchOver counts the same ones).
    private static int Standing(World w)
    {
        int n = 0;
        for (int p = 0; p < w.Config.PlayerCount; p++)
            if (w.EverBuilt(p) && !w.IsDefeated(p)) n++;
        return n;
    }

    // M6-V2: the setup's (or --faction / --ai's) picks into the runner's seats. An id the data lacks, or an AI seat whose faction
    // has no ai.json (SimConfig.Validate would throw), is refused with a warning like any bad flag value; the match still starts.
    // M6-V5: every slot (2-4), one AI difficulty per slot past the first.
    private void PickSeats(GameData data, LaunchOptions options)
    {
        for (int slot = 0; slot < LaunchOptions.SlotCount; slot++)
        {
            _runner.SlotFactions[slot] = -1;
            if (options.FactionId(slot) is not string id) continue;
            int faction = data.FindFaction(id);
            if (faction >= 0) _runner.SlotFactions[slot] = faction;
            else GD.PushWarning($"Ignoring --faction with bad or missing value '{slot + 1} {id}'.");
        }
        for (int slot = 0; slot < LaunchOptions.SlotCount; slot++)
        {
            AiDifficulty? ai = _runner.SlotAi[slot] = options.AiOf(slot);
            if (ai == null || slot >= _runner.PlayerCount) continue;
            int f = _runner.SlotFactions[slot] >= 0 ? _runner.SlotFactions[slot] : data.Factions.Length > 0 ? slot % data.Factions.Length : -1;
            if (f < 0 || f >= data.Ai.Length || data.Ai[f] == null)
            {
                GD.PushWarning($"Ignoring --ai: faction {f} has no ai.json; player {slot} stays human.");
                _runner.SlotAi[slot] = null;
            }
        }
    }

    /// <summary>Enqueues each player's start army in its <see cref="StartLayout"/> block; returns each player's block, and player 0's block centre (meters) in <paramref name="focus"/>.</summary>
    /// <remarks>
    /// Each player spawns its seat's faction's roster (<c>World.FactionOf</c>, M6-V2) round-robin. The units appear on the sim's next tick.
    /// </remarks>
    private static System.Numerics.Vector2[][] SpawnArmies(Simulation sim, int perPlayer, out System.Numerics.Vector2 focus)
    {
        GameData data = sim.World.Data;
        NavGrid grid = sim.World.NavGrid;
        focus = new System.Numerics.Vector2(grid.Width, grid.Height) * MapConstants.CellSize / 2;
        int players = sim.World.Config.PlayerCount;
        var blocks = new System.Numerics.Vector2[players][];
        for (int p = 0; p < players; p++)
        {
            // Two dev start blocks, west and east; a third or fourth player (M6-V5) gets its base and no dev army.
            if (p >= 2)
            {
                blocks[p] = Array.Empty<System.Numerics.Vector2>();
                GD.PushWarning($"Player {p}: the dev start army has two blocks (west and east); no army past two players.");
                continue;
            }
            FactionDef faction = data.Factions[sim.World.FactionOf(p)];
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

    // The playback mark (BUG-0830): a label at the top centre of the HUD, clear of the resource bar (top right) and the debug label.
    private static Label ReplayMark(string text)
    {
        var mark = new Label { Name = "ReplayMark", Text = text, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        mark.AddThemeFontSizeOverride("font_size", 22);
        mark.AddThemeColorOverride("font_color", new Color(1f, 0.93f, 0.75f));
        mark.AddThemeColorOverride("font_outline_color", Colors.Black);
        mark.AddThemeConstantOverride("outline_size", 4);
        mark.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        mark.OffsetLeft = -120f;
        mark.OffsetRight = 120f;
        mark.OffsetTop = 8f;
        mark.OffsetBottom = 40f;
        return mark;
    }

    // "malazan human, whirlwind ai hard": the dev log's seat list (data ids and the replay header's words, not player-facing text).
    private static string SeatsText(GameData data, System.Collections.Immutable.ImmutableArray<PlayerSeat> seats)
    {
        var sb = new System.Text.StringBuilder();
        foreach (PlayerSeat seat in seats)
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(seat.Faction >= 0 ? data.Factions[seat.Faction].Key : "-");
            if (seat.Controller == SeatController.Ai) sb.Append(" ai ").Append(DataLimits.AiDifficultyIds[(int)seat.Difficulty]);
            else sb.Append(" human");
        }
        return sb.ToString();
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
