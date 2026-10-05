using System;
using System.Collections.Generic;
using System.Numerics;
using Rts.Sim.Map;

namespace Rts.Sim.ViewApi;

/// <summary>Builds the terraced terrain mesh from a <see cref="Heightmap"/>: flat plateau cells, sloped ramp cells, vertical cliff walls.</summary>
/// <remarks>
/// Runs once per match, outside the tick, so it allocates freely. Each cell gets its own four
/// vertices (no sharing) so colors and normals stay crisp per cell. It only reads the heightmap.
/// </remarks>
public static class TerrainMeshBuilder
{
    // Placeholder palette (sRGB) until biome materials arrive; presentation only, not game data.
    private static readonly Vector4[] s_levelColors =
    {
        new(0.56f, 0.50f, 0.36f, 1f), // level 0: dusty lowland
        new(0.45f, 0.55f, 0.32f, 1f), // level 1: green upland
        new(0.74f, 0.72f, 0.62f, 1f), // level 2: pale stone
    };

    /// <summary>Color of ramp tops.</summary>
    public static readonly Vector4 RampColor = new(0.80f, 0.60f, 0.34f, 1f);

    /// <summary>Color of vertical cliff walls (and ramp side walls).</summary>
    public static readonly Vector4 CliffColor = new(0.32f, 0.27f, 0.23f, 1f);

    // Height differences below this (meters) count as level ground, not a wall.
    private const float FlatEpsilon = 1e-4f;

    /// <summary>Color of a plateau cell's top at an elevation level (0..<see cref="MapConstants.MaxLevel"/>).</summary>
    public static Vector4 LevelColor(int level) => s_levelColors[Math.Clamp(level, 0, s_levelColors.Length - 1)];

    /// <summary>Builds the mesh. The heightmap is only read.</summary>
    public static TerrainMesh Build(Heightmap map)
    {
        int w = map.Width, h = map.Height;
        const float cs = MapConstants.CellSize;

        // Per cell, the heights of its four corners: [0] (x0,z0), [1] (x1,z0), [2] (x0,z1), [3] (x1,z1).
        var corners = new float[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                TerrainHeight.CellCorners(map, x, y, corners.AsSpan((y * w + x) * 4, 4));

        var mesh = new MeshLists();
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int c = (y * w + x) * 4;
                float x0 = x * cs, x1 = x0 + cs, z0 = y * cs, z1 = z0 + cs;
                float h00 = corners[c], h10 = corners[c + 1], h01 = corners[c + 2], h11 = corners[c + 3];
                float dhdx = (h10 - h00 + h11 - h01) / (2 * cs);
                float dhdz = (h01 - h00 + h11 - h10) / (2 * cs);
                Vector3 n = Vector3.Normalize(new Vector3(-dhdx, 1f, -dhdz));
                Vector4 color = map.IsRamp(x, y) ? RampColor : LevelColor(map.LevelAt(x, y));
                mesh.AddQuad(new(x0, h00, z0), new(x1, h10, z0), new(x1, h11, z1), new(x0, h01, z1), n, color);

                // Wall on the shared edge with the right neighbour (x = x1) and the one below (z = z1).
                if (x + 1 < w)
                {
                    int r = c + 4;
                    AddWall(mesh, new Vector2(x1, z0), new Vector2(x1, z1),
                        corners[c + 1], corners[c + 3], corners[r], corners[r + 2], Vector3.UnitX);
                }
                if (y + 1 < h)
                {
                    int d = c + w * 4;
                    AddWall(mesh, new Vector2(x0, z1), new Vector2(x1, z1),
                        corners[c + 2], corners[c + 3], corners[d], corners[d + 1], Vector3.UnitZ);
                }
            }
        }
        return mesh.ToMesh();
    }

    // A vertical quad between two cells along their shared edge a-b (ground x, z), if their
    // heights differ there. aA/bA are cell A's heights at a and b, aB/bB cell B's; B lies on the
    // +axis side of A. The normal faces the lower cell.
    private static void AddWall(MeshLists mesh, Vector2 a, Vector2 b, float aA, float bA, float aB, float bB, Vector3 axis)
    {
        float aTop = MathF.Max(aA, aB), aBot = MathF.Min(aA, aB);
        float bTop = MathF.Max(bA, bB), bBot = MathF.Min(bA, bB);
        if (aTop - aBot <= FlatEpsilon && bTop - bBot <= FlatEpsilon) return;
        Vector3 n = (aA + bA) > (aB + bB) ? axis : -axis;
        mesh.AddQuad(new(a.X, aBot, a.Y), new(b.X, bBot, b.Y), new(b.X, bTop, b.Y), new(a.X, aTop, a.Y), n, CliffColor);
    }

    private sealed class MeshLists
    {
        private readonly List<Vector3> _positions = new();
        private readonly List<Vector3> _normals = new();
        private readonly List<Vector4> _colors = new();
        private readonly List<int> _indices = new();

        // Corners in order around the quad; emits two triangles wound clockwise as seen from n.
        public void AddQuad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 n, Vector4 color)
        {
            int i = _positions.Count;
            _positions.Add(p0);
            _positions.Add(p1);
            _positions.Add(p2);
            _positions.Add(p3);
            for (int k = 0; k < 4; k++)
            {
                _normals.Add(n);
                _colors.Add(color);
            }
            // The diagonals' cross product points along n when p0..p3 run counter-clockwise from n's side.
            bool ccw = Vector3.Dot(Vector3.Cross(p2 - p0, p3 - p1), n) > 0f;
            if (ccw)
            {
                AddTriangle(i, i + 2, i + 1);
                AddTriangle(i, i + 3, i + 2);
            }
            else
            {
                AddTriangle(i, i + 1, i + 2);
                AddTriangle(i, i + 2, i + 3);
            }
        }

        private void AddTriangle(int a, int b, int c)
        {
            _indices.Add(a);
            _indices.Add(b);
            _indices.Add(c);
        }

        public TerrainMesh ToMesh() =>
            new(_positions.ToArray(), _normals.ToArray(), _colors.ToArray(), _indices.ToArray());
    }
}
