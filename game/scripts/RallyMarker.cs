using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>The selected building's rally marker (M3-V3): a placeholder cone at <see cref="BuildingStore.RallyPosition"/> and a thin line to it from the footprint's edge.</summary>
/// <remarks>
/// Shown while an own finished building is selected (<see cref="SelectionController.SelectedFinishedBuilding"/>) and has a
/// rally point (<see cref="BuildingStore.HasRally"/>); the line starts at <see cref="RallyGeometry.EdgePoint"/> and is
/// absent when the point is inside the footprint. Polled each frame; the transforms are written only when the building or
/// the point changes, so a steady frame allocates nothing. Holds view state only.
/// </remarks>
public partial class RallyMarker : Node3D
{
    /// <summary>The cone's height and base radius, in meters.</summary>
    public const float ConeHeight = 1.6f, ConeRadius = 0.45f;

    /// <summary>The line's width and its lift above the ground, in meters.</summary>
    public const float LineWidth = 0.12f, LineLift = 0.15f;

    private static readonly Color MarkerColor = new(1f, 0.85f, 0.25f);

    private SimRunner? _runner;
    private SelectionController _sel = null!;
    private MeshInstance3D _cone = null!, _line = null!;
    private int _slot = -1, _gen;
    private System.Numerics.Vector2 _target = new(float.NaN, float.NaN);

    /// <summary>The building whose rally point is drawn, or -1.</summary>
    public int ShownSlot => Visible ? _slot : -1;

    /// <summary>The rally point drawn (sim x, y in meters); meaningful while <see cref="ShownSlot"/> is not -1.</summary>
    public System.Numerics.Vector2 ShownTarget => _target;

    /// <summary>True while the line is drawn.</summary>
    public bool LineShown => _line.Visible;

    /// <summary>The cone node.</summary>
    public MeshInstance3D Cone => _cone;

    /// <summary>Times the marker was moved.</summary>
    public int Updates { get; private set; }

    public override void _Ready()
    {
        var mat = new StandardMaterial3D { AlbedoColor = MarkerColor, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _cone = new MeshInstance3D
        {
            Name = "Cone", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = ConeRadius, Height = ConeHeight, RadialSegments = 10, Rings = 1, Material = mat },
        };
        _line = new MeshInstance3D
        {
            Name = "Line", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Mesh = new BoxMesh { Size = new Vector3(LineWidth, 0.04f, 1f), Material = mat },
        };
        AddChild(_cone);
        AddChild(_line);
        Visible = false;
    }

    /// <summary>Connects the marker to the match; call once after the sim exists.</summary>
    public void Init(SimRunner runner, SelectionController selection)
    {
        _runner = runner;
        _sel = selection;
    }

    public override void _Process(double delta)
    {
        if (_runner?.Simulation is Simulation sim) Sync(sim.World);
    }

    /// <summary>Shows the selected building's rally point, moving the nodes only when it changed. Allocation-free.</summary>
    public void Sync(World world)
    {
        if (_sel == null) return;
        BuildingStore b = world.Buildings;
        int slot = _sel.SelectedFinishedBuilding;
        if (slot < 0 || !b.HasRally[slot])
        {
            if (Visible) Visible = false;
            _slot = -1;
            return;
        }
        System.Numerics.Vector2 target = b.RallyPosition[slot];
        if (slot == _slot && b.Generation[slot] == _gen && target == _target && Visible) return;
        _slot = slot;
        _gen = b.Generation[slot];
        _target = target;
        Heightmap map = world.Heightmap;
        float ty = TerrainHeight.At(map, target.X, target.Y);
        _cone.Position = new Vector3(target.X, ty + ConeHeight / 2f, target.Y);
        BuildingDef def = world.Data.Buildings[b.TypeId[slot]];
        int w = world.NavGrid.Width, anchor = b.Cell[slot];
        var min = new System.Numerics.Vector2(anchor % w, anchor / w) * MapConstants.CellSize;
        var max = min + new System.Numerics.Vector2(def.FootprintWidth, def.FootprintHeight) * MapConstants.CellSize;
        float length = RallyGeometry.Line(min, max, target, out System.Numerics.Vector2 from);
        if (length > 0.01f)
        {
            var a = new Vector3(from.X, TerrainHeight.At(map, from.X, from.Y) + LineLift, from.Y);
            var c = new Vector3(target.X, ty + LineLift, target.Y);
            Vector3 dir = c - a;
            // The box's local Z runs along the line, scaled to its length.
            Basis basis = Basis.LookingAt(dir.Normalized(), Vector3.Up) * Basis.FromScale(new Vector3(1f, 1f, dir.Length()));
            _line.Transform = new Transform3D(basis, (a + c) / 2f);
            _line.Visible = true;
        }
        else _line.Visible = false;
        Visible = true;
        Updates++;
    }
}
