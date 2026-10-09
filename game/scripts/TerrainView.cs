using Godot;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>Shows the match terrain: copies the pure <see cref="TerrainMeshBuilder"/> output into an <see cref="ArrayMesh"/>; with a fog, its material darkens the ground by the fog texture (M4-V4).</summary>
public partial class TerrainView : MeshInstance3D
{
    /// <summary>Builds the mesh once for the match's heightmap; with <paramref name="fog"/> its material is the fog's terrain shader, else the plain vertex-colour material.</summary>
    public void Build(Heightmap map, FogOfWar? fog = null)
    {
        TerrainMesh src = TerrainMeshBuilder.Build(map);
        int n = src.Positions.Length;
        var positions = new Vector3[n];
        var normals = new Vector3[n];
        var colors = new Color[n];
        for (int i = 0; i < n; i++)
        {
            System.Numerics.Vector3 p = src.Positions[i], q = src.Normals[i];
            System.Numerics.Vector4 c = src.Colors[i];
            positions[i] = new Vector3(p.X, p.Y, p.Z);
            normals[i] = new Vector3(q.X, q.Y, q.Z);
            colors[i] = new Color(c.X, c.Y, c.Z, c.W);
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = src.Indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, fog != null ? fog.TerrainMaterial() : new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = true,
            Roughness = 1f,
        });
        Mesh = mesh;
    }
}
