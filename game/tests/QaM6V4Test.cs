using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// QA M6-V4 (2026-10-13-1616): attacks on the match-over banner and the stats screen the dev scene doesn't make, through the
/// shell (<c>Main.tscn</c>, dev matches <c>--units 0 --no-bases --speed 8</c>, set up by commands):
/// <list type="bullet">
/// <item>Orders on the defeat tick: the local player keeps ordering its worker every frame and from the Ticked handler of the
/// tick it falls in. No exception; after Continue the worker's Move orders (selection and right-click) are dropped (it
/// doesn't move), the build ghost reads <c>placement.defeated</c>, a ghost click places nothing.</item>
/// <item>Three seats falling together (sites cancelled on one tick): 0 + 1 (2 stands) → Defeat, 1 + 2 → Victory, all three →
/// Draw; one banner each, the dev line count equal to the <c>DefeatEvents</c> seen, the stats' defeat cells equal.</item>
/// <item>Banner vs pause menu: the pause menu up while the killing blow is pending holds the tick and shows no banner; Resume →
/// the banner with the pause menu hidden; clicks on the HUD area under the banner issue nothing; Continue, then the pause
/// menu's Settings and back (no main menu over the match, Stats still offered); the stats after Continue follow the sim
/// (a later kill and the longer clock).</item>
/// <item>The setup screen's Start after a banner match: no banner, no stats, the pause menu without Stats.</item>
/// </list>
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6V4Test.tscn</c>; prints "QA M6-V4 TEST PASS" or "QA M6-V4 TEST FAIL: ..." lines.</remarks>
public partial class QaM6V4Test : Node
{
    private static readonly string[] Args = { "--units", "0", "--no-bases", "--mute", "--seed", "3", "--speed", "8" };

    private readonly List<string> _failures = new();
    private readonly List<DefeatEvent> _events = new();
    private GameData _data = null!;
    private UiText _ui = null!;
    private Main _main = null!;
    private int _ended;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            await Frames(1);
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            _ui = UiText.Shared ?? throw new InvalidOperationException("ui.json failed to load");
            _main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
            _main.UserArgs = Args;
            _main.SettingsPath = $"user://qa-m6v4-{OS.GetProcessId()}/settings.cfg";
            AddChild(_main);
            await Frames(2);
            await OrdersOnTheDefeatTick();
            await TogetherInThreeSeats(new[] { 0, 1 }, MatchOutcome.Defeat, "0+1 fall, 2 stands");
            await TogetherInThreeSeats(new[] { 1, 2 }, MatchOutcome.Victory, "1+2 fall, 0 stands");
            await TogetherInThreeSeats(new[] { 0, 1, 2 }, MatchOutcome.Draw, "all three fall");
            await BannerVersusPause();
            await SetupStartAfterABanner();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        foreach (string f in _failures.Take(60)) GD.Print($"QA M6-V4 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M6-V4 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- the attacks ----

    private async Task OrdersOnTheDefeatTick()
    {
        _main.StartSkirmish(Args);
        Match match = Watch(_main.CurrentMatch);
        Simulation sim = match.Simulation!;
        World w = sim.World;
        var selection = match.GetNode<SelectionController>("SelectionController");
        await Bases(sim, 2);
        EntityHandle worker = UnitsOf(w, 0)[0];
        selection.Selection.Clear();
        selection.Selection.Add(worker);
        System.Numerics.Vector2 home = w.Units.Position[worker.Index];
        var far = new Vector2(home.X, Math.Min(home.Y + 30f, w.NavGrid.CellCenter(0, w.NavGrid.Height - 1).Y - 4f));
        var here = new Vector2(home.X, home.Y);
        // From the Ticked handler of the tick player 0 falls in: an order sent "the same tick it dies".
        int onTheTick = 0;
        match.GetNode<SimRunner>("SimRunner").Ticked += s =>
        {
            foreach (DefeatEvent e in s.World.DefeatEvents)
                if (e.Player == 0)
                {
                    selection.Order(CommandKind.Move, far, queued: false);
                    onTheTick++;
                }
        };
        EntityHandle own = BuildingOf(w, 0);
        await Army(sim, 1, own, 6);
        Raze(sim, 1, own);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!_main.BannerUp && clock.Elapsed.TotalSeconds < 90)
        {
            selection.Order(CommandKind.Move, here, queued: false); // a click every frame up to the defeat: stays put
            await Frames(1);
        }
        Check(_main.BannerUp && match.Outcome == MatchOutcome.Defeat && onTheTick == 1 && match.MatchOverLines == _events.Count && _events.Count == 1,
            $"defeat-tick orders: banner {_main.BannerUp}, outcome {match.Outcome}, on-the-tick orders {onTheTick}, lines {match.MatchOverLines}, events {_events.Count}");
        await Click(_main.Banner!.ContinueButton);
        Check(!GetTree().Paused && !_main.Banner.Visible, "Continue after Defeat did not resume");
        System.Numerics.Vector2 at = w.Units.Position[worker.Index];
        // Orders after the defeat: every one is dropped by the sim (the worker stays where it was).
        for (int i = 0; i < 5; i++)
        {
            selection.Order(CommandKind.Move, far, queued: false);
            await Ticks(sim, 4);
        }
        var camera = match.GetNode<RtsCamera>("RtsCamera");
        Vector2 screen = GetViewport().GetVisibleRect().Size / 2;
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = screen, GlobalPosition = screen });
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = screen, GlobalPosition = screen });
        await Ticks(sim, 30);
        bool alive = w.Units.Alive[worker.Index] && w.Units.Generation[worker.Index] == worker.Generation;
        float moved = System.Numerics.Vector2.Distance(at, w.Units.Position[worker.Index]);
        Check(!alive || moved < 0.5f, $"a defeated player's worker moved {moved:0.00} m after its orders (want them dropped)");
        // The ghost over the worker's explored ground reads "Defeated"; a click there places nothing.
        var card = match.GetNode<CommandCard>("Hud/CommandCard");
        int house = StartBase.BuildingOfSlot(_data, w.FactionOf(0), BuildingSlot.House);
        NavGrid g = w.NavGrid;
        BuildingDef def = _data.Buildings[house];
        int spot = -1;
        w.NavGrid.WorldToCell(at, out int wx, out int wy);
        for (int r = 3; r < 30 && spot < 0; r++)
            for (int dy = -r; dy <= r && spot < 0; dy++)
                for (int dx = -r; dx <= r && spot < 0; dx++)
                {
                    int x = wx + dx, y = wy + dy;
                    if (x < 0 || y < 0 || x + def.FootprintWidth > g.Width || y + def.FootprintHeight > g.Height) continue;
                    if (!w.CanPlace(0, house, y * g.Width + x, out PlacementError why) && why == PlacementError.Defeated) spot = y * g.Width + x;
                }
        Check(spot >= 0, "no anchor near the defeated worker where only 'Defeated' refuses");
        System.Numerics.Vector2 a0 = g.CellCenter(spot % g.Width, spot / g.Width), a1 = g.CellCenter(spot % g.Width + def.FootprintWidth / 2, spot / g.Width + def.FootprintHeight / 2);
        camera.SetFocus(a1.X, a1.Y);
        await Frames(3);
        Camera3D cam = GetViewport().GetCamera3D();
        screen = cam.UnprojectPosition(new Vector3(a1.X, TerrainHeight.At(w.Heightmap, a1.X, a1.Y), a1.Y));
        card.Ghost.ScreenOverride = screen;
        card.Ghost.Begin(house);
        await Frames(3);
        card.Ghost.Sync();
        Check(card.Ghost.Reason == PlacementError.Defeated && card.Ghost.ShownText == _ui.PlacementText(PlacementError.Defeated),
            $"defeated ghost: reason {card.Ghost.Reason}, text '{card.Ghost.ShownText}' (want '{_ui.PlacementText(PlacementError.Defeated)}')");
        int buildings = w.Buildings.Count;
        card.GhostClick(false, screen);
        await Ticks(sim, 6);
        Check(w.Buildings.Count == buildings, $"a defeated player's ghost click placed a building ({buildings} -> {w.Buildings.Count})");
        card.Ghost.End();
        card.Ghost.ScreenOverride = null;
        Check(_ended == 1 && !_main.Banner.Visible, $"defeat-tick: ended {_ended}, banner {_main.Banner.Visible}");
        await QuitMatch();
    }

    private async Task TogetherInThreeSeats(int[] fall, MatchOutcome want, string how)
    {
        _main.BeforeStart = m => m.GetNode<SimRunner>("SimRunner").PlayerCount = 3;
        _main.StartSkirmish(Args);
        _main.BeforeStart = null;
        Match match = Watch(_main.CurrentMatch);
        Simulation sim = match.Simulation!;
        World w = sim.World;
        NavGrid g = w.NavGrid;
        await Scouts(sim, 3);
        var sites = new System.Numerics.Vector2[3];
        for (int p = 0; p < 3; p++)
        {
            int house = StartBase.BuildingOfSlot(_data, w.FactionOf(p), BuildingSlot.House);
            int anchor = Anchor(w, p, house, Spots(g)[p].X + 3, Spots(g)[p].Y + 3);
            sites[p] = g.CellCenter(anchor % g.Width, anchor / g.Width);
            sim.Enqueue(Command.Build(p, UnitsOf(w, p)[0], house, sites[p]));
        }
        await Ticks(sim, 3);
        Check(w.Buildings.Count == 3, $"{how}: {w.Buildings.Count} sites (want 3)");
        foreach (int p in fall) sim.Enqueue(Command.Cancel(p, sites[p]));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!_main.BannerUp && clock.Elapsed.TotalSeconds < 30) await Frames(1);
        MatchOverBanner banner = _main.Banner!;
        string title = want switch { MatchOutcome.Victory => _ui.MatchOver(MatchOverText.Victory), MatchOutcome.Draw => _ui.MatchOver(MatchOverText.Draw), _ => _ui.MatchOver(MatchOverText.Defeat) };
        Check(_main.BannerUp && match.Outcome == want && banner.Shown == want && banner.Title.Text == title && sim.MatchOver == (fall.Length >= 2),
            $"{how}: banner {_main.BannerUp}, outcome {match.Outcome} (want {want}), title '{banner.Title.Text}', MatchOver {sim.MatchOver}");
        Check(_events.Count == fall.Length && _events.All(e => e.Tick == _events[0].Tick) && match.MatchOverLines == _events.Count && _ended == 1,
            $"{how}: events [{string.Join(", ", _events)}], lines {match.MatchOverLines}, ended {_ended}");
        Check(sim.TickNumber == _events[0].Tick + 1, $"{how}: banner at tick {sim.TickNumber}, defeat on {_events[0].Tick}");
        if (_main.BannerUp)
        {
            await Click(banner.StatsButton);
            MatchStats stats = _main.Stats!;
            int col = Array.FindIndex(MatchStats.Columns, c => c.Label == MatchOverText.DefeatedAt);
            for (int p = 0; p < 3; p++)
            {
                string cell = stats.Cell(p, col);
                string expect = fall.Contains(p) ? _ui.Clock(_events[0].Tick + 1) : _ui.MatchOver(MatchOverText.Standing);
                Check(cell == expect, $"{how}: row {p} defeated '{cell}' (want '{expect}')");
            }
            Check(stats.RowCount == 3, $"{how}: {stats.RowCount} stats rows");
            await Click(stats.BackButton);
            await Click(banner.ContinueButton);
            await Ticks(sim, 20);
            Check(_ended == 1 && !banner.Visible && match.MatchOverLines == fall.Length, $"{how}: after Continue ended {_ended}, banner {banner.Visible}, lines {match.MatchOverLines}");
        }
        await QuitMatch();
    }

    private async Task BannerVersusPause()
    {
        _main.StartSkirmish(Args);
        Match match = Watch(_main.CurrentMatch);
        Simulation sim = match.Simulation!;
        World w = sim.World;
        var selection = match.GetNode<SelectionController>("SelectionController");
        await Bases(sim, 2);
        EntityHandle target = BuildingOf(w, 1);
        await Army(sim, 0, target, 6);
        Raze(sim, 0, target);
        // Pause at once: the attack is pending, nothing may tick and no banner may open under the pause menu.
        await Type(Key.Escape);
        int pausedAt = sim.TickNumber;
        await Seconds(0.5);
        Check(_main.Paused && !_main.BannerUp && sim.TickNumber == pausedAt, $"pause menu first: paused {_main.Paused}, banner {_main.BannerUp}, ticks {pausedAt} -> {sim.TickNumber}");
        await Click(_main.Pause!.ResumeButton);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!_main.BannerUp && clock.Elapsed.TotalSeconds < 90) await Frames(1);
        Check(_main.BannerUp && !_main.Pause.Visible && GetTree().Paused && match.Outcome == MatchOutcome.Victory,
            $"after Resume: banner {_main.BannerUp}, pause menu {_main.Pause.Visible}, tree paused {GetTree().Paused}, outcome {match.Outcome}");
        int bannerTick = sim.TickNumber;
        int bannerKills = w.Kills[0];
        // Clicks on the HUD area under the banner: no order, no selection change.
        int moves = selection.IssuedCount(CommandKind.Move), attackMoves = selection.IssuedCount(CommandKind.AttackMove), selected = selection.Selection.Count;
        Vector2 size = GetViewport().GetVisibleRect().Size;
        foreach (Vector2 at in new[] { new Vector2(90f, size.Y - 90f), new Vector2(size.X - 150f, size.Y - 80f), new Vector2(size.X * 0.8f, size.Y * 0.3f) }) // clear of the banner's centred buttons
        {
            await ClickAt(at, MouseButton.Left);
            await ClickAt(at, MouseButton.Right);
        }
        Check(selection.IssuedCount(CommandKind.Move) == moves && selection.IssuedCount(CommandKind.AttackMove) == attackMoves && selection.Selection.Count == selected
            && _main.BannerUp && !_main.Stats!.Visible && sim.TickNumber == bannerTick,
            $"clicks under the banner: moves {moves} -> {selection.IssuedCount(CommandKind.Move)}, selected {selected} -> {selection.Selection.Count}, banner {_main.BannerUp}");
        // Continue, then the pause menu's Settings and back.
        await Click(_main.Banner!.ContinueButton);
        await Type(Key.Escape);
        PauseMenu pause = _main.Pause;
        var card = match.GetNode<CommandCard>("Hud/CommandCard");
        Check(_main.Paused && pause.StatsButton.Visible, $"pause after Continue: paused {_main.Paused}, Stats {pause.StatsButton.Visible}; targeting {selection.Targeting} ({selection.TargetKind}), menu {card.MenuOpen}, ghost {card.GhostActive}, selected {selection.Selection.Count}");
        await Click(pause.SettingsButton);
        Check(_main.SettingsScreen is { Visible: true }, "the pause menu's Settings did not open after a banner");
        await Type(Key.Escape);
        Check(_main.SettingsScreen is { Visible: false } && _main.Paused && pause.StatsButton.Visible && _main.Menu is not { Visible: true } && !_main.BannerUp && GetTree().Paused,
            $"settings Esc after a banner: settings {_main.SettingsScreen?.Visible}, pause {_main.Paused}, Stats {pause.StatsButton.Visible}, menu {_main.Menu?.Visible}, banner {_main.BannerUp}");
        await Click(pause.SettingsButton);
        await Click(_main.SettingsScreen!.BackButton);
        Check(_main.SettingsScreen is { Visible: false } && _main.Paused && _main.Menu is not { Visible: true } && _main.CurrentMatch == match,
            $"settings Back after a banner: settings {_main.SettingsScreen.Visible}, pause {_main.Paused}, menu {_main.Menu?.Visible}");
        await Click(pause.ResumeButton);
        // The stats after Continue follow the sim: player 0's fresh shock units kill player 1's scout (a defeated player's
        // units stay), the clock moves on.
        List<EntityHandle> theirs = UnitsOf(w, 1);
        Check(theirs.Count > 0, "player 1 has no unit left to kill");
        if (theirs.Count > 0)
        {
            System.Numerics.Vector2 by = w.Units.Position[theirs[0].Index];
            int shock = StartBase.UnitOfSlot(_data, w.FactionOf(0), UnitSlot.Shock);
            for (int i = 0; i < 4; i++)
            {
                int c = FlowField.NearestPassable(w.NavGrid, w.NavGrid.WorldToCell(by + new System.Numerics.Vector2(3f, i - 1.5f), out int cx, out int cy) ? cy * w.NavGrid.Width + cx : 0);
                sim.Enqueue(Command.SpawnUnit(0, shock, w.NavGrid.CellCenter(c % w.NavGrid.Width, c / w.NavGrid.Width)));
            }
            await Ticks(sim, 2);
            foreach (EntityHandle u in UnitsOf(w, 0))
                if (w.Units.TypeId[u.Index] == shock) sim.Enqueue(Command.Attack(0, u, theirs[0], isBuilding: false));
        }
        clock.Restart();
        while (w.Kills[0] == bannerKills && clock.Elapsed.TotalSeconds < 30) await Frames(1);
        await Type(Key.Escape);
        await Click(pause.StatsButton);
        MatchStats stats = _main.Stats!;
        int k = Array.FindIndex(MatchStats.Columns, c => c.Label == MatchOverText.Kills);
        Check(stats.Visible && w.Kills[0] > bannerKills && stats.Cell(0, k) == w.Kills[0].ToString() && sim.TickNumber > bannerTick
            && stats.Length.Text == _ui.MatchLength(sim.TickNumber),
            $"stats after Continue: kills cell {stats.Cell(0, k)} / span {w.Kills[0]} (banner {bannerKills}), length '{stats.Length.Text}' at tick {sim.TickNumber} (banner {bannerTick})");
        // No C# literal on the banner or the stats screen (the ShellTest rule for the setup, BUG-0581 item 1): ui.json, faction
        // data, numbers and the ui.json clock / length formats only.
        var allowed = new HashSet<string>(Enum.GetValues<MatchOverText>().Select(_ui.MatchOver)
            .Concat(new[] { SetupText.SlotHuman, SetupText.DifficultyEasy, SetupText.DifficultyNormal, SetupText.DifficultyHard }.Select(_ui.Setup))
            .Concat(_data.Factions.Select(f => f.DisplayName))
            .Concat(new[] { _ui.MatchLength(sim.TickNumber), _ui.Clock(match.DefeatedAt(0)), _ui.Clock(match.DefeatedAt(1)) })) { "" };
        foreach (Node root in new Node[] { stats, _main.Banner! })
            foreach (Node n in root.FindChildren("*", "", true, false))
            {
                string? text = n switch { Label l => l.Text, Button b => b.Text, _ => null };
                if (text != null) Check(allowed.Contains(text) || int.TryParse(text, out _), $"{root.Name} shows '{text}' ({n.Name}), not from ui.json or data");
            }
        await Click(stats.BackButton);
        Check(!stats.Visible && _main.Paused, $"stats Back to the pause menu: stats {stats.Visible}, pause {_main.Paused}");
        await Click(pause.QuitToMenuButton);
        await Frames(2);
        Check(_main.CurrentMatch == null && !GetTree().Paused && _main.Menu is { Visible: true }, "quit after the banner");
    }

    private async Task SetupStartAfterABanner()
    {
        _main.ShowSetup();
        await Frames(2);
        await Click(_main.Setup!.StartButton);
        await Frames(10);
        Match? match = _main.CurrentMatch;
        Check(match != null && !_main.BannerUp && !_main.Banner!.Visible && !_main.Stats!.Visible && !GetTree().Paused && match.Outcome == MatchOutcome.None,
            $"setup Start after a banner: match {match != null}, banner {_main.Banner?.Visible}, stats {_main.Stats?.Visible}, paused {GetTree().Paused}, outcome {match?.Outcome}");
        if (match == null) return;
        await Ticks(match.Simulation!, 10);
        await Type(Key.Escape);
        Check(_main.Paused && !_main.Pause!.StatsButton.Visible, $"new match's pause menu: paused {_main.Paused}, Stats {_main.Pause?.StatsButton.Visible} (want hidden)");
        await Click(_main.Pause!.QuitToMenuButton);
        await Frames(2);
        Check(_main.CurrentMatch == null && !GetTree().Paused, "quit from the setup's match");
    }

    // ---- scene set-up (the dev scene's pattern, commands only) ----

    private Match Watch(Match? match)
    {
        if (match?.Simulation == null) throw new InvalidOperationException("the shell started no match");
        _events.Clear();
        _ended = 0;
        match.Ended += _ => _ended++;
        match.GetNode<SimRunner>("SimRunner").Ticked += s => { foreach (DefeatEvent e in s.World.DefeatEvents) _events.Add(e); };
        match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
        return match;
    }

    private async Task Bases(Simulation sim, int players)
    {
        World w = sim.World;
        NavGrid g = w.NavGrid;
        await Scouts(sim, players);
        for (int p = 0; p < players; p++)
        {
            int house = StartBase.BuildingOfSlot(_data, w.FactionOf(p), BuildingSlot.House);
            int anchor = Anchor(w, p, house, Spots(g)[p].X, Spots(g)[p].Y);
            sim.Enqueue(Command.SpawnBuilding(p, house, g.CellCenter(anchor % g.Width, anchor / g.Width)));
        }
        await Ticks(sim, 3);
        Check(w.Buildings.Count == players, $"{w.Buildings.Count} buildings placed (want {players})");
    }

    private static (int X, int Y)[] Spots(NavGrid g) => new[] { (g.Width / 4, g.Height / 2), (g.Width * 3 / 4, g.Height / 2), (g.Width / 2, g.Height / 5) };

    private async Task Scouts(Simulation sim, int players)
    {
        World w = sim.World;
        NavGrid g = w.NavGrid;
        for (int p = 0; p < players; p++)
        {
            int c = FlowField.NearestPassable(g, Math.Min(Spots(g)[p].Y + 7, g.Height - 1) * g.Width + Spots(g)[p].X);
            sim.Enqueue(Command.SpawnUnit(p, StartBase.UnitOfSlot(_data, w.FactionOf(p), UnitSlot.Worker), g.CellCenter(c % g.Width, c / g.Width)));
        }
        await Ticks(sim, 6);
    }

    private async Task Army(Simulation sim, int player, EntityHandle target, int n)
    {
        World w = sim.World;
        NavGrid g = w.NavGrid;
        int line = StartBase.UnitOfSlot(_data, w.FactionOf(player), UnitSlot.Siege);
        int cell = w.Buildings.Cell[target.Index];
        int cx = cell % g.Width, cy = cell / g.Width;
        for (int i = 0; i < n; i++)
        {
            int x = Math.Clamp(cx - 3 + i % 5 * 2, 0, g.Width - 1), y = Math.Clamp(cy - 4 - i / 5 * 2, 0, g.Height - 1);
            int c = FlowField.NearestPassable(g, y * g.Width + x);
            sim.Enqueue(Command.SpawnUnit(player, line, g.CellCenter(c % g.Width, c / g.Width)));
        }
        await Ticks(sim, 3);
    }

    private static void Raze(Simulation sim, int player, EntityHandle target)
    {
        foreach (EntityHandle u in UnitsOf(sim.World, player)) sim.Enqueue(Command.Attack(player, u, target, isBuilding: true));
    }

    private static List<EntityHandle> UnitsOf(World w, int player)
    {
        var list = new List<EntityHandle>();
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.Owner[i] == player) list.Add(new EntityHandle(i, u.Generation[i]));
        return list;
    }

    private static EntityHandle BuildingOf(World w, int player)
    {
        BuildingStore b = w.Buildings;
        for (int i = 0; i < b.Capacity; i++) if (b.Alive[i] && b.Owner[i] == player) return new EntityHandle(i, b.Generation[i]);
        throw new InvalidOperationException($"player {player} has no building");
    }

    private static int Anchor(World w, int player, int type, int x0, int y0)
    {
        NavGrid g = w.NavGrid;
        for (int r = 0; r < 30; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    int x = x0 + dx, y = y0 + dy;
                    if (x >= 0 && y >= 0 && x < g.Width && y < g.Height && w.CanPlace(player, type, y * g.Width + x, out _)) return y * g.Width + x;
                }
        throw new InvalidOperationException($"no anchor for player {player} near ({x0}, {y0})");
    }

    private async Task QuitMatch()
    {
        if (_main.BannerUp) await Click(_main.Banner!.MenuButton);
        else
        {
            _main.PauseMatch();
            await Frames(2); // the column lays out again when Stats first shows
            await Click(_main.Pause!.QuitToMenuButton);
        }
        await Frames(2);
        Check(_main.CurrentMatch == null && !GetTree().Paused, $"quit to menu: match {_main.CurrentMatch != null}, paused {GetTree().Paused}, pause menu {_main.Pause?.Visible}, stats {_main.Stats?.Visible}, banner {_main.Banner?.Visible}");
    }

    private async Task Ticks(Simulation sim, int n)
    {
        int until = sim.TickNumber + n;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (sim.TickNumber < until && clock.Elapsed.TotalSeconds < 30) await Frames(1);
        Check(sim.TickNumber >= until, $"tick {sim.TickNumber} after 30 s (want {until})");
    }

    // ---- input ----

    private async Task Click(Control c)
    {
        Check(c.IsVisibleInTree(), $"{c.Name} is not visible");
        await ClickAt(c.GetGlobalRect().GetCenter(), MouseButton.Left);
    }

    private async Task ClickAt(Vector2 at, MouseButton button)
    {
        GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        await Frames(1);
        MouseButtonMask mask = button == MouseButton.Left ? MouseButtonMask.Left : MouseButtonMask.Right;
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = button, Pressed = true, Position = at, GlobalPosition = at, ButtonMask = mask });
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = button, Pressed = false, Position = at, GlobalPosition = at });
        await Frames(2);
    }

    private async Task Type(Key key)
    {
        GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
        await Frames(2);
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Seconds(double seconds)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < seconds) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
