using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Game.Tests;

/// <summary>
/// M6-V1: the game shell, through the boot scene the game runs (<c>Main.tscn</c> with no user args) and injected input on
/// the viewport. Main menu -> Skirmish -> setup (Random, then seed 7 typed, a letter refused, map 192) -> Start: a match on
/// seed 7 and a 192 x 192 map that ticks. Esc with A armed cancels the targeting and does not pause; Esc then pauses: the
/// tree is paused, no tick advances over half a second, and a click and the A key reach nothing; Esc resumes and ticks move.
/// Pause -> Quit to menu: no match, sim or view node left anywhere in the tree, the tree unpaused, the menu up; Skirmish
/// again keeps seed 7 and 192 and starts a fresh match that ticks. Also: the boot rule (no user args show the menu, any
/// match flag starts the match: <see cref="LaunchOptions.HasMatchFlag"/>), the <c>--map-size</c> and <c>--skirmish</c>
/// parse, and the new <c>ui.json</c> sections (every key present, each missing key one error naming it).
/// </summary>
/// <remarks>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/ShellTest.tscn</c>; prints "SHELL TEST PASS" and exits 0,
/// else "SHELL TEST FAIL: ..." lines and exits 1. Windowed with <c>-- --shots &lt;dir&gt;</c> it saves <c>shell-menu.png</c>,
/// <c>shell-setup.png</c>, <c>shell-match.png</c> and <c>shell-pause.png</c>.
/// </remarks>
public partial class ShellTest : Node
{
    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private UiText _ui = null!;
    private Main _main = null!;
    private string? _shots;

    public override async void _Ready()
    {
        // The shell pauses the tree; the test must keep running through it.
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
            LaunchRows();
            UiRows();
            await Flow();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        foreach (string f in _failures.Take(60)) GD.Print($"SHELL TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("SHELL TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- rows ----

    private void LaunchRows()
    {
        Check(!LaunchOptions.HasMatchFlag(Array.Empty<string>()), "no args counted as a match flag");
        Check(!LaunchOptions.HasMatchFlag(new[] { "--shots", "x" }), "an unknown flag counted as a match flag");
        foreach (string flag in new[] { "--seed", "--units", "--screenshot", "--bench", "--no-fog", "--mute", "--skirmish", "--map-size" })
            Check(LaunchOptions.HasMatchFlag(new[] { flag, "1" }), $"{flag} not a match flag");
        // Every flag Parse reads skips the menu: a flag Parse knows but MatchFlags lacks would boot a dev run into the menu.
        string source = File.ReadAllText(ProjectSettings.GlobalizePath("res://scripts/LaunchOptions.cs"));
        foreach (string flag in source.Split("case \"").Skip(1).Select(s => s[..s.IndexOf('"')]))
            Check(LaunchOptions.MatchFlags.Contains(flag), $"Parse reads {flag}, which is not in MatchFlags");
        foreach (int size in LaunchOptions.MapSizes)
            Check(LaunchOptions.Parse(new[] { "--map-size", size.ToString() }).MapSize == size, $"--map-size {size} not read");
        Check(LaunchOptions.Parse(new[] { "--map-size", "100" }).MapSize == null, "--map-size 100 accepted");
        Check(LaunchOptions.Parse(new[] { "--map-size", "--skirmish" }) is { MapSize: null, Skirmish: true }, "--map-size swallowed --skirmish (BUG-0041)");
        Check(LaunchOptions.Parse(Array.Empty<string>()) is { MapSize: null, Skirmish: false, Seed: null, UnitsPerPlayer: LaunchOptions.DefaultUnitsPerPlayer },
            "defaults changed");
    }

    private void UiRows()
    {
        var keys = new List<(string Section, string Key, string Text)>();
        foreach (MenuText k in Enum.GetValues<MenuText>()) keys.Add(("menu", UiText.Key(k), _ui.Menu(k)));
        foreach (SetupText k in Enum.GetValues<SetupText>()) keys.Add(("setup", UiText.Key(k), _ui.Setup(k)));
        foreach (PauseText k in Enum.GetValues<PauseText>()) keys.Add(("pause", UiText.Key(k), _ui.Pause(k)));
        Check(keys.Count == 3 + 11 + 3, $"{keys.Count} shell keys");
        string json = File.ReadAllText(UiText.DefaultPath).Replace("\r\n", "\n");
        foreach ((string section, string key, string text) in keys)
        {
            Check(text.Length > 0, $"ui.json {section}.{key} is empty");
            var root = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
            root[section]!.AsObject().Remove(key);
            var errors = new List<string>();
            Check(UiText.Parse(root.ToJsonString(), errors) == null && errors.Count == 1 && errors[0].Contains($"{section}.{key}"),
                $"ui.json without {section}.{key}: [{string.Join("; ", errors)}]");
        }
        foreach (string section in new[] { "menu", "setup", "pause" })
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
            root.Remove(section);
            var errors = new List<string>();
            Check(UiText.Parse(root.ToJsonString(), errors) == null && errors.Count == 1 && errors[0].Contains(section), $"ui.json without {section}: [{string.Join("; ", errors)}]");
        }
        // The keys the brief names, by their ui.json spelling.
        string[] named = { "menu.title", "menu.skirmish", "menu.quit", "setup.title", "setup.seed", "setup.random", "setup.map_size", "setup.slot_human",
            "setup.slot_ai", "setup.faction", "setup.difficulty", "setup.coming_soon", "setup.start", "setup.back", "pause.title", "pause.resume", "pause.quit_to_menu" };
        foreach (string n in named) Check(keys.Any(k => $"{k.Section}.{k.Key}" == n), $"no key {n}");
    }

    // ---- the flow ----

    private async Task Flow()
    {
        _main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        _main.UserArgs = Array.Empty<string>();
        AddChild(_main);
        await Frames(2);

        // The menu, with no user args.
        Check(_main.CurrentMatch == null && _main.Menu is { Visible: true } && _main.Setup is { Visible: false } && !_main.Paused,
            $"boot: match {_main.CurrentMatch != null}, menu {_main.Menu?.Visible}, setup {_main.Setup?.Visible}");
        Check(MatchNodes().Count == 0, $"match nodes on the menu: {string.Join(", ", MatchNodes())}");
        MainMenu menu = _main.Menu!;
        Check(menu.SkirmishButton.Text == _ui.Menu(MenuText.Skirmish) && menu.QuitButton.Text == _ui.Menu(MenuText.Quit)
            && Labels(menu).Contains(_ui.Menu(MenuText.Title)), $"menu text: '{menu.SkirmishButton.Text}', '{menu.QuitButton.Text}', labels {string.Join("|", Labels(menu))}");
        await Shot("menu");

        // Skirmish -> setup.
        await Click(menu.SkirmishButton);
        SkirmishSetup setup = _main.Setup!;
        Check(setup.Visible && !menu.Visible, $"Skirmish: setup {setup.Visible}, menu {menu.Visible}");
        Check(setup.SlotLabels[0].Text == _ui.Setup(SetupText.SlotHuman) && setup.SlotLabels[1].Text == _ui.Setup(SetupText.SlotAi),
            $"slots '{setup.SlotLabels[0].Text}', '{setup.SlotLabels[1].Text}'");
        for (int s = 0; s < 2; s++)
            Check(setup.FactionOptions[s].Disabled && setup.FactionOptions[s].GetItemText(0) == _data.Factions[s].DisplayName
                && setup.FactionOptions[s].TooltipText == _ui.Setup(SetupText.ComingSoon), $"slot {s} faction: disabled {setup.FactionOptions[s].Disabled}, '{setup.FactionOptions[s].GetItemText(0)}'");
        Check(setup.DifficultyOption.Disabled && setup.DifficultyOption.GetItemText(0) == _ui.Setup(SetupText.ComingSoon), "difficulty not disabled 'coming soon'");
        foreach (SetupText k in new[] { SetupText.Title, SetupText.Seed, SetupText.MapSize, SetupText.Faction, SetupText.Difficulty })
            Check(Labels(setup).Contains(_ui.Setup(k)), $"setup has no label '{_ui.Setup(k)}'");
        Check(setup.StartButton.Text == _ui.Setup(SetupText.Start) && setup.BackButton.Text == _ui.Setup(SetupText.Back) && setup.RandomButton.Text == _ui.Setup(SetupText.Random),
            "setup button text");
        Check(setup.MapSizeOption.ItemCount == LaunchOptions.MapSizes.Length && setup.MapSize == 128, $"map sizes {setup.MapSizeOption.ItemCount}, picked {setup.MapSize}");

        // Back and in again.
        await Click(setup.BackButton);
        Check(menu.Visible && !setup.Visible, "Back did not show the menu");
        await Click(menu.SkirmishButton);
        Check(setup.Visible, "Skirmish (second time) did not show the setup");

        // Random writes a number; then the seed is typed: 7, a letter (Start greys), Backspace.
        string before = setup.SeedEdit.Text;
        await Click(setup.RandomButton);
        Check(setup.Seed != null && !setup.StartButton.Disabled, $"Random wrote '{setup.SeedEdit.Text}' (was '{before}')");
        await Click(setup.SeedEdit);
        setup.SeedEdit.SelectAll();
        await Type(Key.Key7, '7');
        Check(setup.SeedEdit.Text == "7" && setup.Seed == 7 && !setup.StartButton.Disabled, $"typed seed '{setup.SeedEdit.Text}'");
        await Type(Key.X, 'x');
        Check(setup.Seed == null && setup.StartButton.Disabled, $"seed '{setup.SeedEdit.Text}': Start disabled {setup.StartButton.Disabled}");
        await Type(Key.Backspace, '\0');
        Check(setup.Seed == 7 && !setup.StartButton.Disabled, $"after Backspace '{setup.SeedEdit.Text}'");
        setup.MapSizeOption.Select(Array.IndexOf(LaunchOptions.MapSizes, 192));
        Check(setup.MapSize == 192, $"map size {setup.MapSize}");
        await Shot("setup");

        // Start -> the match.
        await Click(setup.StartButton);
        await StartedMatch(7, 192);
        await Shot("match");
        Match match = _main.CurrentMatch!;
        SimRunner runner = match.GetNode<SimRunner>("SimRunner");
        var sel = match.GetNode<SelectionController>("SelectionController");

        // Esc while A is armed is the targeting's cancel, not the pause.
        EntityHandle own = OwnUnit(runner.Simulation!);
        Check(own.Generation != 0 && sel.SelectOnly(own), "no own unit to select");
        await Type(Key.A, 'a');
        Check(sel.Targeting, "A did not arm attack-move");
        await Type(Key.Escape, '\0');
        Check(!sel.Targeting && !_main.Paused && !GetTree().Paused, $"Esc with A armed: targeting {sel.Targeting}, paused {_main.Paused}");

        // Esc pauses: no tick, no input reaches the match.
        await Type(Key.Escape, '\0');
        PauseMenu pause = _main.Pause!;
        Check(_main.Paused && GetTree().Paused && pause.Visible, $"Esc: paused {_main.Paused}, tree {GetTree().Paused}, menu {pause.Visible}");
        Check(pause.ResumeButton.Text == _ui.Pause(PauseText.Resume) && pause.QuitToMenuButton.Text == _ui.Pause(PauseText.QuitToMenu)
            && Labels(pause).Contains(_ui.Pause(PauseText.Title)), "pause text");
        await Shot("pause");
        int tick = runner.Simulation!.TickNumber;
        int selected = sel.Selection.Count;
        sel.CancelTargeting();
        await Type(Key.A, 'a');
        if (sel.TryScreenPosition(own.Index, out Vector2 at)) await ClickAt(at + new Vector2(200f, 0f)); // a click on open ground would deselect
        // Real time, not frames: headless frames are far shorter than a 50 ms tick. Half a second is 10 ticks at 1x.
        int pausedFrames = await Seconds(0.5);
        Check(runner.Simulation.TickNumber == tick, $"paused: ticks {tick} -> {runner.Simulation.TickNumber} over {pausedFrames} frames");
        Check(!sel.Targeting && sel.Selection.Count == selected, $"paused: A armed {sel.Targeting}, selection {selected} -> {sel.Selection.Count}");

        // Esc resumes.
        await Type(Key.Escape, '\0');
        Check(!_main.Paused && !GetTree().Paused && !pause.Visible, "Esc on the pause menu did not resume");
        await Seconds(0.3);
        Check(runner.Simulation.TickNumber > tick, $"resumed: no tick past {tick}");

        // Pause -> Quit to menu: the match is gone.
        await Type(Key.Escape, '\0');
        Check(_main.Paused, "second pause");
        await Click(pause.QuitToMenuButton);
        await Frames(2);
        Check(_main.CurrentMatch == null && !GetTree().Paused && !_main.Paused && menu.Visible && !setup.Visible,
            $"Quit to menu: match {_main.CurrentMatch != null}, tree paused {GetTree().Paused}, menu {menu.Visible}");
        List<string> left = MatchNodes();
        Check(left.Count == 0, $"match nodes left after Quit to menu: {string.Join(", ", left.Take(10))}");
        Check(!IsInstanceValid(match) && !IsInstanceValid(runner), "the old match or its runner is still a live object");

        // Skirmish again: the picks are kept, a fresh match.
        await Click(menu.SkirmishButton);
        Check(setup.Visible && setup.Seed == 7 && setup.MapSize == 192, $"setup again: seed '{setup.SeedEdit.Text}', map {setup.MapSize}");
        await Click(setup.StartButton);
        await StartedMatch(7, 192);
        Check(_main.CurrentMatch != match, "the second match is the first one");
        Check(MatchNodes().Count(n => n.StartsWith(nameof(Match) + " ", StringComparison.Ordinal)) == 1, "not exactly one Match in the tree");
    }

    // The match is up on `seed` and a `size` map, and ticks.
    private async Task StartedMatch(ulong seed, int size)
    {
        await Frames(2);
        Match? match = _main.CurrentMatch;
        Check(match != null && !_main.Menu!.Visible && !_main.Setup!.Visible, $"Start: match {match != null}, menu {_main.Menu!.Visible}, setup {_main.Setup!.Visible}");
        if (match == null) throw new InvalidOperationException("no match after Start");
        Simulation sim = match.GetNode<SimRunner>("SimRunner").Simulation!;
        Check(sim.World.Config.Seed == seed && sim.World.Heightmap.Width == size && sim.World.Heightmap.Height == size && sim.World.NavGrid.Width == size,
            $"match seed {sim.World.Config.Seed}, map {sim.World.Heightmap.Width} x {sim.World.Heightmap.Height}");
        int tick = sim.TickNumber;
        await Seconds(0.3);
        Check(sim.TickNumber > tick, $"the match does not tick ({tick})");
    }

    private static EntityHandle OwnUnit(Simulation sim)
    {
        var u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == SelectionController.LocalPlayer) return new EntityHandle(i, u.Generation[i]);
        return default;
    }

    // Every node a match brings (its root, runner, views, controllers, HUD), anywhere under the root.
    private List<string> MatchNodes()
    {
        var found = new List<string>();
        Walk(GetTree().Root, found);
        return found;
    }

    private static void Walk(Node n, List<string> found)
    {
        if (n is Match or SimRunner or UnitViews or BuildingViews or TerrainView or SelectionController or RtsCamera or FogOfWar or Minimap or CommandCard
            or AbilityViews or CombatViews or ProjectileViews or PropsView or ResourceBar or SelectionPanel or Sfx or DebugOverlay or Screenshotter)
            found.Add($"{n.GetType().Name} {n.GetPath()}");
        foreach (Node c in n.GetChildren()) Walk(c, found);
    }

    private static List<string> Labels(Node root)
    {
        var texts = new List<string>();
        foreach (Node n in root.FindChildren("*", "Label", true, false)) texts.Add(((Label)n).Text);
        return texts;
    }

    // ---- input ----

    private void Push(InputEvent e) => GetViewport().PushInput(e);

    private async Task Click(Control c)
    {
        Check(c.IsVisibleInTree(), $"{c.Name} is not visible");
        await ClickAt(c.GetGlobalRect().GetCenter());
    }

    private async Task ClickAt(Vector2 at)
    {
        Push(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        await Frames(1);
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Left });
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
        await Frames(2);
    }

    private async Task Type(Key key, char unicode)
    {
        Push(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = unicode, Pressed = true });
        Push(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = unicode, Pressed = false });
        await Frames(2);
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    // Waits at least `seconds` of real time (the sim ticks from frame time); returns the frames waited.
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

    private async Task Shot(string name)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = $"{_shots}/shell-{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"  screenshot {path}");
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
