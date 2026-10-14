using System;
using Godot;

namespace Rts.Game;

/// <summary>The match-over banner (M6-V4, docs/02 "Game flow" 4): Victory, Defeat or Draw, then Stats, Main menu and Continue watching. Text from <c>ui.json</c> <c>match_over</c>.</summary>
/// <remarks>
/// It runs while the tree is paused (the shell opens it through the pause path) and its backdrop takes every click. It only
/// raises events and issues no sim command; <see cref="Main"/> pauses, resumes and ends the match.
/// </remarks>
public partial class MatchOverBanner : CanvasLayer
{
    /// <summary>Drawn over the HUD, the pause menu (20) and the settings screen (21); the stats screen draws over it.</summary>
    public const int DrawLayer = 22;

    private UiText _ui = null!;

    /// <summary>Raised by the Stats button.</summary>
    public event Action? StatsPressed;

    /// <summary>Raised by the Main menu button.</summary>
    public event Action? MenuPressed;

    /// <summary>Raised by the Continue watching button.</summary>
    public event Action? ContinuePressed;

    /// <summary>The title label (Victory, Defeat or Draw).</summary>
    public Label Title { get; private set; } = null!;

    /// <summary>The Stats button.</summary>
    public Button StatsButton { get; private set; } = null!;

    /// <summary>The Main menu button.</summary>
    public Button MenuButton { get; private set; } = null!;

    /// <summary>The Continue watching button.</summary>
    public Button ContinueButton { get; private set; } = null!;

    /// <summary>The outcome the banner shows; <see cref="MatchOutcome.None"/> before <see cref="ShowOutcome"/>.</summary>
    public MatchOutcome Shown { get; private set; }

    /// <summary>Builds the banner's controls with <paramref name="ui"/>'s text; hidden until <see cref="Main"/> shows it.</summary>
    public void Build(UiText ui)
    {
        _ui = ui;
        Layer = DrawLayer;
        ProcessMode = ProcessModeEnum.Always;
        Visible = false;
        AddChild(ShellUi.Backdrop(new Color(0f, 0f, 0f, 0.45f)));
        VBoxContainer column = ShellUi.CentredColumn(this);
        Title = ShellUi.Heading("", 64);
        column.AddChild(Title);
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 20f) });
        StatsButton = ShellUi.Button("Stats", ui.MatchOver(MatchOverText.Stats));
        StatsButton.Pressed += () => StatsPressed?.Invoke();
        column.AddChild(StatsButton);
        ContinueButton = ShellUi.Button("Continue", ui.MatchOver(MatchOverText.Continue));
        ContinueButton.Pressed += () => ContinuePressed?.Invoke();
        column.AddChild(ContinueButton);
        MenuButton = ShellUi.Button("Menu", ui.MatchOver(MatchOverText.Menu));
        MenuButton.Pressed += () => MenuPressed?.Invoke();
        column.AddChild(MenuButton);
    }

    /// <summary>Sets the title for <paramref name="outcome"/> and shows the banner.</summary>
    public void ShowOutcome(MatchOutcome outcome)
    {
        Shown = outcome;
        Title.Text = _ui.MatchOver(outcome switch
        {
            MatchOutcome.Victory => MatchOverText.Victory,
            MatchOutcome.Draw => MatchOverText.Draw,
            _ => MatchOverText.Defeat,
        });
        Visible = true;
        StatsButton.GrabFocus();
    }
}
