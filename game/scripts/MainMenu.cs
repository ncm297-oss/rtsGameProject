using System;
using Godot;

namespace Rts.Game;

/// <summary>The main menu (M6-V1, docs/02 "Game flow" 1): the game's name, Skirmish, Load Game (M6-V9), Settings (M6-V6) and Quit. Text from <c>ui.json</c> <c>menu</c>.</summary>
/// <remarks>Load Game lists the recorded replays until save / load lands. The menu only raises events; <see cref="Main"/> acts on them.</remarks>
public partial class MainMenu : CanvasLayer
{
    /// <summary>Raised by the Skirmish button.</summary>
    public event Action? SkirmishPressed;

    /// <summary>Raised by the Load Game button.</summary>
    public event Action? LoadPressed;

    /// <summary>Raised by the Settings button.</summary>
    public event Action? SettingsPressed;

    /// <summary>Raised by the Quit button.</summary>
    public event Action? QuitPressed;

    /// <summary>The Skirmish button (tests click it).</summary>
    public Button SkirmishButton { get; private set; } = null!;

    /// <summary>The Load Game button (M6-V9).</summary>
    public Button LoadButton { get; private set; } = null!;

    /// <summary>The Settings button.</summary>
    public Button SettingsButton { get; private set; } = null!;

    /// <summary>The Quit button.</summary>
    public Button QuitButton { get; private set; } = null!;

    /// <summary>Builds the screen's controls with <paramref name="ui"/>'s text.</summary>
    public void Build(UiText ui)
    {
        AddChild(ShellUi.Backdrop(new Color(0.09f, 0.08f, 0.07f)));
        VBoxContainer column = ShellUi.CentredColumn(this);
        column.AddChild(ShellUi.Heading(ui.Menu(MenuText.Title), 56));
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 24f) });
        SkirmishButton = ShellUi.Button("Skirmish", ui.Menu(MenuText.Skirmish));
        SkirmishButton.Pressed += () => SkirmishPressed?.Invoke();
        column.AddChild(SkirmishButton);
        LoadButton = ShellUi.Button("LoadGame", ui.Menu(MenuText.Load));
        LoadButton.Pressed += () => LoadPressed?.Invoke();
        column.AddChild(LoadButton);
        SettingsButton = ShellUi.Button("Settings", ui.Menu(MenuText.Settings));
        SettingsButton.Pressed += () => SettingsPressed?.Invoke();
        column.AddChild(SettingsButton);
        QuitButton = ShellUi.Button("Quit", ui.Menu(MenuText.Quit));
        QuitButton.Pressed += () => QuitPressed?.Invoke();
        column.AddChild(QuitButton);
    }
}
