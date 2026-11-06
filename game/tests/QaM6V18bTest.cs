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
/// QA 2026-11-05-2148 (M6-V18, the setup's Map list), through the shell (<c>Main.tscn</c>):
/// <list type="bullet">
/// <item><c>--map</c> fuzz: 17 junk values (empty, case, padding, dash, space, path separators, a file name, unicode,
/// full-width, a tab, 65 characters, a flag) through <see cref="LaunchOptions.Parse"/> never taken and never swallowing the
/// next flag; id-shaped unknowns parsed and played generated; the last good value wins; real starts for each kind.</item>
/// <item>The command line on a map file: <c>--players 4 / 3 --map</c> capped and warned, 2 not warned, an unknown map not
/// capped; <c>--map-size 256</c>, <c>--forests 0 --mines 0</c> give the file's side and counts with no shortfall line;
/// the halls on the file's starts on level 0 (M6-V19: <c>--map-size 256</c> warned); <c>--players 4 --map</c> with the first two slots on one team (BUG-1066's rule; BUG-1092 fixed in M6-V19).</item>
/// <item>The setup's cap: four slots then the map (two shown, Add disabled and a no-op, the line, the args), the map size
/// restored from 256, a hidden slot's picks kept, a cap that leaves the shown slots on one team refused.</item>
/// <item>The keyboard: with the map picked the focus chain skips Forests and Gold mines (and their boxes); the greyed
/// Map size takes no key; Generated gives back exactly the chain from before.</item>
/// <item>Layout at 1152 x 648 and 1280 x 720 with four slots (Generated) and with the map picked: Start on screen, the
/// line inside its holder and clear of the buttons, Add / Remove not moved.</item>
/// <item>A Raraku + High AI match from the setup: saved at 0:10 (tick 200) and 3:00 (tick 3,600), recorded to 4,600
/// (format 9); Watch to every checkpoint with the end hash; each save loads through Load Game with its hash and reaches the
/// uninterrupted match's hash later; a re-save of the loaded match is format 5 and loads.</item>
/// <item>Load Game with format 4 / 5 saves and 6 / 7 / 8 / 9 recordings side by side (generated FFA, teamed, High;
/// Raraku Standard, Raraku High): every cell as documented, nothing unreadable; the 7 / 8 / 9 rows watch to the end.</item>
/// <item><see cref="SimRunner.StartPlayback"/> refuses a format-9 recording with more players than the map's starts.</item>
/// </list>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6V18bTest.tscn</c>; prints "QA M6-V18b TEST PASS" or
/// "QA M6-V18b TEST FAIL: ..." lines; rows for open bugs print "QA M6-V18b KNOWN BUG-nnnn" and fail only with <c>-- --strict</c>. Windowed with <c>-- --shots &lt;dir&gt;</c> it saves the setups and the Load Game list.
/// </summary>
public partial class QaM6V18bTest : Node
{
    private const double FrameSeconds = 0.25; // five ticks a frame
    private const int EarlySave = 200, LateSave = 3600, EndTick = 4600;

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private UiText _ui = null!;
    private Main _main = null!;
    private MapDef _map = null!;
    private StartingResourcesDef _high = null!;
    private string? _shots;
    private string _root = "", _replays = "", _saves = "";

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
            _map = _data.Maps.First(m => m.Key == "raraku");
            _high = _data.Rules.StartingResources.First(p => p.Key == "high");
            _root = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://qa-m6-v18b-{OS.GetProcessId()}"));
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            _replays = Path.Combine(_root, "replays");
            _saves = Path.Combine(_root, "saves");
            Directory.CreateDirectory(_replays);
            Directory.CreateDirectory(_saves);
            _main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
            _main.UserArgs = new[] { Main.NoConfineFlag };
            _main.SettingsPath = $"user://qa-m6-v18b-{OS.GetProcessId()}/settings.cfg";
            _main.ReplayDir = _replays;
            _main.SaveDir = _saves;
            _main.BeforeStart = m => m.GetNode<SimRunner>("SimRunner").FixedFrameDelta = FrameSeconds;
            AddChild(_main);
            await Frames(2);
            try
            {
                await MapFuzz();
                await CommandLineOnTheMap();
                await SetupCap();
                await Keyboard();
                foreach (Vector2I size in new[] { new Vector2I(1152, 648), new Vector2I(1280, 720) })
                {
                    GetTree().Root.Size = size;
                    await Frames(2);
                    await Layout(size, mapPicked: false);
                    await Layout(size, mapPicked: true);
                }
                GetTree().Root.Size = new Vector2I(1152, 648);
                await Frames(2);
                await RarakuHighAiMatch();
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
        foreach (string f in _failures.Take(60)) GD.Print($"QA M6-V18b TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M6-V18b TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- --map fuzz ----

    private async Task MapFuzz()
    {
        string[] junk =
        {
            "", "Raraku", "RARAKU", " raraku", "raraku ", "ra-raku", "ra raku", "../raraku", "maps/raraku", "maps\\raraku",
            "raraku.json", "räraku", "ｒaraku", "raraku\t", "日本", new string('r', DataLimits.MaxMapIdLength + 1), "res://data/maps/raraku",
        };
        foreach (string value in junk)
        {
            LaunchOptions o = LaunchOptions.Parse(new[] { "--map", value, "--seed", "9" });
            Check(o.MapId == null && o.Seed == 9 && Match.MapFor(_data, o.MapId) == null, $"--map '{value}': parsed '{o.MapId}', seed {o.Seed}");
        }
        // A flag is never the value; a missing value at the end is junk.
        LaunchOptions flag = LaunchOptions.Parse(new[] { "--map", "--seed", "9" });
        Check(flag.MapId == null && flag.Seed == 9, $"--map --seed 9: map '{flag.MapId}', seed {flag.Seed}");
        Check(LaunchOptions.Parse(new[] { "--map" }).MapId == null, "--map with no value");
        // Id-shaped but unknown: parsed, played generated (Match's check).
        foreach (string value in new[] { "no_such_map", "1", "_", new string('a', DataLimits.MaxMapIdLength), "raraku_" })
        {
            LaunchOptions o = LaunchOptions.Parse(new[] { "--map", value });
            Check(o.MapId == value && Match.MapFor(_data, o.MapId) == null, $"--map '{value}': parsed '{o.MapId}'");
        }
        Check(LaunchOptions.Parse(new[] { "--map", new string('r', DataLimits.MaxMapIdLength) }).MapId?.Length == DataLimits.MaxMapIdLength, "the 64-character bound");
        // The last good value wins.
        Check(LaunchOptions.Parse(new[] { "--map", "raraku", "--map", "Junk" }).MapId == "raraku", "--map raraku --map Junk");
        Check(LaunchOptions.Parse(new[] { "--map", "raraku", "--map", "no_such_map" }).MapId == "no_such_map", "--map raraku --map no_such_map");
        // Real starts: junk / unknown play generated; raraku plays the file.
        foreach ((string[] extra, bool onFile) in new[]
        {
            (new[] { "--map", "../raraku" }, false), (new[] { "--map", "räraku" }, false), (new[] { "--map", "RARAKU" }, false),
            (new[] { "--map", "raraku", "--map", "no_such_map" }, false), (new[] { "--map", "raraku", "--map", "ra raku" }, true),
            (new[] { "--map", "raraku" }, true),
        })
        {
            string how = string.Join(' ', extra);
            _main.StartSkirmish(new[] { "--seed", "5", "--units", "0", "--mute" }.Concat(extra).ToArray());
            Match? m = _main.CurrentMatch;
            if (!Check(m?.Simulation != null, $"[{how}] started no match")) continue;
            World w = m!.Simulation!.World;
            Check(w.Config.MapId == (onFile ? _map.Key : "") && w.NavGrid.Width == 128 && m.StartLine.Contains("map file", StringComparison.Ordinal) == onFile,
                $"[{how}]: map '{w.Config.MapId}', line '{m.StartLine}'");
            await Ticks(m.Simulation!, 20);
            await QuitMatch();
        }
    }

    // ---- the command line on the map file ----

    private async Task CommandLineOnTheMap()
    {
        foreach ((string[] args, int players, bool warned, string mapId) in new[]
        {
            (new[] { "--players", "4", "--map", "raraku" }, 2, true, "raraku"),
            (new[] { "--players", "3", "--map", "raraku" }, 2, true, "raraku"),
            (new[] { "--players", "2", "--map", "raraku" }, 2, false, "raraku"),
            (new[] { "--map", "raraku", "--players", "4" }, 2, true, "raraku"),
            (new[] { "--players", "4", "--map", "no_such_map" }, 4, false, ""),
        })
        {
            string how = string.Join(' ', args);
            _main.StartSkirmish(args.Concat(new[] { "--seed", "4", "--units", "0", "--mute", "--skirmish" }).ToArray());
            Match? m = _main.CurrentMatch;
            if (!Check(m?.Simulation != null, $"[{how}] started no match")) continue;
            World w = m!.Simulation!.World;
            Check(w.Config.PlayerCount == players && w.Config.MapId == mapId && (m.MapPlayersWarning.Length > 0) == warned,
                $"[{how}]: {w.Config.PlayerCount} players on '{w.Config.MapId}', warning '{m.MapPlayersWarning}'");
            await Ticks(m.Simulation!, 50);
            Check(!_main.BannerUp, $"[{how}]: ended at once");
            await QuitMatch();
        }

        // The generator's flags don't apply on a map file: the file's side and counts, no shortfall line; halls on its starts.
        _main.StartSkirmish(new[] { "--seed", "6", "--units", "0", "--mute", "--map-size", "256", "--forests", "0", "--mines", "0", "--map", "raraku" });
        Match? f = _main.CurrentMatch;
        if (Check(f?.Simulation != null, "--map-size 256 --map raraku started no match"))
        {
            World w = f!.Simulation!.World;
            Check(w.NavGrid.Width == _map.Size && w.Config.Map == MapGenParams.Default with { Width = _map.Size, Height = _map.Size } && f.ShortfallLine == ""
                && f.StartLine.Contains($"forests {_map.ForestCount} ", StringComparison.Ordinal)
                && f.MapSizeWarning.Contains("--map-size 256", StringComparison.Ordinal) && f.MapSizeWarning.Contains($"playing {_map.Size}", StringComparison.Ordinal),
                $"--map-size 256 --forests 0 --mines 0 --map raraku: side {w.NavGrid.Width}, params {w.Config.Map}, line '{f.StartLine}', shortfall '{f.ShortfallLine}', map-size warning '{f.MapSizeWarning}' (M6-V19)");
            await Ticks(f.Simulation!, 5);
            foreach ((int p, int x, int y) in new[] { (0, 22, 64), (1, 105, 63) })
            {
                Check(w.StartLocations[p].Cell == y * _map.Size + x && w.Heightmap.LevelAt(x, y) == 0 && HallCovers(w, p, x, y),
                    $"player {p}: start {w.StartLocations[p].Cell % _map.Size},{w.StartLocations[p].Cell / _map.Size} level {w.Heightmap.LevelAt(x, y)}, hall over ({x}, {y}) {HallCovers(w, p, x, y)}");
            }
            await QuitMatch();
        }

        // BUG-1066's rule after the cap: four players asked, slots 1 and 2 on team 1, slot 3 on team 2 (two sides as asked);
        // the map keeps two players, both on team 1: one side, a match that never ends. The rule says free-for-all.
        string[] oneSide = { "--players", "4", "--map", "raraku", "--team", "1", "1", "--team", "2", "1", "--team", "3", "2", "--seed", "4", "--units", "0", "--mute", "--skirmish" };
        _main.StartSkirmish(oneSide);
        Match? o = _main.CurrentMatch;
        if (Check(o?.Simulation != null, "the capped one-side match started no match"))
        {
            World w = o!.Simulation!.World;
            GD.Print($"  capped one side: {w.Config.PlayerCount} players, teams {w.TeamOf(0)},{w.TeamOf(1)}, enemies {w.IsEnemy(0, 1)}");
            // BUG-1092, fixed in M6-V19: Match runs the rule again over the capped players (warned, the teams dropped).
            Check(w.Config.PlayerCount == 2 && w.IsEnemy(0, 1) && o.OneTeamWarning.Contains("--team", StringComparison.Ordinal),
                $"[--players 4 --map raraku --team 1 1 --team 2 1 --team 3 2]: {w.Config.PlayerCount} players on teams {w.TeamOf(0)} / {w.TeamOf(1)}, enemies {w.IsEnemy(0, 1)}, warning '{o.OneTeamWarning}': one side, the match can never end (BUG-1092)");
            await QuitMatch();
        }
    }

    private bool HallCovers(World w, int p, int x, int y)
    {
        var b = w.Buildings;
        for (int i = 0; i < b.Capacity; i++)
        {
            if (!b.Alive[i] || b.Owner[i] != p) continue;
            BuildingDef def = _data.Buildings[b.TypeId[i]];
            int ax = b.Cell[i] % w.NavGrid.Width, ay = b.Cell[i] / w.NavGrid.Width;
            if (x >= ax && x < ax + def.FootprintWidth && y >= ay && y < ay + def.FootprintHeight) return true;
        }
        return false;
    }

    // ---- the setup's cap ----

    private async Task SetupCap()
    {
        _main.ShowSetup();
        await Frames(2);
        SkirmishSetup setup = _main.Setup!;
        setup.PickMap("");
        while (setup.SlotCount < SkirmishSetup.MaxSlots) setup.AddSlot();
        Click(setup.MapSizeOption, 1); // 256
        int size256 = setup.MapSizeOption.Selected;
        Click(setup.FactionOptions[2], (setup.FactionOptions[2].Selected + 1) % setup.FactionOptions[2].ItemCount);
        int slot3Faction = setup.FactionOptions[2].Selected;
        Rect2 add = setup.AddSlotButton.GetGlobalRect(), remove = setup.RemoveSlotButton.GetGlobalRect();

        Click(setup.MapOption, _map.Id + 1);
        await Frames(1);
        string[]? args = setup.MatchArgs();
        string joined = args == null ? "(null)" : string.Join(' ', args);
        Check(setup.SlotCount == _map.Players && setup.AddSlotButton.Disabled && setup.MapSizeOption.Disabled
            && LaunchOptions.MapSizes[setup.MapSizeOption.Selected] == _map.Size && !setup.ForestsSpin.Editable && !setup.MinesSpin.Editable
            && setup.MapPlayersLabel.IsVisibleInTree() && setup.MapPlayersLabel.Text == _ui.MapPlayersLine(_map.DisplayName, _map.Players)
            && !setup.StartButton.Disabled,
            $"the map picked from four slots: {setup.SlotCount} slots, Add disabled {setup.AddSlotButton.Disabled}, size {setup.MapSizeOption.Selected}, line '{setup.MapPlayersLabel.Text}'");
        Check(args != null && joined.Contains("--map raraku", StringComparison.Ordinal) && joined.Contains("--players 2", StringComparison.Ordinal)
            && joined.Contains($"--map-size {_map.Size}", StringComparison.Ordinal) && !joined.Contains("--team 3", StringComparison.Ordinal) && !joined.Contains("--faction 3", StringComparison.Ordinal),
            $"the map's args: {joined}");
        Check(setup.MapOption.TooltipText == TooltipWrap.Wrap(_map.Description), $"the list's tooltip '{setup.MapOption.TooltipText}'");
        // Add player: the button and the method are both no-ops.
        setup.AddSlotButton.EmitSignal(BaseButton.SignalName.Pressed);
        setup.AddSlot();
        Check(setup.SlotCount == _map.Players, $"Add player on the map: {setup.SlotCount} slots");
        Check(setup.AddSlotButton.GetGlobalRect() == add && setup.RemoveSlotButton.GetGlobalRect() == remove,
            $"Add / Remove moved with the line: {setup.AddSlotButton.GetGlobalRect()} / {setup.RemoveSlotButton.GetGlobalRect()} vs {add} / {remove}");
        // Picking the map again (same item) changes nothing; Generated gives back 4 slots, 256, the slot's pick, the rows.
        Click(setup.MapOption, _map.Id + 1);
        Click(setup.MapOption, 0);
        await Frames(1);
        Check(setup.SlotCount == SkirmishSetup.MaxSlots && setup.MapSizeOption.Selected == size256 && !setup.MapSizeOption.Disabled
            && setup.ForestsSpin.Editable && setup.MinesSpin.Editable && !setup.MapPlayersLabel.Visible && setup.AddSlotButton.Disabled
            && setup.FactionOptions[2].Selected == slot3Faction && setup.MapOption.TooltipText == "",
            $"Generated again: {setup.SlotCount} slots, size {setup.MapSizeOption.Selected} (want {size256}), slot 3 faction {setup.FactionOptions[2].Selected} (want {slot3Faction}), tooltip '{setup.MapOption.TooltipText}'");

        // A cap that leaves the shown slots on one team: slots 1-2 on team 1, 3-4 on team 2; the map keeps 1-2: refused.
        PickTeam(setup, 0, 1); PickTeam(setup, 1, 1); PickTeam(setup, 2, 2); PickTeam(setup, 3, 2);
        Check(!setup.OneTeam && !setup.StartButton.Disabled, "1,1,2,2 on four slots is two sides");
        Click(setup.MapOption, _map.Id + 1);
        await Frames(1);
        Check(setup.OneTeam && setup.StartButton.Disabled && setup.OneTeamLabel.IsVisibleInTree(), $"the map's cap left 1,1 on one team: one team {setup.OneTeam}, Start disabled {setup.StartButton.Disabled}");
        Check(!setup.OneTeamLabel.GetGlobalRect().Intersects(setup.MapPlayersLabel.GetGlobalRect()), "the one-team line overlaps the map's line");
        await Shot("setup-map-one-team");
        Click(setup.MapOption, 0);
        await Frames(1);
        Check(!setup.OneTeam && !setup.StartButton.Disabled, "Generated again: two sides");
        for (int s = 0; s < SkirmishSetup.MaxSlots; s++) PickTeam(setup, s, 0);
        while (setup.SlotCount > 2) setup.RemoveSlot();
        setup.MapSizeOption.Select(0);
        _main.ShowMenu();
        await Frames(1);
    }

    // ---- the keyboard ----

    private async Task Keyboard()
    {
        _main.ShowSetup();
        await Frames(2);
        SkirmishSetup setup = _main.Setup!;
        setup.PickMap("");
        await Frames(1);
        List<Control> before = FocusChain(setup.SeedEdit);
        Click(setup.MapOption, _map.Id + 1);
        await Frames(1);
        List<Control> picked = FocusChain(setup.SeedEdit);
        Control[] greyed = { setup.ForestsSpin, setup.ForestsSpin.GetLineEdit(), setup.MinesSpin, setup.MinesSpin.GetLineEdit() };
        foreach (Control c in greyed)
            Check(!picked.Contains(c) && c.FocusMode == Control.FocusModeEnum.None, $"the keyboard reaches greyed {c.GetPath()} (focus {c.FocusMode})");
        // The greyed map size: if focusable, no key opens it or changes it.
        int sizeBefore = setup.MapSizeOption.Selected;
        if (setup.MapSizeOption.FocusMode != Control.FocusModeEnum.None)
        {
            setup.MapSizeOption.GrabFocus();
            await Frames(1);
            foreach (Key k in new[] { Key.Down, Key.Up, Key.Enter, Key.Space, Key.KpEnter, Key.Right, Key.Left })
            {
                GetViewport().PushInput(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = true });
                GetViewport().PushInput(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = false });
                await Frames(1);
                Check(setup.MapSizeOption.Selected == sizeBefore && !setup.MapSizeOption.GetPopup().Visible, $"key {k} on the greyed map size: item {setup.MapSizeOption.Selected}, popup {setup.MapSizeOption.GetPopup().Visible}");
                setup.MapSizeOption.GetPopup().Hide();
            }
            setup.MapSizeOption.ReleaseFocus();
        }
        GD.Print($"  focus chain: generated {before.Count}, map {picked.Count}: {string.Join(" > ", picked.Select(c => c.Name.ToString()))}");
        Click(setup.MapOption, 0);
        await Frames(1);
        List<Control> after = FocusChain(setup.SeedEdit);
        Check(after.SequenceEqual(before), $"Generated again changes the focus chain: before [{string.Join(", ", before.Select(c => c.Name.ToString()))}], after [{string.Join(", ", after.Select(c => c.Name.ToString()))}]");
        _main.ShowMenu();
        await Frames(1);
    }

    // The Tab chain from `start` until it comes back (or 80 stops).
    private static List<Control> FocusChain(Control start)
    {
        var chain = new List<Control>();
        Control? c = start;
        for (int i = 0; i < 80 && c != null; i++)
        {
            if (chain.Contains(c)) break;
            chain.Add(c);
            c = c.FindNextValidFocus();
        }
        return chain;
    }

    // ---- layout ----

    private async Task Layout(Vector2I size, bool mapPicked)
    {
        string how = $"{size.X}x{size.Y} {(mapPicked ? "map" : "generated")}";
        _main.ShowSetup();
        await Frames(2);
        SkirmishSetup setup = _main.Setup!;
        setup.PickMap("");
        // The map's line must not move the buttons: compare with Generated at the map's slot count (the cap hides rows,
        // which re-centres the column on its own).
        while (setup.SlotCount > (mapPicked ? _map.Players : 0) && setup.SlotCount > LaunchOptions.MinPlayers) setup.RemoveSlot();
        while (setup.SlotCount < (mapPicked ? _map.Players : SkirmishSetup.MaxSlots)) setup.AddSlot();
        await Frames(2);
        Rect2 add = setup.AddSlotButton.GetGlobalRect(), start4 = setup.StartButton.GetGlobalRect();
        if (mapPicked)
        {
            // From four slots, so the cap runs; then the rects at the capped count.
            Click(setup.MapOption, 0);
            while (setup.SlotCount < SkirmishSetup.MaxSlots) setup.AddSlot();
            Click(setup.MapOption, _map.Id + 1);
        }
        await Frames(2);
        Rect2 view = GetViewport().GetVisibleRect();
        foreach (Control c in new Control[] { setup.MapOption, setup.MapSizeOption, setup.SpeedOption, setup.ResourcesOption, setup.ForestsSpin, setup.MinesSpin,
            setup.AddSlotButton, setup.RemoveSlotButton, setup.BackButton, setup.StartButton, setup.SeedEdit })
            Check(view.Grow(0.5f).Encloses(c.GetGlobalRect()), $"{how}: {c.Name} {c.GetGlobalRect()} leaves the window");
        Rect2 start = setup.StartButton.GetGlobalRect();
        Check(start.End.Y <= view.End.Y - 4f, $"{how}: Start's bottom {start.End.Y} within 4 px of {view.End.Y}");
        // No two of the grid's lists overlap.
        Control[] lists = { setup.MapOption, setup.MapSizeOption, setup.SpeedOption, setup.ResourcesOption, setup.ForestsSpin, setup.MinesSpin, setup.SeedEdit };
        for (int a = 0; a < lists.Length; a++)
            for (int b = a + 1; b < lists.Length; b++)
                Check(!lists[a].GetGlobalRect().Intersects(lists[b].GetGlobalRect()), $"{how}: {lists[a].Name} overlaps {lists[b].Name}");
        if (mapPicked)
        {
            Label line = setup.MapPlayersLabel;
            Rect2 r = line.GetGlobalRect(), holder = ((Control)line.GetParent()).GetGlobalRect();
            Check(view.Grow(0.5f).Encloses(r) && line.GetMinimumSize().Y <= r.Size.Y + 0.5f && line.GetLineCount() <= Math.Max(1, line.GetVisibleLineCount()),
                $"{how}: the map line {r} needs {line.GetMinimumSize()} / {line.GetLineCount()} lines (holder {holder})");
            foreach (Control c in new Control[] { setup.AddSlotButton, setup.RemoveSlotButton, setup.StartButton, setup.BackButton })
                Check(!r.Intersects(c.GetGlobalRect()), $"{how}: the map line {r} overlaps {c.Name} {c.GetGlobalRect()}");
            Check(setup.AddSlotButton.GetGlobalRect() == add && start == start4, $"{how}: Add {setup.AddSlotButton.GetGlobalRect()} / Start {start} moved (were {add} / {start4})");
            GD.Print($"  {how}: line '{line.Text}' {r} ({line.GetLineCount()} lines), Start {start}");
        }
        else GD.Print($"  {how}: Start {start}, map {setup.MapOption.GetGlobalRect()}, size {setup.MapSizeOption.GetGlobalRect()}");
        await Shot($"setup-{size.X}-{(mapPicked ? "map" : "generated")}");
        setup.PickMap("");
        while (setup.SlotCount > 2) setup.RemoveSlot();
        _main.ShowMenu();
        await Frames(1);
    }

    // ---- a Raraku + High AI match: saves at 0:10 and 3:00, Watch, Load ----

    private async Task RarakuHighAiMatch()
    {
        _main.ShowSetup();
        await Frames(2);
        SkirmishSetup setup = _main.Setup!;
        Check(setup.PickResources(_high.Key) && setup.PickMap(_map.Key), "no high / raraku in the lists");
        setup.StartButton.EmitSignal(BaseButton.SignalName.Pressed);
        Match? match = _main.CurrentMatch;
        if (!Check(match?.Simulation != null, "Raraku High: Start started no match")) return;
        Simulation sim = match!.Simulation!;
        Check(sim.World.Config.MapId == _map.Key && sim.World.Config.StartingResources == _high.Key && match.Seats.Any(s => s.Controller == SeatController.Ai),
            $"Raraku High: map '{sim.World.Config.MapId}', preset '{sim.World.Config.StartingResources}'");
        string? replayPath = _main.ReplayPath;
        var hashes = new Dictionary<int, ulong>();
        var saves = new List<(int Tick, string Path, ulong Hash)>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        foreach (int mark in new[] { EarlySave, EarlySave + 400, LateSave, EndTick })
        {
            while (sim.TickNumber < mark && clock.Elapsed.TotalSeconds < 900) await Frames(1);
            _main.PauseMatch();
            await Frames(1);
            Check(sim.TickNumber == mark, $"Raraku High: paused at {sim.TickNumber} (want {mark})");
            hashes[sim.TickNumber] = sim.StateHash();
            if (mark is EarlySave or LateSave && Check(_main.SaveGame() && _main.LastSavePath != null, $"Raraku High: no save at {mark}"))
                saves.Add((mark, _main.LastSavePath!, sim.StateHash()));
            if (mark != EndTick) _main.Resume();
        }
        _main.QuitToMenu();
        await Frames(2);
        if (!Check(replayPath != null && File.Exists(replayPath), $"Raraku High: no recording at {replayPath}")) return;
        string[] lines = File.ReadAllLines(replayPath!);
        int presetLine = Array.IndexOf(lines, $"starting-resources {_high.Key}");
        Check(lines[0] == "rts-replay 9" && presetLine > 0 && lines[presetLine + 1] == $"map {_map.Key}", $"Raraku High recording: '{lines[0]}', preset line {presetLine}");
        foreach ((int tick, string path, ulong _) in saves)
        {
            byte[] head = File.ReadAllBytes(path);
            Check(SaveSummary.TryParse(head, out SaveSummary? s) && s!.FormatVersion == SaveFormat.CurrentVersion && s.MapId == _map.Key && s.StartingResources == _high.Key && s.Tick == tick,
                $"Raraku High save {tick}: format {s?.FormatVersion}, map '{s?.MapId}', preset '{s?.StartingResources}'");
        }

        _main.ShowLoadGame();
        await Frames(2);
        LoadGameScreen load = _main.LoadScreen!;
        int row = load.RowOf(replayPath!);
        if (Check(row >= 0 && load.Select(row) && !load.WatchButton.Disabled, "Raraku High: the recording can't be watched"))
        {
            string cell = load.List.GetRoot().GetChild(row).GetText(2);
            string want = _ui.SeatsWithMap(_ui.SeatsWithPreset(load.SeatsText(load.Entries.First(e => e.Path == replayPath).Seats), _high.DisplayName), _map.DisplayName);
            Check(cell == want, $"Raraku High replay cell '{cell}' (want '{want}')");
            load.WatchButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);
            Match? watched = _main.CurrentMatch;
            if (Check(watched is { IsPlayback: true }, "Raraku High: Watch started no playback"))
            {
                SimRunner runner = watched!.GetNode<SimRunner>("SimRunner");
                runner.FixedFrameDelta = FrameSeconds * 4;
                Check(runner.Simulation!.World.Config.MapId == _map.Key && runner.Simulation.World.Config.StartingResources == _high.Key, "Raraku High playback config");
                var w = System.Diagnostics.Stopwatch.StartNew();
                while (runner.PlaybackState == PlaybackState.Playing && w.Elapsed.TotalSeconds < 900) await Frames(1);
                Replay replay = runner.Playback!;
                Check(runner.PlaybackState == PlaybackState.Ended && runner.PlaybackError == ReplayError.None && runner.CheckpointsCompared == replay.Checkpoints.Length
                    && replay.Checkpoints.Length == EndTick / ReplayRecorder.DefaultCheckpointInterval && runner.Simulation!.StateHash() == hashes[EndTick],
                    $"Raraku High Watch: {runner.PlaybackState} ({runner.PlaybackError}) {runner.CheckpointsCompared}/{replay.Checkpoints.Length}, end hash equal {runner.Simulation?.StateHash() == hashes[EndTick]}");
                GD.Print($"  Raraku High Watch: {runner.CheckpointsCompared}/{replay.Checkpoints.Length} checkpoints, {replay.Commands.Length} commands");
                await QuitMatch();
            }
        }
        // A recording with more players than the map's starts: refused before any sim.
        if (ReplayFormat.TryReadFile(replayPath!, out Replay? rec) == ReplayError.None)
        {
            var runner = new SimRunner();
            AddChild(runner);
            Replay three = Copy(rec!, players: _map.Players + 1);
            ReplayError e = runner.StartPlayback(_data, three);
            Check(e == ReplayError.InvalidHeader && runner.Simulation == null, $"StartPlayback of a 3-player Raraku recording: {e}");
            RemoveChild(runner);
            runner.QueueFree();
        }

        foreach ((int tick, string path, ulong hash) in saves)
        {
            _main.ShowLoadGame();
            await Frames(2);
            int saveRow = load.RowOf(path);
            if (!Check(saveRow >= 0 && load.Select(saveRow) && !load.LoadButton.Disabled, $"Raraku High save {tick}: not loadable")) continue;
            load.LoadButton.EmitSignal(BaseButton.SignalName.Pressed);
            Match? m = _main.CurrentMatch;
            if (!Check(m is { IsLoaded: true } && m.Simulation != null, $"Raraku High save {tick}: Load resumed nothing")) continue;
            Simulation s = m!.Simulation!;
            Check(s.TickNumber == tick && s.StateHash() == hash && s.World.Config.MapId == _map.Key && s.World.Config.StartingResources == _high.Key
                && m.StartLine.Contains($"map file {_map.Key}", StringComparison.Ordinal),
                $"Raraku High save {tick}: tick {s.TickNumber}, hash equal {s.StateHash() == hash}, map '{s.World.Config.MapId}', line '{m.StartLine}'");
            int later = tick == EarlySave ? EarlySave + 400 : EndTick;
            var c = System.Diagnostics.Stopwatch.StartNew();
            while (s.TickNumber < later && c.Elapsed.TotalSeconds < 300) await Frames(1);
            _main.PauseMatch();
            await Frames(1);
            Check(s.TickNumber == later && s.StateHash() == hashes[later], $"Raraku High save {tick}: at {s.TickNumber} hash equal to the uninterrupted run {s.StateHash() == hashes.GetValueOrDefault(later)}");
            if (tick == EarlySave)
            {
                ulong againHash = s.StateHash();
                if (Check(_main.SaveGame() && _main.LastSavePath != null, "Raraku: the loaded match did not save"))
                {
                    string resaved = _main.LastSavePath!;
                    Check(SaveSummary.TryParse(File.ReadAllBytes(resaved), out SaveSummary? sum) && sum!.MapId == _map.Key && sum.FormatVersion == SaveFormat.CurrentVersion,
                        $"Raraku re-save: format {sum?.FormatVersion}, map '{sum?.MapId}'");
                    await QuitMatch();
                    Check(_main.LoadSavedGame(resaved) && _main.CurrentMatch!.Simulation!.StateHash() == againHash && _main.CurrentMatch.Simulation.World.Config.MapId == _map.Key,
                        "Raraku re-save: --load refused or the hash differs");
                }
            }
            await QuitMatch();
        }
    }

    // ---- Load Game: formats 4 / 5 saves and 6 / 7 / 8 / 9 recordings ----

    private async Task MixedFormats()
    {
        var files = new List<(string Replay, string Save, string Preset, string Map, int Format, int SaveFormat, string How)>();
        foreach ((string preset, string map, int[] teams, int format, int saveFormat, string how) in new[]
        {
            (StartingResourcesDef.StandardId, "", new[] { 0, 0 }, 6, 4, "generated ffa"),
            (StartingResourcesDef.StandardId, "", new[] { 1, 2, 0 }, 7, 4, "generated teamed"),
            (_high.Key, "", new[] { 0, 0 }, 8, 4, "generated high"),
            (StartingResourcesDef.StandardId, _map.Key, new[] { 0, 0 }, 9, 5, "raraku standard"),
            (_high.Key, _map.Key, new[] { 1, 2 }, 9, 5, "raraku high teamed"),
        })
        {
            _main.ShowSetup();
            await Frames(2);
            SkirmishSetup setup = _main.Setup!;
            setup.PickMap("");
            while (setup.SlotCount < teams.Length) setup.AddSlot();
            while (setup.SlotCount > teams.Length) setup.RemoveSlot();
            for (int s = 0; s < SkirmishSetup.MaxSlots; s++) PickTeam(setup, s, s < teams.Length ? teams[s] : 0);
            setup.PickResources(preset);
            setup.PickMap(map);
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
            await Ticks(m.Simulation!, 100);
            await QuitMatch();
            if (replay == null || save == null || !File.Exists(replay)) { Check(false, $"{how}: files {replay} / {save}"); continue; }
            string head = File.ReadLines(replay).First();
            Check(head == $"rts-replay {format}", $"{how}: recording '{head}' (want format {format})");
            Check(SaveSummary.TryParse(File.ReadAllBytes(save), out SaveSummary? sum) && sum!.FormatVersion == saveFormat && sum.MapId == map,
                $"{how}: save format {sum?.FormatVersion} (want {saveFormat}), map '{sum?.MapId}'");
            files.Add((replay, save, preset, map, format, saveFormat, how));
        }
        _main.ShowLoadGame();
        await Frames(2);
        LoadGameScreen load = _main.LoadScreen!;
        Check(load.Unreadable.Count == 0 && load.UnreadableSaves.Count == 0, $"unreadable rows: {string.Join(", ", load.Unreadable.Concat(load.UnreadableSaves).Select(Path.GetFileName))}");
        foreach ((string replay, string save, string preset, string map, int format, int _, string how) in files)
            foreach (string path in new[] { replay, save })
            {
                int row = load.RowOf(path);
                if (!Check(row >= 0, $"{how}: {Path.GetFileName(path)} not listed")) continue;
                string cell = load.List.GetRoot().GetChild(row).GetText(2);
                var seats = load.KindOf(row) == LoadGameScreen.RowKind.Save ? load.Saves.First(s => s.Path == path).Seats : load.Entries.First(e => e.Path == path).Seats;
                string want = load.SeatsText(seats);
                if (preset != StartingResourcesDef.StandardId) want = _ui.SeatsWithPreset(want, _high.DisplayName);
                if (map.Length > 0) want = _ui.SeatsWithMap(want, _map.DisplayName);
                Check(cell == want, $"{how}: {Path.GetFileName(path)} cell '{cell}' (want '{want}')");
                GD.Print($"  {how} {(load.KindOf(row) == LoadGameScreen.RowKind.Save ? "save" : "replay")}: '{cell}'");
            }
        await Shot("load-mixed");
        foreach (int format in new[] { 7, 8, 9 })
        {
            var f = files.FirstOrDefault(x => x.Format == format);
            if (!Check(f.Replay != null, $"no format {format} recording")) continue;
            _main.ShowLoadGame();
            await Frames(2);
            if (!Check(load.Select(load.RowOf(f.Replay!)) && !load.WatchButton.Disabled, $"format {format}: not watchable")) continue;
            load.WatchButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);
            if (!Check(_main.CurrentMatch is { IsPlayback: true }, $"format {format}: Watch started no playback")) continue;
            SimRunner runner = _main.CurrentMatch!.GetNode<SimRunner>("SimRunner");
            var w = System.Diagnostics.Stopwatch.StartNew();
            while (runner.PlaybackState == PlaybackState.Playing && w.Elapsed.TotalSeconds < 120) await Frames(1);
            Check(runner.PlaybackState == PlaybackState.Ended && runner.PlaybackError == ReplayError.None && runner.CheckpointsCompared == runner.Playback!.Checkpoints.Length
                && runner.Simulation!.World.Config.MapId == f.Map,
                $"format {format} Watch ({f.How}): {runner.PlaybackState} ({runner.PlaybackError}), map '{runner.Simulation?.World.Config.MapId}'");
            await QuitMatch();
        }
        _main.ShowMenu();
        await Frames(1);
    }

    // ---- helpers ----

    private static Replay Copy(Replay r, int players) => new()
    {
        FormatVersion = r.FormatVersion, Combat = r.Combat, StartingResources = r.StartingResources, MapId = r.MapId,
        SimVersion = r.SimVersion, Seats = r.Seats.IsDefaultOrEmpty ? r.Seats : r.Seats.Add(r.Seats[0]), DataHash = r.DataHash, Map = r.Map with { StartLocations = players }, Seed = r.Seed, PlayerCount = players,
        UnitCapacity = r.UnitCapacity, CommandCapacity = r.CommandCapacity, ResourceCapacity = r.ResourceCapacity,
        CheckpointInterval = r.CheckpointInterval, TickCount = r.TickCount, Commands = r.Commands, Checkpoints = r.Checkpoints,
    };

    private static void Click(OptionButton list, int item)
    {
        list.Select(item);
        list.EmitSignal(OptionButton.SignalName.ItemSelected, (long)item);
    }

    private static void PickTeam(SkirmishSetup setup, int slot, int team) => Click(setup.TeamOptions[slot], team);

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
        string path = $"{_shots}/qa-m6-v18b-{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"  screenshot {path}");
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }

    // A row for an open bug: reported, failing only with -- --strict (the QaV2Test convention); once it prints "now passes",
    // make it a plain check.
    private void Known(string bug, bool ok, string message)
    {
        if (ok)
        {
            GD.Print($"QA M6-V18b KNOWN {bug} row now passes: {message}");
            return;
        }
        GD.Print($"QA M6-V18b KNOWN {bug} (open): {message}");
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--strict") >= 0) _failures.Add($"{bug}: {message}");
    }
}
