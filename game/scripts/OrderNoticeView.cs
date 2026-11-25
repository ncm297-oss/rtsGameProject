using System;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Orders;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>
/// Tells the local player when a queued order of theirs was dropped (M6-V27, Request 58's view half; docs/03 "Implementation
/// (M6-V27)"): the Refused sound and a short line over the unit.
/// </summary>
/// <remarks>
/// After every tick the runner runs (<see cref="SimRunner.Ticked"/>, so a frame of several ticks at a fast game speed misses
/// none) it reads that tick's <c>World.DroppedOrders</c> through <see cref="OrderNotices.Collect"/> for <see cref="Player"/>:
/// a Build refused at its turn shows the <c>ui.json</c> <c>placement</c> text of its error (<c>cannot_afford</c>, <c>blocked</c>, ...), an
/// order a full queue refused shows <c>orders.queue_full</c>. Every other kind shows nothing yet, nor does another player's
/// unit (enemy or ally), a unit dead when read or whose slot holds another unit (BUG-1662), or anything in replay playback
/// (<see cref="Enabled"/> false). One <see cref="SfxEvent.Refused"/> a tick however many units, one line a unit (a second
/// refusal restarts it), the line lasting <see cref="NoticeLines.LifetimeSeconds"/> of frame time while the game runs (a pause
/// holds it) and drawn like the lost-cast words (<see cref="AbilityViews"/>): a billboard over the unit's overlay line, sized by
/// the zoom so it reads the same at every zoom. The timer is presentation only. A steady frame allocates nothing: a label's text,
/// colour and size are written only when they change.
/// </remarks>
public partial class OrderNoticeView : Node3D
{
    /// <summary>The words' colour: the placement ghost's refusal colour (<see cref="BuildGhost"/>), as the lost-cast words.</summary>
    public static readonly Color TextColor = new(1f, 0.62f, 0.58f);

    /// <summary>Notices read at most a tick (own units refused in one tick past this are not shown; far above a real tick's).</summary>
    public const int ReadCapacity = 256;

    private SimRunner? _runner;
    private NoticeLines _lines = new();
    private OrderNotice[] _read = new OrderNotice[ReadCapacity];
    private Label3D[] _labels = Array.Empty<Label3D>();
    private string[] _labelText = Array.Empty<string>();
    private float[] _labelPixel = Array.Empty<float>();
    private float[] _labelAlpha = Array.Empty<float>();
    private string[] _placementText = Array.Empty<string>();
    private string _queueFullText = "";
    private float[] _overlayLine = Array.Empty<float>();
    private float _barScale = 1f;

    /// <summary>The player whose units' notices show (the local player).</summary>
    public int Player { get; private set; } = SelectionController.LocalPlayer;

    /// <summary>False in replay playback: nothing is read, shown or played.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Where the Refused sound goes; null plays none.</summary>
    public Sfx? Sound { get; set; }

    /// <summary>The camera whose zoom sizes the words; null draws them at the default zoom's size.</summary>
    public RtsCamera? Camera { get; set; }

    /// <summary>The lines on show (read-only for tests).</summary>
    public NoticeLines Lines => _lines;

    /// <summary>Ticks that cued the Refused sound (one a tick with at least one own live notice).</summary>
    public int SoundsCued { get; private set; }

    /// <summary>Own live notices read so far (each started or restarted a line).</summary>
    public long NoticesShown { get; private set; }

    /// <summary>Notices read but not shown because the unit was dead when read or its slot held another unit.</summary>
    public long NoticesDead { get; private set; }

    /// <summary>Lines drawn by the last frame.</summary>
    public int ShownLines { get; private set; }

    /// <summary>The label node of line slot <paramref name="slot"/> (tests read its text, position and visibility).</summary>
    public Label3D Label(int slot) => _labels[slot];

    /// <summary>
    /// Connects the notices to the match: the runner (whose ticks are read), the data's unit types and their overlay lines
    /// (<see cref="UnitViews.OverlayLines"/>; null uses each type's capsule top), the texts (<paramref name="ui"/>; null shows no
    /// words, the sound still plays), the player, and whether anything shows (false in replay playback).
    /// </summary>
    public void Init(SimRunner runner, GameData data, float[]? overlayLines, UiText? ui, int player, bool enabled)
    {
        if (_runner != null) _runner.Ticked -= OnTicked;
        _runner = runner;
        _runner.Ticked += OnTicked;
        Player = player;
        Enabled = enabled;
        ProcessPriority = 1; // after the runner's ticks and the camera's pan of this frame (as BuildGhost)
        _overlayLine = new float[data.Units.Length];
        for (int t = 0; t < _overlayLine.Length; t++)
            _overlayLine[t] = overlayLines != null && t < overlayLines.Length ? overlayLines[t] : UnitViews.DrawnHeight(data.Units[t]);
        _placementText = new string[Enum.GetValues<PlacementError>().Length];
        for (int k = 0; k < _placementText.Length; k++) _placementText[k] = ui?.PlacementText((PlacementError)k) ?? "";
        _queueFullText = ui?.Orders(OrdersText.QueueFull) ?? "";
        _lines = new NoticeLines();
        foreach (Label3D old in _labels) old.QueueFree();
        _labels = new Label3D[_lines.Capacity];
        _labelText = new string[_lines.Capacity];
        _labelPixel = new float[_lines.Capacity];
        _labelAlpha = new float[_lines.Capacity];
        for (int i = 0; i < _labels.Length; i++)
        {
            _labels[i] = new Label3D
            {
                Name = $"OrderNotice{i}", Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, FontSize = BuildGhost.ReasonFontSize,
                OutlineSize = 12, Modulate = TextColor, Text = "", Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            _labelText[i] = "";
            _labelAlpha[i] = 1f;
            AddChild(_labels[i]);
        }
        SoundsCued = 0;
        NoticesShown = 0;
        NoticesDead = 0;
        ShownLines = 0;
    }

    public override void _ExitTree()
    {
        if (_runner != null) _runner.Ticked -= OnTicked;
    }

    private void OnTicked(Simulation sim) => CollectTick(sim.World);

    /// <summary>Reads the last tick's dropped orders (<see cref="Collect"/>); does nothing while not <see cref="Enabled"/>.</summary>
    public int CollectTick(World world)
    {
        UnitStore u = world.Units;
        return Collect(world.DroppedOrders, u.Alive, u.Generation);
    }

    /// <summary>
    /// Shows a line for every live notice of <see cref="Player"/> in <paramref name="dropped"/> (one tick's list) and cues the
    /// Refused sound once when there was any; returns the lines started or restarted. Nothing while not <see cref="Enabled"/>.
    /// </summary>
    public int Collect(ReadOnlySpan<DroppedOrder> dropped, ReadOnlySpan<bool> alive, ReadOnlySpan<int> generation)
    {
        if (!Enabled || dropped.IsEmpty) return 0;
        int n = OrderNotices.Collect(dropped, Player, alive, generation, _read);
        int shown = 0;
        for (int k = 0; k < n; k++)
        {
            OrderNotice e = _read[k];
            if (!e.Alive)
            {
                NoticesDead++;
                continue;
            }
            _lines.Show(e.Unit, e.Placement, e.QueueFull);
            shown++;
        }
        if (shown == 0) return 0;
        NoticesShown += shown;
        SoundsCued++;
        Sound?.Play(SfxEvent.Refused);
        return shown;
    }

    /// <summary>The words line slot <paramref name="slot"/> shows (<c>ui.json</c>): its Build's placement text, or <c>orders.queue_full</c>.</summary>
    public string TextOf(int slot)
    {
        if (_lines.QueueFull[slot]) return _queueFullText;
        int p = (int)_lines.Placement[slot];
        return (uint)p < (uint)_placementText.Length ? _placementText[p] : "";
    }

    public override void _Process(double delta)
    {
        if (_runner?.Simulation is Simulation sim) Sync(sim.World, (float)_runner.Alpha, (float)delta);
    }

    /// <summary>One frame: ages the lines by <paramref name="seconds"/> and draws each live one over its unit at render <paramref name="alpha"/>. A line whose unit is dead or whose slot holds another unit draws nothing and expires on time.</summary>
    public void Sync(World world, float alpha, float seconds)
    {
        _lines.Advance(seconds);
        if (Camera != null) _barScale = Math.Max(1f, Camera.Zoom / CombatViews.BarFullZoom);
        float pixel = BuildGhost.ReasonPixelPerZoom * (Camera?.Zoom ?? CameraLimits.DefaultZoom);
        UnitStore u = world.Units;
        ReadOnlySpan<bool> active = _lines.Active;
        int shown = 0;
        for (int i = 0; i < active.Length; i++)
        {
            Label3D label = _labels[i];
            EntityHandle h = _lines.Unit[i];
            string text = active[i] ? TextOf(i) : "";
            if (text.Length == 0 || !OrderNotices.IsLive(h, u.Alive, u.Generation))
            {
                if (label.Visible) label.Visible = false;
                continue;
            }
            if (!ReferenceEquals(_labelText[i], text))
            {
                label.Text = text;
                _labelText[i] = text;
            }
            if (_labelPixel[i] != pixel)
            {
                label.PixelSize = pixel;
                _labelPixel[i] = pixel;
            }
            float opacity = _lines.Opacity(i);
            if (_labelAlpha[i] != opacity)
            {
                label.Modulate = new Color(TextColor.R, TextColor.G, TextColor.B, opacity);
                _labelAlpha[i] = opacity;
            }
            int type = u.TypeId[h.Index];
            float line = (uint)type < (uint)_overlayLine.Length ? _overlayLine[type] : 2f;
            label.Position = UnitViews.GroundPoint(world, h.Index, alpha)
                + new Vector3(0f, line + (CombatViews.BarGap + AbilityViews.BarAbove + AbilityViews.CueTextAbove) * _barScale, 0f);
            if (!label.Visible) label.Visible = true;
            shown++;
        }
        ShownLines = shown;
    }
}
