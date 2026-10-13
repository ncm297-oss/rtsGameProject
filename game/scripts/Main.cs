using System;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;

namespace Rts.Game;

/// <summary>Boot scene script and the game shell (M6-V1): prints the sim version, loads game data, then shows the main menu or starts the match.</summary>
/// <remarks>
/// With any match flag on the command line (<see cref="LaunchOptions.HasMatchFlag"/>: <c>--seed</c>, <c>--units</c>,
/// <c>--screenshot</c>, <c>--bench</c>, <c>--skirmish</c> ...) the match starts at once, as before M6-V1, so every dev
/// script keeps working; with none, the main menu shows. Main menu -> skirmish setup -> match; Esc in a match pauses it
/// (the tree pauses: the sim stops ticking and the match takes no input) unless a targeting order, a build menu or a
/// placement ghost is armed, which Esc cancels as before. Quit to menu frees the whole match. The shell holds which screen
/// is up and the player's picks, never game state.
/// </remarks>
public partial class Main : Node3D
{
    /// <summary>The match scene the shell instantiates for each match.</summary>
    public const string MatchScenePath = "res://scenes/Match.tscn";

    private GameData? _data;
    private MainMenu? _menu;
    private SkirmishSetup? _setup;
    private PauseMenu? _pause;

    /// <summary>The user args to read instead of the command line's (tests); null for <see cref="OS.GetCmdlineUserArgs"/>.</summary>
    public string[]? UserArgs { get; set; }

    /// <summary>The running match, or null on the menu screens.</summary>
    public Match? CurrentMatch { get; private set; }

    /// <summary>The main menu; null when <c>ui.json</c> failed to load (then the shell only starts matches).</summary>
    public MainMenu? Menu => _menu;

    /// <summary>The skirmish setup screen; null without <c>ui.json</c>.</summary>
    public SkirmishSetup? Setup => _setup;

    /// <summary>The pause menu; null without <c>ui.json</c>.</summary>
    public PauseMenu? Pause => _pause;

    /// <summary>True while a match is paused behind the pause menu.</summary>
    public bool Paused => CurrentMatch != null && _pause is { Visible: true };

    public override void _Ready()
    {
        // The smoke gate (tools/qa/smoke.ps1) greps for this banner.
        GD.Print($"Rts.Sim {SimInfo.Version}");

        DataLoadResult result = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
        if (!result.Ok)
        {
            foreach (DataError error in result.Errors)
                GD.PushError(error.ToString());
            GetTree().Quit(1);
            return;
        }
        _data = result.Data!;

        // The shell answers Esc while the match under it is paused; the match is set back to Pausable when it is added.
        ProcessMode = ProcessModeEnum.Always;
        string[] args = UserArgs ?? OS.GetCmdlineUserArgs();
        UiText? ui = UiText.Shared;
        if (ui != null) BuildScreens(ui, _data);
        // Without ui.json there is no menu text: the errors are logged and the match starts as it did before the shell.
        if (LaunchOptions.HasMatchFlag(args) || ui == null) StartMatch(LaunchOptions.Parse(args));
        else ShowMenu();
    }

    private void BuildScreens(UiText ui, GameData data)
    {
        _menu = new MainMenu { Name = "MainMenu", Visible = false };
        AddChild(_menu);
        _menu.Build(ui);
        _menu.SkirmishPressed += ShowSetup;
        _menu.QuitPressed += () => GetTree().Quit();

        _setup = new SkirmishSetup { Name = "SkirmishSetup", Visible = false };
        AddChild(_setup);
        _setup.Build(ui, data);
        _setup.BackPressed += ShowMenu;
        _setup.StartPressed += StartSkirmish;

        _pause = new PauseMenu { Name = "PauseMenu" };
        AddChild(_pause);
        _pause.Build(ui);
        _pause.ResumePressed += Resume;
        _pause.QuitToMenuPressed += QuitToMenu;
    }

    /// <summary>Shows the main menu (no match runs on a menu screen).</summary>
    public void ShowMenu()
    {
        if (_menu == null || _setup == null) return;
        _setup.Visible = false;
        _menu.Visible = true;
        _menu.SkirmishButton.GrabFocus();
    }

    /// <summary>Shows the skirmish setup screen.</summary>
    public void ShowSetup()
    {
        if (_menu == null || _setup == null) return;
        _menu.Visible = false;
        _setup.Visible = true;
        _setup.StartButton.GrabFocus();
    }

    /// <summary>What the setup's Start does: a match from the picks' match flags (<see cref="SkirmishSetup.MatchArgs"/>: seed, map size, each slot's faction, the AI's difficulty), everything else the match's defaults.</summary>
    public void StartSkirmish(string[] args) => StartMatch(LaunchOptions.Parse(args));

    private void StartMatch(LaunchOptions options)
    {
        if (_data == null || CurrentMatch != null) return;
        if (_menu != null) _menu.Visible = false;
        if (_setup != null) _setup.Visible = false;
        var match = GD.Load<PackedScene>(MatchScenePath).Instantiate<Match>();
        match.Name = "Match";
        // Main runs Always (it answers Esc while paused); the match must stop with the tree.
        match.ProcessMode = ProcessModeEnum.Pausable;
        AddChild(match);
        // Ready before Start, as when the match was a child of Main.tscn: Start wires nodes the match's _Ready looked up.
        match.Start(_data, options);
        CurrentMatch = match;
    }

    /// <summary>Pauses the running match: the tree stops (no tick, no match input) and the pause menu shows.</summary>
    public void PauseMatch()
    {
        if (CurrentMatch == null || _pause == null || Paused) return;
        GetTree().Paused = true;
        _pause.Visible = true;
        _pause.ResumeButton.GrabFocus();
    }

    /// <summary>What Resume (or Esc on the pause menu) does.</summary>
    public void Resume()
    {
        if (_pause == null) return;
        _pause.Visible = false;
        GetTree().Paused = false;
    }

    /// <summary>What Quit to menu does: ends the match (its sim and every view node go with it) and shows the main menu.</summary>
    public void QuitToMenu()
    {
        Resume();
        if (CurrentMatch != null)
        {
            RemoveChild(CurrentMatch);
            CurrentMatch.QueueFree();
            CurrentMatch = null;
        }
        ShowMenu();
    }

    public override void _Input(InputEvent e)
    {
        if (!e.IsActionPressed("ui_cancel") || e.IsEcho()) return;
        if (CurrentMatch != null)
        {
            if (_pause == null) return;
            if (Paused) Resume();
            else if (!Armed(CurrentMatch)) PauseMatch();
            else return; // Esc is the armed order's cancel (SelectionController / CommandCard)
            GetViewport().SetInputAsHandled();
        }
        else if (_setup is { Visible: true })
        {
            ShowMenu();
            GetViewport().SetInputAsHandled();
        }
    }

    // Whether Esc belongs to the match: it cancels targeting (A, M, an ability) or closes a build menu and its ghost.
    private static bool Armed(Match match)
    {
        var selection = match.GetNodeOrNull<SelectionController>("SelectionController");
        var card = match.GetNodeOrNull<CommandCard>("Hud/CommandCard");
        return selection is { Targeting: true } || card != null && (card.MenuOpen || card.GhostActive);
    }

    public override void _ExitTree()
    {
        // A tree left paused would freeze whatever runs next in it (a test scene that hosts the shell).
        if (Paused) GetTree().Paused = false;
    }
}
