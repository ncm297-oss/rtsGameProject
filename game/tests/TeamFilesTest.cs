using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
/// M6-V16: the view's readers of the sim's team field (M6-S4: replay format 7, save format 3), through the shell.
/// <list type="bullet">
/// <item>Two fixture files side by side (<c>res://tests/fixtures/teams</c>: a format-6 1v1 and a format-7 2v2, header-valid
/// with a sealed checksum): <see cref="ReplayStore.TryReadHeader"/> reads both (format 6's four-token seats as team 0), agreeing
/// with the sim's full reader; Load Game lists both, the format-7 players cell with "(T1)" / "(T2)" marks, the format-6 one
/// with none. Hostile seat lines (a team of 9, "01", "+1", a missing or an extra token) make the header unreadable.</item>
/// <item>A 2v2 (<c>--skirmish --players 4</c>, slots 1 + 2 on team 1, 3 + 4 on team 2, three AI seats) saved from the pause menu
/// at tick 300 and recorded to tick 600: the recording is format 7 with the teams; Load Game lists the save and the replay
/// with the marks; Watch plays the replay to every checkpoint with the teams (<c>World.TeamOf</c> 1, 1, 2, 2); Load resumes
/// the save with the teams intact.</item>
/// </list>
/// </summary>
/// <remarks>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/TeamFilesTest.tscn</c>; prints "TEAM FILES TEST PASS" or
/// "TEAM FILES TEST FAIL: ..." lines. Per-process folders under <c>user://</c> (BUG-0770's rule), removed at the end. Windowed
/// with <c>-- --shots &lt;dir&gt;</c> it also saves the Load Game list (team-files-load-game.png).
/// </remarks>
public partial class TeamFilesTest : Node
{
    /// <summary>Game time each frame advances (<see cref="SimRunner.FixedFrameDelta"/>): five ticks.</summary>
    public const double FrameSeconds = 0.25;

    private const int SaveTick = 300, RecordTicks = 600;
    private const string Format6 = "20261027-100000-seed6.replay", Format7 = "20261027-100100-seed7.replay";
    private static readonly int[] Teams = { 1, 1, 2, 2 };

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private UiText _ui = null!;
    private string _root = "", _replays = "", _saves = "";
    private string? _shots;

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
            _root = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://team-files-test-{OS.GetProcessId()}"));
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            _replays = Path.Combine(_root, "replays");
            _saves = Path.Combine(_root, "saves");
            Directory.CreateDirectory(_replays);
            Directory.CreateDirectory(_saves);
            Fixtures();
            Hostile();
            await TwoVersusTwo();
            try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        foreach (string f in _failures.Take(60)) GD.Print($"TEAM FILES TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("TEAM FILES TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- the fixtures: format 6 and format 7 headers ----

    private void Fixtures()
    {
        foreach (string name in new[] { Format6, Format7 })
            File.Copy(ProjectSettings.GlobalizePath($"res://tests/fixtures/teams/{name}"), Path.Combine(_replays, name));
        string six = Path.Combine(_replays, Format6), seven = Path.Combine(_replays, Format7);
        Check(ReplayStore.TryReadHeader(six, out ReplayEntry? e6) && e6!.Seats.Length == 2 && e6.Seats.All(s => s.Team == 0) && e6.Seed == 6 && e6.TickCount == 60,
            $"format 6 header: {e6?.Seats.Length} seats [{(e6 == null ? "" : string.Join(", ", e6.Seats))}]");
        Check(ReplayStore.TryReadHeader(seven, out ReplayEntry? e7) && e7!.Seats.Select(s => s.Team).SequenceEqual(new[] { 1, 2, 1, 2 }) && e7.Seed == 7,
            $"format 7 header: seats [{(e7 == null ? "" : string.Join(", ", e7.Seats))}]");
        // The sim's full reader agrees (both files are whole and sealed: only their data hash is foreign).
        foreach ((string path, ReplayEntry? entry) in new[] { (six, e6), (seven, e7) })
        {
            ReplayError read = ReplayFormat.TryReadFile(path, out Replay? full);
            Check(read == ReplayError.None && entry != null && full!.Seats.SequenceEqual(entry.Seats), $"{Path.GetFileName(path)}: the sim reads {read}, seats [{(full == null ? "" : string.Join(", ", full.Seats))}]");
        }
        Check(ReplayFormat.TryReadFile(seven, out Replay? r7) == ReplayError.None && r7!.FormatVersion == 7, "the format-7 fixture is not format 7");
    }

    private void Hostile()
    {
        string bad = Path.Combine(_root, "hostile");
        Directory.CreateDirectory(bad);
        string seven = File.ReadAllText(Path.Combine(_replays, Format7)), six = File.ReadAllText(Path.Combine(_replays, Format6));
        (string Name, string Text)[] cases =
        {
            ("team-9", seven.Replace("seat 1 whirlwind ai easy 2\n", "seat 1 whirlwind ai easy 9\n")),
            ("team-01", seven.Replace("seat 1 whirlwind ai easy 2\n", "seat 1 whirlwind ai easy 02\n")),
            ("team-plus", seven.Replace("seat 1 whirlwind ai easy 2\n", "seat 1 whirlwind ai easy +2\n")),
            ("team-missing", seven.Replace("seat 1 whirlwind ai easy 2\n", "seat 1 whirlwind ai easy\n")),
            ("team-extra", seven.Replace("seat 1 whirlwind ai easy 2\n", "seat 1 whirlwind ai easy 2 2\n")),
            ("format6-five-tokens", six.Replace("seat 1 whirlwind ai hard\n", "seat 1 whirlwind ai hard 1\n")),
        };
        foreach ((string name, string text) in cases)
        {
            string path = Path.Combine(bad, name + ReplayFormat.FileExtension);
            File.WriteAllText(path, text);
            Check(text != seven && text != six && !ReplayStore.TryReadHeader(path, out _), $"hostile seat line '{name}' read as a header");
        }
        // The edge teams read: 0 and 8 (PlayerSeat.MaxTeam).
        string edge = Path.Combine(bad, "edge" + ReplayFormat.FileExtension);
        File.WriteAllText(edge, seven.Replace("seat 1 whirlwind ai easy 2\n", "seat 1 whirlwind ai easy 8\n").Replace("seat 2 malazan ai normal 1\n", "seat 2 malazan ai normal 0\n"));
        Check(ReplayStore.TryReadHeader(edge, out ReplayEntry? e) && e!.Seats.Select(s => s.Team).SequenceEqual(new[] { 1, 8, 0, 2 }), "teams 8 and 0 did not read");
    }

    // ---- a 2v2: saved, recorded, listed, watched, loaded ----

    private async Task TwoVersusTwo()
    {
        Main main = NewMain();
        AddChild(main);
        await Frames(2);
        string? replayPath = null, savePath = null;
        try
        {
            main.StartSkirmish(new[] { "--skirmish", "--players", "4", "--seed", "5", "--mute", "--team", "1", "1", "--team", "2", "1", "--team", "3", "2", "--team", "4", "2" });
            Match? match = main.CurrentMatch;
            if (!Check(match?.Simulation != null, "the 2v2 did not start")) return;
            Simulation sim = match!.Simulation!;
            Check(Enumerable.Range(0, 4).Select(sim.World.TeamOf).SequenceEqual(Teams), "the live 2v2's teams");
            replayPath = main.ReplayPath;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (sim.TickNumber < SaveTick && clock.Elapsed.TotalSeconds < 60) await Frames(1);
            main.PauseMatch();
            await Frames(1);
            int savedAt = sim.TickNumber;
            Check(main.SaveGame(), "the 2v2 did not save");
            savePath = main.LastSavePath;
            main.Resume();
            while (sim.TickNumber < RecordTicks && clock.Elapsed.TotalSeconds < 120) await Frames(1);
            Check(sim.TickNumber == RecordTicks, $"the 2v2 stopped at tick {sim.TickNumber}");
            main.QuitToMenu();
            await Frames(2);
            if (!Check(replayPath != null && File.Exists(replayPath) && savePath != null && File.Exists(savePath), $"files: replay {replayPath}, save {savePath}")) return;

            // The recording: format 7, every seat line with its team.
            string[] lines = File.ReadAllLines(replayPath!);
            Check(lines[0] == "rts-replay 7", $"the 2v2 recording is '{lines[0]}' (want format 7)");
            string[] seatLines = lines.Where(l => l.StartsWith("seat ", StringComparison.Ordinal)).ToArray();
            Check(seatLines.Length == 4 && seatLines.Select((l, p) => l.EndsWith($" {Teams[p]}", StringComparison.Ordinal) && l.Split(' ').Length == 6).All(b => b),
                $"seat lines [{string.Join(" | ", seatLines)}]");
            // The save's header: format 3's team bytes.
            using (FileStream stream = File.OpenRead(savePath!))
                Check(SaveSummary.TryRead(stream, out SaveSummary? summary) && summary!.Teams.SequenceEqual(Teams) && summary.Tick == savedAt,
                    "the save's summary has no teams");

            // Load Game: the fixtures and the 2v2's files side by side.
            main.ShowLoadGame();
            await Frames(2);
            LoadGameScreen load = main.LoadScreen!;
            CheckRow(load, Path.Combine(_replays, Format6), new[] { "malazan", "whirlwind" }, new[] { "you", "hard" }, new[] { 0, 0 });
            CheckRow(load, Path.Combine(_replays, Format7), new[] { "malazan", "whirlwind", "malazan", "whirlwind" }, new[] { "you", "easy", "normal", "hard" }, new[] { 1, 2, 1, 2 });
            ImmutableArray<PlayerSeat> seats = match.Seats;
            string[] factions = seats.Select(s => _data.Factions[s.Faction].Key).ToArray();
            string[] kinds = { "you", "normal", "normal", "normal" };
            CheckRow(load, replayPath!, factions, kinds, Teams);
            CheckRow(load, savePath!, factions, kinds, Teams);
            Check(!load.SeatsText(load.Entries.First(e => e.Path.EndsWith(Format6, StringComparison.Ordinal)).Seats).Contains("(T", StringComparison.Ordinal),
                "the format-6 row shows a team mark");
            // BUG-1062: a four-seat players cell wraps, so seats 3 and 4's "(T2)" show instead of running past the column.
            TreeItem? fourSeats = load.List.GetRoot()?.GetChild(load.RowOf(replayPath!));
            Check(fourSeats != null && fourSeats.GetAutowrapMode(2) != TextServer.AutowrapMode.Off, "the 2v2 row's players cell does not wrap");
            await Shot("load-game");

            // Watch the 2v2 through the screen: every checkpoint equal, with the teams.
            int row = load.RowOf(replayPath!);
            Check(load.Select(row) && !load.WatchButton.Disabled, "the 2v2 replay row can't be watched");
            load.WatchButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);
            Match? watched = main.CurrentMatch;
            if (Check(watched is { IsPlayback: true }, "Watch started no playback"))
            {
                SimRunner runner = watched!.GetNode<SimRunner>("SimRunner");
                World pw = runner.Simulation!.World;
                Check(Enumerable.Range(0, 4).Select(pw.TeamOf).SequenceEqual(Teams) && runner.Seats.Select(s => s.Team).SequenceEqual(Teams)
                    && runner.PlayedSeats.Select(s => s.Team).SequenceEqual(Teams) && runner.Seats.All(s => s.Controller == SeatController.Human),
                    $"playback seats [{string.Join(", ", runner.Seats)}], played [{string.Join(", ", runner.PlayedSeats)}]");
                var watch = System.Diagnostics.Stopwatch.StartNew();
                while (runner.PlaybackState == PlaybackState.Playing && watch.Elapsed.TotalSeconds < 120) await Frames(1);
                Replay replay = runner.Playback!;
                Check(runner.PlaybackState == PlaybackState.Ended && runner.PlaybackError == ReplayError.None && runner.CheckpointsCompared == replay.Checkpoints.Length
                    && replay.Checkpoints.Length == RecordTicks / ReplayRecorder.DefaultCheckpointInterval,
                    $"2v2 playback {runner.PlaybackState} ({runner.PlaybackError}), {runner.CheckpointsCompared} of {replay.Checkpoints.Length} checkpoints");
                main.Banner!.MenuButton.EmitSignal(BaseButton.SignalName.Pressed);
                await Frames(2);
            }

            // Load the save through the screen: the teams come back.
            main.ShowLoadGame();
            await Frames(2);
            row = load.RowOf(savePath!);
            Check(load.Select(row) && !load.LoadButton.Disabled, "the 2v2 save row can't be loaded");
            load.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);
            Match? resumed = main.CurrentMatch;
            if (Check(resumed is { IsLoaded: true } && resumed.Simulation != null, "Load resumed nothing"))
            {
                World lw = resumed!.Simulation!.World;
                Check(Enumerable.Range(0, 4).Select(lw.TeamOf).SequenceEqual(Teams) && lw.AreAllied(0, 1) && !lw.AreAllied(0, 2) && resumed.Seats.Select(s => s.Team).SequenceEqual(Teams),
                    $"the loaded 2v2: TeamOf [{string.Join(", ", Enumerable.Range(0, 4).Select(lw.TeamOf))}], seats [{string.Join(", ", resumed.Seats)}]");
                int from = resumed.Simulation.TickNumber;
                var run = System.Diagnostics.Stopwatch.StartNew();
                while (resumed.Simulation.TickNumber < from + 20 && run.Elapsed.TotalSeconds < 30) await Frames(1);
                // Two frames ran since the Load (five ticks each): the match resumed at the saved tick and ran on.
                Check(from >= savedAt && from <= savedAt + 10 && resumed.Simulation.TickNumber >= from + 20,
                    $"the loaded 2v2 at {from} two frames after the Load (saved at {savedAt}), ran to {resumed.Simulation.TickNumber}");
                main.QuitToMenu();
                await Frames(2);
            }
        }
        finally
        {
            if (main.CurrentMatch != null) main.QuitToMenu();
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
    }

    // The Load Game row of `path`: its players cell is each seat's load.seat text (the faction's name, You or the difficulty),
    // wrapped in load.seat_team for a seat on a team, joined by the separator.
    private void CheckRow(LoadGameScreen load, string path, string[] factions, string[] kinds, int[] teams)
    {
        int row = load.RowOf(path);
        if (!Check(row >= 0 && row < load.RowCount, $"Load Game does not list {Path.GetFileName(path)}")) return;
        string cell = load.List.GetRoot().GetChild(row).GetText(2);
        var parts = new List<string>();
        for (int p = 0; p < factions.Length; p++)
        {
            string kind = kinds[p] switch
            {
                "you" => _ui.Setup(SetupText.SlotHuman),
                "easy" => _ui.Setup(SetupText.DifficultyEasy),
                "hard" => _ui.Setup(SetupText.DifficultyHard),
                _ => _ui.Setup(SetupText.DifficultyNormal),
            };
            string seat = _ui.ReplaySeat(_data.Factions[_data.FindFaction(factions[p])].DisplayName, kind);
            parts.Add(teams[p] > 0 ? _ui.SeatOnTeam(seat, teams[p]) : seat);
        }
        string want = string.Join(_ui.LoadGame(LoadText.SeatSeparator), parts);
        Check(cell == want, $"{Path.GetFileName(path)}: players cell '{cell}' (want '{want}')");
        int marks = teams.Count(t => t > 0);
        Check(cell.Split("(T").Length - 1 == marks, $"{Path.GetFileName(path)}: {cell.Split("(T").Length - 1} team marks (want {marks})");
    }

    private Main NewMain()
    {
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.UserArgs = new[] { Main.NoConfineFlag };
        main.SettingsPath = $"user://team-files-test-{OS.GetProcessId()}/settings.cfg"; // BUG-0770: never the player's file
        main.ReplayDir = _replays;
        main.SaveDir = _saves;
        main.BeforeStart = m => m.GetNode<SimRunner>("SimRunner").FixedFrameDelta = FrameSeconds;
        return main;
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    // Windowed with `-- --shots <dir>` (M6-V16): the viewport as team-files-<name>.png (the Load Game rows with "(T1)").
    private async Task Shot(string name)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = $"{_shots}/team-files-{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"  screenshot {path}");
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
