using System;
using Godot;

namespace Rts.Game;

/// <summary>The pause menu over a match (M6-V1, docs/02 "Game flow" 3): Resume, Save (M6-V14), Settings (M6-V6), Stats (M6-V4, once the match-over banner has shown) and Quit to menu. Text from <c>ui.json</c> <c>pause</c> (Stats: <c>match_over.stats</c>; the save lines: <c>save</c>).</summary>
/// <remarks>
/// It runs while the tree is paused (<see cref="Node.ProcessModeEnum.Always"/>); its backdrop takes every click, so nothing
/// under it gets one. It only raises events and shows what it is told; <see cref="Main"/> pauses, resumes, saves and ends
/// the match. M6-V14: under the buttons, <see cref="RecordNote"/> (<c>save.not_recorded</c>, a loaded match) and
/// <see cref="SaveStatus"/> (the last Save's result: <c>save.saved</c> or the refusal's <c>save.*</c> text).
/// </remarks>
public partial class PauseMenu : CanvasLayer
{
    /// <summary>Drawn over the HUD (layer 1) and the debug overlay.</summary>
    public const int DrawLayer = 20;

    /// <summary>Raised by the Resume button.</summary>
    public event Action? ResumePressed;

    /// <summary>Raised by the Save button (M6-V14).</summary>
    public event Action? SavePressed;

    /// <summary>Raised by the Settings button.</summary>
    public event Action? SettingsPressed;

    /// <summary>Raised by the Quit to menu button.</summary>
    public event Action? QuitToMenuPressed;

    /// <summary>Raised by the Stats button (M6-V4).</summary>
    public event Action? StatsPressed;

    /// <summary>The Stats button; shown by <see cref="Main"/> only once the match's banner has shown.</summary>
    public Button StatsButton { get; private set; } = null!;

    /// <summary>The Resume button.</summary>
    public Button ResumeButton { get; private set; } = null!;

    /// <summary>The Save button (M6-V14); shown by <see cref="Main"/> for a live or loaded match with a save folder, never for a replay.</summary>
    public Button SaveButton { get; private set; } = null!;

    /// <summary>The Settings button.</summary>
    public Button SettingsButton { get; private set; } = null!;

    /// <summary>The Quit to menu button.</summary>
    public Button QuitToMenuButton { get; private set; } = null!;

    /// <summary>The line under the buttons with the last Save's result (M6-V14); empty when the menu opens.</summary>
    public Label SaveStatus { get; private set; } = null!;

    /// <summary>The note that a loaded match is not recorded (<c>save.not_recorded</c>, M6-V14); hidden for a match that records.</summary>
    public Label RecordNote { get; private set; } = null!;

    /// <summary>
    /// The menu's own opaque panel behind the heading, the buttons and the save lines (BUG-1002 item 3, M6-VH4): at 1152 x 648
    /// the lines reach down to the HUD's command card, which the dim backdrop alone left readable through them.
    /// </summary>
    public PanelContainer Panel { get; private set; } = null!;

    /// <summary>The panel's fill (opaque).</summary>
    public static readonly Color PanelColor = new(0.09f, 0.08f, 0.07f, 1f);

    /// <summary>Builds the screen's controls with <paramref name="ui"/>'s text; hidden until <see cref="Main"/> shows it.</summary>
    public void Build(UiText ui)
    {
        Layer = DrawLayer;
        ProcessMode = ProcessModeEnum.Always;
        Visible = false;
        AddChild(ShellUi.Backdrop(new Color(0f, 0f, 0f, 0.55f)));
        var centre = new CenterContainer { Name = "Centre", MouseFilter = Control.MouseFilterEnum.Ignore };
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(centre);
        Panel = new PanelContainer { Name = "Panel" };
        var style = new StyleBoxFlat { BgColor = PanelColor, BorderColor = new Color(0.55f, 0.48f, 0.36f) };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(4);
        style.SetContentMarginAll(20f);
        Panel.AddThemeStyleboxOverride("panel", style);
        centre.AddChild(Panel);
        var column = new VBoxContainer { Name = "Column" };
        column.AddThemeConstantOverride("separation", 14);
        Panel.AddChild(column);
        column.AddChild(ShellUi.Heading(ui.Pause(PauseText.Title), 44));
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 16f) });
        ResumeButton = ShellUi.Button("Resume", ui.Pause(PauseText.Resume));
        ResumeButton.Pressed += () => ResumePressed?.Invoke();
        column.AddChild(ResumeButton);
        SaveButton = ShellUi.Button("Save", ui.Pause(PauseText.Save));
        SaveButton.Pressed += () => SavePressed?.Invoke();
        SaveButton.Visible = false;
        column.AddChild(SaveButton);
        SettingsButton = ShellUi.Button("Settings", ui.Pause(PauseText.Settings));
        SettingsButton.Pressed += () => SettingsPressed?.Invoke();
        column.AddChild(SettingsButton);
        StatsButton = ShellUi.Button("Stats", ui.MatchOver(MatchOverText.Stats));
        StatsButton.Pressed += () => StatsPressed?.Invoke();
        StatsButton.Visible = false;
        column.AddChild(StatsButton);
        QuitToMenuButton = ShellUi.Button("QuitToMenu", ui.Pause(PauseText.QuitToMenu));
        QuitToMenuButton.Pressed += () => QuitToMenuPressed?.Invoke();
        column.AddChild(QuitToMenuButton);
        SaveStatus = Line("SaveStatus", "", 18);
        column.AddChild(SaveStatus);
        RecordNote = Line("RecordNote", ui.Save(SaveText.NotRecorded), 16);
        RecordNote.Visible = false;
        column.AddChild(RecordNote);
    }

    // A centred line wrapped at the buttons' width, so a long refusal text never widens the column (and its buttons).
    private static Label Line(string name, string text, int size)
    {
        Label label = ShellUi.Text(text, size);
        label.Name = name;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(ShellUi.ButtonWidth, 0f);
        return label;
    }
}
