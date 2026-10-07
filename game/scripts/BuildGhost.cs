using System;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>The building placement ghost (M3-V2): a translucent footprint box on the cursor, green where <c>World.CanPlace</c> passes, red with the reason's text where it doesn't.</summary>
/// <remarks>
/// The anchor is <see cref="PlacementGhost.Anchor(NavGrid, BuildingDef, System.Numerics.Vector2)"/> of the ground point under the
/// cursor. <c>CanPlace</c> writes flow-field scratch (docs/03 M3-3), so it is asked only here, from <see cref="_Process"/> on
/// the main thread, after <see cref="SimRunner"/>'s tick step of the frame (this node comes after the runner in Match.tscn, and
/// nodes process in tree order) and never while <see cref="SimRunner.Ticking"/>; at most once per frame
/// (<see cref="CanPlaceCalls"/>), and only when the anchor, the type or the sim tick changed since the last answer (nothing
/// else changes what it says). The box moves only with an answer, so the colour drawn is always <c>CanPlace</c> of the
/// anchor drawn. Meshes (one per building type, made on first use) and the two materials are kept, and the reason label's
/// text is set only when the reason changes, so a steady frame allocates nothing. A click places at the anchor under the
/// click itself (<see cref="ResolveClick"/>, BUG-0109), which may spend the frame's one <c>CanPlace</c> call. Holds no
/// gameplay state: the type and anchor are what is shown; the order is <see cref="SelectionController.OrderBuild"/>.
/// </remarks>
public partial class BuildGhost : Node3D
{
    private static readonly Color GreenColor = new(0.25f, 0.95f, 0.35f, 0.45f);
    private static readonly Color RedColor = new(1f, 0.12f, 0.1f, 0.55f);

    private SimRunner? _runner;
    private RtsCamera _camera = null!;
    private UiText _ui = null!;
    private GameData _data = null!;
    private int _player;
    private int _mainThread;

    private MeshInstance3D _box = null!;
    private Label3D _label = null!;
    private BoxMesh?[] _meshes = Array.Empty<BoxMesh?>();
    private StandardMaterial3D _green = null!, _red = null!;

    // The last CanPlace answer: for which anchor, type and tick, and in which frame it was asked.
    private int _evalAnchor = -1, _evalType = -1, _evalTick = -1;
    private ulong _evalFrame = ulong.MaxValue;
    private string _shownText = "";
    private int _shownGreen = -1; // the box's material: 1 green, 0 red, -1 not set since Begin

    /// <summary>True while the ghost is up (a build menu entry was picked).</summary>
    public bool Active { get; private set; }

    /// <summary>The building type shown, or -1.</summary>
    public int TypeId { get; private set; } = -1;

    /// <summary>The anchor cell the box stands on, or -1 while the cursor is off the map (the box is hidden then).</summary>
    public int Anchor { get; private set; } = -1;

    /// <summary><c>CanPlace</c>'s answer for <see cref="Anchor"/>: true is drawn green.</summary>
    public bool Valid { get; private set; }

    /// <summary>The first rule broken at <see cref="Anchor"/> (<see cref="PlacementError.None"/> when green).</summary>
    public PlacementError Reason { get; private set; }

    /// <summary>The reason text on show (<c>ui.json</c> <c>placement</c>), empty when green.</summary>
    public string ShownText => _shownText;

    /// <summary>True when a click would place: the ghost is up, on the map, and green.</summary>
    public bool CanClick => Active && Visible && Valid && Anchor >= 0;

    /// <summary><c>World.CanPlace</c> calls made by the ghost so far.</summary>
    public int CanPlaceCalls { get; private set; }

    /// <summary>When set, the screen point used instead of the mouse (tests and scripted shots).</summary>
    public Vector2? ScreenOverride { get; set; }

    /// <summary>The box node (tests read its material and position).</summary>
    public MeshInstance3D Box => _box;

    /// <summary>The reason label node.</summary>
    public Label3D ReasonLabel => _label;

    /// <summary>The green material.</summary>
    public StandardMaterial3D GreenMaterial => _green;

    /// <summary>The red material.</summary>
    public StandardMaterial3D RedMaterial => _red;

    public override void _Ready()
    {
        _mainThread = System.Environment.CurrentManagedThreadId;
        _green = Material(GreenColor);
        _red = Material(RedColor);
        _box = new MeshInstance3D { Name = "Box", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _label = new Label3D
        {
            Name = "Reason", Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, FontSize = 48, PixelSize = 0.01f,
            OutlineSize = 12, Modulate = new Color(1f, 0.85f, 0.8f), Position = new Vector3(0f, BuildingViews.BoxHeight + 1.2f, 0f),
        };
        AddChild(_box);
        AddChild(_label);
        Visible = false;
    }

    /// <summary>Connects the ghost to the match; call once after the sim exists.</summary>
    public void Init(SimRunner runner, RtsCamera camera, UiText ui, int player)
    {
        _runner = runner;
        _camera = camera;
        _ui = ui;
        _data = runner.Simulation!.World.Data;
        _player = player;
        _meshes = new BoxMesh?[_data.Buildings.Length];
    }

    /// <summary>Shows the ghost for building type <paramref name="typeId"/> (it appears on the next <see cref="Sync"/>).</summary>
    public void Begin(int typeId)
    {
        if ((uint)typeId >= (uint)_meshes.Length) return;
        BuildingDef def = _data.Buildings[typeId];
        _meshes[typeId] ??= new BoxMesh { Size = new Vector3(def.FootprintWidth * MapConstants.CellSize, BuildingViews.BoxHeight, def.FootprintHeight * MapConstants.CellSize) };
        _box.Mesh = _meshes[typeId];
        _box.Position = new Vector3(0f, BuildingViews.BoxHeight / 2f, 0f);
        Active = true;
        TypeId = typeId;
        Anchor = -1;
        _evalAnchor = _evalType = _evalTick = -1;
        _label.Text = "";
        _label.Visible = false;
        _shownText = "";
        _shownGreen = -1;
        Visible = false;
    }

    /// <summary>Takes the ghost down.</summary>
    public void End()
    {
        Active = false;
        TypeId = -1;
        Anchor = -1;
        Valid = false;
        Visible = false;
    }

    public override void _Process(double delta) => Sync();

    /// <summary>One frame of the ghost: follows the cursor and asks <c>CanPlace</c> when the anchor, type or tick changed (once per frame at most). Allocation-free.</summary>
    public void Sync()
    {
        if (!Active || _runner?.Simulation is not Simulation sim) return;
        World world = sim.World;
        Vector2 screen = ScreenOverride ?? GetViewport().GetMousePosition();
        Vector3 o = _camera.ProjectRayOrigin(screen), d = _camera.ProjectRayNormal(screen);
        if (!GroundPicker.TryPick(world.Heightmap, new(o.X, o.Y, o.Z), new(d.X, d.Y, d.Z), out System.Numerics.Vector3 hit))
        {
            HideBox();
            return;
        }
        int anchor = PlacementGhost.Anchor(world.NavGrid, _data.Buildings[TypeId], new System.Numerics.Vector2(hit.X, hit.Z));
        if (anchor < 0)
        {
            HideBox();
            return;
        }
        if (anchor != _evalAnchor || TypeId != _evalType || sim.TickNumber != _evalTick)
        {
            ulong frame = Engine.GetProcessFrames();
            // Once per frame, main thread, never inside a tick (docs/03 M3-3): otherwise keep the last answer and box.
            if (frame == _evalFrame || _runner.Ticking || System.Environment.CurrentManagedThreadId != _mainThread) return;
            _evalFrame = frame;
            CanPlaceCalls++;
            bool ok = world.CanPlace(_player, TypeId, anchor, out PlacementError reason);
            _evalAnchor = anchor;
            _evalType = TypeId;
            _evalTick = sim.TickNumber;
            Show(world, anchor, ok, reason);
        }
        else if (!Visible) Show(world, anchor, Valid, Reason);
    }

    /// <summary>
    /// The anchor a left click at <paramref name="screen"/> places at (BUG-0109): recomputed from the click's own position,
    /// since input arrives before this frame's <see cref="Sync"/>. The drawn anchor uses the drawn answer; another anchor is
    /// asked of <c>CanPlace</c> as this frame's one call (and drawn), unless the frame's call is spent, a tick is running
    /// or this is not the main thread: then the click is ignored. True (anchor in <paramref name="anchor"/>) only when that
    /// anchor is green; false off the map, red, or ignored.
    /// </summary>
    public bool ResolveClick(Vector2 screen, out int anchor)
    {
        anchor = -1;
        if (!Active || _runner?.Simulation is not Simulation sim) return false;
        World world = sim.World;
        Vector3 o = _camera.ProjectRayOrigin(screen), d = _camera.ProjectRayNormal(screen);
        if (!GroundPicker.TryPick(world.Heightmap, new(o.X, o.Y, o.Z), new(d.X, d.Y, d.Z), out System.Numerics.Vector3 hit)) return false;
        int at = PlacementGhost.Anchor(world.NavGrid, _data.Buildings[TypeId], new System.Numerics.Vector2(hit.X, hit.Z));
        if (at < 0) return false;
        if (at == Anchor && Visible)
        {
            anchor = at;
            return Valid;
        }
        ulong frame = Engine.GetProcessFrames();
        if (frame == _evalFrame || _runner.Ticking || System.Environment.CurrentManagedThreadId != _mainThread)
        {
            ClicksIgnored++;
            return false;
        }
        _evalFrame = frame;
        CanPlaceCalls++;
        bool ok = world.CanPlace(_player, TypeId, at, out PlacementError reason);
        _evalAnchor = at;
        _evalType = TypeId;
        _evalTick = sim.TickNumber;
        Show(world, at, ok, reason);
        anchor = at;
        return ok;
    }

    /// <summary>Clicks <see cref="ResolveClick"/> ignored because this frame's <c>CanPlace</c> call was already spent (or a tick was running).</summary>
    public int ClicksIgnored { get; private set; }

    private void Show(World world, int anchor, bool ok, PlacementError reason)
    {
        if (anchor != Anchor)
        {
            System.Numerics.Vector2 c = StartBase.FootprintCenter(world.NavGrid, _data.Buildings[TypeId], anchor);
            Position = new Vector3(c.X, TerrainHeight.At(world.Heightmap, c.X, c.Y), c.Y);
            Anchor = anchor;
        }
        // Compared with what was drawn since Begin, not with Valid: a new ghost must not keep the last one's colour.
        int green = ok ? 1 : 0;
        if (green != _shownGreen)
        {
            _box.MaterialOverride = ok ? _green : _red;
            _shownGreen = green;
        }
        Valid = ok;
        Reason = reason;
        string text = ok ? "" : _ui.PlacementText(reason);
        if (!ReferenceEquals(text, _shownText))
        {
            _label.Text = text;
            _label.Visible = text.Length > 0;
            _shownText = text;
        }
        Visible = true;
    }

    private void HideBox()
    {
        if (Visible) Visible = false;
    }

    private static StandardMaterial3D Material(Color c) => new()
    {
        AlbedoColor = c,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };
}
