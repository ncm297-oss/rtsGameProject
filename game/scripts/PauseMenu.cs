using System;
using Godot;

namespace Rts.Game;

/// <summary>The pause menu over a match (M6-V1, docs/02 "Game flow" 3): Resume and Quit to menu. Text from <c>ui.json</c> <c>pause</c>.</summary>
/// <remarks>
/// It runs while the tree is paused (<see cref="Node.ProcessModeEnum.Always"/>); its backdrop takes every click, so nothing
/// under it gets one. It only raises events; <see cref="Main"/> pauses, resumes and ends the match.
/// </remarks>
public partial class PauseMenu : CanvasLayer
{
    /// <summary>Drawn over the HUD (layer 1) and the debug overlay.</summary>
    public const int DrawLayer = 20;

    /// <summary>Raised by the Resume button.</summary>
    public event Action? ResumePressed;

    /// <summary>Raised by the Quit to menu button.</summary>
    public event Action? QuitToMenuPressed;

    /// <summary>The Resume button.</summary>
    public Button ResumeButton { get; private set; } = null!;

    /// <summary>The Quit to menu button.</summary>
    public Button QuitToMenuButton { get; private set; } = null!;

    /// <summary>Builds the screen's controls with <paramref name="ui"/>'s text; hidden until <see cref="Main"/> shows it.</summary>
    public void Build(UiText ui)
    {
        Layer = DrawLayer;
        ProcessMode = ProcessModeEnum.Always;
        Visible = false;
        AddChild(ShellUi.Backdrop(new Color(0f, 0f, 0f, 0.55f)));
        VBoxContainer column = ShellUi.CentredColumn(this);
        column.AddChild(ShellUi.Heading(ui.Pause(PauseText.Title), 44));
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 16f) });
        ResumeButton = ShellUi.Button("Resume", ui.Pause(PauseText.Resume));
        ResumeButton.Pressed += () => ResumePressed?.Invoke();
        column.AddChild(ResumeButton);
        QuitToMenuButton = ShellUi.Button("QuitToMenu", ui.Pause(PauseText.QuitToMenu));
        QuitToMenuButton.Pressed += () => QuitToMenuPressed?.Invoke();
        column.AddChild(QuitToMenuButton);
    }
}
