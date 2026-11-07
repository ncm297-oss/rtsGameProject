using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Map;

namespace Rts.Game.Tests;

/// <summary>
/// M6-V18: the skirmish setup's Map list and the <c>--map</c> flag, through the shell (<c>Main.tscn</c>).
/// <list type="bullet">
/// <item>The list: item 0 <c>setup.map_generated</c>, then one item per <c>GameData.Maps</c> entry in data order, its text the
/// map's <c>displayName</c> and its tooltip its <c>description</c> (from the data, no map name in C#); the label <c>setup.map</c>;
/// default Generated, whose args carry no <c>--map</c>.</item>
/// <item>A pick: <see cref="SkirmishSetup.MatchArgs"/> adds <c>--map &lt;id&gt;</c> and the map's side as <c>--map-size</c>; Map
/// size, Forests and Gold mines greyed (no typing, no focus); the slots capped at the map's start locations (four shown
/// become the map's, Add player disabled and a no-op), the <c>setup.map_players</c> line shown; Generated gives back the
/// size, the slot count and the rows. The four-slot setup still fits 1152 x 648 with 8 px under Start, and showing the
/// map's line moves no button.</item>
/// <item>The start: Skirmish → Map → the map → Start plays it (<c>World.Config.MapId</c>, the side, a Town Hall on each of the
/// file's starts, on level 0); the start line names the map file and no shortfall line is printed.</item>
/// <item>The flag: <c>--map &lt;id&gt;</c> read; junk (missing, empty, 65 characters, upper case, a dash, path separators,
/// unicode, a flag) warned and ignored; an id the data lacks plays a generated map; <c>--players 4</c> on a two-start map is
/// capped at 2 with a warning.</item>
/// <item>M6-VH5: each four-start map (Pale Hills, Vathar Crossing) from the menu with 4 and 2 slots: the map's line in its
/// 300 px holder, the start line naming the map file, a Town Hall on each seat's start; a bigger map gives back the slots a
/// smaller map's cap took; every start uses the one held match scene (BUG-1093); a <c>frame cost</c> line per map.</item>
/// </list>
/// </summary>
/// <remarks>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/MapListTest.tscn</c>; prints "MAP LIST TEST PASS" or "MAP
/// LIST TEST FAIL: ..." lines. Per-process folders under <c>user://</c> (BUG-0770's rule), removed at the end. Windowed with
/// <c>-- --shots &lt;dir&gt;</c> it saves the setup with the map picked.
/// </remarks>
public partial class MapListTest : Node
{
    /// <summary>Game time each frame advances (<see cref="SimRunner.FixedFrameDelta"/>): five ticks.</summary>
    public const double FrameSeconds = 0.25;

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private UiText _ui = null!;
    private string _root = "";
    private string? _shots;
    private MapDef _map = null!;
    private PackedScene? _matchScene; // BUG-1093: the scene the first start used

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
            // The rows below need a map file with fewer starts than the setup's slots (the shipped Raraku: 2).
            _map = _data.Maps.FirstOrDefault(m => m.Players < SkirmishSetup.MaxSlots) ?? throw new InvalidOperationException("no map file with fewer than 4 starts");
            _root = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://map-list-test-{OS.GetProcessId()}"));
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            Directory.CreateDirectory(Path.Combine(_root, "replays"));
            Directory.CreateDirectory(Path.Combine(_root, "saves"));
            Flags();
            Main main = NewMain();
            AddChild(main);
            await Frames(2);
            try
            {
                await SetupList(main);
                await SetupStart(main);
                await MenuMaps(main);
                await FlagMatches(main);
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
        foreach (string f in _failures.Take(60)) GD.Print($"MAP LIST TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("MAP LIST TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- the command line ----

    private void Flags()
    {
        Check(LaunchOptions.MatchFlags.Contains("--map") && LaunchOptions.HasMatchFlag(new[] { "--map", _map.Key }), "--map is no match flag");
        Check(LaunchOptions.Parse(Array.Empty<string>()).MapId == null, "no --map: a map picked");
        Check(LaunchOptions.Parse(new[] { "--map", _map.Key }).MapId == _map.Key, $"--map {_map.Key} not read");
        Check(LaunchOptions.Parse(new[] { "--map", new string('a', DataLimits.MaxMapIdLength) }).MapId?.Length == DataLimits.MaxMapIdLength, "a 64-character id refused");
        string[][] junk =
        {
            new[] { "--map" }, new[] { "--map", "" }, new[] { "--map", new string('a', DataLimits.MaxMapIdLength + 1) },
            new[] { "--map", "Raraku" }, new[] { "--map", "ra-raku" }, new[] { "--map", "ra raku" }, new[] { "--map", "../raraku" },
            new[] { "--map", "maps/raraku" }, new[] { "--map", "maps\\raraku" }, new[] { "--map", "rarakü" }, new[] { "--map", "-" },
            new[] { "--map", "--seed", "4" }, new[] { "--map", "raraku.json" },
        };
        foreach (string[] j in junk)
        {
            LaunchOptions o = LaunchOptions.Parse(j);
            Check(o.MapId == null, $"--map junk [{string.Join(' ', j)}] read as '{o.MapId}'");
        }
        Check(LaunchOptions.Parse(new[] { "--map", "--seed", "4" }).Seed == 4, "--map swallowed the next flag");
        Check(LaunchOptions.Parse(new[] { "--map", "no_such_map", "--map", _map.Key }).MapId == _map.Key, "the last --map does not win");
        // The data's check: an id it lacks is a generated map.
        Check(Match.MapFor(_data, null) == null && Match.MapFor(_data, "") == null && Match.MapFor(_data, "no_such_map") == null
            && Match.MapFor(_data, _map.Key) == _map, "Match.MapFor");
    }

    // ---- the setup's list ----

    private async Task SetupList(Main main)
    {
        main.ShowSetup();
        await Frames(2);
        SkirmishSetup setup = main.Setup!;
        OptionButton list = setup.MapOption;
        string[] items = Enumerable.Range(0, list.ItemCount).Select(list.GetItemText).ToArray();
        string[] tips = Enumerable.Range(0, list.ItemCount).Select(list.GetItemTooltip).ToArray();
        string[] wantItems = new[] { _ui.Setup(SetupText.MapGenerated) }.Concat(_data.Maps.Select(m => m.DisplayName)).ToArray();
        string[] wantTips = new[] { "" }.Concat(_data.Maps.Select(m => TooltipWrap.Wrap(m.Description))).ToArray();
        Check(items.SequenceEqual(wantItems) && tips.SequenceEqual(wantTips) && items.All(t => t.Length > 0) && tips.Skip(1).All(t => t.Length > 0),
            $"map items [{string.Join("|", items)}] (want [{string.Join("|", wantItems)}]), tooltips [{string.Join("|", tips)}]");
        Check(list.Selected == 0 && setup.MapPick == null && list.TooltipText == "", $"default pick {list.Selected}, tooltip '{list.TooltipText}'");
        Check(_ui.Setup(SetupText.Map).Length > 0 && Labels(setup).Contains(_ui.Setup(SetupText.Map)), "the list's label is not ui.json's setup.map");
        string[] plain = setup.MatchArgs()!;
        Check(!plain.Contains("--map") && LaunchOptions.Parse(plain).MapId == null, $"Generated passes --map: {string.Join(' ', plain)}");
        Check(!setup.MapSizeOption.Disabled && setup.ForestsSpin.Editable && setup.MinesSpin.Editable && !setup.MapPlayersLabel.Visible, "Generated greys a row or shows the map line");

        // Four slots and a 192 map on Generated, then each map file picked as a click on its item.
        for (int k = 0; k < SkirmishSetup.MaxSlots; k++) setup.AddSlot(); // bounded: a no-op on a capped map
        setup.MapSizeOption.Select(Array.IndexOf(LaunchOptions.MapSizes, 192));
        // M6-V19: counts of the player's own, which each map file's counts replace and Generated gives back.
        const int ownForests = 20, ownMines = 17;
        setup.ForestsSpin.Value = ownForests;
        setup.MinesSpin.Value = ownMines;
        await Frames(2);
        Rect2 add = setup.AddSlotButton.GetGlobalRect(), remove = setup.RemoveSlotButton.GetGlobalRect();
        Rect2 start4 = setup.StartButton.GetGlobalRect();
        Rect2 view = GetViewport().GetVisibleRect();
        Check(start4.End.Y <= view.End.Y - 8f, $"Start {start4} is within 8 px of the window's bottom with four slots");
        Check(view.Grow(0.5f).Encloses(list.GetGlobalRect()) && list.GetGlobalRect().End.Y <= setup.ResourcesOption.GetGlobalRect().Position.Y,
            $"the map list {list.GetGlobalRect()} is off screen or not above the starting resources {setup.ResourcesOption.GetGlobalRect()}");
        for (int i = 0; i < _data.Maps.Length; i++)
        {
            MapDef map = _data.Maps[i];
            Click(list, i + 1);
            string[] args = setup.MatchArgs()!;
            LaunchOptions parsed = LaunchOptions.Parse(args);
            int cap = Math.Min(map.Players, SkirmishSetup.MaxSlots);
            Check(setup.MapPick?.Key == map.Key && parsed.MapId == map.Key && parsed.MapSize == map.Size && setup.MapSize == map.Size && list.TooltipText == TooltipWrap.Wrap(map.Description),
                $"item {i + 1} ({map.Key}): pick '{setup.MapPick?.Key}', args [{string.Join(' ', args)}], tooltip '{list.TooltipText}'");
            Check(setup.MapSizeOption.Disabled && !setup.ForestsSpin.Editable && !setup.MinesSpin.Editable
                && !setup.ForestsSpin.GetLineEdit().Editable && !setup.MinesSpin.GetLineEdit().Editable
                && setup.ForestsSpin.GetLineEdit().FocusMode == Control.FocusModeEnum.None && setup.MinesSpin.GetLineEdit().FocusMode == Control.FocusModeEnum.None,
                $"{map.Key}: the generator's rows are not greyed");
            Check(setup.SlotCount == cap && setup.SlotCap == cap && parsed.Players == cap && setup.AddSlotButton.Disabled == (cap >= SkirmishSetup.MaxSlots || setup.SlotCount >= cap),
                $"{map.Key}: {setup.SlotCount} slots shown (cap {setup.SlotCap}, want {cap}), --players {parsed.Players}, Add disabled {setup.AddSlotButton.Disabled}");
            Check(setup.MapPlayersLabel.Visible && setup.MapPlayersLabel.Text == _ui.MapPlayersLine(map.DisplayName, map.Players) && setup.MapPlayersLabel.Text.Contains(map.DisplayName, StringComparison.Ordinal),
                $"{map.Key}: the players line '{setup.MapPlayersLabel.Text}' (shown {setup.MapPlayersLabel.Visible})");
            // M6-V19: the greyed counts are the map's own (its forests, its gold nodes), shown in the boxes.
            int mines = map.Nodes.Count(n => _data.Resources[n.TypeId].Resource == ResourceKind.Gold);
            Check(setup.ForestsPick == map.ForestCount && setup.MinesPick == mines
                && setup.ForestsSpin.GetLineEdit().Text == map.ForestCount.ToString() && setup.MinesSpin.GetLineEdit().Text == mines.ToString(),
                $"{map.Key}: Forests {setup.ForestsPick} ('{setup.ForestsSpin.GetLineEdit().Text}'), Gold mines {setup.MinesPick} ('{setup.MinesSpin.GetLineEdit().Text}'); want the map's {map.ForestCount} / {mines}");
        }
        // The shipped Raraku by id (never by list index): 6 forests, 12 gold mines.
        if (_data.FindMap("raraku") >= 0)
        {
            setup.PickMap("raraku");
            Check(setup.ForestsPick == 6 && setup.MinesPick == 12 && !setup.ForestsSpin.Editable && !setup.MinesSpin.Editable,
                $"Raraku picked: Forests {setup.ForestsPick}, Gold mines {setup.MinesPick} (want 6 / 12, greyed)");
        }

        // The map with fewer starts: Add player does nothing; the line sits inside the window and moves no button.
        Click(list, _map.Id + 1);
        await Frames(2);
        setup.AddSlot();
        setup.AddSlotButton.EmitSignal(BaseButton.SignalName.Pressed);
        Check(setup.SlotCount == _map.Players && setup.AddSlotButton.Disabled, $"Add player on {_map.Key}: {setup.SlotCount} slots");
        Check(view.Grow(0.5f).Encloses(setup.MapPlayersLabel.GetGlobalRect()), $"the players line {setup.MapPlayersLabel.GetGlobalRect()} leaves the window");
        Check(setup.AddSlotButton.GetGlobalRect().Position.X == add.Position.X && setup.RemoveSlotButton.GetGlobalRect().Position.X == remove.Position.X,
            $"the slot buttons moved with the line: Add {setup.AddSlotButton.GetGlobalRect()} (was {add}), Remove {setup.RemoveSlotButton.GetGlobalRect()} (was {remove})");
        Check(Mathf.Abs(add.GetCenter().X + remove.GetCenter().X - view.Size.X) <= 2f, $"Add {add} and Remove {remove} are not centred");
        Check(!setup.StartButton.Disabled, "Start is disabled with a map picked");
        await Shot("setup-map");

        // Generated again: the 192 size, the four slots and the rows come back.
        Click(list, 0);
        await Frames(1);
        Check(setup.MapPick == null && setup.SlotCount == SkirmishSetup.MaxSlots && setup.MapSize == 192 && !setup.MapSizeOption.Disabled
            && setup.ForestsSpin.Editable && setup.MinesSpin.Editable && setup.ForestsSpin.GetLineEdit().Editable && setup.ForestsSpin.GetLineEdit().FocusMode == Control.FocusModeEnum.All
            && !setup.MapPlayersLabel.Visible && setup.AddSlotButton.Disabled && list.TooltipText == "",
            $"Generated again: {setup.SlotCount} slots, size {setup.MapSize}, size list disabled {setup.MapSizeOption.Disabled}, line {setup.MapPlayersLabel.Visible}");
        Check(setup.ForestsPick == ownForests && setup.MinesPick == ownMines && setup.ForestsSpin.MaxValue == MapGenParams.MaxResourceGroups
            && setup.MinesSpin.MaxValue == MapGenParams.MaxResourceGroups && setup.ForestsSpin.MinValue == SkirmishSetup.MinForestsFor(setup.SlotCount)
            && setup.ForestsSpin.GetLineEdit().Text == ownForests.ToString() && setup.MinesSpin.GetLineEdit().Text == ownMines.ToString(),
            $"Generated again: Forests {setup.ForestsPick} ('{setup.ForestsSpin.GetLineEdit().Text}', {setup.ForestsSpin.MinValue}-{setup.ForestsSpin.MaxValue}), Gold mines {setup.MinesPick} ('{setup.MinesSpin.GetLineEdit().Text}'); want {ownForests} / {ownMines} back");
        string[] back = setup.MatchArgs()!;
        LaunchOptions backParsed = LaunchOptions.Parse(back);
        Check(!back.Contains("--map") && backParsed.Players == SkirmishSetup.MaxSlots && backParsed.MapSize == 192 && backParsed.Forests == ownForests && backParsed.Mines == ownMines,
            $"Generated again passes [{string.Join(' ', back)}]");
        // A slot removed while the map is picked: Generated keeps the player's count.
        Click(list, _map.Id + 1);
        Check(setup.SlotCount == _map.Players, "the cap again");
        setup.RemoveSlot(); // at 2 already: nothing
        Click(list, 0);
        Check(setup.SlotCount == SkirmishSetup.MaxSlots, $"Generated after a no-op Remove: {setup.SlotCount} slots");
        Check(setup.PickMap(_map.Key) && setup.MapPick?.Key == _map.Key && !setup.PickMap("no_such_map") && setup.MapPick?.Key == _map.Key && setup.PickMap("") && setup.MapPick == null, "PickMap");
        setup.PickMap(""); // Generated, whatever the check found
        while (setup.SlotCount > LaunchOptions.MinPlayers) setup.RemoveSlot();
        setup.MapSizeOption.Select(0);
        main.ShowMenu();
        await Frames(1);
    }

    // ---- Skirmish -> Map -> the map -> Start ----

    private async Task SetupStart(Main main)
    {
        main.ShowSetup();
        await Frames(2);
        SkirmishSetup setup = main.Setup!;
        for (int k = 0; k < SkirmishSetup.MaxSlots; k++) setup.AddSlot(); // bounded: a no-op on a capped map
        Click(setup.MapOption, _map.Id + 1);
        setup.StartButton.EmitSignal(BaseButton.SignalName.Pressed);
        Match? match = main.CurrentMatch;
        if (!Check(match?.Simulation != null, "Start on the map started no match")) return;
        Simulation sim = match!.Simulation!;
        World w = sim.World;
        Check(w.Config.MapId == _map.Key && w.Config.PlayerCount == _map.Players && w.NavGrid.Width == _map.Size && w.NavGrid.Height == _map.Size
            && w.Config.Map == MapGenParams.Default with { Width = _map.Size, Height = _map.Size } && match.GetNode<SimRunner>("SimRunner").MapId == _map.Key,
            $"the match plays map '{w.Config.MapId}' ({w.NavGrid.Width} x {w.NavGrid.Height}, {w.Config.PlayerCount} players, params {w.Config.Map})");
        Check(match.StartLine.Contains($", map file {_map.Key},", StringComparison.Ordinal) && match.ShortfallLine == "",
            $"the start line '{match.StartLine}', shortfall '{match.ShortfallLine}'");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (sim.TickNumber < 5 && clock.Elapsed.TotalSeconds < 30) await Frames(1);
        // A Town Hall on each of the file's starts, on level 0.
        for (int p = 0; p < _map.Players; p++)
        {
            int start = _map.Starts[p], sx = start % _map.Size, sy = start / _map.Size;
            bool onHall = false;
            var b = w.Buildings;
            for (int i = 0; i < b.Capacity; i++)
            {
                if (!b.Alive[i] || b.Owner[i] != p) continue;
                BuildingDef def = _data.Buildings[b.TypeId[i]];
                int ax = b.Cell[i] % w.NavGrid.Width, ay = b.Cell[i] / w.NavGrid.Width;
                onHall |= sx >= ax && sx < ax + def.FootprintWidth && sy >= ay && sy < ay + def.FootprintHeight;
            }
            Check(onHall && w.Heightmap.LevelAt(sx, sy) == 0 && w.StartLocations[p].Cell == start, $"player {p}: no Town Hall on the start cell ({sx}, {sy}), level {w.Heightmap.LevelAt(sx, sy)}");
            GD.Print($"  player {p}: start cell ({sx}, {sy})");
        }
        main.QuitToMenu();
        await Frames(2);
    }

    // ---- M6-VH5: every four-start map from the menu, with two and with four slots ----

    // Skirmish -> 2 or 4 slots -> Map -> the map (by click) -> Start, for each map file with room for every slot (Pale Hills and
    // Vathar Crossing on the merged tree). Each start: the map's line inside its 300 px holder, above Back / Start; the match
    // plays the map with the slots asked, the start line names the map file, and each seat has a Town Hall on its own start.
    // The first match of each map is measured: the Start press (the whole match build, synchronous) and the first second of
    // frames at real speed (the trees, the minimap's first raster); the numbers are a dev-log line, a GPU's only when windowed.
    private async Task MenuMaps(Main main)
    {
        MapDef[] four = _data.Maps.Where(m => m.Players >= SkirmishSetup.MaxSlots).ToArray();
        Check(four.Length > 0 || _data.Maps.Length == 1, "no map file with four starts");
        // A map with more starts gives back the slots a smaller map's cap took (the loop on three maps found Raraku's cap kept
        // on Vathar Crossing: 2 slots of its 4).
        if (four.Length > 0)
        {
            main.ShowSetup();
            await Frames(1);
            SkirmishSetup setup = main.Setup!;
            setup.PickMap("");
            for (int k = 0; k < SkirmishSetup.MaxSlots; k++) setup.AddSlot();
            Click(setup.MapOption, _map.Id + 1);
            int capped = setup.SlotCount;
            Click(setup.MapOption, four[0].Id + 1);
            int back = setup.SlotCount;
            Click(setup.MapOption, 0);
            Check(capped == _map.Players && back == SkirmishSetup.MaxSlots && setup.SlotCount == SkirmishSetup.MaxSlots,
                $"4 slots, {_map.Key} then {four[0].Key}: {capped} then {back} slots (want {_map.Players} then {SkirmishSetup.MaxSlots}), Generated {setup.SlotCount}");
            // A slot removed on the bigger map is the player's: Generated then keeps the three.
            Click(setup.MapOption, _map.Id + 1);
            Click(setup.MapOption, four[0].Id + 1);
            setup.RemoveSlot();
            Click(setup.MapOption, 0);
            Check(setup.SlotCount == SkirmishSetup.MaxSlots - 1, $"{_map.Key}, {four[0].Key}, Remove player, Generated: {setup.SlotCount} slots (want {SkirmishSetup.MaxSlots - 1})");
            setup.PickMap("");
            main.ShowMenu();
            await Frames(1);
        }
        foreach (MapDef map in four)
        {
            foreach (int slots in new[] { SkirmishSetup.MaxSlots, LaunchOptions.MinPlayers })
            {
                string what = $"{map.Key} from the menu, {slots} slots";
                main.ShowSetup();
                await Frames(2);
                SkirmishSetup setup = main.Setup!;
                setup.PickMap(""); // the setup keeps the last pick: a capped map would hold the slots at its starts
                for (int k = 0; k < SkirmishSetup.MaxSlots && setup.SlotCount < slots; k++) setup.AddSlot(); // bounded
                for (int k = 0; k < SkirmishSetup.MaxSlots && setup.SlotCount > slots; k++) setup.RemoveSlot();
                Click(setup.MapOption, map.Id + 1);
                await Frames(2);
                Check(setup.SlotCount == slots && setup.MapPick?.Key == map.Key && !setup.StartButton.Disabled, $"{what}: {setup.SlotCount} slots, pick '{setup.MapPick?.Key}', Start disabled {setup.StartButton.Disabled}");
                Label line = setup.MapPlayersLabel;
                Control holder = line.GetParent<Control>();
                Rect2 lineRect = line.GetGlobalRect(), holderRect = holder.GetGlobalRect();
                float textBottom = lineRect.Position.Y + line.GetLineCount() * line.GetLineHeight();
                Check(line.Visible && line.Text == _ui.MapPlayersLine(map.DisplayName, map.Players) && holderRect.Size.X == SkirmishSetup.OneTeamRoom
                    && lineRect.Position.X >= holderRect.Position.X - 0.5f && lineRect.End.X <= holderRect.End.X + 0.5f && line.GetLineCount() <= 2
                    && textBottom <= setup.StartButton.GetGlobalRect().Position.Y && GetViewport().GetVisibleRect().Grow(0.5f).Encloses(lineRect),
                    $"{what}: the line '{line.Text}' ({line.GetLineCount()} lines, text bottom {textBottom}) at {lineRect} in its holder {holderRect}, Start at {setup.StartButton.GetGlobalRect()}");
                if (slots == SkirmishSetup.MaxSlots) await Shot($"setup-{map.Key}");

                bool measure = slots == SkirmishSetup.MaxSlots;
                // Real speed for the measured start (no fixed frame delta), the test's five ticks a frame otherwise.
                Action<Match>? before = main.BeforeStart;
                if (measure) main.BeforeStart = null;
                var press = System.Diagnostics.Stopwatch.StartNew();
                setup.StartButton.EmitSignal(BaseButton.SignalName.Pressed);
                press.Stop();
                main.BeforeStart = before;
                Match? match = main.CurrentMatch;
                if (!Check(match?.Simulation != null, $"{what}: Start started no match")) continue;
                World w = match!.Simulation!.World;
                Check(w.Config.MapId == map.Key && w.Config.PlayerCount == slots && w.NavGrid.Width == map.Size
                    && match.StartLine.StartsWith("Match started:", StringComparison.Ordinal) && match.StartLine.Contains($", map file {map.Key},", StringComparison.Ordinal),
                    $"{what}: map '{w.Config.MapId}', {w.Config.PlayerCount} players, {w.NavGrid.Width} wide, line '{match.StartLine}'");
                // BUG-1093: every start (new, loaded, replay) instantiates the one held scene, never a fresh GD.Load whose
                // throwaway wrapper the GC could be finalizing (each quit below is followed by a full collection).
                Check(ReferenceEquals(_matchScene ??= Main.MatchScene, Main.MatchScene) && GodotObject.IsInstanceValid(Main.MatchScene)
                    && match.SceneFilePath == Main.MatchScenePath, $"{what}: the match scene was not the one held (BUG-1093)");
                if (measure) await FrameCost(match, map, press.Elapsed.TotalMilliseconds);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (match.Simulation.TickNumber < 5 && clock.Elapsed.TotalSeconds < 30) await Frames(1);
                int halls = HallsOnStarts(w, map, slots, what);
                Check(halls == slots, $"{what}: {halls} Town Halls on the map's starts, want {slots}");
                GD.Print($"  {what}: {halls} halls, {match.StartLine}");
                main.QuitToMenu();
                await Frames(2);
                // BUG-1093's shape: a collection between matches, its finalizers left running into the next start.
                GC.Collect();
            }
        }
        main.ShowSetup();
        await Frames(1);
        SkirmishSetup s = main.Setup!;
        s.PickMap("");
        for (int k = 0; k < SkirmishSetup.MaxSlots && s.SlotCount > LaunchOptions.MinPlayers; k++) s.RemoveSlot();
        main.ShowMenu();
        await Frames(1);
    }

    // The first second of a fresh match at real speed: frames, average and worst frame, FPS; and the trees the props view
    // shows and the minimap's refreshes. Headless frames have no draw, so the line says which.
    private async Task FrameCost(Match match, MapDef map, double pressMs)
    {
        var props = match.GetNode<PropsView>("World3D/PropsView");
        var mini = match.GetNode<Minimap>("Hud/Minimap");
        var frames = new List<double>();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        double last = 0;
        while (watch.Elapsed.TotalSeconds < 1.0)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            double now = watch.Elapsed.TotalMilliseconds;
            frames.Add(now - last);
            last = now;
        }
        double seconds = watch.Elapsed.TotalSeconds;
        int tree = _data.FindResource("tree");
        int trees = tree >= 0 ? props.ShownCount(tree) : -1;
        string mode = DisplayServer.GetName() == "headless" ? "headless, no draw" : $"windowed {GetViewport().GetVisibleRect().Size}";
        GD.Print(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"  frame cost {map.Key} ({mode}): Start press {pressMs:0} ms; first second {frames.Count} frames ({frames.Count / seconds:0.0} FPS), " +
            $"first frame {frames[0]:0.0} ms, avg {frames.Average():0.0} ms, worst {frames.Max():0.0} ms; {trees} trees shown, minimap refreshes {mini.Refreshes}"));
        Check(trees > 0, $"{map.Key}: no tree shown");
    }

    // The Town Halls standing on the map's first `players` start cells (a hall of that seat's on each, at the sim's start
    // location). The level is the map's own (Raraku's starts are on 0, Vathar Crossing's on its level-1 steppe).
    private int HallsOnStarts(World w, MapDef map, int players, string what)
    {
        int found = 0;
        var b = w.Buildings;
        for (int p = 0; p < players; p++)
        {
            int start = map.Starts[p], sx = start % map.Size, sy = start / map.Size;
            bool onHall = false;
            for (int i = 0; i < b.Capacity; i++)
            {
                if (!b.Alive[i] || b.Owner[i] != p) continue;
                BuildingDef def = _data.Buildings[b.TypeId[i]];
                int ax = b.Cell[i] % w.NavGrid.Width, ay = b.Cell[i] / w.NavGrid.Width;
                onHall |= sx >= ax && sx < ax + def.FootprintWidth && sy >= ay && sy < ay + def.FootprintHeight;
            }
            if (onHall && w.StartLocations[p].Cell == start) found++;
            else Check(false, $"{what}: player {p} has no Town Hall on its start cell ({sx}, {sy}), level {w.Heightmap.LevelAt(sx, sy)}");
        }
        return found;
    }

    // ---- the flag's matches ----

    private async Task FlagMatches(Main main)
    {
        // --map <id>, with four players asked: capped at the map's starts, warned.
        main.StartSkirmish(new[] { "--skirmish", "--players", "4", "--map-size", "256", "--forests", "40", "--seed", "3", "--mute", "--map", _map.Key });
        Match? match = main.CurrentMatch;
        if (Check(match?.Simulation != null, "--map started no match"))
        {
            World w = match!.Simulation!.World;
            Check(w.Config.MapId == _map.Key && w.Config.PlayerCount == _map.Players && w.NavGrid.Width == _map.Size && match.MapPlayersWarning.Contains("--players", StringComparison.Ordinal),
                $"--players 4 --map {_map.Key}: map '{w.Config.MapId}', {w.Config.PlayerCount} players, {w.NavGrid.Width} wide, warning '{match.MapPlayersWarning}'");
            // M6-V19: --map-size other than the file's side is warned and ignored.
            Check(_map.Size == 256 || match.MapSizeWarning.Contains("--map-size 256", StringComparison.Ordinal),
                $"--map-size 256 --map {_map.Key}: warning '{match.MapSizeWarning}'");
            await QuitMatch(main);
        }
        // M6-V19: --map-size as the file's side (what the setup passes), or with an id the data lacks: no warning.
        foreach ((string[] args, int side) in new[]
        {
            (new[] { "--seed", "3", "--units", "0", "--mute", "--map-size", _map.Size.ToString(), "--map", _map.Key }, _map.Size),
            (new[] { "--seed", "3", "--units", "0", "--mute", "--map-size", "256", "--map", "no_such_map" }, 256),
        })
        {
            main.StartSkirmish(args);
            match = main.CurrentMatch;
            if (!Check(match?.Simulation != null, $"[{string.Join(' ', args)}] started no match")) continue;
            Check(match!.Simulation!.World.NavGrid.Width == side && match.MapSizeWarning == "",
                $"[{string.Join(' ', args)}]: {match.Simulation.World.NavGrid.Width} wide, warning '{match.MapSizeWarning}'");
            await QuitMatch(main);
        }
        // M6-V19 (BUG-1092): four players asked, the map's kept players all on team 1 and the rest on team 2: two sides asked,
        // one side after the cap. BUG-1066's rule runs again over the kept players: warned, a free-for-all. With the kept
        // players on two teams the teams stay.
        foreach (bool oneSide in new[] { true, false })
        {
            var flags = new List<string> { "--skirmish", "--players", "4", "--seed", "3", "--mute", "--map", _map.Key };
            for (int s = 0; s < SkirmishSetup.MaxSlots; s++)
            {
                int team = s < _map.Players && (oneSide || s == 0) ? 1 : 2;
                flags.AddRange(new[] { "--team", (s + 1).ToString(), team.ToString() });
            }
            string how = string.Join(' ', flags);
            Check(LaunchOptions.Parse(flags.ToArray()).OneTeamWarning == "", $"[{how}]: LaunchOptions warned before the cap");
            main.StartSkirmish(flags.ToArray());
            match = main.CurrentMatch;
            if (!Check(match?.Simulation != null, $"[{how}] started no match")) continue;
            World w = match!.Simulation!.World;
            if (oneSide)
                Check(w.Config.PlayerCount == _map.Players && w.IsEnemy(0, 1) && w.TeamOf(0) == 0 && w.TeamOf(1) == 0
                    && match.OneTeamWarning.Contains("--team", StringComparison.Ordinal) && match.OneTeamWarning.Contains($"all {_map.Players} players", StringComparison.Ordinal),
                    $"[{how}]: {w.Config.PlayerCount} players, teams {w.TeamOf(0)} / {w.TeamOf(1)}, enemies {w.IsEnemy(0, 1)}, warning '{match.OneTeamWarning}'");
            else
                Check(w.Config.PlayerCount == _map.Players && w.IsEnemy(0, 1) && w.TeamOf(0) == 1 && w.TeamOf(1) == 2 && match.OneTeamWarning == "",
                    $"[{how}]: {w.Config.PlayerCount} players, teams {w.TeamOf(0)} / {w.TeamOf(1)}, warning '{match.OneTeamWarning}'");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int until = match.Simulation!.TickNumber + 20;
            while (match.Simulation.TickNumber < until && clock.Elapsed.TotalSeconds < 30) await Frames(1);
            Check(!main.BannerUp, $"[{how}]: ended at once");
            await QuitMatch(main);
        }
        // An id the data lacks, and junk: warned, a generated map, the start line as before (no map file named).
        foreach (string[] args in new[]
        {
            new[] { "--seed", "3", "--units", "0", "--mute", "--map", "no_such_map" },
            new[] { "--seed", "3", "--units", "0", "--mute", "--map", "" },
            new[] { "--seed", "3", "--units", "0", "--mute", "--map", new string('r', DataLimits.MaxMapIdLength + 1) },
        })
        {
            main.StartSkirmish(args);
            match = main.CurrentMatch;
            if (!Check(match?.Simulation != null, $"[{string.Join(' ', args)}] started no match")) continue;
            World w = match!.Simulation!.World;
            Check(w.Config.MapId == "" && w.NavGrid.Width == 128 && !match.StartLine.Contains("map file", StringComparison.Ordinal) && match.MapPlayersWarning == "",
                $"[{string.Join(' ', args).Substring(0, Math.Min(60, string.Join(' ', args).Length))}]: map '{w.Config.MapId}', line '{match.StartLine}'");
            await QuitMatch(main);
        }
    }

    private static void Click(OptionButton list, int item)
    {
        list.Select(item);
        list.EmitSignal(OptionButton.SignalName.ItemSelected, (long)item); // as a click on the item
    }

    private static List<string> Labels(Node root)
    {
        var texts = new List<string>();
        foreach (Node n in root.FindChildren("*", nameof(Label), true, false)) texts.Add(((Label)n).Text);
        return texts;
    }

    private async Task QuitMatch(Main main)
    {
        main.QuitToMenu();
        await Frames(2);
    }

    private Main NewMain()
    {
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.UserArgs = new[] { Main.NoConfineFlag };
        main.SettingsPath = $"user://map-list-test-{OS.GetProcessId()}/settings.cfg"; // BUG-0770: never the player's file
        main.ReplayDir = Path.Combine(_root, "replays");
        main.SaveDir = Path.Combine(_root, "saves");
        main.BeforeStart = m => m.GetNode<SimRunner>("SimRunner").FixedFrameDelta = FrameSeconds;
        return main;
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    // Windowed with `-- --shots <dir>`: the viewport as map-list-<name>.png. M6-VH5: three draws waited for with a bound, since
    // a minimized window draws nothing and an unbounded FramePostDraw wait hung the run (seen 4 times while looping it).
    private async Task Shot(string name)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        int drawn = 0;
        void OnDraw() => drawn++;
        RenderingServer.FramePostDraw += OnDraw;
        for (int frames = 0; frames < 300 && drawn < 3; frames++) await Frames(1);
        RenderingServer.FramePostDraw -= OnDraw;
        if (drawn < 3)
        {
            GD.Print($"  screenshot map-list-{name} skipped: the window drew {drawn} frames (mode {DisplayServer.WindowGetMode()})");
            return;
        }
        string path = $"{_shots}/map-list-{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"  screenshot {path}");
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
