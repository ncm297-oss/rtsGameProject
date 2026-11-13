using System;
using System.Collections.Generic;
using System.IO;
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
        await MenuRow(data);
        ForgedRows(dataDir, data);
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
        Check(box.Size.Y > 1.7f && box.Size.Y < 1.95f, $"{id}: {box.Size.Y} m tall, want about 1.8");
        Check(Mathf.Abs(box.Position.Y) < 0.05f, $"{id}: feet at {box.Position.Y} m, want the origin");
        Check(toes.Z < -0.05f, $"{id}: toes point {toes}, want -Z");
        Check(skeleton?.GetBoneCount() == 65, $"{id}: {skeleton?.GetBoneCount()} bones, want the 65-bone Universal rig");
        holder.Free();
    }

    // Criterion 3 (d): the library holds every clip a model's clip map names, each looping.
    private void LibraryRows(JsonNode manifest)
    {
        AnimationLibrary? lib = UnitModels.Library;
        Check(lib != null, "the animation library did not load");
        if (lib == null) return;
        var names = new SortedSet<string>();
        foreach (JsonNode? m in manifest["models"]!.AsArray())
            foreach (KeyValuePair<string, JsonNode?> clip in m!["clips"]!.AsObject()) names.Add(clip.Key);
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
        Check(views.ModelMeshOf(lab)?.MaterialOverlay == views.TintOf(UnitState.Building), "the build tint is not on the model's mesh");
        u.State[lab] = UnitState.Gathering;
        u.GatherNode[lab] = default;
        u.CargoKind[lab] = ResourceKind.Wood;
        views.Sync(w, 1f);
        CheckClip(views, lab, "gather_wood", "a laborer gathering wood");
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
        int[] kinds = { heavy, laborer, heavy };
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
            if (model) Check(views.ModelOf(a) != first && (views.ModelMeshOf(a)?.GetSurfaceOverrideMaterial(views.Models.TeamSurface(kind)) as ShaderMaterial)?.GetShaderParameter("team_colour").AsColor() == UnitViews.ColorFromRgb(rgb[1]),
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
        foreach (int t in new[] { shipped.FindUnit("malazan_laborer"), shipped.FindUnit("malazan_crossbowman") })
        {
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
        views.Sync(w, 0.5f);
        await Frames(3);
        long bytes = 0;
        for (int f = 0; f < 120; f++)
        {
            long start = GC.GetAllocatedBytesForCurrentThread();
            views.Sync(w, (f % 10) / 10f, 1f / 60f);
            bytes += GC.GetAllocatedBytesForCurrentThread() - start;
        }
        GD.Print($"steady: {views.ModelNodeCount} models, 120 syncs allocated {bytes} bytes");
        Check(views.ModelNodeCount == 2 * perSide, $"{views.ModelNodeCount} model instances for {2 * perSide} units");
        Check(bytes == 0, $"120 steady syncs of {2 * perSide} models allocated {bytes} bytes");
        views.QueueFree();
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

    private void Check(bool ok, string why)
    {
        if (!ok) _failures.Add(why);
    }
}
