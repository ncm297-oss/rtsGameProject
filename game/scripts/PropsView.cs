using System;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>Draws the map's resource nodes (trees, gold mines) as props: one <see cref="MultiMeshInstance3D"/> per resource type (M2-3b).</summary>
/// <remarks>
/// Placeholder meshes are built in code, picked by the type's <see cref="ResourceKind"/> (wood: a cone
/// on a trunk; gold: a slate block with a gold top) and sized from its footprint, so a new type is
/// data only. Instance transforms come from the pure <see cref="PropLayout"/>, which relists only when
/// <see cref="NavGrid.Version"/> changes; the instance buffer is uploaded only then, so a steady frame
/// costs one int compare and allocates nothing. Holds no gameplay state.
/// </remarks>
public partial class PropsView : Node3D
{
    /// <summary>Tree trunk height and radius, in meters.</summary>
    public const float TrunkHeight = 1f, TrunkRadius = 0.15f;

    /// <summary>Tree top above the ground, in meters.</summary>
    public const float TreeHeight = 3.5f;

    /// <summary>Share of the footprint's smaller side the canopy's diameter takes (0.8 m radius in a 2 m cell).</summary>
    public const float CanopyFill = 0.8f;

    /// <summary>Mine block height and the gold block's height, in meters.</summary>
    public const float MineHeight = 1.6f, GoldHeight = 0.5f;

    /// <summary>Share of the footprint's sides the gold block on a mine takes.</summary>
    public const float GoldFill = 0.5f;

    // Placeholder tints until the M6 art pass (M2-1 rule: hard-coded like the terrain's).
    private static readonly Color CanopyColor = new(0.11f, 0.30f, 0.12f);
    private static readonly Color TrunkColor = new(0.36f, 0.23f, 0.12f);
    private static readonly Color SlateColor = new(0.20f, 0.22f, 0.25f);
    private static readonly Color GoldColor = new(0.90f, 0.70f, 0.15f);

    private PropLayout? _layout;
    private MultiMesh[] _mms = Array.Empty<MultiMesh>();
    private float[][] _buffers = Array.Empty<float[]>();
    private int[] _typeUploads = Array.Empty<int>();

    /// <summary>The runner whose sim is shown each frame; null shows nothing (tests call <see cref="Sync"/> directly).</summary>
    public SimRunner? Runner { get; set; }

    /// <summary>The layout, or null before <see cref="Bind"/>.</summary>
    public PropLayout? Layout => _layout;

    /// <summary>Relists that uploaded at least one type's buffer (test and debug readout).</summary>
    public int Uploads { get; private set; }

    /// <summary>Times resource type <paramref name="type"/>'s buffer was uploaded: only when its own list changed (BUG-0086).</summary>
    public int UploadsOf(int type) => _typeUploads[type];

    /// <summary>Instances drawn for resource type <paramref name="type"/>.</summary>
    public int ShownCount(int type) => _mms[type].VisibleInstanceCount;

    /// <summary>The MultiMesh child of resource type <paramref name="type"/>.</summary>
    public MultiMeshInstance3D InstanceOf(int type) => GetNode<MultiMeshInstance3D>(type.ToString());

    /// <summary>Builds one MultiMesh child per resource type, sized for <paramref name="capacity"/> nodes (the store's); call once before the first <see cref="Sync"/>.</summary>
    public void Bind(GameData data, int capacity)
    {
        _layout = new PropLayout(data.Resources, capacity);
        _mms = new MultiMesh[data.Resources.Length];
        _buffers = new float[_mms.Length][];
        _typeUploads = new int[_mms.Length];
        for (int t = 0; t < _mms.Length; t++)
        {
            ResourceDef def = data.Resources[t];
            _mms[t] = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = def.Resource == ResourceKind.Gold ? MineMesh(def) : TreeMesh(def),
                InstanceCount = capacity,
                VisibleInstanceCount = 0,
            };
            _buffers[t] = new float[capacity * PropLayout.Stride];
            AddChild(new MultiMeshInstance3D { Name = t.ToString(), Multimesh = _mms[t] });
        }
    }

    public override void _Process(double delta)
    {
        if (Runner?.Simulation is Simulation sim) Sync(sim.World);
    }

    /// <summary>Relists and uploads the props if the grid's version changed since the last upload.</summary>
    public void Sync(World world)
    {
        if (_layout == null) return;
        ResourceStore r = world.Resources;
        if (!_layout.Refresh(world.Heightmap, world.NavGrid, r.Alive, r.TypeId, r.Cell)) return;
        bool any = false;
        for (int t = 0; t < _mms.Length; t++)
        {
            // A felled tree re-uploads the trees, not the mines (BUG-0086).
            if (!_layout.Changed(t)) continue;
            // Each type has its own buffer, so its hidden instances (past the count) only ever hold its own old transforms.
            _layout.TransformsOf(t).CopyTo(_buffers[t]);
            // Godot wants the whole buffer; instances past the count are never drawn.
            _mms[t].Buffer = _buffers[t];
            _mms[t].VisibleInstanceCount = _layout.CountOf(t);
            _typeUploads[t]++;
            any = true;
        }
        if (any) Uploads++;
    }

    // A cone canopy on a trunk cylinder, standing on the origin, inside the footprint.
    private static ArrayMesh TreeMesh(ResourceDef def)
    {
        float side = Math.Min(def.FootprintWidth, def.FootprintHeight) * MapConstants.CellSize;
        float canopy = side * CanopyFill / 2;
        var mesh = new ArrayMesh();
        // Few segments on purpose: up to 4,096 trees, and the facets show the yaw variety.
        Append(mesh, new CylinderMesh { TopRadius = TrunkRadius, BottomRadius = TrunkRadius, Height = TrunkHeight, RadialSegments = 6, Rings = 1 },
            TrunkHeight / 2, TrunkColor);
        Append(mesh, new CylinderMesh { TopRadius = 0f, BottomRadius = canopy, Height = TreeHeight - TrunkHeight, RadialSegments = 7, Rings = 1 },
            TrunkHeight + (TreeHeight - TrunkHeight) / 2, CanopyColor);
        return mesh;
    }

    // A slate block covering the footprint with a smaller gold block on top.
    private static ArrayMesh MineMesh(ResourceDef def)
    {
        float w = def.FootprintWidth * MapConstants.CellSize, d = def.FootprintHeight * MapConstants.CellSize;
        var mesh = new ArrayMesh();
        Append(mesh, new BoxMesh { Size = new Vector3(w, MineHeight, d) }, MineHeight / 2, SlateColor);
        Append(mesh, new BoxMesh { Size = new Vector3(w * GoldFill, GoldHeight, d * GoldFill) }, MineHeight + GoldHeight / 2, GoldColor);
        return mesh;
    }

    // Adds a primitive as a new surface of `mesh`, lifted by `y`, with its own material.
    private static void Append(ArrayMesh mesh, PrimitiveMesh part, float y, Color color)
    {
        var st = new SurfaceTool();
        st.AppendFrom(part, 0, new Transform3D(Basis.Identity, new Vector3(0f, y, 0f)));
        st.Commit(mesh);
        mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, new StandardMaterial3D { AlbedoColor = color, Roughness = 0.9f });
    }
}
