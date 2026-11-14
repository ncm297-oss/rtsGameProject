using System;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>Unit views: one <see cref="MeshInstance3D"/> per unit slot, placed from the sim every frame, drawing its type's model or its placeholder capsule.</summary>
/// <remarks>
/// Models (M6-V20): with <see cref="UseModels"/> on, a type whose model file loads (<see cref="UnitModels"/>, the path from
/// <c>UnitDef.Model</c>) is drawn as that model: the slot's node keeps its place at the capsule's centre (the cargo marker's
/// frame) and draws no mesh; a child "Model" (the imported scene, feet on the ground, at the type's data <c>scale</c>) holds
/// the skinned mesh, whose <c>team</c> surface is overridden by the owner's team material and which takes the flash and
/// worker-tint overlays, and an <see cref="AnimationPlayer"/> on the shared library playing <see cref="ClipFor"/> the unit's
/// sim state each frame (only a change of clip touches it). A slot reused by a type of another model or by a capsule type
/// frees its model; a hidden slot's player stops. Every other type is the capsule below.
/// A slot's node is created the first time the slot holds a live unit and hidden while it is dead,
/// so a unit respawned into the slot reuses it. Meshes (one per unit type, radius from data, M7-V1: height times the
/// type's data <c>scale</c>, M7-V3c: width the collision radius alone, <see cref="DrawnRadius(UnitDef)"/> / <see cref="DrawnHeight"/>) and
/// materials (one per faction, its <c>PrimaryColor</c>; M6-V3: with the match's per-player colours, one per player, set
/// on a slot's node as its material override when the slot's owner changes) are all made in <see cref="Bind"/>. Views
/// hold no gameplay state; they only read the unit store.
/// Worker feedback (M3-V1): a standing worker's <see cref="UnitState"/> (<c>Gathering</c>, <c>Returning</c>,
/// <c>Building</c>) tints its body through a shared overlay material, and while <c>Cargo</c> &gt; 0 a small marker
/// floats above it, gold-coloured or wood-brown by <c>CargoKind</c>. A slot's marker node is made the first time it
/// carries and then reused; overlay and marker are only touched when what they show changes, so a steady frame
/// allocates nothing.
/// Hit flash (M4-V1): a unit whose hp fell since the last frame (<see cref="HitFlash"/>, comparing the store's <c>Hp</c>
/// with what the last frame saw; no sim event) shows a white overlay for <see cref="HitFlash.DefaultSeconds"/> of view
/// time, which a new hit starts again; it takes the place of the worker tint while lit. A unit first seen below its maximum
/// hp (hit between two frames, before its view existed) flashes once too (M4-V2, BUG-0160); the maximum is
/// <c>World.MaxHp</c>, hp techs included, copied per slot each frame (<see cref="FillMaxHp"/>; M4-V7, BUG-0450). A dead unit's node is hidden on the
/// first frame after its death (the slot reads not alive), so its view goes the frame the death is shown.
/// Fog (M4-V4): with <see cref="Fog"/> set, a unit the local player doesn't see (<c>Fog.CanSeeUnit</c> false, through
/// <see cref="FogView.UnitShown"/>) is hidden like a dead one, and its cargo marker and flash with it. A unit that comes
/// into sight is placed at its interpolated position before it is shown, in the same frame, so it never pops in at a
/// stale spot; its flash timer kept running while it was hidden.
/// </remarks>
public partial class UnitViews : Node3D
{
    /// <summary>Capsule height above twice the radius, in meters.</summary>
    public const float ExtraBodyHeight = 1f;

    /// <summary>Cargo marker edge length, and its gap above the body, in meters.</summary>
    public const float CargoSize = 0.35f, CargoGap = 0.3f;

    /// <summary>
    /// Zoom (camera height, m) up to which the cargo marker keeps <see cref="CargoSize"/>; above it the marker grows in
    /// proportion, so it keeps its 30 m screen size (BUG-0107: at 60 m the 0.35 m cube was 3-4 px). At 60 m it is
    /// 0.7 m; its bottom still clears the body by 0.125 m, so the position needn't change.
    /// </summary>
    public const float CargoFullZoom = 30f;

    /// <summary>The camera whose zoom sizes the cargo markers; null keeps <see cref="CargoSize"/>.</summary>
    public RtsCamera? Camera { get; set; }

    private float _cargoZoom = float.NaN;

    // Placeholder tints until the M6 art pass (M2-1 rule). Cargo colours match the props' gold and trunk.
    private static readonly Color GoldCargoColor = new(0.90f, 0.70f, 0.15f);
    private static readonly Color WoodCargoColor = new(0.45f, 0.28f, 0.12f);
    private static readonly Color GatheringTint = new(0.25f, 0.95f, 0.35f, 0.45f);
    private static readonly Color ReturningTint = new(1.00f, 0.80f, 0.20f, 0.45f);
    private static readonly Color BuildingTint = new(0.30f, 0.65f, 1.00f, 0.45f);
    private static readonly Color FlashTint = new(1.00f, 1.00f, 1.00f, 0.75f);

    /// <summary>The flag that forces the placeholder capsules for every unit type (M6-V20).</summary>
    public const string PlaceholderArtFlag = "--placeholder-art";

    /// <summary>
    /// Whether <see cref="Bind"/> draws a type that has a model file as that model (M6-V20); on unless the command line holds
    /// <see cref="PlaceholderArtFlag"/>. A scene that pins capsule geometry or pixels sets it false before binding.
    /// </summary>
    public static bool UseModels { get; set; } = Array.IndexOf(OS.GetCmdlineUserArgs(), PlaceholderArtFlag) < 0;

    /// <summary>The runner whose sim is shown each frame; null shows nothing (tests call <see cref="Sync"/> directly).</summary>
    public SimRunner? Runner { get; set; }

    /// <summary>The fog whose hide rule applies (M4-V4); null shows every live unit.</summary>
    public FogOfWar? Fog { get; set; }

    /// <summary>Each slot's maximum hp this tick (World.MaxHp), for the hit flash's first-sight rule (M4-V2, M4-V7); made in <see cref="Bind"/> and shared with <see cref="CombatViews.MaxHp"/> by <c>Match</c>, so it is filled once a frame (BUG-0490).</summary>
    public SlotMaxHp MaxHp { get; private set; } = new(0);

    private CapsuleMesh[] _meshes = Array.Empty<CapsuleMesh>();
    private float[] _halfHeight = Array.Empty<float>();
    private MeshInstance3D?[] _views = Array.Empty<MeshInstance3D?>();
    private int[] _viewType = Array.Empty<int>();
    private int[] _viewOwner = Array.Empty<int>();
    private StandardMaterial3D[] _playerMats = Array.Empty<StandardMaterial3D>();
    private bool[] _shown = Array.Empty<bool>();

    // Worker feedback: overlay per UnitState (null for untinted states), marker mesh per ResourceKind.
    private StandardMaterial3D?[] _tints = Array.Empty<StandardMaterial3D?>();
    private BoxMesh[] _cargoMeshes = Array.Empty<BoxMesh>();
    private MeshInstance3D?[] _markers = Array.Empty<MeshInstance3D?>();
    // What each slot shows now: the tinted state (Idle: none), the cargo kind (-1: none).
    private UnitState[] _shownTint = Array.Empty<UnitState>();
    private sbyte[] _shownCargo = Array.Empty<sbyte>();
    private bool[] _shownLit = Array.Empty<bool>();
    private StandardMaterial3D _flashMat = null!;

    // M6-V20: models (null: capsules only), the faction colour index each type's team material falls back to without
    // per-player colours, and per slot its model instance (kept while the slot's type keeps the same model), the mesh the
    // overlays go on, its player and the clip it shows (-1: none).
    private UnitModels? _models;
    private int[] _factionColour = Array.Empty<int>();
    private string[] _modelPath = Array.Empty<string>();
    private float[] _modelScale = Array.Empty<float>();
    private Node3D?[] _model = Array.Empty<Node3D?>();
    private MeshInstance3D?[] _modelMesh = Array.Empty<MeshInstance3D?>();
    private AnimationPlayer?[] _anim = Array.Empty<AnimationPlayer?>();
    private int[] _shownClip = Array.Empty<int>();

    /// <summary>The models of the last <see cref="Bind"/>, or null when <see cref="UseModels"/> was off.</summary>
    public UnitModels? Models => _models;

    /// <summary>True when type <paramref name="type"/> is drawn as a model (M6-V20), false for a capsule.</summary>
    public bool DrawsModel(int type) => _models?.Has(type) == true;

    /// <summary>Slot <paramref name="slot"/>'s model instance (M6-V20), or null while it shows a capsule.</summary>
    public Node3D? ModelOf(int slot) => (uint)slot < (uint)_model.Length ? _model[slot] : null;

    /// <summary>Slot <paramref name="slot"/>'s model mesh, the node the team material and the overlays go on, or null.</summary>
    public MeshInstance3D? ModelMeshOf(int slot) => (uint)slot < (uint)_modelMesh.Length ? _modelMesh[slot] : null;

    /// <summary>Slot <paramref name="slot"/>'s animation player, or null while it shows a capsule.</summary>
    public AnimationPlayer? AnimatorOf(int slot) => (uint)slot < (uint)_anim.Length ? _anim[slot] : null;

    /// <summary>The clip slot <paramref name="slot"/> plays (an index into <see cref="UnitModels.ClipNames"/>), or -1.</summary>
    public int ShownClip(int slot) => (uint)slot < (uint)_shownClip.Length ? _shownClip[slot] : -1;

    /// <summary>Hit-flash bookkeeping (M4-V1): which slots are lit and for how long.</summary>
    public HitFlash Flash { get; private set; } = new(0);

    /// <summary>The overlay material of a lit (just hit) unit.</summary>
    public StandardMaterial3D FlashMaterial => _flashMat;

    /// <summary>True while slot <paramref name="slot"/>'s view is drawn (a live unit the fog doesn't hide).</summary>
    public bool IsShown(int slot) => (uint)slot < (uint)_shown.Length && _shown[slot];

    /// <summary>True while slot <paramref name="slot"/>'s view shows the hit flash.</summary>
    public bool ShownLit(int slot) => (uint)slot < (uint)_shownLit.Length && _shownLit[slot];

    /// <summary>View nodes created so far (one per slot ever used).</summary>
    public int NodeCount { get; private set; }

    /// <summary>Meshes created (one per unit type).</summary>
    public int MeshCount => _meshes.Length;

    /// <summary>Materials created (one per faction).</summary>
    public int MaterialCount { get; private set; }

    /// <summary>Per-player body materials (M6-V3), one per <c>playerRgb</c> entry given to <see cref="Bind"/>; 0 without.</summary>
    public int PlayerMaterialCount => _playerMats.Length;

    /// <summary>Player <paramref name="player"/>'s body material (M6-V3), or null without per-player colours.</summary>
    public StandardMaterial3D? PlayerMaterial(int player) => (uint)player < (uint)_playerMats.Length ? _playerMats[player] : null;

    /// <summary>Cargo marker nodes created so far (one per slot that ever carried).</summary>
    public int MarkerCount { get; private set; }

    /// <summary>Creates the shared meshes and materials and sizes the slot pool; call once before the first <see cref="Sync"/>.</summary>
    /// <param name="playerRgb">Per player, its colour (0xRRGGBB, M6-V3: the match's <c>PlayerRgb</c>); null draws each unit in its type's faction's colour.</param>
    public void Bind(GameData data, int unitCapacity, uint[]? playerRgb = null)
    {
        _playerMats = new StandardMaterial3D[playerRgb?.Length ?? 0];
        for (int p = 0; p < _playerMats.Length; p++)
            _playerMats[p] = new StandardMaterial3D { AlbedoColor = ColorFromRgb(playerRgb![p]), Roughness = 0.8f };

        var materials = new StandardMaterial3D[data.Factions.Length];
        for (int f = 0; f < materials.Length; f++)
            materials[f] = new StandardMaterial3D { AlbedoColor = ColorFromRgb(data.Factions[f].PrimaryColor), Roughness = 0.8f };
        MaterialCount = materials.Length;

        _meshes = new CapsuleMesh[data.Units.Length];
        _halfHeight = new float[data.Units.Length];
        for (int t = 0; t < _meshes.Length; t++)
        {
            UnitDef def = data.Units[t];
            // M7-V1: the data scale is applied here, once per type, so the per-frame path is unchanged.
            float height = DrawnHeight(def);
            // Low-poly on purpose: 2,000 of these are on screen at once (M2-7 perf budget).
            _meshes[t] = new CapsuleMesh { Radius = DrawnRadius(def), Height = height, RadialSegments = 12, Rings = 3, Material = materials[def.Faction] };
            _halfHeight[t] = height / 2;
        }

        MaxHp = new SlotMaxHp(unitCapacity);
        _views = new MeshInstance3D?[unitCapacity];
        _viewType = new int[unitCapacity];
        Array.Fill(_viewType, -1);
        _viewOwner = new int[unitCapacity];
        Array.Fill(_viewOwner, -1);
        _shown = new bool[unitCapacity];

        _tints = new StandardMaterial3D?[(int)UnitState.Building + 1];
        _tints[(int)UnitState.Gathering] = Tint(GatheringTint);
        _tints[(int)UnitState.Returning] = Tint(ReturningTint);
        _tints[(int)UnitState.Building] = Tint(BuildingTint);
        _cargoMeshes = new BoxMesh[(int)ResourceKind.Wood + 1];
        _cargoMeshes[(int)ResourceKind.Gold] = CargoMesh(GoldCargoColor);
        _cargoMeshes[(int)ResourceKind.Wood] = CargoMesh(WoodCargoColor);
        _markers = new MeshInstance3D?[unitCapacity];
        _shownTint = new UnitState[unitCapacity];
        _shownCargo = new sbyte[unitCapacity];
        Array.Fill(_shownCargo, (sbyte)-1);
        _shownLit = new bool[unitCapacity];
        _flashMat = Tint(FlashTint);
        Flash = new HitFlash(unitCapacity);

        // M6-V20: the team materials come in the per-player colours when given, else one per faction (the capsule's rule).
        _models = null;
        if (UseModels)
        {
            uint[] colours = playerRgb ?? new uint[data.Factions.Length];
            if (playerRgb == null)
                for (int f = 0; f < colours.Length; f++) colours[f] = data.Factions[f].PrimaryColor;
            _models = new UnitModels(data, colours);
            if (_models.ModelTypes == 0) _models = null;
        }
        _factionColour = new int[data.Units.Length];
        _modelPath = new string[data.Units.Length];
        _modelScale = new float[data.Units.Length];
        for (int t = 0; t < _factionColour.Length; t++)
        {
            _factionColour[t] = data.Units[t].Faction;
            _modelPath[t] = data.Units[t].Model;
            _modelScale[t] = data.Units[t].Scale;
        }
        _model = new Node3D?[unitCapacity];
        _modelMesh = new MeshInstance3D?[unitCapacity];
        _anim = new AnimationPlayer?[unitCapacity];
        _shownClip = new int[unitCapacity];
        Array.Fill(_shownClip, -1);
    }

    /// <summary>The state whose tint slot <paramref name="slot"/> shows (<see cref="UnitState.Idle"/>: none).</summary>
    public UnitState ShownTint(int slot) => _shownTint[slot];

    /// <summary>The cargo kind slot <paramref name="slot"/>'s marker shows, or -1 for no marker.</summary>
    public int ShownCargo(int slot) => _shownCargo[slot];

    /// <summary>The cargo marker node of slot <paramref name="slot"/>, or null if it never carried.</summary>
    public MeshInstance3D? MarkerOf(int slot) => (uint)slot < (uint)_markers.Length ? _markers[slot] : null;

    /// <summary>The overlay material that tints a unit in <paramref name="state"/>, or null for an untinted state.</summary>
    public StandardMaterial3D? TintOf(UnitState state) => (uint)state < (uint)_tints.Length ? _tints[(int)state] : null;

    /// <summary>The marker mesh for cargo of <paramref name="kind"/>.</summary>
    public BoxMesh CargoMeshOf(ResourceKind kind) => _cargoMeshes[(int)kind];

    /// <summary>The state a unit's tint shows: its own if <see cref="TintOf"/> has a material for it, else <see cref="UnitState.Idle"/> (none).</summary>
    public UnitState TintStateFor(UnitState state) => TintOf(state) != null ? state : UnitState.Idle;

    /// <summary>The view node of a slot, or null if the slot has never held a unit.</summary>
    public MeshInstance3D? ViewOf(int slot) => (uint)slot < (uint)_views.Length ? _views[slot] : null;

    public override void _Process(double delta)
    {
        if (Camera != null && Camera.Zoom != _cargoZoom) SetCargoZoom(Camera.Zoom);
        if (Runner?.Simulation is Simulation sim) Sync(sim.World, (float)Runner.Alpha, (float)delta);
    }

    /// <summary>Sizes the shared cargo meshes for camera zoom <paramref name="zoom"/> (one write per kind, only when the zoom changed).</summary>
    public void SetCargoZoom(float zoom)
    {
        _cargoZoom = zoom;
        float size = CargoSize * Math.Max(1f, zoom / CargoFullZoom);
        foreach (BoxMesh m in _cargoMeshes) m.Size = new Vector3(size, size, size);
    }

    /// <summary>Places every live unit at its interpolated position and facing and hides dead slots. No allocation once each slot has its node.</summary>
    /// <param name="delta">View seconds since the last call, for the hit flash's timer (0: the flash doesn't fade).</param>
    public void Sync(World world, float alpha, float delta = 0f)
    {
        UnitStore u = world.Units;
        Flash.UpdateWithSlotMax(u.Alive, u.Generation, u.Hp, MaxHp.Refreshed(world), delta);
        FogView? fog = Fog?.Refreshed(world);
        int n = Math.Min(u.Capacity, _views.Length);
        for (int i = 0; i < n; i++)
        {
            MeshInstance3D? view = _views[i];
            if (!u.Alive[i])
            {
                if (_shown[i]) Hide(i, view!);
                continue;
            }
            // A slot gets its node when it first holds a live unit, seen or not, so the fog never changes when nodes are made.
            view ??= CreateView(i);
            if (fog != null && !fog.ShowsUnit(i))
            {
                if (_shown[i]) Hide(i, view);
                continue;
            }
            int type = u.TypeId[i];
            if (_viewType[i] != type) SetType(i, view, type);
            MeshInstance3D? model = _modelMesh[i];
            int owner = u.Owner[i];
            if (_viewOwner[i] != owner && model != null)
            {
                // M6-V20: the model's team surface in the owner's colour (the faction's without per-player colours).
                int colour = _playerMats.Length > 0 ? owner : _factionColour[type];
                model.SetSurfaceOverrideMaterial(_models!.TeamSurface(type), _models.TeamMaterial(type, colour));
                _viewOwner[i] = owner;
            }
            else if (_viewOwner[i] != owner && _playerMats.Length > 0)
            {
                view.MaterialOverride = (uint)owner < (uint)_playerMats.Length ? _playerMats[owner] : null;
                _viewOwner[i] = owner;
            }
            if (!_shown[i])
            {
                view.Visible = true;
                _shown[i] = true;
                if (_anim[i] is AnimationPlayer resumed) resumed.Active = true;
            }
            Vector3 ground = GroundPoint(world, i, alpha);
            float facing = BlendFacing(u.PrevFacing[i], u.Facing[i], alpha);
            view.Transform = new Transform3D(new Basis(Vector3.Up, Yaw(facing)), ground + new Vector3(0f, _halfHeight[type], 0f));

            UnitState tint = TintStateFor(u.State[i]);
            bool lit = Flash.IsLit(i);
            if (tint != _shownTint[i] || lit != _shownLit[i])
            {
                // On a model the overlay goes on its mesh: the slot's own node draws nothing then.
                (model ?? view).MaterialOverlay = lit ? _flashMat : _tints[(int)tint];
                _shownTint[i] = tint;
                _shownLit[i] = lit;
            }
            if (_anim[i] is AnimationPlayer anim)
            {
                int clip = (int)ClipFor(world, i);
                if (clip != _shownClip[i]) PlayClip(i, anim, clip);
            }
            sbyte cargo = u.Cargo[i] > 0 ? (sbyte)u.CargoKind[i] : (sbyte)-1;
            if (cargo != _shownCargo[i]) ShowCargo(i, view, type, cargo);
        }
    }

    // Shows or hides the slot's cargo marker; the marker is a child of the body, so it follows it and hides with it.
    private void ShowCargo(int slot, MeshInstance3D view, int type, sbyte cargo)
    {
        _shownCargo[slot] = cargo;
        MeshInstance3D? marker = _markers[slot];
        if (cargo < 0 || cargo >= _cargoMeshes.Length)
        {
            if (marker != null) marker.Visible = false;
            return;
        }
        if (marker == null)
        {
            marker = new MeshInstance3D { Name = "Cargo", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            view.AddChild(marker);
            _markers[slot] = marker;
            MarkerCount++;
        }
        marker.Mesh = _cargoMeshes[cargo];
        // The body is centred on its node; the marker floats above its top (types differ in height).
        marker.Position = new Vector3(0f, _halfHeight[type] + CargoGap + CargoSize / 2f, 0f);
        marker.Visible = true;
    }

    private static StandardMaterial3D Tint(Color c) => new()
    {
        AlbedoColor = c,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    private static BoxMesh CargoMesh(Color c) => new()
    {
        Size = new Vector3(CargoSize, CargoSize, CargoSize),
        Material = new StandardMaterial3D { AlbedoColor = c, Roughness = 0.6f },
    };

    /// <summary>Calls of <see cref="FillMaxHp"/> since the program started (a test counter: BUG-0490's one fill a frame).</summary>
    public static int MaxHpFills { get; private set; }

    /// <summary>
    /// Writes each live slot's maximum hit points, <c>World.MaxHp(slot)</c> (the type's <c>hp</c> plus its owner's hp techs),
    /// into <paramref name="maxHp"/>, 0 for a dead slot (M4-V7, BUG-0450: the hp bar's and the first-sight flash's maximum).
    /// One array read per live unit; allocation-free.
    /// </summary>
    public static void FillMaxHp(World world, Span<int> maxHp)
    {
        MaxHpFills++;
        UnitStore u = world.Units;
        int n = Math.Min(u.Capacity, maxHp.Length);
        for (int i = 0; i < n; i++) maxHp[i] = u.Alive[i] ? world.MaxHp(i) : 0;
    }

    /// <summary>A unit's interpolated ground point in view coordinates: lerp(PrevPosition, Position, alpha) with alpha clamped to [0, 1], on the terrain surface.</summary>
    public static Vector3 GroundPoint(World world, int slot, float alpha)
    {
        UnitStore u = world.Units;
        // Clamped so a bad alpha can never extrapolate past the current tick.
        float a = Math.Clamp(float.IsNaN(alpha) ? 1f : alpha, 0f, 1f);
        System.Numerics.Vector2 p = System.Numerics.Vector2.Lerp(u.PrevPosition[slot], u.Position[slot], a);
        return new Vector3(p.X, TerrainHeight.At(world.Heightmap, p.X, p.Y), p.Y);
    }

    /// <summary>A unit's interpolated sim facing: from <paramref name="prev"/> toward <paramref name="current"/> the short way round by alpha (clamped to [0, 1] like <see cref="GroundPoint"/>).</summary>
    /// <remarks>
    /// The difference is wrapped into [-π, π), so 0.9π to -0.9π turns 0.2π through ±π, not 1.8π
    /// back through 0. An exact half turn wraps to -π, so it always turns the same way. The result
    /// is not wrapped: it may leave [-π, π] by up to π, which <see cref="Yaw"/> doesn't mind.
    /// </remarks>
    public static float BlendFacing(float prev, float current, float alpha)
    {
        float a = Math.Clamp(float.IsNaN(alpha) ? 1f : alpha, 0f, 1f);
        float d = current - prev;
        d -= Mathf.Floor((d + Mathf.Pi) / Mathf.Tau) * Mathf.Tau;
        return prev + d * a;
    }

    /// <summary>Godot yaw (radians about +Y) that turns a node's forward (-Z) to the sim facing.</summary>
    /// <remarks>
    /// Sim facing θ = Atan2(vy, vx) points along (cos θ, sin θ) on the ground, which is Godot
    /// (cos θ, 0, sin θ). A yaw φ turns -Z to (-sin φ, 0, -cos φ); equal when φ = -θ - π/2.
    /// </remarks>
    public static float Yaw(float facing) => -facing - Mathf.Pi / 2f;

    /// <summary>Placeholder capsule height for a unit radius (at scale 1; the hit test's body, M7-V1).</summary>
    public static float BodyHeight(float radius) => 2f * radius + ExtraBodyHeight;

    /// <summary>A unit type's drawn capsule radius (M7-V3c): its collision radius alone, so a body is as wide as the room the sim gives it.</summary>
    /// <remarks>Data <c>scale</c> is the height only (<see cref="DrawnHeight"/>); scaling the width as well drew a 0.75 m giant 1.125 m wide,
    /// inside its neighbours (BUG-1260).</remarks>
    public static float DrawnRadius(UnitDef def) => def.Radius;

    /// <summary>A unit type's drawn capsule height (M7-V1): <see cref="BodyHeight"/> of its radius times its data <c>scale</c>, never under twice its radius.</summary>
    /// <remarks>BUG-1290: Godot's <see cref="CapsuleMesh"/> clamps its radius to half its height, so a wide type at a small scale
    /// (<c>scale &lt; 2 radius / (2 radius + 1)</c>) was drawn narrower than its collision radius; such a type is drawn as a ball
    /// instead, and the bar and markers that read this height follow it.</remarks>
    public static float DrawnHeight(UnitDef def) => Math.Max(BodyHeight(def.Radius) * def.Scale, 2f * DrawnRadius(def));

    /// <summary>
    /// Live unit slot <paramref name="slot"/>'s drawn radius (M7-V3c): its collision radius, what the rings, corpse and cue
    /// discs size from. One array read, allocation-free. Click and box select keep the sim's radius too
    /// (docs/03 "Implementation (M7-V1)", "(M7-V3c)").
    /// </summary>
    public static float DrawnRadius(World world, int slot) => world.Units.Radius[slot];

    /// <summary>The capsule mesh of unit type <paramref name="type"/> (shared by every unit of it), or null before <see cref="Bind"/>.</summary>
    public CapsuleMesh? MeshOf(int type) => (uint)type < (uint)_meshes.Length ? _meshes[type] : null;

    /// <summary>Converts a data colour 0xRRGGBB (sRGB) to a Godot colour.</summary>
    public static Color ColorFromRgb(uint rgb) =>
        new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

    private void Hide(int slot, MeshInstance3D view)
    {
        view.Visible = false;
        _shown[slot] = false;
        // A hidden model doesn't animate (a dead slot may wait a long time for its next unit).
        if (_anim[slot] is AnimationPlayer anim) anim.Active = false;
    }

    // The slot now holds a unit of another type: the capsule's mesh, or the type's model (a new instance unless the slot
    // already holds one of the same scene). Material, overlay and clip are set again on the next lines of Sync.
    private void SetType(int slot, MeshInstance3D view, int type)
    {
        int old = _viewType[slot];
        _viewType[slot] = type;
        _viewOwner[slot] = -1;
        _shownTint[slot] = (UnitState)byte.MaxValue;
        _shownClip[slot] = -1;
        bool model = _models?.Has(type) == true;
        bool sameScene = model && old >= 0 && _model[slot] != null && _modelPath[old] == _modelPath[type];
        if (_model[slot] != null && !sameScene)
        {
            _model[slot]!.QueueFree();
            _model[slot] = null;
            _modelMesh[slot] = null;
            _anim[slot] = null;
        }
        view.MaterialOverride = null;
        view.MaterialOverlay = null;
        if (!model)
        {
            view.Mesh = _meshes[type];
            return;
        }
        view.Mesh = null;
        if (_model[slot] == null)
        {
            Node3D root = _models!.Instantiate(type, out MeshInstance3D mesh, out AnimationPlayer anim);
            view.AddChild(root);
            _model[slot] = root;
            _modelMesh[slot] = mesh;
            _anim[slot] = anim;
            ModelNodeCount++;
        }
        // The slot's node sits at the capsule's centre (the cargo marker's frame); the model's feet go to the ground. A data
        // scale other than 1 scales the model uniformly (M6-V20; no shipped model type has one).
        float s = _modelScale[type];
        _model[slot]!.Transform = new Transform3D(Basis.Identity.Scaled(new Vector3(s, s, s)), new Vector3(0f, -_halfHeight[type], 0f));
    }

    /// <summary>The clip a model shows for slot <paramref name="slot"/>'s sim state (M6-V20): standing, moving, attacking, and a worker's three states.</summary>
    public static UnitModels.Clip ClipFor(World world, int slot)
    {
        UnitStore u = world.Units;
        switch (u.State[slot])
        {
            case UnitState.Moving: return UnitModels.Clip.Walk;
            case UnitState.Attacking: return UnitModels.Clip.Attack;
            case UnitState.Building: return UnitModels.Clip.Build;
            case UnitState.Gathering:
                EntityHandle node = u.GatherNode[slot];
                ResourceKind kind = world.Resources.IsAlive(node) ? world.Data.Resources[world.Resources.TypeId[node.Index]].Resource : u.CargoKind[slot];
                return kind == ResourceKind.Wood ? UnitModels.Clip.GatherWood : UnitModels.Clip.GatherGold;
            default: return UnitModels.Clip.Idle; // Idle, Returning (standing with a load), Casting
        }
    }

    // Plays the clip (idle when the library lacks it), started at a phase from the slot so a crowd doesn't move in step.
    private void PlayClip(int slot, AnimationPlayer anim, int clip)
    {
        _shownClip[slot] = clip;
        if (!UnitModels.HasClip((UnitModels.Clip)clip)) clip = (int)UnitModels.Clip.Idle;
        if (!UnitModels.HasClip((UnitModels.Clip)clip))
        {
            anim.Stop();
            return;
        }
        anim.Play(UnitModels.ClipNames[clip]);
        float length = UnitModels.ClipLength((UnitModels.Clip)clip);
        if (length > 0f) anim.Seek(slot * 0.37f % length, true);
    }

    /// <summary>Model instances created so far (M6-V20; one per slot and model scene it held).</summary>
    public int ModelNodeCount { get; private set; }

    private MeshInstance3D CreateView(int slot)
    {
        var view = new MeshInstance3D { Name = $"Unit{slot}", Visible = false };
        AddChild(view);
        _views[slot] = view;
        NodeCount++;
        return view;
    }
}
