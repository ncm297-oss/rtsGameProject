using System;
using Godot;
using Rts.Sim;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>The attack alerts on screen (M6-V12, docs/02 "HUD layout" and "Minimap"): text, a minimap ping and a sound; Space jumps the camera to the last one.</summary>
/// <remarks>
/// Each frame <see cref="AttackAlerts"/> reads the local player's units and buildings (hp fell, no alert in the last 10 s
/// within 30 m). For each new alert: a ping on the minimap (<see cref="Minimap.Ping"/>, 3 s); if the alert's position is off
/// screen (<see cref="ScreenCheck"/>), the text for <see cref="TextSeconds"/> at the top centre (<c>ui.json</c>
/// <c>alert.base_under_attack</c> for a building, else <c>alert.units_under_attack</c>; under the replay mark); and the
/// <see cref="SfxEvent.Alert"/> sound, once a frame. Space (a fixed key, <see cref="KeyMap.Reserved"/>) centres the camera on
/// the newest alert. A replay shows the same alerts for its first seat (the local player). The alert bookkeeping is view
/// time and positions; nothing here writes the sim.
/// </remarks>
public partial class AlertHud : Node
{
    /// <summary>How long an off-screen alert's text stays up, in seconds.</summary>
    public const float TextSeconds = 4f;

    /// <summary>The fixed key that jumps the camera to the last alert.</summary>
    public const Key JumpKey = Key.Space;

    private SimRunner? _runner;
    private RtsCamera? _camera;
    private Sfx? _sfx;
    private Minimap? _minimap;
    private UiText? _ui;
    private Label? _label;
    private int _player;
    private System.Numerics.Vector2[] _buildingCenter = Array.Empty<System.Numerics.Vector2>();
    private float _textLeft;

    /// <summary>The alert bookkeeping (read-only for tests); sized at <see cref="Init"/>.</summary>
    public AttackAlerts Alerts { get; private set; } = new(0, 0);

    /// <summary>The alert text label (null without a HUD).</summary>
    public Label? TextLabel => _label;

    /// <summary>True while an alert's text is up.</summary>
    public bool TextShown => _label != null && _label.Visible;

    /// <summary>Times an alert's text was put up (one per frame with a new off-screen alert).</summary>
    public int TextsShown { get; private set; }

    /// <summary>Pings sent to the minimap (one per alert while there is a minimap).</summary>
    public int Pings { get; private set; }

    /// <summary>Alerts raised on screen (ping and sound, no text).</summary>
    public int OnScreenAlerts { get; private set; }

    /// <summary>Alerts raised off screen (text, ping and sound).</summary>
    public int OffScreenAlerts { get; private set; }

    /// <summary>Times the camera jumped to an alert (<see cref="JumpToLast"/>).</summary>
    public int Jumps { get; private set; }

    /// <summary>Connects the alerts to the match: the runner's sim, the camera, the effects, the HUD's minimap and layer (null under <c>--no-hud</c>), the text, and whose alerts.</summary>
    public void Init(SimRunner runner, RtsCamera camera, Sfx sfx, Minimap? minimap, CanvasLayer? hud, UiText? ui, int player)
    {
        _runner = runner;
        _camera = camera;
        _sfx = sfx;
        _minimap = minimap;
        _ui = ui;
        _player = player;
        World w = runner.Simulation!.World;
        Alerts = new AttackAlerts(w.Units.Capacity, w.Buildings.Capacity);
        _buildingCenter = new System.Numerics.Vector2[w.Buildings.Capacity];
        if (hud != null)
        {
            _label = MakeLabel();
            hud.AddChild(_label);
        }
    }

    public override void _Process(double delta)
    {
        if (_runner?.Simulation is not Simulation sim) return;
        Step(sim.World, (float)delta);
    }

    /// <summary>One frame of <paramref name="dt"/> seconds: new alerts ping, sound and (off screen) put the text up; the text counts down. Called by <c>_Process</c>; tests may call it.</summary>
    public void Step(World w, float dt)
    {
        if (_textLeft > 0f)
        {
            _textLeft -= Math.Max(0f, dt);
            if (_textLeft <= 0f && _label != null) _label.Visible = false;
        }
        UnitStore u = w.Units;
        BuildingStore b = w.Buildings;
        for (int i = 0; i < b.Capacity; i++)
            if (b.Alive[i] && b.Owner[i] == _player && (uint)b.TypeId[i] < (uint)w.Data.Buildings.Length)
                _buildingCenter[i] = StartBase.FootprintCenter(w.NavGrid, w.Data.Buildings[b.TypeId[i]], b.Cell[i]);
        int raised = Alerts.Update(_player, dt, u.Alive, u.Generation, u.Hp, u.Owner, u.Position, b.Alive, b.Generation, b.Hp, b.Owner, _buildingCenter);
        if (raised == 0) return;
        bool offScreen = false, offBase = false;
        for (int k = 0; k < raised; k++)
        {
            int a = Alerts.New(k);
            System.Numerics.Vector2 at = Alerts.Position(a);
            if (_minimap != null)
            {
                _minimap.Ping(at);
                Pings++;
            }
            if (_camera != null && ScreenCheck.OnScreen(_camera, w.Heightmap, at)) OnScreenAlerts++;
            else
            {
                OffScreenAlerts++;
                offScreen = true;
                offBase |= Alerts.Kind(a) == AttackAlertKind.Base;
            }
        }
        if (offScreen && _label != null && _ui != null)
        {
            _label.Text = _ui.Alert(offBase ? AlertText.BaseUnderAttack : AlertText.UnitsUnderAttack);
            _label.Visible = true;
            _textLeft = TextSeconds;
            TextsShown++;
        }
        _sfx?.Play(SfxEvent.Alert);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false } key && (key.PhysicalKeycode == JumpKey || key.Keycode == JumpKey) && JumpToLast())
            GetViewport()?.SetInputAsHandled();
    }

    /// <summary>Centres the camera on the newest alert's position; false (nothing moves) before any alert.</summary>
    public bool JumpToLast()
    {
        if (!Alerts.HasLast || _camera == null) return false;
        System.Numerics.Vector2 at = Alerts.Position(Alerts.LastIndex);
        _camera.SetFocus(at.X, at.Y);
        Jumps++;
        return true;
    }

    // Top centre under the replay mark (8-40 px), clear of the resource bar (top right) and the F12 label (top left).
    private static Label MakeLabel()
    {
        var label = new Label { Name = "AlertText", Visible = false, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", 24);
        label.AddThemeColorOverride("font_color", new Color(1f, 0.55f, 0.42f));
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 5);
        label.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        label.OffsetLeft = -260f;
        label.OffsetRight = 260f;
        label.OffsetTop = 46f;
        label.OffsetBottom = 80f;
        return label;
    }
}
