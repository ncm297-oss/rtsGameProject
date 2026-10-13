using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.Replays;

namespace Rts.Game.Tests;

/// <summary>
/// M6-V2: the AI seat, from the menu and from the dev flags. Menu -> Skirmish -> slot 2 Whirlwind, Hard -> Start: the runner's
/// seats are (malazan, human) and (whirlwind, AI, Hard), the sim's config and <c>World.FactionOf</c> agree, and by tick 400 the
/// AI has a worker gathering or returning or a queue item; paused, no tick runs; three quit-to-menu cycles with an AI seat leak
/// nothing (node count flat, no orphans, objects flat). Then <c>Match.tscn</c> with <c>--ai hard --faction 2 whirlwind</c>
/// and a replay recorder: the same seats, the AI acts, the replay header holds <c>seat 1 whirlwind ai hard</c> and the
/// replay plays back (every seat human, the AI's commands from the log) to equal checkpoints. <c>--faction 1 whirlwind
/// --faction 2 malazan</c> (no <c>--ai</c>) swaps the sides: the local player plays the Whirlwind (its army, its build menu,
/// its colour), player 1 the Malazans, both human. A match whose player 0 builds and cancels its only site twice prints
/// "Match over:" once per cancel (and a normal 400-tick match none). Junk <c>--ai</c> / <c>--faction</c> values are refused and the match still
/// starts on the default seats.
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/AiSeatTest.tscn</c>; prints "AI SEAT TEST PASS" or "AI SEAT TEST FAIL: ..." lines.</remarks>
public partial class AiSeatTest : Node
{
    /// <summary>The tick by which the AI must have acted (brief M6-V2).</summary>
    public const int ActBy = 400;

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private int _malazan, _whirlwind;

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
            _malazan = _data.FindFaction("malazan");
            _whirlwind = _data.FindFaction("whirlwind");
            if (_malazan < 0 || _whirlwind < 0) throw new InvalidOperationException("the malazan / whirlwind factions are not loaded");
            await FromTheMenu();
            await FromTheFlags();
            await SwappedSides();
            await MatchOverLine();
            await JunkFlags();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        foreach (string f in _failures.Take(60)) GD.Print($"AI SEAT TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("AI SEAT TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- the menu ----

    private async Task FromTheMenu()
    {
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.UserArgs = Array.Empty<string>();
        AddChild(main);
        await Frames(2);
        Check(main.Menu is { Visible: true } && main.CurrentMatch == null, "no-arg boot is not the menu");
        MainMenu menu = main.Menu!;
        SkirmishSetup setup = main.Setup!;
        await ClickControl(menu.SkirmishButton);
        Check(setup.Visible, "Skirmish did not open the setup");
        // Slot 2: away from the Whirlwind and back (the default is already faction 1), and Hard.
        setup.FactionOptions[1].Select(_malazan);
        setup.FactionOptions[1].Select(_whirlwind);
        setup.DifficultyOption.Select((int)AiDifficulty.Hard);
        await ClickControl(setup.StartButton);
        await Frames(2);
        Check(main.CurrentMatch != null, "Start started no match");
        if (main.CurrentMatch == null) return;

        SimRunner runner = main.CurrentMatch.GetNode<SimRunner>("SimRunner");
        Simulation sim = runner.Simulation!;
        CheckSeats(runner, _malazan, _whirlwind, AiDifficulty.Hard, "menu");
        runner.GameSpeed = SimRunner.MaxSpeed;
        await UntilTick(sim, ActBy);
        CheckAiActed(sim, "menu");

        // Paused with an AI seat: no tick, so no think either.
        PushKey(Key.Escape);
        await Frames(1);
        Check(main.Paused && GetTree().Paused, "Esc did not pause the AI match");
        int tick = sim.TickNumber, pending = sim.PendingCommandCount;
        int frames = await Seconds(0.5);
        Check(sim.TickNumber == tick && sim.PendingCommandCount == pending,
            $"paused AI match: ticks {tick} -> {sim.TickNumber}, pending {pending} -> {sim.PendingCommandCount} over {frames} frames");
        PushKey(Key.Escape);
        await Seconds(0.2);
        Check(!main.Paused && sim.TickNumber > tick, $"resumed AI match does not tick ({tick})");
        runner.GameSpeed = 1.0;
        await QuitToMenu(main);

        // Three quit cycles with the AI seat (the picks stay: Whirlwind, Hard).
        var rows = new List<(int Nodes, double Objects, double Orphans, double Resources)>();
        for (int cycle = 0; cycle < 3; cycle++)
        {
            await ClickControl(menu.SkirmishButton);
            Check(setup.FactionPick(1) == _whirlwind && setup.DifficultyPick == AiDifficulty.Hard, $"cycle {cycle}: the picks were lost");
            await ClickControl(setup.StartButton);
            await Frames(2);
            Check(main.CurrentMatch != null, $"cycle {cycle}: no match");
            if (main.CurrentMatch == null) continue;
            SimRunner r = main.CurrentMatch.GetNode<SimRunner>("SimRunner");
            Check(r.Seats.Length == 2 && r.Seats[1].Controller == SeatController.Ai, $"cycle {cycle}: seats {string.Join(", ", r.Seats)}");
            r.GameSpeed = SimRunner.MaxSpeed;
            await Seconds(0.3);
            Check(r.Simulation!.TickNumber > 20, $"cycle {cycle}: only {r.Simulation.TickNumber} ticks");
            await QuitToMenu(main);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Frames(2);
            rows.Add((GetTree().GetNodeCount(), Performance.GetMonitor(Performance.Monitor.ObjectCount),
                Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount), Performance.GetMonitor(Performance.Monitor.ObjectResourceCount)));
        }
        for (int i = 0; i < rows.Count; i++)
            GD.Print($"  cycle {i}: nodes {rows[i].Nodes}, objects {rows[i].Objects}, orphans {rows[i].Orphans}, resources {rows[i].Resources}");
        if (rows.Count == 3)
        {
            Check(rows.All(r => r.Nodes == rows[0].Nodes), $"node count not flat: {string.Join(",", rows.Select(r => r.Nodes))}");
            Check(rows.All(r => r.Orphans == 0), $"orphan nodes: {string.Join(",", rows.Select(r => r.Orphans))}");
            // From the second cycle on (first-use caches are in by then), as QaShellTest's same-size rows.
            Check(rows[2].Objects - rows[1].Objects <= 50, $"objects grow {rows[2].Objects - rows[1].Objects} from cycle 1 to 2");
            Check(rows[2].Resources - rows[1].Resources <= 10, $"resources grow {rows[2].Resources - rows[1].Resources} from cycle 1 to 2");
        }
        RemoveChild(main);
        main.QueueFree();
        await Frames(2);
    }

    private async Task QuitToMenu(Main main)
    {
        PushKey(Key.Escape);
        await Frames(2);
        Check(main.Paused, "Esc did not pause");
        await ClickControl(main.Pause!.QuitToMenuButton);
        await Frames(3);
        Check(main.CurrentMatch == null && !GetTree().Paused && main.Menu!.Visible, "Quit to menu state");
    }

    // ---- the flags ----

    private async Task FromTheFlags()
    {
        (Match match, SimRunner runner) = StartMatch(new[] { "--ai", "hard", "--faction", "2", "whirlwind", "--mute", "--seed", "3" }, record: 20);
        Simulation sim = runner.Simulation!;
        CheckSeats(runner, _malazan, _whirlwind, AiDifficulty.Hard, "--ai hard --faction 2 whirlwind");
        runner.GameSpeed = SimRunner.MaxSpeed;
        await UntilTick(sim, ActBy);
        CheckAiActed(sim, "--ai hard");
        Check(match.MatchOverLines == 0, $"{match.MatchOverLines} 'Match over:' lines in a normal {sim.TickNumber}-tick match");
        runner.ProcessMode = ProcessModeEnum.Disabled; // the recording ends here

        Replay replay = runner.Recorder!.ToReplay();
        string header = Encoding.UTF8.GetString(ReplayFormat.Write(replay));
        Check(header.Contains("\nseat 1 whirlwind ai hard\n") && header.Contains("\nseat 0 malazan human normal\n"),
            $"replay header seats: {string.Join(" | ", header.Split('\n').Where(l => l.StartsWith("seat", StringComparison.Ordinal)))}");
        int aiCommands = replay.Commands.Count(c => c.Player == 1);
        ReplayResult twin = ReplayPlayer.Run(replay, _data);
        Check(twin.Ok && replay.Checkpoints.Length > 0, $"playback {twin.Error} at tick {twin.Tick} ({twin.ExpectedHash:x} vs {twin.ActualHash:x}), {replay.Checkpoints.Length} checkpoints");
        Check(aiCommands > 0, "the replay holds no command of the AI's");
        GD.Print($"  replay: {replay.TickCount} ticks, {replay.Commands.Length} commands ({aiCommands} the AI's), {replay.Checkpoints.Length} checkpoints equal on playback");
        await EndMatch(match);
    }

    private async Task SwappedSides()
    {
        (Match match, SimRunner runner) = StartMatch(new[] { "--faction", "1", "whirlwind", "--faction", "2", "malazan", "--mute" }, record: 0);
        World w = runner.Simulation!.World;
        var want = new[] { new PlayerSeat(_whirlwind, SeatController.Human, AiDifficulty.Normal), new PlayerSeat(_malazan, SeatController.Human, AiDifficulty.Normal) };
        Check(runner.Seats.SequenceEqual(want) && w.Config.Seats.SequenceEqual(want) && w.FactionOf(0) == _whirlwind && w.FactionOf(1) == _malazan && !w.Config.HasAiSeat,
            $"swapped seats [{string.Join(", ", runner.Seats)}], FactionOf {w.FactionOf(0)}, {w.FactionOf(1)}");
        await UntilTick(runner.Simulation!, 2); // the start army and base apply on the first tick (ticks follow real time, not frames)

        // The colours: player 0's buildings in the Whirlwind's colour, player 1's in the Malazans'.
        var buildings = match.GetNode<BuildingViews>("World3D/BuildingViews");
        Check(buildings.PlayerMaterial(0).AlbedoColor.IsEqualApprox(UnitViews.ColorFromRgb(_data.Factions[_whirlwind].PrimaryColor))
            && buildings.PlayerMaterial(1).AlbedoColor.IsEqualApprox(UnitViews.ColorFromRgb(_data.Factions[_malazan].PrimaryColor)),
            $"building colours {buildings.PlayerMaterial(0).AlbedoColor}, {buildings.PlayerMaterial(1).AlbedoColor}");

        // Every unit of each side is its seat's faction's (the army and the workers).
        UnitStore u = w.Units;
        int[] wrong = new int[2], count = new int[2];
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || (uint)u.Owner[i] > 1) continue;
            count[u.Owner[i]]++;
            if (_data.Units[u.TypeId[i]].Faction != w.FactionOf(u.Owner[i])) wrong[u.Owner[i]]++;
        }
        Check(count[0] > 0 && count[1] > 0 && wrong[0] == 0 && wrong[1] == 0, $"units: {count[0]} / {count[1]}, of another faction {wrong[0]} / {wrong[1]}");

        // The HUD card: the local worker's build menu offers the Whirlwind's buildings.
        var sel = match.GetNode<SelectionController>("SelectionController");
        var card = match.GetNode<CommandCard>("Hud/CommandCard");
        int worker = -1;
        for (int i = 0; i < u.Capacity && worker < 0; i++)
            if (u.Alive[i] && u.Owner[i] == SelectionController.LocalPlayer && _data.Units[u.TypeId[i]].Slot == UnitSlot.Worker) worker = i;
        Check(worker >= 0, "no local worker");
        if (worker >= 0)
        {
            sel.SelectOnly(new EntityHandle(worker, u.Generation[worker]));
            await Frames(2);
            Check(card.OpenMenu(false), "the basic build menu did not open");
            await Frames(1);
            var types = new List<int>();
            for (int i = 0; i < CommandCard.Cells; i++) if (card.ActionAt(i) == CardCommand.Place) types.Add(card.TypeAt(i));
            Check(types.Count > 0 && types.All(t => t >= 0 && _data.Buildings[t].Faction == _whirlwind),
                $"build menu types [{string.Join(", ", types.Select(t => t >= 0 ? _data.Buildings[t].Key : "-"))}]");
            card.CloseMenu();
        }
        await EndMatch(match);
    }

    // The dev line through the real Match: player 0's only building is a site it cancels; "Match over:" counts once.
    private async Task MatchOverLine()
    {
        (Match match, SimRunner runner) = StartMatch(new[] { "--units", "0", "--no-bases", "--mute", "--seed", "2", "--speed", "8" }, record: 0);
        Simulation sim = runner.Simulation!;
        World w = sim.World;
        NavGrid g = w.NavGrid;
        int laborer = _data.FindUnit("malazan_laborer"), billet = _data.FindBuilding("malazan_billet");
        int centre = FlowField.NearestPassable(g, g.Height / 2 * g.Width + g.Width / 2);
        sim.Enqueue(Command.SpawnUnit(0, laborer, g.CellCenter(centre % g.Width, centre / g.Width)));
        await UntilTick(sim, sim.TickNumber + 3);
        UnitStore u = w.Units;
        int worker = -1;
        for (int i = 0; i < u.Capacity && worker < 0; i++) if (u.Alive[i] && u.Owner[i] == 0) worker = i;
        int anchor = -1;
        for (int r = 2; r < 12 && anchor < 0; r++)
            for (int dy = -r; dy <= r && anchor < 0; dy++)
                for (int dx = -r; dx <= r && anchor < 0; dx++)
                {
                    int x = centre % g.Width + dx, y = centre / g.Width + dy;
                    if (x >= 0 && y >= 0 && x < g.Width && y < g.Height && w.CanPlace(0, billet, y * g.Width + x, out _)) anchor = y * g.Width + x;
                }
        Check(worker >= 0 && anchor >= 0, $"match-over row: worker {worker}, anchor {anchor}");
        if (worker >= 0 && anchor >= 0)
        {
            System.Numerics.Vector2 at = g.CellCenter(anchor % g.Width, anchor / g.Width);
            for (int round = 1; round <= 2; round++)
            {
                sim.Enqueue(Command.Build(0, new EntityHandle(worker, u.Generation[worker]), billet, at));
                await UntilTick(sim, sim.TickNumber + 3);
                Check(w.Buildings.Count == 1 && match.MatchOverLines == round - 1, $"round {round}: {w.Buildings.Count} buildings after the Build, {match.MatchOverLines} lines");
                sim.Enqueue(Command.Cancel(0, at));
                await UntilTick(sim, sim.TickNumber + 10);
                Check(w.Buildings.Count == 0 && match.MatchOverLines == round, $"round {round}: {w.Buildings.Count} buildings after the Cancel, {match.MatchOverLines} 'Match over:' lines");
            }
        }
        await EndMatch(match);
    }

    private async Task JunkFlags()
    {
        foreach (string[] junk in new[]
        {
            new[] { "--ai", "insane", "--mute" },
            new[] { "--faction", "2", "atlantean", "--mute" },
            new[] { "--faction", "3", "whirlwind", "--ai", "--mute" },
            new[] { "--faction", "1", "--ai", "Hard", "--mute" },
        })
        {
            (Match match, SimRunner runner) = StartMatch(junk, record: 0);
            Simulation? sim = runner.Simulation;
            var defaults = SimRunner.BuildSeats(2, _data.Factions.Length, new[] { -1, -1 }, null);
            Check(sim != null && runner.Seats.SequenceEqual(defaults), $"'{string.Join(' ', junk)}': seats [{string.Join(", ", runner.Seats)}]");
            if (sim != null)
            {
                int t0 = sim.TickNumber;
                await Seconds(0.2);
                Check(sim.TickNumber > t0, $"'{string.Join(' ', junk)}': the match does not tick");
            }
            await EndMatch(match);
        }
    }

    // ---- helpers ----

    private (Match Match, SimRunner Runner) StartMatch(string[] args, int record)
    {
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        SimRunner runner = match.GetNode<SimRunner>("SimRunner");
        runner.RecordCheckpointInterval = record;
        match.Start(_data, LaunchOptions.Parse(args));
        match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
        return (match, runner);
    }

    private async Task EndMatch(Match match)
    {
        RemoveChild(match);
        match.QueueFree();
        await Frames(2);
    }

    private void CheckSeats(SimRunner runner, int f0, int f1, AiDifficulty difficulty, string how)
    {
        World w = runner.Simulation!.World;
        var want = new[] { new PlayerSeat(f0, SeatController.Human, AiDifficulty.Normal), new PlayerSeat(f1, SeatController.Ai, difficulty) };
        Check(runner.Seats.SequenceEqual(want) && w.Config.Seats.SequenceEqual(want), $"{how}: seats [{string.Join(", ", runner.Seats)}], config [{string.Join(", ", w.Config.Seats)}]");
        Check(w.FactionOf(0) == f0 && w.FactionOf(1) == f1 && w.Config.HasAiSeat && w.Config.SeatOf(1).Controller == SeatController.Ai,
            $"{how}: FactionOf {w.FactionOf(0)}, {w.FactionOf(1)}; AI seat {w.Config.HasAiSeat}");
    }

    // The AI did something by now: one of its workers gathers or carries, or one of its buildings has a queue item.
    private void CheckAiActed(Simulation sim, string how)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        int working = 0, queued = 0;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == 1 && u.State[i] is UnitState.Gathering or UnitState.Returning) working++;
        BuildingStore b = w.Buildings;
        for (int k = 0; k < b.Capacity; k++)
            if (b.Alive[k] && b.Owner[k] == 1) queued += b.QueueCount[k];
        Check(working > 0 || queued > 0, $"{how}: by tick {sim.TickNumber} the AI has no worker gathering or returning and no queue item");
        GD.Print($"  {how}: tick {sim.TickNumber}, AI workers gathering / returning {working}, queue items {queued}");
    }

    private async Task UntilTick(Simulation sim, int tick)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (sim.TickNumber < tick && clock.Elapsed.TotalSeconds < 60) await Frames(1);
        Check(sim.TickNumber >= tick, $"tick {sim.TickNumber} after 60 s (want {tick})");
    }

    private void PushKey(Key key)
    {
        GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    private async Task ClickControl(Control c)
    {
        Check(c.IsVisibleInTree(), $"{c.Name} is not visible");
        Vector2 at = c.GetGlobalRect().GetCenter();
        GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        await Frames(1);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Left });
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
        await Frames(2);
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task<int> Seconds(double seconds)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        int frames = 0;
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            frames++;
        }
        return frames;
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
