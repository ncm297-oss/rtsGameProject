using Godot;
using Rts.Sim;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>Debug overlay layer: the nav grid as one translucent quad per cell on the terrain (M2-5); geometry from the pure <see cref="NavOverlayBuilder"/>.</summary>
/// <remarks>
/// Has no <c>_Process</c>: <see cref="DebugOverlay"/> calls <see cref="Sync"/> each frame while the overlay is
/// on. Nothing is built until the first call; the mesh is re-uploaded only when the builder refills
/// (the first call and each <c>NavGrid.Version</c> change), so a steady frame costs one integer compare.
/// </remarks>
public partial class NavOverlayView : MeshInstance3D
{
    private NavOverlayBuilder? _builder;
    private Vector3[] _positions = System.Array.Empty<Vector3>();
    private Color[] _colors = System.Array.Empty<Color>();
    private ArrayMesh? _mesh;
    private StandardMaterial3D? _material;

    /// <summary>Times the mesh was uploaded (test and debug readout).</summary>
    public int Uploads { get; private set; }

    /// <summary>The builder, or null before the first <see cref="Sync"/>.</summary>
    public NavOverlayBuilder? Builder => _builder;

    /// <summary>Builds the mesh on first use and re-uploads it when the grid's version changed; otherwise does nothing.</summary>
    public void Sync(World world)
    {
        if (_builder == null)
        {
            _builder = new NavOverlayBuilder(world.Heightmap);
            int n = _builder.Positions.Length;
            _positions = new Vector3[n];
            _colors = new Color[n];
            for (int i = 0; i < n; i++)
            {
                System.Numerics.Vector3 p = _builder.Positions[i];
                _positions[i] = new Vector3(p.X, p.Y, p.Z);
            }
            _mesh = new ArrayMesh();
            _material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                VertexColorUseAsAlbedo = true,
                VertexColorIsSrgb = true,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            Mesh = _mesh;
            CastShadow = ShadowCastingSetting.Off;
        }
        if (!_builder.Refresh(world.NavGrid)) return;

        for (int i = 0; i < _colors.Length; i++)
        {
            System.Numerics.Vector4 c = _builder.Colors[i];
            _colors[i] = new Color(c.X, c.Y, c.Z, c.W);
        }
        // Only on a version change (rare: buildings, trees), so the Godot array here is fine.
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _positions;
        arrays[(int)Mesh.ArrayType.Color] = _colors;
        arrays[(int)Mesh.ArrayType.Index] = _builder.Indices;
        _mesh!.ClearSurfaces();
        _mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _mesh.SurfaceSetMaterial(0, _material);
        Uploads++;
    }
}
