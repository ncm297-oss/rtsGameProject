using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.Replays;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// QA M6-V9 (replay recording and playback, view track). Attacks on the dev's ReplayPlaybackTest:
/// (1) hash twins: a 4-seat (3 AI) <c>--skirmish</c> match recorded through the hosted shell to tick 2,500 (scripted human
/// orders), Quit to menu by the pause menu's button; the file plays equal in <see cref="ReplayPlayer.Run(Replay, GameData)"/>
/// and through the shell with game-speed changes, tree pauses, pause-menu pauses and a seeded 3,000-event input fuzz (clicks,
/// drags, double-clicks, right clicks, minimap clicks, hotkeys, control groups, card presses, wheel): every checkpoint equal,
/// the playback command lines byte-equal to the file's, nothing issued; a second watch the same.
/// (2) damaged files (truncated, empty, seat line altered sealed / unsealed, extra command, format 2) refused with
/// <c>load.refused</c>, no match node left; the seat-faction swap reported.
/// (3) a folder with 50 recorded copies + junk lists 50 rows newest first, the junk counted; a recorded match saved into it
/// prunes to <see cref="ReplayStore.MaxReplays"/> and deletes only the oldest replays.
/// (4) a Victory recorded through the shell, re-watched: the same banner title at the end; with Continue watching before Quit
/// to menu, the Victory banner mid-replay (Continue shown) then "Replay ended".
/// (5) the window-close notification mid-match writes the file.
/// (6) the player's user:// untouched: settings.cfg md5, no user://replays created.
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6V9Test.tscn</c>; prints "QA M6-V9 TEST PASS" or "QA M6-V9 TEST FAIL: ..." lines.</remarks>
public partial class QaM6V9Test : Node
{
    private const double FrameSeconds = 0.25;
    private const int TwinTicks = 2_500;
    private const int FuzzEvents = 3_000;

    private static readonly string[] DevArgs = { "--units", "0", "--no-bases", "--mute", "--seed", "3", "--speed", "8" };

    private readonly List<string> _failures = new();
    private readonly List<string> _notes = new();
    private GameData _data = null!;
    private UiText _ui = null!;
    private string _dir = "", _full = "";

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
            _dir = $"user://qa-m6v9-{OS.GetProcessId()}";
            _full = Path.GetFullPath(ProjectSettings.GlobalizePath(_dir));
            if (Directory.Exists(_full)) Directory.Delete(_full, true);
            Directory.CreateDirectory(_full);
            string settingsBefore = PlayerSettingsMd5();
            bool replaysBefore = Directory.Exists(ProjectSettings.GlobalizePath(ReplayStore.DefaultDir));

            string? twin = await HashTwins();
            if (twin != null)
            {
                await DamagedFiles(twin);
                await FiftyFilesAndPrune(twin);
                FutureDatedNames(twin);
            }
            await VictoryReplay(continueFirst: false);
            await VictoryReplay(continueFirst: true);
            await WindowClose();
            await BootedShellFolder();

            Check(PlayerSettingsMd5() == settingsBefore, "the player's settings.cfg changed over the run");
            Check(Directory.Exists(ProjectSettings.GlobalizePath(ReplayStore.DefaultDir)) == replaysBefore, "the player's user://replays was created or removed over the run");
            try { Directory.Delete(_full, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        foreach (string n in _notes) GD.Print($"QA M6-V9 NOTE: {n}");
        foreach (string f in _failures.Take(60)) GD.Print($"QA M6-V9 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M6-V9 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- (1) hash twins ----

    private async Task<string?> HashTwins()
    {
        Main main = NewMain(new[] { "--skirmish", "--players", "4", "--mute" }, "twin");
        AddChild(main);
        await Frames(2);
        string? path = null;
        try
        {
            Match? match = main.CurrentMatch;
            if (!Check(match != null && !match.IsPlayback && main.ReplayPath != null, "twin: no recorded live match")) return null;
            SimRunner runner = match!.GetNode<SimRunner>("SimRunner");
            Simulation sim = runner.Simulation!;
            Check(runner.Seats.Count(s => s.Controller == SeatController.Ai) == 3, $"twin: seats [{string.Join(", ", runner.Seats)}] (want 3 AI)");
            path = main.ReplayPath!;
            var sel = match.GetNode<SelectionController>("SelectionController");
            int step = 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (sim.TickNumber < TwinTicks && clock.Elapsed.TotalSeconds < 300)
            {
                await Frames(1);
                if (step == 0 && sim.TickNumber >= 60)
                {
                    step++;
                    int hall = OwnBuilding(sim);
                    EntityHandle w0 = OwnUnit(sim);
                    if (hall >= 0 && w0.Generation != 0 && sel.SelectBuilding(hall)) sel.Produce(sim.World.Units.TypeId[w0.Index], false);
                }
                else if (step == 1 && sim.TickNumber >= 400)
                {
                    step++;
                    EntityHandle w = OwnUnit(sim);
                    sel.SelectOnly(w);
                    System.Numerics.Vector2 at = sim.World.Units.Position[w.Index];
                    sel.Order(CommandKind.Move, new Vector2(at.X + 5f, at.Y - 3f), queued: false);
                }
                else if (step == 2 && sim.TickNumber >= 1200)
                {
                    step++;
                    sel.SelectOnly(OwnUnit(sim));
                    sel.Order(CommandKind.Stop, null, queued: false);
                }
            }
            int recorded = sim.TickNumber;
            Check(recorded == TwinTicks, $"twin: recorded to tick {recorded} (want {TwinTicks}, {clock.Elapsed.TotalSeconds:0} s)");
            // Quit to menu mid-match through the pause menu's own button.
            main.PauseMatch();
            await Frames(2);
            main.Pause!.QuitToMenuButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);
            if (!Check(main.CurrentMatch == null && File.Exists(path), $"twin: Quit to menu (pause menu) wrote no file {path}")) return null;
            if (!Check(ReplayFormat.TryReadFile(path, out Replay? replay) == ReplayError.None && replay != null, "twin: the file does not read")) return null;
            Check(replay!.TickCount == recorded && replay.PlayerCount == 4 && replay.Seats.Count(s => s.Controller == SeatController.Ai) == 3
                && replay.Checkpoints.Length == recorded / 100, $"twin: header ticks {replay.TickCount}, players {replay.PlayerCount}, checkpoints {replay.Checkpoints.Length}");
            int ai = replay.Commands.Count(c => c.Player > 0 && c.Tick > 1);
            Check(ai > 50 && replay.Commands.Count(c => c.Player == 0 && c.Tick > 1) >= 3, $"twin: {ai} AI commands, {replay.Commands.Count(c => c.Player == 0 && c.Tick > 1)} human (the AIs must play)");
            _notes.Add($"twin file: {new FileInfo(path).Length} bytes, {replay.Commands.Length} commands, {replay.Checkpoints.Length} checkpoints");

            // Sim twin.
            ReplayResult simTwin = ReplayPlayer.Run(replay, _data);
            Check(simTwin.Error == ReplayError.None, $"twin: ReplayPlayer.Run says {simTwin.Error} at tick {simTwin.Tick}");

            // Shell twin with speed, pause and fuzz.
            await WatchFuzzed(main, path, replay, fuzz: true);
            // A second watch: nothing carried over from the first.
            await WatchFuzzed(main, path, replay, fuzz: false);
            return path;
        }
        finally
        {
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
    }

    private async Task WatchFuzzed(Main main, string path, Replay replay, bool fuzz)
    {
        string label = fuzz ? "fuzzed watch" : "second watch";
        int filesBefore = Directory.GetFiles(_full).Length;
        if (!Check(main.WatchReplay(path), $"{label}: did not start")) return;
        Match match = main.CurrentMatch!;
        SimRunner runner = match.GetNode<SimRunner>("SimRunner");
        Simulation sim = runner.Simulation!;
        var sel = match.GetNode<SelectionController>("SelectionController");
        var card = match.GetNode<CommandCard>("Hud/CommandCard");
        var minimap = match.GetNodeOrNull<Control>("Hud/Minimap");
        match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
        var rng = new Random(9061);
        double[] speeds = { SimRunner.MinSpeed, 0.5, 1.0, 2.0, 4.0, SimRunner.MaxSpeed };
        int pushed = 0, frames = 0, pauses = 0, menuPauses = 0, speedChanges = 0, resumedMenus = 0, interrupted = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Vector2 size = GetViewport().GetVisibleRect().Size;
        while (runner.PlaybackState == PlaybackState.Playing && clock.Elapsed.TotalSeconds < 400)
        {
            frames++;
            if (fuzz)
            {
                // Speed changes and pauses on their own schedule.
                if (frames % 37 == 0) { runner.GameSpeed = speeds[rng.Next(speeds.Length)]; speedChanges++; }
                if (frames % 53 == 0 && !main.BannerUp)
                {
                    GetTree().Paused = true;
                    pauses++;
                    int at = sim.TickNumber;
                    await Frames(3);
                    Check(sim.TickNumber == at, $"{label}: ticked while the tree was paused ({at} -> {sim.TickNumber})");
                    GetTree().Paused = false;
                }
                if (frames % 71 == 0 && !main.BannerUp)
                {
                    main.PauseMatch();
                    menuPauses++;
                    int at = sim.TickNumber;
                    await Frames(2);
                    Check(sim.TickNumber == at, $"{label}: ticked under the pause menu");
                    main.Resume();
                }
                // Fuzz events until the playback is near its end (the end banner's buttons must not be clicked at random).
                if (pushed < FuzzEvents && sim.TickNumber < replay.TickCount - 150)
                {
                    int burst = 6 + rng.Next(6);
                    for (int i = 0; i < burst && pushed < FuzzEvents; i++)
                    {
                        if (main.Paused) { main.Resume(); resumedMenus++; }
                        if (main.BannerUp || main.CurrentMatch != match) { interrupted++; break; }
                        pushed += FuzzOne(rng, size, sel, card, minimap);
                    }
                    if (main.Paused) { main.Resume(); resumedMenus++; }
                    if (main.CurrentMatch != match) { Check(false, $"{label}: the fuzz left the playback (match {main.CurrentMatch != null})"); return; }
                }
            }
            else runner.GameSpeed = SimRunner.MaxSpeed;
            if (main.BannerUp && runner.PlaybackState == PlaybackState.Playing) main.ContinueWatching(); // an outcome mid-replay
            await Frames(1);
        }
        if (fuzz)
        {
            Check(pushed >= FuzzEvents, $"{label}: only {pushed} fuzz events pushed");
            _notes.Add($"{label}: {pushed} events over {frames} frames, {speedChanges} speed changes, {pauses} tree pauses, {menuPauses} pause-menu pauses, {resumedMenus} pause menus the fuzz opened, {interrupted} bursts cut, {clock.Elapsed.TotalSeconds:0.0} s");
        }
        Check(runner.PlaybackState == PlaybackState.Ended && runner.PlaybackError == ReplayError.None && sim.TickNumber == replay.TickCount,
            $"{label}: playback {runner.PlaybackState} ({runner.PlaybackError}) at tick {sim.TickNumber} of {replay.TickCount}");
        ReplayRecorder rec = runner.Recorder!;
        bool allEqual = rec.CheckpointCount == replay.Checkpoints.Length;
        int firstBad = -1;
        for (int i = 0; allEqual && i < replay.Checkpoints.Length; i++)
            if (rec.CheckpointAt(i) != replay.Checkpoints[i]) { allEqual = false; firstBad = i; }
        Check(allEqual && runner.CheckpointsCompared == replay.Checkpoints.Length,
            $"{label}: checkpoints {rec.CheckpointCount}/{replay.Checkpoints.Length}, compared {runner.CheckpointsCompared}, first bad {firstBad}");
        string[] fileLines = CommandLines(File.ReadAllBytes(path)), playLines = CommandLines(ReplayFormat.Write(rec.ToReplay()));
        Check(playLines.SequenceEqual(fileLines), $"{label}: playback command lines {playLines.Length} vs file {fileLines.Length}");
        Check(Enum.GetValues<CommandKind>().Sum(k => sel.IssuedCount(k)) == 0, $"{label}: the selection issued orders in playback");
        MatchOverBanner banner = main.Banner!;
        Check(banner.Visible && !banner.ContinueButton.Visible && banner.Title.Text == _ui.LoadGame(LoadText.Ended), $"{label}: end banner '{banner.Title.Text}', continue {banner.ContinueButton.Visible}");
        banner.MenuButton.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        Check(main.CurrentMatch == null && Directory.GetFiles(_full).Length == filesBefore, $"{label}: Main menu left a match or wrote a file");
    }

    // One fuzz action; returns the events it pushed.
    private int FuzzOne(Random rng, Vector2 size, SelectionController sel, CommandCard card, Control? minimap)
    {
        Vector2 P() => new((float)rng.NextDouble() * size.X, (float)rng.NextDouble() * size.Y);
        Vector2 M() => minimap == null ? P() : minimap.GetGlobalRect().Position + new Vector2((float)rng.NextDouble(), (float)rng.NextDouble()) * minimap.GetGlobalRect().Size;
        Key[] keys = { Key.S, Key.H, Key.A, Key.M, Key.B, Key.V, Key.Tab, Key.Q, Key.W, Key.E, Key.R, Key.T, Key.D, Key.F, Key.G, Key.Z, Key.X, Key.C, Key.Space, Key.Delete, Key.F1, Key.F2, Key.F3, Key.Up, Key.Left };
        Key[] digits = { Key.Key1, Key.Key2, Key.Key3, Key.Key4, Key.Key5, Key.Key6, Key.Key7, Key.Key8, Key.Key9, Key.Key0 };
        switch (rng.Next(12))
        {
            case 0: return Click(P(), MouseButton.Left, false, false);
            case 1:
            {
                Vector2 a = P(), b = P();
                Push(new InputEventMouseMotion { Position = a, GlobalPosition = a });
                Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = a, GlobalPosition = a, ButtonMask = MouseButtonMask.Left });
                Push(new InputEventMouseMotion { Position = b, GlobalPosition = b, ButtonMask = MouseButtonMask.Left });
                Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = b, GlobalPosition = b });
                return 4;
            }
            case 2: return Click(P(), MouseButton.Right, rng.Next(2) == 0, false);
            case 3: return Click(M(), MouseButton.Right, rng.Next(3) == 0, false);
            case 4: return Click(M(), MouseButton.Left, false, false);
            case 5: return Key1(keys[rng.Next(keys.Length)], shift: rng.Next(4) == 0, ctrl: false);
            case 6:
            {
                Key d = digits[rng.Next(digits.Length)];
                int mode = rng.Next(4);
                if (mode == 3) return Key1(d, false, false) + Key1(d, false, false); // double tap
                return Key1(d, shift: mode == 1, ctrl: mode == 2);
            }
            case 7: return Key1(Key.A, false, false) + Click(P(), MouseButton.Left, false, false);
            case 8: return Click(P(), MouseButton.Left, false, true);
            case 9:
            {
                card.Press(rng.Next(CommandCard.Cells));
                return 1;
            }
            case 10:
            {
                Vector2 a = P();
                MouseButton wheel = rng.Next(2) == 0 ? MouseButton.WheelUp : MouseButton.WheelDown;
                Push(new InputEventMouseButton { ButtonIndex = wheel, Pressed = true, Position = a, GlobalPosition = a, Factor = 1f });
                Push(new InputEventMouseButton { ButtonIndex = wheel, Pressed = false, Position = a, GlobalPosition = a, Factor = 1f });
                return 2;
            }
            default:
            {
                // An own unit's screen spot: right click near it, select it, S / H.
                Vector2 a = P();
                return Click(a, MouseButton.Left, false, false) + Key1(rng.Next(2) == 0 ? Key.S : Key.H, false, false) + Click(a + new Vector2(40f, 10f), MouseButton.Right, false, false);
            }
        }
    }

    private int Click(Vector2 at, MouseButton button, bool shift, bool doubleClick)
    {
        MouseButtonMask mask = button == MouseButton.Left ? MouseButtonMask.Left : MouseButtonMask.Right;
        Push(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        Push(new InputEventMouseButton { ButtonIndex = button, Pressed = true, Position = at, GlobalPosition = at, ButtonMask = mask, ShiftPressed = shift, DoubleClick = doubleClick });
        Push(new InputEventMouseButton { ButtonIndex = button, Pressed = false, Position = at, GlobalPosition = at, ShiftPressed = shift });
        return 3;
    }

    private int Key1(Key key, bool shift, bool ctrl)
    {
        Push(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true, ShiftPressed = shift, CtrlPressed = ctrl });
        Push(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false, ShiftPressed = shift, CtrlPressed = ctrl });
        return 2;
    }

    // ---- (2) damaged files ----

    private async Task DamagedFiles(string path)
    {
        string text = File.ReadAllText(path);
        byte[] bytes = File.ReadAllBytes(path);
        string seatLine = text.Split('\n').First(l => l.StartsWith("seat 1 ", StringComparison.Ordinal));
        string[] seat = seatLine.Split(' ');
        string otherFaction = _data.Factions.Select(f => f.Key).First(k => k != seat[2]);
        string[] lines = text.Split('\n');
        string lastCmd = lines.Last(l => l.StartsWith("c ", StringComparison.Ordinal));
        int cmdCount = lines.Count(l => l.StartsWith("c ", StringComparison.Ordinal));
        int midAt = Array.FindIndex(lines, l => l.StartsWith("c ", StringComparison.Ordinal) && l.Split(' ')[1] is string t && int.Parse(t) > 150);
        string[] midParts = lines[midAt].Split(' ');
        midParts[2] = "0"; // player 0
        string mid = string.Join(' ', midParts);
        string firstLine = lines[0];

        var cases = new List<(string Name, byte[] Bytes)>
        {
            ("truncated", bytes.AsSpan(0, bytes.Length * 3 / 4).ToArray()),
            ("empty", Array.Empty<byte>()),
            ("seat-unsealed", Encoding.ASCII.GetBytes(text.Replace(seatLine, $"seat 1 {otherFaction} {seat[3]} {seat[4]}"))),
            ("seat-unknown-faction", Reseal(text.Replace(seatLine, $"seat 1 no_such_faction {seat[3]} {seat[4]}"))),
            ("seat-bad-controller", Reseal(text.Replace(seatLine, $"seat 1 {seat[2]} robot {seat[4]}"))),
            ("extra-command-unsealed", Encoding.ASCII.GetBytes(text.Replace(lastCmd + "\n", lastCmd + "\n" + lastCmd + "\n"))),
            ("extra-command-sealed-duplicate", Reseal(text.Replace(lastCmd + "\n", lastCmd + "\n" + lastCmd + "\n").Replace($"commands {cmdCount}\n", $"commands {cmdCount + 1}\n"))),
            ("extra-command-sealed-mid", Reseal(text.Replace(lines[midAt] + "\n", lines[midAt] + "\n" + mid + "\n").Replace($"commands {cmdCount}\n", $"commands {cmdCount + 1}\n"))),
            ("format-2-sealed", Reseal(text.Replace(firstLine + "\n", "rts-replay 2\n"))),
            ("format-2-unsealed", Encoding.ASCII.GetBytes(text.Replace(firstLine + "\n", "rts-replay 2\n"))),
            ("format-7-sealed", Reseal(text.Replace(firstLine + "\n", "rts-replay 7\n"))),
        };
        Main main = NewMain(Array.Empty<string>(), "damaged");
        AddChild(main);
        await Frames(2);
        try
        {
            foreach ((string name, byte[] b) in cases)
            {
                string copy = Path.Combine(_full, $"damaged-{name}.replay");
                File.WriteAllBytes(copy, b);
                bool started = main.WatchReplay(copy);
                await Frames(1);
                Check(!started && main.CurrentMatch == null && main.GetNodeOrNull("Match") == null && main.LoadScreen is { Visible: true }
                    && main.LoadScreen.Note.Text == _ui.LoadGame(LoadText.Refused) && main.Menu is { Visible: false },
                    $"damaged {name}: started {started}, note '{main.LoadScreen?.Note.Text}'");
                if (started) main.QuitToMenu();
                main.ShowMenu();
                await Frames(1);
                File.Delete(copy);
            }

            // The seat line swapped to another real faction (sealed): it must not play to "Replay ended" with every checkpoint
            // equal while the list names a faction that didn't play. Reported, whatever it does.
            string swap = Path.Combine(_full, "damaged-seat-swap.replay");
            File.WriteAllBytes(swap, Reseal(text.Replace(seatLine, $"seat 1 {otherFaction} {seat[3]} {seat[4]}")));
            ReplayError read = ReplayFormat.TryReadFile(swap, out Replay? swapped);
            string outcome = $"read {read}";
            if (read == ReplayError.None && swapped != null)
            {
                ReplayResult r = ReplayPlayer.Run(swapped, _data);
                outcome += $", ReplayPlayer {r.Error} at tick {r.Tick}";
                if (main.WatchReplay(swap))
                {
                    SimRunner runner = main.CurrentMatch!.GetNode<SimRunner>("SimRunner");
                    runner.GameSpeed = SimRunner.MaxSpeed;
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    while (runner.PlaybackState == PlaybackState.Playing && clock.Elapsed.TotalSeconds < 120)
                    {
                        if (main.BannerUp && runner.PlaybackState == PlaybackState.Playing) main.ContinueWatching();
                        await Frames(1);
                    }
                    outcome += $", shell {runner.PlaybackState} ({runner.PlaybackError}) at tick {runner.Simulation!.TickNumber}, banner '{main.Banner!.Title.Text}'";
                    Check(runner.PlaybackState == PlaybackState.Stopped && main.Banner.Title.Text == _ui.LoadGame(LoadText.Mismatch),
                        $"seat swap {seat[2]} -> {otherFaction}: {outcome} (want stopped out of sync)");
                    main.QuitToMenu();
                    await Frames(2);
                }
                else outcome += ", shell refused";
            }
            _notes.Add($"seat-swap file: {outcome}");
            File.Delete(swap);

            // --replay with a damaged file at boot: the refusal screen, no quit.
            string fmt2 = Path.Combine(_full, "boot-format-2.replay");
            File.WriteAllBytes(fmt2, cases.First(c => c.Name == "format-2-sealed").Bytes);
            Main boot = NewMain(new[] { Main.ReplayFlag, fmt2, "--mute" }, "boot");
            AddChild(boot);
            await Frames(2);
            Check(boot.CurrentMatch == null && boot.LoadScreen is { Visible: true } && boot.LoadScreen.Note.Text == _ui.LoadGame(LoadText.Refused),
                $"--replay format 2: match {boot.CurrentMatch != null}, note '{boot.LoadScreen?.Note.Text}'");
            RemoveChild(boot);
            boot.QueueFree();
            await Frames(2);
            File.Delete(fmt2);
        }
        finally
        {
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
    }

    // ---- (3) 50 files + junk, then the cap ----

    private async Task FiftyFilesAndPrune(string source)
    {
        string dir = $"{_dir}/fifty";
        string full = Path.GetFullPath(ProjectSettings.GlobalizePath(dir));
        Directory.CreateDirectory(full);
        var dated = new List<string>();
        for (int d = 0; d < 50; d++)
        {
            string name = ReplayStore.FileName(new DateTime(2025, 1, 1, 10, 0, 0).AddHours(d * 7), (ulong)(1000 + d));
            File.Copy(source, Path.Combine(full, name));
            dated.Add(name);
        }
        File.WriteAllText(Path.Combine(full, "junk.replay"), "garbage\n");
        File.WriteAllBytes(Path.Combine(full, "zero.replay"), Array.Empty<byte>());
        File.WriteAllText(Path.Combine(full, "notes.txt"), "x");
        File.WriteAllText(Path.Combine(full, "20260101-000000-seed1.replay.tmp"), "x");
        Directory.CreateDirectory(Path.Combine(full, "folder.replay"));

        Main main = NewMain(Array.Empty<string>(), "fifty");
        main.ReplayDir = dir;
        AddChild(main);
        await Frames(2);
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            main.ShowLoadGame();
            watch.Stop();
            LoadGameScreen load = main.LoadScreen!;
            _notes.Add($"Load Game with 50 x {new FileInfo(source).Length} B replays + junk: opened in {watch.Elapsed.TotalMilliseconds:0} ms");
            var want = dated.OrderByDescending(n => n, StringComparer.Ordinal).ToList();
            // M6-VH3 (BUG-0830): the two unreadable files are listed after the 50 replays, so Delete can remove them.
            Check(load.Entries.Count == 50 && load.Skipped == 2 && load.Note.Text == _ui.ReplaysSkipped(2) && load.List.GetRoot().GetChildCount() == 52
                && load.Entries.Select(e => Path.GetFileName(e.Path)).SequenceEqual(want) && load.SelectedIndex == 0,
                $"fifty: {load.Entries.Count} rows, {load.Skipped} skipped, note '{load.Note.Text}', order ok {load.Entries.Select(e => Path.GetFileName(e.Path)).SequenceEqual(want)}");
            for (int i = 0; i < Math.Min(3, load.Entries.Count); i++)
            {
                TreeItem row = load.List.GetRoot().GetChild(i);
                Check(row.GetText(1) == load.Entries[i].Seed.ToString(System.Globalization.CultureInfo.InvariantCulture) && row.GetText(3) == _ui.Clock(TwinTicks),
                    $"fifty row {i}: [{row.GetText(0)} | {row.GetText(1)} | {row.GetText(2)} | {row.GetText(3)}]");
            }
            watch.Restart();
            load.Refresh(dir);
            _notes.Add($"Load Game second read of the same folder: {watch.Elapsed.TotalMilliseconds:0} ms");
            main.ShowMenu();

            // A recorded match into the full folder: the cap keeps MaxReplays, deleting the oldest only.
            main.Menu!.SkirmishButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(1);
            main.BeforeStart = m => m.GetNode<SimRunner>("SimRunner").FixedFrameDelta = FrameSeconds;
            main.Setup!.StartButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);
            string? path = main.ReplayPath;
            Simulation? sim = main.CurrentMatch?.Simulation;
            if (!Check(path != null && sim != null && Path.GetDirectoryName(path) == full.TrimEnd(Path.DirectorySeparatorChar), $"fifty: the menu match records to '{path}'")) return;
            while (sim!.TickNumber < 100) await Frames(1);
            main.QuitToMenu();
            await Frames(2);
            string[] left = Directory.GetFiles(full, "*.replay").Select(Path.GetFileName).ToArray()!;
            string[] leftDated = left.Where(n => dated.Contains(n)).ToArray();
            var newest = want.Take(ReplayStore.MaxReplays - 1).ToHashSet();
            bool oldestOnly = leftDated.All(newest.Contains);
            _notes.Add($"prune: {left.Length} .replay files left: {leftDated.Length} of the 50, the new one {left.Contains(Path.GetFileName(path))}, junk kept [{string.Join(", ", left.Where(n => !dated.Contains(n) && n != Path.GetFileName(path)))}]");
            Check(File.Exists(path) && oldestOnly && File.Exists(Path.Combine(full, "notes.txt")) && File.Exists(Path.Combine(full, "20260101-000000-seed1.replay.tmp")),
                $"prune: new file {File.Exists(path)}, only the newest kept {oldestOnly}, non-replays kept");
            // BUG-0830 (fixed in M6-VH3): unreadable *.replay files hold no cap slot; a check now, was a note.
            int playable = ReplayStore.List(dir, out int skippedAfter).Count;
            Check(playable == ReplayStore.MaxReplays && skippedAfter == 2, $"prune: {playable} playable replays kept (want {ReplayStore.MaxReplays}), {skippedAfter} unreadable files (want 2)");
        }
        finally
        {
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
    }

    // BUG-0830 item 2 (S4, a note): files whose name stamp is later than now (a clock or time-zone change, the DST
    // fall-back hour) outrank the match just saved, so a full folder of them prunes the new recording at once.
    private void FutureDatedNames(string source)
    {
        string dir = $"{_dir}/future";
        string full = ProjectSettings.GlobalizePath(dir);
        Directory.CreateDirectory(full);
        for (int d = 0; d < ReplayStore.MaxReplays; d++) File.Copy(source, Path.Combine(full, ReplayStore.FileName(new DateTime(2099, 1, 1).AddDays(d), (ulong)d)));
        if (ReplayFormat.TryReadFile(source, out Replay? replay) != ReplayError.None) return;
        string path = ReplayStore.NewPath(dir, DateTime.Now, 1);
        Check(ReplayStore.Write(replay!, path, out string error), $"future: write failed: {error}");
        // M6-VH3 (BUG-0830 fixed): the prune Main.SaveReplay runs keeps the path just written; a check now, was a note.
        ReplayStore.Prune(dir, ReplayStore.MaxReplays, path);
        Check(File.Exists(path) && Directory.GetFiles(full, "*.replay").Length == ReplayStore.MaxReplays,
            $"future-dated names: the replay just saved ({Path.GetFileName(path)}) kept {File.Exists(path)}, {Directory.GetFiles(full, "*.replay").Length} files (want {ReplayStore.MaxReplays}) beside {ReplayStore.MaxReplays} files named 2099");
    }

    // ---- (4) Victory ----

    private async Task VictoryReplay(bool continueFirst)
    {
        string label = continueFirst ? "victory+continue" : "victory";
        Main main = NewMain(Array.Empty<string>(), label); // DevArgs at boot would start an unrecorded dev match
        AddChild(main);
        await Frames(2);
        try
        {
            main.BeforeStart = m =>
            {
                SimRunner r = m.GetNode<SimRunner>("SimRunner");
                r.PlayerCount = 3;
                r.FixedFrameDelta = FrameSeconds;
            };
            main.StartSkirmish(DevArgs);
            main.BeforeStart = null;
            Match match = main.CurrentMatch!;
            string? path = main.ReplayPath;
            if (!Check(path != null, $"{label}: StartSkirmish records nothing")) return;
            Simulation sim = match.Simulation!;
            World w = sim.World;
            match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
            await Bases(sim, 2);
            DefeatStaging.SpawnKillers(sim, _data, 0, 1);
            NavGrid g = w.NavGrid;
            int far = FlowField.NearestPassable(g, Spots(g)[2].Y * g.Width + Spots(g)[2].X);
            sim.Enqueue(Command.SpawnUnit(2, StartBase.UnitOfSlot(_data, w.FactionOf(2), UnitSlot.Worker), g.CellCenter(far % g.Width, far / g.Width)));
            await Ticks(sim, 2);
            DefeatStaging.AttackVictims(sim, 0, 1);
            var until = System.Diagnostics.Stopwatch.StartNew();
            while (DefeatStaging.UnitsOf(w, 1).Count > 0 && until.Elapsed.TotalSeconds < 60) await Frames(1);
            EntityHandle target = BuildingOf(w, 1);
            Army(sim, 0, target, 6);
            await Ticks(sim, 3);
            foreach (EntityHandle u in DefeatStaging.UnitsOf(w, 0)) sim.Enqueue(Command.Attack(0, u, target, isBuilding: true));
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (!main.BannerUp && clock.Elapsed.TotalSeconds < 120) await Frames(1);
            if (!Check(main.BannerUp && match.Outcome == MatchOutcome.Victory, $"{label}: staging gave banner {main.BannerUp}, outcome {match.Outcome}")) return;
            int bannerTick = sim.TickNumber;
            string title = main.Banner!.Title.Text;
            Check(File.Exists(path) && ReplayFormat.TryReadFile(path!, out Replay? atBanner) == ReplayError.None && atBanner!.TickCount == bannerTick,
                $"{label}: the banner's save: file {File.Exists(path)}, ticks (want {bannerTick})");
            int extra = 0;
            if (continueFirst)
            {
                main.ContinueWatching();
                int from = sim.TickNumber;
                while (sim.TickNumber < from + 40) await Frames(1);
                extra = sim.TickNumber - bannerTick;
                main.QuitToMenu();
            }
            else main.Banner.MenuButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);
            if (!Check(ReplayFormat.TryReadFile(path!, out Replay? replay) == ReplayError.None && replay != null && replay.TickCount == bannerTick + extra,
                $"{label}: the saved file: ticks {replay?.TickCount} (want {bannerTick + extra})")) return;
            Check(ReplayPlayer.Run(replay!, _data).Error == ReplayError.None, $"{label}: ReplayPlayer refuses the file");

            // Re-watch.
            if (!Check(main.WatchReplay(path!), $"{label}: Watch did not start")) return;
            SimRunner runner = main.CurrentMatch!.GetNode<SimRunner>("SimRunner");
            runner.GameSpeed = SimRunner.MaxSpeed;
            bool sawMid = false;
            clock.Restart();
            while (runner.PlaybackState == PlaybackState.Playing && clock.Elapsed.TotalSeconds < 120)
            {
                await Frames(1);
                if (continueFirst && main.BannerUp && runner.PlaybackState == PlaybackState.Playing)
                {
                    sawMid = true;
                    Check(main.Banner.Title.Text == title && main.Banner.ContinueButton.Visible && runner.Simulation!.TickNumber == bannerTick,
                        $"{label}: mid-replay banner '{main.Banner.Title.Text}' at tick {runner.Simulation!.TickNumber} (want '{title}' at {bannerTick}), continue {main.Banner.ContinueButton.Visible}");
                    main.ContinueWatching();
                }
            }
            Check(runner.PlaybackState == PlaybackState.Ended && runner.CheckpointsCompared == replay!.Checkpoints.Length, $"{label}: playback {runner.PlaybackState} ({runner.PlaybackError})");
            if (continueFirst)
                Check(sawMid && main.Banner.Title.Text == _ui.LoadGame(LoadText.Ended) && !main.Banner.ContinueButton.Visible,
                    $"{label}: mid banner seen {sawMid}, end banner '{main.Banner.Title.Text}'");
            else
                Check(main.Banner.Visible && main.Banner.Title.Text == title && !main.Banner.ContinueButton.Visible && main.Banner.StatsButton.Visible,
                    $"{label}: end banner '{main.Banner.Title.Text}' (want '{title}'), continue {main.Banner.ContinueButton.Visible}");
            main.Banner.MenuButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);
        }
        finally
        {
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
    }

    // ---- (5) window close ----

    private async Task WindowClose()
    {
        Main main = NewMain(new[] { "--skirmish", "--mute" }, "close");
        main.ReplayDir = $"{_dir}/close";
        AddChild(main);
        await Frames(2);
        try
        {
            string? path = main.ReplayPath;
            Simulation? sim = main.CurrentMatch?.Simulation;
            if (!Check(path != null && sim != null, "close: no recorded match")) return;
            while (sim!.TickNumber < 150) await Frames(1);
            main.Notification((int)NotificationWMCloseRequest);
            Check(File.Exists(path) && ReplayFormat.TryReadFile(path!, out Replay? r) == ReplayError.None && r!.TickCount == sim.TickNumber,
                $"close: the window-close notification wrote {File.Exists(path)}");
            main.QuitToMenu();
            await Frames(2);
        }
        finally
        {
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
    }

    // ---- (7) the booted shell's folder ----

    // Main as a child of the root (as when the game boots) picks the player's user://replays; read only, nothing starts,
    // nothing is listed or written (Load Game is never opened here).
    private async Task BootedShellFolder()
    {
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.UserArgs = new[] { Main.NoConfineFlag };
        main.SettingsPath = $"{_dir}/booted/settings.cfg";
        GetTree().Root.AddChild(main);
        await Frames(2);
        Check(main.ReplayDir == ReplayStore.DefaultDir && main.CurrentMatch == null && main.ReplayPath == null,
            $"a root-level shell's replay folder '{main.ReplayDir}' (want {ReplayStore.DefaultDir}), match {main.CurrentMatch != null}");
        GetTree().Root.RemoveChild(main);
        Check(main.ReplayDir == null, $"a shell outside the tree reports folder '{main.ReplayDir}'");
        main.QueueFree();
        await Frames(2);
    }

    // ---- staging (QaM6V4Test's pattern) ----

    private async Task Bases(Simulation sim, int players)
    {
        World w = sim.World;
        NavGrid g = w.NavGrid;
        for (int p = 0; p < players; p++)
        {
            int c = FlowField.NearestPassable(g, Math.Min(Spots(g)[p].Y + 7, g.Height - 1) * g.Width + Spots(g)[p].X);
            sim.Enqueue(Command.SpawnUnit(p, StartBase.UnitOfSlot(_data, w.FactionOf(p), UnitSlot.Worker), g.CellCenter(c % g.Width, c / g.Width)));
        }
        await Ticks(sim, 6);
        for (int p = 0; p < players; p++)
        {
            int hall = DefeatStaging.Hall(_data, w, p);
            DefeatStaging.SpawnAt(sim, p, hall, DefeatStaging.Anchor(w, p, hall, Spots(g)[p].X, Spots(g)[p].Y));
        }
        await Ticks(sim, 3);
    }

    private static (int X, int Y)[] Spots(NavGrid g) => new[] { (g.Width / 4, g.Height / 2), (g.Width * 3 / 4, g.Height / 2), (g.Width / 2, g.Height / 5) };

    private void Army(Simulation sim, int player, EntityHandle target, int n)
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
    }

    private static EntityHandle BuildingOf(World w, int player)
    {
        BuildingStore b = w.Buildings;
        for (int i = 0; i < b.Capacity; i++) if (b.Alive[i] && b.Owner[i] == player) return new EntityHandle(i, b.Generation[i]);
        throw new InvalidOperationException($"player {player} has no building");
    }

    private async Task Ticks(Simulation sim, int n)
    {
        int until = sim.TickNumber + n;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (sim.TickNumber < until && clock.Elapsed.TotalSeconds < 30) await Frames(1);
        Check(sim.TickNumber >= until, $"tick {sim.TickNumber} after 30 s (want {until})");
    }

    // ---- helpers ----

    private Main NewMain(string[] args, string label)
    {
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.UserArgs = args.Append(Main.NoConfineFlag).ToArray();
        main.SettingsPath = $"{_dir}/{label}/settings.cfg";
        main.ReplayDir = _dir;
        main.BeforeStart = m => m.GetNode<SimRunner>("SimRunner").FixedFrameDelta = FrameSeconds;
        return main;
    }

    private static string PlayerSettingsMd5()
    {
        string p = ProjectSettings.GlobalizePath(GameSettings.DefaultPath);
        return File.Exists(p) ? Convert.ToHexString(MD5.HashData(File.ReadAllBytes(p))) : "none";
    }

    private static byte[] Reseal(string text)
    {
        int at = text.LastIndexOf("checksum ", StringComparison.Ordinal);
        string body = text[..at];
        ulong h = 14695981039346656037UL;
        foreach (byte b in Encoding.ASCII.GetBytes(body)) h = unchecked((h ^ b) * 1099511628211UL);
        return Encoding.ASCII.GetBytes($"{body}checksum {h:X16}\n");
    }

    private static string[] CommandLines(byte[] file) => Encoding.ASCII.GetString(file).Split('\n').Where(l => l.StartsWith("c ", StringComparison.Ordinal)).ToArray();

    private static EntityHandle OwnUnit(Simulation sim)
    {
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == SelectionController.LocalPlayer) return new EntityHandle(i, u.Generation[i]);
        return default;
    }

    private static int OwnBuilding(Simulation sim)
    {
        BuildingStore b = sim.World.Buildings;
        for (int i = 0; i < b.Capacity; i++)
            if (b.Alive[i] && b.Owner[i] == SelectionController.LocalPlayer && !b.UnderConstruction[i]) return i;
        return -1;
    }

    private void Push(InputEvent e) => GetViewport().PushInput(e);

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
