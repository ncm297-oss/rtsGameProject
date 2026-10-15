using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace Rts.Game;

/// <summary>
/// The settings screen (M6-V6, docs/02 "Game flow" 7), from the main menu and the pause menu: resolution, window mode,
/// VSync, quality, master / music / SFX volume, edge pan speed, confine cursor to window (M6-V7); Apply, Defaults, Back. Text from <c>ui.json</c> <c>settings</c>.
/// </summary>
/// <remarks>
/// The controls hold the picks until a button commits them; the screen only raises events and <see cref="Main"/> applies
/// and saves (<see cref="Main.CommitSettings"/>). It runs while the tree is paused and draws over the pause menu
/// (<see cref="DrawLayer"/>); its backdrop takes every click. Keybinds are a later slice. View state only.
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

    /// <summary>Raised by Apply.</summary>
    public event Action? ApplyPressed;

    /// <summary>Raised by Defaults.</summary>
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

        var grid = new GridContainer { Name = "Grid", Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddThemeConstantOverride("v_separation", 10);
        column.AddChild(grid);
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
