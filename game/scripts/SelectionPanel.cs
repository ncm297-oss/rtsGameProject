using System;
using System.Globalization;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>The HUD's selection panel, bottom centre (docs/02 "HUD layout"; M3-V3): one unit's portrait and stats, a grid of several units' portraits, or a building's name and hit points.</summary>
/// <remarks>
/// One unit: a placeholder portrait (a square in the type's colour with its initial), the <c>displayName</c>, hit points,
/// attack, armor, range and speed from its <see cref="UnitDef"/>, each researched <c>World.TechBonus</c> as "+N" in a second
/// colour (combat does not apply bonuses until M4), and its state in plain words (<c>ui.json</c> <c>states</c>). Units
/// have no hit points in the sim until combat (M4), so hp shows max / max. Several units: up to
/// <see cref="PortraitGrid.MaxPortraits"/> type-coloured portraits in selection order with a "+N" overflow, the active Tab
/// subgroup's outlined; a click on one selects that unit alone (<see cref="SelectionController.SelectOnly"/>). A building:
/// its <c>displayName</c> and hp (its production card is on the <see cref="CommandCard"/>, its queue on the
/// <see cref="ProductionQueueStrip"/>). Labels come from <c>ui.json</c> <c>hud</c>. Every node is made in
/// <see cref="_Ready"/>; a value's text is rebuilt only when the value shown changes (a building's hp under repair changes
/// every tick: one small string then), so a steady frame allocates nothing. Holds view state only.
/// </remarks>
public partial class SelectionPanel : Control
{
    /// <summary>Portrait size and gap in the multi-select grid, in pixels.</summary>
    public const float CellSize = 40f, CellGap = 4f;

    /// <summary>The single-unit portrait's size in pixels.</summary>
    public const float PortraitSize = 72f;

    private enum Kind { None, Unit, Units, Building }

    private static readonly StringName FontColor = "font_color";
    private static readonly Color BonusColor = new(0.35f, 0.95f, 0.45f);
    private static readonly Color HighlightColor = new(1f, 0.9f, 0.3f);
    private static readonly HudText[] StatRows = { HudText.Hp, HudText.Attack, HudText.Armor, HudText.Range, HudText.Speed };
    private const int Hp = 0, Attack = 1, Armor = 2, Range = 3, Speed = 4;

    private SimRunner? _runner;
    private SelectionController _sel = null!;
    private UiText _ui = null!;
    private GameData _data = null!;
    private Color[] _typeColors = Array.Empty<Color>();
    private string[] _initials = Array.Empty<string>();

    // A translucent backdrop so the text reads over the terrain.
    private ColorRect _backdrop = null!;

    // Single unit / building.
    private Control _single = null!;
    private ColorRect _portrait = null!;
    private Label _initial = null!, _name = null!, _state = null!;
    private readonly Label[] _statName = new Label[StatRows.Length];
    private readonly Label[] _statValue = new Label[StatRows.Length];
    private readonly Label[] _statBonus = new Label[StatRows.Length];

    // Grid.
    private Control _grid = null!;
    private readonly Button[] _cells = new Button[PortraitGrid.MaxPortraits];
    private readonly ColorRect[] _cellFill = new ColorRect[PortraitGrid.MaxPortraits];
    private readonly ColorRect[] _cellFrame = new ColorRect[PortraitGrid.MaxPortraits];
    private readonly Label[] _cellInitial = new Label[PortraitGrid.MaxPortraits];
    private Label _overflow = null!;

    // What is on show.
    private Kind _kind = Kind.None;
    private int _slot = -1, _gen, _type = -1;
    private readonly float[] _value = new float[StatRows.Length];
    private readonly float[] _bonus = new float[StatRows.Length];
    private int _hpNow = -1, _stateShown = -1;
    private readonly EntityHandle[] _cellUnit = new EntityHandle[PortraitGrid.MaxPortraits];
    private readonly int[] _cellType = new int[PortraitGrid.MaxPortraits];
    private readonly bool[] _cellActive = new bool[PortraitGrid.MaxPortraits];
    private int _shownCells, _shownOverflow = -1;

    /// <summary>Times a text in the panel was rebuilt (only when what it shows changes).</summary>
    public int Rebuilds { get; private set; }

    /// <summary>What the panel shows: 0 nothing, 1 one unit, 2 a grid, 3 a building.</summary>
    public int ShownKind => (int)_kind;

    /// <summary>The name label (one unit or a building).</summary>
    public Label NameLabel => _name;

    /// <summary>The state label (one unit).</summary>
    public Label StateLabel => _state;

    /// <summary>The value label of stat row <paramref name="row"/> (0 hp, 1 attack, 2 armor, 3 range, 4 speed).</summary>
    public Label StatValue(int row) => _statValue[row];

    /// <summary>The "+N" bonus label of stat row <paramref name="row"/>; hidden without a bonus.</summary>
    public Label StatBonus(int row) => _statBonus[row];

    /// <summary>The name label of stat row <paramref name="row"/>.</summary>
    public Label StatName(int row) => _statName[row];

    /// <summary>Portraits on show in the grid.</summary>
    public int ShownCells => _shownCells;

    /// <summary>The "+N" overflow label of the grid.</summary>
    public Label OverflowLabel => _overflow;

    /// <summary>The portrait button of grid cell <paramref name="k"/>.</summary>
    public Button CellButton(int k) => _cells[k];

    /// <summary>The unit grid cell <paramref name="k"/> shows.</summary>
    public EntityHandle CellUnit(int k) => _cellUnit[k];

    /// <summary>True when grid cell <paramref name="k"/> is outlined as the active Tab subgroup.</summary>
    public bool CellHighlighted(int k) => _cellFrame[k].Visible;

    /// <summary>The fill colour of grid cell <paramref name="k"/> (its unit type's colour).</summary>
    public Color CellColor(int k) => _cellFill[k].Color;

    /// <summary>The single portrait's colour.</summary>
    public Color PortraitColor => _portrait.Color;

    /// <summary>A unit type's placeholder portrait colour: a hue from the type id (golden-ratio steps), so types read apart.</summary>
    public static Color TypeColor(int typeId)
    {
        float h = typeId * 0.618034f % 1f;
        return Color.FromHsv(h < 0 ? h + 1f : h, 0.55f, 0.85f);
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _backdrop = new ColorRect { Name = "Backdrop", Color = new Color(0f, 0f, 0f, 0.45f), MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_backdrop);
        _single = new Control { Name = "Single", Position = new Vector2(8f, 2f), MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        AddChild(_single);
        _portrait = new ColorRect { Name = "Portrait", Size = new Vector2(PortraitSize, PortraitSize), Position = new Vector2(0f, 8f), MouseFilter = MouseFilterEnum.Ignore };
        _initial = Text("Initial", new Vector2(0f, 8f), new Vector2(PortraitSize, PortraitSize), 34);
        _initial.HorizontalAlignment = HorizontalAlignment.Center;
        _initial.VerticalAlignment = VerticalAlignment.Center;
        _name = Text("Name", new Vector2(PortraitSize + 12f, 0f), new Vector2(300f, 24f), 18);
        _state = Text("State", new Vector2(0f, PortraitSize + 14f), new Vector2(PortraitSize + 40f, 20f), 13);
        _single.AddChild(_portrait);
        _single.AddChild(_initial);
        _single.AddChild(_name);
        _single.AddChild(_state);
        for (int r = 0; r < StatRows.Length; r++)
        {
            float y = 28f + r * 20f;
            _statName[r] = Text($"Stat{r}", new Vector2(PortraitSize + 12f, y), new Vector2(70f, 20f), 14);
            _statValue[r] = Text($"Value{r}", new Vector2(PortraitSize + 86f, y), new Vector2(110f, 20f), 14);
            _statBonus[r] = Text($"Bonus{r}", new Vector2(PortraitSize + 200f, y), new Vector2(60f, 20f), 14);
            _statBonus[r].AddThemeColorOverride(FontColor, BonusColor);
            _single.AddChild(_statName[r]);
            _single.AddChild(_statValue[r]);
            _single.AddChild(_statBonus[r]);
        }

        _grid = new Control { Name = "Grid", Position = new Vector2(8f, 5f), MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        AddChild(_grid);
        for (int k = 0; k < PortraitGrid.MaxPortraits; k++)
        {
            int index = k;
            var pos = new Vector2(k % PortraitGrid.Columns * (CellSize + CellGap), k / PortraitGrid.Columns * (CellSize + CellGap));
            var b = new Button { Name = $"Cell{k}", Position = pos, Size = new Vector2(CellSize, CellSize), FocusMode = FocusModeEnum.None, Flat = true, Visible = false };
            var frame = new ColorRect { Name = "Frame", Color = HighlightColor, Position = new Vector2(-2f, -2f), Size = new Vector2(CellSize + 4f, CellSize + 4f), MouseFilter = MouseFilterEnum.Ignore, Visible = false, ShowBehindParent = true };
            var fill = new ColorRect { Name = "Fill", Position = new Vector2(2f, 2f), Size = new Vector2(CellSize - 4f, CellSize - 4f), MouseFilter = MouseFilterEnum.Ignore };
            Label initial = Text("Initial", Vector2.Zero, new Vector2(CellSize, CellSize), 18);
            initial.HorizontalAlignment = HorizontalAlignment.Center;
            initial.VerticalAlignment = VerticalAlignment.Center;
            b.AddChild(frame);
            b.AddChild(fill);
            b.AddChild(initial);
            b.Pressed += () => CellPressed(index);
            _grid.AddChild(b);
            _cells[k] = b;
            _cellFill[k] = fill;
            _cellFrame[k] = frame;
            _cellInitial[k] = initial;
            _cellType[k] = -1;
        }
        _overflow = Text("Overflow", new Vector2(PortraitGrid.Columns * (CellSize + CellGap) + 4f, 2f * (CellSize + CellGap) + 8f), new Vector2(60f, 24f), 16);
        _overflow.Visible = false;
        _grid.AddChild(_overflow);
    }

    /// <summary>Connects the panel to the match; call once after the sim exists. <paramref name="ui"/> is the loaded <c>ui.json</c>.</summary>
    public void Init(SimRunner runner, SelectionController selection, UiText ui)
    {
        _runner = runner;
        _sel = selection;
        _ui = ui;
        _data = runner.Simulation!.World.Data;
        _typeColors = new Color[_data.Units.Length];
        _initials = new string[_data.Units.Length];
        for (int t = 0; t < _data.Units.Length; t++)
        {
            _typeColors[t] = TypeColor(t);
            string n = _data.Units[t].DisplayName;
            _initials[t] = n.Length > 0 ? n.Substring(0, 1).ToUpperInvariant() : "";
        }
        for (int r = 0; r < StatRows.Length; r++) _statName[r].Text = ui.Hud(StatRows[r]);
        _kind = Kind.None;
        Sync();
    }

    public override void _Process(double delta) => Sync();

    /// <summary>Brings the panel in line with the selection and the sim; rebuilds a text only when its value changed. Allocation-free when nothing changed.</summary>
    public void Sync()
    {
        if (_runner?.Simulation is not Simulation sim) return;
        World world = sim.World;
        int building = _sel.SelectedBuilding;
        UnitStore u = world.Units;
        if (building >= 0) ShowBuilding(world, building);
        else
        {
            // Count the live selection without pruning it (the controller prunes in its own _Process).
            int live = 0, first = -1;
            foreach (EntityHandle h in _sel.Selection.Items)
            {
                if (!u.Alive[h.Index] || u.Generation[h.Index] != h.Generation) continue;
                if (first < 0) first = h.Index;
                live++;
            }
            if (live == 1) ShowUnit(world, first);
            else if (live > 1) ShowGrid(world, live);
            else SetKind(Kind.None);
        }
    }

    private void SetKind(Kind kind)
    {
        if (kind == _kind) return;
        _kind = kind;
        _single.Visible = kind is Kind.Unit or Kind.Building;
        _grid.Visible = kind == Kind.Units;
        _backdrop.Visible = kind != Kind.None;
        _slot = -1;
        _type = -1;
        _hpNow = -1;
        _stateShown = -1;
        _shownOverflow = -1;
        for (int r = 0; r < StatRows.Length; r++)
        {
            _value[r] = float.NaN;
            _bonus[r] = float.NaN;
        }
    }

    private void ShowUnit(World world, int slot)
    {
        SetKind(Kind.Unit);
        UnitStore u = world.Units;
        int type = u.TypeId[slot];
        UnitDef def = _data.Units[type];
        if (slot != _slot || u.Generation[slot] != _gen || type != _type)
        {
            _slot = slot;
            _gen = u.Generation[slot];
            _type = type;
            _portrait.Color = _typeColors[type];
            _initial.Text = _initials[type];
            _name.Text = def.DisplayName;
            for (int r = 0; r < StatRows.Length; r++) _statName[r].Visible = _statValue[r].Visible = true;
            _state.Visible = true;
            Rebuilds++;
        }
        int p = u.Owner[slot];
        // Units have no hit points in the sim before combat (M4): current = max.
        float hpBonus = world.TechBonus(p, type, TechStat.Hp);
        SetStat(Hp, def.Hp, hpBonus, hpNow: def.Hp);
        SetStat(Attack, def.Attack.Value, world.TechBonus(p, type, TechStat.Attack));
        SetStat(Armor, def.Armor, world.TechBonus(p, type, TechStat.Armor));
        SetStat(Range, def.Attack.Range, world.TechBonus(p, type, TechStat.Range));
        SetStat(Speed, def.SpeedPerTick * SimConstants.TicksPerSecond, 0f);
        int state = (int)u.State[slot];
        if (state != _stateShown)
        {
            _stateShown = state;
            _state.Text = _ui.StateText((UnitState)state);
            Rebuilds++;
        }
    }

    private void ShowBuilding(World world, int slot)
    {
        SetKind(Kind.Building);
        BuildingStore b = world.Buildings;
        int type = b.TypeId[slot];
        BuildingDef def = _data.Buildings[type];
        if (slot != _slot || b.Generation[slot] != _gen || type != _type)
        {
            _slot = slot;
            _gen = b.Generation[slot];
            _type = type;
            _portrait.Color = TypeColor(_data.Units.Length + type); // building types take the hues after the unit types
            _initial.Text = def.DisplayName.Length > 0 ? def.DisplayName.Substring(0, 1) : "";
            _name.Text = def.DisplayName;
            for (int r = 0; r < StatRows.Length; r++) _statName[r].Visible = _statValue[r].Visible = _statBonus[r].Visible = r == Hp;
            _state.Visible = false;
            Rebuilds++;
        }
        SetStat(Hp, def.Hp, 0f, hpNow: b.Hp[slot]);
    }

    // Rewrites a stat row only when its value or bonus changed; hp rows show "now / max".
    private void SetStat(int row, float value, float bonus, int hpNow = -1)
    {
        bool hpChanged = row == Hp && hpNow != _hpNow;
        if (value != _value[row] || hpChanged)
        {
            _value[row] = value;
            if (row == Hp)
            {
                _hpNow = hpNow;
                _statValue[row].Text = string.Create(CultureInfo.InvariantCulture, $"{hpNow} / {value:0.#}");
            }
            else _statValue[row].Text = value.ToString("0.#", CultureInfo.InvariantCulture);
            Rebuilds++;
        }
        if (bonus != _bonus[row])
        {
            _bonus[row] = bonus;
            _statBonus[row].Visible = bonus != 0f;
            if (bonus != 0f) _statBonus[row].Text = bonus.ToString("+0.#;-0.#", CultureInfo.InvariantCulture);
            Rebuilds++;
        }
    }

    private void ShowGrid(World world, int live)
    {
        SetKind(Kind.Units);
        UnitStore u = world.Units;
        int active = _sel.Subgroups.ActiveType;
        int k = 0;
        foreach (EntityHandle h in _sel.Selection.Items)
        {
            if (k >= PortraitGrid.MaxPortraits) break;
            if (!u.Alive[h.Index] || u.Generation[h.Index] != h.Generation) continue;
            int type = u.TypeId[h.Index];
            if (h != _cellUnit[k] || type != _cellType[k] || !_cells[k].Visible)
            {
                _cellUnit[k] = h;
                _cellType[k] = type;
                _cellFill[k].Color = _typeColors[type];
                _cellInitial[k].Text = _initials[type];
                _cells[k].Visible = true;
            }
            bool on = type == active;
            if (on != _cellFrame[k].Visible) _cellFrame[k].Visible = on;
            k++;
        }
        for (int j = k; j < PortraitGrid.MaxPortraits; j++)
            if (_cells[j].Visible) _cells[j].Visible = false;
        _shownCells = k;
        int over = PortraitGrid.Overflow(live);
        if (over != _shownOverflow)
        {
            _shownOverflow = over;
            _overflow.Visible = over > 0;
            if (over > 0) _overflow.Text = string.Create(CultureInfo.InvariantCulture, $"+{over}");
            Rebuilds++;
        }
    }

    /// <summary>What a click on grid cell <paramref name="k"/> does: selects that unit alone.</summary>
    public void CellPressed(int k)
    {
        if ((uint)k >= (uint)_shownCells) return;
        _sel.SelectOnly(_cellUnit[k]);
    }

    private static Label Text(string name, Vector2 position, Vector2 size, int fontSize)
    {
        var l = new Label { Name = name, Position = position, Size = size, MouseFilter = MouseFilterEnum.Ignore, ClipText = true };
        l.AddThemeFontSizeOverride("font_size", fontSize);
        l.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
        l.AddThemeConstantOverride("outline_size", 3);
        return l;
    }
}
