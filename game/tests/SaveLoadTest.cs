using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Replays;
using Rts.Sim.Save;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// M6-V14: the pause menu's Save, the saves on the Load Game list beside the replays, and a load that resumes the match.
/// (1) A <c>--skirmish --players 2</c> human twin (<c>--ai 2 human</c>; seed 7) with orders at ticks 100 / 200 / 300 runs to
/// tick 500 through the hosted shell; the pause menu's Save writes <c>&lt;yyyyMMdd-HHmmss&gt;-seed7.sav</c> to the test's
/// folder with the recording so far beside it (<c>.replay</c>, 500 ticks), shows <c>save.saved</c> with 0:25, moves no sim
/// state, and a second Save is byte-identical. Quit to menu; Load Game lists the save (date, seed, players, 0:25, the Save tag)
/// beside the Quit's replay (the Replay tag); Load resumes it at tick 500 with the saved hash, no recorder, no selection, the
/// camera on the start Town Hall as at the start, the slot colours and seats of the save, then 1,000 more ticks hash-equal
/// every tick to an unsaved twin (a sim fed the recording's commands to tick 500, then the same commands as the loaded match:
/// a train at 700, a move at 900). The loaded match's pause menu shows <c>save.not_recorded</c>; its Save writes a <c>.sav</c>
/// with no recording; Quit writes no replay; Delete on Load Game removes a save and the recording beside it.
/// (2) Corrupted files: junk and a forged header are unreadable rows after the good ones; a flipped byte, a cut copy, version
/// 99, another data hash, a wrong length and the junk are each refused with their <c>save.*</c> text and start nothing; the
/// good save stays listed first and loads. (3) <c>--load &lt;path&gt;</c> boots into the match; a missing file shows
/// <c>save.read_failed</c>. (4) A hosted shell with no save folder shows no Save. The player's own folders are unchanged.
/// No row asserts that a match with an AI seat is refused (it stops being true when the sim's slice 2 merges).
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/SaveLoadTest.tscn</c> (windowed with <c>-- --shots &lt;dir&gt;</c>:
/// <c>save-pause-saved.png</c>, <c>save-load-list.png</c>, <c>save-pause-loaded.png</c>, <c>save-load-refused.png</c>); prints "SAVE LOAD TEST PASS" or "SAVE LOAD TEST FAIL: ..." lines.</remarks>
public partial class SaveLoadTest : Node
{
    /// <summary>Game time each frame advances every runner here: five ticks, the clock's cap, so the run is the same however loaded the machine is.</summary>
    public const double FrameSeconds = 0.25;

    /// <summary>The tick the twin saves at (brief M6-V14).</summary>
    public const int SaveTick = 500;

    /// <summary>Ticks the loaded match runs beside the unsaved twin.</summary>
    public const int MoreTicks = 1000;

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private UiText _ui = null!;
    private string? _shots;
    private string _root = "", _saves = "", _replays = "", _savesFull = "", _replaysFull = "";

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // the shell pauses the tree
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
            // BUG-0770's rule: per-process folders, never the player's user://saves or user://replays.
            _root = $"user://save-load-test-{OS.GetProcessId()}";
            _saves = $"{_root}/saves";
            _replays = $"{_root}/replays";
            _savesFull = Path.GetFullPath(ProjectSettings.GlobalizePath(_saves));
            _replaysFull = Path.GetFullPath(ProjectSettings.GlobalizePath(_replays));
            string rootFull = Path.GetFullPath(ProjectSettings.GlobalizePath(_root));
            if (Directory.Exists(rootFull)) Directory.Delete(rootFull, true);
            string playerSaves = Folder(SaveStore.DefaultDir), playerReplays = Folder(ReplayStore.DefaultDir);

            TextRows();
            string? save = await SaveAndResume();
            if (save != null)
            {
                await Corrupted(save);
                await LoadFlag(save);
            }
            await HostedWithoutFolder();
            await AiSeat();
            await GeneratorAndSpeed();

            Check(Folder(SaveStore.DefaultDir) == playerSaves && Folder(ReplayStore.DefaultDir) == playerReplays,
                "the player's user://saves or user://replays folder changed over the run (BUG-0770's rule)");
            try { Directory.Delete(rootFull, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        foreach (string f in _failures.Take(60)) GD.Print($"SAVE LOAD TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("SAVE LOAD TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // The error texts and the header reader on bad bytes.
    private void TextRows()
    {
        foreach (SaveError e in Enum.GetValues<SaveError>())
            if (e != SaveError.None) Check(_ui.SaveErrorText(e).Length > 0, $"SaveError.{e} has no save.* text");
        Check(SaveStore.FileName(new DateTime(2026, 10, 18, 12, 36, 5), 7) == "20261018-123605-seed7.sav"
            && SaveStore.FileName(new DateTime(2026, 10, 18, 12, 36, 5), 7, 1) == "20261018-123605-seed7-2.sav", "SaveStore.FileName");
        Check(SaveStore.ReplayPathOf("C:/x/20261018-123605-seed7.sav").EndsWith("20261018-123605-seed7.replay", StringComparison.Ordinal), "SaveStore.ReplayPathOf");
        foreach (byte[] junk in new[] { Array.Empty<byte>(), "RTSSAVE"u8.ToArray(), "RTSSAVE\0"u8.ToArray(), "not a save at all, just text"u8.ToArray(), new byte[64] })
            Check(!SaveSummary.TryParse(junk, out SaveSummary? s) && s == null, $"SaveSummary read {junk.Length} junk bytes");
    }

    // (1): save at tick 500, quit, list, load, 1,000 more ticks beside the unsaved twin, the loaded match's pause menu, Delete.
    private async Task<string?> SaveAndResume()
    {
        Main main = NewMain(new[] { "--skirmish", "--players", "2", "--ai", "2", "human", "--seed", "7", "--mute" }, "save");
        AddChild(main);
        await Frames(2);
        try
        {
            Match? match = main.CurrentMatch;
            if (!Check(match != null && !match.IsPlayback && !match.IsLoaded, "--skirmish started no live match")) return null;
            SimRunner runner = match!.GetNode<SimRunner>("SimRunner");
            Simulation sim = runner.Simulation!;
            if (!Check(!sim.World.Config.HasAiSeat && runner.Seats.Length == 2 && runner.Recorder != null && match.Bases != null,
                $"the human twin: seats [{string.Join(", ", runner.Seats)}], recorder {runner.Recorder != null}")) return null;
            var sel = match.GetNode<SelectionController>("SelectionController");
            int step = 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (sim.TickNumber < SaveTick && clock.Elapsed.TotalSeconds < 120)
            {
                await Frames(1);
                if (step == 0 && sim.TickNumber >= 100) { step++; MoveOrder(sel, sim, 6f); }
                else if (step == 1 && sim.TickNumber >= 200) { step++; sim.Enqueue(TrainWorker(match)); }
                else if (step == 2 && sim.TickNumber >= 300) { step++; MoveOrder(sel, sim, -5f); }
            }
            Check(sim.TickNumber == SaveTick && step == 3, $"stopped at tick {sim.TickNumber}, want {SaveTick} (five ticks a frame), orders {step}");
            ulong hash = sim.StateHash();
            ImmutableArray<PlayerSeat> seats = runner.Seats;
            System.Numerics.Vector2 startFocus = match.StartFocus;
            uint[] colours = match.PlayerRgb.ToArray();
            Command trainLater = TrainWorker(match); // the loaded match has no start-base plan (its bases are in the save)

            // Save from the pause menu.
            main.PauseMatch();
            PauseMenu pause = main.Pause!;
            Check(main.Paused && pause.SaveButton.Visible && !pause.SaveButton.Disabled && pause.SaveButton.Text == _ui.Pause(PauseText.Save)
                && pause.SaveStatus.Text == "" && !pause.RecordNote.Visible, $"pause menu: Save visible {pause.SaveButton.Visible}, status '{pause.SaveStatus.Text}', note {pause.RecordNote.Visible}");
            Check(pause.SaveButton.GetIndex() == pause.ResumeButton.GetIndex() + 1, "Save is not right under Resume");
            pause.SaveButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(1);
            string? path = main.LastSavePath;
            if (!Check(path != null && File.Exists(path) && path.StartsWith(_savesFull, StringComparison.Ordinal), $"Save wrote no file in {_savesFull} ({path})")) return null;
            Check(Regex.IsMatch(Path.GetFileName(path!), @"^\d{8}-\d{6}-seed7\.sav$"), $"save file name {Path.GetFileName(path)}");
            Check(pause.SaveStatus.Text == _ui.SavedLine(SaveTick) && pause.SaveStatus.Text.Contains("0:25"), $"after Save: status '{pause.SaveStatus.Text}'");
            await Shot("pause-saved");
            Check(sim.TickNumber == SaveTick && sim.StateHash() == hash && main.Paused, "the save moved the sim or unpaused");
            string companion = SaveStore.ReplayPathOf(path!);
            ReplayError read = ReplayFormat.TryReadFile(companion, out Replay? recording);
            if (!Check(read == ReplayError.None && recording != null && recording.TickCount == SaveTick && recording.Seed == 7,
                $"the recording beside the save: {read}, {recording?.TickCount} ticks")) return null;
            // A second save of the same moment: a new name, the same bytes (save -> save is byte-identical).
            pause.SaveButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(1);
            string? again = main.LastSavePath;
            Check(again != null && again != path && File.Exists(again) && File.ReadAllBytes(again).SequenceEqual(File.ReadAllBytes(path!))
                && Path.GetFileName(again).Contains("-seed7", StringComparison.Ordinal), $"second save {again}: not a byte-identical new file");
            if (again != null && again != path)
            {
                Check(SaveStore.Delete(again) && !File.Exists(again) && !File.Exists(SaveStore.ReplayPathOf(again)), "SaveStore.Delete left the save or its recording");
            }
            pause.QuitToMenuButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);
            Check(main.CurrentMatch == null && main.Menu is { Visible: true } && !GetTree().Paused, "Quit to menu after the save");
            string[] replaysAfterQuit = Directory.Exists(_replaysFull) ? Directory.GetFiles(_replaysFull) : Array.Empty<string>();
            Check(replaysAfterQuit.Length == 1, $"{replaysAfterQuit.Length} replays after Quit to menu, want the match's one");

            // The unsaved twin: the recording's commands to tick 500.
            Simulation twin = TwinOf(recording!);
            Check(twin.TickNumber == SaveTick && twin.StateHash() == hash, $"the twin at tick {twin.TickNumber}: hash differs from the saved match's");

            // Load Game: the save beside the replay.
            main.Menu!.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(1);
            LoadGameScreen load = main.LoadScreen!;
            Check(load.Visible && load.List.Columns == 5 && load.List.GetColumnTitle(4) == _ui.LoadGame(LoadText.Kind)
                && load.LoadButton.Text == _ui.LoadGame(LoadText.Load), $"Load Game: visible {load.Visible}, {load.List.Columns} columns");
            int row = load.RowOf(path!);
            int replayRow = replaysAfterQuit.Length == 1 ? load.RowOf(replaysAfterQuit[0]) : -1;
            if (!Check(row == 0 && load.KindOf(row) == LoadGameScreen.RowKind.Save && load.Saves.Count == 1, $"the save is row {row} of {load.RowCount} (want the newest, 0)")) return path;
            TreeItem item = load.List.GetRoot().GetChild(row);
            string players = load.SeatsText(recording!.Seats);
            Check(item.GetText(0) == _ui.ReplayDate(ReplayStore.DateOf(path!)) && item.GetText(1) == "7" && item.GetText(2) == players && players.Length > 0
                && item.GetText(3) == _ui.Clock(SaveTick) && item.GetText(3) == "0:25" && item.GetText(4) == _ui.LoadGame(LoadText.SaveTag),
                $"save row [{item.GetText(0)} | {item.GetText(1)} | {item.GetText(2)} | {item.GetText(3)} | {item.GetText(4)}]");
            Check(load.Saves[0].Seats.SequenceEqual(recording.Seats) && load.Saves[0].Tick == SaveTick && load.Saves[0].Seed == 7, "the save entry's header");
            Check(replayRow > 0 && load.KindOf(replayRow) == LoadGameScreen.RowKind.Replay && load.List.GetRoot().GetChild(replayRow).GetText(4) == _ui.LoadGame(LoadText.ReplayTag)
                && load.List.GetRoot().GetChild(replayRow).GetText(3) == _ui.Clock(SaveTick), $"the replay row {replayRow}");
            Check(!load.LoadButton.Disabled && load.WatchButton.Disabled && !load.DeleteButton.Disabled, "the save row picked: Load / Watch / Delete");
            Check(load.Select(replayRow) && load.LoadButton.Disabled && !load.WatchButton.Disabled, "the replay row picked: Load / Watch");
            Check(load.Select(row), "picking the save row");
            await Shot("load-list");

            // Load: the match resumes at the saved tick, the views fresh, beside the twin.
            load.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
            Match? resumed = main.CurrentMatch;
            if (!Check(resumed != null && resumed.IsLoaded && !resumed.IsPlayback && SamePath(resumed.SavePath!, path!) && !load.Visible,
                "Load started no loaded match")) return path;
            SimRunner runner2 = resumed!.GetNode<SimRunner>("SimRunner");
            Simulation sim2 = runner2.Simulation!;
            int compared = 0, firstBad = -1;
            runner2.Ticked += s =>
            {
                twin.Tick();
                compared++;
                if (firstBad < 0 && (twin.TickNumber != s.TickNumber || twin.StateHash() != s.StateHash())) firstBad = s.TickNumber;
            };
            Check(sim2.TickNumber == SaveTick && sim2.StateHash() == hash, $"loaded at tick {sim2.TickNumber}, hash equal {sim2.StateHash() == hash}");
            Check(runner2.Recorder == null && main.ReplayPath == null && runner2.FixedFrameDelta == FrameSeconds, $"loaded match records (recorder {runner2.Recorder != null}, path {main.ReplayPath})");
            Check(runner2.Seats.SequenceEqual(seats) && resumed.Seats.SequenceEqual(seats), $"loaded seats [{string.Join(", ", runner2.Seats)}], saved [{string.Join(", ", seats)}]");
            Check(resumed.PlayerRgb.SequenceEqual(colours), "loaded colours are not the slot order's");
            var camera = resumed.GetNode<RtsCamera>("RtsCamera");
            Check(resumed.StartFocus == startFocus && camera.Focus == startFocus, $"loaded camera {camera.Focus}, start {startFocus}");
            var sel2 = resumed.GetNode<SelectionController>("SelectionController");
            Check(sel2.Selection.Count == 0 && sel2.OrdersEnabled, $"loaded selection {sel2.Selection.Count}, orders {sel2.OrdersEnabled}");
            Check(resumed.GetNode<CanvasLayer>("Hud").Visible && resumed.GetNode<Minimap>("Hud/Minimap").Visible && resumed.GetNodeOrNull("Hud/ReplayMark") == null, "loaded HUD");
            int step2 = 0;
            clock.Restart();
            while (sim2.TickNumber < SaveTick + MoreTicks && clock.Elapsed.TotalSeconds < 180)
            {
                await Frames(1);
                if (step2 == 0 && sim2.TickNumber >= 700)
                {
                    step2++;
                    sim2.Enqueue(trainLater);
                    twin.Enqueue(trainLater);
                    EntityHandle w = OwnUnit(sim2);
                    Check(w.Generation != 0 && sel2.SelectOnly(w) && sel2.Selection.Count == 1, "no own unit selectable in the loaded match");
                }
                else if (step2 == 1 && sim2.TickNumber >= 900)
                {
                    step2++;
                    EntityHandle w = OwnUnit(sim2);
                    System.Numerics.Vector2 at = sim2.World.Units.Position[w.Index];
                    Command move = Command.Move(SelectionController.LocalPlayer, w, at + new System.Numerics.Vector2(4f, -3f));
                    sim2.Enqueue(move);
                    twin.Enqueue(move);
                }
            }
            Check(sim2.TickNumber == SaveTick + MoreTicks && compared == MoreTicks && firstBad < 0 && step2 == 2,
                $"loaded match at tick {sim2.TickNumber}, {compared} ticks compared, first differing tick {firstBad}, orders {step2}");

            // The loaded match's pause menu: not recorded; Save writes a .sav and no recording; Quit writes no replay.
            main.PauseMatch();
            Check(pause.RecordNote.Visible && pause.RecordNote.Text == _ui.Save(SaveText.NotRecorded) && pause.SaveButton.Visible && pause.SaveStatus.Text == "",
                $"loaded pause menu: note {pause.RecordNote.Visible} '{pause.RecordNote.Text}', Save {pause.SaveButton.Visible}");
            await Frames(1);
            // BUG-1002 item 3 (M6-VH4): the save lines sit inside the menu's opaque panel, not on the HUD's command card.
            Rect2 panel = pause.Panel.GetGlobalRect();
            Check(pause.Panel.Visible && pause.Panel.IsVisibleInTree() && PauseMenu.PanelColor.A >= 1f
                && panel.Encloses(pause.RecordNote.GetGlobalRect()) && panel.Encloses(pause.SaveStatus.GetGlobalRect()) && panel.Encloses(pause.QuitToMenuButton.GetGlobalRect()),
                $"BUG-1002: pause panel {panel}, note {pause.RecordNote.GetGlobalRect()}, status {pause.SaveStatus.GetGlobalRect()}");
            await Shot("pause-loaded");
            pause.SaveButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(1);
            string? path2 = main.LastSavePath;
            Check(path2 != null && path2 != path && File.Exists(path2) && !File.Exists(SaveStore.ReplayPathOf(path2)) && pause.SaveStatus.Text == _ui.SavedLine(SaveTick + MoreTicks),
                $"loaded Save: {path2}, recording beside {path2 != null && File.Exists(SaveStore.ReplayPathOf(path2))}, status '{pause.SaveStatus.Text}'");
            pause.QuitToMenuButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);
            Check(main.CurrentMatch == null && Directory.GetFiles(_replaysFull).Length == 1, $"Quit from the loaded match: {Directory.GetFiles(_replaysFull).Length} replays (want 1: a loaded match is not recorded)");

            // Delete: the loaded match's save goes from the list and the folder.
            main.Menu.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(1);
            int row2 = path2 != null ? load.RowOf(path2) : -1;
            Check(row2 >= 0 && load.Saves.Count == 2 && load.Select(row2), $"the loaded match's save is row {row2} of {load.RowCount}");
            load.DeleteButton.EmitSignal(BaseButton.SignalName.Pressed);
            Check(path2 != null && !File.Exists(path2) && load.RowOf(path2) < 0 && load.Saves.Count == 1 && load.RowOf(path!) == 0, "Delete did not remove the save");
            load.BackButton.EmitSignal(BaseButton.SignalName.Pressed);
            return path;
        }
        finally
        {
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
    }

    // (2): damaged copies of the good save, listed and refused with their texts; the good save stays first and loads.
    private async Task Corrupted(string good)
    {
        byte[] bytes = File.ReadAllBytes(good);
        int svLength = BitConverter.ToInt32(bytes, 20);
        int hashAt = 24 + svLength, playersAt = hashAt + 16;
        string P(string stamp) => Path.Combine(_savesFull, $"2020{stamp}-seed7.sav");
        var cases = new List<(string Path, byte[] Bytes, string Want, bool Listed)>
        {
            (P("0101-000001"), "not a save, only text"u8.ToArray(), _ui.SaveErrorText(SaveError.BadMagic), false),
            (P("0101-000002"), Flip(bytes, bytes.Length / 2), _ui.SaveErrorText(SaveError.ChecksumMismatch), true),
            (P("0101-000003"), bytes[..(bytes.Length / 2)], _ui.SaveErrorText(SaveError.Truncated), true),
            (P("0101-000004"), Patch(bytes, 8, 99, reseal: false), _ui.SaveErrorText(SaveError.FormatVersionMismatch), true),
            (P("0101-000005"), Patch(bytes, hashAt, unchecked((int)0xBAD5EED), reseal: true), _ui.SaveErrorText(SaveError.DataMismatch), true),
            (P("0101-000006"), Patch(bytes, playersAt, 0, reseal: true), _ui.SaveErrorText(SaveError.InvalidHeader), false),
            (P("0101-000007"), PatchLong(bytes, 12, bytes.Length - 1), _ui.SaveErrorText(SaveError.Malformed), true),
        };
        foreach ((string path, byte[] b, _, _) in cases) File.WriteAllBytes(path, b);
        // M6-V15 (BUG-1000): a replay damaged past its header (listed, refused at Watch), older than every good row.
        string[] goodReplays = Directory.Exists(_replaysFull) ? Directory.GetFiles(_replaysFull) : Array.Empty<string>();
        string badReplay = Path.Combine(_replaysFull, "20200101-000009-seed7.replay");
        if (goodReplays.Length == 1) File.WriteAllBytes(badReplay, BumpDigit(File.ReadAllBytes(goodReplays[0])));
        Main main = NewMain(Array.Empty<string>(), "corrupt");
        AddChild(main);
        await Frames(2);
        try
        {
            main.Menu!.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(1);
            LoadGameScreen load = main.LoadScreen!;
            int listed = cases.Count(c => c.Listed);
            Check(load.RowOf(good) == 0 && load.Saves.Count == 1 + listed && load.UnreadableSaves.Count == cases.Count - listed
                && load.Note.Text == _ui.ReplaysSkipped(cases.Count - listed), $"corrupted list: good at row {load.RowOf(good)}, {load.Saves.Count} saves, {load.UnreadableSaves.Count} unreadable, note '{load.Note.Text}'");
            foreach ((string path, _, string want, bool isListed) in cases)
            {
                int row = load.RowOf(path);
                LoadGameScreen.RowKind kind = load.KindOf(row);
                Check(row >= 0 && kind == (isListed ? LoadGameScreen.RowKind.Save : LoadGameScreen.RowKind.UnreadableSave) && load.Select(row)
                    && load.LoadButton.Disabled == !isListed && !load.DeleteButton.Disabled, $"{Path.GetFileName(path)}: row {row} ({kind}), Load disabled {load.LoadButton.Disabled}");
                if (!isListed)
                    Check(row > listed && load.List.GetRoot().GetChild(row).GetText(2) == _ui.LoadGame(LoadText.Unreadable)
                        && load.List.GetRoot().GetChild(row).GetText(4) == _ui.LoadGame(LoadText.SaveTag), $"{Path.GetFileName(path)}: unreadable row {row} not after the good ones");
                bool started = isListed ? Press(main, load.LoadButton) : main.LoadSavedGame(path);
                Check(!started && main.CurrentMatch == null && load.Visible && load.Note.Text == want && want.Length > 0,
                    $"{Path.GetFileName(path)}: started {started || main.CurrentMatch != null}, note '{load.Note.Text}' (want '{want}')");
                // BUG-1000: the refused file's row stays picked under its note (it was row 0, the good save).
                Check(load.SelectedIndex == load.RowOf(path) && load.RowPath(load.SelectedIndex) is string picked && SamePath(picked, path),
                    $"BUG-1000: after the refused {Path.GetFileName(path)} (row {load.RowOf(path)}) row {load.SelectedIndex} is picked ({load.RowPath(load.SelectedIndex)})");
            }
            await RefusedRowKept(main, load, good, cases[1].Path, badReplay, cases.Select(c => c.Path).ToArray());
            await Shot("load-refused");
            // Delete an unreadable row; the good save still loads.
            string junk = cases[0].Path;
            Check(load.Select(load.RowOf(junk)), "picking the junk row");
            load.DeleteButton.EmitSignal(BaseButton.SignalName.Pressed);
            Check(!File.Exists(junk) && load.RowOf(junk) < 0 && load.RowOf(good) == 0, "Delete of an unreadable save");
            Check(load.Select(0) && Press(main, load.LoadButton) && main.CurrentMatch is { IsLoaded: true }, "the good save did not load after the refusals");
            foreach ((string path, _, _, _) in cases) SaveStore.Delete(path);
        }
        finally
        {
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
    }

    // BUG-1000: a refused Load and a refused Watch of a file that is not row 0 keep it picked; Delete then removes exactly
    // it; refused again with the file gone (it isn't listed), row 0 is picked.
    private async Task RefusedRowKept(Main main, LoadGameScreen load, string good, string badSave, string badReplay, string[] damaged)
    {
        int row = load.RowOf(badSave);
        Check(row > 0 && load.Select(row) && !Press(main, load.LoadButton) && SamePath(load.RowPath(load.SelectedIndex)!, badSave),
            $"BUG-1000: refused Load of {Path.GetFileName(badSave)} (row {row}): row {load.SelectedIndex} picked");
        load.DeleteButton.EmitSignal(BaseButton.SignalName.Pressed);
        Check(!File.Exists(badSave) && File.Exists(good) && damaged.All(d => d == badSave || File.Exists(d)) && load.RowOf(good) == 0,
            $"BUG-1000: Delete after the refused Load removed something else (bad save left {File.Exists(badSave)}, good left {File.Exists(good)})");
        Check(!main.LoadSavedGame(badSave) && load.SelectedIndex == 0 && load.Note.Text == _ui.Save(SaveText.ReadFailed),
            $"BUG-1000: refused Load of the deleted file: row {load.SelectedIndex} picked (want 0), note '{load.Note.Text}'");

        int replayRow = load.RowOf(badReplay);
        if (!Check(replayRow > 0 && load.KindOf(replayRow) == LoadGameScreen.RowKind.Replay && load.Select(replayRow) && !load.WatchButton.Disabled,
            $"BUG-1000: the damaged replay is row {replayRow} ({load.KindOf(replayRow)}), Watch disabled {load.WatchButton.Disabled}")) return;
        Check(!Press(main, load.WatchButton) && load.Note.Text == _ui.LoadGame(LoadText.Refused) && SamePath(load.RowPath(load.SelectedIndex)!, badReplay),
            $"BUG-1000: refused Watch of row {replayRow}: row {load.SelectedIndex} picked, note '{load.Note.Text}'");
        int replays = Directory.GetFiles(_replaysFull).Length;
        load.DeleteButton.EmitSignal(BaseButton.SignalName.Pressed);
        Check(!File.Exists(badReplay) && Directory.GetFiles(_replaysFull).Length == replays - 1 && File.Exists(good),
            "BUG-1000: Delete after the refused Watch removed something else");
        Check(!main.WatchReplay(badReplay) && load.SelectedIndex == 0, $"BUG-1000: refused Watch of the deleted replay: row {load.SelectedIndex} picked (want 0)");
        // A refused path that names no file at all (--load "" reaches ShowLoadNote too): row 0 and the note, no exception.
        Check(load.RowOf("") == -1 && !main.LoadSavedGame("") && load.SelectedIndex == 0 && load.Note.Text == _ui.Save(SaveText.ReadFailed),
            $"BUG-1000: refused Load of an empty path: row {load.SelectedIndex}, note '{load.Note.Text}'");
        await Frames(1);
    }

    // A copy with one digit in the middle changed: a replay's header still reads, its checksum no longer matches.
    private static byte[] BumpDigit(byte[] bytes)
    {
        byte[] copy = bytes.ToArray();
        for (int i = copy.Length / 2; i < copy.Length; i++)
        {
            if (copy[i] < (byte)'0' || copy[i] > (byte)'9') continue;
            copy[i] = copy[i] == (byte)'9' ? (byte)'8' : (byte)(copy[i] + 1);
            break;
        }
        return copy;
    }

    // (3): --load <path> boots into the match; a missing file shows save.read_failed on Load Game.
    private async Task LoadFlag(string good)
    {
        Main boot = NewMain(new[] { Main.LoadFlag, good, "--mute", "--speed", "1.5" }, "flag");
        AddChild(boot);
        await Frames(2);
        Check(boot.CurrentMatch is { IsLoaded: true } m && SamePath(m.SavePath!, good) && m.Simulation!.TickNumber >= SaveTick && m.Simulation.TickNumber <= SaveTick + 10
            && boot.Menu is { Visible: false }, $"--load {good}: no loaded match at boot (tick {boot.CurrentMatch?.Simulation?.TickNumber})");
        // M6-V15 (BUG-1001): the dev flag's --speed counts for --load (docs/03); Load Game's Load resumes at 1x (GeneratorAndSpeed).
        double flagSpeed = boot.CurrentMatch?.GetNode<SimRunner>("SimRunner").GameSpeed ?? 0;
        Check(flagSpeed == 1.5 && boot.CurrentMatch!.StartLine.Contains("speed 1.5x", StringComparison.Ordinal),
            $"--load with --speed 1.5: speed {flagSpeed}, line '{boot.CurrentMatch?.StartLine}'");
        RemoveChild(boot);
        boot.QueueFree();
        await Frames(2);
        boot = NewMain(new[] { Main.LoadFlag, $"{_saves}/missing.sav" }, "flag-missing");
        AddChild(boot);
        await Frames(2);
        Check(boot.CurrentMatch == null && boot.LoadScreen is { Visible: true } && boot.LoadScreen.Note.Text == _ui.Save(SaveText.ReadFailed),
            $"--load of a missing file: match {boot.CurrentMatch != null}, note '{boot.LoadScreen?.Note.Text}'");
        RemoveChild(boot);
        boot.QueueFree();
        await Frames(2);
    }

    // (4): a hosted shell with no save folder offers no Save and writes nothing.
    private async Task HostedWithoutFolder()
    {
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.UserArgs = new[] { "--skirmish", "--ai", "2", "human", "--mute", Main.NoConfineFlag };
        main.SettingsPath = $"{_root}/nofolder/settings.cfg";
        main.ReplayDir = null;
        AddChild(main);
        await Frames(2);
        main.PauseMatch();
        Check(main.SaveDir == null && main.Paused && !main.Pause!.SaveButton.Visible && !main.SaveGame(), "a hosted shell without a save folder shows Save or saves");
        main.Resume();
        RemoveChild(main);
        main.QueueFree();
        await Frames(2);
    }

    /// <summary>The tick the AI-seat match saves at (brief M6-V15: ~600).</summary>
    public const int AiSaveTick = 600;

    /// <summary>The tick the generator-options match saves at, and the ticks it then runs beside its twin.</summary>
    public const int GenSaveTick = 300, GenMoreTicks = 500;

    // M6-V15 criterion 1: the default --skirmish (slot 2 a Normal AI) saved at tick 600 from the pause menu ("Saved at 0:30",
    // no refusal), quit, loaded from Load Game, then 1,000 ticks hash-equal every tick to an unsaved twin whose AI plays
    // itself, the AI's opening equal; the loaded pause menu's note and the stats screen's "opened with" line.
    private async Task AiSeat()
    {
        Main main = NewMain(new[] { "--skirmish", "--seed", "11", "--mute" }, "ai", "ai");
        AddChild(main);
        await Frames(2);
        try
        {
            Match? match = main.CurrentMatch;
            if (!Check(match != null && !match.IsLoaded && !match.IsPlayback, "AI row: --skirmish started no live match")) return;
            SimRunner runner = match!.GetNode<SimRunner>("SimRunner");
            Simulation sim = runner.Simulation!;
            if (!Check(sim.World.Config.HasAiSeat && runner.Seats.Length == 2 && runner.Seats[1].Controller == SeatController.Ai
                && runner.Seats[1].Difficulty == AiDifficulty.Normal && runner.Recorder != null,
                $"AI row: seats [{string.Join(", ", runner.Seats)}], recorder {runner.Recorder != null}")) return;
            await RunTo(sim, AiSaveTick);
            ulong hash = sim.StateHash();
            int opening = sim.AiOpening(1);
            Check(sim.TickNumber == AiSaveTick && opening >= 0, $"AI row: stopped at tick {sim.TickNumber} (want {AiSaveTick}), opening {opening}");
            Command trainLater = TrainWorker(match);

            main.PauseMatch();
            PauseMenu pause = main.Pause!;
            pause.SaveButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(1);
            string? path = main.LastSavePath;
            if (!Check(path != null && File.Exists(path) && pause.SaveStatus.Text == _ui.SavedLine(AiSaveTick) && pause.SaveStatus.Text.Contains("0:30"),
                $"AI row: Save wrote {path}, status '{pause.SaveStatus.Text}' (want '{_ui.SavedLine(AiSaveTick)}')")) return;
            Check(sim.TickNumber == AiSaveTick && sim.StateHash() == hash, "AI row: the save moved the sim");
            ReplayError read = ReplayFormat.TryReadFile(SaveStore.ReplayPathOf(path!), out Replay? recording);
            if (!Check(read == ReplayError.None && recording != null && recording.TickCount == AiSaveTick, $"AI row: the recording beside the save: {read}")) return;
            pause.QuitToMenuButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);

            // The unsaved twin: the recording's header with the AI seat playing itself, fed only the view's own commands.
            Simulation twin = TwinOf(recording!, aiPlays: true);
            Check(twin.TickNumber == AiSaveTick && twin.StateHash() == hash && twin.AiOpening(1) == opening,
                $"AI row: the twin at tick {twin.TickNumber}: hash equal {twin.StateHash() == hash}, opening {twin.AiOpening(1)} (want {opening})");

            main.Menu!.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(1);
            LoadGameScreen load = main.LoadScreen!;
            int row = load.RowOf(path!);
            if (!Check(row >= 0 && load.KindOf(row) == LoadGameScreen.RowKind.Save && load.Select(row), $"AI row: the save is row {row}")) return;
            load.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
            Match? resumed = main.CurrentMatch;
            if (!Check(resumed is { IsLoaded: true }, "AI row: Load started no loaded match")) return;
            SimRunner runner2 = resumed!.GetNode<SimRunner>("SimRunner");
            Simulation sim2 = runner2.Simulation!;
            Check(sim2.TickNumber == AiSaveTick && sim2.StateHash() == hash && sim2.AiOpening(1) == opening && runner2.Seats.Length == 2
                && runner2.Seats[1].Controller == SeatController.Ai && runner2.Seats[1].Difficulty == AiDifficulty.Normal,
                $"AI row: loaded at tick {sim2.TickNumber}, hash equal {sim2.StateHash() == hash}, opening {sim2.AiOpening(1)}, seats [{string.Join(", ", runner2.Seats)}]");
            int compared = 0, firstBad = -1, openingBad = -1;
            runner2.Ticked += s =>
            {
                twin.Tick();
                compared++;
                if (firstBad < 0 && (twin.TickNumber != s.TickNumber || twin.StateHash() != s.StateHash())) firstBad = s.TickNumber;
                if (openingBad < 0 && (s.AiOpening(1) != opening || twin.AiOpening(1) != opening)) openingBad = s.TickNumber;
            };
            bool trained = false;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (sim2.TickNumber < AiSaveTick + MoreTicks && clock.Elapsed.TotalSeconds < 180)
            {
                await Frames(1);
                if (!trained && sim2.TickNumber >= 700)
                {
                    trained = true;
                    sim2.Enqueue(trainLater);
                    twin.Enqueue(trainLater);
                }
            }
            Check(sim2.TickNumber == AiSaveTick + MoreTicks && compared == MoreTicks && firstBad < 0 && openingBad < 0 && trained,
                $"AI row: loaded match at tick {sim2.TickNumber}, {compared} ticks compared, first differing tick {firstBad}, opening differs at {openingBad}");

            main.PauseMatch();
            Check(pause.RecordNote.Visible && pause.RecordNote.Text == _ui.Save(SaveText.NotRecorded), "AI row: the loaded pause menu has no not-recorded note");
            main.ShowStats();
            MatchStats stats = main.Stats!;
            int f = sim2.World.FactionOf(1);
            string want = opening >= 0 && (uint)f < (uint)_data.Ai.Length && opening < _data.Ai[f].Openings.Length
                ? _ui.OpeningLine(_data.Factions[f].DisplayName, _data.Ai[f].Openings[opening].DisplayName) : "?";
            Check(stats.Visible && stats.OpeningText(1) == want && stats.OpeningText(0) == "" && stats.OpeningLines.Count == 1,
                $"AI row: the loaded stats screen's opening line '{stats.OpeningText(1)}' (want '{want}'), {stats.OpeningLines.Count} lines");
            await Shot("ai-stats");
            main.CloseStats();
            pause.QuitToMenuButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);
            Check(main.CurrentMatch == null, "AI row: Quit to menu after the load");
        }
        finally
        {
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
    }

    // M6-V15 criteria 4 and 5: a --speed 1.5 match on 6 forests and 4 gold mines has those counts; saved, quit and loaded
    // from Load Game it resumes at 1x (the save holds no speed, BUG-1001) on the same map, hash-equal to an unsaved twin for
    // 500 ticks; the Quit's replay of it watches.
    private async Task GeneratorAndSpeed()
    {
        Main main = NewMain(new[] { "--skirmish", "--ai", "2", "human", "--seed", "5", "--speed", "1.5", "--forests", "6", "--mines", "4", "--mute" }, "gen", "gen");
        AddChild(main);
        await Frames(2);
        string replaysFull = Path.GetFullPath(ProjectSettings.GlobalizePath($"{_root}/gen/replays"));
        try
        {
            Match? match = main.CurrentMatch;
            if (!Check(match != null && !match.IsLoaded, "generator row: no live match")) return;
            SimRunner runner = match!.GetNode<SimRunner>("SimRunner");
            Simulation sim = runner.Simulation!;
            var placed = sim.World.ResourcePlacement;
            Check(runner.GameSpeed == 1.5, $"generator row: --speed 1.5 runs at {runner.GameSpeed}");
            Check(sim.World.Config.Map.Forests == 6 && sim.World.Config.Map.GoldMines == 4 && placed.Forests == 6 && placed.Mines == 4 && GoldNodes(sim.World) == 4,
                $"generator row: map asks {sim.World.Config.Map.Forests} forests / {sim.World.Config.Map.GoldMines} mines, placed {placed.Forests} / {placed.Mines}, {GoldNodes(sim.World)} gold nodes");
            await RunTo(sim, GenSaveTick);
            ulong hash = sim.StateHash();
            Command trainLater = TrainWorker(match);
            main.PauseMatch();
            main.Pause!.SaveButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(1);
            string? path = main.LastSavePath;
            ReplayError read = ReplayFormat.TryReadFile(path == null ? "" : SaveStore.ReplayPathOf(path), out Replay? recording);
            if (!Check(path != null && File.Exists(path) && sim.TickNumber == GenSaveTick && read == ReplayError.None && recording != null && recording.Map.Forests == 6
                && recording.Map.GoldMines == 4, $"generator row: Save at tick {sim.TickNumber} wrote {path}, recording {read}")) return;
            main.Pause.QuitToMenuButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);
            string[] quitReplays = Directory.Exists(replaysFull) ? Directory.GetFiles(replaysFull) : Array.Empty<string>();
            Simulation twin = TwinOf(recording!);
            Check(twin.StateHash() == hash, "generator row: the twin's hash at the save differs");

            main.Menu!.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(1);
            LoadGameScreen load = main.LoadScreen!;
            if (!Check(load.Select(load.RowOf(path!)), "generator row: the save is not listed")) return;
            load.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
            Match? resumed = main.CurrentMatch;
            if (!Check(resumed is { IsLoaded: true }, "generator row: Load started no loaded match")) return;
            SimRunner runner2 = resumed!.GetNode<SimRunner>("SimRunner");
            Simulation sim2 = runner2.Simulation!;
            Check(runner2.GameSpeed == 1.0 && resumed.StartLine.StartsWith("Save loaded: ", StringComparison.Ordinal) && resumed.StartLine.Contains(", speed 1x,", StringComparison.Ordinal),
                $"BUG-1001: a match saved at 1.5x loads at {runner2.GameSpeed}x, line '{resumed.StartLine}'");
            Check(sim2.World.Config.Map.Forests == 6 && sim2.World.Config.Map.GoldMines == 4 && GoldNodes(sim2.World) == 4 && sim2.StateHash() == hash,
                $"generator row: loaded map {sim2.World.Config.Map.Forests} forests / {sim2.World.Config.Map.GoldMines} mines, {GoldNodes(sim2.World)} gold nodes, hash equal {sim2.StateHash() == hash}");
            int compared = 0, firstBad = -1;
            runner2.Ticked += s =>
            {
                twin.Tick();
                compared++;
                if (firstBad < 0 && (twin.TickNumber != s.TickNumber || twin.StateHash() != s.StateHash())) firstBad = s.TickNumber;
            };
            bool trained = false;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (sim2.TickNumber < GenSaveTick + GenMoreTicks && clock.Elapsed.TotalSeconds < 120)
            {
                await Frames(1);
                if (!trained && sim2.TickNumber >= GenSaveTick + 100)
                {
                    trained = true;
                    sim2.Enqueue(trainLater);
                    twin.Enqueue(trainLater);
                }
            }
            Check(compared == GenMoreTicks && firstBad < 0 && trained, $"generator row: {compared} ticks compared, first differing tick {firstBad}");
            main.PauseMatch();
            main.Pause.QuitToMenuButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);

            // The Quit's replay (written before the load) plays the same map.
            main.Menu.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(1);
            if (!Check(quitReplays.Length == 1 && load.Select(load.RowOf(quitReplays[0])) && !load.WatchButton.Disabled,
                $"generator row: {quitReplays.Length} replays after Quit, Watch disabled {load.WatchButton.Disabled}")) return;
            load.WatchButton.EmitSignal(BaseButton.SignalName.Pressed);
            Match? watched = main.CurrentMatch;
            if (!Check(watched is { IsPlayback: true }, "generator row: Watch started no playback")) return;
            SimRunner runner3 = watched!.GetNode<SimRunner>("SimRunner");
            clock.Restart();
            while (runner3.Simulation!.TickNumber < 200 && runner3.PlaybackState == PlaybackState.Playing && clock.Elapsed.TotalSeconds < 60) await Frames(1);
            Simulation sim3 = runner3.Simulation!;
            Check(sim3.TickNumber >= 200 && runner3.PlaybackState != PlaybackState.Stopped && sim3.World.Config.Map.Forests == 6 && sim3.World.ResourcePlacement.Forests == 6
                && sim3.World.ResourcePlacement.Mines == 4, $"generator row: the replay at tick {sim3.TickNumber}, {runner3.PlaybackState} ({runner3.PlaybackError}), placed {sim3.World.ResourcePlacement.Forests} / {sim3.World.ResourcePlacement.Mines}");
        }
        finally
        {
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
    }

    private async Task RunTo(Simulation sim, int tick)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (sim.TickNumber < tick && clock.Elapsed.TotalSeconds < 120) await Frames(1);
    }

    // Live gold mine nodes on the map.
    private static int GoldNodes(World w)
    {
        int n = 0;
        var r = w.Resources;
        for (int i = 0; i < r.Capacity; i++)
            if (r.Alive[i] && w.Data.Resources[r.TypeId[i]].Resource == ResourceKind.Gold) n++;
        return n;
    }

    // Presses a button of `main`'s screens; true when a match runs after it.
    private static bool Press(Main main, Button b)
    {
        b.EmitSignal(BaseButton.SignalName.Pressed);
        return main.CurrentMatch != null;
    }

    // A sim built from the recording's header, fed its commands to its last tick, as SimRunner's playback does. With
    // `aiPlays` (M6-V15) an AI seat stays an AI and plays itself: of its player's logged commands only the view's start
    // spawns are fed (the rest are the AI's own, which it makes again).
    private Simulation TwinOf(Replay r, bool aiPlays = false)
    {
        var seats = r.Seats.Select(s => new PlayerSeat(_data.FindFaction(s.Faction), aiPlays ? s.Controller : SeatController.Human, s.Difficulty)).ToImmutableArray();
        var sim = new Simulation(new SimConfig(r.Seed, r.PlayerCount, r.UnitCapacity, r.CommandCapacity)
        {
            Data = _data, Map = r.Map, ResourceCapacity = r.ResourceCapacity, Combat = r.Combat, Seats = seats,
        });
        int next = 0;
        while (sim.TickNumber < r.TickCount)
        {
            while (next < r.Commands.Length && r.Commands[next].Tick <= sim.TickNumber + 1)
            {
                Command c = r.Commands[next++];
                if (aiPlays && seats[c.Player].Controller == SeatController.Ai && c.Kind is not (CommandKind.SpawnUnit or CommandKind.SpawnBuilding)) continue;
                sim.Enqueue(c);
            }
            sim.Tick();
        }
        return sim;
    }

    private static void MoveOrder(SelectionController sel, Simulation sim, float dx)
    {
        EntityHandle w = OwnUnit(sim);
        sel.SelectOnly(w);
        System.Numerics.Vector2 at = sim.World.Units.Position[w.Index];
        sel.Order(CommandKind.Move, new Vector2(at.X + dx, at.Y + 4f), queued: false);
    }

    // A worker trained at player 0's start Town Hall.
    private static Command TrainWorker(Match match)
    {
        StartBasePlan bases = match.Bases ?? throw new InvalidOperationException("no start bases");
        World w = match.Simulation!.World;
        System.Numerics.Vector2 hall = StartBase.FootprintCenter(w.NavGrid, w.Data.Buildings[bases.HallType[0]], bases.HallAnchor[0]);
        return Command.Train(SelectionController.LocalPlayer, hall, bases.WorkerType[0]);
    }

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

    private static byte[] Flip(byte[] bytes, int at)
    {
        byte[] copy = bytes.ToArray();
        copy[at] ^= 0xFF;
        return copy;
    }

    // An int32 written at `at`; with `reseal` the trailing FNV-1a 64 checksum is recomputed, so the change gets past it.
    private static byte[] Patch(byte[] bytes, int at, int value, bool reseal)
    {
        byte[] copy = bytes.ToArray();
        BitConverter.GetBytes(value).CopyTo(copy, at);
        if (reseal) Reseal(copy);
        return copy;
    }

    // The total-length field (int64 at 12) set to `value`, checksum resealed.
    private static byte[] PatchLong(byte[] bytes, int at, long value)
    {
        byte[] copy = bytes.ToArray();
        BitConverter.GetBytes(value).CopyTo(copy, at);
        Reseal(copy);
        return copy;
    }

    private static void Reseal(byte[] copy)
    {
        const ulong offsetBasis = 14695981039346656037UL, prime = 1099511628211UL;
        ulong h = offsetBasis;
        for (int i = 0; i < copy.Length - 8; i++) h = unchecked((h ^ copy[i]) * prime);
        BitConverter.GetBytes(h).CopyTo(copy, copy.Length - 8);
    }

    // With `folder` the shell saves and records under <root>/<folder>/ (M6-V15's rows keep out of the first rows' list).
    private Main NewMain(string[] args, string label, string? folder = null)
    {
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.UserArgs = args.Append(Main.NoConfineFlag).ToArray();
        main.SettingsPath = $"{_root}/{label}/settings.cfg"; // BUG-0770: never the player's file
        main.ReplayDir = folder == null ? _replays : $"{_root}/{folder}/replays";
        main.SaveDir = folder == null ? _saves : $"{_root}/{folder}/saves";
        main.BeforeStart = m => m.GetNode<SimRunner>("SimRunner").FixedFrameDelta = FrameSeconds;
        return main;
    }

    // A folder's files' names, sizes and write times ("" when there is none).
    private static string Folder(string dir)
    {
        string full = ProjectSettings.GlobalizePath(dir);
        if (!Directory.Exists(full)) return "";
        return string.Join("|", Directory.GetFiles(full).OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => $"{Path.GetFileName(f)}:{new FileInfo(f).Length}:{File.GetLastWriteTimeUtc(f).Ticks}"));
    }

    private static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    // Windowed with `-- --shots <dir>`: the viewport as save-<name>.png.
    private async Task Shot(string name)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = $"{_shots}/save-{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"  screenshot {path}");
    }

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
