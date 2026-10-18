using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Replays;

namespace Rts.Game.Tests;

/// <summary>
/// M6-V9: every shell match is recorded, and Load Game plays a recording back through the match scene.
/// (a) A 600-tick <c>--skirmish</c> match (2 seats, slot 2 an AI) through the hosted shell, with the human's orders scripted
/// at ticks 100 / 200 / 300 (the S key and a right click as live controls: both send): Quit to menu writes
/// <c>&lt;yyyyMMdd-HHmmss&gt;-seed&lt;seed&gt;.replay</c> to the test's folder, whose header holds the seed, the seats and 600
/// ticks; Load Game lists it with that seed, seats and length; Watch plays it to the end through <see cref="SimRunner"/>:
/// every checkpoint equal, "Replay ended" on the banner without Continue watching, the playback sim's command lines byte-equal
/// to the file's, no file written by the playback; Main menu back to the menu.
/// (b) A 3-seat match with two AIs the same way.
/// (c) A copy whose <c>data-hash</c> line is altered (checksum re-sealed) is refused before any match exists, with
/// <c>load.refused</c> on the Load Game screen; <see cref="SimRunner.StartPlayback"/> returns <c>DataMismatch</c> and builds no sim.
/// (d) A copy with the tick-300 checkpoint altered stops at tick 300 with "Replay out of sync" and runs no further tick.
/// (e) A junk <c>.replay</c> and a cut copy are counted by the list and listed as unreadable rows (a <c>.txt</c> is not
/// looked at); Delete removes a row's file; <see cref="ReplayStore.Prune"/> keeps the newest <see cref="ReplayStore.MaxReplays"/>.
/// (g) BUG-0830, the folder's edge cases: unreadable files hold no cap slot and Load Game deletes one; a recording saved into a
/// folder of future-dated names survives its own prune; the list reads headers only (a copy with a damaged body is listed,
/// then refused on Watch); one path separator; the playback shows <c>load.playing</c> and a live match doesn't.
/// (f) During (a)'s playback: selection works, the card is greyed, and the right click, A (+ click), S, H, the card's presses
/// and every public order call send nothing (no issued count, the playback log a prefix of the file's all the way).
/// Also <c>--replay &lt;path&gt;</c> (a file plays at boot; a missing one shows the refusal), a hosted shell with no folder set
/// writes nothing, and the player's own <c>user://replays</c> folder is unchanged over the run (BUG-0770's rule).
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/ReplayPlaybackTest.tscn</c>; prints "REPLAY PLAYBACK TEST PASS" or "REPLAY PLAYBACK TEST FAIL: ..." lines.</remarks>
public partial class ReplayPlaybackTest : Node
{
    /// <summary>Game time each frame advances every runner here (<see cref="SimRunner.FixedFrameDelta"/>): five ticks, the clock's cap, so the run is the same however loaded the machine is.</summary>
    public const double FrameSeconds = 0.25;

    /// <summary>Ticks each recorded match runs before Quit to menu (brief M6-V9: a 600-tick match).</summary>
    public const int RecordTicks = 600;

    /// <summary>The checkpoint (d) alters.</summary>
    public const int AlteredTick = 300;

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private UiText _ui = null!;
    private string _dir = "", _full = "";
    private string? _shots;
    private Vector2I _window;
    private int[] _liveOpenings = Array.Empty<int>();
    private readonly List<string> _notes = new();

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // the shell pauses the tree
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            string[] args = OS.GetCmdlineUserArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--shots") _shots = args[i + 1];
            if (StatsScreenCheck.TryWindow(args, out _window)) GetTree().Root.Size = _window; // M6-V11: the stats screenshots' size
            await Frames(1);
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            _ui = UiText.Shared ?? throw new InvalidOperationException("ui.json failed to load");
            // BUG-0770's rule: a per-process folder, never the player's user://replays.
            _dir = $"user://replay-playback-test-{OS.GetProcessId()}";
            _full = Path.GetFullPath(ProjectSettings.GlobalizePath(_dir)); // as ReplayStore.NewPath writes it
            if (Directory.Exists(_full)) Directory.Delete(_full, true);
            string playerBefore = PlayerFolder();

            UiRows();
            (string? two, Replay? twoReplay) = await RecordAndWatch(new[] { "--skirmish", "--mute" }, "two", scripted: true, aiSeats: 1);
            await RecordAndWatch(new[] { "--skirmish", "--players", "3", "--mute" }, "three", scripted: false, aiSeats: 2);
            if (two != null && twoReplay != null)
            {
                await RefusedHash(two, twoReplay);
                await AlteredCheckpoint(two);
                await ReplayFlag(two);
                await JunkDeleteAndPrune(two);
                await FolderEdgeCases(two);
            }
            await HostedWithoutFolder();
            await OpeningRows();

            Check(PlayerFolder() == playerBefore, "the player's user://replays folder changed over the run (BUG-0770's rule)");
            try { Directory.Delete(_full, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        foreach (string n in _notes) GD.Print($"  note: {n}");
        foreach (string f in _failures.Take(60)) GD.Print($"REPLAY PLAYBACK TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("REPLAY PLAYBACK TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- rows ----

    private void UiRows()
    {
        foreach (LoadText k in Enum.GetValues<LoadText>()) Check(_ui.LoadGame(k).Length > 0, $"ui.json load.{UiText.Key(k)} is empty");
        Check(_ui.Menu(MenuText.Load).Length > 0, "ui.json menu.load is empty");
        Check(ReplayStore.FileName(new DateTime(2026, 10, 16, 20, 36, 5), 7) == "20261016-203605-seed7.replay"
            && ReplayStore.FileName(new DateTime(2026, 10, 16, 20, 36, 5), 7, 1) == "20261016-203605-seed7-2.replay", "ReplayStore.FileName");
        Check(ReplayStore.DateOf("C:/x/20261016-203605-seed7-2.replay") == new DateTime(2026, 10, 16, 20, 36, 5), "ReplayStore.DateOf from the name");
        Check(_ui.ReplayDate(new DateTime(2026, 10, 16, 20, 36, 5)) != _ui.ReplayDate(new DateTime(2026, 10, 17, 21, 36, 5)), "load.date_item names no date");
    }

    // (a) / (b), and (f) in (a)'s playback: record a match through the shell, list it, watch it to the end.
    private async Task<(string?, Replay?)> RecordAndWatch(string[] args, string label, bool scripted, int aiSeats)
    {
        Main main = NewMain(args, label);
        AddChild(main);
        await Frames(2);
        try
        {
            Match? match = main.CurrentMatch;
            if (!Check(match != null && !match.IsPlayback, $"{label}: {string.Join(' ', args)} started no live match")) return (null, null);
            SimRunner runner = match!.GetNode<SimRunner>("SimRunner");
            Simulation sim = runner.Simulation!;
            Check(runner.Recorder is { CheckpointInterval: ReplayRecorder.DefaultCheckpointInterval }, $"{label}: the shell match records no replay (interval {runner.Recorder?.CheckpointInterval})");
            Check(match.GetNodeOrNull("Hud/ReplayMark") == null, $"{label}: a live match shows the replay mark");
            string? path = main.ReplayPath;
            ulong seed = sim.World.Config.Seed;
            if (!Check(path != null && path.StartsWith(_full, StringComparison.Ordinal), $"{label}: replay path '{path}' not in the test folder {_full}")) return (null, null);
            Check(Regex.IsMatch(Path.GetFileName(path!), $"^\\d{{8}}-\\d{{6}}-seed{seed}\\.replay$"), $"{label}: file name {Path.GetFileName(path)}");
            Check(runner.Seats.Count(s => s.Controller == SeatController.Ai) == aiSeats, $"{label}: seats {string.Join(", ", runner.Seats)}");

            var sel = match.GetNode<SelectionController>("SelectionController");
            int step = 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (sim.TickNumber < RecordTicks && clock.Elapsed.TotalSeconds < 120)
            {
                await Frames(1);
                if (!scripted) continue;
                if (step == 0 && sim.TickNumber >= 100) { step++; await LiveOrders(sel, sim, label); }
                else if (step == 1 && sim.TickNumber >= 200) { step++; Train(sel, sim, label); }
                else if (step == 2 && sim.TickNumber >= 300)
                {
                    step++;
                    EntityHandle w = OwnUnit(sim);
                    sel.SelectOnly(w);
                    System.Numerics.Vector2 at = sim.World.Units.Position[w.Index];
                    sel.Order(CommandKind.Move, new Vector2(at.X - 6f, at.Y + 4f), queued: false);
                }
            }
            int recorded = sim.TickNumber;
            Check(recorded == RecordTicks, $"{label}: stopped at tick {recorded}, want {RecordTicks} (five ticks a frame)");
            ImmutableArray<PlayerSeat> seats = runner.Seats;
            // M6-V11: the live match's counters at its end, which the watched replay's stats screen must show recomputed.
            World lw = sim.World;
            var live = Enumerable.Range(0, lw.Config.PlayerCount)
                .Select(p => (lw.GatheredGold[p], lw.UnitsTrained[p], lw.ArmyValueSamples(p).ToArray())).ToArray();
            _liveOpenings = Enumerable.Range(0, lw.Config.PlayerCount).Select(p => sim.AiOpening(p)).ToArray(); // M6-V13
            main.QuitToMenu();
            await Frames(2);
            Check(main.CurrentMatch == null && main.Menu is { Visible: true }, $"{label}: Quit to menu state");

            // The file: the match's header and length.
            if (!Check(File.Exists(path), $"{label}: Quit to menu wrote no {path}")) return (null, null);
            ReplayError read = ReplayFormat.TryReadFile(path!, out Replay? replay);
            if (!Check(read == ReplayError.None && replay != null, $"{label}: the written file reads back as {read}")) return (null, null);
            Check(replay!.Seed == seed && replay.TickCount == recorded && replay.PlayerCount == seats.Length && replay.CheckpointInterval == ReplayRecorder.DefaultCheckpointInterval
                && replay.Checkpoints.Length == recorded / ReplayRecorder.DefaultCheckpointInterval && replay.DataHash == _data.ContentHash(),
                $"{label}: header seed {replay.Seed}, ticks {replay.TickCount}, players {replay.PlayerCount}, interval {replay.CheckpointInterval}, checkpoints {replay.Checkpoints.Length}");
            Check(replay.Seats.Length == seats.Length && seats.Select((s, p) => replay.Seats[p] == new ReplaySeat(_data.Factions[s.Faction].Key, s.Controller, s.Difficulty)).All(b => b),
                $"{label}: header seats [{string.Join(", ", replay.Seats)}], played [{string.Join(", ", seats)}]");
            if (scripted)
            {
                int human = replay.Commands.Count(c => c.Player == SelectionController.LocalPlayer && c.Tick > 1);
                Check(human >= 3, $"{label}: {human} scripted human commands in the log, want 3 or more");
            }

            // Criterion 1: Load Game lists it with the right seed, seats and length.
            Check(main.Menu!.LoadButton.Text == _ui.Menu(MenuText.Load), $"menu Load Game text '{main.Menu.LoadButton.Text}'");
            main.Menu.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(1);
            LoadGameScreen load = main.LoadScreen!;
            Check(load.Visible && !main.Menu.Visible, $"{label}: Load Game did not open (screen {load.Visible}, menu {main.Menu.Visible})");
            int row = load.Entries.ToList().FindIndex(e => SamePath(e.Path, path!));
            if (!Check(row >= 0, $"{label}: Load Game does not list {path} (rows {load.Entries.Count})")) return (path, replay);
            ReplayEntry entry = load.Entries[row];
            TreeItem item = load.List.GetRoot().GetChild(row);
            string wantSeats = load.SeatsText(replay.Seats);
            Check(entry.Seed == seed && entry.TickCount == recorded && entry.Seats.SequenceEqual(replay.Seats), $"{label}: entry seed {entry.Seed}, ticks {entry.TickCount}");
            Check(item.GetText(1) == seed.ToString(CultureInfo.InvariantCulture) && item.GetText(2) == wantSeats && item.GetText(3) == _ui.Clock(recorded)
                && item.GetText(3) == "0:30" && item.GetText(0) == _ui.ReplayDate(ReplayStore.DateOf(path!)),
                $"{label}: row [{item.GetText(0)} | {item.GetText(1)} | {item.GetText(2)} | {item.GetText(3)}]");
            for (int p = 0; p < seats.Length; p++)
                Check(wantSeats.Contains(_data.Factions[seats[p].Faction].DisplayName, StringComparison.Ordinal), $"{label}: players cell '{wantSeats}' lacks seat {p}'s faction");
            Check(wantSeats.Split(_ui.LoadGame(LoadText.SeatSeparator)).Length == seats.Length
                && wantSeats.StartsWith(_ui.ReplaySeat(_data.Factions[seats[0].Faction].DisplayName, _ui.Setup(SetupText.SlotHuman)), StringComparison.Ordinal),
                $"{label}: players cell '{wantSeats}'");
            Check(load.Select(row) && !load.WatchButton.Disabled && !load.DeleteButton.Disabled, $"{label}: picking row {row} did not enable Watch / Delete");

            await Watch(main, load, path!, replay, label, ordersRow: scripted, live);
            return (path, replay);
        }
        finally
        {
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
    }

    // Watch through the screen's button, play to the end, check every checkpoint, the banner, the log; Main menu back.
    private async Task Watch(Main main, LoadGameScreen load, string path, Replay replay, string label, bool ordersRow, (int Gold, int Trained, int[] Samples)[] live)
    {
        int files = Directory.GetFiles(_full).Length;
        load.WatchButton.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        Match? match = main.CurrentMatch;
        if (!Check(match != null && match.IsPlayback && SamePath(match.ReplayPath!, path) && !load.Visible, $"{label}: Watch started no playback")) return;
        SimRunner runner = match!.GetNode<SimRunner>("SimRunner");
        Simulation sim = runner.Simulation!;
        Check(runner.Playback != null && runner.Seats.All(s => s.Controller == SeatController.Human) && !sim.World.Config.HasAiSeat,
            $"{label}: playback seats [{string.Join(", ", runner.Seats)}] (every seat must be human: no AI runs)");
        Check(runner.PlayedSeats.Select(s => s.Controller).SequenceEqual(replay.Seats.Select(s => s.Controller)) && match.Seats.SequenceEqual(runner.PlayedSeats),
            $"{label}: played seats [{string.Join(", ", runner.PlayedSeats)}]");
        Check(main.ReplayPath == null, $"{label}: the playback records to {main.ReplayPath}");
        // BUG-0830: the playback is marked on screen with load.playing.
        Label? mark = match.GetNodeOrNull<Label>("Hud/ReplayMark");
        Check(mark != null && mark.IsVisibleInTree() && mark.Text == _ui.LoadGame(LoadText.Playing), $"{label}: playback mark '{mark?.Text ?? "(none)"}', visible {mark?.IsVisibleInTree()}");
        bool ordersDone = false;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (runner.PlaybackState == PlaybackState.Playing && clock.Elapsed.TotalSeconds < 120)
        {
            await Frames(1);
            if (ordersRow && !ordersDone && sim.TickNumber >= 120 && !GetTree().Paused)
            {
                ordersDone = true;
                await NoOrders(match, runner, replay);
            }
        }
        Check(!ordersRow || ordersDone, $"{label}: the no-orders row (f) did not run");
        Check(runner.PlaybackState == PlaybackState.Ended && runner.PlaybackError == ReplayError.None && sim.TickNumber == replay.TickCount,
            $"{label}: playback {runner.PlaybackState} ({runner.PlaybackError}) at tick {sim.TickNumber} of {replay.TickCount}");
        Check(runner.CheckpointsCompared == replay.Checkpoints.Length && runner.Recorder!.CheckpointCount == replay.Checkpoints.Length
            && Enumerable.Range(0, replay.Checkpoints.Length).All(i => runner.Recorder.CheckpointAt(i) == replay.Checkpoints[i]),
            $"{label}: {runner.CheckpointsCompared} of {replay.Checkpoints.Length} checkpoints compared equal");
        string[] fileLines = CommandLines(File.ReadAllBytes(path)), playLines = CommandLines(ReplayFormat.Write(runner.Recorder!.ToReplay()));
        Check(fileLines.Length == replay.Commands.Length && playLines.SequenceEqual(fileLines),
            $"{label}: the playback sim's command lines ({playLines.Length}) differ from the file's ({fileLines.Length}); first difference at {FirstDifference(playLines, fileLines)}");
        MatchOverBanner banner = main.Banner!;
        Check(banner.Visible && GetTree().Paused && !banner.ContinueButton.Visible && banner.MenuButton.Visible
            && banner.Title.Text == _ui.LoadGame(LoadText.Ended), $"{label}: end banner visible {banner.Visible}, title '{banner.Title.Text}', continue {banner.ContinueButton.Visible}");
        // M6-V11: the banner's Stats shows the playback sim's counters, recomputed from the log: the live match's at its end.
        banner.StatsButton.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        MatchStats stats = main.Stats!;
        int gold = Array.FindIndex(MatchStats.Columns, c => c.Label == MatchOverText.GatheredGold), trained = Array.FindIndex(MatchStats.Columns, c => c.Label == MatchOverText.UnitsTrained);
        Check(stats.Visible && stats.RowCount == live.Length, $"{label}: replay stats visible {stats.Visible}, {stats.RowCount} rows (want {live.Length})");
        for (int p = 0; p < Math.Min(live.Length, stats.RowCount); p++)
            Check(stats.Cell(p, gold) == live[p].Gold.ToString(CultureInfo.InvariantCulture) && stats.Cell(p, trained) == live[p].Trained.ToString(CultureInfo.InvariantCulture)
                && stats.Graph.GraphPoints(p).Length == live[p].Samples.Length && sim.World.ArmyValueSamples(p).SequenceEqual(live[p].Samples),
                $"{label}: replay stats row {p}: gold '{stats.Cell(p, gold)}' (live {live[p].Gold}), trained '{stats.Cell(p, trained)}' (live {live[p].Trained}), {stats.Graph.GraphPoints(p).Length} graph points (live {live[p].Samples.Length} samples)");
        Check(live.Any(l => l.Gold > 0 && l.Trained > 0) && live.All(l => l.Samples.Length == RecordTicks / sim.World.StatsSampleTicks),
            $"{label}: the live match gathered / trained nothing or has {string.Join(", ", live.Select(l => l.Samples.Length))} samples (want {RecordTicks / sim.World.StatsSampleTicks} each)");
        foreach (string f in StatsScreenCheck.Columns(stats, sim.World, _ui, $"{label} replay stats")) Check(false, f);
        foreach (string f in StatsScreenCheck.Graph(stats, match, drawn: true, $"{label} replay stats")) Check(false, f);
        // M6-V13: the "opened with" lines of the watched replay (every seat human in its sim) are the live match's.
        Check(_liveOpenings.Count(o => o >= 0) == replay.Seats.Count(x => x.Controller == SeatController.Ai), $"{label}: live openings [{string.Join(", ", _liveOpenings)}]");
        foreach (string f in StatsScreenCheck.Openings(stats, _data, sim.World, _ui, _liveOpenings, $"{label} replay stats")) Check(false, f);
        await Shot($"replay-stats-{label}");
        main.CloseStats();
        Check(!stats.Visible && banner.Visible, $"{label}: closing the replay's stats: stats {stats.Visible}, banner {banner.Visible}");
        main.ContinueWatching();
        Check(banner.Visible && GetTree().Paused, $"{label}: Continue watching closed a finished replay's banner");
        Check(Directory.GetFiles(_full).Length == files, $"{label}: the playback wrote a file");
        banner.MenuButton.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        Check(main.CurrentMatch == null && main.Menu is { Visible: true } && !GetTree().Paused && !banner.Visible, $"{label}: the banner's Main menu did not end the playback");
        Check(Directory.GetFiles(_full).Length == files, $"{label}: Quit to menu after a playback wrote a file");
    }

    // (f): while the replay plays, the selection works and nothing the player does reaches the sim.
    private async Task NoOrders(Match match, SimRunner runner, Replay replay)
    {
        Simulation sim = runner.Simulation!;
        var sel = match.GetNode<SelectionController>("SelectionController");
        var card = match.GetNode<CommandCard>("Hud/CommandCard");
        Check(!sel.OrdersEnabled && card.ReadOnly, $"(f) orders enabled {sel.OrdersEnabled}, card read-only {card.ReadOnly}");
        EntityHandle w = OwnUnit(sim);
        Check(w.Generation != 0 && sel.SelectOnly(w) && sel.Selection.Count == 1, "(f) no own unit could be selected in playback");
        await Frames(1);
        var shown = Enumerable.Range(0, CommandCard.Cells).Where(i => card.ButtonAt(i).Visible).ToList();
        Check(shown.Count >= 4 && shown.All(i => card.ButtonAt(i).Disabled), $"(f) card: {shown.Count} buttons shown, enabled [{string.Join(", ", shown.Where(i => !card.ButtonAt(i).Disabled))}]");
        int issued = Issued(sel), dropped = sel.DroppedOrders;

        // The keys and the mouse, as the player sends them.
        PushKey(Key.S);
        PushKey(Key.H);
        PushKey(Key.A);
        await Frames(1);
        Check(!sel.Targeting, "(f) A armed targeting in playback");
        if (sel.TryScreenPosition(w.Index, out Vector2 at))
        {
            Vector2 ground = at + new Vector2(60f, 0f);
            Push(new InputEventMouseMotion { Position = ground, GlobalPosition = ground });
            Push(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = ground, GlobalPosition = ground, ButtonMask = MouseButtonMask.Right });
            Push(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = ground, GlobalPosition = ground });
            await Frames(1);
        }
        else Check(false, "(f) the selected unit is not on screen for the right click");
        sel.SelectOnly(w);
        // Every public order call and every card press.
        System.Numerics.Vector2 p = sim.World.Units.Position[w.Index];
        sel.Order(CommandKind.Stop, null, false);
        sel.Order(CommandKind.HoldPosition, null, false);
        sel.Order(CommandKind.Move, new Vector2(p.X + 4f, p.Y), false);
        sel.ContextOrder(new Vector2(p.X + 4f, p.Y), false);
        sel.BeginAttackMove();
        sel.BeginMove();
        Check(!sel.Targeting, "(f) BeginAttackMove / BeginMove armed in playback");
        Check(!sel.AttackOrder(w, false, false) && !sel.AttackMoveClick(at) && !sel.CommandAt(at, false) && !sel.OrderAt(CommandKind.Move, at, false)
            && sel.OrderBuild(0, 0, false) == 0 && !sel.AbilityOrder(0, p, false) && !sel.SelfAbilityOrder(0, false) && !sel.BeginAbility(0),
            "(f) an order call reported success in playback");
        for (int i = 0; i < CommandCard.Cells; i++) card.Press(i);
        Check(!card.OpenMenu(false) && !card.MenuOpen && !card.GhostActive, "(f) a build menu opened in playback");
        int hall = OwnBuilding(sim);
        if (hall >= 0 && sel.SelectBuilding(hall))
        {
            Check(!sel.Produce(0, false) && !sel.RallyOrder(p) && !sel.CancelQueueItem(0) && !sel.CancelSelectedSite(), "(f) a building order reported success in playback");
            for (int i = 0; i < CommandCard.Cells; i++) card.Press(i);
        }
        await Frames(2);
        Check(Issued(sel) == issued && issued == 0 && sel.DroppedOrders == dropped, $"(f) orders issued in playback: {Issued(sel) - issued} (issued before {issued})");
        // The playback log is a prefix of the file's: nothing but the logged commands went in.
        ReplayRecorder rec = runner.Recorder!;
        bool prefix = rec.CommandCount <= replay.Commands.Length;
        for (int i = 0; prefix && i < rec.CommandCount; i++)
        {
            Command c = rec.CommandAt(i);
            Command f = replay.Commands[i];
            prefix = c.Tick == f.Tick && c.Player == f.Player && c.Sequence == f.Sequence && c.Kind == f.Kind && c.Unit.Equals(f.Unit) && c.Position == f.Position;
        }
        int due = replay.Commands.Count(c => c.Tick <= sim.TickNumber + 1);
        Check(prefix && rec.CommandCount == due && sim.PendingCommandCount == replay.Commands.Count(c => c.Tick == sim.TickNumber + 1),
            $"(f) playback log {rec.CommandCount} commands (want the file's first {due}), prefix {prefix}, pending {sim.PendingCommandCount}");
    }

    // (a)'s live controls at tick 100: the same S key and right click do send in a live match.
    private async Task LiveOrders(SelectionController sel, Simulation sim, string label)
    {
        EntityHandle w = OwnUnit(sim);
        if (!Check(w.Generation != 0 && sel.SelectOnly(w), $"{label}: no own worker to order")) return;
        int stops = sel.IssuedCount(CommandKind.Stop);
        PushKey(Key.S);
        await Frames(1);
        Check(sel.IssuedCount(CommandKind.Stop) == stops + 1, $"{label}: the live control S sent {sel.IssuedCount(CommandKind.Stop) - stops} Stops (want 1)");
        int issued = Issued(sel);
        if (sel.TryScreenPosition(w.Index, out Vector2 at))
        {
            Vector2 ground = at + new Vector2(60f, 0f);
            Push(new InputEventMouseMotion { Position = ground, GlobalPosition = ground });
            Push(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = ground, GlobalPosition = ground, ButtonMask = MouseButtonMask.Right });
            Push(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = ground, GlobalPosition = ground });
            await Frames(1);
            Check(Issued(sel) > issued, $"{label}: the live control right click sent nothing");
        }
        else Check(false, $"{label}: the worker is not on screen for the live right click");
    }

    // (a) at tick 200: the hall trains a worker (a Train in the log).
    private void Train(SelectionController sel, Simulation sim, string label)
    {
        int hall = OwnBuilding(sim);
        int worker = sim.World.Units.TypeId[OwnUnit(sim).Index];
        Check(hall >= 0 && sel.SelectBuilding(hall) && sel.Produce(worker, isTech: false), $"{label}: the hall did not train a worker at tick {sim.TickNumber}");
    }

    // (c): a copy with another data hash is refused before any match exists.
    private async Task RefusedHash(string path, Replay replay)
    {
        string copy = Path.Combine(_full, "altered-hash.replay");
        ulong other = replay.DataHash ^ 0x1UL;
        File.WriteAllBytes(copy, Reseal(ReplaceLine(File.ReadAllText(path), "data-hash ", $"data-hash {other:X16}")));
        Check(ReplayFormat.TryReadFile(copy, out Replay? altered) == ReplayError.None && altered!.DataHash == other, "(c) the altered copy does not read (it must: only the data hash differs)");
        var runner = new SimRunner();
        Check(runner.StartPlayback(_data, altered!) == ReplayError.DataMismatch && runner.Simulation == null && runner.PlaybackState == PlaybackState.None,
            $"(c) StartPlayback on the altered copy: sim {runner.Simulation != null}, state {runner.PlaybackState}");
        runner.Free();

        Main main = NewMain(Array.Empty<string>(), "refused");
        AddChild(main);
        await Frames(2);
        bool started = main.WatchReplay(copy);
        await Frames(1);
        Check(!started && main.CurrentMatch == null && main.LoadScreen is { Visible: true } && main.LoadScreen.Note.Text == _ui.LoadGame(LoadText.Refused) && !main.Menu!.Visible,
            $"(c) altered data hash: started {started}, match {main.CurrentMatch != null}, note '{main.LoadScreen?.Note.Text}'");
        Check(main.GetNodeOrNull("Match") == null, "(c) a match node is left after the refusal");
        main.LoadScreen!.BackButton.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(1);
        Check(main.Menu!.Visible && !main.LoadScreen.Visible, "(c) Back from the refusal did not show the menu");
        RemoveChild(main);
        main.QueueFree();
        await Frames(2);
        File.Delete(copy);
    }

    // (d): a copy with one checkpoint altered stops at that tick, with the mismatch banner, and ticks no further.
    private async Task AlteredCheckpoint(string path)
    {
        string copy = Path.Combine(_full, "altered-checkpoint.replay");
        string text = File.ReadAllText(path);
        string prefix = $"k {AlteredTick} ";
        string line = text.Split('\n').First(l => l.StartsWith(prefix, StringComparison.Ordinal));
        ulong hash = ulong.Parse(line[prefix.Length..], NumberStyles.HexNumber, CultureInfo.InvariantCulture) ^ 0x1UL;
        File.WriteAllBytes(copy, Reseal(ReplaceLine(text, prefix, $"{prefix}{hash:X16}")));

        Main main = NewMain(Array.Empty<string>(), "mismatch");
        AddChild(main);
        await Frames(2);
        Check(main.WatchReplay(copy), "(d) the altered-checkpoint copy did not start (it must: it reads and its data hash is right)");
        SimRunner? runner = main.CurrentMatch?.GetNode<SimRunner>("SimRunner");
        if (runner != null)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (runner.PlaybackState == PlaybackState.Playing && clock.Elapsed.TotalSeconds < 60) await Frames(1);
            Simulation sim = runner.Simulation!;
            Check(runner.PlaybackState == PlaybackState.Stopped && runner.PlaybackError == ReplayError.CheckpointMismatch && sim.TickNumber == AlteredTick
                && runner.CheckpointsCompared == AlteredTick / ReplayRecorder.DefaultCheckpointInterval - 1,
                $"(d) playback {runner.PlaybackState} ({runner.PlaybackError}) at tick {sim.TickNumber}, {runner.CheckpointsCompared} compared (want Stopped, CheckpointMismatch, {AlteredTick})");
            MatchOverBanner banner = main.Banner!;
            Check(banner.Visible && banner.Title.Text == _ui.LoadGame(LoadText.Mismatch) && !banner.ContinueButton.Visible && GetTree().Paused,
                $"(d) banner visible {banner.Visible}, title '{banner.Title.Text}', continue {banner.ContinueButton.Visible}");
            // Unpaused, a stopped playback still runs no tick.
            GetTree().Paused = false;
            await Frames(10);
            Check(sim.TickNumber == AlteredTick, $"(d) a stopped playback ticked on to {sim.TickNumber}");
            main.QuitToMenu();
            await Frames(2);
        }
        Check(main.CurrentMatch == null && main.Menu is { Visible: true }, "(d) Quit to menu after the mismatch");
        RemoveChild(main);
        main.QueueFree();
        await Frames(2);
        File.Delete(copy);
    }

    // --replay <path>: the file plays at boot; a missing file shows the refusal on the Load Game screen.
    private async Task ReplayFlag(string path)
    {
        Main main = NewMain(new[] { Main.ReplayFlag, path, "--mute" }, "flag");
        AddChild(main);
        await Frames(2);
        Check(main.CurrentMatch is { IsPlayback: true } m && SamePath(m.ReplayPath!, path) && !main.Menu!.Visible, $"--replay {path}: no playback at boot");
        SimRunner? runner = main.CurrentMatch?.GetNode<SimRunner>("SimRunner");
        await Frames(3);
        Check(runner != null && runner.Simulation!.TickNumber > 0 && runner.PlaybackState == PlaybackState.Playing, $"--replay: playback {runner?.PlaybackState} at tick {runner?.Simulation?.TickNumber}");
        RemoveChild(main);
        main.QueueFree();
        await Frames(2);

        main = NewMain(new[] { Main.ReplayFlag, $"{_dir}/missing.replay" }, "flag-missing");
        AddChild(main);
        await Frames(2);
        Check(main.CurrentMatch == null && main.LoadScreen is { Visible: true } && main.LoadScreen.Note.Text == _ui.LoadGame(LoadText.Refused),
            $"--replay missing file: match {main.CurrentMatch != null}, load {main.LoadScreen?.Visible}, note '{main.LoadScreen?.Note.Text}'");
        RemoveChild(main);
        main.QueueFree();
        await Frames(2);
    }

    // (e): junk and cut files are skipped and counted; Delete removes a file; Prune keeps the newest MaxReplays.
    private async Task JunkDeleteAndPrune(string path)
    {
        int good = ReplayStore.List(_dir, out int before).Count;
        Check(before == 0 && good == 2, $"(e) before the junk: {good} replays, {before} skipped (want 2, 0)");
        File.WriteAllText(Path.Combine(_full, "junk.replay"), "not a replay\n");
        byte[] bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(Path.Combine(_full, "cut.replay"), bytes.AsSpan(0, bytes.Length / 2).ToArray());
        File.WriteAllText(Path.Combine(_full, "notes.txt"), "not looked at\n");

        Main main = NewMain(Array.Empty<string>(), "junk");
        AddChild(main);
        await Frames(2);
        main.Menu!.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(1);
        LoadGameScreen load = main.LoadScreen!;
        Check(load.Entries.Count == 2 && load.Skipped == 2 && load.Note.Text == _ui.ReplaysSkipped(2) && load.List.GetRoot().GetChildCount() == 4,
            $"(e) junk: {load.Entries.Count} rows, {load.Skipped} skipped, note '{load.Note.Text}' (want 2, 2, '{_ui.ReplaysSkipped(2)}')");
        Check(load.Entries[0].When >= load.Entries[1].When, "(e) the list is not newest first");

        // Delete: an old copy, picked, then Delete.
        string old = Path.Combine(_full, ReplayStore.FileName(new DateTime(2020, 1, 1, 12, 0, 0), 99));
        File.Copy(path, old);
        load.Refresh(_dir);
        int row = load.Entries.ToList().FindIndex(e => SamePath(e.Path, old));
        Check(load.Entries.Count == 3 && row == 2, $"(e) the 2020 copy: {load.Entries.Count} rows, at row {row} (want 3, last)");
        if (load.Select(row)) load.DeleteButton.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(1);
        Check(!File.Exists(old) && load.Entries.Count == 2 && load.Entries.All(e => !SamePath(e.Path, old)), $"(e) Delete: file there {File.Exists(old)}, {load.Entries.Count} rows");
        PushKey(Key.Escape);
        await Frames(1);
        Check(main.Menu.Visible && !load.Visible, "(e) Esc on Load Game did not show the menu");
        RemoveChild(main);
        main.QueueFree();
        await Frames(2);

        // The list is empty with a note on a folder with nothing readable.
        string empty = $"{_dir}/empty";
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(empty));
        Check(ReplayStore.List(empty, out int none).Count == 0 && none == 0, "(e) an empty folder lists something");

        // Prune: MaxReplays + 3 files a day apart; the three oldest go.
        string prune = $"{_dir}/prune";
        string pruneFull = ProjectSettings.GlobalizePath(prune);
        Directory.CreateDirectory(pruneFull);
        int n = ReplayStore.MaxReplays + 3;
        for (int d = 1; d <= n; d++) File.Copy(path, Path.Combine(pruneFull, ReplayStore.FileName(new DateTime(2020, 1, 1).AddDays(d), (ulong)d)));
        File.WriteAllText(Path.Combine(pruneFull, "keep.txt"), "x");
        int deleted = ReplayStore.Prune(prune, ReplayStore.MaxReplays);
        var left = Directory.GetFiles(pruneFull, "*.replay").Select(Path.GetFileName).ToHashSet();
        Check(deleted == 3 && left.Count == ReplayStore.MaxReplays && Enumerable.Range(1, 3).All(d => !left.Contains(ReplayStore.FileName(new DateTime(2020, 1, 1).AddDays(d), (ulong)d)))
            && File.Exists(Path.Combine(pruneFull, "keep.txt")), $"(e) Prune: deleted {deleted}, {left.Count} left");
        // A second match in the same second gets its own name.
        var t = new DateTime(2020, 1, 1, 12, 0, 0);
        File.WriteAllText(Path.Combine(pruneFull, ReplayStore.FileName(t, 5)), "x");
        Check(ReplayStore.NewPath(prune, t, 5).EndsWith(ReplayStore.FileName(t, 5, 1), StringComparison.Ordinal), $"(e) NewPath beside a taken name: {ReplayStore.NewPath(prune, t, 5)}");
        foreach (string f in new[] { "junk.replay", "cut.replay", "notes.txt" }) File.Delete(Path.Combine(_full, f));
    }

    // (g) BUG-0830: the replay folder's edge cases.
    private async Task FolderEdgeCases(string path)
    {
        byte[] good = File.ReadAllBytes(path);
        int max = ReplayStore.MaxReplays;

        // Unreadable files hold no cap slot: MaxReplays dated replays, a text and an empty .replay, one newer replay, a prune.
        string cap = $"{_dir}/cap";
        string capFull = Path.GetFullPath(ProjectSettings.GlobalizePath(cap));
        Directory.CreateDirectory(capFull);
        for (int d = 1; d <= max; d++) File.WriteAllBytes(Path.Combine(capFull, ReplayStore.FileName(new DateTime(2020, 1, 1).AddDays(d), (ulong)d)), good);
        File.WriteAllText(Path.Combine(capFull, "junk.replay"), "not a replay\n");
        File.WriteAllBytes(Path.Combine(capFull, "zero.replay"), Array.Empty<byte>());
        string newest = Path.Combine(capFull, ReplayStore.FileName(new DateTime(2021, 1, 1), 77));
        File.WriteAllBytes(newest, good);
        string oldest = Path.Combine(capFull, ReplayStore.FileName(new DateTime(2020, 1, 2), 1));
        ReplayStore.Prune(cap, max);
        int playable = Directory.GetFiles(capFull, "*.replay").Count(f => ReplayFormat.TryReadFile(f, out _) == ReplayError.None);
        Check(playable == max && File.Exists(newest) && !File.Exists(oldest) && File.Exists(Path.Combine(capFull, "junk.replay")) && File.Exists(Path.Combine(capFull, "zero.replay")),
            $"(g) cap with junk: {playable} playable replays kept (want {max}), the newest kept {File.Exists(newest)}, the oldest gone {!File.Exists(oldest)}");

        // Load Game lists the two unreadable files after the replays; one picked: Watch greyed, Delete removes it.
        Main main = NewMain(Array.Empty<string>(), "cap");
        main.ReplayDir = cap;
        AddChild(main);
        await Frames(2);
        main.Menu!.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(1);
        LoadGameScreen load = main.LoadScreen!;
        int rows = load.List.GetRoot().GetChildCount();
        Check(load.Entries.Count == max && rows == max + 2, $"(g) Load Game on the capped folder: {load.Entries.Count} replays, {rows} rows (want {max}, {max + 2})");
        bool picked = load.Select(max);
        string junkCell = picked ? load.List.GetSelected()?.GetText(2) ?? "" : "";
        Check(picked && load.WatchButton.Disabled && !load.DeleteButton.Disabled && junkCell == _ui.LoadGame(LoadText.Unreadable),
            $"(g) an unreadable row picked {picked}: Watch greyed {load.WatchButton.Disabled}, Delete greyed {load.DeleteButton.Disabled}, cell '{junkCell}'");
        if (picked) load.DeleteButton.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(1);
        int unreadableLeft = new[] { "junk.replay", "zero.replay" }.Count(f => File.Exists(Path.Combine(capFull, f)));
        Check(unreadableLeft == 1 && load.List.GetRoot().GetChildCount() == max + 1 && load.Entries.Count == max,
            $"(g) Delete on an unreadable row: {unreadableLeft} unreadable files left, {load.List.GetRoot().GetChildCount()} rows");
        // One separator: every listed path is already in the OS's full form.
        Check(load.Entries.All(e => e.Path == Path.GetFullPath(e.Path)), $"(g) mixed separators: {load.Entries.FirstOrDefault()?.Path}");

        // Headers only: a copy whose body is damaged after the header (the checksum no longer matches) is listed; Watch refuses it.
        string damaged = Path.Combine(capFull, ReplayStore.FileName(new DateTime(2022, 1, 1), 88));
        byte[] bad = (byte[])good.Clone();
        int k = Encoding.ASCII.GetString(bad).IndexOf("\nk ", StringComparison.Ordinal) + 3;
        bad[k] = bad[k] == (byte)'1' ? (byte)'2' : (byte)'1';
        File.WriteAllBytes(damaged, bad);
        Check(ReplayFormat.TryReadFile(damaged, out _) != ReplayError.None, "(g) the damaged copy still reads whole");
        load.Refresh(cap);
        int at = load.Entries.ToList().FindIndex(e => SamePath(e.Path, damaged));
        Check(at == 0, $"(g) headers only: the damaged copy at row {at} (want 0: listed from its header, newest)");
        if (at == 0 && load.Select(0)) load.WatchButton.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        Check(main.CurrentMatch == null && load.Visible && load.Note.Text == _ui.LoadGame(LoadText.Refused) && File.Exists(damaged),
            $"(g) Watch on the damaged copy: match {main.CurrentMatch != null}, note '{load.Note.Text}'");
        RemoveChild(main);
        main.QueueFree();
        await Frames(2);

        // Future-dated names: MaxReplays files stamped 2099, then a menu match saved into the folder survives its own prune.
        string future = $"{_dir}/future";
        string futureFull = Path.GetFullPath(ProjectSettings.GlobalizePath(future));
        Directory.CreateDirectory(futureFull);
        for (int d = 1; d <= max; d++) File.WriteAllBytes(Path.Combine(futureFull, ReplayStore.FileName(new DateTime(2099, 1, 1).AddDays(d), (ulong)d)), good);
        Main live = NewMain(new[] { "--skirmish", "--mute" }, "future");
        live.ReplayDir = future;
        AddChild(live);
        await Frames(4);
        string? saved = live.ReplayPath;
        live.QuitToMenu();
        await Frames(2);
        int kept = Directory.GetFiles(futureFull, "*.replay").Length;
        Check(saved != null && File.Exists(saved) && kept == max, $"(g) future-dated folder: the new recording {saved} kept {saved != null && File.Exists(saved)}, {kept} files (want {max})");
        RemoveChild(live);
        live.QueueFree();
        await Frames(2);
    }

    // A shell a test scene hosts without a folder writes nothing (the game's own boot uses user://replays).
    private async Task HostedWithoutFolder()
    {
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.UserArgs = new[] { "--skirmish", "--mute", Main.NoConfineFlag };
        main.SettingsPath = $"user://replay-playback-test-{OS.GetProcessId()}/hosted/settings.cfg";
        AddChild(main);
        await Frames(3);
        Check(main.ReplayDir == null && main.ReplayPath == null && main.CurrentMatch != null, $"a hosted shell's replay folder '{main.ReplayDir}', path '{main.ReplayPath}'");
        main.QuitToMenu();
        await Frames(1);
        Check(!main.SaveReplay(), "a hosted shell with no folder saved a replay");
        RemoveChild(main);
        main.QueueFree();
        await Frames(2);
    }

    // ---- helpers ----

    private Main NewMain(string[] args, string label)
    {
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.UserArgs = args.Append(Main.NoConfineFlag).ToArray();
        main.SettingsPath = $"user://replay-playback-test-{OS.GetProcessId()}/{label}/settings.cfg"; // BUG-0770: never the player's file
        main.ReplayDir = _dir;
        main.BeforeStart = m => m.GetNode<SimRunner>("SimRunner").FixedFrameDelta = FrameSeconds;
        return main;
    }

    // The player's own replay folder: every file's name, size and write time ("" when there is none).
    private static string PlayerFolder()
    {
        string dir = ProjectSettings.GlobalizePath(ReplayStore.DefaultDir);
        if (!Directory.Exists(dir)) return "";
        return string.Join("|", Directory.GetFiles(dir).OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => $"{Path.GetFileName(f)}:{new FileInfo(f).Length}:{File.GetLastWriteTimeUtc(f).Ticks}"));
    }

    // The file's text with the line starting `prefix` replaced by `line`.
    private static string ReplaceLine(string text, string prefix, string line)
    {
        string[] lines = text.Split('\n');
        int i = Array.FindIndex(lines, l => l.StartsWith(prefix, StringComparison.Ordinal));
        if (i < 0) throw new InvalidOperationException($"no line '{prefix}'");
        lines[i] = line;
        return string.Join('\n', lines);
    }

    // The text with its checksum line recomputed (FNV-1a 64 over every byte before it, docs/03 "Save/load and replays").
    private static byte[] Reseal(string text)
    {
        int at = text.LastIndexOf("checksum ", StringComparison.Ordinal);
        string body = text[..at];
        ulong h = 14695981039346656037UL;
        foreach (byte b in Encoding.ASCII.GetBytes(body)) h = unchecked((h ^ b) * 1099511628211UL);
        return Encoding.ASCII.GetBytes($"{body}checksum {h:X16}\n");
    }

    private static string[] CommandLines(byte[] file) => Encoding.ASCII.GetString(file).Split('\n').Where(l => l.StartsWith("c ", StringComparison.Ordinal)).ToArray();

    private static string FirstDifference(string[] a, string[] b)
    {
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
            if (a[i] != b[i]) return $"line {i}: '{a[i]}' vs '{b[i]}'";
        return a.Length == b.Length ? "none" : $"line {Math.Min(a.Length, b.Length)} (lengths)";
    }

    private static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static int Issued(SelectionController sel) => Enum.GetValues<CommandKind>().Sum(k => sel.IssuedCount(k));

    // Player 0's first live worker-slot unit (any own unit if none).
    private static EntityHandle OwnUnit(Simulation sim)
    {
        UnitStore u = sim.World.Units;
        EntityHandle any = default;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.Owner[i] != SelectionController.LocalPlayer) continue;
            var h = new EntityHandle(i, u.Generation[i]);
            if (sim.World.Data.Units[u.TypeId[i]].Slot == UnitSlot.Worker) return h;
            if (any.Generation == 0) any = h;
        }
        return any;
    }

    // Player 0's first finished building slot, or -1.
    private static int OwnBuilding(Simulation sim)
    {
        BuildingStore b = sim.World.Buildings;
        for (int i = 0; i < b.Capacity; i++)
            if (b.Alive[i] && b.Owner[i] == SelectionController.LocalPlayer && !b.UnderConstruction[i]) return i;
        return -1;
    }

    private void Push(InputEvent e) => GetViewport().PushInput(e);

    private void PushKey(Key key)
    {
        Push(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        Push(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    // ---- M6-V13: "the AI opened with ..." on a 3-opening fixture, live and watched ----

    // The shipped data with both factions' ai.json cut to three openings: the first shipped one, then copies "opening_1" /
    // "Opening 1" and "opening_2" / "Opening 2" (each its own description), loaded from a temp copy, never the shipped files.
    private GameData ThreeOpenings()
    {
        string from = ProjectSettings.GlobalizePath("res://data");
        string to = Path.Combine(_full, "three-openings-data");
        foreach (string file in Directory.GetFiles(from, "*.json", SearchOption.AllDirectories))
        {
            string dest = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, true);
        }
        foreach (string f in new[] { "malazan", "whirlwind" })
        {
            string ai = Path.Combine(to, "factions", f, "ai.json");
            var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(ai))!;
            var openings = root["openings"]!.AsArray();
            var first = openings[0]!.DeepClone();
            openings.Clear();
            openings.Add(first);
            for (int k = 1; k < 3; k++)
            {
                var copy = first.DeepClone();
                copy["id"] = $"opening_{k}";
                copy["displayName"] = $"Opening {k}";
                copy["description"] = $"Fixture opening {k} of {f}";
                openings.Add(copy);
            }
            File.WriteAllText(ai, root.ToJsonString());
        }
        DataLoadResult r = DataLoader.LoadAll(to);
        if (!r.Ok) throw new InvalidOperationException($"the 3-opening fixture failed to load: {string.Join("; ", r.Errors)}");
        return r.Data!;
    }

    private async Task OpeningRows()
    {
        GameData three = ThreeOpenings();
        Check(three.Ai.All(a => a.Openings.Length == 3), "the fixture holds not three openings per faction");
        // A Normal AI in seat 1 (slot 2) on a seed that picks opening 2, then one that picks opening 0.
        foreach (int target in new[] { 2, 0 })
        {
            ulong seed = 1;
            while (Rts.Sim.ViewApi.AiOpeningLookup.Pick(seed, 1, AiDifficulty.Normal, 3) != target) seed++;
            await OpeningRow(three, new[] { "--seed", seed.ToString(CultureInfo.InvariantCulture), "--ai", "2", "normal", "--mute" }, $"normal-{target} (seed {seed})",
                new[] { -1, target });
        }
        // Four seats, every difficulty (Easy, Normal, Hard in slots 2-4) on a seed where Normal and Hard both pick past the first.
        ulong s4 = 1;
        while (Rts.Sim.ViewApi.AiOpeningLookup.Pick(s4, 2, AiDifficulty.Normal, 3) == 0 || Rts.Sim.ViewApi.AiOpeningLookup.Pick(s4, 3, AiDifficulty.Hard, 3) == 0) s4++;
        await OpeningRow(three, new[] { "--seed", s4.ToString(CultureInfo.InvariantCulture), "--players", "4", "--ai", "2", "easy", "--ai", "3", "normal", "--ai", "4", "hard", "--mute" },
            $"four-seats (seed {s4})", null);
    }

    // A live match on the fixture, its stats' opening lines; then its recording watched: the same lines, from the seed (the
    // playback sim has no AI: AiOpening is -1 for every seat).
    private async Task OpeningRow(GameData data, string[] args, string label, int[]? want)
    {
        var m = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(m);
        SimRunner runner = m.GetNode<SimRunner>("SimRunner");
        runner.RecordCheckpointInterval = 20;
        runner.FixedFrameDelta = FrameSeconds;
        m.Start(data, LaunchOptions.Parse(args));
        Simulation sim = runner.Simulation!;
        int players = sim.World.Config.PlayerCount;
        int[] live = Enumerable.Range(0, players).Select(p => sim.AiOpening(p)).ToArray();
        if (want != null) Check(live.SequenceEqual(want), $"{label}: live picks [{string.Join(", ", live)}] (want [{string.Join(", ", want)}])");
        Check(live[0] == -1 && live.Skip(1).All(o => o >= 0), $"{label}: live picks [{string.Join(", ", live)}] (seat 0 human, the rest AI)");
        while (sim.TickNumber < 40) await Frames(1);
        var stats = new MatchStats { Name = "OpeningStats" };
        AddChild(stats);
        stats.Build(_ui, data);
        stats.ShowFor(m);
        await Frames(2);
        foreach (string f in StatsScreenCheck.Openings(stats, data, sim.World, _ui, live, $"{label} live")) Check(false, f);
        foreach (string f in StatsScreenCheck.Fits(stats, GetViewport().GetVisibleRect(), $"{label} live")) Check(false, f);
        string[] liveLines = stats.OpeningLines.Select(l => l.Text).ToArray();
        await Shot($"openings-{label.Split(' ')[0]}");
        Replay replay = runner.Recorder!.ToReplay();
        RemoveChild(m);
        m.QueueFree();
        await Frames(1);

        var watch = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(watch);
        ReplayError refused = watch.StartReplay(data, replay, LaunchOptions.Parse(new[] { "--mute" }), "opening-row");
        if (Check(refused == ReplayError.None, $"{label}: replay refused ({refused})"))
        {
            Simulation psim = watch.GetNode<SimRunner>("SimRunner").Simulation!;
            await Frames(2);
            Check(Enumerable.Range(0, players).All(p => psim.AiOpening(p) == -1), $"{label}: the playback sim runs an AI");
            stats.ShowFor(watch);
            await Frames(2);
            foreach (string f in StatsScreenCheck.Openings(stats, data, psim.World, _ui, live, $"{label} watched")) Check(false, f);
            string[] watched = stats.OpeningLines.Select(l => l.Text).ToArray();
            Check(watched.SequenceEqual(liveLines) && watched.Length == players - 1, $"{label}: watched lines [{string.Join(" | ", watched)}], live [{string.Join(" | ", liveLines)}]");
            _notes.Add($"{label}: [{string.Join(" | ", watched)}]");
        }
        RemoveChild(stats);
        stats.QueueFree();
        RemoveChild(watch);
        watch.QueueFree();
        await Frames(1);
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    // Windowed with `-- --shots <dir> [--size WxH]`: the viewport as <name>.png (M6-V11: a real skirmish's stats screen).
    private async Task Shot(string name)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        if (_window != default && DisplayServer.WindowGetSize() != _window)
        {
            DisplayServer.WindowSetSize(_window);
            GetTree().Root.Size = _window;
            await Frames(2);
        }
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = $"{_shots}/{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"  screenshot {path}");
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
