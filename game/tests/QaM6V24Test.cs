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

/// <summary>
/// QA M6-V24 (BUG-1530): the Shaman's cast gesture in a real match, driven by the match's own <see cref="SimRunner"/>
/// (fixed frame delta 1/60 s, so the run is the same however loaded the machine is) instead of hand-called Sync.
/// </summary>
/// <remarks>
/// Headless, in the scene loop: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6V24Test.tscn</c>; prints
/// "QA M6V24 TEST PASS". Rows: (A) game speed 0.5, 1 and 2 on the shipped Frenzy and on a 1.2 s copy: the cast clip starts
/// within one frame of 0, never steps back, ends within one frame of its end, then idle; (B) paused (the tree) mid-cast for
/// 30 frames: the clip doesn't move; unpaused it carries on from there and still ends at its end; (C) a Stop mid-cast:
/// the figure goes to idle and its idle clip runs on (no longer held by the cast); (D) the caster's slot freed mid-cast (as CombatSystem frees a unit it kills): its
/// figure hides with no error, and a new Shaman spawned into the freed slot plays idle and its clip runs (not held);
/// (E) a recast after a Stop: the clip starts again from its first frame.
/// </remarks>
public partial class QaM6V24Test : Node
{
    private const string Tag = "QA M6V24 TEST";
    private const double FrameDelta = 1.0 / 60.0;
    private readonly List<string> _failures = new();

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // the pause row pauses the tree under it
        try
        {
            UnitViews.UseModels = true;
            GameData shipped = Load(null);
            GameData longCast = Load(2.0f);
            GameData mid = Load(1.2f);
            foreach (double speed in new[] { 0.5, 1.0, 2.0 })
            {
                await SpeedRow(shipped, speed, "shipped");
                await SpeedRow(mid, speed, "1.2 s");
            }
            await PauseRow(longCast);
            await StopRow(longCast);
            await DeathRow(longCast);
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        GetTree().Paused = false;
        foreach (string f in _failures) GD.Print($"{Tag} FAIL: {f}");
        if (_failures.Count == 0) GD.Print($"{Tag} PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // The shipped data, or a copy whose Blood-oil Frenzy takes `castTime` seconds (found by id and edited, rule 58).
    private static GameData Load(float? castTime)
    {
        string dataDir = ProjectSettings.GlobalizePath("res://data");
        if (castTime == null) return DataLoader.LoadAll(dataDir).Data!;
        string copy = Path.Combine(Path.GetTempPath(), $"qa-m6v24-{OS.GetProcessId()}-{castTime:0.0}", "data");
        foreach (string dir in Directory.GetDirectories(dataDir, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(copy, Path.GetRelativePath(dataDir, dir)));
        foreach (string file in Directory.GetFiles(dataDir, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(copy, Path.GetRelativePath(dataDir, file)), true);
        string path = Path.Combine(copy, "factions", "teblor", "abilities.json");
        JsonNode abilities = JsonNode.Parse(File.ReadAllText(path))!;
        foreach (JsonNode? a in abilities["abilities"]!.AsArray())
            if ((string?)a!["id"] == "blood_oil_frenzy") a["castTime"] = castTime.Value;
        File.WriteAllText(path, abilities.ToJsonString());
        DataLoadResult loaded = DataLoader.LoadAll(copy);
        try { Directory.Delete(Path.GetDirectoryName(copy)!, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        if (!loaded.Ok) throw new InvalidOperationException($"the {castTime} s copy does not load");
        return loaded.Data!;
    }

    private sealed class Stage
    {
        public Match Match = null!;
        public SimRunner Runner = null!;
        public Simulation Sim = null!;
        public UnitViews Views = null!;
        public GameData Data = null!;
        public int Shaman, Frenzy, Index, Slot = -1;
        public UnitStore U => Sim.World.Units;
        public AnimationPlayer Anim => Views.AnimatorOf(Slot)!;
        public float Length => Views.Models!.ClipLength(Shaman, UnitModels.Clip.Cast);
        public int CastTicks => Data.Abilities[Frenzy].CastTicks;
    }

    // A match ticked by its own runner at a fixed frame delta; a Shaman (and a Warrior first, so the Shaman is in slot 1).
    private async Task<Stage> Begin(GameData data, double speed, bool combat)
    {
        var s = new Stage { Data = data, Shaman = data.FindUnit("teblor_shaman"), Frenzy = data.FindAbility("blood_oil_frenzy") };
        s.Match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(s.Match);
        s.Match.ProcessMode = ProcessModeEnum.Pausable; // this node runs while the tree is paused; the match must not
        var flags = new List<string> { "--seed", "1", "--units", "0", "--no-bases", "--no-fog", "--mute" };
        if (!combat) flags.Add("--no-combat");
        s.Match.Start(data, LaunchOptions.Parse(flags.ToArray()));
        s.Runner = s.Match.GetNode<SimRunner>("SimRunner");
        s.Runner.FixedFrameDelta = FrameDelta;
        s.Runner.GameSpeed = speed;
        s.Match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
        s.Sim = s.Runner.Simulation!;
        s.Views = s.Match.GetNode<UnitViews>("World3D/UnitViews");
        World w = s.Sim.World;
        NVector2 mid = w.NavGrid.CellCenter(w.NavGrid.Width / 2, w.NavGrid.Height / 2);
        s.Sim.Enqueue(Command.SpawnUnit(0, data.FindUnit("teblor_warrior"), mid + new NVector2(-4f, 0f)));
        s.Sim.Enqueue(Command.SpawnUnit(0, s.Shaman, mid));
        for (int k = 0; k < 40; k++) await Frame();
        for (int i = 0; i < s.U.Capacity && s.Slot < 0; i++) if (s.U.Alive[i] && s.U.TypeId[i] == s.Shaman) s.Slot = i;
        s.Index = data.Units[s.Shaman].Abilities.IndexOf(s.Frenzy);
        if (!Check(s.Slot >= 0 && s.Views.AnimatorOf(s.Slot) != null && s.Index >= 0, "no Shaman model / Frenzy")) s.Slot = -1;
        return s;
    }

    private void Cast(Stage s) =>
        s.Sim.Enqueue(Command.UseAbility(0, new EntityHandle(s.Slot, s.U.Generation[s.Slot]), s.Index, s.U.Position[s.Slot]));

    private async Task End(Stage s)
    {
        GetTree().Paused = false;
        RemoveChild(s.Match);
        s.Match.QueueFree();
        await Frame();
    }

    // Frames until the Shaman is casting (at most `limit`); the clip positions of every frame it is seen casting after that.
    private async Task<List<float>> RecordCast(Stage s, int limit = 600)
    {
        var positions = new List<float>();
        for (int f = 0; f < limit && s.U.State[s.Slot] != UnitState.Casting; f++) await Frame();
        for (int f = 0; f < limit && s.U.State[s.Slot] == UnitState.Casting; f++)
        {
            if (s.Anim.CurrentAnimation == "attack_cast") positions.Add((float)s.Anim.CurrentAnimationPosition);
            await Frame();
        }
        return positions;
    }

    // (A) game speed: one frame's worth of clip at `speed` is length / (seen ticks * frames per tick).
    private async Task SpeedRow(GameData data, double speed, string label)
    {
        Stage s = await Begin(data, speed, false);
        try
        {
            if (s.Slot < 0) return;
            Cast(s);
            List<float> p = await RecordCast(s);
            await Frame();
            string after = s.Anim.CurrentAnimation;
            double framesPerTick = 3.0 / speed;
            float step = (float)(s.Length / ((s.CastTicks - 1) * framesPerTick));
            int back = 0;
            for (int k = 1; k < p.Count; k++) if (p[k] < p[k - 1] - 1e-5f) back++;
            float first = p.Count > 0 ? p[0] : -1f, last = p.Count > 0 ? p[^1] : -1f;
            GD.Print($"(A) {label} cast ({s.CastTicks} ticks) at speed {speed}: {p.Count} frames, first {first:F3} s, last {last:F3} s of {s.Length:F3}, {back} steps back, frame step {step:F3} s; then '{after}'");
            Check(p.Count > 0 && first <= step + 1e-3f, $"(A) {label} x{speed}: the clip starts at {first:F3} s, want 0 within one frame ({step:F3})");
            Check(back == 0, $"(A) {label} x{speed}: the clip stepped back {back} times");
            Check(last >= s.Length - step - 2e-3f && last < s.Length, $"(A) {label} x{speed}: last cast frame at {last:F3} s, want within one frame of {s.Length:F3}");
            Check(after == "idle", $"(A) {label} x{speed}: after the cast plays '{after}', want idle");
        }
        finally { await End(s); }
    }

    // (B) paused mid-cast: the clip holds still; unpaused it carries on and ends at its end.
    private async Task PauseRow(GameData data)
    {
        Stage s = await Begin(data, 1.0, false);
        try
        {
            if (s.Slot < 0) return;
            Cast(s);
            for (int f = 0; f < 200 && s.U.State[s.Slot] != UnitState.Casting; f++) await Frame();
            for (int f = 0; f < 45; f++) await Frame(); // about 0.75 s into the 2 s cast
            float before = (float)s.Anim.CurrentAnimationPosition;
            long tick = s.Sim.TickNumber;
            GetTree().Paused = true;
            float lo = float.MaxValue, hi = float.MinValue;
            for (int f = 0; f < 30; f++)
            {
                await Frame();
                float at = (float)s.Anim.CurrentAnimationPosition;
                lo = Math.Min(lo, at);
                hi = Math.Max(hi, at);
            }
            bool ticked = s.Sim.TickNumber != tick;
            GetTree().Paused = false;
            List<float> p = await RecordCast(s);
            float last = p.Count > 0 ? p[^1] : -1f;
            int back = 0;
            for (int k = 1; k < p.Count; k++) if (p[k] < p[k - 1] - 1e-5f) back++;
            GD.Print($"(B) paused at {before:F3} s for 30 frames: clip {lo:F3}-{hi:F3} s, sim ticked {ticked}; unpaused: {p.Count} frames from {(p.Count > 0 ? p[0] : -1f):F3} to {last:F3} s of {s.Length:F3}, {back} steps back");
            Check(!ticked && Math.Abs(hi - before) < 1e-4f && Math.Abs(lo - before) < 1e-4f, $"(B) paused: the clip moved {lo:F3}-{hi:F3} s from {before:F3} (sim ticked {ticked})");
            Check(p.Count > 0 && p[0] >= before - 1e-4f && back == 0, $"(B) unpaused: the clip went back from {before:F3} to {(p.Count > 0 ? p[0] : -1f):F3} or stepped back {back} times");
            Check(last >= s.Length - 0.05f && last < s.Length, $"(B) unpaused: the cast ends at {last:F3} s of the clip, want its end");
        }
        finally { await End(s); }
    }

    // (C) a Stop mid-cast: idle, and the idle clip runs (the cast no longer holds it); (E) a recast starts the clip at 0.
    private async Task StopRow(GameData data)
    {
        Stage s = await Begin(data, 1.0, false);
        try
        {
            if (s.Slot < 0) return;
            Cast(s);
            for (int f = 0; f < 200 && s.U.State[s.Slot] != UnitState.Casting; f++) await Frame();
            for (int f = 0; f < 45; f++) await Frame();
            float mid = (float)s.Anim.CurrentAnimationPosition;
            s.Sim.Enqueue(Command.Stop(0, new EntityHandle(s.Slot, s.U.Generation[s.Slot])));
            for (int f = 0; f < 6; f++) await Frame();
            string playing = s.Anim.CurrentAnimation;
            float a = (float)s.Anim.CurrentAnimationPosition;
            for (int f = 0; f < 10; f++) await Frame();
            float b = (float)s.Anim.CurrentAnimationPosition;
            GD.Print($"(C) Stop at {mid:F3} s of the cast clip: state {s.U.State[s.Slot]}, plays '{playing}', its clip {a:F3} -> {b:F3} s over 10 frames");
            Check(s.U.State[s.Slot] == UnitState.Idle && playing == "idle", $"(C) after a Stop: state {s.U.State[s.Slot]}, plays '{playing}', want idle");
            Check(Math.Abs(b - a) > 1e-3f, $"(C) after a Stop the idle clip is held at {a:F3} s");
            // (E) recast (the Stop cancelled the cast, no cooldown): starts at the clip's first frame again.
            Cast(s);
            List<float> p = await RecordCast(s);
            float step = (float)(s.Length / ((s.CastTicks - 1) * 3.0));
            GD.Print($"(E) recast: {p.Count} frames, first {(p.Count > 0 ? p[0] : -1f):F3} s, last {(p.Count > 0 ? p[^1] : -1f):F3} s");
            Check(p.Count > 0 && p[0] <= step + 1e-3f, $"(E) recast: the clip starts at {(p.Count > 0 ? p[0] : -1f):F3} s, want 0 within one frame");
            Check(p.Count > 0 && p[^1] >= s.Length - step - 2e-3f, $"(E) recast: the clip ends at {(p.Count > 0 ? p[^1] : -1f):F3} s, want its end");
        }
        finally { await End(s); }
    }

    // (D) killed mid-cast: hidden, no error; a new Shaman in the freed slot plays idle and its clip runs.
    private async Task DeathRow(GameData data)
    {
        Stage s = await Begin(data, 1.0, false);
        try
        {
            if (s.Slot < 0) return;
            Cast(s);
            for (int f = 0; f < 200 && s.U.State[s.Slot] != UnitState.Casting; f++) await Frame();
            for (int f = 0; f < 45; f++) await Frame(); // about 0.75 s into the 2 s cast
            int slot = s.Slot;
            int gen = s.U.Generation[slot];
            float at = (float)s.Anim.CurrentAnimationPosition;
            bool casting = s.U.State[slot] == UnitState.Casting;
            // Test seam between frames: the slot freed as CombatSystem.HitUnit frees a unit it kills (the view sees only that).
            s.U.Free(new EntityHandle(slot, gen));
            int f2 = 0;
            for (int f = 0; f < 4; f++, f2++) await Frame();
            bool dead = !s.U.Alive[slot] || s.U.Generation[slot] != gen;
            bool hidden = s.Views.ViewOf(slot)?.Visible == false;
            GD.Print($"(D) the Shaman killed while casting {casting} at {at:F3} s of its clip: dead {dead}, its view hidden {hidden} {f2} frames later");
            Check(casting && dead && hidden, $"(D) killed mid-cast (casting {casting}): dead {dead}, hidden {hidden}");
            // The enemy goes; a new Shaman of player 0 takes a free slot (most likely the dead one's).
            for (int i = 0; i < s.U.Capacity; i++) if (s.U.Alive[i] && s.U.Owner[i] == 1) s.U.Free(new EntityHandle(i, s.U.Generation[i]));
            s.Sim.Enqueue(Command.SpawnUnit(0, s.Shaman, s.U.Position[0] + new NVector2(0f, 4f)));
            for (int f = 0; f < 30; f++) await Frame();
            int reborn = -1;
            for (int i = 0; i < s.U.Capacity; i++) if (s.U.Alive[i] && s.U.TypeId[i] == s.Shaman) reborn = i;
            if (!Check(reborn >= 0, "(D) no new Shaman")) return;
            AnimationPlayer anim = s.Views.AnimatorOf(reborn)!;
            float a = (float)anim.CurrentAnimationPosition;
            for (int f = 0; f < 10; f++) await Frame();
            float b = (float)anim.CurrentAnimationPosition;
            GD.Print($"(D) new Shaman in slot {reborn} (the dead one's: {reborn == slot}), state {s.U.State[reborn]}, plays '{anim.CurrentAnimation}', clip {a:F3} -> {b:F3} s");
            Check(anim.CurrentAnimation == "idle" && Math.Abs(b - a) > 1e-3f, $"(D) the new Shaman plays '{anim.CurrentAnimation}', clip {a:F3} -> {b:F3}: want idle, running");
        }
        finally { await End(s); }
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
