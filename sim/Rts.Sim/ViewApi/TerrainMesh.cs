using System.Numerics;

namespace Rts.Sim.ViewApi;

/// <summary>Engine-neutral triangle mesh of the terrain: parallel vertex arrays plus a triangle index list.</summary>
/// <remarks>
/// Coordinates are Y-up meters: X = sim x, Y = elevation, Z = sim y. Triangles wind clockwise
/// seen from the side their normal points to (Godot's front-face convention).
/// </remarks>
public sealed class TerrainMesh
{
    /// <summary>Wraps already-built arrays (not copied).</summary>
    public TerrainMesh(Vector3[] positions, Vector3[] normals, Vector4[] colors, int[] indices)
    {
        Positions = positions;
        Normals = normals;
        Colors = colors;
        Indices = indices;
    }

    /// <summary>Vertex positions in meters.</summary>
    public Vector3[] Positions { get; }

    /// <summary>Unit vertex normals, one per position.</summary>
    public Vector3[] Normals { get; }

    /// <summary>Vertex colors as sRGB RGBA in [0, 1], one per position.</summary>
    public Vector4[] Colors { get; }

    /// <summary>Triangle list: every three entries index one triangle.</summary>
    public int[] Indices { get; }
}
