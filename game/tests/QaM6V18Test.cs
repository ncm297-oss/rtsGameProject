using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Rts.Sim.Save;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// M6-V18: a map file carried through a save and its load, the recording and Watch, and the Load Game list, through the shell
/// (<c>Main.tscn</c>):
/// <list type="bullet">
/// <item>A generated menu match (the setup's Generated) saves format 4 (<see cref="SaveFormat.MaplessVersion"/>) with no map
/// id after the preset, its config's map parameters as before M6-V18, and records format 6 with no <c>map</c> line.</item>
/// <item>A menu match on the map file: saved at tick <see cref="SaveTick"/> (format 5, <see cref="SaveFormat.CurrentVersion"/>,
/// the summary's <see cref="SaveSummary.MapId"/> the map's) and played on to tick <see cref="EndTick"/>; its recording is
/// format 9 with <c>map &lt;id&gt;</c> right after <c>starting-resources</c>.</item>
/// <item>Load Game: the map file's save and recording name the map (<c>load.map</c> with its <c>displayName</c>); the generated
/// pair reads exactly as <c>SeatsText</c>. Watch plays the map's recording to every checkpoint on the map; Load resumes the
/// save on the map with the saved state hash and reaches the uninterrupted match's hash at <see cref="EndTick"/>.</item>
/// <item>Hostile <c>map</c> lines (missing, empty, two tokens, <c>-</c>, 65 characters) make the header unreadable; an id the
/// data lacks lists as the id; <see cref="SimRunner.StartPlayback"/> refuses (before any sim) a recording whose map the data
/// lacks or whose size or resource capacity don't fit the map.</item>
/// </list>
/// </summary>
/// <remarks>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6V18Test.tscn</c>; prints "QA M6-V18 TEST PASS" or "QA
/// M6-V18 TEST FAIL: ..." lines. Per-process folders under <c>user://</c> (BUG-0770's rule), removed at the end.
/// </remarks>
public partial class QaM6V18Test : Node
{
    private const double FrameSeconds = 0.25; // five ticks a frame
    private const int SaveTick = 200, EndTick = SaveTick + 1000, GeneratedSave = 100, GeneratedTicks = 200;

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private UiText _ui = null!;
    private string _root = "", _replays = "", _saves = "";
    private MapDef _map = null!;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // the shell pauses the tree
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            await Frames(1);
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            _ui = UiText.Shared ?? throw new InvalidOperationException("ui.json failed to load");
            _map = _data.Maps.FirstOrDefault() ?? throw new InvalidOperationException("the data has no map file");
            _root = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://qa-m6-v18-test-{OS.GetProcessId()}"));
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            _replays = Path.Combine(_root, "replays");
            _saves = Path.Combine(_root, "saves");
            Directory.CreateDirectory(_replays);
            Directory.CreateDirectory(_saves);
            Main main = NewMain();
            AddChild(main);
            await Frames(2);
            try
            {
                await Run(main);
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
        foreach (string f in _failures.Take(60)) GD.Print($"QA M6-V18 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M6-V18 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private async Task Run(Main main)
    {
        // ---- a generated menu match: format 4 / format 6, as before ----
        Menu gen = await MenuMatch(main, "", GeneratedSave, GeneratedTicks);
        if (!Check(gen.Replay != null && gen.Save != null && File.Exists(gen.Replay) && File.Exists(gen.Save), $"generated files: {gen.Replay} / {gen.Save}")) return;
        byte[] genBytes = File.ReadAllBytes(gen.Save!);
        Check(SaveSummary.TryParse(genBytes, out SaveSummary? genSummary) && genSummary!.FormatVersion == SaveFormat.MaplessVersion && genSummary.MapId == "",
            $"the generated save is format {genSummary?.FormatVersion} (want {SaveFormat.MaplessVersion}) with map id '{genSummary?.MapId}'");
        // Right after the preset string comes the heightmap's tag: no map id string was written.
        byte[] preset = StringField(StartingResourcesDef.StandardId);
        int presetAt = IndexOf(genBytes, preset);
        Check(presetAt > 0 && genBytes.AsSpan(presetAt + preset.Length, 4).SequenceEqual("HMAP"u8), "the generated save holds something between its preset and its heightmap");
        Check(gen.Config!.MapId == "" && gen.Config.Map == MapGenParams.Default with
            {
                Width = 128, Height = 128, Forests = LaunchOptions.DefaultForests, GoldMines = LaunchOptions.DefaultMines, StartLocations = 2,
            }, $"the generated match's config: map id '{gen.Config.MapId}', params {gen.Config.Map}");
        string[] genLines = File.ReadAllLines(gen.Replay!);
        Check(genLines[0] == "rts-replay 6" && !genLines.Any(l => l.StartsWith("map ", StringComparison.Ordinal)), $"the generated recording is '{genLines[0]}' (want format 6, no map line)");
        Check(ReplayStore.TryReadHeader(gen.Replay!, out ReplayEntry? genEntry) && genEntry!.MapId == "", "the generated recording's header");

        // ---- a match on the map file: format 5 / format 9 ----
        Menu file = await MenuMatch(main, _map.Key, SaveTick, EndTick);
        if (!Check(file.Replay != null && file.Save != null && File.Exists(file.Replay) && File.Exists(file.Save), $"map files: {file.Replay} / {file.Save}")) return;
        Check(file.Config!.MapId == _map.Key && file.Config.Map == MapGenParams.Default with { Width = _map.Size, Height = _map.Size },
            $"the map match's config: '{file.Config.MapId}', params {file.Config.Map}");
        byte[] fileBytes = File.ReadAllBytes(file.Save!);
        Check(SaveSummary.TryParse(fileBytes, out SaveSummary? fileSummary) && fileSummary!.FormatVersion == SaveFormat.CurrentVersion && fileSummary.MapId == _map.Key
            && fileSummary.Tick == SaveTick && fileSummary.MapWidth == _map.Size,
            $"the map save: format {fileSummary?.FormatVersion} (want {SaveFormat.CurrentVersion}), map '{fileSummary?.MapId}', tick {fileSummary?.Tick}");
        Check(SaveStore.TryReadHeader(file.Save!, out SaveEntry? saveEntry) && saveEntry!.MapId == _map.Key, $"the map save's entry: '{saveEntry?.MapId}'");
        string[] lines = File.ReadAllLines(file.Replay!);
        int presetLine = Array.FindIndex(lines, l => l.StartsWith("starting-resources ", StringComparison.Ordinal));
        Check(lines[0] == "rts-replay 9" && presetLine > 0 && lines[presetLine + 1] == $"map {_map.Key}",
            $"the map recording: '{lines[0]}', after the preset '{(presetLine > 0 ? lines[presetLine + 1] : "-")}'");
        Check(ReplayStore.TryReadHeader(file.Replay!, out ReplayEntry? fileEntry) && fileEntry!.MapId == _map.Key, $"the map recording's header: '{fileEntry?.MapId}'");
        HostileLines(file.Replay!);
        await PlaybackGuards(file.Replay!);

        // ---- Load Game: the map named on its two rows, the generated rows as before ----
        main.ShowLoadGame();
        await Frames(2);
        LoadGameScreen load = main.LoadScreen!;
        foreach ((string path, bool named) in new[] { (file.Replay!, true), (file.Save!, true), (gen.Replay!, false), (gen.Save!, false) })
        {
            int row = load.RowOf(path);
            if (!Check(row >= 0 && row < load.RowCount, $"Load Game does not list {Path.GetFileName(path)}")) continue;
            string cell = load.List.GetRoot().GetChild(row).GetText(2);
            var seats = load.KindOf(row) == LoadGameScreen.RowKind.Save ? load.Saves.First(s => s.Path == path).Seats : load.Entries.First(e => e.Path == path).Seats;
            string want = named ? _ui.SeatsWithMap(load.SeatsText(seats), _map.DisplayName) : load.SeatsText(seats);
            Check(cell == want && cell.Contains(_map.DisplayName, StringComparison.Ordinal) == named, $"{Path.GetFileName(path)}: players cell '{cell}' (want '{want}')");
        }

        // ---- Watch the map's recording: every checkpoint, on the map ----
        int watchRow = load.RowOf(file.Replay!);
        Check(load.Select(watchRow) && !load.WatchButton.Disabled, "the map's replay row can't be watched");
        load.WatchButton.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        Match? watched = main.CurrentMatch;
        if (Check(watched is { IsPlayback: true }, "Watch started no playback"))
        {
            SimRunner runner = watched!.GetNode<SimRunner>("SimRunner");
            Check(runner.MapId == _map.Key && runner.Simulation!.World.Config.MapId == _map.Key && runner.Simulation.World.NavGrid.Width == _map.Size,
                $"playback plays map '{runner.Simulation!.World.Config.MapId}'");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (runner.PlaybackState == PlaybackState.Playing && watch.Elapsed.TotalSeconds < 240) await Frames(1);
            Replay replay = runner.Playback!;
            Check(runner.PlaybackState == PlaybackState.Ended && runner.PlaybackError == ReplayError.None && runner.CheckpointsCompared == replay.Checkpoints.Length
                && replay.Checkpoints.Length == EndTick / ReplayRecorder.DefaultCheckpointInterval,
                $"map playback {runner.PlaybackState} ({runner.PlaybackError}), {runner.CheckpointsCompared} of {replay.Checkpoints.Length} checkpoints");
            main.Banner!.MenuButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);
        }

        // ---- Load the map's save: the saved state, then the uninterrupted match's hash 1,000 ticks on ----
        main.ShowLoadGame();
        await Frames(2);
        int saveRow = load.RowOf(file.Save!);
        Check(load.Select(saveRow) && !load.LoadButton.Disabled, "the map's save row can't be loaded");
        load.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
        Match? resumed = main.CurrentMatch;
        if (Check(resumed is { IsLoaded: true } && resumed.Simulation != null, "Load resumed nothing"))
        {
            Simulation sim = resumed!.Simulation!;
            Check(sim.World.Config.MapId == _map.Key && resumed.GetNode<SimRunner>("SimRunner").MapId == _map.Key && sim.TickNumber == SaveTick && sim.StateHash() == file.SaveHash
                && resumed.StartLine.Contains($", map file {_map.Key},", StringComparison.Ordinal),
                $"the loaded match: map '{sim.World.Config.MapId}', tick {sim.TickNumber}, hash equal {sim.StateHash() == file.SaveHash}, line '{resumed.StartLine}'");
            var run = System.Diagnostics.Stopwatch.StartNew();
            while (sim.TickNumber < EndTick && run.Elapsed.TotalSeconds < 240) await Frames(1);
            Check(sim.TickNumber == EndTick && sim.StateHash() == file.EndHash,
                $"the loaded match at tick {sim.TickNumber}: hash {sim.StateHash():X16}, the uninterrupted match's {file.EndHash:X16}");
            main.QuitToMenu();
            await Frames(2);
        }
    }

    private sealed record Menu(string? Replay, string? Save, SimConfig? Config, ulong SaveHash, ulong EndHash);

    // A menu match from the setup's Start on `mapId` ("" Generated), saved from the pause menu at `saveAt` and played to
    // `ticks`, then quit: the files, the config, the state hash at the save and at the end.
    private async Task<Menu> MenuMatch(Main main, string mapId, int saveAt, int ticks)
    {
        main.ShowSetup();
        await Frames(2);
        SkirmishSetup setup = main.Setup!;
        int forestsBefore = setup.ForestsPick, minesBefore = setup.MinesPick;
        Check(setup.PickMap(mapId), $"the list has no '{mapId}'");
        int shownForests = setup.ForestsPick, shownMines = setup.MinesPick;
        if (mapId.Length > 0)
        {
            // M6-V19: the greyed counts are the map's own (Raraku: 6 forests, 12 gold mines), and Generated gives back the
            // values from before; the match's start line (the sim's placement) then says the same counts the setup showed.
            MapDef map = _data.Maps[_data.FindMap(mapId)];
            Check(shownForests == map.ForestCount && (mapId != "raraku" || (shownForests == 6 && shownMines == 12)),
                $"'{mapId}' picked: Forests {shownForests}, Gold mines {shownMines} (the file lists {map.ForestCount} forests)");
            setup.PickMap("");
            Check(setup.ForestsPick == forestsBefore && setup.MinesPick == minesBefore,
                $"Generated after '{mapId}': Forests {setup.ForestsPick}, Gold mines {setup.MinesPick} (want {forestsBefore} / {minesBefore} back)");
            setup.PickMap(mapId);
        }
        setup.StartButton.EmitSignal(BaseButton.SignalName.Pressed);
        Match? match = main.CurrentMatch;
        if (!Check(match?.Simulation != null, $"'{mapId}': Start started no match")) return new Menu(null, null, null, 0, 0);
        if (mapId.Length > 0)
            Check(match!.StartLine.Contains($"forests {shownForests} ", StringComparison.Ordinal) && match.StartLine.Contains($"mines {shownMines},", StringComparison.Ordinal),
                $"'{mapId}': the setup showed {shownForests} forests / {shownMines} mines, the start line says '{match.StartLine}'");
        Simulation sim = match!.Simulation!;
        string? replayPath = main.ReplayPath;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (sim.TickNumber < saveAt && clock.Elapsed.TotalSeconds < 120) await Frames(1);
        main.PauseMatch();
        await Frames(1);
        ulong saveHash = sim.StateHash();
        Check(sim.TickNumber == saveAt && main.SaveGame(), $"the '{mapId}' match did not save at {saveAt} (tick {sim.TickNumber})");
        string? savePath = main.LastSavePath;
        main.Resume();
        while (sim.TickNumber < ticks && clock.Elapsed.TotalSeconds < 300) await Frames(1);
        Check(sim.TickNumber == ticks, $"the '{mapId}' match stopped at tick {sim.TickNumber} (want {ticks})");
        ulong endHash = sim.StateHash();
        SimConfig config = sim.World.Config;
        main.QuitToMenu();
        await Frames(2);
        return new Menu(replayPath, savePath, config, saveHash, endHash);
    }

    // The header's map line, damaged, makes the header unreadable; an id the data lacks lists as itself.
    private void HostileLines(string replayPath)
    {
        string bad = Path.Combine(_root, "hostile");
        Directory.CreateDirectory(bad);
        string text = File.ReadAllText(replayPath);
        string line = $"map {_map.Key}\n";
        (string Name, string Text, bool Reads)[] cases =
        {
            ("missing", text.Replace(line, ""), false),
            ("empty", text.Replace(line, "map \n"), false),
            ("two-tokens", text.Replace(line, $"map {_map.Key} x\n"), false),
            ("dash", text.Replace(line, "map -\n"), false),
            ("no-key", text.Replace(line, $"{_map.Key}\n"), false),
            ("too-long", text.Replace(line, $"map {new string('a', DataLimits.MaxMapIdLength + 1)}\n"), false),
            ("unknown", text.Replace(line, "map no_such_map\n"), true),
        };
        foreach ((string name, string forged, bool reads) in cases)
        {
            string path = Path.Combine(bad, name + ReplayFormat.FileExtension);
            File.WriteAllText(path, forged);
            bool read = ReplayStore.TryReadHeader(path, out ReplayEntry? e);
            Check(forged != text && read == reads && (!reads || e!.MapId == "no_such_map"), $"hostile map line '{name}': read {read} ('{e?.MapId}')");
        }
        var load = new LoadGameScreen();
        AddChild(load);
        load.Build(_ui, _data);
        var none = System.Collections.Immutable.ImmutableArray<ReplaySeat>.Empty;
        Check(load.PlayersText(none, StartingResourcesDef.StandardId, "no_such_map") == _ui.SeatsWithMap("", "no_such_map")
            && load.PlayersText(none, StartingResourcesDef.StandardId, _map.Key) == _ui.SeatsWithMap("", _map.DisplayName)
            && load.PlayersText(none, StartingResourcesDef.StandardId, "") == "" && load.PlayersText(none, StartingResourcesDef.StandardId) == "",
            "PlayersText for an unknown, a known and no map");
        RemoveChild(load);
        load.QueueFree();
    }

    // SimRunner.StartPlayback refuses a map the data lacks, a size that isn't the map's and a capacity under its nodes, before any sim.
    private async Task PlaybackGuards(string replayPath)
    {
        if (!Check(ReplayFormat.TryReadFile(replayPath, out Replay? replay) == ReplayError.None, "the map recording does not read")) return;
        var runner = new SimRunner();
        AddChild(runner);
        foreach ((string name, Replay forged) in new[]
        {
            ("unknown map", Copy(replay!, mapId: "no_such_map")),
            ("other size", Copy(replay!, map: replay!.Map with { Width = 256, Height = 256 })),
            ("small capacity", Copy(replay!, resources: _map.Nodes.Length - 1)),
        })
        {
            ReplayError e = runner.StartPlayback(_data, forged);
            Check(e == ReplayError.InvalidHeader && runner.Simulation == null, $"StartPlayback of a recording with an {name}: {e}, sim {(runner.Simulation == null ? "none" : "built")}");
        }
        RemoveChild(runner);
        runner.QueueFree();
        await Frames(1);
    }

    private static Replay Copy(Replay r, string? mapId = null, MapGenParams? map = null, int? resources = null) => new()
    {
        FormatVersion = r.FormatVersion, Combat = r.Combat, StartingResources = r.StartingResources, MapId = mapId ?? r.MapId,
        SimVersion = r.SimVersion, Seats = r.Seats, DataHash = r.DataHash, Map = map ?? r.Map, Seed = r.Seed, PlayerCount = r.PlayerCount,
        UnitCapacity = r.UnitCapacity, CommandCapacity = r.CommandCapacity, ResourceCapacity = resources ?? r.ResourceCapacity,
        CheckpointInterval = r.CheckpointInterval, TickCount = r.TickCount, Commands = r.Commands, Checkpoints = r.Checkpoints,
    };

    private static byte[] StringField(string s)
    {
        byte[] id = System.Text.Encoding.UTF8.GetBytes(s);
        return BitConverter.GetBytes(id.Length).Concat(id).ToArray();
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        return -1;
    }

    private Main NewMain()
    {
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.UserArgs = new[] { Main.NoConfineFlag };
        main.SettingsPath = $"user://qa-m6-v18-test-{OS.GetProcessId()}/settings.cfg"; // BUG-0770: never the player's file
        main.ReplayDir = _replays;
        main.SaveDir = _saves;
        main.BeforeStart = m => m.GetNode<SimRunner>("SimRunner").FixedFrameDelta = FrameSeconds;
        return main;
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
