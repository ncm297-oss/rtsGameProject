using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
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

/// <summary>QA for M6-V20 (unit models): the fallbacks and the view's duties on a model that ModelViewTest does not reach.</summary>
/// <remarks>
/// Headless. Run: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6V20Test.tscn</c>; prints "QA M6-V20 TEST PASS" and
/// exits 0, or prints each failure and exits 1. Rows:
/// (1) a model scene without a <c>team</c> material (seeded into the loader's cache) gives the capsule, no ERROR;
/// (2) a library missing <c>attack</c> / <c>walk</c> plays <c>idle</c> for them; no library at all: the model stands, no ERROR;
/// (3) the faction-colour fallback (no per-player colours) on a model;
/// (4) a slot reused capsule -> model -> capsule, model -> other model, and the same model type respawned for another owner
///     (the team colour follows; the freed models are really freed);
/// (5) the hit flash fading back to the worker's tint on a model's mesh;
/// (6) fog: a walking enemy model hidden and shown mid-animation (paused while hidden, moving again when seen);
/// (7) a four-player Malazan skirmish through the shell: every player's models in that player's colour, all four distinct and
///     equal to the capsule colour of the same player;
/// (8) the shell's match started and quit four times: node, resource and object counts flat after the second;
/// (9) a save loaded and the recording watched with models on: every model slot shown as a model, its clip the one its sim
///     state names.
/// </remarks>
public partial class QaM6V20Test : Node
{
    private const string Tag = "QA M6-V20 TEST";
    private readonly List<string> _failures = new();
    private string _root = "";
    private GameData _data = null!;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _root = $"user://qa-m6-v20-{OS.GetProcessId()}";
        UnitViews.UseModels = true;
        try
        {
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            NoTeamRow();
            await LibraryRows();
            ResetStatics();
            FactionColourRow();
            ReuseRows();
            ReuseFuzz();
            FlashRow();
            await FogRow();
            await FourPlayersAndRestarts();
            await SaveAndReplayRow();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        ResetStatics();
        try
        {
            string full = ProjectSettings.GlobalizePath(_root);
            if (Directory.Exists(full)) Directory.Delete(full, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        foreach (string f in _failures) GD.Print($"{Tag} FAIL: {f}");
        if (_failures.Count == 0) GD.Print($"{Tag} PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- reflection on UnitModels' statics (the loader cache and the library), restored after each row ----

    private static readonly FieldInfo LoadedField = typeof(UnitModels).GetField("Loaded", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly FieldInfo LibraryField = typeof(UnitModels).GetField("_library", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly FieldInfo TriedField = typeof(UnitModels).GetField("_libraryTried", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly FieldInfo LengthField = typeof(UnitModels).GetField("_clipLength", BindingFlags.NonPublic | BindingFlags.Static)!;
    private AnimationLibrary? _realLibrary;
    private float[]? _realLengths;

    private void ResetStatics()
    {
        UnitViews.UseModels = true;
        UnitModels.ModelRoot = "res://assets/models/";
        if (_realLibrary != null)
        {
            LibraryField.SetValue(null, _realLibrary);
            LengthField.SetValue(null, _realLengths);
            TriedField.SetValue(null, true);
        }
    }

    // (1) A model with no 'team' surface: the type keeps its capsule (one warning, never ERROR: the loop checks the log).
    private void NoTeamRow()
    {
        const string stem = "malazan/qa_noteam";
        var root = new Node3D { Name = "Root" };
        var mesh = new MeshInstance3D { Name = "Body", Mesh = new BoxMesh { Material = new StandardMaterial3D { ResourceName = "outfit" } } };
        root.AddChild(mesh);
        mesh.Owner = root;
        var scene = new PackedScene();
        scene.Pack(root);
        root.Free();
        var cache = (Dictionary<string, PackedScene?>)LoadedField.GetValue(null)!;
        cache[UnitModels.PathFor(stem)] = scene;

        GameData forged = Forged(u => { if ((string?)u["id"] == "malazan_laborer") u["model"] = stem; }, "noteam");
        var views = new UnitViews();
        AddChild(views);
        views.Bind(forged, 8, new uint[] { 0xC03030, 0x3060D0 });
        int laborer = forged.FindUnit("malazan_laborer"), xbow = forged.FindUnit("malazan_crossbowman");
        Check(!views.DrawsModel(laborer), "(1) a model without a 'team' material is drawn as a model");
        Check(views.DrawsModel(xbow), "(1) the crossbowman lost its model beside a team-less laborer");
        var sim = new Simulation(new SimConfig(3, 2, 8, 4096) { Data = forged });
        NVector2[] west = StartLayout.Block(sim.World.NavGrid, 2, true, 1f);
        sim.Enqueue(Command.SpawnUnit(0, laborer, west[0]));
        sim.Tick();
        sim.Tick();
        views.Sync(sim.World, 1f);
        int slot = Slot(sim.World.Units, 0, laborer);
        Check(slot >= 0 && views.ViewOf(slot)?.Mesh == views.MeshOf(laborer) && views.ModelOf(slot) == null && views.ViewOf(slot)!.Visible,
            "(1) the team-less laborer is not a visible capsule");
        views.QueueFree();
        cache.Remove(UnitModels.PathFor(stem));
    }

    // (2) A library lacking clips: the missing ones play idle; no library: the model stands in its pose, nothing fails.
    private async Task LibraryRows()
    {
        // Load the real library first (a Bind does it), then swap in a cut copy.
        var probe = new UnitViews();
        AddChild(probe);
        probe.Bind(_data, 4, new uint[] { 0xC03030, 0x3060D0 });
        probe.QueueFree();
        _realLibrary = UnitModels.Library;
        _realLengths = (float[])((float[])LengthField.GetValue(null)!).Clone();
        if (!Check(_realLibrary != null && _realLibrary.HasAnimation("idle"), "(2) the shipped library has no idle")) return;

        var cut = new AnimationLibrary();
        cut.AddAnimation("idle", _realLibrary!.GetAnimation("idle"));
        LibraryField.SetValue(null, cut);
        var lengths = new float[UnitModels.ClipNames.Length];
        lengths[(int)UnitModels.Clip.Idle] = _realLengths[(int)UnitModels.Clip.Idle];
        LengthField.SetValue(null, lengths);
        await LibraryCase("idle only", expectPlaying: "idle");

        LibraryField.SetValue(null, null);
        LengthField.SetValue(null, Array.Empty<float>());
        await LibraryCase("no library", expectPlaying: "");
        ResetStatics();
    }

    private async Task LibraryCase(string label, string expectPlaying)
    {
        var views = new UnitViews();
        AddChild(views);
        views.Bind(_data, 8, new uint[] { 0xC03030, 0x3060D0 });
        var sim = new Simulation(new SimConfig(3, 2, 8, 4096) { Data = _data, Combat = false });
        UnitStore u = sim.World.Units;
        int xbow = _data.FindUnit("malazan_crossbowman");
        NVector2[] west = StartLayout.Block(sim.World.NavGrid, 2, true, 1f);
        sim.Enqueue(Command.SpawnUnit(0, xbow, west[0]));
        sim.Tick();
        sim.Tick();
        int s = Slot(u, 0, xbow);
        foreach (UnitState state in new[] { UnitState.Idle, UnitState.Moving, UnitState.Attacking, UnitState.Idle })
        {
            u.State[s] = state;
            views.Sync(sim.World, 1f);
            await Frames(2);
            string playing = views.AnimatorOf(s)?.CurrentAnimation ?? "(no player)";
            Check(views.ModelOf(s) != null && views.ViewOf(s)!.Visible, $"(2) {label}: the model is not drawn in state {state}");
            Check(playing == expectPlaying, $"(2) {label}: state {state} plays '{playing}', want '{expectPlaying}'");
        }
        views.QueueFree();
    }

    // (3) Without per-player colours a model's team surface takes its faction's colour, as the capsule does.
    private void FactionColourRow()
    {
        var views = new UnitViews();
        AddChild(views);
        views.Bind(_data, 8);
        var sim = new Simulation(new SimConfig(3, 2, 8, 4096) { Data = _data, Combat = false });
        int laborer = _data.FindUnit("malazan_laborer");
        NVector2[] west = StartLayout.Block(sim.World.NavGrid, 2, true, 1f);
        sim.Enqueue(Command.SpawnUnit(1, laborer, west[0]));
        sim.Tick();
        sim.Tick();
        views.Sync(sim.World, 1f);
        int s = Slot(sim.World.Units, 1, laborer);
        Color want = UnitViews.ColorFromRgb(_data.Factions[_data.Units[laborer].Faction].PrimaryColor);
        Color got = TeamColour(views, s, laborer);
        Check(got == want, $"(3) faction-colour fallback: team colour {got}, want the faction's {want}");
        views.QueueFree();
    }

    // (4) Slot reuse across kinds and owners.
    private void ReuseRows()
    {
        uint[] rgb = { 0xC03030, 0x3060D0 };
        var views = new UnitViews();
        AddChild(views);
        views.Bind(_data, 8, rgb);
        var sim = new Simulation(new SimConfig(3, 2, 8, 4096) { Data = _data, Combat = false });
        World w = sim.World;
        UnitStore u = w.Units;
        int laborer = _data.FindUnit("malazan_laborer"), xbow = _data.FindUnit("malazan_crossbowman"), heavy = _data.FindUnit("malazan_heavy_infantry");
        NVector2[] west = StartLayout.Block(w.NavGrid, 2, true, 1f);
        // owner, type: capsule -> model -> capsule -> model -> other model -> same model, other owner (dead in between)
        (int Owner, int Type)[] steps = { (0, heavy), (0, laborer), (1, heavy), (1, xbow), (0, laborer), (1, laborer) };
        int slot = -1;
        var freed = new List<Node3D>();
        foreach ((int owner, int type) in steps)
        {
            sim.Enqueue(Command.SpawnUnit(owner, type, west[0]));
            sim.Tick();
            sim.Tick();
            int s = Slot(u, owner, type);
            if (slot < 0) slot = s;
            string what = $"(4) slot {slot} as player {owner}'s {_data.Units[type].Key}";
            if (!Check(s == slot, $"{what}: spawned in slot {s}, not the reused slot")) return;
            views.Sync(w, 1f);
            MeshInstance3D view = views.ViewOf(s)!;
            Check(view.Visible, $"{what}: hidden");
            if (views.DrawsModel(type))
            {
                Node3D? model = views.ModelOf(s);
                Check(view.Mesh == null && model != null && model.GetParent() == view, $"{what}: not a model (mesh {view.Mesh}, model {model})");
                Check(view.MaterialOverride == null, $"{what}: the capsule's material override left on the slot node");
                Check(TeamColour(views, s, type) == UnitViews.ColorFromRgb(rgb[owner]), $"{what}: team colour {TeamColour(views, s, type)}, want {UnitViews.ColorFromRgb(rgb[owner])}");
                Check(views.AnimatorOf(s)?.Active == true && views.AnimatorOf(s)?.CurrentAnimation == "idle", $"{what}: player not active on idle ({views.AnimatorOf(s)?.CurrentAnimation})");
                // A model child is a Node3D holding an AnimationPlayer (a reused slot's new one may not be named "Model": the
                // freed one still holds the name until the end of the frame).
                int children = 0;
                foreach (Node c in view.GetChildren())
                    if (c is Node3D && c.GetNodeOrNull<AnimationPlayer>("AnimationPlayer") != null && !c.IsQueuedForDeletion()) children++;
                Check(children == 1, $"{what}: {children} live model children under the slot node");
                if (model != null && !freed.Contains(model)) freed.Add(model);
            }
            else
            {
                Check(view.Mesh == views.MeshOf(type) && views.ModelOf(s) == null, $"{what}: not its capsule (mesh {view.Mesh}, model {views.ModelOf(s)})");
                Check(view.MaterialOverride == views.PlayerMaterial(owner), $"{what}: capsule material is not player {owner}'s");
                Check(view.MaterialOverlay == null, $"{what}: an overlay left on the capsule");
            }
            u.Free(new EntityHandle(s, u.Generation[s]));
            views.Sync(w, 1f);
            Check(!view.Visible, $"{what}: still visible once dead");
        }
        GD.Print($"reuse: {views.ModelNodeCount} model instances made over {steps.Length} steps, {freed.Count} distinct");
        // laborer, xbow, laborer (new: the xbow freed it), then kept for the same model with another owner.
        Check(views.ModelNodeCount == 3, $"(4) {views.ModelNodeCount} model instances, want 3 (laborer, crossbowman, laborer; the last respawn keeps it)");
        views.QueueFree();
    }

    // (4b) Seeded fuzz: 8 seeds x 400 steps of spawn / kill / state writes over capsule and model types and three owners in a
    // 6-slot store; after every Sync each live slot shows exactly its type's kind (one live model child or its capsule), the
    // owner's colour, the clip ClipFor names, and dead slots are hidden with their player stopped.
    private void ReuseFuzz()
    {
        uint[] rgb = { 0xC03030, 0x3060D0, 0x30A040 };
        int[] types = { _data.FindUnit("malazan_laborer"), _data.FindUnit("malazan_crossbowman"), _data.FindUnit("malazan_heavy_infantry"), _data.FindUnit("whirlwind_raider") };
        UnitState[] states = { UnitState.Idle, UnitState.Moving, UnitState.Attacking, UnitState.Building, UnitState.Gathering, UnitState.Returning };
        int bad = 0, steps = 0, modelsSeen = 0;
        for (int seed = 1; seed <= 8 && bad < 5; seed++)
        {
            var rng = new Random(seed); // test-side randomness only (the sim is untouched by it)
            var views = new UnitViews();
            AddChild(views);
            views.Bind(_data, 6, rgb);
            var sim = new Simulation(new SimConfig((ulong)seed, 3, 6, 4096) { Data = _data, Combat = false });
            World w = sim.World;
            UnitStore u = w.Units;
            NVector2[] spots = StartLayout.Block(w.NavGrid, 6, true, 1f);
            for (int step = 0; step < 400 && bad < 5; step++, steps++)
            {
                int op = rng.Next(4);
                // A state written for the view is put back after the checks: the sim must never tick on it (Moving without a goal).
                int written = -1;
                UnitState was = UnitState.Idle;
                if (op == 0 && u.Count < 6)
                {
                    sim.Enqueue(Command.SpawnUnit(rng.Next(3), types[rng.Next(types.Length)], spots[rng.Next(spots.Length)]));
                    sim.Tick();
                }
                else if (op == 1 && u.Count > 0)
                {
                    int k = rng.Next(u.Capacity);
                    if (u.Alive[k]) u.Free(new EntityHandle(k, u.Generation[k]));
                }
                else if (op == 2)
                {
                    int k = rng.Next(u.Capacity);
                    if (u.Alive[k])
                    {
                        written = k;
                        was = u.State[k];
                        u.State[k] = states[rng.Next(states.Length)];
                        u.GatherNode[k] = default;
                        u.CargoKind[k] = rng.Next(2) == 0 ? ResourceKind.Gold : ResourceKind.Wood;
                    }
                }
                else sim.Tick();
                views.Sync(w, 1f, 0.02f);
                for (int i = 0; i < u.Capacity && bad < 5; i++)
                {
                    MeshInstance3D? view = views.ViewOf(i);
                    if (view == null) continue;
                    int live = 0;
                    foreach (Node c in view.GetChildren())
                        if (c is Node3D && c.GetNodeOrNull<AnimationPlayer>("AnimationPlayer") != null && !c.IsQueuedForDeletion()) live++;
                    string where = $"(4b) seed {seed} step {step} slot {i}";
                    if (live > 1) { bad++; _failures.Add($"{where}: {live} live model children"); }
                    if (!u.Alive[i])
                    {
                        if (view.Visible || views.AnimatorOf(i)?.Active == true) { bad++; _failures.Add($"{where}: dead but visible {view.Visible} / animating {views.AnimatorOf(i)?.Active}"); }
                        continue;
                    }
                    int type = u.TypeId[i], owner = u.Owner[i];
                    if (!view.Visible) { bad++; _failures.Add($"{where}: live but hidden"); continue; }
                    if (views.DrawsModel(type))
                    {
                        modelsSeen++;
                        string want = UnitModels.ClipNames[(int)UnitViews.ClipFor(w, i)];
                        if (view.Mesh != null || views.ModelOf(i) == null || live != 1 || TeamColour(views, i, type) != UnitViews.ColorFromRgb(rgb[owner])
                            || views.AnimatorOf(i)!.CurrentAnimation != want || !views.AnimatorOf(i)!.Active)
                        {
                            bad++;
                            _failures.Add($"{where}: model {_data.Units[type].Key} p{owner}: mesh {view.Mesh}, model {views.ModelOf(i)}, live {live}, colour {TeamColour(views, i, type)}, clip '{views.AnimatorOf(i)?.CurrentAnimation}' want '{want}', active {views.AnimatorOf(i)?.Active}");
                        }
                    }
                    else if (view.Mesh != views.MeshOf(type) || views.ModelOf(i) != null || live != 0 || view.MaterialOverride != views.PlayerMaterial(owner))
                    {
                        bad++;
                        _failures.Add($"{where}: capsule {_data.Units[type].Key} p{owner}: mesh {view.Mesh}, model {views.ModelOf(i)}, live {live}, override {view.MaterialOverride}");
                    }
                }
                if (written >= 0) u.State[written] = was;
            }
            views.Free(); // freed now (not queued) so the next seed's live-child count starts clean
        }
        GD.Print($"reuse fuzz: {steps} steps, {modelsSeen} model slot checks, {bad} bad");
    }

    // (5) The hit flash on a model fades back to the worker's state tint, on the model's mesh.
    private void FlashRow()
    {
        var views = new UnitViews();
        AddChild(views);
        views.Bind(_data, 8, new uint[] { 0xC03030, 0x3060D0 });
        var sim = new Simulation(new SimConfig(3, 2, 8, 4096) { Data = _data, Combat = false });
        UnitStore u = sim.World.Units;
        int laborer = _data.FindUnit("malazan_laborer");
        NVector2[] west = StartLayout.Block(sim.World.NavGrid, 2, true, 1f);
        sim.Enqueue(Command.SpawnUnit(0, laborer, west[0]));
        sim.Tick();
        sim.Tick();
        int s = Slot(u, 0, laborer);
        u.State[s] = UnitState.Building;
        views.Sync(sim.World, 1f, 0.01f);
        MeshInstance3D? mesh = views.ModelMeshOf(s);
        Check(mesh?.MaterialOverlay == views.TintOf(UnitState.Building), "(5) no build tint on the model");
        u.Hp[s] -= 1;
        views.Sync(sim.World, 1f, 0.01f);
        Check(mesh?.MaterialOverlay == views.FlashMaterial, "(5) no flash on the model");
        Check(views.ViewOf(s)!.MaterialOverlay == null, "(5) an overlay on the model slot's own (meshless) node");
        for (int i = 0; i < 60; i++) views.Sync(sim.World, 1f, 0.05f);
        Check(mesh?.MaterialOverlay == views.TintOf(UnitState.Building), $"(5) after the flash: overlay {mesh?.MaterialOverlay}, want the build tint");
        u.State[s] = UnitState.Idle;
        views.Sync(sim.World, 1f, 0.05f);
        Check(mesh?.MaterialOverlay == null, "(5) idle: an overlay left on the model");
        views.QueueFree();
    }

    // (6) Fog: an enemy model walking out of sight is hidden and its player paused; walking back in it shows and moves again.
    private async Task FogRow()
    {
        var sim = new Simulation(new SimConfig(7, 2, 16, 4096) { Data = _data, Combat = false });
        World w = sim.World;
        UnitStore u = w.Units;
        int laborer = _data.FindUnit("malazan_laborer"), xbow = _data.FindUnit("malazan_crossbowman");
        NVector2[] west = StartLayout.Block(w.NavGrid, 2, true, 1f);
        sim.Enqueue(Command.SpawnUnit(0, laborer, west[0]));
        sim.Enqueue(Command.SpawnUnit(1, xbow, west[0] + new NVector2(4f, 0f)));
        sim.Tick();
        sim.Tick();
        var fog = new FogOfWar();
        AddChild(fog);
        fog.Bind(w, 0, true);
        var views = new UnitViews { Fog = fog };
        AddChild(views);
        views.Bind(_data, 16, new uint[] { 0xC03030, 0x3060D0 });
        int enemy = Slot(u, 1, xbow);
        views.Sync(w, 1f);
        if (!Check(views.IsShown(enemy) && views.ModelOf(enemy) != null, "(6) the enemy crossbowman beside the laborer is not shown as a model")) { views.QueueFree(); fog.QueueFree(); return; }
        NVector2 near = u.Position[enemy], far = near + new NVector2(60f, 0f);

        async Task<bool> WalkUntil(NVector2 to, bool shown)
        {
            sim.Enqueue(Command.Move(1, new EntityHandle(enemy, u.Generation[enemy]), to));
            for (int i = 0; i < 1200; i++)
            {
                sim.Tick();
                views.Sync(w, 1f, 0.05f);
                if (views.IsShown(enemy) == shown && u.State[enemy] == UnitState.Moving) return true;
                if (i % 20 == 0) await Frames(1);
            }
            return false;
        }

        if (!Check(await WalkUntil(far, false), "(6) the enemy never left sight while walking")) { views.QueueFree(); fog.QueueFree(); return; }
        AnimationPlayer anim = views.AnimatorOf(enemy)!;
        Check(!views.ViewOf(enemy)!.Visible && !anim.Active, $"(6) hidden mid-walk: visible {views.ViewOf(enemy)!.Visible}, player active {anim.Active}");
        double pos = anim.CurrentAnimationPosition;
        await Frames(5);
        Check(anim.CurrentAnimationPosition == pos, "(6) a hidden model's walk kept advancing");
        if (!Check(await WalkUntil(near, true), "(6) the enemy never came back into sight")) { views.QueueFree(); fog.QueueFree(); return; }
        Check(views.ViewOf(enemy)!.Visible && anim.Active && views.AnimatorOf(enemy) == anim, "(6) shown again: not visible / not active / a different player");
        Check(anim.CurrentAnimation == "walk", $"(6) shown again walking: plays '{anim.CurrentAnimation}'");
        pos = anim.CurrentAnimationPosition;
        await Frames(5);
        Check(anim.CurrentAnimationPosition != pos, "(6) shown again: the walk does not advance");
        views.QueueFree();
        fog.QueueFree();
    }

    // (7) + (8) Four Malazan players through the shell: colours; then four start / quit cycles with the counts flat.
    private async Task FourPlayersAndRestarts()
    {
        string[] args = { "--skirmish", "--players", "4", "--faction", "1", "malazan", "--faction", "2", "malazan", "--faction", "3", "malazan",
            "--faction", "4", "malazan", "--seed", "4", "--mute", "--no-fog", Main.NoConfineFlag };
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.UserArgs = Array.Empty<string>();
        main.SettingsPath = $"{_root}/four/settings.cfg";
        AddChild(main);
        await Frames(2);
        var counts = new List<(double Nodes, double Resources, double Objects, double Orphans)>();
        try
        {
            for (int cycle = 0; cycle < 4; cycle++)
            {
                main.StartSkirmish(args);
                Match? match = main.CurrentMatch;
                if (!Check(match != null, $"(8) cycle {cycle}: no match")) return;
                Simulation sim = match!.Simulation!;
                for (int i = 0; i < 600 && sim.World.Units.Count == 0; i++) await Frames(1);
                await Frames(10);
                if (cycle == 0) FourColours(match, sim);
                main.QuitToMenu();
                await Frames(6);
                counts.Add((Performance.GetMonitor(Performance.Monitor.ObjectNodeCount), Performance.GetMonitor(Performance.Monitor.ObjectResourceCount),
                    Performance.GetMonitor(Performance.Monitor.ObjectCount), Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount)));
                GD.Print($"restart {cycle}: nodes {counts[^1].Nodes}, resources {counts[^1].Resources}, objects {counts[^1].Objects}, orphans {counts[^1].Orphans}");
            }
        }
        finally
        {
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
        // Nodes, resources and orphans exactly flat; the object count (GC-collected wrappers) wanders by a few dozen, so it
        // must not climb on every restart and stays within 200 of the first.
        bool climbing = counts.Count > 2;
        for (int c = 1; c < counts.Count; c++)
        {
            Check(counts[c].Nodes == counts[0].Nodes && counts[c].Resources == counts[0].Resources && counts[c].Orphans == 0,
                $"(8) after restart {c}: nodes {counts[c].Nodes} / resources {counts[c].Resources} / orphans {counts[c].Orphans}, after restart 0: {counts[0]}");
            Check(Math.Abs(counts[c].Objects - counts[0].Objects) < 200, $"(8) after restart {c}: {counts[c].Objects} objects, {counts[0].Objects} after the first");
            if (counts[c].Objects <= counts[c - 1].Objects) climbing = false;
        }
        Check(!climbing, $"(8) the object count climbs on every restart: {string.Join(", ", counts.Select(x => x.Objects))}");
    }

    private void FourColours(Match match, Simulation sim)
    {
        UnitViews views = match.GetNode<UnitViews>("World3D/UnitViews");
        UnitStore u = sim.World.Units;
        var seen = new Color?[4];
        int models = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || !views.DrawsModel(u.TypeId[i])) continue;
            int owner = u.Owner[i];
            if (!Check(views.ModelOf(i) != null, $"(7) player {owner}'s {_data.Units[u.TypeId[i]].Key} in slot {i} is not drawn as a model")) continue;
            models++;
            Color c = TeamColour(views, i, u.TypeId[i]);
            Color want = UnitViews.ColorFromRgb(match.PlayerRgb[owner]);
            Check(c == want, $"(7) player {owner}'s model in slot {i}: team colour {c}, want the match colour {want}");
            Check(views.PlayerMaterial(owner)?.AlbedoColor == want, $"(7) player {owner}: the capsule colour {views.PlayerMaterial(owner)?.AlbedoColor} differs from the model's {want}");
            if (owner < 4) seen[owner] ??= c;
        }
        GD.Print($"four players: {models} models; colours {string.Join(", ", seen.Select(c => c?.ToHtml(false) ?? "none"))}");
        Check(seen.All(c => c != null), "(7) a player with no model unit");
        Check(seen.Where(c => c != null).Distinct().Count() == 4, "(7) the four players' team colours are not all distinct");
    }

    // (9) A save loaded and its recording watched: models drawn and their clips following the sim state.
    private async Task SaveAndReplayRow()
    {
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.UserArgs = new[] { "--skirmish", "--seed", "5", "--faction", "2", "malazan", "--mute", "--no-fog", "--speed", "4", Main.NoConfineFlag };
        main.SettingsPath = $"{_root}/save/settings.cfg";
        main.SaveDir = $"{_root}/save/saves";
        main.ReplayDir = $"{_root}/save/replays";
        AddChild(main);
        await Frames(2);
        try
        {
            Match? match = main.CurrentMatch;
            if (!Check(match != null, "(9) no match")) return;
            Simulation sim = match!.Simulation!;
            var clock = Stopwatch.StartNew();
            while (sim.World.Units.Count == 0 && clock.Elapsed.TotalSeconds < 30) await Frames(1);
            SendToWork(sim);
            while (sim.TickNumber < 300 && clock.Elapsed.TotalSeconds < 120) await Frames(1);
            ClipsFollowState(match, "live");
            main.PauseMatch();
            if (!Check(main.SaveGame() && main.LastSavePath != null, "(9) the save wrote nothing")) return;
            string save = main.LastSavePath!;
            string? replay = main.ReplayPath;
            main.QuitToMenu();
            await Frames(2);

            Check(main.LoadSavedGame(save), "(9) the save did not load");
            if (main.CurrentMatch is Match loaded)
            {
                await Frames(10);
                ClipsFollowState(loaded, "loaded save");
                main.QuitToMenu();
                await Frames(2);
            }
            if (!Check(replay != null && File.Exists(ProjectSettings.GlobalizePath(replay)), $"(9) no recording at {replay}")) return;
            Check(main.WatchReplay(replay!), "(9) the recording would not play");
            if (main.CurrentMatch is Match watched)
            {
                Simulation ws = watched.Simulation!;
                clock.Restart();
                while (ws.TickNumber < 200 && clock.Elapsed.TotalSeconds < 120) await Frames(1);
                ClipsFollowState(watched, "replay");
                main.QuitToMenu();
                await Frames(2);
            }
        }
        finally
        {
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
    }

    private void ClipsFollowState(Match match, string label)
    {
        UnitViews views = match.GetNode<UnitViews>("World3D/UnitViews");
        World w = match.Simulation!.World;
        views.Sync(w, 1f);
        UnitStore u = w.Units;
        int models = 0, hidden = 0;
        var clips = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || !views.DrawsModel(u.TypeId[i])) continue;
            // A loaded save or a replay may run with the fog on: a unit the player doesn't see is hidden, not drawn.
            if (!views.IsShown(i))
            {
                hidden++;
                Check(views.ViewOf(i)?.Visible != true, $"(9) {label}: slot {i} not shown but visible");
                continue;
            }
            if (!Check(views.ModelOf(i) != null && views.ViewOf(i)!.Visible, $"(9) {label}: slot {i} ({_data.Units[u.TypeId[i]].Key}) not a shown model")) continue;
            models++;
            string want = UnitModels.ClipNames[(int)UnitViews.ClipFor(w, i)];
            string got = views.AnimatorOf(i)!.CurrentAnimation;
            clips[got] = clips.GetValueOrDefault(got) + 1;
            Check(got == want, $"(9) {label}: slot {i} in state {u.State[i]} plays '{got}', want '{want}'");
        }
        GD.Print($"{label}: tick {match.Simulation!.TickNumber}, {models} models shown, {hidden} hidden, clips {string.Join(" ", clips.Select(kv => $"{kv.Key}x{kv.Value}"))}");
        Check(models > 0, $"(9) {label}: no model drawn");
    }

    // The local player's workers to the nearest gold (first half) and wood (the rest), as a player would order them.
    private void SendToWork(Simulation sim)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        ResourceStore r = w.Resources;
        int player = SelectionController.LocalPlayer, n = 0, total = 0;
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.Owner[i] == player) total++;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.Owner[i] != player) continue;
            ResourceKind want = n++ < (total + 1) / 2 ? ResourceKind.Gold : ResourceKind.Wood;
            NVector2 best = default;
            float bestD = float.MaxValue;
            for (int k = 0; k < r.Capacity; k++)
            {
                if (!r.Alive[k] || _data.Resources[r.TypeId[k]].Resource != want) continue;
                var at = new NVector2(r.Cell[k] % w.Heightmap.Width + 0.5f, r.Cell[k] / w.Heightmap.Width + 0.5f) * Rts.Sim.Map.MapConstants.CellSize;
                float d = NVector2.DistanceSquared(at, u.Position[i]);
                if (d < bestD) (bestD, best) = (d, at);
            }
            if (bestD < float.MaxValue) sim.Enqueue(Command.Gather(player, new EntityHandle(i, u.Generation[i]), best));
        }
    }

    // ---- helpers ----

    private GameData Forged(Action<JsonNode> edit, string label)
    {
        string src = ProjectSettings.GlobalizePath("res://data");
        string copy = Path.Combine(ProjectSettings.GlobalizePath(_root), label, "data");
        foreach (string dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(copy, Path.GetRelativePath(src, dir)));
        Directory.CreateDirectory(copy);
        foreach (string file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(copy, Path.GetRelativePath(src, file)), true);
        string path = Path.Combine(copy, "factions", "malazan", "units.json");
        JsonNode units = JsonNode.Parse(File.ReadAllText(path))!;
        foreach (JsonNode? unit in units["units"]!.AsArray()) edit(unit!);
        File.WriteAllText(path, units.ToJsonString());
        DataLoadResult loaded = DataLoader.LoadAll(copy);
        if (!loaded.Ok) throw new InvalidOperationException($"forged data ({label}) failed to load");
        return loaded.Data!;
    }

    private static Color TeamColour(UnitViews views, int slot, int type) =>
        (views.ModelMeshOf(slot)?.GetSurfaceOverrideMaterial(views.Models!.TeamSurface(type)) as ShaderMaterial)?.GetShaderParameter("team_colour").AsColor() ?? new Color(-1, -1, -1);

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
