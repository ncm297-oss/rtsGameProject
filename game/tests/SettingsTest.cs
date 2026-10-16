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
/// A child boot on a corrupt file gets the defaults with one warning and no error line. M6-V7: the confine key (default on,
/// round trip, missing = on without a problem, junk = one problem), its check box, and the shell's confine state in a match.
/// M6-V8: the <c>[keys]</c> rows (a round trip, a missing section reads clean, a junk, a reserved and a clashing key each one
/// problem and the default, a command sharing a lower grid key allowed); the Keybinds tab (every action's <c>keys</c> name
/// and key, click-to-rebind A to K, a clash and a reserved key refused with their <c>ui.json</c> lines, Esc stops the wait),
/// then in the running match K arms attack-move and orders it while A does nothing, the card's hint reads K; Default keys
/// puts A back (file and match).
/// </summary>
/// <remarks>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/SettingsTest.tscn</c>; prints "SETTINGS TEST PASS" and exits 0,
/// else "SETTINGS TEST FAIL: ..." lines and exits 1. Headless has no window, so the window mode, size and VSync calls are skipped
/// and asserted through the stored settings and the file. Windowed with <c>-- --shots &lt;dir&gt;</c> it saves
/// <c>settings-menu.png</c>, <c>settings-pause.png</c> and <c>settings-keybinds.png</c> and cycles the window mode borderless and back under the paused match.
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
        // BUG-0681: an engine window flag keeps the engine's window at boot; one after "--" is a user arg, not the engine's.
        foreach (string flag in SettingsApplier.EngineWindowFlags)
            Check(SettingsApplier.HasEngineWindowFlag(new[] { "--path", "game", flag, "1600x900" }), $"engine flag {flag} not seen");
        Check(!SettingsApplier.HasEngineWindowFlag(new[] { "--path", "game", "--headless" }) && !SettingsApplier.HasEngineWindowFlag(new[] { "--path", "game", "--", "--resolution" })
            && SettingsApplier.EngineWindowFlags.Contains("--resolution") && SettingsApplier.EngineWindowFlags.Contains("-f"), "engine window flag rows");
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
            // BUG-0680: a whole number past 18 digits is refused by the line shape (Godot would log an ERROR parsing it); 18 digits
            // parse (out of range); a long run with a fraction or an exponent is a float (out of range, or a float for an int).
            ("an 18-digit int", "[display]\nwidth=999999999999999999\n", 9, s => s.SameAs(defaults)),
            ("a 19-digit int", "[display]\nwidth=9999999999999999999\n", 1, s => s.SameAs(defaults)),
            ("a 64-digit int", "[audio]\nmaster=-" + new string('9', 64) + "\n", 1, s => s.SameAs(defaults)),
            ("20 digits and a fraction", "[camera]\nedge_pan=99999999999999999999.5\n", 9, s => s.SameAs(defaults)),
            ("20 digits and an exponent", "[camera]\nedge_pan=99999999999999999999e2\n", 9, s => s.SameAs(defaults)),
            ("extra keys", new GameSettings { MusicVolume = 7 }.Encode() + "\n[keys]\nfuture=\"x\"\n[display]\nextra=3\n", 0, s => s.MusicVolume == 7),
            // M6-V7: camera.confine_cursor. Off reads off; a file from before the key reads clean with the default (on); a value
            // that isn't true / false is one problem and the default.
            ("confine off", new GameSettings { ConfineCursor = false }.Encode(), 0, s => !s.ConfineCursor),
            ("confine missing (an M6-V6 file)", WithoutConfine(new GameSettings { MusicVolume = 9 }.Encode()), 0, s => s.ConfineCursor && s.MusicVolume == 9),
            ("confine a number", WithoutConfine(new GameSettings().Encode()) + "\nconfine_cursor=0\n", 1, s => s.ConfineCursor),
            ("confine a string", WithoutConfine(new GameSettings().Encode()) + "\nconfine_cursor=\"off\"\n", 1, s => s.ConfineCursor),
            // M6-V8: [keys]. A file without the section (M6-V7's) reads clean with the defaults; a junk, a reserved or a clashing
            // key is one problem and that action's default; a command may share a lower grid key (D is the eighth grid key).
            ("keys missing (an M6-V7 file)", WithoutKeys(new GameSettings { MusicVolume = 8 }.Encode()), 0, s => s.MusicVolume == 8 && s.Keys.SameAs(new KeyMap())),
            ("a junk key", NoKeys + "\n[keys]\norder_stop=\"Blorp\"\n", 1, s => s.Keys.Get("order_stop") == Key.S),
            ("a number for a key", NoKeys + "\n[keys]\norder_stop=5\n", 1, s => s.Keys.Get("order_stop") == Key.S),
            ("a reserved key", NoKeys + "\n[keys]\norder_stop=\"Escape\"\n", 1, s => s.Keys.Get("order_stop") == Key.S),
            ("a key an earlier action holds", NoKeys + "\n[keys]\norder_stop=\"A\"\n", 1, s => s.Keys.Get("order_stop") == Key.S && s.Keys.Get("order_attack_move") == Key.A),
            ("a key a later action holds", NoKeys + "\n[keys]\norder_attack_move=\"S\"\n", 1, s => s.Keys.Get("order_attack_move") == Key.A),
            ("a command on a top-row grid key", NoKeys + "\n[keys]\ngroup_1=\"Q\"\n", 1, s => s.Keys.Get("group_1") == Key.Key1),
            ("a command on a lower grid key", NoKeys + "\n[keys]\norder_stop=\"D\"\n", 0, s => s.Keys.Get("order_stop") == Key.D),
            ("two keys swapped", NoKeys + "\n[keys]\norder_stop=\"H\"\norder_hold=\"S\"\n", 2, s => s.Keys.SameAs(new KeyMap())),
        };
        Check(defaults.ConfineCursor && new GameSettings { ConfineCursor = false }.Encode().Contains("confine_cursor=false"), $"confine default on, written: [{defaults.Encode()}]");
        foreach ((string what, string? text, int want, Func<GameSettings, bool> ok) in rows)
        {
            DeleteTestFile();
            if (text != null) WriteTestFile(text);
            var problems = new List<string>();
            GameSettings s = GameSettings.Read(TestPath, problems, out bool found);
            Check(found == (text != null) && problems.Count == want && ok(s), $"read '{what}': {problems.Count} problems (want {want}): [{string.Join("; ", problems)}]");
        }
        var picks = new GameSettings { Width = 1920, Height = 1200, WindowMode = SettingsWindowMode.Fullscreen, Vsync = false, Quality = SettingsQuality.Low, MasterVolume = 0, MusicVolume = 55, SfxVolume = 1, EdgePan = 0.5f, ConfineCursor = false };
        // M6-V8: the keys round-trip too (a rebind, a digit, a punctuation key, a grid key).
        Check(picks.Keys.TrySet(Array.IndexOf(KeyMap.Actions, "order_attack_move"), Key.K) == KeyMap.Ok
            && picks.Keys.TrySet(Array.IndexOf(KeyMap.Actions, "group_1"), Key.Key0) == KeyMap.Ok
            && picks.Keys.TrySet(Array.IndexOf(KeyMap.Actions, "subgroup_next"), Key.Backslash) == KeyMap.Ok
            && picks.Keys.TrySet(KeyMap.FirstCard + 14, Key.N) == KeyMap.Ok, "the round trip's rebinds were refused");
        Check(picks.Save(TestPath), "save failed");
        GameSettings back = GameSettings.Read(TestPath, new List<string>(), out _);
        Check(back.SameAs(picks), $"round trip: wrote [{picks.Encode()}], read [{back.Encode()}]");
        // A path that can't be written (its folder is a file) is one warning and false, never an error or a throw.
        Check(!picks.Save(TestPath + "/inner.cfg"), "a save under a file's name claimed success");
        DeleteTestFile();
    }

    // The encoded file without its confine_cursor line and without the [keys] section (M6-V8; written last), so the camera
    // section is last again and a line appended after it lands there.
    private static string WithoutConfine(string text) =>
        string.Join("\n", WithoutKeys(text).Split('\n').Where(l => !l.StartsWith("confine_cursor", StringComparison.Ordinal)));

    // The encoded file without its [keys] section: a file written before M6-V8.
    private static string WithoutKeys(string text)
    {
        int at = text.IndexOf("[keys]", StringComparison.Ordinal);
        return at < 0 ? text : text.Substring(0, at);
    }

    // A default file without its [keys] section, for the key rows to add their own.
    private static string NoKeys => WithoutKeys(new GameSettings().Encode());

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
            SettingsText.MasterVolume, SettingsText.MusicVolume, SettingsText.SfxVolume, SettingsText.EdgePanSpeed, SettingsText.ConfineCursor })
            Check(Labels(screen).Contains(_ui.Settings(k)), $"settings has no label '{_ui.Settings(k)}'");
        Check(screen.ApplyButton.Text == _ui.Settings(SettingsText.Apply) && screen.DefaultsButton.Text == _ui.Settings(SettingsText.Defaults) && screen.BackButton.Text == _ui.Settings(SettingsText.Back), "button text");
        Check(Items(screen.WindowModeOption).SequenceEqual(new[] { SettingsText.WindowWindowed, SettingsText.WindowBorderless, SettingsText.WindowFullscreen }.Select(_ui.Settings))
            && Items(screen.VsyncOption).SequenceEqual(new[] { SettingsText.VsyncOn, SettingsText.VsyncOff }.Select(_ui.Settings))
            && Items(screen.QualityOption).SequenceEqual(new[] { SettingsText.QualityLow, SettingsText.QualityMedium, SettingsText.QualityHigh }.Select(_ui.Settings))
            && Items(screen.ResolutionOption).SequenceEqual(screen.Resolutions.Select(r => _ui.ResolutionItem(r.X, r.Y))), "list items not from ui.json");
        var allowed = new HashSet<string>(Enum.GetValues<SettingsText>().Select(_ui.Settings)) { "" };
        // M6-V8: the Keybinds tab's words are ui.json keys texts and action names; the rest are key names.
        allowed.UnionWith(Enum.GetValues<KeysText>().Select(_ui.Keys));
        allowed.UnionWith(Enumerable.Range(0, KeyMap.Actions.Length).Select(i => KeyMap.ActionName(_ui, i)));
        allowed.UnionWith(KeyMap.Defaults().Select(KeyMap.KeyName));
        foreach (string name in Enumerable.Range(0, KeyMap.Actions.Length).Select(i => KeyMap.ActionName(_ui, i)))
            Check(Labels(screen).Contains(name), $"the Keybinds tab has no row '{name}'");
        Check(screen.Tabs.GetTabCount() == 2 && screen.Tabs.GetTabTitle(0) == _ui.Keys(KeysText.TabGeneral) && screen.Tabs.GetTabTitle(1) == _ui.Keys(KeysText.TabKeybinds)
            && screen.Tabs.CurrentTab == 0 && screen.KeysDefaultsButton.Text == _ui.Keys(KeysText.Defaults),
            $"tabs: {screen.Tabs.GetTabCount()} '{screen.Tabs.GetTabTitle(0)}' '{screen.Tabs.GetTabTitle(1)}', current {screen.Tabs.CurrentTab}");
        for (int i = 0; i < KeyMap.Actions.Length; i++)
            Check(screen.KeyButton(i).Text == KeyMap.KeyName(KeyMap.Defaults()[i]) && KeyMap.Defaults()[i] != Key.None, $"key button {KeyMap.Actions[i]} reads '{screen.KeyButton(i).Text}'");
        // Every button of the screen sits inside the window (the tab bar must not push Back off a 648 px window).
        foreach (Button b in new[] { screen.BackButton, screen.ApplyButton, screen.DefaultsButton })
            Check(b.GetGlobalRect().End.Y <= GetViewport().GetVisibleRect().Size.Y, $"{b.Name} ends at y {b.GetGlobalRect().End.Y}");
        foreach (Node n in screen.FindChildren("*", "", true, false))
        {
            string? text = n switch { Label l => l.Text, OptionButton => null, Button b => b.Text, _ => null };
            if (text != null) Check(allowed.Contains(text) || double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _),
                $"settings shows '{text}' ({n.Name}), not from ui.json and not a number");
        }
        ShowsSettings(screen, new GameSettings(), "first open");
        await Shot("settings-menu");

        // Every field changed, then Back.
        var picks = new GameSettings { Width = 1920, Height = 1080, WindowMode = SettingsWindowMode.Borderless, Vsync = false, Quality = SettingsQuality.High, MasterVolume = 80, MusicVolume = 30, SfxVolume = 0, EdgePan = 1.5f, ConfineCursor = false };
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
        // M6-V7: the setting is on (Apply's picks keep the default) but headless has no window to confine the cursor to.
        await Frames(2);
        Check(_main.Settings.ConfineCursor && _main.ConfineAllowed == SettingsApplier.HasWindow && (SettingsApplier.HasWindow || !_main.CursorConfined),
            $"confine in a match: setting {_main.Settings.ConfineCursor}, allowed {_main.ConfineAllowed}, confined {_main.CursorConfined}, window {SettingsApplier.HasWindow}");
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
        fromPause.ConfineCursor = false;
        Pick(screen, fromPause);
        await KeyTab(screen, fromPause);
        await Type(Key.Escape);
        Check(!screen.Visible && pause.Visible && GetTree().Paused && _main.Paused && _main.CurrentMatch == match, $"Esc on settings over pause: screen {screen.Visible}, pause {pause.Visible}, tree paused {GetTree().Paused}");
        Check(match.GetNode<RtsCamera>("RtsCamera").EdgePanSpeed == 0.5f, $"the running match's edge pan {match.GetNode<RtsCamera>("RtsCamera").EdgePanSpeed}, want 0.5");
        Committed(fromPause, "Esc over pause");
        await Type(Key.Escape);
        Check(!GetTree().Paused && !_main.Paused && !pause.Visible, "the second Esc did not resume");
        // The setting turned off from the pause menu applies live: never confined, window or not.
        await Frames(2);
        Check(!_main.Settings.ConfineCursor && !_main.ConfineAllowed && !_main.CursorConfined, $"confine off live: allowed {_main.ConfineAllowed}, confined {_main.CursorConfined}");
        await ReboundKeys(match, screen, pause);
        RemoveChild(_main);
        _main.QueueFree();
        await Frames(2);
    }

    // M6-V8: the Keybinds tab over the paused match: A rebound to K; a clash, a top-row grid key and a reserved key refused
    // with their lines; Esc stops a wait without leaving the screen. `want` takes the rebind.
    private async Task KeyTab(SettingsScreen screen, GameSettings want)
    {
        int attack = Array.IndexOf(KeyMap.Actions, "order_attack_move"), stop = Array.IndexOf(KeyMap.Actions, "order_stop");
        screen.Tabs.CurrentTab = 1;
        await Frames(1);
        await Click(screen.KeyButton(attack));
        Check(screen.Listening && screen.KeyButton(attack).Text == _ui.Keys(KeysText.PressKey), $"waiting: {screen.Listening}, '{screen.KeyButton(attack).Text}'");
        await Type(Key.K);
        Check(!screen.Listening && screen.KeyButton(attack).Text == "K" && screen.KeyStatus.Text == "" && screen.Visible, $"rebound: '{screen.KeyButton(attack).Text}', status '{screen.KeyStatus.Text}'");
        await Shot("settings-keybinds");
        foreach ((Key key, string line) in new[]
        {
            (Key.K, _ui.KeysFormat(KeysText.Conflict, "K", KeyMap.ActionName(_ui, attack))),
            (Key.Q, _ui.KeysFormat(KeysText.Conflict, "Q", KeyMap.ActionName(_ui, KeyMap.FirstCard))),
            (Key.Left, _ui.KeysFormat(KeysText.Reserved, KeyMap.KeyName(Key.Left))),
        })
        {
            await Click(screen.KeyButton(stop));
            await Type(key);
            Check(screen.KeyButton(stop).Text == "S" && screen.KeyStatus.Text == line && screen.Visible, $"{key} for Stop: '{screen.KeyButton(stop).Text}', status '{screen.KeyStatus.Text}', want '{line}'");
        }
        await Click(screen.KeyButton(stop));
        await Type(Key.Escape);
        Check(screen.Visible && !screen.Listening && screen.KeyButton(stop).Text == "S" && GetTree().Paused, $"Esc while waiting: screen {screen.Visible}, waiting {screen.Listening}");
        screen.Tabs.CurrentTab = 0;
        want.Keys.TrySet(attack, Key.K);
    }

    // M6-V8: in the running match K is attack-move and A nothing; the card's hint reads K. Then Default keys puts A back.
    private async Task ReboundKeys(Match match, SettingsScreen screen, PauseMenu pause)
    {
        var sel = match.GetNode<SelectionController>("SelectionController");
        var card = match.GetNode<CommandCard>("Hud/CommandCard");
        Rts.Sim.Entities.UnitStore units = match.Simulation!.World.Units;
        int own = -1;
        for (int i = 0; i < units.Capacity && own < 0; i++) if (units.Alive[i] && units.Owner[i] == SelectionController.LocalPlayer) own = i;
        if (!Check(own >= 0 && sel.SelectOnly(new Rts.Sim.Entities.EntityHandle(own, units.Generation[own])), "no own unit to order")) return;
        card.Sync();
        Check(card.HintAt(5).Text == "K" && card.ActionAt(5) == CardCommand.AttackMove, $"the card's attack hint reads '{card.HintAt(5).Text}'");
        int before = sel.IssuedCount(Rts.Sim.Commands.CommandKind.AttackMove);
        await Type(Key.A);
        Check(!sel.Targeting, "A still arms attack-move after the rebind");
        await Type(Key.K);
        Check(sel.Targeting && sel.TargetKind == Rts.Sim.Commands.CommandKind.AttackMove, $"K: targeting {sel.Targeting} {sel.TargetKind}");
        Vector2 centre = GetViewport().GetVisibleRect().Size / 2f;
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = centre, GlobalPosition = centre, ButtonMask = MouseButtonMask.Left });
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = centre, GlobalPosition = centre });
        await Frames(2);
        Check(!sel.Targeting && sel.IssuedCount(Rts.Sim.Commands.CommandKind.AttackMove) == before + 1, $"K + click: {sel.IssuedCount(Rts.Sim.Commands.CommandKind.AttackMove) - before} attack-moves");

        // Default keys: A again, in the file and in the match.
        await Type(Key.Escape);
        await Click(pause.SettingsButton);
        screen.Tabs.CurrentTab = 1;
        await Frames(1);
        await Click(screen.KeysDefaultsButton);
        int attack = Array.IndexOf(KeyMap.Actions, "order_attack_move");
        Check(screen.KeyButton(attack).Text == "A" && screen.Picks().Keys.SameAs(new KeyMap()), $"Default keys: '{screen.KeyButton(attack).Text}'");
        GameSettings want = _main.Settings.Clone();
        want.Keys.Reset();
        screen.Tabs.CurrentTab = 0;
        await Frames(1);
        await Click(screen.BackButton);
        Committed(want, "Default keys");
        await Type(Key.Escape);
        Check(!GetTree().Paused, "Esc did not resume after Default keys");
        sel.SelectOnly(new Rts.Sim.Entities.EntityHandle(own, units.Generation[own]));
        await Type(Key.K);
        Check(!sel.Targeting, "K still arms attack-move after Default keys");
        await Type(Key.A);
        Check(sel.Targeting && sel.TargetKind == Rts.Sim.Commands.CommandKind.AttackMove, "A does not arm attack-move after Default keys");
        sel.CancelTargeting();
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
            && Math.Abs((float)file.GetValue("camera", "edge_pan", 0f) - want.EdgePan) < 1e-4f
            && (bool)file.GetValue("camera", "confine_cursor", !want.ConfineCursor) == want.ConfineCursor
            && KeyMap.Actions.All(a => (string)file.GetValue("keys", a, "") == OS.GetKeycodeString(want.Keys.Get(a))), $"{what}: the file holds [{FileText()}], want [{want.Encode()}]");
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
        screen.ConfineCheck.ButtonPressed = s.ConfineCursor;
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
