using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using NVector2 = System.Numerics.Vector2;

namespace Rts.Game.Tests;

/// <summary>M6-V20: unit types with a model file are drawn as that model (team colour, clips by sim state), every other type as its capsule, and every fallback is a capsule with no ERROR line.</summary>
/// <remarks>
/// Headless. Run: <c>&amp; $env:GODOT --headless --path game res://tests/ModelViewTest.tscn</c>; prints "MODEL VIEW TEST PASS"
/// and exits 0, or prints each failure and exits 1. The expected models are the ones <c>tools/asset-manifest.json</c> lists
/// (destination = the data's <c>model</c> path). Rows: which types draw a model; per model its triangles (at most the
/// manifest's cap), textures (at most its size), height, feet, facing, bones and <c>team</c> material; the library's clips
/// (every clip a model's clip map names, looping); two players' team colours; idle / walk / attack from the sim's states
/// and a worker's gather_gold / gather_wood / build; the hit flash, the worker tint and the cargo marker on a model; a slot
/// reused capsule, model, capsule, model; a forged data copy whose <c>model</c> points nowhere and an LFS pointer in a
/// model's place (both capsules); a forged <c>scale</c> 1.5 drawn uniformly; <see cref="UnitViews.UseModels"/> off; and a
/// steady frame of 200 animated models allocating nothing in the view's code.
/// M6-VH7: a model shows no worker state tint, its team surface draws the owner's colour while gathering and returning
/// (BUG-1320); a reused slot's model child is named <c>Model</c> and a team-less model warns once over three binds
/// (BUG-1322); the crowd schedule (<see cref="CrowdAnimator"/>) under a camera over 400 models (the nearest every frame,
/// every shown one within the stagger period, none hidden); the steady row runs the schedule too.
/// M6-V21: six model types (the Heavy Infantry, Camp Follower, Raider and Desert Archer join); per model its height within
/// 5 % of 1.8 m x its data <c>scale</c> and its clip map (read from the file) equal to the manifest's; the library holds
/// every clip a map names; a fighting Heavy Infantry and Raider play their melee clip, a shooting Desert Archer its shot,
/// from real sim states, a Camp Follower its three worker clips; a library lacking the melee clip gives idle; the figures
/// follow the game speed (1x, 2x, 0.75x: the same frames advance a walk 1, 2 and 0.75 times as far, under a camera with the
/// stagger too) and stand still while paused; the steady row is 400 models mixed over every model type.
/// M7-V5: eleven model types (five Teblor), one per manifest entry, every other type (the Teblor Destrier Rider and War-dog
/// among them) a capsule; each file's figure (without its props' own surfaces) is 1.8 m within 5 %: the file holds the base
/// figure and the view applies the data scale (TeblorModelTest checks the drawn size).
/// </remarks>
public partial class ModelViewTest : Node
{
    private const string Tag = "MODEL VIEW TEST";
    private readonly List<string> _failures = new();
    private string _scratch = "";

    public override async void _Ready()
    {
        _scratch = Path.Combine(Path.GetTempPath(), $"rts-model-view-{OS.GetProcessId()}");
        try
        {
            await Run();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        UnitViews.UseModels = true;
        UnitModels.ModelRoot = "res://assets/models/";
        try { if (Directory.Exists(_scratch)) Directory.Delete(_scratch, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        foreach (string f in _failures) GD.Print($"{Tag} FAIL: {f}");
        if (_failures.Count == 0) GD.Print($"{Tag} PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private async Task Run()
    {
        string dataDir = ProjectSettings.GlobalizePath("res://data");
        DataLoadResult loaded = DataLoader.LoadAll(dataDir);
        if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
        GameData data = loaded.Data!;
        JsonNode manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "tools", "asset-manifest.json")))!;

        UnitViews.UseModels = true;
        var views = new UnitViews();
        AddChild(views);
        uint[] rgb = { 0xC03030, 0x3060D0 };
        views.Bind(data, 64, rgb);
        Check(views.Models != null, "no model loaded at all");
        WhichTypes(data, views, manifest);
        foreach (JsonNode? m in manifest["models"]!.AsArray()) ModelRows(data, views, m!);
        LibraryRows(manifest);
        await StateRows(data, views, rgb);
        views.QueueFree();
        await FightRows(data, manifest);
        await SpeedRows(data);
        await MenuRow(data);
        ForgedRows(dataDir, data);
        WarningRow(data);
        await StaggerRow(data);
        await SteadyRow(data);
    }

    // Criterion 3 (a): exactly the manifest's types draw a model, at the path their data names; every other type a capsule.
    private void WhichTypes(GameData data, UnitViews views, JsonNode manifest)
    {
        var expected = new Dictionary<string, string>();
        foreach (JsonNode? m in manifest["models"]!.AsArray()) expected[(string)m!["id"]!] = "res://" + ((string)m["destination"]!)["game/".Length..];
        int models = 0;
        for (int t = 0; t < data.Units.Length; t++)
        {
            UnitDef def = data.Units[t];
            bool want = expected.TryGetValue(def.Key, out string? path);
            Check(views.DrawsModel(t) == want, $"{def.Key}: draws a {(views.DrawsModel(t) ? "model" : "capsule")}, want a {(want ? "model" : "capsule")}");
            if (want) Check(UnitModels.PathFor(def.Model) == path, $"{def.Key}: data path {UnitModels.PathFor(def.Model)}, manifest {path}");
            Check(views.MeshOf(t) is CapsuleMesh, $"{def.Key}: no capsule mesh kept for the fallback");
            if (views.DrawsModel(t)) models++;
        }
        GD.Print($"types: {models} models, {data.Units.Length - models} capsules");
        // M7-V5: one model type per manifest entry (11: the six of M6-V21 and five Teblor); every other type, the Teblor
        // Destrier Rider and War-dog among them, a capsule (checked per type above).
        Check(models == expected.Count, $"{models} types draw a model, the manifest has {expected.Count} entries");
        var capsules = new List<string>();
        for (int t = 0; t < data.Units.Length; t++) if (!views.DrawsModel(t) && data.Units[t].Key.StartsWith("teblor_", StringComparison.Ordinal)) capsules.Add(data.Units[t].Key);
        GD.Print($"teblor capsules: [{string.Join(' ', capsules)}]");
    }

    // Criterion 3 (b): triangles, textures, height, feet, facing, rig and the team material, read from the imported model.
    private void ModelRows(GameData data, UnitViews views, JsonNode m)
    {
        string id = (string)m["id"]!;
        int type = data.FindUnit(id);
        if (type < 0 || !views.DrawsModel(type)) return;
        int maxTris = (int)m["maxTriangles"]!, maxTex = (int)m["textureSize"]!;
        var holder = new Node3D();
        AddChild(holder);
        Node3D model = views.Models!.Instantiate(type, out MeshInstance3D mesh, out AnimationPlayer _);
        holder.AddChild(model);
        int tris = 0, team = 0;
        var mats = new List<string>();
        foreach (Node n in model.FindChildren("*", "MeshInstance3D", true, false))
        {
            Mesh? mm = ((MeshInstance3D)n).Mesh;
            for (int s = 0; mm != null && s < mm.GetSurfaceCount(); s++)
            {
                int indices = mm.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Index].AsInt32Array().Length;
                tris += indices / 3;
                var mat = mm.SurfaceGetMaterial(s) as BaseMaterial3D;
                mats.Add(mat?.ResourceName ?? "?");
                if (mat?.ResourceName == UnitModels.TeamMaterialName) team++;
                Vector2I size = mat?.AlbedoTexture?.GetSize() is Vector2 v ? new Vector2I((int)v.X, (int)v.Y) : Vector2I.Zero;
                Check(size.X <= maxTex && size.Y <= maxTex, $"{id}: texture of {mat?.ResourceName} is {size}, over {maxTex}");
            }
        }
        Aabb box = mesh.GlobalTransform * mesh.GetAabb();
        var skeleton = model.FindChildren("*", "Skeleton3D", true, false)[0] as Skeleton3D;
        int foot = skeleton?.FindBone("foot_l") ?? -1, ball = skeleton?.FindBone("ball_l") ?? -1;
        Vector3 toes = foot < 0 || ball < 0 ? Vector3.Zero
            : skeleton!.GlobalTransform * skeleton.GetBoneGlobalRest(ball).Origin - skeleton.GlobalTransform * skeleton.GetBoneGlobalRest(foot).Origin;
        GD.Print($"{id}: {tris} triangles, {box.Size.Y:F2} m tall, feet at {box.Position.Y:F3}, toes ({toes.X:F2}, {toes.Y:F2}, {toes.Z:F2}), {skeleton?.GetBoneCount()} bones, materials [{string.Join(' ', mats)}]");
        Check(tris > 0 && tris <= maxTris, $"{id}: {tris} triangles, cap {maxTris}");
        Check(team == 1, $"{id}: {team} '{UnitModels.TeamMaterialName}' surfaces, want 1");
        // M6-V21: within 5 % of the style guide's 1.8 m human (docs/04 "Scale and dimensions"). M7-V5: the file holds the base
        // figure and the view applies the data scale (a 1.5x Teblor's file is 1.8 m too: no size is baked in), and the height
        // is the figure's without its props' own surfaces (a javelin stands over the head in the rest pose).
        var props = new HashSet<string>();
        foreach (JsonNode? p in m["props"]?.AsArray() ?? new JsonArray())
            if ((string)p!["material"]! != UnitModels.TeamMaterialName) props.Add((string)p["material"]!);
        float figure = FigureHeight(mesh, props);
        const float want = 1.8f;
        GD.Print($"{id}: figure {figure:F3} m tall without the prop surfaces [{string.Join(' ', props)}], data scale {data.Units[type].Scale} (drawn {figure * data.Units[type].Scale:F2} m)");
        Check(Mathf.Abs(figure - want) <= 0.05f * want, $"{id}: the figure is {figure:F3} m tall, want {want:F2} m within 5 % (the data scale is applied by the view)");
        // M6-V21: the clip map the file carries (glTF extras) is the manifest's.
        var map = new List<string>();
        foreach (UnitModels.Clip c in Enum.GetValues<UnitModels.Clip>())
        {
            string name = UnitModels.ClipNames[(int)c];
            string source = views.Models.ClipSource(type, c)!;
            string expect = (string?)m["clips"]![name] ?? name;
            map.Add($"{name}={source}");
            Check(source == expect, $"{id}: clip {name} plays the library's {source}, the manifest says {expect}");
        }
        GD.Print($"{id}: clip map [{string.Join(' ', map)}]");
        Check(Mathf.Abs(box.Position.Y) < 0.05f, $"{id}: feet at {box.Position.Y} m, want the origin");
        Check(toes.Z < -0.05f, $"{id}: toes point {toes}, want -Z");
        Check(skeleton?.GetBoneCount() == 65, $"{id}: {skeleton?.GetBoneCount()} bones, want the 65-bone Universal rig");
        holder.Free();
    }

    // M7-V5: a figure's rest-pose height over every surface but the props' own materials.
    private static float FigureHeight(MeshInstance3D mi, HashSet<string> props)
    {
        float lo = float.MaxValue, hi = float.MinValue;
        Transform3D x = mi.GlobalTransform;
        for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
        {
            if (mi.Mesh.SurfaceGetMaterial(s) is BaseMaterial3D mat && props.Contains(mat.ResourceName)) continue;
            foreach (Vector3 v in mi.Mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
            {
                float y = (x * v).Y;
                lo = Math.Min(lo, y);
                hi = Math.Max(hi, y);
            }
        }
        return hi - lo;
    }

    // Criterion 3 (d): the library holds every clip a model's clip map names, each looping.
    private void LibraryRows(JsonNode manifest)
    {
        AnimationLibrary? lib = UnitModels.Library;
        Check(lib != null, "the animation library did not load");
        if (lib == null) return;
        var names = new SortedSet<string>();
        foreach (JsonNode? m in manifest["models"]!.AsArray())
            foreach (KeyValuePair<string, JsonNode?> clip in m!["clips"]!.AsObject()) names.Add((string)clip.Value!);
        // M6-V21: and the library holds exactly the manifest's library.clips.
        foreach (KeyValuePair<string, JsonNode?> clip in manifest["library"]!["clips"]!.AsObject()) Check(names.Contains(clip.Key), $"library clip {clip.Key} is played by no model");
        Check(lib.GetAnimationList().Count == manifest["library"]!["clips"]!.AsObject().Count, $"library has {lib.GetAnimationList().Count} clips, the manifest {manifest["library"]!["clips"]!.AsObject().Count}");
        foreach (string name in names)
        {
            Check(lib.HasAnimation(name), $"library lacks {name}");
            if (lib.HasAnimation(name)) Check(lib.GetAnimation(name).LoopMode == Animation.LoopModeEnum.Linear, $"clip {name} does not loop");
        }
        GD.Print($"library: [{string.Join(' ', lib.GetAnimationList())}], manifest names [{string.Join(' ', names)}]");
    }

    // Criterion 3 (c, e) and the view's other duties on a model: colours, clips by state, flash, tint, cargo, reuse.
    private async Task StateRows(GameData data, UnitViews views, uint[] rgb)
    {
        var sim = new Simulation(new SimConfig(3, 2, 64, 4096) { Data = data });
        World w = sim.World;
        UnitStore u = w.Units;
        int laborer = data.FindUnit("malazan_laborer"), xbow = data.FindUnit("malazan_crossbowman"), heavy = data.FindUnit("malazan_heavy_infantry");
        NVector2[] west = StartLayout.Block(w.NavGrid, 8, true, 1f);
        sim.Enqueue(Command.SpawnUnit(0, xbow, west[0]));
        sim.Enqueue(Command.SpawnUnit(1, xbow, west[0] + new NVector2(0f, 40f)));
        sim.Enqueue(Command.SpawnUnit(0, laborer, west[1]));
        sim.Tick();
        sim.Tick();
        views.Sync(w, 1f);
        int a = Slot(u, 0, xbow), b = Slot(u, 1, xbow), lab = Slot(u, 0, laborer);
        int surface = views.Models!.TeamSurface(xbow);
        var matA = views.ModelMeshOf(a)?.GetSurfaceOverrideMaterial(surface) as ShaderMaterial;
        var matB = views.ModelMeshOf(b)?.GetSurfaceOverrideMaterial(surface) as ShaderMaterial;
        Color colA = matA?.GetShaderParameter("team_colour").AsColor() ?? default, colB = matB?.GetShaderParameter("team_colour").AsColor() ?? default;
        GD.Print($"team colours: player 0 {colA}, player 1 {colB}");
        Check(colA == UnitViews.ColorFromRgb(rgb[0]) && colB == UnitViews.ColorFromRgb(rgb[1]) && colA != colB, $"team colours {colA} / {colB}, want the two players' {UnitViews.ColorFromRgb(rgb[0])} / {UnitViews.ColorFromRgb(rgb[1])}");
        Check(views.ViewOf(a)!.Mesh == null && views.ModelOf(a)?.GetParent() == views.ViewOf(a), "a model slot's node should draw no capsule and hold the model");

        CheckClip(views, a, "idle", "a standing crossbowman");
        await Frames(3);
        Upright(views, a, "idle");
        sim.Enqueue(Command.Move(0, new EntityHandle(a, u.Generation[a]), west[0] + new NVector2(20f, 0f)));
        for (int i = 0; i < 20 && u.State[a] != UnitState.Moving; i++) sim.Tick();
        views.Sync(w, 1f);
        CheckClip(views, a, "walk", "a moving crossbowman");
        double before = views.AnimatorOf(a)!.CurrentAnimationPosition;
        await Frames(5);
        Check(views.AnimatorOf(a)!.CurrentAnimationPosition != before, "the walk did not advance over five frames");
        Upright(views, a, "walk");
        sim.Enqueue(Command.Move(0, new EntityHandle(a, u.Generation[a]), u.Position[b] + new NVector2(0f, -10f)));
        for (int i = 0; i < 600 && u.State[a] != UnitState.Attacking; i++) sim.Tick();
        views.Sync(w, 1f);
        Check(u.State[a] == UnitState.Attacking, $"the crossbowman never attacked (state {u.State[a]})");
        CheckClip(views, a, "attack", "an attacking crossbowman");

        // A worker's three states, written straight into this test's own world (the view only reads them).
        u.State[lab] = UnitState.Building;
        views.Sync(w, 1f);
        CheckClip(views, lab, "build", "a building laborer");
        // M6-VH7, BUG-1320: no worker state tint over a model (it hid the outfit and the team colour); the state is still
        // recorded, and a capsule keeps its tint (EconomyViewTest).
        Check(views.ModelMeshOf(lab)?.MaterialOverlay == null && views.ShownTint(lab) == UnitState.Building,
            $"a building laborer model: overlay {views.ModelMeshOf(lab)?.MaterialOverlay} (want none), shown tint {views.ShownTint(lab)}");
        u.State[lab] = UnitState.Gathering;
        u.GatherNode[lab] = default;
        u.CargoKind[lab] = ResourceKind.Wood;
        views.Sync(w, 1f);
        CheckClip(views, lab, "gather_wood", "a laborer gathering wood");
        TeamSurfaceRow(views, lab, laborer, UnitViews.ColorFromRgb(rgb[0]), "a gathering laborer");
        u.State[lab] = UnitState.Returning;
        views.Sync(w, 1f);
        TeamSurfaceRow(views, lab, laborer, UnitViews.ColorFromRgb(rgb[0]), "a returning laborer");
        u.State[lab] = UnitState.Gathering;
        views.Sync(w, 1f);
        u.CargoKind[lab] = ResourceKind.Gold;
        u.Cargo[lab] = 5;
        views.Sync(w, 1f);
        CheckClip(views, lab, "gather_gold", "a laborer gathering gold");
        Check(views.MarkerOf(lab)?.Visible == true && views.MarkerOf(lab)?.GetParent() == views.ViewOf(lab), "no cargo marker over a carrying model");
        u.State[lab] = UnitState.Idle;
        u.Cargo[lab] = 0;
        u.Hp[lab] -= 1; // a hit: the flash goes on the model's mesh
        views.Sync(w, 1f, 0.01f);
        Check(views.ModelMeshOf(lab)?.MaterialOverlay == views.FlashMaterial, "the hit flash is not on the model's mesh");

        // Slot reuse: model -> capsule -> model -> capsule in one slot (the store's free list is LIFO).
        Node3D? first = views.ModelOf(a);
        u.Free(new EntityHandle(a, u.Generation[a]));
        views.Sync(w, 1f);
        Check(!views.ViewOf(a)!.Visible && views.AnimatorOf(a)?.Active == false, "a dead model slot should hide and stop animating");
        // M6-V21: the Heavy Infantry is a model now: the capsule kind is the first type without one.
        int capsule = 0;
        while (capsule < data.Units.Length && views.DrawsModel(capsule)) capsule++;
        int[] kinds = { capsule, laborer, capsule, heavy };
        foreach (int kind in kinds)
        {
            sim.Enqueue(Command.SpawnUnit(1, kind, west[5]));
            sim.Tick();
            sim.Tick();
            Check(u.Alive[a] && u.TypeId[a] == kind, $"respawn of {data.Units[kind].Key} did not reuse slot {a}");
            views.Sync(w, 1f);
            bool model = views.DrawsModel(kind);
            Check(views.ViewOf(a)!.Visible, $"reused slot {a} ({data.Units[kind].Key}) hidden");
            Check(model ? views.ViewOf(a)!.Mesh == null && views.ModelOf(a) != null : views.ViewOf(a)!.Mesh == views.MeshOf(kind) && views.ModelOf(a) == null,
                $"reused slot {a} as {data.Units[kind].Key}: mesh {views.ViewOf(a)!.Mesh}, model {views.ModelOf(a)}");
            if (model) Check(views.ModelOf(a)?.Name == "Model" && views.ViewOf(a)!.GetNodeOrNull("Model") == views.ModelOf(a),
                $"reused slot {a} as {data.Units[kind].Key}: the model child is named '{views.ModelOf(a)?.Name}', want 'Model' (BUG-1322)");
            if (model) Check(views.ModelOf(a) != first &&(views.ModelMeshOf(a)?.GetSurfaceOverrideMaterial(views.Models.TeamSurface(kind)) as ShaderMaterial)?.GetShaderParameter("team_colour").AsColor() == UnitViews.ColorFromRgb(rgb[1]),
                $"reused slot {a} as player 1's {data.Units[kind].Key}: not a new model in player 1's colour");
            else Check(views.ViewOf(a)!.MaterialOverride == views.PlayerMaterial(1), $"reused slot {a} as a capsule: not player 1's material");
            u.Free(new EntityHandle(a, u.Generation[a]));
            views.Sync(w, 1f);
        }
        GD.Print($"state rows: {views.NodeCount} slot nodes, {views.ModelNodeCount} model instances, {views.Models.TeamMaterialCount} team materials");
    }

    // Criterion 8: the menu's match (the shell, the setup's Start with its defaults) draws the starting workers as models.
    private async Task MenuRow(GameData data)
    {
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.UserArgs = Array.Empty<string>();
        main.SettingsPath = $"user://model-view-test-{OS.GetProcessId()}/settings.cfg"; // missing: the defaults, never written
        AddChild(main);
        await Frames(2);
        main.Setup!.StartButton.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        Match? match = main.CurrentMatch;
        Check(match != null, "the menu's Start started no match");
        if (match == null) return;
        Simulation sim = match.GetNode<SimRunner>("SimRunner").Simulation!;
        for (int i = 0; i < 600 && sim.World.Units.Count == 0; i++) await Frames(1);
        await Frames(2);
        UnitViews views = match.GetNode<UnitViews>("World3D/UnitViews");
        int models = 0, workers = 0;
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || !views.DrawsModel(u.TypeId[i])) continue;
            workers++;
            if (views.ModelOf(i) != null && views.IsShown(i) == views.ViewOf(i)!.Visible) models++;
        }
        GD.Print($"menu match: {u.Count} units, {workers} of a model type, {models} drawn as models");
        Check(workers > 0 && models == workers, $"the menu's match: {models} of {workers} model-type units drawn as models");
        main.QueueFree();
        await Frames(2);
    }

    // Criterion 3 (f): a model path that points nowhere and an LFS pointer in a model's place give capsules (the scene loop
    // fails on any ERROR line); a forged scale draws the model uniformly with its feet on the ground; UseModels off.
    private void ForgedRows(string dataDir, GameData shipped)
    {
        string copy = Path.Combine(_scratch, "data");
        foreach (string dir in Directory.GetDirectories(dataDir, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(copy, Path.GetRelativePath(dataDir, dir)));
        foreach (string file in Directory.GetFiles(dataDir, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(copy, Path.GetRelativePath(dataDir, file)), true);
        string path = Path.Combine(copy, "factions", "malazan", "units.json");
        JsonNode units = JsonNode.Parse(File.ReadAllText(path))!;
        foreach (JsonNode? unit in units["units"]!.AsArray())
        {
            if ((string?)unit!["id"] == "malazan_laborer") unit["model"] = "malazan/nowhere_laborer";
            if ((string?)unit["id"] == "malazan_crossbowman") unit["scale"] = 1.5;
        }
        File.WriteAllText(path, units.ToJsonString());
        DataLoadResult loaded = DataLoader.LoadAll(copy);
        if (!loaded.Ok) throw new InvalidOperationException("forged data failed to load");
        GameData forged = loaded.Data!;
        int laborer = forged.FindUnit("malazan_laborer"), xbow = forged.FindUnit("malazan_crossbowman");

        var views = new UnitViews();
        AddChild(views);
        views.Bind(forged, 8, new uint[] { 0xC03030, 0x3060D0 });
        Check(!views.DrawsModel(laborer), "a laborer whose model points nowhere should draw its capsule");
        Check(views.DrawsModel(xbow), "the crossbowman lost its model in the forged copy");
        var sim = new Simulation(new SimConfig(3, 2, 8, 4096) { Data = forged });
        NVector2[] west = StartLayout.Block(sim.World.NavGrid, 2, true, 1f);
        sim.Enqueue(Command.SpawnUnit(0, laborer, west[0]));
        sim.Enqueue(Command.SpawnUnit(0, xbow, west[1]));
        sim.Tick();
        sim.Tick();
        views.Sync(sim.World, 1f);
        int lab = Slot(sim.World.Units, 0, laborer), x = Slot(sim.World.Units, 0, xbow);
        Check(views.ViewOf(lab)?.Mesh == views.MeshOf(laborer) && views.ModelOf(lab) == null, "the pointless laborer is not drawn as its capsule");
        Node3D? scaled = views.ModelOf(x);
        float feet = scaled == null ? float.NaN : (scaled.GlobalTransform.Origin.Y - UnitViews.GroundPoint(sim.World, x, 1f).Y);
        Check(scaled != null && scaled.Scale.IsEqualApprox(new Vector3(1.5f, 1.5f, 1.5f)) && Mathf.Abs(feet) < 1e-3f,
            $"the scale-1.5 crossbowman: model scale {scaled?.Scale}, feet {feet} m off the ground");
        views.QueueFree();

        // An LFS pointer in a model's place (a clone without git lfs pull): a scratch model root holding pointer files.
        // M6-V21: a pointer for every shipped model type.
        for (int t = 0; t < shipped.Units.Length; t++)
        {
            if (!File.Exists(ProjectSettings.GlobalizePath(UnitModels.PathFor(shipped.Units[t].Model)))) continue;
            string model = shipped.Units[t].Model;
            string file = Path.Combine(_scratch, "models", model, model[(model.LastIndexOf('/') + 1)..] + ".glb");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "version https://git-lfs.github.com/spec/v1\noid sha256:4d7a214614ab2935c943f9e0ff69d22eadbb8f32b1258daaa5e2ca24d17e2393\nsize 875488\n");
        }
        UnitModels.ModelRoot = Path.Combine(_scratch, "models").Replace('\\', '/') + "/";
        var pointed = new UnitViews();
        AddChild(pointed);
        pointed.Bind(shipped, 8);
        for (int t = 0; t < shipped.Units.Length; t++) Check(!pointed.DrawsModel(t), $"{shipped.Units[t].Key}: a model from an LFS pointer file");
        UnitModels.ModelRoot = "res://assets/models/";
        pointed.QueueFree();

        UnitViews.UseModels = false;
        var placeholder = new UnitViews();
        AddChild(placeholder);
        placeholder.Bind(shipped, 8);
        Check(placeholder.Models == null, "UseModels off still loaded models");
        UnitViews.UseModels = true;
        placeholder.QueueFree();
    }

    // Criterion 6: 200 models a side walking: after the first frames (nodes made, clips started) a Sync allocates nothing.
    private async Task SteadyRow(GameData data)
    {
        const int perSide = 200;
        var sim = new Simulation(new SimConfig(5, 2, 2 * perSide, 4096) { Data = data });
        World w = sim.World;
        // M6-V21: mixed evenly over every model type.
        int[] types = ModelTypes(data);
        NVector2[] west = StartLayout.Block(w.NavGrid, perSide, true, 1f), east = StartLayout.Block(w.NavGrid, perSide, false, 1f);
        for (int i = 0; i < perSide; i++)
        {
            sim.Enqueue(Command.SpawnUnit(0, types[i % types.Length], west[i]));
            sim.Enqueue(Command.SpawnUnit(1, types[(i + types.Length / 2) % types.Length], east[i]));
        }
        sim.Tick();
        sim.Tick();
        var views = new UnitViews();
        AddChild(views);
        views.Bind(data, 2 * perSide, new uint[] { 0xC03030, 0x3060D0 });
        views.Sync(w, 0.5f);
        // M6-VH7: each frame also runs the crowd animation schedule under a camera (the sort, the advances).
        Vector3 eye = views.ViewOf(0)!.GlobalPosition + new Vector3(0.3f, 30f, 10f);
        views.AdvanceAnimations(1.0 / 60.0, eye);
        await Frames(3);
        long bytes = 0;
        for (int f = 0; f < 120; f++)
        {
            long start = GC.GetAllocatedBytesForCurrentThread();
            views.Sync(w, (f % 10) / 10f, 1f / 60f);
            views.AdvanceAnimations(1.0 / 60.0, eye);
            bytes += GC.GetAllocatedBytesForCurrentThread() - start;
        }
        GD.Print($"steady: {views.ModelNodeCount} models of {types.Length} types, 120 syncs and crowd frames allocated {bytes} bytes ({views.Crowd.AdvancedLastFrame} players advanced in the last)");
        Check(views.ModelNodeCount == 2 * perSide, $"{views.ModelNodeCount} model instances for {2 * perSide} units");
        Check(bytes == 0, $"120 steady syncs of {2 * perSide} models allocated {bytes} bytes");
        views.QueueFree();
    }

    // M6-V21: every type drawn as a model on the shipped data, in type order.
    private static int[] ModelTypes(GameData data)
    {
        var probe = new UnitModels(data, new uint[] { 0xC03030 });
        var list = new List<int>();
        for (int t = 0; t < data.Units.Length; t++) if (probe.Has(t)) list.Add(t);
        return list.ToArray();
    }

    // M6-V21, criterion 2: from real sim states, a Heavy Infantry and a Raider fighting each other play their melee clip, a
    // Desert Archer shooting a Crossbowman its shot; a Camp Follower's worker clips; a library without the melee clip: idle.
    private async Task FightRows(GameData data, JsonNode manifest)
    {
        var sim = new Simulation(new SimConfig(7, 2, 32, 4096) { Data = data });
        World w = sim.World;
        UnitStore u = w.Units;
        int heavy = data.FindUnit("malazan_heavy_infantry"), raider = data.FindUnit("whirlwind_raider"),
            archer = data.FindUnit("whirlwind_desert_archer"), follower = data.FindUnit("whirlwind_camp_follower"),
            xbow = data.FindUnit("malazan_crossbowman");
        NVector2[] west = StartLayout.Block(w.NavGrid, 8, true, 1f);
        // The melee pair face to face; the archer 10 m from a crossbowman 30 m north of them (in the archer's range, 14 m).
        sim.Enqueue(Command.SpawnUnit(0, heavy, west[0]));
        sim.Enqueue(Command.SpawnUnit(1, raider, west[0] + new NVector2(1.5f, 0f)));
        sim.Enqueue(Command.SpawnUnit(1, archer, west[0] + new NVector2(0f, 30f)));
        sim.Enqueue(Command.SpawnUnit(0, xbow, west[0] + new NVector2(0f, 40f)));
        sim.Enqueue(Command.SpawnUnit(1, follower, west[0] + new NVector2(-20f, 60f)));
        sim.Tick();
        sim.Tick();
        var views = new UnitViews();
        AddChild(views);
        uint[] rgb = { 0xC03030, 0x3060D0 };
        views.Bind(data, 32, rgb);
        int h = Slot(u, 0, heavy), r = Slot(u, 1, raider), a = Slot(u, 1, archer), f = Slot(u, 1, follower);
        if (!Check(h >= 0 && r >= 0 && a >= 0 && f >= 0 && Slot(u, 0, xbow) >= 0, $"fight rows: a spawn failed (slots {h} {r} {a} {f})")) return;
        var who = new[] { (h, heavy, "a fighting heavy infantry"), (r, raider, "a fighting raider"), (a, archer, "a shooting desert archer") };
        var seen = new bool[who.Length];
        for (int tick = 0; tick < 400 && Array.IndexOf(seen, false) >= 0; tick++)
        {
            sim.Tick();
            views.Sync(w, 1f);
            for (int k = 0; k < who.Length; k++)
            {
                (int slot, int type, string label) = who[k];
                if (seen[k] || !u.Alive[slot] || u.State[slot] != UnitState.Attacking) continue;
                seen[k] = true;
                string source = (string)manifest["models"]!.AsArray().First(m => (string)m!["id"]! == data.Units[type].Key)!["clips"]!["attack"]!;
                AttackClip(views, slot, type, source, label);
            }
        }
        for (int k = 0; k < who.Length; k++) Check(seen[k], $"{who[k].Item3}: never attacked within 400 ticks (state {u.State[who[k].Item1]})");
        // The brief's clips: a melee swing for the two line units, the shot for the archer (no bow clip in UAL1 / UAL2).
        Check(views.Models!.ClipSource(heavy, UnitModels.Clip.Attack) == "attack_melee" && views.Models.ClipSource(raider, UnitModels.Clip.Attack) == "attack_melee",
            $"the line units attack with {views.Models.ClipSource(heavy, UnitModels.Clip.Attack)} / {views.Models.ClipSource(raider, UnitModels.Clip.Attack)}, want attack_melee");
        Check(views.Models.ClipSource(archer, UnitModels.Clip.Attack) == "attack", $"the archer shoots with {views.Models.ClipSource(archer, UnitModels.Clip.Attack)}, want the library's attack");

        // The Camp Follower's three worker states, written into this test's own world (the view only reads them).
        u.State[f] = UnitState.Building;
        views.Sync(w, 1f);
        CheckClip(views, f, "build", "a building camp follower");
        u.State[f] = UnitState.Gathering;
        u.GatherNode[f] = default;
        u.CargoKind[f] = ResourceKind.Wood;
        views.Sync(w, 1f);
        CheckClip(views, f, "gather_wood", "a camp follower gathering wood");
        u.CargoKind[f] = ResourceKind.Gold;
        views.Sync(w, 1f);
        CheckClip(views, f, "gather_gold", "a camp follower gathering gold");
        TeamSurfaceRow(views, f, follower, UnitViews.ColorFromRgb(rgb[1]), "a gathering camp follower");
        views.QueueFree();
        await Frames(1);

        // A library that lacks the melee clip (a cut copy swapped in, as QaM6V20Test row 2 does): a fighting Heavy
        // Infantry plays idle, never nothing.
        var field = typeof(UnitModels).GetField("_library", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var real = (AnimationLibrary)field.GetValue(null)!;
        var cut = new AnimationLibrary();
        foreach (StringName name in real.GetAnimationList()) if (name != "attack_melee") cut.AddAnimation(name, real.GetAnimation(name));
        field.SetValue(null, cut);
        try
        {
            var lacking = new UnitViews();
            AddChild(lacking);
            lacking.Bind(data, 32, rgb);
            u.State[h] = UnitState.Attacking;
            if (u.Alive[h])
            {
                lacking.Sync(w, 1f);
                CheckClip(lacking, h, "idle", "a fighting heavy infantry without the library's attack_melee");
                Check(lacking.ShownClip(h) == (int)UnitModels.Clip.Attack, $"the fallback should still record the attack state, shows {lacking.ShownClip(h)}");
            }
            else Check(false, "the heavy infantry died before the fallback row");
            lacking.QueueFree();
        }
        finally
        {
            field.SetValue(null, real);
        }
        await Frames(1);
    }

    // The player of slot plays "attack", and that is the library clip the type's map names (by Animation identity).
    private void AttackClip(UnitViews views, int slot, int type, string source, string who)
    {
        AnimationPlayer? anim = views.AnimatorOf(slot);
        CheckClip(views, slot, "attack", who);
        Animation? playing = anim != null && anim.HasAnimation("attack") ? anim.GetAnimation("attack") : null;
        Animation? want = UnitModels.Library?.GetAnimation(source);
        GD.Print($"{who}: plays attack = library {source} ({want?.Length:F2} s)");
        Check(playing != null && playing == want, $"{who}: its attack is {playing?.ResourceName} ({playing?.Length} s), want the library's {source} ({want?.Length} s)");
        Check(views.Models!.ClipSource(type, UnitModels.Clip.Attack) == source, $"{who}: clip map says {views.Models.ClipSource(type, UnitModels.Clip.Attack)}, manifest {source}");
    }

    // M6-V21, criterion 3: the figures follow the game speed. 400 walking models mixed over the model types under a camera
    // (the 48 nearest every frame, the rest staggered): after warm-up frames at a speed, every player is put at 0 and nine
    // frames of 1/60 s run; every figure's walk is then at 9/60 s x the speed (1x, 2x, 0.75x), the near and the staggered
    // alike. Paused (the tree, this node pausable): nine frames move nothing and the schedule doesn't run; resumed, it runs.
    private async Task SpeedRows(GameData data)
    {
        const int perSide = 200;
        const double dt = 1.0 / 60.0;
        var sim = new Simulation(new SimConfig(5, 2, 2 * perSide, 4096) { Data = data, Combat = false });
        World w = sim.World;
        UnitStore u = w.Units;
        int[] types = ModelTypes(data);
        NVector2[] west = StartLayout.Block(w.NavGrid, perSide, true, 1f), east = StartLayout.Block(w.NavGrid, perSide, false, 1f);
        for (int i = 0; i < perSide; i++)
        {
            sim.Enqueue(Command.SpawnUnit(0, types[i % types.Length], west[i]));
            sim.Enqueue(Command.SpawnUnit(1, types[i % types.Length], east[i]));
        }
        sim.Tick();
        sim.Tick();
        var runner = new SimRunner { Name = "SpeedRunner" };
        AddChild(runner);
        var views = new UnitViews { Runner = runner, ProcessMode = ProcessModeEnum.Pausable };
        AddChild(views);
        views.Bind(data, 2 * perSide, new uint[] { 0xC03030, 0x3060D0 });
        // Every unit walks (the view only reads the state): one clip, one length, for every figure.
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i]) u.State[i] = UnitState.Moving;
        views.Sync(w, 1f);
        int anchor = Slot(u, 0, types[0]);
        Vector3 eye = views.ViewOf(anchor)!.GlobalPosition + new Vector3(0f, 30f, 10f);
        int n = 0;
        for (int i = 0; i < u.Capacity; i++) if (views.AnimatorOf(i) != null) n++;
        Check(n == 2 * perSide, $"speed: {n} model players for {2 * perSide} units");

        var amount = new Dictionary<double, double>();
        foreach (double speed in new[] { 1.0, 2.0, 0.75 })
        {
            runner.GameSpeed = speed;
            Check(views.AnimationRate == speed, $"speed {speed}: the animation rate is {views.AnimationRate}");
            for (int k = 0; k < CrowdAnimator.StaggerFrames; k++) views.AdvanceAnimations(dt, eye);
            for (int i = 0; i < u.Capacity; i++) views.AnimatorOf(i)?.Seek(0, true);
            for (int k = 0; k < 3 * CrowdAnimator.StaggerFrames; k++) views.AdvanceAnimations(dt, eye);
            double min = double.MaxValue, max = double.MinValue;
            int full = 0;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (views.AnimatorOf(i) is not AnimationPlayer p) continue;
                min = Math.Min(min, p.CurrentAnimationPosition);
                max = Math.Max(max, p.CurrentAnimationPosition);
                if (views.Crowd.WasFullRate(i)) full++;
            }
            amount[speed] = max;
            double want = 3 * CrowdAnimator.StaggerFrames * dt * speed;
            GD.Print($"speed {speed}x: nine frames advanced every walk {min:F4} - {max:F4} s (want {want:F4}), {full} at the full rate in the last frame");
            Check(Math.Abs(min - want) < 1e-4 && Math.Abs(max - want) < 1e-4, $"speed {speed}x: walks advanced {min:F4} - {max:F4} s over nine frames, want {want:F4} for every figure");
        }
        Check(Math.Abs(amount[2.0] / amount[1.0] - 2.0) < 1e-3 && Math.Abs(amount[0.75] / amount[1.0] - 0.75) < 1e-3,
            $"speed: 2x / 1x = {amount[2.0] / amount[1.0]:F4}, 0.75x / 1x = {amount[0.75] / amount[1.0]:F4}");

        // Paused: nothing moves, the schedule holds; resumed: it runs again at the speed.
        runner.GameSpeed = 1.0;
        var before = new double[u.Capacity];
        for (int i = 0; i < u.Capacity; i++) before[i] = views.AnimatorOf(i)?.CurrentAnimationPosition ?? 0;
        long frame = views.Crowd.Frame;
        GetTree().Paused = true;
        double pausedRate = views.AnimationRate;
        for (int k = 0; k < 3 * CrowdAnimator.StaggerFrames; k++) views.AdvanceAnimations(dt, eye);
        long pausedFrames = views.Crowd.Frame - frame;
        GetTree().Paused = false;
        int moved = 0;
        for (int i = 0; i < u.Capacity; i++) if (views.AnimatorOf(i) is AnimationPlayer p && p.CurrentAnimationPosition != before[i]) moved++;
        GD.Print($"paused: rate {pausedRate}, {moved} figures moved over nine frames, the schedule ran {pausedFrames} frames");
        Check(pausedRate == 0.0 && moved == 0 && pausedFrames == 0, $"paused: rate {pausedRate}, {moved} figures moved, {pausedFrames} schedule frames (want 0, 0, 0)");
        views.AdvanceAnimations(dt, eye);
        Check(views.AnimationRate == 1.0 && views.Crowd.Frame == frame + 1 && views.Crowd.AdvancedLastFrame > 0, "resumed: the schedule did not run again");
        views.QueueFree();
        runner.QueueFree();
        await Frames(1);
    }

    // A clip keeps the figure upright over its feet, facing the slot's forward: the head about 1.6 m over the ground point
    // and the toes ahead (the library's tracks and the model's rest agree).
    private void Upright(UnitViews views, int slot, string clip)
    {
        Node3D? model = views.ModelOf(slot);
        var skeleton = model?.FindChildren("*", "Skeleton3D", true, false)[0] as Skeleton3D;
        if (skeleton == null) return;
        Vector3 feet = model!.GlobalTransform.Origin;
        Vector3 head = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(skeleton.FindBone("Head")).Origin - feet;
        Vector3 forward = -views.ViewOf(slot)!.GlobalTransform.Basis.Z;
        Vector3 toes = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(skeleton.FindBone("ball_r")).Origin
            - skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(skeleton.FindBone("foot_r")).Origin;
        GD.Print($"{clip}: head at ({head.X:F2}, {head.Y:F2}, {head.Z:F2}) over the feet, toes {toes.Normalized().Dot(forward):F2} along forward");
        Check(head.Y > 1.4f && head.Y < 1.8f && new Vector2(head.X, head.Z).Length() < 0.35f, $"{clip}: head at {head} from the feet, want upright about 1.6 m up");
        Check(toes.Normalized().Dot(forward) > 0.5f, $"{clip}: toes {toes} against forward {forward}");
    }

    // M6-VH7, BUG-1320: the material Godot draws on a worker model's team surface is its owner's team material, and nothing
    // is drawn over the figure (no overlay, no override).
    private void TeamSurfaceRow(UnitViews views, int slot, int type, Color want, string who)
    {
        MeshInstance3D? mesh = views.ModelMeshOf(slot);
        int surface = views.Models!.TeamSurface(type);
        var drawn = mesh?.GetActiveMaterial(surface) as ShaderMaterial;
        Color got = drawn?.GetShaderParameter("team_colour").AsColor() ?? default;
        GD.Print($"{who}: team surface {surface} draws {drawn?.Shader?.ResourcePath} in {got}, overlay {mesh?.MaterialOverlay?.ToString() ?? "none"}");
        Check(drawn != null && got == want && mesh!.MaterialOverlay == null && mesh.MaterialOverride == null,
            $"{who}: team surface draws {drawn} in {got} (want {want}), overlay {mesh?.MaterialOverlay}, override {mesh?.MaterialOverride}");
    }

    // M6-VH7, BUG-1322: a model without a 'team' material warns once per file per run, not at every Bind. The scene is
    // seeded into the loader's cache (as QaM6V20Test row 1 does) under a stem only this row uses.
    private void WarningRow(GameData shipped)
    {
        const string stem = "malazan/model_view_noteam";
        var root = new Node3D { Name = "Root" };
        var body = new MeshInstance3D { Name = "Body", Mesh = new BoxMesh { Material = new StandardMaterial3D { ResourceName = "outfit" } } };
        root.AddChild(body);
        body.Owner = root;
        var scene = new PackedScene();
        scene.Pack(root);
        root.Free();
        var cache = (Dictionary<string, PackedScene?>)typeof(UnitModels).GetField("Loaded", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        cache[UnitModels.PathFor(stem)] = scene;
        string dataDir = ProjectSettings.GlobalizePath("res://data");
        string forgedDir = Path.Combine(_scratch, "noteam-data");
        foreach (string dir in Directory.GetDirectories(dataDir, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(forgedDir, Path.GetRelativePath(dataDir, dir)));
        foreach (string file in Directory.GetFiles(dataDir, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(forgedDir, Path.GetRelativePath(dataDir, file)), true);
        string path = Path.Combine(forgedDir, "factions", "malazan", "units.json");
        JsonNode units = JsonNode.Parse(File.ReadAllText(path))!;
        foreach (JsonNode? unit in units["units"]!.AsArray())
            if ((string?)unit!["id"] == "malazan_laborer") unit["model"] = stem;
        File.WriteAllText(path, units.ToJsonString());
        GameData forged = DataLoader.LoadAll(forgedDir).Data ?? throw new InvalidOperationException("no-team data failed to load");
        int before = UnitModels.NoTeamWarnings;
        for (int bind = 0; bind < 3; bind++)
        {
            var views = new UnitViews();
            AddChild(views);
            views.Bind(forged, 4, new uint[] { 0xC03030, 0x3060D0 });
            Check(!views.DrawsModel(forged.FindUnit("malazan_laborer")), "a team-less laborer model is drawn as a model");
            views.Free();
        }
        GD.Print($"team-less model: {UnitModels.NoTeamWarnings - before} warning(s) over 3 binds");
        Check(UnitModels.NoTeamWarnings - before == 1, $"a team-less model warned {UnitModels.NoTeamWarnings - before} times over 3 binds, want once per file per run");
        cache.Remove(UnitModels.PathFor(stem));
    }

    // M6-VH7 (BUG-1321): the crowd schedule under a camera. 400 models, a Camera3D over one end: over StaggerFrames frames
    // every shown player advanced at least once; in every frame the FullRateCount nearest advanced, and they are the nearest;
    // a near figure's clip moves every frame, a far one's moves too (by its waiting time), within the period.
    private async Task StaggerRow(GameData data)
    {
        const int perSide = 200;
        var sim = new Simulation(new SimConfig(5, 2, 2 * perSide, 4096) { Data = data });
        World w = sim.World;
        int[] types = { data.FindUnit("malazan_laborer"), data.FindUnit("malazan_crossbowman") };
        NVector2[] west = StartLayout.Block(w.NavGrid, perSide, true, 1f), east = StartLayout.Block(w.NavGrid, perSide, false, 1f);
        for (int i = 0; i < perSide; i++)
        {
            sim.Enqueue(Command.SpawnUnit(0, types[i % 2], west[i]));
            sim.Enqueue(Command.SpawnUnit(1, types[i % 2], east[i]));
        }
        sim.Tick();
        sim.Tick();
        var views = new UnitViews();
        AddChild(views);
        views.Bind(data, 2 * perSide, new uint[] { 0xC03030, 0x3060D0 });
        views.Sync(w, 1f);
        // The eye 30 m over the first west unit (as the match camera would be): the west block is near, the east far.
        UnitStore u = w.Units;
        int anchor = Slot(u, 0, types[0]);
        Vector3 eye = views.ViewOf(anchor)!.GlobalPosition + new Vector3(0f, 30f, 10f);
        CrowdAnimator crowd = views.Crowd;
        int n = 2 * perSide, bad = 0, nearWrong = 0;
        var dist = new float[n];
        for (int i = 0; i < n; i++) dist[i] = views.ViewOf(i) is MeshInstance3D v ? v.GlobalPosition.DistanceSquaredTo(eye) : float.MaxValue;
        var sorted = (float[])dist.Clone();
        Array.Sort(sorted);
        int farSlot = Array.IndexOf(dist, sorted[n - 1]), nearSlot = Array.IndexOf(dist, sorted[0]);
        long start = crowd.Frame;
        double nearPos = views.AnimatorOf(nearSlot)!.CurrentAnimationPosition, farPos = views.AnimatorOf(farSlot)!.CurrentAnimationPosition;
        int nearMoves = 0;
        for (int f = 0; f < 3 * CrowdAnimator.StaggerFrames; f++)
        {
            views.AdvanceAnimations(1.0 / 60.0, eye);
            Check(crowd.EligibleLastFrame == n, $"stagger frame {f}: {crowd.EligibleLastFrame} eligible players, want {n}");
            // The full-rate set: exactly FullRateCount players, none farther than any other player (ties either way).
            int fullCount = 0;
            float farthestFull = 0f, nearestOther = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (crowd.WasFullRate(i))
                {
                    fullCount++;
                    farthestFull = Math.Max(farthestFull, dist[i]);
                    if (crowd.LastAdvanced(i) != crowd.Frame - 1) bad++;
                }
                else nearestOther = Math.Min(nearestOther, dist[i]);
            }
            if (fullCount != CrowdAnimator.FullRateCount || farthestFull > nearestOther) nearWrong++;
            double p = views.AnimatorOf(nearSlot)!.CurrentAnimationPosition;
            if (p != nearPos) nearMoves++;
            nearPos = p;
            if (f >= CrowdAnimator.StaggerFrames - 1)
                for (int i = 0; i < n; i++)
                    if (crowd.Frame - 1 - crowd.LastAdvanced(i) >= CrowdAnimator.StaggerFrames) bad++;
        }
        GD.Print($"stagger: {n} players, {crowd.AdvancedLastFrame} advanced in the last frame, {CrowdAnimator.FullRateCount} nearest every frame, the rest 1 in {CrowdAnimator.StaggerFrames}; near clip moved {nearMoves} of {3 * CrowdAnimator.StaggerFrames} frames");
        Check(bad == 0, $"stagger: {bad} (slot, frame) pairs where a shown player had not advanced within {CrowdAnimator.StaggerFrames} frames or a nearest one missed a frame");
        Check(nearWrong == 0, $"stagger: in {nearWrong} frames the full-rate set was not the {CrowdAnimator.FullRateCount} nearest");
        Check(nearMoves == 3 * CrowdAnimator.StaggerFrames, $"stagger: the nearest figure's clip moved in {nearMoves} of {3 * CrowdAnimator.StaggerFrames} frames, want every frame");
        Check(views.AnimatorOf(farSlot)!.CurrentAnimationPosition != farPos, "stagger: the farthest figure's clip never moved");
        int expect = CrowdAnimator.FullRateCount + (n - CrowdAnimator.FullRateCount) / CrowdAnimator.StaggerFrames;
        Check(Math.Abs(crowd.AdvancedLastFrame - expect) <= 2 + n / 50, $"stagger: {crowd.AdvancedLastFrame} players advanced a frame, want about {expect}");
        // Hidden players are not advanced, and with no eye every shown player advances every frame.
        views.AdvanceAnimations(1.0 / 60.0, null);
        Check(crowd.AdvancedLastFrame == n, $"stagger: with no camera {crowd.AdvancedLastFrame} of {n} advanced, want all");
        u.Free(new EntityHandle(nearSlot, u.Generation[nearSlot]));
        views.Sync(w, 1f);
        views.AdvanceAnimations(1.0 / 60.0, eye);
        Check(crowd.EligibleLastFrame == n - 1 && crowd.LastAdvanced(nearSlot) < crowd.Frame - 1, "stagger: a hidden (dead) figure is still advanced");
        views.QueueFree();
        await Frames(1);
    }

    private void CheckClip(UnitViews views, int slot, string clip, string who)
    {
        AnimationPlayer? anim = views.AnimatorOf(slot);
        string playing = anim?.CurrentAnimation ?? "(no player)";
        Check(playing == clip, $"{who} plays {playing}, want {clip}");
    }

    private static int Slot(UnitStore u, int owner, int type)
    {
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == owner && u.TypeId[i] == type) return i;
        return -1;
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private bool Check(bool ok, string why)
    {
        if (!ok) _failures.Add(why);
        return ok;
    }
}
