using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Rts.Game.Tests;

/// <summary>
/// M6-V6: the settings screen through the boot scene (<c>Main.tscn</c>) and injected clicks and keys. Rows: the dB mapping, the
/// file reader (missing, corrupt, every key missing, wrong types, out of range, extra keys, a round trip), the resolution list
/// and the edge pan vector; then menu -> Settings (every field with its <c>ui.json</c> text and the current values, no other
/// text), every field changed, Back: the file (read back through <see cref="ConfigFile"/>), the bus volumes, the quality on the
/// viewport; reopened shows the picks; Defaults restores the defaults and the file; Apply stays. A skirmish's camera takes the
/// edge pan; from the pause menu the same screen opens over the paused match and Esc returns to the pause menu still paused.
/// A child boot on a corrupt file gets the defaults with one warning and no error line.
/// </summary>
/// <remarks>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/SettingsTest.tscn</c>; prints "SETTINGS TEST PASS" and exits 0,
/// else "SETTINGS TEST FAIL: ..." lines and exits 1. Headless has no window, so the window mode, size and VSync calls are skipped
/// and asserted through the stored settings and the file. Windowed with <c>-- --shots &lt;dir&gt;</c> it saves
/// <c>settings-menu.png</c> and <c>settings-pause.png</c> and cycles the window mode borderless and back under the paused match.
/// The test never touches the player's <c>user://settings.cfg</c>: <see cref="Main.SettingsPath"/> points at <see cref="TestPath"/>.
/// </remarks>
public partial class SettingsTest : Node
{
    /// <summary>The settings file this test reads and writes.</summary>
    public const string TestPath = "user://settings-test/settings.cfg";

    private readonly List<string> _failures = new();
    private UiText _ui = null!;
    private Main _main = null!;
    private string? _shots;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        string[] args = OS.GetCmdlineUserArgs();
        if (args.Length == 2 && args[0] == "--boot-only")
        {
            await BootOnly(args[1]);
            return;
        }
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--shots") _shots = args[i + 1];
            _ui = UiText.Shared ?? throw new InvalidOperationException("ui.json failed to load");
            DeleteTestFile();
            MappingRows();
            ReaderRows();
            await Flow();
            CorruptBoot();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        DeleteTestFile();
        foreach (string f in _failures.Take(60)) GD.Print($"SETTINGS TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("SETTINGS TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private static void DeleteTestFile()
    {
        string full = ProjectSettings.GlobalizePath(TestPath);
        if (File.Exists(full)) File.Delete(full);
    }

    // ---- rows ----

    private void MappingRows()
    {
        (int Volume, float Db)[] cases = { (0, GameSettings.SilentDb), (-5, GameSettings.SilentDb), (100, 0f), (999, 0f), (50, -6.0206f), (10, -20f), (1, -40f) };
        foreach ((int v, float db) in cases)
            Check(Math.Abs(GameSettings.VolumeDb(v) - db) < 0.01f && float.IsFinite(GameSettings.VolumeDb(v)), $"VolumeDb({v}) = {GameSettings.VolumeDb(v)}, want {db}");
        Vector2I[] list = SettingsScreen.ResolutionList(new Vector2I(1000, 700), new Vector2I(1920, 1080));
        Check(list.Contains(new Vector2I(1000, 700)) && list.Count(r => r == new Vector2I(1920, 1080)) == 1 && list.Length == SettingsScreen.CommonResolutions.Length + 1
            && list.SequenceEqual(list.OrderBy(r => r.X).ThenBy(r => r.Y)), $"resolution list [{string.Join(" ", list)}]");
        Check(!SettingsScreen.ResolutionList(new Vector2I(1, 1), new Vector2I(1280, 720)).Contains(new Vector2I(1, 1)), "a 1 x 1 window joined the resolution list");
        // The edge pan multiplies the edge's push only; the keys stay at 1.
        Check(RtsCamera.PanVector(Vector2.Zero, Vector2.Right, 0.5f).IsEqualApprox(new Vector2(0.5f, 0f)) && RtsCamera.PanVector(Vector2.Zero, Vector2.Right, 2f).IsEqualApprox(new Vector2(2f, 0f))
            && RtsCamera.PanVector(Vector2.Right, Vector2.Zero, 2f).IsEqualApprox(Vector2.Right) && RtsCamera.PanVector(Vector2.Right, Vector2.Right, 2f).IsEqualApprox(new Vector2(2f, 0f))
            && RtsCamera.PanVector(Vector2.Zero, Vector2.Right, float.NaN) == Vector2.Zero, "PanVector rows");
    }

    private void ReaderRows()
    {
        var defaults = new GameSettings();
        (string What, string? Text, int Problems, Func<GameSettings, bool> Ok)[] rows =
        {
            ("no file", null, 0, s => s.SameAs(defaults)),
            ("corrupt", "garbage {{{\n[x\n", 1, s => s.SameAs(defaults)),
            ("a value Godot can't parse", "[display]\nwidth=\n", 1, s => s.SameAs(defaults)),
            ("every key missing", "[display]\n", 9, s => s.SameAs(defaults)),
            ("wrong types", "[display]\nwidth=\"wide\"\nvsync=1\nquality=2\n", 9, s => s.SameAs(defaults)),
            ("out of range", "[display]\nwidth=1\nheight=1\nwindow_mode=\"windowed\"\nvsync=true\nquality=\"ultra\"\n[audio]\nmaster=999\nmusic=-1\nsfx=50\n[camera]\nedge_pan=9.0\n", 6,
                s => s.Width == defaults.Width && s.Height == defaults.Height && s.Quality == defaults.Quality && s.MasterVolume == 100 && s.MusicVolume == 100 && s.SfxVolume == 50 && s.EdgePan == 1f),
            ("extra keys", new GameSettings { MusicVolume = 7 }.Encode() + "\n[keys]\nfuture=\"x\"\n[display]\nextra=3\n", 0, s => s.MusicVolume == 7),
        };
        foreach ((string what, string? text, int want, Func<GameSettings, bool> ok) in rows)
        {
            DeleteTestFile();
            if (text != null) WriteTestFile(text);
            var problems = new List<string>();
            GameSettings s = GameSettings.Read(TestPath, problems, out bool found);
            Check(found == (text != null) && problems.Count == want && ok(s), $"read '{what}': {problems.Count} problems (want {want}): [{string.Join("; ", problems)}]");
        }
        var picks = new GameSettings { Width = 1920, Height = 1200, WindowMode = SettingsWindowMode.Fullscreen, Vsync = false, Quality = SettingsQuality.Low, MasterVolume = 0, MusicVolume = 55, SfxVolume = 1, EdgePan = 0.5f };
        Check(picks.Save(TestPath), "save failed");
        GameSettings back = GameSettings.Read(TestPath, new List<string>(), out _);
        Check(back.SameAs(picks), $"round trip: wrote [{picks.Encode()}], read [{back.Encode()}]");
        // A path that can't be written (its folder is a file) is one warning and false, never an error or a throw.
        Check(!picks.Save(TestPath + "/inner.cfg"), "a save under a file's name claimed success");
        DeleteTestFile();
    }

    private async Task Flow()
    {
        _main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        _main.UserArgs = Array.Empty<string>();
        _main.SettingsPath = TestPath;
        AddChild(_main);
        await Frames(2);
        MainMenu menu = _main.Menu!;
        SettingsScreen screen = _main.SettingsScreen!;
        Check(_main.Settings.SameAs(new GameSettings()) && menu.SettingsButton.Text == _ui.Menu(MenuText.Settings), $"boot: settings [{_main.Settings.Encode()}], button '{menu.SettingsButton.Text}'");

        // Menu -> Settings: every field with its text and the current values, nothing else on screen.
        await Click(menu.SettingsButton);
        Check(screen.Visible && !menu.Visible, $"Settings: screen {screen.Visible}, menu {menu.Visible}");
        foreach (SettingsText k in new[] { SettingsText.Title, SettingsText.Resolution, SettingsText.WindowMode, SettingsText.Vsync, SettingsText.Quality,
            SettingsText.MasterVolume, SettingsText.MusicVolume, SettingsText.SfxVolume, SettingsText.EdgePanSpeed })
            Check(Labels(screen).Contains(_ui.Settings(k)), $"settings has no label '{_ui.Settings(k)}'");
        Check(screen.ApplyButton.Text == _ui.Settings(SettingsText.Apply) && screen.DefaultsButton.Text == _ui.Settings(SettingsText.Defaults) && screen.BackButton.Text == _ui.Settings(SettingsText.Back), "button text");
        Check(Items(screen.WindowModeOption).SequenceEqual(new[] { SettingsText.WindowWindowed, SettingsText.WindowBorderless, SettingsText.WindowFullscreen }.Select(_ui.Settings))
            && Items(screen.VsyncOption).SequenceEqual(new[] { SettingsText.VsyncOn, SettingsText.VsyncOff }.Select(_ui.Settings))
            && Items(screen.QualityOption).SequenceEqual(new[] { SettingsText.QualityLow, SettingsText.QualityMedium, SettingsText.QualityHigh }.Select(_ui.Settings))
            && Items(screen.ResolutionOption).SequenceEqual(screen.Resolutions.Select(r => _ui.ResolutionItem(r.X, r.Y))), "list items not from ui.json");
        var allowed = new HashSet<string>(Enum.GetValues<SettingsText>().Select(_ui.Settings)) { "" };
        foreach (Node n in screen.FindChildren("*", "", true, false))
        {
            string? text = n switch { Label l => l.Text, OptionButton => null, Button b => b.Text, _ => null };
            if (text != null) Check(allowed.Contains(text) || double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _),
                $"settings shows '{text}' ({n.Name}), not from ui.json and not a number");
        }
        ShowsSettings(screen, new GameSettings(), "first open");
        await Shot("settings-menu");

        // Every field changed, then Back.
        var picks = new GameSettings { Width = 1920, Height = 1080, WindowMode = SettingsWindowMode.Borderless, Vsync = false, Quality = SettingsQuality.High, MasterVolume = 80, MusicVolume = 30, SfxVolume = 0, EdgePan = 1.5f };
        Pick(screen, picks);
        await Click(screen.BackButton);
        Check(!screen.Visible && menu.Visible, $"Back: screen {screen.Visible}, menu {menu.Visible}");
        Committed(picks, "Back");
        await Click(menu.SettingsButton);
        ShowsSettings(screen, picks, "reopened");

        // Defaults: the defaults on screen, in effect and in the file.
        await Click(screen.DefaultsButton);
        Check(screen.Visible, "Defaults closed the screen");
        ShowsSettings(screen, new GameSettings(), "Defaults");
        Committed(new GameSettings(), "Defaults");

        // Apply stays on the screen; Esc is Back.
        var apply = new GameSettings { EdgePan = 2f, MusicVolume = 64 };
        Pick(screen, apply);
        await Click(screen.ApplyButton);
        Check(screen.Visible, "Apply closed the screen");
        Committed(apply, "Apply");
        await Type(Key.Escape);
        Check(!screen.Visible && menu.Visible, "Esc on the settings did not go back to the menu");

        // A skirmish takes the edge pan; the effects play on the Sfx bus.
        await Click(menu.SkirmishButton);
        await Click(_main.Setup!.StartButton);
        await Frames(3);
        Match? match = _main.CurrentMatch;
        Check(match != null, "Start made no match");
        if (match == null) return;
        Check(match.GetNode<RtsCamera>("RtsCamera").EdgePanSpeed == 2f, $"the new match's edge pan {match.GetNode<RtsCamera>("RtsCamera").EdgePanSpeed}, want 2");
        Check(match.GetNode<Sfx>("Sfx").GetChildren().OfType<AudioStreamPlayer>().All(p => p.Bus == GameSettings.SfxBus), "an effect player is not on the Sfx bus");
        foreach (string bus in new[] { GameSettings.MusicBus, GameSettings.SfxBus })
        {
            int i = AudioServer.GetBusIndex(bus);
            Check(i > 0 && AudioServer.GetBusSend(i) == GameSettings.MasterBus, $"bus {bus}: index {i}, sends to '{(i > 0 ? AudioServer.GetBusSend(i) : "")}'");
        }

        // From the pause menu: the same screen over the paused match; Esc back to the pause menu, still paused; Esc resumes.
        await Type(Key.Escape);
        PauseMenu pause = _main.Pause!;
        Check(_main.Paused && pause.SettingsButton.Text == _ui.Pause(PauseText.Settings), $"pause: {_main.Paused}, button '{pause.SettingsButton.Text}'");
        await Click(pause.SettingsButton);
        Check(screen.Visible && GetTree().Paused && _main.Paused, $"pause -> Settings: screen {screen.Visible}, tree paused {GetTree().Paused}");
        ShowsSettings(screen, apply, "from pause");
        await Shot("settings-pause");
        await WindowCycle(screen, match);
        var fromPause = _main.Settings.Clone();
        fromPause.EdgePan = 0.5f;
        Pick(screen, fromPause);
        await Type(Key.Escape);
        Check(!screen.Visible && pause.Visible && GetTree().Paused && _main.Paused && _main.CurrentMatch == match, $"Esc on settings over pause: screen {screen.Visible}, pause {pause.Visible}, tree paused {GetTree().Paused}");
        Check(match.GetNode<RtsCamera>("RtsCamera").EdgePanSpeed == 0.5f, $"the running match's edge pan {match.GetNode<RtsCamera>("RtsCamera").EdgePanSpeed}, want 0.5");
        Committed(fromPause, "Esc over pause");
        await Type(Key.Escape);
        Check(!GetTree().Paused && !_main.Paused && !pause.Visible, "the second Esc did not resume");
        RemoveChild(_main);
        _main.QueueFree();
        await Frames(2);
    }

    // Windowed only: borderless and back with Apply under the paused match; the match survives both.
    private async Task WindowCycle(SettingsScreen screen, Match match)
    {
        if (!SettingsApplier.HasWindow) return;
        GameSettings start = _main.Settings.Clone();
        foreach (SettingsWindowMode mode in new[] { SettingsWindowMode.Borderless, SettingsWindowMode.Windowed })
        {
            GameSettings s = start.Clone();
            s.WindowMode = mode;
            Pick(screen, s);
            await Click(screen.ApplyButton);
            await Frames(10);
            DisplayServer.WindowMode want = mode == SettingsWindowMode.Windowed ? DisplayServer.WindowMode.Windowed : DisplayServer.WindowMode.Fullscreen;
            Check(DisplayServer.WindowGetMode() == want && _main.CurrentMatch == match && screen.Visible, $"window mode {mode}: the window is {DisplayServer.WindowGetMode()}, match kept {_main.CurrentMatch == match}");
        }
    }

    // The settings in effect and in the file equal `want`, and the buses and the viewport follow them.
    private void Committed(GameSettings want, string what)
    {
        Check(_main.Settings.SameAs(want), $"{what}: in effect [{_main.Settings.Encode()}], want [{want.Encode()}]");
        var file = new ConfigFile();
        Check(file.Load(TestPath) == Error.Ok, $"{what}: {TestPath} does not load");
        Check((int)file.GetValue("display", "width", 0) == want.Width && (int)file.GetValue("display", "height", 0) == want.Height
            && (string)file.GetValue("display", "window_mode", "") == UiText.Key(want.WindowMode) && (bool)file.GetValue("display", "vsync", !want.Vsync) == want.Vsync
            && (string)file.GetValue("display", "quality", "") == UiText.Key(want.Quality) && (int)file.GetValue("audio", "master", -1) == want.MasterVolume
            && (int)file.GetValue("audio", "music", -1) == want.MusicVolume && (int)file.GetValue("audio", "sfx", -1) == want.SfxVolume
            && Math.Abs((float)file.GetValue("camera", "edge_pan", 0f) - want.EdgePan) < 1e-4f, $"{what}: the file holds [{FileText()}], want [{want.Encode()}]");
        foreach ((string bus, int volume) in new[] { (GameSettings.MasterBus, want.MasterVolume), (GameSettings.MusicBus, want.MusicVolume), (GameSettings.SfxBus, want.SfxVolume) })
        {
            float db = AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex(bus));
            Check(Math.Abs(db - GameSettings.VolumeDb(volume)) < 0.01f && !float.IsNaN(db), $"{what}: bus {bus} at {db} dB, want {GameSettings.VolumeDb(volume)} (volume {volume})");
        }
        SettingsApplier.Preset p = SettingsApplier.Presets[(int)want.Quality];
        Viewport root = GetTree().Root;
        Check(root.Msaa3D == p.Msaa && Math.Abs(root.Scaling3DScale - p.RenderScale) < 1e-4f, $"{what}: viewport MSAA {root.Msaa3D}, scale {root.Scaling3DScale}, want {p.Msaa}, {p.RenderScale}");
    }

    private void ShowsSettings(SettingsScreen screen, GameSettings want, string what)
    {
        Check(screen.Picks().SameAs(want), $"{what}: the screen shows [{screen.Picks().Encode()}], want [{want.Encode()}]");
        Check(SettingsScreen.ValueLabel(screen.MusicSlider).Text == want.MusicVolume.ToString(System.Globalization.CultureInfo.InvariantCulture), $"{what}: music value label '{SettingsScreen.ValueLabel(screen.MusicSlider).Text}'");
    }

    private static void Pick(SettingsScreen screen, GameSettings s)
    {
        int r = screen.Resolutions.ToList().IndexOf(new Vector2I(s.Width, s.Height));
        screen.ResolutionOption.Select(r);
        screen.WindowModeOption.Select((int)s.WindowMode);
        screen.VsyncOption.Select(s.Vsync ? 0 : 1);
        screen.QualityOption.Select((int)s.Quality);
        screen.MasterSlider.Value = s.MasterVolume;
        screen.MusicSlider.Value = s.MusicVolume;
        screen.SfxSlider.Value = s.SfxVolume;
        screen.EdgePanSlider.Value = s.EdgePan;
    }

    // A child process boots Main on a corrupt file: the defaults, one settings warning, no error line.
    private void CorruptBoot()
    {
        WriteTestFile("[display]\nwidth=\nthis is not a settings file\n");
        var output = new Godot.Collections.Array();
        int exit = OS.Execute(OS.GetExecutablePath(), new[] { "--headless", "--path", ProjectSettings.GlobalizePath("res://"), "res://tests/SettingsTest.tscn", "--", "--boot-only", TestPath }, output, readStderr: true);
        var lines = new List<string>();
        foreach (Variant chunk in output) lines.AddRange(chunk.AsString().Split('\n').Select(l => l.TrimEnd('\r')));
        int warnings = lines.Count(l => l.StartsWith("WARNING: Settings:", StringComparison.Ordinal));
        var errors = lines.Where(l => l.Contains("ERROR", StringComparison.Ordinal) || l.Contains("Unhandled", StringComparison.Ordinal)).ToList();
        Check(exit == 0 && warnings == 1 && errors.Count == 0 && lines.Contains("SETTINGS BOOT defaults True"),
            $"corrupt boot: exit {exit}, {warnings} settings warnings, errors [{string.Join(" | ", errors)}], lines [{string.Join(" | ", lines.Where(l => l.Contains("SETTINGS") || l.Contains("Settings")))}]");
    }

    private async Task BootOnly(string path)
    {
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.UserArgs = Array.Empty<string>();
        main.SettingsPath = path;
        AddChild(main);
        await Frames(2);
        GD.Print($"SETTINGS BOOT defaults {main.Settings.SameAs(new GameSettings())}");
        SceneExit.Quit(this, 0);
    }

    // ---- helpers ----

    private static void WriteTestFile(string text)
    {
        string full = ProjectSettings.GlobalizePath(TestPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    private static string FileText()
    {
        string full = ProjectSettings.GlobalizePath(TestPath);
        return File.Exists(full) ? File.ReadAllText(full) : "no file";
    }

    private static string[] Items(OptionButton list) => Enumerable.Range(0, list.ItemCount).Select(list.GetItemText).ToArray();

    private static List<string> Labels(Node root) => root.FindChildren("*", "Label", true, false).Select(n => ((Label)n).Text).ToList();

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

    private async Task Type(Key key)
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
        if (_shots == null || DisplayServer.GetName() == "headless") return;
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
