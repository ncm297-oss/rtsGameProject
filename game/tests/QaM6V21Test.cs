using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using NVector2 = System.Numerics.Vector2;

namespace Rts.Game.Tests;

/// <summary>QA for M6-V21 (four more unit models, per-type clip maps, figures follow the game speed): what ModelViewTest does not reach.</summary>
/// <remarks>
/// Headless. Run: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6V21Test.tscn</c>; prints "QA M6-V21 TEST PASS" and
/// exits 0, or prints each failure and exits 1. Rows:
/// (1) game speed changed mid-clip, several times, with the tree paused and resumed between: 400 walking models of every
///     model type under a camera; after every frame each figure's walk is exactly the sum of (frame delta x speed) over the
///     frames up to the last one it was advanced in (no jump, no drift between the 48 nearest and the staggered rest), and a
///     staggered figure is never more than StaggerFrames - 1 frames behind; the paused frames add nothing;
/// (2) the real _Process path: a pausable UnitViews under a paused tree for real frames advances nothing; resumed it does;
/// (3) slot reuse fuzz over every unit type (model and capsule), two owners, fog on: after every Sync each live slot draws
///     exactly its type (a model with that type's own animation library and one "Model" child, or the type's capsule), in its
///     owner's colour when shown, its player active iff shown; units are killed and respawned in sight and in the fog;
/// (4) per-type clip maps: each model type's library names only its map's clips (the Heavy Infantry's and Raider's attack is
///     the library's attack_melee, everyone else's the library's attack); a Camp Follower's three worker clips exist.
/// M7-V5 (the developer, with the Teblor figures): row (1) wants one model type per manifest entry (read from the file, 11),
/// row (4) keeps those pins for M6-V21's six types and checks a later type's attack against its manifest entry.
/// </remarks>
public partial class QaM6V21Test : Node
{
    private const string Tag = "QA M6-V21 TEST";
    private readonly List<string> _failures = new();
    private GameData _data = null!;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        UnitViews.UseModels = true;
        try
        {
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            SpeedChangeRow();
            await RealPauseRow();
            await ReuseFuzz();
            ClipMapRow();
            FlashReuseRow();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        foreach (string f in _failures) GD.Print($"{Tag} FAIL: {f}");
        if (_failures.Count == 0) GD.Print($"{Tag} PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // M7-V5: the asset manifest (the model entries and their clip maps), read once.
    private System.Text.Json.Nodes.JsonNode? _manifest;
    private System.Text.Json.Nodes.JsonNode Manifest() => _manifest ??= System.Text.Json.Nodes.JsonNode.Parse(System.IO.File.ReadAllText(
        System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "tools", "asset-manifest.json")))!;

    private string ManifestAttack(string id)
    {
        foreach (System.Text.Json.Nodes.JsonNode? m in Manifest()["models"]!.AsArray())
            if ((string?)m!["id"] == id) return (string?)m["clips"]?["attack"] ?? "attack";
        return "(no manifest entry)";
    }

    private int[] ModelTypes(UnitViews views)
    {
        var list = new List<int>();
        for (int t = 0; t < _data.Units.Length; t++) if (views.DrawsModel(t)) list.Add(t);
        return list.ToArray();
    }

    // (1) Speed changes mid-clip and a pause mid-stagger, checked exactly per figure.
    private void SpeedChangeRow()
    {
        const int perSide = 200;
        const double dt = 1.0 / 60.0;
        var sim = new Simulation(new SimConfig(11, 2, 2 * perSide, 4096) { Data = _data, Combat = false });
        World w = sim.World;
        UnitStore u = w.Units;
        var runner = new SimRunner { Name = "QaSpeedRunner" };
        AddChild(runner);
        var views = new UnitViews { Runner = runner, ProcessMode = ProcessModeEnum.Pausable };
        AddChild(views);
        views.Bind(_data, 2 * perSide, new uint[] { 0xC03030, 0x3060D0 });
        int[] types = ModelTypes(views);
        // M7-V5: one model type per manifest entry (6 at M6-V21, 11 with the Teblor figures), read from the file.
        int wantTypes = Manifest()["models"]!.AsArray().Count;
        Check(types.Length == wantTypes, $"(1) {types.Length} model types on the shipped data, want {wantTypes} (the manifest's entries)");
        NVector2[] west = StartLayout.Block(w.NavGrid, perSide, true, 1f), east = StartLayout.Block(w.NavGrid, perSide, false, 1f);
        for (int i = 0; i < perSide; i++)
        {
            sim.Enqueue(Command.SpawnUnit(0, types[i % types.Length], west[i]));
            sim.Enqueue(Command.SpawnUnit(1, types[(i + 3) % types.Length], east[i]));
        }
        sim.Tick();
        sim.Tick();
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i]) u.State[i] = UnitState.Moving;
        views.Sync(w, 1f);
        int anchor = -1;
        for (int i = 0; i < u.Capacity && anchor < 0; i++) if (u.Alive[i] && u.Owner[i] == 0) anchor = i;
        Vector3 eye = views.ViewOf(anchor)!.GlobalPosition + new Vector3(0f, 30f, 10f);
        // Warm the schedule, then put every walk at 0 with nothing waiting.
        for (int k = 0; k < CrowdAnimator.StaggerFrames; k++) views.AdvanceAnimations(dt, eye);
        for (int k = 0; k < CrowdAnimator.StaggerFrames; k++) views.AdvanceAnimations(0.0, eye);
        for (int i = 0; i < u.Capacity; i++) views.AnimatorOf(i)?.Seek(0, true);
        long frame0 = views.Crowd.Frame;
        // The sequence: speeds, with 0 meaning "the tree is paused for this frame". Changes fall on every stagger phase.
        double[] seq = { 1, 2, 2, 0.75, 0, 0, 1.5, 8, 0.25, 0, 1, 1, 2, 0.75, 0, 0, 0, 0.75, 1.5, 1, 2, 0.25, 0, 1, 1 };
        var cum = new List<double> { 0.0 }; // cum[f - frame0] = clip seconds over schedule frames before frame f
        double total = 0;
        int worstLag = 0, bad = 0, checks = 0;
        string firstBad = "";
        foreach (double s in seq)
        {
            bool paused = s == 0;
            if (paused) GetTree().Paused = true;
            else runner.GameSpeed = s;
            long before = views.Crowd.Frame;
            views.AdvanceAnimations(dt, eye);
            if (paused)
            {
                Check(views.Crowd.Frame == before && views.AnimationRate == 0.0, $"(1) a paused frame ran the schedule (rate {views.AnimationRate})");
                GetTree().Paused = false;
                continue;
            }
            total += dt * s;
            cum.Add(total);
            long last = views.Crowd.Frame - 1;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (views.AnimatorOf(i) is not AnimationPlayer p) continue;
                long at = views.Crowd.LastAdvanced(i);
                int idx = (int)(at - frame0 + 1);
                if (at < frame0 - 1 || idx < 0 || idx >= cum.Count) continue; // not advanced since the reset (cum[0] = 0 then)
                worstLag = Math.Max(worstLag, (int)(last - at));
                float length = views.Models!.ClipLength(u.TypeId[i], UnitModels.Clip.Walk);
                double want = at < frame0 ? 0.0 : cum[idx] % length;
                checks++;
                if (Math.Abs(p.CurrentAnimationPosition - want) > 1e-4)
                {
                    if (bad++ == 0) firstBad = $"slot {i} (full rate {views.Crowd.WasFullRate(i)}, last advanced {at}, frame {last}) at {p.CurrentAnimationPosition:F5} s, want {want:F5}";
                }
            }
        }
        GD.Print($"(1) speed changes and pauses mid-clip: {seq.Length} frames, {checks} figure checks, {bad} off (first: {firstBad}), worst lag {worstLag} frames, clip seconds {total:F4}");
        Check(bad == 0, $"(1) {bad} figure positions off the summed game-speed time; first: {firstBad}");
        Check(worstLag <= CrowdAnimator.StaggerFrames - 1, $"(1) a staggered figure lagged {worstLag} frames, want at most {CrowdAnimator.StaggerFrames - 1}");
        // Flush: after StaggerFrames frames at 0 delta, every figure sits at the full total (near and far agree).
        for (int k = 0; k < CrowdAnimator.StaggerFrames; k++) views.AdvanceAnimations(0.0, eye);
        double min = double.MaxValue, max = double.MinValue;
        for (int i = 0; i < u.Capacity; i++)
            if (views.AnimatorOf(i) is AnimationPlayer p)
            {
                min = Math.Min(min, p.CurrentAnimationPosition);
                max = Math.Max(max, p.CurrentAnimationPosition);
            }
        GD.Print($"(1) flushed: every walk {min:F5} - {max:F5} s, want {total:F5}");
        Check(Math.Abs(min - total) < 1e-4 && Math.Abs(max - total) < 1e-4, $"(1) flushed walks {min:F5} - {max:F5}, want {total:F5}: near and staggered drifted apart");
        views.Free();
        runner.Free();
    }

    // (2) The real per-frame path: a pausable UnitViews in a paused tree advances nothing over real frames.
    private async Task RealPauseRow()
    {
        var sim = new Simulation(new SimConfig(12, 2, 16, 4096) { Data = _data, Combat = false });
        World w = sim.World;
        UnitStore u = w.Units;
        var views = new UnitViews { ProcessMode = ProcessModeEnum.Pausable };
        AddChild(views);
        views.Bind(_data, 16, new uint[] { 0xC03030, 0x3060D0 });
        int heavy = _data.FindUnit("malazan_heavy_infantry");
        NVector2[] west = StartLayout.Block(w.NavGrid, 2, true, 1f);
        sim.Enqueue(Command.SpawnUnit(0, heavy, west[0]));
        sim.Tick();
        sim.Tick();
        int slot = Slot(u, 0, heavy);
        u.State[slot] = UnitState.Moving;
        views.Sync(w, 1f);
        AnimationPlayer? anim = views.AnimatorOf(slot);
        if (!Check(anim != null, "(2) no model player for a heavy infantry")) { views.QueueFree(); return; }
        await Frames(3);
        double a = anim!.CurrentAnimationPosition;
        GetTree().Paused = true;
        await Frames(6);
        double b = anim.CurrentAnimationPosition;
        GetTree().Paused = false;
        await Frames(3);
        double c = anim.CurrentAnimationPosition;
        GD.Print($"(2) real frames: walk at {a:F4} s, after 6 paused frames {b:F4} s, after 3 resumed {c:F4} s");
        Check(a == b, $"(2) 6 paused frames moved the walk {a:F4} -> {b:F4}");
        Check(c != b, "(2) resumed: the walk does not advance");
        views.QueueFree();
        await Frames(1);
    }

    // (3) Slot reuse fuzz over every type, two owners, with fog.
    private async Task ReuseFuzz()
    {
        const int cap = 24;
        var sim = new Simulation(new SimConfig(13, 2, cap, 4096) { Data = _data, Combat = false });
        World w = sim.World;
        UnitStore u = w.Units;
        NVector2[] west = StartLayout.Block(w.NavGrid, 8, true, 1f);
        NVector2 home = west[0];
        // The watcher (player 0's eyes) stays alive at home in slot 0.
        int watcherType = _data.FindUnit("malazan_crossbowman");
        sim.Enqueue(Command.SpawnUnit(0, watcherType, home));
        sim.Tick();
        sim.Tick();
        var fog = new FogOfWar();
        AddChild(fog);
        fog.Bind(w, 0, true);
        var views = new UnitViews { Fog = fog };
        AddChild(views);
        uint[] rgb = { 0xC03030, 0x3060D0 };
        views.Bind(_data, cap, rgb);
        int watcher = Slot(u, 0, watcherType);
        // Near points (in sight) and far points (fog) that are passable.
        var near = new List<NVector2>();
        var far = new List<NVector2>();
        for (int k = 1; k < west.Length; k++) near.Add(west[k]);
        foreach (NVector2 off in new[] { new NVector2(70f, 0f), new NVector2(70f, 20f), new NVector2(90f, 10f), new NVector2(60f, 40f) })
        {
            NVector2 p = home + off;
            int cx = (int)(p.X / Rts.Sim.Map.MapConstants.CellSize), cy = (int)(p.Y / Rts.Sim.Map.MapConstants.CellSize);
            if (cx < 0 || cy < 0 || cx >= w.NavGrid.Width || cy >= w.NavGrid.Height) continue;
            int cell = Rts.Sim.Pathfinding.FlowField.NearestPassable(w.NavGrid, cy * w.NavGrid.Width + cx);
            far.Add(w.NavGrid.CellCenter(cell % w.NavGrid.Width, cell / w.NavGrid.Width));
        }
        var rng = new Random(2111);
        int steps = 0, modelSeen = 0, capsuleSeen = 0, hiddenModels = 0, typeChanges = 0, errors = 0;
        string first = "";
        int[] lastType = new int[cap];
        Array.Fill(lastType, -1);
        for (int step = 0; step < 300; step++)
        {
            // Kill one or two random live non-watcher units, then spawn one or two of random types (the free list reuses slots).
            int kills = rng.Next(0, 3);
            for (int k = 0; k < kills; k++)
            {
                int s = rng.Next(cap);
                if (s != watcher && u.Alive[s]) u.Free(new EntityHandle(s, u.Generation[s]));
            }
            int spawns = rng.Next(1, 3);
            for (int k = 0; k < spawns; k++)
            {
                int type = rng.Next(_data.Units.Length);
                int owner = rng.Next(2);
                bool inFog = rng.Next(3) == 0 && far.Count > 0;
                NVector2 at = inFog ? far[rng.Next(far.Count)] : near[rng.Next(near.Count)];
                sim.Enqueue(Command.SpawnUnit(owner, type, at));
            }
            sim.Tick();
            if (rng.Next(4) == 0) sim.Tick();
            views.Sync(w, 1f);
            steps++;
            for (int i = 0; i < cap; i++)
            {
                if (!u.Alive[i]) continue;
                int type = u.TypeId[i];
                if (lastType[i] >= 0 && lastType[i] != type) typeChanges++;
                lastType[i] = type;
                string? why = SlotProblem(views, w, i, type, rgb);
                if (why != null && errors++ == 0) first = $"step {step} slot {i} ({_data.Units[type].Key}, owner {u.Owner[i]}): {why}";
                if (views.DrawsModel(type)) { modelSeen++; if (!views.IsShown(i)) hiddenModels++; }
                else capsuleSeen++;
            }
            if (step % 25 == 0) await Frames(1);
        }
        GD.Print($"(3) reuse fuzz: {steps} steps, {typeChanges} type changes in a slot, {modelSeen} model looks ({hiddenModels} hidden in fog), {capsuleSeen} capsule looks, {views.ModelNodeCount} model instances made, {errors} problems (first: {first})");
        Check(errors == 0, $"(3) reuse fuzz: {errors} slot problems; first: {first}");
        Check(typeChanges > 50 && hiddenModels > 0 && modelSeen > 0 && capsuleSeen > 0, $"(3) the fuzz reached too little: {typeChanges} type changes, {hiddenModels} hidden models");
        views.QueueFree();
        fog.QueueFree();
        await Frames(2);
    }

    private string? SlotProblem(UnitViews views, World w, int i, int type, uint[] rgb)
    {
        UnitStore u = w.Units;
        MeshInstance3D? view = views.ViewOf(i);
        if (view == null) return "no view node";
        bool shown = views.IsShown(i);
        if (shown != view.Visible) return $"shown {shown} but node visible {view.Visible}";
        Node3D? model = views.ModelOf(i);
        AnimationPlayer? anim = views.AnimatorOf(i);
        int modelChildren = 0;
        foreach (Node c in view.GetChildren()) if (c.Name.ToString().StartsWith("Model", StringComparison.Ordinal) && !c.IsQueuedForDeletion()) modelChildren++;
        if (views.DrawsModel(type))
        {
            if (model == null || anim == null || views.ModelMeshOf(i) == null) return "a model type without its model";
            if (view.Mesh != null) return "a model slot still draws a capsule mesh";
            if (modelChildren != 1 || model.Name != "Model") return $"{modelChildren} model children, the slot's named {model.Name}";
            AnimationLibrary? lib = anim.HasAnimationLibrary("") ? anim.GetAnimationLibrary("") : null;
            if (lib != views.Models!.LibraryOf(type)) return "its player holds another type's library";
            if (anim.Active != shown) return $"player active {anim.Active}, shown {shown}";
            if (shown)
            {
                Material? team = views.ModelMeshOf(i)!.GetSurfaceOverrideMaterial(views.Models.TeamSurface(type));
                if (team != views.Models.TeamMaterial(type, u.Owner[i])) return "team surface not in its owner's colour";
                if (views.ShownClip(i) != (int)UnitViews.ClipFor(w, i)) return $"shows clip {views.ShownClip(i)}, state wants {(int)UnitViews.ClipFor(w, i)}";
            }
        }
        else
        {
            if (model != null || anim != null || modelChildren != 0) return $"a capsule type with a model ({modelChildren} children)";
            if (view.Mesh != views.MeshOf(type)) return "capsule mesh of another type";
            if (shown && view.MaterialOverride != views.PlayerMaterial(u.Owner[i])) return "capsule not in its owner's colour";
        }
        return null;
    }

    // (4) The per-type libraries hold the clip map's clips under the state names.
    private void ClipMapRow()
    {
        var views = new UnitViews();
        AddChild(views);
        views.Bind(_data, 4, new uint[] { 0xC03030, 0x3060D0 });
        AnimationLibrary shared = UnitModels.Library!;
        var rows = new List<string>();
        foreach (int t in ModelTypes(views))
        {
            AnimationLibrary lib = views.Models!.LibraryOf(t)!;
            var names = new List<string>();
            foreach (StringName n in lib.GetAnimationList())
            {
                names.Add(n);
                string? source = null;
                foreach (UnitModels.Clip c in Enum.GetValues<UnitModels.Clip>())
                    if (UnitModels.ClipNames[(int)c] == n) source = views.Models.ClipSource(t, c);
                Check(source != null && lib.GetAnimation(n) == shared.GetAnimation(source), $"(4) {_data.Units[t].Key}: '{n}' is not the shared library's {source}");
            }
            rows.Add($"{_data.Units[t].Key} [{string.Join(' ', names)}]");
            string attack = views.Models.ClipSource(t, UnitModels.Clip.Attack)!;
            string key = _data.Units[t].Key;
            // M6-V21's six keep this row's pins; a later type (M7-V5's Teblor) attacks with what its manifest entry names.
            string expect = key is "malazan_heavy_infantry" or "whirlwind_raider" ? "attack_melee"
                : key is "malazan_laborer" or "malazan_crossbowman" or "whirlwind_camp_follower" or "whirlwind_desert_archer" ? "attack"
                : ManifestAttack(key);
            Check(attack == expect, $"(4) {key} attacks with {attack}, want {expect}");
            Check(lib.HasAnimation("idle") && lib.HasAnimation("walk") && lib.HasAnimation("attack"), $"(4) {_data.Units[t].Key} lacks idle / walk / attack");
        }
        int follower = _data.FindUnit("whirlwind_camp_follower");
        Check(views.Models!.HasClip(follower, UnitModels.Clip.Build) && views.Models.HasClip(follower, UnitModels.Clip.GatherGold) && views.Models.HasClip(follower, UnitModels.Clip.GatherWood),
            "(4) the camp follower lacks a worker clip");
        GD.Print($"(4) libraries: {string.Join("; ", rows)}");
        views.Free();
    }

    // (5) QaV5Test phase 2 on the model's mesh (its own check reads the slot node, which a model never overlays): a Heavy
    // Infantry dies while flashing; its slot is reused at once by a Heavy Infantry (same model) and then by a Raider (another
    // model); the new unit's model mesh carries no flash overlay on its first Sync.
    private void FlashReuseRow()
    {
        var sim = new Simulation(new SimConfig(14, 2, 8, 4096) { Data = _data, Combat = false });
        World w = sim.World;
        UnitStore u = w.Units;
        var views = new UnitViews();
        AddChild(views);
        views.Bind(_data, 8, new uint[] { 0xC03030, 0x3060D0 });
        int heavy = _data.FindUnit("malazan_heavy_infantry"), raider = _data.FindUnit("whirlwind_raider");
        NVector2[] west = StartLayout.Block(w.NavGrid, 4, true, 1f);
        foreach (int next in new[] { heavy, raider })
        {
            for (int i = 0; i < u.Capacity; i++) if (u.Alive[i]) u.Free(new EntityHandle(i, u.Generation[i]));
            sim.Enqueue(Command.SpawnUnit(0, heavy, west[0]));
            sim.Tick();
            sim.Tick();
            views.Sync(w, 1f, 0.05f);
            int slot = Slot(u, 0, heavy);
            if (!Check(slot >= 0, "(5) the heavy infantry did not spawn")) break;
            u.Hp[slot] = Math.Max(1, u.Hp[slot] / 2); // staging: a hit (test write between ticks)
            views.Sync(w, 1f, 0.01f);
            MeshInstance3D? mesh = views.ModelMeshOf(slot);
            bool litBefore = views.ShownLit(slot) && mesh?.MaterialOverlay == views.FlashMaterial;
            u.Free(new EntityHandle(slot, u.Generation[slot]));
            sim.Enqueue(Command.SpawnUnit(0, next, west[1]));
            sim.Tick();
            sim.Tick();
            int again = Slot(u, 0, next);
            if (!Check(again >= 0, $"(5) the {_data.Units[next].Key} did not spawn")) break;
            views.Sync(w, 1f, 0.01f);
            MeshInstance3D? now = views.ModelMeshOf(again);
            GD.Print($"(5) flash then reuse by {_data.Units[next].Key}: lit before death {litBefore}, slot {slot} -> {again}, new mesh overlay {now?.MaterialOverlay?.ResourceName ?? (now?.MaterialOverlay == null ? "none" : "?")}, shown lit {views.ShownLit(again)}");
            Check(litBefore, $"(5) the hit heavy infantry's model mesh did not flash");
            Check(again == slot, $"(5) the respawn took slot {again}, not {slot} (the row proves nothing)");
            Check(now != null && now.MaterialOverlay != views.FlashMaterial && !views.ShownLit(again), $"(5) the new {_data.Units[next].Key} inherited the flash on its model mesh");
        }
        views.Free();
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
