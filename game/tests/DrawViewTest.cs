using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
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
/// M6-V29: a drawn match reads "Draw" (never "Victory") wherever a result shows, and the draw countdown (<see cref="DrawNotice"/>)
/// is on screen exactly while the sim's draw clock runs. The shell (<c>Main.tscn</c>) plays dev matches (<c>--units 0
/// --no-bases</c>, every seat human, zoom 40) on a copy of the data whose <c>draw.afterSeconds</c> is <see cref="DrawSeconds"/>
/// and whose <c>standard</c> preset starts with no gold, so a seat with a hall and no worker can never train: two such seats
/// are a spent match. A seat is made able to train by giving it a worker (the mines' gold counts for a seat with one).
/// Everything is staged with commands; no store is written, so <c>World.RecountDraw()</c> is never needed.
/// <list type="bullet">
/// <item>Live, two seats: the notice from the clock's first tick ("0:20"), mid-way, the layout at 1152 x 648 and 1920 x 1080
/// (clear of the resource bar, alert text, minimap, command card, queue strip and selection panel; the text fits), a click
/// through it selecting the unit under it, a save mid-clock; the clock stopped by a seat that can train again (a worker), then
/// restarted by that worker's death, and restarted again by a kill; the last tick ("0:01"); the draw: banner and stats say
/// Draw / Standing, no screen names a winner.</item>
/// <item>The Load Game list names no result. The save taken mid-clock loads with the notice up before its first tick and the
/// clock it was saved with, and reaches the draw: "Draw". The recording plays back with the notice on the same ticks with the
/// same text as live, and ends on "Draw".</item>
/// <item>Three seats, free-for-all: the local player out before the draw reads Defeat, and still Defeat once the others draw
/// (no second banner); a save after the draw loads to Defeat. Teams (local and an ally against one): the local player out
/// with its ally standing at the draw reads Draw.</item>
/// <item>A win while the clock runs: the notice goes the tick the hall falls; Victory. Data without the draw block: a spent
/// match never shows the notice.</item>
/// </list>
/// Every tick of every match is checked from <see cref="SimRunner.Ticked"/>: the notice shows iff <c>DrawTicksLeft &gt;= 0</c>,
/// its text is <c>hud.draw_countdown</c> with <c>ceil(DrawTicksLeft / 20)</c> seconds, and the label's text is written once per
/// change of the shown second.
/// </summary>
/// <remarks>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/DrawViewTest.tscn</c>; prints "DRAW VIEW TEST PASS" or FAIL
/// lines. Windowed with <c>-- --shots &lt;dir&gt;</c> it saves <c>draw-notice.png</c> and <c>draw-banner.png</c> (zoom 40, 1152 x 648).
/// </remarks>
public partial class DrawViewTest : Node
{
    /// <summary>The copy's draw wait in seconds.</summary>
    public const int DrawSeconds = 20;

    private const int Wait = DrawSeconds * SimConstants.TicksPerSecond;

    private static readonly string[] Args = { "--units", "0", "--no-bases", "--mute", "--seed", "2", "--speed", "8", "--zoom", "40", Main.NoConfineFlag };

    private readonly List<string> _failures = new();
    private readonly List<string> _notes = new();
    private string _root = "", _copy = "", _noRule = "";
    private string? _shots;
    private GameData _data = null!;
    private UiText _ui = null!;
    private Main _main = null!;
    private Monitor? _mon;
    private int _ended;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // the banner pauses the tree
        _root = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://drawview-{OS.GetProcessId()}"));
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            string[] user = OS.GetCmdlineUserArgs();
            for (int i = 0; i + 1 < user.Length; i++) if (user[i] == "--shots") _shots = user[i + 1];
            await Frames(1);
            _ui = UiText.Shared ?? throw new InvalidOperationException("ui.json failed to load");
            Texts();
            _copy = Forge("data", rule: true);
            _noRule = Forge("norule", rule: false);
            _data = DataLoader.LoadAll(_copy).Data!;
            Check(_data.Rules.DrawAfterTicks == Wait, $"the copy's draw wait is {_data.Rules.DrawAfterTicks} ticks (want {Wait})");
            _main = NewMain(_copy);
            AddChild(_main);
            await Frames(2);
            (string save, string replay, List<int> live) = await LiveTwoSeats();
            ListNamesNoResult();
            await LoadedMidClock(save);
            await Playback(replay, live);
            await LocalOutFirst();
            await TeamDraw();
            await WinWhileRunning();
            RemoveChild(_main);
            _main.QueueFree();
            await Frames(2);
            await NoRule();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        foreach (string n in _notes) GD.Print($"  note: {n}");
        foreach (string f in _failures.Take(60)) GD.Print($"DRAW VIEW TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("DRAW VIEW TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- unit rows (no match) ----

    private void Texts()
    {
        Check(DrawNotice.SecondsFor(-1) == -1 && DrawNotice.SecondsFor(1) == 1 && DrawNotice.SecondsFor(20) == 1 && DrawNotice.SecondsFor(21) == 2
            && DrawNotice.SecondsFor(5999) == 300 && DrawNotice.SecondsFor(0) == 0,
            "SecondsFor does not round the ticks left up to whole seconds");
        string format = _ui.Hud(HudText.DrawCountdown);
        Check(format.Contains("{0}", StringComparison.Ordinal), $"hud.draw_countdown names no time: '{format}'");
        Check(_ui.DrawCountdown(300) == string.Format(format, "5:00") && _ui.DrawCountdown(1) == string.Format(format, "0:01") && _ui.DrawCountdown(61) == string.Format(format, "1:01"),
            $"the countdown's clock: '{_ui.DrawCountdown(300)}', '{_ui.DrawCountdown(1)}', '{_ui.DrawCountdown(61)}'");
        // The key is checked like the other formats: one without {0}, or with a stray placeholder, is a load error naming it.
        string shipped = File.ReadAllText(UiText.DefaultPath);
        foreach (string bad in new[] { "Draw soon", "Draw in {1}", "Draw in {0" })
        {
            JsonNode root = JsonNode.Parse(shipped)!;
            root["hud"]!["draw_countdown"] = bad;
            var errors = new List<string>();
            Check(UiText.Parse(root.ToJsonString(), errors) == null && errors.Any(e => e.Contains("hud.draw_countdown", StringComparison.Ordinal)),
                $"ui.json with hud.draw_countdown '{bad}' loaded: [{string.Join("; ", errors)}]");
        }
        JsonNode gone = JsonNode.Parse(shipped)!;
        gone["hud"]!.AsObject().Remove("draw_countdown");
        var missing = new List<string>();
        Check(UiText.Parse(gone.ToJsonString(), missing) == null && missing.Any(e => e.Contains("missing hud.draw_countdown", StringComparison.Ordinal)),
            $"ui.json without hud.draw_countdown loaded: [{string.Join("; ", missing)}]");
    }

    // ---- live, two seats ----

    private async Task<(string Save, string Replay, List<int> Live)> LiveTwoSeats()
    {
        _main.StartSkirmish(Args);
        Match match = Watch(_main.CurrentMatch);
        Simulation sim = match.Simulation!;
        World w = sim.World;
        NavGrid g = w.NavGrid;
        DrawNotice notice = match.DrawNotice!;
        Check(notice.TextLabel != null && !notice.Shown, "live: no notice label, or it shows before the match is spent");
        await Halls(sim, (0, g.Width / 4, g.Height / 2), (1, g.Width * 3 / 4, g.Height / 2));
        DropOffFor0(sim); // M6-SH3: the worker player 0 gets below needs a drop-off of its own to count the mines' gold
        // Player 1's lone soldier, mid-map and far from both halls: the victim of the kills that restart the clock.
        Spawn(sim, 1, Unit(w, 1, UnitSlot.Line), g.Width / 2, g.Height * 3 / 4);
        await Ticks(sim, 2);
        EntityHandle soldier = DefeatStaging.UnitsOf(w, 1).FirstOrDefault();
        Check(soldier != default && w.MatchSpent && !w.CanStillTrain(0) && !w.CanStillTrain(1) && w.DrawTicksLeft >= 0 && notice.Shown,
            $"live: spent {w.MatchSpent}, can train {w.CanStillTrain(0)} / {w.CanStillTrain(1)}, left {w.DrawTicksLeft}, shown {notice.Shown}");
        // The first tick: the clock at 1, the whole wait less a tick left, shown as the wait.
        Check(_mon!.Starts.Count >= 1 && _mon.Starts[0].Left == Wait - 1 && _mon.Starts[0].Text == string.Format(_ui.Hud(HudText.DrawCountdown), "0:20"),
            $"live: the first tick's notice {(_mon.Starts.Count > 0 ? $"left {_mon.Starts[0].Left} '{_mon.Starts[0].Text}'" : "none")} (want {Wait - 1}, 0:20)");

        // Mid-way: 10 s left.
        await Until(sim, () => w.DrawTicksLeft is >= 0 and <= Wait / 2, "live: mid-way");
        int mid = w.DrawTicksLeft;
        Check(notice.Shown && notice.TextLabel!.Text == string.Format(_ui.Hud(HudText.DrawCountdown), _ui.Clock(DrawNotice.SecondsFor(mid) * SimConstants.TicksPerSecond))
            && notice.TextLabel.Text.Contains("0:10", StringComparison.Ordinal),
            $"live mid-way: left {mid}, '{notice.TextLabel?.Text}' (want 0:10)");
        GetTree().Paused = true; // hold the clock for the layout rows, the click and the picture
        await Layout(match, "live");
        await ClickThrough(match);
        System.Numerics.Vector2 home = HallCenter(w, 0);
        match.GetNode<RtsCamera>("RtsCamera").SetFocus(home.X, home.Y);
        await Shot("notice");
        // A save mid-clock (the tree paused: between ticks).
        Check(_main.SaveGame() && _main.LastSavePath != null, "live: the mid-clock save was not written");
        string save = _main.LastSavePath ?? "";
        int savedLeft = w.DrawTicksLeft, savedClock = w.DrawClock;
        _notes.Add($"live: saved at tick {sim.TickNumber} with {savedLeft} ticks left (clock {savedClock})");
        GetTree().Paused = false;

        // The clock stops without a draw: player 0 gets a worker (the mines' gold is in reach: it can train again).
        int stopsBefore = _mon.Stops.Count, unitsBefore = DefeatStaging.UnitsOf(w, 0).Count;
        Spawn(sim, 0, Unit(w, 0, UnitSlot.Worker), g.Width / 2 + 3, g.Height * 3 / 4); // in the soldier's sight: an Attack needs its target seen
        await Until(sim, () => _mon.Stops.Count > stopsBefore, "live: the worker stops the clock");
        Check(DefeatStaging.UnitsOf(w, 0).Count == unitsBefore + 1, "live: the worker did not appear");
        EntityHandle worker = DefeatStaging.UnitsOf(w, 0).First(u => w.Units.TypeId[u.Index] == Unit(w, 0, UnitSlot.Worker));
        Check(w.CanStillTrain(0) && !w.MatchSpent && w.DrawTicksLeft == -1 && !notice.Shown && !w.MatchOver,
            $"live, a worker: can train {w.CanStillTrain(0)}, spent {w.MatchSpent}, left {w.DrawTicksLeft}, shown {notice.Shown}");
        Check(_mon.Stops.Count == stopsBefore + 1 && _mon.Stops[^1].Cause == "can-train", $"live: the stop by a seat that can train: [{string.Join(", ", _mon.Stops)}]");
        // The soldier kills it: a death; the match is spent again from the next tick, the clock from the start.
        sim.Enqueue(Command.Attack(1, soldier, worker, isBuilding: false));
        await Until(sim, () => !w.Units.IsAlive(worker) && w.DrawTicksLeft >= 0, "live: the worker's death and the restart",
            () => $"(worker alive {w.Units.IsAlive(worker)} at {w.Units.Position[worker.Index]} hp {w.Units.Hp[worker.Index]}, soldier alive {w.Units.IsAlive(soldier)} at {w.Units.Position[soldier.Index]} state {w.Units.State[soldier.Index]}, left {w.DrawTicksLeft}, spent {w.MatchSpent}, can train {w.CanStillTrain(0)} / {w.CanStillTrain(1)}, stops [{string.Join(", ", _mon.Stops)}])");
        Check(_mon.Starts.Count >= 2 && _mon.Starts[^1].Left == Wait - 1, $"live: the restart after the worker: [{string.Join(", ", _mon.Starts)}]");

        // A kill restarts the running clock: four of player 0's soldiers kill player 1's.
        await Until(sim, () => w.DrawTicksLeft is >= 0 and < Wait - 100, "live: the clock 5 s in");
        int startsBefore = _mon.Starts.Count;
        stopsBefore = _mon.Stops.Count;
        int line0 = Unit(w, 0, UnitSlot.Line);
        for (int k = 0; k < 4; k++) Spawn(sim, 0, line0, g.Width / 2 - 2 + k, g.Height * 3 / 4 - 3);
        await Ticks(sim, 2);
        foreach (EntityHandle u in DefeatStaging.UnitsOf(w, 0))
            if (w.Units.TypeId[u.Index] == line0) sim.Enqueue(Command.Attack(0, u, soldier, isBuilding: false));
        await Until(sim, () => !w.Units.IsAlive(soldier) && w.DrawTicksLeft >= 0, "live: the soldier's death and the restart");
        Check(_mon.Stops.Count > stopsBefore && _mon.Stops.Skip(stopsBefore).All(s => s.Cause == "death") && _mon.Starts.Count > startsBefore
            && _mon.Starts[^1].Left == Wait - 1 && _mon.Starts[^1].Text == string.Format(_ui.Hud(HudText.DrawCountdown), "0:20"),
            $"live: the restart by a kill: stops [{string.Join(", ", _mon.Stops.Skip(stopsBefore))}], starts [{string.Join(", ", _mon.Starts.Skip(startsBefore))}]");

        // The draw.
        await UntilBanner(sim, "live draw");
        MatchOverBanner banner = _main.Banner!;
        Check(w.MatchDrawn && !w.IsDefeated(0) && !w.IsDefeated(1) && match.Outcome == MatchOutcome.Draw && banner.Shown == MatchOutcome.Draw
            && banner.Title.Text == _ui.MatchOver(MatchOverText.Draw) && Match.ResultFor(w, 0) == MatchOutcome.Draw,
            $"live draw: drawn {w.MatchDrawn}, outcome {match.Outcome}, banner '{banner.Title.Text}'");
        Check(_mon.SawLast && _mon.Stops[^1].Cause == "draw" && !notice.Shown, $"live draw: last tick seen {_mon.SawLast}, stops [{string.Join(", ", _mon.Stops)}], shown {notice.Shown}");
        _mon.Finish();
        await Shot("banner");
        await StatsAgree(match, "live draw");
        string replay = _main.ReplayPath ?? "";
        Check(File.Exists(replay), $"live draw: no recording at '{replay}'");
        List<int> live = _mon.Seq.ToList();
        await Click(banner.MenuButton);
        await Frames(2);
        return (save, replay, live);
    }

    // ---- the Load Game list ----

    private void ListNamesNoResult()
    {
        _main.ShowLoadGame();
        LoadGameScreen load = _main.LoadScreen!;
        var texts = new List<string>();
        for (TreeItem? item = load.List.GetRoot()?.GetFirstChild(); item != null; item = item.GetNext())
            for (int c = 0; c < load.List.Columns; c++) texts.Add(item.GetText(c));
        string[] results = { _ui.MatchOver(MatchOverText.Victory), _ui.MatchOver(MatchOverText.Defeat), _ui.MatchOver(MatchOverText.Draw) };
        Check(load.RowCount >= 2, $"Load Game: {load.RowCount} rows (want the save and the recording)");
        Check(!texts.Any(t => results.Any(r => t.Contains(r, StringComparison.Ordinal))), $"Load Game names a result: [{string.Join(" | ", texts)}]");
        _main.ShowMenu();
    }

    // ---- the save taken mid-clock ----

    private async Task LoadedMidClock(string save)
    {
        Check(_main.LoadSavedGame(save), $"the mid-clock save '{save}' did not load");
        Match match = Watch(_main.CurrentMatch);
        Simulation sim = match.Simulation!;
        World w = sim.World;
        DrawNotice notice = match.DrawNotice!;
        // Before its first tick: the clock as saved, the notice already up with its time.
        Check(w.DrawTicksLeft >= 0 && notice.Shown && notice.TextLabel!.Text == _ui.DrawCountdown(DrawNotice.SecondsFor(w.DrawTicksLeft)) && notice.TextWrites == 1,
            $"loaded: left {w.DrawTicksLeft}, shown {notice.Shown}, '{notice.TextLabel?.Text}', writes {notice.TextWrites}");
        await UntilBanner(sim, "loaded draw");
        Check(w.MatchDrawn && match.Outcome == MatchOutcome.Draw && _main.Banner!.Title.Text == _ui.MatchOver(MatchOverText.Draw) && !notice.Shown && _mon!.SawLast,
            $"loaded draw: drawn {w.MatchDrawn}, outcome {match.Outcome}, banner '{_main.Banner!.Title.Text}', shown {notice.Shown}");
        _mon!.Finish();
        await StatsAgree(match, "loaded draw");
        await Click(_main.Banner.MenuButton);
        await Frames(2);
    }

    // ---- the recording ----

    private async Task Playback(string replay, List<int> live)
    {
        Check(_main.WatchReplay(replay), $"the recording '{replay}' did not play");
        Match match = Watch(_main.CurrentMatch);
        Simulation sim = match.Simulation!;
        await UntilBanner(sim, "playback draw");
        await Frames(3); // the recording's end lands on the draw's tick: the banner keeps its title
        MatchOverBanner banner = _main.Banner!;
        Check(sim.World.MatchDrawn && match.Outcome == MatchOutcome.Draw && banner.Title.Text == _ui.MatchOver(MatchOverText.Draw) && !banner.ContinueButton.Visible,
            $"playback: drawn {sim.World.MatchDrawn}, outcome {match.Outcome}, banner '{banner.Title.Text}', continue {banner.ContinueButton.Visible}");
        _mon!.Finish();
        List<int> played = _mon.Seq;
        int diff = Enumerable.Range(0, Math.Min(live.Count, played.Count)).FirstOrDefault(i => live[i] != played[i], -1);
        Check(played.Count == live.Count && diff < 0,
            $"playback: the notice's seconds per tick differ from live ({played.Count} ticks vs {live.Count}; first at {diff}: {(diff >= 0 ? $"{played[diff]} vs {live[diff]}" : "")})");
        await StatsAgree(match, "playback draw");
        await Click(banner.MenuButton);
        await Frames(2);
    }

    // ---- three seats: the local player out first ----

    private async Task LocalOutFirst()
    {
        _main.BeforeStart = m => { Setup(m); m.GetNode<SimRunner>("SimRunner").PlayerCount = 3; };
        _main.StartSkirmish(Args);
        _main.BeforeStart = Setup;
        Match match = Watch(_main.CurrentMatch);
        Simulation sim = match.Simulation!;
        World w = sim.World;
        NavGrid g = w.NavGrid;
        await Halls(sim, (0, g.Width / 4, g.Height / 2), (1, g.Width / 2, g.Height / 5), (2, g.Width * 3 / 4, g.Height / 2));
        DropOffFor0(sim); // M6-SH3: so the worker keeps player 0 able to train until it is knocked out
        Spawn(sim, 0, Unit(w, 0, UnitSlot.Worker), g.Width / 4, g.Height / 2 + 6);
        await Ticks(sim, 2);
        Check(w.CanStillTrain(0) && !w.MatchSpent, $"local out: player 0 can train {w.CanStillTrain(0)}, spent {w.MatchSpent}");
        await KnockOut(sim, killer: 1, victim: 0);
        await UntilBanner(sim, "local out");
        MatchOverBanner banner = _main.Banner!;
        Check(w.IsDefeated(0) && !w.MatchOver && match.Outcome == MatchOutcome.Defeat && banner.Title.Text == _ui.MatchOver(MatchOverText.Defeat),
            $"local out: defeated {w.IsDefeated(0)}, over {w.MatchOver}, outcome {match.Outcome}, banner '{banner.Title.Text}'");
        await Click(banner.ContinueButton);
        await Until(sim, () => w.MatchDrawn, "local out: the others' draw");
        await Frames(3);
        Check(_ended == 1 && !_main.BannerUp && match.Outcome == MatchOutcome.Defeat && Match.ResultFor(w, 0) == MatchOutcome.Defeat && !w.IsDefeated(1) && !w.IsDefeated(2),
            $"local out, drawn: ended {_ended}, banner {_main.BannerUp}, outcome {match.Outcome}, result now {Match.ResultFor(w, 0)}");
        _mon!.Finish();
        _main.PauseMatch();
        await Frames(2);
        await Click(_main.Pause!.StatsButton);
        CheckStats(match, "local out");
        _main.CloseStats();
        // A save after the draw loads to the same answer.
        Check(_main.SaveGame() && _main.LastSavePath != null, "local out: the save after the draw was not written");
        string save = _main.LastSavePath ?? "";
        await Click(_main.Pause.QuitToMenuButton);
        await Frames(2);
        Check(_main.LoadSavedGame(save), "local out: the save after the draw did not load");
        Match loaded = Watch(_main.CurrentMatch);
        await UntilBanner(loaded.Simulation!, "local out, loaded");
        Check(loaded.Outcome == MatchOutcome.Defeat && _main.Banner!.Title.Text == _ui.MatchOver(MatchOverText.Defeat) && !loaded.DrawNotice!.Shown,
            $"local out, loaded: outcome {loaded.Outcome}, banner '{_main.Banner!.Title.Text}'");
        _mon!.Finish();
        await Click(_main.Banner.MenuButton);
        await Frames(2);
    }

    // ---- teams: the local player out, its ally standing at the draw ----

    private async Task TeamDraw()
    {
        _main.BeforeStart = m => { Setup(m); m.GetNode<SimRunner>("SimRunner").PlayerCount = 3; };
        _main.StartSkirmish(Args.Concat(new[] { "--players", "3", "--team", "1", "1", "--team", "2", "1", "--team", "3", "2" }).ToArray());
        _main.BeforeStart = Setup;
        Match match = Watch(_main.CurrentMatch);
        Simulation sim = match.Simulation!;
        World w = sim.World;
        NavGrid g = w.NavGrid;
        Check(w.AreAllied(0, 1) && !w.AreAllied(0, 2), "teams: players 0 and 1 are not one team against 2");
        await Halls(sim, (0, g.Width / 4, g.Height / 2), (1, g.Width / 2, g.Height / 5), (2, g.Width * 3 / 4, g.Height / 2));
        DropOffFor0(sim); // M6-SH3: so the worker keeps player 0 able to train until it is knocked out
        Spawn(sim, 0, Unit(w, 0, UnitSlot.Worker), g.Width / 4, g.Height / 2 + 6);
        await Ticks(sim, 2);
        await KnockOut(sim, killer: 2, victim: 0);
        await Until(sim, () => w.IsDefeated(0), "teams: player 0 out");
        await Frames(2);
        Check(!_main.BannerUp && match.Outcome == MatchOutcome.None, $"teams: a banner for the local player out with its ally standing ({match.Outcome})");
        await UntilBanner(sim, "teams draw");
        Check(w.MatchDrawn && w.IsDefeated(0) && !w.IsDefeated(1) && match.Outcome == MatchOutcome.Draw && _main.Banner!.Title.Text == _ui.MatchOver(MatchOverText.Draw),
            $"teams draw: drawn {w.MatchDrawn}, outcome {match.Outcome}, banner '{_main.Banner!.Title.Text}'");
        _mon!.Finish();
        await StatsAgree(match, "teams draw");
        Check(_main.Stats!.TeamLabels.Count == 2, $"teams draw: {_main.Stats.TeamLabels.Count} team labels");
        await Click(_main.Banner.MenuButton);
        await Frames(2);
    }

    // ---- a win while the clock runs ----

    private async Task WinWhileRunning()
    {
        _main.StartSkirmish(Args);
        Match match = Watch(_main.CurrentMatch);
        Simulation sim = match.Simulation!;
        World w = sim.World;
        NavGrid g = w.NavGrid;
        await Halls(sim, (0, g.Width / 4, g.Height / 2), (1, g.Width * 3 / 4, g.Height / 2));
        await Until(sim, () => w.DrawTicksLeft >= 0, "win: the clock");
        EntityHandle hall = BuildingOf(w, 1);
        int cell = w.Buildings.Cell[hall.Index];
        int siege = Unit(w, 0, UnitSlot.Siege);
        for (int i = 0; i < 10; i++) Spawn(sim, 0, siege, cell % g.Width - 4 + i % 5 * 2, cell / g.Width - 5 - i / 5 * 2);
        await Ticks(sim, 2);
        foreach (EntityHandle u in DefeatStaging.UnitsOf(w, 0)) sim.Enqueue(Command.Attack(0, u, hall, isBuilding: true));
        await UntilBanner(sim, "win");
        Check(!w.MatchDrawn && w.IsDefeated(1) && match.Outcome == MatchOutcome.Victory && _main.Banner!.Title.Text == _ui.MatchOver(MatchOverText.Victory) && !match.DrawNotice!.Shown,
            $"win: drawn {w.MatchDrawn}, outcome {match.Outcome}, banner '{_main.Banner!.Title.Text}'");
        Check(_mon!.Stops.Count == 1 && _mon.Stops[0].Cause == "win" && _mon.Stops[0].Tick == sim.TickNumber,
            $"win: the notice was not up until the win's tick: stops [{string.Join(", ", _mon.Stops)}] at tick {sim.TickNumber}");
        _mon.Finish();
        await Click(_main.Banner.MenuButton);
        await Frames(2);
    }

    // ---- no draw rule ----

    private async Task NoRule()
    {
        _main = NewMain(_noRule);
        AddChild(_main);
        await Frames(2);
        _main.StartSkirmish(Args);
        Match match = Watch(_main.CurrentMatch);
        Simulation sim = match.Simulation!;
        World w = sim.World;
        NavGrid g = w.NavGrid;
        await Halls(sim, (0, g.Width / 4, g.Height / 2), (1, g.Width * 3 / 4, g.Height / 2));
        await Ticks(sim, Wait / 2);
        Check(w.DrawWaitTicks == 0 && w.MatchSpent && w.DrawTicksLeft == -1 && _mon!.ShownTicks == 0 && !match.DrawNotice!.Shown && match.DrawNotice.TextWrites == 0,
            $"no rule: wait {w.DrawWaitTicks}, spent {w.MatchSpent}, left {w.DrawTicksLeft}, shown on {_mon!.ShownTicks} ticks");
        _mon.Finish();
        _main.PauseMatch();
        await Frames(2);
        await Click(_main.Pause!.QuitToMenuButton);
        await Frames(2);
    }

    // ---- checks ----

    // The notice clear of every HUD part listed, inside the window, its text inside its rect, at both sizes.
    private async Task Layout(Match match, string how)
    {
        foreach (Vector2I size in new[] { new Vector2I(1152, 648), new Vector2I(1920, 1080) })
        {
            GetTree().Root.Size = size;
            await Frames(3);
            Label label = match.DrawNotice!.TextLabel!;
            Rect2 r = label.GetGlobalRect();
            Rect2 screen = GetViewport().GetVisibleRect();
            Check(screen.Encloses(r) && label.GetMinimumSize().X <= r.Size.X && label.GetMinimumSize().Y <= r.Size.Y + 0.5f,
                $"{how} {size}: notice {r} (text {label.GetMinimumSize()}) not inside the screen {screen} or its rect");
            var parts = new List<(string, Control?)>
            {
                ("resource bar", match.GetNodeOrNull<Control>("Hud/ResourceBar")),
                ("alert text", match.GetNode<AlertHud>("Alerts").TextLabel),
                ("minimap", match.GetNodeOrNull<Control>("Hud/Minimap")),
                ("command card", match.GetNodeOrNull<Control>("Hud/CommandCard")),
                ("queue strip", match.GetNodeOrNull<Control>("Hud/QueueStrip")),
                ("selection panel", match.GetNodeOrNull<Control>("Hud/SelectionPanel")),
                ("replay mark", match.GetNodeOrNull<Control>("Hud/ReplayMark")),
            };
            foreach ((string name, Control? c) in parts)
            {
                if (c == null)
                {
                    Check(name == "replay mark", $"{how} {size}: no {name}");
                    continue;
                }
                Check(!c.GetGlobalRect().Intersects(r), $"{how} {size}: the notice {r} overlaps the {name} {c.GetGlobalRect()}");
            }
            _notes.Add($"{how} {size}: notice {r}, text {label.GetMinimumSize().X:0} px wide");
        }
        GetTree().Root.Size = new Vector2I(1152, 648);
        await Frames(3);
    }

    // A click on the notice reaches the map: one of player 0's units stood under it is selected.
    private async Task ClickThrough(Match match)
    {
        Simulation sim = match.Simulation!;
        World w = sim.World;
        NavGrid g = w.NavGrid;
        var camera = match.GetNode<RtsCamera>("RtsCamera");
        System.Numerics.Vector2 hall = HallCenter(w, 0);
        camera.SetFocus(hall.X, hall.Y);
        await Frames(2);
        Label label = match.DrawNotice!.TextLabel!;
        Vector2 at = label.GetGlobalRect().GetCenter();
        Vector3 o = camera.ProjectRayOrigin(at), d = camera.ProjectRayNormal(at);
        if (!Check(GroundPicker.TryPick(w.Heightmap, new(o.X, o.Y, o.Z), new(d.X, d.Y, d.Z), out System.Numerics.Vector3 hit), "click-through: no ground under the notice")) return;
        g.WorldToCell(new System.Numerics.Vector2(hit.X, hit.Z), out int cx, out int cy);
        int before = DefeatStaging.UnitsOf(w, 0).Count;
        GetTree().Paused = false;
        Spawn(sim, 0, Unit(w, 0, UnitSlot.Line), cx, cy);
        await Until(sim, () => DefeatStaging.UnitsOf(w, 0).Count > before, "click-through: the unit");
        GetTree().Paused = true;
        await Frames(2);
        EntityHandle unit = DefeatStaging.UnitsOf(w, 0).Last();
        System.Numerics.Vector2 p = w.Units.Position[unit.Index];
        Vector2 screen = camera.UnprojectPosition(new Vector3(p.X, hit.Y, p.Y));
        Check(label.GetGlobalRect().HasPoint(screen) && match.DrawNotice.Shown, $"click-through: the unit at {screen} is not under the notice {label.GetGlobalRect()}");
        GetTree().Paused = false; // the match's input runs with the tree
        await ClickAt(screen);
        GetTree().Paused = true;
        var selection = match.GetNode<SelectionController>("SelectionController");
        Check(selection.Selection.Count == 1 && selection.Selection.Contains(unit), $"click-through: the click on the notice selected {selection.Selection.Count} units (want the one under it)");
    }

    // The stats screen (from the banner, else from the pause menu): its title, the defeat cells, and no winner named.
    private async Task StatsAgree(Match match, string how)
    {
        await Click(_main.Banner!.StatsButton);
        CheckStats(match, how);
        await Type(Key.Escape);
        Check(!_main.Stats!.Visible && _main.BannerUp, $"{how}: Esc on the stats");
    }

    private void CheckStats(Match match, string how)
    {
        MatchStats stats = _main.Stats!;
        World w = match.Simulation!.World;
        int defeated = Array.FindIndex(MatchStats.Columns, c => c.Label == MatchOverText.DefeatedAt);
        Check(stats.Visible && stats.Title.Text == _ui.MatchOver(MatchOverText.StatsTitle) && stats.RowCount == w.Config.PlayerCount,
            $"{how}: stats visible {stats.Visible}, title '{stats.Title.Text}', {stats.RowCount} rows");
        for (int p = 0; p < stats.RowCount; p++)
        {
            string want = w.IsDefeated(p) ? _ui.Clock(match.DefeatedAt(p)) : _ui.MatchOver(MatchOverText.Standing);
            Check(stats.Cell(p, defeated) == want, $"{how}: row {p} defeated '{stats.Cell(p, defeated)}' (want '{want}')");
        }
        string victory = _ui.MatchOver(MatchOverText.Victory);
        var named = new List<string>();
        Walk(stats, n => { if (n is Label l && l.Visible && l.Text.Contains(victory, StringComparison.Ordinal)) named.Add(l.Text); });
        Check(named.Count == 0, $"{how}: the stats screen names a winner: [{string.Join(", ", named)}]");
    }

    private static void Walk(Node n, Action<Node> visit)
    {
        visit(n);
        foreach (Node c in n.GetChildren()) Walk(c, visit);
    }

    // ---- set-up (commands only) ----

    private string Forge(string sub, bool rule)
    {
        string from = ProjectSettings.GlobalizePath("res://data"), to = Path.Combine(_root, sub);
        if (Directory.Exists(to)) Directory.Delete(to, true);
        foreach (string dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
        string rules = Path.Combine(to, "common", "rules.json");
        JsonNode root = JsonNode.Parse(File.ReadAllText(rules))!;
        foreach (JsonNode? preset in root["startingResources"]!.AsArray())
            if ((string?)preset!["id"] == StartingResourcesDef.StandardId) preset["gold"] = 0;
        if (rule) root["draw"] = new JsonObject { ["afterSeconds"] = DrawSeconds };
        else root.AsObject().Remove("draw");
        File.WriteAllText(rules, root.ToJsonString());
        DataLoadResult loaded = DataLoader.LoadAll(to);
        if (!loaded.Ok) throw new InvalidOperationException($"the {sub} copy failed to load: {string.Join("; ", loaded.Errors.Take(5))}");
        return to;
    }

    private Main NewMain(string data)
    {
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.UserArgs = new[] { Main.NoConfineFlag }; // the menu: every match below is a recorded skirmish (StartSkirmish)
        main.DataPath = data;
        main.SettingsPath = Path.Combine(_root, $"settings-{Path.GetFileName(data)}.cfg");
        main.ReplayDir = Path.Combine(_root, "replays");
        main.SaveDir = Path.Combine(_root, "saves");
        main.BeforeStart = Setup;
        return main;
    }

    // Every match: 5 ticks a frame whatever the frame time (the clock's cap), so the scene's waits are short and steady.
    private static void Setup(Match m) => m.GetNode<SimRunner>("SimRunner").FixedFrameDelta = 0.25;

    private Match Watch(Match? match)
    {
        if (match?.Simulation == null) throw new InvalidOperationException("the shell started no match");
        _ended = 0;
        match.Ended += _ => _ended++;
        _mon = new Monitor(this, match);
        match.GetNode<SimRunner>("SimRunner").Ticked += _mon.OnTicked;
        match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
        return match;
    }

    private async Task Halls(Simulation sim, params (int Player, int X, int Y)[] at)
    {
        World w = sim.World;
        foreach ((int p, int x, int y) in at)
        {
            int hall = DefeatStaging.Hall(_data, w, p);
            DefeatStaging.SpawnAt(sim, p, hall, DefeatStaging.Anchor(w, p, hall, x, y));
        }
        await Ticks(sim, 3);
        Check(w.Buildings.Count == at.Length, $"{w.Buildings.Count} halls placed (want {at.Length})");
    }

    private int Unit(World w, int player, UnitSlot slot) => StartBase.UnitOfSlot(_data, w.FactionOf(player), slot);

    /// <summary>
    /// M6-SH3 (the draw rule's reach, staging only): a finished drop-off (the faction's Camp-slot building) for player 0 beside
    /// its hall, after <see cref="Halls"/>. Since M6-SH3 a seat's worker counts the gold left on the map only with a finished
    /// drop-off of its own, and these scenes give player 0 a worker so that it can still train; with no worker the drop-off
    /// changes nothing. It is not a unit-producing building, so it neither keeps the seat in the match nor is its hall.
    /// </summary>
    private void DropOffFor0(Simulation sim)
    {
        World w = sim.World;
        NavGrid g = w.NavGrid;
        int camp = StartBase.BuildingOfSlot(_data, w.FactionOf(0), BuildingSlot.Camp);
        DefeatStaging.SpawnAt(sim, 0, camp, DefeatStaging.Anchor(w, 0, camp, g.Width / 4, g.Height / 2 - 8));
    }

    private static void Spawn(Simulation sim, int player, int type, int x, int y)
    {
        NavGrid g = sim.World.NavGrid;
        int c = FlowField.NearestPassable(g, Math.Clamp(y, 0, g.Height - 1) * g.Width + Math.Clamp(x, 0, g.Width - 1));
        sim.Enqueue(Command.SpawnUnit(player, type, g.CellCenter(c % g.Width, c / g.Width)));
    }

    // The killer's siege units raze the victim's only hall and its Line units kill the victim's units: the victim is out.
    private async Task KnockOut(Simulation sim, int killer, int victim)
    {
        World w = sim.World;
        NavGrid g = w.NavGrid;
        EntityHandle hall = BuildingOf(w, victim);
        int cell = w.Buildings.Cell[hall.Index];
        int siege = Unit(w, killer, UnitSlot.Siege);
        for (int i = 0; i < 8; i++) Spawn(sim, killer, siege, cell % g.Width - 4 + i % 4 * 2, cell / g.Width - 5 - i / 4 * 2);
        DefeatStaging.SpawnKillers(sim, _data, killer, victim);
        await Ticks(sim, 2);
        foreach (EntityHandle u in DefeatStaging.UnitsOf(w, killer))
            if (w.Units.TypeId[u.Index] == siege) sim.Enqueue(Command.Attack(killer, u, hall, isBuilding: true));
        List<EntityHandle> theirs = DefeatStaging.UnitsOf(w, victim);
        foreach (EntityHandle u in DefeatStaging.UnitsOf(w, killer))
            if (w.Units.TypeId[u.Index] != siege && theirs.Count > 0) sim.Enqueue(Command.Attack(killer, u, theirs[0], isBuilding: false));
    }

    private static System.Numerics.Vector2 HallCenter(World w, int player)
    {
        EntityHandle h = BuildingOf(w, player);
        return StartBase.FootprintCenter(w.NavGrid, w.Data.Buildings[w.Buildings.TypeId[h.Index]], w.Buildings.Cell[h.Index]);
    }

    private static EntityHandle BuildingOf(World w, int player)
    {
        BuildingStore b = w.Buildings;
        for (int i = 0; i < b.Capacity; i++) if (b.Alive[i] && b.Owner[i] == player) return new EntityHandle(i, b.Generation[i]);
        throw new InvalidOperationException($"player {player} has no building");
    }

    private async Task UntilBanner(Simulation sim, string how)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!_main.BannerUp && clock.Elapsed.TotalSeconds < 120) await Frames(1);
        Check(_main.BannerUp && _ended == 1, $"{how}: banner {_main.BannerUp}, ended {_ended} by tick {sim.TickNumber}");
        if (!_main.BannerUp) throw new InvalidOperationException($"{how}: no banner by tick {sim.TickNumber}");
    }

    private async Task Until(Simulation sim, Func<bool> done, string how, Func<string>? why = null)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && clock.Elapsed.TotalSeconds < 120) await Frames(1);
        if (!done()) throw new InvalidOperationException($"{how}: not reached by tick {sim.TickNumber} {why?.Invoke()}");
    }

    private async Task Ticks(Simulation sim, int n)
    {
        int until = sim.TickNumber + n;
        await Until(sim, () => sim.TickNumber >= until, $"tick {until}");
    }

    // ---- input ----

    private async Task Click(Control c)
    {
        Check(c.IsVisibleInTree(), $"{c.Name} is not visible");
        await ClickAt(c.GetGlobalRect().GetCenter());
    }

    private async Task ClickAt(Vector2 at)
    {
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

    private async Task Shot(string name)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = $"{_shots}/draw-{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"  screenshot {path}");
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }

    /// <summary>
    /// Every tick of one match, from <see cref="SimRunner.Ticked"/> (after the notice's own handler): the notice shows iff the
    /// clock runs, with the text for <c>ceil(DrawTicksLeft / 20)</c>; each start and stop of the clock, the stop's cause; the
    /// shown second per tick (-1 hidden) for the playback comparison; the label's text writes against the second's changes.
    /// </summary>
    private sealed class Monitor
    {
        private readonly DrawViewTest _test;
        private readonly Match _match;
        private int _prevLeft = -1, _lastSec, _writes, _baseWrites, _misses;

        public readonly List<(int Tick, int Left, string Text)> Starts = new();
        public readonly List<(int Tick, string Cause)> Stops = new();
        public readonly List<int> Seq = new();
        public int ShownTicks;
        public bool SawLast;

        public Monitor(DrawViewTest test, Match match)
        {
            _test = test;
            _match = match;
            World w = match.Simulation!.World;
            _prevLeft = w.DrawTicksLeft;
            _lastSec = match.DrawNotice!.ShownSeconds;
            _baseWrites = match.DrawNotice.TextWrites;
        }

        public void OnTicked(Simulation sim)
        {
            World w = sim.World;
            DrawNotice n = _match.DrawNotice!;
            int left = w.DrawTicksLeft;
            bool want = left >= 0;
            int sec = want ? (left + SimConstants.TicksPerSecond - 1) / SimConstants.TicksPerSecond : -1;
            string text = want ? string.Format(_test._ui.Hud(HudText.DrawCountdown), _test._ui.Clock(sec * SimConstants.TicksPerSecond)) : "";
            if (n.Shown != want || want && n.TextLabel!.Text != text)
                Miss($"tick {sim.TickNumber}: left {left}, shown {n.Shown}, '{n.TextLabel?.Text}' (want {(want ? $"'{text}'" : "hidden")})");
            if (want)
            {
                ShownTicks++;
                if (sec != _lastSec) { _writes++; _lastSec = sec; }
                if (_prevLeft < 0) Starts.Add((sim.TickNumber, left, n.TextLabel!.Text));
                if (left == 1) SawLast |= n.TextLabel!.Text == string.Format(_test._ui.Hud(HudText.DrawCountdown), "0:01");
            }
            else if (_prevLeft >= 0)
                Stops.Add((sim.TickNumber, w.MatchDrawn ? "draw" : w.MatchOver ? "win" : !w.MatchSpent ? "can-train" : "death"));
            Seq.Add(sec);
            _prevLeft = left;
        }

        // The label's text writes equal the shown second's changes (once per change, never per tick).
        public void Finish()
        {
            int writes = _match.DrawNotice!.TextWrites - _baseWrites;
            if (writes != _writes) _test.Check(false, $"the notice's text was written {writes} times for {_writes} changes of the shown second");
            if (_misses > 0) _test.Check(false, $"{_misses} ticks with the notice wrong (first ones above)");
        }

        private void Miss(string what)
        {
            if (_misses++ < 5) _test.Check(false, what);
        }
    }
}
