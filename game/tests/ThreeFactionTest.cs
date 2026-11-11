using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Replays;

namespace Rts.Game.Tests;

/// <summary>
/// M7-V3a: the view with a third faction, on a forged copy of the data (<see cref="ThirdFactionForge"/>: faction
/// <c>third</c>, "Forged Third Host", appended to the order; the worktree's data untouched), through the real shell
/// (<see cref="Main.DataPath"/> pointed at the copy).
/// <list type="bullet">
/// <item>The skirmish setup: every slot's faction list has the three factions in the data's order by their data names; the
/// four slots default to faction <c>s mod 3</c>, so an added third slot is the third faction; a slot can pick it (the
/// local slot here), and Start passes <c>--faction 1 third</c>.</item>
/// <item>The 3-player match (You: third, AI: Whirlwind, AI: Malazan) starts with those seats and runs 600 ticks (the scene
/// loop fails any ERROR line); the third faction's hall, worker, production card, build menu and their tooltips read its
/// data names and descriptions; the stats screen lists it by its data name.</item>
/// <item>A save made at tick 300 loads at that tick with the same state hash and reaches the live match's hash at 600; the
/// recording replays to its end with every checkpoint equal.</item>
/// <item><c>--faction 1 third</c> / <c>--faction 2 third</c> on the command line seat it.</item>
/// </list>
/// Windowed with <c>-- --shots &lt;dir&gt;</c> it saves the setup, its open faction list and the running match.
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/ThreeFactionTest.tscn</c>; prints "THREE FACTION TEST PASS" or FAIL lines.
/// </summary>
public partial class ThreeFactionTest : Node
{
    private const string ThirdName = "Forged Third Host";
    private const double FrameSeconds = 0.25; // 5 ticks a frame
    private const int SaveTick = 300, EndTick = 600;

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private string _copy = "", _root = "";
    private string? _shots;
    private int _third;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _root = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://three-faction-test-{OS.GetProcessId()}"));
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            string[] user = OS.GetCmdlineUserArgs();
            for (int i = 0; i + 1 < user.Length; i++)
                if (user[i] == "--shots") _shots = user[i + 1];
            await Frames(1);
            _data = ThirdFactionForge.Forge(ProjectSettings.GlobalizePath("res://data"), _root, ThirdName, out _copy);
            _third = _data.FindFaction(ThirdFactionForge.Id);
            Check(_data.Factions.Length == 3 && _third == 2 && _data.Factions[0].Key == "malazan" && _data.Factions[1].Key == "whirlwind",
                $"forged factions: {string.Join(", ", _data.Factions.Select(f => f.Key))}");
            await SetupAndMatch();
            await CommandLine();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        foreach (string f in _failures.Take(60)) GD.Print($"THREE FACTION TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("THREE FACTION TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private Main NewMain(params string[] args)
    {
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.UserArgs = args.Append(Main.NoConfineFlag).ToArray();
        main.DataPath = _copy;
        main.SettingsPath = Path.Combine(_root, "settings.cfg");
        main.ReplayDir = Path.Combine(_root, "replays");
        main.SaveDir = Path.Combine(_root, "saves");
        main.BeforeStart = m => m.GetNode<SimRunner>("SimRunner").FixedFrameDelta = FrameSeconds;
        return main;
    }

    // ---- the setup, the match, its save and its recording ----

    private async Task SetupAndMatch()
    {
        Main main = NewMain();
        AddChild(main);
        await Frames(2);
        try
        {
            if (!Check(main.Setup != null, "the shell built no setup screen (ui.json?)")) return;
            main.ShowSetup();
            await Frames(2);
            SkirmishSetup setup = main.Setup!;
            for (int slot = 0; slot < SkirmishSetup.MaxSlots; slot++)
            {
                OptionButton list = setup.FactionOptions[slot];
                string[] items = Enumerable.Range(0, list.ItemCount).Select(i => list.GetItemText(i)).ToArray();
                string[] want = _data.Factions.Select(f => f.DisplayName).ToArray();
                Check(items.SequenceEqual(want), $"slot {slot} faction list [{string.Join(", ", items)}] (want the data's [{string.Join(", ", want)}])");
                Check(setup.FactionPick(slot) == slot % _data.Factions.Length, $"slot {slot} defaults to faction {setup.FactionPick(slot)} (want {slot % _data.Factions.Length})");
            }
            Check(setup.FactionOptions[0].GetItemText(_third) == ThirdName, $"the third item reads '{setup.FactionOptions[0].GetItemText(_third)}'");
            Check(setup.SlotCount == 2, $"the setup opens with {setup.SlotCount} slots");
            setup.AddSlot();
            Check(setup.SlotCount == 3 && setup.FactionPick(2) == _third && setup.FactionOptions[2].Visible,
                $"the added third slot: count {setup.SlotCount}, faction {setup.FactionPick(2)} (want {_third}, s mod factions)");
            // The local slot picks the third faction (as a click on its item); slot 3 takes Malazan, so one seat per faction.
            Pick(setup.FactionOptions[0], _third);
            Pick(setup.FactionOptions[2], 0);
            // An AI slot can pick it too: its difficulty tips follow the slot's faction (they read its ai.json) and stay filled.
            Pick(setup.FactionOptions[1], _third);
            Check(setup.FactionPick(1) == _third && setup.DifficultyOptions[1]!.GetItemTooltip(2).Length > 0, "slot 2 could not pick the third faction");
            Pick(setup.FactionOptions[1], 1);
            string[]? args = setup.MatchArgs();
            string joined = args == null ? "" : string.Join(" ", args);
            Check(joined.Contains("--players 3", StringComparison.Ordinal) && joined.Contains("--faction 1 third", StringComparison.Ordinal)
                && joined.Contains("--faction 2 whirlwind", StringComparison.Ordinal) && joined.Contains("--faction 3 malazan", StringComparison.Ordinal),
                $"Start's args: '{joined}'");
            await Shot("three-faction-setup");
            if (_shots != null && DisplayServer.GetName() != "headless")
            {
                setup.FactionOptions[0].ShowPopup();
                await Shot("three-faction-setup-list");
                setup.FactionOptions[0].GetPopup().Hide();
                await Frames(1);
            }

            setup.StartButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(1);
            Match? match = main.CurrentMatch;
            if (!Check(match?.Simulation != null, "Start started no match")) return;
            Simulation sim = match!.Simulation!;
            World w = sim.World;
            Check(w.Config.PlayerCount == 3 && w.FactionOf(0) == _third && w.FactionOf(1) == 1 && w.FactionOf(2) == 0,
                $"seats: {w.Config.PlayerCount} players, factions {w.FactionOf(0)} / {w.FactionOf(1)} / {w.FactionOf(2)}");
            Check(match.StartLine.Contains("seats third human, whirlwind ai normal, malazan ai normal", StringComparison.Ordinal), $"start line '{match.StartLine}'");
            string? replayPath = main.ReplayPath;

            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (sim.TickNumber < 20 && clock.Elapsed.TotalSeconds < 60) await Frames(1);
            await Hud(match, sim);

            while (sim.TickNumber < SaveTick && clock.Elapsed.TotalSeconds < 120) await Frames(1);
            main.PauseMatch();
            await Frames(1);
            ulong saveHash = sim.StateHash();
            Check(sim.TickNumber == SaveTick && main.SaveGame(), $"no save at tick {SaveTick} (tick {sim.TickNumber})");
            string? savePath = main.LastSavePath;
            main.Resume();
            while (sim.TickNumber < EndTick && clock.Elapsed.TotalSeconds < 240) await Frames(1);
            Check(sim.TickNumber == EndTick, $"the match reached tick {sim.TickNumber} (want {EndTick})");
            ulong endHash = sim.StateHash(); // before the shot: windowed, the match ticks on while it waits for drawn frames
            await Shot("three-faction-match");
            main.PauseMatch();
            main.ShowStats();
            await Frames(2);
            MatchStats stats = main.Stats!;
            Check(stats.Visible && stats.RowCount == 3, $"stats: visible {stats.Visible}, {stats.RowCount} rows");
            for (int p = 0; p < Math.Min(3, stats.RowCount); p++)
                Check(stats.FactionName(p) == _data.Factions[w.FactionOf(p)].DisplayName, $"stats row {p}: '{stats.FactionName(p)}'");
            Check(stats.RowCount > 0 && stats.FactionName(0) == ThirdName, $"the stats screen does not list '{ThirdName}'");
            await Shot("three-faction-stats");
            main.CloseStats();
            main.QuitToMenu();
            await Frames(2);

            // The save, loaded: the saved tick and hash, then the live match's hash at 600.
            if (Check(savePath != null && File.Exists(savePath), $"no save file ({savePath})") && main.LoadSavedGame(savePath!))
            {
                Simulation loaded = main.CurrentMatch!.Simulation!;
                Check(loaded.TickNumber == SaveTick && loaded.StateHash() == saveHash && loaded.World.FactionOf(0) == _third,
                    $"the loaded save: tick {loaded.TickNumber}, hash equal {loaded.StateHash() == saveHash}, seat 0 faction {loaded.World.FactionOf(0)}");
                clock.Restart();
                while (loaded.TickNumber < EndTick && clock.Elapsed.TotalSeconds < 120) await Frames(1);
                Check(loaded.TickNumber == EndTick && loaded.StateHash() == endHash,
                    $"the loaded match at tick {loaded.TickNumber}: hash {loaded.StateHash():X16}, the live match's {endHash:X16}");
                main.QuitToMenu();
                await Frames(2);
            }
            else Check(false, "the save did not load");

            // The recording, watched: every checkpoint equal.
            if (Check(replayPath != null && File.Exists(replayPath), $"no recording ({replayPath})") && main.WatchReplay(replayPath!))
            {
                SimRunner runner = main.CurrentMatch!.GetNode<SimRunner>("SimRunner");
                Check(runner.Simulation!.World.FactionOf(0) == _third, "the replay's seat 0 is not the third faction");
                clock.Restart();
                while (runner.PlaybackState == PlaybackState.Playing && clock.Elapsed.TotalSeconds < 120) await Frames(1);
                Replay replay = runner.Playback!;
                Check(runner.PlaybackState == PlaybackState.Ended && runner.PlaybackError == ReplayError.None
                    && replay.Checkpoints.Length >= EndTick / ReplayRecorder.DefaultCheckpointInterval && runner.CheckpointsCompared == replay.Checkpoints.Length,
                    $"playback {runner.PlaybackState} ({runner.PlaybackError}), {runner.CheckpointsCompared} of {replay.Checkpoints.Length} checkpoints, {replay.TickCount} ticks");
                GD.Print($"  replay: {replay.TickCount} ticks, {runner.CheckpointsCompared} checkpoints equal");
                main.QuitToMenu();
                await Frames(2);
            }
            else Check(false, "the recording did not play");
        }
        finally
        {
            if (GetTree().Paused) GetTree().Paused = false;
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
    }

    // The local player's (the third faction's) hall, worker, production card, build menu and tooltips read its data.
    private async Task Hud(Match match, Simulation sim)
    {
        World w = sim.World;
        var sel = match.GetNode<SelectionController>("SelectionController");
        var panel = match.GetNode<SelectionPanel>("Hud/SelectionPanel");
        var card = match.GetNode<CommandCard>("Hud/CommandCard");
        BuildingStore b = w.Buildings;
        int hall = -1;
        for (int i = 0; i < b.Capacity && hall < 0; i++)
            if (b.Alive[i] && b.Owner[i] == 0) hall = i;
        if (!Check(hall >= 0 && sel.SelectBuilding(hall), "the third faction has no building to select")) return;
        await Frames(2);
        BuildingDef hallDef = _data.Buildings[b.TypeId[hall]];
        Check(hallDef.Key.StartsWith(ThirdFactionForge.Id + "_", StringComparison.Ordinal) && panel.NameLabel.Text == hallDef.DisplayName
            && hallDef.DisplayName.StartsWith(ThirdFactionForge.NamePrefix, StringComparison.Ordinal),
            $"the hall: '{hallDef.Key}' shows '{panel.NameLabel.Text}'");
        GD.Print($"  hall '{panel.NameLabel.Text}'");
        int trains = 0;
        for (int i = 0; i < CommandCard.Cells; i++)
        {
            if (card.ActionAt(i) != CardCommand.Train) continue;
            UnitDef u = _data.Units[card.TypeAt(i)];
            trains++;
            Check(u.Key.StartsWith(ThirdFactionForge.Id + "_", StringComparison.Ordinal) && card.NameAt(i).Text == u.DisplayName
                && u.DisplayName.StartsWith(ThirdFactionForge.NamePrefix, StringComparison.Ordinal),
                $"hall card cell {i}: '{u.Key}' named '{card.NameAt(i).Text}'");
            string tip = Plain(card.ButtonAt(i).TooltipText);
            Check(tip.Contains(Plain(u.Description), StringComparison.Ordinal) && tip.Contains(ThirdFactionForge.TextPrefix, StringComparison.Ordinal),
                $"hall card cell {i} tooltip '{tip}'");
            GD.Print($"  hall trains '{card.NameAt(i).Text}': '{tip[..Math.Min(60, tip.Length)]}...'");
        }
        Check(trains > 0, "the third faction's hall card trains nothing");

        UnitStore us = w.Units;
        int worker = -1;
        for (int i = 0; i < us.Capacity && worker < 0; i++)
            if (us.Alive[i] && us.Owner[i] == 0 && _data.Units[us.TypeId[i]].IsWorker) worker = i;
        if (!Check(worker >= 0 && sel.SelectOnly(new EntityHandle(worker, us.Generation[worker])), "the third faction has no worker to select")) return;
        await Frames(2);
        UnitDef wd = _data.Units[us.TypeId[worker]];
        Check(panel.NameLabel.Text == wd.DisplayName && wd.DisplayName.StartsWith(ThirdFactionForge.NamePrefix, StringComparison.Ordinal),
            $"the worker '{wd.Key}' shows '{panel.NameLabel.Text}'");
        Check(card.OpenMenu(false), "the worker's build menu did not open");
        await Frames(1);
        int places = 0;
        for (int i = 0; i < CommandCard.Cells; i++)
        {
            if (card.ActionAt(i) != CardCommand.Place) continue;
            BuildingDef bd = _data.Buildings[card.TypeAt(i)];
            places++;
            string tip = Plain(card.ButtonAt(i).TooltipText);
            Check(bd.Key.StartsWith(ThirdFactionForge.Id + "_", StringComparison.Ordinal) && card.NameAt(i).Text == bd.DisplayName
                && tip.Contains(Plain(bd.Description), StringComparison.Ordinal) && tip.Contains(ThirdFactionForge.TextPrefix, StringComparison.Ordinal),
                $"build menu cell {i}: '{bd.Key}' named '{card.NameAt(i).Text}', tooltip '{tip}'");
        }
        Check(places > 0, "the third faction's build menu is empty");
        GD.Print($"  worker '{panel.NameLabel.Text}', build menu {places} buildings");
        card.CloseMenu();
    }

    // ---- --faction on the command line ----

    private async Task CommandLine()
    {
        foreach ((string slot, int player) in new[] { ("1", 0), ("2", 1) })
        {
            Main main = NewMain("--skirmish", "--faction", slot, ThirdFactionForge.Id, "--mute");
            AddChild(main);
            await Frames(3);
            try
            {
                Simulation? sim = main.CurrentMatch?.Simulation;
                if (!Check(sim != null, $"--faction {slot} {ThirdFactionForge.Id}: no match")) continue;
                Check(sim!.World.FactionOf(player) == _third && sim.World.FactionOf(1 - player) == (1 - player) % 3,
                    $"--faction {slot} third: player {player} plays {sim.World.FactionOf(player)}, the other {sim.World.FactionOf(1 - player)}");
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (sim.TickNumber < 50 && clock.Elapsed.TotalSeconds < 30) await Frames(1);
                Check(sim.TickNumber >= 50, $"--faction {slot} third: the match stalled at {sim.TickNumber}");
                GD.Print($"  --faction {slot} third: '{main.CurrentMatch!.StartLine}'");
                main.QuitToMenu();
                await Frames(2);
            }
            finally
            {
                RemoveChild(main);
                main.QueueFree();
                await Frames(2);
            }
        }
    }

    // ---- helpers ----

    private static string Plain(string text) => text.Replace('\n', ' ');

    private static void Pick(OptionButton list, int index)
    {
        list.Select(index);
        list.EmitSignal(OptionButton.SignalName.ItemSelected, index);
    }

    private async Task Shot(string name)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = $"{_shots}/{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"  screenshot {path}");
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
