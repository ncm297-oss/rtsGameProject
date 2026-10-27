using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;

namespace Rts.Game.Tests;

/// <summary>
/// QA M6-VH4 (session 2026-10-26-2108): the BUG-1030 / BUG-1031 / BUG-1002 fixes attacked beyond the dev's rows.
/// (1) BUG-1030: 200 rows with damaged saves at the top, the middle and the bottom; each refused in turn (bottom, top, middle,
/// bottom, middle) with the list scrolled to the far end first, and once through <c>LoadSavedGame</c> from the main menu
/// (the screen hidden). Visibility is read with <c>Tree.GetItemAtPosition</c> over the tree's visible height (independent of
/// <c>GetItemAreaRect</c>'s coordinate convention); the pick is the exact file. (2) A resize of Load Game with the bottom row
/// picked (1920 x 1080, 1152 x 648, 1280 x 720): the pick is kept; whether it stays on screen is logged. (3) BUG-1002: a
/// readable lone <c>.replay</c> and a junk one among 200 saves: listed with the right kinds, Watch plays the readable one,
/// Delete removes exactly each, the replays beside saves are not listed twice. (4) BUG-1031: a one-frame click on Start
/// after typing (no Enter) the floor-breaking "0", the cap "64", over the cap "500", junk "zz" and an emptied box into
/// both boxes (Forests never focused); and an emptied box with a real Enter key.
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6VH4Test.tscn</c>; prints "QA M6VH4 TEST PASS" or "QA M6VH4 TEST FAIL: ..." lines.</remarks>
public partial class QaM6VH4Test : Node
{
    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private string _root = "", _rootFull = "";

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            GetTree().Root.Size = new Vector2I(1280, 720);
            await Frames(1);
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            _root = $"user://qa-m6vh4-{OS.GetProcessId()}";
            _rootFull = Full(_root);
            if (Directory.Exists(_rootFull)) Directory.Delete(_rootFull, true);

            string? good = await MakeSave();
            if (Check(good != null && File.Exists(SaveStore.ReplayPathOf(good!)), "no good save with a recording beside it"))
            {
                await RefusedEverywhere(good!);
                await LoneReplays(good!);
            }
            await TypedOneFrame();
            try { Directory.Delete(_rootFull, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        foreach (string f in _failures.Take(80)) GD.Print($"QA M6VH4 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M6VH4 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // A save at tick 5 of the default skirmish, with its recording beside it.
    private async Task<string?> MakeSave()
    {
        Main main = NewMain(new[] { "--skirmish", "--seed", "11", "--mute" }, "make", $"{_root}/make/saves", $"{_root}/make/replays");
        AddChild(main);
        await Frames(2);
        try
        {
            SimRunner runner = main.CurrentMatch!.GetNode<SimRunner>("SimRunner");
            Simulation sim = runner.Simulation!;
            var clock = Stopwatch.StartNew();
            while (sim.TickNumber < 5 && clock.Elapsed.TotalSeconds < 60)
            {
                runner.FixedFrameDelta = 0.05;
                await Frames(1);
            }
            runner.FixedFrameDelta = 0;
            main.PauseMatch();
            bool ok = main.SaveGame();
            return ok ? main.LastSavePath : null;
        }
        finally
        {
            if (main.CurrentMatch != null) main.QuitToMenu();
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
    }

    // ---- (1) + (2) BUG-1030 ----

    private async Task RefusedEverywhere(string good)
    {
        byte[] bytes = File.ReadAllBytes(good);
        byte[] bad = bytes.ToArray();
        bad[bad.Length / 2] ^= 0x5A;
        var day = new DateTime(2022, 5, 5, 0, 0, 0);
        string dir = Full($"{_root}/rows/saves");
        Directory.CreateDirectory(dir);
        // 200 saves, newest first: minute 199 is row 0. Damaged at rows 0, 100, 199.
        string top = Path.Combine(dir, SaveStore.FileName(day.AddMinutes(199), 11));
        string mid = Path.Combine(dir, SaveStore.FileName(day.AddMinutes(99), 11));
        string bottom = Path.Combine(dir, SaveStore.FileName(day, 11));
        for (int i = 0; i < 200; i++)
        {
            string p = Path.Combine(dir, SaveStore.FileName(day.AddMinutes(i), 11));
            File.WriteAllBytes(p, p == top || p == mid || p == bottom ? bad : bytes);
        }
        Main main = NewMain(Array.Empty<string>(), "rows", $"{_root}/rows/saves", $"{_root}/rows/replays");
        AddChild(main);
        await Frames(2);
        try
        {
            main.Menu!.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);
            LoadGameScreen load = main.LoadScreen!;
            Check(load.RowCount == 200 && load.RowOf(top) == 0 && load.RowOf(mid) == 100 && load.RowOf(bottom) == 199,
                $"rows: {load.RowCount} rows, top {load.RowOf(top)}, mid {load.RowOf(mid)}, bottom {load.RowOf(bottom)}");
            (string path, string name, int far)[] order = { (bottom, "bottom", 0), (top, "top", 199), (mid, "middle", 0), (bottom, "bottom again", 100), (mid, "middle again", 199) };
            foreach ((string path, string name, int far) in order)
            {
                int row = load.RowOf(path);
                // The player scrolled away (to row `far`), then picks the damaged row by hand and presses Load.
                Check(load.Select(row), $"{name}: Select({row})");
                load.List.ScrollToItem(load.List.GetRoot().GetChild(far));
                await Frames(1);
                bool started = Press(main, load.LoadButton);
                await Frames(2);
                Check(!started && load.SelectedIndex == row && SamePath(load.RowPath(load.SelectedIndex)!, path) && !load.DeleteButton.Disabled,
                    $"{name}: refused Load: started {started}, picked {load.SelectedIndex} (want {row})");
                Check(OnScreen(load.List, out string why), $"BUG-1030: {name}: the refused row is off-screen ({why})");
            }
            // The --load style call from the main menu: the screen is hidden when the refusal comes in.
            load.BackButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);
            Check(!main.LoadSavedGame(bottom) && load.Visible && SamePath(load.RowPath(load.SelectedIndex)!, bottom),
                $"LoadSavedGame(bottom) from the menu: visible {load.Visible}, row {load.SelectedIndex}");
            await Frames(2);
            Check(OnScreen(load.List, out string why2), $"BUG-1030: LoadSavedGame(bottom) from the menu: the row is off-screen ({why2})");

            // (2) Resize with the bottom row picked.
            foreach (Vector2I size in new[] { new Vector2I(1920, 1080), new Vector2I(1152, 648), new Vector2I(800, 600), new Vector2I(1280, 720) })
            {
                GetTree().Root.Size = size;
                await Frames(3);
                bool on = OnScreen(load.List, out string why3);
                Check(load.SelectedIndex == 199, $"resize to {size}: the pick moved to {load.SelectedIndex}");
                GD.Print($"QA NOTE: resize to {size} with the bottom row picked: on screen {on} ({why3})");
            }
            GetTree().Root.Size = new Vector2I(1280, 720);
            await Frames(2);

            // Delete still removes exactly the picked file.
            Check(load.Select(load.RowOf(bottom)), "re-pick bottom");
            load.DeleteButton.EmitSignal(BaseButton.SignalName.Pressed);
            Check(!File.Exists(bottom) && File.Exists(mid) && File.Exists(top) && Directory.GetFiles(dir, "*.sav").Length == 199, "Delete of the bottom row removed something else");
        }
        finally
        {
            if (main.CurrentMatch != null) main.QuitToMenu();
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
    }

    // ---- (3) BUG-1002 lone replays ----

    private async Task LoneReplays(string good)
    {
        byte[] bytes = File.ReadAllBytes(good), rep = File.ReadAllBytes(SaveStore.ReplayPathOf(good));
        var day = new DateTime(2021, 2, 2, 0, 0, 0);
        string dir = Full($"{_root}/lone/saves");
        Directory.CreateDirectory(dir);
        for (int i = 0; i < 200; i++)
        {
            string p = Path.Combine(dir, SaveStore.FileName(day.AddMinutes(i), 11));
            File.WriteAllBytes(p, bytes);
            if (i % 2 == 0) File.WriteAllBytes(SaveStore.ReplayPathOf(p), rep); // half the saves have their recording beside
        }
        string loneGood = SaveStore.ReplayPathOf(Path.Combine(dir, SaveStore.FileName(day.AddMinutes(500), 11)));
        string loneJunk = SaveStore.ReplayPathOf(Path.Combine(dir, SaveStore.FileName(day.AddMinutes(-500), 11)));
        File.WriteAllBytes(loneGood, rep);
        File.WriteAllText(loneJunk, "junk");
        Main main = NewMain(Array.Empty<string>(), "lone", $"{_root}/lone/saves", $"{_root}/lone/replays");
        AddChild(main);
        await Frames(2);
        try
        {
            main.Menu!.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(1);
            LoadGameScreen load = main.LoadScreen!;
            int rGood = load.RowOf(loneGood), rJunk = load.RowOf(loneJunk);
            Check(load.RowCount == 202 && load.Saves.Count == 200 && load.Skipped == 1,
                $"lone: {load.RowCount} rows (want 202: the 100 recordings beside saves not listed), {load.Saves.Count} saves, {load.Skipped} skipped");
            Check(rGood >= 0 && load.KindOf(rGood) == LoadGameScreen.RowKind.Replay, $"lone readable .replay: row {rGood}, kind {(rGood >= 0 ? load.KindOf(rGood) : default)}");
            Check(rJunk >= 0 && load.KindOf(rJunk) == LoadGameScreen.RowKind.UnreadableReplay, $"lone junk .replay: row {rJunk}");
            for (int r = 0; r < load.RowCount; r++)
                if (load.KindOf(r) == LoadGameScreen.RowKind.Replay && r != rGood) Check(false, $"lone: row {r} lists {load.RowPath(r)} as a replay (beside its save)");
            Check(load.Select(rGood) && !load.WatchButton.Disabled && load.LoadButton.Disabled && !load.DeleteButton.Disabled,
                $"lone readable: Watch {!load.WatchButton.Disabled}, Load {!load.LoadButton.Disabled}, Delete {!load.DeleteButton.Disabled}");
            bool watching = Press(main, load.WatchButton);
            Check(watching && main.CurrentMatch!.IsPlayback, $"lone readable .replay: Watch started {watching}");
            if (main.CurrentMatch != null) { main.QuitToMenu(); await Frames(2); }
            Check(File.Exists(loneGood), "watching the lone replay removed it");
            main.Menu.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(1);
            rGood = load.RowOf(loneGood);
            int files = Directory.GetFiles(dir).Length;
            Check(load.Select(rGood), "lone readable: re-pick");
            load.DeleteButton.EmitSignal(BaseButton.SignalName.Pressed);
            Check(!File.Exists(loneGood) && Directory.GetFiles(dir).Length == files - 1 && load.RowCount == 201, $"lone readable: Delete: left {File.Exists(loneGood)}, files {Directory.GetFiles(dir).Length} (want {files - 1})");
            rJunk = load.RowOf(loneJunk);
            Check(load.Select(rJunk), "lone junk: pick");
            load.DeleteButton.EmitSignal(BaseButton.SignalName.Pressed);
            Check(!File.Exists(loneJunk) && Directory.GetFiles(dir).Length == files - 2 && load.RowCount == 200 && load.Skipped == 0,
                $"lone junk: Delete: left {File.Exists(loneJunk)}, {load.RowCount} rows, {load.Skipped} skipped");
            Check(Directory.GetFiles(dir, "*.replay").Length == 100, $"the recordings beside saves: {Directory.GetFiles(dir, "*.replay").Length} left (want 100)");
        }
        finally
        {
            if (main.CurrentMatch != null) main.QuitToMenu();
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
    }

    // ---- (4) BUG-1031 ----

    private async Task TypedOneFrame()
    {
        Main main = NewMain(Array.Empty<string>(), "typed", $"{_root}/typed/saves", $"{_root}/typed/replays");
        AddChild(main);
        await Frames(2);
        try
        {
            await Click(main.Menu!.SkirmishButton, 2);
            SkirmishSetup setup = main.Setup!;
            LineEdit forests = setup.ForestsSpin.GetLineEdit(), mines = setup.MinesSpin.GetLineEdit();
            int floorF = SkirmishSetup.MinForestsFor(setup.SlotCount), floorM = SkirmishSetup.MinMinesFor(setup.SlotCount, _data.Rules.StartMineCount);
            setup.ForestsSpin.Value = 9;
            setup.MinesSpin.Value = 9;
            int lastF = 9, lastM = 9;
            foreach ((string text, int wantF, int wantM, string what) in new[]
            {
                ("0", floorF, floorM, "floor"), ("64", 64, 64, "cap"), ("500", 64, 64, "over cap"), ("zz", -1, -1, "junk"),
                ("13", 13, 13, "plain"), ("", -1, -1, "empty"),
            })
            {
                int wf = wantF < 0 ? lastF : wantF, wm = wantM < 0 ? lastM : wantM;
                await Click(mines, 2);
                mines.Text = text;
                forests.Text = text; // never focused: only MatchArgs' Commit takes it
                await Click(setup.StartButton, 0);
                var map = main.CurrentMatch?.Simulation?.World.Config.Map;
                Check(map != null && map.Forests == wf && map.GoldMines == wm,
                    $"BUG-1031: typed '{text}' ({what}) into both boxes, one-frame Start: the match asks {map?.Forests} / {map?.GoldMines} (want {wf} / {wm})");
                if (main.CurrentMatch != null) { main.QuitToMenu(); await Frames(2); }
                await Click(main.Menu.SkirmishButton, 2);
                Check(setup.ForestsPick == wf && setup.MinesPick == wm && forests.Text == wf.ToString() && mines.Text == wm.ToString(),
                    $"after '{text}' ({what}): setup {setup.ForestsPick} / {setup.MinesPick}, boxes '{forests.Text}' / '{mines.Text}' (want {wf} / {wm})");
                lastF = wf;
                lastM = wm;
            }
            // An emptied box and a real Enter key.
            await Click(mines, 2);
            mines.Text = "";
            Push(new InputEventKey { Keycode = Key.Enter, PhysicalKeycode = Key.Enter, Pressed = true });
            Push(new InputEventKey { Keycode = Key.Enter, PhysicalKeycode = Key.Enter, Pressed = false });
            await Frames(2);
            Check(mines.Text == lastM.ToString() && setup.MinesPick == lastM, $"BUG-1031: emptied box + Enter key: box '{mines.Text}', pick {setup.MinesPick} (want {lastM})");
            // A typed number + Enter key (the redraw must not put the old value back).
            await Click(mines, 2);
            mines.Text = "21";
            Push(new InputEventKey { Keycode = Key.Enter, PhysicalKeycode = Key.Enter, Pressed = true });
            Push(new InputEventKey { Keycode = Key.Enter, PhysicalKeycode = Key.Enter, Pressed = false });
            await Frames(2);
            Check(mines.Text == "21" && setup.MinesPick == 21, $"typed 21 + Enter key: box '{mines.Text}', pick {setup.MinesPick}");
            await Click(setup.BackButton, 2);
        }
        finally
        {
            if (main.CurrentMatch != null) main.QuitToMenu();
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
    }

    // ---- helpers ----

    // The picked row shows inside the tree's visible area: some point in it maps back to the picked item (the tree's own hit
    // test, which knows the scroll), and the whole row band fits.
    private static bool OnScreen(Tree tree, out string why)
    {
        TreeItem? item = tree.GetSelected();
        int first = -1, last = -1;
        float h = tree.Size.Y;
        for (int y = 0; y < (int)h; y++)
        {
            if (tree.GetItemAtPosition(new Vector2(tree.Size.X / 2f, y)) != item || item == null) continue;
            if (first < 0) first = y;
            last = y;
        }
        Rect2 area = item != null ? tree.GetItemAreaRect(item) : new Rect2();
        why = $"hit rows y {first}-{last} of {h}, GetItemAreaRect y {area.Position.Y}-{area.End.Y}, scroll {tree.GetScroll().Y}";
        return item != null && first >= 0 && last - first >= area.Size.Y * 0.8f - 2f;
    }

    private static bool Press(Main main, Button b)
    {
        b.EmitSignal(BaseButton.SignalName.Pressed);
        return main.CurrentMatch != null;
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

    private async Task Click(Control c, int pressFrames)
    {
        Check(c.IsVisibleInTree(), $"{c.Name} is not visible");
        Vector2 at = c.GetGlobalRect().GetCenter();
        Push(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        await Frames(1);
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Left });
        if (pressFrames > 0) await Frames(pressFrames);
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
        await Frames(2);
    }

    private static string Full(string path) => Path.GetFullPath(ProjectSettings.GlobalizePath(path));

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
