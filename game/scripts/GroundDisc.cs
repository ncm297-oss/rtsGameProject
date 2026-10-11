using System;
using Godot;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>A disc or ring drawn on the terrain (M4-VH3, BUG-0430): a zone's disc, an ability's radius or range ring.</summary>
/// <remarks>
/// The mesh is the terrain cells the disc covers (<see cref="GroundPatch.Collect"/>), each a quad on that cell's own top
/// surface plus <see cref="Lift"/>, and the shader cuts the circle out per pixel. So across a cliff the part on the plateau
/// lies on the plateau and the part below on the low ground, with the depth test on: a cliff in front hides what is behind
/// it, as it hides the ground. A flat plane at the centre's height sank under the higher ground and floated over the lower.
/// (A Godot <c>Decal</c> would also paint units and buildings standing in the storm, and a heightmap-sampling vertex shader
/// still stretches a sheet across each cliff; one quad per cell is exact, since a cell's top is one plane.)
/// The mesh is rebuilt only when the centre, radius, hole or lift changes; colours are shader parameters. A rebuild writes
/// an <see cref="ImmediateMesh"/> from a buffer kept between frames, so it allocates nothing once the buffer fits. View
/// state only.
/// </remarks>
public partial class GroundDisc : MeshInstance3D
{
    private static readonly StringName FillParam = "fill_color", RimParam = "rim_color", RimShareParam = "rim_share", ScaleParam = "scale";
    private static Shader? _shader;

    private readonly ImmediateMesh _mesh = new();
    private ShaderMaterial _material = null!;
    private GroundQuad[] _quads = Array.Empty<GroundQuad>();
    private Heightmap? _map;
    private Color _fill = new(-1f, -1f, -1f, -1f), _rim = new(-1f, -1f, -1f, -1f);
    private float _rimShare = -1f, _scale = 1f;

    /// <summary>A hidden disc with its own material; place it with <see cref="Place"/>.</summary>
    public GroundDisc()
    {
        _material = new ShaderMaterial { Shader = _shader ??= GD.Load<Shader>("res://shaders/ground_disc.gdshader") };
        Mesh = _mesh;
        MaterialOverride = _material;
        CastShadow = ShadowCastingSetting.Off;
        Visible = false;
    }

    /// <summary>The centre (sim x, y in meters) as last placed.</summary>
    public System.Numerics.Vector2 Center { get; private set; }

    /// <summary>The outer radius (m) as last placed; -1 before the first <see cref="Place"/>.</summary>
    public float Radius { get; private set; } = -1f;

    /// <summary>The radius (m) inside which nothing is drawn: 0 for a disc, the inner edge of a ring's band.</summary>
    public float Hole { get; private set; }

    /// <summary>Height over the terrain (m).</summary>
    public float Lift { get; private set; }

    /// <summary>Terrain cells in the mesh.</summary>
    public int Cells { get; private set; }

    /// <summary>Times the mesh was rebuilt.</summary>
    public int Rebuilds { get; private set; }

    /// <summary>
    /// Places the disc on <paramref name="map"/> at <paramref name="center"/> with <paramref name="radius"/> m, drawing nothing
    /// within <paramref name="hole"/> m of the centre (0 for a disc), <paramref name="lift"/> m over the ground; the node sits
    /// at the centre (its y on the terrain there + lift). Rebuilds the mesh only when any of them changed. Doesn't show it.
    /// </summary>
    public void Place(Heightmap map, System.Numerics.Vector2 center, float radius, float hole, float lift)
    {
        // BUG-0521 item 1: compare the hole as stored (clamped), so a ring whose band is wider than its radius (a negative
        // hole) that hasn't moved isn't rebuilt every frame.
        hole = Math.Max(0f, hole);
        if (ReferenceEquals(map, _map) && center == Center && radius == Radius && hole == Hole && lift == Lift) return;
        _map = map;
        Center = center;
        Radius = radius;
        Hole = hole;
        Lift = lift;
        float baseY = TerrainHeight.At(map, center.X, center.Y) + lift;
        Position = new Vector3(center.X, baseY, center.Y);
        int need = GroundPatch.MaxCells(radius);
        if (_quads.Length < need) _quads = new GroundQuad[need];
        Cells = GroundPatch.Collect(map, center.X, center.Y, radius, Hole, _quads);
        Rebuilds++;
        _mesh.ClearSurfaces();
        if (Cells == 0 || !(radius > 0f)) return;
        const float cs = MapConstants.CellSize;
        float inv = 1f / radius;
        _mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < Cells; i++)
        {
            GroundQuad q = _quads[i];
            // Relative to the node, so its position is the centre; the circle's UV is the offset over the radius.
            float x0 = q.X * cs - center.X, x1 = x0 + cs, z0 = q.Y * cs - center.Y, z1 = z0 + cs, y = lift - baseY;
            Vertex(x0, q.H00 + y, z0, inv);
            Vertex(x1, q.H10 + y, z0, inv);
            Vertex(x1, q.H11 + y, z1, inv);
            Vertex(x0, q.H00 + y, z0, inv);
            Vertex(x1, q.H11 + y, z1, inv);
            Vertex(x0, q.H01 + y, z1, inv);
        }
        _mesh.SurfaceEnd();
    }

    private void Vertex(float x, float y, float z, float inv)
    {
        _mesh.SurfaceSetUV(new Vector2(x * inv, z * inv));
        _mesh.SurfaceAddVertex(new Vector3(x, y, z));
    }

    /// <summary>Sets the fill (its alpha the fill's opacity; 0 for a ring), the rim and the rim band's share of the radius; writes only what changed.</summary>
    public void SetColors(Color fill, Color rim, float rimShare)
    {
        if (fill != _fill) _material.SetShaderParameter(FillParam, _fill = fill);
        if (rim != _rim) _material.SetShaderParameter(RimParam, _rim = rim);
        if (rimShare != _rimShare) _material.SetShaderParameter(RimShareParam, _rimShare = rimShare);
    }

    /// <summary>The share of <see cref="Radius"/> the circle is drawn at (M5-V1: a resolve flash grows through it); 1 by default.</summary>
    public float DrawnShare => _scale;

    /// <summary>Draws the circle at <paramref name="scale"/> of the meshed radius (clamped to [0.001, 1]) without rebuilding the mesh; writes the shader parameter only when it changed.</summary>
    public void SetScale(float scale)
    {
        scale = float.IsNaN(scale) ? 1f : Math.Clamp(scale, 0.001f, 1f);
        if (scale != _scale) _material.SetShaderParameter(ScaleParam, _scale = scale);
    }

    /// <summary>
    /// The drawn height (m) at ground point (x, z): the surface of the mesh's quad over that point, or NaN where the disc
    /// draws nothing (hidden, outside its drawn radius (<see cref="Radius"/> x <see cref="DrawnShare"/>) or in its hole, or no
    /// cell there). For tests and the QA oracle.
    /// </summary>
    public float SurfaceAt(float x, float z)
    {
        if (!Visible || _map == null) return float.NaN;
        float dx = x - Center.X, dz = z - Center.Y, d2 = dx * dx + dz * dz, drawn = Radius * _scale;
        if (d2 > drawn * drawn || d2 < Hole * Hole) return float.NaN;
        int cx = (int)MathF.Floor(x / MapConstants.CellSize), cy = (int)MathF.Floor(z / MapConstants.CellSize);
        for (int i = 0; i < Cells; i++)
            if (_quads[i].X == cx && _quads[i].Y == cy) return GroundPatch.HeightIn(_quads[i], x, z) + Lift;
        return float.NaN;
    }
}
