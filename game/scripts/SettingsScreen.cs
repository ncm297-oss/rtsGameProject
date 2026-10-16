using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace Rts.Game;

/// <summary>
/// The settings screen (M6-V6, docs/02 "Game flow" 7), from the main menu and the pause menu: resolution, window mode,
/// VSync, quality, master / music / SFX volume, edge pan speed, confine cursor to window (M6-V7) on the General tab, the
/// rebindable keys on the Keybinds tab (M6-V8); Apply, Defaults, Back. Text from <c>ui.json</c> <c>settings</c> and <c>keys</c>.
/// </summary>
/// <remarks>
/// The controls hold the picks until a button commits them; the screen only raises events and <see cref="Main"/> applies
/// and saves (<see cref="Main.CommitSettings"/>). It runs while the tree is paused and draws over the pause menu
/// (<see cref="DrawLayer"/>); its backdrop takes every click. Keybinds tab: a row per <see cref="KeyMap.Actions"/> entry (its
/// <c>keys</c> name and a button showing its key); a click on the button waits for a key (<see cref="Listening"/>), the next
/// key press binds it unless <see cref="KeyMap.TrySet"/> refuses (the refusal's <c>keys.conflict</c> / <c>keys.reserved</c>
/// line shows under the rows), Esc stops waiting; Default keys puts the tab's keys back. Like every field, the keys apply
/// and save only with Apply, Back or Esc. View state only.
/// </remarks>
public partial class SettingsScreen : CanvasLayer
{
    /// <summary>Drawn over the pause menu (<see cref="PauseMenu.DrawLayer"/>).</summary>
    public const int DrawLayer = PauseMenu.DrawLayer + 1;

    /// <summary>The resolution list's sizes besides the window's own and the stored one: common 16:9 and 16:10 sizes.</summary>
    public static readonly Vector2I[] CommonResolutions =
    {
        new(1280, 720), new(1280, 800), new(1366, 768), new(1440, 900), new(1600, 900), new(1680, 1050),
        new(1920, 1080), new(1920, 1200), new(2560, 1440), new(2560, 1600), new(3840, 2160),
    };

    /// <summary>The edge pan slider's step.</summary>
    public const float EdgePanStep = 0.05f;

    private static readonly SettingsText[] WindowKeys = { SettingsText.WindowWindowed, SettingsText.WindowBorderless, SettingsText.WindowFullscreen };
    private static readonly SettingsText[] QualityKeys = { SettingsText.QualityLow, SettingsText.QualityMedium, SettingsText.QualityHigh };

    private UiText _ui = null!;
    private Vector2I[] _resolutions = Array.Empty<Vector2I>();
    private KeyMap _keys = new();
    private readonly Button[] _keyButtons = new Button[KeyMap.Actions.Length];
    private int _listening = -1;

    /// <summary>Raised by Apply.</summary>
    public event Action? ApplyPressed;

    /// <summary>Raised by Defaults (the General fields only; the keys stay as the Keybinds tab shows them, BUG-0743).</summary>
    public event Action? DefaultsPressed;

    /// <summary>Raised by Back (and Esc, through <see cref="Main"/>).</summary>
    public event Action? BackPressed;

    /// <summary>The resolution list.</summary>
    public OptionButton ResolutionOption { get; private set; } = null!;

    /// <summary>The window mode list (<see cref="SettingsWindowMode"/> order).</summary>
    public OptionButton WindowModeOption { get; private set; } = null!;

    /// <summary>The VSync list: on, off.</summary>
    public OptionButton VsyncOption { get; private set; } = null!;

    /// <summary>The quality list (<see cref="SettingsQuality"/> order).</summary>
    public OptionButton QualityOption { get; private set; } = null!;

    /// <summary>The master volume slider (0-100).</summary>
    public HSlider MasterSlider { get; private set; } = null!;

    /// <summary>The music volume slider (0-100).</summary>
    public HSlider MusicSlider { get; private set; } = null!;

    /// <summary>The sound effects volume slider (0-100).</summary>
    public HSlider SfxSlider { get; private set; } = null!;

    /// <summary>The edge pan speed slider (0.5-2).</summary>
    public HSlider EdgePanSlider { get; private set; } = null!;

    /// <summary>The "confine cursor to window" check box (M6-V7); pressed = on.</summary>
    public CheckBox ConfineCheck { get; private set; } = null!;

    /// <summary>The Apply button.</summary>
    public Button ApplyButton { get; private set; } = null!;

    /// <summary>The Defaults button.</summary>
    public Button DefaultsButton { get; private set; } = null!;

    /// <summary>The Back button.</summary>
    public Button BackButton { get; private set; } = null!;

    /// <summary>The General and Keybinds tabs (M6-V8).</summary>
    public TabContainer Tabs { get; private set; } = null!;

    /// <summary>The key button of action <paramref name="i"/> (<see cref="KeyMap.Actions"/>) on the Keybinds tab.</summary>
    public Button KeyButton(int i) => _keyButtons[i];

    /// <summary>The Keybinds tab's line for a refused key (empty otherwise).</summary>
    public Label KeyStatus { get; private set; } = null!;

    /// <summary>The Keybinds tab's Default keys button.</summary>
    public Button KeysDefaultsButton { get; private set; } = null!;

    /// <summary>True while a key button waits for a key.</summary>
    public bool Listening => _listening >= 0;

    /// <summary>The sizes the resolution list holds now, in its order.</summary>
    public IReadOnlyList<Vector2I> Resolutions => _resolutions;

    /// <summary>Builds the screen with <paramref name="ui"/>'s text; hidden until <see cref="Main"/> shows it.</summary>
    public void Build(UiText ui)
    {
        _ui = ui;
        Layer = DrawLayer;
        ProcessMode = ProcessModeEnum.Always;
        Visible = false;
        AddChild(ShellUi.Backdrop(new Color(0.09f, 0.08f, 0.07f)));
        VBoxContainer column = ShellUi.CentredColumn(this, 18);
        column.AddChild(ShellUi.Heading(ui.Settings(SettingsText.Title)));

        Tabs = new TabContainer { Name = "Tabs" };
        column.AddChild(Tabs);
        var grid = new GridContainer { Name = "Grid", Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddThemeConstantOverride("v_separation", 8);
        Tabs.AddChild(grid);
        Tabs.SetTabTitle(0, ui.Keys(KeysText.TabGeneral));
        ResolutionOption = Row(grid, SettingsText.Resolution, List("Resolution"));
        WindowModeOption = Row(grid, SettingsText.WindowMode, List("WindowMode", WindowKeys));
        VsyncOption = Row(grid, SettingsText.Vsync, List("Vsync", new[] { SettingsText.VsyncOn, SettingsText.VsyncOff }));
        QualityOption = Row(grid, SettingsText.Quality, List("Quality", QualityKeys));
        MasterSlider = SliderRow(grid, SettingsText.MasterVolume, "Master", 0, GameSettings.MaxVolume, 1);
        MusicSlider = SliderRow(grid, SettingsText.MusicVolume, "Music", 0, GameSettings.MaxVolume, 1);
        SfxSlider = SliderRow(grid, SettingsText.SfxVolume, "Sfx", 0, GameSettings.MaxVolume, 1);
        EdgePanSlider = SliderRow(grid, SettingsText.EdgePanSpeed, "EdgePan", GameSettings.MinEdgePan, GameSettings.MaxEdgePan, EdgePanStep);
        // No words on the box itself: its label is the row's (ui.json), so the text stays data-only.
        ConfineCheck = Row(grid, SettingsText.ConfineCursor, new CheckBox { Name = "ConfineCursor", CustomMinimumSize = new Vector2(40f, 40f) });
        BuildKeys(ui);

        var buttons = new HBoxContainer { Name = "Buttons", Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 24);
        column.AddChild(buttons);
        BackButton = ShellUi.Button("Back", ui.Settings(SettingsText.Back), 180f);
        BackButton.Pressed += () => BackPressed?.Invoke();
        buttons.AddChild(BackButton);
        DefaultsButton = ShellUi.Button("Defaults", ui.Settings(SettingsText.Defaults), 180f);
        DefaultsButton.Pressed += () => DefaultsPressed?.Invoke();
        buttons.AddChild(DefaultsButton);
        ApplyButton = ShellUi.Button("Apply", ui.Settings(SettingsText.Apply), 180f);
        ApplyButton.Pressed += () => ApplyPressed?.Invoke();
        buttons.AddChild(ApplyButton);
    }

    /// <summary>Sets every control to <paramref name="s"/>; the resolution list is the common sizes plus <paramref name="window"/> (the window's size now) and the stored size.</summary>
    public void ShowSettings(GameSettings s, Vector2I window)
    {
        _resolutions = ResolutionList(window, new Vector2I(s.Width, s.Height));
        ResolutionOption.Clear();
        foreach (Vector2I r in _resolutions) ResolutionOption.AddItem(_ui.ResolutionItem(r.X, r.Y));
        ResolutionOption.Select(Array.IndexOf(_resolutions, new Vector2I(s.Width, s.Height)));
        WindowModeOption.Select((int)s.WindowMode);
        VsyncOption.Select(s.Vsync ? 0 : 1);
        QualityOption.Select((int)s.Quality);
        MasterSlider.Value = s.MasterVolume;
        MusicSlider.Value = s.MusicVolume;
        SfxSlider.Value = s.SfxVolume;
        EdgePanSlider.Value = s.EdgePan;
        ConfineCheck.ButtonPressed = s.ConfineCursor;
        _keys = s.Keys.Clone();
        _listening = -1;
        KeyStatus.Text = "";
        ShowKeys();
    }

    /// <summary>The picks on the screen now.</summary>
    public GameSettings Picks()
    {
        Vector2I r = _resolutions.Length > 0 ? _resolutions[Math.Max(0, ResolutionOption.Selected)] : new Vector2I(GameSettings.DefaultWidth, GameSettings.DefaultHeight);
        return new GameSettings
        {
            Width = r.X,
            Height = r.Y,
            WindowMode = (SettingsWindowMode)Math.Max(0, WindowModeOption.Selected),
            Vsync = VsyncOption.Selected != 1,
            Quality = (SettingsQuality)Math.Max(0, QualityOption.Selected),
            MasterVolume = (int)Math.Round(MasterSlider.Value),
            MusicVolume = (int)Math.Round(MusicSlider.Value),
            SfxVolume = (int)Math.Round(SfxSlider.Value),
            // The slider's steps are 0.05, so the pick is a round number in the file.
            EdgePan = (float)Math.Round(EdgePanSlider.Value, 2),
            ConfineCursor = ConfineCheck.ButtonPressed,
            Keys = _keys.Clone(),
        };
    }

    /// <summary>The value label beside a slider (a number, no words).</summary>
    public static Label ValueLabel(HSlider slider) => slider.GetParent().GetChild<Label>(slider.GetIndex() + 1);

    /// <summary>The common sizes plus <paramref name="window"/> and <paramref name="stored"/>, each once, ordered by width then height.</summary>
    public static Vector2I[] ResolutionList(Vector2I window, Vector2I stored)
    {
        var list = new List<Vector2I>(CommonResolutions);
        foreach (Vector2I extra in new[] { window, stored })
            if (extra.X >= GameSettings.MinWidth && extra.Y >= GameSettings.MinHeight && !list.Contains(extra)) list.Add(extra);
        list.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
        return list.ToArray();
    }

    // The Keybinds tab: a scrolled two-column list (action name, key button), the refusal line and Default keys.
    private void BuildKeys(UiText ui)
    {
        var page = new VBoxContainer { Name = "Keybinds" };
        page.AddThemeConstantOverride("separation", 8);
        Tabs.AddChild(page);
        Tabs.SetTabTitle(1, ui.Keys(KeysText.TabKeybinds));
        // Default keys on top, beside the refusal line, so it isn't stacked over the screen's own Defaults (BUG-0743).
        var header = new HBoxContainer { Name = "Header" };
        header.AddThemeConstantOverride("separation", 16);
        page.AddChild(header);
        KeysDefaultsButton = ShellUi.Button("KeysDefaults", ui.Keys(KeysText.Defaults), 220f);
        KeysDefaultsButton.Pressed += () =>
        {
            _keys.Reset();
            _listening = -1;
            KeyStatus.Text = "";
            ShowKeys();
        };
        header.AddChild(KeysDefaultsButton);
        KeyStatus = ShellUi.Text("", 18);
        KeyStatus.Name = "KeyStatus";
        KeyStatus.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        KeyStatus.CustomMinimumSize = new Vector2(400f, 0f);
        KeyStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        header.AddChild(KeyStatus);
        var scroll = new ScrollContainer { Name = "Scroll", CustomMinimumSize = new Vector2(640f, 380f), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        page.AddChild(scroll);
        var rows = new GridContainer { Name = "Rows", Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("h_separation", 16);
        rows.AddThemeConstantOverride("v_separation", 4);
        scroll.AddChild(rows);
        // The key buttons look like buttons on the dark screen, not only when hovered (BUG-0743).
        StyleBoxFlat normal = KeyStyle(new Color(0.22f, 0.2f, 0.17f), new Color(0.55f, 0.48f, 0.36f));
        StyleBoxFlat hover = KeyStyle(new Color(0.32f, 0.29f, 0.23f), new Color(0.85f, 0.75f, 0.5f));
        for (int i = 0; i < KeyMap.Actions.Length; i++)
        {
            int index = i;
            Label name = ShellUi.Text(KeyMap.ActionName(ui, i), 18);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            rows.AddChild(name);
            var key = new Button { Name = $"Key_{KeyMap.Actions[i]}", CustomMinimumSize = new Vector2(180f, 32f), FocusMode = Control.FocusModeEnum.None };
            key.AddThemeStyleboxOverride("normal", normal);
            key.AddThemeStyleboxOverride("hover", hover);
            key.AddThemeStyleboxOverride("pressed", hover);
            key.Pressed += () => Listen(index);
            rows.AddChild(key);
            _keyButtons[i] = key;
        }
    }

    private static StyleBoxFlat KeyStyle(Color fill, Color border)
    {
        var style = new StyleBoxFlat { BgColor = fill, BorderColor = border };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(4);
        style.SetContentMarginAll(4);
        return style;
    }

    private void Listen(int i)
    {
        _listening = i;
        KeyStatus.Text = "";
        ShowKeys();
    }

    // Every key button's text: the bound key's name, or keys.press_key on the one waiting.
    private void ShowKeys()
    {
        for (int i = 0; i < _keyButtons.Length; i++)
            _keyButtons[i].Text = i == _listening ? _ui.Keys(KeysText.PressKey) : KeyMap.KeyName(_keys[i]);
    }

    /// <summary>While a key button waits, the next key press is its key (Esc stops waiting); the press goes no further.</summary>
    public override void _Input(InputEvent e)
    {
        if (_listening < 0 || !Visible || e is not InputEventKey k || !k.Pressed || k.Echo) return;
        GetViewport().SetInputAsHandled();
        Key key = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
        int action = _listening;
        _listening = -1;
        if (key != Key.Escape)
        {
            int result = _keys.TrySet(action, key);
            KeyStatus.Text = result == KeyMap.Ok ? ""
                : result == KeyMap.IsReserved ? _ui.KeysFormat(KeysText.Reserved, KeyMap.KeyName(key))
                : _ui.KeysFormat(KeysText.Conflict, KeyMap.KeyName(key), KeyMap.ActionName(_ui, result));
        }
        ShowKeys();
    }

    private T Row<T>(GridContainer grid, SettingsText label, T control) where T : Control
    {
        grid.AddChild(ShellUi.Text(_ui.Settings(label)));
        grid.AddChild(control);
        grid.AddChild(new Control());
        return control;
    }

    private HSlider SliderRow(GridContainer grid, SettingsText label, string name, double min, double max, double step)
    {
        grid.AddChild(ShellUi.Text(_ui.Settings(label)));
        var slider = new HSlider
        {
            Name = name, MinValue = min, MaxValue = max, Step = step, CustomMinimumSize = new Vector2(280f, 40f),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        grid.AddChild(slider);
        Label value = ShellUi.Text("");
        value.Name = name + "Value";
        value.CustomMinimumSize = new Vector2(64f, 0f);
        grid.AddChild(value);
        bool fraction = step < 1;
        slider.ValueChanged += v => value.Text = v.ToString(fraction ? "0.00" : "0", CultureInfo.InvariantCulture);
        value.Text = slider.Value.ToString(fraction ? "0.00" : "0", CultureInfo.InvariantCulture);
        return slider;
    }

    private OptionButton List(string name, SettingsText[]? items = null)
    {
        var option = new OptionButton { Name = name, CustomMinimumSize = new Vector2(280f, 40f) };
        option.AddThemeFontSizeOverride("font_size", 20);
        if (items != null) foreach (SettingsText key in items) option.AddItem(_ui.Settings(key));
        return option;
    }
}
