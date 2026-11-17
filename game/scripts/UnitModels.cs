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
/// M6-V24: a model file is one surface (its material named <c>team</c>) drawing one atlas whose alpha marks the team texels;
/// the file's extras carry the atlas layout (<see cref="AtlasOf"/>), which the material's <c>atlas_grid</c> takes, so the one
/// team material draws the whole figure. A file without the layout (built before M6-V24) is drawn as before: its team
/// surface in the player colour, its other surfaces with their own materials.
/// Clip maps (M6-V21): a model file carries its clip map (docs/04 state name -> library clip, the asset manifest's
/// <c>clips</c>) as glTF extras on its armature node, which Godot imports as the node's <c>extras</c> metadata. Each model
/// type gets its own small <see cref="AnimationLibrary"/> holding the shared library's <see cref="Animation"/>s under the
/// state names (no copy), so a player always plays <c>idle</c>, <c>walk</c>, <c>attack</c>, ... and the map decides which
/// motion that is: the Heavy Infantry's <c>attack</c> is the library's <c>attack_melee</c>. A state the map doesn't name
/// plays the library clip of that name (the pre-V21 rule); one the library lacks is left out, and the view plays idle.
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

    /// <summary>
    /// docs/04's clip names, in <see cref="Clip"/> order. M6-V23 (BUG-1441 item 4): the Casting state's slot is named after
    /// the library's one spell clip, <c>attack_cast</c>, so by the usual rule (a state the clip map does not name plays the
    /// library clip of its own name) every model type whose library holds that clip plays it while casting, with no
    /// re-import; a type without it plays idle (<see cref="UnitViews"/>' fallback).
    /// </summary>
    public static readonly StringName[] ClipNames = { "idle", "walk", "attack", "gather_gold", "gather_wood", "build", "attack_cast" };

    /// <summary>Clip indices into <see cref="ClipNames"/>.</summary>
    public enum Clip { Idle, Walk, Attack, GatherGold, GatherWood, Build, Cast }

    private static readonly Dictionary<string, PackedScene?> Loaded = new();
    private static AnimationLibrary? _library;
    private static bool _libraryTried;
    private static float[] _clipLength = Array.Empty<float>();
    // BUG-1350: held for the process (FogOfWar's shaders' rule), so no match start loads it behind a dying wrapper.
    private static Shader? _teamShader;

    private readonly PackedScene?[] _scenes;
    private readonly NodePath?[] _meshPath;
    private readonly int[] _teamSurface;
    private readonly ShaderMaterial?[][] _team;
    private readonly string[]?[] _clipMap;
    private readonly AnimationLibrary?[] _libs;
    private readonly float[]?[] _lengths;
    private readonly Atlas?[] _atlas;

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
        _clipMap = new string[n][];
        _libs = new AnimationLibrary?[n];
        _lengths = new float[n][];
        _atlas = new Atlas?[n];
        Shader shader = _teamShader ??= GD.Load<Shader>(TeamShaderPath);
        var missing = new List<string>();
        for (int t = 0; t < n; t++)
        {
            _team[t] = Array.Empty<ShaderMaterial?>();
            string path = PathFor(data.Units[t].Model);
            PackedScene? scene = Load(path, missing);
            if (scene == null || !FindTeam(scene, path, out NodePath meshPath, out int surface, out Texture2D? texture, out string[] clipMap, out Atlas? atlas)) continue;
            _scenes[t] = scene;
            _clipMap[t] = clipMap;
            _meshPath[t] = meshPath;
            _teamSurface[t] = surface;
            _atlas[t] = atlas;
            _team[t] = new ShaderMaterial?[colours.Length];
            for (int c = 0; c < colours.Length; c++)
            {
                var mat = new ShaderMaterial { Shader = shader };
                mat.SetShaderParameter("albedo_texture", texture!);
                mat.SetShaderParameter("team_colour", UnitViews.ColorFromRgb(colours[c]));
                // M6-V24: the one surface draws the atlas; its alpha says which texels take the player colour.
                mat.SetShaderParameter("use_atlas", atlas != null);
                if (atlas != null) mat.SetShaderParameter("atlas_grid", new Vector2(atlas.Grid.X, atlas.Grid.Y));
                _team[t][c] = mat;
                TeamMaterialCount++;
            }
            ModelTypes++;
        }
        if (missing.Count > 0)
            GD.PushWarning($"Unit models: {missing.Count} file(s) missing ({missing[0]}{(missing.Count > 1 ? ", ..." : "")}): those types draw the placeholder capsule.");
        if (ModelTypes > 0) LoadLibrary();
        // Each model type's library: the shared clips its map names, under the state names (made per Bind from the library
        // as it is now, so a test that swaps the library sees the swap).
        for (int t = 0; t < n && _library != null; t++)
        {
            if (_scenes[t] == null) continue;
            var lib = new AnimationLibrary();
            var lengths = new float[ClipNames.Length];
            for (int c = 0; c < ClipNames.Length; c++)
            {
                StringName source = _clipMap[t]![c];
                if (!_library.HasAnimation(source)) continue;
                Animation anim = _library.GetAnimation(source);
                lib.AddAnimation(ClipNames[c], anim);
                lengths[c] = (float)anim.Length;
            }
            _libs[t] = lib;
            _lengths[t] = lengths;
        }
        // M6-VH8 (BUG-1441 item 2): each model type's highest bone over every clip its players hold, for the overlays' line.
        var sampling = System.Diagnostics.Stopwatch.StartNew();
        _topBone = new float[n];
        _topBoneName = new string[n];
        _topBoneClip = new string[n];
        for (int t = 0; t < n; t++)
        {
            _topBoneName[t] = _topBoneClip[t] = "";
            _topBone[t] = float.NaN;
            if (_scenes[t] == null) continue;
            Rig? rig = RigOf(_scenes[t]!, PathFor(data.Units[t].Model));
            if (rig == null) continue; // no skeleton: NaN, and the view keeps the capsule's top
            (float top, string bone, string clip) = (float.MinValue, "", "");
            AnimationLibrary? lib = _libs[t];
            if (lib == null || lib.GetAnimationList().Count == 0) (top, bone) = ClipTop(rig, null);
            else
                foreach (StringName name in lib.GetAnimationList())
                {
                    (float y, string b) = ClipTop(rig, lib.GetAnimation(name));
                    if (y > top) (top, bone, clip) = (y, b, name.ToString());
                }
            _topBone[t] = top;
            _topBoneName[t] = bone;
            _topBoneClip[t] = clip;
        }
        TopBoneMs += sampling.Elapsed.TotalMilliseconds;
    }

    /// <summary>Milliseconds spent finding the types' highest bones since the program started (a test counter: the first Bind of a run samples, later ones read the cache).</summary>
    public static double TopBoneMs { get; private set; }

    private readonly float[] _topBone;
    private readonly string[] _topBoneName, _topBoneClip;

    /// <summary>
    /// Type <paramref name="type"/>'s highest bone in meters above its feet at scale 1 (the file's base figure), over every
    /// clip its players hold, sampled 17 times a clip (M6-VH8, BUG-1441 item 2; the rest pose when it has no clip); NaN for a
    /// capsule type or a model without a skeleton. Bones named <c>root</c> or with <c>ik</c> in their name, and the skeleton's parentless bone, don't count.
    /// </summary>
    public float TopBone(int type) => Has(type) ? _topBone[type] : float.NaN;

    /// <summary>The bone that reaches <see cref="TopBone"/>, and the state clip it does it in (empty for a capsule type or the rest pose).</summary>
    public (string Bone, string Clip) TopBoneOf(int type) => Has(type) ? (_topBoneName[type], _topBoneClip[type]) : ("", "");

    /// <summary>Samples a clip is read at for <see cref="TopBone"/> (at 0, 1/16, ... and 16/16 of its length).</summary>
    public const int TopBoneSamples = 16;

    // A model file's skeleton: its bones' names, parents, rest poses, the skeleton node's place in the model, and which
    // bones count for the top. Read once per file per run.
    private sealed class Rig
    {
        public string[] Names = Array.Empty<string>();
        public int[] Parent = Array.Empty<int>();
        public Transform3D[] Rest = Array.Empty<Transform3D>();
        public bool[] Counted = Array.Empty<bool>();
        public Transform3D ToModel = Transform3D.Identity;
        // The skeleton's content (bone names, parents, rest poses, place in the model): files with the same rig share
        // their clips' results (the eleven model files today are two or three rigs).
        public string Signature = "";
    }

    private static readonly Dictionary<string, Rig?> Rigs = new();
    // Per (rig signature, clip instance): the clip's highest counted bone and its name (a clip the tests swap in is another instance).
    private static readonly Dictionary<(string, ulong), (float Top, string Bone)> ClipTops = new();

    private static Rig? RigOf(PackedScene scene, string path)
    {
        if (Rigs.TryGetValue(path, out Rig? known)) return known;
        Node probe = scene.Instantiate();
        Rig? rig = null;
        Godot.Collections.Array<Node> found = probe.FindChildren("*", "Skeleton3D", true, false);
        if (found.Count > 0 && found[0] is Skeleton3D skel && skel.GetBoneCount() > 0)
        {
            int n = skel.GetBoneCount();
            rig = new Rig { Names = new string[n], Parent = new int[n], Rest = new Transform3D[n], Counted = new bool[n] };
            for (int b = 0; b < n; b++)
            {
                rig.Names[b] = skel.GetBoneName(b);
                rig.Parent[b] = skel.GetBoneParent(b);
                rig.Rest[b] = skel.GetBoneRest(b);
                string lower = rig.Names[b].ToLowerInvariant();
                rig.Counted[b] = rig.Parent[b] >= 0 && lower != "root" && !lower.Contains("ik", StringComparison.Ordinal);
            }
            // The skeleton's place under the model's root (the root's own transform is the view's: scale and offset).
            for (Node? x = skel; x != null && x != probe; x = x.GetParent())
                if (x is Node3D n3) rig.ToModel = n3.Transform * rig.ToModel;
            var sig = new System.Text.StringBuilder();
            sig.Append(rig.ToModel.ToString());
            for (int b = 0; b < n; b++) sig.Append('|').Append(rig.Names[b]).Append(',').Append(rig.Parent[b]).Append(',').Append(rig.Rest[b].ToString());
            rig.Signature = sig.ToString();
        }
        probe.Free();
        Rigs[path] = rig;
        return rig;
    }

    // The highest counted bone (model space, scale 1) over TopBoneSamples + 1 poses of clip `anim` on `rig` (the rest pose
    // for null). The pose is the clip's track values where it has them, else the bone's rest (a fresh player's pose).
    private static (float Top, string Bone) ClipTop(Rig rig, Animation? anim)
    {
        (string, ulong) key = (rig.Signature, anim?.GetInstanceId() ?? 0);
        if (anim != null && ClipTops.TryGetValue(key, out var known)) return known;
        int nb = rig.Names.Length;
        int tracks = anim?.GetTrackCount() ?? 0;
        var trackBone = new int[tracks];
        for (int t = 0; t < tracks; t++)
        {
            trackBone[t] = -1;
            Animation.TrackType type = anim!.TrackGetType(t);
            if (type is not (Animation.TrackType.Position3D or Animation.TrackType.Rotation3D or Animation.TrackType.Scale3D)) continue;
            NodePath path = anim.TrackGetPath(t);
            if (path.GetSubNameCount() > 0) trackBone[t] = Array.IndexOf(rig.Names, path.GetSubName(0).ToString());
        }
        var pos = new Vector3[nb];
        var rot = new Quaternion[nb];
        var scale = new Vector3[nb];
        var global = new Transform3D[nb];
        var done = new bool[nb];
        float top = float.MinValue;
        string bone = "";
        int samples = anim == null ? 0 : TopBoneSamples;
        for (int k = 0; k <= samples; k++)
        {
            double time = anim == null ? 0.0 : anim.Length * k / samples;
            for (int b = 0; b < nb; b++)
            {
                pos[b] = rig.Rest[b].Origin;
                rot[b] = rig.Rest[b].Basis.GetRotationQuaternion();
                scale[b] = rig.Rest[b].Basis.Scale;
                done[b] = false;
            }
            for (int t = 0; t < tracks; t++)
            {
                int b = trackBone[t];
                if (b < 0) continue;
                switch (anim!.TrackGetType(t))
                {
                    case Animation.TrackType.Position3D: pos[b] = anim.PositionTrackInterpolate(t, time); break;
                    case Animation.TrackType.Rotation3D: rot[b] = anim.RotationTrackInterpolate(t, time); break;
                    default: scale[b] = anim.ScaleTrackInterpolate(t, time); break;
                }
            }
            for (int b = 0; b < nb; b++)
            {
                if (!rig.Counted[b]) continue;
                float y = (rig.ToModel * Global(rig, b, pos, rot, scale, global, done)).Origin.Y;
                if (y > top) (top, bone) = (y, rig.Names[b]);
            }
        }
        if (anim != null) ClipTops[key] = (top, bone);
        return (top, bone);
    }

    // Bone b's pose in skeleton space: its parent's times its own (Godot's bone pose: rotation then scale, at the position).
    private static Transform3D Global(Rig rig, int b, Vector3[] pos, Quaternion[] rot, Vector3[] scale, Transform3D[] global, bool[] done)
    {
        if (done[b]) return global[b];
        var local = new Transform3D(new Basis(rot[b]) * Basis.FromScale(scale[b]), pos[b]);
        int parent = rig.Parent[b];
        global[b] = parent >= 0 ? Global(rig, parent, pos, rot, scale, global, done) * local : local;
        done[b] = true;
        return global[b];
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

    /// <summary>The library clip type <paramref name="type"/> plays for <paramref name="clip"/> (M6-V21: its file's clip map, else the state's own name), or null for a capsule type.</summary>
    public string? ClipSource(int type, Clip clip) => Has(type) ? _clipMap[type]![(int)clip] : null;

    /// <summary>Whether type <paramref name="type"/>'s players hold clip <paramref name="clip"/> (its source is in the library).</summary>
    public bool HasClip(int type, Clip clip) => Has(type) && _libs[type]?.HasAnimation(ClipNames[(int)clip]) == true;

    /// <summary>The length in seconds of what type <paramref name="type"/> plays for <paramref name="clip"/> (0 when it has none).</summary>
    public float ClipLength(int type, Clip clip) => Has(type) && _lengths[type] is float[] l ? l[(int)clip] : 0f;

    /// <summary>Type <paramref name="type"/>'s animation library (the shared clips under the state names), or null.</summary>
    public AnimationLibrary? LibraryOf(int type) => Has(type) ? _libs[type] : null;

    /// <summary>The team material of type <paramref name="type"/> in colour <paramref name="colour"/>, or null.</summary>
    public ShaderMaterial? TeamMaterial(int type, int colour) =>
        Has(type) && (uint)colour < (uint)_team[type].Length ? _team[type][colour] : null;

    /// <summary>The surface index of type <paramref name="type"/>'s team material on its mesh (M6-V24: 0, the mesh's one surface).</summary>
    public int TeamSurface(int type) => _teamSurface[type];

    /// <summary>Type <paramref name="type"/>'s atlas layout (M6-V24, read from its file's extras), or null for a capsule type or a file from before M6-V24.</summary>
    public Atlas? AtlasOf(int type) => Has(type) ? _atlas[type] : null;

    /// <summary>A new instance of type <paramref name="type"/>'s model with an <see cref="AnimationPlayer"/> on the shared library.</summary>
    public Node3D Instantiate(int type, out MeshInstance3D mesh, out AnimationPlayer player)
    {
        var root = _scenes[type]!.Instantiate<Node3D>();
        root.Name = "Model";
        mesh = root.GetNode<MeshInstance3D>(_meshPath[type]!);
        mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        // Manual: UnitViews' crowd schedule advances it (M6-VH7, CrowdAnimator), never Godot's own process step.
        player = new AnimationPlayer { Name = "AnimationPlayer", PlaybackDefaultBlendTime = 0.15, CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual };
        root.AddChild(player);
        if (_libs[type] is AnimationLibrary lib) player.AddAnimationLibrary("", lib);
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

    // The model's first mesh with a "team" surface: its path from the root, the surface, and the texture the team shader
    // shades; and its clip map (M6-V21): per Clip, the library clip its extras name, else the state's own name.
    private static bool FindTeam(PackedScene scene, string path, out NodePath meshPath, out int surface, out Texture2D? texture, out string[] clipMap, out Atlas? atlas)
    {
        meshPath = new NodePath();
        surface = -1;
        texture = null;
        Node probe = scene.Instantiate();
        Godot.Collections.Dictionary? extras = ReadExtras(probe);
        clipMap = ReadClipMap(extras);
        atlas = ReadAtlas(extras);
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
        // Once per file per run, like the loader's other warnings: FindTeam runs at every Bind (BUG-1322).
        if (surface < 0 && NoTeamWarned.Add(path))
        {
            NoTeamWarnings++;
            GD.PushWarning($"Unit model {path} has no '{TeamMaterialName}' material: drawing the placeholder capsule.");
        }
        return surface >= 0;
    }

    private static readonly HashSet<string> NoTeamWarned = new();

    /// <summary>The name of the glTF extras key that holds a model's clip map (M6-V21, tools/art/build_unit.py).</summary>
    public const string ClipMapKey = "clips";

    // The imported glTF extras of the first node that has them (the armature's: the builder writes no other extras), or null.
    private static Godot.Collections.Dictionary? ReadExtras(Node root)
    {
        Node? holder = root.HasMeta("extras") ? root : null;
        if (holder == null)
            foreach (Node n in root.FindChildren("*", "", true, false))
                if (n.HasMeta("extras")) { holder = n; break; }
        return holder?.GetMeta("extras").Obj as Godot.Collections.Dictionary;
    }

    // The clip map in the extras; a file without one, or an entry that is not a string, keeps the state's own name.
    private static string[] ReadClipMap(Godot.Collections.Dictionary? extras)
    {
        var map = new string[ClipNames.Length];
        for (int c = 0; c < map.Length; c++) map[c] = ClipNames[c];
        if (extras == null || !extras.TryGetValue(ClipMapKey, out Variant clipsVar) || clipsVar.Obj is not Godot.Collections.Dictionary clips) return map;
        for (int c = 0; c < map.Length; c++)
            if (clips.TryGetValue(ClipNames[c].ToString(), out Variant v) && v.VariantType == Variant.Type.String) map[c] = v.AsString();
        return map;
    }

    /// <summary>The name of the glTF extras key that holds a model's atlas layout (M6-V24, tools/art/build_unit.py).</summary>
    public const string AtlasKey = "atlas";

    /// <summary>The name of the atlas tile that holds the props' flat colours (M6-V24).</summary>
    public const string PropsTile = "props";

    /// <summary>A model's atlas layout (M6-V24): its grid of tiles and which tile holds what (a role texture or the props' colours).</summary>
    public sealed class Atlas
    {
        /// <summary>Columns and rows of equal tiles.</summary>
        public Vector2I Grid { get; init; }

        /// <summary>Tile name (<c>team</c>, <c>outfit</c>, <c>skin</c>, <c>head</c>, <c>hair</c>, <see cref="PropsTile"/>) to its column and row from the top-left.</summary>
        public Dictionary<string, Vector2I> Tiles { get; init; } = new();

        /// <summary>Tile <paramref name="name"/>'s rectangle in UV space (u right, v down, as imported), or an empty one when the atlas has no such tile.</summary>
        public Rect2 TileRect(string name) =>
            Tiles.TryGetValue(name, out Vector2I t) ? new Rect2((float)t.X / Grid.X, (float)t.Y / Grid.Y, 1f / Grid.X, 1f / Grid.Y) : new Rect2();

        /// <summary>The name of the tile UV <paramref name="uv"/> lies in, or "" when no named tile holds it (tests and tools: a face's role).</summary>
        public string TileAt(Vector2 uv)
        {
            var cell = new Vector2I((int)Math.Floor(uv.X * Grid.X), (int)Math.Floor(uv.Y * Grid.Y));
            foreach (KeyValuePair<string, Vector2I> tile in Tiles)
                if (tile.Value == cell) return tile.Key;
            return "";
        }

        /// <summary>Whether UV <paramref name="uv"/> reads a prop's own colour (the <see cref="PropsTile"/>): a figure measured without its props skips those vertices.</summary>
        public bool IsProp(Vector2 uv) => TileAt(uv) == PropsTile;
    }

    // The atlas layout in the extras ({grid: [c, r], tiles: {name: [c, r]}}), or null for a file from before M6-V24.
    private static Atlas? ReadAtlas(Godot.Collections.Dictionary? extras)
    {
        if (extras == null || !extras.TryGetValue(AtlasKey, out Variant atlasVar) || atlasVar.Obj is not Godot.Collections.Dictionary atlas) return null;
        if (!atlas.TryGetValue("grid", out Variant gridVar) || gridVar.Obj is not Godot.Collections.Array grid || grid.Count != 2) return null;
        var result = new Atlas { Grid = new Vector2I(Math.Max(1, grid[0].AsInt32()), Math.Max(1, grid[1].AsInt32())) };
        if (atlas.TryGetValue("tiles", out Variant tilesVar) && tilesVar.Obj is Godot.Collections.Dictionary tiles)
            foreach (var (key, value) in tiles)
                if (value.Obj is Godot.Collections.Array cr && cr.Count == 2) result.Tiles[key.AsString()] = new Vector2I(cr[0].AsInt32(), cr[1].AsInt32());
        return result;
    }

    /// <summary>"No team material" warnings printed since the program started (a test counter: one per file per run, BUG-1322).</summary>
    public static int NoTeamWarnings { get; private set; }

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
