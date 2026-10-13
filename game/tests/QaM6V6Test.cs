using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;

namespace Rts.Game.Tests;

/// <summary>
/// QA attacks on M6-V6 (session 2026-10-13-0916): the settings file and screen.
/// Reader: hand-edited files the dev rows don't cover (an integer too big for 64 bits, a float overflow, a float where an int
/// goes, duplicate keys and sections, a BOM with CRLF, an escaped quote, an empty file), each read in a child process so an
/// engine ERROR line is caught: the defaults for the bad keys, warnings only, never an ERROR. Writer: a read-only file
/// (false, one warning, the file untouched); the edge pan pick written as a round number. Shell: <c>--mute</c> plus a saved
/// master volume (muted and the volume both hold, through a commit); <c>--vsync on</c> over a file saying off (on for the run,
/// the file keeps off through Back); the pause stack (Settings from pause, Back button, Esc, Esc, Esc again pauses; the main
/// menu never shows under a match); Settings from the menu with Esc. Windowed only: the edge pan measured against the camera
/// at 0.5, 1 and 2 (focus shift per second over <c>CameraLimits.PanSpeed</c>), and window mode cycling windowed -> fullscreen
/// -> borderless -> windowed under a running match (ticks keep advancing, the HUD anchored to the viewport, a screenshot each).
/// </summary>
/// <remarks>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6V6Test.tscn</c>; prints "QA M6-V6 TEST PASS" or
/// "QA M6-V6 TEST FAIL: ..." lines. Windowed: <c>&amp; $env:GODOT --path game res://tests/QaM6V6Test.tscn -- --shots &lt;dir&gt;</c>.
/// Every file is under a per-process <c>user://qa-m6v6-&lt;pid&gt;/</c> folder (never the player's settings, never another run's).
/// </remarks>
public partial class QaM6V6Test : Node
{
    private readonly List<string> _failures = new();
    private string _dir = "";
    private string? _shots;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        string[] args = OS.GetCmdlineUserArgs();
        if (args.Length == 2 && args[0] == "--read")
        {
            GameSettings s = GameSettings.Load(args[1]);
            GD.Print($"QA SETTINGS READ {s.Encode().Replace("\n", " ")}");
            GetTree().Quit(0);
            return;
        }
        try
        {
            if (!SettingsApplier.HasWindow) GetTree().Root.Size = new Vector2I(1152, 648); // headless: a real window's size, so the buttons are laid out
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--shots") _shots = args[i + 1];
            _dir = $"user://qa-m6v6-{OS.GetProcessId()}";
            Directory.CreateDirectory(ProjectSettings.GlobalizePath(_dir));
            ReaderRows();
            WriterRows();
            await MuteRow();
            await VsyncRow();
            await PauseStack();
            await EdgePanMeasured();
            await WindowCycle();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        try { Directory.Delete(ProjectSettings.GlobalizePath(_dir), true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        foreach (string f in _failures.Take(60)) GD.Print($"QA M6-V6 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M6-V6 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private string PathOf(string name) => $"{_dir}/{name}";

    // ---- the reader: each file read by a child Godot so the engine's own ERROR lines are seen ----

    private void ReaderRows()
    {
        string good = new GameSettings { MusicVolume = 40 }.Encode();
        (string What, string Text, bool WantWarning, Func<GameSettings, bool> Ok)[] rows =
        {
            ("int beyond 64 bits", "[display]\nwidth=99999999999999999999\n", true, s => s.Width == GameSettings.DefaultWidth),
            ("negative int beyond 64 bits", "[audio]\nmaster=-99999999999999999999\n", true, s => s.MasterVolume == 100),
            ("float overflow", "[camera]\nedge_pan=1e999\n", true, s => s.EdgePan == 1f),
            ("float for an int", good.Replace("music=40", "music=40.0"), true, s => s.MusicVolume == 100),
            ("duplicate key, last wins in range", good.Replace("music=40", "music=40\nmusic=60"), false, s => s.MusicVolume == 60),
            ("duplicate section", good + "\n[audio]\nsfx=5\n", false, s => s.SfxVolume == 5 && s.MusicVolume == 40),
            ("BOM and CRLF", (char)0xFEFF + good.Replace("\n", "\r\n"), false, s => s.MusicVolume == 40),
            ("escaped quote", good.Replace("\"medium\"", "\"med\\\"ium\""), true, s => s.Quality == SettingsQuality.Medium),
            ("empty file", "", true, s => s.SameAs(new GameSettings())),
            ("nan text", good.Replace("edge_pan=1.0", "edge_pan=nan"), true, s => s.EdgePan == 1f),
        };
        foreach ((string what, string text, bool wantWarning, Func<GameSettings, bool> ok) in rows)
        {
            string path = PathOf("read.cfg");
            File.WriteAllText(ProjectSettings.GlobalizePath(path), text);
            // In process: no throw, the right values. Not for the 64-bit rows: until BUG-0680 is fixed Godot's parser logs an
            // ERROR for them, which would fail the whole scene in the loop's log scan; the child row below names that bug instead.
            if (!what.Contains("64 bits", StringComparison.Ordinal))
            {
                var problems = new List<string>();
                GameSettings s;
                try
                {
                    s = GameSettings.Read(path, problems, out _);
                }
                catch (Exception ex)
                {
                    _failures.Add($"read '{what}' threw {ex.GetType().Name}: {ex.Message}");
                    continue;
                }
                Check(ok(s) && (problems.Count > 0) == wantWarning, $"read '{what}': [{s.Encode().Replace("\n", " ")}], problems [{string.Join("; ", problems)}]");
            }
            // In a child: no ERROR line.
            var output = new Godot.Collections.Array();
            int exit = OS.Execute(OS.GetExecutablePath(), new[] { "--headless", "--path", ProjectSettings.GlobalizePath("res://"), "res://tests/QaM6V6Test.tscn", "--", "--read", path }, output, readStderr: true);
            var lines = new List<string>();
            foreach (Variant chunk in output) lines.AddRange(chunk.AsString().Split('\n').Select(l => l.TrimEnd('\r')));
            var errors = lines.Where(l => l.Contains("ERROR", StringComparison.Ordinal) && !l.TrimStart().StartsWith("[", StringComparison.Ordinal)).ToList();
            int warnings = lines.Count(l => l.StartsWith("WARNING: Settings:", StringComparison.Ordinal));
            Check(exit == 0 && errors.Count == 0 && warnings == (wantWarning ? 1 : 0) && lines.Any(l => l.StartsWith("QA SETTINGS READ", StringComparison.Ordinal)),
                $"boot read '{what}' (BUG-0680 for the 64-bit rows): exit {exit}, {warnings} settings warnings, errors [{string.Join(" | ", errors)}]");
        }
    }

    // ---- the writer ----

    private void WriterRows()
    {
        // A read-only file: Save says false (one warning, no throw) and leaves the file as it was.
        string path = PathOf("readonly.cfg");
        string full = ProjectSettings.GlobalizePath(path);
        var before = new GameSettings { SfxVolume = 12 };
        Check(before.Save(path), "could not write the read-only row's file");
        string text = File.ReadAllText(full);
        File.SetAttributes(full, FileAttributes.ReadOnly);
        try
        {
            bool saved = new GameSettings { SfxVolume = 99 }.Save(path);
            Check(!saved && File.ReadAllText(full) == text, $"save over a read-only file: returned {saved}, file now [{File.ReadAllText(full)}]");
            GameSettings back = GameSettings.Load(path);
            Check(back.SfxVolume == 12, $"the read-only file read back sfx {back.SfxVolume}");
        }
        finally
        {
            File.SetAttributes(full, FileAttributes.Normal);
        }

        // The edge pan pick goes to the file as the round number the slider shows (SettingsScreen.Picks rounds to 0.01).
        foreach (float pan in new[] { 0.55f, 0.65f, 1.15f, 1.35f, 1.95f })
        {
            string encoded = new GameSettings { EdgePan = (float)Math.Round((double)pan, 2) }.Encode();
            string line = encoded.Split('\n').FirstOrDefault(l => l.StartsWith("edge_pan=", StringComparison.Ordinal)) ?? "";
            Check(line == $"edge_pan={pan.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture)}", $"edge pan {pan} is written as '{line}'");
        }
    }

    // ---- the shell ----

    private async Task<Main> Boot(string settingsPath, params string[] userArgs)
    {
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.UserArgs = userArgs;
        main.SettingsPath = settingsPath;
        AddChild(main);
        await Frames(3);
        return main;
    }

    private async Task Drop(Main main)
    {
        if (GetTree().Paused) GetTree().Paused = false;
        RemoveChild(main);
        main.QueueFree();
        await Frames(2);
    }

    private static float MasterDb() => AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex(GameSettings.MasterBus));

    private async Task MuteRow()
    {
        string path = PathOf("mute.cfg");
        new GameSettings { MasterVolume = 50 }.Save(path);
        Main main = await Boot(path, "--units", "0", "--mute");
        Check(main.CurrentMatch != null && Sfx.Muted && Math.Abs(MasterDb() - GameSettings.VolumeDb(50)) < 0.01f,
            $"--mute with master 50: match {main.CurrentMatch != null}, muted {Sfx.Muted}, master {MasterDb()} dB");
        var picks = main.Settings.Clone();
        picks.MasterVolume = 0;
        main.CommitSettings(picks, stay: false);
        float db = MasterDb();
        Check(Sfx.Muted && !float.IsNaN(db) && Math.Abs(db - GameSettings.SilentDb) < 0.01f, $"after a commit at master 0 under --mute: muted {Sfx.Muted}, master {db} dB");
        await Drop(main);
        Sfx.SetMuted(false);
    }

    private async Task VsyncRow()
    {
        string path = PathOf("vsync.cfg");
        new GameSettings { Vsync = false }.Save(path);
        Main main = await Boot(path, "--units", "0", "--vsync", "on");
        Check(main.EffectiveVsync && !main.Settings.Vsync, $"--vsync on over a file saying off: effective {main.EffectiveVsync}, file's {main.Settings.Vsync}");
        if (SettingsApplier.HasWindow) Check(DisplayServer.WindowGetVsyncMode() == DisplayServer.VSyncMode.Enabled, $"--vsync on: window vsync {DisplayServer.WindowGetVsyncMode()}");
        // Through the pause menu's Settings and Back: the file keeps off, the run keeps on.
        await Key(Godot.Key.Escape);
        main.ShowSettings();
        await Frames(1);
        await Click(main.SettingsScreen!.BackButton);
        var file = new ConfigFile();
        Check(file.Load(path) == Error.Ok && !(bool)file.GetValue("display", "vsync", true) && main.EffectiveVsync,
            $"--vsync on, Back: file vsync {file.GetValue("display", "vsync", true)}, effective {main.EffectiveVsync}");
        if (SettingsApplier.HasWindow) Check(DisplayServer.WindowGetVsyncMode() == DisplayServer.VSyncMode.Enabled, $"--vsync on after Back: window vsync {DisplayServer.WindowGetVsyncMode()}");
        await Drop(main);
    }

    private async Task PauseStack()
    {
        string path = PathOf("pause.cfg");
        Main main = await Boot(path, "--units", "0");
        Match? match = main.CurrentMatch;
        PauseMenu pause = main.Pause!;
        SettingsScreen screen = main.SettingsScreen!;
        Check(match != null, "no match");
        if (match == null) return;

        // Settings from pause, the Back button: the pause menu, still paused, the match kept, no main menu.
        await Key(Godot.Key.Escape);
        await Click(pause.SettingsButton);
        Check(screen.Visible && GetTree().Paused && !main.Menu!.Visible, $"pause -> Settings: screen {screen.Visible}, paused {GetTree().Paused}, menu {main.Menu!.Visible}");
        await Click(screen.BackButton);
        Check(!screen.Visible && pause.Visible && GetTree().Paused && main.Paused && main.CurrentMatch == match && !main.Menu.Visible,
            $"Back over pause: screen {screen.Visible}, pause {pause.Visible}, paused {GetTree().Paused}, menu {main.Menu.Visible}");

        // Settings again, Esc, Esc: resumed; a third Esc pauses again (the stack is whole).
        await Click(pause.SettingsButton);
        await Key(Godot.Key.Escape);
        Check(!screen.Visible && pause.Visible && GetTree().Paused, $"Esc on settings over pause: screen {screen.Visible}, pause {pause.Visible}, paused {GetTree().Paused}");
        await Key(Godot.Key.Escape);
        Check(!GetTree().Paused && !pause.Visible && !screen.Visible, $"second Esc: paused {GetTree().Paused}, pause {pause.Visible}");
        int t0 = match.GetNode<SimRunner>("SimRunner").Simulation!.TickNumber;
        await Frames(10);
        Check(match.GetNode<SimRunner>("SimRunner").Simulation!.TickNumber > t0, "the match does not tick after Esc, Esc");
        await Key(Godot.Key.Escape);
        Check(GetTree().Paused && pause.Visible && !screen.Visible, $"third Esc: paused {GetTree().Paused}, pause {pause.Visible}, screen {screen.Visible}");

        // Two Esc in one frame on the settings over pause: closes the settings, then resumes; never the main menu.
        await Click(pause.SettingsButton);
        Push(new InputEventKey { Keycode = Godot.Key.Escape, PhysicalKeycode = Godot.Key.Escape, Pressed = true });
        Push(new InputEventKey { Keycode = Godot.Key.Escape, PhysicalKeycode = Godot.Key.Escape, Pressed = false });
        Push(new InputEventKey { Keycode = Godot.Key.Escape, PhysicalKeycode = Godot.Key.Escape, Pressed = true });
        Push(new InputEventKey { Keycode = Godot.Key.Escape, PhysicalKeycode = Godot.Key.Escape, Pressed = false });
        await Frames(2);
        Check(!screen.Visible && !main.Menu.Visible && main.CurrentMatch == match, $"two Esc in a frame: screen {screen.Visible}, menu {main.Menu.Visible}");
        await Drop(main);

        // From the main menu: Settings, Esc back to the menu; the menu's focus works (Skirmish still opens the setup).
        Main menuMain = await Boot(path);
        await Click(menuMain.Menu!.SettingsButton);
        await Key(Godot.Key.Escape);
        Check(menuMain.Menu.Visible && !menuMain.SettingsScreen!.Visible && menuMain.CurrentMatch == null, $"Esc on settings over the menu: menu {menuMain.Menu.Visible}");
        await Click(menuMain.Menu.SkirmishButton);
        Check(menuMain.Setup!.Visible, "Skirmish after the settings did not open the setup");
        await Drop(menuMain);
    }

    // ---- windowed only ----

    private async Task EdgePanMeasured()
    {
        if (!SettingsApplier.HasWindow) return;
        string path = PathOf("pan.cfg");
        Main main = await Boot(path, "--units", "0");
        Match match = main.CurrentMatch!;
        RtsCamera cam = match.GetNode<RtsCamera>("RtsCamera");
        DisplayServer.WindowMoveToForeground();
        await Frames(10);
        if (!DisplayServer.WindowIsFocused())
        {
            GD.Print("QA M6-V6 NOTE: window not focused; edge pan not measured");
            await Drop(main);
            return;
        }
        Vector2 size = GetViewport().GetVisibleRect().Size;
        var rates = new List<(float Pick, float Rate)>();
        foreach (float pick in new[] { 0.5f, 1f, 2f })
        {
            var s = main.Settings.Clone();
            s.EdgePan = pick;
            main.CommitSettings(s, stay: false);
            cam.SetFocus(cam.Focus.X - 60f, cam.Focus.Y); // room to the right
            Input.WarpMouse(new Vector2(size.X - 2f, size.Y * 0.5f));
            await Frames(3);
            float x0 = cam.Focus.X;
            ulong t0 = Time.GetTicksUsec();
            await Frames(30);
            float dt = (Time.GetTicksUsec() - t0) / 1e6f;
            rates.Add((pick, (cam.Focus.X - x0) / dt));
        }
        Input.WarpMouse(size * 0.5f);
        float baseRate = rates[1].Rate;
        GD.Print($"QA M6-V6 NOTE: edge pan m/s at 0.5 / 1 / 2: {string.Join(" / ", rates.Select(r => r.Rate.ToString("F2")))}");
        Check(baseRate > 0f && Math.Abs(rates[0].Rate / baseRate - 0.5f) < 0.1f && Math.Abs(rates[2].Rate / baseRate - 2f) < 0.3f,
            $"edge pan rates {string.Join(", ", rates.Select(r => $"{r.Pick}: {r.Rate:F2} m/s"))}: not 0.5x / 2x of 1");
        await Drop(main);
    }

    private async Task WindowCycle()
    {
        if (!SettingsApplier.HasWindow) return;
        string path = PathOf("window.cfg");
        Main main = await Boot(path, "--units", "20");
        Match match = main.CurrentMatch!;
        Simulation sim = match.GetNode<SimRunner>("SimRunner").Simulation!;
        Control minimap = match.GetNode<Control>("Hud/Minimap"), card = match.GetNode<Control>("Hud/CommandCard");
        RtsCamera cam = match.GetNode<RtsCamera>("RtsCamera");
        SettingsScreen screen = main.SettingsScreen!;
        foreach (SettingsWindowMode mode in new[] { SettingsWindowMode.Fullscreen, SettingsWindowMode.Borderless, SettingsWindowMode.Windowed })
        {
            // The player's path: Esc, Settings, the mode, Apply, Back, Esc (resume).
            await Key(Godot.Key.Escape);
            await Click(main.Pause!.SettingsButton);
            screen.WindowModeOption.Select((int)mode);
            await Click(screen.ApplyButton);
            await Frames(10);
            await Click(screen.BackButton);
            await Key(Godot.Key.Escape);
            await Frames(20);
            int t0 = sim.TickNumber;
            await Frames(20);
            Vector2 vp = GetViewport().GetVisibleRect().Size;
            Rect2 m = minimap.GetGlobalRect(), c = card.GetGlobalRect();
            bool laidOut = Math.Abs(m.End.Y - (vp.Y - 8f)) < 1.5f && Math.Abs(c.End.X - (vp.X - 8f)) < 1.5f && Math.Abs(c.End.Y - (vp.Y - 8f)) < 1.5f;
            DisplayServer.WindowMode want = mode switch
            {
                SettingsWindowMode.Fullscreen => DisplayServer.WindowMode.ExclusiveFullscreen,
                SettingsWindowMode.Borderless => DisplayServer.WindowMode.Fullscreen,
                _ => DisplayServer.WindowMode.Windowed,
            };
            GD.Print($"QA M6-V6 NOTE: {mode}: window {DisplayServer.WindowGetMode()} {DisplayServer.WindowGetSize()}, viewport {vp}, minimap {m}, card {c}, ticks +{sim.TickNumber - t0}");
            Check(DisplayServer.WindowGetMode() == want && sim.TickNumber > t0 && main.CurrentMatch == match && laidOut && !GetTree().Paused
                && !main.Menu!.Visible && !screen.Visible && main.Settings.WindowMode == mode,
                $"{mode}: window {DisplayServer.WindowGetMode()} (want {want}), ticks +{sim.TickNumber - t0}, hud laid out {laidOut} (viewport {vp}, minimap {m}, card {c}), paused {GetTree().Paused}, menu {main.Menu!.Visible}");
            await Shot($"qa-m6v6-{mode.ToString().ToLowerInvariant()}");
            // No lost input: a left click on a minimap corner still moves the camera.
            System.Numerics.Vector2 f0 = cam.Focus;
            Rect2 mm = minimap.GetGlobalRect();
            Vector2 at = mm.Position + mm.Size * (mode == SettingsWindowMode.Borderless ? new Vector2(0.1f, 0.9f) : new Vector2(0.9f, 0.1f)); // a corner the camera is not on
            Push(new InputEventMouseMotion { Position = at, GlobalPosition = at });
            await Frames(1);
            Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Left });
            Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
            await Frames(2);
            Check(System.Numerics.Vector2.Distance(f0, cam.Focus) > 10f, $"{mode}: a minimap click did not move the camera ({f0} -> {cam.Focus})");
        }
        Check(DisplayServer.WindowGetSize() == new Vector2I(GameSettings.DefaultWidth, GameSettings.DefaultHeight), $"back to windowed at {DisplayServer.WindowGetSize()}");
        await Drop(main);
    }

    // ---- helpers ----

    private void Push(InputEvent e) => GetViewport().PushInput(e);

    private async Task Click(Control c)
    {
        Check(c.IsVisibleInTree(), $"{c.Name} is not visible");
        Vector2 at = c.GetGlobalRect().GetCenter();
        Push(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        await Frames(1);
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Left });
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
        await Frames(2);
    }

    private async Task Key(Key key)
    {
        Push(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        Push(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
        await Frames(2);
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string name)
    {
        if (_shots == null) return;
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
