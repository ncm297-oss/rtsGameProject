using System;
using System.Collections.Generic;
using Godot;
using Rts.Sim.Data;

namespace Rts.Game;

/// <summary>The unit models of one <see cref="UnitViews.Bind"/> (M6-V20): per unit type its imported model, if its file loads, and its team materials.</summary>
/// <remarks>
/// A type's file is <c>{ModelRoot}{model}/{id}.glb</c>, where <c>model</c> is the data's <c>model</c> field (a path stem such as
/// <c>malazan/malazan_laborer</c>) and <c>id</c> its last segment (docs/04 "Directory layout"); no unit id is named in code. A
/// file that is missing, a Git LFS pointer (a clone without <c>git lfs pull</c>, BUG-0860), not a glTF binary, or without a
/// <c>team</c> material gives no model: the type keeps its capsule, and one warning line (never an <c>ERROR</c> line) says why,
/// once per file per run. The shared animation library (<see cref="LibraryPath"/>, imported as an <c>AnimationLibrary</c>)
/// is loaded once per run; its clips loop. The team surface of each model is drawn by <c>team_colour.gdshader</c>: one
/// material per type and player (the outfit's shading times the player colour), made here, never per frame.
/// </remarks>
public sealed class UnitModels
{
    /// <summary>The folder unit models live under (a test may point it at a scratch folder).</summary>
    public static string ModelRoot { get; set; } = "res://assets/models/";

    /// <summary>The shared animation library of the Universal humanoid rig (docs/04 "Animation setup").</summary>
    public const string LibraryPath = "res://assets/animations/universal_humanoid.glb";

    /// <summary>The team-colour shader every model's <c>team</c> surface is drawn with.</summary>
    public const string TeamShaderPath = "res://shaders/team_colour.gdshader";

    /// <summary>The material name that marks a model's team surface (docs/04 "Team color").</summary>
    public const string TeamMaterialName = "team";

    /// <summary>docs/04's clip names, in <see cref="Clip"/> order.</summary>
    public static readonly StringName[] ClipNames = { "idle", "walk", "attack", "gather_gold", "gather_wood", "build" };

    /// <summary>Clip indices into <see cref="ClipNames"/>.</summary>
    public enum Clip { Idle, Walk, Attack, GatherGold, GatherWood, Build }

    private static readonly Dictionary<string, PackedScene?> Loaded = new();
    private static AnimationLibrary? _library;
    private static bool _libraryTried;
    private static float[] _clipLength = Array.Empty<float>();

    private readonly PackedScene?[] _scenes;
    private readonly NodePath?[] _meshPath;
    private readonly int[] _teamSurface;
    private readonly ShaderMaterial?[][] _team;

    /// <summary>Types with a model.</summary>
    public int ModelTypes { get; }

    /// <summary>Team materials made (types with a model times colours).</summary>
    public int TeamMaterialCount { get; }

    /// <summary>Loads every type's model and makes its team materials, one per entry of <paramref name="colours"/> (0xRRGGBB).</summary>
    public UnitModels(GameData data, uint[] colours)
    {
        int n = data.Units.Length;
        _scenes = new PackedScene?[n];
        _meshPath = new NodePath?[n];
        _teamSurface = new int[n];
        _team = new ShaderMaterial?[n][];
        var shader = GD.Load<Shader>(TeamShaderPath);
        var missing = new List<string>();
        for (int t = 0; t < n; t++)
        {
            _team[t] = Array.Empty<ShaderMaterial?>();
            string path = PathFor(data.Units[t].Model);
            PackedScene? scene = Load(path, missing);
            if (scene == null || !FindTeam(scene, path, out NodePath meshPath, out int surface, out Texture2D? texture)) continue;
            _scenes[t] = scene;
            _meshPath[t] = meshPath;
            _teamSurface[t] = surface;
            _team[t] = new ShaderMaterial?[colours.Length];
            for (int c = 0; c < colours.Length; c++)
            {
                var mat = new ShaderMaterial { Shader = shader };
                mat.SetShaderParameter("albedo_texture", texture!);
                mat.SetShaderParameter("team_colour", UnitViews.ColorFromRgb(colours[c]));
                _team[t][c] = mat;
                TeamMaterialCount++;
            }
            ModelTypes++;
        }
        if (missing.Count > 0)
            GD.PushWarning($"Unit models: {missing.Count} file(s) missing ({missing[0]}{(missing.Count > 1 ? ", ..." : "")}): those types draw the placeholder capsule.");
        if (ModelTypes > 0) LoadLibrary();
    }

    /// <summary>A unit model's file for a data <c>model</c> stem: <c>{ModelRoot}{model}/{last segment}.glb</c>.</summary>
    public static string PathFor(string model) => $"{ModelRoot}{model}/{model[(model.LastIndexOf('/') + 1)..]}.glb";

    /// <summary>True when unit type <paramref name="type"/> is drawn as a model.</summary>
    public bool Has(int type) => (uint)type < (uint)_scenes.Length && _scenes[type] != null;

    /// <summary>The shared animation library, or null when it didn't load (models then stand in their bind pose).</summary>
    public static AnimationLibrary? Library => _library;

    /// <summary>Whether the library holds clip <paramref name="clip"/>.</summary>
    public static bool HasClip(Clip clip) => _library != null && _library.HasAnimation(ClipNames[(int)clip]);

    /// <summary>Clip <paramref name="clip"/>'s length in seconds (0 when the library lacks it).</summary>
    public static float ClipLength(Clip clip) => (int)clip < _clipLength.Length ? _clipLength[(int)clip] : 0f;

    /// <summary>The team material of type <paramref name="type"/> in colour <paramref name="colour"/>, or null.</summary>
    public ShaderMaterial? TeamMaterial(int type, int colour) =>
        Has(type) && (uint)colour < (uint)_team[type].Length ? _team[type][colour] : null;

    /// <summary>The surface index of type <paramref name="type"/>'s team material on its mesh.</summary>
    public int TeamSurface(int type) => _teamSurface[type];

    /// <summary>A new instance of type <paramref name="type"/>'s model with an <see cref="AnimationPlayer"/> on the shared library.</summary>
    public Node3D Instantiate(int type, out MeshInstance3D mesh, out AnimationPlayer player)
    {
        var root = _scenes[type]!.Instantiate<Node3D>();
        root.Name = "Model";
        mesh = root.GetNode<MeshInstance3D>(_meshPath[type]!);
        mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        player = new AnimationPlayer { Name = "AnimationPlayer", PlaybackDefaultBlendTime = 0.15 };
        root.AddChild(player);
        if (_library != null) player.AddAnimationLibrary("", _library);
        return root;
    }

    // The scene at path, or null with one warning: a missing file, an LFS pointer or a non-glTF file never reaches GD.Load,
    // whose failed import would print an engine ERROR line (BUG-0860's lesson).
    // A missing file is the normal case for a type the art pass hasn't reached: those are counted into one line per run.
    private static PackedScene? Load(string path, List<string> missing)
    {
        if (Loaded.TryGetValue(path, out PackedScene? known)) return known;
        string? why = Problem(path);
        PackedScene? scene = null;
        if (why == null)
        {
            scene = ResourceLoader.Load<PackedScene>(path);
            if (scene == null) why = "did not load";
        }
        if (why == Missing) missing.Add(path);
        else if (why != null) GD.PushWarning($"Unit model {path} {why}: drawing the placeholder capsule.");
        Loaded[path] = scene;
        return scene;
    }

    private const string Missing = "is missing";

    private static string? Problem(string path)
    {
        if (FileAccess.FileExists(path))
        {
            using FileAccess? f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            byte[] head = f == null ? Array.Empty<byte>() : f.GetBuffer(Math.Min(24, (long)f.GetLength()));
            string text = System.Text.Encoding.ASCII.GetString(head);
            if (text.StartsWith("version https://git-lfs", StringComparison.Ordinal)) return "is a Git LFS pointer (run git lfs pull and re-import)";
            if (!text.StartsWith("glTF", StringComparison.Ordinal)) return "is not a glTF binary";
        }
        // An exported build has no source file, only the import: ResourceLoader knows it.
        return ResourceLoader.Exists(path) ? null : Missing;
    }

    // The model's first mesh with a "team" surface: its path from the root, the surface, and the texture the team shader shades.
    private static bool FindTeam(PackedScene scene, string path, out NodePath meshPath, out int surface, out Texture2D? texture)
    {
        meshPath = new NodePath();
        surface = -1;
        texture = null;
        Node probe = scene.Instantiate();
        foreach (Node n in probe.FindChildren("*", "MeshInstance3D", true, false))
        {
            Mesh? mesh = ((MeshInstance3D)n).Mesh;
            for (int s = 0; mesh != null && s < mesh.GetSurfaceCount(); s++)
            {
                if (mesh.SurfaceGetMaterial(s) is not BaseMaterial3D m || m.ResourceName != TeamMaterialName) continue;
                meshPath = probe.GetPathTo(n);
                surface = s;
                texture = m.AlbedoTexture;
                break;
            }
            if (surface >= 0) break;
        }
        probe.Free();
        if (surface < 0) GD.PushWarning($"Unit model {path} has no '{TeamMaterialName}' material: drawing the placeholder capsule.");
        return surface >= 0;
    }

    private static void LoadLibrary()
    {
        if (_libraryTried) return;
        _libraryTried = true;
        string? why = Problem(LibraryPath);
        if (why == null) _library = ResourceLoader.Load<AnimationLibrary>(LibraryPath);
        if (_library == null)
        {
            GD.PushWarning($"Animation library {LibraryPath} {why ?? "did not load"}: unit models stand still.");
            return;
        }
        _clipLength = new float[ClipNames.Length];
        for (int c = 0; c < ClipNames.Length; c++)
            if (_library.HasAnimation(ClipNames[c])) _clipLength[c] = (float)_library.GetAnimation(ClipNames[c]).Length;
    }
}
