using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Game.Tests;

/// <summary>
/// QA M6-V12 (2026-10-17-2344): Space before any alert moves nothing; the world-sound cap under a 200 v 200 brawl at 8x on
/// screen (no five world plays inside any 100 ms, every one recorded, not only the last 256); UI sounds tried through the
/// brawl all start, and (BUG-0951) are not cut short by later plays taking their pool player before the clip ends.
/// </summary>
/// <remarks>
/// Headless. Run: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6V12Test.tscn</c>; prints "QA M6-V12 TEST PASS"
/// and exits 0, or prints each failure and exits 1.
/// </remarks>
public partial class QaM6V12Test : Node
{
    private readonly List<string> _failures = new();
    private GameData _data = null!;

    public override async void _Ready()
    {
        try
        {
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            await SpaceBeforeAnyAlert();
            await Brawl();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures) GD.Print($"QA M6-V12 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M6-V12 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private Match NewMatch(params string[] args)
    {
        var m = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(m);
        m.Start(_data, LaunchOptions.Parse(args));
        m.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
        return m;
    }

    private async Task EndMatch(Match m)
    {
        RemoveChild(m);
        m.QueueFree();
        await Frame();
    }

    // Space with no alert yet: JumpToLast refuses, the camera stays, nothing counts as a jump.
    private async Task SpaceBeforeAnyAlert()
    {
        Match m = NewMatch("--units", "0", "--seed", "4", "--mute");
        var alerts = m.GetNode<AlertHud>("Alerts");
        var camera = m.GetNode<RtsCamera>("RtsCamera");
        await Frame();
        camera.SetFocus(40f, 52f);
        System.Numerics.Vector2 before = camera.Focus;
        GetViewport().PushInput(new InputEventKey { Keycode = Key.Space, PhysicalKeycode = Key.Space, Pressed = true });
        GetViewport().PushInput(new InputEventKey { Keycode = Key.Space, PhysicalKeycode = Key.Space, Pressed = false });
        await Frame();
        await Frame();
        Check(!alerts.Alerts.HasLast, "an alert was raised in an idle match");
        Check(alerts.Jumps == 0 && !alerts.JumpToLast() && camera.Focus == before,
            $"Space with no alert: {alerts.Jumps} jumps, focus {before} -> {camera.Focus}");
        GD.Print($"space before any alert: focus {camera.Focus}, jumps {alerts.Jumps}");
        await EndMatch(m);
    }

    // 200 v 200 at 8x, attack-moved into the middle with the camera on it.
    private async Task Brawl()
    {
        Match m = NewMatch("--units", "200", "--seed", "3", "--speed", "8", "--zoom", "60", "--mute");
        var sfx = m.GetNode<Sfx>("Sfx");
        var camera = m.GetNode<RtsCamera>("RtsCamera");
        var cues = m.GetNode<SoundCues>("SoundCues");
        Simulation sim = m.GetNode<SimRunner>("SimRunner").Simulation!;
        while (sim.TickNumber < 3) await Frame();
        UnitStore u = sim.World.Units;
        System.Numerics.Vector2 c0 = default, c1 = default;
        int n0 = 0, n1 = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            if (u.Owner[i] == 0) { c0 += u.Position[i]; n0++; }
            else { c1 += u.Position[i]; n1++; }
        }
        Check(n0 >= 200 && n1 >= 200, $"brawl: {n0} v {n1} units, want 200 v 200");
        System.Numerics.Vector2 mid = (c0 / Math.Max(1, n0) + c1 / Math.Max(1, n1)) / 2;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i]) sim.Enqueue(Command.AttackMove(u.Owner[i], new EntityHandle(i, u.Generation[i]), mid));
        camera.SetFocus(mid.X, mid.Y);

        var starts = new List<ulong>();
        long seenWorld = sfx.WorldPlays;
        SfxEvent[] ui = { SfxEvent.Alert, SfxEvent.Ready, SfxEvent.Command, SfxEvent.BuildingDone };
        int uiTried = 0, uiStarted = 0, uiMerged = 0, uiCut = 0, uiChecked = 0;
        var pending = new List<(SfxEvent E, int Player, long StartsAt, ulong Usec, double Seconds)>();
        var cutBy = new Dictionary<SfxEvent, int>();
        ulong lastUi = 0, start = Time.GetTicksUsec();
        int k = 0;
        while (Time.GetTicksUsec() - start < 30_000_000UL && sim.World.Losses[0] + sim.World.Losses[1] < 300)
        {
            await Frame();
            ulong now = Time.GetTicksUsec();
            // Every world play's start, read from the history after each frame (fewer than 256 start in one frame).
            long fresh = sfx.WorldPlays - seenWorld;
            seenWorld = sfx.WorldPlays;
            for (long j = fresh; j > 0; j--) starts.Add(sfx.WorldPlayUsec(sfx.WorldPlayCount - (int)j));
            // A UI play is cut once a later play starts on its pool player before its clip's end. (Was: PoolSize later plays of
            // any kind, the strict round robin's reach; since the fix the UI has players of its own, so the row reads the
            // player the play took, Sfx.LastPlayer, and that player's start count, Sfx.PlayerStarts.)
            for (int p = pending.Count - 1; p >= 0; p--)
            {
                var e = pending[p];
                bool ended = now - e.Usec >= (ulong)(e.Seconds * 1e6);
                if (sfx.PlayerStarts(e.Player) != e.StartsAt && !ended)
                {
                    uiCut++;
                    cutBy[e.E] = cutBy.GetValueOrDefault(e.E) + 1;
                    pending.RemoveAt(p);
                    uiChecked++;
                }
                else if (ended)
                {
                    pending.RemoveAt(p);
                    uiChecked++;
                }
            }
            if (now - lastUi >= 400_000)
            {
                lastUi = now;
                SfxEvent e = ui[k++ % ui.Length];
                uiTried++;
                // The match's own UI cues play too (the HUD's Alert: at 8x a raid re-alerts every 10 s of game time, 1.25 s of
                // wall clock, BUG-0952). A try in the same frame or inside the per-event 50 ms gap of such a play merges into it
                // (the rate limit's "a burst is one sound"): that is not the cap dropping a UI sound.
                long frameNow = (long)Engine.GetProcessFrames();
                bool merges = sfx.LastPlayedFrame(e) >= 0 && (sfx.LastPlayedFrame(e) == frameNow || Time.GetTicksUsec() - sfx.LastPlayedUsec(e) < (ulong)(Sfx.MinGapMs * 1000));
                if (!sfx.Play(e))
                {
                    if (merges) uiMerged++;
                }
                else
                {
                    uiStarted++;
                    int player = sfx.LastPlayer(e);
                    pending.Add((e, player, sfx.PlayerStarts(player), Time.GetTicksUsec(), sfx.LastStream(e)!.GetLength()));
                }
            }
        }
        int windows = 0;
        ulong tightest = ulong.MaxValue;
        for (int j = 0; j + Sfx.WorldCap < starts.Count; j++)
        {
            ulong span = starts[j + Sfx.WorldCap] - starts[j];
            tightest = Math.Min(tightest, span);
            if (span < 100_000) windows++;
        }
        double seconds = (Time.GetTicksUsec() - start) / 1e6;
        GD.Print($"brawl 200 v 200 at 8x: {sim.World.Losses[0] + sim.World.Losses[1]} deaths by tick {sim.TickNumber} in {seconds:F1} s; world plays {starts.Count} " +
            $"({starts.Count / Math.Max(0.1, seconds):F1} / s), capped {sfx.WorldCapped}; cued MeleeHit {cues.Cued(SfxEvent.MeleeHit)}, Shot {cues.Cued(SfxEvent.Shot)}, " +
            $"UnitDeath {cues.Cued(SfxEvent.UnitDeath)}; 5 plays inside 100 ms: {windows} (tightest 5-span {tightest / 1000.0:F1} ms); " +
            $"UI tried {uiTried}, started {uiStarted}, merged into the HUD's own play of the event {uiMerged}, cut short {uiCut} of {uiChecked} [{string.Join(", ", cutBy)}]");
        Check(starts.Count >= 40, $"brawl: only {starts.Count} world plays");
        Check(windows == 0, $"brawl: {windows} windows of 5 world plays inside 100 ms");
        Check(uiTried >= 10 && uiStarted + uiMerged == uiTried && uiMerged <= uiTried / 4, $"brawl: UI sounds started {uiStarted} of {uiTried} ({uiMerged} merged into the HUD's own play)");
        Check(uiCut == 0, $"BUG-0951: {uiCut} of {uiChecked} UI sounds cut short by later plays reusing their pool player ({string.Join(", ", cutBy)})");

        // The per-frame view work the change added, on the same brawl after warm-up: the cues' frame and the alerts' step.
        var alerts = m.GetNode<AlertHud>("Alerts");
        long cueBytes = 0, alertBytes = 0;
        int probed = 0;
        for (int f = 0; f < 200 && sim.World.Units.Count > 0; f++)
        {
            await Frame();
            long before = GC.GetAllocatedBytesForCurrentThread();
            cues._Process(1.0 / 60.0);
            long between = GC.GetAllocatedBytesForCurrentThread();
            alerts.Step(sim.World, 1f / 60f);
            long after = GC.GetAllocatedBytesForCurrentThread();
            if (f >= 20)
            {
                cueBytes += between - before;
                alertBytes += after - between;
                probed++;
            }
        }
        GD.Print($"per-frame bytes over {probed} brawl frames: SoundCues._Process {cueBytes}, AlertHud.Step {alertBytes}");
        Check(probed >= 100 && cueBytes == 0 && alertBytes == 0, $"per-frame view work allocated: SoundCues {cueBytes} B, AlertHud {alertBytes} B over {probed} frames");
        await EndMatch(m);
    }


    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
