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

/// <summary>M6-V25: the last four foot units drawn as figures (the Cadre Mage, the Sapper, the Priest of the Whirlwind and the Zealot), on the shipped data.</summary>
/// <remarks>
/// Headless, in the scene loop. Run: <c>&amp; $env:GODOT --headless --path game res://tests/FootModelTest.tscn</c>; prints
/// "FOOT MODEL TEST PASS" and exits 0, or prints each failure and exits 1. The four types are found by id in the data and
/// in <c>tools/asset-manifest.json</c>; every expectation (clip maps, triangle caps, props) is read from the manifest. Rows,
/// per type: (1) the file is one mesh, one surface, one material named <c>team</c> drawing an atlas with a team tile and a
/// props tile, at most its manifest's triangles, the 65-bone rig, the figure (without its props' own faces) 1.8 m within
/// 5 %, and the two players' materials share the atlas and differ in the colour only; (2) the clip map: from real sim
/// states idle, walk, attack (on a Heavy Infantry) and, for a type with an ability, casting it (aimed at a point in its
/// range), and every other state written into the test's own world, the playing clip is the one
/// <see cref="UnitViews.ClipFor"/> names and the very <see cref="Animation"/> of the library clip the manifest maps it to;
/// no state leaves a figure without a clip; (3) the cast gesture, for each type with an ability (the two casters and the
/// Sapper's Cusser): at 60 FPS and 20 ticks a second its clip starts at its first frame when the cast is first seen, never
/// steps back, ends within one frame of its end as the ability lands (the cast's length from the shipped data's
/// <c>castTime</c>), then idle.
/// </remarks>
public partial class FootModelTest : Node
{
    private const string Tag = "FOOT MODEL TEST";

    // The brief's four (M6-V25); everything else about them is read from the data and the manifest.
    private static readonly string[] Ids = { "malazan_cadre_mage", "malazan_sapper", "whirlwind_priest", "whirlwind_zealot" };

    private static readonly uint[] Colours = { 0xC03030, 0x3060D0 };

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private JsonNode _manifest = null!;

    public override async void _Ready()
    {
        try
        {
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            _manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "tools", "asset-manifest.json")))!;
            UnitViews.UseModels = true;
            var models = new UnitModels(_data, Colours);
            foreach (string id in Ids)
            {
                int type = _data.FindUnit(id);
                JsonNode? entry = Model(id);
                if (!Check(type >= 0 && entry != null, $"{id}: in the data {type >= 0}, in the manifest {entry != null}")) continue;
                if (!SurfaceRows(models, type, entry!)) continue;
                await ClipRows(type, entry!);
                foreach (int ability in _data.Units[type].Abilities) await GestureRow(type, ability);
            }
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        UnitViews.UseModels = true;
        foreach (string f in _failures) GD.Print($"{Tag} FAIL: {f}");
        if (_failures.Count == 0) GD.Print($"{Tag} PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // (1) One surface, the team material, the atlas, the triangles, the rig and the figure's height; the players' materials.
    private bool SurfaceRows(UnitModels models, int type, JsonNode m)
    {
        string id = _data.Units[type].Key;
        if (!Check(models.Has(type), $"{id}: not drawn as a model")) return false;
        var holder = new Node3D();
        AddChild(holder);
        Node3D model = models.Instantiate(type, out MeshInstance3D mesh, out AnimationPlayer _);
        holder.AddChild(model);
        try
        {
            int meshes = model.FindChildren("*", "MeshInstance3D", true, false).Count;
            Mesh arrayMesh = mesh.Mesh;
            var material = arrayMesh.GetSurfaceCount() == 1 ? arrayMesh.SurfaceGetMaterial(0) as BaseMaterial3D : null;
            UnitModels.Atlas? atlas = models.AtlasOf(type);
            Texture2D? texture = material?.AlbedoTexture;
            int tris = arrayMesh.GetSurfaceCount() == 1 ? arrayMesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Index].AsInt32Array().Length / 3 : -1;
            var skeleton = model.FindChildren("*", "Skeleton3D", true, false).Count > 0 ? model.FindChildren("*", "Skeleton3D", true, false)[0] as Skeleton3D : null;
            int cap = (int)m["maxTriangles"]!;
            GD.Print($"{id}: {meshes} mesh, {arrayMesh.GetSurfaceCount()} surface, material '{material?.ResourceName}', atlas {atlas?.Grid} tiles [{(atlas == null ? "" : string.Join(' ', atlas.Tiles.Keys))}], {tris} triangles (cap {cap}), {skeleton?.GetBoneCount()} bones");
            Check(meshes == 1 && arrayMesh.GetSurfaceCount() == 1 && material?.ResourceName == UnitModels.TeamMaterialName && models.TeamSurface(type) == 0,
                $"{id}: {meshes} meshes, {arrayMesh.GetSurfaceCount()} surfaces, material '{material?.ResourceName}'; want one surface with the one 'team' material");
            Check(atlas != null && texture != null && atlas.Tiles.ContainsKey("team") && atlas.Tiles.ContainsKey(UnitModels.PropsTile),
                $"{id}: no atlas, or no team / props tile in it");
            Check(tris > 0 && tris <= cap, $"{id}: {tris} triangles, cap {cap}");
            Check(skeleton?.GetBoneCount() == 65, $"{id}: {skeleton?.GetBoneCount()} bones, want the 65-bone Universal rig");
            float height = FigureHeight(mesh, atlas);
            GD.Print($"{id}: figure {height:F3} m tall without its props' own faces, data scale {_data.Units[type].Scale}");
            Check(Mathf.Abs(height - 1.8f) <= 0.05f * 1.8f, $"{id}: figure {height:F3} m tall, want 1.8 m within 5 %");
            ShaderMaterial? first = models.TeamMaterial(type, 0), second = models.TeamMaterial(type, 1);
            bool shared = first != null && second != null && texture != null
                          && first.GetShaderParameter("albedo_texture").As<Texture2D>() == texture
                          && second.GetShaderParameter("albedo_texture").As<Texture2D>() == texture
                          && first.GetShaderParameter("use_atlas").AsBool() && second.GetShaderParameter("use_atlas").AsBool()
                          && first.GetShaderParameter("team_colour").AsColor() == UnitViews.ColorFromRgb(Colours[0])
                          && second.GetShaderParameter("team_colour").AsColor() == UnitViews.ColorFromRgb(Colours[1]);
            Check(shared, $"{id}: the players' materials don't share the atlas or don't carry their colours");
            return true;
        }
        finally
        {
            holder.Free();
        }
    }

    // The rest-pose height of every vertex but those on the props' tile (a robe, a staff, a satchel).
    private static float FigureHeight(MeshInstance3D mi, UnitModels.Atlas? atlas)
    {
        float lo = float.MaxValue, hi = float.MinValue;
        Godot.Collections.Array arrays = mi.Mesh.SurfaceGetArrays(0);
        Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        Vector2[] uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        Transform3D x = mi.GlobalTransform;
        for (int k = 0; k < vertices.Length; k++)
        {
            if (atlas != null && k < uvs.Length && atlas.IsProp(uvs[k])) continue;
            float y = (x * vertices[k]).Y;
            lo = Math.Min(lo, y);
            hi = Math.Max(hi, y);
        }
        return hi - lo;
    }

    // (2) Every state the type can be in plays a clip: the real ones from the sim, the rest written into this test's world.
    private async Task ClipRows(int type, JsonNode m)
    {
        UnitDef def = _data.Units[type];
        int enemy = _data.FindUnit("malazan_heavy_infantry");
        var sim = new Simulation(new SimConfig(7, 2, 16, 4096) { Data = _data });
        World w = sim.World;
        UnitStore u = w.Units;
        NVector2[] west = StartLayout.Block(w.NavGrid, 4, true, 1f);
        sim.Enqueue(Command.SpawnUnit(0, type, west[0]));
        Tick(sim, 2);
        var views = new UnitViews();
        AddChild(views);
        views.Bind(_data, 16, Colours);
        views.Sync(w, 1f);
        int slot = Slot(u, 0, type);
        try
        {
            if (!Check(slot >= 0 && views.AnimatorOf(slot) != null, $"{def.Key}: no animated model")) return;
            var seen = new List<string>();
            Expect(views, w, slot, m, UnitState.Idle, seen);
            sim.Enqueue(Command.Move(0, Handle(u, slot), u.Position[slot] + new NVector2(12f, 0f)));
            for (int i = 0; i < 40 && u.State[slot] != UnitState.Moving; i++) sim.Tick();
            views.Sync(w, 1f);
            Expect(views, w, slot, m, UnitState.Moving, seen);
            // The cast, when the type has one: aimed 2 m ahead (inside every ground-targeted ability's range).
            if (def.Abilities.Length > 0)
            {
                sim.Enqueue(Command.Stop(0, Handle(u, slot)));
                Tick(sim, 2);
                sim.Enqueue(Command.UseAbility(0, Handle(u, slot), 0, u.Position[slot] + new NVector2(2f, 0f)));
                for (int i = 0; i < 10 && u.State[slot] != UnitState.Casting; i++) sim.Tick();
                views.Sync(w, 1f);
                Expect(views, w, slot, m, UnitState.Casting, seen);
                for (int i = 0; i < 80 && u.State[slot] == UnitState.Casting; i++) sim.Tick();
            }
            sim.Enqueue(Command.SpawnUnit(1, enemy, u.Position[slot] + new NVector2(0f, 6f)));
            sim.Tick();
            for (int i = 0; i < 600 && u.Alive[slot] && u.State[slot] != UnitState.Attacking; i++)
            {
                if (i % 40 == 0)
                {
                    int foe = Slot(u, 1, enemy);
                    if (foe >= 0) sim.Enqueue(Command.Attack(0, Handle(u, slot), Handle(u, foe), isBuilding: false));
                }
                sim.Tick();
            }
            views.Sync(w, 1f);
            Expect(views, w, slot, m, UnitState.Attacking, seen);
            // The other states, written into this test's own world (the view only reads them).
            foreach (UnitState state in Enum.GetValues<UnitState>())
            {
                if (state is UnitState.Idle or UnitState.Moving or UnitState.Attacking) continue;
                if (state == UnitState.Casting && def.Abilities.Length > 0) continue; // seen for real above
                if (!u.Alive[slot]) break;
                u.State[slot] = state;
                if (state == UnitState.Gathering)
                {
                    u.GatherNode[slot] = default;
                    foreach (ResourceKind kind in new[] { ResourceKind.Wood, ResourceKind.Gold })
                    {
                        u.CargoKind[slot] = kind;
                        views.Sync(w, 1f);
                        Expect(views, w, slot, m, state, seen);
                    }
                    continue;
                }
                views.Sync(w, 1f);
                Expect(views, w, slot, m, state, seen);
            }
            GD.Print($"{def.Key}: {string.Join(", ", seen)}");
        }
        finally
        {
            views.QueueFree();
            await Frames(1);
        }
    }

    // The slot is in `state`, its player plays the clip ClipFor names, and that is the library's Animation the type's manifest
    // map names for it (the state's own name when the map is silent).
    private void Expect(UnitViews views, World w, int slot, JsonNode m, UnitState state, List<string> seen)
    {
        string id = (string)m["id"]!;
        if (!Check(w.Units.Alive[slot] && w.Units.State[slot] == state, $"{id}: wanted state {state}, has {w.Units.State[slot]} (alive {w.Units.Alive[slot]})")) return;
        AnimationPlayer anim = views.AnimatorOf(slot)!;
        string want = UnitModels.ClipNames[(int)UnitViews.ClipFor(w, slot)];
        string playing = anim.CurrentAnimation;
        string source = (string?)m["clips"]?[want] ?? want;
        Animation? expected = UnitModels.Library?.HasAnimation(source) == true ? UnitModels.Library.GetAnimation(source) : null;
        Animation? actual = anim.HasAnimation(playing) ? anim.GetAnimation(playing) : null;
        string label = state == UnitState.Gathering ? $"{state} {w.Units.CargoKind[slot]}" : state.ToString();
        seen.Add($"{label} -> {playing} = library {source}");
        Check(playing == want, $"{id} {label}: plays '{playing}', want '{want}'");
        Check(expected != null && actual == expected, $"{id} {label}: plays {actual?.ResourceName} ({actual?.Length:F2} s), want the library's {source} ({expected?.Length:F2} s)");
        Check(anim.IsPlaying() && actual != null, $"{id} {label}: no clip playing (a T-pose)");
    }

    // (3) The cast gesture follows the cast (M6-V24's rule, BUG-1530) at the shipped castTime: from 0 when first seen, once,
    // ending as the ability lands, then idle.
    private async Task GestureRow(int type, int ability)
    {
        UnitDef def = _data.Units[type];
        AbilityDef ab = _data.Abilities[ability];
        int castTicks = ab.CastTicks;
        var views = new UnitViews();
        AddChild(views);
        views.Bind(_data, 8, Colours);
        var sim = new Simulation(new SimConfig(3, 2, 8, 4096) { Data = _data, Combat = false });
        UnitStore u = sim.World.Units;
        NVector2[] west = StartLayout.Block(sim.World.NavGrid, 2, true, 1f);
        // Another unit first, so the caster is in slot 1 (BUG-1530's case: a crowd phase other than 0).
        sim.Enqueue(Command.SpawnUnit(0, _data.FindUnit("malazan_heavy_infantry"), west[1]));
        sim.Enqueue(Command.SpawnUnit(0, type, west[0]));
        Tick(sim, 2);
        int slot = Slot(u, 0, type);
        views.Sync(sim.World, 1f);
        AnimationPlayer? anim = slot >= 0 ? views.AnimatorOf(slot) : null;
        try
        {
            if (!Check(anim != null, $"(3) {def.Key}: no model")) return;
            for (int f = 0; f < 20; f++) { views.Sync(sim.World, 1f, 1f / 60f); views.AdvanceAnimations(1.0 / 60.0, null); }
            sim.Enqueue(Command.UseAbility(0, Handle(u, slot), def.Abilities.IndexOf(ability), u.Position[slot] + new NVector2(2f, 0f)));
            for (int k = 0; k < 6 && u.State[slot] != UnitState.Casting; k++) sim.Tick();
            if (!Check(u.State[slot] == UnitState.Casting, $"(3) {def.Key}: not casting {ab.Key} ({u.State[slot]})")) return;
            float length = views.Models!.ClipLength(type, UnitModels.Clip.Cast);
            const int perTick = 3; // 60 FPS at 20 ticks a second
            float frameClip = length / ((castTicks - 1) * perTick);
            var positions = new List<float>();
            string start = "";
            int frames = 0;
            while (u.State[slot] == UnitState.Casting && frames < 600)
            {
                for (int f = 0; f < perTick && u.State[slot] == UnitState.Casting; f++, frames++)
                {
                    views.Sync(sim.World, (float)f / perTick, 1f / 60f);
                    views.AdvanceAnimations(1.0 / 60.0, null);
                    if (frames == 0) start = anim!.CurrentAnimation;
                    positions.Add((float)anim!.CurrentAnimationPosition);
                }
                sim.Tick();
            }
            views.Sync(sim.World, 0f, 1f / 60f);
            views.AdvanceAnimations(1.0 / 60.0, null);
            string after = anim!.CurrentAnimation;
            int wraps = 0;
            for (int k = 1; k < positions.Count; k++) if (positions[k] < positions[k - 1]) wraps++;
            float first = positions.Count > 0 ? positions[0] : -1f, last = positions.Count > 0 ? positions[^1] : -1f;
            string source = views.Models.ClipSource(type, UnitModels.Clip.Cast) ?? "";
            GD.Print($"(3) {def.Key} casting {ab.Key} ({ab.CastTicks} ticks, seen {castTicks - 1}): '{start}' = library {source} {length:F2} s over {positions.Count} frames, first {first:F3} s, last {last:F3} s, {wraps} steps back; then '{after}'");
            Check(start == UnitModels.ClipNames[(int)UnitModels.Clip.Cast], $"(3) {def.Key}: plays '{start}' on the cast's first frame, want attack_cast");
            Check(first >= 0f && first <= frameClip + 1e-4f, $"(3) {def.Key}: the gesture starts at {first:F3} s, want 0 within one frame ({frameClip:F3} s)");
            Check(wraps == 0, $"(3) {def.Key}: the gesture stepped back {wraps} times (looped)");
            Check(last >= length - frameClip - 2e-3f && last < length, $"(3) {def.Key}: the gesture ends the cast at {last:F3} s of {length:F3} s, want its end within one frame");
            Check(positions.Count == (castTicks - 1) * perTick, $"(3) {def.Key}: {positions.Count} frames of cast, want {(castTicks - 1) * perTick}");
            Check(after == "idle", $"(3) {def.Key}: after the cast plays '{after}', want idle");
        }
        finally
        {
            views.QueueFree();
            await Frames(1);
        }
    }

    private JsonNode? Model(string id)
    {
        foreach (JsonNode? m in _manifest["models"]!.AsArray()) if ((string?)m!["id"] == id) return m;
        return null;
    }

    private static EntityHandle Handle(UnitStore u, int slot) => new(slot, u.Generation[slot]);

    private static int Slot(UnitStore u, int owner, int type)
    {
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == owner && u.TypeId[i] == type) return i;
        return -1;
    }

    private static void Tick(Simulation sim, int n)
    {
        for (int i = 0; i < n; i++) sim.Tick();
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
