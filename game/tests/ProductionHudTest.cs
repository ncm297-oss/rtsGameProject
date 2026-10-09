using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>M3-V3 criteria 1-6 and 8 on the real Match scene: selection panel, production card, queue strip, rally marker, population in the resource bar, the BUG-0108 / 0109 / 0110 regressions, idle allocation.</summary>
/// <remarks>
/// Headless. Run: <c>&amp; $env:GODOT --headless --path game res://tests/ProductionHudTest.tscn</c>; prints "PRODUCTION HUD
/// TEST PASS" and exits 0, or prints each failure and exits 1. The test ticks the sim itself (SimRunner disabled), drives
/// input through <see cref="SelectionController._UnhandledInput"/>, the minimap's <c>_GuiInput</c> and the buttons'
/// <c>pressed</c> signals, reads commands back from the sim's pending queue and sets player money through the ledger
/// (reflection, test only). Windowed with <c>-- --shots &lt;dir&gt;</c> it saves the panel with one unit, the panel with a
/// mixed selection, a production card with a queue, and a rally flag.
/// </remarks>
public partial class ProductionHudTest : Node
{
    private static readonly FieldInfo CommandsField = typeof(Simulation).GetField("_commands", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo DamageMethod = typeof(BuildingStore).GetMethod("Damage", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly PropertyInfo LedgerProperty = typeof(World).GetProperty("Ledger", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private UiText _ui = null!;
    private string? _shots;

    private Match _match = null!;
    private Simulation _sim = null!;
    private SelectionController _sel = null!;
    private RtsCamera _camera = null!;
    private CommandCard _card = null!;
    private BuildGhost _ghost = null!;
    private SelectionPanel _panel = null!;
    private ProductionQueueStrip _strip = null!;
    private RallyMarker _rally = null!;
    private ResourceBar _bar = null!;
    private Minimap _mini = null!;
    private Sfx _sfx = null!;

    // Player 0's production buildings spawned for the card rows.
    private int _hall, _barracks, _armory, _armory2, _yard;

    private World W => _sim.World;
    private UnitStore U => _sim.World.Units;
    private BuildingStore B => _sim.World.Buildings;
    private NavGrid G => _sim.World.NavGrid;

    public override async void _Ready()
    {
        try
        {
            await Run();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        Input.ActionRelease("order_queue");
        foreach (string f in _failures) GD.Print($"PRODUCTION HUD TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("PRODUCTION HUD TEST PASS");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    private async Task Run()
    {
        string[] args = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(args, "--shots");
        if (at >= 0 && at + 1 < args.Length) _shots = args[at + 1];
        GetTree().Root.Size = new Vector2I(1152, 648);
        await Frame();
        DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
        if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
        _data = loaded.Data!;
        UiTextRows();
        _ui = UiText.Shared!;

        await StartMatch(_data);
        await PopRows();
        await BareHallAgeII();
        await SpawnProductionBuildings();
        await PanelOneUnit();
        await PanelMixed();
        await CardContents();
        await GreyingFuzz();
        await Presses();
        await QueueStripRun();
        await Rally();
        await Bug0108();
        await Bug0109();
        await IdleAllocation();
        await EndMatch();
        await HalfPopMatch();
    }

    // Criterion 7 / BUG-0110: the new ui.json sections are complete, a missing key is one named error, an extra key (the
    // forward `requires`) is fine, and a root that is not an object is one error.
    private void UiTextRows()
    {
        var errors = new List<string>();
        UiText? ui = UiText.Load(UiText.DefaultPath, errors);
        if (!Check(ui != null && errors.Count == 0, $"shipped ui.json: {string.Join("; ", errors)}")) return;
        foreach (TrainError r in Enum.GetValues<TrainError>())
            if (r != TrainError.None) Check(ui!.TrainText(r).Length > 0, $"ui.json: no train text for {r}");
        foreach (ResearchError r in Enum.GetValues<ResearchError>())
            if (r != ResearchError.None) Check(ui!.ResearchText(r).Length > 0, $"ui.json: no research text for {r}");
        foreach (UnitState s in Enum.GetValues<UnitState>()) Check(ui!.StateText(s).Length > 0, $"ui.json: no state text for {s}");
        foreach (HudText h in Enum.GetValues<HudText>()) Check(ui!.Hud(h).Length > 0, $"ui.json: no hud text for {h}");
        Check(ui!.ForwardPlacementText.Length > 0 && ui.ForwardTrainText.Length > 0 && ui.ForwardResearchText.Length > 0, "ui.json: a forward requires text is empty");
        Check(UiText.Key(TrainError.QueueFull) == "queue_full" && UiText.Key(ResearchError.AlreadyQueued) == "already_queued" && UiText.Key(UnitState.Idle) == "idle",
            "enum keys are not snake_case");

        string json = File.ReadAllText(UiText.DefaultPath);
        foreach ((string from, string to, string expect) in new[]
        {
            ("\"queue_full\": \"Queue full\",\n    \"cannot_afford\": \"Can't afford\",\n    \"requires\": \"Locked\"\n  },\n  \"research\"", "\"cannot_afford\": \"Can't afford\",\n    \"requires\": \"Locked\"\n  },\n  \"research\"", "train.queue_full"),
            ("\"already_queued\": \"In a queue\",", "", "research.already_queued"),
            ("\"store_full\": \"Too many buildings\",\n    \"requires\": \"Locked\"", "\"store_full\": \"Too many buildings\"", "placement.requires"),
            ("\"cannot_afford\": \"Can't afford\",\n    \"requires\": \"Locked\"\n  },\n  \"states\"", "\"cannot_afford\": \"Can't afford\"\n  },\n  \"states\"", "research.requires"),
            ("\"idle\": \"Idle\",", "", "states.idle"),
            ("\"pop\": \"Pop\",", "", "hud.pop"),
        })
        {
            errors.Clear();
            string text = json.Replace("\r\n", "\n");
            string broken = text.Replace(from, to);
            if (!Check(broken != text, $"ui.json row: '{from}' not found")) continue;
            UiText? bad = UiText.Parse(broken, errors);
            Check(bad == null && errors.Count == 1 && errors[0].Contains(expect), $"broken ui.json ({expect}): {string.Join("; ", errors)}");
        }
        // An extra key with no enum member (what `requires` is before M3-6, or any other) is accepted.
        errors.Clear();
        Check(UiText.Parse(json.Replace("\"queue_full\": \"Queue full\",", "\"queue_full\": \"Queue full\", \"some_future_reason\": \"Later\","), errors) != null && errors.Count == 0,
            $"an extra reason key was refused: {string.Join("; ", errors)}");
        // BUG-0136 / BUG-0147: `states.attacking` ships before the sim's UnitState.Attacking (M4-1) merges, so the merged
        // view boots. Today it is an extra key (accepted, and removing it is fine); once the member exists the enum loop
        // above demands its text, and removing the key is one named error.
        using (JsonDocument doc = JsonDocument.Parse(json))
        {
            Check(doc.RootElement.GetProperty("states").TryGetProperty("attacking", out JsonElement attacking)
                && attacking.ValueKind == JsonValueKind.String && attacking.GetString() is { Length: > 0 },
                "ui.json: states.attacking is missing or empty");
        }
        errors.Clear();
        string noAttacking = json.Replace("\r\n", "\n").Replace("\"building\": \"Building\",\n    \"attacking\": \"Attacking\"", "\"building\": \"Building\"");
        if (Check(noAttacking != json.Replace("\r\n", "\n"), "ui.json row: states.attacking line not found"))
        {
            UiText? r = UiText.Parse(noAttacking, errors);
            bool simHasIt = Enum.TryParse("Attacking", out UnitState _);
            Check(simHasIt ? r == null && errors.Count == 1 && errors[0].Contains("states.attacking") : r != null && errors.Count == 0,
                $"ui.json without states.attacking (sim has the state: {simHasIt}): {string.Join("; ", errors)}");
        }
        // BUG-0110 regression: a root that is not an object is one error, never a UiText with empty labels.
        foreach (string root in new[] { "[]", "null", "42", "\"ui\"", "true" })
        {
            errors.Clear();
            UiText? r = UiText.Parse(root, errors);
            Check(r == null && errors.Count == 1 && errors[0].Contains("root"), $"BUG-0110: ui.json '{root}': {(r == null ? "null" : "a UiText")}, errors [{string.Join("; ", errors)}]");
        }
    }

    private async Task StartMatch(GameData data)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _match.Start(data, LaunchOptions.Parse(new[] { "--seed", "1", "--units", "0", "--mute" }));
        var runner = _match.GetNode<SimRunner>("SimRunner");
        runner.ProcessMode = ProcessModeEnum.Disabled;
        _sim = runner.Simulation!;
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _card = _match.GetNode<CommandCard>("Hud/CommandCard");
        _ghost = _match.GetNode<BuildGhost>("World3D/BuildGhost");
        _panel = _match.GetNode<SelectionPanel>("Hud/SelectionPanel");
        _strip = _match.GetNode<ProductionQueueStrip>("Hud/QueueStrip");
        _rally = _match.GetNode<RallyMarker>("World3D/RallyMarker");
        _bar = _match.GetNode<ResourceBar>("Hud/ResourceBar");
        _mini = _match.GetNode<Minimap>("Hud/Minimap");
        _sfx = _match.GetNode<Sfx>("Sfx");
        Tick(2);
        await Frames();
    }

    private async Task EndMatch()
    {
        RemoveChild(_match);
        _match.QueueFree();
        await Frame();
    }

    // Criterion 5: "Pop 5 / 10" at the start (5 workers, one Town Hall), from HalfPop / HalfPopCap with ui.json's label; red at the cap.
    private async Task PopRows()
    {
        _hall = HallSlot(0);
        string pop = _ui.Hud(HudText.Pop);
        Check(_bar.PopLabel.Text == $"{pop} 5 / 10" && _bar.PopLabel.Text == "Pop 5 / 10" && !_bar.ShownAtCap,
            $"start: pop reads '{_bar.PopLabel.Text}' (half {W.HalfPop[0]} / {W.HalfPopCap[0]}), red {_bar.ShownAtCap}");
        Check(_bar.Text == "Gold 200  Wood 200", $"start: bar reads '{_bar.Text}'");
        Check(_bar.PopLabel.GetGlobalRect().End.X <= GetViewport().GetVisibleRect().Size.X, $"pop label off screen: {_bar.PopLabel.GetGlobalRect()}");
        // Five more laborers (dev spawns count): 10 / 10, red.
        int laborer = WorkerType();
        System.Numerics.Vector2 near = OpenGroundNear(HallCenter(0), 8f);
        for (int i = 0; i < 5; i++) _sim.Enqueue(Command.SpawnUnit(0, laborer, near + new System.Numerics.Vector2(i * 1.2f, 0f)));
        Tick(2);
        await Frames();
        Check(_bar.PopLabel.Text == "Pop 10 / 10" && _bar.ShownAtCap, $"at cap: '{_bar.PopLabel.Text}', red {_bar.ShownAtCap}");
        Color c = _bar.PopLabel.GetThemeColor("font_color");
        Check(c.R > 0.9f && c.G < 0.5f, $"at cap: pop colour {c}");
        GD.Print($"pop: start 'Pop 5 / 10', at cap '{_bar.PopLabel.Text}' colour {c}");
    }

    // BUG-0124: with only the Town Hall, Age II is locked (it needs two finished halls of distinct slots): the button is
    // greyed with research.requires ("Locked"), even with money, and a press enqueues nothing and plays no sound.
    private async Task BareHallAgeII()
    {
        SetMoney(5000, 5000);
        _sel.SelectBuilding(_hall);
        await Frames();
        int age = _data.FindTech("age_ii");
        int cell = Enumerable.Range(0, CommandCard.Cells).FirstOrDefault(i => _card.ActionAt(i) == CardCommand.Research && _card.TypeAt(i) == age, -1);
        if (!Check(cell >= 0, "bare hall: no Age II button")) return;
        W.CanResearch(0, _hall, age, out ResearchError why);
        Check(why == ResearchError.Requires && _card.ReasonAt(cell) == (int)ResearchError.Requires && _card.ButtonAt(cell).Disabled
            && _card.CostAt(cell).Text == _ui.ResearchText(ResearchError.Requires) && _card.CostAt(cell).Text == "Locked"
            && _card.NameAt(cell).Modulate.A <= 0.6f && _card.HintAt(cell).Modulate.A <= 0.6f,
            $"bare hall Age II: sim {why}, card reason {_card.ReasonAt(cell)} disabled {_card.ButtonAt(cell).Disabled} '{_card.CostAt(cell).Text}'");
        await SoundGap();
        int start = _sim.PendingCommandCount, sounds = _sfx.PlayCount(SfxEvent.Command);
        _card.Press(cell);
        Check(_sim.PendingCommandCount == start && _sfx.PlayCount(SfxEvent.Command) == sounds, $"bare hall Age II press: {_sim.PendingCommandCount - start} commands, {_sfx.PlayCount(SfxEvent.Command) - sounds} sounds");
        Tick(2);
        Check(B.QueueCount[_hall] == 0, $"bare hall Age II press: queue {B.QueueCount[_hall]}");
        GD.Print($"bare hall: Age II reads '{_card.CostAt(cell).Text}', press enqueued nothing");
        _sel.ClearBuilding();
        await Frames();
    }

    // Finished Barracks, two Armories and an Engineers' Yard for player 0 (dev spawns), and money. The Barracks and an
    // Armory are two halls of distinct slots, so Age II opens at the Town Hall (BUG-0124).
    private async Task SpawnProductionBuildings()
    {
        int f = W.FactionOf(0);
        var taken = new List<int> { B.Cell[_hall] };
        int Spawn(BuildingSlot slot)
        {
            int type = StartBase.BuildingOfSlot(_data, f, slot);
            int a = FreeAnchor(type, taken);
            taken.Add(a);
            _sim.Enqueue(Command.SpawnBuilding(0, type, G.CellCenter(a % G.Width, a / G.Width)));
            return a;
        }
        int[] anchors = { Spawn(BuildingSlot.InfantryHall), Spawn(BuildingSlot.Forge), Spawn(BuildingSlot.Forge), Spawn(BuildingSlot.SiegeWorks) };
        Tick(2);
        int[] slots = anchors.Select(a => B.SlotAt(a % G.Width, a / G.Width)).ToArray();
        Check(slots.All(s => s >= 0 && !B.UnderConstruction[s] && B.Owner[s] == 0), $"spawned buildings: {string.Join(", ", slots)}");
        (_barracks, _armory, _armory2, _yard) = (slots[0], slots[1], slots[2], slots[3]);
        SetMoney(5000, 5000);
        await Frames();
    }

    // Criterion 1, one unit: name, hp, attack, armor, range, speed from data; the state from ui.json; "+N" after a researched tech.
    private async Task PanelOneUnit()
    {
        EntityHandle worker = Workers()[0];
        await Select(new List<EntityHandle> { worker });
        UnitDef def = _data.Units[U.TypeId[worker.Index]];
        Check(_panel.ShownKind == 1 && _panel.NameLabel.Text == def.DisplayName, $"one unit: kind {_panel.ShownKind}, name '{_panel.NameLabel.Text}'");
        CheckStats("one unit", def, worker.Index);
        for (int r = 0; r < 5; r++) Check(!_panel.StatBonus(r).Visible, $"one unit: bonus row {r} shown before any tech");
        await Shot("panel-one-unit");

        // Melee Weapons at an Armory: workers have a melee attack, so the laborer gets +1 attack (shown, not applied until M4).
        int melee = _data.FindTech("melee_weapons_1");
        _sim.Enqueue(Command.Research(0, SelectionController.SiteCenter(W, _armory), melee));
        Tick(_data.Techs[melee].ResearchTicks + 3);
        Check(W.HasTech(0, melee), "melee weapons not researched");
        await Frames();
        float bonus = W.TechBonus(0, def.Id, TechStat.Attack);
        Check(bonus == 1f && _panel.StatBonus(1).Visible && _panel.StatBonus(1).Text == "+1", $"attack bonus {bonus}: label '{_panel.StatBonus(1).Text}' visible {_panel.StatBonus(1).Visible}");
        Check(_panel.StatValue(1).Text == def.Attack.Value.ToString(), $"attack value '{_panel.StatValue(1).Text}' changed with the bonus");
        Color bc = _panel.StatBonus(1).GetThemeColor("font_color"), vc = _panel.StatValue(1).GetThemeColor("font_color");
        Check(bc != vc, "bonus drawn in the value's colour");
        // The state follows the unit: a Gather order makes it Gathering in plain words.
        int mine = NearestMine(HallCenter(0));
        _sim.Enqueue(Command.Gather(0, worker, NodeCenter(mine)));
        for (int t = 0; t < 400 && U.State[worker.Index] != UnitState.Gathering; t++) Tick(1);
        await Frames();
        Check(_panel.StateLabel.Text == _ui.StateText(U.State[worker.Index]) && U.State[worker.Index] == UnitState.Gathering,
            $"state '{_panel.StateLabel.Text}' for {U.State[worker.Index]}");
        GD.Print($"panel one unit: {def.DisplayName}, attack {_panel.StatValue(1).Text} {_panel.StatBonus(1).Text}, state '{_panel.StateLabel.Text}'");
    }

    private void CheckStats(string what, UnitDef def, int slot)
    {
        string F(float v) => v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        Check(_panel.HpText == $"{def.Hp} / {def.Hp}", $"{what}: hp '{_panel.HpText}'");
        Check(_panel.StatValue(1).Text == F(def.Attack.Value), $"{what}: attack '{_panel.StatValue(1).Text}'");
        Check(_panel.StatValue(2).Text == F(def.Armor), $"{what}: armor '{_panel.StatValue(2).Text}'");
        Check(_panel.StatValue(3).Text == F(def.Attack.Range), $"{what}: range '{_panel.StatValue(3).Text}'");
        Check(_panel.StatValue(4).Text == F(def.SpeedPerTick * SimConstants.TicksPerSecond), $"{what}: speed '{_panel.StatValue(4).Text}'");
        HudText[] rows = { HudText.Hp, HudText.Attack, HudText.Armor, HudText.Range, HudText.Speed };
        for (int r = 0; r < 5; r++) Check(_panel.StatName(r).Text == _ui.Hud(rows[r]), $"{what}: row {r} named '{_panel.StatName(r).Text}'");
        Check(_panel.StateLabel.Text == _ui.StateText(U.State[slot]), $"{what}: state '{_panel.StateLabel.Text}'");
    }

    // Criterion 1, several units: up to 24 portraits in selection order with "+N", the active subgroup outlined (Tab moves it), a click selects that unit alone.
    private async Task PanelMixed()
    {
        int f = W.FactionOf(0);
        int soldier = StartBase.BuildingOfSlot(_data, f, BuildingSlot.InfantryHall) >= 0 ? _data.UnitsTrainedAt(B.TypeId[_barracks])[0] : -1;
        int archer = _data.Factions[f].Units.First(t => _data.Units[t].Slot == UnitSlot.Ranged);
        System.Numerics.Vector2 p = OpenGroundNear(HallCenter(0), 14f);
        for (int i = 0; i < 14; i++) _sim.Enqueue(Command.SpawnUnit(0, i % 2 == 0 ? soldier : archer, p + new System.Numerics.Vector2(i % 5 * 1.3f, i / 5 * 1.3f)));
        Tick(2);
        List<EntityHandle> all = Own();
        Check(all.Count == 24, $"mixed: {all.Count} own units, want 24 (10 laborers + 14 soldiers)");
        _sim.Enqueue(Command.SpawnUnit(0, archer, p + new System.Numerics.Vector2(8f, 8f)));
        _sim.Enqueue(Command.SpawnUnit(0, archer, p + new System.Numerics.Vector2(9.5f, 8f)));
        Tick(2);
        all = Own();
        await Select(all);
        Check(_panel.ShownKind == 2 && _panel.ShownCells == 24 && _panel.OverflowLabel.Visible && _panel.OverflowLabel.Text == $"+{all.Count - 24}",
            $"mixed: kind {_panel.ShownKind}, cells {_panel.ShownCells}, overflow '{_panel.OverflowLabel.Text}' ({all.Count} units)");
        EntityHandle[] items = _sel.Selection.Items.ToArray();
        for (int k = 0; k < 24; k++)
        {
            Check(_panel.CellUnit(k) == items[k] && _panel.CellButton(k).Visible, $"mixed: cell {k} shows {_panel.CellUnit(k)}, selection has {items[k]}");
            Check(_panel.CellColor(k) == SelectionPanel.TypeColor(U.TypeId[items[k].Index]), $"mixed: cell {k} colour");
        }
        for (int tab = 0; tab < 3; tab++)
        {
            int active = _sel.Subgroups.ActiveType;
            int on = 0;
            for (int k = 0; k < 24; k++)
            {
                bool want = U.TypeId[_panel.CellUnit(k).Index] == active;
                if (want) on++;
                Check(_panel.CellHighlighted(k) == want, $"mixed tab {tab}: cell {k} highlight {_panel.CellHighlighted(k)}, active type {active}");
            }
            Check(on > 0, $"mixed tab {tab}: no cell outlined");
            if (tab == 0) await Shot("panel-mixed");
            Key(Godot.Key.Tab);
            await Frames();
        }
        // A portrait click (its button's pressed signal) selects that unit alone.
        EntityHandle pick = _panel.CellUnit(7);
        await SoundGap();
        int sounds = _sfx.PlayCount(SfxEvent.Select);
        _panel.CellButton(7).EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        Check(_sel.Selection.Count == 1 && _sel.Selection.Items[0] == pick && _panel.ShownKind == 1, $"portrait click: {_sel.Selection.Count} selected, kind {_panel.ShownKind}");
        Check(_sfx.PlayCount(SfxEvent.Select) == sounds + 1, "portrait click: no Select sound");
        GD.Print($"panel mixed: {all.Count} units, 24 cells + '{_panel.OverflowLabel.Text}'");
    }

    // Criterion 2: a Barracks shows its units in UnitsTrainedAt order, a Town Hall its units then Age II, an Armory the Forge upgrades then the faction upgrade.
    private async Task CardContents()
    {
        foreach ((string what, int slot) in new[] { ("barracks", _barracks), ("town hall", _hall), ("armory", _armory), ("engineers yard", _yard) })
        {
            Check(_sel.SelectBuilding(slot), $"{what}: not selectable");
            await Frames();
            int type = B.TypeId[slot];
            var want = new List<(CardCommand, int)>();
            foreach (int u in _data.UnitsTrainedAt(type)) want.Add((CardCommand.Train, u));
            foreach (int t in _data.TechsResearchableAt(type).Where(t => _data.Techs[t].Faction < 0)) want.Add((CardCommand.Research, t));
            foreach (int t in _data.TechsResearchableAt(type).Where(t => _data.Techs[t].Faction >= 0)) want.Add((CardCommand.Research, t));
            for (int i = 0; i < CommandCard.Cells; i++)
            {
                (CardCommand c, int id) = i < want.Count ? want[i] : (CardCommand.None, -1);
                Check(_card.ActionAt(i) == c && _card.TypeAt(i) == id && _card.ButtonAt(i).Visible == (c != CardCommand.None), $"{what}: cell {i} is {_card.ActionAt(i)} {_card.TypeAt(i)}, want {c} {id}");
                if (c == CardCommand.None) continue;
                string name = c == CardCommand.Train ? _data.Units[id].DisplayName : _data.Techs[id].DisplayName;
                string desc = c == CardCommand.Train ? _data.Units[id].Description : _data.Techs[id].Description;
                var req = c == CardCommand.Train ? _data.Units[id].Requires : _data.Techs[id].Requires;
                Check(_card.NameAt(i).Text == name && _card.HintAt(i).Text == _card.GridKey(i), $"{what}: cell {i} reads '{_card.NameAt(i).Text}' / '{_card.HintAt(i).Text}'");
                string tip = _card.ButtonAt(i).TooltipText;
                Check(tip.StartsWith(desc) && !tip.Contains("_"), $"{what}: cell {i} tooltip '{tip}'");
                if (req.Length > 0)
                    Check(tip.Contains(_ui.Hud(HudText.Needs)) && req.All(r => tip.Contains(ProductionMenu.RequirementName(_data, r))), $"{what}: cell {i} tooltip lacks its needs: '{tip}'");
            }
            GD.Print($"{what}: {string.Join(", ", want.Select(e => e.Item1 == CardCommand.Train ? _data.Units[e.Item2].DisplayName : _data.Techs[e.Item2].DisplayName))}");
        }
        Check(_sel.SelectedBuilding == _yard, "selection moved");
        Check(_sel.SelectBuilding(_armory), "armory");
        await Frames();
        Check(_data.Techs[_card.TypeAt(6)].Key == "moranth_supply" && Enumerable.Range(0, 6).All(i => _data.Techs[_card.TypeAt(i)].Faction < 0),
            $"armory order: last '{_data.Techs[_card.TypeAt(6)].Key}'");
    }

    // Criterion 2: on 200 random states (money, queues full or not, researched, queued elsewhere, locked) every button's
    // greying and reason text equal CanTrain / CanResearch.
    private async Task GreyingFuzz()
    {
        var rng = new Random(5);
        int[] buildings = { _hall, _barracks, _armory, _armory2, _yard };
        var seen = new HashSet<string>();
        int cellsChecked = 0;
        for (int s = 0; s < 200; s++)
        {
            int op = rng.Next(5);
            if (op == 0) SetMoney(rng.Next(0, 600), rng.Next(0, 400));
            else if (op == 1 || op == 2)
            {
                int k = buildings[rng.Next(buildings.Length)];
                var into = new ProductionEntry[15];
                int n = ProductionMenu.Entries(_data, B.TypeId[k], into);
                if (n > 0)
                {
                    ProductionEntry e = into[rng.Next(n)];
                    System.Numerics.Vector2 c = SelectionController.SiteCenter(W, k);
                    for (int j = rng.Next(1, 4); j > 0; j--) _sim.Enqueue(e.IsTech ? Command.Research(0, c, e.TypeId) : Command.Train(0, c, e.TypeId));
                }
            }
            else if (op == 3)
            {
                int k = buildings[rng.Next(buildings.Length)];
                if (B.QueueCount[k] > 0) _sim.Enqueue(Command.CancelTrain(0, SelectionController.SiteCenter(W, k), rng.Next(B.QueueCount[k])));
            }
            else SetMoney(rng.Next(2000, 5000), rng.Next(2000, 5000));
            Tick(rng.Next(1, 8));
            int sel = buildings[rng.Next(buildings.Length)];
            _sel.SelectBuilding(sel);
            _card.Sync();
            for (int i = 0; i < CommandCard.Cells; i++)
            {
                CardCommand c = _card.ActionAt(i);
                if (c is not (CardCommand.Train or CardCommand.Research)) continue;
                int reason;
                string text;
                if (c == CardCommand.Train)
                {
                    W.CanTrain(0, sel, _card.TypeAt(i), out TrainError e);
                    reason = (int)e;
                    text = e == TrainError.None ? $"{_data.Units[_card.TypeAt(i)].CostGold} / {_data.Units[_card.TypeAt(i)].CostWood}" : _ui.TrainText(e);
                    seen.Add($"train {e}");
                }
                else
                {
                    W.CanResearch(0, sel, _card.TypeAt(i), out ResearchError e);
                    // M3-V4 (BUG-0126): the card shows Researched / In a queue over the sim's earlier reasons.
                    e = ProductionMenu.ShownResearchReason(e, W.HasTech(0, _card.TypeAt(i)), ProductionMenu.IsTechQueued(W.Buildings, 0, _card.TypeAt(i)));
                    reason = (int)e;
                    text = e == ResearchError.None ? $"{_data.Techs[_card.TypeAt(i)].CostGold} / {_data.Techs[_card.TypeAt(i)].CostWood}" : _ui.ResearchText(e);
                    seen.Add($"research {e}");
                }
                cellsChecked++;
                // BUG-0123: a greyed cell's name and hotkey are drawn dimmed, an enabled one's at full strength.
                float alpha = reason != 0 ? CommandCard.DimAlpha : 1f;
                bool dim = _card.NameAt(i).Modulate.A == alpha && _card.HintAt(i).Modulate.A == alpha;
                if (!Check(_card.ReasonAt(i) == reason && _card.ButtonAt(i).Disabled == (reason != 0) && _card.CostAt(i).Text == text && dim,
                        $"state {s}: building {sel} cell {i} ({c} {_card.TypeAt(i)}): shows reason {_card.ReasonAt(i)} disabled {_card.ButtonAt(i).Disabled} '{_card.CostAt(i).Text}' alpha {_card.NameAt(i).Modulate.A}, sim says {reason} '{text}'"))
                    return;
            }
        }
        GD.Print($"greying fuzz: 200 states, {cellsChecked} cells; reasons seen: {string.Join(", ", seen.OrderBy(x => x))}");
        foreach (string want in new[] { "train None", "train CannotAfford", "train QueueFull", "train LockedByRequirement", "research None", "research AlreadyResearched", "research AlreadyQueued", "research CannotAfford" })
            Check(seen.Contains(want), $"greying fuzz never saw {want}");
        // Leave the queues empty for the next rows.
        await ClearQueues(buildings);
    }

    // Criterion 2: a press enqueues exactly one command at the footprint centre with one Command sound; a greyed press (button or key) nothing and no sound.
    private async Task Presses()
    {
        SetMoney(5000, 5000);
        _sel.SelectBuilding(_barracks);
        await Frames();
        System.Numerics.Vector2 centre = SelectionController.SiteCenter(W, _barracks);
        await SoundGap();
        int sounds = _sfx.PlayCount(SfxEvent.Command), start = _sim.PendingCommandCount;
        _card.ButtonAt(0).EmitSignal(BaseButton.SignalName.Pressed);
        List<Command> sent = Pending(start);
        Check(sent.Count == 1 && sent[0].Kind == CommandKind.Train && sent[0].TypeId == _card.TypeAt(0) && sent[0].Position == centre && sent[0].Player == 0,
            $"train press: {Describe(sent)}");
        Check(_sfx.PlayCount(SfxEvent.Command) == sounds + 1, "train press: no Command sound");
        // The grid key does the same (Q = cell 0).
        await SoundGap();
        start = _sim.PendingCommandCount;
        Key(Godot.Key.Q);
        sent = Pending(start);
        Check(sent.Count == 1 && sent[0].Kind == CommandKind.Train && sent[0].Position == centre, $"Q on the barracks: {Describe(sent)}");
        Tick(2);

        // Research by button at the armory (armor, cell 0).
        _sel.SelectBuilding(_armory);
        await Frames();
        int cell = Enumerable.Range(0, CommandCard.Cells).First(i => _card.ActionAt(i) == CardCommand.Research && _card.ReasonAt(i) == 0);
        await SoundGap();
        sounds = _sfx.PlayCount(SfxEvent.Command);
        start = _sim.PendingCommandCount;
        _card.ButtonAt(cell).EmitSignal(BaseButton.SignalName.Pressed);
        sent = Pending(start);
        Check(sent.Count == 1 && sent[0].Kind == CommandKind.Research && sent[0].TypeId == _card.TypeAt(cell) && sent[0].Position == SelectionController.SiteCenter(W, _armory),
            $"research press: {Describe(sent)}");
        Check(_sfx.PlayCount(SfxEvent.Command) == sounds + 1, "research press: no Command sound");
        Tick(2);

        // Greyed: no money. The key, the button's signal and Press all do nothing and play nothing.
        SetMoney(0, 0);
        _sel.SelectBuilding(_barracks);
        await Frames();
        Check(_card.ButtonAt(0).Disabled && _card.ReasonAt(0) == (int)TrainError.CannotAfford && _card.CostAt(0).Text == _ui.TrainText(TrainError.CannotAfford),
            $"broke barracks: disabled {_card.ButtonAt(0).Disabled}, reason {_card.ReasonAt(0)}, '{_card.CostAt(0).Text}'");
        await SoundGap();
        sounds = _sfx.PlayCount(SfxEvent.Command);
        start = _sim.PendingCommandCount;
        Key(Godot.Key.Q);
        _card.ButtonAt(0).EmitSignal(BaseButton.SignalName.Pressed);
        _card.Press(0);
        Check(_sim.PendingCommandCount == start && _sfx.PlayCount(SfxEvent.Command) == sounds, $"greyed press: {_sim.PendingCommandCount - start} commands, {_sfx.PlayCount(SfxEvent.Command) - sounds} sounds");
        // A grid key with nothing on its cell does nothing either (and is the card's: no unit order, no targeting).
        Key(Godot.Key.B);
        Key(Godot.Key.A);
        Check(_sim.PendingCommandCount == start && !_sel.Targeting, "empty-cell keys on a production card did something");
        SetMoney(5000, 5000);
        await ClearQueues(new[] { _barracks, _armory });
    }

    // Criterion 3: the strip equals the queue every frame through [laborer, age_ii, laborer]; Age II completing flashes its name; a click on item k cancels k.
    private async Task QueueStripRun()
    {
        SetMoney(5000, 5000);
        // Room for two more laborers: the spawned soldiers count, so free population by raising the cap is not possible; use houses.
        int house = StartBase.BuildingOfSlot(_data, W.FactionOf(0), BuildingSlot.House);
        var taken = Enumerable.Range(0, B.Capacity).Where(k => B.Alive[k]).Select(k => B.Cell[k]).ToList();
        for (int i = 0; i < 3; i++)
        {
            int a = FreeAnchor(house, taken);
            taken.Add(a);
            _sim.Enqueue(Command.SpawnBuilding(0, house, G.CellCenter(a % G.Width, a / G.Width)));
        }
        Tick(2);
        // BUG-0124: Age II needs two finished halls of distinct slots; the Barracks and an Armory spawned above are they.
        bool ageOpen = W.CanResearch(0, _hall, _data.FindTech("age_ii"), out ResearchError ageWhy);
        Check(B.Alive[_barracks] && !B.UnderConstruction[_barracks] && B.Alive[_armory] && !B.UnderConstruction[_armory] && ageOpen,
            $"queue strip: Age II not open at the hall ({ageWhy})");
        _sel.SelectBuilding(_hall);
        await Frames();
        int laborerCell = 0, ageCell = 1;
        Check(_card.ActionAt(laborerCell) == CardCommand.Train && _card.ActionAt(ageCell) == CardCommand.Research && _card.TypeAt(ageCell) == _data.FindTech("age_ii"), "hall card layout");
        _card.Press(laborerCell);
        _card.Press(ageCell);
        _card.Press(laborerCell);
        Tick(2); // a command applies in the tick after the next
        await Frames();
        Check(B.QueueCount[_hall] == 3, $"hall queue {B.QueueCount[_hall]} after three presses");
        await Shot("card-queue");
        int frames = 0, mismatches = 0, flashAt = -1;
        while (B.QueueCount[_hall] > 0 && frames < 2000)
        {
            Tick(2);
            await Frames();
            frames++;
            int n = B.QueueCount[_hall];
            bool ok = _strip.ShownCount == n;
            for (int k = 0; k < n && ok; k++)
                ok = _strip.ShownType(k) == B.QueueTypeAt(_hall, k) && _strip.ShownIsTech(k) == B.QueueIsTechAt(_hall, k) && _strip.ItemButton(k).Visible;
            float fill = n == 0 ? 0f : Math.Clamp(B.Progress[_hall] / (float)B.ItemTicks(_hall, 0), 0f, 1f);
            ok &= Mathf.IsEqualApprox(_strip.ShownFill, fill) && Mathf.IsEqualApprox(_strip.HeadBar.Size.X, (ProductionQueueStrip.ItemWidth - 2f) * fill);
            for (int k = n; k < QueueStrip.MaxItems; k++) ok &= !_strip.ItemButton(k).Visible;
            if (!ok && mismatches++ < 5) Check(false, $"strip frame {frames}: shows {_strip.ShownCount} items fill {_strip.ShownFill}, sim {n} items fill {fill}");
            if (n > 0 && B.QueueIsTechAt(_hall, 0)) Check(_strip.ItemLabel(0).Text == _data.Techs[B.QueueTypeAt(_hall, 0)].DisplayName, $"tech head label '{_strip.ItemLabel(0).Text}'");
            if (flashAt < 0 && W.Age(0) == 2)
            {
                flashAt = frames;
                Check(_bar.AgeLabel.Visible && _bar.AgeLabel.Text == _data.Techs[_data.AgeTechs[0]].DisplayName && _bar.AgeFlashes == 1,
                    $"Age II: flash visible {_bar.AgeLabel.Visible} '{_bar.AgeLabel.Text}' flashes {_bar.AgeFlashes}");
            }
        }
        Check(B.QueueCount[_hall] == 0 && W.Age(0) == 2 && mismatches == 0, $"queue run: {frames} frames, queue {B.QueueCount[_hall]}, age {W.Age(0)}, mismatches {mismatches}");
        GD.Print($"queue strip: [laborer, age_ii, laborer] over {frames} frames, Age II flash at frame {flashAt}");

        // Click item k: one CancelTrain(k) at the footprint centre, one sound.
        for (int i = 0; i < 4; i++) _card.Press(laborerCell);
        Tick(2); // a command applies in the tick after the next
        await Frames();
        Check(_strip.ShownCount == 4, $"cancel row: strip shows {_strip.ShownCount}");
        foreach (int k in new[] { 2, 0 })
        {
            await SoundGap();
            int sounds = _sfx.PlayCount(SfxEvent.Command), start = _sim.PendingCommandCount;
            _strip.ItemButton(k).EmitSignal(BaseButton.SignalName.Pressed);
            List<Command> sent = Pending(start);
            Check(sent.Count == 1 && sent[0].Kind == CommandKind.CancelTrain && sent[0].TypeId == k && sent[0].Position == SelectionController.SiteCenter(W, _hall),
                $"cancel item {k}: {Describe(sent)}");
            Check(_sfx.PlayCount(SfxEvent.Command) == sounds + 1, $"cancel item {k}: no sound");
            Tick(2); // a command applies in the tick after the next
            await Frames();
        }
        Check(B.QueueCount[_hall] == 2 && _strip.ShownCount == 2, $"after two cancels: queue {B.QueueCount[_hall]}, strip {_strip.ShownCount}");
        int s0 = _sim.PendingCommandCount;
        _strip.ItemPressed(4); // past the count: nothing
        Check(_sim.PendingCommandCount == s0, "a click past the queue enqueued");
        await ClearQueues(new[] { _hall });
    }

    // Criterion 4: right-click ground with a building selected = one SetRally; on the building = one ClearRally; the flag at RallyPosition within a frame; the minimap = SetRally.
    private async Task Rally()
    {
        _sel.SelectBuilding(_hall);
        System.Numerics.Vector2 hall = HallCenter(0);
        System.Numerics.Vector2 ground = OpenGroundNear(hall + new System.Numerics.Vector2(0f, 12f), 0f);
        Vector2 px = await OnScreen(ground);
        if (!_sel.ContextTarget(px, out System.Numerics.Vector2 point, out int hitBuilding) || hitBuilding >= 0) Check(false, $"rally: the ground pixel hits building {hitBuilding}");
        await SoundGap();
        int sounds = _sfx.PlayCount(SfxEvent.Command), start = _sim.PendingCommandCount;
        RightClick(px);
        List<Command> sent = Pending(start);
        Check(sent.Count == 1 && sent[0].Kind == CommandKind.SetRally && sent[0].TypeId == B.Cell[_hall] && sent[0].Position == point,
            $"right-click ground: {Describe(sent)} (point {point})");
        Check(_sfx.PlayCount(SfxEvent.Command) == sounds + 1, "rally: no Command sound");
        Tick(2); // a command applies in the tick after the next
        await Frame();
        await Frame();
        Check(B.HasRally[_hall] && _rally.ShownSlot == _hall && _rally.ShownTarget == B.RallyPosition[_hall] && _rally.LineShown, $"flag: slot {_rally.ShownSlot}, at {_rally.ShownTarget}, sim {B.RallyPosition[_hall]}");
        Vector3 cone = _rally.Cone.GlobalPosition;
        Check(Mathf.IsEqualApprox(cone.X, point.X) && Mathf.IsEqualApprox(cone.Z, point.Y), $"cone at {cone}, rally {point}");
        await Shot("rally");

        // On a resource node: SetRally to that point (a trained worker gathers there).
        int mine = NearestMine(hall);
        Vector2 minePx = await OnScreen(NodeCenter(mine));
        _sel.ContextTarget(minePx, out System.Numerics.Vector2 minePoint, out _);
        start = _sim.PendingCommandCount;
        RightClick(minePx);
        sent = Pending(start);
        Check(sent.Count == 1 && sent[0].Kind == CommandKind.SetRally && sent[0].Position == minePoint && SelectionController.NodeAt(W, minePoint) == mine, $"right-click mine: {Describe(sent)}");
        Tick(2); // a command applies in the tick after the next
        await Frames();
        Check(_rally.ShownTarget == minePoint, $"flag after the mine rally at {_rally.ShownTarget}");

        // On the building itself: one ClearRally; the flag goes.
        Vector2 hallPx = await BoxScreen(_hall);
        Check(_sel.PickBuilding(hallPx) == _hall, "hall pixel misses the hall");
        start = _sim.PendingCommandCount;
        RightClick(hallPx);
        sent = Pending(start);
        Check(sent.Count == 1 && sent[0].Kind == CommandKind.ClearRally && sent[0].Position == SelectionController.SiteCenter(W, _hall), $"right-click own building: {Describe(sent)}");
        Tick(2); // a command applies in the tick after the next
        await Frames();
        Check(!B.HasRally[_hall] && _rally.ShownSlot == -1 && !_rally.Visible, $"after ClearRally: sim {B.HasRally[_hall]}, flag {_rally.ShownSlot}");

        // Minimap right-click: SetRally at that map point.
        MinimapTransform fit = _mini.Fit;
        System.Numerics.Vector2 target = hall + new System.Numerics.Vector2(-20f, 10f);
        System.Numerics.Vector2 mp = fit.ToPixel(target);
        start = _sim.PendingCommandCount;
        _mini._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = new Vector2(mp.X, mp.Y) });
        sent = Pending(start);
        fit.TryToMap(mp, out System.Numerics.Vector2 back);
        Check(sent.Count == 1 && sent[0].Kind == CommandKind.SetRally && sent[0].TypeId == B.Cell[_hall] && System.Numerics.Vector2.Distance(sent[0].Position, back) < 1e-3f,
            $"minimap right-click: {Describe(sent)}, want SetRally at {back}");
        Tick(2); // a command applies in the tick after the next
        await Frames();
        Check(_rally.ShownSlot == _hall && _rally.ShownTarget == B.RallyPosition[_hall], "minimap rally: no flag");

        // A selected site: a right click orders nothing.
        int house = StartBase.BuildingOfSlot(_data, W.FactionOf(0), BuildingSlot.House);
        var taken = Enumerable.Range(0, B.Capacity).Where(k => B.Alive[k]).Select(k => B.Cell[k]).ToList();
        int a = FreeAnchor(house, taken);
        _sim.Enqueue(Command.Build(0, Workers()[1], house, PlacementGhost.AnchorPoint(G, a)));
        Tick(30);
        int site = B.SlotAt(a % G.Width, a / G.Width);
        if (Check(site >= 0 && B.UnderConstruction[site], "rally: no site"))
        {
            _sel.SelectBuilding(site);
            await Frames();
            start = _sim.PendingCommandCount;
            RightClick(px);
            Check(_sim.PendingCommandCount == start && !_rally.Visible, "right-click with a site selected enqueued something");
            _sim.Enqueue(Command.Cancel(0, SelectionController.SiteCenter(W, site)));
            Tick(2);
        }
        _sel.ClearBuilding();
        await Frames();
        Check(!_rally.Visible, "flag shown with nothing selected");
    }

    // BUG-0108 regression: a right click on the visible top of a damaged own building's box (the far edge, whose ground
    // pick lies behind the footprint) sends Repairs; on an enemy building's box the workers move to its centre.
    private async Task Bug0108()
    {
        int max = _data.Buildings[B.TypeId[_barracks]].Hp;
        DamageMethod.Invoke(B, new object[] { B.HandleOf(_barracks), max / 3 });
        List<EntityHandle> three = Workers().Take(3).ToList();
        await Select(three);
        System.Numerics.Vector2 c = SelectionController.SiteCenter(W, _barracks);
        FocusOn(c);
        await Frames();
        BuildingDef def = _data.Buildings[B.TypeId[_barracks]];
        float top = TerrainHeight.At(W.Heightmap, c.X, c.Y) + BuildingViews.BoxHeight;
        Vector2 far = _camera.UnprojectPosition(new Vector3(c.X, top, c.Y - def.FootprintHeight * 0.85f));
        System.Numerics.Vector2 behind = Pick(far);
        Check(BuildingPicker.SlotAt(B, G, behind) != _barracks && _sel.PickBuilding(far) == _barracks, $"far-edge pixel: ground {behind} is inside the footprint or the box isn't hit");
        int start = _sim.PendingCommandCount;
        RightClick(far);
        List<Command> sent = Pending(start);
        Check(sent.Count == 3 && sent.All(x => x.Kind == CommandKind.Repair && x.Position == c), $"BUG-0108: far edge of the damaged barracks' top: {Describe(sent)}");
        Tick(2);
        await Frames();
    }

    // BUG-0109 regression: the click's own anchor decides. A click on a red spot while a green ghost is drawn elsewhere
    // places nothing; a click on another green spot builds there (not at the drawn anchor); a second differing click in the
    // same frame is ignored (one CanPlace a frame).
    private async Task Bug0109()
    {
        SetMoney(5000, 5000);
        List<EntityHandle> three = Workers().Take(3).ToList();
        await Select(three);
        Key(Godot.Key.B);
        Key(Godot.Key.Q);
        int house = _ghost.TypeId;
        if (!Check(_ghost.Active && house >= 0, "BUG-0109: no ghost")) return;
        var taken = Enumerable.Range(0, B.Capacity).Where(k => B.Alive[k]).Select(k => B.Cell[k]).ToList();
        int a1 = FreeAnchor(house, taken);
        taken.Add(a1);
        int a2 = FreeAnchor(house, taken);
        Vector2 px1 = await AnchorScreen(house, a1);
        _ghost.ScreenOverride = px1;
        await Frames();
        Check(_ghost.Anchor == a1 && _ghost.Valid, $"ghost at {_ghost.Anchor} valid {_ghost.Valid}, want green {a1}");
        // A click on the own Town Hall (red), delivered before the ghost's next Sync.
        Vector2 hallPx = _camera.UnprojectPosition(new Vector3(HallCenter(0).X, TerrainHeight.At(W.Heightmap, HallCenter(0).X, HallCenter(0).Y), HallCenter(0).Y));
        int start = _sim.PendingCommandCount, calls = _ghost.CanPlaceCalls;
        LeftClick(hallPx);
        Check(_sim.PendingCommandCount == start && _ghost.CanPlaceCalls == calls + 1, $"BUG-0109: a click on the hall sent {_sim.PendingCommandCount - start} commands, CanPlace calls +{_ghost.CanPlaceCalls - calls}");
        await Frames();
        // A click on another green anchor builds there.
        _ghost.ScreenOverride = px1;
        await Frames();
        Vector2 px2 = _camera.UnprojectPosition(GroundOf(AnchorCentre(house, a2)));
        start = _sim.PendingCommandCount;
        Input.ActionPress("order_queue"); // Shift keeps the ghost for the same-frame row
        LeftClick(px2);
        List<Command> sent = Pending(start);
        Check(sent.Count == 3 && sent.All(x => x.Kind == CommandKind.Build && x.Position == PlacementGhost.AnchorPoint(G, a2)),
            $"BUG-0109: a click on green anchor {a2} (drawn {a1}): {Describe(sent)}");
        // Same frame, another differing spot: ignored, no CanPlace.
        calls = _ghost.CanPlaceCalls;
        int ignored = _ghost.ClicksIgnored;
        start = _sim.PendingCommandCount;
        LeftClick(px1 + new Vector2(0f, 60f));
        Input.ActionRelease("order_queue");
        Check(_ghost.CanPlaceCalls == calls && _ghost.ClicksIgnored == ignored + 1 && _sim.PendingCommandCount == start,
            $"same-frame second click: CanPlace +{_ghost.CanPlaceCalls - calls}, ignored +{_ghost.ClicksIgnored - ignored}, sent {_sim.PendingCommandCount - start}");
        _ghost.ScreenOverride = null;
        Key(Godot.Key.Escape);
        Tick(3);
        await Frames();
    }

    // Criterion 8: 300 idle frames with the panel, a production card, a queue strip and a rally flag: 0 bytes, nothing rewritten.
    private async Task IdleAllocation()
    {
        SetMoney(5000, 5000);
        _sel.SelectBuilding(_hall);
        await Frames();
        _card.Press(0);
        _card.Press(0);
        _sim.Enqueue(Command.SetRally(0, B.Cell[_hall], HallCenter(0) + new System.Numerics.Vector2(10f, 12f)));
        Tick(2);
        await Frames();
        Check(_strip.ShownCount == 2 && _rally.Visible && _panel.ShownKind == 3 && _card.ActionAt(0) == CardCommand.Train, $"idle setup: strip {_strip.ShownCount}, flag {_rally.Visible}, panel {_panel.ShownKind}");
        void SyncAll()
        {
            _panel.Sync();
            _card.Sync();
            _strip.Sync();
            _rally.Sync(W);
            _bar.Sync(W);
        }
        SyncAll();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int f = 0; f < 300; f++) SyncAll();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(bytes == 0, $"300 idle Syncs (panel + card + strip + flag + bar) allocated {bytes} bytes");
        int rebuilds = _panel.Rebuilds, layouts = _card.Layouts, updates = _strip.Updates, flag = _rally.Updates, bar = _bar.Builds + _bar.PopBuilds;
        for (int f = 0; f < 300; f++) await Frame();
        Check(_panel.Rebuilds == rebuilds && _card.Layouts == layouts && _strip.Updates == updates && _rally.Updates == flag && _bar.Builds + _bar.PopBuilds == bar,
            $"idle frames rewrote: panel {rebuilds}->{_panel.Rebuilds}, card {layouts}->{_card.Layouts}, strip {updates}->{_strip.Updates}, flag {flag}->{_rally.Updates}");
        // A unit selected: the panel's unit view, idle.
        await Select(new List<EntityHandle> { Workers()[0] });
        _panel.Sync();
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int f = 0; f < 300; f++) _panel.Sync();
        long unitBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(unitBytes == 0, $"300 idle Syncs of the one-unit panel allocated {unitBytes} bytes");
        await Select(Own());
        _panel.Sync();
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int f = 0; f < 300; f++) _panel.Sync();
        long gridBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(gridBytes == 0, $"300 idle Syncs of the grid panel allocated {gridBytes} bytes");

        // BUG-0123: the hall selected while three workers repair it: hp changes every tick, the panel allocates nothing.
        int max = _data.Buildings[B.TypeId[_hall]].Hp;
        DamageMethod.Invoke(B, new object[] { B.HandleOf(_hall), Math.Max(1, B.Hp[_hall] - max / 3) });
        System.Numerics.Vector2 hc = SelectionController.SiteCenter(W, _hall);
        foreach (EntityHandle h in Workers().Take(3)) _sim.Enqueue(Command.Repair(0, h, hc));
        _sel.SelectBuilding(_hall);
        int hpStart = B.Hp[_hall];
        for (int t = 0; t < 400 && B.Hp[_hall] == hpStart; t++) _sim.Tick();
        _panel.Sync();
        int hp0 = B.Hp[_hall], panelRebuilds = _panel.Rebuilds;
        long repairBytes = 0;
        for (int f = 0; f < 300; f++)
        {
            _sim.Tick();
            before = GC.GetAllocatedBytesForCurrentThread();
            _panel.Sync();
            repairBytes += GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Check(repairBytes == 0 && B.Hp[_hall] > hp0 && _panel.Rebuilds - panelRebuilds > 100 && _panel.HpText == $"{B.Hp[_hall]} / {max}",
            $"BUG-0123: 300 repair ticks: panel allocated {repairBytes} bytes (hp {hp0} -> {B.Hp[_hall]}, {_panel.Rebuilds - panelRebuilds} rewrites, shows '{_panel.HpText}')");
        GD.Print($"repair: panel 0 B over 300 ticks, hp {hp0} -> {B.Hp[_hall]}, {_panel.Rebuilds - panelRebuilds} hp rewrites");

        // M4-V1 (BUG-0123 extended to a damaged unit): a Heavy Infantry selected while an enemy Raider beside it fights it:
        // its hp falls every few ticks, the panel shows it live ("now / max") and allocates nothing.
        int hi = _data.FindUnit("malazan_heavy_infantry"), raider = _data.FindUnit("whirlwind_raider");
        int spotCell = Rts.Sim.Pathfinding.FlowField.NearestPassable(G, (G.Height / 2) * G.Width + G.Width / 2);
        System.Numerics.Vector2 spot = G.CellCenter(spotCell % G.Width, spotCell / G.Width);
        var mineBefore = new HashSet<int>();
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i]) mineBefore.Add(i);
        _sim.Enqueue(Command.SpawnUnit(0, hi, spot));
        _sim.Enqueue(Command.SpawnUnit(1, raider, spot + new System.Numerics.Vector2(1.6f, 0f)));
        Tick(2);
        EntityHandle fighter = default;
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && !mineBefore.Contains(i) && U.Owner[i] == 0) fighter = new EntityHandle(i, U.Generation[i]);
        _sel.SelectOnly(fighter);
        int unitMax = _data.Units[hi].Hp;
        for (int t = 0; t < 400 && U.Hp[fighter.Index] == unitMax; t++) _sim.Tick();
        _panel.Sync();
        int unitHp0 = U.Hp[fighter.Index], unitRebuilds = _panel.Rebuilds, hpWrong = 0;
        long fightBytes = 0;
        for (int f = 0; f < 300 && U.IsAlive(fighter); f++)
        {
            _sim.Tick();
            before = GC.GetAllocatedBytesForCurrentThread();
            _panel.Sync();
            fightBytes += GC.GetAllocatedBytesForCurrentThread() - before;
            if (_panel.StatValue(0).Text != U.Hp[fighter.Index].ToString(System.Globalization.CultureInfo.InvariantCulture)) hpWrong++;
        }
        bool fighterAlive = U.IsAlive(fighter);
        Check(fighterAlive && unitHp0 < unitMax && U.Hp[fighter.Index] < unitHp0 && fightBytes == 0 && hpWrong == 0 && _panel.HpText == $"{U.Hp[fighter.Index]} / {unitMax}",
            $"M4-V1: 300 fight ticks with a hurt unit selected: panel allocated {fightBytes} bytes, {hpWrong} frames off the store, alive {fighterAlive}, hp {unitHp0} -> {(fighterAlive ? U.Hp[fighter.Index] : 0)}, shows '{_panel.HpText}'");
        GD.Print($"fight: one-unit panel 0 B over 300 ticks, hp {unitHp0} -> {U.Hp[fighter.Index]} of {unitMax}, {_panel.Rebuilds - unitRebuilds} rewrites");
        GD.Print($"idle: building view {bytes} bytes, one unit {unitBytes}, grid {gridBytes}");
    }

    // Criterion 5: a half-pop unit reads "Pop 5.5 / 10" (data copy with the Crossbowman at pop 0.5).
    private async Task HalfPopMatch()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"rts-halfpop-{System.Diagnostics.Process.GetCurrentProcess().Id}");
        string src = ProjectSettings.GlobalizePath("res://data");
        foreach (string file in Directory.GetFiles(src, "*.json", SearchOption.AllDirectories))
        {
            string to = Path.Combine(dir, Path.GetRelativePath(src, file));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file, to, overwrite: true);
        }
        string units = Path.Combine(dir, "factions", "malazan", "units.json");
        string text = File.ReadAllText(units);
        int crossbow = text.IndexOf("\"malazan_crossbowman\"", StringComparison.Ordinal);
        int popAt = text.IndexOf("\"pop\": 1,", crossbow, StringComparison.Ordinal);
        File.WriteAllText(units, text.Substring(0, popAt) + "\"pop\": 0.5," + text.Substring(popAt + "\"pop\": 1,".Length));
        DataLoadResult loaded = DataLoader.LoadAll(dir);
        Directory.Delete(dir, recursive: true);
        if (!Check(loaded.Ok, "half-pop data failed to load")) return;
        GameData data = loaded.Data!;
        int xbow = data.FindUnit("malazan_crossbowman");
        Check(data.Units[xbow].HalfPop == 1, $"crossbowman half-pop {data.Units[xbow].HalfPop}");
        await StartMatch(data);
        Check(_bar.PopLabel.Text == "Pop 5 / 10", $"half-pop match start: '{_bar.PopLabel.Text}'");
        _sim.Enqueue(Command.SpawnUnit(0, xbow, OpenGroundNear(HallCenter(0), 8f)));
        Tick(2);
        await Frames();
        Check(_bar.PopLabel.Text == "Pop 5.5 / 10" && !_bar.ShownAtCap, $"after a half-pop unit: '{_bar.PopLabel.Text}'");
        GD.Print($"half pop: '{_bar.PopLabel.Text}'");
        await EndMatch();
    }

    // ---- helpers ----

    private async Task ClearQueues(int[] buildings)
    {
        foreach (int k in buildings)
            for (int i = B.QueueCount[k] - 1; i >= 0; i--) _sim.Enqueue(Command.CancelTrain(0, SelectionController.SiteCenter(W, k), i));
        Tick(2);
        await Frames();
        foreach (int k in buildings) Check(B.QueueCount[k] == 0, $"building {k} queue not cleared ({B.QueueCount[k]})");
    }

    private void SetMoney(int gold, int wood)
    {
        object ledger = LedgerProperty.GetValue(W)!;
        var g = (int[])ledger.GetType().GetProperty("Gold")!.GetValue(ledger)!;
        var w = (int[])ledger.GetType().GetProperty("Wood")!.GetValue(ledger)!;
        g[0] = gold;
        w[0] = wood;
    }

    private int WorkerType() => _data.Factions[W.FactionOf(0)].Units.First(t => _data.Units[t].Slot == UnitSlot.Worker);

    private int FreeAnchor(int type, List<int> taken)
    {
        BuildingDef def = _data.Buildings[type];
        System.Numerics.Vector2 near = HallCenter(0);
        int best = -1;
        float bestD = float.PositiveInfinity;
        for (int cell = 0; cell < G.Width * G.Height; cell++)
        {
            System.Numerics.Vector2 c = StartBase.FootprintCenter(G, def, cell);
            float d = System.Numerics.Vector2.Distance(c, near);
            if (d >= bestD || d < 10f || d > 45f) continue;
            int x = cell % G.Width, y = cell / G.Width;
            if (taken.Any(t => Math.Abs(t % G.Width - x) < 8 && Math.Abs(t / G.Width - y) < 8)) continue;
            bool ok = W.CanPlace(0, type, cell, out PlacementError r);
            // A locked type (D3's building requires) answers Requires before any map rule: probe the map rule itself, the
            // dev spawn ignores requirements.
            if (!ok && r != PlacementError.CannotAfford && !(r == PlacementError.Requires && B.Fits(type, cell))) continue;
            bool empty = true;
            for (int i = 0; i < U.Capacity && empty; i++)
                if (U.Alive[i] && MathF.Abs(U.Position[i].X - c.X) < def.FootprintWidth + 3f && MathF.Abs(U.Position[i].Y - c.Y) < def.FootprintHeight + 3f) empty = false;
            if (empty) (best, bestD) = (cell, d);
        }
        if (best < 0) throw new InvalidOperationException($"no free anchor for {def.Key}");
        return best;
    }

    private System.Numerics.Vector2 AnchorCentre(int type, int anchor) => StartBase.FootprintCenter(G, _data.Buildings[type], anchor);

    private Vector3 GroundOf(System.Numerics.Vector2 p) => new(p.X, TerrainHeight.At(W.Heightmap, p.X, p.Y), p.Y);

    // A screen point whose ghost anchor is `anchor` (the cursor on the footprint's middle cell).
    private async Task<Vector2> AnchorScreen(int type, int anchor)
    {
        BuildingDef def = _data.Buildings[type];
        System.Numerics.Vector2 p = G.CellCenter(anchor % G.Width + def.FootprintWidth / 2, anchor / G.Width + def.FootprintHeight / 2);
        return await OnScreen(p);
    }

    private List<Command> Pending(int from)
    {
        var q = (CommandQueue)CommandsField.GetValue(_sim)!;
        var list = new List<Command>();
        for (int i = from; i < q.Count; i++) list.Add(q[i]);
        return list;
    }

    private static string Describe(List<Command> list) =>
        $"[{string.Join(", ", list.Select(c => $"{c.Kind}{(c.IsQueued ? "+q" : "")} u{c.Unit.Index} t{c.TypeId} ({c.Position.X:0.##},{c.Position.Y:0.##})"))}]";

    private List<EntityHandle> Workers()
    {
        var list = new List<EntityHandle>();
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && U.Owner[i] == 0 && _data.Units[U.TypeId[i]].Slot == UnitSlot.Worker) list.Add(new EntityHandle(i, U.Generation[i]));
        return list;
    }

    private List<EntityHandle> Own()
    {
        var list = new List<EntityHandle>();
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && U.Owner[i] == 0) list.Add(new EntityHandle(i, U.Generation[i]));
        return list;
    }

    private int HallSlot(int player)
    {
        for (int k = 0; k < B.Capacity; k++)
            if (B.Alive[k] && B.Owner[k] == player && _data.Buildings[B.TypeId[k]].Slot == BuildingSlot.TownHall) return k;
        throw new InvalidOperationException($"player {player} has no hall");
    }

    private System.Numerics.Vector2 HallCenter(int player) => SelectionController.SiteCenter(W, HallSlot(player));

    private int NearestMine(System.Numerics.Vector2 from)
    {
        ResourceStore r = W.Resources;
        int best = -1;
        float bestD = float.PositiveInfinity;
        for (int i = 0; i < r.Capacity; i++)
        {
            if (!r.Alive[i] || _data.Resources[r.TypeId[i]].Resource != ResourceKind.Gold) continue;
            float d = System.Numerics.Vector2.Distance(NodeCenter(i), from);
            if (d < bestD) (best, bestD) = (i, d);
        }
        return best;
    }

    private System.Numerics.Vector2 NodeCenter(int node)
    {
        ResourceDef def = _data.Resources[W.Resources.TypeId[node]];
        int c = W.Resources.Cell[node];
        return new System.Numerics.Vector2((c % G.Width + def.FootprintWidth / 2f) * MapConstants.CellSize, (c / G.Width + def.FootprintHeight / 2f) * MapConstants.CellSize);
    }

    private System.Numerics.Vector2 OpenGroundNear(System.Numerics.Vector2 p, float min)
    {
        G.WorldToCell(p, out int px, out int py);
        for (int r = (int)(min / MapConstants.CellSize); r < 30; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    System.Numerics.Vector2 c = G.CellCenter(px + dx, py + dy);
                    if (!StartLayout.IsOpen(G, px + dx, py + dy) || SelectionController.NodeAt(W, c) >= 0 || BuildingPicker.SlotAt(B, G, c) >= 0) continue;
                    bool near = false;
                    for (int k = 0; k < B.Capacity && !near; k++)
                        if (B.Alive[k] && System.Numerics.Vector2.Distance(SelectionController.SiteCenter(W, k), c) < 7f) near = true;
                    if (!near) return c;
                }
        throw new InvalidOperationException("no open ground");
    }

    private async Task<Vector2> BoxScreen(int slot)
    {
        System.Numerics.Vector2 c = SelectionController.SiteCenter(W, slot);
        FocusOn(c);
        await Frames();
        return _camera.UnprojectPosition(new Vector3(c.X, TerrainHeight.At(W.Heightmap, c.X, c.Y) + BuildingViews.BoxHeight / 2f, c.Y));
    }

    private async Task Select(List<EntityHandle> units)
    {
        _sel.ClearBuilding();
        _sel.Selection.Clear();
        foreach (EntityHandle h in units) _sel.Selection.Add(h);
        _sel.Subgroups.Update(_sel.Selection.Items, U.TypeId, reset: true);
        await Frames();
    }

    private void Key(Godot.Key key)
    {
        _sel._UnhandledInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        _sel._UnhandledInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    private void LeftClick(Vector2 at)
    {
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
    }

    private void RightClick(Vector2 at)
    {
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = at });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = at });
    }

    private async Task<Vector2> OnScreen(System.Numerics.Vector2 ground)
    {
        FocusOn(ground);
        await Frame();
        return _camera.UnprojectPosition(GroundOf(ground));
    }

    private void FocusOn(System.Numerics.Vector2 ground)
    {
        _camera.SetZoom(30f);
        _camera.SetFocus(ground.X, ground.Y);
    }

    private System.Numerics.Vector2 Pick(Vector2 px)
    {
        Vector3 o = _camera.ProjectRayOrigin(px), d = _camera.ProjectRayNormal(px);
        if (!GroundPicker.TryPick(W.Heightmap, new(o.X, o.Y, o.Z), new(d.X, d.Y, d.Z), out System.Numerics.Vector3 hit))
            return new(float.NaN, float.NaN);
        return new(hit.X, hit.Z);
    }

    private Task SoundGap() => WallClock.Wait(this, Sfx.MinGapMs + 20); // the wall clock, as Sfx's gap reads it (BUG-0220)

    private void Tick(int n)
    {
        for (int i = 0; i < n; i++) _sim.Tick();
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private async Task Frames()
    {
        await Frame();
        await Frame();
    }

    private async Task Shot(string name)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        await Frames();
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = $"{_shots}/hud-{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"screenshot {path}");
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
