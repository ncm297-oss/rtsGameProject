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
/// QA 2026-11-05-1716 (M6-V17, the setup's starting-resources list and riders BUG-1065 / 1066), through the shell
/// (<c>Main.tscn</c>):
/// <list type="bullet">
/// <item><c>--resources</c> fuzz: unknown, empty, mixed case, padded, unicode and over-long values through
/// <see cref="LaunchOptions.Parse"/> and a real start: every one plays <c>standard</c> with the Standard bank; <c>high</c> the
/// preset's bank on every seat.</item>
/// <item>A High AI match from the setup's Start: saved at 0:10 (tick 200) and 3:00 (tick 3,600), played on to tick 3,700 and
/// recorded. Load Game Watch plays the recording through <see cref="SimRunner.StartPlayback"/> to every checkpoint; each save
/// loads through Load Game with the bank and the state hash it was saved with, and plays on to the uninterrupted match's hash
/// 100 / 200 ticks later.</item>
/// <item>Load Game with a format-8 High pair (FFA and a four-seat 2v2) beside Standard format-6 (FFA) and format-7 (teamed)
/// pairs: every file listed once, the High cells name the preset, the Standard ones read exactly as <c>SeatsText</c>;
/// the format-7 Standard recording still watches to the end.</item>
/// <item>BUG-1065 / 1066 at 1152 x 648 and 1280 x 720: the six teamed four-seat menu stats layouts fit; the setup's one-team
/// line (two and four slots on one team) stays inside the window, clear of Start, and Back / Start don't move; the
/// command-line one-team starts (two players, four players, three players with slot 4 elsewhere) play free-for-all.</item>
/// </list>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6V17Test.tscn</c>; prints "QA M6-V17 TEST PASS" or
/// "QA M6-V17 TEST FAIL: ..." lines. Windowed with <c>-- --shots &lt;dir&gt;</c> it saves the setups and Load Game list.
/// </summary>
public partial class QaM6V17Test : Node
{
    private const double FrameSeconds = 0.25; // five ticks a frame
    private const int EarlySave = 200, LateSave = 3600, EndTick = 3700;

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private UiText _ui = null!;
    private Main _main = null!;
    private string? _shots;
    private string _root = "", _replays = "", _saves = "";
    private StartingResourcesDef _high = null!, _standard = null!;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
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
            _high = _data.Rules.StartingResources.First(p => p.Key == "high");
            _standard = _data.Rules.StartingResources.First(p => p.Key == StartingResourcesDef.StandardId);
            _root = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://qa-m6-v17-{OS.GetProcessId()}"));
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            _replays = Path.Combine(_root, "replays");
            _saves = Path.Combine(_root, "saves");
            Directory.CreateDirectory(_replays);
            Directory.CreateDirectory(_saves);
            _main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
            _main.UserArgs = new[] { Main.NoConfineFlag };
            _main.SettingsPath = $"user://qa-m6-v17-{OS.GetProcessId()}/settings.cfg";
            _main.ReplayDir = _replays;
            _main.SaveDir = _saves;
            _main.BeforeStart = m => m.GetNode<SimRunner>("SimRunner").FixedFrameDelta = FrameSeconds;
            AddChild(_main);
            await Frames(2);
            try
            {
                await ResourcesFuzz();
                await CommandLineOneTeam();
                foreach (Vector2I size in new[] { new Vector2I(1152, 648), new Vector2I(1280, 720) })
                {
                    GetTree().Root.Size = size;
                    await Frames(2);
                    await SetupOneTeam(size, 2);
                    await SetupOneTeam(size, 4);
                    await MenuStats(size);
                }
                GetTree().Root.Size = new Vector2I(1152, 648);
                await Frames(2);
                await HighAiMatch();
                await MixedFormats();
            }
            finally
            {
                if (_main.CurrentMatch != null) _main.QuitToMenu();
            }
            try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        foreach (string f in _failures.Take(60)) GD.Print($"QA M6-V17 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M6-V17 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- --resources fuzz ----

    private async Task ResourcesFuzz()
    {
        // Parse alone: nothing but a snake_case id is taken; mixed case is junk (ids are snake_case), never folded.
        foreach (string junk in new[] { "", "High", "HIGH", " high", "high ", "hi gh", "hi-gh", "ｈigh", "high\t", "1", "_", new string('h', 65) })
        {
            LaunchOptions o = LaunchOptions.Parse(new[] { "--resources", junk, "--seed", "9" });
            string? got = o.StartingResources;
            Check(o.Seed == 9 && (got == null || got == junk) && Match.PresetFor(_data, got) == StartingResourcesDef.StandardId,
                $"--resources '{junk}': parsed '{got}', plays '{Match.PresetFor(_data, got)}', seed {o.Seed}");
        }
        // A real start for each kind of bad value: Standard bank on both seats; then high: the preset's bank.
        foreach ((string value, StartingResourcesDef want) in new[] { ("low", _standard), ("", _standard), ("High", _standard), ("STANDARD", _standard),
            ("standard", _standard), ("high", _high) })
        {
            _main.StartSkirmish(new[] { "--seed", "5", "--units", "0", "--mute", "--resources", value });
            Match? m = _main.CurrentMatch;
            if (!Check(m?.Simulation != null, $"--resources '{value}' started no match")) continue;
            World w = m!.Simulation!.World;
            bool bank = true;
            for (int p = 0; p < w.Config.PlayerCount; p++) bank &= w.Gold[p] == want.Gold && w.Wood[p] == want.Wood;
            Check(w.Config.StartingResources == want.Key && bank && m.StartLine.Contains($"starting resources {want.Key},", StringComparison.Ordinal),
                $"--resources '{value}': plays '{w.Config.StartingResources}' with bank {w.Gold[0]} / {w.Wood[0]} (want {want.Key}), line '{m.StartLine}'");
            await QuitMatch();
        }
    }

    // ---- BUG-1066: the command line ----

    private async Task CommandLineOneTeam()
    {
        foreach ((string[] args, int players, bool ffa) in new[]
        {
            (new[] { "--team", "1", "1", "--team", "2", "1" }, 2, true),
            (new[] { "--players", "4", "--team", "1", "3", "--team", "2", "3", "--team", "3", "3", "--team", "4", "3" }, 4, true),
            // slot 4 is past the match: the three players are still one side.
            (new[] { "--players", "3", "--team", "1", "2", "--team", "2", "2", "--team", "3", "2", "--team", "4", "1" }, 3, true),
            // two sides: left alone.
            (new[] { "--players", "3", "--team", "1", "2", "--team", "2", "2" }, 3, false),
        })
        {
            string how = string.Join(' ', args);
            LaunchOptions o = LaunchOptions.Parse(args);
            Check((o.OneTeamWarning.Length > 0) == ffa, $"[{how}]: warning '{o.OneTeamWarning}'");
            _main.StartSkirmish(args.Concat(new[] { "--seed", "4", "--units", "0", "--mute" }).ToArray());
            Match? m = _main.CurrentMatch;
            if (!Check(m?.Simulation != null, $"[{how}] started no match")) continue;
            World w = m!.Simulation!.World;
            bool enemies = true;
            for (int a = 0; a < players; a++)
                for (int b = 0; b < players; b++)
                    if (a != b) enemies &= w.IsEnemy(a, b);
            Check(w.Config.PlayerCount == players && (ffa ? enemies && Enumerable.Range(0, players).All(p => w.TeamOf(p) == 0) : w.TeamOf(0) == 2 && w.TeamOf(1) == 2),
                $"[{how}]: {w.Config.PlayerCount} players, teams {string.Join(",", Enumerable.Range(0, w.Config.PlayerCount).Select(w.TeamOf))}");
            // Five seconds in, still running (not over at once).
            await Ticks(m.Simulation!, 100);
            Check(!_main.BannerUp, $"[{how}]: the match ended at once");
            await QuitMatch();
        }
    }

    // ---- BUG-1066: the setup's line ----

    private async Task SetupOneTeam(Vector2I size, int slots)
    {
        string how = $"{size.X}x{size.Y}, {slots} slots";
        _main.ShowSetup();
        await Frames(2);
        SkirmishSetup setup = _main.Setup!;
        while (setup.SlotCount < slots) setup.AddSlot();
        while (setup.SlotCount > slots) setup.RemoveSlot();
        setup.PickResources(_high.Key);
        for (int s = 0; s < SkirmishSetup.MaxSlots; s++) PickTeam(setup, s, 0);
        await Frames(2);
        Rect2 back = setup.BackButton.GetGlobalRect(), start = setup.StartButton.GetGlobalRect();
        for (int s = 0; s < slots; s++) PickTeam(setup, s, 2);
        await Frames(2);
        Rect2 view = GetViewport().GetVisibleRect();
        Label line = setup.OneTeamLabel;
        Rect2 r = line.GetGlobalRect();
        Check(setup.OneTeam && setup.StartButton.Disabled && line.IsVisibleInTree(), $"{how}: one team not refused");
        Check(view.Grow(0.5f).Encloses(r), $"{how}: the one-team line {r} leaves the {view.Size} window");
        Check(!r.Intersects(setup.StartButton.GetGlobalRect()) && !r.Intersects(setup.BackButton.GetGlobalRect()), $"{how}: the line {r} overlaps Back / Start");
        // The text must fit its rect (no clipped line): its minimum height at its width within the rect.
        Check(line.GetMinimumSize().Y <= r.Size.Y + 0.5f && line.GetLineCount() <= Math.Max(1, line.GetVisibleLineCount()),
            $"{how}: the line needs {line.GetMinimumSize()} / {line.GetLineCount()} lines, has {r.Size} / {line.GetVisibleLineCount()} visible");
        Check(setup.BackButton.GetGlobalRect() == back && setup.StartButton.GetGlobalRect() == start,
            $"{how}: Back / Start moved with the line: {setup.BackButton.GetGlobalRect()} / {setup.StartButton.GetGlobalRect()} vs {back} / {start}");
        Check(Mathf.Abs(back.GetCenter().X + start.GetCenter().X - view.Size.X) <= 2f, $"{how}: Back {back} / Start {start} not centred");
        foreach (Control c in new Control[] { setup.ResourcesOption, setup.BackButton, setup.StartButton, setup.ForestsSpin, setup.MinesSpin })
            Check(view.Grow(0.5f).Encloses(c.GetGlobalRect()), $"{how}: {c.Name} {c.GetGlobalRect()} leaves the window");
        GD.Print($"  {how}: line {r} (min {line.GetMinimumSize()}, {line.GetLineCount()} lines), Back {back}, Start {start}, list {setup.ResourcesOption.GetGlobalRect()}");
        await Shot($"setup-one-team-{size.X}-{slots}");
        for (int s = 0; s < SkirmishSetup.MaxSlots; s++) PickTeam(setup, s, 0);
        while (setup.SlotCount > 2) setup.RemoveSlot();
        setup.PickResources(StartingResourcesDef.StandardId);
        _main.ShowMenu();
        await Frames(1);
    }

    // ---- BUG-1065: the six teamed four-seat menu layouts (and the menu FFA) ----

    private async Task MenuStats(Vector2I size)
    {
        foreach (int[] teams in new[] { new[] { 1, 2, 3, 0 }, new[] { 1, 2, 3, 4 }, new[] { 0, 1, 2, 3 }, new[] { 1, 1, 2, 2 }, new[] { 1, 2, 0, 0 }, new[] { 0, 0, 0, 0 } })
        {
            string how = $"{size.X}x{size.Y} menu teams {string.Join("", teams)}";
            _main.ShowSetup();
            await Frames(2);
            SkirmishSetup setup = _main.Setup!;
            while (setup.SlotCount < SkirmishSetup.MaxSlots) setup.AddSlot();
            for (int s = 0; s < SkirmishSetup.MaxSlots; s++) PickTeam(setup, s, teams[s]);
            setup.PickResources(_high.Key);
            await Frames(1);
            setup.StartButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(3);
            if (!Check(_main.CurrentMatch?.Simulation != null, $"{how}: Start started no match")) continue;
            _main.CurrentMatch!.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
            _main.PauseMatch();
            await Frames(1);
            _main.ShowStats();
            await Frames(2);
            MatchStats stats = _main.Stats!;
            Rect2 view = GetViewport().GetVisibleRect();
            foreach (string f in StatsScreenCheck.Fits(stats, view, how)) Check(false, f);
            Check(stats.OpeningLines.Count == 3, $"{how}: {stats.OpeningLines.Count} opened-with lines");
            Rect2 b = stats.BackButton.GetGlobalRect();
            Check(b.End.Y <= view.End.Y - 4f, $"{how}: Back {b} within 4 px of the bottom");
            GD.Print($"  {how}: Back {b}, graph {stats.Graph.GetGlobalRect()}");
            if (teams[0] == 1 && teams[1] == 2 && teams[2] == 3) await Shot($"stats-{size.X}-{string.Join("", teams)}");
            _main.CloseStats();
            await Frames(2);
            await QuitMatch();
        }
        SkirmishSetup reset = _main.Setup!;
        for (int s = 0; s < SkirmishSetup.MaxSlots; s++) PickTeam(reset, s, 0);
        while (reset.SlotCount > 2) reset.RemoveSlot();
        reset.PickResources(StartingResourcesDef.StandardId);
    }

    // ---- a High AI match: saves at 0:10 and 3:00, Watch, Load ----

    private async Task HighAiMatch()
    {
        _main.ShowSetup();
        await Frames(2);
        SkirmishSetup setup = _main.Setup!;
        Check(setup.PickResources(_high.Key), "no high in the list");
        setup.StartButton.EmitSignal(BaseButton.SignalName.Pressed);
        Match? match = _main.CurrentMatch;
        if (!Check(match?.Simulation != null, "High: Start started no match")) return;
        Simulation sim = match!.Simulation!;
        Check(match.Seats.Any(s => s.Controller == SeatController.Ai), "the menu match has no AI seat");
        string? replayPath = _main.ReplayPath;
        var hashes = new Dictionary<int, ulong>();
        var saves = new List<(int Tick, string Path, int[] Bank, ulong Hash)>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        int[] marks = { EarlySave, EarlySave + 200, LateSave, EndTick };
        foreach (int mark in marks)
        {
            while (sim.TickNumber < mark && clock.Elapsed.TotalSeconds < 600) await Frames(1);
            _main.PauseMatch();
            await Frames(1);
            Check(sim.TickNumber == mark, $"High: paused at {sim.TickNumber} (want {mark})");
            hashes[sim.TickNumber] = sim.StateHash();
            if (mark is EarlySave or LateSave)
            {
                int[] bank = Bank(sim.World);
                ulong hash = sim.StateHash();
                if (Check(_main.SaveGame() && _main.LastSavePath != null, $"High: no save at {mark}"))
                    saves.Add((mark, _main.LastSavePath!, bank, hash));
                GD.Print($"  High tick {mark}: bank [{string.Join(", ", bank)}]");
            }
            if (mark != EndTick) _main.Resume();
        }
        int[] endBank = Bank(sim.World);
        Check(endBank.Take(sim.World.Config.PlayerCount).Any(g => g != _high.Gold), "High: the bank never moved");
        _main.QuitToMenu();
        await Frames(2);
        if (!Check(replayPath != null && File.Exists(replayPath), $"High: no recording at {replayPath}")) return;
        string[] lines = File.ReadAllLines(replayPath!);
        Check(lines[0] == "rts-replay 8" && lines.Contains($"starting-resources {_high.Key}"), $"High recording: '{lines[0]}'");

        // Watch through Load Game (StartPlayback): every checkpoint.
        _main.ShowLoadGame();
        await Frames(2);
        LoadGameScreen load = _main.LoadScreen!;
        int row = load.RowOf(replayPath!);
        if (Check(row >= 0 && load.Select(row) && !load.WatchButton.Disabled, "High: the recording can't be watched"))
        {
            load.WatchButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);
            Match? watched = _main.CurrentMatch;
            if (Check(watched is { IsPlayback: true }, "High: Watch started no playback"))
            {
                SimRunner runner = watched!.GetNode<SimRunner>("SimRunner");
                runner.FixedFrameDelta = FrameSeconds * 4; // 20 ticks a frame
                var w = System.Diagnostics.Stopwatch.StartNew();
                while (runner.PlaybackState == PlaybackState.Playing && w.Elapsed.TotalSeconds < 600) await Frames(1);
                Replay replay = runner.Playback!;
                Check(runner.PlaybackState == PlaybackState.Ended && runner.PlaybackError == ReplayError.None && runner.CheckpointsCompared == replay.Checkpoints.Length
                    && replay.TickCount == EndTick && replay.Checkpoints.Length > 0 && runner.Simulation!.StateHash() == hashes[EndTick],
                    $"High Watch: {runner.PlaybackState} ({runner.PlaybackError}) {runner.CheckpointsCompared}/{replay.Checkpoints.Length} checkpoints, {replay.TickCount} ticks, end hash equal {runner.Simulation?.StateHash() == hashes[EndTick]}");
                GD.Print($"  High Watch: {runner.CheckpointsCompared}/{replay.Checkpoints.Length} checkpoints, {replay.Commands.Length} commands");
                if (_main.BannerUp) _main.Banner!.MenuButton.EmitSignal(BaseButton.SignalName.Pressed);
                else _main.QuitToMenu();
                await Frames(2);
            }
        }

        // Load each save through Load Game: the bank and hash at the save, then the uninterrupted match's hash later.
        foreach ((int tick, string path, int[] bank, ulong hash) in saves)
        {
            _main.ShowLoadGame();
            await Frames(2);
            int saveRow = load.RowOf(path);
            if (!Check(saveRow >= 0 && load.Select(saveRow) && !load.LoadButton.Disabled, $"High save {tick}: not loadable")) continue;
            string cell = load.List.GetRoot().GetChild(saveRow).GetText(2);
            Check(cell.Contains(_high.DisplayName, StringComparison.Ordinal), $"High save {tick}: cell '{cell}' lacks the preset");
            load.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
            Match? m = _main.CurrentMatch;
            if (!Check(m is { IsLoaded: true } && m.Simulation != null, $"High save {tick}: Load resumed nothing")) continue;
            Simulation s = m!.Simulation!;
            Check(s.TickNumber == tick && Bank(s.World).SequenceEqual(bank) && s.StateHash() == hash && s.World.Config.StartingResources == _high.Key,
                $"High save {tick}: tick {s.TickNumber}, bank [{string.Join(", ", Bank(s.World))}] vs [{string.Join(", ", bank)}], hash equal {s.StateHash() == hash}, preset {s.World.Config.StartingResources}");
            int later = tick == EarlySave ? EarlySave + 200 : EndTick;
            var c = System.Diagnostics.Stopwatch.StartNew();
            while (s.TickNumber < later && c.Elapsed.TotalSeconds < 120) await Frames(1);
            _main.PauseMatch();
            await Frames(1);
            Check(s.TickNumber == later && s.StateHash() == hashes[later], $"High save {tick}: at tick {s.TickNumber} hash equal to the uninterrupted run {s.StateHash() == hashes.GetValueOrDefault(later)}");
            if (tick == EarlySave)
            {
                // A save of the loaded High match is High too, and loads (by --load's path) with its bank.
                int[] again = Bank(s.World);
                ulong againHash = s.StateHash();
                if (Check(_main.SaveGame() && _main.LastSavePath != null, "High: the loaded match did not save"))
                {
                    string resaved = _main.LastSavePath!;
                    using (FileStream fs = File.OpenRead(resaved))
                        Check(SaveSummary.TryRead(fs, out SaveSummary? sum) && sum!.StartingResources == _high.Key, "High: the re-save's header lost the preset");
                    await QuitMatch();
                    Check(_main.LoadSavedGame(resaved), "High: --load of the re-save refused");
                    Simulation r = _main.CurrentMatch!.Simulation!;
                    Check(Bank(r.World).SequenceEqual(again) && r.StateHash() == againHash && r.World.Config.StartingResources == _high.Key,
                        $"High re-save: bank [{string.Join(", ", Bank(r.World))}] vs [{string.Join(", ", again)}], hash equal {r.StateHash() == againHash}");
                }
            }
            await QuitMatch();
        }
    }

    // ---- Load Game: format 6 / 7 Standard beside format 8 High ----

    private async Task MixedFormats()
    {
        var files = new List<(string Replay, string Save, string Preset, int Format, string How)>();
        foreach ((string preset, int[] teams, int format, string how) in new[]
        {
            (StartingResourcesDef.StandardId, new[] { 0, 0 }, 6, "standard ffa"),
            (StartingResourcesDef.StandardId, new[] { 1, 2, 0 }, 7, "standard teamed"),
            (_high.Key, new[] { 0, 0 }, 8, "high ffa"),
            (_high.Key, new[] { 1, 1, 2, 2 }, 8, "high 2v2"),
        })
        {
            _main.ShowSetup();
            await Frames(2);
            SkirmishSetup setup = _main.Setup!;
            while (setup.SlotCount < teams.Length) setup.AddSlot();
            while (setup.SlotCount > teams.Length) setup.RemoveSlot();
            for (int s = 0; s < SkirmishSetup.MaxSlots; s++) PickTeam(setup, s, s < teams.Length ? teams[s] : 0);
            setup.PickResources(preset);
            await Frames(1);
            setup.StartButton.EmitSignal(BaseButton.SignalName.Pressed);
            Match? m = _main.CurrentMatch;
            if (!Check(m?.Simulation != null, $"{how}: no match")) continue;
            string? replay = _main.ReplayPath;
            await Ticks(m!.Simulation!, 100);
            _main.PauseMatch();
            await Frames(1);
            Check(_main.SaveGame(), $"{how}: no save");
            string? save = _main.LastSavePath;
            _main.Resume();
            await Ticks(m.Simulation!, 50);
            await QuitMatch();
            if (replay == null || save == null || !File.Exists(replay)) { Check(false, $"{how}: files {replay} / {save}"); continue; }
            string head = File.ReadLines(replay).First();
            Check(head == $"rts-replay {format}", $"{how}: recording '{head}' (want format {format})");
            files.Add((replay, save, preset, format, how));
        }
        _main.ShowLoadGame();
        await Frames(2);
        LoadGameScreen load = _main.LoadScreen!;
        Check(load.Unreadable.Count == 0 && load.UnreadableSaves.Count == 0, $"unreadable rows: {string.Join(", ", load.Unreadable.Concat(load.UnreadableSaves).Select(Path.GetFileName))}");
        foreach ((string replay, string save, string preset, int format, string how) in files)
            foreach (string path in new[] { replay, save })
            {
                int row = load.RowOf(path);
                if (!Check(row >= 0, $"{how}: {Path.GetFileName(path)} not listed")) continue;
                string cell = load.List.GetRoot().GetChild(row).GetText(2);
                var seats = load.KindOf(row) == LoadGameScreen.RowKind.Save ? load.Saves.First(s => s.Path == path).Seats : load.Entries.First(e => e.Path == path).Seats;
                string want = preset == StartingResourcesDef.StandardId ? load.SeatsText(seats) : _ui.SeatsWithPreset(load.SeatsText(seats), _high.DisplayName);
                Check(cell == want, $"{how}: {Path.GetFileName(path)} cell '{cell}' (want '{want}')");
                GD.Print($"  {how} {(load.KindOf(row) == LoadGameScreen.RowKind.Save ? "save" : "replay")}: '{cell}'");
            }
        await Shot("load-mixed");
        // The format-7 Standard recording still plays to the end (the format-8 reader didn't break 7).
        var teamed = files.FirstOrDefault(f => f.Format == 7);
        if (teamed.Replay != null && load.Select(load.RowOf(teamed.Replay)))
        {
            load.WatchButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);
            if (Check(_main.CurrentMatch is { IsPlayback: true }, "format 7: Watch started no playback"))
            {
                SimRunner runner = _main.CurrentMatch!.GetNode<SimRunner>("SimRunner");
                var w = System.Diagnostics.Stopwatch.StartNew();
                while (runner.PlaybackState == PlaybackState.Playing && w.Elapsed.TotalSeconds < 120) await Frames(1);
                Check(runner.PlaybackState == PlaybackState.Ended && runner.PlaybackError == ReplayError.None && runner.CheckpointsCompared == runner.Playback!.Checkpoints.Length,
                    $"format 7 Watch: {runner.PlaybackState} ({runner.PlaybackError})");
                if (_main.BannerUp) _main.Banner!.MenuButton.EmitSignal(BaseButton.SignalName.Pressed);
                else _main.QuitToMenu();
                await Frames(2);
            }
        }
        _main.ShowMenu();
        await Frames(1);
    }

    // ---- helpers ----

    private static int[] Bank(World w) => w.Gold.ToArray().Concat(w.Wood.ToArray()).ToArray();

    private static void PickTeam(SkirmishSetup setup, int slot, int team)
    {
        setup.TeamOptions[slot].Select(team);
        setup.TeamOptions[slot].EmitSignal(OptionButton.SignalName.ItemSelected, (long)team);
    }

    private async Task QuitMatch()
    {
        if (_main.BannerUp) _main.Banner!.MenuButton.EmitSignal(BaseButton.SignalName.Pressed);
        else _main.QuitToMenu();
        await Frames(2);
        Check(_main.CurrentMatch == null && !GetTree().Paused, "quit to menu");
    }

    private async Task Ticks(Simulation sim, int n)
    {
        int until = sim.TickNumber + n;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (sim.TickNumber < until && clock.Elapsed.TotalSeconds < 60) await Frames(1);
        Check(sim.TickNumber >= until, $"tick {sim.TickNumber} after 60 s (want {until})");
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string name)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = $"{_shots}/qa-m6-v17-{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"  screenshot {path}");
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
