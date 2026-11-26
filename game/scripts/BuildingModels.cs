using System;
using System.Collections.Generic;
using Godot;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>The building models of one <see cref="BuildingViews.Bind"/> (M6-V26): per building type its imported static model, if its file loads, fitted to the type's footprint, and its team materials.</summary>
/// <remarks>
/// A type's file is found by its id (docs/01 2026-11-18 (b) (e)): <c>{ModelRoot}{faction id}/{building id}/{building id}.glb</c>,
/// the shape of a unit's <c>model</c> path; no building id is named in code and the data has no <c>model</c> field. A file
/// that is missing (the normal case for a type the art pass hasn't reached), a Git LFS pointer, not a glTF binary, without
/// a <c>team</c> material or without its atlas layout gives no model: the type keeps its box, and a problem file prints one
/// warning line (never an <c>ERROR</c> line) once per run, as <see cref="UnitModels"/>.
/// A building file is what <c>tools/art/build_building.py</c> writes: one mesh, one surface, one material named <c>team</c>
/// drawing one atlas of flat-colour tiles whose alpha marks the team tile, the layout in the file's extras, exactly as a unit
/// model's since M6-V24, so <c>team_colour.gdshader</c> draws it unchanged. The mesh is drawn on its own
/// <see cref="MeshInstance3D"/> (no instanced scene), so a building view stays one root, a box, a model and its bar.
/// The fit (docs/04 "Scale and dimensions"): one uniform scale, the largest that keeps the model's ground plan
/// <see cref="FitMargin"/> inside each edge of the footprint (cells x <see cref="MapConstants.CellSize"/> from the data), the
/// plan's centre on the footprint's centre and its lowest point on the ground. Then the data's <c>scale</c> multiplies the
/// height only (M6-V30, as a box's since M7-V4): the Teblor's 1.25 stands a quarter taller and no wider; at scale 1 the fit
/// is the same transform as before. Everything read from the fit (the top, the faces a click is tested against, the bar,
/// a tower's shot height, the remembered model) is the drawn shape.
/// Materials, made here once: per type and player the team material (the player colour on the team tiles) and the
/// remembered (ghost) material, the same darkened by <see cref="FogView.ExploredBrightness"/> as a ghost box's colour is.
/// </remarks>
public sealed class BuildingModels
{
    /// <summary>The folder building models live under (a test may point it at a scratch folder).</summary>
    public static string ModelRoot { get; set; } = "res://assets/models/";

    /// <summary>The gap in meters the fitted model keeps from each edge of its footprint (docs/04: "a small margin").</summary>
    public const float FitMargin = 0.25f;

    // Per file path, what was read from it once this run (null: no model), so a later Bind reads nothing.
    private sealed class Source
    {
        public required Mesh Mesh;
        public required Transform3D MeshToRoot;
        public required Texture2D Texture;
        public required UnitModels.Atlas Atlas;
    }

    private static readonly Dictionary<string, Source?> Loaded = new();
    private static Shader? _teamShader;

    private readonly Source?[] _source;
    private readonly Transform3D[] _fit;
    private readonly Aabb[] _bounds;
    private readonly ShaderMaterial?[][] _team, _ghost;
    private readonly Vector3[]?[] _faces;

    /// <summary>Types with a model.</summary>
    public int ModelTypes { get; }

    /// <summary>Materials made (types with a model times colours, team and ghost).</summary>
    public int MaterialCount { get; }

    /// <summary>Problem warnings printed since the program started (a test counter: one per file per run).</summary>
    public static int Warnings { get; private set; }

    /// <summary>Loads every type's model and makes its materials, one team and one ghost material per entry of <paramref name="colours"/> (0xRRGGBB).</summary>
    public BuildingModels(GameData data, uint[] colours)
    {
        int n = data.Buildings.Length;
        _source = new Source?[n];
        _fit = new Transform3D[n];
        _bounds = new Aabb[n];
        _team = new ShaderMaterial?[n][];
        _ghost = new ShaderMaterial?[n][];
        _faces = new Vector3[n][];
        Shader shader = _teamShader ??= GD.Load<Shader>(UnitModels.TeamShaderPath);
        // The ghost box's colour is the player colour x ExploredBrightness in sRGB; the shader multiplies linear albedo.
        float k = FogView.ExploredBrightness;
        float ghostBrightness = new Color(k, k, k).SrgbToLinear().R;
        var missing = new List<string>();
        for (int t = 0; t < n; t++)
        {
            _team[t] = _ghost[t] = Array.Empty<ShaderMaterial?>();
            BuildingDef def = data.Buildings[t];
            Source? src = Load(PathFor(data, def), missing);
            if (src == null) continue;
            _source[t] = src;
            (_fit[t], _bounds[t]) = FitTo(def, src);
            _team[t] = new ShaderMaterial?[colours.Length];
            _ghost[t] = new ShaderMaterial?[colours.Length];
            for (int c = 0; c < colours.Length; c++)
            {
                _team[t][c] = Material(shader, src, UnitViews.ColorFromRgb(colours[c]), 1f);
                _ghost[t][c] = Material(shader, src, UnitViews.ColorFromRgb(colours[c]), ghostBrightness);
                MaterialCount += 2;
            }
            ModelTypes++;
        }
        if (missing.Count > 0)
            GD.Print($"Building models: {missing.Count} type(s) without a model file draw the placeholder box.");
    }

    /// <summary>A building type's model file: <c>{ModelRoot}{faction id}/{building id}/{building id}.glb</c>.</summary>
    public static string PathFor(GameData data, BuildingDef def) => $"{ModelRoot}{data.Factions[def.Faction].Key}/{def.Key}/{def.Key}.glb";

    /// <summary>True when building type <paramref name="type"/> is drawn as a model when finished.</summary>
    public bool Has(int type) => (uint)type < (uint)_source.Length && _source[type] != null;

    /// <summary>Type <paramref name="type"/>'s mesh, or null for a box type.</summary>
    public Mesh? MeshOf(int type) => Has(type) ? _source[type]!.Mesh : null;

    /// <summary>The transform from type <paramref name="type"/>'s mesh to its building's root (origin at the footprint's centre on the ground): the fit.</summary>
    public Transform3D Fit(int type) => Has(type) ? _fit[type] : Transform3D.Identity;

    /// <summary>Type <paramref name="type"/>'s fitted bounds in its building's root space (meters; y up from the ground).</summary>
    public Aabb FittedBounds(int type) => Has(type) ? _bounds[type] : new Aabb();

    /// <summary>The height of type <paramref name="type"/>'s fitted model's highest point above the ground, in meters (0 for a box type).</summary>
    public float Top(int type) => Has(type) ? _bounds[type].End.Y : 0f;

    /// <summary>
    /// Type <paramref name="type"/>'s triangles as fitted, in its building's root space (three corners a triangle; empty for a
    /// box type): what a click's ray is tested against when a unit behind the building may be hidden by it (M6-VH9, BUG-1630).
    /// Made once per type at the first call.
    /// </summary>
    public Vector3[] FittedFaces(int type)
    {
        if (!Has(type)) return Array.Empty<Vector3>();
        if (_faces[type] is Vector3[] known) return known;
        Vector3[] faces = _source[type]!.Mesh.GetFaces();
        for (int k = 0; k < faces.Length; k++) faces[k] = _fit[type] * faces[k];
        _faces[type] = faces;
        return faces;
    }

    /// <summary>
    /// The distance along the ray from <paramref name="origin"/> (in the building's root space) along the unit
    /// <paramref name="direction"/> to type <paramref name="type"/>'s nearest fitted triangle, or +infinity (a miss, or a box
    /// type). Allocation-free once <see cref="FittedFaces"/> is made.
    /// </summary>
    public float RayDistance(int type, Vector3 origin, Vector3 direction)
    {
        Vector3[] faces = FittedFaces(type);
        float best = float.PositiveInfinity;
        for (int i = 0; i + 2 < faces.Length; i += 3)
        {
            // Moller-Trumbore, both faces of a triangle.
            Vector3 a = faces[i], e1 = faces[i + 1] - a, e2 = faces[i + 2] - a;
            Vector3 p = direction.Cross(e2);
            float det = e1.Dot(p);
            if (Math.Abs(det) < 1e-9f) continue;
            float inv = 1f / det;
            Vector3 s = origin - a;
            float u = s.Dot(p) * inv;
            if (u < 0f || u > 1f) continue;
            Vector3 q = s.Cross(e1);
            float v = direction.Dot(q) * inv;
            if (v < 0f || u + v > 1f) continue;
            float t = e2.Dot(q) * inv;
            if (t > 0f && t < best) best = t;
        }
        return best;
    }

    /// <summary>The team material of type <paramref name="type"/> in colour <paramref name="colour"/>, or null.</summary>
    public ShaderMaterial? TeamMaterial(int type, int colour) =>
        Has(type) && (uint)colour < (uint)_team[type].Length ? _team[type][colour] : null;

    /// <summary>The remembered (ghost) material of type <paramref name="type"/> in colour <paramref name="colour"/>: the team material darkened as a ghost box is.</summary>
    public ShaderMaterial? GhostMaterial(int type, int colour) =>
        Has(type) && (uint)colour < (uint)_ghost[type].Length ? _ghost[type][colour] : null;

    /// <summary>Type <paramref name="type"/>'s atlas layout (from its file's extras), or null for a box type.</summary>
    public UnitModels.Atlas? AtlasOf(int type) => Has(type) ? _source[type]!.Atlas : null;

    /// <summary>Type <paramref name="type"/>'s atlas texture, or null for a box type.</summary>
    public Texture2D? TextureOf(int type) => Has(type) ? _source[type]!.Texture : null;

    // The largest uniform scale that keeps the plan FitMargin inside each footprint edge; plan centred, lowest point on the
    // ground. M6-V30: the height then times the data scale (docs/01 2026-11-26 (3), M7-V4's box rule): a scaled model stands
    // taller and never wider than its footprint. At scale 1 the product is exactly s, so a scale-1 model is unchanged.
    private static (Transform3D Fit, Aabb Bounds) FitTo(BuildingDef def, Source src)
    {
        Aabb b = src.MeshToRoot * src.Mesh.GetAabb();
        float w = def.FootprintWidth * MapConstants.CellSize - 2f * FitMargin;
        float d = def.FootprintHeight * MapConstants.CellSize - 2f * FitMargin;
        float s = Math.Min(w / Math.Max(b.Size.X, 1e-4f), d / Math.Max(b.Size.Z, 1e-4f));
        float sy = s * def.Scale;
        Vector3 centre = b.GetCenter();
        var place = new Transform3D(Basis.FromScale(new Vector3(s, sy, s)), new Vector3(-centre.X * s, -b.Position.Y * sy, -centre.Z * s));
        Transform3D fit = place * src.MeshToRoot;
        return (fit, fit * src.Mesh.GetAabb());
    }

    private static ShaderMaterial Material(Shader shader, Source src, Color colour, float brightness)
    {
        var mat = new ShaderMaterial { Shader = shader };
        mat.SetShaderParameter("albedo_texture", src.Texture);
        mat.SetShaderParameter("team_colour", colour);
        mat.SetShaderParameter("use_atlas", true);
        mat.SetShaderParameter("atlas_grid", new Vector2(src.Atlas.Grid.X, src.Atlas.Grid.Y));
        mat.SetShaderParameter("brightness", brightness);
        return mat;
    }

    // The model at path, or null: a missing file is counted (the normal case); any other problem prints one warning. A
    // pointer or a non-glTF file never reaches the loader, whose failed import would print an engine ERROR line (BUG-0860).
    private static Source? Load(string path, List<string> missing)
    {
        if (Loaded.TryGetValue(path, out Source? known)) return known;
        string? why = UnitModels.Problem(path);
        Source? src = null;
        if (why == null)
        {
            PackedScene? scene = ResourceLoader.Load<PackedScene>(path);
            if (scene == null) why = "did not load";
            else src = Read(scene, out why);
        }
        if (why == UnitModels.Missing) missing.Add(path);
        else if (why != null)
        {
            Warnings++;
            GD.PushWarning($"Building model {path} {why}: drawing the placeholder box.");
        }
        Loaded[path] = src;
        return src;
    }

    // The scene's first mesh with a "team" surface (its place under the scene's root, its texture) and the atlas layout.
    private static Source? Read(PackedScene scene, out string? why)
    {
        Node probe = scene.Instantiate();
        Source? src = null;
        why = $"has no '{UnitModels.TeamMaterialName}' material";
        UnitModels.Atlas? atlas = UnitModels.ReadAtlas(UnitModels.ReadExtras(probe));
        foreach (Node n in probe.FindChildren("*", "MeshInstance3D", true, false))
        {
            var mi = (MeshInstance3D)n;
            Mesh? mesh = mi.Mesh;
            if (mesh == null || mesh.GetSurfaceCount() == 0) continue;
            if (mesh.SurfaceGetMaterial(0) is not BaseMaterial3D m || m.ResourceName != UnitModels.TeamMaterialName || m.AlbedoTexture == null) continue;
            if (atlas == null)
            {
                why = "has no atlas layout in its extras";
                break;
            }
            Transform3D toRoot = Transform3D.Identity;
            for (Node? x = mi; x != null && x != probe; x = x.GetParent())
                if (x is Node3D n3) toRoot = n3.Transform * toRoot;
            src = new Source { Mesh = mesh, MeshToRoot = toRoot, Texture = m.AlbedoTexture, Atlas = atlas };
            why = null;
            break;
        }
        probe.Free();
        return src;
    }
}
