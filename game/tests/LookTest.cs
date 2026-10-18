using System;
using Godot;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// The art look test (M6-V10): the owner's humanoid stack (a Quaternius Universal Base Character playing a Universal Animation
/// Library clip) beside a stylized stand-in (an Ultimate Fantasy RTS building) and today's unit capsule, on flat ground under the
/// real <see cref="RtsCamera"/> at the default match zoom and pitch.
/// </summary>
/// <remarks>
/// Headless (scene loop): checks the three figures have meshes, the UAL clips bind to the character's skeleton and advance (idle,
/// then the in-place walk), prints the Universal character's triangle count, height and material names, and prints PASS.
/// Windowed: <c>&amp; $env:GODOT --path game --resolution 1600x900 res://tests/LookTest.tscn -- --out-dir C:\temp\look [--clip walk] [--zoom 20]</c>
/// also saves <c>look-test.png</c> (default zoom, or <c>--zoom &lt;m&gt;</c>) and <c>look-test-far.png</c> (max zoom-out). <c>--stay</c> keeps the window open
/// (wheel zooms, arrows pan). The assets are the hand-copied files under <c>assets/models/looktest/</c> (no manifest yet).
/// A model that doesn't load (an LFS pointer on a clone without <c>git lfs pull</c>, BUG-0860) is a FAIL line and exit 1,
/// never a hang; <c>--character &lt;res path&gt;</c> swaps the character (a missing path is that regression row).
/// The UAL clips drive the character directly: both packs share the Universal rig's bone names and the importer puts each
/// skeleton at <c>Armature/Skeleton3D</c>, so the library's track paths resolve against the character root unchanged.
/// </remarks>
public partial class LookTest : Node3D
{
    private const string Dir = "res://assets/models/looktest/";
    private const string DefaultCharacterPath = Dir + "Superhero_Male_FullBody.gltf";
    private const string LibraryPath = Dir + "UAL1_Standard.glb";
    private const string StandInPath = Dir + "Barracks_FirstAge_Level1.fbx";
    // The glb's Idle_Loop / Walk_Loop: Godot's importer reads the _Loop suffix as the loop flag and drops it from the name.
    private const string IdleClip = "Idle", WalkClip = "Walk";
    private const string StandInBuilding = "malazan_barracks";
    private const string PrimitiveUnit = "malazan_heavy_infantry";
    private const string WatchedBone = "upperarm_l";

    /// <summary>Gap between neighbouring figures, in meters (the brief's 3 m).</summary>
    public const float Spacing = 3f;

    /// <summary>The flat ground patch's side, in cells (2 m each): wide enough to fill the frame at max zoom-out.</summary>
    public const int GroundCells = 96;

    private RtsCamera _camera = null!;
    private Node3D? _universal;
    private AnimationPlayer? _player;
    private Skeleton3D? _skeleton;
    private string _characterPath = DefaultCharacterPath;
    private int _bone;
    private Quaternion _restPose;
    private string _clip = IdleClip;
    private float _zoom = CameraLimits.DefaultZoom;
    private string _outDir = "user://";
    private bool _stay;
    private int _phase, _frames;
    private readonly System.Collections.Generic.List<string> _failures = new();

    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--stay") _stay = true;
            else if (i + 1 < args.Length && args[i] == "--out-dir") _outDir = args[i + 1];
            else if (i + 1 < args.Length && args[i] == "--zoom") _zoom = float.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture);
            else if (i + 1 < args.Length && args[i] == "--clip") _clip = args[i + 1] == "walk" ? WalkClip : IdleClip;
            else if (i + 1 < args.Length && args[i] == "--character") _characterPath = args[i + 1];
        }
        DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
        if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
        GameData data = loaded.Data!;

        var levels = new byte[GroundCells * GroundCells];
        var heights = new float[GroundCells * GroundCells];
        GetNode<TerrainView>("TerrainView").Build(new Heightmap(GroundCells, GroundCells, levels, heights));
        float c = GroundCells * MapConstants.CellSize / 2f;
        _camera = GetNode<RtsCamera>("RtsCamera");
        _camera.SetMap(GroundCells, GroundCells);
        _camera.SetFocus(c, c);
        _camera.SetZoom(_zoom);
        Node3D figures = GetNode<Node3D>("Figures");

        // Left: today's primitive, drawn the way UnitViews draws a unit of this type (radius from data, faction colour).
        UnitDef def = data.Units[data.FindUnit(PrimitiveUnit)];
        float height = UnitViews.BodyHeight(def.Radius);
        var capsule = new MeshInstance3D
        {
            Name = "Primitive",
            Mesh = new CapsuleMesh
            {
                Radius = def.Radius, Height = height, RadialSegments = 12, Rings = 3,
                Material = new StandardMaterial3D { AlbedoColor = UnitViews.ColorFromRgb(data.Factions[def.Faction].PrimaryColor), Roughness = 0.8f },
            },
            Position = new Vector3(c - Spacing, height / 2f, c),
        };
        figures.AddChild(capsule);

        // Centre: the Universal character with the UAL library on its own AnimationPlayer. Either missing skips the clip
        // phases: the scene goes straight to its FAIL line.
        _universal = LoadScene<Node3D>(_characterPath);
        Node? ual = LoadScene<Node>(LibraryPath);
        AnimationPlayer? source = ual == null ? null : FindFirst<AnimationPlayer>(ual);
        AnimationLibrary? library = source?.GetAnimationLibrary("");
        ual?.Free();
        if (ual != null && library == null) Fail("UAL1 has no default animation library");
        if (_universal != null)
        {
            _universal.Name = "Universal";
            _universal.Position = new Vector3(c, 0f, c);
            figures.AddChild(_universal);
            _skeleton = FindFirst<Skeleton3D>(_universal);
            if (_skeleton == null) Fail("the Universal character has no Skeleton3D");
            _player = new AnimationPlayer { Name = "AnimationPlayer" };
            _universal.AddChild(_player);
            _player.RootNode = _player.GetPathTo(_universal);
            if (library != null) _player.AddAnimationLibrary("", library);
        }
        if (_skeleton != null)
        {
            _bone = _skeleton.FindBone(WatchedBone);
            if (_bone < 0) Fail($"no bone {WatchedBone}");
            else _restPose = _skeleton.GetBonePoseRotation(_bone);
            // Facing: the toes point the model's forward (docs/04 wants -Z; glTF exports face +Z).
            int foot = _skeleton.FindBone("foot_l"), ball = _skeleton.FindBone("ball_l");
            if (foot >= 0 && ball >= 0)
            {
                Vector3 toes = _skeleton.GlobalTransform * _skeleton.GetBoneGlobalRest(ball).Origin - _skeleton.GlobalTransform * _skeleton.GetBoneGlobalRest(foot).Origin;
                GD.Print($"LookTest: universal toes point {(toes.Z > 0 ? "+Z" : "-Z")} ({toes.X:F2}, {toes.Y:F2}, {toes.Z:F2})");
            }
        }
        if (_player != null && library != null)
        {
            foreach (string clip in new[] { IdleClip, WalkClip })
                if (!_player.HasAnimation(clip)) Fail($"UAL1 has no {clip}");
            if (_player.HasAnimation(IdleClip)) _player.Play(IdleClip);
        }
        else _phase = 4;

        // Right: the stylized stand-in, its near edge one gap past the character. The pack is authored at about 1 m across,
        // so it is scaled to fill the barracks footprint the way BuildingViews' placeholder box does (the manifest's
        // scale field will carry this in the art pass).
        Node3D? standIn = LoadScene<Node3D>(StandInPath);
        if (standIn != null)
        {
            standIn.Name = "StandIn";
            figures.AddChild(standIn);
            Aabb box = LocalBounds(standIn);
            Report("stand-in as imported", standIn);
            BuildingDef barracks = data.Buildings[data.FindBuilding(StandInBuilding)];
            float scale = barracks.FootprintWidth * MapConstants.CellSize / Mathf.Max(Mathf.Max(box.Size.X, box.Size.Z), 0.001f);
            standIn.Scale = new Vector3(scale, scale, scale);
            standIn.Position = new Vector3(c + Spacing - box.Position.X * scale, -box.Position.Y * scale, c - box.GetCenter().Z * scale);
            GD.Print($"LookTest: stand-in scaled x{scale:F2} to the {StandInBuilding} footprint ({barracks.FootprintWidth} cells)");
        }

        Report("primitive", capsule);
        if (_universal != null && _player != null)
        {
            Report("universal", _universal);
            string skeletonAt = _skeleton == null ? "(none)" : _universal.GetPathTo(_skeleton).ToString();
            GD.Print($"LookTest: universal skeleton at {skeletonAt}, {_skeleton?.GetBoneCount() ?? 0} bones; clips {_player.GetAnimationList().Length} [{string.Join(' ', _player.GetAnimationList())}]; team slot: {(HasTeamMaterial(_universal) ? "yes" : "none")}");
        }
    }

    // A model the scene needs, or a Fail and null: a path that doesn't exist is checked first (no engine ERROR line), and a
    // file that exists but won't load (an LFS pointer: its import failed) comes back null from GD.Load.
    private T? LoadScene<T>(string path) where T : Node
    {
        PackedScene? scene = ResourceLoader.Exists(path) ? GD.Load<PackedScene>(path) : null;
        Node? node = scene?.Instantiate();
        if (node is T typed) return typed;
        node?.Free();
        Fail($"could not load {path} (missing, or an LFS pointer: run git lfs pull and re-import)");
        return null;
    }

    public override void _Process(double delta)
    {
        switch (_phase)
        {
            case 0: // the idle has run a while: it must have moved and bent the watched bone
                if (++_frames < 30) return;
                CheckClip(IdleClip);
                if (_player!.HasAnimation(WalkClip)) _player.Play(WalkClip);
                _frames = 0;
                _phase = 1;
                return;
            case 1: // the in-place walk too
                if (++_frames < 30) return;
                CheckClip(WalkClip);
                if (_player!.HasAnimation(_clip)) _player.Play(_clip);
                _frames = 0;
                _phase = DisplayServer.GetName() == "headless" ? 4 : 2;
                if (_phase == 4) GD.Print("LookTest: capture unavailable in headless mode.");
                return;
            case 2: // default zoom shot
                if (++_frames < 20) return;
                Save("look-test.png");
                _camera.SetZoom(CameraLimits.MaxZoom);
                _frames = 0;
                _phase = 3;
                return;
            case 3: // max zoom-out shot
                if (++_frames < 20) return;
                Save("look-test-far.png");
                _camera.SetZoom(_zoom);
                _phase = 4;
                return;
            case 4:
                if (_failures.Count == 0) GD.Print("LookTest: PASS (three figures shown, UAL idle and walk advance on the Universal rig)");
                else GD.Print($"LookTest: FAIL {string.Join("; ", _failures)}");
                _phase = 5;
                if (_stay && _failures.Count == 0) return;
                SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
                return;
        }
    }

    private void CheckClip(string clip)
    {
        if (_player == null || !_player.HasAnimation(clip)) return; // already failed in _Ready
        double pos = _player.CurrentAnimationPosition;
        float turned = _skeleton == null || _bone < 0 ? 0f : _skeleton.GetBonePoseRotation(_bone).AngleTo(_restPose);
        GD.Print($"LookTest: {clip} at {pos:F2} s of {_player.GetAnimation(clip).Length:F2} s (loop {_player.GetAnimation(clip).LoopMode}), {WatchedBone} {Mathf.RadToDeg(turned):F1} deg from its rest pose");
        if (_player.CurrentAnimation != clip || !(pos > 0)) Fail($"{clip} did not advance");
        if (!(turned > 0.01f)) Fail($"{clip} did not move {WatchedBone}");
    }

    private void Save(string name)
    {
        string path = System.IO.Path.Combine(ProjectSettings.GlobalizePath(_outDir), name);
        Error err = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"LookTest: saved {path} ({err}) at zoom {_camera.Zoom}");
        if (err != Error.Ok) Fail($"could not save {path}");
    }

    private void Fail(string why) => _failures.Add(why);

    // Mesh count, triangles (full LOD) and height of a figure; a figure without a mesh fails.
    private void Report(string label, Node3D figure)
    {
        int meshes = 0, tris = 0;
        var materials = new System.Text.StringBuilder();
        foreach (Node n in figure.FindChildren("*", "MeshInstance3D", true, false))
        {
            Mesh? mesh = ((MeshInstance3D)n).Mesh;
            if (mesh == null) continue;
            meshes++;
            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                Godot.Collections.Array arrays = mesh.SurfaceGetArrays(s);
                int indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array().Length;
                tris += indices > 0 ? indices / 3 : arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length / 3;
                materials.Append(mesh.SurfaceGetMaterial(s)?.ResourceName).Append(' ');
            }
        }
        if (figure is MeshInstance3D own && own.Mesh != null)
        {
            meshes++;
            tris += own.Mesh.GetFaces().Length / 3;
        }
        Aabb box = LocalBounds(figure);
        GD.Print($"LookTest: {label} {meshes} meshes, {tris} tris, size {box.Size.X:F2} x {box.Size.Y:F2} x {box.Size.Z:F2} m, materials [{materials.ToString().Trim()}]");
        if (meshes == 0 || tris == 0) Fail($"{label} has no mesh");
    }

    private static bool HasTeamMaterial(Node figure)
    {
        foreach (Node n in figure.FindChildren("*", "MeshInstance3D", true, false))
        {
            Mesh? mesh = ((MeshInstance3D)n).Mesh;
            for (int s = 0; mesh != null && s < mesh.GetSurfaceCount(); s++)
                if (mesh.SurfaceGetMaterial(s)?.ResourceName == "team") return true;
        }
        return false;
    }

    // The union of a figure's mesh AABBs in the figure's own space (its position ignored).
    private static Aabb LocalBounds(Node3D figure)
    {
        Aabb? total = null;
        Transform3D inv = figure.GlobalTransform.AffineInverse();
        foreach (Node n in figure.FindChildren("*", "MeshInstance3D", true, false))
        {
            var mi = (MeshInstance3D)n;
            Aabb b = inv * mi.GlobalTransform * mi.GetAabb();
            total = total == null ? b : total.Value.Merge(b);
        }
        if (figure is MeshInstance3D own) total = total == null ? own.GetAabb() : total.Value.Merge(own.GetAabb());
        return total ?? new Aabb();
    }

    private static T? FindFirst<T>(Node root) where T : Node
    {
        if (root is T t) return t;
        foreach (Node child in root.GetChildren())
            if (FindFirst<T>(child) is T found) return found;
        return null;
    }
}
