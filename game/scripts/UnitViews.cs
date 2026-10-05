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
/// </remarks>
public partial class UnitViews : Node3D
{
    /// <summary>Capsule height above twice the radius, in meters.</summary>
    public const float ExtraBodyHeight = 1f;

    /// <summary>The runner whose sim is shown each frame; null shows nothing (tests call <see cref="Sync"/> directly).</summary>
    public SimRunner? Runner { get; set; }

    private CapsuleMesh[] _meshes = Array.Empty<CapsuleMesh>();
    private float[] _halfHeight = Array.Empty<float>();
    private MeshInstance3D?[] _views = Array.Empty<MeshInstance3D?>();
    private int[] _viewType = Array.Empty<int>();
    private bool[] _shown = Array.Empty<bool>();

    /// <summary>View nodes created so far (one per slot ever used).</summary>
    public int NodeCount { get; private set; }

    /// <summary>Meshes created (one per unit type).</summary>
    public int MeshCount => _meshes.Length;

    /// <summary>Materials created (one per faction).</summary>
    public int MaterialCount { get; private set; }

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
    }

    /// <summary>The view node of a slot, or null if the slot has never held a unit.</summary>
    public MeshInstance3D? ViewOf(int slot) => (uint)slot < (uint)_views.Length ? _views[slot] : null;

    public override void _Process(double delta)
    {
        if (Runner?.Simulation is Simulation sim) Sync(sim.World, (float)Runner.Alpha);
    }

    /// <summary>Places every live unit at its interpolated position and hides dead slots. No allocation once each slot has its node.</summary>
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
            view.Transform = new Transform3D(new Basis(Vector3.Up, Yaw(u.Facing[i])), ground + new Vector3(0f, _halfHeight[type], 0f));
        }
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
