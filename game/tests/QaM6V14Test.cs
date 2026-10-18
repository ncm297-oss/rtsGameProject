using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.Replays;
using Rts.Sim.Save;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// QA M6-V14 (session 2026-10-18-1236): resume correctness and the save folder's edges, beyond <c>SaveLoadTest</c>.
/// (1) Tick 0: a <c>--skirmish --players 2 --ai 2 human --speed 2</c> match held at tick 0 saves three times in one second
/// (three names, byte-identical, the pending start-base commands inside); a control group and a selection are made; the
/// live match runs on to tick 300 recording its hash every tick. The tick-0 save loads (Load Game) with nothing selected,
/// every control group empty, the HUD's gold / wood / pop / K-L equal to the sim's, the minimap's fog and resource layers
/// equal to the original's at the same tick, the tree unpaused and no menu up; then 300 ticks hash-equal to the original.
/// (2) The banner tick: a staged Victory; Save from the banner's moment (Continue watching, Esc, Save in one frame); the
/// load shows the Victory banner again on its first tick. (3) <see cref="SaveStore.NewPath"/> skips a name a lone
/// <c>.replay</c> or <c>.sav</c> holds. (4) 200 saves in the folder (listed newest first; the timing printed); Delete of the
/// highlighted middle row removes exactly that file; a folder of 200 unreadable saves only (the note, no pick, Load / Watch
/// never act, Delete of a picked row); a save locked by another handle (an unreadable row while locked; Load of it while
/// locked says <c>save.read_failed</c>, no match); a save with no <c>.replay</c> beside loads. (5) <c>--load</c> with junk, a
/// folder, an empty path, a <c>.replay</c>, no value, and a <c>user://</c> path. (6) A save folder that is a file:
/// <c>save.write_failed</c>, the match paused and unharmed, no <c>.tmp</c> left. No row asserts the AI refusal.
/// Observations that are not product rules (the loaded game speed, a lone <c>.replay</c> in the save folder) print as
/// <c>QA NOTE</c> lines.
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6V14Test.tscn</c>; prints "QA M6V14 TEST PASS" or "QA M6V14 TEST FAIL: ..." lines.</remarks>
public partial class QaM6V14Test : Node
{
    private const double FrameSeconds = 0.25;
    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private UiText _ui = null!;
    private string _root = "", _rootFull = "";

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
            _root = $"user://qa-m6v14-{OS.GetProcessId()}";
            _rootFull = Full(_root);
            if (Directory.Exists(_rootFull)) Directory.Delete(_rootFull, true);
            string playerSaves = Folder(SaveStore.DefaultDir), playerReplays = Folder(ReplayStore.DefaultDir);

            string? good = await TickZero();
            await BannerTick();
            NewPathSkips();
            if (good != null)
            {
                await ManyFiles(good);
                await OnlyUnreadable(good);
                await Locked(good);
                await LoadFlags(good);
            }
            await WriteFails();

            Check(Folder(SaveStore.DefaultDir) == playerSaves && Folder(ReplayStore.DefaultDir) == playerReplays, "the player's folders changed");
            try { Directory.Delete(_rootFull, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        foreach (string f in _failures.Take(60)) GD.Print($"QA M6V14 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M6V14 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // (1) Tick 0, three saves in one second, a group and a selection, the live run's hashes, the load and 300 ticks.
    private async Task<string?> TickZero()
    {
        string saves = $"{_root}/t0/saves", replays = $"{_root}/t0/replays";
        double frame = 0.0; // held at tick 0 until the saves are made
        Main main = NewMain(new[] { "--skirmish", "--players", "2", "--ai", "2", "human", "--seed", "11", "--speed", "2", "--mute" }, "t0", saves, replays,
            m => m.GetNode<SimRunner>("SimRunner").FixedFrameDelta = frame);
        AddChild(main);
        await Frames(3);
        string? first = null;
        try
        {
            Match match = main.CurrentMatch!;
            SimRunner runner = match.GetNode<SimRunner>("SimRunner");
            Simulation sim = runner.Simulation!;
            if (!Check(sim.TickNumber == 0, $"held match at tick {sim.TickNumber}, want 0")) return null;
            int pending = sim.PendingCommandCount;
            System.Numerics.Vector2 startFocus = match.StartFocus;
            ulong hash0 = sim.StateHash();
            Check(pending > 0, "the tick-0 match has no pending start-base commands (the case under test)");

            main.PauseMatch();
            PauseMenu pause = main.Pause!;
            var paths = new List<string>();
            var clock = Stopwatch.StartNew();
            for (int k = 0; k < 3; k++)
            {
                pause.SaveButton.EmitSignal(BaseButton.SignalName.Pressed);
                if (main.LastSavePath is string p && !paths.Contains(p)) paths.Add(p);
            }
            Check(paths.Count == 3 && paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3, $"three saves in {clock.ElapsedMilliseconds} ms wrote {paths.Count} names: {string.Join(", ", paths.Select(Path.GetFileName))}");
            if (paths.Count == 0) return null;
            first = paths[0];
            byte[] bytes = File.ReadAllBytes(first);
            Check(paths.All(p => File.ReadAllBytes(p).SequenceEqual(bytes)), "the three tick-0 saves differ");
            Check(pause.SaveStatus.Text == _ui.SavedLine(0) && main.Paused && GetTree().Paused && pause.Visible, $"after the saves: status '{pause.SaveStatus.Text}', paused {main.Paused}");
            Check(sim.TickNumber == 0 && sim.StateHash() == hash0 && sim.PendingCommandCount == pending, "saving at tick 0 moved the sim or its queue");
            Check(!Directory.GetFiles(Full(saves)).Any(f => f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)), "a .tmp left in the save folder");
            string beside = SaveStore.ReplayPathOf(first);
            if (File.Exists(beside))
            {
                ReplayError e = ReplayFormat.TryReadFile(beside, out Replay? r);
                Check(e == ReplayError.None && r!.TickCount == 0, $"the tick-0 recording beside the save: {e}, {r?.TickCount} ticks");
            }
            else GD.Print("QA NOTE: a tick-0 save writes no .replay beside it");

            // A selection and a control group in the original: the load must not carry them.
            var sel = match.GetNode<SelectionController>("SelectionController");
            var hashes = new Dictionary<int, ulong>();
            runner.Ticked += s => hashes[s.TickNumber] = s.StateHash();
            main.Resume();
            frame = FrameSeconds;
            runner.FixedFrameDelta = FrameSeconds;
            await Until(() => sim.TickNumber >= 10, 30, "the original after resume");
            EntityHandle w = OwnUnit(sim);
            Check(w.Generation != 0 && sel.SelectOnly(w), "no own unit to select");
            sel.Groups.Assign(1, new[] { w });
            double speed = runner.GameSpeed;
            await Until(() => sim.TickNumber >= 300, 120, "the original to tick 300");
            Check(sim.TickNumber == 300, $"the original stopped at tick {sim.TickNumber}, want 300");
            (byte[] fog300, byte[] res300) = MinimapLayers(match, sim);
            main.QuitToMenu();
            await Frames(2);

            // Load Game: the tick-0 save, held at tick 0 for the fresh-view checks.
            frame = 0.0;
            main.Menu!.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(1);
            LoadGameScreen load = main.LoadScreen!;
            int row = load.RowOf(first);
            if (!Check(row >= 0 && load.Select(row), $"the tick-0 save is not listed (row {row})")) return first;
            Check(load.RowCount == 4, $"Load Game lists {load.RowCount} rows (want the 3 saves and the Quit's replay)");
            load.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
            Match? resumed = main.CurrentMatch;
            if (!Check(resumed is { IsLoaded: true }, "the tick-0 save did not load")) return first;
            await Frames(3);
            SimRunner runner2 = resumed!.GetNode<SimRunner>("SimRunner");
            Simulation sim2 = runner2.Simulation!;
            Check(sim2.TickNumber == 0 && sim2.StateHash() == hash0 && sim2.PendingCommandCount == pending, $"loaded tick {sim2.TickNumber}, pending {sim2.PendingCommandCount} (want {pending})");
            Check(!GetTree().Paused && !main.Paused && !main.BannerUp && !main.Pause!.Visible && !load.Visible && !main.Menu.Visible, "after load: something paused or a screen is up");
            var sel2 = resumed.GetNode<SelectionController>("SelectionController");
            Check(sel2.Selection.Count == 0, $"loaded selection {sel2.Selection.Count}");
            for (int g = 0; g < ControlGroups.Count; g++)
                Check(sel2.Groups.Items(g).Length == 0 && sel2.Groups.Building(g).Generation == 0, $"loaded control group {g} holds {sel2.Groups.Items(g).Length} units");
            CheckHud(resumed, sim2, "loaded at tick 0");
            System.Numerics.Vector2 loadedFocus = resumed.GetNode<RtsCamera>("RtsCamera").Focus;
            if (loadedFocus != startFocus)
                GD.Print($"QA NOTE: a tick-0 save loads with the camera at {loadedFocus} (the original started at its Town Hall, {startFocus}): the start bases are still queued commands");
            if (runner2.GameSpeed != speed) GD.Print($"QA NOTE: the match ran at {speed:0.##}x; loaded from Load Game it runs at {runner2.GameSpeed:0.##}x (the speed is not in the save)");

            // 300 ticks beside the original's hashes.
            int bad = -1, compared = 0;
            runner2.Ticked += s =>
            {
                compared++;
                if (bad < 0 && (!hashes.TryGetValue(s.TickNumber, out ulong h) || h != s.StateHash())) bad = s.TickNumber;
            };
            runner2.FixedFrameDelta = FrameSeconds;
            await Until(() => sim2.TickNumber >= 300, 120, "the loaded tick-0 save to 300");
            Check(bad < 0 && compared == 300 && sim2.TickNumber == 300, $"the loaded tick-0 save differs from the original at tick {bad} ({compared} compared, at {sim2.TickNumber})");
            CheckHud(resumed, sim2, "loaded at tick 300");
            (byte[] fogLoaded, byte[] resLoaded) = MinimapLayers(resumed, sim2);
            Check(fogLoaded.SequenceEqual(fog300), "the loaded match's minimap fog layer at tick 300 differs from the original's");
            Check(resLoaded.SequenceEqual(res300), "the loaded match's minimap resource layer at tick 300 differs from the original's");
            Check(sim2.World.Fog.Visibility(0).ToArray().Count(b => b > 0) > 0, "no explored cell for the local player after the load");
            main.QuitToMenu();
            await Frames(2);
            return first;
        }
        finally
        {
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
    }

    // (2) A staged Victory; saves at the banner tick (direct, and the UI path in one frame); the load shows the banner again.
    private async Task BannerTick()
    {
        string saves = $"{_root}/banner/saves";
        Main main = NewMain(new[] { "--units", "0", "--no-bases", "--mute", "--seed", "2" }, "banner", saves, $"{_root}/banner/replays",
            m => m.GetNode<SimRunner>("SimRunner").FixedFrameDelta = FrameSeconds);
        AddChild(main);
        await Frames(2);
        try
        {
            Match match = main.CurrentMatch!;
            Simulation sim = match.Simulation!;
            World w = sim.World;
            NavGrid g = w.NavGrid;
            (int X, int Y)[] at = { (g.Width / 4, g.Height / 2), (g.Width * 3 / 4, g.Height / 2) };
            for (int p = 0; p < 2; p++)
            {
                int hall = DefeatStaging.Hall(_data, w, p);
                DefeatStaging.SpawnAt(sim, p, hall, DefeatStaging.Anchor(w, p, hall, at[p].X, at[p].Y));
            }
            await Until(() => w.Buildings.Count == 2, 30, "the two halls");
            EntityHandle target = BuildingOf(w, 1);
            int siege = StartBase.UnitOfSlot(_data, w.FactionOf(0), UnitSlot.Siege);
            int cell = w.Buildings.Cell[target.Index], cx = cell % g.Width, cy = cell / g.Width;
            for (int i = 0; i < 6; i++)
            {
                int c = FlowField.NearestPassable(g, Math.Clamp(cy - 4 - i / 5 * 2, 0, g.Height - 1) * g.Width + Math.Clamp(cx - 3 + i % 5 * 2, 0, g.Width - 1));
                sim.Enqueue(Command.SpawnUnit(0, siege, g.CellCenter(c % g.Width, c / g.Width)));
            }
            int t = sim.TickNumber;
            await Until(() => sim.TickNumber >= t + 3, 30, "the siege spawn");
            for (int i = 0; i < w.Units.Capacity; i++)
                if (w.Units.Alive[i] && w.Units.Owner[i] == 0) sim.Enqueue(Command.Attack(0, new EntityHandle(i, w.Units.Generation[i]), target, isBuilding: true));
            await Until(() => main.BannerUp, 120, "the Victory banner");
            if (!Check(main.BannerUp && match.Outcome == MatchOutcome.Victory, $"no Victory banner (outcome {match.Outcome}, tick {sim.TickNumber})")) return;
            int bannerTick = sim.TickNumber;
            ulong hash = sim.StateHash();
            Check(main.SaveGame() && main.LastSavePath != null, "SaveGame under the banner wrote nothing");
            string direct = main.LastSavePath!;
            // The UI path at the same tick: Continue watching, Esc, Save, with no frame between.
            main.ContinueWatching();
            main.PauseMatch();
            main.Pause!.SaveButton.EmitSignal(BaseButton.SignalName.Pressed);
            string? viaUi = main.LastSavePath;
            Check(viaUi != null && viaUi != direct && sim.TickNumber == bannerTick && main.Pause.SaveStatus.Text == _ui.SavedLine(bannerTick)
                && File.ReadAllBytes(viaUi).SequenceEqual(File.ReadAllBytes(direct)), $"the banner-tick save via the pause menu: {viaUi}, tick {sim.TickNumber} (banner {bannerTick}), status '{main.Pause.SaveStatus.Text}'");
            Check(!File.Exists(SaveStore.ReplayPathOf(direct)), "an unrecorded match wrote a .replay beside its save");
            main.QuitToMenu();
            await Frames(2);

            Check(main.LoadSavedGame(direct), "the banner-tick save did not load");
            Match? resumed = main.CurrentMatch;
            if (resumed == null) return;
            Simulation sim2 = resumed.Simulation!;
            Check(sim2.TickNumber == bannerTick && sim2.StateHash() == hash && sim2.MatchOver, $"loaded banner save: tick {sim2.TickNumber}, over {sim2.MatchOver}");
            await Until(() => main.BannerUp || sim2.TickNumber > bannerTick + 20, 30, "the loaded banner save's first ticks");
            Check(main.BannerUp && main.Banner!.Shown == MatchOutcome.Victory && resumed.Outcome == MatchOutcome.Victory && sim2.TickNumber == bannerTick + 1,
                $"the loaded finished match: banner {main.BannerUp} ({main.Banner?.Shown}), outcome {resumed.Outcome}, at tick {sim2.TickNumber} (save {bannerTick})");
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

    // (3) NewPath against lone sidecars.
    private void NewPathSkips()
    {
        string dir = Full($"{_root}/names");
        Directory.CreateDirectory(dir);
        var when = new DateTime(2026, 10, 18, 12, 36, 5);
        string p0 = Path.Combine(dir, SaveStore.FileName(when, 9)), p1 = Path.Combine(dir, SaveStore.FileName(when, 9, 1));
        Check(SamePath(SaveStore.NewPath(dir, when, 9), p0), "NewPath in an empty folder");
        File.WriteAllText(SaveStore.ReplayPathOf(p0), "lone replay");
        Check(SamePath(SaveStore.NewPath(dir, when, 9), p1), "NewPath took the name a lone .replay holds");
        File.WriteAllText(p1, "lone save");
        Check(SamePath(SaveStore.NewPath(dir, when, 9), Path.Combine(dir, SaveStore.FileName(when, 9, 2))), "NewPath took a held .sav name");
        Check(ReplayStore.DateOf(Path.Combine(dir, SaveStore.FileName(when, 9, 5))) == when, "DateOf of a -6 copy's name");
    }

    // (4a) 200 saves; Delete of the highlighted middle row; a save with no .replay loads; a lone .replay is not listed.
    private async Task ManyFiles(string good)
    {
        string dir = Full($"{_root}/many/saves");
        Directory.CreateDirectory(dir);
        byte[] bytes = File.ReadAllBytes(good);
        var start = new DateTime(2021, 1, 1, 0, 0, 0);
        for (int i = 0; i < 200; i++) File.WriteAllBytes(Path.Combine(dir, SaveStore.FileName(start.AddMinutes(i), 11)), bytes);
        string lone = Path.Combine(dir, SaveStore.FileName(start.AddYears(1), 11).Replace(".sav", ".replay", StringComparison.Ordinal));
        File.WriteAllText(lone, "a recording whose save is gone");
        Main main = NewMain(Array.Empty<string>(), "many", $"{_root}/many/saves", $"{_root}/many/replays", null);
        AddChild(main);
        await Frames(2);
        try
        {
            var clock = Stopwatch.StartNew();
            main.Menu!.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
            clock.Stop();
            GD.Print($"QA NOTE: Load Game with 200 saves opened in {clock.Elapsed.TotalMilliseconds:0} ms");
            LoadGameScreen load = main.LoadScreen!;
            Check(load.RowCount == 200 && load.Saves.Count == 200 && load.Skipped == 0 && load.SelectedIndex == 0 && !load.LoadButton.Disabled,
                $"200 saves: {load.RowCount} rows, {load.Saves.Count} saves, {load.Skipped} skipped, picked {load.SelectedIndex}");
            bool ordered = true;
            for (int r = 0; r < load.RowCount; r++) ordered &= SamePath(load.RowPath(r)!, Path.Combine(dir, SaveStore.FileName(start.AddMinutes(199 - r), 11)));
            Check(ordered, "200 saves not newest first");
            Check(load.RowOf(lone) < 0, "a lone .replay in the save folder is listed");
            if (load.RowOf(lone) < 0) GD.Print("QA NOTE: a .replay in the save folder whose .sav is gone is not listed, so Load Game can't delete it");
            // Delete the highlighted middle row.
            string victim = load.RowPath(57)!, next = load.RowPath(58)!, before = load.RowPath(56)!;
            Check(load.Select(57) && load.SelectedIndex == 57, "picking row 57");
            load.DeleteButton.EmitSignal(BaseButton.SignalName.Pressed);
            Check(!File.Exists(victim) && load.RowCount == 199 && load.RowOf(victim) < 0 && load.RowOf(next) == 57 && load.RowOf(before) == 56
                && Directory.GetFiles(dir, "*.sav").Length == 199, $"Delete of row 57: {load.RowCount} rows, file gone {!File.Exists(victim)}");
            Check(File.Exists(lone), "Delete removed the lone .replay of another name");
            // A save with no .replay beside it loads.
            Check(!File.Exists(SaveStore.ReplayPathOf(load.RowPath(0)!)) && load.Select(0), "row 0 has a .replay beside it");
            load.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
            Check(main.CurrentMatch is { IsLoaded: true }, "a save with no .replay beside it did not load");
            if (main.CurrentMatch != null) { main.QuitToMenu(); await Frames(2); }
        }
        finally
        {
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
    }

    // (4b) A folder of 200 unreadable saves only.
    private async Task OnlyUnreadable(string good)
    {
        string dir = Full($"{_root}/junk/saves");
        Directory.CreateDirectory(dir);
        byte[] bytes = File.ReadAllBytes(good);
        var start = new DateTime(2022, 2, 2, 0, 0, 0);
        for (int i = 0; i < 200; i++)
        {
            byte[] junk = i % 3 == 0 ? Array.Empty<byte>() : i % 3 == 1 ? "junk junk"u8.ToArray() : bytes[..Math.Min(bytes.Length, 30)];
            File.WriteAllBytes(Path.Combine(dir, SaveStore.FileName(start.AddSeconds(i), 3)), junk);
        }
        Main main = NewMain(Array.Empty<string>(), "junk", $"{_root}/junk/saves", $"{_root}/junk/replays", null);
        AddChild(main);
        await Frames(2);
        try
        {
            main.Menu!.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
            LoadGameScreen load = main.LoadScreen!;
            Check(load.RowCount == 200 && load.Saves.Count == 0 && load.UnreadableSaves.Count == 200 && load.Note.Text == _ui.ReplaysSkipped(200)
                && load.SelectedIndex < 0 && load.LoadButton.Disabled && load.WatchButton.Disabled && load.DeleteButton.Disabled,
                $"200 unreadable: {load.RowCount} rows, note '{load.Note.Text}', picked {load.SelectedIndex}, Load {load.LoadButton.Disabled} Watch {load.WatchButton.Disabled} Delete {load.DeleteButton.Disabled}");
            Check(load.Select(10) && load.LoadButton.Disabled && load.WatchButton.Disabled && !load.DeleteButton.Disabled, "an unreadable row picked: buttons");
            load.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
            load.WatchButton.EmitSignal(BaseButton.SignalName.Pressed);
            load.List.EmitSignal(Tree.SignalName.ItemActivated);
            Check(main.CurrentMatch == null && load.Visible, "Load / Watch / a double-click on an unreadable row started something");
            string victim = load.RowPath(10)!;
            load.DeleteButton.EmitSignal(BaseButton.SignalName.Pressed);
            Check(!File.Exists(victim) && load.RowCount == 199 && load.Note.Text == _ui.ReplaysSkipped(199) && Directory.GetFiles(dir).Length == 199,
                $"Delete of an unreadable row: {load.RowCount} rows, note '{load.Note.Text}'");
        }
        finally
        {
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
    }

    // (4c) A save held open by another handle with no sharing.
    private async Task Locked(string good)
    {
        string dir = Full($"{_root}/lock/saves");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, SaveStore.FileName(new DateTime(2023, 3, 3, 3, 3, 3), 11));
        File.Copy(good, path);
        Main main = NewMain(Array.Empty<string>(), "lock", $"{_root}/lock/saves", $"{_root}/lock/replays", null);
        AddChild(main);
        await Frames(2);
        try
        {
            using (new FileStream(path, FileMode.Open, System.IO.FileAccess.ReadWrite, FileShare.None))
            {
                main.Menu!.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
                LoadGameScreen l = main.LoadScreen!;
                Check(l.RowCount == 1 && l.KindOf(0) == LoadGameScreen.RowKind.UnreadableSave && l.Note.Text == _ui.ReplaysSkipped(1), $"a locked save: kind {l.KindOf(0)}, note '{l.Note.Text}'");
                Check(!main.LoadSavedGame(path) && main.CurrentMatch == null && l.Note.Text == _ui.Save(SaveText.ReadFailed), $"Load of a locked save: note '{l.Note.Text}'");
                l.BackButton.EmitSignal(BaseButton.SignalName.Pressed);
            }
            main.Menu!.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
            LoadGameScreen load = main.LoadScreen!;
            Check(load.RowCount == 1 && load.KindOf(0) == LoadGameScreen.RowKind.Save, "the save after the lock went");
            // Listed while free, then locked before Load.
            using (new FileStream(path, FileMode.Open, System.IO.FileAccess.ReadWrite, FileShare.None))
            {
                load.Select(0);
                load.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
                Check(main.CurrentMatch == null && load.Visible && load.Note.Text == _ui.Save(SaveText.ReadFailed), $"Load of a save locked after listing: note '{load.Note.Text}'");
                load.DeleteButton.EmitSignal(BaseButton.SignalName.Pressed); // Delete of a locked file: no crash, the row stays
                Check(File.Exists(path) && load.RowCount == 1, "Delete of a locked save");
            }
        }
        finally
        {
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
    }

    // (5) --load edges.
    private async Task LoadFlags(string good)
    {
        string dir = Full($"{_root}/flags");
        Directory.CreateDirectory(dir);
        string junk = Path.Combine(dir, "junk.sav");
        File.WriteAllText(junk, "this is not a save");
        string replay = Directory.GetFiles(Full($"{_root}/t0/replays"), "*.replay").FirstOrDefault() ?? junk;
        var cases = new (string[] Args, string Want, string Label)[]
        {
            (new[] { Main.LoadFlag, junk }, _ui.SaveErrorText(SaveError.BadMagic), "junk"),
            (new[] { Main.LoadFlag, dir }, _ui.Save(SaveText.ReadFailed), "a folder"),
            (new[] { Main.LoadFlag, "" }, _ui.Save(SaveText.ReadFailed), "an empty path"),
            (new[] { Main.LoadFlag, replay }, _ui.SaveErrorText(SaveError.BadMagic), "a .replay"),
        };
        foreach ((string[] args, string want, string label) in cases)
        {
            Main boot = NewMain(args.Append("--mute").ToArray(), "flag", $"{_root}/flags/saves", $"{_root}/flags/replays", null);
            AddChild(boot);
            await Frames(2);
            Check(boot.CurrentMatch == null && boot.LoadScreen is { Visible: true } && boot.LoadScreen.Note.Text == want,
                $"--load {label}: match {boot.CurrentMatch != null}, Load Game {boot.LoadScreen?.Visible}, note '{boot.LoadScreen?.Note.Text}' (want '{want}')");
            RemoveChild(boot);
            boot.QueueFree();
            await Frames(2);
        }
        Main none = NewMain(new[] { Main.LoadFlag }, "flag", $"{_root}/flags/saves", $"{_root}/flags/replays", null);
        AddChild(none);
        await Frames(2);
        Check(none.CurrentMatch == null && none.Menu is { Visible: true }, "--load with no value: not the menu");
        RemoveChild(none);
        none.QueueFree();
        await Frames(2);
        // A user:// path.
        string rel = Path.GetRelativePath(Full("user://"), good).Replace('\\', '/');
        Main user = NewMain(new[] { Main.LoadFlag, $"user://{rel}", "--mute" }, "flag", $"{_root}/flags/saves", $"{_root}/flags/replays", null);
        AddChild(user);
        await Frames(2);
        Check(user.CurrentMatch is { IsLoaded: true }, $"--load user://{rel} did not load");
        if (user.CurrentMatch != null) { user.QuitToMenu(); await Frames(2); }
        RemoveChild(user);
        user.QueueFree();
        await Frames(2);
    }

    // (6) The save folder is a file.
    private async Task WriteFails()
    {
        string blocker = Full($"{_root}/blocked");
        Directory.CreateDirectory(Path.GetDirectoryName(blocker)!);
        File.WriteAllText(blocker, "a file where the save folder should be");
        Main main = NewMain(new[] { "--skirmish", "--players", "2", "--ai", "2", "human", "--seed", "4", "--mute" }, "blocked", $"{_root}/blocked", $"{_root}/blocked-replays",
            m => m.GetNode<SimRunner>("SimRunner").FixedFrameDelta = FrameSeconds);
        AddChild(main);
        await Frames(4);
        try
        {
            Simulation sim = main.CurrentMatch!.Simulation!;
            main.PauseMatch();
            int tick = sim.TickNumber;
            ulong hash = sim.StateHash();
            main.Pause!.SaveButton.EmitSignal(BaseButton.SignalName.Pressed);
            Check(main.Pause.SaveStatus.Text == _ui.Save(SaveText.WriteFailed) && main.Paused && sim.TickNumber == tick && sim.StateHash() == hash && File.Exists(blocker),
                $"a save into a file-folder: status '{main.Pause.SaveStatus.Text}', paused {main.Paused}");
            main.Resume();
            await Until(() => sim.TickNumber >= tick + 20, 30, "the match after a failed save");
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

    private void CheckHud(Match match, Simulation sim, string at)
    {
        World w = sim.World;
        var bar = match.GetNode<ResourceBar>("Hud/ResourceBar");
        bar.Sync(w);
        Check(bar.ShownGold == w.Gold[0] && bar.ShownWood == w.Wood[0] && bar.ShownHalfPop == w.HalfPop[0] && bar.ShownHalfPopCap == w.HalfPopCap[0]
            && bar.ShownKills == w.Kills[0] && bar.ShownLosses == w.Losses[0],
            $"{at}: HUD gold {bar.ShownGold}/{w.Gold[0]} wood {bar.ShownWood}/{w.Wood[0]} pop {bar.ShownHalfPop}/{w.HalfPop[0]} cap {bar.ShownHalfPopCap}/{w.HalfPopCap[0]}");
    }

    // The minimap's fog and resource layers as drawn now (brought up to the sim first).
    private static (byte[] Fog, byte[] Resources) MinimapLayers(Match match, Simulation sim)
    {
        var minimap = match.GetNode<Minimap>("Hud/Minimap");
        minimap.Refresh(sim);
        minimap.SyncFog(sim.World);
        return (minimap.FogImage.GetData(), minimap.ResourcesImage.GetData());
    }

    private Main NewMain(string[] args, string label, string saves, string replays, Action<Match>? before)
    {
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.UserArgs = args.Append(Main.NoConfineFlag).ToArray();
        main.SettingsPath = $"{_root}/{label}/settings.cfg";
        main.ReplayDir = replays;
        main.SaveDir = saves;
        if (before != null) main.BeforeStart = before;
        return main;
    }

    private static EntityHandle OwnUnit(Simulation sim)
    {
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == SelectionController.LocalPlayer) return new EntityHandle(i, u.Generation[i]);
        return default;
    }

    private static EntityHandle BuildingOf(World w, int player)
    {
        BuildingStore b = w.Buildings;
        for (int i = 0; i < b.Capacity; i++) if (b.Alive[i] && b.Owner[i] == player) return new EntityHandle(i, b.Generation[i]);
        throw new InvalidOperationException($"player {player} has no building");
    }

    private async Task Until(Func<bool> done, double seconds, string what)
    {
        var clock = Stopwatch.StartNew();
        while (!done() && clock.Elapsed.TotalSeconds < seconds) await Frames(1);
        Check(done(), $"{what}: timed out after {seconds} s");
    }

    private static string Full(string dir) => Path.GetFullPath(ProjectSettings.GlobalizePath(dir));

    private static string Folder(string dir)
    {
        string full = ProjectSettings.GlobalizePath(dir);
        if (!Directory.Exists(full)) return "";
        return string.Join("|", Directory.GetFiles(full).OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => $"{Path.GetFileName(f)}:{new FileInfo(f).Length}:{File.GetLastWriteTimeUtc(f).Ticks}"));
    }

    private static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

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
