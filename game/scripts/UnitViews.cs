using System;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>Placeholder unit views: one capsule <see cref="MeshInstance3D"/> per unit slot, placed from the sim every frame.</summary>
/// <remarks>
/// A slot's node is created the first time the slot holds a live unit and hidden while it is dead,
/// so a unit respawned into the slot reuses it. Meshes (one per unit type, radius from data) and
/// materials (one per faction, its <c>PrimaryColor</c>) are all made in <see cref="Bind"/>. Views
/// hold no gameplay state; they only read the unit store.
/// Worker feedback (M3-V1): a standing worker's <see cref="UnitState"/> (<c>Gathering</c>, <c>Returning</c>,
/// <c>Building</c>) tints its body through a shared overlay material, and while <c>Cargo</c> &gt; 0 a small marker
/// floats above it, gold-coloured or wood-brown by <c>CargoKind</c>. A slot's marker node is made the first time it
/// carries and then reused; overlay and marker are only touched when what they show changes, so a steady frame
/// allocates nothing.
/// </remarks>
public partial class UnitViews : Node3D
{
    /// <summary>Capsule height above twice the radius, in meters.</summary>
    public const float ExtraBodyHeight = 1f;

    /// <summary>Cargo marker edge length, and its gap above the body, in meters.</summary>
    public const float CargoSize = 0.35f, CargoGap = 0.3f;

    // Placeholder tints until the M6 art pass (M2-1 rule). Cargo colours match the props' gold and trunk.
    private static readonly Color GoldCargoColor = new(0.90f, 0.70f, 0.15f);
    private static readonly Color WoodCargoColor = new(0.45f, 0.28f, 0.12f);
    private static readonly Color GatheringTint = new(0.25f, 0.95f, 0.35f, 0.45f);
    private static readonly Color ReturningTint = new(1.00f, 0.80f, 0.20f, 0.45f);
    private static readonly Color BuildingTint = new(0.30f, 0.65f, 1.00f, 0.45f);

    /// <summary>The runner whose sim is shown each frame; null shows nothing (tests call <see cref="Sync"/> directly).</summary>
    public SimRunner? Runner { get; set; }

    private CapsuleMesh[] _meshes = Array.Empty<CapsuleMesh>();
    private float[] _halfHeight = Array.Empty<float>();
    private MeshInstance3D?[] _views = Array.Empty<MeshInstance3D?>();
    private int[] _viewType = Array.Empty<int>();
    private bool[] _shown = Array.Empty<bool>();

    // Worker feedback: overlay per UnitState (null for untinted states), marker mesh per ResourceKind.
    private StandardMaterial3D?[] _tints = Array.Empty<StandardMaterial3D?>();
    private BoxMesh[] _cargoMeshes = Array.Empty<BoxMesh>();
    private MeshInstance3D?[] _markers = Array.Empty<MeshInstance3D?>();
    // What each slot shows now: the tinted state (Idle: none), the cargo kind (-1: none).
    private UnitState[] _shownTint = Array.Empty<UnitState>();
    private sbyte[] _shownCargo = Array.Empty<sbyte>();

    /// <summary>View nodes created so far (one per slot ever used).</summary>
    public int NodeCount { get; private set; }

    /// <summary>Meshes created (one per unit type).</summary>
    public int MeshCount => _meshes.Length;

    /// <summary>Materials created (one per faction).</summary>
    public int MaterialCount { get; private set; }

    /// <summary>Cargo marker nodes created so far (one per slot that ever carried).</summary>
    public int MarkerCount { get; private set; }

    /// <summary>Creates the shared meshes and materials and sizes the slot pool; call once before the first <see cref="Sync"/>.</summary>
    public void Bind(GameData data, int unitCapacity)
    {
        var materials = new StandardMaterial3D[data.Factions.Length];
        for (int f = 0; f < materials.Length; f++)
            materials[f] = new StandardMaterial3D { AlbedoColor = ColorFromRgb(data.Factions[f].PrimaryColor), Roughness = 0.8f };
        MaterialCount = materials.Length;

        _meshes = new CapsuleMesh[data.Units.Length];
        _halfHeight = new float[data.Units.Length];
        for (int t = 0; t < _meshes.Length; t++)
        {
            UnitDef def = data.Units[t];
            float height = BodyHeight(def.Radius);
            // Low-poly on purpose: 2,000 of these are on screen at once (M2-7 perf budget).
            _meshes[t] = new CapsuleMesh { Radius = def.Radius, Height = height, RadialSegments = 12, Rings = 3, Material = materials[def.Faction] };
            _halfHeight[t] = height / 2;
        }

        _views = new MeshInstance3D?[unitCapacity];
        _viewType = new int[unitCapacity];
        Array.Fill(_viewType, -1);
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
        if (Runner?.Simulation is Simulation sim) Sync(sim.World, (float)Runner.Alpha);
    }

    /// <summary>Places every live unit at its interpolated position and facing and hides dead slots. No allocation once each slot has its node.</summary>
    public void Sync(World world, float alpha)
    {
        UnitStore u = world.Units;
        int n = Math.Min(u.Capacity, _views.Length);
        for (int i = 0; i < n; i++)
        {
            MeshInstance3D? view = _views[i];
            if (!u.Alive[i])
            {
                if (_shown[i])
                {
                    view!.Visible = false;
                    _shown[i] = false;
                }
                continue;
            }
            view ??= CreateView(i);
            int type = u.TypeId[i];
            if (_viewType[i] != type)
            {
                view.Mesh = _meshes[type];
                _viewType[i] = type;
            }
            if (!_shown[i])
            {
                view.Visible = true;
                _shown[i] = true;
            }
            Vector3 ground = GroundPoint(world, i, alpha);
            float facing = BlendFacing(u.PrevFacing[i], u.Facing[i], alpha);
            view.Transform = new Transform3D(new Basis(Vector3.Up, Yaw(facing)), ground + new Vector3(0f, _halfHeight[type], 0f));

            UnitState tint = TintStateFor(u.State[i]);
            if (tint != _shownTint[i])
            {
                view.MaterialOverlay = _tints[(int)tint];
                _shownTint[i] = tint;
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

    /// <summary>Placeholder capsule height for a unit radius.</summary>
    public static float BodyHeight(float radius) => 2f * radius + ExtraBodyHeight;

    /// <summary>Converts a data colour 0xRRGGBB (sRGB) to a Godot colour.</summary>
    public static Color ColorFromRgb(uint rgb) =>
        new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

    private MeshInstance3D CreateView(int slot)
    {
        var view = new MeshInstance3D { Name = $"Unit{slot}" };
        AddChild(view);
        _views[slot] = view;
        NodeCount++;
        return view;
    }
}
