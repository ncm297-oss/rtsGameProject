using System;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>The production queue strip above the command card (M3-V3): the selected own finished building's queue, the head's progress, a click cancels an item.</summary>
/// <remarks>
/// Up to <see cref="QueueStrip.MaxItems"/> squares, head first: a unit is a square in its type's colour
/// (<see cref="SelectionPanel.TypeColor"/>) with its initial, a tech a slate square with its <c>displayName</c>. The head
/// carries a bar of <see cref="QueueStrip.HeadFill"/> (<c>Progress / ItemTicks(slot, 0)</c>). A click on item k is
/// <see cref="SelectionController.CancelQueueItem"/>(k): <c>Command.CancelTrain(player, building, k)</c>, a full refund.
/// Polled every frame from <see cref="BuildingStore.QueueCount"/>, <see cref="BuildingStore.QueueTypeAt"/> and
/// <see cref="BuildingStore.QueueIsTechAt"/>; an item's text and colour are written only when it changes and the bar's
/// width only when the fill does, so a steady frame allocates nothing. Holds view state only.
/// </remarks>
public partial class ProductionQueueStrip : Control
{
    /// <summary>Item size and gap in pixels.</summary>
    public const float ItemWidth = 64f, ItemHeight = 44f, Gap = 4f;

    /// <summary>Height of the head's progress bar in pixels.</summary>
    public const float BarHeight = 5f;

    private static readonly Color TechColor = new(0.30f, 0.34f, 0.42f);
    private static readonly Color BarColor = new(0.95f, 0.85f, 0.35f);

    private SimRunner? _runner;
    private SelectionController _sel = null!;
    private GameData _data = null!;
    private Color[] _typeColors = Array.Empty<Color>();
    private string[] _initials = Array.Empty<string>();

    private readonly Button[] _items = new Button[QueueStrip.MaxItems];
    private readonly ColorRect[] _fills = new ColorRect[QueueStrip.MaxItems];
    private readonly Label[] _labels = new Label[QueueStrip.MaxItems];
    private ColorRect _bar = null!;

    private readonly int[] _shownType = new int[QueueStrip.MaxItems];
    private readonly bool[] _shownTech = new bool[QueueStrip.MaxItems];
    private int _shownCount = -1;
    private float _shownFill = -1f;

    /// <summary>Items on show.</summary>
    public int ShownCount => Math.Max(_shownCount, 0);

    /// <summary>The unit type or tech id item <paramref name="k"/> shows (-1 past <see cref="ShownCount"/>).</summary>
    public int ShownType(int k) => k < ShownCount ? _shownType[k] : -1;

    /// <summary>True when item <paramref name="k"/> shows a tech.</summary>
    public bool ShownIsTech(int k) => k < ShownCount && _shownTech[k];

    /// <summary>The head's progress share on show (0-1).</summary>
    public float ShownFill => Math.Max(_shownFill, 0f);

    /// <summary>The head's progress bar node.</summary>
    public ColorRect HeadBar => _bar;

    /// <summary>The button of item <paramref name="k"/>.</summary>
    public Button ItemButton(int k) => _items[k];

    /// <summary>The label of item <paramref name="k"/> (a unit's initial or a tech's name).</summary>
    public Label ItemLabel(int k) => _labels[k];

    /// <summary>Times an item or the bar was rewritten.</summary>
    public int Updates { get; private set; }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        for (int k = 0; k < QueueStrip.MaxItems; k++)
        {
            int index = k;
            var b = new Button
            {
                Name = $"Item{k}", Position = new Vector2(QueueStrip.ItemX(k, ItemWidth, Gap), 0f), Size = new Vector2(ItemWidth, ItemHeight),
                FocusMode = FocusModeEnum.None, Flat = true, Visible = false,
            };
            var fill = new ColorRect { Name = "Fill", Position = new Vector2(1f, 1f), Size = new Vector2(ItemWidth - 2f, ItemHeight - 2f), MouseFilter = MouseFilterEnum.Ignore };
            var label = new Label
            {
                Name = "Label", Size = new Vector2(ItemWidth, ItemHeight - BarHeight), MouseFilter = MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart, ClipText = true,
            };
            label.AddThemeFontSizeOverride("font_size", 11);
            label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
            label.AddThemeConstantOverride("outline_size", 3);
            b.AddChild(fill);
            b.AddChild(label);
            b.Pressed += () => ItemPressed(index);
            AddChild(b);
            _items[k] = b;
            _fills[k] = fill;
            _labels[k] = label;
            _shownType[k] = -1;
        }
        _bar = new ColorRect
        {
            Name = "HeadBar", Color = BarColor, Position = new Vector2(1f, ItemHeight - BarHeight - 1f), Size = new Vector2(0f, BarHeight),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _items[0].AddChild(_bar);
    }

    /// <summary>Connects the strip to the match; call once after the sim exists.</summary>
    public void Init(SimRunner runner, SelectionController selection)
    {
        _runner = runner;
        _sel = selection;
        _data = runner.Simulation!.World.Data;
        _typeColors = new Color[_data.Units.Length];
        _initials = new string[_data.Units.Length];
        for (int t = 0; t < _data.Units.Length; t++)
        {
            _typeColors[t] = SelectionPanel.TypeColor(t);
            string n = _data.Units[t].DisplayName;
            _initials[t] = n.Length > 0 ? n.Substring(0, 1).ToUpperInvariant() : "";
        }
        _shownCount = -1;
        Sync();
    }

    public override void _Process(double delta) => Sync();

    /// <summary>Shows the selected own finished building's queue (nothing without one). Allocation-free.</summary>
    public void Sync()
    {
        if (_runner?.Simulation is not Simulation sim) return;
        BuildingStore b = sim.World.Buildings;
        int slot = _sel.SelectedFinishedBuilding;
        int count = slot >= 0 ? QueueStrip.Count(b, slot) : 0;
        for (int k = 0; k < QueueStrip.MaxItems; k++)
        {
            if (k >= count)
            {
                if (_items[k].Visible) _items[k].Visible = false;
                _shownType[k] = -1;
                continue;
            }
            int type = b.QueueTypeAt(slot, k);
            bool tech = b.QueueIsTechAt(slot, k);
            if (type == _shownType[k] && tech == _shownTech[k] && _items[k].Visible) continue;
            _shownType[k] = type;
            _shownTech[k] = tech;
            if (tech)
            {
                _fills[k].Color = TechColor;
                _labels[k].Text = (uint)type < (uint)_data.Techs.Length ? _data.Techs[type].DisplayName : "";
                _labels[k].AddThemeFontSizeOverride("font_size", 11);
            }
            else
            {
                bool known = (uint)type < (uint)_typeColors.Length;
                _fills[k].Color = known ? _typeColors[type] : TechColor;
                _labels[k].Text = known ? _initials[type] : "";
                _labels[k].AddThemeFontSizeOverride("font_size", 22);
            }
            _items[k].TooltipText = tech ? _data.Techs[type].DisplayName : (uint)type < (uint)_data.Units.Length ? _data.Units[type].DisplayName : "";
            _items[k].Visible = true;
            Updates++;
        }
        _shownCount = count;
        float fill = slot >= 0 ? QueueStrip.HeadFill(b, slot) : 0f;
        if (fill != _shownFill)
        {
            _shownFill = fill;
            _bar.Size = new Vector2((ItemWidth - 2f) * fill, BarHeight);
            Updates++;
        }
    }

    /// <summary>What a click on item <paramref name="k"/> does: cancels that queue item (a full refund).</summary>
    public void ItemPressed(int k)
    {
        if ((uint)k >= (uint)ShownCount) return;
        _sel.CancelQueueItem(k);
    }
}
