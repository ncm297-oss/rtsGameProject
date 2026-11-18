using System;
using System.Collections.Generic;
using System.IO;
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

/// <summary>
/// M6-V23 (BUG-1441 item 4): a Casting unit plays the cast clip. On the shipped data in a real match: (1) a Teblor Shaman
/// casting Blood-oil Frenzy plays <c>attack_cast</c>, the very <see cref="Animation"/> of the library's clip, and goes back
/// to <c>idle</c> when the cast ends; (2) with a library that lacks <c>attack_cast</c> (the shared library swapped for a
/// copy without it, restored after) the casting Shaman plays <c>idle</c>, with no warning or error from the view;
/// (3) with models off (capsules) the casting Shaman is its capsule with no animation player, as before.
/// M6-V24 (BUG-1530): (1b) on copies whose Frenzy takes 0.5, 0.8 and 1.2 s, the cast clip starts at its first frame when the
/// cast is first seen and plays once across it, frame by frame (never stepping back, ending within one frame of its end);
/// (1c) eight walking Warriors still start their walk at eight different places (the crowd phase).
/// M6-V25: rows (1) and (1b) run for every caster drawn as a model: the Shaman (Blood-oil Frenzy), the Cadre Mage (Telas
/// Fire) and the Priest of the Whirlwind (Sandstorm); each casts with the library's <c>attack_cast</c>, as its clip map names.
/// </summary>
/// <remarks>
/// Headless, in the scene loop: <c>&amp; $env:GODOT --headless --path game res://tests/CastClipTest.tscn</c>; prints
/// "CAST CLIP TEST PASS". Windowed with <c>-- --shots &lt;dir&gt;</c> it saves the Shaman mid-cast at zoom 20.
/// </remarks>
public partial class CastClipTest : Node
{
    private const string Tag = "CAST CLIP TEST";
    private static readonly FieldInfo LibraryField = typeof(UnitModels).GetField("_library", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly FieldInfo LengthField = typeof(UnitModels).GetField("_clipLength", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly StringName CastClip = UnitModels.ClipNames[(int)UnitModels.Clip.Cast];

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private int _shaman, _frenzy;

    // M6-V25: the casters whose cast gesture rows (1) and (1b) check: (unit id, the ability it casts).
    private static readonly (string Unit, string Ability)[] Casters =
    {
        ("teblor_shaman", "blood_oil_frenzy"), ("malazan_cadre_mage", "telas_fire"), ("whirlwind_priest", "sandstorm"),
    };
    private string? _shots;
    private AnimationLibrary? _realLibrary;
    private float[]? _realLengths;

    public override async void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(args, "--shots");
        if (at >= 0 && at + 1 < args.Length) { _shots = args[at + 1]; Directory.CreateDirectory(_shots); }
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            await Frame();
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            _shaman = _data.FindUnit("teblor_shaman");
            _frenzy = _data.FindAbility("blood_oil_frenzy");
            Check(CastClip == "attack_cast", $"the cast slot is named {CastClip}, want attack_cast");
            UnitViews.UseModels = true;
            foreach ((string unit, string ability) in Casters)
            {
                await CasterRow(unit, ability);
                foreach (float seconds in new[] { 0.5f, 0.8f, 1.2f }) await CastOnceRow(unit, ability, seconds);
            }
            await CrowdPhaseRow();
            await NoClipRow();
            await CapsuleRow();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        RestoreStatics();
        UnitViews.UseModels = true;
        foreach (string f in _failures) GD.Print($"{Tag} FAIL: {f}");
        if (_failures.Count == 0) GD.Print($"{Tag} PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // (1) The shipped library: the casting caster plays attack_cast, then idle once the cast is done.
    private async Task CasterRow(string unitId, string abilityId)
    {
        int unit = _data.FindUnit(unitId), ability = _data.FindAbility(abilityId);
        (Match match, Simulation sim, UnitViews views, int slot) = await Casting(unit, ability);
        try
        {
            if (slot < 0) return;
            AnimationPlayer? anim = views.AnimatorOf(slot);
            Animation? library = UnitModels.Library?.HasAnimation(CastClip) == true ? UnitModels.Library.GetAnimation(CastClip) : null;
            string playing = anim?.CurrentAnimation ?? "(no player)";
            Animation? actual = anim != null && anim.HasAnimation(playing) ? anim.GetAnimation(playing) : null;
            GD.Print($"(1) {unitId} casting {abilityId} (state {sim.World.Units.State[slot]}): plays '{playing}' ({actual?.Length:F2} s), shown clip {views.ShownClip(slot)}");
            Check(views.DrawsModel(unit) && anim != null, $"(1) {unitId} is not drawn as a model");
            Check(playing == "attack_cast" && library != null && actual == library && anim!.IsPlaying() && views.ShownClip(slot) == (int)UnitModels.Clip.Cast,
                $"(1) the casting {unitId} plays '{playing}' (the library's attack_cast: {actual == library && library != null}), want attack_cast");
            if (_shots != null && DisplayServer.GetName() != "headless")
            {
                var camera = match.GetNode<RtsCamera>("RtsCamera");
                NVector2 p = sim.World.Units.Position[slot];
                camera.SetZoom(CameraLimits.MinZoom);
                camera.SetFocus(p.X, p.Y + 2f);
                for (int k = 0; k < 12; k++) await Frame(); // into the thrust (0.5 s clip)
                await Shot($"{unitId}-casting-zoom20");
            }
            for (int k = 0; k < 40 && sim.World.Units.State[slot] == UnitState.Casting; k++) sim.Tick();
            views.Sync(sim.World, 1f);
            string after = anim?.CurrentAnimation ?? "";
            GD.Print($"(1) {unitId}: the cast done (state {sim.World.Units.State[slot]}): plays '{after}'");
            Check(sim.World.Units.State[slot] != UnitState.Casting && after == "idle", $"(1) {unitId} after the cast: state {sim.World.Units.State[slot]}, plays '{after}', want idle");
        }
        finally
        {
            await EndMatch(match);
        }
    }

    // (1b) M6-V24, BUG-1530: on a data copy whose ability takes `seconds` (found by id and edited, rule 58), the cast clip
    // starts at its first frame on the frame the caster is first seen casting and spans the cast once: frame by frame at
    // 60 FPS and 20 ticks a second (the view's alpha a third of a tick further each frame) its position only grows, never
    // wraps, ends within one frame of the clip's end on the last frame of the cast, and the figure plays idle after it.
    // M6-V25: for each caster (the ability is edited in whichever faction file holds its id).
    private async Task CastOnceRow(string unitId, string abilityId, float seconds)
    {
        string dataDir = ProjectSettings.GlobalizePath("res://data");
        string copy = Path.Combine(Path.GetTempPath(), $"rts-cast-clip-{OS.GetProcessId()}", "data");
        foreach (string dir in Directory.GetDirectories(dataDir, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(copy, Path.GetRelativePath(dataDir, dir)));
        foreach (string file in Directory.GetFiles(dataDir, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(copy, Path.GetRelativePath(dataDir, file)), true);
        int edited = 0;
        foreach (string path in Directory.GetFiles(Path.Combine(copy, "factions"), "abilities.json", SearchOption.AllDirectories))
        {
            JsonNode abilities = JsonNode.Parse(File.ReadAllText(path))!;
            int here = 0;
            foreach (JsonNode? a in abilities["abilities"]!.AsArray())
                if ((string?)a!["id"] == abilityId) { a["castTime"] = seconds; here++; }
            if (here > 0) File.WriteAllText(path, abilities.ToJsonString());
            edited += here;
        }
        DataLoadResult loaded = DataLoader.LoadAll(copy);
        try { Directory.Delete(Path.GetDirectoryName(copy)!, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        if (!Check(edited == 1 && loaded.Ok, $"(1b) {unitId} {seconds} s: {edited} {abilityId} rows edited, the copy loads {loaded.Ok}")) return;
        GameData data = loaded.Data!;
        int shaman = data.FindUnit(unitId), frenzy = data.FindAbility(abilityId);
        int castTicks = data.Abilities[frenzy].CastTicks;

        var views = new UnitViews();
        AddChild(views);
        views.Bind(data, 8, new uint[] { 0xC03030, 0x3060D0 });
        var sim = new Simulation(new SimConfig(3, 2, 8, 4096) { Data = data, Combat = false });
        UnitStore u = sim.World.Units;
        NVector2[] west = StartLayout.Block(sim.World.NavGrid, 2, true, 1f);
        // A Warrior first, so the caster is in slot 1 (BUG-1530's case: the old rule started its clip at 0.37 s).
        sim.Enqueue(Command.SpawnUnit(0, data.FindUnit("teblor_warrior"), west[1]));
        sim.Enqueue(Command.SpawnUnit(0, shaman, west[0]));
        sim.Tick();
        sim.Tick();
        int slot = -1;
        for (int i = 0; i < u.Capacity && slot < 0; i++) if (u.Alive[i] && u.TypeId[i] == shaman) slot = i;
        Check(slot == 1, $"(1b) {unitId} {seconds} s: the caster is in slot {slot}, want 1");
        views.Sync(sim.World, 1f);
        AnimationPlayer? anim = slot >= 0 ? views.AnimatorOf(slot) : null;
        if (!Check(anim != null, $"(1b) {unitId} {seconds} s: no caster model")) { views.QueueFree(); return; }
        // Idle a while, so the clip that was playing stands somewhere in its loop (the old rule's start phase came from it).
        for (int f = 0; f < 20; f++) { views.Sync(sim.World, 1f, 1f / 60f); views.AdvanceAnimations(1.0 / 60.0, null); }
        sim.Enqueue(Command.UseAbility(0, new EntityHandle(slot, u.Generation[slot]), data.Units[shaman].Abilities.IndexOf(frenzy), u.Position[slot]));
        for (int k = 0; k < 6 && u.State[slot] != UnitState.Casting; k++) sim.Tick();
        if (!Check(u.State[slot] == UnitState.Casting, $"(1b) {unitId} {seconds} s: the caster is not casting ({u.State[slot]})")) { views.QueueFree(); return; }

        float length = views.Models!.ClipLength(shaman, UnitModels.Clip.Cast);
        const int perTick = 3; // 60 FPS at 20 ticks a second
        float frameClip = length / ((castTicks - 1) * perTick); // one frame of clip time at the cast's pace
        var positions = new List<float>();
        int frames = 0;
        string start = "";
        while (u.State[slot] == UnitState.Casting && frames < 400)
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
        GD.Print($"(1b) {unitId} cast {seconds} s ({castTicks} ticks, seen {castTicks - 1}): clip '{start}' {length:F2} s over {positions.Count} frames, first {first:F3} s, last {last:F3} s, {wraps} steps back; then '{after}'");
        Check(start == "attack_cast", $"(1b) {unitId} {seconds} s: plays '{start}' on the cast's first frame, want attack_cast");
        Check(first >= 0f && first <= frameClip + 1e-4f, $"(1b) {unitId} {seconds} s: the clip starts at {first:F3} s, want 0 within one frame ({frameClip:F3} s)");
        Check(wraps == 0, $"(1b) {unitId} {seconds} s: the clip stepped back {wraps} times (looped)");
        Check(last >= length - frameClip - 2e-3f && last < length, $"(1b) {unitId} {seconds} s: the clip ends the cast at {last:F3} s of {length:F3} s, want its end within one frame");
        Check(positions.Count == (castTicks - 1) * perTick, $"(1b) {unitId} {seconds} s: {positions.Count} frames of cast, want {(castTicks - 1) * perTick}");
        Check(after == "idle", $"(1b) {unitId} {seconds} s: after the cast plays '{after}', want idle");
        views.QueueFree();
        await Frame();
    }

    // (1c) The other clips keep the crowd phase (M6-V20): eight walking Warriors start their walk at different places.
    private async Task CrowdPhaseRow()
    {
        int warrior = _data.FindUnit("teblor_warrior");
        var views = new UnitViews();
        AddChild(views);
        views.Bind(_data, 16, new uint[] { 0xC03030, 0x3060D0 });
        var sim = new Simulation(new SimConfig(3, 2, 16, 4096) { Data = _data, Combat = false });
        UnitStore u = sim.World.Units;
        NVector2[] west = StartLayout.Block(sim.World.NavGrid, 8, true, 1f);
        for (int k = 0; k < 8; k++) sim.Enqueue(Command.SpawnUnit(0, warrior, west[k]));
        sim.Tick();
        sim.Tick();
        views.Sync(sim.World, 1f);
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i]) sim.Enqueue(Command.Move(0, new EntityHandle(i, u.Generation[i]), u.Position[i] + new NVector2(20f, 0f)));
        for (int k = 0; k < 4; k++) sim.Tick();
        views.Sync(sim.World, 1f);
        var phases = new SortedSet<int>();
        int walking = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.State[i] != UnitState.Moving || views.AnimatorOf(i)?.CurrentAnimation != "walk") continue;
            walking++;
            phases.Add((int)Math.Round(views.AnimatorOf(i)!.CurrentAnimationPosition * 1000));
        }
        GD.Print($"(1c) {walking} walking Warriors start their walk at {phases.Count} different places (ms: {string.Join(' ', phases)})");
        Check(walking == 8 && phases.Count == walking, $"(1c) {walking} walking, {phases.Count} different start phases, want 8 and 8");
        views.QueueFree();
        await Frame();
    }

    // (2) A library without attack_cast: the casting Shaman plays idle (no warning, no error: the loop reads the log).
    private async Task NoClipRow()
    {
        // A Bind loads the real library first; then a copy without the cast clip goes in until RestoreStatics.
        var probe = new UnitViews();
        AddChild(probe);
        probe.Bind(_data, 4, new uint[] { 0xC03030, 0x3060D0 });
        probe.QueueFree();
        _realLibrary = UnitModels.Library;
        _realLengths = (float[])((float[])LengthField.GetValue(null)!).Clone();
        if (!Check(_realLibrary != null && _realLibrary.HasAnimation(CastClip), "(2) the shipped library has no attack_cast")) return;
        var cut = new AnimationLibrary();
        foreach (StringName name in _realLibrary!.GetAnimationList())
            if (name != CastClip) cut.AddAnimation(name, _realLibrary.GetAnimation(name));
        LibraryField.SetValue(null, cut);
        var lengths = (float[])_realLengths.Clone();
        lengths[(int)UnitModels.Clip.Cast] = 0f;
        LengthField.SetValue(null, lengths);
        (Match match, Simulation sim, UnitViews views, int slot) = await Casting(_shaman, _frenzy);
        try
        {
            if (slot < 0) return;
            AnimationPlayer? anim = views.AnimatorOf(slot);
            string playing = anim?.CurrentAnimation ?? "(no player)";
            GD.Print($"(2) a library without attack_cast, the Shaman casting: holds the clip {views.Models!.HasClip(_shaman, UnitModels.Clip.Cast)}, plays '{playing}'");
            Check(!views.Models!.HasClip(_shaman, UnitModels.Clip.Cast) && playing == "idle" && anim!.IsPlaying(),
                $"(2) without the clip the casting Shaman plays '{playing}', want idle");
        }
        finally
        {
            await EndMatch(match);
            RestoreStatics();
        }
    }

    // (3) Capsules (models off): the casting Shaman is its capsule with no player, as before M6-V23.
    private async Task CapsuleRow()
    {
        UnitViews.UseModels = false;
        (Match match, Simulation sim, UnitViews views, int slot) = await Casting(_shaman, _frenzy);
        try
        {
            if (slot < 0) return;
            bool capsule = !views.DrawsModel(_shaman) && views.AnimatorOf(slot) == null && views.ModelOf(slot) == null
                           && views.ViewOf(slot)?.Mesh == views.MeshOf(_shaman) && views.ViewOf(slot)!.Visible;
            GD.Print($"(3) models off, the Shaman casting: capsule {capsule}, shown clip {views.ShownClip(slot)}");
            Check(capsule, "(3) models off: the casting Shaman is not its plain capsule");
        }
        finally
        {
            await EndMatch(match);
            UnitViews.UseModels = true;
        }
    }

    // A match with no bases, combat or fog; a caster of player 0 at the map's middle, given its ability (aimed at its own
    // feet: in range for a ground-targeted one) and ticked into its cast; the views synced. Slot -1 (a failure noted) when
    // it never casts. M6-V25: any caster (was the Shaman and the Frenzy).
    private async Task<(Match, Simulation, UnitViews, int)> Casting(int caster, int ability)
    {
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        match.Start(_data, LaunchOptions.Parse(new[] { "--seed", "1", "--units", "0", "--no-bases", "--no-combat", "--no-fog", "--mute" }));
        var runner = match.GetNode<SimRunner>("SimRunner");
        runner.ProcessMode = ProcessModeEnum.Disabled; // ticked here
        match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
        Simulation sim = runner.Simulation!;
        World w = sim.World;
        UnitStore u = w.Units;
        var views = match.GetNode<UnitViews>("World3D/UnitViews");
        sim.Tick();
        sim.Tick();
        NVector2 mid = w.NavGrid.CellCenter(w.NavGrid.Width / 2, w.NavGrid.Height / 2);
        sim.Enqueue(Command.SpawnUnit(0, caster, mid));
        sim.Tick();
        sim.Tick();
        int slot = -1;
        for (int i = 0; i < u.Capacity && slot < 0; i++) if (u.Alive[i] && u.Owner[i] == 0 && u.TypeId[i] == caster) slot = i;
        if (!Check(slot >= 0, $"no {_data.Units[caster].Key} spawned")) return (match, sim, views, -1);
        int index = _data.Units[caster].Abilities.IndexOf(ability);
        if (!Check(index >= 0, $"{_data.Units[caster].Key} has no {_data.Abilities[ability].Key}")) return (match, sim, views, -1);
        sim.Enqueue(Command.UseAbility(0, new EntityHandle(slot, u.Generation[slot]), index, u.Position[slot]));
        for (int k = 0; k < 6 && u.State[slot] != UnitState.Casting; k++) sim.Tick();
        if (!Check(u.State[slot] == UnitState.Casting, $"{_data.Units[caster].Key} is not casting ({u.State[slot]})")) return (match, sim, views, -1);
        views.Sync(w, 1f);
        await Frame();
        views.Sync(w, 1f);
        return (match, sim, views, slot);
    }

    private async Task EndMatch(Match match)
    {
        RemoveChild(match);
        match.QueueFree();
        await Frame();
    }

    private void RestoreStatics()
    {
        if (_realLibrary == null) return;
        LibraryField.SetValue(null, _realLibrary);
        LengthField.SetValue(null, _realLengths);
    }

    private async Task Shot(string name)
    {
        for (int i = 0; i < 4; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = $"{_shots}/{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"screenshot {path}");
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
