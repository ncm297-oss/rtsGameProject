using System;
using System.Globalization;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>The HUD's command card, bottom right (docs/02 "HUD layout"; M3-V2): a 5 x 3 grid of buttons keyed <c>Q W E R T / A S D F G / Z X C V B</c>, the worker build menus and the placement ghost's lifecycle.</summary>
/// <remarks>
/// Contents follow the selection: units show Attack (A), Stop (S), Hold (H) and Move (M) on the middle row; when the
/// active Tab subgroup is a <c>worker</c> type also Build advanced (V) and Build basic (B) on the V and B cells; B / V
/// open a build menu, whose entries (<c>ui.json</c> <c>buildMenus</c>, the faction's building of each slot) take the grid
/// cells in order with their grid keys (docs/02 "Grid hotkeys": a menu owns the whole grid, so A picks its sixth entry,
/// not attack-move); picking one raises the <see cref="BuildGhost"/>. A selected own site shows Cancel on the B cell. A
/// selected own finished building shows its production card (M3-V3, <see cref="ProductionMenu"/>): the units it trains,
/// then the techs it researches, on cells 0, 1, 2 ... with their grid keys; each frame a button is greyed when
/// <c>World.CanTrain</c> / <c>CanResearch</c> refuses, its cost line replaced by the reason (<c>ui.json</c> <c>train</c> /
/// <c>research</c>), and only a change of reason rewrites it. Esc or a right click closes a menu and its ghost. A button
/// press does exactly what its key does (the same <see cref="SelectionController"/> call). Labels come from
/// <see cref="UiText"/> or the building's, unit's or tech's <c>displayName</c>, the tooltip from its <c>description</c>,
/// cost (faction resource names) and <c>requires</c> ("Needs ..." with display names), all built in <see cref="Init"/>;
/// a button's text is written only when the card's contents change (<see cref="Layouts"/>), so a steady frame allocates
/// nothing. Holds view state only (which menu is open).
/// </remarks>
public partial class CommandCard : Control
{
    /// <summary>Grid columns and rows (docs/02: 5 x 3).</summary>
    public const int Columns = 5, Rows = 3, Cells = Columns * Rows;

    /// <summary>Button size and gap in pixels.</summary>
    public const float ButtonWidth = 92f, ButtonHeight = 60f, Gap = 4f;

    // The grid cells of the unit commands: the middle row from A, then V and B on the bottom row.
    private const int AttackCell = 5, StopCell = 6, HoldCell = 7, MoveCell = 8, AdvancedCell = 13, BasicCell = 14, CancelCell = 14;

    private enum Mode { None = -1, Empty, Units, Workers, BasicMenu, AdvancedMenu, Site, Production }

    private static readonly StringName FontColor = "font_color";
    private static readonly Color CostColor = new(0.85f, 0.85f, 0.85f);
    private static readonly Color ReasonColor = new(1f, 0.45f, 0.4f);

    private SimRunner? _runner;
    private SelectionController _sel = null!;
    private BuildGhost _ghost = null!;
    private UiText _ui = null!;
    private GameData _data = null!;

    private readonly Button[] _buttons = new Button[Cells];
    private readonly Label[] _names = new Label[Cells];
    private readonly Label[] _hints = new Label[Cells];
    private readonly Label[] _costs = new Label[Cells];
    private readonly StringName[] _cardActions = new StringName[Cells];
    private readonly string[] _gridKeys = new string[Cells];
    private readonly CardCommand[] _actions = new CardCommand[Cells];
    private readonly int[] _types = new int[Cells];
    private readonly int[] _basic = new int[UiText.MaxMenuEntries], _advanced = new int[UiText.MaxMenuEntries];
    private int _basicCount, _advancedCount;
    private string[] _costText = Array.Empty<string>(), _tooltip = Array.Empty<string>();
    private string[] _unitCost = Array.Empty<string>(), _unitTip = Array.Empty<string>();
    private string[] _techCost = Array.Empty<string>(), _techTip = Array.Empty<string>();
    private readonly ProductionEntry[] _entries = new ProductionEntry[Cells];
    // Per cell of a production card: the reason on show (TrainError or ResearchError as int; -1 not set since the layout).
    private readonly int[] _shownReason = new int[Cells];
    private int _shownBuilding = -1, _shownBuildingGen;

    private Mode _menu = Mode.None;
    private Mode _shown = Mode.None;
    private int _placed;

    /// <summary>Times the card's contents were rewritten (only when what it shows changes).</summary>
    public int Layouts { get; private set; }

    /// <summary>True while a build menu (and maybe its ghost) is open.</summary>
    public bool MenuOpen => _menu != Mode.None;

    /// <summary>True for the advanced (Age II) menu, false for the basic one (meaningful while <see cref="MenuOpen"/>).</summary>
    public bool AdvancedMenuOpen => _menu == Mode.AdvancedMenu;

    /// <summary>True while the placement ghost is up.</summary>
    public bool GhostActive => _ghost != null && _ghost.Active;

    /// <summary>The ghost this card raises.</summary>
    public BuildGhost Ghost => _ghost;

    /// <summary>What grid cell <paramref name="i"/> (0 = Q ... 14 = B) does now.</summary>
    public CardCommand ActionAt(int i) => (uint)i < Cells ? _actions[i] : CardCommand.None;

    /// <summary>The building type of a <see cref="CardCommand.Place"/> cell, the unit type of a <see cref="CardCommand.Train"/> cell or the tech of a <see cref="CardCommand.Research"/> cell, else -1.</summary>
    public int TypeAt(int i) => (uint)i < Cells && _actions[i] is CardCommand.Place or CardCommand.Train or CardCommand.Research ? _types[i] : -1;

    /// <summary>The refusal a production cell shows now (<see cref="TrainError"/> for a Train cell, <see cref="ResearchError"/> for a Research cell, as int; 0 = enabled), or -1 for any other cell.</summary>
    public int ReasonAt(int i) => (uint)i < Cells && _actions[i] is CardCommand.Train or CardCommand.Research ? _shownReason[i] : -1;

    /// <summary>The button of grid cell <paramref name="i"/>.</summary>
    public Button ButtonAt(int i) => _buttons[i];

    /// <summary>The name label of grid cell <paramref name="i"/>: the command's or building's display name.</summary>
    public Label NameAt(int i) => _names[i];

    /// <summary>The hotkey hint label of grid cell <paramref name="i"/>.</summary>
    public Label HintAt(int i) => _hints[i];

    /// <summary>The cost label of grid cell <paramref name="i"/> (build menu entries).</summary>
    public Label CostAt(int i) => _costs[i];

    /// <summary>The grid key name of cell <paramref name="i"/> from the input map (<c>card_&lt;i&gt;</c>).</summary>
    public string GridKey(int i) => _gridKeys[i];

    public override void _Ready()
    {
        // Only the visible buttons take clicks; the empty cells and gaps let them through to the map.
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(Columns * ButtonWidth + (Columns - 1) * Gap, Rows * ButtonHeight + (Rows - 1) * Gap);
        for (int i = 0; i < Cells; i++)
        {
            int index = i;
            var b = new Button
            {
                Name = $"Cell{i}",
                Position = new Vector2(i % Columns * (ButtonWidth + Gap), i / Columns * (ButtonHeight + Gap)),
                Size = new Vector2(ButtonWidth, ButtonHeight),
                FocusMode = FocusModeEnum.None,
                Visible = false,
            };
            // The name sits between the hint (top left) and the cost (bottom right), wrapping onto two lines.
            var name = new Label
            {
                Name = "Name", Position = new Vector2(2f, 13f), Size = new Vector2(ButtonWidth - 4f, 32f), MouseFilter = MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart, ClipText = true,
            };
            name.AddThemeFontSizeOverride("font_size", 12);
            name.AddThemeConstantOverride("line_spacing", -2);
            var hint = new Label { Name = "Hint", Position = new Vector2(4f, 1f), MouseFilter = MouseFilterEnum.Ignore };
            hint.AddThemeFontSizeOverride("font_size", 11);
            hint.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.35f));
            var cost = new Label
            {
                Name = "Cost", Position = new Vector2(0f, ButtonHeight - 17f), Size = new Vector2(ButtonWidth - 4f, 16f),
                HorizontalAlignment = HorizontalAlignment.Right, MouseFilter = MouseFilterEnum.Ignore,
            };
            cost.AddThemeFontSizeOverride("font_size", 10);
            cost.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f));
            b.AddChild(name);
            b.AddChild(hint);
            b.AddChild(cost);
            b.Pressed += () => Press(index);
            AddChild(b);
            _buttons[i] = b;
            _names[i] = name;
            _hints[i] = hint;
            _costs[i] = cost;
            _cardActions[i] = new StringName($"card_{i}");
            _gridKeys[i] = KeyName(_cardActions[i]);
        }
    }

    /// <summary>Connects the card to the match; call once after the sim exists. <paramref name="ui"/> is the loaded <c>ui.json</c>.</summary>
    public void Init(SimRunner runner, SelectionController selection, BuildGhost ghost, UiText ui)
    {
        _runner = runner;
        _sel = selection;
        _ghost = ghost;
        _ui = ui;
        World world = runner.Simulation!.World;
        _data = world.Data;
        int faction = world.FactionOf(SelectionController.LocalPlayer);
        _basicCount = BuildMenu.Entries(_data.Buildings, faction, ui.BasicMenu, _basic);
        _advancedCount = BuildMenu.Entries(_data.Buildings, faction, ui.AdvancedMenu, _advanced);
        // Every string a button can show is made here, once.
        FactionDef f = _data.Factions[Math.Max(faction, 0)];
        _costText = new string[_data.Buildings.Length];
        _tooltip = new string[_data.Buildings.Length];
        for (int t = 0; t < _data.Buildings.Length; t++)
        {
            BuildingDef def = _data.Buildings[t];
            _costText[t] = string.Create(CultureInfo.InvariantCulture, $"{def.CostGold} / {def.CostWood}");
            _tooltip[t] = string.Create(CultureInfo.InvariantCulture, $"{def.Description}\n{f.GoldName} {def.CostGold}  {f.WoodName} {def.CostWood}");
        }
        _unitCost = new string[_data.Units.Length];
        _unitTip = new string[_data.Units.Length];
        for (int t = 0; t < _data.Units.Length; t++)
        {
            UnitDef def = _data.Units[t];
            _unitCost[t] = string.Create(CultureInfo.InvariantCulture, $"{def.CostGold} / {def.CostWood}");
            _unitTip[t] = Tooltip(def.Description, f, def.CostGold, def.CostWood, def.Requires, ui);
        }
        _techCost = new string[_data.Techs.Length];
        _techTip = new string[_data.Techs.Length];
        for (int t = 0; t < _data.Techs.Length; t++)
        {
            TechDef def = _data.Techs[t];
            _techCost[t] = string.Create(CultureInfo.InvariantCulture, $"{def.CostGold} / {def.CostWood}");
            _techTip[t] = Tooltip(def.Description, f, def.CostGold, def.CostWood, def.Requires, ui);
        }
        selection.Card = this;
        _shown = Mode.None;
        Sync();
    }

    public override void _Process(double delta) => Sync();

    /// <summary>Brings the card in line with the selection: closes a menu the selection no longer allows and rewrites the buttons when what they should show changed. Allocation-free when nothing changed.</summary>
    public void Sync()
    {
        if (_runner?.Simulation is not Simulation sim) return;
        Mode want;
        int building = _sel.SelectedBuilding;
        if (building >= 0)
        {
            if (MenuOpen) CloseMenu();
            BuildingStore b = sim.World.Buildings;
            want = b.UnderConstruction[building] ? Mode.Site : b.Owner[building] == SelectionController.LocalPlayer ? Mode.Production : Mode.Empty;
            // Another building of a different type needs its own buttons.
            if (want == Mode.Production && _shown == Mode.Production && (building != _shownBuilding || b.Generation[building] != _shownBuildingGen))
                _shown = Mode.None;
        }
        else if (_sel.Selection.Count == 0)
        {
            if (MenuOpen) CloseMenu();
            want = Mode.Empty;
        }
        else if (!_sel.ActiveSubgroupIsWorker || _sel.LiveWorkerCount() == 0)
        {
            if (MenuOpen) CloseMenu();
            want = _sel.Selection.Count > 0 ? Mode.Units : Mode.Empty;
        }
        else want = MenuOpen ? _menu : Mode.Workers;
        if (want != _shown) Layout(want, building);
        if (_shown == Mode.Production) Grey(sim.World, building);
    }

    // Greys each production button the sim would refuse now; touches a button only when its reason changed.
    private void Grey(World world, int building)
    {
        for (int i = 0; i < Cells; i++)
        {
            CardCommand c = _actions[i];
            if (c is not (CardCommand.Train or CardCommand.Research)) continue;
            int reason;
            if (c == CardCommand.Train)
            {
                world.CanTrain(SelectionController.LocalPlayer, building, _types[i], out TrainError e);
                reason = (int)e;
            }
            else
            {
                world.CanResearch(SelectionController.LocalPlayer, building, _types[i], out ResearchError e);
                reason = (int)e;
            }
            if (reason == _shownReason[i]) continue;
            _shownReason[i] = reason;
            bool ok = reason == 0;
            _buttons[i].Disabled = !ok;
            string cost = c == CardCommand.Train ? _unitCost[_types[i]] : _techCost[_types[i]];
            _costs[i].Text = ok ? cost : c == CardCommand.Train ? _ui.TrainText((TrainError)reason) : _ui.ResearchText((ResearchError)reason);
            _costs[i].AddThemeColorOverride(FontColor, ok ? CostColor : ReasonColor);
        }
    }

    // "<description>\n<gold> G  <wood> W" plus "\nNeeds A, B" when the def requires anything (display names).
    private string Tooltip(string description, FactionDef f, int gold, int wood, System.Collections.Immutable.ImmutableArray<string> requires, UiText ui)
    {
        string tip = string.Create(CultureInfo.InvariantCulture, $"{description}\n{f.GoldName} {gold}  {f.WoodName} {wood}");
        if (requires.IsDefaultOrEmpty) return tip;
        var names = new string[requires.Length];
        for (int k = 0; k < names.Length; k++) names[k] = ProductionMenu.RequirementName(_data, requires[k]);
        return $"{tip}\n{ui.Hud(HudText.Needs)} {string.Join(", ", names)}";
    }

    /// <summary>Opens the basic (B) or advanced (V) build menu; only with a worker subgroup active.</summary>
    public bool OpenMenu(bool advanced)
    {
        if (!_sel.ActiveSubgroupIsWorker || _sel.SelectedBuilding >= 0) return false;
        _ghost.End();
        _menu = advanced ? Mode.AdvancedMenu : Mode.BasicMenu;
        _sel.CancelTargeting();
        Sync();
        return true;
    }

    /// <summary>Closes the build menu and takes its ghost down.</summary>
    public void CloseMenu()
    {
        _ghost.End();
        _menu = Mode.None;
        if (_runner != null) Sync();
    }

    /// <summary>
    /// What a left click at <paramref name="screen"/> on the map does while the ghost is up: the anchor under the click
    /// (<see cref="BuildGhost.ResolveClick"/>, BUG-0109: not the last frame's drawn one) when green gets one <c>Build</c> per
    /// selected worker (<see cref="SelectionController.OrderBuild"/>, which plays the Command sound) and the menu closes
    /// unless <paramref name="shift"/> keeps the ghost for another; red, off the map or unresolved this frame: nothing (no
    /// sound). A Shift placement after the first of the same ghost is queued behind it, so the workers build them in turn.
    /// Returns true if it placed.
    /// </summary>
    public bool GhostClick(bool shift, Vector2 screen)
    {
        if (!_ghost.Active || !_ghost.ResolveClick(screen, out int anchor)) return false;
        if (_sel.OrderBuild(_ghost.TypeId, anchor, queued: shift && _placed > 0) == 0) return false;
        _placed++;
        if (!shift) CloseMenu();
        return true;
    }

    /// <summary>The card's keys, seen before the selection controller's: a menu's grid keys and Esc, a site's Cancel, B / V on a worker card. True when the key was the card's.</summary>
    public bool HandleKey(InputEvent e)
    {
        if (_runner == null || !e.IsPressed() || e.IsEcho()) return false;
        Sync();
        if (MenuOpen)
        {
            if (e.IsActionPressed("order_cancel"))
            {
                CloseMenu();
                return true;
            }
            for (int i = 0; i < Cells; i++)
            {
                if (!e.IsActionPressed(_cardActions[i])) continue;
                Press(i); // an empty cell does nothing, but the grid key is still the menu's
                return true;
            }
            return false;
        }
        if (_shown == Mode.Site && e.IsActionPressed(_cardActions[CancelCell]))
        {
            Press(CancelCell);
            return true;
        }
        if (_shown == Mode.Production)
        {
            // A production card owns the whole grid (docs/02 "Grid hotkeys"); a greyed or empty cell does nothing.
            for (int i = 0; i < Cells; i++)
            {
                if (!e.IsActionPressed(_cardActions[i])) continue;
                Press(i);
                return true;
            }
        }
        if (_shown == Mode.Workers)
        {
            if (e.IsActionPressed("build_basic")) return OpenMenu(false);
            if (e.IsActionPressed("build_advanced")) return OpenMenu(true);
        }
        return false;
    }

    /// <summary>What a press of grid cell <paramref name="i"/>'s button does (also its key): the same call the command's key makes.</summary>
    public void Press(int i)
    {
        if ((uint)i >= Cells || _runner == null) return;
        bool queued = Input.IsActionPressed("order_queue");
        switch (_actions[i])
        {
            case CardCommand.Move: _sel.BeginMove(); break;
            case CardCommand.AttackMove: _sel.BeginAttackMove(); break;
            case CardCommand.Stop:
                _sel.CancelTargeting();
                _sel.Order(CommandKind.Stop, null, queued);
                break;
            case CardCommand.Hold:
                _sel.CancelTargeting();
                _sel.Order(CommandKind.HoldPosition, null, queued);
                break;
            case CardCommand.BuildBasic: OpenMenu(false); break;
            case CardCommand.BuildAdvanced: OpenMenu(true); break;
            case CardCommand.Cancel: _sel.CancelSelectedSite(); break;
            case CardCommand.Place:
                _ghost.Begin(_types[i]);
                _placed = 0;
                break;
            // Produce asks CanTrain / CanResearch again: a greyed press (its key) enqueues nothing and plays nothing.
            case CardCommand.Train: _sel.Produce(_types[i], isTech: false); break;
            case CardCommand.Research: _sel.Produce(_types[i], isTech: true); break;
        }
    }

    // Rewrites every cell for a new mode; the only place button texts change.
    private void Layout(Mode mode, int building)
    {
        _shown = mode;
        Layouts++;
        Array.Clear(_actions);
        Array.Fill(_types, -1);
        Array.Fill(_shownReason, -1);
        switch (mode)
        {
            case Mode.Units:
            case Mode.Workers:
                _actions[AttackCell] = CardCommand.AttackMove;
                _actions[StopCell] = CardCommand.Stop;
                _actions[HoldCell] = CardCommand.Hold;
                _actions[MoveCell] = CardCommand.Move;
                if (mode == Mode.Workers)
                {
                    _actions[AdvancedCell] = CardCommand.BuildAdvanced;
                    _actions[BasicCell] = CardCommand.BuildBasic;
                }
                break;
            case Mode.BasicMenu:
            case Mode.AdvancedMenu:
                int[] list = mode == Mode.BasicMenu ? _basic : _advanced;
                int n = mode == Mode.BasicMenu ? _basicCount : _advancedCount;
                for (int k = 0; k < n; k++)
                {
                    _actions[k] = CardCommand.Place;
                    _types[k] = list[k];
                }
                break;
            case Mode.Site:
                _actions[CancelCell] = CardCommand.Cancel;
                break;
            case Mode.Production:
                BuildingStore bs = _runner!.Simulation!.World.Buildings;
                _shownBuilding = building;
                _shownBuildingGen = bs.Generation[building];
                int count = ProductionMenu.Entries(_data, bs.TypeId[building], _entries);
                for (int k = 0; k < count; k++)
                {
                    _actions[k] = _entries[k].IsTech ? CardCommand.Research : CardCommand.Train;
                    _types[k] = _entries[k].TypeId;
                }
                break;
        }
        for (int i = 0; i < Cells; i++)
        {
            Button b = _buttons[i];
            CardCommand c = _actions[i];
            if (c == CardCommand.None)
            {
                b.Visible = false;
                continue;
            }
            if (c == CardCommand.Place)
            {
                BuildingDef def = _data.Buildings[_types[i]];
                _names[i].Text = def.DisplayName;
                b.TooltipText = _tooltip[def.Id];
                _hints[i].Text = _gridKeys[i];
                _costs[i].Text = _costText[def.Id];
            }
            else if (c == CardCommand.Train)
            {
                UnitDef def = _data.Units[_types[i]];
                _names[i].Text = def.DisplayName;
                b.TooltipText = _unitTip[def.Id];
                _hints[i].Text = _gridKeys[i];
                _costs[i].Text = _unitCost[def.Id];
            }
            else if (c == CardCommand.Research)
            {
                TechDef def = _data.Techs[_types[i]];
                _names[i].Text = def.DisplayName;
                b.TooltipText = _techTip[def.Id];
                _hints[i].Text = _gridKeys[i];
                _costs[i].Text = _techCost[def.Id];
            }
            else
            {
                _names[i].Text = _ui.CommandName(c);
                b.TooltipText = "";
                _hints[i].Text = _ui.CommandHint(c);
                _costs[i].Text = "";
            }
            // Greying is per frame (Grey); a fresh layout starts enabled in the cost colour.
            b.Disabled = false;
            _costs[i].AddThemeColorOverride(FontColor, CostColor);
            b.Visible = true;
        }
    }

    // The key bound to an input action, as the keyboard labels it (rebinding-proof; not a C# literal).
    private static string KeyName(StringName action)
    {
        if (!InputMap.HasAction(action)) return "";
        foreach (InputEvent e in InputMap.ActionGetEvents(action))
        {
            if (e is not InputEventKey k) continue;
            // The headless display server has no keyboard layout: the physical key's (US) name stands in.
            bool layout = DisplayServer.GetName() != "headless";
            Key code = k.PhysicalKeycode == Key.None ? k.Keycode
                : layout ? DisplayServer.KeyboardGetKeycodeFromPhysical(k.PhysicalKeycode) : k.PhysicalKeycode;
            if (code == Key.None) code = k.PhysicalKeycode;
            return OS.GetKeycodeString(code);
        }
        return "";
    }
}
