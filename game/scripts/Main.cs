using System;
using System.IO;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Replays;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>Boot scene script and the game shell (M6-V1): prints the sim version, loads game data, then shows the main menu or starts the match.</summary>
/// <remarks>
/// With any match flag on the command line (<see cref="LaunchOptions.HasMatchFlag"/>: <c>--seed</c>, <c>--units</c>,
/// <c>--screenshot</c>, <c>--bench</c>, <c>--skirmish</c> ...) the match starts at once, as before M6-V1, so every dev
/// script keeps working; with none, the main menu shows. Main menu -> skirmish setup -> match; Esc in a match pauses it
/// (the tree pauses: the sim stops ticking and the match takes no input) unless a targeting order, a build menu or a
/// placement ghost is armed, which Esc cancels as before. Quit to menu frees the whole match. The shell holds which screen
/// is up and the player's picks, never game state. M6-V6: the settings (<see cref="GameSettings"/>, <c>user://settings.cfg</c>)
/// are read and applied right after the data, before the menu or the match; the Settings screen opens from the menu and the
/// pause menu, and its Apply / Defaults / Back apply and save them (<see cref="CommitSettings"/>). <c>--vsync</c> wins over
/// the file for the run and never changes it. M6-V4: when the match gives the local player a result (<see cref="Match.Ended"/>)
/// the tree pauses as for the pause menu and the match-over banner shows, once per match; its Continue watching unpauses and
/// hides it for good, its Stats (and the pause menu's, from then on) opens the stats screen, its Main menu ends the match.
/// M6-V7: every frame the shell confines the cursor to the window while a match runs unpaused in the focused window and the
/// "Confine cursor to window" setting is on (<see cref="CursorConfine"/>), and frees it otherwise; each change is logged
/// ("Cursor: confined" / "Cursor: visible"). Never headless, never with <c>--no-confine</c> (a shell flag, not a match flag).
/// M6-V9: every match started from the menu or with <c>--skirmish</c> is recorded (a <see cref="ReplayRecorder"/>, checkpoints
/// every <see cref="ReplayRecorder.DefaultCheckpointInterval"/> ticks) and written to <see cref="ReplayDir"/> when it ends
/// (the banner) and again at Quit to menu (<see cref="SaveReplay"/>), the oldest files pruned past
/// <see cref="ReplayStore.MaxReplays"/>. Main menu -> Load Game lists them; Watch (or <c>--replay &lt;path&gt;</c>, a shell flag)
/// plays one through the match scene (<see cref="WatchReplay"/>): a refused file stays on the Load Game screen with
/// <c>load.refused</c>; the end of the recording shows the banner ("Replay ended", or the outcome when it fell on the last tick)
/// and a mismatch "Replay out of sync", both without Continue watching.
/// </remarks>
public partial class Main : Node3D
{
    /// <summary>The match scene the shell instantiates for each match.</summary>
    public const string MatchScenePath = "res://scenes/Match.tscn";

    private GameData? _data;
    private MainMenu? _menu;
    private SkirmishSetup? _setup;
    private PauseMenu? _pause;
    private SettingsScreen? _settingsScreen;
    private MatchOverBanner? _banner;
    private MatchStats? _stats;
    private LoadGameScreen? _load;
    private bool _bannerShown;
    private bool _noConfine;
    private bool _replayDirSet;
    private string? _replayDir;

    /// <summary>The shell flag that plays a replay file at boot (M6-V9): <c>--replay &lt;path&gt;</c> (a <c>user://</c> or OS path).</summary>
    public const string ReplayFlag = "--replay";

    /// <summary>
    /// The folder the shell writes each recorded match to and Load Game lists (M6-V9). Unset, it is
    /// <see cref="ReplayStore.DefaultDir"/> when this node is the scene the game booted (a child of the root), and null (no file
    /// written, nothing listed) when a test scene hosts the shell, so no test touches the player's folder; tests set their own.
    /// </summary>
    public string? ReplayDir
    {
        get => _replayDirSet ? _replayDir : IsInsideTree() && GetParent() == GetTree().Root ? ReplayStore.DefaultDir : null;
        set
        {
            _replayDir = value;
            _replayDirSet = true;
        }
    }

    /// <summary>The file the running (or last) recorded match writes, or null when it records nothing or has no folder.</summary>
    public string? ReplayPath { get; private set; }

    /// <summary>The Load Game screen (M6-V9); null without <c>ui.json</c>.</summary>
    public LoadGameScreen? LoadScreen => _load;

    /// <summary>The user args to read instead of the command line's (tests); null for <see cref="OS.GetCmdlineUserArgs"/>.</summary>
    public string[]? UserArgs { get; set; }

    /// <summary>The settings file to read and write (tests point it elsewhere so they never touch the player's); set before the node enters the tree.</summary>
    public string SettingsPath { get; set; } = GameSettings.DefaultPath;

    /// <summary>The settings in effect (read at boot, then each commit); defaults before <see cref="Node._Ready"/>.</summary>
    public GameSettings Settings { get; private set; } = new();

    /// <summary><c>--vsync on|off</c> for this run (it wins over <see cref="Settings"/>), or null.</summary>
    public bool? VsyncOverride { get; private set; }

    /// <summary>The VSync this run uses: the flag, else the settings' (headless has no window, so this is the state tests read).</summary>
    public bool EffectiveVsync => VsyncOverride ?? Settings.Vsync;

    /// <summary>The settings screen; null without <c>ui.json</c>.</summary>
    public SettingsScreen? SettingsScreen => _settingsScreen;

    /// <summary>The match-over banner (M6-V4); null without <c>ui.json</c>.</summary>
    public MatchOverBanner? Banner => _banner;

    /// <summary>The stats screen (M6-V4); null without <c>ui.json</c>.</summary>
    public MatchStats? Stats => _stats;

    /// <summary>True while the match-over banner is up (the tree is paused under it).</summary>
    public bool BannerUp => CurrentMatch != null && _banner is { Visible: true };

    /// <summary>Called with each new match after it enters the tree and before <see cref="Match.Start"/> (tests: e.g. a 3-seat runner); null in the game.</summary>
    public Action<Match>? BeforeStart { get; set; }

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

    /// <summary>The flag that keeps the cursor free for the whole run (dev and scripted scenes; M6-V7).</summary>
    public const string NoConfineFlag = "--no-confine";

    /// <summary>True while the shell holds the cursor confined to the window (M6-V7); what it last set <see cref="Input.MouseMode"/> to.</summary>
    public bool CursorConfined { get; private set; }

    /// <summary>Whether confinement may happen at all this run: the setting, no <see cref="NoConfineFlag"/>, a real window.</summary>
    public bool ConfineAllowed => Settings.ConfineCursor && !_noConfine && SettingsApplier.HasWindow;

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
        _noConfine = Array.IndexOf(args, NoConfineFlag) >= 0;
        UiText? ui = UiText.Shared;
        // Without ui.json there is no menu text: the errors are logged and the match starts as it did before the shell.
        LaunchOptions? options = LaunchOptions.HasMatchFlag(args) || ui == null ? LaunchOptions.Parse(args) : null;
        // M6-V6 boot order: the settings are read and applied before any screen or match exists.
        VsyncOverride = options?.Vsync;
        Settings = GameSettings.Load(SettingsPath);
        // BUG-0681: an engine window flag (--resolution, --fullscreen ...) wins over the file's window for the run, as --vsync does.
        // The process's own command line: OS.GetCmdlineArgs() leaves out the args the engine consumed (it is empty here).
        SettingsApplier.Apply(Settings, GetTree().Root, VsyncOverride, keepWindow: SettingsApplier.HasEngineWindowFlag(System.Environment.GetCommandLineArgs()));
        if (ui != null) BuildScreens(ui, _data);
        // M6-V9: --replay <path> plays the file at once (a refusal shows the Load Game screen with the reason).
        int replayAt = Array.IndexOf(args, ReplayFlag);
        string? replayPath = replayAt >= 0 && replayAt + 1 < args.Length && !args[replayAt + 1].StartsWith("--", StringComparison.Ordinal) ? args[replayAt + 1] : null;
        if (replayAt >= 0 && replayPath == null) GD.PushWarning($"Ignoring {ReplayFlag} with no path.");
        if (replayPath != null)
        {
            if (!WatchReplay(replayPath, LaunchOptions.Parse(args)) && _load == null) GetTree().Quit(1);
        }
        else if (options != null) StartMatch(options, record: options.Skirmish);
        else ShowMenu();
    }

    private void BuildScreens(UiText ui, GameData data)
    {
        _menu = new MainMenu { Name = "MainMenu", Visible = false };
        AddChild(_menu);
        _menu.Build(ui);
        _menu.SkirmishPressed += ShowSetup;
        _menu.LoadPressed += ShowLoadGame;
        _menu.SettingsPressed += ShowSettings;
        _menu.QuitPressed += () => GetTree().Quit();

        _setup = new SkirmishSetup { Name = "SkirmishSetup", Visible = false };
        AddChild(_setup);
        _setup.Build(ui, data);
        _setup.BackPressed += ShowMenu;
        _setup.StartPressed += StartSkirmish;

        _load = new LoadGameScreen { Name = "LoadGame", Visible = false };
        AddChild(_load);
        _load.Build(ui, data);
        _load.BackPressed += ShowMenu;
        _load.WatchPressed += path => WatchReplay(path);

        _pause = new PauseMenu { Name = "PauseMenu" };
        AddChild(_pause);
        _pause.Build(ui);
        _pause.ResumePressed += Resume;
        _pause.QuitToMenuPressed += QuitToMenu;
        _pause.SettingsPressed += ShowSettings;
        _pause.StatsPressed += ShowStats;

        _banner = new MatchOverBanner { Name = "MatchOverBanner" };
        AddChild(_banner);
        _banner.Build(ui);
        _banner.StatsPressed += ShowStats;
        _banner.ContinuePressed += ContinueWatching;
        _banner.MenuPressed += QuitToMenu;

        _stats = new MatchStats { Name = "MatchStats" };
        AddChild(_stats);
        _stats.Build(ui, data);
        _stats.BackPressed += CloseStats;

        _settingsScreen = new SettingsScreen { Name = "SettingsScreen" };
        AddChild(_settingsScreen);
        _settingsScreen.Build(ui);
        _settingsScreen.ApplyPressed += () => CommitSettings(_settingsScreen.Picks(), stay: true);
        // BUG-0743: Defaults resets the General fields; the keys stay as the tab shows them (it has its own Default keys).
        _settingsScreen.DefaultsPressed += () => CommitSettings(new GameSettings { Keys = _settingsScreen.Picks().Keys }, stay: true);
        _settingsScreen.BackPressed += () => CommitSettings(_settingsScreen.Picks(), stay: false);
    }

    /// <summary>Shows the settings screen with the settings in effect: over the main menu, or over the pause menu (the match stays paused).</summary>
    public void ShowSettings()
    {
        if (_settingsScreen == null || _menu == null) return;
        if (!Paused) _menu.Visible = false;
        _settingsScreen.ShowSettings(Settings, GetWindow().Size, VsyncOverride);
        _settingsScreen.Visible = true;
        _settingsScreen.BackButton.GrabFocus();
    }

    /// <summary>
    /// Makes <paramref name="picks"/> the settings: saved to <see cref="SettingsPath"/>, then applied (window and VSync, quality,
    /// buses, the running match's camera). With <paramref name="stay"/> the screen shows them again (Apply, Defaults), else
    /// it closes to the menu or the pause menu it came from (Back, Esc).
    /// </summary>
    public void CommitSettings(GameSettings picks, bool stay)
    {
        Settings = picks.Clone();
        Settings.Save(SettingsPath);
        SettingsApplier.Apply(Settings, GetTree().Root, VsyncOverride);
        ApplyCamera();
        if (_settingsScreen == null) return;
        if (stay)
        {
            _settingsScreen.ShowSettings(Settings, GetWindow().Size, VsyncOverride);
            return;
        }
        _settingsScreen.Visible = false;
        if (Paused) _pause!.ResumeButton.GrabFocus();
        else if (CurrentMatch == null) ShowMenu(); // BUG-0684 item 3: never the menu over a running match
    }

    // The running match's camera takes the edge pan speed (a new match takes it in StartMatch).
    private void ApplyCamera()
    {
        if (CurrentMatch?.GetNodeOrNull<RtsCamera>("RtsCamera") is RtsCamera camera) camera.EdgePanSpeed = Settings.EdgePan;
    }

    /// <summary>Shows the main menu (no match runs on a menu screen).</summary>
    public void ShowMenu()
    {
        if (_menu == null || _setup == null) return;
        _setup.Visible = false;
        if (_load != null) _load.Visible = false;
        _menu.Visible = true;
        _menu.SkirmishButton.GrabFocus();
    }

    /// <summary>Shows the Load Game screen (M6-V9) with <see cref="ReplayDir"/>'s replays read afresh.</summary>
    public void ShowLoadGame()
    {
        if (_menu == null || _load == null) return;
        _menu.Visible = false;
        _load.Refresh(ReplayDir);
        _load.Visible = true;
        if (_load.WatchButton.Disabled) _load.BackButton.GrabFocus();
        else _load.WatchButton.GrabFocus();
    }

    /// <summary>
    /// Plays the replay file at <paramref name="path"/> (M6-V9; a <c>user://</c> or OS path) in a new match scene: true when it
    /// started. A file <see cref="ReplayFormat.TryReadFile"/> refuses, or one <see cref="SimRunner.StartPlayback"/> refuses
    /// (other data, an unknown faction), starts nothing: the Load Game screen shows with <c>load.refused</c>. Of
    /// <paramref name="options"/> (the boot's args for <c>--replay</c>) only the view's flags count.
    /// </summary>
    public bool WatchReplay(string path, LaunchOptions? options = null)
    {
        if (_data == null || CurrentMatch != null) return false;
        string full = ProjectSettings.GlobalizePath(path);
        ReplayError read = ReplayFormat.TryReadFile(full, out Replay? replay);
        if (read != ReplayError.None || replay == null) return Refused(full, read);
        if (_menu != null) _menu.Visible = false;
        if (_setup != null) _setup.Visible = false;
        if (_load != null) _load.Visible = false;
        var match = GD.Load<PackedScene>(MatchScenePath).Instantiate<Match>();
        match.Name = "Match";
        match.ProcessMode = ProcessModeEnum.Pausable;
        AddChild(match);
        BeforeStart?.Invoke(match);
        ReplayError refused = match.StartReplay(_data, replay, options ?? LaunchOptions.Parse(Array.Empty<string>()), full);
        if (refused != ReplayError.None)
        {
            RemoveChild(match);
            match.QueueFree();
            return Refused(full, refused);
        }
        CurrentMatch = match;
        ReplayPath = null; // a replay records nothing
        _bannerShown = false;
        match.Ended += outcome => OnMatchEnded(match, outcome);
        match.GetNode<SimRunner>("SimRunner").PlaybackFinished += runner => OnPlaybackFinished(match, runner);
        ApplyCamera();
        return true;
    }

    // A replay that won't play: the reason in the dev log, the Load Game screen with the refusal note.
    private bool Refused(string path, ReplayError error)
    {
        GD.Print($"Replay refused: {path}: {error}");
        if (_load != null && _menu != null)
        {
            _menu.Visible = false;
            if (_setup != null) _setup.Visible = false;
            _load.Refresh(ReplayDir);
            _load.ShowRefused();
            _load.Visible = true;
            _load.BackButton.GrabFocus();
        }
        return false;
    }

    // M6-V9: playback reached the recording's end or stopped on a mismatch: the banner, without Continue watching. The end
    // on the tick that gave the outcome keeps the outcome's title; a mismatch always says so.
    private void OnPlaybackFinished(Match match, SimRunner runner)
    {
        if (match != CurrentMatch || _banner == null) return;
        bool stopped = runner.PlaybackState == PlaybackState.Stopped;
        _bannerShown = true;
        GetTree().Paused = true;
        if (match.Simulation is Simulation sim) match.GetNodeOrNull<ResourceBar>("Hud/ResourceBar")?.Sync(sim.World);
        if (!stopped && _banner.Visible) _banner.EndOfReplay();
        else _banner.ShowTitle(UiText.Shared?.LoadGame(stopped ? LoadText.Mismatch : LoadText.Ended) ?? "");
    }

    /// <summary>
    /// Writes the running recorded match to <see cref="ReplayPath"/> (M6-V9: at the banner and at Quit to menu, so the file
    /// holds the match up to the latest of the two), then prunes the folder to <see cref="ReplayStore.MaxReplays"/>. False
    /// (one warning, the match unharmed) when nothing records or the write fails.
    /// </summary>
    public bool SaveReplay()
    {
        if (CurrentMatch == null || ReplayPath == null || CurrentMatch.IsPlayback) return false;
        if (CurrentMatch.GetNodeOrNull<SimRunner>("SimRunner")?.Recorder is not ReplayRecorder recorder) return false;
        Replay replay;
        try
        {
            replay = recorder.ToReplay();
        }
        catch (InvalidOperationException e)
        {
            GD.PushWarning($"Replay not saved: {e.Message}");
            return false;
        }
        if (!ReplayStore.Write(replay, ReplayPath, out string error))
        {
            GD.PushWarning($"Replay not saved to {ReplayPath}: {error}");
            return false;
        }
        GD.Print($"Replay saved: {ReplayPath} ({replay.TickCount} ticks)");
        if (Path.GetDirectoryName(ReplayPath) is string dir) ReplayStore.Prune(dir, ReplayStore.MaxReplays);
        return true;
    }

    /// <summary>Shows the skirmish setup screen.</summary>
    public void ShowSetup()
    {
        if (_menu == null || _setup == null) return;
        _menu.Visible = false;
        _setup.Visible = true;
        _setup.StartButton.GrabFocus();
    }

    /// <summary>What the setup's Start does: a match from the picks' match flags (<see cref="SkirmishSetup.MatchArgs"/>: seed, map size, the seat count, each slot's faction and colour, each AI slot's difficulty), everything else the match's defaults.</summary>
    /// <remarks>M6-V9: the match is recorded (<see cref="SaveReplay"/>).</remarks>
    public void StartSkirmish(string[] args) => StartMatch(LaunchOptions.Parse(args), record: true);

    private void StartMatch(LaunchOptions options, bool record)
    {
        if (_data == null || CurrentMatch != null) return;
        if (_menu != null) _menu.Visible = false;
        if (_setup != null) _setup.Visible = false;
        if (_load != null) _load.Visible = false;
        var match = GD.Load<PackedScene>(MatchScenePath).Instantiate<Match>();
        match.Name = "Match";
        // Main runs Always (it answers Esc while paused); the match must stop with the tree.
        match.ProcessMode = ProcessModeEnum.Pausable;
        AddChild(match);
        SimRunner runner = match.GetNode<SimRunner>("SimRunner");
        // M6-V9: the menu's and --skirmish's matches record; a test's BeforeStart may still set its own interval.
        if (record) runner.RecordCheckpointInterval = ReplayRecorder.DefaultCheckpointInterval;
        BeforeStart?.Invoke(match);
        // Ready before Start, as when the match was a child of Main.tscn: Start wires nodes the match's _Ready looked up.
        match.Start(_data, options);
        // The wall clock names the file and nothing else (CLAUDE.md rule 2: it never reaches the sim).
        ReplayPath = record && runner.Recorder != null && ReplayDir is string dir
            ? ReplayStore.NewPath(dir, DateTime.Now, unchecked((ulong)runner.Seed)) : null;
        CurrentMatch = match;
        _bannerShown = false;
        match.Ended += outcome => OnMatchEnded(match, outcome);
        ApplyCamera();
    }

    /// <summary>Pauses the running match: the tree stops (no tick, no match input) and the pause menu shows.</summary>
    public void PauseMatch()
    {
        if (CurrentMatch == null || _pause == null || Paused) return;
        GetTree().Paused = true;
        _pause.StatsButton.Visible = _bannerShown && _stats != null;
        _pause.Visible = true;
        _pause.ResumeButton.GrabFocus();
    }

    // M6-V4: the match's result, once (Match latches it): the banner opens through the pause path (the tree pauses as for the
    // pause menu; SimRunner runs no more ticks this frame). A match that isn't the current one (freed) is ignored.
    private void OnMatchEnded(Match match, MatchOutcome outcome)
    {
        if (match != CurrentMatch || _banner == null || _bannerShown) return;
        _bannerShown = true;
        GetTree().Paused = true;
        SaveReplay(); // M6-V9: the recording up to the result
        // BUG-0710 item 1: the HUD is Pausable and the tree pauses inside the killing tick, so it would show the frame before
        // the kill under the banner; one read of the sim now makes its K / L agree with the stats screen.
        if (match.Simulation is Simulation sim) match.GetNodeOrNull<ResourceBar>("Hud/ResourceBar")?.Sync(sim.World);
        _banner.ShowOutcome(outcome);
    }

    /// <summary>What the banner's Continue watching does: hides it for the rest of the match and unpauses (Esc is the pause menu again).</summary>
    public void ContinueWatching()
    {
        if (_banner == null || !_banner.Visible || !_banner.ContinueButton.Visible) return; // M6-V9: a finished replay has nothing to continue
        _banner.Visible = false;
        if (_stats != null) _stats.Visible = false;
        GetTree().Paused = false;
    }

    /// <summary>Opens the stats screen over the banner or the pause menu (the match stays paused under it).</summary>
    public void ShowStats()
    {
        if (_stats == null || CurrentMatch == null) return;
        _stats.ShowFor(CurrentMatch);
    }

    /// <summary>What the stats screen's Back (or Esc) does: closes it to the banner or the pause menu it came from.</summary>
    public void CloseStats()
    {
        if (_stats == null) return;
        _stats.Visible = false;
        if (_banner is { Visible: true }) _banner.StatsButton.GrabFocus();
        else if (Paused) _pause!.ResumeButton.GrabFocus();
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
        if (_banner != null) _banner.Visible = false;
        if (_stats != null) _stats.Visible = false;
        Resume();
        if (CurrentMatch != null)
        {
            SaveReplay(); // M6-V9: the whole match so far (a no-op for a replay or an unrecorded match)
            ReplayPath = null;
            RemoveChild(CurrentMatch);
            CurrentMatch.QueueFree();
            CurrentMatch = null;
        }
        ShowMenu();
    }

    public override void _Process(double delta)
    {
        // Main runs Always, so this sees the paused frames too: the pause menu, the banner and its stats, settings over the
        // pause menu all pause the tree; the menu screens have no match; Alt-Tab takes the focus.
        bool want = CursorConfine.Wanted(CurrentMatch != null, GetTree().Paused, DisplayServer.WindowIsFocused(), ConfineAllowed);
        SetConfined(want);
    }

    private void SetConfined(bool confined)
    {
        if (confined == CursorConfined) return;
        CursorConfined = confined;
        Input.MouseMode = confined ? Input.MouseModeEnum.Confined : Input.MouseModeEnum.Visible;
        GD.Print(confined ? "Cursor: confined" : "Cursor: visible");
    }

    public override void _Input(InputEvent e)
    {
        if (!e.IsActionPressed("ui_cancel") || e.IsEcho()) return;
        if (_settingsScreen is { Visible: true })
        {
            if (_settingsScreen.Listening) return; // M6-V8: Esc stops a key button's wait (SettingsScreen._Input), not the screen
            // Esc is Back: the picks are kept and the screen closes to where it came from (the pause menu stays paused).
            CommitSettings(_settingsScreen.Picks(), stay: false);
            GetViewport().SetInputAsHandled();
        }
        else if (CurrentMatch != null && _stats is { Visible: true })
        {
            CloseStats();
            GetViewport().SetInputAsHandled();
        }
        else if (BannerUp)
        {
            // The banner is answered with its buttons; Esc must not open the pause menu over it.
            GetViewport().SetInputAsHandled();
        }
        else if (CurrentMatch != null)
        {
            if (_pause == null) return;
            if (Paused) Resume();
            else if (!Armed(CurrentMatch)) PauseMatch();
            else return; // Esc is the armed order's cancel (SelectionController / CommandCard)
            GetViewport().SetInputAsHandled();
        }
        else if (_setup is { Visible: true } || _load is { Visible: true })
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

    public override void _Notification(int what)
    {
        // M6-V9: closing the window mid-match keeps the recording, as Quit to menu does.
        if (what == NotificationWMCloseRequest) SaveReplay();
    }

    public override void _ExitTree()
    {
        // A tree left paused would freeze whatever runs next in it (a test scene that hosts the shell).
        if (Paused || BannerUp) GetTree().Paused = false;
        SetConfined(false);
    }
}
