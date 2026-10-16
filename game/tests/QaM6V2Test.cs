using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;

namespace Rts.Game.Tests;

/// <summary>
/// QA attacks on M6-V2 (session 2026-10-12-1930). Repeated and junk <c>--ai</c> / <c>--faction</c> flags through the real
/// <see cref="Match"/> (last good value wins, junk keeps what was there, a slot of 3 is refused, an id-shaped non-faction is
/// refused, the match always starts and ticks); <c>--no-bases --ai hard</c> at 8x (no "Match over:" line, no error);
/// <c>--faction 1 whirlwind --ai hard</c> (the HUD's resource bar names and the building colours are the Whirlwind's for
/// the local player); the menu's picks (a mirror, Easy, then Hard over Back) surviving quit to menu and each Start, ten
/// quit cycles with a Hard AI seat at 8x (nodes flat, no orphans, managed and object counts flat); and the BUG-0580 class on
/// the minimap (a left press on the minimap held into the pause and released during it must not leave the minimap
/// dragging the camera after Resume).
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6V2Test.tscn</c>; prints "QA M6V2 TEST PASS" or "QA M6V2 TEST FAIL: ..." lines.</remarks>
public partial class QaM6V2Test : Node
{
    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private int _mal, _whirl;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            await Frames(1);
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            _mal = _data.FindFaction("malazan");
            _whirl = _data.FindFaction("whirlwind");
            SizeFormatEscapes();
            await RepeatedAndJunkFlags();
            await NoBasesWithAi();
            await SwappedHud();
            await MenuPicksAndCycles();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        foreach (string f in _failures.Take(60)) GD.Print($"QA M6V2 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M6V2 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- ui.json ----

    // setup.map_size_item must name the size: an escaped "{{0}}" contains the text "{0}" but formats to a literal "{0}" with no
    // number, so every list item would read the same; it should be refused like "{1}" or "size" (one error naming the key).
    private void SizeFormatEscapes()
    {
        string json = System.IO.File.ReadAllText(UiText.DefaultPath);
        foreach (string bad in new[] { "{{0}}", "{{0}} x {{0}}" })
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
            root["setup"]!["map_size_item"] = bad;
            var errors = new List<string>();
            UiText? ui = UiText.Parse(root.ToJsonString(), errors);
            bool refused = ui == null && errors.Count == 1 && errors[0].Contains("setup.map_size_item");
            // BUG-0602 item 1 (fixed in M6-V3): a Check since the fix.
            Check(refused, $"BUG-0602: map_size_item '{bad}' accepted: items would read '{ui?.MapSizeItem(128)}' / '{ui?.MapSizeItem(256)}'; errors [{string.Join("; ", errors)}]");
        }
    }

    // ---- flags ----

    private async Task RepeatedAndJunkFlags()
    {
        var H = SeatController.Human;
        var A = SeatController.Ai;
        var rows = new (string[] Args, PlayerSeat S0, PlayerSeat S1)[]
        {
            (new[] { "--ai", "easy", "--ai", "hard" }, new(_mal, H, AiDifficulty.Normal), new(_whirl, A, AiDifficulty.Hard)),
            (new[] { "--ai", "hard", "--ai", "junk" }, new(_mal, H, AiDifficulty.Normal), new(_whirl, A, AiDifficulty.Hard)),
            (new[] { "--faction", "2", "whirlwind", "--faction", "2", "malazan" }, new(_mal, H, AiDifficulty.Normal), new(_mal, H, AiDifficulty.Normal)),
            (new[] { "--faction", "2", "malazan", "--faction", "2", "Junk" }, new(_mal, H, AiDifficulty.Normal), new(_mal, H, AiDifficulty.Normal)),
            (new[] { "--faction", "3", "whirlwind", "--faction", "2", "malazan", "--ai", "normal" }, new(_mal, H, AiDifficulty.Normal), new(_mal, A, AiDifficulty.Normal)),
            (new[] { "--faction", "1", "whirlwind", "--ai", "hard" }, new(_whirl, H, AiDifficulty.Normal), new(_whirl, A, AiDifficulty.Hard)),
            (new[] { "--faction", "2", "whirlwind", "--ai" }, new(_mal, H, AiDifficulty.Normal), new(_whirl, H, AiDifficulty.Normal)),
            (new[] { "--faction", "2", "malazan_heavy_infantry", "--ai", "easy" }, new(_mal, H, AiDifficulty.Normal), new(_whirl, A, AiDifficulty.Easy)),
            (new[] { "--faction", "1", "whirlwind", "--faction", "1" }, new(_whirl, H, AiDifficulty.Normal), new(_whirl, H, AiDifficulty.Normal)),
            (new[] { "--faction", "-1", "whirlwind", "--faction", "02", "malazan" }, new(_mal, H, AiDifficulty.Normal), new(_whirl, H, AiDifficulty.Normal)),
        };
        foreach ((string[] args, PlayerSeat s0, PlayerSeat s1) in rows)
        {
            string how = string.Join(' ', args);
            (Match match, SimRunner runner) = StartMatch(args.Append("--mute").ToArray());
            Simulation? sim = runner.Simulation;
            Check(sim != null && runner.Seats.Length == 2 && runner.Seats[0] == s0 && runner.Seats[1] == s1
                && sim.World.FactionOf(0) == s0.Faction && sim.World.FactionOf(1) == s1.Faction
                && sim.World.Config.HasAiSeat == (s1.Controller == A),
                $"'{how}': seats [{string.Join(", ", runner.Seats)}], want [{s0}, {s1}]");
            if (sim != null)
            {
                runner.GameSpeed = 8.0;
                await UntilTick(sim, 60);
            }
            await EndMatch(match);
        }
    }

    private async Task NoBasesWithAi()
    {
        (Match match, SimRunner runner) = StartMatch(new[] { "--no-bases", "--ai", "hard", "--units", "10", "--mute", "--speed", "8" });
        Simulation sim = runner.Simulation!;
        Check(runner.Seats[1].Controller == SeatController.Ai, "--no-bases --ai hard: no AI seat");
        await UntilTick(sim, 600);
        Check(match.MatchOverLines == 0 && sim.World.Buildings.Count == 0,
            $"--no-bases --ai hard: {match.MatchOverLines} 'Match over:' lines, {sim.World.Buildings.Count} buildings by tick {sim.TickNumber}");
        await EndMatch(match);
    }

    private async Task SwappedHud()
    {
        (Match match, SimRunner runner) = StartMatch(new[] { "--faction", "1", "whirlwind", "--ai", "hard", "--mute" });
        Simulation sim = runner.Simulation!;
        await UntilTick(sim, 3);
        await Frames(2);
        FactionDef w = _data.Factions[_whirl], m = _data.Factions[_mal];
        var bar = match.GetNode<ResourceBar>("Hud/ResourceBar");
        // Both factions name their resources alike today, so this only proves the bar reads the local seat's faction without error.
        Check(bar.Text.Contains(w.GoldName) && bar.Text.Contains(w.WoodName),
            $"resource bar '{bar.Text}' (whirlwind '{w.GoldName}' / '{w.WoodName}', malazan '{m.GoldName}' / '{m.WoodName}')");
        // The local player's units are the Whirlwind's (the army's colour is per unit type's faction).
        Rts.Sim.Entities.UnitStore u = sim.World.Units;
        int own = 0, foreign = 0;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == 0) { own++; if (_data.Units[u.TypeId[i]].Faction != _whirl) foreign++; }
        Check(own > 0 && foreign == 0, $"local units {own}, not the Whirlwind's {foreign}");
        var buildings = match.GetNode<BuildingViews>("World3D/BuildingViews");
        Color c0 = buildings.PlayerMaterial(0).AlbedoColor, c1 = buildings.PlayerMaterial(1).AlbedoColor;
        // M6-V3 (BUG-0601): a mirror draws two colours, per slot (ui.json's colour table, slot s colour s), in buildings and units.
        UiText ui = UiText.Shared!;
        Check(!c0.IsEqualApprox(c1) && c0.IsEqualApprox(UnitViews.ColorFromRgb(ui.ColourRgb(0))) && c1.IsEqualApprox(UnitViews.ColorFromRgb(ui.ColourRgb(1))),
            $"mirror building colours {c0}, {c1}");
        var units = match.GetNode<UnitViews>("World3D/UnitViews");
        Color? u0 = units.PlayerMaterial(0)?.AlbedoColor, u1 = units.PlayerMaterial(1)?.AlbedoColor;
        Check(u0 is Color a0 && u1 is Color a1 && !a0.IsEqualApprox(a1) && a0.IsEqualApprox(c0) && a1.IsEqualApprox(c1), $"mirror unit colours {u0}, {u1}");
        GD.Print($"  swapped HUD: bar '{bar.Text}', colours p0 {c0} p1 {c1}");
        await EndMatch(match);
    }

    // ---- the menu ----

    private async Task MenuPicksAndCycles()
    {
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.UserArgs = Array.Empty<string>();
        main.SettingsPath = $"user://qa-m6v2-{OS.GetProcessId()}/settings.cfg"; // BUG-0770: never the player's file (missing: the defaults)
        AddChild(main);
        await Frames(2);
        MainMenu menu = main.Menu!;
        SkirmishSetup setup = main.Setup!;
        await ClickControl(menu.SkirmishButton);
        setup.FactionOptions[0].Select(_whirl);
        setup.FactionOptions[1].Select(_whirl);
        setup.DifficultyOption.Select((int)AiDifficulty.Easy);
        setup.SeedEdit.Text = "42";
        setup.SeedEdit.EmitSignal(LineEdit.SignalName.TextChanged, "42");
        setup.MapSizeOption.Select(2);
        await ClickControl(setup.StartButton);
        await Frames(2);
        SeatsAre(main, _whirl, _whirl, AiDifficulty.Easy, "first start");
        await MinimapHeldIntoPause(main);
        await QuitToMenu(main);

        // Change the difficulty, Back, in again: kept; Start: the new pick.
        await ClickControl(menu.SkirmishButton);
        Check(setup.FactionPick(0) == _whirl && setup.FactionPick(1) == _whirl && setup.DifficultyPick == AiDifficulty.Easy && setup.Seed == 42 && setup.MapSize == 256,
            $"after quit: picks {setup.FactionPick(0)}, {setup.FactionPick(1)}, {setup.DifficultyPick}, seed {setup.Seed}, size {setup.MapSize}");
        setup.DifficultyOption.Select((int)AiDifficulty.Hard);
        setup.MapSizeOption.Select(0);
        await ClickControl(setup.BackButton);
        await ClickControl(menu.SkirmishButton);
        Check(setup.DifficultyPick == AiDifficulty.Hard, $"Back lost Hard: {setup.DifficultyPick}");

        var rows = new List<(int Nodes, double Objects, double Orphans, long Managed)>();
        for (int cycle = 0; cycle < 10; cycle++)
        {
            if (!setup.Visible) await ClickControl(menu.SkirmishButton);
            await ClickControl(setup.StartButton);
            await Frames(2);
            SeatsAre(main, _whirl, _whirl, AiDifficulty.Hard, $"cycle {cycle}");
            if (main.CurrentMatch == null) continue;
            SimRunner r = main.CurrentMatch.GetNode<SimRunner>("SimRunner");
            r.GameSpeed = 8.0;
            await UntilTick(r.Simulation!, 120);
            await QuitToMenu(main);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Frames(2);
            rows.Add((GetTree().GetNodeCount(), Performance.GetMonitor(Performance.Monitor.ObjectCount),
                Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount), GC.GetTotalMemory(true)));
        }
        for (int i = 0; i < rows.Count; i++)
            GD.Print($"  AI cycle {i}: nodes {rows[i].Nodes}, objects {rows[i].Objects}, orphans {rows[i].Orphans}, managed {rows[i].Managed / 1048576.0:0.0} MB");
        if (rows.Count == 10)
        {
            Check(rows.All(r => r.Nodes == rows[0].Nodes), $"node count not flat: {string.Join(",", rows.Select(r => r.Nodes))}");
            Check(rows.All(r => r.Orphans == 0), $"orphans: {string.Join(",", rows.Select(r => r.Orphans))}");
            double obj = rows[9].Objects - rows[2].Objects;
            double managedMb = (rows[9].Managed - rows[2].Managed) / 1048576.0;
            GD.Print($"  AI cycles growth 2 -> 9: objects {obj}, managed {managedMb:0.00} MB");
            Check(obj <= 50, $"objects grow {obj} over 7 AI cycles");
            Check(managedMb < 8.0, $"managed memory grows {managedMb:0.00} MB over 7 AI cycles");
        }
        RemoveChild(main);
        main.QueueFree();
        await Frames(2);
    }

    // The BUG-0580 class on the minimap: a left press on the minimap (a camera jump-drag) held into the pause and released during
    // it; after Resume a motion over the minimap with no button held must not move the camera.
    private async Task MinimapHeldIntoPause(Main main)
    {
        Match match = main.CurrentMatch!;
        var mini = match.GetNode<Minimap>("Hud/Minimap");
        RtsCamera cam = match.GetNode<RtsCamera>("RtsCamera");
        cam.EdgePanEnabled = false;
        Rect2 rect = mini.GetGlobalRect();
        Vector2 a = rect.Position + rect.Size * 0.25f, b = rect.Position + rect.Size * 0.75f;
        // Control row, no pause: a full click at a, then a motion to b moves nothing (so a move below is the pause's doing).
        GetViewport().PushInput(new InputEventMouseMotion { Position = a, GlobalPosition = a });
        await Frames(1);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = a, GlobalPosition = a, ButtonMask = MouseButtonMask.Left });
        await Frames(1);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = a, GlobalPosition = a });
        await Frames(2);
        Vector3 control = cam.GlobalPosition;
        GetViewport().PushInput(new InputEventMouseMotion { Position = b, GlobalPosition = b, Relative = b - a });
        await Frames(3);
        Check(cam.GlobalPosition.DistanceTo(control) < 1e-3f, $"minimap control row: a motion after a full click moved the camera {cam.GlobalPosition.DistanceTo(control):0.00} m");
        GetViewport().PushInput(new InputEventMouseMotion { Position = a, GlobalPosition = a });
        await Frames(1);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = a, GlobalPosition = a, ButtonMask = MouseButtonMask.Left });
        await Frames(2);
        PushKey(Key.Escape);
        await Frames(1);
        Check(main.Paused, "minimap row: Esc did not pause");
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = a, GlobalPosition = a });
        await Frames(2);
        PushKey(Key.Escape);
        await Frames(2);
        Vector3 before = cam.GlobalPosition;
        GetViewport().PushInput(new InputEventMouseMotion { Position = b, GlobalPosition = b, Relative = b - a });
        await Frames(3);
        float moved = cam.GlobalPosition.DistanceTo(before);
        // BUG-0600 (fixed in M6-V3): a Check since the fix.
        Check(moved < 1e-3f, $"BUG-0600 (minimap): the camera moved {moved:0.00} m on a motion over the minimap with no button held after resume");
        // Clean up: a full left click on the minimap ends any drag.
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = b, GlobalPosition = b, ButtonMask = MouseButtonMask.Left });
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = b, GlobalPosition = b });
        await Frames(2);
    }

    private void SeatsAre(Main main, int f0, int f1, AiDifficulty d, string how)
    {
        Check(main.CurrentMatch != null, $"{how}: no match");
        if (main.CurrentMatch == null) return;
        SimRunner r = main.CurrentMatch.GetNode<SimRunner>("SimRunner");
        World w = r.Simulation!.World;
        var want = new[] { new PlayerSeat(f0, SeatController.Human, AiDifficulty.Normal), new PlayerSeat(f1, SeatController.Ai, d) };
        Check(r.Seats.SequenceEqual(want) && w.Config.Seats.SequenceEqual(want) && w.FactionOf(0) == f0 && w.FactionOf(1) == f1,
            $"{how}: seats [{string.Join(", ", r.Seats)}]");
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

    // ---- helpers ----

    private (Match Match, SimRunner Runner) StartMatch(string[] args)
    {
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        SimRunner runner = match.GetNode<SimRunner>("SimRunner");
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

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
