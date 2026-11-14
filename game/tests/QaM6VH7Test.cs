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

/// <summary>QA for M6-VH7: the crowd animation schedule (<see cref="CrowdAnimator"/>) under pause, deaths, slot reuse, clip changes and odd deltas.</summary>
/// <remarks>
/// Headless. Run: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6VH7Test.tscn</c>; prints "QA M6-VH7 TEST PASS".
/// Rows: (1) every library clip is longer than the stagger period at 20 FPS (a far figure samples it at least a few times
/// per loop); (2) clip speed: over 60 frames every shown player's clip advanced by the frames' total time, less only the
/// time still waiting; (3) a far figure switched to attack plays the attack clip within the stagger period; (4) a figure
/// hidden (dead) for half a second and shown again does not jump by the hidden time; (5) a slot reused by another model
/// type: the new player is advanced within the period and by no more than the period's time; (6) the schedule and Sync
/// leave the sim's state hash unchanged; (7) a zero, a negative-free huge (5 s) delta and a camera inside the crowd do not
/// throw and keep every player finite; (8) a paused tree freezes every figure and un-pausing after 0.5 s does not jump.
/// </remarks>
public partial class QaM6VH7Test : Node
{
    private const string Tag = "QA M6-VH7 TEST";
    private const double Dt = 1.0 / 60.0;
    private readonly List<string> _failures = new();

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            await Run();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        GetTree().Paused = false;
        UnitViews.UseModels = true;
        foreach (string f in _failures) GD.Print($"{Tag} FAIL: {f}");
        if (_failures.Count == 0) GD.Print($"{Tag} PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private async Task Run()
    {
        DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
        if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
        GameData data = loaded.Data!;
        UnitViews.UseModels = true;
        await ScheduleRows(data);
        ClipRow(); // after a Bind: the library loads there
        await PauseRow(data);
    }

    // (1) Far figures advance once in StaggerFrames frames; at 20 FPS that is a sample every 0.15 s.
    private void ClipRow()
    {
        double period = CrowdAnimator.StaggerFrames / 20.0;
        foreach (UnitModels.Clip c in Enum.GetValues<UnitModels.Clip>())
        {
            float len = UnitModels.ClipLength(c);
            GD.Print($"clip {UnitModels.ClipNames[(int)c]}: {len:0.000} s ({len / period:0.0} far samples per loop at 20 FPS)");
            Check(UnitModels.HasClip(c) && len > 2 * period, $"(1) clip {UnitModels.ClipNames[(int)c]} is {len} s: a far figure samples it fewer than twice per loop at 20 FPS");
        }
    }

    private (Simulation sim, UnitViews views, int[] types) Crowd(GameData data, int perSide)
    {
        var sim = new Simulation(new SimConfig(7, 2, 2 * perSide, 4096) { Data = data });
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
        return (sim, views, types);
    }

    private static double Pos(UnitViews v, int s) => v.AnimatorOf(s)?.CurrentAnimationPosition ?? double.NaN;

    private static double Len(UnitViews v, int s) => v.AnimatorOf(s)?.CurrentAnimationLength ?? 0;

    // Forward distance from a to b on a looping clip of length len.
    private static double Forward(double a, double b, double len) => len > 0 ? ((b - a) % len + len) % len : b - a;

    private async Task ScheduleRows(GameData data)
    {
        const int perSide = 200;
        var (sim, views, types) = Crowd(data, perSide);
        World w = sim.World;
        UnitStore u = w.Units;
        int n = 2 * perSide;
        // The eye over the west block's first unit: west near, east far.
        int anchor = -1;
        for (int i = 0; i < u.Capacity && anchor < 0; i++) if (u.Alive[i] && u.Owner[i] == 0) anchor = i;
        Vector3 eye = views.ViewOf(anchor)!.GlobalPosition + new Vector3(0f, 30f, 10f);
        int far = -1;
        float farD = -1f;
        for (int i = 0; i < n; i++)
            if (u.Alive[i] && views.ViewOf(i)!.GlobalPosition.DistanceSquaredTo(eye) is float d && d > farD) { farD = d; far = i; }
        CrowdAnimator crowd = views.Crowd;
        ulong hash0 = sim.StateHash();

        // (2) Clip speed. Positions before, 60 frames, positions after.
        views.AdvanceAnimations(Dt, eye);
        var before = new double[n];
        var waitedBefore = new double[n];
        for (int i = 0; i < n; i++)
        {
            before[i] = Pos(views, i);
            waitedBefore[i] = (crowd.Frame - 1 - crowd.LastAdvanced(i)) * Dt;
        }
        for (int f = 0; f < 60; f++) views.AdvanceAnimations(Dt, eye);
        int slow = 0;
        double worst = 0;
        for (int i = 0; i < n; i++)
        {
            if (views.AnimatorOf(i) == null) continue;
            long last = crowd.LastAdvanced(i);
            double waiting = (crowd.Frame - 1 - last) * Dt;
            double want = 60 * Dt + waitedBefore[i] - waiting;
            double got = Forward(before[i], Pos(views, i), Len(views, i));
            double err = Math.Abs(got - Forward(0, want, Len(views, i)));
            worst = Math.Max(worst, err);
            if (err > 1e-3) slow++;
        }
        GD.Print($"(2) clip speed over 60 frames: {slow} of {n} players off by more than 1 ms, worst {worst * 1000:0.000} ms");
        Check(slow == 0, $"(2) {slow} players' clips did not advance by the frames' time (worst {worst * 1000:0.000} ms)");

        // (3) A far figure set to attack plays attack within the stagger period, and its clip moves.
        UnitState oldState = u.State[far];
        u.State[far] = UnitState.Attacking;
        views.Sync(w, 1f);
        double p0 = Pos(views, far);
        string clip0 = views.AnimatorOf(far)!.CurrentAnimation;
        bool moved = false;
        for (int f = 0; f < CrowdAnimator.StaggerFrames; f++)
        {
            views.AdvanceAnimations(Dt, eye);
            if (Pos(views, far) != p0) moved = true;
        }
        Check(!crowd.WasFullRate(far), "(3) the farthest figure is on the full-rate list");
        Check(clip0 == "attack" && views.AnimatorOf(far)!.CurrentAnimation == "attack" && moved,
            $"(3) far attacker: clip {clip0} -> {views.AnimatorOf(far)!.CurrentAnimation}, moved {moved}");
        u.State[far] = oldState;
        views.Sync(w, 1f);

        // (4) Hidden for half a second (dead slot), then shown: no jump by the hidden time.
        // Hide it on a frame where it would not have advanced.
        while ((crowd.Frame + far) % CrowdAnimator.StaggerFrames == 0) views.AdvanceAnimations(Dt, eye);
        u.Alive[far] = false;
        views.Sync(w, 1f);
        long hiddenAt = crowd.LastAdvanced(far);
        double ph = Pos(views, far);
        for (int f = 0; f < 30; f++) views.AdvanceAnimations(Dt, eye);
        Check(crowd.LastAdvanced(far) == hiddenAt && Pos(views, far) == ph, "(4) a hidden figure was advanced");
        u.Alive[far] = true;
        views.Sync(w, 1f);
        for (int f = 0; f < CrowdAnimator.StaggerFrames; f++) views.AdvanceAnimations(Dt, eye);
        double jump = Forward(ph, Pos(views, far), Len(views, far));
        GD.Print($"(4) shown again after 0.5 s hidden: clip moved {jump * 1000:0.0} ms over {CrowdAnimator.StaggerFrames} frames");
        Check(crowd.LastAdvanced(far) > hiddenAt && jump <= CrowdAnimator.StaggerFrames * Dt + 1e-4,
            $"(4) re-shown figure moved {jump} s in {CrowdAnimator.StaggerFrames} frames (last advanced {crowd.LastAdvanced(far)})");

        // (5) Slot reused by the other model type: a new player, advanced within the period, by at most the period.
        AnimationPlayer? oldPlayer = views.AnimatorOf(far);
        u.TypeId[far] = u.TypeId[far] == types[0] ? types[1] : types[0];
        views.Sync(w, 1f);
        AnimationPlayer? fresh = views.AnimatorOf(far);
        double q0 = Pos(views, far);
        long before5 = crowd.LastAdvanced(far);
        for (int f = 0; f < CrowdAnimator.StaggerFrames; f++) views.AdvanceAnimations(Dt, eye);
        double q = Forward(q0, Pos(views, far), Len(views, far));
        Check(fresh != null && fresh != oldPlayer, "(5) the reused slot kept its old player");
        Check(crowd.LastAdvanced(far) > before5 && q <= CrowdAnimator.StaggerFrames * Dt + 1e-4,
            $"(5) the reused slot's new player moved {q} s in {CrowdAnimator.StaggerFrames} frames, last advanced {crowd.LastAdvanced(far)}");
        Check(views.ModelOf(far)?.Name == "Model", $"(5) the reused slot's child is '{views.ModelOf(far)?.Name}'");

        // (7) Odd deltas and an eye inside the crowd: no throw, finite positions.
        views.AdvanceAnimations(0, eye);
        views.AdvanceAnimations(5.0, eye);
        views.AdvanceAnimations(Dt, views.ViewOf(anchor)!.GlobalPosition);
        int nonFinite = 0;
        for (int i = 0; i < n; i++) if (views.AnimatorOf(i) != null && !double.IsFinite(Pos(views, i))) nonFinite++;
        Check(nonFinite == 0, $"(7) {nonFinite} players at a non-finite clip position after odd deltas");

        // (6) View-only: the sim's state hash is what it was before all of the above, apart from the test's own writes
        // (restored here), and is unchanged by 100 more frames of Sync + schedule.
        u.TypeId[far] = u.TypeId[far] == types[0] ? types[1] : types[0];
        ulong hash1 = sim.StateHash();
        for (int f = 0; f < 100; f++)
        {
            views.Sync(w, (f % 10) / 10f, (float)Dt);
            views.AdvanceAnimations(Dt, eye);
        }
        Check(sim.StateHash() == hash1 && hash1 == hash0, $"(6) the state hash changed under the view: {hash0:X} / {hash1:X} / {sim.StateHash():X}");
        views.QueueFree();
        await Frames(1);
    }

    // (8) A paused tree: UnitViews' _Process (which runs the schedule) stops; un-pausing does not jump by the paused time.
    private async Task PauseRow(GameData data)
    {
        const int perSide = 100;
        var holder = new Node3D { Name = "Holder", ProcessMode = ProcessModeEnum.Pausable };
        AddChild(holder);
        var (sim, views, _) = Crowd(data, perSide);
        RemoveChild(views);
        holder.AddChild(views);
        var cam = new Camera3D { Name = "Eye" };
        holder.AddChild(cam);
        cam.GlobalPosition = views.ViewOf(0)!.GlobalPosition + new Vector3(0f, 30f, 10f);
        cam.Current = true;
        await Frames(10);
        int n = 2 * perSide;
        Check(views.Crowd.Frame >= 10 && views.Crowd.EligibleLastFrame == n, $"(8) the schedule did not run from _Process: frame {views.Crowd.Frame}, eligible {views.Crowd.EligibleLastFrame}");
        GetTree().Paused = true;
        await Frames(1);
        long frame = views.Crowd.Frame;
        var at = new double[n];
        for (int i = 0; i < n; i++) at[i] = Pos(views, i);
        await ToSignal(GetTree().CreateTimer(0.5, true), SceneTreeTimer.SignalName.Timeout);
        int moved = 0;
        for (int i = 0; i < n; i++) if (Pos(views, i) != at[i]) moved++;
        Check(views.Crowd.Frame == frame && moved == 0, $"(8) paused: schedule frame {frame} -> {views.Crowd.Frame}, {moved} figures moved");
        ulong t0 = Time.GetTicksUsec();
        GetTree().Paused = false;
        await Frames(CrowdAnimator.StaggerFrames + 1);
        double elapsed = (Time.GetTicksUsec() - t0) / 1e6;
        double maxJump = 0;
        for (int i = 0; i < n; i++)
            if (views.AnimatorOf(i) != null) maxJump = Math.Max(maxJump, Forward(at[i], Pos(views, i), Len(views, i)));
        GD.Print($"(8) un-paused after 0.5 s: largest clip move {maxJump * 1000:0.0} ms over {elapsed * 1000:0.0} ms of frames");
        // A jump would add the 0.5 s paused; the frames since un-pausing (slow under load) are allowed in full.
        Check(maxJump < elapsed + 0.25, $"(8) un-pausing jumped a clip by {maxJump} s (paused 0.5 s, {elapsed} s of frames since)");
        _ = sim;
        holder.QueueFree();
        await Frames(1);
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
