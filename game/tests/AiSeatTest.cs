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
/// AI has a worker gathering or returning or a queue item, and in the menu's match (no army, M6-V3) a worker gathering or
/// returning (M6-VH1: the sim's start locations put each hall by its mines, BUG-0604; see <see cref="CheckAiActed"/>);
/// paused, no tick runs; three quit-to-menu cycles with an AI seat leak
/// nothing (node count flat, no orphans, objects flat). Then <c>Match.tscn</c> with <c>--ai hard --faction 2 whirlwind</c>
/// and a replay recorder: the same seats, the AI acts, the replay header holds <c>seat 1 whirlwind ai hard</c> and the
/// replay plays back (every seat human, the AI's commands from the log) to equal checkpoints. <c>--faction 1 whirlwind
/// --faction 2 malazan</c> (no <c>--ai</c>) swaps the sides: the local player plays the Whirlwind (its army, its build menu,
/// its colour), player 1 the Malazans, both human. A match whose player 0 owns one training hall and no unit is defeated when
/// player 1 razes it (staged to hold under the M5-4b and M5-4e rules, M6-V7): one <c>DefeatEvent</c>, one "Match over:" line,
/// the outcome Defeat; its dev SpawnBuilding after it is dropped at the door (no building, still one line; BUG-0674). A normal 400-tick match prints none. Junk <c>--ai</c> / <c>--faction</c> values are refused and the match still
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
        CheckAiActed(sim, "menu", gathering: true);

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
        CheckAiActed(sim, "--ai hard", gathering: false);
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

        // The colours (M6-V3): per slot, not per faction; slot s keeps colour s of ui.json's table when the factions swap.
        var buildings = match.GetNode<BuildingViews>("World3D/BuildingViews");
        UiText ui = UiText.Shared!;
        Check(buildings.PlayerMaterial(0).AlbedoColor.IsEqualApprox(UnitViews.ColorFromRgb(ui.ColourRgb(0)))
            && buildings.PlayerMaterial(1).AlbedoColor.IsEqualApprox(UnitViews.ColorFromRgb(ui.ColourRgb(1))),
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

    // The dev line through the real Match (M6-V4: one line per DefeatEvent): player 0's only building is a finished training
    // hall and it has no unit; player 1, which never builds (so the match is not over: one seat built), shells it with a dev
    // battery, and its fall defeats player 0. M6-V7 (Requests 40): staged so the defeat lands on the hall's tick under both the
    // M5-4b rule (the last building) and the M5-4e one (no unit, no finished unit-producing building), see DefeatStaging.
    // Round 2: a dev SpawnBuilding of the defeated player is dropped at the door, so no building and no second line (BUG-0674).
    private async Task MatchOverLine()
    {
        (Match match, SimRunner runner) = StartMatch(new[] { "--units", "0", "--no-bases", "--mute", "--seed", "2", "--speed", "8" }, record: 0);
        Simulation sim = runner.Simulation!;
        World w = sim.World;
        NavGrid g = w.NavGrid;
        int events = 0, unitsAtDefeat = -1;
        runner.Ticked += s =>
        {
            foreach (DefeatEvent e in s.World.DefeatEvents)
            {
                events++;
                if (e.Player == 0) unitsAtDefeat = DefeatStaging.UnitsOf(s.World, 0).Count;
            }
        };
        int hall = DefeatStaging.Hall(_data, w, 0);
        int anchor = DefeatStaging.Row(w, new[] { 0 }, new[] { hall })[0]; // its battery's cells open too
        DefeatStaging.SpawnAt(sim, 0, hall, anchor);
        await UntilTick(sim, sim.TickNumber + 3);
        EntityHandle target = DefeatStaging.BuildingAt(w, 0, anchor);
        Check(target != default && Owned(w, 0) == 1 && DefeatStaging.UnitsOf(w, 0).Count == 0 && match.MatchOverLines == 0 && events == 0 && !w.IsDefeated(0),
            $"round 1: hall {target}, {Owned(w, 0)} buildings of player 0, {DefeatStaging.UnitsOf(w, 0).Count} units, {match.MatchOverLines} lines, {events} events");
        // Round 1: player 1's battery razes the hall; its fall defeats player 0 (one event, one line, the local result Defeat).
        int shooter = DefeatStaging.Shooter(_data);
        foreach (System.Numerics.Vector2 at in DefeatStaging.Battery(g, _data.Buildings[hall], anchor)) sim.Enqueue(Command.SpawnUnit(1, shooter, at));
        await UntilTick(sim, sim.TickNumber + 3);
        Check(DefeatStaging.UnitsOf(w, 1).Count == DefeatStaging.BatterySize, $"round 1: {DefeatStaging.UnitsOf(w, 1).Count} guns spawned (want {DefeatStaging.BatterySize})");
        foreach (EntityHandle gun in DefeatStaging.UnitsOf(w, 1)) sim.Enqueue(Command.Attack(1, gun, target, isBuilding: true));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (events == 0 && clock.Elapsed.TotalSeconds < 60) await Frames(1);
        await UntilTick(sim, sim.TickNumber + 10);
        Check(Owned(w, 0) == 0 && w.IsDefeated(0) && events == 1 && match.MatchOverLines == events && unitsAtDefeat == 0 && !w.EverBuilt(1),
            $"round 1: {Owned(w, 0)} buildings of player 0 after the raze, defeated {w.IsDefeated(0)}, {events} events, {match.MatchOverLines} 'Match over:' lines, {unitsAtDefeat} units at the defeat, player 1 built {w.EverBuilt(1)}");
        Check(match.Outcome == MatchOutcome.Defeat && !sim.MatchOver && match.DefeatedAt(0) > 0,
            $"round 1: outcome {match.Outcome}, MatchOver {sim.MatchOver}, defeated at {match.DefeatedAt(0)}");
        // Round 2: a defeated player's dev SpawnBuilding is dropped where it applies: on clean ground a live player's would land.
        int spot = DefeatStaging.Anchor(w, 0, hall, anchor % g.Width + DefeatStaging.Stride, anchor / g.Width);
        Check(DefeatStaging.SpawnSpot(w, 0, hall, spot), $"round 2: anchor {spot} is not open ground");
        DefeatStaging.SpawnAt(sim, 0, hall, spot);
        await UntilTick(sim, sim.TickNumber + 10);
        Check(Owned(w, 0) == 0 && events == 1 && match.MatchOverLines == events,
            $"round 2: {Owned(w, 0)} buildings of player 0 after the dropped SpawnBuilding, {events} events, {match.MatchOverLines} lines");
        await EndMatch(match);
    }

    private static int Owned(World w, int player)
    {
        BuildingStore b = w.Buildings;
        int n = 0;
        for (int i = 0; i < b.Capacity; i++) if (b.Alive[i] && b.Owner[i] == player) n++;
        return n;
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

    // The AI did something by now: one of its workers gathers or carries, or one of its buildings has a queue item. With
    // `gathering` (the menu's match, M6-V3) a worker must be at work: the sim's start locations put the hall by its two mines
    // and a forest, so the AI sends Gathers without waiting for its scout (BUG-0604, a printed probe until M6-VH1).
    private void CheckAiActed(Simulation sim, string how, bool gathering)
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
        if (gathering)
            Check(working > 0, $"{how}: by tick {sim.TickNumber} no AI worker is gathering or returning (BUG-0604: the start base should see its mines)");
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
