using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
            await ShamanRow();
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

    // (1) The shipped library: the casting Shaman plays attack_cast, then idle once the cast is done.
    private async Task ShamanRow()
    {
        (Match match, Simulation sim, UnitViews views, int slot) = await CastingShaman();
        try
        {
            if (slot < 0) return;
            AnimationPlayer? anim = views.AnimatorOf(slot);
            Animation? library = UnitModels.Library?.HasAnimation(CastClip) == true ? UnitModels.Library.GetAnimation(CastClip) : null;
            string playing = anim?.CurrentAnimation ?? "(no player)";
            Animation? actual = anim != null && anim.HasAnimation(playing) ? anim.GetAnimation(playing) : null;
            GD.Print($"(1) the Shaman casting (state {sim.World.Units.State[slot]}): plays '{playing}' ({actual?.Length:F2} s), shown clip {views.ShownClip(slot)}");
            Check(views.DrawsModel(_shaman) && anim != null, "(1) the Shaman is not drawn as a model");
            Check(playing == "attack_cast" && library != null && actual == library && anim!.IsPlaying() && views.ShownClip(slot) == (int)UnitModels.Clip.Cast,
                $"(1) the casting Shaman plays '{playing}' (the library's attack_cast: {actual == library && library != null}), want attack_cast");
            if (_shots != null && DisplayServer.GetName() != "headless")
            {
                var camera = match.GetNode<RtsCamera>("RtsCamera");
                NVector2 p = sim.World.Units.Position[slot];
                camera.SetZoom(CameraLimits.MinZoom);
                camera.SetFocus(p.X, p.Y + 2f);
                for (int k = 0; k < 12; k++) await Frame(); // into the thrust (0.5 s clip)
                await Shot("shaman-casting-zoom20");
            }
            for (int k = 0; k < 40 && sim.World.Units.State[slot] == UnitState.Casting; k++) sim.Tick();
            views.Sync(sim.World, 1f);
            string after = anim?.CurrentAnimation ?? "";
            GD.Print($"(1) the cast done (state {sim.World.Units.State[slot]}): plays '{after}'");
            Check(sim.World.Units.State[slot] != UnitState.Casting && after == "idle", $"(1) after the cast: state {sim.World.Units.State[slot]}, plays '{after}', want idle");
        }
        finally
        {
            await EndMatch(match);
        }
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
        (Match match, Simulation sim, UnitViews views, int slot) = await CastingShaman();
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
        (Match match, Simulation sim, UnitViews views, int slot) = await CastingShaman();
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

    // A match with no bases, combat or fog; a Shaman of player 0 at the map's middle, given the Frenzy and ticked into its
    // cast; the views synced. Slot -1 (a failure noted) when it never casts.
    private async Task<(Match, Simulation, UnitViews, int)> CastingShaman()
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
        sim.Enqueue(Command.SpawnUnit(0, _shaman, mid));
        sim.Tick();
        sim.Tick();
        int slot = -1;
        for (int i = 0; i < u.Capacity && slot < 0; i++) if (u.Alive[i] && u.Owner[i] == 0 && u.TypeId[i] == _shaman) slot = i;
        if (!Check(slot >= 0, "no Shaman spawned")) return (match, sim, views, -1);
        int index = _data.Units[_shaman].Abilities.IndexOf(_frenzy);
        if (!Check(index >= 0, "the Shaman has no Blood-oil Frenzy")) return (match, sim, views, -1);
        sim.Enqueue(Command.UseAbility(0, new EntityHandle(slot, u.Generation[slot]), index, u.Position[slot]));
        for (int k = 0; k < 6 && u.State[slot] != UnitState.Casting; k++) sim.Tick();
        if (!Check(u.State[slot] == UnitState.Casting, $"the Shaman is not casting ({u.State[slot]})")) return (match, sim, views, -1);
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
