using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using NVector2 = System.Numerics.Vector2;

namespace Rts.Game.Tests;

/// <summary>M7-V5: the Teblor figures on the shipped data. Each is drawn at its base figure's height times the data <c>scale</c> (feet on the ground), its rings keep the data radius, and it plays its mapped clip in every sim state.</summary>
/// <remarks>
/// Headless, in the scene loop. Run: <c>&amp; $env:GODOT --headless --path game res://tests/TeblorModelTest.tscn</c>; prints
/// "TEBLOR MODEL TEST PASS" and exits 0, or prints each failure and exits 1. The Teblor model types are the shipped data's
/// Teblor types whose model file loads; the expected clips are <c>tools/asset-manifest.json</c>'s clip maps. Rows:
/// (1) in a real match (Match.tscn, both players), per Teblor model type: the figure's drawn height (its rest pose without the
/// prop surfaces the manifest names) is the type's base figure height times its data <c>scale</c> within 2 %, uniform, with the
/// feet on the ground; its selection ring 1.35x, its target ring 1.7x and its corpse disc 1x the data radius;
/// (2) a forged copy whose Warrior has <c>scale</c> 1.25 (found by id and edited, rule 58): drawn at 1.25x its base figure,
/// the ring unchanged; (3) per Teblor model type, from real sim states, idle, walk and attack, and every other state written
/// into the test's own world (the worker's gather_wood / gather_gold / build / returning, casting): the clip
/// <see cref="UnitViews.ClipFor"/> names is playing, and it is the very <see cref="Animation"/> of the library clip the type's
/// map names (by identity, as M6-V21's rows); no state leaves a figure without a clip.
/// </remarks>
public partial class TeblorModelTest : Node
{
    private const string Tag = "TEBLOR MODEL TEST";
    private readonly List<string> _failures = new();
    private string _scratch = "";
    private GameData _data = null!;
    private JsonNode _manifest = null!;
    private readonly HashSet<string> _propMaterials = new();
    private readonly List<int> _types = new();

    public override async void _Ready()
    {
        _scratch = Path.Combine(Path.GetTempPath(), $"rts-teblor-model-{OS.GetProcessId()}");
        try
        {
            await Run();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        UnitViews.UseModels = true;
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
        _data = loaded.Data!;
        _manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "tools", "asset-manifest.json")))!;
        // The props' own surfaces (a greatsword, a javelin): a figure's height is its body's; a team prop is on the team surface.
        foreach (JsonNode? m in _manifest["models"]!.AsArray())
            foreach (JsonNode? p in m!["props"]?.AsArray() ?? new JsonArray())
                if ((string)p!["material"]! != UnitModels.TeamMaterialName) _propMaterials.Add((string)p["material"]!);

        UnitViews.UseModels = true;
        int teblor = -1;
        for (int f = 0; f < _data.Factions.Length; f++) if (_data.Factions[f].Key == "teblor") teblor = f;
        var probe = new UnitModels(_data, new uint[] { 0xC03030 });
        for (int t = 0; t < _data.Units.Length; t++) if (_data.Units[t].Faction == teblor && probe.Has(t)) _types.Add(t);
        GD.Print($"teblor model types: {string.Join(' ', _types.ConvertAll(t => $"{_data.Units[t].Key} (scale {_data.Units[t].Scale})"))}");
        Check(_types.Count > 0, "no Teblor type draws a model");

        await MatchRows();
        await ForgedScaleRow(dataDir);
        await ClipRows();
    }

    // (1) The shipped data in a real match: height = base x scale, feet on the ground, rings and corpse disc at the radius.
    private async Task MatchRows()
    {
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        try
        {
            match.Start(_data, LaunchOptions.Parse(new[] { "--seed", "1", "--units", "0", "--no-bases", "--no-combat", "--no-fog", "--mute" }));
            var runner = match.GetNode<SimRunner>("SimRunner");
            runner.ProcessMode = ProcessModeEnum.Disabled; // ticked here
            Simulation sim = runner.Simulation!;
            World w = sim.World;
            UnitStore u = w.Units;
            var views = match.GetNode<UnitViews>("World3D/UnitViews");
            var sel = match.GetNode<SelectionController>("SelectionController");
            var rings = match.GetNode<SelectionRings>("World3D/SelectionRings");
            var target = match.GetNode<TargetRing>("World3D/TargetRing");
            var combat = match.GetNode<CombatViews>("World3D/CombatViews");
            match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
            Tick(sim, 2);
            NVector2 mid = w.NavGrid.CellCenter(w.NavGrid.Width / 2, w.NavGrid.Height / 2);
            for (int k = 0; k < _types.Count; k++)
            {
                sim.Enqueue(Command.SpawnUnit(0, _types[k], mid + new NVector2(k * 4f, 0f)));
                sim.Enqueue(Command.SpawnUnit(1, _types[k], mid + new NVector2(k * 4f, 6f)));
            }
            Tick(sim, 3);
            views.Sync(w, 1f);
            await Frames(2);
            foreach (int type in _types)
            {
                UnitDef def = _data.Units[type];
                float baseHeight = BaseHeight(views, type);
                for (int owner = 0; owner < 2; owner++)
                {
                    int slot = Slot(u, owner, type);
                    if (!Check(slot >= 0 && views.ModelMeshOf(slot) != null, $"{def.Key} (player {owner + 1}): not drawn as a model")) continue;
                    Aabb drawn = FigureBox(views.ModelMeshOf(slot)!, views.Models!.AtlasOf(type));
                    float ground = UnitViews.GroundPoint(w, slot, 1f).Y;
                    Vector3 s = views.ModelOf(slot)!.Scale;
                    GD.Print($"{def.Key} (player {owner + 1}): drawn {drawn.Size.Y:F3} m tall, base figure {baseHeight:F3} m x data scale {def.Scale} = {baseHeight * def.Scale:F3} m; feet at {drawn.Position.Y - ground:F3} m over the ground; model scale {s}");
                    Check(Mathf.Abs(drawn.Size.Y - baseHeight * def.Scale) <= 0.02f * baseHeight * def.Scale,
                        $"{def.Key}: drawn {drawn.Size.Y:F3} m tall, want the base {baseHeight:F3} m x scale {def.Scale} within 2 %");
                    Check(s.IsEqualApprox(new Vector3(def.Scale, def.Scale, def.Scale)), $"{def.Key}: model scale {s}, want uniform {def.Scale}");
                    Check(Mathf.Abs(views.ModelOf(slot)!.GlobalPosition.Y - ground) < 1e-3f && Mathf.Abs(drawn.Position.Y - ground) < 0.05f,
                        $"{def.Key}: model origin {views.ModelOf(slot)!.GlobalPosition.Y - ground:F3} m and lowest vertex {drawn.Position.Y - ground:F3} m from the ground, want the feet on it");
                    Check(UnitViews.DrawnRadius(w, slot) == def.Radius, $"{def.Key}: drawn radius {UnitViews.DrawnRadius(w, slot)}, want the data radius {def.Radius}");
                }
            }

            // Rings and the corpse disc keep the data radius (0.75 m for the shipped giants), not the figure's size.
            int first = Slot(u, 0, _types[0]);
            sel.SelectOnly(Handle(u, first));
            foreach (int type in _types) if (type != _types[0]) sel.Selection.Add(Handle(u, Slot(u, 0, type)));
            rings.Sync(w, 1f, sel.Selection.Items);
            Check(rings.ShownCount == _types.Count, $"{rings.ShownCount} rings for {_types.Count} selected");
            for (int k = 0; k < rings.ShownCount && k < sel.Selection.Count; k++)
            {
                UnitDef def = _data.Units[u.TypeId[sel.Selection.Items[k].Index]];
                Check(Mathf.Abs(rings.ShownRadius(k) - def.Radius * SelectionRings.RadiusScale) < 1e-5f,
                    $"{def.Key}: selection ring {rings.ShownRadius(k)}, want {def.Radius} x {SelectionRings.RadiusScale}");
            }
            foreach (int type in _types)
            {
                UnitDef def = _data.Units[type];
                target.Show(Handle(u, Slot(u, 1, type)), isBuilding: false);
                target.Sync(w, 1f, 0f);
                float r = target.Ring.Transform.Basis.X.Length();
                Check(target.Shown && Mathf.Abs(r - def.Radius * TargetRing.UnitScale) < 1e-4f, $"{def.Key}: target ring {r}, want {def.Radius} x {TargetRing.UnitScale}");
            }
            target.Mark.Clear();
            var deaths = new DeathEvent[_types.Count];
            for (int k = 0; k < deaths.Length; k++) deaths[k] = new DeathEvent(new EntityHandle(0, 0), false, _types[k], 0, 1, mid + new NVector2(k * 4f, 20f));
            combat.AddDeaths(w, deaths, sim.TickNumber);
            combat.Sync(w, 1f);
            await Frames(2);
            combat.Sync(w, 1f);
            int corpses = 0;
            for (int m = 0; m < combat.Markers.Capacity; m++)
            {
                if (combat.MarkerShown(m) != 1) continue;
                corpses++;
                UnitDef def = _data.Units[combat.Markers.Type[m]];
                float disc = combat.MarkerTransform(m).Basis.X.Length();
                Check(Mathf.Abs(disc - def.Radius) < 1e-5f, $"{def.Key}: corpse disc {disc}, want the data radius {def.Radius}");
            }
            Check(corpses == deaths.Length, $"{corpses} corpses for {deaths.Length} deaths");
            GD.Print($"rings: {rings.ShownCount} selection rings at {SelectionRings.RadiusScale}x, target ring {TargetRing.UnitScale}x, {corpses} corpse discs at 1x the data radius");
        }
        finally
        {
            RemoveChild(match);
            match.QueueFree();
            await Frames(2);
        }
    }

    // (2) A forged copy whose Warrior has scale 1.25: the figure follows the data (1.25x its base), the ring does not.
    private async Task ForgedScaleRow(string dataDir)
    {
        const float forgedScale = 1.25f;
        string copy = Path.Combine(_scratch, "data");
        foreach (string dir in Directory.GetDirectories(dataDir, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(copy, Path.GetRelativePath(dataDir, dir)));
        foreach (string file in Directory.GetFiles(dataDir, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(copy, Path.GetRelativePath(dataDir, file)), true);
        string path = Path.Combine(copy, "factions", "teblor", "units.json");
        JsonNode units = JsonNode.Parse(File.ReadAllText(path))!;
        int edited = 0;
        foreach (JsonNode? unit in units["units"]!.AsArray())
            if ((string?)unit!["id"] == "teblor_warrior") { unit["scale"] = forgedScale; edited++; }
        File.WriteAllText(path, units.ToJsonString());
        Check(edited == 1, $"forged copy: {edited} teblor_warrior rows edited");
        DataLoadResult loaded = DataLoader.LoadAll(copy);
        if (!Check(loaded.Ok, "the forged copy failed to load")) return;
        GameData forged = loaded.Data!;
        int warrior = forged.FindUnit("teblor_warrior");
        Check(forged.Units[warrior].Scale == forgedScale, $"forged warrior scale {forged.Units[warrior].Scale}");

        var views = new UnitViews();
        AddChild(views);
        views.Bind(forged, 8, new uint[] { 0xC03030, 0x3060D0 });
        var sim = new Simulation(new SimConfig(3, 2, 8, 4096) { Data = forged });
        NVector2[] west = StartLayout.Block(sim.World.NavGrid, 2, true, 1f);
        sim.Enqueue(Command.SpawnUnit(0, warrior, west[0]));
        Tick(sim, 2);
        views.Sync(sim.World, 1f);
        int slot = Slot(sim.World.Units, 0, warrior);
        if (Check(slot >= 0 && views.ModelMeshOf(slot) != null, "the forged warrior is not drawn as a model"))
        {
            float baseHeight = BaseHeight(views, warrior);
            Aabb drawn = FigureBox(views.ModelMeshOf(slot)!, views.Models!.AtlasOf(warrior));
            float ground = UnitViews.GroundPoint(sim.World, slot, 1f).Y;
            GD.Print($"forged scale {forgedScale}: warrior drawn {drawn.Size.Y:F3} m, base {baseHeight:F3} m ({drawn.Size.Y / baseHeight:F3}x), feet {drawn.Position.Y - ground:F3} m, drawn radius {UnitViews.DrawnRadius(sim.World, slot)}");
            Check(Mathf.Abs(drawn.Size.Y - baseHeight * forgedScale) <= 0.02f * baseHeight * forgedScale,
                $"forged scale {forgedScale}: drawn {drawn.Size.Y:F3} m, want {baseHeight * forgedScale:F3} m within 2 %");
            Check(Mathf.Abs(drawn.Position.Y - ground) < 0.05f, $"forged scale {forgedScale}: feet {drawn.Position.Y - ground:F3} m off the ground");
            Check(UnitViews.DrawnRadius(sim.World, slot) == forged.Units[warrior].Radius, "forged scale: the drawn radius moved off the data radius");
        }
        views.QueueFree();
        await Frames(1);
    }

    // (3) Per Teblor model type: every sim state plays the state's clip, the very library Animation the type's map names.
    private async Task ClipRows()
    {
        int enemy = _data.FindUnit("malazan_heavy_infantry");
        foreach (int type in _types)
        {
            UnitDef def = _data.Units[type];
            var sim = new Simulation(new SimConfig(7, 2, 16, 4096) { Data = _data });
            World w = sim.World;
            UnitStore u = w.Units;
            NVector2[] west = StartLayout.Block(w.NavGrid, 4, true, 1f);
            sim.Enqueue(Command.SpawnUnit(0, type, west[0]));
            Tick(sim, 2);
            var views = new UnitViews();
            AddChild(views);
            views.Bind(_data, 16, new uint[] { 0xC03030, 0x3060D0 });
            views.Sync(w, 1f);
            int slot = Slot(u, 0, type);
            if (!Check(slot >= 0 && views.AnimatorOf(slot) != null, $"{def.Key}: no animated model")) { views.QueueFree(); continue; }
            var seen = new List<string>();

            // Real states: standing, walking, fighting a Heavy Infantry (melee or in range, whatever the type's attack is).
            Expect(views, w, slot, type, UnitState.Idle, seen);
            sim.Enqueue(Command.Move(0, Handle(u, slot), u.Position[slot] + new NVector2(12f, 0f)));
            for (int i = 0; i < 40 && u.State[slot] != UnitState.Moving; i++) sim.Tick();
            views.Sync(w, 1f);
            Expect(views, w, slot, type, UnitState.Moving, seen);
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
            Expect(views, w, slot, type, UnitState.Attacking, seen);

            // The other states, written into this test's own world (the view only reads them): every state a unit can be in.
            foreach (UnitState state in Enum.GetValues<UnitState>())
            {
                if (state is UnitState.Idle or UnitState.Moving or UnitState.Attacking) continue;
                if (!u.Alive[slot]) break;
                u.State[slot] = state;
                if (state == UnitState.Gathering)
                {
                    u.GatherNode[slot] = default;
                    foreach (ResourceKind kind in new[] { ResourceKind.Wood, ResourceKind.Gold })
                    {
                        u.CargoKind[slot] = kind;
                        views.Sync(w, 1f);
                        Expect(views, w, slot, type, state, seen);
                    }
                    continue;
                }
                views.Sync(w, 1f);
                Expect(views, w, slot, type, state, seen);
            }
            GD.Print($"{def.Key}: {string.Join(", ", seen)}");
            views.QueueFree();
            await Frames(1);
        }
        // The brief's picks (docs/04 "Animation setup"): read from the manifest the rows above checked against.
        string Attack(string id) => (string?)Model(id)?["clips"]?["attack"] ?? "(none)";
        Check(Attack("teblor_warrior") == "attack_melee" && Attack("teblor_breaker") == "attack_melee", $"Warrior / Breaker attack with {Attack("teblor_warrior")} / {Attack("teblor_breaker")}, want attack_melee");
        Check(Attack("teblor_javelin_thrower") == "attack_throw", $"the Javelin Thrower attacks with {Attack("teblor_javelin_thrower")}, want attack_throw");
        Check(Attack("teblor_shaman") == "attack_cast", $"the Shaman attacks with {Attack("teblor_shaman")}, want attack_cast");
        foreach (string clip in new[] { "gather_gold", "gather_wood", "build" })
            Check((string?)Model("teblor_gatherer")?["clips"]?[clip] == clip, $"the Gatherer's {clip} is {(string?)Model("teblor_gatherer")?["clips"]?[clip]}, want the worker clip");
    }

    // The slot is in `state` (the sim's, or written), its player plays the clip ClipFor names, and that is the library's
    // Animation the type's manifest map names for it (the state's own name when the map is silent).
    private void Expect(UnitViews views, World w, int slot, int type, UnitState state, List<string> seen)
    {
        UnitDef def = _data.Units[type];
        if (!Check(w.Units.Alive[slot] && w.Units.State[slot] == state, $"{def.Key}: wanted state {state}, has {w.Units.State[slot]} (alive {w.Units.Alive[slot]})")) return;
        AnimationPlayer anim = views.AnimatorOf(slot)!;
        string want = UnitModels.ClipNames[(int)UnitViews.ClipFor(w, slot)];
        string playing = anim.CurrentAnimation;
        string source = (string?)Model(def.Key)?["clips"]?[want] ?? want;
        Animation? expected = UnitModels.Library?.HasAnimation(source) == true ? UnitModels.Library.GetAnimation(source) : null;
        Animation? actual = anim.HasAnimation(playing) ? anim.GetAnimation(playing) : null;
        string label = state == UnitState.Gathering ? $"{state} {w.Units.CargoKind[slot]}" : state.ToString();
        seen.Add($"{label} -> {playing} = library {source}");
        Check(playing == want, $"{def.Key} {label}: plays '{playing}', want '{want}'");
        Check(expected != null && actual == expected, $"{def.Key} {label}: plays {actual?.ResourceName} ({actual?.Length:F2} s), want the library's {source} ({expected?.Length:F2} s)");
        Check(anim.IsPlaying() && actual != null, $"{def.Key} {label}: no clip playing (a T-pose)");
    }

    private JsonNode? Model(string id)
    {
        foreach (JsonNode? m in _manifest["models"]!.AsArray()) if ((string?)m!["id"] == id) return m;
        return null;
    }

    // The type's figure as its file holds it (unscaled, at the origin): its height without the prop surfaces.
    private float BaseHeight(UnitViews views, int type)
    {
        var holder = new Node3D();
        AddChild(holder);
        Node3D model = views.Models!.Instantiate(type, out MeshInstance3D mesh, out AnimationPlayer _);
        holder.AddChild(model);
        float h = FigureBox(mesh, views.Models!.AtlasOf(type)).Size.Y;
        holder.Free();
        return h;
    }

    // The rest-pose box of a figure's body in world space: every surface but the props' own materials. M6-V24: on the one
    // surface of an atlas model the props' own faces are the vertices reading the props' tile (a team prop reads the team
    // tile and counts, as it did on the team surface); the surface check stays for a pre-atlas file.
    private Aabb FigureBox(MeshInstance3D mi, UnitModels.Atlas? atlas)
    {
        Mesh mesh = mi.Mesh;
        Transform3D x = mi.GlobalTransform;
        Aabb box = default;
        bool any = false;
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            if (mesh.SurfaceGetMaterial(s) is BaseMaterial3D mat && _propMaterials.Contains(mat.ResourceName)) continue;
            Godot.Collections.Array arrays = mesh.SurfaceGetArrays(s);
            Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            Vector2[] uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            for (int k = 0; k < vertices.Length; k++)
            {
                if (atlas != null && k < uvs.Length && atlas.IsProp(uvs[k])) continue;
                Vector3 p = x * vertices[k];
                box = any ? box.Expand(p) : new Aabb(p, Vector3.Zero);
                any = true;
            }
        }
        return box;
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
