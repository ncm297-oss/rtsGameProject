using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Replays;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// M6-V17: the skirmish setup's starting-resources list and the preset carried through the command line, the match start,
/// a save and its load, the recording and its playback, and the Load Game list; plus BUG-1066's two rows. Through the shell
/// (<c>Main.tscn</c>).
/// <list type="bullet">
/// <item>The list: one item per <c>rules.json</c> <c>startingResources</c> preset in file order, each item's text its
/// <c>displayName</c> and its tooltip its <c>description</c> (from the data, no C# literal), the label <c>setup.starting_resources</c>;
/// default <c>standard</c> with no <c>--resources</c> in <see cref="SkirmishSetup.MatchArgs"/>; each other preset adds
/// <c>--resources &lt;id&gt;</c>, which <see cref="LaunchOptions.Parse"/> reads back. The four-slot setup fits 1152 x 648.</item>
/// <item>The flag: junk values warned and ignored; an id the data lacks starts a Standard match (<see cref="Match.PresetFor"/>);
/// <c>--resources high</c> gives every seat of a four-seat match the preset's gold and wood at tick 0, and the "Match started"
/// line names it.</item>
/// <item>A High match from the setup's Start: saved at tick 300 (the save header's preset reads High), recorded to 600 (format
/// 8 with the <c>starting-resources high</c> line); Load Game lists both with <c>load.preset</c> and the preset's name, and a
/// Standard match's save and recording as before (the seats only); Watch plays the High recording to every checkpoint; Load
/// resumes the High save with the bank and the state hash it was saved with. Hostile <c>starting-resources</c> lines make the
/// header unreadable; an id the data lacks lists as the id; a save header cut before its preset lists with no preset.</item>
/// <item>BUG-1066: <c>--team 1 1 --team 2 1</c> (every player on one team) is warned about and starts a free-for-all; in the
/// setup, Back and Start sit at the same place with the one-team line shown and hidden.</item>
/// </list>
/// </summary>
/// <remarks>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/StartingResourcesTest.tscn</c>; prints "STARTING RESOURCES
/// TEST PASS" or "STARTING RESOURCES TEST FAIL: ..." lines. Per-process folders under <c>user://</c> (BUG-0770's rule), removed
/// at the end. Windowed with <c>-- --shots &lt;dir&gt;</c> it saves the setup (High picked, then one team) and the Load Game list.
/// </remarks>
public partial class StartingResourcesTest : Node
{
    /// <summary>Game time each frame advances (<see cref="SimRunner.FixedFrameDelta"/>): five ticks.</summary>
    public const double FrameSeconds = 0.25;

    private const int SaveTick = 300, RecordTicks = 600, StandardTicks = 200;

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private UiText _ui = null!;
    private string _root = "", _replays = "", _saves = "";
    private string? _shots;
    private StartingResourcesDef _high = null!;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // the shell pauses the tree
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            string[] args = OS.GetCmdlineUserArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--shots") _shots = args[i + 1];
            await Frames(1);
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            _ui = UiText.Shared ?? throw new InvalidOperationException("ui.json failed to load");
            // The rows below need a preset other than Standard: the first one in the data (the shipped one is "high").
            _high = _data.Rules.StartingResources.FirstOrDefault(p => p.Key != StartingResourcesDef.StandardId)
                ?? throw new InvalidOperationException("rules.json has no preset other than standard");
            _root = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://starting-resources-test-{OS.GetProcessId()}"));
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            _replays = Path.Combine(_root, "replays");
            _saves = Path.Combine(_root, "saves");
            Directory.CreateDirectory(_replays);
            Directory.CreateDirectory(_saves);
            Flags();
            Main main = NewMain();
            AddChild(main);
            await Frames(2);
            try
            {
                await SetupList(main);
                await FlagMatches(main);
                await OneTeam(main);
                await HighMatch(main);
            }
            finally
            {
                if (main.CurrentMatch != null) main.QuitToMenu();
                RemoveChild(main);
                main.QueueFree();
                await Frames(2);
            }
            try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        foreach (string f in _failures.Take(60)) GD.Print($"STARTING RESOURCES TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("STARTING RESOURCES TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- the command line ----

    private void Flags()
    {
        Check(LaunchOptions.MatchFlags.Contains("--resources") && LaunchOptions.HasMatchFlag(new[] { "--resources", "high" }), "--resources is no match flag");
        Check(LaunchOptions.Parse(Array.Empty<string>()).StartingResources == null, "no --resources: a preset picked");
        Check(LaunchOptions.Parse(new[] { "--resources", _high.Key }).StartingResources == _high.Key, $"--resources {_high.Key} not read");
        // Junk is warned about and ignored (a flag is never taken as the value, BUG-0041).
        foreach (string[] junk in new[] { new[] { "--resources" }, new[] { "--resources", "High" }, new[] { "--resources", "hi-gh" },
            new[] { "--resources", "--seed", "4" }, new[] { "--resources", new string('a', DataLimits.MaxStartingResourcesIdLength + 1) } })
        {
            LaunchOptions o = LaunchOptions.Parse(junk);
            Check(o.StartingResources == null, $"--resources junk [{string.Join(' ', junk)}] read as '{o.StartingResources}'");
        }
        Check(LaunchOptions.Parse(new[] { "--resources", "--seed", "4" }).Seed == 4, "--resources swallowed the next flag");
        Check(LaunchOptions.Parse(new[] { "--resources", "standard", "--resources", _high.Key }).StartingResources == _high.Key, "the last --resources does not win");
        // The data's check: an id it lacks plays Standard.
        Check(Match.PresetFor(_data, null) == StartingResourcesDef.StandardId && Match.PresetFor(_data, "no_such_preset") == StartingResourcesDef.StandardId
            && Match.PresetFor(_data, _high.Key) == _high.Key, "Match.PresetFor");

        // BUG-1066: every one of the match's players on one team is one side: warned, and the teams dropped (a free-for-all).
        LaunchOptions one = LaunchOptions.Parse(new[] { "--team", "1", "1", "--team", "2", "1" });
        Check(one.OneTeamWarning.Contains("--team", StringComparison.Ordinal) && one.TeamOf(0) == 0 && one.TeamOf(1) == 0,
            $"--team 1 1 --team 2 1: warning '{one.OneTeamWarning}', teams {one.TeamOf(0)}, {one.TeamOf(1)}");
        LaunchOptions four = LaunchOptions.Parse(new[] { "--players", "4", "--team", "1", "2", "--team", "2", "2", "--team", "3", "2", "--team", "4", "2" });
        Check(four.OneTeamWarning.Length > 0 && Enumerable.Range(0, 4).All(s => four.TeamOf(s) == 0), "four players on team 2 not dropped");
        // Two sides are left alone: a team and a None, two teams, or a slot past --players on another team.
        foreach (string[] two in new[] { new[] { "--team", "1", "1" }, new[] { "--team", "1", "1", "--team", "2", "2" },
            new[] { "--players", "3", "--team", "1", "1", "--team", "2", "1" }, new[] { "--players", "3", "--team", "1", "1", "--team", "2", "1", "--team", "3", "2" } })
        {
            LaunchOptions o = LaunchOptions.Parse(two);
            Check(o.OneTeamWarning == "" && o.TeamOf(0) == 1, $"[{string.Join(' ', two)}]: warning '{o.OneTeamWarning}', team {o.TeamOf(0)}");
        }
    }

    // ---- the setup's list ----

    private async Task SetupList(Main main)
    {
        main.ShowSetup();
        await Frames(2);
        SkirmishSetup setup = main.Setup!;
        OptionButton list = setup.ResourcesOption;
        var presets = _data.Rules.StartingResources;
        string[] items = Enumerable.Range(0, list.ItemCount).Select(list.GetItemText).ToArray();
        string[] tips = Enumerable.Range(0, list.ItemCount).Select(list.GetItemTooltip).ToArray();
        Check(items.SequenceEqual(presets.Select(p => p.DisplayName)) && tips.SequenceEqual(presets.Select(p => TooltipWrap.Wrap(p.Description))) && items.All(t => t.Length > 0),
            $"list items [{string.Join("|", items)}] (want the data's [{string.Join("|", presets.Select(p => p.DisplayName))}]), tooltips [{string.Join("|", tips)}]");
        int standard = presets.Select(p => p.Key).ToList().IndexOf(StartingResourcesDef.StandardId);
        Check(list.Selected == standard && setup.ResourcesPick == StartingResourcesDef.StandardId && list.TooltipText == TooltipWrap.Wrap(presets[standard].Description),
            $"default pick {list.Selected} ('{setup.ResourcesPick}'), tooltip '{list.TooltipText}'");
        Check(_ui.Setup(SetupText.StartingResources).Length > 0 && Labels(setup).Contains(_ui.Setup(SetupText.StartingResources)), "the list's label is not ui.json's setup.starting_resources");
        string[] plain = setup.MatchArgs()!;
        Check(!plain.Contains("--resources") && LaunchOptions.Parse(plain).StartingResources == null, $"Standard passes --resources: {string.Join(' ', plain)}");
        // Each preset round-trips through the args.
        for (int i = 0; i < presets.Length; i++)
        {
            list.Select(i);
            list.EmitSignal(OptionButton.SignalName.ItemSelected, (long)i); // as a click on the item
            string[] args = setup.MatchArgs()!;
            string? parsed = LaunchOptions.Parse(args).StartingResources;
            bool isStandard = presets[i].Key == StartingResourcesDef.StandardId;
            Check(setup.ResourcesPick == presets[i].Key && (isStandard ? parsed == null && !args.Contains("--resources") : parsed == presets[i].Key)
                && list.TooltipText == TooltipWrap.Wrap(presets[i].Description),
                $"item {i} ({presets[i].Key}): pick '{setup.ResourcesPick}', args [{string.Join(' ', args)}], parsed '{parsed}', tooltip '{list.TooltipText}'");
        }
        Check(setup.PickResources(StartingResourcesDef.StandardId) && !setup.PickResources("no_such_preset") && setup.ResourcesPick == StartingResourcesDef.StandardId, "PickResources");
        // The layout with four slots: every control inside the window, the list under the map size, left of the game speed's column.
        while (setup.SlotCount < SkirmishSetup.MaxSlots) setup.AddSlot();
        setup.PickResources(_high.Key);
        await Frames(2);
        Rect2 view = GetViewport().GetVisibleRect();
        var controls = new List<Control> { setup.SeedEdit, setup.MapSizeOption, setup.SpeedOption, list, setup.ForestsSpin, setup.MinesSpin,
            setup.AddSlotButton, setup.RemoveSlotButton, setup.BackButton, setup.StartButton };
        controls.AddRange(setup.FactionOptions);
        controls.AddRange(setup.TeamOptions);
        foreach (Control c in controls)
            Check(view.Grow(0.5f).Encloses(c.GetGlobalRect()), $"setup control {c.Name} {c.GetGlobalRect()} leaves the {view.Size} window");
        // Some room below Start: the screen grew a row in M6-V17, so the spacing shrank to keep a margin (not 1 px).
        Check(setup.StartButton.GetGlobalRect().End.Y <= view.End.Y - 8f, $"Start {setup.StartButton.GetGlobalRect()} is within 8 px of the window's bottom");
        Check(list.GetGlobalRect().Position.Y >= setup.MapSizeOption.GetGlobalRect().End.Y && list.GetGlobalRect().End.Y <= setup.ForestsSpin.GetGlobalRect().Position.Y,
            $"the list {list.GetGlobalRect()} is not between the map size {setup.MapSizeOption.GetGlobalRect()} and the forests {setup.ForestsSpin.GetGlobalRect()}");
        GD.Print($"  setup: list {list.GetGlobalRect()}, Back {setup.BackButton.GetGlobalRect()}, Start {setup.StartButton.GetGlobalRect()}, Team3 {setup.TeamOptions[3].GetGlobalRect()}");
        await Shot("setup-high");
        while (setup.SlotCount > LaunchOptions.MinPlayers) setup.RemoveSlot();
        setup.PickResources(StartingResourcesDef.StandardId);
        main.ShowMenu();
        await Frames(1);
    }

    // ---- the flag's matches ----

    private async Task FlagMatches(Main main)
    {
        // --resources <the other preset>: every seat of a four-seat match holds the preset's gold and wood at tick 0.
        main.StartSkirmish(new[] { "--skirmish", "--players", "4", "--seed", "3", "--mute", "--resources", _high.Key });
        Match? match = main.CurrentMatch;
        if (Check(match?.Simulation != null, "--resources started no match"))
        {
            World w = match!.Simulation!.World;
            Check(w.Config.StartingResources == _high.Key && match.GetNode<SimRunner>("SimRunner").StartingResources == _high.Key && match.Simulation.TickNumber == 0,
                $"the match plays '{w.Config.StartingResources}' (want '{_high.Key}') at tick {match.Simulation.TickNumber}");
            for (int p = 0; p < 4; p++)
                Check(w.Gold[p] == _high.Gold && w.Wood[p] == _high.Wood, $"--resources {_high.Key}: player {p} holds {w.Gold[p]} gold / {w.Wood[p]} wood at tick 0 (want {_high.Gold} / {_high.Wood})");
            Check(_high.Key != "high" || (_high.Gold == 1000 && _high.Wood == 1000), $"the shipped high preset is {_high.Gold} / {_high.Wood}");
            Check(match.StartLine.StartsWith("Match started: ", StringComparison.Ordinal) && match.StartLine.Contains($"starting resources {_high.Key},", StringComparison.Ordinal),
                $"the start line does not name the preset: '{match.StartLine}'");
            await QuitMatch(main);
        }
        // An id the data lacks: warned (Match.PresetFor), the match plays Standard.
        main.StartSkirmish(new[] { "--seed", "3", "--units", "0", "--mute", "--resources", "no_such_preset" });
        match = main.CurrentMatch;
        if (Check(match?.Simulation != null, "--resources no_such_preset started no match"))
        {
            World w = match!.Simulation!.World;
            StartingResourcesDef standard = _data.Rules.StartingResources[_data.Rules.FindStartingResources(StartingResourcesDef.StandardId)];
            Check(w.Config.StartingResources == StartingResourcesDef.StandardId && w.Gold[0] == standard.Gold && w.Wood[1] == standard.Wood
                && match.StartLine.Contains($"starting resources {StartingResourcesDef.StandardId},", StringComparison.Ordinal),
                $"an unknown preset: plays '{w.Config.StartingResources}', bank {w.Gold[0]} / {w.Wood[1]}, line '{match.StartLine}'");
            await QuitMatch(main);
        }
    }

    // ---- BUG-1066 ----

    private async Task OneTeam(Main main)
    {
        // The command line: every player on team 1 is warned about and plays a free-for-all.
        main.StartSkirmish(new[] { "--team", "1", "1", "--team", "2", "1", "--seed", "3", "--units", "0", "--mute" });
        Match? match = main.CurrentMatch;
        if (Check(match?.Simulation != null, "--team 1 1 --team 2 1 started no match"))
        {
            World w = match!.Simulation!.World;
            Check(w.TeamOf(0) == 0 && w.TeamOf(1) == 0 && w.IsEnemy(0, 1) && match.Seats.All(s => s.Team == 0),
                $"--team 1 1 --team 2 1: teams {w.TeamOf(0)}, {w.TeamOf(1)} (want a free-for-all)");
            await QuitMatch(main);
        }
        // The setup: Back and Start don't move when the one-team line shows.
        main.ShowSetup();
        await Frames(2);
        SkirmishSetup setup = main.Setup!;
        PickTeam(setup, 0, 0);
        PickTeam(setup, 1, 0);
        await Frames(2);
        Rect2 back = setup.BackButton.GetGlobalRect(), start = setup.StartButton.GetGlobalRect();
        Check(!setup.OneTeamLabel.Visible, "the one-team line shows in a free-for-all");
        PickTeam(setup, 0, 1);
        PickTeam(setup, 1, 1);
        await Frames(2);
        Rect2 view = GetViewport().GetVisibleRect();
        Check(setup.OneTeam && setup.OneTeamLabel.IsVisibleInTree() && view.Grow(0.5f).Encloses(setup.OneTeamLabel.GetGlobalRect()), $"the one-team line {setup.OneTeamLabel.GetGlobalRect()} not shown inside the window");
        Check(setup.BackButton.GetGlobalRect() == back && setup.StartButton.GetGlobalRect() == start,
            $"BUG-1066: with the line Back {setup.BackButton.GetGlobalRect()} / Start {setup.StartButton.GetGlobalRect()}, without it {back} / {start}");
        Check(Mathf.Abs(start.GetCenter().X + back.GetCenter().X - view.Size.X) <= 2f, $"Back {back} and Start {start} are not centred in the window");
        await Shot("setup-one-team");
        PickTeam(setup, 0, 0);
        PickTeam(setup, 1, 0);
        await Frames(2);
        Check(setup.BackButton.GetGlobalRect() == back && setup.StartButton.GetGlobalRect() == start, "Back / Start moved when the line hid again");
        main.ShowMenu();
        await Frames(1);
    }

    // ---- a High match from the setup: saved, recorded, listed, watched, loaded ----

    private async Task HighMatch(Main main)
    {
        // A Standard match first, so the list holds files that must read as before.
        (string? stdReplay, string? stdSave, _, _) = await MenuMatch(main, StartingResourcesDef.StandardId, StandardTicks / 2, StandardTicks);
        (string? replayPath, string? savePath, int[] bank, ulong hash) = await MenuMatch(main, _high.Key, SaveTick, RecordTicks);
        if (!Check(replayPath != null && File.Exists(replayPath) && savePath != null && File.Exists(savePath) && stdReplay != null && stdSave != null,
            $"files: replay {replayPath}, save {savePath}, standard {stdReplay} / {stdSave}")) return;

        // The files: the recording is format 8 with the preset's line; the save's header names it; the Standard ones don't.
        string[] lines = File.ReadAllLines(replayPath!);
        Check(lines[0] == "rts-replay 8" && lines.Contains($"starting-resources {_high.Key}"), $"the High recording starts '{lines[0]}' (want format 8 with the preset line)");
        Check(File.ReadAllLines(stdReplay!)[0] == "rts-replay 6", $"the Standard recording is '{File.ReadAllLines(stdReplay!)[0]}' (want format 6: the oldest that holds it)");
        Check(ReplayStore.TryReadHeader(replayPath!, out ReplayEntry? entry) && entry!.StartingResources == _high.Key, $"the High recording's header reads '{entry?.StartingResources}'");
        Check(ReplayStore.TryReadHeader(stdReplay!, out ReplayEntry? stdEntry) && stdEntry!.StartingResources == StartingResourcesDef.StandardId, "the Standard recording's header");
        using (FileStream stream = File.OpenRead(savePath!))
            Check(SaveSummary.TryRead(stream, out SaveSummary? summary) && summary!.StartingResources == _high.Key && summary.FormatVersion == 4,
                $"the High save's summary reads '{summary?.StartingResources}' (format {summary?.FormatVersion})");
        Check(SaveStore.TryReadHeader(stdSave!, out SaveEntry? stdSaveEntry) && stdSaveEntry!.StartingResources == StartingResourcesDef.StandardId, "the Standard save's header");
        HostileHeaders(replayPath!, savePath!);

        // Load Game: the High files name the preset, the Standard ones read as before.
        main.ShowLoadGame();
        await Frames(2);
        LoadGameScreen load = main.LoadScreen!;
        foreach ((string path, bool high) in new[] { (replayPath!, true), (savePath!, true), (stdReplay!, false), (stdSave!, false) })
        {
            int row = load.RowOf(path);
            if (!Check(row >= 0 && row < load.RowCount, $"Load Game does not list {Path.GetFileName(path)}")) continue;
            string cell = load.List.GetRoot().GetChild(row).GetText(2);
            var seats = load.KindOf(row) == LoadGameScreen.RowKind.Save ? load.Saves.First(s => s.Path == path).Seats : load.Entries.First(e => e.Path == path).Seats;
            string want = high ? _ui.SeatsWithPreset(load.SeatsText(seats), _high.DisplayName) : load.SeatsText(seats);
            Check(cell == want && cell.Contains(_high.DisplayName, StringComparison.Ordinal) == high, $"{Path.GetFileName(path)}: players cell '{cell}' (want '{want}')");
        }
        await Shot("load-game");

        // Watch the High recording through the screen: every checkpoint equal.
        int watchRow = load.RowOf(replayPath!);
        Check(load.Select(watchRow) && !load.WatchButton.Disabled, "the High replay row can't be watched");
        load.WatchButton.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        Match? watched = main.CurrentMatch;
        if (Check(watched is { IsPlayback: true }, "Watch started no playback"))
        {
            SimRunner runner = watched!.GetNode<SimRunner>("SimRunner");
            Check(runner.StartingResources == _high.Key && runner.Simulation!.World.Config.StartingResources == _high.Key, $"playback plays '{runner.Simulation!.World.Config.StartingResources}'");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (runner.PlaybackState == PlaybackState.Playing && watch.Elapsed.TotalSeconds < 120) await Frames(1);
            Replay replay = runner.Playback!;
            Check(runner.PlaybackState == PlaybackState.Ended && runner.PlaybackError == ReplayError.None && runner.CheckpointsCompared == replay.Checkpoints.Length
                && replay.Checkpoints.Length == RecordTicks / ReplayRecorder.DefaultCheckpointInterval,
                $"High playback {runner.PlaybackState} ({runner.PlaybackError}), {runner.CheckpointsCompared} of {replay.Checkpoints.Length} checkpoints");
            main.Banner!.MenuButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);
        }

        // Load the High save through the screen: the bank and the state are the saved ones, and it plays on.
        main.ShowLoadGame();
        await Frames(2);
        int saveRow = load.RowOf(savePath!);
        Check(load.Select(saveRow) && !load.LoadButton.Disabled, "the High save row can't be loaded");
        load.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
        Match? resumed = main.CurrentMatch;
        if (Check(resumed is { IsLoaded: true } && resumed.Simulation != null, "Load resumed nothing"))
        {
            Simulation sim = resumed!.Simulation!;
            int[] now = Bank(sim.World);
            Check(now.SequenceEqual(bank) && sim.StateHash() == hash && sim.World.Config.StartingResources == _high.Key
                && resumed.GetNode<SimRunner>("SimRunner").StartingResources == _high.Key && resumed.StartLine.Contains($"starting resources {_high.Key},", StringComparison.Ordinal),
                $"the loaded High match: bank [{string.Join(", ", now)}] (saved [{string.Join(", ", bank)}]), hash equal {sim.StateHash() == hash}, preset '{sim.World.Config.StartingResources}', line '{resumed.StartLine}'");
            int from = sim.TickNumber;
            var run = System.Diagnostics.Stopwatch.StartNew();
            while (sim.TickNumber < from + 20 && run.Elapsed.TotalSeconds < 30) await Frames(1);
            Check(sim.TickNumber >= from + 20, $"the loaded High match stopped at {sim.TickNumber}");
            await QuitMatch(main);
        }
    }

    // A match from the setup's Start at `preset`, saved from the pause menu at `saveAt` and played to `ticks`, then quit:
    // the recording, the save, the bank (gold then wood per player) and the state hash at the save.
    private async Task<(string? Replay, string? Save, int[] Bank, ulong Hash)> MenuMatch(Main main, string preset, int saveAt, int ticks)
    {
        main.ShowSetup();
        await Frames(2);
        SkirmishSetup setup = main.Setup!;
        Check(setup.PickResources(preset), $"the list has no '{preset}'");
        setup.StartButton.EmitSignal(BaseButton.SignalName.Pressed);
        Match? match = main.CurrentMatch;
        if (!Check(match?.Simulation != null, $"{preset}: Start started no match")) return (null, null, Array.Empty<int>(), 0);
        Simulation sim = match!.Simulation!;
        Check(sim.World.Config.StartingResources == preset, $"the setup's {preset} match plays '{sim.World.Config.StartingResources}'");
        string? replayPath = main.ReplayPath;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (sim.TickNumber < saveAt && clock.Elapsed.TotalSeconds < 60) await Frames(1);
        main.PauseMatch();
        await Frames(1);
        int[] bank = Bank(sim.World);
        ulong hash = sim.StateHash();
        Check(main.SaveGame(), $"the {preset} match did not save");
        string? savePath = main.LastSavePath;
        main.Resume();
        while (sim.TickNumber < ticks && clock.Elapsed.TotalSeconds < 120) await Frames(1);
        Check(sim.TickNumber == ticks, $"the {preset} match stopped at tick {sim.TickNumber} (want {ticks})");
        main.QuitToMenu();
        await Frames(2);
        return (replayPath, savePath, bank, hash);
    }

    // The replay header's preset line, damaged, makes the header unreadable; an id the data lacks lists as itself; a save
    // header cut right before its preset lists with no preset (Load refuses the file).
    private void HostileHeaders(string replayPath, string savePath)
    {
        string bad = Path.Combine(_root, "hostile");
        Directory.CreateDirectory(bad);
        string text = File.ReadAllText(replayPath);
        string line = $"starting-resources {_high.Key}\n";
        (string Name, string Text, bool Reads)[] cases =
        {
            ("missing", text.Replace(line, ""), false),
            ("empty", text.Replace(line, "starting-resources \n"), false),
            ("two-tokens", text.Replace(line, $"starting-resources {_high.Key} x\n"), false),
            ("no-key", text.Replace(line, $"{_high.Key}\n"), false),
            ("too-long", text.Replace(line, $"starting-resources {new string('a', DataLimits.MaxStartingResourcesIdLength + 1)}\n"), false),
            ("unknown", text.Replace(line, "starting-resources no_such_preset\n"), true),
        };
        foreach ((string name, string forged, bool reads) in cases)
        {
            string path = Path.Combine(bad, name + ReplayFormat.FileExtension);
            File.WriteAllText(path, forged);
            bool read = ReplayStore.TryReadHeader(path, out ReplayEntry? e);
            Check(forged != text && read == reads && (!reads || e!.StartingResources == "no_such_preset"), $"hostile preset line '{name}': read {read} ('{e?.StartingResources}')");
        }
        // The unknown id's row shows the id itself (the data has no displayName for it).
        var load = new LoadGameScreen();
        AddChild(load);
        load.Build(_ui, _data);
        Check(load.PlayersText(System.Collections.Immutable.ImmutableArray<ReplaySeat>.Empty, "no_such_preset") == _ui.SeatsWithPreset("", "no_such_preset")
            && load.PlayersText(System.Collections.Immutable.ImmutableArray<ReplaySeat>.Empty, "") == ""
            && load.PlayersText(System.Collections.Immutable.ImmutableArray<ReplaySeat>.Empty, StartingResourcesDef.StandardId) == "", "PlayersText for an unknown, a missing and the standard preset");
        RemoveChild(load);
        load.QueueFree();
        // The save: cut right before the preset's string (its length, then the id's bytes).
        byte[] bytes = File.ReadAllBytes(savePath);
        byte[] id = System.Text.Encoding.UTF8.GetBytes(_high.Key);
        byte[] field = BitConverter.GetBytes(id.Length).Concat(id).ToArray();
        int at = IndexOf(bytes, field);
        if (!Check(at > 0, "the save holds no preset string")) return;
        Check(SaveSummary.TryParse(bytes.AsSpan(0, at), out SaveSummary? cut) && cut!.StartingResources == "", $"a save header cut before its preset: '{cut?.StartingResources}'");
        Check(SaveSummary.TryParse(bytes.AsSpan(0, at + field.Length), out SaveSummary? whole) && whole!.StartingResources == _high.Key, "a save header ending at its preset");
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        return -1;
    }

    private static int[] Bank(World w) => w.Gold.ToArray().Concat(w.Wood.ToArray()).ToArray();

    private static void PickTeam(SkirmishSetup setup, int slot, int team)
    {
        setup.TeamOptions[slot].Select(team);
        setup.TeamOptions[slot].EmitSignal(OptionButton.SignalName.ItemSelected, (long)team); // as a click on the item
    }

    private static List<string> Labels(Node root)
    {
        var texts = new List<string>();
        foreach (Node n in root.FindChildren("*", nameof(Label), true, false)) texts.Add(((Label)n).Text);
        return texts;
    }

    private async Task QuitMatch(Main main)
    {
        main.QuitToMenu();
        await Frames(2);
    }

    private Main NewMain()
    {
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.UserArgs = new[] { Main.NoConfineFlag };
        main.SettingsPath = $"user://starting-resources-test-{OS.GetProcessId()}/settings.cfg"; // BUG-0770: never the player's file
        main.ReplayDir = _replays;
        main.SaveDir = _saves;
        main.BeforeStart = m => m.GetNode<SimRunner>("SimRunner").FixedFrameDelta = FrameSeconds;
        return main;
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    // Windowed with `-- --shots <dir>`: the viewport as starting-resources-<name>.png.
    private async Task Shot(string name)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = $"{_shots}/starting-resources-{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"  screenshot {path}");
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
