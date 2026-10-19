using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Rts.Sim.Save;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// QA M6-V15 (session 2026-10-18-1814): the AI-seat save / load through the shell, the refused row, the setup's speed list and
/// the generator's counts, beyond <c>SaveLoadTest</c> / <c>ShellTest</c>.
/// (1) AI seat: the default <c>--skirmish</c> (seed 11, slot 2 Normal AI) saved from the pause menu at tick 0 (before the first
/// think's tick runs), 1, 20 (a think tick), 21 and mid-wave (the AI's first wave under way, found through its blackboard);
/// the oracle is the original sim ticked on by hand from the save; the save loaded from Load Game runs hash-equal every tick
/// for 300 ticks at 1x with the same opening. The tick-1 save is loaded twice in a row (both runs equal), and the loaded match
/// saved again at +100 and loaded (equal to the loaded match ticked on by hand).
/// (2) BUG-1000: refused Load of a damaged (header-valid) save with 1 row, 2 rows and 200 rows (the damaged file at the bottom,
/// and a second damaged file at row 100); refused Watch of a damaged replay then refused Load of a damaged save; the picked row
/// follows the latest refusal and Delete removes exactly it; the bottom row is scrolled into view.
/// (3) Setup: values typed into the spin boxes (no Enter) reach the match through a real mouse click on Start; Enter, junk,
/// below-floor and above-cap typing; the speed pick is kept after Back and after a Load Game load, which still runs at 1x;
/// <c>--speed 1.5 --load</c> in either order runs at 1.5.
/// (4) Generator: map sizes 128 / 192 / 256 x 2 / 3 / 4 slots at the floors (and one under, clamped) and at 64 / 64 (and 65):
/// the match starts, the counts are asked, and every start location has two gold mines and a forest of its own.
/// (5) Layout at 1280 x 720 with four slots: every setup control on screen and no two overlapping (windowed with
/// <c>-- --shots &lt;dir&gt;</c>: <c>qa-v15-setup-4slots.png</c>).
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6V15Test.tscn</c>; prints "QA M6V15 TEST PASS" or "QA M6V15 TEST FAIL: ..." lines.</remarks>
public partial class QaM6V15Test : Node
{
    private const int Compare = 300;
    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private UiText _ui = null!;
    private string _root = "", _rootFull = "";
    private string? _shots;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            GetTree().Root.Size = new Vector2I(1280, 720);
            string[] args = OS.GetCmdlineUserArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--shots") _shots = args[i + 1];
            await Frames(1);
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            _ui = UiText.Shared ?? throw new InvalidOperationException("ui.json failed to load");
            _root = $"user://qa-m6v15-{OS.GetProcessId()}";
            _rootFull = Full(_root);
            if (Directory.Exists(_rootFull)) Directory.Delete(_rootFull, true);
            string playerSaves = Folder(SaveStore.DefaultDir), playerReplays = Folder(ReplayStore.DefaultDir);

            string? only = Filter();
            string? good = null;
            if (only == null || only == "ai")
            {
                foreach (int t in new[] { 0, 1, 20, 21 }) good = await AiSave(t, false, t == 1) ?? good;
                await AiSave(-1, true, false);
            }
            if (only == null || only == "rows")
            {
                good ??= await AiSave(1, false, false);
                if (good != null) await RefusedRows(good);
            }
            if (only == null || only == "setup")
            {
                await TypedCounts();
                if (good != null || (good = await AiSave(1, false, false)) != null) await SpeedKept(good);
            }
            if (only == null || only == "gen") await GeneratorMatrix();
            if (only == null || only == "layout") await Layout();

            Check(Folder(SaveStore.DefaultDir) == playerSaves && Folder(ReplayStore.DefaultDir) == playerReplays, "the player's folders changed");
            try { Directory.Delete(_rootFull, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        foreach (string f in _failures.Take(80)) GD.Print($"QA M6V15 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M6V15 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private static string? Filter()
    {
        string[] a = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < a.Length; i++) if (a[i] == "--only") return a[i + 1];
        return null;
    }

    // ---- (1) AI seat ----

    // Saves the AI match at `tick` (or mid-wave), loads it from Load Game and compares 300 ticks with the original ticked by
    // hand. With `twice`: loads it a second time, then saves the loaded match at +100 and loads that. Returns the save path.
    private async Task<string?> AiSave(int tick, bool midWave, bool twice)
    {
        string label = midWave ? "wave" : $"t{tick}";
        string saves = $"{_root}/ai-{label}/saves", replays = $"{_root}/ai-{label}/replays";
        Main main = NewMain(new[] { "--skirmish", "--seed", "11", "--mute" }, $"ai-{label}", saves, replays);
        AddChild(main);
        await Frames(2);
        try
        {
            Match? match = main.CurrentMatch;
            if (!Check(match != null, $"AI {label}: no match")) return null;
            SimRunner runner = match!.GetNode<SimRunner>("SimRunner");
            Simulation sim = runner.Simulation!;
            if (!Check(sim.World.Config.HasAiSeat && runner.Seats[1].Controller == SeatController.Ai, $"AI {label}: no AI seat")) return null;
            if (midWave)
            {
                var clock = Stopwatch.StartNew();
                runner.FixedFrameDelta = 0.25;
                while (!WaveActive(sim, 1) && sim.TickNumber < 30000 && !sim.MatchOver && clock.Elapsed.TotalSeconds < 240) await Frames(1);
                runner.FixedFrameDelta = 0;
                if (!Check(WaveActive(sim, 1), $"AI wave: no wave by tick {sim.TickNumber} ({clock.Elapsed.TotalSeconds:0} s)")) return null;
                int started = sim.TickNumber;
                await RunTo(runner, sim.TickNumber + 60);
                GD.Print($"QA NOTE: AI wave started by tick {started}; saved at tick {sim.TickNumber}, wave active {WaveActive(sim, 1)}");
                tick = sim.TickNumber;
            }
            else await RunTo(runner, tick);
            if (!Check(sim.TickNumber == tick, $"AI {label}: at tick {sim.TickNumber}, want {tick}")) return null;
            int opening = sim.AiOpening(1);
            ulong hash = sim.StateHash();

            main.PauseMatch();
            main.Pause!.SaveButton.EmitSignal(BaseButton.SignalName.Pressed);
            string? path = main.LastSavePath;
            if (!Check(path != null && File.Exists(path) && main.Pause.SaveStatus.Text == _ui.SavedLine(tick), $"AI {label}: save {path}, status '{main.Pause.SaveStatus.Text}'")) return null;
            Check(sim.StateHash() == hash && sim.TickNumber == tick, $"AI {label}: the save moved the sim");
            ulong[] want = TickByHand(sim, Compare);
            main.Pause.QuitToMenuButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);

            Simulation? sim2 = await LoadAndRun(main, path!, tick, hash, opening, want, $"AI {label}");
            if (twice && sim2 != null)
            {
                main.PauseMatch();
                main.Pause.QuitToMenuButton.EmitSignal(BaseButton.SignalName.Pressed);
                await Frames(2);
                // The same file loaded a second time in a row.
                Simulation? again = await LoadAndRun(main, path!, tick, hash, opening, want.Take(100).ToArray(), $"AI {label} (second load)");
                if (again != null)
                {
                    // Saved again from the loaded match at +100, then loaded.
                    int t2 = again.TickNumber;
                    ulong h2 = again.StateHash();
                    main.PauseMatch();
                    main.Pause.SaveButton.EmitSignal(BaseButton.SignalName.Pressed);
                    string? path2 = main.LastSavePath;
                    if (Check(path2 != null && path2 != path && File.Exists(path2) && !File.Exists(SaveStore.ReplayPathOf(path2)),
                        $"AI {label}: the loaded match's save {path2} (a recording beside it {path2 != null && File.Exists(SaveStore.ReplayPathOf(path2))})"))
                    {
                        ulong[] want2 = TickByHand(again, Compare);
                        main.Pause.QuitToMenuButton.EmitSignal(BaseButton.SignalName.Pressed);
                        await Frames(2);
                        await LoadAndRun(main, path2!, t2, h2, opening, want2, $"AI {label} (save of a load at {t2})");
                    }
                }
            }
            if (main.CurrentMatch != null) { main.QuitToMenu(); await Frames(2); }
            return path;
        }
        finally
        {
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
    }

    // Load Game -> pick `path` -> Load; then the loaded match's ticks against `want` (hash after each tick from `tick`).
    private async Task<Simulation?> LoadAndRun(Main main, string path, int tick, ulong hash, int opening, ulong[] want, string what)
    {
        main.Menu!.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(1);
        LoadGameScreen load = main.LoadScreen!;
        int row = load.RowOf(path);
        if (!Check(row >= 0 && load.Select(row), $"{what}: the save is not listed (row {row})")) return null;
        load.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
        Match? m = main.CurrentMatch;
        if (!Check(m is { IsLoaded: true }, $"{what}: Load started no loaded match (note '{load.Note.Text}')")) return null;
        SimRunner runner = m!.GetNode<SimRunner>("SimRunner");
        Simulation sim = runner.Simulation!;
        Check(sim.TickNumber == tick && sim.StateHash() == hash && sim.AiOpening(1) == opening && runner.GameSpeed == 1.0,
            $"{what}: loaded at tick {sim.TickNumber} (want {tick}), hash equal {sim.StateHash() == hash}, opening {sim.AiOpening(1)} (want {opening}), speed {runner.GameSpeed}");
        int compared = 0, firstBad = -1;
        Action<Simulation> onTick = s =>
        {
            int k = s.TickNumber - tick - 1;
            if (k < 0 || k >= want.Length) return;
            compared++;
            if (firstBad < 0 && s.StateHash() != want[k]) firstBad = s.TickNumber;
        };
        runner.Ticked += onTick;
        await RunTo(runner, tick + want.Length);
        runner.Ticked -= onTick;
        Check(compared == want.Length && firstBad < 0 && sim.AiOpening(1) == opening,
            $"{what}: {compared}/{want.Length} ticks compared, first differing tick {firstBad}, opening {sim.AiOpening(1)}");
        return sim;
    }

    private static ulong[] TickByHand(Simulation sim, int n)
    {
        var h = new ulong[n];
        for (int i = 0; i < n; i++)
        {
            sim.Tick();
            h[i] = sim.StateHash();
        }
        return h;
    }

    // The AI's blackboard says a wave is out (internal state, read by reflection: test only).
    private static bool WaveActive(Simulation sim, int player)
    {
        object? ai = typeof(Simulation).GetMethod("AiOf", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(sim, new object[] { player });
        if (ai == null) return false;
        object board = ai.GetType().GetProperty("Board", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(ai)!;
        return (bool)board.GetType().GetField("WaveActive", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(board)!;
    }

    // Runs the runner (one or five ticks a frame) to exactly `tick`, then holds it.
    private async Task RunTo(SimRunner runner, int tick)
    {
        Simulation sim = runner.Simulation!;
        var clock = Stopwatch.StartNew();
        while (sim.TickNumber < tick && clock.Elapsed.TotalSeconds < 240)
        {
            runner.FixedFrameDelta = tick - sim.TickNumber >= 5 ? 0.25 : 0.05;
            await Frames(1);
        }
        runner.FixedFrameDelta = 0;
    }

    // ---- (2) BUG-1000 ----

    private async Task RefusedRows(string good)
    {
        byte[] bytes = File.ReadAllBytes(good);
        byte[] bad = bytes.ToArray();
        bad[bad.Length / 2] ^= 0x5A; // header intact: listed, refused at Load
        var day = new DateTime(2023, 3, 3, 0, 0, 0);

        // One row: only the damaged save.
        {
            string dir = Full($"{_root}/rows1/saves");
            Directory.CreateDirectory(dir);
            string p = Path.Combine(dir, SaveStore.FileName(day, 11));
            File.WriteAllBytes(p, bad);
            await WithMain("rows1", async (main, load) =>
            {
                Check(load.RowCount == 1 && load.SelectedIndex == 0, $"1 row: {load.RowCount} rows, picked {load.SelectedIndex}");
                bool started = Press(main, load.LoadButton);
                Check(!started && load.SelectedIndex == 0 && load.Note.Text.Length > 0,
                    $"1 row: refused Load: started {started}, picked {load.SelectedIndex}, note '{load.Note.Text}'");
                load.DeleteButton.EmitSignal(BaseButton.SignalName.Pressed);
                Check(!File.Exists(p) && load.RowCount == 0, $"1 row: Delete after refusal: file left {File.Exists(p)}, {load.RowCount} rows");
                await Frames(1);
            });
        }

        // Two rows: good (newer) + damaged (older, row 1).
        {
            string dir = Full($"{_root}/rows2/saves");
            Directory.CreateDirectory(dir);
            string g = Path.Combine(dir, SaveStore.FileName(day.AddDays(1), 11)), p = Path.Combine(dir, SaveStore.FileName(day, 11));
            File.WriteAllBytes(g, bytes);
            File.WriteAllBytes(p, bad);
            await WithMain("rows2", async (main, load) =>
            {
                Check(load.RowOf(p) == 1 && load.Select(1) && !Press(main, load.LoadButton) && load.SelectedIndex == 1 && SamePath(load.RowPath(1)!, p),
                    $"2 rows: refused Load of row 1 left row {load.SelectedIndex} picked");
                load.DeleteButton.EmitSignal(BaseButton.SignalName.Pressed);
                Check(!File.Exists(p) && File.Exists(g) && load.RowCount == 1 && load.SelectedIndex == 0, $"2 rows: Delete: bad left {File.Exists(p)}, good left {File.Exists(g)}");
                // The good one still loads.
                Check(Press(main, load.LoadButton) && main.CurrentMatch is { IsLoaded: true }, "2 rows: the good save no longer loads");
                if (main.CurrentMatch != null) { main.QuitToMenu(); await Frames(2); }
                await Task.CompletedTask;
            });
        }

        // 200 rows: one good save, 196 good replays (small), a damaged save at row 100, a damaged replay, a damaged save at the bottom.
        {
            string sdir = Full($"{_root}/rows200/saves"), rdir = Full($"{_root}/rows200/replays");
            Directory.CreateDirectory(sdir);
            Directory.CreateDirectory(rdir);
            string replaySrc = SaveStore.ReplayPathOf(good);
            byte[] rep = File.Exists(replaySrc) ? File.ReadAllBytes(replaySrc) : Array.Empty<byte>();
            if (!Check(rep.Length > 0, "200 rows: no recording beside the good save to copy")) return;
            // Newest first: row 0 the good save at day+300 min; replays fill; dates fixed so the rows are known.
            string goodSave = Path.Combine(sdir, SaveStore.FileName(day.AddMinutes(300), 11));
            File.WriteAllBytes(goodSave, bytes);
            string mid = Path.Combine(sdir, SaveStore.FileName(day.AddMinutes(200), 11));
            File.WriteAllBytes(mid, bad);
            string bottom = Path.Combine(sdir, SaveStore.FileName(day, 11));
            File.WriteAllBytes(bottom, bad);
            string badReplay = Path.Combine(rdir, $"{day.AddMinutes(150):yyyyMMdd-HHmmss}-seed11.replay");
            File.WriteAllBytes(badReplay, BumpDigit(rep));
            // 98 replays newer than the mid save (minutes 299..202) and 98 older than the damaged replay (98..1): the mid save is
            // row 99, the damaged replay row 100, the bottom save row 199.
            foreach (int minute in Enumerable.Range(202, 98).Concat(Enumerable.Range(1, 98)))
                File.WriteAllBytes(Path.Combine(rdir, $"{day.AddMinutes(minute):yyyyMMdd-HHmmss}-seed11.replay"), rep);
            await WithMain("rows200", async (main, load) =>
            {
                int rows = load.RowCount, rMid = load.RowOf(mid), rBottom = load.RowOf(bottom), rReplay = load.RowOf(badReplay);
                GD.Print($"QA NOTE: 200-row folder: {rows} rows, damaged saves at {rMid} and {rBottom}, damaged replay at {rReplay}, skipped {load.Skipped}");
                Check(rows == 200 && rBottom == rows - 1 && rMid > 0 && rReplay > 0, $"200 rows: {rows} rows, bottom {rBottom}, mid {rMid}, replay {rReplay}");
                // Bottom refused: kept and scrolled into view.
                Check(load.Select(rBottom) && !Press(main, load.LoadButton) && load.SelectedIndex == rBottom, $"200 rows: refused bottom row: row {load.SelectedIndex} picked (want {rBottom})");
                await Frames(2);
                Tree tree = load.List;
                TreeItem? item = tree.GetSelected();
                Rect2 area = item != null ? tree.GetItemAreaRect(item) : new Rect2();
                Vector2 scroll = tree.GetScroll();
                bool visible = item != null && area.Position.Y - scroll.Y >= -1 && area.End.Y - scroll.Y <= tree.Size.Y + 1;
                // BUG-1022 (open): the kept row is not scrolled into view. A note until it is fixed; the fix turns this into a Check.
                await Shot("refused-bottom");
                if (!visible) GD.Print($"QA NOTE: BUG-1022: after the refused bottom row the picked row is off-screen (item y {area.Position.Y}-{area.End.Y}, scroll {scroll.Y}, tree height {tree.Size.Y})");
                // Two damaged files: the mid one refused after the bottom one keeps the mid one.
                Check(load.Select(rMid) && !Press(main, load.LoadButton) && load.SelectedIndex == rMid, $"200 rows: refused mid row after the bottom: row {load.SelectedIndex} (want {rMid})");
                // Refused Watch, then refused Load: each keeps its own row.
                Check(load.Select(rReplay) && !Press(main, load.WatchButton) && load.SelectedIndex == rReplay && load.Note.Text == _ui.LoadGame(LoadText.Refused),
                    $"200 rows: refused Watch: row {load.SelectedIndex} (want {rReplay}), note '{load.Note.Text}'");
                Check(load.Select(rBottom) && !Press(main, load.LoadButton) && load.SelectedIndex == rBottom && load.Note.Text != _ui.LoadGame(LoadText.Refused),
                    $"200 rows: refused Load after refused Watch: row {load.SelectedIndex} (want {rBottom}), note '{load.Note.Text}'");
                // Delete removes exactly the bottom one; then the mid one by a refused --load-style call (LoadSavedGame).
                int before = Directory.GetFiles(sdir).Length + Directory.GetFiles(rdir).Length;
                load.DeleteButton.EmitSignal(BaseButton.SignalName.Pressed);
                Check(!File.Exists(bottom) && File.Exists(mid) && File.Exists(goodSave) && File.Exists(badReplay)
                    && Directory.GetFiles(sdir).Length + Directory.GetFiles(rdir).Length == before - 1, "200 rows: Delete after refused Load removed something else");
                Check(!main.LoadSavedGame(mid) && SamePath(load.RowPath(load.SelectedIndex)!, mid), $"200 rows: LoadSavedGame(mid) refused: row {load.SelectedIndex} picked");
                load.DeleteButton.EmitSignal(BaseButton.SignalName.Pressed);
                Check(!File.Exists(mid) && File.Exists(goodSave) && File.Exists(badReplay), "200 rows: Delete of the refused mid save removed something else");
                Check(!main.WatchReplay(badReplay) && SamePath(load.RowPath(load.SelectedIndex)!, badReplay), $"200 rows: WatchReplay(bad) refused: row {load.SelectedIndex}");
                load.DeleteButton.EmitSignal(BaseButton.SignalName.Pressed);
                Check(!File.Exists(badReplay) && File.Exists(goodSave) && load.RowCount == 197, $"200 rows: Delete of the refused replay: {load.RowCount} rows");
            });
        }
    }

    private async Task WithMain(string label, Func<Main, LoadGameScreen, Task> body)
    {
        Main main = NewMain(Array.Empty<string>(), label, $"{_root}/{label}/saves", $"{_root}/{label}/replays");
        AddChild(main);
        await Frames(2);
        try
        {
            main.Menu!.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(1);
            await body(main, main.LoadScreen!);
        }
        finally
        {
            if (main.CurrentMatch != null) main.QuitToMenu();
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
    }

    // ---- (3) Setup: typed counts, the speed pick ----

    private async Task TypedCounts()
    {
        Main main = NewMain(Array.Empty<string>(), "typed", $"{_root}/typed/saves", $"{_root}/typed/replays");
        AddChild(main);
        await Frames(2);
        try
        {
            await Click(main.Menu!.SkirmishButton);
            SkirmishSetup setup = main.Setup!;
            LineEdit forests = setup.ForestsSpin.GetLineEdit(), mines = setup.MinesSpin.GetLineEdit();
            Check(setup.StartButton.FocusMode != Control.FocusModeEnum.None, $"Start's focus mode {setup.StartButton.FocusMode}: a click would not take focus from a typed spin box");

            // Typed, no Enter, then a real click on Start.
            await Click(forests);
            forests.Text = "7";
            await Click(mines);
            mines.Text = "9";
            await Click(setup.StartButton);
            Match? m = main.CurrentMatch;
            var map = m?.Simulation?.World.Config.Map;
            Check(map != null && map.Forests == 7 && map.GoldMines == 9, $"typed 7 / 9 then a click on Start: the match asks {map?.Forests} forests / {map?.GoldMines} mines");
            if (main.CurrentMatch != null) { main.QuitToMenu(); await Frames(2); }
            // The same with press and release in one frame (a fast click): logged, not asserted.
            await Click(main.Menu.SkirmishButton);
            await Click(mines);
            mines.Text = "11";
            _pressFrames = 0;
            await Click(setup.StartButton);
            _pressFrames = 2;
            GD.Print($"QA NOTE: BUG-1023: typed 11 mines then press+release in one frame on Start: the match asks {main.CurrentMatch?.Simulation?.World.Config.Map.GoldMines} mines");
            if (main.CurrentMatch != null) { main.QuitToMenu(); await Frames(2); }
            setup.MinesSpin.Value = 9;
            await Click(main.Menu.SkirmishButton);
            Check(setup.ForestsPick == 7 && setup.MinesPick == 9, $"after the match the setup shows {setup.ForestsPick} / {setup.MinesPick}");

            // Enter; junk; below the floor; over the cap; an expression.
            foreach ((string text, int want) in new[] { ("5", 5), ("abc", 5), ("1", 4), ("-3", 4), ("1000", 64), ("65", 64), (" 12 ", 12), ("", 12) })
            {
                await Click(mines);
                mines.Text = text;
                mines.EmitSignal(LineEdit.SignalName.TextSubmitted, text);
                await Frames(1);
                string[]? args = setup.MatchArgs();
                int argMines = args == null ? -1 : LaunchOptions.Parse(args).Mines;
                if (mines.Text.Trim() != want.ToString()) GD.Print($"QA NOTE: mines typed '{text}' + Enter: the box shows '{mines.Text}', the pick is {setup.MinesPick}");
                Check(setup.MinesPick == want && argMines == want,
                    $"mines typed '{text}' + Enter: pick {setup.MinesPick}, args {argMines}, box '{mines.Text}' (want {want})");
            }
            // Typed below the floor and focus moved to Add player: the floor of 3 slots applies.
            await Click(forests);
            forests.Text = "1";
            await Click(setup.AddSlotButton);
            Check(setup.SlotCount == 3 && setup.ForestsPick == 3 && setup.MinesPick >= 6, $"typed 1 forest then Add player: {setup.SlotCount} slots, {setup.ForestsPick} forests, {setup.MinesPick} mines");
            await Click(setup.RemoveSlotButton);
        }
        finally
        {
            if (main.CurrentMatch != null) main.QuitToMenu();
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
    }

    // The speed pick: kept after Back, after a load; the load still runs at 1x. --speed with --load in either order.
    private async Task SpeedKept(string good)
    {
        string saves = $"{_root}/speed/saves";
        Directory.CreateDirectory(Full(saves));
        string copy = Path.Combine(Full(saves), Path.GetFileName(good));
        File.Copy(good, copy, true);
        Main main = NewMain(Array.Empty<string>(), "speed", saves, $"{_root}/speed/replays");
        AddChild(main);
        await Frames(2);
        try
        {
            await Click(main.Menu!.SkirmishButton);
            SkirmishSetup setup = main.Setup!;
            setup.SpeedOption.Select(2);
            await Click(setup.BackButton);
            main.Menu.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(1);
            LoadGameScreen load = main.LoadScreen!;
            Check(load.Select(load.RowOf(copy)) && Press(main, load.LoadButton), "speed: the save did not load");
            double speed = main.CurrentMatch?.GetNode<SimRunner>("SimRunner").GameSpeed ?? -1;
            Check(speed == 1.0 && main.CurrentMatch!.StartLine.Contains("speed 1x", StringComparison.Ordinal),
                $"BUG-1001: setup on Fast, a Load Game load runs at {speed} ('{main.CurrentMatch?.StartLine}')");
            main.QuitToMenu();
            await Frames(2);
            await Click(main.Menu.SkirmishButton);
            Check(setup.SpeedOption.Selected == 2 && setup.SpeedPick == 1.5, $"speed: after a load the setup shows item {setup.SpeedOption.Selected}");
        }
        finally
        {
            if (main.CurrentMatch != null) main.QuitToMenu();
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }

        foreach (string[] args in new[] { new[] { Main.LoadFlag, copy, "--speed", "1.5", "--mute" }, new[] { "--speed", "1.5", Main.LoadFlag, copy, "--mute" },
                     new[] { "--speed", "0.75", Main.LoadFlag, copy, "--mute" } })
        {
            Main boot = NewMain(args, "speedflag", saves, $"{_root}/speed/replays");
            AddChild(boot);
            await Frames(2);
            double want = double.Parse(args[Array.IndexOf(args, "--speed") + 1], System.Globalization.CultureInfo.InvariantCulture);
            double got = boot.CurrentMatch?.GetNode<SimRunner>("SimRunner").GameSpeed ?? -1;
            Check(boot.CurrentMatch is { IsLoaded: true } && got == want, $"[{string.Join(' ', args)}]: loaded {boot.CurrentMatch?.IsLoaded}, speed {got} (want {want})");
            if (boot.CurrentMatch != null) boot.QuitToMenu();
            RemoveChild(boot);
            boot.QueueFree();
            await Frames(2);
        }
    }

    // ---- (4) Generator matrix ----

    private async Task GeneratorMatrix()
    {
        Main main = NewMain(Array.Empty<string>(), "gen", $"{_root}/gen/saves", $"{_root}/gen/replays");
        AddChild(main);
        await Frames(2);
        try
        {
            await Click(main.Menu!.SkirmishButton);
            SkirmishSetup setup = main.Setup!;
            int startMines = _data.Rules.StartMineCount;
            for (int size = 0; size < LaunchOptions.MapSizes.Length; size++)
            {
                for (int slots = 2; slots <= 4; slots++)
                {
                    foreach (bool max in new[] { false, true })
                    {
                        if (!setup.Visible) await Click(main.Menu.SkirmishButton);
                        setup.MapSizeOption.Select(size);
                        while (setup.SlotCount < slots) setup.AddSlot();
                        while (setup.SlotCount > slots) setup.RemoveSlot();
                        int fFloor = slots, mFloor = slots * startMines;
                        // One past each end first: clamped.
                        setup.ForestsSpin.Value = max ? 65 : fFloor - 1;
                        setup.MinesSpin.Value = max ? 65 : mFloor - 1;
                        int wantF = max ? 64 : fFloor, wantM = max ? 64 : mFloor;
                        string what = $"{LaunchOptions.MapSizes[size]} map, {slots} slots, {(max ? "cap" : "floor")}";
                        Check(setup.ForestsPick == wantF && setup.MinesPick == wantM, $"{what}: one past clamps to {setup.ForestsPick} / {setup.MinesPick} (want {wantF} / {wantM})");
                        setup.SeedEdit.Text = (17 + size * 10 + slots).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        var clock = Stopwatch.StartNew();
                        setup.StartButton.EmitSignal(BaseButton.SignalName.Pressed);
                        await Frames(2);
                        clock.Stop();
                        Simulation? sim = main.CurrentMatch?.Simulation;
                        if (!Check(sim != null, $"{what}: Start started no match")) continue;
                        World w = sim!.World;
                        Check(w.Config.Map.Forests == wantF && w.Config.Map.GoldMines == wantM && w.StartLocations.Length == slots,
                            $"{what}: asks {w.Config.Map.Forests} / {w.Config.Map.GoldMines}, {w.StartLocations.Length} start locations");
                        string per = PerBase(w, startMines, out bool ok);
                        GD.Print($"QA NOTE: {what}: placed {w.ResourcePlacement.Forests} forests / {w.ResourcePlacement.Mines} mines in {clock.ElapsedMilliseconds} ms; per base {per}");
                        Check(ok, $"{what}: a base is short: {per}");
                        main.QuitToMenu();
                        await Frames(2);
                    }
                }
            }
            while (setup.SlotCount > 2) setup.RemoveSlot();
        }
        finally
        {
            if (main.CurrentMatch != null) main.QuitToMenu();
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
    }

    // Per start location: gold mines centred within StartMineDistance and trees within StartForestDistance of its centre.
    private static string PerBase(World w, int startMines, out bool ok)
    {
        ok = true;
        var parts = new List<string>();
        int width = w.NavGrid.Width;
        MapGenParams p = w.Config.Map;
        ResourceStore r = w.Resources;
        foreach (StartLocation st in w.StartLocations)
        {
            float sx = (st.Cell % width + 0.5f) * MapConstants.CellSize, sy = (st.Cell / width + 0.5f) * MapConstants.CellSize;
            int mines = 0, trees = 0;
            for (int i = 0; i < r.Capacity; i++)
            {
                if (!r.Alive[i]) continue;
                ResourceDef d = w.Data.Resources[r.TypeId[i]];
                int c = r.Cell[i];
                float cx = (c % width + d.FootprintWidth * 0.5f) * MapConstants.CellSize, cy = (c / width + d.FootprintHeight * 0.5f) * MapConstants.CellSize;
                float d2 = (cx - sx) * (cx - sx) + (cy - sy) * (cy - sy);
                if (d.Resource == ResourceKind.Gold && d2 <= (p.StartMineDistance + 0.01f) * (p.StartMineDistance + 0.01f)) mines++;
                if (d.Resource == ResourceKind.Wood && d2 <= (p.StartForestDistance + 2f) * (p.StartForestDistance + 2f)) trees++;
            }
            if (mines < startMines || trees == 0) ok = false;
            parts.Add($"{mines}m/{trees}t");
        }
        return string.Join(" ", parts);
    }

    // ---- (5) Layout ----

    private async Task Layout()
    {
        Main main = NewMain(Array.Empty<string>(), "layout", $"{_root}/layout/saves", $"{_root}/layout/replays");
        AddChild(main);
        await Frames(2);
        try
        {
            await Click(main.Menu!.SkirmishButton);
            SkirmishSetup setup = main.Setup!;
            setup.AddSlot();
            setup.AddSlot();
            await Frames(3);
            Vector2 view = GetViewport().GetVisibleRect().Size;
            var leaves = new List<Control>();
            foreach (Node n in setup.FindChildren("*", "Control", true, false))
            {
                if (n is not Control c || !c.IsVisibleInTree()) continue;
                if (c is Label or Button or SpinBox or LineEdit) if (c.GetParent() is not SpinBox) leaves.Add(c);
            }
            foreach (Control c in leaves)
            {
                Rect2 rc = c.GetGlobalRect();
                Check(rc.Position.X >= -0.5f && rc.Position.Y >= -0.5f && rc.End.X <= view.X + 0.5f && rc.End.Y <= view.Y + 0.5f,
                    $"layout 1280 x 720, 4 slots: {c.Name} '{(c as Label)?.Text ?? (c as Button)?.Text}' at {rc} is off the {view} screen");
            }
            for (int i = 0; i < leaves.Count; i++)
                for (int j = i + 1; j < leaves.Count; j++)
                {
                    Rect2 a = leaves[i].GetGlobalRect().Grow(-1f), b = leaves[j].GetGlobalRect().Grow(-1f);
                    if (a.Size.X > 0 && b.Size.X > 0 && a.Intersects(b))
                        Check(false, $"layout: {leaves[i].Name} {leaves[i].GetGlobalRect()} overlaps {leaves[j].Name} {leaves[j].GetGlobalRect()}");
                }
            GD.Print($"QA NOTE: setup layout at {view}: {leaves.Count} controls checked");
            await Shot("setup-4slots");
            while (setup.SlotCount > 2) setup.RemoveSlot();
        }
        finally
        {
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
    }

    // ---- helpers ----

    private static bool Press(Main main, Button b)
    {
        b.EmitSignal(BaseButton.SignalName.Pressed);
        return main.CurrentMatch != null;
    }

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

    private Main NewMain(string[] args, string label, string saves, string replays)
    {
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.UserArgs = args.Append(Main.NoConfineFlag).ToArray();
        main.SettingsPath = $"{_root}/{label}/settings.cfg";
        main.ReplayDir = replays;
        main.SaveDir = saves;
        main.BeforeStart = m => m.GetNode<SimRunner>("SimRunner").FixedFrameDelta = 0;
        return main;
    }

    private void Push(InputEvent e) => GetViewport().PushInput(e);

    private async Task Click(Control c)
    {
        Check(c.IsVisibleInTree(), $"{c.Name} is not visible");
        Vector2 at = c.GetGlobalRect().GetCenter();
        Push(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        await Frames(1);
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Left });
        if (_pressFrames > 0) await Frames(_pressFrames); // a real click holds the button for a few frames
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
        await Frames(2);
    }

    private int _pressFrames = 2;

    private async Task Shot(string name)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = $"{_shots}/qa-v15-{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"  screenshot {path}");
    }

    private static string Full(string path) => Path.GetFullPath(ProjectSettings.GlobalizePath(path));

    private static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static string Folder(string dir)
    {
        string full = Full(dir);
        if (!Directory.Exists(full)) return "(none)";
        return string.Join("|", Directory.GetFiles(full).OrderBy(f => f, StringComparer.Ordinal).Select(f => $"{Path.GetFileName(f)}:{new FileInfo(f).Length}"));
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
