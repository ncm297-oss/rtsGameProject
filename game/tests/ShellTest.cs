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
/// M6-V2: the <c>--faction</c> / <c>--ai</c> parse and <see cref="SimRunner.BuildSeats"/> rows; the setup's live faction and
/// difficulty lists (data and <c>ui.json</c> text only, BUG-0581 item 1); the picks (sides swapped, Easy) kept over Back and
/// Quit to menu, and each match's seats equal to them.
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
        Check(LaunchOptions.Parse(Array.Empty<string>()) is { MapSize: null, Skirmish: false, Seed: null, UnitsPerPlayer: LaunchOptions.DefaultUnitsPerPlayer, Ai: null },
            "defaults changed");

        // M6-V2: --faction <slot> <id> and --ai <difficulty>.
        Check(LaunchOptions.HasMatchFlag(new[] { "--faction", "2", "whirlwind" }) && LaunchOptions.HasMatchFlag(new[] { "--ai", "hard" }), "--faction / --ai not match flags");
        LaunchOptions none = LaunchOptions.Parse(Array.Empty<string>());
        Check(none.FactionId(0) == null && none.FactionId(1) == null && none.FactionId(2) == null && none.FactionId(-1) == null, "a faction picked with no flag");
        LaunchOptions both = LaunchOptions.Parse(new[] { "--faction", "1", "whirlwind", "--faction", "2", "malazan", "--ai", "easy", "--seed", "4" });
        Check(both.FactionId(0) == "whirlwind" && both.FactionId(1) == "malazan" && both.Ai == AiDifficulty.Easy && both.Seed == 4,
            $"--faction 1 whirlwind --faction 2 malazan --ai easy --seed 4: '{both.FactionId(0)}', '{both.FactionId(1)}', {both.Ai}, {both.Seed}");
        foreach ((string id, AiDifficulty want) in new[] { ("easy", AiDifficulty.Easy), ("normal", AiDifficulty.Normal), ("hard", AiDifficulty.Hard) })
            Check(LaunchOptions.Parse(new[] { "--ai", id }).Ai == want, $"--ai {id} not read");
        foreach (string[] junk in new[]
        {
            new[] { "--ai", "Hard" }, new[] { "--ai", "insane" }, new[] { "--ai", "1" }, new[] { "--ai" }, new[] { "--ai", "--seed", "3" },
        })
        {
            LaunchOptions o = LaunchOptions.Parse(junk);
            Check(o.Ai == null, $"'{string.Join(' ', junk)}' accepted as {o.Ai}");
        }
        Check(LaunchOptions.Parse(new[] { "--ai", "--seed", "3" }).Seed == 3, "--ai swallowed --seed (BUG-0041)");
        foreach (string[] junk in new[]
        {
            new[] { "--faction", "3", "malazan" }, new[] { "--faction", "0", "malazan" }, new[] { "--faction", "whirlwind" }, new[] { "--faction", "2" },
            new[] { "--faction", "2", "Whirl wind!" }, new[] { "--faction", "2", "--ai", "hard" }, new[] { "--faction", "--seed", "3" }, new[] { "--faction" },
        })
        {
            LaunchOptions o = LaunchOptions.Parse(junk);
            Check(o.FactionId(0) == null && o.FactionId(1) == null, $"'{string.Join(' ', junk)}' picked '{o.FactionId(0)}', '{o.FactionId(1)}'");
        }
        Check(LaunchOptions.Parse(new[] { "--faction", "2", "--ai", "hard" }).Ai == AiDifficulty.Hard, "--faction 2 swallowed --ai (BUG-0041)");
        Check(LaunchOptions.Parse(new[] { "--faction", "--seed", "3" }).Seed == 3, "--faction swallowed --seed (BUG-0041)");
        Check(LaunchOptions.Parse(new[] { "--faction", "2", "zzz", "--seed", "5" }) is { Seed: 5 } z && z.FactionId(1) == "zzz",
            "an id-shaped unknown faction is Match's to refuse (it needs the data), and must not eat the next flag");

        // SimRunner's seats: explicit, one per player, the old defaults without picks.
        int factions = _data.Factions.Length;
        var plain = SimRunner.BuildSeats(3, factions, new[] { -1, -1 }, null);
        Check(plain.Length == 3 && Enumerable.Range(0, 3).All(p => plain[p] == new PlayerSeat(p % factions, SeatController.Human, AiDifficulty.Normal)),
            $"default seats {string.Join(", ", plain)}");
        var picked = SimRunner.BuildSeats(3, factions, new[] { 1, 0 }, AiDifficulty.Hard);
        Check(picked[0] == new PlayerSeat(1, SeatController.Human, AiDifficulty.Normal) && picked[1] == new PlayerSeat(0, SeatController.Ai, AiDifficulty.Hard)
            && picked[2] == new PlayerSeat(2 % factions, SeatController.Human, AiDifficulty.Normal), $"picked seats {string.Join(", ", picked)}");
    }

    private void UiRows()
    {
        var keys = new List<(string Section, string Key, string Text)>();
        foreach (MenuText k in Enum.GetValues<MenuText>()) keys.Add(("menu", UiText.Key(k), _ui.Menu(k)));
        foreach (SetupText k in Enum.GetValues<SetupText>()) keys.Add(("setup", UiText.Key(k), _ui.Setup(k)));
        foreach (PauseText k in Enum.GetValues<PauseText>()) keys.Add(("pause", UiText.Key(k), _ui.Pause(k)));
        Check(keys.Count == 3 + 14 + 3, $"{keys.Count} shell keys");
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
            "setup.slot_ai", "setup.faction", "setup.difficulty", "setup.start", "setup.back", "pause.title", "pause.resume", "pause.quit_to_menu",
            "setup.map_size_item", "setup.difficulty_easy", "setup.difficulty_normal", "setup.difficulty_hard" };
        foreach (string n in named) Check(keys.Any(k => $"{k.Section}.{k.Key}" == n), $"no key {n}");
        Check(!keys.Any(k => k.Key == "coming_soon"), "setup.coming_soon is still read (nothing shows it since M6-V2)");

        // BUG-0581 item 1: the map size item is a ui.json format; one that can't format the size is one error naming the key.
        Check(_ui.MapSizeItem(192).Contains("192"), $"map size item '{_ui.MapSizeItem(192)}'");
        foreach (string bad in new[] { "{1} x {1}", "size", "{0", "{0} {x}" })
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
            root["setup"]!["map_size_item"] = bad;
            var errors = new List<string>();
            Check(UiText.Parse(root.ToJsonString(), errors) == null && errors.Count == 1 && errors[0].Contains("setup.map_size_item"),
                $"map_size_item '{bad}': [{string.Join("; ", errors)}]");
        }
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
        // M6-V2: live lists. Each faction list holds every loaded faction's displayName in id order; defaults faction 0 / 1.
        for (int s = 0; s < 2; s++)
        {
            OptionButton list = setup.FactionOptions[s];
            var items = Enumerable.Range(0, list.ItemCount).Select(i => list.GetItemText(i)).ToArray();
            Check(!list.Disabled && items.SequenceEqual(_data.Factions.Select(f => f.DisplayName)) && list.Selected == s && setup.FactionPick(s) == s,
                $"slot {s} faction list: disabled {list.Disabled}, items [{string.Join("|", items)}], selected {list.Selected}");
        }
        OptionButton diff = setup.DifficultyOption;
        var diffItems = Enumerable.Range(0, diff.ItemCount).Select(i => diff.GetItemText(i)).ToArray();
        Check(!diff.Disabled && diffItems.SequenceEqual(new[] { SetupText.DifficultyEasy, SetupText.DifficultyNormal, SetupText.DifficultyHard }.Select(_ui.Setup))
            && setup.DifficultyPick == AiDifficulty.Normal, $"difficulty list: disabled {diff.Disabled}, items [{string.Join("|", diffItems)}], pick {setup.DifficultyPick}");
        var sizeItems = Enumerable.Range(0, setup.MapSizeOption.ItemCount).Select(i => setup.MapSizeOption.GetItemText(i)).ToArray();
        Check(sizeItems.SequenceEqual(LaunchOptions.MapSizes.Select(_ui.MapSizeItem)), $"map size items [{string.Join("|", sizeItems)}] not from setup.map_size_item");
        // No C# literal on the screen (BUG-0581 item 1): every label, button and list item is ui.json or faction data.
        var allowed = new HashSet<string>(Enum.GetValues<SetupText>().Select(_ui.Setup).Concat(LaunchOptions.MapSizes.Select(_ui.MapSizeItem))
            .Concat(_data.Factions.Select(f => f.DisplayName))) { "" };
        foreach (Node n in setup.FindChildren("*", "", true, false))
        {
            string? text = n switch { Label l => l.Text, OptionButton o => null, Button b => b.Text, _ => null };
            if (text != null) Check(allowed.Contains(text), $"setup shows '{text}' ({n.Name}), not from ui.json or data");
        }
        foreach (SetupText k in new[] { SetupText.Title, SetupText.Seed, SetupText.MapSize, SetupText.Faction, SetupText.Difficulty })
            Check(Labels(setup).Contains(_ui.Setup(k)), $"setup has no label '{_ui.Setup(k)}'");
        Check(setup.StartButton.Text == _ui.Setup(SetupText.Start) && setup.BackButton.Text == _ui.Setup(SetupText.Back) && setup.RandomButton.Text == _ui.Setup(SetupText.Random),
            "setup button text");
        Check(setup.MapSizeOption.ItemCount == LaunchOptions.MapSizes.Length && setup.MapSize == 128, $"map sizes {setup.MapSizeOption.ItemCount}, picked {setup.MapSize}");

        // The picks: the sides swapped (You play faction 1, the AI faction 0) and Easy.
        int f0 = 1 % _data.Factions.Length, f1 = 0;
        setup.FactionOptions[0].Select(f0);
        setup.FactionOptions[1].Select(f1);
        setup.DifficultyOption.Select((int)AiDifficulty.Easy);

        // Back and in again: the picks survive.
        await Click(setup.BackButton);
        Check(menu.Visible && !setup.Visible, "Back did not show the menu");
        await Click(menu.SkirmishButton);
        Check(setup.Visible, "Skirmish (second time) did not show the setup");
        Check(setup.FactionPick(0) == f0 && setup.FactionPick(1) == f1 && setup.DifficultyPick == AiDifficulty.Easy,
            $"Back lost the picks: {setup.FactionPick(0)}, {setup.FactionPick(1)}, {setup.DifficultyPick}");

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
        Seats(f0, f1, AiDifficulty.Easy);
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
        Check(setup.FactionPick(0) == f0 && setup.FactionPick(1) == f1 && setup.DifficultyPick == AiDifficulty.Easy,
            $"Quit to menu lost the picks: {setup.FactionPick(0)}, {setup.FactionPick(1)}, {setup.DifficultyPick}");
        await Click(setup.StartButton);
        await StartedMatch(7, 192);
        Seats(f0, f1, AiDifficulty.Easy);
        Check(_main.CurrentMatch != match, "the second match is the first one");
        Check(MatchNodes().Count(n => n.StartsWith(nameof(Match) + " ", StringComparison.Ordinal)) == 1, "not exactly one Match in the tree");
    }

    // M6-V2: the running match's seats are the picks: the runner's, the sim's config's, and World.FactionOf agree.
    private void Seats(int f0, int f1, AiDifficulty difficulty)
    {
        SimRunner runner = _main.CurrentMatch!.GetNode<SimRunner>("SimRunner");
        World w = runner.Simulation!.World;
        var want = new[] { new PlayerSeat(f0, SeatController.Human, AiDifficulty.Normal), new PlayerSeat(f1, SeatController.Ai, difficulty) };
        Check(runner.Seats.SequenceEqual(want) && w.Config.Seats.SequenceEqual(want), $"seats: runner [{string.Join(", ", runner.Seats)}], config [{string.Join(", ", w.Config.Seats)}]");
        Check(w.FactionOf(0) == f0 && w.FactionOf(1) == f1 && w.Config.HasAiSeat, $"FactionOf {w.FactionOf(0)}, {w.FactionOf(1)}");
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
