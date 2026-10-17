using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// M6-V5: the match-over paths of a free-for-all with 3 and 4 seats, through the shell (<c>Main.tscn</c>) started with
/// <c>--players</c> (a dev match: <c>--units 0 --no-bases</c>, every seat Malazan), staged with commands only as the M6-V4
/// scenes are (<see cref="DefeatStaging"/>: every loser's only building is a finished training hall and it has no unit).
/// <list type="bullet">
/// <item>Four seats, Victory: the map has four start locations; player 0 razes players 1, 2 and 3 one by one; no banner and
/// no result until the last falls, then "Victory" on that tick (the human is the last standing); the stats list the four
/// seats, player 0 Standing and each other with its own defeat time.</item>
/// <item>Four seats, Defeat first (slots 3 and 4 AI seats, Easy and Hard): player 1 razes player 0's hall; "Defeat" while
/// three seats stand (the sim's <c>MatchOver</c> false), the tree paused; Continue watching: the match runs on (ticks move,
/// the AIs think), no second banner; the stats (from the pause menu) list four rows: player 0's defeat time, the others
/// Standing, the AI rows by difficulty.</item>
/// <item>Three seats, the human last of the losers: player 2 razes player 1 (no banner), then player 0: "Defeat", the match
/// over now (one stands); three rows.</item>
/// </list>
/// </summary>
/// <remarks>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/FreeForAllTest.tscn</c>; prints "FREE FOR ALL TEST PASS"
/// or "FREE FOR ALL TEST FAIL: ..." lines. The settings file is a per-process <c>user://</c> one that is never written.
/// </remarks>
public partial class FreeForAllTest : Node
{
    private static readonly string[] Base = { "--units", "0", "--no-bases", "--mute", "--seed", "2", "--speed", "8" };

    private readonly List<string> _failures = new();
    private readonly List<DefeatEvent> _events = new();
    private readonly List<int> _unitsAtDefeat = new();
    private GameData _data = null!;
    private UiText _ui = null!;
    private Main _main = null!;
    private int _ended;
    private string? _shots;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // the banner pauses the tree
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            string[] args = OS.GetCmdlineUserArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--shots") _shots = args[i + 1];
            await Frames(1);
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            _ui = UiText.Shared ?? throw new InvalidOperationException("ui.json failed to load");
            _main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
            _main.UserArgs = Array.Empty<string>(); // the menu; each case starts its match through StartSkirmish
            _main.SettingsPath = $"user://ffa-test-{OS.GetProcessId()}/settings.cfg";
            AddChild(_main);
            await Frames(2);
            // M6-V11: --size 1280x720 for the screenshots, after the shell applied its settings' window size.
            if (StatsScreenCheck.TryWindow(OS.GetCmdlineUserArgs(), out Vector2I window))
            {
                DisplayServer.WindowSetSize(window);
                GetTree().Root.Size = window;
                await Frames(2);
            }
            await FourSeatsVictory();
            await FourSeatsDefeatFirst();
            await ThreeSeatsHumanLast();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        foreach (string f in _failures.Take(60)) GD.Print($"FREE FOR ALL TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("FREE FOR ALL TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- the cases ----

    private async Task FourSeatsVictory()
    {
        Match match = Start(4);
        Simulation sim = match.Simulation!;
        World w = sim.World;
        Check(w.StartLocations.Length == 4 && match.PlayerRgb.Distinct().Count() == 4,
            $"four seats: {w.StartLocations.Length} start locations, {match.PlayerRgb.Distinct().Count()} colours");
        await Halls(sim, 4);
        for (int victim = 1; victim <= 3; victim++)
        {
            EntityHandle target = BuildingOf(w, victim);
            await Army(sim, 0, target, 6);
            Raze(sim, 0, target);
            if (victim < 3)
            {
                await UntilDefeated(sim, victim);
                await Ticks(sim, 10);
                Check(!_main.BannerUp && match.Outcome == MatchOutcome.None && !sim.MatchOver && _ended == 0,
                    $"four seats, player {victim} down: banner {_main.BannerUp}, outcome {match.Outcome}, MatchOver {sim.MatchOver} (want none: {4 - victim} stand)");
            }
        }
        await UntilBanner(sim, "four seats victory");
        MatchOverBanner banner = _main.Banner!;
        Check(banner.Title.Text == _ui.MatchOver(MatchOverText.Victory) && match.Outcome == MatchOutcome.Victory && sim.MatchOver && GetTree().Paused
            && _events.Count == 3 && sim.TickNumber == _events[2].Tick + 1,
            $"four seats victory: title '{banner.Title.Text}', outcome {match.Outcome}, MatchOver {sim.MatchOver}, events [{string.Join(", ", _events)}], tick {sim.TickNumber}");
        await Click(banner.StatsButton);
        CheckStats(match, 4, "four seats victory", new[] { "", "", "", "" });
        await Shot("stats-4");
        await Type(Key.Escape);
        await QuitMatch();
    }

    private async Task FourSeatsDefeatFirst()
    {
        Match match = Start(4, "--ai", "3", "easy", "--ai", "4", "hard");
        Simulation sim = match.Simulation!;
        World w = sim.World;
        Check(match.Seats[2].Controller == SeatController.Ai && match.Seats[3].Controller == SeatController.Ai && match.Seats[1].Controller == SeatController.Human,
            $"defeat first: seats [{string.Join(", ", match.Seats)}]");
        await Halls(sim, 4);
        EntityHandle own = BuildingOf(w, 0);
        await Army(sim, 1, own, 6);
        Raze(sim, 1, own);
        await UntilBanner(sim, "four seats defeat first");
        MatchOverBanner banner = _main.Banner!;
        int bannerTick = sim.TickNumber;
        Check(banner.Title.Text == _ui.MatchOver(MatchOverText.Defeat) && match.Outcome == MatchOutcome.Defeat && !sim.MatchOver && GetTree().Paused
            && _events.Count == 1 && _events[0].Player == 0 && Enumerable.Range(1, 3).All(p => !w.IsDefeated(p)),
            $"defeat first: title '{banner.Title.Text}', outcome {match.Outcome}, MatchOver {sim.MatchOver} (want false: three stand), events [{string.Join(", ", _events)}]");
        await Seconds(0.3);
        Check(sim.TickNumber == bannerTick, $"defeat first: ticks {bannerTick} -> {sim.TickNumber} under the banner");
        // Continue watching: the AIs and player 1 play on.
        await Click(banner.ContinueButton);
        await Ticks(sim, Math.Max(60, 2 * w.StatsSampleTicks + 1 - sim.TickNumber)); // M6-V11: two army-value samples for the graph rows
        Check(!banner.Visible && !GetTree().Paused && _ended == 1 && sim.TickNumber > bannerTick + 50 && !sim.MatchOver,
            $"after Continue: banner {banner.Visible}, paused {GetTree().Paused}, ended {_ended}, tick {sim.TickNumber} (banner {bannerTick}), MatchOver {sim.MatchOver}");
        _main.PauseMatch();
        await Frames(2); // the Stats button was hidden: its rect is laid out on the next frame
        Check(_main.Pause!.StatsButton.Visible, "the pause menu after the banner has no Stats");
        await Click(_main.Pause.StatsButton);
        CheckStats(match, 4, "defeat first", new[] { "", _ui.MatchOver(MatchOverText.Human), _ui.Setup(SetupText.DifficultyEasy), _ui.Setup(SetupText.DifficultyHard) });
        await Shot("stats-4-ai");
        await Type(Key.Escape);
        await Click(_main.Pause.QuitToMenuButton);
        await Frames(2);
        Check(_main.CurrentMatch == null && !GetTree().Paused, "quit after defeat first");
    }

    private async Task ThreeSeatsHumanLast()
    {
        Match match = Start(3);
        Simulation sim = match.Simulation!;
        World w = sim.World;
        Check(w.Config.PlayerCount == 3 && w.StartLocations.Length == 3 && match.PlayerRgb.Distinct().Count() == 3,
            $"three seats: {w.Config.PlayerCount} players, {w.StartLocations.Length} start locations");
        await Halls(sim, 3);
        EntityHandle first = BuildingOf(w, 1);
        await Army(sim, 2, first, 6);
        Raze(sim, 2, first);
        await UntilDefeated(sim, 1);
        await Ticks(sim, 10);
        Check(!_main.BannerUp && match.Outcome == MatchOutcome.None && !sim.MatchOver, $"three seats, player 1 down: banner {_main.BannerUp}, outcome {match.Outcome}");
        EntityHandle own = BuildingOf(w, 0);
        await Army(sim, 2, own, 6);
        Raze(sim, 2, own);
        await UntilBanner(sim, "three seats human last");
        MatchOverBanner banner = _main.Banner!;
        Check(banner.Title.Text == _ui.MatchOver(MatchOverText.Defeat) && match.Outcome == MatchOutcome.Defeat && sim.MatchOver && !w.IsDefeated(2),
            $"three seats human last: title '{banner.Title.Text}', outcome {match.Outcome}, MatchOver {sim.MatchOver}");
        await Click(banner.StatsButton);
        CheckStats(match, 3, "three seats human last", new[] { "", "", "" });
        await Type(Key.Escape);
        await QuitMatch();
    }

    // ---- checks ----

    // The open stats screen: one row per seat, its faction, colour and seat name (seats[p] when not empty; "You" for player
    // 0), and the defeat time of each event's player, every other Standing.
    private void CheckStats(Match match, int players, string how, string[] seats)
    {
        MatchStats stats = _main.Stats!;
        World w = match.Simulation!.World;
        int col = Array.FindIndex(MatchStats.Columns, c => c.Label == MatchOverText.DefeatedAt);
        Check(stats.Visible && stats.RowCount == players, $"{how}: stats visible {stats.Visible}, {stats.RowCount} rows (want {players})");
        for (int p = 0; p < Math.Min(players, stats.RowCount); p++)
        {
            DefeatEvent? e = _events.Where(x => x.Player == p).Select(x => (DefeatEvent?)x).FirstOrDefault();
            string want = e is DefeatEvent d ? _ui.Clock(d.Tick + 1) : _ui.MatchOver(MatchOverText.Standing);
            Check(stats.Cell(p, col) == want && match.DefeatedAt(p) == (e is DefeatEvent d2 ? d2.Tick + 1 : -1),
                $"{how}: row {p} defeated '{stats.Cell(p, col)}' (want '{want}'), DefeatedAt {match.DefeatedAt(p)}");
            string seat = p == 0 ? _ui.Setup(SetupText.SlotHuman) : seats[p].Length > 0 ? seats[p] : _ui.MatchOver(MatchOverText.Human);
            Check(stats.SeatName(p) == seat && stats.FactionName(p) == _data.Factions[w.FactionOf(p)].DisplayName && stats.Swatch(p).ToRgba32() >> 8 == match.PlayerRgb[p],
                $"{how}: row {p} seat '{stats.SeatName(p)}' (want '{seat}'), faction '{stats.FactionName(p)}', swatch {stats.Swatch(p).ToHtml(false)} (want {match.PlayerRgb[p]:X6})");
        }
        // M6-V11: the counter columns and the graph: one line per seat (every seat has samples past tick 200), in its swatch's colour.
        foreach (string f in StatsScreenCheck.Columns(stats, w, _ui, how)) Check(false, f);
        foreach (string f in StatsScreenCheck.Graph(stats, match, drawn: true, how)) Check(false, f);
        foreach (string f in StatsScreenCheck.Fits(stats, GetViewport().GetVisibleRect(), how)) Check(false, f); // four rows at 1152 x 648, under 1280 x 720
        ArmyValueGraph g = stats.Graph;
        Check(w.ArmyValueSamples(players - 1).Length > 0 && g.DrawnLines == players
            && Enumerable.Range(0, players).Select(p => g.LineColour(p).ToRgba32()).Distinct().Count() == players,
            $"{how}: graph drew {g.DrawnLines} lines for {players} seats ({w.ArmyValueSamples(players - 1).Length} samples), colours [{string.Join(", ", Enumerable.Range(0, players).Select(p => g.LineColour(p).ToHtml(false)))}]");
    }

    // ---- set-up (commands only) ----

    private Match Start(int players, params string[] extra)
    {
        // Every seat Malazan (alike halls and armies), so the stagings are the same whichever seat razes.
        var args = Base.Concat(new[] { "--players", players.ToString() }).ToList();
        for (int s = 1; s <= players; s++) args.AddRange(new[] { "--faction", s.ToString(), "malazan" });
        _main.StartSkirmish(args.Concat(extra).ToArray());
        Match? match = _main.CurrentMatch;
        if (match?.Simulation == null) throw new InvalidOperationException("the shell started no match");
        Check(match.Simulation.World.Config.PlayerCount == players && match.Seats.Length == players, $"--players {players}: {match.Simulation.World.Config.PlayerCount} players");
        _events.Clear();
        _unitsAtDefeat.Clear();
        _ended = 0;
        match.Ended += _ => _ended++;
        match.GetNode<SimRunner>("SimRunner").Ticked += s =>
        {
            foreach (DefeatEvent e in s.World.DefeatEvents)
            {
                _events.Add(e);
                _unitsAtDefeat.Add(DefeatStaging.UnitsOf(s.World, e.Player).Count);
            }
        };
        match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
        return match;
    }

    // One finished training hall per player, spread over the map (west, east, north, south); no unit.
    private async Task Halls(Simulation sim, int players)
    {
        World w = sim.World;
        NavGrid g = w.NavGrid;
        (int X, int Y)[] at = { (g.Width / 4, g.Height / 2), (g.Width * 3 / 4, g.Height / 2), (g.Width / 2, g.Height / 5), (g.Width / 2, g.Height * 4 / 5) };
        for (int p = 0; p < players; p++)
        {
            int hall = DefeatStaging.Hall(_data, w, p);
            DefeatStaging.SpawnAt(sim, p, hall, DefeatStaging.Anchor(w, p, hall, at[p].X, at[p].Y));
        }
        await Ticks(sim, 3);
        Check(w.Buildings.Count == players, $"{w.Buildings.Count} halls placed (want {players})");
    }

    // `n` of `player`'s siege units round `target`'s footprint, 4-5 cells out (past the catapult's minimum range) on passable
    // cells of the hall's own level: a shooter below a cliff doesn't see the hall, and its Attack is refused.
    private async Task Army(Simulation sim, int player, EntityHandle target, int n)
    {
        World w = sim.World;
        NavGrid g = w.NavGrid;
        int siege = StartBase.UnitOfSlot(_data, w.FactionOf(player), UnitSlot.Siege); // 3x on structures
        int cell = w.Buildings.Cell[target.Index];
        BuildingDef def = _data.Buildings[w.Buildings.TypeId[target.Index]];
        int cx = cell % g.Width, cy = cell / g.Width, level = g.LevelAt(cx, cy), placed = 0;
        for (int r = 4; r <= 5 && placed < n; r++)
            for (int y = cy - r; y <= cy + def.FootprintHeight - 1 + r && placed < n; y++)
                for (int x = cx - r; x <= cx + def.FootprintWidth - 1 + r && placed < n; x++)
                {
                    bool ring = x == cx - r || x == cx + def.FootprintWidth - 1 + r || y == cy - r || y == cy + def.FootprintHeight - 1 + r;
                    if (!ring || (x + y) % 2 != 0 || !g.IsPassable(x, y) || g.LevelAt(x, y) != level) continue;
                    sim.Enqueue(Command.SpawnUnit(player, siege, g.CellCenter(x, y)));
                    placed++;
                }
        Check(placed == n, $"only {placed} of {n} shooters fit round player {w.Buildings.Owner[target.Index]}'s hall");
        await Ticks(sim, 3);
    }

    private static void Raze(Simulation sim, int player, EntityHandle target)
    {
        foreach (EntityHandle u in DefeatStaging.UnitsOf(sim.World, player)) sim.Enqueue(Command.Attack(player, u, target, isBuilding: true));
    }

    private static EntityHandle BuildingOf(World w, int player)
    {
        BuildingStore b = w.Buildings;
        for (int i = 0; i < b.Capacity; i++) if (b.Alive[i] && b.Owner[i] == player) return new EntityHandle(i, b.Generation[i]);
        throw new InvalidOperationException($"player {player} has no building");
    }

    private async Task UntilDefeated(Simulation sim, int player)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!sim.World.IsDefeated(player) && clock.Elapsed.TotalSeconds < 90) await Frames(1);
        Check(sim.World.IsDefeated(player), $"player {player} not defeated by tick {sim.TickNumber}");
    }

    private async Task UntilBanner(Simulation sim, string how)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!_main.BannerUp && clock.Elapsed.TotalSeconds < 90) await Frames(1);
        Check(_main.BannerUp && _ended == 1, $"{how}: banner {_main.BannerUp}, ended {_ended} by tick {sim.TickNumber}");
        if (!_main.BannerUp) throw new InvalidOperationException($"{how}: no banner; {Board(sim.World)}");
        Check(_unitsAtDefeat.Count == _events.Count && _unitsAtDefeat.TrueForAll(n => n == 0), $"{how}: losers' live units on their defeat ticks [{string.Join(", ", _unitsAtDefeat)}] (want all 0)");
    }

    // For a failure message: each building (owner, hp, anchor cell) and each player's units (position, state).
    private static string Board(World w)
    {
        var sb = new System.Text.StringBuilder();
        BuildingStore b = w.Buildings;
        for (int i = 0; i < b.Capacity; i++)
            if (b.Alive[i]) sb.Append($"building p{b.Owner[i]} hp {b.Hp[i]} cell ({b.Cell[i] % w.NavGrid.Width}, {b.Cell[i] / w.NavGrid.Width}); ");
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i]) sb.Append($"unit p{u.Owner[i]} at {u.Position[i]} {u.State[i]}; ");
        return sb.ToString();
    }

    private async Task QuitMatch()
    {
        if (_main.BannerUp) await Click(_main.Banner!.MenuButton);
        else
        {
            _main.PauseMatch();
            await Click(_main.Pause!.QuitToMenuButton);
        }
        await Frames(2);
        Check(_main.CurrentMatch == null && !GetTree().Paused, "quit to menu");
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
        Vector2 at = c.GetGlobalRect().GetCenter();
        GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        await Frames(1);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Left });
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
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

    // Windowed with `-- --shots <dir>`: the viewport as ffa-<name>.png (the stats screen at four seats, M6-V11).
    private async Task Shot(string name)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = $"{_shots}/ffa-{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"  screenshot {path}");
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
