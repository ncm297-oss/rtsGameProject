using System;
using Godot;
using Rts.Sim;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>Debug overlay layer: flow-field arrows of the selection's goal around the camera (M2-5), one <see cref="MultiMesh"/> instance per arrow, plus a marker on the cell the field leads to.</summary>
/// <remarks>
/// Has no <c>_Process</c>: <see cref="DebugOverlay"/> calls <see cref="Sync"/> each frame while the overlay is
/// on. The pure <see cref="FlowArrowLayout"/> peeks the goal's cached field every call and keeps no
/// field; the instance buffer is rewritten only when the layout relists (goal, field version or
/// window changed). Nothing is built until the first call.
/// </remarks>
public partial class FlowArrowsView : MultiMeshInstance3D
{
    /// <summary>Height of the arrows above the terrain at the cell centre, in meters (clears a ramp's slope under the arrow).</summary>
    public const float Lift = 0.3f;

    // Dev overlay tints, not player-facing.
    private static readonly Color ArrowColor = new(1f, 0.92f, 0.2f);
    private static readonly Color MarkerColor = new(0.2f, 0.85f, 1f);

    private const int Stride = 12; // floats per Transform3D instance in the MultiMesh buffer

    private FlowArrowLayout? _layout;
    private MultiMesh? _mm;
    private float[] _buffer = Array.Empty<float>();
    private MeshInstance3D? _marker;

    /// <summary>The layout, or null before the first <see cref="Sync"/>.</summary>
    public FlowArrowLayout? Layout => _layout;

    /// <summary>Arrows currently drawn.</summary>
    public int ShownCount => _mm?.VisibleInstanceCount ?? 0;

    /// <summary>Times the instance buffer was rewritten (test and debug readout).</summary>
    public int Uploads { get; private set; }

    /// <summary>The goal-cell marker, or null before the first <see cref="Sync"/>.</summary>
    public MeshInstance3D? Marker => _marker;

    /// <summary>Peeks the goal's field and redraws the arrows if the layout changed.</summary>
    /// <param name="goal">Goal cell (from <see cref="FlowArrowLayout.GoalOf"/>), or -1 for none.</param>
    /// <param name="focus">Camera focus in sim meters; the window is centred on its cell.</param>
    public void Sync(World world, int goal, System.Numerics.Vector2 focus)
    {
        if (_layout == null) Build();
        if (!_layout!.Refresh(world.FlowFields, world.NavGrid, goal, focus)) return;

        NavGrid grid = world.NavGrid;
        Heightmap map = world.Heightmap;
        ReadOnlySpan<int> cells = _layout.Cells;
        ReadOnlySpan<byte> dirs = _layout.Directions;
        float[] b = _buffer;
        for (int k = 0; k < cells.Length; k++)
        {
            int c = cells[k];
            System.Numerics.Vector2 centre = grid.CellCenter(c % grid.Width, c / grid.Width);
            System.Numerics.Vector2 v = FlowArrowLayout.DirectionVector(dirs[k]);
            float y = TerrainHeight.At(map, centre.X, centre.Y) + Lift;
            // Basis columns X = (v.x, 0, v.y) (the arrow mesh points along +X), Y = up, Z = X x Y; rows of a 3x4 matrix.
            int o = k * Stride;
            b[o] = v.X; b[o + 1] = 0f; b[o + 2] = -v.Y; b[o + 3] = centre.X;
            b[o + 4] = 0f; b[o + 5] = 1f; b[o + 6] = 0f; b[o + 7] = y;
            b[o + 8] = v.Y; b[o + 9] = 0f; b[o + 10] = v.X; b[o + 11] = centre.Y;
        }
        _mm!.Buffer = b;
        _mm.VisibleInstanceCount = cells.Length;

        int t = _layout.MarkedCell;
        _marker!.Visible = t >= 0;
        if (t >= 0)
        {
            System.Numerics.Vector2 centre = grid.CellCenter(t % grid.Width, t / grid.Width);
            _marker.Position = new Vector3(centre.X, TerrainHeight.At(map, centre.X, centre.Y) + Lift, centre.Y);
        }
        Uploads++;
    }

    /// <summary>Drawn arrow <paramref name="k"/>'s transform as uploaded, decoded from the instance buffer (tests; the headless renderer keeps no instance data to read back).</summary>
    public Transform3D ArrowTransform(int k)
    {
        float[] b = _buffer;
        int o = k * Stride;
        // The buffer holds the 3x4 matrix row by row; Basis takes columns.
        var basis = new Basis(new Vector3(b[o], b[o + 4], b[o + 8]), new Vector3(b[o + 1], b[o + 5], b[o + 9]), new Vector3(b[o + 2], b[o + 6], b[o + 10]));
        return new Transform3D(basis, new Vector3(b[o + 3], b[o + 7], b[o + 11]));
    }

    /// <summary>The engine's copy of instance <paramref name="k"/>'s transform (identity under the headless renderer).</summary>
    public Transform3D EngineTransform(int k) => _mm!.GetInstanceTransform(k);

    private void Build()
    {
        _layout = new FlowArrowLayout();
        int capacity = _layout.Window * _layout.Window;
        _buffer = new float[capacity * Stride];
        Multimesh = _mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = ArrowMesh(),
            InstanceCount = capacity,
            VisibleInstanceCount = 0,
        };
        CastShadow = ShadowCastingSetting.Off;
        _marker = new MeshInstance3D
        {
            Name = "GoalMarker",
            Mesh = new CylinderMesh { TopRadius = 0.7f, BottomRadius = 0.7f, Height = 0.1f, RadialSegments = 16 },
            MaterialOverride = Unshaded(MarkerColor),
            CastShadow = ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_marker);
    }

    // A flat arrow in the XZ plane pointing +X, 1.3 m long (cells are 2 m), drawn from both sides.
    private static ArrayMesh ArrowMesh()
    {
        const float tail = -0.65f, neck = 0.1f, tip = 0.65f, shaft = 0.1f, head = 0.32f;
        var v = new[]
        {
            new Vector3(tail, 0, -shaft), new Vector3(neck, 0, -shaft), new Vector3(neck, 0, shaft), new Vector3(tail, 0, shaft),
            new Vector3(neck, 0, -head), new Vector3(tip, 0, 0), new Vector3(neck, 0, head),
        };
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = v;
        arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6 };
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, Unshaded(ArrowColor));
        return mesh;
    }

    private static StandardMaterial3D Unshaded(Color c) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoColor = c,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };
}
